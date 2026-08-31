using JetBrains.Annotations;

namespace Nova.Imaging;

/// <summary>
/// Managed pixel-buffer utilities for the WPF imaging surface on Linux. The
/// byte-aligned case (the WriteableBitmap Bgra32 path) copies whole rows; sub-byte
/// offsets and widths (indexed formats) fall back to a bit loop. Geometry is
/// validated before any write — the native milcore call the WPF side replaces threw
/// ArgumentException on invalid strides/sizes, and so does this.
/// </summary>
[PublicAPI]
public static class PixelBufferUtility
{
    /// <summary>
    /// Copies <paramref name="height"/> rows of <paramref name="copyWidthInBits"/> bits
    /// from <paramref name="input"/> (at <paramref name="inputOffsetInBits"/> within rows
    /// of <paramref name="inputStride"/> bytes) into <paramref name="output"/> (at
    /// <paramref name="outputOffsetInBits"/> within rows of <paramref name="outputStride"/>
    /// bytes).
    /// </summary>
    public static void CopyPixels(
        Span<byte> output,
        uint outputStride,
        uint outputOffsetInBits,
        ReadOnlySpan<byte> input,
        uint inputStride,
        uint inputOffsetInBits,
        uint height,
        uint copyWidthInBits)
    {
        if (copyWidthInBits == 0 || height == 0)
        {
            return;
        }

        if (outputStride > int.MaxValue || inputStride > int.MaxValue)
        {
            throw new ArgumentException("Invalid buffer stride.");
        }

        uint outputRowEndInBits = checked(outputOffsetInBits + copyWidthInBits);
        uint outputLastRowStartInBits = checked((height - 1) * outputStride * 8);
        ulong outputEndInBytes = (((ulong)checked(outputLastRowStartInBits + outputRowEndInBits)) + 7) / 8;
        uint inputRowEndInBits = checked(inputOffsetInBits + copyWidthInBits);
        uint inputLastRowStartInBits = checked((height - 1) * inputStride * 8);
        ulong inputEndInBytes = (((ulong)checked(inputLastRowStartInBits + inputRowEndInBits)) + 7) / 8;

        if (outputEndInBytes > (ulong)output.Length || inputEndInBytes > (ulong)input.Length)
        {
            throw new ArgumentException("Buffer size is insufficient for the copy.");
        }

        bool byteAligned = (outputOffsetInBits & 7) == 0 &&
                           (inputOffsetInBits & 7) == 0 &&
                           (copyWidthInBits & 7) == 0;

        int rowBytes = checked((int)(copyWidthInBits / 8));
        int outputRowOffset = (int)(outputOffsetInBits / 8);
        int inputRowOffset = (int)(inputOffsetInBits / 8);

        for (uint row = 0; row < height; row++)
        {
            Span<byte> dest = output[(int)((row * outputStride) + outputRowOffset)..];
            ReadOnlySpan<byte> source = input[(int)((row * inputStride) + inputRowOffset)..];

            if (byteAligned)
            {
                source[..rowBytes].CopyTo(dest);
                continue;
            }

            uint dstBase = outputOffsetInBits + (row * outputStride * 8);
            uint srcBase = inputOffsetInBits + (row * inputStride * 8);
            for (uint i = 0; i < copyWidthInBits; i++)
            {
                bool bit = ((source[(int)((srcBase + i) >> 3)] >> (7 - (int)((srcBase + i) & 7))) & 1) != 0;
                byte mask = (byte)(1 << (7 - (int)((dstBase + i) & 7)));
                if (bit)
                {
                    dest[(int)((dstBase + i) >> 3)] |= mask;
                }
                else
                {
                    dest[(int)((dstBase + i) >> 3)] &= (byte)~mask;
                }
            }
        }
    }
}
