using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

/// <summary>
/// Window.AllowsTransparency (per-pixel window alpha) end-to-end: the stock WPF
/// <c>Window</c> path sets <c>HwndSourceParameters.UsesPerPixelOpacity</c> in
/// <c>CreateHwndSourceParameters</c>; the flag must reach
/// <c>SdlPresentationSource.UsesPerPixelOpacity</c> instead of dying at
/// <c>CreateWindowFrame</c> (previously the window was silently opaque).
/// Part of the <c>WindowTextBlockTests</c> partial class like every other window test:
/// these tests create top-level SDL windows, and the class' global-state assertions
/// (bindings/channel-mapping counts) are only valid when window tests run serialized.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Window_AllowsTransparency_ShowsWithoutThrow_AndPropagatesFlagToSource()
    {
        // A Window { AllowsTransparency = true, WindowStyle = WindowStyle.None } must
        // build, show, and not throw or crash, and the source must report per-pixel
        // opacity (the transparency flag reached the SDL window path).
        var rect = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red };
        var window = new Window
        {
            Width = 200,
            Height = 80,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            Content = rect
        };
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Assert.True(source.UsesPerPixelOpacity, "AllowsTransparency window must set UsesPerPixelOpacity on the SDL source");
            Assert.True(source.PixelWidth > 0 && source.PixelHeight > 0);
            // The content laid out and is attached to the visual tree.
            Assert.Equal(40, rect.ActualWidth, 1);
            Assert.Equal(20, rect.ActualHeight, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Window_Opaque_UsesPerPixelOpacityStaysFalse()
    {
        // Ordinary windows must remain opaque: no transparency flag on the source.
        var window = new Window { Width = 200, Height = 80, Content = new Rectangle { Width = 40, Height = 20, Fill = Brushes.Red } };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            var source = Assert.IsType<SdlPresentationSource>(PresentationSource.FromVisual(window));
            Assert.False(source.UsesPerPixelOpacity, "ordinary window must not request per-pixel opacity");
        }
        finally
        {
            window.Close();
        }
    }
}
