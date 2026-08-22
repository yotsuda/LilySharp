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
    /// Gets the written value — ALL value tokens joined, with the surrounding quotes of
    /// a quoted value removed — or null when the assignment has no value.
    /// </summary>
    /// <remarks>
    /// ⚠️ This used to return only the FIRST token, which disagreed with the reader that
    /// was actually used: <c>RenderSpecParser.GetPartProperty</c> joins every token,
    /// because a hyphenated bare value (<c>instrument bass-guitar</c>) is word+minus+word
    /// in the green tree. So the same node had two "values" — <c>bass</c> here and
    /// <c>bass-guitar</c> there — and this one had NO consumers in the whole repository
    /// (measured 2026-08-15, docs/VALUE_SITE_AUDIT.md §7 ①). The join is now written
    /// once, here, and GetPartProperty reads it.
    /// </remarks>
    public string? ValueText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 2; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    sb.Append(t.Text);
            if (sb.Length == 0)
                return null;
            var text = sb.ToString();
            return text.Length >= 2 && text[0] == '"' && text[^1] == '"'
                ? text[1..^1]
                : text;
        }
    }

    /// <summary>
    /// The assignment's value as a TYPE rather than as text, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The consumers of a part property each used to reinterpret the tokens their own
    /// way — <c>octave</c> parsed the first token, <c>lines</c> parsed the join, and
    /// <c>instrument</c> split the token list (docs/VALUE_SITE_AUDIT.md §1.1 A3). The
    /// numeric ones now read this. <c>instrument</c> still works off the token list
    /// because it splits a preset from a quoted label, which is two values, not one.
    /// </remarks>
    public LysValue? Value
    {
        get
        {
            if (ValueText is not { } text)
                return null;
            // ONE token: the lexer already decided what it is, so ask its KIND rather
            // than re-deriving the type from the joined text. (A quoted value has lost
            // its quotes to ValueText above; FromToken's Trim is then a no-op.)
            var values = Values.ToList();
            if (values.Count == 1 && values[0] is SyntaxTokenNode single)
                return LysValue.FromToken(single.Kind, text);
            // MORE than one token is a run the lexer did not join for us —
            // `instrument bass-guitar` (word+minus+word), `transpose d'` (pitch+mark).
            // Those are words, not numbers.
            return LysValue.FromToken(SyntaxKind.Identifier, text);
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
    /// What the value run MEANS, read in one pass — the marking, the beat unit and its
    /// dots, the bpm and the swing subdivision together.
    /// </summary>
    /// <remarks>
    /// A consumer that wants more than one of them should read THIS once rather than
    /// several of the properties below, each of which re-reads the run.
    /// </remarks>
    public TempoValue Value => TempoValue.FromTokens(Values.OfType<SyntaxTokenNode>());

    /// <summary>
    /// The tempo marking (e.g., "Allegro"), if present — a bare word in the FIRST
    /// value position (<c>tempo Comodo 4 = 84</c>) or a quoted string.
    /// </summary>
    public string? Marking => Value.Marking;

    /// <summary>
    /// The note value made to swing by a trailing 'swing'/'shuffle' word, or 0 for no
    /// swing: <c>tempo 120 swing</c> = 8 (eighths), <c>tempo 120 swing 16</c> = 16
    /// (sixteenths). Drives the swing-feel equation drawn beside the metronome mark.
    /// </summary>
    public int SwingSubdivision => Value.SwingSubdivision;

    /// <summary>The BPM, if present.</summary>
    public int? Bpm => Value.Bpm;

    /// <summary>
    /// The beat unit (e.g., 4 for quarter note), or null when the run has no <c>=</c> —
    /// <c>tempo 140</c> is a bpm, and reading its 140 as a beat unit printed a
    /// 140th-note metronome glyph once already.
    /// </summary>
    public int? BeatUnit => Value.BeatUnit;

    /// <summary>Augmentation dots on the beat unit (<c>4. = 116</c> → 1).</summary>
    public int BeatDots => Value.BeatDots;
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
/// Font directive — <c>fonts { KEY VALUE… }</c>, binding a face per text role.
/// ⚠️ A node whose <c>IsBlock</c> is false is the REMOVED one-line form, kept in the tree
/// (with its diagnostic) so no source position slides — it binds nothing.
/// </summary>
/// <remarks>
/// The green node holds the block's tokens FLAT (the parser only found the extent), so
/// the entries are read back here. That keeps a growing role vocabulary out of the
/// syntax tree: adding a role adds a word to <c>TextRoles</c> and nothing else.
/// </remarks>
public sealed class FontDeclarationSyntax : SyntaxNode
{
    internal FontDeclarationSyntax(FontDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>font</c> keyword token.</summary>
    public SyntaxTokenNode KeywordToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>True when this directive is written as <c>fonts { … }</c>.</summary>
    public bool IsBlock =>
        SlotCount > 1 && GetChild(1) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace };

    /// <summary>
    /// The font name — the first quoted string literal's unquoted value, or null if
    /// absent.
    /// </summary>
    /// <remarks>
    /// ⚠️ In the BLOCK form this is the first name of the first entry and means very
    /// little; a reader that wants every face the directive asks for wants
    /// <see cref="NamedFaces"/>, which is what the embed check uses.
    /// </remarks>
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
    /// True iff the <c>embedded</c> keyword is present — trailing on the one-liner, or as
    /// a bare entry in the block.
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

    /// <summary>Every face name this directive asks for, in source order, deduplicated.</summary>
    public IReadOnlyList<string> NamedFaces()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        for (int i = 1; i < SlotCount; i++)
        {
            if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } token)
            {
                string name = token.Text.Trim('"');
                if (name.Length > 0 && seen.Add(name))
                    result.Add(name);
            }
        }
        return result;
    }

    /// <summary>
    /// One <c>KEY VALUE…</c> entry of the block form.
    /// </summary>
    /// <param name="Key">The key as written — a role, a group, or a generic family.</param>
    /// <param name="KeyToken">The key's token, for a diagnostic's span.</param>
    /// <param name="Names">Quoted face names, in preference order; empty for a redirect.</param>
    /// <param name="Family">The generic family this entry redirects to, when it does.</param>
    public readonly record struct Entry(
        string Key,
        SyntaxTokenNode KeyToken,
        IReadOnlyList<string> Names,
        Rendering.TextFontFamily? Family);

    /// <summary>
    /// The block's entries. Empty for the one-liner form.
    /// </summary>
    /// <remarks>
    /// An entry runs from its key to the next KEY — there is no separator in this
    /// language — so a bare word is read as a value only when it is a generic family and
    /// as a key otherwise. That is why <c>sans</c> and <c>serif</c> are the only bare
    /// words a value may be: any other bare word would be indistinguishable from the
    /// next entry's key, and a grammar where <c>lyrics Georgia</c> silently binds nothing
    /// is worse than one that refuses the unquoted name.
    /// </remarks>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            if (!IsBlock)
                return [];
            var entries = new List<Entry>();
            SyntaxTokenNode? keyToken = null;
            var names = new List<string>();
            Rendering.TextFontFamily? redirect = null;

            void Flush()
            {
                if (keyToken != null)
                    entries.Add(new Entry(keyToken.Text, keyToken, [.. names], redirect));
                keyToken = null;
                names.Clear();
                redirect = null;
            }

            for (int i = 2; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode token)
                    continue;
                switch (token.Kind)
                {
                    case SyntaxKind.CloseBrace:
                        continue;
                    case SyntaxKind.StringLiteral:
                        names.Add(token.Text.Trim('"'));
                        continue;
                    case SyntaxKind.EmbeddedKeyword:
                        // Read by Embedded; it ends the entry it trails.
                        Flush();
                        continue;
                }
                // A bare word: a family word CONTINUES the open entry, anything else
                // starts a new one.
                if (keyToken != null && names.Count == 0 && redirect == null &&
                    Rendering.TextRoles.TryParseFamily(token.Text, out var fam))
                {
                    redirect = fam;
                    continue;
                }
                Flush();
                keyToken = token;
            }
            Flush();
            return entries;
        }
    }
}

