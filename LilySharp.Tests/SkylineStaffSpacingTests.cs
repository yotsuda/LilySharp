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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Tests for skyline-based staff spacing in MultiStaffLayouter.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/align-interface.cc:217-268 internal_get_minimum_translations()
/// </remarks>
[Trait("Category", "Unit")]
public class SkylineStaffSpacingTests
{
    private static readonly LayoutOptions DefaultOptions = LayoutOptions.Default;
    private static readonly MeasureLayouter MeasureLayouter = new();
    private static readonly double StaffHeight = DefaultOptions.StaffHeight; // 4.0

    /// <summary>
    /// Creates a simple grand staff score with treble/bass and given notes.
    /// </summary>
    private static MultiStaffScore CreateGrandStaffScore(
        ImmutableArray<MusicItem> trebleItems,
        ImmutableArray<MusicItem> bassItems)
    {
        var trebleMeasure = new Measure(trebleItems, BarlineType.None, BarlineType.Single, null, 0, 0);
        var bassMeasure = new Measure(bassItems, BarlineType.None, BarlineType.Single, null, 0, 0);

        var trebleVoice = new Voice("treble", ImmutableArray.Create(trebleMeasure));
        var bassVoice = new Voice("bass", ImmutableArray.Create(bassMeasure));

        var trebleStaff = Staff.Create(ClefType.Treble, trebleVoice);
        var bassStaff = Staff.Create(ClefType.Bass, bassVoice);
        var grandStaff = StaffGroup.CreateGrandStaff(trebleStaff, bassStaff);

        return new MultiStaffScore(
            ImmutableArray.Create(grandStaff),
            new TimeSignature(4, 4),
            KeySignature.CMajor);
    }

