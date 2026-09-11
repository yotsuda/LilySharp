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
using System.Text;

namespace LilySharp.Core.Music;

/// <summary>
/// The set of named chord qualities Lily# understands in chord-name entry
/// (<c>c:maj7</c>, <c>a:m</c>, …). Each maps to a fixed set of chord tones and a
/// display suffix.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-entry.scm — default chord step construction; the
/// named modifiers in ly/chord-modifiers-init.ly (m, dim, aug, maj, sus). The
/// tone sets below are the standard jazz/pop spellings LilyPond produces.
/// </remarks>
public enum ChordQuality
{
    /// <summary>Major triad (root, major third, perfect fifth); no suffix.</summary>
    Major,
    /// <summary>Minor triad (root, minor third, perfect fifth); suffix <c>m</c>.</summary>
    Minor,
    /// <summary>Diminished triad (root, minor third, diminished fifth); suffix <c>dim</c>.</summary>
    Diminished,
    /// <summary>Augmented triad (root, major third, augmented fifth); suffix <c>aug</c>.</summary>
    Augmented,
    /// <summary>Dominant seventh: major triad plus a minor seventh; suffix <c>7</c>.</summary>
    Dominant7,
    /// <summary>Major seventh: major triad plus a major seventh; suffix <c>maj7</c>.</summary>
    Major7,
    /// <summary>Minor seventh: minor triad plus a minor seventh; suffix <c>m7</c>.</summary>
    Minor7,
    /// <summary>Minor-major seventh: minor triad plus a major seventh; suffix <c>m maj7</c>.</summary>
    MinorMajor7,
    /// <summary>Diminished seventh: diminished triad plus a diminished seventh; suffix <c>dim7</c>.</summary>
    Diminished7,
    /// <summary>Half-diminished seventh: diminished triad plus a minor seventh; suffix <c>m7♭5</c>.</summary>
    HalfDiminished7,
    /// <summary>Major sixth: major triad plus a major sixth; suffix <c>6</c>.</summary>
    Major6,
    /// <summary>Minor sixth: minor triad plus a major sixth; suffix <c>m6</c>.</summary>
    Minor6,
    /// <summary>Dominant ninth: dominant seventh plus a major ninth; suffix <c>9</c>.</summary>
    Dominant9,
    /// <summary>Major ninth: major seventh plus a major ninth; suffix <c>maj9</c>.</summary>
    Major9,
    /// <summary>Minor ninth: minor seventh plus a major ninth; suffix <c>m9</c>.</summary>
    Minor9,
    /// <summary>Suspended second (root, major second, perfect fifth); suffix <c>sus2</c>.</summary>
    Sus2,
    /// <summary>Suspended fourth (root, perfect fourth, perfect fifth); suffix <c>sus4</c>.</summary>
    Sus4,
    /// <summary>Dominant seventh with a suspended fourth (no third); suffix <c>7sus4</c>.</summary>
    Dominant7Sus4,

    // The altered and extended qualities. Their ENTRY spellings use '+'/'-' for the
    // altered tension, never '#'/'b' — those belong to the root and the bass alone, which
    // is what keeps "Bb9" unambiguous (see the ByToken remark). They PRINT with the ♭/♯
    // glyphs all the same, exactly as Cm7♭5 has always done.
    /// <summary>Dominant seventh with a lowered fifth; entry <c>7-5</c>, prints <c>7♭5</c>.</summary>
    Dominant7Flat5,
    /// <summary>Dominant seventh with a raised fifth; entry <c>7+5</c>, prints <c>7♯5</c>.</summary>
    Dominant7Sharp5,
    /// <summary>Dominant seventh with a lowered ninth; entry <c>7-9</c>, prints <c>7♭9</c>.</summary>
    Dominant7Flat9,
    /// <summary>Dominant seventh with a raised ninth; entry <c>7+9</c>, prints <c>7♯9</c>.</summary>
    Dominant7Sharp9,
    /// <summary>Dominant seventh with a raised eleventh; entry <c>7+11</c>, prints <c>7♯11</c>.</summary>
    Dominant7Sharp11,
    /// <summary>Major triad plus a ninth, no seventh; suffix <c>add9</c>.</summary>
    MajorAdd9,

    // The plain extensions. A thirteenth chord does NOT carry the eleventh: the natural
    // eleventh a mechanical stack lands on is a semitone from the major third — an
    // interval no player voices and no chart means. Lily# takes the eleventh only when
    // the symbol asks for it by name ('11', 'm11').
    // ⚠️ This is LilyPond's own rule for a NATURAL third, not a departure from it —
    // LILYPOND-REF: scm/chord-entry.scm:155-162 construct-chord-elements — "If natural 11 +
    // natural 3 is present, but not given explicitly, we remove the 11" (remove-step), so
    // ':13' and ':maj13' realize without
    // it. The rule does NOT fire on a MINOR third (its alteration is not 0), so ':m13'
    // keeps the eleventh in LilyPond; the twin writes ':m13^11' to say Lily#'s set
    // (LilyPondModifier below).
    /// <summary>Dominant eleventh; suffix <c>11</c>.</summary>
    Dominant11,
    /// <summary>Dominant thirteenth, without the eleventh; suffix <c>13</c>.</summary>
    Dominant13,
    /// <summary>Minor eleventh; suffix <c>m11</c>.</summary>
    Minor11,
    /// <summary>Minor thirteenth, without the eleventh; suffix <c>m13</c>.</summary>
    Minor13,
    /// <summary>Major thirteenth, without the eleventh; suffix <c>maj13</c>.</summary>
    Major13,
}

/// <summary>
/// A printed chord symbol: the characters, and the index in them where the SUPERSCRIPT
/// begins. The superscript runs from there to the slash bass (or to the end).
/// </summary>
/// <param name="Text">The symbol as it prints, e.g. <c>Cm7♭5</c>, <c>C°7</c>, <c>Am7/C</c>.</param>
/// <param name="SuperFrom">Where the raised run starts, or <see cref="NoSuperscript"/> when
/// the whole symbol stands on one baseline (a plain triad, <c>Cm</c>, <c>C°</c>, a Roman
/// degree, free text).</param>
/// <remarks>
/// LILYPOND-REF: scm/chord-ignatzek-names.scm:179-209 ignatzek-format-chord-name — the
///   four things a printed name is made of, in this order: <c>root-markup</c>, the prefix
///   modifiers (the minor <c>m</c>, kerned by chordPrefixSpacer), the ONE raised group
///   <c>(make-super-markup to-be-raised-stuff)</c>, and the slash separator with the bass.
///   The raised group is a single contiguous span between the other three, which is the
///   fact this one index records.
/// <para>
/// ⚠️ ONE INDEX, NOT TWO. The raised run always ENDS at the slash or at the end of the
/// string — LilyPond puts nothing on the baseline between the quality and the bass — so a
/// second index would be a second spelling of a fact the string already carries, and the
/// two could disagree. The renderer and the reservation both find the end the same way
/// (<c>ChordNameGlyphRun</c>).
/// </para>
/// </remarks>
public readonly record struct ChordSymbolText(string Text, int SuperFrom)
{
    /// <summary>The symbol stands on one baseline.</summary>
    public const int NoSuperscript = -1;

    /// <summary>A symbol drawn on one baseline — free text, a Roman degree, "N.C.".</summary>
    public static ChordSymbolText Flat(string text) => new(text, NoSuperscript);
}

