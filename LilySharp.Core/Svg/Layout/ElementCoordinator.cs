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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Coordinates layout of beams, ties, slurs, and voice collisions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc, lily/tie.cc, lily/slur.cc
/// </remarks>
internal sealed class ElementCoordinator
{
    private readonly LayoutOptions _options;
    private readonly BeamDetector _beamDetector = new();
    private readonly BeamEngraver _beamEngraver = new();
    private readonly TieDetector _tieDetector = new();
    // Tie layout is done by TieFormattingProblem (see LayoutTies); the
    // reference-only TieEngraver twin was deleted.
    private readonly SlurDetector _slurDetector = new();
    private readonly GlissandoDetector _glissandoDetector = new();

    // force-hshift is DISABLED for the initial release. From source the written value is
    // normalized away by horizontal justification and applies to the whole note column
    // rather than to one voice, so it cannot do what it is for (a per-voice, magnitude-
    // honoring, fractional shift). The resolver / NoteCollision support below is kept
    // intact — flip this to true once that proper implementation lands. Not a `const`, so
    // the disabled query does not read as unreachable code.
    //
    // While this is false the spelling is also OUT of SupportedGrobOverrides (removed
    // 2026-08-23, GRAMMAR_AUDIT §4.3): writing it is an honest LYS1029 instead of the
    // silent no-op four docs claimed the language does not have. Flip this AND restore
    // the ("NoteColumn", "force-hshift") row in the same commit.
    private static readonly bool ForceHshiftEnabled = false;

