"""Trusted UID transition. Workloads reach no control endpoint; only a user-granted workload may use sudo."""
import ctypes
import json
import os
import resource
import sys

PR_SET_NO_NEW_PRIVS = 38
PR_CAPBSET_DROP = 24


SUDO = "sudo"
LOCKED = "locked"


def lock(libc):
    with open("/proc/sys/kernel/cap_last_cap", encoding="ascii") as source:
        last_capability = int(source.read())
    for capability in range(last_capability + 1):
        if libc.prctl(PR_CAPBSET_DROP, capability, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), "Cannot drop capability bounding set")
    if libc.prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), "Cannot disable privilege escalation")


def main():
    uid = int(sys.argv[1])
    if uid < 10000 or os.getuid() != 0:
        raise PermissionError("Only the platform supervisor can assign workload identities.")
    privilege = sys.argv[4]
    if privilege not in (SUDO, LOCKED):
        raise ValueError("Unknown workload privilege.")
    environment = json.load(sys.stdin)
    libc = ctypes.CDLL(None, use_errno=True)
    # A workload the user granted sudo may reach root through sudo; every other workload can never gain privileges.
    if privilege == LOCKED:
        lock(libc)
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    resource.setrlimit(resource.RLIMIT_NOFILE, (1024, 1024))
    resource.setrlimit(resource.RLIMIT_NPROC, (128, 128))
    os.setgroups([])
    os.setgid(uid)
    os.setuid(uid)
    os.chdir(sys.argv[2])
    os.umask(int(sys.argv[3], 8))
    os.execvpe(sys.argv[5], sys.argv[5:], environment)


if __name__ == "__main__":
    main()
