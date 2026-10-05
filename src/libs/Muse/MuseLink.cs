using System.Net;
using System.Text.Json;
using Muse.Transport;

namespace Muse;

/// <summary>A logical, backend-hosted Muse link that advertises no executable commands.</summary>
/// <remarks>Unexpected tool invocations are denied in code. This class never opens files or starts processes.</remarks>
public sealed class MuseLink : IAsyncDisposable
{
    private readonly MuseRequest _request;
    private readonly string _registrationId;
    private readonly CancellationTokenSource _lifetime;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _completion;
    private int _disposed;

    private MuseLink(MuseRequest request, string registrationId, CancellationToken cancellationToken)
    {
        _request = request; _registrationId = registrationId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _completion = PumpAsync();
    }

    /// <summary>Completes when control ends, or faults on unpair, rejection, malformed data or network failure.</summary>
    public Task Completion => _completion;
    /// <summary>Whether registration succeeded and the control stream remains active.</summary>
    public bool IsRegistered => _ready.Task.IsCompletedSuccessfully && !_completion.IsCompleted;

    /// <summary>Registers an explicitly authorized logical node without requiring Bluetooth or a separate process.</summary>
    /// <remarks>The connection must already have a valid VM bearer. This does not perform initial account authorization.</remarks>
    public static async Task<MuseLink> RegisterAsync(MuseNoiseConnection connection, string nodeId, string displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId); ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeId.Length, 256);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(displayName.Length, 256);
        var request = await connection.OpenRequestAsync("POST", "/link-control", endBody: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid().ToString("D");
        var link = new MuseLink(request, id, cancellationToken);
        try
        {
            var record = JsonRecords.Encode(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("id", id); writer.WriteString("method", "link.register");
                writer.WriteStartObject("params"); writer.WriteString("node_id", nodeId); writer.WriteString("display_name", displayName);
                writer.WriteString("platform", "dotnet"); writer.WriteString("version", "0.1.0");
                writer.WriteString("device_family", "homehub"); writer.WriteString("model_id", "tryagi-muse-backend");
                writer.WriteBoolean("is_wakeup_supported", false); writer.WriteStartObject("commands_v2"); writer.WriteEndObject();
                writer.WriteEndObject(); writer.WriteEndObject();
            }, prefix: true);
            await request.SendBodyAsync(record, cancellationToken: cancellationToken).ConfigureAwait(false);
            await link._ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            return link;
        }
        catch { await link.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task PumpAsync()
    {
        var decoder = new JsonRecords(true);
        try
        {
            await foreach (var chunk in _request.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (chunk.StatusCode is { } status && status is < 200 or >= 300)
                    throw new HttpRequestException("Muse rejected link control.", null, (HttpStatusCode)status);
                foreach (var record in decoder.Feed(chunk.Data.Span))
                {
                    if (record.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == _registrationId &&
                        !record.TryGetProperty("method", out _))
                    {
                        if (record.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
                            throw new MuseProtocolException("Muse rejected logical-device registration.");
                        _ready.TrySetResult();
                    }
                    if (record.TryGetProperty("event", out var evt) && evt.ValueKind == JsonValueKind.String &&
                        evt.GetString() is "link.unpaired" or "node.unpaired")
                        throw new MuseProtocolException("Muse unpaired the logical device.");
                    if (record.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String && method.GetString() == "link.invoke" &&
                        record.TryGetProperty("id", out var invokeId))
                    {
                        if (invokeId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw new MuseProtocolException("Invalid invocation ID.");
                        var denial = JsonRecords.Encode(writer =>
                        {
                            writer.WriteStartObject(); writer.WriteString("method", "link.result"); writer.WritePropertyName("id"); invokeId.WriteTo(writer);
                            writer.WriteBoolean("ok", false); writer.WriteString("error", "Commands are disabled in this SDK session."); writer.WriteEndObject();
                        }, prefix: true);
                        await _request.SendBodyAsync(denial, cancellationToken: _lifetime.Token).ConfigureAwait(false);
                    }
                }
            }
            decoder.Complete();
            throw new MuseProtocolException("Muse closed link control.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { _ready.TrySetCanceled(); }
        catch (Exception exception) { _ready.TrySetException(exception); throw; }
    }

    /// <summary>Stops the link without revoking or changing the owner's provider credentials.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Completion preserves the exact failure; cleanup must not mask a registration or consumer exception.")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await _completion.ConfigureAwait(false); }
        catch (Exception) when (_completion.IsFaulted) { /* Completion retains the observable failure. */ }
        finally { await _request.DisposeAsync().ConfigureAwait(false); _lifetime.Dispose(); }
    }
}
