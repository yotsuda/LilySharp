// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   scm/auto-beam.scm
//     Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>
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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Where an automatic beam may START and where it MUST end.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/auto-beam.scm:36-127 default-auto-beam-check — the whole decision, asked
/// of one measure position at a time. It is the value of the <c>autoBeamCheck</c> context
/// property (ly/engraver-init.ly:882, <c>autoBeamCheck = #default-auto-beam-check</c>), and
/// the auto-beam engraver calls it twice per stem: once to ask whether the beam it is
/// building must end here, once to ask whether a beam may begin here.
/// <para>
/// ⚠️ THE TYPE THE END TEST IS ASKED ABOUT IS THE SHORTEST NOTE IN THE BEAM SO FAR, not the
/// note at the position (lily/auto-beam-engraver.cc:392-395 — "end should be based on
/// shortest_dur_, begin should be based on current duration"). That is the whole reason a
/// beam of eighths in 3/4 runs the length of the measure while the same bar with one
/// sixteenth in it breaks at the beat: the exception LOOKUP changes, not the beat grid.
/// </para>
/// <para>
/// ⚠️ It is a ONE-PASS rule and cannot be reassembled from two. The entry a meter offers can
/// ask for groups either COARSER than the beat (3/4's eighths, the whole measure) or FINER
/// (6/4's sixteenths, the quarter against a dotted-half beat). A pass that cuts at every beat
/// and then merges can only produce the first kind.
/// </para>
/// </remarks>
internal static class AutoBeamCheck
{
    /// <summary>
    /// Whether a beam is REQUIRED to end at <paramref name="measurePosition"/>, given the
    /// shortest note value it holds.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/auto-beam.scm:82-123 default-auto-beam-check's <c>end?</c> rule. In order: the position is
    /// reduced modulo the beaming period; position zero always ends a beam; otherwise the
    /// meter's exceptions are consulted for <paramref name="type"/>, and with no exception to
    /// be had the beat structure answers.
    /// </remarks>
    public static bool EndsBeam(Fraction measurePosition, Fraction type, BeamingPattern.Options options)
    {
        // LILYPOND-REF: scm/auto-beam.scm:88-89 in default-auto-beam-check — the period is the beat base times the sum of
        // the beat structure, and the position is taken modulo it. LilyPond does this to be
        // "resilient to having a beat structure that is shorter than the measure length"; for
        // a well-formed meter the period IS the measure, and the remainder is the position.
        var pos = EuclideanRemainder(measurePosition, options.Period);

        // LILYPOND-REF: scm/auto-beam.scm:90 in default-auto-beam-check — end at the beginning
        // of the beaming period.
        if (pos == Fraction.Zero)
            return true;

        // LILYPOND-REF: scm/auto-beam.scm:101-108 in default-auto-beam-check — an exact entry for the type wins; failing
        // that, `larger-setting` takes the smallest key that is at least the type, and the
        // moments are then counted in THAT key rather than in the type (:111-119).
        if (LookupException(type, options.BeamExceptions) is var (groups, groupingMoment))
            return IsEndingMoment(pos, groups, groupingMoment);

        // LILYPOND-REF: scm/auto-beam.scm:121-123 in default-auto-beam-check — no exception,
        // so check the beat ending.
        return IsEndingMoment(pos, options.BeatStructure, options.BeatBase);
    }

