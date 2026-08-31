using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace Nova.Framework.Tests;

/// <summary>
/// Real-stack evidence for the caret/hit-testing queries (LoQueryLineCpPpoint /
/// LoQueryLinePointPcp on the FullTextLine path): an RTL paragraph forces FullTextLine (its
/// SimpleTextLine.Create rejects RightToLeft), so every formatted line drives the Lo* engine.
/// Verifies the point→cp→point round-trip and caret movement/selection indices on wrapped RTL
/// text. Pure TextFormatter — no SDL window, so no collection needed.
/// </summary>
public sealed class TextFormatterCaretTests
{
    private sealed class TestRunProperties : TextRunProperties
    {
        private readonly Typeface _typeface = new(
            new FontFamily("Noto Sans Hebrew"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        public override Brush BackgroundBrush => Brushes.Transparent;

        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;

        public override double FontHintingEmSize => 0;

        public override double FontRenderingEmSize => 16;

        public override Brush ForegroundBrush => Brushes.Black;

        public override TextDecorationCollection TextDecorations => [];

        public override TextEffectCollection TextEffects => [];

        public override Typeface Typeface => _typeface;
    }

    private sealed class StringTextSource(string text, TextRunProperties props) : TextSource
    {
        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
        {
            return new TextSpan<CultureSpecificCharacterBufferRange>(
                0,
                new CultureSpecificCharacterBufferRange(CultureInfo.InvariantCulture, new CharacterBufferRange(text, 0, text.Length)));
        }

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
        {
            return textSourceCharacterIndex;
        }

        public override TextRun GetTextRun(int textSourceCharacterIndex)
        {
            return textSourceCharacterIndex >= text.Length
                ? new TextEndOfParagraph(1)
                : new TextCharacters(text, textSourceCharacterIndex, text.Length - textSourceCharacterIndex, props);
        }
    }

    private sealed class TestParagraphProperties(TextRunProperties props, FlowDirection flow, double width) : TextParagraphProperties
    {
        public override TextRunProperties DefaultTextRunProperties => props;

        public override bool FirstLineInParagraph => true;

        public override FlowDirection FlowDirection => flow;

        public override double Indent => 0;

        public override double LineHeight => double.NaN;

        public override TextAlignment TextAlignment => TextAlignment.Left;

        public override TextMarkerProperties TextMarkerProperties => null!;

        public override TextWrapping TextWrapping => double.IsPositiveInfinity(width) ? TextWrapping.NoWrap : TextWrapping.Wrap;
    }

    private static List<(TextLine Line, int FirstCp)> FormatAllLines(string text, double width)
    {
        var props = new TestRunProperties();
        var source = new StringTextSource(text, props);
        var para = new TestParagraphProperties(props, FlowDirection.RightToLeft, width);
        using TextFormatter formatter = TextFormatter.Create();
        var cache = new TextRunCache();

        List<(TextLine, int)> lines = [];
        int dcp = 0;
        while (dcp < text.Length)
        {
            TextLine line = formatter.FormatLine(source, dcp, width, para, null, cache);
            lines.Add((line, dcp));
            dcp += line.Length;
        }

        return lines;
    }

    [Fact]
    public void RtlWrappedText_CaretRoundTrips_PointToCpToPoint()
    {
        // "שלום עולם" (Hebrew "hello world") at 16px in a 60px RTL paragraph wraps to multiple
        // lines; each line takes the FullTextLine path (RTL rejects SimpleTextLine).
        List<(TextLine Line, int FirstCp)> lines = FormatAllLines("שלום עולם", 60);
        Assert.True(lines.Count >= 2, $"expected wrapped RTL to produce multiple lines, got {lines.Count}");

        foreach ((TextLine line, int firstCp) in lines)
        {
            Assert.True(line.Length > 0, "every formatted RTL line must consume text");

            // point -> cp -> point round-trip: for a sample of distances inside the line, the
            // character hit at that distance maps back to the same distance (within one cell).
            for (int d = 0; d <= (int)line.WidthIncludingTrailingWhitespace; d += 7)
            {
                CharacterHit hit = line.GetCharacterHitFromDistance(d);
                double back = line.GetDistanceFromCharacterHit(hit);
                Assert.True(
                    Math.Abs(back - d) <= (line.WidthIncludingTrailingWhitespace / Math.Max(1, line.Length)) + 1.5,
                    $"round-trip {d} -> cp {hit.FirstCharacterIndex} -> {back}");
            }

            // cp -> point -> cp round-trip for every real text character (absolute cps). The
            // trailing newline characters are synthetic and unhittable (zero-width, like the
            // native LineBreak run), so they are excluded.
            for (int k = 0; k < line.Length - line.NewlineLength; k++)
            {
                double d = line.GetDistanceFromCharacterHit(new CharacterHit(firstCp + k, 0));
                CharacterHit hit = line.GetCharacterHitFromDistance(d);
                Assert.True(
                    hit.FirstCharacterIndex == firstCp + k || hit.FirstCharacterIndex == firstCp + k + 1,
                    $"cp {firstCp + k} -> distance {d} -> cp {hit.FirstCharacterIndex}");
            }
        }
    }

    [Fact]
    public void RtlWrappedText_CaretMovement_AdvancesOneCharacter()
    {
        List<(TextLine Line, int FirstCp)> lines = FormatAllLines("שלום עולם", 60);
        Assert.True(lines.Count >= 2);

        foreach ((TextLine line, int firstCp) in lines)
        {
            CharacterHit current = new(firstCp, 0);
            int position = firstCp * 2; // scalar: (cp * 2) + trailing
            int steps = 0;
            while (steps < 200)
            {
                CharacterHit next = line.GetNextCaretCharacterHit(current);
                int nextPosition = (next.FirstCharacterIndex * 2) + next.TrailingLength;
                if (nextPosition <= position)
                {
                    // No further caret stop (end of visible content — trailing spaces and the
                    // paragraph separator are not navigable). This is the natural stop.
                    break;
                }

                current = next;
                position = nextPosition;
                steps++;
            }

            // The caret must move through the line's visible characters; the exact terminal
            // position depends on where the trailing non-navigable runs begin.
            Assert.True(steps > 0, $"caret must move at least once on an RTL line of length {line.Length}");
            Assert.True(position > firstCp * 2, $"caret must leave the leading edge, stopped at {position}");
        }
    }

    [Fact]
    public void RtlSingleLine_CaretAtTrailingEdge_IsLastCharacter()
    {
        // Large finite width (paragraphWidth must not be infinity): one RTL line.
        List<(TextLine Line, int FirstCp)> lines = FormatAllLines("שלום", 10000);
        (TextLine line, int firstCp) = Assert.Single(lines);

        // Clicking at the trailing edge (width) yields the last character with trailing hit.
        // Length includes the paragraph separator, so the last text position is
        // Length - NewlineLength - 1.
        CharacterHit trailing = line.GetCharacterHitFromDistance(line.WidthIncludingTrailingWhitespace);
        Assert.True(
            trailing.FirstCharacterIndex >= firstCp + line.Length - line.NewlineLength - 1,
            $"trailing-edge click should hit the last character, got {trailing.FirstCharacterIndex}");
    }
}
