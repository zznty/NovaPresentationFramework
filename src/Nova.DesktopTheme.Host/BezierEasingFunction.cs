using System.Windows;
using System.Windows.Media.Animation;

namespace Nova.DesktopTheme.Host;

/// <summary>
/// An easing function implementing the CSS cubic-bezier(x1, y1, x2, y2)
/// curve. The curve parameter is solved for the output with Newton-Raphson
/// (x(t) is monotonically increasing because x1, x2 are clamped to [0, 1]).
/// </summary>
public sealed class BezierEasingFunction : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(
        nameof(X1), typeof(double), typeof(BezierEasingFunction), new PropertyMetadata(0.0));

    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(
        nameof(Y1), typeof(double), typeof(BezierEasingFunction), new PropertyMetadata(0.0));

    public static readonly DependencyProperty X2Property = DependencyProperty.Register(
        nameof(X2), typeof(double), typeof(BezierEasingFunction), new PropertyMetadata(1.0));

    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(
        nameof(Y2), typeof(double), typeof(BezierEasingFunction), new PropertyMetadata(1.0));

    public double X1
    {
        get => (double)GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    public double Y1
    {
        get => (double)GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    public double X2
    {
        get => (double)GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    public double Y2
    {
        get => (double)GetValue(Y2Property);
        set => SetValue(Y2Property, value);
    }

    protected override Freezable CreateInstanceCore()
    {
        return new BezierEasingFunction();
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double target = Math.Clamp(normalizedTime, 0, 1);

        // Solve x(t) = target for t. Newton-Raphson converges in a few
        // iterations for the monotonic x(t).
        double t = target;
        for (int i = 0; i < 8; i++)
        {
            double error = Evaluate(X1, X2, t) - target;
            if (Math.Abs(error) < 1e-7)
            {
                break;
            }

            double slope = Derivative(X1, X2, t);
            if (Math.Abs(slope) < 1e-7)
            {
                break;
            }

            t = Math.Clamp(t - (error / slope), 0, 1);
        }

        return Evaluate(Y1, Y2, t);
    }

    /// <summary>The one-dimensional cubic bezier: 3(1-t)^2·t·a + 3(1-t)·t^2·b + t^3.</summary>
    private static double Evaluate(double a, double b, double t)
    {
        double u = 1 - t;
        return (3 * u * u * t * a) + (3 * u * t * t * b) + (t * t * t);
    }

    /// <summary>The derivative of <see cref="Evaluate"/> with respect to t.</summary>
    private static double Derivative(double a, double b, double t)
    {
        double u = 1 - t;
        return (3 * a * u * (1 - (3 * t))) + (3 * b * t * (2 - (3 * t))) + (3 * t * t);
    }
}
