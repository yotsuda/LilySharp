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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Scripts on individual chord members (<c>&lt;c@staccato e@accent&gt;</c>) join
/// the chord's script column and obey manual <c>.up</c>/<c>.down</c> directions.
/// LILYPOND-REF: lily/script-engraver.cc Script_engraver — scripts are made from
/// events, and chord members each carry their own. Paired with the LP regression
/// book chord-scripts.ly; before this the member scripts were parsed and then
/// dropped in silence (only fingering/string/courtesy were consumed).
/// </summary>
[Trait("Category", "Unit")]
public class ChordMemberScriptTests
{
    private static LilySharp.Core.Svg.Model.Score Collect(string source)
        => new MeasureCollector().Collect(SyntaxTree.Parse(source), null);

    [Fact]
    public void MemberScripts_JoinTheChordsScriptColumn()
    {
        var score = Collect("<c'@staccato e'@staccato g'@staccato b'@staccato>4");
        var dots = score.Articulations.Where(a => a.Type == ArticulationType.Staccato).ToList();
        Assert.Equal(4, dots.Count);
        // All four anchor on the SAME chord item — one script column.
        Assert.Single(dots.Select(d => (d.MeasureIndex, d.ItemIndex)).Distinct());
    }

    [Fact]
    public void MemberScripts_ObeyManualDirections()
    {
        var score = Collect("<c'@marcato.down e'@marcato.up>4");
        var marks = score.Articulations.Where(a => a.Type == ArticulationType.Marcato).ToList();
        Assert.Equal(2, marks.Count);
        Assert.Contains(marks, m => m.IsAbove && m.DirectionForced);
        Assert.Contains(marks, m => !m.IsAbove && m.DirectionForced);
    }

    [Fact]
    public void MemberScripts_AreNotCopiedByAChordRepetition()
    {
        // LP's q copies note events only — a member script stays on the original.
        var score = Collect("<c'@staccato e'@staccato>4 q q q");
        Assert.Equal(2, score.Articulations.Count(a => a.Type == ArticulationType.Staccato));
    }
}
