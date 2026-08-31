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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Where the heads and the accidentals of ONE grace column stand, in the column's own frame.
/// </summary>
/// <remarks>
/// THE one spelling: the reservation (<see cref="SpacingRules"/>), the skylines and the
/// renderer all ask here, so a column cannot be reserved at one width and drawn at another.
/// That is the same rule <c>StaffAccidentalColumns</c> states for the full-size staff column
/// and the reason <c>Semantics.GraceBodySupport</c> exists one level up.
/// <para>
/// ⚠️ THERE IS NO NEW GEOMETRY HERE. A grace chord is the ORDINARY chord rule read out of the
/// GRACE'S OWN FONTS — <see cref="ChordHeadPositioning"/> for the seconds and
/// <see cref="AccidentalPlacement"/> for the stacking, both of which already take the fonts
/// and neither of which takes an address. HANDOFF §2 U8 ⒜ measured that before this trip
/// started, and session 308 then measured LilyPond and got the same answer from the other
/// side (scratch/p308/lp):
/// <list type="bullet">
/// <item><c>\grace { &lt;c' d'&gt;16 }</c> — heads 16.1208 / 16.9738, a 0.8530 shift, against
/// 1.2392 for the full-size <c>&lt;c' d'&gt;4</c>. Both are <c>ell − thickness/2</c> read off
/// the right design: 1.298161·magstep(−3) and 1.304200.</item>
/// <item><c>\grace { &lt;cis' dis'&gt;16 }</c> — accidentals 16.2208 / 17.0831, i.e. STACKED,
/// 0.8623 apart against 1.3000 for the full-size chord. A grace chord's accidentals go
/// through <c>position_apes</c> like anyone else's, at the −4 font.</item>
/// <item><c>\grace { &lt;c' e'&gt;16 }</c> — both heads at 16.1208. No second, no shift.</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ WHAT IS NOT HERE IS THE CROSS-VOICE HALF. Two voices carrying SIMULTANEOUS graces stand
/// on ONE staff column in LilyPond, packed into one accidental column and shifted by
/// <c>Note_collision</c> — MEASURED (scratch/p308/lp, x6_gaccsec2v against x7_gaccchord: the
/// two voices' accidentals land at 16.2208 / 17.0831, the SAME pair the one-voice chord gets,
/// to four digits). Lily# draws those two heads on top of each other today, with no chord
/// anywhere in sight. That is a different repair — <see cref="NoteCollision"/> and
/// <c>Collector.StaffAccidentalColumns</c> are keyed on
/// <c>(measure, voice, item, note)</c> and a grace has no item index — and it is filed on its
/// own in docs/HANDOFF.md §2 rather than mixed in here.
/// </para>
/// </remarks>
internal static class GraceColumnHeads
{
    /// <summary>
    /// Grace stems are forced UP, whatever the pitches are, so every caller here reads one
    /// direction rather than deciding one.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:652-656 <c>score-grace-settings</c> —
    /// <c>((Voice Stem direction ,UP) (Voice Slur direction ,DOWN))</c>.
    /// </remarks>
    internal const bool StemUp = true;

    /// <summary>
    /// How far RIGHT of the column's origin each head is drawn, parallel to
    /// <see cref="GraceColumnInfo.Heads"/>. All zero for a single head and for a chord with
    /// no seconds.
    /// </summary>
    internal static ImmutableArray<double> HeadOffsets(GraceColumnInfo column)
    {
        // A rest and a single head both answer "nothing to move"; Heads.Length covers both,
        // because a rest's array is the empty one.
        if (column.Heads.IsDefaultOrEmpty || column.Heads.Length < 2)
            return ImmutableArray<double>.Empty;
        var offsets = ChordHeadPositioning.CalculateOffsets(
            AsChordNotes(column), StemUp,
            GlyphMetrics.NoteValueOf(column.BaseDuration),
            GraceNoteItem.Font);
        return ImmutableArray.Create(offsets);
    }

    /// <summary>
    /// The packed accidentals of one column: each head's accidental ink-left X in the
    /// column's frame, parallel to <see cref="GraceColumnInfo.Heads"/>, null where the head
    /// carries no accidental.
    /// </summary>
    /// <remarks>
    /// ⚠️ TWO FONTS, because the two grobs carry two font-sizes: the accidental is −4 and the
    /// head it clears is −3 (scm/music-functions.scm:635-648 general-grace-settings). Passing
    /// one font for both is what the <c>grace.column.*</c> ledger island used to carry.
    /// </remarks>
    internal static ImmutableArray<double?> AccidentalOffsets(GraceColumnInfo column)
    {
        if (column.Heads.IsDefaultOrEmpty)
            return ImmutableArray<double?>.Empty;
        var heads = column.Heads;
        var result = new double?[heads.Length];
        var notes = AsChordNotes(column);
        var layouts = new AccidentalPlacement().CalculatePositions(
            notes,
            headOffsets: HeadOffsetsOrNull(column),
            accidentalFont: GraceNoteItem.AccidentalFont,
            GraceNoteItem.Font);
        // position_apes answers per ACCIDENTAL, keyed on the staff position it belongs to;
        // the heads of one column are distinct positions (a unison writes one head), so the
        // position is the key back.
        foreach (var al in layouts)
        {
            for (int i = 0; i < heads.Length; i++)
            {
                if (heads[i].StaffPosition == al.StaffPosition && heads[i].Accidental != null)
                {
                    result[i] = al.XOffset;
                    break;
                }
            }
        }
        return ImmutableArray.Create(result);
    }

