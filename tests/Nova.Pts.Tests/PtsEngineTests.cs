using System.Reflection;
using System.Runtime.InteropServices;

namespace Nova.Pts.Tests;

/// <summary>
/// Isolated tests for the bottomless single-column PTS engine: a synthetic <see cref="FsContextInfo"/>
/// callback table (no WPF) drives <see cref="PtsExports"/> through the exact protocol the WPF
/// PtsHost uses — page dimensions, section properties, main text segment, installed-object
/// subtrack formatting, greedy FormatLine per paragraph — and the query entry points read the
/// results back. Also pins the struct layouts against the WPF nest ABI where the two are visible
/// to this test (the nest structs live in PresentationFramework, referenced only by the layout
/// test class).
/// </summary>
public sealed class PtsEngineTests
{
    private const int ContainerParagraphId = 0;
    private const int TextParagraphId = -1;

    private static readonly IntPtr SectionHandle = new(0x10);
    private static readonly IntPtr SegmentHandle = new(0x20);
    private static readonly IntPtr ParaHandle = new(0x30);

    private sealed class Host
    {
        private int _formatLineCall;

        private static readonly int[] LineLengths = [6, 5];

        internal int DestroyLineCalls;
        internal int DestroyParaClientCalls;
        internal int DestroyLineBreakRecordCalls;

        internal unsafe FsContextInfo BuildContextInfo()
        {
            return new FsContextInfo
            {
                Version = 0,
                Fsffi = 0x40, // fsffiUseTextQuickLoop
                DrMinColumnBalancingStep = 960,
                CInstalledObjects = 2,
                PInstalledObjects = new IntPtr(0x5000),
                PfsClient = new IntPtr(0x1234),
                PtsPenaltyModule = IntPtr.Zero,
                GetPageDimensions = GetPageDimensions,
                GetNextSection = (client, nmsCur, out fSuccess, out nmsNext) =>
                {
                    fSuccess = 0; // single section: the page engine starts from the passed section
                    nmsNext = IntPtr.Zero;
                    return PtsErr.None;
                },
                GetSectionProperties = GetSectionProperties,
                GetSectionColumnInfo = GetSectionColumnInfo,
                GetMainTextSegment = (client, nmsSection, out nmSegment) =>
                {
                    nmSegment = SegmentHandle;
                    return PtsErr.None;
                },
                GetFirstPara = (client, nms, out fSuccessful, out nmp) =>
                {
                    fSuccessful = 1;
                    nmp = ParaHandle;
                    return PtsErr.None;
                },
                GetNextPara = (client, nms, nmpCur, out fFound, out nmpNext) =>
                {
                    fFound = 0;
                    nmpNext = IntPtr.Zero;
                    return PtsErr.None;
                },
                GetParaProperties = GetParaProperties,
                CreateParaclient = (client, nmp, out pfsparaclient) =>
                {
                    pfsparaclient = nmp == SegmentHandle ? new IntPtr(0x40) : new IntPtr(0x41);
                    return PtsErr.None;
                },
                DestroyParaclient = (client, pfsparaclient) =>
                {
                    DestroyParaClientCalls++;
                    return PtsErr.None;
                },
                GetTextProperties = GetTextProperties,
                FormatLine = FormatLine,
                DestroyLine = (client, pfsline) =>
                {
                    DestroyLineCalls++;
                    return PtsErr.None;
                },
                DestroyLineBreakRecord = (client, pbrlineIn) =>
                {
                    DestroyLineBreakRecordCalls++;
                    return PtsErr.None;
                },
                GetNumberFootnotes = (client, nmp, fsdcpStart, fsdcpLim, out nFootnote) =>
                {
                    nFootnote = 0;
                    return PtsErr.None;
                },
                GetFootnotes = (client, nmp, fsdcpStart, fsdcpLim, nFootnotes, rgnmftn, rgdcp, out cFootnotes) =>
                {
                    cFootnotes = 0;
                    return PtsErr.None;
                },
                FInterruptFormattingText = (client, pfsparaclient, nmp, dcp, vr, out fInterruptFormatting) =>
                {
                    fInterruptFormatting = 0;
                    return PtsErr.None;
                },
                GetDvrSuppressibleBottomSpace = (client, pfsparaclient, pfsline, fswdir, out dvrSuppressible) =>
                {
                    dvrSuppressible = 0;
                    return PtsErr.None;
                },
                GetDvrAdvance = (client, pfsparaclient, nmp, dcp, fswdir, out dvr) =>
                {
                    dvr = 0;
                    return PtsErr.None;
                },
                GetJustificationProperties = (client, rgnms, cnms, fLastSectionNotBroken, out fJustify, out fskal, out fCancelAtLastColumn) =>
                {
                    fJustify = 0;
                    fskal = 0;
                    fCancelAtLastColumn = 0;
                    return PtsErr.None;
                },
                GetHeaderSegment = (client, nms, pfsbrpagePrelim, fswdir, out fHeaderPresent, out fHardMargin, out dvrMaxHeight, out dvrFromEdge, out fswdirHeader, out nmsHeader) =>
                {
                    fHeaderPresent = 0;
                    fHardMargin = 0;
                    dvrMaxHeight = 0;
                    dvrFromEdge = 0;
                    fswdirHeader = 0;
                    nmsHeader = IntPtr.Zero;
                    return PtsErr.None;
                },
                GetFooterSegment = (client, nms, pfsbrpagePrelim, fswdir, out fFooterPresent, out fHardMargin, out dvrMaxHeight, out dvrFromEdge, out fswdirFooter, out nmsFooter) =>
                {
                    fFooterPresent = 0;
                    fHardMargin = 0;
                    dvrMaxHeight = 0;
                    dvrFromEdge = 0;
                    fswdirFooter = 0;
                    nmsFooter = IntPtr.Zero;
                    return PtsErr.None;
                },
                GetSegmentDefinedColumnSpanAreaInfo = (client, nms, cAreas, rgnmSeg, rgcColumns, out cAreasActual) =>
                {
                    cAreasActual = 0;
                    return PtsErr.None;
                },
                GetHeightDefinedColumnSpanAreaInfo = (client, nms, cAreas, rgdvrAreaHeight, rgcColumns, out cAreasActual) =>
                {
                    cAreasActual = 0;
                    return PtsErr.None;
                },
                UpdGetSegmentChange = (client, nms, out fskch) =>
                {
                    fskch = 0;
                    return PtsErr.None;
                }
            };
        }

