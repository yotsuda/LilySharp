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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A meter whose ink stands out of its staff's own lines is in the staff's inside skyline
/// (SkylineBuilder.SeedMeter, session 871) — at the line start, at the line end's courtesy, on
/// a staff of fewer lines than five and on a tab.
/// LILYPOND-REF: lily/axis-group-interface.cc:914-935 skyline_spacing — a TimeSignature has no
/// outside-staff-priority, so it is inside-staff ink.
/// Each case is read twice: with the score (the seed's column needs it) and without, where the
/// line-start and line-end seeds are skipped — so the reading is the meter's and nothing else's.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MeterSkylineTests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
        return (score, new LayoutEngine(new LayoutOptions()).Layout(score));
    }

    private static (VerticalSkyline Up, VerticalSkyline Down) Inside(
        MultiStaffScore score, ScoreLayout layout, int systemIndex, bool withScore)
    {
        using var music = MusicFont.Use(MusicFonts.Of(score.Fonts));
        var staff = score.EnumerateStaves().First().Staff;
        var system = layout.Systems[systemIndex];
        return new SkylineBuilder(LayoutOptions.Default.StaffHeight, score.TextMetrics, withScore ? score : null)
            .BuildInsideStaffSkylines(staff, system.Measures, systemLeft: system.Indent);
    }

    private static (double Bottom, double Top) Ink(MultiStaffScore score, TimeSignature ts)
    {
        using var music = MusicFont.Use(MusicFonts.Of(score.Fonts));
        var rows = SharedRenderer.MeterRows(ts);
        return (rows.InkBottom, rows.InkTop);
    }

    /// <remarks>
    /// ⚠️ PETALUMA, NOT EMMENTALER: a notation staff of any line count seeds the nominal
    /// five-line band (±2.05, SeedStaffSymbol through MultiStaffLayouter.StaffHeightOf), so an
    /// Emmentaler meter (±2) on a one-line staff is seeded and changes nothing. Whether a
    /// one-line staff's profile should be its one line (LilyPond's StaffSymbol extent) is a
    /// separate question, not this seed's.
    /// </remarks>
    [Fact]
    public void OnAOneLineStaff_ATallMeterIsInTheSkyline()
    {
        // Rests only: no stem reaches past the meter.
        var (score, layout) = Lay("fonts { music \"Petaluma\" }\ntime 3/4\npart r { clef percussion }\n"
            + "section A { r { r2. | r2. | } }\nscore { staff r as lines 1 }\n");
        var (bottom, top) = Ink(score, score.TimeSignature);
        var with = Inside(score, layout, 0, withScore: true);
        var without = Inside(score, layout, 0, withScore: false);

        Assert.Equal(top, with.Up.MaxHeight(), 6);
        Assert.Equal(bottom, with.Down.MaxHeight(), 6);
        Assert.True(without.Up.MaxHeight() < top - 0.5, $"without the seed the profile reads {without.Up.MaxHeight():F3}");
    }

    [Fact]
    public void OnATab_APetalumaMeterBelowTheLowestString_IsInTheSkyline()
    {
        // A six-string tab's strings stand at ±3.75; Petaluma's 4 reaches 3.92 under the centre.
        var (score, layout) = Lay("fonts { music \"Petaluma\" }\ntime 3/4\npart g { clef treble }\n"
            + "section A { g { e4 e e | } }\nscore { tab g }\n");
        var (bottom, _) = Ink(score, score.TimeSignature);
        Assert.True(bottom < -3.8, $"the fixture's meter reaches only {bottom:F3}");
        var with = Inside(score, layout, 0, withScore: true);
        var without = Inside(score, layout, 0, withScore: false);

        Assert.True(with.Down.MaxHeight() <= bottom + 1e-6, $"{with.Down.MaxHeight():F3} against the meter's {bottom:F3}");
        Assert.True(without.Down.MaxHeight() > bottom + 0.05);
    }

    [Fact]
    public void TheLineEndsCourtesyMeter_IsInTheSkyline()
    {
        var (score, layout) = Lay("fonts { music \"Petaluma\" }\ntime 3/4\npart m { clef treble }\n"
            + "section A { m { b'2. | b'2. break | } }\nsection B { time 12/8  m { b'1. | } }\n"
            + "score { staff m }\n");
        var first = layout.Systems[0];
        var last = first.Measures[^1];
        double barlineRight = last.X + last.Width;
        var courtesy = score.EnumerateStaves().First().Staff.PrimaryVoice.Measures[last.MeasureIndex + 1]
            .Items.OfType<TimeSignatureChangeItem>().Single().NewTime;
        var (_, top) = Ink(score, courtesy);
        var with = Inside(score, layout, 0, withScore: true);
        var without = Inside(score, layout, 0, withScore: false);

        Assert.Equal(top, with.Up.MaxHeightInRange(barlineRight, barlineRight + 10), 6);
        Assert.True(without.Up.MaxHeightInRange(barlineRight, barlineRight + 10) < top - 0.5);
    }
}
