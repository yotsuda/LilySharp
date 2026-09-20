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
    /// The span without leading/trailing trivia — the node's own text, which is also the
    /// address a READER of the source means when it points here.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TRIVIA IS THE FIRST AND LAST TERMINAL'S, NOT THIS NODE'S, and until 2026-08-29
    /// this read <c>Green.LeadingTriviaWidth</c> — a property only a TOKEN overrides, so a
    /// composite node (a note, a chord, a repeat) answered 0 however much whitespace stood in
    /// front of its first token. Same-line whitespace is the PREVIOUS token's trailing trivia,
    /// so the two agreed everywhere except at a line break: a node that OPENS a line reported
    /// the newline and the indent as part of itself. A diagnostic on an overfull measure whose
    /// first note stood at column 5 said column 1, and the SVG's <c>data-pos</c> for that note
    /// carried the offset of the whitespace, so clicking the note in the preview lit nothing
    /// up (reported 2026-08-29, scratch/ベースタブLy/Walk.lys line 15, `ees,1`).
    /// </remarks>
    public TextSpan Span => new(
        _position + Green.GetLeadingTriviaWidth(),
        Green.FullWidth - Green.GetLeadingTriviaWidth() - Green.GetTrailingTriviaWidth());

    /// <summary>
    /// The address a reader of the source means when it points at this node — the first
    /// character of the node's own text. Identical to <see cref="Span"/>'s start; the name
    /// exists so a call site says which of <see cref="Position"/> and this one it means.
    /// </summary>
    public int SourceStart => Span.Start;

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
            SyntaxKind.SlashNote => new SlashNoteSyntax((SlashNoteGreen)green, this, position),
            SyntaxKind.BareDuration => new BareDurationSyntax((BareDurationGreen)green, this, position),
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
            SyntaxKind.PaperDeclaration => new PaperDeclarationSyntax((PaperDeclarationGreen)green, this, position),
            SyntaxKind.LayoutDeclaration => new LayoutDeclarationSyntax((LayoutDeclarationGreen)green, this, position),
            SyntaxKind.TimeSignature => new TimeSignatureSyntax((TimeSignatureGreen)green, this, position),
            SyntaxKind.TempoDeclaration => new TempoDeclarationSyntax((TempoDeclarationGreen)green, this, position),
            SyntaxKind.PartialDeclaration => new PartialDeclarationSyntax((PartialDeclarationGreen)green, this, position),
            SyntaxKind.VariableDeclaration => new VariableDeclarationSyntax((VariableDeclarationGreen)green, this, position),
            SyntaxKind.PhraseDeclaration => new PhraseDeclarationSyntax((PhraseDeclarationGreen)green, this, position),
            SyntaxKind.VariableReference => new VariableReferenceSyntax((VariableReferenceGreen)green, this, position),
            SyntaxKind.RepeatExpression => new RepeatExpressionSyntax((RepeatExpressionGreen)green, this, position),
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
            SyntaxKind.ChordExtend => new ChordExtendSyntax((ChordExtendGreen)green, this, position),
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
    /// <remarks>
    /// ⚠️ This used to be a <c>yield return</c> method, so every caller paid for a state
    /// machine on the CALL, before a single child was looked at — see
    /// <see cref="ChildNodeList"/> for what that cost a keystroke.
    /// </remarks>
    public ChildNodeList ChildNodes() => new(this, 0, ChildNodeFilter.Any);

    /// <summary>The direct child nodes of one kind, in document order.</summary>
    /// <remarks>
    /// The named form of <c>ChildNodes().OfType&lt;T&gt;()</c>, which allocates two objects
    /// per ask because LINQ boxes the struct walk — see <see cref="TypedChildNodeList{T}"/>.
    /// </remarks>
    /// <typeparam name="T">The child kind to keep.</typeparam>
    public TypedChildNodeList<T> ChildNodesOfKind<T>() where T : SyntaxNode => new(this);

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
    /// The fully lazy sibling of <see cref="GreenSites"/>: the SAME green
    /// walk, the same pre-order, the same rule — but a match materializes NO
    /// red node at all. Each collected site is handed out as a
    /// <see cref="GreenSite"/> (green + absolute full-span start + a lazy
    /// spine), and the red is created only if a consumer reads
    /// <see cref="GreenSite.Node"/>. Use where most collected sites are never
    /// consumed — the collector's keystroke flat list adopts a prefix and
    /// splices a tail, so only the edit window's sites are ever red
    /// (HANDOFF's retired ▶ ⒭ (the incremental workstream, NOT §2 F ⒭) ⑵′ latter half).
    /// </summary>
    /// <remarks>
    /// Positions are accumulated from green full widths exactly as
    /// <see cref="GetChildPosition"/> computes them, so
    /// <c>site.Position == site.Node.FullSpan.Start</c> for every site — the
    /// checkpoint/splice address reads stay red-free. The spine links
    /// (<see cref="GreenSiteSpine"/>) are allocated only for ancestor frames
    /// that own at least one collected site, and hold no red until a site
    /// below them materializes.
    /// </remarks>
    internal IEnumerable<GreenSite> GreenSitesLazy(GreenSiteRule rule)
        => GreenSitesLazyFrom(rule, [new GreenWalkFrame(Green, 0, Position, -1, new GreenSiteSpine(this))]);

    /// <summary>
    /// The walk of <see cref="GreenSitesLazy"/> itself, started from a frame stack — the
    /// root's alone (<see cref="GreenSitesLazy"/>), or the stack
    /// <see cref="TryGreenSiteAt"/> leaves standing just past one site, so the walk resumes
    /// there in the same pre-order as if it had visited everything before. One spelling
    /// of the walk for both entries: the seek's continuation IS this loop.
    /// </summary>
    internal static IEnumerable<GreenSite> GreenSitesLazyFrom(GreenSiteRule rule, List<GreenWalkFrame> frames)
    {
        while (frames.Count > 0)
        {
            var (green, slot, pos, slotInParent, spine) = frames[^1];
            if (slot >= green.SlotCount)
            {
                frames.RemoveAt(frames.Count - 1);
                continue;
            }
            var child = green.GetSlot(slot);
            frames[^1] = new GreenWalkFrame(green, slot + 1, pos + (child?.FullWidth ?? 0), slotInParent, spine);
            if (child == null || child.IsToken)
                continue;
            var (collect, descend) = rule(child);
            if (collect)
            {
                // Build the spine links root→here (link objects only, no reds;
                // overlapping spines share the links already built).
                if (spine == null)
                {
                    LinkSpines(frames);
                    spine = frames[^1].Spine;
                }
                yield return new GreenSite(child, pos, spine!, slot);
            }
            if (descend && child.SlotCount > 0)
                frames.Add(new GreenWalkFrame(child, 0, pos, slot, null));
        }
    }

    /// <summary>Gives every frame of the stack its spine link (root→top; links already
    /// built are kept, so overlapping spines share them).</summary>
    private static void LinkSpines(List<GreenWalkFrame> frames)
    {
        for (int i = 1; i < frames.Count; i++)
        {
            if (frames[i].Spine == null)
                frames[i] = frames[i] with { Spine = new GreenSiteSpine(frames[i - 1].Spine!, frames[i].SlotInParent) };
        }
    }

    /// <summary>
    /// <see cref="GreenSitesLazy"/> entered at ONE site by its slot path from this node
    /// (<see cref="GreenSite.PathFrom"/>), without visiting anything before it: the site
    /// itself, and the frame stack the walk stands on just past it — hand that to
    /// <see cref="GreenSitesLazyFrom"/> and the walk continues exactly as the full one
    /// would from the same site (GatherSeekTests hold the two equal, site by site).
    /// False when the path does not lead to a site the rule collects through frames it
    /// descends: a slot out of range, a token or empty slot, a container the rule does
    /// not walk into, a leaf it does not collect — the caller then walks from the start.
    /// </summary>
    /// <remarks>
    /// WHY (session 402, R13): the collector's keystroke walk resumed at a recorded
    /// checkpoint by INDEX into its flat site list, so the list had to be gathered whole
    /// before the first adopted bar could be skipped — the whole part block's green walk
    /// on every keystroke (perf-plain1k 2.9 ms / 9000 sites, perf-fingbeam1k 12.2 ms /
    /// 33000 sites, min of 5, Debug), even when every bar was adopted. Descending a
    /// recorded slot path costs the widths of the slots BEFORE each step (one addition
    /// per sibling), no rule call and no site for anything skipped.
    /// </remarks>
    internal bool TryGreenSiteAt(GreenSiteRule rule, int[] path, out GreenSite site, out List<GreenWalkFrame> frames)
    {
        site = default;
        frames = null!;
        if (path.Length == 0)
            return false;
        var stack = new List<GreenWalkFrame>(path.Length + 1)
        {
            new GreenWalkFrame(Green, 0, Position, -1, new GreenSiteSpine(this)),
        };
        for (int depth = 0; depth < path.Length; depth++)
        {
            var frame = stack[^1];
            var green = frame.Green;
            int slot = path[depth];
            if (slot < 0 || slot >= green.SlotCount)
                return false;
            // The slot's absolute full-span start — the walk's NextPos as it reaches it.
            int pos = frame.NextPos;
            for (int s = 0; s < slot; s++)
                pos += green.GetSlot(s)?.FullWidth ?? 0;
            var child = green.GetSlot(slot);
            if (child == null || child.IsToken)
                return false;
            var (collect, descend) = rule(child);
            bool last = depth == path.Length - 1;
            if (last ? !collect : !descend)
                return false;
            // The frame as the walk leaves this slot behind.
            stack[^1] = frame with { NextSlot = slot + 1, NextPos = pos + child.FullWidth };
            if (!last)
            {
                if (child.SlotCount == 0)
                    return false;
                stack.Add(new GreenWalkFrame(child, 0, pos, slot, null));
                continue;
            }
            LinkSpines(stack);
            site = new GreenSite(child, pos, stack[^1].Spine!, slot);
            if (descend && child.SlotCount > 0)
                stack.Add(new GreenWalkFrame(child, 0, pos, slot, null));
            frames = stack;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns all descendant nodes in pre-order (each child before its own
    /// descendants, slots in order) — the same sequence the former recursive
    /// iterator produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the ROOT of a tree the answer is the tree's <see cref="DescendantIndex"/>:
    /// walked once, kept on the root, so the many root-level walks of the diagnostics
    /// pass are paid once per tree (its remarks have the numbers). Below the root the
    /// walk is <see cref="WalkDescendants"/>, lazy as ever.
    /// </para>
    /// <para>
    /// The return type is a struct so that <c>foreach</c> over the root's answer walks the
    /// index's ARRAY directly instead of through <see cref="IEnumerator{T}"/>: the
    /// diagnostics pass holds a dozen validators that each scan the whole flat list for a
    /// handful of nodes, and on perf-fingbeam1k's 234,030 nodes the interface dispatch was
    /// 0.56 ms of each such scan against 0.40 for the array (MEASURED, session 408). It
    /// still implements <see cref="IEnumerable{T}"/>, so LINQ and every existing call site
    /// are unchanged — <c>foreach</c> simply binds to the struct enumerator instead.
    /// </para>
    /// </remarks>
    public DescendantNodeList DescendantNodes()
        => this is CompilationUnitSyntax root
            ? new DescendantNodeList(root.Descendants.Nodes)
            : new DescendantNodeList(WalkDescendants());

    /// <summary>
    /// The pre-order walk itself — what <see cref="DescendantNodes()"/> is below the
    /// root, and what the root's <see cref="DescendantIndex"/> is built from.
    /// </summary>
    /// <remarks>
    /// Iterative with an explicit stack: the recursive version chained one
    /// iterator per tree level, so every element bubbled through O(depth)
    /// MoveNext calls — measured at ~13 ms (plain1k) / ~76 ms (fingbeam1k) per
    /// full-tree enumeration on a warm red tree (session 144), and the collect
    /// phase runs several such walks per keystroke.
    /// </remarks>
    internal IEnumerable<SyntaxNode> WalkDescendants()
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
    /// <see cref="DescendantNodes()"/>). On the root this is a bucket lookup in the
    /// tree's <see cref="DescendantIndex"/> — O(matches) — so a root-level
    /// <c>DescendantNodes().OfType&lt;T&gt;()</c> should be spelled this way instead.
    /// </summary>
    public IEnumerable<T> DescendantNodes<T>() where T : SyntaxNode
        => this is CompilationUnitSyntax root ? root.Descendants.OfType<T>() : WalkDescendants<T>();

    private IEnumerable<T> WalkDescendants<T>() where T : SyntaxNode
    {
        foreach (var node in WalkDescendants())
            if (node is T typed)
                yield return typed;
    }

    /// <summary>
    /// The descendants whose kind is one of <paramref name="kinds"/>, in pre-order — what
    /// a walk with a type switch over those kinds visits. On the root it is a merge of the
    /// <see cref="DescendantIndex"/>'s kind buckets (O(matches)); below the root, the walk
    /// with the kind test. A kind can be one <c>CreateRed</c> gives no class of its own
    /// (a <see cref="GenericSyntaxNode"/> kind), which a type cannot ask for.
    /// </summary>
    public IEnumerable<SyntaxNode> DescendantNodesOfKinds(params SyntaxKind[] kinds)
        => this is CompilationUnitSyntax root ? root.Descendants.OfKinds(kinds) : WalkDescendantsOfKinds(kinds);

    private IEnumerable<SyntaxNode> WalkDescendantsOfKinds(SyntaxKind[] kinds)
    {
        foreach (var node in WalkDescendants())
            if (Array.IndexOf(kinds, node.Kind) >= 0)
                yield return node;
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

/// <summary>One frame of the <see cref="SyntaxNode.GreenSitesLazy"/> walk: a green node
/// being iterated, the next slot to visit, that slot's absolute full-span start, the slot
/// this green occupies in ITS parent, and the frame's spine link once some collected site
/// needs it.</summary>
internal readonly record struct GreenWalkFrame(
    InternalSyntax.GreenNode Green, int NextSlot, int NextPos, int SlotInParent, GreenSiteSpine? Spine);

/// <summary>
/// A lazily materializable ancestor link of a <see cref="GreenSite"/>: one per
/// ancestor frame of the <see cref="SyntaxNode.GreenSitesLazy"/> walk that owns
/// at least one collected site. Holds no red node until a site below it is
/// consumed; then each step is one parent-cached <see cref="SyntaxNode.GetChild"/>,
/// so overlapping spines converge on the same red instances.
/// </summary>
internal sealed class GreenSiteSpine
{
    private readonly GreenSiteSpine? _parent;
    private readonly int _slotInParent;
    private SyntaxNode? _red;

    /// <summary>The walk root's link — its red exists already.</summary>
    internal GreenSiteSpine(SyntaxNode red) => _red = red;

    internal GreenSiteSpine(GreenSiteSpine parent, int slotInParent)
    {
        _parent = parent;
        _slotInParent = slotInParent;
    }

    /// <summary>This frame's red node, materialized on first demand through the
    /// parent chain.</summary>
    internal SyntaxNode Node => _red ??= _parent!.Node.GetChild(_slotInParent)!;

    internal GreenSiteSpine? Parent => _parent;
    internal int SlotInParent => _slotInParent;
    /// <summary>The walk root's red (the link with no parent); null on any other link.</summary>
    internal SyntaxNode? RootRed => _parent == null ? _red : null;
}

/// <summary>
/// A gathered syntax site held WITHOUT its red node: the green node, its
/// absolute full-span start, and the lazy spine to materialize the red through
/// — the (green, position) flat-list element of HANDOFF's retired ▶ ⒭ (the incremental workstream, NOT §2 F ⒭) ⑵′. Reading
/// <see cref="Node"/> is the ONLY thing that creates a red; kind and address
/// reads (checkpoint capture, splice targeting, marker/peek gating) are free.
/// A site can also wrap an ALREADY materialized node (a synthetic phrase
/// marker, a form-fabricated barline, a direct red child) — then
/// <see cref="Node"/> just returns it.
/// </summary>
internal readonly struct GreenSite
{
    internal readonly InternalSyntax.GreenNode Green;

    /// <summary>Absolute full-span start — equals <c>Node.FullSpan.Start</c>
    /// without materializing anything.</summary>
    internal readonly int Position;

    private readonly GreenSiteSpine? _parent;
    private readonly int _slot;
    private readonly SyntaxNode? _red;

    internal GreenSite(InternalSyntax.GreenNode green, int position, GreenSiteSpine parent, int slot)
    {
        Green = green;
        Position = position;
        _parent = parent;
        _slot = slot;
        _red = null;
    }

    /// <summary>Wraps an existing red node (synthetic markers, fabricated
    /// barlines, direct children already materialized by their producer).</summary>
    internal GreenSite(SyntaxNode red)
    {
        Green = red.Green;
        Position = red.Position;
        _parent = null;
        _slot = 0;
        _red = red;
    }

    internal SyntaxKind Kind => Green.Kind;

    /// <summary>The site's red node — created on first read (the consumption
    /// points: ProcessMusicNode, the attached-mark peek, variable-reference
    /// expansion). Parent-cached <see cref="SyntaxNode.GetChild"/> makes every
    /// later read the same instance.</summary>
    internal SyntaxNode Node => _red ?? _parent!.Node.GetChild(_slot)!;

    /// <summary>
    /// The site's slot path from <paramref name="root"/> — the walk root of the
    /// <see cref="SyntaxNode.GreenSitesLazy"/> that gathered it — as
    /// <see cref="SyntaxNode.TryGreenSiteAt"/> reads it back; null for a site that wraps
    /// a preset red (a synthetic marker, a fabricated bar line, a direct child) or was
    /// gathered under another root (a phrase body's expansion). Spine reads only — no red
    /// is materialized.
    /// </summary>
    internal int[]? PathFrom(SyntaxNode root)
    {
        if (_parent == null)
            return null;
        int depth = 1;
        var link = _parent;
        while (link.Parent != null)
        {
            depth++;
            link = link.Parent;
        }
        if (!ReferenceEquals(link.RootRed, root))
            return null;
        var path = new int[depth];
        path[depth - 1] = _slot;
        int i = depth - 2;
        for (var s = _parent; s.Parent != null; s = s.Parent)
            path[i--] = s.SlotInParent;
        return path;
    }
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