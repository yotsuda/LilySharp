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
    /// <remarks>
    /// The lexer emits LilyPond's Dutch contractions <c>es</c>/<c>as</c> verbatim;
    /// normalize them to the canonical flats <c>ees</c>/<c>aes</c> here so every
    /// decoder (BaseName / Accidental / AccidentalOffset / exporters) sees a known
    /// spelling. The raw token text is untouched, so source spans stay correct.
    /// </remarks>
    public string PitchName => PitchToken.Text switch
    {
        "es" => "ees",
        "as" => "aes",
        var t => t,
    };

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
    /// LILYPOND-REF: lily/parser.yy chord_body grammar — post-event articulations.
    /// ⚠️ This is a TYPE FILTER, and until 2026-08-09 it silently swallowed the
    /// member's string number (<c>&lt;e dis'\4&gt;</c>): the parser attached a
    /// StringNumberAnnotation, the collector asked for it with OfType, and this
    /// list never yielded it — the chord fretted as if unforced
    /// (tablature.ly's claim is exactly these entry forms).
    /// </remarks>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 1; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax
                    or StringNumberAnnotationSyntax)
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
    /// The base duration value (1, 2, 4, 8, 16, etc.). Falls back to a quarter (4) when the
    /// number token is empty or malformed — an error-recovery duration synthesised for a
    /// broken input (e.g. <c>partial .</c>) must never throw here, or it takes the whole
    /// render / diagnostics pass down with it (that emptied the Problems panel).
    /// </summary>
    public int Value => int.TryParse(NumberToken.Text, out int v) ? v : 4;

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
    /// <remarks>LILYPOND-REF: lily/parser.yy — post-events attach to rests too
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
/// <summary>
/// A scale-degree chord member (<c>3</c> / <c>3is</c> / <c>7,</c>): a degree
/// number, an optional glued accidental, and octave marks. Resolved against the
/// chord's root and the current key into a concrete pitch.
/// </summary>
public sealed class ScaleDegreeSyntax : SyntaxNode
{
    internal ScaleDegreeSyntax(ScaleDegreeGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The degree token — an <c>IntegerLiteral</c> (<c>3</c>) or a
    /// <c>ScaleDegree</c> (<c>3is</c>).</summary>
    public SyntaxTokenNode DegreeToken => (SyntaxTokenNode)GetChild(0)!;

    private string Text => DegreeToken.Text;

    private int DigitLength
    {
        get { int i = 0; while (i < Text.Length && char.IsDigit(Text[i])) i++; return i; }
    }

    /// <summary>The degree: 1 = root/unison, 3 = third, 7 = seventh, 8 = octave,
    /// 9 = ninth, … (N = up N−1 diatonic scale steps from the root).</summary>
    public int Number => int.TryParse(Text.AsSpan(0, DigitLength), out int n) ? n : 1;

    /// <summary>Chromatic alteration from the glued accidental: <c>is</c>=+1,
    /// <c>isis</c>=+2, <c>es</c>=−1, <c>eses</c>=−2, none = 0.</summary>
    public int Alteration => Text[DigitLength..] switch
    {
        "is" => 1,
        "isis" => 2,
        "es" => -1,
        "eses" => -2,
        _ => 0,
    };

    /// <summary>Net octave shift from the trailing <c>'</c> / <c>,</c> marks.</summary>
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
}

/// <summary>
/// An arpeggio (<c>&lt;&lt; c e g &gt;&gt;</c>): the notes play sequentially but their
/// octaves anchor to the first note like a chord. An optional trailing duration is the
/// group's target total (auto-tuplet).
/// </summary>
public sealed class ArpeggioSyntax : SyntaxNode
{
    internal ArpeggioSyntax(ArpeggioGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The arpeggio's members in source order — bare pitches, scale degrees,
    /// nested chords, and/or rests (<c>&lt;&lt; c 3 &lt;e g&gt; r &gt;&gt;</c>), NONE of
    /// which carry a duration. Each plays in sequence and takes an equal share of the
    /// group's total; a rest is a gap that takes no part in the octave anchoring.</summary>
    public IEnumerable<SyntaxNode> Members
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is PitchSyntax or ScaleDegreeSyntax or ChordSyntax or RestSyntax)
                    yield return GetChild(i)!;
        }
    }

    /// <summary>Net octave shift from the <c>'</c> / <c>,</c> marks AFTER the closing
    /// <c>&gt;&gt;</c> (<c>&lt;&lt; c e g &gt;&gt;,</c> = the whole group down an octave), the
    /// same rule as a chord's <c>&lt;c e g&gt;,</c>. A member's own marks live inside its
    /// pitch/degree node, so only the group-level marks — direct apostrophe/comma tokens —
    /// count here.</summary>
    public int OctaveOffset
    {
        get
        {
            int offset = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode t)
                {
                    if (t.Kind == SyntaxKind.Apostrophe) offset++;
                    else if (t.Kind == SyntaxKind.Comma) offset--;
                }
            }
            return offset;
        }
    }

    /// <summary>The optional total duration written after <c>&gt;&gt;</c> (the group's
    /// total that the members equally subdivide), or null when the group inherits the
    /// running duration and acts like a single note.</summary>
    public DurationSyntax? TotalDuration
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is DurationSyntax d)
                    return d;
            return null;
        }
    }

    /// <summary>The articulations, dynamics, and music marks attached after the
    /// closing <c>&gt;&gt;</c> (<c>&lt;&lt; c e g &gt;&gt;@chord</c>), like a chord's.</summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax)
                    yield return child;
            }
        }
    }
}

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

    /// <summary>The scale-degree members (<c>&lt;d 3 5 7,&gt;</c> → 3, 5, 7,). Empty
    /// for an ordinary all-pitch chord.</summary>
    public IEnumerable<ScaleDegreeSyntax> Degrees
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is ScaleDegreeSyntax d)
                    yield return d;
        }
    }

    /// <summary>The root pitch (first member) of a degree chord — the anchor the
    /// degrees stack on; null if the chord has no pitch member.</summary>
    public PitchSyntax? Root
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (GetChild(i) is PitchSyntax p)
                    return p;
            return null;
        }
    }

    /// <summary>Net octave shift from the <c>'</c> / <c>,</c> marks AFTER the closing
    /// <c>&gt;</c> (<c>&lt;1 3 5&gt;'</c> = whole chord up an octave). A member's own
    /// marks are inside its pitch/degree node, so only the chord-level marks — direct
    /// apostrophe/comma tokens — count here.</summary>
    public int ChordOctaveOffset
    {
        get
        {
            int offset = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is SyntaxTokenNode t)
                {
                    if (t.Kind == SyntaxKind.Apostrophe) offset++;
                    else if (t.Kind == SyntaxKind.Comma) offset--;
                }
            }
            return offset;
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
    /// Gets the articulations, dynamics, music marks and string numbers attached
    /// to this chord (after the closing <c>&gt;</c> — string numbers there pair
    /// with the members in order, <c>&lt;e dis'&gt;\5\4</c>).
    /// </summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 0; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax or MusicMarkSyntax
                    or StringNumberAnnotationSyntax)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A chord repetition (<c>q</c>): stands for the previous chord's notes with
/// its own duration and post-events. The tree holds no pitches — the shared
/// resolver (<see cref="Music.ChordRepetitions"/>) maps each <c>q</c> to its
/// originating <see cref="ChordSyntax"/> at walk time.
/// LILYPOND-REF: scm/music-functions.scm:923-946 expand-repeat-chords!
/// </summary>
public sealed class ChordRepetitionSyntax : SyntaxNode
{
    internal ChordRepetitionSyntax(ChordRepetitionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>The <c>q</c> identifier token.</summary>
    public SyntaxTokenNode QToken => (SyntaxTokenNode)GetChild(0)!;
    /// <summary>The repetition's duration, or null when unspecified (inherited from the previous note).</summary>
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;
    /// <summary>The tremolo suffix (:8, :16, :32), or null.</summary>
    public SyntaxTokenNode? Tremolo => GetChild(2) as SyntaxTokenNode;

    /// <summary>The articulations and dynamics attached to this repetition itself
    /// (the original chord's post-events are NOT copied — LP copies note events only).</summary>
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
