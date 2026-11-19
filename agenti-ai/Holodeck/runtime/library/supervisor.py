"""Owns declared library processes. Only authenticated platform calls control fixed declarations."""
import hmac
from datetime import datetime, timezone
import json
import os
import re
import secrets
import shutil
from pathlib import Path
import signal
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

CONTROL_PORT = 8079
USER_BASE = 10000
AGENT_USER_BASE = 20000
MAX_REGISTRATION_BYTES = 4 * 1024 * 1024
STOP_GRACE_SECONDS = 3
LOG_CHUNK_BYTES = 65536
CONFIG_PATH = Path("/etc/holodeck/config.json")
LAUNCHER = "/opt/holodeck-library/launcher.py"
# The user grants sudo to individual workloads. Holodeck sends the whole list; it is kept with the supervisor's state so
# a restarted environment starts every workload with its grant. Each granted workload gets one sudoers entry by uid.
SUDOERS = Path("/etc/sudoers.d")
SUDOERS_PREFIX = "holodeck-"
PROCESS_ID = r"[A-Za-z0-9_-]{1,106}"
# Every user's home is /home/<user>. An agent's table is /srv/<agent>, in a root-owned parent so the agent cannot
# swap it for a link to someone else's files. The supervisor's own state, including logs and agents' settings, is
# root-owned under /var/lib/holodeck.
HOMES = Path("/home")
TABLES = Path("/srv")
STATE = Path("/var/lib/holodeck")
SETTINGS = STATE / "settings"
LOGS = STATE / "logs"
SUDO_STATE = STATE / "sudo.json"


