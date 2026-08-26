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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

internal sealed partial class LayoutEngine
{
    /// <summary>
    /// Every staff's fingerings at their ISLAND answer — staff/head clearance only. The
    /// script-column walk (<see cref="ArticulationEngraver.CalculateWithFingerings"/>)
    /// finishes them against the note's other scripts, in script-priority order.
    /// </summary>
    /// <remarks>
    /// Fingerings live on the NoteItem, so they must be read from EACH staff's own voice
    /// (<c>score.Voice</c> is only the first staff) and positioned at that staff's index —
    /// otherwise lower-staff fingerings vanish.
    /// <para>
    /// ⚠️ Until 2026-08-08 the fingering was clamped OUTSIDE the outermost articulation
    /// with a tuned gap (1.4/1.9) — a box step that was LILYSHARP-OWN, no LilyPond counterpart.
    /// LilyPond sorts the fingering INTO the column at priority 100 + position, so a bow
    /// (180) goes above it and a tenuto (−50) stays below — measured on
    /// script-stack-order1 (staccato −2.94 / tenuto −3.42 / finger −4.00 / bow −5.33).
    /// LILYPOND-REF: lily/new-fingering-engraver.cc:314-340 position_scripts.
    /// </para>
    /// </remarks>
    private ImmutableArray<FingeringLayout> ComputeFingeringIslands(
        Score? score, ImmutableArray<SystemLayout> systems,
        Dictionary<int, ImmutableArray<Voice>>? voicesByStaff,
        ImmutableArray<BeamLayout> beamLayouts = default)
    {
        if (score == null)
            return ImmutableArray<FingeringLayout>.Empty;
        if (voicesByStaff != null && voicesByStaff.Count > 0)
        {
            var fb = ImmutableArray.CreateBuilder<FingeringLayout>();
            foreach (var kv in voicesByStaff)
            {
                if (kv.Value.IsDefaultOrEmpty)
                    continue;
                var staffScore = new Score(kv.Value[0], score.TimeSignature,
                    score.KeySignature, score.Clef, score.Tempo);
                fb.AddRange(FingeringEngraver.Calculate(staffScore, systems, kv.Key, beamLayouts));
            }
            return fb.ToImmutable();
        }
        return FingeringEngraver.Calculate(score, systems, -1, beamLayouts);
    }

    /// <summary>
    /// The staff-by-staff walk order the fingering island runs in, spelled ONCE: the
    /// per-staff score whose primary voice carries the digits, at that staff's index.
    /// <see cref="ComputeFingeringIslands"/> and the memoized per-(staff, system) path
    /// both consume it, so the two cannot drift into different staff orders — which is
    /// what would break the memo's positional reassembly.
    /// </summary>
    private static List<(int StaffIndex, Score StaffScore)> FingeringStaffScores(
        Score score, Dictionary<int, ImmutableArray<Voice>>? voicesByStaff)
    {
        var list = new List<(int, Score)>();
        if (voicesByStaff != null && voicesByStaff.Count > 0)
        {
            foreach (var kv in voicesByStaff)
            {
                if (kv.Value.IsDefaultOrEmpty)
                    continue;
                list.Add((kv.Key, new Score(kv.Value[0], score.TimeSignature,
                    score.KeySignature, score.Clef, score.Tempo)));
            }
            return list;
        }
        list.Add((-1, score));
        return list;
    }

