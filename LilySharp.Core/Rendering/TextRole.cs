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

namespace LilySharp.Core.Rendering;

/// <summary>
/// WHAT a run of non-music text IS, as the engraver means it — the unit a
/// <c>fonts { }</c> directive binds a face to, and the only thing
/// <see cref="IDrawingContext.DrawText"/> is told about a string's typography.
/// </summary>
/// <remarks>
/// ⚠️ A ROLE, NOT A FAMILY. Every draw call used to pass the literal <c>"serif"</c> (36 of
/// the 37 sites did), so the page could say WHICH FACE a string wanted but never WHAT THE
/// STRING WAS — and a directive that wants to give lyrics one face and chord symbols
/// another has nothing to bind to. The family is now derived from the role by
/// <see cref="TextFontPlan"/>, which is the one place that knows a face name.
/// <para>
/// The leaves are the engraving objects the renderer actually distinguishes, one per
/// <c>SharedRenderer</c> draw site, and they are grouped by
/// <see cref="TextRoles.GroupOf"/> so a score can say <c>marks "Georgia"</c> without
/// naming five of them.
/// </para>
/// <para>
/// LilyPond binds the SAME TWO LAYERS. The broad one is
/// <c>\paper { property-defaults.fonts.serif = "DejaVu Serif" … }</c> — three generic
/// families set by name; the narrow one is <c>font-name</c> per grob. So the GROUPS here
/// are the only invention, and they are a shorthand that resolves to leaves rather than a
/// third thing.
/// ⚠️ VERIFIED AGAINST THE 2.26 TREE ON 2026-08-18, and the first spelling written here was
/// WRONG: it cited <c>make-pango-font-tree</c>, which 2.26 keeps only as a
/// <c>@funindex</c> in the translated manuals. It had been written from memory of an older
/// LilyPond — HANDOFF §0's rule about hearsay, arriving on the day it was quoted.
/// ⚠️ LilyPond's third family is <c>typewriter</c>; this engine draws no monospace text and
/// so has no such key (<see cref="TextRoles.TryParseFamily"/> refuses <c>mono</c>).
/// </para>
/// </remarks>
// LILYPOND-REF: input/regression/font-family-override.ly:20-24 property-defaults.fonts
// LILYPOND-REF: input/regression/font-name.ly:30-33 TimeSignature MultiMeasureRestText
public enum TextRole
{
    // ---- header: page and system labels -------------------------------------------
    /// <summary>The score's title (<c>DrawHeader</c>).</summary>
    Title,
    /// <summary>The score's composer line (<c>DrawHeader</c>).</summary>
    Composer,
    /// <summary>An instrument name at a system's left edge (<c>DrawInstrumentNames</c>).
    /// LilyPond grob: <c>InstrumentName</c>.</summary>
    Instrument,

    // ---- lyrics -------------------------------------------------------------------
    /// <summary>A syllable under a staff (<c>DrawLyrics</c>). LilyPond grob:
    /// <c>LyricText</c>.</summary>
    LyricText,
    /// <summary>A verse number before a lyric line (<c>DrawStanzaNumbers</c>). LilyPond
    /// grob: <c>StanzaNumber</c>.</summary>
    Stanza,

    // ---- chords: harmony labels ---------------------------------------------------
    /// <summary>A chord symbol above the staff (<c>DrawChordNames</c>). LilyPond grob:
    /// <c>ChordName</c>. ⚠️ The one role whose DEFAULT family is sans, which is
    /// LilyPond's default too.</summary>
    ChordName,
    /// <summary>The fret/finger digits inside a chord diagram
    /// (<c>DrawFretFrame</c>). LilyPond grob: <c>FretBoard</c>.</summary>
    FretFrame,
    /// <summary>A figured-bass digit under the staff (<c>DrawFiguredBass</c>). LilyPond
    /// grob: <c>BassFigure</c>.</summary>
    FiguredBass,

    // ---- marks: directive and expressive text -------------------------------------
    /// <summary>Tempo text and the metronome equation (<c>DrawSingleMusicMark</c>,
    /// <c>DrawSwingEquation</c>). LilyPond grob: <c>MetronomeMark</c>.</summary>
    Tempo,
    /// <summary>A boxed rehearsal mark or section label (<c>DrawSingleMusicMark</c>).
    /// LilyPond grob: <c>RehearsalMark</c>.</summary>
    Mark,
    /// <summary>Pedal text — Ped. / sost. / una corda (<c>DrawSingleMusicMark</c>).</summary>
    Pedal,
    /// <summary>Navigation text — D.S. / D.C. / Fine / "To" (<c>DrawSingleMusicMark</c>).</summary>
    Navigation,
    /// <summary>Free text attached to a note or spanning a passage
    /// (<c>DrawCustomTexts</c>, <c>DrawTextSpanners</c>). LilyPond grobs:
    /// <c>TextScript</c>, <c>TextSpanner</c>.</summary>
    Text,
    /// <summary>A dynamic letter or word (<c>DrawDynamics</c>). LilyPond grob:
    /// <c>DynamicText</c>.</summary>
    Dynamics,
    /// <summary>The a2 / solo labels a part-combine prints (<c>DrawPartCombine</c>).</summary>
    PartCombine,

