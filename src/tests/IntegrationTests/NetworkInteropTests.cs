using System.Net;
using System.Text;

namespace Muse.IntegrationTests;

[TestClass]
[TestCategory("Interop")]
public sealed class NetworkInteropTests
{
    private static MuseConnectionOptions Options(int capacity = 256, byte[]? pin = null) => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("MUSE_TEST_ENDPOINT") is { Length: > 0 } value
            ? new Uri(value) : throw new AssertInconclusiveException("Run scripts/test-interop.sh for the independent loopback oracle."),
        VmAuthToken = "loopback-only",
        AllowInsecureLoopback = true,
        MaximumQueuedFramesPerStream = capacity,
        ExpectedServerStaticKey = pin ?? ReadOnlyMemory<byte>.Empty,
    };

    [TestMethod]
    public async Task HandshakeFragmentationAndParallelResponses()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        connection.RemoteStaticPublicKey.Length.Should().Be(32);
        var calls = Enumerable.Range(0, 12).Select(async index =>
        {
            var payload = Encoding.UTF8.GetBytes(new string((char)('A' + index), 150000));
            var result = await connection.RequestAsync("POST", "/echo", payload, cancellationToken: timeout.Token);
            result.Should().Equal(payload);
        });
        await Task.WhenAll(calls);
        connection.BufferedResponseBytes.Should().Be(0);
    }

    [TestMethod]
    public async Task StreamingUploadHalfClose()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        await using var request = await connection.OpenRequestAsync("POST", "/upload", endBody: false, cancellationToken: timeout.Token);
        await request.SendBodyAsync("first"u8.ToArray(), cancellationToken: timeout.Token);
        await request.SendBodyAsync("second"u8.ToArray(), endBody: true, cancellationToken: timeout.Token);
        using var body = new MemoryStream();
        await foreach (var chunk in request.ReadAllAsync(timeout.Token)) body.Write(chunk.Data.Span);
        Encoding.UTF8.GetString(body.ToArray()).Should().Be("firstsecond");
    }

    [TestMethod]
    public async Task CancelResetsOnlyTheRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        await using (var request = await connection.OpenRequestAsync("POST", "/wait", cancellationToken: timeout.Token))
        {
            await foreach (var chunk in request.ReadAllAsync(timeout.Token)) { chunk.StatusCode.Should().Be(200); break; }
        }
        var response = await connection.RequestAsync("GET", "/stats", cancellationToken: timeout.Token);
        int.Parse(Encoding.UTF8.GetString(response), System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(0);
        connection.IsConnected.Should().BeTrue();
    }

    [TestMethod]
    public async Task HttpAndStreamResetErrorsDoNotCloseOtherStreams()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        Func<Task> reject = () => connection.RequestAsync("GET", "/reject", cancellationToken: timeout.Token);
        var error = await reject.Should().ThrowAsync<HttpRequestException>();
        error.Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        error.Which.Message.Should().NotContain("not included");
        Func<Task> reset = () => connection.RequestAsync("GET", "/reset", cancellationToken: timeout.Token);
        await reset.Should().ThrowAsync<MuseProtocolException>();
        connection.IsConnected.Should().BeTrue();
    }

    [TestMethod]
    public async Task AuthenticationFailureClosesConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        Func<Task> action = () => connection.RequestAsync("GET", "/tamper", cancellationToken: timeout.Token);
        await action.Should().ThrowAsync<System.Security.Cryptography.CryptographicException>();
        connection.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    public async Task OversizedWebSocketMessageIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        Func<Task> action = () => connection.RequestAsync("GET", "/oversize", cancellationToken: timeout.Token);
        await action.Should().ThrowAsync<MuseProtocolException>();
        connection.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    public async Task SlowConsumerCannotGrowAnUnboundedQueue()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(capacity: 2), timeout.Token);
        await using var request = await connection.OpenRequestAsync("GET", "/flood", cancellationToken: timeout.Token);
        while (connection.IsConnected) await Task.Delay(10, timeout.Token);
        connection.BufferedResponseBytes.Should().BeLessThanOrEqualTo(18000);
        await request.DisposeAsync();
        connection.BufferedResponseBytes.Should().Be(0);
    }

    [TestMethod]
    public async Task UnexpectedStaticKeyPinIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Resolve the optional fixture before the assertion so missing prerequisites remain inconclusive.
        var options = Options(pin: new byte[32]);
        Func<Task> action = async () => { await using var connection = await MuseNoiseConnection.ConnectAsync(options, timeout.Token); };
        await action.Should().ThrowAsync<MuseProtocolException>();
    }
}
