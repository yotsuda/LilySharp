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
/// <summary>
/// Where the left hand is: the lowest and highest stopped frets it holds.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN, USER SPECIFIED (2026-09-14). The range carries no strings: whether a stretch
/// skips a string is a question about two notes IN A ROW (<see cref="Tunings.SkipCost"/>), not
/// about the two ends of the range — an end may be a note played bars ago that the hand no
/// longer holds (さよならエレジー section C bar 3: f,, after bes,, and a rest is the fourth
/// string's 1st, not a "stretch" to a second-string 4th two notes earlier).
/// </remarks>
public readonly record struct HandPosition(int Low, int High)
{
    /// <summary>A bare fret range.</summary>
    public static implicit operator HandPosition((int Low, int High) range) =>
        new(range.Low, range.High);
}

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
    /// <c>guitar-drop-d-tuning</c> is <c>guitardropd</c>. Two words are Lily#'s own and not
    /// LilyPond's: <c>standard</c> and <c>uke</c>, both older than this table.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, TuningType> ByName = new(StringComparer.Ordinal)
    {
        ["guitar"] = TuningType.Guitar,
        ["standard"] = TuningType.Guitar,               // Lily#'s own word for it
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
        ["uke"] = TuningType.Ukulele,                   // Lily#'s own word for it
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
    /// position is forgotten and the note is placed as a first note is — as low as it can be.
    /// The previous note's STRING is still known, so an octave played in the octave shape
    /// (index finger on the root, little finger two strings up — <see cref="SkipCost"/>) is
    /// still recognised: Amanda section A3 bar 1, g on the fourth string's 3rd and g' on the
    /// second string's 5th, not the open first string (USER SPECIFIED, 2026-09-14).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER APPROVED (2026-09-14). Without it nothing brought the hand down
    /// after a passage high on the neck as long as the notes still fitted there:
    /// <c>test/tab-technique-letters</c> played <c>e a b e'</c> at the 19th–21st frets right
    /// after a b' at the 19th. An octave is the leap a player shifts for anyway.
    /// </remarks>
    public const int LeapResetInterval = 12;    /// <summary>
    /// What a fingering costs when it leaves the hand above low position: as much as moving the
    /// hand two frets, so staying up the neck ties with coming down, and a tie comes down.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER APPROVED (2026-09-14). At 1 the hand stayed a position too high after
    /// a passage up the neck — Arthur's Theme Outro bar 1 took d at the fourth string's 10th
    /// (inside 7..10, 0 + 1) over the third string's 5th (a move, 2).
    /// </remarks>
    public const int AboveLowPositionCost = 2;

    /// <summary>
    /// What leaving the string costs for the note a slur ends on: as much as moving the hand two
    /// frets. A slur on a fretted instrument is a slide, hammer-on or pull-off — played on the
    /// string the slur started on.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER SPECIFIED (2026-09-14, Real Gone Intro bars 12–13:
    /// <c>e,4\3( | b,,8)</c> — the b,, is the third string's 2nd, where the e on the third
    /// string's 7th slides to, not the fourth string's 7th inside the hand).
    /// <para>
    /// ⚠️ Not across a leap of an octave or more (<see cref="LeapResetInterval"/>): that is no
    /// slide, and with the hand freed the same string pulled the octave up to the 16th fret
    /// (さよならエレジー section D <c>aes,,( aes,)</c>, Amanda Interlude2 <c>e,4( fis8.)</c>).
    /// </para>
    /// </remarks>
    public const int SlurAcrossStringsCost = 2;

    /// <summary>What an open string costs while the hand is NOT in low position: more than any
    /// real move, so it is used only when no string can stop the note.</summary>
    private const int OpenOutOfPositionCost = 100;

    /// <summary>
    /// Calculates the best string and fret for a given MIDI pitch.
    /// </summary>
    /// <param name="midiPitch">The MIDI note number to place.</param>
    /// <param name="tuning">The tuning array (index 0 = lowest string).</param>
    /// <param name="preferredString">Preferred string (1 = highest, 0 = auto).</param>
    /// <param name="position">The lowest and highest STOPPED frets the hand holds in the
    /// current position, or null when none has been played yet.</param>
    /// <param name="next">The next note's sounding pitch and its fixed string (0 = free), or
    /// null for the last note. Each candidate costs <see cref="MoveCost"/> for this note plus
    /// the cheapest <see cref="MoveCost"/> of the next note from where this one leaves the
    /// hand, plus <see cref="SkipCost"/> for skipping a string from the previous note and to the
    /// next; the cheapest wins, and a tie goes to the lower fret.</param>
    /// <param name="previousString">The string the previous note was played on (open strings
    /// included), or 0 when there is none.</param>
    /// <param name="previousFret">The previous note's fret (0 = open, -1 = none).</param>
    /// <param name="slurFromString">The string the slur this note ends started on, or 0 when
    /// this note ends no slur (<see cref="SlurAcrossStringsCost"/>).</param>
    /// <returns>A tuple of (stringNumber, fret) where stringNumber 1 = highest pitch string.</returns>
    /// <remarks>
    /// LILYSHARP-OWN, and deliberately not LilyPond's. LilyPond takes the first string from
    /// the top with a non-negative integer fret (scm/translation-functions.scm:591-796
    /// determine-frets-and-strings), so an open string always wins and the hand is never
    /// considered — playable, but awkward to read. What this wants instead, in the words it
    /// was specified in: track where the left hand IS and pick the fret that moves it least;
    /// and do not pay much for the answer, because no automatic chooser gets fingering right
    /// anyway.
    /// <para>
    /// So (USER SPECIFIED, 2026-09-14): a position is the range of frets its stopped notes
    /// hold; a candidate costs how far it would stretch that range past
    /// <see cref="HandSpan"/> frets (a stretch of one fret costs 1, a move of two costs 2),
    /// plus <see cref="SkipCost"/>, <see cref="AboveLowPositionCost"/> and
    /// <see cref="SlurAcrossStringsCost"/>, PLUS the same for the next note played the easiest
    /// way from there; an open string is free in low position and avoided elsewhere; and a tie
    /// goes to the lower fret. One note of lookahead, no backtracking.
    /// </para>
    /// <para>
    /// ⚠️ Why the next note: the user's words were "move toward where the next note is easy to
    /// play". Scoring the current note alone let a stretch lose to a lower two-fret move.
    /// </para>
    /// <para>
    /// ⚠️ KNOWN LIMIT (2026-09-14): one note of lookahead cannot see a move a phrase needs
    /// several notes later (9 to 5 (Morning Train) (Xanadu) section A bar 6, Amanda section B1
    /// bar 6), and no cost weights served both high melodic runs and coming back down: a
    /// lookahead of four notes, and a cost for the jump between notes in a row, were tried and
    /// each broke passages the user had approved. The planned replacement is a dynamic
    /// programme over the whole phrase with the hand position as its state.
    /// </para>
    /// <para>
    /// ⚠️ The position is a RANGE and not the index finger's fret, and that is the point.
    /// After <c>f</c> alone on the second string's third fret nothing yet says whether the
    /// index or the little finger holds it; the <c>ees</c> after it decides — the second
    /// string's first fret keeps the range 1..3 (index on 1, little finger on 3). A rule that
    /// put the index finger on every note it moved to fixed the hand at 3..6 there and sent
    /// that <c>ees</c> to the third string's sixth fret (user report, 2026-09-14,
    /// <c>tab-fret.lys</c>).
    /// </para>
    /// <para>
    /// ⚠️ This replaced a rule that scored |fret − previous fret| and nothing else, which
    /// kept the hand still by walking one string all the way up: <c>test/tab-indent</c> came
    /// out 3 5 7 8 10 12 on a single string, and <c>test/tab-beam-script</c> took an A at the
    /// fifth fret with the open string right there. Distance alone has no reason to come back
    /// down.
    /// </para>
    /// </remarks>
    public static (int stringNum, int fret) CalculateFret(int midiPitch, int[] tuning,
        int preferredString = 0, HandPosition? position = null,
        (int Midi, int PreferredString)? next = null, int handSpan = HandSpan,
        int previousString = 0, int previousFret = -1, int slurFromString = 0)
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

        // Auto: one number per string — what this note costs the hand, plus what the next note
        // then costs at its easiest. Cheapest wins; a tie goes to the lower fret.
        int bestString = stringCount; // lowest string as fallback
        int bestFret = 99;
        int bestCost = int.MaxValue;
        // A next note that leaps an octave or more will forget the position anyway, so it has
        // no say in where this one goes.
        if (next is { } leap && System.Math.Abs(leap.Midi - midiPitch) >= LeapResetInterval)
            next = null;

        // Search from highest to lowest string
        for (int idx = stringCount - 1; idx >= 0; idx--)
        {
            int openPitch = tuning[idx];
            int fret = midiPitch - openPitch;
            if (fret < 0 || fret > 24) continue;

            int str = ToStringNum(idx);
            int cost = MoveCost(position, fret, handSpan)
                       + SkipCost(previousString, previousFret, str, fret);
            // The note a slur ends on stays on the slur's string (a slide or legato).
            if (slurFromString > 0 && str != slurFromString)
                cost += SlurAcrossStringsCost;
            // One point for a fingering that leaves the hand above low position: the same shape
            // one string lower and five frets higher otherwise costs exactly as much, and the
            // hand never came back down (USER SPECIFIED, 2026-09-14: "if the music does not go
            // up, go back as low as possible" — さよならエレジー played whole sections at the
            // 6th-9th frets of the fifth and fourth strings; Arthur's Theme Outro bar 2 took f#
            // at the third string's 9th instead of the second string's 4th).
            if (fret > 0 && Place(position, fret, fret, handSpan).Low > LowPositionTop)
                cost += AboveLowPositionCost;
            // The same pitch again costs nothing wherever this one goes.
            if (next is { } n && n.Midi != midiPitch)
                cost += NextMoveCost(
                    fret > 0 ? Place(position, fret, fret, handSpan) : position,
                    n, tuning, handSpan, str, fret);

            // A tie goes to the lower fret — one rule, whatever the melody does next.
            if (cost < bestCost || (cost == bestCost && fret < bestFret))
            {
                bestCost = cost;
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

    /// <summary>What playing <paramref name="fret"/> costs the hand: how many frets it would
    /// widen the position past what the hand already spans — never less than
    /// <paramref name="handSpan"/> — so 0 anywhere inside it, 1 for a stretch of one fret, 2 or
    /// more for moving the hand. A first note with no position yet costs nothing; an open
    /// string costs nothing in low position (<see cref="LowPositionTop"/>) and more than any
    /// move elsewhere.</summary>
    /// <remarks>
    /// ⚠️ "Past what the hand already spans", not "past <paramref name="handSpan"/>": once a
    /// stretch has been paid for, the hand IS stretched, and the notes inside it are free.
    /// Charging the stretch again on every note inside it let an open string win over a fret
    /// the stretched hand was already covering (<c>tab-fret.lys</c> section B bar 6: the g
    /// under a 3..6 hand is the second string's 5th, not the open first string — user,
    /// 2026-09-14).
    /// </remarks>
    public static int MoveCost(HandPosition? position, int fret, int handSpan = HandSpan)
    {
        if (position is not { } p)
            return 0;
        if (fret == 0)
            return p.Low <= LowPositionTop ? 0 : OpenOutOfPositionCost;
        int spans = System.Math.Max(handSpan, p.High - p.Low + 1);
        return System.Math.Max(0,
            System.Math.Max(p.High, fret) - System.Math.Min(p.Low, fret) + 1 - spans);
    }

    /// <summary>
    /// What skipping a string between two notes in a row costs: as much as moving the hand two
    /// frets.
    /// </summary>
    public const int StringSkipCost = 2;

    /// <summary>
    /// What going from the previous note to this one costs across the strings:
    /// <see cref="StringSkipCost"/> when the two notes skip a string — except the octave shape,
    /// which is free.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN, USER SPECIFIED (2026-09-14):
    /// <list type="bullet">
    /// <item>A SKIP (the strings two or more apart) costs, open strings included: "I don't want
    /// a skip between the second and fourth strings" — Arthur's Theme section E bar 2,
    /// <c>g,\2 d, g,,</c>, the d is the third string's 5th, not the open second string.</item>
    /// <item>THE OCTAVE SHAPE is not a skip: two strings apart and two frets further along
    /// toward the higher string (the fourth string's 3rd and the second string's 5th, either
    /// way round) — "in bass octaves the index finger plays the root and the little finger the
    /// octave, very often".</item>
    /// </list>
    /// <para>
    /// ⚠️ Kept deliberately to these two (USER DECISION, 2026-09-14): a point for an open string
    /// straight after the neighbouring open string, and one more for a stretch across a skip,
    /// were tried and taken out as too fine a distinction to keep stable.
    /// </para>
    /// </remarks>
    public static int SkipCost(int previousString, int previousFret, int stringNum, int fret)
    {
        if (previousString <= 0 || stringNum <= 0)
            return 0;
        int apart = System.Math.Abs(previousString - stringNum);
        bool octaveShape = apart == 2 && previousFret > 0 && fret > 0
                           && (stringNum < previousString ? fret - previousFret : previousFret - fret) == 2;
        return apart > 1 && !octaveShape ? StringSkipCost : 0;
    }

    /// <summary>The cheapest <see cref="MoveCost"/> (plus <see cref="SkipCost"/> from the
    /// current candidate) of the next note from <paramref name="position"/>: over every string,
    /// or only its fixed one.</summary>
    private static int NextMoveCost(HandPosition? position,
        (int Midi, int PreferredString) next, int[] tuning, int handSpan, int fromString, int fromFret)
    {
        int stringCount = tuning.Length;
        int best = int.MaxValue;
        for (int idx = 0; idx < stringCount; idx++)
        {
            if (next.PreferredString >= 1 && next.PreferredString <= stringCount
                && stringCount - idx != next.PreferredString)
                continue;
            int fret = next.Midi - tuning[idx];
            if (fret < 0 || fret > 24) continue;
            best = System.Math.Min(best, MoveCost(position, fret, handSpan)
                                         + SkipCost(fromString, fromFret, stringCount - idx, fret));
        }
        return best == int.MaxValue ? 0 : best;
    }

    /// <summary>
    /// The position after stopped frets <paramref name="low"/>..<paramref name="high"/> are
    /// played. Within <see cref="HandSpan"/> + 1 frets (a comfortable reach or a stretch) they
    /// widen it; further, the hand slides only AS FAR AS IT MUST — the part of the old range
    /// still within <see cref="HandSpan"/> of the new frets stays in the position.
    /// </summary>
    /// <remarks>
    /// USER SPECIFIED (2026-09-14, <c>tab-fret.lys</c> section B): a g on the fourth string's
    /// 3rd fret after bes at its 6th slides the hand from 4..6 to 3..6 — index on 3, little
    /// finger stretched to 6 — and does not restart it at 3..3.
    /// </remarks>
    public static HandPosition Place(HandPosition? position, int low, int high,
        int handSpan = HandSpan)
    {
        if (position is not { } p)
            return new(low, high);
        int newLow, newHigh;
        int keep = handSpan;
        if (System.Math.Max(p.High, high) - System.Math.Min(p.Low, low) <= keep)
        {
            newLow = System.Math.Min(p.Low, low);
            newHigh = System.Math.Max(p.High, high);
        }
        else
        {
            // The hand slides only as far as it must, and it may land stretched: what stays is
            // the old range within a STRETCH of the new frets. Keeping only a comfortable reach
            // dropped too much — Arthur's Theme section D bar 10: from 2..4, a on the second
            // string's 7th left the hand at 7..7 instead of 4..7, and the c# after it went to the
            // fourth string's 9th (a skip across the third string) instead of the third
            // string's 4th.
            int keptLow = System.Math.Max(p.Low, high - keep);
            int keptHigh = System.Math.Min(p.High, low + keep);
            (newLow, newHigh) = keptLow <= keptHigh
                ? (System.Math.Min(low, keptLow), System.Math.Max(high, keptHigh))
                : (low, high);
        }
        return new(newLow, newHigh);
    }
}