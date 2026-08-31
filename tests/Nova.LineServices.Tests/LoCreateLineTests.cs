namespace Nova.LineServices.Tests;

[Collection("LineServices")]
public sealed class LoCreateLineTests
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

    private static LsPap PapWithoutBreakingRules()
    {
        LsPap pap = BreakingPap();
        pap.grpf &= ~LsPapOptions.fFmiApplyBreakingRules;
        return pap;
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
    public void CreateLine_UnknownContext_ReturnsInvalidContext()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());

        Assert.Equal(
            LsErr.InvalidContext,
            LoExports.LoCreateLine(IntPtr.Zero, 0, 2, 100, 0, IntPtr.Zero, out _, out _, out _, out _));
    }

    [Fact]
    public void DisposeLine_UnknownHandle_ReturnsInvalidLine()
    {
        Assert.Equal(LsErr.InvalidLine, LoExports.LoDisposeLine(IntPtr.Zero, false));
    }

    [Fact]
    public void CreateLine_WrapsAtWhitespace_WhenRunExceedsColumn()
    {
        var source = new FakeTextSource("Hello World", ch => ch == ' ' ? 4 : 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 11, 60, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out int maxDepth, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);
            Assert.Equal(1, maxDepth);
            Assert.Equal(LsEndRes.endrNormal, info.endr);
            Assert.Equal(6, info.cpLimToContinue);
            Assert.Equal(6, info.cpLimToStay);

            // "Hello " kept on the line (5 x 10 + 1 x 4); the trailing space is trimmed from the
            // trailing width but stays on the line; the next line starts at cp 6 ("World").
            Assert.Equal(54, widths.upLimLine);
            Assert.Equal(50, widths.upStartTrailing);
            Assert.Equal(54, widths.upMinLimLine);
            Assert.Equal(50, widths.upMinStartTrailing);

            // Callback-driven: FetchPap once, then fetch + widths for the run.
            Assert.Equal(1, source.FetchPapCalls);
            Assert.True(source.FetchRunCalls >= 1);
            Assert.True(source.WidthCalls >= 1);

            // Heights accumulate presentation and reference metrics.
            Assert.Equal(10, info.dvpAscent);
            Assert.Equal(2, info.dvpDescent);
            Assert.Equal(12, info.dvpMultiLineHeight);
            Assert.Equal(10, info.dvrAscent);
            Assert.True(source.MetricsCalls >= 2);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_AppliesLineBreakAcrossRuns_WhenSpaceIsInEarlierRun()
    {
        var source = new FakeTextSource("Hello World", ch => ch == ' ' ? 4 : 10, BreakingPap(), LineProps(), maxFetchLength: 6);
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 11, 60, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);

            // Fetch 1 ("Hello ", fits) is appended; fetch 2 ("World") overflows with no space in
            // its window, so the break is applied across the accumulated line.
            Assert.Equal(2, source.FetchRunCalls);
            Assert.Equal(6, info.cpLimToContinue);
            Assert.Equal(54, widths.upLimLine);
            Assert.Equal(50, widths.upStartTrailing);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_WhenTextFits_ProducesCompleteLine()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 2, 100, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);
            Assert.Equal(LsEndRes.endrEndPara, info.endr);
            Assert.Equal(2, info.cpLimToContinue);
            Assert.Equal(20, widths.upLimLine);
            Assert.Equal(20, widths.upStartTrailing);
            Assert.Equal(10, info.dvpAscent);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_WithoutBreakingRules_CoversFullTextEvenPastColumn()
    {
        var source = new FakeTextSource("Hello World", ch => ch == ' ' ? 4 : 10, PapWithoutBreakingRules(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 11, 60, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);

            // No breaking rules: the width cap still splits runs, but every character lands on
            // the line and the full text is covered.
            Assert.Equal(LsEndRes.endrEndPara, info.endr);
            Assert.Equal(11, info.cpLimToContinue);
            Assert.Equal(104, widths.upLimLine);
            Assert.True(source.FetchRunCalls >= 6);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_GrowsBuffer_WhenFetchExceedsEstimatedCapacity()
    {
        const string text = "Hello World, this run exceeds the estimated line capacity";
        var source = new FakeTextSource(text, _ => 10, BreakingPap(), LineProps())
        {
            EstimatedCharsPerLine = 4,
        };
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, text.Length, 10000, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);

            // Too-small, grown retry, then a terminal empty fetch that ends the line.
            Assert.Equal(3, source.FetchRunCalls);
            Assert.Equal(text.Length, info.cpLimToContinue);
            Assert.Equal(text.Length * 10, widths.upLimLine);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_StopsAtParaSeparator_ReturnedAsDirectPointer()
    {
        var source = new FakeTextSource("Hello", _ => 10, BreakingPap(), LineProps(), directPointerEnd: '\u2029');
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 5, 100, 0, IntPtr.Zero,
                out LsLInfo info, out IntPtr ploline, out _, out LsLineWidths widths);

            Assert.Equal(LsErr.None, err);
            Assert.NotEqual(IntPtr.Zero, ploline);
            Assert.Equal(LsEndRes.endrEndPara, info.endr);

            // The separator is consumed as the line-ending newline: WPF derives the line's
            // character range from cpLimToContinue, and Line.EndOfParagraph requires the
            // TextEndOfParagraph run to fall inside that range (otherwise the line loop
            // re-formats at the separator and CountText asserts "Zero-length text line!").
            Assert.Equal(6, info.cpLimToContinue);
            Assert.Equal(50, widths.upLimLine);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void CreateLine_AutoNumberingParagraph_ReturnsNotImplemented()
    {
        LsPap pap = BreakingPap();
        pap.grpf |= LsPapOptions.fFmiAnm;
        var source = new FakeTextSource("Hi", _ => 10, pap, LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            Assert.Equal(
                LsErr.NotImplemented,
                LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out _, out _, out _));
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }
}
