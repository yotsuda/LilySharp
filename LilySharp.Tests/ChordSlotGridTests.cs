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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The chord row's measure-relative placement, end to end (GRAMMAR_AUDIT 8.1):
/// a bar's written slots — entries, rests, <c>.</c> extensions — divide it on
/// the meter's beat grid, a <c>.</c> merges into the previous entry's time — or, at a
/// bar's head, is the bar's silent slot — and the one grid fault speaks (LYS2009
/// off-grid; the bar-head '.' was LYS2010 until 2026-09-04).
/// </summary>
[Trait("Category", "Unit")]
public class ChordSlotGridTests
{
    private static string Sheet(string row, string time = "4/4") =>
        $"time {time}\nsection Main {{\n  chords prog {{ {row} }}\n}}\n" +
        "form main { Main }\nscore main { chords prog }\n";

    private static (string Text, double Timing)[] Chords(string src)
    {
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return new MeasureCollector().CollectMultiStaff(tree, spec)
            .ChordNames.OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing.ToDouble())
            .Select(c => (c.ChordText, c.Timing.ToDouble())).ToArray();
    }

    [Fact]
    public void ADotHoldsThePreviousChord_OneMoreSlot()
    {
        // | C . . G7 | — C opens and holds three beats, G7 takes the fourth.
        var chords = Chords(Sheet("C . . G7 |"));
        Assert.Equal([("C", 0.0), ("G7", 0.75)], chords);
    }

    [Fact]
    public void ADotMergesIntoOneSpacer_LikeTheDottedDurationItReplaced()
    {
        // The row's layout skeleton carries TWO spacers (a dotted half and a
        // quarter), exactly as the explicit `c2. g4:7` used to — a '.' is not a
        // column of its own.
        var tree = SyntaxTree.Parse(Sheet("C . . G7 |"));
        var spec = RenderSpecParser.FindFirst(tree)!;
        var measures = new MeasureCollector().CollectMultiStaff(tree, spec)
            .StaffGroups.SelectMany(g => g.Staves).SelectMany(s => s.Voices).First().Measures;
        var spacers = measures[0].Items.OfType<RestItem>().ToList();
        Assert.Equal(2, spacers.Count);
        Assert.Equal(new Fraction(3, 4), spacers[0].Duration);
        Assert.Equal(new Fraction(1, 4), spacers[1].Duration);
    }

    [Fact]
    public void SixEight_TwoEntries_AreTheTwoDottedQuarterBeats()
    {
        var chords = Chords(Sheet("Am E |", time: "6/8"));
        Assert.Equal([("Am", 0.0), ("E", 0.375)], chords);
    }

    [Fact]
    public void OffTheGrid_WarnsAndDividesEqually()
    {
        // 3 slots in 4/4 fit no beat: LYS2009 (a WARNING — the bar still renders,
        // divided equally) names it once.
        var src = Sheet("C F G |");
        var d = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(src)),
            x => x.Code == DiagnosticCodes.ChordSlotMismatch);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);

        Assert.Equal([("C", 0.0), ("F", 1.0 / 3), ("G", 2.0 / 3)], Chords(src));
    }

    [Fact]
    public void OnTheGrid_StaysSilent()
    {
        foreach (var row in new[] { "C |", "C F |", "C F G . |", "C F G A |", ". C |", "C | . |" })
            Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(Sheet(row))),
                x => x.Code == DiagnosticCodes.ChordSlotMismatch);
    }

    /// <summary>
    /// A <c>.</c> at a bar's head is the bar's SILENT slot (owner decision 2026-09-04,
    /// HANDOFF §3): it prints nothing, its time passes, and the chord that follows lands on
    /// the next beat of the grid. That is the one job the retired <c>s</c> spacer had — an
    /// empty bar being <c>| |</c> — and until that day this spelling was refused (LYS2010).
    /// </summary>
    [Fact]
    public void ADotAtTheBarHead_IsTheSilentSlot()
    {
        // | C | . G | — no chord on the second bar's first half, G on its second.
        Assert.Equal([("C", 0.0), ("G", 0.5)], Chords(Sheet("C | . G |")));
        // | C | . | — a bar of nothing, its width kept: no symbol, no diagnostic.
        Assert.Equal([("C", 0.0)], Chords(Sheet("C | . |")));
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(Sheet("C | . G |")))
            .Where(x => x.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void AReplayedSection_ReportsEachGridFaultOnce()
    {
        // The structure replays the section's bars per occurrence; one written
        // fault is one diagnostic, not one per occurrence.
        var src = "time 4/4\nsection A {\n  chords prog { C F G | }\n}\n" +
                  "form main { A A \"A2\" }\nscore main { chords prog }\n";
        Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(src)),
            x => x.Code == DiagnosticCodes.ChordSlotMismatch);
    }
}
