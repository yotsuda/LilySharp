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

using System;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What the value-carrying annotations MEAN, read once from their arguments.
/// </summary>
/// <remarks>
/// <para>
/// The families whose argument IS a value moved first — <c>@finger(N)</c>,
/// <c>@pluck(p|i|m|a)</c>, <c>@bend(half|full|N)</c>, <c>@notehead(style)</c>,
/// <c>@text("…")</c>, <c>@feather(…)</c>, <c>@arpeggio(bracket)</c> — because nothing
/// is lost by reading them as one (<c>docs/VALUE_SITE_AUDIT.md</c> §9.5). Then the two
/// whose SPELLING is their meaning: <c>@frame</c>'s fret position string, read from the
/// text because its value has dropped a leading zero, and <c>@chord</c>'s sub-language,
/// read from the text because it denotes no single value at all. Only <c>@fig</c> is
/// left, and it is left deliberately — its runs and its dotted parts do not divide at
/// the same places, so moving it would change which spellings are accepted (§9.5.1).
/// </para>
/// <para>
/// Each is read HERE and only here. Before this type, every one of them was spelled
/// twice: once by the consumer that acts on it, and once again by
/// <see cref="AnnotationNameValidator.IsKnownCompoundName"/> deciding whether anything
/// consumes it — the second copy being the tenth restatement §9.3 counted. Two copies
/// of one predicate is the shape HANDOFF §5.2.1② names, and these two DID disagree; see
/// <see cref="Finger"/>.
/// </para>
/// <para>
/// ⚠️ Each reads the ARGUMENT, not the dotted <see cref="MusicMarkSyntax.MarkName"/>.
/// The gates below are the ones the string-slicing versions applied, transcribed rather
/// than tidied, with the one exception recorded on <see cref="Finger"/>.
/// </para>
/// </remarks>
public static class AnnotationValues
{
    /// <summary>
    /// The finger number of <c>@finger(N)</c>, or null when this is not one.
    /// </summary>
    /// <remarks>
    /// Any non-negative integer, which is the set LilyPond's own grammar takes (measured
    /// on 2.26.0: <c>-0</c>, <c>-5</c>, <c>-6</c> and <c>-12</c> all engrave a Fingering).
    /// <para>
    /// ⚠️ <b>A behaviour change, declared:</b> the two former copies disagreed about
    /// <c>@finger("3")</c>. The collector's read (<c>int.TryParse</c> on the dotted tail)
    /// rejected the quotes, so Lily# drew nothing; the LilyPond exporter's read trimmed
    /// them first and emitted <c>-3</c> anyway — a twin that states music the source does
    /// not. Its own comment claimed the two read "the SAME set". They now do, on the
    /// collector's rule, which makes that claim true. No book writes the quoted form
    /// (measured over the 80-book corpus and 219 fixtures: every written finger argument
    /// is a bare digit), so this converges an unexercised divergence rather than changing
    /// any book.
    /// </para>
    /// </remarks>
    public static int? Finger(MusicMarkSyntax mark)
        => Named(mark, "finger") && Sole(mark) is { Value: LysValue.Int { V: >= 0 and <= int.MaxValue } n }
            ? (int)n.V
            : null;

    /// <summary>
    /// The right-hand finger letter of <c>@pluck(p|i|m|a)</c> in lower case, or null.
    /// </summary>
    /// <remarks>LILYPOND-REF: the p-i-m-a fingering, printed below the note.</remarks>
    public static string? Pluck(MusicMarkSyntax mark)
        => Named(mark, "pluck") && Sole(mark) is { Text.Length: 1 } argument
           && char.ToLowerInvariant(argument.Text[0]) is 'p' or 'i' or 'm' or 'a'
            ? argument.Text.ToLowerInvariant()
            : null;

    /// <summary>
    /// The semitones of a guitar bend-up, <c>@bend(half|full|N)</c>, or null.
    /// </summary>
    /// <remarks>LILYPOND-REF: MusicXML &lt;bend&gt;&lt;bend-alter&gt; semantics.</remarks>
    public static int? Bend(MusicMarkSyntax mark)
    {
        if (!Named(mark, "bend") || Sole(mark) is not { } argument)
            return null;
        return argument.Text.ToLowerInvariant() switch
        {
            "half" => 1,
            "full" => 2,
            _ => argument.Value?.AsInt is { } n && n is > 0 and <= 12 ? n : null,
        };
    }

    /// <summary>
    /// The notehead style word of <c>@notehead(style)</c> in lower case, or null when
    /// the annotation is not one or names no style Lily# draws.
    /// </summary>
    /// <remarks>
    /// The WORD, not a style enum: its two consumers map it to different things (Lily#'s
    /// own <c>NoteheadStyle</c> and MusicXML's <c>&lt;notehead&gt;</c> vocabulary), and
    /// those mappings are genuinely two. What was three copies of the ACCEPTED SET is one.
    /// </remarks>
    public static string? Notehead(MusicMarkSyntax mark)
        => Named(mark, "notehead") && Sole(mark)?.Text.ToLowerInvariant() is
           ("x" or "cross" or "diamond" or "triangle" or "slash" or "xcircle") and var style
            ? style
            : null;

