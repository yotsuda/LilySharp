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
/// path): a bar of music is closed by the barline after it; a lone leading <c>|</c> on
/// an empty span merely ANCHORS the scope start and creates nothing; the SECOND of a
/// <c>| |</c> pair opens an empty placeholder measure; a TYPED barline (<c>||</c>,
/// <c>:|</c>, <c>|.</c>) on an empty span is a decoration. Directives (key/time/clef/…)
/// carry no duration and are not content, so <c>| key |</c> is an empty placeholder —
/// exactly as the collector treats it. Phrase references expand in a fresh
/// default-duration frame; tuplet/grace interiors fold into their wrapper via
/// <see cref="MeasureDurations.ItemDuration"/>; percent/unfold repeats expand to their
/// played length, and while their expansion is in flight the bar AUTO-COMPLETES at the
/// meter (mirroring <c>MeasureBuilder.AddDuration</c>): a body that is no whole number
/// of bars flows across the bar boundary, so counting its turns into one written bar
/// disagreed with the collector's bar count (<c>repeat volta 2 { d8×8 }</c> was one
/// two-whole-note bar here and two rendered bars on the page).
/// </remarks>
internal static class MeasureModel
{
    /// <summary>One measure of a flattened scope: its total sounded duration, the source
    /// span to squiggle, and whether it is an explicit empty <c>| |</c> placeholder.</summary>
    internal readonly record struct Bar(Fraction Duration, TextSpan Span, bool IsEmpty = false);

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
    public static List<Bar> Split(SyntaxNode scope, IReadOnlyDictionary<string, SyntaxNode> phraseBodies,
        Fraction? initialMeter = null)
    {
        var stream = new List<object>();
        Flatten(scope, stream, new HashSet<string>(), phraseBodies);

        var bars = new List<Bar>();
        var current = new List<SyntaxNode>();
        var defaultDuration = Fraction.Quarter;
        var total = Fraction.Zero;
        // The scope-start boundary absorbs one bare `|`.
        bool confirmable = true;
        int prevBarEnd = -1; // ink end of the last barline, for an empty bar's span
        var meter = initialMeter ?? DurationCalculator.ParseTimeSignature(4, 4);
        bool senzaMisura = false; // time none: no auto-complete boundary exists
        int repeatFlow = 0;       // depth of percent/unfold expansions in flight

        void FlushMusic()
        {
            if (current.Count == 0)
                return;
            bars.Add(new Bar(total, MeasureDurations.GetSpan(current)));
            current = new List<SyntaxNode>();
            total = Fraction.Zero;
            confirmable = false;
        }

        foreach (var entry in stream)
        {
            if (entry is DurationResetMarker)
            {
                defaultDuration = Fraction.Quarter;
                continue;
            }
            if (entry is BoundaryMarker)
            {
                // A phrase reference is ONE item; its boundary re-arms the confirmable
                // boundary (like a section start), so a barline at the edge of the phrase
                // body does not pair with an adjacent outer barline into an empty measure.
                confirmable = true;
                continue;
            }
            if (entry is RepeatFlowMarker flow)
            {
                repeatFlow += flow.Delta;
                continue;
            }
            var node = (SyntaxNode)entry;
            if (node is TimeSignatureSyntax time)
            {
                // A directive: it re-arms the meter but carries no duration and is not
                // content (`| time |` stays an empty placeholder pair, as the remark on
                // this class promises).
                if (time.IsSenzaMisura) senzaMisura = true;
                else { senzaMisura = false; meter = DurationCalculator.ParseTimeSignature(time.Beats, time.BeatType); }
                continue;
            }
            if (node is BarlineSyntax bar)
            {
                int barStart = bar.Span.Start;
                int barEnd = bar.Span.Start + bar.Span.Length;
                if (current.Count > 0)
                    FlushMusic(); // the barline closes the bar of music before it
                else if (bar.BarToken.Text != "|")
                    confirmable = false; // a typed bar decorates the boundary
                else if (confirmable)
                    confirmable = false; // a lone `|` anchors the boundary — no measure
                else
                {
                    // The second of a `| |` pair: an empty placeholder spanning the gap
                    // between the two written barlines (falls back to the barline itself
                    // for a leading pair, whose opener anchored the scope start).
                    int start = prevBarEnd >= 0 ? prevBarEnd : barStart;
                    bars.Add(new Bar(Fraction.Zero,
                        new TextSpan(start, Math.Max(1, barEnd - start)), IsEmpty: true));
                }
                prevBarEnd = barEnd;
                continue;
            }
            var itemDuration = MeasureDurations.ItemDuration(node, ref defaultDuration);
            total += itemDuration;
            current.Add(node);

            // While a repeat expansion is in flight the bar auto-completes at the meter,
            // as the collector renders it (MeasureBuilder.AddDuration): a repeat opening
            // mid-bar, or a body that is no whole number of bars, flows across the bar
            // boundary instead of piling its turns into the surrounding written bar.
            // The boundary an auto-fill leaves is ATTACHABLE — the builder's
            // _lastEndAutoFill — so `confirmable` re-arms and a written `|` standing
            // right on it confirms the close instead of opening an empty `| |` bar.
            if (repeatFlow > 0 && !senzaMisura && total >= meter)
            {
                bars.Add(new Bar(total, MeasureDurations.GetSpan(current)));
                current = new List<SyntaxNode>();
                total = Fraction.Zero;
                confirmable = true;
            }

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
            // editor runs on every keystroke.
            if (node is RestSyntax rest && rest.MeasureCount is var written && written > 1)
                // Same cut-off as Flatten's: `R1*2000000000` parses, and this loop
                // would faithfully model that many bars on the diagnostics path.
                for (int extra = 1; extra < written && bars.Count < ExpansionCap; extra++)
                    bars.Add(new Bar(itemDuration, node.Span));
        }
        FlushMusic(); // a trailing partial bar (music after the last barline) counts
        return bars;
    }

