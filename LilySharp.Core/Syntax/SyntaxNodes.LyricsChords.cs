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
/// Lyrics block: lyrics { ... }
/// </summary>
public sealed class LyricsBlockSyntax : SyntaxNode
{
    internal LyricsBlockSyntax(InternalSyntax.LyricsBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>lyrics</c> keyword token.</summary>
    public SyntaxTokenNode LyricsKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>True when written as `lyrics name { … }` (an optional name sits
    /// between the keyword and the brace, binding to a same-named voice). A PART NAME,
    /// so a clef word counts (<see cref="SyntaxFacts.IsPartNameToken"/>, as the parser).</summary>
    private bool HasName =>
        GetChild(1) is SyntaxTokenNode t && SyntaxFacts.IsPartNameToken(t.Kind, t.Text);

    /// <summary>The token carrying this block's track name, or null when it writes none.
    /// The editor colours THIS; <see cref="VoiceName"/> is the same slot read once, so a
    /// name that is coloured and a name that is resolved can never be different tokens.
    /// </summary>
    public SyntaxTokenNode? NameToken => HasName ? (SyntaxTokenNode)GetChild(1)! : null;

    /// <summary>The track name — <see cref="SyntaxFacts.UnnamedLyricsName"/> for an unnamed
    /// block (2026-10-09), which a score places with a bare <c>lyrics</c> row and which
    /// sings the unnamed part, or the file's only part, unless it says otherwise
    /// (<see cref="Music.LyricBindings"/>).</summary>
    public string? VoiceName => NameToken?.Text ?? SyntaxFacts.UnnamedLyricsName;

    /// <summary>The <c>sings</c> keyword token of a melody-bound track
    /// (<c>lyrics ja sings vocal { … }</c>), or null when the block writes none.</summary>
    public SyntaxTokenNode? SingsKeyword =>
        GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } s
            && s.Text == "sings" ? s : null;

    /// <summary>The token naming the part this track sings, or null when this block
    /// declares no binding.</summary>
    public SyntaxTokenNode? SingsTargetToken =>
        SingsKeyword != null && GetChild(3) is SyntaxTokenNode t2 && SyntaxFacts.IsPartNameToken(t2.Kind, t2.Text)
            ? t2 : null;

    /// <summary>The part this track sings (<c>lyrics ja sings vocal</c> → "vocal"),
    /// or null when this block declares no binding. The binding is a property of
    /// the TRACK name — see <see cref="Music.LyricBindings"/> for the resolved
    /// answer across all of a track's blocks.</summary>
    public string? SingsTarget => SingsTargetToken?.Text;

    private int OpenBraceIndex
    {
        get
        {
            // The head is 1-4 tokens (`lyrics [name [sings part]] {`): find the
            // brace by KIND so present-or-absent optional tokens never shift a
            // reader off it.
            for (int i = 1; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace })
                    return i;
            return HasName ? 2 : 1;
        }
    }

    /// <summary>The opening <c>{</c> token.</summary>
    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(OpenBraceIndex)!;
    /// <summary>The lyric syllable items (lyric measures), in order. For the
    /// by-part grouping these are <see cref="SectionDeclarationSyntax"/> children
    /// instead; use <see cref="Sections"/> to read them.</summary>
    public IEnumerable<SyntaxNode> Syllables
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }

    /// <summary>Inner section declarations of the by-part lyric track form
    /// (<c>lyrics { section A { .. } section B { .. } }</c>) — each holds this
    /// track's verse for one named section. Empty for the flat form.</summary>
    public IEnumerable<SectionDeclarationSyntax> Sections
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
                if (GetChild(i) is SectionDeclarationSyntax section)
                    yield return section;
        }
    }

    /// <summary>True when this lyric track is written in the by-part (per-section) form.</summary>
    public bool HasSections
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
                if (GetChild(i) is SectionDeclarationSyntax)
                    return true;
            return false;
        }
    }

    /// <summary>The closing <c>}</c> token.</summary>
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;
}

