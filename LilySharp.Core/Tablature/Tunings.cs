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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Tablature;

/// <summary>
/// Provides predefined tunings and fret calculation for tablature.
/// MIDI note numbers for standard tunings (index 0 = lowest string).
/// String numbers in tablature: 1 = highest pitch, 6 = lowest pitch (for guitar).
/// </summary>
public static class Tunings
{
    /// <summary>One tuning: the symbol LilyPond knows it by, and its open strings as MIDI
    /// numbers ordered from the HIGHEST STRING NUMBER — which on a guitar is the lowest
    /// pitch, and on a ukulele or a banjo is not.</summary>
    private readonly record struct Spec(string LilyPondName, int[] Strings);

    // LILYPOND-REF: ly/string-tunings-init.ly — every \makeDefaultStringTuning in the file,
    // transcribed by reading it (tools: scratchpad/gen-tunings.ps1, 2026-09-13), not typed.
    // LilyPond writes each chord "from the highest string number (generally the lowest
    // pitch) first", which is exactly this array's order, so a tuning is copied across
    // WITHOUT reversing — and the re-entrant ones (ukulele, ukulele-d, every banjo) are
    // therefore NOT ascending and must never be "fixed" into ascending order.
    // c = 48, so e, = 40 and g' = 67.
    private static readonly Dictionary<TuningType, Spec> Table = new()
    {
        // guitars
        [TuningType.Guitar] = new("guitar-tuning", [40, 45, 50, 55, 59, 64]),
        [TuningType.Guitar7] = new("guitar-seven-string-tuning", [35, 40, 45, 50, 55, 59, 64]),
        [TuningType.GuitarDropD] = new("guitar-drop-d-tuning", [38, 45, 50, 55, 59, 64]),
        [TuningType.GuitarDropC] = new("guitar-drop-c-tuning", [36, 43, 48, 53, 57, 62]),
        [TuningType.GuitarOpenG] = new("guitar-open-g-tuning", [38, 43, 50, 55, 59, 62]),
        [TuningType.GuitarOpenD] = new("guitar-open-d-tuning", [38, 45, 50, 54, 57, 62]),
        [TuningType.GuitarDadgad] = new("guitar-dadgad-tuning", [38, 45, 50, 55, 57, 62]),
        [TuningType.GuitarLute] = new("guitar-lute-tuning", [40, 45, 50, 54, 59, 64]),
        [TuningType.GuitarAsus4] = new("guitar-asus4-tuning", [40, 45, 50, 52, 57, 64]),
        // basses. LilyPond spells this first one three times — bass-tuning,
        // bass-four-string-tuning and double-bass-tuning are the same four strings — and
        // writes back the four-string spelling, which is the one its own tab examples use.
        [TuningType.Bass] = new("bass-four-string-tuning", [28, 33, 38, 43]),
        [TuningType.BassDropD] = new("bass-drop-d-tuning", [26, 33, 38, 43]),
        [TuningType.Bass5] = new("bass-five-string-tuning", [23, 28, 33, 38, 43]),
        [TuningType.Bass6] = new("bass-six-string-tuning", [23, 28, 33, 38, 43, 48]),
        // orchestral strings. violin-tuning and mandolin-tuning are the same g d' a' e''.
        [TuningType.Violin] = new("violin-tuning", [55, 62, 69, 76]),
        [TuningType.Viola] = new("viola-tuning", [48, 55, 62, 69]),
        [TuningType.Cello] = new("cello-tuning", [36, 43, 50, 57]),
        // 5-string banjos — the 5th string is a high drone, so index 0 is the HIGHEST pitch.
        [TuningType.BanjoOpenG] = new("banjo-open-g-tuning", [67, 50, 55, 59, 62]),
        [TuningType.BanjoC] = new("banjo-c-tuning", [67, 48, 55, 59, 62]),
        [TuningType.BanjoModal] = new("banjo-modal-tuning", [67, 50, 55, 60, 62]),
        [TuningType.BanjoOpenD] = new("banjo-open-d-tuning", [69, 50, 54, 57, 62]),
        [TuningType.BanjoOpenDm] = new("banjo-open-dm-tuning", [69, 50, 53, 57, 62]),
        [TuningType.BanjoDoubleC] = new("banjo-double-c-tuning", [67, 48, 55, 60, 62]),
        [TuningType.BanjoDoubleD] = new("banjo-double-d-tuning", [69, 50, 55, 62, 64]),
        // ukuleles — the first two re-entrant, the last two not.
        [TuningType.Ukulele] = new("ukulele-tuning", [67, 60, 64, 69]),
        [TuningType.UkuleleD] = new("ukulele-d-tuning", [69, 62, 66, 71]),
        [TuningType.TenorUkulele] = new("tenor-ukulele-tuning", [55, 60, 64, 69]),
        [TuningType.BaritoneUkulele] = new("baritone-ukulele-tuning", [50, 55, 59, 64]),
    };

