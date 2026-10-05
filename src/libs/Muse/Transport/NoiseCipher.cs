using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Muse.Transport;

internal sealed class NoiseCipher : IDisposable
{
    private readonly AesGcm _aes;
    private ulong _nonce;
    private bool _dead;

    internal NoiseCipher(ReadOnlySpan<byte> key) => _aes = new AesGcm(key, 16);

    internal byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        Span<byte> nonce = stackalloc byte[12];
        NextNonce(nonce);
        var output = new byte[plaintext.Length + 16];
        try
        {
            _aes.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associatedData);
            return output;
        }
        catch { _dead = true; throw; }
    }

    internal byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> associatedData = default)
    {
        Span<byte> nonce = stackalloc byte[12];
        NextNonce(nonce);
        try
        {
            if (ciphertext.Length < 16) throw new MuseProtocolException("Truncated encrypted frame.");
            var output = new byte[ciphertext.Length - 16];
            _aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], output, associatedData);
            return output;
        }
        catch { _dead = true; throw; }
    }

    private void NextNonce(Span<byte> output)
    {
        if (_dead || _nonce >= (1UL << 53) - 1)
            throw new MuseProtocolException("Cipher is closed or its nonce budget is exhausted.");
        output.Clear();
        BinaryPrimitives.WriteUInt64BigEndian(output[4..], _nonce++);
    }

    public void Dispose() { _dead = true; _aes.Dispose(); }
}

internal sealed class NoiseHandshake : IDisposable
{
    private byte[] _hash = "Noise_XX_25519_AESGCM_SHA256"u8.ToArray();
    private byte[] _chainingKey;
    private readonly byte[] _ephemeral;
    private readonly byte[] _static;
    private NoiseCipher? _cipher;
    private int _phase;

    internal NoiseHandshake(byte[]? ephemeral = null, byte[]? staticKey = null)
    {
        Array.Resize(ref _hash, 32);
        _chainingKey = (byte[])_hash.Clone();
        _ephemeral = ephemeral is null ? RandomNumberGenerator.GetBytes(32) : (byte[])ephemeral.Clone();
        _static = staticKey is null ? RandomNumberGenerator.GetBytes(32) : (byte[])staticKey.Clone();
        MixHash([]);
    }

    internal byte[] Start()
    {
        if (_phase != 0) throw new MuseProtocolException("Handshake was already started.");
        _phase = 1;
        var message = X25519.PublicKey(_ephemeral);
        MixHash(message);
        MixHash([]);
        return message;
    }

    internal (byte[] Message, NoiseCipher Send, NoiseCipher Receive, byte[] RemoteKey) Finish(
        ReadOnlySpan<byte> message, ReadOnlySpan<byte> expectedServerKey = default)
    {
        if (_phase != 1) throw new MuseProtocolException("Handshake is not awaiting message two.");
        _phase = 2;
        try
        {
            if (message.Length < 96 || message.Length > 65535)
                throw new MuseProtocolException("Invalid handshake message length.");
            var remoteEphemeral = message[..32];
            MixHash(remoteEphemeral);
            MixDh(_ephemeral, remoteEphemeral);
            var remoteStatic = DecryptAndHash(message.Slice(32, 48));
            if (!expectedServerKey.IsEmpty && !CryptographicOperations.FixedTimeEquals(remoteStatic, expectedServerKey))
                throw new MuseProtocolException("Server static key does not match the configured pin.");
            MixDh(_ephemeral, remoteStatic);
            var remotePayload = DecryptAndHash(message[80..]);
            CryptographicOperations.ZeroMemory(remotePayload);
            var encryptedStatic = EncryptAndHash(X25519.PublicKey(_static));
            MixDh(_static, remoteEphemeral);
            var encryptedPayload = EncryptAndHash([]);
            var finalMessage = new byte[encryptedStatic.Length + encryptedPayload.Length];
            encryptedStatic.CopyTo(finalMessage, 0);
            encryptedPayload.CopyTo(finalMessage, encryptedStatic.Length);
            var (sendKey, receiveKey) = Expand(_chainingKey, []);
            try
            {
                return (finalMessage, new NoiseCipher(sendKey), new NoiseCipher(receiveKey), remoteStatic);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sendKey);
                CryptographicOperations.ZeroMemory(receiveKey);
            }
        }
        finally { Dispose(); }
    }

    private static (byte[] First, byte[] Second) Expand(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
    {
        var temporary = HMACSHA256.HashData(key, input);
        try
        {
            var first = HMACSHA256.HashData(temporary.AsSpan(), [1]);
            Span<byte> buffer = stackalloc byte[33];
            first.CopyTo(buffer); buffer[32] = 2;
            var second = HMACSHA256.HashData(temporary, buffer);
            CryptographicOperations.ZeroMemory(buffer);
            return (first, second);
        }
        finally { CryptographicOperations.ZeroMemory(temporary); }
    }

    private void MixDh(byte[] privateKey, ReadOnlySpan<byte> publicKey)
    {
        var secret = X25519.Agree(privateKey, publicKey);
        try
        {
            var (next, key) = Expand(_chainingKey, secret);
            CryptographicOperations.ZeroMemory(_chainingKey);
            _chainingKey = next;
            _cipher?.Dispose();
            try { _cipher = new NoiseCipher(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private void MixHash(ReadOnlySpan<byte> input)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(_hash); hash.AppendData(input);
        CryptographicOperations.ZeroMemory(_hash);
        _hash = hash.GetHashAndReset();
    }

    private byte[] EncryptAndHash(ReadOnlySpan<byte> input)
    {
        var bytes = _cipher!.Encrypt(input, _hash); MixHash(bytes); return bytes;
    }

    private byte[] DecryptAndHash(ReadOnlySpan<byte> input)
    {
        var bytes = _cipher!.Decrypt(input, _hash); MixHash(input); return bytes;
    }

    public void Dispose()
    {
        _phase = 3;
        _cipher?.Dispose();
        CryptographicOperations.ZeroMemory(_hash);
        CryptographicOperations.ZeroMemory(_chainingKey);
        CryptographicOperations.ZeroMemory(_ephemeral);
        CryptographicOperations.ZeroMemory(_static);
    }
}
