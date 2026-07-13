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

/// <summary>
/// Property assignment: name: value
/// </summary>
public sealed class PropertyAssignmentSyntax : SyntaxNode
{
    internal PropertyAssignmentSyntax(PropertyAssignmentGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The property name token.</summary>
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The <c>:</c> separator token.</summary>
    public SyntaxTokenNode Colon => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>
    /// Gets the value tokens (everything after the colon).
    /// </summary>
    public IEnumerable<SyntaxNode> Values
    {
        get
        {
            // Skip name (0) and colon (1), return rest
            for (int i = 2; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
    
    /// <summary>
    /// Gets the first value token text, or null if none.
    /// </summary>
    public string? ValueText
    {
        get
        {
            var firstValue = Values.FirstOrDefault();
            if (firstValue is SyntaxTokenNode token)
                return token.Text.Trim('"');
            return firstValue?.ToString();
        }
    }
}

/// <summary>
/// Time signature: time 4/4
/// </summary>
public sealed class TimeSignatureSyntax : SyntaxNode
{
    internal TimeSignatureSyntax(InternalSyntax.TimeSignatureGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>time</c> keyword token.</summary>
    public SyntaxTokenNode TimeKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The <c>:</c> in a part-header <c>time: 4/4</c>; null for the bare music command.</summary>
    public SyntaxTokenNode? Colon => GetChild(1) as SyntaxTokenNode;
    /// <summary>The first numerator token.</summary>
    public SyntaxTokenNode Numerator => (SyntaxTokenNode)GetChild(2)!;

    // Additive meters (time 3+2/8) put extra (+, int) tokens between the
    // first numerator and the slash, so these scan instead of using fixed
    // slot indices.
    private int SlashIndex
    {
        get
        {
            for (int i = 3; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode t && t.Kind == SyntaxKind.Slash)
                    return i;
            return 3;
        }
    }

    /// <summary>True for <c>time none</c> — unmeasured (senza misura).</summary>
    public bool IsSenzaMisura =>
        Numerator.Kind == SyntaxKind.Identifier
        && Numerator.Text.Equals("none", StringComparison.OrdinalIgnoreCase);

    /// <summary>The <c>/</c> separator token, or null when absent (e.g. <c>time none</c>).</summary>
    public SyntaxTokenNode? Slash => GetChild(SlashIndex) as SyntaxTokenNode;
    /// <summary>The denominator token, or null when absent.</summary>
    public SyntaxTokenNode? Denominator => GetChild(SlashIndex + 1) as SyntaxTokenNode;

    /// <summary>
    /// Gets the numerator value — the SUM for additive meters (3+2/8 → 5).
    /// </summary>
    public int Beats
    {
        get
        {
            int sum = 0;
            bool any = false;
            for (int i = 2; i < SlashIndex; i++)
            {
                if (GetChild(i) is SyntaxTokenNode t && int.TryParse(t.Text, out var v))
                {
                    sum += v;
                    any = true;
                }
            }
            return any ? sum : 4;
        }
    }

    /// <summary>The numerator AS WRITTEN ("3+2") for additive meters; null for
    /// a plain single-number meter.</summary>
    public string? BeatsText
    {
        get
        {
            if (SlashIndex == 3)
                return null;
            var sb = new System.Text.StringBuilder();
            for (int i = 2; i < SlashIndex; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    sb.Append(t.Text);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Gets the denominator value (e.g., 4 for 4/4).
    /// </summary>
    public int BeatType => int.TryParse(Denominator?.Text, out var n) ? n : 4;
}

/// <summary>
/// Tempo declaration: tempo "Allegro" 4 = 120 or tempo 120
/// </summary>
public sealed class TempoDeclarationSyntax : SyntaxNode
{
    internal TempoDeclarationSyntax(InternalSyntax.TempoDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>tempo</c> keyword token.</summary>
    public SyntaxTokenNode TempoKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The <c>:</c> in a part-header <c>tempo: 120</c>; null for the bare music command.</summary>
    public SyntaxTokenNode? Colon => GetChild(1) as SyntaxTokenNode;

    /// <summary>
    /// Gets all value tokens after the keyword (and the optional header colon).
    /// </summary>
    public IEnumerable<SyntaxNode> Values
    {
        get
        {
            // Slot 0 is the keyword, slot 1 is the optional colon; values follow.
            for (int i = 2; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Gets the tempo marking string (e.g., "Allegro"), if present — either a
    /// quoted string anywhere in the values or a bare word in the FIRST value
    /// position (<c>tempo Comodo 4 = 84</c>; swing/shuffle are feel words,
    /// not markings).
    /// </summary>
    public string? Marking
    {
        get
        {
            bool first = true;
            foreach (var value in Values)
            {
                if (value is SyntaxTokenNode token)
                {
                    if (token.Kind == SyntaxKind.StringLiteral)
                        return token.Text.Trim('"');
                    if (first && token.Kind == SyntaxKind.Identifier
                        && token.Text is not ("swing" or "shuffle"))
                        return token.Text;
                }
                first = false;
            }
            return null;
        }
    }

    /// <summary>
    /// The note value made to swing by a trailing 'swing'/'shuffle' word, or 0 for no
    /// swing: <c>tempo 120 swing</c> = 8 (eighths), <c>tempo 120 swing 16</c> = 16
    /// (sixteenths). Drives the swing-feel equation drawn beside the metronome mark.
    /// </summary>
    public int SwingSubdivision
    {
        get
        {
            bool sawSwing = false;
            foreach (var value in Values)
            {
                if (value is not SyntaxTokenNode t)
                    continue;
                if (t.Kind == SyntaxKind.Identifier && (t.Text == "swing" || t.Text == "shuffle"))
                    sawSwing = true;
                else if (sawSwing &&
                         (t.Kind == SyntaxKind.IntegerLiteral) &&
                         int.TryParse(t.Text, out int n))
                    return n;  // the value right after the swing word
            }
            return sawSwing ? 8 : 0;  // bare 'swing' = eighths
        }
    }

    /// <summary>
    /// Gets the BPM value, if present. Stops at a 'swing'/'shuffle' word so the
    /// subdivision number after it (e.g. the 16 in <c>swing 16</c>) is not mistaken
    /// for the tempo.
    /// </summary>
    public int? Bpm
    {
        get
        {
            int? lastInt = null;
            foreach (var value in Values)
            {
                if (value is not SyntaxTokenNode token)
                    continue;
                if (token.Kind == SyntaxKind.Identifier && (token.Text == "swing" || token.Text == "shuffle"))
                    break;
                if ((token.Kind == SyntaxKind.IntegerLiteral) &&
                    int.TryParse(token.Text, out var n))
                    lastInt = n;
            }
            return lastInt;
        }
    }

    /// <summary>
    /// Gets the beat unit (e.g., 4 for quarter note), if present.
    /// </summary>
    public int? BeatUnit
    {
        get
        {
            // Look for the first integer before '=' (beat unit)
            bool foundEquals = false;
            foreach (var value in Values)
            {
                if (value is SyntaxTokenNode token)
                {
                    if (token.Kind == SyntaxKind.Equals)
                    {
                        foundEquals = true;
                        break;
                    }
                    if (token.Kind == SyntaxKind.IntegerLiteral)
                    {
                        if (int.TryParse(token.Text, out var n))
                            return n;
                    }
                }
            }
            return foundEquals ? 4 : null; // Default to quarter note if = is present
        }
    }

    /// <summary>Augmentation dots on the beat unit (<c>4. = 116</c> → 1).</summary>
    public int BeatDots
    {
        get
        {
            int dots = 0;
            bool sawUnit = false;
            foreach (var value in Values)
            {
                if (value is not SyntaxTokenNode t)
                    continue;
                if (t.Kind is SyntaxKind.IntegerLiteral)
                {
                    sawUnit = true;
                    dots = 0;
                    continue;
                }
                if (t.Kind == SyntaxKind.Dot && sawUnit)
                {
                    dots++;
                    continue;
                }
                if (t.Kind == SyntaxKind.Equals)
                    return dots;
            }
            return 0;
        }
    }
}

/// <summary>
/// Partial (anacrusis) declaration: partial 4 — declares the following measure a
/// pickup of the given duration. LILYPOND-REF: ly/music-functions-init.ly:1670-1678
/// 'partial' music function (PartialSet on the Timing context).
/// </summary>
public sealed class PartialDeclarationSyntax : SyntaxNode
{
    internal PartialDeclarationSyntax(InternalSyntax.PartialDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>partial</c> keyword token.</summary>
    public SyntaxTokenNode PartialKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The pickup length as a duration node (number + optional dots).</summary>
    public DurationSyntax Duration => (DurationSyntax)GetChild(1)!;

    /// <summary>The pickup length as a metric fraction (e.g. 1/4 for 'partial 4').</summary>
    public Fraction ToFraction() => Duration.ToFraction();
}

/// <summary>
/// Metadata declaration: title "value" or tempo 120
/// </summary>
public sealed class MetadataDeclarationSyntax : SyntaxNode
{
    internal MetadataDeclarationSyntax(MetadataDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The metadata keyword token (e.g. title, composer, tempo).</summary>
    public SyntaxTokenNode KeywordToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the keyword text (e.g., "title", "tempo", "time").
    /// </summary>
    public string Keyword => KeywordToken.Text;

    /// <summary>
    /// Gets all value tokens after the keyword.
    /// </summary>
    public IEnumerable<SyntaxNode> Values
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Gets the first string literal value, if any.
    /// </summary>
    public string? StringValue
    {
        get
        {
            foreach (var value in Values)
            {
                if (value is SyntaxTokenNode token && token.Kind == SyntaxKind.StringLiteral)
                    return token.Text.Trim('"');
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the first integer value, if any.
    /// </summary>
    public int? IntegerValue
    {
        get
        {
            foreach (var value in Values)
            {
                if (value is SyntaxTokenNode token &&
                    (token.Kind == SyntaxKind.IntegerLiteral))
                {
                    if (int.TryParse(token.Text, out var result))
                        return result;
                }
            }
            return null;
        }
    }
}

/// <summary>
/// Font directive: font "NAME" [embedded]
/// </summary>
public sealed class FontDeclarationSyntax : SyntaxNode
{
    internal FontDeclarationSyntax(FontDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>font</c> keyword token.</summary>
    public SyntaxTokenNode KeywordToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// The font name — the quoted string literal's unquoted value, or null if absent.
    /// </summary>
    public string? FontName
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode token && token.Kind == SyntaxKind.StringLiteral)
                    return token.Text.Trim('"');
            }
            return null;
        }
    }

    /// <summary>
    /// True iff the trailing <c>embedded</c> keyword token is present.
    /// </summary>
    public bool Embedded
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode token && token.Kind == SyntaxKind.EmbeddedKeyword)
                    return true;
            }
            return false;
        }
    }
}

/// <summary>
/// Variable declaration: name = expr
/// </summary>
public sealed class VariableDeclarationSyntax : SyntaxNode
{
    internal VariableDeclarationSyntax(VariableDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The declared variable name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The <c>=</c> token.</summary>
    public SyntaxTokenNode EqualsToken => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The assigned expression.</summary>
    public SyntaxNode Expression => GetChild(2)!;
}

/// <summary>
/// Phrase declaration: phrase name { ... }
/// </summary>
public sealed class PhraseDeclarationSyntax : SyntaxNode
{
    internal PhraseDeclarationSyntax(PhraseDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>phrase</c> keyword token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The declared phrase name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The phrase's music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(2)!;
}

/// <summary>
/// Part declaration: part name { props }
/// </summary>
public sealed class PartDeclarationSyntax : SyntaxNode
{
    internal PartDeclarationSyntax(PartDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // With body: keyword name { props } = 5+ slots
    // Without body: keyword name = 2 slots
    private bool HasBody => SlotCount > 2;

    /// <summary>The <c>part</c> keyword token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The declared part name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;

    // Properties are between braces if HasBody
    /// <summary>The part's property assignments (empty when the part has no body block).</summary>
    public IEnumerable<PropertyAssignmentSyntax> Properties
    {
        get
        {
            if (!HasBody) yield break;
            // Skip keyword, name, openBrace; stop before closeBrace
            for (int i = 3; i < SlotCount - 1; i++)
            {
                if (GetChild(i) is PropertyAssignmentSyntax prop)
                    yield return prop;
            }
        }
    }
}

/// <summary>
/// Variable reference: use name or $name
/// </summary>
public sealed class VariableReferenceSyntax : SyntaxNode
{
    internal VariableReferenceSyntax(VariableReferenceGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // The optional leading `$` keyword is the only Dollar token; when present the
    // name sits at index 1, otherwise at index 0. Trailing octave marks (' / ,)
    // follow the name, so slot count alone can no longer locate it.
    private int NameIndex => GetChild(0) is SyntaxTokenNode { Kind: SyntaxKind.Dollar } ? 1 : 0;

    /// <summary>The referenced variable name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(NameIndex)!;

    /// <summary>
    /// Net octave shift from the trailing marks (<c>'</c> = +1, <c>,</c> = -1),
    /// applied when the movable phrase is placed at the reference site. Same
    /// spelling and meaning as a pitch's octave marks.
    /// </summary>
    public int OctaveOffset
    {
        get
        {
            int offset = 0;
            for (int i = NameIndex + 1; i < SlotCount; i++)
            {
                var child = GetChild(i) as SyntaxTokenNode;
                if (child?.Kind == SyntaxKind.Apostrophe)
                    offset++;
                else if (child?.Kind == SyntaxKind.Comma)
                    offset--;
            }
            return offset;
        }
    }
}
