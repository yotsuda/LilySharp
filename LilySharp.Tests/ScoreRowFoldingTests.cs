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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// SCORE = A VERTICAL STACK OF BANDS (user decision, 2026-08-19, closed before
/// the first tag): a bound <c>lyrics NAME</c> row standing directly BELOW the
/// staff whose part it sings IS that staff's attachment — the association is
/// written at the definition (<c>sings</c>, or the name-is-binding voice rule),
/// the score only orders the bands. These tests pin the fold
/// (RenderSpecParser.FoldAdjacentRows) at the spec level, and the last one pins
/// the identity that makes the fold the whole port: the folded row renders
/// byte-identically (modulo source positions) to the attachment it folds into.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreRowFoldingTests
{
    private static RenderSpec SpecOf(string src)
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(src));
        Assert.NotNull(spec);
        return spec!;
    }

    private const string BoundBody = """
        time 4/4
        section A {
          vocal { c'4 d' e' f' | g'2 g' | }
          lyrics ja sings vocal { la la la la | ho ho | }
          lyrics en sings vocal { na na na na | go go | }
        }
        form main { A }
        """;

    [Fact]
    public void BoundRowAfterItsStaff_FoldsIntoAnAttachedVerse()
    {
        var spec = SpecOf(BoundBody + "score main { staff vocal  lyrics ja }");

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "ja" }, staff.WithLyrics);
    }

    [Fact]
    public void ARunOfBoundRows_StacksAsVersesInWrittenOrder()
    {
        var spec = SpecOf(BoundBody + "score main { staff vocal  lyrics ja  lyrics en }");

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "ja", "en" }, staff.WithLyrics);
    }

    /// <summary>
    /// The part sheet carrying another part's words (test/sings-chorus-row): the
    /// row's binding is NOT the adjacent staff's part, so it keeps its place as an
    /// independent band — placement cannot re-decide the association.
    /// </summary>
    [Fact]
    public void RowSingingAnotherPart_KeepsItsPlaceAsABand()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              sax { c4 d e f | }
              vocal { g8 g a4 a8 a a4 | }
              lyrics ja sings vocal { la la la la la la | }
            }
            form main { A }
            score main { staff sax  lyrics ja }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<LyricsRowSpec>(spec.Items[1]);
        Assert.Empty(Assert.IsType<SingleStaffSpec>(spec.Items[0]).Staff.WithLyrics);
    }

    [Fact]
    public void UnboundRow_StaysTheEvenSpreadLeadSheetRow()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              melody { c'4 d' e' f' | }
              lyrics words { la la | }
            }
            form main { A }
            score main { staff melody  lyrics words }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<LyricsRowSpec>(spec.Items[1]);
    }

    /// <summary>The pre-<c>sings</c> voice rule, kept because the name IS the
    /// binding: <c>voice sop { } + lyrics sop { }</c> — a row named after one of
    /// the staff part's voices folds under that staff.</summary>
    [Fact]
    public void RowNamedAfterAVoiceOfTheStaffsPart_Folds()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              m { voice sop { c'4 d' e' f' | } alt { e4 f g a | } }
              lyrics sop { la la la la | }
            }
            form main { A }
            score main { staff m  lyrics sop }
            """);

        var staff = Assert.IsType<SingleStaffSpec>(Assert.Single(spec.Items)).Staff;
        Assert.Equal(new[] { "sop" }, staff.WithLyrics);
    }

    /// <summary>
    /// A LEADING chords row (nothing staff-like above it) is NOT folded: the row
    /// already is the LilyPond-ported adhesion for the system-opening regime.
    /// <c>lyrics.chord-row.between-systems.*</c> measured LilyPond putting the
    /// row INTO the lyric loose chain (page-layout-problem.cc, ported
    /// 2026-07-27, residuals are font terms only); folding it into the
    /// attached-chords engraver measurably moved that geometry AWAY from
    /// LilyPond (residual −0.002157 → +0.030400, 2026-08-19).
    /// </summary>
    [Fact]
    public void LeadingChordRow_IsAlreadyTheAdhesion_NotFolded()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              melody { c'4 d' e' f' | }
              chords prog { c1 | }
            }
            form main { A }
            score main { chords prog  staff melody }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.IsType<ChordRowSpec>(spec.Items[0]);
        Assert.Null(Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff.WithChords);
    }

    /// <summary>
    /// An INTERIOR chords row (a staff above, a staff below) folds into the
    /// staff below as its attached symbols — the between-staves band placement
    /// reads no up-skyline, and the attached engraver's reservation is the
    /// machinery that was measured to clear the staff-below's pushed-out ink
    /// (ChordRowOnALowerStaff_ClearsARestAnotherVoicePushedOutOfIt).
    /// </summary>
    [Fact]
    public void InteriorChordRow_FoldsIntoTheStaffBelow()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              hi { c'4 d' e' f' | }
              lo { c4 d e f | }
              chords prog { c1 | }
            }
            form main { A }
            score main { staff hi  chords prog  staff lo }
            """);

        Assert.Equal(2, spec.Items.Length);
        Assert.Null(Assert.IsType<SingleStaffSpec>(spec.Items[0]).Staff.WithChords);
        Assert.Equal("prog", Assert.IsType<SingleStaffSpec>(spec.Items[1]).Staff.WithChords);
    }

    [Fact]
    public void BoundRowAfterAGroup_FoldsIntoTheGroupsLastStaff()
    {
        var spec = SpecOf("""
            time 4/4
            section A {
              rh { c'4 d' e' f' | }
              lh { c4 d e f | }
              lyrics words sings lh { la la la la | }
            }
            form main { A }
            score main { grandStaff { staff rh  staff lh }  lyrics words }
            """);

        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Empty(group.Staves[0].WithLyrics);
        Assert.Equal(new[] { "words" }, group.Staves[1].WithLyrics);
    }

    private const string ChoraleBody = """
        time 4/4
        part sop { clef treble }
        part alt { clef treble }
        section A {
          sop { c'4 d' e' f' | }
          alt { e4 f g a | }
          lyrics words sings sop { la la la la | }
        }
        form main { A }
        """;

    /// <summary>Score = a vertical stack of bands INSIDE a group too: the chorale
    /// writes its words between the staves, and the row folds into the staff
    /// directly above it (⑵ of the with-clause removal plan).</summary>
    [Fact]
    public void BoundRowInsideAGroup_FoldsIntoTheStaffAbove()
    {
        var spec = SpecOf(ChoraleBody
            + "score main { choirStaff { staff sop  lyrics words  staff alt } }");

        var group = Assert.IsType<GrandStaffRenderSpec>(Assert.Single(spec.Items)).GrandStaff;
        Assert.Equal(2, group.StaffCount);
        Assert.Equal(new[] { "words" }, group.Staves[0].WithLyrics);
        Assert.Empty(group.Staves[1].WithLyrics);
    }

    [Fact]
    public void RowInAGroupSingingNoAdjacentStaff_IsRefused()
    {
        var src = ChoraleBody.Replace("sings sop", "sings alt")
            + "score main { choirStaff { staff sop  lyrics words  staff alt } }";
        var diags = LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(src));

        var d = Assert.Single(diags, d => d.Code == "LYS6012");
        Assert.Contains("does not sing 'sop'", d.Message);
    }

    [Fact]
    public void NonStaffNonRowMemberInAGroup_IsReportedAtTheMember()
    {
        var tree = SyntaxTree.Parse(ChoraleBody
            + "score main { grandStaff { staff sop  tab alt } }");

        var d = Assert.Single(tree.Diagnostics, d => d.Code == "LYS6011");
        Assert.Contains("cannot contain 'tab'", d.Message);
    }

    /// <summary>
    /// THE CLOSED DOOR SAYS WHERE THE NEW ONE IS: a `with lyrics` / `with chords`
    /// clause is LYS0031, and the message spells the row replacement. (The
    /// identity that made the removal safe — the row spelling rendering the
    /// clause spelling's ink byte-for-byte — was proven by machine while both
    /// spellings existed: commits 6d6d1b92 / 228c6108, three priority-stack pins
    /// and the chorale, all identical modulo data-pos.)
    /// </summary>
    [Fact]
    public void TheRemovedWithClause_ReportsItsRowReplacement()
    {
        var tree = SyntaxTree.Parse(BoundBody
            + "score main { staff vocal with lyrics ja }");
        var d = Assert.Single(tree.Diagnostics, d => d.Code == "LYS0031");
        Assert.Contains("'lyrics ja'", d.Message);
        Assert.Contains("after this staff", d.Message);

        tree = SyntaxTree.Parse(BoundBody + "score main { staff vocal with chords prog }");
        d = Assert.Single(tree.Diagnostics, d => d.Code == "LYS0031");
        Assert.Contains("'chords prog'", d.Message);
        Assert.Contains("before this staff", d.Message);
    }

    /// <summary>
    /// The nameless <c>chords { }</c> block is LYS0032 (user decision,
    /// 2026-08-19): its association was co-writing — stated nowhere, and
    /// hard-coded to staff 0 the moment a section held two parts. The message
    /// spells the replacement: name it, place it.
    /// </summary>
    [Fact]
    public void TheNamelessChordsBlock_ReportsItsNamedRowReplacement()
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            section A {
              melody { c'4 d' e' f' | }
              chords { c1 | }
            }
            form main { A }
            score main { staff melody }
            """);
        var d = Assert.Single(tree.Diagnostics, d => d.Code == "LYS0032");
        Assert.Contains("needs a name", d.Message);
        Assert.Contains("above the staff", d.Message);
    }
}
