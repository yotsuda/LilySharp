namespace LilySharp.Core.Svg;

/// <summary>
/// SMuFL glyph code points for common music notation symbols.
/// Based on SMuFL 1.4 specification.
/// </summary>
public static class SmuflGlyphs
{
    // === Clefs ===
    public const char GClef = '\uE050';
    public const char GClef8vb = '\uE052';
    public const char GClef8va = '\uE053';
    public const char FClef = '\uE062';
    public const char FClef8vb = '\uE064';
    public const char FClef8va = '\uE065';
    public const char CClef = '\uE05C';
    
    // === Note heads ===
    public const char NoteheadWhole = '\uE0A2';
    public const char NoteheadHalf = '\uE0A3';
    public const char NoteheadBlack = '\uE0A4';
    public const char NoteheadDoubleWhole = '\uE0A0';
    
    // === Rests ===
    public const char RestMaxima = '\uE4E0';
    public const char RestLonga = '\uE4E1';
    public const char RestDoubleWhole = '\uE4E2';
    public const char RestWhole = '\uE4E3';
    public const char RestHalf = '\uE4E4';
    public const char RestQuarter = '\uE4E5';
    public const char Rest8th = '\uE4E6';
    public const char Rest16th = '\uE4E7';
    public const char Rest32nd = '\uE4E8';
    public const char Rest64th = '\uE4E9';
    public const char Rest128th = '\uE4EA';
    
    // === Accidentals ===
    public const char AccidentalFlat = '\uE260';
    public const char AccidentalNatural = '\uE261';
    public const char AccidentalSharp = '\uE262';
    public const char AccidentalDoubleSharp = '\uE263';
    public const char AccidentalDoubleFlat = '\uE264';
    
    // === Flags ===
    public const char Flag8thUp = '\uE240';
    public const char Flag8thDown = '\uE241';
    public const char Flag16thUp = '\uE242';
    public const char Flag16thDown = '\uE243';
    public const char Flag32ndUp = '\uE244';
    public const char Flag32ndDown = '\uE245';
    public const char Flag64thUp = '\uE246';
    public const char Flag64thDown = '\uE247';
    public const char Flag128thUp = '\uE248';
    public const char Flag128thDown = '\uE249';
    
    // === Augmentation dot ===
    public const char AugmentationDot = '\uE1E7';
    
    // === Metronome marks (for tempo indication) ===
    public const char MetNoteDoubleWhole = '\uECA0';
    public const char MetNoteWhole = '\uECA2';
    public const char MetNoteHalfUp = '\uECA3';
    public const char MetNoteQuarterUp = '\uECA5';   // ♩
    public const char MetNote8thUp = '\uECA7';       // ♪
    public const char MetNote16thUp = '\uECA9';
    
    // === Time signatures ===
    public const char TimeSig0 = '\uE080';
    public const char TimeSig1 = '\uE081';
    public const char TimeSig2 = '\uE082';
    public const char TimeSig3 = '\uE083';
    public const char TimeSig4 = '\uE084';
    public const char TimeSig5 = '\uE085';
    public const char TimeSig6 = '\uE086';
    public const char TimeSig7 = '\uE087';
    public const char TimeSig8 = '\uE088';
    public const char TimeSig9 = '\uE089';
    public const char TimeSigCommon = '\uE08A';
    public const char TimeSigCutCommon = '\uE08B';
    
    // === Barlines ===
    public const char BarlineSingle = '\uE030';
    public const char BarlineDouble = '\uE031';
    public const char BarlineFinal = '\uE032';
    public const char RepeatLeft = '\uE040';      // |:
    public const char RepeatRight = '\uE041';     // :|
    public const char RepeatRightLeft = '\uE042'; // :|:
    public const char RepeatDots = '\uE043';
    // === Dynamics ===
    public const char DynamicPiano = '\uE520';
    public const char DynamicMezzo = '\uE521';
    public const char DynamicForte = '\uE522';
    public const char DynamicRinforzando = '\uE523';
    public const char DynamicSforzando = '\uE524';
    public const char DynamicZ = '\uE525';
    
    // === Articulations ===
    public const char ArticAccentAbove = '\uE4A0';
    public const char ArticAccentBelow = '\uE4A1';
    public const char ArticStaccatoAbove = '\uE4A2';
    public const char ArticStaccatoBelow = '\uE4A3';
    public const char ArticTenutoAbove = '\uE4A4';
    public const char ArticTenutoBelow = '\uE4A5';
    public const char ArticMarcatoAbove = '\uE4AC';
    public const char ArticMarcatoBelow = '\uE4AD';
    
    /// <summary>
    /// Gets the time signature digit glyph.
    /// </summary>
    public static char GetTimeSigDigit(int digit) => digit switch
    {
        0 => TimeSig0,
        1 => TimeSig1,
        2 => TimeSig2,
        3 => TimeSig3,
        4 => TimeSig4,
        5 => TimeSig5,
        6 => TimeSig6,
        7 => TimeSig7,
        8 => TimeSig8,
        9 => TimeSig9,
        _ => TimeSig0
    };
    
    /// <summary>
    /// Gets the rest glyph for a given note value.
    /// </summary>
    public static char GetRest(int noteValue) => noteValue switch
    {
        1 => RestWhole,
        2 => RestHalf,
        4 => RestQuarter,
        8 => Rest8th,
        16 => Rest16th,
        32 => Rest32nd,
        64 => Rest64th,
        128 => Rest128th,
        _ => RestQuarter
    };
    
    /// <summary>
    /// Gets the notehead glyph for a given note value.
    /// </summary>
    public static char GetNotehead(int noteValue) => noteValue switch
    {
        1 => NoteheadWhole,
        2 => NoteheadHalf,
        _ => NoteheadBlack  // Quarter and shorter
    };
    
    /// <summary>
    /// Gets the flag glyph for a given note value and stem direction.
    /// </summary>
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