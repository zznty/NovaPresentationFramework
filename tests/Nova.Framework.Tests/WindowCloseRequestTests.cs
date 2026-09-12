using System.Windows;
using System.Windows.Controls;
using Nova.Sdl;
using Nova.SdlSource;
using Silk.NET.Core;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.Framework.Tests;

/// <summary>
/// Fifth source file of <see cref="WindowTextBlockTests"/> (same partial class keeps xunit's
/// per-class collection serialized — concurrent SDL window creation races the offscreen driver).
///
/// Regression test for the window-manager close button: SDL delivers
/// <c>SDL_EVENT_WINDOW_CLOSE_REQUESTED</c> for the window, and <c>CompositionFrame.ProcessEvent</c>
/// consumed it as "this frame is closing" and returned false WITHOUT handing the event to the
/// source — so <c>SdlPresentationSource.Dispatch</c> never routed WM_CLOSE into the WPF window and
/// no <c>Closing</c>/<c>Close</c> ever ran. The pump then idled on the closing frame, which is why
/// clicking X did nothing at all (the compositor reports the window as not responding).
///
/// RED before the fix (offscreen driver): <c>Closing</c> never fires and the source is never
/// disposed. GREEN after: the request runs the window's close path and drains the Duce bindings.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void WindowCloseRequest_FromWindowManager_RunsTheWindowClosePath()
    {
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = new TextBlock { Text = "close me" },
        };
        window.Show();
        try
        {
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));

            var closingRan = false;
            window.Closing += (_, _) => closingRan = true;

            PushCloseRequest(source);
            while (source.TryPump(out SdlEvent ev))
            {
                source.Dispatch(ev);
            }

            Assert.True(closingRan);
            Assert.True(source.IsDisposed);
            Assert.Equal(0, CountBindings());
            Assert.Equal(0, CountChannelMappings());
        }
        finally
        {
            // No-op when the close request already closed the window.
            window.Close();
        }
    }

    private static unsafe void PushCloseRequest(SdlPresentationSource source)
    {
        var sdlWindow = new Silk.NET.SDL.WindowHandle((void*)source.Handle);
        var close = new Silk.NET.SDL.Event
        {
            Window = new Silk.NET.SDL.WindowEvent
            {
                Type = Silk.NET.SDL.EventType.WindowCloseRequested,
                Timestamp = 0,
                WindowID = SdlApi.GetWindowID(sdlWindow),
            },
        };

        Assert.True(SdlApi.PushEvent(new Ref<Silk.NET.SDL.Event>(ref close)));
    }
}
