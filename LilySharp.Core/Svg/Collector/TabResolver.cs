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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Tablature post-pass over already-collected voices: it reconciles tie strings and
/// then assigns every tab note a concrete string for the staff's tuning. Pulled out
/// of <see cref="MeasureCollector"/> as a self-contained collaborator — it touches no
/// collection state, only the voice it's given, and accumulates its own warnings.
/// </summary>
internal sealed class TabResolver
{
    private readonly List<TabTieStringWarning> _tieWarnings = new();
    private readonly List<TabRangeWarning> _rangeWarnings = new();

    /// <summary>Tab notes pitched outside the fretboard's range.</summary>
    public IReadOnlyList<TabRangeWarning> RangeWarnings => _rangeWarnings;

    /// <summary>Ties whose two ends carry conflicting explicit string numbers.</summary>
    public IReadOnlyList<TabTieStringWarning> TieWarnings => _tieWarnings;

    /// <summary>
    /// Resolves ties for tab rendering across a single voice. The destination of a
    /// tie is flagged <see cref="NoteItem.IsTieTarget"/> (its fret number is hidden
    /// on a tab staff) and string numbers are reconciled along the tie:
    /// <list type="bullet">
    /// <item>both notes carry an explicit <c>\N</c> that disagree → a warning (a tie
    /// holds one string); the source string is kept.</item>
    /// <item>only the destination carries <c>\N</c> → the source ADOPTS it (so the
    /// struck note sits on the held string).</item>
    /// </list>
    /// Voices with no ties are returned unchanged (no rebuild), so non-tied scores —
    /// and all notation rendering — are byte-for-byte identical.
    /// </summary>
    public Voice ResolveVoiceTabTies(Voice voice)
    {
        bool anyTie = voice.Measures.Any(m => m.Items.Any(it => it is NoteItem { HasTieStart: true }));
        if (!anyTie)
            return voice;

        var items = voice.Measures.Select(m => m.Items.ToArray()).ToArray();
        int pendingMi = -1, pendingIi = -1; // the note awaiting its tie destination

        for (int mi = 0; mi < items.Length; mi++)
        {
            for (int ii = 0; ii < items[mi].Length; ii++)
            {
                if (items[mi][ii] is not NoteItem note)
                    continue;

                if (pendingMi >= 0)
                {
                    var src = (NoteItem)items[pendingMi][pendingIi];
                    int? srcStr = src.StringNumber;
                    int? dstStr = note.StringNumber;

                    if (srcStr.HasValue && dstStr.HasValue && srcStr != dstStr)
                        _tieWarnings.Add(new TabTieStringWarning(
                            note.SourcePosition, srcStr.Value, dstStr.Value));
                    else if (!srcStr.HasValue && dstStr.HasValue)
                    {
                        items[pendingMi][pendingIi] = src with { StringNumber = dstStr };
                        srcStr = dstStr;
                    }

                    // The destination keeps the held string (for chained ties) and
                    // is hidden on the tab staff.
                    note = note with { IsTieTarget = true, StringNumber = dstStr ?? srcStr };
                    items[mi][ii] = note;
                    pendingMi = -1;
                }

                pendingMi = note.HasTieStart ? mi : -1;
                pendingIi = ii;
            }
        }

        var measures = voice.Measures;
        var rebuilt = ImmutableArray.CreateBuilder<Measure>(measures.Length);
        for (int mi = 0; mi < measures.Length; mi++)
            rebuilt.Add(measures[mi] with { Items = ImmutableArray.Create(items[mi]) });
        return voice with { Measures = rebuilt.MoveToImmutable() };
    }

