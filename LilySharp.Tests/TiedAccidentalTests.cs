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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tie's right head does not print its accidental mid-line; it prints it, and is spaced for
/// it, only when the tie is broken by a line break — and the next head of that pitch in the
/// bar prints one whatever it is. LilyPond 2.26.0: lily/accidental-engraver.cc:352-379 and
/// :405-415 stop_translation_timestep (the tie on the Accidental, <c>'tied</c> in
/// localAlterations), lily/accidental.cc:102-117 remove_tied, lily/accidental-placement.cc:86-100
/// split_accidentals. Measured on the owner's China Grove (Xanadu) bars 28-30 and on
/// Lab sessions/p648 tiedacc (both pages and their LP twins).
/// </summary>
[Trait("Category", "Unit")]
public sealed class TiedAccidentalTests
{
    // A major: c is sharp in the key, so a written c prints a natural.
    private static string Book(string body) => $$"""
        octave absolute
        clef bass
        key a major
        time 4/4
        part melody
        section A { melody { {{body}} } }
        form main { ~A }
        score main { staff melody }
        """;

    private static MultiStaffScore Collect(string body)
    {
        var tree = SyntaxTree.Parse(Book(body));
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static RecordingDrawingContext Render(string body)
    {
        var score = Collect(body);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    private static NoteItem[] Notes(MultiStaffScore score, int measure)
        => score.StaffGroups[0].Staves[0].Voices[0].Measures[measure].Items.OfType<NoteItem>().ToArray();

    private static int Naturals(RecordingDrawingContext page)
        => page.Glyphs.Count(g => g.Glyph == EmmentalerGlyphs.AccidentalNatural);

    [Fact]
    public void MidLine_TheTiedHeadHidesItsAccidental_AndTheNextHeadPrintsOne()
    {
        var score = Collect("a,2 c2~ | c2 c2 |");
        var bar2 = Notes(score, 1);
        Assert.Null(bar2[0].Accidental);
        Assert.Equal("natural", bar2[0].LineStartAccidental);
        // The tied head left `tied` in the memory: the next c needs its natural again,
        // though the key's c-sharp was already cancelled in this bar on paper.
        Assert.Equal("natural", bar2[1].Accidental);
        Assert.Equal(2, Naturals(Render("a,2 c2~ | c2 c2 |")));
    }

    [Fact]
    public void ATieWithoutAnAccidental_ChangesNothing()
    {
        var score = Collect("a,2 cis2~ | cis2 cis2 |");
        var bar2 = Notes(score, 1);
        Assert.All(bar2, n => Assert.Null(n.Accidental));
        Assert.All(bar2, n => Assert.Null(n.LineStartAccidental));
    }

    [Fact]
    public void ATieBrokenByTheLine_BringsTheAccidentalBack_AndSpacesForIt()
    {
        // bar 1's natural, bar 2's reminder at the line start, bar 2's second c.
        var broken = Render("a,2 c2~ | break c2 c2 |");
        Assert.Equal(3, Naturals(broken));

        // Spaced as a written natural on the same head is: the line start's first head
        // stands where it would with an untied c2 (LineStartColumn.FirstNoteBoxes).
        var untied = Render("a,2 c2 | break c2 c2 |");
        Assert.Equal(FirstHeadOfSecondSystem(untied), FirstHeadOfSecondSystem(broken), 6);
    }

    [Fact]
    public void ACourtesyAccidentalIsForced_AndStaysOnTheTiedHead()
    {
        var score = Collect("a,2 c2~ | c2@courtesy c2 |");
        Assert.Equal("natural", Notes(score, 1)[0].Accidental);
        Assert.Null(Notes(score, 1)[0].LineStartAccidental);
    }

    // A `~` typed or deleted before a bar line — or the tied note's pitch changed under it —
    // changes the NEXT bar's accidental, outside the edit's own text. The resumed collect has
    // to re-walk that bar rather than splice the recorded one. MEASURED (session 648): the
    // splice re-walks the bar after the edited one (d2~ → c2~: 2 adopted, 36 spliced), so the
    // builder needed no checkpoint field of its own; these rows hold that reach.
    [Theory]
    [InlineData("c2 c2 | c2 c2 | a,2 c2 | c2 c2 |", "a,2 c2 |", "a,2 c2~ |")]
    [InlineData("c2 c2 | c2 c2 | a,2 c2~ | c2 c2 |", "a,2 c2~ |", "a,2 c2 |")]
    [InlineData("c2 c2 | c2 c2 | a,2 d2~ | c2 c2 |", "d2~", "c2~")]
    public void TypingOrDeletingATie_ReachesTheNextBarsAccidental_Incrementally(
        string body, string find, string replacement)
    {
        var options = new SvgRenderOptions { EmbedFont = false };
        var text = Book(string.Concat(Enumerable.Repeat(body + " ", 16))).Replace("\r\n", "\n");
        var tree = SyntaxTree.Parse(text);
        var compiler = new IncrementalCompiler(tree, options);
        compiler.RenderIncremental(tree);
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0);
        tree = tree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        text = text.Substring(0, at) + replacement + text.Substring(at + find.Length);
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), options), compiler.RenderIncremental(tree));
    }

    private static double FirstHeadOfSecondSystem(RecordingDrawingContext page)
    {
        var heads = page.Glyphs.Where(g => g.Glyph == EmmentalerGlyphs.NoteheadHalf).ToList();
        double firstY = heads[0].Y;
        return heads.Where(g => System.Math.Abs(g.Y - firstY) > 5).Min(g => g.X);
    }
}