    /// <summary>
    /// The pass's fingerings and scripts, with the keystroke-crossing per-(staff, system)
    /// memo of the FINGERING half applied (<see cref="FingScriptMemo"/>). Outside the
    /// incremental session the memo is null and this is the old pair of calls, unchanged.
    /// </summary>
    /// <remarks>
    /// The memo replays a unit's digits at their COLUMN answer and keeps them out of the
    /// walk's <paramref name="articulations"/>-driven pass entirely; the articulation
    /// argument is never filtered, so that half of the call — its order, its
    /// <c>SourceIndex</c> stamps, its mover bookkeeping — is the same call it always was.
    /// See <see cref="FingScriptMemo"/> for why a unit carrying ANY script declines.
    /// </remarks>
    private (ImmutableArray<ArticulationLayout> Articulations,
             ImmutableArray<FingeringLayout> Fingerings)
        ComputeFingeringsAndScripts(
            AnnotationLayoutContext ctx,
            Score? score,
            ImmutableArray<SystemLayout> systems,
            Dictionary<int, ImmutableArray<Voice>>? voicesByStaff,
            ImmutableArray<BeamLayout> beamLayouts,
            ImmutableArray<ArticulationItem> articulations,
            ImmutableArray<MeasureLayout> ml,
            Dictionary<int, ImmutableArray<Measure>>? measuresByStaff,
            Func<int, int, double>? staffYAt,
            Dictionary<int, Staff>? staffByIndex)
    {
        var memo = ctx.FingScriptMemo;
        if (memo == null || score == null)
        {
            var islands = ComputeFingeringIslands(score, systems, voicesByStaff, beamLayouts);
            var scripts = ImmutableArray<ArticulationLayout>.Empty;
            if (score != null)
                scripts = ArticulationEngraver.CalculateWithFingerings(
                    score, articulations, ml, measuresByStaff, staffYAt, staffByIndex,
                    beamLayouts, ctx.TieLayouts, ctx.SlurLayouts, islands, out islands);
            return (scripts, islands);
        }

        // ONE fold of each whole-score table, shared by every unit's key and by every
        // unit's island call — the per-unit shape's whole risk is paying an O(score)
        // fold per system, which is the cost this memo exists to remove.
        var tips = beamLayouts.IsDefaultOrEmpty
            ? null : ArticulationEngraver.BuildBeamedStemTips(beamLayouts);
        var beamsAt = BuildBeamsByMeasure(beamLayouts);
        var slursAt = BuildVoiceZeroSlursByMeasure(ctx.SlurLayouts);
        var scriptedMeasures = new HashSet<(int, int)>();
        if (!articulations.IsDefaultOrEmpty)
            foreach (var a in articulations)
                scriptedMeasures.Add((Math.Max(a.StaffIndex, 0), a.MeasureIndex));

        // Unit plan, in the island's own emission order (staff-major, system-minor,
        // measures ascending) so the reassembled array is positionally the old one.
        var units = new List<(int Staff, int System, FingScriptMemo.UnitEntry? Hit,
                              FingScriptMemo.UnitEntry Probe, bool Eligible)>();
        var live = ImmutableArray.CreateBuilder<FingeringLayout>();
        var liveSpans = new List<(int Unit, int Start, int Length)>();

        // The island's measure set, spelled the way the whole-score path spells it:
        // LayoutUtilities.BuildMeasureLayoutMap keeps the LAST system's layout for a
        // repeated MeasureIndex, and the walk then visits the indices ASCENDING. Both
        // properties are load-bearing for the reassembly, so the plan is derived from that
        // map rather than from the systems directly — a per-system split that walked
        // systems[s].Measures would emit a repeated index twice.
        var layoutOf = new Dictionary<int, MeasureLayout>();
        var systemOf = new Dictionary<int, int>();
        for (int s = 0; s < systems.Length; s++)
            foreach (var m in systems[s].Measures)
            {
                layoutOf[m.MeasureIndex] = m;
                systemOf[m.MeasureIndex] = s;
            }
        var ascending = new List<int>(layoutOf.Keys);
        ascending.Sort();
        // Whether each system's measures form ONE ascending run. They do for any layout
        // that breaks lines at barlines; a layout that interleaved them would make a
        // (staff, system) key name two disjoint spans, so the pass declines wholesale
        // rather than memoizing a unit whose identity is not its key.
        var runSeen = new HashSet<int>();
        int lastSystem = -1;
        bool splittable = true;
        foreach (int mi in ascending)
        {
            int s = systemOf[mi];
            if (s != lastSystem)
            {
                if (!runSeen.Add(s)) { splittable = false; break; }
                lastSystem = s;
            }
        }
        if (!splittable)
        {
            var wholeIslands = ComputeFingeringIslands(score, systems, voicesByStaff, beamLayouts);
            var wholeScripts = ArticulationEngraver.CalculateWithFingerings(
                score, articulations, ml, measuresByStaff, staffYAt, staffByIndex,
                beamLayouts, ctx.TieLayouts, ctx.SlurLayouts, wholeIslands, out wholeIslands);
            return (wholeScripts, wholeIslands);
        }

        foreach (var (staffIndex, staffScore) in FingeringStaffScores(score, voicesByStaff))
        {
            int keyStaff = Math.Max(staffIndex, 0);
            var voiceMeasures = staffScore.Voice.Measures;
            var bySystem = new Dictionary<int, ImmutableArray<MeasureLayout>.Builder>();
            var systemOrder = new List<int>();
            foreach (int mi in ascending)
            {
                if (mi < 0 || mi >= voiceMeasures.Length)
                    continue;
                int s = systemOf[mi];
                if (!bySystem.TryGetValue(s, out var b))
                {
                    bySystem[s] = b = ImmutableArray.CreateBuilder<MeasureLayout>();
                    systemOrder.Add(s);
                }
                b.Add(layoutOf[mi]);
            }
            foreach (int s in systemOrder)
            {
                var layouts = bySystem[s].ToImmutable();

                bool eligible = true;
                var probe = new FingScriptMemo.UnitEntry
                {
                    MeasureIndices = new int[layouts.Length],
                    Layouts = new object[layouts.Length],
                    StaffOffsets = new double[layouts.Length],
                };
                var beams = new List<object>();
                var slurs = new List<SlurLayout>();
                for (int i = 0; i < layouts.Length; i++)
                {
                    int mi = layouts[i].MeasureIndex;
                    probe.MeasureIndices[i] = mi;
                    probe.Layouts[i] = layouts[i];
                    probe.StaffOffsets[i] = staffYAt?.Invoke(mi, keyStaff) ?? 0;
                    if (beamsAt != null && beamsAt.TryGetValue((keyStaff, mi), out var bl))
                        beams.AddRange(bl);
                    if (slursAt != null && slursAt.TryGetValue((keyStaff, mi), out var sl))
                        slurs.AddRange(sl);
                    if (scriptedMeasures.Count > 0 && scriptedMeasures.Contains((keyStaff, mi)))
                        eligible = false;
                }
                probe.Beams = beams.ToArray();
                probe.Slurs = slurs.ToArray();

                var hit = eligible ? memo.TryMatch(keyStaff, s, probe) : null;
                if (hit == null)
                {
                    var built = FingeringEngraver.CalculateWithTips(
                        staffScore, layouts, staffIndex, tips);
                    liveSpans.Add((units.Count, live.Count, built.Length));
                    live.AddRange(built);
                }
                units.Add((keyStaff, s, hit, probe, eligible));
            }
        }

        var liveIslands = live.ToImmutable();
        var articulationLayouts = ArticulationEngraver.CalculateWithFingerings(
            score, articulations, ml, measuresByStaff, staffYAt, staffByIndex,
            beamLayouts, ctx.TieLayouts, ctx.SlurLayouts, liveIslands,
            out var liveAdjusted);

        // Reassemble in unit order: a hit replays its stored digits, a miss takes its own
        // contiguous slice of the walk's answer and becomes the unit's new entry.
        var spanOf = new Dictionary<int, (int Start, int Length)>(liveSpans.Count);
        foreach (var (unit, start, length) in liveSpans)
            spanOf[unit] = (start, length);
        var fingerings = ImmutableArray.CreateBuilder<FingeringLayout>();
        for (int u = 0; u < units.Count; u++)
        {
            var unit = units[u];
            if (unit.Hit != null)
            {
                fingerings.AddRange(unit.Hit.Adjusted);
                continue;
            }
            var (start, length) = spanOf[u];
            var slice = new FingeringLayout[length];
            for (int i = 0; i < length; i++)
                slice[i] = liveAdjusted[start + i];
            fingerings.AddRange(slice);
            if (unit.Eligible)
            {
                unit.Probe.Adjusted = slice;
                memo.Store(unit.Staff, unit.System, unit.Probe);
            }
        }
        return (articulationLayouts, fingerings.ToImmutable());
    }

