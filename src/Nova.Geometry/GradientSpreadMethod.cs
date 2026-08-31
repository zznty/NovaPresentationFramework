using JetBrains.Annotations;

namespace Nova.Geometry;

/// <summary>How a gradient is extended past its 0..1 parameter range. Values match WPF.</summary>
[PublicAPI]
public enum GradientSpreadMethod
{
    Pad = 0,
    Reflect = 1,
    Repeat = 2
}
