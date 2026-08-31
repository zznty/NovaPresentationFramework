using JetBrains.Annotations;

namespace Nova.Pts;

/// <summary>
/// PTS client callbacks (<c>PTS.FSCBKGEN</c> / <c>PTS.FSCBKTXT</c> subset). These delegate
/// signatures mirror the WPF nest delegates in <c>MS.Internal.PtsHost.PTS</c> (dotnet/wpf, MIT)
/// field-for-field; the WPF nest's PtsHost implements them in managed code and Nova.Pts drives
/// them. Every callback returns an <see cref="PtsErr"/>; the values mirror the WPF nest fserr codes.
/// </summary>
[PublicAPI]
public static class PtsDelegates
{
    /// <summary>Page dimensions of the section's page.</summary>
    public delegate PtsErr GetPageDimensions(
        IntPtr pfsclient,
        IntPtr nms,
        out uint fswdir,
        out int fHeaderFooterAtTopBottom,
        out int durPage,
        out int dvrPage,
        ref FsRect fsrcMargin);

    /// <summary>Enumerates sections on the page (nmsCur == IntPtr.Zero asks for the first).</summary>
    public delegate PtsErr GetNextSection(
        IntPtr pfsclient,
        IntPtr nmsCur,
        out int fSuccess,
        out IntPtr nmsNext);

    /// <summary>Properties of a section.</summary>
    public delegate PtsErr GetSectionProperties(
        IntPtr pfsclient,
        IntPtr nms,
        out int fNewPage,
        out uint fswdir,
        out int fApplyColumnBalancing,
        out int ccol,
        out int cSegmentDefinedColumnSpanAreas,
        out int cHeightDefinedColumnSpanAreas);

    /// <summary>Column layout of a section's main text segment.</summary>
    public unsafe delegate PtsErr GetSectionColumnInfo(
        IntPtr pfsclient,
        IntPtr nms,
        uint fswdir,
        int ncol,
        FsColumnInfo* fscolinfo,
        out int ccol);

    /// <summary>Main text segment of a section.</summary>
    public delegate PtsErr GetMainTextSegment(
        IntPtr pfsclient,
        IntPtr nmsSection,
        out IntPtr nmSegment);

    /// <summary>First paragraph in a segment.</summary>
    public delegate PtsErr GetFirstPara(
        IntPtr pfsclient,
        IntPtr nms,
        out int fSuccessful,
        out IntPtr nmp);

    /// <summary>Next paragraph in a segment.</summary>
    public delegate PtsErr GetNextPara(
        IntPtr pfsclient,
        IntPtr nms,
        IntPtr nmpCur,
        out int fFound,
        out IntPtr nmpNext);

    /// <summary>Properties of a paragraph.</summary>
    public delegate PtsErr GetParaProperties(
        IntPtr pfsclient,
        IntPtr nmp,
        ref FsPap fspap);

    /// <summary>Creates the client object for a paragraph.</summary>
    public delegate PtsErr CreateParaclient(
        IntPtr pfsclient,
        IntPtr nmp,
        out IntPtr pfsparaclient);

    /// <summary>Transfers display info from an old to a new paragraph client.</summary>
    public delegate PtsErr TransferDisplayInfo(
        IntPtr pfsclient,
        IntPtr pfsparaclientOld,
        IntPtr pfsparaclientNew);

    /// <summary>Destroys the client object for a paragraph.</summary>
    public delegate PtsErr DestroyParaclient(
        IntPtr pfsclient,
        IntPtr pfsparaclient);

    /// <summary>Text properties of a text paragraph.</summary>
    public delegate PtsErr GetTextProperties(
        IntPtr pfsclient,
        IntPtr nmp,
        int iArea,
        ref FsTxtProps fstxtprops);

    /// <summary>Formats one greedy line of a text paragraph.</summary>
    public delegate PtsErr FormatLine(
        IntPtr pfsclient,
        IntPtr pfsparaclient,
        IntPtr nmp,
        int iArea,
        int dcp,
        IntPtr pbrlineIn,
        uint fswdir,
        int urStartLine,
        int durLine,
        int urStartTrack,
        int durTrack,
        int urPageLeftMargin,
        int fAllowHyphenation,
        int fClearOnLeft,
        int fClearOnRight,
        int fTreatAsFirstInPara,
        int fTreatAsLastInPara,
        int fSuppressTopSpace,
        out IntPtr pfsline,
        out int dcpLine,
        out IntPtr ppbrlineOut,
        out int fForcedBroken,
        out FsFlres fsflres,
        out int dvrAscent,
        out int dvrDescent,
        out int urBBox,
        out int durBBox,
        out int dcpDepend,
        out int fReformatNeighborsAsLastLine);

    /// <summary>Formats one forced (single) line of a text paragraph.</summary>
    public delegate PtsErr FormatLineForced(
        IntPtr pfsclient,
        IntPtr pfsparaclient,
        IntPtr nmp,
        int iArea,
        int dcp,
        IntPtr pbrlineIn,
        uint fswdir,
        int urStartLine,
        int durLine,
        int urStartTrack,
        int durTrack,
        int urPageLeftMargin,
        int fClearOnLeft,
        int fClearOnRight,
        int fTreatAsFirstInPara,
        int fTreatAsLastInPara,
        int fSuppressTopSpace,
        int dvrAvailable,
        out IntPtr pfsline,
        out int dcpLine,
        out IntPtr ppbrlineOut,
        out FsFlres fsflres,
        out int dvrAscent,
        out int dvrDescent,
        out int urBBox,
        out int durBBox,
        out int dcpDepend);

