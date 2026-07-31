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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// How many beam lines reach each stem of a beam group, on each side — LilyPond's
/// <c>Stem.beaming</c>.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: lily/beaming-pattern.cc:107-201 <c>Beaming_pattern::beamify</c> — a stem
/// starts with its OWN beam count on both sides (:50-62
/// <c>Beam_rhythmic_element::Beam_rhythmic_element</c> sets
/// <c>beam_count_drul_[LEFT] = beam_count_drul_[RIGHT] = beam_count_</c>) and the passes
/// below only ever REDUCE it, and only for interior stems. So the outer stems keep their own
/// count on the inward side, and a stem whose count exceeds exactly one neighbour's keeps a
/// whole beamlet on the other side.
/// </para>
/// <para>
/// The pass that matters is <c>flag_directions</c> (:116-183): when a stem's count exceeds
/// BOTH its neighbours' there is no side the extra beams can connect to, so LilyPond picks
/// one side to keep them on whole and chips the other. Which side is a three-branch rule
/// ending in <c>rhythmic_importance_</c> (:292-404), a second pass over the pattern — the
/// plainest case there is, an eighth-sixteenth-eighth peak, is the one that reaches the last
/// branch, because its neighbours' counts are equal and the sixteenth neither starts on a
/// beat nor ends on the next one.
/// </para>
/// <para>
/// ⚠️ MOMENTS ARE MEASURED FROM THE START OF THE PERIOD (measure) HOLDING THE FIRST STEM.
/// LilyPond's are absolute and it subtracts <c>measure_offset_</c> — the first stem's
/// measure position — wherever the measure's own origin is wanted (:118, :297), so the two
/// conventions differ by a constant that cancels out of every comparison here.
/// </para>
/// </remarks>
internal static class BeamingPattern
{
    /// <summary>One stem's input, mirroring <c>Beaming_pattern::Beam_rhythmic_element</c>.</summary>
    /// <param name="StartMoment">Position of the stem, measured from the start of the period
    /// holding the group's first stem.</param>
    /// <param name="Duration">The stem's own length, dots included.</param>
    /// <param name="BeamCount">Beams the note carries: <c>duration_log - 2</c>.</param>
    /// <param name="AtSpanStart">This stem begins a tuplet span inside the beam
    /// (<c>Beaming_pattern::at_span_start</c>, :524-531).</param>
    /// <param name="AtSpanStop">This stem ends one (<c>at_span_stop</c>, :532-540).</param>
    internal readonly record struct Element(
        Fraction StartMoment, Fraction Duration, int BeamCount,
        bool AtSpanStart = false, bool AtSpanStop = false);

    /// <summary>
    /// The beat grid the rule reads — LilyPond's <c>Beaming_options</c>, restricted to the
    /// three fields Lily# can produce.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/beaming-pattern.hh:36-69 <c>Beaming_options</c>.
    /// The other four fields are the context properties <c>subdivideBeams</c>,
    /// <c>strictBeatBeaming</c>, <c>respectIncompleteBeams</c> and the two subdivision
    /// intervals. Lily# has no syntax that sets any of them, so they are LilyPond's defaults
    /// (false, false, false, 0 and infinity) wherever the port reads them, and the branches
    /// they guard are marked below rather than written. They come back the day the language
    /// grows a way to set them.
    /// </remarks>
    internal readonly record struct Options(
        Fraction BeatBase, ImmutableArray<int> BeatStructure, Fraction Period)
    {
        /// <summary>
        /// The beat grid of a meter, as LilyPond's default settings define it.
        /// </summary>
        /// <remarks>
        /// LILYPOND-REF: scm/time-signature-settings.scm:367-381 calc-simple-fraction-structure
        /// — the group size is 3 when the numerator is greater than 3 and divisible by 3, and
        /// 1 otherwise; the structure is that group repeated <c>numerator / group</c> times.
        /// LILYPOND-REF: scm/time-signature-settings.scm:125-171 default-time-signature-settings
        /// — the three meters (4/8, 5/8, 8/8) whose table entry OVERRIDES that default with an
        /// uneven grouping.
        /// LILYPOND-REF: scm/time-signature-settings.scm:288-321 beat-base, the time-signature-settings
        /// lookup — with no entry for the meter it is one over the denominator.
        /// LILYPOND-REF: lily/beaming-pattern.cc:562-572 Beaming_options::calc_period — the
        /// period is the beat base times the sum of the structure.
        /// </remarks>
        public static Options For(TimeSignature timeSig)
        {
            // LILYSHARP-OWN: a meter with no beats (senza misura, or a malformed one) has no
            // beat grid at all. LilyPond falls back to the measure length for the PERIOD
            // (:570-571) and would then walk an empty beat structure; Lily# gives it one beat
            // of a whole note so the walk terminates. Nothing observes this: a senza-misura
            // staff has no bar lines and its beams are grouped by hand. It disappears when
            // Lily# carries a real beat structure per meter.
            if (timeSig.SenzaMisura || timeSig.Beats <= 0 || timeSig.BeatType <= 0)
                return new Options(Fraction.Whole, [1], Fraction.Whole);

            var beatBase = new Fraction(1, timeSig.BeatType);
            var structure = DefaultBeatStructure(timeSig);
            int totalBeats = 0;
            foreach (int b in structure) totalBeats += b;
            return new Options(beatBase, structure, beatBase * new Fraction(totalBeats));
        }

        private static ImmutableArray<int> DefaultBeatStructure(TimeSignature timeSig)
        {
            if (timeSig.BeatType == 8)
            {
                switch (timeSig.Beats)
                {
                    case 4: return [2, 2];
                    case 5: return [3, 2];
                    case 8: return [3, 3, 2];
                }
            }

            int group = timeSig.Beats > 3 && timeSig.Beats % 3 == 0 ? 3 : 1;
            var builder = ImmutableArray.CreateBuilder<int>(timeSig.Beats / group);
            for (int i = 0; i < timeSig.Beats / group; i++) builder.Add(group);
            return builder.MoveToImmutable();
        }
    }

