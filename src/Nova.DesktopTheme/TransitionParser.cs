using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using JetBrains.Annotations;

namespace Nova.DesktopTheme;

/// <summary>
/// One parsed CSS transition: the property group, the duration, the delay and
/// the timing function.
/// </summary>
[PublicAPI]
public sealed record CssTransition(string Property, double DurationMs, double DelayMs, CssTimingFunction Timing)
{
    /// <summary>The CSS initial value: all 0s ease 0s.</summary>
    public static CssTransition Initial { get; } = new("ALL", 0, 0, CssTimingFunction.Ease);
}

/// <summary>
/// A proper parser for the CSS <c>transition</c> shorthand and its longhands
/// (<c>transition-property</c>, <c>transition-duration</c>,
/// <c>transition-delay</c>, <c>transition-timing-function</c>).
///
/// Grammar (CSS Transitions Level 1):
/// <c>single-transition = [ none | &lt;single-transition-property&gt; ] || &lt;time&gt; || &lt;easing-function&gt; || &lt;time&gt;</c>
/// where the first &lt;time&gt; is the duration and the second the delay.
/// The timing function grammar:
/// <c>&lt;easing-function&gt; = linear | &lt;cubic-bezier-easing-function&gt; | &lt;step-easing-function&gt;</c>
/// <c>&lt;cubic-bezier-easing-function&gt; = ease | ease-in | ease-out | ease-in-out | cubic-bezier(&lt;number&gt;,...)</c>
/// <c>&lt;step-easing-function&gt; = step-start | step-end | steps(&lt;integer&gt;[, &lt;step-position&gt;]?)</c>
///
/// Invalid constructs fall back to the per-property initial values rather than
/// failing the whole list, matching how CSS handles partial parsing.
/// </summary>
public static class TransitionParser
{
    /// <summary>The property names the timing functions are defined for (the keyword curves).</summary>
    private static readonly Dictionary<string, CssTimingFunction> KeywordCurves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["linear"] = CssTimingFunction.Linear,
        ["ease"] = CssTimingFunction.Ease,
        ["ease-in"] = CssTimingFunction.EaseIn,
        ["ease-out"] = CssTimingFunction.EaseOut,
        ["ease-in-out"] = CssTimingFunction.EaseInOut,
    };

    /// <summary>Parses the transition shorthand: comma-separated single transitions.</summary>
    public static IReadOnlyList<CssTransition> ParseShorthand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<CssTransition> result = [];
        foreach (List<string> group in SplitGroups(value))
        {
            result.Add(ParseSingleTransition(group));
        }

        return result;
    }

    /// <summary>Parses transition-property: comma-separated idents (none/all/custom).</summary>
    public static IReadOnlyList<string> ParsePropertyList(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<string> result = [];
        foreach (List<string> group in SplitGroups(value))
        {
            string property = (group.FirstOrDefault()?.Trim() ?? "all").ToUpperInvariant();
            result.Add(property);
        }

        return result;
    }

    /// <summary>Parses transition-duration or transition-delay: comma-separated CSS times.</summary>
    public static IReadOnlyList<double> ParseTimeList(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<double> result = [];
        foreach (List<string> group in SplitGroups(value))
        {
            result.Add(ParseTime(group.FirstOrDefault()));
        }

        return result;
    }

    /// <summary>Parses transition-timing-function: comma-separated easing functions.</summary>
    public static IReadOnlyList<CssTimingFunction> ParseTimingList(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        List<CssTimingFunction> result = [];
        foreach (List<string> group in SplitGroups(value))
        {
            result.Add(ParseTimingFunction(group.FirstOrDefault()));
        }

        return result;
    }

    /// <summary>
    /// Combines the parsed longhand lists into the transition list, applying the
    /// CSS repetition rule: each missing list repeats its values from the start.
    /// </summary>
    public static IReadOnlyList<CssTransition> CombineLonghands(
        IReadOnlyList<string> properties,
        IReadOnlyList<double> durations,
        IReadOnlyList<double> delays,
        IReadOnlyList<CssTimingFunction> timings)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(durations);
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentNullException.ThrowIfNull(timings);
        int count = Math.Max(properties.Count, Math.Max(durations.Count, Math.Max(delays.Count, timings.Count)));
        List<CssTransition> result = new(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(new CssTransition(
                properties.Count > 0 ? properties[i % properties.Count] : "ALL",
                durations.Count > 0 ? durations[i % durations.Count] : 0,
                delays.Count > 0 ? delays[i % delays.Count] : 0,
                timings.Count > 0 ? timings[i % timings.Count] : CssTimingFunction.Ease));
        }

        return result;
    }

    /// <summary>
    /// Splits the top-level comma-separated groups, respecting the parentheses so
    /// commas inside cubic-bezier(...) and steps(...) do not split.
    /// </summary>
    internal static List<List<string>> SplitGroups(string value)
    {
        List<List<string>> groups = [];
        List<string> current = [];
        int depth = 0;
        int start = 0;

        void FlushToken(int end)
        {
            if (end > start)
            {
                string token = value[start..end];
                if (!string.IsNullOrWhiteSpace(token))
                {
                    current.Add(token);
                }
            }
        }

        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    FlushToken(i);
                    if (current.Count > 0)
                    {
                        groups.Add(current);
                        current = [];
                    }

                    start = i + 1;
                    break;
                case ' ' or '\t' or '\n' when depth == 0:
                    FlushToken(i);
                    start = i + 1;
                    break;
                default:
                    break;
            }
        }

        FlushToken(value.Length);
        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    /// <summary>Parses one single-transition group: [property] [duration] [timing] [delay].</summary>
    private static CssTransition ParseSingleTransition(List<string> tokens)
    {
        string? property = null;
        double? duration = null;
        double? delay = null;
        CssTimingFunction? timing = null;

        foreach (string token in tokens)
        {
            string trimmed = token.Trim();
            if (timing is null && TryParseTimingFunction(trimmed, out CssTimingFunction? parsedTiming))
            {
                timing = parsedTiming;
                continue;
            }

            if (duration is null && TryParseTime(trimmed, out double parsedTime))
            {
                duration = parsedTime;
                continue;
            }

            if (delay is null && TryParseTime(trimmed, out double parsedDelay))
            {
                delay = parsedDelay;
                continue;
            }

            // Remaining bare identifiers are the property (all/none/custom-ident).
            if (property is null && IsIdent(trimmed))
            {
                property = trimmed.ToUpperInvariant();
            }
        }

        return new CssTransition(
            property?.ToUpperInvariant() ?? "ALL",
            duration ?? 0,
            delay ?? 0,
            timing ?? CssTimingFunction.Ease);
    }

    /// <summary>Parses a CSS time: a number followed by "s" or "ms"; invalid input yields the initial 0s.</summary>
    private static double ParseTime(string? value)
    {
        return TryParseTime(value, out double result) ? result : 0;
    }

    private static bool TryParseTime(string? value, out double milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        double factor;
        string number;
        if (trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            factor = 1;
            number = trimmed[..^2];
        }
        else if (trimmed.EndsWith('s'))
        {
            factor = 1000;
            number = trimmed[..^1];
        }
        else
        {
            return false;
        }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return false;
        }

        milliseconds = parsed * factor;
        return true;
    }

    /// <summary>Parses an easing function; invalid input yields the initial value (ease).</summary>
    private static CssTimingFunction ParseTimingFunction(string? value)
    {
        return TryParseTimingFunction(value, out CssTimingFunction? result) ? result : CssTimingFunction.Ease;
    }

    private static bool TryParseTimingFunction(string? value, [NotNullWhen(true)] out CssTimingFunction? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (KeywordCurves.TryGetValue(trimmed, out CssTimingFunction? keyword))
        {
            result = keyword;
            return true;
        }

        if (trimmed.Equals("step-start", StringComparison.OrdinalIgnoreCase))
        {
            result = new CssSteps(1, StepPosition.JumpStart);
            return true;
        }

        if (trimmed.Equals("step-end", StringComparison.OrdinalIgnoreCase))
        {
            result = new CssSteps(1, StepPosition.JumpEnd);
            return true;
        }

        if (TryParseFunction(trimmed, "cubic-bezier", out List<string>? bezierArgs) && bezierArgs!.Count == 4)
        {
            if (TryParseNumbers(bezierArgs!, out double[] numbers) &&
                numbers.All(static n => !double.IsNaN(n) && !double.IsInfinity(n)))
            {
                double x1 = Math.Clamp(numbers[0], 0, 1);
                double x2 = Math.Clamp(numbers[2], 0, 1);
                result = new CssCubicBezier(x1, numbers[1], x2, numbers[3]);
                return true;
            }

            return false;
        }

        if (TryParseFunction(trimmed, "steps", out List<string>? stepArgs) && stepArgs!.Count is 1 or 2)
        {
            if (!int.TryParse(stepArgs[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
            {
                return false;
            }

            StepPosition position = StepPosition.JumpEnd;
            if (stepArgs.Count == 2)
            {
                switch (stepArgs[1].Trim().ToUpperInvariant())
                {
                    case "JUMP-START":
                    case "START":
                        position = StepPosition.JumpStart;
                        break;
                    case "JUMP-END":
                    case "END":
                        position = StepPosition.JumpEnd;
                        break;
                    case "JUMP-NONE":
                        position = StepPosition.JumpNone;
                        break;
                    case "JUMP-BOTH":
                        position = StepPosition.JumpBoth;
                        break;
                    default:
                        return false;
                }
            }

            result = new CssSteps(count, position);
            return true;
        }

        return false;
    }

    /// <summary>Matches name(args) with balanced parentheses; returns the top-level comma-separated args.</summary>
    private static bool TryParseFunction(string value, string name, out List<string>? args)
    {
        args = null;
        int open = value.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !value.EndsWith(')') || !value[..open].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string body = value[(open + 1)..^1].Trim();
        if (body.Length == 0)
        {
            args = [];
            return true;
        }

        args = [];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < body.Length; i++)
        {
            switch (body[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    args.Add(body[start..i].Trim());
                    start = i + 1;
                    break;
                default:
                    break;
            }
        }

        args.Add(body[start..].Trim());
        return true;
    }

    private static bool TryParseNumbers(List<string> tokens, out double[] numbers)
    {
        numbers = new double[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!double.TryParse(tokens[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A bare CSS identifier: starts with an alpha/dash and is not a number.</summary>
    private static bool IsIdent(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        char first = token[0];
        return (char.IsLetter(first) || first is '-' or '_') &&
               token.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_');
    }
}
