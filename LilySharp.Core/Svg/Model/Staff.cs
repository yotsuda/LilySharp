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
    bool IsOssia = false
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