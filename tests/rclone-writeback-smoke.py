"""Real rclone VFS writeback against a disposable WebDAV backend; no mount/WinFsp.
Usage: python tests/rclone-writeback-smoke.py /path/to/rclone
"""
import base64
import http.server
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from xml.sax.saxutils import escape

stored = {}
directories = ["/", "/Zakazky/", "/Zakazky/ABCDEF-123456/", "/Zakazky/ABCDEF-123456/Interni/"]

class Backend(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, status, body=b"", content_type="text/plain"):
        self.send_response(status)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Content-Type", content_type)
        self.end_headers()
        self.wfile.write(body)

    def do_PROPFIND(self):
        path = urllib.parse.unquote(self.path)
        if path not in stored and not path.endswith("/"):
            path += "/"
        if path not in directories and path not in stored:
            return self.reply(404)
        children = [path]
        if path in directories and self.headers.get("Depth") != "0":
            children += [p for p in directories + list(stored) if p != path and p.startswith(path)
                         and "/" not in p[len(path):].rstrip("/")]
        items = []
        for child in children:
            directory = child in directories
            resource = "<d:collection/>" if directory else ""
            items.append(f"""<d:response><d:href>{escape(child)}</d:href><d:propstat><d:prop>
<d:resourcetype>{resource}</d:resourcetype><d:getcontentlength>{len(stored.get(child, b''))}</d:getcontentlength>
<d:getlastmodified>Mon, 21 Sep 2026 10:00:00 GMT</d:getlastmodified>
</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>""")
        self.reply(207, ('<?xml version="1.0"?><d:multistatus xmlns:d="DAV:">' + "".join(items)
                         + '</d:multistatus>').encode(), "application/xml")

    def do_PUT(self):
        data = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        if "/Interni/" not in self.path:
            return self.reply(403, b"Use Interni")
        stored[self.path] = data
        self.reply(201)

    def do_MKCOL(self):
        self.reply(405)  # Existing collections only.

    def do_GET(self):
        if self.path not in stored:
            return self.reply(404)
        self.reply(200, stored[self.path])

    def do_DELETE(self):
        if self.path not in stored:
            return self.reply(404)
        del stored[self.path]
        self.reply(204)

def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]

def request(port, path, method="GET", data=None, headers=None):
    req = urllib.request.Request(f"http://127.0.0.1:{port}{path}", data=data, method=method, headers=headers or {})
    with urllib.request.urlopen(req, timeout=3) as response:
        return response.status, response.read()

def wait_for(fn, description):
    deadline = time.monotonic() + 25
    while time.monotonic() < deadline:
        try:
            result = fn()
            if result:
                return result
        except (OSError, urllib.error.URLError):
            pass
        time.sleep(.2)
    raise AssertionError(description)

backend = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Backend)
threading.Thread(target=backend.serve_forever, daemon=True).start()
try:
    with tempfile.TemporaryDirectory(prefix="camledian-vfs-") as tmp:
        serve_port, rc_port = free_port(), free_port()
        secret = secrets.token_hex(32)
        env = dict(os.environ, RCLONE_RC_USER="test", RCLONE_RC_PASS=secret,
                   RCLONE_WEBDAV_URL=f"http://127.0.0.1:{backend.server_port}/", RCLONE_WEBDAV_VENDOR="other")
        auth = "Basic " + base64.b64encode(f"test:{secret}".encode()).decode()
        def rc(method):
            return json.loads(request(rc_port, "/" + method, "POST", b"{}",
                                     {"Authorization": auth, "Content-Type": "application/json"})[1])
        with open(Path(tmp) / "rclone.log", "w+") as log:
            process = subprocess.Popen([sys.argv[1], "serve", "webdav", ":webdav:",
                "--addr", f"127.0.0.1:{serve_port}", "--rc", "--rc-addr", f"127.0.0.1:{rc_port}",
                "--vfs-cache-mode", "full", "--vfs-write-back", "100ms", "--cache-dir", tmp,
                "--config", str(Path(tmp) / "unused.conf")], env=env, stdout=log, stderr=log)
            try:
                wait_for(lambda: rc("vfs/stats"), "RC startup failed")
                root = "/Zakazky/ABCDEF-123456/"
                status, _ = request(serve_port, root + "rejected.txt", "PUT", b"local-only")
                assert status in (200, 201, 204), "Local VFS should initially accept cached write"
                queue = wait_for(lambda: [i for i in rc("vfs/queue").get("queue", [])
                    if i["tries"] > 0 and not i["uploading"]], "Expected failed upload in real queue")
                assert queue[0]["name"] == "Zakazky/ABCDEF-123456/rejected.txt"
                assert root + "rejected.txt" not in stored, "Rejected data reached backend"
                # Delete only this disposable failed test copy; production app never purges cache.
                request(serve_port, root + "rejected.txt", "DELETE")
                request(serve_port, root + "Interni/accepted.txt", "PUT", b"server-confirmed")
                wait_for(lambda: stored.get(root + "Interni/accepted.txt") == b"server-confirmed", "Valid upload did not finish")
                wait_for(lambda: not rc("vfs/queue")["queue"], "Queue did not clear")
                print("PASS: cached copy succeeds locally, backend rejects with 403, RC exposes retry, valid upload drains queue.")
            except BaseException:
                log.flush()
                log.seek(0)
                print(log.read(), file=sys.stderr)
                raise
            finally:
                process.terminate()
                process.wait(timeout=10)
finally:
    backend.shutdown()
    backend.server_close()
