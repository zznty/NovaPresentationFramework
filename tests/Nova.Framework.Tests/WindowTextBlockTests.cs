using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.Mil;
using Nova.Sdl;
using Nova.SdlSource;
using Nova.Vulkan;

namespace Nova.Framework.Tests;

public sealed partial class WindowTextBlockTests
{
    public WindowTextBlockTests()
    {
        _ = Native.SetEnv("SDL_VIDEO_DRIVER", "offscreen", 1);
        _ = Native.SetEnv("SDL_VIDEODRIVER", "offscreen", 1);
    }

    [Fact]
    public void Window_Constructs()
    {
        var window = new Window { Width = 200, Height = 80 };
        Assert.Equal(200, window.Width);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void Window_ShowThenClose_DrainsDuceBindingsAndChannelMappings()
    {
        var rect = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red };
        var window = new Window { Width = 200, Height = 80, Content = rect };
        window.Show();
        Assert.True(window.IsVisible);
        Assert.True(CountBindings() >= 1);
        Assert.True(CountChannelMappings() >= 1);

        // Window.Close() on Linux must dispose the SdlPresentationSource (via the
        // CloseWindowFromWmClose Linux branch), which detaches the DuceRuntime binding and
        // drains its channel mappings — exactly as popup sources do.
        window.Close();
        Assert.Equal(0, CountBindings());
        Assert.Equal(0, CountChannelMappings());
    }

