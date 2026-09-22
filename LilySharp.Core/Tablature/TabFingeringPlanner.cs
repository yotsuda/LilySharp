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
/// One sounding event of a tab voice, as the fingering planner sees it.
/// </summary>
/// <param name="Midi">The sounding pitch (a chord's lowest note).</param>
/// <param name="FixedString">The string the event must be played on (written <c>\N</c>, a tie,
/// or a chord's assignment); 0 when the planner chooses.</param>
/// <param name="TimeToMove">How long the hand has to get here from the previous event, in whole
/// notes — the previous event's onset to this one's.</param>
/// <param name="HandFree">The hand is free before this event: a rest, a fall, or a leap of an
/// octave or more lies between it and the previous event.</param>
/// <param name="SlurFromPrevious">A slur joins this event to the previous one.</param>
/// <param name="ChordLow">For a chord, its lowest stopped fret (0 when none).</param>
/// <param name="ChordHigh">For a chord, its highest stopped fret (0 when none).</param>
/// <param name="TiedFromPrevious">This event is the held end of a tie from the previous one:
/// it stays on that string.</param>
public readonly record struct TabEvent(
    int Midi, int FixedString, double TimeToMove, bool HandFree, bool SlurFromPrevious,
    int ChordLow = 0, int ChordHigh = 0, bool TiedFromPrevious = false);

/// <summary>
/// The costs the fingering planner weighs.
/// </summary>
/// <remarks>
/// Chosen by search (2026-09-14) against two yardsticks at once: the passages the user fingered
/// in real books, with the "the hand is free" rules of the chooser this replaced
/// (<c>TabFingeringExpectationTests</c>, <c>TabStringNumberTests</c>); and every tab fixture in
/// the repository, where a bar with no written string that fits in the first position is to be
/// played there.
/// <para>
/// ⚠️ The first yardstick alone is a trap: the weights it picked played whole first-position
/// pieces at the fifth fret and a guitar fixture at the 19th–22nd frets, because nothing in 21
/// passages says "come back down". <see cref="HeightPerFret"/> is the weight that answers the
/// second yardstick. Whoever moves a weight should measure both, and read the fret digits of
/// the tab snapshots before and after.
/// </para>
/// </remarks>
public sealed record TabFingeringWeights
{
    /// <summary>Per fret the index finger travels between two events a quarter note apart.</summary>
    public double Shift { get; init; } = 1.0;

    /// <summary>What the first fret of a shift is worth, as a fraction of the others: the hand
    /// creeps one fret more easily than it jumps.</summary>
    public double FirstFret { get; init; } = 0.75;

    /// <summary>The most frets a shift is charged for: beyond a few frets the hand jumps, and a
    /// longer jump is not proportionally harder. Without it a note written far up the neck
    /// pulled the whole passage before it toward itself.</summary>
    public double MaxShiftFrets { get; init; } = 6.0;

    /// <summary>A shortest time to move below which a shift costs no more (a 32nd-note run is not
    /// judged harder than a 16th-note one).</summary>
    public double FastestTime { get; init; } = 1.0 / 16;

    /// <summary>A longest time to move above which a shift costs no less.</summary>
    public double SlowestTime { get; init; } = 1.0 / 2;

    /// <summary>What a shift is worth when nothing holds the hand where it was — a rest, a fall
    /// or a leap before the event, or an open string just played — as a fraction of an ordinary
    /// one.</summary>
    public double FreeShiftFactor { get; init; } = 0.35;

    /// <summary>A little finger stretched one fret past the comfortable hand.</summary>
    public double Stretch { get; init; } = 0.5;

    /// <summary>Two events in a row that skip a string (the octave shape excepted).</summary>
    public double Skip { get; init; } = 1.0;

    /// <summary>Per event played with the index finger above <see cref="Tunings.LowPositionTop"/>.</summary>
    public double High { get; init; } = 1.5;

    /// <summary>Per event, per fret the index finger sits above the first: the pull back down
    /// the neck whenever staying up buys nothing (USER SPECIFIED, 2026-09-14: "if the music
    /// does not go up, go back as low as possible").</summary>
    public double HeightPerFret { get; init; } = 0.1;

