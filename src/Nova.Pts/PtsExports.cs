using JetBrains.Annotations;

namespace Nova.Pts;

/// <summary>
/// The exported PTS entry-point surface (mirrors <c>MS.Internal.PtsHost.PTS</c> in dotnet/wpf,
/// MIT). Implemented: the bottomless single-column core — context lifecycle, page/subtrack
/// formatting, the query readback set and flow-direction transforms. Everything else (finite
/// pages, subpages, tables, floaters/figures, footnotes, multi-column, optimal breaking) throws
/// <see cref="PtsException"/> via <see cref="NotImplemented(string)"/> — a loud honest boundary,
/// never a silently wrong layout.
/// </summary>
[PublicAPI]
public static class PtsExports
{
    private static readonly Dictionary<long, PtsStore> s_stores = [];
    private static readonly Dictionary<long, FsIMethods> s_installedObjects = [];
    private static long s_nextContextHandle = 0x100;
    private static long s_nextInstalledObjectsHandle = 0x2000;

    // ------------------------------------------------------------------
    // Context lifecycle
    // ------------------------------------------------------------------

    /// <summary>Registers the subtrack/subpage installed-object method tables (<c>CreateInstalledObjectsInfo</c>).</summary>
    public static int CreateInstalledObjectsInfo(
        ref FsIMethods fssubtrackparamethods,
        ref FsIMethods fssubpageparamethods,
        out IntPtr pInstalledObjects,
        out int cInstalledObjects)
    {
        // The subpage table is only used by floaters/subpages; the plain path never formats one,
        // so only the subtrack table is retained.
        _ = fssubpageparamethods;

        long handle = s_nextInstalledObjectsHandle;
        s_nextInstalledObjectsHandle++;
        s_installedObjects[handle] = fssubtrackparamethods;
        pInstalledObjects = new IntPtr(handle);
        cInstalledObjects = 2; // subtrack + subpage
        return (int)PtsErr.None;
    }

    /// <summary>Releases the installed-object tables (<c>DestroyInstalledObjectsInfo</c>).</summary>
    public static int DestroyInstalledObjectsInfo(IntPtr pInstalledObjects)
    {
        _ = s_installedObjects.Remove(pInstalledObjects.ToInt64());
        return (int)PtsErr.None;
    }

    /// <summary>Creates a PTS context (<c>CreateDocContext</c>).</summary>
    public static int CreateDocContext(ref FsContextInfo fscontextinfo, out IntPtr pfscontext)
    {
        PtsStore store = new()
        {
            Info = fscontextinfo,
            SubtrackMethods = fscontextinfo.PInstalledObjects != IntPtr.Zero
                && s_installedObjects.TryGetValue(fscontextinfo.PInstalledObjects.ToInt64(), out FsIMethods methods)
                    ? methods
                    : default
        };
        long handle = s_nextContextHandle;
        s_nextContextHandle++;
        pfscontext = new IntPtr(handle);
        s_stores[handle] = store;
        return (int)PtsErr.None;
    }

    /// <summary>Destroys a PTS context (<c>DestroyDocContext</c>).</summary>
    public static int DestroyDocContext(IntPtr pfscontext)
    {
        if (s_stores.Remove(pfscontext.ToInt64(), out PtsStore? store))
        {
            foreach (FsPageState page in store.PagesSnapshot())
            {
                PtsFormatter.DestroyPage(store, page.Handle);
            }
        }

        return (int)PtsErr.None;
    }

    /// <summary>Debug-flag setter (no-op on the managed path).</summary>
    public static int FsSetDebugFlags(int fdebug)
    {
        _ = fdebug;
        return (int)PtsErr.None;
    }

    // ------------------------------------------------------------------
    // Bottomless page formatting
    // ------------------------------------------------------------------

    /// <summary>Formats a bottomless page (<c>FsCreatePageBottomless</c>).</summary>
    public static int FsCreatePageBottomless(
        IntPtr pfscontext,
        IntPtr fsnmsect,
        out FsFmtResult pfsfmtrbl,
        out IntPtr ppfspage)
    {
        PtsStore store = GetStore(pfscontext);
        PtsErr err = PtsFormatter.FormatPage(store, fsnmsect, out pfsfmtrbl, out ppfspage);
        return (int)err;
    }

