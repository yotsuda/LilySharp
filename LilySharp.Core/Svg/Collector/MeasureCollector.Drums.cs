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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

// A drum chart, `drums { hats  back  kick }` (owner's design 2026-10-11): its lines are
// written as a drummer thinks — a hi-hat pattern, a backbeat, a kick — and drawn as a
// drummer reads, in TWO voices: the hands' hits merged into chords on stems up, the feet's on
// stems down. The merge happens here, before the builder, because every side table the walk
// fills (marks, dynamics, tuplet brackets) is keyed by the item's index in its stream and
// cannot be re-keyed after the fact.
public sealed partial class MeasureCollector
{
    /// <summary>One hit of a chart line: when it sounds (from the chart's start), how long its
    /// line held it, the tuplet run it was written in (-1: none), and which voice it joins.</summary>
    private readonly record struct DrumHit(Fraction Onset, Fraction Length, int Run, string Drum, SyntaxNode Node, bool Down,
        GraceExpressionSyntax? Grace = null, DrumNoteSyntax? Member = null)
    {
        /// <summary>Where this hit's head was written: a chord member's own name, else the item.</summary>
        public int HeadSource => Member?.SourceStart ?? Node.SourceStart;
    }

    /// <summary>A <c>tuplet</c> written in a chart line: the time it covers and its ratio.</summary>
    private sealed record DrumTupletRun(Fraction Start, Fraction End, Fraction Scale, int Ratio, int Base, int SourceStart);

    /// <summary>One item of a merged voice: a chord of hits (or one), or a rest when
    /// <see cref="Hits"/> is null, at a written value; <see cref="Run"/> is the tuplet it
    /// stands in.</summary>
    private sealed record DrumPiece(int Value, int Dots, List<DrumHit>? Hits, DrumTupletRun? Run, int SourceStart)
    {
        /// <summary>A rest of a line that writes something on it (<c>r4@todo("…")</c>,
        /// <c>r@fermata</c>): the merge rebuilds the silences, so its marks ride the piece
        /// standing at its moment.</summary>
        public RestSyntax? MarkedRest { get; init; }
    }

    /// <summary>A chart's two voices; <see cref="Lower"/> is empty when the chart has one.</summary>
    private sealed record DrumPlan(List<DrumPiece> Upper, List<DrumPiece> Lower);

    private Dictionary<(ParallelExpressionSyntax, Fraction, Fraction, Fraction, int), DrumPlan?>? _drumPlans;

    /// <summary>
    /// The chart's merged voices for an occurrence opening <paramref name="offsetInBar"/> into a
    /// bar of <paramref name="barLength"/> with the note value <paramref name="value"/> in force —
    /// or null when the lines hold something the merge does not take (a pitched note, a nested
    /// tuplet, two tuplets crossing in one voice, a grace…), and the chart is drawn as it is
    /// written instead: a voice a line, which is what a <c>voice { } { }</c> span would draw.
    /// Deterministic in its key, so the primary walk and <see cref="BuildExtraVoiceTracks"/>
    /// read the same answer.
    /// </summary>
    private DrumPlan? DrumPlanOf(ParallelExpressionSyntax chart, Fraction offsetInBar, Fraction barLength,
        Fraction value, int dots)
    {
        _drumPlans ??= new();
        var key = (chart, offsetInBar, barLength, value, dots);
        if (!_drumPlans.TryGetValue(key, out var plan))
            _drumPlans[key] = plan = PlanDrumChart(chart, offsetInBar, barLength, value, dots);
        return plan;
    }

    /// <summary>How many voices a span puts on the staff: a merged chart one or two, any other
    /// span one a block.</summary>
    private int VoiceCountOf(ParallelExpressionSyntax parallel, Fraction startOffset, Fraction spanDuration,
        int spanDots, MetadataState spanMeta)
    {
        if (parallel.IsDrums && DrumPlanOf(parallel, startOffset, BarLengthOf(spanMeta), spanDuration, spanDots) is { } plan)
            return plan.Lower.Count > 0 ? 2 : 1;
        return parallel.Voices.Count();
    }

    private static Fraction BarLengthOf(MetadataState meta)
        => meta.TimeBeats > 0 && meta.TimeBeatType > 0 && !meta.TimeSenzaMisura
            ? new Fraction(meta.TimeBeats, meta.TimeBeatType)
            : Fraction.Zero;

