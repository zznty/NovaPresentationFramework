using System.Buffers.Binary;
using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Geometry2D;

/// <summary>
/// Decoder for the MIL path-geometry serialized stream produced by WPF's
/// <c>ByteStreamGeometryContext</c> (the buffer passed to <c>MilUtility_PathGeometryBounds</c>
/// and friends). Layout mirrors the native <c>MIL_PATHGEOMETRY</c> / <c>MIL_PATHFIGURE</c> /
/// <c>MIL_SEGMENT_*</c> structures in wgx_core_types.cs:
/// <list type="bullet">
/// <item>geometry header: uint Size, uint Flags, <see cref="MilRectD"/> Bounds (4 doubles),
///   uint FigureCount, uint ForcePacking (48 bytes total)</item>
/// <item>each figure: uint BackSize, uint Flags, uint Count, uint Size, double StartX/Y,
///   uint OffsetToLastSegment, uint ForcePacking (40 bytes)</item>
/// <item>segments written by ByteStreamGeometryContext are Poly* forms
///   (uint Type, uint Flags, uint BackSize, uint Count, then Count full double
///   <c>Point</c>s), plus the fixed 64-byte <c>MIL_SEGMENT_ARC</c> (Point and Size are full
///   double values); the compact MIL_SEGMENT_LINE/BEZIER/QUADRATICBEZIER forms are also
///   understood for robustness</item>
/// </list>
/// Points are full double-precision <c>System.Windows.Point</c> values (16 bytes each),
/// matching the serializer and the parser's <c>Point*</c> reads.
/// </summary>
[PublicAPI]
public static class MilPathData
{
    // MIL_SEGMENT_TYPE values (stream Type field; wgx_core_types.cs enum — note MilSegmentNone=0
    // precedes Line, so the compact segments are 1/2/3, not 0/1/2).
    internal const uint MilSegmentLine = 1;
    internal const uint MilSegmentBezier = 2;
    internal const uint MilSegmentQuadraticBezier = 3;
    internal const uint MilSegmentArc = 4;
    internal const uint MilSegmentPolyLine = 5;
    internal const uint MilSegmentPolyBezier = 6;
    internal const uint MilSegmentPolyQuadraticBezier = 7;

    // MilPathFigureFlags (wgx_core_types.cs): HasGaps=0x1, HasCurves=0x2, IsClosed=0x4,
    // IsFillable=0x8.
    internal const uint FigureIsFillable = 0x8;
    internal const uint FigureIsClosed = 0x4;

    // MIL_PATHGEOMETRY: Size(4) Flags(4) Bounds(32) FigureCount(4) ForcePacking(4) = 48.
    internal const int GeometryHeaderBytes = 48;
    internal const int FigureCountOffset = 40;

    // MIL_PATHFIGURE: BackSize(4) Flags(4) Count(4) Size(4) StartPoint(16)
    // OffsetToLastSegment(4) ForcePacking(4) = 40.
    internal const int FigureHeaderBytes = 40;
    internal const int FigureFlagsOffset = 4;
    internal const int FigureCountOffsetInFigure = 8;
    internal const int FigureSizeOffset = 12;
    internal const int FigureStartOffset = 16;

    // Segment header: Type(4) Flags(4) [BackSize(4)] [Count(4)].
    internal const int SegmentTypeOffset = 0;
    internal const int SegmentFlagsOffset = 4;
    internal const int SegmentPayloadOffset = 16; // Poly*: points start after the 16-byte header

    // MIL_SEGMENT_ARC: Type(4) Flags(4) BackSize(4) LargeArc(4) Point(16) Size(16)
    // XRotation(8) Sweep(4) ForcePacking(4) = 64.
    internal const int ArcLargeArcOffset = 12;
    internal const int ArcPointOffset = 16;
    internal const int ArcSizeOffset = 32;
    internal const int ArcRotationOffset = 48;
    internal const int ArcSweepOffset = 56;
    internal const int ArcSegmentBytes = 64;

    // Compact segments (MIL_SEGMENT_LINE/BEZIER/QUADRATICBEZIER):
    // Type(4) Flags(4) BackSize(4) ForcePacking(4) then points.
    internal const int CompactPointOffset = 16;
    internal const int LineSegmentBytes = 32;
    internal const int QuadraticSegmentBytes = 48;
    internal const int BezierSegmentBytes = 64;

    /// <summary>True when the stream has no figures.</summary>
    public static bool IsEmpty(ReadOnlySpan<byte> stream)
    {
        return stream.Length < GeometryHeaderBytes || ReadUInt32(stream, FigureCountOffset) == 0;
    }

