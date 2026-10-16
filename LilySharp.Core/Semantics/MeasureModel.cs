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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Splits a music scope into measures for the SEMANTIC validators — the single home
/// of Lily#'s bare-barline rule on the validation side, so the fullness, cross-part,
/// and empty-placeholder passes can no longer disagree about what a bar is.
/// </summary>
/// <remarks>
/// The rule (mirrors <c>MeasureBuilder.HandleBarline</c>, which owns it for the RENDER
/// path): a bar of music is closed by the barline after it; a <c>|</c> on an empty span
/// closes an EMPTY measure — including one opening the scope, so <c>{ | | | | }</c> is
/// four bars (owner's decision, 2026-08-28); a bare <c>|</c> that lands on a boundary an
/// auto-fill or a phrase exit just closed merely CONFIRMS it; a TYPED barline (<c>||</c>,
/// <c>:|</c>, <c>|.</c>) on an empty span is a decoration. Directives (key/time/clef/…)
/// carry no duration and are not content, so <c>| key |</c> is an empty placeholder —
/// exactly as the collector treats it. Phrase references expand in a fresh
/// default-duration frame; tuplet/grace interiors fold into their wrapper via
/// <see cref="MeasureDurations.ItemDuration"/>; a percent repeat's bars are written bars, and
/// each mark is the N bars it repeats, as <c>R1*N</c> is. (Until 2026-10-14 a
/// <c>repeat percent</c> body was expanded here with the bar auto-completing at the meter
/// while it was in flight; a percent repeat writes its bar lines now.)
/// </remarks>
internal static class MeasureModel
{
    /// <summary>One measure of a flattened scope: its total sounded duration, the source
    /// span to squiggle, whether it is an explicit empty <c>| |</c> placeholder, the end of
    /// the written bar line that closed its music (-1 for the music after a scope's last bar
    /// line), whether the bar was cut at the meter rather than written — one of the bars
    /// of an <c>R1*N</c> or of a <c>%N</c>, the two places a bar line cannot be written
    /// (<see cref="WrittenBar"/> holds every other bar to its bar lines) — and whether it holds
    /// music inlined from a phrase body (whose text lies outside the scope).</summary>
    internal readonly record struct Bar(Fraction Duration, TextSpan Span, bool IsEmpty = false, int BarLineEnd = -1,
        bool Flowed = false, bool HasPhrase = false);

