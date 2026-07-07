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

using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Typing a partial name after the '@' (or '\') trigger must keep the completion
/// context alive: '@' offers accent/staccato/…, and '@acc' must still resolve to
/// AfterAt so the editor can filter the list down to "accent" — previously the
/// context was lost the moment the first letter was typed and completions vanished.
/// </summary>
[Trait("Category", "Unit")]
public class AnnotationCompletionContextTests
{
    private static LilySharpLanguageServer.CompletionContext ContextOf(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("c4@")]         // just the trigger
    [InlineData("c4@a")]        // one letter in
    [InlineData("c4@acc")]      // partial "accent"
    [InlineData("c4@marca")]    // partial "marcato"
    [InlineData("c'8@stacc")]   // partial "staccato" after an octave/duration note
    [InlineData("@finger")]     // partial with no preceding note
    public void PartialAnnotationName_StaysAfterAt(string text)
        => Assert.Equal("AfterAt", ContextOf(text).ToString());

    [Theory]
    [InlineData("\\")]
    [InlineData("\\stac")]
    public void PartialBackslashName_StaysAfterBackslash(string text)
        => Assert.Equal("AfterBackslash", ContextOf(text).ToString());
}
