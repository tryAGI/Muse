namespace Muse;

/// <summary>Explicit endpoint, authorization, and resource limits for a Muse VM connection.</summary>
public sealed class MuseConnectionOptions
{
    /// <summary>Authorized wss endpoint, including the selected vm_id query parameter.</summary>
    public required Uri Endpoint { get; init; }
    /// <summary>VM bearer credential. This is neither an SDK token nor a device refresh token.</summary>
    public required string VmAuthToken { get; init; }
    /// <summary>Optional independently provisioned 32-byte Noise server static public-key pin.</summary>
    public ReadOnlyMemory<byte> ExpectedServerStaticKey { get; init; }
    /// <summary>Maximum duration for WebSocket connection and the complete Noise handshake.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Maximum plaintext message and aggregate incomplete-fragment memory.</summary>
    public int MaximumMessageBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Maximum total queued response body bytes across all streams.</summary>
    public int MaximumBufferedBytes { get; init; } = 8 * 1024 * 1024;
    /// <summary>Maximum simultaneously open application streams.</summary>
    public int MaximumConcurrentStreams { get; init; } = 16;
    /// <summary>Maximum queued response frames per application stream.</summary>
    public int MaximumQueuedFramesPerStream { get; init; } = 256;
    /// <summary>Allows ws only for explicit loopback interoperability tests. Never enable for a real credential.</summary>
    public bool AllowInsecureLoopback { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(VmAuthToken);
        if (!Endpoint.IsAbsoluteUri || (Endpoint.Scheme != "wss" &&
            !(AllowInsecureLoopback && Endpoint.Scheme == "ws" && Endpoint.IsLoopback)) ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Fragment))
            throw new ArgumentException("Use a secure WebSocket endpoint without user-info or fragments.");
        if (VmAuthToken.StartsWith("mgst_", StringComparison.Ordinal) || VmAuthToken.Contains('\r', StringComparison.Ordinal) || VmAuthToken.Contains('\n', StringComparison.Ordinal))
            throw new ArgumentException("A VM bearer is required; SDK tokens cannot authorize a VM connection.");
        if (ExpectedServerStaticKey.Length is not (0 or 32))
            throw new ArgumentException("The server static-key pin must be 32 bytes.");
        if (HandshakeTimeout <= TimeSpan.Zero || HandshakeTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(HandshakeTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumMessageBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumMessageBytes, 16 * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumBufferedBytes, MaximumMessageBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumBufferedBytes, 64 * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumConcurrentStreams, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumConcurrentStreams, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumQueuedFramesPerStream, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumQueuedFramesPerStream, 4096);
    }

    /// <summary>Returns only non-secret configuration information.</summary>
    public override string ToString() => "MuseConnectionOptions (credentials redacted)";
}

/// <summary>One response header/body/terminal observation from a multiplexed request.</summary>
public sealed class MuseResponseChunk
{
    internal MuseResponseChunk(int? statusCode, ReadOnlyMemory<byte> data, bool endOfBody)
    { StatusCode = statusCode; Data = data; EndOfBody = endOfBody; }
    /// <summary>HTTP-style status on the initial response, otherwise null.</summary>
    public int? StatusCode { get; }
    /// <summary>Body bytes owned by this observation.</summary>
    public ReadOnlyMemory<byte> Data { get; }
    /// <summary>Whether the response body has been half-closed by the server.</summary>
    public bool EndOfBody { get; }
}
