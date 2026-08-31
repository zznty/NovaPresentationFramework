using System.Text;

namespace Nova.DesktopTheme.Tests;

public sealed class DbusWireTests
{
    [Fact]
    public void BuildMethodCall_Read_RoundTripsSignature()
    {
        byte[] message = DbusWire.BuildMethodCall(
            1,
            "org.freedesktop.portal.Desktop",
            "/org/freedesktop/portal/desktop",
            "org.freedesktop.portal.Settings",
            "Read",
            "ss",
            ["org.freedesktop.appearance", "color-scheme"]);
        Assert.Equal(0x6c, message[0]);
        Assert.Equal(DbusWire.MessageTypeMethodCall, message[1]);
        // Body: two uint32-length-prefixed strings.
        Assert.Contains(Encoding.UTF8.GetBytes("org.freedesktop.appearance"), message);
        Assert.Contains(Encoding.UTF8.GetBytes("color-scheme"), message);
    }

    [Fact]
    public void ParseVariantReply_UIntVariant_Parses()
    {
        // Reply body for Read → (v): struct at offset 0, variant (g 'u' + uint32 1).
        var body = new MemoryStream();
        body.WriteByte(1);       // 'g' length
        body.WriteByte((byte)'u');
        body.WriteByte(0);
        body.Write(new byte[1]); // pad 'u' to 4
        WriteUInt32(body, 1);
        (string signature, object? value) = DbusWire.ParseVariantReply(body.ToArray());
        Assert.Equal("u", signature);
        Assert.Equal(1u, value);
    }

    [Fact]
    public void ParseVariantReply_AccentStruct_Parses()
    {
        // (v) with signature "(ddd)" and three doubles.
        var body = new MemoryStream();
        body.WriteByte(5);       // signature length "(ddd)"
        body.Write(Encoding.ASCII.GetBytes("(ddd)"));
        body.WriteByte(0);
        body.Write(new byte[1]); // pad '(' to 8
        WriteDouble(body, 0.039215687662363052);
        WriteDouble(body, 0.62745100259780884);
        WriteDouble(body, 0.90196079015731812);
        (string signature, object? value) = DbusWire.ParseVariantReply(body.ToArray());
        Assert.Equal("(ddd)", signature);
        double[] channels = Assert.IsType<double[]>(value);
        Assert.Equal(0.039215687662363052, channels[0]);
        Assert.Equal(0.62745100259780884, channels[1]);
        Assert.Equal(0.90196079015731812, channels[2]);
    }

    [Fact]
    public void ParseVariantReply_UnsupportedSignature_Throws()
    {
        var body = new MemoryStream();
        body.WriteByte(1);
        body.WriteByte((byte)'x');
        body.WriteByte(0);
        _ = Assert.Throws<DbusProtocolException>(() => DbusWire.ParseVariantReply(body.ToArray()));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteDouble(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        stream.Write(buffer);
    }
}
