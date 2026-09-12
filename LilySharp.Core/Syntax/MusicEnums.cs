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

namespace LilySharp.Core.Syntax;

/// <summary>
/// Types of articulation marks.
/// </summary>
public enum ArticulationType
{
    /// <summary>No articulation.</summary>
    None,
    // Articulations
    /// <summary>Staccato — shortened, detached note (<c>.</c>).</summary>
    Staccato,
    /// <summary>Accent — emphasized note (<c>&gt;</c>).</summary>
    Accent,
    /// <summary>Tenuto — note held for its full value (<c>-</c>).</summary>
    Tenuto,
    /// <summary>Marcato — strong marked accent (<c>^</c>).</summary>
    Marcato,
    /// <summary>Fermata — hold/pause on the note.</summary>
    Fermata,
    /// <summary>Angled short fermata (LP <c>\shortfermata</c>).</summary>
    FermataShort,
    /// <summary>Square long fermata (LP <c>\longfermata</c>).</summary>
    FermataLong,
    /// <summary>Guitar bend-up with a semitone amount (<c>@bend(full)</c>).</summary>
    Bend,
    /// <summary>Hammer-on — TAB "H" (<c>@hammeron</c>).</summary>
    HammerOn,
    /// <summary>Pull-off — TAB "P" (<c>@pulloff</c>).</summary>
    PullOff,
    /// <summary>Tap — TAB "T" (<c>@tap</c>).</summary>
    Tap,
    /// <summary>Bartók (snap) pizzicato (<c>@snappizz</c>).</summary>
    SnapPizz,
    /// <summary>Fret chord diagram (<c>@frame(x32010)</c>).</summary>
    FretFrame,
    /// <summary>Stopped note — "+" above (closed hi-hat, stopped horn).</summary>
    Stopped,
    /// <summary>Cello thumb position (<c>@thumb</c>).</summary>
    Thumb,
    /// <summary>Organ heel (<c>@heel</c>).</summary>
    Heel,
    /// <summary>Organ toe (<c>@toe</c>).</summary>
    Toe,
    /// <summary>p-i-m-a right-hand fingering (<c>@pluck(p)</c>).</summary>
    Pluck,
    /// <summary>Scoop — approach curve rising INTO the note (<c>@scoop</c>).</summary>
    Scoop,
    /// <summary>Plop — approach curve falling INTO the note (<c>@plop</c>).</summary>
    Plop,
    /// <summary>Portato — semi-detached note (slurred staccato).</summary>
    Portato,
    /// <summary>Wedge-shaped extreme staccato (@staccatissimo).
    /// LILYPOND-REF: mf/feta-scripts.mf scripts.u/dstaccatissimo.</summary>
    Staccatissimo,
    // String bowing marks (always above the staff, like LP's defaults).
    // LILYPOND-REF: scm/script.scm "upbow"/"downbow"; mf/feta-scripts.mf.
    /// <summary>Up-bow — string bowing mark, above the staff.</summary>
    UpBow,
    /// <summary>Down-bow — string bowing mark, above the staff.</summary>
    DownBow,
    /// <summary>Harmonic circle (@flageolet).
    /// LILYPOND-REF: scripts.flageolet.</summary>
    Flageolet,
    // Bend-after gestures: a short curved line trailing off the note — a jazz
    // "fall" (drops away) or "doit" (rises away). LilyPond's \bendAfter#-N / #+N.
    /// <summary>Jazz "fall" — a short curved line dropping away from the note (LP <c>\bendAfter#-N</c>).</summary>
    Fall,
    /// <summary>Jazz "doit" — a short curved line rising away from the note (LP <c>\bendAfter#+N</c>).</summary>
    Doit,
    // Ornaments
    /// <summary>Trill ornament.</summary>
    Trill,
    /// <summary>Mordent (lower mordent) ornament.</summary>
    Mordent,
    /// <summary>Prall (upper mordent) ornament.</summary>
    Prall,
    /// <summary>Turn ornament.</summary>
    Turn,
    /// <summary>Inverted turn ornament.</summary>
    InvertedTurn,
    /// <summary>Prall-triller — trill combined with a prall ornament.</summary>
    PrallTriller,
    // Breathing signs — NOT Script grobs: a BreathingSign sits at the TOP of the
    // staff, to the RIGHT of the note it follows (in the gap before the next
    // note), independent of note height and stem. Routed through the articulation
    // anchor/render pipeline for reuse but positioned independently.
    // LILYPOND-REF: lily/breathing-sign.cc; \breathe = scripts.rcomma, \caesura
    // = scripts.caesura.straight (ly/music-functions-init.ly, ly/gregorian.ly).
    /// <summary>Breath mark — comma at the top of the staff after the note (<c>\breathe</c>).</summary>
    Breath,
    /// <summary>Caesura ("railroad tracks") — a break in the sound (<c>\caesura</c>).</summary>
    Caesura,
    // Editorial (suggestion) accidentals — a small accidental ABOVE the note,
    // created from @editorial; the kind comes from the note's resolved
    // accidental. LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion
    /// <summary>Editorial suggestion sharp — small accidental above the note (<c>@editorial</c>).</summary>
    EditorialSharp,
    /// <summary>Editorial suggestion flat — small accidental above the note (<c>@editorial</c>).</summary>
    EditorialFlat,
    /// <summary>Editorial suggestion natural — small accidental above the note (<c>@editorial</c>).</summary>
    EditorialNatural,
    /// <summary>Editorial suggestion double-sharp — small accidental above the note (<c>@editorial</c>).</summary>
    EditorialDoubleSharp,
    /// <summary>Editorial suggestion double-flat — small accidental above the note (<c>@editorial</c>).</summary>
    EditorialDoubleFlat
}

