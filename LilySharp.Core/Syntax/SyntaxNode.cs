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

using System.Collections.Generic;
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
            SyntaxKind.ChordRepetition => new ChordRepetitionSyntax((ChordRepetitionGreen)green, this, position),
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
            SyntaxKind.CondensedStaffRender => new CondensedStaffRenderSyntax((CondensedStaffRenderGreen)green, this, position),
            SyntaxKind.CombinedStaffRender => new CombinedStaffRenderSyntax((CombinedStaffRenderGreen)green, this, position),
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
    /// Returns the DIRECT child nodes only. For declarations the grammar produces
    /// exclusively at the top level (Parser.ParseTopLevelItem: <c>score</c>/render,
    /// <c>form</c>, <c>part</c>, <c>phrase</c>, …) enumerate the root's ChildNodes
    /// instead of <see cref="DescendantNodes"/> — a descendant walk re-enumerates
    /// every music body to find nodes that can only sit at depth 1, and those
    /// discovery walks were about half of the keystroke's collect cost
    /// (session 144: plain1k 41 of 82 ms, fingbeam1k 208 of 525 ms).
    /// </summary>
    public IEnumerable<SyntaxNode> ChildNodes()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetChild(i) is { } child)
                yield return child;
        }
    }

    /// <summary>
    /// The green-tree finder behind the collector's definition/music gathers and
    /// the semantics readers: visits every green node under this node in the
    /// same pre-order <see cref="DescendantNodes()"/> yields red nodes, and
    /// materializes a red node — with its full Parent chain, through the parent
    /// chain's cached <see cref="GetChild"/> — only where <paramref name="rule"/>
    /// collects. Tokens are never offered to the rule and never descended into
    /// (they have no child slots). Use instead of
    /// <c>DescendantNodes().OfType&lt;T&gt;()</c> on a hot path: the red walk
    /// materializes a red wrapper for EVERY descendant (tokens included) just to
    /// type-test it, which made each such scan an O(whole-tree-allocation) pass
    /// (HANDOFF §1 sessions 152–153, red-creation counters).
    /// </summary>
    internal IEnumerable<SyntaxNode> GreenSites(GreenSiteRule rule)
    {
        // Frame: a green node being iterated, the next slot to visit, the slot it
        // occupies in ITS parent, and its red node once materialized. The red is
        // filled lazily: only when a match somewhere below needs the spine.
        var frames = new List<(GreenNode Green, int NextSlot, int SlotInParent, SyntaxNode? Red)>
        {
            (Green, 0, -1, this),
        };
        while (frames.Count > 0)
        {
            var (green, slot, slotInParent, red) = frames[^1];
            if (slot >= green.SlotCount)
            {
                frames.RemoveAt(frames.Count - 1);
                continue;
            }
            frames[^1] = (green, slot + 1, slotInParent, red);
            var child = green.GetSlot(slot);
            if (child == null || child.IsToken)
                continue;
            var (collect, descend) = rule(child);
            if (collect)
            {
                // Materialize the spine root→parent (the red children are cached
                // on their parents, so overlapping spines are created once).
                for (int i = 1; i < frames.Count; i++)
                {
                    if (frames[i].Red == null)
                        frames[i] = (frames[i].Green, frames[i].NextSlot, frames[i].SlotInParent,
                            frames[i - 1].Red!.GetChild(frames[i].SlotInParent));
                }
                var childRed = frames[^1].Red!.GetChild(slot)!;
                yield return childRed;
                if (descend && child.SlotCount > 0)
                    frames.Add((child, 0, slot, childRed));
            }
            else if (descend && child.SlotCount > 0)
            {
                frames.Add((child, 0, slot, null));
            }
        }
    }

    /// <summary>
    /// The common <see cref="GreenSites"/> shape: every descendant of exactly
    /// <paramref name="kind"/>, pre-order, reds materialized only per match —
    /// the green-walk replacement for <c>DescendantNodes().OfType&lt;T&gt;()</c>
    /// (kinds are 1:1 with red types; see <see cref="CreateRed"/>). Callers keep
    /// an <c>OfType&lt;T&gt;()</c> on the result so the type test stays the
    /// authority over the kind name.
    /// </summary>
    internal IEnumerable<SyntaxNode> KindSites(SyntaxKind kind)
        => GreenSites(g => (g.Kind == kind, Descend: true));

    /// <summary>
    /// Returns all descendant nodes in pre-order (each child before its own
    /// descendants, slots in order) — the same sequence the former recursive
    /// iterator produced.
    /// </summary>
    /// <remarks>
    /// Iterative with an explicit stack: the recursive version chained one
    /// iterator per tree level, so every element bubbled through O(depth)
    /// MoveNext calls — measured at ~13 ms (plain1k) / ~76 ms (fingbeam1k) per
    /// full-tree enumeration on a warm red tree (session 144), and the collect
    /// phase runs several such walks per keystroke.
    /// </remarks>
    public IEnumerable<SyntaxNode> DescendantNodes()
    {
        var stack = new Stack<(SyntaxNode Node, int Slot)>();
        var current = (Node: this, Slot: 0);
        while (true)
        {
            if (current.Slot >= current.Node.SlotCount)
            {
                if (stack.Count == 0)
                    yield break;
                current = stack.Pop();
                continue;
            }

            var child = current.Node.GetChild(current.Slot);
            current.Slot++;
            if (child == null)
                continue;

            yield return child;
            if (child.SlotCount > 0)
            {
                stack.Push(current);
                current = (child, 0);
            }
        }
    }

    /// <summary>
    /// Returns all descendant nodes of a specific type (pre-order, as
    /// <see cref="DescendantNodes()"/>).
    /// </summary>
    public IEnumerable<T> DescendantNodes<T>() where T : SyntaxNode
    {
        foreach (var node in DescendantNodes())
            if (node is T typed)
                yield return typed;
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

/// <summary>Per-node decision of a <see cref="SyntaxNode.GreenSites"/> walk:
/// whether to materialize and yield this (non-token) green's red node, and
/// whether to walk into its children. Called once per green node in pre-order.</summary>
internal delegate (bool Collect, bool Descend) GreenSiteRule(InternalSyntax.GreenNode green);

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