/// <summary>One chord tone: a diatonic step above the root (0=root, 2=third,
/// 4=fifth, 6=seventh, 8=ninth) and its semitone offset above the root. The step
/// gives the spelled LETTER; the semitone gives the actual pitch (hence the
/// accidental).</summary>
public readonly record struct ChordToneSpec(int DiatonicStep, int Semitone);

/// <summary>A spelled chord tone: a diatonic letter step (0=C..6=B), an accidental
/// alteration, and how many octaves above the root it sits.</summary>
public readonly record struct ChordTone(int Step, int Alter, int OctaveUp);

/// <summary>
/// Maps chord-entry quality tokens (the text after the <c>:</c>) to a
/// <see cref="ChordQuality"/>, and each quality to its tone set + display suffix.
/// This single table is the foundation the chord NAME display, the editor's
/// note-expansion completion, and future fret diagrams all build on.
/// </summary>
public static class ChordQualityRegistry
{
    // Tones as (diatonic step above root, semitone above root). P1=0, m3=3, M3=4,
    // d5=6, P5=7, #5=8, M6=9, m7=10, M7=11, M9=14.
    private static readonly Dictionary<ChordQuality, ChordToneSpec[]> Tones = new()
    {
        [ChordQuality.Major] = [new(0, 0), new(2, 4), new(4, 7)],
        [ChordQuality.Minor] = [new(0, 0), new(2, 3), new(4, 7)],
        [ChordQuality.Diminished] = [new(0, 0), new(2, 3), new(4, 6)],
        [ChordQuality.Augmented] = [new(0, 0), new(2, 4), new(4, 8)],
        [ChordQuality.Dominant7] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10)],
        [ChordQuality.Major7] = [new(0, 0), new(2, 4), new(4, 7), new(6, 11)],
        [ChordQuality.Minor7] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10)],
        [ChordQuality.MinorMajor7] = [new(0, 0), new(2, 3), new(4, 7), new(6, 11)],
        [ChordQuality.Diminished7] = [new(0, 0), new(2, 3), new(4, 6), new(6, 9)],
        [ChordQuality.HalfDiminished7] = [new(0, 0), new(2, 3), new(4, 6), new(6, 10)],
        [ChordQuality.Major6] = [new(0, 0), new(2, 4), new(4, 7), new(5, 9)],
        [ChordQuality.Minor6] = [new(0, 0), new(2, 3), new(4, 7), new(5, 9)],
        [ChordQuality.Dominant9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 14)],
        [ChordQuality.Major9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 11), new(8, 14)],
        [ChordQuality.Minor9] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10), new(8, 14)],
        [ChordQuality.Sus2] = [new(0, 0), new(1, 2), new(4, 7)],
        [ChordQuality.Sus4] = [new(0, 0), new(3, 5), new(4, 7)],
        [ChordQuality.Dominant7Sus4] = [new(0, 0), new(3, 5), new(4, 7), new(6, 10)],
        [ChordQuality.Dominant7Flat5] = [new(0, 0), new(2, 4), new(4, 6), new(6, 10)],
        [ChordQuality.Dominant7Sharp5] = [new(0, 0), new(2, 4), new(4, 8), new(6, 10)],
        [ChordQuality.Dominant7Flat9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 13)],
        [ChordQuality.Dominant7Sharp9] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 15)],
        [ChordQuality.Dominant7Sharp11] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(10, 18)],
        [ChordQuality.MajorAdd9] = [new(0, 0), new(2, 4), new(4, 7), new(8, 14)],
        [ChordQuality.Dominant11] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 14), new(10, 17)],
        [ChordQuality.Dominant13] = [new(0, 0), new(2, 4), new(4, 7), new(6, 10), new(8, 14), new(12, 21)],
        [ChordQuality.Minor11] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10), new(8, 14), new(10, 17)],
        [ChordQuality.Minor13] = [new(0, 0), new(2, 3), new(4, 7), new(6, 10), new(8, 14), new(12, 21)],
        [ChordQuality.Major13] = [new(0, 0), new(2, 4), new(4, 7), new(6, 11), new(8, 14), new(12, 21)],
    };

    private static readonly Dictionary<ChordQuality, string> Suffix = new()
    {
        [ChordQuality.Major] = "",
        [ChordQuality.Minor] = "m",
        [ChordQuality.Diminished] = "dim",
        [ChordQuality.Augmented] = "aug",
        [ChordQuality.Dominant7] = "7",
        [ChordQuality.Major7] = "maj7",
        [ChordQuality.Minor7] = "m7",
        [ChordQuality.MinorMajor7] = "m maj7",
        [ChordQuality.Diminished7] = "dim7",
        [ChordQuality.HalfDiminished7] = "m7♭5",
        [ChordQuality.Major6] = "6",
        [ChordQuality.Minor6] = "m6",
        [ChordQuality.Dominant9] = "9",
        [ChordQuality.Major9] = "maj9",
        [ChordQuality.Minor9] = "m9",
        [ChordQuality.Sus2] = "sus2",
        [ChordQuality.Sus4] = "sus4",
        [ChordQuality.Dominant7Sus4] = "7sus4",
        [ChordQuality.Dominant7Flat5] = "7♭5",
        [ChordQuality.Dominant7Sharp5] = "7♯5",
        [ChordQuality.Dominant7Flat9] = "7♭9",
        [ChordQuality.Dominant7Sharp9] = "7♯9",
        [ChordQuality.Dominant7Sharp11] = "7♯11",
        [ChordQuality.MajorAdd9] = "add9",
        [ChordQuality.Dominant11] = "11",
        [ChordQuality.Dominant13] = "13",
        [ChordQuality.Minor11] = "m11",
        [ChordQuality.Minor13] = "m13",
        [ChordQuality.Major13] = "maj13",
    };

    // Entry tokens (after the ':') that select each quality. Several spellings map
    // to the same quality (m / min, maj7 / maj, m7b5 / m7.5-).
    private static readonly Dictionary<string, ChordQuality> ByToken = new()
    {
        ["m"] = ChordQuality.Minor,
        ["min"] = ChordQuality.Minor,
        ["dim"] = ChordQuality.Diminished,
        ["aug"] = ChordQuality.Augmented,
        ["7"] = ChordQuality.Dominant7,
        ["maj7"] = ChordQuality.Major7,
        ["maj"] = ChordQuality.Major7,
        ["m7"] = ChordQuality.Minor7,
        ["min7"] = ChordQuality.Minor7,
        ["mmaj7"] = ChordQuality.MinorMajor7,
        ["dim7"] = ChordQuality.Diminished7,
        // The half-diminished spelling follows the symbol grammar's alteration
        // rule — EVERY altered tension is '+'/'-' (never '#'/'b', which belong
        // to the root and bass alone, so Bb9 stays unambiguous), and LilyPond's
        // m7.5- went with the ':' entry format (GRAMMAR_AUDIT 8.1).
        ["m7-5"] = ChordQuality.HalfDiminished7,
        // '+' is the jazz spelling of the augmented triad (C+). One canonical
        // DISPLAY ("Caug") for both entries, like min/m.
        ["+"] = ChordQuality.Augmented,
        ["6"] = ChordQuality.Major6,
        ["m6"] = ChordQuality.Minor6,
        ["min6"] = ChordQuality.Minor6,
        ["9"] = ChordQuality.Dominant9,
        ["maj9"] = ChordQuality.Major9,
        ["m9"] = ChordQuality.Minor9,
        ["min9"] = ChordQuality.Minor9,
        ["sus2"] = ChordQuality.Sus2,
        ["sus4"] = ChordQuality.Sus4,
        ["sus"] = ChordQuality.Sus4,
        ["7sus4"] = ChordQuality.Dominant7Sus4,
        // The altered and extended qualities, spelled by the same rule as m7-5 above:
        // the alteration is '+'/'-', because '#'/'b' after a letter are the ROOT's.
        // Before these were registered a chords row printed them from its raw-suffix
        // fallback (so they looked fine and did not play) while '@chord' refused them
        // outright — one quantity, two answers.
        ["7-5"] = ChordQuality.Dominant7Flat5,
        ["7+5"] = ChordQuality.Dominant7Sharp5,
        ["7-9"] = ChordQuality.Dominant7Flat9,
        ["7+9"] = ChordQuality.Dominant7Sharp9,
        ["7+11"] = ChordQuality.Dominant7Sharp11,
        ["add9"] = ChordQuality.MajorAdd9,
        ["11"] = ChordQuality.Dominant11,
        ["13"] = ChordQuality.Dominant13,
        ["m11"] = ChordQuality.Minor11,
        ["min11"] = ChordQuality.Minor11,
        ["m13"] = ChordQuality.Minor13,
        ["min13"] = ChordQuality.Minor13,
        ["maj13"] = ChordQuality.Major13,
    };

    // The quality as LilyPond's \chordmode writes it after the ':' — the spelling the
    // LilyPond twin (lysc ly) hands to LilyPond, so LilyPond realizes and NAMES the chord
    // by its own rules (owner decision 2026-09-08: the twin carries LilyPond's names, not
    // Lily#'s display strings). Every entry realizes to the SAME tone set as Tones above.
    // LILYPOND-REF: ly/chord-modifiers-init.ly:21 chordmodifiers = default-chord-modifier-list
    //   (m, min, dim, aug, maj, sus — scm/chord-entry.scm:251-257 default-chord-modifier-list);
    // LILYPOND-REF: scm/chord-entry.scm:67-80 construct-chord-elements' interpret-additions /
    //   interpret-removals — the '.' additions with their '+' / '-' alterations, and '^' removals.
    // ⚠️ '7sus4' is written as 'sus4.7': the leading number is the stack-thirds count and
    //   'sus' is a modifier word, so the sus form takes its steps as additions. 'm7+' is the
    //   minor-major seventh ('+' raises the added step). 'm13^11' removes the eleventh
    //   ':m13' would keep (see the enum's remark). ':5.9' is the add-9 triad — thirds up to
    //   the fifth, plus the ninth.
    private static readonly Dictionary<ChordQuality, string> LilyPondModifiers = new()
    {
        [ChordQuality.Major] = "",
        [ChordQuality.Minor] = ":m",
        [ChordQuality.Diminished] = ":dim",
        [ChordQuality.Augmented] = ":aug",
        [ChordQuality.Dominant7] = ":7",
        [ChordQuality.Major7] = ":maj7",
        [ChordQuality.Minor7] = ":m7",
        [ChordQuality.MinorMajor7] = ":m7+",
        [ChordQuality.Diminished7] = ":dim7",
        [ChordQuality.HalfDiminished7] = ":m7.5-",
        [ChordQuality.Major6] = ":6",
        [ChordQuality.Minor6] = ":m6",
        [ChordQuality.Dominant9] = ":9",
        [ChordQuality.Major9] = ":maj9",
        [ChordQuality.Minor9] = ":m9",
        [ChordQuality.Sus2] = ":sus2",
        [ChordQuality.Sus4] = ":sus4",
        [ChordQuality.Dominant7Sus4] = ":sus4.7",
        [ChordQuality.Dominant7Flat5] = ":7.5-",
        [ChordQuality.Dominant7Sharp5] = ":7.5+",
        [ChordQuality.Dominant7Flat9] = ":7.9-",
        [ChordQuality.Dominant7Sharp9] = ":7.9+",
        [ChordQuality.Dominant7Sharp11] = ":7.11+",
        [ChordQuality.MajorAdd9] = ":5.9",
        [ChordQuality.Dominant11] = ":11",
        [ChordQuality.Dominant13] = ":13",
        [ChordQuality.Minor11] = ":m11",
        [ChordQuality.Minor13] = ":m13^11",
        [ChordQuality.Major13] = ":maj13",
    };

    // The four qualities LilyPond names with a SYMBOL rather than with digits and words.
    // Every other quality is spelled the same in both vocabularies, because LilyPond spells
    // those with digits too — so this table is short by construction, not by omission.
    // LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 47-59) —
    //   <c e gis> is "+", <c es ges> is whiteCircleMarkup (the degree sign at \fontsize #2),
    //   <c es ges bes> is a superscript U+00F8 and <c es ges beses> is the circle with a
    //   superscript 7. The range is in prose: a camelCase name with a ranged address counts
    //   as naming nothing (Semantics.ChordQualityStyle's remark).
    // ⚠️ THE SIZE AND THE RAISE ARE NOT PORTED — Lily# draws a chord name as one baseline
    // run (ChordNameGlyphRun's remark) — so these are the CHARACTERS LilyPond names the four
    // chords with, set on the baseline at the name's own size. The same four characters
    // RomanSuffix has always used for the same four qualities.
    private static readonly Dictionary<ChordQuality, string> SymbolSuffix = new()
    {
        [ChordQuality.Diminished] = "°",
        [ChordQuality.Augmented] = "+",
        [ChordQuality.HalfDiminished7] = "ø",
        [ChordQuality.Diminished7] = "°7",
        // The major seventh: LilyPond's majorSevenSymbol, which is a DRAWN TRIANGLE and is a
        // separate property from the exception table above — its default is the triangle, so
        // both belong to the same "LilyPond's own picture" word. The character here only
        // CARRIES it; Svg.Layout.ChordNameGlyphRun.TriangleCarrier is where it is drawn, and
        // says why it is never set as text.
        // LILYPOND-REF: ly/engraver-init.ly majorSevenSymbol (line 947) — the ChordNames
        //   property, whiteTriangleMarkup by default;
        // LILYPOND-REF: scm/chord-ignatzek-names.scm name-step (lines 162-177) — the symbol
        //   replaces the number when the step is 7 and its alteration is 0, which is what
        //   makes it the `maj` of every one of these four and of nothing else.
        [ChordQuality.Major7] = "△",
        [ChordQuality.Major9] = "△9",
        [ChordQuality.Major13] = "△13",
        [ChordQuality.MinorMajor7] = "m△",
    };

    /// <summary>The tones (diatonic step + semitone above root) of a quality.</summary>
    public static IReadOnlyList<ChordToneSpec> GetTones(ChordQuality quality) => Tones[quality];

    /// <summary>
    /// How many characters at the head of the quality's suffix stand on the ROOT's baseline
    /// rather than in the superscript.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm:179-209 ignatzek-format-chord-name — the
    ///   assembled list puts <c>root-markup</c> and the PREFIX modifiers on the line before
    ///   <c>(make-super-markup to-be-raised-stuff)</c>, so what the prefixes hold is on the
    ///   root's baseline and everything in the super group is not;
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm prefix-modifier->markup (lines 136-142) —
    ///   the prefix a minor third contributes is the minorChordModifier, i.e. the <c>m</c>;
    /// LILYPOND-REF: ly/chord-modifiers-init.ly ignatzekExceptionMusic (lines 47-59) — the
    ///   <c>+</c> and the whiteCircleMarkup <c>°</c> are plain (and <c>\fontsize #2</c>)
    ///   markups, NOT wrapped in <c>\super</c>, while the half-diminished <c>ø</c> is.
    /// ⚠️ Not literal in ONE respect, and named for it (§7.6 ⒝): LilyPond decides this by
    /// BUILDING the two lists, where Lily#'s quality is a word and this answers how much of
    /// the word belongs to the first list. The three cases are LilyPond's; the "how many
    /// characters" is the shape Lily#'s model forces. Confirmed over every registered
    /// quality (scratch/p372/lpnames, LilyPond 2.26.0).
    /// </para>
    /// <para>
    /// ⚠️ <c>ø</c> is NOT one of them — LilyPond's half-diminished exception is
    /// <c>\super ø</c>, so the symbol is raised like a digit. Measured, not assumed: it
    /// prints at 1.85 on the raised baseline where <c>°</c> prints at 3.30 on the root's.
    /// </para>
    /// <para>
    /// ⚠️ Under <c>words</c> the qualities LilyPond spells with a symbol have no LilyPond
    /// answer at all — it never prints <c>Cdim</c> — so they are raised whole, which is what
    /// LilyPond does with every quality its exception table does NOT name. Lily#-own, and
    /// the only part of this table that is.
    /// </para>
    /// </remarks>
    public static int BaselineSuffixLength(ChordQuality quality, Semantics.ChordQualityStyle style)
    {
        string suffix = GetSuffix(quality, style);
        if (suffix.Length == 0)
            return 0;
        // The exception table's two baseline symbols, whole or followed by a raised digit.
        if (suffix[0] is '°' or '+')
            return 1;
        // The minor modifier — the same leading `m` DropMinorModifier removes, so the two
        // cannot disagree about which character it is.
        return HasMinorThird(quality) && suffix[0] == 'm' ? 1 : 0;
    }

    /// <summary>
    /// True when the quality's THIRD is minor — LilyPond's test for a lowercase root, asked
    /// of the tone set rather than of the spelling so a new quality answers it the day it is
    /// registered.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm chordNameLowercaseMinor (lines 229-232) —
    ///   the condition is <c>(= (ly:pitch-alteration third) FLAT)</c> on the chord's step-3
    ///   pitch, so a diminished chord answers yes (its third is minor) and a <c>sus</c>
    ///   chord, which has no third at all, answers no.
    /// </remarks>
    public static bool HasMinorThird(ChordQuality quality)
    {
        foreach (var t in Tones[quality])
            if (t.DiatonicStep == 2 && ((t.Semitone % 12) + 12) % 12 == 3)
                return true;
        return false;
    }

    /// <summary>The quality as a <c>\chordmode</c> modifier (<c>:m7</c>, <c>:7.5-</c>,
    /// empty for a major triad) — see the table's remark.</summary>
    public static string LilyPondModifier(ChordQuality quality) => LilyPondModifiers[quality];

    /// <summary>
    /// The printed suffix after the root (e.g. "m7", "maj7", "") in
    /// <paramref name="style"/>'s vocabulary.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE STYLE IS A REQUIRED ARGUMENT and there is no one-argument overload: the symbol
    /// is spelled in four places, and one that kept the old call would print the other
    /// vocabulary beside the one the score asked for, in silence (<see cref="Semantics.ChordSpelling"/>).
    /// </remarks>
    public static string GetSuffix(ChordQuality quality, Semantics.ChordQualityStyle style)
        => style == Semantics.ChordQualityStyle.Symbols && SymbolSuffix.TryGetValue(quality, out var s)
            ? s : Suffix[quality];

    /// <summary>
    /// Resolves a quality token (text after the <c>:</c>, e.g. "m7"); returns false
    /// for an unknown token. An empty/absent token is a plain major triad.
    /// </summary>
    public static bool TryResolve(string? token, out ChordQuality quality)
    {
        if (string.IsNullOrEmpty(token))
        {
            quality = ChordQuality.Major;
            return true;
        }
        return ByToken.TryGetValue(token, out quality);
    }

    /// <summary>All recognized quality tokens (for tooling / completion).</summary>
    public static IReadOnlyCollection<string> Tokens => ByToken.Keys;

    // Reverse of Tones: each quality's pitch-class set (semitones-above-root mod 12)
    // -> the quality, for recognizing a chord's notes. Every registered quality has
    // a distinct set, so a match is unambiguous once the root is fixed.
    private static readonly Dictionary<string, ChordQuality> ByPitchClasses = BuildPitchClassIndex();

    private static Dictionary<string, ChordQuality> BuildPitchClassIndex()
    {
        var map = new Dictionary<string, ChordQuality>();
        foreach (var (quality, tones) in Tones)
            map[PitchClassKey(tones.Select(t => t.Semitone))] = quality;
        // Fifthless voicings: the PERFECT 5TH is the most omittable chord tone, so a
        // chord keeps its name without it. <1 3 7> in C = C-E-B names as Cmaj7 (no G);
        // the two-note root+3rd <c e> / <c ees>, whose 3rd still fixes major vs minor,
        // names as C / Cm — a bare @chord means the writer wants a symbol. Each
        // dropped-fifth set is distinct from every full quality, so recognition stays
        // unambiguous (a rootless-5th dyad like <c g> has no 3rd and is left unnamed).
        foreach (var q in new[]
        {
            ChordQuality.Major, ChordQuality.Minor,
            ChordQuality.Major7, ChordQuality.Dominant7,
            ChordQuality.Minor7, ChordQuality.MinorMajor7,
        })
            map.TryAdd(
                PitchClassKey(Tones[q].Where(t => t.Semitone != 7).Select(t => t.Semitone)), q);
        return map;
    }

    private static string PitchClassKey(IEnumerable<int> semitones) =>
        string.Join(",", semitones.Select(s => ((s % 12) + 12) % 12).Distinct().OrderBy(x => x));

    /// <summary>
    /// Recognizes a chord QUALITY from the set of semitone intervals above its root
    /// (each reduced to a pitch class; 0 = the root itself). Returns false when the
    /// interval set matches no registered quality. The root is supplied separately
    /// by the caller (the chord's first member), so C6 and Am7 — which share a
    /// pitch-class set — are told apart by their root.
    /// </summary>
    public static bool TryRecognize(IEnumerable<int> intervalsFromRoot, out ChordQuality quality)
        => ByPitchClasses.TryGetValue(PitchClassKey(intervalsFromRoot), out quality);
}

