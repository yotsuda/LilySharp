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

using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Syntax;

// ============================================================
// New Section-Oriented Syntax Nodes
// ============================================================

/// <summary>
/// Represents a section declaration: section Name { ... }
/// </summary>
public sealed partial class SectionDeclarationSyntax : SyntaxNode
{
    internal SectionDeclarationSyntax(SectionDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>section</c> keyword token.</summary>
    public SyntaxTokenNode SectionKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The section name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>
    /// Gets the section name as a string.
    /// </summary>
    public string SectionName => Name.Text;
}

/// <summary>
/// Represents a using directive: using "file.lys". Resolved by the using
/// expander before collection; in the parsed tree it is an inert marker.
/// </summary>
public sealed class UsingDirectiveSyntax : SyntaxNode
{
    internal UsingDirectiveSyntax(InternalSyntax.UsingDirectiveGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>using</c> keyword token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The quoted file-path token.</summary>
    public SyntaxTokenNode PathToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The included file path, with surrounding quotes stripped.</summary>
    public string Path => PathToken.Text.Trim('"');
}

/// <summary>
/// Represents a part block inside a section: partName { ... }
/// </summary>
public sealed partial class PartBlockSyntax : SyntaxNode
{
    internal PartBlockSyntax(PartBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The part name token.</summary>
    public SyntaxTokenNode PartName => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the part name as a string.
    /// </summary>
    public string Name => PartName.Text;
}

/// <summary>
/// The <c>{</c> … <c>}</c> span of a declaration whose body is a brace pair standing among
/// its DIRECT children. Nested braces belong to CHILD nodes, so scanning the direct children
/// can only ever find this declaration's own pair.
/// </summary>
/// <remarks>
/// One house for both askers — <see cref="FormDeclarationSyntax.BodySpan"/> and
/// <see cref="RenderDeclarationSyntax.BodySpan"/> ask the same question of the same shape of
/// node, and a second copy of the loop would be a second spelling of it (HANDOFF §5.2.1②).
/// The loop starts at slot 1 because slot 0 is always the keyword.
/// </remarks>
internal static class DeclarationBody
{
    /// <summary>Null when either brace is missing — a malformed header the parser
    /// already reported, so the caller falls back to the keyword's span.</summary>
    public static TextSpan? BraceSpan(SyntaxNode node)
    {
        SyntaxTokenNode? open = null, close = null;
        for (int i = 1; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not SyntaxTokenNode t) continue;
            if (t.Kind == SyntaxKind.OpenBrace && open == null) open = t;
            else if (t.Kind == SyntaxKind.CloseBrace) close = t;
        }
        if (open == null || close == null) return null;
        return new TextSpan(open.Span.Start, close.Span.End - open.Span.Start);
    }
}

/// <summary>
/// Represents a form declaration: <c>form Name { ... }</c> (the surface keyword
/// is <c>form</c>; the node kind stays "Structure" internally).
/// </summary>
public sealed partial class FormDeclarationSyntax : SyntaxNode
{
    internal FormDeclarationSyntax(FormDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>form</c> keyword token.</summary>
    public SyntaxTokenNode FormKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The form's name token (e.g. <c>Main</c>), or null when a malformed
    /// declaration omitted it. Names are case-sensitive.</summary>
    public SyntaxTokenNode? Name =>
        GetChild(1) is SyntaxTokenNode { Kind: not SyntaxKind.OpenBrace } t ? t : null;

    /// <summary>The form's name text, or empty when absent.</summary>
    public string NameText => Name?.Text ?? "";

    /// <summary>
    /// The form body's own <c>{</c> … <c>}</c> span — what LYS6007 underlines, for the same
    /// reason LYS6002 underlines the score's: the body is what has to change.
    /// </summary>
    public TextSpan? BodySpan => DeclarationBody.BraceSpan(this);
}

/// <summary>
/// Represents a section reference in structure: SectionName
/// </summary>
public sealed partial class SectionReferenceSyntax : SyntaxNode
{
    internal SectionReferenceSyntax(SectionReferenceGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The referenced section name token.</summary>
    public SyntaxTokenNode Identifier => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the referenced section name.
    /// </summary>
    public string SectionName => Identifier.Text;

    /// <summary>
    /// Optional per-occurrence display label: <c>structure { First Second
    /// First "First (reprise)" }</c> prints the string instead of the section
    /// identifier for THIS occurrence. Null when no label was given.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: LilyPond's analog is a manual <c>\mark "text"</c> per
    /// occurrence — display labels are occurrence-level events there too.
    /// </remarks>
    public string? DisplayLabel
    {
        get
        {
            // Found by KIND, not by index: the octave marks sit between the name and the
            // label (source order is `B' "reprise"`), so on a shifted reference slot 1 is
            // a mark and the label is wherever the marks stop.
            SyntaxTokenNode? token = null;
            for (int i = 1; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode t
                    && t.Kind is not (SyntaxKind.Apostrophe or SyntaxKind.Comma))
                {
                    token = t;
                    break;
                }
            if (token == null)
                return null;
            var text = token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
                return text.Substring(1, text.Length - 2);
            return text;
        }
    }

    /// <summary>
    /// Net octave shift from the trailing marks (<c>'</c> = +1, <c>,</c> = -1) — the same
    /// spelling, and the same meaning, a phrase reference's marks carry
    /// (<see cref="VariableReferenceSyntax.OctaveOffset"/>).
    /// </summary>
    /// <remarks>
    /// A section boundary REOPENS the relative frame at the part's anchor (user decision,
    /// 2026-08-31: the reset stays and the carry is given by NOTATION), so a section whose
    /// music belongs an octave away had no way to say so. <c>~B'</c> says it per PLAY: the
    /// same section referenced twice can open at two different octaves, and the
    /// DECLARATION is untouched — a shift written on the declaration would move B's
    /// pitches at every call site, which is the bug the reset was introduced to fix
    /// (MeasureCollector.ProcessSectionPrologue's remark keeps the example).
    /// </remarks>
    public int OctaveOffset => SyntaxFacts.NetOctaveMarks(this);
}

/// <summary>
/// Represents a repeat block in structure: |: ... :|
/// </summary>
public sealed partial class FormRepeatBlockSyntax : SyntaxNode
{
    internal FormRepeatBlockSyntax(FormRepeatBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}

/// <summary>
/// Represents an alternative in structure: 1. SectionName or [1. SectionName] or [1-3. SectionName] or [1. ~SectionName]
/// </summary>
public sealed partial class FormAlternativeSyntax : SyntaxNode
{
    internal FormAlternativeSyntax(FormAlternativeGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// True if this is bracket style [1. A], false if legacy style 1. A
    /// </summary>
    public bool HasBracket => ((SyntaxTokenNode)GetChild(0)!).Kind == SyntaxKind.OpenBracket;

    /// <summary>
    /// True if this has a range separator (- or ,) like [1-3. A] or [1,3. A].
    /// </summary>
    /// <remarks>
    /// ⚠️ Asked of SLOT 2, not of <c>SlotCount</c>. It counted slots (9 with a separator,
    /// 7 without) until 2026-08-31, when the ending gained variable-length octave marks and
    /// <c>[1. A'']</c> reached 9 slots without a separator — which would have made this read
    /// the `.` as the separator and the tilde slot as the end number. The separator is the
    /// only thing that can stand at slot 2, so ask it there.
    /// </remarks>
    public bool HasSeparator => HasBracket
        && GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.Minus or SyntaxKind.Comma };

