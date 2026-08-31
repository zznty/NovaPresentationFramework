namespace Nova.DesktopTheme;

/// <summary>
/// Hand-rolled BCL-only DBus client (session bus, unix socket) for the
/// <c>org.freedesktop.portal.Settings</c> calls this feature needs. Chosen over a NuGet DBus
/// library so stages 1–5 carry zero third-party dependencies (the same reason the INI and
/// <c>@define-color</c> parsers are hand-rolled). Only implements the subset the portal
/// appearance interface requires: hello, <c>Read</c> (a single value) and the
/// <c>SettingChanged</c> signal subscription (live restyle).
/// </summary>
internal static class DbusPortal
{
    /// <summary>
    /// Reads one appearance key via the portal. Returns <c>null</c> when the portal is absent
    /// (no session bus, no service), the call fails, or the value is not the expected type.
    /// Never throws.
    /// </summary>
    public static object? ReadAppearance(string namespaceName, string key)
    {
        try
        {
            return DbusConnection.TryReadAppearance(namespaceName, key);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }
}