/// <summary>
/// A fully resolved chord: a root, a quality (hence a tone set), and an optional
/// slash bass. The display name and the spelled note chord are both derived from
/// the structure — so this one model drives the chord-name display, the editor's
/// note-expansion completion, and (later) staff notes and fret diagrams.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/chord-ignatzek-names.scm — root + quality → printed name.
/// Lily# renders the name as PLAIN TEXT (e.g. "Cmaj7"), not LilyPond's
/// superscript/triangle typography — a deliberate Phase-1 simplification.
/// </remarks>
public sealed record ChordStructure(
    int RootStep,            // 0=C, 1=D, … 6=B (diatonic step)
    int RootAlter,           // accidental: -2..+2 (semitone alteration)
    ChordQuality Quality,
    int? BassStep = null,    // slash bass diatonic step (null = no bass)
    int? BassAlter = null,
    // A verbatim quality suffix for a chord whose token is NOT in the registry
    // (e.g. "M7", "7#9"): the interval set is unknown (Tones stays empty, so no
    // note expansion), but the root still yields a Roman degree — so `c:M7`
    // shows "CM7" / "IM7" instead of falling back to the literal name.
    string? RawSuffix = null,
    // True for the /+bass ADDED-bass entry form. LP realizes /bass as an
    // inversion (a chord member dropped to the bottom) and /+bass as an extra
    // note below, but PRINTS the same name for both — so DisplayName ignores
    // this; it records entry intent for a future realization.
    // LILYPOND-REF: scm/chord-entry.scm:91-115 process-inversion — a non-member
    // slash pitch degrades to a plain bass.
    // LILYPOND-REF: scm/chord-entry.scm:46-50 interpret-bass — the /+FOO part
    // always sets a plain bass (never an inversion).
    bool BassIsAdded = false)
{
    // Diatonic-step semitones come from RelativeOctave.StepSemitoneOf (single source).

    /// <summary>Semitone offsets of the chord tones above the root (the pitch set).</summary>
    public ImmutableArray<int> Intervals =>
        RawSuffix != null ? ImmutableArray<int>.Empty
        : [.. ChordQualityRegistry.GetTones(Quality).Select(t => t.Semitone)];

    /// <summary>
    /// The spelled chord tones (letter step, accidental, octave above the root).
    /// Each tone's letter comes from its diatonic degree and its accidental from
    /// the actual semitone, so a minor third spells e♭ (not d♯) and a Bb7 seventh
    /// spells a♭ (not g♯).
    /// </summary>
    public ImmutableArray<ChordTone> Tones
    {
        get
        {
            if (RawSuffix != null)
                return ImmutableArray<ChordTone>.Empty;
            var b = ImmutableArray.CreateBuilder<ChordTone>();
            foreach (var spec in ChordQualityRegistry.GetTones(Quality))
            {
                int absStep = RootStep + spec.DiatonicStep;
                int letterStep = ((absStep % 7) + 7) % 7;
                int octaveUp = absStep / 7;
                // Semitone span from the root LETTER (natural) to the tone LETTER.
                int naturalSpan = Semantics.RelativeOctave.StepSemitoneOf(letterStep)
                                  - Semantics.RelativeOctave.StepSemitoneOf(RootStep) + 12 * octaveUp;
                int alter = spec.Semitone - naturalSpan + RootAlter;
                b.Add(new ChordTone(letterStep, alter, octaveUp));
            }
            return b.ToImmutable();
        }
    }

    /// <summary>
    /// The printed chord symbol, e.g. "C", "Am7", "G7", "B♭maj7", "C/G", spelled the way
    /// <paramref name="spelling"/> asks (<c>layout { chordQualities … minorChords … }</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A METHOD WITH A REQUIRED ARGUMENT, not a property: see
    /// <see cref="ChordQualityRegistry.GetSuffix"/>. <see cref="Semantics.ChordSpelling.Default"/>
    /// is what a caller that must not follow the switch passes — MusicXML, and the editor's
    /// completion, which shows the canonical spelling whatever a score sets.
    /// </para>
    /// <para>
    /// The lowercase arm is LilyPond's, in its own order: the ROOT is lowercased and the
    /// minor modifier — the leading <c>m</c> of the suffix — goes with it, because LilyPond
    /// replaces <c>minorChordModifier</c> with <c>empty-markup</c> in exactly that case. The
    /// BASS keeps its capital (<see cref="Semantics.MinorChords"/>'s remark). A
    /// <see cref="RawSuffix"/> chord is never lowercased: its tone set is unknown, so
    /// whether it has a minor third is unknown too, and guessing from the letters would make
    /// <c>CM7</c> — a MAJOR seventh — read as a minor chord.
    /// </para>
    /// <para>
    /// ⚠️ <c>minorChords lower</c> with <c>chordQualities words</c> spells a half-diminished
    /// <c>c7♭5</c>, which in that convention means what <c>Cm7♭5</c> means — mechanically
    /// right, and readable only to someone who knows the convention. LilyPond never shows
    /// it because ITS exception table is always on: under <c>chordQualities symbols</c> the two
    /// agree again and the chord is <c>cø</c>. Stated rather than special-cased: the reader
    /// chose both switches.
    /// </para>
    /// </remarks>
    public string DisplayName(Semantics.ChordSpelling spelling) => PrintedSymbol(spelling).Text;

    /// <summary>
    /// The printed symbol AND where its superscript begins — the one thing the namer knows
    /// that the printed string alone cannot say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond does not print a chord name on one line: the root, the minor modifier and
    /// the slash bass stand on the baseline and EVERYTHING BETWEEN THEM is raised and
    /// reduced (<c>make-super-markup</c>). Which characters those are is a fact about the
    /// chord, not about the string — <c>Cmaj7</c> raises from the <c>m</c> while
    /// <c>Cm7</c> raises from the <c>7</c>, and no rule over the letters tells the two
    /// apart without knowing the quality. So the namer says it, and every reader of the
    /// symbol carries the pair (<see cref="ChordSymbolText"/>) rather than parsing the
    /// spelling back — the compromise <c>ChordNameGlyphRun</c>'s ⒝ remark names for the
    /// accidentals, not repeated here.
    /// </para>
    /// <para>
    /// MEASURED on LilyPond 2.26.0 (scratch/p372/lpnames): over all 29 registered
    /// qualities, the pieces on the ROOT's baseline are the root, the minor <c>m</c>, the
    /// <c>+</c> and <c>°</c> of the exception table, and the <c>/bass</c>; the digits, the
    /// <c>ø</c>, the <c>sus</c> / <c>add</c> words and the major-seventh symbol are all
    /// raised.
    /// </para>
    /// </remarks>
    public ChordSymbolText PrintedSymbol(Semantics.ChordSpelling spelling)
    {
        bool lower = spelling.LowercaseMinor && RawSuffix == null
                     && ChordQualityRegistry.HasMinorThird(Quality);
        var sb = new StringBuilder();
        sb.Append(SpellPitch(RootStep, RootAlter, lower));

        string suffix = RawSuffix ?? ChordQualityRegistry.GetSuffix(Quality, spelling.Qualities);
        string printed = lower ? DropMinorModifier(suffix) : suffix;
        // How much of the PRINTED suffix stays DOWN with the root. LilyPond's minor modifier
        // is a prefix on the baseline, and the exception table's `+` and `°` are drawn there
        // too (the circle at its own larger size).
        // ⚠️ A LOWERCASED ROOT REMOVES THE MODIFIER, NOT THE BASELINE. The `m` that went is
        // one of those baseline characters, so the count comes down by exactly the one that
        // was dropped — it does not go to zero. Setting it to zero put `c°`'s degree sign in
        // the SUPERSCRIPT, drawn 0.707× and lifted, where LilyPond keeps it on the root's
        // line at fontsize +2: LilyPond reaches the exception table BEFORE it asks about the
        // case (scm/chord-ignatzek-names.scm:245-246 — the `if exception` arm takes
        // lowercase-root? as an argument and formats root + exception + bass, with no super
        // at all), so the case cannot move it.
        int down = RawSuffix != null
            ? 0
            : ChordQualityRegistry.BaselineSuffixLength(Quality, spelling.Qualities)
              - (printed.Length == suffix.Length ? 0 : 1);
        sb.Append(printed);

        // The superscript runs from the end of that baseline part to the slash (or the end).
        // A symbol with nothing raised — a plain triad, a bare `Cm`, a `C°` — says so with
        // NoSuperscript, so the renderer's ordinary one-line path is the one it takes.
        int rootLength = sb.Length - printed.Length;
        int superFrom = down >= printed.Length
            ? ChordSymbolText.NoSuperscript
            : rootLength + down;

        if (BassStep is int bs)
        {
            sb.Append('/');
            // Never lowercased: LilyPond's chordNoteNamer is called with lowercase? = #f.
            sb.Append(SpellPitch(bs, BassAlter ?? 0));
        }
        return new ChordSymbolText(sb.ToString(), superFrom);
    }

    /// <summary>
    /// The suffix with its leading minor modifier removed — the <c>m</c> the lowercase root
    /// replaces, and the space that followed it in <c>m maj7</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm prefix-modifier->markup (lines 136-142) — with a
    ///   lowercased root the minorChordModifier is replaced by <c>empty-markup</c>, which is
    ///   this removal: LilyPond drops the modifier rather than printing it small or moving
    ///   it, and it drops exactly the one the lowercase stands for.
    /// Only called for a quality that HAS a minor third, so the leading <c>m</c> it finds is
    /// that third's modifier and never the head of another word: <c>dim</c> and <c>dim7</c>
    /// are the two minor-third suffixes that do not start with one (their third is spelled
    /// inside the word), and they come through untouched. <c>EveryMinorThirdQuality_…</c> in
    /// the tests asks that of the whole registry rather than trusting the list here.
    /// </remarks>
    private static string DropMinorModifier(string suffix)
        => suffix.StartsWith("m", System.StringComparison.Ordinal)
            ? suffix.Substring(1).TrimStart(' ')
            : suffix;

    /// <summary>
    /// Recognizes a chord from its ROOT (first member) and the pitch classes of all
    /// its members. Returns false when the members match no registered quality. Used
    /// by the auto <c>@chord</c> annotation — the root is the chord's first note, so
    /// C6 and Am7 (same pitch-class set) are told apart by it.
    /// </summary>
    public static bool TryRecognize(int rootStep, int rootAlter,
        IEnumerable<int> memberPitchClasses, out ChordStructure? structure)
    {
        structure = null;
        int rootPc = Mod12(Semantics.RelativeOctave.StepSemitoneOf(rootStep) + rootAlter);
        var intervals = memberPitchClasses.Select(pc => Mod12(pc - rootPc));
        if (!ChordQualityRegistry.TryRecognize(intervals, out var quality))
            return false;
        structure = new ChordStructure(rootStep, rootAlter, quality);
        return true;
    }

    /// <summary>
    /// Recognizes a chord from its SOUNDING members without privileging the one that
    /// happens to be written first: every member is tried as the root, and the first
    /// that names a registered quality wins. When that root is not the lowest note the
    /// chord is an inversion, and the lowest note becomes the printed slash bass —
    /// E-G-C sounding in that order is <c>C/E</c>.
    /// </summary>
    /// <param name="membersLowestFirst">
    /// The chord's members as (diatonic step, alteration), ordered by sounding pitch,
    /// lowest first. Order is the whole point: see the remark.
    /// </param>
    /// <remarks>
    /// ⚠️ THE BASS IS TRIED FIRST, and that is what keeps this from renaming chords that
    /// already work. Several sets read as more than one chord — {C,E,G,A} is C6 from C
    /// and Am7 from A, and a diminished seventh reads from all four of its notes — so
    /// without a preference the answer would depend on iteration order. Preferring the
    /// bass means a root-position chord keeps the name it has today (C6 stays C6, not
    /// Am7/C) and an inversion is only ever consulted when the bass names nothing at all.
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm — the bass note is the inversion's
    /// printed bass, and the root is found from the interval set.
    /// </remarks>
    public static bool TryRecognize(
        IReadOnlyList<(int Step, int Alter)> membersLowestFirst, out ChordStructure? structure)
    {
        structure = null;
        if (membersLowestFirst.Count == 0)
            return false;

        var pcs = new int[membersLowestFirst.Count];
        for (int i = 0; i < membersLowestFirst.Count; i++)
        {
            var (s, a) = membersLowestFirst[i];
            pcs[i] = Mod12(Semantics.RelativeOctave.StepSemitoneOf(s) + a);
        }

        for (int i = 0; i < membersLowestFirst.Count; i++)
        {
            int rootPc = pcs[i];
            if (!ChordQualityRegistry.TryRecognize(pcs.Select(pc => Mod12(pc - rootPc)), out var quality))
                continue;
            var (rootStep, rootAlter) = membersLowestFirst[i];
            var (bassStep, bassAlter) = membersLowestFirst[0];
            structure = rootPc == pcs[0]
                ? new ChordStructure(rootStep, rootAlter, quality)
                : new ChordStructure(rootStep, rootAlter, quality, bassStep, bassAlter);
            return true;
        }
        return false;
    }

    private static int Mod12(int a) => ((a % 12) + 12) % 12;

    /// <summary>
    /// This chord as a Roman-numeral scale degree in the given key, jazz style: an
    /// UPPERCASE numeral for the root's scale degree plus the SAME quality suffix as
    /// the printed name (e.g. <c>Imaj7</c>, <c>IIm7</c>, <c>V7</c>, <c>VIm</c>). A
    /// chromatic root gets a ♯/♭ prefix (<c>♭III</c>, <c>♯IV</c>); a slash bass shows
    /// as its own degree (<c>V7/VII</c>).
    /// </summary>
    /// <param name="tonicStep">The key tonic's diatonic step (0=C .. 6=B) — the actual
    /// tonic, so a minor key is measured from its own tonic (A minor: Am = I).</param>
    /// <param name="keySharps">The signature: +sharps / -flats.</param>
    public string ToRomanNumeral(int tonicStep, int keySharps)
    {
        static string Degree(int step, int alter, int tonicStep, int keySharps)
        {
            int degree = ((step - tonicStep) % 7 + 7) % 7;
            string numeral = new[] { "I", "II", "III", "IV", "V", "VI", "VII" }[degree];
            // Accidental = how far the root sits from the scale's own note on that
            // letter (0 = diatonic, +/- = chromatic).
            int acc = alter - KeySpelling.Alteration(step, keySharps);
            string prefix = acc > 0 ? new string('♯', acc)    // ♯
                : acc < 0 ? new string('♭', -acc)             // ♭
                : "";
            return prefix + numeral;
        }

        var sb = new StringBuilder(Degree(RootStep, RootAlter, tonicStep, keySharps));
        sb.Append(RawSuffix ?? RomanSuffix(Quality));
        if (BassStep is int bs)
            sb.Append('/').Append(Degree(bs, BassAlter ?? 0, tonicStep, keySharps));
        return sb.ToString();
    }

    /// <summary>The quality suffix in Roman-numeral style: the triad symbols
    /// diminished ° / augmented + / half-diminished ø are more idiomatic there than
    /// the name-style "dim"/"aug"/"m7♭5"; every other quality keeps the printed
    /// suffix (m, maj7, m7, 7, …), so IIm7 / V7 / Imaj7 read as expected.</summary>
    /// <remarks>
    /// ⚠️ A ROMAN DEGREE DOES NOT FOLLOW <c>layout { chordQualities }</c>, and the reason is an
    /// identity rather than a preference: this table already spells the four qualities that
    /// vocabulary moves, and it overrides them — so asking for
    /// <see cref="Semantics.ChordQualityStyle.Symbols"/> below would change nothing at all.
    /// Passing <see cref="Semantics.ChordQualityStyle.Words"/> says which of the two equal
    /// answers is meant. <c>TheRomanDegreeIsTheSameInBothVocabularies</c> holds it.
    /// The CASE is fixed for a different reason: a numeral is not a note name, so
    /// <c>minorChords lower</c> has nothing to lowercase here.
    /// </remarks>
    private static string RomanSuffix(ChordQuality quality) => quality switch
    {
        ChordQuality.Diminished => "°",       // °
        ChordQuality.Diminished7 => "°7",     // °7
        ChordQuality.HalfDiminished7 => "ø7",  // ø7
        ChordQuality.Augmented => "+",
        _ => ChordQualityRegistry.GetSuffix(quality, Semantics.ChordQualityStyle.Words),
    };

    /// <summary>The seven numerals, LONGEST FIRST so a prefix never wins over the word
    /// that contains it.</summary>
    /// <remarks>
    /// ⚠️ The order is the whole correctness of <see cref="TryParseRomanEntry"/>. Written
    /// ascending, "V" matches the head of "VI" and "I" the head of "II"/"III"/"IV", so
    /// every compound numeral would parse as its first letter with the remainder falling
    /// into the quality — <c>IV</c> would read as <c>I</c> with quality "V". The same trap
    /// the duration alternation hit in the editor grammar (HANDOFF §1, session 239 ③).
    /// </remarks>
    private static readonly string[] RomanNumeralsLongestFirst =
        { "VII", "III", "VI", "IV", "II", "V", "I" };

    private static readonly string[] RomanNumeralsInOrder =
        { "I", "II", "III", "IV", "V", "VI", "VII" };

    /// <summary>
    /// Reads a chord written as a ROMAN DEGREE of the key — <c>Imaj7</c>, <c>V7</c>,
    /// <c>IIm7</c>, <c>bVII</c>, <c>#IVm7-5</c>, <c>V7/VII</c> — into the same
    /// <see cref="ChordStructure"/> an absolute symbol produces, resolved against the key
    /// in force where it is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The EXACT inverse of <see cref="ToRomanNumeral"/>, and deliberately next to it: the
    /// degree vocabulary — which numeral, which accidental, which quality spelling — is one
    /// thing written twice, so the two have to be read together whenever either changes.
    /// Round trip: a structure printed by <c>ToRomanNumeral</c> and read back here against
    /// the SAME key is that structure again.
    /// </para>
    /// <para>
    /// The accidental is what separates the written degree from the key's own note on that
    /// letter, exactly as <c>ToRomanNumeral</c> emits it: <c>bIII</c> in C major is E♭
    /// because E is natural there, and the same <c>bIII</c> in E♭ major is G♭.
    /// </para>
    /// <para>
    /// ⚠️ THE PRINTED GLYPHS DO NOT ROUND-TRIP THROUGH THE LEXER, and this method's
    /// tolerance of them does not change that. ♭ ♯ ° ø are read here so the string parser
    /// is total, but MEASURED: the lexer refuses each of them outright ("Unexpected
    /// character '♭'"), so they never reach a chord entry and a symbol cannot be pasted
    /// back into the source from the score it came out of. The typeable spellings are the
    /// ASCII ones — <c>bVII</c>, <c>#IV</c>, <c>VIIdim</c>, <c>IIm7-5</c> — and those are
    /// what the grammar documents. Admitting the glyphs is a lexer change, not this one.
    /// </para>
    /// <para>
    /// The quality is read by the printed roman spellings first (° ø7 +, which
    /// <see cref="RomanSuffix"/> emits and which <c>ChordQualityRegistry</c> does not
    /// know), then by the ordinary registry — so <c>VIIdim</c> resolves, <c>+</c> resolves
    /// (it lexes, unlike the other three), and <c>Imaj7</c> / <c>V7</c> / <c>IIm7</c> need
    /// no special case at all.
    /// </para>
    /// </remarks>
    public static bool TryParseRomanEntry(string s, int tonicStep, int keySharps,
        out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(s))
            return false;

        int slash = s.IndexOf('/');
        string main = slash >= 0 ? s.Substring(0, slash) : s;
        string? bass = slash >= 0 ? s.Substring(slash + 1) : null;

        if (!TryParseDegree(main, tonicStep, keySharps, out int step, out int alter, out string qualStr))
            return false;
        if (!TryResolveRomanQuality(qualStr, out var quality))
            return false;

        int? bassStep = null, bassAlter = null;
        if (bass != null)
        {
            if (!TryParseDegree(bass, tonicStep, keySharps, out int bs, out int ba, out string rest)
                || rest.Length != 0)
                return false;
            bassStep = bs;
            bassAlter = ba;
        }

        result = new ChordStructure(step, alter, quality, bassStep, bassAlter);
        return true;
    }

    /// <summary>One roman degree: optional accidental prefix, a numeral, and whatever
    /// follows it (the quality for a root, which must be empty for a bass).</summary>
    private static bool TryParseDegree(string s, int tonicStep, int keySharps,
        out int step, out int alter, out string rest)
    {
        step = 0;
        alter = 0;
        rest = "";
        if (string.IsNullOrEmpty(s))
            return false;

        int i = 0, acc = 0;
        while (i < s.Length && (s[i] == '#' || s[i] == '♯' || s[i] == 'b' || s[i] == '♭'))
        {
            acc += s[i] == '#' || s[i] == '♯' ? 1 : -1;
            i++;
        }

        string tail = s.Substring(i);
        string? numeral = null;
        foreach (var n in RomanNumeralsLongestFirst)
            if (tail.StartsWith(n, System.StringComparison.Ordinal))
            {
                numeral = n;
                break;
            }
        if (numeral == null)
            return false;

        int degree = System.Array.IndexOf(RomanNumeralsInOrder, numeral);
        step = (tonicStep + degree) % 7;
        // The inverse of ToRomanNumeral's `acc = alter - KeySpelling.Alteration(step, sharps)`.
        alter = KeySpelling.Alteration(step, keySharps) + acc;
        rest = tail.Substring(numeral.Length);
        return true;
    }

    /// <summary>The quality of a roman entry: the printed roman-only spellings first,
    /// then the ordinary name-style registry.</summary>
    private static bool TryResolveRomanQuality(string suffix, out ChordQuality quality)
    {
        switch (suffix)
        {
            case "°": quality = ChordQuality.Diminished; return true;      // °
            case "°7": quality = ChordQuality.Diminished7; return true;    // °7
            case "ø7": quality = ChordQuality.HalfDiminished7; return true; // ø7
            case "+": quality = ChordQuality.Augmented; return true;
            default: return ChordQualityRegistry.TryResolve(suffix, out quality);
        }
    }

    /// <summary>
    /// The chord as a Lily# note chord, e.g. "&lt;c e g b&gt;" (Cmaj7),
    /// "&lt;c ees g&gt;" (Cm). The notes are bare (no octave marks): in relative
    /// mode each successive tone resolves to the nearest pitch above, voicing the
    /// chord ascending from the root. A slash bass is prepended below the root.
    /// </summary>
    public string ToNoteChord()
    {
        var notes = new List<string>();
        if (BassStep is int bs)
            notes.Add(SpellLilyPitch(bs, BassAlter ?? 0) + ","); // bass an octave down
        foreach (var t in Tones)
            notes.Add(SpellLilyPitch(t.Step, t.Alter));
        return "<" + string.Join(" ", notes) + ">";
    }

    /// <summary>
    /// The chord as a LilyPond <c>\chordmode</c> entry — Dutch root, the written
    /// <paramref name="duration"/>, the quality modifier, the slash bass:
    /// <c>fis4:m7.5-/cis</c>. LilyPond then realizes the tone set and prints its OWN name for
    /// it (Ignatzek), which is what the twin is for. A <see cref="RawSuffix"/> chord has no
    /// tone set to hand over: the root (and bass) go out alone, and the caller says so.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3848-3856 new_chord — steno_tonic_pitch,
    ///   optional_notemode_duration, chord_separator, chord_items: the duration stands
    ///   BETWEEN the root and the ':' (<c>e1:maj7/dis</c>), the bass is a chord_separator.
    /// LILYPOND-REF: scm/chord-entry.scm:46-50 construct-chord-elements' interpret-bass — '/+'
    ///   is the added bass, '/' the inversion-or-bass that <see cref="BassIsAdded"/> records.
    /// </remarks>
    public string ToChordMode(string duration)
    {
        var sb = new StringBuilder();
        sb.Append(SpellLilyPitch(RootStep, RootAlter)).Append(duration);
        if (RawSuffix == null)
            sb.Append(ChordQualityRegistry.LilyPondModifier(Quality));
        if (BassStep is int bs)
            sb.Append(BassIsAdded ? "/+" : "/").Append(SpellLilyPitch(bs, BassAlter ?? 0));
        return sb.ToString();
    }

    /// <summary>
    /// Parses a chord symbol written as a single word (root letter + Dutch
    /// accidental + quality token), e.g. "cmaj7", "am", "g7", "besm7". Requires a
    /// recognized non-empty quality so plain note letters do not parse as chords.
    /// Used by the editor's note-expansion completion.
    /// </summary>
    public static bool TryParseSymbol(string word, out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(word))
            return false;

        int step = "cdefgab".IndexOf(char.ToLower(word[0]));
        if (step < 0)
            return false;

        string rest = word.Substring(1);
        int alter = 0;
        foreach (var (suffix, a) in new[] { ("isis", 2), ("eses", -2), ("is", 1), ("es", -1) })
        {
            if (rest.StartsWith(suffix, System.StringComparison.Ordinal))
            {
                alter = a;
                rest = rest.Substring(suffix.Length);
                break;
            }
        }

        // Require an explicit, recognized quality (so "c" or "ees" alone do not
        // expand — only "cm", "g7", "besmaj7" do).
        if (rest.Length == 0 || !ChordQualityRegistry.TryResolve(rest, out var quality))
            return false;

        result = new ChordStructure(step, alter, quality);
        return true;
    }

    /// <summary>
    /// Parses a <c>chords { }</c> chord entry — the ONE chord format Lily# accepts:
    /// the SYMBOL as it prints (GRAMMAR_AUDIT 8.1, decided 2026-08-21). An
    /// UPPERCASE root <c>A</c>–<c>G</c>, an optional <c>#</c>/<c>b</c> accidental,
    /// a bare quality (empty = major), and an optional <c>/</c> slash bass spelled
    /// the same way. So "C", "Am", "G7", "F#m", "Bb7", "Cmaj7/E" parse; the
    /// retired <c>:</c> entry form ("a:m", "g:7") and an unknown quality ("C7-9")
    /// do not — an unknown quality still DISPLAYS verbatim in a chords row (the
    /// collector's raw-suffix fallback), this strict parse is what @chord and the
    /// completion build on. Shared by the <c>chords { }</c> block and @chord
    /// annotations, so the two stay in one format.
    /// </summary>
    /// <remarks>
    /// The <c>/+</c> added-bass entry went with the <c>:</c> format: '+' now
    /// spells altered tensions and the augmented triad inside the quality, and
    /// LilyPond PRINTS /bass and /+bass identically anyway (the flag recorded
    /// entry intent only — <see cref="BassIsAdded"/> stays on the model for the
    /// MusicXML importer's sake, but no Lily# spelling sets it any more).
    /// </remarks>
    public static bool TryParseChordEntry(string s, out ChordStructure result)
    {
        result = default!;
        if (string.IsNullOrEmpty(s))
            return false;

        int slash = s.IndexOf('/');
        string main = slash >= 0 ? s.Substring(0, slash) : s;
        string? bass = slash >= 0 ? s.Substring(slash + 1) : null;

        if (!TryParseSymbolPitch(main, out int step, out int alter, out string qualStr))
            return false;
        if (!ChordQualityRegistry.TryResolve(qualStr, out var quality))
            return false;

        int? bassStep = null, bassAlter = null;
        if (bass != null)
        {
            if (!TryParseSymbolPitch(bass, out int bs, out int ba, out string rest)
                || rest.Length != 0)
                return false;
            bassStep = bs;
            bassAlter = ba;
        }

        result = new ChordStructure(step, alter, quality, bassStep, bassAlter);
        return true;
    }

    /// <summary>A symbol-format pitch: uppercase letter + optional '#'/'b',
    /// returning whatever follows (the quality for a root, which must be empty
    /// for a bass). Lowercase letters do not parse — 'b' is an accidental here,
    /// so the case IS the grammar (GRAMMAR_AUDIT 8.1: "Bb5" is B♭'s power chord
    /// precisely because a flat can only follow a root).</summary>
    public static bool TryParseSymbolPitch(string s, out int step, out int alter, out string rest)
    {
        step = -1; alter = 0; rest = "";
        if (string.IsNullOrEmpty(s))
            return false;
        step = "CDEFGAB".IndexOf(s[0]); // display order == the model's 0=C..6=B
        if (step < 0)
            return false;
        int i = 1;
        if (s.Length > 1 && (s[1] == '#' || s[1] == 'b'))
        {
            alter = s[1] == '#' ? 1 : -1;
            i = 2;
        }
        rest = s.Substring(i);
        return true;
    }

    /// <summary>Spells a diatonic step + alteration as a note name with a Unicode
    /// accidental (e.g. 0/+1 → "C♯", 6/-1 → "B♭"). Shared by the chord-name fallback.</summary>
    /// <param name="step">The diatonic step, 0=C..6=B.</param>
    /// <param name="alter">The alteration in half steps, −2..+2.</param>
    /// <param name="lowercase">LilyPond's <c>lowercase?</c> — a minor chord's ROOT under
    /// <c>minorChords lower</c>. Defaults to false, which is what every other caller wants:
    /// a slash bass is named with <c>#f</c> in LilyPond too, and the Roman degrees and the
    /// ledger's spellings are not note names at all.</param>
    /// <remarks>
    /// LILYPOND-REF: scm/chord-name.scm:28-32 conditional-string-capitalize — the helper
    ///   every root namer ends in: the name is capitalized UNLESS the flag holds, which is
    ///   why "lowercase" is a flag on the spelling here rather than a second spelling of it.
    ///   Its callers are the namers at :171, :216 and :241 — the flag is the same one
    ///   chordRootNamer is handed and chordNoteNamer never is.
    /// </remarks>
    public static string SpellPitch(int step, int alter, bool lowercase = false)
    {
        char letter = (lowercase ? "cdefgab" : "CDEFGAB")[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            -2 => "♭♭",
            -1 => "♭",
            0 => "",
            1 => "♯",
            2 => "♯♯",
            _ => "",
        };
        return letter + acc;
    }

    // A Lily# (Dutch) pitch token: letter + is/isis/es/eses accidental.
    private static string SpellLilyPitch(int step, int alter)
    {
        char letter = "cdefgab"[((step % 7) + 7) % 7];
        string acc = alter switch
        {
            2 => "isis",
            1 => "is",
            0 => "",
            -1 => "es",
            -2 => "eses",
            _ => "",
        };
        return letter + acc;
    }
}
