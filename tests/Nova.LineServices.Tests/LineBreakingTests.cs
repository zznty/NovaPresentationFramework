namespace Nova.LineServices.Tests;

[Collection("LineServices")]
public sealed class LineBreakingTests
{
    private static LsPap BreakingPap()
    {
        return new LsPap
        {
            cpFirst = 0,
            cpFirstContent = 0,
            grpf = LsPapOptions.fFmiApplyBreakingRules | LsPapOptions.fFmiTreatHyphenAsRegular,
            lsbrj = LsBreakJust.lsbrjBreakJustify,
            lskj = LsKJust.lskjFullInterWord,
            lskeop = LsKEOP.EndPara1,
            lstflow = LsTFlow.ES,
        };
    }

    private static LsLineProps LineProps()
    {
        return new LsLineProps
        {
            lskal = LsKAlign.lskalLeft,
            durLeft = 0,
            durRightBreak = 1000,
            durRightJustify = 1000,
            durHyphenationZone = 0,
        };
    }

    private static IntPtr CreateContext(FakeTextSource source)
    {
        LsContextInfo info = source.CreateContextInfo();
        LscbkRedefined callbacks = source.CreateCallbacks();
        Assert.Equal(LsErr.None, LoExports.LoCreateContext(ref info, ref callbacks, out IntPtr ploc));
        return ploc;
    }

    [Fact]
    public void CreateLine_CjkText_WrapsBetweenIdeographs_AtColumnWidth()
    {
        var source = new FakeTextSource("甲乙丙丁", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            // Line 1: "甲乙" (20) fits the 25-unit column; the third ideograph would overflow, so
            // the break falls between ideographs (BreakCJK strategy).
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 4, 25, 0, IntPtr.Zero,
                out LsLInfo info1, out IntPtr line1, out _, out LsLineWidths widths1);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, line1);
            Assert.Equal(LsEndRes.endrNormal, info1.endr);
            Assert.Equal(2, info1.cpLimToContinue);
            Assert.Equal(20, widths1.upLimLine);
            _ = LoExports.LoDisposeLine(line1, false);