    /// <summary>
    /// True if this is a silent section reference [1. ~A] (no label displayed)
    /// </summary>
    public bool IsSilent
    {
        get
        {
            if (!HasBracket) return false;
            // Tilde is at slot[3] for without separator, slot[5] for with separator
            var tildeSlot = HasSeparator ? 5 : 3;
            var child = GetChild(tildeSlot);
            return child != null && child is SyntaxTokenNode token && token.Kind == SyntaxKind.Tilde;
        }
    }

    /// <summary>True when the bracket ending is terminated by a closing <c>]</c> — its
    /// right cap is drawn. Omitting the <c>]</c> leaves the ending open on the right.</summary>
    public bool IsClosed =>
        HasBracket && GetChild(SlotCount - 1) is SyntaxTokenNode { Kind: SyntaxKind.CloseBracket };

    /// <summary>
    /// Gets the number token.
    /// Legacy: slot[0], Bracket: slot[1]
    /// </summary>
    public SyntaxTokenNode Number => (SyntaxTokenNode)GetChild(HasBracket ? 1 : 0)!;

    /// <summary>
    /// Gets the section name token.
    /// Legacy (3 slots): slot[2]
    /// Bracket without separator (6 slots): slot[4]
    /// Bracket with separator (8 slots): slot[6]
    /// </summary>
    public SyntaxTokenNode SectionName => (SyntaxTokenNode)GetChild(SectionNameSlot)!;

