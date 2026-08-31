using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nova.DesktopTheme.Host;

/// <summary>
/// Generates the state-transition storyboards from the parsed GTK CSS
/// transitions and attaches them to the templates' state triggers.
///
/// The templates stay declarative: a state trigger carries setters naming the
/// target element and the target color resource. This class walks the
/// templates while they are still unsealed and, for every color setter backed
/// by a parsed transition (duration + cubic-bezier), adds the enter/exit
/// storyboards animating to the state color and back to the resting color.
/// </summary>
internal static class TransitionAnimations
{
    /// <summary>Maps a WPF brush property to the CSS transition property group.</summary>
    private static readonly Dictionary<string, string> PropertyGroups = new(StringComparer.Ordinal)
    {
        ["Background"] = "BACKGROUND-COLOR",
        ["Fill"] = "BACKGROUND-COLOR",
        ["BorderBrush"] = "BORDER-COLOR",
        ["Stroke"] = "BORDER-COLOR",
        ["Foreground"] = "COLOR",
    };

    /// <summary>The resting (non-state) color resource per control and property.</summary>
    private static readonly Dictionary<(string Control, string Property), string> RestingColors = new()
    {
        [("button", "Background")] = "Adwaita.Button.BackgroundColor",
        [("button", "BorderBrush")] = "Adwaita.Button.BorderColor",
        [("button", "Foreground")] = "Adwaita.Button.ForegroundColor",
        [("togglebutton", "Background")] = "Adwaita.Button.BackgroundColor",
        [("togglebutton", "BorderBrush")] = "Adwaita.Button.BorderColor",
        [("togglebutton", "Foreground")] = "Adwaita.Button.ForegroundColor",
        [("combobox", "Background")] = "Adwaita.Button.BackgroundColor",
        [("checkbox", "Background")] = "Adwaita.Entry.BackgroundColor",
        [("radiobutton", "Fill")] = "Adwaita.Entry.BackgroundColor",
        [("radiobutton", "Stroke")] = "Adwaita.Button.BorderColor",
        [("textbox", "BorderBrush")] = "Adwaita.Button.BorderColor",
        [("comboboxitem", "Background")] = "Adwaita.Entry.BackgroundColor",
        [("comboboxitem", "Foreground")] = "Adwaita.Button.ForegroundColor",
        [("menuitem", "Background")] = "Adwaita.Entry.BackgroundColor",
        [("menuitem", "Foreground")] = "Adwaita.Button.ForegroundColor",
        [("tabitem", "Background")] = "Transparent",
        [("tabitem", "BorderBrush")] = "Transparent",
    };

    /// <summary>
    /// Attaches the generated storyboards to every state trigger in the
    /// templates. <paramref name="resources"/> is the owning dictionary the
    /// metrics were injected into (the target colors live there).
    /// </summary>
    public static void Apply(ResourceDictionary templates, GtkThemeMetrics metrics, ResourceDictionary resources)
    {
        foreach (object? value in templates.Values)
        {
            if (value is not Style { TargetType: { } targetType } style)
            {
                continue;
            }

            string control = ControlKey(targetType);
            if (!metrics.Controls.TryGetValue(control, out GtkControlMetrics? controlMetrics))
            {
                continue;
            }

            foreach (SetterBase setterBase in style.Setters)
            {
                if (setterBase is Setter { Value: ControlTemplate template })
                {
                    AnimateTemplate(template, control, controlMetrics, resources);
                }
            }
        }
    }

    private static void AnimateTemplate(ControlTemplate template, string control, GtkControlMetrics metrics, ResourceDictionary resources)
    {
        foreach (TriggerBase triggerBase in template.Triggers)
        {
            foreach (Setter setter in EnumerateSetters(triggerBase).ToList())
            {
                if (setter.TargetName is null || setter.Value is not DynamicResourceExtension resource)
                {
                    continue;
                }

                AnimateSetter(triggerBase, setter, control, metrics, resources, resource.ResourceKey);
            }
        }
    }

    private static void AnimateSetter(
        TriggerBase trigger,
        Setter setter,
        string control,
        GtkControlMetrics metrics,
        ResourceDictionary resources,
        object? stateResourceKey)
    {
        string propertyName = setter.Property.Name;
        if (!PropertyGroups.TryGetValue(propertyName, out string? group))
        {
            return;
        }


        CssTransition? transition = PickTransition(metrics, group);
        if (transition is not { DurationMs: > 0 } || transition.Timing is not CssCubicBezier bezier)
        {
            return;
        }

