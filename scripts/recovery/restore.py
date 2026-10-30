from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from recovery_lib import SupabaseStorage, atomic_json, database_coverage, iso, postgres_environment, require_empty_restore_database, require_env, require_isolated_target, safe_object_file, utc_now, verify_manifest


def parse_time(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def restore_toc(pg_restore: str, dump: Path) -> Path:
    listed = subprocess.run([pg_restore, "--list", str(dump)], text=True, capture_output=True, check=False)
    if listed.returncode:
        raise RuntimeError(f"pg_restore list failed ({listed.returncode}): {listed.stderr.strip()}")
    lines = [line for line in listed.stdout.splitlines() if " SCHEMA - public " not in f" {line} "]
    handle = tempfile.NamedTemporaryFile("w", suffix=".toc", prefix="droneops-restore-", encoding="utf-8", delete=False)
    with handle:
        handle.write("\n".join(lines) + "\n")
    return Path(handle.name)


def main() -> int:
    parser = argparse.ArgumentParser(description="Restore a DroneOps backup into an isolated rehearsal target.")
    parser.add_argument("--backup", required=True, type=Path)
    parser.add_argument("--target-environment", required=True)
    parser.add_argument("--confirm", required=True)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--pg-restore", default="pg_restore")
    parser.add_argument("--psql", default="psql")
    args = parser.parse_args()
    target = require_isolated_target(args.target_environment, args.confirm)
    if os.environ.get("DRONEOPS_RESTORE_ISOLATED", "") != "YES":
        raise RuntimeError("Set DRONEOPS_RESTORE_ISOLATED=YES only after verifying network and account isolation.")
    database_url = require_env("DRONEOPS_RESTORE_DATABASE_URL")
    storage_url = require_env("DRONEOPS_RESTORE_STORAGE_URL").rstrip("/")
    storage_key = require_env("DRONEOPS_RESTORE_STORAGE_SERVICE_KEY")
    backup = args.backup.resolve()
    manifest = verify_manifest(backup)
    if storage_url == str(manifest.get("storageOrigin", "")).rstrip("/"):
        raise RuntimeError("Restore storage must not be the source/production storage project.")

    started = utc_now()
    rpo_hours = (started - parse_time(manifest["completedAt"])).total_seconds() / 3600
    evidence = {
        "formatVersion": 1, "targetEnvironment": target, "sourceBackup": backup.name,
        "sourceBackupCompletedAt": manifest["completedAt"], "rehearsalStartedAt": iso(started),
        "rpoHours": round(rpo_hours, 4), "rpoObjectiveHours": 24, "rtoObjectiveHours": 4,
        "automaticProductionMigration": False, "databaseRestored": False, "privateMediaRestored": False,
        "status": "Running",
    }
    args.evidence.parent.mkdir(parents=True, exist_ok=True)
    atomic_json(args.evidence, evidence)
    uploaded: list[str] = []
    storage = SupabaseStorage(storage_url, storage_key)
    try:
        pg_env, database_target = postgres_environment(database_url)
        evidence["databaseTarget"] = database_target
        evidence["storageTarget"] = storage_url
        require_empty_restore_database(database_url, args.psql)
        toc = restore_toc(args.pg_restore, backup / "database.dump")
        command = [args.pg_restore, "--exit-on-error", "--single-transaction", "--use-list", str(toc),
                   "--no-owner", "--no-acl", "--dbname", pg_env["PGDATABASE"], str(backup / "database.dump")]
        completed = subprocess.run(command, env=pg_env, text=True, capture_output=True, check=False)
        toc.unlink(missing_ok=True)
        if completed.returncode:
            raise RuntimeError(f"pg_restore failed ({completed.returncode}): {completed.stderr.strip()}")
        evidence["databaseRestored"] = True

        media_index = json.loads((backup / "private-media-index.json").read_text(encoding="utf-8"))
        for item in media_index:
            object_path = item["path"]
            data = safe_object_file(backup / "private-media", object_path).read_bytes()
            storage.upload(object_path, data, item.get("contentType") or "application/octet-stream")
            uploaded.append(object_path)
        evidence["privateMediaRestored"] = True

        restored_coverage = database_coverage(database_url, args.psql)
        source_coverage = {key: int(value) for key, value in manifest["coverage"].items()}
        evidence["sourceCoverage"] = source_coverage
        evidence["restoredCoverage"] = restored_coverage
        evidence["coverageMatches"] = restored_coverage == source_coverage
        evidence["privateMediaExpected"] = int(manifest["privateMediaObjects"])
        evidence["privateMediaRestoredCount"] = len(uploaded)
        evidence["privateMediaMatches"] = len(uploaded) == int(manifest["privateMediaObjects"])
        completed_at = utc_now()
        rto_hours = (completed_at - started).total_seconds() / 3600
        evidence.update({"rehearsalCompletedAt": iso(completed_at), "rtoHours": round(rto_hours, 4),
                         "rpoMet": 0 <= rpo_hours <= 24, "rtoMet": rto_hours <= 4})
        passed = evidence["rpoMet"] and evidence["rtoMet"] and evidence["coverageMatches"] and evidence["privateMediaMatches"]
        evidence["status"] = "Passed" if passed else "Failed"
        atomic_json(args.evidence, evidence)
        print(json.dumps(evidence, indent=2))
        return 0 if passed else 1
    except BaseException as error:
        if uploaded:
            try:
                storage.delete(uploaded)
            except Exception as cleanup_error:
                evidence["mediaCleanupError"] = str(cleanup_error)
        evidence.update({"status": "Failed", "failure": str(error), "rehearsalCompletedAt": iso(utc_now())})
        evidence["rtoHours"] = round((utc_now() - started).total_seconds() / 3600, 4)
        atomic_json(args.evidence, evidence)
        raise


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"RESTORE FAILED: {error}", file=sys.stderr)
        raise SystemExit(1)