    /// <summary>A slur that ends on another string than it started on.</summary>
    public double SlurAcross { get; init; } = 5.0;

    /// <summary>An open string, per fret the index finger sits above the first: an open note
    /// belongs to the first position more than to the fifth.</summary>
    public double OpenPerHandFret { get; init; } = 0.05;

    /// <summary>An open string while the hand is above low position.</summary>
    public double OpenAboveLowPosition { get; init; } = 100.0;

    /// <summary>A tie-break toward lower frets, per fret.</summary>
    public double LowerFret { get; init; } = 0.001;

    /// <summary>The weights the resolver uses.</summary>
    public static TabFingeringWeights Default { get; } = new();
}

/// <summary>
/// Chooses a string for every event of a tab voice at once: the fingering whose total cost —
/// hand shifts weighed by how little time they have, stretches, string skips, playing up the
/// neck, open strings out of reach, slurs across strings — is least over the whole voice.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, USER APPROVED (2026-09-14). A dynamic programme (Viterbi) over states
/// <c>(string, fret, hand position)</c>, where the hand position is the fret under the index
/// finger: a stopped fret f is held with the index at f − (handSpan − 1) … f, or with the little
/// finger stretched to f from f − handSpan; an open string leaves the hand where it was, unless
/// the hand is free before it, when it may drop back to the first position. This is the
/// standard formulation of automatic fretboard fingering (the hand as hidden state, the cost of
/// moving it on the transitions) and replaces a note-by-note chooser whose one note of
/// lookahead could not see a shift a phrase needs later, nor tell a run of sixteenths from a
/// held note.
/// <para>
/// ⚠️ The choice is a SUGGESTION that a better planner may change; what a player must be able
/// to rely on is a written <c>\N</c>, which the planner never overrides.
/// </para>
/// <para>
/// PERFORMANCE: it runs on every compile, once per tab staff, and its work is events × states²
/// (some 20–40 states an event), so the inner loop allocates nothing and repeats nothing an
/// event decides once — the states of every event live in one flat run, and the shift costs of
/// an event are a table by frets travelled. The order states are made in and compared in, and
/// the order the costs are added in, are what decide ties: keep both when touching the loop.
/// </para>
/// </remarks>
public static class TabFingeringPlanner
{
    private readonly record struct State(int String, int Fret, int Hand);

    /// <summary>The chosen string for each event (1 = highest), in order.</summary>
    public static int[] Plan(IReadOnlyList<TabEvent> events, int[] tuning, int handSpan,
        TabFingeringWeights? weights = null)
    {
        int n = events.Count;
        var result = new int[n];
        if (n == 0) return result;
        // The thread's trellis, taken out of its drawer and put back only after a solve that
        // finished (see t_trellis): a throw in between costs the next plan a new one.
        var trellis = t_trellis ?? new Trellis();
        t_trellis = null;
        trellis.Reset(n, weights ?? TabFingeringWeights.Default, handSpan);
        trellis.Solve(events, tuning, result);
        t_trellis = trellis;
        return result;
    }

    /// <summary>The trellis <see cref="Plan"/> solves in, kept by the thread between plans.</summary>
    /// <remarks>
    /// Its three state arrays are sized sixteen states an event and were new on every plan:
    /// <c>State[]</c> 63,836 B a keystroke by the runtime's allocation ticks over the reader's
    /// corpus (session 493), and the cost and back-pointer arrays beside it 74,873 B between
    /// them by the array census (session 495, warm-ups included). Reuse is sound because
    /// nothing is read that this plan has not written: the states, costs and back-pointers
    /// only below <c>_count</c> (reset to 0), the event starts only below the event count,
    /// the shift tables whole per event (PrepareShifts), and <c>_seenHand</c> is cleared
    /// entry by entry after every use. WHAT IT RETAINS is the thread's largest plan at about
    /// 24 B a state.
    /// </remarks>
    [System.ThreadStatic]
    private static Trellis? t_trellis;

    /// <summary>Every event's states in one flat run: event i's are [start[i], start[i + 1]).</summary>
    private sealed class Trellis
    {
        private const int ShiftTableSize = 32;

