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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Aligns hairpins and dynamic texts on a horizontal line: the grobs a RUNNING hairpin
/// links (the text at its start moment, the wedge itself, the text that terminates it,
/// and a hairpin chained on at that same moment) ride ONE DynamicLineSpanner, so the
/// whole group is side-positioned ONCE and every member sits on the same baseline.
/// A dynamic with no hairpin at its moment keeps the one-column placement
/// <see cref="DynamicEngraver.Calculate"/> already gave it — LilyPond's line spanner
/// closes in the very timestep that created it, which IS the one-column case.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:119-160 acknowledge_dynamic — every
///   dynamic grob joins the CURRENT line spanner (:141 create_line_spanner, :152
///   Axis_group_interface::add_element).
/// LILYPOND-REF: lily/dynamic-align-engraver.cc:194-235 stop_translation_timestep —
///   :196-208 keep a running_ set of live dynamic spanners; :210
///   <c>bool end = line_ &amp;&amp; running_.empty ()</c> — the line closes only in a
///   timestep where NO hairpin runs. A hairpin therefore keeps the line alive from its
///   start timestep to its end timestep; the terminating text is acknowledged in that
///   final timestep and joins the SAME line; and a second hairpin STARTING at the
///   timestep the first ends is in <c>started_</c> when the first is erased, so the
///   line chains onward unbroken.
/// LILYPOND-REF: scm/define-grobs.scm DynamicLineSpanner — its description names the
///   members ("a vertical baseline to align successive dynamic grobs"); DynamicText
///   hangs <see cref="DynamicEngraver.TextOffsetInSpanner"/> below the spanner
///   (define-grobs.scm:1450 Y-offset, "center on an 'm'") and the Hairpin centres on
///   it (self-alignment-Y . CENTER), which is why the text spends −0.6 (scaled with the
///   letters' size) inside the group profile and the wedge spends nothing.
/// ⚠️ NOT PORTED, DISCLOSED (no pair measures either) — both are LP behaviours this
///   grouping does not reproduce, not Lily#-own quantities (§5.2 audit, session 158):
///   ⑴ (PORTED, session 1225) LilyPond BREAKS the line when a new dynamic carries an explicit
///     direction differing from the line's (:125-138), and the marks after it follow the
///     line they join. The SIDES are settled where the score is gathered
///     (Svg.Collector.DynamicLineSides); here a line is built per staff AND side
///     (<see cref="DynamicLine.IsAbove"/>): the hairpins of one side chain, the texts of
///     that side inside the span join, and the texts a hairpin left behind on the other
///     side are a line of their own (<see cref="BuildLines"/>, the numbered lines).
///   ⑵ Same-column stacking (two voices' dynamics on one column,
///     <see cref="DynamicEngraver.StackStep"/>) is not re-applied to a grouped text —
///     in LilyPond those come from two Voice contexts and ride two separate lines.
/// </remarks>
internal static class DynamicAlignEngraver
{
    /// <summary>
    /// One DynamicLineSpanner: the chained hairpins (indices into the detected
    /// hairpin items) and the dynamic texts of the same side (indices into the dynamic
    /// layouts) that ride it, spanning [Start..End] moments on one staff, below it or —
    /// <paramref name="IsAbove"/> — above.
    /// </summary>
    internal readonly record struct DynamicLine(
        int StaffIndex,
        int StartMeasureIndex, int StartItemIndex,
        int EndMeasureIndex, int EndItemIndex,
        ImmutableArray<int> HairpinItemIndices,
        ImmutableArray<int> DynamicLayoutIndices,
        bool IsAbove = false,
        // The voice of the staff the line is of (its hairpins' HairpinItem.VoiceIndex): the
        // Start / End item indices count that voice's items — the End those of EndVoiceIndex,
        // which differs only where another voice's mark ended the line's last hairpin.
        int VoiceIndex = 0,
        int EndVoiceIndex = 0);

    /// <summary>
    /// One BROKEN PIECE of a multi-member line, in layout-index terms: the members
    /// (indices into the returned dynamic/hairpin layout arrays) that sit on one
    /// system and must move as ONE grob through the outside-staff pass — LilyPond's
    /// pass places the DynamicLineSpanner, not its children one by one.
    /// LILYPOND-REF: scm/define-grobs.scm:1407 DynamicLineSpanner outside-staff-priority
    ///   = 250 — the priority is the SPANNER's.
    /// <paramref name="IsAbove"/>: the line stands above its staff, so the ABOVE pass moves
    /// it (<see cref="OutsideStaffStacker.StackAboveStaff"/>) and the below pass leaves it.
    /// </summary>
    public readonly record struct AlignedLineGroup(
        ImmutableArray<int> DynamicIndices,
        ImmutableArray<int> HairpinIndices,
        bool IsAbove = false);

