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

using System.Collections;
using System.Runtime.CompilerServices;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The measure → system tables of a system array whose measures run CONSECUTIVELY in array
/// order: every system holds at least one measure, and each <c>MeasureIndex</c> is exactly one
/// more than the one before it. Such an array already IS the table — the answer for a measure is
/// found by searching the systems' first indices — so this stores no entry per measure and
/// answers both of the shared tables' questions: the system index
/// (<see cref="SpannerBreakSubstitution.BuildMeasureToSystemMap"/>) and the (system, measure)
/// pair (<see cref="LayoutUtilities.BuildMeasureMap"/>).
/// </summary>
/// <remarks>
/// MEASURED (session 471, the owner's 231 books × 8 forward keystrokes): the two tables were
/// built 7.60 and 4.29 times a keystroke — 16,371 B a keystroke — over 7.60 distinct system
/// arrays a keystroke (every array that got the int table also got the pair table; each staff
/// solve and each re-stamp hands its own array). ALL 22,000 builds were over a consecutive
/// array, which is what a layout that breaks lines at barlines makes.
/// <para>
/// ⚠️ ENUMERATION IS ASCENDING, WHICH IS THE OLD <c>Dictionary</c>'S INSERTION ORDER EXACTLY —
/// that is what "consecutive in array order" means. An array that is not consecutive (a
/// repeated <c>MeasureIndex</c>, an empty system) gets <see langword="null"/> here and the
/// callers keep their <c>Dictionary</c> build, which keeps the LAST entry for a repeated index.
/// </para>
/// </remarks>
internal sealed class ConsecutiveMeasureMap :
    IReadOnlyDictionary<int, int>,
    IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>
{
    private readonly SystemLayout[] _systems;
    private readonly int _first;
    private readonly int _count;

    private ConsecutiveMeasureMap(SystemLayout[] systems, int first, int count)
    {
        _systems = systems;
        _first = first;
        _count = count;
    }

    /// <summary>One per system array, keyed on the ARRAY ITSELF (the argument the two tables'
    /// remarks make); <see langword="null"/> is remembered too, for an array that is not
    /// consecutive.</summary>
    private static readonly ConditionalWeakTable<SystemLayout[], ConsecutiveMeasureMap?> Maps = new();

    /// <summary>The map of <paramref name="systems"/>, or <see langword="null"/> when its
    /// measures do not run consecutively.</summary>
    public static ConsecutiveMeasureMap? Of(SystemLayout[] systems)
        => Maps.GetValue(systems, TryCreate);

    private static ConsecutiveMeasureMap? TryCreate(SystemLayout[] systems)
    {
        if (systems.Length == 0)
            return null;
        int first = 0, next = 0;
        bool started = false;
        foreach (var system in systems)
        {
            var measures = system.Measures;
            if (measures.Length == 0)
                return null;
            foreach (var ml in measures)
            {
                if (!started)
                {
                    first = next = ml.MeasureIndex;
                    started = true;
                }
                if (ml.MeasureIndex != next)
                    return null;
                next++;
            }
        }
        return new ConsecutiveMeasureMap(systems, first, next - first);
    }

    private bool Holds(int measureIndex) => (uint)(measureIndex - _first) < (uint)_count;

    /// <summary>The last system whose first measure is at or before
    /// <paramref name="measureIndex"/>. Pre: <see cref="Holds"/>.</summary>
    private int SystemOf(int measureIndex)
    {
        int lo = 0, hi = _systems.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_systems[mid].Measures[0].MeasureIndex <= measureIndex)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    private (SystemLayout System, MeasureLayout Measure) PairOf(int measureIndex)
    {
        var system = _systems[SystemOf(measureIndex)];
        var measures = system.Measures;
        return (system, measures[measureIndex - measures[0].MeasureIndex]);
    }

    public int Count => _count;

    public bool ContainsKey(int key) => Holds(key);

    private IEnumerable<int> Keys => Enumerable.Range(_first, _count);

    // ── the system index ──

    int IReadOnlyDictionary<int, int>.this[int key]
        => Holds(key) ? SystemOf(key) : throw new KeyNotFoundException();

    bool IReadOnlyDictionary<int, int>.TryGetValue(int key, out int value)
    {
        if (!Holds(key))
        {
            value = 0;
            return false;
        }
        value = SystemOf(key);
        return true;
    }

    IEnumerable<int> IReadOnlyDictionary<int, int>.Keys => Keys;

    IEnumerable<int> IReadOnlyDictionary<int, int>.Values
    {
        get
        {
            for (int s = 0; s < _systems.Length; s++)
                for (int k = 0; k < _systems[s].Measures.Length; k++)
                    yield return s;
        }
    }

    IEnumerator<KeyValuePair<int, int>> IEnumerable<KeyValuePair<int, int>>.GetEnumerator()
    {
        for (int s = 0; s < _systems.Length; s++)
            foreach (var ml in _systems[s].Measures)
                yield return new KeyValuePair<int, int>(ml.MeasureIndex, s);
    }

    // ── the (system, measure) pair ──

    (SystemLayout System, MeasureLayout Measure)
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>.this[int key]
        => Holds(key) ? PairOf(key) : throw new KeyNotFoundException();

    bool IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>.TryGetValue(
        int key, out (SystemLayout System, MeasureLayout Measure) value)
    {
        if (!Holds(key))
        {
            value = default;
            return false;
        }
        value = PairOf(key);
        return true;
    }

    IEnumerable<int> IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>.Keys
        => Keys;

    IEnumerable<(SystemLayout System, MeasureLayout Measure)>
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)>.Values
    {
        get
        {
            foreach (var system in _systems)
                foreach (var ml in system.Measures)
                    yield return (system, ml);
        }
    }

    IEnumerator<KeyValuePair<int, (SystemLayout System, MeasureLayout Measure)>>
        IEnumerable<KeyValuePair<int, (SystemLayout System, MeasureLayout Measure)>>.GetEnumerator()
    {
        foreach (var system in _systems)
            foreach (var ml in system.Measures)
                yield return new KeyValuePair<int, (SystemLayout, MeasureLayout)>(
                    ml.MeasureIndex, (system, ml));
    }

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable<KeyValuePair<int, int>>)this).GetEnumerator();
}
