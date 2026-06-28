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
    Treble,
    Bass,
    Alto,
    Tenor,
    Treble8Below,
    Tab
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
    /// <summary>Whether this is an ossia staff (rendered at reduced size).</summary>
    /// <remarks>LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize</remarks>
    bool IsOssia = false,
    /// <summary>
    /// Whether empty staves should be automatically hidden (hara-kiri).
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-empty property
    /// Equivalent to \override VerticalAxisGroup.remove-empty = ##t
    /// </summary>
    bool RemoveEmpty = false,
    /// <summary>
    /// Whether to allow removal even in the first system.
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-first property
    /// When false (default), the first system always shows all staves.
    /// </summary>
    bool RemoveFirst = false,
    /// <summary>
    /// Staff affinity direction for non-spaceable staves.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-252 staff-affinity
    /// null = normal spaceable staff, UP = attach to staff above, DOWN = attach to staff below.
    /// Used for ossia and cue staves.
    /// </remarks>
    int? StaffAffinity = null,
    /// <summary>
    /// Per-staff key signature override. Set for a TRANSPOSED part in a
    /// multi-staff score so the staff shows its own (transposed) key while the
    /// concert-pitch staves keep the score key. Null = use the score key.
    /// </summary>
    KeySignature? PerStaffKeySignature = null,
    /// <summary>
    /// Whether this is an independent TEXT row (chord symbols or lyric syllables) —
    /// no staff lines, no notes, just text laid out by timing in its own band. The
    /// layout/renderer treat every text row the same; the chord-vs-lyric content
    /// distinction lives on the items (<c>ChordNameItem</c> / <c>LyricItem</c>)
    /// tagged with this staff's index, not on the staff.
    /// </summary>
    bool IsTextRow = false
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
    public static Staff Create(ClefType clef, Voice voice, string? instrumentName = null)
        => new(clef, ImmutableArray.Create(voice), null, instrumentName);

    /// <summary>Creates a staff holding one or more voices (polyphony).</summary>
    public static Staff Create(ClefType clef, ImmutableArray<Voice> voices, string? instrumentName = null)
        => new(clef, voices, null, instrumentName);

    /// <summary>
    /// Creates a tablature staff with the specified tuning.
    /// </summary>
    public static Staff CreateTab(TuningType tuning, Voice voice)
        => new(ClefType.Tab, ImmutableArray.Create(voice), tuning);

    /// <summary>
    /// Creates an ossia staff (small alternative passage).
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
    /// </summary>
    public static Staff CreateOssia(ClefType clef, Voice voice, string? instrumentName = null)
        => new(clef, ImmutableArray.Create(voice), null, instrumentName, IsOssia: true);

    /// <summary>
    /// Creates an independent text row (chord-symbol or lyrics row): no staff lines
    /// or notes. The voice is a placeholder for measure/index bookkeeping; the text
    /// is carried as <c>ChordNameItem</c> / <c>LyricItem</c>s tagged with this row's
    /// staff index.
    /// </summary>
    public static Staff CreateTextRow(Voice voice)
        => new(ClefType.Treble, ImmutableArray.Create(voice), IsTextRow: true);

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
        _ => ClefType.Treble
    };
}