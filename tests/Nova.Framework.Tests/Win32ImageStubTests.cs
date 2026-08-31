// win32-image-stubs workstream regression tests.
//
// Task 1 — the three remaining Win32 traps:
//   trap 3 AccessKeyManager.GetActiveWindow (user32), trap 5
//   DwmIsCompositionEnabled (dwmapi), trap 6 OleDoDragDrop (ole32) are guarded
//   with the 0005 pattern (patches/0005-linux-host-traps.patch): a plain
//   interactive session must no longer DllNotFound-crash on Enter/Escape with a
//   null access-key scope, on SystemParameters.IsGlassEnabled, or on
//   DragDrop.DoDragDrop.
//
// Task 2 — imaging is deferred on this host, but the factory/WIC gates in
//   UnsafeNativeMethodsMilCoreApi.cs are STUBBED so imaging types do not crash
//   the process: an Image with no Source builds and renders nothing, an app
//   graph merely containing imaging types runs, and an explicit decode /
//   create attempt (BitmapImage from a real file, BitmapSource.Create) throws a
//   clear catchable PlatformNotSupportedException("imaging is not yet
//   implemented on this host") — NOT a DllNotFoundException and NOT a process
//   abort (exit 134).
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

public sealed partial class WindowTextBlockTests
{
    // Valid 1x1 transparent RGBA PNG (generated with python3 zlib; content is
    // irrelevant — the decode gate throws before any format sniffing).
    private static readonly byte[] TinyPng =
    [
        137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6,
        0, 0, 0, 31, 21, 196, 137, 0, 0, 0, 11, 73, 68, 65, 84, 120, 156, 99, 96, 0, 2, 0, 0, 5, 0,
        1, 122, 94, 171, 63, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130
    ];

    // Valid 1x1 50%-alpha red RGBA PNG (color type 6, 8-bit): pixel (255,0,0,128) straight.
    // Generated with python3 zlib; verified to decode via ImageSharp to B=0 G=0 R=255 A=128.
    private static readonly byte[] SemiTransparentPng =
    [
        137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6,
        0, 0, 0, 31, 21, 196, 137, 0, 0, 0, 13, 73, 68, 65, 84, 120, 156, 99, 248, 207, 192, 208, 0,
        0, 4, 129, 1, 128, 44, 85, 206, 176, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130
    ];

    // ---- Task 1: Win32 traps ----

