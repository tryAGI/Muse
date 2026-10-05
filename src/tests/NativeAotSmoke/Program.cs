using Muse;
using System.Net;
using System.Text;

try
{
    // Synthetic HTTP verifies the generated client with reflection disabled.
    using var http = new HttpClient(new FixtureHandler());
    using var api = new MuseClient("fixture-only", httpClient: http, disposeHttpClient: false);
    var vms = await api.FetchVmsAsync(xApiVersion: "1.0.0");
    if (vms.VmList?.Count != 1) throw new InvalidOperationException("Unexpected fixture VM response.");
    var rotated = await api.RefreshDeviceTokenAsync("fixture-device", sdkToken: "fixture-sdk");
    if (rotated.Payload?.AccessToken != "fixture-access") throw new InvalidOperationException("Unexpected fixture refresh response.");

    // The oracle launcher removes all provider credentials from this process.
    var endpoint = Environment.GetEnvironmentVariable("MUSE_TEST_ENDPOINT") is { Length: > 0 } value
        ? new Uri(value) : throw new InvalidOperationException("Run this smoke through scripts/test-interop.sh.");
    if (!endpoint.IsLoopback || endpoint.Scheme != "ws") throw new InvalidOperationException("Only the loopback oracle is allowed.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var connection = await MuseNoiseConnection.ConnectAsync(new MuseConnectionOptions
    { Endpoint = endpoint, VmAuthToken = "loopback-only", AllowInsecureLoopback = true }, timeout.Token);
    var payload = Encoding.UTF8.GetBytes(new string('A', 150000));
    var echo = await connection.RequestAsync("POST", "/echo", payload, cancellationToken: timeout.Token);
    if (!echo.AsSpan().SequenceEqual(payload)) throw new InvalidOperationException("Fragmented echo mismatch.");
    await using var link = await MuseLink.RegisterAsync(connection, "fixture-node", "NativeAOT fixture", timeout.Token);
    var chat = new MuseChatClient(connection, "fixture-node");
    await using var subscription = await chat.SubscribeAsync(timeout.Token);
    var ack = await chat.SendTextAsync("Hello", "isolated-test-chat", timeout.Token);
    if (!ack.GetProperty("accepted").GetBoolean()) throw new InvalidOperationException("Chat acknowledgement missing.");
    var events = 0;
    await foreach (var item in subscription.ReadEventsAsync(timeout.Token))
    {
        events++;
        if (events == 1 && item.GetProperty("text").GetString() != "Привет 🌍")
            throw new InvalidOperationException("UTF-8 event mismatch.");
        if (events == 2) break;
    }
    if (events != 2 || !link.IsRegistered) throw new InvalidOperationException("Incomplete chat fixture.");
    Console.WriteLine("NativeAOT HTTP, Noise, fragmented transport, link registration and chat subscription smoke passed (loopback only).");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"NativeAOT smoke failed: {exception.GetType().Name}. Provider data is not logged.");
    return 1;
}

internal sealed class FixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.RequestUri!.AbsolutePath == "/fetch_vms"
            ? "{\"vm_list\":[{\"vm_id\":\"fixture-vm\"}]}"
            : "{\"payload\":{\"access_token\":\"fixture-access\",\"refresh_token\":\"fixture-refresh\"}}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
