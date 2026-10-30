import json
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from backup import safe_media_path
from recovery_lib import SupabaseStorage, atomic_json, postgres_environment, require_isolated_target, sha256, verify_manifest


class RecoveryToolTests(unittest.TestCase):
    def test_restore_guard_rejects_production_and_requires_exact_confirmation(self):
        with self.assertRaises(RuntimeError):
            require_isolated_target("production", "RESTORE-PRODUCTION")
        with self.assertRaises(RuntimeError):
            require_isolated_target("recovery", "yes")
        self.assertEqual(require_isolated_target("recovery", "RESTORE-RECOVERY"), "recovery")

    def test_postgres_secret_is_kept_in_environment_not_safe_target(self):
        env, safe = postgres_environment("postgresql://operator:p%40ss@db.example:5433/droneops_recovery?sslmode=require")
        self.assertEqual(env["PGPASSWORD"], "p@ss")
        self.assertEqual(env["PGSSLMODE"], "require")
        self.assertEqual(safe, "db.example:5433/droneops_recovery")
        self.assertNotIn("p@ss", safe)

    def test_media_path_cannot_escape_backup(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.assertEqual(safe_media_path(root, "licenses/a/file.pdf"), root / "licenses" / "a" / "file.pdf")
            with self.assertRaises(RuntimeError):
                safe_media_path(root, "../secret")

    def test_manifest_verification_detects_tampering(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); payload = root / "database.dump"; payload.write_bytes(b"valid")
            atomic_json(root / "manifest.json", {"files": [{"path": "database.dump", "sha256": sha256(payload)}]})
            self.assertEqual(verify_manifest(root)["files"][0]["path"], "database.dump")
            payload.write_bytes(b"tampered")
            with self.assertRaises(RuntimeError):
                verify_manifest(root)

    def test_migration_order_lists_every_sql_once(self):
        repo = Path(__file__).resolve().parents[2]
        migrations = {path.name for path in (repo / "database" / "migrations").glob("*.sql")}
        order = [line.strip() for line in (repo / "database" / "migration-order.txt").read_text().splitlines()
                 if line.strip() and not line.startswith("#")]
        self.assertEqual(len(order), len(set(order)))
        self.assertEqual(set(order), migrations)

    def test_private_storage_is_listed_recursively_and_can_round_trip(self):
        calls = []
        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_): pass
            def _body(self): return self.rfile.read(int(self.headers.get("Content-Length", "0")))
            def do_POST(self):
                body = self._body(); calls.append(("POST", self.path, body))
                if "/object/list/" in self.path:
                    prefix = json.loads(body)["prefix"]
                    value = ([{"name": "licenses", "id": None}, {"name": "avatar.png", "id": "1", "metadata": {"size": 3, "mimetype": "image/png"}}]
                             if prefix == "" else [{"name": "license.pdf", "id": "2", "metadata": {"size": 4, "mimetype": "application/pdf"}}])
                    data = json.dumps(value).encode(); self.send_response(200); self.send_header("Content-Length", str(len(data))); self.end_headers(); self.wfile.write(data)
                else:
                    self.send_response(200); self.end_headers()
            def do_GET(self):
                calls.append(("GET", self.path, b"")); data = b"file"; self.send_response(200); self.send_header("Content-Length", "4"); self.end_headers(); self.wfile.write(data)
            def do_DELETE(self):
                body = self._body(); calls.append(("DELETE", self.path, body)); self.send_response(200); self.end_headers()
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
        try:
            storage = SupabaseStorage(f"http://127.0.0.1:{server.server_port}", "test-key")
            objects = storage.list_objects()
            self.assertEqual([x["path"] for x in objects], ["licenses/license.pdf", "avatar.png"])
            self.assertEqual(storage.download("licenses/license.pdf"), b"file")
            storage.upload("licenses/restored.pdf", b"data", "application/pdf")
            storage.delete(["licenses/restored.pdf"])
            self.assertTrue(any(x[0] == "DELETE" for x in calls))
        finally:
            server.shutdown(); server.server_close(); thread.join()


if __name__ == "__main__":
    unittest.main()
