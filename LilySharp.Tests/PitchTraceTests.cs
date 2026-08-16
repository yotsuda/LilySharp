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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The resolved-pitch trace behind <c>lysc check --pitches</c>, which RULES §5.3 判定法⑶
/// tells every session to run over a synthesized book before filing anything about it.
/// </summary>
/// <remarks>
/// It used to be built from a bare <c>new MeasureCollector().Collect(tree)</c> — no
/// RenderSpec — and that is a different answer, not a cheaper one: with no spec the
/// relative-octave chain runs once through the tree in source order, so a section's
/// SECOND part block inherits the first one's chain instead of starting at its own
/// clef's anchor. The bass part of <c>test/grandstaff-high-bass</c> was reported at C6.
/// The renderer never did that, and the tiebreak was geometry, not opinion: swapping
/// the two part blocks moved the reported bass from C6 to C4 while the drawn SVG stayed
/// character-identical (data-pos masked), and the drawn chord sits at y 24.62/23.62/22.62
/// against bass staff lines 22.12…26.12 — inside the staff, C3-E3-G3, no ledger lines.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PitchTraceTests
{
    // One treble part and one bass part in the same section. The bass part is what the
    // old reading got wrong, and it is second precisely so that it would.
    private const string TwoParts =
        """
        time 4/4

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          rh { c'1 | }
          lh { c4 d e f | }
        }

        form main { ~Main }

        score main {
          staff rh
          staff lh
        }
        """;

    // The same piece with the two part blocks written the other way round. Nothing about
    // the music, the score or the page changes — only the order they appear in the file.
    private const string Swapped =
        """
        time 4/4

        part rh { clef treble }
        part lh { clef bass }

        section Main {
          lh { c4 d e f | }
          rh { c'1 | }
        }

        form main { ~Main }

        score main {
          staff rh
          staff lh
        }
        """;

    private static List<string> RenderPathPitches(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var collector = SemanticValidation.TryCollect(tree);
        Assert.NotNull(collector);
        return collector!.PitchTrace.Select(e => e.Pitch).OrderBy(p => p).ToList();
    }

    private static List<string> BareCollectPitches(string source)
    {
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(source));
        return collector.PitchTrace.Select(e => e.Pitch).OrderBy(p => p).ToList();
    }

    [Fact]
    public void EachPart_ResolvesFromItsOwnClefAnchor()
    {
        // c'1 under a treble anchor is C5; c d e f under a bass anchor is C3 D3 E3 F3.
        Assert.Equal(
            new[] { "C3", "C5", "D3", "E3", "F3" },
            RenderPathPitches(TwoParts));
    }

    [Fact]
    public void TheTrace_DoesNotDependOnThePartBlockOrder()
    {
        // The page does not depend on it, so neither may the report that claims to
        // describe the page.
        Assert.Equal(RenderPathPitches(TwoParts), RenderPathPitches(Swapped));
    }

    [Fact]
    public void TheBareCollect_DidDependOnThatOrder()
    {
        // The positive control, and the reason the two tests above are not vacuous: the
        // collect this report used to use gives a DIFFERENT answer for the same piece
        // written in a different order. Without this, "the two orders agree" could pass
        // on a report that was wrong in both.
        Assert.NotEqual(BareCollectPitches(TwoParts), BareCollectPitches(Swapped));
    }

    [Fact]
    public void TheBareCollect_MisreadsTheSecondPart()
    {
        // Naming the actual wrong answer, so a future change that "fixes" the bare
        // collect has to come here and say so. The bass part reads a fifth-and-two-
        // octaves too high: C5 D5 E5 F5 instead of C3 D3 E3 F3, because it carries on
        // from where the treble part's chain left off.
        Assert.Equal(
            new[] { "C5", "C5", "D5", "E5", "F5" },
            BareCollectPitches(TwoParts));
        Assert.DoesNotContain("C3", BareCollectPitches(TwoParts));
        Assert.Contains("C3", RenderPathPitches(TwoParts));
    }

    // ---- every score, not only the first --------------------------------------------

    // ⚠️ `a` and `b` cannot be part names — they are pitch names, and the parser says so
    // ("Expected a name, found 'a', a reserved word"). The first draft of this fixture
    // used them and produced a two-entry trace of nonsense; RULES §5.5's "part 名に予約語
    // を避ける" is about exactly this.
    private const string TwoScores =
        """
        time 4/4

        part sopr  { clef treble }
        part basso { clef bass }

        section Main {
          sopr  { c'1 | }
          basso { c4 d e f | }
        }

        form main { ~Main }

        score main "only-sopr"  { staff sopr }
        score main "only-basso" { staff basso }
        """;

    // The same two parts, both drawn by ONE score. Whatever the fold reports for the
    // two-score spelling above, it must equal this — that is the property, and it is
    // stronger than any list of pitches I could write out by hand.
    private const string OneScore =
        """
        time 4/4

        part sopr  { clef treble }
        part basso { clef bass }

        section Main {
          sopr  { c'1 | }
          basso { c4 d e f | }
        }

        form main { ~Main }

        score main {
          staff sopr
          staff basso
        }
        """;

    // The shipped answer, not a copy of it in the test — `check --pitches` prints exactly
    // this list.
    private static List<string> WholeFile(string source)
    {
        var trace = ResolvedPitches.ForFile(SyntaxTree.Parse(source));
        Assert.NotNull(trace);
        return trace!.Select(e => e.Pitch).ToList();
    }

    [Fact]
    public void EveryScoreIsRead_NotOnlyTheFirst()
    {
        // Two scores that each draw one part must read the piece exactly as one score
        // drawing both parts does. Stopping at the first score loses coverage —
        // measured on `test/cue-notes`, 26 notes across its scores against 8 in the
        // first alone.
        Assert.Equal(WholeFile(OneScore), WholeFile(TwoScores));
    }

    [Fact]
    public void TheFirstScoreAlone_WouldHaveReportedOneNote()
    {
        // The positive control for the test above: the two spellings agreeing means
        // nothing unless the FIRST score really does leave most of the piece out.
        var tree = SyntaxTree.Parse(TwoScores);
        var specs = RenderSpecParser.FindAll(tree);
        Assert.Equal(2, specs.Count);

        Assert.Single(SemanticValidation.TryCollect(tree, specs[0])!.PitchTrace);
        Assert.Equal(5, WholeFile(TwoScores).Count);
    }

    [Fact]
    public void ANoteDrawnTwice_IsReportedOnce()
    {
        // A tab book draws each written note on both the notation staff and the tab
        // staff. Without the fold onto the written position every tab fixture doubled —
        // `test/tab-percent-repeat` reports 32 written notes against 64 drawn ones.
        const string tabbed =
            """
            time 4/4

            part gtr { clef treble }

            section Main {
              gtr { c4 d e f | }
            }

            form main { ~Main }

            score main {
              staff gtr
              tab gtr
            }
            """;

        Assert.Equal(new[] { "C4", "D4", "E4", "F4" }, WholeFile(tabbed));
    }

    // ---- group octave marks under `octave absolute` ----------------------------------

    // The marks AFTER a closing '>' / '>>' move the WHOLE group an octave each. In
    // ABSOLUTE mode the drawn page has always done this correctly; the trace did not,
    // because the shift was applied to the ResolvedPitch that ResolveAbsolutePitch had
    // ALREADY recorded. So these tests state the property that matters and cannot be
    // satisfied by agreeing with a wrong page: the note the report NAMES must be the
    // note the collector PLACES, read off the same walk.

    private static string Book(string body, bool absolute) =>
        (absolute ? "octave absolute\n" : "")
        + "time 4/4\npart m { clef treble }\n"
        + $"section A {{ m {{ {body} }} }}\n"
        + "form main { A }\nscore main { staff m }";

    /// <summary>The drawn staff positions of the first group in the book, and the pitches
    /// the trace reports for it — the page and the report, from one collect.</summary>
    private static (int[] Drawn, string[] Reported) DrawnAndReported(string body, bool absolute)
    {
        var collector = new MeasureCollector();
        var score = collector.Collect(SyntaxTree.Parse(Book(body, absolute)), "m");
        var items = score.Voice.Measures.SelectMany(m => m.Items).ToList();
        var chord = items.OfType<LilySharp.Core.Svg.Model.ChordItem>().FirstOrDefault();
        int[] drawn = chord != null
            ? chord.Notes.Select(n => n.StaffPosition).ToArray()
            : items.OfType<LilySharp.Core.Svg.Model.NoteItem>().Select(n => n.StaffPosition).ToArray();
        return (drawn, collector.PitchTrace.Select(e => e.Pitch).ToArray());
    }

    // Treble staff positions, 0 = middle line (B4): C4 = −6, C5 = 1, C3 = −13.
    // Read off the drawing before being written here — noteheads at y 17.35/16.35/15.35
    // (plain), 13.85/12.85/11.85 (with '), 20.85/19.85/18.85 (with ,) against staff lines
    // 12.35…16.35, one staff space apart.
    [Theory]
    [InlineData("<c e g>2",  new[] { -6, -4, -2 }, new[] { "C4", "E4", "G4" })]
    [InlineData("<c e g>'2", new[] {  1,  3,  5 }, new[] { "C5", "E5", "G5" })]
    [InlineData("<c e g>,2", new[] { -13, -11, -9 }, new[] { "C3", "E3", "G3" })]
    public void AnAbsoluteChordsGroupMark_IsReportedWhereItIsDrawn(
        string body, int[] drawn, string[] reported)
    {
        var actual = DrawnAndReported(body, absolute: true);
        Assert.Equal(drawn, actual.Drawn);
        Assert.Equal(reported, actual.Reported);
    }

    [Theory]
    [InlineData("<< c e g >>'4 r2.", new[] {  1,  3,  5 }, new[] { "C5", "E5", "G5" })]
    [InlineData("<< c e g >>,4 r2.", new[] { -13, -11, -9 }, new[] { "C3", "E3", "G3" })]
    public void AnAbsoluteArpeggiosGroupMark_IsReportedWhereItIsDrawn(
        string body, int[] drawn, string[] reported)
    {
        // The arpeggio's ROOT took the ShiftOctave path while its stacked members got the
        // shift through the anchor, so the report used to name a chord that is not the
        // page and cannot be written at all: C4 E5 G5 for `<< c e g >>'`.
        var actual = DrawnAndReported(body, absolute: true);
        Assert.Equal(drawn, actual.Drawn);
        Assert.Equal(reported, actual.Reported);
    }

    [Theory]
    [InlineData("<c e g>2")]
    [InlineData("<c e g>'2")]
    [InlineData("<c e g>,2")]
    [InlineData("<< c e g >>'4 r2.")]
    [InlineData("<< c e g >>,4 r2.")]
    public void TheTwoOctaveModes_DrawAndReportTheSameGroup(string body)
    {
        // The two spellings are byte-identical as pages (measured: data-pos masked, the
        // absolute book hashes to its relative twin for all three chord spellings). A
        // report that describes the page therefore cannot tell them apart either.
        var relative = DrawnAndReported(body, absolute: false);
        var absolute = DrawnAndReported(body, absolute: true);

        // Element-wise: a ValueTuple holding arrays compares them by REFERENCE, so
        // asserting the tuples would fail on equal contents (and, worse, could pass on
        // nothing if both sides were ever the same array).
        Assert.Equal(relative.Drawn, absolute.Drawn);
        Assert.Equal(relative.Reported, absolute.Reported);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheGroupMarkMovesTheGroup_InBothModes(bool absolute)
    {
        // The positive control for the pair above, and the reason it is not vacuous: if
        // the mark were dropped in BOTH modes the two would agree on a wrong answer. It
        // has to actually move something, in the page AND in the report.
        var plain  = DrawnAndReported("<c e g>2", absolute);
        var up     = DrawnAndReported("<c e g>'2", absolute);
        var down   = DrawnAndReported("<c e g>,2", absolute);

        Assert.Equal(plain.Drawn.Select(p => p + 7), up.Drawn);
        Assert.Equal(plain.Drawn.Select(p => p - 7), down.Drawn);
        Assert.NotEqual(plain.Reported, up.Reported);
        Assert.NotEqual(plain.Reported, down.Reported);
    }
}
