using JetBrains.Annotations;
using Nova.SystemTheme;
using Silk.NET.Core;
using Silk.NET.SDL;
using SdlApi = Silk.NET.SDL.Sdl;

namespace Nova.Sdl;

/// <summary>
/// SDL3-backed <see cref="IHostMetrics"/> for <see cref="HostTheme"/>. Captures the display
/// topology at construction (SDL_Init must already have run). Every query that fails — e.g.
/// the offscreen video driver — falls back to the hardcoded Classic numbers; this type never
/// throws.
/// </summary>
[PublicAPI]
public sealed class SdlHostMetrics : IHostMetrics
{
    private const int FallbackWidth = 1920;
    private const int FallbackHeight = 1080;

    private readonly int _workLeft;
    private readonly int _workTop;
    private readonly int _workRight;
    private readonly int _workBottom;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private readonly int _virtualWidth;
    private readonly int _virtualHeight;
    private readonly int _monitorCount;

    public SdlHostMetrics()
    {
        uint primary = SdlApi.GetPrimaryDisplay();
        if (primary == 0 || !TryGetDisplayBounds(primary, out _, out _, out int sw, out int sh))
        {
            _workLeft = 0;
            _workTop = 0;
            _workRight = FallbackWidth;
            _workBottom = FallbackHeight;
            _screenWidth = FallbackWidth;
            _screenHeight = FallbackHeight;
            _virtualWidth = FallbackWidth;
            _virtualHeight = FallbackHeight;
            _monitorCount = 1;
            PixelsPerInch = 96;
            return;
        }

        _screenWidth = sw;
        _screenHeight = sh;

        if (TryGetDisplayUsableBounds(primary, out int wx, out int wy, out int ww, out int wh) && ww > 0 && wh > 0)
        {
            _workLeft = wx;
            _workTop = wy;
            _workRight = wx + ww;
            _workBottom = wy + wh;
        }
        else
        {
            _workLeft = 0;
            _workTop = 0;
            _workRight = FallbackWidth;
            _workBottom = FallbackHeight;
        }

        float scale = SdlApi.GetDisplayContentScale(primary);
        PixelsPerInch = scale > 0 && float.IsFinite(scale)
            ? Math.Max(96, (int)Math.Round(96 * scale, MidpointRounding.AwayFromZero))
            : 96;

        (int virtualWidth, int virtualHeight, int monitorCount) = QueryDisplays();
        _virtualWidth = virtualWidth;
        _virtualHeight = virtualHeight;
        _monitorCount = monitorCount;
    }

    public int PixelsPerInch { get; }

    public int DoubleClickTime => 500;

    public void GetWorkArea(out int left, out int top, out int right, out int bottom)
    {
        left = _workLeft;
        top = _workTop;
        right = _workRight;
        bottom = _workBottom;
    }

    public int GetSystemMetric(int index)
    {
        return index switch
        {
            SystemMetricIndex.CxScreen => _screenWidth,
            SystemMetricIndex.CyScreen => _screenHeight,
            SystemMetricIndex.CxVirtualScreen => _virtualWidth,
            SystemMetricIndex.CyVirtualScreen => _virtualHeight,
            SystemMetricIndex.MonitorCount => _monitorCount,
            _ => 0
        };
    }

    private static bool TryGetDisplayBounds(uint displayId, out int x, out int y, out int w, out int h)
    {
        var rect = new Rect();
        if (!SdlApi.GetDisplayBounds(displayId, new Ref<Rect>(ref rect)))
        {
            x = y = w = h = 0;
            return false;
        }

        x = rect.X;
        y = rect.Y;
        w = rect.W;
        h = rect.H;
        return true;
    }

    private static bool TryGetDisplayUsableBounds(uint displayId, out int x, out int y, out int w, out int h)
    {
        var rect = new Rect();
        if (!SdlApi.GetDisplayUsableBounds(displayId, new Ref<Rect>(ref rect)))
        {
            x = y = w = h = 0;
            return false;
        }

        x = rect.X;
        y = rect.Y;
        w = rect.W;
        h = rect.H;
        return true;
    }

    /// <summary>Union of all display bounds plus the display count. Falls back when no bounds are queryable.</summary>
    private static unsafe (int Width, int Height, int Count) QueryDisplays()
    {
        int count = 0;
        uint* displays = SdlApi.GetDisplays(&count);
        try
        {
            if (displays == null || count <= 0)
            {
                return (FallbackWidth, FallbackHeight, 1);
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (TryGetDisplayBounds(displays[i], out int x, out int y, out int w, out int h))
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxRight = Math.Max(maxRight, x + w);
                    maxBottom = Math.Max(maxBottom, y + h);
                }
            }

            return minX == int.MaxValue
                ? (FallbackWidth, FallbackHeight, count)
                : (maxRight - minX, maxBottom - minY, count);
        }
        finally
        {
            if (displays != null)
            {
                SdlApi.Free(displays);
            }
        }
    }
}
