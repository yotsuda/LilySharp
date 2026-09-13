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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>condensedStaff { … }</c> or <c>combinedStaff { … }</c> as a MEMBER of a
/// <c>grandStaff</c> / <c>staffGroup</c> / <c>choirStaff</c> — the woodwind bracket over two
/// flutes on one staff and an oboe (user decision, session 376).
/// </summary>
/// <remarks>
/// Each such item engraves ONE staff, so the group stays a flat run of staves and nothing
/// below the spec learns about nesting. What can go wrong is therefore all in the reading of
/// the tree and the bookkeeping of staff indices, and that is what these pin:
/// <list type="bullet">
/// <item>⚠️ THE DOUBLE-ADD. <c>RenderSpecParser.Parse</c> walks DESCENDANTS, so a member is
/// reached once as a descendant of the score and once by <c>ParseGrandStaff</c>. Without the
/// <c>IsInsideGrandStaff</c> guard on its case it comes out twice — inside the bracket and
/// again as a loose staff — with no diagnostic. The first two tests are that guard's
/// tripwire.</item>
/// <item>The combined staff's addressing records a GLOBAL staff index, which the collector's
/// binding loop must reach by the same count; a member inside a group used to be impossible,
/// so the index was the group's first and nothing else.</item>
/// </list>
/// </remarks>
[Trait("Category", "Unit")]
public class NestedGroupMemberTests
{
    private const string Body = "octave absolute\ntime 4/4\n" + """
        part fl1 { clef treble }
        part fl2 { clef treble }
        part ob { clef treble }
        part cl { clef treble }
        section A {
          fl1 { c''4 d'' e'' f'' | }
          fl2 { e'4 f' g' a' | }
          ob { g'1 | }
          cl { c'1 | }
          lyrics words sings fl1 { la la la la | }
        }
        form main { ~A }
        """;

    private static string Book(string render) => Body + "\nscore main { " + render + " }\n";

    private static SyntaxTree Parse(string render) => SyntaxTree.Parse(Book(render));

    private static RenderSpec Spec(string render) => RenderSpecParser.FindFirst(Parse(render))!;

    private static ImmutableArray<Voice> OneVoice(string name) =>
        ImmutableArray.Create(new Voice(name, ImmutableArray<Measure>.Empty));

    [Fact]
    public void ACondensedMember_IsOneMemberOfTheGroup_AndNotAlsoALooseStaff()
    {
        var tree = Parse("staffGroup { condensedStaff { fl1 fl2 }  staff ob }");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree)!;
        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(StaffGroupType.StaffGroup, group.Type);
        Assert.Collection(group.Members,
            m => Assert.Equal(new[] { "fl1", "fl2" }, Assert.IsType<CondensedStaffSpec>(m).PartNames),
            m => Assert.Equal("ob", Assert.IsType<SingleStaffSpec>(m).Staff.VoiceName));
    }

    [Fact]
    public void ACombinedMember_IsOneMemberOfTheGroup_AndNotAlsoALooseStaff()
    {
        var tree = Parse("choirStaff { staff ob  combinedStaff { fl1 fl2 } }");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var spec = RenderSpecParser.FindFirst(tree)!;
        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Collection(group.Members,
            m => Assert.IsType<SingleStaffSpec>(m),
            m => Assert.Equal(new[] { "fl1", "fl2" }, Assert.IsType<CombinedStaffSpec>(m).PartNames));
    }

    [Fact]
    public void TheGroupBuildsOneStaffPerMember_TheCondensedOneCarryingBothParts()
    {
        var groups = Spec("staffGroup { condensedStaff { fl1 fl2 }  staff ob }")
            .ToStaffGroups(OneVoice).ToList();

        var group = Assert.Single(groups);
        Assert.Equal(StaffGroupType.StaffGroup, group.Type);
        Assert.Equal(2, group.Staves.Length);
        Assert.Equal(2, group.Staves[0].Voices.Length);
        Assert.Single(group.Staves[1].Voices);
    }

    /// <summary>
    /// ⚠️ THE INDEX A COMBINED MEMBER'S ADDRESSING CARRIES IS GLOBAL, and the binding loop
    /// reaches the same one. A loose staff, then a group whose SECOND member is the combined
    /// staff: that staff is index 2, not 1 (the group's first).
    /// </summary>
    [Fact]
    public void ACombinedMember_IsAddressedAtItsGlobalStaffIndex_InLockstepWithTheBindings()
    {
        var spec = Spec("staff cl  staffGroup { staff ob  combinedStaff { fl1 fl2 } }");

        var addressings = new List<CombinedStaffAddressing>();
        var groups = spec.ToStaffGroups(OneVoice, addressings).ToList();
        Assert.Equal(3, groups.Sum(g => g.Staves.Length));
        Assert.Equal(2, Assert.Single(addressings).StaffIndex);

        // The collector's count: every OwnStaff binding opens the next staff index.
        int staffIndex = -1, fl1Index = -1;
        foreach (var b in spec.GetVoiceBindings())
        {
            if (b.Slotting == VoiceSlotting.OwnStaff) staffIndex++;
            if (b.VoiceName == "fl1") fl1Index = staffIndex;
        }
        Assert.Equal(2, fl1Index);
    }

    [Fact]
    public void ALyricsRowUnderASharedMember_IsRefused()
    {
        var diags = SemanticValidation.Run(
            Parse("staffGroup { condensedStaff { fl1 fl2 }  lyrics words  staff ob }"));

        var d = Assert.Single(diags, d => d.Code == DiagnosticCodes.GroupRowNotBoundToStaffAbove);
        Assert.Contains("condensed or combined staff", d.Message);
    }

    [Fact]
    public void ARowUnderAPlainMemberAfterASharedOne_StillFolds()
    {
        var spec = Spec("staffGroup { condensedStaff { fl2 ob }  staff fl1  lyrics words }");

        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(new[] { "words" }, Assert.IsType<SingleStaffSpec>(group.Members[1]).Staff.WithLyrics);
    }

    [Fact]
    public void AnythingElseInAGroup_IsStillRefused_AndTheMessageNamesTheNewMembers()
    {
        var tree = Parse("staffGroup { staff ob  tab fl1 }");

        var d = Assert.Single(tree.Diagnostics, d => d.Code == DiagnosticCodes.StaffGroupBadMember);
        Assert.Contains("cannot contain 'tab'", d.Message);
        Assert.Contains("condensedStaff", d.Message);
    }

    /// <summary>
    /// Every group may stand inside every group, at any depth (NestedGroupDepthTests) — none of
    /// these is a bad member any more.
    /// </summary>
    [Theory]
    [InlineData("grandStaff { staff ob  grandStaff { staff fl1  staff fl2 } }")]
    [InlineData("staffGroup { staff ob  staffGroup { staff fl1  staff fl2 } }")]
    [InlineData("choirStaff { staff ob  choirStaff { staff fl1  staff fl2 } }")]
    [InlineData("staffGroup { staff ob  grandStaff { staff fl1  grandStaff { staff fl2  staff cl } } }")]
    public void EveryGroupInsideAGroup_IsAMember(string render)
    {
        var tree = Parse(render);

        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.StaffGroupBadMember);
    }
}
