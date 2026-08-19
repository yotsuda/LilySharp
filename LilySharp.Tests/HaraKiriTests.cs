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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for hara-kiri (empty staff auto-hiding).
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/hara-kiri-group-spanner.cc — core suicide decision logic
/// LILYPOND-REF: ly/context-mods-init.ly — RemoveEmptyStaves / RemoveAllEmptyStaves
/// </remarks>
[Trait("Category", "Unit")]
public class HaraKiriTests
{
    private static NoteItem MakeNote(int staffPosition = 0) =>
        new(staffPosition, Fraction.Quarter, 0, null, false, 0);

    private static RestItem MakeRest() =>
        new(Fraction.Quarter, 0, 0);

    private static ChordItem MakeChord() =>
        new(ImmutableArray.Create(new ChordNoteInfo(0, null, false)),
            Fraction.Quarter, 0, 0);

    private static Measure MakeNoteMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeNote(), MakeNote(), MakeNote(), MakeNote()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeRestMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeRest(), MakeRest(), MakeRest(), MakeRest()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Measure MakeChordMeasure() =>
        new(ImmutableArray.Create<MusicItem>(MakeChord()),
            BarlineType.None, BarlineType.Single, null, 0, 0);

    private static Staff CreateStaff(Measure[] measures, bool removeEmpty = false, bool removeFirst = false) =>
        new(ClefType.Treble,
            ImmutableArray.Create(new Voice("v1", measures.ToImmutableArray())),
            RemoveEmpty: removeEmpty,
            RemoveFirst: removeFirst);

    // --- IsStaffEmpty tests ---

    [Fact]
    public void IsStaffEmpty_WithNotes_ReturnsFalse()
    {
        var staff = CreateStaff([MakeNoteMeasure()]);
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_WithOnlyRests_ReturnsTrue()
    {
        var staff = CreateStaff([MakeRestMeasure()]);
        Assert.True(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_WithChord_ReturnsFalse()
    {
        var staff = CreateStaff([MakeChordMeasure()]);
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    [Fact]
    public void IsStaffEmpty_MixedMeasures_ChecksOnlyRange()
    {
        // Measures: [notes, rests, notes]
        var staff = CreateStaff([MakeNoteMeasure(), MakeRestMeasure(), MakeNoteMeasure()]);

        // Range [1,2) = only the rest measure → empty
        Assert.True(HaraKiri.IsStaffEmpty(staff, 1, 2));

        // Range [0,2) = includes note measure → not empty
        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 2));
    }

    [Fact]
    public void IsStaffEmpty_MultiVoice_AnyVoiceKeepsAlive()
    {
        // Voice 1: rests only, Voice 2: has notes
        var v1 = new Voice("v1", ImmutableArray.Create(MakeRestMeasure()));
        var v2 = new Voice("v2", ImmutableArray.Create(MakeNoteMeasure()));
        var staff = new Staff(ClefType.Treble, ImmutableArray.Create(v1, v2), RemoveEmpty: true);

        Assert.False(HaraKiri.IsStaffEmpty(staff, 0, 1));
    }

    // --- removeEmpty part property (grammar → Staff flags) ---

    [Theory]
    [InlineData("", false, false)]                    // absent: never hide
    [InlineData("removeEmpty true", true, false)]     // LP \RemoveEmptyStaves
    [InlineData("removeEmpty all", true, true)]       // LP \RemoveAllEmptyStaves
    [InlineData("removeEmpty false", false, false)]   // explicit off
    public void RemoveEmptyPartProperty_MapsToStaffFlags(
        string property, bool removeEmpty, bool removeFirst)
    {
        string src = $$"""
            time 4/4
            key c major
            part rh { clef treble }
            part lh { clef bass {{property}} }
            section Main { rh { c4 d e f | } lh { r1 | } }
            form main { Main }
            score main "x" { grandStaff { staff rh staff lh } }
            """;
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(src);
        var spec = Core.Svg.Collector.RenderSpecParser.FindFirst(tree);
        Assert.NotNull(spec);
        var multi = new Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec!);

        var lh = multi.StaffGroups[0].Staves[1];
        Assert.Equal(removeEmpty, lh.RemoveEmpty);
        Assert.Equal(removeFirst, lh.RemoveFirst);
    }

    // --- ShouldHideStaff tests ---

    [Fact]
    public void ShouldHide_RemoveEmptyFalse_NeverHides()
    {
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: false);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_RemoveEmpty_EmptyStaff_Hides()
    {
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true);
        Assert.True(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_RemoveEmpty_NonEmptyStaff_DoesNotHide()
    {
        var staff = CreateStaff([MakeNoteMeasure()], removeEmpty: true);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: false));
    }

