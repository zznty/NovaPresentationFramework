using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Nova.Classification;

namespace Nova.LineServices;

/// <summary>
/// Isolated managed Line Services (<c>Lo*</c>) v1 surface.
/// <para>
/// The exported method signatures, blittable struct layouts and callback order mirror the WPF
/// nest (<c>MS.Internal.TextFormatting.UnsafeNativeMethods</c> in dotnet/wpf, MIT). The line
/// formatter is an independent managed implementation whose callback-driving order follows the
/// public wine-mono <c>loline.c</c> behavior reference (MIT; read only, never copied). It is
/// <b>not</b> Microsoft Line Services: no glyph shaping, no object handlers; the optimal
/// (Knuth-Plass) break engine lives in <see cref="KnuthPlass"/> behind LoCreateBreaks.
/// </para>
/// </summary>
[PublicAPI]
public static class LoExports
{
    /// <summary>COM S_OK. Line Services success is <see cref="LsErr.None"/> (0), which is the same value.</summary>
    public const int SOk = 0;

    /// <summary>COM E_NOTIMPL, returned by the DWrite analysis-source helper stub.</summary>
    public const int ENotImplemented = unchecked((int)0x80004001);

    // TextStore.ObjectId values (WPF nest): Reverse = 0, InlineObject = 1, Text_chp = 0xffff.
    // FetchRunRedefined reports the fetched run's object id in LsChp.idObj; the engine uses it
    // to distinguish zero-cp bidi markers and inline objects from ordinary text.
    private const ushort IdObjReverse = 0;
    private const ushort IdObjInlineObject = 1;
    private const ushort IdObjTextChp = 0xffff;

    // CharacterAttributeFlag bit values from Nova.Classification (internal enum; values are part
    // of the WPF CharacterAttribute contract, mirrored here so the engine can test the public
    // CharacterAttributeRow.Flags without widening Classification's public surface).
    private const ushort FlagIdeo = 0x20; // CharacterIdeo

    // Handles are real GCHandle pointers: each context / line / penalty module is allocated as a
    // GCHandle and round-tripped through GCHandle.FromIntPtr(...).Target instead of a dictionary
    // lookup. GCHandle.Alloc/Free/FromIntPtr are thread-safe (the runtime handle table), and the
    // handle value is stable for the object's lifetime, so the wire ABI (blittable nint) is
    // unchanged. FromIntPtr throws ArgumentException for zero/freed/foreign handles, which the
    // helpers translate into the same Invalid*/failure results the old dictionary returned.
    //
    // One inherent difference from the old monotonic-id dictionary: the runtime reuses freed
    // GCHandle slots, so a stale (already-released) handle value can later alias a newly
    // allocated object of the same kind. WPF's line/context lifecycle (create, use, dispose)
    // never re-passes a stale handle, so this is accepted; it is also why the tests serialize
    // on one xunit collection (a concurrent allocation would make freed-handle rejection
    // non-deterministic).
    private static nint AllocHandle<T>(T target) where T : class
    {
        return GCHandle.ToIntPtr(GCHandle.Alloc(target));
    }

    private static bool TryGetHandle<T>(nint handle, [NotNullWhen(true)] out T? target) where T : class
    {
        target = null;
        if (handle == 0)
        {
            return false;
        }

        try
        {
            return GCHandle.FromIntPtr(handle).Target is T typed && (target = typed) != null;
        }
        catch (ArgumentException)
        {
            // Zero or already-freed handle.
            return false;
        }
    }

    private static void FreeHandle(nint handle)
    {
        GCHandle.FromIntPtr(handle).Free();
    }

    private static IntPtr s_szParaSeparator;
    private static IntPtr s_szLineSeparator;
    private static IntPtr s_szHidden;
    private static IntPtr s_szNbsp;
    private static IntPtr s_szObjectTerminator;
    private static IntPtr s_szObjectReplacement;

    /// <summary>
    /// Create a Line Services context. Copies <paramref name="contextInfo"/> and
    /// <paramref name="lscbkRedef"/> into process-owned state (keeping the delegates alive) and
    /// returns an opaque context handle in <paramref name="ploc"/>. Like the native API, the
    /// <c>pols</c> field of the caller's <paramref name="contextInfo"/> is set to the new handle.
    /// </summary>
    public static LsErr LoCreateContext(ref LsContextInfo contextInfo, ref LscbkRedefined lscbkRedef, out IntPtr ploc)
    {
        ploc = IntPtr.Zero;

        if (contextInfo.pfnFetchPap == null
            || contextInfo.pfnFetchLineProps == null
            || contextInfo.pfnGetRunCharWidths == null
            || contextInfo.pfnGetRunTextMetrics == null
            || lscbkRedef.pfnFetchRunRedefined == null)
        {
            return LsErr.InvalidParameter;
        }

        LoContext context = new(contextInfo, lscbkRedef);
        nint handle = AllocHandle(context);
        context.Info.pols = handle;
        contextInfo.pols = handle;
        ploc = handle;
        return LsErr.None;
    }

    /// <summary>
    /// Destroy a context. Returns <see cref="LsErr.ContextInUse"/> while any line created from the
    /// context is still alive.
    /// </summary>
    public static LsErr LoDestroyContext(IntPtr ploc)
    {
        if (!TryGetHandle(ploc, out LoContext? context))
        {
            return LsErr.InvalidContext;
        }

        if (context.LiveLines != 0)
        {
            return LsErr.ContextInUse;
        }

        FreeHandle(ploc);
        return LsErr.None;
    }

    /// <summary>
    /// Set the document device resolution. v1 validates the context and otherwise records nothing:
    /// the callbacks carry the device configuration. No-op success.
    /// </summary>
    public static LsErr LoSetDoc(IntPtr ploc, int isDisplay, int isReferencePresentationEqual, ref LsDevRes deviceInfo)
    {
        _ = isDisplay;
        _ = isReferencePresentationEqual;
        _ = deviceInfo;
        return TryGetHandle<LoContext>(ploc, out _) ? LsErr.None : LsErr.InvalidContext;
    }

    /// <summary>Set the line-breaking strategy. v1 validates the context; no-op success.</summary>
    public static LsErr LoSetBreaking(IntPtr ploc, int strategy)
    {
        _ = strategy;
        return TryGetHandle<LoContext>(ploc, out _) ? LsErr.None : LsErr.InvalidContext;
    }

    /// <summary>Set tab stops. v1 validates the context and does not consume tab runs; no-op success.</summary>
    public static unsafe LsErr LoSetTabs(IntPtr ploc, int durIncrementalTab, int tabCount, LsTbd* pTabs)
    {
        _ = durIncrementalTab;
        _ = tabCount;
        _ = pTabs;
        return TryGetHandle<LoContext>(ploc, out _) ? LsErr.None : LsErr.InvalidContext;
    }

    /// <summary>
    /// Fill <paramref name="escStringInfo"/> with six process-lifetime, NUL-terminated WCHAR
    /// escape strings: para separator U+2029, line separator U+2028, hidden U+FFFF, non-breaking
    /// space U+00A0, object terminator U+0009, object replacement U+FFFC.
    /// <para>
    /// Pins match the WPF nest <c>FillLinuxEscString</c>. wine-mono <c>loservice.c</c> uses
    /// U+FFFB for the object terminator; no public <c>Ls.h</c> confirms either value. This
    /// library stays on the nest pins so a future nest swap does not change TextStore.
    /// </para>
    /// </summary>
    public static void LoGetEscString(ref EscStringInfo escStringInfo)
    {
        escStringInfo.szParaSeparator = PinEscChar(ref s_szParaSeparator, '\u2029');
        escStringInfo.szLineSeparator = PinEscChar(ref s_szLineSeparator, '\u2028');
        escStringInfo.szHidden = PinEscChar(ref s_szHidden, '\uFFFF');
        escStringInfo.szNbsp = PinEscChar(ref s_szNbsp, '\u00A0');
        escStringInfo.szObjectTerminator = PinEscChar(ref s_szObjectTerminator, '\u0009');
        escStringInfo.szObjectReplacement = PinEscChar(ref s_szObjectReplacement, '\uFFFC');
    }

