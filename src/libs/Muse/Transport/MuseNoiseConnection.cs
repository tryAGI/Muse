using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;
using Muse.Transport;

namespace Muse;

/// <summary>A single authenticated Noise connection with bounded multiplexed request streams.</summary>
/// <remarks>No automatic prompt replay or reconnect is performed. Create a new connection after failure.</remarks>
public sealed class MuseNoiseConnection : IAsyncDisposable
{
    private const int MaximumCiphertextBytes = 65535;
    private readonly WebSocket _socket;
    private readonly NoiseCipher _send;
    private readonly NoiseCipher _receive;
    private readonly NoiseFraming _framing;
    private readonly MuseConnectionOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<long, PendingStream> _streams = [];
    private readonly Task _reader;
    private long _nextId;
    private long _bufferedBytes;
    private int _closed;
    private int _disposed;

    private MuseNoiseConnection(WebSocket socket, NoiseCipher send, NoiseCipher receive, MuseConnectionOptions options,
        byte[] remoteKey)
    {
        _socket = socket; _send = send; _receive = receive; _options = options;
        _framing = new(options.MaximumMessageBytes);
        RemoteStaticPublicKey = remoteKey;
        _reader = ReceiveLoopAsync();
    }

    /// <summary>The observed Noise server static key, not automatically trusted or persisted as a pin.</summary>
    public ReadOnlyMemory<byte> RemoteStaticPublicKey { get; }
    /// <summary>Whether this connection has not yet closed or observed a transport failure.</summary>
    public bool IsConnected => Volatile.Read(ref _closed) == 0;
    /// <summary>Response bytes waiting for consumers, excluding data already handed to them.</summary>
    public long BufferedResponseBytes => Interlocked.Read(ref _bufferedBytes);

