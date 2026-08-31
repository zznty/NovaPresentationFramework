using System.Collections.ObjectModel;
using JetBrains.Annotations;

namespace Nova.DesktopTheme;

/// <summary>State-dependent visual colors of a control part.</summary>
[PublicAPI]
public sealed class GtkStateMetrics
{
    public GtkColor? Background { get; set; }

    public GtkColor? Color { get; set; }

    public GtkColor? BorderColor { get; set; }

    internal GtkStateMetrics Clone()
    {
        return new GtkStateMetrics
        {
            Background = Background,
            Color = Color,
            BorderColor = BorderColor
        };
    }
}

/// <summary>
/// Layout and visual metrics of one GTK control, as used to restyle the WPF
/// control templates. Structure properties (radius, padding, borders, min
/// sizes) are single-valued per control; colors carry the per-state variants.
/// </summary>
[PublicAPI]
public sealed class GtkControlMetrics
{
    public required string Control { get; init; }

    /// <summary>Corner radius in device-independent px.</summary>
    public int BorderRadius { get; set; }

    /// <summary>Uniform border width in px (per-side borders collapse to this in v1).</summary>
    public int BorderWidth { get; set; }

    public int PaddingTop { get; set; }

    public int PaddingRight { get; set; }

    public int PaddingBottom { get; set; }

    public int PaddingLeft { get; set; }

    public int MinWidth { get; set; }

    public int MinHeight { get; set; }

    public GtkStateMetrics Normal { get; } = new();

    public GtkStateMetrics Hover { get; } = new();

    public GtkStateMetrics Active { get; } = new();

    public GtkStateMetrics Checked { get; } = new();

    public GtkStateMetrics Disabled { get; } = new();

    public GtkStateMetrics Focus { get; } = new();

    public GtkStateMetrics Drop { get; } = new();

    public GtkStateMetrics Backdrop { get; } = new();

    /// <summary>Trough color (scale part; GTK themes style it via scale > trough).</summary>
    public GtkColor? TroughColor { get; set; }

    /// <summary>Filled-part color (scale > trough > highlight).</summary>
    public GtkColor? FillColor { get; set; }

    /// <summary>Thumb color (scale > trough > slider).</summary>
    public GtkColor? ThumbColor { get; set; }

    /// <summary>The parsed transitions declared for this control.</summary>
    public Collection<CssTransition> Transitions { get; } = [];

    internal IReadOnlyList<string>? TransitionProperties { get; set; }

    internal IReadOnlyList<double>? TransitionDurations { get; set; }

    internal IReadOnlyList<double>? TransitionDelays { get; set; }

    internal IReadOnlyList<CssTimingFunction>? TransitionTimings { get; set; }

    /// <summary>
    /// Recomputes the transition list from the declared longhands. Longhands
    /// never declared fall back to their CSS initial values (all / 0s / 0s /
    /// ease) — the same values a browser would use.
    /// </summary>
    internal void FlushTransitions()
    {
        Transitions.Clear();
        foreach (CssTransition transition in TransitionParser.CombineLonghands(
            TransitionProperties ?? ["ALL"],
            TransitionDurations ?? [0],
            TransitionDelays ?? [0],
            TransitionTimings ?? [CssTimingFunction.Ease]))
        {
            Transitions.Add(transition);
        }
    }

    internal GtkControlMetrics Clone()
    {
        return new GtkControlMetrics
        {
            Control = Control,
            BorderRadius = BorderRadius,
            BorderWidth = BorderWidth,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            PaddingLeft = PaddingLeft,
            MinWidth = MinWidth,
            MinHeight = MinHeight
        };

        // The clone is only used for the plain-selector gate (state metrics
        // get copied separately); transitions are re-derived from the cascade,
        // so nothing to copy here.
    }
}

/// <summary>
/// The resolved DE theme metrics: canonical Adwaita defaults overridden by the
/// active GTK CSS theme when one is present (GTK themes run on any desktop, so
/// the GTK CSS subset is the portable metrics source; the palette stays the
/// per-DE color source).
/// </summary>
[PublicAPI]
public sealed class GtkThemeMetrics
{
    private static readonly Lazy<GtkThemeMetrics> Adwaita = new(CreateAdwaitaDefaults);

    public required GtkColor WindowBackground { get; set; }

