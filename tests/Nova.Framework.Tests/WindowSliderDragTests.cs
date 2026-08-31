using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nova.Sdl;
using Nova.SdlSource;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;
using SdlEventType = Silk.NET.SDL.EventType;

namespace Nova.Framework.Tests;

/// <summary>
/// Third source file of <see cref="WindowTextBlockTests"/> (same partial class keeps
/// xunit's per-class collection serialized — concurrent SDL window creation races the
/// offscreen driver). Live SDL-path regression for the WPFGallery "A simple slider"
/// flicker: during a thumb drag the bound Value oscillated between 0 and 100 (observed
/// on the binding output too, so it is not a raster defect). The events are pushed as
/// REAL SDL events through SdlHost.Poll — constructed SdlEvent tests bypass Poll and
/// cannot see it.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Slider_DragThroughPoll_ValueTracksThumbWithoutOscillation()
    {
        var slider = new Slider
        {
            Width = 200,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            IsSnapToTickEnabled = true,
            Value = 50,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0),
            // The gallery's Fluent theme opens a value TOOLTIP (a transparent popup)
            // while the thumb drags; the popup is a second SDL window opened mid-drag.
            ToolTip = "value"
        };
        var window = new Window
        {
            Width = 240,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            // The gallery hosts the slider inside a ScrollViewer (page chrome); keep the
            // same visual ancestry so hit-testing and coordinate translation match.
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = slider
            }
        };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source); // drain offscreen startup noise
            window.UpdateLayout();
            _ = slider.ApplyTemplate();

            Track track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
            _ = track.ApplyTemplate();
            Thumb thumb = track.Thumb;
            Assert.NotNull(thumb);

            // Thumb center (value 50 => middle of the track), in device coordinates.
            System.Windows.Point thumbCenter = thumb.TranslatePoint(
                new System.Windows.Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), window);
            System.Windows.Point device = source.CompositionTarget.TransformToDevice.Transform(thumbCenter);
            int cx = (int)Math.Round(device.X);
            int cy = (int)Math.Round(device.Y);
            uint windowId = GetWindowId(source);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, cx, cy);
            Assert.Contains(SdlEventKind.MouseButtonDown, PumpOnePass(source));
            Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.LeftButton);

            // Drag right in 10px device steps — the last two steps cross the window's
            // right edge (240px wide, thumb starts near x=104): SDL mouse capture keeps
            // delivering motions with out-of-bounds coordinates, exactly like a real
            // drag that runs off the window.
            var values = new List<double> { slider.Value };
            for (int i = 1; i <= 16; i++)
            {
                int x = cx + (i * 10);
                PushMotion(windowId, x, cy);
                Assert.Contains(SdlEventKind.MouseMoved, PumpOnePass(source));
                values.Add(slider.Value);
            }

            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, cx + 160, cy);
            _ = PumpOnePass(source);

            // The drag must advance from 50 toward 100 without slamming back: an
            // oscillating 0<->100 pattern shows up as a non-monotonic drop or a
            // spurious 0/100 at a mid-track cursor.
            double previous = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                Assert.True(
                    values[i] >= previous - 1.0,
                    $"value must not drop mid-drag: {string.Join(", ", values)}");
                Assert.True(
                    values[i] > 40.0,
                    $"value must not slam to the minimum mid-drag: {string.Join(", ", values)}");
                previous = values[i];
            }

            Assert.True(slider.Value > 70, $"the drag must reach the right end region, got {slider.Value}");
        }
        finally
        {
            window.Close();
        }
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Drop_FileDropThroughPoll_RaisesDropWithFileData()
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            AllowDrop = true,
            Background = System.Windows.Media.Brushes.Transparent
        };
        string[]? dropped = null;
        panel.Drop += (_, e) =>
        {
            dropped = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            e.Handled = true;
        };
        var window = new Window
        {
            Width = 200,
            Height = 100,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = panel
        };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);
            window.UpdateLayout();
            uint windowId = GetWindowId(source);

            unsafe
            {
                byte[] path = "/tmp/dropped.txt"u8.ToArray();
                fixed (byte* p = path)
                {
                    var dropFile = new Silk.NET.SDL.Event
                    {
                        Drop = new Silk.NET.SDL.DropEvent
                        {
                            Type = Silk.NET.SDL.EventType.DropFile,
                            WindowID = windowId,
                            X = 100,
                            Y = 50,
                            Data = (sbyte*)p
                        }
                    };
                    Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref dropFile)));
                }

                var complete = new Silk.NET.SDL.Event
                {
                    Drop = new Silk.NET.SDL.DropEvent
                    {
                        Type = Silk.NET.SDL.EventType.DropComplete,
                        WindowID = windowId,
                        X = 100,
                        Y = 50,
                        Data = null
                    }
                };
                Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref complete)));
            }

            _ = PumpOnePass(source);

            Assert.NotNull(dropped);
            Assert.Equal(["/tmp/dropped.txt"], dropped);
        }
        finally
        {
            window.Close();
        }
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void TextBox_CopyCutPaste_ThroughSdlClipboard_RoundTripsText()
    {
        var first = new System.Windows.Controls.TextBox
        {
            Text = "hello clipboard",
            Width = 200,
            Height = 24
        };
        var second = new System.Windows.Controls.TextBox
        {
            Width = 200,
            Height = 24
        };
        var window = new Window
        {
            Width = 240,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = new System.Windows.Controls.StackPanel { Children = { first, second } }
        };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(source);
            window.UpdateLayout();

            // Copy: select-all + the ApplicationCommands.Copy on the first box.
            _ = first.Focus();
            first.SelectAll();
            System.Windows.Input.ApplicationCommands.Copy.Execute(null, first);
            Assert.Equal("hello clipboard", System.Windows.Clipboard.GetText());

            // Cut on the first box empties it and keeps the clipboard.
            first.SelectAll();
            System.Windows.Input.ApplicationCommands.Cut.Execute(null, first);
            Assert.Equal(string.Empty, first.Text);
            Assert.Equal("hello clipboard", System.Windows.Clipboard.GetText());

            // Paste into the second box replaces its (empty) selection.
            _ = second.Focus();
            System.Windows.Input.ApplicationCommands.Paste.Execute(null, second);
            Assert.Equal("hello clipboard", second.Text);
        }
        finally
        {
            window.Close();
        }
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Clipboard_Image_RoundTripsThroughSdlMime()
    {
        using SdlHost host = new();
        byte[] bgra = [10, 20, 30, 255, 200, 100, 50, 255, 40, 60, 80, 255, 90, 110, 130, 255];
        var source = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, 8);
        Clipboard.SetImage(source);

        System.Windows.Media.Imaging.BitmapSource? round = Clipboard.GetImage();
        Assert.NotNull(round);
        Assert.Equal(2, round!.PixelWidth);
        Assert.Equal(2, round.PixelHeight);
        byte[] copy = new byte[16];
        round.CopyPixels(copy, 8, 0);
        Assert.Equal(bgra, copy);
    }

    [Fact]
    public void Clipboard_FileDropList_RoundTripsThroughSdlMime()
    {
        using SdlHost host = new();
        var paths = new System.Collections.Specialized.StringCollection
        {
            "/tmp/nova-a.txt",
            "/tmp/nova-dir"
        };
        Clipboard.SetFileDropList(paths);

        System.Collections.Specialized.StringCollection round = Clipboard.GetFileDropList();
        Assert.Equal(2, round.Count);
        Assert.Equal("/tmp/nova-a.txt", round[0]);
        Assert.Equal("/tmp/nova-dir", round[1]);
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Clipboard_ContainsProbes_ReflectSdlMimes()
    {
        using SdlHost host = new();
        byte[] bgra = [10, 20, 30, 255, 200, 100, 50, 255, 40, 60, 80, 255, 90, 110, 130, 255];
        var source = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bgra, 8);

        Assert.False(Clipboard.ContainsImage());
        Assert.False(Clipboard.ContainsFileDropList());

        Clipboard.SetImage(source);
        Assert.True(Clipboard.ContainsImage());
        Assert.False(Clipboard.ContainsFileDropList());

        Clipboard.SetFileDropList(["/tmp/nova-b.txt"]);
        Assert.False(Clipboard.ContainsImage());
        Assert.True(Clipboard.ContainsFileDropList());
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void ManagedDragLoop_DoDragDrop_RaisesOverAndDropOnTarget()
    {
        var target = new System.Windows.Controls.Border
        {
            AllowDrop = true,
            Width = 120,
            Height = 60,
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var source = new TextBlock
        {
            Text = "drag payload",
            Width = 120,
            Height = 24,
            Background = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 80, 0, 0)
        };

        string? droppedText = null;
        DragDropEffects negotiated = DragDropEffects.None;
        int dragOvers = 0;
        target.DragEnter += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        target.DragOver += (_, e) =>
        {
            dragOvers++;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        target.Drop += (_, e) =>
        {
            droppedText = e.Data.GetData(System.Windows.DataFormats.Text) as string;
            negotiated = e.Effects;
            e.Handled = true;
        };

        DragDropEffects result = DragDropEffects.None;
        source.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && result == DragDropEffects.None)
            {
                // The managed intra-app loop (patch 0050): runs a nested
                // dispatcher frame and returns the negotiated effects.
                result = DragDrop.DoDragDrop(source, source.Text, DragDropEffects.Copy);
            }
        };

        var window = new Window
        {
            Width = 300,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = new Grid { Children = { target, source } }
        };
        window.Show();
        try
        {
            var sdlSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(sdlSource);
            window.UpdateLayout();
            uint windowId = GetWindowId(sdlSource);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, 20, 92);
            _ = PumpOnePass(sdlSource);
            Assert.Equal(MouseButtonState.Pressed, Mouse.PrimaryDevice.LeftButton);

            // The down's synchronous move fires the handler with Released; a real
            // motion at a different position still over the source triggers
            // DoDragDrop, which pushes a nested frame; the drag loop consumes the
            // REMAINING events inside that pump, so they must be pre-queued here.
            PushMotion(windowId, 30, 95);
            PushMotion(windowId, 30, 20);
            PushMotion(windowId, 40, 20);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, 40, 20);
            _ = PumpOnePass(sdlSource);

            Assert.Equal("drag payload", droppedText);
            Assert.Equal(DragDropEffects.Copy, negotiated);
            Assert.Equal(DragDropEffects.Copy, result);
            Assert.True(dragOvers >= 1, "DragOver must fire at least once");
            Assert.Equal(MouseButtonState.Released, Mouse.PrimaryDevice.LeftButton);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ManagedDragLoop_QueryContinueDragCancel_AbortsWithoutDrop()
    {
        var target = new System.Windows.Controls.Border
        {
            AllowDrop = true,
            Width = 120,
            Height = 60,
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var source = new TextBlock
        {
            Text = "payload",
            Width = 120,
            Height = 24,
            Background = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 80, 0, 0)
        };

        bool dropped = false;
        target.Drop += (_, e) =>
        {
            dropped = true;
            e.Handled = true;
        };
        bool cancelRequested = false;
        source.QueryContinueDrag += (_, e) =>
        {
            e.Action = DragAction.Cancel;
            cancelRequested = true;
            e.Handled = true;
        };

        DragDropEffects result = DragDropEffects.None;
        source.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && result == DragDropEffects.None)
            {
                result = DragDrop.DoDragDrop(source, source.Text, DragDropEffects.Copy);
            }
        };

        var window = new Window
        {
            Width = 300,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = new Grid { Children = { target, source } }
        };
        window.Show();
        try
        {
            var sdlSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(sdlSource);
            window.UpdateLayout();
            uint windowId = GetWindowId(sdlSource);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, 20, 92);
            _ = PumpOnePass(sdlSource);
            PushMotion(windowId, 30, 95);
            PushMotion(windowId, 30, 20);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, 40, 20);
            _ = PumpOnePass(sdlSource);

            Assert.True(cancelRequested, "QueryContinueDrag must fire");
            Assert.False(dropped, "Drop must not fire after a cancel");
            Assert.Equal(DragDropEffects.None, result);
        }
        finally
        {
            window.Close();
        }
    }
}

