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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The words of a <c>pedal text</c> staff stand as LilyPond 2.26.0 sets them (Lab
/// sessions/p1384/m): a word's line spanner lives one timestep past it, so words on
/// consecutive timesteps share one row, and a change is ONE word, "*Ped.", centred on its
/// column.
/// </summary>
public class PedalTextRowTests
{
    private static ScoreLayout Laid(string music)
    {
        string source = "octave absolute\ntime 4/4\npaper { raggedRight }\npart m { clef treble  pedal text }\n"
            + $"section A {{ m {{\n{music}\n}} }}\nform {{ A }}\nscore {{ staff m }}\n";
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        return new LayoutEngine().Layout(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static MusicMarkLayout[] Words(ScoreLayout layout) => [.. layout.MusicMarkLayouts
        .Where(m => m.MarkType is MusicMarkType.SustainOn or MusicMarkType.SustainOff)
        .OrderBy(m => m.X)];

    /// <summary>The "*" of the note right after its "Ped." (t5): LilyPond sets both on the
    /// lower word's baseline (ink bottoms −6.300); Lily# solved each alone and left "Ped."
    /// 0.35 higher.</summary>
    [Theory]
    [InlineData("c''2@sustain c'2@!sustain | g''1 |")]          // t5: the next note, in the bar
    [InlineData("c'1@sustain | d'1@!sustain |")]                // t2: the next note, past a bar line
    public void AReleaseOnTheNextNote_SharesItsEngagesRow(string music)
    {
        var words = Words(Laid(music));
        Assert.Equal(2, words.Length);
        Assert.Equal(words[0].YUp, words[1].YUp, 9);
    }

    /// <summary>Not on the next timestep (t4): two spanners, two rows — each word's own.</summary>
    [Fact]
    public void AReleaseLaterOn_KeepsItsOwnRow()
    {
        var words = Words(Laid("c''2@sustain d''2 | e''2 f''2@!sustain |"));
        Assert.Equal(2, words.Length);
        Assert.True(words[1].YUp - words[0].YUp > 0.5, $"{words[0].YUp:F3} / {words[1].YUp:F3}");
    }

    /// <summary>A change right before a ledger-line note (t8): the "d" of "*Ped." stands over
    /// the c''s ledger, which no note column holds (LedgerLineSpanner), so LilyPond keeps the
    /// staff-floor row (−5.250, as with no ledger there). The allotted span read the ledger
    /// and set all three words 0.266 low.</summary>
    [Fact]
    public void AChangeOverTheNextNotesLedger_KeepsItsRow()
    {
        var ledger = Words(Laid("g''8@sustain g''8@!sustain@sustain c'8 g''8 g''2 | g''1@!sustain |"));
        var none = Words(Laid("g''8@sustain g''8@!sustain@sustain g''8 g''8 g''2 | g''1@!sustain |"));
        Assert.Equal(none[0].YUp, ledger[0].YUp, 9);
        Assert.Equal(none[1].YUp, ledger[1].YUp, 9);
    }

    /// <summary>A change (t6): "*Ped." over the column 3.002 right of the first "Ped.", the
    /// "*" (1.5556 wide) at 0.488..2.044 and the "Ped." (3.472) at 2.044..5.516, all three
    /// words on the first one's row. Lily# kept the change's "Ped." centred on the column,
    /// set the "*" 0.4 to its left, and stacked that "Ped." 2.27 under the first, which it
    /// overlaps.</summary>
    [Fact]
    public void AChange_IsOneWordOnItsColumn_OnTheEngagesRow()
    {
        var words = Words(Laid("c''4@sustain d''4@!sustain@sustain e''4@!sustain c'4 | g''1 |"));
        Assert.Equal(4, words.Length);
        var first = words[0];
        var star = words.Single(w => w.MarkType == MusicMarkType.SustainOff && w.X < 3.0 + first.X);
        var engage = words.Single(w => w.MarkType == MusicMarkType.SustainOn && w != first);
        Assert.Equal(1.266, star.X - first.X, 3);
        Assert.Equal(3.780, engage.X - first.X, 3);
        Assert.All(words.Where(w => w.X <= engage.X), w => Assert.Equal(first.YUp, w.YUp, 9));
    }

    /// <summary>A grace note between "Ped." and "*" is a timestep of its own (Lab
    /// sessions/p1391/m g1 against g0): the "*" is no longer on the next step, so it is its
    /// own spanner and sits 1.242 higher than the "Ped." over the low c' — LilyPond's ink
    /// −5.255 against −6.497. Lily# counted only main columns and kept the row.</summary>
    [Fact]
    public void AGraceBetweenTwoWords_EndsTheRun()
    {
        var plain = Words(Laid("c'2@sustain d''2@!sustain | g''1 |"));
        Assert.Equal(plain[0].YUp, plain[1].YUp, 9);
        var graced = Words(Laid("c'2@sustain grace { g''16 } d''2@!sustain | g''1 |"));
        Assert.Equal(2, graced.Length);
        Assert.Equal(1.242, graced[1].YUp - graced[0].YUp, 2);
    }

    /// <summary>A word written on a grace note stands at its grace column (g3): the "*" is
    /// centred on the grace head's left edge, 3.620 right of the "Ped." — not on the d''
    /// it leans on (5.018), where Lily# set it.</summary>
    [Fact]
    public void AWordOnAGrace_StandsAtItsGraceColumn()
    {
        var words = Words(Laid("c'2@sustain grace { g''16@!sustain } d''2 | g''1 |"));
        Assert.Equal(2, words.Length);
        Assert.Equal(3.620, words[1].X - words[0].X, 2);
    }

    /// <summary>A change on a grace note is one "*Ped." word on the grace column, apart from
    /// the d''s own "*" (g2: LilyPond's "*Ped." at 1.106..6.134, the "*" at 4.240..5.796).
    /// Lily# grouped the grace's words with the d''s, and drew two "*" on top of each other.</summary>
    [Fact]
    public void AChangeOnAGrace_IsOneWordOnItsColumn()
    {
        var words = Words(Laid("c'2@sustain grace { g''16@!sustain@sustain } d''2@!sustain | g''1 |"));
        Assert.Equal(4, words.Length);
        double x0 = words[0].X;
        Assert.Collection(words.Skip(1),
            w => { Assert.Equal(MusicMarkType.SustainOff, w.MarkType); Assert.Equal(1.884, w.X - x0, 2); },
            w => { Assert.Equal(MusicMarkType.SustainOn, w.MarkType); Assert.Equal(4.398, w.X - x0, 2); },
            w => { Assert.Equal(MusicMarkType.SustainOff, w.MarkType); Assert.Equal(5.018, w.X - x0, 2); });
    }

    /// <summary>A word on a grace is supported by its grace column (g4): "Ped." on
    /// `grace { c'16 }` before a g'' hangs under the grace's low c' and its ledger — LilyPond's
    /// ink −6.346..−4.346, 0.526 lower than the g''s column alone would set it, where Lily#
    /// set it — and the "*" on the next bar's g'' is its own row at −4.806.</summary>
    [Fact]
    public void AWordOnAGrace_IsSupportedByItsGraceColumn()
    {
        var onLowGrace = Words(Laid("g''2 grace { c'16@sustain } g''2 | g''1@!sustain |"));
        var onHighGrace = Words(Laid("g''2 grace { g''16@sustain } g''2 | g''1@!sustain |"));
        Assert.Equal(2, onLowGrace.Length);
        Assert.True(onHighGrace[0].YUp - onLowGrace[0].YUp > 0.4, $"{onLowGrace[0].YUp:F3} / {onHighGrace[0].YUp:F3}");
        Assert.Equal(1.540, onLowGrace[1].YUp - onLowGrace[0].YUp, 2);
    }

    /// <summary>A release on a grace and an engage on the note after it are two moments, not a
    /// change (g5): the "*" stays centred on the grace (3.620) and the "Ped." on the d''
    /// (5.018). Lily# took them for one "*Ped." on the d''.</summary>
    [Fact]
    public void AReleaseOnAGrace_AndAnEngageAfterIt_AreNoChange()
    {
        var words = Words(Laid("c'2@sustain grace { g''16@!sustain } d''2@sustain | g''1@!sustain |"));
        Assert.Equal(4, words.Length);
        Assert.Equal(3.620, words[1].X - words[0].X, 2);
        Assert.Equal(5.018, words[2].X - words[0].X, 2);
        Assert.All(words, w => Assert.Equal(words[0].YUp, w.YUp, 9));
    }

    private static MusicMarkLayout[] UnaCordaWords(ScoreLayout layout) => [.. layout.MusicMarkLayouts
        .Where(m => m.MarkType is MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff)
        .OrderBy(m => m.X)];

    /// <summary>A una corda change prints nothing — LilyPond's middle string is "" (Lab
    /// sessions/p1390/m u2). Lily# drew "una corda" on the column and "tre corde" 0.4 left of
    /// it, over the first "una corda".</summary>
    [Fact]
    public void AUnaCordaChange_PrintsNothing()
    {
        var words = UnaCordaWords(Laid("c''4@unaCorda d''4@treCorde@unaCorda e''4@treCorde c'4 | g''1 |"));
        Assert.Collection(words,
            w => Assert.Equal(MusicMarkType.UnaCordaOn, w.MarkType),
            w => Assert.Equal(MusicMarkType.UnaCordaOff, w.MarkType));
        Assert.Equal(6.004, words[1].X - words[0].X, 2);
    }

    /// <summary>The silent change still carries the row (u3): LilyPond's empty grob joins
    /// the line spanner, so "tre corde" on the next step stays on the first word's row
    /// (ink −5.812..−4.158 both). Without a change between them (u4) the same "tre corde"
    /// is its own spanner and stacks under the "una corda" it overlaps (−7.671).</summary>
    [Fact]
    public void AUnaCordaChange_CarriesTheRowToTheNextWord()
    {
        var changed = UnaCordaWords(Laid("c'4@unaCorda d''4@treCorde@unaCorda g''4@treCorde r4 | g''1 |"));
        Assert.Equal(2, changed.Length);
        Assert.Equal(changed[0].YUp, changed[1].YUp, 9);
        var plain = UnaCordaWords(Laid("c'4@unaCorda d''4 g''4@treCorde r4 | g''1 |"));
        Assert.Equal(2, plain.Length);
        Assert.True(plain[0].YUp - plain[1].YUp > 1.5, $"{plain[0].YUp:F3} / {plain[1].YUp:F3}");
    }

    /// <summary>The silent change has no ink to clear (u5 against u6): a sustain "Ped." on the
    /// change's column stands where it stands with no change there — LilyPond's ink
    /// −5.451..−3.451 in both books. Lily# stacked it 3.97 lower, under the two words.</summary>
    [Fact]
    public void AUnaCordaChange_LeavesNoInkForTheRowsBelow()
    {
        double[] SustainRows(string music) => [.. Laid(music).MusicMarkLayouts
            .Where(m => m.MarkType is MusicMarkType.SustainOn or MusicMarkType.SustainOff)
            .OrderBy(m => m.X).Select(m => m.YUp)];
        var changed = SustainRows("c''1@unaCorda | d''2 d''2@treCorde@unaCorda@sustain | r1@!sustain | e''1@treCorde |");
        var plain = SustainRows("c''1@unaCorda | d''2 d''2@sustain | r1@!sustain | e''1@treCorde |");
        Assert.Equal(2, changed.Length);
        Assert.Equal(plain, changed, (a, b) => Math.Abs(a - b) < 1e-9);
    }
}
