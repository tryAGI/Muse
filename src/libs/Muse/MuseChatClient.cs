using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Muse.Transport;

namespace Muse;

/// <summary>Text submission and raw, bounded chat events over an existing authorized Muse connection.</summary>
/// <remarks>Owns no network connection and executes no tools. Event attribution and turn completion remain explicit.</remarks>
public sealed class MuseChatClient
{
    private readonly MuseNoiseConnection _connection;
    private readonly string _deviceId;

    /// <summary>Creates a text client for the already registered logical device.</summary>
    public MuseChatClient(MuseNoiseConnection connection, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deviceId.Length, 256);
        _connection = connection; _deviceId = deviceId;
    }

    /// <summary>Submits one text message to an explicit side chat and returns only its HTTP acknowledgement.</summary>
    /// <remarks>This acknowledgement is not the assistant response or proof a turn finished. Unknown delivery is never retried.</remarks>
    public async Task<JsonElement> SendTextAsync(string message, string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message); ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Length, 65536);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sessionId.Length, 256);
        var bytes = JsonRecords.Encode(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("message", message);
            writer.WriteString("output_modality", "text"); writer.WriteString("device_id", _deviceId);
            writer.WriteString("session_id", sessionId); writer.WriteEndObject();
        });
        var response = await _connection.RequestAsync("POST", "/chat/stream", bytes,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Content-Type"] = "application/json", ["x-request-id"] = Guid.NewGuid().ToString("D"),
                ["x-app-id"] = "tryagi-muse",
            }, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 32 });
        return document.RootElement.Clone();
    }

    /// <summary>Opens the event subscription and waits for a successful response header before returning.</summary>
    public async Task<MuseChatSubscription> SubscribeAsync(CancellationToken cancellationToken = default)
    {
        var request = await _connection.OpenRequestAsync("POST", "/chat/subscribe", "{}"u8.ToArray(),
            headers: new Dictionary<string, string>(StringComparer.Ordinal)
            { ["Content-Type"] = "application/json", ["Accept"] = "application/x-ndjson", ["x-app-id"] = "tryagi-muse" },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reader = request.ReadAllAsync(lifetime.Token).GetAsyncEnumerator(lifetime.Token);
        try
        {
            if (!await reader.MoveNextAsync().ConfigureAwait(false) || reader.Current.StatusCode is not { } status)
                throw new MuseProtocolException("Chat subscription ended before response headers.");
            if (status is < 200 or >= 300) throw new HttpRequestException("Muse rejected the chat subscription.", null, (HttpStatusCode)status);
            return new(request, reader, lifetime);
        }
        catch
        {
            await reader.DisposeAsync().ConfigureAwait(false); await request.DisposeAsync().ConfigureAwait(false);
            lifetime.Dispose(); throw;
        }
    }
}

/// <summary>A single-consumer, non-reconnecting subscription to raw Muse chat events.</summary>
/// <remarks>Events are untrusted provider data. Filter conversation and message IDs before rendering or speaking.</remarks>
public sealed class MuseChatSubscription : IAsyncDisposable
{
    private readonly MuseRequest _request;
    private readonly IAsyncEnumerator<MuseResponseChunk> _reader;
    private readonly CancellationTokenSource _lifetime;
    private int _reading;
    private int _disposed;

    internal MuseChatSubscription(MuseRequest request, IAsyncEnumerator<MuseResponseChunk> reader, CancellationTokenSource lifetime)
    { _request = request; _reader = reader; _lifetime = lifetime; }

    /// <summary>Reads complete bounded JSON records, preserving unknown event fields without inventing turn completion.</summary>
    /// <remarks>Use the subscription's cancellation token to stop an in-flight read. Do not dispose concurrently with enumeration.</remarks>
    public async IAsyncEnumerable<JsonElement> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _reading, 1) != 0) throw new InvalidOperationException("Only one chat-event reader is supported.");
        using var registration = cancellationToken.Register(() => _lifetime.Cancel());
        var decoder = new JsonRecords(false);
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var record in decoder.Feed(_reader.Current.Data.Span)) yield return record;
            }
            while (await _reader.MoveNextAsync().ConfigureAwait(false));
            foreach (var record in decoder.Complete()) yield return record;
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    /// <summary>Releases the subscription after enumeration has stopped.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await _request.DisposeAsync().ConfigureAwait(false);
            await _reader.DisposeAsync().ConfigureAwait(false);
        }
        finally { _lifetime.Dispose(); }
    }
}
