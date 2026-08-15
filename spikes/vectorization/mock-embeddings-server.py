"""Minimal OpenAI-compatible /v1/embeddings stub: proves the HTTP plumbing only.
Vectors are a deterministic hash, NOT semantic."""
import hashlib, json
from http.server import BaseHTTPRequestHandler, HTTPServer

DIM = 384

def embed(text):
    v = [0.0] * DIM
    for tok in "".join(c.lower() if c.isalnum() else " " for c in text).split():
        d = hashlib.sha256(tok.encode()).digest()
        v[int.from_bytes(d[:4], "little") % DIM] += 1.0 if d[4] % 2 == 0 else -1.0
    n = sum(x * x for x in v) ** 0.5 or 1.0
    return [x / n for x in v]

class H(BaseHTTPRequestHandler):
    def do_POST(self):
        length = self.headers["Content-Length"]
        if length is not None:
            raw = self.rfile.read(int(length))
        else:  # .NET's JsonContent sends Transfer-Encoding: chunked
            raw = b""
            while (size := int(self.rfile.readline().strip(), 16)) > 0:
                raw += self.rfile.read(size)
                self.rfile.readline()
            self.rfile.readline()
        body = json.loads(raw)
        out = json.dumps({"object": "list", "model": body["model"],
                          "data": [{"object": "embedding", "index": 0,
                                    "embedding": embed(body["input"])}]}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(out)))
        self.end_headers()
        self.wfile.write(out)
    def log_message(self, *a):
        pass

HTTPServer(("127.0.0.1", 8477), H).serve_forever()
