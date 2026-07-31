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
    private readonly VoiceCollector _voiceCollector = new();
    private readonly NoteCollision _noteCollision = new();

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

            var offsets = _noteCollision.CalculateVoiceOffsets(column);

            foreach (var (voiceId, itemIndex, xOffset, headTransparent, dotForceDown) in offsets)
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
            var measure = score.Voices[group.VoiceIndex].Measures[memberMeasure];
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
            group.GrowDirection,
            group.VoiceIndex);

        // Cross-measure collision detection is deferred — pass empty list for now.
        var beamLayout = _beamEngraver.CalculateBeamLayout(
            renumberedGroup,
            memberXs,
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
            beamLayout.MemberStaffIndices);
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
                              beamEdgeLeftX, beamEdgeRightX, beamOriginX);
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
    /// ⚠️ The head's own STEM is a covered grob too, with its own
    /// <c>STEM_COLLISION_FACTOR</c> (:401-418). Lily# does not collect stems yet; that is
    /// the remaining half of this supply, and it is why
    /// <see cref="BeamQuantParameters.StemCollisionFactor"/> has no reader.
    /// </para>
    /// </remarks>
    private static void AddItemCollisions(
        List<BeamCollision> collisions, MusicItem item, double itemX,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
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
                AddBoxCollision(collisions, itemX + box.Left, itemX + box.Right,
                                centreSs + box.Bottom, centreSs + box.Top,
                                beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                break;
            }
            case NoteItem note:
                AddHeadCollision(collisions, itemX, note.StaffPosition,
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
                    AddHeadCollision(collisions, itemX + offsets[n], chord.Notes[n].StaffPosition,
                                     noteValue, beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                break;
            }
        }
    }

    /// <summary>One note head's box as a covered grob.</summary>
    private static void AddHeadCollision(
        List<BeamCollision> collisions, double headX, int staffPosition, int noteValue,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        var box = GlyphMetrics.GetNoteheadBBox(noteValue);
        double centreSs = staffPosition * 0.5;
        AddBoxCollision(collisions, headX + box.Left, headX + box.Right,
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
    private static void AddBoxCollision(
        List<BeamCollision> collisions,
        double inkLeft, double inkRight, double minY, double maxY,
        double beamEdgeLeftX, double beamEdgeRightX, double beamOriginX)
    {
        // :381 — the box must overlap the beam's DRAWN x extent (x_pos), not the note
        // columns: LilyPond's x_pos is the beam's own stencil span.
        if (inkRight < beamEdgeLeftX || inkLeft > beamEdgeRightX)
            return;
        // :383
        if (inkRight <= inkLeft || maxY <= minY)
            return;

        // :388-389 — staff_space_ is 1 in this frame, so the factor is sqrt(width).
        double widthFactor = Math.Sqrt(inkRight - inkLeft);

        // :391-392 — TWO entries per grob, at its two x edges, each carrying the WHOLE y
        // extent. x is measured from the beam's left STEM; the quanter moves it the last
        // half stem width onto the beam's drawn edge.
        collisions.Add(new BeamCollision(inkLeft - beamOriginX, minY, maxY, widthFactor));
        collisions.Add(new BeamCollision(inkRight - beamOriginX, minY, maxY, widthFactor));
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
        return columnX + (up ? EngravingDefaults.StemUpAttachX : EngravingDefaults.StemDownAttachX);
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
                    if (BeamAccidentalColumn.CalculateSinglePosition(
                            note, note.IsCue ? CueAccidentalScale : 1.0) is { } single)
                        AddAccidentalCollision(
                            collisions, single, itemX, note.IsCue ? CueAccidentalScale : 1.0,
                            beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                    break;

                case ChordItem chord:
                    // The stagger the renderer uses: reversed heads move their accidentals,
                    // so the column must be solved, not assumed.
                    // LILYPOND-REF: lily/accidental-placement.cc position_apes.
                    var offsets = ChordHeadPositioning.CalculateOffsets(
                        chord.Notes, chord.StemUp,
                        LayoutUtilities.GetNoteValueFromFraction(chord.BaseDuration));
                    foreach (var al in BeamAccidentalColumn.CalculatePositions(chord.Notes, offsets))
                        AddAccidentalCollision(collisions, al, itemX, 1.0,
                                               beamEdgeLeftX, beamEdgeRightX, beamOriginX);
                    break;
            }
        }
    }

    /// <summary>LilyPond's CueVoice fontSize = -4 shrinks the accidental grob with the head.</summary>
    private const double CueAccidentalScale = 0.66;

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
                                  beamEdgeLeftX, beamEdgeRightX, beamOriginX);
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
    /// LP only shifts a rest that is a MEMBER of a beam (rest -> stem -> beam),
    /// i.e. a rest sitting BETWEEN the beam's outer stems (e.g. <c>c8[ r e]</c>);
    /// a rest not spanned by a beam is left alone. Beam members here are notes,
    /// so we model that membership by item-index containment: a rest whose
    /// itemIndex lies strictly between the beam's first and last member is under
    /// that beam. (The previous port clamped the beam Y to the beam's endpoints
    /// and so shifted EVERY rest in the measure against EVERY beam — including
    /// rests nowhere near a beam, which LP never touches.)
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
            var members = beamLayout.Group.Members;
            if (members.Length < 2)
                continue;

            int measureIndex = beamLayout.Group.MeasureIndex;
            if (!measureMap.TryGetValue(measureIndex, out var measureLayout))
                continue;

            var measure = score.Voice.Measures[measureIndex];
            int firstItem = members[0].ItemIndex;
            int lastItem = members[^1].ItemIndex;

            // LILYPOND-REF: beam.cc:1372 d = get_grob_direction(stem) — UP = +1,
            // DOWN = -1. Lily# beam Y is staff-positions-from-middle, up-positive,
            // the same sign convention LP uses for positions.
            int d = beamLayout.Group.StemUp ? 1 : -1;

            // LILYPOND-REF: beam.cc:1376-1385 height_of_my_beams = beam_thickness/2
            // + (beam_count - 1) * beam_translation.
            double beamThickness = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamThickness);
            double beamTranslation = EngravingDefaults.ToStaffPositions(EngravingDefaults.BeamTranslation);
            int beamCount = members.Max(m => m.BeamCount);
            double heightOfBeams = beamThickness / 2 + (beamCount - 1) * beamTranslation;

            for (int itemIdx = firstItem + 1; itemIdx < lastItem; itemIdx++)
            {
                if (itemIdx >= measure.Items.Length || measure.Items[itemIdx] is not RestItem)
                    continue;
                if (itemIdx >= measureLayout.Items.Length)
                    continue;

                double restX = measureLayout.X + measureLayout.Items[itemIdx].X;

                // LILYPOND-REF: beam.cc:1373-1386 stem_y is the beam Y interpolated at
                // the rest's stem X; beam_y = stem_y - d*height is the beam edge facing
                // the rest. The rest is within the beam's x-span, so no clamp is needed.
                double stemY = beamLayout.GetYAtX(restX);
                double beamY = stemY - d * heightOfBeams;

                // LILYPOND-REF: beam.cc:1389-1392 rest_dim = rest_extent[d] — the rest
                // edge facing the beam. (LP uses the glyph's real extent; we use the
                // symmetric approximation RestCenterPosition +- RestExtent.)
                double restCenterY = EngravingDefaults.RestCenterPosition;
                double restExtent = EngravingDefaults.RestExtent;
                double restDim = restCenterY + d * restExtent;

                // LILYPOND-REF: beam.cc:1393-1399 shift = d*min(d*(beam_y - d*min - rest_dim), 0).
                double minimumDistance = EngravingDefaults.RestBeamMinDistance;
                double shift = d * Math.Min(d * (beamY - d * minimumDistance - restDim), 0.0);

                if (Math.Abs(shift) <= EngravingDefaults.RestShiftThreshold)
                    continue;

                // LILYPOND-REF: beam.cc:1403-1404 always move by discrete half-spaces
                // (= whole staff positions).
                shift = Math.Ceiling(Math.Abs(shift)) * Math.Sign(shift);

                // LILYPOND-REF: beam.cc:1406-1412 if the shifted rest is still inside
                // the staff, move by whole spaces (= even staff positions) instead.
                double nearEdge = restDim + shift;
                double farEdge = (restCenterY - d * restExtent) + shift;
                bool insideStaff =
                    (nearEdge >= staffSpan.Low && nearEdge <= staffSpan.High) ||
                    (farEdge >= staffSpan.Low && farEdge <= staffSpan.High);
                if (insideStaff)
                    shift = Math.Ceiling(Math.Abs(shift) / 2.0) * 2.0 * Math.Sign(shift);

                var key = new RestShiftKey(measureIndex, itemIdx);
                if (!shifts.TryGetValue(key, out var existing)
                    || Math.Abs(shift) > Math.Abs(existing))
                    shifts[key] = shift;
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
        double scale = chord.IsCue ? 0.66 : 1.0;
        var offsets = ChordHeadPositioning.CalculateOffsets(
            chord.Notes, chord.StemUp, noteValue, scale);
        for (int i = 0; i < chord.Notes.Length; i++)
            if (chord.Notes[i].StaffPosition == staffPosition)
                return offsets[i];
        return 0;
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
                // TieFormattingProblem attaches the bow to the head's inner EDGE (seg*X) when the
                // scored endpoint stays within the head box and to the head CENTRE (seg*CenterX)
                // when it clears the box — LilyPond reads the chord-outline skyline at the tie's Y.
                // We supply both anchors; the scorer picks per candidate. See TieFormattingProblem.GetAttachment.
                double segStartX;
                double segStartCenterX;
                if (segment.IsFirst)
                {
                    // The item X is the head's LEFT edge; seconds displacement follows the head.
                    double startBase = startMeasure.X
                        + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, startMeasure)
                        + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.StartMeasureIndex, tie.StartItemIndex, tie.StaffPosition);

                    int noteValue = tie.StartNote.BaseDuration.Numerator != 1
                        ? 1
                        : tie.StartNote.BaseDuration.Denominator;
                    double advance = GlyphMetrics.GetNoteheadAdvance(noteValue);

                    // Inner edge = right edge of the head plus its augmentation dots.
                    // LILYPOND-REF: scm/define-grobs.scm DotColumn padding; scm/output-lib.scm ly:dots::print.
                    double outlineRight = advance;
                    if (startDots > 0)
                    {
                        double dotWidth = GlyphMetrics.AugmentationDot.Width;
                        outlineRight += 2 * startDots * dotWidth;
                    }
                    segStartX = startBase + outlineRight;
                    // Head centre — dots are NOT subtracted (they sit on the note line, off the
                    // cleared tie Y, so LilyPond's outline recedes past them to the head centre).
                    segStartCenterX = startBase + advance / 2.0;
                }
                else
                {
                    // Broken piece: the bound is the system edge — no head, so centre == edge.
                    segStartX = segSystem.Measures[0].X;
                    segStartCenterX = segStartX;
                }

                double segEndX;
                double segEndCenterX;
                if (segment.IsLast)
                {
                    double endBase = endMeasure.X
                        + GetItemXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, endMeasure)
                        + GetChordHeadXOffset(score.Voices[tie.VoiceIndex], tie.EndMeasureIndex, tie.EndItemIndex, tie.StaffPosition);
                    int endNoteValue = tie.EndNote.BaseDuration.Numerator != 1
                        ? 1
                        : tie.EndNote.BaseDuration.Denominator;
                    segEndX = endBase;                                                      // inner (left) edge of the right head
                    segEndCenterX = endBase + GlyphMetrics.GetNoteheadAdvance(endNoteValue) / 2.0; // right head centre
                }
                else
                {
                    var lastMeasure = segSystem.Measures[^1];
                    segEndX = lastMeasure.X + lastMeasure.Width;
                    segEndCenterX = segEndX;
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
                    // Tab ties hug the digit at a fixed edge (the notehead edge/centre skyline
                    // does not apply to fret digits), so keep the edge/centre rule a no-op here.
                    segStartCenterX = segStartX;
                    segEndCenterX = segEndX;
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
                    // Curve opposite the stem (constructor-set property, no `with`).
                    tieForProblem = new TieItem(
                        tie.StartNote, tie.EndNote, tie.StaffPosition, curveUp: !stemUp,
                        tie.StartMeasureIndex, tie.EndMeasureIndex, tie.StartItemIndex, tie.EndItemIndex);
                }
                else
                {
                    double staffMiddleDown = staffY + _options.StaffHeight / 2;
                    y = staffMiddleDown - tie.StaffPosition / 2.0;
                }

                // The tie-tie collision term (ScoreTieTieCollision) is scored WITHIN one tie
                // column. LilyPond builds one Tie_formatting_problem per Tie_column and feeds it
                // only that column's ties (lily/tie-column.cc:81-93 Tie_column::calc_positioning_done
                // -> problem.from_ties (ties)), so a tie is scored against the OTHER ties of its
                // OWN chord and never against a tie in another bar, another voice, or -- after
                // line-breaking -- an identically-placed one on another system whose bars share a
                // local X. A column is the ties of one chord, so its members share the start chord:
                // same voice, same start measure and item (the ties differ only by staff position).
                // Grouping on that column anchor -- not on the drawn X the collision term uses --
                // is what keeps a tie on one system from flipping to avoid a coincidental
                // X-overlap on another; the broken pieces of THIS tie (same TieItem) are dropped
                // too, as LilyPond scores the column once, unbroken.
                // audit/lp-geometry system.tie-{under,over}-notes.
                var columnTies = tieLayouts
                    .Where(tl => !ReferenceEquals(tl.Tie, tie)
                        && tl.Tie.VoiceIndex == tie.VoiceIndex
                        && tl.Tie.StartMeasureIndex == tie.StartMeasureIndex
                        && tl.Tie.StartItemIndex == tie.StartItemIndex)
                    .ToList();

                var problem = new TieFormattingProblem(
                    tieForProblem, segStartX, segEndX, segStartCenterX, segEndCenterX, y,
                    existingTies: columnTies,
                    startDots: segment.IsFirst ? startDots : 0,
                    isBrokenLeft: !segment.IsFirst,
                    isBrokenRight: !segment.IsLast);
                tieLayouts.Add(problem.Solve() with { StaffIndex = staffIndex, RenderMeasureIndex = segment.StartMeasureIndex });
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
    {
        var slurs = _slurDetector.DetectSlurs(score);

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
