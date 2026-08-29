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
/// A staff shared by several parts (<c>condensedStaff</c>) numbers its voices ACROSS the
/// parts — <c>Staff.Voices</c> is their concatenation — while the collector walks one part
/// at a time. The number it stamps on every item it addresses by voice must be the item's
/// place in THAT array, because that is what every consumer reads it as
/// (<c>score.Voices[tie.VoiceIndex]</c>, <c>AnchorItem(voices, dynamic.VoiceIndex, …)</c>,
/// <c>BeamDetector</c>'s <c>t.VoiceIndex == v</c>). This is the instrument for that.
/// </summary>
/// <remarks>
/// ⚠️ THE CORPUS IS BLIND TO THIS AND THAT WAS MEASURED, NOT ASSUMED (session 284). The fix
/// moved 0 of the 899-book sweep (573 tracked + 326 author books) while the falsifying book
/// below moves — so the sweep sees the mechanism and simply has no book that writes the
/// shape. Only seven corpus books write <c>condensedStaff</c> at all, and none of them
/// gives its later parts a tuplet, a dynamic or an articulation. A 0-book A/B would have
/// said nothing either way (HANDOFF RULES §5.3), which is why the equations are asserted
/// against the part standing ALONE rather than against a snapshot.
/// <para>
/// ⚠️ WHAT A READER SAW BEFORE THE FIX, in the book below: the lower part's triplet number
/// was engraved ABOVE the staff over the UPPER part's notes (x 29.51, y 8.29 — the upper
/// part's beam is the only thing up there), and its <c>@f</c> sat under the upper part's
/// first note. Both now land on the notes that wrote them. The page got 2.70 shorter,
/// because the bracket that had been hoisted above the staff was reserving room there.
/// </para>
/// </remarks>
public class SharedStaffVoiceSlotTests
{
    /// <summary>
    /// BOTH parts are polyphonic, so the staff's voices are <c>[rh, rh.2, lh, lh.2]</c> and
    /// the two numbers this pins are different ones: the lower part's BASE is two (not one
    /// — the arithmetic is "everything already placed on this staff", not "the binding's
    /// ordinal"), and its second voice is base PLUS its own span slot, three. Its triplet
    /// and its <c>@f</c> ride the two islands that carry a collected voice number, and they
    /// are put in DIFFERENT voices of the lower part so one number cannot cover for the
    /// other.
    /// </summary>
    private const string FourVoiceBook = """
        octave absolute
        time 4/4
        key c major
        part rh { clef treble }
        part lh { clef treble }
        section Main {
          rh { voice { c''2 r4 g''8 a''8 | } voice { e'2 r2 | } }
          lh { voice { c'4@f e' g' a' | } voice { g2 tuplet 3/2 { c'8 d' e' } r4 | } }
        }
        form main { Main }
        score main "x" { condensedStaff { rh lh } }
        """;

    /// <summary>
    /// THE EQUATION: the number stamped on a shared staff's later part addresses THAT
    /// part's stream. Read through the staff's own voice array, which is the only place
    /// the number means anything.
    /// </summary>
    [Fact]
    public void ALaterPartOnASharedStaffIsAddressedByItsPlaceInTheStaffsVoices()
    {
        var score = Collect(FourVoiceBook);
        var staff = score.EnumerateStaves().Single().Staff;
        Assert.Equal(new[] { "rh", "rh.2", "lh", "lh.2" }, staff.Voices.Select(v => v.Name));

        // ⑴ The dynamic sits in lh's FIRST voice, so it names the part's BASE — 2, past
        //    BOTH of rh's. The anchor is readable as a note: lh opens on c' where rh's two
        //    voices open on c'' and e', so a wrong slot cannot pass by landing on an
        //    identical head.
        var dynamic = Assert.Single(score.Dynamics);
        Assert.Equal(0, dynamic.StaffIndex);
        Assert.Equal(2, dynamic.VoiceIndex);
        var anchor = LayoutUtilities.VoiceItemAt(
            staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex);
        Assert.Equal(
            StaffPositionOf(staff.Voices[2], dynamic.MeasureIndex, dynamic.ItemIndex),
            StaffPositionOf(anchor));
        Assert.NotEqual(
            StaffPositionOf(staff.Voices[0], dynamic.MeasureIndex, dynamic.ItemIndex),
            StaffPositionOf(anchor));

        // ⑵ The bracket sits in lh's SECOND voice, so it names base PLUS the span slot —
        //    3, the last voice on the staff. This is the number the base alone cannot give.
        var bracket = Assert.Single(score.TupletBrackets);
        Assert.Equal(0, bracket.StaffIndex);
        Assert.Equal(3, bracket.VoiceIndex);
    }