    /// <summary>
    /// Rewrites the Y of every grouped member so texts and wedges linked by running
    /// hairpins share one side-positioned line, and returns the per-system member
    /// groups for the outside-staff pass to move as single grobs. Layouts not in any
    /// multi-member group are returned unchanged (byte-identical to their engravers'
    /// own answers) and appear in no group.
    /// </summary>
    public static (ImmutableArray<DynamicLayout> Dynamics, ImmutableArray<HairpinLayout> Hairpins,
                   ImmutableArray<AlignedLineGroup> Groups)
        AlignLines(
            Rendering.ScoreTextMetrics fonts,
            ImmutableArray<HairpinItem> hairpinItems,
            ImmutableArray<DynamicItem> dynamics,
            ImmutableArray<DynamicLayout> dynamicLayouts,
            ImmutableArray<HairpinLayout> hairpinLayouts,
            ImmutableArray<SystemLayout> systems,
            ImmutableArray<MeasureLayout> measureLayouts,
            Func<int, int, double>? staffYAt,
            ImmutableArray<Voice> voices,
            Dictionary<int, ImmutableArray<Voice>>? voicesByStaff,
            Dictionary<int, ImmutableArray<Measure>>? measuresByStaff,
            ImmutableArray<BeamLayout> beamLayouts,
            Func<int, int>? staffLinesOf = null,
            ImmutableArray<GraceNoteItem> graceNotes = default,
            Func<int, IReadOnlyDictionary<RestShiftKey, double>?>? restShiftsOf = null)
    {
        if (hairpinItems.IsDefaultOrEmpty || hairpinLayouts.IsDefaultOrEmpty)
            return (dynamicLayouts, hairpinLayouts, ImmutableArray<AlignedLineGroup>.Empty);

        var lines = BuildLines(hairpinItems, dynamicLayouts, dynamics);
        // A line with one wedge and no text is the answer HairpinEngraver already
        // computed (same span supports, same wedge outline) — leave it untouched.
        // (A run of lines — a combined staff's part going on in another voice — is one line.)
        var grouped = RunsOfLines(lines, hairpinItems).Where(run => run.Length > 1
            || run[0].DynamicLayoutIndices.Length > 0 || run[0].HairpinItemIndices.Length > 1).ToList();
        if (grouped.Count == 0)
            return (dynamicLayouts, hairpinLayouts, ImmutableArray<AlignedLineGroup>.Empty);
        var groups = ImmutableArray.CreateBuilder<AlignedLineGroup>();

        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);
        var beamMembers = DynamicEngraver.BuildBeamMembers(beamLayouts);

        // A member hairpin's broken pieces, found by the mark identity its layouts
        // carry. A harness-built item without a source index has no way back to its
        // layouts; its line is skipped (production items always carry one).
        var piecesBySource = new Dictionary<int, List<int>>();
        for (int i = 0; i < hairpinLayouts.Length; i++)
            if (hairpinLayouts[i].SourceIndex >= 0)
                (piecesBySource.TryGetValue(hairpinLayouts[i].SourceIndex, out var list)
                    ? list
                    : piecesBySource[hairpinLayouts[i].SourceIndex] = new List<int>()).Add(i);

        var dynBuilder = dynamicLayouts.IsDefaultOrEmpty ? null : dynamicLayouts.ToBuilder();
        var hpBuilder = hairpinLayouts.ToBuilder();