    /// <summary>Connects over TLS/WebSocket and performs a bounded Noise XX handshake.</summary>
    public static async Task<MuseNoiseConnection> ConnectAsync(MuseConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.HandshakeTimeout);
        ClientWebSocket? socket = null;
        NoiseCipher? send = null; NoiseCipher? receive = null;
        try
        {
            socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + options.VmAuthToken);
            socket.Options.SetRequestHeader("User-Agent", "tryAGI.Muse/0.1.0");
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(options.Endpoint, deadline.Token).ConfigureAwait(false);
            using var handshake = new NoiseHandshake();
            await socket.SendAsync(handshake.Start().AsMemory(), WebSocketMessageType.Binary, true, deadline.Token).ConfigureAwait(false);
            var second = await ReadMessageAsync(socket, deadline.Token).ConfigureAwait(false);
            var result = handshake.Finish(second, options.ExpectedServerStaticKey.Span);
            send = result.Send; receive = result.Receive;
            await socket.SendAsync(result.Message.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token).ConfigureAwait(false);
            var connection = new MuseNoiseConnection(socket, send, receive, options, result.RemoteKey);
            socket = null; send = null; receive = null;
            return connection;
        }
        finally
        {
            socket?.Dispose(); send?.Dispose(); receive?.Dispose();
        }
    }

    /// <summary>Opens an HTTP-like request inside the encrypted daemon service.</summary>
    /// <remarks>Set endBody=false for link-control or streaming input. Dispose the returned stream.</remarks>
    public async Task<MuseRequest> OpenRequestAsync(string method, string path,
        ReadOnlyMemory<byte> body = default, bool endBody = true,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method); ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Length > 8192 ||
            path.Contains('\r', StringComparison.Ordinal) || path.Contains('\n', StringComparison.Ordinal) || method.Length > 16 || !method.All(char.IsAsciiLetter))
            throw new ArgumentException("Use a bounded relative application path and HTTP method.");
        if (body.Length > _options.MaximumMessageBytes) throw new ArgumentException("Request exceeds the configured message limit.", nameof(body));
        if (headers is not null && (headers.Count > 64 || headers.Any(pair =>
            pair.Key.Length > 256 || pair.Value.Length > 8192 || pair.Key.Contains('\r', StringComparison.Ordinal) || pair.Key.Contains('\n', StringComparison.Ordinal) ||
            pair.Value.Contains('\r', StringComparison.Ordinal) || pair.Value.Contains('\n', StringComparison.Ordinal))))
            throw new ArgumentException("Invalid or oversized request headers.", nameof(headers));
        var id = Interlocked.Increment(ref _nextId);
        var envelope = WireProtocol.Request(id, method, path, body.Span, headers, endBody);
        if (envelope.Length > _options.MaximumMessageBytes) throw new ArgumentException("Encoded request exceeds the configured message limit.");
        var pending = new PendingStream(_options.MaximumQueuedFramesPerStream);
        lock (_sync)
        {
            EnsureConnected();
            if (_streams.Count >= _options.MaximumConcurrentStreams) throw new InvalidOperationException("Concurrent stream limit reached.");
            _streams.Add(id, pending);
        }
        var stream = new MuseRequest(this, id, pending, endBody);
        try
        {
            await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
    }

    /// <summary>Reads one finite response, enforcing both status and the response size budget.</summary>
    public async Task<byte[]> RequestAsync(string method, string path, ReadOnlyMemory<byte> body = default,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var stream = await OpenRequestAsync(method, path, body, headers: headers,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var ownership = stream.ConfigureAwait(false);
        using var output = new MemoryStream();
        await foreach (var chunk in stream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (chunk.StatusCode is { } status && status is < 200 or >= 300)
                throw new HttpRequestException("Muse application request was rejected.", null, (HttpStatusCode)status);
            if (output.Length + chunk.Data.Length > _options.MaximumMessageBytes)
                throw new MuseProtocolException("Response body exceeds the configured limit.");
            output.Write(chunk.Data.Span);
        }
        return output.ToArray();
    }

    internal async Task SendBodyAsync(long id, ReadOnlyMemory<byte> body, bool endBody, CancellationToken cancellationToken)
    {
        if (body.Length > _options.MaximumMessageBytes - 64) throw new ArgumentException("Body chunk exceeds the configured limit.", nameof(body));
        lock (_sync)
        {
            EnsureConnected();
            if (!_streams.ContainsKey(id)) throw new InvalidOperationException("Request stream is no longer open.");
        }
        await SendEnvelopeAsync(WireProtocol.Body(id, body.Span, endBody), cancellationToken).ConfigureAwait(false);
    }

    private async Task SendEnvelopeAsync(byte[] envelope, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _sendGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            EnsureConnected();
            foreach (var frame in NoiseFraming.Encode(envelope))
            {
                var encrypted = _send.Encrypt(frame);
                // Encryption and WebSocket send share one gate: nonce order is wire order.
                await _socket.SendAsync(encrypted.AsMemory(), WebSocketMessageType.Binary, true, linked.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // An interrupted send may have consumed a nonce or delivered part of a request.
            // Never continue this connection or silently resend the application operation.
            Fail(exception); throw;
        }
        finally { _sendGate.Release(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "The reader propagates every failure to all pending response channels and closes the transport.")]
    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var ciphertext = await ReadMessageAsync(_socket, _lifetime.Token).ConfigureAwait(false);
                var plaintext = _receive.Decrypt(ciphertext);
                var complete = _framing.Decode(plaintext);
                if (complete is null) continue;
                var response = WireProtocol.ParseResponse(complete);
                lock (_sync)
                {
                    if (!_streams.TryGetValue(response.StreamId, out var stream)) continue;
                    if (response.Kind == 5)
                    {
                        _streams.Remove(response.StreamId);
                        stream.Queue.Writer.TryComplete(new MuseProtocolException("Upstream reset the request stream."));
                        continue;
                    }
                    if (response.Kind == 3)
                    {
                        if (stream.ResponseSeen || response.Status is < 100 or > 599)
                            throw new MuseProtocolException("Invalid or duplicate response headers.");
                        stream.ResponseSeen = true;
                    }
                    else if (!stream.ResponseSeen) throw new MuseProtocolException("Response body arrived before its headers.");
                    var retained = Interlocked.Add(ref _bufferedBytes, response.Body.Length);
                    if (retained > _options.MaximumBufferedBytes || !stream.Queue.Writer.TryWrite(
                        new(response.Kind == 3 ? response.Status : null, response.Body, response.EndBody)))
                    {
                        Interlocked.Add(ref _bufferedBytes, -response.Body.Length);
                        throw new MuseProtocolException("A response consumer exceeded its bounded queue budget.");
                    }
                    if (response.EndBody)
                    {
                        _streams.Remove(response.StreamId); stream.Queue.Writer.TryComplete();
                    }
                }
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static async Task<byte[]> ReadMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaximumCiphertextBytes);
        try
        {
            var count = 0;
            while (true)
            {
                var read = await socket.ReceiveAsync(buffer.AsMemory(count, MaximumCiphertextBytes - count), cancellationToken).ConfigureAwait(false);
                if (read.MessageType == WebSocketMessageType.Close) throw new MuseProtocolException("Muse closed the WebSocket connection.");
                if (read.MessageType != WebSocketMessageType.Binary) throw new MuseProtocolException("Expected a binary WebSocket message.");
                count += read.Count;
                if (read.EndOfMessage) return buffer.AsSpan(0, count).ToArray();
                if (count >= MaximumCiphertextBytes) throw new MuseProtocolException("WebSocket message exceeds the wire limit.");
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("Muse connection is closed; create a new authorized connection.");
    }

    private void Fail(Exception exception)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _lifetime.Cancel(); _socket.Abort();
        lock (_sync)
        {
            foreach (var stream in _streams.Values) stream.Queue.Writer.TryComplete(exception);
            _streams.Clear();
        }
    }

    internal void Consumed(int length) => Interlocked.Add(ref _bufferedBytes, -length);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Reset cleanup cannot mask a consumer exception; any failure terminates all streams through Fail.")]
    internal async ValueTask ReleaseAsync(long id, PendingStream pending)
    {
        bool reset;
        lock (_sync)
        {
            reset = _streams.Remove(id); pending.Queue.Writer.TryComplete();
        }
        while (pending.Queue.Reader.TryRead(out var chunk)) Consumed(chunk.Data.Length);
        if (!reset || !IsConnected) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await SendEnvelopeAsync(WireProtocol.Cancel(id), timeout.Token).ConfigureAwait(false); }
        catch (Exception exception) { Fail(exception); }
    }

    /// <summary>Closes the connection and all owned streams; never restarts or replays them.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Fail(new ObjectDisposedException(nameof(MuseNoiseConnection)));
        await _reader.ConfigureAwait(false);
        await _sendGate.WaitAsync().ConfigureAwait(false);
        try { _send.Dispose(); _receive.Dispose(); _socket.Dispose(); }
        finally { _sendGate.Release(); }
        _sendGate.Dispose(); _lifetime.Dispose();
    }

    internal sealed class PendingStream(int capacity)
    {
        internal readonly Channel<MuseResponseChunk> Queue = Channel.CreateBounded<MuseResponseChunk>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = true });
        internal bool ResponseSeen;
    }
}
