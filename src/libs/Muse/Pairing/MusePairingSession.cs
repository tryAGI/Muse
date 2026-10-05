using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Muse.Transport;

namespace Muse;

/// <summary>Server-side community pairing v5. The Apple relay forwards JSON without decrypting it.</summary>
/// <remarks>
/// This is a single, expiring enrollment attempt, not a Bluetooth driver or account login API.
/// Bind it to an authenticated owner, relay device and connection epoch in the host application.
/// Community pairing does not authenticate a manufacturer or prevent an active local MITM.
/// </remarks>
public sealed class MusePairingSession : IDisposable
{
    private const string Label = "hatch-link ble setup v1";
    private readonly object _gate = new();
    private readonly string _mac;
    private readonly string _firmware;
    private readonly TimeProvider _time;
    private readonly long _created;
    private readonly Func<ECDiffieHellman> _createKey;
    private readonly Func<byte[]> _createNonce;
    private string? _sdkToken;
    private AesGcm? _receive;
    private AesGcm? _send;
    private string? _sessionId;
    private ulong _rxCounter;
    private ulong _txCounter;
    private long _phaseStarted;
    private TimeSpan _phaseTimeout = TimeSpan.FromMinutes(10);
    private Phase _phase;

    private enum Phase { Created, AwaitingConfirmation, Confirmed, Provisioning, Closed }

