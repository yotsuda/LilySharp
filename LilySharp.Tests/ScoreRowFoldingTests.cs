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
    /// A chords row is NOT folded: the row already is the LilyPond-ported
    /// adhesion. <c>lyrics.chord-row.between-systems.*</c> measured LilyPond
    /// putting the row INTO the lyric loose chain (page-layout-problem.cc,
    /// ported 2026-07-27, residuals are font terms only); folding it into the
    /// attached-chords engraver measurably moved that geometry AWAY from
    /// LilyPond (residual −0.002157 → +0.030400, 2026-08-19).
    /// </summary>
    [Fact]
    public void ChordRowAboveAStaff_IsAlreadyTheAdhesion_NotFolded()
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

    /// <summary>The chorale identity — the group fold renders the ink the old
    /// in-group attachment clause rendered (the 08-chorale migration is a pure
    /// respelling).</summary>
    [Fact]
    public void TheGroupRowSpelling_RendersTheInGroupAttachmentSpellingsInk()
    {
        string attach = Render(ChoraleBody
            + "score main { choirStaff { staff sop with lyrics words  staff alt } }");
        string rows = Render(ChoraleBody
            + "score main { choirStaff { staff sop  lyrics words  staff alt } }");

        Assert.Equal(attach, rows);

        static string Render(string src) => System.Text.RegularExpressions.Regex.Replace(
            SvgGenerator.Generate(SyntaxTree.Parse(src)), " data-pos=\"[0-9:]*\"", "");
    }

    /// <summary>
    /// THE IDENTITY THAT MAKES THE FOLD THE WHOLE PORT: the row spelling and the
    /// attachment spelling of the same music render the same ink — only the
    /// source positions differ, because the sources do. Verses, a below-staff
    /// marcato (the skyline drop), and a second staff are all in the book so the
    /// claim covers the placement machinery, not just the happy path.
    /// </summary>
    [Fact]
    public void TheRowSpelling_RendersTheAttachmentSpellingsInk()
    {
        const string body = """
            time 4/4
            part voc { clef treble }
            part pno { clef bass }
            section A {
              voc { c'4@marcato d' e' f' | g'2 g' | }
              lyrics ja sings voc { la la la la | ho ho | }
              lyrics en sings voc { na na na na | go go | }
              pno { c4 d e f | c2 c | }
            }
            form main { A }
            """;

        string attach = Render(body + "score main { staff voc with lyrics ja with lyrics en  staff pno }");
        string rows = Render(body + "score main { staff voc  lyrics ja  lyrics en  staff pno }");

        Assert.Equal(attach, rows);

        static string Render(string src) => System.Text.RegularExpressions.Regex.Replace(
            SvgGenerator.Generate(SyntaxTree.Parse(src)), " data-pos=\"[0-9:]*\"", "");
    }
}
