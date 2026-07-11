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
/// An optional language-version directive: <c>version 1</c>. A top-level marker
/// recording the grammar version a document targets, so future grammar revisions
/// can branch behavior on it. Absence means the current/default grammar.
/// </summary>
public sealed class VersionDeclarationSyntax : SyntaxNode
{
    internal VersionDeclarationSyntax(InternalSyntax.VersionDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>version</c> directive word token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The version value token (a bare integer, e.g. <c>1</c>).</summary>
    public SyntaxTokenNode ValueToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The declared version string (e.g. <c>1</c>). Any surrounding quotes
    /// from the rejected legacy <c>version "1"</c> form are stripped for recovery.</summary>
    public string Version => ValueToken.Text.Trim('"');
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
            if (GetChild(1) is not SyntaxTokenNode token)
                return null;
            var text = token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
                return text.Substring(1, text.Length - 2);
            return text;
        }
    }
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
    /// True if this has a range separator (- or ,) like [1-3. A] or [1,3. A]
    /// Slot layout: Bracket with separator has 9 slots, without has 7 slots
    /// (the extra slot over the historical 8/6 is the optional display label).
    /// </summary>
    public bool HasSeparator => HasBracket && SlotCount == 9;

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
    public SyntaxTokenNode SectionName => (SyntaxTokenNode)GetChild(
        HasBracket
            ? (HasSeparator ? 6 : 4)
            : 2)!;

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
            if (GetChild(HasSeparator ? 7 : 5) is not SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } token)
                return null;
            var text = token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
                return text.Substring(1, text.Length - 2);
            return text;
        }
    }
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
                        or SyntaxKind.CloseParen or SyntaxKind.Comma))
                {
                    parts.Add(token.Text);
                }
            }
            return string.Join(".", parts);
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

    /// <summary>The chord display selector after the name (<c>as roman|both|names</c> →
    /// "roman"), or null when absent.</summary>
    public string? DisplayModeText =>
        SlotCount > 3 && GetChild(2) is SyntaxTokenNode a && a.Text == "as"
            && GetChild(3) is SyntaxTokenNode m
            ? m.Text
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
