using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nova.Framework.Tests;

/// <summary>
/// Fourth source file of <see cref="WindowTextBlockTests"/> (same partial class keeps xunit's
/// per-class collection serialized — concurrent SDL window creation races the offscreen driver).
///
/// Regression test for the MODAL close path. <c>Window.Close()</c> on a window shown with
/// <c>ShowDialog()</c> runs <c>CloseWindowFromWmClose</c> → <c>DoDialogHide()</c>, whose Win32
/// thread-window bookkeeping (<c>Debug.Assert</c> + <c>EnableThreadWindows(true)</c>) dereferenced
/// the list that Linux never creates (ShowDialog's enumeration is Windows-only). The assert threw
/// (a NullReferenceException in Release builds) and aborted
/// <c>CloseWindowFromWmClose</c> BEFORE its Linux branch disposed the SDL presentation source, so
/// the window stayed mapped and unpumped: the compositor showed it as "not responding" and the
/// DuceRuntime binding/channel mappings were never drained.
///
/// The non-modal path (<c>Show()</c> + <c>Close()</c>) never enters <c>DoDialogHide</c> and is
/// covered by <c>Window_ShowThenClose_DrainsDuceBindingsAndChannelMappings</c>.
///
/// RED before the fix (offscreen driver): DebugAssertException from <c>Window.DoDialogHide()</c>
/// and the binding/channel-mapping counts still non-zero. GREEN after (both counts 0).
///
/// Note: <c>Window.Closed</c> is not raised for a shown window on Linux — <c>WmDestroy</c>, which
/// raises it on Windows, has no Linux message to run from. Separate gap, not asserted here.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void ModalDialog_Close_CompletesAndDrainsDuceBindings()
    {
        // Owner-less on purpose: the ShowDialog owner path P/Invokes into user32.
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = new TextBlock { Text = "modal" },
        };

        Exception? closeFailure = null;

        // ShowDialog blocks this thread, so queue the close before entering the modal loop.
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            closeFailure = Record.Exception(window.Close);
        });

        bool? dialogResult = window.ShowDialog();

        Assert.Null(closeFailure);
        // DoDialogHide coerces a null DialogResult to false before ShowDialog returns.
        Assert.False(dialogResult);

        // The Linux tail of CloseWindowFromWmClose (source.Dispose → RemoveSource) ran, exactly as
        // in the non-modal path: nothing is left bound to the destroyed window.
        Assert.Equal(0, CountBindings());
        Assert.Equal(0, CountChannelMappings());
    }
}