    private static unsafe nint PinEscChar(ref nint existing, char value)
    {
        if (existing != 0)
        {
            return existing;
        }

        nint block = (nint)NativeMemory.Alloc(sizeof(char) * 2);
        char* p = (char*)block;
        p[0] = value;
        p[1] = '\0';
        existing = block;
        return block;
    }

    /// <summary>
    /// Format one line of text starting at character position <paramref name="cp"/> into a column
    /// <paramref name="durColumn"/> wide. Drives <c>FetchPap</c>, <c>FetchLineProps</c>,
    /// <c>FetchRunRedefined</c> and <c>GetRunCharWidths</c> callbacks (order follows the public
    /// wine-mono <c>loline.c</c> behavior) and wraps at whitespace when the paragraph requests
    /// breaking rules and the run exceeds the column. Returns an opaque line handle in
    /// <paramref name="pploline"/>, line info in <paramref name="plslinfo"/>, and line widths in
    /// <paramref name="lineWidths"/>.
    /// <para>
    /// v1 ignores <paramref name="ccpLim"/>, <paramref name="dwLineFlags"/> and
    /// <paramref name="pInputBreakRec"/> (no break-record resume). Auto-numbering paragraphs
    /// (<c>fFmiAnm</c>) return <see cref="LsErr.NotImplemented"/> because
    /// <c>GetAutoNumberInfo</c> is not installed.
    /// </para>
    /// </summary>
    public static LsErr LoCreateLine(
        IntPtr ploc,
        int cp,
        int ccpLim,
        int durColumn,
        uint dwLineFlags,
        IntPtr pInputBreakRec,
        out LsLInfo plslinfo,
        out IntPtr pploline,
        out int maxDepth,
        out LsLineWidths lineWidths)
    {
        plslinfo = default;
        pploline = IntPtr.Zero;
        maxDepth = 0;
        lineWidths = default;

        _ = ccpLim;
        _ = dwLineFlags;
        _ = pInputBreakRec;

        if (!TryGetHandle(ploc, out LoContext? context))
        {
            return LsErr.InvalidContext;
        }

        LoLine line = new(context);
        LsErr err = FormatLine(ploc, context, line, cp, durColumn, ref plslinfo, ref lineWidths);
        if (err != LsErr.None)
        {
            return err;
        }

        line.EndCp = plslinfo.cpLimToContinue;
        _ = Interlocked.Increment(ref context.LiveLines);
        pploline = AllocHandle(line);
        maxDepth = 1;
        return LsErr.None;
    }

    /// <summary>Dispose a formatted line and release its state.</summary>
    public static LsErr LoDisposeLine(IntPtr ploline, bool finalizing)
    {
        _ = finalizing;
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        FreeHandle(ploline);
        _ = Interlocked.Decrement(ref line.Context.LiveLines);
        return LsErr.None;
    }

    /// <summary>
    /// Display a formatted line by driving the context's <c>DrawTextRun</c> callback for every
    /// run, in layout order, accumulating run origins from the line reference origin
    /// <paramref name="pt"/> (right-to-left flow starts at the right edge and moves left, which
    /// the WPF callback expects: it subtracts <c>dupRun</c> from the passed origin for
    /// <c>lstflowWS</c>). Each run's text and advance pointers come from the engine's line
    /// buffer, heights from <c>GetRunTextMetrics</c>. A context with no draw callback installed
    /// (measure-only) is a no-op success.
    /// </summary>
    public static unsafe LsErr LoDisplayLine(IntPtr ploline, ref LSPOINT pt, uint displayMode, ref LSRECT clipRect)
    {
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        LoContext context = line.Context;
        DrawTextRun? drawTextRun = context.Info.pfnDrawTextRun;
        if (drawTextRun == null)
        {
            // Measure-only context (no draw callback installed): nothing to draw.
            return LsErr.None;
        }

        bool isRtl = line.Flow is LsTFlow.WS or LsTFlow.WN or LsTFlow.NE or LsTFlow.NW;

        int totalWidth = 0;
        foreach (RunInfo run in line.Runs)
        {
            totalWidth += run.Width;
        }

        int x = isRtl ? pt.x + totalWidth : pt.x;
        int offset = 0;
        foreach (RunInfo run in line.Runs)
        {
            // Heights are passed as zero: the WPF DrawTextRun callback ignores LsHeights
            // entirely (it shapes from the run's own metrics), and GetRunTextMetrics cannot be
            // driven at display time (the WPF callback resolves the run through the FullText
            // static, which is null outside line formatting).
            LsHeights heights = default;
            LSPOINT ptText = new() { x = x, y = pt.y };
            LSPOINT ptRun = ptText;
            fixed (char* pText = line.Text)
            fixed (int* pAdvances = line.Advances)
            {
                LsErr err = drawTextRun(
                    context.Info.pols,
                    (Plsrun)(nuint)run.Run,
                    ref ptText,
                    pText + offset,
                    pAdvances + offset,
                    run.Length,
                    line.Flow,
                    displayMode,
                    ref ptRun,
                    ref heights,
                    run.Width,
                    ref clipRect);
                if (err != LsErr.None)
                {
                    return err;
                }
            }

            x = isRtl ? x - run.Width : x + run.Width;
            offset += run.Length;
        }

        return LsErr.None;
    }

    /// <summary>
    /// Enumerate a line by driving the context's <c>EnumText</c> (and <c>EnumTab</c> for tab
    /// runs) callbacks for every text run in layout order, with accumulated run origins. This is
    /// what <c>FullTextLine.GetIndexedGlyphRuns</c> calls: WPF resolves each run through
    /// <c>Draw.CurrentLine</c> (set by the enumeration DrawingState) and builds an
    /// <c>IndexedGlyphRun</c> from the returned cp range, text, advances, and origin. Inline
    /// object runs are skipped (there is no text to enumerate and WPF's EnumText would
    /// dereference a null Shapeable). A context with no enumeration callback installed is a
    /// no-op success.
    /// </summary>
    public static unsafe LsErr LoEnumLine(IntPtr ploline, bool reverseOder, bool fGeometryneeded, ref LSPOINT pt)
    {
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        LoContext context = line.Context;
        EnumText? enumText = context.Info.pfnEnumText;
        EnumTab? enumTab = context.Info.pfnEnumTab;
        if (enumText == null && enumTab == null)
        {
            // Measure-only context (no enumeration callbacks installed): nothing to enumerate.
            return LsErr.None;
        }

        // Enumeration always walks logical order (reverseOder is a native flag the caller never
        // sets here) and geometry is not provided (WPF's EnumText asserts fGeometryProvided == 0).
        _ = reverseOder;
        _ = fGeometryneeded;

        LsHeights heights = default;
        bool isRtl = line.Flow is LsTFlow.WS or LsTFlow.WN or LsTFlow.NE or LsTFlow.NW;
        int totalWidth = 0;
        foreach (RunInfo run in line.Runs)
        {
            totalWidth += run.Width;
        }

        // Right-to-left flow starts at the right edge (pt.x) and moves left, matching the
        // native convention (LoDisplayLine does the same): each run's origin is the previous
        // run's origin minus its width, so glyph runs land at x < pt.x.
        int x = isRtl ? pt.x + totalWidth : pt.x;
        int offset = 0;
        foreach (RunInfo run in line.Runs)
        {
            if (!run.IsObject)
            {
                bool isTab = run.Length == 1 && line.Text[offset] == context.Info.wchTab;
                LSPOINT runStart = new() { x = x, y = pt.y };
                LsErr err;
                fixed (char* pText = line.Text)
                fixed (int* pAdvances = line.Advances)
                {
                    err = isTab && enumTab is not null
                        ? enumTab(
                            context.Info.pols,
                            (Plsrun)(nuint)run.Run,
                            run.StartCp,
                            pText + offset,
                            '\u0000',
                            line.Flow,
                            0,
                            0,
                            ref runStart,
                            ref heights,
                            run.Width)
                        : enumText is not null
                            ? enumText(
                                context.Info.pols,
                                (Plsrun)(nuint)run.Run,
                                run.StartCp,
                                run.Length,
                                pText + offset,
                                run.Length,
                                line.Flow,
                                0,
                                0,
                                ref runStart,
                                ref heights,
                                run.Width,
                                0,
                                pAdvances + offset,
                                null,
                                null,
                                null,
                                null,
                                null,
                                null,
                                0)
                            : LsErr.None;
                }

                if (err != LsErr.None)
                {
                    return err;
                }
            }

            x = isRtl ? x - run.Width : x + run.Width;
            offset += run.Length;
        }

        return LsErr.None;
    }

