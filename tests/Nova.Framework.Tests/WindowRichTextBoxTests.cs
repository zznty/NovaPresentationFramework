using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Nova.Framework.Tests;

/// <summary>
/// RichTextBox regression tests (same partial class keeps xunit's per-class collection
/// serialized — concurrent SDL window creation races the offscreen driver).
///
/// Stage 0 (shutdown guard): <c>RichTextBox</c> creates its <c>PtsContext</c> lazily at
/// layout, and PTS used to be native-only (<c>PresentationNative_cor3.dll</c>), so the first
/// FlowDocument layout threw <see cref="DllNotFoundException"/> from
/// <c>PTS.CreateInstalledObjectsInfo</c>. That failure used to abort the PROCESS at exit
/// (exit 134): the failed acquisition left a half-initialized entry in the <c>PtsCache</c>
/// context pool (null <c>Owner</c>, zero <c>PtsHost.Context</c>), and
/// <c>PtsCache.DestroyPTSContexts</c> dereferenced the null <c>Owner</c> when the Dispatcher
/// shut down. The shutdown paths tolerate the uninitialized entry (and the failed acquisition
/// removes it from the pool), so a RichTextBox that hit the layout DllNotFound shut down
/// cleanly.
///
/// Stage 1 (patch 0014): PTS is rewired to the managed <c>Nova.Pts</c> engine on Linux, so the
/// same layout now SUCCEEDS and renders text. The test below pins that end-to-end contract:
/// layout must complete without an exception and the PtsCache shutdown path (the exact code
/// that used to NRE) must run without aborting.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void RichTextBox_LayoutAndShutdown_DoesNotAbort()
    {
        var richTextBox = new RichTextBox
        {
            Width = 160,
            Height = 80,
            Document = new FlowDocument(new Paragraph(new Run("rich text")))
        };
        var window = new Window { Width = 320, Height = 240, Content = richTextBox };

        try
        {
            // With patch 0014 the managed PTS path formats the FlowDocument; a DllNotFound
            // here would mean the Linux PTS rewire regressed (native PTS is unavailable).
            window.Show();
            window.UpdateLayout();
            Assert.True(richTextBox.ActualWidth > 0, $"layout width={richTextBox.ActualWidth}");
            Assert.True(richTextBox.IsMeasureValid, "RichTextBox did not measure.");
        }
        finally
        {
            window.Close();
        }

        // Exercise the exact Dispatcher-shutdown path (PtsCacheShutDownListener ->
        // PtsCache.Shutdown -> OnPtsContextReleased + DestroyPTSContexts) WITHOUT tearing
        // down the test thread's Dispatcher, which sibling tests share. Any regression
        // (null-Owner dereference / zero-handle FailFast) throws here and fails the test.
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        object ptsCache = typeof(Dispatcher)
            .GetProperty("PtsCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dispatcher)!;
        _ = ptsCache.GetType()
            .GetMethod("Shutdown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ptsCache, null);
    }
}