    /// <summary>
    /// The free text of <c>@text("…")</c> — the string's CONTENT, without its quotes —
    /// or null when the annotation is not one or carries no string.
    /// </summary>
    /// <remarks>
    /// The FIRST string argument, which is what the hand-walk this replaces took (it
    /// scanned the node's slots for the first StringLiteral). An unquoted argument is
    /// not free text and yields null, so <c>@text(dolce)</c> draws nothing — unchanged.
    /// The side is not read here: a trailing <c>.up</c> / <c>.down</c> is a qualifier on
    /// the annotation, and <see cref="MusicMarkSyntax.ForcedAbove"/> answers it.
    /// LILYPOND-REF: TextScript (LP's <c>c^"text"</c> / <c>c_"text"</c>).
    /// </remarks>
    public static string? Text(MusicMarkSyntax mark)
        => Named(mark, "text")
            ? mark.Arguments.Select(a => a.Value).OfType<LysValue.Str>().FirstOrDefault()?.V
            : null;

    /// <summary>
    /// Whether this is a <c>@text(…)</c> annotation that WROTE an argument, whatever it
    /// says. This is what "is it consumed?" means for the family — the string form asked
    /// it as <c>StartsWith("text.")</c>, which an empty <c>@text()</c> fails.
    /// </summary>
    public static bool IsTextAnnotation(MusicMarkSyntax mark)
        => Named(mark, "text") && mark.Arguments.Length > 0;

    /// <summary>
    /// The beam's grow direction from <c>@feather(right|accel)</c> (+1, accelerando) or
    /// <c>@feather(left|rit)</c> (−1, ritardando); 0 for anything else.
    /// </summary>
    /// <remarks>
    /// The LilyPond addresses for the grow-direction property — where it is declared and
    /// where it reaches stem length — are on <c>MeasureCollector.GetFeatherDirection</c>,
    /// which calls this. They are NOT repeated here: a citation copied to a second place
    /// is one more thing to keep true, and this one was already wrong once in the copying
    /// (a draft of this remark put <c>set_stem_lengths</c> at beam.cc:1597, which is the
    /// property list in <c>ADD_INTERFACE (Beam, …)</c>, not a function).
    /// </remarks>
    public static int Feather(MusicMarkSyntax mark)
        => Named(mark, "feather")
            ? Sole(mark)?.Text.ToLowerInvariant() switch
            {
                "right" or "accel" => 1,
                "left" or "rit" => -1,
                _ => 0,
            }
            : 0;

