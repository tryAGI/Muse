# Muse account enrollment through a temporary Apple relay

Design and implemented protocol foundation, 2026-10-05. This updates the earlier **no Bluetooth even during setup** requirement: the owner now explicitly permits one-time enrollment through the existing Apple devices. Conversation runtime must still be entirely backend-owned.

**Status:** the C# pairing-v5 cryptographic endpoint is implemented and locally verified. Native GATT relay, Advantage enrollment endpoints/storage, physical app approval and authenticated provider chat are NOT implemented or verified by these tests. This document is the implementation contract, not a claim that the complete integration is live.

## Chosen route

```text
One-time enrollment:
  Owner starts "Connect Muse" in Advantage iOS/macOS
       |
  Advantage backend creates an expiring, owner-bound logical device
       |
  Advantage macOS advertises that device over Bluetooth
       |
  Official Muse app on the owner's iPhone: Add Device, approve
       |
  Muse iPhone app <-- pairing v5 over BLE --> Advantage macOS
                                            |
                                    authenticated encrypted relay
                                            |
                                  C# pairing endpoint in backend
                                            |
                     verify device grant -> protected durable storage

Normal operation after enrollment:
  Advantage iOS / watchOS / macOS <--> Advantage backend <--> Muse VM
  The Mac relay and Muse phone app are no longer on the conversation path.
```

The Mac acts as the radio for a **logical backend device**, not as an assistant executor. No Raspberry Pi, custom gadget, Python service, installed Muse daemon, shell/file command execution or copied app cookie is needed in the target product. The actual account sign-in and device grant issuance stay inside the official Muse app; we do not need to guess a private token-mint HTTP endpoint.

This is an interoperability design inferred from the published gadget protocol. Physical Muse app acceptance, entitlement, account-grant issuance and cloud use must be measured in the live acceptance test, not assumed from protocol vectors.

## Why the iPhone + Mac route is the baseline

The official SDK pairs a distinct Bluetooth peripheral with the signed-in Muse app. Advertising the same service from another application on that **same iPhone** is not a verified substitute for a distinct peripheral. Switching apps also introduces iOS background advertising restrictions (local-name suppression, overflow UUIDs, suspension). Do not build the first success criterion on same-phone self-discovery or promise iPhone-only onboarding.

The owner's Mac already provides a distinct radio and can keep Advantage visible while the official Muse iPhone app remains foregrounded. A second iOS/iPadOS device running Advantage can be a later foreground relay candidate, but needs its own app/discovery and background tests. watchOS is not part of the enrollment relay baseline; it can initiate the request, show progress and use the completed backend connection.

There is upstream contributor evidence for phone-to-Mac enrollment followed by Linux Docker operation (PR 63) and a relayed Ethernet-only target (PR 64). Those are contributor reports, not our E2E results. Neither PR is required as a runtime dependency or automatically accepted as a security review.

## What happens to the SDK token

1. Backend retrieves the configured Gadget SDK token from protected storage. It creates or selects a stable logical identity.
2. Muse app and backend establish community pairing v5 through the opaque Mac relay. The handshake uses P-256, HKDF-SHA256 and AES-GCM; this is distinct from the later VM Noise XX protocol.
3. Backend validates encrypted `pairing_client_finished` and sends encrypted `pairing_confirmed` containing `sdk_token` to the official app. The token is absent from public advertising, `get_device_info` and `pairing_ready`.
4. The app handles account authorization and issues encrypted `provision_v2` with **separate** device access and refresh credentials. This is a protocol command, not an endpoint to call with the SDK token.
5. Backend decrypts the grant, validates allowed endpoint metadata, verifies it with VM discovery, and atomically saves it. Only then may it send encrypted `auth_ok` back to the app.
6. Backend connects to the selected VM with its VM bearer, registers an explicit command-free node, subscribes and sends a bounded isolated test message.

`GET /fetch_vms` with `mgst_...` as the device bearer is not this enrollment flow. Do not repeat it as a token-validity test. No additional internal secret needs to be manually supplied by the owner: the necessary user action is approving the actual device enrollment in Muse.

