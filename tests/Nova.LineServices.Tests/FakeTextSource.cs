using System.Runtime.InteropServices;

namespace Nova.LineServices.Tests;

/// <summary>
/// Scriptable LS callback source for the engine tests. Fetches the text in run-length-limited
/// chunks into the engine's buffer (WPF-style <c>FetchRunRedefined</c>), returns an optional
/// direct pointer for the paragraph separator, and reports per-character widths that include at
/// most one character that does not fully fit (the WPF nest contract).
/// <para>
/// Text runs are reported with <c>LsChp.idObj == 0xffff</c> (<c>TextStore.ObjectId.Text_chp</c>),
/// mirroring the real WPF callback. <see cref="LeadingReverseRuns"/> models bidi open-reverse
/// markers: each occupies one LSCP (the fetch position) but zero client characters, so the
/// engine's lscp/cp split is exercised. <see cref="ObjectWidths"/> maps a client character
/// position to an inline-object width; the object occupies one cp and one lscp and is reported
/// with <c>idObj == 1</c> (<c>InlineObject</c>).
/// </para>
/// </summary>
internal sealed class FakeTextSource
{
    private const ushort IdObjReverse = 0;
    private const ushort IdObjInlineObject = 1;
    private const ushort IdObjTextChp = 0xffff;

    private static readonly IntPtr ReversePlsrun = new(0x40000002); // IsMarker | Reverse, never a content run
    private static readonly IntPtr ObjectPlsrun = new(0x40000006); // IsMarker | InlineObject, never a content run
    private static readonly char[] ReverseMarkerText = ['\uFFFC'];
    private static readonly char[] ObjectMarkerText = ['\uFFFC'];

    private readonly char[] _text;
    private readonly Func<char, int> _widthOf;
    private readonly LsPap _pap;
    private readonly LsLineProps _lineProps;
    private readonly int _maxFetchLength;
    private readonly GCHandle _textPin;
    private readonly GCHandle _directPin;
    private readonly GCHandle _reversePin;
    private readonly GCHandle _objectPin;

    private int _currentObjectWidth;

    internal FakeTextSource(
        string text,
        Func<char, int> widthOf,
        LsPap pap,
        LsLineProps lineProps,
        int maxFetchLength = int.MaxValue,
        char? directPointerEnd = null)
    {
        _text = text.ToCharArray();
        _widthOf = widthOf;
        _pap = pap;
        _lineProps = lineProps;
        _maxFetchLength = maxFetchLength;
        _textPin = GCHandle.Alloc(_text, GCHandleType.Pinned);
        if (directPointerEnd is char direct)
        {
            _directPin = GCHandle.Alloc(new[] { direct }, GCHandleType.Pinned);
        }

        _reversePin = GCHandle.Alloc(ReverseMarkerText, GCHandleType.Pinned);
        _objectPin = GCHandle.Alloc(ObjectMarkerText, GCHandleType.Pinned);
    }

    internal int EstimatedCharsPerLine { get; set; } = 64;

    /// <summary>Number of leading bidi reverse-marker runs (each occupies 1 LSCP, 0 client cp).</summary>
    internal int LeadingReverseRuns { get; set; }

    /// <summary>Client cp to inline-object width; each object occupies 1 cp and 1 LSCP.</summary>
    internal Dictionary<int, int>? ObjectWidths { get; set; }

    internal int FetchPapCalls { get; private set; }

    internal int FetchLinePropsCalls { get; private set; }

    internal int FetchRunCalls { get; private set; }

    internal int WidthCalls { get; private set; }

    internal int MetricsCalls { get; private set; }

    internal unsafe LsContextInfo CreateContextInfo()
    {
        return new LsContextInfo
        {
            version = 4,
            cEstimatedCharsPerLine = EstimatedCharsPerLine,
            cJustPriorityLim = 3,
            wchNull = '\u0000',
            wchUndef = '\u0001',
            wchTab = '\u0009',
            wchEndPara1 = '\u2029',
            wchEndLineInPara = '\u2028',
            wchSpace = '\u0020',
            wchNonBreakSpace = '\u00A0',
            pfnFetchPap = FetchPap,
            pfnFetchLineProps = FetchLineProps,
            pfnGetRunCharWidths = GetRunCharWidths,
            pfnGetRunTextMetrics = GetRunTextMetrics,
        };
    }

    internal unsafe LscbkRedefined CreateCallbacks()
    {
        return new LscbkRedefined
        {
            pfnFetchRunRedefined = FetchRun,
        };
    }

    private LsErr FetchPap(IntPtr pols, int lscpFetch, ref LsPap lspap)
    {
        _ = pols;
        _ = lscpFetch;
        FetchPapCalls++;
        lspap = _pap;
        return LsErr.None;
    }

    private LsErr FetchLineProps(IntPtr pols, int lscpFetch, int firstLineInPara, ref LsLineProps lsLineProps)
    {
        _ = pols;
        _ = lscpFetch;
        _ = firstLineInPara;
        FetchLinePropsCalls++;
        lsLineProps = _lineProps;
        return LsErr.None;
    }