    /// <summary>Marks a fresh default-duration frame (a phrase reference), matching the
    /// collector's phrase-fresh semantics (<c>EnterDefaultFrame</c>). A repeat turn is NOT
    /// one: the collector's <c>ProcessRepeatExpression</c> walks the body with the running
    /// default — bare notes inherit the note value from BEFORE the repeat and across its
    /// turns — so resetting per turn counted `c8 … repeat percent 4 { a a … }`'s bare a's
    /// as quarters (reported 2026-08-13, scratch/ベースタブLy/1stbarline.lys).</summary>
    private sealed class DurationResetMarker
    {
        public static readonly DurationResetMarker Instance = new();
    }

    /// <summary>Marks a phrase-reference boundary (enter / exit) — re-arms the confirmable
    /// boundary so an edge barline of the phrase body does not pair with an adjacent outer
    /// barline. Mirrors the collector's <c>ResetMeasureBoundary</c> at those markers.</summary>
    private sealed class BoundaryMarker
    {
        public static readonly BoundaryMarker Instance = new();
    }

    /// <summary>Brackets a percent/unfold expansion in the stream: while the depth is
    /// positive, the splitter auto-completes the bar at the meter (the collector's
    /// rendered-bar rule) — outside it, only written barlines close bars, which is the
    /// whole point of validating what the author wrote. Nested repeats nest the depth.</summary>
    private sealed class RepeatFlowMarker
    {
        public static readonly RepeatFlowMarker Enter = new(+1);
        public static readonly RepeatFlowMarker Exit = new(-1);
        public int Delta { get; }
        private RepeatFlowMarker(int delta) => Delta = delta;
    }

    /// <summary>This walk's expansion cut-off: the same phrase DAG the collector
    /// truncates at its budget doubles HERE too (a sibling reference re-flattens
    /// after <c>activeRefs.Remove</c>), and this walk runs on the diagnostics
    /// path per keystroke — it OOM'd on the 2^29 book before this cap did. One
    /// number, one home (<see cref="Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap"/>);
    /// a truncated model can misreport cross-part bar counts for the pathological
    /// book, which already carries LYS1033 naming the real problem.</summary>
    private const int ExpansionCap = Svg.Collector.MeasureCollector.DefaultExpansionBudgetCap;

    private static void Flatten(SyntaxNode scope, List<object> output, HashSet<string> activeRefs,
        IReadOnlyDictionary<string, SyntaxNode> phraseBodies)
    {
        if (output.Count >= ExpansionCap)
            return; // truncated — see ExpansionCap's remarks

        // A variable bound to a single music node has no relevant DESCENDANTS —
        // the node itself is the content.
        if (scope is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax
            or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax or BarlineSyntax
            or TupletExpressionSyntax or GraceExpressionSyntax)
        {
            output.Add(scope);
            return;
        }

        foreach (var n in scope.DescendantNodes())
        {
            // Tuplet/grace interiors are folded into their wrapper by ItemDuration;
            // inline-volta interiors are ordinary written measures and flow through
            // as themselves. Repeat interiors are expanded by the case below.
            if (IsInside<TupletExpressionSyntax>(n, scope) || IsInside<GraceExpressionSyntax>(n, scope)
                || IsInside<RepeatExpressionSyntax>(n, scope))
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
                    output.Add(n);
                    break;

                // A meter change steers the repeat-flow auto-complete in Split (it is a
                // directive there, never content — `| time |` stays an empty pair).
                case TimeSignatureSyntax:
                    output.Add(n);
                    break;

                // Cross-part alignment and bar counting must see repeats at their
                // PLAYED length: percent/unfold repeat their body COUNT times (the
                // collector expands them into that many real measures), a tremolo is
                // one metric item folded by ItemDuration.
                // LILYPOND-REF: lily/percent-repeat-iterator.cc,
                //   lily/chord-tremolo-engraver.cc.
                case RepeatExpressionSyntax rep:
                    if (rep.RepeatType.Text == "tremolo")
                    {
                        output.Add(rep);
                    }
                    else if (int.TryParse(rep.Count.Text, out int repCount))
                    {
                        // No DurationResetMarker here: the collector walks each turn with
                        // the RUNNING default note value (see the marker's remarks). The
                        // flow markers bracket ALL turns: the seam between turns is inside
                        // the flow, so a body that is no whole number of bars auto-completes
                        // across it exactly where the rendered barline falls.
                        output.Add(RepeatFlowMarker.Enter);
                        for (int r = 0; r < Math.Max(1, repCount)
                            && output.Count < ExpansionCap; r++)
                            Flatten(rep.Body, output, activeRefs, phraseBodies);
                        output.Add(RepeatFlowMarker.Exit);
                    }
                    break;

                case VariableReferenceSyntax varRef:
                    var name = varRef.Name.Text;
                    if (phraseBodies.TryGetValue(name, out var body) && activeRefs.Add(name))
                    {
                        output.Add(DurationResetMarker.Instance);
                        output.Add(BoundaryMarker.Instance); // enter
                        Flatten(body, output, activeRefs, phraseBodies);
                        output.Add(BoundaryMarker.Instance); // exit
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
