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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for system-start delimiter types: bracket, line-bracket, bar-line.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/system-start-delimiter.cc — brace/bracket/bar-line/line-bracket rendering
/// LILYPOND-REF: scm/define-grobs.scm — SystemStartBrace, SystemStartBracket, SystemStartBar, SystemStartSquare
/// </remarks>
[Trait("Category", "Unit")]
public class SystemStartDelimiterTests
{
    private static NoteItem MakeNote(int staffPosition = 0) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static Measure MakeNoteMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeNote(), MakeNote(), MakeNote(), MakeNote()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeRestMeasure() =>
        new(ImmutableArray.Create<MusicItem>(
            new RestItem(Fraction.Quarter, 0, 0),
            new RestItem(Fraction.Quarter, 0, 0),
            new RestItem(Fraction.Quarter, 0, 0),
            new RestItem(Fraction.Quarter, 0, 0)),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Staff CreateStaff(ClefType clef, Measure[] measures,
        bool removeEmpty = false, bool removeFirst = false) =>
        new(clef,
            ImmutableArray.Create(new Voice("v1", measures.ToImmutableArray())),
            RemoveEmpty: removeEmpty,
            RemoveFirst: removeFirst);

    [Fact]
    public void ChoirStaff_CreatesGroupWithChoirStaffType()
    {
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var choir = StaffGroup.CreateChoirStaff(s1, s2);

        Assert.Equal(StaffGroupType.ChoirStaff, choir.Type);
        Assert.True(choir.IsChoirStaff);
        Assert.True(choir.HasDelimiter);
        Assert.False(choir.IsGrandStaff);
        Assert.Equal(2, choir.StaffCount);
    }

    [Fact]
    public void BracketGroup_CreatesGroupWithStaffGroupType()
    {
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var group = StaffGroup.CreateBracketGroup(s1, s2);

        Assert.Equal(StaffGroupType.StaffGroup, group.Type);
        Assert.True(group.IsBracketGroup);
        Assert.True(group.HasDelimiter);
        Assert.False(group.IsGrandStaff);
    }

    [Fact]
    public void ChoirStaff_Layout_ProducesBracketDelimiter()
    {
        // LILYPOND-REF: ly/engraver-init.ly — ChoirStaff uses SystemStartBracket
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var choir = StaffGroup.CreateChoirStaff(s1, s2);

        var score = new MultiStaffScore(
            ImmutableArray.Create(choir),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = layouter.LayoutStaffGroups(score);

        Assert.Single(groups);
        var layout = groups[0];
        Assert.Equal(StaffGroupType.ChoirStaff, layout.Type);
        Assert.NotNull(layout.GrandStaffLayout);
        Assert.Equal(SystemStartDelimiterType.Bracket, layout.GrandStaffLayout!.DelimiterType);
        Assert.Equal(2, layout.Staves.Length);
    }

    [Fact]
    public void BracketGroup_Layout_ProducesBracketDelimiter()
    {
        // LILYPOND-REF: ly/engraver-init.ly — StaffGroup uses SystemStartBracket
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var group = StaffGroup.CreateBracketGroup(s1, s2);

        var score = new MultiStaffScore(
            ImmutableArray.Create(group),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = layouter.LayoutStaffGroups(score);

        Assert.Single(groups);
        var layout = groups[0];
        Assert.Equal(StaffGroupType.StaffGroup, layout.Type);
        Assert.NotNull(layout.GrandStaffLayout);
        Assert.Equal(SystemStartDelimiterType.Bracket, layout.GrandStaffLayout!.DelimiterType);
    }

    [Fact]
    public void GrandStaff_Layout_ProducesBraceDelimiter()
    {
        // Verify existing GrandStaff gets Brace delimiter (backward compat)
        var treble = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var bass = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var grandStaff = StaffGroup.CreateGrandStaff(treble, bass);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaff),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = layouter.LayoutStaffGroups(score);

        var layout = groups[0];
        Assert.Equal(SystemStartDelimiterType.Brace, layout.GrandStaffLayout!.DelimiterType);
    }

    [Fact]
    public void BracketGroup_BracketTopBottom_SpanAllStaves()
    {
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s3 = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var group = StaffGroup.CreateBracketGroup(s1, s2, s3);

        var score = new MultiStaffScore(
            ImmutableArray.Create(group),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = layouter.LayoutStaffGroups(score);

        var layout = groups[0];
        var delim = layout.GrandStaffLayout!;

        // Bracket top should be at first staff Y
        Assert.Equal(layout.Staves[0].Y, delim.BraceTop);
        // Bracket bottom should be at the last staff's bottom (Y-up ⇒ Y - height)
        var lastStaff = layout.Staves[^1];
        Assert.Equal(lastStaff.Y - lastStaff.Height, delim.BraceBottom);
    }

    [Fact]
    public void ChoirStaff_WithHaraKiri_HiddenStaves_BracketShrinks()
    {
        // LILYPOND-REF: lily/system-start-delimiter.cc:127-129 — collapse-height
        var s1 = CreateStaff(ClefType.Treble, [MakeNoteMeasure()]);
        var s2 = CreateStaff(ClefType.Treble, [MakeRestMeasure()], removeEmpty: true);
        var s3 = CreateStaff(ClefType.Bass, [MakeNoteMeasure()]);
        var choir = StaffGroup.CreateChoirStaff(s1, s2, s3);

        var score = new MultiStaffScore(
            ImmutableArray.Create(choir),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());
        var groups = layouter.LayoutStaffGroups(score, 0, 1, isFirstSystem: false);

        var layout = groups[0];
        Assert.True(layout.Staves[1].IsHidden); // Middle staff hidden

        // Bracket should still span visible staves
        var delim = layout.GrandStaffLayout!;
        double bracketHeight = delim.TotalHeight;   // Y-up: BraceTop - BraceBottom
        Assert.True(bracketHeight > 0, "Bracket height should be positive");
        Assert.True(bracketHeight >= options.StaffHeight * 2,
            $"Bracket height ({bracketHeight:F2}) should span at least 2 visible staves");
    }

    [Fact]
    public void SystemStartDelimiterType_Enum_HasAllTypes()
    {
        // Verify all LilyPond delimiter types are present
        Assert.Equal(5, Enum.GetValues<SystemStartDelimiterType>().Length);
        Assert.Equal(0, (int)SystemStartDelimiterType.None);
        Assert.Equal(1, (int)SystemStartDelimiterType.Brace);
        Assert.Equal(2, (int)SystemStartDelimiterType.Bracket);
        Assert.Equal(3, (int)SystemStartDelimiterType.LineBracket);
        Assert.Equal(4, (int)SystemStartDelimiterType.BarLine);
    }

    /// <summary>The same book three times, one word different — the trio the grammar
    /// documents now publish as a table, with an observer so the table cannot rot.</summary>
    /// <remarks>
    /// ⚠️ The tests above assert the delimiter TYPE on a hand-built model; nothing looked at
    /// what the three keywords actually DRAW. GRAMMAR.md, SYNTAX_REFERENCE.md and
    /// GRAMMAR_FOR_LLM.md now each print a table of the two differences (the left-edge sign,
    /// and whether bar lines cross the gap), and a published table with no machine is how
    /// <c>staffGroup</c> and <c>choirStaff</c> came to be reserved everywhere and explained
    /// nowhere. Measured 2026-08-18: choirStaff's page is staffGroup's page MINUS exactly the
    /// rects standing in the inter-staff gap, which is LilyPond's ChoirStaff — a StaffGroup
    /// without the Span_bar_engraver.
    /// LILYPOND-REF: ly/engraver-init.ly:468-557 Span_bar_engraver — the StaffGroup context,
    ///   with GrandStaff and ChoirStaff derived from it.
    /// </remarks>
    [Fact]
    public void TheThreeStaffGroups_DrawTheThreeThingsTheGrammarPublishes()
    {
        static string Render(string keyword) =>
            LilySharp.Core.Svg.SvgGenerator.Generate(
                LilySharp.Core.Syntax.SyntaxTree.Parse(
                    "part rh { clef treble }\npart lh { clef bass }\n"
                    + "section A { rh { c4 d e f | } lh { c4 d e f | } }\n"
                    + "form main { A }\n"
                    + $"score main {{ {keyword} {{ staff rh  staff lh }} }}"),
                new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false });

        string brace = Render("grandStaff");
        string bracket = Render("staffGroup");
        string choir = Render("choirStaff");

        // ⑴ No keyword is inert. Two that drew the same page would be indistinguishable from
        //    one of them doing nothing — the state the documents could not have caught.
        Assert.NotEqual(brace, bracket);
        Assert.NotEqual(brace, choir);
        Assert.NotEqual(bracket, choir);

        // ⑵ The left-edge sign: grandStaff sets a glyph from the BRACE face; the other two
        //    draw the bracket as a rule with serif tips and never touch that face.
        Assert.Contains("Emmentaler-Brace", brace, StringComparison.Ordinal);
        Assert.DoesNotContain("Emmentaler-Brace", bracket, StringComparison.Ordinal);
        Assert.DoesNotContain("Emmentaler-Brace", choir, StringComparison.Ordinal);

        // ⑶ Bar lines across the gap: present for grandStaff and staffGroup, absent for
        //    choirStaff, whose staves each keep their own.
        static int VerticalRects(string svg) =>
            System.Text.RegularExpressions.Regex.Matches(svg, "<rect[^>]*height=").Count;

        Assert.Equal(VerticalRects(brace), VerticalRects(bracket));
        Assert.True(VerticalRects(choir) < VerticalRects(bracket),
            $"choirStaff drew {VerticalRects(choir)} rects and staffGroup {VerticalRects(bracket)}: "
            + "a ChoirStaff has no Span_bar_engraver, so the gap between its staves is empty");
    }
}
