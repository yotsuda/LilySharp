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
    private readonly TieEngraver _tieEngraver = new();
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
    /// LILYPOND-REF: lily/note-collision-interface.cc:381-407 — head wipe
    /// LILYPOND-REF: lily/note-collision-interface.cc:486-502 — force-hshift manual override
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

            // LILYPOND-REF: lily/note-collision-interface.cc:309-312
            // Width-based shift normalization: use the widest notehead width
            // in the column so shifts scale correctly for whole/breve noteheads.
            double noteheadWidth = GetColumnNoteheadWidth(column);

            // LILYPOND-REF: lily/note-collision-interface.cc:486-502
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

                // LILYPOND-REF: lily/note-collision-interface.cc:486-502
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
    /// LILYPOND-REF: lily/note-collision-interface.cc:309-312
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
    public ImmutableArray<BeamLayout> LayoutBeams(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var beamGroups = _beamDetector.DetectBeamGroups(score);

        if (beamGroups.Length == 0)
            return ImmutableArray<BeamLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var beamLayouts = new List<BeamLayout>();

        foreach (var group in beamGroups)
        {
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

            collisions.Add(new BeamCollision(
                X: itemX,
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
    /// Detects ties and calculates their layouts.
    /// </summary>
    public ImmutableArray<TieLayout> LayoutTies(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var ties = _tieDetector.DetectTies(score);

        if (ties.Length == 0)
            return ImmutableArray<TieLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var tieLayouts = new List<TieLayout>();

        foreach (var tie in ties)
        {
            if (!measureMap.TryGetValue(tie.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(tie.EndMeasureIndex, out var endInfo))
                continue;

            var (startSystem, startMeasure) = startInfo;
            var (endSystem, endMeasure) = endInfo;

            double startX = startMeasure.X;
            double endX = endMeasure.X;

            if (tie.StartItemIndex < startMeasure.Items.Length)
                startX += startMeasure.Items[tie.StartItemIndex].X;
            if (tie.EndItemIndex < endMeasure.Items.Length)
                endX += endMeasure.Items[tie.EndItemIndex].X;

            double staffY = LayoutUtilities.FindStaffYInSystem(startSystem, staffIndex);
            double staffMiddleY = staffY + _options.StaffHeight / 2;
            double y = staffMiddleY - tie.StaffPosition / 2;

            // Use TieFormattingProblem for optimal tie positioning
            // LILYPOND-REF: lily/tie-formatting-problem.cc
            int startDots = tie.StartNote.Dots;
            var problem = new TieFormattingProblem(
                tie, startX, y, endX, y,
                existingTies: tieLayouts,
                staffHeight: _options.StaffHeight,
                startDots: startDots);
            var tieLayout = problem.Solve();
            tieLayouts.Add(tieLayout);
        }

        return tieLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Detects slurs and calculates their layouts.
    /// </summary>
    public ImmutableArray<SlurLayout> LayoutSlurs(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var slurs = _slurDetector.DetectSlurs(score);

        if (slurs.Length == 0)
            return ImmutableArray<SlurLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var slurLayouts = new List<SlurLayout>();

        foreach (var slur in slurs)
        {
            if (!measureMap.TryGetValue(slur.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(slur.EndMeasureIndex, out var endInfo))
                continue;

            var (startSystem, startMeasure) = startInfo;
            var (endSystem, endMeasure) = endInfo;

            double startX = startMeasure.X;
            double endX = endMeasure.X;

            if (slur.StartItemIndex < startMeasure.Items.Length)
                startX += startMeasure.Items[slur.StartItemIndex].X;
            if (slur.EndItemIndex < endMeasure.Items.Length)
                endX += endMeasure.Items[slur.EndItemIndex].X;

            double staffY = LayoutUtilities.FindStaffYInSystem(startSystem, staffIndex);
            double staffMiddleY = staffY + _options.StaffHeight / 2;
            double startY = staffMiddleY - slur.StartStaffPosition / 2.0;
            double endY = staffMiddleY - slur.EndStaffPosition / 2.0;

            // Offset slur endpoints to the opposite side of the stem
            double slurOffset = 0.6;  // staff spaces
            if (slur.CurveUp)
            {
                startY -= slurOffset;
                endY -= slurOffset;
            }
            else
            {
                startY += slurOffset;
                endY += slurOffset;
            }

            var problem = new SlurScoringProblem(
                slur, startX, startY, endX, endY,
                existingSlurs: slurLayouts,
                staffHeight: _options.StaffHeight);
            var slurLayout = problem.Solve();
            slurLayouts.Add(slurLayout);
        }

        return slurLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Detects glissandos and calculates their layouts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/glissando-engraver.cc
    /// </remarks>
    public ImmutableArray<GlissandoLayout> LayoutGlissandos(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1)
    {
        var glissandos = _glissandoDetector.DetectGlissandos(score);

        if (glissandos.Length == 0)
            return ImmutableArray<GlissandoLayout>.Empty;

        return GlissandoEngraver.Calculate(glissandos, systems, _options.StaffHeight, staffIndex);
    }
}
