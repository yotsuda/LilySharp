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
                        : PureOffset(score, staffIndex, target) ?? PlacedOffsetOf(i);
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
                groups.ToImmutable(), MultiStaffLayouter.BeamStaffLinesOf(staff),
                target =>
                {
                    foreach (var (_, st, si) in score.EnumerateStaves())
                        if (si == target)
                            return st;
                    return null;
                },
                // The plain beams of every staff are laid: a cross-staff beam avoids the
                // later-starting ones where its stems stand (ElementCoordinator.AppendCoveredBeams).
                target => byStaff.TryGetValue(target, out var theirs) ? theirs : ImmutableArray<BeamLayout>.Empty);
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

    /// <summary>The pure distance (<see cref="CrossStaffPure"/>) from one staff to an adjacent
    /// other, in staff positions up-positive; null for staves that are not neighbours.</summary>
    private static double? PureOffset(MultiStaffScore score, int fromStaff, int toStaff)
    {
        if (Math.Abs(fromStaff - toStaff) != 1)
            return null;
        Staff? from = null, to = null;
        foreach (var (_, st, si) in score.EnumerateStaves())
        {
            if (si == fromStaff) from = st;
            if (si == toStaff) to = st;
        }
        if (from is null || to is null)
            return null;
        double dy = fromStaff < toStaff ? CrossStaffPure.Distance(from, to) : CrossStaffPure.Distance(to, from);
        return (fromStaff < toStaff ? -dy : dy) * 2.0;
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
            // …or a rest (docs/cross-rest-design.md): its shift is that staff's already
            // (Staff.CrossedRestShifts), so a script over it was read in that frame too.
            if (!st.CrossedIn.IsEmpty || !st.RestsCrossedIn.IsEmpty) { any = true; break; }
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

    /// <summary>A dynamic or an <c>@text</c> on a note or rest <c>@cross</c> moved hangs off the
    /// staff the note stands on, as a script does (<see cref="ScriptsOnTheirNotesStaff"/>): its
    /// supports were read off the head's position, which is that staff's already, so only the
    /// staff its <see cref="DynamicLayout.YUp"/> is about changes. One a running hairpin's line
    /// took (<paramref name="groups"/>) stays with its line's staff.</summary>
    /// <remarks>
    /// LILYPOND-REF: the twin writes <c>@cross</c> as <c>\change Staff</c> around the note, which
    ///   moves the whole Voice: its Dynamic_engraver's and Text_engraver's grobs are that
    ///   staff's (ly/engraver-init.ly — both are consisted in the Voice).
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1352/marks q2): <c>c4 g4@cross@p c2</c> in the
    ///   left hand: the p at -6.81 under the right hand (Lily# drew it -6.82 under the left).
    /// </remarks>
    private static ImmutableArray<DynamicLayout> DynamicsOnTheirNotesStaff(MultiStaffScore? score,
        ImmutableArray<DynamicLayout> dynamics, ImmutableArray<DynamicAlignEngraver.AlignedLineGroup> groups)
    {
        if (score is null || dynamics.IsDefaultOrEmpty)
            return dynamics;
        bool any = false;
        foreach (var (_, st, _) in score.EnumerateStaves())
            if (!st.CrossedIn.IsEmpty || !st.RestsCrossedIn.IsEmpty) { any = true; break; }
        if (!any)
            return dynamics;
        HashSet<int>? lined = null;
        if (!groups.IsDefaultOrEmpty)
            foreach (var g in groups)
                foreach (int di in g.DynamicIndices)
                    (lined ??= new HashSet<int>()).Add(di);
        ImmutableArray<DynamicLayout>.Builder? moved = null;
        for (int i = 0; i < dynamics.Length; i++)
        {
            var d = dynamics[i];
            if (lined != null && lined.Contains(i))
                continue;
            foreach (var (_, st, si) in score.EnumerateStaves())
            {
                if (si != d.StaffIndex)
                    continue;
                if (d.VoiceIndex < st.Voices.Length && d.MeasureIndex < st.Voices[d.VoiceIndex].Measures.Length
                    && d.ItemIndex < st.Voices[d.VoiceIndex].Measures[d.MeasureIndex].Items.Length
                    && st.Voices[d.VoiceIndex].Measures[d.MeasureIndex].Items[d.ItemIndex].CrossStaffIndex is >= 0 and var target)
                    (moved ??= dynamics.ToBuilder())[i] = d with { StaffIndex = target };
                break;
            }
        }
        return moved?.ToImmutable() ?? dynamics;
    }
}
