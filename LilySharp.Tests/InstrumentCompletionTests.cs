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
using LilySharp.Core.Svg.Model;
using LilySharp.Lsp;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completion right after the <c>instrument</c> part property offers only valid
/// instrument-preset names (never notes or free text).
/// </summary>
[Trait("Category", "Unit")]
public class InstrumentCompletionTests
{
    [Theory]
    [InlineData("part m { instrument ", "instrument")]         // after 'instrument ' (no partial word)
    [InlineData("part m { instrument gu", "instrument")]       // mid instrument name
    [InlineData("part m { instrument piano-", "instrument")]   // hyphenated preset, at the hyphen
    [InlineData("part m { instrument piano-ri", "instrument")] // hyphenated preset, past the hyphen
    [InlineData("part m { instrument 5-str", "instrument")]    // digit-leading hyphenated preset
    public void WordBeforeCursor_FindsInstrument(string text, string expected)
    {
        Assert.Equal(expected, LilySharpLanguageServer.WordBeforeCursor(text, text.Length));
    }

    [Theory]
    [InlineData("part m { instrument ", true)]           // the real part property
    [InlineData("part m { instrument piano-", true)]     // hyphenated partial keeps the context
    [InlineData("lyrics { play my instrument ", false)]  // ordinary English word in lyrics
    [InlineData("title \"My instrument ", false)]        // inside a string literal
    [InlineData("structure { instrument ", false)]       // structure body, not a part
    [InlineData("section S { m { instrument ", false)]   // part REFERENCE body, not a declaration
    public void InstrumentContext_FiresOnlyInsideAPartBlock(string text, bool expected)
    {
        var context = LilySharpLanguageServer.GetCompletionContext(text, text.Length);
        Assert.Equal(expected,
            context == LilySharpLanguageServer.CompletionContext.AfterInstrument);
    }

    [Fact]
    public void InstrumentCompletions_ReplaceTheWholeHyphenatedToken()
    {
        // After typing `instrument piano-`, the client's default word range stops at
        // the hyphen — without an explicit TextEdit, accepting "piano-right" would
        // leave the prefix in place and produce "piano-piano-right".
        const string text = "part m { instrument piano-";
        var items = LilySharpLanguageServer.GetInstrumentCompletions(
            text, text.Length, new Position(0, text.Length)).Items;

        int tokenStart = text.Length - "piano-".Length;
        foreach (var item in items)
        {
            Assert.NotNull(item.TextEdit);
            Assert.Equal(tokenStart, item.TextEdit!.Range.Start.Character);
            Assert.Equal(text.Length, item.TextEdit.Range.End.Character);
            Assert.Equal(item.Label, item.TextEdit.NewText);
        }
    }

    [Fact]
    public void TabTuningPresets_AreKnownAndComplete()
    {
        // GetTuning-only names are valid `instrument` values (they set the tab-tuning
        // default), so they must be in KnownInstruments — which is also exactly what
        // the completion offers.
        foreach (var name in new[] { "ukulele", "uke", "bass-guitar", "electric-bass",
                                     "bass5", "5-string-bass", "bass6", "6-string-bass" })
        {
            Assert.True(InstrumentDefaults.IsKnownInstrument(name), $"{name} not known");
            Assert.NotNull(InstrumentDefaults.GetTuning(name));
        }
    }

    [Fact]
    public void InstrumentCompletions_AreExactlyTheKnownInstruments()
    {
        var labels = LilySharpLanguageServer.GetInstrumentCompletions().Items
            .Select(i => i.Label).ToArray();

        // Exactly the compiler's known-instrument set, in the same (family) order — the
        // completion list is sourced from InstrumentDefaults, so it can never drift.
        Assert.Equal(InstrumentDefaults.KnownInstruments.ToArray(), labels);

        // Every offered name is actually recognized, and no note names leak in.
        foreach (var label in labels)
            Assert.True(InstrumentDefaults.IsKnownInstrument(label), $"{label} not recognized");
        foreach (var note in new[] { "c", "d", "e", "f", "g", "a", "b" })
            Assert.DoesNotContain(note, labels);
    }

    [Fact]
    public void InstrumentCompletions_KeepFamilyOrderViaSortText()
    {
        // The editor sorts by SortText, so the labels ordered by SortText must match the
        // family-grouped source order (not alphabetical, which would scatter families).
        var ordered = LilySharpLanguageServer.GetInstrumentCompletions().Items
            .OrderBy(i => i.SortText, System.StringComparer.Ordinal)
            .Select(i => i.Label)
            .ToArray();

        Assert.Equal(InstrumentDefaults.KnownInstruments.ToArray(), ordered);
    }
}