/// <summary>
/// Paper directive — <c>paper { KEY VALUE… }</c>, setting the page's dimensions.
/// ⚠️ A node whose <c>IsBlock</c> is false is the refused blockless form, kept in the
/// tree (with its diagnostic) so no source position slides — it sets nothing.
/// </summary>
/// <remarks>
/// The green node holds the block's tokens FLAT, like the font block's: the entries are
/// read back here, so a growing paper vocabulary grows <c>PaperPlanReader</c>'s table
/// and nothing in the syntax tree. The one shape the font walker does not have is the
/// NESTED spacing block (<c>systemSystemSpacing { basicDistance 12 }</c>), which this
/// walker tracks with a brace depth of at most one — the parser refuses a deeper one.
/// </remarks>
public sealed class PaperDeclarationSyntax : SyntaxNode
{
    internal PaperDeclarationSyntax(PaperDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>paper</c> keyword token.</summary>
    public SyntaxTokenNode KeywordToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>True when this directive is written as <c>paper { … }</c>.</summary>
    public bool IsBlock =>
        SlotCount > 1 && GetChild(1) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace };

    /// <summary>
    /// One <c>KEY value</c> line of a nested spacing block.
    /// </summary>
    /// <param name="Key">The sub-key as written (<c>basicDistance</c>, …).</param>
    /// <param name="KeyToken">The sub-key's token, for a diagnostic's span.</param>
    /// <param name="MinusToken">The sign, when the value is negative.</param>
    /// <param name="NumberToken">The value's number token, or null when none followed.</param>
    /// <param name="UnitToken">The glued unit suffix (<c>mm</c>/<c>cm</c>/<c>in</c>), if any.</param>
    public readonly record struct SubEntry(
        string Key,
        SyntaxTokenNode KeyToken,
        SyntaxTokenNode? MinusToken,
        SyntaxTokenNode? NumberToken,
        SyntaxTokenNode? UnitToken);