    /// <summary>
    /// Flattens <paramref name="scope"/> (expanding phrase references via
    /// <paramref name="phraseBodies"/>) and splits it into measures with the
    /// bare-barline rule. Empty <c>| |</c> placeholders are returned as
    /// <see cref="Bar.IsEmpty"/> bars spanning the gap between the two barlines.
    /// </summary>
    /// <param name="initialMeter">The meter in force where the scope starts (callers that
    /// track the score meter pass it; default 4/4). It only steers the repeat-flow
    /// auto-complete below — the <see cref="Bar.IsEmpty"/> answer never depends on it, so
    /// the empty-placeholder pass may omit it. A <c>time</c> written inside the scope
    /// (or inside a repeat body — the expansion replays it per turn, exactly as the
    /// collector walks it) updates the running meter from that point.</param>
    /// <param name="meterChanges">When given, every <c>time</c> the walk meets, with the index
    /// of the bar it stands in (a repeat's turns each report theirs) and whether music of that
    /// bar came before it — what <see cref="Svg.Collector.SectionMeterPlan"/> folds into the
    /// score's meters.</param>
    /// <param name="plannedMeterAt">When given, the <c>time</c> the score's meter puts at the
    /// start of a bar this scope does not write one for (<see cref="Svg.Collector.SectionMeterPlan.ForeignChangeAt"/>),
    /// by bar index — adopted as the bar opens, so a bare <c>R</c> or an empty <c>| |</c>
    /// there is a bar of that meter, as the collector draws it.</param>
    /// <param name="expansionBudget">The walk's expansion budget in the collector's own
    /// unit (<see cref="ExpansionCap"/> by default; tests pass a small one to pin the
    /// truncation arms, as ExpansionBudgetTests do for the collector).</param>
    /// <param name="index">When given, filled with the bar every item of music lands in, by the
    /// item's address in the expansion: the start of each phrase reference it was inlined
    /// through, outermost first ("" outside any), and its own start — the address the collector
    /// walks it at (MeasureCollector.WrittenBarAt), so music inlined from a phrase body finds the
    /// written bar it plays in. The first bar an address lands in wins (a repeat's later turns
    /// replay the same addresses).</param>
    public static List<Bar> Split(SyntaxNode scope, IReadOnlyDictionary<string, SyntaxNode> phraseBodies,
        Fraction? initialMeter = null, List<(int Bar, TimeSignatureSyntax Time, bool MidBar)>? meterChanges = null,
        Func<int, TimeSignatureSyntax?>? plannedMeterAt = null, int expansionBudget = ExpansionCap,
        Dictionary<(string Chain, int Position), int>? index = null)
    {
        var stream = new List<object>();
        var budget = new ExpansionBudget(expansionBudget);
        Flatten(scope, stream, new HashSet<string>(), phraseBodies, budget, inPhrase: false);

        var bars = new List<Bar>();
        var current = new List<SyntaxNode>();
        var defaultDuration = Fraction.Quarter;
        var total = Fraction.Zero;
        // The scope-start boundary is CONSUMED: a `|` opening the scope closes an empty
        // measure like any other written barline (owner's decision, 2026-08-28 — the rule
        // is stated on MeasureBuilder._confirmableBoundary, which owns it).
        bool confirmable = false;
        // …but `|:` closes nothing, so at a scope start it leaves no unowned span behind
        // it: `{ |: c'4 d e f :| }` is one bar (MeasureBuilder._atScopeStart is the twin).
        bool atScopeStart = true;
        int prevBarEnd = -1; // ink end of the last barline, for an empty bar's span
        var meter = initialMeter ?? DurationCalculator.ParseTimeSignature(4, 4);
        bool senzaMisura = false; // time none: no auto-complete boundary exists
        int plannedFor = -1;      // the bar plannedMeterAt was last asked for
        bool flows = false;       // the bar under construction closes where its beats fill (Bar.Flowed)
        bool phraseInBar = false; // …holds music inlined from a phrase body (Bar.HasPhrase)
        var callSites = new List<int>();  // the phrase references being inlined, outermost first
        string chain = "";                // …as the index's address prefix
        var pendingKeys = new List<(string, int)>();

        // The bar about to be added: the addresses of its music land in it.
        void IndexTheBar()
        {
            if (index != null)
                foreach (var key in pendingKeys)
                    index.TryAdd(key, bars.Count);
            pendingKeys.Clear();
        }

        void FlushMusic(int barLineEnd = -1)
        {
            if (current.Count == 0)
                return;
            IndexTheBar();
            bars.Add(new Bar(total, MeasureDurations.GetSpan(current), BarLineEnd: barLineEnd, Flowed: flows,
                HasPhrase: phraseInBar));
            current = new List<SyntaxNode>();
            total = Fraction.Zero;
            confirmable = false;
            atScopeStart = false;
            flows = false;
            phraseInBar = false;
        }

        foreach (var entry in stream)
        {
            if (entry is DurationResetMarker)
            {
                defaultDuration = Fraction.Quarter;
                continue;
            }
            if (entry is BoundaryMarker boundary)
            {
                // A phrase reference is ONE item, and its two edges answer differently —
                // mirroring the collector. ENTER changes NOTHING: a `|` at the body's head
                // closes an empty measure exactly as one opening a section does, so that
                // extracting a section into a phrase cannot silently lose a bar. EXIT
                // re-arms the boundary only when the phrase left a CLOSED bar to attach to,
                // so an outer `|` right after `~x` confirms that close instead of a gap.
                if (boundary.IsEnter)
                    callSites.Add(boundary.CallSite);
                else if (callSites.Count > 0)
                    callSites.RemoveAt(callSites.Count - 1);
                chain = callSites.Count == 0 ? "" : string.Join("/", callSites);
                if (!boundary.IsEnter)
                {
                    confirmable = current.Count == 0 && bars.Count > 0;
                    atScopeStart = false;
                }
                continue;
            }
            var node = (SyntaxNode)entry;
            if (plannedMeterAt != null && current.Count == 0 && plannedFor != bars.Count)
            {
                plannedFor = bars.Count;
                if (plannedMeterAt(bars.Count) is { } planned)
                {
                    if (planned.IsSenzaMisura) senzaMisura = true;
                    else { senzaMisura = false; meter = DurationCalculator.ParseTimeSignature(planned.Beats, planned.BeatType); }
                }
            }
            if (node is TimeSignatureSyntax time)
            {
                // A directive: it re-arms the meter but carries no duration and is not
                // content (`| time |` stays an empty placeholder pair, as the remark on
                // this class promises).
                if (time.IsSenzaMisura) senzaMisura = true;
                else { senzaMisura = false; meter = DurationCalculator.ParseTimeSignature(time.Beats, time.BeatType); }
                meterChanges?.Add((bars.Count, time, current.Count > 0));
                // A meter changing inside a written span stands where a bar line was left out
                // (MeasureValidator reports it, LYS2017); with no one bar length to hold the span
                // to, its bars close where their beats fill (Flowed), every note kept.
                if (current.Count > 0)
                    flows = true;
                // …and one inside a phrase body leaves the body unpriced by the bar check
                // (MeasureValidator.FlowingPhraseBody), which could not report what the page would
                // leave undrawn: the bar it opens closes where its beats fill.
                if (callSites.Count > 0)
                    flows = true;
                continue;
            }
            if (node is BarlineSyntax bar)
            {
                int barStart = bar.Span.Start;
                int barEnd = bar.Span.Start + bar.Span.Length;
                // ⚠️ `|:` PAIRS LIKE A BARE `|`, and the other typed barlines do not.
                // `||`, `|.` and `:|` on an empty span DECORATE the bar behind them;
                // `|:` decorates nothing and OPENS the bar in front of it, so the span
                // before it is an unowned gap — an empty measure, exactly as `| |` is.
                // Mirrors MeasureBuilder.HandleBarline's RepeatStart branch, which owns
                // this rule for the render path (owner's decision, 2026-08-28).
                bool pairsLikeBare = bar.BarToken.Text is "|" or "|:";
                // A `|:` that OPENS the scope leaves no unowned span behind it (see
                // `atScopeStart` above); every other written barline ends that span.
                bool openerAtScopeStart = atScopeStart && bar.BarToken.Text == "|:";
                atScopeStart = false;
                if (current.Count > 0)
                    FlushMusic(barEnd); // the barline closes the bar of music before it
                else if (!pairsLikeBare || openerAtScopeStart)
                    confirmable = false; // a typed bar decorates the boundary
                else if (confirmable)
                    confirmable = false; // it CONFIRMS an auto-fill / phrase-exit close
                else
                {
                    // An empty span between two boundaries: a placeholder spanning the gap
                    // (falls back to the barline itself for a `|` that OPENS the scope,
                    // where there is no earlier barline to span from).
                    // ⚠️ IT IS WORTH A FULL MEASURE, because that is what the collector
                    // builds — MeasureBuilder.EmitEmptyMeasure fills the bar with one
                    // spacer of the meter in force. This model exists to give the
                    // validators the collector's own answer, so a zero here would make
                    // every `| |` read underfull to a pass that only sees durations.
                    // Under `time none` the collector still fills the gap with the LAST
                    // METERED length (MeasureBuilder keeps it for exactly this spacer), and
                    // `meter` here is that same value — a senza `time` re-arms nothing. One
                    // answer, both sides; no check reads it there (the fill checks are off).
                    int start = prevBarEnd >= 0 ? prevBarEnd : barStart;
                    bars.Add(new Bar(meter,
                        new TextSpan(start, Math.Max(1, barEnd - start)), IsEmpty: true));
                }
                prevBarEnd = barEnd;
                continue;
            }
            var itemDuration = MeasureDurations.ItemDuration(node, ref defaultDuration);
            // A bare `R` lasts what its bar lacks, as the collector reads it (Music.BarRest);
            // a pickup is the validator's to know, and the meter is this model's answer.
            if (node is RestSyntax bareRest && Music.BarRest.IsBare(bareRest) && !senzaMisura)
                itemDuration = meter - total is { Numerator: > 0 } left ? left : meter;
            // A percent mark is the bars it repeats, each a bar of the meter — the bars a
            // mark may repeat are whole bars (MeasureValidator holds them to it), so the
            // meter is their length, as the page's spacers fill them.
            if (node is PercentMarkSyntax)
                itemDuration = meter;
            // A bar of an `R1*N` or of a `%N` closes where its beats fill (Bar.Flowed): no
            // bar line can be written inside them.
            int written = node switch
            {
                RestSyntax r => r.MeasureCount,
                PercentMarkSyntax mark => Math.Max(1, mark.Bars),
                _ => 1,
            };
            // `R4*5` opening a 5/4 bar is ONE bar of it, `R4*40` eight (BarRest.WholeBars) — the
            // collector's reading (MeasureCollector.MusicWalk's RestSyntax arm); a run that is not
            // whole bars, or opens mid-bar, stays N bars of its value.
            if (written > 1 && node is RestSyntax { RestText: "R" } && !senzaMisura && total.Numerator == 0
                && Music.BarRest.WholeBars(itemDuration, written, meter) is int wholeBars)
            {
                written = wholeBars;
                itemDuration = meter;
            }
            total += itemDuration;
            current.Add(node);
            if (index != null)
                pendingKeys.Add((chain, node.Span.Start));
            if (callSites.Count > 0)
                phraseInBar = true;
            if (written > 1)
                flows = true;

            // `R1*N` is N BARS of silence written as one token, and it is written with ONE
            // barline after it. Counting the token as one bar made this model disagree with
            // the collector, which expands the run and lays out N bars — so a part resting
            // `R1*2` against a part writing three bars was reported as two bars long and
            // "padded with rests to align" (LYS2007) while the layout padded nothing.
            // ★ MEASURED, both halves in one run: audit/lpreg/pcmmas-x4.lys warns that
            // part `pa` spans 2 bars, and the page it draws has three.
            // LILYPOND-REF: lily/parser.yy:3117-3120 MULTI_MEASURE_REST — one event carrying
            // an optional duration, whose `*N` factor is what makes it last N bars.
            // The N bars are all the rest's own written value — that value IS one bar's
            // worth by construction (`R2.*3` in 3/4, `R1*3` in 4/4) — so the meter check
            // sees the same answer for each of them. The LAST of the N is left pending, so
            // the barline that follows closes it the ordinary way and does not read as the
            // second half of an empty `| |` pair.
            // ⚠️ MeasureCount is COMPUTED (a child lookup and an int.TryParse over the token
            // text), so it is read once and held. Written as the loop's condition it was
            // re-parsed on every added bar. This walk is on the diagnostics path, which the
            // editor runs on every keystroke. A `%N` is the same shape: N bars, one mark, one
            // bar line after it.
            // Each interior copy is a site of the expansion budget, as the collector
            // charges it: `R1*2000000000` parses, and this loop would faithfully model
            // that many bars on the diagnostics path.
            // (A mark's bars were charged with the mark, in Flatten.)
            for (int extra = 1; extra < written && (node is PercentMarkSyntax || budget.Charge(1)); extra++)
                bars.Add(new Bar(itemDuration, node.Span, Flowed: true));
        }
        FlushMusic(); // a trailing partial bar (music after the last barline) counts
        return bars;
    }