/// <summary>
/// A chord-symbol block: <c>chords [name] { c | g:7 c | }</c>. WITH a name it is
/// an independent chord part placed in a score via <c>chords name</c> (lead-sheet
/// row); WITHOUT a name its symbols align above the co-written part's staff by
/// timing (the pre-release <c>chordnames</c> form, folded into this keyword).
/// </summary>
public sealed class ChordPartBlockSyntax : SyntaxNode
{
    internal ChordPartBlockSyntax(InternalSyntax.ChordPartBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>chords</c> keyword token.</summary>
    public SyntaxTokenNode ChordsKeyword => (SyntaxTokenNode)GetChild(0)!;

    private bool HasName =>
        GetChild(1) is SyntaxTokenNode t && t.Kind == SyntaxKind.Identifier;

    /// <summary>The token carrying this block's track name, or null when it writes none —
    /// what the editor colours. <see cref="PartName"/> reads the same slot.</summary>
    public SyntaxTokenNode? NameToken => HasName ? (SyntaxTokenNode)GetChild(1)! : null;

    /// <summary>The chord part name this block contributes to — <see cref="SyntaxFacts.UnnamedChordsName"/>
    /// for an unnamed block.</summary>
    public string? PartName => NameToken?.Text ?? SyntaxFacts.UnnamedChordsName;

    private int OpenBraceIndex => HasName ? 2 : 1;

    /// <summary>The opening <c>{</c> token.</summary>
    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(OpenBraceIndex)!;

    /// <summary>The chord entries and barlines, in source order. For the by-part
    /// form these are <see cref="SectionDeclarationSyntax"/> children instead; use
    /// <see cref="Sections"/> to read them.</summary>
    public IEnumerable<SyntaxNode> Items
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }

    /// <summary>Inner section declarations of the by-part chord track form
    /// (<c>chords name { section A { c1 } section B { c1 } }</c>) — each holds this
    /// part's chords for one named section. Empty for the flat form.</summary>
    public IEnumerable<SectionDeclarationSyntax> Sections
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
                if (GetChild(i) is SectionDeclarationSyntax section)
                    yield return section;
        }
    }

    /// <summary>True when this chord track is written in the by-part (per-section) form.</summary>
    public bool HasSections
    {
        get
        {
            for (int i = OpenBraceIndex + 1; i < SlotCount - 1; i++)
                if (GetChild(i) is SectionDeclarationSyntax)
                    return true;
            return false;
        }
    }

    /// <summary>The closing <c>}</c> token.</summary>
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;
}

