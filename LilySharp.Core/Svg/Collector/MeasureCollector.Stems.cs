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

using System.Collections;
using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;


// MeasureCollector's stem and beam resolution. ⚠️ HIDDEN COUPLING (review 2026-08-26
// appendix E-9): ResolveBeamStemDirections consumes _nextBeamId, and ProbeTupletBrackets
// runs a bracket-only probe walk — both live against the walk state the main part owns.
public sealed partial class MeasureCollector
{
    /// <summary>
    /// Bakes the VOICE-forced stem directions into a polyphonic staff's items
    /// (voice 1 up, voice 2 down, …), for the same reason
    /// <see cref="ResolveBeamStemDirections"/> bakes the beam-resolved ones:
    /// LilyPond's <c>\voiceOne</c>/<c>\voiceTwo</c> set Stem.direction in the
    /// engravers, BEFORE spacing runs, so everything downstream must see the
    /// direction that actually gets printed.
    /// </summary>
    /// <remarks>
    /// The renderer already forces these when it draws — SharedRenderer.cs
    /// <c>forcedStemUp ?? note.StemUp</c>, with <c>forcedStemUp</c> from
    /// <see cref="VoiceDefaults.GetDefaultStemUp"/> — but nothing wrote them back
    /// into the model, so an UNBEAMED note in a second voice reached the spacing
    /// engine claiming its pitch-derived direction while the renderer drew the
    /// opposite one. (Beamed notes were already correct, because a beam bakes its
    /// own direction into the same slot.) That broke the stem-direction spacing
    /// corrections in exactly the polyphonic case they exist for: measured against
    /// LilyPond 2.24.4, merging the per-voice wishes with pitch-derived directions
    /// moved a bar's last-column → bar-line distance the WRONG WAY (+0.036 where
    /// LilyPond has −0.100 relative to the same bar set monophonically).
    ///
    /// Applied last, over the beam-resolved directions, to match the renderer's
    /// precedence — but NOT over a direction the writer asked for
    /// (<see cref="NoteItem.ForcedStemUp"/>). In LilyPond only the <c>\\</c> sub-lists
    /// are voicified, so music before the construct in the same measure — which this
    /// measure-granular span cannot tell apart — never receives the voice props at all,
    /// and an explicit <c>\stemDown</c> inside a block is a later property set that
    /// beats <c>\voiceOne</c>'s. Either way the writer's ask survives.
    /// Voices 5+ get <c>null</c> from GetDefaultStemUp and keep their own.
    ///
    /// Only INSIDE the span, though — see <see cref="VoiceDefaults.IsPolyphonicAt"/>.
    /// LilyPond's <c>\\</c> gives each block its own Voice context, so the forcing
    /// dies with the span; baking it across the whole part instead pinned the stems
    /// of monophonic sections that merely shared a part with one <c>voice { }</c>.
    /// LILYPOND-REF: scm/music-functions.scm:1042-1057 voicify-sublist / make-voice-props-set
    /// </remarks>
    /// <remarks>
    /// ⚠️ Also called from <see cref="RenderSpec.ToStaffGroups"/> for a
    /// <c>condensedStaff</c>, whose voices come from SEPARATE parts and so are only a
    /// polyphonic staff once they have been put together. Applying the voice props is the
    /// staff's business, not the part's, and running it per part left the rests of a
    /// condensed staff with no direction at all — both parts' whole rests landed on the
    /// centre line, one on top of the other, where LilyPond's <c>\voiceOne</c>/<c>\voiceTwo</c>
    /// control puts them at ±4 (measured: audit/lpreg/pcsil-a-cond.lys against pcsil-ctl.ly).
    /// The stems were right on their own, because the renderer re-derives THOSE from the
    /// voice index; it is the rests that read the stamp.
    /// ⚠️ NOT for a <c>combinedStaff</c>: the combiner has already decided each item's
    /// direction, and LilyPond's shared and solo contexts carry no voice settings at all.
    /// </remarks>
    internal static ImmutableArray<Voice> ResolveVoiceStemDirections(ImmutableArray<Voice> voices)
    {
        if (voices.Length <= 1)
            return voices;

        // Duration already includes dots (the instance GetItemDuration's rule; this
        // walk is static, so the three-arm switch is restated here).
        static Fraction ItemSoundingDuration(MusicItem item) => item switch
        {
            NoteItem note => note.Duration,
            RestItem rest => rest.Duration,
            ChordItem chord => chord.Duration,
            _ => Fraction.Zero,
        };

        static Measure WithItems(Measure measure, ImmutableArray<MusicItem> items) =>
            new Measure(
                items,
                measure.StartBarline, measure.EndBarline, measure.SectionLabel,
                measure.SourceStart, measure.SourceEnd,
                hasBreakAfter: measure.HasBreakAfter,
                lineBreakPermission: measure.LineBreakPermission,
                breakPenalty: measure.BreakPenalty,
                pageBreakPermission: measure.PageBreakPermission,
                pageTurnPermission: measure.PageTurnPermission,
                sectionLabelPosition: measure.SectionLabelPosition,
                isPickup: measure.IsPickup,
                unmetered: measure.Unmetered,
                breaksMidBar: measure.BreaksMidBar,
                continuesBar: measure.ContinuesBar,
                unmeteredPosition: measure.UnmeteredPosition);

        var rebuilt = voices.ToBuilder();
        for (int vi = 0; vi < voices.Length; vi++)
        {
            if (VoiceDefaults.GetDefaultStemUp(vi + 1) is not { } forced)
                continue;

            var measures = voices[vi].Measures.ToBuilder();
            bool changed = false;
            // The beams whose members this pass turns round — their pure tips are baked
            // again below, at the direction they now carry.
            var turnedBeams = new HashSet<int>();
            for (int mi = 0; mi < measures.Count; mi++)
            {
                if (!VoiceDefaults.IsPolyphonicAt(voices, mi))
                    continue;

                var measure = measures[mi];
                var items = measure.Items.ToBuilder();
                bool measureChanged = false;
                // The span's reach WITHIN the measure: the primary voice's stream keeps
                // flowing after the span closes (`voice { fis2. } { e2. } r4`), and the
                // music after it is back in the surrounding context — LilyPond leaves it
                // unforced (probe vrest-probe.ly: the trailing r2 sits on the MIDDLE
                // line while the span's own rest takes the voiced +4, spacer partner or
                // not). The extra voices' tracks hold nothing but span content, so the
                // span is over where their content ends. ⚠️ An approximation with the
                // same named reach as the measure-granular one above: a first block
                // LONGER than every later one (`voice { fis2. r8 } { e2. } …`) stops
                // forcing where the later blocks stop, where LilyPond's \voiceOne holds
                // to the end of its own block. Carrying the span's extent on the model
                // is what closing that would take; the corpus binds only the trailing
                // case (collision-harmonic-no-dots.ly).
                var spanEnd = Fraction.Zero;
                if (vi == 0)
                {
                    for (int ov = 1; ov < voices.Length; ov++)
                    {
                        if (mi >= voices[ov].Measures.Length)
                            continue;
                        var covered = Fraction.Zero;
                        foreach (var it in voices[ov].Measures[mi].Items)
                            covered += ItemSoundingDuration(it);
                        if (covered > spanEnd)
                            spanEnd = covered;
                    }
                }
                var onset = Fraction.Zero;
                for (int ii = 0; ii < items.Count; ii++)
                {
                    var itemOnset = onset;
                    onset += ItemSoundingDuration(items[ii]);
                    if (vi == 0 && itemOnset >= spanEnd)
                        continue;
                    // The same voice-props distribution reaches RESTS: LilyPond's
                    // make-voice-props-set puts direction on every
                    // direction-polyphonic-grob, and Rest is in that list — the
                    // spacing reads it as the rest's pure voiced position.
                    // LILYPOND-REF: scm/music-functions.scm:666-674 make-voice-props-set
                    int restDir = forced ? 1 : -1;
                    MusicItem? updated = items[ii] switch
                    {
                        NoteItem n when n.ForcedStemUp is null && n.StemUpOverride != forced
                            => n with { StemUpOverride = forced },
                        ChordItem c when c.ForcedStemUp is null && c.StemUpOverride != forced
                            => c with { StemUpOverride = forced },
                        RestItem { IsSpacer: false, IsMultiMeasure: false } r
                                when r.VoiceDirection != restDir
                            => r with { VoiceDirection = restDir },
                        _ => null,
                    };
                    if (updated == null)
                        continue;
                    switch (updated)
                    {
                        case NoteItem { BeamId: { } nb }: turnedBeams.Add(nb); break;
                        case ChordItem { BeamId: { } cb }: turnedBeams.Add(cb); break;
                    }
                    items[ii] = updated;
                    measureChanged = true;
                }
                if (!measureChanged)
                    continue;

                measures[mi] = WithItems(measure, items.ToImmutable());
                changed = true;
            }

            if (turnedBeams.Count > 0)
                RebakePureBeamedTips(measures, turnedBeams, forced, WithItems);

            if (changed)
                rebuilt[vi] = voices[vi] with { Measures = measures.ToImmutable() };
        }
        return rebuilt.ToImmutable();
    }

