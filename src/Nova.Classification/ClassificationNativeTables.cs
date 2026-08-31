using System.Buffers.Binary;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Classification;

/// <summary>
/// Process-lifetime native <c>short***</c> / packed-row pointers matching
/// <c>MS.Internal.Classification</c> lookup (pointer values below
/// <see cref="UnicodeClass.Max"/> are class-ID sentinels).
/// </summary>
[PublicAPI]
public sealed unsafe class ClassificationNativeTables : IDisposable
{
    private const int PlaneCount = 17;
    private const int RowSize = 8;

    private List<nint>? _blocks;

    internal ClassificationNativeTables(List<nint> blocks, nint unicodeClasses, nint characterAttributes)
    {
        _blocks = blocks;
        UnicodeClasses = unicodeClasses;
        CharacterAttributes = characterAttributes;
    }

    /// <summary>Root of the 17-plane <c>short***</c> class lookup.</summary>
    public nint UnicodeClasses { get; }

    /// <summary>Packed <c>CharacterAttribute</c> rows, 8 bytes each, <c>Pack = 1</c>.</summary>
    public nint CharacterAttributes { get; }

    /// <summary>Unused by managed PresentationCore. Always zero.</summary>
    public static nint Mirroring => 0;

    /// <summary>WPF <c>GetUnicodeClass</c> over the pinned pointers.</summary>
    public ushort ClassOf(int scalar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scalar);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scalar, 0x10FFFF);
        ObjectDisposedException.ThrowIf(_blocks is null, this);

        nint** planes = (nint**)UnicodeClasses;
        nint plane = (nint)planes[((scalar >> 16) & 0xFF) % PlaneCount];
        if (plane < (int)UnicodeClass.Max)
        {
            return (ushort)plane;
        }

        nint* pages = (nint*)plane;
        nint page = pages[(scalar & 0xFFFF) >> 8];
        if (page < (int)UnicodeClass.Max)
        {
            return (ushort)page;
        }

        short* cells = (short*)page;
        return (ushort)cells[scalar & 0xFF];
    }

    /// <summary>Reads one packed row from the pinned attribute table.</summary>
    public CharacterAttributeRow AttributeOf(int classId)
    {
        ObjectDisposedException.ThrowIf(_blocks is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(classId);

        byte* rows = (byte*)CharacterAttributes;
        int offset = classId * RowSize;
        return new CharacterAttributeRow(
            rows[offset],
            rows[offset + 1],
            BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(rows + offset + 2, 2)),
            rows[offset + 4],
            rows[offset + 5],
            BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(rows + offset + 6, 2)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<nint>? blocks = _blocks;
        if (blocks is null)
        {
            return;
        }

        _blocks = null;
        foreach (nint block in blocks)
        {
            NativeMemory.Free((void*)block);
        }

        GC.SuppressFinalize(this);
    }

    internal static nint Alloc(List<nint> blocks, int byteCount)
    {
        nint block = (nint)NativeMemory.Alloc((nuint)byteCount);
        blocks.Add(block);
        return block;
    }
}
