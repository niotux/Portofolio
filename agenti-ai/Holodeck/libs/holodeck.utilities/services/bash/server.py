"""Private HTTP/WebSocket terminal manager for the utilities library."""

import argparse
import asyncio
from dataclasses import asdict, dataclass
import hmac
import json
import os
import uuid

from aiohttp import web, WSMsgType

from session import TerminalSession, terminal_size

MAX_SESSIONS = 32
MAX_NAME_LENGTH = 80
MAX_WINDOW_ID_LENGTH = 128
MAX_MESSAGE_BYTES = 65536
MAX_CONNECTIONS_PER_SESSION = 16


@dataclass
class SessionList:
    sessions: list


class TerminalManager:
    def __init__(self, token):
        if not token:
            raise ValueError("HOLODECK_SERVICE_TOKEN is required.")
        self.token = token
        self.sessions = {}
        self.windows = {}
        self.creation_lock = asyncio.Lock()
        self.sockets = set()

    @web.middleware
    async def authorize(self, request, handler):
        expected = "Bearer " + self.token
        supplied = request.headers.get("Authorization", "")
        if not hmac.compare_digest(supplied.encode(), expected.encode()):
            raise web.HTTPUnauthorized(text="Service authorization required.")
        try:
            return await handler(request)
        except (ValueError, json.JSONDecodeError) as error:
            return web.json_response({"error": str(error)}, status=400)

    def application(self):
        app = web.Application(middlewares=[self.authorize], client_max_size=MAX_MESSAGE_BYTES)
        app.router.add_get("/health", self.health)
        app.router.add_get("/sessions", self.list_sessions)
        app.router.add_post("/sessions", self.create_session)
        app.router.add_delete("/sessions/{id}", self.delete_session)
        app.router.add_delete("/sessions/by-window/{windowId}", self.delete_window_session)
        app.router.add_get("/sessions/{id}/terminal", self.terminal)
        app.on_shutdown.append(self.shutdown)
        app.on_cleanup.append(self.cleanup)
        return app

    async def health(self, request):
        return web.json_response({"ready": True})

    async def list_sessions(self, request):
        views = []
        for session in self.sessions.values():
            views.append(asdict(session.view()))
        return web.json_response(asdict(SessionList(views)))

    async def create_session(self, request):
        body = await request.json() if request.can_read_body else {}
        if not isinstance(body, dict):
            raise ValueError("Session settings must be an object.")
        window_id = body.get("windowId")
        if window_id is not None:
            if not isinstance(window_id, str) or not window_id.strip() or len(window_id) > MAX_WINDOW_ID_LENGTH:
                raise ValueError("Window identity must be nonempty text within the identity limit.")
        cols = body.get("cols", 100)
        rows = body.get("rows", 30)
        terminal_size(cols, rows)
        name = body.get("name", "Bash")
        if not isinstance(name, str) or not name.strip() or len(name) > MAX_NAME_LENGTH:
            raise ValueError(f"Session name must contain 1 to {MAX_NAME_LENGTH} characters.")
        async with self.creation_lock:
            if window_id is not None:
                existing = self.windows.get(window_id)
                if existing is not None:
                    return web.json_response(asdict(existing.view()))
            if len(self.sessions) >= MAX_SESSIONS:
                return web.json_response({"error": "End a terminal session before creating another."}, status=409)
            session = TerminalSession(name.strip(), window_id)
            await session.start(cols, rows)
            self.sessions[session.id] = session
            if window_id is not None:
                self.windows[window_id] = session
        return web.json_response(asdict(session.view()), status=201)

    def find(self, request):
        session = self.sessions.get(request.match_info["id"])
        if session is None:
            raise web.HTTPNotFound(text="Terminal session not found.")
        return session

    async def remove_session(self, session):
        await session.close()
        self.sessions.pop(session.id, None)
        if session.window_id is not None:
            self.windows.pop(session.window_id, None)

    async def delete_session(self, request):
        async with self.creation_lock:
            await self.remove_session(self.find(request))
        return web.Response(status=204)

    async def delete_window_session(self, request):
        async with self.creation_lock:
            session = self.windows.get(request.match_info["windowId"])
            if session is not None:
                await self.remove_session(session)
        return web.Response(status=204)

    async def terminal(self, request):
        session = self.find(request)
        if len(session.subscribers) >= MAX_CONNECTIONS_PER_SESSION:
            raise web.HTTPTooManyRequests(text="Too many connections to this terminal session.")
        socket = web.WebSocketResponse(heartbeat=30, max_msg_size=MAX_MESSAGE_BYTES)
        await socket.prepare(request)
        self.sockets.add(socket)
        connection = uuid.uuid4().hex
        queue = session.subscribe(connection)

        async def send_output():
            while True:
                message = await queue.get()
                await socket.send_json(message)
                if message["type"] in ("closed", "disconnected"):
                    await socket.close()
                    return

        sender = asyncio.create_task(send_output())
        try:
            async for incoming in socket:
                if incoming.type != WSMsgType.TEXT:
                    continue
                try:
                    message = json.loads(incoming.data)
                    if not isinstance(message, dict):
                        raise ValueError("Terminal messages must be objects.")
                    kind = message.get("type")
                    if kind == "input":
                        text = message.get("data")
                        if not isinstance(text, str):
                            raise ValueError("Input data must be text.")
                        session.write(text)
                    elif kind == "resize":
                        session.claim(connection, message.get("cols"), message.get("rows"))
                    else:
                        raise ValueError("Unknown terminal message type.")
                except (ValueError, TypeError, json.JSONDecodeError) as error:
                    await socket.send_json({"type": "error", "message": str(error)})
        finally:
            self.sockets.discard(socket)
            session.subscribers.discard(queue)
            session.release(connection)
            sender.cancel()
            await asyncio.gather(sender, return_exceptions=True)
        return socket

    async def shutdown(self, app):
        await asyncio.gather(*(socket.close(code=1001, message=b"Terminal manager stopping") for socket in tuple(self.sockets)))

    async def cleanup(self, app):
        sessions = list(self.sessions.values())
        self.sessions.clear()
        self.windows.clear()
        await asyncio.gather(*(session.close() for session in sessions))


def main():
    parser = argparse.ArgumentParser(description="Utilities Bash session manager")
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    if os.geteuid() == 0:
        raise SystemExit("The terminal manager must run as a non-root library user.")
    manager = TerminalManager(os.environ.get("HOLODECK_SERVICE_TOKEN", ""))
    web.run_app(manager.application(), host="0.0.0.0", port=args.port, print=None)


if __name__ == "__main__":
    main()
