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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// `@p.subito` (owner's spelling 2026-10-13, grammar feedback B9): the level printed with an
/// italic "sub." hanging left of it, sounding as the plain level. MEASURED (LilyPond 2.26.0, Lab
/// sessions/p903/b9): LilyPond's make-dynamic-script of `#:italic "sub." #:dynamic "p"` keeps the
/// p where a plain \p stands, and the twin writes exactly that.
/// </summary>
[Trait("Category", "Unit")]
public class SubitoDynamicTests
{
    private static string Book(string dynamic) =>
        "octave absolute\npart m { clef treble }\nsection A { m { c'4@f d' e' f' | g'4" + dynamic
        + " a' b' c'' | } }\nform { A }\nscore { staff m }";

    private static DynamicLayout Second(string dynamic)
    {
        var tree = SyntaxTree.Parse(Book(dynamic));
        var layout = new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
        return layout.DynamicLayouts.Single(d => d.MeasureIndex == 1);
    }

    [Theory]
    [InlineData("@p.subito", false, null)]
    [InlineData("@ff.subito.up", false, true)]
    public void ItParses_AsASubitoLevel(string dynamic, bool errors, bool? above)
    {
        var tree = SyntaxTree.Parse(Book(dynamic));
        Assert.Equal(errors, tree.Diagnostics.Concat(SemanticValidation.Run(tree)).Any(d => d.Severity == DiagnosticSeverity.Error));
        var d = tree.GetRoot().DescendantNodes<DynamicSyntax>().Single(x => x.IsSubito);
        Assert.Equal(above, d.ForcedAbove);
        Assert.Equal(tree.GetRoot().ToFullString(), Book(dynamic));
    }

    [Fact]
    public void TheLevel_StandsAboveItsNoteAsThePlainOne_AndTheWordHangsLeft()
    {
        var subito = Second("@p.subito");
        var plain = Second("@p");
        Assert.True(subito.IsSubito);
        Assert.Equal(plain.X, subito.X, 6);
        // The word's own ink clears the staff (the bar line under it) as the whole stencil does in
        // LilyPond, so the line sits a little lower. MEASURED (2.26.0, Lab sessions/p903/b9
        // lpsvg): the subito p's baseline 7.9536 against the plain p's 7.5940 — 0.36 lower.
        Assert.InRange(plain.YUp - subito.YUp, 0.36 - 0.1, 0.36 + 0.1);
        var fonts = LilySharp.Core.Rendering.ScoreTextMetrics.Bundled;
        var (left, right) = DynamicEngraver.LabelExtentX(fonts, subito);
        var (plainLeft, plainRight) = DynamicEngraver.LabelExtentX(fonts, plain);
        Assert.Equal(plainRight, right, 6);
        Assert.True(left < plainLeft - 1.5, $"the word reaches only {plainLeft - left:F2} left of the level");
    }

    [Fact]
    public void ThePage_DrawsTheWord()
    {
        var tree = SyntaxTree.Parse(Book("@p.subito"));
        string svg = SvgGenerator.Generate(tree);
        Assert.Contains(">sub.<", svg);
        Assert.DoesNotContain(">sub.<", SvgGenerator.Generate(SyntaxTree.Parse(Book("@p"))));
    }

    [Fact]
    public void TheTwin_WritesLilyPondsDynamicScript()
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(Book("@p.subito")));
        Assert.Contains("g'4-#(make-dynamic-script (markup #:normal-text #:italic \"sub.\" #:dynamic \"p\"))", ly);
        Assert.Contains("^#(make-dynamic-script", new LilyPondExporter().Export(SyntaxTree.Parse(Book("@ff.subito.up"))));
    }

    [Fact]
    public void MusicXml_PutsTheWordBeforeTheDynamicInOneDirection()
    {
        string xml = new LilySharp.Core.MusicXml.MusicXmlExporter().Export(SyntaxTree.Parse(Book("@p.subito"))).ToXml().ToString();
        Assert.Matches(@"<words font-style=""italic"">sub\.</words>\s*</direction-type>\s*<direction-type>\s*<dynamics>\s*<p />", xml);
    }

    [Fact]
    public void It_SoundsAsThePlainLevel()
    {
        int Velocity(string dynamic) => new MidiExporter().Export(SyntaxTree.Parse(Book(dynamic)))
            .Tracks.SelectMany(t => t.Notes).OrderBy(n => n.StartTick).ElementAt(4).Velocity;
        Assert.Equal(Velocity("@p"), Velocity("@p.subito"));
    }
}
