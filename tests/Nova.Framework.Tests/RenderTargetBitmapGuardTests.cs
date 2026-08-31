// rtb-graceful-guard workstream regression tests.
//
// RenderTargetBitmap is DELIBERATELY deferred on this host (its pixels come from the
// sync-channel generic target — PrintInitialize / MILCMD_GENERICTARGET_CREATE with a native
// IMILRenderTargetBitmap handle — which has no managed seam). Imaging
// itself (BitmapDecoder / BitmapSource) works via Nova.Imaging. What regressed with patch
// 0015 (real imaging): MILFactory2.CreateFactory stopped throwing, so RenderTargetBitmap's
// constructor sailed past the old managed imaging gate into the RTB-specific native nests
// (MILFactoryCreateBitmapRenderTarget etc.) and failed with a hard
// DllNotFoundException("wpfgfx_cor3.dll") — exactly the failure class this project
// eliminates. Every RTB-specific native entry point now throws a clear, catchable
// PlatformNotSupportedException("RenderTargetBitmap is not yet implemented on this host")
// on Linux (0005 pattern: public method branches on !OperatingSystem.IsWindows(), real
// DllImport moves to a private *Core method) — NOT DllNotFoundException, NOT a process
// abort (exit 134).
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nova.Framework.Tests;

public sealed partial class WindowTextBlockTests
{
    private const string RtbNotImplemented = "RenderTargetBitmap is not yet implemented on this host";

    [Fact]
    public void RenderTargetBitmap_Ctor_OnLinux_ThrowsPlatformNotSupported()
    {
        // The exact harness feat:rtb failure: `new RenderTargetBitmap(...)` used to throw
        // DllNotFoundException for wpfgfx_cor3.dll. It must throw a catchable
        // PlatformNotSupportedException naming the deferred feature — the ctor reaches
        // MILFactory2.CreateBitmapRenderTarget (MILFactoryCreateBitmapRenderTarget) inside
        // FinalizeCreation, now guarded.
        var ex = Assert.Throws<PlatformNotSupportedException>(() =>
            new RenderTargetBitmap(64, 48, 96, 96, PixelFormats.Pbgra32));
        Assert.Equal(RtbNotImplemented, ex.Message);
    }

    [Fact]
    public void RenderTargetBitmap_Clear_OnLinux_ThrowsPlatformNotSupported()
    {
        // RenderTargetBitmap.Clear() calls MILRenderTargetBitmap.Clear
        // (MILRenderTargetBitmapClear, wgx_exports.cs). The public ctor is already guarded,
        // so a consumer cannot reach Clear with a real instance; drive it on an instance
        // built through the internal parameterless ctor (the Freezable CreateInstanceCore
        // path) with a synthetic render-target handle.
        object rtb = CreateBlankRenderTargetBitmap();

        var ex = Assert.Throws<PlatformNotSupportedException>(() =>
            ((RenderTargetBitmap)rtb).Clear());
        Assert.Equal(RtbNotImplemented, ex.Message);
    }

    [Fact]
    public void RenderTargetBitmap_Render_OnLinux_ThrowsPlatformNotSupported()
    {
        // Render(visual) → BitmapVisualManager.Render: the FIRST native step on a
        // constructed instance is MILUnknown.QueryInterface(handle, IID_IMILRenderTargetBitmap)
        // — the sync-channel generic-target composition route (PrintInitialize /
        // MILCMD_GENERICTARGET_CREATE). That QI must throw the same catchable PNSE, not
        // DllNotFoundException, not a silent no-op, and not a process abort.
        object rtb = CreateBlankRenderTargetBitmap();
        var visual = new DrawingVisual();

        var ex = Assert.Throws<PlatformNotSupportedException>(() =>
            ((RenderTargetBitmap)rtb).Render(visual));
        Assert.Equal(RtbNotImplemented, ex.Message);
    }

    // Builds a RenderTargetBitmap via the internal parameterless ctor (Freezable
    // CreateInstanceCore) and fills the private state Render needs, so the tests drive the
    // exact native entry points a consumer-reachable instance would hit.
    private static object CreateBlankRenderTargetBitmap()
    {
        object rtb = Activator.CreateInstance(
            typeof(RenderTargetBitmap),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null)!;

        // internal fields on BitmapSource (base): _pixelWidth / _pixelHeight / _dpiX / _dpiY.
        SetField(rtb, "_pixelWidth", 64);
        SetField(rtb, "_pixelHeight", 48);
        SetField(rtb, "_dpiX", 96.0);
        SetField(rtb, "_dpiY", 96.0);

        // The property getters (PixelWidth/DpiX, which BitmapVisualManager.Render reads)
        // call _bitmapInit.EnsureInitializedComplete(), so the internal BitmapInitialize
        // must be EndInit'ed (the internal ctor never runs BeginInit/EndInit).
        object bitmapInit = GetField(rtb, "_bitmapInit");
        var supportInitialize = (System.ComponentModel.ISupportInitialize)bitmapInit;
        supportInitialize.BeginInit();
        supportInitialize.EndInit();

        // private SafeMILHandle _renderTargetBitmap on RenderTargetBitmap. Any non-zero
        // token works: the Linux QI guard keys on the requested IID, not the value.
        Type safeMilHandle = typeof(RenderTargetBitmap).Assembly.GetType("System.Windows.Media.SafeMILHandle")
            ?? throw new InvalidOperationException("SafeMILHandle type not found in PresentationCore");
        object handle = Activator.CreateInstance(
            safeMilHandle,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new IntPtr(0x4E1B)],
            culture: null)!;
        SetField(rtb, "_renderTargetBitmap", handle);

        return rtb;
    }

    private static object GetField(object target, string name)
    {
        for (Type? type = target.GetType(); type is not null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field.GetValue(target)!;
            }
        }

        throw new InvalidOperationException($"field {name} not found on {target.GetType()}");
    }

    private static void SetField(object target, string name, object value)
    {
        for (Type? type = target.GetType(); type is not null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                field.SetValue(target, value);
                return;
            }
        }

        throw new InvalidOperationException($"field {name} not found on {target.GetType()}");
    }
}
