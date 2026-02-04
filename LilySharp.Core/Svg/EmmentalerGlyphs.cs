namespace LilySharp.Core.Svg;

/// <summary>
/// Emmentaler font glyph code points for music notation symbols.
/// Based on LilyPond's Emmentaler font (SIL OFL licensed).
/// </summary>
/// <remarks>
/// Barlines are NOT glyphs in Emmentaler - they are drawn as shapes.
/// </remarks>
public static class EmmentalerGlyphs
{
    // === Clefs ===
    public const char GClef = '\uE085';           // clefs.G
    public const char GClef8vb = '\uE086';        // clefs.G_change
    public const char GClef8va = '\uE085';        // same as GClef
    public const char FClef = '\uE083';           // clefs.F
    public const char FClef8vb = '\uE084';        // clefs.F_change
    public const char FClef8va = '\uE083';        // same as FClef
    public const char CClef = '\uE07F';           // clefs.C

    // === Note heads ===
    public const char NoteheadWhole = '\uE0E8';       // noteheads.s0
    public const char NoteheadHalf = '\uE0E9';        // noteheads.s1
    public const char NoteheadBlack = '\uE0EA';       // noteheads.s2
    public const char NoteheadDoubleWhole = '\uE0E6'; // noteheads.sM1

    // === Rests ===
    public const char RestMaxima = '\uE004';      // rests.M3
    public const char RestLonga = '\uE005';       // rests.M2
    public const char RestDoubleWhole = '\uE006'; // rests.M1
    public const char RestWhole = '\uE000';       // rests.0
    public const char RestHalf = '\uE001';        // rests.1
    public const char RestQuarter = '\uE008';     // rests.2
    public const char Rest8th = '\uE00B';         // rests.3
    public const char Rest16th = '\uE00C';        // rests.4
    public const char Rest32nd = '\uE00D';        // rests.5
    public const char Rest64th = '\uE00E';        // rests.6
    public const char Rest128th = '\uE00F';       // rests.7

    // === Accidentals ===
    public const char AccidentalFlat = '\uE021';         // accidentals.flat
    public const char AccidentalNatural = '\uE01D';      // accidentals.natural
    public const char AccidentalSharp = '\uE013';        // accidentals.sharp
    public const char AccidentalDoubleSharp = '\uE01C';  // accidentals.doublesharp
    public const char AccidentalDoubleFlat = '\uE02A';   // accidentals.flatflat

    // === Flags ===
    public const char Flag8thUp = '\uE0D2';       // flags.u3
    public const char Flag8thDown = '\uE0DA';     // flags.d3
    public const char Flag16thUp = '\uE0D3';      // flags.u4
    public const char Flag16thDown = '\uE0DB';    // flags.d4
    public const char Flag32ndUp = '\uE0D4';      // flags.u5
    public const char Flag32ndDown = '\uE0DC';    // flags.d5
    public const char Flag64thUp = '\uE0D5';      // flags.u6
    public const char Flag64thDown = '\uE0DD';    // flags.d6
    public const char Flag128thUp = '\uE0D6';     // flags.u7
    public const char Flag128thDown = '\uE0DE';   // flags.d7

    // === Augmentation dot ===
    public const char AugmentationDot = '\uE038'; // dots.dot

    // === Time signatures (fattened digits) ===
    public const char TimeSig0 = '\uE0B4';        // fattened.zero
    public const char TimeSig1 = '\uE0B5';        // fattened.one
    public const char TimeSig2 = '\uE0B6';        // fattened.two
    public const char TimeSig3 = '\uE0B7';        // fattened.three
    public const char TimeSig4 = '\uE0B8';        // fattened.four
    public const char TimeSig5 = '\uE0BA';        // fattened.five
    public const char TimeSig6 = '\uE0BB';        // fattened.six
    public const char TimeSig7 = '\uE0BC';        // fattened.seven
    public const char TimeSig8 = '\uE0BE';        // fattened.eight
    public const char TimeSig9 = '\uE0BF';        // fattened.nine
    public const char TimeSigCommon = '\uE091';   // timesig.C44
    public const char TimeSigCutCommon = '\uE092'; // timesig.C22

    // === Dynamics (text-based in Emmentaler) ===
    public const char DynamicPiano = 'p';
    public const char DynamicMezzo = 'm';
    public const char DynamicForte = 'f';
    public const char DynamicRinforzando = 'r';
    public const char DynamicSforzando = 's';
    public const char DynamicZ = 'z';

    // === Articulations ===
    public const char ArticAccentAbove = '\uE048';
    public const char ArticAccentBelow = '\uE048';
    public const char ArticStaccatoAbove = '\uE04A';
    public const char ArticStaccatoBelow = '\uE04A';
    public const char ArticTenutoAbove = '\uE04D';
    public const char ArticTenutoBelow = '\uE04D';
    public const char ArticMarcatoAbove = '\uE050';
    public const char ArticMarcatoBelow = '\uE051';

    // === Metronome (use regular noteheads) ===
    public const char MetNoteDoubleWhole = '\uE0E6';
    public const char MetNoteWhole = '\uE0E8';
    public const char MetNoteHalfUp = '\uE0E9';
    public const char MetNoteQuarterUp = '\uE0EA';
    public const char MetNote8thUp = '\uE0EA';
    public const char MetNote16thUp = '\uE0EA';

    // === Repeat dots ===
    public const char RepeatDots = '\uE038';

    /// <summary>Gets the time signature digit glyph.</summary>
    public static char GetTimeSigDigit(int digit) => digit switch
    {
        0 => TimeSig0, 1 => TimeSig1, 2 => TimeSig2, 3 => TimeSig3, 4 => TimeSig4,
        5 => TimeSig5, 6 => TimeSig6, 7 => TimeSig7, 8 => TimeSig8, 9 => TimeSig9,
        _ => TimeSig0
    };

    /// <summary>Gets the rest glyph for a given note value.</summary>
    public static char GetRest(int noteValue) => noteValue switch
    {
        1 => RestWhole, 2 => RestHalf, 4 => RestQuarter, 8 => Rest8th,
        16 => Rest16th, 32 => Rest32nd, 64 => Rest64th, 128 => Rest128th,
        _ => RestQuarter
    };

    /// <summary>Gets the notehead glyph for a given note value.</summary>
    public static char GetNotehead(int noteValue) => noteValue switch
    {
        1 => NoteheadWhole, 2 => NoteheadHalf, _ => NoteheadBlack
    };

    /// <summary>Gets the flag glyph for a given note value and stem direction.</summary>
    public static char? GetFlag(int noteValue, bool stemUp) => noteValue switch
    {
        8 => stemUp ? Flag8thUp : Flag8thDown,
        16 => stemUp ? Flag16thUp : Flag16thDown,
        32 => stemUp ? Flag32ndUp : Flag32ndDown,
        64 => stemUp ? Flag64thUp : Flag64thDown,
        128 => stemUp ? Flag128thUp : Flag128thDown,
        _ => null
    };
}