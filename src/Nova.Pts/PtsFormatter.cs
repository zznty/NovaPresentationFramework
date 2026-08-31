namespace Nova.Pts;

/// <summary>
/// The bottomless single-column page-formatting engine. Drives the WPF <c>PtsHost</c> callbacks
/// (via <see cref="FsContextInfo"/>) in the PTS protocol order: page dimensions → section
/// properties → main text segment → subtrack formatting → per-paragraph greedy
/// <c>FormatLine</c>. The main text segment is a subtrack; each nested container paragraph
/// (a Block such as Paragraph) is itself a subtrack formatted through the host's installed-object
/// callback (<c>ObjFormatParaBottomless</c>), so the plain path recurses exactly like native PTS.
/// Results are recorded into <see cref="PtsStore"/> for the query entry points.
/// </summary>
internal static class PtsFormatter
{
    private const int ContainerParagraphObjectId = 0;

    /// <summary>
    /// Formats a bottomless page starting at the given section (<c>FsCreatePageBottomless</c>).
    /// </summary>
    public static PtsErr FormatPage(PtsStore store, IntPtr sectionName, out FsFmtResult fmt, out IntPtr pageHandle)
    {
        fmt = FsFmtResult.GoalReached;
        pageHandle = IntPtr.Zero;

        FsPageState page = new()
        {
            Handle = IntPtr.Zero,
            SectionName = sectionName,
            Fswdir = 0,
            CColumns = 1,
            PageBodyRect = default,
            PageBodyBbox = new FsBbox(0, default),
            Tracks = []
        };
        pageHandle = store.RegisterPage(page);
        return FormatPageInto(store, sectionName, page, out fmt);
    }

    /// <summary>
    /// The shared body of <see cref="FormatPage"/> and <see cref="UpdatePage"/>: reads the
    /// section geometry, formats the main subtrack and fills the page state. The page handle
    /// is the caller's — a fresh one for a create, the preserved one for an update.
    /// </summary>
    private static PtsErr FormatPageInto(PtsStore store, IntPtr sectionName, FsPageState page, out FsFmtResult fmt)
    {
        fmt = FsFmtResult.GoalReached;

        FsContextInfo info = store.Info;
        IntPtr client = info.PfsClient;

        FsRect discardMargin = default;
        PtsErr err = info.GetPageDimensions(
            client, sectionName, out uint pageFswdir, out _, out _, out _, ref discardMargin);
        if (err != 0)
        {
            return err;
        }

        err = info.GetSectionProperties(
            client, sectionName, out int fNewPage, out uint fswdir, out int fApplyColumnBalancing,
            out int ccol, out int cSegmentDefinedColumnSpanAreas, out int cHeightDefinedColumnSpanAreas);
        if (err != 0)
        {
            return err;
        }

        if (cSegmentDefinedColumnSpanAreas != 0 || cHeightDefinedColumnSpanAreas != 0)
        {
            throw new PtsException("segment-defined / height-defined column-span areas");
        }

        err = info.GetMainTextSegment(client, sectionName, out IntPtr segment);
        if (err != 0)
        {
            return err;
        }

        // Column layout: single column for a bottomless page.
        FsColumnInfo[] columns = new FsColumnInfo[Math.Max(ccol, 1)];
        unsafe
        {
            fixed (FsColumnInfo* pColumns = columns)
            {
                err = info.GetSectionColumnInfo(client, sectionName, fswdir, columns.Length, pColumns, out int ccolActual);
                if (err != 0)
                {
                    return err;
                }
            }
        }

        int contentWidth = columns[0].DurWidth;
        page.SectionName = sectionName;
        page.Fswdir = fswdir;
        page.CColumns = columns.Length;
        page.PageBodyRect = new FsRect(0, 0, contentWidth, 0);
        page.PageBodyBbox = new FsBbox(0, default);
        page.Tracks.Clear();

        store.FormattingPage = page;
        try
        {
            PtsErr subtrackErr = FormatSubtrack(
                store, segment, 0, 0, fswdir, 0, contentWidth, 0,
                out fmt, out int dvrUsed, out FsBbox subtrackBbox);
            if (subtrackErr != PtsErr.None)
            {
                return subtrackErr;
            }

            // The main segment's subtrack is the page's single (simple-page) track.
            FsSubtrackState mainSubtrack = store.SubtrackHandle != IntPtr.Zero
                ? store.FindSubtrack(store.SubtrackHandle) ?? throw new PtsException("main subtrack missing")
                : throw new PtsException("main subtrack missing");
            FsTrackState track = new()
            {
                Handle = mainSubtrack.Handle,
                Nms = mainSubtrack.Nms,
                Fsrc = new FsRect(0, 0, contentWidth, dvrUsed),
                Bbox = subtrackBbox.FDefined != 0
                    ? subtrackBbox
                    : new FsBbox(1, new FsRect(0, 0, contentWidth, dvrUsed)),
                Paras = mainSubtrack.Paras
            };
            page.Tracks.Add(track);
            store.RegisterTrackAlias(track.Handle, track);
            page.PageBodyRect = track.Fsrc;
            page.PageBodyBbox = track.Bbox;
        }
        finally
        {
            store.FormattingPage = null;
        }

        fmt = FsFmtResult.GoalReached;
        return PtsErr.None;
    }

