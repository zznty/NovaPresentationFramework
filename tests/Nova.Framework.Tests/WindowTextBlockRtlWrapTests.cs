using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nova.Framework.Tests;

/// <summary>
/// Real-stack evidence for the Nova.LineServices Lo* engine (patch 0007 nest). RTL paragraphs
/// force the FullTextLine path (SimpleTextLine.Create rejects RightToLeft), so these tests drive
/// LoCreateLine through the patched nest: bidi reverse markers must be skipped (no
/// "Zero-length text line!" Debug.Assert) and wrapping must produce multiple measured lines.
/// Same xunit collection as every other window test (serialized SDL windows).
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void TextBlock_RtlText_LaysOut_NoZeroLengthAbort()
    {
        var window = new Window { Width = 200, Height = 80 };
        var text = new TextBlock
        {
            Text = "שלום",
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Noto Sans Hebrew"),
            FontSize = 16
        };
        window.Content = text;
        window.Show();
        try
        {
            Assert.True(window.IsVisible);
            Assert.True(text.IsMeasureValid, $"measureValid desired={text.DesiredSize}");
            Assert.True(text.ActualWidth > 0, $"actual={text.ActualWidth}x{text.ActualHeight} desired={text.DesiredSize}");
            Assert.True(text.ActualHeight > 0, $"actual={text.ActualWidth}x{text.ActualHeight} desired={text.DesiredSize}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TextBlock_RtlWrappedText_ProducesMultipleLines()
    {
        // "שלום עולם" (Hebrew "hello world") at 16px in a 30px-wide RTL paragraph must wrap to
        // more than one line; the same text in a wide paragraph stays on one line. Wrapping
        // happens in LoCreateLine (BreakCJK/space break), so the wrapped height must exceed the
        // single-line height.
        var narrow = new TextBlock
        {
            Text = "שלום עולם",
            FlowDirection = FlowDirection.RightToLeft,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Noto Sans Hebrew"),
            FontSize = 16,
            Width = 30
        };
        var wide = new TextBlock
        {
            Text = "שלום עולם",
            FlowDirection = FlowDirection.RightToLeft,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Noto Sans Hebrew"),
            FontSize = 16,
            Width = 300
        };

        var window = new Window { Width = 360, Height = 120 };
        var panel = new StackPanel();
        _ = panel.Children.Add(narrow);
        _ = panel.Children.Add(wide);
        window.Content = panel;
        window.Show();
        try
        {
            Assert.True(narrow.IsMeasureValid, $"narrow desired={narrow.DesiredSize}");
            Assert.True(wide.IsMeasureValid, $"wide desired={wide.DesiredSize}");

            Assert.True(wide.DesiredSize.Height > 0, $"wide height={wide.DesiredSize.Height}");
            Assert.True(
                narrow.DesiredSize.Height > wide.DesiredSize.Height + 1,
                $"wrapped height {narrow.DesiredSize.Height} must exceed single-line height {wide.DesiredSize.Height}");
        }
        finally
        {
            window.Close();
        }
    }
}
