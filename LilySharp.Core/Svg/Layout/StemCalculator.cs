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
    /// Length fraction multiplier.
    /// LILYPOND-REF: define-grobs.scm Stem.length-fraction (default 1.0)
    /// </summary>
    public double LengthFraction { get; init; } = 1.0;
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
    /// Calculates the stem end Y position for an unbeamed note.
    /// </summary>
    /// <remarks>
    /// COORDINATE SYSTEM: the body computes in LilyPond's native frame — Y-up,
    /// measured in staff-spaces from the staff middle line (position 0), exactly
    /// like <c>lily/stem.cc</c>. Inputs are device coordinates (Y-down, the
    /// shared layout/render space), converted to the up frame on entry and
    /// reflected back to device Y on return via <c>staffMiddleY - up</c>. This
    /// mirrors LilyPond, which reasons in Y-up and flips to device Y only at
    /// stencil/output time, so the formulae below read sign-for-sign against
    /// <c>stem.cc</c> (stem-up ADDS length, as in <c>stem.cc:588</c>).
    /// </remarks>
    /// <param name="stemAttachY">Device Y where stem attaches to notehead.</param>
    /// <param name="stemUp">True if stem points up.</param>
    /// <param name="staffTopY">Device Y of the top staff line.</param>
    /// <param name="durationLog">Duration log (2=quarter, 3=eighth, 4=16th...).</param>
    /// <param name="staffPosition">Staff position of the note (half-spaces from middle line, positive=up).</param>
    /// <param name="details">Stem details parameters.</param>
    /// <returns>Stem end Y position in device coordinates (staff spaces, Y-down).</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:480-596 internal_calc_stem_end_position
    /// </remarks>
    public static double CalculateStemEndY(
        double stemAttachY,
        bool stemUp,
        double staffTopY,
        int durationLog = 2,
        int staffPosition = 0,
        StemDetails? details = null)
    {
        var d = details ?? StemDetails.Default;
        double staffHeight = 4.0; // staff spaces
        double staffMiddleY = staffTopY + staffHeight / 2;

        // Convert the device-Y attach point into LilyPond's Y-up frame
        // (staff-spaces above the middle line): middle − device.
        double attachUp = staffMiddleY - stemAttachY;

        // --- Base length from duration ---
        // LILYPOND-REF: stem.cc:506-517
        int lengthIndex = Math.Clamp(durationLog - 2, 0, d.Lengths.Length - 1);
        double length = d.Lengths[lengthIndex]; // in staff spaces

        // --- Unnatural direction shortening ---
        // LILYPOND-REF: stem.cc:519-555
        // If stem direction matches head position direction (both above or both below middle),
        // this is the "unnatural" direction and the stem should be shortened.
        int dir = stemUp ? 1 : -1; // 1=up, -1=down
        bool unnaturalDirection = (dir > 0 && staffPosition > 0) || (dir < 0 && staffPosition < 0);

        if (unnaturalDirection && d.StemShorten.Length > 0)
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

        // --- Length fraction ---
        // LILYPOND-REF: stem.cc:557
        length *= d.LengthFraction;

        // --- Calculate stem end (Y-up) ---
        // LILYPOND-REF: stem.cc:588 — stem end = attach + dir * length.
        double stemEndUp = attachUp + dir * length;

        // --- Staff extension: stems should reach at least the middle line ---
        // LILYPOND-REF: stem.cc:591-593 — an up stem must not end below the
        // middle line (up < 0); a down stem must not end above it (up > 0).
        if (stemUp && stemEndUp < 0)
            stemEndUp = 0;
        else if (!stemUp && stemEndUp > 0)
            stemEndUp = 0;

        // --- Minimum stem length ---
        double minLength = EngravingDefaults.MinStemLength;
        double actualLength = Math.Abs(stemEndUp - attachUp);
        if (actualLength < minLength)
        {
            stemEndUp = attachUp + dir * minLength;
        }

        // Reflect back to device coordinates (Y-down): middle − Y-up.
        return staffMiddleY - stemEndUp;
    }

    /// <summary>
    /// Calculates ideal and minimum stem info for beamed notes.
    /// Used by BeamScoringProblem to determine beam positions.
    /// </summary>
    /// <param name="headPosition">Staff position of the note head (half-spaces).</param>
    /// <param name="stemUp">True if stem points up.</param>
    /// <param name="beamCount">Number of beams on this stem.</param>
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
        bool isKnee = false)
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
        if (!isKnee)
        {
            idealY = Math.Max(idealY, 0.0);
            idealY = Math.Max(idealY, -1.0 - beamThickness + heightOfMyBeams);
        }

        // --- Extreme minimum ---
        // LILYPOND-REF: stem.cc:1247-1259
        int extremeMinIdx = Math.Clamp(beamCount - 1, 0, d.BeamedExtremeMinimumFreeLengths.Length - 1);
        double minimumFree = d.BeamedExtremeMinimumFreeLengths[extremeMinIdx];
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
