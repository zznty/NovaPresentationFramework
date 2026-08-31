using JetBrains.Annotations;

namespace Nova.DesktopTheme;

/// <summary>
/// The CSS <c>&lt;easing-function&gt;</c> production: the predefined keyword
/// curves, the cubic-bezier(x1, y1, x2, y2) curve, and the steps() timing
/// function. Keywords are stored as their equivalent cubic bezier so the
/// consumer has exactly one curve representation.
/// </summary>
[PublicAPI]
public abstract record CssTimingFunction
{
    public static CssTimingFunction Linear { get; } = new CssCubicBezier(0, 0, 1, 1);

    public static CssTimingFunction Ease { get; } = new CssCubicBezier(0.25, 0.1, 0.25, 1);

    public static CssTimingFunction EaseIn { get; } = new CssCubicBezier(0.42, 0, 1, 1);

    public static CssTimingFunction EaseOut { get; } = new CssCubicBezier(0, 0, 0.58, 1);

    public static CssTimingFunction EaseInOut { get; } = new CssCubicBezier(0.42, 0, 0.58, 1);
}

/// <summary>cubic-bezier(x1, y1, x2, y2); x coordinates are clamped to [0, 1] per CSS.</summary>
[PublicAPI]
public sealed record CssCubicBezier(double X1, double Y1, double X2, double Y2) : CssTimingFunction;

/// <summary>The position of the jump in a steps() timing function.</summary>
[PublicAPI]
public enum StepPosition
{
    JumpStart,

    JumpEnd,

    JumpNone,

    JumpBoth,
}

/// <summary>steps(count, position); step-start/step-end are keywords for steps(1, ...).</summary>
[PublicAPI]
public sealed record CssSteps(int Count, StepPosition Position) : CssTimingFunction;
