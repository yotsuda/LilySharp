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
}
