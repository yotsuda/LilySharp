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
                        _diagnostics.Warning(span, DiagnosticCodes.MeasureIncomplete,
                            $"Measure duration {duration} is less than time signature {_timeSignature}");
                    }
                }
                else if (duration > _timeSignature)
                {
                    // Overfull measure — always worth flagging.
                    var span = GetSpan(measure.Items);
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