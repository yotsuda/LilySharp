// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Parts of this file are ported from LilyPond, the GNU music typesetter.
// The C# is a modified translation of the following, not a copy of it:
//   lily/beam.cc
//     Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>;
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

using System;
using System.Collections.Generic;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Faithful port of LilyPond's beam-subdivision maths: it assigns every beam line
/// a vertical <em>rank</em> per stem, then collects those into drawable segments.
/// A rank is measured in beam-translation units from the primary beam line and, by
/// LilyPond's convention, larger ranks sit closer to a stem's own noteheads
/// (lily/beam.cc:477-478). This is what decides which side a secondary beam takes,
/// keeps a beam through a knee two straight parallel lines, and places the beam
/// corners — none of which a per-note heuristic gets right in every case.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:261 position_with_maximal_common_beams
/// LILYPOND-REF: lily/beam.cc:294 Beam::calc_beaming
/// LILYPOND-REF: lily/beam.cc:457 Beam::calc_beam_segments
/// </remarks>
internal static class BeamSubdivision
{
    /// <summary>One stem's input to the subdivision maths.</summary>
    /// <param name="LeftCount">Number of beams on the stem's left (toward the previous note).</param>
    /// <param name="RightCount">Number of beams on the stem's right.</param>
    /// <param name="Dir">Stem direction: +1 up, −1 down.</param>
    /// <param name="X">Stem X (staff spaces).</param>
    internal readonly record struct StemBeaming(int LeftCount, int RightCount, int Dir, double X);

    /// <summary>The assigned ranks of one stem's beams, per side.</summary>
    internal sealed class StemRanks
    {
        public List<int> Left { get; } = new();
        public List<int> Right { get; } = new();

        /// <summary>How many ranks this stem touches (left ∪ right, with multiplicity).</summary>
        /// <remarks>
        /// ⚠️ THIS PAIR EXISTS IN ORDER NOT TO ALLOCATE, and that is the only reason the union
        /// is not a walk. It was one <c>yield return</c> method, and a C# iterator builds its
        /// state machine on the CALL rather than on the first <c>MoveNext</c> — 64 bytes per
        /// ask, whether the stem carries four ranks or none. MEASURED (2026-09-20, session 446;
        /// the reader's corpus, 231 books x 8 forward keystrokes, Release): the two readers
        /// asked 83.02 + 27.84 times a keystroke, which is 7,095 B/keystroke for a walk of a
        /// handful of ints. The order is LEFT first, then RIGHT: <see cref="Multiplicity"/>
        /// takes a max or a min and cannot see it, but <c>CalcBeamSegments</c> feeds a
        /// SortedSet, so the order is kept rather than argued about — and that reading was
        /// measured, not assumed: session 446 poisoned the order (right side first) and all
        /// 8,774 tests stayed green, while dropping the right side from the COUNT reddened
        /// eight. What the suite observes here is membership; the order is kept because it is
        /// free, not because a test asks for it (RULES §5.4).
        /// </remarks>
        public int AllCount => Left.Count + Right.Count;

        /// <summary>The i-th rank of left ∪ right, the left side first.</summary>
        public int AllAt(int i) => i < Left.Count ? Left[i] : Right[i - Left.Count];

        /// <summary>
        /// The extreme rank in the stem's direction — LilyPond's
        /// <c>beam_multiplicity[stem_dir]</c> (lily/stem.cc:1269 unites left+right,
        /// then indexes by direction: UP→max, DOWN→min). The stem end reaches this.
        /// </summary>
        public int Multiplicity(int dir)
        {
            bool any = false;
            int best = 0;
            int n = AllCount;
            for (int i = 0; i < n; i++)
            {
                int r = AllAt(i);
                if (!any) { best = r; any = true; continue; }
                best = dir > 0 ? Math.Max(best, r) : Math.Min(best, r);
            }
            return best;
        }
    }

    /// <summary>A drawable beam segment: a horizontal span at one vertical rank.</summary>
    internal readonly record struct Segment(int Rank, double XLeft, double XRight);

