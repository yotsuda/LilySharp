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
/// a form-level <c>:|:</c> is that AND a <see cref="Repeat"/> the next form-level
/// <c>:|</c> closes (<see cref="GroupDividerRepeats"/>, 2026-09-10);
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

    /// <summary>A plain or silent (<c>~</c>) section reference. Both spellings can park a
    /// quoted <see cref="DisplayLabel"/>; whether it is PRINTED is the label rule's
    /// question (Semantics.SectionLabelRule), not this reader's.
    /// <paramref name="OctaveOffset"/> is the net shift from the reference's own trailing
    /// <c>'</c>/<c>,</c> marks, which BOTH spellings carry (the tilde hides the label,
    /// never the music) and which every consumer has to apply to the play's frame.</summary>
    internal sealed record SectionRef(
        string Name, string? DisplayLabel, bool Silent, SyntaxNode Node, int OctaveOffset) : Item;

    /// <summary>A <c>|: … :|</c> block with its children in document order and its
    /// <c>:|*N</c> play count (2 when absent).</summary>
    /// <remarks>
    /// ⚠️ <paramref name="ExplicitPlayCount"/> is null when no <c>*N</c> was written, and the
    /// difference matters to anyone who ALSO counts endings: the music stream's rule is
    /// "an explicit count, else the highest ending number, else 2" (MidiExporter
    /// ProcessRepeatSpan), and a reader that only sees <c>PlayCount</c>'s defaulted 2 cannot
    /// spell that rule. MEASURED 2026-08-31, which is why it is here: MIDI's form-side arm
    /// read neither and hard-coded <c>Math.Max(2, endings)</c>, so <c>form { |: ~X :|*3 }</c>
    /// sounded twice while the same music written <c>|: … :|*3</c> sounded three times.
    /// </remarks>
    /// <param name="Node">The written block, or null for a block a form-level <c>:|:</c>
    /// opened (see <see cref="GroupDividerRepeats"/>) — no consumer reads it today.</param>
    internal sealed record Repeat(
        FormRepeatBlockSyntax? Node, int PlayCount, IReadOnlyList<Item> Children,
        int? ExplicitPlayCount = null) : Item;

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

    /// <summary>A <c>:|:</c> standing at FORM level, outside any block — read by
    /// <see cref="GroupDividerRepeats"/> and never yielded.</summary>
    private sealed record DividerBar(BarlineSyntax Node) : Item;

    /// <summary>The form's items in document order (tokens are never yielded).</summary>
    internal static IReadOnlyList<Item> Read(SyntaxNode container)
    {
        var items = new List<Item>();
        for (int i = 0; i < container.SlotCount; i++)
            Classify(container.GetChild(i), items, insideRepeat: false);
        return GroupDividerRepeats(items);
    }

    /// <summary>
    /// A form-level <c>:|:</c> is TWO bars — <c>:|</c> then <c>|:</c> (GRAMMAR §8, and
    /// MeasureCollector.Form.cs's reading of the same token inside a block) — so it is read
    /// as the one-sided <c>:|</c> it starts with (<see cref="LoneRepeatEnd"/>: repeat the
    /// piece from its beginning) followed by a <see cref="Repeat"/> that runs to the next
    /// form-level <c>:|</c>, whose <c>:|*N</c> is the count and whose trailing endings
    /// (<c>[2. Y]</c>, <c>:| [3. Z]</c> — the parser's finalAlternative / furtherAlternatives
    /// shapes) belong to it. A second <c>:|:</c> closes that block and opens the next, as it
    /// does inside a written block (<c>|: B :|: C :|</c> is <c>|: B :| |: C :|</c>), so it
    /// rewinds nothing. A <c>:|:</c> the form never closes leaves the block without a
    /// <see cref="RepeatEnd"/>; that is LYS4017's case and the page decides it.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-09-10 (scratch/ベースタブLy/pageBreak.lys, <c>form main { A :|: B :| }</c>):
    /// the token fell into <see cref="Other"/>, so MIDI ignored it and rewound at the closing
    /// <c>:|</c> (A B A B), MusicXML wrote one backward repeat on the last bar (the same
    /// reading), while the page drew <c>:|</c> after A, <c>|:</c> before B and <c>:|</c> after
    /// B (A A B B) — four readers, three answers. The LilyPond twin was aligned to the page
    /// first (LilyPondExporter.EmitRewindRepeat); this reader is where the other two get the
    /// same answer, since it is the one reader of a form's spellings.
    /// </remarks>
    private static List<Item> GroupDividerRepeats(List<Item> items)
    {
        bool any = false;
        foreach (var item in items)
            if (item is DividerBar) { any = true; break; }
        if (!any)
            return items;

        var output = new List<Item>(items.Count);
        bool openerOnly = false; // the divider that just CLOSED a block only opens the next
        int i = 0;
        while (i < items.Count)
        {
            if (items[i] is not DividerBar divider)
            {
                output.Add(items[i]);
                i++;
                continue;
            }
            if (!openerOnly)
                output.Add(new LoneRepeatEnd(RewindBar(divider.Node)));
            openerOnly = false;

            var children = new List<Item> { new RepeatStart(divider.Node.BarToken) };
            int? explicitCount = null;
            i++;
            while (i < items.Count)
            {
                var it = items[i];
                if (it is LoneRepeatEnd close)
                {
                    children.Add(new RepeatEnd(close.Node.BarToken));
                    explicitCount = close.Node.HasExplicitRepeatCount ? close.Node.RepeatCount : null;
                    i++;
                    // The endings after the ':|' are this block's.
                    while (i < items.Count)
                    {
                        if (items[i] is Ending e) { children.Add(e); i++; continue; }
                        if (items[i] is LoneRepeatEnd more && i + 1 < items.Count && items[i + 1] is Ending e2)
                        {
                            children.Add(new RepeatEnd(more.Node.BarToken));
                            children.Add(e2);
                            i += 2;
                            continue;
                        }
                        break;
                    }
                    break;
                }
                if (it is DividerBar next)
                {
                    // Closes this block; the outer loop reads it again as the next one's opener.
                    children.Add(new RepeatEnd(next.Node.BarToken));
                    openerOnly = true;
                    break;
                }
                children.Add(it);
                i++;
            }
            output.Add(new Repeat(null, explicitCount ?? 2, children, explicitCount));
        }
        return output;
    }

    /// <summary>The <c>:|</c> half of a form-level <c>:|:</c>, as the bar-line node a
    /// consumer that re-emits bar lines (the LilyPond twin) expects a
    /// <see cref="LoneRepeatEnd"/> to carry — at the divider's own position.</summary>
    private static BarlineSyntax RewindBar(BarlineSyntax divider)
        => new(new InternalSyntax.BarlineGreen(
                new InternalSyntax.SyntaxToken(SyntaxKind.RepeatEndBar, ":|")),
            null, divider.Position);

    private static void Classify(SyntaxNode? child, List<Item> items, bool insideRepeat)
    {
        switch (child)
        {
            case SectionReferenceSyntax r:
                items.Add(new SectionRef(r.SectionName, r.DisplayLabel, Silent: false, r,
                    r.OctaveOffset));
                break;
            case { Kind: SyntaxKind.SilentSectionReference }
                    when child.GetChild(1) is SyntaxTokenNode name:
                // The silent spelling has no red class of its own, so the marks and the
                // parked label are read here off the SAME functions the plain one's
                // properties call.
                // ⚠️ THE LABEL USED TO BE DROPPED HERE (`null`), and that was harmless only
                // while `~` always meant HIDE: a label nobody would print need not travel.
                // Since 2026-08-31 a `~` reference to a `section ~A` SHOWS, so the label it
                // parked is the one to print — measured, `form { ~A "shown" }` printed the
                // section's NAME until this line carried it.
                items.Add(new SectionRef(name.Text, SyntaxFacts.UnquotedLabel(child),
                    Silent: true, child, SyntaxFacts.NetOctaveMarks(child)));
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
            // A form-level ':|:' (a BARLINE node too — Parser.Form.cs ParseFormBarline): two
            // bars, grouped by GroupDividerRepeats once the whole list is read.
            case BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatBothBar } both when !insideRepeat:
                items.Add(new DividerBar(both));
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
        return new Repeat(block, PlayCount(block), children, ExplicitPlayCount(block));
    }

    /// <summary>
    /// The <c>:|*3</c> play count on a repeat block, or 2 when it is absent.
    /// </summary>
    /// <remarks>
    /// The parser keeps it as the <c>*</c> + integer token pair sitting on the
    /// block's end bar line (Parser.Form.cs ParseFormRepeatBlock), not as a node —
    /// the same spelling and the same place an inline <c>:|*3</c> carries it.
    /// </remarks>
    internal static int PlayCount(FormRepeatBlockSyntax block) => ExplicitPlayCount(block) ?? 2;

    /// <summary>
    /// The written <c>:|*3</c> play count, or null when the block carries none — the
    /// distinction <see cref="PlayCount"/>'s default hides. See <see cref="Repeat"/>.
    /// </summary>
    internal static int? ExplicitPlayCount(FormRepeatBlockSyntax block)
    {
        for (int i = 0; i + 1 < block.SlotCount; i++)
            if (block.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.Asterisk }
                && block.GetChild(i + 1) is SyntaxTokenNode count
                && int.TryParse(count.Text, out int n) && n >= 1)
                return n;
        return null;
    }
}
