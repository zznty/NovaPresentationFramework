using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Sdl;
using Nova.SdlSource;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;
using SdlEventType = Silk.NET.SDL.EventType;
using SilkWindow = Silk.NET.SDL.WindowHandle;

namespace Nova.Framework.Tests;

/// <summary>
/// Second source file of <see cref="WindowTextBlockTests"/> (same partial class keeps
/// xunit's per-class collection serialized — concurrent SDL window creation races the
/// offscreen driver). Click-path regression for <see cref="SdlHost.Poll"/>'s
/// unmapped-event skip loop: Poll now consumes unmapped SDL events internally and
/// returns false only when the queue is empty. These tests push REAL SDL events
/// (SdlApi.PushEvent) and pump with the smoke loop shape (while TryPump -&gt; Dispatch
/// -&gt; idle). Constructed-SdlEvent tests bypass Poll and cannot see the bug: pre-fix, a
/// queued unmapped event made Poll return false, breaking the drain mid-click and
/// delaying the Up by N passes.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    /// <summary>Unmapped kinds SdlHost.MapEvent does not translate (consumed-and-skipped).</summary>
    private static readonly SdlEventType[] UnmappedKinds =
    [
        SdlEventType.ClipboardUpdate,
        SdlEventType.WindowPixelSizeChanged,
        SdlEventType.WindowDisplayScaleChanged,
        SdlEventType.WindowMouseEnter,
        SdlEventType.WindowMouseLeave
    ];

    [Fact]
    public void Click_SurvivesUnmappedBacklog_UpArrivesInFirstDrainPass()
    {
        Window window = CreateClickWindow(out Button button, out Func<int> clicks);
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source); // drain offscreen startup noise

            window.UpdateLayout();
            System.Windows.Point center = button.TranslatePoint(
                new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
            System.Windows.Point device = source.CompositionTarget.TransformToDevice.Transform(center);
            int cx = (int)Math.Round(device.X);
            int cy = (int)Math.Round(device.Y);
            uint windowId = GetWindowId(source);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, cx, cy);
            Assert.Contains(SdlEventKind.MouseButtonDown, PumpOnePass(source));

            // Backlog: five unmapped SDL events queued ahead of the release.
            for (int i = 0; i < 5; i++)
            {
                PushUnmapped(windowId, i);
            }

            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, cx, cy);

            // ONE drain pass must skip the unmapped backlog and deliver the Up. Pre-fix,
            // Poll returned false on the first unmapped event, the while loop broke, and
            // the Up waited one pass per queued unmapped event.
            Assert.Contains(SdlEventKind.MouseButtonUp, PumpOnePass(source));

            Assert.Equal(1, clicks());
            Assert.False(button.IsPressed);
            Assert.Null(Mouse.Captured);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Click_SecondDownDuringBacklog_ExactlyOneClick()
    {
        Window window = CreateClickWindow(out Button button, out Func<int> clicks);
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);

            window.UpdateLayout();
            System.Windows.Point center = button.TranslatePoint(
                new System.Windows.Point(button.ActualWidth / 2, button.ActualHeight / 2), window);
            System.Windows.Point device = source.CompositionTarget.TransformToDevice.Transform(center);
            int cx = (int)Math.Round(device.X);
            int cy = (int)Math.Round(device.Y);
            uint windowId = GetWindowId(source);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, cx, cy);
            Assert.Contains(SdlEventKind.MouseButtonDown, PumpOnePass(source));

            // Unmapped backlog, then a second Down while the button is still captured.
            for (int i = 0; i < 3; i++)
            {
                PushUnmapped(windowId, i);
            }

            _ = PumpOnePass(source);
            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, cx, cy);
            Assert.Contains(SdlEventKind.MouseButtonDown, PumpOnePass(source));

            // Backlog again, then the release: still exactly one Click.
            for (int i = 3; i < 6; i++)
            {
                PushUnmapped(windowId, i);
            }

            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, cx, cy);
            Assert.Contains(SdlEventKind.MouseButtonUp, PumpOnePass(source));

            Assert.Equal(1, clicks());
            Assert.False(button.IsPressed);
            Assert.Null(Mouse.Captured);
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateClickWindow(out Button button, out Func<int> clicks)
    {
        var text = new TextBlock
        {
            Text = "Hi",
            FontFamily = new FontFamily("DejaVu Sans"),
            FontSize = 24,
            Margin = new Thickness(8)
        };
        button = new Button
        {
            Content = "Click",
            Width = 80,
            Height = 32,
            Margin = new Thickness(8)
        };
        int counter = 0;
        button.Click += (_, _) => counter++;
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        _ = panel.Children.Add(button);
        _ = panel.Children.Add(text);
        clicks = () => counter;
        return new Window
        {
            Title = "click-poll",
            Width = 320,
            Height = 200,
            Content = panel
        };
    }

    /// <summary>
    /// One smoke-shaped drain pass: while TryPump -&gt; Dispatch, then an ApplicationIdle
    /// yield. Present is intentionally omitted — rasterization is orthogonal to input
    /// routing and the rest of this suite does not present either.
    /// </summary>
    private static List<SdlEventKind> PumpOnePass(SdlPresentationSource source)
    {
        var kinds = new List<SdlEventKind>();
        while (source.TryPump(out SdlEvent ev))
        {
            kinds.Add(ev.Kind);
            source.Dispatch(ev);
        }

        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
        return kinds;
    }

    private static unsafe uint GetWindowId(SdlPresentationSource source)
    {
        var silkWindow = new SilkWindow((void*)source.Handle);
        return SdlApi.GetWindowID(silkWindow);
    }

    private static void PushButton(SdlEventType type, uint windowId, bool down, int x, int y)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Button = new Silk.NET.SDL.MouseButtonEvent
            {
                Type = type,
                WindowID = windowId,
                Which = 1,
                Button = (byte)Nova.Sdl.MouseButton.Left,
                Down = down,
                Clicks = 1,
                X = x,
                Y = y
            }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref ev)));
    }

    [Fact]
    public void TextBox_DragSelect_SelectsRangeWithoutAbort()
    {
        // Regression: dragging across TextBox text aborted the process. TextEditorMouse.
        // OnMouseMoveWithFocus calls _dragDropProcess.SourceOnMouseMove, whose body calls
        // TextEditorCopyPaste._CreateDataObject — resolving that call forces loading the
        // DataObject type, which implements IComVisibleDataObject from
        // System.Private.Windows.Ole (System.Private.Windows.Core), an assembly that only
        // exists in the Windows Desktop runtime. Even a plain drag-select gesture (no
        // selection) crashed with FileNotFoundException. On Linux the drag-drop process is
        // skipped entirely (patch 0012): press-drag-release must extend the selection.
        var box = new TextBox { Text = "hello world hello", Width = 200, Height = 24, Background = Brushes.White };
        var window = new Window { Width = 320, Height = 120, Content = box };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);
            Assert.True(box.Focus() || box.IsKeyboardFocusWithin, "TextBox must take focus");

            uint windowId = GetWindowId(source);
            System.Windows.Point start = box.TranslatePoint(new System.Windows.Point(12, box.ActualHeight / 2), window);
            System.Windows.Point end = box.TranslatePoint(new System.Windows.Point(80, box.ActualHeight / 2), window);
            System.Windows.Point deviceStart = source.CompositionTarget.TransformToDevice.Transform(start);
            System.Windows.Point deviceEnd = source.CompositionTarget.TransformToDevice.Transform(end);

            // REAL SDL events through SdlHost.Poll (not constructed SdlEvents): press, drag, release.
            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, (int)Math.Round(deviceStart.X), (int)Math.Round(deviceStart.Y));
            _ = PumpOnePass(source);
            PushMotion(windowId, (int)Math.Round(deviceEnd.X), (int)Math.Round(deviceEnd.Y));
            _ = PumpOnePass(source);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, (int)Math.Round(deviceEnd.X), (int)Math.Round(deviceEnd.Y));
            _ = PumpOnePass(source);

            // The process survived; the drag selected a real range. ~68px at ~7px/char is
            // roughly 8-10 chars; assert a healthy range, not an exact width.
            Assert.True(box.SelectionLength >= 3, $"drag-select produced SelectionLength={box.SelectionLength} (expected >= 3)");
            Assert.True(box.SelectionStart >= 0 && box.SelectionStart + box.SelectionLength <= box.Text.Length,
                $"selection {box.SelectionStart}+{box.SelectionLength} outside text length {box.Text.Length}");
        }
        finally
        {
            window.Close();
        }
    }

    private static void PushMotion(uint windowId, int x, int y)
    {
        var ev = new Silk.NET.SDL.Event
        {
            Motion = new Silk.NET.SDL.MouseMotionEvent
            {
                Type = SdlEventType.MouseMotion,
                WindowID = windowId,
                Which = 1,
                X = x,
                Y = y,
                Xrel = 0,
                Yrel = 0
            }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref ev)));
    }

    private static void PushUnmapped(uint windowId, int index)
    {
        SdlEventType kind = UnmappedKinds[index % UnmappedKinds.Length];
        var ev = new Silk.NET.SDL.Event
        {
            Type = (uint)kind,
            Window = new Silk.NET.SDL.WindowEvent { Type = kind, WindowID = windowId }
        };
        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref ev)));
    }
}
