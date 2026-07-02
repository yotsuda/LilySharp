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
    [InlineData("part m { instrument ", "instrument")]    // after 'instrument ' (no partial word)
    [InlineData("part m { instrument gu", "instrument")]  // mid instrument name
    public void WordBeforeCursor_FindsInstrument(string text, string expected)
    {
        Assert.Equal(expected, LilySharpLanguageServer.WordBeforeCursor(text, text.Length));
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
