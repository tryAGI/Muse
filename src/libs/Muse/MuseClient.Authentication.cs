namespace Muse;

public sealed partial class MuseClient
{
    /// <summary>Creates a device API client from separately authorized device access credentials.</summary>
    /// <remarks>An mgst_ integration token is not a signed-in user credential. This factory performs no login.</remarks>
    public static MuseClient ForDeviceAccessToken(string deviceAccessToken, HttpClient? httpClient = null,
        Uri? baseUri = null, bool disposeHttpClient = true)
    {
        ValidateDeviceCredential(deviceAccessToken, nameof(deviceAccessToken));
        if (deviceAccessToken.StartsWith("hatch_refresh:", StringComparison.Ordinal))
            throw new ArgumentException("A refresh token cannot be used for VM discovery.", nameof(deviceAccessToken));
        return new(deviceAccessToken, httpClient: httpClient, baseUri: baseUri, disposeHttpClient: disposeHttpClient);
    }

    /// <summary>Creates a refresh client with exactly one hatch_refresh: bearer prefix.</summary>
    /// <remarks>Use RefreshDeviceTokenAsync and pass the SDK token in its sdkToken body argument, not as authorization.</remarks>
    public static MuseClient ForDeviceRefreshToken(string deviceRefreshToken, HttpClient? httpClient = null,
        Uri? baseUri = null, bool disposeHttpClient = true)
    {
        ValidateDeviceCredential(deviceRefreshToken, nameof(deviceRefreshToken));
        const string prefix = "hatch_refresh:";
        var raw = deviceRefreshToken;
        while (raw.StartsWith(prefix, StringComparison.Ordinal)) raw = raw[prefix.Length..];
        ValidateDeviceCredential(raw, nameof(deviceRefreshToken));
        if (raw.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("Unsupported refresh token prefix.", nameof(deviceRefreshToken));
        return new(prefix + raw, httpClient: httpClient, baseUri: baseUri, disposeHttpClient: disposeHttpClient);
    }

    private static void ValidateDeviceCredential(string token, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token, parameterName);
        if (token.Length > 16384 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            throw new ArgumentException("The credential is too long or contains whitespace/control characters.", parameterName);
        if (token.StartsWith("mgst_", StringComparison.Ordinal))
            throw new ArgumentException("A Muse Gadget SDK token identifies the integration; initial account authorization must supply separate device credentials. This does not mean the SDK token is invalid for pairing.", parameterName);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "AutoSDK declares this customization hook as an instance partial method.")]
    partial void PrepareFetchVmsArguments(HttpClient httpClient, ref string? xApiVersion)
    {
        // The generated optional argument does not apply the schema's documented default.
        xApiVersion ??= "1.0.0";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "AutoSDK declares this customization hook as an instance partial method.")]
    partial void PrepareFetchVmsRequest(HttpClient httpClient, HttpRequestMessage httpRequestMessage, string? xApiVersion)
        => AddUserAgent(httpClient, httpRequestMessage);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "AutoSDK declares this customization hook as an instance partial method.")]
    partial void PrepareRefreshDeviceTokenRequest(HttpClient httpClient, HttpRequestMessage httpRequestMessage,
        RefreshDeviceTokenRequest request) => AddUserAgent(httpClient, httpRequestMessage);

    private static void AddUserAgent(HttpClient httpClient, HttpRequestMessage request)
    {
        // Preserve consumer identification and do not mutate a shared HttpClient's default headers.
        if (request.Headers.UserAgent.Count == 0 && httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            request.Headers.UserAgent.ParseAdd("tryAGI.Muse/0.1.0");
    }
}