    public ElementCoordinator(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// The whole staff's X offsets, head wipes and dot adjustments for the notes that
    /// collide in multi-voice contexts — the three tables the renderer reads, as the union
    /// over the staff's measures of <see cref="ComputeVoiceCollisionsOfMeasure"/>. The
    /// finishing pass files the same tables from the same measures through the per-system
    /// memo (<c>LayoutEngine.CalculateVoiceCollisions</c>); this whole-staff spelling is the
    /// one the nets compare it against, not a second computation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-317 — head wipe
    /// LILYPOND-REF: lily/note-collision.cc:607-622 — force-hshift manual override
    /// </remarks>
    internal static (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
            ImmutableHashSet<VoiceItemKey> HeadWipeEntries,
            ImmutableDictionary<VoiceItemKey, DotAdjustment> DotAdjustments) ComputeVoiceOffsets(
        ImmutableArray<Voice> voices, GrobPropertyResolver? resolver = null, int staffIndex = 0)
    {
        var offsetBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotAdjustBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, DotAdjustment>();
        if (voices.Length > 1)
        {
            int measureCount = 0;
            foreach (var voice in voices)
                measureCount = Math.Max(measureCount, voice.Measures.Length);
            for (int m = 0; m < measureCount; m++)
                AddVoiceCollisions(ComputeVoiceCollisionsOfMeasure(voices, m, resolver), staffIndex,
                    offsetBuilder, headWipeBuilder, dotAdjustBuilder);
        }
        return (offsetBuilder.ToImmutable(), headWipeBuilder.ToImmutable(), dotAdjustBuilder.ToImmutable());
    }

    /// <summary>
    /// Files a run of collision entries into the renderer's three tables — the one place
    /// that knows which field of an entry goes to which table. The entries are one staff's
    /// (<paramref name="staffIndex"/>, score-wide): see <see cref="VoiceItemKey"/>.
    /// </summary>
    internal static void AddVoiceCollisions(
        ImmutableArray<VoiceCollisionEntry> entries, int staffIndex,
        ImmutableDictionary<VoiceItemKey, double>.Builder offsets,
        ImmutableHashSet<VoiceItemKey>.Builder headWipes,
        ImmutableDictionary<VoiceItemKey, DotAdjustment>.Builder dotAdjustments)
    {
        foreach (var e in entries)
        {
            var key = new VoiceItemKey(staffIndex, e.MeasureIndex, e.VoiceId, e.ItemIndex);
            if (e.XOffset != 0)
                offsets[key] = e.XOffset;
            if (e.HeadTransparent)
                headWipes.Add(key);
            if (e.Dot != default)
                dotAdjustments[key] = e.Dot;
        }
    }

    /// <summary>
    /// ONE measure's collision entries: the measure's columns across every voice
    /// (<see cref="VoiceCollector.CollectMeasure"/>), each solved by
    /// <see cref="NoteCollision.CalculateVoiceOffsets"/> — the unit
    /// <see cref="VoiceCollisionTable"/> fills a bar at a time and the per-system memo
    /// slices. Reachable from the SPACING side without a <see cref="Score"/> or a
    /// coordinator instance: <see cref="SpacingRules.ApplyCrossVoiceColumnSpacing"/> must
    /// price a column's ink at the X the renderer will draw it — collision shift included —
    /// and the only non-drifting way to know that shift is to ask the SAME computation the
    /// renderer's offsets come from. LILYPOND-REF: lily/note-collision.cc
    /// calc_positioning_done runs before spacing reads the columns' extents, so LilyPond's
    /// separation boxes carry the shifts by construction; Lily# applies them at render
    /// time, so the spacing side has to ask. Empty (no allocation) where nothing collided.
    /// </summary>
    internal static ImmutableArray<VoiceCollisionEntry> ComputeVoiceCollisionsOfMeasure(
        ImmutableArray<Voice> voices, int measureIndex, GrobPropertyResolver? resolver = null)
    {
        if (voices.Length <= 1)
            return ImmutableArray<VoiceCollisionEntry>.Empty;

        var voiceColumns = VoiceCollector.CollectMeasure(voices, measureIndex);
        if (voiceColumns.Length == 0)
            return ImmutableArray<VoiceCollisionEntry>.Empty;

        var noteCollision = new NoteCollision();
        ImmutableArray<VoiceCollisionEntry>.Builder? entries = null;

        foreach (var column in voiceColumns)
        {
            if (column.Entries.Length <= 1)
                continue;

            // LILYPOND-REF: lily/note-collision.cc:427-438
            // Width-based shift normalization: use the widest notehead width
            // in the column so shifts scale correctly for whole/breve noteheads.
            double noteheadWidth = GetColumnNoteheadWidth(column);

            // LILYPOND-REF: lily/note-collision.cc:607-622
            // Check for force-hshift manual override before auto-calculation.
            // When active, force-hshift replaces the auto-calculated offset.
            // (Disabled for the initial release — see ForceHshiftEnabled.)
            double? forceHshift = null;
            if (ForceHshiftEnabled && resolver != null)
            {
                // Advance resolver to the first entry's position in this column
                int minItemIndex = column.Entries.Min(e => e.ItemIndex);
                resolver.AdvanceTo(column.MeasureIndex, minItemIndex);
                forceHshift = resolver.GetDouble("NoteColumn", "force-hshift");
            }

            var offsets = noteCollision.CalculateVoiceOffsets(column);

            foreach (var (voiceId, itemIndex, xOffset, headTransparent, dot) in offsets)
            {
                // LILYPOND-REF: lily/note-collision.cc:607-622
                // force-hshift overrides auto-calculated offsets for all columns at this position.
                double effectiveOffset = forceHshift.HasValue
                    ? forceHshift.Value * noteheadWidth
                    : xOffset;

                // A shift under a thousandth of a space is no shift — the renderer's table
                // never held one, and a wipe or a dot adjustment rides in without it.
                if (Math.Abs(effectiveOffset) <= 0.001)
                    effectiveOffset = 0;
                if (effectiveOffset == 0 && !headTransparent && dot == default)
                    continue;

                (entries ??= ImmutableArray.CreateBuilder<VoiceCollisionEntry>()).Add(
                    new VoiceCollisionEntry(column.MeasureIndex, voiceId, itemIndex,
                        effectiveOffset, headTransparent, dot));
            }
        }

        return entries?.ToImmutable() ?? ImmutableArray<VoiceCollisionEntry>.Empty;
    }

    /// <summary>
    /// Determines the widest notehead width in a voice column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:427-438
    /// LilyPond normalizes collision shifts by the first head's width.
    /// We use the widest notehead to ensure sufficient displacement.
    /// Whole notes (1.688) are wider than half/quarter (1.18).
    /// </remarks>
    private static double GetColumnNoteheadWidth(VoiceColumn column)
    {
        double maxWidth = EngravingDefaults.NoteheadBlackWidth;
        foreach (var entry in column.Entries)
        {
            var duration = entry.Item switch
            {
                NoteItem note => note.BaseDuration,
                ChordItem chord => chord.BaseDuration,
                _ => default
            };
            if (duration.Numerator > 0)
            {
                int noteValue = duration.Denominator / duration.Numerator;
                double width = noteValue switch
                {
                    <= 0 => EngravingDefaults.NoteheadDoubleWholeWidth, // breve or longer
                    1 => EngravingDefaults.NoteheadWholeWidth,          // whole note
                    _ => EngravingDefaults.NoteheadBlackWidth            // half, quarter, etc.
                };
                if (width > maxWidth) maxWidth = width;
            }
        }
        // The heads' size (NoteHead.scale) on the full-size advances above.
        return maxWidth * GlyphMetrics.StaffHeadMagnification;
    }

    /// <summary>
    /// Detects beam groups (raw, without layout calculation).
    /// Used for tuplet bracket-visibility checks.
    /// </summary>
    /// <param name="score">The detection input: its voices, time signature and tuplet
    /// brackets (nothing else of it is read — <see cref="BeamDetector"/>).</param>
    /// <param name="memo">The layout's per-measure detection memo, when a session has one
    /// (<c>SystemLayoutCache.BeamDetection</c>): the bars a previous keystroke detected
    /// replay instead of walking again. Null ⇒ detect every bar live.</param>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Score score, BeamDetectionMemo? memo = null)
        => _beamDetector.DetectBeamGroups(score, memo);

    /// <summary>
    /// Moves a beam voice's item X table onto the x its heads are DRAWN at, when a
    /// multi-voice column shifted them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: lily/note-collision.cc:467-468 <c>done[i]-&gt;translate_axis (amounts[i] -
    /// left_most, X_AXIS)</c> — LilyPond moves the whole <c>Note_column</c>, and
    /// <c>Note_column::get_stem</c> is a member of that column, so a LilyPond stem cannot be
    /// left behind by its head and no separate step exists to port. Lily# resolves the shift
    /// at render time instead (<c>SharedRenderer.CollectStaffItems</c>), which the UNBEAMED
    /// stem rides for free because it is drawn from that already-shifted x — the beamed one is
    /// drawn from <c>BeamLayout.MemberXPositions</c>, so the shift has to reach the table
    /// those are built from.
    /// </para>
    /// <para>
    /// It is applied HERE, to the table, and not at the draw site, because the table is also
    /// the quanter's frame: <c>BeamScoringProblem</c> measures covered grobs against
    /// <c>StemXOf</c> and <see cref="CollectBeamCollisions"/> books them, both off these same
    /// positions. Shifting only what is drawn would score the beam in one frame and draw it in
    /// another — and the shift is per NOTE, not per beam, so it does not cancel out of the
    /// quanter's stem-to-stem distances the way a whole-group translation would.
    /// </para>
    /// <para>
    /// Empty for every single-voice book (the overwhelming majority), which is answered
    /// without a lookup; the per-item probe runs only where the bar actually has shifts.
    /// </para>
    /// </remarks>
    private static void ApplyVoiceCollisionShifts(
        List<double> itemXPositions,
        VoiceCollisionTable voiceShifts,
        int measureIndex, int voiceIndex)
    {
        if (!voiceShifts.AnyShiftIn(measureIndex))
            return;

        // VoiceId is 1-based, as VoiceCollector stamps it and as the renderer and the skyline
        // seed both read it back (SkylineBuilder's `vi + 1`).
        for (int i = 0; i < itemXPositions.Count; i++)
            itemXPositions[i] += voiceShifts.ShiftOf(measureIndex, voiceIndex + 1, i);
    }

    /// <summary>
    /// Detects beam groups and calculates their layouts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — beam grobs (single-measure and multi-measure).
    /// Multi-measure beams (BeamMember.MeasureIndex != group.MeasureIndex for any
    /// member) are handled via <see cref="LayoutCrossMeasureBeamPieces"/>: each
    /// member's X position is resolved against its OWN measure's layout, and
    /// cross-system spans are split into broken pieces per system.
    /// </remarks>
    /// <param name="precomputedGroups">The detection result to lay out, when the caller has
    /// already run <see cref="DetectBeamGroups"/> on <paramref name="score"/> — the per-staff
    /// detection memo (<c>MultiStaffLayouter.StaffBeamGroupsOf</c>) hands its one detection to
    /// every layout call, and the per-system beam memo partitions it by system and hands each
    /// partition back through here, so detection and layout cannot diverge. Null detects
    /// internally, exactly as before (no production caller passes null any more).</param>
    public ImmutableArray<BeamLayout> LayoutBeams(
        Score score, ImmutableArray<SystemLayout> systems, int staffIndex,
        ImmutableArray<BeamGroup>? precomputedGroups = null)
    {
        var beamGroups = precomputedGroups ?? _beamDetector.DetectBeamGroups(score);

        if (beamGroups.Length == 0)
            return ImmutableArray<BeamLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        // Lent, and given back once copied out below — the walk's only exit.
        var beamLayouts = ListPool<BeamLayout>.Rent();
        var voiceShifts = SpacingRules.VoiceCollisionShiftsOf(score.Voices);

        foreach (var group in beamGroups)
        {
            // LILYPOND-REF: lily/beam.cc — multi-measure beams get a dedicated path.
            if (IsCrossMeasureGroup(group))
            {
                foreach (var crossLayout in LayoutCrossMeasureBeamPieces(score, group, measureMap, staffIndex))
                    beamLayouts.Add(crossLayout);
                continue;
            }

            if (!measureMap.TryGetValue(group.MeasureIndex, out var measureInfo))
                continue;

            var (system, measureLayout) = measureInfo;
            // Beams resolve against their OWN voice's measures (voice 2 has its own
            // item stream); single-voice scores keep VoiceIndex 0 = score.Voice.
            var measure = score.Voices[group.VoiceIndex].Measures[group.MeasureIndex];

            // One X per item of whichever stream the branch below walks — the size is that
            // walk's own trip count, not a bound (measured before it was handed over:
            // 35,162 calls, asked == Count every time).
            bool fromColumns = !measureLayout.Columns.IsDefaultOrEmpty
                && measureLayout.Columns.Length > 0;
            var itemXPositions = new List<double>(
                fromColumns ? measure.Items.Length : measureLayout.Items.Length);
            if (fromColumns)
            {
                var currentTiming = Fraction.Zero;
                foreach (var item in measure.Items)
                {
                    double itemX = measureLayout.X + measureLayout.GetXForTiming(currentTiming);
                    itemXPositions.Add(itemX);
                    currentTiming = currentTiming + item.Duration;
                }
            }
            else
            {
                foreach (var itemLayout in measureLayout.Items)
                {
                    itemXPositions.Add(measureLayout.X + itemLayout.X);
                }
            }

            ApplyVoiceCollisionShifts(itemXPositions, voiceShifts, group.MeasureIndex,
                group.VoiceIndex);

            // The X table must cover the beam voice's whole item stream. On the
            // non-column path it is built from the PRIMARY voice's layout items,
            // so a SECONDARY voice's stream (more items than the layout has
            // slots) cannot be positioned — skip the group rather than index out
            // of range (the renderer guards the same situation with
            // itemIdx >= ml.Items.Length and skips the note).
            if (measure.Items.Length > itemXPositions.Count)
                continue;

            var collisions = CollectBeamCollisions(
                score.Voices[group.VoiceIndex].Measures[group.MeasureIndex],
                group,
                itemXPositions,
                score.TextMetrics, fromColumns, measureLayout.ChangeColumnHangs);

            // Also keep the beam clear of the OTHER voices' notes/rests (a
            // polyphonic staff's stem-up beam rides over a high note held below).
            double beamLeftX = itemXPositions[group.Members[0].ItemIndex];
            double beamRightX = itemXPositions[group.Members[^1].ItemIndex];
            AppendCrossVoiceBeamCollisions(
                ref collisions, score, group, measureLayout, beamLeftX, beamRightX);

            // The system comes from the SAME measureMap lookup that gave the X positions, so
            // the stamp and the frame the X is in cannot disagree.
            var beamLayout = _beamEngraver.CalculateBeamLayout(
                group,
                itemXPositions,
                staffIndex,
                system.SystemIndex,
                collisions);

            beamLayouts.Add(beamLayout);
        }

        var laid = beamLayouts.ToImmutableArray();
        ListPool<BeamLayout>.Give(beamLayouts);
        return laid;
    }

    /// <summary>
    /// True iff any member of <paramref name="group"/> declares a measure index
    /// different from the group's own MeasureIndex.
    /// </summary>
    private static bool IsCrossMeasureGroup(BeamGroup group)
    {
        foreach (var m in group.Members)
        {
            int resolved = m.ResolveMeasureIndex(group.MeasureIndex);
            if (resolved != group.MeasureIndex)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes one or more beam layouts for a multi-measure beam group.
    /// When all members share a system, returns a single layout. When members
    /// span a system break (cross-system case), splits into "broken pieces" —
    /// one BeamLayout per system, each anchored to that system's measure layout
    /// and Y reference.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — multi-measure beams.
    /// LILYPOND-REF: lily/break-substitution.cc — cross-system spanner break_substitute.
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing (general split pattern).
    /// </remarks>
    private IEnumerable<BeamLayout> LayoutCrossMeasureBeamPieces(
        Score score, BeamGroup group,
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)> measureMap,
        int staffIndex)
    {
        // Group members by their system index. Members of the same system stay
        // on the same beam piece; the break happens between systems.
        var bySystem = new Dictionary<int, List<BeamMember>>();
        foreach (var m in group.Members)
        {
            int memberMeasure = m.ResolveMeasureIndex(group.MeasureIndex);
            if (!measureMap.TryGetValue(memberMeasure, out var info))
                yield break; // missing measure; abort
            int sysIdx = info.System.SystemIndex;
            if (!bySystem.TryGetValue(sysIdx, out var list))
            {
                list = new List<BeamMember>();
                bySystem[sysIdx] = list;
            }
            list.Add(m);
        }

        if (bySystem.Count == 0)
            yield break;

        // Emit one piece per system (in system-index order). Each piece is built
        // from the original group's metadata but only the members in that system.
        foreach (var sysIdx in bySystem.Keys.OrderBy(k => k))
        {
            var pieceMembers = bySystem[sysIdx];
            if (pieceMembers.Count < 2)
                continue; // single-member fragments aren't beams.

            // The piece's "anchor measure" = first member's actual measure (so the
            // renderer's measureToSystem lookup picks the right system).
            int anchorMeasure = pieceMembers[0].ResolveMeasureIndex(group.MeasureIndex);

            var subGroup = new BeamGroup(
                pieceMembers.ToImmutableArray(),
                measureIndex: anchorMeasure,
                startIndex: pieceMembers[0].ItemIndex,
                group.StemUp,
                group.GrowDirection,
                group.VoiceIndex);

            var pieceLayout = LayoutSingleSystemBeamPiece(
                score, subGroup, measureMap, staffIndex, sysIdx);
            if (pieceLayout != null)
                yield return pieceLayout;
        }
    }

    /// <summary>
    /// Lays out a beam piece whose members are all within a single system but
    /// may span multiple measures inside that system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc — single beam, possibly across measures within one system.
    /// </remarks>
    private BeamLayout? LayoutSingleSystemBeamPiece(
        Score score, BeamGroup group,
        IReadOnlyDictionary<int, (SystemLayout System, MeasureLayout Measure)> measureMap,
        int staffIndex, int systemIndex)
    {
        // The same collision shifts the single-measure path adds to its table — see
        // ApplyVoiceCollisionShifts. A beam that crosses a bar line has members in two
        // measures, and each asks its own measure for its shift.
        var voiceShifts = SpacingRules.VoiceCollisionShiftsOf(score.Voices);

        // Resolves an item's X via its OWN measure layout; null when the measure or the
        // item fell outside this system's map.
        double? ResolveX(int measureIdx, int itemIdx)
        {
            if (!measureMap.TryGetValue(measureIdx, out var info))
                return null;
            var (_, measureLayout) = info;
            if (itemIdx >= measureLayout.Items.Length)
                return null;

            double shift = voiceShifts.ShiftOf(measureIdx, group.VoiceIndex + 1, itemIdx);

            var measure = score.Voices[group.VoiceIndex].Measures[measureIdx];
            if (!measureLayout.Columns.IsDefaultOrEmpty && measureLayout.Columns.Length > 0)
            {
                Fraction t = Fraction.Zero;
                for (int k = 0; k < itemIdx; k++)
                    t += GetItemDuration(measure.Items[k]);
                return measureLayout.X + measureLayout.GetXForTiming(t) + shift;
            }
            return measureLayout.X + measureLayout.Items[itemIdx].X + shift;
        }

        var memberXs = new List<double>(group.Members.Length);
        var renumbered = new List<BeamMember>(group.Members.Length);
        for (int i = 0; i < group.Members.Length; i++)
        {
            var m = group.Members[i];
            if (ResolveX(m.ResolveMeasureIndex(group.MeasureIndex), m.ItemIndex) is not { } x)
                return null;

            memberXs.Add(x);

            // Renumber member.ItemIndex to its index in the dense list so
            // BeamScoringProblem's itemXPositions[member.ItemIndex] resolves.
            renumbered.Add(new BeamMember(
                group.ItemOf(i), m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
                m.StaffPosition, itemIndex: i,
                memberStemUp: m.MemberStemUp,
                targetStaffIndex: m.TargetStaffIndex,
                measureIndex: m.MeasureIndex,
                headPositionMin: m.HeadPositionMin,
                headPositionMax: m.HeadPositionMax));
        }

        // The rests the beam runs over resolve the same way, and their dense indices are
        // appended AFTER the members' so one flat x list serves the scorer for both.
        var restXs = new List<double>(group.RestStems.Length);
        var renumberedRests = new List<BeamRestStem>(group.RestStems.Length);
        foreach (var r in group.RestStems)
        {
            int restMeasure = r.MeasureIndex >= 0 ? r.MeasureIndex : group.MeasureIndex;
            if (ResolveX(restMeasure, r.ItemIndex) is not { } rx)
                return null;
            restXs.Add(rx);
            renumberedRests.Add(r with { ItemIndex = memberXs.Count + renumberedRests.Count });
        }

        var renumberedGroup = new BeamGroup(
            renumbered.ToImmutableArray(),
            group.MeasureIndex,
            startIndex: 0,
            group.StemUp,
            group.GrowDirection,
            group.VoiceIndex,
            restStems: renumberedRests.ToImmutableArray());

        // Cross-measure collision detection is deferred — pass empty list for now.
        var beamLayout = _beamEngraver.CalculateBeamLayout(
            renumberedGroup,
            memberXs.Concat(restXs).ToList(),
            staffIndex: staffIndex,
            systemIndex: systemIndex,
            collisions: null);

        // The dense renumbering above exists ONLY so the scorer can index
        // memberXs by member.ItemIndex. Everything downstream keys on the REAL
        // (measure, item) position — the renderer's beamed-items suppression set
        // (BuildBeamedItemsSet) and the data-pos note resolver — and the drawing
        // itself reads members by ordinal, so hand the layout back with the
        // ORIGINAL members. Leaving the dense indices in would re-stem the
        // beamed notes and suppress unrelated items that happen to sit at the
        // renumbered positions.
        return new BeamLayout(
            group,
            beamLayout.LeftY, beamLayout.RightY,
            beamLayout.LeftX, beamLayout.RightX,
            beamLayout.LeftStemX, beamLayout.RightStemX,
            beamLayout.MemberXPositions,
            beamLayout.StaffIndex,
            beamLayout.SystemIndex,
            beamLayout.MemberStaffIndices,
            // CalculateBeamLayout already resolved these to the invisible stems' x (the
            // rest glyphs' ink centres) from the raw column xs appended above.
            restXPositions: beamLayout.RestXPositions);
    }

    private static Fraction GetItemDuration(MusicItem item) => item switch
    {
        NoteItem n => n.Duration,
        ChordItem c => c.Duration,
        RestItem r => r.Duration,
        _ => Fraction.Zero,
    };

    /// <summary>
    /// Collects collision objects for beam scoring.
    /// </summary>
    private List<BeamCollision>? CollectBeamCollisions(
        Measure measure,
        BeamGroup group,
        IReadOnlyList<double> itemXPositions,
        Rendering.ScoreTextMetrics fonts,
        bool fromColumns,
        ImmutableDictionary<Fraction, double>? changeHangs)
    {
        List<BeamCollision>? collisions = null;
        var beamMemberIndices = new HashSet<int>(group.Members.Select(m => m.ItemIndex));

        int firstMemberIndex = group.Members[0].ItemIndex;
        int lastMemberIndex = group.Members[^1].ItemIndex;
        double beamLeftX = itemXPositions[firstMemberIndex];
        double beamRightX = itemXPositions[lastMemberIndex];
        // The beam's own frame: its stems, and its drawn extent half a stem width past
        // each outer one. LILYPOND-REF: lily/beam.cc:631 horizontal_[dir] += dir * stem_width/2.
        double beamOriginX = BeamStemX(group, 0, beamLeftX);
        double halfStemWidth = EngravingDefaults.StemThickness / 2;
        double beamEdgeLeftX = beamOriginX - halfStemWidth;
        double beamEdgeRightX =
            BeamStemX(group, group.Members.Length - 1, beamRightX) + halfStemWidth;

        for (int i = 0; i < measure.Items.Length; i++)
        {
            if (beamMemberIndices.Contains(i))
                continue;

            var item = measure.Items[i];

            // A REST IS NEVER A COVERED GROB — not between the beam's members, not in
            // another voice, not anywhere.
            // LILYPOND-REF: scm/define-grobs.scm:496-504 collision-interfaces — note-head-interface
            //   and stem-interface are in the Beam's list; rest-interface is NOT. The
            //   engraver reads that list in lily/beam-collision-engraver.cc:100-103
            //   covered_grob_has_interface and has no acknowledge_rest beside its
            //   acknowledge_note_head / acknowledge_stem, so a Rest never enters the beam's
            //   covered set and the quanter never sees it.
            // ⚠️ A rest BETWEEN the members is moved clear of the beam instead
            //   (lily/beam.cc:1331 rest_collision_callback — see CalculateRestShifts);
            //   the beam is quanted as if the rest were not there.
            // ⚠️ This guard used to carry the index range `i > firstMemberIndex &&
            //   i < lastMemberIndex`, which caught only the between case. Rests OUTSIDE
            //   that range — in practice the other voice of a `voice { } { }` span, whose
            //   items share this list — still booked a box and LIFTED the beam: measured
            //   1.810 ss above the middle line for `voice { c4 c8 c8 } { r8 r8 r8 r8 }`
            //   where LP puts the beam ON the middle line. A spacer took the same path.
            //   The interface list is the whole rule; the range was a symptom patch.
            if (item is RestItem)
                continue;
            double itemX = itemXPositions[i];

            // A clef or key change the beam runs across (BeamDetector steps over them, as
            // LilyPond's auto-beamer does) is a covered grob of the beam: the beam clears it
            // rather than drawing through it. Only one BETWEEN the outer stems can be under the
            // beam; the ones outside stand left of its first stem or right of its last.
            if (item is ClefChangeItem or KeySignatureChangeItem)
            {
                if (i > firstMemberIndex && i < lastMemberIndex)
                    AddChangeCollisions(ref collisions, fonts, measure, i, itemX, fromColumns, changeHangs,
                                        beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                continue;
            }

            AddItemCollisions(ref collisions, item, itemX,
                              beamEdgeLeftX, beamEdgeRightX, beamOriginX,
                              _beamEngraver.Parameters.StemCollisionFactor);
        }

        AddAccidentalCollisions(ref collisions, measure, itemXPositions,
                                beamEdgeLeftX, beamEdgeRightX, beamOriginX);
        return collisions;
    }

    /// <summary>
    /// Books a clef or key change under the beam as covered grobs: the clef as one box, the
    /// key change as two — its KeyCancellation and its KeySignature, which are separate grobs.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-collision-engraver.cc:217-225 acknowledge_clef /
    ///   acknowledge_key_signature — both are covered grobs of a beam whose column span they
    ///   fall in; KeyCancellation carries key-signature-interface too. Booked like every
    ///   covered box (<see cref="AddBoxCollision"/>, beam-quanting.cc:377-392).
    /// The x is the one the renderer draws the change at (SharedRenderer's change-column arm:
    /// the musical column hung back by the measure's solved hang
    /// (<see cref="MeasureLayout.ChangeColumnHangs"/>, else
    /// <see cref="SpacingRules.MidMeasureChangeRightGap"/>), then
    /// <see cref="SpacingRules.MidMeasureChangeOffsetWithin"/>); on the item-slot path the
    /// change has its own slot. ⚠️ No book observes the solved hang HERE (session 810's poison
    /// no. 7 read the force-0 gap instead: every test green) — no beam in the suite runs over a
    /// mid-measure change on a line off force 0.
    /// ⚠️ NOT BOOKED, and not reached by any book: a CUE clef (drawn from the plain glyph
    /// shrunk), a percussion clef change (no change-glyph box in the metrics), the clef
    /// modifier's 8, and a meter change (BeamDetector still ends the beam there).
    /// </remarks>
    private static void AddChangeCollisions(
        ref List<BeamCollision>? collisions, Rendering.ScoreTextMetrics fonts, Measure measure,
        int itemIndex, double columnX, bool fromColumns,
        ImmutableDictionary<Fraction, double>? changeHangs,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        var item = measure.Items[itemIndex];
        double x = columnX;
        if (fromColumns)
        {
            var columnItems = Rendering.SharedRenderer.ChangeColumnItems(measure, itemIndex);
            var timing = Fraction.Zero;
            for (int k = 0; k < itemIndex; k++)
                timing += measure.Items[k].Duration;
            x += SpacingRules.MidMeasureChangeOffsetWithin(fonts, columnItems, item)
                 - (changeHangs != null && changeHangs.TryGetValue(timing, out var hang)
                     ? hang
                     : SpacingRules.MidMeasureChangeRightGap(fonts, columnItems));
        }

        switch (item)
        {
            case ClefChangeItem { IsCue: false } clef:
            {
                // The glyph's origin is on the line the clef names (scm/parser-clef.scm
                // supported-clefs — the same positions SharedRenderer.DrawClefChange anchors at).
                var full = MusicFont.Current.FullSize;
                (GlyphMetrics.BBox box, int line)? glyph = clef.NewClef switch
                {
                    ClefType.Bass or ClefType.Bass8Below => (full.Box(MusicGlyph.FClefChange), 2),
                    ClefType.Alto => (full.Box(MusicGlyph.CClefChange), 0),
                    ClefType.Tenor => (full.Box(MusicGlyph.CClefChange), 2),
                    ClefType.Soprano => (full.Box(MusicGlyph.CClefChange), -4),
                    ClefType.MezzoSoprano => (full.Box(MusicGlyph.CClefChange), -2),
                    ClefType.Baritone => (full.Box(MusicGlyph.CClefChange), 4),
                    ClefType.Percussion or ClefType.Tab => null,
                    _ => (full.Box(MusicGlyph.GClefChange), -2),
                };
                if (glyph is not { } g)
                    return;
                double lineY = g.line * 0.5;
                AddBoxCollision(ref collisions, x + g.box.Left, x + g.box.Right,
                                lineY + g.box.Bottom, lineY + g.box.Top,
                                beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                return;
            }
            case KeySignatureChangeItem { Blanked: false } key
                when SpacingRules.ClefEngravesKey(key.Clef):
            {
                // One box per grob: the cancellation naturals, then the new signature.
                var cancel = (L: double.PositiveInfinity, B: double.PositiveInfinity,
                              R: double.NegativeInfinity, T: double.NegativeInfinity);
                var sign = cancel;
                foreach (var (kind, dx, pos) in Rendering.SharedRenderer.KeyChangeGeometry(key).Glyphs)
                {
                    var b = GlyphMetrics.GetAccidentalBBox(kind);
                    double gx = x + dx, gy = pos * 0.5;
                    ref var acc = ref (kind == "natural" ? ref cancel : ref sign);
                    acc = (Math.Min(acc.L, gx + b.Left), Math.Min(acc.B, gy + b.Bottom),
                           Math.Max(acc.R, gx + b.Right), Math.Max(acc.T, gy + b.Top));
                }
                if (cancel.R > cancel.L)
                    AddBoxCollision(ref collisions, cancel.L, cancel.R, cancel.B, cancel.T,
                                    beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                if (sign.R > sign.L)
                    AddBoxCollision(ref collisions, sign.L, sign.R, sign.B, sign.T,
                                    beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                return;
            }
        }
    }

    /// <summary>
    /// Books one non-member item — a note head, a chord's heads, or a rest — as covered
    /// grobs of the beam.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:377-392 init_instance_variables — one BOX per
    ///   covered grob, rejected when it misses the beam's x span (:381) or is empty
    ///   (:383), weighted by <c>width_factor = sqrt (width / staff_space_)</c> and booked
    ///   at BOTH x edges. A chord's heads are separate grobs there, so they are separate
    ///   boxes here, each with the stagger the renderer draws it at.
    /// <para>
    /// ⚠️ The head's own STEM is booked as well, and NOT as a box:
    /// <see cref="AddStemCollision"/> (:394-418).
    /// </para>
    /// </remarks>
    private static void AddItemCollisions(
        ref List<BeamCollision>? collisions, MusicItem item, double itemX,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX,
        double stemCollisionFactor)
    {
        // :394-398 — the stem is taken from the covered grobs that SURVIVED the rejects,
        // because the collect loop `continue`s past this line. A head that misses the
        // beam's x span brings no stem with it.
        bool anyBooked = false;
        switch (item)
        {
            // ⚠️ NO `case RestItem` — a rest is not in the Beam's collision-interfaces, and
            // the caller drops it before reaching here. The removed arm booked the rest's
            // box at its DEFAULT position and lifted the beam over it; LilyPond moves the
            // REST instead. Do not restore it: the missing entry in the list IS the rule.
            // LILYPOND-REF: scm/define-grobs.scm:496-504 collision-interfaces = note-head-interface,
            //   stem-interface and seven more, but no rest-interface;
            //   lily/beam.cc:1331 rest_collision_callback moves the rest instead.
            case NoteItem note:
                anyBooked = AddHeadCollision(
                    ref collisions, itemX, note.StaffPosition,
                    LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration),
                    beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                break;
            case ChordItem chord:
            {
                // The heads the renderer draws, stagger and all — a reversed head sits a
                // notehead width off the column and covers a different part of the beam.
                int noteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                var offsets = ChordHeadPositioning.CalculateOffsets(
                    chord.Notes, chord.StemUp, noteValue);
                for (int n = 0; n < chord.Notes.Length; n++)
                {
                    anyBooked |= AddHeadCollision(
                        ref collisions, itemX + offsets[n], chord.Notes[n].StaffPosition,
                        noteValue, beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                }
                break;
            }
        }

        if (anyBooked)
            AddStemCollision(ref collisions, item, itemX, beamOriginX, stemCollisionFactor);
    }

    /// <summary>
    /// Books a covered grob's STEM — an interval running from the head the stem starts at
    /// to INFINITY in the stem's direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:401-418 init_instance_variables — for each
    ///   colliding stem, <c>x</c> is the CENTRE of the stem's own x extent,
    ///   <c>y.set_full ()</c> then <c>y[-stem_dir] = Stem::chord_start_y (s)</c>, and the
    ///   weight is <c>STEM_COLLISION_FACTOR</c>, or 1.0 when that stem carries no beam
    ///   (:415-416).
    /// <para>
    /// ⚠️ INFINITE on purpose, and not the stem's drawn length: while this beam is being
    /// quanted the covered stem's length may not be settled either (it belongs to another
    /// beam, whose own quanting has not run), so LilyPond reserves the whole half-plane and
    /// discounts it to a tenth. A FREE stem's length IS known, and LilyPond charges it full
    /// weight — such a stem is also a covered grob in its own right
    /// (lily/beam-collision-engraver.cc:179-181 drops only BEAMED stems), whose drawn box
    /// this interval strictly contains at a heavier weight, so booking it again as a box
    /// would change nothing.
    /// </para>
    /// <para>
    /// ⚠️ A rest brings no stem: <c>Rest</c> is not in the Beam's
    /// <c>collision-interfaces</c> at all (scm/define-grobs.scm:496-504), so LilyPond never
    /// reaches one from here.
    /// </para>
    /// </remarks>
    private static void AddStemCollision(
        ref List<BeamCollision>? collisions, MusicItem item, double itemX, double beamOriginX,
        double stemCollisionFactor)
    {
        bool up;
        bool beamed;
        int noteValue;
        int chordStartPosition;
        switch (item)
        {
            case NoteItem note:
                up = note.StemUp;
                beamed = note.IsBeamed;
                noteValue = LayoutUtilities.GetNoteValueFromFraction(note.BaseDuration);
                chordStartPosition = note.StaffPosition;
                break;
            case ChordItem chord when chord.Notes.Length > 0:
                up = chord.StemUp;
                beamed = chord.IsBeamed;
                noteValue = LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration);
                // :410 Stem::chord_start_y is the position of Stem::last_head — the
                // EXTREME head at the -dir end, where the stem begins.
                chordStartPosition = up
                    ? chord.Notes.Min(n => n.StaffPosition)
                    : chord.Notes.Max(n => n.StaffPosition);
                break;
            default:
                return;
        }

        // :395 Stem::is_normal_stem — head_count && duration-log >= 1. A whole note owns a
        // Stem grob, but it is not a normal one and supplies nothing; that is why the
        // beam.quant.over-other-voice books (a sustained whole note) are not in this regime.
        if (noteValue < 2)
            return;

        double chordStartY = chordStartPosition * 0.5;
        (collisions ??= new List<BeamCollision>()).Add(new BeamCollision(
            LayoutUtilities.StemX(itemX, up, noteValue,
                LayoutUtilities.NoteheadStyleOf(item)) - beamOriginX,
            up ? chordStartY : double.NegativeInfinity,
            up ? double.PositiveInfinity : chordStartY,
            beamed ? stemCollisionFactor : 1.0));
    }

    /// <summary>One note head's box as a covered grob; false when the rejects dropped it.</summary>
    private static bool AddHeadCollision(
        ref List<BeamCollision>? collisions, double headX, int staffPosition, int noteValue,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        var box = GlyphMetrics.GetNoteheadBBox(noteValue);
        double centreSs = staffPosition * 0.5;
        return AddBoxCollision(ref collisions, headX + box.Left, headX + box.Right,
                               centreSs + box.Bottom, centreSs + box.Top,
                               beamEdgeLeftX, beamEdgeRightX, beamOriginX);
    }

    /// <summary>
    /// The shared body of <see cref="AddItemCollisions"/> and
    /// <see cref="AddAccidentalCollision"/>: LilyPond's per-covered-grob booking.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:377-392 init_instance_variables — the x-span
    ///   reject, the empty reject, <c>width_factor</c>, and one add_collision per x edge.
    /// </remarks>
    private static bool AddBoxCollision(
        ref List<BeamCollision>? collisions,
        double inkLeft, double inkRight, double minY, double maxY,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        // :381 — the box must overlap the beam's DRAWN x extent (x_pos), not the note
        // columns: LilyPond's x_pos is the beam's own stencil span.
        if (inkRight < beamEdgeLeftX || inkLeft > beamEdgeRightX)
            return false;
        // :383
        if (inkRight <= inkLeft || maxY <= minY)
            return false;

        // :388-389 — staff_space_ is 1 in this frame, so the factor is sqrt(width).
        double widthFactor = Math.Sqrt(inkRight - inkLeft);

        // :391-392 — TWO entries per grob, at its two x edges, each carrying the WHOLE y
        // extent. x is measured from the beam's left STEM; the quanter moves it the last
        // half stem width onto the beam's drawn edge.
        (collisions ??= new List<BeamCollision>()).Add(new BeamCollision(inkLeft - beamOriginX, minY, maxY, widthFactor));
        (collisions ??= new List<BeamCollision>()).Add(new BeamCollision(inkRight - beamOriginX, minY, maxY, widthFactor));
        return true;
    }

    /// <summary>
    /// The x a beam member's STEM stands at, given its note column's x.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:403-405 robust_relative_extent — a covered
    ///   grob's x is measured against the STEMS' own coordinates, and the beam's own
    ///   x_pos comes from those stems; LilyPond has no second "note column x" to
    ///   confuse with them. In Lily# a stem stands a notehead width right of its column
    ///   when it points up, which is the offset SharedRenderer.DrawBeams draws it at
    ///   (<see cref="EngravingDefaults.StemUpAttachX"/>) — so a collision measured from
    ///   the COLUMN is a notehead width out of frame from the beam it is measured against.
    /// </remarks>
    private static double BeamStemX(BeamGroup group, int memberIndex, double columnX)
    {
        bool up = group.IsKnee ? group.Members[memberIndex].MemberStemUp : group.StemUp;
        // Per MEMBER head shape, not per beam: a two-note tremolo pair beams HALF notes
        // (BeamDetector.IsBeamable), whose stem stands 0.073200 further right.
        return LayoutUtilities.StemX(columnX, up,
            GlyphMetrics.NoteValueOf(group.ItemOf(memberIndex)),
            LayoutUtilities.NoteheadStyleOf(group.ItemOf(memberIndex)));
    }

    /// <summary>Single-ape / chord accidental placement — the SAME instance path the
    /// renderer draws with, so the ink a beam is quanted against is the ink drawn.</summary>
    private static readonly AccidentalPlacement BeamAccidentalColumn = new();

    /// <summary>
    /// Registers every printed accidental under the beam as a covered grob.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-collision-engraver.cc:61-69 Beam_collision_engraver — the
    ///   engraver that fills a beam's
    ///   <c>covered-grobs</c> acknowledges note heads, stems, ACCIDENTALS, clefs, clef
    ///   modifiers, key signatures, time signatures, beams and flags. Lily# collected only
    ///   what is a MusicItem (heads/rests), so an accidental was invisible to the quanter and
    ///   a beam came to rest on a sharp (scratch/repro.lys bar 5, beat 4).
    /// <para>
    /// ⚠️ The beam's OWN members are not skipped here. In LilyPond an Accidental is a grob of
    /// its own, so the accidental of a beamed note is a covered grob like any other — it is
    /// only the head and stem of a member that the quanter handles through its stem model.
    /// </para>
    /// </remarks>
    private void AddAccidentalCollisions(
        ref List<BeamCollision>? collisions, Measure measure,
        IReadOnlyList<double> itemXPositions,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        for (int i = 0; i < measure.Items.Length; i++)
        {
            double itemX = itemXPositions[i];
            switch (measure.Items[i])
            {
                case NoteItem note when note.Accidental != null:
                    // A note sharing its column with another voice was packed into that
                    // column's one accidental column, in this same (column) frame.
                    AccidentalLayout? single = note.AccidentalX is { } packedX
                        ? new AccidentalLayout(
                            note.StaffPosition, note.Accidental, packedX, note.IsCourtesy)
                        : BeamAccidentalColumn.CalculateSinglePosition(
                            note, CueAccidentalFont(note.IsCue), CueAccidentalFont(note.IsCue));
                    if (single is { } singleLayout)
                        AddAccidentalCollision(
                            ref collisions, singleLayout, itemX, note.IsCue ? CueAccidentalScale : 1.0,
                            beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                    break;

                case ChordItem chord:
                    // The stagger the renderer uses: reversed heads move their accidentals,
                    // so the column must be solved, not assumed.
                    // LILYPOND-REF: lily/accidental-placement.cc position_apes.
                    foreach (var al in ChordAccidentalLayouts(chord))
                        AddAccidentalCollision(ref collisions, al, itemX, 1.0,
                                               beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                    break;
            }
        }
    }

    /// <summary>
    /// A chord's accidentals, as the placement resolved them: the whole staff column's packing
    /// when another voice stands on the column (<see cref="Collector.StaffAccidentalColumns"/>
    /// baked it onto the members), else this chord's own <c>position_apes</c> solve — the same
    /// answer when the chord stands alone. Both are measured from the column.
    /// </summary>
    /// <param name="font">The font the chord's heads and accidentals are read from — the cue's
    /// for a cue chord, as SharedRenderer.DrawChord solves it; null reads the twenty (the beam
    /// quanter's collision supply still asks this way).</param>
    private static IEnumerable<AccidentalLayout> ChordAccidentalLayouts(
        ChordItem chord, GlyphMetrics.DesignMetrics? font = null)
    {
        if (chord.HasPackedAccidentals)
        {
            foreach (var n in chord.Notes)
                if (n.Accidental is { } acc && n.AccidentalX is { } x)
                    yield return new AccidentalLayout(n.StaffPosition, acc, x, n.IsCourtesy);
            yield break;
        }

        var offsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, chord.StemUp,
            LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration), font);
        foreach (var al in BeamAccidentalColumn.CalculatePositions(chord.Notes, offsets,
                     font, font, stem: AccidentalStem.Of(chord, chord.StemUp, font)))
            yield return al;
    }

    /// <summary>LilyPond's CueVoice fontSize = -4 shrinks the accidental grob with the head, so
    /// both read <see cref="EngravingDefaults.CueScale"/> — one home, and the port moved the
    /// head and its accidental together.</summary>
    private static readonly double CueAccidentalScale = EngravingDefaults.CueScale;

    /// <summary>The font a cue note's accidental is measured with — the design font-size −4
    /// selects, already magnified, or null (the plain 20) for an ordinary note.</summary>
    /// <remarks>
    /// ⚠️ It was <c>Design20.Scaled(0.66)</c> until 2026-08-03: the wrong table AND a rounded
    /// factor. A cue states font-size −4, which asks 12.599pt and lands on the THIRTEEN design;
    /// Emmentaler is optically sized, so that design's glyphs are drawn differently and not
    /// merely smaller. See <see cref="EngravingDefaults.CueFont"/>.
    /// </remarks>
    private static GlyphMetrics.DesignMetrics? CueAccidentalFont(bool isCue) =>
        isCue ? EngravingDefaults.CueFont : null;

    /// <summary>
    /// One accidental as a covered grob: its LILC extent, booked at BOTH x edges.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam-quanting.cc:377-392 Beam_scoring_problem::init_instance_variables
    ///   — <c>b[a] = collisions[j]->extent (common[a], a)</c>,
    ///   the X-overlap reject at :381, the empty-extent reject at :383, then
    ///   <c>width_factor = sqrt (width / staff_space_)</c> and
    ///   <c>for (d : {LEFT, RIGHT}) add_collision (b[X_AXIS][d], b[Y_AXIS], width_factor)</c>.
    ///   TWO entries per grob, at its two x edges, each carrying the WHOLE y extent.
    /// <para>
    /// ⚠️ The box is the grob's EXTENT (the LILC box,
    /// <see cref="GlyphMetrics.GetAccidentalBBox(string?)"/>),
    /// NOT the outline box a skyline is built from — LilyPond reads <c>extent</c> here. The two
    /// differ, and picking by habit is the defect <see cref="GlyphMetrics"/> warns about.
    /// </para>
    /// </remarks>
    private static void AddAccidentalCollision(
        ref List<BeamCollision>? collisions, AccidentalLayout layout,
        double itemX, double scale,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        var box = GlyphMetrics.GetAccidentalBBox(layout.Accidental);
        // XOffset is the INK LEFT (AccidentalPlacement.InkLeft): the glyph origin plus the
        // LILC left bearing, and a courtesy accidental's left parenthesis in front of it.
        double inkLeft = itemX + layout.XOffset;
        double width = box.Width * scale;
        if (layout.IsCourtesy)
            width += (GlyphMetrics.AccidentalLeftParen.Width
                      + GlyphMetrics.AccidentalRightParen.Width) * scale;

        // Y: the glyph box hangs off the note's own position. The note's position is
        // in staff positions, the box is in staff spaces — and staff spaces is what
        // BeamCollision speaks.
        double headSs = layout.StaffPosition * 0.5;
        AddBoxCollision(ref collisions, inkLeft, inkLeft + width,
                        headSs + box.Bottom * scale, headSs + box.Top * scale,
                        beamEdgeLeftX, beamEdgeRightX, beamOriginX);
    }

    /// <summary>
    /// Collision objects for a beam from the OTHER voices on the same staff:
    /// LilyPond's Beam_collision_engraver keeps a beam clear of noteheads/rests
    /// in sibling voices (e.g. a stem-up beam rides over a high note held in the
    /// lower voice). Cross-voice X only aligns through the shared timing columns,
    /// so this is skipped for the item-slot layout path.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/beam-collision-engraver.cc.</remarks>
    private void AppendCrossVoiceBeamCollisions(
        ref List<BeamCollision>? collisions,
        Score score, BeamGroup group, MeasureLayout measureLayout,
        double beamLeftX, double beamRightX)
    {
        // ⚠️ INTO THE CALLER'S LIST. It used to build one of its own and hand it back to an
        // AddRange, and over the reader's corpus that list was ALWAYS EMPTY — 19.03 builds a
        // keystroke, capacity never off zero in 231 books (session 448's census). The books
        // that reach here are monophonic, so the guard below returns before the first Add.
        // Appending here is the same order the AddRange gave: this voice's entries first,
        // the other voices' after (session 451).
        if (score.Voices.Length <= 1
            || measureLayout.Columns.IsDefaultOrEmpty || measureLayout.Columns.Length == 0)
            return;

        double beamOriginX = BeamStemX(group, 0, beamLeftX);
        double halfStemWidth = EngravingDefaults.StemThickness / 2;
        double beamEdgeLeftX = beamOriginX - halfStemWidth;
        double beamEdgeRightX =
            BeamStemX(group, group.Members.Length - 1, beamRightX) + halfStemWidth;
        for (int v = 0; v < score.Voices.Length; v++)
        {
            if (v == group.VoiceIndex) continue;
            var measures = score.Voices[v].Measures;
            if (group.MeasureIndex >= measures.Length) continue;

            var timing = Fraction.Zero;
            foreach (var item in measures[group.MeasureIndex].Items)
            {
                double itemX = measureLayout.X + measureLayout.GetXForTiming(timing);
                timing += GetItemDuration(item);
                // No window here: the x-span reject IS LilyPond's, against the beam's
                // drawn extent and the grob's own box (AddBoxCollision, :381).
                AddItemCollisions(ref collisions, item, itemX,
                                  beamEdgeLeftX, beamEdgeRightX, beamOriginX,
                                  _beamEngraver.Parameters.StemCollisionFactor);
            }
        }
    }

    /// <summary>
    /// Calculates Y shifts (in staff positions) for rests that sit UNDER a beam,
    /// pushing them clear of it. A faithful port of LilyPond's
    /// Beam::rest_collision_callback.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:1331-1415 Beam::rest_collision_callback.
    /// LP only shifts a rest that is a MEMBER of a beam (rest -> stem -> beam):
    /// Beam_engraver::acknowledge_rest (lily/beam-engraver.cc:211-220) chains the
    /// callback onto every rest a MANUAL beam runs over, and those are exactly the
    /// invisible stems <see cref="BeamGroup.RestStems"/> carries — so membership is
    /// read from there, not re-derived by item-index containment. A rest outside
    /// any beam is left alone.
    /// <para>
    /// ⚠️ THE MOVERS CHAIN, they do not compete: LP hands this callback the offset the
    /// earlier movers (voiced position, <c>Rest_collision</c>) already gave the rest as
    /// <c>prev_offset</c>, evaluates the rest's ink WHERE THEY PUT IT (beam.cc:1388-1390
    /// translates <c>rest_extent</c> by it) and returns <c>offset + shift</c> (:1414).
    /// So <paramref name="priorShifts"/> is that table, each entry this pass emits is the
    /// chained TOTAL under the same key, and the caller lets it replace the prior entry.
    /// Before this was read, the ink was priced at the neutral origin and the two tables
    /// merged larger-wins — a voiced +4 beat the chained +4−2 and the beam push never
    /// landed (dot-rest-beam-trigger.ly is the pin: LP rel −1.0, Lily# sat at −2.0).
    /// </para>
    /// </remarks>
    public ImmutableDictionary<RestShiftKey, double> CalculateRestShifts(
        Score score,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beamLayouts,
        ImmutableDictionary<RestShiftKey, double> priorShifts)
    {
        if (beamLayouts.Length == 0)
            return ImmutableDictionary<RestShiftKey, double>.Empty;

        var shifts = new Dictionary<RestShiftKey, double>();
        // ⚠️ THE SHARED TABLE, not a measure-only copy of it: the pass builds this very map
        // for the same array later in the keystroke (MEASURED, session 466: 3,160 of 3,160
        // calls over the owner's 231 books × 8 forward keystrokes), so a copy here was
        // the one build nobody else could reuse.
        var measureMap = LayoutUtilities.BuildMeasureMap(systems);

        // LILYPOND-REF: beam.cc:2860 StaffSymbol has 5 lines -> positions [-4, 4].
        var staffSpan = (Low: -4.0, High: 4.0);

        foreach (var beamLayout in beamLayouts)
        {
            var group = beamLayout.Group;
            if (group.Members.Length < 2 || group.RestStems.IsEmpty)
                continue;

            // LILYPOND-REF: beam.cc:1372 d = get_grob_direction(stem) — UP = +1,
            // DOWN = -1. Lily# beam Y is staff-positions-from-middle, up-positive,
            // the same sign convention LP uses for positions.
            int d = group.StemUp ? 1 : -1;

            // LILYPOND-REF: beam.cc:1376-1377 — the translation is the BEAM's
            // (get_beam_translation narrows it from four beams up, beam.cc:129-145),
            // while the count in height_of_my_beams is the REST's own stem's (:1382).
            // The beam's own thickness and translation (a cue beam's: BeamGroup.Thickness).
            double beamThickness = EngravingDefaults.ToStaffPositions(group.Thickness);
            double beamTranslation = EngravingDefaults.ToStaffPositions(group.Translation);

            bool haveRestX = beamLayout.RestXPositions.Length == group.RestStems.Length;

            for (int r = 0; r < group.RestStems.Length; r++)
            {
                var rest = group.RestStems[r];

                // A rest written at a pitch is not pushed by the beam either — the
                // callback answers with the chained offset the moment it finds a
                // numeric staff-position, before it reads the beam. That is the whole
                // claim of LilyPond's rest-pitched-beam.ly.
                // LILYPOND-REF: lily/beam.cc:1336-1338 Beam::rest_collision_callback.
                if (rest.PrePositioned)
                    continue;

                int measureIndex = rest.MeasureIndex >= 0 ? rest.MeasureIndex : group.MeasureIndex;

                // LILYPOND-REF: beam.cc:1373-1374 the beam Y is read at the rest's own
                // stem x — the rest glyph's ink centre (stem.cc:1093-1105), which is what
                // RestXPositions holds.
                double restX;
                if (haveRestX)
                {
                    restX = beamLayout.RestXPositions[r];
                }
                else
                {
                    // A producer that filled no rest x: fall back to the column x.
                    if (!measureMap.TryGetValue(measureIndex, out var placed)
                        || rest.ItemIndex >= placed.Measure.Items.Length)
                        continue;
                    var measureLayout = placed.Measure;
                    restX = measureLayout.X + measureLayout.Items[rest.ItemIndex].X;
                }

                // LILYPOND-REF: beam.cc:1382-1386 beam_count is the rest stem's own
                // clamped multiplicity; beam_y = stem_y - d*height is the beam stack's
                // face toward the rest (the beams that cross it are the outermost ones).
                int restBeamCount = Math.Max(rest.CountLeft, rest.CountRight);
                double heightOfBeams = beamThickness / 2 + (restBeamCount - 1) * beamTranslation;
                // The line at the rest's own x — GetYAtX interpolates between the outer
                // member STEMS (since 2026-09-07; it interpolated between the column anchors
                // before, i.e. read the line one attach to the right of the rest).
                double stemY = beamLayout.GetYAtX(restX);
                double beamY = stemY - d * heightOfBeams;

                // LILYPOND-REF: beam.cc:1388-1392 rest_dim = rest_extent[d], the extent
                // TRANSLATED by prev_offset — the rest's REAL glyph ink where the voiced
                // position and Rest_collision already put it, on top of its default origin
                // (a semibreve hangs from the line above the middle, rest.cc:101-121;
                // every shorter rest sits at 0). The key is the beam's OWN voice: the rest
                // this pass moves is a member of the beam, and the beam knows whose it is.
                // ⚠️ The FIVE-LINE neutral letter, spelled here rather than asked of
                // NeutralRestPosition: this pass has the Score, not the staff, and a beam
                // pushes rests of a quarter or shorter — whose letter is 0 on every
                // staff. Only a whole or half rest bracketed INTO a beam reads the 2, and
                // no book puts one on a staff of fewer than five lines.
                var key = new RestShiftKey(measureIndex, group.VoiceIndex, rest.ItemIndex);
                priorShifts.TryGetValue(key, out double prior);
                var restBox = GlyphMetrics.GetRestBBox(rest.NoteValue);
                double restOrigin = (rest.NoteValue == 1 ? 2.0 : 0.0) + prior;
                double restTop = restOrigin + EngravingDefaults.ToStaffPositions(restBox.Top);
                double restBottom = restOrigin + EngravingDefaults.ToStaffPositions(restBox.Bottom);
                double restDim = d > 0 ? restTop : restBottom;

                // LILYPOND-REF: beam.cc:1393-1399 shift = d*min(d*(beam_y - d*min - rest_dim), 0),
                // minimum_distance = stemlet-length (0 by default) + Rest.minimum-distance.
                double minimumDistance =
                    EngravingDefaults.ToStaffPositions(EngravingDefaults.RestMinimumDistance);
                double shift = d * Math.Min(d * (beamY - d * minimumDistance - restDim), 0.0);
                if (shift == 0.0)
                    continue;

                // LILYPOND-REF: beam.cc:1403-1404 always move by discrete half-spaces
                // (= whole staff positions).
                shift = Math.Ceiling(Math.Abs(shift)) * Math.Sign(shift);

                // LILYPOND-REF: beam.cc:1406-1412 if the shifted rest is still inside
                // the staff, move by whole spaces (= even staff positions) instead.
                double nearEdge = restDim + shift;
                double farEdge = (d > 0 ? restBottom : restTop) + shift;
                bool insideStaff =
                    (nearEdge >= staffSpan.Low && nearEdge <= staffSpan.High) ||
                    (farEdge >= staffSpan.Low && farEdge <= staffSpan.High);
                if (insideStaff)
                    shift = Math.Ceiling(Math.Abs(shift) / 2.0) * 2.0 * Math.Sign(shift);

                // LILYPOND-REF: beam.cc:1414 return offset + staff_space * shift — the
                // callback answers with the CHAINED total, not its own push alone. Two
                // beams sharing one rest slot keep the larger push (degenerate; LP has
                // one stem -> one beam per rest).
                double total = prior + shift;
                if (!shifts.TryGetValue(key, out var existing)
                    || Math.Abs(total - prior) > Math.Abs(existing - prior))
                    shifts[key] = total;
            }
        }

        return shifts.ToImmutableDictionary();
    }

    /// <summary>
    /// Pushes a rest clear of the NOTES OF ANOTHER VOICE sounding at the same moment — the
    /// port of LilyPond's <c>Rest_collision</c>, which is what moves a rest out of the staff
    /// in an ordinary two-voice texture.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/rest-collision.cc:211-290 <c>calc_positioning_done</c>, the
    /// rests-and-notes branch. The shift is
    /// <c>y = dir * max (0, -dir*restdim[-dir] + dir*notedim[dir] + minimum_dist)</c>,
    /// discretised to half spaces (:275) and then to WHOLE spaces while the result is still
    /// inside the staff widened by one (:277-284).
    /// LILYPOND-REF: scm/define-grobs.scm:2981-2984 RestCollision <c>minimum-distance</c> 0.75.
    /// <para>
    /// ⚠️ A CLAIM THAT USED TO STAND HERE WAS REFUTED BY A DIRECT RENDER (2026-08-09,
    /// probes vrest-probe / vrest2-probe): it read a VerticalAxisGroup extent pair
    /// (−3.55 with a spacer partner, −4.25 with notes) as "a rest alone in a voice must
    /// not move". LilyPond 2.26.0 places the rest of EITHER voice at its voiced ±4 when
    /// the partner holds nothing but spacers — the direction comes from the Voice
    /// context, not from any collision — so the extent pair measured something else
    /// (plausibly the notes' own ink). What gates the voiced base now is the collector's
    /// span-scoped <c>VoiceDirection</c> stamp: zero (outside every span) keeps the rest
    /// on the neutral letter, anything else takes the voiced position, collision or not.
    /// </para>
    /// <para>
    /// The rest's STARTING position is the voiced one — <c>rest.cc</c>'s
    /// <c>staff_position_internal</c>: <c>dir × voiced-position</c> (4), quarter and
    /// shorter take it as-is, a half aligns down to the nearest staff line, a whole
    /// hangs from the next line above (lower voice one line lower) — and the collision
    /// then TRANSLATES from there. rest-avoid-note.ly is the pin: an uncollided
    /// half rest in an up voice sits at +4, not the middle.
    /// LILYPOND-REF: lily/rest.cc:46-141 staff_position_internal;
    /// LILYPOND-REF: scm/define-grobs.scm Rest — voiced-position 4.
    /// </para>
    /// <para>
    /// ⚠️ NOT PORTED, and named rather than left to be discovered: the ONLY-RESTS
    /// branch (rest-collision.cc:142-210), which spreads two voices' rests around the
    /// middle line when NO note sounds at the moment. rest-avoid-note.ly does not reach
    /// it (every colliding moment there has a note, so its rests take THIS branch) —
    /// still no corpus book, HANDOFF 5.4's rule. LilyPond's "too many colliding rests"
    /// warning (:287-288) is likewise absent (no diagnostics channel here).
    /// </para>
    /// <para>
    /// ⚠️ THE NOTE EXTENT IS THE HEAD ONLY except for a column pointing the SAME way at
    /// the SAME musical moment, which counts whole — stem included (:246-265). A note
    /// that started EARLIER but still sounds also collides, head only — LilyPond's
    /// "if the note has already happened … don't look at the stem" arm, keyed there by
    /// column inequality. Before that arm was read, this pass required onset equality
    /// and rest-avoid-note.ly's eighth rest sat on the middle line under a held note.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// ⚠️ NO LAYOUT ARGUMENT, deliberately: the shift is decided by the MUSIC alone — which
    /// voices sound together, at what positions, for how long — so it can be computed before
    /// the staves are spaced. That is what lets the per-staff SKYLINE read the same answer
    /// the renderer draws, instead of reserving a rest where it is not.
    /// </remarks>
    public ImmutableDictionary<RestShiftKey, double> CalculateRestNoteCollisions(Staff staff)
    {
        var shifts = new Dictionary<RestShiftKey, double>();

        // A PITCHED rest (`a4@rest`) is placed by what was written, and no partner and
        // no polyphony are needed to know where: it is the first arm of
        // staff_position_internal, and Rest_collision then computes no translation for
        // it at all. So it is answered here, before the collision walk, and a staff that
        // holds nothing else still leaves with the placement it was told.
        // (Cheap on purpose — an index walk with no timeline and no allocation, since
        // every single-voice staff in the book now passes through it.)
        // LILYPOND-REF: lily/rest.cc:53-74 staff_position_internal — position_override;
        // LILYPOND-REF: lily/rest-collision.cc:228-233 calc_positioning_done — "Do not
        // compute a translation for pre-positioned rests".
        for (int v = 0; v < staff.Voices.Length; v++)
        {
            var measures = staff.Voices[v].Measures;
            for (int m = 0; m < measures.Length; m++)
            {
                var items = measures[m].Items;
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] is not RestItem { StaffPosition: not null } pitched)
                        continue;
                    int pitchedValue = GlyphMetrics.NoteValueOf(pitched.BaseDuration);
                    // The renderer draws the neutral letter unshifted, so the shift is
                    // the distance from it — on five lines the +2 a semibreve gets from
                    // its written position and the +2 in the neutral letter cancel,
                    // leaving the written position itself for every duration; on a staff
                    // whose neutral letter is elsewhere (NeutralRestPosition) the
                    // difference carries the semibreve's +2 through, as LilyPond's
                    // position_override arm hands it out whatever the staff.
                    shifts[new RestShiftKey(m, v, i)] =
                        RestStaffPosition(pitched, pitched.VoiceDirection, pitchedValue, staff.Lines)
                        - NeutralRestPosition(staff.Lines, pitchedValue);
                }
            }
        }

        AddGraceRestCollisions(staff, shifts);

        if (staff.Voices.Length < 2)
            return shifts.ToImmutableDictionary();

        int measureCount = staff.Voices.Min(v => v.Measures.Length);

        for (int m = 0; m < measureCount; m++)
        {
            // What each voice sounds at each moment of this measure, so "at the same time"
            // is answered by the music rather than by item index — the voices need not have
            // the same number of items.
            var byVoice = new List<(Fraction Time, MusicItem Item, int ItemIndex)>[staff.Voices.Length];
            for (int v = 0; v < staff.Voices.Length; v++)
            {
                var list = new List<(Fraction, MusicItem, int)>();
                var t = new Fraction(0, 1);
                var items = staff.Voices[v].Measures[m].Items;
                for (int i = 0; i < items.Length; i++)
                {
                    list.Add((t, items[i], i));
                    t += GetItemDuration(items[i]);
                }
                byVoice[v] = list;
            }

            for (int v = 0; v < staff.Voices.Length; v++)
            {
                foreach (var (time, item, itemIndex) in byVoice[v])
                {
                    if (item is not RestItem rest || rest.IsSpacer || rest.IsMultiMeasure)
                        continue;
                    // A grace rest was answered by AddGraceRestCollisions.
                    if (rest.GraceTime)
                        continue;

                    // Pre-positioned rests were placed above and take no translation.
                    // LILYPOND-REF: lily/rest-collision.cc:228-233 calc_positioning_done.
                    if (rest.StaffPosition is not null)
                        continue;

                    // The rest's direction is the one the collector STAMPED on it
                    // (ResolveVoiceStemDirections — make-voice-props-set reaches Rest),
                    // scoped to the span's actual reach; zero means the rest is outside
                    // every span and takes no voiced displacement at all. Re-deriving
                    // the measure-granular voice default here instead voiced the
                    // trailing rest AFTER a span closed mid-measure
                    // (collision-harmonic-no-dots.ly: its r4 sat two spaces high where
                    // LilyPond leaves it on the middle line — probe vrest-probe.ly) and
                    // was a SECOND spelling of the answer ItemSkylineFactory already
                    // reads off the model.
                    // LILYPOND-REF: lily/rest.cc:224-226 — the Rest's own direction,
                    // the note column's only as fallback.
                    if (rest.VoiceDirection == 0)
                        continue;
                    int dir = rest.VoiceDirection;
                    bool voiceUp = dir > 0;

                    // The rest STARTS at its voiced position (dir × 4, line-aligned per
                    // duration over THIS staff's lines) — rest.cc's
                    // staff_position_internal — and everything below translates from
                    // there. The renderer's default is the NEUTRAL letter
                    // (NeutralRestPosition: the middle, a whole hanging from the line
                    // above it), so the emitted shift carries the base displacement too.
                    int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    double basePos = RestStaffPosition(rest, dir, restValue, staff.Lines);
                    double defaultPos = NeutralRestPosition(staff.Lines, restValue);

                    // The rest's own ink, in staff POSITIONS about the middle line, at
                    // its voiced place (a whole rest's glyph hangs from basePos).
                    var box = GlyphMetrics.GetRestBBox(restValue);
                    double restLow = basePos + box.Bottom * 2.0;
                    double restHigh = basePos + box.Top * 2.0;

                    // The other voices' notes SOUNDING at this moment — started here or
                    // earlier and still held.
                    double noteLow = double.PositiveInfinity, noteHigh = double.NegativeInfinity;
                    for (int o = 0; o < staff.Voices.Length; o++)
                    {
                        if (o == v) continue;
                        foreach (var (otherTime, otherItem, otherIndex) in byVoice[o])
                        {
                            if (otherItem is not (NoteItem or ChordItem))
                                continue;
                            bool otherUp = VoiceDefaults.GetDefaultStemUpAt(staff.Voices, o, m, otherIndex)
                                ?? (o % 2 == 0);
                            if (otherTime > time
                                || otherTime + GetItemDuration(otherItem) <= time)
                                continue;
                            foreach (double p in StaffPositionsOf(otherItem))
                            {
                                // Head ink is ±0.545 ss = ±1.09 positions about its centre.
                                double half = EngravingDefaults.NoteheadHalfHeight * 2.0;
                                noteLow = Math.Min(noteLow, p - half);
                                noteHigh = Math.Max(noteHigh, p + half);
                            }
                            // Same direction at the SAME moment: LilyPond unites the whole
                            // COLUMN, so the stem counts too. A note that merely holds over
                            // from an earlier moment stays head-only, whatever its side
                            // (the different-column arm of :246-265).
                            if (otherUp == voiceUp && otherTime == time)
                            {
                                double tip = StemTipPositionOf(otherItem, otherUp);
                                noteLow = Math.Min(noteLow, tip);
                                noteHigh = Math.Max(noteHigh, tip);
                            }
                        }
                    }

                    double discrete = 0.0;
                    if (!double.IsInfinity(noteLow))
                    {
                        double minimumDist = RestCollisionMinimumDistance * 2.0;  // ss → positions
                        double restNear = dir > 0 ? restLow : restHigh;
                        double noteFar = dir > 0 ? noteHigh : noteLow;
                        double y = dir * Math.Max(0.0, -dir * restNear + dir * noteFar + minimumDist);

                        // Half spaces first (a position IS a half space, so this is a ceil to 1).
                        discrete = dir * Math.Ceiling(dir * y);

                        // ...then whole spaces while the rest is still inside the staff,
                        // widened by one position on each side.
                        if (basePos + discrete >= -5.0 && basePos + discrete <= 5.0)
                            discrete = dir * Math.Ceiling(dir * discrete / 2.0) * 2.0;
                    }

                    double shift = basePos - defaultPos + discrete;
                    if (shift == 0.0)
                        continue;
                    var key = new RestShiftKey(m, v, itemIndex);
                    if (!shifts.TryGetValue(key, out var existing)
                        || Math.Abs(shift) > Math.Abs(existing))
                        shifts[key] = shift;
                }
            }
        }

        return shifts.ToImmutableDictionary();
    }

    /// <summary>
    /// A GRACE rest pushed clear of its own voice's note that still sounds at the grace's
    /// moment — the main note before it, which LilyPond's Rest_collision_engraver counts as
    /// busy: the grace sits at (X, −g), before that note's end (X, 0). MEASURED (Lab
    /// sessions/p723/gr/lpx, LilyPond 2.26.0): `e''4 \grace { r16 f''16 } g''4` lifts the
    /// rest +5 spaces, `e'4 …` +2, `c'4 \grace { r8 } c'4` 0 — each the rests-and-notes
    /// branch's arithmetic; an ordinary rest after the same note does not move.
    /// </summary>
    /// <remarks>
    /// The DIRECTION is why a grace rest moves and an ordinary one does not: the rest has
    /// none, so Rest_collision falls back to its column's, which is its stem's — and a grace
    /// stem is UP (score-grace-settings) — in a lower voice the rest carries the voice's own
    /// direction (VoiceDirection), as its grace stems do. An ordinary rest's column has no direction and
    /// takes no translation. The rest's own position stays the unvoiced one.
    /// LILYPOND-REF: lily/rest-collision-engraver.cc:55-80 process_acknowledged — busyGrobs,
    ///   "Include notes that started any time";
    /// LILYPOND-REF: lily/rest-collision.cc:222-284 calc_positioning_done — dir from the rest,
    ///   else Note_column::dir; a note of another column counts by its head;
    /// LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings — Stem direction UP.
    /// ⚠️ NOT PORTED: another VOICE's notes at that moment (a grace rest under polyphony);
    /// no corpus book has one.
    /// </remarks>
    private static void AddGraceRestCollisions(Staff staff, Dictionary<RestShiftKey, double> shifts)
    {
        for (int v = 0; v < staff.Voices.Length; v++)
        {
            var measures = staff.Voices[v].Measures;
            for (int m = 0; m < measures.Length; m++)
            {
                var items = measures[m].Items;
                for (int i = 0; i < items.Length; i++)
                    if (GraceRestShift(measures, m, i, staff.Lines) is { } shift && shift != 0.0)
                        shifts[new RestShiftKey(m, v, i)] = shift;
            }
        }
    }

    /// <summary>
    /// The shift, in staff positions from the neutral letter, of the grace rest at item
    /// <paramref name="i"/> of bar <paramref name="m"/> (<see cref="AddGraceRestCollisions"/>'s
    /// rule), or null when the item is no unpitched grace rest. The one spelling: the slur's
    /// obstacle for the rest (AddGraceObstaclesForMeasure) reads it too.
    /// </summary>
    internal static double? GraceRestShift(ImmutableArray<Measure> measures, int m, int i, int staffLines)
    {
        var items = measures[m].Items;
        if (i < 0 || i >= items.Length
            || items[i] is not RestItem { GraceTime: true, IsSpacer: false, IsMultiMeasure: false, StaffPosition: null } rest)
            return null;
        int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
        double basePos = RestStaffPosition(rest, rest.VoiceDirection, restValue, staffLines);
        double defaultPos = NeutralRestPosition(staffLines, restValue);

        // The note still sounding: the voice's last main (non-grace) item before the rest,
        // in this bar or at the end of the one before.
        MusicItem? held = null;
        for (int j = i - 1; j >= 0 && held is null; j--)
            if (!items[j].GraceTime)
                held = items[j];
        if (held is null && m > 0)
        {
            var prev = measures[m - 1].Items;
            for (int j = prev.Length - 1; j >= 0 && held is null; j--)
                if (!prev[j].GraceTime)
                    held = prev[j];
        }
        if (held is not (NoteItem or ChordItem))
            return basePos - defaultPos;

        int dir = rest.VoiceDirection != 0 ? rest.VoiceDirection : 1;
        var box = GlyphMetrics.GetRestBBox(restValue);
        double restNear = dir > 0 ? basePos + box.Bottom * 2.0 : basePos + box.Top * 2.0;
        double half = EngravingDefaults.NoteheadHalfHeight * 2.0;
        double noteFar = dir > 0
            ? StaffPositionsOf(held).Max() + half
            : StaffPositionsOf(held).Min() - half;
        double y = dir * Math.Max(0.0,
            -dir * restNear + dir * noteFar + RestCollisionMinimumDistance * 2.0);
        double discrete = dir * Math.Ceiling(dir * y);
        if (basePos + discrete >= -5.0 && basePos + discrete <= 5.0)
            discrete = dir * Math.Ceiling(dir * discrete / 2.0) * 2.0;
        return basePos - defaultPos + discrete;
    }

    /// <summary>RestCollision's <c>minimum-distance</c>, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2981-2984 RestCollision
    /// <c>minimum-distance</c> = 0.75, read by rest-collision.cc:239-241.</remarks>
    private const double RestCollisionMinimumDistance = 0.75;

    /// <summary>
    /// Where a rest STARTS, before any collision moves it: the pitch it was written at
    /// when it was written at one (<c>a4@rest</c>), and otherwise the voiced position
    /// below.
    /// </summary>
    /// <remarks>
    /// The two arms are <c>staff_position_internal</c>'s own, in its order: a numeric
    /// <c>staff-position</c> is taken verbatim — no voiced position, no aligning to a
    /// line ("trust the client on good positioning") — except that a semibreve still
    /// hangs one line above whatever it was given, the same +2 the unpitched arm ends
    /// with. Every reader of a rest's pure position comes through here, so a pitched
    /// rest is placed once and the spacing, the dot column and the print all see it.
    /// LILYPOND-REF: lily/rest.cc:53-74 staff_position_internal — position_override.
    /// </remarks>
    internal static double RestStaffPosition(RestItem rest, int dir, int restValue, int staffLines) =>
        rest.StaffPosition is { } written
            ? (restValue == 1 ? written + 2.0 : written)
            : VoicedRestPosition(dir, restValue, staffLines);

    /// <summary>
    /// Where a rest RESTS when nothing voices or pitches it — the NEUTRAL letter every
    /// rest shift in this class is measured from and the renderer draws unshifted:
    /// <c>staff_position_internal</c> at direction CENTER, from voiced position 0. A
    /// quarter or shorter stays on the middle line; a half (or breve) sits on the last
    /// staff line at or below it, else the lowest line; a whole hangs from the first
    /// line above it, else the top line; a breve on a ONE-line staff hangs two below it.
    /// </summary>
    /// <remarks>
    /// The lines are THE STAFF'S DRAWN ONES (<see cref="EngravingDefaults.StaffLinePositions"/>).
    /// On five lines this is the "+2 for a whole, 0 otherwise" that used to be spelled
    /// inline at every reader; on one line the whole rest hangs from THE line (the space
    /// above it is no line), and on the timbales pair (±2) the half rest sits on the LOWER
    /// line, the middle being no line. Until 2026-09-23 every reader assumed five lines
    /// (the owner's oneline-rest.lys: the whole rest floated a space above the single
    /// line, the half rest a space above the pair's lower line — the same music on three,
    /// four and five lines was right, because there the five-line letter happens to land
    /// on a drawn line).
    /// LILYPOND-REF: lily/rest.cc:76-81 staff_position_internal — vpos 0 at CENTER, and no
    ///   line alignment past a quarter; :90-97 the staff's line-positions and the one-line
    ///   breve arm; :101-121 the semibreve (upper_bound, else back()); :122-129 every
    ///   longer rest (upper_bound, then one back); :131-133 "Finished for neutral position".
    /// </remarks>
    internal static double NeutralRestPosition(int staffLines, int restValue)
        => NeutralRestPosition(EngravingDefaults.StaffLinePositions(staffLines), restValue);

    /// <summary><see cref="NeutralRestPosition(int, int)"/> over the staff's drawn line
    /// positions themselves (ascending) — a tab's strings are not the notation table's lines
    /// (<c>MultiStaffLayouter.LinePositionsOf</c>).</summary>
    internal static double NeutralRestPosition(ReadOnlySpan<double> lines, int restValue)
    {
        if (restValue >= 4)   // duration_log > 1: no line alignment
            return 0.0;
        // rest.cc:96-97 — a breve (duration_log < 0) on a single line, neutral direction.
        if (lines.Length == 1 && restValue == 0)
            return lines[0] - 2.0;
        return AlignRestToLine(lines, 0.0, restValue);
    }

    /// <summary>
    /// rest.cc:101-129 — the line a rest longer than a quarter takes from a position:
    /// a whole hangs from the first line strictly above <paramref name="pos"/> (the top
    /// line when there is none); anything else sits on the last line at or below it (the
    /// bottom line when there is none).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/rest.cc:115-120 <c>std::upper_bound</c>, else
    /// <c>linepos.back()</c>; :124-128 <c>upper_bound</c> then <c>--it</c> unless at
    /// <c>begin()</c>. The list is ascending, as LilyPond sorts it (:99).</remarks>
    private static double AlignRestToLine(ReadOnlySpan<double> lines, double pos, int restValue)
    {
        int it = 0;   // std::upper_bound: the first line strictly above pos
        while (it < lines.Length && lines[it] <= pos)
            it++;
        if (restValue == 1)
            return it < lines.Length ? lines[it] : lines[^1];
        return lines[it > 0 ? it - 1 : 0];
    }

    /// <summary>
    /// The staff position a voiced rest STARTS at, on a staff of <paramref name="staffLines"/>
    /// lines: <c>dir × voiced-position</c> (4); a quarter or shorter takes it as-is; a half
    /// aligns down to the nearest line at or below; a whole first drops one line in a
    /// lower voice, then hangs from the next line above (the top line when there is
    /// none). The proper-side check against the neutral letter
    /// (<see cref="NeutralRestPosition(int, int)"/>) is the tail of the same function, and direction
    /// CENTER IS the neutral letter.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/rest.cc:46-141 staff_position_internal (the
    /// unpitched arm); LILYPOND-REF: scm/define-grobs.scm Rest — voiced-position 4.
    /// <para>
    /// <see cref="ItemSkylineFactory"/> prices a voiced rest's separation box at this same
    /// position (the PURE side of the offset chain — the collision push is unpure).
    /// </para>
    /// </remarks>
    internal static double VoicedRestPosition(int dir, int restValue, int staffLines)
        => VoicedRestPosition(dir, restValue, EngravingDefaults.StaffLinePositions(staffLines));

    /// <summary><see cref="VoicedRestPosition(int, int, int)"/> over the staff's drawn line
    /// positions themselves (see <see cref="NeutralRestPosition(ReadOnlySpan{double}, int)"/>).</summary>
    internal static double VoicedRestPosition(int dir, int restValue, ReadOnlySpan<double> lines)
    {
        const double VoicedPosition = 4.0;
        double pos = dir * VoicedPosition;
        if (restValue >= 4)   // duration_log > 1: no line alignment
            return pos;
        // rest.cc:131-133 — the neutral direction is finished at the aligned position,
        // which is the neutral letter itself (and the one-line breve arm lives there).
        if (dir == 0)
            return NeutralRestPosition(lines, restValue);

        // Whole: "lower voice semibreve rests generally hang a line lower" (:107-108),
        // then from the next available line; half (and breve): the line at or below.
        if (restValue == 1 && dir < 0)
            pos -= 2;
        pos = AlignRestToLine(lines, pos, restValue);

        // Keep the voiced position only on the proper side of the neutral one (:139-144).
        double neutral = NeutralRestPosition(lines, restValue);
        return dir * (pos - neutral) > 0 ? pos : neutral + dir * VoicedPosition;
    }

    /// <summary>
    /// Each dotted REST's augmentation-dot position, in staff positions RELATIVE to the
    /// rest's own glyph origin, from solving the dot COLUMN the rest shares with the other
    /// voices' dotted items at the same musical moment. Memoised per <see cref="Staff"/>
    /// (the answer is a function of the music alone) so the renderer, the skyline seed and
    /// any later consumer read ONE answer without re-running the whole-staff scan.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/dot-column.cc:143-150, 194-227 calc_positioning_done — dots
    /// enter the configuration at their PURE positions and a rest's pure position is its
    /// VOICED one (<see cref="VoicedRestPosition(int, int, int)"/>): the Rest_collision and beam pushes
    /// are unpure, and the dot, whose Y-parent is the rest, RIDES them afterwards — which
    /// is why the emitted answer is relative to the rest, not absolute.
    /// LILYPOND-REF: scm/output-lib.scm:652-664 dots::calc-staff-position — a log 2..4
    /// rest's dot starts AT the rest's position (offset 0; a semibreve's at −2 relative to
    /// its hanging origin is the −1 the renderer's default arm keeps).
    /// <para>
    /// dot-column-vertical-positioning.ly is the pin: its r8. dot lands DOWN (pure +4 → +3)
    /// only because the f'8. dot in the same column already holds +5 — shifting the rest
    /// dot UP would cascade the note dot to +7 (badness 20 against 5). Solo, the same dot
    /// goes UP, which is what the old fixed "one position above the origin" rule happened
    /// to reproduce. The rest dot then rode the rest's unpure +10 to LilyPond's +13.
    /// </para>
    /// <para>
    /// ⚠️ TIES IN THE INSERTION ORDER ARE THE VOICE ORDER, AND THAT IS MEASURED, NOT
    /// DERIVED: LilyPond sorts its dots with <c>std::sort</c> over pure positions
    /// (dot-column.cc:150), whose order on EQUAL keys is unspecified — nothing in the
    /// source promises the acknowledgment order survives. What is known is the
    /// rendering: dot-column-vertical-positioning.ly lands only if the NOTE dot is
    /// inserted first (the reversed order settles on note +3, rest +5 instead), and
    /// voice order reproduces it. If another book measures the opposite on some other
    /// tie, this ordering — not the badness — is the suspect.
    /// </para>
    /// <para>
    /// ⚠️ NOTE dots are read here only as column NEIGHBOURS; the renderer keeps its
    /// per-item <see cref="DotConfiguration.Resolve"/> for them. A cascade that moves a
    /// NOTE's dot (two dotted items colliding across voices) would disagree with that
    /// per-item answer — no corpus book binds one yet, and the seam is named here rather
    /// than discovered. The note-collision DotAdjustment direction override is likewise
    /// not read (voice-default directions only, as the renderer's fallback arm).
    /// </para>
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Staff, ImmutableDictionary<RestShiftKey, int>> _restDotOffsets = new();

    internal static ImmutableDictionary<RestShiftKey, int> RestDotOffsetsOf(Staff staff)
        => _restDotOffsets.GetValue(staff, CalculateRestDotOffsets);

    /// <summary>
    /// The solo answer of the same solve, for rests the table holds no entry for: one
    /// position up off the origin's line — one position DOWN for a hanging semibreve,
    /// whose origin is already the line above. ONE home: the renderer's DrawRest and the
    /// skyline's rest-dot seed both read this, so the default letter cannot fork.
    /// LILYPOND-REF: scm/output-lib.scm:652-664 dots::calc-staff-position;
    /// lily/dot-column.cc:194-227 calc_positioning_done (the on-line remove_collision).
    /// </summary>
    internal static int RestDotDefaultOffset(int restValue) => restValue == 1 ? -1 : 1;

    private static ImmutableDictionary<RestShiftKey, int> CalculateRestDotOffsets(Staff staff)
    {
        if (staff.Voices.Length < 2)
            return ImmutableDictionary<RestShiftKey, int>.Empty;

        var offsets = ImmutableDictionary.CreateBuilder<RestShiftKey, int>();
        int measureCount = staff.Voices.Min(v => v.Measures.Length);

        for (int m = 0; m < measureCount; m++)
        {
            // Dotted items by onset, voices in order — the column is "starts at the same
            // moment": a held note's dot lives at its own earlier column (LP acknowledges
            // grobs during their timestep), so onset equality is the membership.
            Dictionary<Fraction, List<(int Voice, MusicItem Item, int ItemIndex)>>? moments = null;
            bool anyDottedRest = false;
            for (int v = 0; v < staff.Voices.Length; v++)
            {
                var t = new Fraction(0, 1);
                var items = staff.Voices[v].Measures[m].Items;
                for (int i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    bool dotted = item switch
                    {
                        RestItem r => r.Dots > 0 && !r.IsSpacer && !r.IsMultiMeasure,
                        NoteItem n => n.Dots > 0,
                        ChordItem c => c.Dots > 0,
                        _ => false,
                    };
                    if (dotted)
                    {
                        moments ??= new Dictionary<Fraction, List<(int, MusicItem, int)>>();
                        if (!moments.TryGetValue(t, out var list))
                            moments[t] = list = new List<(int, MusicItem, int)>();
                        list.Add((v, item, i));
                        anyDottedRest |= item is RestItem;
                    }
                    t += GetItemDuration(item);
                }
            }
            if (!anyDottedRest)
                continue;

            foreach (var column in moments!.Values)
            {
                if (!column.Exists(e => e.Item is RestItem))
                    continue;

                var positions = new List<int>();
                var dirs = new List<int>();
                var restSlots = new List<(int InputIndex, int Voice, int ItemIndex, int Pure)>();
                foreach (var (v, item, i) in column)
                {
                    bool voiceUp = VoiceDefaults.GetDefaultStemUpAt(staff.Voices, v, m, i) ?? (v % 2 == 0);
                    int dir = voiceUp ? 1 : -1;
                    switch (item)
                    {
                        case RestItem rest:
                            // Pure position from the STAMPED direction (zero = outside
                            // every span → the neutral origin), the same slot
                            // ItemSkylineFactory prices — not a re-derived voice default.
                            int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                            int pure = rest.VoiceDirection == 0 && rest.StaffPosition is null
                                ? (int) NeutralRestPosition(staff.Lines, restValue)
                                : (int) RestStaffPosition(rest, rest.VoiceDirection, restValue, staff.Lines);
                            restSlots.Add((positions.Count, v, i, pure));
                            positions.Add(pure);
                            dirs.Add(0);   // a rest's dot declares no direction (dp.dir_
                                           // is set for note heads only, dot-column.cc:203-205)
                            break;
                        case NoteItem note:
                            positions.Add(note.StaffPosition);
                            dirs.Add(dir);
                            break;
                        case ChordItem chord:
                            foreach (var n in chord.Notes)
                            {
                                positions.Add(n.StaffPosition);
                                dirs.Add(dir);
                            }
                            break;
                    }
                }

                var solved = DotConfiguration.Resolve(positions, dirs);
                foreach (var (idx, v, i, pure) in restSlots)
                    offsets[new RestShiftKey(m, v, i)] = solved[idx] - pure;
            }
        }

        return offsets.ToImmutable();
    }

    /// <summary>Staff positions of every head in a note or chord.</summary>
    private static IEnumerable<double> StaffPositionsOf(MusicItem item) => item switch
    {
        NoteItem n => new[] { (double) n.StaffPosition },
        ChordItem c => c.Notes.Select(n => (double) n.StaffPosition),
        _ => Enumerable.Empty<double>(),
    };

    /// <summary>
    /// Staff position of a stem's far tip, for the one case LilyPond unites the whole note
    /// column rather than just its head.
    /// </summary>
    /// <remarks>
    /// The fixed <see cref="EngravingDefaults.DefaultStemLength"/> rather than the quanted
    /// one, for the reason rest-collision.cc:254-259 gives for avoiding the stem entirely:
    /// asking a beam for its position here would force beam layout early. A whole note has
    /// no stem at all (lily/stem.cc <c>Stem::is_normal_stem</c>).
    /// </remarks>
    private static double StemTipPositionOf(MusicItem item, bool stemUp)
    {
        int noteValue = item switch
        {
            NoteItem n => GlyphMetrics.NoteValueOf(n.BaseDuration),
            ChordItem c => GlyphMetrics.NoteValueOf(c.BaseDuration),
            _ => 1,
        };
        if (noteValue < 2)
            return stemUp ? double.NegativeInfinity : double.PositiveInfinity;
        var positions = StaffPositionsOf(item).ToList();
        if (positions.Count == 0)
            return stemUp ? double.NegativeInfinity : double.PositiveInfinity;
        double root = stemUp ? positions.Max() : positions.Min();
        // THE STEM THE RENDERER DRAWS — duration-dependent length, unnatural-side
        // shortening and the reach-the-middle-line rule — not a fixed default. A
        // fixed 3.5 ss here overshot a half note's 3.0 ss stem and pushed the
        // colliding rest one half-space too far (rest-avoid-note.ly, the lower
        // voice's r2 against g2: LilyPond lands at −11, the fixed length said −12).
        // Frame adapter only: StemCalculator is device (Y-down); positions are
        // Y-up halves of a staff space about the middle line.
        // ⚠️ A BEAMED note's drawn stem ends where the beam does, which this
        // unbeamed formula cannot know — LilyPond reads the column's extent there.
        // Only the same-direction-same-moment arm ever reads a stem at all, and no
        // corpus book puts a beamed column in it; disclosed, not solved.
        int durLog = StemCalculator.GetDurationLog(noteValue);
        const double mid = 10.0;                      // arbitrary device middle
        double deviceNoteY = mid - root / 2.0;
        double deviceStaffTop = mid - EngravingDefaults.StaffMiddle;  // staff top = +4 positions
        double deviceTipY = StemCalculator.CalculateStemEndY(
            deviceNoteY, stemUp, deviceStaffTop, durLog, (int)Math.Round(root));
        return (mid - deviceTipY) * 2.0;
    }

    /// <summary>
    /// Computes the X offset (within the measure) of the item at <paramref name="itemIndex"/>
    /// in the given voice. For multi-staff scores, <see cref="MeasureLayout.Items"/> contains only
    /// the primary staff's items, so per-voice spanners (ties/slurs in non-primary staves) must
    /// instead resolve their X via timing → <see cref="MeasureLayout.Columns"/>.
    /// </summary>
    private static double GetItemXOffset(
        Voice voice, int measureIndex, int itemIndex, MeasureLayout measureLayout)
        => LayoutUtilities.GetItemXOffset(voice.Measures, measureIndex, itemIndex, measureLayout);

    /// <summary>
    /// Within-chord horizontal displacement (staff spaces) of the note at
    /// <paramref name="staffPosition"/> inside the item at <paramref name="itemIndex"/>,
    /// or 0 when the item is a single note or the chord has no second/unison that
    /// reverses a head to the far side of the stem. This mirrors the per-head offset
    /// the renderer applies (<see cref="ChordHeadPositioning.CalculateOffsets(System.Collections.Generic.IReadOnlyList{LilySharp.Core.Svg.Model.ChordNoteInfo},bool,int,GlyphMetrics.DesignMetrics?)"/>) so a
    /// tie or slur attaches to the DISPLACED head's edge, not the undisplaced chord
    /// column. Without it, a tie/slur on the reversed head of a seconds chord starts
    /// inside its own head and fails to reach the matching head at the other end.
    /// LILYPOND-REF: lily/stem.cc Stem::calc_positioning_done; the tie/slur outline
    /// attachment follows the note head's actual X (lily/tie-formatting-problem.cc).
    /// </summary>
    private static double GetChordHeadXOffset(
        Voice voice, int measureIndex, int itemIndex, int staffPosition)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
            return 0;
        var measure = voice.Measures[measureIndex];
        if (itemIndex < 0 || itemIndex >= measure.Items.Length)
            return 0;
        if (measure.Items[itemIndex] is not ChordItem chord)
            return 0;
        int noteValue = GlyphMetrics.NoteValueOf(chord.BaseDuration);
        // The head's own FONT, not the twenty's box times a scale — see ChordHeadPositioning.
        var offsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, chord.StemUp, noteValue, SpacingRules.HeadFontOf(chord));
        for (int i = 0; i < chord.Notes.Length; i++)
            if (chord.Notes[i].StaffPosition == staffPosition)
                return offsets[i];
        return 0;
    }

    /// <summary>
    /// One bound COLUMN of a tie, reduced to the boxes LilyPond's
    /// <c>set_column_chord_outline</c> walks — every head of the chord, the stem, the dots,
    /// the flag, the accidentals — in the tie problem's frame (page X; Y in staff spaces above
    /// the middle line, up-positive). Null when the item is not a note column.
    /// </summary>
    /// <param name="columnX">The column's X: the LEFT edge of an UNDISPLACED head.</param>
    /// <param name="tiedPositions">
    /// The staff positions this column's ties attach to — LilyPond's <c>bounds</c>. It is the
    /// whole tie COLUMN's, not this one tie's, because the recession boxes and
    /// <c>head_extents_</c> are built from all of them (tie-formatting-problem.cc:243-258, :282-286).
    /// </param>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:96-287 set_column_chord_outline.
    /// </remarks>
    internal static TieColumnParts? BuildTieColumn(
        Voice voice, int measureIndex, int itemIndex, double columnX,
        IReadOnlyList<int> tiedPositions, bool isLeftBound, bool atLineStart = false)
    {
        var item = ItemAt(voice, measureIndex, itemIndex);
        if (item is not (NoteItem or ChordItem))
            return null;
        // The right head of a tie broken by the line stands on the line-start column and
        // draws the accidental its tie kept (Model.TiedAccidentals), which the outline has to
        // clear: tie-formatting-problem.cc:226-236 set_column_chord_outline boxes a LIVE
        // accidental on the right head.
        if (atLineStart)
            item = TiedAccidentals.LineStartView(item);

        int noteValue = GlyphMetrics.NoteValueOf(
            item is ChordItem c0 ? c0.BaseDuration : ((NoteItem)item).BaseDuration);
        // The head's INK EXTENT, not its advance. LilyPond's outline boxes each head with
        // head->extent (x_refpoint_, X_AXIS) (:119), a stencil extent; the two differ by
        // 0.000200 on a black head and 0.001400 on a half one. It is invisible on a tie whose
        // BOTH ends recede to a head centre — the whole span shifts and the width does not —
        // and it is 0.000700 on one whose other end is held by the stem.
        // LILYPOND-REF: lily/tie-formatting-problem.cc:119 set_column_chord_outline.
        // A cue head's own box (BowHeadBox): a tie out of `cue { e2~ e2 }` began 0.2511 right of
        // LilyPond's with the twenty's box (Lab sessions/p656, S1 bow-cue).
        var headBBox = BowHeadBox(item, noteValue);
        double headLeftInk = headBBox.Left;
        double headRightInk = headBBox.Right;
        bool stemUp = item is ChordItem c1 ? c1.StemUp : ((NoteItem)item).StemUp;

        // Every head of the column, with the seconds displacement the renderer draws it at.
        // One entry per chord member, or one for a lone note — the branch's own trip count
        // (measured before it was handed over: 26,524 calls, asked == Count every time).
        int headCount = item is ChordItem countChord ? countChord.Notes.Length : 1;
        var positions = new List<int>(headCount);
        var offsets = new List<double>(headCount);
        if (item is ChordItem chord)
        {
            // The OFFSETS come out of the cue's own font, as headBBox above does since session
            // 656 (BowHeadBox) — a cue chord's tie column reads one size.
            var chordOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue, SpacingRules.HeadFontOf(chord));
            for (int i = 0; i < chord.Notes.Length; i++)
            {
                positions.Add(chord.Notes[i].StaffPosition);
                offsets.Add(chordOffsets[i]);
            }
        }
        else
        {
            positions.Add(((NoteItem)item).StaffPosition);
            offsets.Add(0);
        }

        // bounds vs the rest. TiedHeads must come out sorted by position ASCENDING — the
        // recession boxes take the vector's ends and not its extremes by Y.
        // The tied heads are a SUBSET of the column's heads, so positions.Count is a bound —
        // and on the reader's corpus it is exact: measured before it was handed over, all
        // 26,524 calls tied every head of the column (a tie column is named by the chord the
        // ties leave, so an untied member is possible but rare enough never to appear there).
        // ⚠️ `others` IS NULL UNTIL AN UNTIED MEMBER ARRIVES, and over the reader's corpus one
        // never does: session 442's leg proved all 26,524 calls tie every head of the column
        // (tied == positions.Count), and session 448's census then priced the four lists this
        // method builds per call — `others`, `dots`, `flag`, `accidentals` — at 0.00, 0.00,
        // 0.21 and 0.04 items each. They are HANDOFF 1.0 (n)^5: a family the capacity-based
        // accounting cannot see, because an empty List's capacity is zero and only its OBJECT
        // is on the books. TieColumnParts already defaults every one of them to [], so null
        // here is the same answer (session 451).
        var tied = new List<TieOutlineHead>(positions.Count);
        List<TieOutlineBox>? others = null;
        for (int i = 0; i < positions.Count; i++)
        {
            double left = columnX + offsets[i] + headLeftInk;
            double right = columnX + offsets[i] + headRightInk;
            if (tiedPositions.Contains(positions[i]))
                tied.Add(new TieOutlineHead(positions[i], left, right));
            else
                // An UNTIED chord member enters with its own ink height, not the tied heads'
                // one-staff-space box (:221, Staff_symbol_referencer::extent_in_staff).
                (others ??= []).Add(new TieOutlineBox(
                    positions[i] * 0.5 + headBBox.Bottom, positions[i] * 0.5 + headBBox.Top,
                    left, right));
        }
        if (tied.Count == 0)
            return null;
        // Equal positions (a UNISON pair) fall back to X: LilyPond's bounds order is the
        // ties' order, and its unison pair always has the RIGHT head second (the second
        // member is the one displaced, and it goes to the right for either stem direction),
        // so boundary(head_boxes, UP) reads the RIGHT head's centre. Lily#'s member order
        // has the MAIN head first — for a down-stem chord that is the RIGHT one — so without
        // the tiebreak the recession boxes swap heads and the up tie of <f f>~<f f> attaches
        // a head-shift too far left (measured on chord-X-align-on-main-noteheads).
        // A literal port would keep the MEMBER order and instead mirror LilyPond's unison
        // head placement (second member displaced rightward); sorting by page X reproduces
        // the same order without touching how ChordHeadPositioning assigns the offside head.
        // LILYPOND-REF: lily/tie-formatting-problem.cc:243-258 set_column_chord_outline —
        //   boundary picks the head_boxes vector's ENDS by order, not extremes by Y (:50-54)
        tied.Sort((a, b) => a.Position != b.Position
            ? a.Position.CompareTo(b.Position)
            : a.XLeft.CompareTo(b.XLeft));

        // The stem. StemSpacingInfo is null exactly when LilyPond's Stem::is_normal_stem is
        // false (a whole note), which is the branch that boxes a half-plane instead of a shaft.
        int lowPos = positions.Min(), highPos = positions.Max();
        int supportPos = stemUp ? lowPos : highPos;
        double supportLeft = columnX + offsets[positions.IndexOf(supportPos)];
        // The stem's x, through the one house — which reads the SUPPORT HEAD'S OWN attachment
        // (per head shape) as LilyPond does, so this is :149's own quantity and no longer a
        // Lily#-side stem the tie has to be told about. It was LILYSHARP-OWN until 2026-08-03,
        // when LayoutUtilities.StemAttachX stopped answering with the black head's 1.304200
        // for every head; that divergence was the whole of what ledger tie.width.seconds.upper
        // had left (-0.073200 = 1.377400 - 1.304200 on this book's HALF-note chord).
        // LILYPOND-REF: lily/tie-formatting-problem.cc:149 Tie_formatting_problem::set_column_chord_outline
        //   — stem->relative_coordinate (x_refpoint_, X_AXIS).
        var stemInfo = SpacingRules.StemSpacingInfo(item);
        var stem = new TieOutlineStem(
            IsNormal: stemInfo is not null,
            CentreX: LayoutUtilities.StemX(supportLeft, stemUp, noteValue,
                LayoutUtilities.NoteheadStyleOf(item)),
            TipY: stemInfo is { } si ? (stemUp ? si.StemMax : si.StemMin) * 0.5 : 0,
            NearHeadPosition: supportPos,
            SupportHeadCentreX: supportLeft + (headLeftInk + headRightInk) / 2.0);

        // The dots hang off the column's rightmost head, and only the LEFT bound meets them.
        // Where they stand is the reserved dot column — DotColumn.Reserved, the house the
        // renderer draws by and the spacing box reserves by: head ink, one dot width, a flag's
        // push, on DotConfiguration's rows (a line-note's dot lifted into the space).
        List<TieOutlineBox>? dots = null;
        int dotCount = SpacingRules.GetDots(item);
        if (isLeftBound && dotCount > 0)
        {
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotRadius = dotBBox.Height / 2;
            var (dotOffset, rows) = DotColumn.Reserved(item, noteValue, offsets.Max() + headRightInk);
            foreach (int p in rows)
            {
                double dotY = p * 0.5;
                for (int d = 0; d < dotCount; d++)
                {
                    double dotX = columnX + dotOffset + d * 2 * dotBBox.Width;
                    (dots ??= []).Add(new TieOutlineBox(
                        dotY - dotRadius, dotY + dotRadius, dotX, dotX + dotBBox.Width));
                }
            }
        }

        // The flag, on the LEFT bound of an unbeamed short note OR CHORD. Its ink hangs off
        // the stem end, so the glyph's own box is already in the stem's frame (:186-188).
        // LILYPOND-REF: tie-formatting-problem.cc:181-190 asks the STEM for its flag and never
        // how many heads it carries;
        // until session 524 this built the box for single notes only, where it can never be
        // met (a single note's tie leaves on the side away from its flag), and the two
        // owner's-corpus chords whose tie runs under the flag stood 0.47 ss off LilyPond
        // (ledger tie.width.chord-flag / tie.y.chord-flag, probe tie-chord-flag.ly).
        List<TieOutlineBox>? flag = null;
        if (isLeftBound && stemInfo is not null && noteValue >= 8
            && item is NoteItem { IsBeamed: false } or ChordItem { IsBeamed: false })
        {
            var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, stemUp);
            if (flagBBox != default)
            {
                double tipY = (stemUp ? stemInfo.Value.StemMax : stemInfo.Value.StemMin) * 0.5;
                // The glyph sits half a blot INSIDE the stem's end — LayoutUtilities.FlagPlacementY,
                // the one house the drawn flag, its dot support and its spacing band already
                // read. Until session 525 this box alone stood on the tip itself, 0.04 further
                // out, and a short tie's close-by reading (:572-576) landed 0.04 lower on the
                // box's padding slope (skyline.cc:558-610): the whole of ledger
                // tie.width.chord-flag's +0.040000 (Lab sessions/p525/prediction.txt).
                // LILYPOND-REF: lily/flag.cc:183-196 Flag::internal_calc_y_offset —
                //   stem_extent[d] - d * blot / 2; the box at :187-188 is the grob's extent, so
                //   it carries that offset.
                double flagY = LayoutUtilities.FlagPlacementY(tipY, stemUp);
                double flagX = LayoutUtilities.StemX(supportLeft, stemUp, noteValue,
                    LayoutUtilities.NoteheadStyleOf(item));
                (flag ??= []).Add(new TieOutlineBox(
                    flagY + flagBBox.Bottom, flagY + flagBBox.Top, flagX, flagX + flagBBox.Width));
            }
        }