    // ---- numbers: small labels attached to the notation ---------------------------
    /// <summary>A bar number (<c>DrawBarNumbers</c>). LilyPond grob: <c>BarNumber</c>.</summary>
    BarNumber,
    /// <summary>A fingering digit (<c>DrawFingeringsLive</c>). LilyPond grob:
    /// <c>Fingering</c>.</summary>
    Fingering,
    /// <summary>A tuplet number over its bracket (<c>DrawTupletBrackets</c>). LilyPond
    /// grob: <c>TupletNumber</c>.</summary>
    Tuplet,
    /// <summary>A volta label (<c>DrawVoltaBrackets</c>). LilyPond grob:
    /// <c>VoltaBracket</c>.</summary>
    Volta,
    /// <summary>The 8va / 8vb label on an ottava bracket (<c>DrawOttavaBrackets</c>).
    /// LilyPond grob: <c>OttavaBracket</c>.</summary>
    Ottava,
    /// <summary>The semitone label on a guitar bend (<c>DrawGuitarBend</c>).</summary>
    Bend,
    /// <summary>A tab technique letter — H / P / T (<c>DrawArticulations</c>).</summary>
    TabTechnique,

    // ---- notation: text that is really a notation glyph ---------------------------
    // ⚠️ EXCLUDED FROM THE BROAD BINDING — see TextFontPlan.IsNotation.
    /// <summary>The «8» under or over an octave-transposing clef
    /// (<c>DrawClefModifier8</c>). LilyPond grob: <c>ClefModifier</c>.</summary>
    ClefOctave,
    /// <summary>The «+» of a compound meter's numerator, which LilyPond spells with
    /// markup rather than a feta glyph (<c>DrawTimeSignature</c>).</summary>
    Meter,
    /// <summary>A fret number on a tab staff (<c>DrawTabFret</c>,
    /// <c>DrawTabGraceNotes</c>). LilyPond grob: <c>TabNoteHead</c>.</summary>
    TabFret,

    // ---- not a text role at all ---------------------------------------------------
    /// <summary>
    /// The system-start brace, which is drawn as text only because it is a glyph of the
    /// Emmentaler BRACE face and that face is addressed by name.
    /// </summary>
    /// <remarks>
    /// ⚠️ NEVER BOUND. <see cref="TextFontPlan"/> answers this one with the music face
    /// whatever the score asked for; it is in this enum so that no draw site has to pass a
    /// family string beside the role, which is the second spelling this type exists to
    /// delete.
    /// </remarks>
    SystemBrace,
}

/// <summary>
/// The COARSE names a score may bind — each one a set of <see cref="TextRole"/> leaves,
/// so <c>marks "Georgia"</c> reaches five roles and <c>tempo "Georgia"</c> reaches one.
/// </summary>
/// <remarks>
/// A group is only a shorthand: <see cref="TextFontPlan"/> resolves the LEAF first and the
/// group second, so the narrower spelling always wins no matter which order they were
/// written in. That rule is the whole reason groups can exist without ambiguity.
/// </remarks>
public enum TextRoleGroup
{
    /// <summary>Title, composer, instrument names.</summary>
    Header,
    /// <summary>Lyric syllables and stanza numbers.</summary>
    Lyrics,
    /// <summary>Chord symbols, chord diagrams, figured bass.</summary>
    Chords,
    /// <summary>Tempo, rehearsal marks, pedal, navigation, free text, dynamics,
    /// part-combine labels.</summary>
    Marks,
    /// <summary>Bar numbers, fingerings, tuplet/volta/ottava/bend labels, tab
    /// techniques.</summary>
    Numbers,
    /// <summary>Text that is really notation — the clef's octave digit, a compound
    /// meter's «+», tab fret numbers. NOT reached by the broad binding.</summary>
    Notation,
}

/// <summary>Which of the two bundled text faces a role FALLS BACK to.</summary>
/// <remarks>
/// ⚠️ THIS IS THE FALLBACK, not "the face". A score that binds a role to a face this
/// machine has is measured and drawn in THAT file
/// (<see cref="ScoreTextMetrics.Face"/>); this family is where the role lands when it
/// binds nothing, or when the face it binds is not installed here. The note that stood
/// here said the reservation always used it — true until 2026-08-18, when the layout
/// learned to ask by role.
/// </remarks>
public enum TextFontFamily
{
    /// <summary>TeX Gyre Schola, LilyPond's <c>"LilyPond Serif"</c> twin.</summary>
    Serif,
    /// <summary>TeX Gyre Heros, LilyPond's <c>"LilyPond Sans Serif"</c> twin.</summary>
    Sans,
}