    /// <summary>
    /// Query the text cell (and owning sub-line/run) at character position
    /// <paramref name="lscpQuery"/>. The cell is the single character at that position, whose
    /// origin is the accumulated advance of every earlier character in the line (the native
    /// "main direction" convention: logical character 0 starts at x = 0 for both LTR and RTL
    /// flows, and the caller inverts for the paragraph direction). Fills sub-line 0 of
    /// <paramref name="pSubLineInfo"/> with the run owning the character (the caller resolves
    /// the run's LSRun through it) and returns 1 in <paramref name="actualDepthQuery"/>.
    /// </summary>
    public static LsErr LoQueryLineCpPpoint(
        IntPtr ploline,
        int lscpQuery,
        int depthQueryMax,
        IntPtr pSubLineInfo,
        out int actualDepthQuery,
        out LsTextCell lsTextCell)
    {
        actualDepthQuery = 0;
        lsTextCell = default;
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        if (line.NumChars <= 0)
        {
            return LsErr.None;
        }

        // lscp is absolute (store coordinates): WPF indexes the store's plsrun vector with
        // lscp - CpFirst, and for a wrapped paragraph the second line's CpFirst is nonzero.
        int lineFirstCp = line.Runs[0].StartCp;
        int lscp = Math.Clamp(lscpQuery, lineFirstCp, lineFirstCp + line.NumChars - 1);
        FillCellForCp(line, lscp - lineFirstCp, lineFirstCp, depthQueryMax, pSubLineInfo, out actualDepthQuery, ref lsTextCell);
        return LsErr.None;
    }

    /// <summary>
    /// Query the text cell (and owning sub-line/run) at point <paramref name="ptQuery"/>. The
    /// point is in LS "main direction" units relative to the line start (logical character 0 at
    /// x = 0); the caller converts paragraph-relative distances and inverts for RTL. The cell
    /// under the x coordinate is returned; x beyond the last character clamps to the trailing
    /// edge of the last character (the caller uses this for caret-beyond-end).
    /// </summary>
    public static LsErr LoQueryLinePointPcp(
        IntPtr ploline,
        ref LSPOINT ptQuery,
        int depthQueryMax,
        IntPtr pSubLineInfo,
        out int actualDepthQuery,
        out LsTextCell lsTextCell)
    {
        actualDepthQuery = 0;
        lsTextCell = default;
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        if (line.NumChars <= 0)
        {
            return LsErr.None;
        }

        // Find the character whose cell spans the x coordinate: cell i covers
        // [prefix(i), prefix(i) + advance(i)). Clamp to the first/last character.
        int rel = 0;
        int prefix = 0;
        for (int i = 0; i < line.NumChars; i++)
        {
            if (ptQuery.x < prefix + line.Advances[i])
            {
                rel = i;
                break;
            }

            prefix += line.Advances[i];
            rel = i;
        }

        FillCellForCp(line, rel, line.Runs[0].StartCp, depthQueryMax, pSubLineInfo, out actualDepthQuery, ref lsTextCell);
        return LsErr.None;
    }

    /// <summary>
    /// Fill <paramref name="lsTextCell"/> (single-character cell) and sub-line 0 of
    /// <paramref name="pSubLineInfo"/> for the character at line-relative cp
    /// <paramref name="relCp"/>. <paramref name="lineFirstCp"/> is the line's absolute first cp
    /// (the lscp of the first character), which the cell and sub-line report so WPF can index
    /// its store with <c>lscp - CpFirst</c>.
    /// </summary>
    private static unsafe void FillCellForCp(
        LoLine line,
        int relCp,
        int lineFirstCp,
        int depthQueryMax,
        IntPtr pSubLineInfo,
        out int actualDepthQuery,
        ref LsTextCell lsTextCell)
    {
        actualDepthQuery = 0;

        // Locate the run owning the character and the accumulated line-relative x of its start
        // (main-direction convention: logical character 0 at x = 0).
        RunInfo owner = line.Runs[^1];
        int runStartX = 0;
        int charX = 0;
        int offset = 0;
        foreach (RunInfo run in line.Runs)
        {
            if (relCp < offset + run.Length)
            {
                owner = run;
                for (int i = 0; i < relCp; i++)
                {
                    charX += line.Advances[i];
                }

                break;
            }

            runStartX += run.Width;
            offset += run.Length;
        }

        if (pSubLineInfo != IntPtr.Zero && depthQueryMax > 0)
        {
            LsQSubInfo* sub = (LsQSubInfo*)pSubLineInfo;
            sub[0].lstflowSubLine = line.Flow;
            sub[0].lscpFirstSubLine = lineFirstCp;
            sub[0].lsdcpSubLine = line.NumChars;
            sub[0].pointUvStartSubLine = default;
            sub[0].dupSubLine = 0;
            foreach (RunInfo run in line.Runs)
            {
                sub[0].dupSubLine += run.Width;
            }

            sub[0].idobj = owner.IsObject ? IdObjInlineObject : IdObjTextChp;
            sub[0].plsrun = owner.Run;
            sub[0].lscpFirstRun = owner.StartCp;
            sub[0].lsdcpRun = owner.Length;
            sub[0].pointUvStartRun = new LSPOINT { x = runStartX };
            sub[0].dupRun = owner.Width;
            actualDepthQuery = 1;
        }

        lsTextCell.lscpStartCell = lineFirstCp + relCp;
        lsTextCell.lscpEndCell = lineFirstCp + relCp;
        lsTextCell.pointUvStartCell = new LSPOINT { x = charX };
        lsTextCell.dupCell = line.Advances[relCp];
        lsTextCell.cCharsInCell = 1;
        lsTextCell.cGlyphsInCell = 0;
        lsTextCell.plsCellDetails = IntPtr.Zero;
    }

    /// <summary>
    /// Acquire a break record for a line. The record carries the line's end cp; the optimal
    /// breaker re-runs the DP from the next line start (per-line demerits depend only on
    /// adjacent breaks), so the record needs no DP continuation state in v1.
    /// </summary>
    public static LsErr LoAcquireBreakRecord(IntPtr ploline, out IntPtr pbreakrec)
    {
        pbreakrec = IntPtr.Zero;
        if (!TryGetHandle(ploline, out LoLine? line))
        {
            return LsErr.InvalidLine;
        }

        pbreakrec = AllocHandle(new BreakRecord { BreakCp = line.EndCp });
        return LsErr.None;
    }

    /// <summary>Dispose a break record.</summary>
    public static LsErr LoDisposeBreakRecord(IntPtr pBreakRec, bool finalizing)
    {
        _ = finalizing;
        if (!TryGetHandle<BreakRecord>(pBreakRec, out _))
        {
            return LsErr.InvalidParameter;
        }

        FreeHandle(pBreakRec);
        return LsErr.None;
    }

    /// <summary>Clone a break record.</summary>
    public static LsErr LoCloneBreakRecord(IntPtr pBreakRec, out IntPtr pBreakRecClone)
    {
        pBreakRecClone = IntPtr.Zero;
        if (!TryGetHandle(pBreakRec, out BreakRecord? record))
        {
            return LsErr.InvalidParameter;
        }

        pBreakRecClone = AllocHandle(new BreakRecord { BreakCp = record.BreakCp });
        return LsErr.None;
    }

