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
using LilySharp.Core.Semantics;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Completing the staff-line selector (`staff m as lines N` — the count moved
/// off the part header, 2026-08-19): after a staff/ossia part name the editor
/// offers `as lines` beside the ordinary continuations; after `as` it offers
/// `lines`; after `as lines` it enumerates the counts the compiler accepts.
/// The part header's own completion list no longer knows the word.
/// </summary>
[Trait("Category", "Unit")]
public class StaffLinesCompletionTests
{
    private static LilySharpLanguageServer.CompletionContext Ctx(string text)
        => LilySharpLanguageServer.GetCompletionContext(text, text.Length);

    [Theory]
    [InlineData("score main { staff melody ")]
    [InlineData("score main { ossia melody ")]
    public void AfterStaffName_OffersTheLinesSelector(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffAttachName, Ctx(text));

    [Theory]
    [InlineData("score main { staff melody as ")]
    [InlineData("score main { ossia melody as ")]
    public void AfterAs_OffersLines(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffLinesAs, Ctx(text));

    [Fact]
    public void AfterAsLines_EnumeratesTheCounts()
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffLinesValue,
            Ctx("score main { staff melody as lines "));

    [Fact]
    public void StaffAttachCompletions_ContainTheSelectorAndContinuations()
    {
        var items = LilySharpLanguageServer.GetStaffAttachNameCompletions().Items;
        Assert.Contains(items, i => i.Label == "as lines");
        // A following render item is not blocked.
        Assert.Contains(items, i => i.Label == "staff");
        Assert.Contains(items, i => i.Label == "lyrics");
    }

    /// <summary>Written against the compiler's range rather than the literals
    /// 1..5: what can rot is the pair, not the numbers.</summary>
    [Fact]
    public void LinesValueCompletions_AreExactlyTheCompilersRange()
    {
        var labels = LilySharpLanguageServer.GetStaffLinesValueCompletions().Items
            .Select(i => i.Label).ToArray();
        var expected = System.Linq.Enumerable.Range(
                LanguageVocabulary.MinStaffLines,
                LanguageVocabulary.MaxStaffLines - LanguageVocabulary.MinStaffLines + 1)
            .Select(n => n.ToString()).ToArray();
        Assert.Equal(expected, labels);
    }

    [Fact]
    public void PartHeaderCompletions_NoLongerOfferLines()
        => Assert.DoesNotContain(LilySharpLanguageServer.GetPartPropertyCompletions().Items,
            i => i.Label == "lines");
}
