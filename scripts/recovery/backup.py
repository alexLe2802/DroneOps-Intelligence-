from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from recovery_lib import SupabaseStorage, atomic_json, database_coverage, iso, postgres_environment, require_env, safe_object_file, sha256, utc_now


def safe_media_path(root: Path, object_path: str) -> Path:
    return safe_object_file(root, object_path)


def main() -> int:
    parser = argparse.ArgumentParser(description="Create an atomic PostgreSQL + private media DroneOps backup.")
    parser.add_argument("--output-root", required=True, type=Path)
    parser.add_argument("--target-environment", required=True)
    parser.add_argument("--pg-dump", default="pg_dump")
    parser.add_argument("--psql", default="psql")
    args = parser.parse_args()
    started = utc_now()
    database_url = require_env("DRONEOPS_BACKUP_DATABASE_URL")
    storage_url = require_env("DRONEOPS_BACKUP_STORAGE_URL")
    storage_key = require_env("DRONEOPS_BACKUP_STORAGE_SERVICE_KEY")
    output_root = args.output_root.resolve()
    output_root.mkdir(parents=True, exist_ok=True)
    name = started.strftime("droneops-%Y%m%dT%H%M%SZ")
    final = output_root / name
    if final.exists():
        raise RuntimeError(f"Backup already exists: {final}")
    temporary = Path(tempfile.mkdtemp(prefix=name + ".tmp-", dir=output_root))
    try:
        database_dump = temporary / "database.dump"
        pg_env, database_target = postgres_environment(database_url)
        command = [args.pg_dump, "--format=custom", "--compress=9", "--no-owner", "--no-acl", "--file", str(database_dump)]
        completed = subprocess.run(command, env=pg_env, text=True, capture_output=True, check=False)
        if completed.returncode:
            raise RuntimeError(f"pg_dump failed ({completed.returncode}): {completed.stderr.strip()}")
        coverage = database_coverage(database_url, args.psql)
        atomic_json(temporary / "database-coverage.json", coverage)

        storage = SupabaseStorage(storage_url, storage_key)
        media_root = temporary / "private-media"
        media_index = []
        for item in storage.list_objects():
            destination = safe_media_path(media_root, item["path"])
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(storage.download(item["path"]))
            media_index.append({**item, "bytes": destination.stat().st_size, "sha256": sha256(destination)})
        atomic_json(temporary / "private-media-index.json", media_index)

        files = []
        for file_path in sorted(x for x in temporary.rglob("*") if x.is_file() and x.name != "manifest.json"):
            files.append({"path": file_path.relative_to(temporary).as_posix(), "bytes": file_path.stat().st_size, "sha256": sha256(file_path)})
        completed_at = utc_now()
        manifest = {
            "formatVersion": 1, "application": "DroneOps", "targetEnvironment": args.target_environment,
            "startedAt": iso(started), "completedAt": iso(completed_at), "databaseTarget": database_target,
            "storageOrigin": storage_url.rstrip("/"), "storageBucket": "user-media",
            "coverage": coverage, "privateMediaObjects": len(media_index), "files": files,
            "requiredRecoveryObjectives": {"maximumRpoHours": 24, "maximumRtoHours": 4},
        }
        atomic_json(temporary / "manifest.json", manifest)
        os.replace(temporary, final)
        print(json.dumps({"backup": str(final), "completedAt": manifest["completedAt"], "privateMediaObjects": len(media_index), "coverage": coverage}, indent=2))
        return 0
    except BaseException:
        shutil.rmtree(temporary, ignore_errors=True)
        raise


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"BACKUP FAILED: {error}", file=sys.stderr)
        raise SystemExit(1)
