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

using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Syntax;

/// <summary>
/// Base class for red syntax nodes - position-aware facades over green nodes.
/// </summary>
public abstract class SyntaxNode
{
    private readonly SyntaxNode? _parent;
    private readonly int _position;
    internal readonly GreenNode Green;

    // Red children are created lazily and CACHED (Roslyn-style): repeated
    // GetChild(i) returns the same instance, so repeated tree walks (LSP
    // validators, collectors) don't re-allocate the red tree every time.
    // Both arrays are published with CompareExchange so concurrent walkers
    // converge on a single instance without locks.
    private SyntaxNode?[]? _children;
    private int[]? _childPositions;

    internal SyntaxNode(GreenNode green, SyntaxNode? parent, int position)
    {
        Green = green;
        _parent = parent;
        _position = position;
    }

    /// <summary>
    /// The kind of this syntax node.
    /// </summary>
    public SyntaxKind Kind => Green.Kind;

    /// <summary>
    /// The parent node, or null for root.
    /// </summary>
    public SyntaxNode? Parent => _parent;

    /// <summary>
    /// The absolute position in the source text.
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// The full width including trivia.
    /// </summary>
    public int FullWidth => Green.FullWidth;

    /// <summary>
    /// The width without trivia.
    /// </summary>
    public int Width => Green.Width;

    /// <summary>
    /// The span of this node in the source text.
    /// </summary>
    public TextSpan FullSpan => new(_position, Green.FullWidth);

    /// <summary>
    /// The span without leading/trailing trivia.
    /// </summary>
    public TextSpan Span => new(_position + Green.LeadingTriviaWidth, Green.Width);

    /// <summary>
    /// The number of child slots.
    /// </summary>
    public int SlotCount => Green.SlotCount;

    /// <summary>
    /// Gets the child at the specified index, creating the red node on first
    /// access and returning the cached instance afterwards.
    /// </summary>
    public SyntaxNode? GetChild(int index)
    {
        var greenChild = Green.GetSlot(index);
        if (greenChild == null)
            return null;

        var children = _children;
        if (children == null)
        {
            children = new SyntaxNode?[Green.SlotCount];
            children = Interlocked.CompareExchange(ref _children, children, null) ?? children;
        }

        var existing = children[index];
        if (existing != null)
            return existing;

        var created = CreateRed(greenChild, GetChildPosition(index));
        return Interlocked.CompareExchange(ref children[index], created, null) ?? created;
    }

    /// <summary>
    /// Child positions are cumulative sums of green full widths; computing
    /// them once kills the former O(n²) walk over preceding siblings.
    /// </summary>
    private int GetChildPosition(int index)
    {
        var positions = _childPositions;
        if (positions == null)
        {
            int n = Green.SlotCount;
            positions = new int[n];
            int pos = _position;
            for (int i = 0; i < n; i++)
            {
                positions[i] = pos;
                var child = Green.GetSlot(i);
                if (child != null)
                    pos += child.FullWidth;
            }
            positions = Interlocked.CompareExchange(ref _childPositions, positions, null) ?? positions;
        }
        return positions[index];
    }

