namespace Nova.DesktopTheme;

/// <summary>
/// xdg-desktop-portal <c>org.freedesktop.appearance</c> — the cross-desktop canonical source
/// for the dark/light preference and the real accent color (a float RGB triple; NOT the
/// coarse symbolic name that <c>gsettings accent-color</c> returns on KDE). Tests inject a
/// canned <see cref="Func{T1,T2,TResult}"/>; production wraps the hand-rolled BCL DBus
/// client. The read delegate must not throw; a failing read yields an empty contribution.
/// </summary>
public sealed class PortalAppearanceSource(Func<string, string, object?> read) : IThemeSource
{
    public string Name => "portal";

    public ThemeData Load()
    {
        var data = new ThemeData();
        if (Read("color-scheme") is uint scheme)
        {
            data.IsDark = scheme == 1;
        }

        if (Read("accent-color") is (double r, double g, double b))
        {
            data.AccentColorRef = new RgbColor(ToByte(r), ToByte(g), ToByte(b)).ToColorRef();
        }

        return data;
    }

    private object? Read(string key)
    {
        try
        {
            return read("org.freedesktop.appearance", key);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static byte ToByte(double channel)
    {
        double clamped = Math.Clamp(channel, 0.0, 1.0);
        return (byte)Math.Round(clamped * 255.0, MidpointRounding.AwayFromZero);
    }
}
