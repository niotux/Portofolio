"""Echo: the smallest useful Holodeck agent.

It asks Holodeck for the next packet and answers in the same channel with "echo: <note>" and the files it
received. It ignores notes that are already echoes, so two echoes in one channel do not answer each other forever. Holodeck passes its address, this run's key, and this agent's table in environment variables.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

URL = os.environ["HOLODECK_URL"].rstrip("/")
KEY = os.environ["HOLODECK_KEY"]
WAIT_SECONDS = 30
MAX_BACKOFF_SECONDS = 15
ECHO_PREFIX = "echo: "


def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(URL + path, data=data, method=method)
    request.add_header("Authorization", "Bearer " + KEY)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request, timeout=WAIT_SECONDS + 15) as response:
        raw = response.read()
        return json.loads(raw) if raw else None


def main():
    backoff = 1
    while True:
        try:
            packet = call("GET", f"/api/agent/next?wait={WAIT_SECONDS}")
            backoff = 1
        except urllib.error.HTTPError as error:
            if error.code == 401:
                print("my key was revoked; stopping", flush=True)
                sys.exit(0)
            print(f"holodeck answered {error.code}; retrying in {backoff}s", flush=True)
            time.sleep(backoff)
            backoff = min(backoff * 2, MAX_BACKOFF_SECONDS)
            continue
        except (urllib.error.URLError, OSError) as error:
            print(f"cannot reach holodeck ({error}); retrying in {backoff}s", flush=True)
            time.sleep(backoff)
            backoff = min(backoff * 2, MAX_BACKOFF_SECONDS)
            continue
        if packet is None:
            continue
        print(f"from {packet['from']} in {packet['channel']}: {packet['note']!r} with {len(packet['files'])} files", flush=True)
        if packet["note"].startswith(ECHO_PREFIX):
            continue
        try:
            call("POST", "/api/agent/send", {"channel": packet["channelId"], "note": ECHO_PREFIX + packet["note"], "files": packet["files"]})
        except urllib.error.HTTPError as error:
            print(f"could not reply to {packet['from']}: {error.code} {error.read().decode(errors='replace')}", flush=True)


if __name__ == "__main__":
    main()
