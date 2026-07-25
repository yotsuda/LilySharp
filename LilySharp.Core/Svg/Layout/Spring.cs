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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A spring connecting two adjacent items in a measure.
/// LILYPOND-REF: lily/spring.cc:1-237 Spring class
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spring.cc:219-237 Spring::length()
///   length = max(ideal_distance + force * inverse_stretch_strength, min_distance)
///
/// Where:
/// - IdealDistance: The preferred distance based on duration
/// - MinDistance: The minimum distance (rod constraint)
/// - InverseStretchStrength: Controls how much the spring stretches per unit force
/// - Force: Applied uniformly to all springs to achieve target width
/// </remarks>
internal sealed record Spring
{
    /// <summary>
    /// The ideal distance between reference points.
    /// Calculated from duration using logarithmic scaling (Gourlay algorithm).
    /// </summary>
    public double IdealDistance { get; init; }

    /// <summary>
    /// The minimum distance (rod constraint).
    /// Calculated from collision avoidance or canonical notehead width.
    /// </summary>
    public double MinDistance { get; init; }

    /// <summary>
    /// The inverse stretch strength (flexibility).
    /// Higher values mean more stretching per unit force.
    /// Lilypond default: ideal - min (at least 0.1).
    /// </summary>
    public double InverseStretchStrength { get; init; }

    /// <summary>
    /// The inverse compress strength.
    /// Used when force is negative (compression).
    /// Lilypond default: ideal - min (at least 0).
    /// </summary>
    public double InverseCompressStrength { get; init; }

    /// <summary>
    /// The force at which the spring transitions from compressed to stretched.
    /// Below this force, the spring is at MinDistance.
    /// </summary>
    public double BlockingForce { get; }

    public Spring(double idealDistance, double minDistance, double inverseStretchStrength)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        InverseStretchStrength = inverseStretchStrength;

        // Lilypond default: inverse_compress_strength = ideal - min (at least 0)
        InverseCompressStrength = Math.Max(0, idealDistance - minDistance);

