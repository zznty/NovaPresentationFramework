using System.Text;
using JetBrains.Annotations;

namespace Nova.SystemTheme;

/// <summary>
/// Host system colors, fonts, and non-client metrics.
/// On Windows this can later read the real SPI/GetSysColor values.
/// On Linux it returns a light-desktop default set so WPF cctors can run.
/// </summary>
[PublicAPI]
public static class HostTheme
{
    public const string DefaultFontFamily = "DejaVu Sans";

    private const string ClassicTheme = "classic";
    private const string AeroTheme = "aero";
    private const string Aero2Theme = "aero2";
    private const string FluentTheme = "fluent";

    /// <summary>
    /// Win32 <c>NONCLIENTMETRICS</c> (fonts + chrome metrics). Delegates to the provider when it
    /// supplies metrics; otherwise the hardcoded Classic defaults.
    /// </summary>
    public static NonClientMetrics NonClient => Provider?.NonClient ?? CreateDefaultNonClient();

    private static IHostMetrics? s_provider;

    private static string? s_themeName;

    /// <summary>
    /// Selects the themed dictionary opt-in. <c>null</c> restores the environment-derived
    /// selection (<c>NOVA_THEME</c>); any other value is normalized to
    /// <c>classic</c> / <c>aero</c> / <c>aero2</c> / <c>fluent</c>. Default (no env var,
    /// no override) is <c>aero2</c>, matching the uxtheme theme WPF resolves on Windows
    /// 10/11 (flat tabs; the classic theme draws the rolled-corner TabItem geometry).
    /// <c>fluent</c> selects the WPF Fluent theme (<see cref="IsFluentTheme"/>), which is a
    /// separate system from uxtheme visual styles: under Fluent the uxtheme
    /// <see cref="ThemeName"/> stays <c>classic</c> so non-Fluent styles fall back to
    /// Classic, exactly as on Windows 11.
    /// </summary>
    public static void SetTheme(string? theme)
    {
        Volatile.Write(ref s_themeName, theme is null ? null : NormalizeTheme(theme));
    }

    /// <summary>
    /// The selected uxtheme theme name: <c>"classic"</c> (default), <c>"aero"</c>, or
    /// <c>"aero2"</c>. On a Win10-reporting host, <c>"aero"</c> resolves to the Aero2
    /// dictionary inside <c>UxThemeWrapper</c> (stock aero→Aero2 mapping). A
    /// <c>fluent</c> selection reports <c>classic</c> here — Fluent is not a uxtheme
    /// visual style and is surfaced through <see cref="IsFluentTheme"/> instead.
    /// </summary>
    public static string ThemeName
    {
        get
        {
            string? theme = Volatile.Read(ref s_themeName);
            if (theme is null)
            {
                theme = NormalizeTheme(Environment.GetEnvironmentVariable("NOVA_THEME"));
                Volatile.Write(ref s_themeName, theme);
            }

            return theme == FluentTheme ? ClassicTheme : theme;
        }
    }

    /// <summary>
    /// True when the WPF Fluent theme is selected (<c>NOVA_THEME=fluent</c> or
    /// <see cref="SetTheme"/><c>("fluent")</c>). The host bootstrap maps this to
    /// <c>Application.Current.ThemeMode</c> (the stock WPF Fluent opt-in), which loads the
    /// <c>PresentationFramework.Fluent</c> dictionaries. Defaults to false (Classic).
    /// </summary>
    public static bool IsFluentTheme
    {
        get
        {
            string? theme = Volatile.Read(ref s_themeName);
            if (theme is null)
            {
                theme = NormalizeTheme(Environment.GetEnvironmentVariable("NOVA_THEME"));
                Volatile.Write(ref s_themeName, theme);
            }

            return theme == FluentTheme;
        }
    }

    private static string NormalizeTheme(string? raw)
    {
        return raw?.Trim().ToUpperInvariant() switch
        {
            "AERO" => AeroTheme,
            "AERO2" => Aero2Theme,
            "FLUENT" => FluentTheme,
            // Default: Aero2 — the uxtheme theme WPF reports on Windows 10/11 (Flat tabs,
            // no classic rolled corners). Classic remains available via NOVA_THEME=classic.
            // Default: Aero2 — the uxtheme theme WPF reports on Windows 10/11 (Flat tabs,
            // no classic rolled corners). Classic remains available via NOVA_THEME=classic.
            _ => Aero2Theme,
        };
    }

    /// <summary>
    /// Replaces the live metric source. <c>null</c> restores the hardcoded Classic defaults
    /// (96 DPI, 1920×1080, 500 ms double-click). Implementations must not throw.
    /// </summary>
    public static void SetProvider(IHostMetrics? provider)
    {
        Volatile.Write(ref s_provider, provider);
    }

    private static IHostMetrics? Provider => Volatile.Read(ref s_provider);

