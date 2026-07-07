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
internal sealed class MeasureValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    private Fraction _timeSignature = new(4, 4); // Default 4/4
    private bool _senzaMisura; // time none: no bar-length validation
    // The meter AS WRITTEN ("4/4", "6/8") for diagnostics — the Fraction
    // normalizes (4/4 → "1"), which made the overfull warning read
    // "exceeds time signature 1".
    private string _meterText = "4/4";
    // Set by a top-level `partial N` — the declared pickup length for every
    // voice's first measure (mirrors MeasureCollector._filePartial).
    private Fraction? _filePartial;

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// Sets the current time signature.
    /// </summary>
    public void SetTimeSignature(int beats, int beatUnit)
    {
        _timeSignature = DurationCalculator.ParseTimeSignature(beats, beatUnit);
        _meterText = $"{beats}/{beatUnit}";
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

        // A tremolo repeat's body ("{ c32 }") is metric material of the
        // ENCLOSING measure (counted above via ItemDuration), never a
        // standalone bar — recursing flagged it as a 1/32 "first measure".
        if (node is RepeatExpressionSyntax trem && trem.RepeatType.Text == "tremolo")
            return;

        // LYS0010 recovery: a nested voice's block INLINES into the enclosing
        // voice (SplitIntoMeasures expands it there) — validating it as a
        // standalone block would re-add the phantom short-bar warnings.
        if (node is NestedVoiceRecoverySyntax)
            return;

        switch (node)
        {
            case MusicBlockSyntax block:
                ValidateMusicBlock(block);
                break;

            case SectionDeclarationSyntax section:
                ValidateSectionInlineMusic(section);
                break;

            case TimeSignatureSyntax timeSig when !IsInsideMusicBlock(timeSig) && timeSig.IsSenzaMisura:
                _senzaMisura = true;
                break;

            case TimeSignatureSyntax timeSig when !IsInsideMusicBlock(timeSig):
                // Only a TOP-LEVEL / header `time` re-arms the document meter.
                // An in-block change is applied inside ValidateMusicBlock and
                // scoped there; re-applying it here leaked the previous part's
                // mid-music 3/4 onto the NEXT part's perfectly good 4/4 bars.
                SetTimeSignature(timeSig.Beats, timeSig.BeatType);
                break;

            case PartialDeclarationSyntax filePartial when !IsInsideMusicBlock(filePartial):
                // A top-level `partial N` (a GlobalSetting) arms EVERY voice's
                // first measure; the first bar of each block is then strictly
                // checked against the declared pickup length.
                _filePartial = filePartial.ToFraction();
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
        => ValidateItemsScoped(block.Items, block.Position);

    // Part-major sections hold their music INLINE (a SectionDeclarationSyntax with
    // note/bar children and no MusicBlock wrapper), so the bar-check never saw them
    // — only section-major sections, whose part blocks each wrap a MusicBlock, were
    // checked. Validate the inline music the same way. A section-major section holds
    // part blocks (not inline music) and is left to the per-part-block pass.
    private void ValidateSectionInlineMusic(SectionDeclarationSyntax section)
    {
        var items = new List<SyntaxNode>();
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode) continue;
            if (child is PartBlockSyntax) return; // section-major: not inline music
            items.Add(child);
        }
        if (items.Count > 0)
            ValidateItemsScoped(items, section.Position);
    }

    private void ValidateItemsScoped(IEnumerable<SyntaxNode> items, int startPos)
    {
        // A mid-music `time` re-arms the meter for the rest of THIS block/section
        // only — the state must not leak into the next part's block (each part
        // restates its own changes), or every 4/4 bar of the following part gets
        // flagged against the previous part's 3/4.
        // LILYPOND-REF: Timing is Score-level in LP, but Lily# parts restate
        // meter changes per part; validation follows the per-block timeline.
        var savedTime = _timeSignature;
        var savedMeterText = _meterText;
        var savedSenza = _senzaMisura;
        try
        {
            ValidateMeasures(items, startPos);
        }
        finally
        {
            _timeSignature = savedTime;
            _meterText = savedMeterText;
            _senzaMisura = savedSenza;
        }
    }

    private void ValidateMeasures(IEnumerable<SyntaxNode> items, int startPos)
    {
        var measures = SplitIntoMeasures(items, startPos);
        var defaultDuration = Fraction.Quarter;

        for (int i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];

            // A mid-piece \time takes effect at the bar it appears in
            // (LilyPond applies the new meter from that timestep), so adopt
            // the new reference meter before checking this bar's fill — else a
            // valid 3/4 bar after a 4/4 opening is wrongly flagged underfull.
            // A 'partial N' in the bar declares it a pickup of length 1/N, which
            // is then the expected fill for THIS measure only.
            Fraction? partialLength = null;
            foreach (var item in measure.Items)
            {
                if (item is TimeSignatureSyntax ts)
                {
                    if (ts.IsSenzaMisura) _senzaMisura = true;
                    else { _senzaMisura = false; SetTimeSignature(ts.Beats, ts.BeatType); }
                }
                else if (item is PartialDeclarationSyntax pd)
                    partialLength = pd.ToFraction();
            }

            var duration = CalculateMeasureDuration(measure.Items, ref defaultDuration);

            // A file-level `partial` describes the PIECE-opening pickup. Blocks
            // are validated per section, so apply it to a block's first bar
            // only when that bar does not already fill the meter (a later
            // section's full first bar is not the pickup). An inline `partial`
            // in the bar always wins.
            if (partialLength == null && i == 0 && _filePartial is { } fp
                && duration != _timeSignature)
            {
                partialLength = fp;
            }

            // The pickup's declared length overrides the meter as the fill target
            // for the measure that carries the \partial.
            var expected = partialLength ?? _timeSignature;

            if (!_senzaMisura && duration != expected && duration != Fraction.Zero)
            {
                if (duration < expected)
                {
                    // Underfull FIRST measures are pickups (anacrusis) and
                    // underfull LAST measures conventionally complete them —
                    // both are normal notation, not authoring errors, so only
                    // interior measures warn. But a measure with an explicit
                    // 'partial N' is strictly checked against its declared length
                    // even at an edge: that is the whole point of writing \partial.
                    bool isEdgeMeasure = i == 0 || i == measures.Count - 1;
                    if (!isEdgeMeasure || partialLength != null)
                    {
                        var span = GetSpan(measure.Items);
                        _warnedSpans.Add((span.Start, span.Length));
                        _diagnostics.Warning(span, DiagnosticCodes.MeasureIncomplete,
                            partialLength != null
                                ? $"Pickup measure duration {duration} is less than the declared partial {expected}"
                                : $"Measure duration {duration} is less than time signature {_meterText}");
                    }
                    else if (i == 0)
                    {
                        // An underfull FIRST bar is conventionally an anacrusis,
                        // but written bare it is indistinguishable from a
                        // miscount, gets no strict length check, and bar
                        // numbering counts it as bar 1. Nudge toward declaring
                        // it (a declared pickup is checked exactly and numbered
                        // as bar 0).
                        var span = GetSpan(measure.Items);
                        _diagnostics.Warning(span, DiagnosticCodes.PickupWithoutPartial,
                            $"first measure is shorter than the meter ({duration} of {_meterText}); " +
                            $"if this is a pickup, declare it with '{SuggestPartial(duration)}' " +
                            "(top level or in the voice) so its length is checked and " +
                            "bar numbering starts after it");
                    }
                }
                else if (duration > expected)
                {
                    // Overfull measure — always worth flagging.
                    var span = GetSpan(measure.Items);
                    _warnedSpans.Add((span.Start, span.Length));
                    _diagnostics.Warning(span, DiagnosticCodes.MeasureOverflow,
                        partialLength != null
                            ? $"Pickup measure duration {duration} exceeds the declared partial {expected}"
                            : $"Measure duration {duration} exceeds time signature {_meterText}");
                }
            }
        }
    }

    /// <summary>True when the node sits inside a music block (an in-music
    /// `partial`/`time` belongs to one voice/section; only a top-level one is
    /// file-wide). A PART-MAJOR section holds its music inline with no MusicBlock
    /// wrapper, so a `partial`/`time` written among that inline music counts as
    /// in-music too — otherwise it leaks as a file-wide pickup onto every section.
    /// A SECTION-MAJOR section holds part blocks; its section-level `time`/`partial`
    /// arms the meter for the whole section and is left to the top-level path, so
    /// that is NOT treated as "inside a block" here.</summary>
    private static bool IsInsideMusicBlock(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is MusicBlockSyntax)
                return true;
            if (p is SectionDeclarationSyntax s && SectionHasInlineMusic(s))
                return true;
        }
        return false;
    }

    /// <summary>True for a PART-MAJOR section (inline note/bar children), false for
    /// a SECTION-MAJOR section (part blocks) or an empty one.</summary>
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        bool sawItem = false;
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode) continue;
            if (child is PartBlockSyntax) return false; // section-major
            sawItem = true;
        }
        return sawItem;
    }

    /// <summary>The `partial` clause matching a pickup of <paramref name="length"/>:
    /// exact for plain (1/N) and dotted (3/2N) lengths, a generic hint otherwise.</summary>
    private static string SuggestPartial(Fraction length)
    {
        if (length.Numerator == 1)
            return $"partial {length.Denominator}";
        if (length.Numerator == 3 && length.Denominator % 2 == 0)
            return $"partial {length.Denominator / 2}.";
        return "partial <duration>";
    }

    private record MeasureContent(List<SyntaxNode> Items, int StartPosition);

    private List<MeasureContent> SplitIntoMeasures(IEnumerable<SyntaxNode> blockItems, int blockStartPos)
    {
        var measures = new List<MeasureContent>();
        var currentItems = new List<SyntaxNode>();
        int startPos = blockStartPos;

        void AddItems(IEnumerable<SyntaxNode> items)
        {
            foreach (var item in items)
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
                else if (item is NestedVoiceRecoverySyntax)
                {
                    // LYS0010 recovery: the nested voice's braces are
                    // transparent — its content counts in THIS voice's bars.
                    for (int ci = 0; ci < item.SlotCount; ci++)
                        if (item.GetChild(ci) is MusicBlockSyntax inner)
                            AddItems(inner.Items);
                }
                else
                {
                    currentItems.Add(item);
                }
            }
        }
        AddItems(blockItems);

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

            case DrumNoteSyntax drum:
                var drumDuration = drum.Duration is { } dd
                    ? dd.ToFraction()
                    : defaultDuration;
                if (drum.Duration != null) defaultDuration = drumDuration;
                return drumDuration;

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

            case RepeatExpressionSyntax rep
                when rep.RepeatType.Text == "tremolo"
                  && int.TryParse(rep.Count.Text, out int tremCount):
            {
                // LILYPOND-REF: lily/chord-tremolo-iterator.cc — the tremolo
                // repeat's metric length is count × body (8 × c32 = a quarter).
                var body = Fraction.Zero;
                foreach (var bodyItem in rep.Body.Items)
                    body += ItemDuration(bodyItem, ref defaultDuration);
                return body * new Fraction(tremCount, 1);
            }

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