    /// <summary>
    /// ...and the consequence the drawn page shows: the first part's beams are detected
    /// from the first part's tuplets only. Asserted against the SAME music standing alone,
    /// after first showing the case discriminates — a book whose beams do not move would
    /// make any slotting look sound, including none at all.
    /// </summary>
    /// <remarks>
    /// The shape is the one <see cref="ForeignTupletBracketTests"/> measured for two STAVES
    /// (its remark carries why this and its mirror were the only two of nine hand-written
    /// candidates that answer differently): the upper part's <c>c16 d16. e32 f16</c> has one
    /// interior stem that takes a flag direction, and a span STARTING on that stem's moment
    /// turns its beamlet round, because <c>flag_directions</c> skips a stem standing at a
    /// span boundary. Read as an index into the upper part, the lower part's triplet opens
    /// exactly there.
    /// </remarks>
    [Fact]
    public void TheFirstPartsBeamletIsUnmovedByTheSecondPartsTuplet()
    {
        const string upper = "c16 d16. e32 f16 g16 r2 r8 r16 |";
        var shared = Collect($$"""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef treble }
            section Main {
              rh { {{upper}} }
              lh { c'16 d' tuplet 3/2 { e' f' g' } r2. | }
            }
            form main { Main }
            score main "x" { condensedStaff { rh lh } }
            """);
        var staff = shared.EnumerateStaves().Single().Staff;
        var bracket = Assert.Single(shared.TupletBrackets);
        Assert.Equal(1, bracket.VoiceIndex);

        var drawn = Surface(new BeamDetector().DetectBeamGroups(
            MultiStaffLayouter.StaffBeamScoreOf(shared, staff, 0)));

        // ⑴ The case discriminates: hand the SAME detection the unslotted list — every
        //    bracket at slot 0, which is what the collector stamped before session 284 —
        //    and the first part's answer changes.
        var unslotted = new Score(
            staff.Voices, shared.TimeSignature, shared.KeySignature, "treble",
            tupletBrackets: shared.TupletBrackets
                .Select(t => t with { VoiceIndex = 0 }).ToImmutableArray());
        Assert.NotEqual(drawn, Surface(new BeamDetector().DetectBeamGroups(unslotted)));

        // ⑵ ...and the slotted answer for the first part is the one it gives with no second
        //    part in the book at all.
        var alone = Collect($$"""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            section Main { rh { {{upper}} } }
            form main { Main }
            score main "x" { staff rh }
            """);
        var aloneStaff = alone.EnumerateStaves().Single().Staff;
        Assert.Empty(alone.TupletBrackets);
        var aloneSurface = Surface(new BeamDetector().DetectBeamGroups(
            MultiStaffLayouter.StaffBeamScoreOf(alone, aloneStaff, 0)));
        Assert.Equal(aloneSurface, FirstVoiceOf(drawn));
    }