        // One line's own reading: its voice's columns and its members' pieces. Null where a
        // member hairpin has no way back to its layouts.
        LinePart? PartOf(DynamicLine line)
        {
            var lineVoices = voicesByStaff != null
                && voicesByStaff.TryGetValue(line.StaffIndex, out var vv) ? vv : voices;
            var staffMeasures = LayoutUtilities.ResolveStaffMeasures(
                measuresByStaff, line.StaffIndex,
                lineVoices.IsDefaultOrEmpty ? ImmutableArray<Measure>.Empty : lineVoices[0].Measures);

            // The line's voice: LilyPond's engraver lives in ONE Voice context, so all
            // members share it — the line's hairpins' (BuildLines groups by it), or, for a
            // numbered line of texts alone, its first text's. The columns the line runs over
            // are that voice's.
            int voiceIndex = line.VoiceIndex;
            if (line.HairpinItemIndices.IsDefaultOrEmpty)
                foreach (int di in line.DynamicLayoutIndices)
                {
                    int si = dynamicLayouts[di].SourceIndex;
                    if (si >= 0 && si < dynamics.Length)
                    {
                        voiceIndex = dynamics[si].VoiceIndex;
                        break;
                    }
                }
            if (!lineVoices.IsDefaultOrEmpty && voiceIndex >= lineVoices.Length)
                voiceIndex = 0;
            var lineMeasures = line.HairpinItemIndices.IsDefaultOrEmpty
                ? staffMeasures
                : HairpinEngraver.VoiceMeasures(lineVoices, line.VoiceIndex, staffMeasures);
            var lineEndMeasures = HairpinEngraver.VoiceMeasures(lineVoices, line.EndVoiceIndex, staffMeasures);

            // Wedge pieces per system, via each member item's layouts.
            var wedgePieces = new List<(int LayoutIdx, int HairpinItemIdx)>();
            bool resolvable = true;
            foreach (int hi in line.HairpinItemIndices)
            {
                int src = hairpinItems[hi].SourceIndex;
                if (src < 0 || !piecesBySource.TryGetValue(src, out var pieceIdxs))
                {
                    resolvable = false;
                    break;
                }
                foreach (int pi in pieceIdxs)
                    wedgePieces.Add((pi, hi));
            }
            if (!resolvable)
                return null;
            return new LinePart(line, lineVoices, voiceIndex, lineMeasures, lineEndMeasures, wedgePieces);
        }

