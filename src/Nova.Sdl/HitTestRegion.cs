namespace Nova.Sdl;

/// <summary>
/// The hit-test classification a window region resolves to, mirroring SDL_HitTestResult.
/// The values are layout-identical to the SDL enum (and Silk's binding) so the cast to
/// <c>Silk.NET.SDL.HitTestResult</c> is a simple numeric conversion — the SDL order after
/// TOPRIGHT runs RIGHT, BOTTOMRIGHT, BOTTOM, BOTTOMLEFT, LEFT; a left-to-right reading
/// order here would silently mirror the horizontal edges.
/// </summary>
public enum HitTestRegion
{
    Normal = 0,
    Draggable = 1,
    ResizeTopLeft = 2,
    ResizeTop = 3,
    ResizeTopRight = 4,
    ResizeRight = 5,
    ResizeBottomRight = 6,
    ResizeBottom = 7,
    ResizeBottomLeft = 8,
    ResizeLeft = 9,
}