        // Calculate blocking force (from Lilypond spring.cc update_blocking_force)
        if (minDistance > idealDistance)
        {
            if (inverseStretchStrength > 0)
                BlockingForce = (minDistance - idealDistance) / inverseStretchStrength;
            else
                BlockingForce = 0;
        }
        else
        {
            if (InverseCompressStrength > 0)
                BlockingForce = (minDistance - idealDistance) / InverseCompressStrength;
            else
                BlockingForce = 0;
        }
    }

    /// <summary>
    /// Full constructor with explicit compress strength.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spring.cc:29-35</remarks>
    public Spring(double idealDistance, double minDistance,
                  double inverseStretchStrength, double inverseCompressStrength)
    {
        IdealDistance = idealDistance;
        MinDistance = minDistance;
        InverseStretchStrength = inverseStretchStrength;
        InverseCompressStrength = inverseCompressStrength;

        if (minDistance > idealDistance)
        {
            BlockingForce = inverseStretchStrength > 0
                ? (minDistance - idealDistance) / inverseStretchStrength
                : 0;
        }
        else
        {
            BlockingForce = inverseCompressStrength > 0
                ? (minDistance - idealDistance) / inverseCompressStrength
                : 0;
        }
    }

    /// <summary>
    /// Replaces the ideal distance, leaving BOTH strengths exactly as they are.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:131-141 Spring::set_ideal_distance — it assigns
    /// <c>ideal_distance_</c> and calls <c>update_blocking_force ()</c>, and that is ALL:
    /// <c>inverse_compress_strength_</c> and <c>inverse_stretch_strength_</c> keep whatever
    /// <c>set_default_strength</c> / <c>set_inverse_*_strength</c> last put there.
    /// <para>
    /// Every refinement LilyPond applies to a note spring after building it goes through
    /// this setter — the left head width and the stem correction
    /// (lily/note-spacing.cc:77 and :111, both landing on :113
    /// <c>base.set_ideal_distance (std::max (0.0, ideal))</c>) — so a note spring's
    /// compressibility stays <c>fraction * (duration_space - increment)</c> from
    /// lily/spacing-basic.cc:151-157 with lily/spring.cc:204-210, NOT
    /// <c>ideal - min_distance</c> recomputed from the refined pair.
    /// </para>
    /// <para>
    /// MEASURED, per spring, off LilyPond's own compressed line
    /// (audit/lp-geometry/probes/compressed-line-force.ly, CLW - CLJ over |force|
    /// 0.006918750): a quarter-to-quarter spring compresses at 1.698045 where
    /// <c>ideal - min</c> would give 1.398045, and a half at 2.898045. Those are exactly
    /// <c>len - 1.2</c>, i.e. this setter's semantics and not the constructor's.
    /// </para>
    /// </remarks>
    public Spring WithIdealDistance(double idealDistance)
        => new(idealDistance, MinDistance, InverseStretchStrength, InverseCompressStrength);

    /// <summary>
    /// Replaces the minimum distance, leaving BOTH strengths exactly as they are.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:143-153 Spring::set_min_distance — as with
    /// <see cref="WithIdealDistance"/>, it updates the blocking force and nothing else.
    /// This is the setter <c>Note_spacing::get_spacing</c> uses to put the SKYLINE distance
    /// under a duration-built spring (lily/note-spacing.cc:82-83
    /// <c>base.set_min_distance (min_dist)</c>): the minimum becomes geometric while the
    /// compressibility stays the duration one.
    /// </remarks>
    public Spring WithMinDistance(double minDistance)
        => new(IdealDistance, minDistance, InverseStretchStrength, InverseCompressStrength);

    /// <summary>
    /// Raises the minimum distance to at least <paramref name="minDistance"/>, leaving both
    /// strengths as they are.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/spring.cc:155-159 Spring::ensure_min_distance —
    /// <c>set_min_distance (std::max (d, min_distance_))</c>.</remarks>
    public Spring EnsureMinDistance(double minDistance)
        => minDistance > MinDistance ? WithMinDistance(minDistance) : this;

    /// <summary>
    /// Merges the simultaneous spacing wishes for ONE column pair into one spring —
    /// LilyPond's <c>merge_springs</c>, taking all of them at once.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:101-129 <c>merge_springs</c>:
    /// <code>
    ///   avg_distance += springs[i].ideal_distance ();
    ///   avg_stretch  += springs[i].inverse_stretch_strength ();
    ///   avg_compress += 1 / springs[i].inverse_compress_strength ();
    ///   min_distance  = max (springs[i].min_distance (), min_distance);
    ///   ... /= springs.size ();
    ///   avg_distance = max (min_distance + 0.3, avg_distance);
    ///   ret.set_inverse_stretch_strength (avg_stretch);
    ///   ret.set_inverse_compress_strength (1 / avg_compress);
    /// </code>
    /// The ideal distance and the stretch flexibility are ARITHMETIC means, the compress
    /// flexibility is the HARMONIC one, the minimum is the MAX, and the merged ideal is
    /// floored at <c>min_distance + 0.3</c> — LilyPond's own comment: "leave a little
    /// headroom above the largest minimum distance so that things don't get too cramped".
    /// <para>
    /// ⚠️ N at once, NOT a fold of a two-argument merge. Folding weights three wishes
    /// 1/4 : 1/4 : 1/2 where LilyPond weights them 1/3 each, and re-applies the headroom
    /// floor at every step — so a system's third staff would count double.
    /// </para>
    /// <para>
    /// ⚠️ The harmonic term is taken UNCONDITIONALLY, which is the whole behaviour of a
    /// RIGID wish: one spring with <c>inverse_compress_strength == 0</c> contributes
    /// <c>1 / 0 = +inf</c>, so <c>avg_compress</c> is <c>+inf</c> and the merged strength is
    /// <c>1 / inf = 0</c> — the merged spring cannot be compressed at all. That is
    /// LilyPond's arithmetic and not an edge case to be smoothed away: a staff that cannot
    /// give way stops the whole column pair from giving way. (A notation+tab line start is
    /// exactly this — the TAB clef's <c>minimum-fixed-space</c> wish sits on its
    /// <c>0.3 + min_dist</c> floor with nothing left to compress.)
    /// </para>
    /// <para>
    /// <c>set_inverse_compress_strength</c>'s <c>!isfinite</c> guard (spring.cc:172-181) is
    /// unreachable from here: it would need <c>avg_compress == 0</c>, i.e. EVERY wish
    /// infinitely compressible, and <c>inverse_compress_strength</c> is always finite.
    /// </para>
    /// </remarks>
    public static Spring MergeSprings(IReadOnlyList<Spring> springs)
    {
        double avgDistance = 0, minDistance = 0, avgStretch = 0, avgCompress = 0;

        for (int i = 0; i < springs.Count; i++)
        {
            avgDistance += springs[i].IdealDistance;
            avgStretch += springs[i].InverseStretchStrength;
            avgCompress += 1 / springs[i].InverseCompressStrength;
            minDistance = Math.Max(springs[i].MinDistance, minDistance);
        }

        avgStretch /= springs.Count;
        avgCompress /= springs.Count;
        avgDistance /= springs.Count;
        avgDistance = Math.Max(minDistance + SpacingRules.SpringHeadroom, avgDistance);

        return new Spring(avgDistance, minDistance, avgStretch, 1 / avgCompress);
    }

    /// <summary>
    /// Scales the spring by a factor (e.g., for grace notes).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spring.cc:88-95 Spring::operator*=()
    /// </remarks>
    public Spring Scale(double factor)
    {
        double newIdeal = Math.Max(MinDistance, IdealDistance * factor);
        double newCompress = Math.Max(0, newIdeal - MinDistance);
        double newStretch = InverseStretchStrength * factor;
        return new Spring(newIdeal, MinDistance, newStretch, newCompress);
    }

    /// <summary>
    /// Calculates the spring length for a given force.
    /// </summary>
    /// <param name="force">The force applied (positive = stretch, negative = compress)</param>
    /// <returns>The resulting length, never less than MinDistance</returns>
    public double Length(double force)
    {
        // LILYPOND-REF: lily/spring.cc:219-237 Spring::length()
        double effectiveForce = Math.Max(force, BlockingForce);
        double invK = effectiveForce < 0 ? InverseCompressStrength : InverseStretchStrength;

        // LILYPOND-REF: lily/spring.cc:228-234 - handle +Inf case
        // +Inf can happen; -Inf is impossible as BlockingForce is finite
        if (double.IsPositiveInfinity(effectiveForce))
        {
            effectiveForce = 0.0;
        }

        // Corner case: if min_distance > ideal_distance but spring is fixed (inv_k = 0),
        // we must return min_distance
        return Math.Max(MinDistance, IdealDistance + effectiveForce * invK);
    }
}