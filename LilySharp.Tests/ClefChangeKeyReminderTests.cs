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
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// After a clef change in the middle of a line, a note its key alters writes the key's sharp
/// or flat out — once a bar and note name, to the end of the line (owner's decision
/// 2026-10-14: the key signature at the line's start stands on the OLD clef's lines, so the
/// player reads the F♯ of D major off the wrong line; a staff in the line's own clef
/// confuses no one, and the next line's key signature is drawn in the new clef).
/// MeasureCollector.KeyCourtesyDue; SvgGenerator.CollectAndLayout collects twice for it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClefChangeKeyReminderTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Book(string music) => $$"""
        key d major
        part m { clef treble }
        section A { m { {{music}} } }
        form { A }
        score { staff m }
        """;

    /// <summary>Each bar's notes' accidentals ("-" for none), bars joined by " | ".</summary>
    private static string Accidentals(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        var (score, _) = SvgGenerator.CollectAndLayout(tree, RenderSpecParser.FindAll(tree).FirstOrDefault());
        var voice = score.EnumerateStaves().First().Staff.Voices[0];
        return string.Join(" | ", voice.Measures.Select(m =>
            string.Join(" ", m.Items.OfType<NoteItem>().Select(n => n.Accidental ?? "-"))));
    }

    [Fact]
    public void AfterAMidLineClefChange_TheKeysSharpIsWrittenOut()
        // The owner's book: the f' of bar 1 is a natural, then tenor clef and fis.
        => Assert.Equal("natural - - natural | sharp", Accidentals("c'4 d e f | clef tenor fis,,1 |"));

    [Fact]
    public void WithoutAClefChange_NothingIsAdded_NotEvenAfterANaturalInTheBarBefore()
        // A bar line resets the accidentals: the next bar's F♯ is the key's, written bare.
        => Assert.Equal("natural - - natural | -", Accidentals("c'4 d e f | fis1 |"));

    [Fact]
    public void AClefChangeAtTheStartOfALine_IsDrawnWithTheKeySignature_AndAddsNothing()
        => Assert.Equal("natural - - natural | -", Accidentals("c'4 d e f | break clef tenor fis,,1 |"));

    [Fact]
    public void OnceABarAndNoteName_UntilTheLineEnds()
        => Assert.Equal("natural - - natural | sharp - - - | sharp | -",
            Accidentals("c'4 d e f | clef tenor fis,,4 fis fis fis | fis1 | break fis1 |"));

    [Fact]
    public void BackInTheLinesOwnClef_NothingIsAdded()
        => Assert.Equal("natural - - natural | sharp | -",
            Accidentals("c'4 d e f | clef tenor fis,,1 | clef treble fis''1 |"));

    [Fact]
    public void ANoteTheKeyDoesNotAlter_GetsNothing()
        => Assert.Equal("natural - - natural | -", Accidentals("c'4 d e f | clef tenor e1 |"));

    /// <summary>Where the LAYOUT breaks the line (no `break` written): the bars of the line
    /// the clef changed in write the sharp, the bars of the lines after it do not — the
    /// second collect reads the first layout's line starts.</summary>
    [Fact]
    public void WhereTheLayoutBreaksTheLine_TheReminderStopsThere()
    {
        string music = "c'4 d e f | clef tenor " + string.Join(" | ", Enumerable.Repeat("fis,,4 e d e", 1))
            + " | " + string.Join(" | ", Enumerable.Repeat("fis4 e d e", 30)) + " |";
        var tree = SyntaxTree.Parse(Book(music));
        var (score, layout) = SvgGenerator.CollectAndLayout(tree, RenderSpecParser.FindAll(tree).FirstOrDefault());
        var starts = SvgGenerator.LineStarts(layout);
        Assert.True(starts.Length >= 2, "the book must run onto a second line");
        var voice = score.EnumerateStaves().First().Staff.Voices[0];
        for (int m = 1; m < voice.Measures.Length; m++)
        {
            var fis = voice.Measures[m].Items.OfType<NoteItem>().First();
            Assert.Equal(m < starts[1] ? "sharp" : null, fis.Accidental);
        }
    }

    [Fact]
    public void ThePreviewDrawsWhatTheFullRenderDraws()
    {
        string text = Book("c'4 d e f | clef tenor fis,,4 fis fis fis | fis1 | e1 |");
        var session = new IncrementalCompiler(SyntaxTree.Parse(text), Opt);
        string preview = session.Render().Replace("\r\n", "\n");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n"), preview);
        // …and after an edit too.
        var edited = text.Replace("e1 |", "g1 |");
        int at = text.IndexOf("e1 |", System.StringComparison.Ordinal);
        string after = session.Edit(new TextChange(new TextSpan(at, 1), "g")).Replace("\r\n", "\n");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(edited), Opt).Replace("\r\n", "\n"), after);
    }
}
