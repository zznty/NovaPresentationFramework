using System.Runtime.InteropServices;

namespace Nova.LineServices.Tests;

/// <summary>
/// Handle-table contract for the GCHandle-backed handle storage: handles are live GCHandle
/// pointers (round-trippable through GCHandle.FromIntPtr(...).Target), and release frees the
/// handle so a freed handle is rejected on every subsequent use.
/// <para>
/// Shares the <c>LineServices</c> xunit collection with the other test classes: GCHandle slots
/// are process-wide, and a freed slot can be reused by a concurrent allocation in another
/// class, which would make freed-handle rejection non-deterministic. Serializing the classes
/// keeps the freed-handle assertions deterministic.
/// </para>
/// </summary>
[Collection("LineServices")]
public sealed class GCHandleTests
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
    public void ContextHandle_IsALiveGchandle_RoundTripsThroughTarget()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            // The returned handle is a real GCHandle pointer: it round-trips through Target and
            // is allocated until LoDestroyContext frees it.
            GCHandle handle = GCHandle.FromIntPtr(ploc);
            Assert.True(handle.IsAllocated);
            Assert.NotNull(handle.Target);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }

        // After release the engine rejects the freed handle (the serialized collection keeps the
        // freed slot from being reused between the free and this assertion).
        var dev = new LsDevRes();
        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetDoc(ploc, 1, 1, ref dev));
    }

    [Fact]
    public void LineHandle_IsALiveGchandle_RoundTripsThroughTarget()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 2, 100, 0, IntPtr.Zero,
                out _, out IntPtr ploline, out _, out _);

            Assert.Equal(LsErr.None, err);
            GCHandle handle = GCHandle.FromIntPtr(ploline);
            Assert.True(handle.IsAllocated);
            Assert.NotNull(handle.Target);
            _ = LoExports.LoDisposeLine(ploline, false);
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void FreedContextHandle_IsRejectedOnEveryUse()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));

        // A freed GCHandle is not silently accepted: every context-taking export rejects it.
        LsDevRes dev = default;
        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetDoc(ploc, 1, 1, ref dev));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetBreaking(ploc, 0));
        Assert.Equal(
            LsErr.InvalidContext,
            LoExports.LoCreateLine(ploc, 0, 2, 100, 0, IntPtr.Zero, out _, out _, out _, out _));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoAcquirePenaltyModule(ploc, out _));
    }

    [Fact]
    public void FreedLineHandle_IsRejectedOnEveryUse()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            LsErr err = LoExports.LoCreateLine(
                ploc, 0, 2, 100, 0, IntPtr.Zero,
                out _, out IntPtr ploline, out _, out _);

            Assert.Equal(LsErr.None, err);
            Assert.Equal(LsErr.None, LoExports.LoDisposeLine(ploline, false));

            // The freed handle is rejected: line-consuming exports return InvalidLine.
            var pt = new LSPOINT();
            var clip = new LSRECT();
            Assert.Equal(LsErr.InvalidLine, LoExports.LoDisplayLine(ploline, ref pt, 1, ref clip));
            Assert.Equal(LsErr.InvalidLine, LoExports.LoEnumLine(ploline, false, false, ref pt));
            Assert.Equal(LsErr.InvalidLine, LoExports.LoAcquireBreakRecord(ploline, out _));
            Assert.Equal(LsErr.InvalidLine, LoExports.LoDisposeLine(ploline, false));
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }

    [Fact]
    public void ContextDestroy_WithLiveLine_ThenDispose_ThenDestroy()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        LsErr err = LoExports.LoCreateLine(
            ploc, 0, 2, 100, 0, IntPtr.Zero,
            out _, out IntPtr ploline, out _, out _);

        Assert.Equal(LsErr.None, err);
        Assert.Equal(LsErr.ContextInUse, LoExports.LoDestroyContext(ploc));

        Assert.Equal(LsErr.None, LoExports.LoDisposeLine(ploline, false));
        Assert.Equal(LsErr.None, LoExports.LoDestroyContext(ploc));

        // Destroyed context + freed line: both rejected.
        Assert.Equal(LsErr.InvalidLine, LoExports.LoDisposeLine(ploline, false));
        Assert.Equal(LsErr.InvalidContext, LoExports.LoSetBreaking(ploc, 0));
    }

    [Fact]
    public void PenaltyModule_HandleIsFreedOnDispose()
    {
        var source = new FakeTextSource("Hi", _ => 10, BreakingPap(), LineProps());
        IntPtr ploc = CreateContext(source);
        try
        {
            Assert.Equal(LsErr.None, LoExports.LoAcquirePenaltyModule(ploc, out IntPtr module));
            Assert.True(GCHandle.FromIntPtr(module).IsAllocated);
            Assert.Equal(LsErr.None, LoExports.LoDisposePenaltyModule(module));

            // Freed module handle: the engine rejects further use (double-dispose and query).
            Assert.Equal(LsErr.InvalidParameter, LoExports.LoDisposePenaltyModule(module));
            Assert.Equal(LsErr.InvalidParameter, LoExports.LoGetPenaltyModuleInternalHandle(module, out _));
        }
        finally
        {
            _ = LoExports.LoDestroyContext(ploc);
        }
    }
}
