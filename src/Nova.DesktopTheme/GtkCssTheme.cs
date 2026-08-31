using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExCSS;
using JetBrains.Annotations;

namespace Nova.DesktopTheme;

/// <summary>
/// The GTK CSS theme extractor. Discovers the active GTK theme's CSS
/// (gtk-3.0/gtk-4.0 files) and applies it over the Adwaita baseline:
/// selectors map GTK widgets to the WPF control metrics, CSS declarations
/// override the baseline in specificity order. GTK themes run on any desktop,
/// so this is the portable STRUCTURE source; per-DE colors stay in the palette
/// reader.
/// </summary>
[PublicAPI]
public static class GtkCssTheme
{
    private static readonly Regex DefineColor = new(
        @"@define-color\s+(?<name>[\w-]+)\s+(?<value>[^;]+);",
        RegexOptions.Compiled);

    private static readonly Regex ThemeName = new(
        @"^\s*gtk-theme-name\s*=\s*(?<name>\S+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex PreferDark = new(
        @"^\s*gtk-application-prefer-dark-theme\s*=\s*true",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex FontName = new(
        @"^\s*gtk-font-name\s*=\s*(?<family>[^,]+),\s*(?<size>[0-9.]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ImportPattern = new(
        @"@import\s+(?:url\(\s*)?['""](?<path>[^'""]+)['""]",
        RegexOptions.Compiled);

    // ExCSS crashes on GTK border-image shorthands (periodic value converter);
    // the extractor never consumes border-image, so strip those declarations.
    private static readonly Regex BorderImage = new(
        @"border-image\s*:[^;]*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ThemeRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".themes"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "themes"),
        "/usr/local/share/themes",
        "/usr/share/themes"
    ];

    /// <summary>The discovered CSS source: resolved stylesheet text plus the dark-mode flag and the GTK font.</summary>
    internal readonly record struct GtkCssSource(string? Css, bool IsDark, string? FontFamily, double? FontSizePt);

    /// <summary>
    /// Discovers the active GTK CSS: the theme's gtk.css (the dark variant when
    /// the system prefers it) followed by the user override, with @import
    /// directives resolved inline (KDE writes its palette into an imported
    /// colors.css), or null when no GTK theme CSS exists on this system.
    /// </summary>
    public static string? DiscoverCss()
    {
        return DiscoverCssSource().Css;
    }

    internal static GtkCssSource DiscoverCssSource()
    {
        string configRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config");

        string gtk3Settings = Path.Combine(configRoot, "gtk-3.0", "settings.ini");
        string gtk4Settings = Path.Combine(configRoot, "gtk-4.0", "settings.ini");

        string themeName = ReadThemeName(gtk3Settings)
            ?? ReadThemeName(gtk4Settings)
            ?? "Adwaita";
        bool preferDark = ReadPreferDark(gtk3Settings) || ReadPreferDark(gtk4Settings);
        bool isDark = preferDark || themeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        (string? fontFamily, double? fontSizePt) = ReadFontName(gtk3Settings) ?? ReadFontName(gtk4Settings) ?? (null, null);

        // Load BOTH variants: gtk-3.0 first, gtk-4.0 second — the modern
        // metrics (the sizes a GTK4 app shows) win the cascade.
        StringBuilder css = new();
        foreach (string themeCss in FindThemeCssFiles(themeName, preferDark))
        {
            AppendWithImports(css, themeCss);
        }

        string userCss = Path.Combine(configRoot, "gtk-3.0", "gtk.css");
        if (File.Exists(userCss))
        {
            AppendWithImports(css, userCss);
        }

        return new GtkCssSource(css.Length > 0 ? css.ToString() : null, isDark, fontFamily, fontSizePt);
    }

    private static (string? Family, double? SizePt)? ReadFontName(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        Match match = FontName.Match(File.ReadAllText(settingsPath));
        return match.Success &&
               double.TryParse(match.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
            ? (match.Groups["family"].Value.Trim(), size)
            : null;
    }

    private static string? ReadThemeName(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        Match match = ThemeName.Match(File.ReadAllText(settingsPath));
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static bool ReadPreferDark(string settingsPath)
    {
        return File.Exists(settingsPath) && PreferDark.IsMatch(File.ReadAllText(settingsPath));
    }

    /// <summary>
    /// Appends a stylesheet, resolving its @import directives inline (relative
    /// paths, depth-capped). Non-file imports (resource://, http) are skipped.
    /// </summary>
    internal static void AppendWithImports(StringBuilder css, string path, int depth = 0)
    {
        if (depth > 8)
        {
            return;
        }

        string text = File.ReadAllText(path);
        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        foreach (Match match in ImportPattern.Matches(text))
        {
            string target = match.Groups["path"].Value;
            if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("resource", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string resolved = Path.Combine(dir, target);
            if (File.Exists(resolved))
            {
                AppendWithImports(css, resolved, depth + 1);
            }
        }

        _ = css.Append('\n').Append(text);
    }

    private static List<string> FindThemeCssFiles(string themeName, bool preferDark)
    {
        // When dark is preferred and the name is not already dark, the -Dark
        // sibling theme (the GTK convention KDE also follows) takes priority.
        bool darkName = themeName.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
        string[] names = preferDark && !darkName ? [themeName + "-Dark", themeName + "-dark", themeName] : [themeName];
        string[] candidates = preferDark
            ? ["gtk-3.0/gtk-dark.css", "gtk-dark.css", "gtk-3.0/gtk.css", "gtk-4.0/gtk.css", "gtk.css"]
            : ["gtk-3.0/gtk.css", "gtk-4.0/gtk.css", "gtk.css"];

        var found = new List<string>();
        foreach (string root in ThemeRoots)
        {
            foreach (string name in names)
            {
                string themeDir = Path.Combine(root, name);
                if (!Directory.Exists(themeDir))
                {
                    continue;
                }

                foreach (string candidate in candidates)
                {
                    string path = Path.Combine(themeDir, candidate);
                    if (File.Exists(path) && !found.Contains(path, StringComparer.Ordinal))
                    {
                        found.Add(path);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Applies a GTK CSS stylesheet over the Adwaita baseline metrics.</summary>
    public static GtkThemeMetrics Apply(GtkThemeMetrics baseline, string css)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(css);
        css = SanitizeForParse(css);
        Dictionary<string, GtkColor> defines = ResolveDefineColors(css);

        // The theme CSS is untrusted input; ExCSS still trips over some GTK
        // constructs (e.g. periodic values in real Adwaita-derived themes), so
        // a parse failure degrades to the unmodified baseline.
        Stylesheet sheet;
        try
        {
            var parser = new StylesheetParser(
                includeUnknownDeclarations: true,
                includeUnknownRules: true,
                tolerateInvalidValues: true,
                tolerateInvalidSelectors: true,
                tolerateInvalidConstraints: true);
            sheet = parser.Parse(css);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException
                                     or FormatException
                                     or InvalidOperationException
                                     or ArgumentException
                                     or KeyNotFoundException)
        {
            return baseline;
        }

        GtkThemeMetrics result = baseline.Clone();
        var applications = new List<Application>();
        int order = 0;
        foreach (IStyleRule rule in sheet.StyleRules)
        {
            foreach (SelectorMatch match in EnumerateSelectorMatches(rule.SelectorText))
            {
                foreach (Property declaration in rule.Style.Declarations)
                {
                    if (string.IsNullOrEmpty(declaration.Value))
                    {
                        continue;
                    }

                    applications.Add(new Application(
                        match.Ids,
                        match.Classes,
                        match.Tags,
                        order,
                        match.Control,
                        match.State,
                        match.Plain,
                        match.LastCompoundPlain,
                        match.Part,
                        declaration.Name,
                        declaration.Value.Trim()));
                }

                order++;
            }
        }

        applications.Sort(static (a, b) =>
        {
            int byIds = a.Ids.CompareTo(b.Ids);
            if (byIds != 0)
            {
                return byIds;
            }

            int byClasses = a.Classes.CompareTo(b.Classes);
            if (byClasses != 0)
            {
                return byClasses;
            }

            int byTags = a.Tags.CompareTo(b.Tags);
            return byTags != 0 ? byTags : a.Order.CompareTo(b.Order);
        });

        foreach (Application application in applications)
        {
            ApplyProperty(result, application, defines);
        }

        // The surfaces may never be styled by selectors (GTK themes address the
        // window through the .background class and the standard color names);
        // fall back to the theme's canonical define names.
        if (result.WindowBackground == baseline.WindowBackground && defines.TryGetValue("theme_bg_color", out GtkColor bg))
        {
            result.WindowBackground = bg;
        }

        if (result.ViewBackground == baseline.ViewBackground && defines.TryGetValue("theme_base_color", out GtkColor view))
        {
            result.ViewBackground = view;
        }

        if (result.TextColor == baseline.TextColor && defines.TryGetValue("theme_fg_color", out GtkColor fg))
        {
            result.TextColor = fg;
        }

        // The theme's canonical border color (GTK themes expose it as the
        // "borders" define); the Adwaita default is light gray and wrong on
        // dark themes.
        if (result.BorderColor == baseline.BorderColor && defines.TryGetValue("borders", out GtkColor borders))
        {
            result.BorderColor = borders;
        }

        // Selection colors drive the check/radio indicator fill and dot; the
        // accent prefers the theme's accent define, else the selection color.
        if (defines.TryGetValue("accent_bg_color", out GtkColor accent))
        {
            result.AccentColor = accent;
        }
        else if (defines.TryGetValue("theme_selected_bg_color", out GtkColor accentFromSelection))
        {
            result.AccentColor = accentFromSelection;
        }

        if (defines.TryGetValue("theme_selected_bg_color", out GtkColor selectedBg))
        {
            result.SelectedBackground = selectedBg;
        }

        if (defines.TryGetValue("theme_selected_fg_color", out GtkColor selectedFg))
        {
            result.SelectedForeground = selectedFg;
        }

        return result;
    }

    /// <summary>Strips constructs that crash ExCSS and that the extractor never consumes.</summary>
    internal static string SanitizeForParse(string css)
    {
        return BorderImage.Replace(css, string.Empty);
    }

    /// <summary>Resolves @define-color pairs (recursively, to a fixed point).</summary>
    internal static Dictionary<string, GtkColor> ResolveDefineColors(string css)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DefineColor.Matches(css))
        {
            raw[match.Groups["name"].Value] = match.Groups["value"].Value.Trim();
        }

        var resolved = new Dictionary<string, GtkColor>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string definition) in raw)
        {
            if (ResolveColor(definition, raw, resolved, depth: 0) is { } color)
            {
                resolved[name] = color;
            }
        }

        return resolved;
    }

    private static GtkColor? ResolveColor(
        string value,
        IReadOnlyDictionary<string, string> raw,
        IDictionary<string, GtkColor> resolved,
        int depth)
    {
        if (depth > 16)
        {
            return null;
        }

        if (raw.TryGetValue(value, out string? referenced))
        {
            GtkColor? referencedColor = ResolveColor(referenced, raw, resolved, depth + 1);
            resolved[value] = referencedColor ?? default;
            return referencedColor;
        }

        return ResolveValue(value, resolved.AsReadOnly());
    }

    /// <summary>Resolves a CSS color value against already-resolved defines; currentColor
    /// resolves to the control's own foreground.</summary>
    private static GtkColor? ResolveValue(string value, IReadOnlyDictionary<string, GtkColor> defines, GtkColor? currentColor = null)
    {
        string v = value.Trim();
        return v.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            ? currentColor
            : v.StartsWith('@')
            ? (defines.TryGetValue(v[1..], out GtkColor defined) ? defined : null)
            : GtkColor.Parse(v) is { } direct
                ? direct
                : !TryParseFunction(v, out string name, out string[] args)
            ? null
            : name switch
            {
                "ALPHA" when args.Length == 2 &&
                               ResolveValue(args[0], defines, currentColor) is { } alphaColor &&
                               TryFactor(args[1], out double alphaFactor) =>
                    GtkColor.Alpha(alphaColor, alphaFactor),
                "SHADE" when args.Length == 2 &&
                              ResolveValue(args[0], defines, currentColor) is { } shadeColor &&
                              TryFactor(args[1], out double shadeFactor) =>
                    GtkColor.Shade(shadeColor, shadeFactor),
                "LIGHTER" when args.Length == 1 &&
                                ResolveValue(args[0], defines, currentColor) is { } lighterColor =>
                    GtkColor.Shade(lighterColor, 1.3),
                "DARKER" when args.Length == 1 &&
                               ResolveValue(args[0], defines, currentColor) is { } darkerColor =>
                    GtkColor.Shade(darkerColor, 0.7),
                "MIX" when args.Length == 3 &&
                            ResolveValue(args[0], defines, currentColor) is { } mixFirst &&
                            ResolveValue(args[1], defines, currentColor) is { } mixSecond &&
                            TryFactor(args[2], out double mixFactor) =>
                    GtkColor.Mix(mixFirst, mixSecond, mixFactor),
                _ => null
            };
    }

    private static bool TryFactor(string text, out double factor)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out factor);
    }

    private static bool TryParseFunction(string value, out string name, out string[] args)
    {
        name = string.Empty;
        args = [];
        int open = value.IndexOf('(', StringComparison.Ordinal);
        if (open < 0 || !value.EndsWith(')'))
        {
            return false;
        }

        name = value[..open].Trim().ToUpperInvariant();
        string argText = value[(open + 1)..^1];
        args = argText.Split(',', StringSplitOptions.TrimEntries);
        return true;
    }

    private readonly record struct Application(
        int Ids,
        int Classes,
        int Tags,
        int Order,
        string? Control,
        string? State,
        bool Plain,
        bool LastCompoundPlain,
        string? Part,
        string Property,
        string Value);

    private readonly record struct SelectorMatch(int Ids, int Classes, int Tags, string? Control, string? State, bool Plain, bool LastCompoundPlain, string? Part);

    private static IEnumerable<SelectorMatch> EnumerateSelectorMatches(string selectorText)
    {
        foreach (string complexText in SplitTopLevel(selectorText, ','))
        {
            List<string> compounds = SplitCompounds(complexText);
            if (compounds.Count == 0)
            {
                continue;
            }

            int ids = 0;
            int classes = 0;
            int tags = 0;
            int lastCompoundClasses = 0;
            bool lastCompoundLinking = false;
            string? control = null;
            string? state = null;
            string? part = null;
            string? ancestorElement = null;

            for (int i = 0; i < compounds.Count; i++)
            {
                string compound = compounds[i];
                string element = string.Empty;
                int pos = 0;
                while (pos < compound.Length)
                {
                    char c = compound[pos];
                    if (c == '.')
                    {
                        int start = ++pos;
                        while (pos < compound.Length && IsNameChar(compound[pos]))
                        {
                            pos++;
                        }

                        classes++;
                        if (i == compounds.Count - 1 && start < pos)
                        {
                            string className = compound[start..pos];
                            state ??= className switch
                            {
                                "suggested-action" or "destructive-action" => className,
                                _ => state
                            };
                        }

                        continue;
                    }

                    if (c == '#')
                    {
                        int start = ++pos;
                        while (pos < compound.Length && IsNameChar(compound[pos]))
                        {
                            pos++;
                        }

                        ids++;
                        continue;
                    }

                    if (c == ':')
                    {
                        pos++;
                        int start = pos;
                        int parens = 0;
                        while (pos < compound.Length && (IsNameChar(compound[pos]) || (compound[pos] == '(' && ++parens > 0) || (parens > 0 && compound[pos] == ')' && --parens >= 0)))
                        {
                            pos++;
                        }

                        string pseudo = compound[start..pos];
                        if (i == compounds.Count - 1 && IsStatePseudo(pseudo))
                        {
                            state = pseudo;
                        }

                        if (i == compounds.Count - 1 && IsLinkingPseudo(pseudo))
                        {
                            lastCompoundLinking = true;
                        }
                        else
                        {
                            classes++;
                        }
                        continue;
                    }

                    if (IsNameChar(c))
                    {
                        int start = pos;
                        while (pos < compound.Length && IsNameChar(compound[pos]))
                        {
                            pos++;
                        }

                        element = compound[start..pos];
                        tags++;
                        continue;
                    }

                    pos++;
                }

                if (i == compounds.Count - 1)
                {
                    lastCompoundClasses = ids + classes;
                }

                if (i == compounds.Count - 1 && control is null)
                {
                    // Widget parts (scale > trough/highlight/slider) attach to
                    // their ancestor control.
                    control = element is "trough" or "highlight" or "slider"
                        ? (ancestorElement == "scale" ? "scale" : null)
                        : ResolveControl(element, compounds);
                    part = element is "trough" or "highlight" or "slider" ? element : null;
                }

                if (i < compounds.Count - 1 && element.Length > 0)
                {
                    ancestorElement = element;
                }
            }

            if (control is not null)
            {
                yield return new SelectorMatch(ids, classes, tags, control, state, compounds.Count == 1, lastCompoundClasses == 0 && !lastCompoundLinking, part);
            }
        }
    }

    private static string? ResolveControl(string element, List<string> compounds)
    {
        if (element.Length > 0)
        {
            return element switch
            {
                "button" or "entry" or "checkbutton" or "radiobutton" or "switch" or "scrollbar"
                    or "combobox" or "menu" or "menuitem" or "tooltip" or "headerbar" or "titlebar"
                    or "progressbar" or "frame" or "list" or "treeview" or "notebook" or "scale"
                    or "spinbutton" or "separator" or "label" or "window" or "view" => element,
                "popover" => "menu",
                _ => null
            };
        }

        // Class-only selectors must match EXACTLY: .background is the GTK window
        // surface, but .background.csd / .background:backdrop variants style other
        // aspects and must not hijack the window color.
        if (compounds.Count == 1)
        {
            string compound = compounds[0];
            if (compound == ".background")
            {
                return "window";
            }

            if (compound is ".suggested-action" or ".destructive-action")
            {
                return "button";
            }

            if (compound == ".titlebar")
            {
                return "headerbar";
            }

            if (compound == ".body")
            {
                return "body";
            }
        }

        return null;
    }

    private static bool IsStatePseudo(string pseudo)
    {
        return pseudo is "hover" or "active" or "checked" or "disabled" or "backdrop" or "focus" or "selected" or "drop(active)" or "drop";
    }

    /// <summary>Context/linking pseudo-classes mark structural variants (linked entries,
    /// first/last children) whose geometry must not override the base control.</summary>
    private static bool IsLinkingPseudo(string pseudo)
    {
        return pseudo == "not" || pseudo.Contains("child", StringComparison.Ordinal) || pseudo.Contains("sibling", StringComparison.Ordinal);
    }

    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '-' or '_';
    }

    private static List<string> SplitCompounds(string complexSelector)
    {
        var compounds = new List<string>();
        var current = new StringBuilder();
        int parens = 0;
        foreach (char c in complexSelector.Trim())
        {
            if (c == '(')
            {
                parens++;
                _ = current.Append(c);
                continue;
            }

            if (c == ')')
            {
                parens = Math.Max(0, parens - 1);
                _ = current.Append(c);
                continue;
            }

            if (parens == 0 && c is ' ' or '>' or '+' or '~')
            {
                if (current.Length > 0)
                {
                    compounds.Add(current.ToString());
                    _ = current.Clear();
                }

                continue;
            }

            _ = current.Append(c);
        }

        if (current.Length > 0)
        {
            compounds.Add(current.ToString());
        }

        return compounds;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var current = new StringBuilder();
        int parens = 0;
        foreach (char c in text)
        {
            if (c == '(')
            {
                parens++;
            }
            else if (c == ')')
            {
                parens = Math.Max(0, parens - 1);
            }
            else if (c == separator && parens == 0)
            {
                yield return current.ToString();
                _ = current.Clear();
                continue;
            }

            _ = current.Append(c);
        }

        yield return current.ToString();
    }

    private static void ApplyProperty(
        GtkThemeMetrics theme,
        Application application,
        IReadOnlyDictionary<string, GtkColor> defines)
    {
        string property = application.Property.ToUpperInvariant();
        string value = application.Value;

        switch (application.Control)
        {
            case "window" when property is "BACKGROUND-COLOR" or "BACKGROUND" &&
                                application.State is null && application.Classes <= 1 && application.Plain:
                if (ResolveValue(value, defines) is { } windowColor)
                {
                    theme.WindowBackground = windowColor;
                }

                return;

            case "view" when property is "BACKGROUND-COLOR" or "BACKGROUND" &&
                              application.State is null && application.Classes == 0 && application.Plain:
                if (ResolveValue(value, defines) is { } viewColor)
                {
                    theme.ViewBackground = viewColor;
                }

                return;

            case "body" when property == "FONT-SIZE":
                if (ParseFontSize(value) is { } bodySize)
                {
                    theme.FontSizePt = bodySize;
                }

                return;

            case "label" when property == "COLOR" &&
                                application.State is null && application.Classes == 0 && application.Plain:
                if (ResolveValue(value, defines) is { } textColor)
                {
                    theme.TextColor = textColor;
                }

                return;

            default:
                break;
        }

        if (!theme.Controls.TryGetValue(application.Control!, out GtkControlMetrics? metrics))
        {
            return;
        }

        GtkStateMetrics state = StateFor(metrics, application.State);
        GtkColor currentColor = metrics.Normal.Color ?? theme.TextColor;
        bool plainStructure = application.Plain && application.Classes == 0 && application.Ids == 0;
        switch (property)
        {
            case "scale" when application.Part == "trough" && property is "BACKGROUND-COLOR" or "BACKGROUND":
                if (ResolveValue(value, defines, currentColor) is { } troughColor)
                {
                    metrics.TroughColor = troughColor;
                }

                return;

            case "scale" when application.Part == "highlight" && property is "BACKGROUND-IMAGE" or "BACKGROUND-COLOR":
                if (ResolveImageColor(value, defines, currentColor) is { } fillColor)
                {
                    metrics.FillColor = fillColor;
                }

                return;

            case "scale" when application.Part == "slider" && property is "COLOR" or "BACKGROUND-COLOR":
                if (ResolveValue(value, defines, currentColor) is { } thumbColor)
                {
                    metrics.ThumbColor = thumbColor;
                }

                return;

            case "TROUGH" or "HIGHLIGHT" or "SLIDER":
                return;

            case "TRANSITION":
                metrics.Transitions.Clear();
                foreach (CssTransition transition in TransitionParser.ParseShorthand(value))
                {
                    metrics.Transitions.Add(transition);
                }

                return;

            case "TRANSITION-PROPERTY":
                metrics.TransitionProperties = TransitionParser.ParsePropertyList(value);
                metrics.FlushTransitions();
                return;

            case "TRANSITION-DURATION":
                metrics.TransitionDurations = TransitionParser.ParseTimeList(value);
                metrics.FlushTransitions();
                return;

            case "TRANSITION-DELAY":
                metrics.TransitionDelays = TransitionParser.ParseTimeList(value);
                metrics.FlushTransitions();
                return;

            case "TRANSITION-TIMING-FUNCTION":
                metrics.TransitionTimings = TransitionParser.ParseTimingList(value);
                metrics.FlushTransitions();
                return;
            case "BORDER-RADIUS" when plainStructure:
            case "BORDER-TOP-LEFT-RADIUS" when plainStructure:
            case "BORDER-TOP-RIGHT-RADIUS" when plainStructure:
            case "BORDER-BOTTOM-LEFT-RADIUS" when plainStructure:
            case "BORDER-BOTTOM-RIGHT-RADIUS" when plainStructure:
                if (ParseLength(FirstToken(value)) is { } radius)
                {
                    metrics.BorderRadius = radius;
                }

                return;

            case "BORDER-WIDTH" when plainStructure:
            case "BORDER-TOP-WIDTH" when plainStructure:
            case "BORDER-RIGHT-WIDTH" when plainStructure:
            case "BORDER-BOTTOM-WIDTH" when plainStructure:
            case "BORDER-LEFT-WIDTH" when plainStructure:
                if (ParseLength(FirstToken(value)) is { } borderWidth)
                {
                    metrics.BorderWidth = borderWidth;
                }

                return;

            case "BORDER" when plainStructure:
                ApplyBorderShorthand(metrics, state, value, defines);
                return;

            case "BORDER-COLOR":
            case "BORDER-TOP-COLOR":
            case "BORDER-RIGHT-COLOR":
            case "BORDER-BOTTOM-COLOR":
            case "BORDER-LEFT-COLOR":
                // Try the whole value first: rgba()/rgb() contain spaces and
                // FirstToken would cut them. The first token remains the
                // fallback for the 4-side border-color shorthand.
                if ((ResolveValue(value, defines, currentColor) ??
                     ResolveValue(FirstToken(value), defines, currentColor)) is { } borderColor)
                {
                    state.BorderColor = borderColor;
                }

                return;

            case "PADDING" when plainStructure:
                ApplyPadding(metrics, value);
                return;

            case "PADDING-TOP" when plainStructure:
                if (ParseLength(value) is { } padTop)
                {
                    metrics.PaddingTop = padTop;
                }

                return;

            case "PADDING-RIGHT" when plainStructure:
                if (ParseLength(value) is { } padRight)
                {
                    metrics.PaddingRight = padRight;
                }

                return;

            case "PADDING-BOTTOM" when plainStructure:
                if (ParseLength(value) is { } padBottom)
                {
                    metrics.PaddingBottom = padBottom;
                }

                return;

            case "PADDING-LEFT" when plainStructure:
                if (ParseLength(value) is { } padLeft)
                {
                    metrics.PaddingLeft = padLeft;
                }

                return;

            case "MIN-WIDTH" when plainStructure:
                if (ParseLength(value) is { } minWidth)
                {
                    metrics.MinWidth = minWidth;
                }

                return;

            case "MIN-HEIGHT" when plainStructure:
                if (ParseLength(value) is { } minHeight)
                {
                    metrics.MinHeight = minHeight;
                }

                return;

            case "BACKGROUND-COLOR":
            case "BACKGROUND":
                if (ResolveValue(value, defines, currentColor) is { } backgroundColor)
                {
                    state.Background = backgroundColor;
                }

                return;

            case "COLOR":
                if (ResolveValue(value, defines, currentColor) is { } foregroundColor)
                {
                    state.Color = foregroundColor;
                }

                return;

            default:
                return;
        }
    }

    private static GtkStateMetrics StateFor(GtkControlMetrics metrics, string? state)
    {
        return state switch
        {
            "hover" => metrics.Hover,
            "active" => metrics.Active,
            "checked" => metrics.Checked,
            "disabled" => metrics.Disabled,
            "backdrop" => metrics.Backdrop,
            "selected" => metrics.Checked,
            "focus" => metrics.Focus,
            "drop(active)" or "drop" => metrics.Drop,
            _ => metrics.Normal
        };
    }

    private static void ApplyBorderShorthand(
        GtkControlMetrics metrics,
        GtkStateMetrics state,
        string value,
        IReadOnlyDictionary<string, GtkColor> defines)
    {
        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseLength(token) is { } width)
            {
                metrics.BorderWidth = width;
                continue;
            }

            if (ResolveValue(token, defines) is { } color)
            {
                state.BorderColor = color;
            }
        }
    }

    private static void ApplyPadding(GtkControlMetrics metrics, string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int[] lengths = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (ParseLength(parts[i]) is not { } length)
            {
                return;
            }

            lengths[i] = length;
        }

        switch (lengths.Length)
        {
            case 1:
                metrics.PaddingTop = metrics.PaddingRight = metrics.PaddingBottom = metrics.PaddingLeft = lengths[0];
                return;

            case 2:
                metrics.PaddingTop = metrics.PaddingBottom = lengths[0];
                metrics.PaddingLeft = metrics.PaddingRight = lengths[1];
                return;

            case 3:
                metrics.PaddingTop = lengths[0];
                metrics.PaddingLeft = metrics.PaddingRight = lengths[1];
                metrics.PaddingBottom = lengths[2];
                return;

            case 4:
                metrics.PaddingTop = lengths[0];
                metrics.PaddingRight = lengths[1];
                metrics.PaddingBottom = lengths[2];
                metrics.PaddingLeft = lengths[3];
                return;

            default:
                return;
        }
    }

    private static GtkColor? ResolveImageColor(string value, IReadOnlyDictionary<string, GtkColor> defines, GtkColor? currentColor)
    {
        return TryParseFunction(value, out string name, out string[] args) &&
               name == "IMAGE" &&
               args.Length == 1
            ? ResolveValue(args[0], defines, currentColor)
            : ResolveValue(value, defines, currentColor);
    }

    private static double? ParseFontSize(string value)
    {
        string v = value.Trim();
        return v.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
               double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double pt)
            ? pt
            : v.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
              double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double px)
                ? px * 3.0 / 4.0
                : null;
    }

    private static string FirstToken(string value)
    {
        int space = value.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? value : value[..space];
    }

    private static int? ParseLength(string value)
    {
        string v = value.Trim();
        return v == "0"
            ? 0
            : v.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
              int.TryParse(v[..^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int px)
                ? px
                : null;
    }
}
