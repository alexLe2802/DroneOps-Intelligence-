from __future__ import annotations

import hashlib
import json
import os
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

ISOLATED_NAMES = {"local", "development", "dev", "test", "testing", "staging", "recovery", "rehearsal", "isolated"}
PRODUCTION_NAMES = {"prod", "production", "live"}


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_object_file(root: Path, object_path: str) -> Path:
    relative = PurePosixPath(object_path)
    if relative.is_absolute() or ".." in relative.parts:
        raise RuntimeError(f"Unsafe storage object path: {object_path}")
    target = root.joinpath(*relative.parts).resolve()
    if root.resolve() not in target.parents:
        raise RuntimeError(f"Storage object escapes recovery root: {object_path}")
    return target


def atomic_json(path: Path, value: object) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def require_env(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        raise RuntimeError(f"Required environment variable {name} is missing.")
    return value


def require_isolated_target(name: str, confirmation: str) -> str:
    normalized = name.strip().lower()
    if normalized in PRODUCTION_NAMES or normalized not in ISOLATED_NAMES:
        raise RuntimeError("Restore target must be an explicitly isolated non-production environment.")
    if confirmation != f"RESTORE-{normalized.upper()}":
        raise RuntimeError(f"Confirmation must be RESTORE-{normalized.upper()}.")
    return normalized


def postgres_environment(url: str) -> tuple[dict[str, str], str]:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme not in {"postgres", "postgresql"} or not parsed.hostname or not parsed.path.strip("/"):
        raise RuntimeError("Database URL must be a PostgreSQL URI.")
    env = os.environ.copy()
    env.update({
        "PGHOST": parsed.hostname,
        "PGPORT": str(parsed.port or 5432),
        "PGDATABASE": parsed.path.strip("/"),
        "PGUSER": urllib.parse.unquote(parsed.username or "postgres"),
        "PGPASSWORD": urllib.parse.unquote(parsed.password or ""),
    })
    query = urllib.parse.parse_qs(parsed.query)
    if "sslmode" in query:
        env["PGSSLMODE"] = query["sslmode"][0]
    safe = f"{parsed.hostname}:{parsed.port or 5432}/{parsed.path.strip('/')}"
    return env, safe


def run_postgres(tool: str, args: list[str], database_url: str, capture: bool = False) -> str:
    env, _ = postgres_environment(database_url)
    completed = subprocess.run([tool, *args], env=env, text=True, capture_output=capture, check=False)
    if completed.returncode:
        detail = (completed.stderr or completed.stdout or "no diagnostic").strip()
        raise RuntimeError(f"{tool} failed ({completed.returncode}): {detail}")
    return completed.stdout.strip() if capture else ""


COVERAGE_SQL = r'''SELECT json_build_object(
 'accountLicenseMetadata', (SELECT count(*) FROM "User") + (SELECT count(*) FROM "Pilot") + (SELECT count(*) FROM "PilotRegistration") + (SELECT count(*) FROM "LicenseImage") + (SELECT count(*) FROM "PilotUpdateRequest"),
 'missionReview', (SELECT count(*) FROM "Mission") + (SELECT count(*) FROM "MissionApproval") + (SELECT count(*) FROM "MissionUpdateRequest"),
 'telemetry', (SELECT count(*) FROM "TelemetryRecord"),
 'incidents', (SELECT count(*) FROM "Incident") + (SELECT count(*) FROM "IncidentTransition") + (SELECT count(*) FROM "IncidentAlertDelivery"),
 'privateMediaReferences', (SELECT count(*) FROM "LicenseImage" WHERE "ImageUrl" IS NOT NULL) + (SELECT count(*) FROM "User" WHERE "AvatarPath" IS NOT NULL) + (SELECT count(*) FROM "UAV" WHERE "ImageStoragePath" IS NOT NULL)
)::text;'''


def database_coverage(database_url: str, psql: str = "psql") -> dict[str, int]:
    output = run_postgres(psql, ["--no-psqlrc", "--set", "ON_ERROR_STOP=1", "--tuples-only", "--no-align", "--command", COVERAGE_SQL], database_url, True)
    return {key: int(value) for key, value in json.loads(output).items()}


class SupabaseStorage:
    def __init__(self, url: str, service_key: str, bucket: str = "user-media"):
        self.base = url.rstrip("/") + "/storage/v1"
        self.service_key = service_key
        self.bucket = bucket

    def _request(self, method: str, endpoint: str, body: bytes | None = None, content_type: str = "application/json") -> bytes:
        request = urllib.request.Request(self.base + endpoint, data=body, method=method)
        request.add_header("Authorization", "Bearer " + self.service_key)
        request.add_header("apikey", self.service_key)
        if body is not None:
            request.add_header("Content-Type", content_type)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", "replace")[:500]
            raise RuntimeError(f"Storage {method} {endpoint} failed ({error.code}): {detail}") from error

    def list_objects(self, prefix: str = "") -> list[dict]:
        found: list[dict] = []
        offset = 0
        while True:
            payload = json.dumps({"prefix": prefix, "limit": 1000, "offset": offset, "sortBy": {"column": "name", "order": "asc"}}).encode()
            page = json.loads(self._request("POST", f"/object/list/{self.bucket}", payload))
            for item in page:
                name = item.get("name", "")
                object_path = f"{prefix}/{name}".strip("/")
                if item.get("id") is None:
                    found.extend(self.list_objects(object_path))
                else:
                    found.append({"path": object_path, "size": int((item.get("metadata") or {}).get("size") or 0),
                                  "contentType": (item.get("metadata") or {}).get("mimetype") or "application/octet-stream"})
            if len(page) < 1000:
                break
            offset += len(page)
        return found

    def download(self, object_path: str) -> bytes:
        quoted = urllib.parse.quote(object_path, safe="/")
        return self._request("GET", f"/object/authenticated/{self.bucket}/{quoted}")

    def upload(self, object_path: str, data: bytes, content_type: str) -> None:
        quoted = urllib.parse.quote(object_path, safe="/")
        self._request("POST", f"/object/{self.bucket}/{quoted}", data, content_type)

    def delete(self, paths: list[str]) -> None:
        if paths:
            self._request("DELETE", f"/object/{self.bucket}", json.dumps({"prefixes": paths}).encode())


def verify_manifest(backup: Path) -> dict:
    manifest_path = backup / "manifest.json"
    if not manifest_path.is_file():
        raise RuntimeError("Backup manifest.json is missing.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    for item in manifest.get("files", []):
        file_path = backup / item["path"]
        if not file_path.is_file() or sha256(file_path) != item["sha256"]:
            raise RuntimeError(f"Backup checksum failed for {item['path']}.")
    return manifest
