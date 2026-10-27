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
using System.Xml.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A chord symbol, a figured bass group and a chord diagram written on a GRACE note (session
/// 964): where they stand, what room the run makes for them, and what the two exporters write.
/// The LilyPond numbers are the ledger's (chordname.grace.* / figbass.grace.* / diagram.grace.*);
/// these are the model-level nets around them.
/// </summary>
[Trait("Category", "Unit")]
public class GraceChordNameAndDiagramTests
{
    private static string Book(string music, string clef = "treble") => $$"""
        octave absolute
        time 4/4
        part m { clef {{clef}} }
        section A { m { {{music}} } }
        form { ~A }
        score { staff m }
        """;

    private static (ScoreLayout Layout, LilySharp.Core.Svg.Model.MultiStaffScore Score) LayoutOf(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)!);
        return (new LayoutEngine(new LayoutOptions()).Layout(score), score);
    }

    /// <summary>The run's columns as the drawer places them (<see cref="GraceNoteEngraver.RunPlacement"/>).</summary>
    private static SpacingRules.GraceColumnLayout Placed(ScoreLayout layout, LilySharp.Core.Svg.Model.MultiStaffScore score)
    {
        var grace = Assert.Single(layout.GraceNoteLayouts);
        var measure = score.StaffGroups[0].Staves[0].Voices[0].Measures[grace.MeasureIndex];
        return GraceNoteEngraver.PlacedColumns(grace.Columns, measure.Items[grace.MainNoteItemIndex],
            measure, grace.MainNoteItemIndex, layout.AllSystems[0].Measures[grace.MeasureIndex]);
    }

    [Fact]
    public void ASymbolOnAGrace_StandsOnTheGraceColumn_AndTheMainsOnItsOwn()
    {
        var (layout, score) = LayoutOf(Book("c4@chord(C) grace { d8@chord(D) } e4@chord(E) c2@chord(C) |"));
        var names = layout.ChordNameLayouts.OrderBy(c => c.X).ToList();
        Assert.Equal(4, names.Count);
        var grace = Assert.Single(layout.GraceNoteLayouts);
        // The D on the grace column: its anchor is the run's first column.
        Assert.Equal(grace.X, names[1].X, 6);
        // …and the E on the main note, past the grace, which the D's rod holds off: the name's
        // width + 0.5 + 0.5 + 0.1 (LilyPond's ChordName rods, ledger chordname.grace.*).
        double dWidth = ChordNameEngraver.FootprintWidth(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            score.ChordNames.Single(c => c.ChordText == "D"));
        Assert.Equal(dWidth + 1.1, names[2].X - names[1].X, 6);
        Assert.Equal(grace.MainNoteX, names[2].X, 6);
    }

    [Fact]
    public void AGraceWithNoSymbol_KeepsTheBareRun_AndTheSymbolsRodOnlyEachOther()
    {
        // The control: no symbol on the grace — the run is the bare grace spring.
        var (bare, _) = LayoutOf(Book("c4@chord(C) grace { d8 } e4@chord(E) c2@chord(C) |"));
        var (named, _) = LayoutOf(Book("c4@chord(C) grace { d8@chord(D) } e4@chord(E) c2@chord(C) |"));
        double bareGap = Assert.Single(bare.GraceNoteLayouts).MainNoteX - Assert.Single(bare.GraceNoteLayouts).X;
        double namedGap = Assert.Single(named.GraceNoteLayouts).MainNoteX - Assert.Single(named.GraceNoteLayouts).X;
        Assert.True(namedGap > bareGap + 0.9, $"the D's rod widens the grace → main gap: {bareGap:F3} → {namedGap:F3}");
        // A symbol on the grace with NONE on the main note rods nothing into the main: the gap
        // stays the bare spring (ledger chordname.grace.unnamed-main.*).
        var (unnamedMain, _) = LayoutOf(Book("c4@chord(C) grace { d8@chord(D) } e4 c2@chord(C) |"));
        double unnamedGap = Assert.Single(unnamedMain.GraceNoteLayouts).MainNoteX - Assert.Single(unnamedMain.GraceNoteLayouts).X;
        Assert.Equal(bareGap, unnamedGap, 6);
    }

    [Fact]
    public void ASymbolOnTheSecondOfTwoGraces_RodsOnlyFromThatColumn()
    {
        var (layout, score) = LayoutOf(Book("c4@chord(C) grace { d8 e8@chord(D) } f4@chord(E) c2@chord(C) |"));
        var grace = Assert.Single(layout.GraceNoteLayouts);
        var placed = Placed(layout, score);
        var names = layout.ChordNameLayouts.OrderBy(c => c.X).ToList();
        // The D stands on the SECOND grace column.
        Assert.Equal(grace.X + placed.Offsets[1], names[1].X, 6);
        // The first gap is the bare grace spring, the second carries the rod.
        Assert.True(placed.Offsets[1] < 1.6, $"first gap bare: {placed.Offsets[1]:F3}");
        Assert.True(placed.ToMain > 2.9, $"second gap rodded: {placed.ToMain:F3}");
    }

    [Fact]
    public void AFigureOnAGrace_StandsOnTheGraceColumn_AndMovesNothing()
    {
        var (bare, _) = LayoutOf(Book("c,4@figuredBass(5) grace { d,8 } e,4@figuredBass(3) c,2 |", "bass"));
        var (figured, _) = LayoutOf(Book("c,4@figuredBass(5) grace { d,8@figuredBass(6) } e,4@figuredBass(3) c,2 |", "bass"));
        var grace = Assert.Single(figured.GraceNoteLayouts);
        var figures = figured.FiguredBassLayouts.OrderBy(f => f.X).ToList();
        Assert.Equal(3, figures.Count);
        Assert.Equal(grace.X, figures[1].X, 6);
        Assert.Equal(new[] { "6" }, figures[1].FigureTexts);
        // The figure is narrower than the grace spring: the run does not move.
        Assert.Equal(Assert.Single(bare.GraceNoteLayouts).X, grace.X, 6);
        Assert.Equal(Assert.Single(bare.GraceNoteLayouts).MainNoteX, grace.MainNoteX, 6);
    }

    [Fact]
    public void ADiagramOnAGrace_StandsOffTheGraceColumnAsOffAFullSizeOne_AndRodsTheRunsNeighbours()
    {
        var (graced, _) = LayoutOf(Book("c4 grace { d8@diagram(x32010) } e4 c2 |"));
        var (full, _) = LayoutOf(Book("c4 d8@diagram(x32010) e8 c2 |"));
        var graceFrame = Assert.Single(graced.ArticulationLayouts);
        var fullFrame = Assert.Single(full.ArticulationLayouts);
        var grace = Assert.Single(graced.GraceNoteLayouts);
        // The grid stands where it stands off a full-size column (ledger: 1.689495 left of it).
        var fullMeasure = full.AllSystems[0].Measures[0];
        double fullHeadX = fullMeasure.X + fullMeasure.Items[1].X;
        Assert.Equal(fullFrame.X - fullHeadX, graceFrame.X - grace.X, 6);
        // …and the run makes room: the previous head to the grace, the grace to the main note,
        // both wider than the bare run (ledger diagram.grace.textlength.*: 3.193695 / 4.542156).
        var (bare, _) = LayoutOf(Book("c4 grace { d8 } e4 c2 |"));
        double bareGap = Assert.Single(bare.GraceNoteLayouts).MainNoteX - Assert.Single(bare.GraceNoteLayouts).X;
        Assert.True(grace.MainNoteX - grace.X > bareGap + 2.0,
            $"the diagram's box rods the grace → main gap: {bareGap:F3} → {grace.MainNoteX - grace.X:F3}");
        var gracedMeasure = graced.AllSystems[0].Measures[0];
        double cHeadX = gracedMeasure.X + gracedMeasure.Items[0].X;
        Assert.InRange(grace.X - cHeadX, 3.1, 3.3);
    }

    // ---- the twin ----

    private static (string Ly, string[] Warnings) Export(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var exporter = new LilyPondExporter();
        string ly = exporter.Export(tree);
        return (ly, exporter.Warnings.ToArray());
    }

    [Fact]
    public void TheTwin_WritesTheGraceSymbolAsAGraceInTheChordStream()
    {
        var (ly, warnings) = Export(Book("c'4@chord(C) grace { d'8@chord(D) } e'4@chord(E) c'2@chord(C) |"));
        Assert.Empty(warnings);
        Assert.Matches(@"mInlineChords = \\chordmode \{\s*c4 \\grace \{ d8 \} e4 c2 \|", ly);
        // …and the staff's grace stays bare: the symbol is the ChordNames context's.
        Assert.Matches(@"\\fixed c \{\s*\\time 4/4\s*c'4 \\grace \{ d'8 \} e'4 c'2 \|", ly);
        Assert.DoesNotContain("@chord", ly);
    }

    [Fact]
    public void TheTwin_WritesTheWholeRun_SilentColumnsIncluded()
    {
        var (ly, _) = Export(Book("c4@chord(C) grace { d8 e16@chord(D) } f4@chord(E) c2@chord(C) |"));
        Assert.Matches(@"c4 \\grace \{ s8 d16 \} e4 c2 \|", ly);
        var (unnamedMain, _) = Export(Book("c4@chord(C) grace { d8@chord(D) } e4 c2@chord(C) |"));
        Assert.Matches(@"c4 \\grace \{ d8 \} s4 c2 \|", unnamedMain);
    }

    [Fact]
    public void TheTwin_WritesTheGraceFigureAsAGraceInTheFigureStream_AndSaysLilyPondStacks()
    {
        var (ly, warnings) = Export(Book("c,4@figuredBass(5) grace { d,8@figuredBass(6) } e,4@figuredBass(3) c,2 |", "bass"));
        // The 3 lasts to the bar's end, as every last group does; the grace's 6 takes no time.
        Assert.Matches(@"mFigures = \\figuremode \{\s*<5>4 \\grace \{ <6>8 \} <3>2\. \|", ly);
        Assert.Contains(warnings, w => w.Contains("@figuredBass on a grace note"));
    }

    [Fact]
    public void TheTwin_WritesTheGraceDiagramOnTheGraceNote()
    {
        var (ly, warnings) = Export(Book("c4 grace { d8@diagram(x32010) } e4 c2 |"));
        Assert.Empty(warnings);
        // On the grace note itself, with \textLengthOn's two tweaks as on a full-size note.
        Assert.Matches(@"\\grace \{ d8-\\tweak extra-spacing-width[^}]*\^\\markup \\fret-diagram-terse ""x;3;2;o;1;o;"" \}", ly);
    }

    // ---- MusicXML ----

    [Fact]
    public void MusicXml_WritesTheHarmonyAndTheFiguresBeforeTheGraceNote()
    {
        var tree = SyntaxTree.Parse(Book("c4@chord(C) grace { d8@chord(D)@figuredBass(6) } e4@chord(E) c2 |"));
        var doc = new MusicXmlExporter().Export(tree).ToXml();
        var measure = doc.Descendants("measure").First();
        var kinds = measure.Elements().Select(e => e.Name.LocalName).ToList();
        // harmony(C) note(c) harmony(D) figured-bass note(grace d) harmony(E) note(e) note(c)
        int graceNote = measure.Elements("note").ToList().FindIndex(n => n.Element("grace") != null);
        Assert.True(graceNote >= 0, "a grace <note> is written");
        var before = measure.Elements().TakeWhile(e => !(e.Name.LocalName == "note" && e.Element("grace") != null)).ToList();
        Assert.Equal("figured-bass", before[^1].Name.LocalName);
        Assert.Equal("harmony", before[^2].Name.LocalName);
        Assert.Equal("D", before[^2].Element("root")!.Element("root-step")!.Value);
        Assert.Equal(3, doc.Descendants("harmony").Count());
    }
}