        private static PtsErr GetPageDimensions(
            IntPtr client, IntPtr nms, out uint fswdir, out int fHeaderFooterAtTopBottom,
            out int durPage, out int dvrPage, ref FsRect fsrcMargin)
        {
            fswdir = 0; // ES (LTR)
            fHeaderFooterAtTopBottom = 0;
            durPage = 1000;
            dvrPage = 1000;
            fsrcMargin = new FsRect(50, 50, 900, 900);
            return PtsErr.None;
        }

        private static PtsErr GetSectionProperties(
            IntPtr client, IntPtr nms, out int fNewPage, out uint fswdir, out int fApplyColumnBalancing,
            out int ccol, out int cSegmentDefinedColumnSpanAreas, out int cHeightDefinedColumnSpanAreas)
        {
            fNewPage = 0;
            fswdir = 0;
            fApplyColumnBalancing = 0;
            ccol = 1;
            cSegmentDefinedColumnSpanAreas = 0;
            cHeightDefinedColumnSpanAreas = 0;
            return PtsErr.None;
        }

        private static unsafe PtsErr GetSectionColumnInfo(
            IntPtr client, IntPtr nms, uint fswdir, int ncol, FsColumnInfo* fscolinfo, out int ccol)
        {
            ccol = ncol;
            for (int i = 0; i < ncol && fscolinfo is not null; i++)
            {
                fscolinfo[i] = new FsColumnInfo { DurBefore = 0, DurWidth = 900 };
            }

            return PtsErr.None;
        }

        private static PtsErr GetParaProperties(IntPtr client, IntPtr nmp, ref FsPap fspap)
        {
            fspap.Idobj = nmp == SegmentHandle ? ContainerParagraphId : TextParagraphId;
            fspap.FKeepWithNext = 0;
            fspap.FBreakPageBefore = 0;
            fspap.FBreakColumnBefore = 0;
            return PtsErr.None;
        }

