using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Nova.Geometry;
using Nova.Mil;
using Nova.MilCmd;
using Nova.SdlSource;
using Nova.Vulkan;

namespace Nova.Framework.Tests;

/// <summary>
/// Control-coverage probe driver. Third file of the <see cref="WindowTextBlockTests"/> partial
/// class so it shares the per-class xunit collection.
///
/// WHY the probe lives as a console harness: a measured multi-window bug makes every WPF window
/// after the FIRST in a process render nothing (the second window's graph gets a stale
/// <c>Root</c> handle that is not in its resource table, so <c>SlaveGraph.Rasterize</c> walks
/// nothing and window-presenter readback returns the clear color — see
/// <see cref="Debug_MultiWindow_Classify"/>). A single test process can therefore only report
/// trustworthy pixels for its first window; trustworthy per-control renders require per-control
/// process isolation, which this test drives by re-running the harness binary per control.
///
/// Both facts here are opt-in (set <c>NOVA_CONTROL_PROBE=1</c>) and slow (one process per
/// control, ~3 s each), so the default CI run stays fast and deterministic. Run manually:
/// <c>NOVA_CONTROL_PROBE=1 dotnet test tests/Nova.Framework.Tests --filter ControlCoverage</c>
/// or directly: <c>dotnet run --project samples/ControlProbeHarness -- all</c>.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    [Fact]
    public void ControlCoverage_Probe_ReportsPerControl()
    {
        string repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        if (Environment.GetEnvironmentVariable("NOVA_CONTROL_PROBE") != "1")
        {
            Console.WriteLine($"Control-coverage probe is opt-in: set NOVA_CONTROL_PROBE=1 (runs samples/ControlProbeHarness per control, one process each; repo {repoRoot}).");
            return;
        }

        string project = System.IO.Path.Combine(repoRoot, "samples", "ControlProbeHarness", "ControlProbeHarness.csproj");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("all");
        using var process = Process.Start(startInfo)!;
        string table = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Console.WriteLine(table);
        if (error.Length > 0)
        {
            Console.WriteLine("probe stderr tail: " + error[^Math.Min(error.Length, 500)..]);
        }

        Assert.True(process.ExitCode == 0, $"harness exited {process.ExitCode}");
        Assert.Contains("control        |", table, StringComparison.Ordinal);
    }

    /// <summary>
    /// Evidence for the multi-window rendering bug. Run with <c>NOVA_CONTROL_PROBE=1</c>.
    /// Measured result (2026-08-18): the FIRST window per process renders (colors=2, fills=2),
    /// every later window reads back the clear color even though its graph holds the full WPF
    /// content (resources, root, colored brushes, render-data blobs). The difference: the later
    /// graphs' <c>Root</c> handle is not in the graph's own resource table (seq#1 Root=2, tree
    /// starts at v15), so <c>WalkVisual(Root)</c> bails at <c>_resources.TryGetValue</c> and
    /// Rasterize emits nothing. <c>DuceRuntime</c> bindings/channel mappings also leak on
    /// <c>Window.Close()</c> (never detached), which turns the single-binding fallback of
    /// <c>GraphFor</c> into a null (dropped commits) for unregistered channels.
    /// </summary>
    [Fact]
    public void Debug_MultiWindow_Classify()
    {
        if (Environment.GetEnvironmentVariable("NOVA_CONTROL_PROBE") != "1")
        {
            return;
        }

        ClassifyOne("seq#0");
        ClassifyOne("seq#1");
        ClassifyTwo();
    }

    private static void ClassifyOne(string tag)
    {
        var window = new Window
        {
            Width = 320,
            Height = 240,
            Content = new Rectangle { Width = 80, Height = 40, Fill = Brushes.Red }
        };
        window.Show();
        window.UpdateLayout();
        var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
        IVulkanPresenter presenter = GetWindowPresenter(source);
        presenter.EnableReadback();
        FlushDispatcher();
        source.Present();
        source.Present();
        Report(tag, source, presenter);
        window.Close();
        ReportRuntimeState(tag + "-after-close");
    }

    private static void ClassifyTwo()
    {
        var windowA = new Window
        {
            Width = 320,
            Height = 240,
            Content = new Rectangle { Width = 80, Height = 40, Fill = Brushes.Red }
        };
        windowA.Show();
        windowA.UpdateLayout();
        var sourceA = (SdlPresentationSource)PresentationSource.FromVisual(windowA);
        IVulkanPresenter presenterA = GetWindowPresenter(sourceA);
        presenterA.EnableReadback();

        var windowB = new Window
        {
            Width = 320,
            Height = 240,
            Content = new Rectangle { Width = 80, Height = 40, Fill = Brushes.Blue }
        };
        windowB.Show();
        windowB.UpdateLayout();
        var sourceB = (SdlPresentationSource)PresentationSource.FromVisual(windowB);
        IVulkanPresenter presenterB = GetWindowPresenter(sourceB);
        presenterB.EnableReadback();

        FlushDispatcher();
        sourceA.Present();
        sourceB.Present();
        sourceA.Present();
        sourceB.Present();

        Report("sim#0", sourceA, presenterA);
        Report("sim#1", sourceB, presenterB);

        // Decisive: inject a KNOWN rect into window B's WPF-populated graph and re-present.
        // Red readback => the presenter/raster path is fine; the WPF content itself is what the
        // stale-Root walk skips.
        object frameB = typeof(SdlPresentationSource).GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sourceB)!;
        InjectRedRectInto((Nova.Host.CompositionFrame)frameB);
        FlushDispatcher();
        sourceB.Present();
        sourceB.Present();
        ReadOnlyMemory<byte> injected = presenterB.ReadbackRgba();
        Console.WriteLine($"sim#1-injected: colors={CountDistinctColors(injected)} px0={FormatPixel(injected.Span)}");

        windowA.Close();
        windowB.Close();
    }

    private static void Report(string tag, SdlPresentationSource source, IVulkanPresenter presenter)
    {
        ReadOnlyMemory<byte> pixels = presenter.ReadbackRgba();
        object frame = typeof(SdlPresentationSource).GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
        object graph = frame.GetType().GetProperty("Graph")!.GetValue(frame)!;
        bool rootIsNull = (bool)graph.GetType().GetProperty("Root")!.GetValue(graph)!.GetType().GetProperty("IsNull")!.GetValue(graph.GetType().GetProperty("Root")!.GetValue(graph)!)!;
        var recorder = new RecordingList();
        ((SlaveGraph)graph).Rasterize(recorder, null);
        Console.WriteLine(
            $"{tag}: colors={CountDistinctColors(pixels)} px0={FormatPixel(pixels.Span)} graphRootIsNull={rootIsNull} graphResources={CountGraphResources(graph)} rasterizeFills={recorder.Fills.Count} {DumpVisualTree(graph)} | {DumpBrushes(graph)} | {DumpRootChain(graph)}");
        ReportRuntimeState(tag);
    }

    private static void ReportRuntimeState(string tag)
    {
        Type runtime = typeof(DuceRuntime);
        object bindings = runtime.GetField("s_bindings", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        int bindingCount = (int)bindings.GetType().GetProperty("Count")!.GetValue(bindings)!;
        object channelMap = runtime.GetField("s_graphsByChannel", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        int channelCount = (int)channelMap.GetType().GetProperty("Count")!.GetValue(channelMap)!;
        Console.WriteLine($"  {tag}: DuceRuntime bindings={bindingCount} channelMappings={channelCount}");
    }

    private static int CountGraphResources(object graph)
    {
        object resources = graph.GetType().GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
        return (int)resources.GetType().GetProperty("Count")!.GetValue(resources)!;
    }

    private static string DumpVisualTree(object graph)
    {
        object resources = graph.GetType().GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
        var dictionary = (System.Collections.IDictionary)resources;
        Type slotType = dictionary.Values.Cast<object>().First().GetType();
        System.Reflection.FieldInfo kindField = slotType.GetField("Kind")!;
        System.Reflection.FieldInfo contentField = slotType.GetField("Content")!;
        System.Reflection.FieldInfo childrenField = slotType.GetField("Children")!;
        var parts = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            object slot = entry.Value!;
            if (kindField.GetValue(slot)!.ToString() != "Visual")
            {
                continue;
            }

            uint content = (uint)contentField.GetValue(slot)!.GetType().GetProperty("Value")!.GetValue(contentField.GetValue(slot))!;
            var children = (System.Collections.IList)childrenField.GetValue(slot)!;
            var childHandles = new List<string>();
            foreach (object child in children)
            {
                childHandles.Add(((uint)child.GetType().GetProperty("Value")!.GetValue(child)!).ToString(CultureInfo.InvariantCulture));
            }

            parts.Add($"v{entry.Key}:content={content} children=[{string.Join(",", childHandles)}]");
        }

        return parts.Count == 0 ? "noVisuals" : string.Join(" ", parts);
    }

    private static string DumpBrushes(object graph)
    {
        object resources = graph.GetType().GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
        var dictionary = (System.Collections.IDictionary)resources;
        var parts = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            object slot = entry.Value!;
            if (slot.GetType().GetField("Kind")!.GetValue(slot)!.ToString() != "SolidColorBrush")
            {
                continue;
            }

            ColorRgba color = (ColorRgba)slot.GetType().GetField("Color")!.GetValue(slot)!;
            parts.Add($"b{entry.Key}=#{((byte)(color.R * 255)).ToString("X2", CultureInfo.InvariantCulture)}{((byte)(color.G * 255)).ToString("X2", CultureInfo.InvariantCulture)}{((byte)(color.B * 255)).ToString("X2", CultureInfo.InvariantCulture)}{((byte)(color.A * 255)).ToString("X2", CultureInfo.InvariantCulture)}");
        }

        return parts.Count == 0 ? "noBrushes" : string.Join(" ", parts);
    }

    /// <summary>Scans every RenderData blob's draw commands and checks each brush/pen reference against the graph's own table.</summary>
    private static string DumpRootChain(object graph)
    {
        object resources = graph.GetType().GetField("_resources", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graph)!;
        var dictionary = (System.Collections.IDictionary)resources;
        Type slotType = dictionary.Values.Cast<object>().First().GetType();
        System.Reflection.FieldInfo kindField = slotType.GetField("Kind")!;
        System.Reflection.FieldInfo blobField = slotType.GetField("Blob")!;
        var hits = new List<string>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            object slot = entry.Value!;
            if (kindField.GetValue(slot)!.ToString() != "RenderData")
            {
                continue;
            }

            byte[] blob = (byte[])blobField.GetValue(slot)!;
            var capture = new CaptureVisitor();
            MilCommandParser.ParseRenderData(blob, [], capture);
            foreach ((Nova.Geometry.Rect rect, ResourceHandle brush, _) in capture.DrawRectangles)
            {
                bool brushIn = dictionary.Contains(brush.Value);
                string brushKind = brushIn ? kindField.GetValue(dictionary[brush.Value]!)!.ToString()! : "ABSENT";
                hits.Add($"rd={entry.Key} rect({rect.X:F0},{rect.Y:F0},{rect.Width:F0}x{rect.Height:F0}) brush={brush.Value}({brushKind})");
            }
        }

        return hits.Count == 0 ? "noDrawsInBlobs" : string.Join("; ", hits);
    }

    /// <summary>Captures draw commands from a RenderData blob without GPU work.</summary>
    private sealed class CaptureVisitor : MilCommandVisitor
    {
        public List<(Nova.Geometry.Rect Rect, ResourceHandle Brush, ResourceHandle Pen)> DrawRectangles { get; } = [];

        public override void VisitDrawRectangle(Nova.Geometry.Rect rectangle, ResourceHandle brush, ResourceHandle pen)
        {
            DrawRectangles.Add((rectangle, brush, pen));
        }
    }

    /// <summary>Records what Rasterize emits without touching a GPU.</summary>
    private sealed class RecordingList : IRasterCommandList
    {
        public List<Nova.Geometry.Rect> Fills { get; } = [];

        public void Clear(ColorRgba color)
        {
        }

        public void FillRectangle(Nova.Geometry.Rect rectangle, ColorRgba color)
        {
            Fills.Add(rectangle);
        }

        public void FillQuad(Nova.Geometry.Point p0, Nova.Geometry.Point p1, Nova.Geometry.Point p2, Nova.Geometry.Point p3, ColorRgba color)
        {
        }

        public void FillTriangles(ReadOnlySpan<Nova.Geometry.Point> vertices, ColorRgba color)
        {
        }

        public void FillGradientTriangles(ReadOnlySpan<Nova.Geometry.Point> vertices, ReadOnlySpan<Nova.Geometry.Point> gradientCoords, TextureHandle lut, GradientKind kind, Nova.Geometry.GradientSpreadMethod spread, ColorRgba tint)
        {
        }

        public void DrawTexturedQuad(Nova.Geometry.Point p0, Nova.Geometry.Point p1, Nova.Geometry.Point p2, Nova.Geometry.Point p3, TextureHandle texture, Nova.Geometry.Point uv0, Nova.Geometry.Point uv1, Nova.Geometry.Point uv2, Nova.Geometry.Point uv3, ColorRgba tint)
        {
        }

        public void DrawTexturedTriangles(ReadOnlySpan<Nova.Geometry.Point> vertices, ReadOnlySpan<Nova.Geometry.Point> uvs, TextureHandle texture, ColorRgba tint)
        {
        }

        public void PushClip(Nova.Geometry.Rect rectangle)
        {
        }

        public void PopClip()
        {
        }

        public void PushOpacity(double opacity)
        {
        }

        public void PopOpacity()
        {
        }

        public void PushTransform(Matrix3x2 transform)
        {
        }

        public void PopTransform()
        {
        }
    }

    private static IVulkanPresenter GetWindowPresenter(SdlPresentationSource source)
    {
        object frame = typeof(SdlPresentationSource)
            .GetProperty("Frame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(source)!;
        return (IVulkanPresenter)frame.GetType().GetProperty("Presenter")!.GetValue(frame)!;
    }

    private static void FlushDispatcher()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static int CountDistinctColors(ReadOnlyMemory<byte> pixels)
    {
        var seen = new HashSet<int>();
        ReadOnlySpan<byte> span = pixels.Span;
        for (int i = 0; i + 3 < span.Length; i += 4)
        {
            int color = span[i] | (span[i + 1] << 8) | (span[i + 2] << 16) | (span[i + 3] << 24);
            _ = seen.Add(color);
            if (seen.Count > 16)
            {
                return seen.Count;
            }
        }

        return seen.Count;
    }

    private static string FormatPixel(ReadOnlySpan<byte> pixels)
    {
        return pixels.Length < 4
            ? "empty"
            : "#" + pixels[0].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[1].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[2].ToString("X2", CultureInfo.InvariantCulture)
                + pixels[3].ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void InjectRedRectInto(Nova.Host.CompositionFrame frame)
    {
        const uint visual = 1;
        const uint brush = 2;
        const uint renderData = 3;
        var writer = new List<byte>();
        void U32(uint value)
        {
            writer.AddRange(BitConverter.GetBytes(value));
        }

        void F32(float value)
        {
            writer.AddRange(BitConverter.GetBytes(value));
        }

        void F64(double value)
        {
            writer.AddRange(BitConverter.GetBytes(value));
        }

        U32((uint)MilCommandKind.ChannelCreateResource);
        U32(visual);
        U32((uint)MilResourceType.Visual);
        U32((uint)MilCommandKind.ChannelCreateResource);
        U32(brush);
        U32((uint)MilResourceType.SolidColorBrush);
        U32((uint)MilCommandKind.ChannelCreateResource);
        U32(renderData);
        U32((uint)MilResourceType.RenderData);
        U32((uint)MilCommandKind.SolidColorBrush);
        U32(brush);
        F64(1.0);
        F32(1);
        F32(0);
        F32(0);
        F32(1);
        U32(0);
        U32(0);
        U32(0);
        U32(0);

        var blob = new List<byte>();
        void B32(uint value)
        {
            blob.AddRange(BitConverter.GetBytes(value));
        }

        void B64(double value)
        {
            blob.AddRange(BitConverter.GetBytes(value));
        }

        B32(48);
        B32((uint)MilCommandKind.DrawRectangle);
        B64(8);
        B64(8);
        B64(16);
        B64(16);
        B32(1);
        B32(0);

        U32((uint)MilCommandKind.RenderData);
        U32(renderData);
        U32((uint)blob.Count);
        writer.AddRange(blob);
        U32((uint)MilCommandKind.VisualSetContent);
        U32(visual);
        U32(renderData);
        U32((uint)MilCommandKind.TargetSetRoot);
        U32(0);
        U32(visual);

        MilCommandParser.ParseChannel(writer.ToArray(), frame.Graph);
        frame.Graph.SetRenderDataDependents(new Nova.Geometry.ResourceHandle(renderData), [new Nova.Geometry.ResourceHandle(brush)]);
    }
}
