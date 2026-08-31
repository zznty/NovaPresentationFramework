using JetBrains.Annotations;

namespace Nova.Pts;

/// <summary>
/// Thrown by PTS entry points whose feature is not implemented in Nova.Pts (tables, floaters,
/// figures, finite/page breaking, multi-column, footnotes, optimal breaking). The WPF host
/// surfaces this as a formatting failure (<see cref="PtsErr.CallbackException"/>), never as a
/// silently wrong layout.
/// </summary>
[PublicAPI]
public sealed class PtsException : NotSupportedException
{
    public PtsException()
        : this("an unsupported feature")
    {
    }

    public PtsException(string feature)
        : base($"Nova.Pts does not implement {feature} yet.")
    {
        Feature = feature;
    }

    public PtsException(string feature, Exception innerException)
        : base($"Nova.Pts does not implement {feature} yet.", innerException)
    {
        Feature = feature;
    }

    /// <summary>The unimplemented PTS feature name.</summary>
    public string Feature { get; }
}
