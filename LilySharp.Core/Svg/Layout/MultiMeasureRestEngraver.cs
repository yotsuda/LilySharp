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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a multi-measure rest spanning <c>MeasureCount</c> consecutive measures.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob
/// LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest (expand-limit . 10)
/// When MeasureCount &lt;= ExpandLimit (default 10) the LP renderer combines
/// whole/breve/long rest glyphs (church_rest); above that limit it draws an
/// H-bar (big_rest) with the count printed above. This struct carries the
/// information that a renderer needs to draw either form.
/// </remarks>
public readonly record struct MultiMeasureRestLayout(
    int StartMeasureIndex,
    int MeasureCount,
    // X coordinate of the leftmost measure's start.
    double StartX,
    // X coordinate of the rightmost measure's end.
    double EndX,
    // Within-system Y offset (device, down from the system top) of the rest's
    // vertical centre = the staff middle. The draw resolves the system-top Y-up
    // (pageHeight - system.Y) and subtracts this. NOT an absolute page Y.
    double Y,
    // True ⇒ church_rest (1..ExpandLimit), false ⇒ big_rest (H-bar).
    bool UseChurchRest);

/// <summary>
/// A run of consecutive measures that EVERY staff rests with an explicit
/// multi-measure rest, identified WITHOUT reference to the system assignment.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest-engraver.cc — process_music builds ONE
/// Multi_measure_rest spanner before spacing runs, so the run is a property of
/// the music, not of where the line happens to break.
/// </remarks>
internal readonly record struct MmrRun(int StartMeasureIndex, int Count);

/// <summary>
/// Measure-index lookup over <see cref="MmrRun"/>s: which measure OPENS a run (and
/// so carries the run's springs and its rod) and which measures are swallowed by one.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — a compressed multi-measure rest is ONE
/// spanner between two bar-line columns, so the run occupies a single column pair.
/// Collapsing the interior measures here is what reproduces that: their springs and
/// bar lines drop out (the bar lines are already suppressed for drawing in
/// SharedRenderer.Barlines), leaving the run-opening measure to carry the whole bar.
/// </remarks>
internal sealed class MmrRunMap
{
    private readonly Dictionary<int, MmrRun> _starts = new();
    private readonly HashSet<int> _interior = new();
    private readonly HashSet<int> _forbidAfter = new();

    public static readonly MmrRunMap Empty = new();

    public static MmrRunMap Build(ImmutableArray<MmrRun> runs)
    {
        var map = new MmrRunMap();
        foreach (var run in runs)
        {
            map._starts[run.StartMeasureIndex] = run;
            for (int m = run.StartMeasureIndex + 1; m < run.StartMeasureIndex + run.Count; m++)
                map._interior.Add(m);
            // Breaking AFTER measures [start, start+count-2] would fall inside the run;
            // breaking after the LAST measure of the run is where a break belongs.
            for (int m = run.StartMeasureIndex; m < run.StartMeasureIndex + run.Count - 1; m++)
                map._forbidAfter.Add(m);
        }
        return map;
    }

    private static readonly System.Runtime.CompilerServices
        .ConditionalWeakTable<MultiStaffScore, MmrRunMap> _byScore = new();

    /// <summary>
    /// The score's run map, memoized per score — the run grouping is a property of the
    /// music alone (<see cref="MultiMeasureRestEngraver.FindRuns(MultiStaffScore)"/>
    /// consults no system assignment), so ONE construction serves the break gate, the
    /// per-system layout loop and the content keys, where the layout used to rebuild
    /// the full-score walk for every system.
    /// </summary>
    public static MmrRunMap ForScore(MultiStaffScore score)
        => _byScore.GetValue(score, s => Build(MultiMeasureRestEngraver.FindRuns(s)));

    /// <summary>True when this measure is swallowed by a run that opened earlier.</summary>
    public bool IsInterior(int measureIndex) => _interior.Contains(measureIndex);

