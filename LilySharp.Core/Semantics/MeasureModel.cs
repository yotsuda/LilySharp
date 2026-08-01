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
/// played length.
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
    public static List<Bar> Split(SyntaxNode scope, IReadOnlyDictionary<string, SyntaxNode> phraseBodies)
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
            var node = (SyntaxNode)entry;
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
            total += MeasureDurations.ItemDuration(node, ref defaultDuration);
            current.Add(node);
        }
        FlushMusic(); // a trailing partial bar (music after the last barline) counts
        return bars;
    }

    /// <summary>Marks a fresh default-duration frame (a phrase reference or one turn of a
    /// repeat body), matching the collector's phrase-fresh semantics.</summary>
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

    private static void Flatten(SyntaxNode scope, List<object> output, HashSet<string> activeRefs,
        IReadOnlyDictionary<string, SyntaxNode> phraseBodies)
    {
        // A variable bound to a single music node has no relevant DESCENDANTS —
        // the node itself is the content.
        if (scope is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax
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
                case BarlineSyntax:
                case TupletExpressionSyntax:
                case GraceExpressionSyntax:
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
                        for (int r = 0; r < Math.Max(1, repCount); r++)
                        {
                            output.Add(DurationResetMarker.Instance);
                            Flatten(rep.Body, output, activeRefs, phraseBodies);
                        }
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
