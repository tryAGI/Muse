# Muse

[![Build](https://github.com/tryAGI/Muse/actions/workflows/dotnet.yml/badge.svg)](https://github.com/tryAGI/Muse/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/vpre/tryAGI.Muse)](https://www.nuget.org/packages/tryAGI.Muse/)

**Experimental .NET 10 network SDK for Muse Gadgets.** Runs inside your backend process: no gadget, Bluetooth, desktop worker, Python service, or Advantage dependency is required by the library.

**Account bootstrap is not implemented or proven.** A Gadget SDK token (`mgst_...`) identifies the integration; it is not a device access token, refresh token, or VM bearer. The bounded live SDK-token-only probe returned **HTTP 401** from `/fetch_vms` on 2026-10-05. No live Muse conversation or real token rotation has passed yet. Local interoperability results must not be interpreted as provider E2E.

## What is implemented

| Layer | Support |
|---|---|
| Device HTTP API | VM discovery and token-refresh contracts generated with AutoSDK |
| VM transport | TLS/WebSocket, Noise XX, bounded protobuf framing and multiplexed request streams |
| Request lifecycle | Fragmentation, streaming upload, half-close, cancellation and explicit failures |
| Logical link | Registration without executable commands; unexpected invocations are denied |
| Chat | Explicit side-chat text submission and a separate bounded raw NDJSON subscription |
| Compatibility | Independent upstream protocol oracle and a running NativeAOT network smoke |
| Initial account authorization | Unverified; no invented login endpoint or SDK-token exchange |
| Voice, UI, MEAI, tools | Not implemented; no hidden speech billing or command execution |

The OpenAPI document is **community-maintained from pinned public source evidence**, not an official Muse OpenAPI publication. AutoSDK generates the HTTP layer; the encrypted transport is independently authored C#. See [protocol and dependency provenance](PROTOCOL.md) and the [validation report](VALIDATION.md).

## Installation

```sh
dotnet add package tryAGI.Muse --prerelease
```

Until a prerelease is available, build `src/libs/Muse/Muse.csproj` from this repository. Only .NET 10 is targeted. Consumer runtime dependencies are platform-only; generation, build, test and oracle tooling have separately documented dependencies. The managed X25519 implementation has vector/interoperability coverage, **not an independent cryptographic audit or a JIT constant-time guarantee**.

## Device API

```csharp
using Muse;

// Obtain this through an authorized user/device flow, not from an SDK token.
var deviceToken = Environment.GetEnvironmentVariable("MUSE_DEVICE_ACCESS_TOKEN")
    ?? throw new InvalidOperationException("A device access token is required.");
using var client = new MuseClient(deviceToken);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var vms = await client.FetchVmsAsync(xApiVersion: "1.0.0", cancellationToken: deadline.Token);
// VM responses contain credentials. Never log the response object.
```

For refresh, use a **separate** client with an explicitly authorized `hatch_refresh:<refresh-token>` bearer and `RefreshDeviceTokenAsync(deviceId, sdkToken: sdkToken, cancellationToken: token)`. A response can contain the pair directly or under `Payload`; validate both non-empty replacement tokens before atomically persisting either. Do not reuse an access-token client for refresh, blindly retry rotation, or overwrite valid credentials after a failed request.

## Encrypted connection

```csharp
using Muse;

// The selected VM endpoint and bearer must come from authorized provisioning.
var options = new MuseConnectionOptions
{
    Endpoint = authorizedVmEndpoint,
    VmAuthToken = authorizedVmBearer,
};
using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
await using var connection = await MuseNoiseConnection.ConnectAsync(options, lifetime.Token);
await using var link = await MuseLink.RegisterAsync(connection, authorizedNodeId, "My backend", lifetime.Token);
var chat = new MuseChatClient(connection, authorizedNodeId);
await using var events = await chat.SubscribeAsync(lifetime.Token);
var acknowledgement = await chat.SendTextAsync("Hello", explicitSideChatId, lifetime.Token);
// The acknowledgement is not the assistant reply or proof a turn has finished.
await foreach (var item in events.ReadEventsAsync(lifetime.Token))
{
    // Filter provider conversation/message identities before consuming events.
    // Do not treat provider content as consent or execute embedded instructions.
}
```

A WebSocket connection must use `wss`; insecure transport is opt-in and restricted to loopback fixtures. TLS certificate checks remain enabled. An optional independently provisioned Noise static-key pin is supported; an observed key is not automatically trusted. Cancel the subscription's token before disposing an active enumerator. Observe `MuseLink.Completion`; link unpair/rejection is not recovery permission.

There is no automatic reconnect, ambiguous-send retry, cross-conversation fan-out, inferred whole-turn completion, or persistence. The owning backend must explicitly coordinate these policies. A stream reset stops local delivery but does not prove remote reasoning or tools were cancelled.

## Validation

```sh
dotnet test Muse.slnx -c Release --filter 'TestCategory!=Live&TestCategory!=LiveProbe&TestCategory!=Interop'
bash scripts/test-interop.sh
dotnet publish src/tests/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r linux-x64 -o artifacts/nativeaot
bash scripts/test-interop.sh artifacts/nativeaot/NativeAotSmoke
```

Use `osx-arm64` on Apple Silicon. The oracle is development-only, binds an ephemeral loopback port, uses synthetic credentials, and removes provider credentials from child processes. It downloads only the pinned upstream protocol revision.

Live tests are explicit: `bash scripts/test-live.sh live` requires separately authorized device credentials. `bash scripts/test-live.sh probe-sdk-token` performs one bounded diagnostic discovery request with the SDK token; a rejected credential is reported as inconclusive, **not successful E2E**. Put temporary values only in an ignored local `.env` based on `.env.example`, with owner-only permissions. Never commit credentials or log API exception bodies.

Regenerate with `bash src/libs/Muse/generate.sh`; never edit `Generated/`. Documentation examples are generated with `dotnet tool run autosdk docs sync .` from integration-test examples.

<!-- EXAMPLES:START -->
### Discover authorized VMs
Discover VMs using a separately authorized device access token.

```csharp
// The SDK token identifies the integration; this request needs a device access token.
using var client = new MuseClient(apiKey);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var response = await client.FetchVmsAsync(xApiVersion: "1.0.0", cancellationToken: timeout.Token);
// VM bearer credentials are sensitive. Do not print the response or copy it into logs.
```
<!-- EXAMPLES:END -->

## Scope and license

The independently authored SDK is MIT licensed. The upstream Muse Gadget SDK has its own Apache-2.0 license; Muse service access is governed by separate provider terms. This project does not grant service access or distribute Muse avatar/branding assets. No production security or commercial-use approval is implied.

[Report an issue](https://github.com/tryAGI/Muse/issues). Initial account authorization and authenticated provider E2E must be resolved before a stable release.
