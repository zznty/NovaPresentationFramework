
namespace Nova.LineServices.Tests;

[Collection("LineServices")]
public sealed class OptimalBreakingTests
{
    /// <summary>pap with breaking rules + the justification mode.</summary>
    private static LsPap Pap(bool justified)
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
            fJustify = justified ? 1 : 0,
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
    public void CreateBreaks_Justified_OptimalBreakOutperformsGreedyLastFit()
    {
        // Words 40/40/100/40 with 10-unit spaces in a 95-unit column, justified
        // (stretch = space width, shrink = half). Greedy last-fit breaks after the
        // first space (cp 50, badness-capped underfull r=4.5). Knuth-Plass leaves
        // "w1 w2 " on line 1 (cp 100): natural 100, shrink 10 -> r = -0.5 (badness
        // 12.5), so the remaining 150-unit tail is the (free) last line instead of
        // an overfull cascade. The DP wins 156 demerits vs greedy's 1e8+.
        var source = new FakeTextSource(
            "aaaa bbbb cccccccccc dddd",
            _ => 10,
            Pap(justified: true),
            LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            IntPtr pploparabreak = IntPtr.Zero;
            bool justified = false;
            Assert.Equal(LsErr.None, LoExports.LoCreateParaBreakingSession(ploc, 0, 95, IntPtr.Zero, ref pploparabreak, ref justified));
            Assert.True(justified);

            unsafe
            {
                LsBreaks breaks = default;
                Assert.Equal(LsErr.None, LoExports.LoCreateBreaks(
                    ploc, 0, IntPtr.Zero, pploparabreak, IntPtr.Zero, ref breaks, out int bestFitIndex));

                Assert.Equal(2, breaks.cBreaks);
                // Candidate offsets: after the first space (cp 5) and after the second (cp 10).
                Assert.Equal(5, breaks.plslinfoArray[0].cpLimToContinue);
                Assert.Equal(10, breaks.plslinfoArray[1].cpLimToContinue);
                Assert.Equal(LsEndRes.endrNormal, breaks.plslinfoArray[0].endr);
                Assert.Equal(LsEndRes.endrNormal, breaks.plslinfoArray[1].endr);

                // The optimal first line is "aaaa bbbb " (cp 10), not greedy's "aaaa " (cp 5).
                Assert.Equal(1, bestFitIndex);

                for (int i = 0; i < breaks.cBreaks; i++)
                {
                    Assert.NotEqual(IntPtr.Zero, breaks.pplolineArray[i]);
                    Assert.Equal(LsErr.None, LoExports.LoDisposeLine(breaks.pplolineArray[i], false));
                }
            }

            // Greedy reference: the same paragraph formatted as one line stops at cp 5.
            Assert.Equal(LsErr.None, LoExports.LoCreateLine(
                ploc, 0, 25, 95, 0, IntPtr.Zero,
                out LsLInfo greedyInfo, out IntPtr greedyLine, out _, out _));
            Assert.Equal(5, greedyInfo.cpLimToContinue);
            Assert.Equal(LsErr.None, LoExports.LoDisposeLine(greedyLine, false));

            Assert.Equal(LsErr.None, LoExports.LoDisposeParaBreakingSession(pploparabreak, false));
        }
        finally
        {
            Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
        }
    }

