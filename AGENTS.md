# Muse SDK

This is the independent tryAGI.Muse .NET 10 SDK. Follow the tryAGI workspace NEW_SDK_GUIDE.md and SDK_DEPENDENCY_POLICY.md. Work on main with explicit scoped commits; preserve concurrent work.

- Source schema: src/libs/Muse/openapi.yaml. Regenerate with bash src/libs/Muse/generate.sh; never hand-edit Generated/.
- Keep Muse SDK tokens, device access/refresh tokens, and VM bearer credentials distinct. Never log or commit them. .env is local-only.
- No shell/file executor, Bluetooth, UI, paid speech providers, or Advantage dependency belongs in the runtime.
- Protocol compatibility is not a security audit or proof of headless account bootstrap. Do not claim live chat from a successful HTTP denial or a loopback test.
- Use bounded frames/streams, fail closed on authentication errors, and never automatically replay non-idempotent chat submissions.
- Validate with dotnet test Muse.slnx, the loopback interoperability harness, and NativeAOT. Examples are the test/documentation source.
- Publish only prereleases until actual authenticated Muse chat and credential lifecycle are verified.