    /// <summary>True when a line break directly AFTER this measure would split a run.</summary>
    public bool ForbidsBreakAfter(int measureIndex) => _forbidAfter.Contains(measureIndex);

    /// <summary>True when a run OPENS at this measure; <paramref name="run"/> is that run.</summary>
    public bool TryGetRunStartingAt(int measureIndex, out MmrRun run)
        => _starts.TryGetValue(measureIndex, out run);
}

/// <summary>
/// Detects runs of consecutive measures that contain a single full-measure rest
/// and groups them into <see cref="MultiMeasureRestLayout"/> entries.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest-engraver.cc — process_music
/// A "full-measure rest" is detected as a measure whose only content is a
/// single <see cref="RestItem"/> with no fingering / dynamics / etc. The
/// renderer collapses such runs into a single visual MMR symbol.
/// </remarks>
internal static class MultiMeasureRestEngraver
{
    /// <summary>
    /// Threshold above which the church_rest combination is replaced by an H-bar.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest (expand-limit . 10).</remarks>
    public const int ExpandLimit = 10;

    /// <summary>
    /// Calculates MMR layouts for a single-staff score.
    /// </summary>
    public static ImmutableArray<MultiMeasureRestLayout> Calculate(
        Score score,
        ImmutableArray<SystemLayout> systems,
        double staffHeight,
        int staffIndex = -1,
        IReadOnlyList<ImmutableArray<Measure>>? allStaffMeasures = null)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<MultiMeasureRestLayout>.Empty;

        var measureMap = LayoutUtilities.BuildMeasureMap(systems);
        var voice = score.Voice;
        var builder = ImmutableArray.CreateBuilder<MultiMeasureRestLayout>();

        // Measures a staff repeats under a % sign print ONLY the sign there:
        // LilyPond's percent iterator plays the body once, so its MMR engraver
        // never sees the repeated R at all; Lily#'s unfold keeps the R for
        // playback, so the symbol pass must skip it — the same split the note
        // renderer makes (SharedRenderer's percentCovered).
        // LILYPOND-REF: lily/percent-repeat-engraver.cc.
        HashSet<(int Staff, int Measure)>? percentCovered = null;
        foreach (var pr in score.PercentRepeats)
            (percentCovered ??= new()).Add((pr.StaffIndex, pr.MeasureIndex));