            // Line 2: the remainder "丙丁" (20) fits and completes the paragraph.
            err = LoExports.LoCreateLine(
                ploc, 2, 4, 25, 0, IntPtr.Zero,
                out LsLInfo info2, out IntPtr line2, out _, out LsLineWidths widths2);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(LsEndRes.endrEndPara, info2.endr);
            Assert.Equal(4, info2.cpLimToContinue);
            Assert.Equal(20, widths2.upLimLine);
            _ = LoExports.LoDisposeLine(line2, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_RtlParagraph_WithLeadingReverseMarker_DoesNotAbort()
    {
        LsPap pap = BreakingPap();
        pap.lstflow = LsTFlow.WS; // WPF FetchPap: RightToLeft ? lstflowWS : lstflowES

        var source = new FakeTextSource("שלום", _ => 10, pap, LineProps())
        {
            LeadingReverseRuns = 1,
        };
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 4, 100, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);
            Assert.Equal(LsEndRes.endrEndPara, info.endr);

            // The reverse marker occupies one LSCP but zero client characters: the line consumes
            // the four Hebrew characters and nothing else.
            Assert.Equal(4, info.cpLimToContinue);
            Assert.Equal(40, widths.upLimLine);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_MixedDirectionParagraph_WithReverseMarkerMidRun_AdvancesCp()
    {
        LsPap pap = BreakingPap();
        pap.lstflow = LsTFlow.WS;

        // One leading reverse marker before an LTR/RTL mix; the engine must keep cp aligned with
        // the text even though the marker consumed an LSCP.
        var source = new FakeTextSource("aשb", _ => 10, pap, LineProps())
        {
            LeadingReverseRuns = 1,
        };
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 3, 100, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(3, info.cpLimToContinue);
            Assert.Equal(30, widths.upLimLine);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_InlineObject_MeasuresWidthAndConsumesCp()
    {
        // "a" (10) + inline object (25) + "b" (10) = 45; the object occupies its own cp and is
        // fetched as a distinct run between the text runs.
        var source = new FakeTextSource("a\uFFFCb", _ => 10, BreakingPap(), LineProps())
        {
            ObjectWidths = new Dictionary<int, int> { [1] = 25 },
        };
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 3, 100, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);
            Assert.Equal(LsEndRes.endrEndPara, info.endr);
            Assert.Equal(3, info.cpLimToContinue);
            Assert.Equal(45, widths.upLimLine);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_InlineObject_WrapsAfterObject_WhenLineOverflows()
    {
        // "aa" (20) + object (10) + "b" (10): the object lands on line 1, "b" wraps to line 2.
        var source = new FakeTextSource("aa\uFFFCb", _ => 10, BreakingPap(), LineProps())
        {
            ObjectWidths = new Dictionary<int, int> { [2] = 10 },
        };
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 4, 30, 0, IntPtr.Zero,
                out LsLInfo info1, out IntPtr line1, out _, out LsLineWidths widths1);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(LsEndRes.endrNormal, info1.endr);
            Assert.Equal(3, info1.cpLimToContinue);
            Assert.Equal(30, widths1.upLimLine);
            _ = LoExports.LoDisposeLine(line1, false);

            err = LoExports.LoCreateLine(
                ploc, 3, 4, 30, 0, IntPtr.Zero,
                out LsLInfo info2, out IntPtr line2, out _, out LsLineWidths widths2);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(LsEndRes.endrEndPara, info2.endr);
            Assert.Equal(4, info2.cpLimToContinue);
            Assert.Equal(10, widths2.upLimLine);
            _ = LoExports.LoDisposeLine(line2, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public unsafe void DisplayLine_DrivesDrawTextRun_PerRunWithAccumulatedOrigins()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        List<(Plsrun Run, int X, int Dup)> drawn = [];
        LsErr Draw(IntPtr pols, Plsrun plsrun, ref LSPOINT ptText, char* runText, int* charWidths, int cchText, LsTFlow textFlow, uint displayMode, ref LSPOINT ptRun, ref LsHeights heights, int dupRun, ref LSRECT clipRect)
        {
            _ = pols;
            _ = runText;
            _ = charWidths;
            _ = cchText;
            _ = textFlow;
            _ = displayMode;
            _ = ptRun;
            _ = heights;
            _ = clipRect;
            drawn.Add((plsrun, ptText.x, dupRun));
            return LsErr.None;
        }

        LsContextInfo info = source.CreateContextInfo();
        info.pfnDrawTextRun = Draw;
        LscbkRedefined callbacks = source.CreateCallbacks();
        Assert.Equal(LsErr.None, LoExports.LoCreateContext(ref info, ref callbacks, out IntPtr ploc));
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 2, 100, 0, IntPtr.Zero,
                out _, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(20, widths.upLimLine);

            var pt = new LSPOINT { x = 0, y = 12 };
            var clip = new LSRECT();
            Assert.Equal(LsErr.None, LoExports.LoDisplayLine(ploline, ref pt, 1, ref clip));

            // One draw call per run, origins accumulate left-to-right, y carries the baseline.
            (_, int runX, int runDup) = Assert.Single(drawn);
            Assert.Equal(0, runX);
            Assert.Equal(20, runDup);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public unsafe void DisplayLine_RtlFlow_StartsOriginsAtRightEdge()
    {
        LsPap pap = BreakingPap();
        pap.lstflow = LsTFlow.WS;

        var source = new FakeTextSource("ab", _ => 10, pap, LineProps());
        List<int> origins = [];
        LsErr Draw(IntPtr pols, Plsrun plsrun, ref LSPOINT ptText, char* runText, int* charWidths, int cchText, LsTFlow textFlow, uint displayMode, ref LSPOINT ptRun, ref LsHeights heights, int dupRun, ref LSRECT clipRect)
        {
            _ = pols;
            _ = plsrun;
            _ = runText;
            _ = charWidths;
            _ = cchText;
            _ = textFlow;
            _ = displayMode;
            _ = ptRun;
            _ = heights;
            _ = dupRun;
            _ = clipRect;
            origins.Add(ptText.x);
            return LsErr.None;
        }

        LsContextInfo info = source.CreateContextInfo();
        info.pfnDrawTextRun = Draw;
        LscbkRedefined callbacks = source.CreateCallbacks();
        Assert.Equal(LsErr.None, LoExports.LoCreateContext(ref info, ref callbacks, out IntPtr ploc));
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 2, 100, 0, IntPtr.Zero,
                out _, out IntPtr ploline, out _, out _);

            Assert.Equal(LsErr.None, err);

            var pt = new LSPOINT { x = 0, y = 12 };
            var clip = new LSRECT();
            Assert.Equal(LsErr.None, LoExports.LoDisplayLine(ploline, ref pt, 1, ref clip));

            // RTL: the single two-character run draws at the right edge (total width).
            int origin = Assert.Single(origins);
            Assert.Equal(20, origin);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }
}
