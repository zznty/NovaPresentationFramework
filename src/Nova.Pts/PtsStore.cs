namespace Nova.Pts;

/// <summary>
/// One formatted line of a text paragraph.
/// </summary>
internal sealed class FsLineState
{
    public required IntPtr Pfsline;
    public required IntPtr Pfsbreakreclineclient;
    public required int DcpFirst;
    public required int DcpLim;
    public required int UrStart;
    public required int Dur;
    public required int VrStart;
    public required int DvrAscent;
    public required int DvrDescent;
    public required int UrBBox;
    public required int DurBBox;
    public required int FClearOnLeft;
    public required int FClearOnRight;
    public required int FTreatedAsFirst;
    public required int FForceBroken;
    public required FsFlres Flres;
}

/// <summary>
/// One formatted paragraph of a track.
/// </summary>
internal sealed class FsParaState
{
    public required IntPtr Nmp;
    public required IntPtr Pfsparaclient;
    public required int Idobj;
    public required uint Fswdir;
    public required List<FsLineState> Lines;
    public required int DvrUsed;
    public required int DvrTopSpace;
    public required FsBbox Bbox;

    /// <summary>For a container paragraph: the subtrack handle holding its own paragraphs
    /// (must be cleaned up recursively when the page is destroyed).</summary>
    public IntPtr SubtrackHandle;

    /// <summary>Margin collapsing state returned by the host's subtrack formatting (owned by PTS).</summary>
    public IntPtr McsClientOut;
}

/// <summary>
/// One formatted track (column) of a page.
/// </summary>
internal sealed class FsTrackState
{
    public required IntPtr Handle;
    public required IntPtr Nms;
    public required FsRect Fsrc;
    public required FsBbox Bbox;
    public required List<FsParaState> Paras;
}

/// <summary>
/// One formatted page (bottomless single-column).
/// </summary>
internal sealed class FsPageState
{
    public required IntPtr Handle;
    public required IntPtr SectionName;
    public required uint Fswdir;
    public required int CColumns;
    public required FsRect PageBodyRect;
    public required FsBbox PageBodyBbox;
    public required List<FsTrackState> Tracks;
}

/// <summary>
/// One formatted subtrack (the container paragraph's formatting result).
/// </summary>
internal sealed class FsSubtrackState
{
    public required IntPtr Handle;
    public required IntPtr Nms;
    public required FsRect Fsrc;
    public required FsBbox Bbox;
    public required List<FsParaState> Paras;
}

/// <summary>
/// Per-PTS-context state: the client callback table plus every formatted page, track and
/// paragraph. The context handle returned by <see cref="PtsExports.CreateDocContext"/> keys the
/// store; page/track/section handles are monotonic IntPtr ids allocated here (never aliased with
/// client handles, which the host registers in its own handle mapper).
/// </summary>
internal sealed class PtsStore
{
    private const long FirstHandle = 0x1000;
    private const int TextObjectId = -1;

    private readonly Dictionary<long, FsPageState> _pages = [];
    private readonly Dictionary<long, FsTrackState> _tracks = [];
    private readonly Dictionary<long, FsSubtrackState> _subtracks = [];
    private long _nextHandle = FirstHandle;

    /// <summary>The client callback table for this context.</summary>
    public required FsContextInfo Info { get; init; }

    /// <summary>Installed subtrack methods (from <see cref="PtsExports.CreateInstalledObjectsInfo"/>).</summary>
    public FsIMethods SubtrackMethods { get; set; }

    /// <summary>The page currently being formatted (set during <c>FsCreatePageBottomless</c> so the
    /// subtrack round-trip through the host's installed-object callback records into it).</summary>
    internal FsPageState? FormattingPage { get; set; }

    /// <summary>The subtrack handle returned by the most recent <c>FsFormatSubtrackBottomless</c>
    /// (queried by the host's ContainerParaClient).</summary>
    internal IntPtr SubtrackHandle { get; set; }

    /// <summary>Object id reported by the host for text paragraphs.</summary>
    public static int TextParagraphObjectId => TextObjectId;

    private long AllocateHandle()
    {
        long handle = _nextHandle;
        _nextHandle++;
        return handle;
    }

    /// <summary>Allocates a fresh page handle and registers the page state.</summary>
    public IntPtr RegisterPage(FsPageState page)
    {
        long handle = AllocateHandle();
        page.Handle = new IntPtr(handle);
        _pages[handle] = page;
        return page.Handle;
    }

