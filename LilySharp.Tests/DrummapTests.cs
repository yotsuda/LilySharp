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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// What <c>drummap { }</c> accepts, and what it drops.
/// </summary>
/// <remarks>
/// ★ THE FIRST OBSERVERS THIS BLOCK HAS EVER HAD. Measured 2026-08-15: not one of the
/// 308 .lys on disk writes a drummap, and the string "drummap" did not occur anywhere in
/// this test project — yet the feature is live (a valid block moves 164 lines of a drum
/// score's SVG). Every mistake in it was silent: a block whose drum name, setting key,
/// range and value word were all wrong rendered byte-for-byte as if absent and reported
/// "No errors found". These state the accepted set BEFORE anything types the
/// sub-language, so that a later change to it has something to fail against
/// (VALUE_SITE_AUDIT §9, the ⒞ item; HANDOFF §5.0 「点が先」).
/// </remarks>
[Trait("Category", "Unit")]
public class DrummapTests
{
    private static string Score(string drummap) => $$"""
        part kit { clef percussion }
        {{drummap}}
        section Groove { kit { hh8 hh hhc4 bd4 | } }
        form main { Groove }
        score main "drums" { staff kit }
        """;

    /// <summary>The drum table a score's drummap produces, read the way the collector does.</summary>
    private static DrumInfo Resolve(string drummap, string drum)
    {
        var root = SyntaxTree.Parse(Score(drummap)).GetRoot();
        return DrumOverrides.Resolve(DrumOverrides.Build(root), drum);
    }

    private static IReadOnlyList<Diagnostic> Warnings(string drummap)
    {
        var tree = SyntaxTree.Parse(Score(drummap));
        var validator = new DrummapValidator();
        validator.Validate(tree);
        return validator.Diagnostics;
    }