    public required GtkColor ViewBackground { get; set; }

    public required GtkColor TextColor { get; set; }

    public required GtkColor AccentColor { get; set; }

    public required GtkColor DangerColor { get; set; }

    public required GtkColor BorderColor { get; set; }

    /// <summary>Button hover fallback (used when the CSS theme carries no hover color).</summary>
    public required GtkColor ButtonHoverFallback { get; set; }

    /// <summary>Button active fallback (used when the CSS theme carries no active color).</summary>
    public required GtkColor ButtonActiveFallback { get; set; }

    public required Dictionary<string, GtkControlMetrics> Controls { get; init; }

    /// <summary>True when the system prefers dark GTK variants (settings.ini flag or a -Dark theme name).</summary>
    public required bool IsDark { get; set; }

    /// <summary>The GTK font family from settings.ini (null = the WPF default).</summary>
    public string? FontFamily { get; set; }

    /// <summary>The GTK font size in points from settings.ini (null = the WPF default).</summary>
    public double? FontSizePt { get; set; }

    /// <summary>The WPF font size for the GTK point size (pt * 4/3, 1/96-inch units).</summary>
    public double? FontSize => FontSizePt is { } pt ? pt * 4.0 / 3.0 : null;

    /// <summary>Selection fill color (theme_selected_bg_color).</summary>
    public required GtkColor SelectedBackground { get; set; }

    /// <summary>Selection foreground color (theme_selected_fg_color).</summary>
    public required GtkColor SelectedForeground { get; set; }