        // The accidentals, on the RIGHT bound only — they stand between the arriving tie and
        // the head it is arriving at, and no other bound can meet them (:231-236).
        List<TieOutlineBox>? accidentals = null;
        if (!isLeftBound)
        {
            var placement = new AccidentalPlacement();
            IEnumerable<AccidentalLayout> laid = item switch
            {
                ChordItem ch when ch.HasPackedAccidentals
                    => ChordAccidentalLayouts(ch),
                ChordItem ch => placement.CalculatePositions(ch.Notes, offsets.ToArray(),
                    stem: AccidentalStem.Of(ch, stemUp)),
                // Packed with the rest of its staff column, in the column's frame — which is
                // the frame columnX names below (Collector.StaffAccidentalColumns).
                NoteItem pn when pn is { Accidental: { } acc, AccidentalX: { } px }
                    => [new AccidentalLayout(pn.StaffPosition, acc, px, pn.IsCourtesy)],
                _ => placement.CalculateSinglePosition((NoteItem)item) is { } one ? [one] : [],
            };
            foreach (var layout in laid)
            {
                var accBBox = GlyphMetrics.GetAccidentalBBox(layout.Accidental);
                double accX = columnX + layout.XOffset;
                double accY = layout.StaffPosition * 0.5;
                (accidentals ??= []).Add(new TieOutlineBox(
                    accY + accBBox.Bottom, accY + accBBox.Top, accX, accX + accBBox.Width));
            }
        }

