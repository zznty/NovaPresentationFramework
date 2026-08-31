using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Nova.LineServices;

/// <summary>
/// Fetch paragraph properties for the paragraph containing <paramref name="lscpFetch"/>.
/// Called by <c>LoCreateLine</c> before any run is fetched.
/// </summary>
[PublicAPI]
public delegate LsErr FetchPap(IntPtr pols, int lscpFetch, ref LsPap lspap);

/// <summary>
/// Fetch line properties (margins/alignment) for the line starting at
/// <paramref name="lscpFetch"/>. <paramref name="firstLineInPara"/> is a logical boolean
/// (1 when this is the first line of the paragraph).
/// </summary>
[PublicAPI]
public delegate LsErr FetchLineProps(IntPtr pols, int lscpFetch, int firstLineInPara, ref LsLineProps lsLineProps);

/// <summary>
/// Fetch the next run of text starting at <paramref name="lscpFetch"/>.
/// <para>
/// The run text is either copied into <paramref name="pwchTextBuffer"/> (capacity
/// <paramref name="cchTextBuffer"/>; <paramref name="fIsBufferUsed"/> set to 1) or returned
/// directly in <paramref name="pwchText"/>. When the buffer is too small the callback returns
/// <see cref="LsErr.None"/> with <paramref name="fIsBufferUsed"/> = 0 and
/// <paramref name="pwchText"/> = null; <paramref name="cchText"/> then reports the required
/// length and the engine grows its buffer and retries.
/// </para>
/// </summary>
[PublicAPI]
public unsafe delegate LsErr FetchRunRedefined(
    IntPtr pols,
    int lscpFetch,
    int fIsStyle,
    IntPtr pstyle,
    char* pwchTextBuffer,
    int cchTextBuffer,
    ref int fIsBufferUsed,
    out char* pwchText,
    ref int cchText,
    ref int fIsHidden,
    ref LsChp lschp,
    ref IntPtr lsplsrun);

/// <summary>
/// Fill <paramref name="charWidths"/> (per character) and the run <paramref name="totalWidth"/>
/// for at most <paramref name="maxWidth"/> of column width. Reports how many characters were
/// processed in <paramref name="cchProcessed"/>. The WPF nest contract allows at most one
/// character that does not fully fit to be included.
/// </summary>
[PublicAPI]
public unsafe delegate LsErr GetRunCharWidths(
    IntPtr pols,
    Plsrun plsrun,
    LsDevice device,
    char* runText,
    int cchRun,
    int maxWidth,
    LsTFlow textFlow,
    int* charWidths,
    ref int totalWidth,
    ref int cchProcessed);

/// <summary>Fill run text metrics (ascent/descent/height) for presentation or reference device.</summary>
[PublicAPI]
public delegate LsErr GetRunTextMetrics(IntPtr pols, Plsrun plsrun, LsDevice lsDevice, LsTFlow lstFlow, ref LsTxM lstTextMetrics);

/// <summary>
/// Draw one text run, driven by <c>LoDisplayLine</c>. The WPF nest installs this as a
/// function pointer in <c>LsContextInfo.pfnDrawTextRun</c>; the engine recovers a managed
/// delegate via <c>Marshal.GetDelegateForFunctionPointer</c>. <paramref name="ptText"/> is the
/// run origin in LS device units (the engine accumulates run origins from the line reference
/// origin; for right-to-left flow it starts at the right edge and moves left, mirroring native
/// LS). <paramref name="ptRun"/> carries the same origin, <paramref name="dupRun"/> the run
/// advance width.
/// </summary>
[PublicAPI]
public unsafe delegate LsErr DrawTextRun(
    IntPtr pols,
    Plsrun plsrun,
    ref LSPOINT ptText,
    char* runText,
    int* charWidths,
    int cchText,
    LsTFlow textFlow,
    uint displayMode,
    ref LSPOINT ptRun,
    ref LsHeights lsHeights,
    int dupRun,
    ref LSRECT clipRect);

