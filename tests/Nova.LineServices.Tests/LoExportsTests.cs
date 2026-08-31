namespace Nova.LineServices.Tests;

[Collection("LineServices")]
public sealed class LoExportsTests
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
        LsErr err = LoExports.LoCreateContext(ref info, ref callbacks, out IntPtr ploc);
        Assert.Equal(LsErr.None, err);
        Assert.NotEqual(IntPtr.Zero, ploc);
        Assert.Equal(ploc, info.pols);
        return ploc;
    }

    [Fact]
    public void CreateDestroyContext_RoundTrips()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);

        Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoDestroyContext(ploc));
    }

    [Fact]
    public void CreateContext_MissingFetchPap_ReturnsInvalidParameter()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        LsContextInfo info = source.CreateContextInfo();
        info.pfnFetchPap = null;
        LscbkRedefined callbacks = source.CreateCallbacks();

        Assert.Equal(LsErr.InvalidParameter, LoExports.LoCreateContext(ref info, ref callbacks, out _));
    }

    [Fact]
    public void CreateContext_MissingFetchRun_ReturnsInvalidParameter()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        LsContextInfo info = source.CreateContextInfo();
        LscbkRedefined callbacks = source.CreateCallbacks();
        callbacks.pfnFetchRunRedefined = null;

        Assert.Equal(LsErr.InvalidParameter, LoExports.LoCreateContext(ref info, ref callbacks, out _));
    }

    [Fact]
    public void DestroyContext_WithLiveLine_ReturnsContextInUse()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);

        LsErr err = LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out IntPtr ploline, out _, out _);
        Assert.Equal(LsErr.None, err);

        Assert.Equal(LsErr.ContextInUse, LoExports.LoDestroyContext(ploc));
        Assert.Equal(LsErr.None, LoExports.LoDisposeLine(ploline, false));
        Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));
    }

    [Fact]
    public void SetDocBreakingTabs_ValidateContext()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        var dev = new LsDevRes { dxpInch = 1440, dypInch = 1440, dxrInch = 1440, dyrInch = 1440 };

        Assert.Equal(LsErr.None, LoExports.LoSetDoc(ploc, 1, 1, ref dev));
        Assert.Equal(LsErr.None, LoExports.LoSetBreaking(ploc, 1));
        unsafe
        {
            Assert.Equal(LsErr.None, LoExports.LoSetTabs(ploc, 480, 0, null));
        }

        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetDoc(IntPtr.Zero, 1, 1, ref dev));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetBreaking(IntPtr.Zero, 1));
        unsafe
        {
            Assert.Equal(LsErr.InvalidContext, LoExports.LoSetTabs(IntPtr.Zero, 480, 0, null));
        }

        _ = LoExports.LoDestroyContext(ploc);
    }

    [Fact]
    public unsafe void GetEscString_FillsSixNulTerminatedWchars()
    {
        var info = new EscStringInfo();
        LoExports.LoGetEscString(ref info);

        AssertWchar(info.szParaSeparator, '\u2029');
        AssertWchar(info.szLineSeparator, '\u2028');
        AssertWchar(info.szHidden, '\uFFFF');
        AssertWchar(info.szNbsp, '\u00A0');
        AssertWchar(info.szObjectTerminator, '\u0009');
        AssertWchar(info.szObjectReplacement, '\uFFFC');
    }

    [Fact]
    public void GetEscString_PinsAreStableAcrossCalls()
    {
        var first = new EscStringInfo();
        var second = new EscStringInfo();
        LoExports.LoGetEscString(ref first);
        LoExports.LoGetEscString(ref second);

        Assert.Equal(first.szParaSeparator, second.szParaSeparator);
        Assert.Equal(first.szLineSeparator, second.szLineSeparator);
        Assert.Equal(first.szHidden, second.szHidden);
        Assert.Equal(first.szNbsp, second.szNbsp);
        Assert.Equal(first.szObjectTerminator, second.szObjectTerminator);
        Assert.Equal(first.szObjectReplacement, second.szObjectReplacement);
    }

    [Fact]
    public void DisplayLine_IsReal_EnumLineIsReal()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        _ = LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out IntPtr ploline, out _, out _);
        var pt = new LSPOINT { x = 0, y = 0 };
        var clip = new LSRECT();

        // LoDisplayLine and LoEnumLine are real: without the draw/enumeration callbacks
        // installed (the fake source does not install them) both are no-op successes for a
        // valid line.
        Assert.Equal(LsErr.None, LoExports.LoDisplayLine(ploline, ref pt, 1, ref clip));
        Assert.Equal(LsErr.None, LoExports.LoEnumLine(ploline, false, false, ref pt));
        Assert.Equal(LsErr.InvalidLine, LoExports.LoDisplayLine(IntPtr.Zero, ref pt, 1, ref clip));
        Assert.Equal(LsErr.InvalidLine, LoExports.LoEnumLine(IntPtr.Zero, false, false, ref pt));

        _ = LoExports.LoDisposeLine(ploline, false);
        _ = LoExports.LoDestroyContext(ploc);
    }

    [Fact]
    public void QueryLineHitTesting_IsReal()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        _ = LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out IntPtr ploline, out _, out _);

        // Cp -> point: character 1 starts at x = 10 (after 'H'), spans 10 units.
        Assert.Equal(
            LsErr.None,
            LoExports.LoQueryLineCpPpoint(ploline, 1, 1, IntPtr.Zero, out int depth, out LsTextCell cell));
        Assert.Equal(0, depth);
        Assert.Equal(1, cell.lscpStartCell);
        Assert.Equal(1, cell.lscpEndCell);
        Assert.Equal(10, cell.pointUvStartCell.x);
        Assert.Equal(10, cell.dupCell);
        Assert.Equal(1, cell.cCharsInCell);

        // Point -> cp: x = 15 is inside character 1.
        var pt = new LSPOINT { x = 15, y = 0 };
        Assert.Equal(
            LsErr.None,
            LoExports.LoQueryLinePointPcp(ploline, ref pt, 1, IntPtr.Zero, out depth, out cell));
        Assert.Equal(1, cell.lscpStartCell);

        _ = LoExports.LoDisposeLine(ploline, false);
        _ = LoExports.LoDestroyContext(ploc);
    }


    [Fact]
    public void PenaltyModule_AcquireGetDispose_RoundTrips()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);

        Assert.Equal(LsErr.None, LoExports.LoAcquirePenaltyModule(ploc, out IntPtr module));
        Assert.NotEqual(IntPtr.Zero, module);
        Assert.Equal(LsErr.None, LoExports.LoGetPenaltyModuleInternalHandle(module, out IntPtr internalHandle));
        Assert.Equal(ploc, internalHandle);
        Assert.Equal(LsErr.None, LoExports.LoDisposePenaltyModule(module));
        Assert.Equal(LsErr.InvalidParameter, LoExports.LoDisposePenaltyModule(module));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoAcquirePenaltyModule(IntPtr.Zero, out _));

        _ = LoExports.LoDestroyContext(ploc);
    }

    [Fact]
    public void ObjectHandlerAndRelievePenalty_AreStubbed()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        _ = LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out IntPtr ploline, out _, out _);

        unsafe
        {
            Assert.Equal(LsErr.NotImplemented, LoExports.LocbkGetObjectHandlerInfo(ploc, 1, null));
        }

        Assert.Equal(LsErr.None, LoExports.LoRelievePenaltyResource(ploline));
        Assert.Equal(LsErr.InvalidLine, LoExports.LoRelievePenaltyResource(IntPtr.Zero));

        _ = LoExports.LoDisposeLine(ploline, false);
        _ = LoExports.LoDestroyContext(ploc);
    }

    [Fact]
    public unsafe void DWriteAnalysisHelpers_AreNotImplemented()
    {
        Assert.True(LoExports.CreateTextAnalysisSink() == null);
        Assert.True(LoExports.GetScriptAnalysisList(null) == null);
        Assert.True(LoExports.GetNumberSubstitutionList(null) == null);

        void* analysisSource = (void*)0x1234;
        int hr = LoExports.CreateTextAnalysisSource(null, 0, null, null, false, null, false, 0, &analysisSource);
        Assert.Equal(LoExports.ENotImplemented, hr);
        Assert.Equal(0, (nint)analysisSource);
    }

    private static unsafe void AssertWchar(nint pointer, char expected)
    {
        Assert.NotEqual(0, pointer);
        char* p = (char*)pointer;
        Assert.Equal(expected, p[0]);
        Assert.Equal('\0', p[1]);
    }
}