class ManagedProcess:
    def __init__(self, definition, uid, agent=False):
        self.definition = definition
        self.uid = uid
        self.agent = agent
        self.sudo = False
        self.create_user()
        self.process = None
        self.started_at = ""
        self.gate = threading.Lock()
        name = definition["id"]
        self.home = HOMES / name
        self.table = TABLES / name
        self.settings = SETTINGS / (name + ".json")
        self.log = LOGS / (name + ".log")
        self.prepare_private(self.home)
        if agent:
            self.prepare_private(self.table)
        self.write_settings()
        log_descriptor = os.open(self.log, os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_NOFOLLOW, 0o600)
        os.fchown(log_descriptor, 0, 0)
        os.fchmod(log_descriptor, 0o600)
        os.close(log_descriptor)

    # Agents share their folders with Holodeck's group for its file APIs; services keep theirs to themselves.
    def prepare_private(self, directory):
        group = self.definition.get("platformGid")
        self.prepare_directory(directory, self.uid, 0o700 if group is None else 0o2770, group)

    @staticmethod
    def prepare_directory(directory, uid, mode, group=None):
        try:
            directory.mkdir()
        except FileExistsError:
            pass
        descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            os.fchown(descriptor, uid, uid if group is None else group)
            os.fchmod(descriptor, mode)
        finally:
            os.close(descriptor)

    def create_user(self):
        name = self.definition["id"]
        known = False
        with open("/etc/passwd", encoding="utf-8") as accounts:
            for account in accounts:
                fields = account.rstrip().split(":")
                if fields[0] == name:
                    if fields[2] != str(self.uid):
                        raise ValueError("Declared process user conflicts with a runtime account.")
                    known = True
                    break
        if not known:
            with open("/etc/passwd", "a", encoding="utf-8") as accounts:
                accounts.write(f"{name}:x:{self.uid}:{self.uid}:Library process:/home/{name}:/bin/bash\n")
            with open("/etc/group", "a", encoding="utf-8") as groups:
                groups.write(f"{name}:x:{self.uid}:\n")
        # A valid account without any password: sudo's account check passes, and no password can ever log in.
        with open("/etc/shadow", encoding="utf-8") as shadow:
            for entry in shadow:
                if entry.split(":", 1)[0] == name:
                    return
        with open("/etc/shadow", "a", encoding="utf-8") as shadow:
            shadow.write(f"{name}:*:0:0:99999:7:::\n")

    def write_settings(self):
        if "settingsJson" not in self.definition:
            return
        temporary = self.settings.with_suffix(".tmp")
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o640)
        with os.fdopen(descriptor, "wb") as output:
            os.fchown(output.fileno(), 0, self.uid)
            os.fchmod(output.fileno(), 0o640)
            output.write(self.definition["settingsJson"].encode())
        os.replace(temporary, self.settings)

    def start(self):
        with self.gate:
            if self.process is not None and self.process.poll() is None:
                return
            environment = {
                "HOME": str(self.home), "USER": self.definition["id"],
                "LOGNAME": self.definition["id"], "LANG": "C.UTF-8",
                "PATH": str(self.home / ".local/bin") + ":/usr/local/bin:/usr/bin:/bin",
                "TERM": "xterm-256color", "HOLODECK_SERVICE_TOKEN": self.definition["serviceToken"],
            }
            environment.update(self.definition.get("environment", {}))
            directory = self.home
            if self.agent:
                environment["HOLODECK_TABLE"] = str(self.table)
                environment["HOLODECK_SETTINGS"] = str(self.settings)
                directory = self.table
            mask = "007" if self.definition.get("platformGid") is not None else "077"
            privilege = "sudo" if self.sudo else "locked"
            command = ["/usr/bin/python3", "-I", LAUNCHER, str(self.uid), str(directory), mask, privilege] + self.definition["command"]
            descriptor = os.open(self.log, os.O_WRONLY | os.O_APPEND | os.O_NOFOLLOW)
            with os.fdopen(descriptor, "ab", buffering=0) as output:
                self.process = subprocess.Popen(command, env={"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8"}, stdin=subprocess.PIPE, stdout=output, stderr=output, start_new_session=True)
                self.process.stdin.write(json.dumps(environment).encode())
                self.process.stdin.close()
                self.started_at = datetime.now(timezone.utc).isoformat()

    def stop(self):
        with self.gate:
            self.signal_owned(signal.SIGTERM)
            deadline = time.monotonic() + STOP_GRACE_SECONDS
            while self.owned_pids() and time.monotonic() < deadline:
                time.sleep(0.05)
            self.signal_owned(signal.SIGKILL)
            if self.process is not None:
                try:
                    self.process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    pass

    def reset_home(self):
        with self.gate:
            if self.owned_pids():
                raise ValueError("Stop the agent before resetting its home.")
            shutil.rmtree(self.home)
            self.prepare_private(self.home)

    # The workload's own processes, including a running sudo, and root commands it started through sudo, which sudo
    # marks with the workload's uid.
    def owned_pids(self):
        result = []
        marker = f"SUDO_UID={self.uid}".encode()
        for entry in Path("/proc").iterdir():
            if not entry.name.isdecimal():
                continue
            try:
                status = (entry / "status").read_text()
                real_uid = int(status.split("\nUid:\t", 1)[1].split("\t", 1)[0])
                owned = real_uid == self.uid
                if not owned and real_uid == 0:
                    owned = marker in (entry / "environ").read_bytes().split(b"\0")
                # Zombies have exited; waiting for init to reap them is not a running process.
                if owned and "\nState:\tZ" not in status:
                    result.append(int(entry.name))
            except (FileNotFoundError, ProcessLookupError, PermissionError, IndexError, ValueError):
                continue
        return result

    def signal_owned(self, number):
        for pid in self.owned_pids():
            try:
                os.kill(pid, number)
            except ProcessLookupError:
                pass

    def logs(self, offset):
        descriptor = os.open(self.log, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(descriptor, "rb") as source:
            length = os.fstat(source.fileno()).st_size
            if offset < 0 or offset > length:
                offset = 0
            source.seek(offset)
            contents = source.read(LOG_CHUNK_BYTES)
            return {"text": contents.decode("utf-8", errors="replace"), "offset": offset + len(contents)}

    def view(self):
        code = None if self.process is None else self.process.poll()
        return {"id": self.definition["id"], "uid": self.uid, "sudo": self.sudo,
                "running": self.process is not None and code is None, "exitCode": code, "startedAt": self.started_at}


class SudoGrants:
    """Which workloads may use sudo. A change applies to new sudo commands at once and to the launch on restart."""

    def __init__(self, processes):
        self.processes = processes
        self.granted = set()
        if SUDO_STATE.exists():
            self.granted = set(json.loads(SUDO_STATE.read_text())["processes"])

    def replace(self, identities):
        if not isinstance(identities, list) or any(not isinstance(value, str) or not re.fullmatch(PROCESS_ID, value) for value in identities):
            raise ValueError("Sudo grants must be a list of process identities.")
        self.granted = set(identities)
        temporary = SUDO_STATE.with_suffix(".tmp")
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
        with os.fdopen(descriptor, "w") as output:
            json.dump({"processes": sorted(self.granted)}, output)
        os.replace(temporary, SUDO_STATE)
        self.apply()

    def apply(self):
        wanted = set()
        for identity, process in self.processes.items():
            process.sudo = identity in self.granted
            if process.sudo:
                wanted.add(SUDOERS_PREFIX + str(process.uid))
                self.write_entry(process.uid)
        for entry in SUDOERS.iterdir():
            if entry.name.startswith(SUDOERS_PREFIX) and entry.name not in wanted:
                entry.unlink()

    @staticmethod
    def write_entry(uid):
        target = SUDOERS / (SUDOERS_PREFIX + str(uid))
        temporary = SUDOERS / (".tmp-" + str(uid))
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o440)
        with os.fdopen(descriptor, "w") as output:
            os.fchown(output.fileno(), 0, 0)
            os.fchmod(output.fileno(), 0o440)
            output.write(f"#{uid} ALL=(ALL:ALL) NOPASSWD: ALL\n")
        os.replace(temporary, target)


