namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for stem length calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:3121-3141 Stem.details
/// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position
/// </remarks>
public sealed record StemDetails
{
    /// <summary>Default parameters matching LilyPond defaults.</summary>
    public static StemDetails Default { get; } = new();

    /// <summary>
    /// Base stem lengths by duration log (index = durationLog - 2).
    /// Quarter=3.5, eighth=3.5, 16th=3.5, 32nd=4.25, 64th=5.0, 128th=6.0, 256th=7.0, 512th=8.0, 1024th=9.0.
    /// LILYPOND-REF: define-grobs.scm:3121 (lengths . (3.5 3.5 3.5 4.25 5.0 6.0 7.0 8.0 9.0))
    /// </summary>
    public double[] Lengths { get; init; } = [3.5, 3.5, 3.5, 4.25, 5.0, 6.0, 7.0, 8.0, 9.0];

    /// <summary>
    /// Ideal stem lengths for beamed stems by beam count (index = beamCount - 1).
    /// LILYPOND-REF: define-grobs.scm:3129 (beamed-lengths . (3.26 3.5 3.6))
    /// </summary>
    public double[] BeamedLengths { get; init; } = [3.26, 3.5, 3.6];

    /// <summary>
    /// Minimum free stem lengths for beamed stems (clearance from chord).
    /// LILYPOND-REF: define-grobs.scm:3132 (beamed-minimum-free-lengths . (1.83 1.5 1.25))
    /// </summary>
    public double[] BeamedMinimumFreeLengths { get; init; } = [1.83, 1.5, 1.25];

    /// <summary>
    /// Absolute minimum free stem lengths for beamed stems.
    /// LILYPOND-REF: define-grobs.scm:3136 (beamed-extreme-minimum-free-lengths . (2.0 1.25))
    /// </summary>
    public double[] BeamedExtremeMinimumFreeLengths { get; init; } = [2.0, 1.25];

    /// <summary>
    /// Shortening amounts for unnatural stem direction by duration.
    /// LILYPOND-REF: define-grobs.scm:3141 (stem-shorten . (1.0 0.5 0.25))
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
/// LILYPOND-REF: lily/stem.cc:1012-1022 Stem_info struct
/// </remarks>
public readonly record struct StemInfo(
    /// <summary>Ideal stem end Y position (staff spaces from staff top).</summary>
    double IdealY,
    /// <summary>Minimum (shortest) stem end Y position.</summary>
    double ShortestY,
    /// <summary>Stem direction: true = up.</summary>
    bool StemUp);

/// <summary>
/// Calculates stem lengths faithfully following LilyPond's algorithm.
/// Handles duration-dependent base lengths, unnatural direction shortening,
/// staff extension rules, and beamed stem info calculation.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position
/// LILYPOND-REF: lily/stem.cc:1024-1155 calc_stem_info
/// </remarks>
public static class StemCalculator
{
    /// <summary>
    /// Calculates the stem end Y position for an unbeamed note.
    /// All coordinates are in staff spaces, with Y increasing downward.
    /// </summary>
    /// <param name="stemAttachY">Y where stem attaches to notehead.</param>
    /// <param name="stemUp">True if stem points up (smaller Y).</param>
    /// <param name="staffTopY">Y of the top staff line.</param>
    /// <param name="durationLog">Duration log (2=quarter, 3=eighth, 4=16th...).</param>
    /// <param name="staffPosition">Staff position of the note (half-spaces from middle line, positive=up).</param>
    /// <param name="details">Stem details parameters.</param>
    /// <returns>Stem end Y position in staff spaces.</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:415-523 internal_calc_stem_end_position
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

        // --- Base length from duration ---
        // LILYPOND-REF: stem.cc:441-443
        int lengthIndex = Math.Clamp(durationLog - 2, 0, d.Lengths.Length - 1);
        double length = d.Lengths[lengthIndex]; // in staff spaces

        // --- Unnatural direction shortening ---
        // LILYPOND-REF: stem.cc:446-482
        // If stem direction matches head position direction (both above or both below middle),
        // this is the "unnatural" direction and the stem should be shortened.
        int dir = stemUp ? 1 : -1; // 1=up, -1=down
        bool unnaturalDirection = (dir > 0 && staffPosition > 0) || (dir < 0 && staffPosition < 0);

