// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/stem.cc
//     Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
//     Jan Nieuwenhuizen <janneke@gnu.org>
// LilyPond is free software under the GNU General Public License version 3 or
// later; its notices are kept here as that licence requires. The full list is in
// LILYPOND-ATTRIBUTION.md. Lily# is an independent project, not affiliated with
// or endorsed by the LilyPond project.
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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for stem length calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3435-3452 Stem.details
/// LILYPOND-REF: lily/stem.cc:480-596 internal_calc_stem_end_position
/// </remarks>
public sealed record StemDetails
{
    /// <summary>Default parameters matching LilyPond defaults.</summary>
    public static StemDetails Default { get; } = new();

    /// <summary>
    /// Base stem lengths by duration log (index = durationLog - 2).
    /// Quarter=3.5, eighth=3.5, 16th=3.5, 32nd=4.25, 64th=5.0, 128th=6.0, 256th=7.0, 512th=8.0, 1024th=9.0.
    /// LILYPOND-REF: define-grobs.scm:3448 (lengths . (3.5 3.5 3.5 4.25 5.0 6.0 7.0 8.0 9.0))
    /// </summary>
    public double[] Lengths { get; init; } = [3.5, 3.5, 3.5, 4.25, 5.0, 6.0, 7.0, 8.0, 9.0];

    /// <summary>
    /// Ideal stem lengths for beamed stems by beam count (index = beamCount - 1).
    /// LILYPOND-REF: define-grobs.scm:3442 (beamed-lengths . (3.26 3.5 3.6))
    /// </summary>
    public double[] BeamedLengths { get; init; } = [3.26, 3.5, 3.6];

    /// <summary>
    /// Minimum free stem lengths for beamed stems (clearance from chord).
    /// LILYPOND-REF: define-grobs.scm:3444 (beamed-minimum-free-lengths . (1.83 1.5 1.25))
    /// </summary>
    public double[] BeamedMinimumFreeLengths { get; init; } = [1.83, 1.5, 1.25];

    /// <summary>
    /// Absolute minimum free stem lengths for beamed stems.
    /// LILYPOND-REF: define-grobs.scm:3436 (beamed-extreme-minimum-free-lengths . (2.0 1.25))
    /// </summary>
    public double[] BeamedExtremeMinimumFreeLengths { get; init; } = [2.0, 1.25];

    /// <summary>
    /// Shortening amounts for unnatural stem direction by duration.
    /// LILYPOND-REF: define-grobs.scm:3452 (stem-shorten . (1.0 0.5 0.25))
    /// </summary>
    public double[] StemShorten { get; init; } = [1.0, 0.5, 0.25];

    /// <summary>
    /// How much to shorten beamed stems forced into their unnatural direction,
    /// indexed by beam count − 1. A beam-level amount (scaled by the forced fraction),
    /// subtracted from every stem's ideal beam Y.
    /// LILYPOND-REF: define-grobs.scm:493 (beamed-stem-shorten . (1.0 0.5 0.25))
    /// </summary>
    public double[] BeamedStemShorten { get; init; } = [1.0, 0.5, 0.25];

    /// <summary>
    /// Length fraction multiplier.
    /// LILYPOND-REF: define-grobs.scm Stem.length-fraction (default 1.0)
    /// </summary>
    public double LengthFraction { get; init; } = 1.0;

    /// <summary>
    /// Whether this stem REFUSES to be lengthened to reach the middle staff line.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:591-593 <c>internal_calc_stem_end_position</c> —
    ///   <c>if (!no_extend &amp;&amp; dir * stem_end &lt; 0) stem_end = 0.0;</c>, and the beamed
    ///   twin at :1233-1235 <c>calc_stem_info</c>, which guards its two staff-boundary
    ///   clamps with the SAME property (beside the knee test Lily# already honours).
    /// <para>
    /// ⚠️ THIS IS A SEPARATE PROPERTY FROM <see cref="LengthFraction"/> AND IT IS NOT
    /// DERIVABLE FROM IT: <c>general-grace-settings</c> states both for a grace
    /// (scm/music-functions.scm:642-643, <c>length-fraction 0.8</c> and
    /// <c>no-stem-extend #t</c>) while <c>\name CueVoice</c> states only the fraction, so a
    /// cue stem still extends and a grace stem does not.
    /// </para>
    /// <para>
    /// MEASURED on 2.26.0 (scratch/p313/lp/g3.ly, three pitches where the two answers
    /// differ): <c>\grace { a16 }</c>, two ledgers below the staff, draws a 2.80 stem that
    /// STOPS SHORT of the middle line, where the full-size <c>a16</c> beside it is dragged
    /// out to 4.00 and ends exactly on it. Without this flag Lily# drew the grace at 4.00
    /// too — the extension was firing on the one voice LilyPond turns it off for.
    /// </para>
    /// </remarks>
    public bool NoStemExtend { get; init; }
}

