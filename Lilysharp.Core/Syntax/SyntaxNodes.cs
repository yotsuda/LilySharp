using Lilysharp.Core.Semantics;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Syntax;

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

    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;

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
/// Relative expression: relative c' { ... }
/// </summary>
public sealed class RelativeExpressionSyntax : SyntaxNode
{
    internal RelativeExpressionSyntax(RelativeExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode RelativeKeyword => (SyntaxTokenNode)GetChild(0)!;
    public PitchSyntax BasePitch => (PitchSyntax)GetChild(1)!;
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(2)!;
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
    /// Gets the accidental as semitone offset (-2 to +2).
    /// </summary>
    public int AccidentalOffset => Accidental switch
    {
        "isis" => 2,
        "is" => 1,
        "" => 0,
        "es" => -1,
        "eses" => -2,
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
/// A note: pitch + optional duration + articulations
/// </summary>
public sealed class NoteSyntax : SyntaxNode
{
    internal NoteSyntax(NoteGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public PitchSyntax Pitch => (PitchSyntax)GetChild(0)!;
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;
    
    /// <summary>
    /// Gets the articulations and dynamics attached to this note.
    /// </summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 2; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
}

/// <summary>
/// A rest: r, s, R + optional duration
/// </summary>
public sealed class RestSyntax : SyntaxNode
{
    internal RestSyntax(RestGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode RestToken => (SyntaxTokenNode)GetChild(0)!;
    public DurationSyntax? Duration => GetChild(1) as DurationSyntax;
}

/// <summary>
/// A chord: < pitches > + optional duration
/// </summary>
public sealed class ChordSyntax : SyntaxNode
{
    internal ChordSyntax(ChordGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

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
    /// Gets the articulations attached to this chord.
    /// </summary>
    public IEnumerable<SyntaxNode> Articulations
    {
        get
        {
            for (int i = 0; i < Green.SlotCount; i++)
            {
                var child = GetChild(i);
                if (child is ArticulationSyntax or DynamicSyntax)
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

    public SyntaxTokenNode BarToken => (SyntaxTokenNode)GetChild(0)!;
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

    public bool IsOpen => ((SyntaxTokenNode)GetChild(0)!).Kind == SyntaxKind.OpenParen;
}

/// <summary>
/// Score declaration: score "title" { ... }
/// </summary>
public sealed class ScoreDeclarationSyntax : SyntaxNode
{
    internal ScoreDeclarationSyntax(ScoreDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode ScoreKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode? Title => GetChild(1) as SyntaxTokenNode;

    public IEnumerable<PartDeclarationSyntax> Parts
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (GetChild(i) is PartDeclarationSyntax part)
                    yield return part;
            }
        }
    }
}

/// <summary>
/// Part declaration: part Name "display" { ... }
/// </summary>
public sealed class PartDeclarationSyntax : SyntaxNode
{
    internal PartDeclarationSyntax(PartDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode PartKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode? Name => GetChild(1) as SyntaxTokenNode;
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

    public SyntaxTokenNode StaffKeyword => (SyntaxTokenNode)GetChild(0)!;
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

    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode Colon => (SyntaxTokenNode)GetChild(1)!;
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

    public SyntaxTokenNode TimeKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode Numerator => (SyntaxTokenNode)GetChild(1)!;
    public SyntaxTokenNode Slash => (SyntaxTokenNode)GetChild(2)!;
    public SyntaxTokenNode Denominator => (SyntaxTokenNode)GetChild(3)!;
    
    /// <summary>
    /// Gets the numerator value (e.g., 4 for 4/4).
    /// </summary>
    public int Beats => int.TryParse(Numerator.Text, out var n) ? n : 4;
    
    /// <summary>
    /// Gets the denominator value (e.g., 4 for 4/4).
    /// </summary>
    public int BeatType => int.TryParse(Denominator.Text, out var n) ? n : 4;
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

    public SyntaxTokenNode TempoKeyword => (SyntaxTokenNode)GetChild(0)!;
    
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
    /// Gets the tempo marking string (e.g., "Allegro"), if present.
    /// </summary>
    public string? Marking
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
    /// Gets the BPM value, if present.
    /// </summary>
    public int? Bpm
    {
        get
        {
            // Look for the last integer (BPM is usually at the end)
            int? lastInt = null;
            foreach (var value in Values)
            {
                if (value is SyntaxTokenNode token && 
                    (token.Kind == SyntaxKind.IntegerLiteral || token.Kind == SyntaxKind.DurationNumber))
                {
                    if (int.TryParse(token.Text, out var n))
                        lastInt = n;
                }
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

    public SyntaxTokenNode LetKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;
    public SyntaxTokenNode EqualsToken => (SyntaxTokenNode)GetChild(2)!;
    public SyntaxNode Expression => GetChild(3)!;
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

    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;
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

    public SyntaxTokenNode RepeatKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode RepeatType => (SyntaxTokenNode)GetChild(1)!;
    public SyntaxTokenNode Count => (SyntaxTokenNode)GetChild(2)!;
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(3)!;
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

    public SyntaxTokenNode AlternativeKeyword => (SyntaxTokenNode)GetChild(0)!;
    
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
/// Parallel expression: << expr \\ expr >>
/// </summary>
public sealed class ParallelExpressionSyntax : SyntaxNode
{
    internal ParallelExpressionSyntax(InternalSyntax.ParallelExpressionGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode OpenAngle => (SyntaxTokenNode)GetChild(0)!;
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
                if (child is MusicBlockSyntax or RelativeExpressionSyntax)
                    yield return child;
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

    public SyntaxTokenNode KeyKeyword => (SyntaxTokenNode)GetChild(0)!;
    public PitchSyntax Pitch => (PitchSyntax)GetChild(1)!;
    public SyntaxTokenNode Mode => (SyntaxTokenNode)GetChild(2)!;
    
    public bool IsMajor => Mode.Kind == SyntaxKind.MajorKeyword;
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

    public SyntaxTokenNode ClefKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode ClefName => (SyntaxTokenNode)GetChild(1)!;
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

    public SyntaxTokenNode TupletKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode Numerator => (SyntaxTokenNode)GetChild(1)!;
    public SyntaxTokenNode Slash => (SyntaxTokenNode)GetChild(2)!;
    public SyntaxTokenNode Denominator => (SyntaxTokenNode)GetChild(3)!;
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

    public SyntaxTokenNode GraceKeyword => (SyntaxTokenNode)GetChild(0)!;
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

    public SyntaxTokenNode LyricsKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode OpenBrace => (SyntaxTokenNode)GetChild(1)!;
    public IEnumerable<SyntaxNode> Syllables
    {
        get
        {
            for (int i = 2; i < SlotCount - 1; i++)
            {
                var child = GetChild(i);
                if (child != null)
                    yield return child;
            }
        }
    }
    public SyntaxTokenNode CloseBrace => (SyntaxTokenNode)GetChild(SlotCount - 1)!;
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

    public SyntaxTokenNode AtToken => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode NameToken => (SyntaxTokenNode)GetChild(1)!;
    
    /// <summary>
    /// Gets the articulation type.
    /// </summary>
    public ArticulationType Type => NameToken.Kind switch
    {
        SyntaxKind.StaccatoKeyword => ArticulationType.Staccato,
        SyntaxKind.AccentKeyword => ArticulationType.Accent,
        SyntaxKind.TenutoKeyword => ArticulationType.Tenuto,
        SyntaxKind.MarcatoKeyword => ArticulationType.Marcato,
        SyntaxKind.FermataKeyword => ArticulationType.Fermata,
        SyntaxKind.PortatoKeyword => ArticulationType.Portato,
        _ => ArticulationType.None
    };
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

    public SyntaxTokenNode BackslashToken => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode DynamicToken => (SyntaxTokenNode)GetChild(1)!;
    
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
            
            // Fallback to text matching (for pitch tokens used as dynamics like \f)
            return DynamicToken.Text switch
            {
                "ppp" => DynamicLevel.PPP,
                "pp" => DynamicLevel.PP,
                "p" => DynamicLevel.P,
                "mp" => DynamicLevel.MP,
                "mf" => DynamicLevel.MF,
                "f" => DynamicLevel.F,
                "ff" => DynamicLevel.FF,
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
/// Represents a tablature staff declaration: \tabStaff { ... }
/// </summary>
public sealed partial class TabStaffDeclarationSyntax : SyntaxNode
{
    internal TabStaffDeclarationSyntax(TabStaffDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode TabStaffKeyword => (SyntaxTokenNode)GetChild(0)!;
    public TuningDeclarationSyntax? Tuning => SlotCount > 2 ? GetChild(1) as TuningDeclarationSyntax : null;
    public MusicBlockSyntax Body => (MusicBlockSyntax)GetChild(SlotCount - 1)!;
}

/// <summary>
/// Represents a tuning declaration: \tuning guitar | \tuning bass | \tuning custom { pitches }
/// </summary>
public sealed partial class TuningDeclarationSyntax : SyntaxNode
{
    internal TuningDeclarationSyntax(TuningDeclarationGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode BackslashToken => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode TuningKeyword => (SyntaxTokenNode)GetChild(1)!;
    public SyntaxTokenNode TuningName => (SyntaxTokenNode)GetChild(2)!;
    
    /// <summary>
    /// Gets the tuning type.
    /// </summary>
    public TuningType Type => TuningName.Text.ToLowerInvariant() switch
    {
        "guitar" => TuningType.Guitar,
        "bass" => TuningType.Bass,
        "bass5" => TuningType.Bass5,
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

    public SyntaxTokenNode StringNumberToken => (SyntaxTokenNode)GetChild(0)!;
    
    /// <summary>
    /// Gets the string number (1-based).
    /// </summary>
    public int StringNumber => int.Parse(StringNumberToken.Text);
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

    public SyntaxTokenNode SectionKeyword => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode Name => (SyntaxTokenNode)GetChild(1)!;
    
    /// <summary>
    /// Gets the section name as a string.
    /// </summary>
    public string SectionName => Name.Text;
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

    public SyntaxTokenNode Identifier => (SyntaxTokenNode)GetChild(0)!;
    
    /// <summary>
    /// Gets the referenced section name.
    /// </summary>
    public string SectionName => Identifier.Text;
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
/// Represents an alternative in structure: 1. SectionName
/// </summary>
public sealed partial class StructureAlternativeSyntax : SyntaxNode
{
    internal StructureAlternativeSyntax(StructureAlternativeGreen green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenNode Number => (SyntaxTokenNode)GetChild(0)!;
    public SyntaxTokenNode SectionName => (SyntaxTokenNode)GetChild(2)!;
    
    /// <summary>
    /// Gets the alternative number.
    /// </summary>
    public int AlternativeNumber => int.Parse(Number.Text);
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
    Segno,
    Fine,
    Coda,
    ToCoda,
    DaCapo,
    DaCapoAlFine,
    DaCapoAlCoda,
    DalSegno,
    DalSegnoAlFine,
    DalSegnoAlCoda
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

    public SyntaxTokenNode RenderKeyword => (SyntaxTokenNode)GetChild(0)!;
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

    public SyntaxTokenNode StaffKeyword => (SyntaxTokenNode)GetChild(0)!;
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

    public SyntaxTokenNode PartName => (SyntaxTokenNode)GetChild(0)!;
}