    /// <summary>LilyPond's <c>Direction</c>: the side a stem's spare beams are kept on.</summary>
    private const int Left = -1, Center = 0, Right = 1;

    /// <summary>
    /// The beam count on each side of every stem.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beaming-pattern.cc:106-201 <c>Beaming_pattern::beamify</c>.
    /// ⚠️ <c>unbeam_invisible_stems</c> (:471-494) is not ported: it exists for the rests
    /// LilyPond keeps inside a beam (stemlets), and a Lily# beam group holds only beamable
    /// notes and chords — a rest ENDS the group (BeamDetector.IsBeamable). It comes back with
    /// beamed rests.
    /// ⚠️ <c>subdivide_beams</c> (:186-188) is not ported either: it is gated on
    /// <c>subdivideBeams</c>, which Lily# has no syntax to set.
    /// </remarks>
    public static (int Left, int Right)[] Beamify(IReadOnlyList<Element> infos, Options options)
    {
        int n = infos.Count;
        var counts = new (int Left, int Right)[n];
        for (int i = 0; i < n; i++)
            counts[i] = (infos[i].BeamCount, infos[i].BeamCount);

        // LILYPOND-REF: lily/beaming-pattern.cc:109-110 Beaming_pattern::beamify — a pattern
        // of one stem is left alone.
        if (n <= 1)
            return counts;

        var importance = SetRhythmicImportance(infos, options);

        var flagDirections = new int[n];   // all CENTER
        Fraction curBeat = Fraction.Zero;
        // LILYPOND-REF: lily/beaming-pattern.cc:118 next_beat — the walk starts at the
        // measure's own origin (start_moment_ less measure_offset_), which is moment zero in
        // this port's convention (see the type's remarks).
        Fraction nextBeat = Fraction.Zero;
        int remainingBeats = 0;

        // LILYPOND-REF: lily/beaming-pattern.cc:121-158 flag_directions — stems at the
        // boundaries of tuplet spans must stay CENTER, which is why 0 and n-1 are not
        // iterated: they are span boundaries by definition.
        for (int i = 1; i < n - 1; i++)
        {
            int leftCount = infos[i - 1].BeamCount;
            int rightCount = infos[i + 1].BeamCount;

            if (infos[i].AtSpanStart || infos[i].AtSpanStop
                || infos[i].BeamCount <= Math.Min(leftCount, rightCount))
                continue;

            // LILYPOND-REF: lily/beaming-pattern.cc:135-144 remaining_beats — advance the beat
            // walk to this stem, refilling the list from beat_structure_ when it runs out. The
            // walk never rewinds, so a stem the branch above skipped does not move it either.
            while (nextBeat <= infos[i].StartMoment)
            {
                if (remainingBeats >= options.BeatStructure.Length)
                    remainingBeats = 0;
                curBeat = nextBeat;
                nextBeat += new Fraction(options.BeatStructure[remainingBeats]) * options.BeatBase;
                remainingBeats++;
            }

            bool pointRight;
            // LILYPOND-REF: lily/beaming-pattern.cc:147-148 point_right from left_count and
            // right_count. The other half of that condition is strict_beat_beaming_, the
            // context property strictBeatBeaming — false by default and unsettable in Lily#,
            // so only the count comparison is written.
            if (leftCount != rightCount)
                pointRight = rightCount > leftCount;
            // LILYPOND-REF: lily/beaming-pattern.cc:149-151 start_moment_ against end_moment —
            // when exactly one of "starts on this beat" and "ends on the next" holds, the
            // beams point away from the beat.
            else if ((infos[i].StartMoment == curBeat) != (EndMoment(infos[i]) == nextBeat))
                pointRight = infos[i].StartMoment == curBeat;
            // LILYPOND-REF: lily/beaming-pattern.cc:153-154 point_right from rhythmic_importance_
            // — otherwise the neighbour standing at the more important moment wins. Smaller is
            // more important.
            else
                pointRight = importance[i] < importance[i + 1];

            flagDirections[i] = pointRight ? Right : Left;
        }

        // LILYPOND-REF: lily/beaming-pattern.cc:161-167 flag_directions — a CENTER standing
        // between a LEFT and a RIGHT takes its neighbour's direction.
        // ⚠️ WITH LILYPOND'S DEFAULT OPTIONS THIS PASS CANNOT CHANGE A COUNT, and it is
        // written out anyway because that is a property of the options rather than of the
        // rule. A CENTER stem has count <= min(neighbours), so the chip below only fires for
        // it when its count EQUALS the opposite neighbour's — and working that back through
        // the branch that made the adjacent stem LEFT (or RIGHT) contradicts itself unless
        // strictBeatBeaming is on. Turn that option on and the pass starts to bite.
        for (int i = 1; i < n - 1; i++)
        {
            if (flagDirections[i] == Center && flagDirections[i - 1] == Left)
                flagDirections[i] = Right;
            if (flagDirections[i] == Center && flagDirections[i + 1] == Right)
                flagDirections[i] = Left;
        }

        // LILYPOND-REF: lily/beaming-pattern.cc:169-183 beam_count_drul_[opposite_dir] — the
        // chip. The flagged side keeps the stem's own count; the OTHER side is reduced by
        // max(count - neighbour, 1), and only when this stem is the one with beams to spare.
        for (int i = 1; i < n - 1; i++)
        {
            if (flagDirections[i] == Center)
                continue;

            int oppositeDir = -flagDirections[i];
            int neighbour = i + oppositeDir;
            if (infos[i].BeamCount >= infos[neighbour].BeamCount)
            {
                int chip = Math.Max(infos[i].BeamCount - infos[neighbour].BeamCount, 1);
                if (oppositeDir == Left)
                    counts[i].Left -= chip;
                else
                    counts[i].Right -= chip;
            }
        }

        // LILYPOND-REF: lily/beaming-pattern.cc:190-200 at_span_start / at_span_stop — a
        // beamlet must not stick out of the tuplet its stem belongs to.
        for (int i = 1; i < n - 1; i++)
        {
            if (infos[i].AtSpanStart)
                counts[i].Left = Math.Min(counts[i].Left, counts[i - 1].Right);
            else if (infos[i].AtSpanStop)
                counts[i].Right = Math.Min(counts[i].Right, counts[i + 1].Left);
        }

        return counts;
    }

