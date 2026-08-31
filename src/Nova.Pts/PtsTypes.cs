using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.Pts;

/// <summary>
/// PTS error codes (FSERR). Zero (<see cref="None"/>) is success; negative values are failures.
/// The values mirror the WPF nest enum in <c>MS.Internal.PtsHost.PTS</c> (blittable <c>int</c>).
/// </summary>
[PublicAPI]
public enum PtsErr
{
    None = 0,
    InvalidParameter = -1,
    OutOfMemory = -2,
    NullOutputParameter = -3,
    InvalidContext = -4,
    InvalidLine = -5,
    InvalidDnode = -6,
    InvalidDeviceResolution = -7,
    InvalidRun = -8,
    MismatchLineContext = -9,
    ContextInUse = -10,
    DuplicateSpecialCharacter = -11,
    InvalidAutonumRun = -12,
    FormattingFunctionDisabled = -13,
    UnfinishedDnode = -14,
    InvalidDnodeType = -15,
    InvalidPenDnode = -16,
    InvalidNonPenDnode = -17,
    InvalidBaselinePenDnode = -18,
    InvalidFormatterResult = -19,
    InvalidObjectIdFetched = -20,
    InvalidDcpFetched = -21,
    InvalidCpContentFetched = -22,
    InvalidBookmarkType = -23,
    SetDocDisabled = -24,
    FiniFunctionDisabled = -25,
    CurrentDnodeIsNotTab = -26,
    PendingTabIsNotResolved = -27,
    WrongFiniFunction = -28,
    InvalidBreakingClass = -29,
    BreakingTableNotSet = -30,
    InvalidModWidthClass = -31,
    ModWidthPairsNotSet = -32,
    NotImplemented = -10000,
    CallbackException = -100002
}

/// <summary>
/// Result of bottomless paragraph formatting (<c>FSFMTRBL</c>).
/// </summary>
[PublicAPI]
public enum FsFmtResult
{
    /// <summary>Formatting reached the end of the content.</summary>
    GoalReached = 0,

    /// <summary>Formatting stopped because of a collision with an obstacle.</summary>
    Collision = 1,

    /// <summary>Formatting was interrupted (background formatting).</summary>
    Interrupted = 2
}

/// <summary>
/// Update kind (<c>FSKUPDATE</c>): how a formatted object changed relative to the previous format.
/// </summary>
[PublicAPI]
public enum FsKUpdate
{
    Inherited = 0,
    NoChange = 1,
    New = 2,
    ChangeInside = 3,
    Shifted = 4
}

/// <summary>
/// Kind of text lines (<c>FSKTEXTLINES</c>): which FormatLine callback was used when formatting.
/// </summary>
[PublicAPI]
public enum FsKTextLines
{
    /// <summary>Normal <c>FormatLine</c> callback (greedy).</summary>
    Normal = 0,

    /// <summary>Optimal breaking via <c>ReconstructLineVariant</c>.</summary>
    Optimal = 1,

    /// <summary>Special forced formatting via <c>FormatLineForced</c>.</summary>
    Forced = 2,

    /// <summary>Word-compatibility callback.</summary>
    Word = 3
}

/// <summary>
/// Text paragraph details variant (<c>FSKTEXTDETAILS</c>).
/// </summary>
[PublicAPI]
public enum FsKTextDetails
{
    Cached = 0,
    Full = 1
}

/// <summary>
/// Line formatting result (<c>FSFLRES</c>). Values mirror <c>PTS.FSFLRES</c> in the WPF nest.
/// </summary>
[PublicAPI]
public enum FsFlres
{
    OutOfSpace = 0,
    OutOfSpaceHyphenated = 1,
    EndOfParagraph = 2,
    EndOfParagraphClearLeft = 3,
    EndOfParagraphClearRight = 4,
    EndOfParagraphClearBoth = 5,
    PageBreak = 6,
    ColumnBreak = 7,
    SoftBreak = 8,
    SoftBreakClearLeft = 9,
    SoftBreakClearRight = 10,
    SoftBreakClearBoth = 11,
    NoProgressClear = 12
}

