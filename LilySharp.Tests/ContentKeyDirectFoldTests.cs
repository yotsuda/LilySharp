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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="MeasureContentKey"/> folds a value-typed property by calling its GetHashCode
/// directly instead of boxing it into <c>AddValue</c>. The claim is that the two fold THE
/// SAME NUMBER — and no corpus test can see it if they do not: a hash that changed but stayed
/// equally discriminating renders every book byte-identically and keeps every incremental
/// net green, right up until two different measures collide (HANDOFF RULES §5.3 — an
/// all-books A/B is not the instrument for an equation).
/// </summary>
public class ContentKeyDirectFoldTests
{
    /// <summary>A book reaching the item kinds the key folds: notes, chords, rests, a
    /// tie/slur, articulations, dynamics, a tuplet, a clef and meter change, a manual beam
    /// and a barline of its own.</summary>
    private const string Book = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          c8( d) e-. f-> <c e g>4 |
          c16 tuplet 3/2 { d16. e32 f16 } g16 r8 a8\f b8 |
          c8[ d e f] clef bass g,4 :|
          time 3/4 a,8@stemDown b, c d e f |
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// THE EQUATION: for every property taken without a box, on every item of every measure
    /// of the book, the direct fold equals the number the boxed path would have folded.
    /// </summary>
    /// <remarks>
    /// The liveness assertion matters as much as the equality one: if the no-box path were
    /// never taken this test would be a loop over nothing and pass forever (the empty-set
    /// trap, RULES §5.4).
    /// </remarks>
    [Fact]
    public void EveryUnboxedPropertyFoldsTheNumberTheBoxWouldHave()
    {
        var disagreed = new List<string>();
        int taken = 0, itemsSeen = 0;
        foreach (var item in EveryItem())
        {
            itemsSeen++;
            foreach (var (property, agrees) in MeasureContentKey.DirectFoldReport(item))
            {
                taken++;
                if (!agrees) disagreed.Add($"{item.GetType().Name}.{property}");
            }
        }

        Assert.True(itemsSeen > 20, $"only {itemsSeen} items reached — the net is vacuous");
        Assert.True(taken > 100,
            $"only {taken} properties took the no-box path — the net is vacuous");
        Assert.Empty(disagreed.Distinct());
    }

    /// <summary>
    /// THE SAME EQUATION FOR SEQUENCES (session 504): an <c>ImmutableArray&lt;T&gt;</c> property
    /// is walked where it stands instead of being boxed into <c>AddValue</c>, and the walk must
    /// fold the number the box would have — empty arrays, grace columns, chord notes (which
    /// keep their position-normalising arm) and all.
    /// </summary>
    /// <remarks>
    /// The book reaches a non-empty grace array and a chord's notes, so both element arms of
    /// the walk are exercised, not only the empty array every plain note carries.
    /// </remarks>
    [Fact]
    public void EverySequencePropertyFoldsTheNumberTheBoxWouldHave()
    {
        var disagreed = new List<string>();
        int taken = 0, nonEmptyGrace = 0, chords = 0;
        foreach (var item in EveryItem(BookWithGrace))
        {
            if (item is NoteItem { LeadingGrace.Length: > 0 } or ChordItem { LeadingGrace.Length: > 0 })
                nonEmptyGrace++;
            if (item is ChordItem)
                chords++;
            foreach (var (property, agrees) in MeasureContentKey.SequenceFoldReport(item))
            {
                taken++;
                if (!agrees) disagreed.Add($"{item.GetType().Name}.{property}");
            }
        }

        Assert.True(taken > 10,$"only {taken} sequence properties reached — the net is vacuous");
        Assert.True(nonEmptyGrace > 0, "no item carried a grace array — the element arm is untested");
        Assert.True(chords > 0, "no chord reached — the ChordNoteInfo arm is untested");
        Assert.Empty(disagreed.Distinct());
    }

    /// <summary>
    /// A DEFAULT (uninitialised) array folds the boxed path's marker: the boxed walk throws on
    /// it and <c>AddValue</c> folds −1 in its place. No book reaches this — a collected item
    /// always carries a real array — so the item is built by hand, as the exporters' fixtures
    /// build grace groups.
    /// </summary>
    [Fact]
    public void ADefaultArrayFoldsTheMarkerTheBoxedWalkFolds()
    {
        var item = new GraceNoteItem(GraceNoteType.Grace, default, 0, 0, 0);
        Assert.True(item.Columns.IsDefault, "the fixture must carry a default array");
        var report = MeasureContentKey.SequenceFoldReport(item);
        Assert.Contains(report, r => r.Property == nameof(GraceNoteItem.Columns));
        Assert.All(report, r => Assert.True(r.Agrees, $"{r.Property} folds differently"));
    }

    private const string BookWithGrace = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          grace { d'16 } c'4 grace { e'16 f'16 } <c' e' g'>4 grace { <d' f'>8 } e'4 f'4 |
          c'8( d') e'-. f'-> <c' e' g'>2 |
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// The three kinds <c>AddValue</c> reads specially must NOT be taken directly: a string
    /// is folded char by char, a sequence element by element, and a ChordNoteInfo with its
    /// source position normalised away. Taking any of them by GetHashCode would change what
    /// the key SEES — and for the chord note it would make the key position-dependent, which
    /// is the defect the normalisation exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(typeof(Measure), nameof(Measure.SectionLabel))]        // string
    [InlineData(typeof(Measure), nameof(Measure.Items))]               // sequence
    [InlineData(typeof(ChordItem), nameof(ChordItem.Notes))]           // sequence of ChordNoteInfo
    public void TheTypesAddValueReadsSpecially_StayOnTheObjectPath(Type declaring, string property)
    {
        Assert.True(MeasureContentKey.FoldsThroughTheObjectPath(declaring, property),
            $"{declaring.Name}.{property} is folded by GetHashCode, which is not what "
            + "AddValue does with it");
    }

    private static IEnumerable<object> EveryItem(string book = Book)
    {
        var tree = SyntaxTree.Parse(book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        foreach (var (_, staff, _) in score.EnumerateStaves())
            foreach (var voice in staff.Voices)
                foreach (var measure in voice.Measures)
                {
                    yield return measure;
                    foreach (var item in measure.Items)
                        yield return item;
                }
    }
}