    [Fact]
    public void CreateBreaks_Ragged_OptimalEqualsLastFit()
    {
        // Ragged text has no shrink, so every feasible line must fit naturally;
        // the DP's stretch sentinel still prefers fuller lines, and the single
        // fitting candidate is the only choice.
        var source = new FakeTextSource(
            "aaaa bbbb",
            _ => 10,
            Pap(justified: false),
            LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            IntPtr pploparabreak = IntPtr.Zero;
            bool justified = true;
            Assert.Equal(LsErr.None, LoExports.LoCreateParaBreakingSession(ploc, 0, 50, IntPtr.Zero, ref pploparabreak, ref justified));
            Assert.False(justified);

            unsafe
            {
                LsBreaks breaks = default;
                Assert.Equal(LsErr.None, LoExports.LoCreateBreaks(
                    ploc, 0, IntPtr.Zero, pploparabreak, IntPtr.Zero, ref breaks, out int bestFitIndex));

                // "aaaa " (5 chars) fits; "aaaa bbbb " (9 chars) does not. Ragged: one candidate.
                Assert.Equal(1, breaks.cBreaks);
                Assert.Equal(5, breaks.plslinfoArray[0].cpLimToContinue);
                Assert.Equal(0, bestFitIndex);
                Assert.NotEqual(IntPtr.Zero, breaks.pplolineArray[0]);
                Assert.Equal(LsErr.None, LoExports.LoDisposeLine(breaks.pplolineArray[0], false));
            }

            Assert.Equal(LsErr.None, LoExports.LoDisposeParaBreakingSession(pploparabreak, false));
        }
        finally
        {
            Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
        }
    }

    [Fact]
    public void CreateBreaks_WholeParagraphFits_EndCandidateCarriesEndPara()
    {
        // A paragraph shorter than the column: the end break is a candidate, the
        // DP prefers breaking at the first opportunity over the single-line
        // solution, and the end candidate reports endrEndPara.
        var source = new FakeTextSource(
            "aaaa bbbb",
            _ => 10,
            Pap(justified: false),
            LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            IntPtr pploparabreak = IntPtr.Zero;
            bool justified = true;
            Assert.Equal(LsErr.None, LoExports.LoCreateParaBreakingSession(ploc, 0, 500, IntPtr.Zero, ref pploparabreak, ref justified));

            unsafe
            {
                LsBreaks breaks = default;
                Assert.Equal(LsErr.None, LoExports.LoCreateBreaks(
                    ploc, 0, IntPtr.Zero, pploparabreak, IntPtr.Zero, ref breaks, out int bestFitIndex));

                Assert.Equal(2, breaks.cBreaks);
                Assert.Equal(5, breaks.plslinfoArray[0].cpLimToContinue);
                Assert.Equal(9, breaks.plslinfoArray[1].cpLimToContinue);
                Assert.Equal(LsEndRes.endrNormal, breaks.plslinfoArray[0].endr);
                Assert.Equal(LsEndRes.endrEndPara, breaks.plslinfoArray[1].endr);
                Assert.Equal(0, bestFitIndex);

                for (int i = 0; i < breaks.cBreaks; i++)
                {
                    Assert.Equal(LsErr.None, LoExports.LoDisposeLine(breaks.pplolineArray[i], false));
                }
            }

            Assert.Equal(LsErr.None, LoExports.LoDisposeParaBreakingSession(pploparabreak, false));
        }
        finally
        {
            Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
        }
    }

    [Fact]
    public void BreakRecords_AcquireCloneDispose_RoundTrip()
    {
        var source = new FakeTextSource("aaaa bbbb", c => 10, Pap(justified: false), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            Assert.Equal(LsErr.None, LoExports.LoCreateLine(
                ploc, 0, 10, 50, 0, IntPtr.Zero,
                out _, out IntPtr line, out _, out _));

            Assert.Equal(LsErr.None, LoExports.LoAcquireBreakRecord(line, out IntPtr record));
            Assert.NotEqual(IntPtr.Zero, record);

            Assert.Equal(LsErr.None, LoExports.LoCloneBreakRecord(record, out IntPtr clone));
            Assert.NotEqual(IntPtr.Zero, clone);
            Assert.NotEqual(record, clone);

            Assert.Equal(LsErr.None, LoExports.LoDisposeBreakRecord(clone, false));
            Assert.Equal(LsErr.None, LoExports.LoDisposeBreakRecord(record, false));
            // Disposing again must report the dead handle, not corrupt state.
            Assert.Equal(LsErr.InvalidParameter, LoExports.LoDisposeBreakRecord(record, false));

            Assert.Equal(LsErr.None, LoExports.LoDisposeLine(line, false));
        }
        finally
        {
            Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
        }
    }
}
