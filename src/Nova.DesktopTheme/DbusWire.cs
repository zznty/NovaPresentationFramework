using System.Buffers.Binary;
using System.Text;

namespace Nova.DesktopTheme;

/// <summary>
/// Minimal DBus wire codec (BCL only): little-endian framing, headers, and the value types
/// the portal appearance interface needs (<c>y u s g o v (…) a{sv}</c>). Deliberately not a
/// general implementation — anything beyond this subset throws
/// <see cref="DbusProtocolException"/>, which callers treat as "no portal".
/// </summary>
internal static class DbusWire
{
    public const byte MessageTypeMethodCall = 1;
    public const byte MessageTypeMethodReturn = 2;
    public const byte MessageTypeError = 3;
    public const byte MessageTypeSignal = 4;

    public const byte HeaderFieldPath = 1;
    public const byte HeaderFieldInterface = 2;
    public const byte HeaderFieldMember = 3;
    public const byte HeaderFieldErrorName = 4;
    public const byte HeaderFieldReplySerial = 5;
    public const byte HeaderFieldDestination = 6;
    public const byte HeaderFieldSender = 7;
    public const byte HeaderFieldSignature = 8;

    public static byte[] BuildMethodCall(
        uint serial,
        string destination,
        string path,
        string interfaceName,
        string member,
        string signature,
        object?[] args)
    {
        var header = new MemoryStream();
        var body = new MemoryStream();
        WriteFixedHeader(header, serial);
        WriteHeaderField(header, HeaderFieldPath, 'o', path);
        WriteHeaderField(header, HeaderFieldInterface, 's', interfaceName);
        WriteHeaderField(header, HeaderFieldMember, 's', member);
        WriteHeaderField(header, HeaderFieldDestination, 's', destination);
        if (signature.Length > 0)
        {
            WriteHeaderField(header, HeaderFieldSignature, 'g', signature);
            WriteBody(body, signature, args);
        }

        byte[] headerBytes = header.ToArray();
        byte[] bodyBytes = body.ToArray();
        var message = new MemoryStream();
        message.WriteByte(0x6c); // 'l' — little-endian
        message.WriteByte(MessageTypeMethodCall);
        message.WriteByte(0);    // flags
        message.WriteByte(1);    // protocol version
        WriteUInt32(message, (uint)bodyBytes.Length);
        WriteUInt32(message, serial);
        message.Write(headerBytes, 0, headerBytes.Length);
        message.Write(bodyBytes, 0, bodyBytes.Length);
        return message.ToArray();
    }

    /// <summary>Reads a variant: a <c>g</c> signature followed by the value.</summary>
    public static (string Signature, object? Value) ReadVariant(ReadOnlySpan<byte> data, ref int offset)
    {
        string signature = ReadSignature(data, ref offset);
        return (signature, ReadValue(data, ref offset, signature));
    }

    /// <summary>
    /// Reads a value of the given single-complete-type <paramref name="signature"/>. Returns
    /// the CLR object for <c>y u s o g b d</c>, a <c>double[3]</c> for a <c>(ddd)</c> struct,
    /// a <c>Dictionary&lt;string, object?&gt;</c> for <c>a{sv}</c>, and the raw bytes for an
    /// <c>ay</c> array.
    /// </summary>
    public static object? ReadValue(ReadOnlySpan<byte> data, ref int offset, string signature)
    {
        return signature switch
        {
            "y" => data[offset++],
            "u" => ReadUInt32Aligned(data, ref offset),
            "s" or "o" => ReadString(data, ref offset),
            "g" => ReadSignature(data, ref offset),
            "b" => ReadUInt32Aligned(data, ref offset) != 0,
            "d" => BitConverter.Int64BitsToDouble((long)ReadUInt64Aligned(data, ref offset)),
            "(ddd)" => ReadDoubleStruct(data, ref offset),
            "a{sv}" => ReadStringVariantDict(data, ref offset),
            "ay" => ReadByteArray(data, ref offset),
            _ => throw new DbusProtocolException($"unsupported reply signature '{signature}'")
        };
    }

