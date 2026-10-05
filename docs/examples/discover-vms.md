# Discover authorized VMs

Discover VMs using a separately authorized device access token.

This example assumes `using Muse;` is in scope and `apiKey` contains your Muse API key.

```csharp
// The SDK token identifies the integration; this request needs a device access token.
using var client = new MuseClient(apiKey);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var response = await client.FetchVmsAsync(xApiVersion: "1.0.0", cancellationToken: timeout.Token);
// VM bearer credentials are sensitive. Do not print the response or copy it into logs.
```