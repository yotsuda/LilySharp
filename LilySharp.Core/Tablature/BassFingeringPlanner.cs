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

namespace LilySharp.Core.Tablature;

/// <summary>
/// The costs the bass fingering planner weighs, fitted to the owner's fingering of 267 bass
/// books (see <see cref="BassFingeringPlanner"/>).
/// </summary>
public sealed record BassFingeringWeights
{
    /// <summary>What any move of the hand costs, however short.</summary>
    public double ShiftBase { get; init; }
    /// <summary>What each fret of a move costs, up to <see cref="ShiftCap"/> frets.</summary>
    public double ShiftPerFret { get; init; }
    /// <summary>The most frets a move is charged for.</summary>
    public double ShiftCap { get; init; }
    /// <summary>What a move costs, as a fraction, when the hand is free before the note (a
    /// rest, or a leap of an octave or more).</summary>
    public double FreeShiftFactor { get; init; }
    /// <summary>How strongly a move's cost grows as the time to make it shrinks:
    /// (quarter note ÷ time)^this.</summary>
    public double TimeExponent { get; init; }
    /// <summary>What a move costs, as a fraction, right after an open string.</summary>
    public double AfterOpenFactor { get; init; }
    /// <summary>Two notes in a row that skip a string.</summary>
    public double Skip { get; init; }
    /// <summary>Each further string skipped.</summary>
    public double SkipPerString { get; init; }
    /// <summary>The little finger stretched one fret past the hand.</summary>
    public double Stretch { get; init; }
    /// <summary>A slur that ends on another string.</summary>
    public double SlurAcross { get; init; }
    /// <summary>The same pitch again on another string.</summary>
    public double RepeatAcross { get; init; }
    /// <summary>An open string.</summary>
    public double Open { get; init; }
    /// <summary>An open string, per fret the hand sits above the first.</summary>
    public double OpenPerHandFret { get; init; }
    /// <summary>An open string, per string, the highest first (index 0 = the 1st string), on top
    /// of <see cref="Open"/>.</summary>
    public IReadOnlyList<double> OpenStringCost { get; init; } = [];
    /// <summary>A stopped note on each string, the highest first (index 0 = the 1st string; the
    /// fifth entry also serves any lower string).</summary>
    public IReadOnlyList<double> StringCost { get; init; } = [];
    /// <summary>The hand at each position, the first first (index 0 = the 1st fret; the last
    /// entry also serves every position above it).</summary>
    public IReadOnlyList<double> PositionCost { get; init; } = [];
    /// <summary>An open string followed by an open LOWER string: the higher one rings on, and
    /// with no finger down the left hand has to stop to mute it. Not charged when the lower note
    /// is stopped — the finger stopping it lies across the higher string and mutes it — nor the
    /// other way round, where the right hand's finger comes to rest on the lower string (USER
    /// SPECIFIED, 2026-10-25: "D A: the 3rd string's 5th then the open 3rd, not the open 2nd then
    /// the open 3rd; A D: the open 3rd then the open 2nd is fine; the open G then the 2nd
    /// string's 3rd is fine").</summary>
    public double OpenThenLowerOpen { get; init; }
    /// <summary>Two stopped notes in a row on one string at different frets.</summary>
    public double SameStringStep { get; init; }
    /// <summary>The frets the hand covers past the index finger before it stretches.</summary>
    public int Span { get; init; } = 3;
}

