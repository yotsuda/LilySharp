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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Clef type enumeration.
/// </summary>
public enum ClefType
{
    /// <summary>Treble (G) clef.</summary>
    Treble,
    /// <summary>Bass (F) clef.</summary>
    Bass,
    /// <summary>Alto (C) clef, centered on the middle line.</summary>
    Alto,
    /// <summary>Tenor (C) clef, centered on the fourth line.</summary>
    Tenor,
    /// <summary>Treble clef sounding an octave lower (<c>treble_8</c>).</summary>
    Treble8Below,
    /// <summary>Treble clef with the 8 ABOVE, sounding an octave up (<c>treble^8</c>).</summary>
    Treble8Above,
    /// <summary>Soprano clef: C clef on line 1 (C4 on the bottom line).</summary>
    Soprano,
    /// <summary>Mezzo-soprano clef: C clef on line 2.</summary>
    MezzoSoprano,
    /// <summary>Baritone clef: C clef on line 5 (top line).</summary>
    Baritone,
    /// <summary>Bass clef sounding an octave lower (<c>bass_8</c>, written like bass).</summary>
    Bass8Below,
    /// <summary>Unpitched percussion clef (positions like alto).</summary>
    Percussion,
    /// <summary>Tablature clef for fret/string notation.</summary>
    Tab
}

/// <summary>
/// How piano pedal marks (sustain/sostenuto/una-corda) are drawn for a part.
/// LILYPOND-REF: lily/piano-pedal-engraver.cc — pedalSustainStyle; the three
/// LilyPond styles render as:
///   bracket: |_________/\____|   (a spanning bracket; the default here)
///   text:       Ped.        *    ("Ped." at engage, "*" at release)
///   mixed:      Ped. ______/\____|  ("Ped." text then a bracket line)
/// LilyPond defaults to 'text; Lily# defaults to Bracket because the bracket
/// shows the pedalled SPAN unambiguously (a language choice — every style still
/// renders layout-identically to the corresponding LilyPond style).
/// </summary>
public enum PedalStyle
{
    /// <summary>A spanning bracket with a hook at each end. Lily# default.</summary>
    Bracket,
    /// <summary>"Ped." at engage and "*" at release, with no bracket.</summary>
    Text,
    /// <summary>"Ped." text followed by a bracket line and a release hook.</summary>
    Mixed
}

