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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The page chain <c>PageLayouter.PositionSystemsOnPage</c> reports through
/// <c>LayoutEngine.DebugPageBreakingScoring</c> must DESCRIBE the page it just solved.
/// </summary>
/// <remarks>
/// The dump walks two parallel sequences — the spring builder and a list of labels — so the
/// way it rots is silent: a spring added to the chain without a label shifts every name after
/// it by one, and a reader would take the top spring's rod for a staff pair's. These tests
/// pin the two invariants that cannot survive such a shift: the labels are in the chain's own
/// order (top spring, then per system its staff pairs, then the pair to the next system, and
/// last-bottom to close), and the SOLVED lengths add back up to where the systems were
/// actually placed.
/// </remarks>
[Trait("Category", "Unit")]
public class PageChainDebugTests
{
    /// <summary>
    /// A titled grand-staff book long enough to need several pages, so the chain is reported
    /// for a page that opens with the title AND for pages that do not.
    /// </summary>
    private static string TwoStaffSource(int bars)
    {
        string rh = string.Concat(Enumerable.Repeat("c'4 d' e' f' | g'4 a' b' c'' | ", bars / 2));
        string lh = string.Concat(Enumerable.Repeat("c4 d e f | g4 a b c' | ", bars / 2));
        return $$"""
            title "Chain"
            octave absolute
            time 4/4
            part rh { clef treble }
            part lh { clef bass }
            section S {
              rh { {{rh}} }
              lh { {{lh}} }
            }
            form main { S }
            score main "chain" { staff rh staff lh }
            """;
    }

    /// <summary>
    /// Lays the fixture out with the debug hook on and returns the lines it emitted.
    /// </summary>
    /// <remarks>
    /// ⚠️ The hook is a STATIC field and this suite runs tests in parallel, so a layout on
    /// another thread would pour its own chain into this collector while the hook is set.
    /// Only lines raised on the thread that drives THIS layout are kept.
    /// </remarks>
    private static List<string> Capture(double pageHeight, int bars, out ScoreLayout layout)
    {
        var tree = SyntaxTree.Parse(TwoStaffSource(bars));
        var spec = RenderSpecParser.FindAll(tree).First();
        var score = SvgGenerator.CollectScore(tree, spec);
        var options = score.Paper with
        {
            PageHeight = pageHeight,
            UseOptimalPageBreaking = true,
        };

        var log = new List<string>();
        int mine = Environment.CurrentManagedThreadId;
        LayoutEngine.DebugPageBreakingScoring = s =>
        {
            if (Environment.CurrentManagedThreadId == mine)
                log.Add(s);
        };
        try
        {
            layout = new LayoutEngine(options).Layout(score);
        }
        finally
        {
            LayoutEngine.DebugPageBreakingScoring = null;
        }
        return log;
    }

    private sealed record Chain(string Header, List<string> Springs);

    private static List<Chain> ChainsOf(List<string> log)
    {
        var chains = new List<Chain>();
        foreach (string line in log)
        {
            if (line.StartsWith("page chain:", StringComparison.Ordinal))
                chains.Add(new Chain(line, new List<string>()));
            else if (line.TrimStart().StartsWith("spring ", StringComparison.Ordinal) && chains.Count > 0)
                chains[^1].Springs.Add(line.Trim());
        }
        return chains;
    }