    /// <summary>
    /// Creates a red node for a green node.
    /// </summary>
    private SyntaxNode CreateRed(GreenNode green, int position)
    {
        if (green.IsToken)
            return new SyntaxTokenNode((SyntaxToken)green, this, position);

        return green.Kind switch
        {
            SyntaxKind.CompilationUnit => new CompilationUnitSyntax((CompilationUnitGreen)green, this, position),
            SyntaxKind.MusicBlock => new MusicBlockSyntax((MusicBlockGreen)green, this, position),
            SyntaxKind.Note => new NoteSyntax((NoteGreen)green, this, position),
            SyntaxKind.DrumNote => new DrumNoteSyntax((DrumNoteGreen)green, this, position),
            SyntaxKind.NestedVoiceRecovery => new NestedVoiceRecoverySyntax((NestedVoiceRecoveryGreen)green, this, position),
            SyntaxKind.DrummapDeclaration => new DrummapDeclarationSyntax((DrummapDeclarationGreen)green, this, position),
            SyntaxKind.Rest => new RestSyntax((RestGreen)green, this, position),
            SyntaxKind.Chord => new ChordSyntax((ChordGreen)green, this, position),
            SyntaxKind.Arpeggio => new ArpeggioSyntax((ArpeggioGreen)green, this, position),
            SyntaxKind.Pitch => new PitchSyntax((PitchGreen)green, this, position),
            SyntaxKind.ChordDegree => new ScaleDegreeSyntax((ScaleDegreeGreen)green, this, position),
            SyntaxKind.Duration => new DurationSyntax((DurationGreen)green, this, position),
            SyntaxKind.Barline => new BarlineSyntax((BarlineGreen)green, this, position),
            SyntaxKind.Break => new BreakSyntax((BreakGreen)green, this, position),
            SyntaxKind.Tie => new TieSyntax((TieGreen)green, this, position),
            SyntaxKind.Slur => new SlurSyntax((SlurGreen)green, this, position),
            SyntaxKind.BeamMarker => new BeamMarkerSyntax((BeamMarkerGreen)green, this, position),
            SyntaxKind.InlineVolta => new InlineVoltaSyntax((InlineVoltaGreen)green, this, position),
            SyntaxKind.PartDeclaration => new PartDeclarationSyntax((PartDeclarationGreen)green, this, position),
            SyntaxKind.PropertyAssignment => new PropertyAssignmentSyntax((PropertyAssignmentGreen)green, this, position),
            SyntaxKind.MetadataDeclaration => new MetadataDeclarationSyntax((MetadataDeclarationGreen)green, this, position),
            SyntaxKind.FontDeclaration => new FontDeclarationSyntax((FontDeclarationGreen)green, this, position),
            SyntaxKind.TimeSignature => new TimeSignatureSyntax((TimeSignatureGreen)green, this, position),
            SyntaxKind.TempoDeclaration => new TempoDeclarationSyntax((TempoDeclarationGreen)green, this, position),
            SyntaxKind.PartialDeclaration => new PartialDeclarationSyntax((PartialDeclarationGreen)green, this, position),
            SyntaxKind.VariableDeclaration => new VariableDeclarationSyntax((VariableDeclarationGreen)green, this, position),
            SyntaxKind.PhraseDeclaration => new PhraseDeclarationSyntax((PhraseDeclarationGreen)green, this, position),
            SyntaxKind.VariableReference => new VariableReferenceSyntax((VariableReferenceGreen)green, this, position),
            SyntaxKind.RepeatExpression => new RepeatExpressionSyntax((RepeatExpressionGreen)green, this, position),
            SyntaxKind.AlternativeClause => new AlternativeClauseSyntax((AlternativeClauseGreen)green, this, position),
            SyntaxKind.ParallelExpression => new ParallelExpressionSyntax((ParallelExpressionGreen)green, this, position),
            SyntaxKind.KeySignature => new KeySignatureSyntax((KeySignatureGreen)green, this, position),
            SyntaxKind.ClefDeclaration => new ClefDeclarationSyntax((ClefDeclarationGreen)green, this, position),
            SyntaxKind.OctaveDirective => new OctaveDirectiveSyntax((OctaveDirectiveGreen)green, this, position),
            SyntaxKind.TupletExpression => new TupletExpressionSyntax((TupletExpressionGreen)green, this, position),
            SyntaxKind.GraceExpression => new GraceExpressionSyntax((GraceExpressionGreen)green, this, position),
            SyntaxKind.CueExpression => new CueExpressionSyntax((CueExpressionGreen)green, this, position),
            SyntaxKind.LyricsBlock => new LyricsBlockSyntax((LyricsBlockGreen)green, this, position),
            SyntaxKind.ChordPartBlock => new ChordPartBlockSyntax((ChordPartBlockGreen)green, this, position),
            SyntaxKind.ChordEntry => new ChordEntrySyntax((ChordEntryGreen)green, this, position),
            SyntaxKind.Articulation => new ArticulationSyntax((ArticulationGreen)green, this, position),
            SyntaxKind.Dynamic => new DynamicSyntax((DynamicGreen)green, this, position),
            SyntaxKind.StringNumberAnnotation => new StringNumberAnnotationSyntax((StringNumberAnnotationGreen)green, this, position),

            // Section/Structure/Render declarations
            SyntaxKind.SectionDeclaration => new SectionDeclarationSyntax((SectionDeclarationGreen)green, this, position),
            SyntaxKind.UsingDirective => new UsingDirectiveSyntax((UsingDirectiveGreen)green, this, position),
            SyntaxKind.PartBlock => new PartBlockSyntax((PartBlockGreen)green, this, position),
            SyntaxKind.FormDeclaration => new FormDeclarationSyntax((FormDeclarationGreen)green, this, position),
            SyntaxKind.FormRepeatBlock => new FormRepeatBlockSyntax((FormRepeatBlockGreen)green, this, position),
            SyntaxKind.FormAlternative => new FormAlternativeSyntax((FormAlternativeGreen)green, this, position),
            SyntaxKind.SectionReference => new SectionReferenceSyntax((SectionReferenceGreen)green, this, position),
            SyntaxKind.NavigationMark => new NavigationMarkSyntax((NavigationMarkGreen)green, this, position),
            SyntaxKind.MusicMark => new MusicMarkSyntax((MusicMarkGreen)green, this, position),
            SyntaxKind.CustomText => new CustomTextSyntax((CustomTextGreen)green, this, position),
            SyntaxKind.RenderDeclaration => new RenderDeclarationSyntax((RenderDeclarationGreen)green, this, position),
            SyntaxKind.StaffRender => new StaffRenderSyntax((StaffRenderGreen)green, this, position),
            SyntaxKind.ChordRowRender => new ChordRowRenderSyntax((ChordRowRenderGreen)green, this, position),
            SyntaxKind.LyricsRowRender => new LyricsRowRenderSyntax((LyricsRowRenderGreen)green, this, position),
            SyntaxKind.GrandStaffRender => new GrandStaffRenderSyntax((GrandStaffRenderGreen)green, this, position),
            SyntaxKind.TabRender => new TabRenderSyntax((TabRenderGreen)green, this, position),
            SyntaxKind.OssiaRender => new OssiaRenderSyntax((OssiaRenderGreen)green, this, position),
            SyntaxKind.MidiPartRender => new MidiPartRenderSyntax((MidiPartRenderGreen)green, this, position),

            // Override/Revert
            SyntaxKind.OverrideDeclaration => new OverrideDeclarationSyntax((OverrideDeclarationGreen)green, this, position),
            SyntaxKind.RevertDeclaration => new RevertDeclarationSyntax((RevertDeclarationGreen)green, this, position),
            SyntaxKind.OnceModifier => new OnceModifierSyntax((OnceModifierGreen)green, this, position),

            _ => new GenericSyntaxNode(green, this, position)
        };
    }