        private static PtsErr GetTextProperties(IntPtr client, IntPtr nmp, int iArea, ref FsTxtProps fstxtprops)
        {
            fstxtprops.Fswdir = 0;
            fstxtprops.DcpStartContent = 0;
            fstxtprops.FKeepTogether = 0;
            fstxtprops.FDropCap = 0;
            fstxtprops.CMinLinesAfterBreak = 0;
            fstxtprops.CMinLinesBeforeBreak = 0;
            fstxtprops.FVerticalGrid = 0;
            fstxtprops.FOptimizeParagraph = 1;
            fstxtprops.FAvoidHyphenationAtTrackBottom = 0;
            fstxtprops.FAvoidHyphenationOnLastChainElement = 0;
            fstxtprops.CMaxConsecutiveHyphens = int.MaxValue;
            return PtsErr.None;
        }

        private PtsErr FormatLine(
            IntPtr client, IntPtr pfsparaclient, IntPtr nmp, int iArea, int dcp, IntPtr pbrlineIn,
            uint fswdir, int urStartLine, int durLine, int urStartTrack, int durTrack, int urPageLeftMargin,
            int fAllowHyphenation, int fClearOnLeft, int fClearOnRight, int fTreatAsFirstInPara,
            int fTreatAsLastInPara, int fSuppressTopSpace,
            out IntPtr pfsline, out int dcpLine, out IntPtr ppbrlineOut, out int fForcedBroken,
            out FsFlres fsflres, out int dvrAscent, out int dvrDescent, out int urBBox, out int durBBox,
            out int dcpDepend, out int fReformatNeighborsAsLastLine)
        {
            int lineIndex = _formatLineCall;
            if (lineIndex >= LineLengths.Length)
            {
                // End of paragraph: no more text.
                pfsline = IntPtr.Zero;
                dcpLine = 0;
                ppbrlineOut = IntPtr.Zero;
                fForcedBroken = 0;
                fsflres = FsFlres.EndOfParagraph;
                dvrAscent = 0;
                dvrDescent = 0;
                urBBox = 0;
                durBBox = 0;
                dcpDepend = 0;
                fReformatNeighborsAsLastLine = 0;
                return PtsErr.None;
            }

            _formatLineCall++;
            pfsline = new IntPtr(0x100 + lineIndex);
            dcpLine = LineLengths[lineIndex];
            ppbrlineOut = lineIndex == LineLengths.Length - 1 ? new IntPtr(0x200 + lineIndex) : IntPtr.Zero;
            fForcedBroken = lineIndex == LineLengths.Length - 1 ? 1 : 0;
            fsflres = FsFlres.SoftBreak;
            dvrAscent = 100;
            dvrDescent = 20;
            urBBox = 0;
            durBBox = 880;
            dcpDepend = 0;
            fReformatNeighborsAsLastLine = 0;
            return PtsErr.None;
        }
    }

    /// <summary>Creates a context + installed objects; returns the context handle and host.</summary>
    private static (IntPtr Context, Host Host) CreateContext()
    {
        Host host = new();
        IntPtr contextHandle = IntPtr.Zero;
        FsIMethods subtrack = new()
        {
            PfnFormatParaBottomless = (pfssobjc, pfsparaclient, nmp, iArea, pfsgeom, fSuppressTopSpace, fswdir,
                urTrack, durTrack, vrTrack, pmcsclientIn, fskclearIn, fInterruptable,
                out fsfmtrbl, out pfspara, out dvrUsed, out fsbbox,
                out pmcsclientOut, out fskclearOut, out dvrTopSpace, out fPageBecomesUninterruptable) =>
            {
                // Mimics ContainerParagraph.FormatParaBottomless: feed the subtrack rect into
                // FsFormatSubtrackBottomless and surface the aggregated result.
                int fserr = PtsExports.FsFormatSubtrackBottomless(
                    contextHandle, nmp, iArea, IntPtr.Zero, fSuppressTopSpace, fswdir, urTrack, durTrack, vrTrack,
                    IntPtr.Zero, 0, 1,
                    out fsfmtrbl, out pfspara, out dvrUsed, out fsbbox,
                    out pmcsclientOut, out fskclearOut, out dvrTopSpace, out fPageBecomesUninterruptable);
                return fserr != 0
                    ? throw new InvalidOperationException($"subtrack format failed: {fserr}")
                    : PtsErr.None;
            }
        };
        FsIMethods subpage = default;

        int rc = PtsExports.CreateInstalledObjectsInfo(ref subtrack, ref subpage, out IntPtr installedObjects, out int count);
        Assert.Equal(0, rc);
        Assert.Equal(2, count);

        FsContextInfo info = host.BuildContextInfo();
        info.PInstalledObjects = installedObjects;
        rc = PtsExports.CreateDocContext(ref info, out IntPtr context);
        Assert.Equal(0, rc);
        Assert.NotEqual(IntPtr.Zero, context);
        contextHandle = context;
        return (context, host);
    }

