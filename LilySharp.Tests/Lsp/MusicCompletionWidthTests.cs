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

using System.Linq;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The popup that opens while notes are typed stays narrow: VS Code widens its suggest
/// widget to the widest row's label plus inline detail (suggestWidget.ts, fitWidthToDetails),
/// and one long detail stretched it across the preview beside the editor — over the very
/// bars being typed (owner report, 2026-09-25). A row's detail is a short label; the
/// explanation it used to carry lives in its documentation, which only the details panel
/// shows.
/// </summary>
public class MusicCompletionWidthTests
{
    private const int MaxDetail = 32;

    [Fact]
    public void EveryMusicRow_KeepsItsDetailShort()
    {
        var rows = LilySharpLanguageServer.GetMusicCompletions("", 0, phraseNames: ["theme"]).Items
            .Concat(LilySharpLanguageServer.GetMusicCompletions("", 3, keyTonic: 'a').Items)
            .Concat(LilySharpLanguageServer.GetDrumCompletions().Items)
            .ToList();
        Assert.True(rows.Count > 50, $"the net did not bite: {rows.Count} rows");
        var wide = rows.Where(r => (r.Detail?.Length ?? 0) > MaxDetail)
            .Select(r => $"{r.Label}: \"{r.Detail}\" ({r.Detail!.Length})")
            .Distinct()
            .ToList();
        Assert.True(wide.Count == 0, $"{wide.Count} row(s) wider than {MaxDetail}:\n" + string.Join("\n", wide));
    }
}