    /// <summary>
    /// Whether a beam is ALLOWED to start at <paramref name="measurePosition"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/auto-beam.scm:66-79 default-auto-beam-check's <c>start?</c> rule: start anywhere, except at
    /// the halfway point of a 3/N meter with a note that is a sixth of a measure — which would
    /// make the bar look like 6/N.
    /// <para>
    /// ⚠️ ALL OF THAT IS BEHIND ONE OPTION AND THE OPTION IS ON. The first branch is
    /// <c>(get 'beamHalfMeasure #t)</c>, and the default really is <c>#t</c>
    /// (ly/engraver-init.ly:880, beside <c>autoBeamCheck = #default-auto-beam-check</c> at
    /// :882), so the <c>or</c> short-circuits and every position may start a beam. Lily# has
    /// no syntax that sets <c>beamHalfMeasure</c>, so the rest of the rule is unreachable and
    /// is written here rather than executed — the same treatment the CENTER correction pass in
    /// <see cref="BeamingPattern"/> gets, and for the same reason: what makes it dead is a
    /// property of the OPTION, not of the rule, so the day the language grows the option the
    /// rule must already be here and correct. The ledger holds it at
    /// <c>beam.grouping.half-measure-start.first-group</c>, which is 3/4's half measure and
    /// prints one beam of three.
    /// </para>
    /// </remarks>
    public static bool StartsBeam(Fraction measurePosition, Fraction type, BeamingPattern.Options options)
    {
        _ = measurePosition;
        _ = type;
        _ = options;
        return BeamHalfMeasure;
    }

    /// <summary>
    /// <c>beamHalfMeasure</c>, the property the START rule hangs on.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:880 beamHalfMeasure, beside default-auto-beam-check
    /// at :882 — the Voice context's defaults, where the property is <c>##t</c>.
    /// </remarks>
    private const bool BeamHalfMeasure = true;

    /// <summary>
    /// The exception grouping that answers for <paramref name="type"/>, with the note value
    /// its numbers are counted in — or null when the meter offers none.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/auto-beam.scm:101-119 in default-auto-beam-check — <c>type-grouping</c>
    /// (an exact match, counted in the type itself), else <c>default-rule</c> from
    /// <c>larger-setting</c> (:48-49, counted in the key it found).
    /// ⚠️ THE SEARCH GOES UPWARD, and that direction is the whole rule: a sixteenth in 9/4
    /// finds nothing, because 9/4's only entry is keyed on a THIRTY-SECOND, which is smaller.
    /// So 9/4 beams its sixteenths by its dotted-half beats while 6/4 — whose entry is keyed
    /// on the sixteenth itself — beams them by the quarter. Reading it the other way round
    /// makes the two meters look alike and is wrong in both
    /// (ledger beam.grouping.nine-four-sixteenths.groups against six-four-sixteenths.groups).
    /// </remarks>
    private static (ImmutableArray<int> Groups, Fraction Moment)? LookupException(
        Fraction type, ImmutableArray<(Fraction Key, ImmutableArray<int> Groups)> exceptions)
    {
        foreach (var (key, groups) in exceptions)   // sorted ascending by key
        {
            if (key == type)
                return (groups, type);
            if (type < key)
                return (groups, key);
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="pos"/> is one of the moments a grouping ends at: the running
    /// sums of <paramref name="groups"/>, each times <paramref name="moment"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/auto-beam.scm:41-52, default-auto-beam-check's <c>ending-moments</c>
    /// and <c>beat-end?</c> — membership, and EXACT membership at that. A position that falls
    /// strictly inside a group does not end a beam however far into it it is, which is what
    /// lets a beam run through a tuplet whose notes never land on the grid.
    /// The list is walked instead of materialised: it is ascending, so the first sum that
    /// reaches <paramref name="pos"/> decides.
    /// </remarks>
    private static bool IsEndingMoment(Fraction pos, ImmutableArray<int> groups, Fraction moment)
    {
        int beat = 0;
        foreach (int group in groups)
        {
            beat += group;
            var ending = moment * new Fraction(beat);
            if (ending == pos)
                return true;
            if (ending > pos)
                return false;
        }
        return false;
    }

    /// <summary>
    /// <paramref name="value"/> modulo <paramref name="period"/>, for the non-negative
    /// positions a measure walk produces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/auto-beam.scm:89, default-auto-beam-check's
    /// <c>euclidean-remainder</c>. Guile's is defined for
    /// negative arguments too; a measure position never is one, so the floor and the truncation
    /// agree and this is the integer part of the quotient times the period, subtracted.
    /// </remarks>
    private static Fraction EuclideanRemainder(Fraction value, Fraction period)
    {
        if (period <= Fraction.Zero)
            return value;
        var quotient = value / period;
        int whole = quotient.Numerator / quotient.Denominator;   // both positive: floor
        return value - period * new Fraction(whole);
    }
}