class ProcessRegistry:
    def __init__(self, processes):
        self.processes = processes
        self.gate = threading.RLock()
        self.path = STATE / "agents.json"
        self.saved = {"nextUid": AGENT_USER_BASE, "agents": {}}
        if self.path.exists():
            self.saved = json.loads(self.path.read_text())
        for identity, entry in self.saved["agents"].items():
            if not entry.get("deleted", False):
                processes[identity] = ManagedProcess(entry["definition"], entry["uid"], True)

    def register(self, definition):
        identity = definition.get("id", "")
        if not re.fullmatch(r"agent-[A-Za-z0-9_-]{1,100}", identity):
            raise ValueError("Invalid agent process identity.")
        command = definition.get("command")
        if not isinstance(command, list) or not command or any(not isinstance(value, str) or "\x00" in value for value in command):
            raise ValueError("A declared agent command is required.")
        if not isinstance(definition.get("environment"), dict) or not isinstance(definition.get("platformGid"), int):
            raise ValueError("An agent environment and platform group are required.")
        with self.gate:
            existing = self.saved["agents"].get(identity)
            if existing is not None and not existing.get("deleted", False):
                prior = self.processes[identity]
                old_definition = dict(prior.definition)
                old_definition.pop("settingsJson", None)
                new_definition = dict(definition)
                new_definition.pop("settingsJson", None)
                new_definition["serviceToken"] = prior.definition["serviceToken"]
                if old_definition != new_definition:
                    raise ValueError("An existing agent launch cannot be changed implicitly.")
                prior.definition["settingsJson"] = definition.get("settingsJson", "{}")
                prior.write_settings()
                self.save()
                return prior
            if identity in self.processes:
                raise ValueError("An agent cannot replace a declared service.")
            uid = self.saved["nextUid"]
            self.saved["nextUid"] += 1
            definition["serviceToken"] = secrets.token_hex(32)
            process = ManagedProcess(definition, uid, True)
            self.processes[identity] = process
            self.saved["agents"][identity] = {"uid": uid, "definition": definition}
            self.save()
            return process

    def remove(self, identity):
        with self.gate:
            if identity not in self.saved["agents"]:
                raise ValueError("Only registered agents can be removed.")
            self.processes[identity].stop()
            del self.processes[identity]
            self.saved["agents"][identity]["deleted"] = True
            self.save()

    def save(self):
        temporary = self.path.with_suffix(".tmp")
        descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
        with os.fdopen(descriptor, "w") as output:
            json.dump(self.saved, output)
        os.replace(temporary, self.path)


def prepare_layout():
    for directory, mode in ((HOMES, 0o755), (TABLES, 0o755), (STATE, 0o711), (SETTINGS, 0o711), (LOGS, 0o700)):
        ManagedProcess.prepare_directory(directory, 0, mode)


