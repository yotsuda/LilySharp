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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tuplet bracket addresses its notes by INDEX INTO ITS OWN STREAM'S measure, so a bracket
/// belonging to another staff (or another voice) is meaningless in this stream — and yet
/// resolves against it perfectly happily, because the indices are in range. This is the
/// instrument for the rule that stops that (<see cref="TupletBracketItem.AddressedTo"/>).
/// </summary>
/// <remarks>
/// ⚠️ THE CORPUS IS NOT THE INSTRUMENT, AND THAT WAS MEASURED, NOT ASSUMED (session 193).
/// Blanking either scoped caller's bracket list entirely — the collect-time stem-direction
/// probe, and the annotation quantity — moved 0 of 566 books; blanking the DRAWN path's list
/// (<c>MultiStaffLayouter.StaffBeamScoreOf</c>, which was already scoped) moved 2
/// (<c>autobeam-tuplet-recheck</c>, <c>beamlet-test</c>). So the sweep is NOT blind to the
/// mechanism: it sees tuplets move beamlets, and simply has no book that writes a foreign
/// bracket over a beam that would notice. A 0-book A/B here would have said nothing either
/// way (HANDOFF RULES §5.3), which is why the equation is asserted directly.
/// <para>
/// ⚠️ AND THE OUTPUT DOES NOT MOVE TODAY. The BEAMS THAT GET DRAWN come from the staff
/// quantity, whose list was always staff-scoped; the two callers fixed here feed a stem
/// direction bake and an annotation pass, and the only thing a tuplet list can change in a
/// detection is beamlet COUNTS. The book below renders byte-identically before and after.
/// The defect is therefore a live trap rather than a live bug — the next consumer to read a
/// beamlet count off either quantity inherits it silently.
/// </para>
/// <para>
/// ⚠️ THE COLLECT-TIME PROBE HAS NO NET OF ITS OWN, ON PURPOSE. Everything it bakes —
/// stem directions, beam identities, pure stem tips — is a function of group membership and
/// head positions, and a bracket list can move neither, so no assertion about its output can
/// tell a scoped list from an unscoped one (measured above: 0 of 566, and the falsifying
/// book below renders byte-identically either way). A net written anyway would pass for the
/// wrong reason (RULES §5.4). What holds that caller is that it does not spell the rule: it
/// calls <see cref="TupletBracketItem.AddressedTo"/>, which
/// <see cref="AddressedTo_KeepsOnlyItsOwnStreamsBrackets"/> pins on both of its axes.
/// </para>
/// </remarks>
public class ForeignTupletBracketTests
{
    /// <summary>
    /// The upper staff's first beam is <c>c16 d16. e32 f16</c> — one beat, and its e32 is the
    /// single interior stem that gets a flag direction and a chip. The LOWER staff's triplet
    /// opens at item index 2, so read as an index into the UPPER staff a span STARTS on that
    /// stem's moment, and <c>flag_directions</c> skips a stem standing at a span boundary.
    /// </summary>
    /// <remarks>
    /// Finding a shape that discriminates at all took measuring: a tuplet no longer bounds a
    /// beam (see <c>BeamDetector.DetectBeamGroups</c>), so a bracket reaches the answer only
    /// through the span-boundary beamlet rules and the written-proportion ranking — of nine
    /// hand-written two-staff shapes, this one and its mirror were the only candidates and
    /// only this one answered differently.
    /// </remarks>
    private const string TwoStaffBook = """
        time 4/4
        key c major
        part rh { clef treble }
        part lh { clef bass }
        section Main {
          rh { c16 d16. e32 f16 g16 r2 r8 r16 | }
          lh { c,16 d,16 tuplet 3/2 { e,16 f,16 g,16 } r2. | }
        }
        form main { Main }
        score main "x" { grandStaff { staff rh staff lh } }
        """;

    /// <summary>
    /// THE EQUATION: what the annotation quantity detects must not depend on a bracket that
    /// belongs to another staff. Asserted after first showing that this book's foreign
    /// bracket DOES change the detection — a book the detector answers identically would make
    /// any scoping rule look sound, including no rule at all.
    /// </summary>
    [Fact]
    public void TheAnnotationQuantityDetectsWithoutAnotherStaffsBracket()
    {
        var score = Collect(TwoStaffBook);
        var (staff, staffIndex) = score.PrimaryContentStaffWithIndex();
        Assert.False(score.TupletBrackets.IsDefaultOrEmpty, "the book carries no bracket — vacuous");
        Assert.DoesNotContain(score.TupletBrackets, t => t.StaffIndex == staffIndex);

        // The score the annotation pass travels in: the primary voice against the WHOLE
        // score's bracket list, because that pass draws every bracket from it.
        var drawingScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature, "treble",
            tupletBrackets: score.TupletBrackets);
        var detectionScore = LayoutEngine.DetectionScoreFor(
            drawingScore, staff, score, staffIndex);