/// <summary>
/// Chooses a string for every note of a BASS tab voice at once: the fingering whose total cost
/// — hand moves weighed by the time they have, string skips, stretches, open strings, and where
/// on the neck the hand sits — is least over the whole voice.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, FITTED TO THE OWNER'S FINGERING (2026-10-25). The owner fingered 267 bass books
/// in LilyPond by hand (the strings written as <c>\N</c>, the rest LilyPond's highest string);
/// the costs below are the ones that make this planner play those books with the fewest
/// strings left to write. ⚠️ THE OUTPUT IS FROZEN: a user's reviewed tab must not change under
/// them between versions, so <c>BassFingeringGoldenTests</c> holds the planner's every string
/// over that corpus, and a change to a cost or to this code is a change to the language —
/// it needs the owner's approval and a line in the changelog.
/// <para>
/// MEASURED (2026-10-25, 267 books, 111,068 notes aligned to LilyPond's own strings — read back
/// note by note with LilySharp-Lab runs/2026-10-25-bass-fingering/probe.ly): the strings a user
/// would write, first miss first, before the planner plays a book as fingered — 6,948 (median 19
/// a book) against the general planner's 17,319 and LilyPond's 33,301; 85.7% of the notes as
/// fingered. The costs were searched for that count on half the books and checked on the other
/// half (3,483 / 3,493). Re-measure with the Measure-category tests TabCorpusMeasureTests and
/// TabPlannerLab (LILYSHARP_TABCORPUS, LILYSHARP_TABCORPUS_LP).
/// </para>
/// <para>
/// A dynamic programme (Viterbi) over states (string, fret, hand position), the hand position
/// being the fret under the index finger: a stopped fret f is held with the index at
/// f − <see cref="BassFingeringWeights.Span"/> … f, the last a stretch; an open string leaves the
/// hand where it was, unless the hand is free before it.
/// </para>
/// </remarks>
public static class BassFingeringPlanner
{
    private readonly record struct State(int String, int Fret, int Hand);

    /// <summary>The chosen string for each event (1 = highest), in order.</summary>
    public static int[] Plan(IReadOnlyList<TabEvent> events, int[] tuning, BassFingeringWeights? weights = null)
    {
        var w = weights ?? t_weightsForMeasurement ?? Default;
        int n = events.Count, strings = tuning.Length;
        var result = new int[n];
        if (n == 0) return result;
        int span = w.Span;

        var start = new int[n + 1];
        var states = new List<State>(n * 8);
        var cost = new List<double>(n * 8);
        var back = new List<int>(n * 8);
        var hands = new List<int>();
        var seenHand = new HashSet<int>();

        for (int i = 0; i < n; i++)
        {
            var ev = events[i];
            start[i] = states.Count;
            int prevStart = i == 0 ? 0 : start[i - 1], prevEnd = start[i];
            double timeScale = w.TimeExponent == 0 ? 1
                : System.Math.Pow(0.25 / System.Math.Clamp(ev.TimeToMove, 1.0 / 32, 2), w.TimeExponent);

            bool any = false;
            for (int s = 1; s <= strings; s++)
            {
                if (ev.FixedString >= 1 && ev.FixedString <= strings && s != ev.FixedString) continue;
                int f = ev.Midi - tuning[strings - s];
                if (f < 0 || f > 24) continue;
                any = true;
                if (f > 0)
                {
                    for (int p = System.Math.Max(1, f - span); p <= f; p++)
                        Add(new State(s, f, p));
                }
                else if (i == 0)
                {
                    Add(new State(s, 0, 1));
                }
                else
                {
                    // An open string leaves the hand where the previous note left it — or, when
                    // the hand is free, lets it drop back to the first position.
                    hands.Clear(); seenHand.Clear();
                    for (int j = prevStart; j < prevEnd; j++)
                        if (seenHand.Add(states[j].Hand)) hands.Add(states[j].Hand);
                    if (ev.HandFree && seenHand.Add(1)) hands.Add(1);
                    foreach (int p in hands) Add(new State(s, 0, p));
                }
            }
            if (!any)
            {
                var (s0, f0) = Tunings.CalculateFret(ev.Midi, tuning, ev.FixedString);
                Add(new State(s0, f0, System.Math.Max(1, f0)));
            }

            void Add(State x)
            {
                double node = x.Fret > 0
                    ? Pick(w.StringCost, x.String) + (x.Fret - x.Hand == span ? w.Stretch : 0)
                    : w.Open + w.OpenPerHandFret * (x.Hand - 1) + Pick(w.OpenStringCost, x.String);
                node += Pick(w.PositionCost, x.Hand);
                if (i == 0 || prevStart == prevEnd)
                {
                    states.Add(x); cost.Add(node); back.Add(-1);
                    return;
                }
                var pe = events[i - 1];
                double best = double.MaxValue; int arg = -1;
                for (int j = prevStart; j < prevEnd; j++)
                {
                    var q = states[j];
                    if (x.Fret == 0 && x.Hand != q.Hand && !ev.HandFree) continue;
                    double e = cost[j];
                    int dp = System.Math.Abs(x.Hand - q.Hand);
                    if (dp > 0)
                    {
                        double sh = (w.ShiftBase + w.ShiftPerFret * System.Math.Min(dp, w.ShiftCap)) * timeScale;
                        // After a fall the hand has already left: moving costs nothing (USER
                        // SPECIFIED, 2026-09-14 — "the hand comes all the way down").
                        if (ev.AfterFall) sh = 0;
                        else if (ev.HandFree) sh *= w.FreeShiftFactor;
                        else if (q.Fret == 0) sh *= w.AfterOpenFactor;
                        e += sh;
                    }
                    int ds = System.Math.Abs(x.String - q.String);
                    if (ds > 1) e += w.Skip + w.SkipPerString * (ds - 2);
                    if (ev.SlurFromPrevious && ds != 0) e += w.SlurAcross;
                    if (ev.TiedFromPrevious && ds != 0) e += 1_000_000;
                    if (pe.Midi == ev.Midi && ds != 0) e += w.RepeatAcross;
                    if (ds == 0 && x.Fret > 0 && q.Fret > 0 && x.Fret != q.Fret) e += w.SameStringStep;
                    if (q.Fret == 0 && x.Fret == 0 && x.String > q.String) e += w.OpenThenLowerOpen;
                    if (e < best) { best = e; arg = j; }
                }
                if (arg < 0) { best = 1e9; arg = prevStart; }
                states.Add(x); cost.Add(best + node); back.Add(arg);
            }
        }
        start[n] = states.Count;

        int k = start[n - 1];
        for (int j = start[n - 1] + 1; j < start[n]; j++) if (cost[j] < cost[k]) k = j;
        for (int i = n - 1; i >= 0 && k >= 0; i--)
        {
            result[i] = states[k].String;
            k = back[k];
        }
        return result;
    }