    /// <summary>Registers a track state under a fresh handle.</summary>
    public IntPtr RegisterTrack(FsTrackState track)
    {
        long handle = AllocateHandle();
        track.Handle = new IntPtr(handle);
        _tracks[handle] = track;
        return track.Handle;
    }

    /// <summary>Aliases an existing handle (the main subtrack handle) as a track state.</summary>
    public void RegisterTrackAlias(IntPtr handle, FsTrackState track)
    {
        _tracks[handle.ToInt64()] = track;
    }

    /// <summary>Registers a subtrack state under a fresh handle.</summary>
    public IntPtr RegisterSubtrack(FsSubtrackState subtrack)
    {
        long handle = AllocateHandle();
        subtrack.Handle = new IntPtr(handle);
        _subtracks[handle] = subtrack;
        return subtrack.Handle;
    }

    /// <summary>Looks up a page by handle, or returns null.</summary>
    public FsPageState? FindPage(IntPtr handle)
    {
        return _pages.TryGetValue(handle.ToInt64(), out FsPageState? page) ? page : null;
    }

    /// <summary>Looks up a track by handle, or returns null.</summary>
    public FsTrackState? FindTrack(IntPtr handle)
    {
        return _tracks.TryGetValue(handle.ToInt64(), out FsTrackState? track) ? track : null;
    }

    /// <summary>Looks up a subtrack by handle, or returns null.</summary>
    public FsSubtrackState? FindSubtrack(IntPtr handle)
    {
        return _subtracks.TryGetValue(handle.ToInt64(), out FsSubtrackState? subtrack) ? subtrack : null;
    }

    /// <summary>Removes a page and its tracks (without destroying client objects).</summary>
    public void RemovePage(IntPtr handle)
    {
        if (_pages.TryGetValue(handle.ToInt64(), out FsPageState? page))
        {
            foreach (FsTrackState track in page.Tracks)
            {
                _ = _tracks.Remove(track.Handle.ToInt64());
            }

            _ = _pages.Remove(handle.ToInt64());
        }
    }

    /// <summary>Removes a subtrack from the store.</summary>
    public void RemoveSubtrack(IntPtr handle)
    {
        _ = _subtracks.Remove(handle.ToInt64());
    }

    /// <summary>Finds the paragraph state for a paragraph name handle within a page's tracks.</summary>
    public FsParaState? FindPara(IntPtr pageHandle, IntPtr nmp)
    {
        FsPageState? page = FindPage(pageHandle);
        if (page is null)
        {
            return null;
        }

        foreach (FsTrackState track in page.Tracks)
        {
            foreach (FsParaState para in track.Paras)
            {
                if (para.Nmp == nmp)
                {
                    return para;
                }
            }
        }

        return null;
    }

    /// <summary>Finds a paragraph state by its name handle across every page/track.</summary>
    public FsParaState? FindParaByNmp(IntPtr nmp)
    {
        foreach (FsPageState page in _pages.Values)
        {
            foreach (FsTrackState track in page.Tracks)
            {
                FsParaState? found = FindParaInList(track.Paras, nmp);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private FsParaState? FindParaInList(List<FsParaState> paras, IntPtr nmp)
    {
        foreach (FsParaState para in paras)
        {
            if (para.Nmp == nmp)
            {
                return para;
            }

            // Container paragraphs hold their own paragraphs in a nested subtrack.
            if (para.SubtrackHandle != IntPtr.Zero)
            {
                FsSubtrackState? nested = FindSubtrack(para.SubtrackHandle);
                if (nested is not null)
                {
                    FsParaState? found = FindParaInList(nested.Paras, nmp);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Finds a paragraph state by nmp inside a specific subtrack.</summary>
    public FsParaState? FindParaInSubtrack(IntPtr subtrackHandle, IntPtr nmp)
    {
        FsSubtrackState? subtrack = FindSubtrack(subtrackHandle);
        return subtrack is null ? null : FindParaInList(subtrack.Paras, nmp);
    }

    /// <summary>Finds the page whose section name matches.</summary>
    public FsPageState? FindPageBySection(IntPtr sectionName)
    {
        foreach (FsPageState page in _pages.Values)
        {
            if (page.SectionName == sectionName)
            {
                return page;
            }
        }

        return null;
    }

    /// <summary>Snapshot of every registered page (for context teardown).</summary>
    public List<FsPageState> PagesSnapshot()
    {
        return [.. _pages.Values];
    }
}