    /// <summary>Re-formats a bottomless page (<c>FsUpdateBottomlessPage</c>).</summary>
    public static int FsUpdateBottomlessPage(
        IntPtr pfscontext,
        IntPtr pfspage,
        IntPtr fsnmsect,
        out FsFmtResult pfsfmtrbl)
    {
        PtsStore store = GetStore(pfscontext);
        PtsErr err = PtsFormatter.UpdatePage(store, pfspage, fsnmsect, out pfsfmtrbl);
        return (int)err;
    }

    /// <summary>Clears update info in a page (all entries are New on the managed path).</summary>
    public static int FsClearUpdateInfoInPage(IntPtr pfscontext, IntPtr pfspage)
    {
        _ = GetStore(pfscontext);
        _ = pfspage;
        return (int)PtsErr.None;
    }

    /// <summary>Destroys a page and its client objects (<c>FsDestroyPage</c>).</summary>
    public static int FsDestroyPage(IntPtr pfscontext, IntPtr pfspage)
    {
        PtsStore store = GetStore(pfscontext);
        PtsFormatter.DestroyPage(store, pfspage);
        return (int)PtsErr.None;
    }

    /// <summary>Destroys a page break record (none are produced on the plain path).</summary>
    public static int FsDestroyPageBreakRecord(IntPtr pfscontext, IntPtr pfsbreakrec)
    {
        _ = GetStore(pfscontext);
        _ = pfsbreakrec;
        return (int)PtsErr.None;
    }

    /// <summary>Formats a subtrack bottomless (<c>FsFormatSubtrackBottomless</c>).</summary>
    public static int FsFormatSubtrackBottomless(
        IntPtr pfsContext,
        IntPtr fsnmSegment,
        int iArea,
        IntPtr pfsGeom,
        int fSuppressTopSpace,
        uint fswdir,
        int ur,
        int dur,
        int vr,
        IntPtr pfsMcsClientIn,
        int fsKClearIn,
        int fCanBeInterruptedIn,
        out FsFmtResult pfsfmtrbl,
        out IntPtr ppfsSubtrack,
        out int pdvrUsed,
        out FsBbox pfsBBox,
        out IntPtr ppfsMcsClientOut,
        out int pfsKClearOut,
        out int pTopSpace,
        out int pfCanBeInterruptedOut)
    {
        _ = pfsGeom;
        _ = pfsMcsClientIn;
        _ = fsKClearIn;
        _ = fCanBeInterruptedIn;
        PtsStore store = GetStore(pfsContext);
        PtsErr err = PtsFormatter.FormatSubtrack(
            store, fsnmSegment, iArea, fSuppressTopSpace, fswdir, ur, dur, vr,
            out pfsfmtrbl, out pdvrUsed, out pfsBBox);
        ppfsSubtrack = store.SubtrackHandle;
        ppfsMcsClientOut = IntPtr.Zero;
        pfsKClearOut = 0;
        pTopSpace = 0;
        pfCanBeInterruptedOut = 0;
        return (int)err;
    }

    /// <summary>Clears update info in a subtrack (all entries are New on the managed path).</summary>
    public static int FsClearUpdateInfoInSubtrack(IntPtr pfsContext, IntPtr pfsSubtrack)
    {
        _ = GetStore(pfsContext);
        _ = pfsSubtrack;
        return (int)PtsErr.None;
    }

    /// <summary>Destroys a subtrack's stored paragraphs (<c>FsDestroySubtrack</c>).</summary>
    public static int FsDestroySubtrack(IntPtr pfsContext, IntPtr pfsSubtrack)
    {
        _ = GetStore(pfsContext);
        _ = pfsSubtrack;
        return (int)PtsErr.None;
    }

    /// <summary>Destroys a subtrack break record (none are produced on the plain path).</summary>
    public static int FsDestroySubtrackBreakRecord(IntPtr pfsContext, IntPtr pfsbreakrec)
    {
        _ = GetStore(pfsContext);
        _ = pfsbreakrec;
        return (int)PtsErr.None;
    }

    // ------------------------------------------------------------------
    // Queries
    // ------------------------------------------------------------------