    [Fact]
    public void ShouldHide_FirstSystem_RemoveFirstFalse_NeverHides()
    {
        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — remove-first = false
        // First system always shows all staves unless remove-first = true
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true, removeFirst: false);
        Assert.False(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: true));
    }

    [Fact]
    public void ShouldHide_FirstSystem_RemoveFirstTrue_CanHide()
    {
        // LILYPOND-REF: RemoveAllEmptyStaves sets both remove-empty and remove-first
        var staff = CreateStaff([MakeRestMeasure()], removeEmpty: true, removeFirst: true);
        Assert.True(HaraKiri.ShouldHideStaff(staff, 0, 1, isFirstSystem: true));
    }

    // --- Layout integration tests ---

    [Fact]
    public void LayoutStaffGroups_HidesEmptyStaff_InNonFirstSystem()
    {
        // Create 3-staff score: treble (notes), alto (rests), bass (notes)
        // Alto has RemoveEmpty=true
        var noteMeasures = new[] { MakeNoteMeasure(), MakeNoteMeasure() };
        var restMeasures = new[] { MakeRestMeasure(), MakeRestMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true);
        var bassStaff = CreateStaff(noteMeasures);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        // Non-first system, measures [0,2)
        var staffGroups = layouter.LayoutStaffGroups(score, 0, 2, isFirstSystem: false);

        // Alto staff should be hidden
        Assert.Equal(3, staffGroups.Length);
        Assert.False(staffGroups[0].Staves[0].IsHidden); // treble: visible
        Assert.True(staffGroups[1].Staves[0].IsHidden);  // alto: hidden
        Assert.False(staffGroups[2].Staves[0].IsHidden); // bass: visible

        // Hidden staff should have Height=0
        Assert.Equal(0, staffGroups[1].Height);
    }

    [Fact]
    public void LayoutStaffGroups_ShowsEmptyStaff_InFirstSystem_WhenRemoveFirstFalse()
    {
        var restMeasures = new[] { MakeRestMeasure() };
        var noteMeasures = new[] { MakeNoteMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true, removeFirst: false);
        var bassStaff = CreateStaff(noteMeasures);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        // First system — should NOT hide even though empty
        var staffGroups = layouter.LayoutStaffGroups(score, 0, 1, isFirstSystem: true);

        Assert.False(staffGroups[1].Staves[0].IsHidden); // alto: visible in first system
        Assert.True(staffGroups[1].Height > 0);
    }

    [Fact]
    public void LayoutStaffGroups_HiddenStaff_DoesNotAffectSpacing()
    {
        // When a staff is hidden, the gap between adjacent visible staves should be
        // smaller (no extra inter-group spacing for the hidden staff)
        var noteMeasures = new[] { MakeNoteMeasure() };
        var restMeasures = new[] { MakeRestMeasure() };

        var trebleStaff = CreateStaff(noteMeasures);
        var altoStaff = CreateStaff(restMeasures, removeEmpty: true);
        var bassStaff = CreateStaff(noteMeasures);

        var scoreWithHidden = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(altoStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var scoreWithoutMiddle = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(trebleStaff),
                StaffGroup.CreateSingle(bassStaff)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var groupsWithHidden = layouter.LayoutStaffGroups(scoreWithHidden, 0, 1, isFirstSystem: false);
        var groupsWithout = layouter.LayoutStaffGroups(scoreWithoutMiddle, 0, 1, isFirstSystem: false);

        // The bass staff Y should be similar (hidden staff contributes no height/gap)
        double bassYWithHidden = groupsWithHidden[2].Y;
        double bassYWithout = groupsWithout[1].Y;

        Assert.True(Math.Abs(bassYWithHidden - bassYWithout) < 0.1,
            $"Bass Y with hidden ({bassYWithHidden:F2}) should be ≈ Bass Y without ({bassYWithout:F2})");
    }

    [Fact]
    public void LayoutStaffGroups_AllStavesEmpty_HidesAll()
    {
        var restMeasures = new[] { MakeRestMeasure() };

        var staff1 = CreateStaff(restMeasures, removeEmpty: true, removeFirst: true);
        var staff2 = CreateStaff(restMeasures, removeEmpty: true, removeFirst: true);

        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(staff1),
                StaffGroup.CreateSingle(staff2)),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var staffGroups = layouter.LayoutStaffGroups(score, 0, 1, isFirstSystem: true);

        Assert.True(staffGroups[0].Staves[0].IsHidden);
        Assert.True(staffGroups[1].Staves[0].IsHidden);
    }

    // --- The declaration on its own ---

    /// <summary>
    /// Book JSK's music — two staves, 120 bars, which the breaker packs eight systems to a
    /// page and then SQUEEZES — with the upper staff optionally DECLARING
    /// <c>removeEmpty</c>. Neither staff is ever empty, so the declaration can never fire.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE MUSIC IS JSK'S ON PURPOSE, not the LYRHKD/LYRHKN books that were built for
    /// this question. Those carry lyrics, which makes their systems tall enough that only
    /// seven fit and the page STRETCHES (measured: inside 9.166134, above the ideal
    /// 9.000000), and a spring's minimum cannot bind on a stretching page — the pair agrees
    /// there while the defect is untouched. HANDOFF 5.0 trap 7, and it caught this test
    /// before this test caught anything. JSK's page compresses (inside 8.651797, below the
    /// ideal), and audit/lp-geometry <c>page.compressed.staff-staff-inside</c> records that
    /// LilyPond reads the same 8.651797 there — so the undeclared side of this pair is
    /// independently known to be RIGHT, not merely different.
    /// <para>⚠️ Lily# <c>c</c> is LilyPond <c>c'</c> (HANDOFF 5.5).</para>
    /// </remarks>
    private static string PlainHaraKiriScore(bool declareRemoveEmpty, bool wideInk = false)
    {
        // wideInk: the rh dives below its staff and the lh climbs above its own, so the ink
        // BETWEEN the staves needs far more room than basic-distance asks for and the
        // alignment minimum — not the spec — decides the distance (measured: 22.090000
        // against a basic-distance of 9). That is the regime the hara-kiri placement used to
        // be blind to, and it is a different one from the compressed page below: this one
        // binds at force 0, on any paper.
        string rh = string.Concat(Enumerable.Repeat(
            wideInk ? "c,,4 d,, e,, f,, | " : "c4 d e f | ", 120)).Trim();
        string lh = string.Concat(Enumerable.Repeat(
            wideInk ? "c''4 d'' e'' f'' | " : "c,4 d, e, f, | ", 120)).Trim();
        return $$"""
            octave absolute
            time 4/4
            key c major

            part rh { clef treble{{(declareRemoveEmpty ? " removeEmpty all" : "")}} }
            part lh { clef bass }

            section Main {
              rh { {{rh}} }
              lh { {{lh}} }
            }

            form main { ~Main }

            score main "HKJ" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """;
    }

    /// <summary>
    /// Declaring <c>removeEmpty</c> where nothing is ever empty changes NOTHING.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/hara-kiri-group-spanner.cc <c>consider_suicide</c> +
    /// lily/page-layout-problem.cc:1366-1370 — LilyPond's hara-kiri is a suicide followed by
    /// a live-filter, so a grob that never dies leaves NO TRACE and its two readings of this
    /// pair are identical BY CONSTRUCTION. Whatever Lily# reads differently between them is
    /// entirely its own, and needs no LilyPond measurement to interpret: the pair is an
    /// identity (HANDOFF 5.0), so any difference IS the defect.
    /// <para>
    /// ⚠️ THE PAPER IS PART OF THE TEST. Compression is the ONLY regime where a staff
    /// spring's MINIMUM binds: a spring built without skylines falls back to the drawn
    /// distance, so it can stretch but never squeeze, and on a stretching page the two
    /// sides agree while the defect is untouched (HANDOFF 5.0 trap 7). Assert the regime
    /// rather than trusting it — if the inside gap ever comes out at or above the ideal
    /// 9.000000, this test has stopped measuring and must be re-aimed, not relaxed.
    /// </para>
    /// <para>
    /// MEASURED before the fix: undeclared 8.651797 inside (the ledger's LilyPond value),
    /// declared 9.000000 — the spring stuck at its drawn distance, with the squeeze it
    /// refused pushed into the system springs (11.303595 -&gt; 10.927848).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]  // the SPRING: a compressed page, where a spring's minimum binds
    [InlineData(true)]   // the PLACEMENT: ink between the staves that the spec cannot see
    public void RemoveEmptyDeclaration_WithNothingEmpty_ChangesNothing(bool wideInk)
    {
        var paper = LayoutOptions.Default with
        {
            PageBreaking = LayoutOptions.Default.PageBreaking with { MaxSystemsPerPage = 8 },
        };

        var declared = RenderedGeometry.Render(PlainHaraKiriScore(true, wideInk), paper);
        var undeclared = RenderedGeometry.Render(PlainHaraKiriScore(false, wideInk), paper);

        // HANDOFF 5.0 trap 7: prove each book is in the regime it was built for, or it
        // measures nothing. Compressed means BELOW the ideal; wide ink means far above it.
        double ideal = LayoutOptions.Default.StaffSpacing.StaffStaff.BasicDistance;
        double inside = undeclared.StaffGapAt(0);
        Assert.True(wideInk ? inside > ideal : inside < ideal,
            $"book is not in its regime: inside {inside:F6} against the ideal {ideal:F6} "
            + $"(wideInk={wideInk}); re-aim the book rather than relaxing this");

        Assert.Equal(undeclared.PageCount, declared.PageCount);
        for (int page = 0; page < undeclared.PageCount; page++)
        {
            Assert.Equal(
                undeclared.StaffRefpoints(page).Select(y => Math.Round(y, 6)),
                declared.StaffRefpoints(page).Select(y => Math.Round(y, 6)));
        }
    }

    /// <summary>
    /// A system is as tall as the staves it PLACED, whoever placed them — the height is not
    /// re-derived from the spacing specs by a second walk that hara-kiri gets its own copy of.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:220-238 generic_group_extent().
    /// <para>
    /// ⚠️ THIS ASSERTS THE RULE, NOT TODAY'S NUMBER (HANDOFF 5.4). The perturbation is the
    /// inter-group SPEC: drive <c>default-staff-staff-spacing</c>'s basic-distance and the
    /// system height must follow it, because the placement follows it. The branch this
    /// replaced spelled the gap as the literal <c>StaffGroupStaff.BasicDistance</c>, so it
    /// stayed put while the staves below it moved — 1.500000 of height that nothing was ever
    /// drawn in (audit/lp-geometry book LYRHK,
    /// <c>lyrics.hara-kiri.shown-system.staff-to-lyric</c>). A literal version fails here.
    /// </para>
    /// <para>
    /// ⚠️ THE SKYLINE-LESS OVERLOAD IS DELIBERATE HERE, unlike in BraceCollapseTests, which
    /// were moved onto the render path's overload on 2026-07-27. This test's subject is the
    /// SPEC, and a skyline can raise the gap above it — which would be the alignment minimum
    /// binding instead, and the perturbation would stop being a clean one-variable
    /// experiment. LilyPond makes the same split: the spec-only answer is its PURE estimate
    /// (align-interface.cc:234-238).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(9.0)]
    [InlineData(14.0)]
    public void HaraKiriSystemHeight_FollowsTheInterGroupSpec_NotALiteral(double basicDistance)
    {
        var upper = CreateStaff([MakeNoteMeasure()]);
        var lower = CreateStaff([MakeNoteMeasure()], removeEmpty: true);
        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(upper),
                StaffGroup.CreateSingle(lower)),
            new TimeSignature(4, 4), KeySignature.CMajor);

        var sp = LayoutOptions.Default.StaffSpacing;
        var options = LayoutOptions.Default with
        {
            StaffSpacing = sp with
            {
                DefaultStaffStaff = sp.DefaultStaffStaff with { BasicDistance = basicDistance },
            },
        };
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var groups = layouter.LayoutStaffGroups(score, 0, 1, isFirstSystem: true);
        double height = MultiStaffLayouter.SystemHeightOf(groups);

        // Two staves neither of which carries a grouper: the gap between them is
        // default-staff-staff-spacing, so the system is two staves plus that distance.
        Assert.Equal(options.StaffHeight + basicDistance, height, 9);
    }

    /// <summary>
    /// A group whose every staff committed hara-kiri leaves the system's height by itself:
    /// no arithmetic counts the survivors, they are simply what is left in the union.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1366-1370 — <c>consider_suicide</c> followed
    /// by <c>if (is_live()) push_back</c>. LilyPond has no hara-kiri height formula.
    /// <para>
    /// ⚠️ Skyline-less on purpose, for the reason given on the test above: the claim is that
    /// a dead group contributes NOTHING, and the survivor's own height is the whole answer.
    /// A skyline could only add to it, which would blur exactly what is being asserted.
    /// </para>
    [Fact]
    public void HaraKiriSystemHeight_OfAHiddenNeighbour_IsTheSurvivorAlone()
    {
        var upper = CreateStaff([MakeNoteMeasure()]);
        // removeFirst too: a staff LilyPond keeps on the first system is not dead there.
        var lower = CreateStaff([MakeRestMeasure()], removeEmpty: true, removeFirst: true);
        var score = new MultiStaffScore(
            ImmutableArray.Create(
                StaffGroup.CreateSingle(upper),
                StaffGroup.CreateSingle(lower)),
            new TimeSignature(4, 4), KeySignature.CMajor);

        var options = LayoutOptions.Default;
        var layouter = new MultiStaffLayouter(options, new MeasureLayouter());

        var groups = layouter.LayoutStaffGroups(score, 0, 1, isFirstSystem: true);

        Assert.True(groups[1].Staves[0].IsHidden);
        Assert.Equal(options.StaffHeight, MultiStaffLayouter.SystemHeightOf(groups), 9);
    }

    private static (bool RemoveEmpty, bool RemoveFirst) StaffFlagsOf(string source)
    {
        var spec = RenderSpecParser.FindFirst(SyntaxTree.Parse(source))!;
        var staff = spec.Items.OfType<SingleStaffSpec>().Single().Staff;
        return (staff.RemoveEmpty, staff.RemoveFirst);
    }

    private static string Book(string header) =>
        header + NL + "section A { m { c'1 } }" + NL
        + "form main { A }" + NL + "score main { staff m }" + NL;

    private static string NL => Environment.NewLine;

    [Fact]
    public void TheRemoveEmptyVocabulary_IsWhatRenderSpecParserReads()
    {
        // ★ SymbolCaseValidator.RemoveEmptyValueVocabulary was published on 2026-08-19 so the
        // editor could colour these three words, and publishing it made a SECOND SPELLING of a
        // vocabulary RenderSpecParser already held as `removeEmpty is "true" or "all"`. Two
        // spellings of one quantity means one drifts (RULES §7.7); the transposition markers got
        // a check for exactly this reason and this list did not. Found by running the closing
        // checklist, not by a red test.
        //
        // ⚠️ The mapping itself is NOT re-asserted here — RemoveEmptyPartProperty_MapsToStaffFlags
        // above already holds it, and a copy of those three lines would be a third spelling. What
        // is new is the direction that theory cannot see: it names its own words, so a word ADDED
        // to the published list without the reader learning it stays green there. Measured: adding
        // `none` to the list leaves that theory passing and turns this red.
        var readings = SymbolCaseValidator.RemoveEmptyValueVocabulary
            .Select(v => StaffFlagsOf(Book($"part m {{ removeEmpty {v} }}")))
            .ToArray();
        Assert.Equal(SymbolCaseValidator.RemoveEmptyValueVocabulary.Count, readings.Distinct().Count());

        // ⚠️ And the edge the list sits beside, so the count above is not mistaken for the
        // whole story: THIS READER still takes anything. An unknown word is silently the
        // default here, and `TRUE` is silently `true` because the reader lower-cases — which
        // is why, when the hole was closed on 2026-08-19, it was closed in the VALIDATOR
        // (SymbolCaseValidator refuses both spellings, Ordinal, like every other symbol in a
        // part header) and not by tightening these comparisons. A book that reaches this
        // reader has already been checked; what these two lines pin is that the reader's
        // leniency is UNCHANGED, so the refusal above it is the only thing anyone has to
        // reason about. See SymbolCaseValidatorTests for the door itself.
        Assert.Equal((false, false), StaffFlagsOf(Book("part m { removeEmpty banana }")));
        Assert.Equal((true, false), StaffFlagsOf(Book("part m { removeEmpty TRUE }")));
    }

    /// <summary>
    /// A verse row folds onto its staff, so it DIES WITH IT: on a system where the
    /// staff hara-kiri'd, the verse leaves no syllables and no reserved room — the
    /// system gap is the same as in the same book with no verse at all. (LilyPond's
    /// Lyrics carries its own remove-empty, engraver-init.ly:648-658; here the
    /// question cannot arise per-syllable, because a syllable exists only where its
    /// melody has a note, and a hidden staff has none — this pins that no OTHER
    /// piece of the verse machinery, reservation included, survives the staff.)
    /// </summary>
    [Fact]
    public void AVerseOfAHiddenStaff_LeavesNoInkAndNoRoom()
    {
        static string VerseBook(bool verse) =>
            "octave absolute" + Environment.NewLine + "time 4/4" + Environment.NewLine
            + "part voc { clef treble removeEmpty true }" + Environment.NewLine
            + "part pno { clef bass }" + Environment.NewLine
            + "section A { voc { c'4 d' e' f' | } "
            + (verse ? "lyrics w sings voc { la la la la | } " : "")
            + "pno { c4 d e f | } }" + Environment.NewLine
            + "section B { voc { R1 | } pno { g4 a b c' | } }" + Environment.NewLine
            + "form main { A break B }" + Environment.NewLine
            + "score main { staff voc " + (verse ? " lyrics w " : "") + " staff pno }"
            + Environment.NewLine;

        static (double Gap, int SyllablesPastSystem1) Measure(string src)
        {
            var tree = SyntaxTree.Parse(src);
            var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(
                tree, RenderSpecParser.FindFirst(tree));
            var layout = new LayoutEngine().Layout(score);
            Assert.Equal(2, layout.Systems.Length);
            var sys1 = layout.Systems[0].StaffGroups.SelectMany(g => g.Staves).ToList();
            var sys2 = layout.Systems[1].StaffGroups.SelectMany(g => g.Staves).ToList();
            // The hidden staff is still in the table (Height 0, IsHidden) — the
            // suicide really happened on system 2 and only there.
            Assert.DoesNotContain(sys1, s => s.IsHidden);
            Assert.Contains(sys2, s => s.IsHidden);
            double gap = sys2.Where(s => !s.IsHidden).Min(s => s.Y)
                       - sys1.Where(s => !s.IsHidden).Max(s => s.Y);
            return (gap, layout.LyricLayouts.Count(l => l.Item.MeasureIndex >= 1));
        }

        var (withGap, straySyllables) = Measure(VerseBook(verse: true));
        var (withoutGap, _) = Measure(VerseBook(verse: false));

        Assert.Equal(0, straySyllables);
        Assert.Equal(withoutGap, withGap, 6);
    }
}
