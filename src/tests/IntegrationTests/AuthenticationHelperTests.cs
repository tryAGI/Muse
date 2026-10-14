using System.Net;
using System.Text;
using System.Text.Json;

namespace Muse.IntegrationTests;

[TestClass]
public sealed class AuthenticationHelperTests
{
    [TestMethod]
    public async Task DiscoveryAppliesVersionAndUserAgentWithoutCallerArguments()
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var client = MuseClient.ForDeviceAccessToken("fixture-access", http, disposeHttpClient: false);
        await client.FetchVmsAsync();
        handler.Authorization.Should().Be("Bearer fixture-access");
        handler.Version.Should().Be("1.0.0");
        handler.UserAgent.Should().Be("tryAGI.Muse/0.1.0");
        http.DefaultRequestHeaders.Authorization.Should().BeNull();
        http.DefaultRequestHeaders.UserAgent.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CallerVersionAndUserAgentArePreserved()
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Consumer/2.0");
        using var client = MuseClient.ForDeviceAccessToken("fixture-access", http, disposeHttpClient: false);
        await client.FetchVmsAsync(xApiVersion: "explicit-version");
        handler.Version.Should().Be("explicit-version");
        handler.UserAgent.Should().Be("Consumer/2.0");
    }

    [TestMethod]
    [DataRow("fixture-refresh")]
    [DataRow("hatch_refresh:fixture-refresh")]
    [DataRow("hatch_refresh:hatch_refresh:fixture-refresh")]
    public async Task RefreshNormalizesPrefixAndKeepsSdkTokenInBody(string refresh)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var access = MuseClient.ForDeviceAccessToken("fixture-access", http, disposeHttpClient: false);
        using var client = MuseClient.ForDeviceRefreshToken(refresh, http, disposeHttpClient: false);
        await client.RefreshDeviceTokenAsync("fixture-node", sdkToken: "mgst_synthetic-integration");
        handler.Authorization.Should().Be("Bearer hatch_refresh:fixture-refresh");
        handler.UserAgent.Should().Be("tryAGI.Muse/0.1.0");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("sdk_token").GetString().Should().Be("mgst_synthetic-integration");
        body.RootElement.GetProperty("device_id").GetString().Should().Be("fixture-node");
        await access.FetchVmsAsync();
        handler.Authorization.Should().Be("Bearer fixture-access");
        http.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [TestMethod]
    [DataRow("mgst_synthetic-integration", false)]
    [DataRow("hatch_refresh:fixture-refresh", false)]
    [DataRow("mgst_synthetic-integration", true)]
    [DataRow("hatch_refresh:mgst_synthetic-integration", true)]
    [DataRow("hatch_refresh:", true)]
    [DataRow("wrong:fixture-refresh", true)]
    [DataRow("fixture\r\ninjected", false)]
    [DataRow(" fixture ", true)]
    public void WrongRolesAndMalformedCredentialsFailBeforeNetworkWithoutLeaking(string token, bool refresh)
    {
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        Action action = () =>
        {
            using var client = refresh
                ? MuseClient.ForDeviceRefreshToken(token, http, disposeHttpClient: false)
                : MuseClient.ForDeviceAccessToken(token, http, disposeHttpClient: false);
        };
        var failure = action.Should().Throw<ArgumentException>();
        failure.Which.Message.Should().NotContain(token);
        handler.Calls.Should().Be(0);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        internal string? Authorization { get; private set; }
        internal string? UserAgent { get; private set; }
        internal string? Version { get; private set; }
        internal string? Body { get; private set; }
        internal int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            Version = request.Headers.TryGetValues("X-API-Version", out var versions) ? versions.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(request.Method == HttpMethod.Get ? "{\"vm_list\":[]}" :
                    "{\"access_token\":\"fixture-new-access\",\"refresh_token\":\"fixture-new-refresh\"}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
