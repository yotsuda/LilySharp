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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The seam between the TWO walks of a <c>form</c>'s playback order:
/// <c>MeasureCollector.ProcessForm</c>, which a staff's music drives, and
/// <c>MeasureCollector.EnsureSectionStartsForRows</c>, which sizes the grid of a score
/// whose tracks are all rows (<c>lyrics N</c> / <c>chords N</c> and no staff). They must
/// agree on the ORDER and the OCCURRENCE COUNT of every section; they cannot be folded
/// into one because a part-less chord grid declares its sections inside the TRACK, so
/// there is no music for the first walk to walk.
/// </summary>
/// <remarks>
/// The net is a DIFFERENTIAL: the same book is rendered twice, once with <c>staff melody</c>
/// in the score and once without, and the lyrics row — collected by the same
/// <c>LyricsCollector.CollectRow</c> in both — must land on the same bars. Only the source
/// of the section starts differs, which is exactly the seam.
///
/// Before this net existed the rows-only walk skipped <c>|: … :|</c> blocks whole (every arm
/// was gated on <c>!IsInsideRepeatBlock</c> and no arm handled the block) and collapsed a
/// section's second occurrence onto its first, with a cursor assignment that REWOUND the
/// grid. A staffless <c>form main { A B A }</c> engraved 6 bars instead of 10, and the
/// reprise's syllables were laid on top of the first pass's — the "lyrics overlap" report.
/// The suite was fully green throughout: 5874 tests, 222 snapshots and 572 tracked books all
/// agreed, because every tracked staffless book names each of its sections exactly once.
/// </remarks>
public class RowsOnlyFormOrderTests
{
    // One book, two scores. `staff melody` is the ONLY difference; `lyrics verse` is an
    // independent row in both, so both renders reach LyricsCollector.CollectRow and differ
    // only in where StartMeasure / AllStarts came from.
    private const string Head = """
        time 4/4
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | f4 f e e | d d c2 | }
          section B { g'4 g f f | e e d2 | }
        }
        lyrics verse {
          section A { Twin- kle twin- kle | lit- tle star | How I won- der | what you are | }
          section B { Up a- bove the | world so high | }
        }
        """;

    private static string Book(string form, bool withStaff) => $$"""
        {{Head}}
        form main { {{form}} }
        score main {
        {{(withStaff ? "  staff melody" : "")}}
          lyrics verse
        }
        """;

    private static LilySharp.Core.Svg.Model.MultiStaffScore Collect(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        return new MeasureCollector().CollectMultiStaff(tree, spec!);
    }

    // (text, bar, verse) of the lyrics ROW, in reading order. The row's X inside a bar is
    // an even spread in both renders, so the bar and the verse are the whole answer.
    private static List<(string Text, int Bar, int Verse)> RowSyllables(
        LilySharp.Core.Svg.Model.MultiStaffScore score)
        => score.Lyrics.Where(l => l.IsLyricsRow)
            .OrderBy(l => l.MeasureIndex).ThenBy(l => l.VerseNumber).ThenBy(l => l.ItemIndex)
            .Select(l => (l.Text, l.MeasureIndex, l.VerseNumber)).ToList();

    private static int RowBars(LilySharp.Core.Svg.Model.MultiStaffScore score)
        => score.Lyrics.Where(l => l.IsLyricsRow).Select(l => l.MeasureIndex).DefaultIfEmpty(-1).Max() + 1;