    /// <summary>Creates a fresh attempt using a persisted, logical MAC-shaped identity, never a hardware address.</summary>
    public MusePairingSession(string logicalMac, string sdkToken, string firmwareVersion, TimeProvider? timeProvider = null)
        : this(logicalMac, sdkToken, firmwareVersion, timeProvider ?? TimeProvider.System,
            () => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256), () => RandomNumberGenerator.GetBytes(16)) { }

    internal MusePairingSession(string logicalMac, string sdkToken, string firmwareVersion, TimeProvider timeProvider,
        Func<ECDiffieHellman> createKey, Func<byte[]> createNonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalMac);
        if (logicalMac.Length != 17 || logicalMac.Where((_, i) => i % 3 == 2).Any(c => c != ':') ||
            logicalMac.Where((_, i) => i % 3 != 2).Any(c => !char.IsAsciiHexDigit(c) || char.IsUpper(c)))
            throw new ArgumentException("Use a lower-case logical MAC-shaped identity.", nameof(logicalMac));
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkToken);
        ValidateText(sdkToken, 512, nameof(sdkToken));
        if (!sdkToken.StartsWith("mgst_", StringComparison.Ordinal))
            throw new ArgumentException("A Gadget SDK token is required for enrollment.", nameof(sdkToken));
        ValidateText(firmwareVersion, 128, nameof(firmwareVersion));
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(createKey);
        ArgumentNullException.ThrowIfNull(createNonce);
        _mac = logicalMac; _sdkToken = sdkToken; _firmware = firmwareVersion;
        _time = timeProvider; _createKey = createKey; _createNonce = createNonce;
        _created = _phaseStarted = _time.GetTimestamp();
        var suffix = logicalMac.Replace(":", string.Empty, StringComparison.Ordinal)[^6..];
        NodeId = "homelink-" + suffix;
        DeviceId = "hatch-link:" + logicalMac;
        AdvertisementName = "MuseGadget" + suffix.ToUpperInvariant();
    }

    /// <summary>Stable provider node identifier. Persist with the device grant across backend restarts.</summary>
    public string NodeId { get; }
    /// <summary>Provider setup identifier, distinct from the Advantage device ID.</summary>
    public string DeviceId { get; }
    /// <summary>Exact local name expected by Muse discovery; advertise this on the selected relay only.</summary>
    public string AdvertisementName { get; }

    /// <summary>Returns non-secret metadata; networkReady must reflect the enrollment backend's actual availability.</summary>
    public byte[] GetDeviceInfo(bool networkReady) => Encode(writer =>
    {
        writer.WriteString("type", "device_info"); WriteIdentity(writer);
        writer.WriteString("version", _firmware); writer.WriteString("build_sha", string.Empty);
        writer.WriteNumber("pairing_protocol", 5); writer.WriteBoolean("network_ready", networkReady);
    });

    /// <summary>Accepts the public client hello and returns the public pairing-ready message.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5390", Justification = "Keys derive from ephemeral P-256 ECDH. Fixed UTF-8 strings are HKDF domain-separation information, not keys.")]
    public byte[] HandleClientHello(ReadOnlyMemory<byte> json)
    {
        lock (_gate)
        {
            Ensure(Phase.Created);
            try
            {
                using var document = Parse(json);
                var root = document.RootElement;
                if (Text(root, "action") != "pairing_client_hello" || root.GetProperty("version").GetInt32() != 5 ||
                    Text(root, "pairing_auth") != "none" || Text(root, "pairing_policy") != "confirm_app")
                    throw new MuseProtocolException("Unsupported pairing hello.");
                var mobilePub = Decode(Text(root, "mobile_pub"), 65);
                var mobileNonce = Decode(Text(root, "mobile_nonce"), 16);
                if (mobilePub.Length != 65 || mobilePub[0] != 4 || mobileNonce.Length != 16)
                    throw new MuseProtocolException("Invalid pairing key or nonce length.");
                using var peer = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = mobilePub[1..33], Y = mobilePub[33..65] },
                });
                using var local = _createKey();
                var parameters = local.ExportParameters(false);
                var publicKey = new byte[65]; publicKey[0] = 4;
                parameters.Q.X!.CopyTo(publicKey, 1); parameters.Q.Y!.CopyTo(publicKey, 33);
                var nonce = _createNonce();
                if (nonce.Length != 16) throw new MuseProtocolException("Invalid local nonce.");
                var transcript = string.Join('\n',
                    "hatch-link-pairing-v5", "version=5", "initiator_role=mobile", "responder_role=link",
                    "device_id=" + DeviceId, "node_id=" + NodeId, "mac=" + _mac, "model=hatch_link",
                    "firmware_version=" + _firmware, "selected_cipher_suite=p256-hkdf-sha256-aes-gcm-v1",
                    "pairing_auth=none", "pairing_auth_epoch=0", "pairing_policy=confirm_app", "confirm_timeout_seconds=0",
                    "mobile_pub=" + B64(mobilePub), "device_pub=" + B64(publicKey),
                    "mobile_nonce=" + B64(mobileNonce), "device_nonce=" + B64(nonce));
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(transcript));
                var secret = local.DeriveRawSecretAgreement(peer.PublicKey);
                byte[]? sessionSecret = null; byte[]? rx = null; byte[]? tx = null;
                try
                {
                    var salt = SHA256.HashData([.. mobileNonce, .. nonce, .. hash]);
                    sessionSecret = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, Encoding.UTF8.GetBytes(Label));
                    rx = HKDF.Expand(HashAlgorithmName.SHA256, sessionSecret, 32, "mobile->device"u8.ToArray());
                    tx = HKDF.Expand(HashAlgorithmName.SHA256, sessionSecret, 32, "device->mobile"u8.ToArray());
                    using var idHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    idHash.AppendData("hatch-link session id v1"u8); idHash.AppendData(hash); idHash.AppendData(secret);
                    _sessionId = B64(idHash.GetHashAndReset().AsSpan(0, 16));
                    _receive = new AesGcm(rx, 16); _send = new AesGcm(tx, 16);
                    SetPhase(Phase.AwaitingConfirmation, TimeSpan.FromSeconds(60));
                    return Encode(writer =>
                    {
                        writer.WriteString("type", "pairing_ready"); writer.WriteNumber("version", 5); WriteIdentity(writer);
                        writer.WriteString("firmware_version", _firmware); writer.WriteString("device_pub", B64(publicKey));
                        writer.WriteString("device_nonce", B64(nonce)); writer.WriteString("transcript_hash", B64(hash));
                        writer.WriteString("session_id", _sessionId);
                    });
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                    if (sessionSecret is not null) CryptographicOperations.ZeroMemory(sessionSecret);
                    if (rx is not null) CryptographicOperations.ZeroMemory(rx);
                    if (tx is not null) CryptographicOperations.ZeroMemory(tx);
                }
            }
            catch { Close(); throw; }
        }
    }

    /// <summary>Validates the first encrypted record and returns the encrypted SDK-token handoff to the Muse app.</summary>
    public byte[] ConfirmClient(ReadOnlyMemory<byte> envelope)
    {
        lock (_gate)
        {
            Ensure(Phase.AwaitingConfirmation);
            try
            {
                using var message = Open(envelope);
                if (message.RootElement.EnumerateObject().Count() != 1 || Text(message.RootElement, "action") != "pairing_client_finished")
                    throw new MuseProtocolException("Expected client-finished confirmation.");
                SetPhase(Phase.Confirmed, TimeSpan.FromSeconds(120));
                return Seal(writer =>
                {
                    writer.WriteString("type", "status"); writer.WriteString("status", "pairing_confirmed");
                    writer.WriteString("sdk_token", _sdkToken);
                });
            }
            catch { Close(); throw; }
        }
    }

    /// <summary>Consumes one encrypted setup command exactly once. Credentials are returned only to the trusted host.</summary>
    public MusePairingCommand ReceiveCommand(ReadOnlyMemory<byte> envelope)
    {
        lock (_gate)
        {
            Ensure(Phase.Confirmed);
            try
            {
                using var document = Open(envelope);
                var root = document.RootElement;
                return Text(root, "action") switch
                {
                    "wifi_scan" => new MusePairingCommand(null),
                    "provision_v2" => new MusePairingCommand(ReadProvisioning(root)),
                    _ => throw new MuseProtocolException("Unsupported enrollment command."),
                };
            }
            catch { Close(); throw; }
        }
    }

    /// <summary>Encrypts a bounded network/provisioning-progress response; terminal status is reserved for FinishProvisioning.</summary>
    public byte[] EncryptResponse(JsonElement message)
    {
        lock (_gate)
        {
            Ensure(_phase == Phase.Provisioning ? Phase.Provisioning : Phase.Confirmed);
            if (message.ValueKind != JsonValueKind.Object || message.GetRawText().Length > 8192 ||
                message.TryGetProperty("status", out var status) && status.GetString() is "auth_ok" or "auth_failed")
                throw new ArgumentException("Use a bounded setup object, not a terminal authorization status.", nameof(message));
            return Seal(writer => { foreach (var property in message.EnumerateObject()) property.WriteTo(writer); });
        }
    }

    /// <summary>Consumes encrypted provision_v2 and returns credentials ONLY to the trusted enrollment host.</summary>
    /// <remarks>Verify the allowed endpoints, discover VMs and atomically persist the grant before FinishProvisioning(true).</remarks>
    private MuseProvisioningCredentials ReadProvisioning(JsonElement root)
    {
                if (Text(root, "action") != "provision_v2" || Text(root, "token_type") != "device")
                    throw new MuseProtocolException("Expected device provisioning, not an SDK-token login.");
                var access = Text(root, "access_token"); var refresh = Text(root, "refresh_token");
                ValidateText(access, 8192, "access_token"); ValidateText(refresh, 8192, "refresh_token");
                if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) ||
                    access.StartsWith("mgst_", StringComparison.Ordinal) || refresh.StartsWith("mgst_", StringComparison.Ordinal))
                    throw new MuseProtocolException("Provisioning requires separate device access and refresh credentials.");
                var result = new MuseProvisioningCredentials(NodeId, DeviceId, access, refresh,
                    OptionalText(root, "api_url_v2"), OptionalText(root, "noise_host"));
                // Wi-Fi passwords, user names and unrelated app fields are deliberately not retained.
                SetPhase(Phase.Provisioning, TimeSpan.FromSeconds(120));
                return result;
    }

    /// <summary>Returns encrypted auth_ok only after the host has verified AND durably committed this exact grant.</summary>
    /// <remarks>Call false after rejection. Both outcomes permanently close this attempt. A local handshake is not account authorization.</remarks>
    public byte[] FinishProvisioning(bool verifiedAndPersisted)
    {
        lock (_gate)
        {
            Ensure(Phase.Provisioning);
            try { return Seal(w => { w.WriteString("type", "status"); w.WriteString("status", verifiedAndPersisted ? "auth_ok" : "auth_failed"); }); }
            finally { Close(); }
        }
    }

    private JsonDocument Open(ReadOnlyMemory<byte> envelope)
    {
        using var document = Parse(envelope);
        var root = document.RootElement;
        if (Text(root, "action") != "pairing_encrypted" || Text(root, "session_id") != _sessionId ||
            !ulong.TryParse(Text(root, "counter"), NumberStyles.None, CultureInfo.InvariantCulture, out var counter) ||
            counter != _rxCounter || counter == ulong.MaxValue)
            throw new MuseProtocolException("Invalid, stale or replayed pairing record.");
        var ciphertext = Decode(Text(root, "ciphertext"), 12288); var tag = Decode(Text(root, "tag"), 16);
        if (tag.Length != 16) throw new MuseProtocolException("Invalid authentication tag.");
        var plaintext = new byte[ciphertext.Length];
        try
        {
            _receive!.Decrypt(Nonce(0, counter), ciphertext, tag, plaintext, Aad("m2d", counter));
            _rxCounter++;
            // Parse through a stream so JsonDocument owns its storage before plaintext is cleared.
            using var stream = new MemoryStream(plaintext, writable: false);
            var parsed = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            ValidateObject(parsed);
            return parsed;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private byte[] Seal(Action<Utf8JsonWriter> write)
    {
        if (_txCounter == ulong.MaxValue) { Close(); throw new MuseProtocolException("Pairing counter exhausted."); }
        var plaintext = Encode(write); var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
        var counter = _txCounter++;
        try
        {
            _send!.Encrypt(Nonce(1, counter), plaintext, ciphertext, tag, Aad("d2m", counter));
            return Encode(w =>
            {
                w.WriteString("type", "pairing_encrypted"); w.WriteString("session_id", _sessionId);
                w.WriteString("counter", counter.ToString(CultureInfo.InvariantCulture));
                w.WriteString("ciphertext", B64(ciphertext)); w.WriteString("tag", B64(tag));
            });
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private void WriteIdentity(Utf8JsonWriter writer)
    {
        writer.WriteString("device_id", DeviceId); writer.WriteString("node_id", NodeId); writer.WriteString("mac", _mac);
        writer.WriteString("model", "hatch_link"); writer.WriteString("pairing_auth", "none");
        writer.WriteNumber("pairing_auth_epoch", 0); writer.WriteString("pairing_policy", "confirm_app");
    }

    private void SetPhase(Phase phase, TimeSpan timeout)
    { _phase = phase; _phaseStarted = _time.GetTimestamp(); _phaseTimeout = timeout; }

    private void Ensure(Phase phase)
    {
        if (_time.GetElapsedTime(_created) > TimeSpan.FromMinutes(10) || _time.GetElapsedTime(_phaseStarted) > _phaseTimeout)
        { Close(); throw new MuseProtocolException("Pairing attempt expired; explicitly start a fresh attempt."); }
        if (_phase != phase) throw new InvalidOperationException("Invalid pairing phase or closed attempt.");
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > 16384) throw new MuseProtocolException("Pairing message exceeds the limit.");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        ValidateObject(document);
        return document;
    }

    private static void ValidateObject(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == document.RootElement.EnumerateObject().Count())
            return;
        document.Dispose(); throw new MuseProtocolException("Expected an unambiguous pairing object.");
    }

    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new MuseProtocolException("Missing or invalid pairing field.");
    private static string? OptionalText(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string B64(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value, int maximumBytes)
    {
        if (value.Length > ((maximumBytes + 2) / 3 * 4) || value.Length % 4 == 1 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new MuseProtocolException("Invalid base64url pairing field.");
        var result = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (result.Length > maximumBytes) throw new MuseProtocolException("Oversized pairing field.");
        return result;
    }
    private byte[] Aad(string direction, ulong counter) => Encoding.UTF8.GetBytes($"{Label}|{_sessionId}|{direction}|{counter.ToString(CultureInfo.InvariantCulture)}");
    private static byte[] Nonce(byte direction, ulong counter)
    { var nonce = new byte[12]; nonce[0] = direction; BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter); return nonce; }
    private static byte[] Encode(Action<Utf8JsonWriter> write) => JsonRecords.Encode(w => { w.WriteStartObject(); write(w); w.WriteEndObject(); });
    private static void ValidateText(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException("Invalid bounded text.", parameter);
    }
    private void Close()
    {
        _receive?.Dispose(); _receive = null; _send?.Dispose(); _send = null; _sdkToken = null; _phase = Phase.Closed;
    }
    /// <summary>Discards ephemeral keys and invalidates this attempt. No provider credentials are revoked.</summary>
    public void Dispose() { lock (_gate) { Close(); } }
    /// <summary>Never includes credentials, decrypted commands or key material.</summary>
    public override string ToString() => "MusePairingSession (secrets redacted)";
}

/// <summary>Provider-issued credentials for the enrollment host, never for the BLE relay or diagnostics.</summary>
public sealed class MuseProvisioningCredentials
{
    internal MuseProvisioningCredentials(string nodeId, string deviceId, string accessToken, string refreshToken, string? apiRoot, string? noiseHost)
    { NodeId = nodeId; DeviceId = deviceId; AccessToken = accessToken; RefreshToken = refreshToken; ApiRoot = apiRoot; NoiseHost = noiseHost; }
    /// <summary>Preserve the logical node ID when refreshing and reconnecting.</summary>
    public string NodeId { get; }
    /// <summary>Original setup identity, not the BLE hardware address.</summary>
    public string DeviceId { get; }
    /// <summary>Sensitive device access credential.</summary>
    public string AccessToken { get; }
    /// <summary>Sensitive device refresh credential.</summary>
    public string RefreshToken { get; }
    /// <summary>Untrusted endpoint metadata; validate against a provider allowlist before sending any credential.</summary>
    public string? ApiRoot { get; }
    /// <summary>Untrusted endpoint metadata; never connect without host validation.</summary>
    public string? NoiseHost { get; }
    /// <summary>Redacts secrets from incidental string formatting; callers must also prevent object serialization in logs.</summary>
    public override string ToString() => "MuseProvisioningCredentials (redacted)";
}
