namespace LilySharp.Core.Syntax;

/// <summary>
/// Types of articulation marks.
/// </summary>
public enum ArticulationType
{
    None,
    Staccato,
    Accent,
    Tenuto,
    Marcato,
    Fermata,
    Portato
}

/// <summary>
/// Dynamic levels from pianississimo to fortississimo.
/// </summary>
public enum DynamicLevel
{
    None,
    PPP = 20,
    PP = 35,
    P = 50,
    MP = 65,
    MF = 80,
    F = 95,
    FF = 110,
    FFF = 127
}

/// <summary>
/// Predefined instrument tunings for tablature.
/// </summary>
public enum TuningType
{
    /// <summary>Standard guitar tuning: E A D G B E</summary>
    Guitar,
    /// <summary>4-string bass tuning: E A D G</summary>
    Bass,
    /// <summary>5-string bass tuning: B E A D G</summary>
    Bass5,
    /// <summary>Ukulele tuning: G C E A</summary>
    Ukulele,
    /// <summary>Custom tuning</summary>
    Custom
}