        return new TieColumnParts
        {
            TiedHeads = tied,
            OtherHeads = (IReadOnlyList<TieOutlineBox>?)others ?? [],
            Stem = stem,
            Dots = (IReadOnlyList<TieOutlineBox>?)dots ?? [],
            Flag = (IReadOnlyList<TieOutlineBox>?)flag ?? [],
            Accidentals = (IReadOnlyList<TieOutlineBox>?)accidentals ?? [],
            HeadPositions = positions,
        };
    }

    /// <summary>
    /// Detects ties and calculates their layouts, splitting cross-system ties into broken pieces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: lily/break-substitution.cc:67-153 — substitute_grob &amp; do_break_substitution
    /// A tie that crosses one or more system breaks is split into per-system pieces.
    /// Each piece's bound on the broken side is reattached to the system edge.
    /// </remarks>
    /// <param name="fonts">The SCORE's text metrics — a tab tie hangs off its fret digit,
    /// whose em is the plan's (<see cref="TabConstants.FretEm"/>). Passed rather than read
    /// off <paramref name="score"/>: a per-staff <c>Score</c> carries no plan.</param>
    public ImmutableArray<TieLayout> LayoutTies(Rendering.ScoreTextMetrics fonts, Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1, Model.Staff? staff = null)
        => LayoutTies(fonts, _tieDetector.DetectTies(score), score, systems, staffIndex, staff);

    /// <summary>The detectors themselves, for a caller that must run them ONCE and lay out
    /// per system — see the remark on the pre-detected overloads below. They live here
    /// because this is where they live; a second <c>new SlurDetector()</c> elsewhere would
    /// be a second home for one thing (HANDOFF 5.2.1).</summary>
    internal ImmutableArray<SlurItem> DetectSlurs(Score score) => _slurDetector.DetectSlurs(score);

    /// <inheritdoc cref="DetectSlurs"/>
    internal ImmutableArray<TieItem> DetectTies(Score score) => _tieDetector.DetectTies(score);

    /// <summary>
    /// The same, on ties the caller has ALREADY detected.
    /// </summary>
    /// <remarks>
    /// ⚠️ AN EXPLICIT PARAMETER RATHER THAN AN OPTIONAL ONE, deliberately: HANDOFF 7.7's
    /// "same function, optional argument" layer is what let a whole island of profiles be
    /// built with their side tables at default, and a defaulted <c>ties</c> here would be the
    /// same trap one level down. A caller either detects, or hands in what it detected.
    /// <para>
    /// The detection is a walk of the WHOLE score
    /// (<see cref="Collector.TieDetector.DetectTies"/> over <c>VoiceScan.WalkVoiceItems</c>)
    /// while the layout it feeds is per SYSTEM, so a caller that runs once per system pays
    /// that walk once per system for an answer that cannot change — see
    /// <c>MultiStaffLayouter.StaffSpannerItemsOf</c>, which is the memo that fixes it.
    /// </para>
    /// </remarks>
    internal ImmutableArray<TieLayout> LayoutTies(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<TieItem> ties, Score score, ImmutableArray<SystemLayout> systems,
        int staffIndex = -1, Model.Staff? staff = null)
    {
        if (ties.Length == 0)
            return ImmutableArray<TieLayout>.Empty;

        // A NUMBERS-ONLY TAB DRAWS NO TIE, and the suppression is here rather than at the
        // draw site so the tie is not in the skylines either — a bow nothing prints must not
        // reserve room above the staff.
        // LILYPOND-REF: ly/engraver-init.ly:1271-1276 slur::move-closer-to-tab-note-heads —
        //   the tab context's tie/slur block, which that override ends. Tie.stencil,
        //   RepeatTie.stencil and LaissezVibrerTie.stencil all go to ##f while the SLUR two
        //   lines later keeps its stencil and only moves: a tab suppresses ties and NOT slurs,
        //   which is why this guard names the tie alone. \tabFullNotation (ly/property-init.ly)
        //   puts the ties back, and that is exactly Lily#'s `as numbers` / `as full`.
        // ⚠️ THE HELD NOTE IS STILL SHOWN, by the half of LilyPond's rule that was already
        // ported: the tie's TARGET prints no fret digit (SharedRenderer.Tab,
        // scm/tablature.scm:186-224 tab-note-head::handle-ties). A numbers-only tab says
        // "keep holding" by the absent number, which is what a reader of published tab
        // expects; the bow is what the notation staff above carries.
        // ★ Reported by a reader on 2026-08-29 (scratch/ベースタブLy/Walk.lys): the bow was
        //   printing on the tab in BOTH styles, and the comment at the draw site said so
        //   ("Ties still print") — a divergence nobody had asked LilyPond about.
        if (staff is { IsTab: true, TabNumbersOnly: true })
            return ImmutableArray<TieLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        // The layouts, the columns and each column's list are lent (ListPool) and given back at
        // the one exit below: ToImmutableArray copies the layouts, and a column is read only by
        // this loop (the sort takes a lent list of its own). MEASURED (session 475's census): 234 + 643 B a
        // keystroke for the two outer lists, none alive after a render; the per-column lists
        // (the `[]` below until session 476) are a collection expression no census counts.
        var tieLayouts = ListPool<TieLayout>.Rent();

        // A tie COLUMN is the ties of ONE chord: same voice, same start measure and item.
        // LilyPond builds one Tie_formatting_problem per Tie_column and feeds it that column's
        // ties (lily/tie-column.cc:81-93 Tie_column::calc_positioning_done -> problem.from_ties
        // (ties)), which is what lets a tie's answer depend on where its neighbours went -- and
        // is why a tie is never scored against a tie in another bar, another voice, or, after
        // line-breaking, an identically-placed one on another system whose bars share a local X.
        // audit/lp-geometry system.tie-{under,over}-notes, and tie.y.{seconds,triad}.lower for
        // what solving them ONE AT A TIME cost.
        var columns = ListPool<List<TieItem>>.Rent();
        // Lent, and given back right after the bucketing — its only reader (see RentColumnOf).
        var columnOf = RentColumnOf();
        foreach (var tie in ties)
        {
            var key = (tie.VoiceIndex, tie.StartMeasureIndex, tie.StartItemIndex);
            if (!columnOf.TryGetValue(key, out int existing))
            {
                existing = columns.Count;
                columnOf[key] = existing;
                columns.Add(ListPool<TieItem>.Rent());
            }
            columns[existing].Add(tie);
        }
        GiveColumnOf(columnOf);

        foreach (var column in columns)
        {
            // The bounds and the break-up belong to the COLUMN, not to each tie: every tie of a
            // column runs between the same two chords, so they all split at the same systems.
            // ⚠️ NOT PORTED — LP's solve-once-then-break order: A BROKEN COLUMN IS SOLVED
            //   ONCE PER SEGMENT, NOT ONCE. LP has the order (the citation below), so this
            //   is a knowing structural divergence, not a Lily#-own quantity (§5.2 audit,
            //   session 158).
            //   departs from: lily/tie-column.cc:81-93, where Tie_column::calc_positioning_done
            //     scores the column ONCE on the unbroken spanners and lily/spanner.cc:36-144
            //     then breaks the result. Here each system segment builds its own problem with
            //     its own bounds (a broken bound has no column to read an outline off, so its
            //     attachment is the system edge either way).
            //   goes away when: the tie carries a positioning decided before break substitution
            //     -- the same shape the slur's broken pieces have, and a change to both.
            //   observed by: NOTHING that separates the two orders. audit/lp-geometry
            //     system.tie-{under,over}-notes DO measure a broken tie's drawn geometry, but
            //     they sit at +0.000442474 -- a residual that predates this and has never been
            //     attributed, so it cannot be read either as evidence for the per-segment solve
            //     or against it. Separating them needs a book where the two systems' pieces
            //     would score differently, which is a COLUMN broken mid-chord; there is none.
            var anchor = column[0];
            // A tie whose other end is on a system this call was not handed (the per-system staff
            // skylines lay out ONE system) still has its piece here, as a slur's does
            // (SpannerBreakSubstitution.SplitClipped): the missing bound's measure is null and only
            // the piece that holds it reads it.
            measureMap.TryGetValue(anchor.StartMeasureIndex, out var startInfo);
            measureMap.TryGetValue(anchor.EndMeasureIndex, out var endInfo);

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            var segments = SpannerBreakSubstitution.SplitClipped(
                anchor.StartMeasureIndex, anchor.EndMeasureIndex, systems, measureToSystemIdx);

            if (segments.IsEmpty)
                continue;

            // Bottom -> top, the order LilyPond's front()/back() and its monotonicity terms are
            // written in. TieDetector already emits a chord's ties that way; sorting here says
            // so rather than relying on it.
            // Stable, as the OrderBy it replaced was, and into a lent list like the column's
            // own — both lists here are read by this iteration alone and given back at its end.
            // MEASURED (session 508's census of every copy in Core, Release, the reader's
            // corpus, eight forward keystrokes a book): the OrderBy and the Select/Distinct
            // chain were 2,345 B a keystroke, 7.14 columns of one tie.
            var ordered = ListPool<TieItem>.Rent();
            foreach (var t in column)
            {
                int at = ordered.Count;
                while (at > 0 && ordered[at - 1].StaffPosition > t.StaffPosition)
                    at--;
                ordered.Insert(at, t);
            }

            // The whole COLUMN's bound heads, which is what each chord outline is built from —
            // or a chord's upper tie recedes past a head that is there. Distinct, in the order
            // the positions first appear.
            var tiedPositions = ListPool<int>.Rent();
            foreach (var t in ordered)
                if (!tiedPositions.Contains(t.StaffPosition))
                    tiedPositions.Add(t.StaffPosition);
            // The END column's bound heads are the ties' own end heads, which stand elsewhere only
            // where an ottava bracket begins or ends between the two (TieDetector.SamePitch). The
            // outline is then built around the head the tie really reaches, stem and all
            // (tie-formatting-problem.cc:96-287 reads the bound heads), not around no head at all.
            List<int>? endTiedPositions = null;
            foreach (var t in ordered)
                if (t.EndNote.StaffPosition != t.StaffPosition)
                {
                    endTiedPositions = [];
                    foreach (var u in ordered)
                        if (!endTiedPositions.Contains(u.EndNote.StaffPosition))
                            endTiedPositions.Add(u.EndNote.StaffPosition);
                    break;
                }

            // A TAB column's directions come from the STRING LINES, so they are decided here,
            // once for the column, and handed to every segment's specifications.
            bool[]? tabCurveUp = staff is { IsTab: true }
                ? TabColumnCurveUp(fonts, score, staff, ordered)
                : null;

            // A LONE tie BROKEN at a line end takes its direction BEFORE it is scored, from
            // both original heads' stems (the piece's own and its broken neighbour's), and is
            // then solved as a tie with that direction imposed.
            // LILYPOND-REF: lily/tie.cc:193-211 Tie::calc_control_points (`me->original ()
            //   && ties.size () == 1 && !direction` → set_grob_direction (get_default_dir)),
            //   lily/tie.cc:94-127 Tie::get_default_dir.
            // Broken = some piece lacks one of the tie's own ends (one system handed can hold
            // a single piece of a broken tie).
            bool broken = segments.Length > 1 || !segments[0].IsFirst || !segments[0].IsLast;
            bool? brokenCurveUp = broken && ordered.Count == 1
                && staff is not { IsTab: true } && ordered[0].ForcedCurveUp is null
                    ? BrokenTieDefaultCurveUp(score.Voices[ordered[0].VoiceIndex], ordered[0])
                    : null;

            var solved = new TieLayout[ordered.Count, segments.Length];

            for (int s = 0; s < segments.Length; s++)
            {
                var specs = new List<TieSpecification>(ordered.Count);
                for (int i = 0; i < ordered.Count; i++)
                {
                    specs.Add(BuildTieSpecification(
                        fonts, score, systems, staff, staffIndex, ordered[i], segments[s],
                        startMeasure, endMeasure, tiedPositions, tabCurveUp?[i], brokenCurveUp, endTiedPositions));
                }

                // The thread's lent problem (TieFormattingProblem.SolveColumn) — one column a
                // problem, the same arithmetic, and no fresh dictionary / candidates / lists.
                var layouts = TieFormattingProblem.SolveColumn(specs);
                for (int i = 0; i < ordered.Count; i++)
                {
                    solved[i, s] = layouts[i] with
                    {
                        StaffIndex = staffIndex,
                        RenderMeasureIndex = segments[s].StartMeasureIndex,
                    };
                }
            }

            // Emitted tie-major, which is the order the drawn ties -- and every ledger reading
            // that indexes them -- have always come out in.
            // ⚠️ The slot lookup must be BY REFERENCE: TieItem is a record, and a UNISON
            // chord's two ties are value-EQUAL (same position, same synthesized bounds), so
            // List.IndexOf hands both of them slot 0 and the column's upper tie is drawn as a
            // second copy of the lower one — the solver's up/down split (LP's
            // set_ties_config_standard_directions seeding) never reaches the page.
            // A loop, not FindIndex(t => …): the lambda built an environment and a delegate for
            // every tie — 138 + 507 B a keystroke over the reader's corpus (session 470's
            // allocation-tick price by type).
            foreach (var tie in column)
            {
                int i = 0;
                while (!ReferenceEquals(ordered[i], tie))
                    i++;
                for (int s = 0; s < segments.Length; s++)
                    tieLayouts.Add(solved[i, s]);
            }
            ListPool<int>.Give(tiedPositions);
            ListPool<TieItem>.Give(ordered);
        }

        var laidOut = tieLayouts.ToImmutableArray();
        ListPool<TieLayout>.Give(tieLayouts);
        foreach (var column in columns)
            ListPool<TieItem>.Give(column);
        ListPool<List<TieItem>>.Give(columns);
        return laidOut;
    }

    /// <summary>
    /// The key → column index map <see cref="LayoutTies(Rendering.ScoreTextMetrics, ImmutableArray{TieItem}, Score, ImmutableArray{SystemLayout}, int, Model.Staff?)"/>
    /// buckets a staff's ties into columns with, lent from one map the thread keeps between calls.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 457's census, Release, the reader's corpus, eight forward keystrokes
    /// a book): 1.27 bucketings a keystroke at 20.01 columns each (max 142), and all 2,356
    /// maps built were unreachable by the time the render that built them returned — the map
    /// is read only inside the bucketing loop; the columns themselves are a list. The maps and
    /// their growth ladders were 2,667 B a keystroke, 0.08% of it.
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — a map given back dirty would answer a key the previous staff had with
    /// an index into THIS staff's column list: a tie filed under another chord's column, or an
    /// index past its end. There is no early return and no throw between the rent and the
    /// give, and the map is only ever looked up, never walked, so its reuse cannot reorder
    /// anything. It holds value tuples and ints, so the drawer pins nothing but its capacity.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static Dictionary<(int Voice, int Measure, int Item), int>? t_columnOf;

    /// <summary>Takes the thread's key → column map, or makes the thread's first.</summary>
    private static Dictionary<(int Voice, int Measure, int Item), int> RentColumnOf()
    {
        var map = t_columnOf;
        if (map is null)
            return new Dictionary<(int Voice, int Measure, int Item), int>();
        t_columnOf = null;
        return map;
    }

    /// <summary>Puts a finished bucketing's map back, emptied, with its capacity.</summary>
    private static void GiveColumnOf(Dictionary<(int Voice, int Measure, int Item), int> map)
    {
        map.Clear();
        t_columnOf = map;
    }

    /// <summary>
    /// Which way each tie of a TAB column curves, by LilyPond's own column rule run on the
    /// tab's own staff positions — the string lines the fret digits sit on.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS THE TAB'S QUANTITY, TAKEN FROM THE TAB. A tie's <c>StaffPosition</c> is the
    /// NOTATED pitch's, and on a tab that is a different question with a different answer:
    /// which string a note is played on is the fret allocation's decision, not the pitch's,
    /// and a written string number can put a higher pitch on a lower string. This is the
    /// fourth quantity in a row that a tab was taking from the notation spelling (the string
    /// number after a slur close, the stem x, the single tie's side — HANDOFF §1, 第179),
    /// which is why the column is re-sorted here rather than trusting <c>ordered</c>.
    /// <para>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:1025-1084 set_ties_config_standard_directions
    ///   — the rule itself, reached through
    ///   <see cref="TieFormattingProblem.StandardDirections(IReadOnlyList{int}, IReadOnlyList{bool?}, bool)"/>
    ///   so there is one copy of it.
    /// </para>
    /// <para>
    /// MEASURED on 2.26.0 (test/tab-chord-tie's twin, dumping every Tie's <c>direction</c>):
    /// the TabStaff's three ties of <c>&lt;c' e' g'&gt;</c> come out −1, +1, +1 at TabNoteHead
    /// staff-positions 1, 3, 5 — the bottom string below its digit, the two above it above
    /// theirs — which is what this rule answers for those positions. The notation staff of
    /// the same book reports the same −1, +1, +1 at positions 8, 10, 12, and Lily# already
    /// agreed there; only the tab did not.
    /// </para>
    /// <para>
    /// ⚠️ A SINGLE TAB TIE IS PROVABLY UNCHANGED BY THIS, which is why it replaces the old
    /// rule outright instead of sitting beside it. The old rule was "opposite the tab stem",
    /// and a tab stem points up exactly when its mean string is BELOW the middle of the
    /// fretboard (<c>string &gt; (StringCount+1)/2</c>); the position here is
    /// <c>StringCount+1−2·string</c>, whose sign is positive exactly when
    /// <c>string &lt; (StringCount+1)/2</c>. For a column of one this rule takes
    /// <c>sign(position)</c>, falling back to <c>neutral-direction</c> = UP when it is zero —
    /// and zero happens only on the middle string of an odd tuning, where the old rule's
    /// strict <c>&gt;</c> also answered UP. The two agree on every string of every tuning.
    /// USER DECISION (2026-08-16): for CHORDS, defer to LilyPond's spread.
    /// </para>
    /// </remarks>
    private static bool[] TabColumnCurveUp(Rendering.ScoreTextMetrics fonts, Score score, Model.Staff staff, List<TieItem> ordered)
    {
        // staffY is irrelevant to a string number and a staff position; this geometry is
        // asked for neither of the two things it needs a page position for.
        var geom = new TabStaffGeometry(fonts,
            staff.Tuning ?? TuningType.Guitar, 0, staff.TabSourceClef, staff.Transposition);

        var byPosition = new (int Index, int Position, bool? VoiceUp)[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            var tie = ordered[i];
            var item = ItemAt(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex);
            int stringNum = item is ChordItem chord
                ? geom.ChordNoteDigitColumn(chord, tie.StaffPosition).StringNum
                : geom.NoteDigitColumn(tie.StartNote.Midi, tie.StartNote.StringNumber).StringNum;
            // Inside a polyphonic span the voice props set the tie's side (NoteItem.VoiceStemUp).
            // LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set — Tie.direction.
            bool? voiceUp = item switch
            {
                NoteItem n => n.VoiceStemUp,
                ChordItem c => c.VoiceStemUp,
                _ => null,
            };
            byPosition[i] = (i, geom.StaffPositionOfString(stringNum), voiceUp);
        }

        // Bottom -> top on the TAB, which is the order the rule's front()/back() mean.
        Array.Sort(byPosition, (a, b) => a.Position.CompareTo(b.Position));
        var dirs = TieFormattingProblem.StandardDirections(
            byPosition.Select(p => p.Position).ToArray(),
            byPosition.Select(p => p.VoiceUp).ToArray(),
            TieDetails.Default.NeutralDirectionUp);

        var result = new bool[ordered.Count];
        for (int k = 0; k < byPosition.Length; k++)
            result[byPosition[k].Index] = dirs[k] > 0;
        return result;
    }

    /// <summary>
    /// Where a tie or slur piece that a line break OPENS begins: the right edge of the
    /// line-start column's extent ON THIS STAFF (its own clef, key and meter), which ends at
    /// the bar line the system opens with when it opens with one (a <c>.|:</c>) —
    /// <see cref="SystemLayout.LineStartStaffRights"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:262-270 set_minimum_height
    /// (<c>staff_extent (bounds[0])[-dir]</c>); lily/slur-scoring.cc:594-598
    /// get_base_attachments (<c>ext[-d]</c> of the bound column). ABC.lys (Lab corpus)
    /// section B3: the tie piece began 2.94 left of LilyPond's, under the repeat bar.
    /// </remarks>
    private static double BrokenPieceStartX(SystemLayout segSystem, int staffIndex)
        => segSystem.Measures[0].X + segSystem.LineStartRightOf(staffIndex);

    /// <summary>
    /// Where a tie or slur piece that a line break CLOSES ends: the LEFT edge of the system's
    /// closing break column — the end bar line's ink, or a courtesy clef standing in front of
    /// it — not the measure's end, which is the bar line's RIGHT edge.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:262-270 set_minimum_height
    /// (staff_extent[-dir]; a tie's note-head gap comes off after, in FinalAttachment);
    /// lily/slur-scoring.cc:594-598 get_base_attachments. The same bound a multi-measure rest
    /// ends on (MultiMeasureRestEngraver's endX). ABC.lys (Lab corpus) bar 63: the extra
    /// bar-line width kept a line-end tie on the head's edge where LilyPond, 0.19 shorter
    /// there, drops it under the head.
    /// </remarks>
    private static double BrokenPieceEndX(Rendering.ScoreTextMetrics fonts, Voice voice, SystemLayout segSystem)
    {
        var lastMeasure = segSystem.Measures[^1];
        var measures = voice.Measures;
        int lastIndex = lastMeasure.MeasureIndex;
        var endBar = lastIndex < measures.Length
            ? EngravingDefaults.LineEndBarline(measures[lastIndex].EndBarline)
            : BarlineType.Single;
        return lastMeasure.X + lastMeasure.Width
            - EngravingDefaults.BarlineDrawnWidth(endBar)
            - SpacingRules.BoundaryClefAllowance(fonts, endBar,
                lastIndex + 1 < measures.Length ? measures[lastIndex + 1] : null);
    }
    /// <summary>
    /// Everything one tie's two bounds hand the scorer, for one system segment of it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges for broken
    /// pieces. The attachment itself is read off each bound column's CHORD OUTLINE
    /// (<see cref="TieChordOutline"/>); what is carried here is the column, plus the fixed
    /// anchor a bound that is NOT a column falls back to — a piece broken at a system edge, or
    /// a tab digit.
    /// </remarks>
    private TieSpecification BuildTieSpecification(
        Rendering.ScoreTextMetrics fonts,
        Score score,
        ImmutableArray<SystemLayout> systems,
        Model.Staff? staff,
        int staffIndex,
        TieItem tie,
        SpannerBreakSegment segment,
        MeasureLayout? startMeasure,
        MeasureLayout? endMeasure,
        List<int> tiedPositions,
        bool? tabCurveUp = null,
        bool? brokenCurveUp = null,
        List<int>? endTiedPositions = null)
    {
        int startDots = tie.StartNote.Dots;

        var segSystem = systems[segment.SystemIndex];

        // The item X is the LEFT edge of an undisplaced head; the seconds displacement is
        // per head and belongs to the outline, not to the column. Hoisted out of the two
        // branches because the TAB anchors below are measured from these, not from the
        // notation offsets the branches build.
        // …and a head is where the note-collision shift put its voice's column (the slur pass
        // reads the same table and says why: a bound IS the shifted NoteColumn).
        // MEASURED (session 658, `c8[~ c]` against `e4` in the other voice of a condensedStaff):
        // LilyPond's tie leaves the shifted c at 10.618, Lily#'s left the unshifted one at 10.174.
        var voiceShifts = SpacingRules.VoiceCollisionShiftsOf(score.Voices);
        // A bound on a system this call was not handed has no measure (null) — only the piece
        // that holds that bound reads its X.
        double startColumnX = startMeasure is null ? double.NaN : startMeasure.X
            + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, startMeasure)
            + voiceShifts.ShiftOf(tie.StartMeasureIndex, tie.VoiceIndex + 1, tie.StartItemIndex);
        double endColumnX = endMeasure is null ? double.NaN : endMeasure.X
            + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, endMeasure)
            + voiceShifts.ShiftOf(tie.EndMeasureIndex, tie.VoiceIndex + 1, tie.EndItemIndex);

        double segStartX;
        TieColumnParts? startColumn = null;
        TieColumnParts? endColumn = null;
        if (segment.IsFirst)
        {
            startColumn = BuildTieColumn(
                score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex,
                startColumnX, tiedPositions, isLeftBound: true);

            // The fallback anchor, used only when there is no column to read. On a NOTATION
            // staff that is a piece broken at a system edge; a TAB bound overwrites both of
            // these below, from the fret digit the bow actually leaves.
            double startBase = startColumnX
                + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, tie.StaffPosition);
            int noteValue = tie.StartNote.BaseDuration.Numerator != 1
                ? 1
                : tie.StartNote.BaseDuration.Denominator;
            double outlineRight = GlyphMetrics.GetNoteheadAdvance(noteValue);
            if (startDots > 0)
                outlineRight += EngravingDefaults.DotRowReach(startDots, GlyphMetrics.AugmentationDot.Width);
            segStartX = startBase + outlineRight;
        }
        else
        {
            // Broken piece: the line-start column's staff extent (BrokenPieceStartX); there is
            // no note column.
            segStartX = BrokenPieceStartX(segSystem, staffIndex);
        }

        double segEndX;
        if (segment.IsLast)
        {
            endColumn = BuildTieColumn(
                score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex,
                endColumnX, endTiedPositions ?? tiedPositions, isLeftBound: false, atLineStart: !segment.IsFirst);
            // The fallback anchor (tab only): the right head's inner (left) edge.
            segEndX = endColumnX
                + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, tie.StaffPosition);
        }
        else
        {
            segEndX = BrokenPieceEndX(fonts, score.Voices[tie.VoiceIndex], segSystem);
        }

        // Tie Y position is uniform (same pitch on both ends).
        // Within-system staff-top offset (device, down from system top), NOT an
        // absolute page Y — so the scored tie (and the tab-digit geometry below)
        // is system-independent. TieFormattingProblem reasons over Y DIFFERENCES,
        // so feeding the relative base shifts every output Y by exactly system.Y,
        // undone once in DrawTies (byte-identical to the former absolute origin).
        // Decouples the tie from SystemLayout.Y for the W2 stacking-origin flip
        // (step 2d, shared with slurs).
        double staffY = LayoutUtilities.StaffOffsetInSystemDown(segSystem, staffIndex);
        double y;
        var tieForProblem = tie;
        if (staff is { IsTab: true })
        {
            // A fret digit is not a note column: it has no chord outline to read, so the
            // tie hangs off the fixed anchors above and the whole Y-dependent
            // attachment (TieChordOutline) does not apply.
            startColumn = null;
            endColumn = null;
            // LILYSHARP-OWN: no head extent on a tab, so the horizontal-distance term
            // (tie-formatting-problem.cc:665-683) scores 0 for BOTH ends here and the
            // attachment is whatever the digit rule below chose.
            //   departs from: :670, `spec.note_head_drul_[d]->extent (…)`. LilyPond
            //     builds that from a TabNoteHead like any other head, so it HAS the
            //     term; this engine composes the digit's edge instead (axis + zigzag
            //     column + half a digit, below), and there is no grob box for the
            //     penalty to measure against.
            //   goes away when: a fret digit carries a head extent the outline can be
            //     built from — at which point the tab tie stops needing any of this
            //     branch and goes through the scored search like every other tie.
            //   observed by: NOTHING. There is no ledger point on a tab tie's width,
            //     and there cannot be one until the tab fixtures pin their strings
            //     (docs/HANDOFF.md's "tab の残り 3 冊"). test/tab-tie and
            //     test/tab-chord-tie hold the drawing only; TabChordTieTests pins the
            //     x arithmetic directly.
            // On a tab the tie connects two fret digits on ONE string, so it
            // belongs on that string's line — NOT at the notation pitch height.
            var geom = new TabStaffGeometry(fonts, staff.Tuning ?? TuningType.Guitar, staffY, staff.TabSourceClef, staff.Transposition);
            // A chord's per-string ties must each hug their OWN string, AND HANG OFF THEIR
            // OWN DIGIT. Both answers come from one call so they cannot disagree about which
            // note they are talking about.
            // LILYPOND-REF: lily/tab-note-heads-engraver.cc:106-123 — each
            // TabNoteHead's staff-position is the STRING LINE its
            // noteToFretFunction (exclusive chord allocation) assigned, and
            // the tie follows the heads. So resolve this note's string via
            // the chord's allocation, keyed by staff position (a chord tie's
            // synthesized start note carries no MIDI) — not a per-note
            // auto-fret, which hands several notes the same string.
            var tieItem = ItemAt(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex);
            var endItem = ItemAt(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex);
            var startDigit = tieItem is ChordItem tieChord
                ? geom.ChordNoteDigitColumn(tieChord, tie.StaffPosition)
                : geom.NoteDigitColumn(tie.StartNote.Midi, tie.StartNote.StringNumber);
            var endDigit = endItem is ChordItem endChord
                ? geom.ChordNoteDigitColumn(endChord, tie.StaffPosition)
                : geom.NoteDigitColumn(tie.EndNote.Midi, tie.EndNote.StringNumber);
            double digitY = geom.StringY(startDigit.StringNum);

            // ⚠️ THE BOW HANGS BETWEEN TWO DIGITS, AND ON A CHORD THOSE ARE NOT AT THE NOTE
            // COLUMN'S X. Lily#'s fret digits are large enough that a chord has to zigzag
            // them into two columns (TabChordColumns), so a chord's three ties leave from
            // three different x's — while the anchors above are the NOTATION chord's head
            // displacement (GetChordHeadXOffset), which is a different question with a
            // different answer. MEASURED on test/tab-chord-tie before this: all three ties
            // came out over the identical span 10.95 … 13.45, while the digits they belong
            // to sat at 8.42 / 10.33 / 8.42 and 12.70 / 14.60 / 12.70 — so the middle
            // string's bow STARTED INSIDE its own digit and the outer two started 1.67 past
            // theirs.
            // ⚠️ LILYSHARP-OWN, and LilyPond cannot be asked: MEASURED on 2.26.0, all three
            //   TabNoteHeads of that chord report the SAME X-offset (8.82, then 12.951), so
            //   there is no zigzag there to port and no second x for a tie to choose between.
            //   departs from: nothing — lily/tie-formatting-problem.cc builds the attachment
            //     from the head's own extent, which is exactly what this does; the extent is
            //     just one Lily# has to compose (axis + column + half a digit) rather than
            //     read off a grob.
            //   goes away when: a fret digit carries a head extent the outline can be built
            //     from, which is the same decision named below.
            //   observed by: TabChordTieTests, and the test/tab-chord-tie snapshot.
            double startAxis = startColumnX + EngravingDefaults.TabHeadCenterOffset;
            double endAxis = endColumnX + EngravingDefaults.TabHeadCenterOffset;
            if (segment.IsFirst)
            {
                // Past this digit's right ink edge, then past its dots. The dot spacing is
                // the renderer's own (DrawTabAugmentationDots starts one dot-width past the
                // digit edge and steps 2 per dot), so this is no longer a second spelling of
                // where they end — it is the same arithmetic.
                segStartX = startAxis + startDigit.Dx + startDigit.HalfWidth
                          + EngravingDefaults.DotRowReach(startDots, GlyphMetrics.AugmentationDot.Width);
            }
            if (segment.IsLast)
                segEndX = endAxis + endDigit.Dx - endDigit.HalfWidth;
            // LilyPond hangs the tab tie right at the digit's edge — a small,
            // shallow curve hugging the number — so offset by the VISIBLE
            // glyph half-height plus a hair, not the full erase-box height.
            double clearance = 0.36 * TabConstants.FretEm(fonts, geom.StringCount) + 0.1; // ~0.54 sp at font 2.6
            // Which SIDE of its digit this bow hangs on was decided for the whole column, by
            // LilyPond's own rule run on the string lines (TabColumnCurveUp). Passed in
            // rather than recomputed because the rule reads the column, not the tie.
            // ⚠️ IT ARRIVES AS AN IMPOSED DIRECTION, and on a tab that is still right: a fret
            // digit has no head extent for the scored direction search to weigh (see the
            // note above), so there is nothing for the search to overturn it WITH. On the
            // notation staff the same rule only SEEDS a configuration.
            bool curveUp = tabCurveUp ?? !geom.TabStemUp(tieItem ?? (MusicItem) tie.StartNote);
            y = digitY + (curveUp ? -clearance : clearance);
            tieForProblem = new TieItem(
                tie.StartNote, tie.EndNote, tie.StaffPosition, forcedCurveUp: curveUp,
                tie.StartMeasureIndex, tie.EndMeasureIndex, tie.StartItemIndex, tie.EndItemIndex)
            {
                // The written `~` comes along. Without it a tab tie is the one bow family
                // the reader cannot point at — and on a tab-only book (the shape most of
                // the real corpus takes) that is EVERY tie in the book.
                // ⚠️ DISCLOSED: this rebuild drops VoiceIndex to 0, as it did before this
                // line existed. That is what ResolveDataPos's locator reads, so on a
                // MULTI-VOICE tab a re-resolved address would be read out of voice 0.
                // Left alone deliberately: ArticulationEngraver and LayoutEngine.Prelim
                // also key on this field, so correcting it here is a geometry change and
                // belongs to its own trip. MEASURED 2026-08-30 over the 1645 .lys on disk:
                // 341 carry a tab staff and NONE of them writes a `voice { }` block, so no
                // book reaches the wrong-voice read.
                SourcePosition = tie.SourcePosition,
            };
        }
        else
        {
            double staffMiddleDown = staffY + _options.StaffHeight / 2;
            y = staffMiddleDown - tie.StaffPosition / 2.0;
            // A lone broken tie's direction, decided before scoring (see the caller).
            if (brokenCurveUp is { } up)
            {
                tieForProblem = new TieItem(
                    tie.StartNote, tie.EndNote, tie.StaffPosition, forcedCurveUp: up,
                    tie.StartMeasureIndex, tie.EndMeasureIndex, tie.StartItemIndex, tie.EndItemIndex,
                    tie.VoiceIndex)
                {
                    SourcePosition = tie.SourcePosition,
                };
            }
        }

        // The two bound stems, which decide the direction whenever they AGREE
        // (TieFormattingProblem.ScoreDirectionAgainstStems). Read at each BOUND, not
        // once at the start note: LilyPond asks both heads (
        // tie-formatting-problem.cc:687-697), and the two disagreeing is what the
        // whole port is about. A piece broken at a system edge has no head on the
        // broken side, so no stem either.
        bool? startStemUp = segment.IsFirst
            ? BoundStemUp(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex)
            : null;
        bool? endStemUp = segment.IsLast
            ? BoundStemUp(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex)
            : null;

        return new TieSpecification
        {
            Tie = tieForProblem,
            StartX = segStartX,
            EndX = segEndX,
            Y = y,
            StartDots = segment.IsFirst ? startDots : 0,
            IsBrokenLeft = !segment.IsFirst,
            IsBrokenRight = !segment.IsLast,
            StartColumn = startColumn,
            EndColumn = endColumn,
            StartStemUp = startStemUp,
            EndStemUp = endStemUp,
        };
    }

    /// <summary>
    /// The direction a lone tie broken at a line end is given before it is scored: DOWN when
    /// both bound stems point up, against the one stem when only one bound has a stem, the
    /// sign of its position when neither has, and otherwise (stems that conflict, or both
    /// down) the Tie's <c>neutral-direction</c>, UP.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie.cc:94-127 Tie::get_default_dir, verbatim — including the two
    /// gaps its own comments ask about: two DOWN stems do not give UP by their own branch, and
    /// conflicting stems skip the position and go straight to neutral-direction
    /// (scm/define-grobs.scm Tie: neutral-direction UP). Both heads are the ORIGINAL tie's:
    /// a broken piece reads its missing one off the broken neighbour.
    /// </remarks>
    private static bool BrokenTieDefaultCurveUp(Voice voice, TieItem tie)
    {
        bool? left = BoundStemUp(voice, tie.StartMeasureIndex, tie.StartItemIndex);
        bool? right = BoundStemUp(voice, tie.EndMeasureIndex, tie.EndItemIndex);
        if (left is { } l && right is { } r)
        {
            if (l && r)
                return false;
        }
        else if (left is { } lo)
            return !lo;
        else if (right is { } ro)
            return !ro;
        else if (tie.StaffPosition != 0)
            return tie.StaffPosition > 0;
        return true;
    }

    /// <summary>
    /// Which way the stem of the note/chord at (<paramref name="measureIndex"/>,
    /// <paramref name="itemIndex"/>) points, or null when it has no stem to point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:687-697 score_aptitude — the tie's scorer
    /// takes the stem off each bound head and keeps it only if <c>Stem::is_normal_stem</c>,
    /// which a whole note's is not. Null therefore means "this bound casts no vote", not "down".
    /// </remarks>
    internal static bool? BoundStemUp(Voice voice, int measureIndex, int itemIndex)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
            return null;
        var items = voice.Measures[measureIndex].Items;
        if (itemIndex < 0 || itemIndex >= items.Length)
            return null;

        bool stemUp;
        Fraction baseDuration;
        switch (items[itemIndex])
        {
            case NoteItem n: stemUp = n.StemUp; baseDuration = n.BaseDuration; break;
            case ChordItem c: stemUp = c.StemUp; baseDuration = c.BaseDuration; break;
            default: return null;   // rest / spacer — no stem
        }
        // Whole notes (value 1) and breves have no stem, as in ResolveSlurEdge.
        return GlyphMetrics.NoteValueOf(baseDuration) >= 2 ? stemUp : null;
    }

    /// <summary>
    /// Detects slurs and calculates their layouts, splitting cross-system slurs into broken pieces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur.cc, lily/slur-scoring.cc
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: lily/break-substitution.cc:67-153 — substitute_grob &amp; do_break_substitution
    /// A slur that crosses one or more system breaks is split into per-system pieces.
    /// Each piece is scored independently with its bounds reattached to the system edges
    /// (LP-faithful: each broken piece gets its own SlurScoringProblem invocation).
    /// </remarks>
    /// <summary>
    /// Staff position of the note nearest a broken slur edge: the first
    /// (leftEdge) or last sounding note of <paramref name="segSystem"/> that
    /// lies within the slur's span. For chords the head on the curve's side
    /// anchors the edge. Null when the system holds no covered note.
    /// </summary>
    private static int? EdgeNoteStaffPosition(
        Voice voice, SystemLayout segSystem, SlurItem slur, bool leftEdge)
        => EdgeColumn(voice, segSystem, slur, leftEdge) is { } c
            ? MusicItem.EdgeStaffPosition(voice.Measures[c.Measure].Items[c.Item], slur.CurveUp)
            : null;

    /// <summary>
    /// Device Y a broken slur edge's base attachment starts from when the edge column's
    /// STEM reaches past its heads on the slur's side — the stem tip (a beamed stem's, on
    /// the beam's face) plus half a space; null when the stem points away or does not reach
    /// further than the head, so the head base stands.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:600-616 get_base_attachments, a broken bound:
    ///   y = robust_relative_extent (col, Y)[dir_] + dir_ * 0.5 — the WHOLE note column's
    ///   extent, whose stem counts (Note_column holds its Stem; stem.cc:142 ends a beamed
    ///   one on the beam). MEASURED, Lab sessions/p653 s1/slur-break (LilyPond 2.26.0): the
    ///   second piece of `c2( e | break g2 c'')` starts at 3.0 = the up stem's tip 2.5 +
    ///   0.5, where the head base put Lily#'s at 0.195.
    /// :602 `extremes_[-d].bound_ != col`: when the piece's only column is its OTHER, real
    ///   bound (`a( | break b)`'s first piece holds `a` alone), LilyPond takes that bound's y
    ///   (:614) — the caller copies it; this answers null for <paramref name="otherBound"/>.
    ///   Omitting it moved ten exact pairs of the reader's corpus by 2.8-2.96 (session 653).
    /// </remarks>
    private static double? BrokenEdgeStemBaseY(
        Voice voice, SystemLayout segSystem, SlurItem slur, bool leftEdge,
        double staffMiddleDown, double headBaseY,
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember,
        (int Measure, int Item)? otherBound, VoiceCollisionTable voiceShifts)
    {
        if (EdgeColumn(voice, segSystem, slur, leftEdge) is not { } c
            || (otherBound is { } ob && ob.Measure == c.Measure && ob.Item == c.Item))
            return null;
        var item = voice.Measures[c.Measure].Items[c.Item];
        if (NoteColumnLayout.Of(item) is not { HasStem: true } col || col.StemUp != slur.CurveUp)
            return null;
        double x = c.Layout.X + GetItemXOffset(voice, c.Measure, c.Item, c.Layout)
            + voiceShifts.ShiftOf(c.Measure, slur.VoiceIndex + 1, c.Item);
        double stemX = LayoutUtilities.StemX(x, col.StemUp, col.NoteValue, col.Notehead);
        double tip = TryGetBeamedStemTipDeviceY(beamByMember, slur.VoiceIndex, c.Measure, c.Item,
                stemX, staffMiddleDown, col.StemUp, out double beamTip)
            ? beamTip
            : staffMiddleDown - EngravingDefaults.StaffMiddle + col.OutwardTipDeviceY(col.StemUp);
        double y = tip + (slur.CurveUp ? -0.5 : 0.5);
        // Device Y grows downward: the stem base wins only where it lies further out.
        return slur.CurveUp ? (y < headBaseY ? y : null) : (y > headBaseY ? y : null);
    }

    /// <summary>The item of <see cref="EdgeColumn"/>, or null.</summary>
    private static MusicItem? EdgeColumnItem(
        Voice voice, SystemLayout segSystem, SlurItem slur, bool leftEdge)
        => EdgeColumn(voice, segSystem, slur, leftEdge) is { } c
            ? voice.Measures[c.Measure].Items[c.Item]
            : null;

    /// <summary>The first (<paramref name="leftEdge"/>) or last sounding note column of
    /// <paramref name="segSystem"/> within the slur's span — a broken edge's bound column.</summary>
    private static (int Measure, int Item, MeasureLayout Layout)? EdgeColumn(
        Voice voice, SystemLayout segSystem, SlurItem slur, bool leftEdge)
    {
        var measures = leftEdge
            ? segSystem.Measures.AsEnumerable()
            : segSystem.Measures.Reverse();

        foreach (var ml in measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex)
                continue;
            if (mi >= voice.Measures.Length)
                continue;

            var items = voice.Measures[mi].Items;
            int lo = mi == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = mi == slur.EndMeasureIndex ? slur.EndItemIndex : items.Length - 1;
            hi = Math.Min(hi, items.Length - 1);

            // ⚠️ GRACE TIME IS NOT AN EDGE COLUMN. A broken slur's synthetic end reattaches
            // to the first (or last) note column of its own piece, and a grace standing in
            // front of that note would be found first — the segment then started at the
            // GRACE's pitch. MEASURED on audit/lpreg/lyhygrace: the continuation of
            // `g2( … g2)` began 0.35 below where it belongs, at the appoggiatura's f rather
            // than the g it attaches to. LilyPond's Slur_engraver acknowledges note columns
            // of its own context and a grace's belong to the grace group.
            if (leftEdge)
            {
                for (int i = lo; i <= hi; i++)
                    if (!items[i].GraceTime
                        && MusicItem.EdgeStaffPosition(items[i], slur.CurveUp) is not null)
                        return (mi, i, ml);
            }
            else
            {
                for (int i = hi; i >= lo; i--)
                    if (!items[i].GraceTime
                        && MusicItem.EdgeStaffPosition(items[i], slur.CurveUp) is not null)
                        return (mi, i, ml);
            }
        }
        return null;
    }

    /// <summary>
    /// Whether the beam of the stem at (<paramref name="measureIndex"/>,
    /// <paramref name="itemIndex"/>) continues on its <paramref name="rightward"/> side — the
    /// voice's neighbouring sounding item on that side stands under the same beam.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:549-554 get_base_attachments —
    /// <c>Stem::get_beaming (stem, -d)</c>, the beam count on the stem's INNER side. Read off
    /// the items' <see cref="NoteItem.BeamId"/>, not the written <c>[</c> <c>]</c> marks
    /// (<c>HasBeamStart</c> / <c>HasBeamEnd</c>), which an AUTOMATIC beam never carries: the
    /// last note of an auto-beamed pair counted as beamed on its right, and a slur leaving it
    /// started on the beam instead of the head (Butterfly.lys, Lab corpus: `b,,8 b,,( dis,)`,
    /// 1.55 above LilyPond's left end; 想い人, Need You Now, Top of the World …).
    /// </remarks>
    private static bool BeamContinuesToward(Voice voice, int measureIndex, int itemIndex, bool rightward)
    {
        var self = voice.Measures[measureIndex].Items[itemIndex];
        int? beam = self switch { NoteItem n => n.BeamId, ChordItem c => c.BeamId, _ => null };
        if (beam is null)
            return false;
        int step = rightward ? 1 : -1;
        for (int m = measureIndex, i = itemIndex + step; m >= 0 && m < voice.Measures.Length; )
        {
            var items = voice.Measures[m].Items;
            for (; i >= 0 && i < items.Length; i += step)
            {
                var item = items[i];
                // A rest has no stem to share the beam with; a beam that runs over it goes on
                // to the next stem, which answers.
                if (item.GraceTime)
                    continue;
                switch (item)
                {
                    case NoteItem n: return n.BeamId == beam;
                    case ChordItem c: return c.BeamId == beam;
                }
            }
            m += step;
            if (m >= 0 && m < voice.Measures.Length)
                i = rightward ? 0 : voice.Measures[m].Items.Length - 1;
        }
        return false;
    }

    /// <summary>
    /// Resolves the slur-edge note facts (stem presence/direction, inner-side beaming)
    /// the scorer needs. Returns default (no stem) for a rest, an out-of-range index, or a
    /// whole/breve note. <paramref name="leftEdge"/> selects which side is "inner": the left
    /// edge's inner side is the RIGHT (a beam continues right unless it ends here); the right
    /// edge's inner side is the LEFT.
    /// LILYPOND-REF: lily/slur-scoring.cc Slur_score_state extremes_ / edge_has_beams_.
    /// </summary>
    private static SlurEdgeInfo ResolveSlurEdge(
        Voice voice, int voiceIndex, int measureIndex, int itemIndex, bool leftEdge,
        double columnX = double.NaN, double staffMiddleDown = double.NaN,
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember = null)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
            return default;
        var items = voice.Measures[measureIndex].Items;
        if (itemIndex < 0 || itemIndex >= items.Length)
            return default;

        bool stemUp, beamed, hasBeamStart, hasBeamEnd;
        Fraction baseDuration;
        switch (items[itemIndex])
        {
            case NoteItem n:
                stemUp = n.StemUp; beamed = n.IsBeamed;
                hasBeamStart = n.HasBeamStart; hasBeamEnd = n.HasBeamEnd;
                baseDuration = n.BaseDuration;
                break;
            case ChordItem c:
                stemUp = c.StemUp; beamed = c.IsBeamed;
                hasBeamStart = c.HasBeamStart; hasBeamEnd = c.HasBeamEnd;
                baseDuration = c.BaseDuration;
                break;
            default:
                return default; // rest / spacer / barline — no stem
        }
        // A grace column's stem is UP whatever its pitch (score-grace-settings) — DOWN in a lower
        // voice (MusicItem.GraceStemDown) — as the renderer draws it; the model's StemUp is the
        // ungraced default.
        if (items[itemIndex].GraceTime)
            stemUp = !items[itemIndex].GraceStemDown;

        // Whole notes (value 1) and breves have no stem.
        bool hasStem = GlyphMetrics.NoteValueOf(baseDuration) >= 2;
        // Beamed on the INNER side (toward the other endpoint).
        bool beamedInner = beamed && BeamContinuesToward(voice, measureIndex, itemIndex, rightward: leftEdge);
        // The endpoint head's ink width — LP's slur_head_x_extent_, consumed by the
        // tilt X shift and the extra-encompass edge check.
        double headWidth = BowHeadBox(items[itemIndex], GlyphMetrics.NoteValueOf(baseDuration)).Width;

        // The edge stem's frame — LP's extremes_[d].stem_extent_, consumed by the
        // stem-attachment X rule (slur-scoring.cc:738-760). The tip is the same
        // canonical read the encompass obstacles take: the quanted beam's outer
        // face for a beamed stem, the drawn stem end otherwise.
        // ⚠️ The begin is the anchor HEAD'S CENTRE; LP's stem-begin-position is
        // the attachment point, ~0.17 ss off the centre toward the tip — only
        // the 0.25-widened containment window's head-side edge reads it, so a
        // candidate exactly on that margin could attach differently.
        // The extent is the stem UNITED WITH ITS FLAG on both axes — LP builds
        // stem_extent_ as stem->extent ∪ flag->extent (get_bound_info
        // slur-scoring.cc:188-203). The flag hangs on the stem's right in both
        // stem directions and never reaches past the tip, so the union widens
        // X to the flag's reserved ink (ItemSkylineFactory reserves the same
        // [stemX, stemX + width] frame) and can push only the Y window's
        // head-side edge.
        double stemXLo = double.NaN, stemXHi = double.NaN,
            stemTipY = double.NaN, stemBeginY = double.NaN;
        if (hasStem && !double.IsNaN(columnX)
            && NoteColumnLayout.Of(items[itemIndex]) is { } col)
        {
            var bowFont = BowFont(items[itemIndex]);
            double stemX = LayoutUtilities.StemX(columnX, stemUp, col.NoteValue, col.Notehead, bowFont);
            double halfStem = EngravingDefaults.StemThickness / 2.0;
            stemXLo = stemX - halfStem;
            stemXHi = stemX + halfStem;
            stemBeginY = staffMiddleDown - col.HeadPositionToward(!stemUp) / 2.0;
            if (TryGetBeamedStemTipDeviceY(beamByMember, voiceIndex, measureIndex, itemIndex,
                    stemX, staffMiddleDown, stemUp, out double tip))
                stemTipY = tip;
            else if (items[itemIndex].GraceTime)
                // A grace stem by the rule the renderer draws it with (GraceStemDetails:
                // length-fraction 0.8, no-stem-extend) — see AddGraceObstaclesForMeasure.
                stemTipY = staffMiddleDown - StemCalculator.CalculateStemEndPosition(
                    stemUp, StemCalculator.GetDurationLog(col.NoteValue), col.HeadPositionToward(stemUp),
                    GrobFontSize.GraceStemDetails) / 2.0;
            else
                stemTipY = staffMiddleDown - EngravingDefaults.StaffMiddle
                    + col.OutwardTipDeviceY(stemUp);
            var flag = beamed ? default
                : bowFont is { } cueFont ? GlyphMetrics.GetFlagBBox(cueFont, col.NoteValue, stemUp)
                : GlyphMetrics.GetFlagBBox(col.NoteValue, stemUp);
            if (flag != default)
            {
                stemXHi = Math.Max(stemXHi, stemX + flag.Width);
                // The flag's reach from the tip toward the head (device Y) — the one
                // spelling ItemSkylineFactory reserves the same ink by.
                var (flagYMin, flagYMax) = ItemSkylineFactory.FlagInkBand(stemTipY, stemUp, flag);
                double flagInnerY = stemUp ? flagYMax : flagYMin;
                stemBeginY = stemUp
                    ? Math.Max(stemBeginY, flagInnerY)
                    : Math.Min(stemBeginY, flagInnerY);
            }
        }

        return new SlurEdgeInfo(hasStem, stemUp, beamedInner, beamed, headWidth,
            stemXLo, stemXHi, stemTipY, stemBeginY);
    }

    /// <summary>
    /// Half the endpoint note's notehead width, in staff-spaces (device X units). Lily#'s
    /// segStartX/segEndX are the head's LEFT-edge column X; LP attaches at the head CENTER
    /// (slur-scoring.cc:562 fh->extent(X).center()), so this is added to shift right. 0 for a
    /// rest / out-of-range index.
    /// </summary>
    private static double EndpointHeadHalfWidth(Voice voice, int measureIndex, int itemIndex)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length) return 0;
        var items = voice.Measures[measureIndex].Items;
        if (itemIndex < 0 || itemIndex >= items.Length) return 0;
        Fraction dur;
        switch (items[itemIndex])
        {
            case NoteItem n: dur = n.BaseDuration; break;
            case ChordItem c: dur = c.BaseDuration; break;
            case RestItem { IsSpacer: false } r:
                // A rest bound attaches at the rest's ink centre. ⚠️ STAND-IN:
                // LP's rest bound goes through the no-note-column loop, whose X
                // is the BOUND grob's extent edge (ext[-d]), not an ink centre —
                // the bound there is not the rest column, and what its extent is
                // in Lily# terms has no answer yet. The Y side of the same loop
                // is ported exactly (see the rest-base branch in LayoutSlurs);
                // the X was NOT compared against LP.
                // LILYPOND-REF: slur-scoring.cc:594-598 —
                //   breakable_bound_extent / generic_bound_extent, x = ext[-d].
                return GlyphMetrics.GetRestBBox(GlyphMetrics.NoteValueOf(r.BaseDuration)).CenterX;
            default: return 0;
        }
        return BowHeadBox(items[itemIndex], GlyphMetrics.NoteValueOf(dur)).Width / 2.0;
    }

    /// <summary>
    /// The notehead box a slur reads off a note column: a cue note's in the cue's own font
    /// (EngravingDefaults.CueFont — the thirteen design at font-size −4), everyone else's in
    /// the twenty.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:556-562 get_base_attachments (head->extent), as the
    /// encompass infos read theirs —
    /// every head extent the slur reads is the head grob's own, and a CueVoice head is set at
    /// font-size −4. MEASURED, Lab sessions/p656 cue1 (LilyPond 2.26.0): `cue { e4( f) }`'s
    /// down slur ends 0.354 under the head centres (0.56222 × 0.62996) and starts 0.461 right
    /// of the head's left edge (the cue head's centre 0.4077 + the tilt shift); Lily# read the
    /// twenty's 0.545 and 0.652 until session 656.
    /// </remarks>
    /// <summary>
    /// Device Y of a HEAD-bound slur end's base: the head's Y extent read at
    /// <c>linear_combination (0.5 · dir)</c> — three quarters of the way to its slurward edge.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/slur-scoring.cc:576-577 get_base_attachments,
    /// <c>head->extent (common_[Y_AXIS], Y_AXIS).linear_combination (0.5 * dir_)</c>;
    /// Interval::linear_combination (x) = ((1 − x)·lo + (1 + x)·hi) / 2.</remarks>
    private static double HeadBoundBaseY(double staffMiddleDown, int staffPosition,
        GlyphMetrics.BBox box, bool curveUp)
    {
        double x = curveUp ? 0.5 : -0.5;
        double upOffset = ((1 - x) * box.Bottom + (1 + x) * box.Top) / 2.0;
        return staffMiddleDown - staffPosition / 2.0 - upOffset;
    }

    private static GlyphMetrics.BBox BowHeadBox(MusicItem item, int noteValue)
        => BowFont(item) is { } cueFont
            ? GlyphMetrics.GetNoteheadBBox(cueFont, noteValue)
            : GlyphMetrics.GetNoteheadBBox(noteValue);

    /// <summary>
    /// The font a slur reads a note column's head-hung grobs from — its stem's attachment x
    /// and its flag's box as well as its head: the cue's own font for a cue note, null (the
    /// twenty) for everyone else.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:184-203 get_bound_info — stem_extent_ is the Stem
    /// grob's own extent united with the Flag's, and a CueVoice Stem stands on its head's
    /// attachment in the cue font (lily/stem.cc internal_calc_stem_offset_from_head), as
    /// SharedRenderer.DrawNote draws it. MEASURED, Lab sessions/p691/cue (LilyPond 2.26.0):
    /// `cue { e4( a4 d'4 c4) }`'s up slur leaves the e's stem 0.3 right of its right edge;
    /// with the twenty's attachment Lily# started it 0.49 further right.
    /// </remarks>
    private static GlyphMetrics.DesignMetrics? BowFont(MusicItem item)
        => item.GraceTime
            // A grace column — the start of a grace slur (SlurItem.StartGraceGroup): its head
            // is set at general-grace-settings' NoteHead font-size −3, as the renderer draws it.
            ? GrobFontSize.FontOf(item, SizedGrob.NoteHead)
            : SpacingRules.HeadFontOf(item);

    /// <summary>
    /// Device-Y of the slur attachment when the endpoint note's stem joins a beam — LP's
    /// stem_extent_[Y][dir_] (slur-scoring.cc:554) = the beam stack's outer edge on the slur
    /// side. Uses the canonical <see cref="BeamLayout.OuterEdgeStaffSpaceAtX"/> (frame B) and
    /// converts to device once. False when the note is not in any supplied beam layout.
    /// </summary>
    /// <remarks>
    /// <paramref name="noteX"/> is the DRAWN STEM's x (every caller hands
    /// <see cref="LayoutUtilities.StemX(double, bool, int, NoteheadStyle, double)"/>), and
    /// since 2026-09-07 the face is in that frame. Until then the face was in the column
    /// ANCHOR frame, so this read was off by the beam's slope times the attach — 1.2392 for
    /// an up stem — on every sloped beam a slur attached to.
    /// </remarks>
    private static bool TryGetBeamedStemTipDeviceY(
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember,
        int voiceIndex, int measureIndex, int itemIndex, double noteX,
        double staffMiddleDown, bool curveUp, out double stemTipDeviceY)
    {
        stemTipDeviceY = 0;
        if (beamByMember is null
            || !beamByMember.TryGetValue((voiceIndex, measureIndex, itemIndex), out var bl))
            return false;

        // curveUp == the endpoint note's stem direction here (caller gates on StemUp == curveUp).
        stemTipDeviceY = staffMiddleDown - bl.OuterEdgeStaffSpaceAtX(noteX, curveUp);
        return true;
    }

    /// <summary>
    /// The note columns the slur encompasses within this broken segment — voice
    /// columns AND the grace columns hanging inside the span — in device
    /// coordinates and sorted by X. The scorer treats the first and last columns
    /// as the slur's edges and scores head encompass over the interior; a column
    /// whose stem points WITH the slur also carries its stem's reach, so the
    /// curve lifts clear of slurward stem tips (a grace run's forced-up stems
    /// under an up slur — "slur-grace.ly").
    /// Returns an empty list when the segment covers no note column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc:111-161 get_encompass_info — x_ is the
    ///   head's ink CENTER (:127-132), or the stem's X when the stem points with
    ///   the slur (:152-155); stem_ = the stem's Y extent on the slur side plus
    ///   half the beam's thickness when beamed (:146-150), else head_.
    /// LILYPOND-REF: lily/slur-engraver.cc acknowledge_note_column — every
    ///   column engraved while the slur is OPEN joins, which covers a grace run
    ///   attached to any note after the start (the start note's own graces sound
    ///   before the slur opens and stay out).
    /// The stem reads are the canonical houses: a beamed stem ends on
    /// <see cref="Model.BeamLayout.OuterEdgeStaffSpaceAtX(double, bool)"/>
    /// (= LP's stem extent, which
    /// includes the half-thickness beam_end_corrective, stem.cc:142), an
    /// unbeamed one on <see cref="NoteColumnLayout.OutwardTipDeviceY"/>; a grace
    /// stem on the renderer's own recipe (SharedRenderer.GraceNotes — fixed
    /// DefaultStemLength × the grace magstep, or the quanted grace beam).
    /// ⚠️ Grace geometry is rebuilt from the same producers the renderer reads
    /// (SpacingRules.GraceColumns / GraceNoteEngraver.QuantGraceBeam) at scale 1
    /// with no ossia factor — the same simplification the head boxes take — and
    /// without GraceNoteEngraver's script-overhang shift (a fermata on the main
    /// note pushes the drawn run further left than the scored one; no corpus
    /// book pairs a covered grace with such a script yet).
    /// </remarks>
    private static IReadOnlyList<SlurObstacle> BuildSlurObstacles(
        Voice voice, SystemLayout segSystem, SlurItem slur,
        double staffMiddleDown, double segStartX, double segEndX,
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure,
        GraceObstacleGeom?[]? graceGeomCache,
        VoiceCollisionTable voiceShifts)
    {
        const double eps = 0.001;
        var obstacles = new List<SlurObstacle>();

        foreach (var ml in segSystem.Measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex)
                continue;
            if (mi >= voice.Measures.Length)
                continue;

            var items = voice.Measures[mi].Items;
            int lo = mi == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = mi == slur.EndMeasureIndex ? slur.EndItemIndex : items.Length - 1;
            hi = Math.Min(hi, items.Length - 1);

            for (int i = lo; i <= hi; i++)
            {
                // ⚠️ NOT THE GRACE COLUMNS — AddGraceObstaclesForMeasure below adds those, at
                // the GRACE FONT'S ink and on the run's own X offsets. Reading one here would
                // enter it a SECOND time and as a full-size head with a full-size stem:
                // MEASURED on audit/lpreg/lyhygrace, the continuation segment of `g2( … g2)`
                // stopped being flat and dipped 0.35 to clear a stem that is not that tall.
                // (Invisible before session 310: a grace was not an item, so this loop could
                // not reach one.) A grace REST too: AddGraceObstaclesForMeasure builds it, on
                // the run's own column X.
                if (items[i].GraceTime)
                    continue;

                // A REST column participates too: LilyPond's Slur_engraver
                // acknowledges every NoteColumn engraved while the slur is open,
                // and get_encompass_info's no-stem branch reads the COLUMN's Y
                // extent — the rest's own ink ("slur-rest-direction.ly": the
                // interior rests are what push an all-rest slur off its base).
                // LILYPOND-REF: slur-scoring.cc:117-122 — !stem: x_ = the
                //   column's own refpoint (relative_coordinate — the rest ink's
                //   LEFT edge, every rest glyph's bbox starting at 0), NOT the
                //   ink centre the headed branch reads; head_ = stem_ =
                //   notecol->extent(Y)[dir_].
                if (items[i] is RestItem { IsSpacer: false } rest
                    && !rest.IsMultiMeasure)
                {
                    double rx = ml.X + GetItemXOffset(voice, mi, i, ml)
                        + voiceShifts.ShiftOf(mi, slur.VoiceIndex + 1, i);
                    if (rx < segStartX - eps || rx > segEndX + eps)
                        continue;
                    int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    var restBox = GlyphMetrics.GetRestBBox(restValue);
                    // The glyph origin the renderer draws at: a whole rest hangs
                    // one space above the middle, everything else sits on it
                    // (SharedRenderer.DrawRest / the skyline seed's shared rule).
                    // ⚠️ The beam-collision shift (PureBeamShift) is not read —
                    // no corpus book slurs over a beamed rest yet.
                    double originDown = staffMiddleDown - (restValue == 1 ? 1.0 : 0.0);
                    obstacles.Add(new SlurObstacle(
                        rx + restBox.Left,
                        originDown - restBox.Top,
                        originDown - restBox.Bottom));
                    continue;
                }

                int? topPos = MusicItem.EdgeStaffPosition(items[i], preferTop: true);
                int? bottomPos = MusicItem.EdgeStaffPosition(items[i], preferTop: false);
                if (topPos is null || bottomPos is null)
                    continue; // spacer / barline — no column

                // The column X (head's LEFT edge) keeps the window test the same
                // one the extra-object builder runs; the scored x_ is the head's
                // ink CENTER, as LP reads it (:127-132) — which also puts an edge
                // column exactly ON its attachment X, where LP's strictly-inside
                // test (slur-configuration.cc:251) leaves it out of the scoring
                // unless a candidate's tilt shift moves the attachment off it.
                double x = ml.X + GetItemXOffset(voice, mi, i, ml)
                    + voiceShifts.ShiftOf(mi, slur.VoiceIndex + 1, i);
                if (x < segStartX - eps || x > segEndX + eps)
                    continue;
                double obstacleX = x + EndpointHeadHalfWidth(voice, mi, i);

                // Visual top edge = the highest head's GLYPH top (smallest device Y);
                // visual bottom edge = the lowest head's glyph bottom. The glyph's own
                // box, not a nominal half space: LilyPond reads the extremal head's
                // extent, and the black and half heads reach 0.545 ss from their centre
                // where a nominal 0.5 leaves 0.045 — enough to change which candidate
                // wins. MEASURED (samples/nocturne.lys's right hand, bar 4
                // `a4( cis8 e g4 fis8 e)`, Lab sessions/p538/nograce): LilyPond's winner
                // is the flat (2.545, 2.545), scored "L edge=1.60"; the candidate this
                // engine chose with 0.5-boxes, (2.045, 2.545), scores "L edge=1.10,
                // variance=0.64 = 1.74" in LilyPond — the same curve, the variance term
                // read 0.045 closer to the g'' and fis'' heads.
                // LILYPOND-REF: lily/slur-scoring.cc:137-144 get_encompass_info —
                //   h = Stem::extremal_heads (stem)[dir_]; ei.head_ = h->extent (Y)[dir_];
                // LILYPOND-REF: lily/slur-configuration.cc:257-291 score_encompass — head_dy
                //   and the convex head distances read that extent.
                int headValue = NoteColumnLayout.Of(items[i]) is { } headCol
                    ? headCol.NoteValue
                    : GlyphMetrics.NoteValueOf(items[i]);
                var headBox = BowHeadBox(items[i], headValue);
                double topY = (staffMiddleDown - topPos.Value / 2.0) - headBox.Top;
                double bottomY = (staffMiddleDown - bottomPos.Value / 2.0) - headBox.Bottom;

                // stem_ / the stem-x_ move, only when the stem points WITH the slur.
                // LILYPOND-REF: slur-scoring.cc:146-158.
                double stemY = double.NaN;
                if (NoteColumnLayout.Of(items[i]) is { } col
                    && col.HasStem && col.StemUp == slur.CurveUp)
                {
                    double stemX = LayoutUtilities.StemX(
                        x, col.StemUp, col.NoteValue, col.Notehead, BowFont(items[i]));
                    if (TryGetBeamedStemTipDeviceY(beamByMember, slur.VoiceIndex, mi, i, stemX,
                            staffMiddleDown, col.StemUp, out double beamTip))
                        // Beamed: the extent already ends on the stack's outer face
                        // (beam_end_corrective, stem.cc:142); LP adds another half
                        // beam thickness on top (slur-scoring.cc:149-150).
                        stemY = beamTip + (col.StemUp ? -0.5 : 0.5)
                            // The beam's own thickness (a cue beam's is 0.35).
                            * (beamByMember != null && beamByMember.TryGetValue((slur.VoiceIndex, mi, i), out var ownBeam)
                                ? ownBeam.Group.Thickness : EngravingDefaults.BeamThickness);
                    else
                        // Unbeamed: the drawn stem end, from the one house
                        // (staff-top frame, middle at EngravingDefaults.StaffMiddle).
                        stemY = staffMiddleDown - EngravingDefaults.StaffMiddle
                            + col.OutwardTipDeviceY(col.StemUp);
                    obstacleX = stemX;
                }

                obstacles.Add(new SlurObstacle(obstacleX, topY, bottomY, stemY));
            }

            AddGraceObstaclesForMeasure(
                obstacles, voice, slur, graceNotes, graceByMeasure, graceGeomCache,
                ml, mi, hi, staffMiddleDown, segStartX, segEndX, voiceShifts);
        }

        obstacles.Sort((a, b) => a.X.CompareTo(b.X));
        return obstacles;
    }

    /// <summary>
    /// One grace group's slur-obstacle geometry, resolved ONCE per
    /// <c>LayoutSlurs</c> pass (either overload) and cached by group index: the column
    /// springs (<see cref="SpacingRules.GraceColumns"/>) and the beam quant
    /// (<see cref="GraceNoteEngraver.QuantGraceBeam"/>) are the expensive
    /// parts, and re-solving them for every covering slur segment cost +36%
    /// wall-clock on a 300-bar grace-under-slur book (perf-slurgrace300).
    /// </summary>
    private readonly record struct GraceObstacleGeom(
        ImmutableArray<double> Offsets, double Span, double? BeamLeftY, double? BeamRightY);

    /// <summary>
    /// <paramref name="slurs"/> with this staff's GRACE slurs in front: one per grace run that
    /// slurs to its main note — an <c>acciaccatura</c> or an <c>appoggiatura</c> — from the run's
    /// last column to the main note, DOWN (score-grace-settings) unless the voice fixes the
    /// side (a \voiceOne's UP wins). A hand-written
    /// <c>grace { g16( } a8)</c> is not made here: its marks ride the items, and the ordinary
    /// SlurDetector pairs them (session 725). A run whose columns publish
    /// no item address, whose last column is a rest, or whose main note is no note, has none.
    /// </summary>
    /// <remarks>
    /// Every entry to the slur layout calls it (LayoutSlurs, and the preliminary pass before it
    /// buckets slurs by system), so a staff whose only bows are grace slurs still has them;
    /// a list that already holds them is returned as it is. A TAB staff has them too since
    /// session 730: LilyPond's TabVoice runs the same grace-init.ly slur over the grace's digit
    /// (MEASURED, audit/lp-geometry/probes/tab-grace-slur.ly TGA), laid out by BuildTabSlurLayout.
    /// </remarks>
    internal static ImmutableArray<SlurItem> WithGraceSlurs(
        ImmutableArray<SlurItem> slurs, Score score, ImmutableArray<GraceNoteItem> graceNotes,
        int staffIndex, Model.Staff? staff)
    {
        if (graceNotes.IsDefaultOrEmpty)
            return slurs;
        foreach (var s in slurs)
            if (s.StartGraceGroup >= 0)
                return slurs;
        int graceStaff = Math.Max(staffIndex, 0);
        List<SlurItem>? graceSlurs = null;
        for (int gi = 0; gi < graceNotes.Length; gi++)
            {
                var g = graceNotes[gi];
                if (g.StaffIndex != graceStaff || g.Columns.IsDefaultOrEmpty)
                    continue;
                if (g.Type is not (GraceNoteType.Acciaccatura or GraceNoteType.Appoggiatura))
                    continue;
                if (g.ColumnItemIndices.IsDefaultOrEmpty || g.ColumnItemIndices.Length != g.Columns.Length
                    || g.VoiceIndex < 0 || g.VoiceIndex >= score.Voices.Length)
                    continue;
                var last = g.Columns[^1];
                if (last.IsRest)
                    continue;
                var main = ItemAt(score.Voices[g.VoiceIndex], g.MeasureIndex, g.MainNoteItemIndex);
                if (main is not (NoteItem or ChordItem))
                    continue;
                // DOWN, unless the voice fixes the side: \voiceOne's UP wins over the grace
                // settings. MEASURED (probes/grace-inner-slur.ly VOICEACC, LilyPond 2.26.0): an
                // acciaccatura's slur under \voiceOne is drawn UP, 3.89 above the middle line.
                bool up = Collector.VoiceScan.ForcedCurveUpAt(
                    score.Voices, g.VoiceIndex, g.MeasureIndex, g.ColumnItemIndices[^1]) ?? false;
                (graceSlurs ??= new List<SlurItem>()).Add(new SlurItem(
                    up ? last.Highest.StaffPosition : last.Lowest.StaffPosition,
                    MusicItem.EdgeStaffPosition(main, up) ?? 0,
                    curveUp: up,
                    g.MeasureIndex, g.MeasureIndex, g.ColumnItemIndices[^1], g.MainNoteItemIndex,
                    voiceIndex: g.VoiceIndex)
                {
                    StartSourcePosition = g.SourcePosition,
                    StartGraceGroup = gi,
                });
            }
        if (graceSlurs == null)
            return slurs;
        graceSlurs.Sort((a, b) => a.StartMeasureIndex != b.StartMeasureIndex
            ? a.StartMeasureIndex.CompareTo(b.StartMeasureIndex)
            : a.StartItemIndex.CompareTo(b.StartItemIndex));
        return [.. graceSlurs, .. slurs];
    }

    /// <summary>A grace group's column geometry (offsets, span, quanted beam), solved once
    /// per pass into <paramref name="cache"/>.</summary>
    private static GraceObstacleGeom GraceGeomOf(
        Voice voice, ImmutableArray<GraceNoteItem> graceNotes, int gi, GraceObstacleGeom?[] cache,
        MeasureLayout ml)
    {
        if (cache[gi] is { } hit)
            return hit;
        var g = graceNotes[gi];
        var mainItem = ItemAt(voice, g.MeasureIndex, g.MainNoteItemIndex);
        // As GraceNoteEngraver.RunPlacement places the drawn run — the same reading.
        var columns = GraceNoteEngraver.PlacedColumns(
            g.Columns, mainItem, voice.Measures[g.MeasureIndex], g.MainNoteItemIndex, ml);
        var (bl, br) = GraceNoteEngraver.QuantGraceBeam(g, columns.Offsets);
        var geom = new GraceObstacleGeom(columns.Offsets, columns.Span, bl, br);
        cache[gi] = geom;
        return geom;
    }

    /// <summary>Which grace column item <paramref name="itemIndex"/> of measure
    /// <paramref name="measureIndex"/> in voice <paramref name="voiceIndex"/> stands as — its
    /// group in <paramref name="graceNotes"/> and its column there — or null when it is no
    /// grace column of this staff's runs (a main-grid item, or a run that publishes no item
    /// addresses).</summary>
    private static (int Group, int Column)? GraceColumnOf(
        ImmutableArray<GraceNoteItem> graceNotes, Dictionary<int, List<int>>? graceByMeasure,
        int voiceIndex, int measureIndex, int itemIndex)
    {
        if (graceByMeasure is null || !graceByMeasure.TryGetValue(measureIndex, out var groups))
            return null;
        foreach (int gi in groups)
        {
            var g = graceNotes[gi];
            if (g.VoiceIndex != voiceIndex || g.ColumnItemIndices.IsDefaultOrEmpty
                || g.ColumnItemIndices.Length != g.Columns.Length)
                continue;
            int k = g.ColumnItemIndices.IndexOf(itemIndex);
            if (k >= 0)
                return (gi, k);
        }
        return null;
    }

    /// <summary>The head left edge of column <paramref name="column"/> of grace group
    /// <paramref name="gi"/>: <see cref="GraceGroupX"/> plus the column's offset.</summary>
    private static double GraceColumnX(Voice voice, ImmutableArray<GraceNoteItem> graceNotes,
        int gi, int column, GraceObstacleGeom?[] cache, MeasureLayout ml, int voiceIndex,
        VoiceCollisionTable voiceShifts)
    {
        var geom = GraceGeomOf(voice, graceNotes, gi, cache, ml);
        return GraceGroupX(voice, graceNotes[gi], geom, ml, voiceIndex, voiceShifts)
            + (column < geom.Offsets.Length ? geom.Offsets[column] : 0.0);
    }

    /// <summary>
    /// Device Y of a beamed grace run's beam — its OUTER face, the side a slur on the stems'
    /// side hangs from: the upper face of an up run, the lower face of a lower voice's down
    /// run (GraceColumnInfo.StemDown) — at <paramref name="stemX"/>, when column <paramref name="column"/> is beamed on
    /// the side toward the slur's other end (<paramref name="towardRight"/>); null otherwise
    /// (a lone or flagged column, a run with a rest, or the run's outer end on that side).
    /// </summary>
    /// <remarks>
    /// The grace beam is quanted by <see cref="GraceNoteEngraver.QuantGraceBeam"/> and drawn by
    /// the grace renderer, so it is not in the BeamLayouts <c>TryGetBeamedStemTipDeviceY</c>
    /// reads; this is the same line, read the way AddGraceObstaclesForMeasure reads it.
    /// LILYPOND-REF: lily/slur-scoring.cc:549-557 get_base_attachments — a bound whose stem
    /// points the slur's way and is beamed on its inner side attaches 0.5 beyond the beam.
    /// </remarks>
    private static double? GraceBeamOuterFaceDeviceY(Voice voice, ImmutableArray<GraceNoteItem> graceNotes,
        int gi, int column, bool towardRight, GraceObstacleGeom?[] cache, MeasureLayout ml, int voiceIndex,
        VoiceCollisionTable voiceShifts, double stemX, double staffMiddleDown)
    {
        var g = graceNotes[gi];
        int last = g.Columns.Length - 1;
        if (last < 1 || (towardRight ? column >= last : column <= 0))
            return null;
        foreach (var c in g.Columns)
            if (c.IsRest)
                return null;
        var geom = GraceGeomOf(voice, graceNotes, gi, cache, ml);
        if (geom.BeamLeftY is not { } bl || geom.BeamRightY is not { } br || geom.Offsets.Length <= last)
            return null;
        double groupX = GraceGroupX(voice, g, geom, ml, voiceIndex, voiceShifts);
        var font = g.HeadFont;
        bool up = g.StemUp;
        double xL = LayoutUtilities.StemX(groupX + geom.Offsets[0], up, noteValue: 4, NoteheadStyle.Default, font);
        double xR = LayoutUtilities.StemX(groupX + geom.Offsets[last], up, noteValue: 4, NoteheadStyle.Default, font);
        double t = xR - xL > 0.001 ? (stemX - xL) / (xR - xL) : 0.0;
        double centerUp = (bl + (br - bl) * t) / 2.0;
        return staffMiddleDown - (centerUp + (up ? 1 : -1) * EngravingDefaults.GraceBeamThickness / 2.0);
    }

    /// <summary>Where a grace group's first column stands: its main note's column, shifted
    /// as that column is, less the run's span. Column k's head left edge is this plus
    /// <c>geom.Offsets[k]</c>.</summary>
    private static double GraceGroupX(Voice voice, GraceNoteItem g, GraceObstacleGeom geom,
        MeasureLayout ml, int voiceIndex, VoiceCollisionTable voiceShifts)
        => ml.X
            + GetItemXOffset(voice, g.MeasureIndex, g.MainNoteItemIndex, ml)
            + voiceShifts.ShiftOf(g.MeasureIndex, voiceIndex + 1, g.MainNoteItemIndex)
            - geom.Span;

    /// <summary>
    /// Adds the grace columns the slur covers in measure <paramref name="mi"/> to
    /// <paramref name="obstacles"/> — heads at the grace font's own ink, stems
    /// forced UP (score-grace-settings) or a lower voice's DOWN (GraceColumnInfo.StemDown),
    /// the group's geometry rebuilt from the
    /// same producers the renderer reads. See
    /// <see cref="BuildSlurObstacles"/> for the LP references and disclosures.
    /// </summary>
    private static void AddGraceObstaclesForMeasure(
        List<SlurObstacle> obstacles, Voice voice, SlurItem slur,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure, GraceObstacleGeom?[]? graceGeomCache,
        MeasureLayout ml, int mi, int hi,
        double staffMiddleDown, double segStartX, double segEndX,
        VoiceCollisionTable voiceShifts)
    {
        if (graceByMeasure is null || graceGeomCache is null
            || !graceByMeasure.TryGetValue(mi, out var groupIndices))
            return;
        const double eps = 0.001;

        foreach (int gi in groupIndices)
        {
            var g = graceNotes[gi];
            // Which columns the slur covers, read off each column's own item address: from
            // the start item to the end item, both included — the window the main-grid loop
            // reads. A run before the start note sounds BEFORE the slur opens, so LP's
            // engraver never acknowledges it into this slur; a slur that starts or ends IN
            // the run (`grace { d'16( e') }`, `grace { g16( } a8)`, a grace slur) covers the
            // columns from its own bound on. A run that publishes no item addresses falls
            // back to its main note's place, which is the same answer for a slur bound on
            // main-grid items (the run's items stand just before its main note).
            bool addressed = !g.ColumnItemIndices.IsDefaultOrEmpty
                && g.ColumnItemIndices.Length == g.Columns.Length;
            if (!addressed)
            {
                bool afterStart = mi > slur.StartMeasureIndex
                    || g.MainNoteItemIndex > slur.StartItemIndex;
                bool beforeEnd = mi < slur.EndMeasureIndex
                    || g.MainNoteItemIndex <= slur.EndItemIndex;
                if (!afterStart || !beforeEnd || g.MainNoteItemIndex > hi)
                    continue;
            }

            var geom = GraceGeomOf(voice, graceNotes, gi, graceGeomCache, ml);
            double groupX = GraceGroupX(voice, g, geom, ml, slur.VoiceIndex, voiceShifts);

            var font = g.HeadFont;
            double headHalf = font.NoteheadBlack.Top;
            // The quanted grace beam (null for a lone / unbeamable run): the
            // scored line's staff-position pair at the two OUTER STEMS, exactly
            // what the renderer anchors the drawn beam on.
            var (beamL, beamR) = (geom.BeamLeftY, geom.BeamRightY);
            // The run's stem direction: up, or a lower voice's down (GraceColumnInfo.StemDown).
            bool stemUp = g.StemUp;
            double StemXAt(int k) => LayoutUtilities.StemX(
                groupX + (k < geom.Offsets.Length ? geom.Offsets[k] : 0.0),
                stemUp, noteValue: 4, NoteheadStyle.Default, font);

            for (int k = 0; k < g.Columns.Length; k++)
            {
                if (addressed
                    && !((mi > slur.StartMeasureIndex || g.ColumnItemIndices[k] >= slur.StartItemIndex)
                        && (mi < slur.EndMeasureIndex || g.ColumnItemIndices[k] <= slur.EndItemIndex)))
                    continue;
                double hx = groupX + (k < geom.Offsets.Length ? geom.Offsets[k] : 0.0);
                if (hx < segStartX - eps || hx > segEndX + eps)
                    continue;
                var note = g.Columns[k];
                // A REST column has no head and no stem (GraceColumnInfo.IsRest): the slur
                // reads its glyph's box, as it reads an ordinary rest's (BuildSlurObstacles'
                // rest branch — LP's no-stem encompass, slur-scoring.cc:117-122: x_ = the
                // column's refpoint, head_ = stem_ = its Y extent). The glyph is the STAFF's
                // size — general-grace-settings never names Rest (SharedRenderer.GraceNotes)
                // — at LP's unvoiced position 0 (rest.cc:76-81). MEASURED (Lab sessions/p723/gr,
                // LilyPond 2.26.0): `e'4( grace { r16 f'16 } g'4)` — LP lifts the slur over the
                // rest; until session 723 Lily# ran under it (session 717 skipped the column so
                // as not to throw; the controls without the rest agree exactly).
                if (note.IsRest)
                {
                    int restValue = GlyphMetrics.NoteValueOf(note.BaseDuration);
                    var restBox = GlyphMetrics.GetRestBBox(restValue);
                    // Where the rest is DRAWN: lifted off the held note before it
                    // (GraceRestShift), found at the column's own item address.
                    int restItem = k < g.ColumnItemIndices.Length ? g.ColumnItemIndices[k] : -1;
                    double lift = GraceRestShift(voice.Measures, mi, restItem, 5) ?? 0.0;
                    double originDown = staffMiddleDown - (restValue == 1 ? 1.0 : 0.0) - lift / 2.0;
                    obstacles.Add(new SlurObstacle(
                        hx + restBox.Left, originDown - restBox.Top, originDown - restBox.Bottom));
                    continue;
                }
                // A slur under a grace is kept off the column's NEAREST ink, so an UP
                // slur reads the top head of a chord and a DOWN slur its bottom one.
                // For a single head the two are one number and the books do not move.
                int headPos = slur.CurveUp
                    ? note.Highest.StaffPosition : note.Lowest.StaffPosition;
                double headCenterY = staffMiddleDown - headPos / 2.0;

                // Grace stems are forced UP whatever the pitch
                // (scm/music-functions.scm:652-656 score-grace-settings) — DOWN in a lower
                // voice — so the stem participates only under a slur on its side.
                double stemY = double.NaN;
                double obstacleX = hx + font.NoteheadBlackAdvance / 2.0;
                if (slur.CurveUp == stemUp)
                {
                    double stemX = StemXAt(k);
                    if (beamL is { } bl && beamR is { } br && g.Columns.Length > 1)
                    {
                        // Beamed run: the quanted line interpolated to this stem's
                        // X, plus a full grace beam thickness — half for the stem
                        // extent's beam_end_corrective, half for LP's encompass
                        // margin (slur-scoring.cc:149-150).
                        double xL = StemXAt(0), xR = StemXAt(g.Columns.Length - 1);
                        double t = xR - xL > 0.001 ? (stemX - xL) / (xR - xL) : 0.0;
                        double centerUp = (bl + (br - bl) * t) / 2.0;
                        stemY = staffMiddleDown
                            - (centerUp + (stemUp ? 1 : -1) * EngravingDefaults.GraceBeamThickness);
                    }
                    else
                    {
                        // Lone / flagged grace: the drawn stem end — the stem rule the
                        // renderer draws a grace-time stem with (StemDetailsOf →
                        // GrobFontSize.GraceStemDetails: general-grace-settings' length-fraction
                        // 0.8 and no-stem-extend), from the column's highest head. MEASURED
                        // (Lab sessions/p723/gd, LilyPond 2.26.0): b'16 in a grace, tip 2.7
                        // above its head, drawn 2.70. Until session 723 this read 3.5 × the
                        // head's magstep(−3) = 2.475, and a slur over a flagged grace sat 0.5
                        // low. ⚠️ The flag is not read (LP's encompass reads the stem extent
                        // alone, slur-scoring.cc:146-151).
                        int durationLog = StemCalculator.GetDurationLog(note.BaseDuration.Denominator);
                        stemY = staffMiddleDown - StemCalculator.CalculateStemEndPosition(
                            stemUp, durationLog,
                            (stemUp ? note.Highest : note.Lowest).StaffPosition,
                            GrobFontSize.GraceStemDetails) / 2.0;
                    }
                    obstacleX = stemX;
                }

                obstacles.Add(new SlurObstacle(
                    obstacleX, headCenterY - headHalf, headCenterY + headHalf, stemY));
            }
        }
    }

    /// <summary>
    /// Extra-encompass objects for a slur segment: the augmentation-dot rows of
    /// every dotted note, chord member and rest the slur covers, as extent boxes
    /// in device coordinates. LilyPond's Slur_engraver acknowledges each Dots
    /// grob into the open slur's <c>encompass-objects</c>; Dots declares
    /// <c>avoid-slur: inside</c>, so the scorer keeps the curve clear of them —
    /// "Slurs avoid dots" (input/regression/slur-dot-collision.ly).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-engraver.cc:78 ADD_ACKNOWLEDGER_FOR(acknowledge_extra_object, dots).
    /// LILYPOND-REF: scm/define-grobs.scm Dots — (avoid-slur . inside).
    /// LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos — a
    ///   dots-interface grob's Y extent widens by 0.2, then every box widens by
    ///   thickness*0.5 vertically and thickness*1.0 horizontally; penalty =
    ///   extra-object-collision-penalty.
    /// The dot row geometry is the same recipe the skyline seed and the renderer
    /// spell (SkylineBuilder.AddMusicItemToSkylines / MergeDotRow): base X = head
    /// ink right + one dot width, row advance two dot widths, position from
    /// DotConfiguration.Resolve. The collision DotAdjustment is not read here
    /// either (the same simplification the skyline seed discloses).
    /// ⚠️ Two further simplifications against that seed: geometry is read at
    /// scale 1 (the same choice BuildSlurObstacles makes with its head boxes),
    /// and DotConfiguration.Resolve runs with NO direction where the seed feeds
    /// the voice-forced one — a forced-voice score could seat the scored dot row
    /// one position off the drawn one.
    /// </remarks>
    /// <param name="tieLayouts">This staff's ties, already laid out (the tie pass runs
    /// before the slur pass — LayoutEngine.Prelim). A tie whose END note the slur covers
    /// joins the set as a spanner box, and its two ends go to
    /// <paramref name="tieEnds"/> for the scorer's forbidden-attachment term.</param>
    /// <param name="tieEnds">The covered ties' end points, device Y down — null when the
    /// slur covers no tie.</param>
    private static IReadOnlyList<SlurExtraObject> BuildSlurExtraObjects(
        Rendering.ScoreTextMetrics fonts,
        Voice voice, SystemLayout segSystem, SlurItem slur,
        double staffMiddleDown, double segStartX, double segEndX,
        out List<(double X, double Y)>? tieEnds,
        ImmutableArray<TupletBracketLayout> tupletNumbers = default,
        ImmutableArray<TupletBracketItem> tupletItems = default,
        ImmutableArray<InsideSlurScript> insideScripts = default,
        ImmutableArray<TieLayout> tieLayouts = default,
        IReadOnlyDictionary<int, int>? measureToSystemIdx = null,
        VoiceCollisionTable? voiceShifts = null)
    {
        // thickness_ = Slur.thickness (1.2, define-grobs.scm) * the layout
        // line-thickness dimension (0.1 ss at default staff size) = 0.12 ss.
        // LILYPOND-REF: lily/slur-scoring.cc:248-251 line_thickness field.
        const double slurThickness = 1.2 * 0.1;
        const double eps = 0.001;
        var extras = new List<SlurExtraObject>();
        tieEnds = null;

        void AddDotRow(int dotCount, double dotStartX, double dotCenterDeviceY)
        {
            var dotBox = GlyphMetrics.AugmentationDot;
            double advance = 2 * dotBox.Width;
            double left = dotStartX + dotBox.Left - slurThickness;
            double right = dotStartX + (dotCount - 1) * advance + dotBox.Right + slurThickness;
            // Device Y down: top edge = centre - (half height + widens). The bare
            // extent (the glyph's own, no 0.2, no thickness) rides along for the avoid
            // point, as generate_avoid_offsets reads the Dots grob.
            double halfH = dotBox.Top + 0.2 + slurThickness * 0.5;
            extras.Add(new SlurExtraObject(
                left, right,
                dotCenterDeviceY - halfH, dotCenterDeviceY + halfH,
                SlurAvoidType.Inside,
                SlurScoreParameters.Default.ExtraObjectCollisionPenalty,
                AvoidTopY: dotCenterDeviceY - dotBox.Top,
                AvoidBottomY: dotCenterDeviceY - dotBox.Bottom));
        }

        // The accidentals of every column the slur covers, its two ends included: an
        // Accidental is avoid-slur 'inside, which the slur engraver acknowledges as an extra
        // object (lily/slur-engraver.cc:73 ADD_ACKNOWLEDGER_FOR inline_accidental;
        // scm/define-grobs.scm:39 Accidental). Scored with accidental-collision (3), not the
        // extra-object 50, and read along X not at the centre but where the glyph's ink
        // reaches toward the slur (lily/slur-scoring.cc:860-877 get_extra_encompass_infos —
        // flat LEFT, sharp 0.5·dir, natural −dir; a parenthesized or restore-first one at
        // the centre). Until session 649 no accidental entered: Butterfly.lys (Lab corpus)
        // `b,,8 b,,( dis,)` drew the slur through the sharp once it left the head.
        // Same frame as the beam's accidental collisions (AddAccidentalCollisions): the
        // packed column X when the collector packed one, else the per-item solve; scale 1,
        // the simplification this set already discloses for heads and dots.
        void AddAccidentals(MusicItem item, double columnX)
        {
            // A cue note's accidental is the cue font's glyph, placed against the cue head —
            // the solve SharedRenderer.DrawNote / DrawChord draw by (BowFont).
            var font = BowFont(item);
            IEnumerable<AccidentalLayout> laid = item switch
            {
                NoteItem { Accidental: { } acc, AccidentalX: { } px } pn
                    => [new AccidentalLayout(pn.StaffPosition, acc, px, pn.IsCourtesy)],
                NoteItem { Accidental: not null } n
                    => BeamAccidentalColumn.CalculateSinglePosition(n, font, font) is { } one ? [one] : [],
                ChordItem c when c.Notes.Any(m => m.Accidental != null) => ChordAccidentalLayouts(c, font),
                _ => [],
            };
            int dir = slur.CurveUp ? 1 : -1;
            foreach (var layout in laid)
            {
                var box = font is { } f
                    ? GlyphMetrics.GetAccidentalBBox(f, layout.Accidental)
                    : GlyphMetrics.GetAccidentalBBox(layout.Accidental);
                double width = box.Width;
                if (layout.IsCourtesy)
                    width += font is { } pf
                        ? pf.AccidentalLeftParen.Width + pf.AccidentalRightParen.Width
                        : GlyphMetrics.AccidentalLeftParen.Width + GlyphMetrics.AccidentalRightParen.Width;
                double left = columnX + layout.XOffset;
                double centreDown = staffMiddleDown - layout.StaffPosition / 2.0;
                double topDown = centreDown - box.Top, bottomDown = centreDown - box.Bottom;
                double idx = layout.IsCourtesy || layout.Accidental.StartsWith("natural", StringComparison.Ordinal)
                        && layout.Accidental != "natural"
                    ? 0.0
                    : layout.Accidental switch
                    {
                        "flat" or "doubleFlat" => -1.0,
                        "sharp" => 0.5 * dir,
                        "natural" => -dir,
                        _ => 0.0,
                    };
                extras.Add(new SlurExtraObject(
                    left - slurThickness, left + width + slurThickness,
                    topDown - slurThickness * 0.5, bottomDown + slurThickness * 0.5,
                    SlurAvoidType.Inside,
                    SlurScoreParameters.Default.AccidentalCollision,
                    Idx: idx,
                    AvoidTopY: topDown,
                    AvoidBottomY: bottomDown));
            }
        }

        foreach (var ml in segSystem.Measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex)
                continue;
            if (mi >= voice.Measures.Length)
                continue;

            var items = voice.Measures[mi].Items;
            int lo = mi == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = mi == slur.EndMeasureIndex ? slur.EndItemIndex : items.Length - 1;
            hi = Math.Min(hi, items.Length - 1);

            for (int i = lo; i <= hi; i++)
            {
                double x = ml.X + GetItemXOffset(voice, mi, i, ml)
                    + (voiceShifts?.ShiftOf(mi, slur.VoiceIndex + 1, i) ?? 0);
                if (x < segStartX - eps || x > segEndX + eps)
                    continue;

                if (!items[i].GraceTime)
                    AddAccidentals(items[i], x);

                switch (items[i])
                {
                    // Where a column's dots stand is DotColumn.Reserved's — the house the
                    // renderer draws by (head ink, one dot width, a flag's push) — on
                    // DotConfiguration's rows.
                    case NoteItem { Dots: > 0 } note:
                    {
                        int value = GlyphMetrics.NoteValueOf(note.BaseDuration);
                        var (dotOffset, rows) = DotColumn.Reserved(
                            note, value, GlyphMetrics.GetNoteheadBBox(value).Right);
                        foreach (int p in rows)
                            AddDotRow(note.Dots, x + dotOffset, staffMiddleDown - p / 2.0);
                        break;
                    }
                    case ChordItem { Dots: > 0 } chord when chord.Notes.Length > 0:
                    {
                        int value = GlyphMetrics.NoteValueOf(chord.BaseDuration);
                        // The staff's own font, matching the box on the line below.
                        var headOffsets = ChordHeadPositioning.CalculateOffsets(
                            chord.Notes, chord.StemUp, value);
                        var (dotOffset, rows) = DotColumn.Reserved(
                            chord, value,
                            GlyphMetrics.GetNoteheadBBox(value).Right + Math.Max(0, headOffsets.Max()));
                        foreach (int p in rows)
                            AddDotRow(chord.Dots, x + dotOffset, staffMiddleDown - p / 2.0);
                        break;
                    }
                    case RestItem { Dots: > 0, IsSpacer: false, IsMultiMeasure: false } rest:
                    {
                        int value = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                        // A rest's dots sit one dot width past its glyph, in the space
                        // above the middle line (row 1), as the renderer draws them.
                        var (dotOffset, rows) = DotColumn.Reserved(
                            rest, value, GlyphMetrics.GetRestBBox(value).Right);
                        AddDotRow(rest.Dots, x + dotOffset, staffMiddleDown - rows[0] / 2.0);
                        break;
                    }
                }
            }
        }

        // Tuplet NUMBERS the slur's span covers — LP's engraver acknowledges the
        // number while the slur is open; the box is the number's ink (centred on
        // the bracket midpoint, the same TextFontMetrics read the staff skyline
        // makes) plus the standard thickness widens. 'inside with the default
        // extra-object penalty — the additional_ys extension is what lets the
        // grid climb over it (slur-shift-region.ly's claim).
        // LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos —
        //   the non-slur branch; no dots-0.2, ye.widen(th*0.5), xe.widen(th*1.0).
        // ⚠️ Two stand-ins, disclosed: ⑴ LP acknowledges a number only when its
        //   tuplet STARTS while the slur is open (engraver timing); this gate is
        //   plain time-range OVERLAP, so a tuplet begun before the slur also
        //   contributes. ⑵ The number's X centres on Lily#'s bracket span (bound
        //   stem faces); LP centres on the DRAWN bracket, which X-positions /
        //   shorten-pair extend ~0.2 per side (unported, the bracket X regime) —
        //   the box can sit a few tenths off LP's along X.
        if (!tupletNumbers.IsDefaultOrEmpty)
        {
            foreach (var t in tupletNumbers)
            {
                if (string.IsNullOrEmpty(t.NumberText))
                    continue;
                // Time overlap with the slur (item-level at the boundary measures
                // when the source item is known; measure-level otherwise).
                int tStart = 0, tEnd = int.MaxValue;
                if (!tupletItems.IsDefaultOrEmpty
                    && t.SourceIndex >= 0 && t.SourceIndex < tupletItems.Length)
                {
                    tStart = tupletItems[t.SourceIndex].StartNoteIndex;
                    tEnd = tupletItems[t.SourceIndex].EndNoteIndex;
                }
                bool startsAfterSlur = t.MeasureIndex > slur.EndMeasureIndex
                    || (t.MeasureIndex == slur.EndMeasureIndex && tStart > slur.EndItemIndex);
                bool endsBeforeSlur = t.MeasureIndex < slur.StartMeasureIndex
                    || (t.MeasureIndex == slur.StartMeasureIndex && tEnd < slur.StartItemIndex);
                if (startsAfterSlur || endsBeforeSlur)
                    continue;
                // This SEGMENT only (a broken slur's other segment keeps its own).
                if (t.NumberX < segStartX - eps || t.NumberX > segEndX + eps)
                    continue;

                double halfW = fonts.Advance(
                    t.NumberText, TupletBracketEngraver.NumberEm(fonts),
                    Rendering.TextRole.Tuplet, TupletBracketEngraver.NumberStyle(fonts)) / 2.0;
                double halfH = fonts.InkHeight(
                    t.NumberText, TupletBracketEngraver.NumberEm(fonts),
                    Rendering.TextRole.Tuplet, TupletBracketEngraver.NumberStyle(fonts)) / 2.0;
                // NumberYUp is staff-spaces above this staff's TOP line (the
                // layout ran with no staff offset); page device Y down.
                double cy = staffMiddleDown - EngravingDefaults.StaffMiddle - t.NumberYUp;
                extras.Add(new SlurExtraObject(
                    t.NumberX - halfW - slurThickness,
                    t.NumberX + halfW + slurThickness,
                    cy - halfH - slurThickness * 0.5,
                    cy + halfH + slurThickness * 0.5,
                    SlurAvoidType.Inside,
                    SlurScoreParameters.Default.ExtraObjectCollisionPenalty,
                    AvoidTopY: cy - halfH,
                    AvoidBottomY: cy + halfH));
            }
        }

        // The SCRIPTS the slur's span covers that declare avoid-slur #'inside — a
        // staccato, staccatissimo, tenuto, marcato, stopped or (inverted) turn. They are
        // NOT moved out of the bow's way: Slur_engraver acknowledges them into the open
        // slur's encompass-objects and the BOW is scored around them, which is the exact
        // mirror of the 'around/'outside marks ArticulationEngraver rides off the finished
        // curve. Same box recipe as the tuplet number above (no dots-0.2 widen).
        // LILYPOND-REF: lily/slur.cc:364-387 auxiliary_acknowledge_extra_object — a tie or
        //   an 'inside grob goes to add_extra_encompass on every OPEN slur; 'around and
        //   'outside instead chain outside_slur_callback onto the grob itself.
        // LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos — the
        //   non-slur branch: ye.widen(th*0.5), xe.widen(th*1.0), extra-object penalty;
        //   lily/slur-scoring.cc:695-704 generate_avoid_offsets puts the box's dir edge in
        //   the curve's avoid list too (SlurScoringProblem.BuildAvoidOffsets).
        // The gate is the engraver's TIMESTEP rule, SlurOpenAt: a script is acknowledged
        //   at the timestep of its note, and joins every slur open or ending there. The
        //   same rule CoveringSlurPiece uses for the other direction.
        // ⚠️ The script boxes arrive from a walk run WITHOUT slurs
        //   (ArticulationEngraver.InsideSlurScriptLayouts) — sound because an 'inside mark's
        //   placement does not depend on the bow; see that method's remark.
        // ⚠️ THE SEGMENT TEST IS THE MEASURE, NOT THE COLUMN WINDOW the dots and the tuplet
        //   number are filtered by. A script's X is its own ink's CENTRE on the head, which
        //   sits half a head to the RIGHT of the column X those windows are cut at — gating a
        //   script by [segStartX, segEndX] drops every mark on the slur's last note, which is
        //   the whole of book SSC. Measures never straddle a break, so membership of this
        //   system's bars is an exact segment test.
        if (!insideScripts.IsDefaultOrEmpty)
        {
            foreach (var (s, voiceIndex) in insideScripts)
            {
                // LP's Slur_engraver lives in the Voice context: a bow never sees another
                // voice's script (the key the 'around direction looks slurs up by too).
                if (voiceIndex != slur.VoiceIndex)
                    continue;
                if (!SlurOpenAt(slur, s.MeasureIndex, s.ItemIndex))
                    continue;
                // This SEGMENT only (a broken slur's other piece keeps its own).
                bool onThisSystem = false;
                foreach (var ml in segSystem.Measures)
                    if (ml.MeasureIndex == s.MeasureIndex) { onThisSystem = true; break; }
                if (!onThisSystem)
                    continue;

                var ink = s.Ink;
                // LP's own guard: an empty extent contributes nothing (a tab letter's
                // 0-extent stand-in reaches here through the same list).
                if (ink.Right - ink.Left <= 0 || ink.Top - ink.Bottom <= 0)
                    continue;

                // YUp is up-positive about THIS staff's middle line; page device Y down.
                double topDown = staffMiddleDown - (s.YUp + ink.Top);
                double bottomDown = staffMiddleDown - (s.YUp + ink.Bottom);
                extras.Add(new SlurExtraObject(
                    s.X + ink.Left - slurThickness,
                    s.X + ink.Right + slurThickness,
                    topDown - slurThickness * 0.5,
                    bottomDown + slurThickness * 0.5,
                    SlurAvoidType.Inside,
                    SlurScoreParameters.Default.ExtraObjectCollisionPenalty,
                    AvoidTopY: topDown,
                    AvoidBottomY: bottomDown));
            }
        }

        // The TIES the slur covers. LilyPond's Slur_engraver acknowledges every Tie, and a
        // Tie goes to the open slurs' encompass-objects UNCONDITIONALLY (before any
        // avoid-slur is read); its box is the tie STENCIL's — the bezier sandwich's true
        // extent — with the standard thickness widens, 'inside (Tie's avoid-slur), the
        // extra-object penalty; and, being a Spanner, it never takes the "over an edge
        // head" attachment read. Its two ends are the forbidden attachments. Until
        // 2026-09-23 no tie entered the set, so a slur over `e4~ e8` ran through the tie's
        // apex (samples/nocturne.lys bar 4): LilyPond lifts that slur one whole 0.5 ss
        // — its curve WITHOUT the tie is Lily#'s old one to the hundredth (Lab
        // sessions/p536/nocturne/notie.ly).
        // LILYPOND-REF: lily/slur-engraver.cc:79 acknowledge_extra_object (tie), the
        //   ADD_END_ACKNOWLEDGER_FOR line;
        // LILYPOND-REF: lily/slur.cc:365-387 auxiliary_acknowledge_extra_object —
        //   has_interface<Tie> (e) → add_extra_encompass on slurs[] and end_slurs[];
        // LILYPOND-REF: lily/slur-scoring.cc:850-884 get_extra_encompass_infos — the
        //   non-slur branch: g->extent (X/Y), ye.widen (th*0.5), xe.widen (th*1.0);
        // LILYPOND-REF: lily/tie.cc:249-253 Tie::print — Lookup::slur (b, get_grob_direction
        //   × base_thick, line_thick, dash_definition); lily/lookup.cc:508-514
        //   bezier_sandwich — the box is both curves' extents united, widened by half the pen;
        // LILYPOND-REF: lily/tie-engraver.cc:312 make_spanner ("Tie") in process_acknowledged
        //   — the Tie spanner is made at the timestep of its SECOND head, so the slur that
        //   sees it is one open, or ending, at the tie's END note (SlurOpenAt), in the
        //   tie's own VOICE (Slur_engraver lives in the Voice). A broken tie counts on the
        //   system its piece is drawn on.
        if (!tieLayouts.IsDefaultOrEmpty)
        {
            foreach (var t in tieLayouts)
            {
                var tie = t.Tie;
                if (tie.VoiceIndex != slur.VoiceIndex)
                    continue;
                if (!SlurOpenAt(slur, tie.EndMeasureIndex, tie.EndItemIndex))
                    continue;
                // This SEGMENT only: the piece's own system.
                int pieceMeasure = t.RenderMeasureIndex >= 0 ? t.RenderMeasureIndex : tie.StartMeasureIndex;
                if (measureToSystemIdx != null
                    && measureToSystemIdx.TryGetValue(pieceMeasure, out int pieceSystem)
                    && pieceSystem != segSystem.SystemIndex)
                    continue;

                var (left, right, topDown, bottomDown) = TieStencilBox(t);
                extras.Add(new SlurExtraObject(
                    left - slurThickness,
                    right + slurThickness,
                    topDown - slurThickness * 0.5,
                    bottomDown + slurThickness * 0.5,
                    SlurAvoidType.Inside,
                    SlurScoreParameters.Default.ExtraObjectCollisionPenalty,
                    IsSpanner: true,
                    AvoidTopY: topDown,
                    AvoidBottomY: bottomDown));
                // control_[0] and control_[3]: the bow's own ends, device Y down.
                (tieEnds ??= new List<(double X, double Y)>(2)).Add((t.StartX, -t.StartYUp));
                tieEnds.Add((t.EndX, -t.EndYUp));
            }
        }

        // ⚠️ NOT PORTED, named rather than left to be discovered: the COMMAND COLUMN.
        //   Slur_engraver::stop_translation_timestep adds the timestep's currentCommandColumn
        //   (the non-musical PaperColumn — bar line, clef change, key change) to every slur
        //   ending there, and to every open slur at a timestep with no start event
        //   (lily/slur-engraver.cc:332-340). Its extent is the column's grobs'; a bare bar
        //   line's column has no Y extent and contributes nothing, so no fixture has yet
        //   shown the difference. A mid-slur clef or key change is where it would.
        return extras;
    }

    /// <summary>
    /// The engraver's TIMESTEP rule for what a slur acknowledges: a grob engraved at the
    /// timestep of (<paramref name="measureIndex"/>, <paramref name="itemIndex"/>) in the
    /// slur's own voice joins the slur when the slur is OPEN there — started at that
    /// timestep or before and not yet ended — or ENDING there. The item order of a voice
    /// IS its timestep order (a grace column is an earlier item than the note it
    /// precedes, as LilyPond's grace moments are earlier timesteps), so the rule is the
    /// closed interval from the slur's start note to its end note.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-engraver.cc:295-327 process_music — the stop events move a
    ///   slur from slurs_ to end_slurs_ (try_to_end, :273-292) and the start events create
    ///   the new ones into slurs_ (create_slur, :181-210), BEFORE the timestep's grobs are
    ///   acknowledged; :354-356 stop_translation_timestep hands objects_to_acknowledge_ to
    ///   auxiliary_acknowledge_extra_object over slurs_ AND end_slurs_; :361 end_slurs_ is
    ///   cleared at the end of the timestep, so a slur sees exactly the timesteps from its
    ///   start note to its end note, both included.
    /// ⚠️ One voice only — the caller compares voices first; Slur_engraver lives in the
    ///   Voice context and never sees another voice's grobs.
    /// </remarks>
    private static bool SlurOpenAt(SlurItem slur, int measureIndex, int itemIndex)
    {
        if (measureIndex < slur.StartMeasureIndex || measureIndex > slur.EndMeasureIndex)
            return false;
        if (measureIndex == slur.StartMeasureIndex && itemIndex < slur.StartItemIndex)
            return false;
        if (measureIndex == slur.EndMeasureIndex && itemIndex > slur.EndItemIndex)
            return false;
        return true;
    }

    /// <summary>
    /// A drawn tie's STENCIL box, device Y down: the bezier sandwich's two curves — the
    /// centreline's interior controls pushed out and in by half the mid thickness, normal
    /// to the chord — each taken at its TRUE extent and united, then widened by half the
    /// round pen. The same sandwich the renderer strokes and the staff skyline flattens
    /// (SkylineBuilder.SeedBowInk), read here as the box LilyPond's <c>g->extent</c>
    /// answers for a Tie.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lookup.cc:395-410 Lookup::slur (…, dash_details) — perp =
    ///   0.5·curvethick·(−dir.y, dir.x); back.control_[1,2] += perp; curve.control_[1,2]
    ///   −= perp;
    /// LILYPOND-REF: lily/lookup.cc:508-514 bezier_sandwich — x/y extents of both curves
    ///   united (Bezier::extent), b.widen (0.5·thickness) with the LINE thickness;
    /// LILYPOND-REF: lily/tie.cc:231-235 Tie::print — base_thick = staff_thick × thickness
    ///   (1.2), line_thick = staff_thick × line-thickness (0.8); :252 the direction sign on
    ///   base_thick only swaps which curve is "back", and the union is the same.
    /// </remarks>
    private static (double Left, double Right, double TopDown, double BottomDown) TieStencilBox(TieLayout t)
    {
        double dx = t.EndX - t.StartX, dy = t.EndYUp - t.StartYUp;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double ux = len > 0 ? dx / len : 1.0, uy = len > 0 ? dy / len : 0.0;
        double half = 0.5 * EngravingDefaults.TieMidThickness;
        double px = -uy * half, py = ux * half;
        var back = new Bezier(t.StartX, t.StartYUp,
            t.Control1.X + px, t.Control1.Y + py, t.Control2.X + px, t.Control2.Y + py,
            t.EndX, t.EndYUp);
        var front = new Bezier(t.StartX, t.StartYUp,
            t.Control1.X - px, t.Control1.Y - py, t.Control2.X - px, t.Control2.Y - py,
            t.EndX, t.EndYUp);
        var (bx0, bx1) = back.Extent(yAxis: false);
        var (fx0, fx1) = front.Extent(yAxis: false);
        var (by0, by1) = back.Extent(yAxis: true);
        var (fy0, fy1) = front.Extent(yAxis: true);
        double pen = 0.5 * EngravingDefaults.BowEndRounding;
        double left = Math.Min(bx0, fx0) - pen, right = Math.Max(bx1, fx1) + pen;
        double topUp = Math.Max(by1, fy1) + pen, bottomUp = Math.Min(by0, fy0) - pen;
        return (left, right, -topUp, -bottomUp);
    }

    /// <param name="fonts">The SCORE's text metrics — a tab slur clears fret digits whose em
    /// is the plan's; a per-staff <c>Score</c> carries no plan, so the caller passes the
    /// enclosing score's.</param>
    public ImmutableArray<SlurLayout> LayoutSlurs(Rendering.ScoreTextMetrics fonts, Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1, Model.Staff? staff = null, ImmutableArray<GraceNoteItem> graceNotes = default, ImmutableArray<BeamLayout> beamLayouts = default, Func<ImmutableArray<InsideSlurScript>>? insideScripts = null, ImmutableArray<TieLayout> tieLayouts = default)
        => LayoutSlurs(fonts, _slurDetector.DetectSlurs(score), score, systems, staffIndex, staff,
            graceNotes, beamLayouts, insideScripts, tieLayouts);

    /// <summary>The same, on slurs the caller has ALREADY detected — the slur twin of
    /// <see cref="LayoutTies(Rendering.ScoreTextMetrics, ImmutableArray{TieItem}, Score, ImmutableArray{SystemLayout}, int, Model.Staff?)"/>,
    /// and for the same reason.</summary>
    /// <param name="insideScripts">This staff's <c>avoid-slur = #'inside</c> marks, already
    /// placed in the staff's own frame — see
    /// <see cref="ArticulationEngraver.InsideSlurScriptLayouts"/>. A FACTORY, not an array,
    /// so that the extra script walk is paid only by a staff that has slurs at all: a
    /// script-heavy but slur-free book must not buy it once per staff per system.</param>
    /// <param name="tieLayouts">This staff's ties, laid out BEFORE the slurs (the order
    /// LayoutEngine.Prelim keeps): the slur is scored around the ties it covers, as
    /// LilyPond's Slur_engraver acknowledges them (BuildSlurExtraObjects).</param>
    internal ImmutableArray<SlurLayout> LayoutSlurs(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<SlurItem> slurs, Score score, ImmutableArray<SystemLayout> systems,
        int staffIndex = -1, Model.Staff? staff = null,
        ImmutableArray<GraceNoteItem> graceNotes = default,
        ImmutableArray<BeamLayout> beamLayouts = default,
        Func<ImmutableArray<InsideSlurScript>>? insideScripts = null,
        ImmutableArray<TieLayout> tieLayouts = default,
        int? graceStaffIndex = null)
    {
        // Which staff's grace groups are this staff's: the score's own index, which a caller
        // laying the staff out in a trivial one-staff frame (MultiStaffLayouter.StaffSlurLayouts,
        // staffIndex 0) states apart — reading staff 0's groups there made staff N's grace
        // slurs (and grace obstacles) out of another staff's grace runs.
        int graceStaff = graceStaffIndex ?? Math.Max(staffIndex, 0);

        // No slur and no grace run that could make one (WithGraceSlurs, below).
        if (slurs.Length == 0 && graceNotes.IsDefaultOrEmpty)
            return ImmutableArray<SlurLayout>.Empty;

        // Placed WITHOUT slurs, once for this staff — the boxes the scorer's
        // extra-encompass set needs (BuildSlurExtraObjects). Behind the slur count
        // above, and behind the factory's own "does this staff HAVE an 'inside mark"
        // gate, so the ordinary book pays nothing.
        var insideScriptLayouts = insideScripts?.Invoke()
            ?? ImmutableArray<InsideSlurScript>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var slurLayouts = new List<SlurLayout>();

        // Grace-obstacle pre-resolution, once per pass: this staff's groups
        // bucketed by measure, and a lazy geometry cache by group index — the
        // spring/beam-quant solve happens once per COVERED group, not once per
        // covering slur segment (see GraceObstacleGeom).
        Dictionary<int, List<int>>? graceByMeasure = null;
        GraceObstacleGeom?[]? graceGeomCache = null;
        if (!graceNotes.IsDefaultOrEmpty)
        {
            for (int gi = 0; gi < graceNotes.Length; gi++)
            {
                var g = graceNotes[gi];
                if (g.StaffIndex != graceStaff || g.Columns.IsDefaultOrEmpty)
                    continue;
                graceByMeasure ??= new Dictionary<int, List<int>>();
                if (!graceByMeasure.TryGetValue(g.MeasureIndex, out var list))
                    graceByMeasure[g.MeasureIndex] = list = new List<int>();
                list.Add(gi);
            }
            if (graceByMeasure != null)
                graceGeomCache = new GraceObstacleGeom?[graceNotes.Length];
        }

        // The GRACE slurs — each auto-slurred grace run's bow from its last column to its main
        // note — laid out here as ordinary slurs, FIRST, so an enclosing phrasing slur scores
        // them. Until session 724 the renderer drew them itself (SharedRenderer.DrawGraceSlur,
        // fixed clearances 0.5 / 0.65 / 0.15 and the two heads as the only obstacles), the
        // largest family of bows off their LilyPond twin (session 647's sweep).
        // LILYPOND-REF: ly/grace-init.ly startGraceSlur / stopGraceSlur — an ordinary Slur on
        // the last grace note and the main note; scm/music-functions.scm:652-656
        // score-grace-settings — (Voice Slur direction DOWN).
        slurs = WithGraceSlurs(slurs, score, graceNotes, graceStaff, staff);
        if (slurs.Length == 0)
            return ImmutableArray<SlurLayout>.Empty;

        // Beam lookup, once per pass: (measure, item) → its beam layout. The
        // per-column stem resolution used to scan every beam layout's member
        // list for every covered column of every slur — quadratic in bars on a
        // beamed-and-slurred book (perf-slurbeam300).
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember = null;
        if (!beamLayouts.IsDefaultOrEmpty)
        {
            // The members of every beam, which BOUNDS the table (TryAdd drops a repeat).
            // Measured before the size was handed over: 493 calls, asked == Count every one
            // of them — nothing repeated, so the bound was the count on this corpus.
            int memberCount = 0;
            foreach (var bl in beamLayouts)
                memberCount += bl.Group.Members.Length;
            // Lent, and given back at the method's one return (see t_beamByMember).
            beamByMember = RentBeamByMember();
            beamByMember.EnsureCapacity(memberCount);
            foreach (var bl in beamLayouts)
                foreach (var m in bl.Group.Members)
                    // Keyed by VOICE too: (measure, item) alone is ambiguous across the
                    // voices of a shared staff. Until session 651 it was not, and the first
                    // beam won — so voice one's unbeamed g''2 in test/dot-cross-voice-spacing
                    // read voice two's beamed e8 (also item 0) as its stem, its up stem ended
                    // on a down beam, the slur missed LP's stem attachment (slur-scoring.cc:
                    // 742-752) and started 0.365 left of LilyPond's.
                    beamByMember.TryAdd(
                        (bl.Group.VoiceIndex, m.ResolveMeasureIndex(bl.Group.MeasureIndex), m.ItemIndex), bl);
        }

        // Tuplet-NUMBER boxes, once per pass: LilyPond's slur engraver acknowledges
        // the TupletNumber (NOT the bracket) into encompass-objects, where it is an
        // 'inside extra-encompass object — additional_ys then raises the attachment
        // range over it ("a slur's shift region is automatically made higher",
        // slur-shift-region.ly). The geometry is rebuilt from the same producer the
        // renderer draws from (TupletBracketEngraver.Calculate), the slurgrace
        // precedent. ⚠️ Scale 1 and no per-voice force (the same simplifications
        // the other extras disclose); the bracket itself casts no box, as in LP.
        // ⚠️ No scripts are passed either: when a script pushes the bracket up
        // (avoid-scripts), the number box this rebuild hands the slur sits at
        // the script-less height — tuplet-number-slur-script.ly measures that
        // seam if it ever binds.
        // LILYPOND-REF: lily/slur-engraver.cc:80 acknowledge_extra_object —
        //   ADD_ACKNOWLEDGER_FOR (acknowledge_extra_object, tuplet_number).
        // LILYPOND-REF: scm/define-grobs.scm TupletNumber (avoid-slur . inside).
        ImmutableArray<TupletBracketLayout> tupletNumberLayouts = default;
        // Gated on some slur's span actually TOUCHING some tuplet: Calculate
        // walks every tuplet's columns (with per-column beam probes), and this
        // runs on every layout pass — a tuplet-free slur book, or a score whose
        // tuplets and slurs never meet, must not pay it on each preview edit.
        bool slurMeetsTuplet = false;
        if (!score.TupletBrackets.IsDefaultOrEmpty && systems.Length > 0
            && !score.Voices.IsDefaultOrEmpty)
        {
            foreach (var s in slurs)
            {
                foreach (var t in score.TupletBrackets)
                {
                    if (t.MeasureIndex >= s.StartMeasureIndex
                        && t.MeasureIndex <= s.EndMeasureIndex)
                    {
                        slurMeetsTuplet = true;
                        break;
                    }
                }
                if (slurMeetsTuplet)
                    break;
            }
        }
        if (slurMeetsTuplet)
        {
            int maxMi = 0;
            foreach (var sys in systems)
                foreach (var m in sys.Measures)
                    maxMi = Math.Max(maxMi, m.MeasureIndex);
            var mlArr = new MeasureLayout[maxMi + 1];
            foreach (var sys in systems)
                foreach (var m in sys.Measures)
                    if (m.MeasureIndex <= maxMi)
                        mlArr[m.MeasureIndex] = m;
            var beamGroups = beamLayouts.IsDefaultOrEmpty
                ? ImmutableArray<BeamGroup>.Empty
                : beamLayouts.Select(bl => bl.Group).ToImmutableArray();
            tupletNumberLayouts = TupletBracketEngraver.Calculate(
                fonts, score.TupletBrackets, mlArr.ToImmutableArray(),
                score.Voices[0].Measures, beamGroups, beamLayouts);
        }

        // The slur end attaches to the note-head EDGE, then lifts 0.5 staff-space
        // beyond it (the beamed stem-tip path below lifts the same 0.5 off the beam).
        // Every standard notehead shares the LILC Y half-extent, so the head edge is
        // the note centre ± that half-height; the enumeration in SlurScoringProblem now
        // starts AT this base with no further lift of its own.
        // LILYPOND-REF: lily/slur-scoring.cc:556-557 get_base_attachments —
        //   y = head->extent(Y)[dir]; y += dir * 0.5 * staff_space.
        double slurOffset = GlyphMetrics.StaffHeadFont.NoteheadBlack.Top + 0.5; // 0.545 + 0.5 = 1.045 ss

        // The note-collision shift of a voice's column — the table the beams stand their stems
        // on (ApplyVoiceCollisionShifts) and the renderer draws the heads by. A slur's bound is
        // that shifted NoteColumn in LilyPond, so its head centre and its stem move with it.
        // MEASURED (session 658, the reader's beam-slur.lys, `c8[( c)]` against `e4` in the
        // other voice of a condensedStaff): LilyPond shifts the up-stemmed c 0.443 right and
        // starts the slur on the shifted stem, 10.218; Lily# drew the c there too but started
        // the slur on the UNSHIFTED column's stem, 9.774.
        var voiceShifts = SpacingRules.VoiceCollisionShiftsOf(score.Voices);

        foreach (var slur in slurs)
        {
            // A slur whose other end is on a system this call was not handed (the per-system
            // staff skylines lay out ONE system) still has its piece here: SplitClipped below.
            measureMap.TryGetValue(slur.StartMeasureIndex, out var startInfo);
            measureMap.TryGetValue(slur.EndMeasureIndex, out var endInfo);

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            var segments = SpannerBreakSubstitution.SplitClipped(
                slur.StartMeasureIndex, slur.EndMeasureIndex, systems, measureToSystemIdx);

            if (segments.IsEmpty)
                continue;

            foreach (var segment in segments)
            {
                var segSystem = systems[segment.SystemIndex];

                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges for broken pieces.
                // A bound that is a GRACE column stands at its run's own X (the column's head
                // left edge, as AddGraceObstaclesForMeasure reads it), not on the main grid: the
                // grace slur's start (StartGraceGroup), and since session 725 either end of a
                // hand-written slur in grace time — `grace { d'16( e') }`, `grace { g16( } a8)`.
                var startGrace = slur.StartGraceGroup >= 0
                    ? (Group: slur.StartGraceGroup, Column: graceNotes[slur.StartGraceGroup].Columns.Length - 1)
                    : GraceColumnOf(graceNotes, graceByMeasure, slur.VoiceIndex, slur.StartMeasureIndex, slur.StartItemIndex);
                var endGrace = GraceColumnOf(graceNotes, graceByMeasure, slur.VoiceIndex, slur.EndMeasureIndex, slur.EndItemIndex);
                double segStartX;
                if (segment.IsFirst && startGrace is { } sg && graceGeomCache != null)
                    segStartX = GraceColumnX(score.Voices[slur.VoiceIndex], graceNotes, sg.Group, sg.Column,
                        graceGeomCache, startMeasure, slur.VoiceIndex, voiceShifts);
                else if (segment.IsFirst)
                {
                    segStartX = startMeasure.X
                        + GetItemXOffset(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, startMeasure)
                        // Follow the curve-side head's within-chord displacement (seconds).
                        + GetChordHeadXOffset(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, slur.StartStaffPosition)
                        + voiceShifts.ShiftOf(slur.StartMeasureIndex, slur.VoiceIndex + 1, slur.StartItemIndex);
                }
                else
                {
                    // The line-start column's staff extent, as a broken tie's piece reads it:
                    // LILYPOND-REF: lily/slur-scoring.cc:594-598 get_base_attachments —
                    // x = ext[-d] of the bound column (generic_bound_extent).
                    // A slur's bound also counts an EMPTY KeySignature's position, which a tie's
                    // staff_extent skips (MultiStaffLayouter.LineStartSlurRights).
                    segStartX = segSystem.Measures[0].X + segSystem.LineStartSlurRightOf(staffIndex);
                }

                double segEndX;
                if (segment.IsLast && endGrace is { } eg && graceGeomCache != null)
                    segEndX = GraceColumnX(score.Voices[slur.VoiceIndex], graceNotes, eg.Group, eg.Column,
                        graceGeomCache, endMeasure, slur.VoiceIndex, voiceShifts);
                else if (segment.IsLast)
                {
                    segEndX = endMeasure.X
                        + GetItemXOffset(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, endMeasure)
                        // Follow the curve-side head's within-chord displacement (seconds).
                        + GetChordHeadXOffset(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, slur.EndStaffPosition)
                        + voiceShifts.ShiftOf(slur.EndMeasureIndex, slur.VoiceIndex + 1, slur.EndItemIndex);
                }
                else
                {
                    // The line-end column's LEFT edge (the bar line's ink, or a courtesy clef in
                    // front of it), not the measure's end past the bar: slur-scoring.cc:594-598.
                    // Bohemian Rhapsody, multi-line-spanners…: 0.19 (a thin bar) past LilyPond's.
                    segEndX = BrokenPieceEndX(fonts, score.Voices[slur.VoiceIndex], segSystem);
                }

                // On a TAB staff the same scorer runs, but in the tab's own frame —
                // staff-space 1.5, its own line count, fret digits for heads and no
                // stems — and its answer is then translated back toward the numbers.
                // See BuildTabSlurLayout for LilyPond's two stages and the measurement.
                if (staff is { IsTab: true })
                {
                    // A bound on a GRACE column is the grace's own digit (session 730; until then
                    // such a slur was skipped here). MEASURED, audit/lp-geometry/probes/tab-grace-slur.ly.
                    var tabLayout = BuildTabSlurLayout(
                        fonts, score, slur, segment.IsFirst, segment.IsLast, segSystem,
                        staffIndex, staff, segStartX, segEndX, graceNotes,
                        graceByMeasure, graceGeomCache, slurLayouts, beamByMember,
                        segment.IsFirst ? startGrace : null, segment.IsLast ? endGrace : null);
                    if (tabLayout != null)
                        slurLayouts.Add(tabLayout with { RenderMeasureIndex = segment.StartMeasureIndex });
                    continue;
                }

                // The obstacle/extra-object builders below filter items to the
                // segment's column window — captured BEFORE the endpoints shift to
                // the head CENTRE, because the edge columns' own X (the head's left
                // edge) sits half a head LEFT of the shifted endpoint and must stay
                // in the window: the left bound's own dots are exactly what
                // "Slurs avoid dots" is about.
                double windowStartX = segStartX;
                double windowEndX = segEndX;

                // LILYPOND-REF: slur-scoring.cc:562 get_base_attachments — the base
                // attachment X is the NOTEHEAD CENTER; segStartX/segEndX are the head's
                // left-edge column X, so shift each real endpoint right by half a head.
                if (segment.IsFirst)
                    segStartX += EndpointHeadHalfWidth(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex);
                if (segment.IsLast)
                    segEndX += EndpointHeadHalfWidth(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex);

                // Y at a broken edge anchors at the NEAREST covered note in
                // this system, not at the slur's far endpoint — anchoring the
                // continuation at the global end note's pitch ran the curve
                // through the segment's own first/last heads when they sit
                // lower/higher. LilyPond re-scores each broken piece over its
                // real encompassed columns; this is the endpoint part of that.
                // LILYPOND-REF: lily/slur-scoring.cc — encompass_info over the
                // broken piece's own note columns.
                double startStaffPos = segment.IsFirst
                    ? slur.StartStaffPosition
                    : EdgeNoteStaffPosition(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: true)
                        ?? slur.EndStaffPosition;
                double endStaffPos = segment.IsLast
                    ? slur.EndStaffPosition
                    : EdgeNoteStaffPosition(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: false)
                        ?? slur.StartStaffPosition;

                // Within-system Y offset (device, down from the system top) of the staff
                // middle, NOT an absolute page Y. Every Y the scorer sees (segStartY/
                // segEndY via PositionToDevice, obstacles, beamed stem tips) is derived
                // from this, and LP slur-scoring reasons over note-position DIFFERENCES,
                // so feeding the relative middle shifts every scored output Y by exactly
                // system.Y — undone once in DrawSlurs (byte-identical to the former
                // absolute origin). Decouples the scored slur from SystemLayout.Y for the
                // Stage-4 W2 stacking-origin flip (step 2d).
                double staffMiddleDown = LayoutUtilities.StaffOffsetInSystemDown(segSystem, staffIndex)
                    + _options.StaffHeight / 2.0;

                // LILYPOND-REF: slur-scoring.cc:549-557 get_base_attachments — the endpoint
                // attaches to the STEM TIP (the beam it joins), 0.5 ss beyond it, when the
                // note's stem points the same way as the slur AND is beamed on the inner side;
                // otherwise to the notehead (slurOffset). This lifts the slur clear of the beam.
                var leftEdgeInfo = segment.IsFirst
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.VoiceIndex, slur.StartMeasureIndex, slur.StartItemIndex, leftEdge: true,
                        windowStartX, staffMiddleDown, beamByMember)
                    : default;
                var rightEdgeInfo = segment.IsLast
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.VoiceIndex, slur.EndMeasureIndex, slur.EndItemIndex, leftEdge: false,
                        windowEndX, staffMiddleDown, beamByMember)
                    : default;
                const double stemTipGap = 0.5; // staff-spaces beyond the beam (LP dir_*0.5*staff_space)

                // A REST bound is not a note-column bound to LP: the fallback
                // loop reads the FIRST/LAST encompassed column's Y extent — the
                // rest's own ink — plus dir·0.5. MEASURED (debug-slur-scoring,
                // audit\lpreg\slurrest-dbg): the all-rest 16th slur's WINNING
                // candidate is idx=0 TOTAL=0.00 sitting at 2.55 = the r16 ink
                // bottom 2.05 + 0.5 — the base itself, not a scored climb; the
                // half-rest row's 0.5 = its ink bottom 0 + 0.5, same rule.
                // LILYPOND-REF: slur-scoring.cc:587-619 get_base_attachments,
                //   the !note_column_ loop: y = robust_relative_extent(col,
                //   Y)[dir] + dir * 0.5 * staff_space.
                double RestBoundBaseY(RestItem r)
                {
                    int rv = GlyphMetrics.NoteValueOf(r.BaseDuration);
                    var box = GlyphMetrics.GetRestBBox(rv);
                    double originDown = staffMiddleDown - (rv == 1 ? 1.0 : 0.0);
                    return slur.CurveUp
                        ? originDown - box.Top - 0.5
                        : originDown - box.Bottom + 0.5;
                }

                // LILYPOND-REF: slur-scoring.cc:556-557 — head->extent (Y)[dir_] + dir_ * 0.5.
                double HeadLift(MusicItem? item) => item is NoteItem or ChordItem
                    ? BowHeadBox(item, GlyphMetrics.NoteValueOf(item)).Top + 0.5
                    : slurOffset;

                RestItem? startRest = segment.IsFirst
                    && ItemAt(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex)
                        is RestItem { IsSpacer: false } sr ? sr : null;
                RestItem? endRest = segment.IsLast
                    && ItemAt(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex)
                        is RestItem { IsSpacer: false } er ? er : null;

                double segStartY;
                if (startRest is { } sRest)
                    segStartY = RestBoundBaseY(sRest);
                else if (segment.IsFirst && leftEdgeInfo.StemUp == slur.CurveUp && leftEdgeInfo.BeamedInner
                    && TryGetBeamedStemTipDeviceY(beamByMember, slur.VoiceIndex, slur.StartMeasureIndex, slur.StartItemIndex,
                        // At the DRAWN STEM, as every reader of the beam face (not the head
                        // centre segStartX: a sloped beam is 0.65 x slope off there).
                        leftEdgeInfo.StemXLo + EngravingDefaults.StemThickness / 2.0,
                        staffMiddleDown, slur.CurveUp, out double startTip))
                    segStartY = startTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                // A grace bound whose stem points the slur's way (up, or a lower voice's down —
                // MusicItem.GraceStemDown), beamed toward the slur's other end, hangs from the
                // grace beam. MEASURED (probes/grace-inner-slur.ly VOICEONE): `grace { d''16(
                // e''16) }` under \voiceOne — 0.45 low on the heads until session 725.
                else if (segment.IsFirst && leftEdgeInfo.StemUp == slur.CurveUp && startGrace is { } sgb && graceGeomCache != null
                    && GraceBeamOuterFaceDeviceY(score.Voices[slur.VoiceIndex], graceNotes, sgb.Group, sgb.Column,
                        towardRight: true, graceGeomCache, startMeasure, slur.VoiceIndex, voiceShifts,
                        leftEdgeInfo.StemXLo + EngravingDefaults.StemThickness / 2.0, staffMiddleDown) is { } startFace)
                    segStartY = startFace + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                {
                    // The lift off the head is that head's own extent (a cue head is smaller).
                    double startLift = HeadLift(segment.IsFirst
                        ? ItemAt(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex)
                        : EdgeColumnItem(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: true));
                    segStartY = (staffMiddleDown - startStaffPos / 2.0)
                        + (slur.CurveUp ? -startLift : startLift);
                    // A broken edge reads its bound column's whole extent, stem included.
                    if (!segment.IsFirst
                        && BrokenEdgeStemBaseY(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: true,
                            staffMiddleDown, segStartY, beamByMember,
                            segment.IsLast ? (slur.EndMeasureIndex, slur.EndItemIndex) : null, voiceShifts) is { } stemBase)
                        segStartY = stemBase;
                }

                double segEndY;
                // A GRACE slur never hangs its main-note end from that note's beam: the beam
                // attachment wants the slur not to lie strictly inside the beam, unless both ends
                // share it — and a main note beamed on its left (its inner side) has the grace
                // column, the slur's other bound, inside that beam's span, on no beam of its own.
                // (An ordinary slur strictly inside a beam has both ends on it, so the clause
                // never binds there.) MEASURED (Lab sessions/p724/rg, real-gone.lys):
                // `d, grace { a,16( } b,8)` under one beam — LilyPond ends the bow at b's head.
                // LILYPOND-REF: lily/slur-scoring.cc:549-554 get_base_attachments —
                //   (!spanner_less (slur_, Stem::get_beam (stem)) || has_same_beam_).
                // The same holds for a hand-written `grace { g16( } a8)`: a slur from grace time
                // out to a main note; one whose two ends are both in grace time is not this case.
                bool endMayHangFromBeam = startGrace is null || endGrace is not null;
                if (endRest is { } eRest)
                    segEndY = RestBoundBaseY(eRest);
                else if (segment.IsLast && endMayHangFromBeam && rightEdgeInfo.StemUp == slur.CurveUp && rightEdgeInfo.BeamedInner
                    && TryGetBeamedStemTipDeviceY(beamByMember, slur.VoiceIndex, slur.EndMeasureIndex, slur.EndItemIndex,
                        rightEdgeInfo.StemXLo + EngravingDefaults.StemThickness / 2.0,
                        staffMiddleDown, slur.CurveUp, out double endTip))
                    segEndY = endTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else if (segment.IsLast && rightEdgeInfo.StemUp == slur.CurveUp && endGrace is { } egb && graceGeomCache != null
                    && GraceBeamOuterFaceDeviceY(score.Voices[slur.VoiceIndex], graceNotes, egb.Group, egb.Column,
                        towardRight: false, graceGeomCache, endMeasure, slur.VoiceIndex, voiceShifts,
                        rightEdgeInfo.StemXLo + EngravingDefaults.StemThickness / 2.0, staffMiddleDown) is { } endFace)
                    segEndY = endFace + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                {
                    double endLift = HeadLift(segment.IsLast
                        ? ItemAt(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex)
                        : EdgeColumnItem(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: false));
                    segEndY = (staffMiddleDown - endStaffPos / 2.0)
                        + (slur.CurveUp ? -endLift : endLift);
                    if (!segment.IsLast
                        && BrokenEdgeStemBaseY(score.Voices[slur.VoiceIndex], segSystem, slur, leftEdge: false,
                            staffMiddleDown, segEndY, beamByMember,
                            segment.IsFirst ? (slur.StartMeasureIndex, slur.StartItemIndex) : null, voiceShifts) is { } stemBase)
                        segEndY = stemBase;
                }
                // A piece whose only column is its other, real bound: the broken end takes that
                // bound's base y (slur-scoring.cc:613-614 `y = base_attachment[-d][Y_AXIS]`).
                var pieceVoice = score.Voices[slur.VoiceIndex];
                if (!segment.IsLast && segment.IsFirst && startRest is null
                    && EdgeColumn(pieceVoice, segSystem, slur, leftEdge: false) is { } rc
                    && rc.Measure == slur.StartMeasureIndex && rc.Item == slur.StartItemIndex)
                    segEndY = segStartY;
                else if (!segment.IsFirst && segment.IsLast && endRest is null
                    && EdgeColumn(pieceVoice, segSystem, slur, leftEdge: true) is { } lc
                    && lc.Measure == slur.EndMeasureIndex && lc.Item == slur.EndItemIndex)
                    segStartY = segEndY;

                // A slur written on ONE CHORD HEAD (<c e( g>4 <d f) a>) is bound to that head,
                // not to the column: its base is the head's INNER edge (the right edge of the
                // start head, the left edge of the end head), a quarter of the head's height
                // off its centre toward the slur, with no stem, no beam and no staff-line nudge
                // (SlurScoringProblem reads SlurEdgeInfo.OnHead for the rest).
                // LILYPOND-REF: lily/slur-scoring.cc:574-582 get_base_attachments — the
                //   `else if (head)` arm: y = head extent (Y).linear_combination (0.5 * dir_),
                //   x = head extent (X)[-d].
                if (segment.IsFirst && slur.StartOnHead
                    && ItemAt(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex) is ChordItem sHead)
                {
                    var box = BowHeadBox(sHead, GlyphMetrics.NoteValueOf(sHead));
                    double headLeft = segStartX - EndpointHeadHalfWidth(
                        score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex);
                    segStartX = headLeft + box.Right;
                    segStartY = HeadBoundBaseY(staffMiddleDown, slur.StartStaffPosition, box, slur.CurveUp);
                    leftEdgeInfo = new SlurEdgeInfo(false, leftEdgeInfo.StemUp, false, false, box.Width,
                        OnHead: true, HeadCenterOffset: (box.Left + box.Right) / 2.0 - box.Right);
                }
                if (segment.IsLast && slur.EndOnHead
                    && ItemAt(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex) is ChordItem eHead)
                {
                    var box = BowHeadBox(eHead, GlyphMetrics.NoteValueOf(eHead));
                    double headLeft = segEndX - EndpointHeadHalfWidth(
                        score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex);
                    segEndX = headLeft + box.Left;
                    segEndY = HeadBoundBaseY(staffMiddleDown, slur.EndStaffPosition, box, slur.CurveUp);
                    rightEdgeInfo = new SlurEdgeInfo(false, rightEdgeInfo.StemUp, false, false, box.Width,
                        OnHead: true, HeadCenterOffset: (box.Left + box.Right) / 2.0 - box.Left);
                }

                var obstacles = BuildSlurObstacles(
                    score.Voices[slur.VoiceIndex], segSystem, slur, staffMiddleDown,
                    windowStartX, windowEndX, beamByMember, graceNotes,
                    graceByMeasure, graceGeomCache, voiceShifts);

                var extraObjects = BuildSlurExtraObjects(
                    score.TextMetrics, score.Voices[slur.VoiceIndex], segSystem, slur, staffMiddleDown, windowStartX, windowEndX,
                    out var tieEnds,
                    tupletNumberLayouts, score.TupletBrackets, insideScriptLayouts,
                    tieLayouts, measureToSystemIdx, voiceShifts);

                // The slurs already laid out are NOT obstacles: a slur never avoids a slur in
                // LilyPond (SlurScoringProblem.ScoreExtraEncompass's ⚠️) — only a PhrasingSlur
                // does, and it is laid out after every slur (SlurDetector) so they are all here.
                var solved = SlurScoringProblem.SolveLent(
                    slur, segStartX, segStartY, segEndX, segEndY, staffMiddleDown,
                    obstacles: obstacles,
                    enclosedSlurs: slur.IsPhrasing
                        ? EnclosedSlurs(slur, segment.StartMeasureIndex, slurLayouts, measureToSystemIdx)
                        : null,
                    isBrokenLeft: !segment.IsFirst,
                    isBrokenRight: !segment.IsLast,
                    leftEdge: leftEdgeInfo,
                    rightEdge: rightEdgeInfo,
                    extraObjects: extraObjects,
                    tieEnds: tieEnds);
                slurLayouts.Add(solved with { StaffIndex = staffIndex, RenderMeasureIndex = segment.StartMeasureIndex });
            }
        }

        if (beamByMember != null)
            GiveBeamByMember(beamByMember);
        return slurLayouts.ToImmutableArray();
    }

    /// <summary>
    /// The slur pass's (voice, measure, item) → beam table, lent from one map the thread keeps
    /// between passes.
    /// </summary>
    /// <remarks>
    /// MEASURED (session 472's census at HEAD, Release, the reader's corpus, eight forward
    /// keystrokes a book): 0.27 builds a keystroke at 156.98 entries (max 768), 1,354 B a
    /// keystroke, and none reachable once the render that built it returned — the pass reads
    /// it through <see cref="TryGetBeamedStemTipDeviceY"/> and the obstacle builder, and both
    /// copy numbers out of the beam they find.
    /// <para>
    /// RENTING TAKES IT OUT OF THE DRAWER (session 421's idiom), THE CLEARING IS ON GIVE
    /// (session 456) — and here a dirty map is WORSE than stale: the table is filled with
    /// <c>TryAdd</c> (the first beam wins, see the fill), so a stale entry under a shared key
    /// would win over this pass's own beam. A throw between the rent
    /// and the give only costs the next pass a new map.
    /// </para>
    /// <para>
    /// WHAT IT RETAINS is one map a thread at that thread's most-beamed staff — emptied, so
    /// it pins no beam.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static Dictionary<(int Voice, int Measure, int Item), BeamLayout>? t_beamByMember;

    /// <summary>Takes the thread's beam table, or makes the thread's first.</summary>
    private static Dictionary<(int Voice, int Measure, int Item), BeamLayout> RentBeamByMember()
    {
        var map = t_beamByMember ?? new Dictionary<(int Voice, int Measure, int Item), BeamLayout>();
        t_beamByMember = null;
        return map;
    }

    /// <summary>Puts a finished pass's beam table back, emptied, with its capacity.</summary>
    private static void GiveBeamByMember(Dictionary<(int Voice, int Measure, int Item), BeamLayout> map)
    {
        map.Clear();
        t_beamByMember = map;
    }

    /// <summary>
    /// The slur pieces a phrasing slur's segment avoids: the slurs of ITS voice that START
    /// inside it, on the system this segment is drawn on. Null when there are none.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur.cc:364-387 Slur::auxiliary_acknowledge_extra_object — a slur is
    /// acknowledged when it is CREATED, and added to every phrasing slur still open
    /// (<c>slurs</c>) or ending at that moment (<c>end_slurs</c>): so its START decides, from
    /// the phrasing slur's own start to its end inclusive. The engraver lives in the Voice
    /// (lily/phrasing-slur-engraver.cc), so another voice's slurs are never seen.
    /// The system test is Lily#'s: a piece's curve is in its own system's X, and LilyPond's
    /// small slur is the piece on the same line (Slur::get_curve of the broken spanner).
    /// </remarks>
    private static List<SlurLayout>? EnclosedSlurs(
        SlurItem phrasing, int segmentStartMeasure, IReadOnlyList<SlurLayout> laidOut,
        IReadOnlyDictionary<int, int> measureToSystem)
    {
        if (!measureToSystem.TryGetValue(segmentStartMeasure, out int system))
            return null;
        return EnclosedSlurs(phrasing, laidOut,
            m => measureToSystem.TryGetValue(m, out int s) && s == system);
    }

    /// <summary>The same, with the system test given as a predicate on a piece's
    /// <see cref="BowLayout.RenderMeasureIndex"/> — the tab path's form, which holds its
    /// segment's <see cref="SystemLayout"/> rather than the measure-to-system map.</summary>
    private static List<SlurLayout>? EnclosedSlurs(
        SlurItem phrasing, IReadOnlyList<SlurLayout> laidOut, Func<int, bool> onThisSystem)
    {
        List<SlurLayout>? found = null;
        foreach (var sl in laidOut)
        {
            var s = sl.Slur;
            if (s.IsPhrasing || s.VoiceIndex != phrasing.VoiceIndex)
                continue;
            bool startsInside =
                (s.StartMeasureIndex > phrasing.StartMeasureIndex
                 || (s.StartMeasureIndex == phrasing.StartMeasureIndex && s.StartItemIndex >= phrasing.StartItemIndex))
                && (s.StartMeasureIndex < phrasing.EndMeasureIndex
                    || (s.StartMeasureIndex == phrasing.EndMeasureIndex && s.StartItemIndex <= phrasing.EndItemIndex));
            if (!startsInside)
                continue;
            if (!onThisSystem(sl.RenderMeasureIndex))
                continue;
            (found ??= new List<SlurLayout>()).Add(sl);
        }
        return found;
    }

    /// <summary>The voice item at (measure, index), or null if out of range.</summary>
    private static MusicItem? ItemAt(Voice voice, int measureIndex, int itemIndex)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length) return null;
        var items = voice.Measures[measureIndex].Items;
        return itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
    }

    /// <summary>
    /// Lays out a slur on a TAB staff the way LilyPond does — in two stages, neither of
    /// them invented here: ⑴ the ORDINARY slur scorer, run in the tab staff's own frame
    /// (staff-space 1.5, four or six lines, fret digits for note heads and no stems at
    /// all), then ⑵ the whole curve translated <c>staff-space × direction × 0.35</c> back
    /// toward the numbers — on a numbers-only tab; a full tab reverts ⑵.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:1248-1258 no-stem-extend, stem-shorten,
    ///   beamed-lengths — the TabStaff zeroes every entry of <c>Stem.details</c>, under the
    ///   comment "make the Stems as short as possible to minimize their influence on the
    ///   slur::calc-control-points routine", and then hides the stencil. So the scorer
    ///   runs, and it runs over bare heads.
    /// LILYPOND-REF: ly/engraver-init.ly:1275 → scm/tablature.scm:144-157 slur::move-closer-to-tab-note-heads
    ///   — ⑵ a <c>control-points</c> transformer that subtracts
    ///   <c>staff-space × direction × 0.35</c> from ALL FOUR points, i.e. a rigid
    ///   translation; the shape the scorer chose survives it.
    /// LILYPOND-REF: lily/slur.cc:47-70 Slur::calc_direction — UP as soon as ONE
    ///   encompassed non-rest column's stem points DOWN, else DOWN. On a tab that stem
    ///   direction is the STRING's, not the notated pitch's (TabStaffGeometry.TabStemUp).
    /// <para>
    /// MEASURED on 2.26.0, scratch/p318/tabslur-dump.ly (four-string bass tab, the pair
    /// <c>ts.ly</c>/<c>ts.lys</c> the report came in on). Score 0 is the default TabStaff,
    /// score 1 the same staff with <c>Slur.control-points</c> reverted to
    /// <c>ly:slur::calc-control-points</c>, so the two differ by the transformer alone:
    /// <code>
    ///   e4( g)        raw  (0.533956, 3.601926) (1.291445, 4.242020)
    ///                      (2.458072, 4.236815) (3.209821, 3.589989)
    ///                 final    same x, every y − 0.525000  = 1.5 × 1 × 0.35
    ///   c8( d e f g4) raw  (0.532636, 3.589989) (2.282832, 5.092561)
    ///                      (6.442477, 5.092561) (8.192674, 3.589989)
    /// </code>
    /// and the raw base attachment is the digit's ink top + 0.750000 =
    /// <c>dir × 0.5 × staff_space</c> (slur-scoring.cc:555-557), NOT a stem: the dumped
    /// tab Stem's Y extent is empty.
    /// </para>
    /// <para>
    /// ⚠️ WHAT THIS REPLACED WAS AN INVENTION, and the reason it had to go rather than be
    /// re-tuned: it built a symmetric cubic whose peak cleared the topmost digit by a
    /// hand-chosen <c>0.36 × FretFontSize + 0.1</c>, and bowed UP always. Multiplying an
    /// invented arch by LilyPond's 0.35 would only have brought it NEAR the right answer by
    /// coincidence — the second invention covering the first (docs/RULES.md §5.2.1).
    /// </para>
    /// <para>
    /// ⚠️ ALL OF THE ABOVE IS THE NUMBERS-ONLY TAB (LilyPond's default TabStaff). A FULL tab —
    /// a lone <c>tab</c>, U4, whose twin is <c>\tabFullNotation</c> — draws its stems and
    /// beams, and LilyPond scores them there as on a staff and does not translate the curve
    /// (ly/property-init.ly:845 reverts <c>Slur.control-points</c>). So on a full tab the
    /// edges and the encompass read the drawn stems, and ⑵ does not run. Until session 633
    /// every tab took the numbers-only path; a written side toward the stems (HANDOFF ⒳¹³ ⑹)
    /// is what made the difference visible — the direction rule alone keeps a bow away from
    /// its stems. MEASURED: audit/lp-geometry/probes/tab-slur-full.ly and tab-slur-stems.ly.
    /// </para>
    /// <para>
    /// ⚠️ Rests do not enter. On a TabStaff LilyPond sets <c>Rest.stencil = ##f</c>
    /// (engraver-init.ly:1266), so a rest column carries no ink for the curve to clear —
    /// unlike the notation path, where <see cref="BuildSlurObstacles"/> reads a rest's own
    /// box. Nor do dots, tuplet numbers, scripts or text: the same block hides all of them,
    /// which is why there is no extra-encompass set here.
    /// </para>
    /// </remarks>
    private SlurLayout? BuildTabSlurLayout(
        Rendering.ScoreTextMetrics fonts,
        Score score, SlurItem slur, bool isFirst, bool isLast, SystemLayout segSystem,
        int staffIndex, Model.Staff staff, double segStartX, double segEndX,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure,
        GraceObstacleGeom?[]? graceGeomCache,
        IReadOnlyList<SlurLayout> slurLayouts,
        Dictionary<(int Voice, int Measure, int Item), BeamLayout>? beamByMember,
        (int Group, int Column)? startGrace = null, (int Group, int Column)? endGrace = null)
    {
        const double eps = 0.001;
        var voice = score.Voices[slur.VoiceIndex];
        // A GRACE bound (session 730): the bound is the grace column's own digit — drawn centred
        // on the column's X at TabConstants.GraceFretScale, with no stem (SharedRenderer.TabGraceDigits).
        MusicItem? GraceBoundItem((int Group, int Column)? bound)
        {
            if (bound is not { } b || b.Group < 0 || b.Group >= graceNotes.Length)
                return null;
            var g = graceNotes[b.Group];
            return b.Column < g.ColumnItemIndices.Length
                ? ItemAt(voice, g.MeasureIndex, g.ColumnItemIndices[b.Column])
                : null;
        }
        var startGraceItem = GraceBoundItem(startGrace);
        var endGraceItem = GraceBoundItem(endGrace);
        // A FULL tab draws its stems and beams (LilyPond's \tabFullNotation reverts the
        // TabStaff's zero-length stems), so they enter the scorer as they do on a staff. A
        // numbers-only tab keeps LilyPond's default TabStaff: bare digits, then the 0.35.
        bool full = !staff.TabNumbersOnly;
        // Within-system staff-top offset (device, down from system top), NOT absolute —
        // so the tab slur's digit/string geometry is system-independent and DrawSlurs
        // (shared with the notation slur) can add the system-top Y-up back uniformly.
        // TabStaffGeometry is additive in staffY (StringY = StaffY + n·space), so this is
        // a pure origin shift that leaves the device string frame intact (island 2).
        double staffY = LayoutUtilities.StaffOffsetInSystemDown(segSystem, staffIndex);
        var geom = new TabStaffGeometry(fonts, staff.Tuning ?? TuningType.Guitar, staffY, staff.TabSourceClef, staff.Transposition);
        double space = geom.StringSpace;
        double staffMiddleDown = staffY + (geom.StringCount - 1) * space / 2.0;
        double halfDigit = TabConstants.FretDigitHeight(fonts, geom.StringCount) / 2.0;

        // The note columns this segment encompasses, in X order — LilyPond's
        // note_columns_. A grace column joins below, at its own (smaller) digit size.
        var columns = new List<(double X, MusicItem Item, int Measure, int Index)>();
        foreach (var ml in segSystem.Measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex || mi >= voice.Measures.Length)
                continue;
            var items = voice.Measures[mi].Items;
            int lo = mi == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = mi == slur.EndMeasureIndex ? slur.EndItemIndex : items.Length - 1;
            hi = Math.Min(hi, items.Length - 1);
            for (int i = lo; i <= hi; i++)
            {
                if (items[i].GraceTime || items[i] is not (NoteItem or ChordItem))
                    continue;
                double cx = ml.X + GetItemXOffset(voice, mi, i, ml);
                if (cx < segStartX - eps || cx > segEndX + eps)
                    continue;
                columns.Add((cx, items[i], mi, i));
            }
        }
        // The edge items: the grace's own column where a bound is one, else the outermost
        // covered main column.
        if (columns.Count == 0 && (startGraceItem is null || endGraceItem is null))
            return null;
        var leftItem = startGraceItem ?? columns[0].Item;
        var rightItem = endGraceItem ?? columns[^1].Item;

        // The DRAWN stem's direction: a beamed stem takes its whole beam's (a full tab
        // draws beams; ArticulationEngraver asks the same), a lone one its own digits'.
        // A numbers-only tab keeps the per-column rule it has always read.
        BeamLayout? BeamOf(int measure, int index) =>
            full && beamByMember is not null
            && beamByMember.TryGetValue((slur.VoiceIndex, measure, index), out var b) ? b : null;
        bool StemUpOf(MusicItem item, BeamLayout? beam) =>
            beam is not null ? geom.GroupStemUp(beam.Group.MemberItems()) : geom.TabStemUp(item);

        // A full tab's drawn stem as the scorer's edge record (no head width — the caller
        // adds it); default (no stem) on a numbers-only tab, a whole note, or a missing tip.
        SlurEdgeInfo TabStemOf((double X, MusicItem Item, int Measure, int Index) column, bool leftEdge)
        {
            if (!full || NoteColumnLayout.Of(column.Item) is not { HasStem: true })
                return default;
            var beam = BeamOf(column.Measure, column.Index);
            bool stemUp = StemUpOf(column.Item, beam);
            double stemX = Rendering.SharedRenderer.TabStemX(column.X);
            // ⚠️ The beam line is read in ITS OWN member frame (the column plus the stem
            // attachment, TabBeamOuterEdgeY's xs), where its ends are the drawn beam's ends —
            // at the drawn stem's X it answered 0.053 tab spaces inside LilyPond's stem end
            // on a sloped beam (MEASURED, audit/lp-geometry/probes/tab-slur-stems.ly).
            double beamX = column.X + LayoutUtilities.StemAttachX(
                stemUp, GlyphMetrics.NoteValueOf(column.Item), column.Item switch
                {
                    NoteItem n => n.Notehead,
                    ChordItem c => c.Notehead,
                    _ => NoteheadStyle.Default,
                });
            double tip = beam is not null
                ? ArticulationEngraver.TabBeamOuterEdgeY(beam, geom, beamX)
                : geom.UnbeamedStemTipY(column.Item, stemUp, geom.StemHeadString(column.Item, stemUp))
                    ?? double.NaN;
            if (double.IsNaN(tip))
                return default;
            // Inner-side beaming off the beam identity, as on a staff (BeamContinuesToward).
            bool beamedInner = beam is not null
                && BeamContinuesToward(voice, column.Measure, column.Index, rightward: leftEdge);
            double halfStem = EngravingDefaults.StemThickness / 2.0;
            double stemXHi = stemX + halfStem;
            double stemBeginY = geom.StringY(geom.StemHeadString(column.Item, !stemUp));
            // A lone stem's extent is united with its FLAG, as the staff path's edge is
            // (ResolveSlurEdge): the flag hangs on the stem's right and toward the head, so it
            // widens X and can push the head-side edge of the Y window. The tab's flag is the
            // staff's glyph at the stem's end (SharedRenderer.DrawUnbeamedTabStem).
            // LILYPOND-REF: lily/slur-scoring.cc:188-203 get_bound_info — stem_extent_ is
            //   stem->extent ∪ flag->extent on both axes.
            var flag = beam is null
                ? GlyphMetrics.GetFlagBBox(GlyphMetrics.NoteValueOf(column.Item), stemUp)
                : default;
            if (flag != default)
            {
                stemXHi = Math.Max(stemXHi, stemX + flag.Width);
                var (flagYMin, flagYMax) = ItemSkylineFactory.FlagInkBand(tip, stemUp, flag);
                double flagInnerY = stemUp ? flagYMax : flagYMin;
                stemBeginY = stemUp ? Math.Max(stemBeginY, flagInnerY) : Math.Min(stemBeginY, flagInnerY);
            }
            return new SlurEdgeInfo(
                HasStem: true, StemUp: stemUp, BeamedInner: beamedInner, Beamed: beam is not null,
                StemXLo: stemX - halfStem, StemXHi: stemXHi,
                StemTipY: tip,
                StemBeginY: stemBeginY);
        }

        // A written side wins over the rule, as on a staff (SlurDetector).
        // LILYPOND-REF: lily/slur-engraver.cc:190-191 set_grob_direction in Slur_engraver::create_slur.
        // LILYPOND-REF: lily/slur.cc:60-68 calc_direction — DOWN unless some column's stem
        //   points DOWN.
        // ⚠️ Until session 633 the written side was dropped here (HANDOFF ⒳¹³ ⑹): the stems
        // were not scored, so a side toward them drew through the beams (Lab
        // sessions/p484/tab.lys). On a full tab they are scored now; on a numbers-only tab
        // there are no stems to cross.
        // Inside a polyphonic span the voice props set the side as they set the stems
        // (NoteItem.VoiceStemUp): \voiceOne's slurs bow up, \voiceTwo's down.
        // LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set — Slur.direction.
        var startItem = ItemAt(voice, slur.StartMeasureIndex, slur.StartItemIndex);
        int written = slur.IsPhrasing ? startItem?.PhrasingSlurDirection ?? 0 : 0;
        if (written == 0 && startItem switch
            {
                NoteItem n => n.VoiceStemUp,
                ChordItem c => c.VoiceStemUp,
                _ => null,
            } is { } voiceUp)
            written = voiceUp ? 1 : -1;
        // A slur that STARTS in grace time is DOWN — the grace settings' Slur direction, in a
        // TabVoice as in a Voice (MEASURED, tab-grace-slur.ly: all three DOWN).
        // LILYPOND-REF: scm/music-functions.scm:652-656 score-grace-settings — (Voice Slur direction ,DOWN).
        if (written == 0 && startGraceItem is not null)
            written = -1;
        bool curveUp = written > 0;
        if (written == 0)
        {
            foreach (var (_, item, m, ix) in columns)
            {
                if (!StemUpOf(item, BeamOf(m, ix)))
                {
                    curveUp = true;
                    break;
                }
            }
        }
        int dir = curveUp ? 1 : -1;              // LilyPond's dir_, in the Y-UP frame
        double outward = curveUp ? -1.0 : 1.0;   // the same direction in DEVICE Y (down)

        // Base attachments. The head is the digit on the SLUR'S OWN SIDE of the column
        // (LilyPond's extremes_[d].slur_head_); a broken edge has no edge note of its own,
        // so it reads the nearest covered column instead and keeps the system-edge X.
        // LILYPOND-REF: lily/slur-scoring.cc:555-557 get_base_attachments (real edge) and
        //   :600-614 breakable_bound_extent (broken).
        var startCol = geom.EdgeDigitColumn(leftItem, top: curveUp);
        var endCol = geom.EdgeDigitColumn(rightItem, top: curveUp);
        // A grace digit is drawn at TabConstants.GraceFretScale, so its ink box — the head the
        // base attachment clears — is that much smaller.
        double startScale = startGraceItem is not null ? TabConstants.GraceFretScale : 1.0;
        double endScale = endGraceItem is not null ? TabConstants.GraceFretScale : 1.0;
        double startDigitY = geom.StringY(startCol.StringNum);
        double endDigitY = geom.StringY(endCol.StringNum);
        double startY = startDigitY + outward * (halfDigit * startScale + 0.5 * space);
        double endY = endDigitY + outward * (halfDigit * endScale + 0.5 * space);

        // A full tab's stems, in the frame the staff path hands the scorer (ResolveSlurEdge,
        // BuildSlurObstacles): the stem stands on the digits' X centre (SharedRenderer.TabStemX),
        // a beamed stem ends on its beam's outer face, a lone one where the renderer ends it.
        // A lone stem's flag joins its extent (TabStemOf), as on the staff.
        // A tab grace has no stem (SharedRenderer.TabGraceDigits draws the bare number).
        var leftStem = isFirst && startGraceItem is null ? TabStemOf(columns[0], leftEdge: true) : default;
        var rightStem = isLast && endGraceItem is null ? TabStemOf(columns[^1], leftEdge: false) : default;
        // LILYPOND-REF: lily/slur-scoring.cc:549-557 get_base_attachments — a stem pointing
        //   the slur's way and beamed on the inner side: its end, then dir·0.5·staff_space.
        if (leftStem.Beamed && leftStem.BeamedInner && leftStem.StemUp == curveUp)
            startY = leftStem.StemTipY + outward * 0.5 * space;
        if (rightStem.Beamed && rightStem.BeamedInner && rightStem.StemUp == curveUp)
            endY = rightStem.StemTipY + outward * 0.5 * space;

        // The fret digits sit a TabHeadCenterOffset right of their note columns
        // (see EngravingDefaults), plus the chord zigzag of the digit actually attached to.
        // A grace digit is drawn CENTRED on its column's X (the run's own placement), so it
        // takes no TabHeadCenterOffset.
        double startX = segStartX + (isFirst
            ? (startGraceItem is not null ? startCol.Dx * startScale : EngravingDefaults.TabHeadCenterOffset + startCol.Dx)
            : 0);
        double endX = segEndX + (isLast
            ? (endGraceItem is not null ? endCol.Dx * endScale : EngravingDefaults.TabHeadCenterOffset + endCol.Dx)
            : 0);
        if (endX - startX < 0.5)
            return null;

        // One obstacle per column: its digit stack's ink box, at the slur-side digit's X —
        // and on a full tab, a stem pointing the slur's way, at the stem's X.
        // LILYPOND-REF: lily/slur-scoring.cc:146-158 get_encompass_info — stem_ is the stem's
        //   extent on the slur side, plus half the beam's thickness when beamed; x_ the stem's.
        var obstacles = new List<SlurObstacle>(columns.Count);
        foreach (var column in columns)
        {
            var (cx, item, _, _) = column;
            var top = geom.EdgeDigitColumn(item, top: true);
            var bottom = geom.EdgeDigitColumn(item, top: false);
            double ox = cx + EngravingDefaults.TabHeadCenterOffset + (curveUp ? top.Dx : bottom.Dx);
            double stemY = double.NaN;
            var stem = TabStemOf(column, leftEdge: true);
            if (stem.HasStem && stem.StemUp == curveUp && !double.IsNaN(stem.StemTipY))
            {
                stemY = stem.StemTipY
                    + (stem.Beamed ? outward * 0.5 * EngravingDefaults.BeamThickness : 0.0);
                ox = (stem.StemXLo + stem.StemXHi) / 2.0;
            }
            obstacles.Add(new SlurObstacle(
                ox,
                geom.StringY(top.StringNum) - halfDigit,
                geom.StringY(bottom.StringNum) + halfDigit,
                stemY));
        }
        AddTabGraceObstacles(
            obstacles, voice, slur, geom, graceNotes, graceByMeasure, graceGeomCache,
            segSystem, segStartX, segEndX);
        obstacles.Sort((a, b) => a.X.CompareTo(b.X));

        // A numbers-only tab has no stem on either edge (LilyPond's are zero-length and
        // stencil-less), so the edge info carries only the head width the min-length
        // snap-back and the tilt shift read — LilyPond's slur_head_x_extent_. A full tab's
        // edge carries its stem as well, which the stem-attachment X rule reads.
        var leftEdge = leftStem with { HeadWidth = isFirst ? startCol.HalfWidth * 2 * startScale : 0.0 };
        var rightEdge = rightStem with { HeadWidth = isLast ? endCol.HalfWidth * 2 * endScale : 0.0 };

        var tabSlur = new SlurItem(
            slur.StartStaffPosition, slur.EndStaffPosition, curveUp,
            slur.StartMeasureIndex, slur.EndMeasureIndex,
            slur.StartItemIndex, slur.EndItemIndex, slur.VoiceIndex)
        {
            // Same written `(`…`)` as the notation bow above it: one part drawn on two
            // staves is two bows with ONE address, which is what a chord's heads already
            // are and what the webview's clusterInstances already expects.
            StartSourcePosition = slur.StartSourcePosition,
            EndSourcePosition = slur.EndSourcePosition,
            IsPhrasing = slur.IsPhrasing,
        };

        var solved = SlurScoringProblem.SolveLent(
            tabSlur, startX, startY, endX, endY, staffMiddleDown,
            obstacles: obstacles,
            // A phrasing slur clears the tab's slurs as it does the staff's. The pieces are
            // the TRANSFORMED ones (control points already moved toward the digits below),
            // which is what LilyPond reads: the grob's control-points, the tablature
            // transformer's output (scm/lily/tablature.scm).
            // LILYPOND-REF: lily/slur-scoring.cc:814-821 get_extra_encompass_infos —
            //   Slur::get_curve (small_slur) of each enclosed slur.
            enclosedSlurs: slur.IsPhrasing
                ? EnclosedSlurs(slur, slurLayouts,
                    m => segSystem.Measures.Any(ml => ml.MeasureIndex == m))
                : null,
            isBrokenLeft: !isFirst,
            isBrokenRight: !isLast,
            leftEdge: leftEdge,
            rightEdge: rightEdge,
            staffSpace: space,
            staffLineCount: geom.StringCount,
            // LILYPOND-REF: lily/slur-scoring.cc:334-341 musical_dy_ — the two edge HEADS'
            //   reference coordinates, which on a tab are their STRING lines (Y-up = −device).
            musicalDy: startDigitY - endDigitY);

        // ⑵ The transformer: every control point toward the numbers by
        // staff-space × direction × 0.35 (0.525 on a 1.5-space tab). BowLayout's Ys are
        // page Y-up, which is the frame LilyPond's control-points live in, so the
        // subtraction is spelled exactly as scm/lily/tablature.scm:155-156 spells it.
        // ⚠️ A NUMBERS-ONLY TAB ONLY. A full tab (a lone `tab`, or `as full`) is LilyPond's
        // \tabFullNotation, which puts the ordinary control points back, so its bow stays
        // where the scorer left it.
        // LILYPOND-REF: ly/property-init.ly:845 (tabFullNotation) reverts ly/engraver-init.ly:1275 slur::move-closer-to-tab-note-heads.
        // MEASURED on 2.26.0 (audit/lp-geometry/probes/tab-slur-full.ly, tab-slur.ly's book
        // under \tabFullNotation): y0 1.570223 against the plain tab's 1.220223, the 0.35
        // exactly, and the same rise and span. Until session 633 every full tab moved too.
        double closer = staff.TabNumbersOnly ? space * dir * 0.35 : 0.0;
        return new SlurLayout(tabSlur,
            solved.StartX, solved.StartYUp - closer,
            solved.EndX, solved.EndYUp - closer,
            (solved.Control1.X, solved.Control1.Y - closer),
            (solved.Control2.X, solved.Control2.Y - closer),
            isBrokenLeft: !isFirst, isBrokenRight: !isLast) { StaffIndex = staffIndex };
    }

    /// <summary>
    /// Adds the GRACE fret digits a tab slur covers to <paramref name="obstacles"/> — one
    /// per head of every covered grace column, at the grace digit size, with no stem
    /// (a tab grace is a bare number: GraceNoteEngraver skips the beam quant for one).
    /// </summary>
    /// <remarks>
    /// The X is the renderer's own: <c>groupX + ColumnOffsets[k]</c>, which
    /// <c>SharedRenderer.TabGraceDigits</c> draws centred on — so, unlike the main
    /// columns, there is no TabHeadCenterOffset here. The group origin is
    /// <see cref="AddGraceObstaclesForMeasure"/>'s (the main note's column less the run's
    /// span), which carries the same disclosed simplification: the script overhang
    /// GraceNoteEngraver subtracts is not read.
    /// </remarks>
    private static void AddTabGraceObstacles(
        List<SlurObstacle> obstacles, Voice voice, SlurItem slur, TabStaffGeometry geom,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure, GraceObstacleGeom?[]? graceGeomCache,
        SystemLayout segSystem, double segStartX, double segEndX)
    {
        if (graceByMeasure is null || graceGeomCache is null || graceNotes.IsDefaultOrEmpty)
            return;
        const double eps = 0.001;
        double halfGrace = TabConstants.FretDigitHeight(geom.Fonts, geom.StringCount) * TabConstants.GraceFretScale / 2.0;

        foreach (var ml in segSystem.Measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex || mi >= voice.Measures.Length)
                continue;
            if (!graceByMeasure.TryGetValue(mi, out var groupIndices))
                continue;

            foreach (int gi in groupIndices)
            {
                var g = graceNotes[gi];
                // Covered = the main note lies inside the span, excluding the start note
                // itself (its run sounds BEFORE the slur opens) — the same gate the
                // notation path takes.
                bool afterStart = mi > slur.StartMeasureIndex || g.MainNoteItemIndex > slur.StartItemIndex;
                bool beforeEnd = mi < slur.EndMeasureIndex || g.MainNoteItemIndex <= slur.EndItemIndex;
                if (!afterStart || !beforeEnd)
                    continue;

                // The one house for a run's geometry (it was spelt out again here until session
                // 729, which would have left this reader on the unstretched run).
                var cached = GraceGeomOf(voice, graceNotes, gi, graceGeomCache, ml);
                double groupX = ml.X + GetItemXOffset(voice, mi, g.MainNoteItemIndex, ml) - cached.Span;

                for (int k = 0; k < g.Columns.Length; k++)
                {
                    double hx = groupX + (k < cached.Offsets.Length ? cached.Offsets[k] : 0.0);
                    if (hx < segStartX - eps || hx > segEndX + eps)
                        continue;
                    // ONE DIGIT PER HEAD, as the renderer draws them: a tab grace chord
                    // prints a number on every string it sounds.
                    double top = double.PositiveInfinity, bottom = double.NegativeInfinity;
                    foreach (var head in g.Columns[k].Heads)
                    {
                        double y = geom.DigitY(head.Midi, head.StringNumber);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                    if (double.IsInfinity(top))
                        continue;
                    obstacles.Add(new SlurObstacle(hx, top - halfGrace, bottom + halfGrace));
                }
            }
        }
    }

    /// <summary>
    /// Detects glissandos and calculates their layouts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/scheme-engravers.scm
    /// </remarks>
    public ImmutableArray<GlissandoLayout> LayoutGlissandos(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var glissandos = _glissandoDetector.DetectGlissandos(score);

        if (glissandos.Length == 0)
            return ImmutableArray<GlissandoLayout>.Empty;

        // Each glissando resolves its endpoint X against its OWN voice's measures.
        // A single-voice score is one group over Voices[0] — byte-identical.
        var layouts = ImmutableArray.CreateBuilder<GlissandoLayout>();
        foreach (var group in glissandos.GroupBy(g => g.VoiceIndex))
            layouts.AddRange(GlissandoEngraver.Calculate(
                group.ToImmutableArray(), systems, staffIndex,
                score.Voices[group.Key].Measures));
        return layouts.ToImmutable();
    }
}
