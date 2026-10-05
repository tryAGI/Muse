using System.Text;
using Muse.Transport;

namespace Muse.IntegrationTests;

[TestClass]
public sealed class JsonRecordTests
{
    [TestMethod]
    public void NdjsonHandlesEveryUtf8SplitAndFinalRecord()
    {
        var data = Encoding.UTF8.GetBytes("{\"text\":\"Привет 🌍\"}\n{\"done\":true}");
        for (var split = 0; split <= data.Length; split++)
        {
            var decoder = new JsonRecords(false);
            var result = decoder.Feed(data.AsSpan(0, split));
            result.AddRange(decoder.Feed(data.AsSpan(split))); result.AddRange(decoder.Complete());
            result.Count.Should().Be(2); result[0].GetProperty("text").GetString().Should().Be("Привет 🌍");
        }
    }

    [TestMethod]
    public void ControlHandlesKeepaliveAndEveryPrefixSplit()
    {
        var data = JsonRecords.Encode(writer => { writer.WriteStartObject(); writer.WriteString("id", "test"); writer.WriteEndObject(); }, prefix: true);
        for (var split = 0; split <= data.Length; split++)
        {
            var decoder = new JsonRecords(true);
            decoder.Feed(new byte[4]).Should().BeEmpty();
            var result = decoder.Feed(data.AsSpan(0, split));
            result.AddRange(decoder.Feed(data.AsSpan(split))); result.AddRange(decoder.Complete());
            result.Single().GetProperty("id").GetString().Should().Be("test");
        }
    }

    [TestMethod]
    public void OversizeAndTruncatedRecordsFailClosed()
    {
        var decoder = new JsonRecords(true, 32);
        Action oversize = () => decoder.Feed([255, 255, 255, 127]);
        oversize.Should().Throw<MuseProtocolException>();
        Action reuse = () => decoder.Feed([0, 0, 0, 0]); reuse.Should().Throw<MuseProtocolException>();
        var partial = new JsonRecords(true); partial.Feed([2, 0]);
        Action truncated = () => partial.Complete(); truncated.Should().Throw<MuseProtocolException>();
        var ndjson = new JsonRecords(false, 8);
        Action longLine = () => ndjson.Feed("123456789"u8); longLine.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void InvalidUtf8OrNonObjectRecordsFail()
    {
        Action invalid = () => new JsonRecords(false).Feed([123, 34, 120, 34, 58, 34, 255, 34, 125, 10]);
        invalid.Should().Throw<MuseProtocolException>();
        Action array = () => new JsonRecords(false).Feed("[]\n"u8); array.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void WrongCredentialAndUnsafeEndpointAreRejectedBeforeNetwork()
    {
        Func<Task> sdkToken = () => MuseNoiseConnection.ConnectAsync(new()
        { Endpoint = new Uri("wss://example.invalid/v1/noise"), VmAuthToken = "mgst_fixture" });
        sdkToken.Should().ThrowAsync<ArgumentException>().GetAwaiter().GetResult();
        Action endpoint = () => new MuseConnectionOptions
        { Endpoint = new Uri("ws://example.invalid/v1/noise"), VmAuthToken = "dummy" }.Validate();
        endpoint.Should().Throw<ArgumentException>();
    }
}