        foreach (var run in grouped)
        {
            // Each line of the run reads its own voice's columns and brings its own members.
            var parts = new List<LinePart>(run.Length);
            foreach (var member in run)
                if (PartOf(member) is { } read)
                    parts.Add(read);
            if (parts.Count != run.Length)
                continue;
            var runSystems = new SortedSet<int>();
            foreach (var part in parts)
                runSystems.UnionWith(MemberSystems(part.Line, part.WedgePieces, hairpinLayouts,
                    dynamicLayouts, measureToSystem));
            var line = run[0];

            // LILYPOND-REF: lily/spanner.cc:36-144 Spanner::do_break_processing — the
            // DynamicLineSpanner breaks per system and each piece is side-positioned
            // against the supports inside it, so the level is resolved per system, not
            // once for the whole span.
            foreach (int sysIdx in runSystems)
            {
                (VerticalSkyline Up, VerticalSkyline Down)? myDim = null;
                void Fold((VerticalSkyline Up, VerticalSkyline Down) part)
                {
                    if (myDim is { } dim)
                    {
                        dim.Up.Merge(part.Up);
                        dim.Down.Merge(part.Down);
                    }
                    else
                        myDim = part;
                }

                var sysTexts = new List<int>();
                var sysWedges = new List<(int LayoutIdx, int HairpinItemIdx)>();
                foreach (var part in parts)
                {
                    foreach (int di in part.Line.DynamicLayoutIndices)
                    {
                        var d = dynamicLayouts[di];
                        if (!measureToSystem.TryGetValue(d.MeasureIndex, out int ds) || ds != sysIdx)
                            continue;
                        sysTexts.Add(di);
                        Fold(DynamicEngraver.LabelSkylines(fonts, d.Text, d.IsExpressiveText, d.X,
                            -DynamicEngraver.TextOffsetInSpanner(fonts), d.IsSubito, d.IsEditorial));
                    }
                    foreach (var (pi, hi) in part.WedgePieces)
                    {
                        var piece = hpBuilder[pi];
                        if (!measureToSystem.TryGetValue(piece.StartMeasureIndex, out int ws)
                            || ws != sysIdx)
                            continue;
                        sysWedges.Add((pi, hi));
                        Fold(HairpinEngraver.WedgeSkylines(piece.StartX, piece.EndX,
                            piece.StartOpening, piece.EndOpening, 0.0, piece.CircleX));
                    }
                }
                if (myDim is not { } my)
                    continue;

                // The line's staff's own extent floors the supports (an `as lines N` staff's
                // drawn lines), as each member's own placement read it.
                int staffLines = staffLinesOf?.Invoke(line.StaffIndex)
                    ?? EngravingDefaults.DefaultStaffLines;
                (VerticalSkyline Up, VerticalSkyline Down)? runSupport = null;
                foreach (var part in parts)
                {
                    var partSupport = DynamicEngraver.SpanSupportSkylines(
                        part.Voices, part.VoiceIndex,
                        SpanColumns(part.Line, sysIdx, part.Measures, measureLayouts, measureToSystem,
                            graceNotes, part.Voices, part.VoiceIndex, part.EndMeasures),
                        (vi, mi, ii) => beamMembers.TryGetValue((line.StaffIndex, vi, mi, ii), out var b)
                            ? b : null,
                        staffLines, restShiftsOf?.Invoke(line.StaffIndex));
                    if (runSupport is { } soFar)
                    {
                        soFar.Up.Merge(partSupport.Up);
                        soFar.Down.Merge(partSupport.Down);
                    }
                    else
                        runSupport = partSupport;
                }
                var support = runSupport!.Value;
                // The line's own side: one computation, dir its sign. The text hangs
                // TextOffsetInSpanner BELOW the spanner on either side (define-grobs.scm:1450
                // is a plain Y-offset, not a side position).
                double spannerY = DynamicEngraver.SpannerOffsetY(
                    dir: line.IsAbove ? 1.0 : -1.0, support, my, staffLines);

                foreach (int di in sysTexts)
                    dynBuilder![di] = dynBuilder[di] with
                    {
                        YUp = spannerY - DynamicEngraver.TextOffsetInSpanner(fonts),
                    };
                foreach (var (pi, hi) in sysWedges)
                {
                    // The same staff-middle → system-top conversion HairpinEngraver
                    // spends, with ITS convention for the within-system offset: the
                    // PIECE's own first measure (its system's — not the item's start,
                    // which is another system's for a piece carried over a break).
                    double staffOffset = staffYAt?.Invoke(
                        hpBuilder[pi].StartMeasureIndex, line.StaffIndex) ?? 0;
                    hpBuilder[pi] = hpBuilder[pi] with
                    {
                        YUp = spannerY - EngravingDefaults.StaffMiddle - staffOffset,
                    };
                }
                groups.Add(new AlignedLineGroup(
                    sysTexts.ToImmutableArray(),
                    sysWedges.Select(w => w.LayoutIdx).ToImmutableArray(),
                    line.IsAbove));
            }
        }

