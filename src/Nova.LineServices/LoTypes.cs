using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.LineServices;

/// <summary>
/// Line Services error codes (LSERR). Zero (<see cref="None"/>) is success; negative values are
/// failures. The values mirror the WPF nest enum in
/// <c>MS.Internal.TextFormatting</c> (blittable <c>int</c>).
/// </summary>
[PublicAPI]
public enum LsErr
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
    WrongTruncationPoint = -33,
    WrongBreak = -34,
    DupInvalid = -35,
    RubyInvalidVersion = -36,
    TatenakayokoInvalidVersion = -37,
    WarichuInvalidVersion = -38,
    WarichuInvalidData = -39,
    CreateSublineDisabled = -40,
    CurrentSublineDoesNotExist = -41,
    CpOutsideSubline = -42,
    HihInvalidVersion = -43,
    InsufficientQueryDepth = -44,
    InvalidBreakRecord = -45,
    InvalidPap = -46,
    ContradictoryQueryInput = -47,
    LineIsNotActive = -48,
    TooLongParagraph = -49,
    TooManyCharsToGlyph = -50,
    WrongHyphenationPosition = -51,
    TooManyPriorities = -52,
    WrongGivenCp = -53,
    WrongCpFirstForGetBreaks = -54,
    WrongJustTypeForGetBreaks = -55,
    WrongJustTypeForCreateLineGivenCp = -56,
    TooLongGlyphContext = -57,
    InvalidCharToGlyphMapping = -58,
    InvalidMathUsage = -59,
    InconsistentChp = -60,
    StoppedInSubline = -61,
    PenPositionCouldNotBeUsed = -62,
    DebugFlagsInShip = -63,
    InvalidOrderTabs = -64,
    OutputArrayTooSmall = -110,
    SystemRestrictionsExceeded = -100,
    LsInternalError = -1000,
    NotImplemented = -10000,
    ClientAbort = -100000,
}

/// <summary>Opaque handle to a text run, as passed to the run callbacks.</summary>
[PublicAPI]
public enum Plsrun
{
    CloseAnchor = 0,
    Reverse = 1,
    FakeLineBreak = 2,
    FormatAnchor = 3,
    Hidden = 4,
    Text = 5,
    InlineObject = 6,
    LineBreak = 7,
    ParaBreak = 8,

    Undefined = int.MinValue,
    IsMarker = 0x40000000,
    UseNewCharacterBuffer = 0x20000000,
    IsSymbol = 0x10000000,
    UnmaskAll = 0x0FFFFFFF,
}

/// <summary>Line ending results (LSENDRES).</summary>
[PublicAPI]
public enum LsEndRes
{
    endrNormal,
    endrHyphenated,
    endrEndPara,
    endrAltPara,
    endrSoftCR,
    endrEndColumn,
    endrEndSection,
    endrEndPage,
    endrEndParaSection,
    endrStopped,
    endrBeforeFillLineObject,
    endrAfterFillLineObject,
    endrMathUserRequiredBreak,
}

/// <summary>Paragraph break justification mode (LSBREAKJUST).</summary>
[PublicAPI]
public enum LsBreakJust
{
    lsbrjBreakJustify,
    lsbrjBreakWithCompJustify,
    lsbrjBreakThenExpand,
    lsbrjBreakOptimal,
    lsbrjBreakThenSqueeze,
}

/// <summary>Line justification kind (LSKJUST).</summary>
[PublicAPI]
public enum LsKJust
{
    lskjFullInterWord,
    lskjFullInterLetterAligned,
    lskjFullScaled,
    lskjFullGlyphs,
    lskjFullMixed,
    lskjSnapGrid,
}

/// <summary>Line alignment (LSKALIGN).</summary>
[PublicAPI]
public enum LsKAlign
{
    lskalLeft,
    lskalCentered,
    lskalRight,
}

/// <summary>
/// Paragraph end option (LSKEOP). Member values match the WPF nest; names drop the type-name
/// prefix (CA1712) and are not part of the ABI.
/// </summary>
[PublicAPI]
public enum LsKEOP
{
    EndPara1,
    EndPara2,
    EndPara12,
    EndParaAlt,
}

