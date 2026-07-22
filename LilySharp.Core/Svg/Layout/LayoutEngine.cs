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
internal sealed class LayoutEngine
{
    private readonly LayoutOptions _options;
    private readonly ElementCoordinator _elementCoordinator;
    private readonly SkylineBuilder _skylineBuilder;
    private readonly MeasureLayouter _measureLayouter = new();
    private readonly PageLayouter _pageLayouter;
    private readonly SystemBreaker _systemBreaker;
    private readonly MultiStaffLayouter _multiStaffLayouter;

    public LayoutEngine(LayoutOptions? options = null)
    {
        _options = options ?? LayoutOptions.Default;
        _elementCoordinator = new ElementCoordinator(_options);
        _skylineBuilder = new SkylineBuilder(_options.StaffHeight);
        _pageLayouter = new PageLayouter(_options);
        _systemBreaker = new SystemBreaker(_options);
        _multiStaffLayouter = new MultiStaffLayouter(_options, _measureLayouter);
    }

    /// <summary>
    /// Calculates the complete layout for a single-staff score by wrapping it in a
    /// <see cref="MultiStaffScore"/> and running the single, real layout path — the same
    /// path the renderer takes for every score (see SvgGenerator.CollectScore). Kept as a
    /// convenience entry for the many single-voice call sites.
    /// </summary>
    public ScoreLayout Layout(Score score)
    {
        return Layout(MultiStaffScore.FromScore(score));
    }

    /// <summary>Calculates the complete layout for a multi-staff score.</summary>
    public ScoreLayout Layout(MultiStaffScore score, IReadOnlyList<int>? precomputedLineSizes = null,
        SystemLayoutCache? systemCache = null)
    {
        double headerHeight = LayoutUtilities.CalculateHeaderHeight(score.Title, score.Composer);

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

        // Beam membership drives note spacing (a beamed note has no flag). Collect
        // it across every staff's voices before laying out the measures.
        var beamGroups = new List<BeamGroup>();
        foreach (var (_, staff, _) in score.EnumerateStaves())
            beamGroups.AddRange(_elementCoordinator.DetectBeamGroups(
                new Score(staff.Voices, score.TimeSignature, score.KeySignature, ClefToString(staff.Clef))));
        _measureLayouter.IsItemBeamed = BeamedPredicate(beamGroups);

        // LILYPOND-REF: ly/paper-defaults-init.ly — indent / short-indent
        // LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
        // Calculate indent from instrument names (auto-calculate if not explicitly set)
        double indent = _options.Indent > 0
            ? _options.Indent
            : CalculateIndentFromInstrumentNames(score);
        double shortIndent = _options.ShortIndent;

        // F3 incremental cutoff: when the line-break gate is unchanged the driver
        // passes the cached per-line measure counts so SystemBreaker regroups the
        // new measures and skips the DP. Null => normal (byte-identical) breaking.
        var systemMeasures = _systemBreaker.BreakIntoSystems(score, commonShortestDuration, precomputedLineSizes);

        // LILYPOND-REF: lily/align-interface.cc:217-268
        // Compute first system measure layouts first, then use skyline-based staff spacing
        multiStaffLayouter.CurrentIndent = indent;
        var firstSystemMeasureLayouts = systemMeasures.Count > 0
            ? ComputeSystemMeasures(systemCache, 0, systemMeasures[0].Count, true,
                systemMeasures.Count == 1, indent, commonShortestDuration,
                () => multiStaffLayouter.LayoutMeasures(score, 0, 0, systemMeasures[0].Count,
                    isLastSystem: systemMeasures.Count == 1,
                    baseShortestDuration: commonShortestDuration))
            : ImmutableArray<MeasureLayout>.Empty;
        double systemHeight = multiStaffLayouter.CalculateSystemHeight(
            score, _skylineBuilder, firstSystemMeasureLayouts);
        var firstStaffGroupLayouts = multiStaffLayouter.LayoutStaffGroups(
            score, _skylineBuilder, firstSystemMeasureLayouts);

        // Pre-compute staff group layouts for subsequent systems (shortIndent)
        multiStaffLayouter.CurrentIndent = shortIndent;
        var defaultStaffGroupLayouts = indent != shortIndent
            ? multiStaffLayouter.LayoutStaffGroups(score, _skylineBuilder, firstSystemMeasureLayouts)
            : firstStaffGroupLayouts;

        // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — check if any staff uses remove-empty
        bool hasHaraKiri = score.StaffGroups.Any(g => g.Staves.Any(s => s.RemoveEmpty));

        // Pre-calculate first system skylines for initial Y positioning
        var (firstUpSkyline, _) = _skylineBuilder.BuildSystemSkylines(
            score, firstSystemMeasureLayouts, systemHeight, indent);
        double currentY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, LayoutUtilities.CalculateUpExtent(firstUpSkyline),
            _options.StaffHeight / 2.0, _options.VerticalSpacing.TopSystem);

        // Layout each system with skyline extents
        var systems = new List<SystemLayout>();
        var perSystemExtents = new List<(double upExtent, double downExtent)>();
        var perSystemSkylines = new List<(VerticalSkyline up, VerticalSkyline down)>();
        // Per-system body height. Equals the scalar systemHeight for every system
        // unless hara-kiri hides different staves per system (then each system is as
        // tall as its OWN surviving staves). CreatePages spaces systems by this so a
        // hara-kiri'd system's gap is not over-reserved at the full height.
        var perSystemHeights = new List<double>();
        int firstMeasureIndex = 0;
        for (int sysIdx = 0; sysIdx < systemMeasures.Count; sysIdx++)
        {
            bool isFirstSystem = sysIdx == 0;
            double sysIndent = isFirstSystem ? indent : shortIndent;
            multiStaffLayouter.CurrentIndent = sysIndent;
            int measureCount = systemMeasures[sysIdx].Count;
            var measureLayouts = isFirstSystem
                ? firstSystemMeasureLayouts
                : ComputeSystemMeasures(systemCache, firstMeasureIndex, measureCount, false,
                    sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration,
                    () => multiStaffLayouter.LayoutMeasures(score, sysIdx, firstMeasureIndex, measureCount,
                        sysIdx == systemMeasures.Count - 1, commonShortestDuration));

            // LILYPOND-REF: lily/hara-kiri-group-spanner.cc — per-system staff visibility
            // When hara-kiri is active, compute per-system staff group layouts
            // so empty staves are hidden only in systems where they have no content.
            var sysStaffGroups = hasHaraKiri
                ? multiStaffLayouter.LayoutStaffGroups(
                    score, firstMeasureIndex, firstMeasureIndex + measureCount, isFirstSystem)
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

            var (upSky, downSky) = ComputeSystemSkyline(systemCache, firstMeasureIndex, measureCount,
                isFirstSystem, sysIdx == systemMeasures.Count - 1, sysIndent, commonShortestDuration, sysHeight,
                () => _skylineBuilder.BuildSystemSkylines(score, measureLayouts, sysHeight, sysIndent));
            perSystemSkylines.Add((upSky, downSky));
            perSystemExtents.Add((
                LayoutUtilities.CalculateUpExtent(upSky),
                LayoutUtilities.CalculateDownExtent(downSky, sysHeight)));
            perSystemHeights.Add(sysHeight);

            systems.Add(new SystemLayout(
                SystemIndex: sysIdx, Y: currentY,
                Width: _options.ContentWidth - sysIndent,
                PrefixWidth: SpacingRules.CalculatePrefixWidth(score.LeadingKeySharps, isFirstSystem && !score.AllStavesTab,
                    score.TimeSignature.Beats, score.TimeSignature.BeatType),
                Measures: measureLayouts, StaffGroups: sysStaffGroups,
                Indent: sysIndent));
            currentY += sysHeight + _options.SystemSpacing;
            firstMeasureIndex += measureCount;
        }

        // LILYPOND-REF: lily/page-layout-problem.cc:1025-1054 distribute_loose_lines()
        var perSystemBands = new List<(double bandUp, double bandDown)>();
        var multiMeasureRanges = new List<(int startMeasure, int measureCount)>();
        int multiMeasStart = 0;
        foreach (var sysMeasures in systemMeasures)
        {
            multiMeasureRanges.Add((multiMeasStart, sysMeasures.Count));
            multiMeasStart += sysMeasures.Count;
        }
        // Chord symbols on a TEXT ROW (lead sheets) live in their own band and
        // must not inflate a music staff's up-extent; inline chord symbols
        // (nameless `chords { }`) sit above their staff and must.
        var textRowStaves = new HashSet<int>();
        foreach (var (_, st, gi) in score.EnumerateStaves())
            if (st.IsTextRow)
                textRowStaves.Add(gi);
        var inlineChordNames = score.ChordNames
            .Where(c => !textRowStaves.Contains(c.StaffIndex)).ToImmutableArray();
        AugmentExtentsWithLooseLines(perSystemExtents,
            score.Lyrics, score.Dynamics, score.FiguredBasses,
            score.MusicMarks, score.VoltaBrackets, multiMeasureRanges,
            inlineChordNames, perSystemBands);

