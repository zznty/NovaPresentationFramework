using System.Windows;
using System.Windows.Media;

namespace Nova.DesktopTheme.Host;

/// <summary>
/// The DE-styled control template set. Loads the embedded Adwaita template
/// dictionary and injects the structural metrics (radius, padding, borders)
/// and theme colors parsed from the active GTK CSS theme — the
/// WPF-equivalent of what Breeze/Adwaita implement in code/CSS.
/// </summary>
public static class AdwaitaTheme
{
    /// <summary>Writes the metric and color resources the templates consume.</summary>
    public static void ApplyMetrics(ResourceDictionary target, GtkThemeMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(metrics);
        GtkControlMetrics button = metrics.Controls["button"];
        GtkControlMetrics entry = metrics.Controls["entry"];
        GtkControlMetrics menu = metrics.Controls["menu"];

        Set(target, "Adwaita.Button.Radius", new CornerRadius(button.BorderRadius));
        // The theme's entry ring comes from the border-image asset (a ~2-3px
        // corner curve, Windows-11-like); the CSS 10px only clips the inner
        // fill. Clamp to the asset's subtle rounding.
        Set(target, "Adwaita.Entry.Radius", new CornerRadius(Math.Min(entry.BorderRadius, 4)));
        Set(target, "Adwaita.Menu.Radius", new CornerRadius(menu.BorderRadius));
        Set(target, "Adwaita.Button.Padding", new Thickness(button.PaddingLeft, button.PaddingTop, button.PaddingRight, button.PaddingBottom));
        // The view is stretched to the viewport by the ScrollViewer and its
        // line is 18.16px at this font size; the TextBox style's
        // VerticalContentAlignment=Center forwards to the view and centers the
        // line. Keep the CSS horizontal padding minus the view's caret inset.
        double entryPadH = Math.Max(0, entry.PaddingLeft - 2);
        Set(target, "Adwaita.Entry.Padding", new Thickness(entryPadH, 0, entryPadH, 0));
        Set(target, "Adwaita.MenuItem.Padding", new Thickness(
            metrics.Controls["menuitem"].PaddingLeft,
            metrics.Controls["menuitem"].PaddingTop,
            metrics.Controls["menuitem"].PaddingRight,
            metrics.Controls["menuitem"].PaddingBottom));

        Set(target, "Adwaita.Button.MinHeight", (double)button.MinHeight);
        Set(target, "Adwaita.Entry.MinHeight", (double)entry.MinHeight);
        Set(target, "Adwaita.MenuItem.MinHeight", (double)metrics.Controls["menuitem"].MinHeight);
        Set(target, "Adwaita.TabItem.Padding", new Thickness(
            metrics.Controls["tab"].PaddingLeft,
            metrics.Controls["tab"].PaddingTop,
            metrics.Controls["tab"].PaddingRight,
            metrics.Controls["tab"].PaddingBottom));
        Set(target, "Adwaita.ScrollBar.Thickness", (double)metrics.Controls["scrollbar"].MinWidth);

        GtkControlMetrics scale = metrics.Controls["scale"];
        if (scale.TroughColor is { } troughColor)
        {
            Set(target, "Adwaita.Scale.Trough", Brush(troughColor));
        }

        Set(target, "Adwaita.Scale.Fill", Brush(FirstColor(scale.FillColor, metrics.SelectedBackground)));
        Set(target, "Adwaita.Scale.Thumb", Brush(FirstColor(scale.ThumbColor, metrics.SelectedBackground)));

        if (metrics.FontSize is { } fontSize)
        {
            Set(target, "Adwaita.FontSize", fontSize);
        }

        if (metrics.FontFamily is { } fontFamily)
        {
            Set(target, "Adwaita.FontFamily", new System.Windows.Media.FontFamily(fontFamily));
        }

        Set(target, "Adwaita.Window.Background", Brush(metrics.WindowBackground));
        Set(target, "Adwaita.View.Background", Brush(metrics.ViewBackground));
        Set(target, "Adwaita.Accent.Background", Brush(metrics.AccentColor));
        Set(target, "Adwaita.Selected.Background", Brush(metrics.SelectedBackground));
        Set(target, "Adwaita.Selected.Foreground", Brush(metrics.SelectedForeground));
        Set(target, "Adwaita.Button.Foreground", Brush(FirstColor(button.Normal.Color, metrics.TextColor)));
        Set(target, "Adwaita.Text.Foreground", Brush(metrics.TextColor));
        SetColor(target, "Adwaita.Button.ForegroundColor", FirstColor(button.Normal.Color, metrics.TextColor));
        SetColor(target, "Adwaita.Selected.BackgroundColor", metrics.SelectedBackground);
        SetColor(target, "Adwaita.Selected.ForegroundColor", metrics.SelectedForeground);
        SetColor(target, "Adwaita.View.BackgroundColor", metrics.ViewBackground);
        SetColor(target, "Adwaita.Accent.BackgroundColor", metrics.AccentColor);
        SetColor(target, "Adwaita.Entry.BackgroundColor", FirstColor(entry.Normal.Background, metrics.ViewBackground));
        Set(target, "Adwaita.Separator.Color", Brush(GtkColor.Alpha(metrics.TextColor, 0.2)));
        Set(target, "Adwaita.Hover.Wash", Brush(GtkColor.Alpha(metrics.TextColor, 0.08)));
        Set(target, "Adwaita.Accent.Foreground", Brush(ToGtk(255, 255, 255)));
        Set(target, "Adwaita.Button.Background", Brush(FirstColor(button.Normal.Background, metrics.WindowBackground)));
        Set(target, "Adwaita.Button.Border", Brush(FirstColor(button.Normal.BorderColor, metrics.BorderColor)));
        GtkColor buttonBackground = FirstColor(button.Normal.Background, metrics.WindowBackground);
        GtkColor buttonBorder = FirstColor(button.Normal.BorderColor, metrics.BorderColor);
        GtkColor buttonHoverBackground = FirstColor(button.Hover.Background, buttonBackground);
        GtkColor buttonActiveBackground = FirstColor(button.Active.Background, buttonBackground);
        GtkColor buttonHoverBorder = FirstColor(button.Hover.BorderColor, buttonBorder);
        Set(target, "Adwaita.Button.BackgroundHover", Brush(buttonHoverBackground));
        Set(target, "Adwaita.Button.BackgroundActive", Brush(buttonActiveBackground));
        SetColor(target, "Adwaita.Button.BackgroundColor", buttonBackground);
        SetColor(target, "Adwaita.Button.BackgroundHoverColor", buttonHoverBackground);
        SetColor(target, "Adwaita.Button.BackgroundActiveColor", buttonActiveBackground);
        SetColor(target, "Adwaita.Button.BorderColor", buttonBorder);
        SetColor(target, "Adwaita.Button.BorderHoverColor", buttonHoverBorder);

        Set(target, "Adwaita.Button.BorderHover", Brush(buttonHoverBorder));
        Set(target, "Adwaita.Entry.Background", Brush(FirstColor(entry.Normal.Background, metrics.ViewBackground)));

        ApplyTransitions(target, button);
    }