    /// <summary>
    /// Gets the alternative number.
    /// </summary>
    public int AlternativeNumber => int.Parse(Number.Text);

    /// <summary>
    /// Gets the separator token (- or ,) if present.
    /// Only valid when HasBracket and HasSeparator are true.
    /// Slot[2] when HasSeparator.
    /// </summary>
    public SyntaxTokenNode? Separator => HasSeparator ? (SyntaxTokenNode?)GetChild(2) : null;

    /// <summary>
    /// Gets the end number token (e.g., "3" in [1-3. A]).
    /// Only valid when HasBracket and HasSeparator are true.
    /// Slot[3] when HasSeparator.
    /// </summary>
    public SyntaxTokenNode? EndNumber => HasSeparator ? (SyntaxTokenNode?)GetChild(3) : null;

    /// <summary>
    /// Gets the volta text for display (e.g., "1.", "1-3.", "1,3.").
    /// </summary>
    public string VoltaText
    {
        get
        {
            if (!HasBracket) return $"{Number.Text}.";
            if (!HasSeparator) return $"{Number.Text}.";
            return $"{Number.Text}{Separator!.Text}{EndNumber!.Text}.";
        }
    }

    /// <summary>
    /// Optional display label on a bracket alternative: <c>[1. A "label"]</c>,
    /// shown as the section's mark just like a plain reference's <c>A "A2"</c>.
    /// Null when no label was given (or on the legacy non-bracket style). The
    /// label slot sits right after the section name (slot[7] with a separator,
    /// slot[5] without).
    /// </summary>
    public string? DisplayLabel
    {
        get
        {
            if (!HasBracket) return null;
            // By KIND: the octave marks sit between the name and the label, so the label's
            // index is no longer fixed. A StringLiteral can only be the label here.
            SyntaxTokenNode? token = null;
            for (int i = SectionNameSlot + 1; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } t)
                {
                    token = t;
                    break;
                }
            if (token == null) return null;
            var text = token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
                return text.Substring(1, text.Length - 2);
            return text;
        }
    }

    /// <summary>
    /// Net octave shift from the trailing marks (<c>[1. B']</c> = +1), the same spelling
    /// and the same meaning a plain section reference carries
    /// (<see cref="SectionReferenceSyntax.OctaveOffset"/>) — an ending IS a reference with
    /// a bracket around it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Counted FROM the section-name slot, not over the whole node, because this is the
    /// one reference shape whose <c>,</c> tokens are not all marks: the range separator in
    /// <c>[1,3. B]</c> is a Comma standing at slot 2. Counting every slot would read that
    /// ending as "an octave down".
    /// </remarks>
    public int OctaveOffset => SyntaxFacts.NetOctaveMarksFrom(this, SectionNameSlot + 1);

