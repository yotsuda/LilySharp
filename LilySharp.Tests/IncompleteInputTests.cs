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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A book the writer has not finished typing is still a book to the editor: its preview
/// validates and renders on every keystroke, so no half-written directive may throw out of a
/// reader — the parser's error is the whole answer. Found by cutting 211 books at 40 points
/// each (session 716, LilySharp-Lab sessions/p716/fuzz.txt: 8,398 cuts, two distinct throws,
/// both a bare <c>key</c>, whose empty tonic the collector read as <c>PitchName[0]</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class IncompleteInputTests
{
    [Theory]
    [InlineData("key")]                                                   // a file's first word
    [InlineData("part m { clef bass }\nsection S { m { c1 | } }\nsection F {\n  key ")]
    [InlineData("part m { clef bass }\nsection S { m { c1 | } }\nsection F { key \n m { c1 | } }\nform main { S F }\nscore main { staff m }")]
    [InlineData("part m { clef bass\n key }\nsection S { m { c1 | } }\nform main { S }\nscore main { staff m }")]
    public void AKeyWithNothingAfterIt_IsTheParsersErrorAlone(string src)
    {
        var tree = SyntaxTree.Parse(src);
        Assert.NotEmpty(tree.Diagnostics);
        _ = SemanticValidation.Run(tree).ToList();
        _ = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });   // empty when no score is written yet
        _ = new MidiExporter().Export(tree);
        _ = new MusicXmlExporter().Export(tree).ToXml().ToString();
        _ = new LilyPondExporter().Export(tree);
    }

    private static string Book(string music) =>
        "part m { clef treble }\nsection S { m { " + music + " } }\nform main { S }\nscore main { staff m }\n";

    private static void EveryReader(SyntaxTree tree)
    {
        _ = SemanticValidation.Run(tree).ToList();
        Assert.NotEmpty(SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false }));
        _ = new MidiExporter().Export(tree);
        _ = new MusicXmlExporter().Export(tree).ToXml().ToString();
        _ = new LilyPondExporter().Export(tree);
    }

    /// <summary>
    /// A REST in a grace group is a column with no head (GraceColumnInfo.IsRest), and two
    /// readers asked it for its highest head: the script gate on the main note and the slur's
    /// obstacles. Both books are VALID — no diagnostic — and the page threw on them until
    /// session 717 (found by the edit sweep, LilySharp-Lab sessions/p717).
    /// </summary>
    [Theory]
    [InlineData("c'4 grace { r16 d'16 } e'4@staccato f' g' |")]
    [InlineData("c'4 grace { r16 } e'4@fermata f' g' |")]
    [InlineData("c'4( grace { r16 d'16 } e'4) f' g' |")]
    [InlineData("c'4 grace { d'16 e'16 r16 } e'4@staccato f' g' |")]
    public void AGraceRest_IsReadAsTheRestItIs(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Empty(tree.Diagnostics);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Severity == DiagnosticSeverity.Error);
        EveryReader(tree);
    }

    /// <summary>
    /// A duration that is not a note value (<c>c'3</c>, or a chord shape's frets read as
    /// durations while a line is typed) is LYS1004 — and only that: every reader takes the next
    /// longer note value, where 1/3 + 1/5 + 1/7 + … overflowed a long in all of them and the
    /// check printed an exception instead of the error (session 717).
    /// </summary>
    [Theory]
    [InlineData("c'3 d'5 e'7 f'11 g'13 a'17 b'19 c''23 d''29 e''31 f''37 g''41 |")]
    [InlineData("c'133211 d'320001 |")]
    public void ADurationThatIsNoNoteValue_IsLys1004Alone(string music)
    {
        var tree = SyntaxTree.Parse(Book(music));
        Assert.Contains(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.InvalidDuration);
        EveryReader(tree);
    }
}
