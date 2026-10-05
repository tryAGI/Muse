using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Muse;

// Two independent ephemeral peers, no radio, credentials, file persistence or provider calls.
using var backend = new MusePairingSession("02:00:00:00:00:01", "mgst_synthetic_only", "1.0.0");
using var phone = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
var point = phone.ExportParameters(false).Q;
byte[] publicKey = [4, .. point.X!, .. point.Y!];
var mobileNonce = RandomNumberGenerator.GetBytes(16);
var hello = Encode(w =>
{
    w.WriteString("action", "pairing_client_hello"); w.WriteNumber("version", 5);
    w.WriteString("pairing_auth", "none"); w.WriteString("pairing_policy", "confirm_app");
    w.WriteString("mobile_pub", B64(publicKey)); w.WriteString("mobile_nonce", B64(mobileNonce));
});
using var ready = JsonDocument.Parse(backend.HandleClientHello(hello));
var root = ready.RootElement;
var devicePub = Decode(root.GetProperty("device_pub").GetString()!);
using var peer = ECDiffieHellman.Create(new ECParameters
{ Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = devicePub[1..33], Y = devicePub[33..65] } });
var secret = phone.DeriveRawSecretAgreement(peer.PublicKey);
var hash = Decode(root.GetProperty("transcript_hash").GetString()!);
var nonce = Decode(root.GetProperty("device_nonce").GetString()!);
var salt = SHA256.HashData([.. mobileNonce, .. nonce, .. hash]);
var sessionSecret = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, "hatch-link ble setup v1"u8.ToArray());
var tx = HKDF.Expand(HashAlgorithmName.SHA256, sessionSecret, 32, "mobile->device"u8.ToArray());
var rx = HKDF.Expand(HashAlgorithmName.SHA256, sessionSecret, 32, "device->mobile"u8.ToArray());
var sessionId = root.GetProperty("session_id").GetString()!;
try
{
    var response = backend.ConfirmClient(Seal("{\"action\":\"pairing_client_finished\"}"u8.ToArray(), tx, sessionId, 0));
    using var confirmed = Open(response, rx, sessionId, 0);
    if (confirmed.RootElement.GetProperty("sdk_token").GetString() != "mgst_synthetic_only") throw new InvalidOperationException("SDK handoff mismatch.");
    var grant = backend.ReceiveCommand(Seal("{\"action\":\"provision_v2\",\"token_type\":\"device\",\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh\"}"u8.ToArray(), tx, sessionId, 1));
    if (grant.Credentials?.AccessToken != "synthetic-access" || grant.Credentials.RefreshToken != "synthetic-refresh") throw new InvalidOperationException("Grant mismatch.");
    // No real authorization was verified; explicitly reject the synthetic grant.
    using var completion = Open(backend.FinishProvisioning(false), rx, sessionId, 1);
    if (completion.RootElement.GetProperty("status").GetString() != "auth_failed") throw new InvalidOperationException("Rejection mismatch.");
    Console.WriteLine("NativeAOT ephemeral P-256 enrollment and encrypted grant handoff passed (synthetic; no live authorization).");
}
finally
{
    CryptographicOperations.ZeroMemory(secret); CryptographicOperations.ZeroMemory(sessionSecret);
    CryptographicOperations.ZeroMemory(tx); CryptographicOperations.ZeroMemory(rx);
}

static byte[] Seal(byte[] plain, byte[] key, string id, int counter)
{
    var encrypted = new byte[plain.Length]; var tag = new byte[16];
    using var aes = new AesGcm(key, 16); aes.Encrypt(Nonce(0, counter), plain, encrypted, tag, Aad(id, "m2d", counter));
    return Encode(w =>
    {
        w.WriteString("action", "pairing_encrypted"); w.WriteString("session_id", id);
        w.WriteString("counter", counter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        w.WriteString("ciphertext", B64(encrypted)); w.WriteString("tag", B64(tag));
    });
}
static JsonDocument Open(byte[] response, byte[] key, string id, int counter)
{
    using var doc = JsonDocument.Parse(response); var item = doc.RootElement;
    if (item.GetProperty("session_id").GetString() != id || item.GetProperty("counter").GetString() != counter.ToString(System.Globalization.CultureInfo.InvariantCulture)) throw new InvalidOperationException("Record mismatch.");
    var encrypted = Decode(item.GetProperty("ciphertext").GetString()!); var plain = new byte[encrypted.Length];
    using var aes = new AesGcm(key, 16); aes.Decrypt(Nonce(1, counter), encrypted, Decode(item.GetProperty("tag").GetString()!), plain, Aad(id, "d2m", counter));
    return JsonDocument.Parse(plain);
}
static byte[] Encode(Action<Utf8JsonWriter> write)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); write(writer); writer.WriteEndObject(); }
    return stream.ToArray();
}
static byte[] Nonce(byte direction, int counter)
{ var result = new byte[12]; result[0] = direction; BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(4), (ulong)counter); return result; }
static byte[] Aad(string id, string direction, int counter) => Encoding.UTF8.GetBytes(FormattableString.Invariant($"hatch-link ble setup v1|{id}|{direction}|{counter}"));
static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
