using System.Collections.ObjectModel;

namespace Nova.DesktopTheme.Tests;

public sealed class TransitionParserTests
{
    [Fact]
    public void ParseShorthand_TypicalThemeTransition()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 75ms cubic-bezier(0, 0, 0.2, 1), border 300ms cubic-bezier(0, 0, 0.2, 1), box-shadow 300ms cubic-bezier(0, 0, 0.2, 1)");

        Assert.Equal(3, transitions.Count);
        Assert.Equal(new CssTransition("ALL", 75, 0, new CssCubicBezier(0, 0, 0.2, 1)), transitions[0]);
        Assert.Equal(new CssTransition("BORDER", 300, 0, new CssCubicBezier(0, 0, 0.2, 1)), transitions[1]);
        Assert.Equal(new CssTransition("BOX-SHADOW", 300, 0, new CssCubicBezier(0, 0, 0.2, 1)), transitions[2]);
    }

    [Fact]
    public void ParseShorthand_KeywordEasing_ExpandsToBezier()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand("all 200ms ease-in-out");

        CssTransition transition = Assert.Single(transitions);
        Assert.Equal(200, transition.DurationMs);
        Assert.Equal(new CssCubicBezier(0.42, 0, 0.58, 1), transition.Timing);
    }

    [Fact]
    public void ParseShorthand_DurationAndDelayOrder()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand("all 1s 500ms ease");

        CssTransition transition = Assert.Single(transitions);
        Assert.Equal(1000, transition.DurationMs);
        Assert.Equal(500, transition.DelayMs);
        Assert.Equal(CssTimingFunction.Ease, transition.Timing);
    }

    [Fact]
    public void ParseShorthand_CustomPropertyIdent()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand("background-color 300ms linear");

        CssTransition transition = Assert.Single(transitions);
        Assert.Equal("BACKGROUND-COLOR", transition.Property);
        Assert.Equal(CssTimingFunction.Linear, transition.Timing);
    }

    [Fact]
    public void ParseShorthand_CommaInsideBezier_DoesNotSplit()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 75ms cubic-bezier(0.4, 0, 0.2, 1), color 120ms ease-out");

        Assert.Equal(2, transitions.Count);
        Assert.Equal(new CssCubicBezier(0.4, 0, 0.2, 1), transitions[0].Timing);
        Assert.Equal("COLOR", transitions[1].Property);
        Assert.Equal(CssTimingFunction.EaseOut, transitions[1].Timing);
    }

    [Fact]
    public void ParseShorthand_BezierXCoordinatesClamped()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 100ms cubic-bezier(-1, 2, 3, -4)");

        CssCubicBezier bezier = Assert.IsType<CssCubicBezier>(Assert.Single(transitions).Timing);
        Assert.Equal(0, bezier.X1);
        Assert.Equal(2, bezier.Y1);
        Assert.Equal(1, bezier.X2);
        Assert.Equal(-4, bezier.Y2);
    }

    [Fact]
    public void ParseShorthand_Steps()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 250ms steps(4, jump-none)");

        CssSteps steps = Assert.IsType<CssSteps>(Assert.Single(transitions).Timing);
        Assert.Equal(4, steps.Count);
        Assert.Equal(StepPosition.JumpNone, steps.Position);
    }

    [Fact]
    public void ParseShorthand_StepKeywords()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 100ms step-start, all 100ms step-end");

        Assert.Equal(new CssSteps(1, StepPosition.JumpStart), transitions[0].Timing);
        Assert.Equal(new CssSteps(1, StepPosition.JumpEnd), transitions[1].Timing);
    }

    [Fact]
    public void ParseShorthand_InvalidTiming_FallsBackToEase()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand(
            "all 100ms cubic-bezier(0, 1, 2)");

        CssTransition transition = Assert.Single(transitions);
        Assert.Equal(CssTimingFunction.Ease, transition.Timing);
        Assert.Equal(100, transition.DurationMs);
    }

    [Fact]
    public void ParseShorthand_MissingDuration_IsZero()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.ParseShorthand("all ease");

        CssTransition transition = Assert.Single(transitions);
        Assert.Equal(0, transition.DurationMs);
        Assert.Equal(CssTimingFunction.Ease, transition.Timing);
    }

    [Fact]
    public void ParseTimeList_MixedUnits()
    {
        IReadOnlyList<double> times = TransitionParser.ParseTimeList("75ms, 1s, 0.25s");

        Assert.Equal([75, 1000, 250], times);
    }

    [Fact]
    public void ParseTimeList_InvalidEntry_IsZero()
    {
        IReadOnlyList<double> times = TransitionParser.ParseTimeList("fast");

        Assert.Equal([0], times);
    }

    [Fact]
    public void ParsePropertyList_NoneIsPreserved()
    {
        IReadOnlyList<string> properties = TransitionParser.ParsePropertyList("none, background-color");

        Assert.Equal(["NONE", "BACKGROUND-COLOR"], properties);
    }

    [Fact]
    public void CombineLonghands_RepetitionRule()
    {
        IReadOnlyList<CssTransition> transitions = TransitionParser.CombineLonghands(
            ["ALL", "BORDER-COLOR"],
            [75, 300, 150],
            [0],
            [CssTimingFunction.Ease]);

        Assert.Equal(3, transitions.Count);
        // Shorter property list repeats: ALL, BORDER-COLOR, ALL.
        Assert.Equal("ALL", transitions[0].Property);
        Assert.Equal(75, transitions[0].DurationMs);
        Assert.Equal("BORDER-COLOR", transitions[1].Property);
        Assert.Equal(300, transitions[1].DurationMs);
        Assert.Equal("ALL", transitions[2].Property);
        Assert.Equal(150, transitions[2].DurationMs);
    }

    [Fact]
    public void FromCss_TransitionShorthand_ReachesMetrics()
    {
        const string css = """
            button { transition: all 75ms cubic-bezier(0, 0, 0.2, 1), border-color 300ms ease; }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        Collection<CssTransition> transitions = theme.Controls["button"].Transitions;
        Assert.Equal(2, transitions.Count);
        Assert.Equal("ALL", transitions[0].Property);
        Assert.Equal(75, transitions[0].DurationMs);
        Assert.Equal("BORDER-COLOR", transitions[1].Property);
        Assert.Equal(300, transitions[1].DurationMs);
    }

    [Fact]
    public void FromCss_TransitionLonghands_ReachMetrics()
    {
        const string css = """
            button {
                transition-property: background-color, border-color;
                transition-duration: 75ms, 300ms;
                transition-timing-function: cubic-bezier(0, 0, 0.2, 1);
            }
            """;

        GtkThemeMetrics theme = GtkThemeMetrics.FromCss(css);

        Collection<CssTransition> transitions = theme.Controls["button"].Transitions;
        Assert.Equal(2, transitions.Count);
        Assert.Equal("BACKGROUND-COLOR", transitions[0].Property);
        Assert.Equal(75, transitions[0].DurationMs);
        Assert.Equal(new CssCubicBezier(0, 0, 0.2, 1), transitions[0].Timing);
        Assert.Equal("BORDER-COLOR", transitions[1].Property);
        Assert.Equal(300, transitions[1].DurationMs);
        // The single timing repeats per CSS.
        Assert.Equal(new CssCubicBezier(0, 0, 0.2, 1), transitions[1].Timing);
    }
}