    /// <summary>Marks a fresh default-duration frame (a phrase reference), matching the
    /// collector's phrase-fresh semantics (<c>EnterDefaultFrame</c>). A repeat body is NOT
    /// one: the collector walks it with the running default — bare notes inherit the note
    /// value from BEFORE it — so resetting there counted `c8 … repeat percent 4 { a a … }`'s
    /// bare a's as quarters (reported 2026-08-13, scratch/ベースタブLy/1stbarline.lys).</summary>
    private sealed class DurationResetMarker
    {
        public static readonly DurationResetMarker Instance = new();
    }

    /// <summary>Marks a phrase-reference boundary. The two edges are NOT the same: ENTER
    /// consumes the boundary (the phrase body's own leading <c>|</c> closes an empty
    /// measure), EXIT re-arms it when the phrase left a closed bar (an outer <c>|</c>
    /// confirms that close). Mirrors the collector's <c>ResetMeasureBoundary</c>, whose
    /// <c>retargetableClose</c> parameter draws the same distinction.</summary>
    private sealed class BoundaryMarker(bool isEnter, int callSite = -1)
    {
        public bool IsEnter { get; } = isEnter;
        /// <summary>Where the reference is written (its span's start) — the address the
        /// collector's reset marker carries (RelativeResetMarker's call site).</summary>
        public int CallSite { get; } = callSite;
        public static BoundaryMarker EnterAt(int callSite) => new(true, callSite);
        public static readonly BoundaryMarker Exit = new(false);
    }

