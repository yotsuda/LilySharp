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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Nets for three rules of the LilyPond twin that REFACTOR_PLAN stages A and B found observed
/// by the corpus sweep alone (HANDOFF §1.1 第740 ⑶, 第741 ①③ — 125, 16 and 1 books moved
/// under the poisons, and no test): the stream frame a nested body inherits and hands back,
/// the <c>\skip</c> a lyrics stream writes before its first syllable, and the bar a line
/// break splits, which gets no bar check between its halves.
/// </summary>
[Trait("Category", "Unit")]
public class LilyPondExporterStreamNetsTests
{
    private static string Export(string lys)
    {
        var tree = SyntaxTree.Parse(lys);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new LilyPondExporter().Export(tree);
    }

    /// <summary>
    /// A tuplet's body is written by a nested exporter that hands its stream frame BACK —
    /// the last written value among it — so the second tuplet's <c>c</c> writes no value
    /// (the first left an eighth) and the <c>d2</c> after it reads from the <c>e</c>:
    /// test/tuplets-beamed's first bar, one of the 16 books the sweep moved when the frame
    /// was not carried back while no test did (HANDOFF §1.1 第740 ⑶). The frame carried IN
    /// is observed by ShapeChordItemTests.InAGraceAndATuplet_TheSameNotes.
    /// </summary>
    [Fact]
    public void TheStreamFrame_IsCarriedBackFromANestedBody()
    {
        var ly = Export("""
            time 4/4
            part melody { clef treble }
            section A { melody { tuplet 3/2 { g'8 a b } tuplet 3/2 { c d e } d2 | } }
            form main { A }
            score main { staff melody }
            """);
        Assert.Contains("\\tuplet 3/2 { g'8 a b } \\tuplet 3/2 { c d e } d2 |", ly);
    }

    /// <summary>A lyrics stream read off the page fills the time before its first syllable
    /// of a bar with <c>\skip</c> — the rest the melody opens its second bar with. (A rest
    /// between syllables is covered by holding the syllable before it: <c>a2</c>.)</summary>
    [Fact]
    public void ALyricsStream_SkipsTheTimeBeforeItsFirstSyllable()
    {
        var ly = Export("""
            time 4/4
            part melody { clef treble }
            section A { melody { c'4 r d' e' | r2 f' | } lyrics words sings melody { a b c | d | } }
            form main { A }
            score main { staff melody lyrics words }
            """);
        Assert.Contains("a2 b4 c4 |", ly);
        Assert.Contains("\\skip 2 d2 |", ly);
    }

    /// <summary>The two halves of a bar a <c>break</c> splits are ONE bar in LilyPond: the
    /// stream's first half ends its line with no bar check, and the second half closes the
    /// bar (test/mid-bar-break's lyrics row).</summary>
    [Fact]
    public void ASplitBar_GetsNoBarCheckBetweenItsHalves()
    {
        var ly = Export("""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c'4 d break e f | g1 | } }
            lyrics words { section Main { a b c d | e | } }
            form main { Main }
            score main { staff melody  lyrics words }
            """);
        Assert.Contains("a4 b4\n  c4 d4 |", ly);
    }
}
