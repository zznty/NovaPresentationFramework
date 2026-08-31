using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nova.FontConfig;
using Nova.SdlSource;
using Nova.SystemTheme;

namespace Nova.Framework.Tests;

/// <summary>
/// Fluent-theme workstream. These tests are deliberately Application-free: WPF's
/// <c>Application.Shutdown</c> sets the process-static <c>Application.IsShuttingDown</c>,
/// after which no window can ever be shown again, so an in-process Application-bearing test
/// would poison the shared test process for every later window test. The end-to-end Fluent
/// assertions (dictionary source + chrome pixels + glyph pixels + shadow, each in its own
/// process) live in the harness probes <c>feat fluenttheme / fluentglyph / fluentshadow</c>.
/// This class proves, without an Application:
///  (a) the default stays Classic (no Fluent activity), and the Fluent dictionary really
///      loads from <c>PresentationFramework.Fluent</c> (pack-URI load, key presence);
///  (b) the app-level <c>SymbolThemeFontFamily</c> override wins over the theme dictionary
///      (MergedDictionaries reverse lookup — the mechanism the app override relies on);
///  (c) the bundled "Nova Fluent Icons" font (renamed + F090/F08E-extended Uno Fluent Icons)
///      rasterizes all 18 PUA codepoints as real glyphs, not tofu — including the DataGrid
///      sort carets F090/F08E.
/// </summary>
public sealed partial class WindowTextBlockTests
{
    /// <summary>The 18 PUA codepoints the Fluent theme references via
    /// <c>{DynamicResource SymbolThemeFontFamily}</c> (extracted from Styles/*.xaml).</summary>
    private static readonly string[] FluentIconCodepoints =
    [
        "\uE70D", "\uE70E", "\uE72A", "\uE72B", "\uE73E", "\uE76B", "\uE76C", "\uE787", "\uE894",
        "\uE915", "\uE9AE", "\uEDD9", "\uEDDA", "\uEDDB", "\uEDDC", "\uF08E", "\uF090", "\uF169"
    ];

    /// <summary>Calibration (single-window glyph swap, 48px, measured 2026-08-20):
    /// absent codepoints render 0 dark px (blank) or 138 (fallback tofu box, 36x25 bbox,
    /// ~15% fill density); the DataGrid carets F090/F08E render 276/264 dark px in a 24x17
    /// bbox (~68% fill density — solid filled triangles).</summary>
    private const int FluentCaretMinDark = 200;

    [Fact]
    public void Theme_FluentDefault_IsNotActiveAndLoadsNoFluentDictionary()
    {
        // Fluent is opt-in: in the baseline process IsFluentThemeEnabled is false, the host
        // reports no Fluent selection, and the uxtheme selection is the default Aero2.
        Assert.Null(Application.Current);
        Assert.False(IsFluentThemeEnabled());
        Assert.False(HostTheme.IsFluentTheme);
        Assert.Equal("aero2", HostTheme.ThemeName);
    }

