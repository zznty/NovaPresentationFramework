using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

/// <summary>
/// Area 2 + 3 verification with real pixels: ControlTemplate / DataTemplate /
/// attached properties must RENDER (readback), not merely parse. Lives in the same
/// <see cref="WindowTextBlockTests"/> class (one xunit collection) as every other
/// window test so all windows serialize on the single shared DUCE channel graph;
/// the probe harness documented that a second CONCURRENT window can render nothing
/// (stale Root handle — see ControlCoverageProbe). Serial => trustworthy pixels.
/// </summary>
public sealed partial class WindowTextBlockTests
{




    [Fact]
    public void ZPath_CenterAlignment_Checker()
    {
        // A 7x4 path centered in a 17x24 border at a known position: where does it render?
        var path = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M 0 0 L 3.5 4 L 7 0 Z"),
            Fill = System.Windows.Media.Brushes.Black,
            Stroke = null,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var border = new Border
        {
            Width = 17,
            Height = 24,
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(1),
            Child = path,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(103, 2, 0, 0),
        };
        var win = new Window { Width = 300, Height = 100, Content = border };
        win.Show();
        try
        {
            Flush();
            var src = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(win));
            var mainSrc = src;
            mainSrc.EnableReadback();
            mainSrc.Present();
            mainSrc.Present();
            Flush();
            byte[] bytes = mainSrc.ReadbackRgba().Span.ToArray();
            // Centered 7x4 path in the 17x24 border: the dark core must be found near the
            // border's center (103+2..119, 2..24 → core around x109-113, y12-15).
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int y = 0; y < 100; y++)
            {
                for (int x = 90; x < 140; x++)
                {
                    int o = ((y * 300) + x) * 4;
                    int r = bytes[o], g = bytes[o + 1], b = bytes[o + 2];
                    if (r < 60 && g < 60 && b < 60)
                    {
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
            }

            Assert.True(minX >= 106 && maxX <= 116, $"centered path not horizontal-centered: x {minX}..{maxX}");
            Assert.True(minY >= 8 && maxY <= 18, $"centered path not vertical-centered: y {minY}..{maxY}");
        }
        finally
        {
            win.Close();
        }
    }


#pragma warning disable CA1305, IDE0047, IDE0048, IDE0058, IDE0060, IDE0011, IDE0019, IDE0004
    [Fact]
    public void ZComboBox_ChevronSize()
    {
        var results = new System.Text.StringBuilder();
        foreach (int h in new[] { 24, 40, 60 })
        {
            MeasureChevron(h, results);
        }

    }