    /// <summary>Win32 <c>COLORREF</c> <c>0x00BBGGRR</c> for <paramref name="index"/>.</summary>
    public static int GetSysColor(int index)
    {
        int? fromProvider = Provider?.GetSysColor(index);
        return fromProvider ?? (index switch
        {
            SystemColorIndex.ScrollBar => 0x00C8C8C8,
            SystemColorIndex.Background => 0x00C8C8C8,
            SystemColorIndex.ActiveCaption => 0x00D1B499,
            SystemColorIndex.InactiveCaption => 0x00DBCDCD,
            SystemColorIndex.Menu => 0x00F0F0F0,
            SystemColorIndex.Window => 0x00FFFFFF,
            SystemColorIndex.WindowFrame => 0x00646464,
            SystemColorIndex.MenuText => 0x00000000,
            SystemColorIndex.WindowText => 0x00000000,
            SystemColorIndex.CaptionText => 0x00000000,
            SystemColorIndex.ActiveBorder => 0x00B4B4B4,
            SystemColorIndex.InactiveBorder => 0x00F4F7FC,
            SystemColorIndex.AppWorkspace => 0x00ABABAB,
            SystemColorIndex.Highlight => 0x00D77800,
            SystemColorIndex.HighlightText => 0x00FFFFFF,
            SystemColorIndex.ButtonFace => 0x00F0F0F0,
            SystemColorIndex.ButtonShadow => 0x00A0A0A0,
            SystemColorIndex.GrayText => 0x006D6D6D,
            SystemColorIndex.ButtonText => 0x00000000,
            SystemColorIndex.InactiveCaptionText => 0x00000000,
            SystemColorIndex.ButtonHighlight => 0x00FFFFFF,
            SystemColorIndex.ThreeDDarkShadow => 0x00696969,
            SystemColorIndex.ThreeDLight => 0x00E3E3E3,
            SystemColorIndex.InfoText => 0x00000000,
            SystemColorIndex.Info => 0x00E1FFFF,
            SystemColorIndex.HotLight => 0x00CC6600,
            SystemColorIndex.GradientActiveCaption => 0x00EAD1B9,
            SystemColorIndex.GradientInactiveCaption => 0x00F2E4D7,
            SystemColorIndex.MenuHighlight => 0x00D77800,
            SystemColorIndex.MenuBar => 0x00F0F0F0,
            _ => 0x00C8C8C8
        });
    }

    public static bool GetBoolParameter(int action)
    {
        _ = action;
        return false;
    }

    public static int GetIntParameter(int action)
    {
        // SPI_GETCARETWIDTH (0x2006): Linux has no Win32 caret-width setting; a 1px caret
        // matches the WPF default (SystemParameters.CaretWidth drives the visible caret
        // width). Everything else stays 0.
        return action == 0x2006 ? 1 : 0;
    }

    public static void GetWorkArea(out int left, out int top, out int right, out int bottom)
    {
        IHostMetrics? provider = Provider;
        if (provider is null)
        {
            left = 0;
            top = 0;
            right = 1920;
            bottom = 1080;
            return;
        }

        provider.GetWorkArea(out left, out top, out right, out bottom);
    }

    /// <summary>
    /// Win32 <c>GetSystemMetrics</c> values. Unknown indices return 0. The SDL-backed indices
    /// (screen, virtual screen, monitor count) delegate to <see cref="Provider"/> when set.
    /// </summary>
    public static int GetSystemMetric(int index)
    {
        IHostMetrics? provider = Provider;
        return provider is not null && IsProviderBacked(index)
            ? provider.GetSystemMetric(index)
            : index switch
            {
                SystemMetricIndex.CxDoubleClick => 4,
                SystemMetricIndex.CyDoubleClick => 4,
                SystemMetricIndex.CxDrag => 4,
                SystemMetricIndex.CyDrag => 4,
                SystemMetricIndex.MousePresent => 1,
                SystemMetricIndex.MouseButtons => 5,
                SystemMetricIndex.MouseWheelPresent => 1,
                SystemMetricIndex.CxScreen => 1920,
                SystemMetricIndex.CyScreen => 1080,
                SystemMetricIndex.CxVirtualScreen => 1920,
                SystemMetricIndex.CyVirtualScreen => 1080,
                SystemMetricIndex.MonitorCount => 1,
                SystemMetricIndex.SameDisplayFormat => 1,
                // Classic (uxtheme-off) Win32 defaults for the metrics WPF reads for chrome
                // (scrollbars, borders, frames, cursors, icons, menus, caption). Without
                // these, e.g. VerticalScrollBarWidth resolves to 0 and templates that size
                // from SystemParameters (the ComboBox button) collapse to 0-width.
                SystemMetricIndex.CxVScroll => 17,
                SystemMetricIndex.CyVScroll => 17,
                SystemMetricIndex.CxHScroll => 17,
                SystemMetricIndex.CyHScroll => 17,
                SystemMetricIndex.CxVThumb => 17,
                SystemMetricIndex.CyHThumb => 17,
                SystemMetricIndex.CxBorder => 1,
                SystemMetricIndex.CyBorder => 1,
                SystemMetricIndex.CxEdge => 2,
                SystemMetricIndex.CyEdge => 2,
                SystemMetricIndex.CxFixedFrame => 3,
                SystemMetricIndex.CyFixedFrame => 3,
                SystemMetricIndex.CxFrame => 3,
                SystemMetricIndex.CyFrame => 3,
                SystemMetricIndex.CxMinTrack => 2,
                SystemMetricIndex.CyMinTrack => 2,
                SystemMetricIndex.CxMin => 2,
                SystemMetricIndex.CyMin => 2,
                SystemMetricIndex.CxIcon => 16,
                SystemMetricIndex.CyIcon => 16,
                SystemMetricIndex.CxCursor => 16,
                SystemMetricIndex.CyCursor => 16,
                SystemMetricIndex.CxSmIcon => 16,
                SystemMetricIndex.CySmIcon => 16,
                SystemMetricIndex.CxIconSpacing => 75,
                SystemMetricIndex.CyIconSpacing => 75,
                SystemMetricIndex.CyCaption => 19,
                SystemMetricIndex.CxSize => 12,
                SystemMetricIndex.CySize => 12,
                SystemMetricIndex.CxMenuSize => 21,
                SystemMetricIndex.CyMenuSize => 21,
                _ => 0
            };
    }

