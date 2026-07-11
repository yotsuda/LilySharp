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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note below the tab's lowest string can't be fretted; instead of clamping it
/// to a wrong open string (fret 0), the tab hides it (<see cref="NoteItem.TabBelowRange"/>).
/// The flag is per tab staff — the companion notation staff shows the true pitch.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TabBelowRangeHideTests
{
    private static MultiStaffScore Collect(string body)
    {
        var tree = SyntaxTree.Parse(
            "time 4/4\nkey c major\npart m { instrument bass section A { " + body + " } }\n"
            + "form main { A }\nscore main { staff m  tab m }");
        var spec = RenderSpecParser.FindAll(tree).First(s => s.HasTab);
        return new MeasureCollector().CollectMultiStaff(tree, spec);
    }

    private static NoteItem[] TabNotes(MultiStaffScore score, int measureIndex) =>
        score.EnumerateStaves().First(s => s.Staff.IsTab).Staff
            .PrimaryVoice.Measures[measureIndex].Items.OfType<NoteItem>().ToArray();

    [Fact]
    public void BelowLowestString_FlaggedHiddenOnTab()
    {
        // Measure 2 (c, …) sits an octave below the bass's low E; measure 1 is playable.
        var score = Collect("e8 f g a e f g a | c,8 d, e, f, c, d, e, f, |");
        Assert.All(TabNotes(score, 1), n => Assert.True(n.TabBelowRange));
        Assert.All(TabNotes(score, 0), n => Assert.False(n.TabBelowRange));
    }

    [Fact]
    public void NotationStaff_NeverFlagged()
    {
        // The flag is tab-only, so the notation staff still renders the note.
        var score = Collect("c,8 d, e, f, c, d, e, f, | r1 |");
        var notation = score.EnumerateStaves().First(s => !s.Staff.IsTab).Staff;
        Assert.All(notation.PrimaryVoice.Measures[0].Items.OfType<NoteItem>(),
            n => Assert.False(n.TabBelowRange));
    }
}
