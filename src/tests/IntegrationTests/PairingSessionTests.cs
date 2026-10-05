using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Muse.IntegrationTests;

// Published synthetic community_app_v5 vector, not account credentials or an implementation import.
// facebookincubator/muse-gadget-sdk@74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff
// linux/tests/vectors/link_pairing_v5.json (Apache-2.0; Meta Platforms, Inc. and affiliates).
[TestClass]
public sealed class PairingSessionTests
{
    private const string SessionId = "z0eNLGw5mczvD4a1F2ubSQ";
    private const string DevicePublic = "BHzyexiNA09-ilI4AwS1GsPAiWnid_IbNaYLSPxHZpl4B3dVENuO0EApPZrGn3Qw27p9reY86YIpngS3nSJ4c9E";
    private const string MobilePublic = "BGsX0fLhLEJH-Lzm5WOkQPJ3A32BLeszoPShOUXYmMKWT-NC4v4af5uO5-tKfA-eFivOM1drMV7Oy7ZAaDe_UfU";
    private const string FixtureSdkToken = "mgst_fixture_only";
    private static readonly byte[] Tx = Convert.FromHexString("ee25e7f1eb3c05cc8465634e5f634deed858862b1c4c35fa434dc926f7facaeb");
    private static readonly byte[] Rx = Convert.FromHexString("9abb2954b05e2f5f7112e9bfd41fd970602e27ed07453c1cff51f575de5d0b4e");
    private static byte[] Hello => Encoding.UTF8.GetBytes("{\"action\":\"pairing_client_hello\",\"version\":5,\"pairing_auth\":\"none\",\"pairing_policy\":\"confirm_app\",\"mobile_pub\":\"" + MobilePublic + "\",\"mobile_nonce\":\"AAECAwQFBgcICQoLDA0ODw\"}");
    private static byte[] Finished => "{\"action\":\"pairing_encrypted\",\"session_id\":\"z0eNLGw5mczvD4a1F2ubSQ\",\"counter\":\"0\",\"ciphertext\":\"uwNX3KEkix5T6nWce3s2SKPPh-5E31x-UIHFSK6MpMUg562B\",\"tag\":\"zT4kU6XK-rM7rvt3VEITLg\"}"u8.ToArray();
    private const string Provision = "{\"action\":\"provision_v2\",\"token_type\":\"device\",\"access_token\":\"fixture-access\",\"refresh_token\":\"fixture-refresh\",\"ssid\":\"Fixture LAN\",\"password\":\"not-retained\",\"api_url_v2\":\"https://api.muse.ai\",\"noise_host\":\"fixture.invalid\"}";