/// <summary>
/// Dynamic levels from pianississimo to fortississimo.
/// </summary>
public enum DynamicLevel
{
    /// <summary>No dynamic marking.</summary>
    None,
    /// <summary>Five-p pianissississimo, <c>\ppppp</c> (softest).</summary>
    PPPPP = 6,
    /// <summary>pianississimo, <c>\pppp</c>.</summary>
    PPPP = 12,
    /// <summary>pianississimo, <c>\ppp</c>.</summary>
    PPP = 20,
    /// <summary>pianissimo (very soft), <c>\pp</c>.</summary>
    PP = 35,
    /// <summary>piano (soft), <c>\p</c>.</summary>
    P = 50,
    /// <summary>mezzo-piano (moderately soft), <c>\mp</c>.</summary>
    MP = 65,
    /// <summary>mezzo-forte (moderately loud), <c>\mf</c>.</summary>
    MF = 80,
    /// <summary>fp — loud attack then soft; a single MIDI note gets the attack level.</summary>
    FP = 90,
    /// <summary>forte (loud), <c>\f</c>.</summary>
    F = 95,
    /// <summary>sf — subito forte accent on one note.</summary>
    SF = 105,
    /// <summary>fortissimo (very loud), <c>\ff</c>.</summary>
    FF = 110,
    /// <summary>rf — rinforzando, gentler than the sforzato family.</summary>
    RF = 111,
    /// <summary>sfz / rfz / fz — sforzato family, a hair under fff.</summary>
    SFZ = 112,
    /// <summary>rfz — rinforzando member of the sforzato family.</summary>
    RFZ = 113,
    /// <summary>fz — forzando member of the sforzato family.</summary>
    FZ = 114,
    /// <summary>sffz — the heaviest sforzato.</summary>
    SFFZ = 116,
    // fff moved off the 127 ceiling to give ffff/fffff their own headroom.
    /// <summary>fortississimo (extremely loud), <c>\fff</c>.</summary>
    FFF = 120,
    /// <summary>Four-f fortissississimo, <c>\ffff</c>.</summary>
    FFFF = 124,
    /// <summary>Five-f fortissississimo, <c>\fffff</c> (loudest).</summary>
    FFFFF = 127
}