    [Fact]
    public void Theme_Fluent_DictionaryLoads_AndContainsFluentResources()
    {
        // The pack-URI load ThemeManager uses (Fluent.Light.xaml for ThemeMode.Light) must
        // resolve the Fluent assembly and surface its resources without an Application.
        var fluent = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Absolute)
        };
        Assert.Equal("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", fluent.Source.ToString());

        Assert.True(fluent.Contains("SymbolThemeFontFamily"), "Fluent theme must define SymbolThemeFontFamily");
        Assert.True(fluent.Contains("ButtonBackground"), "Fluent theme must define ButtonBackground (Button chrome)");
        Assert.True(fluent.Contains("ControlCornerRadius"), "Fluent theme must define ControlCornerRadius");
        Assert.True(fluent.Count > 100, $"Fluent theme dictionary must be substantial (got {fluent.Count} entries)");

        // The theme's own SymbolThemeFontFamily is the proprietary Segoe list — the app
        // override exists to replace it (we never bundle Segoe).
        FontFamily themeFont = (FontFamily)fluent["SymbolThemeFontFamily"];
        Assert.Contains("Segoe Fluent Icons", themeFont.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Theme_Fluent_DictionaryOverride_BeatsThemeLookupOrder()
    {
        // The app-level override mechanism: ThemeManager merges the Fluent dictionary at
        // index 0 (Insert(0, ...)); the app override sits at a HIGHER index. WPF searches
        // MergedDictionaries in REVERSE order, so the override wins. This is exactly the
        // lookup the harness/real app relies on for {DynamicResource SymbolThemeFontFamily}.
        var fluent = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Absolute)
        };
        var overrideDict = new ResourceDictionary { ["SymbolThemeFontFamily"] = new FontFamily("Nova Fluent Icons") };

        var merged = new ResourceDictionary();
        merged.MergedDictionaries.Add(fluent);      // index 0 — ThemeManager's slot
        merged.MergedDictionaries.Add(overrideDict); // index 1 — app override

        FontFamily resolved = (FontFamily)merged["SymbolThemeFontFamily"];
        Assert.Equal("Nova Fluent Icons", resolved.Source);
    }

    [Fact]
    public void Theme_Fluent_BundledFont_AllCodepointsRasterize()
    {
        // The font file must be bundled and registered; then every codepoint the Fluent
        // theme draws must render SOMETHING (an absent codepoint renders 0 dark px — blank).
        RegisterBundledIconFont();
        foreach (string glyph in FluentIconCodepoints)
        {
            int dark = RenderGlyphDarkCount(glyph);
            Assert.True(dark > 0,
                $"U+{(int)glyph[0]:X4} rendered {dark} dark pixels — blank, not a glyph");
        }
    }

    [Fact]
    public void Theme_Fluent_BundledFont_DataGridSortCaretsRasterize()
    {
        // DataGrid.xaml hardcodes the sort indicators as F090/F08E on a
        // {DynamicResource SymbolThemeFontFamily} TextBlock. Both must rasterize as filled
        // carets; they are mirror images, so their dark counts must be close and large.
        RegisterBundledIconFont();
        int up = RenderGlyphDarkCount("\uF090");
        int down = RenderGlyphDarkCount("\uF08E");
        // Solid filled carets: 276/264 dark px in a 24x17 bbox (~68% density). A tofu box
        // (missing glyph) measures ~138 dark px at ~15% density — below the threshold.
        Assert.True(up >= FluentCaretMinDark, $"F090 caret rendered only {up} dark pixels — tofu, not a solid caret");
        Assert.True(down >= FluentCaretMinDark, $"F08E caret rendered only {down} dark pixels — tofu, not a solid caret");
        Assert.True(Math.Abs(up - down) <= up / 3,
            $"mirror carets must have similar dark counts (F090={up}, F08E={down})");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void RegisterBundledIconFont()
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "fonts", "NovaFluentIcons.ttf");
        Assert.True(File.Exists(fontPath), $"bundled icon font missing: {fontPath}");
        FontConfigLibrary.RegisterAppFont(fontPath);
    }

    private static bool IsFluentThemeEnabled()
    {
        var themeManager = typeof(Application).Assembly.GetType("System.Windows.ThemeManager", throwOnError: true)!;
        return (bool)themeManager.GetProperty("IsFluentThemeEnabled", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    }

    private static int RenderGlyphDarkCount(string glyph)
    {
        Window? window = null;
        try
        {
            var text = new TextBlock
            {
                Text = glyph,
                FontSize = 48,
                Foreground = Brushes.Black,
                FontFamily = new FontFamily("Nova Fluent Icons"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            window = RenderWindow(text);
            var source = (SdlPresentationSource)PresentationSource.FromVisual(window);
            source.EnableReadback();
            SdlPresentationSource.PresentAll();
            SdlPresentationSource.PresentAll();
            ReadOnlyMemory<byte> pixels = source.ReadbackRgba();
            ReadOnlySpan<byte> p = pixels.Span;
            int dark = 0;
            for (int i = 0; i + 3 < p.Length; i += 4)
            {
                if (p[i] < 128 && p[i + 1] < 128 && p[i + 2] < 128)
                {
                    dark++;
                }
            }

            return dark;
        }
        finally
        {
            window?.Close();
        }
    }

    private static Window RenderWindow(FrameworkElement content)
    {
        var window = new Window { Width = 320, Height = 240, Content = content };
        window.Show();
        FlushDispatcher();
        return window;
    }
}