/// <summary>
/// The static shape of the role vocabulary: which group a leaf belongs to, which family
/// it is measured against, and the spellings a score writes.
/// </summary>
public static class TextRoles
{
    /// <summary>Every leaf role, in declaration order.</summary>
    public static readonly IReadOnlyList<TextRole> All =
        (TextRole[])Enum.GetValues(typeof(TextRole));

    /// <summary>The group <paramref name="role"/> belongs to.</summary>
    /// <remarks>
    /// <see cref="TextRole.SystemBrace"/> has no group — it is not text — and asking for
    /// one returns null rather than a group a binding could accidentally reach.
    /// </remarks>
    public static TextRoleGroup? GroupOf(TextRole role) => role switch
    {
        TextRole.Title or TextRole.Composer or TextRole.Instrument => TextRoleGroup.Header,
        TextRole.LyricText or TextRole.Stanza => TextRoleGroup.Lyrics,
        TextRole.ChordName or TextRole.FretFrame or TextRole.FiguredBass => TextRoleGroup.Chords,
        TextRole.Tempo or TextRole.Mark or TextRole.Pedal or TextRole.Navigation
            or TextRole.Text or TextRole.Dynamics or TextRole.PartCombine => TextRoleGroup.Marks,
        TextRole.BarNumber or TextRole.Fingering or TextRole.Tuplet or TextRole.Volta
            or TextRole.Ottava or TextRole.Bend or TextRole.TabTechnique => TextRoleGroup.Numbers,
        TextRole.ClefOctave or TextRole.Meter or TextRole.TabFret => TextRoleGroup.Notation,
        _ => null,
    };

    /// <summary>
    /// The bundled family <paramref name="role"/> is measured against when nothing
    /// redirects it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm — <c>ChordName</c> carries
    /// <c>(font-family . sans)</c>; every other text grob this engine draws inherits the
    /// default <c>roman</c>. So the one exception here is LilyPond's exception, not an
    /// invention.
    /// </remarks>
    public static TextFontFamily DefaultFamily(TextRole role) =>
        role == TextRole.ChordName ? TextFontFamily.Sans : TextFontFamily.Serif;

    /// <summary>
    /// Is <paramref name="role"/> notation drawn as text — the clef's octave digit, a
    /// compound meter's «+», a tab fret number?
    /// </summary>
    /// <remarks>
    /// ⚠️ THESE DO NOT FOLLOW THE BROAD BINDING, decided 2026-08-18. A <c>serif</c>/
    /// <c>sans</c> binding says what the score's PROSE is set in; a fret
    /// number is not prose, and restyling it changes the notation rather than the words.
    /// Before this rule, a broad binding reached all three. They are still
    /// bindable — by naming <c>notation</c> or the leaf itself — because a score that
    /// really wants its tab numbers in another face should be able to say so, out loud.
    /// <para>
    /// LILYPOND-REF: input/regression/dead-notes.ly:55 TabNoteHead — LilyPond agrees on
    ///   both halves: its BROAD setting (property-defaults.fonts) does not touch a fret
    ///   number, and the way to change one is to name the grob,
    ///   <c>\override TabNoteHead.font-name = "DejaVu Sans Mono"</c>. Read on 2026-08-18
    ///   AFTER the rule was decided, so this is confirmation and not the source of it.
    /// </para>
    /// </remarks>
    public static bool IsNotation(TextRole role) => GroupOf(role) == TextRoleGroup.Notation;

    /// <summary>
    /// The leaves <paramref name="group"/> contains.
    /// </summary>
    public static IEnumerable<TextRole> LeavesOf(TextRoleGroup group)
        => All.Where(r => GroupOf(r) == group);

    /// <summary>
    /// The spelling a score writes for <paramref name="role"/> inside <c>fonts { }</c>.
    /// </summary>
    /// <remarks>
    /// camelCase, matching the language's own multi-word keywords (<c>grandStaff</c>,
    /// <c>staffGroup</c>, <c>choirStaff</c>) rather than inventing a second convention.
    /// </remarks>
    public static string Spelling(TextRole role) => role switch
    {
        TextRole.Title => "title",
        TextRole.Composer => "composer",
        TextRole.Instrument => "instrument",
        TextRole.LyricText => "lyricText",
        TextRole.Stanza => "stanza",
        TextRole.ChordName => "chordName",
        TextRole.FretFrame => "fretFrame",
        TextRole.FiguredBass => "figuredBass",
        TextRole.Tempo => "tempo",
        TextRole.Mark => "mark",
        TextRole.Pedal => "pedal",
        TextRole.Navigation => "navigation",
        TextRole.Text => "text",
        TextRole.Dynamics => "dynamics",
        TextRole.PartCombine => "partCombine",
        TextRole.BarNumber => "barNumber",
        TextRole.Fingering => "fingering",
        TextRole.Tuplet => "tuplet",
        TextRole.Volta => "volta",
        TextRole.Ottava => "ottava",
        TextRole.Bend => "bend",
        TextRole.TabTechnique => "tabTechnique",
        TextRole.ClefOctave => "clefOctave",
        TextRole.Meter => "meter",
        TextRole.TabFret => "tabFret",
        // Not written in any score; see the remark on the member.
        TextRole.SystemBrace => "systemBrace",
        _ => role.ToString(),
    };

