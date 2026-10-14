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
/// Repeat expression: <c>repeat unfold 2 { ... }</c> (<c>percent</c>, <c>tremolo</c>) — an
/// abbreviation for notes. A repeat that changes the playing order is written in the form
/// (<c>|: A [1. B] :| [2. C]</c>); LilyPond's <c>\repeat volta</c> is refused where it is
/// written (<see cref="DiagnosticCodes.LilyPondRepeatVolta"/>) and its <c>\alternative</c>
/// is an ordinary word.
/// </summary>
public sealed class RepeatExpressionSyntax : SyntaxNode
{
    internal RepeatExpressionSyntax(InternalSyntax.RepeatExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>repeat</c> keyword token.</summary>
    public SyntaxTokenNode RepeatKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The repeat-kind token (<c>unfold</c>, <c>percent</c>, <c>tremolo</c>).</summary>
    public SyntaxTokenNode RepeatType => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The repeat-count token.</summary>
    public SyntaxTokenNode Count => (SyntaxTokenNode)GetChild(2)!;
    /// <summary>The repeated music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(3)!;
}

/// <summary>
/// A percent mark, <c>%</c> or <c>%N</c>: one repetition of the N bars written before it
/// (docs/percent-repeat-design.md). Inside a <see cref="PercentRepeatSyntax"/> it is one
/// repetition; a mark left outside one (too few bars before it, …) is reported and read as
/// a bar of nothing.
/// </summary>
public sealed class PercentMarkSyntax : SyntaxNode
{
    internal PercentMarkSyntax(InternalSyntax.PercentMarkGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>%</c> / <c>%N</c> token.</summary>
    public SyntaxTokenNode Mark => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The number of bars the mark repeats: <c>%</c> is 1, <c>%N</c> is N; 0 for a
    /// count that is not a number of bars (<c>%0</c>, a count too large to read).</summary>
    public int Bars => LengthOf(Green.GetSlot(0)!.Text);

    internal static int LengthOf(string text) =>
        text.Length == 1 ? 1
        : int.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int n) && n is > 0 and <= MaxBars ? n
        : 0;

