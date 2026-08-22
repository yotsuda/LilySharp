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
    /// between the keyword and the brace, binding to a same-named voice).</summary>
    private bool HasName =>
        GetChild(1) is SyntaxTokenNode t && t.Kind == SyntaxKind.Identifier;

    /// <summary>The voice name this lyrics block binds to, or null for the default
    /// (first voice).</summary>
    public string? VoiceName =>
        HasName ? ((SyntaxTokenNode)GetChild(1)!).Text : null;

    /// <summary>The <c>sings</c> keyword token of a melody-bound track
    /// (<c>lyrics ja sings vocal { … }</c>), or null when the block writes none.</summary>
    public SyntaxTokenNode? SingsKeyword =>
        HasName && GetChild(2) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } s
            && s.Text == "sings" ? s : null;

    /// <summary>The part this track sings (<c>lyrics ja sings vocal</c> → "vocal"),
    /// or null when this block declares no binding. The binding is a property of
    /// the TRACK name — see <see cref="Music.LyricBindings"/> for the resolved
    /// answer across all of a track's blocks.</summary>
    public string? SingsTarget =>
        SingsKeyword != null && GetChild(3) is SyntaxTokenNode { Kind: SyntaxKind.Identifier } t2
            ? t2.Text : null;

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
    /// part-major form these are <see cref="SectionDeclarationSyntax"/> children
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

    /// <summary>Inner section declarations of the part-major lyric track form
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

    /// <summary>True when this lyric track is written in the part-major (per-section) form.</summary>
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

    /// <summary>The chord part name this block contributes to.</summary>
    public string? PartName =>
        HasName ? ((SyntaxTokenNode)GetChild(1)!).Text : null;

    private int OpenBraceIndex => HasName ? 2 : 1;

    /// <summary>The opening <c>{</c> token.</summary>
    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(OpenBraceIndex)!;

    /// <summary>The chord entries and barlines, in source order. For the part-major
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

    /// <summary>Inner section declarations of the part-major chord track form
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

    /// <summary>True when this chord track is written in the part-major (per-section) form.</summary>
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
    /// (adjacent by construction, so this is the source slice).</summary>
    public string SymbolText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    sb.Append(t.Text);
            return sb.ToString();
        }
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
