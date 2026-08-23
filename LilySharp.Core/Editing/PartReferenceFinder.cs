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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// Resolves every occurrence of a <c>part</c> name in a document: its declaration
/// (<c>part NAME { … }</c>) and each place that references it — section-body part
/// blocks (<c>NAME { … }</c>), score render targets (<c>staff [clef] NAME</c>,
/// <c>ossia NAME</c>, <c>tab NAME</c>, grand-staff staves, and the bare members of
/// <c>condensedStaff { … }</c> / <c>combinedStaff { … }</c>) and midi part renders.
/// Powers the editor's "rename a part" refactor, so a part can be renamed
/// everywhere from any single occurrence.
/// </summary>
/// <remarks>
/// The reference-token selection mirrors
/// <see cref="Svg.Collector.RenderSpecParser"/> (ParseStaff / ParseOssia /
/// ParseTab): the same rules pick the part token so a rename can never touch a
/// token the compiler reads as something else — a clef word or a bare per-score
/// display name.
/// </remarks>
public static class PartReferenceFinder
{
    /// <summary>
    /// Every part-name token in the tree — the declaration name plus each
    /// reference token — in document order. A reference to an undefined part is
    /// still returned (rename works purely by matching name text).
    /// </summary>
    public static IReadOnlyList<SyntaxTokenNode> AllPartNameTokens(SyntaxNode root)
    {
        var tokens = new List<SyntaxTokenNode>();
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case PartDeclarationSyntax part:
                    tokens.Add(part.Name);
                    break;
                case PartBlockSyntax block:
                    tokens.Add(block.PartName);
                    break;
                case MidiPartRenderSyntax midi:
                    tokens.Add(midi.PartName);
                    break;
                case StaffRenderSyntax staff:
                    if (StaffPartToken(staff) is { } st)
                        tokens.Add(st);
                    break;
                case OssiaRenderSyntax ossia:
                    if (LastTargetToken(ossia) is { } ot)
                        tokens.Add(ot);
                    break;
                case TabRenderSyntax tab:
                    if (TabPartToken(tab) is { } tt)
                        tokens.Add(tt);
                    break;
                // `condensedStaff { a b … }` / `combinedStaff { a b }` hold BARE part names
                // — no clef, no display name, no tail to cut off — so the node's own
                // accessor is the selection, and it is the same one RenderSpecParser reads.
                case CondensedStaffRenderSyntax condensed:
                    tokens.AddRange(condensed.PartNameTokens);
                    break;
                case CombinedStaffRenderSyntax combined:
                    tokens.AddRange(combined.PartNameTokens);
                    break;
                // ⚠️ THE ROW FAMILY IS DELIBERATELY ABSENT. `chords NAME` / `lyrics NAME`
                // name their own tracks, and the language server ALREADY resolves those in
                // full — declaration and row reference alike
                // (LilySharpLanguageServer.Navigation, ChordNameTokens / LyricsNameTokens).
                // Collecting them here as part names took the caret away from that resolver
                // and answered with a SMALLER set (three LSP tests said so). What the
                // validator needs is a different question anyway — see Tracks.
            }
        }
        return tokens;
    }

    /// <summary>
    /// Only the tokens that REFERENCE a part (score render targets: <c>staff</c> /
    /// <c>ossia</c> / <c>tab</c> / midi part), NOT the declarations. A reference whose
    /// name matches no <c>part NAME { … }</c> header and no section-body part block is an
    /// undefined-part error — see <c>SymbolReferenceValidator</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TRACK FAMILY IS NOT IN HERE, AND THAT IS THE POINT — see
    /// <see cref="Tracks"/>. A <c>chords NAME</c> row names a chord track,
    /// which a <c>part NAME { … }</c> header does not declare and a staff cannot render;
    /// folding the two lists together would make <c>staff prog</c> legal on a chord track,
    /// which is exactly the empty staff LYS1007 exists to catch.
    /// </remarks>
    public static IReadOnlyList<SyntaxTokenNode> ReferenceTokens(SyntaxNode root)
    {
        var tokens = new List<SyntaxTokenNode>();
        foreach (var node in root.DescendantNodes())
            CollectReferenceTokens(node, tokens);
        return tokens;
    }

    /// <summary>
    /// The part references ONE node spells, appended to <paramref name="into"/> — the same
    /// question <see cref="ReferenceTokens"/> asks, asked of a single node.
    /// </summary>
    /// <remarks>
    /// ⚠️ Split out so a caller already walking the tree pays nothing to ask. The language
    /// server's semantic tokens need this list to decide which part names in a
    /// <c>score { }</c> resolve and may be coloured, and they run on the keystroke path
    /// (RULES §7.9), where an extra <c>DescendantNodes()</c> pass is the whole cost. Both
    /// callers reach the same six spellings through this one switch, so a seventh render
    /// form has one place to be added rather than two — and a spelling that reached only
    /// one of them would colour a name the diagnostics call undefined, or leave plain a
    /// name they accept.
    /// </remarks>
    public static void CollectReferenceTokens(SyntaxNode node, List<SyntaxTokenNode> into)
    {
        switch (node)
        {
            case MidiPartRenderSyntax midi:
                into.Add(midi.PartName);
                break;
            case StaffRenderSyntax staff:
                if (StaffPartToken(staff) is { } st)
                    into.Add(st);
                break;
            case OssiaRenderSyntax ossia:
                if (LastTargetToken(ossia) is { } ot)
                    into.Add(ot);
                break;
            case TabRenderSyntax tab:
                if (TabPartToken(tab) is { } tt)
                    into.Add(tt);
                break;
            case CondensedStaffRenderSyntax condensed:
                into.AddRange(condensed.PartNameTokens);
                break;
            case CombinedStaffRenderSyntax combined:
                into.AddRange(combined.PartNameTokens);
                break;
        }
    }

    /// <summary>
    /// The name token by which this node DECLARES a part, or null. Two spellings declare
    /// one: the header <c>part NAME { … }</c> and the section-body block
    /// <c>NAME { … }</c>, which carries the part's music and so lets a staff render it
    /// with no header at all.
    /// </summary>
    /// <remarks>
    /// ⚠️ The set these build is what a reference has to be found in — it is what
    /// <c>SymbolReferenceValidator</c> checks LYS1007 against and what the language server
    /// checks before colouring a name in a <c>score { }</c>. One predicate for both, so the
    /// editor cannot paint a name blue in the same pass that underlines it as undefined.
    /// </remarks>
    public static SyntaxTokenNode? DeclaredName(SyntaxNode node) => node switch
    {
        PartDeclarationSyntax header => header.Name,
        PartBlockSyntax block => block.PartName,
        _ => null,
    };

    /// <summary>
    /// Every place a score names a chord or lyric TRACK, and the tracks those names can
    /// resolve to: the row targets <c>chords NAME</c> / <c>lyrics NAME</c> (top level and
    /// inside group bodies alike), each tagged with which of the two namespaces it names,
    /// plus the declared names of each namespace.
    /// </summary>
    /// <param name="References">The track references, in document order, tagged
    /// <c>IsChord</c>.</param>
    /// <param name="ChordTracks">Names declared by <c>chords NAME { … }</c>.</param>
    /// <param name="LyricTracks">Names declared by <c>lyrics NAME { … }</c>.</param>
    public readonly record struct TrackReferences(
        IReadOnlyList<(SyntaxTokenNode Token, bool IsChord)> References,
        HashSet<string> ChordTracks,
        HashSet<string> LyricTracks);

    /// <summary>
    /// The score's chord- and lyric-TRACK references — the rows <c>chords NAME</c> /
    /// <c>lyrics NAME</c> — each tagged with which track it names, because the two are
    /// separate namespaces and a reference must be checked against its own.
    /// </summary>
    /// <remarks>
    /// A chord track's name is declared by a NAMED <c>chords NAME { … }</c> block and a lyric
    /// track's by a named <c>lyrics NAME { … }</c> block; neither is a
    /// <c>part NAME { … }</c> header or a section-body part block, which is why they need
    /// their own list rather than a place in <see cref="ReferenceTokens"/>.
    /// <para>
    /// ⚠️ NOT A SECOND SPELLING OF THE LANGUAGE SERVER'S <c>ChordNameTokens</c>, which asks
    /// a different question: it wants EVERY occurrence of a chord-track name (block and
    /// row) so a rename can rewrite them all, while this wants only the ones
    /// a score has to be able to RESOLVE — the declaring block is not one of those.
    /// </para>
    /// <para>
    /// ⚠️ THE DECLARATIONS COME BACK IN THE SAME CALL, on purpose: they are found in the
    /// same walk. A named <c>chords NAME { … }</c> block declares a chord track and a
    /// named <c>lyrics NAME { … }</c> block a lyric one; a nameless block (an error
    /// since LYS0032, kept by recovery) declares no name — and slot 1 of one is the
    /// opening BRACE, which a flat reading collects as a track called "{". Asking for
    /// the two halves separately would walk the tree twice for one question that has
    /// one answer.
    /// </para>
    /// </remarks>
    public static TrackReferences Tracks(SyntaxNode root)
    {
        var refs = new List<(SyntaxTokenNode, bool)>();
        var chords = new HashSet<string>();
        var lyrics = new HashSet<string>();
        foreach (var node in root.DescendantNodes())
        {
            if (ReferencedTrackName(node) is { } reference)
                refs.Add((reference.Token, reference.IsChord));
            if (DeclaredTrackName(node) is { } declaration)
                (declaration.IsChord ? chords : lyrics).Add(declaration.Token.Text);
        }
        return new TrackReferences(refs, chords, lyrics);
    }

    /// <summary>A chord- or lyric-track name and which of the two namespaces it is in —
    /// they are separate, so a name must be checked against its own.</summary>
    public readonly record struct TrackName(SyntaxTokenNode Token, bool IsChord);

    /// <summary>The track name this node DECLARES, or null. A named
    /// <c>chords NAME { … }</c> block declares a chord track and a named
    /// <c>lyrics NAME { … }</c> block a lyric one; a nameless block (LYS0032, kept by
    /// recovery) declares nothing — its slot 1 is the opening BRACE, and a flat reading
    /// collects that as a track called "{".</summary>
    public static TrackName? DeclaredTrackName(SyntaxNode node) => node switch
    {
        ChordPartBlockSyntax c when c.NameToken is { } t => new TrackName(t, true),
        LyricsBlockSyntax l when l.NameToken is { } t => new TrackName(t, false),
        _ => null,
    };

    /// <summary>The track name this node REFERS to, or null — the score rows
    /// <c>chords NAME</c> / <c>lyrics NAME</c>. A row inside a staff group is the same
    /// node as a top-level one, so both arrive here. (A row is the only track reference a
    /// score can spell: the <c>with</c> clauses this once scanned are retired, LYS0031.)
    /// </summary>
    public static TrackName? ReferencedTrackName(SyntaxNode node) => node switch
    {
        ChordRowRenderSyntax when RowTargetToken(node) is { } t => new TrackName(t, true),
        LyricsRowRenderSyntax when RowTargetToken(node) is { } t => new TrackName(t, false),
        _ => null,
    };

    /// <summary>The token naming the part a lyric track sings, or null. Both spellings
    /// state the same track property — the definition <c>lyrics verse sings melody { … }</c>
    /// and the score row <c>lyrics verse sings melody</c> — so both answer here.</summary>
    /// <remarks>
    /// ⚠️ This target resolves against a WIDER set than the score's part references do:
    /// parts AND the named voices of a <c>&lt;&lt; … &gt;&gt;</c>, which is what
    /// <c>LyricSingsValidator</c> checks (LYS6011). A caller colouring it must use
    /// <see cref="CollectVoiceNames"/> as well, or it will leave plain a name the
    /// diagnostics accept.
    /// </remarks>
    public static SyntaxTokenNode? SingsTargetToken(SyntaxNode node) => node switch
    {
        LyricsBlockSyntax block => block.SingsTargetToken,
        LyricsRowRenderSyntax row => row.SingsTargetToken,
        _ => null,
    };

    /// <summary>The named voices this node introduces, appended to
    /// <paramref name="into"/> — the second half of what a <c>sings</c> target may name.
    /// </summary>
    public static void CollectVoiceNames(SyntaxNode node, HashSet<string> into)
    {
        if (node is not ParallelExpressionSyntax parallel)
            return;
        foreach (var (name, _) in parallel.NamedVoices)
            if (name is { Length: > 0 })
                into.Add(name);
    }

    /// <summary>
    /// The part-name token whose span contains <paramref name="offset"/> (end
    /// inclusive, so a caret just past the identifier still resolves), or null
    /// when the offset is not on a part declaration or reference.
    /// </summary>
    public static SyntaxTokenNode? PartNameTokenAt(SyntaxNode root, int offset)
    {
        foreach (var tok in AllPartNameTokens(root))
            if (offset >= tok.Span.Start && offset <= tok.Span.End)
                return tok;
        return null;
    }

    /// <summary>All part-name tokens whose text equals <paramref name="name"/>.</summary>
    public static IReadOnlyList<SyntaxTokenNode> Occurrences(SyntaxNode root, string name)
        => AllPartNameTokens(root).Where(t => t.Text == name).ToList();

    // ── reference-token selection (mirrors RenderSpecParser) ──

    /// <summary>
    /// The part token in a <c>staff</c> render item: skip a leading <c>~</c>
    /// (label suppression) and the per-score display string, then take the
    /// first token that is not a clef keyword (a trailing <c>as lines N</c>
    /// selector sits after the part, so it cannot shadow it). LILYPOND-REF is
    /// n/a — this mirrors RenderSpecParser.ParseStaff.
    /// </summary>
    private static SyntaxTokenNode? StaffPartToken(StaffRenderSyntax staff)
    {
        var toks = TargetTokens(staff);
        toks.RemoveAll(t => t.Kind == SyntaxKind.Tilde);
        int si = toks.FindIndex(t => t.Kind == SyntaxKind.StringLiteral);
        if (si >= 0)
            toks.RemoveAt(si);
        if (toks.Count == 0)
            return null;
        int partIdx = IsClefKeyword(toks[0].Kind) ? 1 : 0;
        return partIdx < toks.Count ? toks[partIdx] : null;
    }

    /// <summary>The part token for <c>ossia</c>: cut the trailing <c>as lines N</c>
    /// selector (the SAME cut RenderSpecParser.ParseOssia makes — one home,
    /// <see cref="Svg.Collector.RenderSpecParser.CutLinesSelector"/>), then the
    /// last target token (<c>ossia [clef] part</c> takes the last slot).</summary>
    private static SyntaxTokenNode? LastTargetToken(SyntaxNode node)
    {
        var toks = TargetTokens(node);
        Svg.Collector.RenderSpecParser.CutLinesSelector(toks);
        return toks.Count > 0 ? toks[^1] : null;
    }

    /// <summary>
    /// The part token in a <c>tab</c> render item: cut off the trailing
    /// <c>as numbers | full</c> style selector, and take the last token that
    /// remains. A leading token before it is the tuning override, not the part.
    /// </summary>
    /// <remarks>
    /// Mirrors RenderSpecParser.ParseTab, which strips BOTH tails before reading
    /// <c>toks[^1]</c> (a trailing <c>as</c> with no mode word is left alone
    /// there, so it is left alone here). Taking the last token flat instead read the
    /// selector word as the part name: <c>tab m as numbers</c> reported LYS1007
    /// "Undefined part: 'numbers'" on a perfectly good score — the committed fixture
    /// test/tab-as-numbers.lys among them — and a rename from any occurrence would have
    /// rewritten the selector rather than the part.
    /// </remarks>
    private static SyntaxTokenNode? TabPartToken(TabRenderSyntax tab)
    {
        var toks = TargetTokens(tab);
        int asIdx = toks.FindIndex(t => string.Equals(t.Text, "as", System.StringComparison.Ordinal));
        if (asIdx >= 0 && asIdx + 1 < toks.Count)
            toks = toks.GetRange(0, asIdx);
        return toks.Count > 0 ? toks[^1] : null;
    }

    /// <summary>
    /// The part token of a ROW render item — <c>chords NAME [as …]</c> or
    /// <c>lyrics NAME</c>. Both spell the part in slot 1, which is exactly what
    /// RenderSpecParser reads (<c>ChordRowRenderSyntax.PartName</c> /
    /// <c>LyricsRowRenderSyntax.PartName</c>) and what the language server already treats as
    /// a reference (LilySharpLanguageServer.Navigation).
    /// </summary>
    /// <remarks>
    /// The row is a track of its own, and MeasureCollector matches it against the same
    /// <c>voiceName</c> a <c>staff</c> target does (`renderSpec.Items.OfType&lt;ChordRowSpec&gt;()
    /// .Any(c =&gt; c.PartName == voiceName)`).
    /// <para>
    /// ⚠️ A ZERO-WIDTH TOKEN IS NOT A REFERENCE. When the name fails to parse the slot holds
    /// a missing token whose text is empty; reporting it would put "Undefined part: ''"
    /// under a syntax error that already says what is wrong. Skipped, exactly as the
    /// language server skips it.
    /// </para>
    /// </remarks>
    private static SyntaxTokenNode? RowTargetToken(SyntaxNode node)
        => node.GetChild(1) is SyntaxTokenNode t && t.Text.Length > 0 ? t : null;


    /// <summary>The render item's tokens, skipping the leading keyword and braces
    /// (RenderSpecParser.RenderTargetTokens).</summary>
    private static List<SyntaxTokenNode> TargetTokens(SyntaxNode node)
    {
        var toks = new List<SyntaxTokenNode>();
        for (int i = 1; i < node.SlotCount; i++)
            if (node.GetChild(i) is SyntaxTokenNode t
                && t.Kind is not (SyntaxKind.OpenBrace or SyntaxKind.CloseBrace))
                toks.Add(t);
        return toks;
    }

    private static bool IsClefKeyword(SyntaxKind kind) => kind is
        SyntaxKind.TrebleKeyword or SyntaxKind.BassKeyword or SyntaxKind.AltoKeyword
        or SyntaxKind.TenorKeyword or SyntaxKind.Treble8Keyword or SyntaxKind.Treble8UpKeyword
        or SyntaxKind.SopranoKeyword or SyntaxKind.MezzoSopranoKeyword
        or SyntaxKind.BaritoneKeyword or SyntaxKind.Bass8Keyword or SyntaxKind.PercussionKeyword;
}