    /// <summary>
    /// LILYPOND-REF: lily/beam.cc:261 position_with_maximal_common_beams.
    /// Finds the integer shift for the current stem's beams that maximises the count
    /// of beams shared with the previous stem's right side (so the beams line up and
    /// connect). Ties resolve to the last candidate unless <paramref name="specialShift"/>.
    /// </summary>
    private static int PositionMaxCommon(
        IReadOnlyList<int> prevRight, IReadOnlyList<int> curLeft,
        int leftDir, int rightDir, bool specialShift)
    {
        if (prevRight.Count == 0) return 0;
        int lmin = int.MaxValue, lmax = int.MinValue;
        // Indexed, not foreach, here and below: both rank lists arrive as interfaces, so
        // foreach would box an enumerator — and the inner one once per candidate shift
        // (RULES §5.3).
        for (int p = 0; p < prevRight.Count; p++)
        {
            int v = prevRight[p];
            if (v < lmin) lmin = v;
            if (v > lmax) lmax = v;
        }
        // i ranges over the previous stem's right-rank span, stepping in leftDir.
        int start = leftDir > 0 ? lmin : lmax;
        int endB = leftDir > 0 ? lmax : lmin;
        int bestCount = 0, bestStart = 0;
        for (int i = start; (i - endB) * leftDir <= 0; i += leftDir)
        {
            int count = 0;
            for (int c = 0; c < curLeft.Count; c++)
            {
                int k = -rightDir * curLeft[c] + i;
                if (Contains(prevRight, k)) count++;
            }
            if (count > bestCount || (count == bestCount && !specialShift))
            {
                bestCount = count;
                bestStart = i;
            }
        }
        return bestStart;
    }

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }

    /// <summary>
    /// LILYPOND-REF: lily/beam.cc:294 Beam::calc_beaming.
    /// Assigns each stem's left/right beams to aligned integer ranks (relative to the
    /// primary at rank 0). Handles knee direction flips via special_shift so the beam
    /// forms a corner rather than incrementally shifting.
    /// </summary>
    public static StemRanks[] CalcBeaming(IReadOnlyList<StemBeaming> stems)
    {
        int n = stems.Count;
        var ranks = new StemRanks[n];
        for (int i = 0; i < n; i++)
        {
            ranks[i] = new StemRanks();
            // Initial beaming: beam indices {0..count-1} on each side (0 = primary).
            for (int s = 0; s < stems[i].LeftCount; s++) ranks[i].Left.Add(s);
            for (int s = 0; s < stems[i].RightCount; s++) ranks[i].Right.Add(s);
        }

        var lastRight = new List<int> { 0 };  // last_beaming = (() . (0))
        int lastDir = 0;
        int lastRightCount = 0;
        // first_slice_of_prev_dirs[0], [1] — both Slice(0) initially.
        int[] fsMin = { 0, 0 };
        int[] fsMax = { 0, 0 };

        for (int i = 0; i < n; i++)
        {
            var curLeft = ranks[i].Left;
            var curRight = ranks[i].Right;
            if (curLeft.Count == 0 && curRight.Count == 0) continue;

            int thisDir = stems[i].Dir;
            int rightBeamCount = curRight.Count;
            int leftBeamCount = curLeft.Count;

            bool specialShift = thisDir * lastDir < 0
                && lastRightCount >= leftBeamCount
                && lastRightCount < rightBeamCount;
            int effLeftDir = lastDir != 0 ? lastDir : thisDir;
            int startPoint = PositionMaxCommon(lastRight, curLeft, effLeftDir, thisDir, specialShift);
            if (specialShift)
            {
                int slice0AtDir = thisDir > 0 ? fsMax[0] : fsMin[0];
                specialShift = thisDir * (slice0AtDir - startPoint) > 0;
            }

            int newMin = int.MaxValue, newMax = int.MinValue;
            for (int dd = 0; dd < 2; dd++)  // 0 = LEFT, 1 = RIGHT
            {
                var list = dd == 0 ? curLeft : curRight;
                newMin = int.MaxValue; newMax = int.MinValue;  // set_empty per side
                for (int idx = 0; idx < list.Count; idx++)
                {
                    int s = list[idx];  // original beam index (list not yet mutated at idx)
                    int newPos = specialShift
                        ? ((-thisDir > 0 ? fsMax[1] : fsMin[1]) + thisDir * s)
                        : (startPoint - thisDir * s);
                    if (newPos < newMin) newMin = newPos;
                    if (newPos > newMax) newMax = newPos;
                    list[idx] = newPos;
                }
            }

            if (newMax >= newMin && thisDir != 0 && thisDir != lastDir)
            {
                fsMin[0] = fsMin[1]; fsMax[0] = fsMax[1];
                fsMin[1] = newMin; fsMax[1] = newMax;
            }

            if (curRight.Count > 0)
            {
                lastRight = new List<int>(curRight);
                lastDir = thisDir;
                lastRightCount = rightBeamCount;
            }
        }

        return ranks;
    }

    /// <summary>
    /// The list <see cref="CalcBeamSegments(IReadOnlyList{StemBeaming}, StemRanks[], double, double, double, List{Segment})"/>
    /// fills for its two product callers, lent from one list the thread keeps between beams.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 28.81 segment lists a keystroke at 1.39 segments each (max 4), and all 53,243
    /// built were unreachable by the time the render that built them returned. The lists and
    /// their first growth were 4,379 B a keystroke, 0.12% of it. Session 460 split the 28.81
    /// by caller: 19.12 are <see cref="BeamScoringProblem"/>'s and 9.69 the renderer's
    /// (<c>SharedRenderer.DrawBeams</c>), and BOTH read the list to the end inside the one
    /// method that asked for it — the quanter's field looks like it outlives its constructor,
    /// but only the constructor's collision booking ever reads it.
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a list given back dirty would open the next beam's segments with this
    /// beam's, which the renderer draws and the quanter books collisions against. A
    /// <see cref="Segment"/> holds no reference, so the drawer pins nothing but its capacity.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static List<Segment>? t_segments;

    /// <summary>Takes the thread's segment list, or makes the thread's first.</summary>
    internal static List<Segment> RentSegments()
    {
        var segs = t_segments;
        if (segs is null)
            return new List<Segment>();
        t_segments = null;
        return segs;
    }

    /// <summary>Puts a finished beam's segment list back, emptied, with its capacity.</summary>
    internal static void GiveSegments(List<Segment> segs)
    {
        segs.Clear();
        t_segments = segs;
    }

    /// <summary>
    /// Builds drawable beam segments from per-stem ranks, mirroring
    /// LILYPOND-REF: lily/beam.cc:457 Beam::calc_beam_segments — buckets stem beams by
    /// rank and merges neighbours that share the rank into one span; a rank with no
    /// same-rank neighbour on a side becomes a beamlet stub. The horizontal extents
    /// use the same beamlet-default-length (1.1) and 0.75 max-proportion cap
    /// (beam.cc:604-624), and EVERY segment end that stops at its stem overhangs it by
    /// half the stem width — interior ends included, not just the beam's terminals:
    /// LILYPOND-REF: lily/beam.cc:627-631 — the "closest to its stem" edge gets
    /// <c>horizontal_[event_dir] += event_dir * seg.width_ / 2</c> unconditionally, so a
    /// run that ends mid-beam (a rank drop) and a beamlet's stem-side edge both cover
    /// their stem flush. (Only the far END of a beamlet — the tip — carries no overhang.)
    /// Measured before the port: every interior end sat exactly 0.065 short of LilyPond's
    /// ink (LP regression beam-multiplicity-over-rests.ly, per-bar comparison).
    /// </summary>
    public static List<Segment> CalcBeamSegments(
        IReadOnlyList<StemBeaming> stems, StemRanks[] ranks,
        double beamletLength, double maxProportion, double halfStemWidth)
    {
        var segs = new List<Segment>();
        CalcBeamSegments(stems, ranks, beamletLength, maxProportion, halfStemWidth, segs);
        return segs;
    }

    /// <summary>
    /// <see cref="CalcBeamSegments(IReadOnlyList{StemBeaming}, StemRanks[], double, double, double)"/>
    /// into a caller's list, which it APPENDS to — the form both product callers use, each
    /// with a list lent by <see cref="RentSegments"/>.
    /// </summary>
    public static void CalcBeamSegments(
        IReadOnlyList<StemBeaming> stems, StemRanks[] ranks,
        double beamletLength, double maxProportion, double halfStemWidth, List<Segment> segs)
    {
        int n = stems.Count;

        // Gather every distinct rank present. 28.81 of these a keystroke over the reader's
        // corpus, and 63.2% of them find exactly ONE rank — a beam whose stems all carry the
        // same beam count, which is most beams. So the first rank lives in a local and the
        // set is built on the second DISTINCT one, seeded with the first so the ascending
        // walk below is the same walk (session 451; the census prices the container, not the
        // red-black node each Add allocates on top of it).
        int firstRank = 0;
        bool haveRank = false;
        SortedSet<int>? allRanks = null;
        for (int i = 0; i < n; i++)
        {
            var r = ranks[i];
            for (int k = 0, m = r.AllCount; k < m; k++)
            {
                int rv = r.AllAt(k);
                if (!haveRank)
                {
                    firstRank = rv;
                    haveRank = true;
                }
                else if (rv != firstRank)
                {
                    (allRanks ??= new SortedSet<int> { firstRank }).Add(rv);
                }
            }
        }

        if (allRanks is null)
        {
            if (haveRank)
                EmitRank(firstRank);
        }
        else
        {
            foreach (int rank in allRanks)
                EmitRank(rank);
        }

        return;

        void EmitRank(int rank)
        {
            // "hasRight[i]" = stem i carries this rank on its right (connects to i+1);
            // "hasLeft[i]" = on its left (connects to i-1).
            int i = 0;
            while (i < n)
            {
                bool here = Contains(ranks[i].Left, rank) || Contains(ranks[i].Right, rank);
                if (!here) { i++; continue; }

                // Extend a run while consecutive stems connect at this rank:
                // stem j's right AND stem j+1's left both carry it.
                int e = i;
                while (e + 1 < n
                       && Contains(ranks[e].Right, rank)
                       && Contains(ranks[e + 1].Left, rank))
                    e++;

                double xL = stems[i].X;
                double xR = stems[e].X;

                // The beamlet reach at stem `at`, measured toward `nb`.
                // LILYPOND-REF: lily/beam.cc:602-624 calc_beam_segments —
                //   length = beamlet-default-length, capped at
                //   |neighbour stem x − this stem x| × beamlet-max-length-proportion
                //   when the stem is an inner one.
                double Beamlet(int at, int nb) =>
                    nb >= 0 && nb < n
                        ? Math.Min(beamletLength,
                                   Math.Abs(stems[nb].X - stems[at].X) * maxProportion)
                        : beamletLength;

                if (e > i)
                {
                    // A span from stem i to stem e. Each END is asked LilyPond's question
                    // separately: is this the edge FURTHEST from its stem (a free end), or
                    // the edge closest to it (a continuation)?
                    // LILYPOND-REF: lily/beam.cc:589-631 calc_beam_segments — `seg.dir_ ==
                    //   event_dir` takes the beamlet arm (:602-624), otherwise the edge only
                    //   overhangs its stem by width/2 (:627-631).
                    // ⚠️ THIS USED TO GIVE BOTH ENDS THE HALF-STEM OVERHANG, on the reading
                    //   that a beamlet is what a LONE rank makes. It is not: a RUN whose end
                    //   stem carries the rank on its outward side while the neighbour there
                    //   does not is a beamlet that MERGES with the run — LilyPond builds one
                    //   segment per stem-side and merges by rank, so the two are the same
                    //   drawing. Measured on audit/lp-regression's autobeam-tuplet-recheck,
                    //   beam group 2 (c8 c16 c16 c16): LilyPond's 16th beam runs
                    //   24.954..31.829, i.e. 1.100 PAST the second stem at 26.054; Lily#
                    //   started it at the stem and the forward stub was missing.
                    bool leftFree = Contains(ranks[i].Left, rank)
                                    && (i == 0 || !Contains(ranks[i - 1].Right, rank));
                    bool rightFree = Contains(ranks[e].Right, rank)
                                     && (e == n - 1 || !Contains(ranks[e + 1].Left, rank));
                    xL = leftFree ? xL - Beamlet(i, i - 1) : xL - halfStemWidth;
                    xR = rightFree ? xR + Beamlet(e, e + 1) : xR + halfStemWidth;
                }
                else
                {
                    // A lone rank at stem i — a beamlet stub. It points toward the
                    // side that carries it (right if it has a right beam, else left);
                    // the stem-side edge overhangs its stem, the tip does not.
                    bool right = Contains(ranks[i].Right, rank);
                    double len = beamletLength;
                    int nb = right ? i + 1 : i - 1;
                    if (nb >= 0 && nb < n)
                        len = Math.Min(len, Math.Abs(stems[nb].X - stems[i].X) * maxProportion);
                    if (right)
                    {
                        xL = stems[i].X - halfStemWidth;
                        xR = stems[i].X + len;
                    }
                    else
                    {
                        xL = stems[i].X - len;
                        xR = stems[i].X + halfStemWidth;
                    }
                }

                segs.Add(new Segment(rank, xL, xR));
                i = e + 1;
            }
        }
    }
}
