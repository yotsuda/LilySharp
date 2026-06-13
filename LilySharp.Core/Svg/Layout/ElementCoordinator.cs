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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Coordinates layout of beams, ties, slurs, and voice collisions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc, lily/tie.cc, lily/slur.cc
/// </remarks>
public sealed class ElementCoordinator
{
    private readonly LayoutOptions _options;
    private readonly BeamDetector _beamDetector = new();
    private readonly BeamEngraver _beamEngraver = new();
    private readonly TieDetector _tieDetector = new();
    // Tie layout is done by TieFormattingProblem (see LayoutTies); the old
    // (The reference-only TieEngraver twin was deleted; TieFormattingProblem
    // below is the live tie layout.)
    private readonly SlurDetector _slurDetector = new();
    private readonly GlissandoDetector _glissandoDetector = new();
    private readonly VoiceCollector _voiceCollector = new();
    private readonly NoteCollision _noteCollision = new();

    public ElementCoordinator(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Calculates X offsets and head wipe flags for notes that collide in multi-voice contexts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:381-407 — head wipe
    /// LILYPOND-REF: lily/note-collision.cc:486-502 — force-hshift manual override
    /// Returns both voice offsets and head wipe entries (noteheads to hide on merge).
    /// </remarks>
    public (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
            ImmutableHashSet<VoiceItemKey> HeadWipeEntries,
            ImmutableHashSet<VoiceItemKey> DotForceDownEntries) CalculateVoiceOffsets(
        Score score, GrobPropertyResolver? resolver = null)
    {
        if (score.Voices.Length <= 1)
            return (ImmutableDictionary<VoiceItemKey, double>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty);

        var voiceColumns = _voiceCollector.Collect(score);

        if (voiceColumns.Length == 0)
            return (ImmutableDictionary<VoiceItemKey, double>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty);

        var offsetBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotForceDownBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();

        foreach (var column in voiceColumns)
        {
            if (column.Entries.Length <= 1)
                continue;

            // LILYPOND-REF: lily/note-collision.cc:309-312
            // Width-based shift normalization: use the widest notehead width
            // in the column so shifts scale correctly for whole/breve noteheads.
            double noteheadWidth = GetColumnNoteheadWidth(column);

            // LILYPOND-REF: lily/note-collision.cc:486-502
            // Check for force-hshift manual override before auto-calculation.
            // When active, force-hshift replaces the auto-calculated offset.
            double? forceHshift = null;
            if (resolver != null)
            {
                // Advance resolver to the first entry's position in this column
                int minItemIndex = column.Entries.Min(e => e.ItemIndex);
                resolver.AdvanceTo(column.MeasureIndex, minItemIndex);
                forceHshift = resolver.GetDouble("NoteColumn", "force-hshift");
            }

            var offsets = _noteCollision.CalculateVoiceOffsets(column, noteheadWidth);

            foreach (var (voiceId, itemIndex, xOffset, headTransparent, dotForceDown) in offsets)
            {
                var key = new VoiceItemKey(column.MeasureIndex, voiceId, itemIndex);

                // LILYPOND-REF: lily/note-collision.cc:486-502
                // force-hshift overrides auto-calculated offsets for all columns at this position.
                double effectiveOffset = forceHshift.HasValue
                    ? forceHshift.Value * noteheadWidth
                    : xOffset;

                if (Math.Abs(effectiveOffset) > 0.001)
                {
                    offsetBuilder[key] = effectiveOffset;
                }

                if (headTransparent)
                {
                    headWipeBuilder.Add(key);
                }

                if (dotForceDown)
                {
                    dotForceDownBuilder.Add(key);
                }
            }
        }

        return (offsetBuilder.ToImmutable(), headWipeBuilder.ToImmutable(), dotForceDownBuilder.ToImmutable());
    }

    /// <summary>
    /// Determines the widest notehead width in a voice column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:309-312
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
        return maxWidth;
    }

