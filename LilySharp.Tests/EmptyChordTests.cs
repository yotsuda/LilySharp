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

using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The empty chord <c>&lt;&gt;</c> is a carrier for post-events only: it occupies no time,
/// leaves the running default duration alone, and its marks belong to the moment it sits at.
/// The corpus twin of LilyPond's regression <c>empty-chord.ly</c> is the book
/// (<c>audit/lp-regression/lys/empty-chord.lys</c>).
/// </summary>
/// <remarks>
/// Both defects below were invisible until the file had to be structured: a headerless note
/// stream skipped measure validation entirely, so the twin's overfull bars and its dropped
/// phrase mark said nothing at all (found while closing LYS0020, 2026-08-09).
/// </remarks>
[Trait("Category", "Unit")]
public class EmptyChordTests
{
    private static IReadOnlyList<Diagnostic> MeasureDiagnostics(string music)
    {
        var validator = new MeasureValidator();
        validator.Validate(SyntaxTree.Parse(MusicSource.Wrap(music, "octave absolute\ntime 4/4")));
        return validator.Diagnostics;
    }

    /// <summary>The engraved result, with the source offsets stripped — two sources that
    /// differ only in spelling must produce the identical glyph stream.</summary>
    private static string Engraved(string music)
    {
        string svg = SvgGenerator.Generate(
            SyntaxTree.Parse(MusicSource.Wrap(music, "octave absolute\ntime 4/4")),
            new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });
        return Regex.Replace(svg, " data-pos=\"\\d+\"", "");
    }

    [Fact]
    public void EmptyChord_OccupiesNoTime()
    {
        // r4 e8 g <> c'4 r4 = 1/4 + 1/8 + 1/8 + 1/4 + 1/4 = exactly 4/4. The metric side
        // used to charge <> the running 1/8 and call the bar 9/8 — the collector adds no
        // item for it at all, so the two spellings of "how long is a chord" disagreed.
        // Both now read ChordSyntax.IsEmpty.
        Assert.Empty(MeasureDiagnostics("r4 e8 g <> c'4 r4 |"));
    }

    [Fact]
    public void EmptyChord_LeavesTheRunningDurationAlone()
    {
        // `<>4` carries a written duration, and it must change NOTHING: the two `c` after
        // it are still EIGHTHS (inherited from g), so the bar is
        //   1/4 + 1/8 + 1/8 + 0 + 1/8 + 1/8 + 1/4 = 4/4.
        // Had the `4` become the running default, they would be quarters and the bar 5/4.
        // LILYPOND-REF: regression empty-chord.ly — "occupy no time, and leave the current
        // duration unchanged" (both halves of the claim, in one bar).
        Assert.Empty(MeasureDiagnostics("r4 e8 g <>4 c c r4 |"));
    }

    [Fact]
    public void EmptyChord_DoesNotSwallowASlurEnd()
    {
        var tree = SyntaxTree.Parse(MusicSource.Wrap("r4 e8( g <>) c'4 r4 |", "octave absolute\ntime 4/4"));
        var validator = new SlurPairingValidator();
        validator.Validate(tree);

        // The mark used to be dropped on the floor: no slur drawn, and the warning that
        // says so only appeared once the file was structured.
        Assert.Empty(validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSlur));
    }

    [Fact]
    public void SlurClosedOnAnEmptyChord_EndsOnTheNoteAtThatMoment()
    {
        // ★ MEASURED against LilyPond 2.26.0 (scratch/lpreg/ecslur-{a,b,c}.ly). <> occupies
        // no time, so its moment IS the following note's, and LP ends the slur there:
        //   (a) r4 e'8( g' <>) c''4  and  (c) r4 e'8( g' c''4)  draw the SAME curve
        //       (both 1.2883 -> 6.1207), while
        //   (b) r4 e'8( g')          closing on g' draws a different one (0.7803 -> 3.5345).
        // So this is the LP identity, not a convenience: the slur does NOT end on the note
        // the ')' visually trails. Do not "fix" it that way.
        Assert.Equal(
            Engraved("r4 e8( g c'4) r4 |"),
            Engraved("r4 e8( g <>) c'4 r4 |"));
    }

    [Fact]
    public void SlurOpenedOnAnEmptyChord_StartsOnTheNoteAtThatMoment()
    {
        // The same rule on the opening side, by the same mechanism.
        Assert.Equal(
            Engraved("r4 e8 g c'4( r4) |"),
            Engraved("r4 e8 g <>( c'4 r4) |"));
    }

    [Fact]
    public void EmptyChordSlur_ReachesIntoAFollowingTuplet()
    {
        // The carrier can be the first note INSIDE a group: the tuplet path emits items on
        // its own, so it takes the pending mark too (the one-arm-of-two hole this codebase
        // keeps re-finding).
        var tree = SyntaxTree.Parse(MusicSource.Wrap(
            "r4 e8( g <>) tuplet 3/2 { c' d' e' } r4 |", "octave absolute\ntime 4/4"));
        var validator = new SlurPairingValidator();
        validator.Validate(tree);

        Assert.Empty(validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSlur));
    }

    [Fact]
    public void ADegreeChordIsNotEmpty()
    {
        // ⚠️ "Empty" is not "has no Pitches" — a degree chord and a drum chord have no pitch
        // members and are full chords. Reading it that way would give them no duration.
        var chords = SyntaxTree.Parse(MusicSource.Wrap("<1 3 5>4 <c e g>4 <>4 c4 |"))
            .GetNodes<ChordSyntax>().ToList();

        Assert.Equal(3, chords.Count);
        Assert.False(chords[0].IsEmpty);  // <1 3 5>
        Assert.False(chords[1].IsEmpty);  // <c e g>
        Assert.True(chords[2].IsEmpty);   // <>
    }
}