/// <summary>Tab kind (LSKTAB).</summary>
[PublicAPI]
public enum LsKTab
{
    lsktLeft,
    lsktCenter,
    lsktRight,
    lsktDecimal,
    lsktChar,
}

/// <summary>
/// Text flow direction (LSTFLOW). Values match the WPF nest (ES is 0); the nest's
/// <c>lstflowDefault</c> alias is dropped because it duplicates ES (CA1069). The engine passes
/// <see cref="ES"/> for default horizontal flow.
/// </summary>
[PublicAPI]
public enum LsTFlow
{
    ES = 0,
    EN,
    SE,
    SW,
    WS,
    WN,
    NE,
    NW,
}

/// <summary>Break condition (LSBRKCOND).</summary>
[PublicAPI]
public enum LsBrkCond
{
    Never,
    Can,
    Please,
    Must,
}

/// <summary>Device used for a metric query.</summary>
[PublicAPI]
public enum LsDevice
{
    Presentation,
    Reference,
}

/// <summary>Expansion type (LSEXPTYPE).</summary>
[PublicAPI]
public enum LsExpType
{
    None = 0,
    AddWhiteSpace,
    AddInkContinuous,
    AddInkDiscrete,
}

/// <summary>Paragraph property flags (LSPAP.grpf). Layout matches the WPF nest's
/// <c>LsPap.Flags</c> (uint-sized); the name drops the CA1711 "Flags" suffix and stays plural
/// for <see cref="System.FlagsAttribute"/>.</summary>
[PublicAPI]
public enum LsPapOptions : int
{
    None = 0,
    fFmiVisiCondHyphens = 0x00000001,
    fFmiVisiParaMarks = 0x00000002,
    fFmiVisiSpaces = 0x00000004,
    fFmiVisiTabs = 0x00000008,
    fFmiVisiSplats = 0x00000010,
    fFmiVisiBreaks = 0x00000020,
    fFmiApplyBreakingRules = 0x00000040,
    fFmiApplyOpticalAlignment = 0x00000080,
    fFmiPunctStartLine = 0x00000100,
    fFmiHangingPunct = 0x00000200,
    fFmiPresSuppressWiggle = 0x00000400,
    fFmiPresExactSync = 0x00000800,
    fFmiAnm = 0x00001000,
    fFmiAutoDecimalTab = 0x00002000,
    fFmiUnderlineTrailSpacesRM = 0x00004000,
    fFmiSpacesInfluenceHeight = 0x00008000,
    fFmiIgnoreSplatBreak = 0x00010000,
    fFmiLimSplat = 0x00020000,
    fFmiAllowSplatLine = 0x00040000,
    fFmiForceBreakAsNext = 0x00080000,
    fFmiAllowHyphenation = 0x00100000,
    fFmiDrawInCharCodes = 0x00200000,
    fFmiTreatHyphenAsRegular = 0x00400000,
    fFmiWrapTrailingSpaces = 0x00800000,
    fFmiWrapAllSpaces = 0x01000000,
    fFmiFCheckTruncateBefore = 0x02000000,
    fFmiForgetLastTabAlignment = 0x10000000,
    fFmiIndentChangesHyphenZone = 0x20000000,
    fFmiNoPunctAfterAutoNumber = 0x40000000,
    fFmiResolveTabsAsWord97 = unchecked((int)0x80000000),
}

/// <summary>Character property flags (LSCHP.flags). Layout matches the WPF nest's
/// <c>LsChp.Flags</c> (uint-sized); the name drops the CA1711 "Flags" suffix and stays plural
/// for <see cref="System.FlagsAttribute"/>.</summary>
[PublicAPI]
[Flags]
public enum LsChpOptions : int
{
    None = 0,
    fApplyKern = 0x0001,
    fModWidthOnRun = 0x0002,
    fModWidthSpace = 0x0004,
    fModWidthPairs = 0x0008,
    fCompressOnRun = 0x0010,
    fCompressSpace = 0x0020,
    fCompressTable = 0x0040,
    fExpandOnRun = 0x0080,
    fExpandSpace = 0x0100,
    fExpandTable = 0x0200,
    fGlyphBased = 0x0400,
    fInvisible = 0x00010000,
    fUnderline = 0x00020000,
    fStrike = 0x00040000,
    fShade = 0x00080000,
    fBorder = 0x00100000,
    fSymbol = 0x00200000,
    fHyphen = 0x00400000,
    fCheckForReplaceChar = 0x00800000,
}

