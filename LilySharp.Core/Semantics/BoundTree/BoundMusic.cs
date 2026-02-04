using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics.BoundTree;

/// <summary>
/// Base class for all bound music nodes.
/// Bound nodes represent music after semantic analysis (name resolution, structure expansion).
/// </summary>
/// <remarks>
/// Design inspired by Roslyn's BoundNode.
/// All bound nodes are immutable records that reference their originating syntax.
/// </remarks>
public abstract record BoundMusic
{
    /// <summary>
    /// The syntax node from which this bound node was created.
    /// </summary>
    public abstract SyntaxNode? Syntax { get; }

    /// <summary>
    /// The source position for error reporting and click-to-source mapping.
    /// </summary>
    public int SourcePosition => Syntax?.Position ?? 0;
}

/// <summary>
/// Represents an absolute pitch (after relative pitch resolution).
/// </summary>
/// <param name="Step">The pitch step: C=0, D=1, E=2, F=3, G=4, A=5, B=6</param>
/// <param name="Octave">The octave number (4 = middle C octave)</param>
/// <param name="Alteration">Semitone alteration: -2=double flat, -1=flat, 0=natural, 1=sharp, 2=double sharp</param>
public readonly record struct Pitch(int Step, int Octave, int Alteration)
{
    /// <summary>Middle C.</summary>
    public static Pitch MiddleC => new(0, 4, 0);

    /// <summary>
    /// Gets the MIDI note number (Middle C = 60).
    /// </summary>
    public int MidiNote => (Octave + 1) * 12 + StepToSemitone(Step) + Alteration;

    /// <summary>
    /// Gets the staff position relative to middle C (0 = middle C line).
    /// Each step is one staff position.
    /// </summary>
    public int StaffPosition => (Octave - 4) * 7 + Step;

    /// <summary>
    /// Gets the accidental string for rendering, or null if natural.
    /// </summary>
    public string? AccidentalString => Alteration switch
    {
        -2 => "bb",
        -1 => "b",
        0 => null,
        1 => "#",
        2 => "##",
        _ => null
    };

    /// <summary>
    /// Creates a pitch from a pitch name character and octave.
    /// </summary>
    public static Pitch FromName(char name, int octave, int alteration = 0)
    {
        var step = char.ToLower(name) switch
        {
            'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3,
            'g' => 4, 'a' => 5, 'b' => 6,
            _ => 0
        };
        return new Pitch(step, octave, alteration);
    }

    /// <summary>
    /// Gets the pitch name character.
    /// </summary>
    public char Name => (char)('c' + (Step < 5 ? Step : Step - 7));

    private static int StepToSemitone(int step) => step switch
    {
        0 => 0,  // C
        1 => 2,  // D
        2 => 4,  // E
        3 => 5,  // F
        4 => 7,  // G
        5 => 9,  // A
        6 => 11, // B
        _ => 0
    };

    public override string ToString()
    {
        var alt = Alteration switch
        {
            -2 => "bb", -1 => "b", 1 => "#", 2 => "##", _ => ""
        };
        var octMark = Octave > 4 ? new string('\'', Octave - 4)
                    : Octave < 4 ? new string(',', 4 - Octave)
                    : "";
        return $"{Name}{alt}{octMark}";
    }
}
