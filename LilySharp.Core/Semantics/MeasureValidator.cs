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

        foreach (var measure in measures)
        {
            var duration = CalculateMeasureDuration(measure.Items, ref defaultDuration);
            
            if (duration != _timeSignature && duration != Fraction.Zero)
            {
                if (duration < _timeSignature)
                {
                    // Incomplete measure
                    var span = GetSpan(measure.Items);
                    _diagnostics.Warning(span, DiagnosticCodes.MeasureIncomplete,
                        $"Measure duration {duration} is less than time signature {_timeSignature}");
                }
                else if (duration > _timeSignature)
                {
                    // Overfull measure
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
        {
            switch (item)
            {
                case NoteSyntax note:
                    var noteDuration = DurationCalculator.GetDuration(note, defaultDuration);
                    if (note.Duration != null)
                        defaultDuration = noteDuration;
                    total += noteDuration;
                    break;

                case RestSyntax rest:
                    var restDuration = DurationCalculator.GetDuration(rest, defaultDuration);
                    if (rest.Duration != null)
                        defaultDuration = restDuration;
                    total += restDuration;
                    break;

                case ChordSyntax chord:
                    var chordDuration = DurationCalculator.GetDuration(chord, defaultDuration);
                    if (chord.Duration != null)
                        defaultDuration = chordDuration;
                    total += chordDuration;
                    break;
            }
        }

        return total;
    }

    private static TextSpan GetSpan(List<SyntaxNode> items)
    {
        if (items.Count == 0)
            return new TextSpan(0, 0);

        int start = items[0].Position;
        var last = items[^1];
        int end = last.Position + last.FullWidth;
        return new TextSpan(start, end - start);
    }
}