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
    private static readonly bool ForceHshiftEnabled = false;

    public ElementCoordinator(LayoutOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Calculates X offsets and head wipe flags for notes that collide in multi-voice contexts.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/note-collision.cc:254-317 — head wipe
    /// LILYPOND-REF: lily/note-collision.cc:607-622 — force-hshift manual override
    /// Returns both voice offsets and head wipe entries (noteheads to hide on merge).
    /// </remarks>
    public (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
            ImmutableHashSet<VoiceItemKey> HeadWipeEntries,
            ImmutableDictionary<VoiceItemKey, DotAdjustment> DotAdjustments) CalculateVoiceOffsets(
        Score score, GrobPropertyResolver? resolver = null)
        => ComputeVoiceOffsets(score.Voices, resolver);

    /// <summary>
    /// The static core of <see cref="CalculateVoiceOffsets"/>, reachable from the SPACING
    /// side without a <see cref="Score"/> or a coordinator instance:
    /// <see cref="SpacingRules.ApplyCrossVoiceColumnSpacing"/> must price a column's ink at
    /// the X the renderer will draw it — collision shift included — and the only
    /// non-drifting way to know that shift is to ask the SAME computation the renderer's
    /// offsets come from. LILYPOND-REF: lily/note-collision.cc calc_positioning_done runs
    /// before spacing reads the columns' extents, so LilyPond's separation boxes carry the
    /// shifts by construction; Lily# applies them at render time, so the spacing side has
    /// to ask.
    /// </summary>
    internal static (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
            ImmutableHashSet<VoiceItemKey> HeadWipeEntries,
            ImmutableDictionary<VoiceItemKey, DotAdjustment> DotAdjustments) ComputeVoiceOffsets(
        ImmutableArray<Voice> voices, GrobPropertyResolver? resolver = null)
    {
        if (voices.Length <= 1)
            return (ImmutableDictionary<VoiceItemKey, double>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty,
                    ImmutableDictionary<VoiceItemKey, DotAdjustment>.Empty);

        var voiceColumns = new VoiceCollector().Collect(voices);
        var noteCollision = new NoteCollision();

        if (voiceColumns.Length == 0)
            return (ImmutableDictionary<VoiceItemKey, double>.Empty,
                    ImmutableHashSet<VoiceItemKey>.Empty,
                    ImmutableDictionary<VoiceItemKey, DotAdjustment>.Empty);

        var offsetBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotAdjustBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, DotAdjustment>();

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
                var key = new VoiceItemKey(column.MeasureIndex, voiceId, itemIndex);

                // LILYPOND-REF: lily/note-collision.cc:607-622
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

                if (dot != default)
                {
                    dotAdjustBuilder[key] = dot;
                }
            }
        }

        return (offsetBuilder.ToImmutable(), headWipeBuilder.ToImmutable(), dotAdjustBuilder.ToImmutable());
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
    public ImmutableArray<BeamLayout> LayoutBeams(
        Score score, ImmutableArray<SystemLayout> systems, int staffIndex)
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
            // Beams resolve against their OWN voice's measures (voice 2 has its own
            // item stream); single-voice scores keep VoiceIndex 0 = score.Voice.
            var measure = score.Voices[group.VoiceIndex].Measures[group.MeasureIndex];

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
                itemXPositions);

            // Also keep the beam clear of the OTHER voices' notes/rests (a
            // polyphonic staff's stem-up beam rides over a high note held below).
            double beamLeftX = itemXPositions[group.Members[0].ItemIndex];
            double beamRightX = itemXPositions[group.Members[^1].ItemIndex];
            collisions.AddRange(CollectCrossVoiceBeamCollisions(
                score, group, measureLayout, beamLeftX, beamRightX));

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
        Dictionary<int, (SystemLayout System, MeasureLayout Measure)> measureMap,
        int staffIndex, int systemIndex)
    {
        // Resolves an item's X via its OWN measure layout; null when the measure or the
        // item fell outside this system's map.
        double? ResolveX(int measureIdx, int itemIdx)
        {
            if (!measureMap.TryGetValue(measureIdx, out var info))
                return null;
            var (_, measureLayout) = info;
            if (itemIdx >= measureLayout.Items.Length)
                return null;

            var measure = score.Voices[group.VoiceIndex].Measures[measureIdx];
            if (!measureLayout.Columns.IsDefaultOrEmpty && measureLayout.Columns.Length > 0)
            {
                Fraction t = Fraction.Zero;
                for (int k = 0; k < itemIdx; k++)
                    t += GetItemDuration(measure.Items[k]);
                return measureLayout.X + measureLayout.GetXForTiming(t);
            }
            return measureLayout.X + measureLayout.Items[itemIdx].X;
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
                m.Item, m.BeamCount, m.BeamCountLeft, m.BeamCountRight,
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
    private List<BeamCollision> CollectBeamCollisions(
        Measure measure,
        BeamGroup group,
        IReadOnlyList<double> itemXPositions)
    {
        var collisions = new List<BeamCollision>();
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

            // A rest sitting BETWEEN the beam's outer members belongs to the beam
            // (rest -> stem -> beam in LilyPond) and is moved clear of it by
            // rest_collision_callback (see CalculateRestShifts) — the beam itself
            // is quanted as if the rest were not there. LilyPond's beam quanter
            // likewise ignores such rests (verified: `c'8[ r8 c'8]` and
            // `c'8[ c'8]` quant to identical positions). Treating the rest as a
            // collision object would instead lift the BEAM to clear it, which LP
            // never does. LILYPOND-REF: lily/beam.cc:1331 rest_collision_callback.
            if (item is RestItem && i > firstMemberIndex && i < lastMemberIndex)
                continue;
            double itemX = itemXPositions[i];

            AddItemCollisions(collisions, item, itemX,
                              beamEdgeLeftX, beamEdgeRightX, beamOriginX,
                              _beamEngraver.Parameters.StemCollisionFactor);
        }

        AddAccidentalCollisions(collisions, measure, itemXPositions,
                                beamEdgeLeftX, beamEdgeRightX, beamOriginX);
        return collisions;
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
        List<BeamCollision> collisions, MusicItem item, double itemX,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX,
        double stemCollisionFactor)
    {
        // :394-398 — the stem is taken from the covered grobs that SURVIVED the rejects,
        // because the collect loop `continue`s past this line. A head that misses the
        // beam's x span brings no stem with it.
        bool anyBooked = false;
        switch (item)
        {
            case RestItem rest:
            {
                var box = GlyphMetrics.GetRestBBox(
                    LayoutUtilities.GetNoteValueFromFraction(rest.BaseDuration));
                // ⚠️ The rest is taken at its DEFAULT position. A rest that another voice
                // (or CalculateRestShifts) has moved covers a different band, and this does
                // not know it — the same staleness the rest's own shift already carries.
                double centreSs = EngravingDefaults.RestCenterPosition * 0.5;
                anyBooked = AddBoxCollision(
                    collisions, itemX + box.Left, itemX + box.Right,
                    centreSs + box.Bottom, centreSs + box.Top,
                    beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                break;
            }
            case NoteItem note:
                anyBooked = AddHeadCollision(
                    collisions, itemX, note.StaffPosition,
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
                        collisions, itemX + offsets[n], chord.Notes[n].StaffPosition,
                        noteValue, beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                }
                break;
            }
        }

        if (anyBooked)
            AddStemCollision(collisions, item, itemX, beamOriginX, stemCollisionFactor);
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
        List<BeamCollision> collisions, MusicItem item, double itemX, double beamOriginX,
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
        collisions.Add(new BeamCollision(
            LayoutUtilities.StemX(itemX, up, noteValue,
                LayoutUtilities.NoteheadStyleOf(item)) - beamOriginX,
            up ? chordStartY : double.NegativeInfinity,
            up ? double.PositiveInfinity : chordStartY,
            beamed ? stemCollisionFactor : 1.0));
    }

    /// <summary>One note head's box as a covered grob; false when the rejects dropped it.</summary>
    private static bool AddHeadCollision(
        List<BeamCollision> collisions, double headX, int staffPosition, int noteValue,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        var box = GlyphMetrics.GetNoteheadBBox(noteValue);
        double centreSs = staffPosition * 0.5;
        return AddBoxCollision(collisions, headX + box.Left, headX + box.Right,
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
        List<BeamCollision> collisions,
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
        collisions.Add(new BeamCollision(inkLeft - beamOriginX, minY, maxY, widthFactor));
        collisions.Add(new BeamCollision(inkRight - beamOriginX, minY, maxY, widthFactor));
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
            GlyphMetrics.NoteValueOf(group.Members[memberIndex].Item),
            LayoutUtilities.NoteheadStyleOf(group.Members[memberIndex].Item));
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
        List<BeamCollision> collisions, Measure measure,
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
                            collisions, singleLayout, itemX, note.IsCue ? CueAccidentalScale : 1.0,
                            beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                    break;

                case ChordItem chord:
                    // The stagger the renderer uses: reversed heads move their accidentals,
                    // so the column must be solved, not assumed.
                    // LILYPOND-REF: lily/accidental-placement.cc position_apes.
                    foreach (var al in ChordAccidentalLayouts(chord))
                        AddAccidentalCollision(collisions, al, itemX, 1.0,
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
    private static IEnumerable<AccidentalLayout> ChordAccidentalLayouts(ChordItem chord)
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
            LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration));
        foreach (var al in BeamAccidentalColumn.CalculatePositions(chord.Notes, offsets))
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
    /// ⚠️ The box is the grob's EXTENT (the LILC box, <see cref="GlyphMetrics.GetAccidentalBBox"/>),
    /// NOT the outline box a skyline is built from — LilyPond reads <c>extent</c> here. The two
    /// differ, and picking by habit is the defect <see cref="GlyphMetrics"/> warns about.
    /// </para>
    /// </remarks>
    private static void AddAccidentalCollision(
        List<BeamCollision> collisions, AccidentalLayout layout,
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
        AddBoxCollision(collisions, inkLeft, inkLeft + width,
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
    private List<BeamCollision> CollectCrossVoiceBeamCollisions(
        Score score, BeamGroup group, MeasureLayout measureLayout,
        double beamLeftX, double beamRightX)
    {
        var collisions = new List<BeamCollision>();
        if (score.Voices.Length <= 1
            || measureLayout.Columns.IsDefaultOrEmpty || measureLayout.Columns.Length == 0)
            return collisions;

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
                AddItemCollisions(collisions, item, itemX,
                                  beamEdgeLeftX, beamEdgeRightX, beamOriginX,
                                  _beamEngraver.Parameters.StemCollisionFactor);
            }
        }

        return collisions;
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
    /// </remarks>
    public ImmutableDictionary<RestShiftKey, double> CalculateRestShifts(
        Score score,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beamLayouts)
    {
        if (beamLayouts.Length == 0)
            return ImmutableDictionary<RestShiftKey, double>.Empty;

        var shifts = new Dictionary<RestShiftKey, double>();
        var measureMap = LayoutUtilities.BuildMeasureLayoutMap(systems);

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
            double beamThickness = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamThickness);
            double beamTranslation = EngravingDefaults.ToStaffPositions(
                EngravingDefaults.BeamTranslationOf(
                    EngravingDefaults.BeamThickness, 1.0,
                    group.Members.Max(m => m.BeamCount)));

            bool haveRestX = beamLayout.RestXPositions.Length == group.RestStems.Length;

            for (int r = 0; r < group.RestStems.Length; r++)
            {
                var rest = group.RestStems[r];
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
                    if (!measureMap.TryGetValue(measureIndex, out var measureLayout)
                        || rest.ItemIndex >= measureLayout.Items.Length)
                        continue;
                    restX = measureLayout.X + measureLayout.Items[rest.ItemIndex].X;
                }

                // LILYPOND-REF: beam.cc:1382-1386 beam_count is the rest stem's own
                // clamped multiplicity; beam_y = stem_y - d*height is the beam stack's
                // face toward the rest (the beams that cross it are the outermost ones).
                int restBeamCount = Math.Max(rest.CountLeft, rest.CountRight);
                double heightOfBeams = beamThickness / 2 + (restBeamCount - 1) * beamTranslation;
                double stemY = beamLayout.GetYAtX(restX);
                double beamY = stemY - d * heightOfBeams;

                // LILYPOND-REF: beam.cc:1389-1392 rest_dim = rest_extent[d] — the rest's
                // REAL glyph extent at its default origin (a semibreve hangs from the
                // line above the middle, rest.cc:101-121; every shorter rest sits at 0).
                var restBox = GlyphMetrics.GetRestBBox(rest.NoteValue);
                double restOrigin = rest.NoteValue == 1 ? 2.0 : 0.0;
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

                // Voice 0: CalculateRestShifts is handed the staff's PRIMARY-voice score, so
                // the rests it can see are that voice's. A beamed rest in voice two gets no
                // beam shift, which is the granularity this pass has always had — named here
                // rather than implied now that the key can tell voices apart.
                var key = new RestShiftKey(measureIndex, 0, rest.ItemIndex);
                if (!shifts.TryGetValue(key, out var existing)
                    || Math.Abs(shift) > Math.Abs(existing))
                    shifts[key] = shift;
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
    /// ⚠️ IT IS THE COLLISION, NOT THE VOICE, and this was measured before it was written:
    /// on 2.26.0 a staff's VerticalAxisGroup reaches -3.55 for one voice AND for a
    /// <c>\voiceTwo</c> whose partner holds spacer rests, and only -4.25 once that partner
    /// has NOTES. So a rest alone in a voice must not move, and this pass returns nothing
    /// for it.
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
        if (staff.Voices.Length < 2)
            return ImmutableDictionary<RestShiftKey, double>.Empty;

        var shifts = new Dictionary<RestShiftKey, double>();
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
                // The rest's direction is its voice's: LilyPond reads the Rest's own
                // direction and falls back to the note column's (:224-226), and in an
                // ordinary polyphonic texture both are the voice's forced stem direction.
                bool voiceUp = VoiceDefaults.GetDefaultStemUpAt(staff.Voices, v, m) ?? (v % 2 == 0);
                int dir = voiceUp ? 1 : -1;

                foreach (var (time, item, itemIndex) in byVoice[v])
                {
                    if (item is not RestItem rest || rest.IsSpacer || rest.IsMultiMeasure)
                        continue;

                    // The rest STARTS at its voiced position (dir × 4, line-aligned per
                    // duration) — rest.cc's staff_position_internal — and everything
                    // below translates from there. The renderer's default is the
                    // NEUTRAL letter (middle; whole hangs at +2), so the emitted shift
                    // carries the base displacement too.
                    int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    double basePos = VoicedRestPosition(dir, restValue);
                    double defaultPos = restValue == 1 ? 2.0 : 0.0;

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
                        bool otherUp = VoiceDefaults.GetDefaultStemUpAt(staff.Voices, o, m) ?? (o % 2 == 0);
                        foreach (var (otherTime, otherItem, _) in byVoice[o])
                        {
                            if (otherItem is not (NoteItem or ChordItem))
                                continue;
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

    /// <summary>RestCollision's <c>minimum-distance</c>, in staff spaces.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2981-2984 RestCollision
    /// <c>minimum-distance</c> = 0.75, read by rest-collision.cc:239-241.</remarks>
    private const double RestCollisionMinimumDistance = 0.75;

    /// <summary>
    /// The staff position a voiced rest STARTS at, for the standard five-line staff:
    /// <c>dir × voiced-position</c> (4); a quarter or shorter takes it as-is; a half
    /// aligns down to the nearest line at or below; a whole first drops one line in a
    /// lower voice, then hangs from the next line above (the top line when there is
    /// none). The proper-side check against the neutral letter is the tail of the
    /// same function.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/rest.cc:46-141 staff_position_internal (the
    /// unpitched arm); LILYPOND-REF: scm/define-grobs.scm Rest — voiced-position 4.</remarks>
    private static double VoicedRestPosition(int dir, int restValue)
    {
        const double VoicedPosition = 4.0;
        double pos = dir * VoicedPosition;
        if (restValue >= 4)   // duration_log > 1: no line alignment
            return pos;

        double[] lines = { -4.0, -2.0, 0.0, 2.0, 4.0 };
        if (restValue == 1)
        {
            // Whole: "lower voice semibreve rests generally hang a line lower",
            // then from the next available line.
            if (dir < 0)
                pos -= 2;
            double hang = lines[^1];
            foreach (var l in lines)
                if (l > pos) { hang = l; break; }
            pos = hang;
        }
        else
        {
            // Half (and breve): the line at or below, clamped to the bottom line.
            double aligned = lines[0];
            foreach (var l in lines)
                if (l <= pos) aligned = l;
            pos = aligned;
        }

        // Keep the voiced position only on the proper side of the neutral one
        // (+2 for a hanging whole, 0 otherwise on this staff).
        double neutral = restValue == 1 ? 2.0 : 0.0;
        return dir * (pos - neutral) > 0 ? pos : neutral + dir * VoicedPosition;
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
        double deviceStaffTop = mid - 2.0;            // staff top = +4 positions
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
    /// the renderer applies (<see cref="ChordHeadPositioning.CalculateOffsets"/>) so a
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
        double scale = chord.IsCue ? EngravingDefaults.CueScale : 1.0;
        var offsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, chord.StemUp, noteValue, scale);
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
    private static TieColumnParts? BuildTieColumn(
        Voice voice, int measureIndex, int itemIndex, double columnX,
        IReadOnlyList<int> tiedPositions, bool isLeftBound)
    {
        var item = ItemAt(voice, measureIndex, itemIndex);
        if (item is not (NoteItem or ChordItem))
            return null;

        int noteValue = GlyphMetrics.NoteValueOf(
            item is ChordItem c0 ? c0.BaseDuration : ((NoteItem)item).BaseDuration);
        // The head's INK EXTENT, not its advance. LilyPond's outline boxes each head with
        // head->extent (x_refpoint_, X_AXIS) (:119), a stencil extent; the two differ by
        // 0.000200 on a black head and 0.001400 on a half one. It is invisible on a tie whose
        // BOTH ends recede to a head centre — the whole span shifts and the width does not —
        // and it is 0.000700 on one whose other end is held by the stem.
        // LILYPOND-REF: lily/tie-formatting-problem.cc:119 set_column_chord_outline.
        var headBBox = GlyphMetrics.GetNoteheadBBox(noteValue);
        double headLeftInk = headBBox.Left;
        double headRightInk = headBBox.Right;
        bool stemUp = item is ChordItem c1 ? c1.StemUp : ((NoteItem)item).StemUp;

        // Every head of the column, with the seconds displacement the renderer draws it at.
        var positions = new List<int>();
        var offsets = new List<double>();
        if (item is ChordItem chord)
        {
            double scale = chord.IsCue ? EngravingDefaults.CueScale : 1.0;
            var chordOffsets = ChordHeadPositioning.CalculateOffsets(
                chord.Notes, chord.StemUp, noteValue, scale);
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
        var tied = new List<TieOutlineHead>();
        var others = new List<TieOutlineBox>();
        for (int i = 0; i < positions.Count; i++)
        {
            double left = columnX + offsets[i] + headLeftInk;
            double right = columnX + offsets[i] + headRightInk;
            if (tiedPositions.Contains(positions[i]))
                tied.Add(new TieOutlineHead(positions[i], left, right));
            else
                // An UNTIED chord member enters with its own ink height, not the tied heads'
                // one-staff-space box (:221, Staff_symbol_referencer::extent_in_staff).
                others.Add(new TieOutlineBox(
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
        var dots = new List<TieOutlineBox>();
        int dotCount = SpacingRules.GetDots(item);
        if (isLeftBound && dotCount > 0)
        {
            double maxRight = columnX + offsets.Max() + headRightInk;
            var dotBBox = GlyphMetrics.AugmentationDot;
            double dotRadius = dotBBox.Height / 2;
            foreach (int p in positions)
            {
                // A dot on a staff line is pushed up half a space (dots-engraver.cc:62-80).
                double dotY = p * 0.5 + (p % 2 == 0 ? 0.5 : 0);
                for (int d = 0; d < dotCount; d++)
                {
                    double dotX = maxRight + EngravingDefaults.DotGap
                                  + d * (dotBBox.Width + EngravingDefaults.DotGap);
                    dots.Add(new TieOutlineBox(
                        dotY - dotRadius, dotY + dotRadius, dotX, dotX + dotBBox.Width));
                }
            }
        }

        // The flag, on the LEFT bound of an unbeamed short note. Its ink hangs off the stem
        // end, so the glyph's own box is already in the stem's frame (:186-188).
        var flag = new List<TieOutlineBox>();
        if (isLeftBound && stemInfo is not null && item is NoteItem fn && noteValue >= 8 && !fn.IsBeamed)
        {
            var flagBBox = GlyphMetrics.GetFlagBBox(noteValue, stemUp);
            if (flagBBox != default)
            {
                double tipY = (stemUp ? stemInfo.Value.StemMax : stemInfo.Value.StemMin) * 0.5;
                double flagX = LayoutUtilities.StemX(supportLeft, stemUp, noteValue,
                    LayoutUtilities.NoteheadStyleOf(item));
                flag.Add(new TieOutlineBox(
                    tipY + flagBBox.Bottom, tipY + flagBBox.Top, flagX, flagX + flagBBox.Width));
            }
        }

        // The accidentals, on the RIGHT bound only — they stand between the arriving tie and
        // the head it is arriving at, and no other bound can meet them (:231-236).
        var accidentals = new List<TieOutlineBox>();
        if (!isLeftBound)
        {
            var placement = new AccidentalPlacement();
            IEnumerable<AccidentalLayout> laid = item switch
            {
                ChordItem ch when ch.HasPackedAccidentals
                    => ChordAccidentalLayouts(ch),
                ChordItem ch => placement.CalculatePositions(ch.Notes, offsets.ToArray()),
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
                accidentals.Add(new TieOutlineBox(
                    accY + accBBox.Bottom, accY + accBBox.Top, accX, accX + accBBox.Width));
            }
        }

        return new TieColumnParts
        {
            TiedHeads = tied,
            OtherHeads = others,
            Stem = stem,
            Dots = dots,
            Flag = flag,
            Accidentals = accidentals,
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
    public ImmutableArray<TieLayout> LayoutTies(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1, Model.Staff? staff = null)
        => LayoutTies(_tieDetector.DetectTies(score), score, systems, staffIndex, staff);

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
        ImmutableArray<TieItem> ties, Score score, ImmutableArray<SystemLayout> systems,
        int staffIndex = -1, Model.Staff? staff = null)
    {
        if (ties.Length == 0)
            return ImmutableArray<TieLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var tieLayouts = new List<TieLayout>();

        // A tie COLUMN is the ties of ONE chord: same voice, same start measure and item.
        // LilyPond builds one Tie_formatting_problem per Tie_column and feeds it that column's
        // ties (lily/tie-column.cc:81-93 Tie_column::calc_positioning_done -> problem.from_ties
        // (ties)), which is what lets a tie's answer depend on where its neighbours went -- and
        // is why a tie is never scored against a tie in another bar, another voice, or, after
        // line-breaking, an identically-placed one on another system whose bars share a local X.
        // audit/lp-geometry system.tie-{under,over}-notes, and tie.y.{seconds,triad}.lower for
        // what solving them ONE AT A TIME cost.
        var columns = new List<List<TieItem>>();
        var columnOf = new Dictionary<(int Voice, int Measure, int Item), int>();
        foreach (var tie in ties)
        {
            var key = (tie.VoiceIndex, tie.StartMeasureIndex, tie.StartItemIndex);
            if (!columnOf.TryGetValue(key, out int existing))
            {
                existing = columns.Count;
                columnOf[key] = existing;
                columns.Add([]);
            }
            columns[existing].Add(tie);
        }

        foreach (var column in columns)
        {
            // The bounds and the break-up belong to the COLUMN, not to each tie: every tie of a
            // column runs between the same two chords, so they all split at the same systems.
            // ⚠️ LILYSHARP-OWN: A BROKEN COLUMN IS SOLVED ONCE PER SEGMENT, NOT ONCE.
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
            if (!measureMap.TryGetValue(anchor.StartMeasureIndex, out var startInfo))
                continue;
            if (!measureMap.TryGetValue(anchor.EndMeasureIndex, out var endInfo))
                continue;

            var (_, startMeasure) = startInfo;
            var (_, endMeasure) = endInfo;

            var segments = SpannerBreakSubstitution.Split(
                anchor.StartMeasureIndex, anchor.EndMeasureIndex, systems, measureToSystemIdx);

            if (segments.IsEmpty)
                continue;

            // Bottom -> top, the order LilyPond's front()/back() and its monotonicity terms are
            // written in. TieDetector already emits a chord's ties that way; sorting here says
            // so rather than relying on it.
            var ordered = column.OrderBy(t => t.StaffPosition).ToList();

            // The whole COLUMN's bound heads, which is what each chord outline is built from —
            // or a chord's upper tie recedes past a head that is there.
            var tiedPositions = ordered.Select(t => t.StaffPosition).Distinct().ToList();

            var solved = new TieLayout[ordered.Count, segments.Length];

            for (int s = 0; s < segments.Length; s++)
            {
                var specs = new List<TieSpecification>(ordered.Count);
                foreach (var tie in ordered)
                {
                    specs.Add(BuildTieSpecification(
                        score, systems, staff, staffIndex, tie, segments[s],
                        startMeasure, endMeasure, tiedPositions));
                }

                var layouts = new TieFormattingProblem(specs).Solve();
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
            foreach (var tie in column)
            {
                int i = ordered.FindIndex(t => ReferenceEquals(t, tie));
                for (int s = 0; s < segments.Length; s++)
                    tieLayouts.Add(solved[i, s]);
            }
        }

        return tieLayouts.ToImmutableArray();
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
        Score score,
        ImmutableArray<SystemLayout> systems,
        Model.Staff? staff,
        int staffIndex,
        TieItem tie,
        SpannerBreakSegment segment,
        MeasureLayout startMeasure,
        MeasureLayout endMeasure,
        List<int> tiedPositions)
    {
        int startDots = tie.StartNote.Dots;

        var segSystem = systems[segment.SystemIndex];

        double segStartX;
        TieColumnParts? startColumn = null;
        TieColumnParts? endColumn = null;
        if (segment.IsFirst)
        {
            // The item X is the LEFT edge of an undisplaced head; the seconds
            // displacement is per head and belongs to the outline, not to the column.
            double startColumnX = startMeasure.X
                + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, startMeasure);
            startColumn = BuildTieColumn(
                score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex,
                startColumnX, tiedPositions, isLeftBound: true);

            // The fallback anchor, used only when there is no column to read — on a
            // TAB, where the bound is a fret digit. It is this tie's own head's inner
            // edge, past its dots, which is where the tab rule below hangs the bow.
            // ⚠️ LILYSHARP-OWN, AND IT IS A SECOND SPELLING OF "WHERE THE DOTS END".
            //   BuildTieColumn boxes the dots at DotGap + n*(width + DotGap) off the
            //   column's rightmost head, which is where they are DRAWN; this says
            //   2*n*width off this head. The two disagree, and only this one is left.
            //   departs from: nothing in LilyPond — a tab digit is not a Note_head, so
            //     LilyPond's tie has no such anchor at all (it builds the outline from
            //     the TabNoteHead like any other head).
            //   goes away when: the tab tie stops being a Lily#-own placement, i.e.
            //     when a fret digit carries a column the outline can be built from.
            //     That is the same decision named in the tab branch below.
            //   observed by: NOTHING. No ledger point reads a tab tie's width, and
            //     test/tab-tie holds the drawing only. A DOTTED tab tie is not in the
            //     corpus at all, which is why the disagreement is currently unreachable.
            double startBase = startColumnX
                + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, tie.StaffPosition);
            int noteValue = tie.StartNote.BaseDuration.Numerator != 1
                ? 1
                : tie.StartNote.BaseDuration.Denominator;
            double outlineRight = GlyphMetrics.GetNoteheadAdvance(noteValue);
            if (startDots > 0)
                outlineRight += 2 * startDots * GlyphMetrics.AugmentationDot.Width;
            segStartX = startBase + outlineRight;
        }
        else
        {
            // Broken piece: the bound is the system edge, and there is no column.
            segStartX = segSystem.Measures[0].X;
        }

        double segEndX;
        if (segment.IsLast)
        {
            double endColumnX = endMeasure.X
                + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, endMeasure);
            endColumn = BuildTieColumn(
                score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex,
                endColumnX, tiedPositions, isLeftBound: false);
            // The fallback anchor (tab only): the right head's inner (left) edge.
            segEndX = endColumnX
                + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, tie.StaffPosition);
        }
        else
        {
            var lastMeasure = segSystem.Measures[^1];
            segEndX = lastMeasure.X + lastMeasure.Width;
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
            // The fret digits sit a TabHeadCenterOffset right of their note
            // columns (see EngravingDefaults), so shift the tie's note-end
            // attachments to match — otherwise the tie detaches to the left.
            if (segment.IsFirst) segStartX += EngravingDefaults.TabHeadCenterOffset;
            if (segment.IsLast) segEndX += EngravingDefaults.TabHeadCenterOffset;
            // A fret digit is not a note column: it has no chord outline to read, so the
            // tie hangs off the fixed anchors above and the whole Y-dependent
            // attachment (TieChordOutline) does not apply.
            startColumn = null;
            endColumn = null;
            // LILYSHARP-OWN: no head extent on a tab, so the horizontal-distance term
            // (tie-formatting-problem.cc:665-683) scores 0 for BOTH ends here and the
            // attachment is whatever the digit rule above chose.
            //   departs from: :670, `spec.note_head_drul_[d]->extent (…)`. LilyPond
            //     builds that from a TabNoteHead like any other head, so it HAS the
            //     term; this engine's tab tie is anchored to a fret digit's edge by a
            //     rule of its own (see just above), and there is no box for the
            //     penalty to measure against that would mean the same thing.
            //   goes away when: the tab tie stops being a Lily#-own placement — i.e.
            //     when a tab digit carries a head extent the scorer can read. That is
            //     a decision, not a defect: HANDOFF 3 records that Lily# chooses a
            //     tab tie's SIDE from the string, which LilyPond is not asked about.
            //   observed by: NOTHING. There is no ledger point on a tab tie's width,
            //     and there cannot be one until the tab fixtures pin their strings
            //     (HANDOFF 1's "tab の残り 3 冊"). test/tab-tie holds the drawing only.
            // On a tab the tie connects two fret digits on ONE string, so it
            // belongs on that string's line — NOT at the notation pitch height.
            // It curves OPPOSITE the stem: below the digits when the stem
            // points up, above when it points down (matching the tab stem,
            // which uses note.StemUp).
            var geom = new TabStaffGeometry(staff.Tuning ?? TuningType.Guitar, staffY, staff.TabSourceClef, staff.Transposition);
            // A chord's per-string ties must each hug their OWN string.
            // LILYPOND-REF: lily/tab-note-heads-engraver.cc:106-123 — each
            // TabNoteHead's staff-position is the STRING LINE its
            // noteToFretFunction (exclusive chord allocation) assigned, and
            // the tie follows the heads. So resolve this note's string via
            // the chord's allocation, keyed by staff position (a chord tie's
            // synthesized start note carries no MIDI) — not a per-note
            // auto-fret, which hands several notes the same string.
            var tieItem = ItemAt(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex);
            double digitY = tieItem is ChordItem tieChord
                ? geom.ChordNoteDigitY(tieChord, tie.StaffPosition)
                : geom.DigitY(tie.StartNote.Midi, tie.StartNote.StringNumber);
            // LilyPond hangs the tab tie right at the digit's edge — a small,
            // shallow curve hugging the number — so offset by the VISIBLE
            // glyph half-height plus a hair, not the full erase-box height.
            double clearance = 0.36 * TabConstants.FretFontSize + 0.1; // ~0.54 sp at font 2.6
            bool stemUp = tie.StartNote.StemUp;
            y = digitY + (stemUp ? clearance : -clearance);
            // Curve opposite the stem (constructor-set property, no `with`). On a tab
            // this IS a decision and is IMPOSED — a Lily#-own feature (the tie belongs
            // to a string line, not to a pitch), so it does not go through the scored
            // search the notation staff's ties now use.
            tieForProblem = new TieItem(
                tie.StartNote, tie.EndNote, tie.StaffPosition, forcedCurveUp: !stemUp,
                tie.StartMeasureIndex, tie.EndMeasureIndex, tie.StartItemIndex, tie.EndItemIndex);
        }
        else
        {
            double staffMiddleDown = staffY + _options.StaffHeight / 2;
            y = staffMiddleDown - tie.StaffPosition / 2.0;
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
    /// Which way the stem of the note/chord at (<paramref name="measureIndex"/>,
    /// <paramref name="itemIndex"/>) points, or null when it has no stem to point.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie-formatting-problem.cc:687-697 score_aptitude — the tie's scorer
    /// takes the stem off each bound head and keeps it only if <c>Stem::is_normal_stem</c>,
    /// which a whole note's is not. Null therefore means "this bound casts no vote", not "down".
    /// </remarks>
    private static bool? BoundStemUp(Voice voice, int measureIndex, int itemIndex)
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

            if (leftEdge)
            {
                for (int i = lo; i <= hi; i++)
                    if (MusicItem.EdgeStaffPosition(items[i], slur.CurveUp) is { } p)
                        return p;
            }
            else
            {
                for (int i = hi; i >= lo; i--)
                    if (MusicItem.EdgeStaffPosition(items[i], slur.CurveUp) is { } p)
                        return p;
            }
        }
        return null;
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
        Voice voice, int measureIndex, int itemIndex, bool leftEdge)
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

        // Whole notes (value 1) and breves have no stem.
        bool hasStem = GlyphMetrics.NoteValueOf(baseDuration) >= 2;
        // Beamed on the INNER side (toward the other endpoint).
        bool beamedInner = beamed && (leftEdge ? !hasBeamEnd : !hasBeamStart);
        return new SlurEdgeInfo(hasStem, stemUp, beamedInner, beamed);
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
            default: return 0;
        }
        return GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(dur)).Width / 2.0;
    }

    /// <summary>
    /// Device-Y of the slur attachment when the endpoint note's stem joins a beam — LP's
    /// stem_extent_[Y][dir_] (slur-scoring.cc:554) = the beam stack's outer edge on the slur
    /// side. Uses the canonical <see cref="BeamLayout.OuterEdgeStaffSpaceAtX"/> (frame B) and
    /// converts to device once. False when the note is not in any supplied beam layout.
    /// </summary>
    private static bool TryGetBeamedStemTipDeviceY(
        ImmutableArray<BeamLayout> beamLayouts, int measureIndex, int itemIndex, double noteX,
        double staffMiddleDown, bool curveUp, out double stemTipDeviceY)
    {
        stemTipDeviceY = 0;
        if (beamLayouts.IsDefaultOrEmpty) return false;
        foreach (var bl in beamLayouts)
        {
            bool hit = false;
            foreach (var m in bl.Group.Members)
                if (m.ItemIndex == itemIndex && m.ResolveMeasureIndex(bl.Group.MeasureIndex) == measureIndex)
                    hit = true;
            if (!hit) continue;

            // curveUp == the endpoint note's stem direction here (caller gates on StemUp == curveUp).
            stemTipDeviceY = staffMiddleDown - bl.OuterEdgeStaffSpaceAtX(noteX, curveUp);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Note-head obstacles the slur encompasses within this broken segment, in
    /// device coordinates and sorted by X. The scorer treats the first and last
    /// columns as the slur's edges and scores head encompass over the interior,
    /// so the curve lifts to clear notes that bulge into its path. Returns an
    /// empty list when the segment covers no note column.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur-scoring.cc Slur_score_state::get_encompass_infos —
    /// the encompassed note columns (bounds included) feed score_encompass().
    /// </remarks>
    private static IReadOnlyList<SlurObstacle> BuildSlurObstacles(
        Voice voice, SystemLayout segSystem, SlurItem slur,
        double staffMiddleDown, double segStartX, double segEndX)
    {
        const double headHalfHeight = 0.5; // staff spaces, half a notehead
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
                int? topPos = MusicItem.EdgeStaffPosition(items[i], preferTop: true);
                int? bottomPos = MusicItem.EdgeStaffPosition(items[i], preferTop: false);
                if (topPos is null || bottomPos is null)
                    continue; // rest / spacer / barline — no head

                double x = ml.X + GetItemXOffset(voice, mi, i, ml);
                if (x < segStartX - eps || x > segEndX + eps)
                    continue;

                // Visual top edge = highest pitch (smallest device Y) minus half a
                // head; visual bottom edge = lowest pitch plus half a head.
                double topY = (staffMiddleDown - topPos.Value / 2.0) - headHalfHeight;
                double bottomY = (staffMiddleDown - bottomPos.Value / 2.0) + headHalfHeight;
                obstacles.Add(new SlurObstacle(x, topY, bottomY, SlurObstacleType.NoteHead));
            }
        }

        obstacles.Sort((a, b) => a.X.CompareTo(b.X));
        return obstacles;
    }

    public ImmutableArray<SlurLayout> LayoutSlurs(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1, Model.Staff? staff = null, ImmutableArray<GraceNoteItem> graceNotes = default, ImmutableArray<BeamLayout> beamLayouts = default)
        => LayoutSlurs(_slurDetector.DetectSlurs(score), score, systems, staffIndex, staff,
            graceNotes, beamLayouts);

    /// <summary>The same, on slurs the caller has ALREADY detected — the slur twin of
    /// <see cref="LayoutTies(ImmutableArray{TieItem}, Score, ImmutableArray{SystemLayout}, int, Model.Staff?)"/>,
    /// and for the same reason.</summary>
    internal ImmutableArray<SlurLayout> LayoutSlurs(
        ImmutableArray<SlurItem> slurs, Score score, ImmutableArray<SystemLayout> systems,
        int staffIndex = -1, Model.Staff? staff = null,
        ImmutableArray<GraceNoteItem> graceNotes = default,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (slurs.Length == 0)
            return ImmutableArray<SlurLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var measureToSystemIdx = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var slurLayouts = new List<SlurLayout>();

        // The slur end attaches to the note-head EDGE, then lifts 0.5 staff-space
        // beyond it (the beamed stem-tip path below lifts the same 0.5 off the beam).
        // Every standard notehead shares the LILC Y half-extent, so the head edge is
        // the note centre ± that half-height; the enumeration in SlurScoringProblem now
        // starts AT this base with no further lift of its own.
        // LILYPOND-REF: lily/slur-scoring.cc:556-557 get_base_attachments —
        //   y = head->extent(Y)[dir]; y += dir * 0.5 * staff_space.
        double slurOffset = GlyphMetrics.NoteheadBlack.Top + 0.5; // 0.545 + 0.5 = 1.045 ss

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
                        + GetItemXOffset(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, startMeasure)
                        // Follow the curve-side head's within-chord displacement (seconds).
                        + GetChordHeadXOffset(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, slur.StartStaffPosition);
                }
                else
                {
                    segStartX = segSystem.Measures[0].X;
                }

                double segEndX;
                if (segment.IsLast)
                {
                    segEndX = endMeasure.X
                        + GetItemXOffset(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, endMeasure)
                        // Follow the curve-side head's within-chord displacement (seconds).
                        + GetChordHeadXOffset(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, slur.EndStaffPosition);
                }
                else
                {
                    var lastMeasure = segSystem.Measures[^1];
                    segEndX = lastMeasure.X + lastMeasure.Width;
                }

                // On a TAB staff a slur connects fret DIGITS on their string
                // lines, not notation pitch heights. The notation SlurScoringProblem
                // (5 staff lines, pitch dy) is meaningless here and ran the arc
                // through the digits — including a grace note's digit. Lay the tab
                // slur out directly instead, hugging above the numbers it spans.
                if (staff is { IsTab: true })
                {
                    var tabLayout = BuildTabSlurLayout(
                        score, slur, segment.IsFirst, segment.IsLast, segSystem,
                        staffIndex, staff, segStartX, segEndX, graceNotes);
                    if (tabLayout != null)
                        slurLayouts.Add(tabLayout with { RenderMeasureIndex = segment.StartMeasureIndex });
                    continue;
                }

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
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, leftEdge: true)
                    : default;
                var rightEdgeInfo = segment.IsLast
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, leftEdge: false)
                    : default;
                const double stemTipGap = 0.5; // staff-spaces beyond the beam (LP dir_*0.5*staff_space)

                double segStartY;
                if (segment.IsFirst && leftEdgeInfo.StemUp == slur.CurveUp && leftEdgeInfo.BeamedInner
                    && TryGetBeamedStemTipDeviceY(beamLayouts, slur.StartMeasureIndex, slur.StartItemIndex,
                        segStartX, staffMiddleDown, slur.CurveUp, out double startTip))
                    segStartY = startTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                    segStartY = (staffMiddleDown - startStaffPos / 2.0)
                        + (slur.CurveUp ? -slurOffset : slurOffset);

                double segEndY;
                if (segment.IsLast && rightEdgeInfo.StemUp == slur.CurveUp && rightEdgeInfo.BeamedInner
                    && TryGetBeamedStemTipDeviceY(beamLayouts, slur.EndMeasureIndex, slur.EndItemIndex,
                        segEndX, staffMiddleDown, slur.CurveUp, out double endTip))
                    segEndY = endTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                    segEndY = (staffMiddleDown - endStaffPos / 2.0)
                        + (slur.CurveUp ? -slurOffset : slurOffset);

                var obstacles = BuildSlurObstacles(
                    score.Voices[slur.VoiceIndex], segSystem, slur, staffMiddleDown, segStartX, segEndX);

                // A slur avoids only other slurs whose SPAN OVERLAPS IT IN TIME. LilyPond
                // populates a slur's encompass-objects at ENGRAVE time: an acknowledged slur
                // (or tie, or avoid-slur=inside object) is added to every slur that is still
                // OPEN at that moment, so a slur in a later bar -- closed before the next one
                // opens -- never enters this one's set. Matching that by musical span, rather
                // than by the drawn X the collision term itself uses, is what keeps a slur on
                // one system from avoiding an identically-placed one on ANOTHER: after
                // line-breaking their bars share a local X, but never a span.
                // LILYPOND-REF: lily/slur.cc:364-387 Slur::auxiliary_acknowledge_extra_object
                //   adds e to `slurs`/`end_slurs` (the currently-OPEN slurs); read back in
                //   scoring at lily/slur-scoring.cc:679-682. audit/lp-geometry
                //   system.slur-{under,over}-notes.
                var overlappingSlurs = slurLayouts
                    .Where(sl => SlurSpansOverlap(slur, sl.Slur))
                    .ToList();

                var problem = new SlurScoringProblem(
                    slur, segStartX, segStartY, segEndX, segEndY, staffMiddleDown,
                    obstacles: obstacles,
                    existingSlurs: overlappingSlurs,
                    isBrokenLeft: !segment.IsFirst,
                    isBrokenRight: !segment.IsLast,
                    leftEdge: leftEdgeInfo,
                    rightEdge: rightEdgeInfo);
                slurLayouts.Add(problem.Solve() with { StaffIndex = staffIndex, RenderMeasureIndex = segment.StartMeasureIndex });
            }
        }

        return slurLayouts.ToImmutableArray();
    }

    /// <summary>
    /// Whether two slur spans overlap in musical time — the condition under which LilyPond
    /// makes them avoid one another.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/slur.cc:364-387 Slur::auxiliary_acknowledge_extra_object adds an
    /// acknowledged slur to another's <c>encompass-objects</c> only while the other is still
    /// OPEN, i.e. when their <c>[start, end]</c> spans overlap. Two disjoint spans (one bar's
    /// slur closing before the next opens) never reference each other, which is exactly why a
    /// slur repeated on a later system does not avoid the one above it.
    /// </remarks>
    private static bool SlurSpansOverlap(SlurItem a, SlurItem b) =>
        !(SpanBefore(a.EndMeasureIndex, a.EndItemIndex, b.StartMeasureIndex, b.StartItemIndex)
          || SpanBefore(b.EndMeasureIndex, b.EndItemIndex, a.StartMeasureIndex, a.StartItemIndex));

    /// <summary>Whether position (<paramref name="m1"/>, <paramref name="i1"/>) strictly
    /// precedes (<paramref name="m2"/>, <paramref name="i2"/>) — so a span touching another at
    /// a shared column still counts as overlapping, as LilyPond's end_slurs branch does.</summary>
    private static bool SpanBefore(int m1, int i1, int m2, int i2) =>
        m1 < m2 || (m1 == m2 && i1 < i2);

    /// <summary>
    /// Device-Y of an item's TOP fret-digit row (smallest string number = highest
    /// line). Used both to hang a tab slur's endpoints off the digits and to find
    /// the topmost digit the arch must clear.
    /// </summary>
    private static double TabItemTopDigitY(MusicItem item, TabStaffGeometry geom)
        => geom.StringY(geom.StemHeadString(item, stemUp: true));

    /// <summary>The voice item at (measure, index), or null if out of range.</summary>
    private static MusicItem? ItemAt(Voice voice, int measureIndex, int itemIndex)
    {
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length) return null;
        var items = voice.Measures[measureIndex].Items;
        return itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
    }

    /// <summary>
    /// Lays out a slur on a TAB staff: a shallow arch ABOVE the fret numbers,
    /// anchored on each edge's digit and lifted clear of the TOPMOST digit it
    /// spans — the encompassed main notes AND the grace digits hanging off them,
    /// which is exactly what a pitch-based arc used to run straight through.
    /// Bypasses <see cref="SlurScoringProblem"/> (five staff lines, pitch dy),
    /// which does not model a tab staff.
    /// </summary>
    private SlurLayout? BuildTabSlurLayout(
        Score score, SlurItem slur, bool isFirst, bool isLast, SystemLayout segSystem,
        int staffIndex, Model.Staff staff, double segStartX, double segEndX,
        ImmutableArray<GraceNoteItem> graceNotes)
    {
        var voice = score.Voices[slur.VoiceIndex];
        // Within-system staff-top offset (device, down from system top), NOT absolute —
        // so the tab slur's digit/string geometry is system-independent and DrawSlurs
        // (shared with the notation slur) can add the system-top Y-up back uniformly.
        // TabStaffGeometry is additive in staffY (StringY = StaffY + n·space), so this is
        // a pure origin shift that leaves the device string frame intact (island 2).
        double staffY = LayoutUtilities.StaffOffsetInSystemDown(segSystem, staffIndex);
        var geom = new TabStaffGeometry(staff.Tuning ?? TuningType.Guitar, staffY, staff.TabSourceClef, staff.Transposition);

        // The fret digits sit a TabHeadCenterOffset right of their note columns
        // (see EngravingDefaults); shift the real (unbroken) edges to hang the
        // slur off the digit, not the bare column — same as the tab tie.
        double startX = segStartX + (isFirst ? EngravingDefaults.TabHeadCenterOffset : 0);
        double endX = segEndX + (isLast ? EngravingDefaults.TabHeadCenterOffset : 0);
        if (endX - startX < 0.5)
            return null;

        // Digit row at each edge (the edge item's top string). A broken
        // continuation has no edge note in this piece — anchor at the staff top.
        MusicItem? startItem = isFirst ? EdgeItem(voice, slur.StartMeasureIndex, slur.StartItemIndex) : null;
        MusicItem? endItem = isLast ? EdgeItem(voice, slur.EndMeasureIndex, slur.EndItemIndex) : null;
        double startDigitY = startItem is { } si ? TabItemTopDigitY(si, geom) : geom.StaffY;
        double endDigitY = endItem is { } ei ? TabItemTopDigitY(ei, geom) : geom.StaffY;

        // Topmost digit the arch must clear: every encompassed main note plus the
        // grace digits attached to them.
        double topDigitY = Math.Min(startDigitY, endDigitY);
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
                if (items[i] is NoteItem or ChordItem)
                    topDigitY = Math.Min(topDigitY, TabItemTopDigitY(items[i], geom));
        }
        var graces = graceNotes.IsDefault ? ImmutableArray<GraceNoteItem>.Empty : graceNotes;
        foreach (var gr in graces)
        {
            if (gr.StaffIndex != staffIndex
                || gr.MeasureIndex < slur.StartMeasureIndex || gr.MeasureIndex > slur.EndMeasureIndex)
                continue;
            int lo = gr.MeasureIndex == slur.StartMeasureIndex ? slur.StartItemIndex : 0;
            int hi = gr.MeasureIndex == slur.EndMeasureIndex ? slur.EndItemIndex : int.MaxValue;
            if (gr.MainNoteItemIndex < lo || gr.MainNoteItemIndex > hi)
                continue;
            foreach (var gn in gr.Notes)
                topDigitY = Math.Min(topDigitY, geom.DigitY(gn.Midi));
        }

        // Hug just above the digit (the visible glyph half-height plus a hair,
        // matching the tab tie's clearance), and let the arch peak clear the
        // topmost digit by the same margin — and rise a touch above the endpoints
        // so it reads as a curve even on one string.
        double clearance = 0.36 * TabConstants.FretFontSize + 0.1;
        double startY = startDigitY - clearance;
        double endY = endDigitY - clearance;
        double peakY = Math.Min(topDigitY - clearance, Math.Min(startY, endY) - 0.4);
        // Symmetric cubic midpoint B(0.5).y = (startY + endY + 6*controlY)/8; solve
        // controlY so the peak reaches peakY.
        double controlY = (8 * peakY - startY - endY) / 6;
        double dx = endX - startX;
        var c1 = (X: startX + dx * 0.3, Y: controlY);
        var c2 = (X: startX + dx * 0.7, Y: controlY);

        // Tab slurs bow UP (above the numbers), independent of the notation slur's
        // pitch-derived direction; DrawBow tapers by CurveUp, so record it here.
        var tabSlur = new SlurItem(
            slur.StartStaffPosition, slur.EndStaffPosition, curveUp: true,
            slur.StartMeasureIndex, slur.EndMeasureIndex,
            slur.StartItemIndex, slur.EndItemIndex, slur.VoiceIndex);
        // The geometry above is device Y; BowLayout stores page Y-up (= -device),
        // so reflect the endpoints and control points on the way in.
        return new SlurLayout(tabSlur, startX, -startY, endX, -endY,
            (c1.X, -c1.Y), (c2.X, -c2.Y),
            isBrokenLeft: !isFirst, isBrokenRight: !isLast) { StaffIndex = staffIndex };

        static MusicItem? EdgeItem(Voice v, int mi, int ii)
        {
            if (mi < 0 || mi >= v.Measures.Length) return null;
            var items = v.Measures[mi].Items;
            return ii >= 0 && ii < items.Length ? items[ii] : null;
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
