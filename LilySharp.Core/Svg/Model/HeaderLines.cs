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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// The two header lines of 2026-11-13 (owner's decision), beyond the four strings the model
/// has always carried (<see cref="MultiStaffScore.Title"/>, <c>Composer</c>, <c>Subtitle</c>,
/// <c>Poet</c>): the arranger — LilyPond's <c>\header arranger</c>, right-aligned on the line
/// under the composer — and the copyright line — LilyPond's <c>\header copyright</c>, centred
/// at the foot of the first page — each with where its string is written (0 = none).
/// </summary>
/// <remarks>
/// One value, compared by what it holds: it is a key of the incremental compiler's
/// (IncrementalCompiler's global key) and a field of the collector's MetadataState, which
/// compares and shifts it as a whole. A score with neither carries <see cref="None"/>, the
/// one instance, so the keystroke path allocates nothing for it.
/// </remarks>
/// <param name="Arranger">The arranger, or null.</param>
/// <param name="ArrangerPosition">Where the arranger's string is written in the file.</param>
/// <param name="Copyright">The copyright line, or null.</param>
/// <param name="CopyrightPosition">Where the copyright's string is written in the file.</param>
public sealed record HeaderLines(
    string? Arranger = null, int ArrangerPosition = 0,
    string? Copyright = null, int CopyrightPosition = 0)
{
    /// <summary>A header with no arranger and no copyright line.</summary>
    public static HeaderLines None { get; } = new();

    /// <summary>
    /// This header with both source positions carried through <paramref name="shift"/> (the
    /// collector's window map after an edit), or null when one of them cannot be.
    /// </summary>
    public HeaderLines? Shifted(Func<int, int?> shift)
    {
        if (ReferenceEquals(this, None))
            return this;
        if (shift(ArrangerPosition) is not { } arranger || shift(CopyrightPosition) is not { } copyright)
            return null;
        return this with { ArrangerPosition = arranger, CopyrightPosition = copyright };
    }
}
