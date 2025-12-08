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

/// <summary>
/// Score declaration: score "title" { ... }
/// </summary>
internal sealed class ScoreDeclarationGreen : GreenSyntaxNode
{
    public ScoreDeclarationGreen(
        SyntaxToken scoreKeyword,
        SyntaxToken? title,
        SyntaxToken openBrace,
        GreenNode?[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.ScoreDeclaration, [scoreKeyword, title, openBrace, .. members, closeBrace])
    {
    }
}

/// <summary>
/// Part declaration: part Name "display" { ... }
/// </summary>
internal sealed class PartDeclarationGreen : GreenSyntaxNode
{
    public PartDeclarationGreen(
        SyntaxToken partKeyword,
        SyntaxToken? name,
        SyntaxToken? displayName,
        SyntaxToken openBrace,
        GreenNode?[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.PartDeclaration, [partKeyword, name, displayName, openBrace, .. members, closeBrace])
    {
    }
}

/// <summary>
/// Staff declaration: staff Name { ... }
/// </summary>
internal sealed class StaffDeclarationGreen : GreenSyntaxNode
{
    public StaffDeclarationGreen(
        SyntaxToken staffKeyword,
        SyntaxToken? name,
        SyntaxToken openBrace,
        GreenNode?[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.StaffDeclaration, [staffKeyword, name, openBrace, .. members, closeBrace])
    {
    }
}

/// <summary>
/// Property assignment: name: value
/// </summary>
internal sealed class PropertyAssignmentGreen : GreenSyntaxNode
{
    public PropertyAssignmentGreen(SyntaxToken name, SyntaxToken colon, GreenNode?[] valueTokens)
        : base(SyntaxKind.PropertyAssignment, [name, colon, .. valueTokens])
    {
    }
}

/// <summary>
/// Metadata declaration: title "value" or tempo 120
/// </summary>
internal sealed class MetadataDeclarationGreen : GreenSyntaxNode
{
    public MetadataDeclarationGreen(SyntaxToken keyword, GreenNode?[] valueTokens)
        : base(SyntaxKind.MetadataDeclaration, [keyword, .. valueTokens])
    {
    }
}

/// <summary>
/// Variable declaration: let name = expr
/// </summary>
internal sealed class VariableDeclarationGreen : GreenSyntaxNode
{
    public VariableDeclarationGreen(
        SyntaxToken letKeyword,
        SyntaxToken name,
        SyntaxToken equals,
        GreenNode expression)
        : base(SyntaxKind.VariableDeclaration, [letKeyword, name, equals, expression])
    {
    }
}

/// <summary>
/// Variable reference: use name or $name
/// </summary>
internal sealed class VariableReferenceGreen : GreenSyntaxNode
{
    public VariableReferenceGreen(SyntaxToken keyword, SyntaxToken name)
        : base(SyntaxKind.VariableReference, [keyword, name])
    {
    }
}

/// <summary>
/// Articulation: @staccato, @accent, etc.
/// </summary>
internal sealed class ArticulationGreen : GreenSyntaxNode
{
    public ArticulationGreen(SyntaxToken atToken, SyntaxToken nameToken)
        : base(SyntaxKind.Articulation, [atToken, nameToken])
    {
    }
}

/// <summary>
/// Dynamic mark: \p, \f, \cresc, etc.
/// </summary>
internal sealed class DynamicGreen : GreenSyntaxNode
{
    public DynamicGreen(SyntaxToken backslashToken, SyntaxToken dynamicToken)
        : base(SyntaxKind.Dynamic, [backslashToken, dynamicToken])
    {
    }
}

/// <summary>
/// Repeat expression: repeat volta 2 { ... } alternative { ... }
/// </summary>
internal sealed class RepeatExpressionGreen : GreenSyntaxNode
{
    public RepeatExpressionGreen(
        SyntaxToken repeatKeyword,
        SyntaxToken repeatType,
        SyntaxToken count,
        MusicBlockGreen body,
        AlternativeClauseGreen? alternative)
        : base(SyntaxKind.RepeatExpression, [repeatKeyword, repeatType, count, body, alternative])
    {
    }
}

/// <summary>
/// Alternative clause: alternative { { ... } { ... } }
/// </summary>
internal sealed class AlternativeClauseGreen : GreenSyntaxNode
{
    public AlternativeClauseGreen(
        SyntaxToken alternativeKeyword,
        SyntaxToken openBrace,
        GreenNode?[] alternatives,
        SyntaxToken closeBrace)
        : base(SyntaxKind.AlternativeClause, [alternativeKeyword, openBrace, .. alternatives, closeBrace])
    {
    }
}

/// <summary>
/// Parallel expression: << expr \\ expr >>
/// </summary>
internal sealed class ParallelExpressionGreen : GreenSyntaxNode
{
    public ParallelExpressionGreen(
        SyntaxToken openAngle,
        GreenNode?[] voices,
        SyntaxToken closeAngle)
        : base(SyntaxKind.ParallelExpression, [openAngle, .. voices, closeAngle])
    {
    }
}

/// <summary>
/// Key signature: key c major, key g minor
/// </summary>
internal sealed class KeySignatureGreen : GreenSyntaxNode
{
    public KeySignatureGreen(
        SyntaxToken keyKeyword,
        GreenNode pitch,
        SyntaxToken mode)
        : base(SyntaxKind.KeySignature, [keyKeyword, pitch, mode])
    {
    }
}

/// <summary>
/// Clef declaration: clef treble, clef bass
/// </summary>
internal sealed class ClefDeclarationGreen : GreenSyntaxNode
{
    public ClefDeclarationGreen(
        SyntaxToken clefKeyword,
        SyntaxToken clefName)
        : base(SyntaxKind.ClefDeclaration, [clefKeyword, clefName])
    {
    }
}

/// <summary>
/// Tuplet expression: tuplet 3/2 { ... }
/// </summary>
internal sealed class TupletExpressionGreen : GreenSyntaxNode
{
    public TupletExpressionGreen(
        SyntaxToken tupletKeyword,
        SyntaxToken numerator,
        SyntaxToken slash,
        SyntaxToken denominator,
        GreenNode body)
        : base(SyntaxKind.TupletExpression, [tupletKeyword, numerator, slash, denominator, body])
    {
    }
}

/// <summary>
/// Grace expression: grace { notes } or acciaccatura { notes }
/// </summary>
internal sealed class GraceExpressionGreen : GreenSyntaxNode
{
    public GraceExpressionGreen(
        SyntaxToken graceKeyword,
        GreenNode body)
        : base(SyntaxKind.GraceExpression, [graceKeyword, body])
    {
    }
}

/// <summary>
/// Lyrics block: lyrics { text -- text }
/// </summary>
internal sealed class LyricsBlockGreen : GreenSyntaxNode
{
    public LyricsBlockGreen(
        SyntaxToken lyricsKeyword,
        SyntaxToken openBrace,
        GreenNode?[] syllables,
        SyntaxToken closeBrace)
        : base(SyntaxKind.LyricsBlock, [lyricsKeyword, openBrace, .. syllables, closeBrace])
    {
    }
}