using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Sdl;

[PublicAPI]
public readonly struct SdlEvent(
    SdlEventKind kind,
    WindowHandle window,
    Point position,
    Vector delta,
    MouseButton mouseButton,
    uint keyScanCode,
    string? text) : IEquatable<SdlEvent>
{
    public SdlEventKind Kind { get; } = kind;
    public WindowHandle Window { get; } = window;
    public Point Position { get; } = position;
    public Vector Delta { get; } = delta;
    public MouseButton MouseButton { get; } = mouseButton;
    public uint KeyScanCode { get; } = keyScanCode;
    public string? Text { get; } = text;

    public static SdlEvent Quit()
    {
        return new SdlEvent(SdlEventKind.Quit, WindowHandle.Invalid, Point.Origin, Vector.Zero, default, 0, null);
    }

    public bool Equals(SdlEvent other)
    {
        return Kind == other.Kind
            && Window == other.Window
            && Position == other.Position
            && Delta == other.Delta
            && MouseButton == other.MouseButton
            && KeyScanCode == other.KeyScanCode
            && Text == other.Text;
    }

    public override bool Equals(object? obj)
    {
        return obj is SdlEvent other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Kind, Window, Position, Delta, MouseButton, KeyScanCode, Text);
    }

    public static bool operator ==(SdlEvent left, SdlEvent right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SdlEvent left, SdlEvent right)
    {
        return !left.Equals(right);
    }
}