    /// <summary>
    /// LILYSHARP-OWN (owner's design 2026-10-11): LilyPond has no drum chart — its drum music
    /// is written in the voices it is drawn in. This merge is how Lily# draws lines written apart
    /// in the two voices a drummer reads: by limb, hits sharing an onset as one chord, each held
    /// to what comes next, the rests in as few values as fit (not yet split at the beat).
    /// </summary>
    private DrumPlan? PlanDrumChart(ParallelExpressionSyntax chart, Fraction offsetInBar, Fraction barLength,
        Fraction value, int dots)
    {
        var hits = new List<DrumHit>();
        var runs = new List<DrumTupletRun>();
        var restMarks = new List<(Fraction Onset, RestSyntax Node, bool Down)>();
        Fraction length = Fraction.Zero;
        foreach (var (line, down) in chart.DrumLines)
        {
            int before = hits.Count;
            var reader = new DrumLineReader(this, down, hits, runs) { Value = value, Dots = dots };
            if (!reader.Read(GatherVoiceMusicNodes(line), new Fraction(1, 1), -1))
                return null;
            if (reader.Time > length)
                length = reader.Time;
            // A marked rest stands on its line's side: the line's word, else its first hit's.
            bool lineDown = down ?? (hits.Count > before && hits[before].Down);
            foreach (var (onset, rest) in reader.MarkedRests)
                restMarks.Add((onset, rest, lineDown));
        }

        var upper = hits.Where(h => !h.Down).ToList();
        var lower = hits.Where(h => h.Down).ToList();
        bool upperIsDown = false;
        // A chart of feet alone (a kick line) is one voice, drawn where it lies.
        if (upper.Count == 0)
        {
            (upper, lower) = (lower, upper);
            upperIsDown = true;
        }
        // A marked rest on a side with no voice of its own (a kick-only chart's hands)
        // joins the voice there is.
        var upperMarks = restMarks.Where(m => m.Down == upperIsDown || lower.Count == 0)
            .Select(m => (m.Onset, m.Node)).ToList();
        var lowerMarks = restMarks.Where(m => m.Down != upperIsDown && lower.Count > 0)
            .Select(m => (m.Onset, m.Node)).ToList();
        var bars = new List<Fraction>();
        if (barLength > Fraction.Zero)
            for (var b = barLength - offsetInBar; b < length; b += barLength)
                if (b > Fraction.Zero)
                    bars.Add(b);
        var up = VoiceOf(upper, runs, bars, length);
        var low = lower.Count > 0 ? VoiceOf(lower, runs, bars, length) : new List<DrumPiece>();
        return up == null || low == null ? null : new DrumPlan(WithMarkedRests(up, upperMarks), WithMarkedRests(low, lowerMarks));
    }

    /// <summary>
    /// Gives each marked rest of a voice's lines to the piece standing at its moment: the rest
    /// that begins there, else whatever does (a hit of another line), else the piece it falls in.
    /// </summary>
    private static List<DrumPiece> WithMarkedRests(List<DrumPiece> pieces, List<(Fraction Onset, RestSyntax Node)> marks)
    {
        if (marks.Count == 0)
            return pieces;
        var starts = new List<Fraction>(pieces.Count);
        Fraction t = Fraction.Zero;
        foreach (var piece in pieces)
        {
            starts.Add(t);
            t += Fraction.FromNoteValue(piece.Value).Dotted(piece.Dots) * (piece.Run?.Scale ?? new Fraction(1, 1));
        }
        var result = pieces.ToList();
        foreach (var (onset, rest) in marks)
        {
            int at = Enumerable.Range(0, result.Count).Where(i => starts[i] == onset)
                .OrderBy(i => result[i].Hits == null ? 0 : 1).DefaultIfEmpty(-1).First();
            if (at < 0)
                at = Enumerable.Range(0, result.Count).LastOrDefault(i => starts[i] <= onset);
            if (at >= 0 && at < result.Count && result[at].MarkedRest == null)
                result[at] = result[at] with { MarkedRest = rest };
        }
        return result;
    }

