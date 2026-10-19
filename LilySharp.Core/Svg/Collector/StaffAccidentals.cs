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
/// The accidentals a STAFF owes beyond the ones each of its voices decided for itself: a head
/// whose alteration differs from the one last shown, earlier in the bar, for the same note in
/// ANOTHER voice of the staff — or, under an ottava line, for a head drawn on the same line or
/// space in another octave. The pass only ADDS accidentals; none the voices printed is taken
/// away.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/engraver-init.ly — the default <c>Accidental_engraver</c> is consisted in
/// the Staff context, and scm/music-functions.scm's default style is <c>(Staff same-octave 0)</c>:
/// an alteration shown in one voice holds for the other voices of the staff to the bar line.
/// The collect decides accidentals one VOICE at a time (each voice's walk has its own memory,
/// <c>MeasureCollector.ResetAccidentalMemory</c>), so until 2026-10-20 a lower voice's
/// <c>f'</c> after the upper voice's <c>fes'</c> printed no natural, and a reader saw two
/// f-flats (OMR report 2026-10-20, item 8; LilyPond 2.26.0 prints the natural, Lab
/// sessions/p915/lp/check.ly). This pass reads every voice's heads together, in time order.
/// </para>
/// <para>
/// LILYSHARP-OWN, two ways, both because a reader reads what is drawn (owner's decision
/// 2026-10-02: musical validity, not LilyPond, is the arbiter of what the notation says):
/// ⑴ the voices' own accidentals are kept where LilyPond's Staff memory would drop them (a
/// lower voice repeating the upper voice's sharp) — the reminder is correct and harmless;
/// ⑵ a head drawn on the line or space of an earlier head of another octave — the two sides
/// of an ottava line's end — gets its accidental when the alterations differ. LilyPond
/// remembers by pitch, so `\ottava #1 cisis'' \ottava #0 cis'` draws the second head on the
/// first one's space with no sharp and reads as c-double-sharp (OMR report 2026-10-20, item
/// 10; LilyPond 2.26.0 draws it so, the same book).
/// </para>
/// <para>
/// Runs under the DEFAULT accidental style only: the other styles are rule sets of their own,
/// and their contexts are not modelled here. A bar with a clef change skips the drawn-position
/// rule (a line or space then names two notes). Two heads at the same moment in different
/// voices do not see each other (LilyPond writes its memory at the end of the timestep). A
/// head a tie carries in prints nothing, as in LilyPond.
/// </para>
/// </remarks>
internal static class StaffAccidentals
{
    private readonly record struct Head(
        int Voice, int Item, int Member, Fraction Onset, int GraceRank,
        int Packed, int StaffPosition, bool HasAccidental, bool TiedIn);

    /// <summary>
    /// Returns <paramref name="voices"/> with the accidentals the staff owes added, or the
    /// same array when it owes none.
    /// </summary>
    public static ImmutableArray<Voice> Apply(ImmutableArray<Voice> voices, AccidentalStyleSpec style)
    {
        if (voices.IsDefaultOrEmpty || !ReferenceEquals(style, AccidentalStyles.Default))
            return voices;

        int measureCount = 0;
        foreach (var v in voices)
            measureCount = Math.Max(measureCount, v.Measures.Length);

        // Per measure, every head of every voice, and whether the bar changes clef.
        var heads = new List<Head>[measureCount];
        var clefChange = new bool[measureCount];
        for (int vi = 0; vi < voices.Length; vi++)
        {
            MusicItem? previous = null;
            var measures = voices[vi].Measures;
            for (int mi = 0; mi < measures.Length; mi++)
            {
                var onset = Fraction.Zero;
                var items = measures[mi].Items;
                for (int ii = 0; ii < items.Length; ii++)
                {
                    var item = items[ii];
                    switch (item)
                    {
                        case ClefChangeItem:
                            clefChange[mi] = true;
                            break;
                        case NoteItem n:
                            if (n.WrittenPitch != 0)
                                (heads[mi] ??= new()).Add(new Head(vi, ii, -1, onset, n.GraceTime ? 0 : 1,
                                    n.WrittenPitch, n.StaffPosition,
                                    n.Accidental != null || n.LineStartAccidental != null || n.EditorialAccidental != null,
                                    TiedInto(previous, n.StaffPosition, n.Midi)));
                            previous = n;
                            break;
                        case ChordItem c:
                            for (int k = 0; k < c.Notes.Length; k++)
                            {
                                var member = c.Notes[k];
                                if (member.WrittenPitch != 0)
                                    (heads[mi] ??= new()).Add(new Head(vi, ii, k, onset, c.GraceTime ? 0 : 1,
                                        member.WrittenPitch, member.StaffPosition,
                                        member.Accidental != null || member.LineStartAccidental != null,
                                        TiedInto(previous, member.StaffPosition, member.Midi)));
                            }
                            previous = c;
                            break;
                        case RestItem:
                            previous = null;
                            break;
                    }
                    onset += item.Duration;
                }
            }
        }

        Dictionary<(int Voice, int Measure, int Item, int Member), string>? added = null;
        for (int mi = 0; mi < measureCount; mi++)
        {
            var bar = heads[mi];
            if (bar == null || bar.Count < 2)
                continue;
            foreach (var head in bar)
            {
                if (head.HasAccidental || head.TiedIn)
                    continue;
                int alter = WrittenPitch.Alter(head.Packed);
                // ⑴ The same note in the same octave, last shown in ANOTHER voice.
                int? owed = Owed(bar, head, alter,
                    e => WrittenPitch.StepOctave(e.Packed) == WrittenPitch.StepOctave(head.Packed),
                    e => e.Voice != head.Voice);
                // ⑵ The same line or space, last shown for another octave (an ottava's two sides).
                if (owed is null && !clefChange[mi])
                    owed = Owed(bar, head, alter,
                        e => e.StaffPosition == head.StaffPosition
                             && WrittenPitch.Step(e.Packed) == WrittenPitch.Step(head.Packed),
                        e => WrittenPitch.Octave(e.Packed) != WrittenPitch.Octave(head.Packed));
                if (owed is { } previousAlter)
                    (added ??= new())[(head.Voice, mi, head.Item, head.Member)] =
                        Name(alter, previousAlter, style.ExtraNatural);
            }
        }
        if (added == null)
            return voices;

        var result = voices.ToBuilder();
        for (int vi = 0; vi < voices.Length; vi++)
        {
            var measures = voices[vi].Measures;
            ImmutableArray<Measure>.Builder? rebuilt = null;
            for (int mi = 0; mi < measures.Length; mi++)
            {
                var items = measures[mi].Items;
                MusicItem[]? copy = null;
                for (int ii = 0; ii < items.Length; ii++)
                {
                    switch (items[ii])
                    {
                        case NoteItem n when added.TryGetValue((vi, mi, ii, -1), out var name):
                            (copy ??= items.ToArray())[ii] = n with { Accidental = name };
                            break;
                        case ChordItem c:
                            var notes = c.Notes;
                            for (int k = 0; k < notes.Length; k++)
                                if (added.TryGetValue((vi, mi, ii, k), out var memberName))
                                    notes = notes.SetItem(k, notes[k] with { Accidental = memberName });
                            if (notes != c.Notes)
                                (copy ??= items.ToArray())[ii] = c with { Notes = notes };
                            break;
                    }
                }
                if (copy == null)
                    continue;
                rebuilt ??= measures.ToBuilder();
                rebuilt[mi] = measures[mi] with
                {
                    Items = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(copy),
                };
            }
            if (rebuilt != null)
                result[vi] = voices[vi] with { Measures = rebuilt.MoveToImmutable() };
        }
        return result.MoveToImmutable();
    }