    /// <summary>The spelling a score writes for <paramref name="group"/>.</summary>
    public static string Spelling(TextRoleGroup group) => group switch
    {
        TextRoleGroup.Header => "header",
        TextRoleGroup.Lyrics => "lyrics",
        TextRoleGroup.Chords => "chords",
        TextRoleGroup.Marks => "marks",
        TextRoleGroup.Numbers => "numbers",
        TextRoleGroup.Notation => "notation",
        _ => group.ToString(),
    };

    /// <summary>The spelling a score writes for <paramref name="family"/>.</summary>
    public static string Spelling(TextFontFamily family) =>
        family == TextFontFamily.Sans ? "sans" : "serif";

    /// <summary>
    /// Reads a <c>fonts { }</c> key: a leaf role, a group, or a generic family.
    /// </summary>
    /// <param name="word">The written key, e.g. <c>lyricText</c> / <c>marks</c> /
    /// <c>serif</c>.</param>
    /// <param name="role">The leaf, when <paramref name="word"/> spells one.</param>
    /// <param name="group">The group, when <paramref name="word"/> spells one.</param>
    /// <param name="family">The generic family, when <paramref name="word"/> spells one.</param>
    /// <returns>True when the word is a key this vocabulary knows.</returns>
    /// <remarks>
    /// ⚠️ CASE-INSENSITIVE, deliberately: <c>lyricText</c> and <c>lyrictext</c> are the
    /// same key, because the reader who mistypes the hump should get the binding rather
    /// than a "no such role" they have to squint at. The DIAGNOSTIC still prints the
    /// canonical spelling. <c>systemBrace</c> is refused — it is not a text role and
    /// binding it would mean asking for the brace in Georgia.
    /// </remarks>
    public static bool TryParseKey(string word, out TextRole? role, out TextRoleGroup? group,
        out TextFontFamily? family)
    {
        role = null;
        group = null;
        family = null;
        foreach (var candidate in All)
        {
            if (candidate == TextRole.SystemBrace)
                continue;
            if (string.Equals(word, Spelling(candidate), StringComparison.OrdinalIgnoreCase))
            {
                role = candidate;
                return true;
            }
        }
        foreach (TextRoleGroup candidate in Enum.GetValues(typeof(TextRoleGroup)))
        {
            if (string.Equals(word, Spelling(candidate), StringComparison.OrdinalIgnoreCase))
            {
                group = candidate;
                return true;
            }
        }
        if (TryParseFamily(word, out var f))
        {
            family = f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reads a generic family word — the values a key may point at instead of a face name.
    /// </summary>
    /// <param name="word">The written family, e.g. <c>serif</c> or <c>sans</c>.</param>
    /// <param name="family">The family, when the word spells one.</param>
    /// <returns>True when the word is a generic family.</returns>
    /// <remarks>
    /// ⚠️ <c>sans-serif</c> IS ACCEPTED as a spelling of <c>sans</c> because that is the
    /// CSS/SVG name and the string the draw sites passed for years; <c>mono</c> is NOT,
    /// because no role in this engine is monospace and a binding that reaches nothing is
    /// worse than a word the reader is told does not exist.
    /// </remarks>
    public static bool TryParseFamily(string word, out TextFontFamily family)
    {
        if (string.Equals(word, "serif", StringComparison.OrdinalIgnoreCase))
        {
            family = TextFontFamily.Serif;
            return true;
        }
        if (string.Equals(word, "sans", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(word, "sans-serif", StringComparison.OrdinalIgnoreCase))
        {
            family = TextFontFamily.Sans;
            return true;
        }
        family = TextFontFamily.Serif;
        return false;
    }

    /// <summary>Every key a score may write, canonically spelled — for diagnostics.</summary>
    public static IEnumerable<string> AllKeySpellings()
    {
        yield return "serif";
        yield return "sans";
        foreach (TextRoleGroup g in Enum.GetValues(typeof(TextRoleGroup)))
            yield return Spelling(g);
        foreach (var r in All)
            if (r != TextRole.SystemBrace)
                yield return Spelling(r);
    }
}