    /// <summary>Deep copy (control metrics and their state colors are cloned).</summary>
    internal GtkThemeMetrics Clone()
    {
        var controls = new Dictionary<string, GtkControlMetrics>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, GtkControlMetrics control) in Controls)
        {
            GtkControlMetrics copy = control.Clone();
            CopyStates(control, copy);
            controls[key] = copy;
        }

        return new GtkThemeMetrics
        {
            WindowBackground = WindowBackground,
            ViewBackground = ViewBackground,
            TextColor = TextColor,
            AccentColor = AccentColor,
            DangerColor = DangerColor,
            BorderColor = BorderColor,
            IsDark = IsDark,
            FontFamily = FontFamily,
            FontSizePt = FontSizePt,
            SelectedBackground = SelectedBackground,
            SelectedForeground = SelectedForeground,
            ButtonHoverFallback = ButtonHoverFallback,
            ButtonActiveFallback = ButtonActiveFallback,
            Controls = controls
        };
    }

    private static void CopyStates(GtkControlMetrics source, GtkControlMetrics target)
    {
        target.TroughColor = source.TroughColor;
        target.FillColor = source.FillColor;
        target.ThumbColor = source.ThumbColor;

        target.Normal.Background = source.Normal.Background;
        target.Normal.Color = source.Normal.Color;
        target.Normal.BorderColor = source.Normal.BorderColor;
        target.Hover.Background = source.Hover.Background;
        target.Hover.Color = source.Hover.Color;
        target.Hover.BorderColor = source.Hover.BorderColor;
        target.Active.Background = source.Active.Background;
        target.Active.Color = source.Active.Color;
        target.Active.BorderColor = source.Active.BorderColor;
        target.Checked.Background = source.Checked.Background;
        target.Checked.Color = source.Checked.Color;
        target.Checked.BorderColor = source.Checked.BorderColor;
        target.Disabled.Background = source.Disabled.Background;
        target.Disabled.Color = source.Disabled.Color;
        target.Disabled.BorderColor = source.Disabled.BorderColor;
        target.Focus.Background = source.Focus.Background;
        target.Focus.Color = source.Focus.Color;
        target.Focus.BorderColor = source.Focus.BorderColor;
        target.Drop.Background = source.Drop.Background;
        target.Drop.Color = source.Drop.Color;
        target.Drop.BorderColor = source.Drop.BorderColor;
        target.Backdrop.Background = source.Backdrop.Background;
        target.Backdrop.Color = source.Backdrop.Color;
        target.Backdrop.BorderColor = source.Backdrop.BorderColor;
    }

    /// <summary>Canonical Adwaita metrics (the structural baseline every theme starts from).</summary>
    public static GtkThemeMetrics AdwaitaDefault => Adwaita.Value;

    /// <summary>Applies a GTK CSS theme over the Adwaita defaults.</summary>
    public static GtkThemeMetrics FromCss(string css)
    {
        return GtkCssTheme.Apply(AdwaitaDefault, css);
    }

    /// <summary>
    /// Loads the metrics for the active session: the discovered GTK CSS theme
    /// over the Adwaita defaults, or the bare defaults when no theme is found.
    /// </summary>
    public static GtkThemeMetrics Load()
    {
        GtkCssTheme.GtkCssSource source = GtkCssTheme.DiscoverCssSource();
        GtkThemeMetrics result = source.Css is null ? AdwaitaDefault : FromCss(source.Css);
        result.IsDark = source.IsDark;
        result.FontFamily = source.FontFamily;
        result.FontSizePt = source.FontSizePt;
        return result;
    }

    private static GtkThemeMetrics CreateAdwaitaDefaults()
    {
        GtkColor window = GtkColor.Parse("#fafafa")!.Value;
        GtkColor view = GtkColor.Parse("#ffffff")!.Value;
        GtkColor text = GtkColor.Parse("#2e3436")!.Value;
        GtkColor accent = GtkColor.Parse("#3584e4")!.Value;
        GtkColor danger = GtkColor.Parse("#e01b24")!.Value;
        GtkColor border = GtkColor.Parse("#c0c0c0")!.Value;

        static GtkControlMetrics Control(string name, int radius, int borderWidth, int padV, int padH, int minW = 0, int minH = 0)
        {
            return new GtkControlMetrics
            {
                Control = name,
                BorderRadius = radius,
                BorderWidth = borderWidth,
                PaddingTop = padV,
                PaddingBottom = padV,
                PaddingLeft = padH,
                PaddingRight = padH,
                MinWidth = minW,
                MinHeight = minH
            };
        }

        return new GtkThemeMetrics
        {
            WindowBackground = window,
            ViewBackground = view,
            TextColor = text,
            AccentColor = accent,
            DangerColor = danger,
            BorderColor = border,
            IsDark = false,
            FontFamily = null,
            FontSizePt = null,
            SelectedBackground = accent,
            SelectedForeground = GtkColor.Parse("#ffffff")!.Value,
            ButtonHoverFallback = GtkColor.Parse("#ededed")!.Value,
            ButtonActiveFallback = GtkColor.Parse("#dedede")!.Value,
            Controls = new Dictionary<string, GtkControlMetrics>(StringComparer.OrdinalIgnoreCase)
            {
                ["button"] = Control("button", 6, 1, 4, 8, minH: 24),
                ["togglebutton"] = Control("togglebutton", 6, 1, 4, 8, minH: 24),
                ["entry"] = Control("entry", 6, 1, 4, 8, minH: 28),
                ["checkbutton"] = Control("checkbutton", 3, 1, 4, 8),
                ["radiobutton"] = Control("radiobutton", 100, 1, 4, 8),
                ["switch"] = Control("switch", 13, 1, 0, 0, 48, 26),
                ["scrollbar"] = Control("scrollbar", 6, 0, 0, 0, 12, 12),
                ["combobox"] = Control("combobox", 6, 1, 4, 8, minH: 24),
                ["menu"] = Control("menu", 12, 1, 6, 0, minW: 200),
                ["menuitem"] = Control("menuitem", 6, 0, 6, 10, minH: 28),
                ["tooltip"] = Control("tooltip", 8, 1, 4, 8),
                ["headerbar"] = Control("headerbar", 0, 0, 10, 10, minH: 46),
                ["progressbar"] = Control("progressbar", 4, 0, 0, 0, minH: 14),
                ["frame"] = Control("frame", 6, 1, 0, 0),
                ["list"] = Control("list", 6, 0, 0, 0),
                ["treeview"] = Control("treeview", 6, 0, 0, 0),
                ["notebook"] = Control("notebook", 6, 1, 6, 6),
                ["tab"] = Control("tab", 6, 0, 4, 10),
                ["scale"] = Control("scale", 3, 0, 0, 0, 16, 16),
                ["spinbutton"] = Control("spinbutton", 6, 1, 4, 8, minH: 28),
                ["separator"] = Control("separator", 0, 0, 6, 6)
            }
        };
    }
}