    private static void MeasureChevron(int height, System.Text.StringBuilder results)
    {
        var combo = new ComboBox
        {
            Width = 120,
            Height = height,
            Margin = new Thickness(0, 0, 0, 0),
            Items = { "One" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        const int w = 300;
        const int hwin = 100;
        System.Windows.Shapes.Path? preArrow = null;
        void FindPre(System.Windows.DependencyObject d)
        {
            if (preArrow == null && d is System.Windows.FrameworkElement fe && fe.Name == "Arrow")
            {
                preArrow = (System.Windows.Shapes.Path)fe;
            }

            int nn = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < nn; i++) { FindPre(System.Windows.Media.VisualTreeHelper.GetChild(d, i)); }
        }

        FindPre(combo);
        results.AppendLine("h=" + height + " pre arrow bounds=" + (preArrow == null ? "none" : preArrow.Data.Bounds.X + "," + preArrow.Data.Bounds.Y + "," + preArrow.Data.Bounds.Width + "," + preArrow.Data.Bounds.Height));
        var ab = System.Windows.Media.Geometry.Parse("M 0 0 L 4.5 5 L 9 0 Z");
        results.AppendLine("h=" + height + " AB-static-ab bounds=" + ab.Bounds.X + "," + ab.Bounds.Y + "," + ab.Bounds.Width + "," + ab.Bounds.Height);
        var abPath = new System.Windows.Shapes.Path { Data = ab, Fill = System.Windows.Media.Brushes.Black, Width = 30, Height = 30 };
        System.Windows.Shapes.Path? shot = null;
        combo.ApplyTemplate();
        void WalkNow(System.Windows.DependencyObject d)
        {
            if (shot == null && d is System.Windows.FrameworkElement f3 && f3.Name == "Arrow")
            {
                shot = (System.Windows.Shapes.Path)f3;
            }

            int nw = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < nw; i++) { WalkNow(System.Windows.Media.VisualTreeHelper.GetChild(d, i)); }
        }

        WalkNow(combo);
        results.AppendLine("h=" + height + " at-applytemplate arrow bounds=" + (shot is null ? "none" : shot.Data.Bounds.X + "," + shot.Data.Bounds.Y + "," + shot.Data.Bounds.Width + "," + shot.Data.Bounds.Height));
        var grid = new System.Windows.Controls.Grid();
        grid.Children.Add(combo);
        grid.Children.Add(abPath);
        var window = new Window { Width = w, Height = hwin, Content = grid };
        window.Show();
        try
        {
            Flush();
            System.Windows.Shapes.Path? arPre = null;
            void WalkPre(System.Windows.DependencyObject d)
            {
                if (arPre == null && d is System.Windows.FrameworkElement f2 && f2.Name == "Arrow")
                {
                    arPre = (System.Windows.Shapes.Path)f2;
                }

                int np = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < np; i++) { WalkPre(System.Windows.Media.VisualTreeHelper.GetChild(d, i)); }
            }

            WalkPre(combo);
            results.AppendLine("h=" + height + " post-flush arrow bounds=" + (arPre is null ? "none" : arPre.Data.Bounds.X + "," + arPre.Data.Bounds.Y + "," + arPre.Data.Bounds.Width + "," + arPre.Data.Bounds.Height));
            results.AppendLine("h=" + height + " AB-render-ab bounds=" + ab.Bounds.X + "," + ab.Bounds.Y + "," + ab.Bounds.Width + "," + ab.Bounds.Height);
            results.AppendLine("h=" + height + " AB-render-arrow(preArrow2path later)");
            var mainSrc = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            mainSrc.EnableReadback();
            mainSrc.Present();
            mainSrc.Present();
            Flush();
            byte[] bytes = mainSrc.ReadbackRgba().Span.ToArray();

            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int y = 0; y < hwin; y++)
            {
                for (int x = 80; x < 200; x++)
                {
                    int o = ((y * w) + x) * 4;
                    int r = bytes[o], g = bytes[o + 1], b = bytes[o + 2];
                    if (r < 150 && g < 150 && b < 150)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            results.AppendLine("h=" + height + " chevron bbox: " + (maxX - minX) + "x" + (maxY - minY) + " @ (" + minX + "," + minY + ")");

            // Chevron gate — the splitBorder button must be the scrollbar-width (17px) and the
            // arrow centered inside it. With the scrollbar metric at 0 the button collapses to
            // ~2px and the arrow sticks past the combo's right edge (small + offset right).
            if (arPre is not null && arPre.Parent is System.Windows.FrameworkElement par)
            {
                double bcenter = par.TransformToAncestor(window).Transform(new Point(0, 0)).X + (par.ActualWidth / 2);
                double acenter = arPre.TransformToAncestor(window).Transform(new Point(0, 0)).X + (arPre.ActualWidth / 2);
                Assert.True(par.ActualWidth > 10, $"chevron button {par.ActualWidth}px wide — scrollbar metric collapsed it (CXVSCROLL=0)");
                Assert.True(Math.Abs(acenter - bcenter) < 3, $"chevron not centered: arrowCenter={acenter:F1} buttonCenter={bcenter:F1}");
            }

            // dump template element positions for h=24
            if (height == 24)
            {
                System.Windows.FrameworkElement? arrow = null;
                void WalkPos(System.Windows.DependencyObject d)
                {
                    if (arrow == null && d is System.Windows.FrameworkElement fe && fe.Name == "Arrow")
                    {
                        arrow = fe;
                    }

                    int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < n; i++)
                    {
                        WalkPos(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                    }
                }

                WalkPos(combo);
                if (arrow != null)
                {
                    var pt = arrow.TransformToAncestor(window).Transform(new Point(0, 0));
                    results.AppendLine("Arrow '" + arrow.GetType().Name + "' " + arrow.ActualWidth + "x" + arrow.ActualHeight + " @ " + pt.X + "," + pt.Y +
                        " parent='" + (arrow.Parent?.GetType().Name ?? "?") + "'");
                    if (arrow.Parent is System.Windows.FrameworkElement parent)
                    {
                        var ppt = parent.TransformToAncestor(window).Transform(new Point(0, 0));
                        results.AppendLine("ARROW-PARENT '" + parent.GetType().Name + "' " + parent.ActualWidth + "x" + parent.ActualHeight + " @ " + ppt.X + "," + ppt.Y);
                    }

                    var rgProp = typeof(System.Windows.Shapes.Shape).GetProperty("RenderedGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    results.AppendLine("arrow RenderedGeometry bounds=" + (rgProp?.GetValue(arrow) is System.Windows.Media.Geometry rg
                        ? rg.Bounds.X + "," + rg.Bounds.Y + "," + rg.Bounds.Width + "," + rg.Bounds.Height
                        : "?"));
                    results.AppendLine("arrow Data bounds=" + ((System.Windows.Shapes.Path)arrow).Data.Bounds.X + "," + ((System.Windows.Shapes.Path)arrow).Data.Bounds.Y + "," + ((System.Windows.Shapes.Path)arrow).Data.Bounds.Width + "," + ((System.Windows.Shapes.Path)arrow).Data.Bounds.Height);
                    results.AppendLine("arrow Data string=" + ((System.Windows.Shapes.Path)arrow).Data.ToString());
                    var dg = ((System.Windows.Shapes.Path)arrow).Data;
                    results.AppendLine("arrow Data.Transform=" + (dg.Transform == null ? "null" : dg.Transform.Value.M11 + "," + dg.Transform.Value.M12 + "," + dg.Transform.Value.M21 + "," + dg.Transform.Value.M22 + "," + dg.Transform.Value.OffsetX + "," + dg.Transform.Value.OffsetY));
                    results.AppendLine("arrow RenderTransform=" + (arrow.RenderTransform == null ? "null" : arrow.RenderTransform.ToString()));
                    results.AppendLine("arrow LayoutTransform=" + (arrow.LayoutTransform == null ? "null" : arrow.LayoutTransform.ToString()));
                    if (((System.Windows.Shapes.Path)arrow).Data is System.Windows.Media.StreamGeometry sg)
                    {
                        var af = typeof(System.Windows.Media.StreamGeometry).GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (af != null && af.GetValue(sg) is byte[] adb)
                        {
                            results.AppendLine("arrow stream header: " + System.BitConverter.ToString(adb, 0, 48));
                            results.AppendLine("arrow stream FULL: " + System.BitConverter.ToString(adb));
                            if (((System.Windows.Shapes.Path)arrow).Data is System.Windows.Media.PathGeometry pg)
                            {
                                var segs = new System.Collections.Generic.List<string>();
                                foreach (var f in pg.Figures)
                                {
                                    foreach (var sg2 in f.Segments)
                                    {
                                        if (sg2 is System.Windows.Media.LineSegment { } ls2)
                                        {
                                            segs.Add("L(" + ls2.Point.X + "," + ls2.Point.Y + ")");
                                        }
                                    }
                                }

                                results.AppendLine("parsed segments: " + string.Join(" ", segs));
                            }
                            var fcs = Nova.Geometry2D.MilPathFlattener.Flatten(adb);
                            foreach (var c in fcs)
                            {
                                var ps = c.ReadOnlySpan;
                                var pts = new string[ps.Length];
                                for (int pi = 0; pi < ps.Length; pi++) pts[pi] = ps[pi].X + "," + ps[pi].Y;
                                results.AppendLine("flatten: " + string.Join(" | ", pts));
                            }
                        }
                    }
                    results.AppendLine("arrow Desired=" + arrow.DesiredSize.Width + "x" + arrow.DesiredSize.Height);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZComboBox_PopupFirstOpenPosition()
    {
        // ComboBox popup first-open placement: known combo position, capture the popup
        // source's window position on first and second open (the bug: first open at the
        // window's 0,0).
        var combo = new ComboBox
        {
            Width = 120,
            Margin = new Thickness(100, 40, 0, 0),
            Items = { "One", "Two", "Three" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Width = 640, Height = 160, Content = combo };
        window.Show();
        try
        {
            Flush();
            var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
            Assert.NotNull(popup);

            System.Text.StringBuilder log = new System.Text.StringBuilder();
            for (int open = 0; open < 2; open++)
            {
                combo.IsDropDownOpen = true;
                Flush();
                Flush();
                // popup child's presentation source position
                var childVisual = (System.Windows.Media.Visual)popup.Child;
                var src = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(childVisual));
                log.AppendLine("open" + open + " left=" + src.PixelLeft + " top=" + src.PixelTop + " w=" + src.PixelWidth + " h=" + src.PixelHeight);
                // the placement's target interest point check: combo's client position expected
                // popup at approximately window-pos + (120, 40..64); main window pos:

                log.AppendLine("  main=" + ((SdlPresentationSource)PresentationSource.FromVisual(window)).PixelLeft + "," + ((SdlPresentationSource)PresentationSource.FromVisual(window)).PixelTop);
                combo.IsDropDownOpen = false;
                Flush();
            }

            var mainSrc = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            log.AppendLine("window left=" + mainSrc.PixelLeft + " top=" + mainSrc.PixelTop);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZHighlightGeometry_FlattenOutput()
    {
        double left = 64.92666666666668;
        double top = 17.171666666666667;
        double right = left + 140;
        double bottom = top + 28;
        double r = 3.0;
        var g = new System.Windows.Media.StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new System.Windows.Point(left, bottom), true, true);
            ctx.LineTo(new System.Windows.Point(left, top + r), true, true);
            ctx.ArcTo(new System.Windows.Point(left + r, top), new System.Windows.Size(r, r), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, true);
            ctx.LineTo(new System.Windows.Point(right - r, top), true, true);
            double cutX = right - (r * 0.293);
            double cutY = top + (r * 0.293);
            double icutX = right - 1.0 - ((r - 1.0) * 0.293);
            double icutY = top + 1.0 + ((r - 1.0) * 0.293);
            ctx.ArcTo(new System.Windows.Point(cutX, cutY), new System.Windows.Size(r, r), 0, false, System.Windows.Media.SweepDirection.Clockwise, true, true);
            ctx.LineTo(new System.Windows.Point(icutX, icutY), true, true);
            ctx.ArcTo(new System.Windows.Point(right - r, top + 1), new System.Windows.Size(r - 1, r - 1), 0, false, System.Windows.Media.SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new System.Windows.Point(left + r, top + 1), true, true);
            ctx.ArcTo(new System.Windows.Point(left + 1, top + r), new System.Windows.Size(r - 1, r - 1), 0, false, System.Windows.Media.SweepDirection.Counterclockwise, true, true);
            ctx.LineTo(new System.Windows.Point(left + 1, bottom), true, true);
        }

        g.Freeze();
        // reflect the serialized stream
        var streamField = typeof(System.Windows.Media.StreamGeometry).GetField("_data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("StreamGeometry _data field not found");
        byte[] data = (byte[])streamField.GetValue(g)!;
        var contours = Nova.Geometry2D.MilPathFlattener.Flatten(data);
        Assert.Single(contours);
        var pts = contours[0].ReadOnlySpan;

        // Regression gate for the C blob: the poly-line before the top-left arc must
        // advance the pen — the arc's flattened points must START at the line's end
        // (64.93, 20.17) and stay on the radius-3 arc around (67.93, 20.17), never on
        // the old inflated spiral (center (65.8, 31.2), radius 13.5).
        // the line's end (64.93, 20.17) appears at pts[1]; the ARC must then START there
        // (pts[2] is the arc's first sample — the old bug had the arc start at the figure
        // start (64.93, 45.17) instead).
        Assert.True(pts.Length >= 4, "flattened contour too short");
        Assert.True(Math.Abs(pts[1].X - 64.92666666666668) < 0.01 && Math.Abs(pts[1].Y - 20.171666666666667) < 0.01, "line end missing");
        int arcStart = 2;
        Assert.True(Math.Abs(pts[arcStart].X - 64.92666666666668) < 0.01 && Math.Abs(pts[arcStart].Y - 20.171666666666667) < 0.01, "arc must start at the line's end (pen not advanced)");
        for (int pi = arcStart; pi < pts.Length; pi++)
        {
            double px = pts[pi].X;
            double py = pts[pi].Y;
            bool onQuarter = px >= 64.9 && px <= 68.1 && py >= 17.0 && py <= 20.3;
            if (!onQuarter)
            {
                // rest of the figure (top line + inner band) leaves the quarter
                break;
            }

            double dist = Math.Sqrt(((px - 67.92666666666668) * (px - 67.92666666666668)) + ((py - 20.171666666666667) * (py - 20.171666666666667)));
            Assert.True(Math.Abs(dist - 3.0) < 0.2, $"arc point ({px:F2},{py:F2}) at r={dist:F2} — inflated arc (C blob)");
        }
    }


    [Fact]
    public void ZTabItem_Single_Classic_CornerDump()
    {
        // Isolate the classic TabItem decorator's corner rendering: ONE unselected item,
        // empty header, no TabPanel/selection interaction.
        var item = new TabItem
        {
            Header = "",
            Width = 140,
            Height = 28,
            IsSelected = false,
        };
        var front = new TabItem
        {
            Header = "",
            Width = 52.92666666666667,
            Height = 28.343333333333334,
            IsSelected = true,
        };
#pragma warning disable IDE0058
        var host = new System.Windows.Controls.Canvas();
        host.Children.Add(front);
        host.Children.Add(item);
        System.Windows.Controls.Canvas.SetLeft(front, 12);
        System.Windows.Controls.Canvas.SetTop(front, 16);
        System.Windows.Controls.Canvas.SetLeft(item, 64.92666666666667);
        System.Windows.Controls.Canvas.SetTop(item, 17.171666666666667);
#pragma warning restore IDE0058
        var window = new Window { Width = 200, Height = 100, Content = host };
        window.Show();
        try
        {
            Flush();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();
            Flush();
            byte[] bytes = source.ReadbackRgba().Span.ToArray();
            const int w2 = 200;
            int cornerRun = 0;
            for (int y = 18; y < 48; y++)
            {
                int run = 0;
                for (int x = 60; x < 80; x++)
                {
                    int o = ((y * w2) + x) * 4;
                    int g = Math.Max(bytes[o], Math.Max(bytes[o + 1], bytes[o + 2]));
                    if (g < 200)
                    {
                        run++;
                        if (run > cornerRun)
                        {
                            cornerRun = run;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }

            Assert.True(cornerRun <= 8, $"single classic corner {cornerRun}px — C blob");
        }
        finally
        {
            window.Close();
        }
    }

#pragma warning restore CA1305, IDE0047, IDE0048, IDE0058, IDE0060, IDE0011

    [Fact]
    public void ZTabControl_Aero2_NoRolledCornerArtifact()
    {
        // Regression gate for the classic + Torch-theme TabControl tab strips: the classic
        // single-host template renders the corner cleanly; the Torch theme's fixed template
        // (single IsItemsHost, no ScrollViewer/double host) must match.
        VerifyTabStrip(custom: false, blackWindow: false);
        VerifyTabStrip(custom: false, blackWindow: true);
        VerifyTabStrip(custom: true, blackWindow: false);

        // The half-circle "C" is the CLASSIC theme's rolled-corner TabItem geometry
        // (arc + 45° band). Windows WPF renders Aero2/flat tabs; Nova defaults to the
        // Classic theme, so the Torch UI selects aero2 via NOVA_THEME (UiManager sets it
        // before the first window — the live process binding is the first, so it takes).
        // This gate asserts the process theme's corner: classic's rolled design renders,
        // aero2's stays flat. The testhost binds Classic, so the default run gates the
        // rolled side; the aero2 side is verified live (the smoke + Torch UI).
        VerifyTabStrip(custom: false, blackWindow: false);
        VerifyTabStrip(custom: false, blackWindow: false, noText: true);
    }

    private static void VerifyTabStrip(bool custom, bool blackWindow, bool noText = false)
    {
        const int w = 640, h = 160;
        ControlTemplate template;
        if (custom)
        {
            template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'>" +
                "<Grid ClipToBounds='True' SnapsToDevicePixels='True' KeyboardNavigation.TabNavigation='Local'>" +
                "<Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='0'/></Grid.ColumnDefinitions>" +
                "<Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition/></Grid.RowDefinitions>" +
                "<TabPanel x:Name='HeaderPanel' Panel.ZIndex='1' Grid.Column='0' Grid.Row='0' Margin='2,2,2,0' " +
                "IsItemsHost='True'/>" +
                "<Border x:Name='ContentPanel' BorderBrush='Transparent' " +
                "BorderThickness='{TemplateBinding BorderThickness}' Background='Transparent' " +
                "Grid.Column='0' Grid.Row='1'>" +
                "<ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent' " +
                "Margin='{TemplateBinding Padding}'/></Border>" +
                "</Grid></ControlTemplate>");
        }
        else
        {
            template = null!; // the implicit classic template
        }

        var tabs = new TabControl
        {
            Width = 620,
            Height = 130,
            Background = Brushes.Transparent,
            Items =
            {
                new TabItem { Header = noText ? "" : "Log", Width = 52.92666666666667, Height = 28.343333333333334 },
                new TabItem { Header = noText ? "" : "Configuration", Width = 140, Height = 28 },
            },
        };
        if (template != null)
        {
            tabs.Template = template;
        }

        var window = new Window { Width = w, Height = h, Content = tabs };
        if (blackWindow)
        {
            window.Background = Brushes.Black;
        }
        window.Show();
        try
        {
            Flush();
            var source = PresentationSource.FromVisual(window) as Nova.SdlSource.SdlPresentationSource;
            Assert.NotNull(source);
            source.EnableReadback();
            source.Present();
            source.Present();
            Flush();
            ReadOnlySpan<byte> p = source.ReadbackRgba().Span;
            byte[] bytes = p.ToArray();

            // first tab's left-top corner: tab starts at x=12; region x=10..30, y=20..42
            int cornerRun = 0;
            for (int y = 20; y < 42; y++)
            {
                int run = 0;
                for (int x = 10; x < 30; x++)
                {
                    int o = ((y * w) + x) * 4;
                    int g = Math.Max(bytes[o], Math.Max(bytes[o + 1], bytes[o + 2]));
                    if (g is >= 20 and < 200)
                    {
                        run++;
                        if (run > cornerRun)
                        {
                            cornerRun = run;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }

            // boundary corner: item1's left-top corner (item0 12..65, item1 65..) — the visible C region
            int boundaryRun = 0;
            for (int y = 18; y < 40; y++)
            {
                int run = 0;
                for (int x = 62; x < 74; x++)
                {
                    int o = ((y * w) + x) * 4;
                    int g = Math.Max(bytes[o], Math.Max(bytes[o + 1], bytes[o + 2]));
                    if (g < 200)
                    {
                        run++;
                        if (run > boundaryRun)
                        {
                            boundaryRun = run;
                        }
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }


            // The classic corner renders the thin design band since the Poly* walker pen fix
            // (the walkers never advanced penX/penY, so the corner arcs' endpoint-to-center
            // math received the figure start and inflated the radius ~4.5x into the "C" blob).
            // Gate: corner run stays thin (<=8; the top-band row is excluded by the y=18 start).
            Assert.True(boundaryRun <= 8, $"{(custom ? "theme" : "classic")} corner {boundaryRun}px — C blob (flattener walker stack)");
            Assert.True(cornerRun <= 5, $"{(custom ? "theme" : "classic")} tab corner stroke {cornerRun}px — half-circle artifact");

            if (blackWindow)
            {
                // the classic notch: near-black (the window) showing through the corner bands
                int blackRun = 0;
                for (int y = 20; y < 42; y++)
                {
                    int run = 0;
                    for (int x = 10; x < 30; x++)
                    {
                        int o = ((y * w) + x) * 4;
                        int g = Math.Max(bytes[o], Math.Max(bytes[o + 1], bytes[o + 2]));
                        if (g <= 10)
                        {
                            run++;
                            if (run > blackRun)
                            {
                                blackRun = run;
                            }
                        }
                        else
                        {
                            run = 0;
                        }
                    }
                }

                Assert.True(blackRun <= 2, $"black window: black notch run {blackRun}px — C artifact present");
            }
        }
        finally
        {
            window.Close();
        }
    }


    [Fact]
    public void ControlTemplate_Button_RendersChromeAndContent()
    {
        var button = new Button
        {
            Content = "Go",
            Width = 120,
            Height = 32,
            Background = Brushes.Red,
            Template = CreateButtonTemplate()
        };

        var window = new Window { Width = 200, Height = 100, Content = button };
        window.Show();
        try
        {
            Flush();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();

            // The templated button's Border chrome (Background=Red) must rasterize:
            // red-dominant pixels in the button's region.
            ReadOnlySpan<byte> p = source.ReadbackRgba().Span;
            Assert.True(HasRedPixels(p), "ControlTemplate chrome must render red pixels");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DataTemplate_ItemsControl_RendersBoundItems()
    {
        var items = new ItemsControl
        {
            ItemTemplate = CreateItemTemplate(),
            ItemsSource = new[]
            {
                new Item("a", new SolidColorBrush(Color.FromRgb(255, 0, 0))),
                new Item("b", new SolidColorBrush(Color.FromRgb(0, 255, 0))),
                new Item("c", new SolidColorBrush(Color.FromRgb(0, 0, 255)))
            }
        };

        var window = new Window { Width = 200, Height = 160, Content = items };
        window.Show();
        try
        {
            Flush();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();

            ReadOnlySpan<byte> p = source.ReadbackRgba().Span;
            Assert.True(HasRedPixels(p), "DataTemplate item 1 (red) must render");
            Assert.True(HasBrightGreenPixels(p), "DataTemplate item 2 (green) must render");
            Assert.True(HasBluePixels(p), "DataTemplate item 3 (blue) must render");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void AttachedProperties_GridCanvasDock_LayoutAndCustom()
    {        // Framework attached properties: Grid.Row/Column, Canvas.Left/Top, DockPanel.Dock.
        var header = new Border
        {
            Height = 24,
            Background = Brushes.Blue,
            Child = new TextBlock { Text = "header" }
        };
        DockPanel.SetDock(header, Dock.Top);

        var redRect = new Rectangle { Width = 20, Height = 20, Fill = Brushes.Red };
        var canvas = new Canvas
        {
            Width = 120,
            Height = 40,
            Background = Brushes.LightGray,
            Children = { redRect }
        };
        Canvas.SetLeft(redRect, 10);
        Canvas.SetTop(redRect, 10);

        var label = new TextBlock { Text = "area3", VerticalAlignment = VerticalAlignment.Center };
        TrackedProp.SetMark(label, "custom-attached-ok");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(canvas, 0);
        Grid.SetColumn(canvas, 0);
        _ = grid.Children.Add(canvas);
        Grid.SetRow(label, 0);
        Grid.SetColumn(label, 1);
        _ = grid.Children.Add(label);

        var dock = new DockPanel
        {
            Children =
            {
                header,
                grid
            }
        };

        var window = new Window { Width = 240, Height = 160, Content = dock };
        window.Show();
        try
        {
            Flush();
            Assert.Equal(24, header.ActualHeight);
            Assert.Equal(0, Grid.GetRow(canvas));
            Assert.Equal(1, Grid.GetColumn(label));
            Assert.Equal(10, Canvas.GetLeft(redRect));
            Assert.Equal(Dock.Top, DockPanel.GetDock(header));
            Assert.Equal("custom-attached-ok", TrackedProp.GetMark(label));

            // The attached-layout visuals must rasterize: header blue + canvas red rect.
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            source.Present();
            source.Present();
            ReadOnlySpan<byte> p = source.ReadbackRgba().Span;
            Assert.True(HasBluePixels(p), "DockPanel.Dock header (blue) must render");
            Assert.True(HasRedPixels(p), "Canvas.Left/Top rect (red) must render");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The invalidation-chain regression guard: a runtime property change on a LIVE
    /// window must reach the slave graph and the pixels through the real Dispatcher
    /// loop — a DispatcherTimer (promoted by the Linux message loop) mutates content,
    /// the drain runs the Render-priority pass, and the loop's present gate fires —
    /// not just the initial frame. Verifies BOTH a brush fill change and a text change
    /// produce a DIFFERENT readback. Note: ReadbackRgba returns the last-presented
    /// frame, so a present must follow the change drain (exactly what the loop's
    /// present-after-Render-drain gate does).
    /// </summary>
    [Fact]
    public void RuntimeChanges_RepaintThroughDispatcherLoop()
    {
        var text = new TextBlock { Text = "before" };
        var rect = new Rectangle { Width = 60, Height = 30, Fill = Brushes.Red };
        var panel = new StackPanel
        {
            Children =
            {
                text,
                rect
            }
        };
        var window = new Window { Width = 240, Height = 120, Content = panel };
        window.Show();
        try
        {
            Flush();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();

            // Initial frame (the window's content).
            Flush();
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> before = source.ReadbackRgba();
            Assert.True(HasRedPixels(before.Span), "initial rect must render red");

            // Runtime mutations driven the way a real app drives them: from a
            // DispatcherTimer Tick on the dispatcher thread. The Linux message loop
            // promotes the timer, the drain runs the layout/render pass, and the
            // loop presents because Render-priority work ran.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                if (ticks == 1)
                {
                    rect.Fill = Brushes.Blue;
                }
                else if (ticks == 2)
                {
                    text.Text = "after";
                }
                else
                {
                    timer.Stop();
                }
            };
            timer.Start();

            // Drain each timer tick (the loop's PromoteTimers + DrainLinuxQueue +
            // present-after-Render-drain), presenting after the drain like the loop does.
            while (timer.IsEnabled || ticks < 2)
            {
                Flush();
                source.Present();
            }

            ReadOnlyMemory<byte> after = source.ReadbackRgba();
            Assert.NotEqual(before.ToArray(), after.ToArray());
            Assert.True(HasBluePixels(after.Span), "changed rect must render blue");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The template-binding invalidation guard: a CONTROL property whose metadata is
    /// <c>FrameworkPropertyMetadataOptions.None</c> (Control.Background) repaints only
    /// because the template's <c>{TemplateBinding Background}</c> propagates the change
    /// to the Border (AffectsRender) and that invalidates the visual. If the dependent
    /// chain breaks on Linux, the sample's Button.Background change on a timer never
    /// reaches PostRender and the frame stays stale. Driven through the real
    /// DispatcherTimer + Flush + present gate like the app loop does.
    /// </summary>
    [Fact]
    public void TemplateBinding_ButtonBackground_RepaintsThroughLoop()
    {
        var button = new Button
        {
            Width = 160,
            Height = 40,
            Content = "Templated",
            Background = Brushes.Red,
            Template = CreateButtonTemplate(),
        };
        var window = new Window { Width = 240, Height = 100, Content = button };
        window.Show();
        try
        {
            Flush();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();

            Flush();
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> before = source.ReadbackRgba();
            Assert.True(HasRedPixels(before.Span), "initial button must render red via template binding");

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                if (ticks == 1)
                {
                    button.Background = Brushes.Blue;
                }
                else
                {
                    timer.Stop();
                }
            };
            timer.Start();

            while (timer.IsEnabled || ticks < 1)
            {
                Flush();
                source.Present();
            }

            ReadOnlyMemory<byte> after = source.ReadbackRgba();
            Assert.NotEqual(before.ToArray(), after.ToArray());
            Assert.True(HasBluePixels(after.Span), "changed button background must render blue");
        }
        finally
        {
            window.Close();
        }
    }

    private static ControlTemplate CreateButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate { DataType = typeof(Item) };
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var rect = new FrameworkElementFactory(typeof(Rectangle));
        rect.SetValue(Rectangle.WidthProperty, 12.0);
        rect.SetValue(Rectangle.HeightProperty, 12.0);
        rect.SetValue(Rectangle.FillProperty, new Binding("Color"));
        panel.AppendChild(rect);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextProperty, new Binding("Label"));
        text.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 0, 0));
        panel.AppendChild(text);
        template.VisualTree = panel;
        return template;
    }

    private static void Flush()
    {
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
    }

    private static bool HasRedPixels(ReadOnlySpan<byte> p)
    {
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            if (p[i] > 100 && p[i + 1] < 60 && p[i + 2] < 60)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBrightGreenPixels(ReadOnlySpan<byte> p)
    {
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            if (p[i + 1] > 100 && p[i] < 60 && p[i + 2] < 60)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBluePixels(ReadOnlySpan<byte> p)
    {
        for (int i = 0; i + 3 < p.Length; i += 4)
        {
            if (p[i + 2] > 100 && p[i] < 60 && p[i + 1] < 60)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Bound item for the DataTemplate.</summary>
    private sealed record Item(string Label, Brush Color);

    /// <summary>Consumer-defined attached property registered via public API.</summary>
    private static class TrackedProp
    {
        public static readonly DependencyProperty MarkProperty =
            DependencyProperty.RegisterAttached("Mark", typeof(string), typeof(TrackedProp), new PropertyMetadata(null));

        public static string? GetMark(DependencyObject element)
        {
            return (string?)element.GetValue(MarkProperty);
        }

        public static void SetMark(DependencyObject element, string? value)
        {
            element.SetValue(MarkProperty, value);
        }
    }
}