    /// <summary>This walk's expansion cut-off: the same phrase DAG the collector
    /// truncates at its budget doubles HERE too (a sibling reference re-flattens
    /// after <c>activeRefs.Remove</c>), and this walk runs on the diagnostics
    /// path per keystroke — it OOM'd on the 2^29 book before this cap did. One
    /// number, one home (<see cref="Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap"/>),
    /// and ONE UNIT: the budget is charged for what the collector's
    /// <c>ChargeExpansion</c> charges — a phrase entry, each site inside an expanded
    /// phrase body, each extra pass of a percent/unfold repeat by its body's size, each
    /// interior copy of <c>R1*N</c> — and never for the written music of the scope. Until
    /// 2026-10-10 the cap was on the flattened item count, written music included, so a
    /// scope of more than 50,000 written items was truncated here while the collector,
    /// which charges nothing for written music, drew it whole and no LYS1033 said so
    /// (HANDOFF §2 R12 ⒠). The budget is per <see cref="Split"/> scope where the
    /// collector's is per collect, so over a scope the score plays the collector has
    /// charged at least as much as this walk: a truncated model stands beside LYS1033.</summary>
    private const int ExpansionCap = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;

    /// <summary>The walk's share of the budget, spent as <c>MeasureCollector.ChargeExpansion</c>
    /// spends it: a charge the remainder cannot cover empties it and answers false.</summary>
    private sealed class ExpansionBudget(int sites)
    {
        private int _left = sites;