    /// <summary>End of a stem's own note — <c>Beaming_pattern::end_moment</c> (:518-522).</summary>
    private static Fraction EndMoment(Element info) => info.StartMoment + info.Duration;

    /// <summary>
    /// How significant the moment each stem falls on is; smaller is more significant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/beaming-pattern.cc:291-404 Beaming_pattern::set_rhythmic_importance.
    /// Consecutive thirty-seconds get
    /// 0 3 2 3 1 3 2 3 …: the binary logarithm of the denominator of the stem's position
    /// within its beat, so a stem on a half-beat outranks one on a quarter-beat.
    /// </para>
    /// <para>
    /// ⚠️ THE TUPLET SPAN CONTEXTS ARE NOT PORTED (:296-347, the <c>Span_position</c> list and
    /// <c>current_factor</c>). They rescale a stem's position by the tuplet it is written
    /// inside, and Lily# has no tuplet span in its beam model at all — a beam group carries
    /// notes at their WRITTEN positions (see BeamDetector's tupletInteriors), which is the
    /// same convention this reads. What is lost is a beam INSIDE a tuplet whose interior stem
    /// exceeds both neighbours AND whose neighbours' counts are equal AND which is ambiguous
    /// on the beat test: only then does the answer come from here at all. NO POINT OBSERVES
    /// IT — the six ledger books are all in plain 4/4 — so it would have to be measured
    /// before it could be closed. It disappears when the beam model carries tuplet spans.
    /// </para>
    /// <para>
    /// ⚠️ Two more branches are left out because their options are unsettable in Lily#:
    /// <c>beamMaximumSubdivision</c> (:374-378, infinity by default, so the <c>isfinite</c>
    /// test is false) and <c>respectIncompleteBeams</c> (:395-400, false by default).
    /// </para>
    /// </remarks>
    private static int[] SetRhythmicImportance(IReadOnlyList<Element> infos, Options options)
    {
        var importance = new int[infos.Count];

        // LILYPOND-REF: lily/beaming-pattern.cc:296-298 span_contexts — the ROOT Span_position,
        // whose beat base is the whole PERIOD (not the beat) and whose beat length is 1.
        // Without tuplets it is the only span there ever is.
        Fraction current = Fraction.Zero, next = Fraction.Zero;
        int momentNum = -1;

        for (int i = 0; i < infos.Count; i++)
        {
            Fraction stemPos = infos[i].StartMoment;

            // LILYPOND-REF: lily/beaming-pattern.cc:258-267 Span_position::update.
            while (next <= stemPos)
            {
                current = next;
                next += options.Period;
                momentNum++;
            }

            if (stemPos == current)
            {
                // LILYPOND-REF: lily/beaming-pattern.cc:358-363 rhythmic_importance_for_length
                // — a stem ON the period's own moment is ranked by the period's length,
                // lifted by the span's beat_level.
                importance[i] = RhythmicImportanceForLength(next - current) - BeatLevel(momentNum);
            }
            else
            {
                // LILYPOND-REF: lily/beaming-pattern.cc:366-390 moment_relative_to_beat.
                Fraction relative = stemPos - current;
                importance[i] = RhythmicImportanceForPosition(relative);
                // …and never finer than what is LEFT of the period, which is what keeps a
                // sextuplet of six equal notes from subdividing after the second.
                importance[i] = Math.Max(
                    importance[i], RhythmicImportanceForLength(next - stemPos));
            }
        }

        return importance;
    }

