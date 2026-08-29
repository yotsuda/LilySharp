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
    /// <summary>The START of a text spanner — the printed word plus the dashed rule that
    /// runs from it to its <see cref="TextSpanStop"/>. The word is NOT in the type:
    /// <c>@textSpan("poco rit.")</c> carries it as an argument, and the sugar spellings
    /// (<c>@rit</c>, <c>@accel</c>, <c>@rall</c>) carry it in the one table
    /// <see cref="MusicMarkItem.TextSpanSugarText"/> — because LilyPond's own vocabulary is
    /// open (<c>ly/articulate.ly:565-589</c> compares STRINGS, and its TODO asks for more
    /// synonyms), so an enum arm per word would be a closed list of an open set.</summary>
    TextSpanStart,
    /// <summary>The END of a text spanner (<c>@!rit</c>, <c>@!textSpan</c>). It prints
    /// nothing of its own: it is the place the rule stops.</summary>
    /// <remarks>LILYPOND-REF: lily/text-spanner-engraver.cc:60-68 Text_spanner_engraver::process_music — the stop event ends the
    /// open spanner and makes no grob.</remarks>
    TextSpanStop,
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

    /// <summary>
    /// The voice this mark was authored in (0 = the first/only voice). The text spanner
    /// pairs its START with its STOP within the SAME voice, which is where LilyPond keeps
    /// the engraver that pairs them.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:375 — <c>\consists Text_spanner_engraver</c> stands
    /// in the <c>Voice</c> context, so each voice holds its own open spanner and a
    /// <c>\stopTextSpan</c> in another voice cannot reach it.
    /// </remarks>
    public int VoiceIndex { get; init; }

    /// <summary>Source position for click-to-source mapping.</summary>
    public int SourcePosition { get; init; }

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
    /// Marks whose visuals are produced by a SPANNER engraver (text
    /// spanners, ottava brackets): their MusicMarkLayout entries are not
    /// drawn by DrawMusicMarks and must not occupy outside-staff space.
    /// </summary>
    public static bool IsSpannerHandled(MusicMarkType type) =>
        type is MusicMarkType.Cresc or MusicMarkType.Decresc or MusicMarkType.Dim
             or MusicMarkType.TextSpanStart or MusicMarkType.TextSpanStop
             or MusicMarkType.OttavaUp or MusicMarkType.OttavaDown
             or MusicMarkType.QuindicesUp or MusicMarkType.QuindicesDown
             or MusicMarkType.Loco;

    /// <summary>
    /// The words that open a text spanner as SUGAR, mapped to the text each one prints —
    /// or null when <paramref name="name"/> is not one of them. The one place a word is
    /// turned into a printed string; adding a synonym is adding a line here.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/articulate.ly (ac:getactions, its TextScriptEvent branch at lines
    /// 565-589 — cited WITHOUT a range on purpose: nothing there carries a name the citation
    /// ratchet can check, and a range with no name is what that ratchet counts)
    /// — LilyPond compares the spanner's text
    /// against the STRINGS "rall", "rit.", "accel.", "poco rall.", and the TODO on the same
    /// lines asks for more synonyms. The vocabulary is open by construction, which is why
    /// these are a table of words and not arms of <see cref="MusicMarkType"/>: every one of
    /// them makes the same grob, differing only in what is printed.
    /// <para>
    /// ⚠️ The GENERAL spelling is <c>@textSpan("…")</c>, which takes any text at all. These
    /// three are shorthand for the three a reader writes constantly, and each is exactly
    /// <c>@textSpan("…")</c> with the argument filled in — nothing else about them differs,
    /// the terminator <c>@!rit</c> included.
    /// </para>
    /// </remarks>
    public static string? TextSpanSugarText(string name) => name.ToLowerInvariant() switch
    {
        "rit" => "rit.",
        "accel" => "accel.",
        "rall" => "rall.",
        _ => null
    };

    /// <summary>Parses a mark NAME (e.g. <c>segno</c>, <c>ds.al.fine</c>,
    /// <c>ottava.bassa</c>) into a <see cref="MusicMarkType"/>, or null if
    /// unrecognized.</summary>
    /// <remarks>
    /// A table of names, nothing else. The rehearsal mark used to be answered here too,
    /// by testing the dotted string for a <c>"mark."</c> prefix — but <c>@mark("A")</c>
    /// writes an ARGUMENT, not a compound name, and its label is read from that argument
    /// by <see cref="Semantics.AnnotationValues.Rehearsal"/>
    /// (docs/VALUE_SITE_AUDIT.md §9.5.3 ⑵). Everything left below is a name a reader
    /// types whole, so this takes a string and asks nothing about arguments.
    /// </remarks>
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
            // The text spanner: the general spelling plus the three sugar words. All four
            // open the SAME spanner and differ only in the text they print, which
            // TextSpanSugarText / the @textSpan argument supplies — see BuildPlain.
            "textspan" or "rit" or "accel" or "rall" => MusicMarkType.TextSpanStart,
            "cresc" => MusicMarkType.Cresc,
            "decresc" => MusicMarkType.Decresc,
            "dim" => MusicMarkType.Dim,
            "ottava" or "8va" => MusicMarkType.OttavaUp,
            "ottava.bassa" or "8vb" => MusicMarkType.OttavaDown,
            "quindicesima" or "15ma" => MusicMarkType.QuindicesUp,
            "quindicesima.bassa" or "15mb" => MusicMarkType.QuindicesDown,
            "loco" => MusicMarkType.Loco,
            // The pedals carry LilyPond's own names (ly/spanners-init.ly:
            // sustainOn/sustainOff, sostenutoOn/sostenutoOff, unaCorda/treCorde).
            // Each is ONE word: the event has no argument in LilyPond either —
            // it is a span event carrying only START/STOP, and how the pedal is
            // PRINTED is a context property (pedalSustainStyle), not an argument.
            // The earlier '@ped' / '@ped(off)' spellings put a state in an
            // argument slot that does not exist, so they are gone (audit B-5 kept
            // one spelling per pedal; this keeps LilyPond's).
            "sustainon" => MusicMarkType.SustainOn,
            "sustainoff" => MusicMarkType.SustainOff,
            "sostenutoon" => MusicMarkType.SostenutoOn,
            "sostenutooff" => MusicMarkType.SostenutoOff,
            "unacorda" => MusicMarkType.UnaCordaOn,
            "trecorde" => MusicMarkType.UnaCordaOff,
            _ => null
        };
    }

    /// <summary>
    /// Parses the name of a TERMINATOR annotation — the <c>X</c> of <c>@!X</c> — into the
    /// mark it ends, or null when nothing of that name can be ended.
    /// </summary>
    /// <remarks>
    /// <c>@!X</c> closes what <c>@X</c> opened, so this takes the SAME names
    /// <see cref="ParseMarkName"/> does and answers with the STOP of the family each one
    /// starts. Today only the text spanner has a terminator; the pedals and the ottava are
    /// the next family to move here, and until they do <c>@!sustainOn</c> is refused by
    /// name rather than accepted and dropped.
    /// <para>
    /// ⚠️ ONE STOP FOR THE WHOLE FAMILY, exactly as in LilyPond: <c>\stopTextSpan</c> ends
    /// whatever <c>\startTextSpan</c> is open, whatever word it printed. So <c>@!rit</c>
    /// and <c>@!textSpan</c> are the same mark, and a reader who opened with <c>@accel</c>
    /// may close with <c>@!accel</c> because that reads best — not because the engine can
    /// tell the two apart.
    /// </para>
    /// </remarks>
    public static MusicMarkType? ParseSpanEndName(string name) =>
        ParseMarkName(name) switch
        {
            MusicMarkType.TextSpanStart => MusicMarkType.TextSpanStop,
            _ => null
        };

    /// <summary>
    /// The mark a plain one-word annotation denotes — <c>@name</c>, or <c>@!name</c> when
    /// <paramref name="isSpanEnd"/> — with the text it prints already resolved, or null
    /// when the name denotes no mark.
    /// </summary>
    /// <remarks>
    /// The one door the collector's two mark sites go through, so the sugar table is read
    /// in one place. A text-span START built any other way has no text to print: the word
    /// is not recoverable from <see cref="MusicMarkType.TextSpanStart"/>, which is the
    /// point of it (see the enum's remark).
    /// </remarks>
    public static MusicMarkItem? BuildPlain(string name, bool isSpanEnd, int measureIndex,
        int sourcePosition, int anchorItemIndex = -1, Fraction anchorTiming = default)
    {
        var type = isSpanEnd ? ParseSpanEndName(name) : ParseMarkName(name);
        if (type is null)
            return null;
        if (type == MusicMarkType.TextSpanStart)
            return new MusicMarkItem(type.Value, TextSpanSugarText(name) ?? "",
                measureIndex, sourcePosition, anchorItemIndex, anchorTiming);
        return new MusicMarkItem(type.Value, measureIndex, sourcePosition,
            anchorItemIndex, anchorTiming);
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
        // A text spanner's word is written by the reader, not implied by the type: the
        // sugar words go through TextSpanSugarText and @textSpan("…") carries its own, both
        // via BuildPlain / the collector's argument reading, which use the TEXT constructor.
        // Reaching here means one was built without a word, and printing a guess ("rit.")
        // would put a word on the page that no one wrote.
        MusicMarkType.TextSpanStart => "",
        MusicMarkType.TextSpanStop => "",
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

    /// <summary>
    /// Where a mark of <paramref name="type"/> anchors, WITHOUT an instance.
    /// </summary>
    /// <remarks>
    /// <see cref="Position"/> is set from this at construction and never from anything else,
    /// so the two answers cannot differ — which is what lets a caller holding only a placed
    /// <c>MusicMarkLayout</c> (which carries no Position) price the same extent as one
    /// holding the item. <c>MusicMarkEngraver.MarkXExtent</c> is that caller.
    /// </remarks>
    internal static MusicMarkPosition PositionOf(MusicMarkType type) => GetMarkPosition(type);

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
        MusicMarkType.TextSpanStart => MusicMarkVertical.Below,
        MusicMarkType.TextSpanStop => MusicMarkVertical.Below,
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