    /// <summary>
    /// One entry of the block form: a scalar (<c>paperWidth 210mm</c>), a bare flag
    /// (<c>raggedRight</c>), or a nested spacing block
    /// (<c>systemSystemSpacing { … }</c>).
    /// </summary>
    /// <param name="Key">The key as written.</param>
    /// <param name="KeyToken">The key's token, for a diagnostic's span.</param>
    /// <param name="MinusToken">The sign, when the scalar value is negative.</param>
    /// <param name="NumberToken">The scalar value's number token, or null.</param>
    /// <param name="UnitToken">The glued unit suffix, if any.</param>
    /// <param name="HasBlock">True when the key is followed by a nested block.</param>
    /// <param name="SubEntries">The nested block's lines; empty otherwise.</param>
    public readonly record struct Entry(
        string Key,
        SyntaxTokenNode KeyToken,
        SyntaxTokenNode? MinusToken,
        SyntaxTokenNode? NumberToken,
        SyntaxTokenNode? UnitToken,
        bool HasBlock,
        IReadOnlyList<SubEntry> SubEntries);

    /// <summary>
    /// The block's entries. Empty for the blockless form.
    /// </summary>
    /// <remarks>
    /// An entry runs from its key to the next KEY, the same convention as the font
    /// block — there is no separator in this language. A unit suffix is a word GLUED to
    /// its number (<c>210mm</c>, one quantity, like LilyPond's <c>210\mm</c>): a spaced
    /// <c>210 mm</c> reads as a new key named <c>mm</c>, which the reader refuses with
    /// the glued spelling in the message rather than binding a second spelling silently.
    /// </remarks>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            if (!IsBlock)
                return [];
            var entries = new List<Entry>();
            SyntaxTokenNode? keyToken = null, minus = null, number = null, unit = null;
            bool hasBlock = false;
            List<SubEntry> subEntries = [];
            SyntaxTokenNode? subKeyToken = null, subMinus = null, subNumber = null, subUnit = null;
            int depth = 0;

            void FlushSub()
            {
                if (subKeyToken != null)
                    subEntries.Add(new SubEntry(subKeyToken.Text, subKeyToken, subMinus, subNumber, subUnit));
                subKeyToken = null; subMinus = null; subNumber = null; subUnit = null;
            }

            void Flush()
            {
                FlushSub();
                if (keyToken != null)
                    entries.Add(new Entry(keyToken.Text, keyToken, minus, number, unit, hasBlock, [.. subEntries]));
                keyToken = null; minus = null; number = null; unit = null;
                hasBlock = false;
                subEntries = [];
            }