    /// <summary>
    /// Re-formats an existing bottomless page in place (<c>FsUpdateBottomlessPage</c>). The old
    /// client objects are destroyed and the page is re-formatted from the section.
    /// </summary>
    public static PtsErr UpdatePage(PtsStore store, IntPtr pageHandle, IntPtr sectionName, out FsFmtResult fmt)
    {
        // The update REUSES the caller's page handle: WPF keeps _ptsPage across
        // updates, so the previous destroy-then-create (returning a fresh handle
        // the caller never sees) left the next FsQueryPageDetails with a stale
        // handle and crashed with 'unknown page handle'. Only the page's CONTENT
        // is destroyed; the state object and its handle survive.
        fmt = FsFmtResult.GoalReached;
        FsPageState? page = store.FindPage(pageHandle);
        if (page is null)
        {
            return PtsErr.InvalidParameter;
        }

        foreach (FsTrackState track in page.Tracks)
        {
            DestroyParaList(store, track.Paras);
        }

        page.Tracks.Clear();
        return FormatPageInto(store, sectionName, page, out fmt);
    }

    /// <summary>
    /// Destroys a page and every client object it created (<c>FsDestroyPage</c>), including the
    /// nested subtracks of container paragraphs.
    /// </summary>
    public static void DestroyPage(PtsStore store, IntPtr pageHandle)
    {
        FsPageState? page = store.FindPage(pageHandle);
        if (page is null)
        {
            return;
        }

        foreach (FsTrackState track in page.Tracks)
        {
            DestroyParaList(store, track.Paras);
        }

        store.RemovePage(pageHandle);
    }

    private static void DestroyParaList(PtsStore store, List<FsParaState> paras)
    {
        FsContextInfo info = store.Info;
        IntPtr client = info.PfsClient;

        foreach (FsParaState para in paras)
        {
            foreach (FsLineState line in para.Lines)
            {
                if (line.Pfsline != IntPtr.Zero)
                {
                    _ = info.DestroyLine(client, line.Pfsline);
                }

                if (line.Pfsbreakreclineclient != IntPtr.Zero)
                {
                    _ = info.DestroyLineBreakRecord(client, line.Pfsbreakreclineclient);
                }
            }

            if (para.Pfsparaclient != IntPtr.Zero)
            {
                _ = info.DestroyParaclient(client, para.Pfsparaclient);
            }

            if (para.McsClientOut != IntPtr.Zero)
            {
                _ = info.DestroyMcsclient(client, para.McsClientOut);
            }

            // Container paragraphs carry their own subtrack with further paragraphs; destroy
            // those recursively so every para client / line created during formatting is released.
            if (para.SubtrackHandle != IntPtr.Zero)
            {
                FsSubtrackState? nested = store.FindSubtrack(para.SubtrackHandle);
                if (nested is not null)
                {
                    DestroyParaList(store, nested.Paras);
                    store.RemoveSubtrack(para.SubtrackHandle);
                }
            }
        }
    }