    /// <summary>The slot the section name stands at — the last FIXED index in this node,
    /// and the place the by-kind reads above start from.</summary>
    private int SectionNameSlot => HasBracket ? (HasSeparator ? 6 : 4) : 2;
}

/// <summary>
/// Represents a navigation mark: segno, fine, coda, dc, ds, etc.
/// </summary>
public sealed partial class NavigationMarkSyntax : SyntaxNode
{
    internal NavigationMarkSyntax(NavigationMarkGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// Gets the type of navigation mark.
    /// </summary>
    public NavigationMarkType MarkType
    {
        get
        {
            var first = (SyntaxTokenNode)GetChild(0)!;
            return first.Kind switch
            {
                SyntaxKind.SegnoKeyword => NavigationMarkType.Segno,
                SyntaxKind.FineKeyword => NavigationMarkType.Fine,
                SyntaxKind.CodaKeyword => NavigationMarkType.Coda,
                SyntaxKind.ToKeyword => NavigationMarkType.ToCoda,
                SyntaxKind.DcKeyword => SlotCount == 1 ? NavigationMarkType.DaCapo :
                    ((SyntaxTokenNode)GetChild(2)!).Kind == SyntaxKind.FineKeyword
                        ? NavigationMarkType.DaCapoAlFine
                        : NavigationMarkType.DaCapoAlCoda,
                SyntaxKind.DsKeyword => SlotCount == 1 ? NavigationMarkType.DalSegno :
                    ((SyntaxTokenNode)GetChild(2)!).Kind == SyntaxKind.FineKeyword
                        ? NavigationMarkType.DalSegnoAlFine
                        : NavigationMarkType.DalSegnoAlCoda,
                _ => NavigationMarkType.Segno
            };
        }
    }
}

/// <summary>
/// Navigation mark types.
/// </summary>
public enum NavigationMarkType
{
    /// <summary>Segno sign (jump target).</summary>
    Segno,
    /// <summary>Fine: end of the piece on a repeat pass.</summary>
    Fine,
    /// <summary>Coda sign (jump target for the coda section).</summary>
    Coda,
    /// <summary>To Coda: jump to the coda from this point.</summary>
    ToCoda,
    /// <summary>Da Capo: repeat from the beginning.</summary>
    DaCapo,
    /// <summary>Da Capo al Fine: repeat from the beginning, then stop at Fine.</summary>
    DaCapoAlFine,
    /// <summary>Da Capo al Coda: repeat from the beginning, then jump to the coda.</summary>
    DaCapoAlCoda,
    /// <summary>Dal Segno: repeat from the segno.</summary>
    DalSegno,
    /// <summary>Dal Segno al Fine: repeat from the segno, then stop at Fine.</summary>
    DalSegnoAlFine,
    /// <summary>Dal Segno al Coda: repeat from the segno, then jump to the coda.</summary>
    DalSegnoAlCoda
}

/// <summary>
/// Represents a music mark: @segno, @fine, @ds.al.fine, etc.
/// </summary>
public sealed partial class MusicMarkSyntax : SyntaxNode
{
    internal MusicMarkSyntax(MusicMarkGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// Gets the mark name by joining the name and its arguments with '.'.
    /// For example "@fig(6 4)" returns "fig.6.4" and "@chord(Dm)" returns "chord.Dm".
    /// The bracketing '(' ')' and ',' separators are part of the source span but are
    /// excluded here, so downstream collectors keep parsing the same dotted string.
    /// </summary>
    public string MarkName
    {
        get
        {
            var parts = new List<string>();
            for (int i = 0; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is SyntaxTokenNode token && token.Kind is not (
                        SyntaxKind.At or SyntaxKind.Dot or SyntaxKind.OpenParen
                        or SyntaxKind.CloseParen or SyntaxKind.Comma
                        // The '!' of '@!X' is punctuation, exactly like the '@' beside it.
                        // See IsSpanEnd for why it must not reach the name.
                        or SyntaxKind.DashedBar))
                {
                    parts.Add(token.Text);
                }
            }
            return string.Join(".", parts);
        }
    }

    /// <summary>
    /// The annotation's NAME on its own — the word after the '@' (and after the '!' of a
    /// terminator), with no arguments, no dotted name parts and no placement qualifier
    /// ("fig", "chord", "ds").
    /// </summary>
    public string Name
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode token
                    && token.Kind is not (SyntaxKind.At or SyntaxKind.DashedBar))
                    return token.Text;
            return "";
        }
    }

