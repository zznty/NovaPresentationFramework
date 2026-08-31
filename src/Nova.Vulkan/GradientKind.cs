using JetBrains.Annotations;

namespace Nova.Vulkan;

/// <summary>Gradient shape used by the gradient fragment shader.</summary>
[PublicAPI]
public enum GradientKind
{
    Linear = 0,
    Radial = 1
}
