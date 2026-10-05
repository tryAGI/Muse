using Muse.Transport;

namespace Muse.IntegrationTests;

[TestClass]
public sealed class FramingTests
{
    [TestMethod]
    public void ReassemblesReorderedChunks()
    {
        var payload = new byte[150000]; new Random(42).NextBytes(payload);
        var frames = NoiseFraming.Encode(payload).Reverse().ToArray();
        var decoder = new NoiseFraming(200000);
        decoder.Decode(frames[0]).Should().BeNull();
        decoder.Decode(frames[1]).Should().BeNull();
        decoder.Decode(frames[2]).Should().Equal(payload);
    }

    [TestMethod]
    public void DuplicateChunkPoisonsDecoder()
    {
        var frame = NoiseFraming.Encode(new byte[100000]).First();
        var decoder = new NoiseFraming(200000); decoder.Decode(frame);
        Action duplicate = () => decoder.Decode(frame);
        duplicate.Should().Throw<MuseProtocolException>();
        Action reuse = () => decoder.Decode(NoiseFraming.Encode([]).Single());
        reuse.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void GlobalMemoryBudgetIsEnforced()
    {
        var decoder = new NoiseFraming(100);
        Action action = () => decoder.Decode(NoiseFraming.Encode(new byte[101]).Single());
        action.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void TruncatedOrOverflowingVarintIsRejected()
    {
        var decoder = new NoiseFraming(100);
        Action action = () => decoder.Decode([8, 255, 255, 255, 255, 255, 255, 255, 255, 255, 2]);
        action.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void RejectsInvalidProtobufLength()
    {
        var decoder = new NoiseFraming(100);
        Action action = () => decoder.Decode([34, 127, 1]);
        action.Should().Throw<MuseProtocolException>();
    }
}
