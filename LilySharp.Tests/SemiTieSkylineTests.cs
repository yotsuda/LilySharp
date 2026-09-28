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

using System;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A HALF-TIE (<c>@laissezVibrer</c> / <c>@repeatTie</c>, and the repeat tie a tie carried
/// over a repeat sign draws — SectionTieCarry) is inside-staff ink, so a section label stands
/// clear of it. LilyPond: LaissezVibrerTie / RepeatTie take their vertical skylines from the
/// stencil and declare no outside-staff-priority (scm/define-grobs.scm), so
/// skyline_spacing puts them in the inside-staff profile every mark is placed against
/// (lily/axis-group-interface.cc:914-935).
/// Until 2026-09-28 they were in no vertical skyline (SkylineBuilder.AddSemiTiesToSkylines):
/// the second ending's label B was drawn on the automatic repeat tie of
/// <c>form main { I |: A [1. B] :| [2. B] }</c> (SYNTAX_REFERENCE "Across a section
/// boundary"), and a label at a section opened by a user-written <c>@repeatTie</c> the same.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SemiTieSkylineTests
{
    // The manual's example: the tie from A's last note reaches ending 2 as a repeat tie.
    private const string AutoRepeatTieIntoSecondEnding = """
        part vn {
          section I { c''1~ || }
          section A { c''1 | e1~ || }
          section B { e''1 | }
        }
        form main { I |: A [1. B] :| [2. B] }
        score main { staff vn }
        """;

    // A user-written @repeatTie on the first note of a labelled section.
    private const string WrittenRepeatTieAtLabelledSection = """
        octave absolute
        part vn {
          section A { c''1 | d''1 || }
          section B { d''1@repeatTie | e''1 | }
        }
        form main { A B }
        score main { staff vn }
        """;

    // The tie carried back to the |: — the repeat tie lands on the body's first note.
    private const string AutoRepeatTieBackToTheRepeatStart = """
        octave absolute
        part vn {
          section I { c''1 || }
          section A { e''1 | c''1 | e''1~ || }
          section B { e''1 | }
        }
        form main { I |: A :| B }
        score main { staff vn }
        """;

    public static TheoryData<string, string> Books => new()
    {
        { "auto repeat tie into ending 2", AutoRepeatTieIntoSecondEnding },
        { "written @repeatTie at a labelled section", WrittenRepeatTieAtLabelledSection },
        { "auto repeat tie back to |:", AutoRepeatTieBackToTheRepeatStart },
    };

    [Theory]
    [MemberData(nameof(Books))]
    public void TheSectionLabelStandsClearOfTheRepeatTie(string name, string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);
        Assert.Single(layout.Systems);
        var system = layout.Systems[0];
        // The half-ties' Y is device-down within the system; the marks' is Y-up about the top
        // staff's middle. One conversion: the top staff's middle, device-down.
        double middleDown = LayoutUtilities.StaffOffsetInSystemDown(system, -1) + 2.0;

        var repeatTies = layout.TieVariantLayouts.Where(t => t.Kind == TieVariantKind.Repeat).ToList();
        Assert.NotEmpty(repeatTies);
        var labels = layout.MusicMarkLayouts.Where(m => m.MarkType == MusicMarkType.SectionLabel).ToList();
        Assert.NotEmpty(labels);

        int sideBySide = 0;
        foreach (var tie in repeatTies)
        {
            var (tx0, tx1, ty0, ty1) = BowInk(tie, middleDown);
            foreach (var label in labels)
            {
                var (x0, x1, top, bottom) = OutsideStaffStacker.MusicMarkExtents(score.TextMetrics, label);
                double lx0 = label.X + x0, lx1 = label.X + x1;
                double ly0 = label.YUp - bottom, ly1 = label.YUp + top;
                if (lx1 <= tx0 || tx1 <= lx0)
                    continue; // not over the tie at all
                sideBySide++;
                Assert.True(ly0 >= ty1 || ly1 <= ty0,
                    $"{name}: label '{label.Text}' box x[{lx0:F3},{lx1:F3}] y[{ly0:F3},{ly1:F3}] "
                    + $"overlaps the repeat tie's ink x[{tx0:F3},{tx1:F3}] y[{ty0:F3},{ty1:F3}]");
            }
        }
        // Not vacuous: at least one label stands over a repeat tie horizontally, so it is the
        // vertical placement that is being asserted.
        if (name != "auto repeat tie back to |:")
            Assert.True(sideBySide > 0, $"{name}: no label stands over a repeat tie");
    }

    /// <summary>The bow's ink box, Y-up about the top staff's middle: the centreline bezier
    /// sampled, widened by the bezier sandwich's half mid-thickness and half pen.</summary>
    private static (double X0, double X1, double Y0, double Y1) BowInk(TieVariantLayout t, double middleDown)
    {
        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        for (int i = 0; i <= 64; i++)
        {
            double s = i / 64.0, u = 1 - s;
            double x = u * u * u * t.StartX + 3 * u * u * s * t.Control1.X + 3 * u * s * s * t.Control2.X + s * s * s * t.EndX;
            double y = u * u * u * t.Y + 3 * u * u * s * t.Control1.Y + 3 * u * s * s * t.Control2.Y + s * s * s * t.Y;
            double yUp = middleDown - y;
            x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
            y0 = Math.Min(y0, yUp); y1 = Math.Max(y1, yUp);
        }
        double half = 0.5 * (EngravingDefaults.TieMidThickness + EngravingDefaults.BowEndRounding);
        return (x0 - half, x1 + half, y0 - half, y1 + half);
    }
}