    /// <summary>Query page details (<c>FsQueryPageDetails</c>); simple single-track page.</summary>
    public static int FsQueryPageDetails(IntPtr pfsContext, IntPtr pPage, out FsPageDetails pPageDetails)
    {
        PtsStore store = GetStore(pfsContext);
        FsPageState page = store.FindPage(pPage) ?? throw new PtsException("unknown page handle");
        FsTrackState track = page.Tracks[0];
        pPageDetails = new FsPageDetails
        {
            Fskupd = FsKUpdate.New,
            FSimple = 1,
            Simple = new FsPageDetailsSimple
            {
                Trackdescr = new FsTrackDescription
                {
                    Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
                    Nms = page.SectionName,
                    Fsrc = track.Fsrc,
                    Fsbbox = track.Bbox,
                    FTrackRelativeToRect = 0,
                    Pfstrack = track.Handle
                }
            }
        };
        return (int)PtsErr.None;
    }

    /// <summary>Query the page's section list (<c>FsQueryPageSectionList</c>); single section.</summary>
    public static unsafe int FsQueryPageSectionList(
        IntPtr pfsContext,
        IntPtr pPage,
        int cArraySize,
        FsSectionDescription* rgSectionDescription,
        out int cActualSize)
    {
        PtsStore store = GetStore(pfsContext);
        FsPageState page = store.FindPage(pPage) ?? throw new PtsException("unknown page handle");
        cActualSize = 1;
        if (cArraySize >= 1 && rgSectionDescription is not null)
        {
            *rgSectionDescription = new FsSectionDescription
            {
                Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
                Nms = page.SectionName,
                Fsrc = page.PageBodyRect,
                Fsbbox = page.PageBodyBbox,
                FOtherSectionInside = 0,
                DvrUsedTop = 0,
                DvrUsedBottom = 0,
                Pfssection = page.SectionName
            };
        }

        return (int)PtsErr.None;
    }

    /// <summary>Query section details (<c>FsQuerySectionDetails</c>); page-notes variant.</summary>
    public static int FsQuerySectionDetails(IntPtr pfsContext, IntPtr pSection, out FsSectionDetails pSectionDetails)
    {
        PtsStore store = GetStore(pfsContext);
        FsPageState page = store.FindPageBySection(pSection) ?? throw new PtsException("unknown section handle");
        pSectionDetails = new FsSectionDetails
        {
            FFootnotesAsPagenotes = 0,
            WithPageNotes = new FsSectionDetailsWithPageNotes
            {
                Fswdir = page.Fswdir,
                FColumnBalancingApplied = 0,
                FsrcSectionBody = page.PageBodyRect,
                FsbboxSectionBody = page.PageBodyBbox,
                CBasicColumns = page.CColumns,
                CSegmentDefinedColumnSpanAreas = 0,
                CHeightDefinedColumnSpanAreas = 0,
                FsrcEndnote = default,
                FsbboxEndnote = default,
                CEndnoteColumns = 0,
                TrackdescrEndnoteSeparator = default
            }
        };
        return (int)PtsErr.None;
    }

    /// <summary>Query the section's basic column (track) list (<c>FsQuerySectionBasicColumnList</c>).</summary>
    public static unsafe int FsQuerySectionBasicColumnList(
        IntPtr pfsContext,
        IntPtr pSection,
        int cArraySize,
        FsTrackDescription* rgColumnDescription,
        out int cActualSize)
    {
        PtsStore store = GetStore(pfsContext);
        FsPageState page = store.FindPageBySection(pSection) ?? throw new PtsException("unknown section handle");
        cActualSize = page.Tracks.Count;
        for (int i = 0; i < Math.Min(cArraySize, page.Tracks.Count) && rgColumnDescription is not null; i++)
        {
            FsTrackState track = page.Tracks[i];
            rgColumnDescription[i] = new FsTrackDescription
            {
                Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
                Nms = page.SectionName,
                Fsrc = track.Fsrc,
                Fsbbox = track.Bbox,
                FTrackRelativeToRect = 0,
                Pfstrack = track.Handle
            };
        }

        return (int)PtsErr.None;
    }

    /// <summary>Query track details (<c>FsQueryTrackDetails</c>).</summary>
    public static int FsQueryTrackDetails(IntPtr pfsContext, IntPtr pTrack, out FsTrackDetails pTrackDetails)
    {
        PtsStore store = GetStore(pfsContext);
        FsTrackState track = store.FindTrack(pTrack) ?? throw new PtsException("unknown track handle");
        pTrackDetails = new FsTrackDetails { CParas = track.Paras.Count };
        return (int)PtsErr.None;
    }

