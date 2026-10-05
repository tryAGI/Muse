# Protocol provenance and scope

Reference revision: facebookincubator/muse-gadget-sdk `74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff` (2026-10-05).

The source OpenAPI document in this repository is community-maintained, not published by Muse. It describes only observed device API operations. There is no invented OAuth/device-login or SDK-token-to-user-token exchange.

Runtime layers are independently authored C# implementations of the observed wire formats. Reference files: `linux/src/musegadget/muse_api.py`, `link_client.py`, `noise/{noise_xx,framing,envelope,transport}.py`, and `esp32/components/muse/muse_chat_session.cpp`. No upstream SDK runtime source is packaged. Muse's upstream code is Apache-2.0; it remains a separately downloaded development oracle. No images, avatars, or branding assets from Muse are included.

The wire stack is TLS + WebSocket `/v1/noise?vm_id=...` with a VM bearer header, `Noise_XX_25519_AESGCM_SHA256`, protobuf transport chunks, request/response envelopes, and multiplexed application streams. Chat uses `/chat/stream` and a distinct `/chat/subscribe` NDJSON stream. Link control is length-prefixed JSON, not MCP.

Noise XX authenticates possession of the negotiated keys, not a pre-known peer identity by itself. Keep TLS certificate validation enabled. An optional expected server static key provides an additional application pin. Unpinned sessions rely on TLS for endpoint identity.

SDK token, device access token, device refresh token and VM token are different credentials. Published gadget setup receives user credentials from the official app. The presence of an SDK token alone does not prove a headless bootstrap path.

## Dependency policy

Consumer runtime: .NET 10 platform only. No native binaries, external cryptography package, Python process, shell executor, Bluetooth dependency or Advantage dependency is shipped. AES-GCM, SHA-256, HMAC and secure random use platform cryptography. The managed X25519 implementation is first-party, follows RFC 7748, and must be independently reviewed before production security claims. Passing vectors and interoperability tests is not a cryptographic audit or a JIT constant-time guarantee.

Tooling inherited from AutoSDK scaffold: AutoSDK CLI, MinVer and DotNet.ReproducibleBuilds. Tests use MSTest, AwesomeAssertions and GitHubActionsTestLogger; the optional oracle uses Python cryptography/websockets in an isolated development environment. These are not consumer runtime dependencies. Restored transitive/package verification is recorded in the validation report.
