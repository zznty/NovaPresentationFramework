using Nova.SystemTheme;

namespace Nova.DesktopTheme.Tests;

public sealed class DesktopThemeProviderTests
{
    [Fact]
    public void GetSysColor_PresentSlot_ReturnsPaletteColor()
    {
        var palette = new DesktopPalette(
            new Dictionary<int, int> { [SystemColorIndex.Highlight] = 0x00E6A00A },
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);
        Assert.Equal(0x00E6A00A, provider.GetSysColor(SystemColorIndex.Highlight));
    }

    [Fact]
    public void GetSysColor_AbsentSlot_ReturnsNull()
    {
        var palette = new DesktopPalette(
            new Dictionary<int, int>(),
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);
        Assert.Null(provider.GetSysColor(SystemColorIndex.Window));
    }

    [Fact]
    public void Metrics_DelegateToInner()
    {
        var palette = new DesktopPalette(
            new Dictionary<int, int>(),
            font: null,
            pixelsPerInch: 120,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);

        provider.GetWorkArea(out int left, out int top, out int right, out int bottom);
        Assert.Equal((0, 0, 3200, 1800), (left, top, right, bottom));
        Assert.Equal(500, provider.DoubleClickTime);
        Assert.Equal(7, provider.GetSystemMetric(SystemMetricIndex.MonitorCount));
        // DPI comes from the palette when present.
        Assert.Equal(120, provider.PixelsPerInch);
    }

    [Fact]
    public void PixelsPerInch_MissingInPalette_FallsBackToInner()
    {
        var palette = new DesktopPalette(
            new Dictionary<int, int>(),
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);
        Assert.Equal(96, provider.PixelsPerInch);
    }

    [Fact]
    public void NonClient_WithFont_ProducesMetricsWithDesktopFont()
    {
        var font = new SystemFontMetrics("Noto Sans", -13, 400);
        var palette = new DesktopPalette(
            new Dictionary<int, int>(),
            font,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);
        NonClientMetrics? metrics = provider.NonClient;
        NonClientMetrics actual = Assert.NotNull(metrics);
        Assert.Equal("Noto Sans", actual.MessageFont.FaceName);
        Assert.Equal(-13, actual.MessageFont.Height);
        Assert.Equal(1, actual.BorderWidth);
        Assert.Equal(17, actual.ScrollWidth);
    }

    [Fact]
    public void NonClient_WithoutFont_ReturnsNull()
    {
        var palette = new DesktopPalette(
            new Dictionary<int, int>(),
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), palette);
        Assert.Null(provider.NonClient);
    }

    [Fact]
    public void ReplacePalette_SwapsColorsLive()
    {
        var first = new DesktopPalette(
            new Dictionary<int, int> { [SystemColorIndex.Window] = 0x001E1E1E },
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var second = new DesktopPalette(
            new Dictionary<int, int> { [SystemColorIndex.Window] = 0x00FFFFFF },
            font: null,
            pixelsPerInch: null,
            accentColorRef: null,
            isDark: null);
        var provider = new DesktopThemeProvider(new StubMetrics(), first);
        Assert.Equal(0x001E1E1E, provider.GetSysColor(SystemColorIndex.Window));
        provider.ReplacePalette(second);
        Assert.Equal(0x00FFFFFF, provider.GetSysColor(SystemColorIndex.Window));
    }

    private sealed class StubMetrics : IHostMetrics
    {
        public int PixelsPerInch => 96;

        public int DoubleClickTime => 500;

        public void GetWorkArea(out int left, out int top, out int right, out int bottom)
        {
            left = 0;
            top = 0;
            right = 3200;
            bottom = 1800;
        }

        public int GetSystemMetric(int index)
        {
            return index switch
            {
                SystemMetricIndex.CxScreen => 3200,
                SystemMetricIndex.CyScreen => 1800,
                SystemMetricIndex.MonitorCount => 7,
                _ => 0
            };
        }
    }
}
