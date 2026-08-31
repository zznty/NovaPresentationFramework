// Imaging workstream regression tests (feature/imaging-imagesharp).
//
// Imaging is implemented on this host via Nova.Imaging (SixLabors.ImageSharp): BitmapSource /
// BitmapImage decode to real pixels, the Image control renders its bitmap through the DUCE
// transport (SendCommandBitmapSource -> SlaveGraph.VisitDrawImage), ImageBrush fills, and the
// straight->premultiplied alpha conversion is exercised end-to-end. These tests assert exact
// pixel values — a green "something rendered" is not enough for the premultiply trap.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

public sealed partial class WindowTextBlockTests
{
    // 4x4 Bgra32: left half red (B=255), right half blue (R=255), opaque.
    private static BitmapSource CreateTwoToneBitmap()
    {
        const int size = 4;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int offset = ((y * size) + x) * 4;
                bool left = x < 2;
                pixels[offset] = left ? (byte)255 : (byte)0;      // B
                pixels[offset + 1] = 0;                            // G
                pixels[offset + 2] = left ? (byte)0 : (byte)255;   // R
                pixels[offset + 3] = 255;                          // A
            }
        }

        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static bool IsRed(byte r, byte g, byte b)
    {
        return r > 200 && g < 60 && b < 60;
    }

    private static bool IsBlue(byte r, byte g, byte b)
    {
        return b > 200 && r < 60 && g < 60;
    }

    // ReadbackRgba returns R,G,B,A (normalized from the B8G8R8A8 surface), so index
    // span[i]=R, span[i+1]=G, span[i+2]=B — same as the harness FindColor(IsRed).
    private static (int Red, int Blue) CountColors(ReadOnlyMemory<byte> pixels)
    {
        int red = 0;
        int blue = 0;
        ReadOnlySpan<byte> span = pixels.Span;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            byte r = span[i];
            byte g = span[i + 1];
            byte b = span[i + 2];
            if (IsRed(r, g, b))
            {
                red++;
            }
            else if (IsBlue(r, g, b))
            {
                blue++;
            }
        }

        return (red, blue);
    }

    [Fact]
    public void ImageControl_Z_Fill_RendersAtPositionWithStretchFill()
    {
        var bitmap = CreateTwoToneBitmap();
        var image = new Image { Width = 80, Height = 40, Stretch = Stretch.Fill, Source = bitmap };
        var window = new Window { Width = 320, Height = 240, Content = image };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Equal(80, image.ActualWidth);
            Assert.Equal(40, image.ActualHeight);

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int blue) = CountColors(source.ReadbackRgba());

            // 80x40 = 3200 px; left half red (40x40 = 1600), right half blue (1600). The
            // 4x4 texture scaled 20x leaves a ~2.5px bilinear blend band at the center
            // (x=40), so each half loses ~120 px to the seam: measured red=1360 blue=1360.
            Assert.InRange(red, 1300, 1400);
            Assert.InRange(blue, 1300, 1400);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ImageControl_A_Uniform_SizesDestRectToAspect()
    {
        var bitmap = CreateTwoToneBitmap(); // 4x4 square
        var image = new Image { Width = 80, Height = 40, Stretch = Stretch.Uniform, Source = bitmap };
        var host = new Grid();
        _ = host.Children.Add(image);
        var window = new Window { Width = 320, Height = 240, Content = host };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(image.RenderSize.Width > 0 && image.RenderSize.Height > 0, $"renderSize={image.RenderSize} actual={image.ActualWidth}x{image.ActualHeight} measureValid={image.IsMeasureValid}");
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int blue) = CountColors(source.ReadbackRgba());

            // Uniform into 80x40 from a square: 40x40 content, centered; left half red
            // (20x40=800), right half blue (800), with the same ~10px-wide bilinear seam.
            Assert.InRange(red, 650, 800);
            Assert.InRange(blue, 650, 800);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ImageBrush_TwoToneBitmap_FillsBothHalves()
    {
        var rect = new Rectangle
        {
            Width = 120,
            Height = 80,
            Fill = new ImageBrush(CreateTwoToneBitmap()) { Stretch = Stretch.Fill }
        };
        var window = new Window { Width = 320, Height = 240, Content = rect };
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int blue) = CountColors(source.ReadbackRgba());

            // 120x80 = 9600; left half red (60x80=4800), right half blue (4800); the 4x4
            // texture scaled 30x leaves a ~15px bilinear seam: measured red=4080 blue=4080.
            Assert.InRange(red, 3900, 4200);
            Assert.InRange(blue, 3900, 4200);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ImageBrush_BitmapSurvivesGcBeforePresent_RendersBothHalves()
    {
        // Deterministic regression for the QI-refcount flake: the Linux identity
        // MILUnknown.QueryInterface used to return the bitmap token WITHOUT bumping
        // WicHandleTable's refcount, so a wrapping SafeMILHandle's finalizer released the
        // token's only ref and disposed the decoded bitmap before the render pass detached
        // it into the graph (SendCommandBitmapSource -> SetBitmapSourcePixels). The original
        // ImageBrush_TwoToneBitmap_FillsBothHalves only caught this as an intermittent
        // white frame (red=0/blue=0) because it depended on GC timing inside the suite; this
        // test forces the finalizers between bitmap construction and present, so it fails
        // 100% of the time without the QI AddRef and passes deterministically with it.
        var bitmap = CreateTwoToneBitmap();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var rect = new Rectangle
        {
            Width = 120,
            Height = 80,
            Fill = new ImageBrush(bitmap) { Stretch = Stretch.Fill }
        };
        var window = new Window { Width = 320, Height = 240, Content = rect };
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            (int red, int blue) = CountColors(source.ReadbackRgba());

            Assert.InRange(red, 3900, 4200);
            Assert.InRange(blue, 3900, 4200);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SemiTransparentPng_OverWhite_CompositesPremultipliedCorrectly()
    {
        // The premultiply discriminator: a 50%-alpha red PNG (straight Bgra32 0,0,255,128)
        // over an opaque white background must composite to (255,128,128) pink. If the
        // straight pixel were fed to the premultiplied blend pipeline as-is, the result
        // would be half-transparent red (128,0,0,128-ish) — the classic silent wire bug.
        string png = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nova-imaging-semitrans-composite.png");
        System.IO.File.WriteAllBytes(png, SemiTransparentPng);
        try
        {
            var bitmap = new BitmapImage(new Uri(png));
            byte[] verifyPx = new byte[4];
            bitmap.CopyPixels(verifyPx, 4, 0);
            Assert.Equal([(byte)0, (byte)0, (byte)255, (byte)128], verifyPx); // straight red 50%

            // Compose the 50%-alpha red bitmap over an opaque white background using an Image
            // element in a Grid, then read the presented frame back.
            var panel = new Grid { Background = Brushes.White };
            _ = panel.Children.Add(new Image { Stretch = Stretch.Fill, Source = bitmap });
            var window = new Window { Width = 64, Height = 64, Content = panel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
                source.EnableReadback();
                Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
                SdlPresentationSource.PresentAll();
                SdlPresentationSource.PresentAll();
                ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
                int width = source.PixelWidth;

                // Sample the center of the 64x64 image (stretched 1x1 PNG fills the whole).
                // ReadbackRgba is R,G,B,A.
                int offset = ((32 * width) + 32) * 4;
                byte r = pixels.Span[offset];
                byte g = pixels.Span[offset + 1];
                byte b = pixels.Span[offset + 2];
                byte a = pixels.Span[offset + 3];

                // 50% red over white: r≈255, g≈b≈128, a=255. Allow tiny sampling tolerance.
                Assert.InRange(r, 245, 255);
                Assert.InRange(g, 120, 140);
                Assert.InRange(b, 120, 140);
                Assert.Equal(255, a);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            System.IO.File.Delete(png);
        }
    }

    [Fact]
    public void BitmapSource_Lifecycle_ReclaimsWicHandleTableEntries()
    {
        // The QI AddRef fix (UnsafeNativeMethodsMilCoreApi.cs MILUnknown.QueryInterface) must
        // be balanced: every wrapping SafeMILHandle that releases a QI'd token now has its own
        // refcount, so once the last handle dies the WicHandleTable entry is reclaimed — a
        // missing counterpart would accumulate entries (and leak the pooled ImageSharp Image).
        // Force the finalizers between construction and present (the deterministic-flake
        // window) across many bitmaps and assert the table returns to its baseline size.
        //
        // The creation loops live in a HELPER method: a Debug JIT keeps one temporary slot
        // per call site alive for the whole enclosing frame, so the last bitmap of each loop
        // would appear rooted (a +1 per loop artifact) if the loops were inline here. The
        // helper's frame dies on return, so the count below is the true table size.
        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        static void AbandonBitmaps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _ = CreateTwoToneBitmap();
            }
        }

        // Warm the imaging pipeline so any one-time tokens (factories, decoders) settle.
        AbandonBitmaps(4);
        Collect();

        int baseline = Nova.Imaging.WicHandleTable.Count;

        // Create-and-render many ImageBrush bitmaps (the real DUCE send/detach path).
        for (int i = 0; i < 8; i++)
        {
            var rect = new Rectangle
            {
                Width = 120,
                Height = 80,
                Fill = new ImageBrush(CreateTwoToneBitmap()) { Stretch = Stretch.Fill }
            };
            var window = new Window { Width = 320, Height = 240, Content = rect };
            window.Show();
            try
            {
                window.UpdateLayout();
                var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
                source.EnableReadback();
                _ = PumpOnePass(source);
                SdlPresentationSource.PresentAll();
                SdlPresentationSource.PresentAll();
                (int red, _) = CountColors(source.ReadbackRgba());
                Assert.InRange(red, 3900, 4200);
            }
            finally
            {
                window.Close();
            }
        }

        // Create-and-abandon bitmaps without rendering (pure QI/refcount lifecycle).
        AbandonBitmaps(8);
        Collect();

        int after = Nova.Imaging.WicHandleTable.Count;

        // Second abandon batch: a per-batch leak would grow the table again.
        AbandonBitmaps(8);
        Collect();
        int afterSecond = Nova.Imaging.WicHandleTable.Count;

        // Third batch: extra Collect rounds in between to rule out finalizer ordering.
        AbandonBitmaps(8);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        int afterThird = Nova.Imaging.WicHandleTable.Count;

        Assert.True(afterSecond <= baseline + 2,
            $"WicHandleTable grew again after a second abandon batch ({baseline} -> {afterSecond}): a QI AddRef has no matching Release.");
        Assert.True(afterThird <= baseline + 2,
            $"WicHandleTable grew again after a third abandon batch ({baseline} -> {afterThird}): a QI AddRef has no matching Release.");
        Assert.True(after <= baseline + 2,
            $"WicHandleTable grew from {baseline} to {after}: a QI AddRef has no matching Release, or a detach leaked the bitmap.");
    }
}