    /// <summary>
    /// Create the break candidates for the line starting at <paramref name="cpFirst"/>. Every
    /// feasible break opportunity becomes a pre-formatted line (<c>pplolineArray</c>) with its
    /// metrics (<c>plslinfoArray</c>); <paramref name="bestFitIndex"/> is the Knuth-Plass
    /// optimal choice among them. The caller keeps only the chosen line and finalizes the
    /// rest; the arrays are session-owned and freed by the next call or the session disposal.
    /// </summary>
    public static unsafe LsErr LoCreateBreaks(
        IntPtr ploc,
        int cpFirst,
        IntPtr previousBreakRecord,
        IntPtr ploparabreak,
        IntPtr ptslinevariantRestriction,
        ref LsBreaks lsbreaks,
        out int bestFitIndex)
    {
        lsbreaks = default;
        bestFitIndex = 0;
        _ = previousBreakRecord;
        _ = ptslinevariantRestriction;

        if (!TryGetHandle(ploc, out LoContext? context))
        {
            return LsErr.InvalidContext;
        }

        if (!TryGetHandle(ploparabreak, out ParaBreakSession? session))
        {
            return LsErr.InvalidContext;
        }

        LsErr err = MeasureParagraph(
            ploc,
            context,
            cpFirst,
            out LoLine model,
            out List<ParagraphChunk> chunks,
            out int marginLeft,
            out LsEndRes hardBreakEndr,
            out bool endIsForcedBreak);
        if (err != LsErr.None)
        {
            return err;
        }

        if (model.NumChars == 0)
        {
            return LsErr.None;
        }

        int column = session.MaxWidth - marginLeft;

        // The break-node model: the paragraph start, every opportunity, and the
        // forced paragraph end.
        var nodes = new List<KnuthPlass.BreakNode>((model.NumChars / 4) + 3)
        {
            new(0, 0, 0, 0, 0, 0)
        };
        int cumulative = 0;
        for (int i = 0; i < model.NumChars; i++)
        {
            cumulative += model.Advances[i];
            if (!IsBreakAfter(context, model.Grpf, model.Text[i]))
            {
                continue;
            }

            int offset = i + 1;
            int glueWidth = model.Text[i] == context.Info.wchSpace ? model.Advances[i] : 0;
            bool hyphenish = IsHyphenAfter(context, model.Text[i]);
            int stretch = glueWidth > 0
                ? (session.Justified ? Math.Max(1, glueWidth) : Math.Max(1, column))
                : 0;
            int shrink = glueWidth > 0 && session.Justified ? Math.Max(1, glueWidth / 2) : 0;
            nodes.Add(new KnuthPlass.BreakNode(
                offset,
                cumulative,
                glueWidth,
                stretch,
                shrink,
                hyphenish ? HyphenBreakPenalty : 0));
        }

        // The paragraph end is a forced break. A hard separator was consumed by the fetch
        // (cp advanced past it), so the end offset is the model length either way.
        nodes.Add(new KnuthPlass.BreakNode(
            model.NumChars,
            cumulative,
            glueWidth: 0,
            glueStretch: 0,
            glueShrink: 0,
            penalty: KnuthPlass.InfinityPenalty));

        // Feasible candidates: the line can be set within the column (shrink covers a
        // justified overfull). Mirror the greedy emergency when nothing fits.
        var candidateOffsets = new List<int>();
        int cumulativeShrink = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            KnuthPlass.BreakNode node = nodes[i];
            cumulativeShrink += node.GlueShrink;
            // The paragraph end is a candidate only when the whole remainder fits
            // (shrink credit included); an overfull end falls back to the earlier
            // opportunities or the greedy emergency.
            if (node.Offset > 0 && node.CumulativeWidth - cumulativeShrink <= column)
            {
                candidateOffsets.Add(node.Offset);
            }
        }

        if (candidateOffsets.Count == 0)
        {
            // Greedy emergency parity: the longest prefix that fits the column, at least one
            // character (an over-wide single character stays on its own line).
            int width = 0;
            int fit = 0;
            for (int i = 0; i < model.NumChars && width + model.Advances[i] <= column; i++)
            {
                width += model.Advances[i];
                fit = i + 1;
            }

            candidateOffsets.Add(Math.Max(1, fit));
        }

        // Knuth-Plass optimal choice among the candidates.
        int[] optimal = KnuthPlass.ComputeBreaks(nodes, column);
        int chosenOffset = optimal.Length > 0 ? optimal[0] : candidateOffsets[^1];
        bestFitIndex = candidateOffsets.IndexOf(chosenOffset);
        if (bestFitIndex < 0)
        {
            bestFitIndex = candidateOffsets.Count - 1;
        }

        // Pre-format every candidate line and publish the arrays. The arrays are session-owned
        // (freed by the next call or LoDisposeParaBreakingSession); the line HANDLES are
        // disposed by the caller (each FullTextBreakpoint owns and finalizes its ploline).
        int count = candidateOffsets.Count;
        LsLInfo* infoArray = (LsLInfo*)Marshal.AllocHGlobal(count * Marshal.SizeOf<LsLInfo>());
        IntPtr* penaltyArray = (IntPtr*)Marshal.AllocHGlobal(count * sizeof(IntPtr));
        IntPtr* lineArray = (IntPtr*)Marshal.AllocHGlobal(count * sizeof(IntPtr));
        FreeBreakArrays(session);
        session.InfoArray = (nint)infoArray;
        session.PenaltyArray = (nint)penaltyArray;
        session.LineArray = (nint)lineArray;

        try
        {
            for (int i = 0; i < count; i++)
            {
                int cpEnd = cpFirst + candidateOffsets[i];
                bool isParagraphEnd = candidateOffsets[i] == model.NumChars;
                LsEndRes endr = isParagraphEnd
                    ? (endIsForcedBreak ? hardBreakEndr : LsEndRes.endrEndPara)
                    : LsEndRes.endrNormal;
                err = BuildCandidateLine(
                    ploc,
                    context,
                    model,
                    chunks,
                    cpFirst,
                    cpEnd,
                    marginLeft,
                    endr,
                    out LoLine? candidate,
                    out LsLInfo info,
                    out LsLineWidths widths);
                if (err != LsErr.None)
                {
                    return err;
                }

                infoArray[i] = info;
                penaltyArray[i] = IntPtr.Zero;
                lineArray[i] = AllocHandle(candidate!);
                _ = Interlocked.Increment(ref context.LiveLines);
                _ = widths;
            }
        }
        catch
        {
            FreeBreakArrays(session);
            throw;
        }