## SDK layer now available

`MusePairingSession` owns one monotonic, expiring attempt. It is transport-independent C# and has no Bluetooth, Apple, Python, storage or Advantage runtime dependency.

- `GetDeviceInfo(networkReady)` returns public discovery metadata.
- `HandleClientHello(bytes)` handles the initial public message and returns `pairing_ready`.
- `ConfirmClient(bytes)` validates the first encrypted record and returns encrypted SDK-token delivery.
- `ReceiveCommand(bytes)` consumes one record exactly once and identifies `wifi_scan` or returns a `MuseProvisioningCredentials` grant. It never executes tools or joins a Wi-Fi network.
- `EncryptResponse(json)` sends bounded network/provisioning progress. It cannot send terminal `auth_ok`/`auth_failed`.
- `FinishProvisioning(verifiedAndPersisted)` is a **host-only** operation after grant receipt. `true` is permitted only after provider validation and an atomic durable commit by the host; this Boolean is not a provider verification API. Both outcomes close the cryptographic attempt.
- `Dispose()` invalidates pending ephemeral keys without revoking an existing stored grant.

The host must dispatch encrypted records by phase: confirmation first, then `ReceiveCommand`. Do not decrypt a message to inspect its action and then feed its ciphertext a second time; replayed counters are deliberately rejected.

Commands, credentials and SDK token are untrusted/sensitive data, not logging payloads. Redacted `ToString()` is not permission to destructure secret-bearing objects into structured logs.

## Advantage application integration

The following names and wire operations are proposed, not deployed APIs.

### Backend

Introduce an owner-only `MuseEnrollmentCoordinator`, a per-owner protected grant store and a `MuseSessionCoordinator` for post-enrollment runtime. Use the standalone SDK, not code copied into the Runway service. No Codex reasoning process should run merely to authorize or converse with Muse.

Suggested enrollment control:

- `POST /muse/enrollments`: owner explicitly chooses a trusted, online relay device; create an attempt with a deadline of at most 10 minutes. Derive owner/profile from authenticated claims, never the request body.
- Ordered relay messages carry `enrollmentId`, `epoch`, `relayDeviceId`, `centralConnectionEpoch`, `sequence`, bounded payload and request ID over the existing authenticated Advantage app transport.
- `GET /muse/enrollments/{id}`: safe stage/error metadata, never raw frames or tokens.
- Explicit cancel and disconnect operations invalidate only this attempt/binding; replacement does not revoke or overwrite a working grant until the replacement is verified.

Bind the attempt to exactly one selected relay and one BLE central. An identifier alone is not authorization. Duplicate transport requests must return the **cached original encrypted response**; they must not decrypt the same counter or encrypt a second response again. Same sequence with a changed payload is an error. Bound frame size, total buffered bytes, outstanding requests and per-attempt rate. Do not persist raw payloads in general traces, worker journals or analytics.

Treat `api_url_v2` and `noise_host` from the app as endpoint metadata, not an instruction to send credentials to arbitrary hosts. Use an explicit Muse allowlist, HTTPS/WSS, normal certificate validation, no credential-carrying redirects and no user-info/query-token URLs.

Persist stable pseudo-MAC, `hatch-link:...` device ID, `homelink-...` node ID, provider binding, encrypted access/refresh credentials and version. The published identity uses random locally administered MAC-shaped bytes; it is not the relay's Bluetooth address. Check the generated node suffix for collisions. A refresh uses the **same node identity**, not a newly selected iPhone/Watch/Mac connection ID.

Existing Advantage evidence to reuse:

- `backend/Advantage.Backend/Services/Codex/CodexNativeSettingsStore.cs` uses `IRuntimeSettingRepository` and an `IDataProtector` purpose to store protected settings. A Muse store needs a distinct purpose, explicit owner binding and versioned compare-and-swap; do not reuse a Codex key or assume a field is encrypted merely because it is in the database.
- Existing authenticated Apple app transport should carry typed enrollment control. Add the shared spec and required C#/Swift consumers together with normal generators; do not smuggle new auth traffic through an arbitrary shell tool.
- On backend restart during enrollment, expire the ephemeral attempt and request fresh pairing. Never resume a stale nonce state. Already committed device grants survive restarts; ordinary refresh/reconnect remains server-side.

