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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for LyricHyphenEngraver - hyphen and extender layout calculation.
/// </summary>
[Trait("Category", "Unit")]
public class LyricHyphenTests
{
    private static LyricLayout CreateLyricLayout(
        string text,
        int measureIndex,
        int itemIndex,
        double x,
        double y,
        double width,
        LyricConnectorType connectorType = LyricConnectorType.None,
        int verseNumber = 1,
        LilySharp.Core.Semantics.Fraction timing = default)
    {
        var item = new LyricItem(text, measureIndex, itemIndex, connectorType, 0, verseNumber,
            Timing: timing);
        // LyricLayout stores Y-up from the system top; the helper's y is a device
        // baseline, so store its negation.
        return new LyricLayout(item, x, -y, width);
    }

    private static ImmutableArray<SystemLayout> CreateSingleSystem(params (int measureIndex, double x, double width)[] measures)
    {
        var measureLayouts = measures.Select(m =>
            new MeasureLayout(m.measureIndex, m.x, m.width, ImmutableArray<ItemLayout>.Empty))
            .ToImmutableArray();

        var system = new SystemLayout(0, 0, 76, 0, measureLayouts);
        return ImmutableArray.Create(system);
    }

    [Fact]
    public void CalculateLayouts_NoConnectors_ReturnsEmpty()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hello", 0, 0, 5, 10, 3),
            CreateLyricLayout("World", 0, 1, 15, 10, 3)
        };
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Empty(result);
    }

    [Fact]
    public void CalculateLayouts_SingleHyphen_CreatesHyphenLayout()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 9, 10, 1.5)  // Narrow gap for single hyphen
        };
        var systems = CreateSingleSystem((0, 0, 20));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Hyphen, result[0].Type);
        Assert.True(result[0].Dashes.Length >= 1, "Should have at least one hyphen dash");
        Assert.False(result[0].CrossesSystemBreak);
    }
    [Fact]
    public void CalculateLayouts_WideGap_CreatesMultipleHyphens()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 50, 10, 1.5)  // Very wide gap
        };
        var systems = CreateSingleSystem((0, 0, 60));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Hyphen, result[0].Type);
        // Wide gap should produce multiple hyphens
        Assert.True(result[0].Dashes.Length >= 2,
            $"Expected at least 2 hyphens for wide gap, got {result[0].Dashes.Length}");
    }

    [Fact]
    public void CalculateLayouts_Extender_CreatesExtenderLayout()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("star", 0, 0, 5, 10, 2.5, LyricConnectorType.Extender),
            CreateLyricLayout("", 0, 3, 25, 10, 0)  // Empty syllable after melisma
        };
        var systems = CreateSingleSystem((0, 0, 35));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.Equal(LyricConnectorType.Extender, result[0].Type);
        Assert.True(result[0].ExtenderStartX > 0);
        Assert.True(result[0].ExtenderEndX > result[0].ExtenderStartX);
        Assert.False(result[0].CrossesSystemBreak);
    }

    [Fact]
    public void CalculateLayouts_MultipleVerses_CalculatesIndependently()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            // Verse 1
            CreateLyricLayout("Twin", 0, 0, 5, 10, 2.5, LyricConnectorType.Hyphen, verseNumber: 1),
            CreateLyricLayout("kle", 0, 1, 15, 10, 2, verseNumber: 1),
            // Verse 2
            CreateLyricLayout("Bril", 0, 0, 5, 12, 2, LyricConnectorType.Hyphen, verseNumber: 2),
            CreateLyricLayout("liant", 0, 1, 15, 12, 3, verseNumber: 2)
        };
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Equal(2, result.Length);
        // Both should be hyphens
        Assert.All(result, r => Assert.Equal(LyricConnectorType.Hyphen, r.Type));
    }

    [Fact]
    public void CalculateLayouts_TooNarrowGap_NoHyphen()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("Hap", 0, 0, 5, 10, 2, LyricConnectorType.Hyphen),
            CreateLyricLayout("py", 0, 1, 7, 10, 1.5)  // Very narrow gap
        };
        var systems = CreateSingleSystem((0, 0, 15));

        var result = engraver.CalculateLayouts(lyrics, systems);

        // Should return empty because gap is too narrow
        Assert.Empty(result);
    }

    [Fact]
    public void Parameters_Defaults_AreTheLilyPondDeclaredValues()
    {
        // LILYPOND-REF: lily/lyric-hyphen.cc:64-74 dash_period, dash_length reads of
        // the LyricHyphen defaults (set in scm/define-grobs.scm).
        var p = LyricHyphenParameters.Default;

        Assert.Equal(10.0, p.DashPeriod);
        Assert.Equal(0.66, p.DashLength);
        Assert.Equal(0.42, p.HyphenHeight);
        Assert.Equal(1.3, p.HyphenThickness);
        Assert.Equal(0.07, p.HyphenPadding);
        Assert.Equal(0.3, p.HyphenMinimumLength);
        Assert.True(p.ExtenderThickness > 0);
    }

    // ---- LP-pinned observers: lyric-hyphen-grace.ly twin (LilyPond 2.26.0 SVG,
    //      staff-relative X = page X − 8.5358, syllable ink width 3.0045) ----

    private static ImmutableArray<SystemLayout> CreateTwoSystems(
        (int measureIndex, double x, double width) m0,
        (int measureIndex, double x, double width) m1)
    {
        return ImmutableArray.Create(
            new SystemLayout(0, 0, m0.x + m0.width, 0, ImmutableArray.Create(
                new MeasureLayout(m0.measureIndex, m0.x, m0.width, ImmutableArray<ItemLayout>.Empty))),
            new SystemLayout(1, 20, m1.x + m1.width, 0, ImmutableArray.Create(
                new MeasureLayout(m1.measureIndex, m1.x, m1.width, ImmutableArray<ItemLayout>.Empty))));
    }

    [Fact]
    public void CalculateLayouts_MidLine_LaysDashesOnTheDeclaredPeriodWithTheLeftoverSplit()
    {
        // System 4 staff one: bla (ink 4.9864..7.9909) -- bla (ink left 53.1085).
        // LilyPond prints 5 dashes of 0.66 starting at 10.2198, exactly 10.0 apart:
        // n = ceil(45.1176/10 − ½) = 5, leftover 4.4576 split half at each end.
        // LILYPOND-REF: lily/lyric-hyphen.cc:101-133 space_left around dash_period steps.
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("bla", 0, 0, 6.48865, 10, 3.0045, LyricConnectorType.Hyphen),
            CreateLyricLayout("bla", 0, 1, 54.61075, 10, 3.0045)
        };
        var systems = CreateSingleSystem((0, 0, 102.43));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        var dashes = result[0].Dashes;
        Assert.Equal(5, dashes.Length);
        Assert.Equal(10.2198, dashes[0].X1, 3);
        Assert.Equal(10.2198 + 0.66, dashes[0].X2, 3);
        for (int i = 1; i < dashes.Length; i++)
            Assert.Equal(10.0, dashes[i].X1 - dashes[i - 1].X1, 6);
        // The dash box sits (height .. height+th) ABOVE the baseline; the stored Y
        // is its centre: baseline − (0.42 + 0.13/2).
        Assert.Equal(10 - 0.485, dashes[0].Y, 6);
    }

    [Fact]
    public void CalculateLayouts_RightSyllableOnTheLineStartMoment_FillsTheLineEndAndKillsTheStub()
    {
        // The fixture's claim: a broken piece whose right syllable sits on the new
        // line's FIRST moment spans no musical time and is killed — a grace note
        // under it takes none. The line-END piece still fills to the barline's ink:
        // system 1 staff one, bla ink right 66.6587 → barline left 102.2399,
        // 4 dashes starting 69.1194 (LilyPond page 77.6552..107.6552).
        // LILYPOND-REF: scm/define-grobs.scm:2151 after-line-breaking
        //   (kill-zero-spanned-time);
        // LILYPOND-REF: lily/lyric-hyphen.cc:107-121 break_status_dir of the RIGHT
        //   bound skips the squeeze/disappear guards.
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("bla", 0, 0, 65.15645, 10, 3.0045, LyricConnectorType.Hyphen),
            CreateLyricLayout("bla", 1, 0, 8.0, 10, 3.0045) // Timing 0 = first moment
        };
        var systems = CreateTwoSystems((0, 0, 102.4299), (1, 3.365, 99.0649));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        Assert.True(result[0].CrossesSystemBreak);
        var dashes = result[0].Dashes;
        Assert.All(dashes, d => Assert.False(d.OnNextSystem)); // no line-start stub
        Assert.Equal(4, dashes.Length);
        Assert.Equal(69.1194, dashes[0].X1, 3);
        Assert.Equal(99.1194, dashes[3].X1, 3);
    }

    [Fact]
    public void CalculateLayouts_SpannedTimeIntoTheNewLine_KeepsTheLineStartPiece()
    {
        // System 2 staff one: the melisma runs into the new line and the right
        // syllable sits mid-measure (Timing ½) — the line-start piece SURVIVES:
        // 6 dashes from the line's music start (LilyPond bounds on the clef ink
        // right, 3.365) to the syllable ink left 60.8890, first at 6.7970.
        // LILYPOND-REF: lily/lyric-hyphen.cc:101-133 space_left around dash_period steps.
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>
        {
            CreateLyricLayout("bla", 0, 0, 65.15645, 10, 3.0045, LyricConnectorType.Hyphen),
            CreateLyricLayout("bla", 1, 0, 62.39125, 10, 3.0045,
                timing: new LilySharp.Core.Semantics.Fraction(1, 2))
        };
        var systems = CreateTwoSystems((0, 0, 102.4299), (1, 3.365, 99.0649));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Single(result);
        var stub = result[0].Dashes.Where(d => d.OnNextSystem).ToList();
        Assert.Equal(6, stub.Count);
        Assert.Equal(6.7970, stub[0].X1, 3);
        // The stub's Y is relative to the NEXT syllable's system (its baseline).
        Assert.Equal(10 - 0.485, stub[0].Y, 6);
        Assert.Equal(4, result[0].Dashes.Length - stub.Count); // line-end fill stays
    }

    [Fact]
    public void CalculateLayouts_EmptyInput_ReturnsEmpty()
    {
        var engraver = new LyricHyphenEngraver();
        var lyrics = new List<LyricLayout>();
        var systems = CreateSingleSystem((0, 0, 30));

        var result = engraver.CalculateLayouts(lyrics, systems);

        Assert.Empty(result);
    }
}
