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
/// Compilation unit - the root of a syntax tree.
/// </summary>
public sealed class CompilationUnitSyntax : SyntaxNode
{
    internal CompilationUnitSyntax(CompilationUnitGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// All members (notes, declarations, etc.)
    /// </summary>
    public IEnumerable<SyntaxNode> Members
    {
        get
        {
            for (int i = 0; i < SlotCount - 1; i++) // -1 to exclude EOF
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// Music block: { ... }
/// </summary>
public sealed class MusicBlockSyntax : SyntaxNode
{
    internal MusicBlockSyntax(MusicBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The opening <c>{</c> token.</summary>
    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The closing <c>}</c> token.</summary>
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;

    /// <summary>The items contained in the block, in source order.</summary>
    public IEnumerable<SyntaxNode> Items
    {
        get
        {
            for (int i = 1; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A pitch: c, cis', des,,
/// </summary>
public sealed class PitchSyntax : SyntaxNode
{
    internal PitchSyntax(PitchGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The pitch-name token (letter with accidental suffix).</summary>
    public SyntaxTokenNode PitchToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// The base pitch name (c, d, e, f, g, a, b) with accidentals.
    /// </summary>
    public string PitchName => PitchToken.Text;

    /// <summary>
    /// Number of octave marks (' positive, , negative).
    /// </summary>
    public int OctaveOffset
    {
        get
        {
            int offset = 0;
            for (int i = 1; i < SlotCount; i++)
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

    /// <summary>
    /// The base pitch letter (c, d, e, f, g, a, b) without accidentals.
    /// </summary>
    public char BaseName => char.ToLower(PitchName[0]);

    /// <summary>
    /// The accidental suffix (is, es, isis, eses, s, as) or empty string.
    /// </summary>
    public string Accidental => PitchName.Length > 1 ? PitchName[1..] : string.Empty;

    /// <summary>
    /// Per-pitch articulations attached inside a chord (e.g., <c>&lt;c@finger.1&gt;</c>).
    /// Returns the syntax-level articulation nodes after the octave marks.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lily-parser.yy chord_body grammar — post-event articulations.
    /// </remarks>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Gets the accidental as semitone offset (-2 to +2).
    /// </summary>
    public int AccidentalOffset => Accidental switch
    {
        "isis" => 2,
        "isih" => 1,
        "is" => 1,
        "" => 0,
        "es" => -1,
        "eseh" => -1,
        "eses" => -2,
        _ => 0
    };

    /// <summary>Quarter-tone offset on top of <see cref="AccidentalOffset"/>:
    /// +1 = a quarter sharp higher (ih / isih), -1 = a quarter flat lower
    /// (eh / eseh), 0 otherwise. LILYPOND-REF: quarter-tone note names.</summary>
    public int QuarterOffset => Accidental switch
    {
        "ih" or "isih" => 1,
        "eh" or "eseh" => -1,
        _ => 0
    };
}

/// <summary>
/// A duration: 4, 8., 16..
/// </summary>
public sealed class DurationSyntax : SyntaxNode
{
    internal DurationSyntax(DurationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The base-duration number token (before any dots).</summary>
    public SyntaxTokenNode NumberToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// The base duration value (1, 2, 4, 8, 16, etc.)
    /// </summary>
    public int Value => int.Parse(NumberToken.Text);

    /// <summary>
    /// Number of dots.
    /// </summary>
    public int DotCount => SlotCount - 1;

    /// <summary>
    /// Converts to a Fraction representing the duration.
    /// </summary>
    public Fraction ToFraction() => Fraction.FromNoteValue(Value).Dotted(DotCount);
}

/// <summary>
/// A note: pitch + optional duration + optional tremolo + articulations
/// </summary>
public sealed class NoteSyntax : SyntaxNode
{
    internal NoteSyntax(NoteGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The note's pitch.</summary>
    public PitchSyntax Pitch => (PitchSyntax)GetChild(0)!;
    /// <summary>The note's duration, or null when unspecified (inherited from the previous note).</summary>
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;

    /// <summary>
    /// Gets the tremolo suffix (:8, :16, :32) if present.
    /// </summary>
    public SyntaxTokenNode? Tremolo => GetChild(2) as SyntaxTokenNode;

    /// <summary>
    /// Gets the articulations and dynamics attached to this note.
    /// </summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 3; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// <c>drummap { hh: position 6 notehead x … }</c> — per-score overrides of
/// the built-in drum table (position / notehead / midi / mark of EXISTING
/// registry names; new instrument names are out of scope, the parser's drum
/// vocabulary is static).
/// </summary>
public sealed class DrummapDeclarationSyntax : SyntaxNode
{
    internal DrummapDeclarationSyntax(InternalSyntax.DrummapDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The entries: (drum name, key → value). Keys: position
    /// (leading minus supported), notehead, midi, mark.</summary>
    public IEnumerable<(string Name, Dictionary<string, string> Settings)> Entries
    {
        get
        {
            // Token stream: name colon (key value)* … next name colon …
            var tokens = new List<string>();
            for (int i = 2; i < SlotCount - 1; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    tokens.Add(t.Text);

            string? name = null;
            Dictionary<string, string>? settings = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (i + 1 < tokens.Count && tokens[i + 1] == ":")
                {
                    if (name != null && settings != null)
                        yield return (name, settings);
                    name = tokens[i];
                    settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    i++; // skip the colon
                    continue;
                }
                if (settings == null || i + 1 >= tokens.Count)
                    continue;
                string key = tokens[i].ToLowerInvariant();
                string value = tokens[i + 1];
                if (value == "-" && i + 2 < tokens.Count)
                {
                    value = "-" + tokens[i + 2];
                    i++;
                }
                settings[key] = value;
                i++;
            }
            if (name != null && settings != null)
                yield return (name, settings);
        }
    }
}

/// <summary>
/// Error-recovery node for a nested <c>voice { }</c> (LYS0010): a neutral
/// wrapper whose block content inlines into the enclosing voice — walkers
/// reach the notes as ordinary descendants.
/// </summary>
public sealed class NestedVoiceRecoverySyntax : SyntaxNode
{
    internal NestedVoiceRecoverySyntax(InternalSyntax.NestedVoiceRecoveryGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}

/// <summary>
/// A drum note: <c>bd4</c>, <c>sn8</c>, <c>hh</c> — a DrumNameRegistry name
/// with the same trailing structure as a pitched note.
/// </summary>
/// <remarks>LILYPOND-REF: \drummode note events.</remarks>
public sealed class DrumNoteSyntax : SyntaxNode
{
    internal DrumNoteSyntax(InternalSyntax.DrumNoteGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The drum-name token.</summary>
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The drum instrument name (a DrumNameRegistry entry).</summary>
    public string DrumName => NameToken.Text;
    /// <summary>The drum note's duration, or null when unspecified.</summary>
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;
    /// <summary>The tremolo suffix (:8, :16, :32), or null.</summary>
    public SyntaxTokenNode? Tremolo => GetChild(2) as SyntaxTokenNode;

    /// <summary>The articulations and dynamics attached to this drum note.</summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 3; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A rest: <c>r</c>, <c>s</c>, <c>R</c> + optional duration, with optional
/// <c>*N</c> multi-measure count.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/multi-measure-rest.cc — multi-measure rest grob.
/// </remarks>
public sealed class RestSyntax : SyntaxNode
{
    internal RestSyntax(RestGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The rest token (<c>r</c>, <c>s</c>, or <c>R</c>).</summary>
    public SyntaxTokenNode RestToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The rest's duration, or null when unspecified.</summary>
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;

    /// <summary>
    /// Multi-measure rest count (the N in <c>R1*N</c>). Returns 1 when no
    /// <c>*N</c> multiplier was provided.
    /// </summary>
    public int MeasureCount
    {
        get
        {
            if (GetChild(3) is SyntaxTokenNode countToken &&
                int.TryParse(countToken.Text, out int n) && n >= 1)
            {
                return n;
            }
            return 1;
        }
    }

    /// <summary>True iff this rest carries a <c>*N</c> multi-measure multiplier.</summary>
    public bool IsMultiMeasure => MeasureCount > 1;

    /// <summary>
    /// Articulations / marks / dynamics attached to the rest (e.g.
    /// <c>r4@fermata</c> — a fermata over a rest is standard notation).
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/lily-parser.yy — post-events attach to rests too
    /// (<c>r4\fermata</c>).</remarks>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 4; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A chord: <c>&lt; pitches &gt;</c> + optional duration
/// </summary>
public sealed class ChordSyntax : SyntaxNode
{
    internal ChordSyntax(ChordGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Drum-name members of a drum chord (&lt;bd hh&gt;).</summary>
    public IEnumerable<DrumNoteSyntax> DrumNames
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is DrumNoteSyntax d)
                    yield return d;
        }
    }

    /// <summary>The chord's pitch members.</summary>
    public IEnumerable<PitchSyntax> Pitches
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is PitchSyntax pitch)
                    yield return pitch;
            }
        }
    }

    /// <summary>
    /// Gets the duration of the chord (after the closing angle bracket).
    /// </summary>
    public DurationSyntax? Duration
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is DurationSyntax duration)
                    return duration;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the tremolo suffix (:8, :16, :32) if present.
    /// </summary>
    public SyntaxTokenNode? Tremolo
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is SyntaxTokenNode token && token.Kind == SyntaxKind.TremoloSuffix)
                    return token;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the articulations, dynamics, and music marks attached to this chord.
    /// </summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 0; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A barline: |, ||, |., etc.
/// </summary>
public sealed class BarlineSyntax : SyntaxNode
{
    internal BarlineSyntax(BarlineGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The barline token.</summary>
    public SyntaxTokenNode BarToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Explicit volta-repeat play count from a <c>:|*N</c> end-repeat barline.
    /// Returns the default (2) when no <c>*N</c> multiplier is present. Only
    /// meaningful on a <c>:|</c> (<see cref="SyntaxKind.RepeatEndBar"/>) barline.
    /// </summary>
    public int RepeatCount
    {
        get
        {
            if (GetChild(2) is SyntaxTokenNode countToken &&
                int.TryParse(countToken.Text, out int n) && n >= 1)
            {
                return n;
            }
            return 2;
        }
    }

    /// <summary>True iff this barline carries an explicit <c>:|*N</c> repeat count.</summary>
    public bool HasExplicitRepeatCount => GetChild(2) is SyntaxTokenNode;
}

/// <summary>
/// A tie: ~
/// </summary>
public sealed class TieSyntax : SyntaxNode
{
    internal TieSyntax(TieGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}

/// <summary>
/// Slur markers: ( or )
/// </summary>
public sealed class SlurSyntax : SyntaxNode
{
    internal SlurSyntax(SlurGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>True for a slur-open <c>(</c>; false for a slur-close <c>)</c>.</summary>
    public bool IsOpen => ((SyntaxTokenNode)GetChild(0)!).Kind == SyntaxKind.OpenParen;
}

/// <summary>
/// Beam markers: [ or ]
/// </summary>
public sealed class BeamMarkerSyntax : SyntaxNode
{
    internal BeamMarkerSyntax(BeamMarkerGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>True for a beam-start <c>[</c>; false for a beam-end <c>]</c>.</summary>
    public bool IsStart => ((SyntaxTokenNode)GetChild(0)!).Kind == SyntaxKind.OpenBracket;
}

/// <summary>
/// An inline volta ending in a <c>|: … :|</c> repeat: <c>[1. … ]</c> (or a range
/// <c>[1-2. …]</c> / list <c>[1,3. …]</c>). Carries the volta number(s) and the
/// ending's literal music; selected per repeat pass for playback and drawn as a
/// volta bracket over its measures.
/// </summary>
public sealed class InlineVoltaSyntax : SyntaxNode
{
    internal InlineVoltaSyntax(InlineVoltaGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The (first) volta pass-number token.</summary>
    public SyntaxTokenNode Number => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>True for a range/list form like <c>[1-2. …]</c> or <c>[1,3. …]</c>.</summary>
    public bool HasSeparator => GetChild(2) is SyntaxTokenNode;
    /// <summary>The range/list separator token (<c>-</c> or <c>,</c>), or null.</summary>
    public SyntaxTokenNode? Separator => GetChild(2) as SyntaxTokenNode;
    /// <summary>The end volta-number token for a range/list form, or null.</summary>
    public SyntaxTokenNode? EndNumber => GetChild(3) as SyntaxTokenNode;

    /// <summary>Display text for the bracket label, e.g. "1.", "1-2.", "1,3.".</summary>
    public string VoltaText => HasSeparator
        ? $"{Number.Text}{Separator!.Text}{EndNumber!.Text}."
        : $"{Number.Text}.";

    /// <summary>The ending's music items (between the dot and the closing bracket).</summary>
    public IEnumerable<SyntaxNode> Items
    {
        get
        {
            // Slots: [ number sep? endNumber? . items… ]  — items start after the dot
            // (slot 4) and stop before the closing bracket (last slot).
            for (int i = 5; i < SlotCount - 1; i++)
            {
                if (GetChild(i) is SyntaxNode n && n is not SyntaxTokenNode)
                    yield return n;
            }
        }
    }

    /// <summary>True when the ending is terminated by a closing <c>]</c> — its right
    /// cap (down hook) is drawn. Omitting the <c>]</c> leaves the ending open on the
    /// right; the bracket then runs to the next boundary (another ending, the repeat
    /// barline, or the end of the block), and the engraver still opens line-break pieces.</summary>
    public bool IsClosed => GetChild(SlotCount - 1) is SyntaxTokenNode { Kind: SyntaxKind.CloseBracket };

    /// <summary>The set of pass numbers this ending applies to.</summary>
    public IEnumerable<int> Numbers
    {
        get
        {
            int start = int.Parse(Number.Text);
            if (HasSeparator && EndNumber != null && int.TryParse(EndNumber.Text, out int end))
            {
                if (Separator!.Kind == SyntaxKind.Minus)
                {
                    for (int n = start; n <= end; n++)
                        yield return n;
                }
                else // comma list: [1,3. …]
                {
                    yield return start;
                    yield return end;
                }
            }
            else
            {
                yield return start;
            }
        }
    }

    /// <summary>Highest pass number this ending covers (drives the inferred repeat count).</summary>
    public int MaxNumber => Numbers.Max();

    /// <summary>True iff this ending should play on the given (1-based) repeat pass.</summary>
    public bool Matches(int pass) => Numbers.Contains(pass);
}

/// <summary>
/// Staff declaration: staff Name { ... }
/// </summary>
public sealed class StaffDeclarationSyntax : SyntaxNode
{
    internal StaffDeclarationSyntax(StaffDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>staff</c> keyword token.</summary>
    public SyntaxTokenNode StaffKeyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The staff name token, or null when the staff is unnamed.</summary>
    public SyntaxTokenNode? Name => GetChild(1) as SyntaxTokenNode;
}

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
                         (t.Kind == SyntaxKind.IntegerLiteral || t.Kind == SyntaxKind.DurationNumber) &&
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
                if ((token.Kind == SyntaxKind.IntegerLiteral || token.Kind == SyntaxKind.DurationNumber) &&
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
                    if (token.Kind == SyntaxKind.IntegerLiteral || token.Kind == SyntaxKind.DurationNumber)
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
                if (t.Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DurationNumber)
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
                    (token.Kind == SyntaxKind.IntegerLiteral || token.Kind == SyntaxKind.DurationNumber))
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
/// Variable declaration: let name = expr
/// </summary>
public sealed class VariableDeclarationSyntax : SyntaxNode
{
    internal VariableDeclarationSyntax(VariableDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // New style (3 slots): name = expression
    // Legacy style (4 slots): let name = expression
    private bool IsLegacyStyle => SlotCount == 4;

    /// <summary>The legacy <c>let</c> keyword token, or null in the new <c>name = expr</c> form.</summary>
    public SyntaxTokenNode? LetKeyword => IsLegacyStyle ? (SyntaxTokenNode)GetChild(0)! : null;
    /// <summary>The declared variable name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(IsLegacyStyle ? 1 : 0)!;
    /// <summary>The <c>=</c> token.</summary>
    public SyntaxTokenNode EqualsToken => (SyntaxTokenNode)GetChild(IsLegacyStyle ? 2 : 1)!;
    /// <summary>The assigned expression.</summary>
    public SyntaxNode Expression => GetChild(IsLegacyStyle ? 3 : 2)!;
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

    // Name can be at index 0 (single-arg constructor) or index 1 (two-arg with keyword)
    /// <summary>The referenced variable name token.</summary>
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(SlotCount > 1 ? 1 : 0)!;
}

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
    /// <summary>The lyric syllable items, in order.</summary>
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

    /// <summary>The chord entries and barlines, in source order.</summary>
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

    /// <summary>The closing <c>}</c> token.</summary>
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;
}

/// <summary>
/// A single chord entry: root[duration][:quality][/bass] (e.g. c1, a:m, g2:7,
/// d:m7/f). The quality token is the raw text after the colon (resolved against
/// <c>ChordQualityRegistry</c> by the collector).
/// </summary>
public sealed class ChordEntrySyntax : SyntaxNode
{
    internal ChordEntrySyntax(InternalSyntax.ChordEntryGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    // Slots: 0 root, 1 duration?, 2 colon?, [quality tokens…], slash?, bass?
    // (slash/bass are always the final two slots, null when absent).
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
            for (int i = 3; i < SlotCount - 2; i++)
                if (GetChild(i) is SyntaxTokenNode t)
                    sb.Append(t.Text);
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }

    /// <summary>The slash-bass pitch (<c>c/g</c>), or null.</summary>
    public PitchSyntax? Bass => GetChild(SlotCount - 1) as PitchSyntax;
}

/// <summary>
/// An articulation mark: @staccato, @accent, etc.
/// </summary>
public sealed class ArticulationSyntax : SyntaxNode
{
    internal ArticulationSyntax(InternalSyntax.ArticulationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The leading <c>@</c> token.</summary>
    public SyntaxTokenNode AtToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The articulation name token.</summary>
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>
    /// Gets the articulation type by resolving the <c>@name</c> text (and its
    /// abbreviations) against <see cref="ArticulationRegistry"/>. Returns
    /// <see cref="ArticulationType.None"/> for non-articulation marks (e.g. a
    /// music mark), which are then resolved by name downstream.
    /// </summary>
    public ArticulationType Type => ArticulationRegistry.Resolve(NameToken.Text);

    /// <summary>
    /// Forced placement from a <c>.up</c> / <c>.down</c> qualifier
    /// (<c>@staccato.up</c>): <c>true</c> = above, <c>false</c> = below,
    /// <c>null</c> = automatic (opposite the stem, the default).
    /// </summary>
    public bool? ForcedAbove => GetChild(2) is SyntaxTokenNode dir
        ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
        : null;
}

/// <summary>
/// A dynamic mark: \p, \f, \ff, etc.
/// </summary>
public sealed class DynamicSyntax : SyntaxNode
{
    internal DynamicSyntax(InternalSyntax.DynamicGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The leading <c>\</c> token.</summary>
    public SyntaxTokenNode BackslashToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The dynamic name token (e.g. <c>p</c>, <c>f</c>, <c>ff</c>).</summary>
    public SyntaxTokenNode DynamicToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>
    /// Forced placement from a <c>.up</c> / <c>.down</c> qualifier (<c>@f.up</c>):
    /// <c>true</c> = above, <c>false</c> = below, <c>null</c> = default (below).
    /// </summary>
    public bool? ForcedAbove => GetChild(2) is SyntaxTokenNode dir
        ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
        : null;

    /// <summary>
    /// Gets the dynamic level.
    /// </summary>
    public DynamicLevel Level
    {
        get
        {
            // First try by Kind (for proper dynamic tokens)
            var byKind = DynamicToken.Kind switch
            {
                SyntaxKind.DynamicPPP => DynamicLevel.PPP,
                SyntaxKind.DynamicPP => DynamicLevel.PP,
                SyntaxKind.DynamicP => DynamicLevel.P,
                SyntaxKind.DynamicMP => DynamicLevel.MP,
                SyntaxKind.DynamicMF => DynamicLevel.MF,
                SyntaxKind.DynamicF => DynamicLevel.F,
                SyntaxKind.DynamicFF => DynamicLevel.FF,
                SyntaxKind.DynamicFFF => DynamicLevel.FFF,
                _ => DynamicLevel.None
            };

            if (byKind != DynamicLevel.None) return byKind;

            // Fallback to text matching (for pitch tokens used as dynamics like \f,
            // and for the accent dynamics, which lex as plain identifiers).
            return DynamicToken.Text switch
            {
                "ppppp" => DynamicLevel.PPPPP,
                "pppp" => DynamicLevel.PPPP,
                "ppp" => DynamicLevel.PPP,
                "pp" => DynamicLevel.PP,
                "p" => DynamicLevel.P,
                "mp" => DynamicLevel.MP,
                "mf" => DynamicLevel.MF,
                "fp" => DynamicLevel.FP,
                "f" => DynamicLevel.F,
                "sf" => DynamicLevel.SF,
                "ff" => DynamicLevel.FF,
                "sfz" => DynamicLevel.SFZ,
                "rf" => DynamicLevel.RF,
                "rfz" => DynamicLevel.RFZ,
                "fz" => DynamicLevel.FZ,
                "sffz" => DynamicLevel.SFFZ,
                "fffff" => DynamicLevel.FFFFF,
                "ffff" => DynamicLevel.FFFF,
                "fff" => DynamicLevel.FFF,
                _ => DynamicLevel.None
            };
        }
    }

    /// <summary>
    /// Gets the MIDI velocity value for this dynamic.
    /// </summary>
    public int Velocity => (int)Level;
}

// ============================================================
// Tablature Nodes
// ============================================================

/// <summary>
/// Represents a tuning declaration: \tuning guitar | \tuning bass | \tuning custom { pitches }
/// </summary>
public sealed partial class TuningDeclarationSyntax : SyntaxNode
{
    internal TuningDeclarationSyntax(TuningDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The leading <c>\</c> token.</summary>
    public SyntaxTokenNode BackslashToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The <c>tuning</c> keyword token.</summary>
    public SyntaxTokenNode TuningKeyword => (SyntaxTokenNode)GetChild(1)!;
    /// <summary>The tuning name token (e.g. <c>guitar</c>, <c>bass</c>, <c>custom</c>).</summary>
    public SyntaxTokenNode TuningName => (SyntaxTokenNode)GetChild(2)!;

    /// <summary>
    /// Gets the tuning type.
    /// </summary>
    public TuningType Type => TuningName.Text.ToLowerInvariant() switch
    {
        "guitar" => TuningType.Guitar,
        "bass" => TuningType.Bass,
        "bass5" => TuningType.Bass5,
        "bass6" => TuningType.Bass6,
        "ukulele" => TuningType.Ukulele,
        _ => TuningType.Guitar
    };
}

/// <summary>
/// Represents a string number annotation: \1, \2, \3, etc.
/// </summary>
public sealed partial class StringNumberAnnotationSyntax : SyntaxNode
{
    internal StringNumberAnnotationSyntax(StringNumberAnnotationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The string-number token (the full <c>\N</c> annotation, e.g. <c>\4</c>).</summary>
    public SyntaxTokenNode StringNumberToken => (SyntaxTokenNode)GetChild(0)!;

    /// <summary>
    /// Gets the string number (1-based). The token text is the full <c>\N</c>
    /// annotation (e.g. "\4"), so the leading backslash is stripped before parsing.
    /// </summary>
    public int StringNumber => int.Parse(StringNumberToken.Text.TrimStart('\\'));
}

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
/// Represents an include directive: include "file.lys". Resolved by the include
/// expander before collection; in the parsed tree it is an inert marker.
/// </summary>
public sealed class IncludeDirectiveSyntax : SyntaxNode
{
    internal IncludeDirectiveSyntax(InternalSyntax.IncludeDirectiveGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>include</c> keyword token.</summary>
    public SyntaxTokenNode Keyword => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The quoted file-path token.</summary>
    public SyntaxTokenNode PathToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The included file path, with surrounding quotes stripped.</summary>
    public string Path => PathToken.Text.Trim('"');
}

/// <summary>
/// An optional language-version directive: <c>version "1"</c>. A top-level marker
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
    /// <summary>The quoted version value token.</summary>
    public SyntaxTokenNode ValueToken => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>The declared version string, with surrounding quotes stripped
    /// (e.g. <c>"1"</c> → <c>1</c>).</summary>
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
/// Represents a structure declaration: structure { ... }
/// </summary>
public sealed partial class StructureDeclarationSyntax : SyntaxNode
{
    internal StructureDeclarationSyntax(StructureDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>structure</c> keyword token.</summary>
    public SyntaxTokenNode StructureKeyword => (SyntaxTokenNode)GetChild(0)!;
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
public sealed partial class StructureRepeatBlockSyntax : SyntaxNode
{
    internal StructureRepeatBlockSyntax(StructureRepeatBlockGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}

/// <summary>
/// Represents an alternative in structure: 1. SectionName or [1. SectionName] or [1-3. SectionName] or [1. ~SectionName]
/// </summary>
public sealed partial class StructureAlternativeSyntax : SyntaxNode
{
    internal StructureAlternativeSyntax(StructureAlternativeGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// True if this is bracket style [1. A], false if legacy style 1. A
    /// </summary>
    public bool HasBracket => ((SyntaxTokenNode)GetChild(0)!).Kind == SyntaxKind.OpenBracket;

    /// <summary>
    /// True if this has a range separator (- or ,) like [1-3. A] or [1,3. A]
    /// Slot layout: Bracket with separator has 8 slots, without has 6 slots
    /// </summary>
    public bool HasSeparator => HasBracket && SlotCount == 8;

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

    /// <summary>The <c>render</c> keyword token.</summary>
    public SyntaxTokenNode RenderKeyword => (SyntaxTokenNode)GetChild(0)!;

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

    /// <summary>The <c>break</c> keyword token.</summary>
    public SyntaxTokenNode BreakKeyword => (SyntaxTokenNode)GetChild(0)!;
}

/// <summary>
/// Marker indicating the start of a new section.
/// Used to reset relative pitch resolver at section boundaries.
/// </summary>
public sealed class SectionStartMarkerSyntax : SyntaxNode
{
    internal SectionStartMarkerSyntax(SectionStartMarkerGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>
    /// Creates a new instance of SectionStartMarkerSyntax.
    /// </summary>
    public static SectionStartMarkerSyntax Create(int position)
        => new(SectionStartMarkerGreen.Instance, null, position);
}

/// <summary>
/// Override declaration: override Grob.property = value
/// LILYPOND-REF: lily/context-property.cc (push/override)
/// </summary>
public sealed class OverrideDeclarationSyntax : SyntaxNode
{
    internal OverrideDeclarationSyntax(InternalSyntax.OverrideDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Grob type name (e.g., "Stem", "Beam").</summary>
    public SyntaxTokenNode GrobName => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>Property name (e.g., "length", "thickness").</summary>
    public SyntaxTokenNode PropertyName => (SyntaxTokenNode)GetChild(3)!;

    /// <summary>Value token.</summary>
    public SyntaxTokenNode ValueToken => (SyntaxTokenNode)GetChild(5)!;
}

/// <summary>
/// Revert declaration: revert Grob.property
/// LILYPOND-REF: lily/context-property.cc (pop/revert)
/// </summary>
public sealed class RevertDeclarationSyntax : SyntaxNode
{
    internal RevertDeclarationSyntax(InternalSyntax.RevertDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Grob type name.</summary>
    public SyntaxTokenNode GrobName => (SyntaxTokenNode)GetChild(1)!;

    /// <summary>Property name.</summary>
    public SyntaxTokenNode PropertyName => (SyntaxTokenNode)GetChild(3)!;
}

/// <summary>
/// Once modifier: once override/revert ...
/// LILYPOND-REF: lily/context-property.cc (temporary_override/revert)
/// </summary>
public sealed class OnceModifierSyntax : SyntaxNode
{
    internal OnceModifierSyntax(InternalSyntax.OnceModifierGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The command modified by once (override or revert).</summary>
    public SyntaxNode Command => GetChild(1)!;
}