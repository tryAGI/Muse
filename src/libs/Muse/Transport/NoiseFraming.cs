using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Muse.Transport;

internal sealed class NoiseFraming(int maximumMessageBytes, TimeProvider? timeProvider = null)
{
    internal const int ChunkSize = 65489;
    private readonly Dictionary<ulong, Assembly> _pending = [];
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private int _retainedBytes;
    private bool _dead;

    internal static IEnumerable<byte[]> Encode(byte[] data)
    {
        var count = Math.Max(1, (data.Length + ChunkSize - 1) / ChunkSize);
        if (count > 256) throw new MuseProtocolException("Message exceeds the wire chunk limit.");
        var id = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
        for (var index = 0; index < count; index++)
        {
            var frame = new ProtoWriter();
            frame.Number(1, id);
            if (index != 0) frame.Number(2, (ulong)index);
            frame.Number(3, (ulong)count);
            var offset = index * ChunkSize;
            frame.Bytes(4, data.AsSpan(offset, Math.Min(ChunkSize, data.Length - offset)));
            yield return frame.Finish();
        }
    }

    internal byte[]? Decode(ReadOnlySpan<byte> input)
    {
        if (_dead) throw new MuseProtocolException("Framing is closed after an earlier failure.");
        try
        {
            ulong id = 0; var index = 0; var total = 1;
            ReadOnlySpan<byte> payload = default;
            var reader = new ProtoReader(input);
            while (reader.Next(out var field, out var wire))
            {
                switch (field)
                {
                    case 1: id = reader.Number(wire); break;
                    case 2: index = checked((int)reader.Number(wire)); break;
                    case 3: total = checked((int)reader.Number(wire)); break;
                    case 4: payload = reader.Bytes(wire); break;
                    default: reader.Skip(wire); break;
                }
            }
            if (total is < 1 or > 256 || index < 0 || index >= total || payload.Length > ChunkSize)
                throw new MuseProtocolException("Invalid transport chunk.");
            if (_retainedBytes + payload.Length > maximumMessageBytes)
                throw new MuseProtocolException("Transport reassembly memory budget exceeded.");
            foreach (var item in _pending.Values)
                if (_time.GetElapsedTime(item.Start) > TimeSpan.FromSeconds(60))
                    throw new MuseProtocolException("Transport assembly expired.");
            if (!_pending.TryGetValue(id, out var assembly))
            {
                if (_pending.Count >= 16) throw new MuseProtocolException("Too many incomplete transport assemblies.");
                assembly = new Assembly(total, _time.GetTimestamp()); _pending.Add(id, assembly);
            }
            if (assembly.Chunks.Length != total || assembly.Chunks[index] is not null)
                throw new MuseProtocolException("Inconsistent or duplicate transport chunk.");
            assembly.Chunks[index] = payload.ToArray(); assembly.Count++;
            assembly.Bytes += payload.Length; _retainedBytes += payload.Length;
            if (assembly.Count != total) return null;
            _pending.Remove(id); _retainedBytes -= assembly.Bytes;
            var result = new byte[assembly.Bytes]; var position = 0;
            foreach (var chunk in assembly.Chunks)
            {
                chunk!.CopyTo(result, position); position += chunk.Length;
            }
            return result;
        }
        catch
        {
            _dead = true; _pending.Clear(); _retainedBytes = 0; throw;
        }
    }

    private sealed class Assembly(int total, long start)
    {
        internal readonly byte[]?[] Chunks = new byte[total][];
        internal readonly long Start = start;
        internal int Bytes;
        internal int Count;
    }
}