    /// <summary>
    /// Bakes <see cref="NoteItem.PureBeamedStemTip"/> again for the beams the voice props
    /// just turned round, from the direction their stems now carry.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveBeamStemDirections"/> baked every member's tip at the direction the
    /// beam took from its pitches; <see cref="ResolveVoiceStemDirections"/> then wrote the
    /// voice's direction over the members and left the tip where it was, so a beam that
    /// pitches would hang DOWN and <c>\voiceOne</c> turns UP kept a down tip under an up stem
    /// — its whole pure band on the wrong side of the head. MEASURED (2.26.0,
    /// test/beam-over-stem = scratch/p361/lp/bos.lys): voice 1's <c>b8 b</c> on the middle line
    /// read a band of (−6.75 .. 0.37) staff positions, and the stem correction into the bar
    /// line came out 0.1562 where the up band (0.37 .. 7) gives LilyPond's 0.1296 — the
    /// +0.026 every bar of that book carried; the same band is the stem's spacing box
    /// (ItemSkylineFactory.AddStem), drawn below the head instead of above it.
    /// LilyPond's pure height is read off the stem's ACTUAL direction — the calc_beam branch
    /// unites the heights of the beam's stems whose direction equals this stem's — so the tip
    /// is the extreme of the same-direction members' unbeamed bands at THAT direction, the
    /// bake ResolveBeamStemDirections makes, repeated with the forced direction.
    /// LILYPOND-REF: lily/stem.cc:399-418 Stem::internal_pure_height — <c>dir =
    ///   get_grob_direction (me)</c>; <c>get_grob_direction (normal_stems[i]) == dir</c>.
    /// LILYPOND-REF: lily/stem.cc:449-458 Stem::cache_pure_height.
    /// </remarks>
    private static void RebakePureBeamedTips(
        ImmutableArray<Measure>.Builder measures, HashSet<int> beams, bool forced,
        Func<Measure, ImmutableArray<MusicItem>, Measure> withItems)
    {
        static int? BeamOf(MusicItem item) => item switch
        {
            NoteItem n => n.BeamId,
            ChordItem c => c.BeamId,
            _ => null,
        };
        static bool StemUpOf(MusicItem item) => item switch
        {
            NoteItem n => n.StemUp,
            ChordItem c => c.StemUp,
            _ => false,
        };
        // The UNBEAMED band is read with the baked tip cleared — the tip is what this pass
        // is about to write, and StemSpacingInfo would otherwise hand it straight back.
        static MusicItem Bare(MusicItem item) => item switch
        {
            NoteItem n => n with { PureBeamedStemTip = null },
            ChordItem c => c with { PureBeamedStemTip = null },
            _ => item,
        };

        var tips = new Dictionary<int, double>();
        foreach (var measure in measures)
            foreach (var item in measure.Items)
            {
                if (BeamOf(item) is not { } id || !beams.Contains(id) || StemUpOf(item) != forced)
                    continue;
                if (Layout.SpacingRules.StemSpacingInfo(Bare(item), forced) is not { } band)
                    continue;
                double tip = forced ? band.StemMax : band.StemMin;
                tips[id] = tips.TryGetValue(id, out var seen)
                    ? (forced ? Math.Max(seen, tip) : Math.Min(seen, tip))
                    : tip;
            }

        for (int mi = 0; mi < measures.Count; mi++)
        {
            var measure = measures[mi];
            ImmutableArray<MusicItem>.Builder? items = null;
            for (int ii = 0; ii < measure.Items.Length; ii++)
            {
                var item = measure.Items[ii];
                if (BeamOf(item) is not { } id || !tips.TryGetValue(id, out var tip)
                    || StemUpOf(item) != forced)
                    continue;
                MusicItem? updated = item switch
                {
                    NoteItem n when n.PureBeamedStemTip != tip => n with { PureBeamedStemTip = tip },
                    ChordItem c when c.PureBeamedStemTip != tip => c with { PureBeamedStemTip = tip },
                    _ => null,
                };
                if (updated == null)
                    continue;
                items ??= measure.Items.ToBuilder();
                items[ii] = updated;
            }
            if (items != null)
                measures[mi] = withItems(measure, items.ToImmutable());
        }
    }