    /// <summary>
    /// LILYPOND-REF: lily/beaming-pattern.cc:279-289 Span_position::beat_level.
    /// The root span's beat length is 1, so the modulo is always zero and only the first
    /// moment is exempt; every other is ranked by the lowest set bit of its index.
    /// </summary>
    private static int BeatLevel(int momentNum)
        => momentNum == 0 ? 0 : IntLog2(momentNum & -momentNum) + 1;

    /// <summary>LILYPOND-REF: lily/beaming-pattern.cc:81-85 <c>rhythmic_importance_for_position</c>.</summary>
    private static int RhythmicImportanceForPosition(Fraction r)
        => IntLog2(r.Denominator) - 2 - (r.Denominator == 1 ? IntLog2(r.Numerator) : 0);

    /// <summary>LILYPOND-REF: lily/beaming-pattern.cc:86-90 <c>rhythmic_importance_for_length</c>.</summary>
    private static int RhythmicImportanceForLength(Fraction r)
        => IntLog2(r.Denominator) - 2 - IntLog2(r.Numerator);

    /// <summary>
    /// The base-2 logarithm, rounded down.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/include/misc.hh — the <c>intlog2</c> template at :30-48, which
    /// raises an error on a non-positive argument rather than returning something. (No line
    /// range on the address: the only name at those lines is intlog2, which is one word, and
    /// LpReferenceCitationTests can only recognise a multi-part one.)
    /// Both callers here are fed
    /// strictly positive fractions (a length, or a stem's offset INTO a beat), so the throw
    /// is a guard on that reasoning, not a case to handle.
    /// </remarks>
    private static int IntLog2(int d)
    {
        if (d <= 0)
            throw new ArgumentOutOfRangeException(nameof(d), d, "intlog2 needs a positive argument");
        int i = 0;
        while (d != 1) { d /= 2; i++; }
        return i;
    }
}
