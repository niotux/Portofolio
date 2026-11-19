"""Disposable Docker proof: distinct identities, private files, independent process control and granted sudo."""
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

IMAGE = "holodeck-library:1"


def docker(*arguments):
    return subprocess.check_output(["docker", "--context", "default", *arguments], text=True, stderr=subprocess.PIPE).strip()


def main():
    name = "holodeck-library-proof-" + secrets.token_hex(4)
    control = secrets.token_hex(32)
    with tempfile.TemporaryDirectory(prefix="holodeck-library-proof-") as temporary:
        root = Path(temporary)
        library = root / "library"
        library.mkdir()
        data = root / "data"
        for folder in ("home", "srv", "state"):
            (data / folder).mkdir(parents=True)
        program = '''import json, os, pathlib, subprocess, time, urllib.request, urllib.error
root = pathlib.Path.cwd()
(root / "private.txt").write_text("private")
status = pathlib.Path("/proc/self/status").read_text()
report = {"uid": os.getuid(), "noPrivileges": "NoNewPrivs:\\t1" in status,
          "capabilitiesDropped": "CapEff:\\t0000000000000000" in status and "CapBnd:\\t0000000000000000" in status}
for label, filename in [("controlPrivate", "/etc/holodeck/config.json"), ("peerPrivate", "/home/beta/private.txt"), ("peerTablePrivate", "/srv/agent-two/private.txt")]:
    try:
        pathlib.Path(filename).read_text()
        report[label] = False
    except PermissionError:
        report[label] = True
    except FileNotFoundError:
        report[label] = True
try:
    os.setuid(0)
    report["rootDenied"] = False
except PermissionError:
    report["rootDenied"] = True
try:
    urllib.request.urlopen("http://127.0.0.1:8079/processes")
    report["controlDenied"] = False
except urllib.error.HTTPError as error:
    report["controlDenied"] = error.code == 403
for label, target in [("homeSwapDenied", pathlib.Path.home()), ("tableSwapDenied", root), ("logSwapDenied", pathlib.Path("/var/lib/holodeck/logs") / (os.environ["USER"] + ".log"))]:
    try:
        target.rename(str(target) + ".old")
        report[label] = False
    except PermissionError:
        report[label] = True
try:
    escalated = subprocess.run(["sudo", "-n", "id", "-u"], capture_output=True, text=True, timeout=10)
    report["sudo"] = escalated.stdout.strip() if escalated.returncode == 0 else ""
    report["sudoError"] = escalated.stderr.strip()
except OSError as error:
    report["sudo"] = ""
    report["sudoError"] = str(error)
if report["sudo"] == "0":
    subprocess.Popen(["sudo", "-n", "sleep", "600"])
(root / "proof.json").write_text(json.dumps(report))
while True:
    time.sleep(1)
'''
        (library / "proof.py").write_text(program)
        processes = []
        for identity in ("alpha", "beta", "service"):
            processes.append({"id": identity, "command": ["python3", "/library/proof.py"], "serviceToken": secrets.token_hex(32)})
        config = root / "config.json"
        config.write_text(json.dumps({"controlToken": control, "processes": processes}))
        os.chmod(config, 0o600)
        command = ["run", "-d", "--name", name, "--init", "--cap-drop=ALL", "--memory=256m", "--pids-limit=256"]
        for capability in ("CHOWN", "DAC_OVERRIDE", "FOWNER", "FSETID", "SETUID", "SETGID", "SETPCAP", "KILL"):
            command.append("--cap-add=" + capability)
        command += ["-p", "127.0.0.1::8079", "-v", str(config) + ":/etc/holodeck/config.json:ro", "-v", str(library) + ":/library:ro"]
        command += ["-v", str(data / "home") + ":/home", "-v", str(data / "srv") + ":/srv", "-v", str(data / "state") + ":/var/lib/holodeck", IMAGE]
        try:
            docker(*command)
            port = json.loads(docker("inspect", name))[0]["NetworkSettings"]["Ports"]["8079/tcp"][0]["HostPort"]
            base = "http://127.0.0.1:" + port

            def request(path, method="GET", value=None):
                body = None if value is None else json.dumps(value).encode()
                call = urllib.request.Request(base + path, data=body, method=method, headers={"Authorization": "Bearer " + control, "Content-Type": "application/json"})
                with urllib.request.urlopen(call, timeout=3) as response:
                    return json.load(response)

            deadline = time.monotonic() + 15
            while True:
                try:
                    state = request("/processes")
                    if len(state["processes"]) == 3 and all(item["running"] for item in state["processes"]):
                        break
                except (OSError, urllib.error.URLError):
                    pass
                if time.monotonic() >= deadline:
                    raise AssertionError("Supervisor/processes failed to start: " + docker("logs", name))
                time.sleep(0.1)
            for identity in ("alpha", "beta", "service"):
                path = f"/home/{identity}/proof.json"
                while True:
                    try:
                        report = json.loads(docker("exec", name, "cat", path))
                        break
                    except subprocess.CalledProcessError:
                        if time.monotonic() >= deadline:
                            raise
                        time.sleep(0.1)
                assert report["uid"] >= 10000, report
                assert report["noPrivileges"] and report["capabilitiesDropped"] and report["sudo"] == "", report
                assert report["controlPrivate"] and report["rootDenied"] and report["controlDenied"], report
                assert report["homeSwapDenied"] and report["logSwapDenied"] and report["peerTablePrivate"], report
                if identity != "beta":
                    assert report["peerPrivate"], report
            docker("exec", "--user", "10000:10000", name, "/bin/mkdir", "-p", "/home/alpha/.local/bin")
            docker("exec", "--user", "10000:10000", name, "/bin/sh", "-c", "printf '#!/bin/sh\nif [ \"$(id -u)\" = 0 ]; then touch /home/alpha/ROOT-LAUNCHER-INJECTION; fi\nexec /usr/bin/python3 \"$@\"\n' > /home/alpha/.local/bin/python3; chmod +x /home/alpha/.local/bin/python3")
            before = request("/processes")
            assert len({item["uid"] for item in before["processes"]}) == 3
            request("/processes/alpha/stop", "POST")
            stopped = {item["id"]: item for item in request("/processes")["processes"]}
            assert not stopped["alpha"]["running"]
            assert stopped["beta"]["running"] and stopped["service"]["running"]
            request("/processes/alpha/start", "POST")
            assert all(item["running"] for item in request("/processes")["processes"])
            time.sleep(0.2)
            injection = subprocess.run(["docker", "--context", "default", "exec", name, "test", "-e", "/home/alpha/ROOT-LAUNCHER-INJECTION"], capture_output=True)
            assert injection.returncode != 0, "Workload PATH replaced the privileged launcher"
            for identity in ("agent-one", "agent-two"):
                registration = {"id": identity, "command": ["python3", "/library/proof.py"],
                                "environment": {"HOLODECK_KEY": secrets.token_hex(32)}, "platformGid": 1000,
                                "settingsJson": "{\"private\":true}"}
                registered = request("/processes/register", "POST", registration)
                assert registered["uid"] >= 20000 and not registered["running"]
                request(f"/processes/{identity}/start", "POST")
                target = f"/srv/{identity}/proof.json"
                deadline = time.monotonic() + 5
                while True:
                    try:
                        report = json.loads(docker("exec", "--user", "1000:1000", name, "cat", target))
                        break
                    except subprocess.CalledProcessError:
                        if time.monotonic() >= deadline:
                            raise
                        time.sleep(0.1)
                assert report["rootDenied"] and report["capabilitiesDropped"] and report["peerPrivate"], report
                assert report["homeSwapDenied"] and report["tableSwapDenied"] and report["logSwapDenied"], report
                if identity == "agent-one":
                    assert report["peerTablePrivate"], report
            request("/processes/agent-one/stop", "POST")
            state = {item["id"]: item for item in request("/processes")["processes"]}
            assert not state["agent-one"]["running"] and state["agent-two"]["running"] and state["service"]["running"]
            first_uid = str(state["agent-one"]["uid"])
            docker("exec", "--user", first_uid + ":" + first_uid, name, "touch", "/home/agent-one/reset-marker")
            request("/processes/agent-one/reset-home", "POST")
            marker = subprocess.run(["docker", "--context", "default", "exec", name, "test", "-e", "/home/agent-one/reset-marker"], capture_output=True)
            assert marker.returncode != 0
            docker("exec", name, "test", "-e", "/srv/agent-one/proof.json")
            uid = str(state["agent-two"]["uid"])
            private_settings = subprocess.run(["docker", "--context", "default", "exec", "--user", uid + ":" + uid, name, "cat", "/var/lib/holodeck/settings/agent-one.json"], capture_output=True)
            assert private_settings.returncode != 0
            logs = request("/processes/agent-one/logs?offset=0")
            assert isinstance(logs["text"], str) and logs["offset"] >= 0
            docker("restart", name)
            port = json.loads(docker("inspect", name))[0]["NetworkSettings"]["Ports"]["8079/tcp"][0]["HostPort"]
            base = "http://127.0.0.1:" + port
            deadline = time.monotonic() + 10
            while True:
                try:
                    state = {item["id"]: item for item in request("/processes")["processes"]}
                    if state["service"]["running"]:
                        break
                except (OSError, urllib.error.URLError):
                    pass
                if time.monotonic() >= deadline:
                    raise AssertionError("Recovery failed: " + docker("logs", name))
                time.sleep(0.1)
            assert not state["agent-one"]["running"] and not state["agent-two"]["running"]
            assert state["agent-two"]["uid"] == int(uid)
            prove_sudo(name, request)
            print("Shared container proof passed: separate non-root services and registered agents; private credentials/files; zero capabilities; platform group access; independent stop/start; persistent identities and stopped-agent recovery; sudo only for granted workloads, ended with them and revocable.")
        finally:
            subprocess.run(["docker", "--context", "default", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
            # The proof created these files as workload UIDs. Remove only its disposable bind directory.
            subprocess.run(["docker", "--context", "default", "run", "--rm", "--entrypoint", "rm", "-v", str(data) + ":/proof", IMAGE, "-rf", "/proof/home", "/proof/srv", "/proof/state"], stdout=subprocess.DEVNULL, check=False)


def read_report(name, identity, previous):
    deadline = time.monotonic() + 10
    while True:
        try:
            report = docker("exec", name, "cat", f"/home/{identity}/proof.json")
            if report != previous:
                return json.loads(report)
        except subprocess.CalledProcessError:
            pass
        if time.monotonic() >= deadline:
            raise AssertionError(f"{identity} wrote no new proof: " + docker("logs", name))
        time.sleep(0.1)


def root_sleeps(name):
    return docker("exec", name, "sh", "-c", "ps -o args= -u 0 | grep -c '^sleep 600' || true")


def prove_sudo(name, request):
    request("/sudo", "POST", {"processes": ["beta"]})
    before = docker("exec", name, "cat", "/home/beta/proof.json")
    request("/processes/beta/stop", "POST")
    request("/processes/beta/start", "POST")
    granted = read_report(name, "beta", before)
    assert granted["sudo"] == "0" and not granted["noPrivileges"] and granted["rootDenied"], granted
    state = {item["id"]: item for item in request("/processes")["processes"]}
    assert state["beta"]["sudo"] and not state["alpha"]["sudo"], state
    refused = subprocess.run(["docker", "--context", "default", "exec", "--user", "10000:10000", name, "sudo", "-n", "true"], capture_output=True)
    assert refused.returncode != 0, "An ungranted workload could use sudo"
    deadline = time.monotonic() + 5
    while root_sleeps(name) != "1":
        if time.monotonic() >= deadline:
            raise AssertionError("The granted workload's root command did not start")
        time.sleep(0.1)
    request("/processes/beta/stop", "POST")
    assert root_sleeps(name) == "0", "Stopping a granted workload left its root command running"
    request("/sudo", "POST", {"processes": []})
    before = docker("exec", name, "cat", "/home/beta/proof.json")
    request("/processes/beta/start", "POST")
    revoked = read_report(name, "beta", before)
    assert revoked["sudo"] == "" and revoked["noPrivileges"], revoked
    entries = docker("exec", name, "ls", "/etc/sudoers.d")
    assert "holodeck-" not in entries, entries


if __name__ == "__main__":
    main()