    /// <summary>
    /// Bakes beam-resolved stem directions into the collected items, IN
    /// PLACE. A beam forces one direction onto all members, and LilyPond
    /// resolves directions in the engravers BEFORE spacing — skyline rods and
    /// stem-direction corrections must see the same stems the renderer draws,
    /// or beamed runs space differently from LilyPond (this showed up as
    /// down-natural 8ths inside an up-beam getting ~8% extra room).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc Beam::calc_direction — beam direction wins.
    /// LILYPOND-REF: lily/beam.cc:894-982 consider_auto_knees — per-member
    /// directions for kneed beams (BeamMember.MemberStemUp).
    /// </remarks>
    /// <summary>
    /// The brackets addressed to the stream <see cref="ResolveBeamStemDirections"/> is about
    /// to probe — the ones this collector stamped with the staff and voice it is collecting
    /// RIGHT NOW.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE COLLECTOR COLLECTS EVERY STAFF (the voice-binding loop bumps
    /// <c>_cursor.StaffIndex</c>), and <c>_tupletBrackets</c> is never cleared, so by the time
    /// staff N is probed the list also holds staves 0..N−1's brackets — and, inside a
    /// <c>&lt;&lt; \\ &gt;&gt;</c> span, the sibling voices'. Handed unfiltered, the detector reads
    /// every one of them as an index into THIS stream's items:
    /// <c>BeamDetector.BuildTupletSpans</c> drops only the out-of-range ones and says so, and
    /// says the in-range collision "closes only when the probe filters by staff/voice". This
    /// is that filter, spelt once in <see cref="TupletBracketItem.AddressedTo"/> — the
    /// annotation quantity's caller (<c>LayoutEngine.DetectionScoreFor</c>) has the same
    /// problem and calls the same home. The list is empty in the overwhelming majority of
    /// books, so the common path allocates nothing.
    /// </remarks>
    private ImmutableArray<TupletBracketItem> ProbeTupletBrackets()
        => TupletBracketItem.AddressedTo(
            _tupletBrackets, _cursor.StaffIndex, _cursor.VoiceIndex);