    /// <summary>
    /// Formats the paragraphs of a subtrack bottomless (<c>FsFormatSubtrackBottomless</c>).
    /// Text paragraphs are formatted greedily; nested container paragraphs (Blocks/ListItems)
    /// recurse through the host's installed-object callback, mirroring native PTS.
    /// </summary>
    public static PtsErr FormatSubtrack(
        PtsStore store,
        IntPtr segment,
        int iArea,
        int fSuppressTopSpace,
        uint fswdir,
        int ur,
        int dur,
        int vr,
        out FsFmtResult fmt,
        out int dvrUsed,
        out FsBbox bbox)
    {
        fmt = FsFmtResult.GoalReached;
        dvrUsed = 0;
        bbox = new FsBbox(0, default);

        if (iArea != 0)
        {
            throw new PtsException("column-span areas (iArea != 0)");
        }

        _ = fSuppressTopSpace;
        _ = vr;

        FsContextInfo info = store.Info;
        IntPtr client = info.PfsClient;

        FsSubtrackState subtrack = new()
        {
            Handle = IntPtr.Zero,
            Nms = segment,
            Fsrc = new FsRect(ur, vr, dur, 0),
            Bbox = new FsBbox(0, default),
            Paras = []
        };
        _ = store.RegisterSubtrack(subtrack);

        int totalDvr = 0;
        PtsErr err = info.GetFirstPara(client, segment, out int fSuccessful, out IntPtr nmp);
        if (err != 0)
        {
            return err;
        }

        while (fSuccessful != 0)
        {
            FsPap pap = default;
            err = info.GetParaProperties(client, nmp, ref pap);
            if (err != 0)
            {
                return err;
            }

            if (pap.Idobj == PtsStore.TextParagraphObjectId)
            {
                err = FormatTextParagraph(store, nmp, fswdir, ur, dur, out FsParaState para);
                if (err != 0)
                {
                    return err;
                }

                subtrack.Paras.Add(para);
                totalDvr += para.DvrUsed;
            }
            else if (pap.Idobj == ContainerParagraphObjectId)
            {
                // Nested container paragraph (a Block such as Paragraph): dispatch through the
                // installed-object method, which routes to the host's ContainerParagraph and back
                // into FormatSubtrack for the container's own content.
                err = FormatContainerParagraph(store, nmp, fswdir, ur, dur, out FsParaState containerPara);
                if (err != 0)
                {
                    return err;
                }

                subtrack.Paras.Add(containerPara);
                totalDvr += containerPara.DvrUsed;
            }
            else
            {
                throw new PtsException($"paragraph idobj {pap.Idobj} (tables, floaters, figures, UIElement paragraphs)");
            }

            err = info.GetNextPara(client, segment, nmp, out int fFound, out IntPtr nmpNext);
            if (err != 0)
            {
                return err;
            }

            if (fFound == 0)
            {
                break;
            }

            nmp = nmpNext;
        }

        dvrUsed = totalDvr;
        bbox = new FsBbox(1, new FsRect(0, 0, dur, totalDvr));
        subtrack.Fsrc = new FsRect(ur, vr, dur, totalDvr);
        subtrack.Bbox = bbox;
        store.SubtrackHandle = subtrack.Handle;
        return PtsErr.None;
    }

    private static PtsErr FormatContainerParagraph(PtsStore store, IntPtr nmp, uint fswdir, int ur, int dur, out FsParaState para)
    {
        para = null!;
        FsContextInfo info = store.Info;
        IntPtr client = info.PfsClient;

        PtsErr err = info.CreateParaclient(client, nmp, out IntPtr paraClient);
        if (err != 0)
        {
            return err;
        }

        FsIMethods subtrackMethods = store.SubtrackMethods;
        if (subtrackMethods.PfnFormatParaBottomless is null)
        {
            throw new PtsException("installed subtrack methods (CreateInstalledObjectsInfo was not called)");
        }

        try
        {
            PtsErr fserr = subtrackMethods.PfnFormatParaBottomless(
                IntPtr.Zero, paraClient, nmp, 0, IntPtr.Zero, 0, fswdir,
                ur, dur, 0, IntPtr.Zero, 0, 1,
                out FsFmtResult fmt, out IntPtr nestedSubtrack, out int dvrUsed, out FsBbox fsbbox,
                out IntPtr mcsOut, out int clearOut, out int topSpace, out int uninterruptible);
            if (fserr != PtsErr.None)
            {
                _ = info.DestroyParaclient(client, paraClient);
                return fserr;
            }

            para = new FsParaState
            {
                Nmp = nmp,
                Pfsparaclient = paraClient,
                Idobj = ContainerParagraphObjectId,
                Fswdir = fswdir,
                Lines = [],
                DvrUsed = dvrUsed,
                DvrTopSpace = topSpace,
                Bbox = fsbbox.FDefined != 0 ? fsbbox : new FsBbox(1, new FsRect(0, 0, dur, dvrUsed)),
                SubtrackHandle = nestedSubtrack,
                McsClientOut = mcsOut
            };
            return PtsErr.None;
        }
        catch
        {
            _ = info.DestroyParaclient(client, paraClient);
            throw;
        }
    }