    /// <summary>
    /// The word a <c>.lys</c> writes → the tuning it names. THE one reading of these words:
    /// the part header (<c>PartHeaderDefaults</c>), the score row (<c>RenderSpecParser</c>)
    /// and the LilyPond twin (<c>LilyPondExporter</c>) all ask here.
    /// </summary>
    /// <remarks>
    /// ⚠️ Those three held a COPY EACH of a five-arm switch until 2026-09-13, which is three
    /// copies of any future defect in it — the same shape the tab STYLE word was pulled out
    /// of (<c>TabRenderVocabularyValidator.IsNumbersOnly</c>), and it would have been three
    /// places to add twenty-five tunings to, with nothing to notice if one was missed.
    /// <para>
    /// The spelling rule: LilyPond's symbol without its <c>-tuning</c> suffix, with
    /// <c>&lt;n&gt;-string</c> written as the digit and the hyphens dropped — so
    /// <c>bass-five-string-tuning</c> is <c>bass5</c> (which is what it already was) and
    /// <c>guitar-drop-d-tuning</c> is <c>guitardropd</c>. Every word is LilyPond's: the two
    /// Lily#-own second spellings, <c>standard</c> (= <c>guitar</c>) and <c>uke</c>
    /// (= <c>ukulele</c>), were retired on 2026-09-15 before 0.7.0 shipped (owner decision —
    /// one spelling per tuning; the only doubles left are LilyPond's own).
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, TuningType> ByName = new(StringComparer.Ordinal)
    {
        ["guitar"] = TuningType.Guitar,
        ["guitar7"] = TuningType.Guitar7,
        ["guitardropd"] = TuningType.GuitarDropD,
        ["guitardropc"] = TuningType.GuitarDropC,
        ["guitaropeng"] = TuningType.GuitarOpenG,
        ["guitaropend"] = TuningType.GuitarOpenD,
        ["guitardadgad"] = TuningType.GuitarDadgad,
        ["guitarlute"] = TuningType.GuitarLute,
        ["guitarasus4"] = TuningType.GuitarAsus4,
        ["bass"] = TuningType.Bass,
        ["bass4"] = TuningType.Bass,                    // LP's bass-four-string-tuning
        ["doublebass"] = TuningType.Bass,               // LP's double-bass-tuning
        ["bassdropd"] = TuningType.BassDropD,
        ["bass5"] = TuningType.Bass5,
        ["bass6"] = TuningType.Bass6,
        ["violin"] = TuningType.Violin,
        ["mandolin"] = TuningType.Violin,               // LP's mandolin-tuning, same strings
        ["viola"] = TuningType.Viola,
        ["cello"] = TuningType.Cello,
        ["banjoopeng"] = TuningType.BanjoOpenG,
        ["banjoc"] = TuningType.BanjoC,
        ["banjomodal"] = TuningType.BanjoModal,
        ["banjoopend"] = TuningType.BanjoOpenD,
        ["banjoopendm"] = TuningType.BanjoOpenDm,
        ["banjodoublec"] = TuningType.BanjoDoubleC,
        ["banjodoubled"] = TuningType.BanjoDoubleD,
        ["ukulele"] = TuningType.Ukulele,
        ["ukuleled"] = TuningType.UkuleleD,
        ["tenorukulele"] = TuningType.TenorUkulele,
        ["baritoneukulele"] = TuningType.BaritoneUkulele,
    };