        // ⑴ The case discriminates: the foreign bracket really does move this staff's answer.
        var withForeign = Surface(new BeamDetector().DetectBeamGroups(drawingScore));
        var withoutForeign = Surface(new BeamDetector().DetectBeamGroups(detectionScore));
        Assert.NotEqual(withForeign, withoutForeign);

        // ⑵ ...and the detection input is the one WITHOUT it — which is also the answer the
        // staff would give standing alone, with no lower staff in the book at all.
        Assert.Empty(detectionScore.TupletBrackets);
        var alone = Collect(TwoStaffBook.Replace(
            "rh { c16 d16. e32 f16 g16 r2 r8 r16 | }\n  lh { c,16 d,16 tuplet 3/2 { e,16 f,16 g,16 } r2. | }",
            "rh { c16 d16. e32 f16 g16 r2 r8 r16 | }"));
        var (aloneStaff, _) = alone.PrimaryContentStaffWithIndex();
        Assert.Equal(
            Surface(new BeamDetector().DetectBeamGroups(new Score(
                aloneStaff.PrimaryVoice, alone.TimeSignature, alone.KeySignature, "treble"))),
            withoutForeign);
    }

    /// <summary>
    /// The scoping rule itself, on the two axes it has to cut on: another staff's brackets and
    /// another voice's, out of one flat list — the shape both callers hold theirs in.
    /// </summary>
    [Fact]
    public void AddressedTo_KeepsOnlyItsOwnStreamsBrackets()
    {
        var all = ImmutableArray.Create(
            Bracket(staffIndex: 0, voiceIndex: 0),
            Bracket(staffIndex: 0, voiceIndex: 1),
            Bracket(staffIndex: 1, voiceIndex: 0),
            Bracket(staffIndex: 1, voiceIndex: 1));

        Assert.Equal(
            new[] { (0, 0) },
            TupletBracketItem.AddressedTo(all, 0, 0).Select(t => (t.StaffIndex, t.VoiceIndex)));
        Assert.Equal(
            new[] { (1, 1) },
            TupletBracketItem.AddressedTo(all, 1, 1).Select(t => (t.StaffIndex, t.VoiceIndex)));
        Assert.Empty(TupletBracketItem.AddressedTo(all, 2, 0));
        Assert.Empty(TupletBracketItem.AddressedTo(
            ImmutableArray<TupletBracketItem>.Empty, 0, 0));
    }

    /// <summary>
    /// A single-staff book's own list survives whole — the rule must not cost the common case
    /// its brackets, and it must hand the caller back its OWN score instance so the annotation
    /// quantity and the staff quantity go on sharing one detection through the input-keyed
    /// memo (<c>MultiStaffLayouter.BeamGroupsOf</c>, session 192).
    /// </summary>
    [Fact]
    public void ASingleStaffBooksOwnBracketsAreKept_AndTheCallerGetsItsOwnScoreBack()
    {
        var score = Collect("""
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c16 tuplet 3/2 { d16. e32 f16 } g16 |
            } }
            form main { Main }
            score main "x" { staff melody }
            """);
        var (staff, staffIndex) = score.PrimaryContentStaffWithIndex();
        Assert.False(score.TupletBrackets.IsDefaultOrEmpty, "the book carries no bracket — vacuous");

        var drawingScore = new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature, "treble",
            tupletBrackets: score.TupletBrackets);
        Assert.Same(drawingScore,
            LayoutEngine.DetectionScoreFor(drawingScore, staff, score, staffIndex));
    }

    // ---------- helpers ----------

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static TupletBracketItem Bracket(int staffIndex, int voiceIndex) =>
        new(3, 2, StartNoteIndex: 0, EndNoteIndex: 2, MeasureIndex: 0, SourcePosition: 0,
            NestingDepth: 0, StaffIndex: staffIndex, VoiceIndex: voiceIndex);

    /// <summary>The whole of a detection's answer, as a string, so two answers can be
    /// compared and a difference can be read. The beamlet counts are the point here.</summary>
    private static string Surface(ImmutableArray<BeamGroup> groups) =>
        string.Join(";", groups.Select(g =>
            $"{g.MeasureIndex}:{g.StartIndex}:{(g.StemUp ? 'u' : 'd')}:{g.GrowDirection}:{g.VoiceIndex}"
            + "|" + string.Join(",", g.Members.Select(m =>
                $"{m.ResolveMeasureIndex(g.MeasureIndex)}.{m.ItemIndex}"
                + $".{(m.MemberStemUp ? 'u' : 'd')}.{m.BeamCount}.{m.BeamCountLeft}.{m.BeamCountRight}"))));
}
