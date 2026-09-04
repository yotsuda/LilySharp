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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A lead sheet's rows stand ABOVE its staff, and the page's top spring has to price them.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/page-layout-problem.cc:1093-1122 <c>build_system_skyline</c> merges
/// every element's own skyline at its own translation and then leaves the result anchored on
/// the first SPACEABLE staff (<c>up->raise (-first_spaceable_dy)</c>); :625-630
/// <c>append_system</c> floors the spring with <c>up_skyline.distance (bottom_skyline_)</c>,
/// where <c>bottom_skyline_</c> is the bottom of the header.
/// <para>
/// ⚠️ WHY IT IS A TEST AND NOT A LEDGER POINT. The ledger's page family measures the first
/// staff's REFPOINT, and on every probe book that carries a chord row the floor loses to
/// <c>top-system-spacing</c>'s basic-distance 6 — MEASURED on book LYRCH, whose ChordName ink
/// reaches 4.998884 above that refpoint, so the floor comes to 5.998884 and the reading is
/// 11.690551 either way. A book where the floor BINDS needs a taller stack over the staff
/// than any probe carries, which is exactly the shape the user reported. This asserts the
/// RELATION the floor exists for — the header's ink and the system's ink do not meet —
/// which no page number can be written down for without such a book.
/// </para>
/// <para>
/// ⚠️ THE THIRD ARM IS THE CONTROL AND IT IS THE POINT: a score whose topmost element IS the
/// staff must not move at all, because that is the case where the origin and the first
/// spaceable staff's top line coincide and the conversion is the identity. MEASURED over the
/// LP regression corpus: 79 of 81 books byte-identical, the two that moved
/// (<c>chord-names-bass</c>, <c>chord-names-in-grand-staff</c>) both chord-row books and both
/// landing on LilyPond 2.26.0 — 12.05 against LilyPond's 12.0719 and 10.10 against 10.0927,
/// from 9.69 on both before.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LeadSheetHeaderClearanceTests
{
    /// <summary>The user's report, reduced: title + composer over `chords / lyrics / staff`.</summary>
    private const string LeadSheetWithHeader = """
        title "T"
        composer "C"
        key c major
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 }
        }
        chords prog {
          section A { Dmaj7 | Em7 }
        }
        lyrics verse {
          section A { la la la la | la la la | }
        }
        form main { A }
        score main {
          chords prog as names
          lyrics verse sings melody
          staff melody
        }
        """;

    /// <summary>The same book with the two rows removed — the topmost element IS the staff.</summary>
    private const string StaffOnlyWithHeader = """
        title "T"
        composer "C"
        key c major
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 }
        }
        form main { A }
        score main {
          staff melody
        }
        """;

    /// <summary>
    /// No chord symbol may be printed inside the header's ink.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE ASSERTION IS THE ROW'S OWN RESERVED ASCENT, not a measured constant: the row is
    /// reserved by the ink <c>ChordNameEngraver.RowSkylines</c> builds, and asking for a
    /// smaller clearance than that would pass while the symbols still overlapped a taller
    /// name.
    /// <para>
    /// ⚠️ IT WAS THE LITERAL 1.907250371 UNTIL 2026-08-26, which is the drawn ink of THESE
    /// names and of no others: <c>D</c> and <c>E</c> are flat-topped, so it is the face's cap
    /// height. A ROUND CAPITAL REACHES 0.047092602 HIGHER (<c>C</c> is 0.747 em against the
    /// cap's 0.729), so the arm was asking for less clearance than a book spelling <c>C</c>
    /// or <c>G</c> actually needs — the very failure its own first sentence warns about.
    /// Found by ledger <c>page.chord-row.staff-to-chord-baseline</c>, which is the first
    /// entry in the corpus to print a round capital.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordRowAboveTheStaff_IsSpacedBelowTheHeader()
    {
        var g = RenderedGeometry.Render(LeadSheetWithHeader);
        var options = new LayoutOptions();
        double headerBottom = HeaderBottom(options);

        var chords = g.ChordSymbols;
        Assert.NotEmpty(chords);
        foreach (var c in chords)
        {
            double inkTop = c.Y - ChordNameEngraver.SymbolInk(ScoreTextMetrics.Bundled, c.Text).Top;
            Assert.True(
                inkTop >= headerBottom,
                $"chord symbol \"{c.Text}\" reaches {inkTop:F6} — above the header's bottom "
                + $"{headerBottom:F6}, i.e. printed through the title.");
        }
    }

    /// <summary>
    /// ...and neither may a lyric syllable, which is the row the reported book had between
    /// the chords and the staff.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS ARM IS A GUARD, NOT A REPRODUCTION, and saying so is the point: poisoned by
    /// reverting the port it stays GREEN, because on the reported book only the CHORD row
    /// reached into the title — the lyric row under it cleared the composer by its own
    /// spacing. What it watches is the other half of the same stack, which nothing else
    /// asserts: a book whose row order puts the syllables on top would fail here first.
    /// The arm that fails on the unported engine is
    /// <see cref="ChordRowAboveTheStaff_IsSpacedBelowTheHeader"/>.
    /// </remarks>
    [Fact]
    public void LyricRowAboveTheStaff_IsSpacedBelowTheHeader()
    {
        var g = RenderedGeometry.Render(LeadSheetWithHeader);
        var options = new LayoutOptions();
        double headerBottom = HeaderBottom(options);

        var syllables = g.Texts.Where(t => t.Text == "la").ToList();
        Assert.NotEmpty(syllables);
        // The lyric ascender at the 3.2 ss lyric font, the figure the engine reserves.
        foreach (var s in syllables)
            Assert.True(
                s.Y - 2.11 >= headerBottom,
                $"lyric syllable reaches {s.Y - 2.11:F6}, above the header's bottom "
                + $"{headerBottom:F6}.");
    }

    /// <summary>
    /// THE CONTROL. With no row over the staff the origin IS the staff's top line, so the
    /// conversion this island corrects is the identity and the anchor must be LilyPond's own
    /// <c>top-margin + top-system-spacing</c>'s basic-distance — the number
    /// audit/lp-geometry <c>page.first-staff-refpoint</c> already pins.
    /// </summary>
    [Fact]
    public void StaffTopmost_KeepsLilyPondsTopSystemAnchor()
    {
        var g = RenderedGeometry.Render(StaffOnlyWithHeader);
        var options = new LayoutOptions();
        double headerBottom = HeaderBottom(options);

        // The title column's bottom + markup-system-spacing's floor (padding 0.5 + the
        // system's ink over the refpoint). This book's ink above the refpoint is the section
        // label's, so the floor binds; what the control fixes is that the anchor is built from
        // the STAFF and nothing else stands over it.
        double refpoint = g.FirstStaffRefpoint();
        Assert.True(
            refpoint >= headerBottom + options.VerticalSpacing.MarkupSystem.Padding,
            $"first staff refpoint {refpoint:F6} is inside the header + padding.");
        Assert.True(
            refpoint <= headerBottom + 12,
            $"first staff refpoint {refpoint:F6} is implausibly far down for a staff-topped "
            + "system — the rows-above conversion has leaked into a score that has no rows.");
    }

    /// <summary>
    /// Where the header's ink ENDS on a single-page book: the title column's top is
    /// top-markup-spacing's length below the margin (4 at rest — a page that fits stands at
    /// force 0), and the column reaches its own depth below that.
    /// </summary>
    private static double HeaderBottom(LayoutOptions options)
        => options.MarginTop
           + LayoutUtilities.TitleTopSpring(options.VerticalSpacing).Length(0)
           + HeaderBand.Build("T", "C", ScoreTextMetrics.Bundled)!.Depth;

    /// <summary>
    /// The column stacks its rows as bookTitleMarkup does: the composer's baseline is
    /// baseline-skip below the title's when that is the larger step, and the ink height of
    /// the two rows otherwise — never the sum of two font sizes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/titling-init.ly bookTitleMarkup (line 69) baseline-skip 3.5; scm/stencil.scm
    /// stack-lines (lines 153-168) via ly:stencil-stack — reference points at least the skip apart, extents at least
    /// touching. Poisoned by dropping either arm of the max, one of the two books here fails.
    /// </remarks>
    [Fact]
    public void HeaderBand_StacksTheComposerUnderTheTitle_BySkipOrByInk()
    {
        var fonts = ScoreTextMetrics.Bundled;

        // "T" over "C": a short title with no descender, a cap-height composer — the skip binds.
        var skipBound = HeaderBand.Build("T", "C", fonts)!;
        var titleInk = fonts.Ink("T", HeaderBand.TitleFontSize, TextRole.Title, FontStyle.Bold);
        var composerInk = fonts.Ink("C", HeaderBand.ComposerFontSize, TextRole.Composer);
        Assert.Equal(titleInk.Top, skipBound.TitleBaseline!.Value, 9);
        Assert.Equal(titleInk.Top + HeaderBand.BaselineSkip, skipBound.ComposerBaseline!.Value, 9);
        Assert.Equal(skipBound.ComposerBaseline!.Value - composerInk.Bottom, skipBound.Depth, 9);

        // A title alone is exactly its ink; a composer alone exactly its.
        Assert.Equal(titleInk.Top - titleInk.Bottom, HeaderBand.Build("T", null, fonts)!.Depth, 9);
        Assert.Equal(composerInk.Top - composerInk.Bottom, HeaderBand.Build(null, "C", fonts)!.Depth, 9);
        Assert.Null(HeaderBand.Build(null, null, fonts));

        // The other arm: a title whose descender reaches further than the skip leaves room
        // for is pushed apart by INK. "gjpqy" descends about 0.2 em at 3.49; the composer's
        // ascender at 2.2 rises about 0.7 em — together more than 3.5 − the title's ascent
        // only when the title is all descenders, so the arm is asserted on its own terms:
        // the composer baseline is never above the title's ink bottom plus its own ink top.
        var deep = HeaderBand.Build("gjpqy", "ÅÉÎ", fonts)!;
        var deepTitle = fonts.Ink("gjpqy", HeaderBand.TitleFontSize, TextRole.Title, FontStyle.Bold);
        var tallComposer = fonts.Ink("ÅÉÎ", HeaderBand.ComposerFontSize, TextRole.Composer);
        double byInk = (deepTitle.Top - deepTitle.Bottom) + tallComposer.Top;
        double bySkip = deepTitle.Top + HeaderBand.BaselineSkip;
        Assert.Equal(Math.Max(byInk, bySkip), deep.ComposerBaseline!.Value, 9);
    }

    /// <summary>The same book twice, one word apart: a ROUND capital against a FLAT one.</summary>
    private static string ChordRowOverAStaff(string chord) => $$"""
        key c major
        part melody {
          clef treble
          section A { g4 a g a | g a g a }
        }
        chords prog {
          section A { {{chord}} | {{chord}} }
        }
        form main { A }
        score main {
          chords prog as names
          staff melody
        }
        """;

    /// <summary>
    /// A chord row stands on its INK, not on its baseline — so a letter that dips below the
    /// baseline is placed no lower for it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1257-1338 <c>get_spacing_spec</c> with
    /// :622-630 — a loose line is spaced by <c>Skyline::distance</c>, i.e. by where its INK
    /// is, and the baseline is wherever that leaves it. MEASURED against LilyPond 2.26.0 on
    /// ledger book CHR1: LilyPond stands the row's ink bottom at 3.000000 over the staff
    /// refpoint (the up stem tops — <c>a'</c> at -0.5 under a 3.5 stem) and Lily# stands it
    /// at 3.000000000, while the two BASELINES differ by 0.013091398 because the faces give
    /// <c>C</c> a different overshoot.
    /// <para>
    /// ⚠️ WHY IT IS THIS PAIR AND NOT A NUMBER. Asserting "the ink bottom is 3.000000" would
    /// pin the melody as much as the rule, and asserting the baseline would pin the FACE:
    /// LilyPond sets chord names in Nimbus Sans and Lily# in TeX Gyre Heros, two URW faces
    /// that agree exactly on <c>A</c>, on <c>m</c> and on every advance and differ only in
    /// the overshoot of a round capital, so a baseline assertion here would turn a font
    /// choice into a layout invariant. The DIFFERENCE between two books one word apart is
    /// free of both.
    /// </para>
    /// <para>
    /// ⚠️ THE SECOND ASSERTION IS WHAT MAKES THE FIRST MEAN SOMETHING: the baselines MUST
    /// differ. Without it an engine that placed every row's baseline at a constant would pass
    /// the first line and fail the rule, and that is precisely the defect this watches for.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordRowStandsOnItsInk_NotOnItsBaseline()
    {
        var fonts = ScoreTextMetrics.Bundled;
        double Baseline(string chord)
        {
            var g = RenderedGeometry.Render(ChordRowOverAStaff(chord));
            return g.ChordBaselineAboveStaff();
        }

        // "A" sits flat on its baseline; "C" dips 0.018 em below it.
        double flatDip = ChordNameEngraver.SymbolInk(fonts, "A").Bottom;
        double roundDip = ChordNameEngraver.SymbolInk(fonts, "C").Bottom;
        Assert.Equal(0.0, flatDip, 12);
        Assert.True(roundDip < -1e-3,
            $"the pair is only a pair while \"C\" dips below its baseline — it dips {roundDip:F9}, "
            + "so this book no longer separates ink from baseline and the assertion below is vacuous.");

        double flat = Baseline("A");
        double round = Baseline("C");

        // Both readings are ABOVE the staff and up-positive, and `Bottom` is signed, so the
        // row's ink bottom is baseline PLUS it.
        Assert.Equal(flat + flatDip, round + roundDip, 9);
        Assert.NotEqual(flat, round, 9);
    }
}