    [Fact]
    public void FormatPage_ProducesSingleTrack_WithOneParagraphAndTwoLines()
    {
        (IntPtr context, Host host) = CreateContext();
        try
        {
            int rc = PtsExports.FsCreatePageBottomless(context, SectionHandle, out FsFmtResult fmt, out IntPtr page);
            Assert.Equal(0, rc);
            Assert.Equal(FsFmtResult.GoalReached, fmt);
            Assert.NotEqual(IntPtr.Zero, page);

            // Page details: simple page, one track 900 wide, content height 2*120.
            rc = PtsExports.FsQueryPageDetails(context, page, out FsPageDetails pageDetails);
            Assert.Equal(0, rc);
            Assert.Equal(FsKUpdate.New, pageDetails.Fskupd);
            Assert.Equal(1, pageDetails.FSimple);
            Assert.Equal(900, pageDetails.Simple.Trackdescr.Fsrc.DU);
            Assert.Equal(240, pageDetails.Simple.Trackdescr.Fsrc.DV);
            Assert.Equal(1, pageDetails.Simple.Trackdescr.Fsbbox.FDefined);
            IntPtr track = pageDetails.Simple.Trackdescr.Pfstrack;
            Assert.NotEqual(IntPtr.Zero, track);

            // Track details: exactly one paragraph.
            rc = PtsExports.FsQueryTrackDetails(context, track, out FsTrackDetails trackDetails);
            Assert.Equal(0, rc);
            Assert.Equal(1, trackDetails.CParas);

            // Paragraph list.
            unsafe
            {
                FsParaDescription para = default;
                rc = PtsExports.FsQueryTrackParaList(context, track, 1, &para, out int cParaDesc);
                Assert.Equal(0, rc);
                Assert.Equal(1, cParaDesc);
                Assert.Equal(ParaHandle, para.Nmp);
                Assert.Equal(ParaHandle, para.Pfspara);
                Assert.Equal(new IntPtr(0x41), para.Pfsparaclient);
                Assert.Equal(TextParagraphId, para.Idobj);
                Assert.Equal(240, para.DvrUsed);
                Assert.Equal(1, para.Fsbbox.FDefined);
                Assert.Equal(900, para.Fsbbox.Fsrc.DU);
            }

            // Text details: full, 2 lines, dcp 0..11.
            rc = PtsExports.FsQueryTextDetails(context, ParaHandle, out FsTextDetails textDetails);
            Assert.Equal(0, rc);
            Assert.Equal(FsKTextDetails.Full, textDetails.Fsktd);
            Assert.Equal(FsKTextLines.Normal, textDetails.Full.Fsklines);
            Assert.Equal(0, textDetails.Full.FLinesComposite);
            Assert.Equal(2, textDetails.Full.CLines);
            Assert.Equal(0, textDetails.Full.CAttachedObjects);
            Assert.Equal(0, textDetails.Full.DcpFirst);
            Assert.Equal(11, textDetails.Full.DcpLim);
            Assert.Equal(2, textDetails.Full.CLinesChanged);

            // Line list: two lines with correct dcp ranges and vertical stacking.
            unsafe
            {
                FsLineDescriptionSingle[] lines = new FsLineDescriptionSingle[2];
                fixed (FsLineDescriptionSingle* pLines = lines)
                {
                    rc = PtsExports.FsQueryLineListSingle(context, ParaHandle, 2, pLines, out int cLineDesc);
                    Assert.Equal(0, rc);
                    Assert.Equal(2, cLineDesc);
                }

                Assert.Equal(0, lines[0].DcpFirst);
                Assert.Equal(6, lines[0].DcpLim);
                Assert.Equal(0, lines[0].VrStart);
                Assert.Equal(100, lines[0].DvrAscent);
                Assert.Equal(20, lines[0].DvrDescent);
                Assert.Equal(1, lines[0].FTreatedAsFirst);

                Assert.Equal(6, lines[1].DcpFirst);
                Assert.Equal(11, lines[1].DcpLim);
                Assert.Equal(120, lines[1].VrStart);
                Assert.Equal(0, lines[1].FTreatedAsFirst);
                Assert.Equal(1, lines[1].FForceBroken);
                Assert.NotEqual(IntPtr.Zero, lines[1].Pfsbreakreclineclient);
            }

            // Section queries.
            int cSections;
            unsafe
            {
                rc = PtsExports.FsQueryPageSectionList(context, page, 1, null, out cSections);
            }

            Assert.Equal(0, rc);
            Assert.Equal(1, cSections);
            rc = PtsExports.FsQuerySectionDetails(context, SectionHandle, out FsSectionDetails sectionDetails);
            Assert.Equal(0, rc);
            Assert.Equal(0, sectionDetails.FFootnotesAsPagenotes);
            Assert.Equal(1, sectionDetails.WithPageNotes.CBasicColumns);
            int cColumns;
            unsafe
            {
                rc = PtsExports.FsQuerySectionBasicColumnList(context, SectionHandle, 1, null, out cColumns);
            }

            Assert.Equal(0, rc);
            Assert.Equal(1, cColumns);
        }
        finally
        {
            _ = PtsExports.DestroyDocContext(context);
        }
    }

