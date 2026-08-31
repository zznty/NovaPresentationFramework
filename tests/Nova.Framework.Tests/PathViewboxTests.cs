// Path-in-Viewbox render regression (WPFGallery GitHub icon, 2026-08-26).
//
// The gallery's HeaderTile "WPF GitHub" icon is a <Path> whose Data is a 600-point
// StreamGeometry inside a <Viewbox>. WPF-side probing showed the Path's OnRender produced
// a DrawingGroup with correct bounds, yet no DUCE command for the geometry ever reached
// the slave — the icon rendered nothing. These tests pin the pipeline end-to-end through
// the real DUCE transport and readback, with a trivial triangle and the actual geometry.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nova.SdlSource;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;
using SdlEventType = Silk.NET.SDL.EventType;

namespace Nova.Framework.Tests;

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void SecondWindow_CloseRequested_RoutesThroughMainPump()
    {
        // The app loop pumps ONLY the main source; the SDL close-requested for a second
        // window arrives on that pump and must be forwarded to the owning source, or the
        // second window's compositor close button does nothing.
        var main = new Window { Width = 200, Height = 100, Title = "main" };
        var second = new Window { Width = 200, Height = 100, Title = "second" };
        main.Show();
        second.Show();
        try
        {
            var s1 = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(main));
            var s2 = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(second));
            _ = PumpOnePass(s1); // drain offscreen startup noise
            uint secondId = GetWindowId(s2);

            var ev = new Silk.NET.SDL.Event
            {
                Window = new Silk.NET.SDL.WindowEvent
                {
                    Type = SdlEventType.WindowCloseRequested,
                    WindowID = secondId
                }
            };
            Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref ev)));

            // ONE pass on the MAIN source must deliver the routed close to the second.
            _ = PumpOnePass(s1);
            _ = PumpOnePass(s1);
            Assert.False(second.IsVisible, "the second window must close from the routed close-requested");
        }
        finally
        {
            main.Close();
            second.Close();
        }
    }

    [Fact]
    public void TextBox_DoubleClick_SelectsWord()
    {
        // The double-click word selection drives SelectionWordBreaker.IsAtWordBoundary
        // which classified characters via kernel32 GetStringTypeEx — a DllNotFound that
        // crashed the gallery on every TextBox double-click. The managed classifier
        // must select the word under the pointer.
        var textBox = new TextBox { Text = "hello world", Width = 220, FontSize = 14 };
        var window = new Window { Width = 320, Height = 120, Content = textBox };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source); // drain offscreen startup noise
            window.UpdateLayout();

            System.Windows.Point center = textBox.TranslatePoint(new System.Windows.Point(12, 12), window);
            System.Windows.Point device = source.CompositionTarget.TransformToDevice.Transform(center);
            int cx = (int)Math.Round(device.X);
            int cy = (int)Math.Round(device.Y);
            uint windowId = GetWindowId(source);

            for (int click = 0; click < 2; click++)
            {
                PushButton(SdlEventType.MouseButtonDown, windowId, down: true, cx, cy);
                _ = PumpOnePass(source);
                PushButton(SdlEventType.MouseButtonUp, windowId, down: false, cx, cy);
                _ = PumpOnePass(source);
            }

            // The Win32 word selection includes the trailing whitespace after the word.
            Assert.StartsWith("hello", textBox.SelectedText, StringComparison.Ordinal);
            Assert.True(textBox.SelectionLength >= 5, $"word must be selected, got {textBox.SelectionLength}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void CheckBox_Indeterminate_RendersBoxAndCenteredDash()
    {
        // The Fluent indeterminate state composes the filled box + the E9AE dash glyph.
        // The user reported the dash smaller and left-offset; the pixels must show the
        // box fill plus a dash centered inside it.
        var window = new Window { Width = 120, Height = 80 };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Resources/Theme/Light.xaml")
        });
        var checkBox = new CheckBox { IsThreeState = true, IsChecked = null, Width = 40, Height = 40 };
        window.Content = checkBox;
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();

            ReadOnlySpan<byte> span = source.ReadbackRgba().Span;
            int w = (int)window.ActualWidth;
            int h = (int)window.ActualHeight;

            // The checkbox box: the accent fill (blue-ish) region near the top-left of
            // the checkbox. Scan the upper 40x40 for the box bounds.
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int y = 0; y < Math.Min(h, 44); y++)
            {
                for (int x = 0; x < Math.Min(w, 44); x++)
                {
                    int i = ((y * w) + x) * 4;
                    if (span[i + 2] > 120 && span[i] < 100 && span[i + 1] <= 130)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            Assert.True(minX <= maxX, "the indeterminate box fill must render");
            int boxCx = (minX + maxX) / 2;

            // The dash: a light horizontal bar INSIDE the box. Scan the box's interior
            // rows (the glyph is lighter than the accent fill) for the widest light run
            // and check it is horizontally centered on the box.
            int dashStart = -1, dashEnd = -1, dashRow = -1;
            for (int y = minY + 2; y <= maxY - 2; y++)
            {
                int runStart = -1;
                for (int x = minX; x <= maxX; x++)
                {
                    int i = ((y * w) + x) * 4;
                    bool light = span[i] > 180 && span[i + 1] > 180 && span[i + 2] > 180;
                    if (light && runStart < 0)
                    {
                        runStart = x;
                    }

                    if (!light && runStart >= 0)
                    {
                        if (x - 1 - runStart > dashEnd - dashStart)
                        {
                            dashStart = runStart;
                            dashEnd = x - 1;
                            dashRow = y;
                        }

                        runStart = -1;
                    }
                }
            }

            Assert.True(dashStart >= 0, "the indeterminate dash must render inside the box");
            int dashCx = (dashStart + dashEnd) / 2;
            _ = dashRow;
            Assert.True(Math.Abs(dashCx - boxCx) <= 3, $"dash must be centered: dashCx={dashCx} boxCx={boxCx} ({dashStart}-{dashEnd} vs {minX}-{maxX})");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Cursor_StreamLoad_NoOpOnLinux()
    {
        // GridViewColumnHeader's splitter cursor loads via user32 LoadImageCursor; on
        // Linux that DllNotFound crashed the gallery on every GridView page. The trap
        // must make both the stream ctor and the column-header template path safe.
        using Cursor streamCursor = new Cursor(new MemoryStream(8));
        Assert.NotNull(streamCursor);

        var header = new GridViewColumnHeader();
        _ = header.ApplyTemplate(); // hooks the gripper events -> loads the split cursor
        header.Measure(new Size(120, 30));
    }

    [Fact]
    public void Viewbox_Path_Triangle_Renders()
    {
        var path = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M0,0 L50,0 L25,40 Z"), Fill = Brushes.Black };
        var viewbox = new Viewbox { Height = 52, Child = path };
        var window = new Window { Width = 200, Height = 100, Content = viewbox };
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();

            int dark = CountDark(source.ReadbackRgba());
            Assert.True(dark > 50, $"triangle in Viewbox must render dark pixels, got {dark}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Viewbox_Path_GitHubGeometry_Renders()
    {
        var shared = System.Windows.Media.Geometry.Parse(GitHubIconPath);
        shared.Freeze();
        var path = new System.Windows.Shapes.Path { Data = shared, Fill = Brushes.Black };
        var viewbox = new Viewbox { Height = 52, Margin = new Thickness(-20, 0, 0, 0), Child = path };
        var presenter = new ContentPresenter { Content = viewbox };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(presenter, 0);
        _ = grid.Children.Add(presenter);
        var button = new Button { Content = grid, Padding = new Thickness(24) };

        // Mirror the dashboard: a tall hero row above the tile row, both inside a ScrollViewer.
        var pageGrid = new Grid();
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var hero = new System.Windows.Controls.Border { Height = 100, Background = Brushes.LightGray };
        Grid.SetRowSpan(hero, 2);
        _ = pageGrid.Children.Add(hero);
        Grid.SetRow(button, 1);
        _ = pageGrid.Children.Add(button);
        var scroll = new ScrollViewer { Content = pageGrid };
        var window = new Window { Width = 300, Height = 260, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();

            ReadOnlyMemory<byte> fb = source.ReadbackRgba();
            int dark = CountDark(fb);
            Assert.True(dark > 100, $"GitHub geometry in Viewbox must render dark pixels, got {dark}");

            // The icon must land right below the 100-tall hero (window is 260 tall): any
            // visible dark icon pixel proves the ScrollViewer+Grid chain didn't displace it
            // out of the window.
            int darkTop = CountDark(fb[..(300 * 4 * 150)]);
            Assert.True(darkTop > 50, $"icon must be visible in the upper band, got {darkTop}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void FluentTheme_SystemThemeMode_ResolvesButtonBrushes()
    {
        // The Fluent color dictionaries (ButtonBackground #B3FFFFFF, ButtonBorderBrush gradient)
        // live in Resources/Theme/Light.xaml and are loaded ONLY through ThemeManager when
        // Application.ThemeMode != ThemeMode.None. With the default None the navigation cards
        // rendered no fill and no border. The startup path (ThemeMode=System before Resources
        // init) is stock WPF; this test pins the window-scoped rendering end to end: the
        // resolved brushes, the fill, and the gradient border whose RelativeTransform puts the
        // darker strip on the BOTTOM edge (the Windows look, not the top).
        var window = new Window { Width = 200, Height = 120 };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Resources/Theme/Light.xaml")
        });
        var button = new Button
        {
            Width = 140,
            Height = 44,
            Content = "x",
            Style = Assert.IsType<Style>(window.FindResource("DefaultButtonStyle"))
        };
        window.Content = button;
        window.Show();
        try
        {
            window.UpdateLayout();
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.EnableReadback();
            _ = PumpOnePass(source);
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();

            Assert.NotNull(button.Background);
            Assert.NotNull(button.BorderBrush);

            ReadOnlySpan<byte> span = source.ReadbackRgba().Span;
            int w = (int)window.ActualWidth;
            int h = (int)window.ActualHeight;
            int nearWhite = 0;
            for (int i = 0; i + 3 < span.Length; i += 4)
            {
                if (span[i] > 245 && span[i + 1] > 245 && span[i + 2] > 245)
                {
                    nearWhite++;
                }
            }

            // The button fill #B3FFFFFF over the light window surface must produce a
            // large near-white region; a missing fill leaves the interior transparent.
            Assert.True(nearWhite > 500, $"button fill must render near-white pixels, got {nearWhite}");
            File.WriteAllBytes("/tmp/fluent_button.rgba", source.ReadbackRgba().ToArray());
            File.WriteAllText("/tmp/fluent_button.size", $"{w} {h}");

            // The Fluent button border is a 1px gradient stroke around the button rect whose
            // RelativeTransform puts the darker stop on the BOTTOM edge (the Windows look —
            // the strip under the card, not on top of it). Scan the bottom edge band.
            int border = 0;
            for (int y = 76; y < 88; y++)
            {
                for (int x = 30; x < 170; x++)
                {
                    int i = ((y * w) + x) * 4;
                    byte r = span[i];
                    byte g = span[i + 1];
                    byte b = span[i + 2];
                    if (r < 235 && r > 140 && Math.Abs(r - g) < 8 && Math.Abs(g - b) < 8)
                    {
                        border++;
                    }
                }
            }

            Assert.True(border > 20, $"button border must render at the bottom edge, got {border} darker pixels");
        }
        finally
        {
            window.Close();
        }
    }

    private static int CountDark(ReadOnlyMemory<byte> pixels)
    {
        int dark = 0;
        ReadOnlySpan<byte> span = pixels.Span;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            if (span[i] < 60 && span[i + 1] < 60 && span[i + 2] < 60)
            {
                dark++;
            }
        }

        return dark;
    }

    /// <summary>The WPFGallery Resources/PageStyles.xaml GitHubIconGeometry path data.</summary>
    private const string GitHubIconPath = "M21.7999992370605,0L19.220495223999,0.26007080078125 16.81787109375,1.00595712661743 14.6436157226563,2.18616962432861 12.7492189407349,3.74921894073486 11.1861696243286,5.64361572265625 10.0059566497803,7.81787109375 9.26007080078125,10.2204961776733 9,12.8000001907349 9.65248012542725,16.8459720611572 11.4694375991821,20.3591785430908 14.2401514053345,23.1291217803955 17.7539005279541,24.9453010559082 18.4305686950684,24.8080005645752 18.6273498535156,24.3296756744385 18.6207065582275,23.4247951507568 18.609375,21.9468746185303 16.4340572357178,22.0373229980469 15.1187467575073,21.4822216033936 14.4708204269409,20.7821025848389 14.2976503372192,20.4375 13.8297338485718,19.5214366912842 13.3685493469238,18.947265625 12.8765497207642,18.5656261444092 12.3995819091797,18.1091804504395 12.4844465255737,17.87890625 12.7874250411987,17.7974605560303 12.9647998809814,17.7875003814697 13.8134965896606,18.0311241149902 14.4276065826416,18.4802703857422 14.8007507324219,18.9127178192139 14.926549911499,19.1062507629395 15.8880548477173,20.1437015533447 16.9443283081055,20.494140625 17.9229640960693,20.416259765625 18.6515502929688,20.1687507629395 18.9645938873291,19.1242198944092 19.4640502929688,18.4593753814697 17.3543262481689,18.0241260528564 15.4833002090454,17.014066696167 14.1450357437134,15.1450166702271 13.6336002349854,12.1328001022339 13.9853601455688,10.2268438339233 14.9500007629395,8.69764995574951 14.7027282714844,7.54188776016235 14.7441072463989,6.53565359115601 15.0765495300293,5.30859994888306 15.2825078964233,5.28076791763306 15.9191312789917,5.34375619888306 17.0145378112793,5.71729135513306 18.596851348877,6.62109994888306 21.799976348877,6.19062519073486 25.004674911499,6.62265014648438 26.5845413208008,5.71818733215332 27.6791000366211,5.34472513198853 28.315746307373,5.28210020065308 28.5218753814697,5.31015014648438 28.8556652069092,6.53784370422363 28.8976573944092,7.5438346862793 28.6499996185303,8.69764995574951 29.6154251098633,10.2268533706665 29.9656257629395,12.1328001022339 29.453296661377,15.1497011184692 28.1123065948486,17.0164012908936 26.2366523742676,18.020601272583 24.120325088501,18.4500007629395 24.7275562286377,19.3355484008789 24.9890747070313,20.8187503814697 24.9804744720459,23.0584030151367 24.9718742370605,24.3312511444092 25.1693305969238,24.8128852844238 25.8531246185303,24.9453010559082 29.3641395568848,23.1273632049561 32.1326217651367,20.3568344116211 33.948070526123,16.8442134857178 34.5999984741211,12.8000001907349 34.3399276733398,10.2204961776733 33.5940399169922,7.81787109375 32.4138298034668,5.64361572265625 30.8507804870605,3.74921894073486 28.9563827514648,2.18616962432861 26.7821273803711,1.00595712661743 24.3795032501221,0.26007080078125 21.7999992370605,0z";
}
