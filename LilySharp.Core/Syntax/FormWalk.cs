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

namespace LilySharp.Core.Syntax;

/// <summary>
/// THE ONE READER of a form's item spellings, for the walks that replay or export
/// the form (MidiExporter.PlayForm, MusicXmlExporter.WalkForm,
/// LilyPondExporter.AppendFormItems). A form item has eight spellings
/// (Parser.Form.cs ParseFormItem), and before this reader existed each walk
/// re-derived the classification by hand — which is how a silent <c>~Name</c>
/// reference came to be dropped by MIDI (exported ZERO notes while the page
/// engraved the section), how the volta-ending label rule was mirrored off a
/// broken arm, and why every repeat/volta/D.S. change needed the same edit in
/// three places. The reader hands each consumer typed items; WHAT each output
/// does with an item (play it, bracket it, flatten it) stays with that output.
/// </summary>
/// <remarks>
/// What is normalized here, once:
/// <list type="bullet">
/// <item>A silent <c>~Name</c> reference (no red node; the name is slot 1) is a
/// <see cref="SectionRef"/> with <c>Silent = true</c> — the tilde hides the LABEL,
/// never the music. A malformed silent reference (no name token) degrades to
/// <see cref="Other"/>, preserving each consumer's old degenerate handling.</item>
/// <item>A repeat block's children arrive in document order, its bar-line tokens
/// as typed items (<see cref="RepeatStart"/>/<see cref="RepeatEnd"/>/
/// <see cref="BothBar"/> — the <c>:|:</c> divider stays ONE item; expanding it to
/// <c>:| |:</c> is the LilyPond twin's concern and MUST NOT happen a second
/// time), and its <c>:|*N</c> play count read once (<see cref="Repeat.PlayCount"/>,
/// default 2 — the token pair itself is consumed here and not re-yielded).</item>
/// <item>A volta ending outside any repeat block is still an <see cref="Ending"/>:
/// all three consumers give it the same LilyPond reading (played once, as its
/// plain section — lily/alternative-sequence-iterator.cc:83-84 defaults
/// repeat-count to 1), but their label rules differ, so the judgment stays
/// with them and the reader only classifies.</item>
/// <item>A one-sided <c>:|</c> written at form level (repeat from the beginning
/// of the piece — user decision, 2026-08-15) is <see cref="LoneRepeatEnd"/>;
/// tokens are never yielded, and anything else is <see cref="Other"/> so a
/// consumer that warns on unknown items (the LilyPond twin) still sees it.</item>
/// </list>
/// ⚠️ The page's own form walk (MeasureCollector.Form.cs ProcessForm /
/// ProcessRepeatBlock) is deliberately NOT a consumer yet: it interleaves
/// classification with measure building and bar synchronization, and folding it
/// is a separate step with the engraving as its observer.
/// </remarks>
internal static class FormWalk
{
    internal abstract record Item;

    /// <summary>A plain or silent (<c>~</c>) section reference. For a silent one,
    /// <see cref="DisplayLabel"/> is null — the grammar gives it no label slot.</summary>
    internal sealed record SectionRef(
        string Name, string? DisplayLabel, bool Silent, SyntaxNode Node) : Item;

    /// <summary>A <c>|: … :|</c> block with its children in document order and its
    /// <c>:|*N</c> play count (2 when absent).</summary>
    internal sealed record Repeat(
        FormRepeatBlockSyntax Node, int PlayCount, IReadOnlyList<Item> Children) : Item;

    /// <summary>A volta ending <c>[1. Name]</c>, inside a repeat block or lone. The
    /// syntax node carries the whole surface (numbers, label, <c>~</c>).</summary>
    internal sealed record Ending(FormAlternativeSyntax Node) : Item;

    /// <summary>The block's opening <c>|:</c> token.</summary>
    internal sealed record RepeatStart(SyntaxTokenNode Token) : Item;