    /// <summary>Query the track's paragraph list (<c>FsQueryTrackParaList</c>).</summary>
    public static unsafe int FsQueryTrackParaList(
        IntPtr pfsContext,
        IntPtr pTrack,
        int cParas,
        FsParaDescription* rgParaDesc,
        out int cParaDesc)
    {
        PtsStore store = GetStore(pfsContext);
        FsTrackState track = store.FindTrack(pTrack) ?? throw new PtsException("unknown track handle");
        cParaDesc = track.Paras.Count;
        for (int i = 0; i < Math.Min(cParas, track.Paras.Count) && rgParaDesc is not null; i++)
        {
            FsParaState para = track.Paras[i];
            // For a container paragraph the host's ContainerParaClient queries the subtrack
            // handle (FsQuerySubtrackDetails), so pfspara must be the subtrack handle, not nmp.
            IntPtr paraHandle = para.SubtrackHandle != IntPtr.Zero ? para.SubtrackHandle : para.Nmp;
            rgParaDesc[i] = new FsParaDescription
            {
                Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
                Pfspara = paraHandle,
                Pfsparaclient = para.Pfsparaclient,
                Nmp = para.Nmp,
                Idobj = para.Idobj,
                DvrUsed = para.DvrUsed,
                Fsbbox = para.Bbox,
                DvrTopSpace = para.DvrTopSpace
            };
        }

        return (int)PtsErr.None;
    }

    /// <summary>Query subtrack details (<c>FsQuerySubtrackDetails</c>).</summary>
    public static int FsQuerySubtrackDetails(IntPtr pfsContext, IntPtr pSubTrack, out FsSubtrackDetails pSubTrackDetails)
    {
        PtsStore store = GetStore(pfsContext);
        FsSubtrackState subtrack = store.FindSubtrack(pSubTrack) ?? throw new PtsException("unknown subtrack handle");
        pSubTrackDetails = new FsSubtrackDetails
        {
            Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
            Nms = subtrack.Nms,
            Fsrc = subtrack.Fsrc,
            CParas = subtrack.Paras.Count
        };
        return (int)PtsErr.None;
    }

    /// <summary>Query the subtrack's paragraph list (<c>FsQuerySubtrackParaList</c>).</summary>
    public static unsafe int FsQuerySubtrackParaList(
        IntPtr pfsContext,
        IntPtr pSubTrack,
        int cParas,
        FsParaDescription* rgParaDesc,
        out int cParaDesc)
    {
        PtsStore store = GetStore(pfsContext);
        FsSubtrackState subtrack = store.FindSubtrack(pSubTrack) ?? throw new PtsException("unknown subtrack handle");
        cParaDesc = subtrack.Paras.Count;
        for (int i = 0; i < Math.Min(cParas, subtrack.Paras.Count) && rgParaDesc is not null; i++)
        {
            FsParaState para = subtrack.Paras[i];
            IntPtr paraHandle = para.SubtrackHandle != IntPtr.Zero ? para.SubtrackHandle : para.Nmp;
            rgParaDesc[i] = new FsParaDescription
            {
                Fsupdinf = new FsUpdateInfo { Fskupd = FsKUpdate.New, DvrShifted = 0 },
                Pfspara = paraHandle,
                Pfsparaclient = para.Pfsparaclient,
                Nmp = para.Nmp,
                Idobj = para.Idobj,
                DvrUsed = para.DvrUsed,
                Fsbbox = para.Bbox,
                DvrTopSpace = para.DvrTopSpace
            };
        }

        return (int)PtsErr.None;
    }

    /// <summary>Query text paragraph details (<c>FsQueryTextDetails</c>); full variant.</summary>
    public static int FsQueryTextDetails(IntPtr pfsContext, IntPtr pPara, out FsTextDetails pTextDetails)
    {
        PtsStore store = GetStore(pfsContext);
        FsParaState para = store.FindParaByNmp(pPara) ?? throw new PtsException("unknown paragraph handle");
        int cLines = para.Lines.Count;
        pTextDetails = new FsTextDetails
        {
            Fsktd = FsKTextDetails.Full,
            Full = new FsTextDetailsFull
            {
                Fswdir = para.Fswdir,
                Fsklines = FsKTextLines.Normal,
                FLinesComposite = 0,
                CLines = cLines,
                CAttachedObjects = 0,
                DcpFirst = cLines > 0 ? para.Lines[0].DcpFirst : 0,
                DcpLim = cLines > 0 ? para.Lines[^1].DcpLim : 0,
                FDropCapPresent = 0,
                FsupdinfDropCap = default,
                FSuppressTopLineSpacing = 0,
                FUpdateInfoForLinesPresent = 0,
                CLinesBeforeChange = 0,
                DvrShiftBeforeChange = 0,
                CLinesChanged = cLines,
                DcLinesChanged = 0,
                DvrShiftAfterChange = 0,
                DdcpAfterChange = 0
            }
        };
        return (int)PtsErr.None;
    }

