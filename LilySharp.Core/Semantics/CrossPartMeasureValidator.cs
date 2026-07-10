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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Cross-part measure-alignment validation. Extracted from <see cref="MeasureValidator"/>
/// (which owns per-block fullness): the two share only the running warned-span set
/// (a fullness warning suppresses the mismatch report for the same span) and the
/// <see cref="MeasureDurations"/> beat-counting logic.
/// </summary>
/// <remarks>
/// Time signatures are SCORE-level (like LilyPond's Timing context): a
/// "time" declared at top level or section level governs every part, so
/// a part that writes the right number of beats without restating the
/// time signature is correct and must NOT warn. What the time signature
/// cannot explain — two parts disagreeing about a measure's length at
/// the same index — breaks vertical alignment, span bars and playback,
/// and is reported here. Fullness warnings already emitted by the
/// per-block pass suppress the mismatch report for the same source span
/// (one root cause, one diagnostic).
/// </remarks>
internal sealed class CrossPartMeasureValidator
{
    private readonly DiagnosticBag _diagnostics;
    private readonly HashSet<(int Start, int Length)> _warnedSpans;
    private Dictionary<string, SyntaxNode>? _phraseBodies;

    /// <summary>
    /// Shares the caller's diagnostic bag and warned-span set so the cross-part
    /// pass runs AFTER (and defers to) the per-block fullness pass.
    /// </summary>
    public CrossPartMeasureValidator(DiagnosticBag diagnostics, HashSet<(int Start, int Length)> warnedSpans)
    {
        _diagnostics = diagnostics;
        _warnedSpans = warnedSpans;
    }

    private sealed class DurationResetMarker
    {
        public static readonly DurationResetMarker Instance = new();
    }

    private readonly record struct PartMeasure(Fraction Duration, TextSpan Span);

    public void Validate(SyntaxNode root)
    {
        _phraseBodies = new Dictionary<string, SyntaxNode>();
        foreach (var n in root.DescendantNodes())
        {
            if (n is PhraseDeclarationSyntax ph)
                _phraseBodies[ph.Name.Text] = ph.Body;
            else if (n is VariableDeclarationSyntax vd)
                _phraseBodies[vd.Name.Text] = vd.Expression;
        }

        // Document-order walk: top-level time declarations update the score
        // time; each section validates with the time in force at its site.
        var time = new Fraction(4, 4);
        WalkForSections(root, ref time);
    }

