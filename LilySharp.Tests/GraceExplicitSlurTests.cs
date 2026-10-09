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
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A hand-written slur in grace time is an ordinary slur. <c>grace { g16( } a8)</c> draws the
/// bow an <c>appoggiatura</c> draws on its own (session 328, owner decision: LilyPond prints
/// it, so does Lily#), and since session 725 every other place a slur mark can stand in a
/// grace body draws too — on an earlier column, on a grace rest, or wholly inside the body.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/grace-init.ly startGraceSlur / stopGraceSlur — an appoggiatura IS a grace
/// with a slur event on its last note and the end on the main note, so the two spellings are
/// one picture in LilyPond. The pair is asserted as PAGE EQUALITY against the keyword rather
/// than as "a path exists": both are one ordinary slur (ElementCoordinator.WithGraceSlurs
/// makes the keyword's, SlurDetector pairs the hand-written one), and a page that told them
/// apart would be the second spelling RULES §5.2.1② names.
/// <para>
/// The other shapes were LYS4020 drops until session 725 (HANDOFF §2 U8 ⒝2's slur half):
/// LilyPond's Slur_engraver is a Voice engraver and does not know grace time, so a mark
/// anywhere in the body is paired like any other. Their geometry is in the LP fidelity ledger
/// (slur.in-grace.*); here each is asserted to be INK and to warn about nothing.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class GraceExplicitSlurTests
{
    private static string Book(string music)
        => "time 4/4\npart m { clef treble }\nsection A { m {\n" + music + "\n} }\n"
           + "form { ~A }\nscore { staff m }\n";

    /// <summary>The page with every source offset masked, so two books that write the same
    /// music at different lengths differ in nothing.</summary>
    private static string Page(string music)
        => Regex.Replace(LiveRender.Svg(Book(music)), "data-pos=\"\\d+\"", "data-pos=\"#\"");

    private static IReadOnlyList<Diagnostic> GraceDrops(string music)
    {
        var validator = new GraceBodyValidator();
        validator.Validate(SyntaxTree.Parse(Book(music)));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnengravedGraceContent).ToList();
    }

    private static IReadOnlyList<Diagnostic> UnpairedSlurs(string music)
    {
        var validator = new SlurPairingValidator();
        validator.Validate(SyntaxTree.Parse(Book(music)));
        return validator.Diagnostics.Where(d => d.Code == DiagnosticCodes.UnpairedSlur).ToList();
    }

    [Fact]
    public void TheHandWrittenPair_IsTheAppoggiaturasBow_AndNothingIsReported()
    {
        const string written = "c4 grace { g16( } a8) c4 d | e1 |";
        const string keyword = "c4 appoggiatura { g16 } a8 c4 d | e1 |";
        const string bare = "c4 grace { g16 } a8 c4 d | e1 |";

        Assert.Equal(Page(keyword), Page(written));
        // The positive control: the bow is INK — the bare grace is a different page.
        Assert.NotEqual(Page(bare), Page(written));

        Assert.Empty(GraceDrops(written));
        Assert.Empty(UnpairedSlurs(written));
    }

    [Fact]
    public void AnOpenTheMainNoteDoesNotClose_IsUnpaired_AndDrawsNoBow()
    {
        const string open = "c4 grace { g16( } a8 c4 d | e1 |";
        const string bare = "c4 grace { g16 } a8 c4 d | e1 |";

        Assert.Equal(Page(bare), Page(open));
        Assert.Empty(GraceDrops(open));
        var warning = Assert.Single(UnpairedSlurs(open));
        Assert.Contains("never closed", warning.Message);
        // At the note the `(` was written on, as for an unclosed slur on any note
        // (SlurPairingScanner reports the bound item's position).
        Assert.Equal(Book(open).IndexOf("g16(", System.StringComparison.Ordinal), warning.Span.Start);
    }

    [Theory]
    // A `(` on the FIRST of two grace notes, closed on the main note.
    [InlineData("c4 grace { f16( g16 } a8) c4 d | e1 |", "c4 grace { f16 g16 } a8 c4 d | e1 |")]
    // ⚠️ NOT A ROW: a `(` on a grace REST (`grace { g16 r16( } a8)`). Session 725 wrote it off
    // because `r16( d)` reported LYS4010 "')' has no '(' open" — session 726 found the slur WAS
    // drawn and only the warning was wrong (SlurPairingScanner now asks SlurDetector), so the
    // row is not blocked any more; it has not been written.
    // Both ends inside the body.
    [InlineData("c4 grace { d'16( e') } c4 d e | e1 |", "c4 grace { d'16 e' } c4 d e | e1 |")]
    // From a main note into a grace body.
    [InlineData("c4( grace { d'16) } e4 d e | e1 |", "c4 grace { d'16 } e4 d e | e1 |")]
    public void ASlurAnywhereInGraceTime_IsDrawn_AndNothingIsReported(string written, string control)
    {
        Assert.NotEqual(Page(control), Page(written));
        Assert.Empty(GraceDrops(written));
        Assert.Empty(UnpairedSlurs(written));
    }

    /// <summary>
    /// On a TAB staff a slur bound on a grace column is drawn — the grace's own digit is the
    /// bound (ElementCoordinator.BuildTabSlurLayout, session 730; until then it was skipped and
    /// this test pinned the gap). LilyPond draws it DOWN, under the digits
    /// (audit/lp-geometry/probes/tab-grace-slur.ly; ledger slur.tab.grace.*).
    /// <para>
    /// It counts the BOWS (the Bézier paths), not the page: the slur's spacing rod moves the
    /// written run's columns (session 727), so the two pages differ by spacing as well.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("c,4 r g,, r | c, r grace { d16( } e4) g, |", "c,4 r g,, r | c, r grace { d16 } e4 g, |")]
    [InlineData("c,4 r g,, r | c, r grace { d16( e16) } f4 g, |", "c,4 r g,, r | c, r grace { d16 e16 } f4 g, |")]
    public void OnATabStaff_AGraceSlurIsDrawn(string written, string control)
    {
        // Through the render block: LiveRender.Svg draws the part on a notation staff.
        static string TabPage(string music) => Regex.Replace(LiveRender.SvgFromRenderSpec(
            "octave absolute\ntime 4/4\npart bl { clef bass tuning bass }\nsection A { bl {\n" + music
            + "\n} }\nform { ~A }\nscore { tab bl }\n"), "data-pos=\"\\d+\"", "data-pos=\"#\"");
        static int Bows(string page) => Regex.Matches(page, "<path d=\"M[^\"]* C ").Count;
        Assert.Equal(Bows(TabPage(control)) + 1, Bows(TabPage(written)));
    }

    /// <summary>The twin writes both marks, and LilyPond draws its Slur from them.</summary>
    [Fact]
    public void TheTwin_CarriesBothMarks()
    {
        var tree = SyntaxTree.Parse(Book("c4 grace { g16( } a8) c4 d | e1 |"));
        Assert.False(tree.HasErrors);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\grace { g16 ( } a8 )", Regex.Replace(ly, @"\s+", " "));
    }
}
