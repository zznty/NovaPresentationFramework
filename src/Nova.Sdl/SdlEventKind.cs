using JetBrains.Annotations;

namespace Nova.Sdl;

[PublicAPI]
public enum SdlEventKind
{
    Quit,
    WindowCloseRequested,
    WindowMaximized,
    WindowMinimized,
    WindowRestored,
    WindowResized,
    WindowMoved,
    WindowFocusGained,
    WindowFocusLost,
    WindowExposed,
    WindowDisplayChanged,
    MouseMoved,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp,
    TextInput,
    DropBegin,
    DropFile,
    DropText,
    DropComplete,
    DropPosition
}