    private static double Solved(string springLine)
    {
        var m = Regex.Match(springLine, @"-> solved (-?[\d.]+)");
        Assert.True(m.Success, $"no solved length in: {springLine}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EverySpringOfThePage_IsNamedInTheChainsOwnOrder()
    {
        var log = Capture(pageHeight: 60, bars: 48, out var layout);
        var chains = ChainsOf(log);

        Assert.Equal(layout.Pages.Length, chains.Count);
        Assert.True(layout.Pages.Length >= 2,
            $"the fixture must page, or this proves nothing — got {layout.Pages.Length} page(s) "
            + $"of {string.Join(",", layout.Pages.Select(p => p.Systems.Length))} system(s)");
        // ...and it must reach BOTH openings, or half of what follows is never run: a page
        // that opens with the title takes two springs down to the first staff and every other
        // page takes one, which is exactly the off-by-one a mislabelled chain would hide.
        Assert.Contains(chains, c => c.Header.Contains("(titled)", StringComparison.Ordinal));
        Assert.Contains(chains, c => !c.Header.Contains("(titled)", StringComparison.Ordinal));

        for (int p = 0; p < chains.Count; p++)
        {
            var chain = chains[p];
            var announced = Regex.Match(chain.Header, @"springs (\d+)");
            Assert.True(announced.Success, chain.Header);
            Assert.Equal(int.Parse(announced.Groups[1].Value, CultureInfo.InvariantCulture),
                chain.Springs.Count);

            // The springs are numbered from 1 and in order.
            for (int k = 0; k < chain.Springs.Count; k++)
                Assert.StartsWith($"spring {k + 1,2} ", chain.Springs[k], StringComparison.Ordinal);

            // The page OPENS with its top spring — two of them where the book title is on it
            // (top-markup down to the title, then markup-system down to the first staff).
            if (chain.Header.Contains("(titled)", StringComparison.Ordinal))
            {
                Assert.Contains("top-markup", chain.Springs[0], StringComparison.Ordinal);
                Assert.Contains("markup-system", chain.Springs[1], StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("top-system", chain.Springs[0], StringComparison.Ordinal);
            }

            // ...and CLOSES with the spring down to the foot.
            Assert.Contains("last-bottom", chain.Springs[^1], StringComparison.Ordinal);

            // One inter-system spring per adjacent pair on the page, and one staff spring per
            // sprung pair of every system on it — a grand staff of two, here.
            int systems = layout.Pages[p].Systems.Length;
            Assert.Equal(systems - 1,
                chain.Springs.Count(s => s.Contains("system-system", StringComparison.Ordinal)));
            Assert.Equal(systems,
                chain.Springs.Count(s => s.Contains("staff-staff", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void TheSolvedLengths_AddUpToWhereTheSystemsWerePlaced()
    {
        var log = Capture(pageHeight: 60, bars: 48, out var layout);
        var chains = ChainsOf(log);

        // Between two systems of one page the chain runs: this system's staff springs, then
        // the inter-system spring. Every system of this fixture has the same origin-to-first-
        // refpoint span, so that sum IS the drop from one system's origin to the next's —
        // which the placement records as the difference of their (Y-up) page positions.
        bool compared = false;
        for (int p = 0; p < chains.Count; p++)
        {
            var springs = chains[p].Springs;
            var systems = layout.Pages[p].Systems;
            // Walk the chain past its top spring(s) and read one system's worth at a time.
            int k = chains[p].Header.Contains("(titled)", StringComparison.Ordinal) ? 2 : 1;
            for (int s = 0; s + 1 < systems.Length; s++)
            {
                double drop = 0;
                while (springs[k].Contains("staff-staff", StringComparison.Ordinal))
                    drop += Solved(springs[k++]);
                Assert.Contains("system-system", springs[k], StringComparison.Ordinal);
                drop += Solved(springs[k++]);

                Assert.Equal(systems[s].Y - systems[s + 1].Y, drop, 3);
                compared = true;
            }
        }
        Assert.True(compared, "no page carried two systems, so nothing was compared");
    }

    /// <summary>
    /// The page-count costs reported beside the chosen split must BE the table the choice was
    /// made from: the count marked <c>*</c> is the one the layout used, and it is the cheapest
    /// of those reported.
    /// </summary>
    /// <remarks>
    /// What this is for: on the books where Lily# and LilyPond still page differently they
    /// agree on the system count and the line breaking, so the whole difference is which
    /// column of this table wins — and a margin of 1e-5 and a margin of 1 look identical from
    /// outside. A dump that named a count the layout did not take, or ranked them by anything
    /// but the DP's own cell, would answer that question wrongly and silently.
    /// </remarks>
    [Fact]
    public void ThePageCountCosts_RankTheCountTheLayoutTook_Cheapest()
    {
        var log = Capture(pageHeight: 60, bars: 48, out var layout);
        var costs = log.Where(l => l.StartsWith("page-count costs", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(costs);

        foreach (string line in costs)
        {
            var entries = Regex.Matches(line, @"(\d+):(-?[\d.]+)(\*?)")
                .Select(m => (
                    Count: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Cost: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    Chosen: m.Groups[3].Value == "*"))
                .ToList();
            Assert.True(entries.Count >= 2,
                $"a book that pages must be able to report an alternative: {line}");

            var chosen = Assert.Single(entries.Where(e => e.Chosen));
            Assert.Equal(chosen.Cost, entries.Min(e => e.Cost), 9);
            Assert.Equal(layout.Pages.Length, chosen.Count);
        }
    }
}
