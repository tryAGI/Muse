using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Muse.Transport;

// Independently authored RFC 7748 Montgomery ladder. Fixed loop counts and limb
// selection avoid secret-dependent branches in source; this is not a JIT audit.
internal static class X25519
{
    internal static byte[] PublicKey(ReadOnlySpan<byte> privateKey)
    {
        Span<byte> point = stackalloc byte[32];
        point.Clear();
        point[0] = 9;
        return Agree(privateKey, point);
    }

    internal static byte[] Agree(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != 32 || publicKey.Length != 32)
            throw new ArgumentException("X25519 keys must be 32 bytes.");
        Span<byte> scalar = stackalloc byte[32];
        privateKey.CopyTo(scalar);
        scalar[0] &= 248;
        scalar[31] = (byte)((scalar[31] & 127) | 64);
        try
        {
            var x1 = Field.Decode(publicKey);
            var x2 = new Field(1, 0, 0, 0, 0);
            var z2 = default(Field);
            var x3 = x1;
            var z3 = x2;
            ulong swap = 0;
            for (var bit = 254; bit >= 0; bit--)
            {
                var next = (ulong)((scalar[bit / 8] >> (bit % 8)) & 1);
                swap ^= next;
                Field.Swap(ref x2, ref x3, swap);
                Field.Swap(ref z2, ref z3, swap);
                swap = next;
                var a = x2 + z2;
                var aa = a * a;
                var b = x2 - z2;
                var bb = b * b;
                var e = aa - bb;
                var c = x3 + z3;
                var d = x3 - z3;
                var da = d * a;
                var cb = c * b;
                var sum = da + cb;
                var difference = da - cb;
                x3 = sum * sum;
                z3 = x1 * (difference * difference);
                x2 = aa * bb;
                z2 = e * (aa + new Field(121665, 0, 0, 0, 0) * e);
            }
            Field.Swap(ref x2, ref x3, swap);
            Field.Swap(ref z2, ref z3, swap);
            var inverse = new Field(1, 0, 0, 0, 0);
            // Exponent p-2 = 2^255-21; exponent bits are public constants.
            for (var bit = 254; bit >= 0; bit--)
            {
                inverse *= inverse;
                if (bit >= 8 || ((0xeb >> bit) & 1) != 0)
                    inverse *= z2;
            }
            var shared = (x2 * inverse).Encode();
            Span<byte> zero = stackalloc byte[32];
            zero.Clear();
            if (CryptographicOperations.FixedTimeEquals(shared, zero))
                throw new CryptographicException("X25519 rejected a low-order peer key.");
            return shared;
        }
        finally { CryptographicOperations.ZeroMemory(scalar); }
    }

    private readonly record struct Field(ulong A, ulong B, ulong C, ulong D, ulong E)
    {
        private const ulong Mask = (1UL << 51) - 1;

        internal static Field Decode(ReadOnlySpan<byte> value) => new(
            BinaryPrimitives.ReadUInt64LittleEndian(value) & Mask,
            (BinaryPrimitives.ReadUInt64LittleEndian(value[6..]) >> 3) & Mask,
            (BinaryPrimitives.ReadUInt64LittleEndian(value[12..]) >> 6) & Mask,
            (BinaryPrimitives.ReadUInt64LittleEndian(value[19..]) >> 1) & Mask,
            (BinaryPrimitives.ReadUInt64LittleEndian(value[24..]) >> 12) & Mask);

        private static Field Reduce(UInt128 a, UInt128 b, UInt128 c, UInt128 d, UInt128 e)
        {
            for (var pass = 0; pass < 3; pass++)
            {
                b += a >> 51; a &= Mask;
                c += b >> 51; b &= Mask;
                d += c >> 51; c &= Mask;
                e += d >> 51; d &= Mask;
                a += 19 * (e >> 51); e &= Mask;
            }
            return new((ulong)a, (ulong)b, (ulong)c, (ulong)d, (ulong)e);
        }

        public static Field operator +(Field x, Field y) =>
            Reduce(x.A + y.A, x.B + y.B, x.C + y.C, x.D + y.D, x.E + y.E);

        public static Field operator -(Field x, Field y) =>
            Reduce(x.A + 2 * (Mask - 18) - y.A, x.B + 2 * Mask - y.B,
                x.C + 2 * Mask - y.C, x.D + 2 * Mask - y.D, x.E + 2 * Mask - y.E);

        public static Field operator *(Field x, Field y)
        {
            UInt128 a = (UInt128)x.A * y.A + 19 * ((UInt128)x.B * y.E + (UInt128)x.C * y.D + (UInt128)x.D * y.C + (UInt128)x.E * y.B);
            UInt128 b = (UInt128)x.A * y.B + (UInt128)x.B * y.A + 19 * ((UInt128)x.C * y.E + (UInt128)x.D * y.D + (UInt128)x.E * y.C);
            UInt128 c = (UInt128)x.A * y.C + (UInt128)x.B * y.B + (UInt128)x.C * y.A + 19 * ((UInt128)x.D * y.E + (UInt128)x.E * y.D);
            UInt128 d = (UInt128)x.A * y.D + (UInt128)x.B * y.C + (UInt128)x.C * y.B + (UInt128)x.D * y.A + 19 * (UInt128)x.E * y.E;
            UInt128 e = (UInt128)x.A * y.E + (UInt128)x.B * y.D + (UInt128)x.C * y.C + (UInt128)x.D * y.B + (UInt128)x.E * y.A;
            return Reduce(a, b, c, d, e);
        }

        internal static void Swap(ref Field x, ref Field y, ulong bit)
        {
            var mask = unchecked(0UL - bit);
            var a = (x.A ^ y.A) & mask; var b = (x.B ^ y.B) & mask;
            var c = (x.C ^ y.C) & mask; var d = (x.D ^ y.D) & mask;
            var e = (x.E ^ y.E) & mask;
            x = new(x.A ^ a, x.B ^ b, x.C ^ c, x.D ^ d, x.E ^ e);
            y = new(y.A ^ a, y.B ^ b, y.C ^ c, y.D ^ d, y.E ^ e);
        }

        internal byte[] Encode()
        {
            var f = Reduce(A, B, C, D, E);
            var q = (f.A + 19) >> 51;
            q = (f.B + q) >> 51; q = (f.C + q) >> 51;
            q = (f.D + q) >> 51; q = (f.E + q) >> 51;
            var a = f.A + 19 * q;
            var b = f.B + (a >> 51); a &= Mask;
            var c = f.C + (b >> 51); b &= Mask;
            var d = f.D + (c >> 51); c &= Mask;
            var e = (f.E + (d >> 51)) & Mask; d &= Mask;
            var result = new byte[32];
            BinaryPrimitives.WriteUInt64LittleEndian(result, a | (b << 51));
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8), (b >> 13) | (c << 38));
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(16), (c >> 26) | (d << 25));
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(24), (d >> 39) | (e << 12));
            return result;
        }
    }
}