    private void WalkForSections(SyntaxNode node, ref Fraction time)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child == null || child is SyntaxTokenNode)
                continue;
            switch (child)
            {
                case TimeSignatureSyntax ts:
                    time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
                    break;
                case SectionDeclarationSyntax section:
                    time = ValidateSectionCrossPart(section, time);
                    break;
                case PhraseDeclarationSyntax:
                case VariableDeclarationSyntax:
                    break; // bodies are validated where referenced
                default:
                    WalkForSections(child, ref time);
                    break;
            }
        }
    }

    private Fraction ValidateSectionCrossPart(SectionDeclarationSyntax section, Fraction time)
    {
        // Section items in document order: a section-level time declaration
        // applies to the part blocks that follow it. Each part records the
        // time in force at its own position.
        var parts = new List<(string Name, Fraction Time, TextSpan TimeSpan, List<PartMeasure> Measures)>();
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            switch (child)
            {
                case TimeSignatureSyntax ts:
                    time = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
                    break;
                case PartBlockSyntax pb:
                    parts.Add((pb.Name, time, pb.PartName.Span, BuildPartMeasures(pb)));
                    break;
            }
        }

        if (parts.Count < 2)
            return time;

        // A time declared BETWEEN part blocks would put the parts of one
        // section in different meters — flag it; alignment is undefined.
        for (int p = 1; p < parts.Count; p++)
        {
            if (parts[p].Time != parts[0].Time)
            {
                _diagnostics.Warning(parts[p].TimeSpan, DiagnosticCodes.ConflictingTimeSignatures,
                    $"Part '{parts[p].Name}' is in {parts[p].Time} but part '{parts[0].Name}' is in {parts[0].Time} within the same section");
            }
        }

        int maxLen = parts.Max(p => p.Measures.Count);
        for (int i = 0; i < maxLen; i++)
        {
            var present = parts.Where(p => i < p.Measures.Count).ToList();
            if (present.Count < 2)
                continue;

            var durations = present.Select(p => p.Measures[i].Duration).Distinct().ToList();
            if (durations.Count <= 1)
                continue;

            // Blame the parts whose duration deviates from their meter; if
            // none matches the meter, blame everyone after the first.
            var conformers = present.Where(p => p.Measures[i].Duration == p.Time).ToList();
            var reference = conformers.Count > 0 ? conformers[0] : present[0];
            foreach (var part in present)
            {
                if (part.Measures[i].Duration == reference.Measures[i].Duration)
                    continue;
                var span = part.Measures[i].Span;
                if (_warnedSpans.Contains((span.Start, span.Length)))
                    continue; // already explained by a fullness warning
                _warnedSpans.Add((span.Start, span.Length));
                _diagnostics.Warning(span, DiagnosticCodes.MeasureDurationMismatch,
                    $"Measure {i + 1} of part '{part.Name}' lasts {part.Measures[i].Duration} but part '{reference.Name}' has {reference.Measures[i].Duration} — parts will not align");
            }
        }

        return time;
    }

    /// <summary>
    /// Flattens a part block into measures, expanding $phrase references
    /// (each reference enters a fresh default-duration frame, matching the
    /// collector's phrase-fresh semantics) and splitting at written
    /// barlines. Tuplet/grace interiors are handled by ItemDuration.
    /// </summary>
    private List<PartMeasure> BuildPartMeasures(PartBlockSyntax part)
    {
        var stream = new List<object>();
        FlattenMusic(part, stream, new HashSet<string>());

        var measures = new List<PartMeasure>();
        var current = new List<SyntaxNode>();
        var defaultDuration = Fraction.Quarter;
        var total = Fraction.Zero;

        void Flush()
        {
            if (current.Count == 0)
                return;
            measures.Add(new PartMeasure(total, MeasureDurations.GetSpan(current)));
            current = new List<SyntaxNode>();
            total = Fraction.Zero;
        }

        foreach (var entry in stream)
        {
            if (entry is DurationResetMarker)
            {
                defaultDuration = Fraction.Quarter;
                continue;
            }
            var node = (SyntaxNode)entry;
            if (node is BarlineSyntax)
            {
                Flush();
                continue;
            }
            total += MeasureDurations.ItemDuration(node, ref defaultDuration);
            current.Add(node);
        }
        Flush();
        return measures;
    }

    private void FlattenMusic(SyntaxNode scope, List<object> output, HashSet<string> activeRefs)
    {
        // A variable bound to a single music node has no relevant
        // DESCENDANTS — the node itself is the content.
        if (scope is NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax
            or TupletExpressionSyntax or GraceExpressionSyntax)
        {
            output.Add(scope);
            return;
        }

        foreach (var n in scope.DescendantNodes())
        {
            // Tuplet/grace interiors are folded into their wrapper by
            // ItemDuration; inline-volta interiors are ordinary written
            // measures and flow through as themselves. Repeat interiors are
            // expanded by the RepeatExpressionSyntax case below.
            if (IsInside<TupletExpressionSyntax>(n, scope) || IsInside<GraceExpressionSyntax>(n, scope)
                || IsInside<RepeatExpressionSyntax>(n, scope))
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

                // The cross-part alignment must see repeats at their PLAYED
                // length: percent/unfold repeat their body COUNT times (the
                // collector expands them into that many real measures), a
                // tremolo is one metric item folded by ItemDuration — before
                // this the cello's `repeat percent 3` counted once and every
                // later measure "misaligned".
                // LILYPOND-REF: lily/percent-repeat-iterator.cc,
                //   lily/chord-tremolo-iterator.cc.
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
                            FlattenMusic(rep.Body, output, activeRefs);
                        }
                    }
                    break;

                case VariableReferenceSyntax varRef:
                    var name = varRef.Name.Text;
                    if (_phraseBodies!.TryGetValue(name, out var body) && activeRefs.Add(name))
                    {
                        output.Add(DurationResetMarker.Instance);
                        FlattenMusic(body, output, activeRefs);
                        activeRefs.Remove(name);
                    }
                    break;
            }
        }
    }

    private static bool IsInside<T>(SyntaxNode node, SyntaxNode scope) where T : SyntaxNode
    {
        for (var p = node.Parent; p != null && p != scope; p = p.Parent)
        {
            if (p is T)
                return true;
        }
        return false;
    }
}