    /// <summary>The longest body a mark may repeat — a bound so a typo cannot ask for a
    /// million bars; the owner's books repeat at most eight.</summary>
    public const int MaxBars = 64;
}

/// <summary>
/// A percent repeat, grouped by the parser around a run of marks: the bars repeated (their
/// items, bar lines between them included), the bar line closing them, and each mark with
/// the bar lines between the marks (and any line break beside a mark) — the source's own nodes
/// in its order. The bar line after the last mark is outside, and so are the directives at the
/// head of the first bar (Parser.GroupPercentRepeats). <c>a2 b | c d | %2 | %2 |</c> is the
/// pattern three times.
/// </summary>
public sealed class PercentRepeatSyntax : SyntaxNode
{
    internal PercentRepeatSyntax(InternalSyntax.PercentRepeatGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The slot of the bar line that closes the body: the last one before the first
    /// mark (line breaks may stand between the two).</summary>
    private int BodyEndSlot
    {
        get
        {
            int first = 0;
            while (first < SlotCount && Green.GetSlot(first)?.Kind != SyntaxKind.PercentMarkItem)
                first++;
            int bar = first - 1;
            while (bar > 0 && Green.GetSlot(bar)?.Kind != SyntaxKind.Barline)
                bar--;
            return bar;
        }
    }

    /// <summary>The slot of the first mark.</summary>
    private int FirstMarkSlot
    {
        get
        {
            int first = 0;
            while (first < SlotCount && Green.GetSlot(first)?.Kind != SyntaxKind.PercentMarkItem)
                first++;
            return first;
        }
    }

    /// <summary>The items of the bars repeated, in order, the bar lines between them
    /// included and the one closing them not.</summary>
    public IEnumerable<SyntaxNode> BodyItems
    {
        get
        {
            int end = BodyEndSlot;
            for (int i = 0; i < end; i++)
                if (GetChild(i) is { } child)
                    yield return child;
        }
    }

    /// <summary>The bar line that closes the body, before the first mark.</summary>
    public BarlineSyntax BodyEndBar => (BarlineSyntax)GetChild(BodyEndSlot)!;

    /// <summary>The marks, one per repetition.</summary>
    public IEnumerable<PercentMarkSyntax> Marks
    {
        get
        {
            for (int i = BodyEndSlot + 1; i < SlotCount; i++)
                if (Green.GetSlot(i)?.Kind == SyntaxKind.PercentMarkItem)
                    yield return (PercentMarkSyntax)GetChild(i)!;
        }
    }

    /// <summary>The number of bars repeated — every mark's length (the grouping keeps only
    /// marks of one length together).</summary>
    public int Bars => PercentMarkSyntax.LengthOf(Green.GetSlot(FirstMarkSlot)!.GetSlot(0)!.Text);

    /// <summary>How many times the bars are heard: once written, once per mark.</summary>
    public int Count => 1 + Marks.Count();
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
    /// Each voice block with its optional name (<c>voice sop { … } alt { … }</c> → "sop",
    /// "alt"; <c>voice { … } { … }</c> → null, null). A name token, when present, sits
    /// immediately before its block. A <c>voice</c> keyword clears the pending name — it
    /// appears here only in a file that repeated it (LYS0019) or in the recovered
    /// <c>&lt;&lt; … &gt;&gt;</c> shape.
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

    /// <summary>True for a <c>drums { … }</c> chart (Parser.Drums): its voices are LINES the
    /// page merges by limb, not voices of their own.</summary>
    public bool IsDrums => Green.GetSlot(0)?.Kind == SyntaxKind.DrumsOpener;

    /// <summary>
    /// A chart's lines, each with the side its <c>up</c>/<c>down</c> word puts it on (true =
    /// down), or null where none is written and each kit piece takes its own (hands up, feet
    /// down — <see cref="DrumNameRegistry.IsFootDrum"/>).
    /// </summary>
    public IEnumerable<(MusicBlockSyntax Line, bool? Down)> DrumLines
    {
        get
        {
            bool? pending = null;
            for (int i = 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child is MusicBlockSyntax line)
                {
                    yield return (line, pending);
                    pending = null;
                }
                else if (child is SyntaxTokenNode { Kind: SyntaxKind.DrumsLineDirection } dir)
                    pending = dir.Text == "down";
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

    /// <summary>The <c>!</c> after the keyword (<c>key! …</c>): drawn even if it changes
    /// nothing. Null when not written.</summary>
    public SyntaxTokenNode? ForceMark => GetChild(1) as SyntaxTokenNode;
    /// <summary>True when <see cref="ForceMark"/> is written.</summary>
    public bool IsForced => ForceMark is not null;

    /// <summary>True for <c>key custom …</c> (slot 2 is the word, pitches follow).</summary>
    public bool IsCustom => GetChild(2) is SyntaxTokenNode t
        && t.Text.Equals("custom", StringComparison.OrdinalIgnoreCase);

    /// <summary>The tonic pitch of a traditional key.</summary>
    public PitchSyntax Pitch => (PitchSyntax)GetChild(2)!;

    /// <summary>How the editor names this key — <c>f major</c>, <c>d minor</c>, or
    /// <c>custom</c> for a custom signature, which has no tonic.</summary>
    /// <remarks>The outline and the hover read <see cref="Pitch"/> as if it could be null;
    /// it is a cast, and on <c>key custom …</c> it threw — every book with a custom key lost
    /// its outline (found by the language-server sweep, session 717).</remarks>
    public string DisplayName => IsCustom ? "custom"
        : $"{(GetChild(2) as PitchSyntax)?.PitchName} {(IsMajor ? "major" : "minor")}";
    /// <summary>The mode token (e.g. <c>major</c>, <c>minor</c>).</summary>
    public SyntaxTokenNode Mode => (SyntaxTokenNode)GetChild(3)!;

    /// <summary>True for a major key (a non-custom key whose mode is <c>major</c>).</summary>
    public bool IsMajor => !IsCustom && Mode.Kind == SyntaxKind.MajorKeyword;

    /// <summary>The custom signature's (step 0=C..6=B, alteration) pairs in
    /// print order; empty for a traditional key.</summary>
    public IEnumerable<(int Step, int Alter)> CustomAlterations
    {
        get
        {
            if (!IsCustom) yield break;
            for (int i = 3; i < SlotCount; i++)
                if (GetChild(i) is PitchSyntax p)
                    yield return (
                        "cdefgab".IndexOf(p.BaseName),
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
    /// <summary>The <c>!</c> after the keyword (<c>clef! …</c>): drawn even if it changes
    /// nothing. Null when not written.</summary>
    public SyntaxTokenNode? ForceMark => GetChild(1) as SyntaxTokenNode;
    /// <summary>True when <see cref="ForceMark"/> is written.</summary>
    public bool IsForced => ForceMark is not null;
    /// <summary>The clef name token (e.g. <c>treble</c>, <c>bass</c>).</summary>
    public SyntaxTokenNode ClefName => (SyntaxTokenNode)GetChild(2)!;
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
    public bool IsAppoggiatura => GraceKeyword.Kind == SyntaxKind.AppoggiaturaKeyword;

    /// <summary>
    /// True for <c>afterGrace { … }</c>: grace notes AFTER the note written before it, played
    /// at the end of that note and drawn before what follows it (LilyPond's <c>\afterGrace</c>,
    /// which places them at 3/4 of the main note — just before the next column).
    /// </summary>
    public bool IsAfterGrace => GraceKeyword.Kind == SyntaxKind.AfterGraceKeyword;
}

/// <summary>
/// Cue expression: <c>cue { notes }</c> or <c>cue &lt;clef&gt; { notes }</c> — a REGION of
/// cue-sized music, which is the shape LilyPond actually has.
/// </summary>
/// <remarks>
/// LilyPond has no "this note is a cue": its cue is the <c>CueVoice</c> CONTEXT and the size
/// comes from <c>fontSize = #-4</c>, a context property (ly/engraver-init.ly). A per-note
/// mark cannot say where the region starts and ends, and the boundary is observable —
/// MEASURED in audit/lp-geometry/probes/cue-span.ly, a beam, a tie and a slur all fail to
/// cross it (only the accidental state does), so all three would have to be guessed.
/// <para>
/// The optional clef is LilyPond's <c>\cueClef</c>, which is a property of the REGION: it
/// draws a SMALL clef (font-size −4) before the region, positions the cue's notes in that
/// clef, and needs <c>\cueClefUnset</c> after it or the change leaks into the rest of the
/// staff (measured, same probe, books D-WITH / D-NOUNSET).
/// </para>
/// See docs/cue-context-design.md.
/// </remarks>
public sealed class CueExpressionSyntax : SyntaxNode
{
    internal CueExpressionSyntax(InternalSyntax.CueExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>cue</c> keyword token.</summary>
    public SyntaxTokenNode CueKeyword => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>The cue clef keyword, or null when the region keeps the staff's clef.</summary>
    public SyntaxTokenNode? ClefKeyword => GetChild(1) as SyntaxTokenNode;

    /// <summary>The cue region's music block.</summary>
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(2)!;
}
