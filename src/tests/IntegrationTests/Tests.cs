namespace Muse.IntegrationTests;

[TestClass]
public partial class Tests
{
    private static MuseClient GetAuthenticatedClient()
    {
        var token = Environment.GetEnvironmentVariable("MUSE_DEVICE_ACCESS_TOKEN") is { Length: > 0 } value
            ? value : throw new AssertInconclusiveException("MUSE_DEVICE_ACCESS_TOKEN is required; an SDK token is not a user credential.");
        return new MuseClient(token);
    }
}