        // Preliminary annotation pass (see the single-staff path): real
        // protrusions of brackets/marks/voltas/dynamics/ties/slurs join the
        // spacing extents before the page Y is fixed.
        List<(VerticalSkyline up, VerticalSkyline down)>? pagingSkylines = perSystemSkylines;
        {
            var prelimSystems = systems.ToImmutableArray();
            var prelimStaff = score.PrimaryContentStaff;
            var prelimScore = new Score(
                prelimStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(prelimStaff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: score.TupletBrackets)
            {
                TempoText = score.TempoText,
                TempoBeatUnit = score.TempoBeatUnit,
                TempoDots = score.TempoDots,
            };
            var prelimBeams = new List<BeamLayout>();
            var prelimTies = new List<TieLayout>();
            var prelimSlurs = new List<SlurLayout>();
            foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
            {
                // Beam detection breaks at tuplet boundaries by note index, so a
                // per-staff beam score must see only THIS staff's tuplets — else a
                // tuplet on another staff would split a beam at a colliding index.
                var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
                var staffScore = new Score(
                    staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                    ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                    tupletBrackets: staffTuplets);
                // Beams detect per voice — expose every voice so voice 2's beam
                // protrusions join the spacing extents (matches the final pass).
                // Ties/slurs keep the primary-voice prelim score (unchanged).
                var staffBeamScore = staff.Voices.Length > 1
                    ? new Score(
                        staff.Voices, score.TimeSignature, score.KeySignature,
                        ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                        tupletBrackets: staffTuplets)
                    : staffScore;
                var staffPrelimBeams = _elementCoordinator.LayoutBeams(staffBeamScore, prelimSystems, staffIndex);
                prelimBeams.AddRange(staffPrelimBeams);
                prelimTies.AddRange(_elementCoordinator.LayoutTies(staffScore, prelimSystems, staffIndex, staff));
                prelimSlurs.AddRange(_elementCoordinator.LayoutSlurs(staffScore, prelimSystems, staffIndex, staff, score.GraceNotes, staffPrelimBeams));
            }
            // The SAME per-staff / per-voice lookups the final annotation pass gets. Without
            // them TupletBracketEngraver falls back to the PRIMARY staff's PRIMARY voice for
            // every tuplet, so a voice-two tuplet is positioned from voice one's notes and a
            // lower staff's tuplet from the top staff's — silently, because the FINAL pass
            // does have the lookups and draws the bracket correctly. Only the spacing pass
            // was wrong, which is exactly the kind of divergence a snapshot cannot see.
            // Caught by system.tuplet-bracket-down staying put while its mirror -up moved.
            var prelimVoicesByStaff = new Dictionary<int, ImmutableArray<Voice>>();
            var prelimMeasuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
            var prelimStaffByIndex = new Dictionary<int, Staff>();
            foreach (var (_, st, idx) in score.EnumerateStaves())
            {
                prelimVoicesByStaff[idx] = st.Voices;
                prelimMeasuresByStaff[idx] = st.PrimaryVoice.Measures;
                prelimStaffByIndex[idx] = st;
            }
            // Device-DOWN staff offsets, built the same way the final pass builds its own.
            // Read from the PRELIMINARY systems on purpose: a staff's offset INSIDE a system
            // is fixed by the staff layout and paging only moves the system's own Y, so this
            // is the same table — and it has to exist before paging, which is what needs it.
            var prelimStaffYByIndex = new Dictionary<int, double>();
            if (prelimSystems.Length > 0 && !prelimSystems[0].StaffGroups.IsDefaultOrEmpty)
                foreach (var sg in prelimSystems[0].StaffGroups)
                    foreach (var st in sg.Staves)
                        prelimStaffYByIndex[st.StaffIndex] = -st.Y;

            var prelimAnn = CalculateAnnotationLayouts(new AnnotationLayoutContext
            {
                Score = prelimScore,
                Systems = prelimSystems,
                Dynamics = score.Dynamics,
                Articulations = score.Articulations,
                GraceNotes = score.GraceNotes,
                Lyrics = score.Lyrics,
                MusicMarks = score.MusicMarks,
                CustomTexts = score.CustomTexts,
                VoltaBrackets = score.VoltaBrackets,
                TupletBrackets = score.TupletBrackets,
                Arpeggios = score.Arpeggios,
                Measures = prelimStaff.PrimaryVoice.Measures,
                FiguredBasses = score.FiguredBasses,
                ChordNames = score.ChordNames,
                PercentRepeats = score.PercentRepeats,
                TrillSpanners = score.TrillSpanners,
                BeamGroups = _elementCoordinator.DetectBeamGroups(prelimScore),
                BeamLayouts = prelimBeams.ToImmutableArray(),
                SystemSkylines = perSystemSkylines,
                TupletForceStemUp = prelimStaff.IsMultiVoice,
                StaffVoices = prelimStaff.Voices,
                VoicesByStaff = prelimVoicesByStaff,
                MeasuresByStaff = prelimMeasuresByStaff,
                StaffYByIndex = prelimStaffYByIndex,
                StaffByIndex = prelimStaffByIndex,
            });
            EnrichExtentsWithAnnotationProtrusions(perSystemExtents, prelimSystems,
                prelimAnn, prelimTies.ToImmutableArray(), prelimSlurs.ToImmutableArray());
            pagingSkylines = AugmentSkylinesForPaging(
                perSystemSkylines, prelimAnn.Articulations, prelimAnn.FiguredBasses,
                prelimAnn.VoltaBrackets, prelimSystems,
                prelimAnn.MusicMarks, prelimAnn.CustomTexts, prelimAnn.ChordNames,
                prelimAnn.BarNumbers, prelimAnn.TupletBrackets);
        }

        var (pages, systemsArray) = CreatePages(
            systems.ToImmutableArray(), headerHeight, perSystemExtents, systemHeight,
            pagingSkylines, perSystemHeights, perSystemBands);

        // Calculate beams/ties/slurs/glissandos per staff
        var (allBeamLayouts, allTieLayouts, allSlurLayouts, allGlissandoLayouts, restShifts) =
            LayoutAllSpanners(score, systemsArray);

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
        var primaryStaff = score.PrimaryContentStaff;
        var primaryScore = new Score(
            primaryStaff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(primaryStaff.Clef), score.Tempo, score.Title, score.Composer,
            tupletBrackets: score.TupletBrackets, swingSubdivision: score.SwingSubdivision,
            // The MMR engraver reads score.ChordNames to keep a chord-bearing
            // rest bar out of a compressed run (see MultiMeasureRestEngraver).
            chordNames: score.ChordNames)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };

        // Per-staff lookups so a dynamic is positioned under its OWN staff (clears
        // that staff's stems) and offset to it — score-level dynamics otherwise all
        // collapse onto the first staff. Staff vertical offsets are uniform across
        // systems, so read them from the first laid-out system.
        var voicesByStaff = new Dictionary<int, ImmutableArray<Voice>>();
        var measuresByStaff = new Dictionary<int, ImmutableArray<Measure>>();
        var staffByIndex = new Dictionary<int, Staff>();
        foreach (var (g, st, idx) in score.EnumerateStaves())
        {
            voicesByStaff[idx] = st.Voices;
            measuresByStaff[idx] = st.PrimaryVoice.Measures;
            staffByIndex[idx] = st;
        }
        // Device-DOWN offsets: the annotation engravers downstream (grace notes,
        // lyrics, ottava, chord names) all measure downward from the system top, so
        // this table reflects staff.Y's Y-up storage once, here at the island edge.
        var staffYByIndex = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
            foreach (var sg in systemsArray[0].StaffGroups)
                foreach (var st in sg.Staves)
                    staffYByIndex[st.StaffIndex] = -st.Y;

        // Anchor Y for note-bound lyrics attached to a staff in a NON-LAST staff group:
        // the group's BOTTOM staff Y (Y-up ⇒ min, reflected to device-down like
        // staffYByIndex above), so `staff X with lyrics …` followed
        // by another staff sits in the inter-group gap below its group. Staves in the LAST
        // group are omitted ⇒ their lyrics keep the legacy below-the-whole-system
        // placement — which deliberately includes a grand staff's staves (one group), so
        // an SATB chorale's lyrics still sit below the whole grand staff.
        var noteBoundAnchorY = new Dictionary<int, double>();
        if (systemsArray.Length > 0 && !systemsArray[0].StaffGroups.IsDefaultOrEmpty)
        {
            var groups = systemsArray[0].StaffGroups;
            for (int gi = 0; gi < groups.Length - 1; gi++)
            {
                if (groups[gi].Staves.IsDefaultOrEmpty) continue;
                double bottomY = -groups[gi].Staves.Min(s => s.Y);
                foreach (var st in groups[gi].Staves)
                    noteBoundAnchorY[st.StaffIndex] = bottomY;
            }
        }

        var annotations = CalculateAnnotationLayouts(new AnnotationLayoutContext
        {
            Score = primaryScore,
            Systems = systemsArray,
            Dynamics = score.Dynamics,
            Articulations = score.Articulations,
            GraceNotes = score.GraceNotes,
            Lyrics = score.Lyrics,
            MusicMarks = score.MusicMarks,
            CustomTexts = score.CustomTexts,
            VoltaBrackets = score.VoltaBrackets,
            TupletBrackets = score.TupletBrackets,
            Arpeggios = score.Arpeggios,
            Measures = primaryStaff.PrimaryVoice.Measures,
            FiguredBasses = score.FiguredBasses,
            ChordNames = score.ChordNames,
            PercentRepeats = score.PercentRepeats,
            CrossStaffLayouts = crossStaffLayouts,
            TrillSpanners = score.TrillSpanners,
            // bracket-visibility = if-no-beam needs the beam groups; without
            // them every tuplet bracket draws even when fully beamed. The
            // beam LAYOUTS let the suppressed-bracket number attach to the
            // beam itself.
            BeamGroups = _elementCoordinator.DetectBeamGroups(primaryScore),
            BeamLayouts = allBeamLayouts.ToImmutableArray(),
            SystemSkylines = perSystemSkylines,
            TupletForceStemUp = primaryStaff.IsMultiVoice,
            StaffVoices = primaryStaff.Voices,
            VoicesByStaff = voicesByStaff,
            MeasuresByStaff = measuresByStaff,
            StaffYByIndex = staffYByIndex,
            NoteBoundAnchorY = noteBoundAnchorY,
            StaffByIndex = staffByIndex,
        });

        var (voiceOffsets, headWipes, dotForceDown, partCombineLayouts) =
            CalculateVoiceCollisions(score, systemsArray);