    /// <summary>Weights a measurement runs the whole pipeline with on this thread (tests only).</summary>
    [System.ThreadStatic]
    internal static BassFingeringWeights? t_weightsForMeasurement;

    private static double Pick(IReadOnlyList<double> table, int oneBased)
        => table.Count == 0 ? 0 : table[System.Math.Min(oneBased, table.Count) - 1];

    /// <summary>The weights the resolver uses (fitted 2026-10-25; FROZEN — see the remarks above).</summary>
    public static BassFingeringWeights Default { get; } = new()
    {
        ShiftBase = 0,
        ShiftPerFret = 1.8,
        ShiftCap = 5.5,
        FreeShiftFactor = 0.88,
        TimeExponent = 0.5,
        AfterOpenFactor = 0.45,
        Skip = 2.8,
        SkipPerString = 4.5,
        Stretch = 0.5,
        SlurAcross = 16,
        RepeatAcross = 23,
        Open = 0.1,
        OpenPerHandFret = 0.3,
        // The open A and E are the open strings the owner's books reach for (60% of their A's,
        // 96% of their E's; the D and G are mostly stopped). The A's -0.3 is not the fit's (0,
        // 11 fewer strings to write over the corpus) but chosen so a lone A is the open string,
        // as most of the owner's are and as LilyPond plays it.
        OpenStringCost = [0, 0, -0.3, -0.3, 0],
        // 1st (G), 2nd (D), 3rd (A), 4th (E), 5th (low B): the middle strings are the home ones.
        StringCost = [0.1, -0.6, -0.5, 0.11, 0.5],
        // The hand at the 1st … 12th fret: the 2nd–6th positions are home; above the 7th, dearer.
        PositionCost = [-0.05, -0.3, 0.1, -0.3, -0.15, -0.1, 0.25, 0.4, 0.6, 2.4, 0.9, 3.8],
        SameStringStep = 0,
        Span = 3,
    };
}
