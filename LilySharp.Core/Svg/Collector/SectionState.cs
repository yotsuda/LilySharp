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

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Section-tracking state owned by <see cref="MeasureCollector"/>: the section
/// declarations by name, the part-major cells, and the per-name measure-start
/// bookkeeping that keeps lyric/chord rows aligned to their sections. Grouped
/// out of MeasureCollector's ~55-field bag so section state has one owner and
/// one <see cref="Reset"/> (mirrors the OctaveContext extraction).
/// </summary>
internal sealed class SectionState
{
    /// <summary>Section declarations by name (last wins).</summary>
    public Dictionary<string, SectionDeclarationSyntax> Sections { get; } = new();

    /// <summary>Part-major cells: <c>(section, part)</c> → its declaration.</summary>
    public Dictionary<(string section, string part), SectionDeclarationSyntax> PartMajorCells { get; } = new();

    /// <summary>First measure index of each section, by name.</summary>
    public Dictionary<string, int> StartMeasure { get; } = new();

    /// <summary>All measure-start indices of each section (one per repeat pass), by name.</summary>
    public Dictionary<string, List<int>> AllStarts { get; } = new();

    /// <summary>Row section labels: <c>(measure index, label, source position)</c>.</summary>
    public List<(int MeasureIndex, string Label, int Position)> RowLabels { get; } = new();

    /// <summary>Clears all section bookkeeping for a fresh collection pass.</summary>
    public void Reset()
    {
        Sections.Clear();
        PartMajorCells.Clear();
        StartMeasure.Clear();
        AllStarts.Clear();
        RowLabels.Clear();
    }
}
