using System.Net;
using System.Text;
using System.Text.Json;

namespace Muse.IntegrationTests;

[TestClass]
public sealed class HttpContractTests
{
    [TestMethod]
    public async Task DiscoveryUsesDeviceBearerAndVersionHeader()
    {
        using var handler = new FixtureHandler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.Should().Be("/fetch_vms");
            request.Headers.Authorization!.Parameter.Should().Be("fixture-device-access");
            request.Headers.GetValues("X-API-Version").Single().Should().Be("1.0.0");
            await Task.CompletedTask;
            return Json("{\"vm_list\":[{\"vm_id\":\"fixture-vm\",\"vm_auth_token\":\"fixture-vm-token\",\"default\":true}],\"future_field\":{\"enabled\":true}}");
        });
        using var http = new HttpClient(handler);
        using var client = new MuseClient("fixture-device-access", httpClient: http, disposeHttpClient: false);
        var result = await client.FetchVmsAsync(xApiVersion: "1.0.0");
        result.VmList.Should().ContainSingle();
        result.VmList![0].VmId.Should().Be("fixture-vm");
        handler.Calls.Should().Be(1);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RefreshHasItsOwnCredentialAndAcceptsBothResponseShapes(bool nested)
    {
        using var handler = new FixtureHandler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/device_token/refresh");
            request.Headers.Authorization!.Parameter.Should().Be("hatch_refresh:fixture-refresh");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            body.RootElement.GetProperty("device_id").GetString().Should().Be("fixture-device");
            body.RootElement.GetProperty("sdk_token").GetString().Should().Be("fixture-sdk");
            const string pair = "{\"access_token\":\"fixture-next-access\",\"refresh_token\":\"fixture-next-refresh\"}";
            return Json(nested ? "{\"payload\":" + pair + "}" : pair);
        });
        using var http = new HttpClient(handler);
        using var client = new MuseClient("hatch_refresh:fixture-refresh", httpClient: http, disposeHttpClient: false);
        var result = await client.RefreshDeviceTokenAsync("fixture-device", sdkToken: "fixture-sdk");
        (result.Payload?.AccessToken ?? result.AccessToken).Should().Be("fixture-next-access");
        (result.Payload?.RefreshToken ?? result.RefreshToken).Should().Be("fixture-next-refresh");
        handler.Calls.Should().Be(1);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    public async Task RejectedRefreshIsNotAutomaticallyRetried(HttpStatusCode status)
    {
        using var handler = new FixtureHandler(_ => Task.FromResult(new HttpResponseMessage(status)));
        using var http = new HttpClient(handler);
        using var client = new MuseClient("hatch_refresh:fixture-refresh", httpClient: http, disposeHttpClient: false);
        Func<Task> action = () => client.RefreshDeviceTokenAsync("fixture-device");
        var error = await action.Should().ThrowAsync<ApiException>();
        error.Which.StatusCode.Should().Be(status);
        handler.Calls.Should().Be(1);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class FixtureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return respond(request); }
    }
}
