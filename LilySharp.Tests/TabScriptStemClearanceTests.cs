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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A forced-above script on a TAB staff has to clear the tab stem, which reaches well past
/// the string lines.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. <see cref="ArticulationEngraver"/> resolves a tab script in four
/// branches — beamed/unbeamed x above/below — and only the BEAMED pair carried a stem term
/// (it clears the beam's outer edge). The unbeamed pair clamped to the staff's outer line,
/// so a stem drawn 2.85 past that line went straight through the glyph. MEASURED on
/// test/tab-articulations-multistaff before the fix: tab stems ran to 17.960000 while the
/// flageolet AND the fermata both sat at 19.810000 — one number for two glyphs of different
/// heights, which is a clamp rather than a placement, and the fermata's arm crossed the stem
/// at x 24.20. HANDOFF's recurring shape: one member of a family fixed.
/// </para>
/// <para>
/// ⚠️ IT ASSERTS THE RULE, NOT THE NUMBER. The clearance is read out of the same render as
/// the stem, so a change to the tab stem length, the string space or the digit font moves
/// both sides together and this still holds. What it forbids is the script and the stem
/// occupying the same Y band.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabScriptStemClearanceTests
{
    /// <summary>
    /// Open low strings on a BASS tab, so the digits sit on the lower lines and the
    /// string-based stem direction is UP — the case where the stem travels toward a
    /// forced-above script instead of away from it.
    /// </summary>
    private const string Book =
        "time 4/4\nkey c major\n"
        + "part m { instrument bass section A { a4@fermata a4 a4 a4 | } }\n"
        + "form main { A }\nscore main { staff m  tab m }";

    private static RecordingDrawingContext RenderFirstPage(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    [Fact]
    public void ForcedAboveScript_OnATabStaff_SitsClearOfTheUnbeamedStem()
    {
        var page = RenderFirstPage(Book);

        // Stems are the only vertical strokes drawn at the stem thickness (bar lines are
        // 0.16, staff and string lines 0.1), and the TAB staff's are the lower set — picked
        // by depth on the page, never by where the script landed, or the test would be
        // asking the defect to describe itself.
        var stems = page.Lines
            .Where(l => System.Math.Abs(l.X1 - l.X2) < 1e-9
                        && System.Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .OrderBy(l => System.Math.Max(l.Y1, l.Y2))
            .ToList();
        Assert.Equal(8, stems.Count);              // four notes, two staves
        var tabStems = stems.TakeLast(4).ToList();

        // Both staves engrave the fermata; the tab's is the lower one.
        var fermatas = page.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.FermataAbove)
            .OrderBy(g => g.Y)
            .ToList();
        Assert.Equal(2, fermatas.Count);
        var tabFermata = fermatas[^1];

        // The stem under this script, by column.
        var stem = tabStems.OrderBy(l => System.Math.Abs(l.X1 - tabFermata.X)).First();
        double stemTip = System.Math.Min(stem.Y1, stem.Y2);
        double stemFoot = System.Math.Max(stem.Y1, stem.Y2);

        // ⚠️ POSITIVE CONTROL, and the test says nothing without it: the stem must actually
        // travel UP and past the staff, or "the script is above the tip" is satisfied by a
        // book that never posed the question (HANDOFF 5.0 trap 7 — assert you are in the
        // regime). The TAB staff is identified by its line SPACING, not by where anything
        // landed: a bass tab's strings sit a string-space apart where notation lines sit 1.0.
        double stringSpace = EngravingDefaults.TabStringSpace(
            Tunings.GetStringCount(TuningType.Bass));
        var rules = page.Lines
            .Where(l => System.Math.Abs(l.Y1 - l.Y2) < 1e-9
                        && System.Math.Abs(l.X2 - l.X1) > 10.0)
            .Select(l => l.Y1)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
        double tabTopLine = rules
            .Where(y => rules.Any(o => System.Math.Abs(o - (y + stringSpace)) < 1e-6))
            .Min();
        Assert.True(stemTip < tabTopLine,
            $"the tab stem does not reach past its own staff (tip {stemTip:F6}, "
            + $"top line {tabTopLine:F6}) — this book no longer tests anything.");
        Assert.True(stemFoot > tabTopLine, "the tab stem should start inside the staff.");

        // THE CLAIM: the script is clear of the stem, not on it. In device Y-down, above is
        // less. Before the fix this read 19.810000 against a tip of 17.960000.
        Assert.True(tabFermata.Y < stemTip,
            $"the tab fermata is drawn ON its own stem: script {tabFermata.Y:F6}, "
            + $"stem tip {stemTip:F6}..{stemFoot:F6}.");
    }

    [Fact]
    public void TheUnbeamedTabStemTip_IsTheGeometrysOwnNumber()
    {
        // The engraver clears a tip it computes from TabStaffGeometry while the renderer
        // draws one of its own; if the two ever disagree the clearance above is measured
        // against a stem nobody drew. This pins them to one number — the reason
        // TabConstants.UnbeamedStemLength was moved out of the renderer.
        var page = RenderFirstPage(Book);
        var tabStems = page.Lines
            .Where(l => System.Math.Abs(l.X1 - l.X2) < 1e-9
                        && System.Math.Abs(l.StrokeWidth - EngravingDefaults.StemThickness) < 1e-9)
            .OrderBy(l => System.Math.Max(l.Y1, l.Y2))
            .TakeLast(4)
            .ToList();
        double drawnLength = tabStems
            .Select(l => System.Math.Round(System.Math.Abs(l.Y1 - l.Y2), 9))
            .Distinct()
            .Single();
        double stringSpace = EngravingDefaults.TabStringSpace(
            Tunings.GetStringCount(TuningType.Bass));
        Assert.Equal(TabConstants.UnbeamedStemLength(stringSpace), drawnLength, 9);
    }
}
