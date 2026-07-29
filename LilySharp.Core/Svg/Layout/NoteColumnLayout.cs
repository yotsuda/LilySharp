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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The Y-side column/stem model: ONE house for "how far a note column reaches",
/// built from an item after its stem direction and beam membership are resolved
/// (i.e. AFTER beam quantization for a beamed column).
/// </summary>
/// <remarks>
/// HANDOFF §5.2.1②: session 34 counted FOUR homes of a column's vertical reach —
/// <c>TupletBracketEngraver.ColumnOutwardTip</c>, <c>ArticulationEngraver.StemSupportExtent</c>,
/// <c>DynamicEngraver</c>'s column support edges (<c>GetHighestExtent</c>/<c>GetLowestExtent</c>)
/// and <c>SkylineBuilder</c>'s stem seed. Each had its own spelling of one skeleton
/// (pick the head on the reach's side; no stem for a whole/breve; a beamed stem ends
/// on the quanted beam; an unbeamed one where the renderer draws it), and fidelity
/// ports kept landing on one home at a time — the raw 3.5 left the articulations in
/// session 29, the skyline seed in earlier ports, the tuplet bracket in session 34,
/// and still lives in the dynamics. This record is the single house: the skeleton is
/// written once, and the consumers' MODELS sit side by side as named reads, so the
/// next fidelity port is a read switch here, with its ledger point, instead of a
/// rewrite there.
/// <para>
/// ⚠️ OUTPUT-INVARIANT BY CONSTRUCTION: every read reproduces its consumer's
/// pre-existing arithmetic bit for bit. Where the models DISAGREE today the
/// disagreement is deliberately preserved and named here; changing any row of this
/// table is an output-moving port that needs its ledger point first (§5.2.1③):
/// </para>
/// <para>
/// ┌ read (consumer) ──────────────────────┬ stem model ───────────────────────────┬ head ink ─────────────────┐
/// │ <see cref="OutwardTipDeviceY"/>        │ quanted beam face at the stem's X,    │ glyph ink, bbox.Top used  │
/// │ (tuplet bracket encompass)             │ else the drawn stem end               │ for BOTH sides            │
/// │                                        │ (<see cref="StemCalculator.CalculateStemEndY"/>) │                │
/// │ <see cref="StemSupportDistanceDeviceY"/>│ the same two                         │ NOMINAL half head 0.5     │
/// │ (articulation side-support)            │                                       │ (whole/breve only)        │
/// │ <see cref="RawSupportEdgeUp"/>         │ LILYSHARP-OWN raw                     │ glyph ink, true side      │
/// │ (dynamics/trill support)               │ <see cref="EngravingDefaults.DefaultStemLength"/> 3.5 — predates │
/// │                                        │ the drawn-stem ports; blind to beams  │                           │
/// │ <see cref="RendererStemLength"/>       │ length rule only (no middle-line      │ (seeded separately by the │
/// │ (skyline stem seed, per chord HEAD)    │ extension, no minimum floor — both    │ skyline, glyph ink)       │
/// │                                        │ bind only inside the staff)           │                           │
/// └────────────────────────────────────────┴───────────────────────────────────────┴───────────────────────────┘
/// </para>
/// <para>
/// Direction POLICY stays with the consumers: multi-voice forcing, the beam's
/// direction override and each consumer's beam-member lookup differ by call site
/// (staff-keyed vs voice-keyed maps, knee/cross-staff gates), so each caller
/// resolves them and hands the results to <see cref="Of"/>.
/// </para>
/// </remarks>
public readonly record struct NoteColumnLayout
{
    /// <summary>Highest head's staff position (half-spaces from the middle line, up-positive).</summary>
    public int TopHeadPosition { get; init; }

    /// <summary>Lowest head's staff position (half-spaces from the middle line, up-positive).</summary>
    public int BottomHeadPosition { get; init; }

    /// <summary>Written note value (1 = whole, 2 = half, 4 = quarter, …) via
    /// <see cref="LayoutUtilities.GetNoteValueFromFraction"/>.</summary>
    public int NoteValue { get; init; }

    /// <summary>The written base duration — kept so the articulation model can spell its own
    /// stem gate over it (see <see cref="StemSupportDistanceDeviceY"/>).</summary>
    public Semantics.Fraction BaseDuration { get; init; }

    /// <summary>The column's RESOLVED stem direction — multi-voice forcing and the beam's
    /// direction override are the CALLER's policy, applied before construction.</summary>
    public bool StemUp { get; init; }

    /// <summary>The quanted beam this column's stem ends on, or null when unbeamed.
    /// LILYPOND-REF: lily/stem.cc Stem::get_beam — a beamed stem ends at its beam.</summary>
    public BeamLayout? Beam { get; init; }

    /// <summary>The X the stem meets the beam at (the beam model's member X).
    /// Read only when <see cref="Beam"/> is non-null.</summary>
    public double BeamStemX { get; init; }

    /// <summary>
    /// Builds the column model for a note or chord; null for any other item (a rest has no
    /// stem or head — its support/skyline contributions stay with the consumers).
    /// </summary>
    public static NoteColumnLayout? Of(
        MusicItem item, bool? forcedStemUp = null,
        BeamLayout? beam = null, double beamStemX = 0.0) => item switch
    {
        NoteItem n => new NoteColumnLayout
        {
            TopHeadPosition = n.StaffPosition,
            BottomHeadPosition = n.StaffPosition,
            NoteValue = LayoutUtilities.GetNoteValueFromFraction(n.BaseDuration),
            BaseDuration = n.BaseDuration,
            StemUp = forcedStemUp ?? n.StemUp,
            Beam = beam,
            BeamStemX = beamStemX,
        },
        ChordItem c when c.Notes.Length > 0 => new NoteColumnLayout
        {
            TopHeadPosition = c.Notes.Max(x => x.StaffPosition),
            BottomHeadPosition = c.Notes.Min(x => x.StaffPosition),
            NoteValue = LayoutUtilities.GetNoteValueFromFraction(c.BaseDuration),
            BaseDuration = c.BaseDuration,
            StemUp = forcedStemUp ?? c.StemUp,
            Beam = beam,
            BeamStemX = beamStemX,
        },
        _ => null,
    };

    /// <summary>A stem exists only for a half note or shorter.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc Stem::is_normal_stem — duration-log &gt;= 1, i.e. note
    /// value &gt;= 2. The same guard the renderer (SharedRenderer.Noteheads.cs), the
    /// skyline seed and the dynamics all take; a whole note or a breve must not
    /// reserve a phantom stem.
    /// </remarks>
    public bool HasStem => NoteValue >= 2;

    /// <summary>The head on the reach's side: the highest head for an UP reach, the lowest
    /// for a DOWN reach — a chord's stem-tip-side head when the reach follows the stem.</summary>
    public int HeadPositionToward(bool up) => up ? TopHeadPosition : BottomHeadPosition;

    /// <summary>
    /// THE column extent — how far this column reaches on <paramref name="towardUp"/>'s side,
    /// in the staff-top device frame (down-positive, middle line =
    /// <see cref="EngravingDefaults.StaffMiddle"/>). The most LP-faithful of the reads:
    /// LilyPond's <c>Note_column::cross_staff_extent[dir]</c>, ported in session 34 for the
    /// drawn tuplet bracket's encompass.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-bracket.cc:554-561 calc_position_and_height — the encompass
    ///   points are the columns' actual extents; there is no beamed-specific formula. A beamed
    ///   stem's extent ends at the quanted beam (:504-509 triggers <c>quantized-positions</c>
    ///   before reading the stem), an unbeamed one at <c>internal_calc_stem_end_position</c>'s
    ///   end (lily/stem.cc:480-596 — duration lengths, the unnatural-direction shortening that
    ///   includes the middle line, the middle-line pull), and a stem pointing AWAY from the
    ///   reach's side contributes only the head's ink on that side.
    /// <para>
    /// The head branch reads <c>bbox.Top</c> for BOTH sides (the head glyph is near-symmetric)
    /// — kept verbatim from the session-34 port, which the ledger pair
    /// <c>staff.staff.tuplet-bracket-*</c> pins; reading the true side ink is an output-moving
    /// change that needs a point first.
    /// </para>
    /// </remarks>
    public double OutwardTipDeviceY(bool towardUp)
    {
        double noteY = EngravingDefaults.StaffMiddle - (HeadPositionToward(towardUp) * 0.5);

        // Stemless (whole/breve), or the stem points away from the reach's side: the
        // column's reach on that side is the notehead's own ink.
        if (NoteValue < 2 || StemUp != towardUp)
        {
            double ink = GlyphMetrics.GetNoteheadBBox(NoteValue).Top;
            return towardUp ? noteY - ink : noteY + ink;
        }

        // A beamed stem ends at the quanted beam's outer face at its own X.
        // LILYPOND-REF: lily/tuplet-bracket.cc:504-509 calc_position_and_height fires quantized-positions.
        if (Beam is { } beam)
            return EngravingDefaults.StaffMiddle - beam.OuterEdgeStaffSpaceAtX(BeamStemX, StemUp);

        // An unbeamed stem ends where the renderer draws it.
        // LILYPOND-REF: lily/stem.cc:480-596 internal_calc_stem_end_position via
        //   StemCalculator.CalculateStemEndY (the same house the renderer and the
        //   articulations read).
        return StemCalculator.CalculateStemEndY(
            noteY, StemUp, 0.0, StemCalculator.GetDurationLog(NoteValue),
            HeadPositionToward(towardUp));
    }

    /// <summary>
    /// The stem's contribution to a side-position SUPPORT (articulations): the DISTANCE from
    /// the tip-side head's centre to the real stem tip — the beam-quanted face for a beamed
    /// column, the drawn stem end for an unbeamed one, and the nominal half head for a column
    /// with no stem at all. Only meaningful when the stem points toward the script's side
    /// (the caller's branch); a stem pointing away is skipped by the support entirely.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/side-position-interface.cc:279-284 get_grob_direction skip — a stem
    ///   is a support only when its direction matches the script's.
    /// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position via
    ///   <see cref="StemCalculator"/>.
    /// <para>
    /// Two verbatim-preserved quirks of this model (vs <see cref="OutwardTipDeviceY"/>):
    /// ⑴ the no-stem fallback is the NOMINAL <see cref="EngravingDefaults.NoteheadHalfHeight"/>
    /// 0.5, not the glyph ink (LilyPond's support would carry the head's extent); ⑵ the stem
    /// gate is spelled over <see cref="BaseDuration"/> (denominator when the numerator is 1,
    /// else no stem) rather than <see cref="HasStem"/> — the two agree for every base duration
    /// the model holds (1/N notes and the 2/1 breve). Either change is an output-moving port.
    /// </para>
    /// </remarks>
    public double StemSupportDistanceDeviceY()
    {
        int anchorPosition = HeadPositionToward(StemUp);
        double anchorY = EngravingDefaults.StaffMiddle - anchorPosition / 2.0;

        // A beamed stem's end is the beam-quanted line, already resolved by beam layout —
        // trust it over the unbeamed formula.
        if (Beam is { } beam)
        {
            double beamTip = EngravingDefaults.StaffMiddle
                - beam.OuterEdgeStaffSpaceAtX(BeamStemX, StemUp);
            return Math.Abs(anchorY - beamTip);
        }

        int denominator = BaseDuration.Numerator == 1 ? BaseDuration.Denominator : 0;
        if (denominator < 2)
            return EngravingDefaults.NoteheadHalfHeight;  // whole note / breve: no stem to clear

        int durLog = StemCalculator.GetDurationLog(denominator);
        double tipY = StemCalculator.CalculateStemEndY(
            anchorY, StemUp, 0.0, durLog, anchorPosition);
        return Math.Abs(anchorY - tipY);
    }

    /// <summary>
    /// LILYSHARP-OWN: the dynamics/trill support edge on <paramref name="up"/>'s side, in the
    /// Y-up staff-middle frame (staff-spaces above the middle line, up-positive) — the head's
    /// glyph ink, extended by a RAW <see cref="EngravingDefaults.DefaultStemLength"/> when the
    /// stem points toward that side.
    /// </summary>
    /// <remarks>
    /// The raw 3.5 predates the drawn-stem ports and is blind to beams: no
    /// unnatural-direction shortening, no middle-line pull, no beam-quanted face — LilyPond's
    /// support would carry the real Stem grob extent
    /// (lily/side-position-interface.cc:265-321 aligned_side merges the supports).
    /// It is the LAST of the four homes still on the raw model; switching this read to
    /// <see cref="OutwardTipDeviceY"/>'s stem rules is an output-moving port that needs a
    /// ledger point first (no <c>dynamics</c>-side point measures it yet — HANDOFF ▶).
    /// LILYPOND-REF: lily/grob.cc:85-89 simple_vertical_skylines_from_extents — the head's
    ///   own extent is its LILC glyph ink (±0.545), not a nominal half staff space.
    /// </remarks>
    public double RawSupportEdgeUp(bool up)
    {
        double headUp = HeadPositionToward(up) * 0.5;
        if (HasStem && StemUp == up)
        {
            return up
                ? headUp + EngravingDefaults.DefaultStemLength
                : headUp - EngravingDefaults.DefaultStemLength;
        }
        var head = GlyphMetrics.GetNoteheadBBox(NoteValue);
        return headUp + (up ? head.Top : head.Bottom);
    }

    /// <summary>
    /// The skyline stem seed's LENGTH (staff-spaces): the renderer's duration-and-shortening
    /// length rule, without the middle-line extension and the minimum-length floor that
    /// <see cref="StemCalculator.CalculateStemEndY"/> applies on top — both bind only inside
    /// the staff, which the staff-symbol seed already covers. A STATIC read because the
    /// skyline seeds a chord's stem per HEAD (each head's own position feeds the shortening),
    /// not once per column — a named model quirk of that consumer.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:506-557 internal_calc_stem_end_position via
    ///   <see cref="StemCalculator.CalculateStemLength"/>.
    /// </remarks>
    public static double RendererStemLength(bool stemUp, int noteValue, int headPosition)
        => StemCalculator.CalculateStemLength(
            stemUp, StemCalculator.GetDurationLog(noteValue), headPosition);
}
