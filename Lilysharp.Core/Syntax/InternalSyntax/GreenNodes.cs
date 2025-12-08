namespace Lilysharp.Core.Syntax.InternalSyntax;

/// <summary>
/// A list of Green nodes (used for children).
/// </summary>
internal sealed class GreenNodeList : GreenNode
{
    private readonly GreenNode[] _nodes;

    public GreenNodeList(GreenNode[] nodes)
        : base(SyntaxKind.None, nodes)
    {
        _nodes = nodes;
    }

    public int Count => _nodes.Length;
    public GreenNode this[int index] => _nodes[index];
}

/// <summary>
/// Base class for syntax nodes (non-token internal nodes).
/// </summary>
internal abstract class GreenSyntaxNode : GreenNode
{
    protected GreenSyntaxNode(SyntaxKind kind, GreenNode?[] children)
        : base(kind, children)
    {
    }
}

/// <summary>
/// Compilation unit - the root node.
/// </summary>
internal sealed class CompilationUnitGreen : GreenSyntaxNode
{
    public CompilationUnitGreen(GreenNode?[] members, SyntaxToken endOfFile)
        : base(SyntaxKind.CompilationUnit, [.. members, endOfFile])
    {
    }
}

/// <summary>
/// A music block: { ... }
/// </summary>
internal sealed class MusicBlockGreen : GreenSyntaxNode
{
    public MusicBlockGreen(SyntaxToken openBrace, GreenNode?[] items, SyntaxToken closeBrace)
        : base(SyntaxKind.MusicBlock, [openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// Relative expression: relative c' { ... }
/// </summary>
internal sealed class RelativeExpressionGreen : GreenSyntaxNode
{
    public RelativeExpressionGreen(
        SyntaxToken relativeKeyword,
        PitchGreen basePitch,
        MusicBlockGreen body)
        : base(SyntaxKind.RelativeExpression, [relativeKeyword, basePitch, body])
    {
    }
}

/// <summary>
/// A pitch with optional octave marks: c, cis', des,,
/// </summary>
internal sealed class PitchGreen : GreenSyntaxNode
{
    public PitchGreen(SyntaxToken pitchToken, GreenNode?[] octaveMarks)
        : base(SyntaxKind.Pitch, [pitchToken, .. octaveMarks])
    {
    }
}

/// <summary>
/// A duration: 4, 8., 16..
/// </summary>
internal sealed class DurationGreen : GreenSyntaxNode
{
    public DurationGreen(SyntaxToken number, GreenNode?[] dots)
        : base(SyntaxKind.Duration, [number, .. dots])
    {
    }
}

/// <summary>
/// A note: pitch + optional duration + articulations
/// </summary>
internal sealed class NoteGreen : GreenSyntaxNode
{
    public NoteGreen(PitchGreen pitch, DurationGreen? duration, GreenNode?[] articulations)
        : base(SyntaxKind.Note, [pitch, duration, .. articulations])
    {
    }
}

/// <summary>
/// A rest: r, s, R + optional duration
/// </summary>
internal sealed class RestGreen : GreenSyntaxNode
{
    public RestGreen(SyntaxToken restToken, DurationGreen? duration)
        : base(SyntaxKind.Rest, [restToken, duration])
    {
    }
}

/// <summary>
/// A chord: < pitch pitch ... > + optional duration
/// </summary>
internal sealed class ChordGreen : GreenSyntaxNode
{
    public ChordGreen(
        SyntaxToken openAngle,
        GreenNode?[] pitches,
        SyntaxToken closeAngle,
        DurationGreen? duration,
        GreenNode?[] articulations)
        : base(SyntaxKind.Chord, [openAngle, .. pitches, closeAngle, duration, .. articulations])
    {
    }
}

/// <summary>
/// A barline: |, ||, |., |:, :|
/// </summary>
internal sealed class BarlineGreen : GreenSyntaxNode
{
    public BarlineGreen(SyntaxToken barToken)
        : base(SyntaxKind.Barline, [barToken])
    {
    }
}

/// <summary>
/// A tie: ~
/// </summary>
internal sealed class TieGreen : GreenSyntaxNode
{
    public TieGreen(SyntaxToken tilde)
        : base(SyntaxKind.Tie, [tilde])
    {
    }
}

/// <summary>
/// Slur markers: ( or )
/// </summary>
internal sealed class SlurGreen : GreenSyntaxNode
{
    public SlurGreen(SyntaxToken parenToken)
        : base(SyntaxKind.Slur, [parenToken])
    {
    }
}