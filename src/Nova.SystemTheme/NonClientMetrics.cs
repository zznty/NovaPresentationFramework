using JetBrains.Annotations;

namespace Nova.SystemTheme;

/// <summary>Win32 <c>NONCLIENTMETRICS</c> fields WPF reads for captions, menus, and system fonts.</summary>
[PublicAPI]
public readonly struct NonClientMetrics(
    int borderWidth,
    int scrollWidth,
    int scrollHeight,
    int captionWidth,
    int captionHeight,
    int smallCaptionWidth,
    int smallCaptionHeight,
    int menuWidth,
    int menuHeight,
    SystemFontMetrics captionFont,
    SystemFontMetrics smallCaptionFont,
    SystemFontMetrics menuFont,
    SystemFontMetrics statusFont,
    SystemFontMetrics messageFont) : IEquatable<NonClientMetrics>
{
    public int BorderWidth { get; } = borderWidth;
    public int ScrollWidth { get; } = scrollWidth;
    public int ScrollHeight { get; } = scrollHeight;
    public int CaptionWidth { get; } = captionWidth;
    public int CaptionHeight { get; } = captionHeight;
    public int SmallCaptionWidth { get; } = smallCaptionWidth;
    public int SmallCaptionHeight { get; } = smallCaptionHeight;
    public int MenuWidth { get; } = menuWidth;
    public int MenuHeight { get; } = menuHeight;
    public SystemFontMetrics CaptionFont { get; } = captionFont;
    public SystemFontMetrics SmallCaptionFont { get; } = smallCaptionFont;
    public SystemFontMetrics MenuFont { get; } = menuFont;
    public SystemFontMetrics StatusFont { get; } = statusFont;
    public SystemFontMetrics MessageFont { get; } = messageFont;

    public bool Equals(NonClientMetrics other)
    {
        return BorderWidth == other.BorderWidth
            && ScrollWidth == other.ScrollWidth
            && ScrollHeight == other.ScrollHeight
            && CaptionWidth == other.CaptionWidth
            && CaptionHeight == other.CaptionHeight
            && SmallCaptionWidth == other.SmallCaptionWidth
            && SmallCaptionHeight == other.SmallCaptionHeight
            && MenuWidth == other.MenuWidth
            && MenuHeight == other.MenuHeight
            && CaptionFont == other.CaptionFont
            && SmallCaptionFont == other.SmallCaptionFont
            && MenuFont == other.MenuFont
            && StatusFont == other.StatusFont
            && MessageFont == other.MessageFont;
    }

    public override bool Equals(object? obj)
    {
        return obj is NonClientMetrics other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            HashCode.Combine(BorderWidth, ScrollWidth, ScrollHeight, CaptionWidth, CaptionHeight),
            HashCode.Combine(SmallCaptionWidth, SmallCaptionHeight, MenuWidth, MenuHeight),
            CaptionFont,
            MenuFont,
            MessageFont);
    }

    public static bool operator ==(NonClientMetrics left, NonClientMetrics right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NonClientMetrics left, NonClientMetrics right)
    {
        return !left.Equals(right);
    }
}
