from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from recovery_lib import atomic_json, sha256


def run(command: list[str], cwd: Path) -> None:
    executable = shutil.which(command[0])
    if not executable:
        raise RuntimeError(f"Required build tool was not found: {command[0]}")
    completed = subprocess.run([executable, *command[1:]], cwd=cwd, check=False)
    if completed.returncode:
        raise RuntimeError(f"Build command failed ({completed.returncode}): {' '.join(command)}")


def copy_tree(source: Path, target: Path) -> None:
    shutil.copytree(source, target, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("*.private.*", "session.private.json", ".env", ".env.*", "__pycache__", "*.pyc"))


def main() -> int:
    parser = argparse.ArgumentParser(description="Build a secret-free DroneOps deployment ZIP. This never runs database migrations.")
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--install-frontend", action="store_true")
    args = parser.parse_args()
    repo = args.repo.resolve()
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix="droneops-deployment-"))
    try:
        api = staging / "api"
        run(["dotnet", "publish", "DroneOps.API/DroneOps.API.csproj", "-c", "Release", "--no-restore", "-o", str(api)], repo / "be" / "DroneOps")
        if args.install_frontend:
            run(["npm", "ci"], repo / "fe")
        run(["npm", "run", "build"], repo / "fe")
        web = staging / "web"
        copy_tree(repo / "fe" / ".next" / "standalone", web)
        copy_tree(repo / "fe" / ".next" / "static", web / ".next" / "static")
        copy_tree(repo / "fe" / "public", web / "public")
        copy_tree(repo / "gateway", staging / "gateway")
        copy_tree(repo / "database" / "migrations", staging / "migrations")
        shutil.copy2(repo / "database" / "migration-order.txt", staging / "migrations" / "migration-order.txt")
        copy_tree(repo / "scripts" / "recovery", staging / "recovery-tools")
        copy_tree(repo / "deploy", staging / "deploy")
        shutil.copy2(repo / "docs" / "deployment-recovery-runbook.md", staging / "DEPLOYMENT-RECOVERY-RUNBOOK.md")
        manifest = {"application": "DroneOps", "version": args.version,
                    "createdAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
                    "automaticProductionMigration": False, "files": []}
        for file_path in sorted(x for x in staging.rglob("*") if x.is_file() and x.name != "package-manifest.json"):
            manifest["files"].append({"path": file_path.relative_to(staging).as_posix(), "bytes": file_path.stat().st_size, "sha256": sha256(file_path)})
        atomic_json(staging / "package-manifest.json", manifest)
        archive_base = output.with_suffix("")
        archive = Path(shutil.make_archive(str(archive_base), "zip", staging))
        if archive != output:
            if output.exists(): output.unlink()
            archive.replace(output)
        print(json.dumps({"package": str(output), "sha256": sha256(output), "files": len(manifest["files"]),
                          "automaticProductionMigration": False}, indent=2))
        return 0
    finally:
        shutil.rmtree(staging, ignore_errors=True)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"PACKAGE FAILED: {error}", file=sys.stderr)
        raise SystemExit(1)
