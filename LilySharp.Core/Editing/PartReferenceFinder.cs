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
/// <c>ossia NAME</c>, <c>tab NAME</c>, grand-staff staves) and midi part renders.
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
