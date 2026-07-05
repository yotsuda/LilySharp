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
    None,
    // Articulations
    Staccato,
    Accent,
    Tenuto,
    Marcato,
    Fermata,
    FermataShort,   // angled (LP \shortfermata)
    FermataLong,    // square (LP \longfermata)
    Bend,           // guitar bend-up with semitone amount (@bend(full))
    HammerOn,       // TAB "H" (@ho)
    PullOff,        // TAB "P" (@po)
    Tap,            // TAB "T" (@tap)
    SnapPizz,       // Bartók pizzicato (@snappizz)
    Portato,
    /// <summary>Wedge-shaped extreme staccato (@staccatissimo).
    /// LILYPOND-REF: mf/feta-scripts.mf scripts.u/dstaccatissimo.</summary>
    Staccatissimo,
    // String bowing marks (always above the staff, like LP's defaults).
    // LILYPOND-REF: scm/script.scm "upbow"/"downbow"; mf/feta-scripts.mf.
    UpBow,
    DownBow,
    /// <summary>Harmonic circle (@harmonic / @flageolet).
    /// LILYPOND-REF: scripts.flageolet.</summary>
    Flageolet,
    // Bend-after gestures: a short curved line trailing off the note — a jazz
    // "fall" (drops away) or "doit" (rises away). LilyPond's \bendAfter#-N / #+N.
    Fall,
    Doit,
    // Ornaments
    Trill,
    Mordent,
    Prall,
    Turn,
    InvertedTurn,
    PrallTriller,
    // Breathing signs — NOT Script grobs: a BreathingSign sits at the TOP of the
    // staff, to the RIGHT of the note it follows (in the gap before the next
    // note), independent of note height and stem. Routed through the articulation
    // anchor/render pipeline for reuse but positioned independently.
    // LILYPOND-REF: lily/breathing-sign.cc; \breathe = scripts.rcomma, \caesura
    // = scripts.caesura.straight (ly/music-functions-init.ly, ly/gregorian.ly).
    Breath,
    Caesura,
    // Editorial (suggestion) accidentals — a small accidental ABOVE the note,
    // created from @editorial; the kind comes from the note's resolved
    // accidental. LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion
    EditorialSharp,
    EditorialFlat,
    EditorialNatural,
    EditorialDoubleSharp,
    EditorialDoubleFlat
}

/// <summary>
/// Dynamic levels from pianississimo to fortississimo.
/// </summary>
public enum DynamicLevel
{
    None,
    PPPPP = 6,
    PPPP = 12,
    PPP = 20,
    PP = 35,
    P = 50,
    MP = 65,
    MF = 80,
    /// <summary>fp — loud attack then soft; a single MIDI note gets the attack level.</summary>
    FP = 90,
    F = 95,
    /// <summary>sf — subito forte accent on one note.</summary>
    SF = 105,
    FF = 110,
    /// <summary>rf — rinforzando, gentler than the sforzato family.</summary>
    RF = 111,
    /// <summary>sfz / rfz / fz — sforzato family, a hair under fff.</summary>
    SFZ = 112,
    RFZ = 113,
    FZ = 114,
    /// <summary>sffz — the heaviest sforzato.</summary>
    SFFZ = 116,
    // fff moved off the 127 ceiling to give ffff/fffff their own headroom.
    FFF = 120,
    FFFF = 124,
    FFFFF = 127
}

/// <summary>
/// Predefined instrument tunings for tablature.
/// </summary>
public enum TuningType
{
    /// <summary>Standard guitar tuning: E A D G B E</summary>
    Guitar,
    /// <summary>4-string bass tuning: E A D G</summary>
    Bass,
    /// <summary>5-string bass tuning: B E A D G</summary>
    Bass5,
    /// <summary>6-string bass tuning: B E A D G C</summary>
    Bass6,
    /// <summary>Ukulele tuning: G C E A</summary>
    Ukulele,
    /// <summary>Custom tuning</summary>
    Custom
}
