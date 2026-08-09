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
                var drumDuration = DurationCalculator.GetDuration(drum, defaultDuration);
                if (drum.Duration != null) defaultDuration = drumDuration;
                return drumDuration;

            case RestSyntax rest:
                var restDuration = DurationCalculator.GetDuration(rest, defaultDuration);
                if (rest.Duration != null) defaultDuration = restDuration;
                return restDuration;

            // The EMPTY chord <> is a post-event carrier and occupies NO time — with or
            // without a written duration. A duration written on it still SETS the running
            // default for what follows, exactly as one written on any other chord does.
            // ★ MEASURED against LilyPond 2.26.0, three bar-checks
            // (scratch/lpreg/ecdur-p{1,2,3}.ly):
            //   p1  e'8 g' <>   c''×6            = 4/4 exactly  -> bare <> changes nothing
            //   p3  e'8 g' <>4  c''8×6           = 4/4 exactly  -> <>4 consumes no time
            //   p2  e'8 g' <>4  c''×6 (inherited)= 7/4          -> <>4 DID set the default
            // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — a written
            //   duration becomes parser->default_duration_. It is PARSER state, set by the
            //   grammar rule every event goes through, and `note_chord_element: chord_body
            //   optional_notemode_duration post_events` (:3148-3164) means <> goes through
            //   it like anything else. Nothing there asks whether the chord had members.
            // ⚠️ The regression's texidoc says "occupy no time, and leave the current
            // duration unchanged", and that sentence is about the BARE <> the book writes.
            // Reading it as covering <>4 too — which is how this was first written — makes
            // the notes after <>4 inherit the wrong value in silence. The rule was already
            // cited correctly in four other places in this tree (MeasureCollector's
            // _defaultDuration, ItemFactory, the arpeggio total, the .ly exporter); the
            // mistake was inferring from a texidoc sentence instead of reading them.
            case ChordSyntax { IsEmpty: true } empty:
                if (empty.Duration != null)
                    defaultDuration = DurationCalculator.GetDuration(empty, defaultDuration);
                return Fraction.Zero;

            case ChordSyntax chord:
                var chordDuration = DurationCalculator.GetDuration(chord, defaultDuration);
                if (chord.Duration != null) defaultDuration = chordDuration;
                return chordDuration;

            case ChordRepetitionSyntax rep:
                // `q` occupies its own written duration (the copied notes take
                // the repetition's duration — LP copy-repeat-chord :890-891).
                var repDuration = DurationCalculator.GetDuration(rep, defaultDuration);
                if (rep.Duration != null) defaultDuration = repDuration;
                return repDuration;

            case TupletExpressionSyntax tuplet:
                // actual = written * BaseDivision / TupletRatio
                // (\tuplet 3/2 { c8 c c } -> 3 * 1/8 * 2/3 = 1/4).
                var inner = Fraction.Zero;
                foreach (var bodyItem in tuplet.Body.Items)
                    inner += ItemDuration(bodyItem, ref defaultDuration);
                if (tuplet.TupletRatio > 0)
                    inner *= new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);
                return inner;

            case ArpeggioSyntax arp:
            {
                // `<< … >>` is one written-out broken chord: its durationless members
                // equally subdivide the group's total, so it occupies exactly that total —
                // the trailing `N`, or (absent one) the inherited running duration, so it
                // acts like a single note both in metric length and in the default-carry.
                var arpTotal = arp.TotalDuration?.ToFraction() ?? defaultDuration;
                if (arp.TotalDuration != null) defaultDuration = arpTotal;
                return arpTotal;
            }

            case GraceExpressionSyntax:
                // Grace notes are ornamental and consume no metric time.
                return Fraction.Zero;

            case CueExpressionSyntax cue:
            {
                // ⚠️ A cue region is ORDINARY METRIC TIME — unlike a grace, it is real music
                // in a smaller type, and LilyPond's CueVoice occupies the bar like any voice.
                // Leaving it at the default zero made every bar holding one look short:
                // `c4 d cue { e4 f }` was validated as 1/2 of 4/4 and drew LYS2006 on a bar
                // that is exactly full.
                var cueTotal = Fraction.Zero;
                foreach (var bodyItem in cue.Body.Items)
                    cueTotal += ItemDuration(bodyItem, ref defaultDuration);
                return cueTotal;
            }

            case RepeatExpressionSyntax rep
                when rep.RepeatType.Text == "tremolo"
                  && int.TryParse(rep.Count.Text, out int tremCount):
            {
                // LILYPOND-REF: lily/chord-tremolo-engraver.cc — the tremolo
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