        private TabFingeringWeights _w = TabFingeringWeights.Default;
        private int _handSpan;
        private int[] _start = System.Array.Empty<int>();
        private State[] _states = System.Array.Empty<State>();
        private double[] _cost = System.Array.Empty<double>();
        private int[] _back = System.Array.Empty<int>(); // a state's cheapest predecessor, as an index into the run; -1 for none
        private int _count;

        // The current event's shift cost by frets travelled, with the hand held and free.
        private readonly double[] _shift = new double[ShiftTableSize];
        private readonly double[] _freeShift = new double[ShiftTableSize];
        private double _timeScale;

        // The hand positions the previous event can leave, in the order they first appear there.
        private readonly List<int> _openHands = new();
        private bool[] _seenHand = new bool[ShiftTableSize];

        /// <summary>Readies the trellis for a plan of <paramref name="events"/> events: at least
        /// sixteen states an event of room (what a new one was given), and nothing counted.</summary>
        public void Reset(int events, TabFingeringWeights w, int handSpan)
        {
            _w = w;
            _handSpan = handSpan;
            _count = 0;
            if (_start.Length < events)
                _start = new int[events];
            int capacity = events * 16;
            if (_states.Length < capacity)
            {
                _states = new State[capacity];
                _cost = new double[capacity];
                _back = new int[capacity];
            }
        }

        public void Solve(IReadOnlyList<TabEvent> events, int[] tuning, int[] result)
        {
            int n = events.Count;
            int stringCount = tuning.Length;
            for (int i = 0; i < n; i++)
            {
                var ev = events[i];
                _start[i] = _count;
                PrepareShifts(ev);
                bool openHandsReady = false;

                bool pinned = ev.FixedString >= 1 && ev.FixedString <= stringCount;
                bool any = false;
                for (int idx = stringCount - 1; idx >= 0; idx--)
                {
                    int str = stringCount - idx;
                    if (pinned && str != ev.FixedString) continue;
                    int fret = ev.Midi - tuning[idx];
                    if (fret < 0 || fret > 24) continue;
                    any = true;
                    AddCandidate(i, str, fret, in ev, ref openHandsReady);
                }
                if (!any)
                {
                    // Out of range, or a fixed string that cannot play it: the calculator's fallback.
                    var (s, f) = Tunings.CalculateFret(ev.Midi, tuning, ev.FixedString);
                    AddCandidate(i, s, f, in ev, ref openHandsReady);
                }
            }

            // Walk back from the cheapest final state.
            int best = 0;
            int lastStart = _start[n - 1];
            for (int k = 1; k < _count - lastStart; k++)
                if (_cost[lastStart + k] < _cost[lastStart + best]) best = k;
            for (int i = n - 1; i >= 0; i--)
            {
                int start = _start[i];
                int end = i + 1 < n ? _start[i + 1] : _count;
                if (end == start) { result[i] = 0; continue; }
                int at = start + best;
                result[i] = _states[at].String;
                int back = _back[at];
                if (back < 0) break;
                best = back - _start[i - 1];
            }
        }

        private void AddCandidate(int i, int str, int fret, in TabEvent ev, ref bool openHandsReady)
        {
            if (fret > 0)
            {
                int low = ev.ChordLow > 0 ? System.Math.Min(ev.ChordLow, fret) : fret;
                int high = ev.ChordHigh > 0 ? System.Math.Max(ev.ChordHigh, fret) : fret;
                for (int hand = System.Math.Max(1, high - _handSpan); hand <= low; hand++)
                    AddState(i, new State(str, fret, hand), in ev);
            }
            else if (i == 0)
            {
                AddState(i, new State(str, 0, 1), in ev);
            }
            else
            {
                // An open string leaves the hand where it was: one state per hand position the
                // previous event could have left — and, when the hand is free before it, the
                // first position it may drop back to while the string rings.
                if (!openHandsReady)
                {
                    CollectOpenHands(i, ev.HandFree);
                    openHandsReady = true;
                }
                foreach (int hand in _openHands)
                    AddState(i, new State(str, 0, hand), in ev);
            }
        }

