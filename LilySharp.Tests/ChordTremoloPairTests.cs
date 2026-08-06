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