/// <summary>Point in LS device units.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LSPOINT : IEquatable<LSPOINT>
{
    public int x;
    public int y;

    public readonly bool Equals(LSPOINT other)
    {
        return x == other.x
            && y == other.y;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LSPOINT other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(x, y);
    }

    public static bool operator ==(LSPOINT left, LSPOINT right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LSPOINT left, LSPOINT right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Rectangle in LS device units.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LSRECT : IEquatable<LSRECT>
{
    public int left;
    public int top;
    public int right;
    public int bottom;

    public readonly bool Equals(LSRECT other)
    {
        return left == other.left
            && top == other.top
            && right == other.right
            && bottom == other.bottom;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LSRECT other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(left, top, right, bottom);
    }

    public static bool operator ==(LSRECT left, LSRECT right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LSRECT left, LSRECT right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Output of <c>LoGetEscString</c>: six NUL-terminated WCHAR escape strings.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct EscStringInfo : IEquatable<EscStringInfo>
{
    public IntPtr szParaSeparator;
    public IntPtr szLineSeparator;
    public IntPtr szHidden;
    public IntPtr szNbsp;
    public IntPtr szObjectTerminator;
    public IntPtr szObjectReplacement;

    public readonly bool Equals(EscStringInfo other)
    {
        return szParaSeparator == other.szParaSeparator
            && szLineSeparator == other.szLineSeparator
            && szHidden == other.szHidden
            && szNbsp == other.szNbsp
            && szObjectTerminator == other.szObjectTerminator
            && szObjectReplacement == other.szObjectReplacement;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is EscStringInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(szParaSeparator, szLineSeparator, szHidden, szNbsp, szObjectTerminator, szObjectReplacement);
    }

    public static bool operator ==(EscStringInfo left, EscStringInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EscStringInfo left, EscStringInfo right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Tab definition (LSTBD).</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsTbd : IEquatable<LsTbd>
{
    public LsKTab lskt;
    public int ur;
    public char wchTabLeader;
    public char wchCharTab;

    public readonly bool Equals(LsTbd other)
    {
        return lskt == other.lskt
            && ur == other.ur
            && wchTabLeader == other.wchTabLeader
            && wchCharTab == other.wchCharTab;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsTbd other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(lskt, ur, wchTabLeader, wchCharTab);
    }

    public static bool operator ==(LsTbd left, LsTbd right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsTbd left, LsTbd right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Tab configuration (LSTABS).</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsTabs : IEquatable<LsTabs>
{
    public int durIncrementalTab;
    public int iTabUserDefMac;
    public IntPtr plsTbd;

    public readonly bool Equals(LsTabs other)
    {
        return durIncrementalTab == other.durIncrementalTab
            && iTabUserDefMac == other.iTabUserDefMac
            && plsTbd == other.plsTbd;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsTabs other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(durIncrementalTab, iTabUserDefMac, plsTbd);
    }

    public static bool operator ==(LsTabs left, LsTabs right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsTabs left, LsTabs right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Device resolutions in LS units (LSDEVRES).</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsDevRes : IEquatable<LsDevRes>
{
    public uint dxpInch;
    public uint dypInch;
    public uint dxrInch;
    public uint dyrInch;

    public readonly bool Equals(LsDevRes other)
    {
        return dxpInch == other.dxpInch
            && dypInch == other.dypInch
            && dxrInch == other.dxrInch
            && dyrInch == other.dyrInch;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsDevRes other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(dxpInch, dypInch, dxrInch, dyrInch);
    }

    public static bool operator ==(LsDevRes left, LsDevRes right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsDevRes left, LsDevRes right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Paragraph properties (LSPAP), returned to the engine through FetchPap.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsPap : IEquatable<LsPap>
{
    public int cpFirst;
    public int cpFirstContent;
    public LsPapOptions grpf;
    public LsBreakJust lsbrj;
    public LsKJust lskj;
    public int fJustify;
    public int durAutoDecimalTab;
    public LsKEOP lskeop;
    public LsTFlow lstflow;

    public readonly bool Equals(LsPap other)
    {
        return cpFirst == other.cpFirst
            && cpFirstContent == other.cpFirstContent
            && grpf == other.grpf
            && lsbrj == other.lsbrj
            && lskj == other.lskj
            && fJustify == other.fJustify
            && durAutoDecimalTab == other.durAutoDecimalTab
            && lskeop == other.lskeop
            && lstflow == other.lstflow;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsPap other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(cpFirst, cpFirstContent, grpf, lsbrj, lskj, fJustify, durAutoDecimalTab, lskeop);
    }

    public static bool operator ==(LsPap left, LsPap right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsPap left, LsPap right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Line properties, returned through FetchLineProps.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsLineProps : IEquatable<LsLineProps>
{
    public LsKAlign lskal;
    public int durLeft;
    public int durRightBreak;
    public int durRightJustify;
    public int fProhibitHyphenation;
    public int durHyphenationZone;

    public readonly bool Equals(LsLineProps other)
    {
        return lskal == other.lskal
            && durLeft == other.durLeft
            && durRightBreak == other.durRightBreak
            && durRightJustify == other.durRightJustify
            && fProhibitHyphenation == other.fProhibitHyphenation
            && durHyphenationZone == other.durHyphenationZone;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsLineProps other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(lskal, durLeft, durRightBreak, durRightJustify, fProhibitHyphenation, durHyphenationZone);
    }

    public static bool operator ==(LsLineProps left, LsLineProps right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsLineProps left, LsLineProps right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Character properties (LSCHP), returned through FetchRunRedefined.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsChp : IEquatable<LsChp>
{
    public ushort idObj;
    public ushort dcpMaxContent;
    public uint effectsFlags;
    public LsChpOptions flags;
    public int dvpPos;

    public readonly bool Equals(LsChp other)
    {
        return idObj == other.idObj
            && dcpMaxContent == other.dcpMaxContent
            && effectsFlags == other.effectsFlags
            && flags == other.flags
            && dvpPos == other.dvpPos;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsChp other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(idObj, dcpMaxContent, effectsFlags, flags, dvpPos);
    }

    public static bool operator ==(LsChp left, LsChp right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsChp left, LsChp right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Text metrics (LSTXM), returned through GetRunTextMetrics.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsTxM : IEquatable<LsTxM>
{
    public int dvAscent;
    public int dvDescent;
    public int dvMultiLineHeight;
    public int fMonospaced;

    public readonly bool Equals(LsTxM other)
    {
        return dvAscent == other.dvAscent
            && dvDescent == other.dvDescent
            && dvMultiLineHeight == other.dvMultiLineHeight
            && fMonospaced == other.fMonospaced;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsTxM other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(dvAscent, dvDescent, dvMultiLineHeight, fMonospaced);
    }

    public static bool operator ==(LsTxM left, LsTxM right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsTxM left, LsTxM right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Heights (LSHEIGHTS), used for line height accounting.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsHeights : IEquatable<LsHeights>
{
    public int dvAscent;
    public int dvDescent;
    public int dvMultiLineHeight;

    public readonly bool Equals(LsHeights other)
    {
        return dvAscent == other.dvAscent
            && dvDescent == other.dvDescent
            && dvMultiLineHeight == other.dvMultiLineHeight;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsHeights other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(dvAscent, dvDescent, dvMultiLineHeight);
    }

    public static bool operator ==(LsHeights left, LsHeights right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsHeights left, LsHeights right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Line information (LSLINFO), returned by <c>LoCreateLine</c>.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsLInfo : IEquatable<LsLInfo>
{
    public int dvpAscent;
    public int dvrAscent;
    public int dvpDescent;
    public int dvrDescent;
    public int dvpMultiLineHeight;
    public int dvrMultiLineHeight;
    public int dvpAscentAutoNumber;
    public int dvrAscentAutoNumber;
    public int dvpDescentAutoNumber;
    public int dvrDescentAutoNumber;
    public int cpLimToContinue;
    public int cpLimToStay;
    public int dcpDepend;
    public int cpFirstVis;
    public LsEndRes endr;
    public int fAdvanced;
    public int vaAdvance;
    public int fFirstLineInPara;
    public int fTabInMarginExLine;
    public int fForcedBreak;
    public uint EffectsFlags;

    public readonly bool Equals(LsLInfo other)
    {
        return dvpAscent == other.dvpAscent
            && dvrAscent == other.dvrAscent
            && dvpDescent == other.dvpDescent
            && dvrDescent == other.dvrDescent
            && dvpMultiLineHeight == other.dvpMultiLineHeight
            && dvrMultiLineHeight == other.dvrMultiLineHeight
            && dvpAscentAutoNumber == other.dvpAscentAutoNumber
            && dvrAscentAutoNumber == other.dvrAscentAutoNumber
            && dvpDescentAutoNumber == other.dvpDescentAutoNumber
            && dvrDescentAutoNumber == other.dvrDescentAutoNumber
            && cpLimToContinue == other.cpLimToContinue
            && cpLimToStay == other.cpLimToStay
            && dcpDepend == other.dcpDepend
            && cpFirstVis == other.cpFirstVis
            && endr == other.endr
            && fAdvanced == other.fAdvanced
            && vaAdvance == other.vaAdvance
            && fFirstLineInPara == other.fFirstLineInPara
            && fTabInMarginExLine == other.fTabInMarginExLine
            && fForcedBreak == other.fForcedBreak
            && EffectsFlags == other.EffectsFlags;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsLInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(dvpAscent, dvrAscent, dvpDescent, dvrDescent, dvpMultiLineHeight, dvrMultiLineHeight, dvpAscentAutoNumber, dvrAscentAutoNumber);
    }

    public static bool operator ==(LsLInfo left, LsLInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsLInfo left, LsLInfo right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Text cell (LSTEXTCELL), output of the line hit-testing queries.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsTextCell : IEquatable<LsTextCell>
{
    public int lscpStartCell;
    public int lscpEndCell;
    public LSPOINT pointUvStartCell;
    public int dupCell;
    public int cCharsInCell;
    public int cGlyphsInCell;
    public IntPtr plsCellDetails;

    public readonly bool Equals(LsTextCell other)
    {
        return lscpStartCell == other.lscpStartCell
            && lscpEndCell == other.lscpEndCell
            && pointUvStartCell == other.pointUvStartCell
            && dupCell == other.dupCell
            && cCharsInCell == other.cCharsInCell
            && cGlyphsInCell == other.cGlyphsInCell
            && plsCellDetails == other.plsCellDetails;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsTextCell other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(lscpStartCell, lscpEndCell, pointUvStartCell, dupCell, cCharsInCell, cGlyphsInCell, plsCellDetails);
    }

    public static bool operator ==(LsTextCell left, LsTextCell right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsTextCell left, LsTextCell right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Glyph offset in device units (LS glyph-offset pair), layout-compatible with the nest.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct GlyphOffset : IEquatable<GlyphOffset>
{
    public short du;
    public short dv;

    public readonly bool Equals(GlyphOffset other)
    {
        return du == other.du
            && dv == other.dv;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is GlyphOffset other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(du, dv);
    }

    public static bool operator ==(GlyphOffset left, GlyphOffset right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GlyphOffset left, GlyphOffset right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Sub-line information (LSQSUBINFO), output of the line hit-testing queries. Layout matches the
/// WPF nest <c>LsQSubInfo</c>. The v1 engine fills element 0 only (a single sub-line spanning
/// the queried run): the caller reads <c>plsrun</c> and <c>lstflowSubLine</c> from it to resolve
/// the run and the caret direction.
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsQSubInfo : IEquatable<LsQSubInfo>
{
    public LsTFlow lstflowSubLine;
    public int lscpFirstSubLine;
    public int lsdcpSubLine;
    public LSPOINT pointUvStartSubLine;
    public LsHeights lsHeightsPresSubLine;
    public int dupSubLine;

    public uint idobj;
    public IntPtr plsrun;
    public int lscpFirstRun;
    public int lsdcpRun;
    public LSPOINT pointUvStartRun;
    public LsHeights lsHeightsPresRun;
    public int dupRun;
    public int dvpPosRun;
    public int dupBorderBefore;
    public int dupBorderAfter;

    public LSPOINT pointUvStartObj;
    public LsHeights lsHeightsPresObj;
    public int dupObj;

    public readonly bool Equals(LsQSubInfo other)
    {
        return lstflowSubLine == other.lstflowSubLine
            && lscpFirstSubLine == other.lscpFirstSubLine
            && lsdcpSubLine == other.lsdcpSubLine
            && pointUvStartSubLine == other.pointUvStartSubLine
            && lsHeightsPresSubLine == other.lsHeightsPresSubLine
            && dupSubLine == other.dupSubLine
            && idobj == other.idobj
            && plsrun == other.plsrun
            && lscpFirstRun == other.lscpFirstRun
            && lsdcpRun == other.lsdcpRun
            && pointUvStartRun == other.pointUvStartRun
            && lsHeightsPresRun == other.lsHeightsPresRun
            && dupRun == other.dupRun
            && dvpPosRun == other.dvpPosRun
            && dupBorderBefore == other.dupBorderBefore
            && dupBorderAfter == other.dupBorderAfter
            && pointUvStartObj == other.pointUvStartObj
            && lsHeightsPresObj == other.lsHeightsPresObj
            && dupObj == other.dupObj;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsQSubInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(lstflowSubLine, lscpFirstSubLine, lsdcpSubLine, pointUvStartSubLine, lsHeightsPresSubLine, dupSubLine, idobj, plsrun);
    }

    public static bool operator ==(LsQSubInfo left, LsQSubInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsQSubInfo left, LsQSubInfo right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Line widths (LSLINEWIDTHS), returned by <c>LoCreateLine</c>.</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public struct LsLineWidths : IEquatable<LsLineWidths>
{
    public int upStartMarker;
    public int upLimMarker;
    public int upStartMainText;
    public int upStartTrailing;
    public int upLimLine;
    public int upMinStartTrailing;
    public int upMinLimLine;

    public readonly bool Equals(LsLineWidths other)
    {
        return upStartMarker == other.upStartMarker
            && upLimMarker == other.upLimMarker
            && upStartMainText == other.upStartMainText
            && upStartTrailing == other.upStartTrailing
            && upLimLine == other.upLimLine
            && upMinStartTrailing == other.upMinStartTrailing
            && upMinLimLine == other.upMinLimLine;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsLineWidths other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(upStartMarker, upLimMarker, upStartMainText, upStartTrailing, upLimLine, upMinStartTrailing, upMinLimLine);
    }

    public static bool operator ==(LsLineWidths left, LsLineWidths right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsLineWidths left, LsLineWidths right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Break array (LSBREAKS), output of <c>LoCreateBreaks</c> (stub in v1).</summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential)]
public unsafe struct LsBreaks : IEquatable<LsBreaks>
{
    public int cBreaks;
    public LsLInfo* plslinfoArray;
    public IntPtr* plinepenaltyArray;
    public IntPtr* pplolineArray;

    public readonly bool Equals(LsBreaks other)
    {
        return cBreaks == other.cBreaks
            && plslinfoArray == other.plslinfoArray
            && plinepenaltyArray == other.plinepenaltyArray
            && pplolineArray == other.pplolineArray;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsBreaks other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(cBreaks, (nint)plslinfoArray, (nint)plinepenaltyArray, (nint)pplolineArray);
    }

    public static bool operator ==(LsBreaks left, LsBreaks right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsBreaks left, LsBreaks right)
    {
        return !left.Equals(right);
    }
}
