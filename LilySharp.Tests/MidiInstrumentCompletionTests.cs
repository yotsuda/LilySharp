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
using LilySharp.Core.Midi;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Inside <c>midiInstrument "…"</c> the completion offers LilyPond's 128 General MIDI names
/// (owner's request 2026-10-11), each replacing what is typed between the opening quote and
/// the caret — the names hold spaces and parentheses, which the client's default word range
/// stops at — and before the quote each brings its own quotes.
/// </summary>
[Trait("Category", "Unit")]
public class MidiInstrumentCompletionTests
{
    [Fact]
    public void TheNames_AreExactlyTheCompilers_InGeneralMidiOrder()
    {
        var items = LilySharpLanguageServer.GetMidiInstrumentCompletions().Items;
        Assert.Equal(GeneralMidi.InstrumentNames, items.Select(i => i.Label));
        Assert.Equal(GeneralMidi.InstrumentNames,
            items.OrderBy(i => i.SortText, System.StringComparer.Ordinal).Select(i => i.Label));
        // Every name the editor offers is one the compiler plays.
        Assert.All(items, i => Assert.NotNull(GeneralMidi.ProgramOf(i.Label)));
        Assert.All(items, i => Assert.NotNull(i.Detail));
        Assert.Equal("General MIDI 1: piano", items[0].Detail);
        Assert.Equal("General MIDI 41: strings", items[40].Detail);   // violin
    }

    [Fact]
    public void InsideTheString_TheItemReplacesWhatIsTypedSinceTheQuote()
    {
        const string text = "part m { midiInstrument \"electric gui";
        var items = LilySharpLanguageServer.GetMidiInstrumentCompletions(
            text, text.Length, new Position(0, text.Length)).Items;
        int interior = text.IndexOf('"') + 1;
        foreach (var item in items)
        {
            Assert.NotNull(item.TextEdit);
            Assert.Equal(interior, item.TextEdit!.Range.Start.Character);
            Assert.Equal(text.Length, item.TextEdit.Range.End.Character);
            Assert.Equal(item.Label, item.TextEdit.NewText);
            Assert.Null(item.InsertText);
        }
    }

    [Fact]
    public void BetweenTheSnippetsQuotes_TheClosingQuoteIsKept()
    {
        // `midiInstrument "|"` — what the property item leaves: the range is empty at the
        // caret, so the name lands before the closing quote.
        const string text = "part m { midiInstrument \"\"";
        int caret = text.Length - 1;
        var item = LilySharpLanguageServer.GetMidiInstrumentCompletions(
            text, caret, new Position(0, caret)).Items[0];
        Assert.Equal(caret, item.TextEdit!.Range.Start.Character);
        Assert.Equal(caret, item.TextEdit.Range.End.Character);
        Assert.Equal("acoustic grand", item.TextEdit.NewText);
    }

    [Fact]
    public void BeforeAnyQuote_TheItemBringsItsOwn()
    {
        const string text = "part m { midiInstrument ";
        var item = LilySharpLanguageServer.GetMidiInstrumentCompletions(
            text, text.Length, new Position(0, text.Length)).Items.First(i => i.Label == "violin");
        Assert.Null(item.TextEdit);
        Assert.Equal("\"violin\"", item.InsertText);
    }
}