    /// <summary>
    /// Creates a simple measure layout for testing.
    /// </summary>
    private static ImmutableArray<MeasureLayout> CreateSimpleMeasureLayouts(int itemCount)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemLayout(i, 5.0 + i * 4.0, 1.0));
        return ImmutableArray.Create(new MeasureLayout(0, 0, 40, items.ToImmutable()));
    }

    /// <summary>Builds a one-measure beam layout over two eighth notes, with the given
    /// per-member target staves and knee-ness, for the suppression-policy tests below.</summary>
    private static BeamLayout MakeBeamLayout(
        int targetStaffIndex0, int targetStaffIndex1, bool knee)
    {
        var n0 = new NoteItem(-9, Fraction.Eighth, 0, null, false, 0);
        var n1 = new NoteItem(knee ? 9 : -9, Fraction.Eighth, 0, null, false, 1);
        var members = ImmutableArray.Create(
            new BeamMember(n0, 1, 1, 1, staffPosition: -9, itemIndex: 0,
                memberStemUp: false, targetStaffIndex: targetStaffIndex0),
            new BeamMember(n1, 1, 1, 1, staffPosition: knee ? 9 : -9, itemIndex: 1,
                memberStemUp: knee, targetStaffIndex: targetStaffIndex1));
        var group = new BeamGroup(members, measureIndex: 0, startIndex: 0,
            stemUp: false, growDirection: 0, voiceIndex: 0);
        return new BeamLayout(group, leftY: -13, rightY: -13, leftX: 5.0, rightX: 9.0,
            ImmutableArray.Create(5.0, 9.0), staffIndex: 0, systemIndex: 0);
    }

    /// <summary>
    /// An ordinary (same-staff, same-direction) beam's members lose their fixed
    /// 3.5 stems: the drawn beam is seeded instead.
    /// audit/lp-geometry staff.staff.beam-{under,over}-notes.
    /// </summary>
    [Fact]
    public void BeamedItemsToSuppress_OrdinaryBeam_SuppressesMembers()
    {
        var set = SkylineBuilder.BeamedItemsToSuppress(
            ImmutableArray.Create(MakeBeamLayout(-1, -1, knee: false)));
        Assert.Equal(2, set.Count);
        Assert.Contains((0, 0, 0), set);
        Assert.Contains((0, 0, 1), set);
    }

    /// <summary>
    /// A CROSS-STAFF beam's members are suppressed too — with NO seed: LilyPond leaves
    /// cross-staff grobs out of the vertical skylines altogether, and a stem whose beam
    /// is cross-staff is itself cross-staff, so neither the beam nor the stems reserve
    /// anything (the noteheads still do). The fixed 3.5 stem Lily# kept for them
    /// reserved ink LilyPond does not.
    /// LILYPOND-REF: lily/axis-group-interface.cc:850-858 ("we just leave cross-staff
    ///   grobs out of the skyline altogether"), :921,:954 skyline_spacing;
    ///   lily/stem.cc:1278-1290 Stem::is_cross_staff.
    /// </summary>
    /// <remarks>
    /// No end-to-end twin exists yet: nothing in the collector stamps a BeamMember's
    /// TargetStaffIndex, so IsCrossStaff is unreachable from .lys today (the @cross
    /// annotation flows only to CrossStaffLayouts at render time). This pins the
    /// skyline policy for when the feature lands — see HANDOFF §1.
    /// </remarks>
    [Fact]
    public void BeamedItemsToSuppress_CrossStaffBeam_SuppressesMembersWithoutSeed()
    {
        var set = SkylineBuilder.BeamedItemsToSuppress(
            ImmutableArray.Create(MakeBeamLayout(-1, 1, knee: true)));
        Assert.Equal(2, set.Count);
        Assert.Contains((0, 0, 0), set);
        Assert.Contains((0, 0, 1), set);
    }

    /// <summary>
    /// A same-staff KNEED beam keeps the per-note fixed stems: LilyPond does carry its
    /// real Beam/Stem stencils in the skylines, but Lily# has no faithful model of the
    /// knee's stencil band yet, so the members stay on the fixed-stem reservation until
    /// that band is measured from LP (deferred — see BeamedItemsToSuppress remarks).
    /// </summary>
    [Fact]
    public void BeamedItemsToSuppress_SameStaffKnee_KeepsFixedStems()
    {
        var set = SkylineBuilder.BeamedItemsToSuppress(
            ImmutableArray.Create(MakeBeamLayout(-1, -1, knee: true)));
        Assert.Empty(set);
    }

    [Fact]
    public void SkylineSpacing_SimpleNotes_UsesAtLeastBasicDistance()
    {
        // Notes in the middle of the staff → no collision, should use basic-distance
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(2, Fraction.Quarter, 0, null, false, 0));

        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0),
            new NoteItem(-2, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(2);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double height = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts, systemIndex: 0);
        double heightFixed = layouter.CalculateSystemHeight(score);

        // Notes in the middle of both staves cannot reach across the gap, so the alignment
        // minimum stays under basic-distance and the spec decides: the skyline height is
        // EXACTLY the fixed one. ⚠️ The old assertion was `>= heightFixed - 0.01`, which
        // allowed the skyline path to come out SMALLER than basic-distance — the one thing
        // the name says cannot happen — and would have survived the skyline contribution
        // being dropped entirely (HANDOFF 5.4).
        Assert.Equal(heightFixed, height, 6);
    }

    [Fact]
    public void SkylineSpacing_ExtremeLedgerLines_IncreasesGap()
    {
        // Notes with extreme ledger lines (below treble, above bass) should force larger gap
        // Treble: very low note (staff position -8 → 4 ledger lines below staff)
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(-8, Fraction.Quarter, 0, null, false, 0));

        // Bass: very high note (staff position 10 → 3 ledger lines above staff)
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(10, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(1);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double skylineHeight = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts, systemIndex: 0);
        double fixedHeight = layouter.CalculateSystemHeight(score);

        // ⚠️ STRICTLY greater, which is what the test's name claims. The old assertion was
        // `>=`, so it passed whether or not the skyline did anything at all — and the
        // companion test above shows that middle-of-staff notes make the two EQUAL, i.e.
        // `>=` was satisfied by the case this test exists to distinguish itself from.
        // Measured 2026-07-27: 15.090000 against 13.000000.
        Assert.True(skylineHeight > fixedHeight,
            $"Skyline height ({skylineHeight:F6}) must EXCEED fixed height ({fixedHeight:F6}) "
            + "when ledger lines reach across the gap — equal means the skyline was ignored");
    }

    [Fact]
    public void SkylineSpacing_LayoutStaffGroups_ProducesValidLayout()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(1);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        var groups = layouter.LayoutStaffGroups(
            score, skylineBuilder, measureLayouts, systemIndex: 0);

        Assert.Single(groups);
        var grandStaff = groups[0];
        Assert.NotNull(grandStaff.GrandStaffLayout);
        Assert.Equal(2, grandStaff.GrandStaffLayout!.Staves.Length);

        // staff.Y is Y-up: the second staff sits BELOW the first, i.e. at least a full
        // staff height further down ⇒ its Y is SMALLER by more than staffHeight.
        double firstY = grandStaff.GrandStaffLayout.Staves[0].Y;
        double secondY = grandStaff.GrandStaffLayout.Staves[1].Y;
        Assert.True(secondY < firstY - StaffHeight,
            $"Second staff Y ({secondY:F2}) should be < first staff Y ({firstY:F2}) − staffHeight ({StaffHeight})");
    }

    [Fact]
    public void SkylineSpacing_FallsBackGracefully_WhenSkylineEmpty()
    {
        // Empty measures → skylines will be empty → should fall back to fixed formula
        var trebleItems = ImmutableArray<MusicItem>.Empty;
        var bassItems = ImmutableArray<MusicItem>.Empty;

        var trebleMeasure = new Measure(trebleItems, BarlineType.None, BarlineType.Single, null, 0, 0);
        var bassMeasure = new Measure(bassItems, BarlineType.None, BarlineType.Single, null, 0, 0);

        var trebleVoice = new Voice("treble", ImmutableArray.Create(trebleMeasure));
        var bassVoice = new Voice("bass", ImmutableArray.Create(bassMeasure));

        var trebleStaff = Staff.Create(ClefType.Treble, trebleVoice);
        var bassStaff = Staff.Create(ClefType.Bass, bassVoice);
        var grandStaff = StaffGroup.CreateGrandStaff(trebleStaff, bassStaff);

        var score = new MultiStaffScore(
            ImmutableArray.Create(grandStaff),
            new TimeSignature(4, 4),
            KeySignature.CMajor);

        var skylineBuilder = new SkylineBuilder(StaffHeight);
        var measureLayouts = CreateSimpleMeasureLayouts(0);

        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);
        double skylineHeight = layouter.CalculateSystemHeight(score, skylineBuilder, measureLayouts, systemIndex: 0);
        double fixedHeight = layouter.CalculateSystemHeight(score);

        // With empty skylines, should fall back to the same as fixed formula
        Assert.Equal(fixedHeight, skylineHeight, 2);
    }

    // --- Pure height estimation ---
    // LILYPOND-REF: lily/axis-group-interface.cc:138-173

    [Fact]
    public void PureSystemHeight_IncludesLooseLineExtents()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);

        double baseHeight = layouter.CalculateSystemHeight(score);

        // With loose line extents (e.g., lyrics below, tempo above)
        double pureHeight = layouter.CalculatePureSystemHeight(score, looseDownExtent: 3.0, looseUpExtent: 2.5);

        Assert.Equal(baseHeight + 5.5, pureHeight, 3);
    }

    [Fact]
    public void PureSystemHeight_ZeroExtents_EqualsBaseHeight()
    {
        var trebleItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));
        var bassItems = ImmutableArray.Create<MusicItem>(
            new NoteItem(0, Fraction.Quarter, 0, null, false, 0));

        var score = CreateGrandStaffScore(trebleItems, bassItems);
        var layouter = new MultiStaffLayouter(DefaultOptions, MeasureLayouter);

        double baseHeight = layouter.CalculateSystemHeight(score);
        double pureHeight = layouter.CalculatePureSystemHeight(score, 0, 0);

        Assert.Equal(baseHeight, pureHeight, 3);
    }

    /// <summary>
    /// Each system is spaced against ITS OWN staves' skylines, so a system whose ink
    /// reaches between the staves gets the room it needs and its neighbours do not.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:217-268 — every System owns a
    /// VerticalAlignment and is spaced through it, so there is no score-wide staff
    /// placement in LilyPond to share. Until 2026-07-26 Lily# computed ONE placement from
    /// system 0's measure layouts and handed it to every system, so a later system whose
    /// music needed more room silently got system 0's answer. The corpus could not see it:
    /// no fixture has two systems that want different distances, which is why this is a
    /// test and not a snapshot.
    /// <para>
    /// ⚠️ ASSERTS THE RULE, NOT THE NUMBERS. What matters is that system 1's staves stand
    /// further apart than system 0's, because only system 1's ink asks for it; the shared
    /// version returns the same distance for both and fails here.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachSystemIsSpacedByItsOwnInk_NotByTheFirstSystems()
    {
        // System 0 is ordinary. System 1 drives the treble staff far below its own staff
        // and the bass staff far above its own, so its ink between the two needs much more
        // than staff-staff basic-distance.
        const string source = """
            octave absolute
            time 4/4
            key c major

            part rh { clef treble }
            part lh { clef bass }

            section Main {
              rh { c'4 d' e' f' |
                   break
                   c,,4 d,, e,, f,, | }
              lh { c,4 d, e, f, |
                   break
                   c''4 d'' e'' f'' | }
            }

            form main { ~Main }

            score main "TWO" {
              grandStaff {
                staff rh
                staff lh
              }
            }
            """;

        var geometry = LpFidelity.RenderedGeometry.Render(source);
        var refpoints = geometry.StaffRefpoints(0);

        Assert.Equal(4, refpoints.Count);
        double insideSystem0 = refpoints[1] - refpoints[0];
        double insideSystem1 = refpoints[3] - refpoints[2];

        Assert.Equal(
            DefaultOptions.StaffSpacing.StaffStaff.BasicDistance, insideSystem0, 6);
        Assert.True(insideSystem1 > insideSystem0 + 1.0,
            $"system 1's ink needs room system 0's does not, but the two were spaced alike: "
            + $"{insideSystem1:F6} against {insideSystem0:F6} — the placement is being shared");
    }

    /// <summary>
    /// A CHORD ROW'S ENTRY IN THE PER-STAFF SKYLINE LIST IS ITS SYMBOL INK — the wiring, on a
    /// real score, of what <c>StaffSkylineFrameTests</c> asserts on the pieces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:948-990 — a ChordNames context is one of the
    /// alignment's non-spaceable lines, so the walk that fixes the distances above and below
    /// it measures ITS skyline, which is its ChordName stencils.
    /// <para>
    /// ⚠️ THE WIRING IS THE POINT, not the value: the row's ink is built by
    /// <c>ChordNameEngraver.RowSkylines</c> and merged by
    /// <c>MultiStaffLayouter.BuildAllStaffSkylines</c>, two files apart, and a version that
    /// built it and forgot to merge it looks exactly like this test's absence
    /// (HANDOFF 5.4: a measurement helper has to assert the premise that makes its reading
    /// mean anything).
    /// </para>
    /// <para>
    /// ⚠️ AND IT NAMES WHAT USED TO BE THERE. Before 2026-07-27 this entry was a five-line
    /// staff symbol (±2.050000) plus the row's UNDRAWN noteheads (±0.500000) — a row that
    /// prints neither — while the symbols that ARE drawn were in no skyline at all. Either
    /// phantom coming back fails this by more than a staff space.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChordRowsSkylineEntry_IsItsSymbolInk_NotAStaffSymbolAndNotItsUndrawnNotes()
    {
        const string source = """
            octave absolute
            time 4/4
            key c major

            part melody { clef treble }

            section Main {
              melody { g4 a g a | g4 a g a | }
              chords prog { c1 | c1 | }
            }

            form main { ~Main }

            score main "ROW" {
              chords prog
              staff melody
            }
            """;

        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(
            tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine().Layout(score);

        var skylines = new MultiStaffLayouter(DefaultOptions, new MeasureLayouter())
            .BuildStaffSkylines(
                score, new SkylineBuilder(DefaultOptions.StaffHeight),
                layout.Systems[0].Measures, systemIndex: 0)
            .Skylines;

        // Staff 0 is the chord row (`chords prog` comes first in the score block).
        // The em AND the series come from the engraving defaults rather than literals — see
        // the note on StaffSkylineFrameTests.ChordRow_OwnInkIsMeasuredFromItsTextBaseline,
        // which held the same third copy of both.
        var (bottom, top) = LilySharp.Core.Rendering.TextFontMetrics.Ink(
            "C", LilySharp.Core.Svg.EngravingDefaults.ChordNameFontSize,
            sans: true, LilySharp.Core.Svg.EngravingDefaults.ChordNameFontStyle);
        Assert.Equal(top, skylines[0].Up.MaxHeight(), 9);
        Assert.Equal(bottom, skylines[0].Down.MaxHeight(), 9);

        // ...and the staff below still carries a staff's own silhouette, so the reading above
        // is the ROW's rule and not a builder that stopped seeding anything.
        Assert.True(skylines[1].Up.MaxHeight() > 3.0);
        Assert.True(skylines[1].Down.MaxHeight() < -3.0);
    }

    /// <summary>Inside-staff skylines for one staff holding a single one-measure voice.</summary>
    private static (VerticalSkyline Up, VerticalSkyline Down) BuildOneItemStaffSkylines(
        MusicItem item)
    {
        var measure = new Measure(ImmutableArray.Create(item),
            BarlineType.None, BarlineType.Single, null, 0, 0);
        var staff = Staff.Create(ClefType.Treble,
            new Voice("v", ImmutableArray.Create(measure)));
        return new SkylineBuilder(StaffHeight)
            .BuildInsideStaffSkylines(staff, CreateSimpleMeasureLayouts(1));
    }

    /// <summary>
    /// A DRAWN AUGMENTATION DOT IS IN THE STAFF'S OWN SKYLINE — as its extent box, at the
    /// renderer's column X and resolved position — and an undotted twin reserves nothing
    /// there. The dot is what LilyPond's outside-staff pass makes a WIDE fermata clear
    /// (three script levels over a dotted note where a boxless seed gave two:
    /// input/regression/fermata-dot-position.ly block A, LP 4.255/4.183/4.003).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1272-1288 Dots — dots::calc-dot-stencil and
    ///   friends, but NO vertical-skylines entry, so the extents default applies.
    /// LILYPOND-REF: lily/grob.cc:81-85 simple_vertical_skylines_from_extents_proc.
    /// </remarks>
    [Fact]
    public void InsideStaffSkyline_CarriesTheDrawnDot_AndItsUndottedTwinDoesNot()
    {
        // A5 (staff position 6, ON the first ledger line): the dot resolves into the
        // space above (position 7), centre Y-up 3.5 — past the head top and the ledger.
        NoteItem Note(int dots) => new(6, Fraction.Quarter, dots, null, true, 0);

        // The renderer's dot column: head ink right + one dot width from the item's
        // column (5.0 in CreateSimpleMeasureLayouts); probe the dot box's centre.
        double probeX = 5.0 + GlyphMetrics.GetNoteheadBBox(4).Right
            + GlyphMetrics.AugmentationDot.Width * 1.5;

        var dotted = BuildOneItemStaffSkylines(Note(1));
        Assert.Equal(3.5 + GlyphMetrics.AugmentationDot.Top,
            dotted.Up.Height(probeX), 9);

        // The undotted twin holds only the staff symbol's own ink at that X — the dot's
        // reach is the DOT's, not a widened head (the ledger stops short of this X).
        var plain = BuildOneItemStaffSkylines(Note(0));
        Assert.True(plain.Up.Height(probeX) < 3.0,
            $"nothing but the staff should be at the dot's X, read {plain.Up.Height(probeX):F6}");

        // ...and the DOWN skyline of a low dotted note carries the dot too, for the
        // below-staff readers (position -6, on the ledger below the staff: the dot
        // resolves into the space at -5, centre Y-up -2.5, whose box bottom -2.725
        // reaches past the staff's own -2.05).
        var lowDotted = BuildOneItemStaffSkylines(
            new NoteItem(-6, Fraction.Quarter, 1, null, true, 0));
        Assert.Equal(-2.5 + GlyphMetrics.AugmentationDot.Bottom,
            lowDotted.Down.Height(probeX), 9);
    }
}
