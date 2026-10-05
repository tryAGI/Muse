using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;

namespace Muse.Transport;

internal sealed class JsonRecords(bool lengthPrefixed, int maximumBytes = 262144)
{
    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private readonly byte[] _prefix = new byte[4];
    private int _prefixBytes;
    private int _length = -1;
    private bool _failed;

    internal List<JsonElement> Feed(ReadOnlySpan<byte> bytes)
    {
        if (_failed) throw new MuseProtocolException("JSON decoder is closed after a prior failure.");
        try
        {
            List<JsonElement> records = [];
            while (!bytes.IsEmpty)
            {
                if (lengthPrefixed)
                {
                    if (_length < 0)
                    {
                        var prefixCount = Math.Min(4 - _prefixBytes, bytes.Length);
                        bytes[..prefixCount].CopyTo(_prefix.AsSpan(_prefixBytes));
                        bytes = bytes[prefixCount..]; _prefixBytes += prefixCount;
                        if (_prefixBytes != 4) continue;
                        var announced = BinaryPrimitives.ReadUInt32LittleEndian(_prefix);
                        if (announced > maximumBytes) throw new MuseProtocolException("Control record exceeds the configured limit.");
                        _prefixBytes = 0;
                        if (announced == 0) continue;
                        _length = (int)announced;
                    }
                    var count = Math.Min(_length - _buffer.WrittenCount, bytes.Length);
                    Append(bytes[..count]); bytes = bytes[count..];
                    if (_buffer.WrittenCount == _length) { records.Add(Parse()); _length = -1; }
                }
                else
                {
                    var newline = bytes.IndexOf((byte)'\n');
                    var count = newline < 0 ? bytes.Length : newline;
                    Append(bytes[..count]); bytes = bytes[(count + (newline >= 0 ? 1 : 0))..];
                    if (newline >= 0 && _buffer.WrittenCount != 0) records.Add(Parse());
                }
            }
            return records;
        }
        catch { _failed = true; _buffer.Clear(); throw; }
    }

    internal List<JsonElement> Complete()
    {
        if (_failed || (lengthPrefixed && (_length >= 0 || _prefixBytes != 0)))
            throw new MuseProtocolException("JSON stream ended in a partial record.");
        return _buffer.WrittenCount == 0 ? [] : [Parse()];
    }

    private void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length > maximumBytes - _buffer.WrittenCount)
            throw new MuseProtocolException("JSON record exceeds the configured limit.");
        _buffer.Write(data);
    }

    private JsonElement Parse()
    {
        try
        {
            StrictUtf8.GetCharCount(_buffer.WrittenSpan);
            using var document = JsonDocument.Parse(_buffer.WrittenMemory, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new MuseProtocolException("Expected a JSON object record.");
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is JsonException or System.Text.DecoderFallbackException)
        { _failed = true; throw new MuseProtocolException("Malformed JSON record.", exception); }
        finally { _buffer.Clear(); }
    }

    internal static byte[] Encode(Action<Utf8JsonWriter> write, bool prefix = false)
    {
        using var output = new MemoryStream();
        if (prefix) output.Write(new byte[4]);
        using (var writer = new Utf8JsonWriter(output)) write(writer);
        var bytes = output.ToArray();
        if (prefix) BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)(bytes.Length - 4));
        return bytes;
    }
}
