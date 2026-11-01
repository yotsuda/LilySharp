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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie written on one member of a chord (OMR request 16, 2026-11-01): <c>&lt;c f d~&gt;8. &lt;c f
/// d&gt;16</c> ties the d alone and sounds the c and the f again, on the page, in the sound, in
/// MusicXML and in the LilyPond twin. LilyPond's chord_body takes it the same way.
/// </summary>
[Trait("Category", "Unit")]
public class ChordMemberTieTests
{
    private static string Book(string music) => $$"""
        octave absolute
        time 3/4
        part rh { clef treble }
        section A { rh { {{music}} } }
        form { A }
        score { staff rh }
        """;

    private const string OneMember = "<c'' f'' d'''~>8. <c'' f'' d'''>16 r2 |";
    private const string TwoMembers = "<c'' f''~ d'''~>8. <c'' f'' d'''>16 r2 |";
    private const string WholeChord = "<c'' f'' d'''>8.~ <c'' f'' d'''>16 r2 |";

    private static ScoreLayout LayoutOf(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.False(tree.HasErrors);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        return new LayoutEngine(score.Paper).Layout(score);
    }

    [Theory]
    [InlineData(OneMember, 1)]
    [InlineData(TwoMembers, 2)]
    [InlineData(WholeChord, 3)]
    public void ThePage_TiesTheMembersThatCarryATie(string music, int ties)
        => Assert.Equal(ties, LayoutOf(music).TieLayouts.Length);

    [Fact]
    public void AMemberTieToAChordThatLacksIt_TiesNothing_AndWarns()
    {
        const string music = "<c'' f'' d'''~>8. <c'' f''>16 r2 |";
        Assert.Empty(LayoutOf(music).TieLayouts);
        var collector = new MeasureCollector();
        collector.Collect(SyntaxTree.Parse(Book(music)), null);
        Assert.NotEmpty(collector.TieTargetWarnings);
    }

    [Fact]
    public void TheSound_HoldsTheTiedMember_AndStrikesTheOthersAgain()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book(OneMember))).Tracks
            .SelectMany(t => t.Notes).ToList();
        Assert.Single(notes, n => n.Pitch == 86); // d''' held over both
        Assert.Equal(2, notes.Count(n => n.Pitch == 72)); // c'' struck twice
        Assert.Equal(2, notes.Count(n => n.Pitch == 77)); // f'' struck twice
    }

    [Fact]
    public void MusicXml_StartsAndStopsTheTiedMemberAlone()
    {
        var notes = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(OneMember))).Parts.Single()
            .Measures.SelectMany(m => m.Notes).ToList();
        Assert.Equal(1, notes.Count(n => n.TieStart));
        Assert.Equal(1, notes.Count(n => n.TieStop));
        Assert.Equal("D", notes.Single(n => n.TieStart).Step);
    }

    [Fact]
    public void TheTwin_WritesTheTieOnTheMember()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(OneMember)));
        Assert.Matches(@"<\S+ \S+ \S+~>8\.", ly);
        Assert.DoesNotMatch(@">8\.~", ly);
    }

    [Theory]
    [InlineData("<c'' f''~ d'''>4~ <c'' f'' d'''>4 r4 |")] // on a member and on the whole chord
    [InlineData("<~ c'' f''>4 <c'' f''>4 r4 |")]           // before any pitch
    public void AMisplacedTie_IsAnError(string music)
        => Assert.True(SyntaxTree.Parse(Book(music)).HasErrors);
}