    /// <summary>
    /// Whether this is <c>@arpeggio(bracket)</c> — the non-arpeggiated chord bracket.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT LilyPond's <c>\arpeggioBracket</c>, which is a pair of overrides; the twin
    /// writes <c>\nonArpeggiato</c>, whose engraver makes the ChordBracket grob this
    /// means. The full argument, with its addresses, is on
    /// <c>LilyPondExporter.NonArpeggiato</c>; they are not repeated here.
    /// </remarks>
    public static bool IsArpeggioBracket(MusicMarkSyntax mark)
        => Named(mark, "arpeggio")
           && string.Equals(Sole(mark)?.Text, "bracket", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The fret-diagram position string of <c>@frame(032010)</c> in lower case, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ This is the family the argument node was SHAPED for (VALUE_SITE_AUDIT §9.2).
    /// The spec is a POSITION STRING — one character per string, low to high, a digit for
    /// the fret, <c>o</c> for open, <c>x</c> for muted — so <c>032010</c> is six strings
    /// beginning with an open sixth. It is read from <see cref="MarkArgument.Text"/> and
    /// never from its <see cref="MarkArgument.Value"/>, which is <c>Int(32010)</c> and has
    /// dropped the leading zero, i.e. turned a six-string shape into a five-string one.
    /// The net that states this is <c>MarkArgumentTests.AFretPositionArgument_…</c>.
    /// </para>
    /// <para>
    /// ⚠️ <b>A behaviour change, declared</b> — the same shape as <see cref="Finger"/>'s.
    /// The MusicXML exporter had NO gate: it took whatever followed "frame." and wrote it
    /// into the chord diagram, so <c>@frame(zzz)</c> reached the XML while Lily# drew
    /// nothing and the validator called it unknown. Three copies, one of them accepting a
    /// strictly larger set than the thing it was exporting. One reader, so one answer.
    /// No book writes <c>@frame(</c> at all (measured over the 80-book corpus and 219
    /// fixtures), so the nets here are the only guard this family has ever had.
    /// </para>
    /// LILYPOND-REF: MusicXML &lt;frame&gt;; LilyPond's <c>\fret-diagram</c>.
    /// </remarks>
    public static string? Frame(MusicMarkSyntax mark)
        => Named(mark, "frame") && Sole(mark)?.Text.ToLowerInvariant() is { } spec
           && spec.Length is >= 4 and <= 8
           && spec.All(ch => ch is 'x' or 'o' or (>= '0' and <= '9'))
            ? spec
            : null;

    /// <summary>
    /// The chord symbol of <c>@chord(c:m7)</c> as it should print (<c>Cm7</c>), the
    /// empty string for a bare <c>@chord</c> (which derives its symbol from the notes),
    /// or null when this is not a chord annotation or names no chord Lily# can write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ A SUB-LANGUAGE, not a value (VALUE_SITE_AUDIT §9.2): the argument borrows the
    /// music grammar, so <c>c:m7</c> arrives as the tokens <c>c</c> <c>:</c> <c>m7</c>
    /// and denotes no single <see cref="LysValue"/>. It is therefore read from
    /// <see cref="MarkArgument.Text"/> — which, arguments being RUNS of adjacent tokens,
    /// is already the written <c>"c:m7"</c>. That is the whole point of this reader:
    /// the dotted <see cref="MusicMarkSyntax.MarkName"/> spelled the same value
    /// <c>chord.c.:.m7</c> and the chord parser split it on '.' and rejoined it with ""
    /// to get the written text back — the second and third of the three round trips
    /// §9.3 counted, now gone.
    /// </para>
    /// <para>
    /// ⚠️ ALL the arguments, joined, not the first one. <c>@chord(c :m7)</c> — written
    /// with a space — is two runs and names Cm7 today, and reading only the first would
    /// quietly stop accepting it. (Measured: across the 80-book corpus and 219 fixtures
    /// every one of the 14 written <c>@chord(…)</c> arguments is a single run, so the
    /// spaced form is unexercised, not impossible.)
    /// </para>
    /// <para>
    /// ⚠️ <b>A behaviour change, declared and chosen</b>: a '.' WRITTEN inside the
    /// parentheses used to disappear, because MarkName joined the tokens with dots and
    /// the parser then removed every dot — so <c>@chord(b.es:7)</c> printed B♭7 and
    /// <c>@chord(.c:m7)</c> printed Cm7. Reading the run keeps what was written, so
    /// those now name no chord and are reported as unknown annotations. No book writes
    /// a dot inside <c>@chord(</c> (measured by spelling all 299 books back out of their
    /// trees), and nothing ever documented the swallowing; it was an artefact of the
    /// round trip this reader removes.
    /// </para>
    /// <para>
    /// ⚠️ The name gate is case-SENSITIVE, unlike <see cref="Named"/>: the string form
    /// asked <c>StartsWith("chord.", Ordinal)</c>, so <c>@Chord(c)</c> names no chord
    /// today and must keep naming none.
    /// </para>
    /// LILYPOND-REF: scm/chord-ignatzek-names.scm — root + quality → printed name.
    /// </remarks>
    public static string? Chord(MusicMarkSyntax mark, out Music.ChordStructure? structure)
    {
        structure = null;
        if (!string.Equals(mark.Name, "chord", StringComparison.Ordinal))
            return null;

        // No argument at all: a bare '@chord' derives its symbol from the notes it sits
        // on, and '@chord()' is the state the completion leaves behind. Both are known
        // annotations that name nothing here, which is what the empty string says.
        var written = mark.Arguments.Length switch
        {
            0 => "",
            1 => mark.Arguments[0].Text,
            _ => string.Concat(mark.Arguments.Select(a => a.Text)),
        };
        if (written.Length == 0)
            return "";

        // Quoted free text — @chord("N.C.") — prints verbatim, dots and all. Read from
        // the TEXT (which keeps the quotes) rather than the value, so that an unbalanced
        // quote is refused here exactly as it was before.
        if (written[0] == '"')
        {
            int close = written.LastIndexOf('"');
            return close >= 1 ? written.Substring(1, close - 1) : null;
        }

        // A real chord entry (the chords{} form) prints as its canonical symbol;
        // anything else is refused (→ unknown-annotation warning), which steers @chord
        // to chords that can also be played. Free display text goes in quotes, above.
        if (Music.ChordStructure.TryParseChordEntry(written, out var parsed))
        {
            structure = parsed;
            return parsed.DisplayName;
        }
        return null;
    }

    private static bool Named(MusicMarkSyntax mark, string name)
        => string.Equals(mark.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single argument, or null when the annotation has none or several. All four
    /// families take exactly one — a second argument was never accepted by the string
    /// forms either, because the extra dotted part broke every one of their parses.
    /// </summary>
    private static MarkArgument? Sole(MusicMarkSyntax mark)
        => mark.Arguments is [var only] ? only : null;
}
