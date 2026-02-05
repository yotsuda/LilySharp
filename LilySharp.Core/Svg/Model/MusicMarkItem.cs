using System.Collections.Immutable;

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// Type of music mark symbol.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:90-140 Mark types
/// LILYPOND-REF: define-grobs.scm:3650-3710 Segno, Coda mark definitions
/// </remarks>
public enum MusicMarkType
{
    /// <summary>Segno sign (𝄋)</summary>
    Segno,
    /// <summary>Coda sign (𝄌)</summary>
    Coda,
    /// <summary>Fine text</summary>
    Fine,
    /// <summary>D.S. (Dal Segno)</summary>
    DalSegno,
    /// <summary>D.C. (Da Capo)</summary>
    DaCapo,
    /// <summary>D.S. al Fine</summary>
    DalSegnoAlFine,
    /// <summary>D.S. al Coda</summary>
    DalSegnoAlCoda,
    /// <summary>D.C. al Fine</summary>
    DaCapoAlFine,
    /// <summary>D.C. al Coda</summary>
    DaCapoAlCoda,
    /// <summary>To Coda</summary>
    ToCoda,
    /// <summary>rit. (ritardando)</summary>
    Rit,
    /// <summary>accel. (accelerando)</summary>
    Accel,
    /// <summary>cresc. (crescendo)</summary>
    Cresc,
    /// <summary>decresc. (decrescendo)</summary>
    Decresc,
    /// <summary>dim. (diminuendo)</summary>
    Dim,
}

/// <summary>
/// Horizontal position of a music mark.
/// </summary>
public enum MusicMarkPosition
{
    /// <summary>At the beginning of the measure/section</summary>
    Beginning,
    /// <summary>At the end of the measure/section</summary>
    End,
}

/// <summary>
/// Vertical position of a music mark.
/// </summary>
public enum MusicMarkVertical
{
    /// <summary>Above the staff</summary>
    Above,
    /// <summary>Below the staff</summary>
    Below,
}

/// <summary>
/// Represents a music mark (segno, coda, fine, D.S., D.C., etc.) in the score.
/// </summary>
/// <remarks>
/// LILYPOND-REF: mark-engraver.cc:36-89 Mark_engraver class
/// LILYPOND-REF: define-grobs.scm:3650-3710 Mark grob definitions
///
/// Music marks are structural annotations that indicate navigation or expression:
/// - Segno/Coda: Navigation symbols for repeats
/// - Fine/D.S./D.C.: End and jump instructions
/// - rit./accel./cresc./dim.: Expression marks
/// </remarks>
public sealed record MusicMarkItem
{
    /// <summary>The type of music mark.</summary>
    public MusicMarkType Type { get; }

    /// <summary>The text representation of this mark.</summary>
    public string Text { get; }

    /// <summary>Horizontal position (beginning or end of measure).</summary>
    public MusicMarkPosition Position { get; }

    /// <summary>Vertical position (above or below staff).</summary>
    public MusicMarkVertical Vertical { get; }

    /// <summary>Whether this mark uses a symbol glyph (segno, coda) vs text.</summary>
    public bool IsSymbol { get; }

    /// <summary>The measure index where this mark appears.</summary>
    public int MeasureIndex { get; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    public MusicMarkItem(MusicMarkType type, int measureIndex, int sourcePosition)
    {
        Type = type;
        Text = GetMarkText(type);
        Position = GetMarkPosition(type);
        Vertical = GetMarkVertical(type);
        IsSymbol = type == MusicMarkType.Segno || type == MusicMarkType.Coda;
        MeasureIndex = measureIndex;
        SourcePosition = sourcePosition;
    }

    /// <summary>
    /// Parses a mark name string into a MusicMarkType.
    /// </summary>
    public static MusicMarkType? ParseMarkName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "segno" => MusicMarkType.Segno,
            "coda" => MusicMarkType.Coda,
            "fine" => MusicMarkType.Fine,
            "ds" => MusicMarkType.DalSegno,
            "dc" => MusicMarkType.DaCapo,
            "ds.al.fine" => MusicMarkType.DalSegnoAlFine,
            "ds.al.coda" => MusicMarkType.DalSegnoAlCoda,
            "dc.al.fine" => MusicMarkType.DaCapoAlFine,
            "dc.al.coda" => MusicMarkType.DaCapoAlCoda,
            "to.coda" or "tocoda" => MusicMarkType.ToCoda,
            "rit" => MusicMarkType.Rit,
            "accel" => MusicMarkType.Accel,
            "cresc" => MusicMarkType.Cresc,
            "decresc" => MusicMarkType.Decresc,
            "dim" => MusicMarkType.Dim,
            _ => null
        };
    }

    /// <summary>
    /// Parses a multi-part mark name (e.g., ["ds", "al", "fine"]) into a MusicMarkType.
    /// </summary>
    public static MusicMarkType? ParseMarkParts(ImmutableArray<string> parts)
    {
        if (parts.Length == 0) return null;
        
        var combined = string.Join(".", parts.Select(p => p.ToLowerInvariant()));
        return ParseMarkName(combined);
    }

    private static string GetMarkText(MusicMarkType type) => type switch
    {
        MusicMarkType.Segno => "𝄋",        // SMuFL will use glyph
        MusicMarkType.Coda => "𝄌",         // SMuFL will use glyph
        MusicMarkType.Fine => "Fine",
        MusicMarkType.DalSegno => "D.S.",
        MusicMarkType.DaCapo => "D.C.",
        MusicMarkType.DalSegnoAlFine => "D.S. al Fine",
        MusicMarkType.DalSegnoAlCoda => "D.S. al Coda",
        MusicMarkType.DaCapoAlFine => "D.C. al Fine",
        MusicMarkType.DaCapoAlCoda => "D.C. al Coda",
        MusicMarkType.ToCoda => "To Coda",
        MusicMarkType.Rit => "rit.",
        MusicMarkType.Accel => "accel.",
        MusicMarkType.Cresc => "cresc.",
        MusicMarkType.Decresc => "decresc.",
        MusicMarkType.Dim => "dim.",
        _ => type.ToString()
    };

    private static MusicMarkPosition GetMarkPosition(MusicMarkType type) => type switch
    {
        MusicMarkType.Segno => MusicMarkPosition.Beginning,
        MusicMarkType.Coda => MusicMarkPosition.Beginning,
        _ => MusicMarkPosition.End
    };

    private static MusicMarkVertical GetMarkVertical(MusicMarkType type) => type switch
    {
        MusicMarkType.Rit => MusicMarkVertical.Below,
        MusicMarkType.Accel => MusicMarkVertical.Below,
        MusicMarkType.Cresc => MusicMarkVertical.Below,
        MusicMarkType.Decresc => MusicMarkVertical.Below,
        MusicMarkType.Dim => MusicMarkVertical.Below,
        _ => MusicMarkVertical.Above
    };
}