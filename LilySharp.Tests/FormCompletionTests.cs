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
/// Editor completion inside a <c>form Name { }</c> block offers section names and
/// navigation marks, never note names.
/// </summary>
[Trait("Category", "Unit")]
public class FormCompletionTests
{
    private const string Doc = """
        part m {
          section Intro { c4 d e f | }
          section Verse { g4 a b c | }
        }
        form main { Intro segno Verse to coda }
        score main { staff m }
        """;

    [Fact]
    public void InsideFormBlock_IsDetected()
    {
        int offset = Doc.IndexOf("Intro segno") + "Intro ".Length; // inside form main { }
        Assert.Equal(LilySharpLanguageServer.CompletionContext.FormBlock,
            LilySharpLanguageServer.GetCompletionContext(Doc, offset));
    }

    [Fact]
    public void InsideSectionMusicBlock_IsNotFormBody()
    {
        // Inside a section's music block the completions route to music (note)
        // names, not the form body's section/navigation list.
        int offset = Doc.IndexOf("c4 d");
        Assert.NotEqual(LilySharpLanguageServer.CompletionContext.FormBlock,
            LilySharpLanguageServer.GetCompletionContext(Doc, offset));
    }

    [Fact]
    public void StructureCompletions_OfferSectionNamesAndNavMarks()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
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
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        Assert.Contains("|:", labels);     // repeat barlines
        Assert.Contains(":|", labels);
        Assert.Contains("[1. ]", labels);  // volta brackets
        Assert.Contains("[2. ]", labels);
        Assert.Contains("_\"\"", labels);  // custom text

        // Silent sections are offered with the name attached (~Intro), not bare ~.
        Assert.Contains("~Intro", labels);
        Assert.Contains("~Verse", labels);
        Assert.DoesNotContain("~", labels);
    }

    [Fact]
    public void StructureCompletions_OfferNoNoteNames()
    {
        var labels = LilySharpLanguageServer.GetFormCompletions(Doc).Items
            .Select(i => i.Label).ToHashSet();

        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }
}