/// <summary>
/// Enumerate one text run, driven by <c>LoEnumLine</c> (the WPF nest installs this as
/// <c>LsContextInfo.pfnEnumText</c>; the engine calls the managed delegate through the 0007
/// bridge trampoline). <paramref name="cpFirst"/>/<paramref name="dcp"/> are the run's client
/// cp range, <paramref name="pwchText"/>/<paramref name="cchText"/> its text,
/// <paramref name="pptStart"/> its origin in LS units, <paramref name="dupRun"/> its width, and
/// <paramref name="charWidths"/> its per-character advances (the engine never drives glyph-based
/// enumeration, so <paramref name="glyphBaseRun"/> is 0 and the glyph pointers are null).
/// </summary>
[PublicAPI]
public unsafe delegate LsErr EnumText(
    IntPtr pols,
    Plsrun plsrun,
    int cpFirst,
    int dcp,
    char* pwchText,
    int cchText,
    LsTFlow lstFlow,
    int fReverseOrder,
    int fGeometryProvided,
    ref LSPOINT pptStart,
    ref LsHeights pheights,
    int dupRun,
    int glyphBaseRun,
    int* charWidths,
    ushort* pClusterMap,
    ushort* characterProperties,
    ushort* puglyphs,
    int* pGlyphAdvances,
    GlyphOffset* pGlyphOffsets,
    uint* pGlyphProperties,
    int glyphCount);

/// <summary>
/// Enumerate one tab run, driven by <c>LoEnumLine</c> for runs whose text is a single tab
/// character. <paramref name="tabLeader"/> is the tab-leader character (0 when none).
/// </summary>
[PublicAPI]
public unsafe delegate LsErr EnumTab(
    IntPtr pols,
    Plsrun plsrun,
    int cpFirst,
    char* pwchText,
    char tabLeader,
    LsTFlow lstFlow,
    int fReverseOrder,
    int fGeometryProvided,
    ref LSPOINT pptStart,
    ref LsHeights heights,
    int dupRun);