    /// <summary>
    /// How far the column's HEAD ink reaches right of its origin — the rightmost head's own
    /// right edge, which a reversed second pushes out.
    /// </summary>
    internal static double HeadInkRight(GraceColumnInfo column)
    {
        double ell = GlyphMetrics.GetNoteheadBBox(
            GraceNoteItem.Font, GlyphMetrics.NoteValueOf(column.BaseDuration)).Right;
        var offsets = HeadOffsets(column);
        if (offsets.IsDefaultOrEmpty)
            return ell;
        double max = ell;
        foreach (double o in offsets)
            max = System.Math.Max(max, o + ell);
        return max;
    }

    /// <summary>
    /// How far the column's ACCIDENTAL ink reaches LEFT of its origin, as a positive number,
    /// or 0 when the column carries none. The leftmost of the packed set — for a single head
    /// that is the one accidental, and for a chord it is whichever the stacking pushed out
    /// furthest.
    /// </summary>
    internal static double AccidentalInkLeft(GraceColumnInfo column)
    {
        double left = 0;
        foreach (double? x in AccidentalOffsets(column))
            if (x is { } v && v < 0)
                left = System.Math.Max(left, -v);
        return left;
    }

    /// <summary>
    /// The column as a full-size <see cref="MusicItem"/>, for the houses that take one and ask
    /// it only about pitches and duration — the beam quanter's <c>BeamMember.Item</c> and the
    /// spacing's approach column.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS A STAND-IN, NOT A CONVERSION: nothing downstream may read its SIZE off it,
    /// because this item carries the full-size font by construction. The two callers read the
    /// head positions and the note value, both of which are the same numbers at either size.
    /// A one-head column stands in as a <c>NoteItem</c> and a chord as a <c>ChordItem</c>, so
    /// the head RANGE reaches the readers that ask for it.
    /// <para>
    /// ⚠️ <paramref name="dots"/> DEFAULTS TO ZERO because both callers passed a literal 0
    /// before this type learned about chords, and a stand-in that started carrying the
    /// column's real dots would move single-note books for a reason unrelated to this trip.
    /// The dots are drawn from <c>GraceNoteEngraver.Dots</c>, which reads the column itself.
    /// </para>
    /// </remarks>
    internal static MusicItem StandIn(
        GraceColumnInfo column, int sourcePosition, bool? stemUpOverride = null, int dots = 0)
    {
        // A REST stands in as a RestItem, so a reader that asks what kind of thing this
        // column is gets the true answer rather than a note at position 0.
        if (column.IsRest)
            return new RestItem(column.BaseDuration, dots, sourcePosition)
            {
                IsSpacer = column.IsSpacer,
            };
        if (column.Heads.Length > 1)
        {
            var notes = ImmutableArray.CreateBuilder<ChordNoteInfo>(column.Heads.Length);
            foreach (var h in column.Heads)
                notes.Add(new ChordNoteInfo(h.StaffPosition, h.Accidental, h.NeedsLedger));
            return new ChordItem(notes.MoveToImmutable(), column.BaseDuration, dots,
                                 sourcePosition)
            {
                StemUpOverride = stemUpOverride,
            };
        }
        var head = column.Lowest;
        return new NoteItem(head.StaffPosition, column.BaseDuration, dots,
                            head.Accidental, head.NeedsLedger, sourcePosition)
        {
            StemUpOverride = stemUpOverride,
        };
    }

    /// <summary>The column's heads as the chord primitives the two shared houses read.</summary>
    /// <remarks>
    /// ⚠️ A TRANSLATION, NOT A SECOND MODEL. <c>ChordHeadPositioning</c> and
    /// <c>AccidentalPlacement</c> are the SAME engravers the full-size chord goes through;
    /// giving a grace its own copy of either is the "second spelling of one quantity" this
    /// repository's checklist 7.7 names as its most repeated defect.
    /// </remarks>
    private static ChordNoteInfo[] AsChordNotes(GraceColumnInfo column)
    {
        var notes = new ChordNoteInfo[column.Heads.Length];
        for (int i = 0; i < column.Heads.Length; i++)
        {
            var h = column.Heads[i];
            notes[i] = new ChordNoteInfo(
                h.StaffPosition, h.Accidental, h.NeedsLedger, IsCourtesy: false);
        }
        return notes;
    }

    private static double[]? HeadOffsetsOrNull(GraceColumnInfo column)
    {
        var offsets = HeadOffsets(column);
        return offsets.IsDefaultOrEmpty ? null : offsets.ToArray();
    }
}
