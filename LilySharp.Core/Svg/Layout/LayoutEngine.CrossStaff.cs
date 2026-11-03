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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>
    /// Lays every cross-staff beam again on the FINAL systems, in the frame of the beam's own
    /// staff, and hands back the carried beams with those replaced. A score without one gets
    /// its two tables back untouched.
    /// </summary>
    /// <remarks>
    /// The preliminary pass may lay them too, and nothing reads that: a cross-staff beam is in
    /// no skyline (SkylineBuilder.FillBeamedItemsToSuppress), so it does not take part in the
    /// spacing that decides the distance it depends on.
    /// LILYPOND-REF: lily/axis-group-interface.cc:850-858 skyline_spacing — cross-staff grobs are
    ///   left out of the skyline; lily/beam.cc:1497-1513 Beam::is_cross_staff defers them.
    /// </remarks>
    private (Dictionary<int, ImmutableArray<BeamLayout>> ByStaff, ImmutableArray<BeamLayout> All)
        LayCrossStaffBeams(MultiStaffScore score, ImmutableArray<SystemLayout> systems,
            MultiStaffLayouter layouter,
            Dictionary<int, ImmutableArray<BeamLayout>> byStaff, ImmutableArray<BeamLayout> all)
    {
        bool any = false;
        foreach (var beam in all)
            if (beam.Group.IsCrossStaff) { any = true; break; }
        if (!any)
            return (byStaff, all);

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        HashSet<MusicItem>? crossMembers = null;
        var newByStaff = new Dictionary<int, ImmutableArray<BeamLayout>>(byStaff.Count);
        var newAll = ImmutableArray.CreateBuilder<BeamLayout>(all.Length);
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
        {
            var laid = byStaff[staffIndex];
            var groups = ImmutableArray.CreateBuilder<BeamGroup>();
            foreach (var group in layouter.StaffBeamGroupsOf(score, staff, staffIndex))
            {
                if (!group.IsCrossStaff || !measureMap.TryGetValue(group.MeasureIndex, out var at))
                    continue;
                var system = at.System;
                double beamStaffY = LayoutUtilities.FindStaffYInSystem(system, staffIndex);
                var g = group;
                bool forced = VoiceDefaults.GetDefaultStemUpAt(
                    staff.Voices, g.VoiceIndex, g.MeasureIndex, g.StartIndex) is not null;
                double PlacedOffsetOf(int i)
                {
                    int target = g.Members[i].TargetStaffIndex;
                    return target < 0
                        ? 0.0
                        : (LayoutUtilities.FindStaffYInSystem(system, target) - beamStaffY) * 2.0;
                }
                var across = BeamDetector.AcrossStaves(g, PlacedOffsetOf, i =>
                {
                    int target = g.Members[i].TargetStaffIndex;
                    return target < 0
                        ? 0.0
                        : PureOffset(score, staffIndex, target, ref crossMembers, layouter) ?? PlacedOffsetOf(i);
                }, forced);
                // The knee is only known now, and a stem's readers ask its stamp (a tuplet's
                // side, a slur's end): they run after this.
                for (int i = 0; i < across.Members.Length; i++)
                {
                    bool up = across.Members[i].MemberStemUp;
                    switch (across.ItemOf(i))
                    {
                        case NoteItem { BeamId: { } id } n: n.StampBeam(up, id, null); break;
                        case ChordItem { BeamId: { } id } c: c.StampBeam(up, id, null); break;
                    }
                }
                groups.Add(across);
            }
            if (groups.Count == 0)
            {
                newByStaff[staffIndex] = laid;
                newAll.AddRange(laid);
                continue;
            }
            var again = _elementCoordinator.LayoutBeams(
                MultiStaffLayouter.StaffBeamScoreOf(score, staff, staffIndex), systems, staffIndex,
                groups.ToImmutable(), MultiStaffLayouter.BeamStaffLinesOf(staff));
            var merged = ImmutableArray.CreateBuilder<BeamLayout>(laid.Length);
            foreach (var beam in laid)
                if (!beam.Group.IsCrossStaff)
                    merged.Add(beam);
            merged.AddRange(again);
            var staffBeams = merged.ToImmutable();
            newByStaff[staffIndex] = staffBeams;
            newAll.AddRange(staffBeams);
        }
        return (newByStaff, newAll.ToImmutable());
    }

    /// <summary>staff-staff-spacing's basic-distance and padding, which a staff's pure distance
    /// reads. LILYPOND-REF: scm/define-grobs.scm VerticalAxisGroup — staff-staff-spacing.</summary>
    private const double PureBasicDistance = 9.0, PurePadding = 1.0;

    /// <summary>The length a pure stem is estimated at: the ideal unbeamed stem.</summary>
    private const double PureStemLength = 3.5;

    /// <summary>
    /// The distance, in staff positions up-positive, from one staff to an ADJACENT other that
    /// LilyPond's knee judges a cross-staff beam by: not the placed distance but the PURE one,
    /// the two staves' whole-score bounding boxes apart, and at least the basic distance. Null
    /// for staves that are not neighbours (the caller reads the placed distance).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:228-238 internal_get_minimum_translations — dy =
    ///   skyline distance + padding, and for the whole score max (dy, basic-distance).
    /// LILYPOND-REF: lily/align-interface.cc:90-125 get_skylines — pure: a flat box of the
    ///   rest of the line, the clefs apart.
    /// Not literal: the boxes here hold heads and the estimated length of a stem that is not
    /// a cross-staff beam's, where LilyPond's hold every inside-staff grob's pure height.
    /// MEASURED, LilyPond 2.26.0 (Lab sessions/p1093/p2): `&lt;c' e'&gt;8 g'` knees with the placed
    /// staves too close to knee, which is why the placed distance cannot answer.
    /// </remarks>
    private static double? PureOffset(MultiStaffScore score, int fromStaff, int toStaff,
        ref HashSet<MusicItem>? crossMembers, MultiStaffLayouter layouter)
    {
        if (Math.Abs(fromStaff - toStaff) != 1)
            return null;
        if (crossMembers is null)
        {
            crossMembers = new HashSet<MusicItem>();
            foreach (var (_, st, si) in score.EnumerateStaves())
                foreach (var g in layouter.StaffBeamGroupsOf(score, st, si))
                    if (g.IsCrossStaff)
                        foreach (var item in g.MemberItems())
                            crossMembers.Add(item);
        }
        Staff? from = null, to = null;
        foreach (var (_, st, si) in score.EnumerateStaves())
        {
            if (si == fromStaff) from = st;
            if (si == toStaff) to = st;
        }
        if (from is null || to is null)
            return null;
        var upper = fromStaff < toStaff ? from : to;
        var lower = fromStaff < toStaff ? to : from;
        double dy = Math.Max(PureBasicDistance,
            ExtentOf(lower, crossMembers).Hi - ExtentOf(upper, crossMembers).Lo + PurePadding);
        return (fromStaff < toStaff ? -dy : dy) * 2.0;
    }

    /// <summary>A staff's pure box, in staff spaces about its middle line: its lines, its own
    /// notes and the notes moved onto it.</summary>
    private static (double Lo, double Hi) ExtentOf(Staff staff, HashSet<MusicItem> crossMembers)
    {
        double lo = -2, hi = 2;
        void Head(MusicItem item, bool stem)
        {
            if (MusicItem.EdgeStaffPosition(item, preferTop: true) is not { } top
                || MusicItem.EdgeStaffPosition(item, preferTop: false) is not { } bottom)
                return;
            lo = Math.Min(lo, bottom / 2.0 - 0.5);
            hi = Math.Max(hi, top / 2.0 + 0.5);
            if (stem && item is NoteItem { StemUp: var up })
            {
                if (up) hi = Math.Max(hi, bottom / 2.0 + PureStemLength);
                else lo = Math.Min(lo, top / 2.0 - PureStemLength);
            }
        }
        foreach (var voice in staff.Voices)
            foreach (var measure in voice.Measures)
                foreach (var item in measure.Items)
                    if (item.CrossStaffIndex < 0 && !item.GraceTime && item is NoteItem or ChordItem)
                        Head(item, !crossMembers.Contains(item));
        foreach (var crossed in staff.CrossedIn)
            Head(crossed.Item, false);
        return (lo, hi);
    }

    /// <summary>A tie between notes <c>@cross</c> moved stands about THEIR staff: it was laid
    /// about the voice's, with the heads' positions already the other staff's, so the whole bow
    /// moves by the distance between the two.</summary>
    /// <remarks>⚠️ Both ends are taken to stand together. A tie from one staff to the other is
    /// drawn about the first note's staff; LilyPond slants it.</remarks>
    private static ImmutableArray<TieLayout> TiesOnTheirNotesStaff(ImmutableArray<TieLayout> ties,
        ImmutableArray<SystemLayout> systems, int staffIndex, IReadOnlyDictionary<int, int> systemOfMeasure)
    {
        if (ties.IsDefaultOrEmpty)
            return ties;
        ImmutableArray<TieLayout>.Builder? moved = null;
        for (int i = 0; i < ties.Length; i++)
        {
            var tie = ties[i];
            int target = tie.Tie.StartNote.CrossStaffIndex;
            if (target < 0 || !systemOfMeasure.TryGetValue(tie.RenderMeasureIndex, out int sys)
                || (uint)sys >= (uint)systems.Length)
                continue;
            moved ??= ties.ToBuilder();
            moved[i] = (TieLayout)tie.ShiftedUp(LayoutUtilities.FindStaffYInSystem(systems[sys], target)
                - LayoutUtilities.FindStaffYInSystem(systems[sys], staffIndex));
        }
        return moved?.ToImmutable() ?? ties;
    }

    /// <summary>A script on a note <c>@cross</c> moved is read about the staff the note stands
    /// on (<see cref="ArticulationLayout.YUp"/> is about its <c>StaffIndex</c>): the engraver read
    /// the head's position, which is that staff's already.</summary>
    private static ImmutableArray<ArticulationLayout> ScriptsOnTheirNotesStaff(
        MultiStaffScore? score, ImmutableArray<ArticulationLayout> scripts)
    {
        if (score is null || scripts.IsDefaultOrEmpty)
            return scripts;
        bool any = false;
        foreach (var (_, st, _) in score.EnumerateStaves())
            if (!st.CrossedIn.IsEmpty) { any = true; break; }
        if (!any)
            return scripts;
        var builder = scripts.ToBuilder();
        for (int i = 0; i < builder.Count; i++)
        {
            var a = builder[i];
            if (a.SourceIndex < 0 || a.SourceIndex >= score.Articulations.Length)
                continue;
            foreach (var (_, st, si) in score.EnumerateStaves())
            {
                if (si != a.StaffIndex)
                    continue;
                int vi = score.Articulations[a.SourceIndex].VoiceIndex;
                if (vi < st.Voices.Length && a.MeasureIndex < st.Voices[vi].Measures.Length
                    && a.ItemIndex < st.Voices[vi].Measures[a.MeasureIndex].Items.Length
                    && st.Voices[vi].Measures[a.MeasureIndex].Items[a.ItemIndex].CrossStaffIndex is >= 0 and var target)
                    builder[i] = a with { StaffIndex = target };
                break;
            }
        }
        return builder.ToImmutable();
    }
}
