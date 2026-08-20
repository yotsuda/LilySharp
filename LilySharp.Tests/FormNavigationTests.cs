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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Navigation marks written in the <c>structure</c> block (segno / coda / fine /
/// to coda / D.C. / D.S. al fine|coda) are engraved like the inline @-marks.
/// </summary>
[Trait("Category", "Unit")]
public class FormNavigationTests
{
    private static MusicMarkType[] Marks(string structure)
    {
        var source = $$"""
            part m {
              clef treble
              section A { c4 d e f | }
              section B { g4 a b c | }
              section C { e4 f g a | }
              section D { c'4 b a g | }
            }
            form main { {{structure}} }
            score main "x" { staff m }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(source));
        return score.MusicMarks.Select(m => m.Type).ToArray();
    }

    private static MusicMarkItem[] MarkItems(string structure)
    {
        var source = $$"""
            part m {
              clef treble
              section A { c4 d e f | }
              section B { g4 a b c | }
              section C { e4 f g a | }
              section D { c'4 b a g | }
            }
            form main { {{structure}} }
            score main "x" { staff m }
            """;
        return new MeasureCollector().Collect(SyntaxTree.Parse(source)).MusicMarks.ToArray();
    }

    [Fact]
    public void JumpInstructions_SitBelowStaff_TargetsAndToCodaAbove()
    {
        var marks = MarkItems("A segno B to coda C ds al coda coda D");
        MusicMarkVertical V(MusicMarkType t) => marks.First(m => m.Type == t).Vertical;

        // Jump-FROM instructions (D.S./D.C. family) are below the staff.
        Assert.Equal(MusicMarkVertical.Below, V(MusicMarkType.DalSegnoAlCoda));
        // Targets (segno/coda) and "To Coda" stay above.
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.Segno));
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.Coda));
        Assert.Equal(MusicMarkVertical.Above, V(MusicMarkType.ToCoda));
    }

    [Fact]
    public void SegnoToCodaDsAlCodaAndCoda_AreCollected()
    {
        var marks = Marks("A segno B to coda C ds al coda coda D");
        Assert.Contains(MusicMarkType.Segno, marks);
        Assert.Contains(MusicMarkType.ToCoda, marks);
        Assert.Contains(MusicMarkType.DalSegnoAlCoda, marks);
        Assert.Contains(MusicMarkType.Coda, marks);
    }

    [Fact]
    public void ToCoda_OneWordSpelling_IsCollected()
    {
        // "tocoda" (run together) is accepted as "to coda" — it previously read as
        // an undefined section name.
        var marks = Marks("A tocoda B coda C D");
        Assert.Contains(MusicMarkType.ToCoda, marks);
    }

    [Fact]
    public void ToCoda_OneWordAndTwoWord_AreEquivalent()
    {
        Assert.Equal(
            Marks("A to coda B coda C D"),
            Marks("A tocoda B coda C D"));
    }

    [Fact]
    public void DaCapoAlFineAndFine_AreCollected()
    {
        var marks = Marks("A B dc al fine C fine D");
        Assert.Contains(MusicMarkType.DaCapoAlFine, marks);
        Assert.Contains(MusicMarkType.Fine, marks);
    }

    [Fact]
    public void NoNavigationMarks_WhenStructureHasNone()
    {
        Assert.Empty(Marks("A B C D"));
    }

    /// <summary>
    /// The pairing half of the co-placement (session 227 rebuilt it INSIDE the
    /// stacking pass): a boundary "To Coda" and the next section's label share a
    /// barline (close X), so the sign is tucked to the label's left — a fixed 4.0
    /// centre-to-centre gap, baseline at the label box's bottom edge — while the
    /// label itself is untouched. The stacker then prices the pair as ONE union
    /// extent and moves both together, which is what makes a raise over a volta
    /// bracket under the sign (blogger.lys) and a raise over ink under the label
    /// (the old device's uncovered "mirror case") the same, direction-free story —
    /// guarded end to end by the tocoda-volta-clearance / tocoda-label-mirror
    /// fixtures' snapshots.
    /// </summary>
    [Fact]
    public void CoPlaceToCoda_TucksTheSignBesideTheLabel()
    {
        var marks = System.Collections.Immutable.ImmutableArray.Create(
            new MusicMarkLayout(1, 39.65, 4.50, MusicMarkType.ToCoda, "To Coda", false, 0),
            new MusicMarkLayout(2, 41.15, 4.50, MusicMarkType.SectionLabel, "C", false, 0),
            new MusicMarkLayout(1, 23.93, 4.50, MusicMarkType.SectionLabel, "B", false, 0));

        var placed = MusicMarkEngraver.CoPlaceToCodaWithLabels(
            marks, (_, _) => true, out var pairs);
        var tc = placed.First(m => m.MarkType == MusicMarkType.ToCoda);
        var cLabel = placed.First(m => m.MarkType == MusicMarkType.SectionLabel && m.Text == "C");

        Assert.Single(pairs);
        Assert.Equal(MusicMarkType.ToCoda, placed[pairs[0].Sign].MarkType);
        Assert.Equal("C", placed[pairs[0].Label].Text);
        Assert.Equal(1, tc.MeasureIndex);   // keeps its own (prev-section) measure
        // Centre-to-centre gap 4.0, to the label's LEFT.
        Assert.Equal(41.15 - 4.0, tc.X, 3);
        // Sign baseline meets the box bottom: label line − boxHalf, boxHalf =
        // (4.0*0.55 + 0.4)/2 = 1.3, so 4.50 − 1.30 = 3.20.
        Assert.Equal(3.20, tc.YUp, 3);
        // The label is untouched — the union placement decides the pair's line.
        Assert.Equal(4.50, cLabel.YUp, 3);
        Assert.Equal(41.15, cLabel.X, 3);
        // B is a different barline (far X) and is not part of any pair.
        Assert.Equal(4.50, placed.First(m => m.Text == "B").YUp, 3);
    }

    /// <summary>
    /// ⚠️ A sign at a line's END must not pair with the label OPENING the next line:
    /// absolute X keeps adjacent measures close across a break, so the X window alone
    /// cannot tell — the same-system gate is what says no.
    /// </summary>
    [Fact]
    public void CoPlaceToCoda_DoesNotPairAcrossALineBreak()
    {
        var marks = System.Collections.Immutable.ImmutableArray.Create(
            new MusicMarkLayout(1, 39.65, 4.50, MusicMarkType.ToCoda, "To Coda", false, 0),
            new MusicMarkLayout(2, 41.15, 4.50, MusicMarkType.SectionLabel, "C", false, 0));

        // Measure 1 ends one system, measure 2 opens the next.
        var placed = MusicMarkEngraver.CoPlaceToCodaWithLabels(
            marks, (ma, mb) => ma == mb, out var pairs);

        Assert.Empty(pairs);
        Assert.Equal(marks, placed);
    }

    // --- A SIGN THAT OPENS A LINE BREAK-ALIGNS ON THE BAR LINE DRAWN THERE (session 206) ---

    /// <summary>
    /// Four sections; the form opens the LAST one with a repeat, so its <c>|:</c> is drawn at
    /// a system start and a coda sign shares that moment with the section label.
    /// </summary>
    private static string CodaOpeningALineWithRepeat => """
        part melody {
          clef treble
          section A { c1 | c1 | c1 | c1 | break }
          section E { c1 | c1 }
        }

        form main { A coda |: E :| }

        score main { staff melody }
        """;

    private static (double CodaX, double LabelX, double BarX, double StaffTop) LineStartGeometry()
    {
        string svg = LiveRender.SvgFromRenderSpec(CodaOpeningALineWithRepeat);
        double Num(string pattern) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(svg, pattern).Groups[1].Value);

        // The LAST staff-line group is the final system's; its thick repeat stroke (0.60) is
        // the |: the sign must align to.
        var staffTops = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<line x1=""0\.00"" y1=""([\d.]+)"" x2=""[\d.]+"" y2=""\1""")
            .Select(m => double.Parse(m.Groups[1].Value)).Distinct().OrderBy(v => v).ToList();
        double staffTop = staffTops[^5];   // five lines per staff, last system's topmost

        double barX = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<rect x=""([\d.]+)"" y=""([\d.]+)"" width=""0\.60""")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .Where(r => r.Y >= staffTop - 0.01).Select(r => r.X).Min();

        // The coda is the only MUSIC glyph standing above that staff — matched by where it
        // is rather than by its character, so the test does not depend on the glyph literal
        // surviving every editor and encoding between here and the font.
        double codaX = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<text class=""music"" x=""([\d.]+)"" y=""([\d.]+)""")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .Where(g => g.Y < staffTop && g.Y > staffTop - 5.0).Select(g => g.X).Single();

        return (codaX, Num(@"<text x=""([\d.]+)""[^>]*>E</text>"), barX, staffTop);
    }

    /// <summary>
    /// ⚠️ AT A LINE START THE BAR LINE IS NOT ALWAYS INVISIBLE. The anchor fell back to the
    /// system's left edge on the premise that a line start has no bar line to align to — true
    /// of a system that merely continues, false of one that OPENS WITH A REPEAT. The owner's
    /// book drew the coda at x 0.30 with the <c>|:</c> at 6.44.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm CodaMark and SegnoMark declare
    ///   <c>(break-align-symbols . (staff-bar key-signature clef))</c> — the staff bar first —
    ///   where SectionLabel declares <c>(left-edge staff-bar)</c> and so keeps the edge, which
    ///   is why this test pins BOTH: the sign moves and the label does not.
    /// <para>
    /// MEASURED: audit/lp-geometry/probes/coda-line-start.ly. Mid-line (CB1) LilyPond puts the
    /// coda ON the bar line — 15.100113 against the bar's 14.555113, inside its 1.84 of ink.
    /// ⚠️ At a BREAK (CB2/CB3) LilyPond prints no coda on the new line at all
    /// (<c>break-visibility</c> is <c>begin-of-line-invisible</c>, so the end-of-line copy is
    /// the one that shows), while the SectionLabel control CB4 does appear at x 0.0. Lily#
    /// deliberately keeps the sign on the new line, so this placement is LILYSHARP-OWN and
    /// what is asserted is the RULE LilyPond does supply: which grob it aligns to.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACodaOpeningALine_SitsOnItsRepeatBarline()
    {
        var (codaX, labelX, barX, _) = LineStartGeometry();

        Assert.Equal(barX, codaX, 2);
        Assert.True(labelX < barX - 1.0,
            $"the section label keeps the LEFT EDGE ({labelX:F2}) — only the sign moves to the "
            + $"bar line ({barX:F2})");
    }

    /// <summary>
    /// ⚠️ AND THE LABEL MUST NOT BE LIFTED BY IT. Marks are grouped by (measure, position,
    /// timing), which was a PROXY for "these share an X" — true only while every mark of an
    /// opening column was anchored alike. Once the sign moved to the bar line and the label
    /// kept the edge, the proxy broke and the stack floated the label: measured on the owner's
    /// book, the "E" box sat 4.96 ss above its staff where an unstacked label sits at 2.28.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc avoid_outside_staff_collisions — outside-staff
    ///   grobs are skylined pointwise, so two that do not meet in X do not raise each other
    ///   however close their moments are.
    /// </remarks>
    [Fact]
    public void ACodaOpeningALine_DoesNotLiftTheSectionLabelBesideIt()
    {
        var (codaX, labelX, _, staffTop) = LineStartGeometry();
        string svg = LiveRender.SvgFromRenderSpec(CodaOpeningALineWithRepeat);

        // The label's box bottom, and how far it stands above its staff.
        var box = System.Text.RegularExpressions.Regex
            .Matches(svg, @"<rect x=""([\d.]+)"" y=""([\d.]+)"" width=""[\d.]+"" height=""2\.60""")
            .Select(m => (X: double.Parse(m.Groups[1].Value), Y: double.Parse(m.Groups[2].Value)))
            .Where(r => r.Y < staffTop).OrderBy(r => r.Y).Last();

        Assert.True(codaX - labelX > 1.0, "the two must actually stand apart for this to mean anything");
        Assert.True(staffTop - (box.Y + 2.60) < 3.0,
            $"the label is floating {staffTop - (box.Y + 2.60):F2} ss above its staff — it is "
            + "being stacked on a sign that is not in its column");
    }
}
