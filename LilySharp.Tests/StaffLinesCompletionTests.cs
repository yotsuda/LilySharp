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

    /// <summary>Inside a staff group the selector still applies (a member takes
    /// it), but the continuations are the group's own NARROW list — never the
    /// score-wide one, whose chords row a group refuses (LYS6011).</summary>
    [Fact]
    public void InsideAGroup_AfterStaffName_OffersTheSelectorAndGroupItems()
    {
        Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterGroupStaffAttachName,
            Ctx("score main { grandStaff { staff melody "));

        var labels = LilySharpLanguageServer.GetGroupStaffAttachNameCompletions().Items
            .Select(i => i.Label).ToArray();
        Assert.Contains("as lines", labels);
        Assert.Contains("staff", labels);
        Assert.Contains("lyrics", labels);
        Assert.DoesNotContain("chords", labels);
    }

    [Fact]
    public void InsideAGroup_AfterAs_StillReachesTheLinesSelector()
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffLinesAs,
            Ctx("score main { grandStaff { staff melody as "));

    [Fact]
    public void PartHeaderCompletions_NoLongerOfferLines()
        => Assert.DoesNotContain(LilySharpLanguageServer.GetPartPropertyCompletions().Items,
            i => i.Label == "lines");

    // ----- `as removeEmpty V` — hara-kiri joined the selectors 2026-09-08 (user decision) -----

    [Theory]
    [InlineData("score main { staff melody as removeEmpty ")]
    [InlineData("score main { staff melody as removeEmpty a")]
    [InlineData("score main { staff melody as lines 1 removeEmpty ")]   // chained after one `as`
    [InlineData("score main { grandStaff { staff melody as removeEmpty ")]
    public void AfterAsRemoveEmpty_OffersItsValues(string text)
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterRemoveEmpty, Ctx(text));

    [Fact]
    public void AfterAsRemoveEmptyAllLines_StillEnumeratesTheCounts()
        => Assert.Equal(LilySharpLanguageServer.CompletionContext.AfterStaffLinesValue,
            Ctx("score main { staff melody as removeEmpty all lines "));

    [Fact]
    public void ARemoveEmptyNoAsGoverns_IsNotAValueSlot()
        // A part that happens to be named removeEmpty, placed as a MIDI-only item.
        => Assert.NotEqual(LilySharpLanguageServer.CompletionContext.AfterRemoveEmpty,
            Ctx("score main { staff melody removeEmpty "));

    [Fact]
    public void TheSelectorLists_OfferRemoveEmptyBesideLines()
    {
        Assert.Contains(LilySharpLanguageServer.GetStaffAttachNameCompletions().Items, i => i.Label == "as removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetGroupStaffAttachNameCompletions().Items, i => i.Label == "as removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetStaffLinesSelectorCompletions().Items, i => i.Label == "removeEmpty");
        Assert.Contains(LilySharpLanguageServer.GetStaffLinesSelectorCompletions().Items, i => i.Label == "lines");
    }

    /// <summary>The net for the moved word: every value the editor offers after
    /// <c>as removeEmpty</c> is compiled in that position and must produce no error.</summary>
    [Fact]
    public void EveryOfferedRemoveEmptyValue_Compiles()
    {
        var rejected = LilySharpLanguageServer.GetRemoveEmptyCompletions().Items
            .Select(i => i.Label)
            .Where(v =>
            {
                var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(
                    "part m { }\nsection A { m { c'1 } }\nform main { A }\nscore main { staff m as removeEmpty " + v + " }\n");
                return tree.Diagnostics.Concat(SemanticValidation.Run(tree))
                    .Any(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
            })
            .ToList();
        Assert.True(rejected.Count == 0,
            "the editor offers these after `as removeEmpty` and the compiler refuses them: "
            + string.Join(", ", rejected));
    }
}