    /// <summary>
    /// Whether this was written as a TERMINATOR — <c>@!rit</c> rather than <c>@rit</c>. The
    /// '!' ends what the same name opened.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE NAME IS THE SAME EITHER WAY, deliberately: <see cref="Name"/> and
    /// <see cref="MarkName"/> step over the '!', so <c>@!rit</c> and <c>@rit</c> both report
    /// "rit". That keeps ONE vocabulary and ONE "did you mean" list — a terminator with a
    /// name of its own would need a second of each, and every word added would have to be
    /// added to both. What the mark MEANS is this flag, read beside the name.
    /// </remarks>
    public bool IsSpanEnd
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.DashedBar })
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Whether the annotation was written with a parenthesised argument list. This is
    /// what tells bare <c>@chord</c> (derive the symbol from the notes) from
    /// <c>@chord(c:m7)</c>, and it stays true for an empty <c>@text()</c>.
    /// </summary>
    public bool HasArgumentList
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.OpenParen })
                    return true;
            return false;
        }
    }

    /// <summary>
    /// The arguments written between '(' and ')', each carrying both the value it
    /// denotes and the text that was written for it. Empty when there is no argument
    /// list.
    /// </summary>
    /// <remarks>
    /// This is the typed reading of the same run <see cref="MarkName"/> flattens into a
    /// dotted string; see <see cref="MarkArgument"/> for why an argument needs both
    /// halves and how its runs differ from MarkName's per-token split. The two coexist
    /// while the consumers move over one family at a time (VALUE_SITE_AUDIT §9.5).
    /// </remarks>
    public ImmutableArray<MarkArgument> Arguments => MarkArgument.FromTokens(ArgumentTokens);

    /// <summary>
    /// The tokens written between '(' and ')', in source order, with the brackets
    /// themselves removed and nothing else. Empty when there is no argument list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw material both readings are made of: <see cref="Arguments"/> groups these
    /// into runs of adjacent tokens, and <see cref="MarkName"/> joins them with '.'. A
    /// SUB-LANGUAGE wants neither grouping — it wants the tokens, because tokens are what
    /// it is written in (VALUE_SITE_AUDIT §9.2). The figured bass reads them here, which
    /// is the last argument that was being read out of the dotted name.
    /// </para>
    /// <para>
    /// ⚠️ EVERY interior token, including the ',' that separates arguments and any '.'
    /// written inside the brackets. <see cref="MarkName"/> drops both, and that dropped
    /// dot is precisely the one family whose spelling changes when a reader moves here
    /// (§9.5.3 ⑴ — the same artefact <c>@chord(b.es:7)</c> had).
    /// </para>
    /// </remarks>
    public ImmutableArray<SyntaxTokenNode> ArgumentTokens
    {
        get
        {
            ImmutableArray<SyntaxTokenNode>.Builder? interior = null;
            bool open = false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode token)
                    continue;
                if (token.Kind == SyntaxKind.OpenParen)
                {
                    open = true;
                    interior = ImmutableArray.CreateBuilder<SyntaxTokenNode>();
                }
                else if (token.Kind == SyntaxKind.CloseParen)
                {
                    open = false;
                }
                else if (open)
                {
                    interior!.Add(token);
                }
            }
            return interior is null ? [] : interior.ToImmutable();
        }
    }

    /// <summary>
    /// The side forced by a trailing '.up' / '.down' qualifier, or null when none was
    /// written. Only <c>@text(…)</c> takes one; the parser refuses it elsewhere so the
    /// other families' dotted names cannot be corrupted by it.
    /// </summary>
    public bool? ForcedAbove
    {
        get
        {
            bool afterClose = false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode token)
                    continue;
                if (token.Kind == SyntaxKind.CloseParen)
                    afterClose = true;
                else if (afterClose && token.Kind == SyntaxKind.Identifier)
                    return token.Text switch { "up" => true, "down" => false, _ => null };
            }
            return null;
        }
    }
}