/// <summary>
/// Predefined instrument tunings for tablature — one member per distinct SET OF STRINGS
/// in LilyPond's <c>ly/string-tunings-init.ly</c>, never one per spelling.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/string-tunings-init.ly — the thirty <c>\makeDefaultStringTuning</c>
/// symbols. Three pairs of those symbols are the SAME four or six strings, so they share a
/// member here and the member is named for the instrument LilyPond lists first:
/// <c>bass-four-string-tuning</c> and <c>double-bass-tuning</c> are <see cref="Bass"/>, and
/// <c>mandolin-tuning</c> is <see cref="Violin"/> (both g d' a' e''). <see cref="Tablature.Tunings"/>
/// holds the strings, the Lily# spellings and the LilyPond symbol each member writes back.
/// <para>
/// ⚠️ A sixth member, <c>Custom</c>, stood here until 2026-09-13 and NOTHING read it — no
/// switch arm, no parser, no exporter, not one test. It advertised a feature the language
/// does not have (there is no way to write a tuning of your own), which is the same kind of
/// lie <c>splashhihat</c> told in the drum table: a name whose existence is the only thing
/// it does. Removed on the pre-release rule.
/// </para>
/// </remarks>
public enum TuningType
{
    /// <summary>Standard guitar tuning: E A D G B E</summary>
    Guitar,
    /// <summary>7-string guitar: B E A D G B E</summary>
    Guitar7,
    /// <summary>Guitar, dropped D: D A D G B E</summary>
    GuitarDropD,
    /// <summary>Guitar, dropped C: C G C F A D</summary>
    GuitarDropC,
    /// <summary>Guitar, open G: D G D G B D</summary>
    GuitarOpenG,
    /// <summary>Guitar, open D: D A D F♯ A D</summary>
    GuitarOpenD,
    /// <summary>Guitar, DADGAD: D A D G A D</summary>
    GuitarDadgad,
    /// <summary>Guitar, lute tuning: E A D F♯ B E</summary>
    GuitarLute,
    /// <summary>Guitar, Asus4: E A D E A E</summary>
    GuitarAsus4,
    /// <summary>4-string bass tuning: E A D G — also the double bass.</summary>
    Bass,
    /// <summary>4-string bass, dropped D: D A D G</summary>
    BassDropD,
    /// <summary>5-string bass tuning: B E A D G</summary>
    Bass5,
    /// <summary>6-string bass tuning: B E A D G C</summary>
    Bass6,
    /// <summary>Violin: G D A E — also the mandolin.</summary>
    Violin,
    /// <summary>Viola: C G D A</summary>
    Viola,
    /// <summary>Cello: C G D A, an octave below the viola.</summary>
    Cello,
    /// <summary>5-string banjo, open G: g D G B D (the g is the high drone string).</summary>
    BanjoOpenG,
    /// <summary>5-string banjo, C tuning: g C G B D</summary>
    BanjoC,
    /// <summary>5-string banjo, modal (sawmill): g D G C D</summary>
    BanjoModal,
    /// <summary>5-string banjo, open D: a D F♯ A D</summary>
    BanjoOpenD,
    /// <summary>5-string banjo, open Dm: a D F A D</summary>
    BanjoOpenDm,
    /// <summary>5-string banjo, double C: g C G C D</summary>
    BanjoDoubleC,
    /// <summary>5-string banjo, double D: a D G D E</summary>
    BanjoDoubleD,
    /// <summary>Ukulele tuning: G C E A (re-entrant — the G is the HIGHEST string).</summary>
    Ukulele,
    /// <summary>Ukulele in D: A D F♯ B (re-entrant).</summary>
    UkuleleD,
    /// <summary>Tenor ukulele: G C E A, with the G an octave down (not re-entrant).</summary>
    TenorUkulele,
    /// <summary>Baritone ukulele: D G B E</summary>
    BaritoneUkulele,
}