    /// <summary>
    /// Returns the full text including trivia.
    /// </summary>
    public string ToFullString() => Green.ToFullString();

    /// <summary>
    /// Returns all descendant nodes.
    /// </summary>
    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var child = GetChild(i);
            if (child != null)
            {
                yield return child;
                foreach (var descendant in child.DescendantNodes())
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// Returns all descendant nodes of a specific type.
    /// </summary>
    public IEnumerable<T> DescendantNodes<T>() where T : SyntaxNode
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var child = GetChild(i);
            if (child != null)
            {
                if (child is T typed)
                    yield return typed;
                foreach (var descendant in child.DescendantNodes<T>())
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// True when any ancestor (walking the parent chain, excluding this node)
    /// is of type <typeparamref name="T"/>. The single source for the many
    /// <c>IsInsideXxx</c> parent-chain walks in the collector and exporters.
    /// </summary>
    public bool IsInside<T>() where T : SyntaxNode
    {
        for (var p = Parent; p != null; p = p.Parent)
            if (p is T)
                return true;
        return false;
    }

    /// <summary>
    /// Find the node at the given position.
    /// </summary>
    public SyntaxNode? FindNode(int position)
    {
        if (position < Position || position >= Position + FullWidth)
            return null;

        for (int i = 0; i < SlotCount; i++)
        {
            var child = GetChild(i);
            if (child != null)
            {
                var found = child.FindNode(position);
                if (found != null)
                    return found;
            }
        }

        return this;
    }

    /// <summary>Returns the node kind and its full span for debugging.</summary>
    public override string ToString() => $"{Kind} [{Position}..{Position + FullWidth})";
}

/// <summary>
/// A red node wrapper for tokens.
/// </summary>
public sealed class SyntaxTokenNode : SyntaxNode
{
    internal SyntaxTokenNode(SyntaxToken token, SyntaxNode? parent, int position)
        : base(token, parent, position)
    {
    }

    /// <summary>
    /// The text of this token.
    /// </summary>
    public string Text => Green.Text;
}

/// <summary>
/// Generic syntax node for untyped access.
/// </summary>
public sealed class GenericSyntaxNode : SyntaxNode
{
    internal GenericSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}