/// <summary>
/// Represents a custom text annotation: _"text"
/// </summary>
public sealed partial class CustomTextSyntax : SyntaxNode
{
    internal CustomTextSyntax(CustomTextGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// Gets the text content without quotes.
    /// </summary>
    public string Text
    {
        get
        {
            // Slot 0: underscore, Slot 1: string literal
            var textToken = (SyntaxTokenNode)GetChild(1)!;
            var text = textToken.Text;
            // Remove surrounding quotes
            if (text.StartsWith("\"") && text.EndsWith("\""))
            {
                return text.Substring(1, text.Length - 2);
            }
            return text;
        }
    }
}

/// <summary>
/// Represents a render declaration: render Name "file.svg" { ... }
/// </summary>
public sealed partial class RenderDeclarationSyntax : SyntaxNode
{
    internal RenderDeclarationSyntax(RenderDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>score</c> keyword token.</summary>
    public SyntaxTokenNode RenderKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// The form this score renders — the bare-identifier reference right after
    /// <c>score</c> (`score Main …`), or null when omitted (a validator error).
    /// A quoted string is the basename, never the form name.
    /// </summary>
    public SyntaxTokenNode? FormName => LeadingToken(basename: false);

    /// <summary>The form name text, or empty when absent.</summary>
    public string FormNameText => FormName?.Text ?? "";

    /// <summary>
    /// The optional output basename — the quoted string in the header
    /// (`score Main "clean" …`), or null. Extension, if written, is dropped.
    /// </summary>
    public SyntaxTokenNode? Basename => LeadingToken(basename: true);

    /// <summary>The basename text with surrounding quotes stripped, or null.</summary>
    public string? BasenameText => Basename?.Text.Trim('"');

    // Header tokens before the '{' are, in source order, an optional form-name
    // (any bare token) and an optional basename (a string literal); the transpose
    // is a property NODE, not a token, so it never matches here.
    private SyntaxTokenNode? LeadingToken(bool basename)
    {
        for (int i = 1; i < SlotCount; i++)
        {
            if (GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.OpenBrace)
                break;
            bool isString = t.Kind == SyntaxKind.StringLiteral;
            if (isString == basename)
                return t;
        }
        return null;
    }

    /// <summary>
    /// The score body's own <c>{</c> … <c>}</c> span. Nested braces (a <c>form</c> or a
    /// <c>grandStaff</c> inside the block) belong to CHILD nodes, so scanning the direct
    /// children can only ever find this score's pair. Null when either brace is missing
    /// (a malformed header the parser already reported).
    /// </summary>
    public TextSpan? BodySpan => DeclarationBody.BraceSpan(this);

    /// <summary>
    /// The optional per-score <c>transpose &lt;pitch&gt;</c> (a property node before the
    /// brace), or null. Render items are staff/tab/etc. nodes, never properties, so
    /// a direct-child property is unambiguously the score transpose.
    /// </summary>
    public PropertyAssignmentSyntax? Transpose
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
                if (GetChild(i) is PropertyAssignmentSyntax prop)
                    return prop;
            return null;
        }
    }
}

/// <summary>
/// Represents a staff render item: staff [clef] { partName }
/// </summary>
public sealed partial class StaffRenderSyntax : SyntaxNode
{
    internal StaffRenderSyntax(StaffRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>staff</c> keyword token.</summary>
    public SyntaxTokenNode StaffKeyword => (SyntaxTokenNode)GetChild(0)!;
}

/// <summary>
/// A chord-row render item: <c>chords name</c> inside a score — places a chord part
/// as an independent row.
/// </summary>
public sealed class ChordRowRenderSyntax : SyntaxNode
{
    internal ChordRowRenderSyntax(InternalSyntax.ChordRowRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>chords</c> keyword token.</summary>
    public SyntaxTokenNode ChordsKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The chord part name to place (e.g. <c>chords riff</c> → "riff").</summary>
    public string PartName => ((SyntaxTokenNode)GetChild(1)!).Text;

    /// <summary>The chord display selector after the name (<c>as roman|names</c> →
    /// "roman"), or null when absent.</summary>
    public string? DisplayModeText => DisplayModeToken?.Text;

    /// <summary>The token carrying the display selector, or null when the row writes none —
    /// what a diagnostic about the selector underlines (the WORD, not the whole row).</summary>
    public SyntaxTokenNode? DisplayModeToken =>
        SlotCount > 3 && GetChild(2) is SyntaxTokenNode a && a.Text == "as"
            && GetChild(3) is SyntaxTokenNode m
            ? m
            : null;
}

/// <summary>
/// A lyrics-row render item: <c>lyrics name</c> inside a score — places a lyrics
/// part as an independent row.
/// </summary>
public sealed class LyricsRowRenderSyntax : SyntaxNode
{
    internal LyricsRowRenderSyntax(InternalSyntax.LyricsRowRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>lyrics</c> keyword token.</summary>
    public SyntaxTokenNode LyricsKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The lyrics part name to place (e.g. <c>lyrics verse</c> → "verse").</summary>
    public string PartName => ((SyntaxTokenNode)GetChild(1)!).Text;

    /// <summary>The <c>sings</c> keyword token of a binding-stating row
    /// (<c>lyrics verse sings melody</c>), or null when the row writes none.</summary>
    public SyntaxTokenNode? SingsKeyword =>
        SlotCount > 2 && GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } s
            && s.Text == "sings" ? s : null;

