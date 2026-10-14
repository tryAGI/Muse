using System.Net;
using System.Text.Json;

namespace Muse.IntegrationTests;

/// <summary>A bounded, explicitly selected real-service probe; never counted as a successful chat E2E.</summary>
[TestClass]
[TestCategory("LiveProbe")]
public sealed class CredentialProbeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SdkTokenDeviceApiAccessProbe(bool matchPythonHeaders)
    {
        var sdkToken = Environment.GetEnvironmentVariable("MUSE_SDK_TOKEN") is { Length: > 0 } value
            ? value : throw new AssertInconclusiveException("MUSE_SDK_TOKEN is required for the explicit live probe.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        if (matchPythonHeaders)
        {
            var reportPath = Environment.GetEnvironmentVariable("MUSE_PYTHON_AUTH_REPORT") is { Length: > 0 } path
                ? path : throw new AssertInconclusiveException("Run scripts/test-live.sh probe-sdk-token to obtain the Python header baseline.");
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            http.DefaultRequestHeaders.UserAgent.ParseAdd(report.RootElement.GetProperty("userAgent").GetString()!);
        }
        TestContext.WriteLine("C# discovery headers: {0}.", matchPythonHeaders ? "matching Python User-Agent and X-API-Version" : "SDK defaults");
        // The raw generated constructor is intentional in this negative diagnostic.
        // Application examples use the role-aware factories instead.
        using var client = new MuseClient(sdkToken, httpClient: http, disposeHttpClient: false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var response = await client.FetchVmsAsync(cancellationToken: deadline.Token);
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
            Assert.Inconclusive($"SDK-token-only authorization was rejected (HTTP {(int)exception.StatusCode}); this does not test whether the token is valid for gadget pairing. Separate account authorization remains unverified. No live chat was exercised.");
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
