# Account availability and live validation

Evidence reviewed on 2026-10-16. Product availability and credential provisioning are separate prerequisites; a rejected SDK token does not establish a geographic restriction.

## Official regional statements

- The [Muse business FAQ](https://muse.ai/business) lists the United States and Canada and an age requirement of 18+.
- Meta's [2026-09-24 Connect announcement](https://about.fb.com/fr/news/2026/09/meta-connect-2026-meta-annonce-de-nouvelles-lunettes-ia-des-nouveautes-pour-muse-et-les-meta-vr-glasses/?noamp=mobile) lists the United States, Canada and Mexico.
- The [Gadgets site](https://gadgets.muse.ai/) describes HomeLink availability for US subscribers.

These pages describe different surfaces and do not give an identical country list. None of these statements includes the UAE. This is evidence of a documented availability gap, not proof of the precise reason for any particular account's denial. Provider confirmation or a denial from the official signed application with the correct account flow is still needed to establish that reason.

## Current local readiness

A read-only role check found an integration SDK token, but no device access token, device refresh token, device ID, VM bearer or Noise host. Secret values were not printed, and no provider request was sent during this review. Repeating the SDK-token-only `/fetch_vms` probe would test the wrong credential role again.

The previously measured SDK-token-only `401` responses are inconclusive for region and for initial pairing. The SDK remains experimental/prerelease. Offline protocol and NativeAOT checks do not prove authenticated provider E2E.

## Completion criteria

1. Use the official account/device authorization flow with a legitimately eligible account, or obtain provider confirmation of UAE availability. Do not use region spoofing or undocumented credential exchanges.
2. Complete the authorized pairing/relay flow tracked in [issue #2](https://github.com/tryAGI/Muse/issues/2), with explicit grant persistence and removal in the owning backend.
3. Provision role-correct device credentials without logging them, then run the bounded live test lane.
4. Capture successful VM discovery, a real assistant response, credential rotation, and unpair/revocation behavior before claiming live readiness.

[Issue #7](https://github.com/tryAGI/Muse/issues/7) remains open for the missing account-level evidence. No geographic diagnosis or live completion is inferred from documentation alone.