    /// <summary>
    /// The three slottings, off the render spec itself: what the collector is told about
    /// each binding before it walks a note.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE COMBINER'S SECOND PART IS PINNED AS A DEBT, NOT AS AN ANSWER. It keeps slot
    /// 0 — the number it had before the slotting existed — because no slot is derivable
    /// from the part alone once <c>PartCombiner.Combine</c> has rewritten both streams: it
    /// MOVES items between them and emits a SINGLE voice when the parts are never apart, so
    /// neither the voice number nor the item index the part was collected with survives.
    /// MEASURED (session 284): in a two-part book where the parts are apart the whole bar,
    /// a <c>@f</c> written on the second part still anchors to the FIRST part's note under
    /// <c>combinedStaff</c>, exactly as it did under <c>condensedStaff</c> before this fix.
    /// Closing it is its own trip (HANDOFF §2 A); this test is what makes a change to it
    /// deliberate rather than incidental.
    /// </remarks>
    [Theory]
    [InlineData("condensedStaff", VoiceSlotting.AppendedToStaff)]
    [InlineData("combinedStaff", VoiceSlotting.CombinedIntoStaff)]
    public void ASharedStaffOpensOnceAndSlotsItsLaterPartsByHowTheStaffIsBuilt(
        string kind, VoiceSlotting later)
    {
        var tree = SyntaxTree.Parse($$"""
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef treble }
            section Main { rh { c''1 | } lh { c'1 | } }
            form main { Main }
            score main "x" { {{kind}} { rh lh } }
            """);
        var slots = RenderSpecParser.FindFirst(tree).GetVoiceBindings()
            .Select(b => (b.VoiceName, b.Slotting)).ToArray();
        Assert.Equal(new[] { ("rh", VoiceSlotting.OwnStaff), ("lh", later) }, slots);
    }

    /// <summary>
    /// A staff of its own still starts at slot 0, and its <c>voice { } { }</c> spans still
    /// count from there — the arithmetic the shared case generalises must not cost the
    /// common case its numbering. Two staves, so a wrong base would show as a staff's
    /// second voice claiming a slot the staff does not have.
    /// </summary>
    [Fact]
    public void StavesOfTheirOwnKeepCountingFromZero()
    {
        var score = Collect("""
            octave absolute
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef bass }
            section Main {
              rh { voice { tuplet 3/2 { c''8 d'' e'' } r2. | } voice { e'1 | } }
              lh { voice { tuplet 3/2 { c8 d e } r2. | } voice { e,1 | } }
            }
            form main { Main }
            score main "x" { grandStaff { staff rh staff lh } }
            """);
        Assert.Equal(
            new[] { (0, 0), (1, 0) },
            score.TupletBrackets.Select(t => (t.StaffIndex, t.VoiceIndex)).OrderBy(x => x));
        foreach (var (_, staff, index) in score.EnumerateStaves())
            foreach (var t in score.TupletBrackets.Where(t => t.StaffIndex == index))
                Assert.InRange(t.VoiceIndex, 0, staff.Voices.Length - 1);
    }

    // ---------- helpers ----------

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static int? StaffPositionOf(MusicItem? item)
        => item is NoteItem n ? n.StaffPosition : null;

    private static int? StaffPositionOf(Voice voice, int measureIndex, int itemIndex)
        => StaffPositionOf(LayoutUtilities.VoiceItemAt(
            ImmutableArray.Create(voice), 0, measureIndex, itemIndex));

    /// <summary>The whole of a detection's answer, as a string, so two answers can be
    /// compared and a difference can be read. The beamlet counts are the point here.</summary>
    private static string Surface(ImmutableArray<BeamGroup> groups) =>
        string.Join(";", groups.Select(g =>
            $"{g.MeasureIndex}:{g.StartIndex}:{(g.StemUp ? 'u' : 'd')}:{g.GrowDirection}:{g.VoiceIndex}"
            + "|" + string.Join(",", g.Members.Select(m =>
                $"{m.ResolveMeasureIndex(g.MeasureIndex)}.{m.ItemIndex}"
                + $".{(m.MemberStemUp ? 'u' : 'd')}.{m.BeamCount}.{m.BeamCountLeft}.{m.BeamCountRight}"))));

    /// <summary>The groups of voice 0 out of a whole staff's surface — the staff carries
    /// both parts' beams, the book standing alone carries only the first part's.</summary>
    private static string FirstVoiceOf(string surface) =>
        string.Join(";", surface.Split(';').Where(g => g.Split('|')[0].EndsWith(":0")));
}