Use one refresh owner/lease per binding, cancellation/deadlines, bounded backoff and atomic token replacement. No blind retry of ambiguous token rotations or chat submissions. Confirmed revocation transitions to reauthorization-required; a transient network failure does not delete an existing working grant.

### macOS

Add a session-scoped `MuseEnrollmentRelay` to Advantage's existing macOS application, not a permanent CLI worker dependency. Keep native window/menu code in `AdvantageMacOSUI`; keep transport-independent shared models outside macOS-only UI.

The relay implements the exact upstream GATT service/characteristic UUIDs, properties and BLE framing. Advertise the exact `MuseGadget<SUFFIX>` name from the backend identity; an arbitrary name such as `Advantage` can fail Muse discovery. Respect characteristic write offsets, fragmentation, maximum lengths, MTU and notification backpressure (`peripheralManagerIsReady(toUpdateSubscribers:)`). Accept only one central for an attempt; clear pending packet state on unsubscribe, power-off, cancellation and epoch change.

The relay can reassemble/split **opaque protocol JSON** for network efficiency, but does not create pairing keys, decrypt grants, store credentials or run received commands. Do not forward unsupported plaintext provisioning as a shortcut. Stop advertising on completion/cancel/timeout. Restore prior app-owned advertising state, not global Bluetooth settings.

Advantage already uses a `CBPeripheralManager` for proximity in `src/AdvantageShared/Sources/AdvantageShared/Session/Services/AdvantageBLEProximityService.swift`. Coordinate/pause/restore that existing advertiser during the enrollment window; do not assume unrelated peripheral managers can advertise arbitrary names simultaneously. Existing macOS Bluetooth usage descriptions cover proximity and must be updated truthfully for enrollment if required by the feature.

If an app asks for Wi-Fi scan/provisioning, represent the already-connected network as the published Linux SDK does. Do not invent a nearby SSID, change the owner's network or retain a supplied Wi-Fi password. Validate the chosen representation on the actual iPhone app; `network_ready=true` alone does not prove that every app skips Wi-Fi UI.

### iOS

Provide the main "Connect Muse" screen: choose the owner's trusted Mac, start the bounded relay, display the exact advertised name and instructions to approve it in the official Muse app. It may initiate the backend flow and monitor safe status while Muse is foregrounded. Completion must be available after returning from Muse, with distinct states for app approval, grant verification, provider connection and real reply.

Do not use an invented Muse deep link. An ordinary instruction to open Muse is sufficient for the first version. No extraction of Muse cookies, password database or app sandbox, and no copy/paste of device/refresh tokens.

### watchOS

Reuse the same owner-level Muse connection through Advantage backend. Show "Finish setup on iPhone/Mac" until enrollment completes; no refresh credential or SDK token goes to the watch. Do not create a new Muse device grant for every watch surface or temporary connection.

PTT, text, captions and optional explicitly approved TTS belong to the next integration step. Preserve the existing global-microphone ownership and single-uplink policy. No ambient audio is forwarded to Muse merely because enrollment exists. Do not instantiate a Runway media room or advertise native Muse full-duplex voice without separate evidence.

## Security and owner confirmation

A trusted Mac and the existing Advantage authentication boundary are part of this design. Although the honest relay only sees encrypted grant messages, **community pairing does not provide an authenticated manufacturer/backend key to the phone**. A compromised active relay or local MITM is not ruled out just because payloads are encrypted. Keep the upstream community-device warning, use short explicit sessions on the owner's trusted devices, and do not market the flow as attested or MITM-proof.

The account signed into Muse is the account granting access; do not infer that it is automatically identical to the Advantage login. Show the destination Advantage profile and require explicit owner confirmation. Use verified provider account information where the protocol makes it available; never invent an identity equality check from a friendly display name.