public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void ManagedDragLoop_GiveFeedback_FiresPerMoveWithEffects()
    {
        var target = new System.Windows.Controls.Border
        {
            AllowDrop = true,
            Width = 120,
            Height = 60,
            Background = System.Windows.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var source = new TextBlock
        {
            Text = "payload",
            Width = 120,
            Height = 24,
            Background = System.Windows.Media.Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 80, 0, 0)
        };

        int feedbackCount = 0;
        DragDropEffects lastFeedback = DragDropEffects.None;
        source.GiveFeedback += (_, e) =>
        {
            feedbackCount++;
            lastFeedback = e.Effects;
            e.Handled = true;
        };
        target.DragOver += (_, e) =>
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        DragDropEffects result = DragDropEffects.None;
        source.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && result == DragDropEffects.None)
            {
                result = DragDrop.DoDragDrop(source, source.Text, DragDropEffects.Copy);
            }
        };

        var window = new Window
        {
            Width = 300,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Content = new Grid { Children = { target, source } }
        };
        window.Show();
        try
        {
            var sdlSource = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            _ = PumpOnePass(sdlSource);
            window.UpdateLayout();
            uint windowId = GetWindowId(sdlSource);

            PushButton(SdlEventType.MouseButtonDown, windowId, down: true, 20, 92);
            _ = PumpOnePass(sdlSource);
            PushMotion(windowId, 30, 95);
            PushMotion(windowId, 30, 20);
            PushMotion(windowId, 40, 20);
            PushButton(SdlEventType.MouseButtonUp, windowId, down: false, 40, 20);
            _ = PumpOnePass(sdlSource);

            Assert.True(feedbackCount >= 2, $"GiveFeedback must fire per move, got {feedbackCount}");
            Assert.Equal(DragDropEffects.Copy, lastFeedback);
        }
        finally
        {
            window.Close();
        }
    }
}
