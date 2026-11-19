"""A manager-owned PTY session. Browser connections are disposable observers.

A shell has one terminal size. The connection that last asked for its own size owns it; every other connection
shows the same grid. Retained output is kept in chunks tagged with the size it was printed at, so a replay passes
through the same sizes as the live terminal did."""

import asyncio
import codecs
from collections import deque
from dataclasses import dataclass
import fcntl
import os
import signal
import struct
import termios
import uuid

SCROLLBACK_CHARACTERS = 262144
SUBSCRIBER_MESSAGES = 32
OUTPUT_READS_PER_TICK = 16
MAX_PENDING_INPUT_BYTES = 262144
MAX_COLUMNS = 500
MAX_ROWS = 300


def terminal_size(cols, rows):
    if not isinstance(cols, int) or isinstance(cols, bool) or not 2 <= cols <= MAX_COLUMNS:
        raise ValueError(f"Columns must be between 2 and {MAX_COLUMNS}.")
    if not isinstance(rows, int) or isinstance(rows, bool) or not 2 <= rows <= MAX_ROWS:
        raise ValueError(f"Rows must be between 2 and {MAX_ROWS}.")
    return struct.pack("HHHH", rows, cols, 0, 0)


def prepare_terminal():
    os.setsid()
    fcntl.ioctl(0, termios.TIOCSCTTY, 0)


def signal_session(session_id, signum):
    # Bash gives foreground/background jobs different process groups in one
    # terminal session. Signal all of them, not only the shell's own group.
    groups = set()
    for entry in os.scandir("/proc"):
        if not entry.name.isdigit():
            continue
        try:
            with open(entry.path + "/stat", encoding="utf-8") as source:
                record = source.read()
            fields = record[record.rfind(")") + 2:].split()
            if int(fields[3]) == session_id:
                groups.add(int(fields[2]))
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue
    for group in groups:
        try:
            os.killpg(group, signum)
        except ProcessLookupError:
            pass


@dataclass
class OutputChunk:
    cols: int
    rows: int
    parts: list
    length: int = 0

    def text(self):
        return "".join(self.parts)


@dataclass
class SessionView:
    id: str
    name: str
    running: bool
    exitCode: int | None
    windowId: str | None