    /// <summary>The token naming the part this row says its track sings, or null when the
    /// row states no binding — what the editor colours.</summary>
    public SyntaxTokenNode? SingsTargetToken =>
        SingsKeyword != null && SlotCount > 3
            && GetChild(3) is SyntaxTokenNode { Kind: SyntaxKind.Identifier, Text.Length: > 0 } t2
            ? t2 : null;

    /// <summary>The part this row says its track sings (<c>lyrics verse sings
    /// melody</c> → "melody"), or null when the row states no binding. Same
    /// quantity as the definition's — the binding is a property of the TRACK
    /// name; see <see cref="Music.LyricBindings"/> for the resolved answer.</summary>
    public string? SingsTarget => SingsTargetToken?.Text;
}

/// <summary>
/// Represents a grand staff render item: grandStaff { staff staff ... }
/// </summary>
public sealed partial class GrandStaffRenderSyntax : SyntaxNode
{
    internal GrandStaffRenderSyntax(GrandStaffRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>grandStaff</c> keyword token.</summary>
    public SyntaxTokenNode GrandStaffKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the staff render items (at least 2 required, validated semantically).
    /// </summary>
    public IEnumerable<StaffRenderSyntax> Staves
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is StaffRenderSyntax staff)
                    yield return staff;
            }
        }
    }
}

/// <summary>
/// Represents a condensed-staff render item: <c>condensedStaff { partA partB … }</c>.
/// </summary>
/// <remarks>
/// The members are BARE part names rather than <c>staff</c> items: however many parts go in,
/// exactly one staff comes out, and each part becomes one of its voices. Read it as "these
/// things, each of which would be its own staff, condensed onto one" — the same reading that
/// lets a <c>combinedStaff</c> sit inside it without lying about what it is.
/// </remarks>
public sealed partial class CondensedStaffRenderSyntax : SyntaxNode
{
    internal CondensedStaffRenderSyntax(CondensedStaffRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>condensedStaff</c> keyword token.</summary>
    public SyntaxTokenNode CondensedStaffKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The part-name TOKENS to condense, in source order — which is also VOICE
    /// order, so the first part gets voice 1 (stems up) exactly as the first block of a
    /// <c>voice { … } { … }</c> span does. Tokens rather than text, because a reference that
    /// names no part has to be reported ON its own name (SymbolReferenceValidator).</summary>
    public IEnumerable<SyntaxTokenNode> PartNameTokens
    {
        get
        {
            // Slots: keyword, '{', member…, '}'. Selected by KIND rather than by index, so
            // a missing brace (error recovery) cannot shift the names.
            // ⚠️ Positively a PART NAME, not merely "not a brace": a member the container
            // rejected is KEPT in the tree so its width survives (ParseBarePartNameMembers),
            // and a `not` test would hand that rejected token back as a part name — which
            // would then be reported a SECOND time as an undefined part, under the
            // "cannot contain" error that already says what is wrong.
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode t && SyntaxFacts.IsPartNameKind(t.Kind))
                    yield return t;
            }
        }
    }

    /// <summary>The same names as text. ONE loop, so the token span and the string can
    /// never come from different members.</summary>
    public IEnumerable<string> PartNames => PartNameTokens.Select(t => t.Text);
}

/// <summary>
/// Represents a combined-staff render item: <c>combinedStaff { partA partB }</c>.
/// </summary>
/// <remarks>
/// Exactly two parts, and bare names for the same reason <c>condensedStaff</c> takes them:
/// one staff comes out. The difference is what happens to the music — a condensed staff
/// keeps both parts and draws them as two voices, while a combined staff MERGES them where
/// they agree, which is what makes the "a2" and "Solo" it prints true.
/// </remarks>
public sealed partial class CombinedStaffRenderSyntax : SyntaxNode
{
    internal CombinedStaffRenderSyntax(CombinedStaffRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>combinedStaff</c> keyword token.</summary>
    public SyntaxTokenNode CombinedStaffKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The two part-name TOKENS, in source order: the first is the part whose stems
    /// go UP where the two are engraved apart, and the one "Solo" refers to. Tokens rather
    /// than text, for the same reason as <see cref="CondensedStaffRenderSyntax.PartNameTokens"/>
    /// — an undefined one is reported on its own name.</summary>
    public IEnumerable<SyntaxTokenNode> PartNameTokens
    {
        get
        {
            // Selected by KIND, not index, so error recovery on a missing brace cannot
            // shift the names — and positively a part name, so a rejected member kept for
            // its width is not returned as one (as in CondensedStaffRenderSyntax).
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode t && SyntaxFacts.IsPartNameKind(t.Kind))
                    yield return t;
            }
        }
    }

    /// <summary>The same names as text. ONE loop, so the token span and the string can
    /// never come from different members.</summary>
    public IEnumerable<string> PartNames => PartNameTokens.Select(t => t.Text);
}

