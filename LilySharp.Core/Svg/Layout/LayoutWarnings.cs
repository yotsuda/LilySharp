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
using System.Globalization;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// What a finished layout had to give up, said in words — the console's share of LilyPond's
/// layout warnings. There is one: an over-full page.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:806-822 solve_rod_spring_problem ("compressing over-full
/// page by %.1f
/// staff-spaces") and lily/page-breaking.cc:577-579 draw_page ("page %d has been compressed") — said once
/// a page here. The page is still drawn, its systems pressed together
/// (<see cref="PageLayout.Overflow"/>); the words are for a reader who cannot see it, such as an
/// OMR reader's training run that varies <c>systemsPerPage</c> and must drop the pages whose
/// systems overlap (LilySharp-Omr's proposal of 2026-10-02, P2).
/// </remarks>
internal static class LayoutWarnings
{
    /// <summary>Tells <paramref name="sink"/> about each over-full page of
    /// <paramref name="layout"/>; nothing when the sink is null.</summary>
    internal static void Report(ScoreLayout layout, Action<string>? sink)
    {
        if (sink is null)
            return;
        foreach (var page in layout.Pages)
        {
            if (page.Overflow > 0)
                sink(string.Format(CultureInfo.InvariantCulture,
                    "page {0} is over-full by {1:F1} staff spaces: its systems were pressed together to fit it",
                    page.PageIndex + 1, page.Overflow));
        }
    }
}
