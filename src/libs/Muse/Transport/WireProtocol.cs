using System.Buffers;
using System.Text;

namespace Muse.Transport;

// Only the published protobuf wire subset is implemented; unknown non-group
// fields are skipped, lengths and varints are validated before allocation.
internal sealed class ProtoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    internal void Number(int field, ulong value)
    {
        Varint((ulong)(field << 3)); Varint(value);
    }

    internal void Bytes(int field, ReadOnlySpan<byte> value)
    {
        Varint((ulong)((field << 3) | 2)); Varint((ulong)value.Length);
        _buffer.Write(value);
    }

    internal void Text(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
    internal byte[] Finish() => _buffer.WrittenSpan.ToArray();

    private void Varint(ulong value)
    {
        while (value > 127)
        {
            _buffer.GetSpan(1)[0] = (byte)((value & 127) | 128);
            _buffer.Advance(1); value >>= 7;
        }
        _buffer.GetSpan(1)[0] = (byte)value; _buffer.Advance(1);
    }
}

internal ref struct ProtoReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal bool Next(out int field, out int wire)
    {
        if (_offset == _data.Length) { field = wire = 0; return false; }
        var key = Varint();
        if (key < 8 || key >> 3 > 536870911)
            throw new MuseProtocolException("Invalid protobuf field number.");
        field = (int)(key >> 3); wire = (int)(key & 7); return true;
    }

    internal ulong Number(int wire)
    {
        Require(wire, 0); return Varint();
    }

    internal ReadOnlySpan<byte> Bytes(int wire)
    {
        Require(wire, 2);
        var length = Varint();
        if (length > (ulong)(_data.Length - _offset))
            throw new MuseProtocolException("Protobuf length exceeds the containing frame.");
        var result = _data.Slice(_offset, (int)length); _offset += (int)length;
        return result;
    }

    internal string Text(int wire)
    {
        try { return StrictUtf8.GetString(Bytes(wire)); }
        catch (DecoderFallbackException exception) { throw new MuseProtocolException("Invalid UTF-8 in protocol metadata.", exception); }
    }

    internal void Skip(int wire)
    {
        switch (wire)
        {
            case 0: Varint(); break;
            case 2: Bytes(wire); break;
            case 1: Advance(8); break;
            case 5: Advance(4); break;
            default: throw new MuseProtocolException("Unsupported protobuf wire type.");
        }
    }

    private void Advance(int count)
    {
        if (_data.Length - _offset < count) throw new MuseProtocolException("Truncated protobuf field.");
        _offset += count;
    }

    private ulong Varint()
    {
        ulong value = 0;
        for (var index = 0; index < 10; index++)
        {
            if (_offset >= _data.Length) throw new MuseProtocolException("Truncated protobuf varint.");
            var part = _data[_offset++];
            if (index == 9 && part > 1) throw new MuseProtocolException("Protobuf varint overflow.");
            value |= (ulong)(part & 127) << (index * 7);
            if ((part & 128) == 0) return value;
        }
        throw new MuseProtocolException("Invalid protobuf varint.");
    }

    private static void Require(int actual, int expected)
    {
        if (actual != expected) throw new MuseProtocolException("Incorrect protobuf wire type.");
    }
}

internal sealed record WireResponse(long StreamId, int Kind, int Status, byte[] Body, bool EndBody);

internal static class WireProtocol
{
    internal static byte[] Request(long id, string method, string path, ReadOnlySpan<byte> body,
        IReadOnlyDictionary<string, string>? headers = null, bool endBody = true)
    {
        var request = new ProtoWriter();
        request.Text(1, method); request.Text(2, path);
        if (headers is not null)
            foreach (var (key, value) in headers)
            {
                var header = new ProtoWriter(); header.Text(1, key); header.Text(2, value);
                request.Bytes(3, header.Finish());
            }
        if (!body.IsEmpty) request.Bytes(4, body);
        if (endBody) request.Number(5, 1);
        return Envelope(id, 2, request.Finish());
    }

    internal static byte[] Body(long id, ReadOnlySpan<byte> body, bool endBody)
    {
        var chunk = new ProtoWriter(); chunk.Bytes(1, body);
        if (endBody) chunk.Number(2, 1);
        return Envelope(id, 4, chunk.Finish());
    }

    internal static byte[] Cancel(long id)
    {
        var reset = new ProtoWriter(); reset.Number(1, 1);
        return Envelope(id, 5, reset.Finish());
    }

    private static byte[] Envelope(long id, int kind, ReadOnlySpan<byte> body)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var frame = new ProtoWriter(); frame.Number(1, (ulong)id); frame.Bytes(kind, body);
        var request = new ProtoWriter(); request.Bytes(2, frame.Finish());
        return request.Finish();
    }

    internal static WireResponse ParseResponse(ReadOnlySpan<byte> input)
    {
        var outer = new ProtoReader(input);
        ReadOnlySpan<byte> payload = default;
        while (outer.Next(out var field, out var wire))
        {
            if (field == 1) payload = outer.Bytes(wire); else outer.Skip(wire);
        }
        if (payload.IsEmpty) throw new MuseProtocolException("Empty response envelope.");
        var frame = new ProtoReader(payload);
        long id = 0; var kind = 0;
        ReadOnlySpan<byte> message = default;
        while (frame.Next(out var field, out var wire))
        {
            if (field == 1) id = checked((long)frame.Number(wire));
            else if (field is >= 2 and <= 5)
            {
                if (kind != 0) throw new MuseProtocolException("Ambiguous response frame.");
                kind = field; message = frame.Bytes(wire);
            }
            else frame.Skip(wire);
        }
        if (id <= 0 || kind is < 3 or > 5) throw new MuseProtocolException("Unexpected response kind or stream ID.");
        var reader = new ProtoReader(message);
        var status = 0; var end = false; byte[] body = [];
        while (reader.Next(out var field, out var wire))
        {
            if ((kind is 3 or 5) && field == 1) status = checked((int)reader.Number(wire));
            else if ((kind == 3 && field == 3) || (kind == 4 && field == 1)) body = reader.Bytes(wire).ToArray();
            else if ((kind == 3 && field == 4) || (kind == 4 && field == 2)) end = reader.Number(wire) != 0;
            else reader.Skip(wire);
        }
        return new(id, kind, status, body, end);
    }
}
