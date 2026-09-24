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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>@mark</c> at the bar a section label opens is not engraved — the label is — and the
/// mark is reported (LYS4021). One mark a moment, the label kept: the reader's decision
/// (session 558, "LP に合わせて") after the twin showed LilyPond keeping the label and
/// discarding the <c>@mark</c> on the reader's `Hold the Line` (Lab sessions/p547/hold-lp.log).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/mark-tracking-translator.cc:185-192 Mark_tracking_translator::listen_ad_hoc_mark
/// — the first ad-hoc mark of a timestep is kept; lily/stream-event.cc:103-117
/// warn_reassign_event_ptr — the second is dropped with "discarding event".
/// <para>
/// Poisons (RULES §5.4): drop the <c>ShadowedBySectionLabel</c> test from
/// <c>MergeSectionLabels</c> and the first fact goes red (two marks at bar 1, two "Solo"
/// texts); drop <c>RecordShadowedRehearsalMarks</c> from <c>SvgGenerator.CollectScore</c> and
/// the second goes red (no LYS4021). The controls — the mark in the silent section, the
/// <c>sectionLabels none</c> book, the all-silent form — are green under both.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ShadowedRehearsalMarkTests
{
    private const string Book =
        "part m { clef treble }\n"
        + "section Solo { m { c'1@mark(\"Solo\") | d'1 | } }\n"
        + "section B { m { e'1@mark(\"B\") | f'1 | } }\n"
        + "form main { Solo ~B }\n"
        + "score main { staff m }\n";

    private static System.Collections.Immutable.ImmutableArray<MusicMarkItem> AllMarks(string source)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return MusicMarkEngraver.BuildAllMarks(score.MusicMarks, score.Voice.Measures, score.Tempo,
            score.SwingSubdivision, score.TempoText, score.TempoBeatUnit, score.TempoDots,
            sectionLabels: score.LayoutPlan.SectionLabels);
    }

    private static IReadOnlyList<Diagnostic> Shadowed(string source)
        => SemanticValidation.Run(SyntaxTree.Parse(source))
            .Where(d => d.Code == DiagnosticCodes.RehearsalMarkShadowedBySectionLabel).ToList();

    [Fact]
    public void AMarkAtTheBarALabelOpens_IsNotEngraved_AndTheLabelIs()
    {
        var marks = AllMarks(Book);
        // Bar 1: the label alone. The mark in the silent section (bar 3) is the control —
        // no label there, so the letter is on the page.
        var first = Assert.Single(marks.Where(m => m.MeasureIndex == 0));
        Assert.Equal(MusicMarkType.SectionLabel, first.Type);
        Assert.Equal("Solo", first.Text);
        var third = Assert.Single(marks.Where(m => m.MeasureIndex == 2));
        Assert.Equal(MusicMarkType.Rehearsal, third.Type);
        Assert.Equal("B", third.Text);

        // The page says the same: one "Solo", one "B".
        string svg = LiveRender.Svg(Book);
        Assert.Equal(1, CountOf(svg, ">Solo</text>"));
        Assert.Equal(1, CountOf(svg, ">B</text>"));
    }

    [Fact]
    public void TheHiddenMark_IsReportedOnce_AtTheMark_AndNotAsUnengraved()
    {
        var all = SemanticValidation.Run(SyntaxTree.Parse(Book));
        var warning = Assert.Single(all.Where(d => d.Code == DiagnosticCodes.RehearsalMarkShadowedBySectionLabel));
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(Book.IndexOf("@mark(\"Solo\")", System.StringComparison.Ordinal), warning.Span.Start);
        Assert.Contains("\"Solo\"", warning.Message);
        // LYS4019 is about a mark the collect never produced; this one it did produce.
        Assert.DoesNotContain(all, d => d.Code == DiagnosticCodes.UnengravedRehearsalMark);
    }

    [Fact]
    public void UnderSectionLabelsNone_TheMarkIsEngraved_AndNothingIsReported()
    {
        string book = "layout { sectionLabels none }\n" + Book;
        var first = Assert.Single(AllMarks(book).Where(m => m.MeasureIndex == 0));
        Assert.Equal(MusicMarkType.Rehearsal, first.Type);
        Assert.Equal("Solo", first.Text);
        Assert.Empty(Shadowed(book));
    }

    [Fact]
    public void ASilentReference_KeepsTheMark_AndNothingIsReported()
    {
        string book = Book.Replace("form main { Solo ~B }", "form main { ~Solo ~B }");
        var first = Assert.Single(AllMarks(book).Where(m => m.MeasureIndex == 0));
        Assert.Equal(MusicMarkType.Rehearsal, first.Type);
        Assert.Empty(Shadowed(book));
    }

    /// <summary>A repeated section opens with its label every pass; the written mark is one
    /// position and is reported once.</summary>
    [Fact]
    public void ARepeatedSection_ReportsTheWrittenMarkOnce()
    {
        string book = Book.Replace("form main { Solo ~B }", "form main { Solo Solo ~B }");
        Assert.Single(Shadowed(book));
        Assert.Empty(AllMarks(book).Where(m => m.Type == MusicMarkType.Rehearsal && m.Text == "Solo"));
    }

    private static int CountOf(string text, string needle)
    {
        int n = 0;
        for (int at = text.IndexOf(needle, System.StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, System.StringComparison.Ordinal))
            n++;
        return n;
    }
}
