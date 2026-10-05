using System.Text;

namespace Muse.IntegrationTests;

[TestClass]
[TestCategory("Interop")]
public sealed class ChatInteropTests
{
    private static MuseConnectionOptions Options() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("MUSE_TEST_ENDPOINT") is { Length: > 0 } value
            ? new Uri(value) : throw new AssertInconclusiveException("Run scripts/test-interop.sh."),
        VmAuthToken = "loopback-only", AllowInsecureLoopback = true,
    };

    [TestMethod]
    public async Task RegisterTextAndEventsWithoutAnyToolExecution()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        await using var link = await MuseLink.RegisterAsync(connection, "fixture-node", "Fixture backend", timeout.Token);
        link.IsRegistered.Should().BeTrue();
        var chat = new MuseChatClient(connection, "fixture-node");
        await using var subscription = await chat.SubscribeAsync(timeout.Token);
        var ack = await chat.SendTextAsync("Hello", "isolated-test-chat", timeout.Token);
        ack.GetProperty("accepted").GetBoolean().Should().BeTrue();
        var count = 0;
        await foreach (var evt in subscription.ReadEventsAsync(timeout.Token))
        {
            count++;
            if (count == 1) evt.GetProperty("text").GetString().Should().Be("Привет 🌍");
            if (count == 2) break;
        }
        count.Should().Be(2);
        var denials = await connection.RequestAsync("GET", "/denials", cancellationToken: timeout.Token);
        int.Parse(Encoding.UTF8.GetString(denials), System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(0);
        connection.IsConnected.Should().BeTrue();
    }

    [TestMethod]
    public async Task CancellingIdleSubscriptionDoesNotHangOrReplay()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var connection = await MuseNoiseConnection.ConnectAsync(Options(), timeout.Token);
        var chat = new MuseChatClient(connection, "fixture-node");
        await using var subscription = await chat.SubscribeAsync(timeout.Token);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Func<Task> read = async () => { await foreach (var _ in subscription.ReadEventsAsync(cancel.Token)) { } };
        await read.Should().ThrowAsync<OperationCanceledException>();
        connection.IsConnected.Should().BeTrue();
        (await connection.RequestAsync("GET", "/echo", "still alive"u8.ToArray(), cancellationToken: timeout.Token)).Should().Equal("still alive"u8.ToArray());
    }
}