    private static bool IsProviderBacked(int index)
    {
        return index is SystemMetricIndex.CxScreen
            or SystemMetricIndex.CyScreen
            or SystemMetricIndex.CxVirtualScreen
            or SystemMetricIndex.CyVirtualScreen
            or SystemMetricIndex.MonitorCount;
    }

    /// <summary>Win32 <c>GetDoubleClickTime</c> in milliseconds. Delegates to the provider when set.</summary>
    public static int DoubleClickTime => Provider?.DoubleClickTime ?? 500;

    /// <summary>Win32 <c>SPI_GETHIGHCONTRAST</c>. Linux default is off.</summary>
    public static bool IsHighContrast => false;

    /// <summary>The desktop theme is light (no Windows personalization on Linux).</summary>
    public static bool IsLightTheme => true;

    /// <summary>
    /// Win32 <c>IsThemeActive</c> (uxtheme visual-styles ABI). True only when a themed
    /// (non-Classic) uxtheme dictionary has been selected via <c>NOVA_THEME</c> or
    /// <see cref="SetTheme"/>. The Fluent selection reports false here on purpose: Fluent
    /// bypasses uxtheme (as on Windows 11), so visual styles stay Classic under Fluent;
    /// use <see cref="IsFluentTheme"/> to detect Fluent. Default (no <c>NOVA_THEME</c>, no override) is Aero2, matching Windows 10/11.
    /// </summary>
    public static bool IsThemeActive => ThemeName != ClassicTheme;

    /// <summary>
    /// Win32 <c>GetCurrentThemeName</c> pszThemeFileName: the uxtheme file name whose
    /// extension-less basename WPF's <c>UxThemeWrapper</c> turns into <see cref="ThemeName"/>.
    /// On a Win10-reporting host, <c>"aero.msstyles"</c> is mapped by stock WPF to
    /// <c>"Aero2"</c>, so the Aero2 dictionary is the faithful themed choice.
    /// </summary>
    public static string UxThemeFileName => ThemeName switch
    {
        AeroTheme => "aero.msstyles",
        Aero2Theme => "aero2.msstyles",
        _ => "classic.msstyles",
    };

    /// <summary>Win32 <c>GetCurrentThemeName</c> pszColorBuff. Always the NormalColor variant.</summary>
    public static string UxThemeColor => "NormalColor";

    /// <summary>Win32 <c>GetCurrentThemeName</c> pszSizeBuff. uxtheme has no size on this host.</summary>
    public static string UxThemeSize => string.Empty;

    /// <summary>
    /// Fills the three <c>GetCurrentThemeName</c> output buffers (null-safe) and returns
    /// <c>S_OK</c> (0). Mirrors the uxtheme API so WPF can marshal it through unchanged.
    /// </summary>
    public static int FillCurrentThemeName(StringBuilder? fileName, StringBuilder? color, StringBuilder? size)
    {
        _ = fileName?.Append(UxThemeFileName);
        _ = color?.Append(UxThemeColor);
        _ = size?.Append(UxThemeSize);
        return 0; // S_OK
    }

    /// <summary>Win32 <c>IsProcessDPIAware</c>. Linux defaults to system-aware (96 DPI).</summary>
    public static bool IsProcessDpiAware => true;

    /// <summary>Win32 <c>LOGPIXELSX</c>/<c>LOGPIXELSY</c>. Delegates to the provider when set; 96 otherwise.</summary>
    public static int PixelsPerInch => Provider?.PixelsPerInch ?? 96;

    private static NonClientMetrics CreateDefaultNonClient()
    {
        var font = new SystemFontMetrics(DefaultFontFamily, -12, 400);
        return new NonClientMetrics(
            borderWidth: 1,
            scrollWidth: 17,
            scrollHeight: 17,
            captionWidth: 36,
            captionHeight: 23,
            smallCaptionWidth: 22,
            smallCaptionHeight: 22,
            menuWidth: 17,
            menuHeight: 20,
            captionFont: font,
            smallCaptionFont: font,
            menuFont: font,
            statusFont: font,
            messageFont: font);
    }
}