    /// <summary>
    /// Detects beam groups (raw, without layout calculation).
    /// Used for tuplet bracket-visibility checks.
    /// </summary>
    public ImmutableArray<BeamGroup> DetectBeamGroups(Score score)
        => _beamDetector.DetectBeamGroups(score);

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
    public ImmutableArray<BeamLayout> LayoutBeams(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var beamGroups = _beamDetector.DetectBeamGroups(score);

        if (beamGroups.Length == 0)
            return ImmutableArray<BeamLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var beamLayouts = new List<BeamLayout>();

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
            var measure = score.Voice.Measures[group.MeasureIndex];

            var itemXPositions = new List<double>();
            if (!measureLayout.Columns.IsDefaultOrEmpty && measureLayout.Columns.Length > 0)
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

            var collisions = CollectBeamCollisions(
                score.Voice.Measures[group.MeasureIndex],
                group,
                itemXPositions);

            var beamLayout = _beamEngraver.CalculateBeamLayout(
                group,
                itemXPositions,
                collisions,
                staffIndex);

            beamLayouts.Add(beamLayout);
        }

        return beamLayouts.ToImmutableArray();
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
        Dictionary<int, (SystemLayout System, MeasureLayout Measure)> measureMap,
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
                group.GrowDirection);

            var pieceLayout = LayoutSingleSystemBeamPiece(score, subGroup, measureMap, staffIndex);
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
        Dictionary<int, (SystemLayout System, MeasureLayout Measure)> measureMap,
        int staffIndex)
    {
        // Resolve each member's X position via its OWN measure layout.
        var memberXs = new List<double>(group.Members.Length);
        var renumbered = new List<BeamMember>(group.Members.Length);
        for (int i = 0; i < group.Members.Length; i++)
        {
            var m = group.Members[i];
            int memberMeasure = m.ResolveMeasureIndex(group.MeasureIndex);
            if (!measureMap.TryGetValue(memberMeasure, out var info))
                return null;
            var (_, measureLayout) = info;
            if (m.ItemIndex >= measureLayout.Items.Length)
                return null;

            double x;
            var measure = score.Voice.Measures[memberMeasure];
            if (!measureLayout.Columns.IsDefaultOrEmpty && measureLayout.Columns.Length > 0)
            {
                Fraction t = Fraction.Zero;
                for (int k = 0; k < m.ItemIndex; k++)
                    t += GetItemDuration(measure.Items[k]);
                x = measureLayout.X + measureLayout.GetXForTiming(t);
            }
            else
            {
                x = measureLayout.X + measureLayout.Items[m.ItemIndex].X;
            }

            memberXs.Add(x);

            // Renumber member.ItemIndex to its index in the dense list so
            // BeamScoringProblem's itemXPositions[member.ItemIndex] resolves.
            renumbered.Add(new BeamMember(
                m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
                m.StaffPosition, itemIndex: i,
                memberStemUp: m.MemberStemUp,
                targetStaffIndex: m.TargetStaffIndex,
                measureIndex: m.MeasureIndex,
                headPositionMin: m.HeadPositionMin,
                headPositionMax: m.HeadPositionMax));
        }

        var renumberedGroup = new BeamGroup(
            renumbered.ToImmutableArray(),
            group.MeasureIndex,
            startIndex: 0,
            group.StemUp,
            group.GrowDirection);

        // Cross-measure collision detection is deferred — pass empty list for now.
        var beamLayout = _beamEngraver.CalculateBeamLayout(
            renumberedGroup,
            memberXs,
            collisions: null,
            staffIndex: staffIndex);

        return beamLayout;
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
    private List<BeamCollision> CollectBeamCollisions(
        Measure measure,
        BeamGroup group,
        IReadOnlyList<double> itemXPositions)
    {
        var collisions = new List<BeamCollision>();
        var beamMemberIndices = new HashSet<int>(group.Members.Select(m => m.ItemIndex));

        double beamLeftX = itemXPositions[group.Members[0].ItemIndex];
        double beamRightX = itemXPositions[group.Members[^1].ItemIndex];

        for (int i = 0; i < measure.Items.Length; i++)
        {
            if (beamMemberIndices.Contains(i))
                continue;

            var item = measure.Items[i];
            double itemX = itemXPositions[i];

            double xPadding = _options.CollisionXPadding;
            if (itemX < beamLeftX - xPadding || itemX > beamRightX + xPadding)
                continue;

            int staffPosition;
            double halfHeight;

            switch (item)
            {
                case RestItem:
                    staffPosition = (int)EngravingDefaults.RestCenterPosition;
                    halfHeight = EngravingDefaults.RestExtent;
                    break;
                case NoteItem note:
                    staffPosition = note.StaffPosition;
                    halfHeight = EngravingDefaults.NoteheadHalfHeight;
                    break;
                case ChordItem chord:
                    int minPos = chord.Notes.Min(n => n.StaffPosition);
                    int maxPos = chord.Notes.Max(n => n.StaffPosition);
                    staffPosition = (minPos + maxPos) / 2;
                    halfHeight = (maxPos - minPos) / 2.0 + EngravingDefaults.NoteheadHalfHeight;
                    break;
                default:
                    continue;
            }

            // BeamCollision.X is relative to the beam's left stem —
            // BeamScoringProblem range-checks it against [0, xSpan] and
            // evaluates the beam Y at that offset. Passing the absolute
            // item X silently discarded most collisions.
            collisions.Add(new BeamCollision(
                X: itemX - beamLeftX,
                MinY: staffPosition - halfHeight,
                MaxY: staffPosition + halfHeight,
                BasePenalty: 1.0));
        }

        return collisions;
    }

    /// <summary>
    /// Calculates Y shifts for rests to avoid beam collisions.
    /// </summary>
    public ImmutableDictionary<RestShiftKey, double> CalculateRestShifts(
        Score score,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beamLayouts)
    {
        if (beamLayouts.Length == 0)
            return ImmutableDictionary<RestShiftKey, double>.Empty;

        var shifts = new Dictionary<RestShiftKey, double>();
        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);

        var beamsByMeasure = beamLayouts
            .GroupBy(bl => bl.Group.MeasureIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in beamsByMeasure)
        {
            int measureIndex = kvp.Key;
            var measureBeams = kvp.Value;

            if (!measureMap.TryGetValue(measureIndex, out var measureLayout))
                continue;

            var measure = score.Voice.Measures[measureIndex];

            var itemXPositions = measureLayout.Items
                .Select(item => measureLayout.X + item.X)
                .ToList();

            for (int itemIdx = 0; itemIdx < measure.Items.Length; itemIdx++)
            {
                if (measure.Items[itemIdx] is not RestItem)
                    continue;

                double restX = itemXPositions[itemIdx];

                foreach (var beamLayout in measureBeams)
                {
                    double beamY;
                    if (restX < beamLayout.LeftX)
                        beamY = beamLayout.LeftY;
                    else if (restX > beamLayout.RightX)
                        beamY = beamLayout.RightY;
                    else
                        beamY = beamLayout.GetYAtX(restX);

                    int d = beamLayout.Group.StemUp ? -1 : 1;

                    double beamThickness = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamThickness);
                    double beamTranslation = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamTranslation);
                    int beamCount = beamLayout.Group.Members.Max(m => m.BeamCount);

                    double heightOfBeams = beamThickness / 2 + (beamCount - 1) * beamTranslation;
                    double beamEdgeY = beamY + d * heightOfBeams;

                    double restCenterY = EngravingDefaults.RestCenterPosition;
                    double restExtent = EngravingDefaults.RestExtent;
                    double restEdgeY = restCenterY - d * restExtent;

                    double minimumDistance = EngravingDefaults.RestBeamMinDistance;

                    double gap = d * (beamEdgeY - d * minimumDistance - restEdgeY);
                    double shift = d * Math.Min(gap, 0.0);

                    if (Math.Abs(shift) > EngravingDefaults.RestShiftThreshold)
                    {
                        shift = Math.Ceiling(Math.Abs(shift) * 2) / 2.0 * Math.Sign(shift);
                        var key = new RestShiftKey(measureIndex, itemIdx);
                        shifts[key] = shift;
                    }
                }
            }
        }

        return shifts.ToImmutableDictionary();
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
    /// Detects ties and calculates their layouts, splitting cross-system ties into broken pieces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc
    /// LILYPOND-REF: lily/spanner.cc:36-144 — Spanner::do_break_processing
    /// LILYPOND-REF: lily/break-substitution.cc:67-153 — substitute_grob &amp; do_break_substitution
    /// A tie that crosses one or more system breaks is split into per-system pieces.
    /// Each piece's bound on the broken side is reattached to the system edge.
    /// </remarks>
    public ImmutableArray<TieLayout> LayoutTies(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var ties = _tieDetector.DetectTies(score);

        if (ties.Length == 0)
            return ImmutableArray<TieLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var tieLayouts = new List<TieLayout>();

        foreach (var tie in ties)
        {
            if (!measureMap.TryGetValue(tie.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(tie.EndMeasureIndex, out var endInfo))
                continue;

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            var segments = SpannerBreakSubstitution.Split(
                tie.StartMeasureIndex, tie.EndMeasureIndex, systems, measureToSystemIdx);

            if (segments.IsEmpty)
                continue;

            int startDots = tie.StartNote.Dots;

            foreach (var segment in segments)
            {
                var segSystem = systems[segment.SystemIndex];

                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges for broken pieces.
                double segStartX;
                if (segment.IsFirst)
                {
                    segStartX = startMeasure.X
                        + GetItemXOffset(score.Voice, tie.StartMeasureIndex, tie.StartItemIndex, startMeasure);

                    // The tie attaches at the RIGHT edge of the left note's
                    // outline (head + augmentation dots) — the item X is the
                    // head's LEFT edge. TieFormattingProblem then insets by
                    // x-gap on top, matching attachment_x_.widen(-x_gap_).
                    // LILYPOND-REF: lily/tie-formatting-problem.cc:560-581 —
                    // attachments come from the chord outline at the tie's Y.
                    int noteValue = tie.StartNote.BaseDuration.Numerator != 1
                        ? 1
                        : tie.StartNote.BaseDuration.Denominator;
                    double outlineRight = GlyphMetrics.GetNoteheadAdvance(noteValue);
                    if (startDots > 0)
                    {
                        // Dot column geometry (matches SharedRenderer): the
                        // first dot starts one dot-width right of the head,
                        // each dot advances two dot-widths; the outline ends
                        // at the last dot's right edge = head + 2n·dotWidth.
                        // LILYPOND-REF: scm/define-grobs.scm DotColumn padding;
                        //   scm/output-lib.scm ly:dots::print.
                        double dotWidth = GlyphMetrics.AugmentationDot.Width;
                        outlineRight += 2 * startDots * dotWidth;
                    }
                    segStartX += outlineRight;
                }
                else
                {
                    segStartX = segSystem.Measures[0].X;
                }

                double segEndX;
                if (segment.IsLast)
                {
                    segEndX = endMeasure.X
                        + GetItemXOffset(score.Voice, tie.EndMeasureIndex, tie.EndItemIndex, endMeasure);
                }
                else
                {
                    var lastMeasure = segSystem.Measures[^1];
                    segEndX = lastMeasure.X + lastMeasure.Width;
                }

                // Tie Y position is uniform (same pitch on both ends).
                double staffY = LayoutUtilities.FindStaffYInSystem(segSystem, staffIndex);
                double staffMiddleY = staffY + _options.StaffHeight / 2;
                double y = staffMiddleY - tie.StaffPosition / 2;

                var problem = new TieFormattingProblem(
                    tie, segStartX, y, segEndX, y,
                    existingTies: tieLayouts,
                    staffHeight: _options.StaffHeight,
                    startDots: segment.IsFirst ? startDots : 0,
                    isBrokenLeft: !segment.IsFirst,
                    isBrokenRight: !segment.IsLast);
                tieLayouts.Add(problem.Solve());
            }
        }

        return tieLayouts.ToImmutableArray();
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
        Score score, SystemLayout segSystem, SlurItem slur, bool leftEdge)
    {
        var measures = leftEdge
            ? segSystem.Measures.AsEnumerable()
            : segSystem.Measures.Reverse();

        foreach (var ml in measures)
        {
            int mi = ml.MeasureIndex;
            if (mi < slur.StartMeasureIndex || mi > slur.EndMeasureIndex)
                continue;
            if (mi >= score.Voice.Measures.Length)
                continue;

            var items = score.Voice.Measures[mi].Items;
            int lo = mi == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = mi == slur.EndMeasureIndex ? slur.EndItemIndex : items.Length - 1;
            hi = Math.Min(hi, items.Length - 1);

            if (leftEdge)
            {
                for (int i = lo; i <= hi; i++)
                    if (ItemStaffPosition(items[i], slur.CurveUp) is { } p)
                        return p;
            }
            else
            {
                for (int i = hi; i >= lo; i--)
                    if (ItemStaffPosition(items[i], slur.CurveUp) is { } p)
                        return p;
            }
        }
        return null;
    }

    private static int? ItemStaffPosition(MusicItem item, bool curveUp) => item switch
    {
        NoteItem n => n.StaffPosition,
        ChordItem c when c.Notes.Length > 0 => curveUp
            ? c.Notes.Max(n => n.StaffPosition)
            : c.Notes.Min(n => n.StaffPosition),
        _ => null
    };

    public ImmutableArray<SlurLayout> LayoutSlurs(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var slurs = _slurDetector.DetectSlurs(score);

        if (slurs.Length == 0)
            return ImmutableArray<SlurLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var slurLayouts = new List<SlurLayout>();

        // Offset slur endpoints to the opposite side of the stem.
        const double slurOffset = 0.6; // staff spaces

        foreach (var slur in slurs)
        {
            if (!measureMap.TryGetValue(slur.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(slur.EndMeasureIndex, out var endInfo))
                continue;

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            var segments = SpannerBreakSubstitution.Split(
                slur.StartMeasureIndex, slur.EndMeasureIndex, systems, measureToSystemIdx);

            if (segments.IsEmpty)
                continue;

            foreach (var segment in segments)
            {
                var segSystem = systems[segment.SystemIndex];

                // LILYPOND-REF: lily/spanner.cc:124-137 — bounds reattached to system edges for broken pieces.
                double segStartX;
                if (segment.IsFirst)
                {
                    segStartX = startMeasure.X
                        + GetItemXOffset(score.Voice, slur.StartMeasureIndex, slur.StartItemIndex, startMeasure);
                }
                else
                {
                    segStartX = segSystem.Measures[0].X;
                }

                double segEndX;
                if (segment.IsLast)
                {
                    segEndX = endMeasure.X
                        + GetItemXOffset(score.Voice, slur.EndMeasureIndex, slur.EndItemIndex, endMeasure);
                }
                else
                {
                    var lastMeasure = segSystem.Measures[^1];
                    segEndX = lastMeasure.X + lastMeasure.Width;
                }

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
                    : EdgeNoteStaffPosition(score, segSystem, slur, leftEdge: true)
                        ?? slur.EndStaffPosition;
                double endStaffPos = segment.IsLast
                    ? slur.EndStaffPosition
                    : EdgeNoteStaffPosition(score, segSystem, slur, leftEdge: false)
                        ?? slur.StartStaffPosition;

                double staffY = LayoutUtilities.FindStaffYInSystem(segSystem, staffIndex);
                double staffMiddleY = staffY + _options.StaffHeight / 2;
                double segStartY = staffMiddleY - startStaffPos / 2.0;
                double segEndY = staffMiddleY - endStaffPos / 2.0;

                if (slur.CurveUp)
                {
                    segStartY -= slurOffset;
                    segEndY -= slurOffset;
                }
                else
                {
                    segStartY += slurOffset;
                    segEndY += slurOffset;
                }

                var problem = new SlurScoringProblem(
                    slur, segStartX, segStartY, segEndX, segEndY,
                    existingSlurs: slurLayouts,
                    staffHeight: _options.StaffHeight,
                    isBrokenLeft: !segment.IsFirst,
                    isBrokenRight: !segment.IsLast);
                slurLayouts.Add(problem.Solve());
            }
        }

        return slurLayouts.ToImmutableArray();
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

        return GlissandoEngraver.Calculate(glissandos, systems, _options.StaffHeight, staffIndex, score.Voice.Measures);
    }
}
