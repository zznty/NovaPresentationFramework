using System.Text;

namespace Nova.DesktopTheme.Tests;

public sealed partial class GtkCssThemeTests
{
    [Fact]
    public void AdwaitaDefaults_HaveCanonicalMetrics()
    {
        GtkThemeMetrics theme = GtkThemeMetrics.AdwaitaDefault;

        Assert.NotNull(theme.Controls["button"]);
        Assert.Equal(6, theme.Controls["button"].BorderRadius);
        Assert.Equal(1, theme.Controls["button"].BorderWidth);
        Assert.Equal(24, theme.Controls["button"].MinHeight);
        Assert.Equal(100, theme.Controls["radiobutton"].BorderRadius);
        Assert.Equal(48, theme.Controls["switch"].MinWidth);
        Assert.Equal(new GtkColor(250, 250, 250), theme.WindowBackground);
    }

    [Fact]
    public void FromCss_OverridesStructureAndStates()
    {
        const string css = """
            @define-color theme_accent #3584e4;
            button {
                border-radius: 9px;
                padding: 10px;
                min-height: 40px;
            }
            button:hover {
                background-color: alpha(@theme_accent, 0.5);
            }
            button:disabled {
                color: shade(#808080, 0.5);
            }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        GtkControlMetrics button = theme.Controls["button"];
        Assert.Equal(9, button.BorderRadius);
        Assert.Equal(10, button.PaddingTop);
        Assert.Equal(10, button.PaddingLeft);
        Assert.Equal(40, button.MinHeight);
        Assert.Equal(new GtkColor(53, 132, 228, 0.5), button.Hover.Background);
        Assert.Equal(new GtkColor(64, 64, 64), button.Disabled.Color);
    }

    [Fact]
    public void FromCss_SpecificityOrdersOverrides()
    {
        const string css = """
            button { border-radius: 4px; }
            button.big { border-radius: 12px; }
            button { border-radius: 6px; }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        // Variant rules (.big) describe a widget variant the extractor cannot
        // represent; they must not override the base button geometry.
        Assert.Equal(6, theme.Controls["button"].BorderRadius);
    }

    [Fact]
    public void FromCss_BackgroundClassAndDefines_SetSurfaces()
    {
        const string css = """
            @define-color theme_bg_color #1e1e1e;
            @define-color theme_base_color #141414;
            @define-color theme_fg_color #eeeeee;
            .background { background-color: @theme_bg_color; }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        Assert.Equal(new GtkColor(30, 30, 30), theme.WindowBackground);
        Assert.Equal(new GtkColor(20, 20, 20), theme.ViewBackground);
        Assert.Equal(new GtkColor(238, 238, 238), theme.TextColor);
    }

    [Fact]
    public void FromCss_DefinesWithoutSurfaceRules_StillSetSurfaces()
    {
        const string css = """
            @define-color theme_bg_color #1e1e1e;
            @define-color theme_base_color #141414;
            @define-color theme_fg_color #eeeeee;
            button { border-radius: 5px; }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        Assert.Equal(new GtkColor(30, 30, 30), theme.WindowBackground);
        Assert.Equal(new GtkColor(20, 20, 20), theme.ViewBackground);
        Assert.Equal(new GtkColor(238, 238, 238), theme.TextColor);
    }

    [Fact]
    public void FromCss_ThemeLevelColors_MapToSurfaceAndText()
    {
        const string css = """
            window { background-color: #101010; }
            view { background-color: #202020; }
            label { color: #eeeeee; }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        Assert.Equal(new GtkColor(16, 16, 16), theme.WindowBackground);
        Assert.Equal(new GtkColor(32, 32, 32), theme.ViewBackground);
        Assert.Equal(new GtkColor(238, 238, 238), theme.TextColor);
    }

    [Fact]
    public void GtkColor_ParsesGrammar()
    {
        Assert.Equal(new GtkColor(255, 0, 0), GtkColor.Parse("#f00"));
        Assert.Equal(new GtkColor(18, 52, 86), GtkColor.Parse("#123456"));
        Assert.Equal(new GtkColor(18, 52, 86, 128.0 / 255.0), GtkColor.Parse("#12345680"));
        Assert.Equal(new GtkColor(1, 2, 3), GtkColor.Parse("rgb(1, 2, 3)"));
        Assert.Equal(new GtkColor(1, 2, 3, 0.25), GtkColor.Parse("rgba(1, 2, 3, 0.25)"));
        Assert.Equal(new GtkColor(255, 255, 255), GtkColor.Parse("white"));
        Assert.Null(GtkColor.Parse("12px"));
        Assert.Null(GtkColor.Parse("linear-gradient(to bottom, #fff, #000)"));
    }

    [Fact]
    public void GtkColor_Functions_MatchGtkSemantics()
    {
        GtkColor gray = new(128, 128, 128);
        // shade toward white (factor > 1) and black (factor < 1).
        Assert.Equal(new GtkColor(160, 160, 160), GtkColor.Shade(gray, 1.25));
        Assert.Equal(new GtkColor(64, 64, 64), GtkColor.Shade(gray, 0.5));
        Assert.Equal(new GtkColor(160, 160, 160), GtkColor.Mix(gray, new GtkColor(255, 255, 255), 0.25));
        Assert.Equal(new GtkColor(128, 128, 128, 0.5), GtkColor.Alpha(gray, 0.5));
    }

    [Fact]
    public void Load_MatchesTheDiscoveredHostTheme()
    {
        // The loader must return exactly what the discovered CSS produces
        // (defaults when no GTK theme CSS exists, the parsed theme otherwise).
        string? css = GtkCssTheme.DiscoverCss();
        GtkThemeMetrics expected = css is null ? GtkThemeMetrics.AdwaitaDefault : GtkThemeMetrics.FromCss(css);
        GtkThemeMetrics theme = GtkThemeMetrics.Load();

        Assert.Equal(expected.WindowBackground, theme.WindowBackground);
        Assert.Equal(expected.Controls.Count, theme.Controls.Count);
    }
}

public sealed partial class GtkCssThemeTests
{
    [Fact]
    public void ImportResolution_InlinesImportedDefines()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-gtk-imports-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "colors.css"), "@define-color theme_bg #1e1e1e;");
            File.WriteAllText(Path.Combine(dir, "base.css"), "@import 'colors.css';\nbutton { border-radius: 8px; }");

            var css = new StringBuilder();
            GtkCssTheme.AppendWithImports(css, Path.Combine(dir, "base.css"));
            string text = css.ToString();

            Assert.Contains("@define-color theme_bg #1e1e1e;", text, StringComparison.Ordinal);
            GtkThemeMetrics theme = GtkThemeMetrics.FromCss(text);
            Assert.Equal(8, theme.Controls["button"].BorderRadius);
            Assert.Equal(new GtkColor(30, 30, 30), GtkCssTheme.ResolveDefineColors(text)["theme_bg"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ImportResolution_SkipsResourceImports()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nova-gtk-imports-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "base.css"), "@import url(\"resource:///org/gtk/theme.css\");\nbutton { border-radius: 7px; }");

            var css = new StringBuilder();
            GtkCssTheme.AppendWithImports(css, Path.Combine(dir, "base.css"));

            Assert.Contains("border-radius: 7px", css.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
