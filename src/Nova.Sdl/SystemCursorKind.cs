namespace Nova.Sdl;

/// <summary>
/// The SDL system cursor kinds a window can display. Mirrors SDL_SystemCursor, but only the
/// cursors the WPF host maps to; <see cref="SdlWindow.SetCursor"/> accepts
/// <see langword="null"/> to hide the cursor.
/// </summary>
public enum SystemCursorKind
{
    Default,
    Text,
    Wait,
    Crosshair,
    Progress,
    ResizeNwse,
    ResizeNesw,
    ResizeEw,
    ResizeNs,
    ResizeN,
    ResizeNe,
    ResizeE,
    ResizeSe,
    ResizeS,
    ResizeSw,
    ResizeW,
    ResizeNw,
    Move,
    NotAllowed,
    Hand,
}