            for (int i = 2; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode token)
                    continue;
                switch (token.Kind)
                {
                    case SyntaxKind.OpenBrace:
                        // The parser refused any deeper brace, so this opens the one
                        // nested spacing block of the OPEN entry.
                        depth = 1;
                        hasBlock = true;
                        continue;
                    case SyntaxKind.CloseBrace:
                        if (depth == 1) { depth = 0; continue; }
                        continue; // the block's own closer
                    case SyntaxKind.Minus:
                        if (depth == 1) subMinus = token; else minus = token;
                        continue;
                    case SyntaxKind.IntegerLiteral:
                    case SyntaxKind.DecimalLiteral:
                        if (depth == 1) subNumber ??= token; else number ??= token;
                        continue;
                }
                // A word. Glued to the entry's number it is that number's UNIT;
                // anything else starts the next entry (or sub-entry).
                if (depth == 1)
                {
                    if (subNumber != null && subUnit == null && token.Span.Start == subNumber.Span.End)
                    {
                        subUnit = token;
                        continue;
                    }
                    FlushSub();
                    subKeyToken = token;
                }
                else
                {
                    if (number != null && unit == null && token.Span.Start == number.Span.End)
                    {
                        unit = token;
                        continue;
                    }
                    Flush();
                    keyToken = token;
                }
            }
            Flush();
            return entries;
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

    /// <summary>The <c>part</c> keyword token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The declared part name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The optional inline display-name token (<c>part melody "Violin I"</c>) —
    /// a string literal sitting right after the name, before any body brace. Null when
    /// absent. Detected by kind, so it never collides with the opening <c>{</c>.</summary>
    private SyntaxTokenNode? DisplayNameToken =>
        GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } t ? t : null;

    /// <summary>The part's default display name (surrounding quotes stripped), or null.
    /// A score's <c>staff X "…"</c> overrides it for that score; otherwise this is the
    /// label printed at the staff's left. Not a symbol — free text, may contain spaces.</summary>
    public string? DisplayName => DisplayNameToken is { } t ? t.Text.Trim('"') : null;

    // Layout: keyword name [displayName] [openBrace props… closeBrace]. The body,
    // when present, begins at the first token after the optional display name.
    private int OpenBraceIndex => DisplayNameToken != null ? 3 : 2;
    private bool HasBody =>
        SlotCount > OpenBraceIndex
        && GetChild(OpenBraceIndex) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace };

    /// <summary>The part's property assignments (empty when the part has no body block).</summary>
    public IEnumerable<PropertyAssignmentSyntax> Properties
    {
        get
        {
            if (!HasBody) yield break;
            // Between the opening brace and the closing one.
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
            {
                if (GetChild(i) is PropertyAssignmentSyntax prop)
                    yield return prop;
            }
        }
    }
}

/// <summary>
/// Variable reference: a bare phrase name (<c>Chorus</c>).
/// </summary>
public sealed class VariableReferenceSyntax : SyntaxNode
{
    internal VariableReferenceSyntax(VariableReferenceGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // The name is child 0. Trailing octave marks (' / ,) follow it, so slot count
    // alone cannot locate it — the index can, now that nothing precedes the name.
    // (The `$` sigil that used to sit at index 0 was removed 2026-08-22; see
    // DiagnosticCodes.PhraseNameUnreachable for what it had been reaching.)
    /// <summary>The referenced variable name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(0)!;

    // Raw trailing tokens: the net '/, mark count and the optional glued
    // interval argument (`'(3)`) parsed after them.
    private (int Marks, int? Interval) MarkInfo()
    {
        int marks = 0;
        int? interval = null;
        for (int i = 1; i < SlotCount; i++)   // the name is child 0
        {
            var child = GetChild(i) as SyntaxTokenNode;
            if (child?.Kind == SyntaxKind.Apostrophe)
                marks++;
            else if (child?.Kind == SyntaxKind.Comma)
                marks--;
            else if (child?.Kind == SyntaxKind.IntegerLiteral
                     && int.TryParse(child.Text, out int n) && n >= 1)
                interval = n;
        }
        return (marks, interval);
    }

    /// <summary>
    /// Net octave shift from the trailing marks (<c>'</c> = +1, <c>,</c> = -1),
    /// applied when the movable phrase is placed at the reference site. Same
    /// spelling and meaning as a pitch's octave marks. With an interval argument
    /// the LAST mark carries the interval instead of a whole octave
    /// (<c>Melody'(3)</c> = up a third, no octave; <c>Melody''(3)</c> = up an
    /// octave plus a third), so one mark is consumed here and reappears as
    /// <see cref="DiatonicShiftSteps"/>.
    /// </summary>
    public int OctaveOffset
    {
        get
        {
            var (marks, interval) = MarkInfo();
            return interval is null ? marks : System.Math.Sign(marks) * (System.Math.Abs(marks) - 1);
        }
    }

    /// <summary>Diatonic scale-step shift from the glued interval argument —
    /// <c>Melody'(3)</c> = +2 steps (a third up in the ambient key),
    /// <c>Motif,(2)</c> = −1 (a second down). 1-based like a degree, so
    /// <c>'(8)</c> ≡ <c>'</c> and <c>'(1)</c> is a no-op; 0 with no argument.</summary>
    public int DiatonicShiftSteps
    {
        get
        {
            var (marks, interval) = MarkInfo();
            return interval is { } n ? System.Math.Sign(marks) * (n - 1) : 0;
        }
    }
}
