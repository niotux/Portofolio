#!/usr/bin/env python3
"""Call one humans-prompts MCP tool over stdio: mcp.py TOOL '{"arg":...}'  (or @file.json for arguments)."""
import json, subprocess, sys
tool = sys.argv[1]
raw = sys.argv[2] if len(sys.argv) > 2 else "{}"
args = json.load(open(raw[1:])) if raw.startswith("@") else json.loads(raw)
p = subprocess.Popen([__import__("os").path.expanduser("~/.local/share/humans-prompts/humans-prompts")], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
def send(m): p.stdin.write(json.dumps(m) + "\n"); p.stdin.flush()
def recv(i):
    while True:
        m = json.loads(p.stdout.readline())
        if m.get("id") == i: return m
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"run-script","version":"1"}}})
recv(1)
send({"jsonrpc":"2.0","method":"notifications/initialized"})
send({"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":tool,"arguments":args}})
r = recv(2)
p.stdin.close(); p.terminate()
res = r.get("result", r)
for c in res.get("content", []):
    print(c.get("text", ""))
if res.get("isError") or "error" in r: print("ERROR", json.dumps(r)[:500]); sys.exit(1)
