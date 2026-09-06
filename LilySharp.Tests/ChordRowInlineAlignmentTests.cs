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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A note-attached <c>@chord</c> and the independent chord ROW standing above its staff print
/// on ONE line — the line nearest the staff — and the ROW's symbol is the one lifted off it
/// where the two want one column (owner's decision, 2026-09-06, on
/// <c>scratch/ベースタブLy/bench.lys</c>).
/// </summary>
/// <remarks>
/// ⚠️ NO FIXTURE MEASURES THIS. Counted at the time of writing: no <c>.lys</c> anywhere in the
/// tree — Fixtures, samples, audit or the owner's 300-book corpus — carries both an
/// <c>@chord</c> and a <c>chords</c> block except <c>bench.lys</c> itself, which is untracked.
/// So the rule has no observer but these cases.
/// <para>
/// ⚠️ THE DIRECTION WAS WRONG THE FIRST TIME and the tests passed: the first cut let the ROW
/// hold the line and dropped the <c>@chord</c> below it, which reads as two lines with the
/// symbols split across them. The reader saw it in the picture, not in a number. That is why
/// <see cref="RowSymbolOnAnInlineChordsColumn_IsTheOneThatLifts"/> asserts WHICH symbol moved
/// and not merely that they differ.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ChordRowInlineAlignmentTests
{
    /// <summary>Every chord symbol of a rendered two-bar score, by its printed text.</summary>
    private static Dictionary<string, ChordNameLayout> Chords(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return new LayoutEngine().Layout(score).ChordNameLayouts
            .ToDictionary(c => c.ChordText, c => c);
    }

    /// <summary>
    /// A three-bar score: a chord row with <paramref name="progression"/> over a staff whose
    /// bars 2 and 3 are given.
    /// </summary>
    /// <remarks>
    /// ⚠️ THREE BARS, NOT TWO, and the third is what makes the books legal: the merge declines
    /// when EVERY <c>@chord</c> stands on a row symbol's column (<c>ChordLineOfSystem</c>),
    /// because there would be nothing shared to show for it. A two-bar book with one colliding
    /// <c>@chord</c> is exactly that case, so it cannot be used to test the lift.
    /// </remarks>
    private static string Book(string progression, string bar2, string bar3 = "g'1",
                               string placement = "chords prog  staff m")
        => "octave absolute\n"
         + "part m { clef treble }\n"
         + (progression is null ? "" : $"chords prog {{ {progression} }}\n")
         + $"section A {{ m {{ c'1 | {bar2} | {bar3} | }} }}\n"
         + "form main { A }\n"
         + $"score main {{ {placement} }}\n";

    [Fact]
    public void InlineChord_PrintsOnTheRowsLine()
    {
        // The row speaks in bar 1, the @chord is in bar 2: nothing to argue about, one line.
        var c = Chords(Book("Cmaj7 |  |  |", "e'1@chord(Am7)"));
        Assert.Equal(c["Cmaj7"].YUp, c["Am7"].YUp, precision: 6);
    }

    [Fact]
    public void RowSymbolOnAnInlineChordsColumn_IsTheOneThatLifts()
    {
        // ★ THE OWNER'S THIRD READING, 2026-09-06, in its own words: the lower line must hold
        // `Cmaj7 F♯sus4 Emaj7' and only `Dmaj7' — the row symbol standing on the @chord's
        // column — may stack above it. So: `G7' lifts, `Am7' does NOT drop.
        // This is bench.lys's own shape: one @chord on a row symbol's column, one clear of it.
        var c = Chords(Book("Cmaj7 | G7 |  |", "e'1@chord(Am7)", bar3: "g'1@chord(Fmaj7)"));

        // The two @chord symbols and the clear row symbol are all on one line...
        Assert.Equal(c["Cmaj7"].YUp, c["Am7"].YUp, precision: 6);
        Assert.Equal(c["Cmaj7"].YUp, c["Fmaj7"].YUp, precision: 6);
        // ...and the row symbol that shares a column is the one above it (Y-up is up-positive).
        Assert.True(c["G7"].YUp > c["Am7"].YUp + 1.0,
            $"expected the row's G7 lifted off the line, got {c["G7"].YUp} vs {c["Am7"].YUp}");
        // The two really are on one column — otherwise this book proves nothing.
        Assert.Equal(c["G7"].X, c["Am7"].X, precision: 6);
    }

    [Fact]
    public void WhenEveryInlineChordIsOnARowSymbolsColumn_TheMergeDeclinesAndTheBookIsUntouched()
    {
        // ⚠️ THE REGRESSION THIS CLOSES was found in the picture, not in a number:
        // scratch/site-showcase/chord-axes.lys prints ONE progression in four spellings on
        // four lines, and its staff's @chord sits on every row symbol's column. Merging there
        // hauled the whole third row up into the second and left it ragged — two lines again,
        // drawn worse. The rule exists to put symbols ON one line; where it cannot, it must
        // leave the book alone.
        var c = Chords(Book("Cmaj7 | G7 | Am7 |", "e'1@chord(Dm7)", bar3: "g'1@chord(Fmaj7)"));

        // Every row symbol keeps the row's line...
        Assert.Equal(c["Cmaj7"].YUp, c["G7"].YUp, precision: 6);
        Assert.Equal(c["Cmaj7"].YUp, c["Am7"].YUp, precision: 6);
        // ...and both @chords keep theirs, below it, level with each other.
        Assert.Equal(c["Dm7"].YUp, c["Fmaj7"].YUp, precision: 6);
        Assert.True(c["Dm7"].YUp < c["Cmaj7"].YUp - 1.0,
            $"expected the @chord line under the row, got {c["Dm7"].YUp} vs {c["Cmaj7"].YUp}");
    }

    [Fact]
    public void RowOnBeatOne_AndAnInlineChordOnBeatThree_ShareTheLine()
    {
        // ★ OWNER, 2026-09-06: "line the Y up in cases like beat one and beat three too."
        // ⚠️ THIS IS WHY THE TEST IS THE BOXES AND NOT THE BAR. A bar-level rule — "the
        // @chord joins the row wherever the row wrote nothing in that bar" — gives the same
        // answer for an empty bar and the WRONG one here: bar 2 is written, and the two
        // symbols still have a whole half-note between them. Only the boxes know that.
        var c = Chords(Book("Cmaj7 | G7 |  |", "c'2 e'2@chord(Am7)"));

        Assert.True(c["Am7"].X > c["G7"].X + 1e-6, "the probe must put them on different columns");
        Assert.Equal(c["G7"].YUp, c["Am7"].YUp, precision: 6);
    }

    [Fact]
    public void TwoStackedRows_KeepSeparateLines_AndTheInlineChordJoinsTheNEARESTOneAboveIt()
    {
        // ★ OWNER'S QUESTION, 2026-09-06: "when several `chords` are stacked in one `score {}`
        // they must certainly keep different Ys — does the implementation hold?" It does: a
        // row's own symbols are never moved onto another line (each row is its own staff band,
        // solved by the loose-line chain), so the only thing that can travel is an @chord, and
        // it joins the row NEAREST above its staff.
        var c = Chords(Book("Cmaj7 |  |  |", "e'1@chord(Am7)",
                            placement: "chords prog as roman  chords prog  staff m"));

        double roman = c["Imaj7"].YUp, names = c["Cmaj7"].YUp, inline = c["Am7"].YUp;
        Assert.True(roman > names + 1e-6,
            $"expected the roman row above the names row, got {roman} vs {names}");
        Assert.Equal(names, inline, precision: 6);   // the LOWER of the two: nearest its staff
    }

    /// <summary>
    /// The distance from the chord ROW's staff down to the MUSIC staff — the room the band
    /// question is about.
    /// </summary>
    private static double RowToStaff(string progression, string bar2, string bar3 = "g'1")
    {
        var tree = SyntaxTree.Parse(Book(progression, bar2, bar3));
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var staves = new LayoutEngine().Layout(score)
            .Systems[0].StaffGroups.SelectMany(g => g.Staves).ToList();
        int rowStaff = score.EnumerateStaves()
            .Where(t => t.Staff.IsTextRow).Select(t => t.GlobalStaffIndex).Single();
        int musicStaff = score.EnumerateStaves()
            .Where(t => !t.Staff.IsTextRow).Select(t => t.GlobalStaffIndex).Single();
        // StaffLayout.Y is up-positive, so the row (above) is the larger number.
        return staves.Single(s => s.StaffIndex == rowStaff).Y
             - staves.Single(s => s.StaffIndex == musicStaff).Y;
    }

    [Fact]
    public void AStaffUnderAChordRow_BooksNoBandForItsOwnChordLine()
    {
        // ★ OWNER'S SECOND REPORT, 2026-09-06, on bench.lys: "the chord-name Ys line up, but
        // there is wasted space under them — it is still two lines, the names on the upper one
        // and the lower one reserved and blank." That blank line is
        // MultiStaffLayouter.ReserveChordRowBand, booked above a staff that carries @chord.
        //
        // ⚠️ THE CLAIM IS AN EQUALITY, NOT A DROP: once the row has taken the symbol, a staff
        // that carries @chord must cost EXACTLY what one with none costs. Measuring only "less
        // than before" would pass for a band that merely shrank.
        double none   = RowToStaff("Cmaj7 |  |  |", "e'1");             // no @chord at all
        double joined = RowToStaff("Cmaj7 |  |  |", "e'1@chord(Am7)");  // one, on the row's line

        Assert.Equal(none, joined, precision: 6);

        // ⚠️ NO ARM HERE FOR THE LIFTED CASE, and the reason is worth writing down: a merged
        // line carries ink over bars the row alone did not, so it can be spaced further from
        // the staff for a REAL reason — the per-X skyline distance to a high note under a bar
        // the row was silent in. Measured, that is 0.995000 on `Cmaj7 | G7 |  |' over
        // `c'1 | e'1 | g'1'. Asserting equality there would be asserting that the row need not
        // clear the music, and the band this test is about is a flat 2-plus staff spaces.
        // The band gate's own liveness is shown by poisoning it, not by this book.
    }

    [Fact]
    public void WithNoRowAboveIt_AnInlineChordKeepsItsOwnLine()
    {
        // ★ THE CONTROL FOR ALL OF THE ABOVE: with no chord row in the score, nothing in this
        // island runs — the @chord is placed by the old 0.6-plus-protrusion arm and the staff
        // books its band as it always did. Every book in the corpus is this book.
        var tree = SyntaxTree.Parse(
            Book(progression: null, "e'1@chord(Am7)", placement: "staff m"));
        Assert.False(tree.HasErrors,
            string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);

        var inline = Assert.Single(layout.ChordNameLayouts);
        Assert.Equal(-1, inline.RowStaffIndex);   // on no row's line

        double staffY = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves).Single().Y;
        Assert.Equal(0.65, inline.YUp - staffY, precision: 6);   // the measured no-protrusion distance
    }
}