        public bool Charge(int sites)
        {
            if (_left < sites)
            {
                _left = 0;
                return false;
            }
            _left -= sites;
            return true;
        }
    }

    /// <param name="inPhrase">True inside an expanded phrase body, where every site is
    /// charged (the collector's <c>ExpandVariable</c> charges each node it emits); the
    /// written music of the scope itself, and a repeat body's first pass, are free.</param>
    private static void Flatten(SyntaxNode scope, List<object> output, HashSet<string> activeRefs,
        IReadOnlyDictionary<string, SyntaxNode> phraseBodies, ExpansionBudget budget, bool inPhrase)
    {
        // A site of the expansion: charged inside a phrase body, free elsewhere. False
        // once the budget is spent — the caller stops, as the collector's walk does.
        bool Emit(SyntaxNode n)
        {
            if (inPhrase && !budget.Charge(1))
                return false;
            output.Add(n);
            return true;
        }

        // A variable bound to a single music node has no relevant DESCENDANTS —
        // the node itself is the content.
        if (scope is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax
            or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax or BarlineSyntax
            or TupletExpressionSyntax or GraceExpressionSyntax or ArpeggioSyntax)
        {
            Emit(scope);
            return;
        }

        foreach (var n in scope.DescendantNodes())
        {
            // Tuplet/grace interiors are folded into their wrapper by ItemDuration;
            // inline-volta interiors are ordinary written measures and flow through
            // as themselves. Repeat interiors are expanded by the case below.
            // (A percent repeat is not among them: its body and bar lines are written music.)
            if (IsInside<TupletExpressionSyntax>(n, scope) || IsInside<GraceExpressionSyntax>(n, scope)
                || IsInside<RepeatExpressionSyntax>(n, scope) || IsInside<ArpeggioSyntax>(n, scope))
                continue;

            // A voice span's voices sound SIMULTANEOUSLY, so only voice 1 advances the
            // scope's timeline — it is the one the collector walks inline, and the bars
            // it closes are the staff's bars. Reading every voice in document order
            // concatenated them: a two-voice section counted twice its bars and twice
            // its beats, which cancelled only because the parts around it were shaped
            // the same way. Voices 2..N are checked for fullness by MeasureValidator,
            // which knows the lead-in they start with.
            if (IsInsideNonLeadVoice(n, scope))
                continue;

            switch (n)
            {
                case NoteSyntax:
                case DrumNoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case ChordRepetitionSyntax:
                case SlashNoteSyntax:
                case BareDurationSyntax:
                case BarlineSyntax:
                case TupletExpressionSyntax:
                case GraceExpressionSyntax:
                    if (!Emit(n))
                        return;
                    break;

                // `<< … >>` is ONE item whose length is the group's total
                // (MeasureDurations.ItemDuration), its members a subdivision of it. It was
                // missing here, so the group counted for nothing: `<< f as des >>4 c'8 des'
                // f'2` read as a 3/4 bar and a grand staff drew LYS2004 against the full
                // bar beside it (2026-09-26).
                case ArpeggioSyntax:
                    if (!Emit(n))
                        return;
                    break;

                // A meter change steers the repeat-flow auto-complete in Split (it is a
                // directive there, never content — `| time |` stays an empty pair).
                case TimeSignatureSyntax:
                    if (!Emit(n))
                        return;
                    break;

                // A tremolo is one metric item folded by ItemDuration
                // (LILYPOND-REF: lily/chord-tremolo-engraver.cc). The old `repeat percent` /
                // `repeat unfold` is reported where written and read as its body once, as the
                // collector draws it.
                case RepeatExpressionSyntax rep:
                    if (rep.RepeatType.Text == "tremolo")
                    {
                        if (!Emit(rep))
                            return;
                    }
                    else
                    {
                        // Inside a phrase body the repeat is ONE site of the body (the
                        // collector's ExpandVariable emits the wrapper as one node).
                        if (inPhrase && !budget.Charge(1))
                            return;
                        Flatten(rep.Body, output, activeRefs, phraseBodies, budget, inPhrase: false);
                    }
                    break;

                // A percent repeat's body and bar lines are written music and flow through as
                // themselves; each mark is the bars it repeats (priced in Split), charged by
                // the body's size as the collector's ProcessPercentRepeat charges a repetition.
                case PercentMarkSyntax mark:
                    if (mark.Parent is PercentRepeatSyntax group
                        && !budget.Charge(Math.Max(1, group.BodyItems.Count())))
                        return;
                    if (!Emit(n))
                        return;
                    break;

                case VariableReferenceSyntax varRef:
                    var name = varRef.Name.Text;
                    if (phraseBodies.TryGetValue(name, out var body) && activeRefs.Add(name))
                    {
                        // A phrase ENTRY is a site (a DAG of empty phrases is sites all the
                        // same); on a spent budget the reference emits nothing, markers
                        // included — balanced by omission, as the collector leaves it.
                        if (!budget.Charge(1))
                        {
                            activeRefs.Remove(name);
                            return;
                        }
                        output.Add(DurationResetMarker.Instance);
                        output.Add(BoundaryMarker.EnterAt(varRef.Span.Start));
                        Flatten(body, output, activeRefs, phraseBodies, budget, inPhrase: true);
                        output.Add(BoundaryMarker.Exit);
                        activeRefs.Remove(name);
                    }
                    break;
            }
        }
    }

    private static bool IsInside<T>(SyntaxNode node, SyntaxNode scope) where T : SyntaxNode
    {
        for (var p = node.Parent; p != null && p != scope; p = p.Parent)
            if (p is T)
                return true;
        return false;
    }

    /// <summary>True when <paramref name="node"/> sits in a voice span but NOT in its first
    /// voice — the branches that sound alongside the timeline rather than advancing it.</summary>
    private static bool IsInsideNonLeadVoice(SyntaxNode node, SyntaxNode scope)
    {
        for (var p = node.Parent; p != null && p != scope; p = p.Parent)
        {
            if (p.Parent is ParallelExpressionSyntax par && !ReferenceEquals(p, par.Voices.FirstOrDefault()))
                return true;
        }
        return false;
    }
}