    /// <summary>Destroys a line created by <see cref="FormatLine"/>.</summary>
    public delegate PtsErr DestroyLine(
        IntPtr pfsclient,
        IntPtr pfsline);

    /// <summary>Destroys a line break record returned by <see cref="FormatLine"/>.</summary>
    public delegate PtsErr DestroyLineBreakRecord(
        IntPtr pfsclient,
        IntPtr pbrlineIn);

    /// <summary>Destroys a margin collapsing state returned by <see cref="ObjFormatParaBottomless"/>.</summary>
    public delegate PtsErr DestroyMcsclient(
        IntPtr pfsclient,
        IntPtr pmcsclient);

    /// <summary>Number of footnote references in a text range.</summary>
    public delegate PtsErr GetNumberFootnotes(
        IntPtr pfsclient,
        IntPtr nmp,
        int fsdcpStart,
        int fsdcpLim,
        out int nFootnote);

    /// <summary>Footnote references in a text range (not used on the plain path).</summary>
    public unsafe delegate PtsErr GetFootnotes(
        IntPtr pfsclient,
        IntPtr nmp,
        int fsdcpStart,
        int fsdcpLim,
        int nFootnotes,
        IntPtr* rgnmftn,
        int* rgdcp,
        out int cFootnotes);

    /// <summary>Whether text formatting should be interrupted at the given position.</summary>
    public delegate PtsErr FInterruptFormattingText(
        IntPtr pfsclient,
        IntPtr pfsparaclient,
        IntPtr nmp,
        int dcp,
        int vr,
        out int fInterruptFormatting);

    /// <summary>Empty space suppressible at the bottom of a line.</summary>
    public delegate PtsErr GetDvrSuppressibleBottomSpace(
        IntPtr pfsclient,
        IntPtr pfsparaclient,
        IntPtr pfsline,
        uint fswdir,
        out int dvrSuppressible);

    /// <summary>Advance amount in tight wrap at the given position.</summary>
    public delegate PtsErr GetDvrAdvance(
        IntPtr pfsclient,
        IntPtr pfsparaclient,
        IntPtr nmp,
        int dcp,
        uint fswdir,
        out int dvr);

    /// <summary>Vertical alignment / justification properties for the page.</summary>
    public unsafe delegate PtsErr GetJustificationProperties(
        IntPtr pfsclient,
        IntPtr* rgnms,
        int cnms,
        int fLastSectionNotBroken,
        out int fJustify,
        out int fskal,
        out int fCancelAtLastColumn);

    /// <summary>Header segment of a section (not used on the plain path).</summary>
    public delegate PtsErr GetHeaderSegment(
        IntPtr pfsclient,
        IntPtr nms,
        IntPtr pfsbrpagePrelim,
        uint fswdir,
        out int fHeaderPresent,
        out int fHardMargin,
        out int dvrMaxHeight,
        out int dvrFromEdge,
        out uint fswdirHeader,
        out IntPtr nmsHeader);

    /// <summary>Footer segment of a section (not used on the plain path).</summary>
    public delegate PtsErr GetFooterSegment(
        IntPtr pfsclient,
        IntPtr nms,
        IntPtr pfsbrpagePrelim,
        uint fswdir,
        out int fFooterPresent,
        out int fHardMargin,
        out int dvrMaxHeight,
        out int dvrFromEdge,
        out uint fswdirFooter,
        out IntPtr nmsFooter);

    /// <summary>Segment-defined column-span areas of a section (single column: none).</summary>
    public unsafe delegate PtsErr GetSegmentDefinedColumnSpanAreaInfo(
        IntPtr pfsclient,
        IntPtr nms,
        int cAreas,
        IntPtr* rgnmSeg,
        int* rgcColumns,
        out int cAreasActual);

    /// <summary>Height-defined column-span areas of a section (single column: none).</summary>
    public unsafe delegate PtsErr GetHeightDefinedColumnSpanAreaInfo(
        IntPtr pfsclient,
        IntPtr nms,
        int cAreas,
        int* rgdvrAreaHeight,
        int* rgcColumns,
        out int cAreasActual);

    /// <summary>Whether the segment content changed (incremental update; not used on the plain path).</summary>
    public delegate PtsErr UpdGetSegmentChange(
        IntPtr pfsclient,
        IntPtr nms,
        out int fskch);

    /// <summary>Formats an installed-object paragraph (subtrack/subpage) bottomless.</summary>
    public delegate PtsErr ObjFormatParaBottomless(
        IntPtr pfssobjc,
        IntPtr pfsparaclient,
        IntPtr nmp,
        int iArea,
        IntPtr pfsgeom,
        int fSuppressTopSpace,
        uint fswdir,
        int urTrack,
        int durTrack,
        int vrTrack,
        IntPtr pmcsclientIn,
        int fskclearIn,
        int fInterruptable,
        out FsFmtResult fsfmtrbl,
        out IntPtr pfspara,
        out int dvrUsed,
        out FsBbox fsbbox,
        out IntPtr pmcsclientOut,
        out int fskclearOut,
        out int dvrTopSpace,
        out int fPageBecomesUninterruptable);
}
