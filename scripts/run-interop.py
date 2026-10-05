#!/usr/bin/env python3
"""Run C# network tests against the pinned, independently maintained Muse oracle.

Only an ephemeral loopback server and synthetic credentials are used. Provider
credentials are removed from the child environment. This is not live Muse E2E.
"""
import asyncio
import hashlib
import json
import os
from pathlib import Path
import sys
import struct
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REVISION = "74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff"
CACHE = ROOT / "artifacts" / "oracle"
FILES = ["__init__.py", "_proto.py", "envelope.py", "framing.py", "noise_xx.py", "transport.py"]


def prepare():
    package = CACHE / "musegadget" / "noise"
    package.mkdir(parents=True, exist_ok=True)
    (CACHE / "musegadget" / "__init__.py").touch()
    evidence = {}
    for name in FILES:
        path = package / name
        url = f"https://raw.githubusercontent.com/facebookincubator/muse-gadget-sdk/{REVISION}/linux/src/musegadget/noise/{name}"
        # Never execute mutable main or a caller-selected URL.
        data = urllib.request.urlopen(url, timeout=20).read(200000)
        path.write_bytes(data)
        evidence[name] = hashlib.sha256(data).hexdigest()
    license_data = urllib.request.urlopen(f"https://raw.githubusercontent.com/facebookincubator/muse-gadget-sdk/{REVISION}/LICENSE", timeout=20).read(50000)
    (CACHE / "LICENSE").write_bytes(license_data)
    (CACHE / "provenance.json").write_text(json.dumps({"revision": REVISION, "sha256": evidence}, indent=2))
    sys.path.insert(0, str(CACHE))


