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
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A condensed or combined staff draws ONE clef, the first part's, and every part on it is
/// placed under that clef. The cello of <c>condensedStaff { vla vc }</c> was placed by its own
/// bass clef on the alto staff — its C3 drawn on the B3 space (grammar feedback C2,
/// 2026-10-13). MEASURED (LilyPond 2.26.0, Lab sessions/p903/c2): the twin's
/// <c>&lt;&lt; \vla \\ \vc &gt;&gt;</c> under <c>\clef "alto"</c> puts that C3 under the staff
/// on its ledger line, the same place a staff of its own under the alto clef does.
/// </summary>
[Trait("Category", "Unit")]
public class SharedStaffClefTests
{
    private static string Book(string scoreItems) =>
        "octave absolute\npart vla \"Viola\" { instrument viola }\npart vc \"Cello\" { instrument cello }\n"
        + "section A { vla { c'4 d' e' f' | } vc { c4 d e f | } }\n"
        + "form { A }\nscore { " + scoreItems + " }";

    private static List<int> Positions(string scoreItems, int staff = 0)
    {
        var tree = SyntaxTree.Parse(Book(scoreItems));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return score.StaffGroups.SelectMany(g => g.Staves).ElementAt(staff).Voices
            .SelectMany(v => v.Measures).SelectMany(m => m.Items)
            .SelectMany(i => i switch
            {
                NoteItem n => new[] { n.StaffPosition },
                ChordItem c => c.Notes.Select(n => n.StaffPosition).ToArray(),
                _ => [],
            })
            .OrderBy(p => p).ToList();
    }

    [Theory]
    [InlineData("condensedStaff { vla vc }")]
    [InlineData("combinedStaff { vla vc }")]
    public void TheLaterPart_IsPlacedUnderTheStaffsClef(string shared)
    {
        // The cello alone on an alto staff: where its notes belong on this one.
        var celloUnderAlto = Positions("staff vla staff alto vc", staff: 1);
        var violaUnderAlto = Positions("staff vla");
        Assert.Equal(celloUnderAlto.Concat(violaUnderAlto).OrderBy(p => p), Positions(shared));
    }
}
