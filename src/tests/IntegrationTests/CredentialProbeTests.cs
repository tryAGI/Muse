using System.Net;

namespace Muse.IntegrationTests;

/// <summary>A bounded, explicitly selected real-service probe; never counted as a successful chat E2E.</summary>
[TestClass]
[TestCategory("LiveProbe")]
public sealed class CredentialProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SdkTokenDeviceApiAccessProbe()
    {
        var sdkToken = Environment.GetEnvironmentVariable("MUSE_SDK_TOKEN") is { Length: > 0 } value
            ? value : throw new AssertInconclusiveException("MUSE_SDK_TOKEN is required for the explicit live probe.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var client = new MuseClient(sdkToken, httpClient: http, disposeHttpClient: false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var response = await client.FetchVmsAsync(xApiVersion: "1.0.0", cancellationToken: deadline.Token);
            // Never print provider bodies, VM identifiers, headers, or credential-bearing objects.
            TestContext.WriteLine("Live GET /fetch_vms: HTTP 200; credentials and response body redacted.");
            if (!string.IsNullOrEmpty(response.ErrorTitle) || !string.IsNullOrEmpty(response.BackendErrorCode))
                Assert.Inconclusive("The service returned an application-level rejection; no live chat was exercised.");
            Assert.IsNotNull(response.VmList, "The response did not contain a VM list.");
            TestContext.WriteLine("VM discovery succeeded. This probe does not establish chat or refresh support.");
        }
        catch (ApiException exception) when (exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            TestContext.WriteLine("Live GET /fetch_vms: HTTP {0}; response body redacted.", (int)exception.StatusCode);
            Assert.Inconclusive($"SDK-token-only authorization was rejected (HTTP {(int)exception.StatusCode}); authorized device credentials are still required. No live chat was exercised.");
        }
        catch (ApiException exception)
        {
            Assert.Fail($"Live credential probe returned HTTP {(int)exception.StatusCode}; provider body withheld.");
        }
        catch (HttpRequestException)
        {
            Assert.Fail("The live credential probe failed at the HTTP transport; details withheld to protect credentials.");
        }
    }
}
