using JetBrains.Annotations;
using Nova.Geometry;

namespace Nova.Sdl;

[PublicAPI]
public sealed class WindowOptions
{
    public string Title { get; init; } = "Nova";

    public PixelSize Size { get; init; } = new(800, 600);

    public bool Hidden { get; init; }

    public bool Resizable { get; init; } = true;

    public bool HighDpi { get; init; } = true;

    public bool Vulkan { get; init; } = true;

    /// <summary>
    /// Requests a window whose pixels are composited with per-pixel alpha
    /// (<c>SDL_WINDOW_TRANSPARENT</c>): fully transparent areas of the frame let the
    /// desktop show through. The Vulkan presenter pairs this with a non-opaque
    /// (premultiplied) swapchain composite-alpha mode. <see langword="false"/> for
    /// ordinary opaque windows.
    /// </summary>
    public bool Transparent { get; init; }

    /// <summary>
    /// Popup kind. <see cref="PopupKind.None"/> creates a regular top-level window;
    /// otherwise the window is created with <c>SDL_CreatePopupWindow</c> relative to
    /// <see cref="Parent"/> at (<see cref="X"/>, <see cref="Y"/>).
    /// </summary>
    public PopupKind Popup { get; init; } = PopupKind.None;

    /// <summary>Parent window for a popup. Ignored for regular windows.</summary>
    public SdlWindow? Parent { get; init; }

    /// <summary>Popup x offset relative to the parent window origin (<c>SDL_CreatePopupWindow</c>).</summary>
    public int X { get; init; }

    /// <summary>Popup y offset relative to the parent window origin (<c>SDL_CreatePopupWindow</c>).</summary>
    public int Y { get; init; }
}
