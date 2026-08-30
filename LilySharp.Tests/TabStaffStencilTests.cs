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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A tablature staff prints none of the markup the notation staff beside it already
/// carries, and reserves no room for it either.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. A part shown as BOTH a notation staff and a tab is collected once per
/// staff, so every annotation on it exists twice — once per <c>StaffIndex</c>. LilyPond's
/// <c>TabStaff</c> context blanks the stencils of exactly this family, so its copy prints
/// nothing; Lily# drew all of them, and the reader saw <c>rit.</c>, the dynamic and the
/// free text twice on every such score. LILYPOND-REF:
/// ly/engraver-init.ly:1277-1285 Tab_staff_symbol_engraver — the block
/// <see cref="TabStaffStencils"/> transcribes.
/// </para>
/// <para>
/// ⚠️ AND THE SECOND COPY WAS NOT ONLY INK. A blanked stencil has an empty extent in
/// LilyPond, so it joins no skyline; Lily# reserved an outside-staff band for the tab's
/// <c>rit.</c> above the tab line, and with a chord row folded onto that staff the band
/// landed INSIDE the notation staff above it — which is how the defect was reported
/// (2026-08-30, <c>scratch/ベースタブLy/Untitled-6.lys</c>: "五線とコード名と rit が重なる").
/// The layout assertion below is the one that watches the reservation; the drawn-ink
/// assertion alone would pass on a build that still reserved the room.
/// </para>
/// <para>
/// ⚠️ THE POISON TABLE — which case goes red for which arm, measured 2026-08-30. Every
/// test here was green on the first run, and a test that has only ever been green cannot
/// tell "agrees" from "sees nothing" (HANDOFF §5.4), so each arm was reverted in turn:
/// <list type="bullet">
/// <item>dynamics, INK half (LayoutEngine.Annotations) → 2 red:
/// <see cref="NoBlankedMarkupIsLaidOutOnATabStaff"/> +
/// <see cref="BlankedMarkupIsEngravedOnce_OnTheNotationStaffOnly"/></item>
/// <item>hairpin, INK half → 1 red: <see cref="NoBlankedMarkupIsLaidOutOnATabStaff"/>
/// (a hairpin's wedge is not text, so the ink test cannot count it — which is why the
/// book carries an <c>@cresc</c> the layout test reads and the ink test does not)</item>
/// <item>text spanner, INK half → the same 2 as dynamics</item>
/// <item>text spanner, RESERVATION half (ScoreSideTables) → 2 red:
/// <see cref="ATabStaffReservesNoRoomForTheFamiliesItBlanks"/> +
/// <see cref="ABlankedSpannerAddsNoRoomBetweenTheStavesItIsBlankOn"/></item>
/// <item>dynamics, RESERVATION half → 1 red:
/// <see cref="ATabStaffReservesNoRoomForTheFamiliesItBlanks"/> ALONE. See that test for
/// why no page-geometry book can reach this arm.</item>
/// <item>the frame conversion (nominal half-staff instead of the tab's own) → 1 red:
/// <see cref="AFoldedChordLine_ClearsTheLedgerNotesOfTheStaffAbove"/>, at symbol ink top
/// 15.845551 against a notehead bottom of 17.050551 — the reported defect's own numbers.</item>
/// </list>
/// ⚠️ ONE OF THOSE RUNS LIED FIRST. The bucket test came back red on a build whose source
/// was already correct; a <c>--no-incremental</c> rebuild made it green with the same
/// source. An incremental build here can serve a stale Core, so a surprising poison result
/// gets a clean rebuild before it gets an explanation.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabStaffStencilTests
{
    /// <summary>
    /// One part, carrying one of each blanked family, shown as a notation staff AND a tab.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ <c>as numbers</c> IS THE SUBJECT, NOT DECORATION. The criterion is
    /// <c>Staff.TabNumbersOnly</c>, not "is a tab" (reader decision, 2026-08-30 — see
    /// <see cref="TabStaffStencils"/>), and since 2026-08-29 a tab beside a notation staff
    /// of the same part already defaults to it. The book STATES the style rather than
    /// inheriting it, so that a later change to the default cannot silently retire these
    /// tests — and so that the pair with <see cref="AFullTabKeepsTheMarkupItHasToCarry"/>
    /// reads as the one switch it is.
    /// </remarks>
    private const string Music =
        "part m {\n"
        + "  clef treble\n"
        + "  section A { c4@rit c@!rit g'@f a@text(\"dolce\") | b4@cresc c'' d'' e''@f | }\n"
        + "}\n"
        + "form main { A }\n";

    private const string Both = Music + "score main { staff m  tab m as numbers }";

    /// <summary>The same music with NO tab — the control every count below is read against.</summary>
    private const string StaffOnly = Music + "score main { staff m }";

    private static (MultiStaffScore Score, ScoreLayout Layout) LayoutOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        return (score, new LayoutEngine().Layout(score));
    }

    private static RecordingDrawingContext RenderFirstPage(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var spec = RenderSpecParser.FindFirst(tree);
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);
        using var doc = new RecordingDocumentContext();
        SharedRenderer.RenderTo(score, layout, doc);
        return doc.Page;
    }

    /// <summary>The global staff indices of a score's tab staves.</summary>
    private static HashSet<int> TabStaves(MultiStaffScore score)
    {
        var set = new HashSet<int>();
        foreach (var (_, staff, index) in score.EnumerateStaves())
            if (staff.IsTab)
                set.Add(index);
        return set;
    }

    /// <summary>
    /// THE RESERVATION CLAIM: no blanked family has a LAYOUT on a tab staff. A layout is
    /// what reserves outside-staff room, so this is the half of the defect the picture
    /// cannot show.
    /// </summary>
    [Fact]
    public void NoBlankedMarkupIsLaidOutOnATabStaff()
    {
        var (score, layout) = LayoutOf(Both);
        var tabs = TabStaves(score);

        // POSITIVE CONTROL — the regime. Without a tab staff, and without the annotations
        // actually reaching a layout, "none of them is on the tab" is satisfied by a book
        // that never posed the question (HANDOFF §5.4: prove the check can fail).
        Assert.Single(tabs);
        Assert.NotEmpty(layout.TextSpannerLayouts);
        Assert.NotEmpty(layout.DynamicLayouts);
        Assert.NotEmpty(layout.HairpinLayouts);

        foreach (var s in layout.TextSpannerLayouts)
            Assert.DoesNotContain(s.StaffIndex, tabs);
        foreach (var d in layout.DynamicLayouts)
            Assert.DoesNotContain(d.StaffIndex, tabs);
        foreach (var h in layout.HairpinLayouts)
            Assert.DoesNotContain(h.StaffIndex, tabs);
    }

    /// <summary>
    /// THE INK CLAIM: the reader sees each annotation ONCE, and sees it at all — the tab's
    /// copy is dropped, the notation staff's is untouched.
    /// </summary>
    [Fact]
    public void BlankedMarkupIsEngravedOnce_OnTheNotationStaffOnly()
    {
        var withTab = RenderFirstPage(Both);
        var without = RenderFirstPage(StaffOnly);

        // ⚠️ READ AGAINST THE STAFF-ONLY BOOK, NOT AGAINST 1. The counts are the control's,
        // so a later change to how many pieces a spanner is drawn in (a broken rit. draws
        // its label once and its line per system) moves both sides together and this still
        // holds. What it forbids is the TAB adding copies.
        foreach (string label in new[] { "rit.", "dolce", "f" })
        {
            int expected = without.Texts.Count(t => t.Text == label);
            Assert.True(expected > 0, $"the control book does not draw \"{label}\" at all.");
            Assert.Equal(expected, withTab.Texts.Count(t => t.Text == label));
        }
    }

    /// <summary>
    /// THE ROOM CLAIM: a blanked grob reserves NOTHING, so adding one to the music must not
    /// push the two staves apart. This is the half <see cref="ScoreSideTables"/> carries —
    /// the staff-keyed buckets the skyline pass reads — and no drawn-ink assertion reaches
    /// it, because the ink is already gone while the band it left behind is not.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/grob.cc Grob::extent — a grob with no stencil answers with an
    /// empty interval, so lily/axis-group-interface.cc:864-989 skyline_spacing never sees
    /// it. That empty extent IS this test.
    /// </remarks>
    [Fact]
    public void ABlankedSpannerAddsNoRoomBetweenTheStavesItIsBlankOn()
    {
        const string bare =
            "part m {\n  clef treble\n  section A { c4 c g' a | }\n}\n"
            + "form main { A }\nscore main { staff m  tab m as numbers }";
        const string spanned =
            "part m {\n  clef treble\n  section A { c4@rit c@!rit g' a | }\n}\n"
            + "form main { A }\nscore main { staff m  tab m as numbers }";

        // The gap between the notation staff's bottom line and the tab's top line — the
        // band a tab's own rit. would reserve ABOVE itself.
        double bareGap = Gap(RenderFirstPage(bare), 4, 5);
        double spannedGap = Gap(RenderFirstPage(spanned), 4, 5);

        // POSITIVE CONTROL: the spanner really is in the spanned book, on the upper staff
        // only. Without it, "the gap did not move" is what two identical books say.
        var (_, spannedLayout) = LayoutOf(spanned);
        Assert.NotEmpty(spannedLayout.TextSpannerLayouts);

        Assert.Equal(bareGap, spannedGap, 9);
    }

    /// <summary>
    /// The same room claim, the other way up. A tab's dynamics and free texts hang BELOW
    /// it, so their reservation cannot be seen between the staff and the tab — it needs a
    /// staff UNDER the tab to push. Without this book the dynamics arm of
    /// <see cref="ScoreSideTables.DynamicsByStaff"/> has no observer at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️⚠️ THIS ONE READS THE BUCKET, NOT THE PICTURE, AND THAT IS THE HONEST LEVEL FOR IT.
    /// Two page-geometry books were tried first and neither can state the claim:
    /// </para>
    /// <list type="bullet">
    /// <item>Under a second STAFF the band is invisible — that pair is sprung at
    /// <c>default-staff-staff-spacing</c>'s basic-distance 9 (scm/define-grobs.scm:4232-4240
    /// VerticalAxisGroup), which a ~2 ss dynamic band never exceeds. MEASURED: green under
    /// the poison, i.e. the book says nothing.</item>
    /// <item>Under a LYRICS row the gap does move — but it moves for the NOTATION staff's
    /// own dynamic too (the row is spaced from the staff it sings), so the measurement
    /// cannot tell the blanked copy from the kept one. MEASURED: 2.369959965 → 5.410830896
    /// on a CLEAN build, which is the legitimate half.</item>
    /// </list>
    /// <para>
    /// So the claim is made where it is actually made: the staff-keyed bucket the skyline
    /// pass reads is EMPTY for a tab staff. Everything downstream of that is the reservation
    /// machinery's own business, and
    /// <see cref="ABlankedSpannerAddsNoRoomBetweenTheStavesItIsBlankOn"/> shows end-to-end
    /// that an empty bucket really does mean no room.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATabStaffReservesNoRoomForTheFamiliesItBlanks()
    {
        var (score, _) = LayoutOf(Both);
        var tabs = TabStaves(score);
        Assert.Single(tabs);
        int tab = tabs.Single();
        int notation = score.EnumerateStaves()
            .First(t => !t.Staff.IsTab && !t.Staff.IsTextRow).GlobalStaffIndex;

        // POSITIVE CONTROL: the notation staff's buckets are NOT empty, so "the tab's are"
        // is a statement about the tab and not about a book with no annotations in it.
        Assert.NotEmpty(ScoreSideTables.DynamicsByStaff(score).At(notation));
        Assert.NotEmpty(ScoreSideTables.TextSpannersByStaff(score).At(notation));

        Assert.Empty(ScoreSideTables.DynamicsByStaff(score).At(tab));
        Assert.Empty(ScoreSideTables.TextSpannersByStaff(score).At(tab));
    }

    /// <summary>
    /// The distance between two of the page's horizontal rules, counted top-down. Read off
    /// the drawn rules so it is the distance the reader actually sees.
    /// </summary>
    private static double Gap(RecordingDrawingContext page, int upper, int lower)
    {
        var rules = page.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) > 10.0)
            .Select(l => l.Y1)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
        Assert.True(rules.Count > lower,
            $"the page has {rules.Count} rules; this book cannot be measured at {lower}.");
        return rules[lower] - rules[upper];
    }

    /// <summary>
    /// THE NARROWNESS CONTROL: a Script is NOT blanked, though LilyPond blanks it at
    /// :1284. Lily# engraves scripts on a tab line deliberately (seven tracked fixtures and
    /// <see cref="TabScriptStemClearanceTests"/>), so this asserts the filter stayed inside
    /// the families it was built for.
    /// </summary>
    /// <remarks>
    /// Without this, the cheapest way to make the test above pass is to widen the filter
    /// until the tab draws nothing — which would silently delete a feature.
    /// </remarks>
    [Fact]
    public void AnOrdinaryScriptIsBlankedOnANumbersOnlyTabAndKeptOnAFullOne()
    {
        const string music =
            "part m {\n  clef treble\n  section A { g'4@accent a b c'' | }\n}\nform main { A }\n";

        // A FULL tab carries its own markup: nothing else is carrying it for the reader.
        int full = RenderFirstPage(music + "score main { staff m  tab m as full }")
            .Glyphs.Count(g => g.Glyph == EmmentalerGlyphs.ArticAccentAbove);
        Assert.Equal(2, full);

        // A NUMBERS-ONLY tab does not — the staff above is carrying it. Reader report,
        // 2026-08-30: "an @accent is showing on an `as numbers` tab; it should not".
        int numbers = RenderFirstPage(music + "score main { staff m  tab m as numbers }")
            .Glyphs.Count(g => g.Glyph == EmmentalerGlyphs.ArticAccentAbove);
        Assert.Equal(1, numbers);
    }

    /// <summary>
    /// THE NARROWNESS CONTROL, and the one the corpus nearly lost: the TAB TECHNIQUE LETTERS
    /// stay on a numbers-only tab. They are what a guitarist reads the tab FOR.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>test/tab-technique-letters</c> is a numbers-only tab (<c>staff gtr</c> +
    /// <c>tab gtr</c>, no <c>as</c> clause) written for exactly these marks, after a reader
    /// reported one drawn into its own notehead on 2026-08-28. Blanking Scripts by TYPE
    /// would have deleted that fixture's whole subject while every assertion about accents
    /// stayed green — which is why <c>TabStaffStencils.BlanksScript</c> asks per ITEM.
    /// </remarks>
    [Fact]
    public void TabTechniqueLettersSurviveOnANumbersOnlyTab()
    {
        const string book =
            "octave absolute\npart m { clef treble }\n"
            + "section A { m { c4@tap e@hammeron g@pulloff b@pluck(p) | } }\n"
            + "form main { ~A }\nscore main { staff m  tab m as numbers }";

        var page = RenderFirstPage(book);
        foreach (string letter in new[] { "T", "H", "P", "p" })
            Assert.True(page.Texts.Any(t => t.Text == letter),
                $"the technique letter \"{letter}\" is gone from a numbers-only tab.");
    }

    /// <summary>
    /// THE OTHER HALF OF THE SWITCH: a FULL tab keeps the markup, because nothing else is
    /// carrying it for the reader. A tab standing alone is the whole engraving.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS TEST USED TO ASSERT THE OPPOSITE, and the flip is the record of a decision
    /// rather than a bug. LilyPond blanks these on EVERY TabStaff, so the first cut of
    /// <see cref="TabStaffStencils"/> did too — and a tab-only score lost its markup
    /// entirely. MEASURED on the corpus (2026-08-30):
    /// <c>scratch/ベースタブLy/奏（かなで）.lys</c> has three score blocks and showed both
    /// faces at once — its <c>"both"</c> score drew <c>@text("人差し指で")</c> twice and now
    /// draws it once (the defect), while its <c>"tab"</c> score drew it once and would have
    /// drawn it not at all. A fingering instruction addressed to the player reading the tab
    /// would have disappeared. The reader took the decision the same day (HANDOFF §2 U12):
    /// keep it, by gating on <c>TabNumbersOnly</c>.
    /// ⇒ That the flip cost a TEST EDIT and not a silent drift is the whole point of having
    /// written the losing answer down.
    /// </remarks>
    [Fact]
    public void AFullTabKeepsTheMarkupItHasToCarry()
    {
        const string music =
            "part m {\n  clef treble\n  section A { c4@f c g'@text(\"dolce\") a | }\n}\n"
            + "form main { A }\n";

        // Explicitly full, and standing alone — where `tab m` alone would ALSO be full by
        // default (RenderSpecParser.StaffRenderedParts), stated so the default cannot
        // silently retire the test.
        var alone = RenderFirstPage(music + "score main { tab m as full }");
        Assert.Contains(alone.Texts, t => t.Text == "dolce");
        Assert.Contains(alone.Texts, t => t.Text == "f");

        // NEGATIVE CONTROL — the same music on a numbers-only tab loses both, so this test
        // is measuring the STYLE and not merely "a tab draws text".
        var numbers = RenderFirstPage(music + "score main { staff m  tab m as numbers }");
        Assert.Single(numbers.Texts.Where(t => t.Text == "dolce"));
        Assert.Single(numbers.Texts.Where(t => t.Text == "f"));
    }

    /// <summary>
    /// A <c>chords … as names</c> row written between a notation staff and a tab folds onto
    /// the TAB, and its symbols have to clear the ledger notes hanging below the staff
    /// above. They did not: the line's baseline was re-framed with the NOMINAL staff's half
    /// height, so on a six-string tab (7.5 tall, not 4.0) it was drawn 1.75 too high —
    /// straight through the notes.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT ASSERTS THE RULE, NOT THE NUMBER. Both sides are read out of the same render
    /// and the symbol's height comes from the one home that engraves it
    /// (<see cref="ChordNameEngraver.SymbolInk"/>), so a change to the chord font, the
    /// string space or the notehead moves both together. What it forbids is the symbol and
    /// the notehead occupying the same Y band.
    /// </remarks>
    [Fact]
    public void AFoldedChordLine_ClearsTheLedgerNotesOfTheStaffAbove()
    {
        // c4 in treble sits a ledger line BELOW the staff, directly over the chord line.
        const string book =
            "part m {\n  clef treble\n  section A { c4 d e f | g a b2 | }\n}\n"
            + "chords prog { section A { Dmaj7 | Em7 } }\n"
            + "form main { A }\n"
            + "score main { staff m  chords prog as names  tab m as full }";

        var (score, _) = LayoutOf(book);
        var page = RenderFirstPage(book);

        var symbol = page.Texts.FirstOrDefault(t => t.Text == "Dmaj7");
        Assert.False(symbol.Text is null, "the chord symbol was not engraved at all.");

        // The notation staff's noteheads — the tab draws digits as TEXT, not glyphs, so the
        // black heads on this page are the upper staff's and nothing else.
        var heads = page.Glyphs
            .Where(g => g.Glyph == EmmentalerGlyphs.NoteheadBlack)
            .OrderByDescending(g => g.Y)
            .ToList();
        Assert.NotEmpty(heads);
        var lowest = heads[0];

        // The staff's own line spacing, for the head's half height and for the control.
        var staffLines = page.Lines
            .Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && Math.Abs(l.X2 - l.X1) > 10.0)
            .Select(l => l.Y1)
            .Distinct()
            .OrderBy(y => y)
            .ToList();
        double bottomNotationLine = staffLines[4];   // five lines, top-down

        // POSITIVE CONTROL — the regime. If the lowest head does not hang below the staff,
        // this book poses no question and the assertion below is free.
        Assert.True(lowest.Y > bottomNotationLine,
            $"the lowest notehead ({lowest.Y:F6}) does not hang below the staff "
            + $"({bottomNotationLine:F6}) — this book no longer tests anything.");

        // The symbol's ink about its baseline, from the engraver's own home. Device Y is
        // DOWN and SymbolInk is Y-up about the baseline, so the ink top is baseline − Top.
        var (_, inkTop) = ChordNameEngraver.SymbolInk(score.TextMetrics, "Dmaj7");
        double symbolInkTop = symbol.Y - inkTop;
        double headInkBottom = lowest.Y + (staffLines[1] - staffLines[0]) / 2.0;

        // THE CLAIM. Before the fix this read 19.83 against a head bottom of 21.02.
        Assert.True(symbolInkTop > headInkBottom,
            $"the chord symbol is drawn ON the staff's ledger notes: symbol ink top "
            + $"{symbolInkTop:F6}, lowest notehead bottom {headInkBottom:F6}.");
    }
}