/// <summary>
/// A single staff with its own clef and voices.
/// </summary>
/// <remarks>
/// In a grand staff (piano), there are typically two staves:
/// - Upper staff: treble clef, right hand
/// - Lower staff: bass clef, left hand
/// </remarks>
public sealed record Staff(
    ClefType Clef,
    ImmutableArray<Voice> Voices,
    TuningType? Tuning = null,
    string? InstrumentName = null,
    // Whether this is an ossia staff (rendered at reduced size).
    // LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
    bool IsOssia = false,
    // Whether empty staves should be automatically hidden (hara-kiri).
    // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-empty property
    // Equivalent to \override VerticalAxisGroup.remove-empty = ##t
    bool RemoveEmpty = false,
    // Whether to allow removal even in the first system.
    // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-first property
    // When false (default), the first system always shows all staves.
    bool RemoveFirst = false,
    // Staff affinity direction for non-spaceable staves.
    // LILYPOND-REF: lily/align-interface.cc:240-252 staff-affinity
    // null = normal spaceable staff, UP = attach to staff above, DOWN = attach to staff below.
    // Used for ossia and cue staves.
    int? StaffAffinity = null,
    // Per-staff key signature override. Set for a TRANSPOSED part in a
    // multi-staff score so the staff shows its own (transposed) key while the
    // concert-pitch staves keep the score key. Null = use the score key.
    KeySignature? PerStaffKeySignature = null,
    // Whether this is an independent TEXT row (chord symbols or lyric syllables) —
    // no staff lines, no notes, just text laid out by timing in its own band. The
    // layout/renderer treat every text row the same; the chord-vs-lyric content
    // distinction lives on the items (ChordNameItem / LyricItem)
    // tagged with this staff's index, not on the staff.
    bool IsTextRow = false,
    // For a lyrics text row, how many stacked verses it carries (1 = a single
    // line). Drives the reserved band height so verse 2+ don't overlap the staff
    // below. Always 1 for chord rows and normal staves.
    int TextRowVerses = 1,
    // Whether this text row carries LYRICS (vs chord symbols). A lyric row
    // renders as "a staff with the lines removed": it occupies a full
    // staff-height band, its barlines run at staff height and the words sit
    // where the staff would be. Chord rows keep the compact band their
    // symbols hang on.
    bool IsLyricsTextRow = false
)
{
    /// <summary>The primary voice (first voice).</summary>
    public Voice PrimaryVoice => Voices[0];

    /// <summary>Whether this staff has multiple voices.</summary>
    public bool IsMultiVoice => Voices.Length > 1;

    /// <summary>Number of measures (from primary voice).</summary>
    public int MeasureCount => PrimaryVoice.Measures.Length;

    /// <summary>Whether this is a tablature staff.</summary>
    public bool IsTab => Clef == ClefType.Tab;

    /// <summary>
    /// Creates a single-voice staff.
    /// </summary>
    /// <summary>Number of staff lines (5 default; 1 = rhythmic, 2 = timbales
    /// style), drawn centered on the normal 5-line frame — positions, barline
    /// height and stems keep the standard geometry.
    /// LILYPOND-REF: StaffSymbol line-positions (percussion/timbales styles).</summary>
    public int Lines { get; init; } = 5;

    /// <summary>
    /// How this part's piano pedal marks render (part property <c>pedal</c>:
    /// <c>bracket</c> | <c>text</c> | <c>mixed</c>). Default <see cref="PedalStyle.Bracket"/>.
    /// LILYPOND-REF: lily/piano-pedal-engraver.cc — pedalSustainStyle.
    /// </summary>
    public PedalStyle PedalStyle { get; init; } = PedalStyle.Bracket;

    /// <summary>Creates a single-voice staff.</summary>
    public static Staff Create(ClefType clef, Voice voice, string? instrumentName = null)
        => new(clef, ImmutableArray.Create(voice), null, instrumentName);

    /// <summary>Creates a staff holding one or more voices (polyphony).</summary>
    public static Staff Create(ClefType clef, ImmutableArray<Voice> voices, string? instrumentName = null)
        => new(clef, voices, null, instrumentName);

    /// <summary>
    /// The NOTATION clef of the part a tab staff was derived from: treble_8
    /// (standard guitar notation) means the written pitches sound an octave
    /// lower, which the fret calculation must recover.
    /// </summary>
    public ClefType TabSourceClef { get; init; } = ClefType.Treble;

    /// <summary>
    /// The part's resolved SOUNDING transposition in semitones (written→sounding),
    /// EXCLUDING the octave the clef already carries — a bass sounds −12 from its plain
    /// bass clef, a piccolo +12. Resolved once (explicit <c>transposition</c> property &gt;
    /// instrument preset &gt; tuning default) and read by BOTH the tab fret shift
    /// (<see cref="Tablature.Tunings.SoundingShift"/>) and MIDI, so the printed fret and
    /// the sounding pitch always agree.
    /// </summary>
    public int Transposition { get; init; }

    /// <summary>
    /// A tab staff that prints fret digits ONLY (LilyPond's default TabStaff):
    /// no stems, flags, beams, dots, rests or tuplet brackets — the rhythm is read
    /// from a paired notation staff. False (this renderer's default) is the
    /// <c>\tabFullNotation</c> equivalent. Set by <c>tab part as numbers</c>.
    /// </summary>
    public bool TabNumbersOnly { get; init; }

    /// <summary>
    /// Creates a tablature staff with the specified tuning.
    /// </summary>
    public static Staff CreateTab(TuningType tuning, Voice voice, ClefType sourceClef = ClefType.Treble,
        int transposition = 0, bool numbersOnly = false)
        => new(ClefType.Tab, ImmutableArray.Create(voice), tuning)
        { TabSourceClef = sourceClef, Transposition = transposition, TabNumbersOnly = numbersOnly };

    /// <summary>
    /// Creates an ossia staff (small alternative passage).
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize.
    /// An ossia exists only while its fragment plays: LP instantiates the ossia
    /// context just for the span, so no staff prints anywhere else (NR "Ossia
    /// staves"). Hara-kiri per system — including the first — is the
    /// equivalent here; the renderer additionally trims the staff to the
    /// fragment's measures WITHIN a system.
    /// </summary>
    public static Staff CreateOssia(ClefType clef, Voice voice, string? instrumentName = null)
        => new(clef, ImmutableArray.Create(voice), null, instrumentName, IsOssia: true,
            RemoveEmpty: true, RemoveFirst: true);

    /// <summary>
    /// Creates an independent text row (chord-symbol or lyrics row): no staff lines
    /// or notes. The voice is a placeholder for measure/index bookkeeping; the text
    /// is carried as <c>ChordNameItem</c> / <c>LyricItem</c>s tagged with this row's
    /// staff index.
    /// </summary>
    /// <remarks>
    /// The affinity is DOWN — the ChordNames default — and a LYRICS row is flipped to UP
    /// where it is tagged as one (<c>MeasureCollector</c>), because the two contexts
    /// disagree: ly/engraver-init.ly:721 gives ChordNames DOWN and :648 gives Lyrics UP.
    /// <para>
    /// ⚠️ AN AFFINITY IS WHAT MAKES A LINE NON-SPACEABLE
    /// (LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 <c>is_spaceable</c>), so setting
    /// it is not decoration: it is what puts the row into the loose-line chain instead of
    /// the page's own spring chain.
    /// </para>
    /// </remarks>
    public static Staff CreateTextRow(Voice voice)
        => new(ClefType.Treble, ImmutableArray.Create(voice), IsTextRow: true,
            StaffAffinity: Layout.StaffAffinityDirection.Down);

    /// <summary>Parses a pedal-style string; unknown/empty falls back to the default Bracket.</summary>
    public static PedalStyle ParsePedalStyle(string? style) => style switch
    {
        "text" => PedalStyle.Text,
        "mixed" => PedalStyle.Mixed,
        _ => PedalStyle.Bracket
    };

    /// <summary>
    /// Parses a clef string to ClefType.
    /// </summary>
    public static ClefType ParseClef(string clef) => clef.ToLowerInvariant() switch
    {
        "treble" => ClefType.Treble,
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        "treble^8" => ClefType.Treble8Above,
        "soprano" => ClefType.Soprano,
        "mezzosoprano" => ClefType.MezzoSoprano,
        "baritone" => ClefType.Baritone,
        "bass_8" => ClefType.Bass8Below,
        "percussion" => ClefType.Percussion,
        "tab" => ClefType.Tab,
        _ => ClefType.Treble
    };

    /// <summary>
    /// Parses a SyntaxKind to ClefType.
    /// </summary>
    public static ClefType ParseClef(SyntaxKind kind) => kind switch
    {
        SyntaxKind.TrebleKeyword => ClefType.Treble,
        SyntaxKind.BassKeyword => ClefType.Bass,
        SyntaxKind.AltoKeyword => ClefType.Alto,
        SyntaxKind.TenorKeyword => ClefType.Tenor,
        SyntaxKind.Treble8Keyword => ClefType.Treble8Below,
        SyntaxKind.Treble8UpKeyword => ClefType.Treble8Above,
        SyntaxKind.SopranoKeyword => ClefType.Soprano,
        SyntaxKind.MezzoSopranoKeyword => ClefType.MezzoSoprano,
        SyntaxKind.BaritoneKeyword => ClefType.Baritone,
        SyntaxKind.Bass8Keyword => ClefType.Bass8Below,
        SyntaxKind.PercussionKeyword => ClefType.Percussion,
        _ => ClefType.Treble
    };
}