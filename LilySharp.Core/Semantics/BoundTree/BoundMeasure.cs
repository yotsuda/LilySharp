using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.BoundTree;

/// <summary>
/// A bound measure with resolved music content.
/// </summary>
public sealed record BoundMeasure : BoundMusic
{
    /// <summary>
    /// Creates a bound measure.
    /// </summary>
    public BoundMeasure(
        SyntaxNode? syntax,
        ImmutableArray<BoundMusic> items,
        BarlineType startBarline,
        BarlineType endBarline,
        string? sectionLabel)
    {
        _syntax = syntax;
        Items = items;
        StartBarline = startBarline;
        EndBarline = endBarline;
        SectionLabel = sectionLabel;
    }

    private readonly SyntaxNode? _syntax;

    /// <inheritdoc/>
    public override SyntaxNode? Syntax => _syntax;

    /// <summary>The music items in this measure.</summary>
    public ImmutableArray<BoundMusic> Items { get; }

    /// <summary>The barline at the start of this measure.</summary>
    public BarlineType StartBarline { get; }

    /// <summary>The barline at the end of this measure.</summary>
    public BarlineType EndBarline { get; }

    /// <summary>Optional section label for this measure.</summary>
    public string? SectionLabel { get; }
}