    private static MusePairingSession Create(TimeProvider? time = null) => new("02:00:00:00:00:01", FixtureSdkToken, "1.0.0", time ?? TimeProvider.System,
        () =>
        {
            var publicKey = Decode(DevicePublic); var scalar = new byte[32]; scalar[31] = 2;
            return ECDiffieHellman.Create(new ECParameters
            { Curve = ECCurve.NamedCurves.nistP256, D = scalar, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] } });
        }, () => Decode("EBESExQVFhcYGRobHB0eHw"));

    private static MusePairingSession Confirmed()
    {
        var session = Create(); session.HandleClientHello(Hello); session.ConfirmClient(Finished); return session;
    }

    [TestMethod]
    public void MatchesOfficialP256TranscriptAndSessionVector()
    {
        using var session = Create();
        using var response = JsonDocument.Parse(session.HandleClientHello(Hello));
        response.RootElement.GetProperty("device_pub").GetString().Should().Be(DevicePublic);
        response.RootElement.GetProperty("transcript_hash").GetString().Should().Be("18Rs196tzddPGuj18pZ8rWObWYpwoDUIwocjnUASj_0");
        response.RootElement.GetProperty("session_id").GetString().Should().Be(SessionId);
        session.NodeId.Should().Be("homelink-000001");
        session.AdvertisementName.Should().Be("MuseGadget000001");
    }

    [TestMethod]
    public void SdkTokenIsDeliveredOnlyInsideEncryptedConfirmation()
    {
        using var session = Create();
        Encoding.UTF8.GetString(session.GetDeviceInfo(true)).Should().NotContain(FixtureSdkToken);
        Encoding.UTF8.GetString(session.HandleClientHello(Hello)).Should().NotContain(FixtureSdkToken);
        var bytes = session.ConfirmClient(Finished);
        Encoding.UTF8.GetString(bytes).Should().NotContain(FixtureSdkToken);
        using var confirmed = ReadServer(bytes, 0);
        confirmed.RootElement.GetProperty("status").GetString().Should().Be("pairing_confirmed");
        confirmed.RootElement.GetProperty("sdk_token").GetString().Should().Be(FixtureSdkToken);
    }

    [TestMethod]
    public void EncryptedWifiAndProvisioningReachServerNotRelay()
    {
        using var session = Confirmed();
        session.ReceiveCommand(Seal("{\"action\":\"wifi_scan\"}", 1)).IsWifiScan.Should().BeTrue();
        using var network = JsonDocument.Parse("{\"type\":\"wifi_scan_result\",\"networks\":[{\"ssid\":\"Fixture LAN\",\"secure\":false,\"rssi\":-40}]}");
        using var reply = ReadServer(session.EncryptResponse(network.RootElement), 1);
        reply.RootElement.GetProperty("type").GetString().Should().Be("wifi_scan_result");
        var command = session.ReceiveCommand(Seal(Provision, 2));
        command.IsWifiScan.Should().BeFalse();
        command.Credentials!.AccessToken.Should().Be("fixture-access");
        command.Credentials.RefreshToken.Should().Be("fixture-refresh");
        command.Credentials.NodeId.Should().Be(session.NodeId);
        command.Credentials.ToString().Should().NotContain("fixture-access");
        command.ToString().Should().NotContain("fixture-refresh");
    }

    [TestMethod]
    [DataRow(true, "auth_ok")]
    [DataRow(false, "auth_failed")]
    public void OnlyHostCanFinishAfterGrantReceipt(bool hostVerifiedAndPersisted, string expected)
    {
        using var session = Confirmed();
        Action early = () => session.FinishProvisioning(true);
        early.Should().Throw<InvalidOperationException>();
        session.ReceiveCommand(Seal(Provision, 1));
        using var result = ReadServer(session.FinishProvisioning(hostVerifiedAndPersisted), 1);
        result.RootElement.GetProperty("status").GetString().Should().Be(expected);
        Action twice = () => session.FinishProvisioning(hostVerifiedAndPersisted);
        twice.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void TerminalSuccessCannotBeSentThroughGenericResponse()
    {
        using var session = Confirmed();
        using var message = JsonDocument.Parse("{\"type\":\"status\",\"status\":\"auth_ok\"}");
        Action action = () => session.EncryptResponse(message.RootElement);
        action.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void ReplayedOrSkippedCountersCloseAttempt()
    {
        using var session = Confirmed();
        Action replay = () => session.ReceiveCommand(Finished);
        replay.Should().Throw<MuseProtocolException>();
        Action next = () => session.ReceiveCommand(Seal("{\"action\":\"wifi_scan\"}", 1));
        next.Should().Throw<InvalidOperationException>();
        using var other = Create(); other.HandleClientHello(Hello);
        Action skip = () => other.ConfirmClient(Seal("{\"action\":\"pairing_client_finished\"}", 1));
        skip.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void TamperedTagAndOldSessionAreRejected()
    {
        using var session = Create(); session.HandleClientHello(Hello);
        var altered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Finished).Replace("zT4kU6", "AT4kU6", StringComparison.Ordinal));
        Action tamper = () => session.ConfirmClient(altered);
        tamper.Should().Throw<CryptographicException>();
        using var other = Create(); other.HandleClientHello(Hello);
        Action stale = () => other.ConfirmClient(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Finished).Replace(SessionId, "old-session", StringComparison.Ordinal)));
        stale.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    [DataRow("{\"action\":\"provision_v2\",\"token_type\":\"device\",\"sdk_token\":\"mgst_fixture_only\"}")]
    [DataRow("{\"action\":\"provision_v2\",\"token_type\":\"device\",\"access_token\":\"mgst_wrong_role\",\"refresh_token\":\"fixture\"}")]
    public void SdkTokenNeverSubstitutesForAppIssuedDeviceCredentials(string input)
    {
        using var session = Confirmed();
        Action action = () => session.ReceiveCommand(Seal(input, 1));
        action.Should().Throw<MuseProtocolException>();
        Action finish = () => session.FinishProvisioning(true);
        finish.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void DuplicateJsonAndOversizedMessagesAreRejected()
    {
        using var session = Create();
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Hello).Replace("\"version\":5", "\"version\":5,\"version\":5", StringComparison.Ordinal));
        Action action = () => session.HandleClientHello(duplicate);
        action.Should().Throw<MuseProtocolException>();
        using var other = Create();
        Action oversized = () => other.HandleClientHello(new byte[16385]);
        oversized.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void MonotonicConfirmationDeadlineAndDisposalInvalidateKeys()
    {
        var clock = new Clock(); using var session = Create(clock);
        session.HandleClientHello(Hello); clock.Advance(TimeSpan.FromSeconds(61));
        Action action = () => session.ConfirmClient(Finished);
        action.Should().Throw<MuseProtocolException>();
        using var other = Confirmed(); other.Dispose();
        Action disposed = () => other.ReceiveCommand(Seal("{\"action\":\"wifi_scan\"}", 1));
        disposed.Should().Throw<InvalidOperationException>();
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan time) => _ticks += time.Ticks;
    }

    // This synthetic phone uses the published keys independently of the server's HKDF implementation.
    private static byte[] Seal(string message, ulong counter)
    {
        var plaintext = Encoding.UTF8.GetBytes(message); var encrypted = new byte[plaintext.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Tx, 16);
        aes.Encrypt(Nonce(0, counter), plaintext, encrypted, tag, Aad("m2d", counter));
        return Encoding.UTF8.GetBytes("{\"action\":\"pairing_encrypted\",\"session_id\":\"" + SessionId + "\",\"counter\":\"" + counter.ToString(CultureInfo.InvariantCulture) + "\",\"ciphertext\":\"" + B64(encrypted) + "\",\"tag\":\"" + B64(tag) + "\"}");
    }

    private static JsonDocument ReadServer(byte[] message, ulong expectedCounter)
    {
        using var envelope = JsonDocument.Parse(message); var root = envelope.RootElement;
        root.GetProperty("session_id").GetString().Should().Be(SessionId);
        root.GetProperty("counter").GetString().Should().Be(expectedCounter.ToString(CultureInfo.InvariantCulture));
        var encrypted = Decode(root.GetProperty("ciphertext").GetString()!); var plaintext = new byte[encrypted.Length];
        using var aes = new AesGcm(Rx, 16);
        aes.Decrypt(Nonce(1, expectedCounter), encrypted, Decode(root.GetProperty("tag").GetString()!), plaintext, Aad("d2m", expectedCounter));
        return JsonDocument.Parse(plaintext);
    }
    private static byte[] Nonce(byte direction, ulong counter)
    { var nonce = new byte[12]; nonce[0] = direction; BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter); return nonce; }
    private static byte[] Aad(string arrow, ulong counter) => Encoding.UTF8.GetBytes($"hatch-link ble setup v1|{SessionId}|{arrow}|{counter.ToString(CultureInfo.InvariantCulture)}");
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