    private void ResolveBeamStemDirections(List<Measure> measures)
    {
        if (measures.Count == 0)
            return;

        var voice = new Voice("beam-direction-probe", measures.ToImmutableArray());
        var groups = new BeamDetector().DetectBeamGroups(
            voice, new TimeSignature(_meta.TimeBeats, _meta.TimeBeatType, _meta.TimeBeatsText, _meta.TimeSenzaMisura),
            ProbeTupletBrackets(),
            memo: BeamMemo);

        // ⚠️ ONE REBUILD PER MEASURE, NOT ONE PER STAMP. Every bake below used to write
        // through measure.Items.SetItem(...) and new Measure(...), so a measure with eight
        // beamed notes was rebuilt eight times — a fresh item array and a fresh Measure per
        // stamp — and then again for the pure-tip pass. MEASURED (session 191, keystroke
        // allocation): this method was 5.8 MB of perf-plain1k's 8.5 MB collect, and plain1k
        // is beamed eighths where perf-fingstack1k (unbeamed quarters) pays 0.
        // The stamps now land in a per-measure working array and each touched measure is
        // rebuilt once, after every group has had its say.
        // ⚠️ READS MUST SEE THE STAMPS ALREADY MADE — the pure-tip pass reads the directions
        // the first pass wrote — which is why reads go through ItemAt rather than through
        // measures, and why the commit happens after the loop and not inside it.
        var work = new MusicItem[]?[measures.Count];
        MusicItem[] Work(int mi) => work[mi] ??= measures[mi].Items.ToArray();
        int ItemCount(int mi) => work[mi]?.Length ?? measures[mi].Items.Length;
        MusicItem ItemAt(int mi, int i) => work[mi] is { } w ? w[i] : measures[mi].Items[i];

        // ⚠️ ONE REBUILD PER NOTE, NOT ONE PER STAMP — the same rule as the measures above,
        // one level down. The direction/BeamId stamp and the pure-tip stamp used to be two
        // passes each writing a fresh item, so every beamed note was built TWICE (MEASURED,
        // session 192: 1.43 MB + 1.57 MB of a 47.7 MB perf-plain1k keystroke). The tip needs
        // the band at the direction the group is about to give the stem, and that is the ONLY
        // thing the first pass's item was for — so the band is read with the direction passed
        // EXPLICITLY (StemSpacingInfo's stemUpOverride) and both stamps land in one `with`.
        // Reused across groups so the band list is one allocation, not one per beam.
        var memberBands = new List<(int Mi, int ItemIndex, bool StemUp, bool HasBand)>();
        foreach (var group in groups)
        {
            // One identity per BeamGroup, stamped on every member — the stand-in for the
            // Beam grob pointer two stems are compared through. Running across calls so
            // the voices of one staff never collide.
            int beamId = _nextBeamId++;

            // The PURE beamed stem tip: the extreme of the group's same-direction members'
            // UNBEAMED stem tips. Spacing runs before any beam is quanted, so LilyPond
            // prices a beamed stem by its PURE height — the calc_beam branch unites the
            // same-direction members' unbeamed heights and clips the non-stem side back to
            // the stem's own, so the whole result is the own head-side end plus this one
            // shared tip. LilyPond caches the answer per stem; this bake is that cache.
            // The cross-staff coords term (:421-436) is identically zero here: a Lily#
            // beam group never spans staves, so every member's pure Y refpoint is the
            // same and the per-member adjustment vanishes.
            // LILYPOND-REF: lily/stem.cc:387-447 Stem::internal_pure_height — :399-444
            //   the calc_beam branch; :443 iv.intersect (overshoot).
            // LILYPOND-REF: lily/stem.cc:449-458 Stem::cache_pure_height.
            double upTip = double.NegativeInfinity, downTip = double.PositiveInfinity;
            memberBands.Clear();
            foreach (var member in group.Members)
            {
                int mi = member.MeasureIndex >= 0 ? member.MeasureIndex : group.MeasureIndex;
                if (mi < 0 || mi >= measures.Count
                    || member.ItemIndex < 0 || member.ItemIndex >= ItemCount(mi))
                    continue;
                // The direction is the one about to be stamped, and PureBeamedStemTip is
                // still unset, so this reads the UNBEAMED band — the same two facts the
                // two-pass shape arranged by stamping first.
                // A stemless member (a whole note) has no band; it still takes the group's
                // direction and BeamId below, which is what the unconditional first pass
                // gave it before the tip pass filtered it out.
                if (Layout.SpacingRules.StemSpacingInfo(
                        ItemAt(mi, member.ItemIndex), member.MemberStemUp) is not { } info)
                {
                    memberBands.Add((mi, member.ItemIndex, member.MemberStemUp, false));
                    continue;
                }
                if (info.StemUp)
                    upTip = Math.Max(upTip, info.StemMax);
                else
                    downTip = Math.Min(downTip, info.StemMin);
                memberBands.Add((mi, member.ItemIndex, info.StemUp, true));
            }

            foreach (var (mi, itemIndex, stemUp, hasBand) in memberBands)
            {
                double tip = stemUp ? upTip : downTip;
                double? bakedTip = hasBand && !double.IsInfinity(tip) ? tip : null;

                MusicItem? updated = ItemAt(mi, itemIndex) switch
                {
                    NoteItem n => n with
                    {
                        StemUpOverride = stemUp, BeamId = beamId,
                        PureBeamedStemTip = bakedTip ?? n.PureBeamedStemTip,
                    },
                    ChordItem c => c with
                    {
                        StemUpOverride = stemUp, BeamId = beamId,
                        PureBeamedStemTip = bakedTip ?? c.PureBeamedStemTip,
                    },
                    _ => null,
                };
                if (updated == null)
                    continue;

                Work(mi)[itemIndex] = updated;
            }
            // Bake the PURE beam-push estimate into every rest this manual beam runs
            // over, so horizontal spacing sees the rest roughly where the beam will
            // put it — spacing runs before any beam is quanted; the print later uses
            // the real collision shift (ElementCoordinator.CalculateRestShifts).
            // LILYPOND-REF: lily/beam.cc:1421-1494 Beam::pure_rest_collision_callback.
            int beamDir = group.StemUp ? 1 : -1;
            foreach (var restStem in group.RestStems)
            {
                int mi = restStem.MeasureIndex >= 0 ? restStem.MeasureIndex : group.MeasureIndex;
                if (mi < 0 || mi >= measures.Count)
                    continue;
                if (restStem.ItemIndex < 0 || restStem.ItemIndex >= ItemCount(mi)
                    || ItemAt(mi, restStem.ItemIndex) is not RestItem restItem)
                    continue;

                // beam.cc:1443-1469 left/right are the nearest stems WITH HEADS — other
                // rests are not in my_stems, so these are the flanking visible members.
                // ⚠️ A rest at the beam's END has only ONE neighbour, and LilyPond uses it
                // for BOTH sides (:1461-1464 `left = right = my_stems[1]' at the head,
                // `my_stems[idx - 1]' at the tail). Indexing blindly threw here the moment
                // a bracketed rest could bound a beam (2026-09-07, `r8[ c c c]').
                var left = group.Members[Math.Max(0, restStem.BeforeMember - 1)];
                var right = group.Members[Math.Min(group.Members.Length - 1, restStem.BeforeMember)];
                if (restStem.BeforeMember == 0) left = right;
                else if (restStem.BeforeMember >= group.Members.Length) right = left;

                // beam.cc:1471-1478 the closest beam is estimated four staff positions
                // past the neighbouring heads' beam-side average, and never crosses the
                // staff centre by more than two positions.
                double beamPos = ((beamDir > 0 ? left.HeadPositionMax : left.HeadPositionMin)
                        + (beamDir > 0 ? right.HeadPositionMax : right.HeadPositionMin)) / 2.0
                    + 4.0 * beamDir;
                beamPos = Math.Max(-2.0, beamPos * beamDir) * beamDir;

                // beam.cc:1480-1491 offset = beam_pos·ss/2 − minimum_distance·dir −
                // extent[dir], floored to whole staff spaces, only ever away from the
                // beam (a semibreve's default origin hangs one space up, rest.cc:101-121).
                var restBox = Layout.GlyphMetrics.GetRestBBox(restStem.NoteValue);
                double restExtentAtDir = beamDir > 0 ? restBox.Top : restBox.Bottom;
                double offsetSs = beamPos / 2.0
                    - EngravingDefaults.RestMinimumDistance * beamDir - restExtentAtDir;
                double previousSs = restStem.NoteValue == 1 ? 1.0 : 0.0;
                double shiftSs =
                    Math.Floor(Math.Min(0.0, (offsetSs - previousSs) * beamDir)) * beamDir;
                if (shiftSs == 0.0)
                    continue;

                Work(mi)[restStem.ItemIndex] = restItem with { PureBeamShift = shiftSs * 2.0 };
            }
        }

        // Commit: every measure a stamp landed in is rebuilt exactly once, with the whole
        // set of stamps it accumulated. Untouched measures keep their original instance.
        for (int mi = 0; mi < work.Length; mi++)
        {
            if (work[mi] is not { } items)
                continue;
            var m = measures[mi];
            measures[mi] = new Measure(
                ImmutableArray.Create(items),
                m.StartBarline, m.EndBarline, m.SectionLabel,
                m.SourceStart, m.SourceEnd,
                hasBreakAfter: m.HasBreakAfter,
                lineBreakPermission: m.LineBreakPermission,
                breakPenalty: m.BreakPenalty,
                pageBreakPermission: m.PageBreakPermission,
                pageTurnPermission: m.PageTurnPermission,
                sectionLabelPosition: m.SectionLabelPosition,
                isPickup: m.IsPickup,
                unmetered: m.Unmetered,
                breaksMidBar: m.BreaksMidBar,
                continuesBar: m.ContinuesBar,
                unmeteredPosition: m.UnmeteredPosition);
        }
    }

}