        return (dynBuilder?.ToImmutable() ?? dynamicLayouts, hpBuilder.ToImmutable(),
            groups.ToImmutable());
    }

    /// <summary>One line of a run as <see cref="AlignLines"/> reads it: its voices, the voice
    /// whose columns it runs over, that voice's measures and its end's, and its wedges' pieces.</summary>
    private readonly record struct LinePart(
        DynamicLine Line, ImmutableArray<Voice> Voices, int VoiceIndex,
        ImmutableArray<Measure> Measures, ImmutableArray<Measure> EndMeasures,
        List<(int LayoutIdx, int HairpinItemIdx)> WedgePieces);

    /// <summary>
    /// The lines in RUNS: a line alone, or the lines one part's hairpins make as a combined
    /// staff engraves the part in one voice and then another — a hairpin ended by ITS OWN
    /// part's next (<see cref="MusicMarkItem.MeetsVoice"/>), which stands in the other voice.
    /// A run is side-positioned as one line: its members' outlines against all its lines'
    /// supports, each read in its own voice.
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond's line is a Voice's (ly/engraver-init.ly:359-410), and a part
    ///   the combiner moves to another context opens another line there. A part's two hairpins
    ///   that meet tip to tip (<see cref="HairpinItem.AdjacentAtEnd"/>) are one gesture of one
    ///   player; on two lines the tips met at two heights, as much as a staff space apart (Lab
    ///   sessions/p1325/probe h1, h3). MEASURED (LilyPond 2.26.0, Lab sessions/p1324/lp s2, s3):
    ///   LilyPond keeps those bars in two voices, so there the two ARE one Voice's line, on one
    ///   baseline — the picture this gives the unison Lily# makes of them.
    ///   departs from: lily/dynamic-align-engraver.cc:194-235 stop_translation_timestep.
    ///   observed by: CombinedStaffHairpinTests.APartsHairpinsInTwoVoices_RideOneLine.
    /// A hairpin met in ANOTHER part's voice (<see cref="HairpinItem.MetInAnotherVoice"/>) is
    /// not of the run: the two are two players' lines. Nor is a next one written on the other
    /// side of the staff.
    /// ⚠️ No observer (Lab sessions/p1325/poisons.ps1 R4, R5, R6, R10, R11 stay green): the
    /// following line's staff, its voice, the next hairpin's source and its place each say what
    /// the others already do in every book written so far, and a run whose lines stand on two
    /// systems has not been written.
    /// </remarks>
    internal static List<ImmutableArray<DynamicLine>> RunsOfLines(
        ImmutableArray<DynamicLine> lines, ImmutableArray<HairpinItem> hairpinItems)
    {
        var runs = new List<ImmutableArray<DynamicLine>>(lines.Length);
        // The line each line follows on (itself: none).
        int[]? root = null;
        int RootOf(int i)
        {
            while (root![i] != i)
                i = root[i] = root[root[i]];
            return i;
        }
        for (int a = 0; a < lines.Length; a++)
            foreach (int hi in lines[a].HairpinItemIndices)
            {
                var ending = hairpinItems[hi];
                if (ending.EndVoiceIndex == ending.VoiceIndex || ending.MetInAnotherVoice
                    || ending.EndSourcePosition < 0)
                    continue;
                for (int b = 0; b < lines.Length; b++)
                {
                    if (b == a || lines[b].StaffIndex != lines[a].StaffIndex
                        || lines[b].IsAbove != lines[a].IsAbove || lines[b].VoiceIndex != ending.EndVoiceIndex)
                        continue;
                    foreach (int hj in lines[b].HairpinItemIndices)
                    {
                        var starting = hairpinItems[hj];
                        if (starting.SourcePosition != ending.EndSourcePosition
                            || starting.StartMeasureIndex != ending.EndMeasureIndex
                            || starting.StartItemIndex != ending.EndItemIndex)
                            continue;
                        root ??= Enumerable.Range(0, lines.Length).ToArray();
                        int ra = RootOf(a), rb = RootOf(b);
                        if (ra != rb)
                            root[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }
            }
        if (root == null)
        {
            foreach (var line in lines)
                runs.Add(ImmutableArray.Create(line));
            return runs;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            if (RootOf(i) != i)
                continue;
            var run = ImmutableArray.CreateBuilder<DynamicLine>();
            for (int j = i; j < lines.Length; j++)
                if (RootOf(j) == i)
                    run.Add(lines[j]);
            runs.Add(run.ToImmutable());
        }
        return runs;
    }

    /// <summary>
    /// Builds the lines: per staff and side, hairpins sorted by start moment chain while each
    /// starts no later than the running end (:210 — the set of running spanners never
    /// empties at a shared timestep), and every dynamic text of that side whose moment
    /// falls inside the chained span [start..end] (both ends inclusive: the start
    /// text and the terminating text are acknowledged while the line is alive) joins.
    /// </summary>
    /// <param name="dynamics">The score's dynamics, the layouts' sources: where they number
    /// their lines (<see cref="DynamicItem.LineId"/>), the texts of one number that no hairpin
    /// of their side joins are a line too — the line a hairpin left when the level at its
    /// start wrote the other side keeps that level and the one closing the hairpin.
    /// MEASURED (LilyPond 2.26.0, Lab sessions/p1225/dyn/lp/d3, <c>c''4_\ff\&gt; … f''\p</c>
    /// under \dynamicUp): the ff and the p stand on one baseline, 17.2776, below the staff,
    /// the hairpin between them above it.</param>
    internal static ImmutableArray<DynamicLine> BuildLines(
        ImmutableArray<HairpinItem> hairpinItems,
        ImmutableArray<DynamicLayout> dynamicLayouts,
        ImmutableArray<DynamicItem> dynamics = default)
    {
        var lines = ImmutableArray.CreateBuilder<DynamicLine>();
        HashSet<int>? chained = null;
        foreach (var staffGroup in hairpinItems
            .Select((hp, i) => (Item: hp, Index: i))
            // …and per VOICE: the engraver that keeps the line stands in one Voice, so two
            // voices' hairpins are two lines, and their moments (item indices) are not one
            // clock. Until session 1238 the staff was one stream, and a lower voice's hairpin
            // chained with the upper voice's by item number.
            // LILYPOND-REF: ly/engraver-init.ly:359-410 Dynamic_align_engraver is consisted into the Voice context
            .GroupBy(x => (Staff: x.Item.StaffIndex, Voice: x.Item.VoiceIndex, Above: x.Item.IsAbove)))
        {
            var ordered = staffGroup
                .OrderBy(x => x.Item.StartMeasureIndex)
                .ThenBy(x => x.Item.StartItemIndex)
                .ToList();
            int at = 0;
            while (at < ordered.Count)
            {
                var first = ordered[at];
                var members = ImmutableArray.CreateBuilder<int>();
                members.Add(first.Index);
                (int M, int I) start = (first.Item.StartMeasureIndex, first.Item.StartItemIndex);
                (int M, int I) end = (first.Item.EndMeasureIndex, first.Item.EndItemIndex);
                int endVoice = first.Item.EndVoiceIndex;
                at++;
                while (at < ordered.Count)
                {
                    var next = ordered[at];
                    if (Cmp((next.Item.StartMeasureIndex, next.Item.StartItemIndex), end) > 0)
                        break;
                    members.Add(next.Index);
                    var nextEnd = (next.Item.EndMeasureIndex, next.Item.EndItemIndex);
                    if (Cmp(nextEnd, end) > 0)
                    {
                        end = nextEnd;
                        endVoice = next.Item.EndVoiceIndex;
                    }
                    at++;
                }

                var texts = ImmutableArray.CreateBuilder<int>();
                for (int di = 0; di < dynamicLayouts.Length; di++)
                {
                    var d = dynamicLayouts[di];
                    if (d.IsAbove != staffGroup.Key.Above || d.IsExpressiveText
                        || d.StaffIndex != staffGroup.Key.Staff)
                        continue;
                    var moment = (d.MeasureIndex, d.ItemIndex);
                    // The line's own voice's texts inside its span — and the text of another
                    // voice that ENDED one of its hairpins (HairpinItem.EndVoiceIndex), which
                    // stands at that hairpin's end and nowhere else on this line's clock.
                    bool joins = d.VoiceIndex == staffGroup.Key.Voice
                        ? Cmp(moment, start) >= 0 && Cmp(moment, end) <= 0
                        : EndsAMember(members, hairpinItems, d);
                    if (joins)
                    {
                        texts.Add(di);
                        (chained ??= new HashSet<int>()).Add(di);
                    }
                }

                lines.Add(new DynamicLine(staffGroup.Key.Staff,
                    start.M, start.I, end.M, end.I,
                    members.ToImmutable(), texts.ToImmutable(), staffGroup.Key.Above,
                    staffGroup.Key.Voice, endVoice));
            }
        }

        // The numbered lines' texts that no chain took: two or more of one number are a line
        // with no wedge on it, from its first text's moment to its last.
        if (!dynamics.IsDefaultOrEmpty)
        {
            Dictionary<(int Staff, int Line, bool Above), List<int>>? loose = null;
            for (int di = 0; di < dynamicLayouts.Length; di++)
            {
                var d = dynamicLayouts[di];
                if (d.IsExpressiveText || d.SourceIndex < 0 || d.SourceIndex >= dynamics.Length
                    || dynamics[d.SourceIndex].LineId is not (> 0 and var id)
                    || (chained?.Contains(di) ?? false))
                    continue;
                loose ??= new();
                if (!loose.TryGetValue((d.StaffIndex, id, d.IsAbove), out var list))
                    loose[(d.StaffIndex, id, d.IsAbove)] = list = new List<int>();
                list.Add(di);
            }
            if (loose != null)
                foreach (var (key, list) in loose)
                {
                    if (list.Count < 2)
                        continue;
                    (int M, int I) first = (int.MaxValue, int.MaxValue), last = (-1, -1);
                    foreach (int di in list)
                    {
                        var moment = (dynamicLayouts[di].MeasureIndex, dynamicLayouts[di].ItemIndex);
                        if (Cmp(moment, first) < 0)
                            first = moment;
                        if (Cmp(moment, last) > 0)
                            last = moment;
                    }
                    lines.Add(new DynamicLine(key.Staff, first.M, first.I, last.M, last.I,
                        ImmutableArray<int>.Empty, list.ToImmutableArray(), key.Above));
                }
        }
        return lines.ToImmutable();
    }

    private static int Cmp((int M, int I) a, (int M, int I) b)
        => a.M != b.M ? a.M.CompareTo(b.M) : a.I.CompareTo(b.I);

    /// <summary>Whether <paramref name="text"/>, written in another voice than the line's, is
    /// the level one of the line's hairpins ended on.</summary>
    private static bool EndsAMember(ImmutableArray<int>.Builder members,
        ImmutableArray<HairpinItem> hairpinItems, DynamicLayout text)
    {
        foreach (int hi in members)
        {
            var hp = hairpinItems[hi];
            if (hp.EndVoiceIndex != hp.VoiceIndex && hp.EndVoiceIndex == text.VoiceIndex
                && hp.EndMeasureIndex == text.MeasureIndex && hp.EndItemIndex == text.ItemIndex)
                return true;
        }
        return false;
    }

    /// <summary>The systems any member of the line lands on, each visited once.</summary>
    private static IEnumerable<int> MemberSystems(DynamicLine line,
        List<(int LayoutIdx, int HairpinItemIdx)> wedgePieces,
        ImmutableArray<HairpinLayout> hairpinLayouts,
        ImmutableArray<DynamicLayout> dynamicLayouts,
        IReadOnlyDictionary<int, int> measureToSystem)
    {
        var seen = new SortedSet<int>();
        foreach (int di in line.DynamicLayoutIndices)
            if (measureToSystem.TryGetValue(dynamicLayouts[di].MeasureIndex, out int s))
                seen.Add(s);
        foreach (var (pi, _) in wedgePieces)
            if (measureToSystem.TryGetValue(hairpinLayouts[pi].StartMeasureIndex, out int s))
                seen.Add(s);
        return seen;
    }

    /// <summary>
    /// The (measure, item, column X) of every timestep the line runs over inside one
    /// system — the heads and stems LilyPond's engraver adds as support at each
    /// stop_translation_timestep while the line is alive.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/dynamic-align-engraver.cc:222-223 add_support. A GRACE
    /// column stands at its own X (<see cref="HairpinEngraver.GraceColumnX"/>), not its main
    /// note's: until session 918 a line opening at a dynamic on a grace note took the grace
    /// head's support at the next main column, and sat 0.14 too high (ledger
    /// hairpin.grace.start-at-text.*).</remarks>
    private static IEnumerable<(int Measure, int Item, double X)> SpanColumns(
        DynamicLine line, int sysIdx,
        ImmutableArray<Measure> staffMeasures, ImmutableArray<MeasureLayout> measureLayouts,
        IReadOnlyDictionary<int, int> measureToSystem,
        ImmutableArray<GraceNoteItem> graceNotes, ImmutableArray<Voice> voices, int voiceIndex,
        ImmutableArray<Measure> endMeasures)
    {
        if (staffMeasures.IsDefaultOrEmpty)
            yield break;
        // staffMeasures are the line's OWN voice's; an end another voice wrote is read back
        // into that voice's count (HairpinEngraver.OwnVoiceEndItem).
        int endItemInOwnVoice = HairpinEngraver.OwnVoiceEndItem(line.EndMeasureIndex, line.EndItemIndex,
            line.EndVoiceIndex == line.VoiceIndex, staffMeasures, endMeasures);
        for (int m = line.StartMeasureIndex;
             m <= line.EndMeasureIndex && m < measureLayouts.Length && m < staffMeasures.Length;
             m++)
        {
            if (!measureToSystem.TryGetValue(m, out int s) || s != sysIdx)
                continue;
            var layout = measureLayouts[m];
            int itemCount = staffMeasures[m].Items.Length;
            int from = m == line.StartMeasureIndex ? line.StartItemIndex : 0;
            int to = m == line.EndMeasureIndex ? endItemInOwnVoice : itemCount - 1;
            for (int i = Math.Max(0, from); i <= to && i < itemCount; i++)
                yield return (m, i,
                    HairpinEngraver.GraceColumnX(graceNotes, voices, line.StaffIndex, m, i,
                        measureLayouts, voiceIndex)
                    ?? layout.X + LayoutUtilities.GetItemXOffset(staffMeasures, m, i, layout)
                       + HairpinEngraver.CollisionShift(voices, voiceIndex, m, i));
        }
    }
}
