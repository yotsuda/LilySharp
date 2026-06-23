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
/// Validates measures against time signatures.
/// </summary>
public sealed class MeasureValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    private Fraction _timeSignature = new(4, 4); // Default 4/4

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Sets the current time signature.
    /// </summary>
    public void SetTimeSignature(int beats, int beatUnit)
    {
        _timeSignature = DurationCalculator.ParseTimeSignature(beats, beatUnit);
    }

    /// <summary>
    /// Validates all measures in a compilation unit.
    /// </summary>
    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        ValidateNode(root);
        ValidateCrossPart(root);
    }

    private void ValidateNode(SyntaxNode node)
    {
        // A tuplet/grace body is a nested MusicBlock, but its notes belong to the
        // enclosing measure (and are counted there with the correct tuplet scale).
        // Don't recurse into it, or it would be validated as a short standalone bar.
        if (node is TupletExpressionSyntax or GraceExpressionSyntax)
            return;

        switch (node)
        {
            case MusicBlockSyntax block:
                ValidateMusicBlock(block);
                break;

            case TimeSignatureSyntax timeSig:
                SetTimeSignature(timeSig.Beats, timeSig.BeatType);
                break;

            case MetadataDeclarationSyntax:
                // MetadataDeclaration now only handles title/composer
                // Time signatures use TimeSignatureSyntax
                break;

            case PropertyAssignmentSyntax:
                // Property assignments are handled within blocks
                break;
        }

        // Recurse into children
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && child is not SyntaxTokenNode)
            {
                ValidateNode(child);
            }
        }
    }

    private void ValidateMusicBlock(MusicBlockSyntax block)
    {
        var measures = SplitIntoMeasures(block);
        var defaultDuration = Fraction.Quarter;

        for (int i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];

            // A mid-piece \time takes effect at the bar it appears in
            // (LilyPond applies the new meter from that timestep), so adopt
            // the new reference meter before checking this bar's fill — else a
            // valid 3/4 bar after a 4/4 opening is wrongly flagged underfull.
            foreach (var item in measure.Items)
            {
                if (item is TimeSignatureSyntax ts)
                    SetTimeSignature(ts.Beats, ts.BeatType);
            }

            var duration = CalculateMeasureDuration(measure.Items, ref defaultDuration);

            if (duration != _timeSignature && duration != Fraction.Zero)
            {
                if (duration < _timeSignature)
                {
                    // Underfull FIRST measures are pickups (anacrusis) and
                    // underfull LAST measures conventionally complete them —
                    // both are normal notation, not authoring errors, so only
                    // interior measures warn. (LilyPond marks pickups with
                    // \partial; Lily# has no such keyword yet, so the edge
                    // measures get the benefit of the doubt.)
                    bool isEdgeMeasure = i == 0 || i == measures.Count - 1;
                    if (!isEdgeMeasure)
                    {
                        var span = GetSpan(measure.Items);
                        _warnedSpans.Add((span.Start, span.Length));
                        _diagnostics.Warning(span, DiagnosticCodes.MeasureIncomplete,
                            $"Measure duration {duration} is less than time signature {_timeSignature}");
                    }
                }
                else if (duration > _timeSignature)
                {
                    // Overfull measure — always worth flagging.
                    var span = GetSpan(measure.Items);
                    _warnedSpans.Add((span.Start, span.Length));
                    _diagnostics.Warning(span, DiagnosticCodes.MeasureOverflow,
                        $"Measure duration {duration} exceeds time signature {_timeSignature}");
                }
            }
        }
    }

    private record MeasureContent(List<SyntaxNode> Items, int StartPosition);

    private List<MeasureContent> SplitIntoMeasures(MusicBlockSyntax block)
    {
        var measures = new List<MeasureContent>();
        var currentItems = new List<SyntaxNode>();
        int startPos = block.Position;

        foreach (var item in block.Items)
        {
            if (item is BarlineSyntax)
            {
                if (currentItems.Count > 0)
                {
                    measures.Add(new MeasureContent(currentItems, startPos));
                    currentItems = [];
                }
                startPos = item.Position + item.FullWidth;
            }
            else
            {
                currentItems.Add(item);
            }
        }

        // Add final measure if not empty
        if (currentItems.Count > 0)
        {
            measures.Add(new MeasureContent(currentItems, startPos));
        }

        return measures;
    }

    private Fraction CalculateMeasureDuration(List<SyntaxNode> items, ref Fraction defaultDuration)
    {
        var total = Fraction.Zero;
        foreach (var item in items)
            total += ItemDuration(item, ref defaultDuration);
        return total;
    }

    /// <summary>
    /// Metric duration of a single music item, recursing into tuplets (scaled by
    /// their ratio) so triplets etc. fill the correct fraction of the bar.
    /// </summary>
    private Fraction ItemDuration(SyntaxNode item, ref Fraction defaultDuration)
    {
        switch (item)
        {
            case NoteSyntax note:
                var noteDuration = DurationCalculator.GetDuration(note, defaultDuration);
                if (note.Duration != null) defaultDuration = noteDuration;
                return noteDuration;

            case RestSyntax rest:
                var restDuration = DurationCalculator.GetDuration(rest, defaultDuration);
                if (rest.Duration != null) defaultDuration = restDuration;
                return restDuration;

            case ChordSyntax chord:
                var chordDuration = DurationCalculator.GetDuration(chord, defaultDuration);
                if (chord.Duration != null) defaultDuration = chordDuration;
                return chordDuration;

            case TupletExpressionSyntax tuplet:
                // actual = written * BaseDivision / TupletRatio
                // (\tuplet 3/2 { c8 c c } -> 3 * 1/8 * 2/3 = 1/4).
                var inner = Fraction.Zero;
                foreach (var bodyItem in tuplet.Body.Items)
                    inner += ItemDuration(bodyItem, ref defaultDuration);
                if (tuplet.TupletRatio > 0)
                    inner *= new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);
                return inner;

            case GraceExpressionSyntax:
                // Grace notes are ornamental and consume no metric time.
                return Fraction.Zero;

            default:
                return Fraction.Zero;
        }
    }


    // =================================================================
    // Cross-part validation
    // =================================================================
    //
    // Time signatures are SCORE-level (like LilyPond's Timing context): a
    // "time" declared at top level or section level governs every part, so
    // a part that writes the right number of beats without restating the
    // time signature is correct and must NOT warn. What the time signature
    // cannot explain — two parts disagreeing about a measure's length at
    // the same index — breaks vertical alignment, span bars and playback,
    // and is reported here. Fullness warnings already emitted by the
    // per-block pass suppress the mismatch report for the same source span
    // (one root cause, one diagnostic).

    private sealed class DurationResetMarker
    {
        public static readonly DurationResetMarker Instance = new();
    }

    private readonly record struct PartMeasure(Fraction Duration, TextSpan Span);

    private readonly HashSet<(int Start, int Length)> _warnedSpans = new();
    private Dictionary<string, SyntaxNode>? _phraseBodies;

    private void ValidateCrossPart(SyntaxNode root)
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
            measures.Add(new PartMeasure(total, GetSpan(current)));
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
            total += ItemDuration(node, ref defaultDuration);
            current.Add(node);
        }
        Flush();
        return measures;
    }

    private void FlattenMusic(SyntaxNode scope, List<object> output, HashSet<string> activeRefs)
    {
        // A variable bound to a single music node has no relevant
        // DESCENDANTS — the node itself is the content.
        if (scope is NoteSyntax or RestSyntax or ChordSyntax or BarlineSyntax
            or TupletExpressionSyntax or GraceExpressionSyntax)
        {
            output.Add(scope);
            return;
        }

        foreach (var n in scope.DescendantNodes())
        {
            // Tuplet/grace interiors are folded into their wrapper by
            // ItemDuration; inline-volta and repeat interiors are ordinary
            // written measures and flow through as themselves.
            if (IsInside<TupletExpressionSyntax>(n, scope) || IsInside<GraceExpressionSyntax>(n, scope))
                continue;

            switch (n)
            {
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case BarlineSyntax:
                case TupletExpressionSyntax:
                case GraceExpressionSyntax:
                    output.Add(n);
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

    private static TextSpan GetSpan(List<SyntaxNode> items)
    {
        if (items.Count == 0)
            return new TextSpan(0, 0);

        // Use Span (not Position/FullSpan) to exclude leading/trailing trivia like comments
        int start = items[0].Span.Start;
        var lastSpan = items[^1].Span;
        int end = lastSpan.Start + lastSpan.Length;
        return new TextSpan(start, end - start);
    }
}