    /// <summary>
    /// The alteration last shown, before <paramref name="head"/> in its bar, for a head the
    /// <paramref name="match"/> names — when that last showing is one <paramref name="owesFrom"/>
    /// accepts and its alteration differs from <paramref name="alter"/>, so that an accidental
    /// is owed — else null. A last showing <paramref name="owesFrom"/> refuses (the head's own
    /// voice, the head's own octave) owes nothing here: the voice's walk already answered it.
    /// </summary>
    private static int? Owed(List<Head> bar, Head head, int alter, Func<Head, bool> match,
        Func<Head, bool> owesFrom)
    {
        // The LATEST earlier matching heads — several when they share a moment in different
        // voices, and then any one that differs is owed (it may be the one the eye read last).
        List<Head>? latest = null;
        foreach (var e in bar)
        {
            if (!Earlier(e, head) || !match(e))
                continue;
            if (latest == null)
            {
                latest = new() { e };
                continue;
            }
            bool later = false, earlier = false;
            foreach (var l in latest)
            {
                if (Earlier(l, e)) later = true;
                else if (Earlier(e, l)) earlier = true;
            }
            if (later && !earlier)
                latest = new() { e };
            else if (!earlier)
                latest.Add(e);
        }
        if (latest == null)
            return null;
        foreach (var l in latest)
        {
            if (!owesFrom(l))
                continue;
            int previous = WrittenPitch.Alter(l.Packed);
            if (previous != alter)
                return previous;
        }
        return null;
    }

    /// <summary>Whether <paramref name="a"/> sounds before <paramref name="b"/>: in one voice,
    /// by the order written; across voices, by moment — a grace before its main note's moment,
    /// and two heads at one moment not at all.</summary>
    private static bool Earlier(Head a, Head b)
    {
        if (a.Voice == b.Voice)
            return a.Item < b.Item;
        int byOnset = a.Onset.CompareTo(b.Onset);
        return byOnset < 0 || (byOnset == 0 && a.GraceRank < b.GraceRank);
    }

    /// <summary>Whether a tie from <paramref name="previous"/> (the voice's last sounding
    /// item) carries into a head at this position and pitch.</summary>
    private static bool TiedInto(MusicItem? previous, int staffPosition, int midi)
    {
        switch (previous)
        {
            case NoteItem { HasTieStart: true } n:
                return n.StaffPosition == staffPosition && n.Midi == midi;
            case ChordItem { HasTieStart: true } c:
                foreach (var m in c.Notes)
                    if (m.StaffPosition == staffPosition && m.Midi == midi)
                        return true;
                return false;
            default:
                return false;
        }
    }

    /// <summary>The glyph for <paramref name="alter"/> after <paramref name="previous"/> —
    /// with the restoring natural when it steps down inside one sign, as the walk's
    /// <c>GetDisplayAccidental</c> spells it.</summary>
    private static string Name(int alter, int previous, bool extraNatural)
    {
        bool restore = extraNatural && alter != 0 && Math.Abs(alter) < Math.Abs(previous)
            && previous * alter > 0;
        return alter switch
        {
            >= 2 => "doubleSharp",
            1 => restore ? "naturalSharp" : "sharp",
            0 => "natural",
            -1 => restore ? "naturalFlat" : "flat",
            _ => "doubleFlat",
        };
    }
}
