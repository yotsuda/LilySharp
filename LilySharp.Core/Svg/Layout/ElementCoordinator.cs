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
    /// Calculates X offsets for notes that collide in multi-voice contexts.
    /// </summary>
    public ImmutableDictionary<VoiceItemKey, double> CalculateVoiceOffsets(Score score)
    {
        if (score.Voices.Length <= 1)
            return ImmutableDictionary<VoiceItemKey, double>.Empty;

        var voiceColumns = _voiceCollector.Collect(score);

        if (voiceColumns.Length == 0)
            return ImmutableDictionary<VoiceItemKey, double>.Empty;

        double noteheadWidth = EngravingDefaults.NoteheadBlackWidth;

        var builder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();

        foreach (var column in voiceColumns)
        {
            if (column.Entries.Length <= 1)
                continue;

            var offsets = _noteCollision.CalculateVoiceOffsets(column, noteheadWidth);

            foreach (var (voiceId, itemIndex, xOffset) in offsets)
            {
                if (Math.Abs(xOffset) > 0.001)
                {
                    var key = new VoiceItemKey(column.MeasureIndex, voiceId, itemIndex);
                    builder[key] = xOffset;
                }
            }
        }

        return builder.ToImmutable();
    }

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
            var problem = new TieFormattingProblem(
                tie, startX, y, endX, y,
                existingTies: tieLayouts,
                staffHeight: _options.StaffHeight);
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