def main():
    os.umask(0o077)
    config = json.loads(CONFIG_PATH.read_text())
    prepare_layout()
    processes = {}
    for index, definition in enumerate(config["processes"]):
        processes[definition["id"]] = ManagedProcess(definition, USER_BASE + index)

    registry = ProcessRegistry(processes)
    sudo = SudoGrants(processes)
    sudo.apply()

    class ControlHandler(BaseHTTPRequestHandler):
        def log_message(self, format, *args):
            # Never retain request headers or bearer credentials in logs.
            pass

        def authorized(self):
            actual = self.headers.get("Authorization", "")
            return hmac.compare_digest(actual, "Bearer " + config["controlToken"])

        def respond(self, status, value):
            body = json.dumps(value).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if not self.authorized():
                self.respond(403, {"error": "Platform authorization required."})
                return
            target = urlsplit(self.path)
            if target.path == "/processes":
                with registry.gate:
                    self.respond(200, {"processes": [process.view() for process in processes.values()]})
                return
            parts = target.path.strip("/").split("/")
            if len(parts) == 3 and parts[0] == "processes" and parts[1] in processes and parts[2] == "logs":
                try:
                    offset = int(parse_qs(target.query).get("offset", ["0"])[0])
                except ValueError:
                    self.respond(400, {"error": "Log offset must be an integer."})
                    return
                self.respond(200, processes[parts[1]].logs(offset))
                return
            self.respond(404, {"error": "Unknown control operation."})

        def do_POST(self):
            if not self.authorized():
                self.respond(403, {"error": "Platform authorization required."})
                return
            if self.path == "/processes/register":
                try:
                    length = int(self.headers.get("Content-Length", "0"))
                    if length <= 0 or length > MAX_REGISTRATION_BYTES:
                        raise ValueError("Invalid registration length.")
                    definition = json.loads(self.rfile.read(length))
                    with registry.gate:
                        process = registry.register(definition)
                        sudo.apply()
                    self.respond(200, process.view())
                except (ValueError, TypeError, KeyError):
                    self.respond(409, {"error": "The agent registration is invalid or conflicts with its existing launch."})
                return
            if self.path == "/sudo":
                try:
                    length = int(self.headers.get("Content-Length", "0"))
                    if length <= 0 or length > MAX_REGISTRATION_BYTES:
                        raise ValueError("Invalid sudo grant length.")
                    with registry.gate:
                        sudo.replace(json.loads(self.rfile.read(length)).get("processes"))
                        self.respond(200, {"processes": [process.view() for process in processes.values()]})
                except (ValueError, TypeError, AttributeError):
                    self.respond(400, {"error": "The sudo grants are invalid."})
                return
            parts = self.path.strip("/").split("/")
            if len(parts) != 3 or parts[0] != "processes" or parts[1] not in processes or parts[2] not in ("start", "stop", "delete", "reset-home"):
                self.respond(404, {"error": "Unknown control operation."})
                return
            process = processes[parts[1]]
            try:
                if parts[2] == "start":
                    process.start()
                elif parts[2] == "stop":
                    process.stop()
                elif parts[2] == "reset-home":
                    if parts[1] not in registry.saved["agents"]:
                        raise ValueError("Only an agent home can be reset.")
                    process.reset_home()
                else:
                    registry.remove(parts[1])
                self.respond(200, process.view())
            except Exception:
                self.respond(500, {"error": "Declared process operation failed."})

    server = ThreadingHTTPServer(("0.0.0.0", CONTROL_PORT), ControlHandler)
    for definition in config["processes"]:
        processes[definition["id"]].start()
    stopping = threading.Event()

    def stop_all(number, frame):
        if stopping.is_set():
            return
        stopping.set()

        def shutdown():
            workers = []
            with registry.gate:
                snapshot = list(processes.values())
            for process in snapshot:
                worker = threading.Thread(target=process.stop)
                worker.start()
                workers.append(worker)
            for worker in workers:
                worker.join()
            server.shutdown()
        threading.Thread(target=shutdown).start()

    signal.signal(signal.SIGTERM, stop_all)
    signal.signal(signal.SIGINT, stop_all)
    server.serve_forever()
    server.server_close()


if __name__ == "__main__":
    main()
