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
/// A clef change in the middle of a line leaves the key alone: the notes the key alters write no
/// sign, as LilyPond and the engraving convention have it (OMR's report, REQUESTS #23, owner's
/// decision 2026-11-09). The rule of 2026-10-14 that wrote them out is withdrawn.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClefChangeKeepsTheKeyTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Book(string music, string key = "d major") => $$"""
        key {{key}}
        part m { clef treble }
        section A { m { {{music}} } }
        form { A }
        score { staff m }
        """;

    /// <summary>Each bar's notes' accidentals ("-" for none), bars joined by " | ".</summary>
    private static string Accidentals(string music, string key = "d major")
    {
        var tree = SyntaxTree.Parse(Book(music, key));
        var (score, _) = SvgGenerator.CollectAndLayout(tree, RenderSpecParser.FindAll(tree).FirstOrDefault());
        var voice = score.EnumerateStaves().First().Staff.Voices[0];
        return string.Join(" | ", voice.Measures.Select(m =>
            string.Join(" ", m.Items.OfType<NoteItem>().Select(n => n.Accidental ?? "-"))));
    }

    [Fact]
    public void AfterAMidLineClefChange_TheKeysNoteWritesNoSign()
        => Assert.Equal("natural - - natural | -", Accidentals("c'4 d e f | clef tenor fis,,1 |"));

    [Fact]
    public void InsideTheBarOfTheClefChange_TooAndForEveryNoteName()
        => Assert.Equal("- - - -", Accidentals("g,4 clef treble bes'4 ees''4 bes'4 |", "bes major"));

    [Fact]
    public void OnTheBarsAfter_AndBackInTheFirstClef()
        => Assert.Equal("- | - | -", Accidentals("clef tenor fis,1 | fis1 | clef treble fis''1 |"));

    [Fact]
    public void ARealAccidentalAfterTheClef_IsStillDrawn()
        => Assert.Equal("natural | sharp", Accidentals("clef tenor f,1 | clef treble gis''1 |"));

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