    /// <summary>The block's closing <c>:|</c> token.</summary>
    internal sealed record RepeatEnd(SyntaxTokenNode Token) : Item;

    /// <summary>The block's <c>:|:</c> divider token (one item — see the class remarks).</summary>
    internal sealed record BothBar(SyntaxTokenNode Token) : Item;

    /// <summary>A one-sided <c>:|</c> barline at form level: repeat the piece from
    /// its beginning, once.</summary>
    internal sealed record LoneRepeatEnd(BarlineSyntax Node) : Item;

    /// <summary>Any other non-token item (<c>break</c>, a navigation mark, an
    /// <c>@</c> mark, <c>_"text"</c>, a malformed reference…) — classified nowhere
    /// so that a consumer's catch-all (warn, pass through, ignore) still runs.</summary>
    internal sealed record Other(SyntaxNode Node) : Item;

    /// <summary>The form's items in document order (tokens are never yielded).</summary>
    internal static IReadOnlyList<Item> Read(SyntaxNode container)
    {
        var items = new List<Item>();
        for (int i = 0; i < container.SlotCount; i++)
            Classify(container.GetChild(i), items, insideRepeat: false);
        return items;
    }

    private static void Classify(SyntaxNode? child, List<Item> items, bool insideRepeat)
    {
        switch (child)
        {
            case SectionReferenceSyntax r:
                items.Add(new SectionRef(r.SectionName, r.DisplayLabel, Silent: false, r));
                break;
            case { Kind: SyntaxKind.SilentSectionReference }
                    when child.GetChild(1) is SyntaxTokenNode name:
                items.Add(new SectionRef(name.Text, null, Silent: true, child));
                break;
            case FormRepeatBlockSyntax rb:
                items.Add(ReadRepeat(rb));
                break;
            case FormAlternativeSyntax alt:
                items.Add(new Ending(alt));
                break;
            // The block's own bar-line tokens (only meaningful inside a repeat —
            // matched before the generic token skip below).
            case SyntaxTokenNode { Kind: SyntaxKind.RepeatStartBar } t when insideRepeat:
                items.Add(new RepeatStart(t));
                break;
            case SyntaxTokenNode { Kind: SyntaxKind.RepeatEndBar } t when insideRepeat:
                items.Add(new RepeatEnd(t));
                break;
            case SyntaxTokenNode { Kind: SyntaxKind.RepeatBothBar } t when insideRepeat:
                items.Add(new BothBar(t));
                break;
            // The one-sided ':|' is a BARLINE node at form level (the block's ':|'
            // is a token, matched above); inside a block it has no rewind meaning
            // and stays Other, which every consumer already handled as such.
            case BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatEndBar } bar when !insideRepeat:
                items.Add(new LoneRepeatEnd(bar));
                break;
            case null or SyntaxTokenNode: // keywords, braces, the consumed :|*N pair
                break;
            default:
                items.Add(new Other(child));
                break;
        }
    }

    private static Repeat ReadRepeat(FormRepeatBlockSyntax block)
    {
        var children = new List<Item>();
        for (int i = 0; i < block.SlotCount; i++)
            Classify(block.GetChild(i), children, insideRepeat: true);
        return new Repeat(block, PlayCount(block), children);
    }

    /// <summary>
    /// The <c>:|*3</c> play count on a repeat block, or 2 when it is absent.
    /// </summary>
    /// <remarks>
    /// The parser keeps it as the <c>*</c> + integer token pair sitting on the
    /// block's end bar line (Parser.Form.cs ParseFormRepeatBlock), not as a node —
    /// the same spelling and the same place an inline <c>:|*3</c> carries it.
    /// </remarks>
    internal static int PlayCount(FormRepeatBlockSyntax block)
    {
        for (int i = 0; i + 1 < block.SlotCount; i++)
            if (block.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Asterisk }
                && block.GetChild(i + 1) is SyntaxTokenNode count
                && int.TryParse(count.Text, out int n) && n >= 1)
                return n;
        return 2;
    }
}