/// <summary>
/// Kind of clearing (<c>FSKCLEAR</c>). Values mirror <c>PTS.FSKCLEAR</c> in the WPF nest.
/// </summary>
[PublicAPI]
public enum FsKClear
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = 3
}

/// <summary>
/// Point in PTS text coordinates (<c>FSPOINT</c>): <c>u</c> is the horizontal (flow) coordinate,
/// <c>v</c> the vertical. Text device units (1/96 inch per text point).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsPoint(int u, int v) : IEquatable<FsPoint>
{
    public int U = u;
    public int V = v;

    public readonly bool Equals(FsPoint other)
    {
        return U == other.U && V == other.V;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsPoint other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(U);
        h.Add(V);
        return h.ToHashCode();
    }

    public static bool operator ==(FsPoint left, FsPoint right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsPoint left, FsPoint right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Vector in PTS text coordinates (<c>FSVECTOR</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsVector(int du, int dv) : IEquatable<FsVector>
{
    public int DU = du;
    public int DV = dv;

    public readonly bool Equals(FsVector other)
    {
        return DU == other.DU && DV == other.DV;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsVector other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(DU);
        h.Add(DV);
        return h.ToHashCode();
    }

    public static bool operator ==(FsVector left, FsVector right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsVector left, FsVector right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Rectangle in PTS text coordinates (<c>FSRECT</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsRect(int u, int v, int du, int dv) : IEquatable<FsRect>
{
    public int U = u;
    public int V = v;
    public int DU = du;
    public int DV = dv;

    public readonly bool IsEmpty => DU == 0 && DV == 0;

    public static bool operator ==(FsRect left, FsRect right)
    {
        return left.U == right.U && left.V == right.V && left.DU == right.DU && left.DV == right.DV;
    }

    public static bool operator !=(FsRect left, FsRect right)
    {
        return !(left == right);
    }

    public readonly bool Equals(FsRect other)
    {
        return this == other;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsRect other && this == other;
    }

    public override readonly int GetHashCode()
    {
        return U ^ V ^ DU ^ DV;
    }

    public override readonly string ToString()
    {
        return $"u={U} v={V} du={DU} dv={DV}";
    }
}

/// <summary>
/// Bounding box in PTS text coordinates (<c>FSBBOX</c>): an optionally defined rectangle.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsBbox(int fDefined, FsRect fsrc) : IEquatable<FsBbox>
{
    public int FDefined = fDefined;
    public FsRect Fsrc = fsrc;

    public readonly bool Equals(FsBbox other)
    {
        return FDefined == other.FDefined && Fsrc == other.Fsrc;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsBbox other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(FDefined);
        h.Add(Fsrc);
        return h.ToHashCode();
    }

    public static bool operator ==(FsBbox left, FsBbox right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsBbox left, FsBbox right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Update info attached to formatted objects (<c>FSUPDATEINFO</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsUpdateInfo : IEquatable<FsUpdateInfo>
{
    public FsKUpdate Fskupd;
    public int DvrShifted;

    public readonly bool Equals(FsUpdateInfo other)
    {
        return Fskupd == other.Fskupd && DvrShifted == other.DvrShifted;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsUpdateInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fskupd);
        h.Add(DvrShifted);
        return h.ToHashCode();
    }

    public static bool operator ==(FsUpdateInfo left, FsUpdateInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsUpdateInfo left, FsUpdateInfo right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Paragraph properties (<c>FSPAP</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsPap : IEquatable<FsPap>
{
    public int Idobj;
    public int FKeepWithNext;
    public int FBreakPageBefore;
    public int FBreakColumnBefore;

    public readonly bool Equals(FsPap other)
    {
        return Idobj == other.Idobj && FKeepWithNext == other.FKeepWithNext && FBreakPageBefore == other.FBreakPageBefore && FBreakColumnBefore == other.FBreakColumnBefore;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsPap other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Idobj);
        h.Add(FKeepWithNext);
        h.Add(FBreakPageBefore);
        h.Add(FBreakColumnBefore);
        return h.ToHashCode();
    }

    public static bool operator ==(FsPap left, FsPap right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsPap left, FsPap right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Text paragraph properties (<c>FSTXTPROPS</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsTxtProps : IEquatable<FsTxtProps>
{
    public uint Fswdir;
    public int DcpStartContent;
    public int FKeepTogether;
    public int FDropCap;
    public int CMinLinesAfterBreak;
    public int CMinLinesBeforeBreak;
    public int FVerticalGrid;
    public int FOptimizeParagraph;
    public int FAvoidHyphenationAtTrackBottom;
    public int FAvoidHyphenationOnLastChainElement;
    public int CMaxConsecutiveHyphens;

    public readonly bool Equals(FsTxtProps other)
    {
        return Fswdir == other.Fswdir && DcpStartContent == other.DcpStartContent && FKeepTogether == other.FKeepTogether && FDropCap == other.FDropCap && CMinLinesAfterBreak == other.CMinLinesAfterBreak && CMinLinesBeforeBreak == other.CMinLinesBeforeBreak && FVerticalGrid == other.FVerticalGrid && FOptimizeParagraph == other.FOptimizeParagraph && FAvoidHyphenationAtTrackBottom == other.FAvoidHyphenationAtTrackBottom && FAvoidHyphenationOnLastChainElement == other.FAvoidHyphenationOnLastChainElement && CMaxConsecutiveHyphens == other.CMaxConsecutiveHyphens;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsTxtProps other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fswdir);
        h.Add(DcpStartContent);
        h.Add(FKeepTogether);
        h.Add(FDropCap);
        h.Add(CMinLinesAfterBreak);
        h.Add(CMinLinesBeforeBreak);
        h.Add(FVerticalGrid);
        h.Add(FOptimizeParagraph);
        h.Add(FAvoidHyphenationAtTrackBottom);
        h.Add(FAvoidHyphenationOnLastChainElement);
        h.Add(CMaxConsecutiveHyphens);
        return h.ToHashCode();
    }

    public static bool operator ==(FsTxtProps left, FsTxtProps right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsTxtProps left, FsTxtProps right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Column layout info (<c>FSCOLUMNINFO</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsColumnInfo : IEquatable<FsColumnInfo>
{
    public int DurBefore;
    public int DurWidth;

    public readonly bool Equals(FsColumnInfo other)
    {
        return DurBefore == other.DurBefore && DurWidth == other.DurWidth;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsColumnInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(DurBefore);
        h.Add(DurWidth);
        return h.ToHashCode();
    }

    public static bool operator ==(FsColumnInfo left, FsColumnInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsColumnInfo left, FsColumnInfo right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Description of a single line in a text paragraph (<c>FSLINEDESCRIPTIONSINGLE</c>).
/// <c>Pfslineclient</c> is the client's line handle (opaque; created by the FormatLine callback).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsLineDescriptionSingle : IEquatable<FsLineDescriptionSingle>
{
    public IntPtr Pfslineclient;
    public IntPtr Pfsbreakreclineclient;
    public int DcpFirst;
    public int DcpLim;
    public int UrStart;
    public int Dur;
    public int FAllowHyphenation;
    public int UrBBox;
    public int DurBBox;
    public int VrStart;
    public int DvrAscent;
    public int DvrDescent;
    public int FClearOnLeft;
    public int FClearOnRight;
    public int FTreatedAsFirst;
    public int FForceBroken;

    public readonly bool Equals(FsLineDescriptionSingle other)
    {
        return Pfslineclient == other.Pfslineclient && Pfsbreakreclineclient == other.Pfsbreakreclineclient && DcpFirst == other.DcpFirst && DcpLim == other.DcpLim && UrStart == other.UrStart && Dur == other.Dur && FAllowHyphenation == other.FAllowHyphenation && UrBBox == other.UrBBox && DurBBox == other.DurBBox && VrStart == other.VrStart && DvrAscent == other.DvrAscent && DvrDescent == other.DvrDescent && FClearOnLeft == other.FClearOnLeft && FClearOnRight == other.FClearOnRight && FTreatedAsFirst == other.FTreatedAsFirst && FForceBroken == other.FForceBroken;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsLineDescriptionSingle other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Pfslineclient);
        h.Add(Pfsbreakreclineclient);
        h.Add(DcpFirst);
        h.Add(DcpLim);
        h.Add(UrStart);
        h.Add(Dur);
        h.Add(FAllowHyphenation);
        h.Add(UrBBox);
        h.Add(DurBBox);
        h.Add(VrStart);
        h.Add(DvrAscent);
        h.Add(DvrDescent);
        h.Add(FClearOnLeft);
        h.Add(FClearOnRight);
        h.Add(FTreatedAsFirst);
        h.Add(FForceBroken);
        return h.ToHashCode();
    }

    public static bool operator ==(FsLineDescriptionSingle left, FsLineDescriptionSingle right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsLineDescriptionSingle left, FsLineDescriptionSingle right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Full text paragraph details (<c>FSTEXTDETAILSFULL</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsTextDetailsFull : IEquatable<FsTextDetailsFull>
{
    public uint Fswdir;
    public FsKTextLines Fsklines;
    public int FLinesComposite;
    public int CLines;
    public int CAttachedObjects;
    public int DcpFirst;
    public int DcpLim;
    public int FDropCapPresent;
    public FsUpdateInfo FsupdinfDropCap;
    public int FSuppressTopLineSpacing;
    public int FUpdateInfoForLinesPresent;
    public int CLinesBeforeChange;
    public int DvrShiftBeforeChange;
    public int CLinesChanged;
    public int DcLinesChanged;
    public int DvrShiftAfterChange;
    public int DdcpAfterChange;

    public readonly bool Equals(FsTextDetailsFull other)
    {
        return Fswdir == other.Fswdir && Fsklines == other.Fsklines && FLinesComposite == other.FLinesComposite && CLines == other.CLines && CAttachedObjects == other.CAttachedObjects && DcpFirst == other.DcpFirst && DcpLim == other.DcpLim && FDropCapPresent == other.FDropCapPresent && FsupdinfDropCap == other.FsupdinfDropCap && FSuppressTopLineSpacing == other.FSuppressTopLineSpacing && FUpdateInfoForLinesPresent == other.FUpdateInfoForLinesPresent && CLinesBeforeChange == other.CLinesBeforeChange && DvrShiftBeforeChange == other.DvrShiftBeforeChange && CLinesChanged == other.CLinesChanged && DcLinesChanged == other.DcLinesChanged && DvrShiftAfterChange == other.DvrShiftAfterChange && DdcpAfterChange == other.DdcpAfterChange;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsTextDetailsFull other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fswdir);
        h.Add(Fsklines);
        h.Add(FLinesComposite);
        h.Add(CLines);
        h.Add(CAttachedObjects);
        h.Add(DcpFirst);
        h.Add(DcpLim);
        h.Add(FDropCapPresent);
        h.Add(FsupdinfDropCap);
        h.Add(FSuppressTopLineSpacing);
        h.Add(FUpdateInfoForLinesPresent);
        h.Add(CLinesBeforeChange);
        h.Add(DvrShiftBeforeChange);
        h.Add(CLinesChanged);
        h.Add(DcLinesChanged);
        h.Add(DvrShiftAfterChange);
        h.Add(DdcpAfterChange);
        return h.ToHashCode();
    }

    public static bool operator ==(FsTextDetailsFull left, FsTextDetailsFull right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsTextDetailsFull left, FsTextDetailsFull right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Text paragraph details (<c>FSTEXTDETAILS</c>). Only the <see cref="Full"/> variant is produced.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsTextDetails : IEquatable<FsTextDetails>
{
    public FsKTextDetails Fsktd;
    public FsTextDetailsFull Full;

    public readonly bool Equals(FsTextDetails other)
    {
        return Fsktd == other.Fsktd && Full == other.Full;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsTextDetails other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fsktd);
        h.Add(Full);
        return h.ToHashCode();
    }

    public static bool operator ==(FsTextDetails left, FsTextDetails right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsTextDetails left, FsTextDetails right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Paragraph description within a track (<c>FSPARADESCRIPTION</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsParaDescription : IEquatable<FsParaDescription>
{
    public FsUpdateInfo Fsupdinf;
    public IntPtr Pfspara;
    public IntPtr Pfsparaclient;
    public IntPtr Nmp;
    public int Idobj;
    public int DvrUsed;
    public FsBbox Fsbbox;
    public int DvrTopSpace;

    public readonly bool Equals(FsParaDescription other)
    {
        return Fsupdinf == other.Fsupdinf && Pfspara == other.Pfspara && Pfsparaclient == other.Pfsparaclient && Nmp == other.Nmp && Idobj == other.Idobj && DvrUsed == other.DvrUsed && Fsbbox == other.Fsbbox && DvrTopSpace == other.DvrTopSpace;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsParaDescription other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fsupdinf);
        h.Add(Pfspara);
        h.Add(Pfsparaclient);
        h.Add(Nmp);
        h.Add(Idobj);
        h.Add(DvrUsed);
        h.Add(Fsbbox);
        h.Add(DvrTopSpace);
        return h.ToHashCode();
    }

    public static bool operator ==(FsParaDescription left, FsParaDescription right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsParaDescription left, FsParaDescription right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Track details (<c>FSTRACKDETAILS</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsTrackDetails : IEquatable<FsTrackDetails>
{
    public int CParas;

    public readonly bool Equals(FsTrackDetails other)
    {
        return CParas == other.CParas;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsTrackDetails other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(CParas);
        return h.ToHashCode();
    }

    public static bool operator ==(FsTrackDetails left, FsTrackDetails right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsTrackDetails left, FsTrackDetails right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Track description (<c>FSTRACKDESCRIPTION</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsTrackDescription : IEquatable<FsTrackDescription>
{
    public FsUpdateInfo Fsupdinf;
    public IntPtr Nms;
    public FsRect Fsrc;
    public FsBbox Fsbbox;
    public int FTrackRelativeToRect;
    public IntPtr Pfstrack;

    public readonly bool Equals(FsTrackDescription other)
    {
        return Fsupdinf == other.Fsupdinf && Nms == other.Nms && Fsrc == other.Fsrc && Fsbbox == other.Fsbbox && FTrackRelativeToRect == other.FTrackRelativeToRect && Pfstrack == other.Pfstrack;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsTrackDescription other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fsupdinf);
        h.Add(Nms);
        h.Add(Fsrc);
        h.Add(Fsbbox);
        h.Add(FTrackRelativeToRect);
        h.Add(Pfstrack);
        return h.ToHashCode();
    }

    public static bool operator ==(FsTrackDescription left, FsTrackDescription right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsTrackDescription left, FsTrackDescription right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Section description (<c>FSSECTIONDESCRIPTION</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsSectionDescription : IEquatable<FsSectionDescription>
{
    public FsUpdateInfo Fsupdinf;
    public IntPtr Nms;
    public FsRect Fsrc;
    public FsBbox Fsbbox;
    public int FOtherSectionInside;
    public int DvrUsedTop;
    public int DvrUsedBottom;
    public IntPtr Pfssection;

    public readonly bool Equals(FsSectionDescription other)
    {
        return Fsupdinf == other.Fsupdinf && Nms == other.Nms && Fsrc == other.Fsrc && Fsbbox == other.Fsbbox && FOtherSectionInside == other.FOtherSectionInside && DvrUsedTop == other.DvrUsedTop && DvrUsedBottom == other.DvrUsedBottom && Pfssection == other.Pfssection;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsSectionDescription other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fsupdinf);
        h.Add(Nms);
        h.Add(Fsrc);
        h.Add(Fsbbox);
        h.Add(FOtherSectionInside);
        h.Add(DvrUsedTop);
        h.Add(DvrUsedBottom);
        h.Add(Pfssection);
        return h.ToHashCode();
    }

    public static bool operator ==(FsSectionDescription left, FsSectionDescription right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsSectionDescription left, FsSectionDescription right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Section details with page notes (<c>FSSECTIONDETAILSWITHPAGENOTES</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsSectionDetailsWithPageNotes : IEquatable<FsSectionDetailsWithPageNotes>
{
    public uint Fswdir;
    public int FColumnBalancingApplied;
    public FsRect FsrcSectionBody;
    public FsBbox FsbboxSectionBody;
    public int CBasicColumns;
    public int CSegmentDefinedColumnSpanAreas;
    public int CHeightDefinedColumnSpanAreas;
    public FsRect FsrcEndnote;
    public FsBbox FsbboxEndnote;
    public int CEndnoteColumns;
    public FsTrackDescription TrackdescrEndnoteSeparator;

    public readonly bool Equals(FsSectionDetailsWithPageNotes other)
    {
        return Fswdir == other.Fswdir && FColumnBalancingApplied == other.FColumnBalancingApplied && FsrcSectionBody == other.FsrcSectionBody && FsbboxSectionBody == other.FsbboxSectionBody && CBasicColumns == other.CBasicColumns && CSegmentDefinedColumnSpanAreas == other.CSegmentDefinedColumnSpanAreas && CHeightDefinedColumnSpanAreas == other.CHeightDefinedColumnSpanAreas && FsrcEndnote == other.FsrcEndnote && FsbboxEndnote == other.FsbboxEndnote && CEndnoteColumns == other.CEndnoteColumns && TrackdescrEndnoteSeparator == other.TrackdescrEndnoteSeparator;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsSectionDetailsWithPageNotes other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fswdir);
        h.Add(FColumnBalancingApplied);
        h.Add(FsrcSectionBody);
        h.Add(FsbboxSectionBody);
        h.Add(CBasicColumns);
        h.Add(CSegmentDefinedColumnSpanAreas);
        h.Add(CHeightDefinedColumnSpanAreas);
        h.Add(FsrcEndnote);
        h.Add(FsbboxEndnote);
        h.Add(CEndnoteColumns);
        h.Add(TrackdescrEndnoteSeparator);
        return h.ToHashCode();
    }

    public static bool operator ==(FsSectionDetailsWithPageNotes left, FsSectionDetailsWithPageNotes right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsSectionDetailsWithPageNotes left, FsSectionDetailsWithPageNotes right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Section details (<c>FSSECTIONDETAILS</c>). Only the page-notes variant is produced.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsSectionDetails : IEquatable<FsSectionDetails>
{
    public int FFootnotesAsPagenotes;
    public FsSectionDetailsWithPageNotes WithPageNotes;

    public readonly bool Equals(FsSectionDetails other)
    {
        return FFootnotesAsPagenotes == other.FFootnotesAsPagenotes && WithPageNotes == other.WithPageNotes;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsSectionDetails other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(FFootnotesAsPagenotes);
        h.Add(WithPageNotes);
        return h.ToHashCode();
    }

    public static bool operator ==(FsSectionDetails left, FsSectionDetails right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsSectionDetails left, FsSectionDetails right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Simple page details (<c>FSPAGEDETAILSSIMPLE</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsPageDetailsSimple : IEquatable<FsPageDetailsSimple>
{
    public FsTrackDescription Trackdescr;

    public readonly bool Equals(FsPageDetailsSimple other)
    {
        return Trackdescr == other.Trackdescr;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsPageDetailsSimple other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Trackdescr);
        return h.ToHashCode();
    }

    public static bool operator ==(FsPageDetailsSimple left, FsPageDetailsSimple right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsPageDetailsSimple left, FsPageDetailsSimple right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Page details (<c>FSPAGEDETAILS</c>). Only the simple variant is produced.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsPageDetails : IEquatable<FsPageDetails>
{
    public FsKUpdate Fskupd;
    public int FSimple;
    public FsPageDetailsSimple Simple;

    public readonly bool Equals(FsPageDetails other)
    {
        return Fskupd == other.Fskupd && FSimple == other.FSimple && Simple == other.Simple;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsPageDetails other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fskupd);
        h.Add(FSimple);
        h.Add(Simple);
        return h.ToHashCode();
    }

    public static bool operator ==(FsPageDetails left, FsPageDetails right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsPageDetails left, FsPageDetails right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Subtrack details (<c>FSSUBTRACKDETAILS</c>). Reserved for the finite/multi-column boundary.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsSubtrackDetails : IEquatable<FsSubtrackDetails>
{
    public FsUpdateInfo Fsupdinf;
    public IntPtr Nms;
    public FsRect Fsrc;
    public int CParas;

    public readonly bool Equals(FsSubtrackDetails other)
    {
        return Fsupdinf == other.Fsupdinf && Nms == other.Nms && Fsrc == other.Fsrc && CParas == other.CParas;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsSubtrackDetails other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(Fsupdinf);
        h.Add(Nms);
        h.Add(Fsrc);
        h.Add(CParas);
        return h.ToHashCode();
    }

    public static bool operator ==(FsSubtrackDetails left, FsSubtrackDetails right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsSubtrackDetails left, FsSubtrackDetails right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Installed-object method table (<c>FSIMETHODS</c>). Only <see cref="PfnFormatParaBottomless"/>
/// (subtrack formatting of the main text segment) is driven on the plain path; the remaining
/// slots are reserved for finite/subpage/table formatting and stay zero.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[PublicAPI]
public struct FsIMethods : IEquatable<FsIMethods>
{
    public IntPtr PfnCreateContext;
    public IntPtr PfnDestroyContext;
    public IntPtr PfnFormatParaFinite;
    public PtsDelegates.ObjFormatParaBottomless PfnFormatParaBottomless;
    public IntPtr PfnUpdateBottomlessPara;
    public IntPtr PfnSynchronizeBottomlessPara;
    public IntPtr PfnComparePara;
    public IntPtr PfnClearUpdateInfoInPara;
    public IntPtr PfnDestroyPara;
    public IntPtr PfnDuplicateBreakRecord;
    public IntPtr PfnDestroyBreakRecord;
    public IntPtr PfnGetColumnBalancingInfo;
    public IntPtr PfnGetNumberFootnotes;
    public IntPtr PfnGetFootnoteInfo;
    public IntPtr PfnGetFootnoteInfoWord;
    public IntPtr PfnShiftVertical;
    public IntPtr PfnTransferDisplayInfoPara;

    public readonly bool Equals(FsIMethods other)
    {
        return PfnCreateContext == other.PfnCreateContext && PfnDestroyContext == other.PfnDestroyContext && PfnFormatParaFinite == other.PfnFormatParaFinite && PfnFormatParaBottomless == other.PfnFormatParaBottomless && PfnUpdateBottomlessPara == other.PfnUpdateBottomlessPara && PfnSynchronizeBottomlessPara == other.PfnSynchronizeBottomlessPara && PfnComparePara == other.PfnComparePara && PfnClearUpdateInfoInPara == other.PfnClearUpdateInfoInPara && PfnDestroyPara == other.PfnDestroyPara && PfnDuplicateBreakRecord == other.PfnDuplicateBreakRecord && PfnDestroyBreakRecord == other.PfnDestroyBreakRecord && PfnGetColumnBalancingInfo == other.PfnGetColumnBalancingInfo && PfnGetNumberFootnotes == other.PfnGetNumberFootnotes && PfnGetFootnoteInfo == other.PfnGetFootnoteInfo && PfnGetFootnoteInfoWord == other.PfnGetFootnoteInfoWord && PfnShiftVertical == other.PfnShiftVertical && PfnTransferDisplayInfoPara == other.PfnTransferDisplayInfoPara;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is FsIMethods other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        HashCode h = new();
        h.Add(PfnCreateContext);
        h.Add(PfnDestroyContext);
        h.Add(PfnFormatParaFinite);
        h.Add(PfnFormatParaBottomless);
        h.Add(PfnUpdateBottomlessPara);
        h.Add(PfnSynchronizeBottomlessPara);
        h.Add(PfnComparePara);
        h.Add(PfnClearUpdateInfoInPara);
        h.Add(PfnDestroyPara);
        h.Add(PfnDuplicateBreakRecord);
        h.Add(PfnDestroyBreakRecord);
        h.Add(PfnGetColumnBalancingInfo);
        h.Add(PfnGetNumberFootnotes);
        h.Add(PfnGetFootnoteInfo);
        h.Add(PfnGetFootnoteInfoWord);
        h.Add(PfnShiftVertical);
        h.Add(PfnTransferDisplayInfoPara);
        return h.ToHashCode();
    }

    public static bool operator ==(FsIMethods left, FsIMethods right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FsIMethods left, FsIMethods right)
    {
        return !left.Equals(right);
    }
}