    [Fact]
    public void DestroyPage_DestroysCreatedClientObjects()
    {
        (IntPtr context, Host host) = CreateContext();
        try
        {
            int rc = PtsExports.FsCreatePageBottomless(context, SectionHandle, out _, out IntPtr page);
            Assert.Equal(0, rc);

            // 2 lines + 1 text para client (the main segment is now a subtrack and gets no
            // separate client; only the text paragraph's client is created).
            Assert.Equal(0, host.DestroyLineCalls);
            Assert.Equal(0, host.DestroyParaClientCalls);
            Assert.Equal(0, host.DestroyLineBreakRecordCalls);

            rc = PtsExports.FsDestroyPage(context, page);
            Assert.Equal(0, rc);

            Assert.Equal(2, host.DestroyLineCalls);
            Assert.Equal(1, host.DestroyParaClientCalls);
            Assert.Equal(1, host.DestroyLineBreakRecordCalls);
        }
        finally
        {
            _ = PtsExports.DestroyDocContext(context);
        }
    }

    [Fact]
    public void UpdatePage_ReusesHandle_QueryOnOriginalHandleSucceeds()
    {
        // Regression: FsUpdateBottomlessPage destroyed the page and returned a fresh
        // handle the caller never sees (WPF keeps _ptsPage), so the next
        // FsQueryPageDetails threw 'unknown page handle' — the FlowDocument text-page
        // crash. The update must reuse the caller's handle.
        (IntPtr context, _) = CreateContext();
        try
        {
            int rc = PtsExports.FsCreatePageBottomless(context, SectionHandle, out _, out IntPtr page);
            Assert.Equal(0, rc);
            Assert.NotEqual(IntPtr.Zero, page);

            rc = PtsExports.FsUpdateBottomlessPage(context, page, SectionHandle, out FsFmtResult fmt);
            Assert.Equal(0, rc);
            Assert.Equal(FsFmtResult.GoalReached, fmt);

            // The ORIGINAL handle must remain valid after the update.
            rc = PtsExports.FsQueryPageDetails(context, page, out FsPageDetails details);
            Assert.Equal(0, rc);
            Assert.Equal(1, details.FSimple);
            Assert.NotEqual(IntPtr.Zero, details.Simple.Trackdescr.Pfstrack);
        }
        finally
        {
            _ = PtsExports.DestroyDocContext(context);
        }
    }

    [Fact]
    public void TransformRectangle_MirrorsHorizontally_ForRtl()
    {
        FsRect page = new(0, 0, 1000, 800);
        FsRect rect = new(100, 200, 300, 40);

        int rc = PtsExports.FsTransformRectangle(0, ref page, ref rect, 0, out FsRect same);
        Assert.Equal(0, rc);
        Assert.Equal(rect, same);

        rc = PtsExports.FsTransformRectangle(0, ref page, ref rect, 4, out FsRect mirrored);
        Assert.Equal(0, rc);
        Assert.Equal(600, mirrored.U); // 1000 - 100 - 300
        Assert.Equal(200, mirrored.V);
        Assert.Equal(300, mirrored.DU);
        Assert.Equal(40, mirrored.DV);
    }