Grant commit and remote `auth_ok` delivery are distinct: if credentials were safely committed but the final BLE notification was lost, retain and reconcile that durable result rather than performing an unbounded new mint/refresh. Redact every secret and exclude enrollment payloads from notifications, crash breadcrumbs and screenshots.

## Physical acceptance: what proves completion

1. With no prior binding, the real Muse iPhone app discovers the selected Mac's exact name, displays the community warning, and accepts the owner's approval.
2. Official app-provided `provision_v2` reaches the backend, not a copied credential fixture. Provider VM discovery and allowed-host checks succeed; the encrypted grant is durably stored before `auth_ok`.
3. Backend-only VM connection/registration succeeds. A uniquely tagged isolated text turn receives a real assistant reply with verified conversation/message attribution. A HTTP acknowledgement or unrelated main-chat message does not count.
4. Stop the Mac relay and close Muse. Another isolated turn through the backend still succeeds. This is the decisive test that the Mac/phone is not a runtime dependency.
5. Restart/redeploy the backend, retaining the logical identity and protected grant; then repeat the turn and verify refresh/expiry/revocation without blind replay or duplicate delivery.
6. Invoke the same mode from Advantage iOS and watchOS; verify no duplicate provider turn, no background microphone leak, clear cancellation and no hidden paid TTS fallback.

The only unavoidable user interaction is the official Muse sign-in/enrollment approval and any OS Bluetooth permission request. No agent should claim that synthetic tests performed those approvals.

## Verification completed for this foundation

- 12 local `PairingSessionTests` passed: published P-256 transcript/session/key agreement, encrypted SDK-token handoff, encrypted scan/provisioning, no premature auth success, grant-role separation, tampering/replay/old session rejection, duplicate JSON/size limits, monotonic expiry and disposal.
- The standalone `src/tests/PairingNativeAot` app was published and **executed** as osx-arm64 NativeAOT. Fresh random P-256 peers completed encrypted confirmation and grant handoff; the synthetic grant was explicitly rejected rather than presented as real authorization.
- The first NativeAOT publish timed out after restore; the scoped retry without the shared compiler completed. No root cause for the compiler stall is claimed. Two native linker debug-module-cache warnings remained; no IL trimming/AOT diagnostics were reported.
- No actual Muse account was paired, no user token was requested, and no Bluetooth advertisement or Advantage production change was made in this foundation step.

Reproduce:

```sh
dotnet test Muse.slnx -c Release --filter FullyQualifiedName~PairingSessionTests
dotnet publish src/tests/PairingNativeAot/PairingNativeAot.csproj -c Release -r osx-arm64 -o artifacts/pairing-nativeaot -p:GeneratePackageOnBuild=false -p:UseSharedCompilation=false
artifacts/pairing-nativeaot/PairingNativeAot
```

## Evidence

Upstream revision: `facebookincubator/muse-gadget-sdk@74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff`.

- [Official owner setup](https://github.com/facebookincubator/muse-gadget-sdk/blob/74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff/linux/README.md)
- [Pairing and encrypted SDK-token handoff](https://github.com/facebookincubator/muse-gadget-sdk/blob/74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff/linux/src/musegadget/pairing.py)
- [Provisioning receiver](https://github.com/facebookincubator/muse-gadget-sdk/blob/74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff/linux/src/musegadget/ble_setup.py)
- [Logical device identity](https://github.com/facebookincubator/muse-gadget-sdk/blob/74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff/linux/src/musegadget/identity.py)
- [Published v5 test vector](https://github.com/facebookincubator/muse-gadget-sdk/blob/74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff/linux/tests/vectors/link_pairing_v5.json)
- [Mac enrollment report, PR 63](https://github.com/facebookincubator/muse-gadget-sdk/pull/63)
- [Relayed Ethernet-target enrollment report, PR 64](https://github.com/facebookincubator/muse-gadget-sdk/pull/64)
- [Apple: Core Bluetooth background behavior](https://developer.apple.com/library/archive/documentation/NetworkingInternetWeb/Conceptual/CoreBluetooth_concepts/CoreBluetoothBackgroundProcessingForIOSApps/PerformingTasksWhileYourAppIsInTheBackground.html)
