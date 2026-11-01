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

namespace LilySharp.Core.Editing;

/// <summary>
/// One thing <see cref="CompositionChecks"/> found. <see cref="Message"/> is the sentence the composing
/// flow reads; the rest is for the review (docs/review-tool-design.md §2). What is said of the check
/// itself, whichever piece it was made in, is <see cref="ReviewNotes"/>'s, by <see cref="Id"/>.
/// </summary>
/// <param name="Id">Which check made it, as <c>melody.leaps</c>.</param>
/// <param name="Message">The finding and the advice, one sentence.</param>
public sealed record CompositionFinding(string Id, string Message)
{
    /// <summary>The parts it is about; empty when it is about the piece.</summary>
    public IReadOnlyList<string> Parts { get; init; } = [];

    /// <summary>The bars it is about, as the form plays them, first bar 1; empty when the check does not
    /// name bars. A check that counts bars names the ones it can show, not always all it counted.</summary>
    public IReadOnlyList<int> Bars { get; init; } = [];

    /// <summary>The source line of the first place it is about, first line 1; 0 when the check reads the sounding notes.</summary>
    public int Line { get; init; }

    /// <summary>What is written there; null with no <see cref="Line"/>.</summary>
    public string? Written { get; init; }

    /// <summary>The question to put to the person, where only their intent can decide; null when there is none.</summary>
    public string? Ask { get; init; }
}
