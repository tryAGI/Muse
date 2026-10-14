# Network validation — 2026-10-05

## Authentication follow-up — 2026-10-05 17:29 UTC

The supplied SDK token was confirmed equal to the existing local secret without publishing either its value or fingerprint. The file remains ignored and owner-readable only. No whitespace or encoding change was observed.

| Probe | Observed result |
|---|---|
| Published Python `fetch_vms_with_status` | HTTP 401; no VMs |
| Generated C# client with corrected default headers | HTTP 401 |
| Generated C# with the same Python User-Agent and API-version header | HTTP 401 |
| SDK token validity for its intended gadget-pairing role | Not established by these discovery probes |
| Authenticated real Muse conversation | Still not exercised |

Python executed the unmodified API module at upstream revision `74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff`, Git blob `d93e1fffdfdbb415720577c3ee1aaa8186d3d786`. Only the package-version import was supplied as `auth-diagnostic` to avoid installing BLE/device-executor dependencies. The emitted User-Agent was `musegadget/auth-diagnostic (Darwin arm64) Python/3.14.7`; C# used that exact value in the parity measurement. Both used `Authorization: Bearer <SDK token>`, `X-API-Version: 1.0.0`, normal TLS validation and disabled redirects. No response bodies or credentials were logged.

This reproduces the rejection across languages and with the inspected headers aligned. It is not proof that an arbitrary other header can never matter, nor that the integration token is revoked. The published API function expects a device access token. Its refresh function uses a separate `hatch_refresh:` bearer and puts `sdk_token` in the JSON body; it does not exchange an SDK token alone for a signed-in user's credentials. The [SDK-token portal](https://gadgets.muse.ai/settings/sdk-tokens) describes the token as enabling gadget pairing with the Muse app. Initial backend-only account authorization remains tracked in [issue #2](https://github.com/tryAGI/Muse/issues/2).

### Fixed in the authentication follow-up

- The documented default API-version header is now actually applied when the caller omits the optional argument. A default SDK User-Agent is added without replacing a caller's value or mutating shared HttpClient headers. The original live probe already supplied the API version explicitly, so this fix does not explain its earlier 401.
- `MuseClient.ForDeviceAccessToken` and `ForDeviceRefreshToken` provide role-aware construction. SDK tokens and malformed credentials are rejected locally without leaking their values; refresh prefixes are normalized exactly once. These are local validation helpers, not token minting or provider-side validity checks. The raw generated constructor remains an explicitly low-level API.
- The live wrapper no longer reports success when the selected tests are skipped/inconclusive, missing or empty. The parity run produced two inconclusive C# results and correctly returned exit code 3.
- Live examples and the NativeAOT smoke now use the role-aware factories and separate access/refresh clients.

### Follow-up verification

- macOS: 33 offline C# tests passed with no skipped tests; build completed with no C# warnings/errors. Two Python gate tests covered nine result-report cases and passed.
- A broader local validation command timed out during the redundant build before interoperability started. One no-build retry then timed out waiting for the loopback WebSocket HTTP upgrade. These local network reruns are **not passing evidence**; their cause is not established. No encrypted-transport implementation was changed in this follow-up.
- The updated NativeAOT smoke and independent network checks are included in Linux CI. Their new run must be checked separately; the successful historical baseline below is not a substitute for that run.

Reproduce the bounded language/header comparison with `bash scripts/test-live.sh probe-sdk-token`. `artifacts/auth-python.json` and the fresh `artifacts/live-auth-*/live.trx` contain sanitized local observations. The temporary token stays outside source, GitHub Actions and public issue content.

## Initial network baseline (commit 2d72741)

The C# network implementation is suitable for further isolated integration work. **It is not a completed authenticated Muse E2E or a production security certification.** No Advantage application, microphone, speech provider, BLE pairing, or remote tool executor was enabled.

