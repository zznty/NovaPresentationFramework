using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace Nova.DesktopTheme.Host;

/// <summary>
/// The DE-styled theme as a ResourceDictionary. Merging one instance applies
/// the whole theme automatically: the embedded Adwaita control templates plus
/// the structural metrics and colors parsed from the active GTK CSS theme
/// (Adwaita defaults when none is installed).
///
/// <code>
/// &lt;Application.Resources&gt;
///     &lt;ResourceDictionary&gt;
///         &lt;ResourceDictionary.MergedDictionaries&gt;
///             &lt;desktop:GtkThemeDictionary /&gt;
///         &lt;/ResourceDictionary.MergedDictionaries&gt;
///     &lt;/ResourceDictionary&gt;
/// &lt;/Application.Resources&gt;
/// </code>
/// </summary>
public sealed class GtkThemeDictionary : ResourceDictionary, IReadOnlyCollection<object>
{
    int IReadOnlyCollection<object>.Count => Count;

    IEnumerator<object> IEnumerable<object>.GetEnumerator()
    {
        return Keys.Cast<object>().GetEnumerator();
    }

    public GtkThemeDictionary()
    {
        var templates = (ResourceDictionary)XamlReader.Parse(ReadTemplateXaml());
        MergedDictionaries.Add(templates);
        AdwaitaTheme.ApplyMetrics(this, DesktopThemeHost.Metrics);
        // Transition storyboards are generated but not attached: the
        // ColorAnimation on the state brushes leaves the borders in a broken
        // state in this runtime (the exact mechanism is still open). The
        // instant setters guarantee the correct state colors; the generator
        // stays for when the animation path is fixed.
        if (Environment.GetEnvironmentVariable("NOVA-THEME-ANIMATIONS") is not null)
        {
            TransitionAnimations.Apply(templates, DesktopThemeHost.Metrics, this);
        }
    }

    private static string ReadTemplateXaml()
    {
        Assembly assembly = typeof(GtkThemeDictionary).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(
            static name => name.EndsWith("Themes.Adwaita.xaml", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
