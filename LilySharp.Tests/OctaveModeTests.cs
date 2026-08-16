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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for the <c>octave absolute</c> / <c>octave relative</c> directive.
/// Relative is the default; absolute makes <c>'</c>/<c>,</c> offsets from a
/// fixed C4 anchor (bare c = C4), with no carry between notes.
/// </summary>
[Trait("Category", "Unit")]
public class OctaveModeTests
{
    private static string[] Trace(string source)
    {
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(source), "melody");
        return collector.PitchTrace.Select(e => e.Pitch).ToArray();
    }

    private static string Wrap(string body, string prefix = "") => $@"
{prefix}part melody {{ clef treble }}
section A {{ melody {{ {body} }} }}
form main {{ A }}
score main ""t"" {{ staff {{ melody }} }}
";

    [Fact]
    public void Absolute_IsStateless_OffsetFromC4()
    {
        // octave absolute: bare c = C4, each ' = +1 octave, each , = -1, no carry.
        var trace = Trace(Wrap("cis'4 c'' c' e' | c d e f |", prefix: "octave absolute\n"));
        Assert.Equal(
            new[] { "C#5", "C6", "C5", "E5", "C4", "D4", "E4", "F4" },
            trace);
    }

    [Fact]
    public void Relative_IsDefault_AndAccumulates()
    {
        // No directive ⇒ relative (unchanged): the same text accumulates octaves
        // (each ' adds to the previous note's octave), so it must NOT match the
        // absolute reading. Guards against the default flipping to absolute.
        var trace = Trace(Wrap("cis'4 c'' c' e' |"));
        Assert.Equal(new[] { "C#5", "C7", "C8", "E9" }, trace);
    }

    [Fact]
    public void MidStream_SwitchesBothDirections()
    {
        // relative … then `octave absolute` … then `octave relative` again.
        var trace = Trace(Wrap("c'4 d e octave absolute c' c'' octave relative g a |"));
        //                      C5  D5 E5            C5  C6              G5 A5
        Assert.Equal(new[] { "C5", "D5", "E5", "C5", "C6", "G5", "A5" }, trace);
    }

    [Fact]
    public void Mode_RevertsToFileDefault_AtSectionBoundary()
    {
        // A mid-section `octave absolute` does not leak into the next section:
        // section B starts back in the file-default relative mode.
        var src = @"
part melody { clef treble }
section A { melody { octave absolute  c' c'' | } }
section B { melody { c' c'' | } }
form main { A B }
score main ""t"" { staff melody }
";
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(src), "melody");
        var trace = collector.PitchTrace.Select(e => e.Pitch).ToArray();

        // A: absolute → C5, C6.  B: relative → c' = C5, then c'' accumulates to
        // C7 (NOT the absolute C6), proving the mode reverted at the boundary.
        Assert.Equal("C5", trace[0]);
        Assert.Equal("C6", trace[1]);
        Assert.Equal("C5", trace[2]);
        Assert.Equal("C7", trace[3]);
    }

    // ---- a phrase reference's trailing marks, in ABSOLUTE mode ----------------------

    // A reference's ' / , move the frame its body is read in. Relative mode moves the
    // running frame; absolute mode has none, so the same shift moves OctaveBase — the
    // anchor a bare `c` is measured from. Until 2026-08-16 the absolute half was simply
    // missing: `theme'`, `theme,` and `theme` drew the same page, and only the LilyPond
    // twin said so (it warned that the body was exported UNSHIFTED).

    private static string PhraseBook(string reference, bool absolute) => $@"
{(absolute ? "octave absolute" : "")}
part melody {{ clef treble }}
phrase theme {{ c4 d e f }}
section A {{ melody {{ {reference} | }} }}
form main {{ A }}
score main ""t"" {{ staff melody }}
";

    [Theory]
    [InlineData("theme",  new[] { "C4", "D4", "E4", "F4" })]
    [InlineData("theme'", new[] { "C5", "D5", "E5", "F5" })]
    [InlineData("theme,", new[] { "C3", "D3", "E3", "F3" })]
    public void AnAbsolutePhraseReference_TakesItsTrailingMarks(string reference, string[] pitches)
    {
        Assert.Equal(pitches, Trace(PhraseBook(reference, absolute: true)));
    }

    [Theory]
    [InlineData("theme")]
    [InlineData("theme'")]
    [InlineData("theme,")]
    public void TheTwoOctaveModes_ReadAReferenceTheSameWay(string reference)
    {
        // The identity pair. Confirmed outside the suite against LilyPond 2.26.0: the two
        // twins this produces (`\relative c''` and `\fixed c''`) render to byte-identical
        // SVGs, and the three spellings render to three different ones.
        Assert.Equal(Trace(PhraseBook(reference, absolute: false)),
                     Trace(PhraseBook(reference, absolute: true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReferencesMarkMovesItsBody_InBothModes(bool absolute)
    {
        // The positive control for the pair above: two modes that BOTH ignored the mark
        // would agree, and agreeing is what that test checks. So the mark has to move
        // something in each mode on its own.
        Assert.NotEqual(Trace(PhraseBook("theme", absolute)), Trace(PhraseBook("theme'", absolute)));
        Assert.NotEqual(Trace(PhraseBook("theme", absolute)), Trace(PhraseBook("theme,", absolute)));
    }

    [Fact]
    public void AReferencesMarkDoesNotLeakPastIt_InAbsoluteMode()
    {
        // The shift is scoped to the body: OctaveBase is pushed at the reference and
        // popped at its end marker, so the bare `c` AFTER the reference is C4 again. Two
        // references in a row each start from the unshifted anchor rather than compounding.
        Assert.Equal(
            new[] { "C5", "D5", "E5", "F5", "C4", "C3", "D3", "E3", "F3" },
            Trace(PhraseBook("theme' c4 theme,", absolute: true)));
    }
}
