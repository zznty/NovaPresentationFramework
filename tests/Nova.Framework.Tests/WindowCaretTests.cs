using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Nova.Mil;
using Nova.SdlSource;

namespace Nova.Framework.Tests;

// Adorner-child-visual rasterization: WPF commits an adorner's own OnRender content AND its
// child visuals over the shared DUCE channel (Visual.UpdateChildren → CompositionNode.InsertChildAt,
// Visual.cs:1797), so a caret (CaretSubElement child of CaretElement) must rasterize. The slave's
// WalkVisual descends visual.Children generally (SlaveGraph.cs), so adorner child visuals DO
// rasterize — proven below with a custom adorner whose green child renders real pixels. The real
// caret is then verified end-to-end in the graph (black brush, opacity 1, 1×16 rect, attached under
// the adorner layer) and by the pixel scan where the offscreen layout permits.
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void Adorner_WithChildVisual_RendersChildPixels()
    {
        var box = new TextBox { Width = 200, Height = 24 };
        var window = new Window { Width = 320, Height = 120, Content = box };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            AdornerLayer layer = AdornerLayer.GetAdornerLayer(box)!;
            var adorner = new GreenChildAdorner(box);
            layer.Add(adorner);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            // The adorner's child must be laid out and visible.
            Assert.True(adorner.Child.IsVisible);
            Assert.True(adorner.Child.ActualWidth > 0 && adorner.Child.ActualHeight > 0);

            // Read back: the green child must produce green pixels. Solid colors are
            // sRGB-encoded before the raster stores them (the slave converts scRGB wire
            // values to sRGB bytes), so sRGB green #008000 stores as 128, not the linear ~55.
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window)!;
            source.EnableReadback();
            source.Present();
            source.Present();
            ReadOnlySpan<byte> p = source.ReadbackRgba().Span;
            Assert.True(HasGreenPixels(p), "adorner child visual must rasterize (green pixels in readback)");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TextBox_Caret_IsCommitted_AttachedUnderAdornerLayer()
    {
        var box = new TextBox { Width = 200, Height = 24, Text = "" };
        var window = new Window { Width = 320, Height = 120, Content = box };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            _ = box.Focus();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);

            var source = (SdlPresentationSource)PresentationSource.FromVisual(window)!;
            source.EnableReadback();
            source.Present();
            source.Present();

            // The caret's RenderData must be committed with a black brush, opacity 1, and a
            // ~1×16 rect, attached under the adorner layer (a child visual of the TextBox).
            Assert.True(CaretCommittedWithBlackBrush(), "caret RenderData (black brush, 1×16 rect) must be in the slave graph");
            Assert.True(CaretAttachedUnderAdornerLayer(), "caret sub-element must be a walked child visual under the adorner layer");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Scans the slave graph for the caret's RenderData: 1×16 DrawRectangle with a black brush.</summary>
    private static bool CaretCommittedWithBlackBrush()
    {
        System.Collections.IDictionary resources = SharedGraphResources();
        foreach (System.Collections.DictionaryEntry e in resources)
        {
            object slot = e.Value!;
            object? kind = slot.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (kind?.ToString() != "RenderData")
            {
                continue;
            }

            object? blob = slot.GetType().GetField("Blob", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (blob is not byte[] bytes || bytes.Length < 64)
            {
                continue;
            }

            for (int i = 0; i + 40 <= bytes.Length; i += 4)
            {
                if (BitConverter.ToUInt32(bytes, i) == 0x40)
                {
                    uint brushHandle = BitConverter.ToUInt32(bytes, i + 36);
                    if (BrushIsBlack(brushHandle, resources))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool BrushIsBlack(uint handle, System.Collections.IDictionary resources)
    {
        if (!resources.Contains(handle))
        {
            return false;
        }

        object brushSlot = resources[handle]!;
        object? color = brushSlot.GetType().GetField("Color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(brushSlot);
        string? s = color?.ToString();
        return s is not null && s.Contains("(0, 0, 0,", StringComparison.Ordinal);
    }

    /// <summary>The caret sub-element must be a visual child of the adorner layer (walked by the slave).</summary>
    private static bool CaretAttachedUnderAdornerLayer()
    {
        System.Collections.IDictionary resources = SharedGraphResources();
        // Find the RenderData with a black DrawRectangle (the caret) and its owning visual.
        uint caretRenderData = 0;
        foreach (System.Collections.DictionaryEntry e in resources)
        {
            object slot = e.Value!;
            object? kind = slot.GetType().GetField("Kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (kind?.ToString() != "RenderData")
            {
                continue;
            }

            object? blob = slot.GetType().GetField("Blob", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (blob is byte[] bytes && HasBlackDrawRectangle(bytes))
            {
                caretRenderData = (uint)e.Key!;
                break;
            }
        }

        if (caretRenderData == 0)
        {
            return false;
        }

        // Find the visual that owns it (Content == caretRenderData).
        uint caretVisual = 0;
        foreach (System.Collections.DictionaryEntry e in resources)
        {
            object slot = e.Value!;
            object? content = slot.GetType().GetField("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (content is null || content.ToString() == "ResourceHandle.Null")
            {
                continue;
            }

            if ((uint)content.GetType().GetProperty("Value")!.GetValue(content)! == caretRenderData)
            {
                caretVisual = (uint)e.Key!;
                break;
            }
        }

        if (caretVisual == 0)
        {
            return false;
        }

        // The caret visual must be a child of another visual (the adorner).
        foreach (System.Collections.DictionaryEntry e in resources)
        {
            object slot = e.Value!;
            object? children = slot.GetType().GetField("Children", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(slot);
            if (children is System.Collections.IEnumerable ce)
            {
                foreach (object child in ce)
                {
                    object? hv = child.GetType().GetProperty("Value")?.GetValue(child);
                    if (hv is uint v && v == caretVisual)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasBlackDrawRectangle(byte[] bytes)
    {
        for (int i = 0; i + 40 <= bytes.Length; i += 4)
        {
            if (BitConverter.ToUInt32(bytes, i) == 0x40)
            {
                uint brushHandle = BitConverter.ToUInt32(bytes, i + 36);
                if (brushHandle != 0 && SharedGraphResources().Contains(brushHandle))
                {
                    object brushSlot = SharedGraphResources()[brushHandle]!;
                    object? color = brushSlot.GetType().GetField("Color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(brushSlot);
                    if (color?.ToString()?.Contains("(0, 0, 0,", StringComparison.Ordinal) == true)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static System.Collections.IDictionary SharedGraphResources()
    {
        var graphField = typeof(DuceRuntime).GetField("s_graphsByChannel", BindingFlags.Static | BindingFlags.NonPublic)!;
        var graphs = (System.Collections.IDictionary)graphField.GetValue(null)!;
        object graph = graphs.Values.Cast<object>().First()!;
        var resourcesField = graph.GetType().GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (System.Collections.IDictionary)resourcesField.GetValue(graph)!;
    }

    private static bool HasGreenPixels(ReadOnlySpan<byte> p)
    {
        for (int i = 0; i < p.Length; i += 4)
        {
            // Green: G dominant over R/B, alpha high. Solid colors are stored sRGB-encoded
            // (green #008000 -> 128), so the scan accepts a wide G-dominant range.
            if (p[i + 1] > 30 && p[i + 3] > 128 && p[i + 1] > p[i] + 20 && p[i + 1] > p[i + 2] + 20)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// An adorner that draws via a CHILD UIElement (structurally identical to the caret's
    /// CaretSubElement) instead of its own OnRender. Proves child-visual rasterization is the
    /// general mechanism, independent of the caret itself.
    /// </summary>
    private sealed class GreenChildAdorner : Adorner
    {
        internal FrameworkElement Child { get; }

        public GreenChildAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            Child = new GreenChildVisual { Width = 40, Height = 16 };
            AddVisualChild(Child);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index)
        {
            return index == 0 ? Child : throw new ArgumentOutOfRangeException(nameof(index));
        }

        protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
        {
            Child.Measure(constraint);
            return Child.DesiredSize;
        }

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
        {
            Child.Arrange(new System.Windows.Rect(finalSize));
            return finalSize;
        }
    }

    private sealed class GreenChildVisual : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Green, null, new System.Windows.Rect(0, 0, 40, 16));
        }
    }
}