        lsbreaks.cBreaks = count;
        lsbreaks.plslinfoArray = infoArray;
        lsbreaks.plinepenaltyArray = penaltyArray;
        lsbreaks.pplolineArray = lineArray;
        return LsErr.None;
    }

    /// <summary>
    /// Create a paragraph breaking session: records the paragraph start, the column width and
    /// the justification mode (pap.fJustify) for the <see cref="LoCreateBreaks"/> calls.
    /// </summary>
    public static LsErr LoCreateParaBreakingSession(
        IntPtr ploc,
        int cpParagraphFirst,
        int maxWidth,
        IntPtr previousParaBreakRecord,
        ref IntPtr pploparabreak,
        ref bool fParagraphJustified)
    {
        _ = previousParaBreakRecord;
        pploparabreak = IntPtr.Zero;
        fParagraphJustified = false;

        if (!TryGetHandle(ploc, out LoContext? context))
        {
            return LsErr.InvalidContext;
        }

        LsPap pap = default;
        LsErr err = context.Info.pfnFetchPap!(ploc, cpParagraphFirst, ref pap);
        if (err != LsErr.None)
        {
            return err;
        }

        var session = new ParaBreakSession
        {
            CpParagraphFirst = cpParagraphFirst,
            MaxWidth = maxWidth,
            Justified = pap.fJustify != 0
        };
        fParagraphJustified = session.Justified;
        pploparabreak = AllocHandle(session);
        return LsErr.None;
    }

    /// <summary>Dispose a paragraph breaking session and its published break arrays.</summary>
    public static LsErr LoDisposeParaBreakingSession(IntPtr ploparabreak, bool finalizing)
    {
        _ = finalizing;
        if (!TryGetHandle(ploparabreak, out ParaBreakSession? session))
        {
            return LsErr.InvalidParameter;
        }

        FreeBreakArrays(session);
        FreeHandle(ploparabreak);
        return LsErr.None;
    }

    /// <summary>Relieve a line's penalty resource. v1 no-op success (no penalty resources exist).</summary>
    public static LsErr LoRelievePenaltyResource(IntPtr ploline)
    {
        return TryGetHandle<LoLine>(ploline, out _) ? LsErr.None : LsErr.InvalidLine;
    }

    /// <summary>
    /// Query installed object-handler info. v1 stub: <see cref="LsErr.NotImplemented"/> (no object
    /// handlers are installed in a v1 context).
    /// </summary>
    public static unsafe LsErr LocbkGetObjectHandlerInfo(IntPtr ploc, uint objectId, void* objectInfo)
    {
        _ = objectId;
        _ = objectInfo;
        return TryGetHandle<LoContext>(ploc, out _) ? LsErr.NotImplemented : LsErr.InvalidContext;
    }

    /// <summary>
    /// Acquire the text penalty module for a context. v1 returns a valid module token bound to the
    /// context (no penalty logic), so WPF's <c>TextPenaltyModule</c> lifecycle round-trips.
    /// </summary>
    public static LsErr LoAcquirePenaltyModule(IntPtr ploc, out IntPtr penaltyModuleHandle)
    {
        penaltyModuleHandle = IntPtr.Zero;
        if (!TryGetHandle(ploc, out LoContext? context))
        {
            return LsErr.InvalidContext;
        }

        penaltyModuleHandle = AllocHandle(context);
        return LsErr.None;
    }

    /// <summary>Dispose a text penalty module.</summary>
    public static LsErr LoDisposePenaltyModule(IntPtr penaltyModuleHandle)
    {
        if (!TryGetHandle<LoContext>(penaltyModuleHandle, out _))
        {
            return LsErr.InvalidParameter;
        }

        FreeHandle(penaltyModuleHandle);
        return LsErr.None;
    }

    /// <summary>
    /// Return the penalty module's internal handle. v1 has no internal module state, so the
    /// context handle is returned.
    /// </summary>
    public static LsErr LoGetPenaltyModuleInternalHandle(IntPtr penaltyModuleHandle, out IntPtr penaltyModuleInternalHandle)
    {
        if (TryGetHandle(penaltyModuleHandle, out LoContext? context))
        {
            penaltyModuleInternalHandle = context.Info.pols;
            return LsErr.None;
        }

        penaltyModuleInternalHandle = IntPtr.Zero;
        return LsErr.InvalidParameter;
    }

    /// <summary>
    /// Create a DWrite text-analysis sink. v1 stub: returns null (the analyzer is managed in
    /// this host).
    /// </summary>
    public static unsafe void* CreateTextAnalysisSink()
    {
        return null;
    }

    /// <summary>Get the script-analysis list from a sink. v1 stub: null.</summary>
    public static unsafe void* GetScriptAnalysisList(void* textAnalysisSink)
    {
        _ = textAnalysisSink;
        return null;
    }

    /// <summary>Get the number-substitution list from a sink. v1 stub: null.</summary>
    public static unsafe void* GetNumberSubstitutionList(void* textAnalysisSink)
    {
        _ = textAnalysisSink;
        return null;
    }

    /// <summary>
    /// Create a DWrite text-analysis source. v1 stub: returns <see cref="ENotImplemented"/> and a
    /// null source.
    /// </summary>
    public static unsafe int CreateTextAnalysisSource(
        char* text,
        uint length,
        char* culture,
        void* factory,
        bool isRightToLeft,
        char* numberCulture,
        bool ignoreUserOverride,
        uint numberSubstitutionMethod,
        void** ppTextAnalysisSource)
    {
        _ = text;
        _ = length;
        _ = culture;
        _ = factory;
        _ = isRightToLeft;
        _ = numberCulture;
        _ = ignoreUserOverride;
        _ = numberSubstitutionMethod;

        if (ppTextAnalysisSource != null)
        {
            *ppTextAnalysisSource = null;
        }

        return ENotImplemented;
    }

    private static unsafe LsErr FormatLine(
        IntPtr pols,
        LoContext context,
        LoLine line,
        int cp,
        int durColumn,
        ref LsLInfo info,
        ref LsLineWidths widths)
    {
        LsPap pap = default;
        LsErr err = context.Info.pfnFetchPap!(pols, cp, ref pap);
        if (err != LsErr.None)
        {
            return err;
        }

        if ((pap.grpf & LsPapOptions.fFmiAnm) != 0)
        {
            // Auto-numbering needs GetAutoNumberInfo, which v1 does not install.
            return LsErr.NotImplemented;
        }

        bool applyBreakingRules = (pap.grpf & LsPapOptions.fFmiApplyBreakingRules) != 0;
        LsTFlow flow = pap.lstflow;
        line.Flow = flow;

        bool eol = false;
        LsEndRes endr = LsEndRes.endrEndPara;
        int remaining = durColumn;
        // lscp is the Line Services character position handed to the callbacks; it diverges from
        // the client cp across zero-cp control runs (bidi reverse markers, which WPF emits as
        // spans that occupy one LSCP but no client character).
        int lscp = cp;
        IntPtr lastRun = IntPtr.Zero;
        bool marginSet = false;

        while (!eol)
        {
            LsLineProps lineProps = default;
            err = context.Info.pfnFetchLineProps!(pols, lscp, line.Runs.Count == 0 ? 1 : 0, ref lineProps);
            if (err != LsErr.None)
            {
                return err;
            }

            if (!marginSet)
            {
                // Apply the left margin exactly once. Zero-cp control runs (bidi reverse
                // markers) keep line.Runs empty, so a Runs.Count check would double-count.
                widths.upStartMainText = lineProps.durLeft;
                remaining -= lineProps.durLeft;
                marginSet = true;
            }

            int bufferUsed = 0;
            int isHidden = 0;
            int len = 0;
            LsChp chp = default;
            IntPtr currRun = IntPtr.Zero;
            char* textPointer = null;

            int capacity = line.EnsureCapacity(line.NumChars + 1);
            fixed (char* pText = line.Text)
            {
                err = context.Callbacks.pfnFetchRunRedefined!(
                    pols,
                    lscp,
                    0,
                    IntPtr.Zero,
                    pText + line.NumChars,
                    capacity - line.NumChars,
                    ref bufferUsed,
                    out textPointer,
                    ref len,
                    ref isHidden,
                    ref chp,
                    ref currRun);
            }

            _ = isHidden;
            if (err != LsErr.None)
            {
                return err;
            }

            lastRun = currRun;

            // A zero-length fetch is the terminal end-of-paragraph condition, regardless of the
            // reported object id (the WPF callback leaves idObj zeroed on the empty fetch). This
            // must be checked before the reverse-marker branch, which would otherwise advance the
            // fetch position forever.
            if (len <= 0)
            {
                break;
            }

            bool usedBuffer = bufferUsed != 0;

            // A fetched run with no buffer content and no direct pointer means the callback's
            // buffer was too small: it reported the required length instead. Grow and retry at
            // the same lscp. This must precede the object-id dispatch because the callback leaves
            // idObj zeroed on the too-small response (a zeroed idObj is also the Reverse marker).
            if (!usedBuffer && textPointer == null)
            {
                if (capacity >= line.NumChars + len)
                {
                    return LsErr.ClientAbort;
                }

                _ = line.EnsureCapacity(line.NumChars + len);
                continue;
            }

            // Bidi open/close reverse markers (LsChp.idObj == Reverse): WPF emits these as spans
            // that consume one LSCP but zero client characters and zero width. Advance the fetch
            // position only; never append, measure, or consume cp here.
            if (chp.idObj == IdObjReverse)
            {
                lscp += Math.Max(1, len);
                continue;
            }

            // Inline object (embedded element): one client character, no real text. The width
            // comes from the callbacks; the real WPF GetRunCharWidths reports 0 for synthetic
            // runs because the object handler is not installed in v1 (LocbkGetObjectHandlerInfo
            // is a documented stub), so real objects lay out zero-width but consume their cp.
            if (chp.idObj == IdObjInlineObject)
            {
                int objectLen = usedBuffer || textPointer != null ? Math.Max(1, len) : 0;
                if (!usedBuffer && textPointer != null)
                {
                    _ = line.EnsureCapacity(line.NumChars + objectLen);
                    fixed (char* pText = line.Text)
                    {
                        for (int i = 0; i < objectLen; i++)
                        {
                            pText[line.NumChars + i] = textPointer[i];
                        }
                    }
                }

                int objectWidth = 0;
                if (objectLen > 0)
                {
                    LsErr widthErr = MeasureWidth(pols, context, line, line.NumChars, currRun, objectLen, int.MaxValue, flow, out objectWidth, out _);
                    if (widthErr != LsErr.None)
                    {
                        return widthErr;
                    }
                }

                line.AppendRun(cp, currRun, 1, objectWidth, isObject: true);
                line.NumChars += 1;
                remaining -= objectWidth;
                cp += 1;
                lscp += Math.Max(1, len);
                continue;
            }

            if (!usedBuffer && textPointer != null)
            {
                // Direct pointer: copy into the line text so break backtracking and width
                // accounting work on one buffer.
                _ = line.EnsureCapacity(line.NumChars + len);
                fixed (char* pText = line.Text)
                {
                    for (int i = 0; i < len; i++)
                    {
                        pText[line.NumChars + i] = textPointer[i];
                    }
                }
            }

            LsErr measureErr = MeasureWidth(pols, context, line, line.NumChars, currRun, len, remaining, flow, out int totalWidth, out int chars);
            if (measureErr != LsErr.None)
            {
                return measureErr;
            }

            if (chars <= 0)
            {
                break;
            }

            // A hard line/para separator in the fetched text (WPF isolates these into their own
            // runs, but a separator can arrive embedded in a fetched chunk) ends the line before
            // it. The separator character is consumed as the line-ending newline: WPF derives
            // the line's character range from cpLimToContinue, and Line.EndOfParagraph requires
            // the TextEndOfParagraph/TextEndOfLine run to fall inside that range. Without the
            // consume, the line loop re-formats at the separator and CountText asserts
            // "Zero-length text line!".
            int hardBreak = FindHardBreak(line, line.NumChars, chars, context);
            if (hardBreak >= 0)
            {
                int keep = hardBreak;
                int width = 0;
                for (int i = 0; i < keep; i++)
                {
                    width += line.Advances[line.NumChars + i];
                }

                if (keep > 0)
                {
                    line.AppendRun(cp, currRun, keep, width);
                    line.NumChars += keep;
                }

                char separator = line.Text[line.NumChars + hardBreak];
                endr = separator == context.Info.wchEndPara1 || separator == context.Info.wchEndPara2
                    ? LsEndRes.endrEndPara
                    : LsEndRes.endrSoftCR;

                cp += 1;
                eol = true;
                break;
            }

            if (applyBreakingRules && totalWidth > remaining)
            {
                eol = true;
                endr = LsEndRes.endrNormal;

                // Append the measured run so the whole accumulated line is available for break
                // scanning, then find the last break opportunity that fits the column (after a
                // space, after a hyphen/dash, or between CJK ideographs) and trim to it. Without
                // an opportunity, fall back to an emergency character break: keep all but the
                // last character, or the whole line when it is a single over-wide character (the
                // callbacks allow at most one character that does not fully fit).
                line.AppendRun(cp, currRun, chars, totalWidth);
                line.NumChars += chars;

                if (TryBreakAtOpportunity(line, context, pap.grpf, line.NumChars, durColumn - widths.upStartMainText, out int keep))
                {
                    TrimRunsTo(line, keep);
                    line.NumChars = keep;
                }
                else if (line.NumChars > 1)
                {
                    TrimRunsTo(line, line.NumChars - 1);
                    line.NumChars -= 1;
                }

                cp = line.Runs[^1].StartCp + line.Runs[^1].Length;
            }
            else
            {
                line.AppendRun(cp, currRun, chars, totalWidth);
                line.NumChars += chars;
                remaining -= totalWidth;
                cp += chars;
                lscp += chars;
            }
        }

        ComputeHeights(pols, context, line, ref info, lastRun);

        widths.upLimLine = widths.upStartMainText;
        foreach (RunInfo run in line.Runs)
        {
            widths.upLimLine += run.Width;
        }

        widths.upMinLimLine = widths.upStartTrailing = widths.upLimLine;

        if (line.NumChars > 0 && line.Runs.Count > 0)
        {
            RunInfo last = line.Runs[^1];
            int trailing = widths.upStartTrailing;
            int c = line.NumChars - 1;
            for (int i = last.Length - 1; i >= 0 && line.Text[c] == context.Info.wchSpace; i--)
            {
                trailing -= line.Advances[c];
                c--;
            }

            widths.upStartTrailing = trailing;
        }

        widths.upMinStartTrailing = widths.upStartTrailing;

        info.cpLimToContinue = cp;
        info.cpLimToStay = cp;
        info.endr = endr;
        return LsErr.None;
    }

    /// <summary>
    /// Measure <paramref name="len"/> characters of <c>line.Text</c> starting at
    /// <paramref name="offset"/> through <c>GetRunCharWidths</c>, filling
    /// <c>line.Advances</c>. <c>maxWidth</c> bounds how many characters fit (the callback may
    /// include at most one character that does not fully fit, per the WPF nest contract).
    /// </summary>
    private static unsafe LsErr MeasureWidth(
        IntPtr pols,
        LoContext context,
        LoLine line,
        int offset,
        IntPtr currRun,
        int len,
        int maxWidth,
        LsTFlow flow,
        out int totalWidth,
        out int chars)
    {
        totalWidth = 0;
        chars = 0;
        fixed (char* pText = line.Text)
        fixed (int* pAdvances = line.Advances)
        {
            return context.Info.pfnGetRunCharWidths!(
                pols,
                (Plsrun)(nuint)currRun,
                LsDevice.Presentation,
                pText + offset,
                len,
                maxWidth,
                flow,
                pAdvances + offset,
                ref totalWidth,
                ref chars);
        }
    }

    private static int FindHardBreak(LoLine line, int start, int count, LoContext context)
    {
        for (int i = 0; i < count; i++)
        {
            char c = line.Text[start + i];
            if (c == context.Info.wchEndLineInPara
                || c == context.Info.wchEndPara1
                || c == context.Info.wchEndPara2)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when a line break is allowed immediately after <paramref name="c"/>: after a space,
    /// after a hyphen/dash (unless <c>fFmiTreatHyphenAsRegular</c>), or after a CJK ideograph
    /// (BreakCJK strategy, which WPF sets via <c>LoSetBreaking</c>).
    /// </summary>
    private static bool IsBreakAfter(LoContext context, LsPapOptions grpf, char c)
    {
        return (c == context.Info.wchSpace)
            || (((grpf & LsPapOptions.fFmiTreatHyphenAsRegular) == 0)
                && (c == context.Info.wchHyphen
                    || c == context.Info.wchEnDash
                    || c == context.Info.wchEmDash
                    || c == context.Info.wchNonReqHyphen))
            || IsCjkIdeograph(c);
    }

    /// <summary>True when <paramref name="c"/> is a hyphen/dash break (penalized in the DP).</summary>
    private static bool IsHyphenAfter(LoContext context, char c)
    {
        return c == context.Info.wchHyphen
            || c == context.Info.wchEnDash
            || c == context.Info.wchEmDash
            || c == context.Info.wchNonReqHyphen;
    }

    /// <summary>
    /// Looks up the CJK-ideograph classification flag. ASCII and the European/Middle-East/
    /// Indic blocks are all below U+2E80 and never ideographic, so the (process-lifetime, lazily
    /// built) classification tables are only forced for text that can actually contain ideographs.
    /// </summary>
    private static bool IsCjkIdeograph(char c)
    {
        if (c < 0x2E80 || char.IsSurrogate(c))
        {
            return false;
        }

        ClassificationNativeTables tables = ClassificationTableBuilder.NativeTables;
        return (tables.AttributeOf(tables.ClassOf(c)).Flags & FlagIdeo) != 0;
    }

    /// <summary>
    /// Find the last break opportunity in <c>line.Text[0..scanEnd)</c> whose accumulated width
    /// does not exceed <paramref name="maxWidth"/>, and return the character offset (exclusive)
    /// to keep on the line. Break opportunities come from <see cref="IsBreakAfter"/>: after
    /// spaces, hyphens/dashes, and CJK ideographs. Returns false when no opportunity fits, so the
    /// caller falls back to an emergency character break.
    /// </summary>
    private static bool TryBreakAtOpportunity(LoLine line, LoContext context, LsPapOptions grpf, int scanEnd, int maxWidth, out int keep)
    {
        keep = 0;
        int width = 0;
        for (int i = 0; i < scanEnd; i++)
        {
            width += line.Advances[i];
            if (width > maxWidth)
            {
                // Everything from the next character on exceeds the column; the last opportunity
                // that fit is the break.
                return keep > 0;
            }

            if (IsBreakAfter(context, grpf, line.Text[i]))
            {
                keep = i + 1;
            }
        }

        return keep > 0;
    }

    /// <summary>
    /// Trim the run list so it covers exactly the first <paramref name="keep"/> characters of
    /// the line, adjusting the run that contains the break point and dropping every later run.
    /// </summary>
    private static void TrimRunsTo(LoLine line, int keep)
    {
        int idx = 0;
        for (int i = 0; i < line.Runs.Count; i++)
        {
            RunInfo run = line.Runs[i];
            int runEnd = idx + run.Length;
            if (runEnd <= keep)
            {
                idx = runEnd;
                continue;
            }

            if (idx < keep)
            {
                int newWidth = run.Width;
                for (int c = runEnd - 1; c >= keep; c--)
                {
                    newWidth -= line.Advances[c];
                }

                line.Runs[i] = new RunInfo(run.StartCp, run.Run, keep - idx, newWidth, run.IsObject);
                i++;
            }

            if (i < line.Runs.Count)
            {
                line.Runs.RemoveRange(i, line.Runs.Count - i);
            }

            break;
        }
    }

    private static void ComputeHeights(IntPtr pols, LoContext context, LoLine line, ref LsLInfo info, IntPtr lastRun)
    {
        if (line.Runs.Count == 0)
        {
            // Empty line (e.g. immediate para separator): take the last fetched run's metrics so
            // the line still reports a height.
            if (lastRun == IntPtr.Zero)
            {
                return;
            }

            AccumulateHeights(pols, context, (Plsrun)(nuint)lastRun, ref info);
            return;
        }

        foreach (RunInfo run in line.Runs)
        {
            AccumulateHeights(pols, context, (Plsrun)(nuint)run.Run, ref info);
        }
    }

    private static void AccumulateHeights(IntPtr pols, LoContext context, Plsrun plsrun, ref LsLInfo info)
    {
        LsTxM presentation = default;
        _ = context.Info.pfnGetRunTextMetrics!(pols, plsrun, LsDevice.Presentation, LsTFlow.ES, ref presentation);
        info.dvpAscent = Math.Max(info.dvpAscent, presentation.dvAscent);
        info.dvpDescent = Math.Max(info.dvpDescent, presentation.dvDescent);
        info.dvpMultiLineHeight = Math.Max(info.dvpMultiLineHeight, presentation.dvMultiLineHeight);

        LsTxM reference = default;
        _ = context.Info.pfnGetRunTextMetrics!(pols, plsrun, LsDevice.Reference, LsTFlow.ES, ref reference);
        info.dvrAscent = Math.Max(info.dvrAscent, reference.dvAscent);
        info.dvrDescent = Math.Max(info.dvrDescent, reference.dvDescent);
        info.dvrMultiLineHeight = Math.Max(info.dvrMultiLineHeight, reference.dvMultiLineHeight);
    }

    /// <summary>Break penalty for hyphen/dash breaks (TeX \exhyphenpenalty).</summary>
    private const int HyphenBreakPenalty = 50;

    /// <summary>
    /// Fetches and measures the whole remaining paragraph from <paramref name="cp"/> into
    /// <paramref name="model"/> (text + per-character advances, mirroring
    /// <see cref="FormatLine"/>'s fetch loop without the width stop), recording the fetched
    /// chunks for candidate-line construction. A hard separator ends the paragraph and is
    /// consumed (cp advances past it, matching <see cref="FormatLine"/>).
    /// </summary>
    private static unsafe LsErr MeasureParagraph(
        IntPtr pols,
        LoContext context,
        int cp,
        out LoLine model,
        out List<ParagraphChunk> chunks,
        out int marginLeft,
        out LsEndRes hardBreakEndr,
        out bool endIsForcedBreak)
    {
        model = new LoLine(context);
        chunks = [];
        marginLeft = 0;
        hardBreakEndr = LsEndRes.endrEndPara;
        endIsForcedBreak = false;

        LsPap pap = default;
        LsErr err = context.Info.pfnFetchPap!(pols, cp, ref pap);
        if (err != LsErr.None)
        {
            return err;
        }

        model.Grpf = pap.grpf;

        int lscp = cp;
        bool marginSet = false;
        while (true)
        {
            LsLineProps lineProps = default;
            err = context.Info.pfnFetchLineProps!(pols, lscp, model.NumChars == 0 ? 1 : 0, ref lineProps);
            if (err != LsErr.None)
            {
                return err;
            }

            if (!marginSet)
            {
                marginLeft = lineProps.durLeft;
                marginSet = true;
            }

            int bufferUsed = 0;
            int isHidden = 0;
            int len = 0;
            LsChp chp = default;
            IntPtr currRun = IntPtr.Zero;
            char* textPointer = null;

            int capacity = model.EnsureCapacity(model.NumChars + 1);
            fixed (char* pText = model.Text)
            {
                err = context.Callbacks.pfnFetchRunRedefined!(
                    pols,
                    lscp,
                    0,
                    IntPtr.Zero,
                    pText + model.NumChars,
                    capacity - model.NumChars,
                    ref bufferUsed,
                    out textPointer,
                    ref len,
                    ref isHidden,
                    ref chp,
                    ref currRun);
            }

            _ = isHidden;
            if (err != LsErr.None)
            {
                return err;
            }

            if (len <= 0)
            {
                break;
            }

            bool usedBuffer = bufferUsed != 0;
            if (!usedBuffer && textPointer == null)
            {
                if (capacity >= model.NumChars + len)
                {
                    return LsErr.ClientAbort;
                }

                _ = model.EnsureCapacity(model.NumChars + len);
                continue;
            }

            if (chp.idObj == IdObjReverse)
            {
                lscp += Math.Max(1, len);
                continue;
            }

            if (chp.idObj == IdObjInlineObject)
            {
                int objectLen = usedBuffer || textPointer != null ? Math.Max(1, len) : 0;
                if (!usedBuffer && textPointer != null)
                {
                    _ = model.EnsureCapacity(model.NumChars + objectLen);
                    fixed (char* pText = model.Text)
                    {
                        for (int i = 0; i < objectLen; i++)
                        {
                            pText[model.NumChars + i] = textPointer[i];
                        }
                    }
                }

                int objectWidth = 0;
                if (objectLen > 0)
                {
                    err = MeasureWidth(pols, context, model, model.NumChars, currRun, objectLen, int.MaxValue, pap.lstflow, out objectWidth, out _);
                    if (err != LsErr.None)
                    {
                        return err;
                    }
                }

                chunks.Add(new ParagraphChunk(cp, currRun, 1, objectWidth, isObject: true));
                model.NumChars += 1;
                cp += 1;
                lscp += Math.Max(1, len);
                continue;
            }

            if (!usedBuffer && textPointer != null)
            {
                _ = model.EnsureCapacity(model.NumChars + len);
                fixed (char* pText = model.Text)
                {
                    for (int i = 0; i < len; i++)
                    {
                        pText[model.NumChars + i] = textPointer[i];
                    }
                }
            }

            err = MeasureWidth(pols, context, model, model.NumChars, currRun, len, int.MaxValue, pap.lstflow, out int totalWidth, out int chars);
            if (err != LsErr.None)
            {
                return err;
            }

            if (chars <= 0)
            {
                break;
            }

            int hardBreak = FindHardBreak(model, model.NumChars, chars, context);
            if (hardBreak >= 0)
            {
                int keep = hardBreak;
                if (keep > 0)
                {
                    chunks.Add(new ParagraphChunk(cp, currRun, keep, SumAdvances(model, model.NumChars, keep)));
                    model.NumChars += keep;
                }

                char separator = model.Text[model.NumChars + hardBreak];
                hardBreakEndr = separator == context.Info.wchEndPara1 || separator == context.Info.wchEndPara2
                    ? LsEndRes.endrEndPara
                    : LsEndRes.endrSoftCR;
                endIsForcedBreak = true;
                cp += 1;
                break;
            }

            chunks.Add(new ParagraphChunk(cp, currRun, chars, totalWidth));
            model.NumChars += chars;
            cp += chars;
            lscp += chars;
        }

        return LsErr.None;
    }

    private static int SumAdvances(LoLine line, int offset, int count)
    {
        int width = 0;
        for (int i = 0; i < count; i++)
        {
            width += line.Advances[offset + i];
        }

        return width;
    }

    /// <summary>
    /// Builds a pre-formatted candidate line for the range [cpFirst, cpEnd) from the measured
    /// paragraph model: runs come from the fetched chunks (trimmed at the boundary), heights
    /// from the run metrics, and the trailing/end info mirror <see cref="FormatLine"/>.
    /// </summary>
    private static LsErr BuildCandidateLine(
        IntPtr pols,
        LoContext context,
        LoLine model,
        List<ParagraphChunk> chunks,
        int cpFirst,
        int cpEnd,
        int marginLeft,
        LsEndRes endr,
        out LoLine? candidate,
        out LsLInfo info,
        out LsLineWidths widths)
    {
        candidate = null;
        info = default;
        widths = default;

        int length = cpEnd - cpFirst;
        if (length <= 0)
        {
            return LsErr.InvalidParameter;
        }

        candidate = new LoLine(context);
        _ = candidate.EnsureCapacity(length);
        Array.Copy(model.Text, 0, candidate.Text, 0, length);
        Array.Copy(model.Advances, 0, candidate.Advances, 0, length);
        candidate.NumChars = length;
        candidate.EndCp = cpEnd;
        candidate.Grpf = model.Grpf;

        int lineWidth = 0;
        int modelOffset = 0;
        foreach (ParagraphChunk chunk in chunks)
        {
            int chunkEnd = chunk.StartCp + chunk.Length;
            if (chunkEnd <= cpFirst)
            {
                modelOffset += chunk.Length;
                continue;
            }

            if (chunk.StartCp >= cpEnd)
            {
                break;
            }

            int sliceStart = Math.Max(chunk.StartCp, cpFirst);
            int sliceEnd = Math.Min(chunkEnd, cpEnd);
            int sliceLength = sliceEnd - sliceStart;
            if (sliceLength <= 0)
            {
                modelOffset += chunk.Length;
                continue;
            }

            int sliceWidth = chunk.IsObject
                ? SumAdvances(candidate, sliceStart - cpFirst, sliceLength)
                : SumAdvances(model, modelOffset + (sliceStart - chunk.StartCp), sliceLength);
            candidate.AppendRun(sliceStart, chunk.Run, sliceLength, sliceWidth, chunk.IsObject);
            lineWidth += sliceWidth;
            modelOffset += chunk.Length;
        }

        ComputeHeights(pols, context, candidate, ref info, chunks.Count > 0 ? chunks[^1].Run : IntPtr.Zero);

        info.cpLimToContinue = cpEnd;
        info.cpLimToStay = cpEnd;
        info.endr = endr;

        widths.upStartMainText = marginLeft;
        widths.upLimLine = marginLeft + lineWidth;
        widths.upMinLimLine = widths.upStartTrailing = widths.upLimLine;
        widths.upMinStartTrailing = widths.upLimLine;
        return LsErr.None;
    }

    private static void FreeBreakArrays(ParaBreakSession session)
    {
        if (session.InfoArray != 0)
        {
            Marshal.FreeHGlobal(session.InfoArray);
            session.InfoArray = 0;
        }

        if (session.PenaltyArray != 0)
        {
            Marshal.FreeHGlobal(session.PenaltyArray);
            session.PenaltyArray = 0;
        }

        if (session.LineArray != 0)
        {
            Marshal.FreeHGlobal(session.LineArray);
            session.LineArray = 0;
        }
    }

}

internal sealed class LoContext
{
    internal LoContext(LsContextInfo info, LscbkRedefined callbacks)
    {
        Info = info;
        Callbacks = callbacks;
    }

    /// <summary>Context configuration; <c>pols</c> is filled with the context handle by LoCreateContext.</summary>
    internal LsContextInfo Info;

    internal LscbkRedefined Callbacks { get; }

    /// <summary>Number of live lines created from this context (ContextInUse check).</summary>
    internal int LiveLines;
}

internal sealed class LoLine
{
    private const int InitialCapacity = 16;
    private int _capacity;

    internal LoLine(LoContext context)
    {
        Context = context;
        _capacity = Math.Max(InitialCapacity, context.Info.cEstimatedCharsPerLine);
        Text = new char[_capacity];
        Advances = new int[_capacity];
    }

    internal LoContext Context { get; }

    internal List<RunInfo> Runs { get; } = [];

    internal char[] Text { get; private set; }

    internal int[] Advances { get; private set; }

    internal int NumChars { get; set; }

    internal int EnsureCapacity(int required)
    {
        if (required <= _capacity)
        {
            return _capacity;
        }

        int next = Math.Max(required, _capacity * 2);
        char[] text = Text;
        int[] advances = Advances;
        Array.Resize(ref text, next);
        Array.Resize(ref advances, next);
        Text = text;
        Advances = advances;
        _capacity = next;
        return _capacity;
    }

    internal void AppendRun(int startCp, IntPtr run, int length, int width, bool isObject = false)
    {
        Runs.Add(new RunInfo(startCp, run, length, width, isObject));
    }

    /// <summary>Paragraph text flow (pap.lstflow), used when driving draw callbacks.</summary>
    internal LsTFlow Flow { get; set; }

    /// <summary>Exclusive end cp of the line (cpLimToContinue), carried by break records.</summary>
    internal int EndCp { get; set; }

    /// <summary>Paragraph options (pap.grpf) fetched when the line was formatted.</summary>
    internal LsPapOptions Grpf { get; set; }
}

internal readonly struct RunInfo
{
    internal RunInfo(int startCp, IntPtr run, int length, int width, bool isObject = false)
    {
        StartCp = startCp;
        Run = run;
        Length = length;
        Width = width;
        IsObject = isObject;
    }

    internal int StartCp { get; }

    internal IntPtr Run { get; }

    internal int Length { get; }

    internal int Width { get; }

    /// <summary>True when this run is an inline object (LsChp.idObj == InlineObject).</summary>
    internal bool IsObject { get; }
}

/// <summary>One fetched run chunk of the measured paragraph (candidate-line construction).</summary>
internal readonly struct ParagraphChunk
{
    internal ParagraphChunk(int startCp, IntPtr run, int length, int width, bool isObject = false)
    {
        StartCp = startCp;
        Run = run;
        Length = length;
        Width = width;
        IsObject = isObject;
    }

    internal int StartCp { get; }

    internal IntPtr Run { get; }

    internal int Length { get; }

    internal int Width { get; }

    internal bool IsObject { get; }
}

/// <summary>A paragraph breaking session (ploparabreak): column width + justification mode.</summary>
internal sealed class ParaBreakSession
{
    internal int CpParagraphFirst;
    internal int MaxWidth;
    internal bool Justified;

    /// <summary>Marshal.AllocHGlobal blocks published by the last LoCreateBreaks call.</summary>
    internal nint InfoArray;
    internal nint PenaltyArray;
    internal nint LineArray;
}

/// <summary>A break record: the line's end cp. The optimal breaker re-runs the DP from the
/// next line start, so no DP continuation state is carried in v1.</summary>
internal sealed class BreakRecord
{
    internal int BreakCp;
}
