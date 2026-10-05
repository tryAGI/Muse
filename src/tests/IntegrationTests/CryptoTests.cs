using Muse.Transport;
using System.Security.Cryptography;

namespace Muse.IntegrationTests;

[TestClass]
public sealed class CryptoTests
{
    [TestMethod]
    public void Rfc7748AliceBobAgreement()
    {
        var alice = Convert.FromHexString("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        var bob = Convert.FromHexString("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb");
        Convert.ToHexStringLower(X25519.PublicKey(alice)).Should().Be("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a");
        Convert.ToHexStringLower(X25519.PublicKey(bob)).Should().Be("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");
        Convert.ToHexStringLower(X25519.Agree(alice, X25519.PublicKey(bob))).Should().Be("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742");
        X25519.Agree(bob, X25519.PublicKey(alice)).Should().Equal(X25519.Agree(alice, X25519.PublicKey(bob)));
    }

    [TestMethod]
    public void Rfc7748ThousandIterations()
    {
        var scalar = new byte[32]; scalar[0] = 9;
        var point = (byte[])scalar.Clone();
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var previous = scalar; scalar = X25519.Agree(scalar, point); point = previous;
            if (iteration == 0) Convert.ToHexStringLower(scalar).Should().Be("422c8e7a6227d7bca1350b3e2bb7279f7897b87bb6854b783c60e80311ae3079");
        }
        Convert.ToHexStringLower(scalar).Should().Be("684cf59ba83309552800ef566f2f4d3c1c3887c49360e3875f2eb94d99532c51");
    }

    [TestMethod]
    public void LowOrderPointIsRejected()
    {
        Action action = () => X25519.Agree(new byte[32], new byte[32]);
        action.Should().Throw<CryptographicException>();
    }

    [TestMethod]
    public void CipherRejectsTamperingAndCannotBeReused()
    {
        using var sender = new NoiseCipher(new byte[32]);
        using var receiver = new NoiseCipher(new byte[32]);
        var ciphertext = sender.Encrypt("test"u8); ciphertext[0] ^= 1;
        Action first = () => receiver.Decrypt(ciphertext);
        first.Should().Throw<CryptographicException>();
        Action second = () => receiver.Decrypt(ciphertext);
        second.Should().Throw<MuseProtocolException>();
    }

    [TestMethod]
    public void CipherUsesBigEndianNonceAndAuthenticationData()
    {
        using var cipher = new NoiseCipher(new byte[32]);
        cipher.Encrypt([]);
        var actual = cipher.Encrypt("payload"u8, "ad"u8);
        using var aes = new AesGcm(new byte[32], 16);
        var nonce = new byte[12]; nonce[11] = 1;
        var expected = new byte[23];
        aes.Encrypt(nonce, "payload"u8, expected.AsSpan(0, 7), expected.AsSpan(7), "ad"u8);
        actual.Should().Equal(expected);
    }
}
