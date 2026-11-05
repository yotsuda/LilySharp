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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A staff's music as that staff holds it: a note <c>@cross</c> moved stays in its voice's items
/// but is not on this staff, and its position is the other staff's. Whatever asks "which heads
/// stand here" reads this view, where the moved note is silence of the same length, at the same
/// index.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/rest-collision.cc:244-267 calc_positioning_done,
///   lily/note-collision.cc check_meshing_chords, lily/beam-collision-engraver.cc:115-194
///   finalize — all per Staff context, so a Voice that left this Staff is not among them.
/// MEASURED (OMR REQUESTS #21; Lab sessions/p1093/v1): read in place, a lower voice's rest fell
/// five spaces clear of a note that stood on the other staff, its beam ran 2.6 spaces long, and
/// its head stepped aside for a false second.
/// </remarks>
internal static class HeldHere
{
    /// <summary><paramref name="measure"/> without the notes standing on another staff; the same
    /// instance when it has none.</summary>
    public static Measure Of(Measure measure)
    {
        MusicItem[]? copy = null;
        var items = measure.Items;
        for (int i = 0; i < items.Length; i++)
            if (items[i].CrossStaffIndex >= 0)
                (copy ??= items.ToArray())[i] = new RestItem(items[i].Duration, 0, items[i].SourcePosition) { IsSpacer = true };
        return copy is null ? measure : measure with { Items = ImmutableArray.Create(copy) };
    }

    /// <summary><paramref name="voices"/> as <see cref="Of(Measure)"/> reads them; the same array
    /// when no note moved.</summary>
    public static ImmutableArray<Voice> Of(ImmutableArray<Voice> voices)
    {
        Voice[]? copy = null;
        for (int v = 0; v < voices.Length; v++)
        {
            var measures = voices[v].Measures;
            Measure[]? rebuilt = null;
            for (int m = 0; m < measures.Length; m++)
            {
                var here = Of(measures[m]);
                if (!ReferenceEquals(here, measures[m]))
                    (rebuilt ??= measures.ToArray())[m] = here;
            }
            if (rebuilt != null)
                (copy ??= voices.ToArray())[v] = voices[v] with { Measures = ImmutableArray.Create(rebuilt) };
        }
        return copy is null ? voices : ImmutableArray.Create(copy);
    }
}
/// <summary>
/// One item's place in a note column shared across staves by <c>@cross</c>: its voice's
/// <see cref="Voice.CrossCollisions"/>. The column is solved once, on the staff the heads stand
/// on, with that staff's own voices and the notes moved onto it; each voice keeps the answer for
/// its own items, so the staff that draws an item reads it from its own collision table.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/note-collision.cc:403-472 calc_positioning_done — the NoteCollision grob is
///   the Staff's (Collision_engraver), so a Voice that <c>\change Staff</c> moved in collides
///   with the voices it now stands among.
/// MEASURED, LilyPond 2.26.0 (Lab sessions/p1111/x1): `e''4@stemUp` over `d''4@cross@stemDown`
///   sets the down head beside the up one, as in one staff's two voices.
/// </remarks>
/// <param name="MeasureIndex">The measure of the item.</param>
/// <param name="ItemIndex">The item's index in its own voice's measure.</param>
/// <param name="XOffset">Its column's shift, in staff spaces.</param>
/// <param name="HeadTransparent">Whether a merge wiped its head.</param>
public readonly record struct CrossCollisionMark(int MeasureIndex, int ItemIndex, double XOffset, bool HeadTransparent)
{
    /// <summary>Whether any of <paramref name="voices"/> shares a column with a moved note — a staff
    /// of one voice then still has a collision answer.</summary>
    internal static bool AnyIn(ImmutableArray<Voice> voices)
    {
        foreach (var voice in voices)
            if (!voice.CrossCollisions.IsEmpty)
                return true;
        return false;
    }
}

/// <summary>
/// One note standing on a staff that is not its voice's (<see cref="Staff.CrossedIn"/>): the
/// address in that voice's measures, where its column is read.
/// </summary>
public sealed record CrossedNote(ImmutableArray<Measure> Measures, int MeasureIndex, int ItemIndex)
{
    /// <summary>The moved note.</summary>
    public MusicItem Item => Measures[MeasureIndex].Items[ItemIndex];

    /// <summary>Its column's note-collision shift among the heads it stands with
    /// (<see cref="CrossCollisionMark"/>), 0 when it shares no column — what the staff it stands
    /// on spaces it at.</summary>
    public double CollisionShift { get; init; }

    // Identity, not value equality: see ModelIdentity.
    public bool Equals(CrossedNote? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}

/// <summary>
/// Marks a note/chord that should be rendered on a different staff
/// than the voice it belongs to (cross-staff notation).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:1496-1507 - Beam::is_cross_staff
/// LILYPOND-REF: lily/stem.cc:1279-1283 - Stem::is_cross_staff
///
/// In LilyPond, cross-staff is achieved with \change Staff = "name".
/// In LilySharp, the @cross annotation on a note marks it for rendering
/// on the other staff in a grand staff context.
///
/// Common use case: piano music where notes from one hand temporarily
/// appear on the other staff, connected by a beam.
/// </remarks>
public sealed record CrossStaffItem(
    // Measure containing the cross-staff note.
    int MeasureIndex,

    // Item index within the measure.
    int ItemIndex,

    // Source position for diagnostics.
    int SourcePosition
)
{
    // Identity, not value equality: see ModelIdentity.
    public bool Equals(CrossStaffItem? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override int GetHashCode() => ModelIdentity.HashOf(this);
}
