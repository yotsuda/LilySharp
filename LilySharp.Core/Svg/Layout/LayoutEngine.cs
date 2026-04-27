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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Engine for calculating score layout.
/// </summary>
public sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;
    private readonly SkylineBuilder _skylineBuilder;
    private readonly MeasureLayouter _measureLayouter = new();
    private readonly SystemLayouter _systemLayouter;
    private readonly PageLayouter _pageLayouter;
    private readonly SystemBreaker _systemBreaker;
    private readonly MultiStaffLayouter _multiStaffLayouter;

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
        _elementCoordinator = new ElementCoordinator(_options);
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight);
        _systemLayouter = new SystemLayouter(_options, _measureLayouter);
        _pageLayouter = new PageLayouter(_options);
        _systemBreaker = new SystemBreaker(_options);
        _multiStaffLayouter = new MultiStaffLayouter(_options, _measureLayouter);
    }

    /// <summary>Calculates the complete layout for a single-staff score.</summary>
    public ScoreLayout Layout(Score score)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

        // LILYPOND-REF: lily/spacing-spanner.cc
        // Calculate the common shortest duration across all voices for Gourlay spacing
        double commonShortestDuration = SpacingRules.CalculateCommonShortestDuration(score);

        var systemMeasures = _systemBreaker.BreakIntoSystems(score, commonShortestDuration);

        // Pre-calculate first system skylines for initial Y positioning
        var firstSystemMeasures = systemMeasures.Count > 0 ? systemMeasures[0] : new List<Measure>();
        var firstMeasureLayouts = _systemLayouter.LayoutMeasuresForSystem(firstSystemMeasures, score.KeySignature.Sharps, true, 0,
            baseShortestDuration: commonShortestDuration);
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(firstSystemMeasures, firstMeasureLayouts);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout systems and build skylines in a single pass
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            var system = _systemLayouter.LayoutSystem(
                sysIdx, systemMeasures[sysIdx], currentY,
                score.KeySignature.Sharps, sysIdx == 0, firstMeasureIndex,
                score.Lyrics, isLastSystem: sysIdx == systemMeasures.Count - 1,
                baseShortestDuration: commonShortestDuration);
            systems.Add(system);

            var (upSkyline, downSkyline) = _skylineBuilder.BuildSystemSkylines(systemMeasures[sysIdx], system.Measures);
            perSystemSkylines.Add((upSkyline, downSkyline));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSkyline),
                LayoutUtilities.CalculateDownExtent(downSkyline, _options.StaffHeight)));

            currentY += _options.StaffHeight + _options.SystemSpacing;
            firstMeasureIndex += systemMeasures[sysIdx].Count;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        // Augment system extents with estimated loose line heights (lyrics, dynamics, figured bass)
        var singleMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int measStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            singleMeasureRanges.Add((measStart, sysMeasures.Count));
            measStart += sysMeasures.Count;
        }
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Lyrics, score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, singleMeasureRanges);

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, _options.StaffHeight,
            perSystemSkylines);

        var beamLayouts = _elementCoordinator.LayoutBeams(score, systemsArray);
        var tieLayouts = _elementCoordinator.LayoutTies(score, systemsArray);
        var slurLayouts = _elementCoordinator.LayoutSlurs(score, systemsArray);
        var glissandoLayouts = _elementCoordinator.LayoutGlissandos(score, systemsArray);
        // LILYPOND-REF: lily/note-collision.cc:486-502
        // Create a resolver for force-hshift manual override during collision calculation
        GrobPropertyResolver? collisionResolver = null;
        if (!score.GrobOverrides.IsDefaultOrEmpty || !score.GrobReverts.IsDefaultOrEmpty)
        {
            collisionResolver = new GrobPropertyResolver(score.GrobOverrides, score.GrobReverts);
        }
        var (voiceOffsets, headWipeEntries, dotForceDownEntries) = _elementCoordinator.CalculateVoiceOffsets(score, collisionResolver);
        var restShifts = _elementCoordinator.CalculateRestShifts(score, systemsArray, beamLayouts);

        // Detect beam groups for tuplet bracket-visibility (if-no-beam)
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket.bracket-visibility
        var beamGroups = _elementCoordinator.DetectBeamGroups(score);

        var annotations = CalculateAnnotationLayouts(
            score, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            score.Voice.Measures, score.FiguredBasses, score.ChordNames, score.PercentRepeats,
            trillSpanners: score.TrillSpanners,
            beamGroups: beamGroups);

        // Calculate part combination layouts for multi-voice scores
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        if (score.IsMultiVoice && score.Voices.Length >= 2)
        {
            var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
            var combineItems = PartCombineAnalyzer.Analyze(
                score.Voices[0], score.Voices[1], score.TimeSignature);
            partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml);
        }

        var result = BuildScoreLayout(pages, systemsArray, beamLayouts, tieLayouts, slurLayouts,
            glissandoLayouts, annotations, voiceOffsets, headWipeEntries, dotForceDownEntries, restShifts, partCombineLayouts);
        result = result with { Options = _options };

        // LILYPOND-REF: lily/grob-property.cc — attach user overrides/reverts to layout
        if (!score.GrobOverrides.IsDefaultOrEmpty || !score.GrobReverts.IsDefaultOrEmpty)
        {
            result = result with
            {
                GrobPropertyResolver = new GrobPropertyResolver(score.GrobOverrides, score.GrobReverts)
            };
        }

        return result;
    }

    /// <summary>Calculates the complete layout for a multi-staff score.</summary>
    public ScoreLayout Layout(MultiStaffScore score)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);
        double headerBottom = _options.MarginTop + headerHeight;

        // LILYPOND-REF: lily/page-layout-problem.cc:656-717 alignment_distances
        // Apply user overrides for StaffGrouper spacing before layout
        var multiStaffLayouter = _multiStaffLayouter;
        if (!score.GrobOverrides.IsDefaultOrEmpty
            && score.GrobOverrides.Any(o => o.GrobType == "StaffGrouper"))
        {
            var overriddenSpacing = _options.StaffSpacing.ApplyOverrides(score.GrobOverrides);
            if (overriddenSpacing != _options.StaffSpacing)
            {
                var overriddenOptions = _options with { StaffSpacing = overriddenSpacing };
                multiStaffLayouter = new MultiStaffLayouter(overriddenOptions, _measureLayouter);
            }
        }

        // LILYPOND-REF: lily/spacing-spanner.cc
        // Calculate the common shortest duration across all voices for Gourlay spacing
        double commonShortestDuration = SpacingRules.CalculateCommonShortestDuration(score);

        // LILYPOND-REF: ly/paper-defaults-init.ly — indent / short-indent
        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // Calculate indent from instrument names (auto-calculate if not explicitly set)
        double indent = _options.Indent > 0
            ? _options.Indent
            : CalculateIndentFromInstrumentNames(score);
        double shortIndent = _options.ShortIndent;

        var systemMeasures = _systemBreaker.BreakIntoSystems(score, commonShortestDuration);

        // LILYPOND-REF: lily/align-interface.cc:217-268
        // Compute first system measure layouts first, then use skyline-based staff spacing
        multiStaffLayouter.CurrentIndent = indent;
        var firstSystemMeasureLayouts = systemMeasures.Count > 0
            ? multiStaffLayouter.LayoutMeasures(score, 0, 0, systemMeasures[0].Count,
                baseShortestDuration: commonShortestDuration)
            : ImmutableArray<MeasureLayout>.Empty;
        double systemHeight = multiStaffLayouter.CalculateSystemHeight(
            score, _skylineBuilder, firstSystemMeasureLayouts);
        var firstStaffGroupLayouts = multiStaffLayouter.LayoutStaffGroups(
            score, 0, _skylineBuilder, firstSystemMeasureLayouts);

        // Pre-compute staff group layouts for subsequent systems (shortIndent)
        multiStaffLayouter.CurrentIndent = shortIndent;
        var defaultStaffGroupLayouts = indent != shortIndent
            ? multiStaffLayouter.LayoutStaffGroups(score, 0, _skylineBuilder, firstSystemMeasureLayouts)
            : firstStaffGroupLayouts;

        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — check if any staff uses remove-empty
        bool hasHaraKiri = score.StaffGroups.Any(g => g.Staves.Any(s => s.RemoveEmpty));

        // Pre-calculate first system skylines for initial Y positioning
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(score, firstSystemMeasureLayouts, systemHeight);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, LayoutUtilities.CalculateUpExtent(firstUpSkyline), _options.TopSystemPadding);

        // Layout each system with skyline extents
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            double sysIndent = isFirstSystem ? indent : shortIndent;
            multiStaffLayouter.CurrentIndent = sysIndent;
            int measureCount = systemMeasures[sysIdx].Count;
            var measureLayouts = isFirstSystem
                ? firstSystemMeasureLayouts
                : multiStaffLayouter.LayoutMeasures(score, sysIdx, firstMeasureIndex, measureCount,
                    sysIdx == systemMeasures.Count - 1, commonShortestDuration);

            // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — per-system staff visibility
            // When hara-kiri is active, compute per-system staff group layouts
            // so empty staves are hidden only in systems where they have no content.
            var sysStaffGroups = hasHaraKiri
                ? multiStaffLayouter.LayoutStaffGroups(
                    score, 0, firstMeasureIndex, firstMeasureIndex + measureCount, isFirstSystem)
                : (isFirstSystem ? firstStaffGroupLayouts : defaultStaffGroupLayouts);

            // Use per-system height when hara-kiri is active (different staves may be visible per system)
            double sysHeight = hasHaraKiri
                ? sysStaffGroups.Where(g => !g.Staves.All(s => s.IsHidden)).Sum(g => g.Height)
                    + Math.Max(0, (sysStaffGroups.Count(g => !g.Staves.All(s => s.IsHidden)) - 1)
                        * (_options.StaffSpacing.StaffGroupStaff.BasicDistance - _options.StaffHeight))
                : systemHeight;
            // Ensure at least one staff space for completely empty systems
            if (hasHaraKiri && sysHeight <= 0)
                sysHeight = _options.StaffHeight;

            var (upSky, downSky) = _skylineBuilder.BuildSystemSkylines(score, measureLayouts, sysHeight);
            perSystemSkylines.Add((upSky, downSky));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, sysHeight)));

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY,
                Width: _options.ContentWidth - sysIndent,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.KeySignature.Sharps, isFirstSystem,
                    score.TimeSignature.Beats, score.TimeSignature.BeatType),
                Measures: measureLayouts, StaffGroups: sysStaffGroups,
                Indent: sysIndent));
            currentY += sysHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        var multiMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int multiMeasStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            multiMeasureRanges.Add((multiMeasStart, sysMeasures.Count));
            multiMeasStart += sysMeasures.Count;
        }
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Lyrics, score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, multiMeasureRanges);

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight,
            perSystemSkylines);

        // Calculate beams/ties/slurs/glissandos per staff
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
        var allGlissandoLayouts = new List<GlissandoLayout>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer);
            allBeamLayouts.AddRange(_elementCoordinator.LayoutBeams(staffScore, systemsArray, staffIndex));
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffScore, systemsArray, staffIndex));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffScore, systemsArray, staffIndex));
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffScore, systemsArray, staffIndex));
        }

        // Resolve cross-staff layouts per voice
        var crossStaffLayouts = ImmutableArray<CrossStaffLayout>.Empty;
        if (!score.CrossStaffItems.IsDefaultOrEmpty)
        {
            // Use primary staff index (0) as default source; in full multi-voice,
            // each voice would have its own staff index.
            int primaryStaffIdx = 0;
            int staffCount = score.StaffGroups.Sum(g => g.StaffCount);
            crossStaffLayouts = CrossStaffEngraver.Calculate(
                score.CrossStaffItems, primaryStaffIdx, staffCount);
        }

        // Create primary staff Score for annotation engravers
        var primaryStaff = score.StaffGroups[0].PrimaryStaff;
        var primaryScore = new Score(
            primaryStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(primaryStaff.Clef));

        var annotations = CalculateAnnotationLayouts(
            primaryScore, systemsArray,
            score.Dynamics, score.Articulations, score.GraceNotes,
            score.Lyrics, score.MusicMarks, score.CustomTexts,
            score.VoltaBrackets, score.TupletBrackets, score.Arpeggios,
            primaryStaff.PrimaryVoice.Measures, score.FiguredBasses, score.ChordNames,
            score.PercentRepeats, crossStaffLayouts,
            trillSpanners: score.TrillSpanners);

        // Calculate part combination layouts for staves with multiple voices
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (staff.Voices.Length >= 2)
                {
                    var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
                    var combineItems = PartCombineAnalyzer.Analyze(
                        staff.Voices[0], staff.Voices[1], score.TimeSignature);
                    partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml);
                    break; // Only first multi-voice staff for now
                }
            }
            if (!partCombineLayouts.IsEmpty) break;
        }

        var result = BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            ImmutableDictionary<VoiceItemKey, double>.Empty,
            ImmutableHashSet<VoiceItemKey>.Empty,
            ImmutableHashSet<VoiceItemKey>.Empty,
            ImmutableDictionary<RestShiftKey, double>.Empty,
            partCombineLayouts);
        result = result with { Options = _options };

        // LILYPOND-REF: lily/grob-property.cc — attach user overrides/reverts to layout
        if (!score.GrobOverrides.IsDefaultOrEmpty || !score.GrobReverts.IsDefaultOrEmpty)
        {
            result = result with
            {
                GrobPropertyResolver = new GrobPropertyResolver(score.GrobOverrides, score.GrobReverts)
            };
        }

        return result;
    }

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null)
    {
        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
        {
            // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
            // Pass per-system skylines for X-dependent inter-system collision detection
            var skylines = perSystemSkylines != null
                ? (ImmutableArray<(VerticalSkyline, VerticalSkyline)>?)perSystemSkylines.ToImmutableArray()
                : null;
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray(), skylines);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        // Recalculate Y positions using skyline extents to avoid overlaps
        double headerBottom = _options.MarginTop + headerHeight;
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            headerBottom, perSystemExtents[0].upExtent, _options.TopSystemPadding);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                double padding = _options.SystemSpacing * 0.5;
                double minDistance = systemHeight + Math.Max(_options.SystemSpacing,
                    perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent + padding);
                skylineY += minDistance;
            }
        }
        double lastDownExtent = perSystemExtents[systems.Length - 1].downExtent;
        double totalHeight = skylineY + systemHeight + lastDownExtent + _options.MarginBottom;
        var systemsArray = updatedSystems.ToImmutableArray();
        var page = new PageLayout(0, _options.PageWidth, totalHeight, headerHeight, systemsArray);
        return (ImmutableArray.Create(page), systemsArray);
    }

    /// <summary>
    /// Estimates the additional down extent contributed by loose lines (lyrics, dynamics, figured bass)
    /// for a range of measures in a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height estimation
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside-staff-priority
    ///
    /// In LilyPond, non-spaceable staves (lyrics, dynamics, figured bass) are "loose lines"
    /// that are distributed between spaceable staves after the main vertical spring calculation.
    /// We estimate their height contribution so that page breaking accounts for them.
    ///
    /// Pure height estimation covers both below-staff (downExtent) and above-staff (upExtent)
    /// elements so that page breaking can accurately predict system heights.
    /// </remarks>
    private static (double downExtent, double upExtent) EstimateLooseLineExtents(
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        int startMeasure, int endMeasure)
    {
        double downExtent = 0;
        double upExtent = 0;

        // --- Below-staff elements (downExtent) ---

        // LILYPOND-REF: scm/define-grobs.scm LyricText.outside-staff-priority = #(* 100 1)
        // Lyrics: staffPadding(2.5) + (verseCount-1) * verseSpacing(1.8) + fontSize(1.2)
        if (!lyrics.IsDefaultOrEmpty)
        {
            int maxVerse = 0;
            foreach (var lyric in lyrics)
            {
                if (lyric.MeasureIndex >= startMeasure && lyric.MeasureIndex < endMeasure)
                    maxVerse = Math.Max(maxVerse, lyric.VerseNumber);
            }
            if (maxVerse > 0)
                downExtent = Math.Max(downExtent, 2.5 + (maxVerse - 1) * 1.8 + 1.2);
        }

        // LILYPOND-REF: scm/define-grobs.scm DynamicLineSpanner.outside-staff-priority = 250
        // Dynamics + hairpins: staffPadding(0.2) + padding(0.6) + textAscent(1.2) = 2.0
        bool hasDynamic = false;
        if (!dynamics.IsDefaultOrEmpty)
        {
            foreach (var dyn in dynamics)
            {
                if (dyn.MeasureIndex >= startMeasure && dyn.MeasureIndex < endMeasure)
                {
                    hasDynamic = true;
                    break;
                }
            }
            if (hasDynamic)
                downExtent = Math.Max(downExtent, 2.0);
        }

        // LILYPOND-REF: scm/define-grobs.scm Hairpin — same DynamicLineSpanner (priority 250)
        // Hairpins share Y level with dynamics; estimate ~1.5 ss if no dynamics
        if (!musicMarks.IsDefaultOrEmpty && !hasDynamic)
        {
            bool hasHairpin = false;
            foreach (var mark in musicMarks)
            {
                if (mark.MeasureIndex >= startMeasure && mark.MeasureIndex < endMeasure
                    && (mark.Type == MusicMarkType.Cresc || mark.Type == MusicMarkType.Decresc
                        || mark.Type == MusicMarkType.Dim))
                {
                    hasHairpin = true;
                    break;
                }
            }
            if (hasHairpin)
                downExtent = Math.Max(downExtent, 1.5);
        }

        // LILYPOND-REF: scm/define-grobs.scm BassFigure
        // Figured bass: staffPadding(1.0) + belowStaffOffset(1.0) + figCount * figureSpacing(1.5)
        if (!figuredBasses.IsDefaultOrEmpty)
        {
            int maxFigures = 0;
            foreach (var fb in figuredBasses)
            {
                if (fb.MeasureIndex >= startMeasure && fb.MeasureIndex < endMeasure)
                    maxFigures = Math.Max(maxFigures, fb.Figures.Length);
            }
            if (maxFigures > 0)
                downExtent = Math.Max(downExtent, 2.0 + maxFigures * 1.5);
        }

        // --- Above-staff elements (upExtent) ---

        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var mark in musicMarks)
            {
                if (mark.MeasureIndex < startMeasure || mark.MeasureIndex >= endMeasure)
                    continue;

                // LILYPOND-REF: scm/define-grobs.scm MetronomeMark.outside-staff-priority = 1000
                if (mark.Type == MusicMarkType.Tempo)
                    upExtent = Math.Max(upExtent, 2.5); // tempo + metronome mark height

                // LILYPOND-REF: scm/define-grobs.scm RehearsalMark.outside-staff-priority = 1500
                if (mark.Type == MusicMarkType.Rehearsal)
                    upExtent = Math.Max(upExtent, 3.0); // boxed rehearsal mark

                // LILYPOND-REF: scm/define-grobs.scm SectionLabel
                if (mark.Type == MusicMarkType.SectionLabel)
                    upExtent = Math.Max(upExtent, 3.5); // boxed section label

                // LILYPOND-REF: scm/define-grobs.scm SegnoMark/CodaMark
                if (mark.Type == MusicMarkType.Segno || mark.Type == MusicMarkType.Coda)
                    upExtent = Math.Max(upExtent, 2.5); // glyph above staff
            }
        }

        // LILYPOND-REF: scm/define-grobs.scm VoltaBracketSpanner.outside-staff-priority = 600
        if (!voltaBrackets.IsDefaultOrEmpty)
        {
            foreach (var vb in voltaBrackets)
            {
                if (vb.StartMeasureIndex < endMeasure && vb.EndMeasureIndex >= startMeasure)
                {
                    upExtent = Math.Max(upExtent, 2.0); // volta bracket height
                    break;
                }
            }
        }

        return (downExtent, upExtent);
    }

    /// <summary>
    /// Augments per-system extents with estimated loose line heights.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height
    /// Estimates both above-staff (upExtent) and below-staff (downExtent)
    /// contributions from annotations, so page breaking accounts for full system height.
    /// </remarks>
    private static void AugmentExtentsWithLooseLines(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        List<(int startMeasure, int measureCount)> systemMeasureRanges)
    {
        for (int i = 0; i < perSystemExtents.Count && i < systemMeasureRanges.Count; i++)
        {
            var (start, count) = systemMeasureRanges[i];
            var (looseDown, looseUp) = EstimateLooseLineExtents(
                lyrics, dynamics, figuredBasses, musicMarks, voltaBrackets,
                start, start + count);

            var ext = perSystemExtents[i];
            if (looseDown > 0 || looseUp > 0)
            {
                perSystemExtents[i] = (
                    Math.Max(ext.upExtent, looseUp),
                    Math.Max(ext.downExtent, looseDown));
            }
        }
    }

    private AnnotationLayouts CalculateAnnotationLayouts(
        Score? score, ImmutableArray<SystemLayout> systems,
        ImmutableArray<DynamicItem> dynamics, ImmutableArray<ArticulationItem> articulations,
        ImmutableArray<GraceNoteItem> graceNotes, ImmutableArray<LyricItem> lyrics,
        ImmutableArray<MusicMarkItem> musicMarks, ImmutableArray<CustomTextItem> customTexts,
        ImmutableArray<VoltaBracketItem> voltaBrackets, ImmutableArray<TupletBracketItem> tupletBrackets,
        ImmutableArray<ArpeggioItem> arpeggios, ImmutableArray<Measure> measures,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffLayout>? crossStaffLayouts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null,
        ImmutableArray<BeamGroup>? beamGroups = null)
    {
        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();
        var lyricLayouts = new LyricEngraver().CalculateLayouts(lyrics, ml, _options.StaffHeight);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, systems, ml) : ImmutableArray<DynamicLayout>.Empty;

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        var textSpannerItems = TextSpannerEngraver.DetectTextSpanners(musicMarks);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        var ottavaLayouts = OttavaBracketEngraver.Calculate(ottavaItems, systems, ml);

        // Layout arpeggio markings
        var arpeggioLayouts = ArpeggioEngraver.Calculate(arpeggios, systems, ml, _options.StaffHeight);

        // Detect and layout pedal brackets from sustain/sostenuto/una corda marks
        var pedalBracketItems = PedalEngraver.DetectPedalBrackets(musicMarks);
        var pedalBracketLayouts = PedalEngraver.Calculate(pedalBracketItems, systems, ml);

        // Layout figured bass
        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty, systems, ml);

        // Layout chord names
        var chordNameLayouts = ChordNameEngraver.Calculate(
            chordNames ?? ImmutableArray<ChordNameItem>.Empty, systems, ml);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line)
        // LILYPOND-REF: scm/scheme-engravers.scm — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml);

        // Calculate volta brackets first — needed by MusicMarkEngraver for collision avoidance
        // LILYPOND-REF: axis-group-interface.cc — elements sorted by outside-staff-priority
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml);

        // LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
        // Post-process below-staff elements using priority-based stacking.
        // This ensures hairpins avoid dynamics (both priority 250) and
        // text spanners avoid both dynamics and hairpins (priority 350).
        var (stackedDynamics, stackedHairpins, stackedTextSpanners) =
            OutsideStaffStacker.StackBelowStaff(systems, dynamicLayouts, hairpinLayouts, textSpannerLayouts);

        return new AnnotationLayouts(
            Dynamics: stackedDynamics,
            Articulations: score != null ? ArticulationEngraver.Calculate(score, articulations, systems, ml) : ImmutableArray<ArticulationLayout>.Empty,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, systems, ml) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(lyricLayouts, systems),
            MusicMarks: MusicMarkEngraver.Calculate(score, musicMarks, systems, ml, measures, voltaBracketLayouts),
            CustomTexts: CustomTextEngraver.Calculate(score, customTexts, systems, ml),
            VoltaBrackets: voltaBracketLayouts,
            TupletBrackets: TupletBracketEngraver.Calculate(tupletBrackets, systems, ml, measures, beamGroups ?? default),
            Hairpins: stackedHairpins,
            TextSpanners: stackedTextSpanners,
            OttavaBrackets: ottavaLayouts,
            Arpeggios: arpeggioLayouts,
            PedalBrackets: pedalBracketLayouts,
            FiguredBasses: figuredBassLayouts,
            ChordNames: chordNameLayouts,
            PercentRepeats: percentRepeatLayouts,
            CrossStaffs: crossStaffLayouts ?? ImmutableArray<CrossStaffLayout>.Empty,
            TrillSpanners: trillSpannerLayouts,
            // LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob.
            Fingerings: score != null
                ? FingeringEngraver.Calculate(score, systems)
                : ImmutableArray<FingeringLayout>.Empty,
            // LILYPOND-REF: lily/laissez-vibrer-engraver.cc + repeat-tie-engraver.cc — half-ties.
            TieVariants: score != null
                ? TieVariantEngraver.Calculate(score, systems)
                : ImmutableArray<TieVariantLayout>.Empty,
            // LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob.
            MultiMeasureRests: score != null
                ? MultiMeasureRestEngraver.Calculate(score, systems, _options.StaffHeight)
                : ImmutableArray<MultiMeasureRestLayout>.Empty,
            // LILYPOND-REF: lily/ledger-line-spanner.cc — LedgerLineSpanner grob.
            LedgerLineSpans: score != null
                ? LedgerLineSpannerEngraver.Calculate(score, systems, _options.StaffHeight)
                : ImmutableArray<LedgerLineSpan>.Empty,
            // LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob.
            BarNumbers: BarNumberEngraver.Calculate(systems),
            // LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob.
            StanzaNumbers: StanzaNumberEngraver.Calculate(lyricLayouts, systems));
    }

    private static ScoreLayout BuildScoreLayout(
        ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs, ImmutableArray<GlissandoLayout> glissandos,
        AnnotationLayouts a,
        ImmutableDictionary<VoiceItemKey, double> voiceOffsets,
        ImmutableHashSet<VoiceItemKey> headWipeEntries,
        ImmutableHashSet<VoiceItemKey> dotForceDownEntries,
        ImmutableDictionary<RestShiftKey, double> restShifts,
        ImmutableArray<PartCombineLayout> partCombineLayouts = default)
    {
        return new ScoreLayout(pages, systems, beams, ties, slurs,
            a.Dynamics, a.Articulations, a.GraceNotes,
            a.Lyrics, a.LyricHyphens, a.MusicMarks,
            a.CustomTexts, a.VoltaBrackets, a.TupletBrackets,
            a.Hairpins, a.TextSpanners, a.OttavaBrackets,
            glissandos, a.Arpeggios, a.PedalBrackets,
            a.FiguredBasses, a.ChordNames, a.PercentRepeats,
            a.CrossStaffs,
            partCombineLayouts.IsDefault ? ImmutableArray<PartCombineLayout>.Empty : partCombineLayouts,
            a.TrillSpanners,
            a.Fingerings,
            a.TieVariants,
            a.MultiMeasureRests,
            a.LedgerLineSpans,
            a.BarNumbers,
            a.StanzaNumbers,
            voiceOffsets, headWipeEntries, dotForceDownEntries, restShifts);
    }

    /// <summary>
    /// Calculates the indent needed to accommodate instrument names.
    /// Returns the indent in staff spaces, or 0 if no names are present.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/pango-font.cc — LilyPond uses exact Pango font metrics.
    /// We approximate with average character width ≈ 0.5 × font-size for serif fonts.
    /// LILYPOND-REF: scm/define-grobs.scm:1711-1728 InstrumentName padding = 0.3
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    /// </remarks>
    private static double CalculateIndentFromInstrumentNames(MultiStaffScore score)
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm
        // 15mm / (20pt/4 × 0.3528mm/pt) = 15 / 1.764 ≈ 8.5 staff spaces
        const double DefaultIndent = 8.5;

        double maxNameWidth = 0;

        // Font size: SvgRenderer.FontSize (4.0) × instrument name scale (0.75) = 3.0
        // Average serif character width ≈ 0.5 × font-size = 1.5 staff spaces
        const double charWidth = 1.5;

        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (!string.IsNullOrEmpty(staff.InstrumentName))
                {
                    double nameWidth = staff.InstrumentName.Length * charWidth;
                    if (nameWidth > maxNameWidth)
                        maxNameWidth = nameWidth;
                }
            }
        }

        if (maxNameWidth <= 0)
            return 0;

        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // Indent = max(LP default 15mm, name width + delimiter + padding)
        double calculatedIndent = maxNameWidth + 1.5; // 1.0 delimiter + 0.3 name padding + 0.2 extra
        return Math.Max(DefaultIndent, calculatedIndent);
    }

    private static string ClefToString(ClefType clef) => clef switch
    {
        ClefType.Treble => "treble",
        ClefType.Bass => "bass",
        ClefType.Alto => "alto",
        ClefType.Tenor => "tenor",
        ClefType.Treble8Below => "treble_8",
        _ => "treble"
    };

    private sealed record AnnotationLayouts(
        ImmutableArray<DynamicLayout> Dynamics,
        ImmutableArray<ArticulationLayout> Articulations,
        ImmutableArray<GraceNoteLayout> GraceNotes,
        ImmutableArray<LyricLayout> Lyrics,
        ImmutableArray<LyricHyphenLayout> LyricHyphens,
        ImmutableArray<MusicMarkLayout> MusicMarks,
        ImmutableArray<CustomTextLayout> CustomTexts,
        ImmutableArray<VoltaBracketLayout> VoltaBrackets,
        ImmutableArray<TupletBracketLayout> TupletBrackets,
        ImmutableArray<HairpinLayout> Hairpins,
        ImmutableArray<TextSpannerLayout> TextSpanners,
        ImmutableArray<OttavaBracketLayout> OttavaBrackets,
        ImmutableArray<ArpeggioLayout> Arpeggios,
        ImmutableArray<PedalBracketLayout> PedalBrackets,
        ImmutableArray<FiguredBassLayout> FiguredBasses,
        ImmutableArray<ChordNameLayout> ChordNames,
        ImmutableArray<PercentRepeatLayout> PercentRepeats,
        ImmutableArray<CrossStaffLayout> CrossStaffs,
        ImmutableArray<TrillSpannerLayout> TrillSpanners,
        ImmutableArray<FingeringLayout> Fingerings,
        ImmutableArray<TieVariantLayout> TieVariants,
        ImmutableArray<MultiMeasureRestLayout> MultiMeasureRests,
        ImmutableArray<LedgerLineSpan> LedgerLineSpans,
        ImmutableArray<BarNumberLayout> BarNumbers,
        ImmutableArray<StanzaNumberLayout> StanzaNumbers);
}