    internal static uint ReadUInt32(ReadOnlySpan<byte> stream, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(offset, sizeof(uint)));
    }

    internal static float ReadSingle(ReadOnlySpan<byte> stream, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(stream.Slice(offset, sizeof(float)));
    }

    internal static double ReadDouble(ReadOnlySpan<byte> stream, int offset)
    {
        return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(stream.Slice(offset, sizeof(double))));
    }

    /// <summary>The declared stream byte size (MIL_PATHGEOMETRY.Size at offset 0).</summary>
    internal static uint ReadSize(ReadOnlySpan<byte> stream)
    {
        return ReadUInt32(stream, 0);
    }
}

/// <summary>Float point in the MIL path stream.</summary>
[PublicAPI]
public readonly struct MilPoint2F(float x, float y) : IEquatable<MilPoint2F>
{
    public float X { get; } = x;

    public float Y { get; } = y;

    public Point ToPoint()
    {
        return new Point(X, Y);
    }

    public bool Equals(MilPoint2F other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is MilPoint2F other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public static bool operator ==(MilPoint2F left, MilPoint2F right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MilPoint2F left, MilPoint2F right)
    {
        return !left.Equals(right);
    }
}

/// <summary>MIL double rect (left/top/right/bottom) as stored in the stream header.</summary>
[PublicAPI]
public readonly struct MilRectD(double left, double top, double right, double bottom) : IEquatable<MilRectD>
{
    public double Left { get; } = left;

    public double Top { get; } = top;

    public double Right { get; } = right;

    public double Bottom { get; } = bottom;

    public static MilRectD NaN { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);

    public Rect ToRect()
    {
        return new Rect(Left, Top, Right - Left, Bottom - Top);
    }

    public bool Equals(MilRectD other)
    {
        return Left.Equals(other.Left) && Top.Equals(other.Top) && Right.Equals(other.Right) && Bottom.Equals(other.Bottom);
    }

    public override bool Equals(object? obj)
    {
        return obj is MilRectD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Top, Right, Bottom);
    }

    public static bool operator ==(MilRectD left, MilRectD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MilRectD left, MilRectD right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Managed mirror of the native <c>MilMatrix3x2D</c> field order (S_11, S_12, S_21, S_22,
/// DX, DY), exposed with analyzer-clean names matching <see cref="Matrix3x2"/>.
/// </summary>
[PublicAPI]
public readonly struct MilMatrix3x2DManaged(double m11, double m12, double m21, double m22, double offsetX, double offsetY) : IEquatable<MilMatrix3x2DManaged>
{
    public double M11 { get; } = m11;
    public double M12 { get; } = m12;
    public double M21 { get; } = m21;
    public double M22 { get; } = m22;
    public double OffsetX { get; } = offsetX;
    public double OffsetY { get; } = offsetY;

    public Matrix3x2 ToMatrix3x2()
    {
        return new Matrix3x2(M11, M12, M21, M22, OffsetX, OffsetY);
    }

    public bool Equals(MilMatrix3x2DManaged other)
    {
        return M11.Equals(other.M11) && M12.Equals(other.M12) && M21.Equals(other.M21) && M22.Equals(other.M22) && OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY);
    }

    public override bool Equals(object? obj)
    {
        return obj is MilMatrix3x2DManaged other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);
    }

    public static bool operator ==(MilMatrix3x2DManaged left, MilMatrix3x2DManaged right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MilMatrix3x2DManaged left, MilMatrix3x2DManaged right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Managed mirror of the native <c>MilRectD</c> (left/top/right/bottom).</summary>
[PublicAPI]
public readonly struct MilRectDManaged(double left, double top, double right, double bottom) : IEquatable<MilRectDManaged>
{
    public double Left { get; } = left;
    public double Top { get; } = top;
    public double Right { get; } = right;
    public double Bottom { get; } = bottom;

    public bool Equals(MilRectDManaged other)
    {
        return Left.Equals(other.Left) && Top.Equals(other.Top) && Right.Equals(other.Right) && Bottom.Equals(other.Bottom);
    }

    public override bool Equals(object? obj)
    {
        return obj is MilRectDManaged other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Top, Right, Bottom);
    }

    public static bool operator ==(MilRectDManaged left, MilRectDManaged right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(MilRectDManaged left, MilRectDManaged right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Bridge entry point for the WPF <c>MilUtility_PathGeometryBounds</c> nest on Linux.
/// Takes the raw MIL path stream plus stroke thickness and the combined world*geometry
/// matrix components, returns the axis-aligned bounds as a <see cref="MilRectDManaged"/>.
/// </summary>
[PublicAPI]
public static class PathBoundsManaged
{
    public static MilRectDManaged OfPathManaged(
        ReadOnlySpan<byte> stream,
        double strokeThickness,
        double m11,
        double m12,
        double m21,
        double m22,
        double offsetX,
        double offsetY)
    {
        var world = new Matrix3x2(m11, m12, m21, m22, offsetX, offsetY);
        Rect bounds = PathBounds.OfPath(stream, strokeThickness, world);
        return new MilRectDManaged(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);
    }
}
