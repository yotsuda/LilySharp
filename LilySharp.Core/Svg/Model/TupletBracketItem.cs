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
using System.Collections.Immutable;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Represents a tuplet bracket with ratio information.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/tuplet-bracket.cc:1-400 Tuplet_bracket class
/// </remarks>
public sealed record TupletBracketItem(
    // Tuplet ratio numerator (e.g., 3 for triplets).
    int Numerator,
    // Tuplet ratio denominator (e.g., 2 for triplets).
    int Denominator,
    // Starting note index within the measure.
    int StartNoteIndex,
    // Ending note index within the measure.
    int EndNoteIndex,
    // Measure index containing this tuplet.
    int MeasureIndex,
    // Source position for click-to-source mapping.
    int SourcePosition,
    // Nesting depth for nested tuplets (0 = top-level, 1 = first nesting, etc.).
    // LILYPOND-REF: lily/tuplet-bracket.cc:400-500 nested bracket stacking
    int NestingDepth = 0,
    // Global staff index this tuplet belongs to (multi-staff routing;
    // see DynamicItem.StaffIndex). 0 for single-staff.
    int StaffIndex = 0,
    // Index of the voice (within its staff) that owns this tuplet.
    // 0 = primary. Auto-beaming breaks at a tuplet boundary only within the
    // SAME voice, so the beam detector must not apply one voice's tuplet
    // indices to another voice's note stream.
    // ⚠️ IT IS THE SLOT IN Staff.Voices, NOT THE VOICE'S NUMBER WITHIN ITS PART.
    // On a staff several parts share (condensedStaff) those differ: the staff's voices
    // are the parts' voices concatenated, so the second part's primary stream is slot N,
    // not slot 0. The collector counts it that way (RenderSpec.VoiceSlotting); this is
    // the same reading every consumer of a VoiceIndex uses, because they all index the
    // staff's array with it (score.Voices[tie.VoiceIndex], AnchorItem, and the filter in
    // AddressedTo below).
    int VoiceIndex = 0
)
{
    /// <summary>
    /// The brackets in <paramref name="all"/> that address ONE note stream — the voice
    /// <paramref name="voiceIndex"/> of staff <paramref name="staffIndex"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, BECAUSE A BRACKET'S INDICES ARE ONLY MEANINGFUL IN ITS OWN STREAM.
    /// <see cref="StartNoteIndex"/>/<see cref="EndNoteIndex"/> are positions in a measure's
    /// item array, and every stream has its own item array — so handed a foreign bracket the
    /// beam detector resolves it against the WRONG notes and gets a plausible answer for
    /// music that is not there. It cannot defend itself: <c>BeamDetector.BuildTupletSpans</c>
    /// drops only the OUT-OF-RANGE ones (and its remark says the in-range collision "closes
    /// only when the probe filters by staff/voice"). Every caller that hands the detector a
    /// SINGLE stream must therefore scope the list first, and this is where that is spelt —
    /// the two callers are <c>MeasureCollector.ProbeTupletBrackets</c> (the collect-time
    /// stem-direction probe, whose collector walks every staff into one list) and
    /// <c>LayoutEngine.DetectionScoreFor</c> (the annotation quantity, which takes the
    /// primary staff's primary voice against the whole score's list).
    /// <para>
    /// MEASURED (session 193): with the list unscoped, a triplet opening at index 2 of the
    /// LOWER staff turns the UPPER staff's thirty-second beamlet round — left/right 2/3
    /// where the staff's own list gives 3/2 — because the foreign span's start lands on that
    /// stem's moment and <c>flag_directions</c> skips a stem standing at a span boundary.
    /// The corpus does not observe it: blanking either caller's list entirely moved 0 of 566
    /// books, while blanking the DRAWN path's list (which was already scoped) moved 2, so the
    /// sweep sees the mechanism and simply has no book that writes the shape.
    /// <c>ForeignTupletBracketTests</c> is the instrument instead.
    /// </para>
    /// <para>
    /// ⚠️ THE FILTER IS ONLY AS GOOD AS THE NUMBERS IT READS, and until session 284 one of
    /// them lied. A <c>condensedStaff</c>'s parts share a staff index AND were all collected
    /// at voice slot 0, so this rule could not tell them apart and each part was scoped with
    /// the other's brackets — in range and silently, whenever the other part's bars were
    /// longer than one item. MEASURED (session 284): the second part's triplet number was
    /// engraved above the staff over the FIRST part's notes, and the first part's beamlet
    /// turned round exactly as the two-staff case above does. The repair is upstream, in what
    /// the collector stamps (<c>RenderSpec.VoiceSlotting</c>); this rule did not change.
    /// The corpus is blind to that one too — 0 of 899 books moved — so
    /// <c>SharedStaffVoiceSlotTests</c> is its instrument.
    /// </para>
    /// </remarks>
    internal static ImmutableArray<TupletBracketItem> AddressedTo(
        IReadOnlyList<TupletBracketItem> all, int staffIndex, int voiceIndex)
    {
        if (all.Count == 0)
            return ImmutableArray<TupletBracketItem>.Empty;
        var own = ImmutableArray.CreateBuilder<TupletBracketItem>(all.Count);
        foreach (var t in all)
            if (t.StaffIndex == staffIndex && t.VoiceIndex == voiceIndex)
                own.Add(t);
        return own.ToImmutable();
    }

    /// <summary>
    /// Gets the display text for the tuplet number (e.g., "3" for triplets).
    /// </summary>
    public string DisplayText => Numerator.ToString();
    
    /// <summary>
    /// Gets the tuplet duration factor (notes play faster than written).
    /// For triplets (3/2): each note plays at 2/3 of its written duration.
    /// </summary>
    public Fraction DurationFactor => new Fraction(Denominator, Numerator);
}