    /// <summary>Which beams touch each (staff, measure) — the reference set a unit's key
    /// folds, built once per pass rather than once per unit.</summary>
    private static Dictionary<(int, int), List<BeamLayout>>? BuildBeamsByMeasure(
        ImmutableArray<BeamLayout> beamLayouts)
    {
        if (beamLayouts.IsDefaultOrEmpty)
            return null;
        var map = new Dictionary<(int, int), List<BeamLayout>>();
        foreach (var beam in beamLayouts)
        {
            var group = beam.Group;
            for (int i = 0; i < group.Members.Length; i++)
            {
                int staff = !beam.MemberStaffIndices.IsDefaultOrEmpty
                    && i < beam.MemberStaffIndices.Length
                        ? beam.MemberStaffIndices[i]
                        : Math.Max(0, beam.StaffIndex);
                var key = (Math.Max(staff, 0),
                    group.Members[i].ResolveMeasureIndex(group.MeasureIndex));
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<BeamLayout>();
                // One entry per beam per measure: a group's members all name the same beam.
                if (list.Count == 0 || !ReferenceEquals(list[^1], beam))
                    list.Add(beam);
            }
        }
        return map;
    }

    /// <summary>The voice-0 slurs covering each (staff, measure) — the only ones a digit's
    /// <c>avoid-slur #'around</c> shift can read (<c>ArticulationEngraver</c>'s flush keys
    /// its lookup at voice 0 by construction).</summary>
    private static Dictionary<(int, int), List<SlurLayout>>? BuildVoiceZeroSlursByMeasure(
        ImmutableArray<SlurLayout> slurLayouts)
    {
        if (slurLayouts.IsDefaultOrEmpty)
            return null;
        var map = new Dictionary<(int, int), List<SlurLayout>>();
        foreach (var s in slurLayouts)
        {
            if (s.Slur.VoiceIndex != 0)
                continue;
            int staff = Math.Max(s.StaffIndex, 0);
            for (int m = s.Slur.StartMeasureIndex; m <= s.Slur.EndMeasureIndex; m++)
            {
                if (!map.TryGetValue((staff, m), out var list))
                    map[(staff, m)] = list = new List<SlurLayout>();
                list.Add(s);
            }
        }
        return map;
    }