        var result = BuildScoreLayout(pages, systemsArray,
            allBeamLayouts.ToImmutableArray(), allTieLayouts.ToImmutableArray(),
            allSlurLayouts.ToImmutableArray(), allGlissandoLayouts.ToImmutableArray(),
            annotations,
            voiceOffsets,
            headWipes,
            dotForceDown,
            restShifts,
            partCombineLayouts);
        return FinalizeLayout(result, score.GrobOverrides, score.GrobReverts);
    }

    /// <summary>
    /// Lays out beams, ties, slurs and glissandos for every staff (each detected
    /// per voice, so a polyphonic staff exposes all its voices). Extracted verbatim
    /// from the multi-staff <c>Layout</c> body.
    /// </summary>
    private (List<BeamLayout> Beams, List<TieLayout> Ties, List<SlurLayout> Slurs, List<GlissandoLayout> Glissandos,
             ImmutableDictionary<RestShiftKey, double> RestShifts)
        LayoutAllSpanners(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        var allBeamLayouts = new List<BeamLayout>();
        var allTieLayouts = new List<TieLayout>();
        var allSlurLayouts = new List<SlurLayout>();
        var allGlissandoLayouts = new List<GlissandoLayout>();
        // Rest shifts are keyed by (measure, item) only, so they are computed for
        // each staff's PRIMARY voice — enough for the single-voice scores where
        // beamed rests occur in practice. A polyphonic beamed rest in a
        // non-primary voice, or the same (measure, item) slot across staves, would
        // share one entry; this matches the granularity the codebase already
        // accepts for HeadWipe/DotForceDown (VoiceItemKey, no staff axis).
        var restShiftsBuilder = ImmutableDictionary.CreateBuilder<RestShiftKey, double>();
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            // Beam detection breaks at tuplet boundaries by note index, so scope
            // the tuplets to THIS staff — a tuplet on another staff must not split
            // this staff's beams at a colliding index.
            var staffTuplets = StaffTuplets(score.TupletBrackets, staffIndex);
            var staffScore = new Score(
                staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                // Beam detection must see tuplet spans: auto beams break at
                // tuplet boundaries (BeamDetector).
                tupletBrackets: staffTuplets);
            // Beam AND slur/tie/glissando detection run PER VOICE, so a polyphonic
            // staff must expose all its voices (not just the primary) — else voice 2's
            // eighths never beam. Single-voice staves reuse the primary-voice score,
            // so their layout is unchanged.
            var staffSpannerScore = staff.Voices.Length > 1
                ? new Score(
                    staff.Voices, score.TimeSignature, score.KeySignature,
                    ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                    tupletBrackets: staffTuplets)
                : staffScore;
            var staffFinalBeams = _elementCoordinator.LayoutBeams(staffSpannerScore, systemsArray, staffIndex);
            allBeamLayouts.AddRange(staffFinalBeams);
            // Push beamed rests clear of their beam (Beam::rest_collision_callback).
            var staffRestShifts = _elementCoordinator.CalculateRestShifts(
                staffScore, systemsArray, staffFinalBeams.ToImmutableArray());
            foreach (var kv in staffRestShifts)
                if (!restShiftsBuilder.TryGetValue(kv.Key, out var existing)
                    || Math.Abs(kv.Value) > Math.Abs(existing))
                    restShiftsBuilder[kv.Key] = kv.Value;
            allTieLayouts.AddRange(_elementCoordinator.LayoutTies(staffSpannerScore, systemsArray, staffIndex, staff));
            allSlurLayouts.AddRange(_elementCoordinator.LayoutSlurs(staffSpannerScore, systemsArray, staffIndex, staff, score.GraceNotes, staffFinalBeams));
            allGlissandoLayouts.AddRange(_elementCoordinator.LayoutGlissandos(staffSpannerScore, systemsArray, staffIndex));
        }
        return (allBeamLayouts, allTieLayouts, allSlurLayouts, allGlissandoLayouts,
                restShiftsBuilder.ToImmutable());
    }

    // F3/S5-3a: route a system's measure layout through the session cache when one
    // is installed (single-staff incremental path). Null cache => direct compute,
    // byte-identical to the non-incremental path.
    private static ImmutableArray<MeasureLayout> ComputeSystemMeasures(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration,
        Func<ImmutableArray<MeasureLayout>> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeMeasures(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, compute);

    // F3/S5-3c: route a system's skyline through the session cache (the dominant
    // per-system cost, esp. multi-staff). Keyed additionally on systemHeight.
    private static (VerticalSkyline up, VerticalSkyline down) ComputeSystemSkyline(
        SystemLayoutCache? cache, int firstMeasureIndex, int measureCount, bool isFirstSystem,
        bool isLastSystem, double indent, double commonShortestDuration, double systemHeight,
        Func<(VerticalSkyline up, VerticalSkyline down)> compute)
        => cache == null
            ? compute()
            : cache.GetOrComputeSkyline(firstMeasureIndex, measureCount, isFirstSystem, isLastSystem,
                indent, commonShortestDuration, systemHeight, compute);

    private (ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems) CreatePages(
        ImmutableArray<SystemLayout> systems, double headerHeight,
        List<(double upExtent, double downExtent)> perSystemExtents, double systemHeight,
        List<(VerticalSkyline up, VerticalSkyline down)>? perSystemSkylines = null,
        List<double>? perSystemHeights = null,
        List<(double bandUp, double bandDown)>? perSystemBands = null)
    {
        // Whole-line annotation bands (lyric lines below, chord-symbol rows
        // above). They lay out only after the page Y is fixed, so they are
        // absent from the skylines — the skyline distance must be floored by
        // them or adjacent systems overprint them (found by the Greensleeves
        // sample). Local annotations (dynamics, ties, …) are NOT banded: the
        // X-aware skyline distance is the better model for those.
        (double up, double down) AnnBand(int i) =>
            perSystemBands == null || i >= perSystemBands.Count
                ? (0, 0)
                : (perSystemBands[i].bandUp, perSystemBands[i].bandDown);
        // Per-system body height, defaulting to the scalar systemHeight when the
        // caller has none (single-staff path, or no hara-kiri) — in that case every
        // entry equals systemHeight, so the result is byte-identical.
        double SysHeight(int i) =>
            perSystemHeights != null && i >= 0 && i < perSystemHeights.Count
                ? perSystemHeights[i]
                : systemHeight;
        // An empty score (no systems) has nothing to page; return empty rather than
        // indexing perSystemExtents[0] below.
        if (systems.IsDefaultOrEmpty || perSystemExtents.Count == 0)
            return (ImmutableArray<PageLayout>.Empty, ImmutableArray<SystemLayout>.Empty);

        // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline
        // Pass per-system skylines for X-dependent inter-system collision detection
        (ImmutableArray<PageLayout>, ImmutableArray<SystemLayout>) OptimalPages()
        {
            var skylines = perSystemSkylines != null
                ? (ImmutableArray<(VerticalSkyline, VerticalSkyline)>?)perSystemSkylines.ToImmutableArray()
                : null;
            var pages = _pageLayouter.CreatePagesWithOptimalBreaking(
                systems, headerHeight, perSystemExtents.ToImmutableArray(), skylines,
                perSystemBands?.ToImmutableArray(), perSystemHeights);
            return (pages, pages.SelectMany(p => p.Systems).ToImmutableArray());
        }

        if (_options.UseOptimalPageBreaking && _options.PageHeight > 0)
            return OptimalPages();

        // Recalculate Y positions using skyline extents to avoid overlaps
        double skylineY = LayoutUtilities.CalculateFirstSystemY(
            _options.MarginTop, headerHeight, perSystemExtents[0].upExtent,
            _options.StaffHeight / 2.0, _options.VerticalSpacing.TopSystem);
        var updatedSystems = new List<SystemLayout>();
        for (int i = 0; i < systems.Length; i++)
        {
            updatedSystems.Add(systems[i] with { Y = skylineY });
            if (i < systems.Length - 1)
            {
                // LILYPOND-REF: ly/paper-defaults-init.ly:62-65 system-system-spacing —
                // the pair's padding is 1, its minimum-distance 8 and its basic-distance
                // 12, and page-layout-problem.cc:625-632 uses exactly those. This path
                // used to invent `SystemSpacing * 0.5` (= 4) instead, four times
                // LilyPond's padding, which made the skyline term bind on scores where
                // LilyPond's does not — it was invisible while the skylines were thin,
                // and surfaced the moment the clef joined them.
                var pairSpec = _options.VerticalSpacing.SystemSystem;

                // Reference-to-reference distance to the next system. Prefer the
                // X-dependent skyline distance (the same measure the optimal page
                // path uses); the scalar sum below adds this system's deepest
                // downward protrusion to the next system's tallest upward one on
                // ANY X, so it spaces systems too far apart when those protrusions
                // do not actually overlap horizontally. Distance() is the true
                // per-X minimum clearance, so flooring it by systemHeight +
                // SystemSpacing (staff bodies never touch) and adding padding can
                // never introduce an overlap — only close a false gap.
                // Distance() returns -inf for an empty skyline; the scalar sum is
                // then kept, which is byte-identical to the previous behaviour.
                // LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline;
                //   lily/skyline.cc Skyline::distance.
                double skylineDistance = SysHeight(i)
                    + perSystemExtents[i].downExtent + perSystemExtents[i + 1].upExtent;
                if (perSystemSkylines != null && i + 1 < perSystemSkylines.Count)
                {
                    // LILYPOND-REF: lily/page-layout-problem.cc:618-629 — measured
                    // with the System grob's skyline-horizontal-padding (1.0).
                    double dist = perSystemSkylines[i + 1].up.Distance(
                        perSystemSkylines[i].down,
                        EngravingDefaults.SystemSkylineHorizontalPadding);
                    if (!double.IsNegativeInfinity(dist))
                    {
                        // A whole-line annotation band clears against the OTHER
                        // side's full extent (the band spans every X, so the
                        // X-disjoint argument for preferring Distance() does not
                        // apply to it).
                        var annPrev = AnnBand(i);
                        var annNext = AnnBand(i + 1);
                        if (annPrev.down > 0)
                            dist = Math.Max(dist, SysHeight(i) + annPrev.down
                                + perSystemExtents[i + 1].upExtent);
                        if (annNext.up > 0)
                            dist = Math.Max(dist, SysHeight(i)
                                + perSystemExtents[i].downExtent + annNext.up);
                        skylineDistance = dist;
                    }
                }

                // LILYPOND-REF: lily/page-layout-problem.cc:625-632 + spring.cc:219-237 —
                // the ink is a FLOOR under the spring, and at force 0 (which is what an
                // unjustified single page runs at) the spring is
                // max(min_distance, ideal_distance). Same shape as PageLayouter's chain.
                double minDistance = Math.Max(
                    pairSpec.MinimumDistance, skylineDistance + pairSpec.Padding);
                skylineY += Math.Max(pairSpec.BasicDistance, minDistance);
            }
        }
        double lastDownExtent = perSystemExtents[systems.Length - 1].downExtent;
        double totalHeight = skylineY + SysHeight(systems.Length - 1) + lastDownExtent + _options.MarginBottom;

        // Auto-pagination: a score that FITS one page keeps this simple layout
        // (byte-identical to the historical single-page output); one that
        // overflows the paper height re-runs through the optimal page breaker
        // and splits across real pages, like LilyPond always does.
        if (_options.PageHeight > 0 && totalHeight > _options.PageHeight)
            return OptimalPages();

        // Stage-4 W2-core: the loop above accumulated each system's top DOWNWARD
        // (device) to size the page; store the final origins as page Y-up (UP from
        // the page bottom) by reflecting through the now-known totalHeight. This is
        // the single-page producer seam — after it, SystemLayout.Y is Y-up and the
        // renderer's YFlip is the only device conversion left.
        var systemsArray = updatedSystems
            .Select(s => s with { Y = totalHeight - s.Y })
            .ToImmutableArray();
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
    private static (double downExtent, double upExtent, double bandDown, double bandUp) EstimateLooseLineExtents(
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        int startMeasure, int endMeasure,
        ImmutableArray<ChordNameItem> chordNames = default)
    {
        double downExtent = 0;
        double upExtent = 0;
        // Whole-line bands: annotation classes that span the system's full
        // width (lyric lines below, chord-symbol rows above). These floor the
        // inter-system skyline distance — see FloorDistance in CreatePages.
        double bandDown = 0;
        double bandUp = 0;

        // LILYPOND-REF: scm/define-grobs.scm ChordName (a TextScript-class grob
        // above the staff). Inline chord symbols (nameless `chords { }`) sit
        // above the staff: staffPadding(~1.4) + text height(~1.6).
        if (!chordNames.IsDefaultOrEmpty)
        {
            foreach (var cn in chordNames)
            {
                if (cn.MeasureIndex >= startMeasure && cn.MeasureIndex < endMeasure)
                {
                    upExtent = Math.Max(upExtent, 3.0);
                    bandUp = Math.Max(bandUp, 3.0);
                    break;
                }
            }
        }

        // --- Below-staff elements (downExtent) ---

        // LILYPOND-REF: scm/define-grobs.scm LyricText.outside-staff-priority = #(* 100 1)
        // Lyrics: staffPadding(2.5) + (verseCount-1) * verseSpacing(1.8) + fontSize(1.2)
        if (!lyrics.IsDefaultOrEmpty)
        {
            int maxVerse = 0;
            foreach (var lyric in lyrics)
            {
                // Independent lyrics-row syllables get their own text band; they must
                // not reserve phantom space under a music staff (that inflates the gap).
                if (lyric.IsLyricsRow)
                    continue;
                if (lyric.MeasureIndex >= startMeasure && lyric.MeasureIndex < endMeasure)
                    maxVerse = Math.Max(maxVerse, lyric.VerseNumber);
            }
            if (maxVerse > 0)
            {
                // Down-extent reserved for staff-attached lyrics. (These per-verse
                // constants are the annotation-band ESTIMATE and are distinct from
                // MultiStaffLayouter.TextRowVerseSpacing, which sizes independent
                // text rows — different purpose, so the values legitimately differ.)
                const double lyricStaffPadding = 2.5; // LyricText staff-padding
                const double lyricVerseSpacing = 1.8; // extra band per additional verse
                const double lyricFontSize = 1.2;     // lyric text height
                double lyricBand = lyricStaffPadding + (maxVerse - 1) * lyricVerseSpacing + lyricFontSize;
                downExtent = Math.Max(downExtent, lyricBand);
                bandDown = Math.Max(bandDown, lyricBand);
            }
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

        return (downExtent, upExtent, bandDown, bandUp);
    }

    /// <summary>
    /// Augments per-system extents with estimated loose line heights.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:138-173 pure_height
    /// Estimates both above-staff (upExtent) and below-staff (downExtent)
    /// contributions from annotations, so page breaking accounts for full system height.
    /// </remarks>

    /// <summary>
    /// Measures each system's REAL vertical protrusions from a preliminary
    /// annotation pass and max-merges them into the spacing extents. The
    /// provisional systems already carry final X geometry — only the page Y
    /// changes afterwards — so slurs, ties, tuplet brackets, marks and
    /// dynamics can be laid out, measured, and discarded; the final pass
    /// recomputes them against the re-spaced systems.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127
    /// build_system_skyline — page spacing reads COMPLETE system stencils
    /// (slurs, brackets, scripts included), not just note skylines.
    /// </remarks>
    private static void EnrichExtentsWithAnnotationProtrusions(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<SystemLayout> systems,
        AnnotationLayouts ann,
        ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs)
    {
        int n = Math.Min(perSystemExtents.Count, systems.Length);
        var up = new double[n];
        var down = new double[n];

        var measureToSystem = new Dictionary<int, int>();
        var bottoms = new double[n];
        for (int i = 0; i < n; i++)
        {
            foreach (var m in systems[i].Measures)
                measureToSystem[m.MeasureIndex] = i;
            // System bottom relative to its top: last visible staff's bottom
            // (4.0 for a single staff).
            bottoms[i] = 4.0;
            if (!systems[i].StaffGroups.IsDefaultOrEmpty)
            {
                foreach (var g in systems[i].StaffGroups)
                    foreach (var st in g.Staves)
                        if (!st.IsHidden)
                            bottoms[i] = Math.Max(bottoms[i], st.Height - st.Y);
            }
        }

        void Add(int measureIndex, double topRel, double bottomRel)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], bottomRel - bottoms[s]);
        }

        foreach (var t in ann.TupletBrackets)
        {
            // t.*YUp is Y-up from the system top; this pass is system-relative device.
            double startY = -t.StartYUp;
            double endY = -t.EndYUp;
            double hi = Math.Min(startY, endY);
            double lo = Math.Max(startY, endY);
            Add(t.MeasureIndex, hi - (t.IsStemUp ? 1.6 : 0.1), lo + (t.IsStemUp ? 0.7 : 1.7));
        }
        foreach (var v in ann.VoltaBrackets)
        {
            // YUp is Y-up from the system top; this extent pass is system-relative
            // device (down+), which is exactly -YUp.
            double vY = -v.YUp;
            Add(v.StartMeasureIndex, vY - 0.1, vY + 1.6);
        }
        foreach (var m in ann.MusicMarks)
        {
            if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                continue;
            // m.YUp is Y-up above the top-staff middle; system-relative device
            // (down+ from the system top) is 2 − YUp (the middle sits at device 2).
            double mY = 2.0 - m.YUp;
            Add(m.MeasureIndex, mY - 2.1, mY + 0.7);
        }
        foreach (var ct in ann.CustomTexts)
        {
            // ct.YUp is Y-up above the top-staff middle; system-relative device is 2 − YUp.
            double ctY = 2.0 - ct.YUp;
            Add(ct.MeasureIndex, ctY - 1.8, ctY + 0.6);
        }
        // Chord names ride above the staff and rise (ChordNameEngraver skyline) to
        // clear high notes; their REAL text top must join the system up-extent or a
        // lifted chord line pokes into the header/title. Chord font = FontSize*0.65
        // (≈2.6 ss), Middle-anchored, so the glyph top is cap-height/2 ≈ 0.9 above
        // the anchor and the descent ≈ 0.3 below.
        foreach (var cn in ann.ChordNames)
        {
            // cn.YUp is Y-up from the system top; the system-relative device Y (old
            // cn.Y) is its negation.
            double cnY = -cn.YUp;
            Add(cn.MeasureIndex, cnY - 1.9, cnY + 0.3);
        }
        // Lyric text (staff-bound AND row): the ascender rises ~2.11 ss above
        // the baseline at the 3.2 ss lyric font — without it, a first system
        // whose top content is a lyrics/chord ROW grazes the title ink.
        foreach (var lyLay in ann.Lyrics)
        {
            // lyLay.YUp is Y-up from the system top; the system-relative device
            // baseline (old lyLay.Y) is its negation.
            double lyY = -lyLay.YUp;
            Add(lyLay.Item.MeasureIndex, lyY - 2.11, lyY + 0.9);
        }
        foreach (var tr in ann.TrillSpanners)
        {
            // tr.YUp is Y-up from the system top; this pass is system-relative device.
            double trY = -tr.YUp;
            Add(tr.StartMeasureIndex, trY - GlyphMetrics.OrnTrillGlyph.Top, trY + 0.25);
        }
        // Figured-bass rows hang below the staff; a skyline-dropped row must
        // widen the gap to the NEXT system, or its digits print through that
        // system's volta boxes / high notes (showcase/04).
        foreach (var fb in ann.FiguredBasses)
        {
            // YUp is Y-up; this extent pass is system-relative device, so reconstruct
            // against this figure's own staff offset (0 for a single/top staff).
            double fbOff = measureToSystem.TryGetValue(fb.MeasureIndex, out int fbSys)
                ? LayoutUtilities.StaffOffsetInSystemDown(systems[fbSys], fb.StaffIndex)
                : 0;
            double fbY = fbOff + (2.0 - fb.YUp);
            Add(fb.MeasureIndex,
                fbY - FiguredBassEngraver.FigureTopExtent,
                fbY + (fb.FigureTexts.Length - 1) * FiguredBassEngraver.FigureSpacing + 0.5);
        }
        // Note-bound scripts (a fermata over the top staff, a staccatissimo
        // under the bottom) extend the system silhouette like any other
        // annotation; Ink is the glyph's real box about its anchor (Y-up).
        foreach (var a in ann.Articulations)
        {
            // a.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            double aY = 2.0 - a.YUp;
            Add(a.MeasureIndex, aY - a.Ink.Top, aY - a.Ink.Bottom);
        }
        foreach (var d in ann.Dynamics)
        {
            // d.YUp is Y-up above the staff middle; system-relative device is 2 − YUp.
            double dY = 2.0 - d.YUp;
            Add(d.MeasureIndex, dY - 1.2, dY + 0.3);
        }
        foreach (var h in ann.Hairpins)
        {
            // h.YUp is Y-up from the system top; this pass is system-relative device.
            double hY = -h.YUp;
            Add(h.StartMeasureIndex, hY - 0.34, hY + 0.34);
        }
        foreach (var sp in ann.TextSpanners)
        {
            // sp.YUp is Y-up from the system top; this pass is system-relative device.
            double spY = -sp.YUp;
            Add(sp.StartMeasureIndex, spY - 1.2, spY + 0.3);
        }
        foreach (var bn in ann.BarNumbers)
        {
            if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                continue;
            // bn.YUp is Y-up from the system top; the system-relative device value
            // (the old bn.Y - system.Y) is just -YUp.
            double rel = -bn.YUp;
            up[s] = Math.Max(up[s], -(rel - 1.3));
        }

        // Ties and slurs now store WITHIN-SYSTEM device Y (step 2d), so the negated
        // bow Y each caller passes is already system-relative — no system.Y subtraction.
        void AddCurve(int measureIndex, double y0, double y1, double c1, double c2)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            // Curve extreme ~ 3/4 of the way from endpoints to controls.
            double topRel = Math.Min(Math.Min(y0, y1), Math.Min(y0, y1) * 0.25 + Math.Min(c1, c2) * 0.75);
            double botRel = Math.Max(Math.Max(y0, y1), Math.Max(y0, y1) * 0.25 + Math.Max(c1, c2) * 0.75);
            up[s] = Math.Max(up[s], -topRel);
            down[s] = Math.Max(down[s], botRel - bottoms[s]);
        }

        foreach (var t in ties)
        {
            // A broken tie's continuation piece (IsBrokenLeft) lives on a LATER
            // system at that system's Y — attribute its extent to the system holding
            // its END, or its low Y leaks onto the start system and forces a huge
            // inter-system gap.
            int mi = t.IsBrokenLeft ? t.Tie.EndMeasureIndex : t.Tie.StartMeasureIndex;
            // Bow Y is now page Y-up (= -device); reflect back for this device extent pass.
            AddCurve(mi, -t.StartYUp, -t.EndYUp, -t.Control1.Y, -t.Control2.Y);
        }
        foreach (var sl in slurs)
        {
            int mi = sl.IsBrokenLeft ? sl.Slur.EndMeasureIndex : sl.Slur.StartMeasureIndex;
            AddCurve(mi, -sl.StartYUp, -sl.EndYUp, -sl.Control1.Y, -sl.Control2.Y);
        }

        for (int i = 0; i < n; i++)
        {
            var ext = perSystemExtents[i];
            perSystemExtents[i] = (
                Math.Max(ext.upExtent, up[i]),
                Math.Max(ext.downExtent, down[i]));
        }
    }

    /// <summary>
    /// Returns per-system skylines with the scripts' ink merged in — above
    /// scripts into the UP skyline, below scripts into the DOWN skyline. The
    /// input skylines are NOT mutated; non-augmented systems reuse the
    /// originals. LilyPond's axis-group skyline contains the note-bound
    /// scripts, so anything spaced against it (the chord-name line above, the
    /// figured-bass row below) clears a staccato or fermata over a protruding
    /// note; these system skylines are built before script layout exists,
    /// hence this second pass. Inner-staff scripts merge harmlessly (the
    /// system silhouette already dominates them). The ink transform mirrors
    /// OutsideStaffStacker's script seeding; script Y is system-local
    /// (staff offset already applied).
    /// LILYPOND-REF: lily/axis-group-interface.cc:359-474 — grobs without
    /// outside-staff-priority stay in the support skyline.
    /// </summary>
    private static IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesWithScripts(
        IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? systemSkylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<SystemLayout> systems)
    {
        if (systemSkylines == null || articulations.IsDefaultOrEmpty)
            return systemSkylines;

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < systemSkylines.Count; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        var augmented = systemSkylines.ToArray();
        foreach (var a in articulations)
        {
            if (!measureToSystem.TryGetValue(a.MeasureIndex, out int sysIdx))
                continue;
            // ArticulationLayout.YUp is Y-up (staff-spaces above the staff middle);
            // this skyline is Y-up too (system-top origin). Translate against this
            // staff's system-local middle, and take the offset in the SAME frame so the
            // whole line adds — the middle sits half a staff BELOW the staff top, which
            // in Y-up subtracts. Ink Top/Bottom stay up-positive, so they ADD.
            // This is now the same expression as OutsideStaffStacker's articulation
            // branch; the two used to be one Y-up and one Y-down spelling of it.
            var sys = systems[sysIdx];
            double staffMidUp = LayoutUtilities.StaffOffsetInSystemUp(sys, a.StaffIndex) - 2.0;
            double aY = a.YUp + staffMidUp;
            double inkTop = aY + a.Ink.Top;
            double inkBottom = aY + a.Ink.Bottom;
            if (a.IsAbove)
            {
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(augmented[sysIdx].up);
                up.Merge(VerticalSkyline.FromBox(
                    a.X + a.Ink.Left, a.X + a.Ink.Right,
                    inkBottom, inkTop, VerticalDirection.Up));
                augmented[sysIdx] = (up, augmented[sysIdx].down);
            }
            else
            {
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(augmented[sysIdx].down);
                down.Merge(VerticalSkyline.FromBox(
                    a.X + a.Ink.Left, a.X + a.Ink.Right,
                    inkBottom, inkTop, VerticalDirection.Down));
                augmented[sysIdx] = (augmented[sysIdx].up, down);
            }
        }
        return augmented;
    }

    /// <summary>
    /// Paging skylines: the per-system skylines plus the annotation ink that
    /// hangs outside the staves — note-bound scripts (both directions) and
    /// figured-bass rows. The optimal page-stacking path spaces systems by
    /// skyline DISTANCE (PageLayouter), so anything missing from these
    /// silhouettes can print into the neighbouring system (showcase/04:
    /// figured-bass digits through the next system's volta boxes).
    /// LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline — LP's
    /// paging skylines contain every grob of the system.
    /// </summary>
    private static List<(VerticalSkyline up, VerticalSkyline down)>? AugmentSkylinesForPaging(
        List<(VerticalSkyline up, VerticalSkyline down)>? skylines,
        ImmutableArray<ArticulationLayout> articulations,
        ImmutableArray<FiguredBassLayout> figuredBasses,
        ImmutableArray<VoltaBracketLayout> voltaBrackets,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MusicMarkLayout> musicMarks = default,
        ImmutableArray<CustomTextLayout> customTexts = default,
        ImmutableArray<ChordNameLayout> chordNames = default,
        ImmutableArray<BarNumberLayout> barNumbers = default,
        ImmutableArray<TupletBracketLayout> tupletBrackets = default)
    {
        if (skylines == null)
            return null;
        var result = AugmentSkylinesWithScripts(skylines, articulations, systems)!.ToList();

        var measureToSystem = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length && s < result.Count; s++)
            foreach (var m in systems[s].Measures)
                measureToSystem[m.MeasureIndex] = s;

        // A tuplet bracket is ordinary ink inside its staff's axis group in LilyPond, so
        // the next system has to clear it exactly as it clears the notes. This skyline is
        // the one the PAGE spaces systems by; MultiStaffLayouter seeds the other one, the
        // per-staff skyline Align_interface reads, and seeding only that left the bracket
        // reserved between staves and not between systems.
        // (EnrichExtentsWithAnnotationProtrusions does add tuplets to the scalar extents,
        // but those are only the fallback for an EMPTY skyline — see CreatePages — so they
        // never decide anything here.)
        // LILYPOND-REF: scm/define-grobs.scm TupletBracket carries vertical-skylines from
        //   its stencil and sets no outside-staff-priority, so axis-group-interface keeps
        //   it inside; lily/page-layout-problem.cc:1070-1127 build_system_skyline spaces
        //   pages by the COMPLETE system stencil.
        if (!tupletBrackets.IsDefaultOrEmpty)
        {
            foreach (var group in tupletBrackets.GroupBy(t => t.MeasureIndex))
            {
                if (!measureToSystem.TryGetValue(group.Key, out int s))
                    continue;
                // *YUp here IS the system frame (the annotation pass baked the staff
                // offset in through staffYAt), which is this skyline's own frame.
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                var down = new VerticalSkyline(VerticalDirection.Down);
                down.Merge(result[s].down);
                SkylineBuilder.AddTupletBracketsToSkyline(group.ToImmutableArray(), up, down);
                result[s] = (up, down);
            }
        }

        foreach (var fb in figuredBasses)
        {
            if (!measureToSystem.TryGetValue(fb.MeasureIndex, out int s))
                continue;
            double half = FiguredBassEngraver.MinFigureBoxWidth;
            // YUp is Y-up; this inter-system skyline is Y-up too (system-top origin), so
            // take the figure's own staff offset in that frame as well and the line adds.
            // The staff middle is half a staff below the staff top, hence the -2.0; the
            // figure column then extends downward (smaller Y-up).
            double fbStaffOffsetUp = LayoutUtilities.StaffOffsetInSystemUp(systems[s], fb.StaffIndex);
            double fbY = fb.YUp - 2.0 + fbStaffOffsetUp;
            double top = fbY + FiguredBassEngraver.FigureTopExtent;
            double bottom = fbY
                - ((fb.FigureTexts.Length - 1) * FiguredBassEngraver.FigureSpacing + 0.5);
            var down = new VerticalSkyline(VerticalDirection.Down);
            down.Merge(result[s].down);
            down.Merge(VerticalSkyline.FromBox(
                fb.X - half, fb.X + half, bottom, top, VerticalDirection.Down));
            result[s] = (result[s].up, down);
        }

        // Volta brackets and their "End1"-style label boxes rise above the
        // staff: without them in the UP silhouette, a previous system's
        // figured-bass digits settle onto the boxes.
        foreach (var v in voltaBrackets)
        {
            if (!measureToSystem.TryGetValue(v.StartMeasureIndex, out int s))
                continue;
            // YUp is Y-up from the system top; this skyline is Y-up too, so use it directly.
            double vY = v.YUp;
            var up = new VerticalSkyline(VerticalDirection.Up);
            up.Merge(result[s].up);
            up.Merge(VerticalSkyline.FromBox(
                v.StartX, v.EndX, vY - 1.6, vY + 0.1, VerticalDirection.Up));
            result[s] = (result[s].up, result[s].down);
            result[s] = (up, result[s].down);
        }

        // Section labels, rehearsal marks and navigation text (Fine, D.C. …)
        // stack above (or below) the staff like any other annotation. Without
        // their boxes in the silhouette, the X-aware inter-system distance let
        // a label above system 2 print through system 1's figured bass. The
        // box is added to BOTH sides — merging on the side the mark does not
        // protrude toward is a no-op, so no direction bookkeeping is needed.
        void AddMarkBox(int measureIndex, double x0, double x1, double top, double bottom)
        {
            if (!measureToSystem.TryGetValue(measureIndex, out int s))
                return;
            var up = new VerticalSkyline(VerticalDirection.Up);
            up.Merge(result[s].up);
            up.Merge(VerticalSkyline.FromBox(x0, x1, bottom, top, VerticalDirection.Up));
            var down = new VerticalSkyline(VerticalDirection.Down);
            down.Merge(result[s].down);
            down.Merge(VerticalSkyline.FromBox(x0, x1, bottom, top, VerticalDirection.Down));
            result[s] = (up, down);
        }
        if (!musicMarks.IsDefaultOrEmpty)
        {
            foreach (var m in musicMarks)
            {
                if (MusicMarkItem.IsSpannerHandled(m.MarkType))
                    continue;
                // Same vertical envelope Enrich uses; width from the real text
                // (boxed labels get the box padding, symbols a 2 ss square).
                double halfW = m.IsSymbol
                    ? 1.0
                    : Rendering.TextFontMetrics.SerifBold(m.Text, 2.4) / 2 + 0.4;
                // YUp is Y-up; the skyline is Y-up too. Translate to the top staff's frame.
                double mY = m.YUp - 2.0;
                AddMarkBox(m.MeasureIndex, m.X - halfW, m.X + halfW, mY + 2.1, mY - 0.7);
            }
        }
        if (!customTexts.IsDefaultOrEmpty)
        {
            foreach (var ct in customTexts)
            {
                double halfW = Rendering.TextFontMetrics.SerifBold(ct.Text, 2.0) / 2 + 0.2;
                // YUp is Y-up; the skyline is Y-up too. Translate to the top staff's frame.
                double ctY = ct.YUp - 2.0;
                AddMarkBox(ct.MeasureIndex, ct.X - halfW, ct.X + halfW, ctY + 1.8, ctY - 0.6);
            }
        }
        // Inline chord symbols: their scalar height joins the up-extents, but
        // the X-aware inter-system Distance() never saw them — on a ragged
        // (natural-gap) page a below-staff jump text ("D.S. al Coda") printed
        // straight onto the next system's chord letters. Same envelope the
        // scalar extents use (cap ascent 1.9, descent 0.3).
        if (!chordNames.IsDefaultOrEmpty)
        {
            foreach (var cn in chordNames)
            {
                double halfW = Rendering.TextFontMetrics.SansBold(cn.ChordText, 2.6) / 2 + 0.3;
                double cnY = cn.YUp; // cn.YUp is Y-up from the system top (skyline frame)
                AddMarkBox(cn.MeasureIndex, cn.X - halfW, cn.X + halfW, cnY + 1.9, cnY - 0.3);
            }
        }
        // Line-start bar numbers sit in the band above the staff start where
        // only the staff-symbol roof exists; without their ink in the UP
        // silhouette, Distance() lets the previous system's staff lines crowd
        // the number (their scalar up-extent is overridden by the X-aware
        // distance). Same cap envelope Enrich uses (top = baseline − 1.3).
        // LILYPOND-REF: lily/page-layout-problem.cc build_system_skyline —
        // the system skyline contains the BarNumber grob.
        if (!barNumbers.IsDefaultOrEmpty)
        {
            foreach (var bn in barNumbers)
            {
                if (!measureToSystem.TryGetValue(bn.MeasureIndex, out int s))
                    continue;
                // bn.YUp is Y-up from the system top; the skyline is Y-up too.
                double rel = bn.YUp;
                double w = Rendering.TextFontMetrics.SerifBold(
                    bn.Text, BarNumberEngraver.FontSize);
                double x0 = bn.RightAligned ? bn.X - w : bn.X;
                var up = new VerticalSkyline(VerticalDirection.Up);
                up.Merge(result[s].up);
                up.Merge(VerticalSkyline.FromBox(
                    x0, x0 + w, rel, rel + 1.3, VerticalDirection.Up));
                result[s] = (up, result[s].down);
            }
        }
        return result;
    }

    private static void AugmentExtentsWithLooseLines(
        List<(double upExtent, double downExtent)> perSystemExtents,
        ImmutableArray<LyricItem> lyrics,
        ImmutableArray<DynamicItem> dynamics,
        ImmutableArray<FiguredBassItem> figuredBasses,
        ImmutableArray<MusicMarkItem> musicMarks,
        ImmutableArray<VoltaBracketItem> voltaBrackets,
        List<(int startMeasure, int measureCount)> systemMeasureRanges,
        ImmutableArray<ChordNameItem> chordNames = default,
        List<(double bandUp, double bandDown)>? perSystemBands = null)
    {
        for (int i = 0; i < perSystemExtents.Count && i < systemMeasureRanges.Count; i++)
        {
            var (start, count) = systemMeasureRanges[i];
            var (looseDown, looseUp, bandDown, bandUp) = EstimateLooseLineExtents(
                lyrics, dynamics, figuredBasses, musicMarks, voltaBrackets,
                start, start + count, chordNames);
            perSystemBands?.Add((bandUp, bandDown));

            var ext = perSystemExtents[i];
            if (looseDown > 0 || looseUp > 0)
            {
                perSystemExtents[i] = (
                    Math.Max(ext.upExtent, looseUp),
                    Math.Max(ext.downExtent, looseDown));
            }
        }
    }

    /// <summary>
    /// Inputs to <see cref="CalculateAnnotationLayouts"/>. Collapses the former
    /// 21-parameter signature into one context object; the optional members default
    /// to null/empty, matching the old optional parameters exactly.
    /// </summary>
    private sealed class AnnotationLayoutContext
    {
        public required Score? Score { get; init; }
        public required ImmutableArray<SystemLayout> Systems { get; init; }
        public required ImmutableArray<DynamicItem> Dynamics { get; init; }
        public required ImmutableArray<ArticulationItem> Articulations { get; init; }
        public required ImmutableArray<GraceNoteItem> GraceNotes { get; init; }
        public required ImmutableArray<LyricItem> Lyrics { get; init; }
        public required ImmutableArray<MusicMarkItem> MusicMarks { get; init; }
        public required ImmutableArray<CustomTextItem> CustomTexts { get; init; }
        public required ImmutableArray<VoltaBracketItem> VoltaBrackets { get; init; }
        public required ImmutableArray<TupletBracketItem> TupletBrackets { get; init; }
        public required ImmutableArray<ArpeggioItem> Arpeggios { get; init; }
        public required ImmutableArray<Measure> Measures { get; init; }
        public ImmutableArray<FiguredBassItem>? FiguredBasses { get; init; }
        public ImmutableArray<ChordNameItem>? ChordNames { get; init; }
        public ImmutableArray<PercentRepeatItem>? PercentRepeats { get; init; }
        public ImmutableArray<CrossStaffLayout>? CrossStaffLayouts { get; init; }
        public ImmutableArray<TrillSpannerItem>? TrillSpanners { get; init; }
        public ImmutableArray<BeamGroup>? BeamGroups { get; init; }
        public ImmutableArray<BeamLayout>? BeamLayouts { get; init; }
        public IReadOnlyList<(VerticalSkyline up, VerticalSkyline down)>? SystemSkylines { get; init; }
        public bool TupletForceStemUp { get; init; }
        public ImmutableArray<Voice> StaffVoices { get; init; }
        public Dictionary<int, ImmutableArray<Voice>>? VoicesByStaff { get; init; }
        public Dictionary<int, ImmutableArray<Measure>>? MeasuresByStaff { get; init; }
        public Dictionary<int, double>? StaffYByIndex { get; init; }
        public Dictionary<int, double>? NoteBoundAnchorY { get; init; }
        public Dictionary<int, Staff>? StaffByIndex { get; init; }
    }

    private AnnotationLayouts CalculateAnnotationLayouts(AnnotationLayoutContext ctx)
    {
        var score = ctx.Score;
        var systems = ctx.Systems;
        var dynamics = ctx.Dynamics;
        var articulations = ctx.Articulations;
        var graceNotes = ctx.GraceNotes;
        var lyrics = ctx.Lyrics;
        var musicMarks = ctx.MusicMarks;
        var customTexts = ctx.CustomTexts;
        var voltaBrackets = ctx.VoltaBrackets;
        var tupletBrackets = ctx.TupletBrackets;
        var arpeggios = ctx.Arpeggios;
        var measures = ctx.Measures;
        var figuredBasses = ctx.FiguredBasses;
        var chordNames = ctx.ChordNames;
        var percentRepeats = ctx.PercentRepeats;
        var crossStaffLayouts = ctx.CrossStaffLayouts;
        var trillSpanners = ctx.TrillSpanners;
        var beamGroups = ctx.BeamGroups;
        var beamLayouts = ctx.BeamLayouts;
        var systemSkylines = ctx.SystemSkylines;
        var tupletForceStemUp = ctx.TupletForceStemUp;
        var staffVoices = ctx.StaffVoices;
        var voicesByStaff = ctx.VoicesByStaff;
        var measuresByStaff = ctx.MeasuresByStaff;
        var staffYByIndex = ctx.StaffYByIndex;
        var staffByIndex = ctx.StaffByIndex;

        var ml = systems.SelectMany(s => s.Measures).ToImmutableArray();

        // Per-system staff-Y resolver. A staff's within-system offset can differ
        // between systems under hara-kiri (a hidden upper staff shifts the staves
        // below it up), so a per-staff annotation must use the offset for the system
        // its OWN measure falls in, not a single global value. Built from each
        // system's StaffGroups only when staffYByIndex is supplied (the multi-staff
        // final pass); staffYAt stays null for the offset-free prelim/single-staff
        // passes, preserving their exact behavior. Without hara-kiri every system has
        // the same staff Y, so staffYAt collapses to the old staffYByIndex lookup and
        // the result is byte-identical.
        Func<int, int, double>? staffYAt = null;
        Func<int, double>? minStaffYAt = null;
        if (staffYByIndex != null)
        {
            var measureToSystem = new Dictionary<int, int>();
            var staffYBySystem = new List<Dictionary<int, double>>(systems.Length);
            for (int s = 0; s < systems.Length; s++)
            {
                var map = new Dictionary<int, double>();
                if (!systems[s].StaffGroups.IsDefaultOrEmpty)
                    foreach (var sg in systems[s].StaffGroups)
                        foreach (var st in sg.Staves)
                            map[st.StaffIndex] = -st.Y;   // Y-up storage → device-down offset
                staffYBySystem.Add(map);
                foreach (var m in systems[s].Measures)
                    measureToSystem[m.MeasureIndex] = s;
            }
            int SysOf(int measureIndex) =>
                staffYBySystem.Count == 0 ? 0
                : measureToSystem.TryGetValue(measureIndex, out var s) ? s : 0;
            staffYAt = (measureIndex, staffIndex) =>
                staffYBySystem.Count > 0
                && staffYBySystem[SysOf(measureIndex)].TryGetValue(staffIndex, out var y) ? y : 0;
            minStaffYAt = measureIndex =>
            {
                if (staffYBySystem.Count == 0) return 0;
                var map = staffYBySystem[SysOf(measureIndex)];
                return map.Count > 0 ? map.Values.Min() : 0;
            };
        }

        // Scripts are laid out BEFORE the lyric/figured-bass rows: a note-bound
        // script (marcato/staccato/tenuto/…) carries no outside-staff-priority, so it
        // stays in the staff's support skyline that those rows drop below. Without the
        // augmented DOWN skyline a marcato hanging under a low note overprints the
        // syllable beneath it. LILYPOND-REF: lily/axis-group-interface.cc:359-474.
        var articulationLayouts = score != null
            ? ArticulationEngraver.Calculate(score, articulations, ml, measuresByStaff, staffYAt, staffByIndex,
                beamLayouts ?? default)
            : ImmutableArray<ArticulationLayout>.Empty;
        var scriptedSkylines = AugmentSkylinesWithScripts(systemSkylines, articulationLayouts, systems);

        // Per-(system, staff) DOWN-skyline for a note-bound lyric line that sits under an
        // UPPER staff, so it clears THAT staff's own notes and real (font-metric) glyph
        // height instead of the whole system's lowest staff. Mirrors lowerStaffUpSkyline
        // (chord names on a non-top staff); built lazily and only when such a line
        // exists, so single-/bottom-staff lyrics do no extra work and stay byte-identical.
        Func<int, int, VerticalSkyline?>? noteBoundStaffDownSkyline = null;
        var nbAnchor = ctx.NoteBoundAnchorY;
        if (nbAnchor is { Count: > 0 } && staffByIndex != null
            && lyrics.Any(l => !l.IsLyricsRow && nbAnchor.ContainsKey(l.StaffIndex)))
        {
            var downCache = new Dictionary<(int, int), VerticalSkyline?>();
            noteBoundStaffDownSkyline = (sysIdx, staffIndex) =>
            {
                if (sysIdx < 0 || sysIdx >= systems.Length
                    || !staffByIndex.TryGetValue(staffIndex, out var staff))
                    return null;
                var key = (sysIdx, staffIndex);
                if (!downCache.TryGetValue(key, out var sky))
                {
                    sky = _skylineBuilder.BuildStaffSkylines(staff, systems[sysIdx].Measures).Down;
                    downCache[key] = sky;
                }
                return sky;
            };
        }

        var lyricLayouts = new LyricEngraver().CalculateLayouts(
            lyrics, ml, _options.StaffHeight, systems, scriptedSkylines, staffYByIndex,
            ctx.NoteBoundAnchorY, noteBoundStaffDownSkyline);

        // LILYPOND-REF: axis-group-interface.cc skyline_spacing
        // Outside-staff elements are placed in priority order (lower priority = closer to staff).
        // DynamicLineSpanner (250) must be calculated before TextSpanner (350)
        // so text spanners can be placed below dynamics.

        // Dynamics first (outside-staff-priority: 250)
        var dynamicLayouts = score != null ? DynamicEngraver.Calculate(score, dynamics, ml, staffVoices, voicesByStaff, measuresByStaff) : ImmutableArray<DynamicLayout>.Empty;

        // Detect and layout hairpins from cresc/decresc marks
        var hairpinItems = HairpinEngraver.DetectHairpins(musicMarks, dynamics);
        var hairpinLayouts = HairpinEngraver.Calculate(hairpinItems, systems, ml, staffYAt);

        // Detect and layout text spanners from rit/accel marks (outside-staff-priority: 350)
        // Pass dynamic layouts so text spanners can stack below them
        var textSpannerItems = TextSpannerEngraver.DetectTextSpanners(musicMarks);
        var textSpannerLayouts = TextSpannerEngraver.Calculate(textSpannerItems, systems, ml, dynamicLayouts, staffYAt);

        // Detect and layout ottava brackets from ottava/loco marks
        var ottavaItems = OttavaBracketEngraver.DetectOttavaBrackets(musicMarks);
        var ottavaLayouts = OttavaBracketEngraver.Calculate(ottavaItems, systems, ml, staffYAt);

        // Layout arpeggio markings
        var arpeggioLayouts = ArpeggioEngraver.Calculate(arpeggios, systems, measures, measuresByStaff);

        // Piano pedal marks render per the part's `pedal` style (Staff.PedalStyle):
        //   bracket (Lily# default) / mixed  -> a spanning bracket, and the "Ped." /
        //                                        "*" text is suppressed (mixed keeps
        //                                        the leading "Ped." only);
        //   text                             -> keep "Ped." / "*", no bracket.
        // LILYPOND-REF: lily/piano-pedal-engraver.cc — pedalSustainStyle.
        PedalStyle StaffPedalStyle(int staffIndex) =>
            staffByIndex != null && staffByIndex.TryGetValue(staffIndex, out var st)
                ? st.PedalStyle : PedalStyle.Text; // no staff info -> plain text
        var pedalBracketBuilder = ImmutableArray.CreateBuilder<PedalBracketLayout>();
        if (!musicMarks.IsDefaultOrEmpty && staffByIndex != null)
        {
            foreach (var staffIndex in musicMarks
                .Where(m => IsPedalMark(m.Type)).Select(m => m.StaffIndex).Distinct())
            {
                var style = StaffPedalStyle(staffIndex);
                if (style == PedalStyle.Text)
                    continue;
                var staffMarks = musicMarks.Where(m => m.StaffIndex == staffIndex).ToImmutableArray();
                var brackets = PedalEngraver.DetectPedalBrackets(staffMarks);
                pedalBracketBuilder.AddRange(
                    PedalEngraver.Calculate(brackets, systems, ml, isMixed: style == PedalStyle.Mixed));
            }
        }
        var pedalBracketLayouts = pedalBracketBuilder.ToImmutable();
        // A bracket/mixed style suppresses the "Ped." / "*" text a mark would draw.
        // The predicate is applied when the mark LAYOUT is built (below), so the raw
        // mark list — and every mark's SourceIndex into it — stays intact for the
        // incremental-reuse data-pos path (SharedRenderer.ResolveDataPos).
        Func<MusicMarkItem, bool> keepMarkText = m => KeepPedalTextMark(m, StaffPedalStyle(m.StaffIndex));

        // Layout figured bass (drops below below-staff scripts via the
        // script-augmented DOWN skylines)
        var figuredBassLayouts = FiguredBassEngraver.Calculate(
            figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty, systems, ml, measures,
            measuresByStaff, scriptedSkylines);

        // Layout chord names (skyline-spaced above high notes when skylines available).
        // A chords-ONLY sheet (chord rows, no lyric rows) is a measure grid: its
        // symbols centre between the full-height grid barlines.
        bool chordGridSheet =
            (chordNames?.Any(c => c.IsChordRow) ?? false)
            && !lyrics.Any(l => l.IsLyricsRow);

        // Chord names on a NON-top staff (`staff bass with chords ...`) must clear
        // that staff's own high/ledger notes, but the system up-skyline carries only
        // the topmost staff. Provide a per-(system, staff) up-skyline for the staves
        // that actually host a chord row below the top; built lazily and only when
        // such a row exists, so the common lead sheet (chords on the top staff only)
        // does no extra work and is byte-identical.
        Func<int, int, VerticalSkyline?>? lowerStaffUpSkyline = null;
        var cn = chordNames ?? ImmutableArray<ChordNameItem>.Empty;
        if (!cn.IsDefaultOrEmpty && staffByIndex != null && staffYByIndex != null
            && staffYByIndex.Count > 0)
        {
            double topStaffY = staffYByIndex.Values.Min();
            bool anyLowerStaffChords = cn.Any(c => !c.IsChordRow
                && staffYByIndex.TryGetValue(c.StaffIndex, out var sy) && sy > topStaffY + 1e-6);
            if (anyLowerStaffChords)
            {
                var skyCache = new Dictionary<(int, int), VerticalSkyline?>();
                lowerStaffUpSkyline = (sysIdx, staffIndex) =>
                {
                    if (sysIdx < 0 || sysIdx >= systems.Length
                        || !staffByIndex.TryGetValue(staffIndex, out var staff))
                        return null;
                    var key = (sysIdx, staffIndex);
                    if (!skyCache.TryGetValue(key, out var sky))
                    {
                        sky = _skylineBuilder.BuildStaffSkylines(staff, systems[sysIdx].Measures).Up;
                        skyCache[key] = sky;
                    }
                    return sky;
                };
            }
        }

        var chordNameLayouts = ChordNameEngraver.Calculate(
            cn, systems, ml, measures,
            measuresByStaff, staffYAt, minStaffYAt, scriptedSkylines,
            chordGridSheet: chordGridSheet, lowerStaffUpSkyline: lowerStaffUpSkyline);

        // Layout percent repeats
        var percentRepeatLayouts = PercentRepeatEngraver.Calculate(
            percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty, systems, ml);

        // Layout trill spanners (tr + wavy line)
        // LILYPOND-REF: scm/scheme-engravers.scm — trill spanner positioning
        var trillSpannerLayouts = TrillSpannerEngraver.Calculate(
            trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty, systems, ml, staffYAt);

        // Calculate volta brackets first — needed by MusicMarkEngraver for collision avoidance
        // LILYPOND-REF: axis-group-interface.cc — elements sorted by outside-staff-priority
        var voltaBracketLayouts = VoltaBracketEngraver.Calculate(voltaBrackets, systems, ml);

        // LILYPOND-REF: lily/axis-group-interface.cc:359-474 outside_staff_axis_group
        // Post-process below-staff elements using priority-based stacking.
        // This ensures hairpins avoid dynamics (both priority 250) and
        // text spanners avoid both dynamics and hairpins (priority 350).
        var (stackedDynamics, stackedHairpins) =
            OutsideStaffStacker.StackBelowStaff(systems, dynamicLayouts, hairpinLayouts,
                articulationLayouts, applyStaffOffsets: staffYAt != null);

        // ABOVE-staff: one unified priority pass (trill 50, bar number 100,
        // tuplet brackets 200 as immovable seeds, ottava 400, text 450,
        // volta 600, marks 1500), seeded from the per-system up-skylines.
        // Replaces the old pairwise hacks (bar-number-vs-volta in the
        // renderer; music-mark-vs-volta inside MusicMarkEngraver).
        var tupletBracketLayouts = TupletBracketEngraver.Calculate(
            tupletBrackets, ml, measures, beamGroups ?? default, beamLayouts ?? default,
            forceStemUp: tupletForceStemUp,
            measuresByStaff: measuresByStaff, voicesByStaff: voicesByStaff, staffYAt: staffYAt,
            staffByIndex: staffByIndex);
        var musicMarkLayouts = MusicMarkEngraver.Calculate(
            score, musicMarks, systems, ml, measures, default,
            chordNames: chordNameLayouts, lyrics: lyricLayouts, keepMarkText: keepMarkText);
        var customTextLayouts = CustomTextEngraver.Calculate(customTexts, ml);
        // A leading \partial pickup is bar 0: shift displayed numbers down by one
        // so the first FULL measure is numbered 1, not 2.
        int barNumberOffset = (!measures.IsDefaultOrEmpty && measures[0].IsPickup) ? -1 : 0;
        var barNumberLayouts = BarNumberEngraver.Calculate(systems, numberOffset: barNumberOffset);
        // Forced-above dynamics (@f.up) join the above-staff pass so they clear, and are
        // cleared by, the other above-staff grobs. Below dynamics were already placed by
        // StackBelowStaff and pass through untouched.
        var (stackedTrills, stackedBarNumbers, stackedOttavas, stackedCustomTexts,
             stackedVoltas, stackedMarks, stackedDynamicsAbove, stackedTextSpanners) = OutsideStaffStacker.StackAboveStaff(
            systems, systemSkylines, tupletBracketLayouts,
            trillSpannerLayouts, barNumberLayouts, ottavaLayouts,
            customTextLayouts, voltaBracketLayouts, musicMarkLayouts,
            articulationLayouts, aboveDynamics: stackedDynamics, textSpanners: textSpannerLayouts);
        stackedDynamics = stackedDynamicsAbove;
        // After stacking, sit a boundary "To Coda" on the adjacent section label's
        // line (the two straddle one barline) instead of stacking them apart.
        stackedMarks = MusicMarkEngraver.CoPlaceToCodaWithLabels(stackedMarks);
        // Likewise a tempo mark joins its section label's line ("[Chorus] ♩ = 132").
        stackedMarks = MusicMarkEngraver.CoPlaceTempoWithLabels(stackedMarks, chordNameLayouts, systems);

        // Fingerings live on the NoteItem, so they must be read from EACH staff's
        // own voice (score.Voice is only the first staff) and positioned at that
        // staff's index — otherwise lower-staff fingerings vanish.
        ImmutableArray<FingeringLayout> fingeringLayouts;
        if (score == null)
            fingeringLayouts = ImmutableArray<FingeringLayout>.Empty;
        else if (voicesByStaff != null && voicesByStaff.Count > 0)
        {
            var fb = ImmutableArray.CreateBuilder<FingeringLayout>();
            foreach (var kv in voicesByStaff)
            {
                if (kv.Value.IsDefaultOrEmpty)
                    continue;
                var staffScore = new Score(kv.Value[0], score.TimeSignature,
                    score.KeySignature, score.Clef, score.Tempo);
                fb.AddRange(FingeringEngraver.Calculate(staffScore, systems, kv.Key));
            }
            fingeringLayouts = fb.ToImmutable();
        }
        else
            fingeringLayouts = FingeringEngraver.Calculate(score, systems);

        // Push each fingering OUTSIDE any articulation sharing the note & side, so
        // they don't overprint. LilyPond keeps the articulation (Script) close to
        // the note and places the fingering on the OUTSIDE -- verified against
        // LilyPond 2.24.4: a fingering and a marcato forced above the same note
        // render marcato inner, fingering outer (the digit sits above the marcato).
        // So the fingering, not the articulation, is the one that moves.
        // LILYPOND-REF: lily/new-fingering-engraver.cc; empirical stacking order.
        if (!fingeringLayouts.IsDefaultOrEmpty && !articulationLayouts.IsDefaultOrEmpty)
        {
            // Gap from the outermost articulation's anchor to the fingering baseline.
            // Below needs more: the fingering baseline is the digit's lower edge.
            const double aboveGap = 1.4;
            const double belowGap = 1.9;
            // Outermost articulation anchor per note & side (min Y above / max Y below).
            // StaffIndex is -1 for single-staff fingerings and 0 for their articulations,
            // so normalise a negative staff to 0 on both sides before matching.
            // Outermost articulation anchor per note & side, in Y-up: "outermost"
            // is the MOST above (max YUp) or MOST below (min YUp).
            var artOuter = new Dictionary<(int, int, int, bool), double>();
            foreach (var a in articulationLayouts)
            {
                var key = (a.StaffIndex < 0 ? 0 : a.StaffIndex, a.MeasureIndex, a.ItemIndex, a.IsAbove);
                artOuter[key] = artOuter.TryGetValue(key, out var cur)
                    ? (a.IsAbove ? System.Math.Max(cur, a.YUp) : System.Math.Min(cur, a.YUp))
                    : a.YUp;
            }
            var fb2 = fingeringLayouts.ToBuilder();
            for (int i = 0; i < fb2.Count; i++)
            {
                var fg = fb2[i];
                var key = (fg.StaffIndex < 0 ? 0 : fg.StaffIndex, fg.MeasureIndex, fg.ItemIndex, fg.IsAbove);
                if (artOuter.TryGetValue(key, out var artYUp))
                {
                    // Both are Y-up now; do the gap clamp in device against the shared
                    // staff middle (fingering & its articulation are the same note/staff),
                    // then reflect back to Y-up.
                    double staffMid = (staffYAt?.Invoke(fg.MeasureIndex, fg.StaffIndex) ?? 0)
                        + _options.StaffHeight / 2.0;
                    double artY = staffMid - artYUp; // ToDevice
                    double fgY = staffMid - fg.YUp;  // ToDevice
                    double target = fg.IsAbove ? artY - aboveGap : artY + belowGap;
                    double newY = fg.IsAbove ? System.Math.Min(fgY, target) : System.Math.Max(fgY, target);
                    fb2[i] = fg with { YUp = staffMid - newY }; // ToUp
                }
            }
            fingeringLayouts = fb2.ToImmutable();
        }

        return new AnnotationLayouts(
            Dynamics: stackedDynamics,
            Articulations: articulationLayouts,
            GraceNotes: score != null ? GraceNoteEngraver.Calculate(score, graceNotes, ml, measuresByStaff, staffYByIndex, staffByIndex, articulations) : ImmutableArray<GraceNoteLayout>.Empty,
            Lyrics: lyricLayouts,
            LyricHyphens: new LyricHyphenEngraver().CalculateLayouts(lyricLayouts, systems),
            MusicMarks: stackedMarks,
            CustomTexts: stackedCustomTexts,
            VoltaBrackets: stackedVoltas,
            TupletBrackets: tupletBracketLayouts,
            Hairpins: stackedHairpins,
            TextSpanners: stackedTextSpanners,
            OttavaBrackets: stackedOttavas,
            Arpeggios: arpeggioLayouts,
            PedalBrackets: pedalBracketLayouts,
            FiguredBasses: figuredBassLayouts,
            ChordNames: chordNameLayouts,
            PercentRepeats: percentRepeatLayouts,
            CrossStaffs: crossStaffLayouts ?? ImmutableArray<CrossStaffLayout>.Empty,
            TrillSpanners: stackedTrills,
            // LILYPOND-REF: lily/fingering-engraver.cc — Fingering grob.
            Fingerings: fingeringLayouts,
            // LILYPOND-REF: lily/laissez-vibrer-engraver.cc + repeat-tie-engraver.cc — half-ties.
            TieVariants: score != null
                ? TieVariantEngraver.Calculate(score, systems)
                : ImmutableArray<TieVariantLayout>.Empty,
            // LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob.
            MultiMeasureRests: score != null
                ? MultiMeasureRestEngraver.Calculate(score, systems, _options.StaffHeight,
                    allStaffMeasures: measuresByStaff != null && measuresByStaff.Count > 1
                        ? measuresByStaff.Values.ToArray() : null)
                : ImmutableArray<MultiMeasureRestLayout>.Empty,
            // LILYPOND-REF: lily/ledger-line-spanner.cc — LedgerLineSpanner grob.
            LedgerLineSpans: score != null
                ? LedgerLineSpannerEngraver.Calculate(score, systems, _options.StaffHeight)
                : ImmutableArray<LedgerLineSpan>.Empty,
            // LILYPOND-REF: lily/bar-number-engraver.cc — BarNumber grob.
            BarNumbers: stackedBarNumbers,
            // LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob.
            StanzaNumbers: StanzaNumberEngraver.Calculate(lyricLayouts, systems));
    }

    /// <summary>
    /// Voice collision offsets / head-wipes / dot-force-down for multi-voice staves
    /// (so the renderer can nudge opposing voices apart), plus opt-in part-combine
    /// layouts. Keys are (measureIndex, voiceId, itemIndex) — correct for the common
    /// single-multi-voice-staff case. Extracted verbatim from the multi-staff
    /// <c>Layout</c> body.
    /// </summary>
    private (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
             ImmutableHashSet<VoiceItemKey> HeadWipes,
             ImmutableHashSet<VoiceItemKey> DotForceDown,
             ImmutableArray<PartCombineLayout> PartCombine)
        CalculateVoiceCollisions(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        var voiceOffsetsBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotForceDownBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            if (staff.Voices.Length < 2)
                continue;

            var staffScore = new Score(
                staff.Voices, score.TimeSignature, score.KeySignature, ClefToString(staff.Clef));
            var (vo, hw, df) = _elementCoordinator.CalculateVoiceOffsets(staffScore);
            foreach (var kv in vo) voiceOffsetsBuilder[kv.Key] = kv.Value;
            foreach (var k in hw) headWipeBuilder.Add(k);
            foreach (var k in df) dotForceDownBuilder.Add(k);

            // Part combination is opt-in (\partcombine); plain << \\ >> voices
            // carry no a2/Solo text. Gated off by default to match LilyPond.
            if (_options.EnablePartCombine && partCombineLayouts.IsEmpty)
            {
                var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
                var combineItems = PartCombineAnalyzer.Analyze(
                    staff.Voices[0], staff.Voices[1]);
                partCombineLayouts = PartCombineAnalyzer.Calculate(combineItems, ml, staff.Voices[0].Measures);
            }
        }
        return (voiceOffsetsBuilder.ToImmutable(), headWipeBuilder.ToImmutable(),
                dotForceDownBuilder.ToImmutable(), partCombineLayouts);
    }

    /// <summary>
    /// Common tail of both <c>Layout</c> overloads: stamp the engine options onto
    /// the built layout and, when the score carries user \override/\revert, attach
    /// a grob-property resolver.
    /// LILYPOND-REF: lily/grob-property.cc — user overrides/reverts on the layout.
    /// </summary>
    private ScoreLayout FinalizeLayout(ScoreLayout result,
        ImmutableArray<GrobOverride> grobOverrides, ImmutableArray<GrobRevert> grobReverts)
    {
        result = result with { Options = _options };
        if (!grobOverrides.IsDefaultOrEmpty || !grobReverts.IsDefaultOrEmpty)
        {
            result = result with
            {
                GrobPropertyResolver = new GrobPropertyResolver(grobOverrides, grobReverts)
            };
        }
        return result;
    }

    // Piano pedal sustain / sostenuto / una-corda marks.
    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // A pedal RELEASE mark ("*"): the mixed style has no star (Ped._____| ).
    private static bool IsPedalOffMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOff or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOff;

    // Whether a mark's "Ped." / "*" text is still drawn under the given style.
    // LILYPOND-REF: lily/piano-pedal-engraver.cc — text keeps both, bracket keeps
    // neither, mixed keeps the leading "Ped." only.
    private static bool KeepPedalTextMark(MusicMarkItem m, PedalStyle style)
    {
        if (!IsPedalMark(m.Type)) return true;
        return style switch
        {
            PedalStyle.Text => true,
            PedalStyle.Mixed => !IsPedalOffMark(m.Type),
            _ => false,
        };
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
    /// LILYPOND-REF: scm/define-grobs.scm:1851-1868 InstrumentName padding = 0.3
    /// LILYPOND-REF: scm/output-lib.scm — system-start-text::calc-x-offset
    /// </remarks>
    private static double CalculateIndentFromInstrumentNames(MultiStaffScore score)
    {
        // LILYPOND-REF: ly/paper-defaults-init.ly — indent = 15\mm
        // 15mm / (20pt/4 × 0.3528mm/pt) = 15 / 1.764 ≈ 8.5 staff spaces
        const double DefaultIndent = 8.5;

        double maxNameWidth = 0;

        // Font size: SvgRenderer.FontSize (4.0) × instrument name scale (0.75) = 3.0
        // Average serif char ≈ 0.5 em; CJK glyphs are full-width (≈ 1.0 em) —
        // the flat Latin estimate ran 「津田さん」 into the clef.
        const double nameFontSize = 3.0;

        foreach (var group in score.StaffGroups)
        {
            foreach (var staff in group.Staves)
            {
                if (!string.IsNullOrEmpty(staff.InstrumentName))
                {
                    const int cjkBlockStart = 0x2E80;  // CJK Radicals Supplement — first full-width block
                    const double cjkEmWidth = 1.0;     // CJK glyph ≈ full em
                    const double latinEmWidth = 0.5;   // average serif Latin char ≈ half em
                    double nameWidth = 0;
                    foreach (char ch in staff.InstrumentName)
                        nameWidth += (ch >= cjkBlockStart ? cjkEmWidth : latinEmWidth) * nameFontSize;
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

    /// <summary>
    /// Builds a reference-identity predicate over the items carried by the given
    /// beam groups (the notes/chords that render with no flag). Returns null when
    /// nothing is beamed, so the spacing keeps its flag-reserving default.
    /// </summary>
    private static Func<MusicItem, bool>? BeamedPredicate(IEnumerable<BeamGroup> groups)
    {
        var set = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var g in groups)
            foreach (var m in g.Members)
                set.Add(m.Item);
        if (set.Count == 0)
            return null;
        return set.Contains;
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

    /// <summary>The tuplets belonging to one staff — used to scope beam-break
    /// boundaries so a tuplet on another staff can't split this staff's beams.</summary>
    private static ImmutableArray<TupletBracketItem> StaffTuplets(
        ImmutableArray<TupletBracketItem> all, int staffIndex)
        => all.IsDefaultOrEmpty ? all
            : all.Where(t => t.StaffIndex == staffIndex).ToImmutableArray();

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
