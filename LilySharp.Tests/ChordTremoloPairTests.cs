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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Two-note tremolo pairs (<c>repeat tremolo N { a b }</c>): the gap-count that
/// keeps the repeat symbol from reading as an ordinary beam, and the chord-body
/// pair that used to fall out of the pair machinery entirely.
/// LILYPOND-REF: lily/chord-tremolo-engraver.cc:117-140 acknowledge_stem —
/// gap_count = min(flags, intlog2(repeat_count) + 1), set unless
/// Stem::duration_log == 1 (a half-note pair's beams reach the stems).
/// Paired with the LP regression books chord-tremolo.ly (gap claim) and
/// chord-tremolo-accidental.ly (chord bodies).
/// </summary>
[Trait("Category", "Unit")]
public class ChordTremoloPairTests
{
    private static List<MusicItem> Items(string source)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(source), null);
        return score.Voice.Measures.SelectMany(m => m.Items).ToList();
    }

    [Fact]
    public void Pair_CarriesTheGapCount_WholeDisplay()
    {
        // 4 × (d32 e) = a whole: flags 3, min(3, log2(4)+1 = 3) = 3 — all beams gapped.
        var notes = Items("repeat tremolo 16 { d32 e }").OfType<NoteItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n =>
        {
            Assert.Equal(3, n.TremoloPairBeams);
            Assert.Equal(3, n.TremoloGapCount);
            Assert.Equal(Fraction.Whole, n.BaseDuration);
            Assert.Equal(new Fraction(1, 2), n.TimeScale);
        });
    }

    [Fact]
    public void Pair_HalfDisplay_HasNoGap()
    {
        // 4 × (d16 e) = a half note: duration_log == 1 — beams reach the stems.
        var notes = Items("time 2/4 repeat tremolo 4 { d16 e }").OfType<NoteItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n =>
        {
            Assert.Equal(2, n.TremoloPairBeams);
            Assert.Equal(0, n.TremoloGapCount);
            Assert.Equal(Fraction.Half, n.BaseDuration);
        });
    }

    [Fact]
    public void Pair_DottedHalfDisplay_HasNoGap()
    {
        // 3 × (d8 e) = a dotted half: still duration_log 1 (dots don't change it).
        var notes = Items("time 3/4 repeat tremolo 3 { d8 e }").OfType<NoteItem>().ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, n => Assert.Equal(0, n.TremoloGapCount));
    }

    [Fact]
    public void WholeNoteTremolo_DrawsStemlessSlashes()
    {
        // Regression chord-tremolo-single: `repeat tremolo 32 { d32 }` reduces
        // to a WHOLE note — no stem, so the three slashes anchor 1.5ss above
        // the head and stack 0.81 apart (they used to vanish with the stem).
        // LILYPOND-REF: lily/stem-tremolo.cc:349-366 y_offset (whole_note branch).
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 repeat tremolo 32 { d32 }"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        var slashes = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.480\"")
            .Where(m => m.Groups[2].Value != m.Groups[4].Value) // slanted, not a stem
            .ToList();
        Assert.Equal(3, slashes.Count);
        // The stack steps 0.81 per flag and each slash rises 1.5 × 0.25 across.
        var y1s = slashes.Select(m => double.Parse(m.Groups[2].Value)).OrderBy(v => v).ToList();
        Assert.Equal(0.81, y1s[1] - y1s[0], 2);
        Assert.Equal(0.81, y1s[2] - y1s[1], 2);
        var m0 = slashes[0];
        Assert.Equal(1.5, double.Parse(m0.Groups[3].Value) - double.Parse(m0.Groups[1].Value), 2);
    }

    [Fact]
    public void WholeNotePair_HasInvisibleStemsAndFloatingBeams()
    {
        // Regression chord-tremolo-whole: `repeat tremolo 32 { g''64 a }` displays
        // two WHOLE notes joined by four beams. The stems are Stem grobs with NO ink
        // (duration-log 0 < 1), the beams float BETWEEN the heads — each gapped end
        // clamped to the head's inner edge ± gap/2 — and the stack is seeded from
        // the heads (no_visible_stem_positions), landing on hang-below-middle-line:
        // beam centres 9.26/10.13/11.01/11.88, LilyPond's page to three decimals.
        // LILYPOND-REF: lily/stem.cc:1006-1018 Stem::print (is_valid_stem);
        //   lily/beam-quanting.cc:485-510 no_visible_stem_positions;
        //   lily/beam.cc:637-654 calc_beam_segments (the Stem::is_invisible clamp).
        string svg = LilySharp.Core.Svg.SvgGenerator.Generate(
            SyntaxTree.Parse("time 4/4 repeat tremolo 32 { g''64 a }"),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        // No stem ink: no vertical line at stem thickness anywhere.
        var stems = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.130\"")
            .Where(m => m.Groups[1].Value == m.Groups[3].Value)
            .ToList();
        Assert.Empty(stems);

        // Four flat beams, stacked at the 4-beam translation (3·ss + line − thick)/3.
        var beams = System.Text.RegularExpressions.Regex.Matches(svg,
                "<polygon points=\"([-\\d.]+),([-\\d.]+) ([-\\d.]+),([-\\d.]+) [^\"]+\"")
            .Select(m => (XLeft: double.Parse(m.Groups[1].Value),
                          YLeft: double.Parse(m.Groups[2].Value),
                          XRight: double.Parse(m.Groups[3].Value),
                          YRight: double.Parse(m.Groups[4].Value)))
            .ToList();
        Assert.Equal(4, beams.Count);
        Assert.All(beams, b => Assert.Equal(b.YLeft, b.YRight, 3));
        // SVG coordinates round to 2 decimals, so each step reads 0.87–0.88.
        var ys = beams.Select(b => b.YLeft).OrderBy(v => v).ToList();
        Assert.All(new[] { ys[1] - ys[0], ys[2] - ys[1], ys[3] - ys[2] },
            step => Assert.InRange(step, 0.86, 0.89));

        // The stack hangs from the MIDDLE staff line: the bottom beam's top edge
        // flush with the line's top edge (the "hang" quant, centre 0.19 below).
        // Staff lines are the LONG horizontal lines; the a''s ledger line is short.
        var staffLineYs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"")
            .Where(m => m.Groups[2].Value == m.Groups[4].Value
                && double.Parse(m.Groups[3].Value) - double.Parse(m.Groups[1].Value) > 5)
            .Select(m => double.Parse(m.Groups[2].Value))
            .Distinct().OrderBy(v => v).ToList();
        Assert.Equal(5, staffLineYs.Count);
        double middleLineY = staffLineYs[2];
        // ys are the beams' TOP edges (the polygon's first two points); the staff
        // line's own half-thickness is 0.05.
        Assert.Equal(middleLineY - 0.05, ys.Max(), 2);

        // The gapped beams clear both heads by half a gap (0.4) from the inner edges.
        var headXs = System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\" x=\"([-\\d.]+)\" y=\"([-\\d.]+)\"[^>]*data-pos")
            .Select(m => double.Parse(m.Groups[1].Value)).OrderBy(v => v).ToList();
        Assert.Equal(2, headXs.Count);
        double headWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.GetNoteheadBBox(1).Right;
        Assert.Equal(headXs[0] + headWidth + 0.4, beams.Min(b => b.XLeft), 2);
        Assert.Equal(headXs[1] - 0.4, beams.Max(b => b.XRight), 2);
    }

    [Fact]
    public void Pair_WithAChordBody_JoinsThePairMachinery()
    {
        // Regression chord-tremolo-accidental: `c''32 <dis'' fis''>` — the chord
        // half of the pair used to skip the pair transform entirely (silently
        // rendered at its written 32nd value with no pair beams).
        var items = Items("repeat tremolo 16 { c'32 <dis' fis'> }");
        var note = Assert.Single(items.OfType<NoteItem>());
        var chord = Assert.Single(items.OfType<ChordItem>());
        Assert.Equal(3, note.TremoloPairBeams);
        Assert.Equal(3, chord.TremoloPairBeams);
        Assert.Equal(3, chord.TremoloGapCount);
        Assert.Equal(Fraction.Whole, chord.BaseDuration);
        Assert.Equal(new Fraction(1, 2), chord.TimeScale);
        Assert.True(note.HasBeamStart);
        Assert.True(chord.HasBeamEnd);
        // The chord's members resolve normally (dis' fis' — the accidental book's
        // subject is these accidentals appearing on the pair).
        Assert.Equal(2, chord.Notes.Length);
        Assert.Equal("sharp", chord.Notes[0].Accidental);
        Assert.Equal("sharp", chord.Notes[1].Accidental);
    }
}