/// <summary>
/// Stem length and position information for beam quantization.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/include/stem-info.hh:30-37 Stem_info struct
/// </remarks>
public readonly record struct StemInfo(
    // Ideal beam Y position, in staff-spaces measured up from the staff middle line.
    double IdealY,
    // Minimum (shortest) beam Y position, staff-spaces from the staff middle (Y-up).
    double ShortestY,
    // Stem direction: true = up.
    bool StemUp);

/// <summary>
/// Calculates stem lengths faithfully following LilyPond's algorithm.
/// Handles duration-dependent base lengths, unnatural direction shortening,
/// staff extension rules, and beamed stem info calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stem.cc:480-596 internal_calc_stem_end_position
/// LILYPOND-REF: lily/stem.cc:1135-1266 calc_stem_info
/// </remarks>
public static class StemCalculator
{
    /// <summary>
    /// The stem's LENGTH in staff spaces: <c>details.lengths</c> picked by duration, less the
    /// unnatural-direction shortening, times <c>length-fraction</c>. This is LilyPond's
    /// <c>length</c> local in <c>Stem::calc_length</c> — BEFORE the middle-line extension and
    /// the minimum-length floor that <see cref="CalculateStemEndY"/> applies on top of it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:506-557 internal_calc_stem_end_position — :506-517 picks the
    /// length out of <c>details.lengths</c>, :519-555 shortens a stem that points the way its
    /// own head already lies, :557 scales by <c>length-fraction</c>.
    /// <para>
    /// Split out of <see cref="CalculateStemEndY"/> so the SKYLINE can reserve the stem the
    /// renderer draws. <c>SkylineBuilder</c> seeded a flat
    /// <see cref="EngravingDefaults.DefaultStemLength"/>, which is LilyPond's <c>lengths</c>
    /// ENTRY and not its <c>length</c> — the same "draws right, reserves stale" double model
    /// that <c>AddStaffToSkylines</c> already names for BEAMED stems (HANDOFF §5.2.1②).
    /// </para>
    /// </remarks>
    /// <param name="stemUp">True if the stem points up.</param>
    /// <param name="durationLog">Duration log (2=quarter, 3=eighth, 4=16th...).</param>
    /// <param name="staffPosition">
    /// Staff position of the head the stem hangs off, in HALF-spaces from the middle line,
    /// up-positive — LilyPond's <c>hp[dir]</c>.
    /// </param>
    /// <param name="details">Stem details parameters.</param>
    public static double CalculateStemLength(
        bool stemUp,
        int durationLog = 2,
        int staffPosition = 0,
        StemDetails? details = null)
    {
        var d = details ?? StemDetails.Default;

        // LILYPOND-REF: stem.cc:506-517 — length = 2 * details.lengths[durlog - 2], in
        // half-spaces there and in whole staff-spaces here.
        int lengthIndex = Math.Clamp(durationLog - 2, 0, d.Lengths.Length - 1);
        double length = d.Lengths[lengthIndex]; // in staff spaces

        // LILYPOND-REF: stem.cc:519-522 — "Stems in unnatural (forced) direction should be
        // shortened, according to [Roush & Gourlay]":
        //     Interval hp = head_positions (me);
        //     if (dir && dir * hp[dir] >= 0)
        // ⚠️ The comparison is >= 0, so a head sitting ON the middle line is shortened too:
        // its stem points neither with nor against the head, and LilyPond counts that as the
        // unnatural side. This used to be spelled as two STRICT inequalities, which agrees
        // with LilyPond everywhere except position 0 — exactly where a plain middle-line
        // down-stem is the deepest ink a system has.
        int dir = stemUp ? 1 : -1; // 1=up, -1=down
        if (dir * staffPosition >= 0 && d.StemShorten.Length > 0)
        {
            int shortenIndex = Math.Clamp(durationLog - 2, 0, d.StemShorten.Length - 1);
            // LP computes the whole shortening in HALF-spaces: length=2·lengths, and
            // shorten-property=2·(stem-shorten) (stem.cc:516,530). Our `length` above is in
            // whole staff-spaces, so we run the transition in LP's half-space frame and
            // convert the result back (÷2) at the subtraction.
            double shortenProperty = 2 * d.StemShorten[shortenIndex]; // half-spaces (LP ×2)

            // Smooth shortening transition
            // LILYPOND-REF: stem.cc:541-554
            double quarterStemLength = 2 * d.Lengths[0]; // in half-spaces
            double staffRadius = 2.0; // half-staff height in half-spaces
            double shorteningStep = Math.Clamp(shortenProperty / 6.0, 0.25, 0.5);
            double whichStep = Math.Min(1.0, quarterStemLength - 2 * staffRadius - 2)
                               + Math.Abs(staffPosition); // staffPosition already in half-spaces
            double shorten = Math.Clamp(shorteningStep * whichStep, 0, shortenProperty); // half-spaces
            length -= shorten / 2.0; // half-spaces -> staff-spaces
        }

        // LILYPOND-REF: stem.cc:557 — length *= length-fraction.
        length *= d.LengthFraction;
        return length;
    }

