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

namespace LilySharp.Core.MusicXmlImport;

/// <summary>
/// Collects the human-facing warnings from a MusicXML import: every construct the
/// importer dropped or approximated. Import is a deliberately opinionated,
/// non-unique mapping (many valid <c>.lys</c> render the same MusicXML), so the
/// report is how the result stays honest — anything not representable is surfaced
/// here, never silently wrong.
/// </summary>
public sealed class ImportReport
{
    private readonly List<string> _warnings = new();

    /// <summary>The accumulated warnings, in the order they were reported.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>True when at least one construct was dropped or approximated.</summary>
    public bool HasWarnings => _warnings.Count > 0;

    /// <summary>Records one approximation/drop, e.g. "grace note at m.12 dropped".</summary>
    public void Warn(string message) => _warnings.Add(message);

    /// <summary>Records a per-measure warning with the bar number folded into the text.</summary>
    public void Warn(int measure, string message) => _warnings.Add($"m.{measure}: {message}");
}