    // The seam itself, for each shape of the playback order.
    [Theory]
    [InlineData("A B")]           // no reprise — the shape every tracked staffless book writes
    [InlineData("A B A")]         // a section named twice
    [InlineData("A |: B :| A")]   // a repeat block between two passes of the same section
    [InlineData("A |: B :| A \"A2\"")] // …closed by a named alternative ending
    [InlineData("~A B")]          // a silent (unlabelled) reference
    // Navigation marks are anchored by the same cursor. They are written BARE in a form
    // ('@' modifies a note) — the parser says so, and this fixture was rejected until it did.
    [InlineData("A segno B A fine")]
    public void RowsOnlyGrid_PlacesTheSameSyllablesAsTheStafffulPath(string form)
    {
        var staffless = Collect(Book(form, withStaff: false));
        var staffful = Collect(Book(form, withStaff: true));

        Assert.Equal(RowSyllables(staffful), RowSyllables(staffless));
        Assert.Equal(RowBars(staffful), RowBars(staffless));
        // The bar cursor also anchors the structure signs a band chart needs — a segno
        // sits at the NEXT section's start and a Fine at the END of the section just
        // played, so a cursor that rewound put both on bars that had already gone by.
        Assert.Equal(
            staffful.MusicMarks.Select(m => (m.Type, m.MeasureIndex)).OrderBy(m => m.MeasureIndex).ToList(),
            staffless.MusicMarks.Select(m => (m.Type, m.MeasureIndex)).OrderBy(m => m.MeasureIndex).ToList());
    }

    // The measured answer, pinned independently of the differential above — so a
    // regression that moves BOTH walks the same way still fails something.
    [Fact]
    public void RepeatedSection_IsEngravedOnceMore_NotFoldedOntoItsFirstPass()
    {
        var rows = RowSyllables(Collect(Book("A B A", withStaff: false)));

        // A is 4 bars and B is 2, so the reprise starts at bar 6 and the grid is 10 bars.
        Assert.Equal(10, RowBars(Collect(Book("A B A", withStaff: false))));
        Assert.Equal(new[] { 0, 1, 2, 3 }, rows.Where(r => r.Text == "Twin" || r.Text == "lit"
            || r.Text == "How" || r.Text == "what").Where(r => r.Bar < 6).Select(r => r.Bar).Distinct());
        Assert.Equal(new[] { 6, 7, 8, 9 }, rows.Where(r => r.Text == "Twin" || r.Text == "lit"
            || r.Text == "How" || r.Text == "what").Where(r => r.Bar >= 6).Select(r => r.Bar).Distinct());
        // …and nothing stacked as a second verse, which is what the overlap looked like.
        Assert.All(rows, r => Assert.Equal(1, r.Verse));
    }

    // A part-less chord grid: the sections live inside the TRACK, so this shape has no
    // music anywhere in the book and ProcessForm can never reach it. It is the reason the
    // rows-only walk exists at all — and it had the same defect.
    private const string Grid = """
        time 4/4
        chords prog {
          section A { C | F | G | C | }
          section B { Am | G | }
        }
        form main { A B A }
        score main { chords prog }
        """;

    [Fact]
    public void PartLessChordGrid_ReplaysARepeatedSection()
    {
        var score = Collect(Grid);
        var bars = score.ChordNames.OrderBy(c => c.MeasureIndex)
            .Select(c => (c.ChordText, c.MeasureIndex)).ToList();

        Assert.Equal(new[]
        {
            ("C", 0), ("F", 1), ("G", 2), ("C", 3),
            ("Am", 4), ("G", 5),
            ("C", 6), ("F", 7), ("G", 8), ("C", 9),
        }, bars);
    }

    // A section written entirely as `[N. …]` verses is the shape the reported book used.
    // The verses are ALTERNATIVE stanzas for the same bars, so the section is as wide as
    // the widest one — not their sum, and not the zero a direct-children count answered.
    [Fact]
    public void BracketedVerses_SizeTheirSection_ByTheWidestVerse()
    {
        const string src = """
            time 4/4
            lyrics verse {
              section A { one | two | }
              section B {
                [~1. three | four |]
                [~2. five | six |]
              }
            }
            chords prog { section A { C | F | } section B { G | C | } }
            form main { A B }
            score main { chords prog  lyrics verse }
            """;
        var tree = SyntaxTree.Parse(src);
        Assert.False(tree.HasErrors);
        var block = tree.GetRoot().DescendantNodes().OfType<LyricsBlockSyntax>().Single();
        var sectionB = block.Sections.Single(s => s.SectionName == "B");

        Assert.Equal(2, LyricSyllableReader.CountBars(sectionB));
    }
}