    /// <summary>
    /// Calculates the stem end Y position for an unbeamed note.
    /// </summary>
    /// <remarks>
    /// COORDINATE SYSTEM: the body computes in LilyPond's native frame — Y-up,
    /// measured in staff-spaces from the staff middle line (position 0), exactly
    /// like <c>lily/stem.cc</c>. Inputs are device coordinates (Y-down, the
    /// shared layout/render space), converted to the up frame on entry and
    /// reflected back to device Y on return via <c>staffMiddleDown - up</c>. This
    /// mirrors LilyPond, which reasons in Y-up and flips to device Y only at
    /// stencil/output time, so the formulae below read sign-for-sign against
    /// <c>stem.cc</c> (stem-up ADDS length, as in <c>stem.cc:588</c>).
    /// <para>
    /// LILYPOND-REF: lily/stem.cc:480-596 internal_calc_stem_end_position.
    /// </para>
    /// </remarks>
    /// <param name="stemAttachY">Device Y where stem attaches to notehead.</param>
    /// <param name="stemUp">True if stem points up.</param>
    /// <param name="staffTopDown">Device Y of the top staff line.</param>
    /// <param name="durationLog">Duration log (2=quarter, 3=eighth, 4=16th...).</param>
    /// <param name="staffPosition">Staff position of the note (half-spaces from middle line, positive=up).</param>
    /// <param name="details">Stem details parameters.</param>
    /// <returns>Stem end Y position in device coordinates (staff spaces, Y-down).</returns>
    public static double CalculateStemEndY(
        double stemAttachY,
        bool stemUp,
        double staffTopDown,
        int durationLog = 2,
        int staffPosition = 0,
        StemDetails? details = null)
    {
        var d = details ?? StemDetails.Default;
        double staffHeight = 4.0; // staff spaces
        double staffMiddleDown = staffTopDown + staffHeight / 2;

        // Convert the device-Y attach point into LilyPond's Y-up frame
        // (staff-spaces above the middle line): middle − device.
        double attachUp = staffMiddleDown - stemAttachY;

        // --- Length from duration, less the unnatural-direction shortening ---
        // LILYPOND-REF: stem.cc:506-557 (see CalculateStemLength)
        int dir = stemUp ? 1 : -1; // 1=up, -1=down
        double length = CalculateStemLength(stemUp, durationLog, staffPosition, d);

        // --- Calculate stem end (Y-up) ---
        // LILYPOND-REF: stem.cc:588 — stem end = attach + dir * length.
        double stemEndUp = attachUp + dir * length;

        // --- Staff extension: stems should reach at least the middle line ---
        // LILYPOND-REF: stem.cc:591-593 — an up stem must not end below the
        // middle line (up < 0); a down stem must not end above it (up > 0) —
        // UNLESS the stem states no-stem-extend, which a grace does and a cue does not
        // (see StemDetails.NoStemExtend, and the measurement on it).
        if (!d.NoStemExtend)
        {
            if (stemUp && stemEndUp < 0)
                stemEndUp = 0;
            else if (!stemUp && stemEndUp > 0)
                stemEndUp = 0;
        }

        // THE MINIMUM-LENGTH FLOOR IS GONE (session 85), because LilyPond has none.
        // internal_calc_stem_end_position runs :506-517 (the table), :519-555 (the
        // unnatural-direction shortening), :557 (length-fraction), :559-586 (the tremolo,
        // whose max() is the ONLY one in the function) and then :588-595 — the end position
        // and the middle-line rule — and returns. Nothing else bounds `length`.
        //
        // Lily# had a 2.5-staff-space floor here, declared "conventional, no single named LP
        // constant", and IT NEVER FIRED: the shortest length :506-555 can produce is
        // 3.5 − 1.0 = 2.5 exactly, and the middle-line rule at :591-593 only ever makes a stem
        // LONGER (it fires when the end has crossed the middle line, i.e. when the stem is
        // already reaching past it). So the floor was dead code that became live the moment a
        // length-fraction arrived — a cue quarter wants 2.099868416491456 and would have been
        // clamped up to 2.5. Scaling the floor by the fraction was the first fix here and was
        // an invention where a deletion was available: removing it is both the literal port
        // and output-invariant, which was measured, not assumed (3976 tests, 657 snapshots,
        // 462 ledger points — nothing moved).
        // LILYPOND-REF: lily/stem.cc:481-596 internal_calc_stem_end_position — the whole
        //   function, cited whole on purpose: the claim being made is about what it does NOT
        //   contain.

        // Reflect back to device coordinates (Y-down): middle − Y-up.
        return staffMiddleDown - stemEndUp;
    }

