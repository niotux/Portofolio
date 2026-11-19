#!/usr/bin/env python3
"""Holodeck test driver (user API). Usage:
  hh.py say PROJECT CHANNEL "text"          -> prints packet id
  hh.py wait PROJECT CHANNEL AFTER SECONDS  -> waits for non-You messages after id, prints them
  hh.py history PROJECT CHANNEL [AFTER]
  hh.py stream PROJECT AGENT [LAST_N]       -> the person's continuing history from its DB
  hh.py sql PROJECT AGENT "SQL"
  hh.py log PROJECT AGENT
  hh.py file PROJECT AGENT PATH
"""
import json, sys, time, urllib.request, uuid
BASE = "http://127.0.0.1:7077"
def req(method, path, body=None, form=None):
    data, headers = None, {}
    if form is not None:
        b = uuid.uuid4().hex
        parts = []
        for k, v in form.items():
            parts.append(f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n')
        data = ("".join(parts) + f"--{b}--\r\n").encode()
        headers["Content-Type"] = f"multipart/form-data; boundary={b}"
    elif body is not None:
        data = json.dumps(body).encode(); headers["Content-Type"] = "application/json"
    r = urllib.request.urlopen(urllib.request.Request(BASE + path, data=data, method=method, headers=headers), timeout=60)
    t = r.read().decode()
    ct = r.headers.get("Content-Type", "")
    return json.loads(t) if "json" in ct else t
def room(p): return f"/api/projects/{p}/room"
def history(p, c, after=0): return req("GET", f"{room(p)}/channels/{c}/packets?after={after}")
def you(p):
    for m in req("GET", room(p))["members"]:
        if m["isYou"]: return m["id"]
def names(p): return {m["id"]: m["name"] for m in req("GET", room(p))["members"]}
def say(p, c, text):
    r = req("POST", f"{room(p)}/channels/{c}/packets", form={"note": text})
    return r["id"] if isinstance(r, dict) else r
def items(h): return h if isinstance(h, list) else h.get("packets", h.get("items", []))
def wait(p, c, after, seconds):
    me = you(p); end = time.time() + seconds
    while time.time() < end:
        got = [m for m in items(history(p, c, after)) if m.get("senderId") != me]
        if got: return got
        time.sleep(3)
    return []
def sql(p, a, q): return req("POST", f"{room(p)}/agents/{a}/database", {"statements": [{"sql": q, "parameters": []}]})["results"][0]["rows"]
def person(p, name, version="hh-0.2.0", direct=True, start=True):
    a = req("POST", f"{room(p)}/agents", {"library": f"holodeck.humans/0.1.0/agents/{version}"})["id"]
    req("PUT", f"{room(p)}/agents/{a}", {"name": name})
    c = req("POST", f"{room(p)}/channels", {"name": name.lower(), "memberIds": [you(p), a]})["id"] if direct else ""
    if start: req("POST", f"{room(p)}/agents/{a}/start")
    return a, c
def group(p, name, members, with_you=True):
    ids = members + ([you(p)] if with_you else [])
    return req("POST", f"{room(p)}/channels", {"name": name, "memberIds": ids})["id"]
def converse(p, c, lines, seconds=150, settle=20):
    """Send each line after the other side answers; print the transcript. A line starting with '+N ' waits N seconds first."""
    n = names(p); me = you(p)
    for line in lines:
        if line.startswith("+"):
            d, line = line[1:].split(" ", 1); time.sleep(int(d))
        last = say(p, c, line); print(">>", line, flush=True)
        got = wait(p, c, last, seconds)
        # Let a burst of several messages finish.
        while got:
            time.sleep(settle)
            more = [m for m in items(history(p, c, got[-1]["id"])) if m.get("senderId") != me]
            for m in got: print("  ", n.get(m["senderId"], "?") + ":", m["note"], flush=True)
            got = more
if __name__ == "__main__":
    cmd, a = sys.argv[1], sys.argv[2:]
    if cmd == "say": print(say(a[0], a[1], a[2]))
    elif cmd == "wait":
        n = names(a[0])
        for m in wait(a[0], a[1], int(a[2]), int(a[3])): print(m["id"], n.get(m["senderId"], m["senderId"]) + ":", m["note"])
    elif cmd == "history":
        n = names(a[0])
        for m in items(history(a[0], a[1], int(a[2]) if len(a) > 2 else 0)): print(m["id"], n.get(m["senderId"], "?") + ":", m["note"])
    elif cmd == "stream":
        last = int(a[2]) if len(a) > 2 else 40
        for r in sql(a[0], a[1], f"SELECT id, kind, speaker, text, to_char(at AT TIME ZONE 'Europe/Bucharest','HH24:MI:SS') FROM (SELECT * FROM stream ORDER BY id DESC LIMIT {last}) s ORDER BY id"):
            print(f"{r[0]:>4} {r[4]} {r[1]:<12} {(r[2]+': ') if r[2] else ''}{r[3]}")
    elif cmd == "sql":
        for r in sql(a[0], a[1], a[2]): print(r)
    elif cmd == "person": print(*person(a[0], a[1], a[2] if len(a) > 2 else "hh-0.2.0"))
    elif cmd == "group": print(group(a[0], a[1], a[2].split(","), not (len(a) > 3 and a[3] == "noyou")))
    elif cmd == "start": req("POST", f"{room(a[0])}/agents/{a[1]}/start"); print("started")
    elif cmd == "stop": req("POST", f"{room(a[0])}/agents/{a[1]}/stop"); print("stopped")
    elif cmd == "add": req("POST", f"{room(a[0])}/channels/{a[1]}/members", {"memberId": a[2]}); print("added")
    elif cmd == "remove": req("DELETE", f"{room(a[0])}/channels/{a[1]}/members/{a[2]}"); print("removed")
    elif cmd == "converse": converse(a[0], a[1], [l for l in open(a[2]).read().split("\n") if l.strip()], int(a[3]) if len(a) > 3 else 150)
    elif cmd == "log": print(req("GET", f"{room(a[0])}/agents/{a[1]}/log"))
    elif cmd == "file": print(req("GET", f"{room(a[0])}/agents/{a[1]}/table/file?path={a[2]}"))
