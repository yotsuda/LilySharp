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
/// A <c>lyrics</c> track's optional voice-binding name (<c>lyrics sop { … }</c>) aligns
/// the track to a voice/part, so after <c>lyrics </c> at a definition site the editor
/// offers the declared voice/part names.
/// </summary>
[Trait("Category", "Unit")]
public class LyricsVoiceBindingCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    private static string[] Names(string text)
        => LilySharpLanguageServer.GetVoiceBindingNameCompletions(text).Items.Select(i => i.Label).ToArray();

    [Fact]
    public void AfterLyricsKeyword_AtDefinitionSite_IsTheVoiceBindingContext()
    {
        var text = "part melody { clef treble }\nlyrics ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsName, Ctx(text));
    }

    [Fact]
    public void OffersTheDeclaredPartNames()
    {
        var text = "part melody { clef treble }\npart bass { clef bass }\nlyrics ";
        Assert.Equal(new[] { "melody", "bass" }, Names(text));
    }

    [Fact]
    public void IncludesExplicitlyNamedVoices()
    {
        var text = """
            part p { << voice up { c } \\ voice down { e } >> }
            lyrics
            """;
        var names = Names(text);
        Assert.Contains("p", names);
        Assert.Contains("up", names);
        Assert.Contains("down", names);
    }

    [Fact]
    public void InsideAScoreBlock_LyricsIsStillTheTrackReference_NotVoiceBinding()
    {
        // `staff melody with lyrics ▮` references a declared lyrics track, so it keeps
        // the AfterLyricsRef context — not the voice-binding one.
        var text = "score main { staff melody with lyrics ";
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterLyricsRef, Ctx(text));
    }
}