async def run():
    prepare()
    from websockets.asyncio.server import serve
    from websockets.exceptions import ConnectionClosed
    from musegadget.noise.noise_xx import NoiseXXResponder
    from musegadget.noise.framing import NoiseFrameDecoder, encode_noise_frames
    from musegadget.noise.transport import decode_request_envelope, encode_response_envelope
    from musegadget.noise.envelope import ServiceFrame, ApplicationResponse, BodyChunk, Reset, ResetCode

    counts = {"handshakes": 0, "requests": 0, "resets": 0, "deniedCommands": 0}
    errors = []

    async def handler(ws):
        if ws.request.headers.get("Authorization") != "Bearer loopback-only":
            await ws.close(1008, "Synthetic credentials required")
            return
        try:
            handshake = NoiseXXResponder()
            handshake.initialize()
            second = handshake.read_message1_and_write_message2(await ws.recv())
            await ws.send([second[:7], second[7:]])
            handshake.read_message3(await ws.recv())
            send, receive = handshake.split()
            counts["handshakes"] += 1
            decoder = NoiseFrameDecoder()
            uploads = {}
            controls = {}
            subscriptions = []

            async def emit(frame, tamper=False):
                for plain in encode_noise_frames(encode_response_envelope(frame)):
                    encrypted = send.encrypt_with_ad(b"", plain)
                    if tamper:
                        encrypted = bytes([encrypted[0] ^ 1]) + encrypted[1:]
                    # Exercise WebSocket fragmentation independently of protocol chunks.
                    await ws.send([encrypted[:11], encrypted[11:]])

            async for encrypted in ws:
                assembled = decoder.decode(receive.decrypt_with_ad(b"", encrypted))
                if assembled is None:
                    continue
                frame = decode_request_envelope(assembled)
                sid = frame.stream_id
                if frame.kind == "reset":
                    counts["resets"] += 1
                    uploads.pop(sid, None)
                    if sid in subscriptions:
                        subscriptions.remove(sid)
                    continue
                if frame.kind == "body_chunk" and sid in controls:
                    controls[sid].extend(frame.value.data)
                    data = controls[sid]
                    while len(data) >= 4:
                        length = struct.unpack_from('<I', data)[0]
                        if len(data) < length + 4:
                            break
                        message = json.loads(bytes(data[4:4 + length]))
                        del data[:4 + length]
                        if message['method'] == 'link.register':
                            assert message['params']['commands_v2'] == {}
                            reply = json.dumps({'id': message['id'], 'result': {'ok': True}}).encode()
                            invoke = json.dumps({'id': 'denied-fixture', 'method': 'link.invoke', 'command': 'system.run', 'params': {'command': 'never execute this'}}).encode()
                            wire = struct.pack('<I', len(reply)) + reply + struct.pack('<I', len(invoke)) + invoke
                            for start in range(0, len(wire), 5):
                                await emit(ServiceFrame.body_chunk(sid, BodyChunk(data=wire[start:start + 5])))
                        elif message['method'] == 'link.result':
                            assert message['id'] == 'denied-fixture' and message['ok'] is False
                            counts['deniedCommands'] += 1
                    continue
                if frame.kind == "body_chunk":
                    uploads[sid].extend(frame.value.data)
                    if frame.value.end_body:
                        await emit(ServiceFrame.response(sid, ApplicationResponse(status=200, body=bytes(uploads.pop(sid)), end_body=True)))
                    continue
                assert frame.kind == "request"
                request = frame.value
                counts["requests"] += 1
                if request.path == "/link-control":
                    controls[sid] = bytearray()
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200)))
                elif request.path == "/denials":
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200, body=str(counts['deniedCommands']).encode(), end_body=True)))
                elif request.path == "/wait":
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200)))
                elif request.path == "/upload":
                    uploads[sid] = bytearray(request.body)
                elif request.path == "/reject":
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=403, body=b"not included in client errors", end_body=True)))
                elif request.path == "/reset":
                    await emit(ServiceFrame.reset(sid, Reset(code=ResetCode.CANCELLED)))
                elif request.path == "/tamper":
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200, end_body=True)), tamper=True)
                elif request.path == "/oversize":
                    await ws.send(b"x" * 65536)
                elif request.path == "/flood":
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200)))
                    for _ in range(32):
                        await emit(ServiceFrame.body_chunk(sid, BodyChunk(data=b"f" * 9000)))
                elif request.path == "/chat/subscribe":
                    subscriptions.append(sid)
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200)))
                elif request.path == "/chat/stream":
                    message = json.loads(request.body)
                    assert message["output_modality"] == "text"
                    assert message["session_id"] == "isolated-test-chat"
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200, body=b'{"accepted":true}', end_body=True)))
                    for sub in subscriptions:
                        data = ('{"type":"test.event","text":"Привет 🌍"}\n' + '{"type":"test.done"}\n').encode()
                        for start in range(0, len(data), 3):
                            await emit(ServiceFrame.body_chunk(sub, BodyChunk(data=data[start:start + 3])))
                else:
                    body = str(counts["resets"]).encode() if request.path == "/stats" else request.body
                    await emit(ServiceFrame.response(sid, ApplicationResponse(status=200, body=body, end_body=True)))
        except ConnectionClosed:
            pass
        except Exception as error:
            # Do not include provider data or arbitrary peer bytes in test logs.
            errors.append(type(error).__name__)

    environment = {key: value for key, value in os.environ.items() if not key.startswith("MUSE_")}
    async with serve(handler, "127.0.0.1", 0, max_size=65535, ping_interval=None, close_timeout=1) as server:
        port = server.sockets[0].getsockname()[1]
        environment["MUSE_TEST_ENDPOINT"] = f"ws://127.0.0.1:{port}/v1/noise?vm_id=fixture"
        command = sys.argv[1:] or ["dotnet", "test", "Muse.slnx", "-c", "Release", "--no-build", "--no-restore", "--filter", "TestCategory=Interop", "--logger", "console;verbosity=normal"]
        process = await asyncio.create_subprocess_exec(*command, cwd=ROOT, env=environment)
        try:
            result = await asyncio.wait_for(process.wait(), timeout=180)
        except TimeoutError:
            process.kill()
            await process.wait()
            raise
    print(json.dumps({"oracleRevision": REVISION, "scope": "loopback interoperability, not provider E2E", **counts, "errors": errors}))
    # A selected negative-handshake test may correctly establish no session.
    # The default complete suite must demonstrate successful sessions too.
    if errors or (not sys.argv[1:] and counts["handshakes"] == 0):
        return 1
    return result


if __name__ == "__main__":
    raise SystemExit(asyncio.run(run()))