        if (unnaturalDirection && d.StemShorten.Length > 0)
        {
            int shortenIndex = Math.Clamp(durationLog - 2, 0, d.StemShorten.Length - 1);
            double shortenProperty = d.StemShorten[shortenIndex];

            // Smooth shortening transition
            // LILYPOND-REF: stem.cc:460-472
            double quarterStemLength = 2 * d.Lengths[0]; // in half-spaces
            double staffRadius = 2.0; // half-staff height in half-spaces
            double shorteningStep = Math.Clamp(shortenProperty / 6.0, 0.25, 0.5);
            double whichStep = Math.Min(1.0, quarterStemLength - 2 * staffRadius - 2)
                               + Math.Abs(staffPosition);
            double shorten = Math.Clamp(shorteningStep * whichStep, 0, shortenProperty);
            length -= shorten;
        }

        // --- Length fraction ---
        // LILYPOND-REF: stem.cc:484
        length *= d.LengthFraction;

        // --- Calculate stem end Y ---
        double stemEndY = stemUp ? stemAttachY - length : stemAttachY + length;

        // --- Staff extension: stems should reach at least the middle line ---
        // LILYPOND-REF: stem.cc:517-520
        if (stemUp && stemEndY > staffMiddleY)
            stemEndY = staffMiddleY;
        else if (!stemUp && stemEndY < staffMiddleY)
            stemEndY = staffMiddleY;

        // --- Minimum stem length ---
        double minLength = EngravingRules.MinimumStemLength;
        double actualLength = Math.Abs(stemEndY - stemAttachY);
        if (actualLength < minLength)
        {
            stemEndY = stemUp ? stemAttachY - minLength : stemAttachY + minLength;
        }

        return stemEndY;
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
    /// <returns>Stem info with ideal and shortest Y positions (in staff positions/half-spaces).</returns>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:1024-1155 calc_stem_info
    /// </remarks>
    public static StemInfo CalculateBeamedStemInfo(
        int headPosition,
        bool stemUp,
        int beamCount,
        double beamThickness = 0.48,
        double beamTranslation = 0.81,
        StemDetails? details = null)
    {
        var d = details ?? StemDetails.Default;
        int dir = stemUp ? 1 : -1; // staff positions: positive = up

        // --- Ideal length from beamed-lengths ---
        // LILYPOND-REF: stem.cc:1051-1064
        int beamIdx = Math.Clamp(beamCount - 1, 0, d.BeamedLengths.Length - 1);
        double idealLength = d.BeamedLengths[beamIdx] * d.LengthFraction
                             - 0.5 * beamThickness; // stem extends to center of beam

        // --- Minimum free length ---
        // LILYPOND-REF: stem.cc:1066-1074
        int minFreeIdx = Math.Clamp(beamCount - 1, 0, d.BeamedMinimumFreeLengths.Length - 1);
        double idealMinimumFree = d.BeamedMinimumFreeLengths[minFreeIdx] * d.LengthFraction;

        // --- Height of beams ---
        // LILYPOND-REF: stem.cc:1085-1098
        double heightOfMyBeams = beamThickness + (beamCount - 1) * beamTranslation;
        double idealMinimumLength = idealMinimumFree + heightOfMyBeams - 0.5 * beamThickness;
        idealLength = Math.Max(idealLength, idealMinimumLength);

        // --- Note start position (in staff spaces) ---
        // LILYPOND-REF: stem.cc:1102-1105
        double noteStart = headPosition * 0.5 * dir; // convert to staff spaces in stem direction
        double idealY = noteStart + idealLength;

        // --- Staff boundary constraints ---
        // LILYPOND-REF: stem.cc:1122-1132
        // Stems should not end below the middle staff line
        idealY = Math.Max(idealY, 0.0);

        // --- Extreme minimum ---
        // LILYPOND-REF: stem.cc:1136-1152
        int extremeMinIdx = Math.Clamp(beamCount - 1, 0, d.BeamedExtremeMinimumFreeLengths.Length - 1);
        double minimumFree = d.BeamedExtremeMinimumFreeLengths[extremeMinIdx];
        double minimumLength = minimumFree + heightOfMyBeams - 0.5 * beamThickness;
        double shortestY = (noteStart + minimumLength) * dir;

        // Convert back to staff-space Y coordinates
        // idealY and shortestY are in "stem direction distance from staff center"
        // Convert: positive idealY means further from notehead
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
