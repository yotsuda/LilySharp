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

using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// In a volta ending the tilde binds to the SECTION NAME, so it hides the section LABEL —
/// the same thing a plain <c>~Name</c> hides. The bracket, its number and its caps belong
/// to the ending and are not the tilde's to take.
/// </summary>
/// <remarks>
/// <para>
/// The grammar says it in the production: <c>StructureVolta = '[' , Integer ,
/// [ ( '-' | ',' ) , Integer ] , '.' , [ '~' ] , Identifier , [ ']' ]</c> — the optional
/// tilde stands immediately before the Identifier, exactly as in the plain item
/// <c>'~' , Identifier , [ String ]</c> whose comment reads "same section, label hidden".
/// </para>
/// <para>
/// ⚠️ IT WAS INVERTED INSIDE A REPEAT, AND ONLY THERE (user report,
/// scratch/ベースタブLy/repeat-disappear.lys, 2026-08-25): <c>form main { A |: [1. ~B :| }</c>
/// printed B's label and drew no ending at all. <c>MeasureCollector.Form.cs</c>'s in-repeat
/// arm wrote the label unconditionally and gated the BRACKET on <c>IsSilent</c> — both
/// halves the wrong way round — while the three other page readers
/// (<c>MeasureCollector.cs</c>'s outside-a-repeat arm and its two resume arms) had always
/// read it correctly and <see cref="FormVoltaWithoutRepeatTests"/> pinned one of them.
/// </para>
/// <para>
/// ⚠️ AND IT CROSSED THE OUTPUT BOUNDARY BY CITATION, which is why this file tests all
/// three outputs rather than the page alone (HANDOFF 5.0: a quantity with N outputs cannot
/// be guarded by one output's net). Both <c>LilyPondExporter</c> sites said in comments that
/// they "mirror MeasureCollector.Form.cs's alternative arm", and mirrored the broken one;
/// <c>MusicXmlExporter</c>'s doc said it suppressed the bracket "as the engraving does".
/// Three readers, one of them right.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class FormVoltaSilentLabelTests
{
    private const string Head = """
        part melody {
          section A { c'4 d e f | g2 g | }
          section B { c'4 d e f | g2 g | }
        }
        """;

    private static string Source(string form) =>
        $"{Head}\n\nform main {{ {form} }}\n\nscore main {{\n  staff melody\n}}\n";

    private static string Svg(string form)
    {
        var tree = SyntaxTree.Parse(Source(form));
        Assert.False(tree.HasErrors, string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        return SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
    }

    private static string[] Texts(string svg)
        => Regex.Matches(svg, "<text[^>]*>([^<]+)</text>")
            .Select(m => m.Groups[1].Value).ToArray();

    /// <summary>
    /// The volta bracket's horizontal span, or null when no bracket is drawn. Found without
    /// an X model or a Y literal: a staff line is the widest horizontal stroke there is, and
    /// the bracket is the horizontal stroke ABOVE the topmost of them.
    /// </summary>
    private static (double Left, double Right)? Bracket(string svg)
    {
        var lines = Regex.Matches(svg,
                "<line x1=\"(-?[0-9.]+)\" y1=\"(-?[0-9.]+)\" x2=\"(-?[0-9.]+)\" y2=\"(-?[0-9.]+)\"")
            .Select(m => (
                X1: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Y1: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                X2: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                Y2: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToList();
        var horizontals = lines.Where(l => l.Y1 == l.Y2).ToList();
        if (horizontals.Count == 0) return null;
        // ⚠️ "WIDE" IS NOT ENOUGH TO MEAN "STAFF LINE" — the bracket's own bar is wide too,
        // and taking it for a staff line made this helper answer null on correct output.
        // A staff line runs the whole system, so it is the WIDEST there is, and all five
        // share that width exactly.
        double staffWidth = horizontals.Max(l => l.X2 - l.X1);
        double topStaffLine = horizontals
            .Where(l => l.X2 - l.X1 > staffWidth - 0.001).Min(l => l.Y1);
        var bar = horizontals.Where(l => l.Y1 < topStaffLine - 0.5)
            .OrderBy(l => l.Y1).FirstOrDefault();
        return bar == default ? null : (bar.X1, bar.X2);
    }

    // ── the report ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reported book: inside a repeat, <c>~</c> hides the label and leaves the ending.
    /// </summary>
    [Fact]
    public void InsideARepeat_TheTildeHidesTheLabelAndKeepsTheEnding()
    {
        var texts = Texts(Svg("A |: [1. ~B :|"));
        Assert.Contains("1.", texts);
        Assert.DoesNotContain("B", texts);
        Assert.Contains("A", texts);          // the untilded section still speaks
        Assert.NotNull(Bracket(Svg("A |: [1. ~B :|")));
    }

    /// <summary>
    /// …and the tilde is not a no-op: without it the label is there. Without this arm the
    /// one above passes on a build that draws no labels at all.
    /// </summary>
    [Fact]
    public void InsideARepeat_WithoutTheTilde_TheLabelIsDrawn()
    {
        var texts = Texts(Svg("A |: [1. B :|"));
        Assert.Contains("1.", texts);
        Assert.Contains("B", texts);
    }

    // ── the differential net ───────────────────────────────────────────────────────────

    /// <summary>
    /// THE CLAIM, WRITTEN WITHOUT AN EXPECTED VALUE (HANDOFF 7.7): the tilde changes the
    /// LABEL and nothing else about the ending, so the bracket a tilded ending draws is the
    /// bracket an untilded one draws — same span, same number, same cap.
    /// </summary>
    /// <remarks>
    /// A differential arm survives what a literal cannot: it does not need to know where the
    /// bracket goes, only that the tilde does not move it. Both the open form and the closed
    /// <c>]</c> form are asked, because the cap is the other thing a reader might mistake the
    /// tilde for.
    /// </remarks>
    [Theory]
    [InlineData("A |: [1. B :|", "A |: [1. ~B :|")]
    [InlineData("A |: [1. B] :|", "A |: [1. ~B] :|")]
    public void TheTildeMovesTheLabelOnly_NotTheEnding(string plain, string tilded)
    {
        var plainSvg = Svg(plain);
        var tildedSvg = Svg(tilded);

        var a = Bracket(plainSvg);
        var b = Bracket(tildedSvg);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.Value.Left, b!.Value.Left, 6);
        Assert.Equal(a.Value.Right, b.Value.Right, 6);

        // The label is the ONE text that differs.
        var gone = Texts(plainSvg).ToList();
        foreach (var t in Texts(tildedSvg)) gone.Remove(t);
        Assert.Equal(new[] { "B" }, gone.ToArray());
    }

    // ── the other two outputs ──────────────────────────────────────────────────────────

    /// <summary>
    /// The LilyPond twin drops the ending's <c>\mark</c> and keeps its <c>\alternative</c>.
    /// </summary>
    [Fact]
    public void TheTwin_DropsTheMarkAndKeepsTheAlternative()
    {
        string plain = new LilyPondExporter().Export(SyntaxTree.Parse(Source("A |: [1. B :|")));
        string tilded = new LilyPondExporter().Export(SyntaxTree.Parse(Source("A |: [1. ~B :|")));

        Assert.Contains("\\alternative", plain);
        Assert.Contains("\\alternative", tilded);
        Assert.Contains("\\box \"B\"", plain);
        Assert.DoesNotContain("\\box \"B\"", tilded);
        Assert.Contains("\\box \"A\"", tilded);
    }

    /// <summary>
    /// MusicXML is INDIFFERENT to the tilde, and that is the right answer rather than a gap:
    /// the tilde hides a section label and this exporter writes none, so the two books are
    /// the same document.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ARM IS WHERE THE DEFECT CROSSED OVER. Until 2026-08-25 the exporter dropped
    /// the ending's <c>&lt;ending&gt;</c> elements for a tilded ending, on the stated grounds
    /// that it was matching the engraving — so a `~` silently changed the exported REPEAT
    /// STRUCTURE of the piece, which no reading of the grammar asks for.
    /// </remarks>
    [Fact]
    public void MusicXml_ReadsTheTwoBooksAsOneDocument()
    {
        string Xml(string form) => new MusicXmlExporter()
            .Export(SyntaxTree.Parse(Source(form))).ToXml().ToString();

        Assert.Equal(Xml("A |: [1. B :|"), Xml("A |: [1. ~B :|"));
        // …and the endings are actually there, so the equality above is not two blanks.
        Assert.Contains("<ending", Xml("A |: [1. ~B :|"));
    }
}
