using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>How brush content is scaled into its destination. Values match WPF.</summary>
[PublicAPI]
public enum Stretch
{
    None = 0,
    Fill = 1,
    Uniform = 2,
    UniformToFill = 3
}