class TerminalSession:
    def __init__(self, name, window_id=None):
        self.id = uuid.uuid4().hex
        self.name = name
        self.window_id = window_id
        self.process = None
        self.master = None
        self.exit_code = None
        self.history = deque()
        self.history_length = 0
        self.cols = 0
        self.rows = 0
        self.owner = None
        self.subscribers = set()
        self.decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.exited = None
        self.reader_active = False
        self.closed = False
        self.pending_input = bytearray()

    async def start(self, cols, rows):
        size = terminal_size(cols, rows)
        self.cols = cols
        self.rows = rows
        master, slave = os.openpty()
        self.master = master
        try:
            fcntl.ioctl(slave, termios.TIOCSWINSZ, size)
            environment = {}
            for key, value in os.environ.items():
                if not key.startswith("HOLODECK_"):
                    environment[key] = value
            environment["TERM"] = "xterm-256color"
            environment["COLORTERM"] = "truecolor"
            self.process = await asyncio.create_subprocess_exec("/bin/bash", "--noprofile", "-i", stdin=slave, stdout=slave, stderr=slave, env=environment, preexec_fn=prepare_terminal)
        except BaseException:
            os.close(master)
            self.master = None
            raise
        finally:
            os.close(slave)
        os.set_blocking(master, False)
        asyncio.get_running_loop().add_reader(master, self.read_output)
        self.reader_active = True
        self.exited = asyncio.create_task(self.wait_for_exit())

    def view(self):
        return SessionView(self.id, self.name, self.process.returncode is None, self.exit_code, self.window_id)

    def publish(self, message):
        for queue in tuple(self.subscribers):
            if queue.full():
                # Drop the subscriber, never stop draining the shell's PTY.
                while not queue.empty():
                    queue.get_nowait()
                queue.put_nowait({"type": "disconnected", "message": "Terminal output exceeded the connection buffer. Reconnect to continue."})
                self.subscribers.discard(queue)
            else:
                queue.put_nowait(message)

    def retain(self, text):
        if not text:
            return
        if not self.history or self.history[-1].cols != self.cols or self.history[-1].rows != self.rows:
            self.history.append(OutputChunk(self.cols, self.rows, []))
        latest = self.history[-1]
        latest.parts.append(text)
        latest.length += len(text)
        self.history_length += len(text)
        self.trim_history()
        self.publish({"type": "output", "data": text})

    def trim_history(self):
        while self.history_length > SCROLLBACK_CHARACTERS:
            excess = self.history_length - SCROLLBACK_CHARACTERS
            oldest = self.history[0]
            if oldest.length <= excess:
                self.history.popleft()
                self.history_length -= oldest.length
                continue
            # Start the kept output at a line boundary, not inside a line or an escape sequence.
            text = oldest.text()
            start = text.find("\n", excess)
            start = len(text) if start < 0 else start + 1
            oldest.parts = [text[start:]]
            oldest.length = len(text) - start
            self.history_length -= start
            if oldest.length == 0:
                self.history.popleft()

    def replay(self, connection):
        chunks = []
        for chunk in self.history:
            chunks.append({"cols": chunk.cols, "rows": chunk.rows, "data": chunk.text()})
        return {"type": "replay", "connection": connection, "owner": self.owner, "cols": self.cols, "rows": self.rows, "chunks": chunks}

    def size_message(self):
        return {"type": "size", "cols": self.cols, "rows": self.rows, "owner": self.owner}

    def read_output(self):
        for _ in range(OUTPUT_READS_PER_TICK):
            if self.master is None:
                return
            try:
                data = os.read(self.master, 16384)
            except BlockingIOError:
                return
            except OSError:
                data = b""
            if not data:
                self.stop_reader()
                return
            self.retain(self.decoder.decode(data))

    def stop_reader(self):
        if self.reader_active:
            asyncio.get_running_loop().remove_reader(self.master)
            self.reader_active = False

    async def wait_for_exit(self):
        self.exit_code = await self.process.wait()
        self.read_output()
        self.stop_reader()
        self.retain(self.decoder.decode(b"", final=True))
        self.publish({"type": "exit", "code": self.exit_code})

    def subscribe(self, connection):
        queue = asyncio.Queue(maxsize=SUBSCRIBER_MESSAGES)
        self.subscribers.add(queue)
        # No await between snapshot and subscription: replay precedes live output.
        queue.put_nowait(self.replay(connection))
        if self.exit_code is not None:
            queue.put_nowait({"type": "exit", "code": self.exit_code})
        return queue

    def write(self, text):
        if self.closed or self.process.returncode is not None:
            return
        data = text.encode("utf-8")
        if len(self.pending_input) + len(data) > MAX_PENDING_INPUT_BYTES:
            raise ValueError("Terminal input is arriving too quickly.")
        self.pending_input.extend(data)
        self.flush_input()

    def flush_input(self):
        if self.master is None:
            return
        try:
            while self.pending_input:
                count = os.write(self.master, self.pending_input)
                del self.pending_input[:count]
        except BlockingIOError:
            asyncio.get_running_loop().add_writer(self.master, self.flush_input)
            return
        except OSError:
            self.pending_input.clear()
        asyncio.get_running_loop().remove_writer(self.master)

    def claim(self, connection, cols, rows):
        """Size the shell for this connection's screen and make it the owner."""
        size = terminal_size(cols, rows)
        if self.owner == connection and self.cols == cols and self.rows == rows:
            return
        self.owner = connection
        if self.cols != cols or self.rows != rows:
            self.cols = cols
            self.rows = rows
            if self.master is not None:
                fcntl.ioctl(self.master, termios.TIOCSWINSZ, size)
        # Published before the shell's redraw is read, so every view resizes before drawing it.
        self.publish(self.size_message())

    def release(self, connection):
        if self.owner != connection:
            return
        self.owner = None
        self.publish(self.size_message())

    async def close(self):
        if self.closed:
            return
        self.closed = True
        signal_session(self.process.pid, signal.SIGHUP)
        if self.process.returncode is None:
            try:
                await asyncio.wait_for(asyncio.shield(self.exited), 2)
            except asyncio.TimeoutError:
                signal_session(self.process.pid, signal.SIGKILL)
                await self.exited
        else:
            await self.exited
        # End jobs that ignored the hangup even if Bash exited immediately.
        signal_session(self.process.pid, signal.SIGKILL)
        if self.master is not None:
            self.stop_reader()
            asyncio.get_running_loop().remove_writer(self.master)
            os.close(self.master)
            self.master = None
        self.publish({"type": "closed"})
