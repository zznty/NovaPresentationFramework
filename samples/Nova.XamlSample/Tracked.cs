using System.Windows;

namespace Nova.XamlSample;

/// <summary>
/// Consumer-defined attached property registered through the public
/// <c>DependencyProperty.RegisterAttached</c> API. Referenced from MainWindow.xaml
/// as <c>local:Tracked.Mark</c>, which also exercises the build-time
/// GeneratedInternalTypeHelper path for local types (patches/0011).
/// </summary>
internal static class Tracked
{
    /// <summary>The attached property.</summary>
    public static readonly DependencyProperty MarkProperty =
        DependencyProperty.RegisterAttached(
            "Mark",
            typeof(string),
            typeof(Tracked),
            new PropertyMetadata(null));

    /// <summary>Gets <see cref="MarkProperty"/>.</summary>
    public static string? GetMark(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(MarkProperty);
    }

    /// <summary>Sets <see cref="MarkProperty"/>.</summary>
    public static void SetMark(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(MarkProperty, value);
    }
}
