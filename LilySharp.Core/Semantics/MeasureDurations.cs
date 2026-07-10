// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Metric-duration helpers shared by the per-block <see cref="MeasureValidator"/>
/// and the <see cref="CrossPartMeasureValidator"/>. Both count a bar's beats the
/// same way (threading the running default note value, recursing into tuplets and
/// tremolos), so the logic lives in one place.
/// </summary>
internal static class MeasureDurations
{
    public static Fraction CalculateMeasureDuration(List<SyntaxNode> items, ref Fraction defaultDuration)
    {
        var total = Fraction.Zero;
        foreach (var item in items)
            total += ItemDuration(item, ref defaultDuration);
        return total;
    }

    /// <summary>
    /// Metric duration of a single music item, recursing into tuplets (scaled by
    /// their ratio) so triplets etc. fill the correct fraction of the bar.
    /// </summary>
    public static Fraction ItemDuration(SyntaxNode item, ref Fraction defaultDuration)
    {
        switch (item)
        {
            case NoteSyntax note:
                var noteDuration = DurationCalculator.GetDuration(note, defaultDuration);
                if (note.Duration != null) defaultDuration = noteDuration;
                return noteDuration;

            case DrumNoteSyntax drum:
                var drumDuration = drum.Duration is { } dd
                    ? dd.ToFraction()
                    : defaultDuration;
                if (drum.Duration != null) defaultDuration = drumDuration;
                return drumDuration;

            case RestSyntax rest:
                var restDuration = DurationCalculator.GetDuration(rest, defaultDuration);
                if (rest.Duration != null) defaultDuration = restDuration;
                return restDuration;

            case ChordSyntax chord:
                var chordDuration = DurationCalculator.GetDuration(chord, defaultDuration);
                if (chord.Duration != null) defaultDuration = chordDuration;
                return chordDuration;

            case TupletExpressionSyntax tuplet:
                // actual = written * BaseDivision / TupletRatio
                // (\tuplet 3/2 { c8 c c } -> 3 * 1/8 * 2/3 = 1/4).
                var inner = Fraction.Zero;
                foreach (var bodyItem in tuplet.Body.Items)
                    inner += ItemDuration(bodyItem, ref defaultDuration);
                if (tuplet.TupletRatio > 0)
                    inner *= new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);
                return inner;

            case GraceExpressionSyntax:
                // Grace notes are ornamental and consume no metric time.
                return Fraction.Zero;

            case RepeatExpressionSyntax rep
                when rep.RepeatType.Text == "tremolo"
                  && int.TryParse(rep.Count.Text, out int tremCount):
            {
                // LILYPOND-REF: lily/chord-tremolo-iterator.cc — the tremolo
                // repeat's metric length is count × body (8 × c32 = a quarter).
                var body = Fraction.Zero;
                foreach (var bodyItem in rep.Body.Items)
                    body += ItemDuration(bodyItem, ref defaultDuration);
                return body * new Fraction(tremCount, 1);
            }

            default:
                return Fraction.Zero;
        }
    }

    public static TextSpan GetSpan(List<SyntaxNode> items)
    {
        if (items.Count == 0)
            return new TextSpan(0, 0);

        // Use Span (not Position/FullSpan) to exclude leading/trailing trivia like comments
        int start = items[0].Span.Start;
        var lastSpan = items[^1].Span;
        int end = lastSpan.Start + lastSpan.Length;
        return new TextSpan(start, end - start);
    }
}
