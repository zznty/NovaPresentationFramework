namespace Nova.Imaging.Tests;

public sealed class PixelBufferUtilityTests
{
    [Fact]
    public void CopyPixels_ByteAligned_CopiesRows()
    {
        // 3 rows of 2 bytes, source stride 3 (one pad byte per row), dest stride 4.
        byte[] input =
        [
            0x11, 0x22, 0xAA,
            0x33, 0x44, 0xBB,
            0x55, 0x66, 0xCC
        ];
        byte[] output = new byte[3 * 4];

        PixelBufferUtility.CopyPixels(output, 4, 0, input, 3, 0, 3, 16);

        Assert.Equal([0x11, 0x22, 0x00, 0x00, 0x33, 0x44, 0x00, 0x00, 0x55, 0x66, 0x00, 0x00], output);
    }

    [Fact]
    public void CopyPixels_ByteAligned_WithOffsets_HonorsOffsetsAndStrides()
    {
        // Source row payload starts at byte 1 of each 3-byte row; dest at byte 2 of each 4-byte row.
        byte[] input =
        [
            0xAA, 0x11, 0x22,
            0xBB, 0x33, 0x44
        ];
        byte[] output = new byte[2 * 4];

        PixelBufferUtility.CopyPixels(output, 4, 16, input, 3, 8, 2, 16);

        Assert.Equal([0x00, 0x00, 0x11, 0x22, 0x00, 0x00, 0x33, 0x44], output);
    }

    [Fact]
    public void CopyPixels_SubByteWidth_CopiesBits()
    {
        // 4 bits: the source's high nibble (1010) into the destination's low nibble
        // (bits 4-7; bit 0 = MSB), leaving the destination's high nibble untouched.
        byte[] input = [0b1010_0000];
        byte[] output = [0b0000_1111];

        PixelBufferUtility.CopyPixels(output, 1, 4, input, 1, 0, 1, 4);

        Assert.Equal([0b0000_1010], output);
    }

    [Fact]
    public void CopyPixels_ZeroSize_IsNoOp()
    {
        byte[] input = [0xFF];
        byte[] output = [0x00];

        PixelBufferUtility.CopyPixels(output, 1, 0, input, 1, 0, 0, 8);
        PixelBufferUtility.CopyPixels(output, 1, 0, input, 1, 0, 1, 0);

        Assert.Equal([0x00], output);
    }

    [Fact]
    public void CopyPixels_Overflow_ThrowsBeforeWriting()
    {
        byte[] input = [0x11, 0x22];
        byte[] output = new byte[1];

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            PixelBufferUtility.CopyPixels(output, 1, 0, input, 1, 0, 1, 16));
        _ = ex;

        Assert.Equal([0x00], output);
    }
}
