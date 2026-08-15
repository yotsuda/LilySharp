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
/// <c>ossia NAME</c>, <c>tab NAME</c>, <c>chords NAME</c>, <c>lyrics NAME</c>,
/// grand-staff staves) and midi part renders.
/// Powers the editor's "rename a part" refactor, so a part can be renamed
/// everywhere from any single occurrence.
/// </summary>
/// <remarks>
/// The reference-token selection mirrors
/// <see cref="Svg.Collector.RenderSpecParser"/> (ParseStaff / ParseOssia /
/// ParseTab): the same rules pick the part token so a rename can never touch a
/// token the compiler reads as something else — a clef word, a bare per-score
/// display name, or the chord part named after <c>with chords</c>.
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
                // ⚠️ THE ROW FAMILY IS DELIBERATELY ABSENT. `chords NAME` / `lyrics NAME`
                // name their own tracks, and the language server ALREADY resolves those in
                // full — declaration, row reference and `with chords|lyrics` clause alike
                // (LilySharpLanguageServer.Navigation, ChordNameTokens / LyricsNameTokens).
                // Collecting them here as part names took the caret away from that resolver
                // and answered with a SMALLER set: the `with` clauses dropped out of rename
                // and find-references, and go-to-definition on a chord row stopped jumping
                // to its block (three LSP tests said so). What the validator needs is a
                // different question anyway — see RowReferenceTokens.
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
    /// ⚠️ THE ROW FAMILY IS NOT IN HERE, AND THAT IS THE POINT — see
    /// <see cref="RowReferenceTokens"/>. A <c>chords NAME</c> row names a chord track,
    /// which a <c>part NAME { … }</c> header does not declare and a staff cannot render;
    /// folding the two lists together would make <c>staff prog</c> legal on a chord track,
    /// which is exactly the empty staff LYS1007 exists to catch.
    /// </remarks>
    public static IReadOnlyList<SyntaxTokenNode> ReferenceTokens(SyntaxNode root)
    {
        var tokens = new List<SyntaxTokenNode>();
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
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
            }
        }
        return tokens;
    }

    /// <summary>
    /// A score's ROW render targets and the tracks they can resolve to: <c>chords NAME</c>
    /// and <c>lyrics NAME</c> references, each tagged with which of the two namespaces it
    /// names, plus the declared names of each namespace.
    /// </summary>
    /// <param name="References">The row targets, in document order, tagged
    /// <c>IsChordRow</c>.</param>
    /// <param name="ChordTracks">Names declared by <c>chords NAME { … }</c>.</param>
    /// <param name="LyricTracks">Names declared by <c>lyrics NAME { … }</c>.</param>
    public readonly record struct RowReferences(
        IReadOnlyList<(SyntaxTokenNode Token, bool IsChordRow)> References,
        HashSet<string> ChordTracks,
        HashSet<string> LyricTracks);

    /// <summary>
    /// The score's ROW render targets — <c>chords NAME</c> and <c>lyrics NAME</c> — each
    /// tagged with which track it names, because the two are separate namespaces and a
    /// reference must be checked against its own.
    /// </summary>
    /// <remarks>
    /// A chord row's name is declared by a NAMED <c>chords NAME { … }</c> block and a lyric
    /// row's by a named <c>lyrics NAME { … }</c> block; neither is a
    /// <c>part NAME { … }</c> header or a section-body part block, which is why they need
    /// their own list rather than a place in <see cref="ReferenceTokens"/>.
    /// <para>
    /// ⚠️ <c>staff NAME with chords C</c> and <c>staff NAME with lyrics L</c> name the same
    /// two namespaces and are NOT collected here: those tails are cut off by
    /// <see cref="StaffPartToken"/> and have never been validated. Adding them is the same
    /// shape of change as this one and is deliberately not made here — see
    /// docs/HANDOFF.md.
    /// </para>
    /// <para>
    /// ⚠️ NOT A SECOND SPELLING OF THE LANGUAGE SERVER'S <c>ChordNameTokens</c>, which asks
    /// a different question: it wants EVERY occurrence of a chord-track name (block, row and
    /// <c>with</c> clause) so a rename can rewrite them all, while this wants only the ones
    /// a score has to be able to RESOLVE. The two share the row shape and nothing else.
    /// </para>
    /// <para>
    /// ⚠️ THE DECLARATIONS COME BACK IN THE SAME CALL, on purpose: they are found in the
    /// same walk. A named <c>chords NAME { … }</c> block declares a chord track and a named
    /// <c>lyrics NAME { … }</c> block a lyric one; an UNNAMED block of either kind attaches
    /// to a co-written staff instead of standing as a row, so it declares no name — and slot
    /// 1 of an unnamed block is the opening BRACE, which a flat reading collects as a track
    /// called "{". Asking for the two halves separately would walk the tree twice for one
    /// question that has one answer.
    /// </para>
    /// </remarks>
    public static RowReferences Rows(SyntaxNode root)
    {
        var refs = new List<(SyntaxTokenNode, bool)>();
        var chords = new HashSet<string>();
        var lyrics = new HashSet<string>();
        foreach (var node in root.DescendantNodes())
            switch (node)
            {
                case ChordRowRenderSyntax:
                    if (RowPartToken(node) is { } ct)
                        refs.Add((ct, true));
                    break;
                case LyricsRowRenderSyntax:
                    if (RowPartToken(node) is { } lt)
                        refs.Add((lt, false));
                    break;
                case ChordPartBlockSyntax c when c.PartName is { Length: > 0 } cn:
                    chords.Add(cn);
                    break;
                case LyricsBlockSyntax l when l.VoiceName is { Length: > 0 } ln:
                    lyrics.Add(ln);
                    break;
            }
        return new RowReferences(refs, chords, lyrics);
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
    /// (label suppression) and the per-score display string, cut off the
    /// <c>with chords …</c> tail (a different, chord-part name), then take the
    /// first token that is not a clef keyword. LILYPOND-REF is n/a — this mirrors
    /// RenderSpecParser.ParseStaff.
    /// </summary>
    private static SyntaxTokenNode? StaffPartToken(StaffRenderSyntax staff)
    {
        var toks = TargetTokens(staff);
        toks.RemoveAll(t => t.Kind == SyntaxKind.Tilde);
        int si = toks.FindIndex(t => t.Kind == SyntaxKind.StringLiteral);
        if (si >= 0)
            toks.RemoveAt(si);
        int wi = toks.FindIndex(t => t.Kind == SyntaxKind.WithKeyword);
        if (wi >= 0)
            toks = toks.GetRange(0, wi);
        if (toks.Count == 0)
            return null;
        int partIdx = IsClefKeyword(toks[0].Kind) ? 1 : 0;
        return partIdx < toks.Count ? toks[partIdx] : null;
    }

    /// <summary>The part token for <c>ossia</c>: always the last target token
    /// (RenderSpecParser.ParseOssia — <c>ossia [clef] part</c> takes the last slot, and an
    /// ossia carries no <c>as</c> selector to strip).</summary>
    private static SyntaxTokenNode? LastTargetToken(SyntaxNode node)
    {
        var toks = TargetTokens(node);
        return toks.Count > 0 ? toks[^1] : null;
    }

    /// <summary>
    /// The part token in a <c>tab</c> render item: cut off the <c>with chords …</c> tail
    /// (a different, chord-part name), then the trailing <c>as numbers | full</c> style
    /// selector, and take the last token that remains. A leading token before it is the
    /// tuning override, not the part.
    /// </summary>
    /// <remarks>
    /// Mirrors RenderSpecParser.ParseTab, which strips BOTH tails before reading
    /// <c>toks[^1]</c> — including the guards (a <c>with</c> tail shorter than
    /// <c>with chords NAME</c>, or a trailing <c>as</c> with no mode word, is left alone
    /// there, so it is left alone here). Taking the last token flat instead read the
    /// selector word as the part name: <c>tab m as numbers</c> reported LYS1007
    /// "Undefined part: 'numbers'" on a perfectly good score — the committed fixture
    /// test/tab-as-numbers.lys among them — and a rename from any occurrence would have
    /// rewritten the selector rather than the part.
    /// </remarks>
    private static SyntaxTokenNode? TabPartToken(TabRenderSyntax tab)
    {
        var toks = TargetTokens(tab);
        int wi = toks.FindIndex(t => t.Kind == SyntaxKind.WithKeyword);
        if (wi >= 0 && toks.Count >= wi + 3)
            toks = toks.GetRange(0, wi);
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
    /// ⚠️ NOT THE SAME NAME AS <c>staff … with chords NAME</c>, which
    /// <see cref="StaffPartToken"/> deliberately cuts off. That one names a chord row
    /// ATTACHED to a staff and is resolved elsewhere; this one is a row of its own, and
    /// MeasureCollector matches it against the same <c>voiceName</c> a <c>staff</c> target
    /// does (`renderSpec.Items.OfType&lt;ChordRowSpec&gt;().Any(c =&gt; c.PartName ==
    /// voiceName)`). Same namespace, so the same check.
    /// <para>
    /// ⚠️ A ZERO-WIDTH TOKEN IS NOT A REFERENCE. When the name fails to parse the slot holds
    /// a missing token whose text is empty; reporting it would put "Undefined part: ''"
    /// under a syntax error that already says what is wrong. Skipped, exactly as the
    /// language server skips it.
    /// </para>
    /// </remarks>
    private static SyntaxTokenNode? RowPartToken(SyntaxNode node)
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