/// <summary>
/// The redefined callback table passed to <c>LoCreateContext</c>. Layout matches the WPF nest
/// <c>LscbkRedefined</c> (three function pointers).
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct LscbkRedefined : IEquatable<LscbkRedefined>
{
    public FetchRunRedefined? pfnFetchRunRedefined;
    public IntPtr pfnGetGlyphsRedefined;
    public FetchLineProps pfnFetchLineProps;

    public readonly bool Equals(LscbkRedefined other)
    {
        return pfnFetchRunRedefined == other.pfnFetchRunRedefined
            && pfnGetGlyphsRedefined == other.pfnGetGlyphsRedefined
            && pfnFetchLineProps == other.pfnFetchLineProps;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LscbkRedefined other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(pfnFetchRunRedefined, pfnGetGlyphsRedefined, pfnFetchLineProps);
    }

    public static bool operator ==(LscbkRedefined left, LscbkRedefined right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LscbkRedefined left, LscbkRedefined right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Context configuration (LSCONTEXTINFO) passed to <c>LoCreateContext</c>. Blittable layout
/// identical to the WPF nest <c>LsContextInfo</c>: every callback slot is pointer-sized (a
/// managed delegate field occupies one pointer in a sequential layout, so the field order and
/// size match the nest exactly). The v1 engine drives <see cref="pfnFetchPap"/>,
/// <see cref="pfnFetchLineProps"/>, <see cref="pfnGetRunCharWidths"/>,
/// <see cref="pfnGetRunTextMetrics"/> plus the redefined fetch callback and
/// <see cref="pfnDrawTextRun"/> (display); all other slots are zero (not installed) and typed as
/// <see cref="IntPtr"/>. Like the driven callbacks, <see cref="pfnDrawTextRun"/> is a managed
/// delegate handed in by the nest bridge through a trampoline — not a raw function pointer, so
/// no <c>GetDelegateForFunctionPointer</c> round-trip (which cannot re-wrap a thunk created from
/// the WPF delegate type).
/// </summary>
[PublicAPI]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct LsContextInfo : IEquatable<LsContextInfo>
{
    public uint version;
    public int cInstalledHandlers;
    public IntPtr plsimethods;
    public int cEstimatedCharsPerLine;
    public int cJustPriorityLim;
    public char wchUndef;
    public char wchNull;
    public char wchSpace;
    public char wchHyphen;
    public char wchTab;
    public char wchPosTab;
    public char wchEndPara1;
    public char wchEndPara2;
    public char wchAltEndPara;
    public char wchEndLineInPara;
    public char wchColumnBreak;
    public char wchSectionBreak;
    public char wchPageBreak;
    public char wchNonBreakSpace;
    public char wchNonBreakHyphen;
    public char wchNonReqHyphen;
    public char wchEmDash;
    public char wchEnDash;
    public char wchEmSpace;
    public char wchEnSpace;
    public char wchNarrowSpace;
    public char wchOptBreak;
    public char wchNoBreak;
    public char wchFESpace;
    public char wchJoiner;
    public char wchNonJoiner;
    public char wchToReplace;
    public char wchReplace;
    public char wchVisiNull;
    public char wchVisiAltEndPara;
    public char wchVisiEndLineInPara;
    public char wchVisiEndPara;
    public char wchVisiSpace;
    public char wchVisiNonBreakSpace;
    public char wchVisiNonBreakHyphen;
    public char wchVisiNonReqHyphen;
    public char wchVisiTab;
    public char wchVisiPosTab;
    public char wchVisiEmSpace;
    public char wchVisiEnSpace;
    public char wchVisiNarrowSpace;
    public char wchVisiOptBreak;
    public char wchVisiNoBreak;
    public char wchVisiFESpace;
    public char wchEscAnmRun;
    public char wchPad;
    public IntPtr pols;
    public IntPtr pfnNewPtr;
    public IntPtr pfnDisposePtr;
    public IntPtr pfnReallocPtr;
    public IntPtr pfnFetchRun;
    public IntPtr pfnGetAutoNumberInfo;
    public IntPtr pfnGetNumericSeparators;
    public IntPtr pfnCheckForDigit;
    public FetchPap? pfnFetchPap;
    public FetchLineProps? pfnFetchLineProps;
    public IntPtr pfnFetchTabs;
    public IntPtr pfnReleaseTabsBuffer;
    public IntPtr pfnGetBreakThroughTab;
    public IntPtr pfnGetPosTabProps;
    public IntPtr pfnFGetLastLineJustification;
    public IntPtr pfnCheckParaBoundaries;
    public GetRunCharWidths? pfnGetRunCharWidths;
    public IntPtr pfnCheckRunKernability;
    public IntPtr pfnGetRunCharKerning;
    public GetRunTextMetrics? pfnGetRunTextMetrics;
    public IntPtr pfnGetRunUnderlineInfo;
    public IntPtr pfnGetRunStrikethroughInfo;
    public IntPtr pfnGetBorderInfo;
    public IntPtr pfnReleaseRun;
    public IntPtr pfnReleaseRunBuffer;
    public IntPtr pfnHyphenate;
    public IntPtr pfnGetPrevHyphenOpp;
    public IntPtr pfnGetNextHyphenOpp;
    public IntPtr pfnGetHyphenInfo;
    public IntPtr pfnDrawUnderline;
    public IntPtr pfnDrawStrikethrough;
    public IntPtr pfnDrawBorder;
    public IntPtr pfnFInterruptUnderline;
    public IntPtr pfnFInterruptShade;
    public IntPtr pfnFInterruptBorder;
    public IntPtr pfnShadeRectangle;
    public DrawTextRun? pfnDrawTextRun;
    public IntPtr pfnDrawSplatLine;
    public IntPtr pfnFInterruptShaping;
    public IntPtr pfnGetGlyphs;
    public IntPtr pfnGetGlyphPositions;
    public IntPtr pfnDrawGlyphs;
    public IntPtr pfnReleaseGlyphBuffers;
    public IntPtr pfnGetGlyphExpansionInfo;
    public IntPtr pfnGetGlyphExpansionInkInfo;
    public IntPtr pfnGetGlyphRunInk;
    public IntPtr pfnGetEms;
    public IntPtr pfnPunctStartLine;
    public IntPtr pfnModWidthOnRun;
    public IntPtr pfnModWidthSpace;
    public IntPtr pfnCompOnRun;
    public IntPtr pfnCompWidthSpace;
    public IntPtr pfnExpOnRun;
    public IntPtr pfnExpWidthSpace;
    public IntPtr pfnGetModWidthClasses;
    public IntPtr pfnGetBreakingClasses;
    public IntPtr pfnFTruncateBefore;
    public IntPtr pfnCanBreakBeforeChar;
    public IntPtr pfnCanBreakAfterChar;
    public IntPtr pfnFHangingPunct;
    public IntPtr pfnGetSnapGrid;
    public IntPtr pfnDrawEffects;
    public IntPtr pfnFCancelHangingPunct;
    public IntPtr pfnModifyCompAtLastChar;
    public IntPtr pfnGetDurMaxExpandRagged;
    public IntPtr pfnGetCharExpansionInfoFullMixed;
    public IntPtr pfnGetGlyphExpansionInfoFullMixed;
    public IntPtr pfnGetCharCompressionInfoFullMixed;
    public IntPtr pfnGetGlyphCompressionInfoFullMixed;
    public IntPtr pfnGetCharAlignmentStartLine;
    public IntPtr pfnGetCharAlignmentEndLine;
    public IntPtr pfnGetGlyphAlignmentStartLine;
    public IntPtr pfnGetGlyphAlignmentEndLine;
    public IntPtr pfnGetPriorityForGoodTypography;
    public EnumText? pfnEnumText;
    public EnumTab? pfnEnumTab;
    public IntPtr pfnEnumPen;
    public IntPtr pfnGetObjectHandlerInfo;
    public IntPtr pfnAssertFailedPtr;
    public int fDontReleaseRuns;

    public readonly bool Equals(LsContextInfo other)
    {
        return version == other.version
            && cInstalledHandlers == other.cInstalledHandlers
            && plsimethods == other.plsimethods
            && cEstimatedCharsPerLine == other.cEstimatedCharsPerLine
            && cJustPriorityLim == other.cJustPriorityLim
            && wchUndef == other.wchUndef
            && wchNull == other.wchNull
            && wchSpace == other.wchSpace
            && wchHyphen == other.wchHyphen
            && wchTab == other.wchTab
            && wchPosTab == other.wchPosTab
            && wchEndPara1 == other.wchEndPara1
            && wchEndPara2 == other.wchEndPara2
            && wchAltEndPara == other.wchAltEndPara
            && wchEndLineInPara == other.wchEndLineInPara
            && wchColumnBreak == other.wchColumnBreak
            && wchSectionBreak == other.wchSectionBreak
            && wchPageBreak == other.wchPageBreak
            && wchNonBreakSpace == other.wchNonBreakSpace
            && wchNonBreakHyphen == other.wchNonBreakHyphen
            && wchNonReqHyphen == other.wchNonReqHyphen
            && wchEmDash == other.wchEmDash
            && wchEnDash == other.wchEnDash
            && wchEmSpace == other.wchEmSpace
            && wchEnSpace == other.wchEnSpace
            && wchNarrowSpace == other.wchNarrowSpace
            && wchOptBreak == other.wchOptBreak
            && wchNoBreak == other.wchNoBreak
            && wchFESpace == other.wchFESpace
            && wchJoiner == other.wchJoiner
            && wchNonJoiner == other.wchNonJoiner
            && wchToReplace == other.wchToReplace
            && wchReplace == other.wchReplace
            && wchVisiNull == other.wchVisiNull
            && wchVisiAltEndPara == other.wchVisiAltEndPara
            && wchVisiEndLineInPara == other.wchVisiEndLineInPara
            && wchVisiEndPara == other.wchVisiEndPara
            && wchVisiSpace == other.wchVisiSpace
            && wchVisiNonBreakSpace == other.wchVisiNonBreakSpace
            && wchVisiNonBreakHyphen == other.wchVisiNonBreakHyphen
            && wchVisiNonReqHyphen == other.wchVisiNonReqHyphen
            && wchVisiTab == other.wchVisiTab
            && wchVisiPosTab == other.wchVisiPosTab
            && wchVisiEmSpace == other.wchVisiEmSpace
            && wchVisiEnSpace == other.wchVisiEnSpace
            && wchVisiNarrowSpace == other.wchVisiNarrowSpace
            && wchVisiOptBreak == other.wchVisiOptBreak
            && wchVisiNoBreak == other.wchVisiNoBreak
            && wchVisiFESpace == other.wchVisiFESpace
            && wchEscAnmRun == other.wchEscAnmRun
            && wchPad == other.wchPad
            && pols == other.pols
            && pfnNewPtr == other.pfnNewPtr
            && pfnDisposePtr == other.pfnDisposePtr
            && pfnReallocPtr == other.pfnReallocPtr
            && pfnFetchRun == other.pfnFetchRun
            && pfnGetAutoNumberInfo == other.pfnGetAutoNumberInfo
            && pfnGetNumericSeparators == other.pfnGetNumericSeparators
            && pfnCheckForDigit == other.pfnCheckForDigit
            && pfnFetchPap == other.pfnFetchPap
            && pfnFetchLineProps == other.pfnFetchLineProps
            && pfnFetchTabs == other.pfnFetchTabs
            && pfnReleaseTabsBuffer == other.pfnReleaseTabsBuffer
            && pfnGetBreakThroughTab == other.pfnGetBreakThroughTab
            && pfnGetPosTabProps == other.pfnGetPosTabProps
            && pfnFGetLastLineJustification == other.pfnFGetLastLineJustification
            && pfnCheckParaBoundaries == other.pfnCheckParaBoundaries
            && pfnGetRunCharWidths == other.pfnGetRunCharWidths
            && pfnCheckRunKernability == other.pfnCheckRunKernability
            && pfnGetRunCharKerning == other.pfnGetRunCharKerning
            && pfnGetRunTextMetrics == other.pfnGetRunTextMetrics
            && pfnGetRunUnderlineInfo == other.pfnGetRunUnderlineInfo
            && pfnGetRunStrikethroughInfo == other.pfnGetRunStrikethroughInfo
            && pfnGetBorderInfo == other.pfnGetBorderInfo
            && pfnReleaseRun == other.pfnReleaseRun
            && pfnReleaseRunBuffer == other.pfnReleaseRunBuffer
            && pfnHyphenate == other.pfnHyphenate
            && pfnGetPrevHyphenOpp == other.pfnGetPrevHyphenOpp
            && pfnGetNextHyphenOpp == other.pfnGetNextHyphenOpp
            && pfnGetHyphenInfo == other.pfnGetHyphenInfo
            && pfnDrawUnderline == other.pfnDrawUnderline
            && pfnDrawStrikethrough == other.pfnDrawStrikethrough
            && pfnDrawBorder == other.pfnDrawBorder
            && pfnFInterruptUnderline == other.pfnFInterruptUnderline
            && pfnFInterruptShade == other.pfnFInterruptShade
            && pfnFInterruptBorder == other.pfnFInterruptBorder
            && pfnShadeRectangle == other.pfnShadeRectangle
            && pfnDrawTextRun == other.pfnDrawTextRun
            && pfnDrawSplatLine == other.pfnDrawSplatLine
            && pfnFInterruptShaping == other.pfnFInterruptShaping
            && pfnGetGlyphs == other.pfnGetGlyphs
            && pfnGetGlyphPositions == other.pfnGetGlyphPositions
            && pfnDrawGlyphs == other.pfnDrawGlyphs
            && pfnReleaseGlyphBuffers == other.pfnReleaseGlyphBuffers
            && pfnGetGlyphExpansionInfo == other.pfnGetGlyphExpansionInfo
            && pfnGetGlyphExpansionInkInfo == other.pfnGetGlyphExpansionInkInfo
            && pfnGetGlyphRunInk == other.pfnGetGlyphRunInk
            && pfnGetEms == other.pfnGetEms
            && pfnPunctStartLine == other.pfnPunctStartLine
            && pfnModWidthOnRun == other.pfnModWidthOnRun
            && pfnModWidthSpace == other.pfnModWidthSpace
            && pfnCompOnRun == other.pfnCompOnRun
            && pfnCompWidthSpace == other.pfnCompWidthSpace
            && pfnExpOnRun == other.pfnExpOnRun
            && pfnExpWidthSpace == other.pfnExpWidthSpace
            && pfnGetModWidthClasses == other.pfnGetModWidthClasses
            && pfnGetBreakingClasses == other.pfnGetBreakingClasses
            && pfnFTruncateBefore == other.pfnFTruncateBefore
            && pfnCanBreakBeforeChar == other.pfnCanBreakBeforeChar
            && pfnCanBreakAfterChar == other.pfnCanBreakAfterChar
            && pfnFHangingPunct == other.pfnFHangingPunct
            && pfnGetSnapGrid == other.pfnGetSnapGrid
            && pfnDrawEffects == other.pfnDrawEffects
            && pfnFCancelHangingPunct == other.pfnFCancelHangingPunct
            && pfnModifyCompAtLastChar == other.pfnModifyCompAtLastChar
            && pfnGetDurMaxExpandRagged == other.pfnGetDurMaxExpandRagged
            && pfnGetCharExpansionInfoFullMixed == other.pfnGetCharExpansionInfoFullMixed
            && pfnGetGlyphExpansionInfoFullMixed == other.pfnGetGlyphExpansionInfoFullMixed
            && pfnGetCharCompressionInfoFullMixed == other.pfnGetCharCompressionInfoFullMixed
            && pfnGetGlyphCompressionInfoFullMixed == other.pfnGetGlyphCompressionInfoFullMixed
            && pfnGetCharAlignmentStartLine == other.pfnGetCharAlignmentStartLine
            && pfnGetCharAlignmentEndLine == other.pfnGetCharAlignmentEndLine
            && pfnGetGlyphAlignmentStartLine == other.pfnGetGlyphAlignmentStartLine
            && pfnGetGlyphAlignmentEndLine == other.pfnGetGlyphAlignmentEndLine
            && pfnGetPriorityForGoodTypography == other.pfnGetPriorityForGoodTypography
            && pfnEnumText == other.pfnEnumText
            && pfnEnumTab == other.pfnEnumTab
            && pfnEnumPen == other.pfnEnumPen
            && pfnGetObjectHandlerInfo == other.pfnGetObjectHandlerInfo
            && pfnAssertFailedPtr == other.pfnAssertFailedPtr
            && fDontReleaseRuns == other.fDontReleaseRuns;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is LsContextInfo other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(version, cInstalledHandlers, plsimethods, cEstimatedCharsPerLine, cJustPriorityLim, wchUndef, wchNull, wchSpace);
    }

    public static bool operator ==(LsContextInfo left, LsContextInfo right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(LsContextInfo left, LsContextInfo right)
    {
        return !left.Equals(right);
    }
}
