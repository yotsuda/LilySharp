namespace Lilysharp.Core.Syntax;

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