        if (!TryResolveColor(resources, stateResourceKey, out System.Windows.Media.Color stateColor))
        {
            return;
        }

        System.Windows.Media.Color resting = RestingColor(resources, control, propertyName);
        string path = $"({setter.Property.OwnerType.Name}.{setter.Property.Name}).(SolidColorBrush.Color)";
        Duration duration = new(TimeSpan.FromMilliseconds(transition.DurationMs));
        BezierEasingFunction easing = new()
        {
            X1 = bezier.X1,
            Y1 = bezier.Y1,
            X2 = bezier.X2,
            Y2 = bezier.Y2,
        };

        System.Windows.Media.Animation.ColorAnimation enter = BuildAnimation(setter.TargetName, path, stateColor, duration, easing);
        System.Windows.Media.Animation.ColorAnimation exit = BuildAnimation(setter.TargetName, path, resting, duration, easing);

        trigger.EnterActions.Add(new BeginStoryboard { Storyboard = new Storyboard { Children = { enter } } });
        trigger.ExitActions.Add(new BeginStoryboard { Storyboard = new Storyboard { Children = { exit } } });

        // The storyboards own the state change now; the templates bind
        // per-element brushes so the ColorAnimation never touches a shared
        // resource.
        _ = (trigger as Trigger)?.Setters.Remove(setter) ?? (trigger as MultiTrigger)?.Setters.Remove(setter);
    }

    private static System.Windows.Media.Animation.ColorAnimation BuildAnimation(
        string targetName,
        string path,
        System.Windows.Media.Color to,
        Duration duration,
        BezierEasingFunction easing)
    {
        System.Windows.Media.Animation.ColorAnimation animation = new()
        {
            To = to,
            Duration = duration,
            EasingFunction = easing,
        };
        Storyboard.SetTargetName(animation, targetName);
        Storyboard.SetTargetProperty(animation, new PropertyPath(path));
        return animation;
    }

    /// <summary>The setters of a trigger (or each of a multi-trigger's setters).</summary>
    private static IEnumerable<Setter> EnumerateSetters(TriggerBase trigger)
    {
        return trigger switch
        {
            Trigger simple => simple.Setters.OfType<Setter>(),
            MultiTrigger multi => multi.Setters.OfType<Setter>(),
            _ => [],
        };
    }

    private static CssTransition? PickTransition(GtkControlMetrics metrics, string group)
    {
        foreach (CssTransition transition in metrics.Transitions)
        {
            if (transition.Property == "NONE")
            {
                continue;
            }

            if (transition.Property == group || transition.Property == "ALL")
            {
                return transition;
            }
        }

        return null;
    }

    private static string ControlKey(Type targetType)
    {
        string key = targetType switch
        {
            _ when targetType == typeof(Button) => "button",
            _ when targetType == typeof(ToggleButton) => "togglebutton",
            _ when targetType == typeof(CheckBox) => "checkbox",
            _ when targetType == typeof(RadioButton) => "radiobutton",
            _ when targetType == typeof(TextBox) => "textbox",
            _ when targetType == typeof(ComboBox) => "combobox",
            _ when targetType == typeof(ComboBoxItem) => "comboboxitem",
            _ when targetType == typeof(MenuItem) => "menuitem",
            _ when targetType == typeof(TabItem) => "tabitem",
            _ => targetType.Name.ToUpperInvariant(),
        };
        return key;
    }

    private static bool TryResolveColor(ResourceDictionary resources, object? key, out System.Windows.Media.Color color)
    {
        color = default;
        if (key is not string resourceKey || !resources.Contains(resourceKey))
        {
            return false;
        }

        // The templates reference the brush resources; the animations animate
        // the brush's color.
        switch (resources[resourceKey])
        {
            case System.Windows.Media.Color resolved:
                color = resolved;
                return true;
            case SolidColorBrush brush:
                color = brush.Color;
                return true;
            default:
                return false;
        }
    }



    private static System.Windows.Media.Color RestingColor(ResourceDictionary resources, string control, string property)
    {
        if (RestingColors.TryGetValue((control, property), out string? key))
        {
            if (key == "Transparent")
            {
                return System.Windows.Media.Colors.Transparent;
            }

            if (resources.Contains(key) && resources[key] is System.Windows.Media.Color color)
            {
                return color;
            }
        }

        return System.Windows.Media.Colors.Transparent;
    }
}
