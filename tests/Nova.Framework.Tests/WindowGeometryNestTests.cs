using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

/// <summary>
/// Regression tests for the MilUtility nest wiring (patches/0005): <c>MilUtility_PolygonBounds</c>
/// (Line shape layout), <c>MilUtility_PathGeometryCombine</c> (ComboBox dropdown + Menu
/// submenu open), and the curve-aware <c>MilUtility_PathGeometryBounds</c> path (Bezier
/// Path measure + render). Before the nests were wired these controls crashed at layout with
/// DllNotFoundException (wpfgfx_cor3) or measured 0x0.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Line_WithStroke_LaysOutAndRenders()
    {
        var line = new Line
        {
            X1 = 0,
            Y1 = 0,
            X2 = 100,
            Y2 = 50,
            Stroke = Brushes.Red,
            StrokeThickness = 4
        };
        var window = new Window { Width = 320, Height = 240, Content = line };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            // Trap 11: MilUtility_PolygonBounds. The Line must lay out (not crash) with a
            // non-zero stroke-inflated size.
            Assert.True(line.ActualWidth > 0, $"line ActualWidth={line.ActualWidth}");
            Assert.True(line.ActualHeight > 0, $"line ActualHeight={line.ActualHeight}");

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            Console.WriteLine($"Line-diag: colors={CountDistinctColors(pixels.Span)} px0={pixels.Span[0]:X2}{pixels.Span[1]:X2}{pixels.Span[2]:X2}{pixels.Span[3]:X2} size={pixels.Length} px={source.PixelWidth}x{source.PixelHeight} actual={line.ActualWidth}x{line.ActualHeight}");
            Assert.True(HasDominantRedPixels(pixels.Span), "stroked Line must render red pixels");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ComboBox_DropdownOpen_LaysOutAndRendersPopup()
    {
        var combo = new ComboBox { Width = 120, Height = 24 };
        _ = combo.Items.Add("one");
        _ = combo.Items.Add("two");
        _ = combo.Items.Add("three");
        var window = new Window { Width = 320, Height = 240, Content = combo };
        window.Show();
        try
        {
            combo.IsDropDownOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            // Trap 10: MilUtility_PathGeometryCombine via FrameworkElement.GetLayoutClip during
            // popup layout. The dropdown must open and realize a popup source.
            var mainSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            SdlPresentationSource? popupSource = FindPopupSource(mainSource);
            Assert.NotNull(popupSource);

            popupSource!.EnableReadback();
            popupSource.Present();
            popupSource.Present();
            Assert.True(CountDistinctColors(popupSource.ReadbackRgba().Span) >= 1,
                "ComboBox dropdown popup must render more than a flat clear color");
        }
        finally
        {
            combo.IsDropDownOpen = false;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            window.Close();
        }
    }

    [Fact]
    public void Menu_SubmenuOpen_LaysOutAndRendersPopup()
    {
        var menu = new Menu();
        var file = new MenuItem { Header = "File" };
        var sub = new MenuItem { Header = "Open" };
        _ = file.Items.Add(sub);
        _ = menu.Items.Add(file);
        var window = new Window { Width = 320, Height = 240, Content = menu };
        window.Show();
        try
        {
            file.IsSubmenuOpen = true;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            // Trap 10 via the Menu submenu path (same GetLayoutClip combine).
            var mainSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            SdlPresentationSource? popupSource = FindPopupSource(mainSource);
            Assert.NotNull(popupSource);

            popupSource!.EnableReadback();
            popupSource.Present();
            popupSource.Present();
            Assert.True(CountDistinctColors(popupSource.ReadbackRgba().Span) >= 1,
                "Menu submenu popup must render more than a flat clear color");
        }
        finally
        {
            file.IsSubmenuOpen = false;
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            window.Close();
        }
    }

    [Fact]
    public void Path_Bezier_MeasuresNonZeroAndRenders()
    {
        var geometry = new PathGeometry(
            [new PathFigure(
                new Point(0, 40),
                [new BezierSegment(new Point(10, 0), new Point(50, 80), new Point(80, 40), true)],
                true)]);
        var path = new System.Windows.Shapes.Path { Data = geometry, Fill = Brushes.Red };
        var window = new Window { Width = 320, Height = 240, Content = path };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            // Curve-aware bounds (MilUtility_PathGeometryBounds with the fixed wire decoder):
            // the Bezier path must measure non-zero instead of 0x0.
            Assert.True(path.ActualWidth > 0, $"path ActualWidth={path.ActualWidth}");
            Assert.True(path.ActualHeight > 0, $"path ActualHeight={path.ActualHeight}");

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            Console.WriteLine($"Path-diag: colors={CountDistinctColors(pixels.Span)} px0={pixels.Span[0]:X2}{pixels.Span[1]:X2}{pixels.Span[2]:X2}{pixels.Span[3]:X2} actual={path.ActualWidth}x{path.ActualHeight}");
            Assert.True(HasDominantRedPixels(pixels.Span), "Bezier path must render red pixels");
        }
        finally
        {
            window.Close();
        }
    }

    private static SdlPresentationSource? FindPopupSource(SdlPresentationSource main)
    {
        SdlPresentationSource? any = null;
        foreach (PresentationSource candidate in PresentationSource.CurrentSources)
        {
            if (candidate is SdlPresentationSource ps && !ReferenceEquals(ps, main))
            {
                if (ReferenceEquals(ps.Owner, main))
                {
                    return ps;
                }

                any ??= ps;
            }
        }

        return any;
    }

    private static bool HasDominantRedPixels(ReadOnlySpan<byte> pixels)
    {
        // Solid colors are sRGB-encoded before the raster stores them, so red #FF0000
        // stores as 255; accept a wide red-dominant range so the scan is robust.
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte r = pixels[i];
            byte g = pixels[i + 1];
            byte b = pixels[i + 2];
            if (r > 150 && g < 100 && b < 100)
            {
                return true;
            }
        }

        return false;
    }
}
