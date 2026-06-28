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
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Editor completion inside a <c>structure { }</c> block offers section names and
/// navigation marks, never note names.
/// </summary>
[Trait("Category", "Unit")]
public class StructureCompletionTests
{
    private const string Doc = """
        part m {
          section Intro { c4 d e f | }
          section Verse { g4 a b c | }
        }
        structure { Intro segno Verse to coda }
        score "x" { staff m }
        """;

    [Fact]
    public void InsideStructureBlock_IsDetected()
    {
        int offset = Doc.IndexOf("Intro segno") + "Intro ".Length; // inside structure { }
        Assert.Equal("structure", LilySharpLanguageServer.InnermostOpenBlock(Doc, offset));
    }

    [Fact]
    public void InsideSectionMusicBlock_IsNotStructure()
    {
        // Inside a section's music block the innermost token is the section NAME,
        // so it is not "structure" and routes to music (note) completions.
        int offset = Doc.IndexOf("c4 d");
        Assert.NotEqual("structure", LilySharpLanguageServer.InnermostOpenBlock(Doc, offset));
    }

    [Fact]
    public void StructureCompletions_OfferSectionNamesAndNavMarks()
    {
        var labels = LilySharpLanguageServer.GetStructureCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        Assert.Contains("Intro", labels);   // declared section names
        Assert.Contains("Verse", labels);
        Assert.Contains("segno", labels);   // navigation marks
        Assert.Contains("to coda", labels);
        Assert.Contains("ds al coda", labels);
    }

    [Fact]
    public void StructureCompletions_OfferRepeatVoltaAndOtherSyntax()
    {
        var labels = LilySharpLanguageServer.GetStructureCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        Assert.Contains("|:", labels);     // repeat barlines
        Assert.Contains(":|", labels);
        Assert.Contains("[1. ]", labels);  // volta brackets
        Assert.Contains("[2. ]", labels);
        Assert.Contains("~", labels);      // silent section prefix
        Assert.Contains("_\"\"", labels);  // custom text
    }

    [Fact]
    public void StructureCompletions_OfferNoNoteNames()
    {
        var labels = LilySharpLanguageServer.GetStructureCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }
}