    [Fact]
    public void NotImplemented_ThrowsPtsException()
    {
        PtsException ex = Assert.Throws<PtsException>(() => PtsExports.NotImplemented("FsCreatePageFinite"));
        Assert.Contains("FsCreatePageFinite", ex.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Struct layout pins vs the WPF nest ABI (PresentationFramework, internal types via
    // reflection). The Pts.cs wrapper reinterprets the pure-data structs with Unsafe.As, so the
    // field-for-field layout contract is load-bearing: a mismatch would corrupt the callbacks.

    [Fact]
    public void StructLayouts_MatchWpfNest()
    {
        // The csproj references the runtime PresentationCore/PresentationFramework implementations
        // (HintPath), so the loaded FrameworkElement assembly is executable, not the ref assembly.
        Type pts = typeof(System.Windows.FrameworkElement).Assembly
            .GetType("MS.Internal.PtsHost.UnsafeNativeMethods.PTS", throwOnError: true)!;
        Type fsrc = pts.GetNestedType("FSRECT", BindingFlags.NonPublic)!;
        Type fsPoint = pts.GetNestedType("FSPOINT", BindingFlags.NonPublic)!;
        Type fsVector = pts.GetNestedType("FSVECTOR", BindingFlags.NonPublic)!;
        Type fsBbox = pts.GetNestedType("FSBBOX", BindingFlags.NonPublic)!;
        Type fsPap = pts.GetNestedType("FSPAP", BindingFlags.NonPublic)!;
        Type fsTxtProps = pts.GetNestedType("FSTXTPROPS", BindingFlags.NonPublic)!;
        Type fsColumnInfo = pts.GetNestedType("FSCOLUMNINFO", BindingFlags.NonPublic)!;
        Type fsUpdateInfo = pts.GetNestedType("FSUPDATEINFO", BindingFlags.NonPublic)!;
        Type fsFlres = pts.GetNestedType("FSFLRES", BindingFlags.NonPublic)!;
        Type fsFmt = pts.GetNestedType("FSFMTRBL", BindingFlags.NonPublic)!;

        Assert.Equal(Marshal.SizeOf(fsrc), Marshal.SizeOf<FsRect>());
        Assert.Equal(Marshal.SizeOf(fsPoint), Marshal.SizeOf<FsPoint>());
        Assert.Equal(Marshal.SizeOf(fsVector), Marshal.SizeOf<FsVector>());
        Assert.Equal(Marshal.SizeOf(fsBbox), Marshal.SizeOf<FsBbox>());
        Assert.Equal(Marshal.SizeOf(fsPap), Marshal.SizeOf<FsPap>());
        Assert.Equal(Marshal.SizeOf(fsTxtProps), Marshal.SizeOf<FsTxtProps>());
        Assert.Equal(Marshal.SizeOf(fsColumnInfo), Marshal.SizeOf<FsColumnInfo>());
        Assert.Equal(Marshal.SizeOf(fsUpdateInfo), Marshal.SizeOf<FsUpdateInfo>());

        // Enums: compare underlying-type size.
        Assert.Equal(Marshal.SizeOf(Enum.GetUnderlyingType(fsFlres)), Marshal.SizeOf(Enum.GetUnderlyingType(typeof(FsFlres))));
        Assert.Equal(Marshal.SizeOf(Enum.GetUnderlyingType(fsFmt)), Marshal.SizeOf(Enum.GetUnderlyingType(typeof(FsFmtResult))));

        // Field offsets for the commonly-marshaled rect.
        Assert.Equal(Marshal.OffsetOf(fsrc, "u"), Marshal.OffsetOf<FsRect>("U"));
        Assert.Equal(Marshal.OffsetOf(fsrc, "dv"), Marshal.OffsetOf<FsRect>("DV"));
        Assert.Equal(Marshal.OffsetOf(fsBbox, "fDefined"), Marshal.OffsetOf<FsBbox>("FDefined"));
    }
}