    /// <summary>
    /// Calculates ideal and minimum stem info for beamed notes.
    /// Used by BeamScoringProblem to determine beam positions.
    /// </summary>
    /// <param name="headPosition">Staff position of the note head (half-spaces).</param>
    /// <param name="stemUp">True if stem points up.</param>
    /// <param name="beamCount">The beam's MAXIMUM beam count for this stem's direction, not
    /// the stem's own multiplicity — LilyPond reads it from
    /// <c>Beam::get_direction_beam_count</c> (lily/stem.cc:1158), so a group's stems all get
    /// the same ideal length and <c>a8[ a32]</c> comes out horizontal (:1196-1202).</param>
    /// <param name="beamThickness">Beam thickness in staff spaces.</param>
    /// <param name="beamTranslation">Distance between beam centers in staff spaces.</param>
    /// <param name="details">Stem details parameters.</param>
    /// <param name="isKnee">True when the owning beam is kneed — LilyPond skips
    /// the staff-extension clamps for knees (<c>knee</c> beam property).</param>
    /// <returns>Stem info with absolute ideal and shortest beam Y positions, in
    /// staff-spaces (LilyPond's frame: noteStart = headPosition*0.5, lengths in ss).
    /// NOT half-spaces — the beam quanter reads these directly.</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:1135-1266 calc_stem_info
    /// </remarks>
    public static StemInfo CalculateBeamedStemInfo(
        int headPosition,
        bool stemUp,
        int beamCount,
        double beamThickness = 0.48,
        double beamTranslation = 0.81,
        StemDetails? details = null,
        bool isKnee = false,
        double beamShorten = 0.0)
    {
        var d = details ?? StemDetails.Default;
        int dir = stemUp ? 1 : -1; // staff positions: positive = up

        // --- Ideal length from beamed-lengths ---
        // LILYPOND-REF: stem.cc:1164-1175
        int beamIdx = Math.Clamp(beamCount - 1, 0, d.BeamedLengths.Length - 1);
        double idealLength = d.BeamedLengths[beamIdx] * d.LengthFraction
                             - 0.5 * beamThickness; // stem extends to center of beam

        // --- Minimum free length ---
        // LILYPOND-REF: stem.cc:1178-1185
        int minFreeIdx = Math.Clamp(beamCount - 1, 0, d.BeamedMinimumFreeLengths.Length - 1);
        double idealMinimumFree = d.BeamedMinimumFreeLengths[minFreeIdx] * d.LengthFraction;

        // --- Height of beams ---
        // LILYPOND-REF: stem.cc:1203-1211
        double heightOfMyBeams = beamThickness + (beamCount - 1) * beamTranslation;
        double idealMinimumLength = idealMinimumFree + heightOfMyBeams - 0.5 * beamThickness;
        idealLength = Math.Max(idealLength, idealMinimumLength);

        // --- Note start position (in staff spaces) ---
        // LILYPOND-REF: stem.cc:1213-1216
        double noteStart = headPosition * 0.5 * dir; // convert to staff spaces in stem direction
        double idealY = noteStart + idealLength;

        // --- Staff boundary constraints ---
        // LILYPOND-REF: stem.cc:1218-1243 — the highest beam of an UP beam must
        // never be lower than the middle staffline, and the lowest beam never
        // lower than the second staffline. NOT applied to knees ("Also, not
        // for knees. Seems to be a good thing.") — for a knee the ideal beam
        // sits in the gap between the pitch groups, outside the staff.
        // ⚠️ AND NOT WHEN THE STEM REFUSES TO BE EXTENDED: :1233-1235 guards both clamps
        // with no-stem-extend as well as the knee test, which is the same property the
        // unbeamed rule at :591-593 reads (StemDetails.NoStemExtend). Lily# honoured the knee
        // half and not this one, so a grace BEAM was still being dragged toward the staff.
        if (!d.NoStemExtend && !isKnee)
        {
            idealY = Math.Max(idealY, 0.0);
            idealY = Math.Max(idealY, -1.0 - beamThickness + heightOfMyBeams);
        }

        // --- Forced-direction shortening (beam-level, ideal only) ---
        // LILYPOND-REF: stem.cc:1245 ideal_y -= (ly:grob-property beam 'shorten).
        // The beam's 'shorten (Beam::calc_stem_shorten) pulls the ideal beam toward the
        // staff for stems forced into their unnatural direction; the shortest_y_ floor
        // below is deliberately NOT shortened (LilyPond leaves minimum_y alone).
        idealY -= beamShorten;

        // --- Extreme minimum ---
        // LILYPOND-REF: stem.cc:1247-1259
        int extremeMinIdx = Math.Clamp(beamCount - 1, 0, d.BeamedExtremeMinimumFreeLengths.Length - 1);
        // ⚠️ length_fraction scales this one too — lily/stem.cc:1247-1259 multiplies the
        // extreme minimum by staff_space AND length_fraction, exactly as it does the ideal
        // and the minimum-free above. Invisible while the fraction is 1; a grace beam is
        // where it starts to matter.
        double minimumFree = d.BeamedExtremeMinimumFreeLengths[extremeMinIdx] * d.LengthFraction;
        double minimumLength = minimumFree + heightOfMyBeams - 0.5 * beamThickness;
        double shortestY = (noteStart + minimumLength) * dir;

        // Return absolute beam Y in staff-spaces (Y-up). idealY/shortestY above are
        // "stem-direction distance from the note", so ×dir gives the absolute beam Y
        // measured up from the staff middle line (higher = larger). Already ss — no
        // half-space conversion.
        return new StemInfo(idealY * dir, shortestY, stemUp);
    }

    /// <summary>
    /// Gets the duration log for a note value.
    /// </summary>
    public static int GetDurationLog(int noteValue) => noteValue switch
    {
        1 => 0,   // whole
        2 => 1,   // half
        4 => 2,   // quarter
        8 => 3,   // eighth
        16 => 4,  // 16th
        32 => 5,  // 32nd
        64 => 6,  // 64th
        128 => 7, // 128th
        _ => 2    // default to quarter
    };
}
