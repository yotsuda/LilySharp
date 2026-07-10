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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A structure-level repeat may write its endings the same way the inline volta
/// does — <c>|: A [1. D] :| [2. O]</c> — with the repeat barline between the
/// first and second endings, not after both.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FormVoltaTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section A { m { c4 c c c | } }\n" +
        "section D { m { d4 d d d | } }\n" +
        "section O { m { e4 e e e | } }\n";

    private const string Tail = "\nscore { staff m }\n";

    private static int MeasureCount(string structure)
    {
        var tree = SyntaxTree.Parse(Head + structure + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return new MeasureCollector().Collect(tree, "m").Voice.Measures.Count();
    }

    [Fact]
    public void InlineVoltaForm_ParsesAndRenders()
    {
        // Repeat barline between the endings — the unified form.
        Assert.True(MeasureCount("form main { |: A [1. D] :| [2. O] }") > 0);
    }

    [Fact]
    public void OldSpelling_RepeatBarlineAfterBothEndings_IsAnError()
    {
        // |: A [1. D] [2. O] :|  — the repeat barline must go BETWEEN the endings
        // ([1. D] :| [2. O]); the old after-both spelling is rejected.
        var tree = SyntaxTree.Parse(Head + "form main { |: A [1. D] [2. O] :| }" + Tail);
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.VoltaRepeatBarlinePlacement);
    }

    [Fact]
    public void BareEnding_WithoutOpeningBracket_IsRejected()
    {
        // The '[' is required: write '[2. O]', not a bare '2. O'.
        var tree = SyntaxTree.Parse(Head + "form main { |: A [1. D] :| 2. O }" + Tail);
        Assert.True(tree.HasErrors);
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.VoltaBracketRequired);
    }

    [Fact]
    public void OpenBracket_WithoutClosingBracket_IsAccepted()
    {
        // The closing ']' is optional — absent leaves the cap open.
        Assert.True(MeasureCount("form main { |: A [1. D :| [2. O }") > 0);
    }

    [Fact]
    public void BackToBackRepeat_ColonPipeColon_EqualsExplicitTwoBlocks()
    {
        // ':|:' (one shared barline) must produce exactly the same measures as the
        // explicit ':| |:' two-block spelling — a repeat-end that opens a new repeat.
        static (BarlineType Start, BarlineType End)[] Bars(string structure)
        {
            var tree = SyntaxTree.Parse(Head + structure + Tail);
            Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
            return new MeasureCollector().Collect(tree, "m").Voice.Measures
                .Select(m => (m.StartBarline, m.EndBarline)).ToArray();
        }

        var shorthand = Bars("form main { A |: D :|: O :| }");
        var explicitTwoBlocks = Bars("form main { A |: D :| |: O :| }");
        Assert.Equal(explicitTwoBlocks, shorthand);

        // And the shared boundary is a repeat-end meeting a repeat-start (which the
        // renderer fuses into the RepeatBoth glyph): D closes with RepeatEnd, O opens
        // with RepeatStart.
        Assert.Contains(shorthand, b => b.End == BarlineType.RepeatEnd);
        Assert.Contains(shorthand, b => b.Start == BarlineType.RepeatStart);
    }

    [Fact]
    public void SilentReference_InsideRepeat_RendersMeasuresWithoutLabel()
    {
        // '~D' inside a repeat must render D's music with NO label — not drop the
        // measure. The top-level silent-reference case skips in-repeat nodes, so
        // without ProcessRepeatBlock handling it the whole measure vanished.
        var tree = SyntaxTree.Parse(Head + "form main { A |: D :|: ~D :| }" + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var measures = new MeasureCollector().Collect(tree, "m").Voice.Measures.ToArray();
        Assert.Equal(3, measures.Length);                              // A, D, ~D (was 2)
        Assert.Single(measures, m => m.SectionLabel == "D");           // only labelled D
    }

    [Fact]
    public void SilentReference_WithHiddenLabel_WarnsButRendersUnlabelled()
    {
        // '~D "alt"' — a label parked but hidden by '~' (write now, reveal later by
        // dropping the '~'). Valid (not an error): warn, keep the text, render the
        // measure with NO label.
        var tree = SyntaxTree.Parse(Head + "form main { A ~D \"alt\" }" + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.HiddenSectionLabel);

        var measures = new MeasureCollector().Collect(tree, "m").Voice.Measures.ToArray();
        Assert.Equal(2, measures.Length);                              // A, ~D
        Assert.DoesNotContain(measures, m => m.SectionLabel == "alt"); // label stays hidden
    }

    [Fact]
    public void SilentReference_NoLabel_DoesNotWarn()
    {
        var tree = SyntaxTree.Parse(Head + "form main { A ~D }" + Tail);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.HiddenSectionLabel);
    }

    [Fact]
    public void VoltaSilentAlternative_WithLabel_DoesNotWarn()
    {
        // '[2. ~O "alt"]' SHOWS "alt" — there the '~' suppresses only the volta
        // bracket, so the label is not hidden and must not warn.
        var tree = SyntaxTree.Parse(Head + "form main { |: A [1. D] :| [2. ~O \"alt\"] }" + Tail);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Code == DiagnosticCodes.HiddenSectionLabel);
    }

    [Fact]
    public void SilentSection_BetweenRepeatedSections_KeepsVisibleLabels()
    {
        // 'A |: ~D :| A' hides only D's mark; the two A boxes must remain. The
        // "single distinct section = noise, drop the box" heuristic counted only
        // VISIBLE labels, so hiding D collapsed the visible set to one distinct "A"
        // and wrongly wiped BOTH A boxes. A hand-hidden section signals the author
        // is curating marks — keep the survivors.
        var tree = SyntaxTree.Parse(Head + "form main { A |: ~D :| A }" + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var measures = new MeasureCollector().Collect(tree, "m").Voice.Measures;
        var marks = MusicMarkEngraver.BuildAllMarks(
            ImmutableArray<MusicMarkItem>.Empty, measures, tempo: null);
        var labels = marks.Where(m => m.Type == MusicMarkType.SectionLabel)
                          .OrderBy(m => m.MeasureIndex).Select(m => m.Text).ToArray();
        Assert.Equal(new[] { "A", "A" }, labels);  // D hidden, both A shown
    }

    [Fact]
    public void SingleRepeatedSection_StillDropsRedundantBoxes()
    {
        // Guard the heuristic the fix above narrows: 'A A' (one section, no hidden
        // sibling) is still one distinct section with nothing to navigate to — its
        // boxes stay suppressed as noise.
        var tree = SyntaxTree.Parse(Head + "form main { A A }" + Tail);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));

        var measures = new MeasureCollector().Collect(tree, "m").Voice.Measures;
        var marks = MusicMarkEngraver.BuildAllMarks(
            ImmutableArray<MusicMarkItem>.Empty, measures, tempo: null);
        Assert.DoesNotContain(marks, m => m.Type == MusicMarkType.SectionLabel);
    }

    [Fact]
    public void FirstEnding_BeforeRepeatBarline_Closes()
    {
        // The 1st ending sits before the :|, so its bracket must close with a down
        // hook at the repeat (regression: it used to stay open — only the last
        // ending closed).
        var tree = SyntaxTree.Parse(
            Head + "form main { |: A [1. D] :| [2. O] }\nscore { staff m  tab m }\n");
        var spec = RenderSpecParser.FindFirst(tree)!;
        var layout = new LayoutEngine().Layout(new MeasureCollector().CollectMultiStaff(tree, spec));

        var firstEnding = layout.VoltaBracketLayouts.First(v => v.VoltaText == "1.");
        Assert.True(firstEnding.IsClosed, "1st ending must close at the following :|");
    }
}
