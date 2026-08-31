using System.Buffers.Binary;

namespace Nova.Geometry2D.Tests;

/// <summary>
/// Builds MIL path-geometry streams matching ByteStreamGeometryContext's real writer layout:
/// padded MIL_PATHGEOMETRY / MIL_PATHFIGURE headers and Poly* segments (MIL_SEGMENT_POLY)
/// plus the fixed 64-byte MIL_SEGMENT_ARC.
/// </summary>
internal sealed class PathStreamWriter
{
    private readonly List<byte> _bytes = [];
    private int _figureHeaderOffset = -1;
    private int _figureStart;
    private uint _segmentCount;
    private uint _figureCount;

    public PathStreamWriter()
    {
        U32(0); // Size, patched at Close
        U32(0); // Flags
        for (int i = 0; i < 4; i++)
        {
            F64(0); // Bounds (MilRectD)
        }

        U32(0); // FigureCount, patched at Close
        U32(0); // ForcePacking
    }

    public void BeginFigure(double startX, double startY, bool isFilled, bool isClosed)
    {
        _figureHeaderOffset = _bytes.Count;
        _figureStart = _bytes.Count;
        _figureCount++;
        U32(0); // BackSize
        U32((isFilled ? PathStreamFigureFlagsFillable : 0) | (isClosed ? PathStreamFigureFlagsClosed : 0));
        U32(0); // Count, patched at EndFigure
        U32(0); // Size, patched at EndFigure
        F64(startX);
        F64(startY);
        U32(0); // OffsetToLastSegment
        U32(0); // ForcePacking
        _segmentCount = 0;
    }

    public void LineTo(double x, double y)
    {
        U32(5); // Type = MilSegmentPolyLine
        U32(0); // Flags
        U32(0); // BackSize
        U32(1); // Count
        F64(x); // Point (full double Point)
        F64(y);
        _segmentCount++;
    }

    public void BezierTo(double c1x, double c1y, double c2x, double c2y, double ex, double ey)
    {
        U32(6); // Type = MilSegmentPolyBezier
        U32(0); // Flags
        U32(0); // BackSize
        U32(3); // Count = the RAW POINT count (the real writer counts per point; 3 per cubic)
        F64(c1x);
        F64(c1y);
        F64(c2x);
        F64(c2y);
        F64(ex);
        F64(ey);
        _segmentCount++;
    }

    public void QuadraticTo(double cx, double cy, double ex, double ey)
    {
        U32(7); // Type = MilSegmentPolyQuadraticBezier
        U32(0); // Flags
        U32(0); // BackSize
        U32(2); // Count = the RAW POINT count (2 per quadratic)
        F64(cx);
        F64(cy);
        F64(ex);
        F64(ey);
        _segmentCount++;
    }

    public void PolyLineTo(params (double X, double Y)[] points)
    {
        U32(5); // Type = MilSegmentPolyLine
        U32(0); // Flags
        U32(0); // BackSize
        U32((uint)points.Length);
        foreach ((double x, double y) in points)
        {
            F64(x);
            F64(y);
        }

        _segmentCount++;
    }

    public void ArcTo(double x, double y, double radiusX, double radiusY, double rotation, bool isLargeArc, bool sweepClockwise)
    {
        U32(4); // Type = MilSegmentArc
        U32(0); // Flags
        U32(0); // BackSize
        U32(isLargeArc ? 1u : 0u); // LargeArc
        F64(x); // Point (2 doubles)
        F64(y);
        F64(radiusX); // Size (2 doubles)
        F64(radiusY);
        F64(rotation); // XRotation
        U32(sweepClockwise ? 1u : 0u); // Sweep
        U32(0); // ForcePacking
        _segmentCount++;
    }

    public void EndFigure()
    {
        int now = _bytes.Count;
        PatchU32(_figureHeaderOffset + 8, _segmentCount); // Count
        PatchU32(_figureHeaderOffset + 12, (uint)(now - _figureStart)); // Size
    }

    public byte[] Close()
    {
        if (_figureHeaderOffset >= 0)
        {
            EndFigure();
        }

        PatchU32(40, _figureCount); // FigureCount
        PatchU32(0, (uint)_bytes.Count); // Size
        return [.. _bytes];
    }

    private void U32(uint value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _bytes.AddRange(span);
    }

    private void F32(float value)
    {
        Span<byte> span = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(span, value);
        _bytes.AddRange(span);
    }

    private void F64(double value)
    {
        Span<byte> span = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(span, BitConverter.DoubleToInt64Bits(value));
        _bytes.AddRange(span);
    }

    private void PatchU32(int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_bytes).Slice(offset, 4), value);
    }

    private const uint PathStreamFigureFlagsFillable = 0x8;
    private const uint PathStreamFigureFlagsClosed = 0x4;
}
