using System.Runtime.CompilerServices;

namespace Muse;

/// <summary>One half-closeable request and its single-consumer response stream.</summary>
public sealed class MuseRequest : IAsyncDisposable
{
    private readonly MuseNoiseConnection _connection;
    private readonly MuseNoiseConnection.PendingStream _pending;
    private bool _bodyClosed;
    private int _writing;
    private int _reading;
    private int _disposed;

    internal MuseRequest(MuseNoiseConnection connection, long id, MuseNoiseConnection.PendingStream pending, bool bodyClosed)
    { _connection = connection; StreamId = id; _pending = pending; _bodyClosed = bodyClosed; }

    /// <summary>Connection-local stream ID. Never reuse across reconnects.</summary>
    public long StreamId { get; }

    /// <summary>Sends more request body bytes; endBody=true permanently half-closes the request.</summary>
    /// <remarks>Await each write before the next. Concurrent body writers are rejected.</remarks>
    public async Task SendBodyAsync(ReadOnlyMemory<byte> data, bool endBody = false, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _writing, 1, 0) != 0)
            throw new InvalidOperationException("Concurrent body writes are not supported.");
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_bodyClosed) throw new InvalidOperationException("The request body is already closed.");
            await _connection.SendBodyAsync(StreamId, data, endBody, cancellationToken).ConfigureAwait(false);
            _bodyClosed = endBody;
        }
        finally { Volatile.Write(ref _writing, 0); }
    }

    /// <summary>Reads ordered response chunks once. Cancellation or early exit releases the request.</summary>
    public async IAsyncEnumerable<MuseResponseChunk> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _reading, 1) != 0) throw new InvalidOperationException("Only one response reader is supported.");
        try
        {
            await foreach (var chunk in _pending.Queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _connection.Consumed(chunk.Data.Length);
                yield return chunk;
            }
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    /// <summary>Stops local delivery and sends a bounded stream reset when still open.</summary>
    /// <remarks>A reset is not confirmation that remote reasoning or tools stopped.</remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _connection.ReleaseAsync(StreamId, _pending).ConfigureAwait(false);
    }
}
