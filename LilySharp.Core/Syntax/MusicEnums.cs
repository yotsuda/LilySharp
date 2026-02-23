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
    Portato,
    // Ornaments
    Trill,
    Mordent,
    Prall,
    Turn,
    InvertedTurn,
    PrallTriller
}

/// <summary>
/// Dynamic levels from pianississimo to fortississimo.
/// </summary>
public enum DynamicLevel
{
    None,
    PPP = 20,
    PP = 35,
    P = 50,
    MP = 65,
    MF = 80,
    F = 95,
    FF = 110,
    FFF = 127
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
    /// <summary>Ukulele tuning: G C E A</summary>
    Ukulele,
    /// <summary>Custom tuning</summary>
    Custom
}
