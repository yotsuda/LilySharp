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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A slur written on ONE chord member — <c>&lt;c e( g&gt;4 &lt;d f) a&gt;</c> — is bound to that
/// note head, not to the chord's column: LilyPond's note-slur (lily/slur-engraver.cc:138-152),
/// scored by the slur_head-only branches of lily/slur-scoring.cc (:216-219 bound info, :505-509
/// "allow only minimal movement", :574-582 the base on the head's inner edge, no staff-line
/// nudge). LilyPond 2.26.0, Lab sessions/p691/headslur (hs.lys and its twin): all nine bows
/// of the probe book agree to 0.01.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordHeadSlurTests
{
    private const string Book = """
        octave absolute
        time 4/4
        part m {
          section A {
            <c e( g>4 <d f) a> <c( e g>4 <d f a)> |
          }
        }
        form { A }
        score { staff m }
        """;

    private static Score Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return new MeasureCollector().Collect(tree, "m");
    }

    [Fact]
    public void AMemberSlur_BindsToThatHead()
    {
        var slurs = new SlurDetector().DetectSlurs(Collect(Book));
        Assert.Equal(2, slurs.Length);
        // e (−4) to f (−3); c (−6) to a (−1).
        Assert.True(slurs[0].StartOnHead);
        Assert.True(slurs[0].EndOnHead);
        Assert.Equal(-4, slurs[0].StartStaffPosition);
        Assert.Equal(-3, slurs[0].EndStaffPosition);
        Assert.True(slurs[1].StartOnHead);
        Assert.True(slurs[1].EndOnHead);
        Assert.Equal(-6, slurs[1].StartStaffPosition);
        Assert.Equal(-1, slurs[1].EndStaffPosition);
    }

    [Fact]
    public void AMarkAfterTheBracket_StaysOnTheChord()
    {
        var slurs = new SlurDetector().DetectSlurs(Collect(Book.Replace("<d f a)>", "<d f a>)")));
        Assert.True(slurs[1].StartOnHead);
        Assert.False(slurs[1].EndOnHead);
    }

    /// <summary>
    /// LilyPond 2.26.0 (Lab sessions/p691/headslur, bar 1): the e→f slur leaves the e's head
    /// at its right edge plus the tilt shift, 1.4270 right of the head's left and 2.2725 under
    /// the middle line (the e's centre −2.0 less a quarter of the head, 0.2725); it arrives
    /// 0.1228 into the f's head, 1.7725 under the middle.
    /// </summary>
    [Fact]
    public void AMemberSlur_HangsFromTheHeadsInnerEdge()
    {
        var tree = SyntaxTree.Parse(Book);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        var page = doc.Page;
        double middle = Assert.Single(TwinBeamSweep.StavesOf(page)).Middle;
        var columns = page.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .Select(g => g.X).Distinct().OrderBy(x => x).ToArray();
        var slur = page.Beziers.OrderBy(b => b.P0.X).First();
        Assert.Equal(1.4270, slur.P0.X - columns[0], 0.01);
        Assert.Equal(-2.2725, middle - slur.P0.Y, 0.01);
        Assert.Equal(0.1228, slur.P1.X - columns[1], 0.01);
        Assert.Equal(-1.7725, middle - slur.P1.Y, 0.01);
    }

    [Fact]
    public void TheTwin_WritesTheMarkOnTheMember()
    {
        var ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book));
        Assert.Contains("<c e( g>4 <d f) a> <c( e g>4 <d f a)>", ly);
    }

    /// <summary>The MusicXML slur start sits on the member's own note (the E), not on the
    /// chord's first note (the C).</summary>
    [Fact]
    public void TheMusicXml_PutsTheSlurOnTheMembersNote()
    {
        var xml = System.Xml.Linq.XDocument.Parse(
            new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book)).ToXml().ToString());
        var notes = xml.Descendants("note").ToArray();
        string Step(System.Xml.Linq.XElement n) => (string?)n.Element("pitch")?.Element("step") ?? "";
        string? Slur(System.Xml.Linq.XElement n) =>
            (string?)n.Element("notations")?.Element("slur")?.Attribute("type");
        Assert.Equal("start", Slur(notes[1]));
        Assert.Equal("E", Step(notes[1]));
        Assert.Null(Slur(notes[0]));
        Assert.Equal("stop", Slur(notes[4]));
        Assert.Equal("F", Step(notes[4]));
    }

    [Fact]
    public void AMarkWithNoPitchBeforeIt_IsReported()
    {
        var tree = SyntaxTree.Parse(Book.Replace("<c e( g>4", "<( c e g>4"));
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains("goes after the pitch"));
    }
}
