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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A run's verse ink has TWO builders, and this net binds them to agree: the geometry
/// engraver's per-verse lists (<see cref="LyricEngraver.NoteBoundBlockSkylines"/> /
/// <see cref="LyricEngraver.RowBlockSkylines"/>, which the pair walk and the page
/// reservation read) and <see cref="LyricEngraver.BuildVerseSkylines"/>' dictionaries
/// (which the loose-line solve reads) must produce the same skyline for the same
/// (system, line, verse), or the page reserves one room and the chain solves another.
/// </summary>
/// <remarks>
/// This is the seam <c>LooseLineSpacer.RunSlots</c>' remark names in its ink paragraph
/// — HANDOFF 5.2.1②'s "two spellings of one quantity", bound here BEFORE the
/// unification's second stage moves any supplier. Both builders go through ONE X model
/// (<c>CalculateSyllableLayout</c> + <c>ResolveOverlaps</c>) and ONE profile builder
/// (<c>MergeSyllableInto</c>), so what this net actually watches is the residue: the
/// engraver instances differ (<c>ForGeometry</c> against the annotation pass's, whose
/// <c>parentAlignmentEdge</c> comes from the live context), and a divergence there is a
/// real defect of the reservation, not a tolerance to widen.
/// <para>
/// ⚠️ THE COMPARISON IS FUNCTIONAL AND EXACT. The two sides merge the same layouts in
/// the same order, so their skylines must agree to the bit; an epsilon here would be a
/// place for drift to hide. The membership is asserted too — the same verses, in the
/// same ascending order — which is the position-vs-number seam (<c>RunSlots</c> seam ⑵)
/// measured on books where every verse lays.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class VerseInkBuilderAgreementTests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Render(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var multi = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(multi);
        return (multi, layout);
    }

    /// <summary>The last spaceable staff's index — the anchor a below-system block
    /// hangs from, and the boundary of the chain's IsUpper test.</summary>
    private static int LastSpaceableStaffIndex(MultiStaffScore score)
    {
        int last = -1;
        foreach (var (_, staff, idx) in score.EnumerateStaves())
            if (StaffAffinity.IsSpaceable(staff.StaffAffinity))
                last = idx;
        Assert.True(last >= 0, "the book must have a spaceable staff");
        return last;
    }

    private static Dictionary<int, int> MeasureToSystem(ScoreLayout layout)
    {
        var map = new Dictionary<int, int>();
        for (int s = 0; s < layout.AllSystems.Length; s++)
            foreach (var ml in layout.AllSystems[s].Measures)
                map[ml.MeasureIndex] = s;
        return map;
    }

    private static (int Start, int End) MeasureRange(SystemLayout system)
    {
        int start = int.MaxValue, end = int.MinValue;
        foreach (var ml in system.Measures)
        {
            start = Math.Min(start, ml.MeasureIndex);
            end = Math.Max(end, ml.MeasureIndex + 1);
        }
        return (start, end);
    }

    /// <summary>Exact functional equality, sampled at every building edge and midpoint
    /// of BOTH skylines — identical inputs through one merge must agree to the bit.</summary>
    private static void AssertSkylinesAgree(VerticalSkyline a, VerticalSkyline b, string what)
    {
        Assert.Equal(a.IsEmpty, b.IsEmpty);
        var xs = new SortedSet<double>();
        foreach (var sky in new[] { a, b })
            foreach (var bl in sky.Buildings)
            {
                if (!double.IsInfinity(bl.Start)) xs.Add(bl.Start);
                if (!double.IsInfinity(bl.End)) xs.Add(bl.End);
                if (!double.IsInfinity(bl.Start) && !double.IsInfinity(bl.End))
                    xs.Add((bl.Start + bl.End) / 2);
            }
        foreach (double x in xs)
        {
            double ha = a.Height(x), hb = b.Height(x);
            Assert.True(ha.Equals(hb),
                $"{what}: the two builders disagree at x={x:F6}: "
                + $"list {ha:F9} vs dictionary {hb:F9}");
        }
    }

    /// <summary>One book's note-bound block, both builders, every system.</summary>
    private static void AssertNoteBoundAgrees(string source)
    {
        var (score, layout) = Render(source);
        int lastSpaceable = LastSpaceableStaffIndex(score);
        int LineKeyOf(LyricItem l)
            => l.IsLyricsRow || l.StaffIndex != lastSpaceable ? l.StaffIndex : -1;

        var (up, down) = LyricEngraver.BuildVerseSkylines(
            score.TextMetrics, layout.LyricLayouts, MeasureToSystem(layout),
            LineKeyOf, layout.AllSystems, null);
        var engraver = LyricEngraver.ForGeometry(score);

        // Every note-bound line the book has: [staff, staff+1), keyed as the chain keys it.
        var noteBoundStaves = score.Lyrics
            .Where(l => !l.IsLyricsRow).Select(l => l.StaffIndex).Distinct().ToList();
        Assert.NotEmpty(noteBoundStaves);

        int comparedVerses = 0;
        for (int s = 0; s < layout.AllSystems.Length; s++)
        {
            var (start, end) = MeasureRange(layout.AllSystems[s]);
            foreach (int staff in noteBoundStaves)
            {
                var list = engraver.NoteBoundBlockSkylines(
                    score.Lyrics, layout.AllSystems[s].Measures, start, end, staff, staff + 1);
                int lineKey = staff == lastSpaceable ? -1 : staff;
                var verses = score.Lyrics
                    .Where(l => !l.IsLyricsRow && l.StaffIndex == staff
                                && l.MeasureIndex >= start && l.MeasureIndex < end)
                    .Select(l => l.VerseNumber).Distinct().OrderBy(v => v).ToList();

                // Membership: the dictionary holds exactly the verses the list built,
                // ascending — the position-vs-number seam, measured.
                Assert.Equal(verses.Count, list.Count);
                for (int k = 0; k < verses.Count; k++)
                {
                    Assert.True(up.ContainsKey((s, lineKey, verses[k])),
                        $"system {s} line {lineKey} verse {verses[k]} is missing from the solve's dictionary");
                    AssertSkylinesAgree(list[k].Up, up[(s, lineKey, verses[k])],
                        $"system {s} line {lineKey} verse {verses[k]} UP");
                    AssertSkylinesAgree(list[k].Down, down[(s, lineKey, verses[k])],
                        $"system {s} line {lineKey} verse {verses[k]} DOWN");
                    comparedVerses++;
                }
            }
        }
        Assert.True(comparedVerses > 0, "the net compared nothing — a blind instrument");
    }

    [Fact]
    public void NoteBoundBelowSystem_TwoVerses_BothBuildersAgree()
    {
        AssertNoteBoundAgrees(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | }
  lyrics melody { Aa bb cc dd | ee ff gg hh | Pp qq rr ss | tt uu vv ww | }
}
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
    }

    [Fact]
    public void NoteBoundUpperStaff_BothBuildersAgree()
    {
        AssertNoteBoundAgrees(
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n" +
            "lyrics ly sings melody { section A { la le li lo } }\n" +
            "form main { A }\n" +
            "score main {\n  staff melody  lyrics ly\n  staff back\n}\n");
    }

    [Fact]
    public void NoteBound_AcrossSystems_BothBuildersAgree()
    {
        // Enough bars to break into more than one system, so the X frames that restart
        // per system are exercised — the divergence a single-system book cannot see.
        AssertNoteBoundAgrees(@"
time 4/4
section Main {
  melody { c'4 d e f | g a b c'' | c''4 b a g | f e d c' | c'4 d e f | g a b c'' | c''4 b a g | f e d c' | c'4 d e f | g a b c'' | c''4 b a g | f e d c' | }
  lyrics melody { Aa bb cc dd | ee ff gg hh | ii jj kk ll | mm nn oo pp | qq rr ss tt | uu vv ww xx | yy zz ab cd | ef gh ij kl | mn op qr st | uv wx yz za | zb zc zd ze | zf zg zh zi | }
}
form main { Main }
score main ""x"" { staff melody  lyrics melody }
");
    }

    [Fact]
    public void IndependentRow_BothBuildersAgree()
    {
        var (score, layout) = Render(
            "part melody { section A { c4 d e f } }\n" +
            "part back { section A { e4 f g a } }\n" +
            "lyrics ly sings melody { section A { la le li lo la le li lo } }\n" +
            "form main { A A }\n" +
            "score main {\n  staff melody\n  staff back\n  lyrics ly\n}\n");

        var rowStaves = score.Lyrics
            .Where(l => l.IsLyricsRow).Select(l => l.StaffIndex).Distinct().ToList();
        Assert.NotEmpty(rowStaves);

        int lastSpaceable = LastSpaceableStaffIndex(score);
        int LineKeyOf(LyricItem l)
            => l.IsLyricsRow || l.StaffIndex != lastSpaceable ? l.StaffIndex : -1;
        var (up, down) = LyricEngraver.BuildVerseSkylines(
            score.TextMetrics, layout.LyricLayouts, MeasureToSystem(layout),
            LineKeyOf, layout.AllSystems, null);
        var engraver = LyricEngraver.ForGeometry(score);

        int comparedVerses = 0;
        for (int s = 0; s < layout.AllSystems.Length; s++)
        {
            var (start, end) = MeasureRange(layout.AllSystems[s]);
            foreach (int rowStaff in rowStaves)
            {
                var list = engraver.RowBlockSkylines(
                    score.Lyrics, layout.AllSystems[s].Measures, start, end, rowStaff);
                var verses = score.Lyrics
                    .Where(l => l.IsLyricsRow && l.StaffIndex == rowStaff
                                && l.MeasureIndex >= start && l.MeasureIndex < end)
                    .Select(l => l.VerseNumber).Distinct().OrderBy(v => v).ToList();
                Assert.Equal(verses.Count, list.Count);
                for (int k = 0; k < verses.Count; k++)
                {
                    Assert.True(up.ContainsKey((s, rowStaff, verses[k])),
                        $"system {s} row {rowStaff} verse {verses[k]} is missing from the solve's dictionary");
                    AssertSkylinesAgree(list[k].Up, up[(s, rowStaff, verses[k])],
                        $"system {s} row {rowStaff} verse {verses[k]} UP");
                    AssertSkylinesAgree(list[k].Down, down[(s, rowStaff, verses[k])],
                        $"system {s} row {rowStaff} verse {verses[k]} DOWN");
                    comparedVerses++;
                }
            }
        }
        Assert.True(comparedVerses > 0, "the net compared nothing — a blind instrument");
    }
}