    /// <summary>
    /// Voice collision offsets / head-wipes / dot-force-down for multi-voice staves
    /// (so the renderer can nudge opposing voices apart), plus opt-in part-combine
    /// layouts. Keys are (measureIndex, voiceId, itemIndex) — correct for the common
    /// single-multi-voice-staff case. Extracted verbatim from the multi-staff
    /// <c>Layout</c> body.
    /// </summary>
    private (ImmutableDictionary<VoiceItemKey, double> VoiceOffsets,
             ImmutableHashSet<VoiceItemKey> HeadWipes,
             ImmutableDictionary<VoiceItemKey, DotAdjustment> DotAdjustments,
             ImmutableArray<PartCombineLayout> PartCombine)
        CalculateVoiceCollisions(MultiStaffScore score, ImmutableArray<SystemLayout> systemsArray)
    {
        var voiceOffsetsBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, double>();
        var headWipeBuilder = ImmutableHashSet.CreateBuilder<VoiceItemKey>();
        var dotAdjustBuilder = ImmutableDictionary.CreateBuilder<VoiceItemKey, DotAdjustment>();
        var partCombineLayouts = ImmutableArray<PartCombineLayout>.Empty;
        foreach (var (group, staff, staffIndex) in score.EnumerateStaves())
        {
            if (staff.Voices.Length < 2)
                continue;

            var staffScore = new Score(
                staff.Voices, score.TimeSignature, score.KeySignature, ClefToString(staff.Clef));
            var (vo, hw, da) = _elementCoordinator.CalculateVoiceOffsets(staffScore);
            foreach (var kv in vo) voiceOffsetsBuilder[kv.Key] = kv.Value;
            foreach (var k in hw) headWipeBuilder.Add(k);
            foreach (var kv in da) dotAdjustBuilder[kv.Key] = kv.Value;
        }

        // The a2/Solo labels belong to a combinedStaff and come off the model with the
        // voices they were computed alongside (Staff.PartCombineMarks). Placement is the
        // only part of them that is layout's business.
        //
        // ⚠️ ONLY THE FIRST COMBINED STAFF'S LABELS ARE PLACED, and a score may now hold two
        // (`score s { combinedStaff { a b } combinedStaff { c d } }`). This is a gap, stated
        // rather than hidden: PartCombineLayout carries a measure index and no STAFF index,
        // and the renderer hangs every label off the SYSTEM top (DrawPartCombine), so a
        // second staff's labels have nowhere correct to go — carrying them anyway would draw
        // "Solo" for the lower pair above the upper one, which is worse than not drawing it.
        // Closing it is one field (the staff index) plus the height coming from the staff
        // instead of the system, which is the same move that would put the label on the
        // outside-staff stacker where LilyPond has it (priority 475).
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (staff.PartCombineMarks.IsDefaultOrEmpty)
                continue;
            var ml = systemsArray.SelectMany(s => s.Measures).ToImmutableArray();
            partCombineLayouts = PartCombineAnalyzer.Calculate(
                staff.PartCombineMarks, ml, staff.Voices[0].Measures);
            break;
        }
        return (voiceOffsetsBuilder.ToImmutable(), headWipeBuilder.ToImmutable(),
                dotAdjustBuilder.ToImmutable(), partCombineLayouts);
    }

    /// <summary>
    /// Common tail of both <c>Layout</c> overloads: stamp the engine options onto
    /// the built layout and, when the score carries user \override/\revert, attach
    /// a grob-property resolver.
    /// LILYPOND-REF: lily/grob-property.cc — user overrides/reverts on the layout.
    /// </summary>
    private ScoreLayout FinalizeLayout(ScoreLayout result,
        ImmutableArray<GrobOverride> grobOverrides, ImmutableArray<GrobRevert> grobReverts)
    {
        result = result with { Options = _options };
        if (!grobOverrides.IsDefaultOrEmpty || !grobReverts.IsDefaultOrEmpty)
        {
            result = result with
            {
                GrobPropertyResolver = new GrobPropertyResolver(grobOverrides, grobReverts)
            };
        }
        return result;
    }

    // Piano pedal sustain / sostenuto / una-corda marks.
    private static bool IsPedalMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOn or MusicMarkType.SustainOff
             or MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff;

    // A pedal RELEASE mark ("*"): the mixed style has no star (Ped._____| ).
    private static bool IsPedalOffMark(MusicMarkType type) =>
        type is MusicMarkType.SustainOff or MusicMarkType.SostenutoOff
             or MusicMarkType.UnaCordaOff;

    // Whether a mark's "Ped." / "*" text is still drawn under the given style.
    // LILYPOND-REF: lily/piano-pedal-engraver.cc — text keeps both, bracket keeps
    // neither, mixed keeps the leading "Ped." only.
    private static bool KeepPedalTextMark(MusicMarkItem m, PedalStyle style)
    {
        if (!IsPedalMark(m.Type)) return true;
        return style switch
        {
            PedalStyle.Text => true,
            PedalStyle.Mixed => !IsPedalOffMark(m.Type),
            _ => false,
        };
    }

    private static ScoreLayout BuildScoreLayout(
        ImmutableArray<PageLayout> pages, ImmutableArray<SystemLayout> systems,
        ImmutableArray<BeamLayout> beams, ImmutableArray<TieLayout> ties,
        ImmutableArray<SlurLayout> slurs, ImmutableArray<GlissandoLayout> glissandos,
        AnnotationLayouts a,
        ImmutableDictionary<VoiceItemKey, double> voiceOffsets,
        ImmutableHashSet<VoiceItemKey> headWipeEntries,
        ImmutableDictionary<VoiceItemKey, DotAdjustment> dotAdjustments,
        ImmutableDictionary<RestShiftKey, double> restShifts,
        ImmutableArray<PartCombineLayout> partCombineLayouts = default)
    {
        return new ScoreLayout(pages, systems, beams, ties, slurs,
            a.Dynamics, a.Articulations, a.GraceNotes,
            a.Lyrics, a.LyricHyphens, a.MusicMarks,
            a.CustomTexts, a.VoltaBrackets, a.TupletBrackets,
            a.Hairpins, a.TextSpanners, a.OttavaBrackets,
            glissandos, a.Arpeggios, a.PedalBrackets,
            a.FiguredBasses, a.ChordNames, a.PercentRepeats,
            a.CrossStaffs,
            partCombineLayouts.IsDefault ? ImmutableArray<PartCombineLayout>.Empty : partCombineLayouts,
            a.TrillSpanners,
            a.Fingerings,
            a.TieVariants,
            a.MultiMeasureRests,
            a.LedgerLineSpans,
            a.BarNumbers,
            a.StanzaNumbers,
            voiceOffsets, headWipeEntries, dotAdjustments, restShifts);
    }

    /// <summary>
    /// The indent a score with instrument names gets: LilyPond's paper default, or 0 when
    /// the score carries no name at all.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/paper-defaults-init.ly — <c>indent = 15\mm</c>. The value is
    /// LilyPond's own reading of it in staff spaces, taken from
    /// <c>(ly:output-def-lookup layout 'indent)</c> in
    /// audit/lp-geometry/probes/instrument-name-x.ly rather than converted here, because the
    /// millimetre-to-staff-space conversion is LilyPond's and reproducing it is one more thing
    /// to get subtly wrong (a derivation through 25.4/72.27 lands 3e-5 away).
    /// <para>
    /// ⚠️ IT IS NOT SIZED FROM THE NAMES, and until 2026-08-04 it was:
    /// <c>max (8.5, estimatedWidth + 1.5)</c> where <c>estimatedWidth</c> was a flat half em
    /// per Latin character and a full em per CJK one. That made the name's width a quantity
    /// with TWO spellings — this estimate, and the real metrics the text was drawn with —
    /// and the estimate erred both ways (WWWWWWW estimated 10.5 against 20.55 real; iiiiiii
    /// 10.5 against 6.69), so ordinary names were drawn over the brace. LilyPond's indent is
    /// a paper constant and a name too wide for it simply overflows to the LEFT
    /// (SharedRenderer.InstrumentNameRightEdge), which is the behaviour this restores.
    /// </para>
    /// <para>
    /// ⚠️ A SCORE WITH NO NAMES STILL GETS 0, WHICH IS NOT LILYPOND. LilyPond indents the
    /// first system by 15\mm whether or not anything is written in it. Keeping 0 is Lily#'s
    /// own choice and is left alone here on purpose: changing it moves every book in the
    /// corpus rather than the ones this island is about. Not measured against LilyPond.
    /// </para>
    /// </remarks>
    private static double CalculateIndentFromInstrumentNames(MultiStaffScore score)
    {
        const double DefaultIndent = 8.535826771653543;

        foreach (var group in score.StaffGroups)
            foreach (var staff in group.Staves)
                if (!string.IsNullOrEmpty(staff.InstrumentName))
                    return DefaultIndent;

        return 0;
    }

    internal static string ClefToString(ClefType clef) => clef switch
    {
        ClefType.Treble => "treble",
        ClefType.Bass => "bass",
        ClefType.Alto => "alto",
        ClefType.Tenor => "tenor",
        ClefType.Treble8Below => "treble_8",
        _ => "treble"
    };

    /// <summary>The tuplets belonging to one staff — used to scope beam-break
    /// boundaries so a tuplet on another staff can't split this staff's beams.</summary>
    private static ImmutableArray<TupletBracketItem> StaffTuplets(
        ImmutableArray<TupletBracketItem> all, int staffIndex)
        => all.IsDefaultOrEmpty ? all
            : all.Where(t => t.StaffIndex == staffIndex).ToImmutableArray();

    /// <summary>
    /// <paramref name="voiceScore"/> if its bracket list already addresses nothing but its own
    /// stream, otherwise the same music with the list narrowed to that stream — the input the
    /// beam detector must be given when the score it is handed holds ONE voice out of several.
    /// </summary>
    /// <remarks>
    /// The annotation quantity is the primary content staff's PRIMARY VOICE, and the score it
    /// travels in carries the WHOLE score's bracket list because the annotation pass DRAWS
    /// every bracket from it. The detector cannot tell those two apart — see
    /// <see cref="TupletBracketItem.AddressedTo"/>, where the scoping rule and its measurement
    /// live — so the detection input is built here instead of reusing the drawing one.
    /// <para>
    /// Returns the caller's own instance whenever the filter drops nothing, which is every
    /// single-staff book and every book without tuplets: that keeps the detection input
    /// identical to the staff quantity's on those books, so the two go on sharing one
    /// detection through <see cref="MultiStaffLayouter.BeamGroupsOf"/>'s input-keyed memo
    /// (session 192) instead of paying for a second walk of the same music.
    /// </para>
    /// </remarks>
    internal static Score DetectionScoreFor(
        Score voiceScore, Staff staff, MultiStaffScore score, int staffIndex)
    {
        var all = score.TupletBrackets;
        if (all.IsDefaultOrEmpty)
            return voiceScore;
        var own = TupletBracketItem.AddressedTo(all, staffIndex, voiceIndex: 0);
        if (own.Length == all.Length)
            return voiceScore;
        return new Score(
            staff.PrimaryVoice, score.TimeSignature, score.KeySignature,
            ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
            tupletBrackets: own);
    }

    /// <summary>
    /// The score a staff's ties and slurs are laid out on: every voice of a polyphonic
    /// staff, the primary-voice score itself (same instance) otherwise.
    /// </summary>
    /// <remarks>
    /// ONE house for both passes, the way the beams' construction has one
    /// (<see cref="MultiStaffLayouter.StaffBeamScoreOf"/>) and for the same reason: the
    /// preliminary pass (<see cref="RunPreliminaryAnnotationPass"/>) and the final pass
    /// (<c>LayoutAllSpanners</c>) must lay bows on the SAME quantity, and two spellings of
    /// one construction is how that stops being true. When they differed — the prelim read
    /// the primary voice only until session 140 — a voice-2 bow was drawn into a gap it was
    /// reserved nowhere in, measured as system.voice2-slur-under-notes -1.122500648 and
    /// system.voice2-tie-under-notes -0.917560328 (both landed on their single-voice twins'
    /// residuals once the passes agreed). Single-voice staves return
    /// <paramref name="primaryVoiceScore"/> itself, so their layout is byte-unchanged by
    /// whichever pass calls this.
    /// </remarks>
    private static Score StaffSpannerScoreOf(
        MultiStaffScore score, Staff staff, ImmutableArray<TupletBracketItem> staffTuplets,
        Score primaryVoiceScore)
        => staff.Voices.Length > 1
            ? new Score(
                staff.Voices, score.TimeSignature, score.KeySignature,
                ClefToString(staff.Clef), score.Tempo, score.Title, score.Composer,
                tupletBrackets: staffTuplets)
            : primaryVoiceScore;

    private sealed record AnnotationLayouts(
        ImmutableArray<DynamicLayout> Dynamics,
        ImmutableArray<ArticulationLayout> Articulations,
        ImmutableArray<GraceNoteLayout> GraceNotes,
        ImmutableArray<LyricLayout> Lyrics,
        ImmutableArray<LyricHyphenLayout> LyricHyphens,
        ImmutableArray<MusicMarkLayout> MusicMarks,
        ImmutableArray<CustomTextLayout> CustomTexts,
        ImmutableArray<VoltaBracketLayout> VoltaBrackets,
        ImmutableArray<TupletBracketLayout> TupletBrackets,
        ImmutableArray<HairpinLayout> Hairpins,
        ImmutableArray<TextSpannerLayout> TextSpanners,
        ImmutableArray<OttavaBracketLayout> OttavaBrackets,
        ImmutableArray<ArpeggioLayout> Arpeggios,
        ImmutableArray<PedalBracketLayout> PedalBrackets,
        ImmutableArray<FiguredBassLayout> FiguredBasses,
        ImmutableArray<ChordNameLayout> ChordNames,
        ImmutableArray<PercentRepeatLayout> PercentRepeats,
        ImmutableArray<CrossStaffLayout> CrossStaffs,
        ImmutableArray<TrillSpannerLayout> TrillSpanners,
        ImmutableArray<FingeringLayout> Fingerings,
        ImmutableArray<TieVariantLayout> TieVariants,
        ImmutableArray<MultiMeasureRestLayout> MultiMeasureRests,
        ImmutableArray<LedgerLineSpan> LedgerLineSpans,
        ImmutableArray<BarNumberLayout> BarNumbers,
        ImmutableArray<StanzaNumberLayout> StanzaNumbers);
}