/// <summary>
/// Represents an ossia render item: ossia [clef] { partName }
/// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
/// </summary>
public sealed partial class OssiaRenderSyntax : SyntaxNode
{
    internal OssiaRenderSyntax(OssiaRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>ossia</c> keyword token.</summary>
    public SyntaxTokenNode OssiaKeyword => (SyntaxTokenNode)GetChild(0)!;
}

/// <summary>
/// Represents a tab render item: tab tuning { partName }
/// </summary>
public sealed partial class TabRenderSyntax : SyntaxNode
{
    internal TabRenderSyntax(TabRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>tab</c> keyword token.</summary>
    public SyntaxTokenNode TabKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The token carrying the tab STYLE selector (<c>as numbers|full</c>), or null
    /// when the item writes none — what a diagnostic about the selector underlines (the
    /// WORD, not the whole item).</summary>
    /// <remarks>
    /// ⚠️ IT EXISTED FOR THE CHORD TWIN AND NOT FOR THIS ONE, and that asymmetry is where the
    /// silence lived: <c>ChordRowRenderSyntax.DisplayModeToken</c> was added so
    /// ChordDisplayModeValidator could underline a bad <c>as roman|names</c> word, while the
    /// tab half of the very same <c>ConsumeAsSelector</c> had no accessor and therefore no
    /// validator — measured 2026-08-24, <c>tab m as bogus</c> drew full notation in silence.
    /// Matched by TEXT because <c>as</c> also lexes as the Dutch A-flat pitch.
    /// </remarks>
    public SyntaxTokenNode? DisplayModeToken
    {
        get
        {
            for (int i = 1; i + 1 < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode a
                    && string.Equals(a.Text, "as", System.StringComparison.Ordinal)
                    && GetChild(i + 1) is SyntaxTokenNode m)
                    return m;
            return null;
        }
    }

    /// <summary>The token naming an explicit TUNING override (<c>tab bass m</c>), or null
    /// when the tuning comes from the part definition.</summary>
    /// <remarks>
    /// The item is <c>tab [tuning] part [as style]</c>, so the tuning is present exactly when
    /// TWO target tokens stand before the selector. Reading it from the node rather than
    /// re-cutting the token list keeps RenderSpecParser, PartReferenceFinder and the
    /// validator on one answer (HANDOFF §5.2.1②).
    /// </remarks>
    public SyntaxTokenNode? TuningToken
    {
        get
        {
            var targets = new List<SyntaxTokenNode>();
            for (int i = 1; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode t) continue;
                if (t.Kind is SyntaxKind.OpenBrace or SyntaxKind.CloseBrace) continue;
                if (string.Equals(t.Text, "as", System.StringComparison.Ordinal)) break;
                targets.Add(t);
            }
            return targets.Count >= 2 ? targets[0] : null;
        }
    }
}

/// <summary>
/// Represents a MIDI part render: partName channel:1 instrument:25
/// </summary>
public sealed partial class MidiPartRenderSyntax : SyntaxNode
{
    internal MidiPartRenderSyntax(MidiPartRenderGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The part name token.</summary>
    public SyntaxTokenNode PartName => (SyntaxTokenNode)GetChild(0)!;
}


/// <summary>
/// Represents a line break: break
/// </summary>
public sealed partial class BreakSyntax : SyntaxNode
{
    internal BreakSyntax(BreakGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>break</c> / <c>nobreak</c> keyword token.</summary>
    public SyntaxTokenNode BreakKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>True for <c>nobreak</c> (forbid a line break here), false for <c>break</c>.</summary>
    public bool IsNoBreak => BreakKeyword.Kind == SyntaxKind.NoBreakKeyword;
}