| Check | Observed result |
|---|---|
| Release build (.NET SDK 10.0.301, macOS arm64) | Successful, zero C# warnings/errors |
| Offline crypto/framing/JSON/HTTP contracts | 20 passed; no skipped tests in this selected lane |
| Independent upstream protocol oracle | 10 passed; 9 handshakes, 26 requests, 4 stream resets, 1 denied command, no oracle errors |
| NativeAOT osx-arm64 publish | Successful; no IL trimming/AOT diagnostics; two native linker debug-module-cache warnings |
| Executed NativeAOT interoperability | Passed HTTP serialization, encrypted 150 KB echo, logical registration, text acknowledgement, UTF-8 event subscription and disposal |
| NativeAOT oracle observations | 1 handshake, 4 requests, 2 resets, 1 denied command, no oracle errors |
| Real GET /fetch_vms with supplied SDK token | HTTP 401 on 2026-10-05 16:24 UTC; classified inconclusive for provider E2E, not a passing chat test |
| Authorized real VM connection / assistant reply | Not exercised: no authorized device or VM bearer was supplied |
| Real refresh / revocation / fresh headless authorization | Not exercised; unresolved authorization prerequisite |
| Linux and CI | Configured for verification in GitHub Actions; not implied by the local macOS results |

The live probe used the generated C# HTTP client, one bounded request, normal TLS validation, and no redirects. It logged only the endpoint/status and a redacted result. It did not send a chat prompt. A 401 proves that this token did not authorize this request; it does not establish whether the SDK token is valid for every other provider operation.

## Coverage and interpretation

The oracle uses the pinned Muse Python Noise implementation as an independent wire peer. Its chat and control handlers are **synthetic fixtures**, not a Muse server. Passing them proves tested wire interoperability and local lifecycle handling, not service entitlement, real event attribution, native speech, exactly-once delivery, or whole-turn completion.

Covered cases include RFC 7748 Alice/Bob and 1,000-iteration vectors, low-order points, AES-GCM tampering/nonce order, fragmented and reordered envelopes, malformed lengths/varints, duplicate chunks, aggregate reassembly limits, all UTF-8 record split points, bounded queues, half-close, parallel requests, scoped reset, wrong static-key pin, subscription cancellation, and denied shell invocations. HTTP fixtures verify device/version headers, distinct refresh authorization, flat/nested token responses, and no default retry on 401/429.

The first baseline exposed a test assertion that incorrectly converted a missing oracle into failure; prerequisite resolution now happens before the exception assertion. An initial oracle launch had no completed handshakes and timed out; the runner now compiles before launching the bounded fixture, uses Release/no-build execution, a bounded close timeout and visible failures. Subsequent complete runs passed. These facts are not evidence of a repaired production Muse incident.

## NativeAOT warning caveat

The macOS native link emitted warnings about unavailable cached Foundation and SwiftConcurrencyShims debug modules in SDK-supplied static libraries. Native code generation completed and the resulting binary passed the network smoke. Debug-symbol quality is affected; this report does not claim a completely warning-free native toolchain. The C# Release build and managed analyzers were clean.

## Dependencies and packaging

A fresh restore and `dotnet list src/libs/Muse/Muse.csproj package --include-transitive` resolve only `MinVer 7.0.0` and `DotNet.ReproducibleBuilds 2.0.2` for the SDK project. They are private build/versioning tooling; no consumer runtime package is declared. Inspection of the produced `tryAGI.Muse.0.0.0-dev.nupkg` found no NuGet dependency entries, no `runtimes/` native assets, and no credential-file names. The generator rerun was a cache hit and left all 28 generated C# files unchanged; CI also checks a clean regeneration.

Additional tooling: pinned AutoSDK CLI `0.35.0-preview.2.74`; MSTest `4.1.0`, AwesomeAssertions `9.4.0`, GitHubActionsTestLogger `3.0.3`; optional development-only Python oracle uses cryptography `47.0.0` and websockets `15.0.1`. The oracle downloads revision `74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff`, retains its Apache-2.0 license, and records downloaded file hashes under ignored artifacts. These tooling dependencies are not described as first-party or as part of the consumer runtime.

No secret, transcript, provider response body, or VM bearer belongs in the source tree, package, CI logs, or public issues. A local `.env` is ignored and owner-readable only; `.env.example` contains empty placeholders.

## Remaining release gates

A stable release requires an authorized, genuinely device-free initial credential flow; real VM discovery/connect/isolated assistant response; rotation and revoke/unpair verification; reconnect and cancellation behavior against real service events; and independent review of the new cryptographic transport implementation. Do not work around this gate by extracting application tokens or pretending that an SDK token is a user credential.
