using JetBrains.Annotations;

namespace Nova.Classification;

/// <summary>
/// WPF-private classification tables: a 3-level class lookup over all Unicode scalars plus the
/// packed <see cref="CharacterAttributeRow"/> per class ID. Class IDs are equivalence-class
/// indices of identical rows, mirroring the managed consumer contract of <c>MILGetClassificationTables</c>.
/// </summary>
[PublicAPI]
public sealed class ClassificationTables
{
    private readonly short[][][] _planes;
    private readonly CharacterAttributeRow[] _attributes;

    internal ClassificationTables(short[][][] planes, CharacterAttributeRow[] attributes)
    {
        _planes = planes;
        _attributes = attributes;
    }

    /// <summary>Number of distinct classification rows; always less than <see cref="UnicodeClass.Max"/>.</summary>
    public int ClassCount => _attributes.Length;

    /// <summary>
    /// Looks up the class ID of a Unicode scalar value. Pages whose 256 cells share one class are
    /// stored as a 1-element sentinel array (WPF sentinel semantics).
    /// </summary>
    /// <param name="scalar">Unicode scalar value in <c>[0, 0x10FFFF]</c>.</param>
    public ushort ClassOf(int scalar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scalar);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scalar, 0x10FFFF);

        short[][] plane = _planes[((scalar >> 16) & 0xFF) % 17];
        short[] page = plane[(scalar & 0xFFFF) >> 8];
        return page.Length == 1 ? (ushort)page[0] : (ushort)page[scalar & 0xFF];
    }

    /// <summary>Returns the packed row for a class ID.</summary>
    /// <param name="classId">Class ID in <c>[0, <see cref="ClassCount"/>)</c>.</param>
    public CharacterAttributeRow AttributeOf(int classId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(classId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(classId, _attributes.Length);
        return _attributes[classId];
    }

    /// <summary>Returns the packed row for a Unicode scalar value.</summary>
    /// <param name="scalar">Unicode scalar value in <c>[0, 0x10FFFF]</c>.</param>
    public CharacterAttributeRow AttributeOfScalar(int scalar)
    {
        return AttributeOf(ClassOf(scalar));
    }

    /// <summary>
    /// Copies the tables into native memory with WPF sentinel semantics so
    /// <c>MS.Internal.Classification</c> can consume them without
    /// <c>PresentationNative</c>.
    /// </summary>
    public unsafe ClassificationNativeTables PinNative()
    {
        List<nint> blocks = [];
        nint planes = ClassificationNativeTables.Alloc(blocks, 17 * nint.Size);
        nint* planeTable = (nint*)planes;
        for (int plane = 0; plane < 17; plane++)
        {
            nint pages = ClassificationNativeTables.Alloc(blocks, 256 * nint.Size);
            planeTable[plane] = pages;
            nint* pageTable = (nint*)pages;
            short[][] planePages = _planes[plane];
            for (int page = 0; page < 256; page++)
            {
                short[] cells = planePages[page];
                if (cells.Length == 1)
                {
                    pageTable[page] = cells[0];
                    continue;
                }

                nint cellBlock = ClassificationNativeTables.Alloc(blocks, 256 * sizeof(short));
                short* dest = (short*)cellBlock;
                for (int cell = 0; cell < 256; cell++)
                {
                    dest[cell] = cells[cell];
                }

                pageTable[page] = cellBlock;
            }
        }

        nint attrs = ClassificationNativeTables.Alloc(blocks, _attributes.Length * 8);
        byte* attrBytes = (byte*)attrs;
        for (int i = 0; i < _attributes.Length; i++)
        {
            CharacterAttributeRow row = _attributes[i];
            int offset = i * 8;
            attrBytes[offset] = row.Script;
            attrBytes[offset + 1] = row.ItemClass;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                new Span<byte>(attrBytes + offset + 2, 2),
                row.Flags);
            attrBytes[offset + 4] = row.BreakType;
            attrBytes[offset + 5] = row.BiDi;
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                new Span<byte>(attrBytes + offset + 6, 2),
                row.LineBreak);
        }

        return new ClassificationNativeTables(blocks, planes, attrs);
    }
}