    /// <summary>
    /// One voice of a chart: its hits merged into chords by onset, each held to the next onset
    /// (or bar line, or tuplet edge), the gaps filled with rests. Null when two tuplets cross
    /// in the voice, or a hit outside a tuplet falls inside one.
    /// </summary>
    private static List<DrumPiece>? VoiceOf(List<DrumHit> hits, List<DrumTupletRun> allRuns,
        List<Fraction> bars, Fraction length)
    {
        // The runs this voice plays in, one a span of time: the same triplet written in two
        // lines is one bracket.
        var runs = new List<DrumTupletRun>();
        var runOf = new Dictionary<int, DrumTupletRun>();
        foreach (int r in hits.Where(h => h.Run >= 0).Select(h => h.Run).Distinct())
        {
            var run = allRuns[r];
            var same = runs.FirstOrDefault(o => o.Start == run.Start && o.End == run.End && o.Scale == run.Scale);
            if (same == null)
            {
                if (runs.Any(o => o.Start < run.End && run.Start < o.End))
                    return null;
                runs.Add(same = run);
            }
            runOf[r] = same;
        }
        // A hit from another line may fall inside a run only on the run's own grid — a
        // backbeat's quarter on a shuffle's beat joins it; an eighth between two triplet notes
        // cannot be written in that tuplet, and the chart is drawn as written instead.
        foreach (var h in hits)
            if (RunAt(runs, h.Onset) is { } inside
                && (h.Run < 0 || runOf[h.Run] != inside)
                && !OnRunGrid(inside, h.Onset, hits.Where(o => o.Run >= 0 && runOf[o.Run] == inside)))
                return null;

        var cuts = new SortedSet<Fraction> { Fraction.Zero, length };
        foreach (var h in hits) cuts.Add(h.Onset);
        foreach (var r in runs) { cuts.Add(r.Start); cuts.Add(r.End); }
        foreach (var b in bars) cuts.Add(b);
        var onsets = hits.GroupBy(h => h.Onset).ToDictionary(g => g.Key, g => g.ToList());
        var points = cuts.Where(c => c <= length).ToList();

        var pieces = new List<DrumPiece>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Fraction at = points[i], next = points[i + 1];
            var run = RunAt(runs, at);
            Fraction scale = run?.Scale ?? new Fraction(1, 1);
            int source = pieces.Count > 0 ? pieces[^1].SourceStart : 0;
            if (onsets.TryGetValue(at, out var group))
            {
                // Held for as long as its longest member was written, never past what comes next.
                Fraction held = group.Max(h => h.Length);
                Fraction sound = held < next - at ? held : next - at;
                if (!Representable(sound / scale, out int v, out int d))
                {
                    // Not one note value: the longest that fits sounds, a rest takes the rest.
                    if (!LargestFitting(sound / scale, out v, out d))
                        return null;
                }
                source = group.Min(h => h.Node.SourceStart);
                pieces.Add(new DrumPiece(v, d, group, run, source));
                at += Fraction.FromNoteValue(v).Dotted(d) * scale;
            }
            // The silence up to the next cut, in as few rests as its value allows.
            while (at < next)
            {
                if (!LargestFitting((next - at) / scale, out int v, out int d))
                    return null;
                pieces.Add(new DrumPiece(v, d, null, run, source));
                at += Fraction.FromNoteValue(v).Dotted(d) * scale;
            }
        }
        return pieces;
    }

    /// <summary>True when <paramref name="onset"/>, read in the run's written time, is no
    /// finer than the run's own hits are — a step its notes already take.</summary>
    private static bool OnRunGrid(DrumTupletRun run, Fraction onset, IEnumerable<DrumHit> own)
    {
        int finest = own.Select(o => ((o.Onset - run.Start) / run.Scale).Denominator)
            .Append(((run.End - run.Start) / run.Scale).Denominator).Max();
        return finest % ((onset - run.Start) / run.Scale).Denominator == 0;
    }

    private static DrumTupletRun? RunAt(List<DrumTupletRun> runs, Fraction t)
        => runs.FirstOrDefault(r => r.Start <= t && t < r.End);

    /// <summary>True when <paramref name="written"/> is one note value — whole to 128th,
    /// up to two dots.</summary>
    private static bool Representable(Fraction written, out int value, out int dots)
    {
        for (value = 1; value <= 128; value *= 2)
            for (dots = 0; dots <= 2; dots++)
                if (Fraction.FromNoteValue(value).Dotted(dots) == written)
                    return true;
        (value, dots) = (0, 0);
        return false;
    }

    /// <summary>The longest note value (whole to 128th, up to two dots) not longer than
    /// <paramref name="room"/>; false when not even a 128th fits.</summary>
    private static bool LargestFitting(Fraction room, out int value, out int dots)
    {
        for (value = 1; value <= 128; value *= 2)
            for (dots = 2; dots >= 0; dots--)
                if (Fraction.FromNoteValue(value).Dotted(dots) <= room)
                    return true;
        (value, dots) = (0, 0);
        return false;
    }

    /// <summary>
    /// Reads one chart line into hits: drum notes, drum chords, bare durations repeating
    /// either, rests, bar lines, <c>tuplet</c> and percent repeats — the music a
    /// drum pattern is written in. Anything else answers false, and the chart is drawn
    /// unmerged.
    /// </summary>
    private sealed class DrumLineReader(MeasureCollector owner, bool? down, List<DrumHit> hits, List<DrumTupletRun> runs)
    {
        public Fraction Time = Fraction.Zero;
        public Fraction Value;
        public int Dots;
        /// <summary>The line's rests that write something on them, at their moments.</summary>
        public readonly List<(Fraction Onset, RestSyntax Node)> MarkedRests = new();
        private GraceExpressionSyntax? _grace;

        public bool Read(List<GreenSite> sites, Fraction scale, int run)
        {
            foreach (var site in sites)
            {
                if (site.Kind == SyntaxKind.None || site.Kind == SyntaxKind.Barline)
                    continue;
                switch (site.Node)
                {
                    case DrumNoteSyntax drum:
                        Hit(scale, run, Length(drum.Duration), (drum.DrumName, drum, null));
                        break;
                    case ChordSyntax chord when IsDrumChord(chord):
                        Hit(scale, run, Length(chord.Duration),
                            chord.DrumNames.Select(d => (d.DrumName, (SyntaxNode)chord, (DrumNoteSyntax?)d)).ToArray());
                        break;
                    case BareDurationSyntax bare:
                        switch (Music.BareDurations.OriginalOf(bare))
                        {
                            case DrumNoteSyntax drum:
                                Hit(scale, run, Length(bare.Duration), (drum.DrumName, bare, null));
                                break;
                            case ChordSyntax chord when IsDrumChord(chord):
                                Hit(scale, run, Length(bare.Duration),
                                    chord.DrumNames.Select(d => (d.DrumName, (SyntaxNode)bare, (DrumNoteSyntax?)d)).ToArray());
                                break;
                            default:
                                return false;
                        }
                        break;
                    case RestSyntax rest when rest.RestText != "R" || rest.Duration != null:
                        if (rest.Articulations.Any())
                            MarkedRests.Add((Time, rest));
                        Time += Length(rest.Duration) * new Fraction(rest.MeasureCount, 1) * scale;
                        break;
                    case TupletExpressionSyntax tuplet when run < 0:
                    {
                        var inner = scale * new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);
                        int id = runs.Count;
                        runs.Add(null!);
                        var start = Time;
                        if (!Read(owner.GatherVoiceMusicNodes(tuplet.Body), inner, id))
                            return false;
                        runs[id] = new DrumTupletRun(start, Time, inner, tuplet.TupletRatio, tuplet.BaseDivision, tuplet.SourceStart);
                        break;
                    }
                    case RepeatExpressionSyntax repeat when repeat.RepeatType.Text is "unfold" or "percent":
                        // The old spelling, reported where written: its body once.
                        if (!Read(owner.GatherVoiceMusicNodes(repeat.Body), scale, run))
                            return false;
                        break;
                    case PercentRepeatSyntax percent:
                    {
                        // A chart line's pattern is heard in every bar, so the merged chart
                        // writes each repetition's hits (the bar lines read nothing).
                        var body = owner.GatherVoiceMusicNodes(percent);
                        body.RemoveAll(s => s.Kind == SyntaxKind.PercentMarkItem);
                        for (int k = 0; k < percent.Count; k++)
                            if (!Read(body, scale, run))
                                return false;
                        break;
                    }
                    // A flam's grace waits for the hit after it, and is walked in front of the
                    // item that hit joins (EmitDrumPiece).
                    case GraceExpressionSyntax grace:
                        _grace = grace;
                        break;
                    // A mark the flattened walk lists after its own note (IsAttachedMark):
                    // it rides the hit, which collects it.
                    case MusicMarkSyntax when IsAttachedMark(site):
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        private static bool IsDrumChord(ChordSyntax chord)
            => !chord.IsShapeChord && chord.DrumNames.Any() && !chord.Pitches.Any() && !chord.Degrees.Any();

        /// <summary>The written value, carried like any stream's: a duration sets it, its
        /// absence reads it.</summary>
        private Fraction Length(DurationSyntax? duration)
        {
            if (duration != null)
                (Value, Dots) = (Fraction.FromNoteValue(duration.Value), duration.DotCount);
            return Value.Dotted(Dots);
        }

        private void Hit(Fraction scale, int run, Fraction written, params (string Drum, SyntaxNode Node, DrumNoteSyntax? Member)[] members)
        {
            var sound = written * scale;
            foreach (var (drum, node, member) in members)
            {
                hits.Add(new DrumHit(Time, sound, run, drum, node, down ?? DrumNameRegistry.IsFootDrum(drum), _grace, member));
                _grace = null;
            }
            Time += sound;
        }
    }

    /// <summary>
    /// The hits of <paramref name="chart"/> no drummer can play at once (owner's rule
    /// 2026-10-11): three or more kit pieces for the two hands, three or more for the two
    /// feet, or two sounds of the one hi-hat (closed and open). Each is named at the hit that
    /// made it too many. Read on a FINISHED collect (the phrases are known then), by
    /// DrumChartValidator; the lines are read from a quarter-note default, which only a
    /// line opening without a duration could feel.
    /// </summary>
    internal IReadOnlyList<(SyntaxNode At, string Problem)> DrumChartCollisions(ParallelExpressionSyntax chart)
    {
        var hits = new List<DrumHit>();
        var runs = new List<DrumTupletRun>();
        foreach (var (line, down) in chart.DrumLines)
            new DrumLineReader(this, down, hits, runs) { Value = new Fraction(1, 4) }
                .Read(GatherVoiceMusicNodes(line), new Fraction(1, 1), -1);

        var found = new List<(SyntaxNode, string)>();
        foreach (var moment in hits.GroupBy(h => h.Onset))
        {
            var pieces = moment.GroupBy(h => DrumNameRegistry.TryGet(h.Drum, out var i) ? i.FullName : h.Drum)
                .Select(g => g.Last()).ToList();
            var hands = pieces.Where(h => !DrumNameRegistry.IsFootDrum(h.Drum)).ToList();
            var feet = pieces.Where(h => DrumNameRegistry.IsFootDrum(h.Drum)).ToList();
            var hihat = pieces.Where(h => DrumNameRegistry.TryGet(h.Drum, out var i)
                && i.FullName is "hihat" or "closedhihat" or "openhihat" or "halfopenhihat").ToList();
            if (hands.Count >= 3)
                found.Add((hands[2].Node, $"{hands.Count} kit pieces for two hands at once ({string.Join(", ", hands.Select(h => h.Drum))})"));
            if (feet.Count >= 3)
                found.Add((feet[2].Node, $"{feet.Count} kit pieces for two feet at once ({string.Join(", ", feet.Select(h => h.Drum))})"));
            if (hihat.Count >= 2)
                found.Add((hihat[1].Node, $"the one hi-hat both {string.Join(" and ", hihat.Select(h => h.Drum))} at once"));
        }
        return found;
    }

    /// <summary>
    /// Emits one merged voice into <paramref name="builder"/>: each piece a drum note, a drum
    /// chord or a rest, a tuplet's pieces scaled and bracketed as <see cref="ProcessTuplet"/>
    /// does, and every hit's own marks — its automatic "+"/"○", its dynamics and scripts — on
    /// the item it joined.
    /// </summary>
    private void EmitDrumVoice(List<DrumPiece> pieces, MeasureBuilder builder, bool stemUp)
    {
        for (int i = 0; i < pieces.Count;)
        {
            var run = pieces[i].Run;
            if (run == null)
            {
                EmitDrumPiece(pieces[i], builder, stemUp, scaled: null);
                i++;
                continue;
            }
            int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
            int first = builder.CurrentItemCount;
            Fraction written = Fraction.Zero;
            for (; i < pieces.Count && pieces[i].Run == run; i++)
                written += EmitDrumPiece(pieces[i], builder, stemUp, run.Scale);
            _tupletBrackets.Add(new TupletBracketItem(run.Ratio, run.Base, first, builder.CurrentItemCount - 1,
                measureIndex, run.SourceStart, 0, _cursor.StaffIndex, _cursor.VoiceIndex));
            builder.AddDuration(written * run.Scale, run.SourceStart + 1);
        }
    }

    /// <summary>A hit's head stands in parentheses: its item writes <c>@parenthesize</c>, or, as a
    /// chord member, the member does (<c>&lt;hh sn@parenthesize&gt;</c>).</summary>
    private static bool HitParenthesized(DrumHit h)
        => WritesParenthesize(h.Node) || (h.Member is { } m && m.Articulations.Any(IsParenthesizeMark));

    /// <summary>One piece of a merged voice; returns its written value.</summary>
    private Fraction EmitDrumPiece(DrumPiece piece, MeasureBuilder builder, bool stemUp, Fraction? scaled)
    {
        int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
        int itemIndex = builder.CurrentItemCount;
        Fraction anchor = builder.CurrentDuration;
        var value = Fraction.FromNoteValue(piece.Value);
        // A flam: its grace is walked now, in front of the item its hit joined.
        if (piece.Hits?.FirstOrDefault(h => h.Grace != null) is { Grace: { } grace } graced)
        {
            _pendingGrace = grace;
            _pendingGraceAt = (builder.CurrentMeasureIndex, builder.CurrentItemCount, _meta.Clef);
            bool noSlur = false;
            (itemIndex, _, _) = OpenSoundingItem(graced.Node, builder, measureIndex, itemIndex, ref noSlur);
        }
        MusicItem item;
        if (piece.Hits is not { } hits)
            item = new RestItem(value, piece.Dots, piece.SourceStart);
        else if (hits.Count == 1)
        {
            var info = DrumOverrides.Resolve(_drumOverrides, hits[0].Drum);
            item = new NoteItem(info.StaffPosition, value, piece.Dots, accidental: null, piece.SourceStart)
            {
                Notehead = info.Notehead,
                Midi = info.GmKey,
            };
        }
        else
        {
            var notes = hits.Select(h =>
            {
                var info = DrumOverrides.Resolve(_drumOverrides, h.Drum);
                return new ChordNoteInfo(info.StaffPosition, null, Notehead: info.Notehead, Midi: info.GmKey,
                    SourcePosition: h.HeadSource);
            }).OrderBy(n => n.StaffPosition).ToImmutableArray();
            item = new ChordItem(notes, value, piece.Dots, piece.SourceStart);
        }

        // Decorated as any sounding item, then given its parentheses hit by hit: a ghost
        // note's pair is its own head's, whichever lines it shares the chord with.
        if (piece.Hits is { Count: > 0 } sounding)
            item = item switch
            {
                NoteItem n => DecorateSoundingItem(n, sounding[0].Node, false) with
                {
                    Parenthesized = HitParenthesized(sounding[0]),
                },
                ChordItem c => DecorateSoundingItem(c, sounding[0].Node, false) is var dc
                    ? dc with
                    {
                        Parenthesized = false,
                        Notes = [.. dc.Notes.Select(n => n with
                        {
                            Parenthesized = sounding.Any(h => h.HeadSource == n.SourcePosition && HitParenthesized(h)),
                        })],
                    }
                    : c,
                _ => item,
            };
        // A `@todo` written in a line rides the item it became: a hit's, or a marked rest's
        // (the merge rebuilt that silence). Until 2026-10-11 neither reached the page.
        if ((piece.MarkedRest as SyntaxNode ?? piece.Hits?.Select(h => h.Node).FirstOrDefault(n => TodoKeyOf(n) != null)) is { } todo
            && TodoKeyOf(todo) != null)
            item = item with { TodoKey = TodoKeyOf(todo), TodoMemo = TodoMemoOf(todo), TodoBelow = TodoBelowOf(todo) };
        if (scaled is { } scale)
            builder.AddItemWithoutDuration(item switch
            {
                NoteItem n => n with { TimeScale = scale },
                ChordItem c => c with { TimeScale = scale },
                RestItem r => r with { TimeScale = scale },
                _ => item,
            });
        else
            builder.AddItem(item);

        if (piece.MarkedRest is { } marked)
        {
            CollectDynamics(marked, measureIndex, itemIndex);
            CollectArticulations(marked, measureIndex, itemIndex, stemUp, null, anchor);
        }
        if (piece.Hits is { } members)
        {
            foreach (var h in members)
                AddDrumMark(h.Drum, measureIndex, itemIndex, h.Node.SourceStart);
            foreach (var node in members.Select(h => h.Node).Distinct())
            {
                CollectDynamics(node, measureIndex, itemIndex);
                CollectArticulations(node, measureIndex, itemIndex, stemUp, null, anchor);
            }
        }
        return value.Dotted(piece.Dots);
    }
}