    /// <summary>Every tuning word the language takes, sorted. The ONE home of the list —
    /// the part-header validator, the score-row validator, the editor's completion and the
    /// grammar's <c>TuningName</c> are all readers of it.</summary>
    public static IReadOnlyCollection<string> Names { get; } =
        [.. ByName.Keys.OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>A tuning word → its tuning; an unknown, differently-cased or absent word →
    /// the guitar, which is what a tab with nothing said falls back to.</summary>
    public static TuningType Parse(string? name) =>
        name != null && ByName.TryGetValue(name, out var type) ? type : TuningType.Guitar;

    /// <summary>The LilyPond predefined-tuning symbol the twin writes for a tuning.</summary>
    public static string LilyPondName(TuningType type) => Table[type].LilyPondName;

    /// <summary>Standard 6-string guitar tuning (MIDI notes, index 0 = lowest string).</summary>
    public static int[] Guitar => Table[TuningType.Guitar].Strings;

    /// <summary>Standard 4-string bass tuning (MIDI notes, index 0 = lowest string).</summary>
    public static int[] Bass => Table[TuningType.Bass].Strings;

    /// <summary>Returns the tuning array (index 0 = lowest string) for the given tuning type.</summary>
    public static int[] GetTuning(TuningType type) =>
        Table.TryGetValue(type, out var spec) ? spec.Strings : Guitar;

    /// <summary>Returns the number of strings for the given tuning type.</summary>
    public static int GetStringCount(TuningType type) => GetTuning(type).Length;

    /// <summary>
    /// True for bass tunings, which sound an octave BELOW where they are written
    /// in bass clef (the bass guitar is a transposing instrument). Tab frets are
    /// therefore computed from the written pitch shifted down one octave.
    /// ⚠️ <see cref="TuningType.Bass"/> is the double bass's tuning too, and the double bass
    /// reads bass clef 8va for the same reason — so the one answer serves both.
    /// </summary>
    public static bool IsBass(TuningType type) =>
        type is TuningType.Bass or TuningType.BassDropD
             or TuningType.Bass5 or TuningType.Bass6;

    /// <summary>The DEFAULT sounding transposition (semitones) a tuning implies when a
    /// part gives no explicit <c>transposition</c> and no instrument preset: −12 for
    /// bass tunings (they sound 8vb from bass-clef notation), 0 otherwise. This is the
    /// fallback the resolved <see cref="Svg.Model.Staff.Transposition"/> uses so a bare
    /// <c>tuning bass</c> still frets the sounding pitch.</summary>
    public static int TuningTransposition(TuningType type) => IsBass(type) ? -12 : 0;

    /// <summary>The octave a CLEF already carries: a <c>treble_8</c> part (standard
    /// guitar/tenor notation) sounds an octave below what is written, <c>treble^8</c>
    /// an octave above, <c>bass_8</c> an octave below. LilyPond pitches are SOUNDING
    /// pitches; Lily# writes display pitches, so the tab and MIDI recover the sounding
    /// octave from this plus the part's <c>transposition</c>.</summary>
    public static int ClefOctaveShift(Svg.Model.ClefType clef) => clef switch
    {
        Svg.Model.ClefType.Treble8Below => -12,
        Svg.Model.ClefType.Bass8Below => -12,
        Svg.Model.ClefType.Treble8Above => 12,
        _ => 0,
    };

    /// <summary>The total written→sounding shift for a tab staff: the clef octave plus
    /// the part's resolved <c>transposition</c> (which already folds in the bass/preset
    /// default). Both the fret calculation and MIDI playback read this one value, so a
    /// note frets, sounds, and prints consistently.</summary>
    public static int SoundingShift(Svg.Model.ClefType clef, int transposition) =>
        ClefOctaveShift(clef) + transposition;

    /// <summary>
    /// String/fret allocation for a CHORD, mimicking LilyPond: notes with an
    /// explicit string number claim it first; the rest, highest pitch first,
    /// take the HIGHEST free string whose fret is playable (≥ 0) and within
    /// the maximum stretch (4) of the frets already chosen. Results are in
    /// input-note order.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm determine-frets-and-strings;
    /// scm/translation-functions.scm:864 maximumFretStretch default 4.
    /// </remarks>
    public static (int stringNum, int fret)[] CalculateChordFrets(
        System.Collections.Generic.IReadOnlyList<(int Midi, int? StringNumber)> notes,
        int[] tuning)
    {
        const int maxStretch = 4;
        int stringCount = tuning.Length;
        var result = new (int stringNum, int fret)[notes.Count];
        var freeStrings = new System.Collections.Generic.List<int>();
        for (int s = 1; s <= stringCount; s++)
            freeStrings.Add(s);
        var chosenFrets = new System.Collections.Generic.List<int>();

        int FretOn(int midi, int stringNum) => midi - tuning[stringCount - stringNum];
        bool CloseEnough(int fret)
        {
            foreach (int f in chosenFrets)
                if (f != 0 && fret != 0 && System.Math.Abs(fret - f) > maxStretch)
                    return false;
            return true;
        }

        // Assigned strings first.
        for (int i = 0; i < notes.Count; i++)
        {
            result[i] = (0, -1);
            if (notes[i].StringNumber is int s && s >= 1 && s <= stringCount)
            {
                int fret = FretOn(notes[i].Midi, s);
                if (fret >= 0)
                {
                    result[i] = (s, fret);
                    freeStrings.Remove(s);
                    chosenFrets.Add(fret);
                }
            }
        }

        // Unassigned notes, highest pitch first.
        var order = new System.Collections.Generic.List<int>();
        for (int i = 0; i < notes.Count; i++)
            if (result[i].stringNum == 0)
                order.Add(i);
        order.Sort((a, b) => notes[b].Midi.CompareTo(notes[a].Midi));

        foreach (int i in order)
        {
            int chosen = -1, chosenFret = 0;
            foreach (int s in freeStrings) // ascending = highest string first
            {
                int fret = FretOn(notes[i].Midi, s);
                if (fret >= 0 && fret <= 24 && CloseEnough(fret))
                {
                    chosen = s;
                    chosenFret = fret;
                    break;
                }
            }
            if (chosen < 0)
            {
                // LP warns "No string for pitch"; keep the note on the lowest
                // string rather than dropping it.
                chosen = stringCount;
                chosenFret = System.Math.Max(0, FretOn(notes[i].Midi, stringCount));
            }
            result[i] = (chosen, chosenFret);
            freeStrings.Remove(chosen);
            chosenFrets.Add(chosenFret);
        }
        return result;
    }

    /// <summary>
    /// How wide one comfortable position is on a BASS, in frets: index, middle and little
    /// finger on three neighbouring frets. One fret more is a stretch; more than that moves
    /// the hand.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER SPECIFIED (2026-09-14). A bass — electric or double — is fingered
    /// 1-2-4 in the low positions: index on fret p, middle on p+1, little finger on p+2, and
    /// the ring finger unused. Reaching p+3 is a stretch of the little finger: playable, a
    /// little awkward, and still cheaper than moving the hand two frets. One width for the
    /// whole neck, because one number is what a reader can predict.
    /// </remarks>
    public const int HandSpan = 3;

    /// <summary>
    /// How wide one comfortable position is on every other fretted instrument: one finger per
    /// fret, four fingers on four frets.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER SPECIFIED (2026-09-14): "on a guitar the four fingers play four
    /// frets — with the index finger at the 5th, the little finger plays the 8th with no
    /// stretch and no penalty".
    /// </remarks>
    public const int OneFingerPerFretSpan = 4;

    /// <summary>The comfortable position width for a tuning: <see cref="HandSpan"/> on a bass,
    /// <see cref="OneFingerPerFretSpan"/> otherwise.</summary>
    public static int HandSpanFor(TuningType type) => IsBass(type) ? HandSpan : OneFingerPerFretSpan;

    /// <summary>
    /// The highest fret the index finger may sit at for the hand to count as being in LOW
    /// position — the only place an open string is used.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER SPECIFIED (2026-09-14): "low position is the open string up to about
    /// the fifth fret", and "an open string is natural while playing in low position; one open
    /// note in the middle of a passage played higher up is not".
    /// </remarks>
    public const int LowPositionTop = 5;

    /// <summary>
    /// A leap of this many semitones or more from the previous note frees the hand: the
    /// fingering planner charges the shift after it as it charges one after a rest.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER APPROVED (2026-09-14). Without it nothing brought the hand down
    /// after a passage high on the neck as long as the notes still fitted there:
    /// <c>test/tab-technique-letters</c> played <c>e a b e'</c> at the 19th–21st frets right
    /// after a b' at the 19th. An octave is the leap a player shifts for anyway.
    /// </remarks>
    public const int LeapResetInterval = 12;

    /// <summary>
    /// Calculates the string and fret for a given MIDI pitch: the preferred string when it can
    /// play the pitch, otherwise the lowest fret any string offers.
    /// </summary>
    /// <param name="midiPitch">The MIDI note number to place.</param>
    /// <param name="tuning">The tuning array (index 0 = lowest string).</param>
    /// <param name="preferredString">Preferred string (1 = highest, 0 = auto).</param>
    /// <returns>A tuple of (stringNumber, fret) where stringNumber 1 = highest pitch string.</returns>
    /// <remarks>
    /// This is not how a tab note's string is chosen: that is <see cref="TabFingeringPlanner"/>,
    /// which plans the whole voice from where the hand is (LILYSHARP-OWN, and deliberately not
    /// LilyPond's first-string-from-the-top rule). This gives the fret of a known string, and
    /// places a pitch with nothing else to go on. Equal frets (a re-entrant tuning) go to the
    /// higher string, which is searched first.
    /// </remarks>
    public static (int stringNum, int fret) CalculateFret(int midiPitch, int[] tuning, int preferredString = 0)
    {
        int stringCount = tuning.Length;

        // Convert 1-based string number to array index
        // String 1 (highest) = index stringCount-1
        // String N (lowest) = index 0
        int ToIndex(int str) => stringCount - str;
        int ToStringNum(int idx) => stringCount - idx;

        // If preferred string is specified, use it
        if (preferredString >= 1 && preferredString <= stringCount)
        {
            int idx = ToIndex(preferredString);
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            if (fret >= 0 && fret <= 24)
            {
                return (preferredString, fret);
            }
        }

        int bestString = stringCount; // lowest string as fallback
        int bestFret = 99;

        // Search from highest to lowest string
        for (int idx = stringCount - 1; idx >= 0; idx--)
        {
            int fret = midiPitch - tuning[idx];
            if (fret < 0 || fret > 24) continue;
            if (fret < bestFret)
            {
                bestString = ToStringNum(idx);
                bestFret = fret;
            }
        }

        // If no valid position found, return lowest string with calculated fret
        if (bestFret == 99)
        {
            bestFret = midiPitch - tuning[0];
            if (bestFret < 0) bestFret = 0;
        }

        return (bestString, bestFret);
    }
}