/// <summary>
/// A single chord entry: the SYMBOL as it prints (<c>Am</c>, <c>G7</c>,
/// <c>F#m7-5/C#</c>) — a glued token run (GRAMMAR_AUDIT 8.1, decided
/// 2026-08-21). <see cref="SymbolText"/> re-joins the run; the collector hands
/// it to <c>ChordStructure.TryParseChordEntry</c>, so the block and
/// <c>@chord</c> read one format. Entries carry NO duration: a chord row is
/// measure-relative — the entries and <c>.</c> extensions of a bar divide it on
/// the meter's beat grid (<see cref="Svg.Collector.ChordRhythm"/>).
/// </summary>
public sealed class ChordEntrySyntax : SyntaxNode
{
    internal ChordEntrySyntax(InternalSyntax.ChordEntryGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The symbol exactly as written — the run's token texts joined
    /// (adjacent by construction, so this is the source slice), up to the <c>(</c> that
    /// opens its shapes — with its ALTERATION LIST, when the first glued group is one,
    /// re-joined after it with commas: <c>C7(b9, #11)</c> and <c>C7(b9 #11)</c> both give
    /// <c>C7(b9,#11)</c>, the one string <c>Music.ChordStructure.TryParseChordEntry</c> reads
    /// (session 967, owner's decision: spaces inside the parentheses are allowed).</summary>
    public string SymbolText
    {
        get
        {
            // The run's tokens outside the groups — the symbol, and the `/E` a chart writes
            // after its alteration list (C7(b9)/E) — with the alteration group re-joined in
            // its place and the shape group left out.
            var alterations = AlterationGroup;
            var sb = new System.Text.StringBuilder();
            int depth = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is not SyntaxTokenNode t)
                    continue;
                if (t.Kind == SyntaxKind.OpenParen)
                {
                    if (depth++ == 0 && alterations != null && t.SourceStart == alterations.Open.SourceStart)
                    {
                        sb.Append('(');
                        for (int w = 0; w < alterations.Words.Count; w++)
                            sb.Append(w == 0 ? "" : ",").Append(alterations.Words[w].Text);
                        sb.Append(')');
                    }
                    continue;
                }
                if (t.Kind == SyntaxKind.CloseParen)
                {
                    if (depth > 0)
                        depth--;
                    continue;
                }
                if (depth == 0)
                    sb.Append(t.Text);
            }
            return sb.ToString();
        }
    }

    /// <summary>The <c>(</c> that opens the entry's chord-diagram shapes
    /// (<c>F(133211 2010)</c>, the second group of <c>C7(b9)(x32310)</c>), or null when it
    /// writes none.</summary>
    public SyntaxTokenNode? ShapesOpenParen => ShapeGroup?.Open;

    /// <summary>The <c>)</c> closing the entry's chord-diagram shapes, or null when it
    /// writes none or the group is unclosed.</summary>
    public SyntaxTokenNode? ShapesCloseParen => ShapeGroup?.Close;

    /// <summary>
    /// The words inside the entry's SHAPE parentheses — each a run of GLUED tokens, as a
    /// <c>@chord</c>'s argument is, whitespace and ',' separating — with their spans; empty
    /// when it writes none. Read by <c>Music.ChordShapes.ParseWords</c>. The alteration
    /// list's words are not here: they are part of <see cref="SymbolText"/>.
    /// </summary>
    public IReadOnlyList<(string Text, TextSpan Span)> ShapeWords
        => ShapeGroup?.Words ?? [];

    /// <summary>The words of the entry's alteration list (<c>C7(b9, #11)</c>: <c>b9</c>,
    /// <c>#11</c>) with their spans; empty when it writes none.</summary>
    public IReadOnlyList<(string Text, TextSpan Span)> AlterationWords
        => AlterationGroup?.Words ?? [];

    /// <summary>One glued <c>( … )</c> group after the symbol: its parentheses (the close
    /// null when unwritten) and its words.</summary>
    private sealed record ParenGroup(SyntaxTokenNode Open, SyntaxTokenNode? Close,
        IReadOnlyList<(string Text, TextSpan Span)> Words);

    /// <summary>
    /// The symbol's ALTERATION LIST — the first group when every word of it is an alteration
    /// (<c>Music.ChordSymbolSpelling.IsAlterationList</c>: <c>b9</c>, <c>#11</c>, <c>add9</c>,
    /// <c>omit3</c>, <c>sus4</c>, <c>maj7</c>, a bare <c>9</c>) — else null. A shape word is
    /// never one (<c>x32010</c>, <c>0003</c>, <c>xx-10-12-13-11</c>, a tuning name), so the
    /// two groups cannot be confused; an empty group is the shapes', as it always was.
    /// </summary>
    private ParenGroup? AlterationGroup
    {
        get
        {
            var groups = Groups();
            return groups.Count > 0 && IsAlterationList(groups[0]) ? groups[0] : null;
        }
    }

    /// <summary>The shapes' group: the group after the alteration list, else the first.</summary>
    private ParenGroup? ShapeGroup
    {
        get
        {
            var groups = Groups();
            int at = groups.Count > 0 && IsAlterationList(groups[0]) ? 1 : 0;
            return at < groups.Count ? groups[at] : null;
        }
    }

    private static bool IsAlterationList(ParenGroup group)
        => Music.ChordSymbolSpelling.IsAlterationList([.. group.Words.Select(w => w.Text)]);

    // The glued groups in order, their words split as a @chord argument's are: a token
    // after trivia or a ',' starts a new word.
    private List<ParenGroup> Groups()
    {
        var groups = new List<ParenGroup>();
        SyntaxTokenNode? open = null;
        List<(string Text, TextSpan Span)>? words = null;
        var sb = new System.Text.StringBuilder();
        int start = -1, end = -1;
        void Flush()
        {
            if (sb.Length > 0)
                words!.Add((sb.ToString(), new TextSpan(start, end - start)));
            sb.Clear();
            start = -1;
        }
        for (int i = 0; i < SlotCount; i++)
        {
            if (GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (open == null)
            {
                if (t.Kind == SyntaxKind.OpenParen)
                {
                    open = t;
                    words = new();
                }
                continue;
            }
            if (t.Kind == SyntaxKind.CloseParen)
            {
                Flush();
                groups.Add(new ParenGroup(open, t.Text.Length > 0 ? t : null, words!));
                open = null;
                continue;
            }
            if (t.Text.Length == 0)
                continue;
            if (t.Kind == SyntaxKind.Comma)
            {
                Flush();
                continue;
            }
            // A token after trivia starts a new word (the MarkArgument rule).
            if (sb.Length > 0 && t.Green.LeadingTriviaWidth > 0)
                Flush();
            if (start < 0)
                start = t.SourceStart;
            sb.Append(t.Text);
            end = t.SourceStart + t.Text.Length;
            if (t.Green.TrailingTriviaWidth > 0)
                Flush();
        }
        if (open != null)
        {
            Flush();
            groups.Add(new ParenGroup(open, null, words!));
        }
        return groups;
    }
}

/// <summary>
/// A chord-row slot extension: a lone <c>.</c> in a <c>chords</c> body. The
/// previous entry (or rest) holds through one more slot of the measure's beat
/// grid — <c>| C . . G7 |</c> in 4/4 is C for three beats, then G7. A <c>.</c>
/// never crosses a barline; one at the head of a measure has nothing to extend
/// and is reported (the slot still counts, so the grid stays honest).
/// </summary>
public sealed class ChordExtendSyntax : SyntaxNode
{
    internal ChordExtendSyntax(InternalSyntax.ChordExtendGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>.</c> token.</summary>
    public SyntaxTokenNode DotToken => (SyntaxTokenNode)GetChild(0)!;
}