    private static PtsErr FormatTextParagraph(PtsStore store, IntPtr nmp, uint fswdir, int ur, int dur, out FsParaState para)
    {
        para = null!;
        FsContextInfo info = store.Info;
        IntPtr client = info.PfsClient;

        PtsErr err = info.CreateParaclient(client, nmp, out IntPtr paraClient);
        if (err != 0)
        {
            return err;
        }

        FsTxtProps txtProps = default;
        err = info.GetTextProperties(client, nmp, 0, ref txtProps);
        if (err != 0)
        {
            return err;
        }

        uint paraFswdir = txtProps.Fswdir != 0 ? txtProps.Fswdir : fswdir;

        List<FsLineState> lines = [];
        int dcp = 0;
        int vr = 0;
        bool firstLine = true;
        try
        {
            while (true)
            {
                err = info.FormatLine(
                    client, paraClient, nmp, 0, dcp, IntPtr.Zero, paraFswdir,
                    ur, dur, ur, dur, 0,
                    0, 0, 0, firstLine ? 1 : 0, 0, 0,
                    out IntPtr lineHandle, out int dcpLine, out IntPtr breakRecOut, out int fForcedBroken,
                    out FsFlres flres, out int dvrAscent, out int dvrDescent, out int urBBox, out int durBBox,
                    out _, out _);
                if (err != 0)
                {
                    return err;
                }

                if (lineHandle == IntPtr.Zero || dcpLine <= 0)
                {
                    // End of paragraph: no line / no progress. Destroy a stray empty line.
                    if (lineHandle != IntPtr.Zero)
                    {
                        _ = info.DestroyLine(client, lineHandle);
                    }

                    if (breakRecOut != IntPtr.Zero)
                    {
                        _ = info.DestroyLineBreakRecord(client, breakRecOut);
                    }

                    break;
                }

                // End of paragraph: the final line reports fsflrEndOfParagraph (no break record,
                // since there is no following line to resume from). Calling FormatLine again would
                // step past the paragraph's last symbol.
                bool paragraphEnded = flres is FsFlres.EndOfParagraph
                    or FsFlres.EndOfParagraphClearLeft
                    or FsFlres.EndOfParagraphClearRight
                    or FsFlres.EndOfParagraphClearBoth;

                lines.Add(new FsLineState
                {
                    Pfsline = lineHandle,
                    Pfsbreakreclineclient = breakRecOut,
                    DcpFirst = dcp,
                    DcpLim = dcp + dcpLine,
                    UrStart = ur,
                    Dur = dur,
                    VrStart = vr,
                    DvrAscent = dvrAscent,
                    DvrDescent = dvrDescent,
                    UrBBox = urBBox,
                    DurBBox = durBBox,
                    FClearOnLeft = 0,
                    FClearOnRight = 0,
                    FTreatedAsFirst = firstLine ? 1 : 0,
                    FForceBroken = fForcedBroken,
                    Flres = flres
                });

                vr += dvrAscent + dvrDescent;
                dcp += dcpLine;
                firstLine = false;

                if (paragraphEnded || fForcedBroken != 0)
                {
                    break;
                }
            }
        }
        catch
        {
            foreach (FsLineState line in lines)
            {
                if (line.Pfsline != IntPtr.Zero)
                {
                    _ = info.DestroyLine(client, line.Pfsline);
                }

                if (line.Pfsbreakreclineclient != IntPtr.Zero)
                {
                    _ = info.DestroyLineBreakRecord(client, line.Pfsbreakreclineclient);
                }
            }

            _ = info.DestroyParaclient(client, paraClient);
            throw;
        }

        para = new FsParaState
        {
            Nmp = nmp,
            Pfsparaclient = paraClient,
            Idobj = PtsStore.TextParagraphObjectId,
            Fswdir = paraFswdir,
            Lines = lines,
            DvrUsed = vr,
            DvrTopSpace = 0,
            Bbox = new FsBbox(1, new FsRect(0, 0, dur, vr))
        };
        return PtsErr.None;
    }
}