    /// <summary>Parses a method-return body shaped <c>(v)</c> — the portal Read reply.</summary>
    public static (string Signature, object? Value) ParseVariantReply(ReadOnlySpan<byte> data)
    {
        int offset = (0 + 7) & ~7; // '(' struct alignment
        return ReadVariant(data, ref offset);
    }

    private static void WriteFixedHeader(Stream stream, uint serial)
    {
        stream.WriteByte(0x6c);
        stream.WriteByte(MessageTypeMethodCall);
        stream.WriteByte(0);
        stream.WriteByte(1);
        WriteUInt32(stream, 0); // body length — patched by caller
        WriteUInt32(stream, serial);
    }

    private static void WriteHeaderField(Stream stream, byte code, char type, string value)
    {
        // a(yv): one dict entry; the variant is a signature 's'/'o'/'g' + the string value.
        var entry = new MemoryStream();
        entry.WriteByte(code);
        WriteSignature(entry, type.ToString());
        WriteString(entry, value);
        byte[] entryBytes = entry.ToArray();
        stream.WriteByte((byte)entryBytes.Length);
        stream.Write(entryBytes, 0, entryBytes.Length);
    }

    private static void WriteBody(Stream stream, string signature, object?[] args)
    {
        if (signature == "ss")
        {
            WriteString(stream, (string)args[0]!);
            WriteString(stream, (string)args[1]!);
            return;
        }

        if (signature == "as")
        {
            string[] values = (string[])args[0]!;
            WriteUInt32(stream, (uint)values.Length);
            foreach (string value in values)
            {
                WriteString(stream, value);
            }

            return;
        }

        throw new DbusProtocolException($"unsupported call signature '{signature}'");
    }

    private static void WriteSignature(Stream stream, string signature)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(signature);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static uint ReadUInt32Aligned(ReadOnlySpan<byte> data, ref int offset)
    {
        offset = (offset + 3) & ~3;
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64Aligned(ReadOnlySpan<byte> data, ref int offset)
    {
        offset = (offset + 7) & ~7;
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
        offset += 8;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        uint length = ReadUInt32Aligned(data, ref offset);
        string value = Encoding.UTF8.GetString(data.Slice(offset, (int)length));
        offset += (int)length + 1; // trailing NUL
        return value;
    }

    private static string ReadSignature(ReadOnlySpan<byte> data, ref int offset)
    {
        int length = data[offset++];
        string value = Encoding.ASCII.GetString(data.Slice(offset, length));
        offset += length + 1; // trailing NUL
        return value;
    }

    private static double[] ReadDoubleStruct(ReadOnlySpan<byte> data, ref int offset)
    {
        // '(' aligns to 8; then three doubles.
        offset = (offset + 7) & ~7;
        var values = new double[3];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.Int64BitsToDouble((long)ReadUInt64Aligned(data, ref offset));
        }

        return values;
    }

    private static Dictionary<string, object?> ReadStringVariantDict(ReadOnlySpan<byte> data, ref int offset)
    {
        uint count = ReadUInt32Aligned(data, ref offset);
        var dict = new Dictionary<string, object?>();
        for (uint i = 0; i < count; i++)
        {
            string key = ReadString(data, ref offset);
            (_, object? value) = ReadVariant(data, ref offset);
            dict[key] = value;
        }

        return dict;
    }

    private static byte[] ReadByteArray(ReadOnlySpan<byte> data, ref int offset)
    {
        uint count = ReadUInt32Aligned(data, ref offset);
        offset = (offset + 3) & ~3;
        byte[] bytes = data.Slice(offset, (int)count).ToArray();
        offset += (int)count;
        return bytes;
    }
}

/// <summary>A DBus protocol or connection failure; treated as "no portal" by callers.</summary>
public sealed class DbusProtocolException : Exception
{
    public DbusProtocolException()
    {
    }

    public DbusProtocolException(string message)
        : base(message)
    {
    }

    public DbusProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