    private unsafe LsErr FetchRun(
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
        ref IntPtr lsplsrun)
    {
        _ = pols;
        _ = fIsStyle;
        _ = pstyle;
        FetchRunCalls++;

        if (lscpFetch < LeadingReverseRuns)
        {
            // Bidi open-reverse marker: one LSCP, zero client cp, zero width.
            fIsBufferUsed = 0;
            pwchText = (char*)_reversePin.AddrOfPinnedObject();
            cchText = 1;
            fIsHidden = 0;
            lschp = new LsChp { idObj = IdObjReverse };
            lsplsrun = ReversePlsrun;
            return LsErr.None;
        }

        int textCp = lscpFetch - LeadingReverseRuns;
        if (ObjectWidths != null && ObjectWidths.TryGetValue(textCp, out int objectWidth))
        {
            // Inline object: one client cp, one LSCP; width reported through GetRunCharWidths.
            fIsBufferUsed = 0;
            pwchText = (char*)_objectPin.AddrOfPinnedObject();
            cchText = 1;
            fIsHidden = 0;
            lschp = new LsChp { idObj = IdObjInlineObject };
            lsplsrun = ObjectPlsrun;
            _currentObjectWidth = objectWidth;
            return LsErr.None;
        }

        if (textCp >= _text.Length)
        {
            if (_directPin.IsAllocated)
            {
                fIsBufferUsed = 0;
                pwchText = (char*)_directPin.AddrOfPinnedObject();
                cchText = 1;
                fIsHidden = 0;
                lschp = new LsChp { idObj = IdObjTextChp };
                lsplsrun = IntPtr.Zero;
                return LsErr.None;
            }

            fIsBufferUsed = 0;
            pwchText = null;
            cchText = 0;
            fIsHidden = 0;
            lschp = default;
            lsplsrun = IntPtr.Zero;
            return LsErr.None;
        }

        int len = Math.Min(_maxFetchLength, _text.Length - textCp);
        if (ObjectWidths != null)
        {
            // Stop the text run before the next inline object so the object is fetched as its
            // own run (one cp, one lscp).
            foreach (KeyValuePair<int, int> objectRun in ObjectWidths)
            {
                int distance = objectRun.Key - textCp;
                if (distance > 0 && distance < len)
                {
                    len = distance;
                }
            }
        }

        if (len > cchTextBuffer)
        {
            // Buffer too small: report the required length, do not use the buffer.
            fIsBufferUsed = 0;
            pwchText = null;
            cchText = len;
            fIsHidden = 0;
            lschp = default;
            lsplsrun = IntPtr.Zero;
            return LsErr.None;
        }

        fIsBufferUsed = 1;
        cchText = len;
        fixed (char* pText = _text)
        {
            for (int i = 0; i < len; i++)
            {
                pwchTextBuffer[i] = pText[textCp + i];
            }
        }

        fIsHidden = 0;
        lschp = new LsChp { idObj = IdObjTextChp };
        lsplsrun = 1 + textCp;
        pwchText = pwchTextBuffer;
        return LsErr.None;
    }

    private unsafe LsErr GetRunCharWidths(
        IntPtr pols,
        Plsrun plsrun,
        LsDevice device,
        char* runText,
        int cchRun,
        int maxWidth,
        LsTFlow textFlow,
        int* charWidths,
        ref int totalWidth,
        ref int cchProcessed)
    {
        _ = pols;
        _ = device;
        _ = textFlow;
        WidthCalls++;

        if (plsrun == (Plsrun)ObjectPlsrun)
        {
            // Inline object: report its configured width.
            charWidths[0] = _currentObjectWidth;
            totalWidth = _currentObjectWidth;
            cchProcessed = 1;
            return LsErr.None;
        }

        totalWidth = 0;
        int i = 0;
        while (i < cchRun)
        {
            int width = _widthOf(runText[i]);
            if (totalWidth + width > maxWidth)
            {
                break;
            }

            charWidths[i] = width;
            totalWidth += width;
            i++;
        }

        if (i < cchRun)
        {
            // Include at most one character that does not fully fit.
            charWidths[i] = _widthOf(runText[i]);
            totalWidth += charWidths[i];
            i++;
        }

        cchProcessed = i;
        return LsErr.None;
    }

    private LsErr GetRunTextMetrics(IntPtr pols, Plsrun plsrun, LsDevice lsDevice, LsTFlow lstFlow, ref LsTxM lstTextMetrics)
    {
        _ = pols;
        _ = plsrun;
        _ = lsDevice;
        _ = lstFlow;
        MetricsCalls++;
        lstTextMetrics = new LsTxM { dvAscent = 10, dvDescent = 2, dvMultiLineHeight = 12, fMonospaced = 0 };
        return LsErr.None;
    }
}