    private static int CountBindings()
    {
        FieldInfo field = typeof(DuceRuntime).GetField(
            "s_bindings",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }

    private static int CountChannelMappings()
    {
        FieldInfo field = typeof(DuceRuntime).GetField(
            "s_graphsByChannel",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((System.Collections.ICollection)field.GetValue(null)!).Count;
    }

    [Fact]
    public void TextBlock_TypefaceResolves()
    {
        var text = new TextBlock
        {
            Text = "Hi",
            FontFamily = new FontFamily("DejaVu Sans"),
            FontSize = 16
        };
        Assert.Equal("Hi", text.Text);
        var typeface = new Typeface(
            new FontFamily("DejaVu Sans"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyph));
        Assert.True(glyph.GlyphCount > 0);
    }

    [Fact]
    public void Window_Show_Rectangle_LaysOut()
    {
        var window = new Window { Width = 200, Height = 80 };
        var rect = new Rectangle
        {
            Width = 40,
            Height = 20,
            Fill = Brushes.Red
        };
        window.Content = rect;
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            Assert.True(rect.IsMeasureValid, $"measureValid desired={rect.DesiredSize}");
            Assert.Equal(40, rect.ActualWidth);
            Assert.Equal(20, rect.ActualHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_TextBlock_LaysOut()
    {
        var window = new Window { Width = 200, Height = 80 };
        var text = new TextBlock
        {
            Text = "Hi",
            FontFamily = new FontFamily("DejaVu Sans"),
            FontSize = 16
        };
        window.Content = text;
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            Assert.True(text.IsMeasureValid, $"measureValid desired={text.DesiredSize}");
            Assert.True(text.ActualWidth > 0, $"actual={text.ActualWidth}x{text.ActualHeight} desired={text.DesiredSize}");
            Assert.True(text.ActualHeight > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_TextBlock_NbspAndTab_LaysOut()
    {
        var window = new Window { Width = 200, Height = 80 };
        var text = new TextBlock
        {
            Text = "A\u00A0B\tC",
            FontFamily = new FontFamily("DejaVu Sans"),
            FontSize = 16
        };
        window.Content = text;
        window.Show();
        try
        {
            Assert.True(text.IsMeasureValid, $"measureValid desired={text.DesiredSize}");
            Assert.True(text.ActualWidth > 0, $"actual={text.ActualWidth}x{text.ActualHeight}");
            Assert.True(text.ActualHeight > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LoGetEscString_LinuxPinsExpectedWchars()
    {
        Type methods = Type.GetType(
            "MS.Internal.TextFormatting.UnsafeNativeMethods, PresentationCore",
            throwOnError: true)!;
        Type escType = Type.GetType("MS.Internal.TextFormatting.EscStringInfo, PresentationCore", throwOnError: true)!;
        object esc = Activator.CreateInstance(escType)!;
        MethodInfo method = methods.GetMethod(
            "LoGetEscString",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("LoGetEscString missing");
        object[] args = [esc];
        Assert.Null(method.Invoke(null, args));
        esc = args[0];

        AssertEsc((IntPtr)escType.GetField("szParaSeparator")!.GetValue(esc)!, '\u2029');
        AssertEsc((IntPtr)escType.GetField("szLineSeparator")!.GetValue(esc)!, '\u2028');
        AssertEsc((IntPtr)escType.GetField("szHidden")!.GetValue(esc)!, '\uFFFF');
        AssertEsc((IntPtr)escType.GetField("szNbsp")!.GetValue(esc)!, '\u00A0');
        AssertEsc((IntPtr)escType.GetField("szObjectTerminator")!.GetValue(esc)!, '\u0009');
        AssertEsc((IntPtr)escType.GetField("szObjectReplacement")!.GetValue(esc)!, '\uFFFC');
    }

    [Fact]
    public void Window_Show_Dispatch_ProcessInputSeesMouseAndText()
    {
        var window = new Window { Width = 200, Height = 80 };
        var rect = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red };
        window.Content = rect;
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            int reports = 0;
            InputManager.Current.PreProcessInput += OnPreProcess;
            try
            {
                source.Dispatch(new SdlEvent(
                    SdlEventKind.MouseButtonDown,
                    WindowHandle.Invalid,
                    new Nova.Geometry.Point(12, 12),
                    Nova.Geometry.Vector.Zero,
                    Sdl.MouseButton.Left,
                    0,
                    null));
                source.Dispatch(new SdlEvent(
                    SdlEventKind.TextInput,
                    WindowHandle.Invalid,
                    Nova.Geometry.Point.Origin,
                    Nova.Geometry.Vector.Zero,
                    Sdl.MouseButton.None,
                    0,
                    "a"));
            }
            finally
            {
                InputManager.Current.PreProcessInput -= OnPreProcess;
            }

            Assert.True(reports > 0);
            return;

            void OnPreProcess(object sender, PreProcessInputEventArgs e)
            {
                _ = sender;
                _ = e;
                reports++;
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_Button_DispatchClick_RaisesClick()
    {
        var window = new Window { Width = 200, Height = 80 };
        var rect = new Rectangle
        {
            Width = 40,
            Height = 20,
            Fill = Brushes.Red
        };
        var button = new Button
        {
            Width = 80,
            Height = 40,
            Content = rect
        };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        window.Content = button;
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            Assert.True(button.ApplyTemplate() || button.Template is not null);
            Assert.True(VisualTreeHelper.GetChildrenCount(button) > 0);
            Assert.True(button.ActualWidth > 0, $"button ActualWidth={button.ActualWidth}");
            Assert.True(button.ActualHeight > 0, $"button ActualHeight={button.ActualHeight}");

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            System.Windows.Point center = button.TranslatePoint(
                new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2),
                window);
            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(center.X, center.Y),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));
            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonUp,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(center.X, center.Y),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));

            Assert.Equal(1, clicks);
            Assert.False(button.IsPressed);
            Assert.Null(Mouse.Captured);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_Button_MissClick_DoesNotRaiseClick()
    {
        var window = new Window { Width = 200, Height = 80 };
        var button = new Button
        {
            Width = 80,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Content = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red }
        };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        window.Content = button;
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(190, 70),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));
            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonUp,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(190, 70),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Left,
                0,
                null));

            Assert.Equal(0, clicks);
            Assert.False(button.IsPressed);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void MapMouseActions_MiddleAndRight_FollowWireOrder()
    {
        // RawMouseActions is internal to PresentationCore; assert the mapped
        // actions against its documented wire values (RawMouseActions.cs):
        // AbsoluteMove=0x10, Button1=0x40/0x80, Button2=0x100/0x200, Button3=0x400/0x800.
        const int wireAbsoluteMove = 0x10;
        const int wireButton1Press = 0x40;
        const int wireButton1Release = 0x80;
        const int wireButton2Press = 0x100;
        const int wireButton2Release = 0x200;
        const int wireButton3Press = 0x400;
        const int wireButton3Release = 0x800;

        MethodInfo mapMouseActions = typeof(SdlPresentationSource).GetMethod(
            "MapMouseActions",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MapMouseActions missing");

        int Invoke(SdlEvent ev)
        {
            return Convert.ToInt32(mapMouseActions.Invoke(null, [ev]), CultureInfo.InvariantCulture);
        }

        // WPF wire order: Button1=Left, Button2=Right, Button3=Middle.
        Assert.Equal(wireAbsoluteMove | wireButton3Press, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonDown,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Middle,
            0,
            null)));
        Assert.Equal(wireAbsoluteMove | wireButton3Release, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonUp,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Middle,
            0,
            null)));
        Assert.Equal(wireAbsoluteMove | wireButton2Press, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonDown,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Right,
            0,
            null)));
        Assert.Equal(wireAbsoluteMove | wireButton2Release, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonUp,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Right,
            0,
            null)));
        Assert.Equal(wireAbsoluteMove | wireButton1Press, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonDown,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Left,
            0,
            null)));
        Assert.Equal(wireAbsoluteMove | wireButton1Release, Invoke(new SdlEvent(
            SdlEventKind.MouseButtonUp,
            WindowHandle.Invalid,
            Nova.Geometry.Point.Origin,
            Nova.Geometry.Vector.Zero,
            Sdl.MouseButton.Left,
            0,
            null)));
    }

    [Fact]
    public void Window_Show_MiddleAndRightButton_DispatchReportsWireState()
    {
        var window = new Window
        {
            Width = 200,
            Height = 80,
            Content = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red }
        };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));

            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(12, 12),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Middle,
                0,
                null));
            Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.MiddleButton);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.RightButton);

            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonUp,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(12, 12),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Middle,
                0,
                null));
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.MiddleButton);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.RightButton);

            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonDown,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(12, 12),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Right,
                0,
                null));
            Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.RightButton);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.MiddleButton);

            source.Dispatch(new SdlEvent(
                SdlEventKind.MouseButtonUp,
                WindowHandle.Invalid,
                new Nova.Geometry.Point(12, 12),
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.Right,
                0,
                null));
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.RightButton);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.MiddleButton);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_DispatchKeyDown_KeyboardStateTracksWithoutThrow()
    {
        var window = new Window { Width = 200, Height = 80 };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));

            // Left Ctrl (VK 0x11) down: Keyboard.Modifiers must reflect Control and,
            // on Linux, must not throw DllNotFoundException (no user32 GetKeyState).
            source.Dispatch(new SdlEvent(
                SdlEventKind.KeyDown,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                (uint)KeyInterop.VirtualKeyFromKey(Key.LeftCtrl),
                null));
            Assert.Equal(ModifierKeys.Control, Keyboard.Modifiers & ModifierKeys.Control);
            Assert.True(Keyboard.IsKeyDown(Key.LeftCtrl));
            Assert.False(Keyboard.IsKeyUp(Key.LeftCtrl));
            Assert.Equal(KeyStates.Down, Keyboard.GetKeyStates(Key.LeftCtrl) & KeyStates.Down);

            // 'A' (VK 0x41) down then up.
            source.Dispatch(new SdlEvent(
                SdlEventKind.KeyDown,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                (uint)KeyInterop.VirtualKeyFromKey(Key.A),
                null));
            Assert.True(Keyboard.IsKeyDown(Key.A));
            source.Dispatch(new SdlEvent(
                SdlEventKind.KeyUp,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                (uint)KeyInterop.VirtualKeyFromKey(Key.A),
                null));
            Assert.False(Keyboard.IsKeyDown(Key.A));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_SetBounds_UpdatesRootRenderSize()
    {
        var window = new Window { Width = 200, Height = 80 };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Assert.Equal(200, window.ActualWidth, 1);
            Assert.Equal(80, window.ActualHeight, 1);

            source.SetBounds(0, 0, 400, 240, move: false, resize: true);

            Assert.Equal(400, window.ActualWidth, 1);
            Assert.Equal(240, window.ActualHeight, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_SizeToContentWidthAndHeight_SizesSdlWindowToContent()
    {
        // Regression: a top-level Window with SizeToContent=WidthAndHeight stayed at the
        // 800×600 CreateWindowFrame fallback (an STC window has NaN Width/Height, so the
        // parameter size is always 0 and nothing resized the SDL window). The SDL window
        // and its presenter must be resized to the laid-out content in device pixels.
        var border = new Border { Width = 100, Height = 50, Background = Brushes.Red };
        var window = new Window { SizeToContent = SizeToContent.WidthAndHeight, Content = border };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Assert.Equal(100, border.ActualWidth, 1);
            Assert.Equal(50, border.ActualHeight, 1);
            Assert.Equal(100, source.PixelWidth);
            Assert.Equal(50, source.PixelHeight);
            Assert.True(source.GetPlacement(out _, out _, out int width, out int height));
            Assert.Equal(100, width);
            Assert.Equal(50, height);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Show_SizeToContentWidth_SizesOnlyWidth()
    {
        // Dimension-specific STC: only the STC'd axis follows the content; the other axis
        // keeps the window's explicit size.
        var border = new Border { Width = 120, Height = 60, Background = Brushes.Red };
        var window = new Window { Width = 200, Height = 100, SizeToContent = SizeToContent.Width, Content = border };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Assert.Equal(120, source.PixelWidth);
            Assert.Equal(100, source.PixelHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_TextBox_TypeTextViaDispatch_TextUpdates()
    {
        var window = new Window { Width = 320, Height = 120 };
        var box = new TextBox { Width = 200, Height = 24, FontFamily = new FontFamily("DejaVu Sans") };
        window.Content = box;
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);

            window.UpdateLayout();
            Assert.True(box.ActualWidth > 0, $"box ActualWidth={box.ActualWidth}");
            Assert.True(box.ActualHeight > 0, $"box ActualHeight={box.ActualHeight}");
            Assert.True(box.IsKeyboardFocusWithin || box.Focus(), "TextBox did not receive focus");

            // Type "hi" via TextInput reports, then Backspace.
            source.Dispatch(new SdlEvent(
                SdlEventKind.TextInput,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                0,
                "h"));
            source.Dispatch(new SdlEvent(
                SdlEventKind.TextInput,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                0,
                "i"));
            Assert.Equal("hi", box.Text);

            // Backspace (VK 0x08) down+up.
            source.Dispatch(new SdlEvent(
                SdlEventKind.KeyDown,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                (uint)KeyInterop.VirtualKeyFromKey(Key.Back),
                null));
            source.Dispatch(new SdlEvent(
                SdlEventKind.KeyUp,
                WindowHandle.Invalid,
                Nova.Geometry.Point.Origin,
                Nova.Geometry.Vector.Zero,
                Sdl.MouseButton.None,
                (uint)KeyInterop.VirtualKeyFromKey(Key.Back),
                null));

            Assert.Equal("h", box.Text);
            Assert.True(box.CaretIndex >= 1, $"caretIndex={box.CaretIndex}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Debug_CaretVisibility_Mechanism()
    {
        var window = new Window { Width = 320, Height = 120 };
        var box = new TextBox { Width = 200, Height = 24, FontFamily = new FontFamily("DejaVu Sans") };
        window.Content = box;
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Console.WriteLine($"caret-diag: source-handle={source.Handle} IsWindows={OperatingSystem.IsWindows()}");
            _ = PumpOnePass(source);
            Console.WriteLine($"caret-diag: IsWindows={OperatingSystem.IsWindows()} after-pump");
            window.UpdateLayout();
            Console.WriteLine($"caret-diag: after-first-layout {box.ActualWidth}");
            Assert.True(box.IsKeyboardFocusWithin || box.Focus(), "TextBox did not receive focus");
            Console.WriteLine($"caret-diag: after-focus {box.CaretIndex}");

            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(box);
            Adorner[]? adorners = layer?.GetAdorners(box);
            Console.WriteLine($"caret-diag: adornerLayer={(layer is null ? "null" : "present")} adornersOnBox={(adorners is null ? "null" : adorners.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            // Walk the box's visual subtree (template children, e.g. the RenderScope) for
            // any adorner layer containing a caret adorner.
            int caretAdorners = 0;
            string? caretLayerOwner = null;
            void ScanDescendants(DependencyObject current)
            {
                if (current is System.Windows.UIElement element)
                {
                    AdornerLayer? probeLayer = AdornerLayer.GetAdornerLayer(element);
                    Adorner[]? probeAdorners = probeLayer?.GetAdorners(element);
                    if (probeAdorners is { Length: > 0 })
                    {
                        foreach (Adorner adorner in probeAdorners)
                        {
                            if (adorner.GetType().Name.Contains("Caret", StringComparison.Ordinal))
                            {
                                caretAdorners++;
                                caretLayerOwner = element.GetType().Name + ":" + element.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }

                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    ScanDescendants(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
                }
            }

            ScanDescendants(box);
            Console.WriteLine($"caret-diag: caretAdornersInSubtree={caretAdorners} layerOwner={caretLayerOwner}");

            // Read back pixels at the caret x-offset for an empty box.
            var frame = typeof(SdlPresentationSource)
                .GetProperty("Frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(source)!;
            var presenter = (IVulkanPresenter)frame.GetType().GetProperty("Presenter")!.GetValue(frame)!;
            presenter.EnableReadback();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            source.Present();
            source.Present();
            ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();
            Console.WriteLine($"caret-diag: px0={FormatHex(pixels.Span[0])},{FormatHex(pixels.Span[1])},{FormatHex(pixels.Span[2])},{FormatHex(pixels.Span[3])} len={pixels.Length}");
            int width = (int)Math.Sqrt(pixels.Length / 4);
            // Scan the box row for a dark (caret-colored) vertical run.
            int boxY = (int)box.TranslatePoint(new System.Windows.Point(0, box.ActualHeight / 2), window).Y;
            var darkColumns = new List<int>();
            for (int x = 0; x < width; x++)
            {
                int rowOffset = boxY * width;
                int byteOffset = (rowOffset + x) * 4;
                byte r = pixels.Span[byteOffset];
                byte g = pixels.Span[byteOffset + 1];
                byte b = pixels.Span[byteOffset + 2];
                if (r < 128 && g < 128 && b < 128)
                {
                    darkColumns.Add(x);
                }
            }

            Console.WriteLine($"caret-diag: boxY={boxY} darkColumns={darkColumns.Count} first={string.Join(",", darkColumns.Take(8))}");
        }
        finally
        {
            window.Close();
        }
    }

    private static string FormatHex(byte value)
    {
        return value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static unsafe void AssertEsc(IntPtr pointer, char expected)
    {
        Assert.NotEqual(IntPtr.Zero, pointer);
        char* p = (char*)pointer;
        Assert.Equal(expected, p[0]);
        Assert.Equal('\0', p[1]);
    }

    private static partial class Native
    {
        [LibraryImport("libc", EntryPoint = "setenv", StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int SetEnv(string name, string value, int overwrite);
    }
}
