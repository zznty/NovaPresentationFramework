using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>How a tile brush repeats its base tile. Values match WPF.</summary>
[PublicAPI]
public enum TileMode
{
    None = 0,
    FlipX = 1,
    FlipY = 2,
    FlipXY = 3,
    Tile = 4
}