    [Fact]
    public void GetActiveWindow_OnLinux_ReturnsZero_NoThrow()
    {
        // Trap 3: AccessKeyManager.GetActiveSource/CriticalGetActiveSource call
        // MS.Win32.UnsafeNativeMethods.GetActiveWindow and treat IntPtr.Zero as
        // "no active scope" (access-key matching gives up cleanly). Must not
        // DllNotFound on user32.
        // Try resolution across the three WPF assemblies that compile the
        // Shared MS.Win32 sources. Type.GetType resolves internal types when
        // the caller already has the assembly loaded (proven by the
        // LoGetEscString test), so use the throwing overload per assembly.
        Type methods = Type.GetType("MS.Win32.UnsafeNativeMethods, PresentationCore", throwOnError: false)
            ?? Type.GetType("MS.Win32.UnsafeNativeMethods, PresentationFramework", throwOnError: false)
            ?? Type.GetType("MS.Win32.UnsafeNativeMethods, WindowsBase", throwOnError: false)
            ?? throw new InvalidOperationException(
                "MS.Win32.UnsafeNativeMethods not resolvable from PresentationCore/Framework/WindowsBase");
        MethodInfo getActiveWindow = methods.GetMethod(
            "GetActiveWindow",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetActiveWindow missing");
        Assert.Equal(IntPtr.Zero, (IntPtr)getActiveWindow.Invoke(null, null)!);
    }

    [Fact]
    public void AccessKeyManager_ProcessKey_NullScope_ReturnsFalse_NoThrow()
    {
        // The exact trap-3 corner: a key whose sender scope resolves null falls
        // back to CriticalGetActiveSource -> GetActiveWindow. On Linux that now
        // yields IntPtr.Zero -> no active source -> NoMatch (false), previously
        // DllNotFound on the first Enter/Escape in a null-scope session.
        Assert.False(AccessKeyManager.ProcessKey(null, "A", false));
    }

    [Fact]
    public void DwmIsCompositionEnabled_OnLinux_ReturnsTrue_NoThrow()
    {
        // Trap 5: reached via SystemParameters.IsGlassEnabled; OSVersionHelper
        // reports Win10 on Linux (patch 0005) so the dwmapi import would fire.
        // Reports composition enabled (Aero/glass callers behave as on a
        // compositing Win10; Classic theme paths do not read it).
        Type standard = Type.GetType("Standard.NativeMethods, PresentationFramework", throwOnError: true)!;
        MethodInfo compositionEnabled = standard.GetMethod(
            "DwmIsCompositionEnabled",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("DwmIsCompositionEnabled missing");
        Assert.True((bool)compositionEnabled.Invoke(null, null)!);
    }

    [Fact]
    public void SystemParameters_IsGlassEnabled_OnLinux_NoThrow()
    {
        // Public surface for trap 5 — the import must not crash.
        _ = SystemParameters.IsGlassEnabled;
    }

    [Fact]
    public void DoDragDrop_OnLinux_ReturnsNone_NoThrow()
    {
        // Trap 6: app-initiated OLE drag-drop cannot work on Linux (no ole32;
        // WPF's DataObject relies on System.Private.Windows.Ole which does not
        // exist on this host). The public method now validates its arguments and
        // returns DragDropEffects.None as a documented no-op instead of
        // DllNotFoundException / FileNotFoundException / process abort. The
        // Windows-only body lives in DoDragDropCore and is never JIT-compiled
        // here, so the DataObject type is never loaded.
        var dragSource = new Button(); // UIElement — no window needed
        DragDropEffects result = DragDrop.DoDragDrop(dragSource, "payload", DragDropEffects.Copy | DragDropEffects.Move);
        Assert.Equal(DragDropEffects.None, result);
    }

    [Fact]
    public void DoDragDrop_OnLinux_StillValidatesArguments()
    {
        var dragSource = new Button();
        _ = Assert.Throws<ArgumentNullException>(() => DragDrop.DoDragDrop(null!, "x", DragDropEffects.Copy));
        _ = Assert.Throws<ArgumentNullException>(() => DragDrop.DoDragDrop(dragSource, null!, DragDropEffects.Copy));
    }

    // ---- Task 2: imaging stubs ----

    [Fact]
    public void Image_NoSource_ConstructsLaysOutAndRenders_NoThrow()
    {
        // Acceptance (a): an Image with no Source builds, lays out and renders
        // nothing — no factory is touched, so no exception and no abort.
        // Note: Image.MeasureOverride returns Size(0,0) for a null source and
        // ArrangeOverride reports 0, so ActualWidth is 0 here — identical to
        // Windows. The layout slot still honors the explicit Width (DesiredSize
        // is clamped up to 40x20 via MinMax).
        var image = new Image { Width = 40, Height = 20 };
        var window = new Window { Width = 200, Height = 80, Content = image };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(image.IsMeasureValid, $"measureValid desired={image.DesiredSize}");
            Assert.True(image.IsArrangeValid);
            Assert.Equal(40, image.DesiredSize.Width);
            Assert.Equal(20, image.DesiredSize.Height);

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ImagingTypes_InGraph_BuildAndRun_NoThrow()
    {
        // Acceptance (c): an app graph merely containing imaging types (Image,
        // ImageBrush, uninitialized BitmapImage) builds and runs. Only an
        // explicit decode/create attempt touches the stubbed factories.
        var image = new Image();                    // no source
        var brush = new ImageBrush();               // no source, not rendered
        var bitmap = new BitmapImage();             // never initialized -> no decode
        var panel = new StackPanel();
        _ = panel.Children.Add(image);
        var window = new Window { Width = 200, Height = 80, Content = panel };
        window.Show();
        try
        {
            window.UpdateLayout();
            _ = brush;
            _ = bitmap;
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void BitmapImage_RealFileSource_DecodesRealPixels()
    {
        // Imaging is implemented on this host (Nova.Imaging / ImageSharp): decoding a real
        // PNG file must produce a BitmapSource with the correct size and exact pixel values,
        // NOT throw. The TinyPng is a 1x1 transparent RGBA PNG; decoded straight alpha.
        string png = Path.Combine(Path.GetTempPath(), "nova-imaging-probe.png");
        File.WriteAllBytes(png, TinyPng);
        try
        {
            var bitmap = new BitmapImage(new Uri(png));
            Assert.Equal(1, bitmap.PixelWidth);
            Assert.Equal(1, bitmap.PixelHeight);
            byte[] pixels = new byte[4];
            bitmap.CopyPixels(pixels, 4, 0);
            // 1x1 transparent RGBA -> straight Bgra32: B=0 G=0 R=0 A=0.
            Assert.Equal([(byte)0, (byte)0, (byte)0, (byte)0], pixels);
        }
        finally
        {
            File.Delete(png);
        }
    }

    [Fact]
    public void BitmapSource_Create_ProducesExactPixels()
    {
        // BitmapSource.Create -> CachedBitmap -> managed imaging factory: the pixels must be
        // exact. Red pixel in Bgra32 (B=255 G=0 R=0 A=255).
        var pixels = new byte[] { 255, 0, 0, 255 };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        Assert.Equal(1, bitmap.PixelWidth);
        Assert.Equal(1, bitmap.PixelHeight);
        Assert.Equal(PixelFormats.Bgra32, bitmap.Format);
        byte[] copy = new byte[4];
        bitmap.CopyPixels(copy, 4, 0);
        Assert.Equal(pixels, copy);
    }

    [Fact]
    public void BitmapImage_StraightAlphaDecode_PreservesStraightPixels()
    {
        // A 50%-alpha red pixel decoded from PNG stays STRAIGHT in the BitmapSource
        // (B=0 G=0 R=255 A=128) — the premultiply happens only at GPU upload. This is the
        // premultiply-vs-straight discriminator: a wrong implementation would return
        // premultiplied bytes here.
        string png = Path.Combine(Path.GetTempPath(), "nova-imaging-semitrans-stub.png");
        File.WriteAllBytes(png, SemiTransparentPng);
        try
        {
            var bitmap = new BitmapImage(new Uri(png));
            byte[] pixels = new byte[4];
            bitmap.CopyPixels(pixels, 4, 0);
            Assert.Equal([(byte)0, (byte)0, (byte)255, (byte)128], pixels);
        }
        finally
        {
            File.Delete(png);
        }
    }
}
