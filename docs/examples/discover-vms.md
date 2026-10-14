# Discover authorized VMs

Discover VMs using a separately authorized device access token.

This example assumes `using Muse;` is in scope and `apiKey` contains your Muse API key.

```csharp
// The SDK token identifies the integration; this request needs a device access token.
var deviceToken = Environment.GetEnvironmentVariable("MUSE_DEVICE_ACCESS_TOKEN") is { Length: > 0 } value
    ? value : throw new AssertInconclusiveException("MUSE_DEVICE_ACCESS_TOKEN is required.");
using var client = MuseClient.ForDeviceAccessToken(deviceToken);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var response = await client.FetchVmsAsync(cancellationToken: timeout.Token);
// VM bearer credentials are sensitive. Do not print the response or copy it into logs.
```