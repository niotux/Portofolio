import asyncio
import os
import re
import unittest

from aiohttp import ClientSession
from aiohttp.test_utils import TestClient, TestServer

from server import TerminalManager


class TerminalTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.manager = TerminalManager("test-private-service-token")
        self.client = TestClient(TestServer(self.manager.application()), headers={"Authorization": "Bearer test-private-service-token"})
        await self.client.start_server()

    async def asyncTearDown(self):
        await self.client.close()

    async def create(self, name="Test"):
        response = await self.client.post("/sessions", json={"name": name, "cols": 90, "rows": 25})
        self.assertEqual(response.status, 201)
        return await response.json()

    async def output_until(self, socket, expected):
        output = ""
        async with asyncio.timeout(5):
            while expected not in output:
                message = await socket.receive_json()
                if message["type"] == "output":
                    output += message["data"]
                if message["type"] == "replay":
                    for chunk in message["chunks"]:
                        output += chunk["data"]
        return output

    async def receive(self, socket, kind):
        async with asyncio.timeout(5):
            while True:
                message = await socket.receive_json()
                if message["type"] == kind:
                    return message

    async def test_disconnect_retains_process_and_replays_output(self):
        session = await self.create()
        path = "/sessions/" + session["id"] + "/terminal"
        socket = await self.client.ws_connect(path)
        await socket.send_json({"type": "input", "data": "printf 'before-%s\\n' reconnect; export HOLODECK_TEST_VALUE=retained\n"})
        await self.output_until(socket, "before-reconnect")
        await socket.close()
        process = self.manager.sessions[session["id"]].process
        self.assertIsNone(process.returncode)
        socket = await self.client.ws_connect(path)
        await self.output_until(socket, "before-reconnect")
        await socket.send_json({"type": "input", "data": "printf 'value-%s\\n' \"$HOLODECK_TEST_VALUE\"\n"})
        await self.output_until(socket, "value-retained")
        await socket.close()

    async def test_resize_and_independent_sessions_and_explicit_end(self):
        first = await self.create("First")
        second = await self.create("Second")
        first_session = self.manager.sessions[first["id"]]
        socket = await self.client.ws_connect("/sessions/" + first["id"] + "/terminal")
        await socket.send_json({"type": "resize", "cols": 123, "rows": 41})
        await socket.send_json({"type": "input", "data": "stty size\n"})
        await self.output_until(socket, "41 123")
        response = await self.client.delete("/sessions/" + first["id"])
        self.assertEqual(response.status, 204)
        self.assertIsNotNone(first_session.process.returncode)
        self.assertNotIn(first["id"], self.manager.sessions)
        self.assertIsNone(self.manager.sessions[second["id"]].process.returncode)
        listing = await (await self.client.get("/sessions")).json()
        self.assertEqual([entry["id"] for entry in listing["sessions"]], [second["id"]])
        await socket.close()

    async def test_one_size_follows_the_screen_that_claims_it(self):
        session = await self.create()
        path = "/sessions/" + session["id"] + "/terminal"
        laptop = await self.client.ws_connect(path)
        laptop_id = (await self.receive(laptop, "replay"))["connection"]
        await laptop.send_json({"type": "resize", "cols": 120, "rows": 40})
        size = await self.receive(laptop, "size")
        self.assertEqual((size["cols"], size["rows"], size["owner"]), (120, 40, laptop_id))
        phone = await self.client.ws_connect(path)
        joined = await self.receive(phone, "replay")
        phone_id = joined["connection"]
        self.assertEqual((joined["cols"], joined["rows"], joined["owner"]), (120, 40, laptop_id))
        await phone.send_json({"type": "resize", "cols": 45, "rows": 20})
        for socket in (laptop, phone):
            size = await self.receive(socket, "size")
            self.assertEqual((size["cols"], size["rows"], size["owner"]), (45, 20, phone_id))
        await laptop.send_json({"type": "input", "data": "stty size\n"})
        await self.output_until(laptop, "20 45")
        await phone.close()
        released = await self.receive(laptop, "size")
        self.assertIsNone(released["owner"])
        self.assertEqual((released["cols"], released["rows"]), (45, 20))
        await laptop.close()

    async def test_replay_keeps_the_size_each_output_was_printed_at(self):
        session = await self.create()
        path = "/sessions/" + session["id"] + "/terminal"
        socket = await self.client.ws_connect(path)
        await socket.send_json({"type": "input", "data": "printf 'wide-%s\\n' output\n"})
        await self.output_until(socket, "wide-output")
        await socket.send_json({"type": "resize", "cols": 40, "rows": 12})
        await socket.send_json({"type": "input", "data": "printf 'narrow-%s\\n' output\n"})
        await self.output_until(socket, "narrow-output")
        await socket.close()
        socket = await self.client.ws_connect(path)
        replay = await self.receive(socket, "replay")
        sizes = []
        for chunk in replay["chunks"]:
            sizes.append((chunk["cols"], chunk["rows"]))
        self.assertEqual(sizes, [(90, 25), (40, 12)])
        self.assertIn("wide-output", replay["chunks"][0]["data"])
        self.assertIn("narrow-output", replay["chunks"][1]["data"])
        self.assertIsNone(replay["owner"])
        await socket.close()

    async def test_window_creation_is_idempotent_and_independent(self):
        first = await self.client.post("/sessions", json={"windowId": "first-window"})
        self.assertEqual(first.status, 201)
        original = await first.json()
        retry = await self.client.post("/sessions", json={"windowId": "first-window"})
        self.assertEqual(retry.status, 200)
        self.assertEqual((await retry.json())["id"], original["id"])
        other = await self.client.post("/sessions", json={"windowId": "second-window"})
        self.assertEqual(other.status, 201)
        self.assertNotEqual((await other.json())["id"], original["id"])
        response = await self.client.delete("/sessions/by-window/first-window")
        self.assertEqual(response.status, 204)
        response = await self.client.delete("/sessions/by-window/first-window")
        self.assertEqual(response.status, 204)
        replacement = await self.client.post("/sessions", json={"windowId": "first-window"})
        self.assertEqual(replacement.status, 201)
        self.assertNotEqual((await replacement.json())["id"], original["id"])

    async def test_explicit_end_terminates_background_jobs(self):
        session = await self.create()
        socket = await self.client.ws_connect("/sessions/" + session["id"] + "/terminal")
        await socket.send_json({"type": "input", "data": "sleep 300 & printf 'job-pid:%s:end\\n' \"$!\"\n"})
        output = await self.output_until(socket, ":end")
        match = re.search(r"job-pid:(\d+):end", output)
        # The command echo includes :end too; continue until the result arrives.
        while match is None:
            output += await self.output_until(socket, ":end")
            match = re.search(r"job-pid:(\d+):end", output)
        child = int(match.group(1))
        self.assertTrue(os.path.exists(f"/proc/{child}"))
        response = await self.client.delete("/sessions/" + session["id"])
        self.assertEqual(response.status, 204)
        async with asyncio.timeout(5):
            while os.path.exists(f"/proc/{child}"):
                with open(f"/proc/{child}/stat") as source:
                    fields = source.read().split()
                if fields[2] == "Z":
                    break
                await asyncio.sleep(0.02)
        await socket.close()

    async def test_authentication_and_input_validation(self):
        async with ClientSession() as anonymous:
            response = await anonymous.get(self.client.make_url("/sessions"))
            self.assertEqual(response.status, 401)
        response = await self.client.post("/sessions", json={"cols": 999999})
        self.assertEqual(response.status, 400)
        self.assertEqual(len(self.manager.sessions), 0)
        session = await self.create()
        socket = await self.client.ws_connect("/sessions/" + session["id"] + "/terminal")
        await socket.send_json({"type": "input", "data": "printf 'service-token:%s:end\\n' \"$HOLODECK_SERVICE_TOKEN\"\n"})
        output = await self.output_until(socket, "service-token::end")
        self.assertNotIn("test-private-service-token", output)
        await socket.close()

    async def test_completed_shell_is_inspectable_and_cleanup_reaps_remaining(self):
        first = await self.create()
        socket = await self.client.ws_connect("/sessions/" + first["id"] + "/terminal")
        await socket.send_json({"type": "input", "data": "exit 7\n"})
        async with asyncio.timeout(5):
            while True:
                message = await socket.receive_json()
                if message["type"] == "exit":
                    self.assertEqual(message["code"], 7)
                    break
        listing = await (await self.client.get("/sessions")).json()
        self.assertFalse(listing["sessions"][0]["running"])
        self.assertEqual(listing["sessions"][0]["exitCode"], 7)
        await socket.close()
        second = await self.create()
        process = self.manager.sessions[second["id"]].process
        await self.manager.cleanup(None)
        self.assertIsNotNone(process.returncode)


if __name__ == "__main__":
    unittest.main()