    /// <summary>Query a text paragraph's simple line list (<c>FsQueryLineListSingle</c>).</summary>
    public static unsafe int FsQueryLineListSingle(
        IntPtr pfsContext,
        IntPtr pPara,
        int cLines,
        FsLineDescriptionSingle* rgLineDesc,
        out int cLineDesc)
    {
        PtsStore store = GetStore(pfsContext);
        FsParaState para = store.FindParaByNmp(pPara) ?? throw new PtsException("unknown paragraph handle");
        cLineDesc = para.Lines.Count;
        for (int i = 0; i < Math.Min(cLines, para.Lines.Count) && rgLineDesc is not null; i++)
        {
            FsLineState line = para.Lines[i];
            rgLineDesc[i] = new FsLineDescriptionSingle
            {
                Pfslineclient = line.Pfsline,
                Pfsbreakreclineclient = line.Pfsbreakreclineclient,
                DcpFirst = line.DcpFirst,
                DcpLim = line.DcpLim,
                UrStart = line.UrStart,
                Dur = line.Dur,
                FAllowHyphenation = 0,
                UrBBox = line.UrBBox,
                DurBBox = line.DurBBox,
                VrStart = line.VrStart,
                DvrAscent = line.DvrAscent,
                DvrDescent = line.DvrDescent,
                FClearOnLeft = line.FClearOnLeft,
                FClearOnRight = line.FClearOnRight,
                FTreatedAsFirst = line.FTreatedAsFirst,
                FForceBroken = line.FForceBroken
            };
        }

        return (int)PtsErr.None;
    }

    // ------------------------------------------------------------------
    // Transforms
    // ------------------------------------------------------------------

    /// <summary>Transforms a rectangle between flow directions (<c>FsTransformRectangle</c>).</summary>
    public static int FsTransformRectangle(
        uint fswdirIn,
        ref FsRect rectPage,
        ref FsRect rectTransform,
        uint fswdirOut,
        out FsRect rectOut)
    {
        rectOut = TransformRect(fswdirIn, rectPage, rectTransform, fswdirOut);
        return (int)PtsErr.None;
    }

    /// <summary>Transforms a bounding box between flow directions (<c>FsTransformBbox</c>).</summary>
    public static int FsTransformBbox(
        uint fswdirIn,
        ref FsRect rectPage,
        ref FsBbox bboxTransform,
        uint fswdirOut,
        out FsBbox bboxOut)
    {
        FsBbox result = bboxTransform;
        result.Fsrc = TransformRect(fswdirIn, rectPage, bboxTransform.Fsrc, fswdirOut);
        bboxOut = result;
        return (int)PtsErr.None;
    }

    /// <summary>Throws an honest <see cref="PtsException"/> for a feature the managed PTS does not
    /// implement. Returns <see cref="PtsErr.None"/> only to satisfy callers' int-return shape.</summary>
    public static int NotImplemented(string feature)
    {
        throw new PtsException(feature);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static FsRect TransformRect(uint fswdirIn, FsRect page, FsRect rect, uint fswdirOut)
    {
        if (fswdirIn == fswdirOut)
        {
            return rect;
        }

        // ES (0, LTR) <-> WS (4, RTL), EN (1) <-> WN (5): mirror horizontally about the page.
        return IsHorizontalMirrorPair(fswdirIn, fswdirOut)
            ? new FsRect(page.DU - rect.U - rect.DU, rect.V, rect.DU, rect.DV)
            : throw new PtsException($"flow-direction transform {fswdirIn} -> {fswdirOut} (rotated writing modes)");
    }

    private static bool IsHorizontalMirrorPair(uint a, uint b)
    {
        return (a == 0 && b == 4) || (a == 4 && b == 0)
            || (a == 1 && b == 5) || (a == 5 && b == 1);
    }

    private static PtsStore GetStore(IntPtr pfscontext)
    {
        return s_stores.TryGetValue(pfscontext.ToInt64(), out PtsStore? store)
            ? store
            : throw new PtsException("unknown PTS context handle");
    }
}
