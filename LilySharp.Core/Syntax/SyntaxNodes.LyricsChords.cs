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

    private int OpenBraceIndex => HasName ? 2 : 1;

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
/// A single chord entry: root[duration][:quality][/bass] (e.g. c1, a:m, g2:7,
/// d:m7/f, f:maj7/+e). The quality token is the raw text after the colon
/// (resolved against <c>ChordQualityRegistry</c> by the collector).
/// </summary>
public sealed class ChordEntrySyntax : SyntaxNode
{
    internal ChordEntrySyntax(InternalSyntax.ChordEntryGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // Slots: 0 root, 1 duration?, 2 colon?, [quality tokens…], slash?, plus?,
    // bass? (slash/plus/bass are always the final three slots, null when absent).
    /// <summary>The chord root pitch.</summary>
    public PitchSyntax Root => (PitchSyntax)GetChild(0)!;
    /// <summary>The chord's duration, or null when unspecified.</summary>
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;

    /// <summary>The full quality text after the <c>:</c> (e.g. "m7", "maj7", "7",
    /// "m7.5-"), joined from its token run, or null for a plain major triad.</summary>
    public string? QualityText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 3; i < SlotCount - 3; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    sb.Append(t.Text);
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }

    /// <summary>The slash-bass pitch (<c>c/g</c> or <c>c/+g</c>), or null.</summary>
    public PitchSyntax? Bass => GetChild(SlotCount - 1) as PitchSyntax;

    /// <summary>True for the ADDED-bass form <c>/+bass</c> (LP: the bass is added
    /// below the full chord; the plain <c>/bass</c> is an inversion). The printed
    /// name is identical either way — the flag records entry intent (and feeds a
    /// future realization).</summary>
    public bool BassIsAdded => GetChild(SlotCount - 2) != null;
}
