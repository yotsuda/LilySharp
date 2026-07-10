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
/// Repeat expression: repeat volta 2 { ... } alternative { ... }
/// </summary>
public sealed class RepeatExpressionSyntax : SyntaxNode
{
    internal RepeatExpressionSyntax(InternalSyntax.RepeatExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>repeat</c> keyword token.</summary>
    public SyntaxTokenNode RepeatKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The repeat-type token (e.g. <c>volta</c>).</summary>
    public SyntaxTokenNode RepeatType => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The repeat-count token.</summary>
    public SyntaxTokenNode Count => (SyntaxTokenNode)GetChild(2)!;
    /// <summary>The repeated music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(3)!;
    /// <summary>The optional trailing alternative clause, or null.</summary>
    public AlternativeClauseSyntax? Alternative => GetChild(4) as AlternativeClauseSyntax;
}

/// <summary>
/// Alternative clause: alternative { { ... } { ... } }
/// </summary>
public sealed class AlternativeClauseSyntax : SyntaxNode
{
    internal AlternativeClauseSyntax(InternalSyntax.AlternativeClauseGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>alternative</c> keyword token.</summary>
    public SyntaxTokenNode AlternativeKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The alternative-ending music blocks, in order.</summary>
    public IEnumerable<MusicBlockSyntax> Alternatives
    {
        get
        {
            for (int i = 2; i < SlotCount - 1; i++)
            {
                if (GetChild(i) is MusicBlockSyntax block)
                    yield return block;
            }
        }
    }
}

/// <summary>
/// Parallel expression: <c>&lt;&lt; expr \\ expr &gt;&gt;</c>
/// </summary>
public sealed class ParallelExpressionSyntax : SyntaxNode
{
    internal ParallelExpressionSyntax(InternalSyntax.ParallelExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The opening <c>&lt;&lt;</c> token.</summary>
    public SyntaxTokenNode OpenAngle => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The closing <c>&gt;&gt;</c> token.</summary>
    public SyntaxTokenNode CloseAngle => (SyntaxTokenNode)GetChild(SlotCount - 1)!;

    /// <summary>
    /// Gets the voice expressions (music blocks or relative expressions between \\).
    /// </summary>
    public IEnumerable<SyntaxNode> Voices
    {
        get
        {
            for (int i = 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child is MusicBlockSyntax)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Each voice block with its optional name (<c>voice sop { … }</c> → "sop";
    /// <c>voice { … }</c> → null). A name token, when present, sits immediately
    /// before its block; a separating <c>voice</c> keyword clears the pending name.
    /// </summary>
    public IEnumerable<(string? Name, MusicBlockSyntax Block)> NamedVoices
    {
        get
        {
            string? pending = null;
            for (int i = 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child is MusicBlockSyntax mb)
                {
                    yield return (pending, mb);
                    pending = null;
                }
                else if (child is SyntaxTokenNode t && t.Kind == SyntaxKind.Identifier)
                    pending = t.Text;
                else
                    pending = null; // a separating `voice` keyword
            }
        }
    }
}

/// <summary>
/// Key signature: key c major
/// </summary>
public sealed class KeySignatureSyntax : SyntaxNode
{
    internal KeySignatureSyntax(InternalSyntax.KeySignatureGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>key</c> keyword token.</summary>
    public SyntaxTokenNode KeyKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>True for <c>key custom …</c> (slot 1 is the word, pitches follow).</summary>
    public bool IsCustom => GetChild(1) is SyntaxTokenNode t
        && t.Text.Equals("custom", StringComparison.OrdinalIgnoreCase);

    /// <summary>The tonic pitch of a traditional key.</summary>
    public PitchSyntax Pitch => (PitchSyntax)GetChild(1)!;
    /// <summary>The mode token (e.g. <c>major</c>, <c>minor</c>).</summary>
    public SyntaxTokenNode Mode => (SyntaxTokenNode)GetChild(2)!;

    /// <summary>True for a major key (a non-custom key whose mode is <c>major</c>).</summary>
    public bool IsMajor => !IsCustom && Mode.Kind == SyntaxKind.MajorKeyword;

    /// <summary>The custom signature's (step 0=C..6=B, alteration) pairs in
    /// print order; empty for a traditional key.</summary>
    public IEnumerable<(int Step, int Alter)> CustomAlterations
    {
        get
        {
            if (!IsCustom) yield break;
            for (int i = 2; i < SlotCount; i++)
                if (GetChild(i) is PitchSyntax p)
                    yield return (
                        "cdefgab".IndexOf(char.ToLowerInvariant(p.PitchName[0])),
                        p.AccidentalOffset);
        }
    }
}

/// <summary>
/// Clef declaration: clef treble
/// </summary>
public sealed class ClefDeclarationSyntax : SyntaxNode
{
    internal ClefDeclarationSyntax(InternalSyntax.ClefDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>clef</c> keyword token.</summary>
    public SyntaxTokenNode ClefKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The clef name token (e.g. <c>treble</c>, <c>bass</c>).</summary>
    public SyntaxTokenNode ClefName => (SyntaxTokenNode)GetChild(1)!;
}

/// <summary>
/// Octave mode directive: <c>octave absolute</c> / <c>octave relative</c>.
/// </summary>
public sealed class OctaveDirectiveSyntax : SyntaxNode
{
    internal OctaveDirectiveSyntax(InternalSyntax.OctaveDirectiveGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>octave</c> keyword token.</summary>
    public SyntaxTokenNode OctaveKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The mode token (<c>absolute</c> or <c>relative</c>).</summary>
    public SyntaxTokenNode Mode => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>True for <c>octave absolute</c>, false for <c>octave relative</c>.</summary>
    public bool IsAbsolute => Mode.Text.Equals("absolute", System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Tuplet expression: tuplet 3/2 { ... }
/// </summary>
public sealed class TupletExpressionSyntax : SyntaxNode
{
    internal TupletExpressionSyntax(InternalSyntax.TupletExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>tuplet</c> keyword token.</summary>
    public SyntaxTokenNode TupletKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The ratio numerator token.</summary>
    public SyntaxTokenNode Numerator => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The <c>/</c> token.</summary>
    public SyntaxTokenNode Slash => (SyntaxTokenNode)GetChild(2)!;
    /// <summary>The ratio denominator token.</summary>
    public SyntaxTokenNode Denominator => (SyntaxTokenNode)GetChild(3)!;
    /// <summary>The tuplet's music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(4)!;

    /// <summary>
    /// Gets the tuplet ratio (e.g., 3 for triplets)
    /// </summary>
    public int TupletRatio => int.TryParse(Numerator.Text, out int n) ? n : 3;

    /// <summary>
    /// Gets the base division (e.g., 2 for triplets in place of 2)
    /// </summary>
    public int BaseDivision => int.TryParse(Denominator.Text, out int d) ? d : 2;
}

/// <summary>
/// Grace expression: grace { notes } or acciaccatura { notes }
/// </summary>
public sealed class GraceExpressionSyntax : SyntaxNode
{
    internal GraceExpressionSyntax(InternalSyntax.GraceExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The grace keyword token (<c>grace</c>, <c>acciaccatura</c>, or <c>appoggiatura</c>).</summary>
    public SyntaxTokenNode GraceKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The grace-note music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(1)!;

    /// <summary>
    /// True if this is an acciaccatura (slashed grace note)
    /// </summary>
    public bool IsAcciaccatura => GraceKeyword.Kind == SyntaxKind.AcciaccaturaKeyword;

    /// <summary>
    /// True if this is an appoggiatura (unslashed grace note)
    /// </summary>
    public bool IsAppoggiatura => GraceKeyword.Kind == SyntaxKind.AppogiaturaKeyword;
}
