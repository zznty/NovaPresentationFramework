namespace Nova.LineServices.Tests;

/// <summary>
/// Exact-value tests for the caret/hit-testing queries (<c>LoQueryLineCpPpoint</c> /
/// <c>LoQueryLinePointPcp</c>) and line enumeration (<c>LoEnumLine</c>): cell geometry, the
/// point→cp→point round-trip, RTL main-direction coordinates, and EnumText-driving with
/// accumulated run origins.
/// </summary>
[Collection("LineServices")]
public sealed class QueryEnumTests
{
    private static LsPap BreakingPap(LsTFlow flow = LsTFlow.ES)
    {
        return new LsPap
        {
            cpFirst = 0,
            cpFirstContent = 0,
            grpf = LsPapOptions.fFmiApplyBreakingRules | LsPapOptions.fFmiTreatHyphenAsRegular,
            lsbrj = LsBreakJust.lsbrjBreakJustify,
            lskj = LsKJust.lskjFullInterWord,
            lskeop = LsKEOP.EndPara1,
            lstflow = flow,
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
    public unsafe void CpToPoint_ReturnsExactCellGeometry()
    {
        // "Hello" with 'H'=20, 'e'=10, 'l'=5, 'o'=15: cell for each cp has exact origin+width.
        var source = new FakeTextSource(
            "Hello",
            ch => ch switch { 'H' => 20, 'e' => 10, 'l' => 5, 'o' => 15, _ => 1 },
            BreakingPap(),
            LineProps());
        IntPtr ploc = CreateContext(source);
        LsErr err = LoExports.LoCreateLine(
            ploc, 0, 5, 1000, 0, IntPtr.Zero,
            out _, out IntPtr ploline, out _, out _);
        Assert.Equal(LsErr.None, err);
        try
        {
            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 0, 1, IntPtr.Zero, out int depth, out LsTextCell cell));
            Assert.Equal(0, cell.lscpStartCell);
            Assert.Equal(0, cell.pointUvStartCell.x);
            Assert.Equal(20, cell.dupCell);

            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 1, 1, IntPtr.Zero, out depth, out cell));
            Assert.Equal(20, cell.pointUvStartCell.x);
            Assert.Equal(10, cell.dupCell);

            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 3, 1, IntPtr.Zero, out depth, out cell));
            Assert.Equal(35, cell.pointUvStartCell.x);
            Assert.Equal(5, cell.dupCell);
        }
        finally
        {
            _ = LoExports.LoDisposeLine(ploline, false);
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public unsafe void PointToCp_ReturnsExactCell_AndRoundTrips()
    {
        var source = new FakeTextSource(
            "Hello",
            ch => ch switch { 'H' => 20, 'e' => 10, 'l' => 5, 'o' => 15, _ => 1 },
            BreakingPap(),
            LineProps());
        IntPtr ploc = CreateContext(source);
        LsErr err = LoExports.LoCreateLine(
            ploc, 0, 5, 1000, 0, IntPtr.Zero,
            out _, out IntPtr ploline, out _, out _);
        Assert.Equal(LsErr.None, err);
        try
        {
            // Point inside each character's cell maps to that character.
            AssertPointToCp(ploline, 5, 0);
            AssertPointToCp(ploline, 25, 1);
            AssertPointToCp(ploline, 33, 2);
            AssertPointToCp(ploline, 55, 4);

            // Beyond the last character: trailing edge of the last cell.
            AssertPointToCp(ploline, 1000, 4);

            // Round-trip: point -> cp -> point returns the same cell origin for every boundary.
            foreach ((int x, int expectedCp) in new[] { (0, 0), (20, 1), (30, 2), (35, 3), (40, 4), (55, 4) })
            {
                var pt = new LSPOINT { x = x, y = 0 };
                Assert.Equal(LsErr.None, LoExports.LoQueryLinePointPcp(ploline, ref pt, 1, IntPtr.Zero, out _, out LsTextCell cell));
                Assert.Equal(expectedCp, cell.lscpStartCell);

                Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, cell.lscpStartCell, 1, IntPtr.Zero, out _, out LsTextCell back));
                Assert.Equal(cell.pointUvStartCell.x, back.pointUvStartCell.x);
                Assert.Equal(cell.dupCell, back.dupCell);
            }
        }
        finally
        {
            _ = LoExports.LoDisposeLine(ploline, false);
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    private static void AssertPointToCp(IntPtr ploline, int x, int expectedCp)
    {
        var pt = new LSPOINT { x = x, y = 0 };
        Assert.Equal(LsErr.None, LoExports.LoQueryLinePointPcp(ploline, ref pt, 1, IntPtr.Zero, out _, out LsTextCell cell));
        Assert.Equal(expectedCp, cell.lscpStartCell);
    }

    [Fact]
    public unsafe void Query_WithSubLineInfo_FillsOwningRun()
    {
        // Runs are "He" (cp 0-2), "ll" (cp 2-4), "o" (cp 4-5) via maxFetchLength=2, so the
        // subline run info is meaningful.
        var source = new FakeTextSource("Hello", _ => 10, BreakingPap(), LineProps(), maxFetchLength: 2);
        IntPtr ploc = CreateContext(source);
        LsErr err = LoExports.LoCreateLine(
            ploc, 0, 5, 1000, 0, IntPtr.Zero,
            out _, out IntPtr ploline, out _, out _);
        Assert.Equal(LsErr.None, err);
        try
        {
            // Query a cp in the second run ("ll"); sub-line 0 must name that run and its geometry.
            LsQSubInfo* sub = stackalloc LsQSubInfo[1];
            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 3, 1, (IntPtr)sub, out int depth, out LsTextCell cell));
            Assert.Equal(1, depth);
            Assert.Equal(LsTFlow.ES, sub[0].lstflowSubLine);
            Assert.Equal(5, sub[0].lsdcpSubLine);
            Assert.Equal(2, sub[0].lscpFirstRun);
            Assert.Equal(2, sub[0].lsdcpRun);
            Assert.Equal(20, sub[0].pointUvStartRun.x);
            Assert.Equal(20, sub[0].dupRun);
            Assert.Equal(30, cell.pointUvStartCell.x);
        }
        finally
        {
            _ = LoExports.LoDisposeLine(ploline, false);
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public unsafe void Query_RtlFlow_CoordinatesAreLogicalMainDirection()
    {
        // RTL: the first logical character is at x = 0 (main direction), widths accumulate in
        // logical order; the caller inverts for the paragraph direction.
        var source = new FakeTextSource("ab", _ => 10, BreakingPap(LsTFlow.WS), LineProps());
        IntPtr ploc = CreateContext(source);
        LsErr err = LoExports.LoCreateLine(
            ploc, 0, 2, 1000, 0, IntPtr.Zero,
            out _, out IntPtr ploline, out _, out _);
        Assert.Equal(LsErr.None, err);
        try
        {
            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 0, 1, IntPtr.Zero, out _, out LsTextCell cell0));
            Assert.Equal(0, cell0.pointUvStartCell.x);
            Assert.Equal(10, cell0.dupCell);

            Assert.Equal(LsErr.None, LoExports.LoQueryLineCpPpoint(ploline, 1, 1, IntPtr.Zero, out _, out LsTextCell cell1));
            Assert.Equal(10, cell1.pointUvStartCell.x);
            Assert.Equal(10, cell1.dupCell);
        }
        finally
        {
            _ = LoExports.LoDisposeLine(ploline, false);
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public unsafe void EnumLine_DrivesEnumText_PerRunWithAccumulatedOrigins()
    {
        // "He" + "llo" (maxFetchLength=2): two runs, EnumText called per run with the exact
        // cp range, text, advances, width, and accumulated origin.
        var source = new FakeTextSource(
            "Hello",
            ch => ch switch { 'H' => 20, 'e' => 10, 'l' => 5, 'o' => 15, _ => 1 },
            BreakingPap(),
            LineProps(),
            maxFetchLength: 2);

        List<(int CpFirst, int Dcp, int X, int Dup, string Text)> enums = [];
        LsErr Enum(IntPtr pols, Plsrun plsrun, int cpFirst, int dcp, char* pwchText, int cchText, LsTFlow lstFlow, int fReverseOrder, int fGeometryProvided, ref LSPOINT pptStart, ref LsHeights pheights, int dupRun, int glyphBaseRun, int* charWidths, ushort* pClusterMap, ushort* characterProperties, ushort* puglyphs, int* pGlyphAdvances, GlyphOffset* pGlyphOffsets, uint* pGlyphProperties, int glyphCount)
        {
            _ = pols;
            _ = plsrun;
            _ = lstFlow;
            _ = fReverseOrder;
            _ = fGeometryProvided;
            _ = pheights;
            _ = glyphBaseRun;
            _ = charWidths;
            _ = pClusterMap;
            _ = characterProperties;
            _ = puglyphs;
            _ = pGlyphAdvances;
            _ = pGlyphOffsets;
            _ = pGlyphProperties;
            _ = glyphCount;
            enums.Add((cpFirst, dcp, pptStart.x, dupRun, new string(pwchText, 0, cchText)));
            return LsErr.None;
        }

        LsContextInfo info = source.CreateContextInfo();
        info.pfnEnumText = Enum;
        LscbkRedefined callbacks = source.CreateCallbacks();
        Assert.Equal(LsErr.None, LoExports.LoCreateContext(ref info, ref callbacks, out IntPtr ploc));
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 5, 1000, 0, IntPtr.Zero,
                out _, out IntPtr ploline, out _, out _);
            Assert.Equal(LsErr.None, err);

            var pt = new LSPOINT { x = 0, y = 12 };
            Assert.Equal(LsErr.None, LoExports.LoEnumLine(ploline, false, false, ref pt));

            // "He" (30), "ll" (10), "o" (15): one EnumText per run, origins accumulate.
            Assert.Equal(3, enums.Count);
            Assert.Equal((0, 2, 0, 30, "He"), enums[0]);
            Assert.Equal((2, 2, 30, 10, "ll"), enums[1]);
            Assert.Equal((4, 1, 40, 15, "o"), enums[2]);

            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }
}
