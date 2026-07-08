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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;

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
    /// <summary>8va (ottava alta - up one octave)</summary>
    OttavaUp,
    /// <summary>8vb (ottava bassa - down one octave)</summary>
    OttavaDown,
    /// <summary>15ma (quindicesima alta - up two octaves)</summary>
    QuindicesUp,
    /// <summary>15mb (quindicesima bassa - down two octaves)</summary>
    QuindicesDown,
    /// <summary>loco (return to normal pitch)</summary>
    Loco,
    /// <summary>Rehearsal mark (boxed letter/number above staff)</summary>
    Rehearsal,
    /// <summary>Section label (boxed section name above staff)</summary>
    SectionLabel,
    /// <summary>Tempo marking (♩= NNN)</summary>
    Tempo,
    /// <summary>Sustain pedal on (Ped.)</summary>
    SustainOn,
    /// <summary>Sustain pedal off (*)</summary>
    SustainOff,
    /// <summary>Sostenuto pedal on (Sost. Ped.)</summary>
    SostenutoOn,
    /// <summary>Sostenuto pedal off (*)</summary>
    SostenutoOff,
    /// <summary>Una corda pedal on (una corda)</summary>
    UnaCordaOn,
    /// <summary>Una corda pedal off (tre corde)</summary>
    UnaCordaOff,
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

    /// <summary>For a <see cref="MusicMarkType.Tempo"/> mark, the note value to swing
    /// (0 = none, 8 = eighths, 16 = sixteenths) — drives the feel equation beside it.</summary>
    public int SwingSubdivision { get; init; }

    /// <summary>For a Tempo mark: the textual marking ("Grave") printed bold
    /// before the parenthesized metronome equation; null for a bare ♩ = N.</summary>
    public string? TempoText { get; init; }

    /// <summary>For a Tempo mark: the metronome beat unit (4 = quarter,
    /// 2 = half, 8 = eighth).</summary>
    public int TempoBeatUnit { get; init; } = 4;

    /// <summary>For a Tempo mark: augmentation dots on the beat unit
    /// (<c>tempo "Lively" 4. = 116</c> → 1).</summary>
    public int TempoDots { get; init; }

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

    /// <summary>
    /// The staff this mark was authored on (0 = the first/only staff). Spanner
    /// detection (hairpins, ottava, text spanners) pairs a start mark with its end
    /// within the SAME staff, and the spanner is stacked below/above that staff —
    /// so a cresc on staff 2 no longer terminates against a cresc on staff 1, and
    /// the wedge hangs under its own staff.
    /// </summary>
    public int StaffIndex { get; init; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; }

    /// <summary>
    /// Index of the measure item (note/rest) this mark anchors on, or -1 when
    /// the mark anchors on the measure start (break-align).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: metronome-engraver.cc — a \tempo mid-measure attaches its
    /// MetronomeMark to the musical column at that moment (the following note),
    /// not to the measure's break-align prefix. Index 0 (the first note of the
    /// measure) is treated as a measure-start tempo and still break-aligns.
    /// On a single staff the index resolves the note directly; on a grand staff
    /// (independent rhythms) it only flags "mid-measure" and <see cref="AnchorTiming"/>
    /// resolves the X via the shared timing columns.
    /// </remarks>
    public int AnchorItemIndex { get; }

    /// <summary>
    /// Musical time elapsed from the measure start at this mark's moment. Used to
    /// resolve the X on a multi-staff measure whose timing columns are shared
    /// across staves, so the index of the authoring voice cannot be trusted.
    /// </summary>
    public Fraction AnchorTiming { get; }

    /// <summary>Creates a music mark of the given type with standard text.</summary>
    public MusicMarkItem(MusicMarkType type, int measureIndex, int sourcePosition,
        int anchorItemIndex = -1, Fraction anchorTiming = default)
    {
        Type = type;
        Text = GetMarkText(type);
        Position = GetMarkPosition(type);
        Vertical = GetMarkVertical(type);
        IsSymbol = type == MusicMarkType.Segno || type == MusicMarkType.Coda;
        MeasureIndex = measureIndex;
        SourcePosition = sourcePosition;
        AnchorItemIndex = anchorItemIndex;
        AnchorTiming = anchorTiming;
    }

    /// <summary>
    /// Creates a music mark with custom text (for rehearsal marks).
    /// </summary>
    public MusicMarkItem(MusicMarkType type, string text, int measureIndex, int sourcePosition,
        int anchorItemIndex = -1, Fraction anchorTiming = default)
    {
        Type = type;
        Text = text;
        Position = GetMarkPosition(type);
        Vertical = GetMarkVertical(type);
        IsSymbol = false;
        MeasureIndex = measureIndex;
        SourcePosition = sourcePosition;
        AnchorItemIndex = anchorItemIndex;
        AnchorTiming = anchorTiming;
    }

    /// <summary>
    /// Parses a mark name string into a MusicMarkType.
    /// Returns null if the name is not recognized.
    /// For rehearsal marks (@mark.A, @mark.1), returns Rehearsal.
    /// Use ParseRehearsalText to get the display text for rehearsal marks.
    /// </summary>
    /// <summary>
    /// Marks whose visuals are produced by a SPANNER engraver (text
    /// spanners, ottava brackets): their MusicMarkLayout entries are not
    /// drawn by DrawMusicMarks and must not occupy outside-staff space.
    /// </summary>
    public static bool IsSpannerHandled(MusicMarkType type) =>
        type is MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim
             or MusicMarkType.Rit or MusicMarkType.Accel
             or MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
             or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown
             or MusicMarkType.Loco;

    /// <summary>Parses a mark name (e.g. <c>segno</c>, <c>ds.al.fine</c>, <c>mark.A</c>)
    /// into a <see cref="MusicMarkType"/>, or null if unrecognized.</summary>
    public static MusicMarkType? ParseMarkName(string name)
    {
        // Check for rehearsal marks: @mark.A, @mark.B, @mark.1, etc.
        var lower = name.ToLowerInvariant();
        if (lower.StartsWith("mark.") && name.Length > 5)
            return MusicMarkType.Rehearsal;

        return lower switch
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
            "ottava" or "8va" => MusicMarkType.OttavaUp,
            "ottava.bassa" or "8vb" => MusicMarkType.OttavaDown,
            "quindicesima" or "15ma" => MusicMarkType.QuindicesUp,
            "quindicesima.bassa" or "15mb" => MusicMarkType.QuindicesDown,
            "loco" => MusicMarkType.Loco,
            // ONE spelling per pedal (pre-release grammar audit B-5):
            // engage = name, release = name.off. Una corda keeps its
            // traditional pair (tre corde IS the release, not an "off").
            "ped" => MusicMarkType.SustainOn,
            "ped.off" => MusicMarkType.SustainOff,
            "sost" => MusicMarkType.SostenutoOn,
            "sost.off" => MusicMarkType.SostenutoOff,
            "una.corda" => MusicMarkType.UnaCordaOn,
            "tre.corde" => MusicMarkType.UnaCordaOff,
            _ => null
        };
    }

    /// <summary>
    /// Extracts the rehearsal mark display text from a mark name.
    /// For example: "mark.A" returns "A", "mark.1" returns "1".
    /// </summary>
    public static string ParseRehearsalText(string name)
    {
        if (name.Length > 5 && name.StartsWith("mark.", StringComparison.OrdinalIgnoreCase))
        {
            var label = name.Substring(5);
            // @mark("A") — strip the surrounding quotes so the box shows A, not "A".
            if (label.Length >= 2 && label[0] == '"' && label[^1] == '"')
                label = label.Substring(1, label.Length - 2);
            // Render the label exactly as written — @mark("Solo") is "Solo", not
            // "SOLO" (a free-text label, like @text and LilyPond's \mark "…").
            return label;
        }
        return name;
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
        MusicMarkType.OttavaUp => "8va",
        MusicMarkType.OttavaDown => "8vb",
        MusicMarkType.QuindicesUp => "15ma",
        MusicMarkType.QuindicesDown => "15mb",
        MusicMarkType.Loco => "loco",
        MusicMarkType.Rehearsal => "?",  // Rehearsal text set via constructor overload
        MusicMarkType.SectionLabel => "?",  // Section label text set via constructor overload
        MusicMarkType.Tempo => "?",  // Tempo text set via constructor overload
        MusicMarkType.SustainOn => "Ped.",
        MusicMarkType.SustainOff => "*",
        MusicMarkType.SostenutoOn => "Sost. Ped.",
        MusicMarkType.SostenutoOff => "*",
        MusicMarkType.UnaCordaOn => "una corda",
        MusicMarkType.UnaCordaOff => "tre corde",
        _ => type.ToString()
    };

    private static MusicMarkPosition GetMarkPosition(MusicMarkType type) => type switch
    {
        MusicMarkType.Segno => MusicMarkPosition.Beginning,
        MusicMarkType.Coda => MusicMarkPosition.Beginning,
        MusicMarkType.Rehearsal => MusicMarkPosition.Beginning,
        MusicMarkType.SectionLabel => MusicMarkPosition.Beginning,
        MusicMarkType.Tempo => MusicMarkPosition.Beginning,
        // LILYPOND-REF: piano-pedal-engraver.cc - pedal marks at note position
        MusicMarkType.SustainOn or MusicMarkType.SustainOff => MusicMarkPosition.Beginning,
        MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff => MusicMarkPosition.Beginning,
        MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff => MusicMarkPosition.Beginning,
        _ => MusicMarkPosition.End
    };

    private static MusicMarkVertical GetMarkVertical(MusicMarkType type) => type switch
    {
        // Jump-FROM instructions sit below the staff, right-aligned at the barline
        // (Gould, Behind Bars). Segno/Coda targets and "To Coda" stay above.
        MusicMarkType.DalSegno or MusicMarkType.DaCapo
            or MusicMarkType.DalSegnoAlFine or MusicMarkType.DalSegnoAlCoda
            or MusicMarkType.DaCapoAlFine or MusicMarkType.DaCapoAlCoda
            => MusicMarkVertical.Below,
        MusicMarkType.Rit => MusicMarkVertical.Below,
        MusicMarkType.Accel => MusicMarkVertical.Below,
        MusicMarkType.Cresc => MusicMarkVertical.Below,
        MusicMarkType.Decresc => MusicMarkVertical.Below,
        MusicMarkType.Dim => MusicMarkVertical.Below,
        // 8va/15ma are above staff; 8vb/15mb are below staff
        MusicMarkType.OttavaUp or MusicMarkType.QuindicesUp => MusicMarkVertical.Above,
        MusicMarkType.OttavaDown or MusicMarkType.QuindicesDown => MusicMarkVertical.Below,
        MusicMarkType.Loco => MusicMarkVertical.Above,
        // LILYPOND-REF: define-grobs.scm:3275-3296 SustainPedalLineSpanner direction = DOWN
        MusicMarkType.SustainOn or MusicMarkType.SustainOff => MusicMarkVertical.Below,
        MusicMarkType.SostenutoOn or MusicMarkType.SostenutoOff => MusicMarkVertical.Below,
        MusicMarkType.UnaCordaOn or MusicMarkType.UnaCordaOff => MusicMarkVertical.Below,
        _ => MusicMarkVertical.Above
    };
}