namespace Nova.SystemTheme.Tests;

public sealed class SystemThemeTests
{
    [Fact]
    public void GetSysColor_WindowText_IsBlack()
    {
        Assert.Equal(0x00000000, HostTheme.GetSysColor(SystemColorIndex.WindowText));
        Assert.Equal(0x00FFFFFF, HostTheme.GetSysColor(SystemColorIndex.Window));
    }

    [Fact]
    public void NonClient_MessageFont_IsDejaVuSans()
    {
        Assert.Equal("DejaVu Sans", HostTheme.NonClient.MessageFont.FaceName);
        Assert.Equal(-12, HostTheme.NonClient.MessageFont.Height);
        Assert.True(HostTheme.NonClient.CaptionHeight > 0);
    }

    [Fact]
    public void GetWorkArea_HasPositiveSize()
    {
        HostTheme.GetWorkArea(out int left, out int top, out int right, out int bottom);
        Assert.Equal(0, left);
        Assert.Equal(0, top);
        Assert.True(right > left);
        Assert.True(bottom > top);
    }

    [Fact]
    public void GetSystemMetric_PrimaryScreen_MatchesWorkArea()
    {
        Assert.Equal(1920, HostTheme.GetSystemMetric(SystemMetricIndex.CxScreen));
        Assert.Equal(1080, HostTheme.GetSystemMetric(SystemMetricIndex.CyScreen));
        Assert.Equal(1, HostTheme.GetSystemMetric(SystemMetricIndex.MousePresent));
        Assert.Equal(0, HostTheme.GetSystemMetric(-1));
    }

    [Fact]
    public void DoubleClickTime_Is500ms()
    {
        Assert.Equal(500, HostTheme.DoubleClickTime);
        Assert.False(HostTheme.IsHighContrast);
        Assert.False(HostTheme.IsThemeActive);
        Assert.True(HostTheme.IsProcessDpiAware);
        Assert.Equal(96, HostTheme.PixelsPerInch);
    }

    [Fact]
    public void ThemeSelection_DefaultIsClassic()
    {
        // No NOVA_THEME and no programmatic override: the historic Classic behavior.
        HostTheme.SetTheme(null);
        Assert.Equal("classic", HostTheme.ThemeName);
        Assert.False(HostTheme.IsThemeActive);
        Assert.Equal("classic.msstyles", HostTheme.UxThemeFileName);
        Assert.Equal("NormalColor", HostTheme.UxThemeColor);
        Assert.Equal(string.Empty, HostTheme.UxThemeSize);
    }

    [Fact]
    public void ThemeSelection_Aero2OptIn_ActivatesTheme()
    {
        HostTheme.SetTheme("aero2");
        try
        {
            Assert.Equal("aero2", HostTheme.ThemeName);
            Assert.True(HostTheme.IsThemeActive);
            Assert.Equal("aero2.msstyles", HostTheme.UxThemeFileName);
        }
        finally
        {
            HostTheme.SetTheme(null);
        }
    }

    [Fact]
    public void ThemeSelection_AeroOptIn_NormalizesToAero()
    {
        HostTheme.SetTheme("AERO");
        try
        {
            Assert.Equal("aero", HostTheme.ThemeName);
            Assert.True(HostTheme.IsThemeActive);
            Assert.Equal("aero.msstyles", HostTheme.UxThemeFileName);
        }
        finally
        {
            HostTheme.SetTheme(null);
        }
    }

    [Fact]
    public void ThemeSelection_UnknownValueFallsBackToClassic()
    {
        HostTheme.SetTheme("bogus");
        try
        {
            Assert.Equal("classic", HostTheme.ThemeName);
            Assert.False(HostTheme.IsThemeActive);
        }
        finally
        {
            HostTheme.SetTheme(null);
        }
    }

    [Fact]
    public void ThemeSelection_FluentOptIn_IsFluentThemeAndKeepsUxThemeClassic()
    {
        // Fluent is a separate system from uxtheme: NOVA_THEME=fluent / SetTheme("fluent")
        // selects the Fluent dictionary (via Application.ThemeMode) while the uxtheme ABI
        // keeps reporting Classic (visual styles are not active under Fluent, as on Win11).
        HostTheme.SetTheme("fluent");
        try
        {
            Assert.True(HostTheme.IsFluentTheme);
            Assert.Equal("classic", HostTheme.ThemeName);
            Assert.False(HostTheme.IsThemeActive);
            Assert.Equal("classic.msstyles", HostTheme.UxThemeFileName);
        }
        finally
        {
            HostTheme.SetTheme(null);
        }
    }

    [Fact]
    public void ThemeSelection_FluentEnvVar_IsFluentTheme()
    {
        // NOVA_THEME=fluent in the process environment selects Fluent; SetTheme(null)
        // re-reads the environment (the host-bootstrap contract).
        HostTheme.SetTheme(null);
        string? previous = Environment.GetEnvironmentVariable("NOVA_THEME");
        try
        {
            Environment.SetEnvironmentVariable("NOVA_THEME", "fluent");
            HostTheme.SetTheme(null);
            Assert.True(HostTheme.IsFluentTheme);
            Assert.Equal("classic", HostTheme.ThemeName);
            Assert.False(HostTheme.IsThemeActive);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVA_THEME", previous);
            HostTheme.SetTheme(null);
        }
    }

    [Fact]
    public void FillCurrentThemeName_FillsBuffersAndReturnsS_OK()
    {
        HostTheme.SetTheme("aero2");
        try
        {
            var name = new System.Text.StringBuilder();
            var color = new System.Text.StringBuilder();
            int hr = HostTheme.FillCurrentThemeName(name, color, null);
            Assert.Equal(0, hr);
            Assert.Equal("aero2.msstyles", name.ToString());
            Assert.Equal("NormalColor", color.ToString());

            // Null-safe: a null size buffer must not throw (UxThemeWrapper passes null).
            int hr2 = HostTheme.FillCurrentThemeName(null, null, null);
            Assert.Equal(0, hr2);
        }
        finally
        {
            HostTheme.SetTheme(null);
        }
    }
}
