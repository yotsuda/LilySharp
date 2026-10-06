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
using LilySharp.Core;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>@niente</c> (HANDOFF §2 E ⑷, the owner's approval 2026-10-06): the silent dynamic. A
/// hairpin's THIN end that touches it — a decrescendo ending on it (al niente), a crescendo
/// starting from it (dal niente) — is drawn as LilyPond's circled tip and the niente is not
/// printed; a niente at the THICK end warns (LYS4029); alone it is the word, in italic.
/// </summary>
/// <remarks>
/// MEASURED (LilyPond 2.26.0, Lab sessions/p847/ni): the twin of the probe book draws the same
/// circles at the same tips, the shared circle of a mid-bar al/dal niente pair, and the italic
/// word, on the same two systems.
/// </remarks>
[Trait("Category", "Unit")]
public class NienteTests
{
    private static string Book(string measures) =>
        "octave absolute\n" +
        "part m { clef treble }\n" +
        $"section S {{ m {{ {measures} }} }}\n" +
        "form main { S }\n" +
        "score main { staff m }\n";

    private const string AlNiente = "c'4@mf@decresc d' e' f' | g'2 a'2@niente |";
    private const string DalNiente = "c'4@niente@cresc d' e' f' | g'1@f |";
    private const string Shared = "c'4@p@decresc d' e'@niente@cresc f' | g'1@f |";

    private static ScoreLayout LayoutOf(string measures)
    {
        var tree = SyntaxTree.Parse(Book(measures));
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static MultiStaffScore ScoreOf(string measures)
    {
        var tree = SyntaxTree.Parse(Book(measures));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void Niente_IsADynamicLevel()
    {
        var tree = SyntaxTree.Parse(Book(AlNiente));
        Assert.False(tree.HasErrors);
        Assert.Contains(tree.GetRoot().DescendantNodes<DynamicSyntax>(), d => d.Level == DynamicLevel.Niente);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TheThinEnd_IsCircled_AtAlAndDalNiente()
    {
        var al = Assert.Single(HairpinEngraver.DetectHairpins(ScoreOf(AlNiente).MusicMarks, ScoreOf(AlNiente).Dynamics));
        Assert.True(al.CircledTip);
        Assert.NotNull(al.NienteAtEnd);
        var dal = Assert.Single(HairpinEngraver.DetectHairpins(ScoreOf(DalNiente).MusicMarks, ScoreOf(DalNiente).Dynamics));
        Assert.True(dal.CircledTip);
        Assert.NotNull(dal.NienteAtStart);
        Assert.False(al.TipAdjacent || dal.TipAdjacent);
    }

    [Fact]
    public void TheThickEnd_IsNotCircled_AndWarns()
    {
        const string thick = "c'4@pp@cresc d' e' f'@niente | g'4@niente@decresc a' b' c''@p |";
        var score = ScoreOf(thick);
        var hairpins = HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics);
        Assert.Equal(2, hairpins.Length);
        Assert.All(hairpins, h => Assert.False(h.CircledTip));
        Assert.All(hairpins, h => Assert.NotNull(h.NienteAtThickEnd));
        var found = SemanticValidation.Run(SyntaxTree.Parse(Book(thick)))
            .Where(d => d.Code == DiagnosticCodes.NienteAtThickEnd).ToList();
        Assert.Equal(2, found.Count);
        Assert.Contains(found, d => d.Message.Contains("ends a crescendo"));
        Assert.Contains(found, d => d.Message.Contains("starts a decrescendo"));
        Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Book(AlNiente + " " + DalNiente))),
            d => d.Code == DiagnosticCodes.NienteAtThickEnd);
    }

    /// <summary>LILYPOND-REF: lily/hairpin.cc:323-355 add_at_edge — the arms leave 2·rad and the circle is
    /// set at their edge (the line box is widened by th / 2): a decrescendo's circle centre is
    /// its arms' end + th/2 + rad, a crescendo's its arms' start − th/2 − rad.</summary>
    [Fact]
    public void TheCircle_StandsAtTheArmsEdge()
    {
        double rad = HairpinEngraver.CircleRadius, th = HairpinEngraver.CircleThickness;
        Assert.Equal(0.6666 * 0.525, rad, 12);
        var al = Assert.Single(LayoutOf(AlNiente).HairpinLayouts);
        Assert.Equal(al.EndX + th / 2 + rad, al.CircleX, 9);
        var dal = Assert.Single(LayoutOf(DalNiente).HairpinLayouts);
        Assert.Equal(dal.StartX - th / 2 - rad, dal.CircleX, 9);
        // A plain hairpin has none.
        Assert.All(LayoutOf("c'4@p@cresc d' e' f' | g'1@f |").HairpinLayouts, h => Assert.True(double.IsNaN(h.CircleX)));
    }

    /// <summary>LILYPOND-REF: lily/hairpin.cc:243-252 x_points — back-to-back hairpins on one column with
    /// circled tips hang at the column centre ± (rad − th/2), so the two circles coincide.</summary>
    [Fact]
    public void AlAndDalNiente_OnOneNote_ShareOneCircle()
    {
        var score = ScoreOf(Shared);
        Assert.All(HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics), h => Assert.True(h.TipAdjacent));
        var pins = LayoutOf(Shared).HairpinLayouts;
        Assert.Equal(2, pins.Length);
        Assert.Equal(pins[0].CircleX, pins[1].CircleX, 9);
        // At a bar's head the decrescendo ends on the bar line, not the column: two circles.
        var head = ScoreOf("c'4@p@decresc d' e' f' | g'4@niente@cresc a' b' c'' | d''1@ff |");
        Assert.All(HairpinEngraver.DetectHairpins(head.MusicMarks, head.Dynamics), h => Assert.False(h.TipAdjacent));
    }

    [Fact]
    public void ACircledNiente_IsNotPrinted_ALoneOneIsTheItalicWord()
    {
        var layout = LayoutOf(AlNiente + " " + DalNiente);
        Assert.DoesNotContain(layout.DynamicLayouts, d => d.Text == "niente");
        var lone = LayoutOf("c'4@niente d' e' f' |");
        var word = Assert.Single(lone.DynamicLayouts);
        Assert.Equal("niente", word.Text);
        Assert.True(word.IsExpressiveText);
    }

    /// <summary>The pairing's own regression (session 847): a dynamic standing before the next
    /// hairpin mark in the same bar ends the hairpin. Both hairpins used to vanish.</summary>
    [Fact]
    public void ADynamicBeforeTheNextMarkInTheSameBar_EndsTheHairpin()
    {
        var pins = LayoutOf("c'4@p@cresc d'@f e'@decresc f' | g'1@p |").HairpinLayouts;
        Assert.Equal(2, pins.Length);
    }

    [Fact]
    public void TheTwin_WritesTheCircledTip_AndEndsWithTheStopEvent()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book(AlNiente + " " + DalNiente + " " + Shared)));
        Assert.Contains("-\\tweak circled-tip ##t \\>", ly);
        Assert.Contains("-\\tweak circled-tip ##t \\<", ly);
        Assert.Contains("a'2\\!", ly);
        Assert.Contains("e'\\!-\\tweak circled-tip ##t \\<", ly);
        Assert.DoesNotContain("niente", ly.Replace("title = \"", ""));
        string lone = new LilyPondExporter().Export(SyntaxTree.Parse(Book("c'4@niente d' e' f' |")));
        Assert.Contains("-\\markup { \\italic \"niente\" }", lone);
    }

    [Fact]
    public void MusicXml_CarriesTheWedgesNiente_AndReadsItBack()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(AlNiente + " " + DalNiente))).ToXml().ToString();
        Assert.Contains("<wedge type=\"stop\" niente=\"yes\" />", xml);
        Assert.Contains("<wedge type=\"crescendo\" niente=\"yes\" />", xml);
        Assert.DoesNotContain("<n />", xml);
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(lys, "@niente").Count);
        string lone = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("c'4@niente d' e' f' |"))).ToXml().ToString();
        Assert.Contains("<n />", lone);
    }

    [Fact]
    public void Midi_PlaysNienteAtTheQuietestVelocity()
    {
        var notes = new MidiExporter().Export(SyntaxTree.Parse(Book("c'4@niente d' e'@p f' |"))).Tracks
            .SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ToList();
        Assert.Equal(new[] { 1, 1, 50, 50 }, notes.Select(n => n.Velocity).ToArray());
    }
}
