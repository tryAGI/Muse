# Network validation — 2026-10-05

## Result

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
