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
/// A singleton global — metadata (title/composer/font/version) or a piece-wide default
/// (time/key/tempo/octave) — is dropped from the top-level completion once the file already
/// declares it at the global scope, so it is never offered twice. Duplicable keywords
/// (override, part, section, …) are always offered.
/// </summary>
[Trait("Category", "Unit")]
public class TopLevelSingletonCompletionTests
{
    private static string[] Labels(string text)
        => LilySharpLanguageServer.GetTopLevelCompletions(text).Items.Select(i => i.Label!).ToArray();

    [Fact]
    public void SingletonsAlreadyPresent_AreNotOfferedAgain()
    {
        var labels = Labels("title \"Song\"\ncomposer \"Me\"\ntime 4/4\nkey c major\ntempo 120\n");
        foreach (var kw in new[] { "title", "composer", "time", "key", "tempo" })
            Assert.DoesNotContain(kw, labels);
    }

    [Fact]
    public void SingletonsAbsent_AreStillOffered()
    {
        var labels = Labels("part melody { }\n");
        foreach (var kw in new[] { "title", "composer", "time", "key" })
            Assert.Contains(kw, labels);
    }

    [Fact]
    public void DuplicableKeywords_AreAlwaysOffered()
    {
        // `override` is per-grob; `part` / `section` recur — none is a singleton.
        var labels = Labels("override NoteHead.color = red\npart melody { }\n");
        Assert.Contains("override", labels);
        Assert.Contains("part", labels);
        Assert.Contains("section", labels);
    }

    [Fact]
    public void KeywordInsideABlockStringOrComment_DoesNotCountAsGlobal()
    {
        // `time` inside a part block (not global), `key` in a comment, and the words in the
        // composer STRING are not global declarations — so time / key stay offered; only the
        // real global `composer` is dropped.
        var labels = Labels("part melody { time 3/4 }\n// key c major\ncomposer \"key and time\"\n");
        Assert.Contains("time", labels);
        Assert.Contains("key", labels);
        Assert.DoesNotContain("composer", labels);
    }

    [Fact]
    public void ParameterlessForm_DoesNoFiltering()
    {
        // Other tests call the no-arg overload; it must return the full list.
        var labels = LilySharpLanguageServer.GetTopLevelCompletions().Items.Select(i => i.Label!).ToArray();
        Assert.Contains("title", labels);
        Assert.Contains("time", labels);
    }

    // ----- part-major with no top-level section yet: known section names at the top level -----

    [Fact]
    public void PartMajorNoGlobalSection_OffersKnownSectionNamesAtTopLevel()
    {
        var text = "part melody { section A { c } section B { d } }\nform main { A B }\n";
        var items = LilySharpLanguageServer.GetTopLevelCompletions(text, text.Length).Items;
        var labels = items.Select(i => i.Label!).ToArray();
        Assert.Contains("section A", labels);
        Assert.Contains("section B", labels);
        // A top-level section sits at column 0 (nest = ""); caret between the braces.
        Assert.Equal("section A {\n\t$0\n}", items.Single(i => i.Label == "section A").InsertText);
    }

    [Fact]
    public void WithAGlobalSectionAlready_TopLevelHasNoSectionNameScaffolds()
    {
        // A top-level (section-major) section exists → the `section` keyword + after-`section`
        // list handle it; the name scaffolds are not added (they would re-offer declared names).
        var text = "section A { melody { c } }\nform main { A B }\n";
        var labels = LilySharpLanguageServer.GetTopLevelCompletions(text, text.Length)
            .Items.Select(i => i.Label!).ToArray();
        Assert.DoesNotContain("section A", labels);
        Assert.DoesNotContain("section B", labels);
        Assert.Contains("section", labels);   // the bare keyword stays
    }

    [Fact]
    public void NoKnownSections_AddsNoScaffolds()
    {
        // No parts / form → nothing known → just the fixed top-level list (with `section`).
        var labels = LilySharpLanguageServer.GetTopLevelCompletions("part melody { }\n", 16)
            .Items.Select(i => i.Label!).ToArray();
        Assert.Contains("section", labels);
        Assert.DoesNotContain("section A", labels);
    }
}
