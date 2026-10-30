from __future__ import annotations

import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def main() -> None:
    parser = argparse.ArgumentParser(description="Isolated in-memory Storage target for recovery rehearsal.")
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--seed", action="append", default=[])
    args = parser.parse_args()
    objects: dict[str, bytes] = {path: ("rehearsal:" + path).encode() for path in args.seed}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_: object) -> None:
            pass

        def reply(self, status: int, payload: object) -> None:
            body = json.dumps(payload).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(length)
            if self.path == "/storage/v1/object/list/user-media":
                payload = json.loads(body or b"{}")
                prefix = str(payload.get("prefix", "")).strip("/")
                children: dict[str, dict] = {}
                for path, data in objects.items():
                    if prefix and not path.startswith(prefix + "/"):
                        continue
                    relative = path[len(prefix) + 1:] if prefix else path
                    first, separator, _ = relative.partition("/")
                    children[first] = ({"name": first, "id": None} if separator else
                        {"name": first, "id": path, "metadata": {"size": len(data), "mimetype": "application/octet-stream"}})
                self.reply(200, list(children.values()))
                return
            marker = "/storage/v1/object/user-media/"
            if marker not in self.path:
                self.reply(404, {"message": "isolated fixture route not found"})
                return
            objects[self.path.split(marker, 1)[1]] = body
            self.reply(200, {"stored": True})

        def do_DELETE(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length) or b"{}")
            for path in payload.get("prefixes", []):
                objects.pop(path, None)
            self.reply(200, {"deleted": True})

        def do_GET(self) -> None:
            if self.path == "/health":
                self.reply(200, {"isolated": True, "objects": len(objects)})
            elif "/storage/v1/object/authenticated/user-media/" in self.path:
                path = self.path.split("/storage/v1/object/authenticated/user-media/", 1)[1]
                if path not in objects:
                    self.reply(404, {"message": "not found"})
                    return
                body = objects[path]
                self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
            else:
                self.reply(404, {"message": "not found"})

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(json.dumps({"url": f"http://127.0.0.1:{server.server_port}", "isolated": True}), flush=True)
    try:
        server.serve_forever()
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
