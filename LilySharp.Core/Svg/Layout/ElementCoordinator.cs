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
            // ⚠️ NO `case RestItem` — a rest is not in the Beam's collision-interfaces, and
            // the caller drops it before reaching here. The removed arm booked the rest's
            // box at its DEFAULT position and lifted the beam over it; LilyPond moves the
            // REST instead. Do not restore it: the missing entry in the list IS the rule.
            // LILYPOND-REF: scm/define-grobs.scm:496-504 collision-interfaces = note-head-interface,
            //   stem-interface and seven more, but no rest-interface;
            //   lily/beam.cc:1331 rest_collision_callback moves the rest instead.
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

                // LILYPOND-REF: beam.cc:1388-1392 rest_dim = rest_extent[d], the extent
                // TRANSLATED by prev_offset — the rest's REAL glyph ink where the voiced
                // position and Rest_collision already put it, on top of its default origin
                // (a semibreve hangs from the line above the middle, rest.cc:101-121;
                // every shorter rest sits at 0). The key is the beam's OWN voice: the rest
                // this pass moves is a member of the beam, and the beam knows whose it is.
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
                    // the distance from it — and both the +2 a semibreve gets from its
                    // written position and the +2 in the neutral letter cancel, leaving
                    // the written position itself for every duration.
                    shifts[new RestShiftKey(m, v, i)] =
                        RestStaffPosition(pitched, pitched.VoiceDirection, pitchedValue)
                        - (pitchedValue == 1 ? 2.0 : 0.0);
                }
            }
        }

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
                    // duration) — rest.cc's staff_position_internal — and everything
                    // below translates from there. The renderer's default is the
                    // NEUTRAL letter (middle; whole hangs at +2), so the emitted shift
                    // carries the base displacement too.
                    int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                    double basePos = RestStaffPosition(rest, dir, restValue);
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
    // internal: ItemSkylineFactory prices a voiced rest's separation box at this same
    // position (the PURE side of the offset chain — the collision push below is unpure).
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
    internal static double RestStaffPosition(RestItem rest, int dir, int restValue) =>
        rest.StaffPosition is { } written
            ? (restValue == 1 ? written + 2.0 : written)
            : VoicedRestPosition(dir, restValue);

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
    /// VOICED one (<see cref="VoicedRestPosition"/>): the Rest_collision and beam pushes
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
                    bool voiceUp = VoiceDefaults.GetDefaultStemUpAt(staff.Voices, v, m) ?? (v % 2 == 0);
                    int dir = voiceUp ? 1 : -1;
                    switch (item)
                    {
                        case RestItem rest:
                            // Pure position from the STAMPED direction (zero = outside
                            // every span → the neutral origin), the same slot
                            // ItemSkylineFactory prices — not a re-derived voice default.
                            int restValue = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                            int pure = rest.VoiceDirection == 0 && rest.StaffPosition is null
                                ? (restValue == 1 ? 2 : 0)
                                : (int) RestStaffPosition(rest, rest.VoiceDirection, restValue);
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
        Voice voice, int measureIndex, int itemIndex, bool leftEdge,
        double columnX = double.NaN, double staffMiddleDown = double.NaN,
        Dictionary<(int Measure, int Item), BeamLayout>? beamByMember = null)
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
        // The endpoint head's ink width — LP's slur_head_x_extent_, consumed by the
        // tilt X shift and the extra-encompass edge check.
        double headWidth = GlyphMetrics.GetNoteheadBBox(
            GlyphMetrics.NoteValueOf(baseDuration)).Width;

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
            double stemX = LayoutUtilities.StemX(columnX, stemUp, col.NoteValue, col.Notehead);
            double halfStem = EngravingDefaults.StemThickness / 2.0;
            stemXLo = stemX - halfStem;
            stemXHi = stemX + halfStem;
            stemBeginY = staffMiddleDown - col.HeadPositionToward(!stemUp) / 2.0;
            if (TryGetBeamedStemTipDeviceY(beamByMember, measureIndex, itemIndex,
                    stemX, staffMiddleDown, stemUp, out double tip))
                stemTipY = tip;
            else
                stemTipY = staffMiddleDown - EngravingDefaults.StaffMiddle
                    + col.OutwardTipDeviceY(stemUp);
            var flag = beamed ? default : GlyphMetrics.GetFlagBBox(col.NoteValue, stemUp);
            if (flag != default)
            {
                stemXHi = Math.Max(stemXHi, stemX + flag.Width);
                // The flag's reach from the tip toward the head (device Y),
                // spelled the way ItemSkylineFactory reserves the same ink.
                double flagInnerY = stemUp
                    ? stemTipY - flag.Bottom - flag.Top
                    : stemTipY + flag.Top - flag.Bottom;
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
        return GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(dur)).Width / 2.0;
    }

    /// <summary>
    /// Device-Y of the slur attachment when the endpoint note's stem joins a beam — LP's
    /// stem_extent_[Y][dir_] (slur-scoring.cc:554) = the beam stack's outer edge on the slur
    /// side. Uses the canonical <see cref="BeamLayout.OuterEdgeStaffSpaceAtX"/> (frame B) and
    /// converts to device once. False when the note is not in any supplied beam layout.
    /// </summary>
    private static bool TryGetBeamedStemTipDeviceY(
        Dictionary<(int Measure, int Item), BeamLayout>? beamByMember,
        int measureIndex, int itemIndex, double noteX,
        double staffMiddleDown, bool curveUp, out double stemTipDeviceY)
    {
        stemTipDeviceY = 0;
        if (beamByMember is null
            || !beamByMember.TryGetValue((measureIndex, itemIndex), out var bl))
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
    /// <see cref="BeamGroup.OuterEdgeStaffSpaceAtX"/> (= LP's stem extent, which
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
        Dictionary<(int Measure, int Item), BeamLayout>? beamByMember,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure,
        GraceObstacleGeom?[]? graceGeomCache)
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
                    double rx = ml.X + GetItemXOffset(voice, mi, i, ml);
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
                double x = ml.X + GetItemXOffset(voice, mi, i, ml);
                if (x < segStartX - eps || x > segEndX + eps)
                    continue;
                double obstacleX = x + EndpointHeadHalfWidth(voice, mi, i);

                // Visual top edge = highest pitch (smallest device Y) minus half a
                // head; visual bottom edge = lowest pitch plus half a head.
                double topY = (staffMiddleDown - topPos.Value / 2.0) - headHalfHeight;
                double bottomY = (staffMiddleDown - bottomPos.Value / 2.0) + headHalfHeight;

                // stem_ / the stem-x_ move, only when the stem points WITH the slur.
                // LILYPOND-REF: slur-scoring.cc:146-158.
                double stemY = double.NaN;
                if (NoteColumnLayout.Of(items[i]) is { } col
                    && col.HasStem && col.StemUp == slur.CurveUp)
                {
                    double stemX = LayoutUtilities.StemX(
                        x, col.StemUp, col.NoteValue, col.Notehead);
                    if (TryGetBeamedStemTipDeviceY(beamByMember, mi, i, stemX,
                            staffMiddleDown, col.StemUp, out double beamTip))
                        // Beamed: the extent already ends on the stack's outer face
                        // (beam_end_corrective, stem.cc:142); LP adds another half
                        // beam thickness on top (slur-scoring.cc:149-150).
                        stemY = beamTip + (col.StemUp ? -0.5 : 0.5)
                            * EngravingDefaults.BeamThickness;
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
                ml, mi, hi, staffMiddleDown, segStartX, segEndX);
        }

        obstacles.Sort((a, b) => a.X.CompareTo(b.X));
        return obstacles;
    }

    /// <summary>
    /// One grace group's slur-obstacle geometry, resolved ONCE per
    /// <see cref="LayoutSlurs"/> pass and cached by group index: the column
    /// springs (<see cref="SpacingRules.GraceColumns"/>) and the beam quant
    /// (<see cref="GraceNoteEngraver.QuantGraceBeam"/>) are the expensive
    /// parts, and re-solving them for every covering slur segment cost +36%
    /// wall-clock on a 300-bar grace-under-slur book (perf-slurgrace300).
    /// </summary>
    private readonly record struct GraceObstacleGeom(
        ImmutableArray<double> Offsets, double Span, double? BeamLeftY, double? BeamRightY);

    /// <summary>
    /// Adds the grace columns the slur covers in measure <paramref name="mi"/> to
    /// <paramref name="obstacles"/> — heads at the grace font's own ink, stems
    /// forced UP (score-grace-settings), the group's geometry rebuilt from the
    /// same producers the renderer reads. See
    /// <see cref="BuildSlurObstacles"/> for the LP references and disclosures.
    /// </summary>
    private static void AddGraceObstaclesForMeasure(
        List<SlurObstacle> obstacles, Voice voice, SlurItem slur,
        ImmutableArray<GraceNoteItem> graceNotes,
        Dictionary<int, List<int>>? graceByMeasure, GraceObstacleGeom?[]? graceGeomCache,
        MeasureLayout ml, int mi, int hi,
        double staffMiddleDown, double segStartX, double segEndX)
    {
        if (graceByMeasure is null || graceGeomCache is null
            || !graceByMeasure.TryGetValue(mi, out var groupIndices))
            return;
        const double eps = 0.001;

        foreach (int gi in groupIndices)
        {
            var g = graceNotes[gi];
            // Covered = the grace's MAIN note lies inside the span, excluding the
            // start note itself: its grace run sounds BEFORE the slur opens, so
            // LP's engraver never acknowledges it into this slur.
            bool afterStart = mi > slur.StartMeasureIndex
                || g.MainNoteItemIndex > slur.StartItemIndex;
            bool beforeEnd = mi < slur.EndMeasureIndex
                || g.MainNoteItemIndex <= slur.EndItemIndex;
            if (!afterStart || !beforeEnd || g.MainNoteItemIndex > hi)
                continue;

            if (graceGeomCache[gi] is not { } geom)
            {
                var mainItem = ItemAt(voice, mi, g.MainNoteItemIndex);
                var columns = SpacingRules.GraceColumns(g.Notes, mainItem);
                var (bl, br) = GraceNoteEngraver.QuantGraceBeam(g, columns.Offsets);
                geom = new GraceObstacleGeom(columns.Offsets, columns.Span, bl, br);
                graceGeomCache[gi] = geom;
            }
            double groupX = ml.X
                + GetItemXOffset(voice, mi, g.MainNoteItemIndex, ml)
                - geom.Span;

            var font = GraceNoteItem.Font;
            double headHalf = font.NoteheadBlack.Top;
            // The quanted grace beam (null for a lone / unbeamable run): the
            // scored line's staff-position pair at the two OUTER STEMS, exactly
            // what the renderer anchors the drawn beam on.
            var (beamL, beamR) = (geom.BeamLeftY, geom.BeamRightY);
            double StemXAt(int k) => LayoutUtilities.StemX(
                groupX + (k < geom.Offsets.Length ? geom.Offsets[k] : 0.0),
                up: true, noteValue: 4, NoteheadStyle.Default, font);

            for (int k = 0; k < g.Notes.Length; k++)
            {
                double hx = groupX + (k < geom.Offsets.Length ? geom.Offsets[k] : 0.0);
                if (hx < segStartX - eps || hx > segEndX + eps)
                    continue;
                var note = g.Notes[k];
                double headCenterY = staffMiddleDown - note.StaffPosition / 2.0;

                // Grace stems are forced UP whatever the pitch
                // (scm/music-functions.scm:652-656 score-grace-settings), so the
                // stem participates only under an UP slur.
                double stemY = double.NaN;
                double obstacleX = hx + font.NoteheadBlackAdvance / 2.0;
                if (slur.CurveUp)
                {
                    double stemX = StemXAt(k);
                    if (beamL is { } bl && beamR is { } br && g.Notes.Length > 1)
                    {
                        // Beamed run: the quanted line interpolated to this stem's
                        // X, plus a full grace beam thickness — half for the stem
                        // extent's beam_end_corrective, half for LP's encompass
                        // margin (slur-scoring.cc:149-150).
                        double xL = StemXAt(0), xR = StemXAt(g.Notes.Length - 1);
                        double t = xR - xL > 0.001 ? (stemX - xL) / (xR - xL) : 0.0;
                        double centerUp = (bl + (br - bl) * t) / 2.0;
                        stemY = staffMiddleDown
                            - (centerUp + EngravingDefaults.GraceBeamThickness);
                    }
                    else
                    {
                        // Lone / flagged grace: the drawn stem end — head centre
                        // plus the renderer's fixed grace stem length.
                        stemY = headCenterY
                            - EngravingDefaults.DefaultStemLength * GraceNoteItem.ScaleFactor;
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
    /// scale 1 (the same choice BuildSlurObstacles makes with its 0.5 head box),
    /// and DotConfiguration.Resolve runs with NO direction where the seed feeds
    /// the voice-forced one — a forced-voice score could seat the scored dot row
    /// one position off the drawn one.
    /// </remarks>
    private static IReadOnlyList<SlurExtraObject> BuildSlurExtraObjects(
        Voice voice, SystemLayout segSystem, SlurItem slur,
        double staffMiddleDown, double segStartX, double segEndX,
        ImmutableArray<TupletBracketLayout> tupletNumbers = default,
        ImmutableArray<TupletBracketItem> tupletItems = default,
        ImmutableArray<InsideSlurScript> insideScripts = default)
    {
        // thickness_ = Slur.thickness (1.2, define-grobs.scm) * the layout
        // line-thickness dimension (0.1 ss at default staff size) = 0.12 ss.
        // LILYPOND-REF: lily/slur-scoring.cc:248-251 line_thickness field.
        const double slurThickness = 1.2 * 0.1;
        const double eps = 0.001;
        var extras = new List<SlurExtraObject>();

        void AddDotRow(int dotCount, double dotStartX, double dotCenterDeviceY)
        {
            var dotBox = GlyphMetrics.AugmentationDot;
            double advance = 2 * dotBox.Width;
            double left = dotStartX + dotBox.Left - slurThickness;
            double right = dotStartX + (dotCount - 1) * advance + dotBox.Right + slurThickness;
            // Device Y down: top edge = centre - (half height + widens).
            double halfH = dotBox.Top + 0.2 + slurThickness * 0.5;
            extras.Add(new SlurExtraObject(
                left, right,
                dotCenterDeviceY - halfH, dotCenterDeviceY + halfH,
                SlurAvoidType.Inside,
                SlurScoreParameters.Default.ExtraObjectCollisionPenalty));
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
                double x = ml.X + GetItemXOffset(voice, mi, i, ml);
                if (x < segStartX - eps || x > segEndX + eps)
                    continue;

                switch (items[i])
                {
                    case NoteItem { Dots: > 0 } note:
                    {
                        int value = GlyphMetrics.NoteValueOf(note.BaseDuration);
                        double dotX = x + GlyphMetrics.GetNoteheadBBox(value).Right
                            + GlyphMetrics.AugmentationDot.Width;
                        int pos = DotConfiguration.Resolve(new[] { note.StaffPosition })[0];
                        AddDotRow(note.Dots, dotX, staffMiddleDown - pos / 2.0);
                        break;
                    }
                    case ChordItem { Dots: > 0 } chord when chord.Notes.Length > 0:
                    {
                        int value = GlyphMetrics.NoteValueOf(chord.BaseDuration);
                        var headOffsets = ChordHeadPositioning.CalculateOffsets(
                            chord.Notes, chord.StemUp, value, 1.0);
                        double dotX = x + GlyphMetrics.GetNoteheadBBox(value).Right
                            + Math.Max(0, headOffsets.Max())
                            + GlyphMetrics.AugmentationDot.Width;
                        var positions = DotConfiguration.Resolve(
                            chord.Notes.Select(n => n.StaffPosition).ToArray());
                        foreach (int p in positions)
                            AddDotRow(chord.Dots, dotX, staffMiddleDown - p / 2.0);
                        break;
                    }
                    case RestItem { Dots: > 0 } rest:
                    {
                        int value = GlyphMetrics.NoteValueOf(rest.BaseDuration);
                        double dotX = x + GlyphMetrics.GetRestBBox(value).Right
                            + GlyphMetrics.AugmentationDot.Width;
                        // A rest's dots sit in the space above the middle line
                        // (position 1), as the renderer draws them.
                        AddDotRow(rest.Dots, dotX, staffMiddleDown - 0.5);
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

                double halfW = Rendering.TextFontMetrics.Advance(
                    t.NumberText, TupletBracketEngraver.NumberFontSize,
                    sans: false, TupletBracketEngraver.NumberFontStyle) / 2.0;
                double halfH = Rendering.TextFontMetrics.InkHeight(
                    t.NumberText, TupletBracketEngraver.NumberFontSize,
                    sans: false, TupletBracketEngraver.NumberFontStyle) / 2.0;
                // NumberYUp is staff-spaces above this staff's TOP line (the
                // layout ran with no staff offset); page device Y down.
                double cy = staffMiddleDown - 2.0 - t.NumberYUp;
                extras.Add(new SlurExtraObject(
                    t.NumberX - halfW - slurThickness,
                    t.NumberX + halfW + slurThickness,
                    cy - halfH - slurThickness * 0.5,
                    cy + halfH + slurThickness * 0.5,
                    SlurAvoidType.Inside,
                    SlurScoreParameters.Default.ExtraObjectCollisionPenalty));
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
        // ⚠️ The gate is the SLUR's span, item-level at the boundary measures, because LP's
        //   is engraver timing: a script is acknowledged at a timestep where the slur is
        //   open, and a slur is open at both of its own bound notes (slurs[] at the start,
        //   end_slurs[] at the end). The same rule CoveringSlurPiece uses for the other
        //   direction.
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
                if (s.MeasureIndex < slur.StartMeasureIndex
                    || s.MeasureIndex > slur.EndMeasureIndex)
                    continue;
                if (s.MeasureIndex == slur.StartMeasureIndex && s.ItemIndex < slur.StartItemIndex)
                    continue;
                if (s.MeasureIndex == slur.EndMeasureIndex && s.ItemIndex > slur.EndItemIndex)
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
                    SlurScoreParameters.Default.ExtraObjectCollisionPenalty));
            }
        }

        return extras;
    }

    public ImmutableArray<SlurLayout> LayoutSlurs(Score score, ImmutableArray<SystemLayout> systems, int staffIndex = -1, Model.Staff? staff = null, ImmutableArray<GraceNoteItem> graceNotes = default, ImmutableArray<BeamLayout> beamLayouts = default, Func<ImmutableArray<InsideSlurScript>>? insideScripts = null)
        => LayoutSlurs(_slurDetector.DetectSlurs(score), score, systems, staffIndex, staff,
            graceNotes, beamLayouts, insideScripts);

    /// <summary>The same, on slurs the caller has ALREADY detected — the slur twin of
    /// <see cref="LayoutTies(ImmutableArray{TieItem}, Score, ImmutableArray{SystemLayout}, int, Model.Staff?)"/>,
    /// and for the same reason.</summary>
    /// <param name="insideScripts">This staff's <c>avoid-slur = #'inside</c> marks, already
    /// placed in the staff's own frame — see
    /// <see cref="ArticulationEngraver.InsideSlurScriptLayouts"/>. A FACTORY, not an array,
    /// so that the extra script walk is paid only by a staff that has slurs at all: a
    /// script-heavy but slur-free book must not buy it once per staff per system.</param>
    internal ImmutableArray<SlurLayout> LayoutSlurs(
        ImmutableArray<SlurItem> slurs, Score score, ImmutableArray<SystemLayout> systems,
        int staffIndex = -1, Model.Staff? staff = null,
        ImmutableArray<GraceNoteItem> graceNotes = default,
        ImmutableArray<BeamLayout> beamLayouts = default,
        Func<ImmutableArray<InsideSlurScript>>? insideScripts = null)
    {
        if (slurs.Length == 0)
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
            int graceStaff = Math.Max(staffIndex, 0);
            for (int gi = 0; gi < graceNotes.Length; gi++)
            {
                var g = graceNotes[gi];
                if (g.StaffIndex != graceStaff || g.Notes.IsDefaultOrEmpty)
                    continue;
                graceByMeasure ??= new Dictionary<int, List<int>>();
                if (!graceByMeasure.TryGetValue(g.MeasureIndex, out var list))
                    graceByMeasure[g.MeasureIndex] = list = new List<int>();
                list.Add(gi);
            }
            if (graceByMeasure != null)
                graceGeomCache = new GraceObstacleGeom?[graceNotes.Length];
        }

        // Beam lookup, once per pass: (measure, item) → its beam layout. The
        // per-column stem resolution used to scan every beam layout's member
        // list for every covered column of every slur — quadratic in bars on a
        // beamed-and-slurred book (perf-slurbeam300).
        Dictionary<(int Measure, int Item), BeamLayout>? beamByMember = null;
        if (!beamLayouts.IsDefaultOrEmpty)
        {
            beamByMember = new Dictionary<(int, int), BeamLayout>();
            foreach (var bl in beamLayouts)
                foreach (var m in bl.Group.Members)
                    // TryAdd, not indexer: (measure, item) is ambiguous across
                    // VOICES on a shared staff, and the linear scan this replaces
                    // returned the FIRST matching layout — last-wins flipped one
                    // multi-voice snapshot (test/dot-cross-voice-spacing).
                    beamByMember.TryAdd((m.ResolveMeasureIndex(bl.Group.MeasureIndex), m.ItemIndex), bl);
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
                score.TupletBrackets, mlArr.ToImmutableArray(),
                score.Voices[0].Measures, beamGroups, beamLayouts);
        }

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
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.StartMeasureIndex, slur.StartItemIndex, leftEdge: true,
                        windowStartX, staffMiddleDown, beamByMember)
                    : default;
                var rightEdgeInfo = segment.IsLast
                    ? ResolveSlurEdge(score.Voices[slur.VoiceIndex], slur.EndMeasureIndex, slur.EndItemIndex, leftEdge: false,
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
                    && TryGetBeamedStemTipDeviceY(beamByMember, slur.StartMeasureIndex, slur.StartItemIndex,
                        segStartX, staffMiddleDown, slur.CurveUp, out double startTip))
                    segStartY = startTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                    segStartY = (staffMiddleDown - startStaffPos / 2.0)
                        + (slur.CurveUp ? -slurOffset : slurOffset);

                double segEndY;
                if (endRest is { } eRest)
                    segEndY = RestBoundBaseY(eRest);
                else if (segment.IsLast && rightEdgeInfo.StemUp == slur.CurveUp && rightEdgeInfo.BeamedInner
                    && TryGetBeamedStemTipDeviceY(beamByMember, slur.EndMeasureIndex, slur.EndItemIndex,
                        segEndX, staffMiddleDown, slur.CurveUp, out double endTip))
                    segEndY = endTip + (slur.CurveUp ? -stemTipGap : stemTipGap);
                else
                    segEndY = (staffMiddleDown - endStaffPos / 2.0)
                        + (slur.CurveUp ? -slurOffset : slurOffset);

                var obstacles = BuildSlurObstacles(
                    score.Voices[slur.VoiceIndex], segSystem, slur, staffMiddleDown,
                    windowStartX, windowEndX, beamByMember, graceNotes,
                    graceByMeasure, graceGeomCache);

                var extraObjects = BuildSlurExtraObjects(
                    score.Voices[slur.VoiceIndex], segSystem, slur, staffMiddleDown, windowStartX, windowEndX,
                    tupletNumberLayouts, score.TupletBrackets, insideScriptLayouts);

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
                    rightEdge: rightEdgeInfo,
                    extraObjects: extraObjects);
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
