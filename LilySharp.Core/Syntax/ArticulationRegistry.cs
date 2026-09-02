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

using System;
using System.Collections.Generic;

namespace LilySharp.Core.Syntax;

/// <summary>
/// Resolves articulation / ornament names written as <c>@name</c> to an
/// <see cref="ArticulationType"/>. This is the single source of truth that
/// replaces the former reserved lexer keywords, so ordinary words like
/// <c>turn</c> stay free as identifiers and only carry articulation meaning
/// behind the <c>@</c> sigil.
/// </summary>
public static class ArticulationRegistry
{
    // Recognized articulation / ornament names. Case-insensitive.
    private static readonly Dictionary<string, ArticulationType> ByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Articulations
            ["staccato"] = ArticulationType.Staccato,
            ["accent"] = ArticulationType.Accent,
            ["tenuto"] = ArticulationType.Tenuto,
            ["marcato"] = ArticulationType.Marcato,
            ["fermata"] = ArticulationType.Fermata,
            ["shortfermata"] = ArticulationType.FermataShort,
            ["longfermata"] = ArticulationType.FermataLong,
            ["portato"] = ArticulationType.Portato,
            ["staccatissimo"] = ArticulationType.Staccatissimo,
            // String bowing marks and the harmonic circle (○). '@harmonic' is the
            // familiar name for guitar / lead-sheet users; '@flageolet' is the
            // classical term — both render the circle. (The diamond harmonic
            // NOTEHEAD ◇ is '@notehead.diamond'.)
            ["upbow"] = ArticulationType.UpBow,
            ["downbow"] = ArticulationType.DownBow,
            ["flageolet"] = ArticulationType.Flageolet,
            ["harmonic"] = ArticulationType.Flageolet,
            ["hammeron"] = ArticulationType.HammerOn,
            ["pulloff"] = ArticulationType.PullOff,
            ["tap"] = ArticulationType.Tap,
            ["snappizz"] = ArticulationType.SnapPizz,
            ["stopped"] = ArticulationType.Stopped,
            ["thumb"] = ArticulationType.Thumb,
            ["heel"] = ArticulationType.Heel,
            ["toe"] = ArticulationType.Toe,
            ["scoop"] = ArticulationType.Scoop,
            ["plop"] = ArticulationType.Plop,
            ["fall"] = ArticulationType.Fall,
            ["doit"] = ArticulationType.Doit,
            // Ornaments
            ["trill"] = ArticulationType.Trill,
            ["mordent"] = ArticulationType.Mordent,
            ["prall"] = ArticulationType.Prall,
            ["turn"] = ArticulationType.Turn,
            // LilyPond's name (\reverseturn, scm/script.scm); the MusicXML term
            // "inverted-turn" was the spelling until 2026-09-02. The enum member keeps its
            // name (a rename is the owner's MSVS job).
            ["reverseturn"] = ArticulationType.InvertedTurn,
            ["pralltriller"] = ArticulationType.PrallTriller,
            // Breathing signs (after the note they follow)
            ["breath"] = ArticulationType.Breath,
            ["caesura"] = ArticulationType.Caesura,
        };

    /// <summary>
    /// Maps an <c>@name</c> to its articulation type, or
    /// <see cref="ArticulationType.None"/> when the name is not an articulation
    /// (it may still be a dynamic or a music mark, resolved elsewhere).
    /// </summary>
    public static ArticulationType Resolve(string name) =>
        ByName.TryGetValue(name, out var type) ? type : ArticulationType.None;

    /// <summary>True iff <paramref name="name"/> is a known articulation/ornament.</summary>
    public static bool IsKnown(string name) => ByName.ContainsKey(name);

    /// <summary>All recognized names and abbreviations (for tooling / diagnostics).</summary>
    public static IReadOnlyCollection<string> Names => ByName.Keys;
}
