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
    /// <param name="Tuplet">The INNERMOST tuplet span the stem belongs to, or null outside
    /// any tuplet — <c>Beam_rhythmic_element</c>'s <c>tuplet_</c>. The span boundary tests
    /// (<see cref="AtSpanStart"/> / <see cref="AtSpanStop"/>) and the span stack in
    /// <see cref="SetRhythmicImportance"/> both read it.</param>
    /// <param name="Invisible">A stem with nothing to hang beams from — a beamed REST.
    /// LilyPond gives every beamed rest a headless Stem (Rest carries the rhythmic-head
    /// interface the stem engraver acknowledges), and a headless stem answers
    /// <c>Stem::is_invisible</c>, which is the flag the beam engraver forwards into the
    /// pattern (lily/template-engraver-for-beams.cc:69-78 add_stem).</param>
    internal readonly record struct Element(
        Fraction StartMoment, Fraction Duration, int BeamCount,
        TupletDescription? Tuplet = null, bool Invisible = false);

    /// <summary>
    /// A tuplet span as the pattern reads it — LilyPond's <c>Tuplet_description</c>,
    /// restricted to the five fields the pattern consumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/include/tuplet-description.hh:38-44 start_moment_ / stop_moment_ /
    /// <c>parent_</c> for nesting / <c>numerator_</c> and <c>denominator_</c>.
    /// Start and Stop are in the same moment convention as the stems' (see the type's
    /// remarks); LilyPond's are absolute Moments read through <c>tuplet_start ()</c> /
    /// <c>tuplet_stop ()</c> (lily/tuplet-description.cc:51-64), and every use here compares
    /// them against stem moments, so the constant offset cancels the same way.
    /// </para>
    /// <para>
    /// ⚠️ NUMERATOR AND DENOMINATOR ARE LILYPOND'S EVENT FIELDS, WHICH ARE THE WRITTEN RATIO
    /// REVERSED: <c>\tuplet 3/2</c> stores numerator 2, denominator 3
    /// (ly/music-functions-init.ly:2488-2494 — <c>'numerator (cdr ratio)</c>), so
    /// Numerator/Denominator is the factor that scales WRITTEN time into ACTUAL time (2/3
    /// for a triplet) and Denominator is the printed digit. A Lily# TupletBracketItem holds
    /// the printed ratio, so the wiring in BeamDetector swaps its two fields.
    /// </para>
    /// <para>
    /// A CLASS rather than a record: the span stack compares descriptions by OBJECT IDENTITY
    /// exactly as LilyPond compares <c>Tuplet_description</c> pointers
    /// (beaming-pattern.cc:309-346), and a record's value equality could conflate two
    /// distinct spans.
    /// </para>
    /// </remarks>
    internal sealed class TupletDescription(
        Fraction start, Fraction stop, int numerator, int denominator, TupletDescription? parent)
    {
        /// <summary><c>tuplet_start ()</c>.</summary>
        public Fraction Start { get; } = start;
        /// <summary><c>tuplet_stop ()</c>.</summary>
        public Fraction Stop { get; } = stop;
        /// <summary><c>numerator_</c> — the written ratio's SECOND number (2 in 3/2).</summary>
        public int Numerator { get; } = numerator;
        /// <summary><c>denominator_</c> — the printed digit (3 in 3/2).</summary>
        public int Denominator { get; } = denominator;
        /// <summary><c>parent_</c> — the enclosing tuplet, for nested spans.</summary>
        public TupletDescription? Parent { get; } = parent;
    }

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
        Fraction BeatBase, ImmutableArray<int> BeatStructure, Fraction Period,
        ImmutableArray<(Fraction Key, ImmutableArray<int> Groups)> BeamExceptions = default)
    {
        /// <summary>
        /// The meter's <c>beamExceptions</c>, sorted by key ascending — groupings that end a
        /// beam somewhere other than the beat, keyed on the note value they answer for.
        /// </summary>
        /// <remarks>
        /// LILYPOND-REF: scm/time-signature-settings.scm:69-173 default-time-signature-settings —
        /// every <c>(beamExceptions . ((end . ((key . (groups…))))))</c> in the table: 2/2 at
        /// :74, 2/8 at :81, 3/2 at :90, 3/4 at :99-100, 3/8 at :104, 4/2 at :112, 4/4 at
        /// :120-121, 6/4 at :133, 9/4 at :144, 12/4 at :155. Sorted here because the lookup
        /// that misses an exact key takes the SMALLEST key that is at least the type
        /// (scm/auto-beam.scm:48-49 larger-setting), and LilyPond sorts the alist by key for
        /// exactly that reason (scm/auto-beam.scm:91-100, the <c>car&lt;</c> sort).
        /// ⚠️ A meter absent from the table has NO exceptions and beams by its beats alone —
        /// that is what makes 2/4 group its eighths in twos where 4/4 groups them in fours.
        /// </remarks>
        public ImmutableArray<(Fraction Key, ImmutableArray<int> Groups)> BeamExceptions { get; }
            = BeamExceptions.IsDefault ? [] : BeamExceptions;

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
            return new Options(beatBase, structure, beatBase * new Fraction(totalBeats),
                DefaultBeamExceptions(timeSig));
        }

        /// <summary>
        /// The <c>beamExceptions</c> the default settings give this meter, sorted by key.
        /// </summary>
        /// <remarks>
        /// LILYPOND-REF: scm/time-signature-settings.scm:69-173 default-time-signature-settings.
        /// The whole table, transcribed: every entry that carries a <c>beamExceptions</c>, and
        /// no others. 4/8, 5/8 and 8/8 are in the table too but carry a <c>beatStructure</c>
        /// instead, which <see cref="DefaultBeatStructure"/> holds.
        /// <para>
        /// ⚠️ THE KEY IS A NOTE VALUE, NOT A GROUP LENGTH, and the groups are counted in it:
        /// 6/4's <c>(1/16 . (4 4 4 4 4 4))</c> means six groups of four SIXTEENTHS — the
        /// quarter — against a beat structure of dotted halves. The lookup that consumes this
        /// (scm/auto-beam.scm:101-120) multiplies the running sum by the key, or by the
        /// LARGER key it settled for, which is why both are kept.
        /// </para>
        /// </remarks>
        private static ImmutableArray<(Fraction Key, ImmutableArray<int> Groups)> DefaultBeamExceptions(
            TimeSignature timeSig)
            // The table is a CONSTANT, so it is built once and handed out, not rebuilt per
            // measure. (It was rebuilt per measure for one commit, and the price showed up on
            // the one benchmark label that has no beams in it at all: 0.060 ms of walking 400
            // bars of quarters became 0.155.)
            => (timeSig.Beats, timeSig.BeatType) switch
            {
                // :74 in 2/2: end beams with 32nd notes each 1/4 beat.
                (2, 2) => TwoTwo,
                // :81 in 2/8: beam the entire measure together.
                (2, 8) => TwoEight,
                // :90 in 3/2: 32nd notes and finer each 1/4 beat.
                (3, 2) => ThreeTwo,
                // :99-100 in 3/4: eighths the whole measure, anything shorter back to the beat.
                (3, 4) => ThreeFour,
                // :104 in 3/8: beam the entire measure together.
                (3, 8) => ThreeEight,
                // :112 in 4/2: 16th notes or finer each 1/4 beat.
                (4, 2) => FourTwo,
                // :120-121 in 4/4: eighths by the half measure, anything shorter by the beat.
                (4, 4) => FourFour,
                // :133 in 6/4: 16th or finer each 1/4 beat.
                (6, 4) => SixFour,
                // :144 in 9/4 and :155 in 12/4: 32nd or finer each 1/4 beat.
                (9, 4) => NineFour,
                (12, 4) => TwelveFour,
                _ => [],
            };

        private static ImmutableArray<int> Repeat(int value, int count)
        {
            var b = ImmutableArray.CreateBuilder<int>(count);
            for (int i = 0; i < count; i++) b.Add(value);
            return b.MoveToImmutable();
        }

        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> TwoTwo
            = [(new Fraction(1, 32), Repeat(8, 4))];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> TwoEight
            = [(new Fraction(1, 8), [2])];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> ThreeTwo
            = [(new Fraction(1, 32), Repeat(8, 6))];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> ThreeFour
            = [(new Fraction(1, 12), [3, 3, 3]), (new Fraction(1, 8), [6])];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> ThreeEight
            = [(new Fraction(1, 8), [3])];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> FourTwo
            = [(new Fraction(1, 16), Repeat(4, 8))];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> FourFour
            = [(new Fraction(1, 12), [3, 3, 3, 3]), (new Fraction(1, 8), [4, 4])];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> SixFour
            = [(new Fraction(1, 16), Repeat(4, 6))];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> NineFour
            = [(new Fraction(1, 32), Repeat(8, 8))];
        private static readonly ImmutableArray<(Fraction, ImmutableArray<int>)> TwelveFour
            = [(new Fraction(1, 32), Repeat(8, 12))];

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
    /// ⚠️ <c>subdivide_beams</c> (:186-188) is not ported: it is gated on
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

        // LILYPOND-REF: lily/beaming-pattern.cc:471-494 unbeam_invisible_stems — "invisible
        // stems should be treated as though they have the same number of beams as their
        // least-beamed neighbour": a beamed rest's own count is clamped to min(previous) in
        // one pass and min(next) in a second, and the CLAMPED count is what every later pass
        // reads (count() reads beam_count_, which those loops overwrite). That clamp is how
        // the beams that survive OVER a rest are exactly the ones both sides carry, and how
        // the extra ones become beamlets on the visible neighbours.
        var beamCount = new int[n];
        for (int i = 0; i < n; i++)
            beamCount[i] = infos[i].BeamCount;
        for (int i = 1; i < n; i++)
            if (infos[i].Invisible)
                beamCount[i] = Math.Min(beamCount[i], beamCount[i - 1]);
        for (int i = 0; i < n - 1; i++)
            if (infos[i].Invisible)
                beamCount[i] = Math.Min(beamCount[i], beamCount[i + 1]);
        for (int i = 0; i < n; i++)
            if (infos[i].Invisible)
                counts[i] = (beamCount[i], beamCount[i]);

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
            int leftCount = beamCount[i - 1];
            int rightCount = beamCount[i + 1];

            if (AtSpanStart(infos, i) || AtSpanStop(infos, i)
                || beamCount[i] <= Math.Min(leftCount, rightCount))
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
        // ⚠️ WITH LILYPOND'S DEFAULT OPTIONS AND NO INVISIBLE STEMS THIS PASS CANNOT CHANGE
        // A COUNT, and it is written out anyway because that is a property of the options
        // rather than of the rule. A CENTER stem has count <= min(neighbours), so the chip
        // below only fires for it when its count EQUALS the opposite neighbour's — and
        // working that back through the branch that made the adjacent stem LEFT (or RIGHT)
        // contradicts itself unless strictBeatBeaming is on. An INVISIBLE stem, though, is
        // routinely clamped to exactly a neighbour's count, so for it the fill-then-chip can
        // fire — in LilyPond just as here.
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
            if (beamCount[i] >= beamCount[neighbour])
            {
                int chip = Math.Max(beamCount[i] - beamCount[neighbour], 1);
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
            if (AtSpanStart(infos, i))
                counts[i].Left = Math.Min(counts[i].Left, counts[i - 1].Right);
            else if (AtSpanStop(infos, i))
                counts[i].Right = Math.Min(counts[i].Right, counts[i + 1].Left);
        }

        return counts;
    }

    /// <summary>End of a stem's own note — <c>Beaming_pattern::end_moment</c> (:518-522).</summary>
    private static Fraction EndMoment(Element info) => info.StartMoment + info.Duration;

    /// <summary>
    /// This stem stands on the first moment of its tuplet span — or of the whole beam, when
    /// the tuplet opened before it (or there is none).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beaming-pattern.cc:524-531 <c>at_span_start</c> — the tuplet's
    /// start clamped to the beam's (<c>max (tuplet_start, start_moment (0))</c>): a beam
    /// picking up MID-tuplet treats its own first stem as the boundary. Without a tuplet the
    /// test is against the first stem's moment, which no later stem can equal.
    /// </remarks>
    private static bool AtSpanStart(IReadOnlyList<Element> infos, int i)
    {
        Fraction first = infos[0].StartMoment;
        return infos[i].StartMoment
            == (infos[i].Tuplet is { } t && t.Start > first ? t.Start : first);
    }

    /// <summary>This stem's note ends its tuplet span — <c>at_span_stop</c> (:532-540), the
    /// mirror of <see cref="AtSpanStart"/> with the tuplet's stop clamped to the last stem's
    /// end.</summary>
    private static bool AtSpanStop(IReadOnlyList<Element> infos, int i)
    {
        Fraction last = EndMoment(infos[infos.Count - 1]);
        return EndMoment(infos[i])
            == (infos[i].Tuplet is { } t && t.Stop < last ? t.Stop : last);
    }

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
    /// The span stack (:294-347): every stem is ranked inside the DEEPEST tuplet span that
    /// has already opened — the front of the list. Spans expire when the stem reaches their
    /// stop (:308-318) and open only once a stem stands STRICTLY past their start (:328), so
    /// the stem ON a span's first moment is ranked by the PARENT context, as if it were the
    /// whole child tuplet (:335-343, :353-357). <c>current_factor</c> follows the openings
    /// and expiries (:299-302, :314-316, :332-333) and divides every moment back into
    /// WRITTEN proportions — a triplet's middle eighth reads as an eighth again, not as a
    /// twelfth. The observer for this whole block is LP regression beamlet-test.ly's last
    /// bar, <c>tuplet 5/4 {a8 a32 a8 a16. a8 a8}</c>: the a32 stands on a span moment
    /// (importance 1, against the next stem's 3), so its two beamlets point RIGHT; with the
    /// root span alone the tie-break reads 1 against 1 and pointed them LEFT.
    /// </para>
    /// <para>
    /// ⚠️ Two branches are left out because their options are unsettable in Lily#:
    /// <c>beamMaximumSubdivision</c> (:374-378, infinity by default, so the <c>isfinite</c>
    /// test is false) and <c>respectIncompleteBeams</c> (:395-400, false by default —
    /// which is also why <see cref="SpanPosition"/> does not store <c>end_moment_</c>,
    /// whose only reader that branch is).
    /// </para>
    /// </remarks>
    private static int[] SetRhythmicImportance(IReadOnlyList<Element> infos, Options options)
    {
        var importance = new int[infos.Count];

        // LILYPOND-REF: lily/beaming-pattern.cc:294-298 span_contexts — never empty: the
        // ROOT span, whose beat base is the whole PERIOD (not the beat), whose beat length
        // is 1, and whose start is the measure's own origin — moment zero in this port's
        // convention (see the type's remarks). The front of the list is the deepest open span.
        var spans = new LinkedList<SpanPosition>();
        spans.AddFirst(new SpanPosition(options.Period, 1, Fraction.Zero));
        // LILYPOND-REF: lily/beaming-pattern.cc:299-302 current_factor — the stems' own
        // duration factors are not sufficient, so the factor is maintained by hand from the
        // spans opening and expiring.
        Fraction currentFactor = Fraction.Whole;

        for (int i = 0; i < infos.Count; i++)
        {
            Fraction stemPos = infos[i].StartMoment;

            // LILYPOND-REF: lily/beaming-pattern.cc:308-318 tuplet_stop / pop_front — delete
            // expired tuplet spans, undoing their factors. The root's Tuplet is null, so the
            // walk always stops.
            while (spans.First!.Value.Tuplet is { } curTuplet)
            {
                if (curTuplet.Stop > stemPos)
                    break;
                currentFactor = currentFactor / new Fraction(curTuplet.Numerator)
                    * new Fraction(curTuplet.Denominator);
                spans.RemoveFirst();
            }

            // LILYPOND-REF: lily/beaming-pattern.cc:320-347 tuplet_start / emplace_after —
            // open the stem's not-yet-open
            // spans, the deepest ending up in front. The parent-chain walk from the stem's
            // innermost tuplet must reach the front's tuplet: a span still in front contains
            // this stem's moment, so it is on the stem's chain. For i > 0 the walk may stop
            // at the first span not yet strictly begun, because such a stem is guaranteed to
            // be that span's first note (:335-343); the first stem of the BEAM is not — the
            // beam may pick up mid-tuplet — so its whole chain is examined.
            {
                LinkedListNode<SpanPosition>? insertAfter = null;
                TupletDescription? currentParent = spans.First!.Value.Tuplet;
                TupletDescription? tupletIt = infos[i].Tuplet;
                while (!ReferenceEquals(tupletIt, currentParent))
                {
                    if (tupletIt!.Start < stemPos)
                    {
                        var opened = new SpanPosition(tupletIt);
                        insertAfter = insertAfter is null
                            ? spans.AddFirst(opened)
                            : spans.AddAfter(insertAfter, opened);
                        currentFactor = currentFactor * new Fraction(tupletIt.Numerator)
                            / new Fraction(tupletIt.Denominator);
                    }
                    else if (i > 0)
                        break;

                    tupletIt = tupletIt.Parent;
                }
            }

            // LILYPOND-REF: lily/beaming-pattern.cc:349-351 span_contexts.front — the span
            // whose moments rank this stem, walked up to it.
            SpanPosition curPosition = spans.First!.Value;
            curPosition.Update(stemPos);

            if (stemPos == curPosition.CurrentMoment)
            {
                // LILYPOND-REF: lily/beaming-pattern.cc:358-363 rhythmic_importance_for_length
                // — a stem ON the span's own moment is ranked by the moment's WRITTEN length,
                // lifted by the span's beat_level.
                importance[i] = RhythmicImportanceForLength(
                        (curPosition.NextMoment - curPosition.CurrentMoment) / currentFactor)
                    - curPosition.BeatLevel();
            }
            else
            {
                // LILYPOND-REF: lily/beaming-pattern.cc:366-390 moment_relative_to_beat,
                // read in written time.
                Fraction relative = (stemPos - curPosition.CurrentMoment) / currentFactor;
                importance[i] = RhythmicImportanceForPosition(relative);
                // …and never finer than what is LEFT of the span's moment, which is what
                // keeps a sextuplet of six equal notes from subdividing after the second.
                importance[i] = Math.Max(importance[i], RhythmicImportanceForLength(
                    (curPosition.NextMoment - stemPos) / currentFactor));
            }
        }

        return importance;
    }

    /// <summary>
    /// The walking beat position of one span layer — LilyPond's <c>Span_position</c>, one
    /// per open tuplet span plus the root.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beaming-pattern.cc:203-289 Span_position. A tuplet's beat base is
    /// its actual length over its denominator — the actual length of one WRITTEN unit — and
    /// its beat length is the odd part of the denominator (3 for a sextuplet), so
    /// <see cref="BeatLevel"/> can rank the tuplet's own internal power-of-two structure.
    /// <c>end_moment_</c> is not stored: its only reader is the unported
    /// <c>respectIncompleteBeams</c> branch (see <see cref="SetRhythmicImportance"/>).
    /// </remarks>
    private sealed class SpanPosition
    {
        private readonly Fraction _beatBase;
        private readonly int _beatLength; // stays constant
        private Fraction _current, _next;
        private int _momentNum = -1;

        /// <summary>The span's tuplet; null for the root.</summary>
        public TupletDescription? Tuplet { get; }

        /// <summary>LILYPOND-REF: lily/beaming-pattern.cc:231-245 Span_position — the tuplet
        /// constructor. current_moment_ starts at next_moment_'s value to be safe against a
        /// negative tuplet start.</summary>
        public SpanPosition(TupletDescription tuplet)
        {
            _beatBase = (tuplet.Stop - tuplet.Start) / new Fraction(tuplet.Denominator);
            _beatLength = tuplet.Denominator / (tuplet.Denominator & -tuplet.Denominator);
            _current = tuplet.Start;
            _next = tuplet.Start;
            Tuplet = tuplet;
        }

        /// <summary>LILYPOND-REF: lily/beaming-pattern.cc:246-255 Span_position — the root
        /// constructor, instantiated only once.</summary>
        public SpanPosition(Fraction beatBase, int beatLength, Fraction start)
        {
            _beatBase = beatBase;
            _beatLength = beatLength;
            _current = start;
            _next = start;
        }

        /// <summary>LILYPOND-REF: lily/beaming-pattern.cc:257-267 Span_position::update —
        /// must be called before each stem to align the moments.</summary>
        public void Update(Fraction pos)
        {
            while (_next <= pos)
            {
                _current = _next;
                _next += _beatBase;
                _momentNum++;
            }
        }

        public Fraction CurrentMoment => _current;
        public Fraction NextMoment => _next;

        /// <summary>
        /// LILYPOND-REF: lily/beaming-pattern.cc:279-289 Span_position::beat_level —
        /// incomplete 'beats' and the span's own first moment rank as level zero; a complete
        /// beat is lifted by the lowest set bit of its index. The root's beat length is 1,
        /// so for it the modulo is always zero and only the first moment is exempt.
        /// </summary>
        public int BeatLevel()
        {
            if (_momentNum == 0 || _momentNum % _beatLength != 0)
                return 0;

            int beatNum = _momentNum / _beatLength;
            return IntLog2(beatNum & -beatNum) + 1;
        }
    }

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
