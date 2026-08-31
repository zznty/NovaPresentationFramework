using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>Whether brush coordinates are absolute or relative to the painted bounds. Values match WPF.</summary>
[PublicAPI]
public enum BrushMappingMode
{
    Absolute = 0,
    RelativeToBoundingBox = 1
}