    private static void AssertIgnored(string drummap, string quoted)
    {
        var warning = Assert.Single(Warnings(drummap));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticCodes.DrummapEntryIgnored, warning.Code);
        Assert.Contains(quoted, warning.Message);
    }

    // --- what it accepts -----------------------------------------------------

    [Theory]
    [InlineData("drummap { hh: position 6 }", 6)]
    [InlineData("drummap { hh: position -9 }", -9)]   // a leading minus is its own token
    [InlineData("drummap { hh: position 0 }", 0)]
    [InlineData("drummap { hh: position 9 }", 9)]
    public void APosition_MovesTheDrumOnTheStaff(string drummap, int position)
        => Assert.Equal(position, Resolve(drummap, "hh").StaffPosition);

    [Theory]
    [InlineData("x", NoteheadStyle.Cross)]
    [InlineData("cross", NoteheadStyle.Cross)]
    [InlineData("diamond", NoteheadStyle.Diamond)]
    [InlineData("triangle", NoteheadStyle.Triangle)]
    [InlineData("slash", NoteheadStyle.Slash)]
    [InlineData("xcircle", NoteheadStyle.XCircle)]
    [InlineData("default", NoteheadStyle.Default)]
    [InlineData("DIAMOND", NoteheadStyle.Diamond)]   // the value word is case-insensitive
    public void ANotehead_ChangesTheHead(string word, NoteheadStyle expected)
        => Assert.Equal(expected, Resolve($"drummap {{ hh: notehead {word} }}", "hh").Notehead);

    [Theory]
    [InlineData("drummap { hh: midi 0 }", 0)]
    [InlineData("drummap { hh: midi 60 }", 60)]
    [InlineData("drummap { hh: midi 127 }", 127)]
    public void AMidiKey_ChangesTheGmNumber(string drummap, int gm)
        => Assert.Equal(gm, Resolve(drummap, "hh").GmKey);

    [Theory]
    [InlineData("stopped")]
    [InlineData("open")]
    public void AMark_SetsTheAutoArticulation(string mark)
        => Assert.Equal(mark, Resolve($"drummap {{ hh: mark {mark} }}", "hh").Mark);

    /// <summary>Several settings in one entry, and the key word is case-insensitive.</summary>
    [Fact]
    public void OneEntry_CarriesEverySetting()
    {
        var info = Resolve("drummap { hh: position -4 NOTEHEAD slash midi 44 mark open }", "hh");
        Assert.Equal(-4, info.StaffPosition);
        Assert.Equal(NoteheadStyle.Slash, info.Notehead);
        Assert.Equal(44, info.GmKey);
        Assert.Equal("open", info.Mark);
    }

    /// <summary>Entries are independent, and an alias resolves to the same drum as its
    /// full name — so overriding 'hh' overrides 'hihat'.</summary>
    [Fact]
    public void SeveralEntries_EachOverrideTheirOwnDrum()
    {
        var map = DrumOverrides.Build(
            SyntaxTree.Parse(Score("drummap { hh: position 6  bd: position -6 }")).GetRoot());
        Assert.Equal(6, DrumOverrides.Resolve(map, "hihat").StaffPosition);
        Assert.Equal(-6, DrumOverrides.Resolve(map, "bd").StaffPosition);
        // Untouched drums keep the built-in table.
        Assert.Equal(1, DrumOverrides.Resolve(map, "sn").StaffPosition);
    }

    /// <summary>A drum named twice accumulates: the second entry does not discard the
    /// settings of the first, it adds to them.</summary>
    [Fact]
    public void ADrumNamedTwice_MergesItsSettings()
    {
        var info = Resolve("drummap { hh: position 6  hh: notehead diamond }", "hh");
        Assert.Equal(6, info.StaffPosition);
        Assert.Equal(NoteheadStyle.Diamond, info.Notehead);
    }

    [Fact]
    public void NoDrummap_LeavesTheBuiltInTable()
    {
        Assert.Null(DrumOverrides.Build(SyntaxTree.Parse(Score("")).GetRoot()));
        Assert.Equal(3, Resolve("", "hh").StaffPosition);
    }

    [Fact]
    public void AValidDrummap_IsSilent()
        => Assert.Empty(Warnings("drummap { hh: position 6 notehead x midi 44 mark open }"));

    // --- what it drops, and now says so (LYS0024) ----------------------------

    /// <summary>The drum vocabulary is static: a drummap overrides the built-in table and
    /// cannot add an instrument to it.</summary>
    [Fact]
    public void AnUnknownDrumName_IsIgnoredAndReported()
    {
        Assert.Null(DrumOverrides.Build(
            SyntaxTree.Parse(Score("drummap { zz: position 3 }")).GetRoot()));
        AssertIgnored("drummap { zz: position 3 }", "'zz' is not a drum name");
    }

    [Fact]
    public void AnUnknownSetting_IsIgnoredAndReported()
    {
        Assert.Equal(3, Resolve("drummap { hh: postion 6 }", "hh").StaffPosition);
        AssertIgnored("drummap { hh: postion 6 }", "'postion' is not a drummap setting");
    }

    [Theory]
    [InlineData("position 10")]
    [InlineData("position -10")]
    [InlineData("position two")]
    public void APositionOutOfRange_IsIgnoredAndReported(string setting)
    {
        Assert.Equal(3, Resolve($"drummap {{ hh: {setting} }}", "hh").StaffPosition);
        AssertIgnored($"drummap {{ hh: {setting} }}", $"'{setting}' is ignored");
    }

    [Theory]
    [InlineData("midi 128")]
    [InlineData("midi -1")]
    public void AMidiKeyOutOfRange_IsIgnoredAndReported(string setting)
    {
        Assert.Equal(42, Resolve($"drummap {{ hh: {setting} }}", "hh").GmKey);
        AssertIgnored($"drummap {{ hh: {setting} }}", $"'{setting}' is ignored");
    }

    [Fact]
    public void AnUnknownNoteheadWord_LeavesTheHeadAloneAndIsReported()
    {
        Assert.Equal(NoteheadStyle.Cross, Resolve("drummap { hh: notehead diamand }", "hh").Notehead);
        AssertIgnored("drummap { hh: notehead diamand }", "'notehead diamand' is ignored");
    }

    /// <summary>
    /// ⚠️ The one drop that is NOT merely a drop: an unrecognised mark word does not leave
    /// the drum's mark alone, it CLEARS it — the closed hi-hat loses its "stopped" (+).
    /// Pinned as it is because nothing was decided about changing it; the message says so
    /// rather than claiming the setting was ignored. Whoever types this sub-language should
    /// decide whether that is the intent (it reads like a fall-through, not a choice).
    /// </summary>
    [Fact]
    public void AnUnknownMarkWord_ClearsTheMark()
    {
        Assert.Equal("stopped", Resolve("", "hhc").Mark);
        Assert.Null(Resolve("drummap { hhc: mark loud }", "hhc").Mark);
        AssertIgnored("drummap { hhc: mark loud }", "'mark loud' is not a drum mark");
    }

    /// <summary>Each report points at what is wrong, not at the block: six mistakes in one
    /// drummap are six warnings at six different offsets.</summary>
    [Fact]
    public void EveryMistake_IsReportedAtItsOwnSpan()
    {
        var warnings = Warnings(
            "drummap { hh: postion -9 notehead diamand "
            + "zz: position 3 hh: position 99 midi 999 mark loud }");
        Assert.Equal(6, warnings.Count);
        Assert.Equal(6, warnings.Select(w => w.Span.Start).Distinct().Count());
        Assert.All(warnings, w => Assert.Equal(DiagnosticCodes.DrummapEntryIgnored, w.Code));
    }
}