    /// <summary>
    /// Injects the animation resources the templates' state storyboards use:
    /// the parsed transition durations, the cubic-bezier control points, and
    /// the per-state target colors.
    /// </summary>
    private static void ApplyTransitions(ResourceDictionary target, GtkControlMetrics control)
    {
        CssTransition? background = PickTransition(control, "ALL", "BACKGROUND-COLOR", "BACKGROUND-IMAGE");
        CssTransition? border = PickTransition(control, "BORDER-COLOR", "BOX-SHADOW", "BORDER");
        double fastMs = background?.DurationMs ?? 75;
        double borderMs = border?.DurationMs ?? 300;
        CssCubicBezier? easing = (background ?? border)?.Timing as CssCubicBezier;

        Set(target, "Adwaita.Animation.FastDuration", new Duration(TimeSpan.FromMilliseconds(fastMs)));
        Set(target, "Adwaita.Animation.BorderDuration", new Duration(TimeSpan.FromMilliseconds(borderMs)));
        Set(target, "Adwaita.Animation.EasingX1", easing?.X1 ?? 0.0);
        Set(target, "Adwaita.Animation.EasingY1", easing?.Y1 ?? 0.0);
        Set(target, "Adwaita.Animation.EasingX2", easing?.X2 ?? 1.0);
        Set(target, "Adwaita.Animation.EasingY2", easing?.Y2 ?? 1.0);
    }

    /// <summary>First transition whose property matches one of the candidates, skipping none.</summary>
    private static CssTransition? PickTransition(GtkControlMetrics control, params string[] candidates)
    {
        foreach (CssTransition transition in control.Transitions)
        {
            if (transition.Property == "NONE")
            {
                continue;
            }

            if (candidates.Contains(transition.Property, StringComparer.Ordinal))
            {
                return transition;
            }
        }

        return null;
    }

    private static void Set(ResourceDictionary target, string key, object value)
    {
        target[key] = value;
    }

    private static void SetColor(ResourceDictionary target, string key, GtkColor color)
    {
        Set(target, key, Color.FromArgb(
            (byte)Math.Round(color.A * 255.0),
            color.R,
            color.G,
            color.B));
    }

    private static SolidColorBrush Brush(GtkColor color)
    {
        // Not frozen: the state brushes are animated by the template
        // storyboards, and WPF cannot animate a frozen Freezable.
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(color.A * 255.0),
            color.R,
            color.G,
            color.B));
    }

    private static GtkColor FirstColor(GtkColor? color, GtkColor fallback)
    {
        // Fully-transparent colors are the GTK "no color for this state"
        // convention (e.g. button:focus { border-color: transparent }) — they
        // must not override the fallback, or the state would animate to
        // invisible.
        return color is { A: > 0 } c ? c : fallback;
    }

    private static GtkColor ToGtk(byte r, byte g, byte b)
    {
        return new GtkColor(r, g, b);
    }
}