        private void CollectOpenHands(int i, bool handFree)
        {
            _openHands.Clear();
            for (int j = _start[i - 1]; j < _start[i]; j++)
                SeeHand(_states[j].Hand);
            if (handFree)
                SeeHand(1);
            foreach (int hand in _openHands)
                _seenHand[hand] = false;

            void SeeHand(int hand)
            {
                if (hand >= _seenHand.Length)
                    System.Array.Resize(ref _seenHand, hand * 2);
                if (_seenHand[hand]) return;
                _seenHand[hand] = true;
                _openHands.Add(hand);
            }
        }

        private void PrepareShifts(in TabEvent ev)
        {
            double time = System.Math.Clamp(ev.TimeToMove, _w.FastestTime, _w.SlowestTime);
            _timeScale = 0.25 / time;
            for (int travel = 1; travel < ShiftTableSize; travel++)
            {
                double shift = ShiftCost(travel);
                _shift[travel] = shift;
                _freeShift[travel] = shift * _w.FreeShiftFactor;
            }
        }

        private double ShiftCost(int travel)
        {
            double frets = System.Math.Min(travel - 1 + _w.FirstFret, _w.MaxShiftFrets);
            return _w.Shift * frets * _timeScale;
        }

        private void AddState(int i, State s, in TabEvent ev)
        {
            double own = NodeCost(s, ev.TiedFromPrevious);
            int prevStart = i == 0 ? 0 : _start[i - 1];
            int prevEnd = _start[i];
            if (i == 0 || prevStart == prevEnd)
            {
                Append(s, own, -1);
                return;
            }

            bool free = ev.HandFree;
            bool slur = ev.SlurFromPrevious;
            bool tied = ev.TiedFromPrevious;
            double bestCost = double.MaxValue;
            int bestPrev = -1;
            for (int j = prevStart; j < prevEnd; j++)
            {
                var p = _states[j];
                // An open string keeps the hand, unless the hand is free before it.
                if (s.Fret == 0 && s.Hand != p.Hand && !free) continue;

                double cost = 0;
                int travel = System.Math.Abs(s.Hand - p.Hand);
                if (travel > 0)
                {
                    bool unheld = free || p.Fret == 0;
                    if (travel < ShiftTableSize)
                        cost += unheld ? _freeShift[travel] : _shift[travel];
                    else
                        cost += unheld ? ShiftCost(travel) * _w.FreeShiftFactor : ShiftCost(travel);
                }

                int apart = System.Math.Abs(p.String - s.String);
                if (apart > 1
                    && !(apart == 2 && p.Fret > 0 && s.Fret > 0
                         && (s.String < p.String ? s.Fret - p.Fret : p.Fret - s.Fret) == 2))
                    cost += _w.Skip;

                if (slur && s.String != p.String)
                    cost += _w.SlurAcross;

                // A tie holds one string.
                if (tied && s.String != p.String)
                    cost += 1_000_000;

                double c = _cost[j] + cost;
                if (c < bestCost) { bestCost = c; bestPrev = j; }
            }
            if (bestPrev < 0) return;
            Append(s, bestCost + own, bestPrev);
        }

        private double NodeCost(State s, bool tied)
        {
            // The held end of a tie is not played again: whatever it costs was paid when it was.
            if (tied)
                return 0;

            double cost = _w.LowerFret * s.Fret + _w.HeightPerFret * (s.Hand - 1);
            if (s.Fret > 0 && s.Fret - s.Hand == _handSpan)
                cost += _w.Stretch;
            if (s.Fret > 0 && s.Hand > Tunings.LowPositionTop)
                cost += _w.High;
            if (s.Fret == 0)
            {
                cost += _w.OpenPerHandFret * (s.Hand - 1);
                if (s.Hand > Tunings.LowPositionTop)
                    cost += _w.OpenAboveLowPosition;
            }
            return cost;
        }

        private void Append(State s, double cost, int back)
        {
            if (_count == _states.Length)
            {
                int grown = _states.Length * 2;
                System.Array.Resize(ref _states, grown);
                System.Array.Resize(ref _cost, grown);
                System.Array.Resize(ref _back, grown);
            }
            _states[_count] = s;
            _cost[_count] = cost;
            _back[_count] = back;
            _count++;
        }
    }
}