        foreach (var run in FindRuns(score, allStaffMeasures))
        {
            int runLast = run.StartMeasureIndex + run.Count - 1;
            int pieceStart = run.StartMeasureIndex;

            // Spacing forbids a line break inside a run (the breaker gets
            // BreakPermission.Forbid for a run's interior), so a run normally lives
            // in ONE system and this loop runs once. It stays a safety net: one MMR
            // symbol cannot span a system boundary, so should a break ever land
            // inside, emit one symbol per system rather than stretching or dropping.
            while (pieceStart <= runLast)
            {
                if (!measureMap.TryGetValue(pieceStart, out var startInfo))
                {
                    pieceStart++;
                    continue;
                }
                var (startSystem, startMeasure) = startInfo;

                int runStart = pieceStart;
                int runEnd = pieceStart;
                while (runEnd + 1 <= runLast &&
                       measureMap.TryGetValue(runEnd + 1, out var next) &&
                       next.System.SystemIndex == startSystem.SystemIndex)
                {
                    runEnd++;
                }
                pieceStart = runEnd + 1;

                int count = runEnd - runStart + 1;
                if (!measureMap.TryGetValue(runEnd, out var endInfo))
                    continue;
                var (_, endMeasure) = endInfo;

                // Centre the rest between the INNER edges of the bounding bar lines,
                // not the outer measure box. A bar line's drawn stencil — especially a
                // repeat `:|`, whose dots reach ~1.8 ss back into the measure — must be
                // excluded, or the centre drifts toward the barline and a whole rest
                // collides with the repeat dots.
                // LILYPOND-REF: lily/multi-measure-rest.cc Multi_measure_rest::bar_width
                // — centres between Paper_column::break_align_width(col,"staff-bar")[-d],
                // i.e. each bounding bar line's INNER edge.
                double startX = startMeasure.X
                    + EngravingDefaults.BarlineDrawnWidth(voice.Measures[runStart].StartBarline);
                double endX = endMeasure.X + endMeasure.Width
                    - EngravingDefaults.BarlineDrawnWidth(voice.Measures[runEnd].EndBarline);
                // EVERY resting staff gets its own symbol: in LilyPond the engraver lives
                // in the Staff context, so a run (which only forms when all staves rest)
                // prints one Multi_measure_rest per staff, each with its own count above
                // it. Verified on 2.24.4 with a PianoStaff resting R1*4 in both staves.
                // Lily# suppresses the per-bar rest glyphs for the whole run across all
                // staves, so emitting only one symbol left every staff below the first
                // blank. LILYPOND-REF: lily/multi-measure-rest-engraver.cc (Staff context).
                foreach (int si in StaffIndicesIn(startSystem, staffIndex))
                {
                    // No symbol on a staff whose run is percent-covered (the % is the
                    // symbol). Checked over the whole piece so a run that ever merged
                    // a covered measure could not smuggle its rest back; other staves
                    // of the same measures keep their own symbols (LilyPond prints
                    // the % only on the staff that wrote the repeat).
                    if (percentCovered != null)
                    {
                        int matchStaff = si < 0 ? 0 : si;
                        bool covered = false;
                        for (int m = runStart; m <= runEnd && !covered; m++)
                            covered = percentCovered.Contains((matchStaff, m));
                        if (covered)
                            continue;
                    }

                    // Within-system Y offset (device, down from the system top) of the
                    // staff middle, NOT an absolute page Y — so it is independent of where
                    // paging places the system. The draw resolves the system-top Y-up and
                    // subtracts this, which decouples the MMR from SystemLayout.Y for the
                    // Stage-4 W2 stacking-origin flip.
                    double y = LayoutUtilities.StaffOffsetInSystemDown(startSystem, si)
                        + staffHeight / 2.0;

                    builder.Add(new MultiMeasureRestLayout(
                        StartMeasureIndex: runStart,
                        MeasureCount: count,
                        StartX: startX,
                        EndX: endX,
                        Y: y,
                        UseChurchRest: count <= ExpandLimit));
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Groups consecutive measures that EVERY staff rests with an explicit
    /// multi-measure rest into runs, WITHOUT consulting the system assignment.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/multi-measure-rest-engraver.cc — process_music creates ONE
    /// Multi_measure_rest spanner over the run BEFORE spacing runs, so the run is a
    /// property of the music. Keeping this break-independent is what lets the spring
    /// builders collapse the run to a single column pair and apply LilyPond's
    /// run-level rod (lily/multi-measure-rest.cc:341-391) instead of the old
    /// per-measure approximation — and it is the same grouping the post-break
    /// drawing pass consumes, so spacing and drawing cannot disagree.
    /// </remarks>
    internal static ImmutableArray<MmrRun> FindRuns(
        Score score,
        IReadOnlyList<ImmutableArray<Measure>>? allStaffMeasures = null)
    {
        if (score.Voices.IsDefaultOrEmpty)
            return ImmutableArray<MmrRun>.Empty;

        // A rest measure that carries a CHORD SYMBOL stays its own bar (a
        // one-bar MMR with a centred rest): merging it into a run stacked
        // every chord of the run onto the combined bar's single anchor
        // column, overprinting them. Chord ROWS live on their own row staff
        // and do not constrain the music staff.
        var chordMeasures = new HashSet<int>();
        foreach (var cn in score.ChordNames)
            if (!cn.IsChordRow)
                chordMeasures.Add(cn.MeasureIndex);

        return FindRuns(score.Voice.Measures, allStaffMeasures, chordMeasures,
            score.TimeSignature.MeasureDuration);
    }

    /// <summary>
    /// Run grouping for the multi-staff spacing path (line breaker and layouter),
    /// which must agree with the drawing pass measure for measure.
    /// </summary>
    internal static ImmutableArray<MmrRun> FindRuns(MultiStaffScore score)
    {
        var staffMeasures = new List<ImmutableArray<Measure>>();
        foreach (var group in score.StaffGroups)
            foreach (var staff in group.Staves)
                staffMeasures.Add(staff.PrimaryVoice.Measures);

        if (staffMeasures.Count == 0)
            return ImmutableArray<MmrRun>.Empty;

        var chordMeasures = new HashSet<int>();
        foreach (var cn in score.ChordNames)
            if (!cn.IsChordRow)
                chordMeasures.Add(cn.MeasureIndex);

        return FindRuns(staffMeasures[0], staffMeasures, chordMeasures,
            score.TimeSignature.MeasureDuration);
    }

    /// <summary>
    /// Run grouping from raw measures — the form the spacing path uses, where only
    /// the staves' measures and the chord-bearing measure indices are on hand.
    /// </summary>
    internal static ImmutableArray<MmrRun> FindRuns(
        ImmutableArray<Measure> primaryMeasures,
        IReadOnlyList<ImmutableArray<Measure>>? allStaffMeasures,
        IReadOnlySet<int> chordMeasures,
        Fraction initialMeasureDuration)
    {
        if (primaryMeasures.IsDefaultOrEmpty)
            return ImmutableArray<MmrRun>.Empty;

        var meters = PrevailingMeters(new[] { primaryMeasures }, primaryMeasures.Length,
            initialMeasureDuration);

        // A measure collapses into a multi-measure rest only when EVERY staff
        // rests it. LilyPond keeps the measures (and their barlines) separate when
        // another staff has content — the resting staff then shows individual
        // whole rests, not a merged MMR symbol. Verified against LilyPond 2.24
        // (single staff R1*4 → individual rests + barlines; only \compressMMRests
        // over all-resting measures merges them). LILYPOND-REF: lily/bar-engraver.cc
        // (barlines from Timing, independent of MMR) + lily/multi-measure-rest.cc.
        // Only an EXPLICIT multi-measure rest (capital `R`) collapses into a centred
        // MMR symbol. A plain lowercase `r1` that fills the measure stays an ordinary
        // Rest drawn at beat 1 (it must NOT centre, and must hang from the 4th line via
        // the normal rest renderer). LILYPOND-REF: scm/define-grobs.scm Rest vs
        // MultiMeasureRest; lily/multi-measure-rest.cc (only the MMR spanner centres).
        // A clef / key / time change LilyPond hangs on the run's opening
        // NonMusicalPaperColumn (a break-aligned grob), NOT on the rest as measure
        // content — so it does not disqualify the bar from the run: it rides the run's
        // LEFT bound. A change PART WAY through a rest sequence instead starts a fresh
        // run there (see OpensNewRun). All three behave alike; verified on 2.24.4:
        // `\clef bass R1*5` renders one "5" church rest, and `R1*2 \clef bass R1*3`
        // renders "2" then "3" — the same pair of outcomes key and time give.
        // Where the clef GLYPH sits differs from key/time (LP puts clef BEFORE the bar
        // line, key/time after — scm/define-grobs.scm:650-664 break-align-orders), but
        // that is a drawing question and does not change the run grouping.
        static bool HasLeadingBreakAlignedChange(Measure m)
            => m.Items.Length > 0 && IsBreakAlignedChange(m.Items[0]);

        // The bar rests the whole measure with an EXPLICIT multi-measure rest, optionally
        // preceded by break-aligned changes that ride the run's opening column. This is
        // what a run swallows; the leading changes stay on the run's left bound.
        static bool IsMmrMeasure(Measure m, Fraction meter)
        {
            int i = BarRestIndex(m);
            return i >= 0 && ((RestItem)m.Items[i]).Duration >= meter;
        }

        bool RestsEverywhere(int m)
        {
            if (m >= primaryMeasures.Length || !IsMmrMeasure(primaryMeasures[m], meters[m]))
                return false;
            if (allStaffMeasures != null)
                foreach (var sm in allStaffMeasures)
                    if (m >= sm.Length || !IsMmrMeasure(sm[m], meters[m]))
                        return false;
            return true;
        }

        // The bar is the FIRST measure of a written rest event (`R1` on its own, or the
        // head of an `R1*N`) rather than the 2nd..Nth measure that event expands into.
        // LILYPOND-REF: lily/multi-measure-rest-engraver.cc process_music — one
        // Multi_measure_rest spanner per written event.
        static bool StartsWrittenRest(Measure m)
        {
            int i = BarRestIndex(m);
            return i >= 0 && ((RestItem)m.Items[i]).OpensWrittenRun;
        }

        // A break-aligned change at the START of a rest bar forces a run boundary there:
        // LilyPond splits the compressed rest at the change and hangs it on the new run's
        // left bound (verified on 2.24.4 — `R1*2 \key g\major R1*3` renders "2" then "3",
        // the key sig on the between-column). A run may OPEN on such a bar, never SWALLOW
        // one. The change may sit in any staff, so a boundary in one splits the run in all.
        //
        // A NEW WRITTEN REST does the same, and for the same reason: LilyPond builds one
        // Multi_measure_rest spanner per written event, so `\compressMMRests` compresses
        // an N-measure event but never fuses separately written rests. Measured on 2.26.0
        // (audit/lpreg/pcmsh-r1.log): `R1 | R1 | R1` gives three grobs with bars=1 and NO
        // count printed, `R1*3` gives one grob with bars=3 and MMNUM "3". Without this the
        // run grouping saw only "every staff rests here" and merged the three into one.
        bool OpensNewRun(int m)
        {
            if (m < primaryMeasures.Length &&
                (HasLeadingBreakAlignedChange(primaryMeasures[m]) ||
                 StartsWrittenRest(primaryMeasures[m])))
                return true;
            if (allStaffMeasures != null)
                foreach (var sm in allStaffMeasures)
                    if (m < sm.Length &&
                        (HasLeadingBreakAlignedChange(sm[m]) || StartsWrittenRest(sm[m])))
                        return true;
            return false;
        }

        var runs = ImmutableArray.CreateBuilder<MmrRun>();
        int mi = 0;
        while (mi < primaryMeasures.Length)
        {
            if (!RestsEverywhere(mi))
            {
                mi++;
                continue;
            }

            int runStart = mi;
            int runEnd = mi;
            // A chord-bearing rest measure stays a ONE-bar MMR: it neither
            // extends into a run nor lets a run swallow it (see chordMeasures).
            // A break-aligned change opens a fresh run, so it ends the current one.
            while (runEnd + 1 < primaryMeasures.Length &&
                   RestsEverywhere(runEnd + 1) &&
                   !chordMeasures.Contains(runStart) &&
                   !chordMeasures.Contains(runEnd + 1) &&
                   !OpensNewRun(runEnd + 1))
            {
                runEnd++;
            }

            runs.Add(new MmrRun(runStart, runEnd - runStart + 1));
            mi = runEnd + 1;
        }

        return runs.ToImmutable();
    }

    /// <summary>
    /// The staves a run's symbol is drawn on. An explicit <paramref name="requested"/>
    /// index yields just that staff; the default (-1) yields every staff the system
    /// carries, or the single unindexed staff when the system has no groups — which is
    /// the single-staff path, where this keeps the previous behaviour exactly.
    /// </summary>
    private static IEnumerable<int> StaffIndicesIn(SystemLayout system, int requested)
    {
        if (requested >= 0 || system.StaffGroups.IsDefaultOrEmpty)
        {
            yield return requested;
            yield break;
        }

        bool any = false;
        foreach (var group in system.StaffGroups)
            foreach (var staff in group.Staves)
            {
                any = true;
                yield return staff.StaffIndex;
            }
        if (!any)
            yield return requested;
    }

    /// <summary>
    /// The meter in force at each bar index: the score's time signature carried forward,
    /// updated by any time change a bar itself holds (a change at the head of a bar
    /// governs that bar). A change may sit in any voice, so all of them are scanned.
    /// </summary>
    internal static Fraction[] PrevailingMeters(
        IReadOnlyList<ImmutableArray<Measure>> voices, int barCount, Fraction initial)
    {
        var meters = new Fraction[barCount];
        var meter = initial;
        for (int m = 0; m < barCount; m++)
        {
            foreach (var measures in voices)
                if (m < measures.Length)
                    foreach (var item in measures[m].Items)
                        if (item is TimeSignatureChangeItem tc)
                            meter = tc.NewTime.MeasureDuration;
            meters[m] = meter;
        }
        return meters;
    }

    /// <summary>
    /// A clef / key / time change LilyPond hangs on the run's opening NonMusicalPaperColumn,
    /// so it rides the bar rather than being its content.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:650-664 break-align-orders — the break-aligned
    /// grobs, which are placed relative to the bar line rather than to a musical column.
    /// </remarks>
    internal static bool IsBreakAlignedChange(MusicItem it)
        => it is KeySignatureChangeItem or TimeSignatureChangeItem or ClefChangeItem;

    /// <summary>
    /// The index of the bar's explicit multi-measure rest when the bar holds NOTHING BUT
    /// that rest (optionally behind break-aligned changes, which ride the opening column);
    /// -1 otherwise.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the ONE spelling of "this bar rests with a written <c>R</c>". It was
    /// written three times before being unified — twice here and once in
    /// <c>PartCombiner</c>, where the third copy skipped EVERY zero-duration item instead of
    /// only the break-aligned ones, so the two could disagree about a bar (an ottava or any
    /// other zero-duration item made one say "rested bar" and the other say "not a run
    /// bar"). Nothing read the disagreement yet, because the flag it guards is only consulted
    /// where the stricter test already holds — which is exactly the kind of containment that
    /// stops being true after an unrelated edit.
    /// <para>
    /// Deliberately does NOT compare the rest against the meter: run grouping adds that (see
    /// <c>IsMmrMeasure</c>), while callers that merely need to FIND the bar's rest run before
    /// the per-bar meters are known.
    /// </para>
    /// </remarks>
    internal static int BarRestIndex(Measure m)
    {
        int i = 0;
        while (i < m.Items.Length && IsBreakAlignedChange(m.Items[i]))
            i++;
        return i == m.Items.Length - 1
            && m.Items[i] is RestItem { IsMultiMeasure: true, IsSpacer: false }
            ? i : -1;
    }

    /// <summary>
    /// True iff the measure contains exactly one <see cref="RestItem"/> filling
    /// (or longer than) the bar — the canonical "rest the whole measure".
    /// </summary>
    /// <remarks>
    /// The bar is <paramref name="meter"/>, the prevailing time signature — NOT a whole
    /// note. A full-measure rest creates no musical column in LilyPond, and that is just
    /// as true of a 2/4 bar's half rest as of a 4/4 bar's whole rest; flooring at a whole
    /// note counted the former and dropped the latter. Use <see cref="PrevailingMeters"/>
    /// to obtain the per-bar meter.
    /// </remarks>
    internal static bool IsFullMeasureRest(Measure measure, Fraction meter)
    {
        if (measure.Items.Length != 1)
            return false;
        if (measure.Items[0] is not RestItem rest)
            return false;
        if (rest.IsSpacer)
            return false; // invisible chord-row filler — not a real rest
        return rest.Duration >= meter;
    }
}
