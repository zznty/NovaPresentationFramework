using JetBrains.Annotations;

namespace Nova.Sdl;

/// <summary>
/// SDL3 popup window kinds. Popups are child windows of a parent window:
/// <c>SDL_WINDOW_TOOLTIP</c> passes no input through, <c>SDL_WINDOW_POPUP_MENU</c>
/// implicitly gains keyboard focus and is dismissed by the compositor on an outside
/// click (xdg_popup on Wayland, override-redirect popup on X11).
/// </summary>
[PublicAPI]
public enum PopupKind
{
    /// <summary>A regular top-level window.</summary>
    None = 0,

    /// <summary><c>SDL_WINDOW_TOOLTIP</c>: a tooltip popup that does not get mouse or keyboard focus.</summary>
    Tooltip = 1,

    /// <summary><c>SDL_WINDOW_POPUP_MENU</c>: a popup menu; the topmost popup menu implicitly gains keyboard focus.</summary>
    PopupMenu = 2
}