    /// <summary>
    /// Gives every note of a chord its own string for a tuning, so two fret numbers
    /// never collide on one line. Explicit <c>\N</c> notes are pinned; the rest are
    /// assigned highest pitch first, each taking its lowest-fret FREE string (a free
    /// playable string failing only for genuinely out-of-range pitches).
    /// </summary>
    private static ImmutableArray<ChordNoteInfo> AssignChordStrings(
        ImmutableArray<ChordNoteInfo> notes, int[] tun, int shift)
    {
        int n = tun.Length;
        var result = notes.ToArray();
        var used = new bool[n + 1]; // 1-based string numbers

        foreach (var cn in notes)
            if (cn.StringNumber is int s && s >= 1 && s <= n)
                used[s] = true;

        foreach (int i in Enumerable.Range(0, notes.Length).OrderByDescending(k => notes[k].Midi))
        {
            if (notes[i].StringNumber is int es && es >= 1 && es <= n)
                continue; // keep explicit \N
            int midi = notes[i].Midi + shift;
            int best = -1, bestFret = int.MaxValue;
            for (int str = 1; str <= n; str++)
            {
                if (used[str]) continue;
                int fret = midi - tun[n - str]; // string `str` → tuning index n-str
                if (fret < 0 || fret > 24) continue;
                if (fret < bestFret) { bestFret = fret; best = str; }
            }
            if (best == -1)
                best = Tunings.CalculateFret(midi, tun, 0).stringNum; // out of range: best effort
            else
                used[best] = true;
            result[i] = notes[i] with { StringNumber = best };
        }
        return ImmutableArray.Create(result);
    }

    /// <summary>A sounding pitch is playable when some string frets it at 0..24.</summary>
    private static bool IsTabPlaceable(int sounding, int[] tun)
    {
        foreach (var open in tun)
            if (sounding - open >= 0 && sounding - open <= 24) return true;
        return false;
    }

    /// <summary>
    /// Assigns every tab note a concrete string for a staff's tuning so the fret
    /// number, the stem and the beam all read one consistent value. Explicit <c>\N</c>
    /// (or tie-adopted) strings are kept; a pitch already seen earlier in the SAME bar
    /// reuses that string (reset at the bar line); otherwise the string whose fret is
    /// closest to the previous note's keeps the hand in position. Tuning-dependent, so
    /// it runs per tab staff after the score is assembled.
    /// </summary>
    public Voice ResolveTabStrings(Voice voice, TuningType tuning, ClefType clef = ClefType.Treble)
    {
        int[] tun = Tunings.GetTuning(tuning);
        int shift = Tunings.OctaveShift(tuning, clef);
        int lowestOpen = tun.Min();
        int? prevFret = null; // hand position, carried across bar lines

        var rebuilt = ImmutableArray.CreateBuilder<Measure>(voice.Measures.Length);
        foreach (var measure in voice.Measures)
        {
            var barString = new Dictionary<int, int>(); // written MIDI -> string, reset each bar
            var items = measure.Items.ToArray();
            bool changed = false;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is ChordItem chord)
                {
                    // Each chord note needs its OWN string, else two fret numbers
                    // land on the same line and overlap into one.
                    var newNotes = AssignChordStrings(chord.Notes, tun, shift);
                    items[i] = chord with { Notes = newNotes };
                    changed = true;
                    foreach (var cn in newNotes)
                        if (!IsTabPlaceable(cn.Midi + shift, tun))
                            _rangeWarnings.Add(new TabRangeWarning(chord.SourcePosition, cn.Midi + shift < lowestOpen));
                    if (newNotes.Length > 0)
                    {
                        var low = newNotes[0];
                        foreach (var c in newNotes) if (c.Midi < low.Midi) low = c;
                        prevFret = Tunings.CalculateFret(low.Midi + shift, tun, low.StringNumber ?? 0).fret;
                    }
                    continue;
                }
                if (items[i] is not NoteItem note) continue;
                int midi = note.Midi + shift;
                if (!IsTabPlaceable(midi, tun))
                    _rangeWarnings.Add(new TabRangeWarning(note.SourcePosition, midi < lowestOpen));
                int strNum, fret;
                if (note.StringNumber.HasValue)
                {
                    (strNum, fret) = Tunings.CalculateFret(midi, tun, note.StringNumber.Value);
                    barString[note.Midi] = strNum;
                }
                else if (barString.TryGetValue(note.Midi, out var inherited))
                {
                    (strNum, fret) = Tunings.CalculateFret(midi, tun, inherited);
                    items[i] = note with { StringNumber = strNum };
                    changed = true;
                }
                else
                {
                    (strNum, fret) = Tunings.CalculateFret(midi, tun, 0, nearFret: prevFret);
                    items[i] = note with { StringNumber = strNum };
                    barString[note.Midi] = strNum;
                    changed = true;
                }
                prevFret = fret;
            }
            rebuilt.Add(changed ? measure with { Items = ImmutableArray.Create(items) } : measure);
        }
        return voice with { Measures = rebuilt.MoveToImmutable() };
    }
}
