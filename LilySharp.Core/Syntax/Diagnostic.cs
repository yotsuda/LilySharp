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

namespace LilySharp.Core.Syntax;

/// <summary>
/// Severity of a diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Suppressed diagnostic — not surfaced to the user.</summary>
    Hidden,
    /// <summary>Informational message; no problem implied.</summary>
    Info,
    /// <summary>Warning — a potential problem that does not stop compilation.</summary>
    Warning,
    /// <summary>Error — a problem that prevents successful compilation.</summary>
    Error
}

/// <summary>
/// Represents a diagnostic message (error, warning, etc.)
/// </summary>
public sealed class Diagnostic
{
    /// <summary>
    /// Initializes a new diagnostic with the given severity, source location, code, and message.
    /// </summary>
    public Diagnostic(DiagnosticSeverity severity, TextSpan span, string code, string message)
    {
        Severity = severity;
        Span = span;
        Code = code;
        Message = message;
    }

    /// <summary>
    /// The severity of this diagnostic.
    /// </summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>
    /// The location in source.
    /// </summary>
    public TextSpan Span { get; }

    /// <summary>
    /// Diagnostic code (e.g., "LYS001").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Returns a human-readable representation of this diagnostic.
    /// </summary>
    public override string ToString()
    {
        var severityStr = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            _ => "hidden"
        };
        return $"{severityStr} {Code}: {Message} at {Span}";
    }

    // Factory methods

    /// <summary>
    /// Creates an error diagnostic.
    /// </summary>
    public static Diagnostic Error(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Error, span, code, message);

    /// <summary>
    /// Creates a warning diagnostic.
    /// </summary>
    public static Diagnostic Warning(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Warning, span, code, message);

    /// <summary>
    /// Creates an informational diagnostic.
    /// </summary>
    public static Diagnostic Info(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Info, span, code, message);
}

/// <summary>
/// A collection of diagnostics.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>
    /// Adds a diagnostic to the bag.
    /// </summary>
    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    /// <summary>
    /// Adds a sequence of diagnostics to the bag.
    /// </summary>
    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    /// <summary>
    /// Adds an error diagnostic to the bag.
    /// </summary>
    public void Error(TextSpan span, string code, string message)
        => Add(Diagnostic.Error(span, code, message));

    /// <summary>
    /// Adds a warning diagnostic to the bag.
    /// </summary>
    public void Warning(TextSpan span, string code, string message)
        => Add(Diagnostic.Warning(span, code, message));

    /// <summary>
    /// Returns the diagnostics as a read-only list.
    /// </summary>
    public IReadOnlyList<Diagnostic> ToList() => _diagnostics;

    /// <summary>
    /// Gets a value indicating whether the bag contains any error diagnostics.
    /// </summary>
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets the number of diagnostics in the bag.
    /// </summary>
    public int Count => _diagnostics.Count;

    /// <summary>
    /// Gets the error diagnostics in the bag.
    /// </summary>
    public IEnumerable<Diagnostic> Errors => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets the warning diagnostics in the bag.
    /// </summary>
    public IEnumerable<Diagnostic> Warnings => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);
}

/// <summary>
/// Standard diagnostic codes for LilySharp.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ RETIRED NUMBERS — never reused, and listed here rather than at eight scattered
/// deletion sites so the next author picking a number sees them all at once:
/// </para>
/// <para>
/// <c>LYS0001</c> UnexpectedToken · <c>LYS0005</c> InvalidNumber · <c>LYS0013</c> the
/// removed <c>version</c> directive · <c>LYS1002</c> DuplicateVariable · <c>LYS1003</c>
/// InvalidPitch · <c>LYS1006</c> UndefinedPhrase · <c>LYS1015</c> MultipleFormDeclarations
/// · <c>LYS2003</c> NoTimeSignature · <c>LYS8007</c> FontOneLinerRemoved ·
/// <c>LYS6009</c> LyricsAttachmentUnbound and <c>LYS6010</c> LyricsAttachmentWrongStaff
/// (both guarded the <c>with lyrics</c> clause and died with it, 2026-08-19: at top
/// level a bound row folds only onto the staff it sings, a row for another part is a
/// legal independent band, an unbound row is the lead-sheet row; the surviving refusal
/// is the group case, <see cref="GroupRowNotBoundToStaffAbove"/>) ·
/// <c>LYS0031</c> WithClauseRemoved — the clause removal's own migration error, retired
/// the same day it was born (2026-08-19, user decision, still before the first tag):
/// with every book already respelled, <c>with</c> stopped being a keyword at all (the
/// LYS8007 / <c>font</c> precedent below), so the old spelling reads as ordinary
/// tokens — a bare display name, then a row — and the error had nothing left to catch.
/// </para>
/// <para>
/// LYS8007 lived for one day. It refused the one-line <c>font "NAME"</c> and told the
/// writer the block to write instead — a migration path for a spelling that could not have
/// reached anyone, since Lily# has never been released. The keyword was renamed to
/// <c>fonts</c> the same day (user decision 2026-08-18) and <c>font</c> stopped being a
/// keyword at all, which made the code unreachable as well as unnecessary.
/// </para>
/// <para>
/// All but LYS0013 were retired together on 2026-08-16, when the question was first asked
/// of the SET rather than one code at a time: 96 declared, 89 named by a caller, seven
/// named by nobody — no symbol reference, no string-literal reference, no test, no doc.
/// A code nothing raises is a rule the language does not have; the two most telling were
/// LYS1015, which says a file holds one form (<c>test/multi-movement.lys</c> declares
/// three), and LYS1006, whose subject IS validated but folded into
/// <see cref="UndefinedVariable"/>'s message ("Undefined variable or phrase").
/// <c>DiagnosticCodeTests.CodesThatNothingEmits_DoNotGrow</c> is the machine that keeps
/// the list from refilling.
/// </para>
/// </remarks>
public static class DiagnosticCodes
{
    // Parser errors (LYS0xxx)

    /// <summary>Parser error: an expected token was missing.</summary>
    public const string ExpectedToken = "LYS0002";
    /// <summary>Parser error: a string literal was not terminated.</summary>
    public const string UnterminatedString = "LYS0003";
    /// <summary>Parser error: a comment was not terminated.</summary>
    public const string UnterminatedComment = "LYS0004";
    /// <summary>Parser error: the removed repeat-volta syntax was used.</summary>
    public const string RepeatVoltaRemoved = "LYS0006";
    /// <summary>Parser error: a legacy declaration form was used.</summary>
    public const string LegacyDeclarationForm = "LYS0007";
    /// <summary>Parser error: the removed parallel syntax was used.</summary>
    public const string ParallelSyntaxRemoved = "LYS0008";
    /// <summary>Parser error: a LilyPond-style backslash command was used.</summary>
    public const string LilypondBackslashCommand = "LYS0009";
    /// <summary>Parser error: a voice block was nested where not allowed.</summary>
    public const string NestedVoiceBlock = "LYS0010";

    /// <summary>Parser error: the <c>voice</c> keyword was written again inside a span
    /// (<c>voice { … } voice { … }</c>). It opens the span ONCE; the other voices are
    /// further blocks (<c>voice { … } { … }</c>). Unlike the other retired spellings this
    /// one gets its own code, because it still parses: a second <c>voice</c> opens a
    /// SECOND span, and two one-voice spans play in sequence rather than together — so
    /// without this the file holds different music and says nothing.</summary>
    public const string RepeatedVoiceKeyword = "LYS0019";

    /// <summary>Parser warning: a bare clef name (treble/bass/…) was used like a
    /// staff block at the top level (e.g. <c>treble { … }</c>), which the grammar
    /// silently drops. Points to the real grand-staff form.</summary>
    public const string ClefNameAsStaff = "LYS0011";

    /// <summary>Parser warning: a silent section reference carries a display label
    /// that is hidden by the <c>~</c> (e.g. <c>~B "alt"</c>). The label text is
    /// kept in the source but not shown; a nudge that it is currently hidden.</summary>
    public const string HiddenSectionLabel = "LYS0012";

    /// <summary>Parser warning: a key gave no mode (<c>key bes</c>). The mode is
    /// assumed to be <c>major</c>; write it explicitly (<c>key bes major</c>) to be
    /// clear. A warning, not an error, so the piece still renders.</summary>
    public const string KeyModeAssumedMajor = "LYS0014";

    /// <summary>Parse error: a duration written on a chord/arpeggio member
    /// (<c>&lt;c e g2&gt;</c>). Members share one duration, written after the
    /// closing bracket (<c>&lt;c e g&gt;2</c>, <c>&lt;&lt; c e g &gt;&gt;2</c>).
    /// The adjacency rule tells it apart from a scale degree: a GLUED number
    /// (<c>g2</c>) is a duration, a spaced one (<c>g 2</c>) is a degree.</summary>
    public const string DurationInsideChord = "LYS0015";

    /// <summary>Semantic error: a bare duration with nothing before it to
    /// repeat. Until 2026-08-19 EVERY spaced number in music was this error;
    /// now that <c>bes8 8 8</c> is the repeat spelling (GRAMMAR §BareDuration),
    /// the code survives for the one shape that still has no meaning — a
    /// repeat with no note, chord or slash before it.</summary>
    public const string DetachedDuration = "LYS0016";

    /// <summary>Parse error: a declaration name (part/section/phrase/…) starts with
    /// a digit. Numbers are already durations (<c>c4</c>) and scale degrees
    /// (<c>&lt;1 3 5&gt;</c>) in Lily#, so a name must start with a letter.</summary>
    public const string NameStartsWithDigit = "LYS0017";

    /// <summary>Lexer error: an unexpected character was encountered.</summary>
    /// <remarks>
    /// ⚠️ Was LYS0014, which <see cref="KeyModeAssumedMajor"/> already held — this and the
    /// three above had been appended UNDER the LYS7xxx heading, where the next free number
    /// in this band is not in view. That is the whole mechanism of the collision, so they
    /// are back in the band they number. LYS0013 is retired — it, and the seven retired
    /// with it, are listed once on <see cref="DiagnosticCodes"/> itself.
    /// </remarks>
    public const string UnexpectedCharacter = "LYS0018";

    /// <summary>Parse error: MUSIC was written at the top level of a file (a note stream,
    /// a <c>{ … }</c> block, a grace/tuplet group, a <c>break</c>, or a <c>$phrase</c>
    /// reference). A file is a set of DECLARATIONS; notes belong to a part, reached through
    /// a section. The top-level <c>clef</c>/<c>key</c>/<c>time</c>/<c>tempo</c> directives
    /// stay — with no music beside them they are unambiguously the file defaults, which is
    /// the point: the same spelling no longer means "default" or "change" depending on
    /// whether a note happens to precede it.</summary>
    /// <remarks>
    /// The headerless note stream was never a documented form — <c>GRAMMAR.md</c>'s
    /// <c>TopLevelItem</c> has never listed music and <c>GRAMMAR_FOR_LLM.md</c>'s "minimal
    /// document" is the four-line structured one — it was only ever a permissive parse.
    /// It also skipped measure validation, so a miscounted bar in such a file said nothing.
    /// </remarks>
    public const string TopLevelMusic = "LYS0020";

    /// <summary>Parse error: a decimal number appeared in a music stream, where the
    /// only number that means anything is a duration — and a duration is whole
    /// (<c>c4</c>, <c>c8</c>), lengthened by dots (<c>c4.</c>), never fractional.</summary>
    /// <remarks>
    /// This exists because the alternative was SILENCE. Before the lexer had a decimal
    /// literal, <c>c4.5</c> lexed as <c>c</c> + <c>4</c> + <c>.</c> + <c>5</c> and read as
    /// a dotted quarter followed by a stray <c>5</c> that the music loop dropped without
    /// a word — the file said one thing and rendered another. Now the whole <c>4.5</c> is
    /// one token, and one token can be pointed at.
    /// </remarks>
    public const string FractionalDuration = "LYS0021";

    /// <summary>Parse error: a decimal appeared in a <c>tempo</c> value run, which has
    /// no fractional position — a metronome mark is a whole number of beats per minute
    /// and a beat unit is a note value (<c>tempo 4. = 116</c>).</summary>
    /// <remarks>
    /// Its own code rather than <see cref="FractionalDuration"/>: the decimal is inside
    /// a tempo, and the reader needs to be told about the tempo. Before the lexer had a
    /// decimal literal, <c>tempo 4.5 = 116</c> read its beat unit as a 5.
    /// </remarks>
    public const string FractionalTempoValue = "LYS0022";

    /// <summary>Parse error: a '.' in a music stream that belongs to nothing — no rule
    /// claimed it. It has TWO causes, both measured, and the message names both: an
    /// augmentation dot with no number in front of it (<c>c4 g.</c>), and the legacy
    /// dotted spelling of an annotation that now takes parentheses
    /// (<c>@finger.3</c> for <c>@finger(3)</c>).</summary>
    /// <remarks>
    /// <para>
    /// The duration half: LILYPOND-REF <c>lily/parser.yy</c> — <c>steno_duration</c> is
    /// <c>UNSIGNED dots</c> or <c>DURATION_IDENTIFIER dots</c>, so a bare '.' cannot
    /// begin one. MEASURED on 2.26.0: <c>\new Staff { c'4 g'. a'4 }</c> fails with
    /// <c>syntax error, unexpected '.'</c> and the file is refused outright.
    /// </para>
    /// <para>
    /// This exists because the alternative was SILENCE — and worse silence than
    /// <see cref="FractionalDuration"/>'s. The lone dot reached no rule, so the music
    /// loop's skip recovery dropped it from the TREE: <c>c4 g. a4</c> spelled itself
    /// back out as <c>c4 ga4</c>, which is not the same music, and every node after
    /// the dot stood one character early (the shape HANDOFF §1 第168 ⑴ measured for
    /// <c>@staccato.up</c>). The dot is therefore KEPT, and only its meaning is
    /// refused. No book writes either form: measured over the 80-book corpus and 219
    /// fixtures, first by spelling every book back out of its tree and then by counting
    /// this diagnostic across all 1,443 .lys files on disk — the 23 that raise it are
    /// stale <c>scratch/</c> and <c>output/</c> artefacts in the legacy dotted spelling,
    /// 12 of which the parser already refused for other reasons.
    /// </para>
    /// <para>
    /// ⚠️ Reading a bare dot as "inherit the number, add a dot" was considered and
    /// rejected: inheritance already carries the dots (<c>c4. g</c> is two dotted
    /// quarters, measured), so after a dotted note <c>g.</c> would have two readings —
    /// 4. and 4.. — with nothing to choose between them.
    /// </para>
    /// <para>
    /// ⚠️ The dots that DO belong to something never reach here: a placement qualifier
    /// (<c>@staccato.up</c>), a dotted navigation mark (<c>@ds.al.fine</c>) and a
    /// duration's own dots (<c>c4.</c>) are all consumed by the rule that owns them.
    /// </para>
    /// </remarks>
    public const string UnclaimedDot = "LYS0023";

    /// <summary>Warning: part of a <c>drummap { }</c> entry is ignored — an unknown drum
    /// name, an unknown setting, or a value outside its range or vocabulary.</summary>
    /// <remarks>
    /// <para>
    /// One code for the whole block, like <see cref="UnknownAnnotation"/>: what varies is
    /// which part was dropped, and that belongs in the message and the span rather than in
    /// four codes a reader would have to tell apart.
    /// </para>
    /// <para>
    /// ⚠️ This exists because the alternative was SILENCE, and total silence: a drummap in
    /// which the drum name, the setting key, the range and the value word were ALL wrong
    /// rendered byte-for-byte as if the block were absent and reported "No errors found"
    /// (measured 2026-08-15, data-pos aside). Nothing accepted or refused changed when this
    /// was added — see <c>DrummapValidator</c>, whose remarks also record the two shapes it
    /// still cannot see and why.
    /// </para>
    /// </remarks>
    public const string DrummapEntryIgnored = "LYS0024";

    /// <summary>Parser error: a token in a PART HEADER that the header cannot place — not a
    /// property name, a <c>key</c>, an inner <c>section</c>, or a grob directive. The header
    /// loop used to drop such a token silently (<c>else Advance()</c>), which is how
    /// <c>part m { bass }</c> came to mean exactly <c>part m { }</c> — MEASURED byte-identical
    /// output and "No errors found" — and a bare clef word reads so much like a clef that the
    /// silence was the whole trap. Distinct from <see cref="UnknownSymbolCase"/> (a name in
    /// property POSITION that is not a known property) and from
    /// <see cref="PartPropertyMissingValue"/> (a known name with no value).</summary>
    public const string PartHeaderStrayToken = "LYS0025";

    /// <summary>Parser error: a part-header property with no value (<c>part m { clef }</c>).
    /// The value used to be consumed unconditionally, so the property ate the closing brace
    /// and everything below was parsed INSIDE the part — surfacing as a complaint about a
    /// line far away (<c>Undefined variable or phrase: 'm'</c>), or, with another part after
    /// it, as <c>Unknown clef '}'</c>: the brace itself reported as a clef name. A value is
    /// no longer taken from a brace or from end-of-file.</summary>
    public const string PartPropertyMissingValue = "LYS0026";

    /// <summary>Parser error: a <c>\N</c> tab string number standing in the music sequence
    /// instead of on a note.</summary>
    /// <remarks>
    /// <para>
    /// A string number is a post-event: it names the string the note before it is played
    /// on. One that reaches the sequence belongs to nothing, and the music-item loop used
    /// to drop it — which on a tab staff is the worst kind of silence, because the page
    /// still shows a fret. The automatic chooser simply answers instead, and its answer is
    /// a real fret on a real string, so nothing looks wrong: <c>c( g')\2</c> printed the
    /// first string open where LilyPond prints the fifth fret of the second (measured on
    /// 2.26.0, 2026-08-16, on the twin of the reader's own file).
    /// </para>
    /// <para>
    /// ⚠️ That spelling is no longer a stray — a post-event may follow the note's slur, tie
    /// and beam marks, which is what LilyPond's unordered <c>post_events</c> means and what
    /// <c>Parser.IsPostEventStart</c> now says. This code is the net UNDER that fix: it
    /// catches a string number written where no note precedes it at all, and any shape that
    /// slips past the ordering rule next time.
    /// </para>
    /// <para>
    /// ⚠️ The token is KEPT in the tree (like <see cref="UnclaimedDot"/>): dropping it would
    /// move every following node one character early and break the round trip.
    /// </para>
    /// </remarks>
    public const string StrayStringNumber = "LYS0027";

    /// <summary>Warning: a <c>using "file.lys"</c> naming a file that cannot be read, or
    /// naming nothing at all (<c>using ""</c>). The directive contributes no text.</summary>
    /// <remarks>
    /// <para>
    /// This was the last spelling in the grammar that names something which may not exist
    /// and reported NOTHING when it did not. Measured 2026-08-16: a book whose only
    /// difference from its twin was <c>using "meta.lys"</c> misspelt as <c>using
    /// "metaa.lys"</c> passed <c>lysc check</c> as <c>No errors found.</c>, and its SVG —
    /// with <c>data-pos</c> masked, since the line's own length shifts those — was
    /// CHARACTER-IDENTICAL to the same book with the <c>using</c> line deleted. The title
    /// and tempo the included file carried were simply absent.
    /// </para>
    /// <para>
    /// ⚠️ The two halves of the failure are not equally visible, which is why the silent
    /// half needed a name. When the missing file declared something the score REFERENCES,
    /// the reader does get errors — but they point at the wrong lines: the misspelt include
    /// produced <c>Undefined section: 'A'</c> and <c>Undefined part: 'shared'</c> against
    /// the two lines that were CORRECT, and said nothing about the one line that was wrong.
    /// When it declared only unreferenced things (a title, a tempo, an override), there was
    /// no diagnostic at all.
    /// </para>
    /// <para>
    /// ⚠️ A WARNING, not an error, deliberately: <see cref="Parser.UsingExpander"/> declares
    /// that "a missing using never aborts the render", and the LSP preview resolves includes
    /// from disk on every keystroke — a sibling that is momentarily unsaved or not yet
    /// created must not blank the preview. The warning removes the silence without touching
    /// that contract, and <c>UsingTests.MissingFile_IsSkipped</c> stays green.
    /// </para>
    /// <para>
    /// ⚠️ A <c>using</c> whose file was ALREADY included (the diamond / cycle cases) is not
    /// this: it resolved, it is simply deduplicated, and it must stay quiet.
    /// </para>
    /// </remarks>
    public const string UsingFileUnreadable = "LYS0028";

    /// <summary>Parser error: a <c>using "file.lys"</c> written anywhere but the top level —
    /// inside a section, a score, a form, a part header or a music block. It includes a whole
    /// FILE, so only the file level can hold one; nested, it can never do anything.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ The silence was only half of it, and the louder half was the one nobody looked at.
    /// The tokens were consumed with a bare <c>Advance()</c>, which drops their WIDTH from the
    /// green tree — and a node's position is the running sum of the green widths before it, so
    /// EVERY source position after the directive slid left by the length of what was dropped.
    /// Measured 2026-08-16 on <c>section A { using "n.lys" &lt;notes&gt; }</c>: the tree spelled
    /// itself back 16 characters short, every <c>data-pos</c> in the SVG pointed 16 characters
    /// early (52/55/57/59/61 — the offsets the same book has with the line DELETED — against
    /// notes truly at 68/71/73/75/77), and <c>check --pitches</c> named the <c>using</c> line
    /// itself as the music, reporting <c>g</c>, <c>n</c>, <c>lys</c>, <c>s</c> where the file
    /// says <c>c d e f</c>. That report is the instrument RULES §5.3 判定法⑶ tells every
    /// session to run over a synthesized book before filing anything.
    /// </para>
    /// <para>
    /// ⚠️ FIVE spellings, and the one that already spoke was assumed to be fine. A part header
    /// reported <see cref="PartHeaderStrayToken"/> — and dropped the width anyway, so the noisy
    /// case corrupted positions exactly like the four silent ones (section body 16, score body
    /// 14, form body 14, part header 14, music block 14). Reporting and keeping are different
    /// repairs; this code does both, and folds the part header's cascading second error (about
    /// the quoted path) into one message.
    /// </para>
    /// <para>
    /// ⚠️ An ERROR, not a warning like <see cref="UsingFileUnreadable"/>. That one is a warning
    /// because the skip is DECLARED design (<c>UsingExpander</c>: "a missing using never aborts
    /// the render", pinned by <c>UsingTests.MissingFile_IsSkipped</c>) — a file that is briefly
    /// unsaved must not blank the preview. No such contract covers a misplaced one: no state of
    /// the file system can make it mean anything.
    /// </para>
    /// <para>
    /// ⚠️ <c>UsingExpander.HasUsings</c> still reads the ROOT'S CHILDREN ONLY, and must: it is
    /// asked on every keystroke, and the directives it looks for are the ones
    /// <c>UsingExpander.FindUsings</c> resolves — also root children. A nested directive now
    /// exists as a node but is never expanded, which is what this error says. The invariant
    /// that keeps the cheap spelling honest is stated as a test:
    /// <c>UsingTests.EveryUsingHasUsingsSkips_IsReported</c>.
    /// </para>
    /// </remarks>
    public const string UsingMustBeTopLevel = "LYS0029";

    /// <summary>Parser error: a token that the container it stands in has no item rule for —
    /// something written directly inside a <c>section</c> body, a <c>form</c> body, a
    /// <c>score</c> body or a music block that is none of the things that container holds.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Until 2026-08-16 all four containers consumed such a token with a bare
    /// <c>Advance()</c> and said NOTHING. Measured on four probe books that differ from a
    /// control only by an inserted <c>"oops"</c> (7 characters): <c>lysc check</c> answered
    /// <c>No errors found.</c> for every one, and all five SVGs — <c>data-pos</c> INCLUDED —
    /// were byte-identical to the control. That equality is the defect stated twice: the
    /// token contributed nothing, and every source offset after it pointed 7 characters
    /// early, at the offsets the book has with the token DELETED.
    /// </para>
    /// <para>
    /// ⚠️ The width is the half that reaches a reader who never mistypes. A node's position
    /// is the running sum of the green widths before it, so a dropped token slides
    /// <c>data-pos</c>, the LSP's jump targets, <c>check --pitches</c>' line numbers and the
    /// editor's write-back. It also corrupts OTHER diagnostics: measured on
    /// <c>form main { A section B }</c>, the (correct) <c>Undefined section: 'B'</c> was
    /// reported at column 15 — on the dropped <c>section</c> keyword — where <c>B</c> stands
    /// at column 23.
    /// </para>
    /// <para>
    /// ⚠️ Reported AND kept, like <see cref="UnclaimedDot"/>,
    /// <see cref="StrayStringNumber"/> and <see cref="ChordBlockBadMember"/>: the token
    /// stands in the item list contributing its width and nothing else. Every consumer of
    /// these item lists selects POSITIVELY by node type (counted 2026-08-16 across the 35
    /// files that name the three declaration nodes), and all three containers already hold
    /// token children — the keyword, the name and the braces — so one more token child is
    /// structurally indistinguishable from what they already skip.
    /// </para>
    /// <para>
    /// ⚠️ An ERROR, not a warning (user decision, 2026-08-16), on the same ground as
    /// <see cref="UsingMustBeTopLevel"/>: <see cref="UsingFileUnreadable"/> is a warning
    /// because its silence was DECLARED design and pinned by a test, and no such contract
    /// covers a token that no rule of the language can place.
    /// </para>
    /// </remarks>
    public const string StrayItemToken = "LYS0030";

    /// <summary>Syntax error: a nameless <c>chords { … }</c> block. Name the progression
    /// and place it: <c>chords prog { … }</c> in the section, <c>chords prog</c> above the
    /// staff in the score.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THIS CODE EARNS ITS PLACE IN THE PRESENT TENSE, not as a migration path. Two
    /// reasons, either one sufficient. First, <c>chords</c> is a live keyword, so
    /// <c>chords {</c> is a position the parser must answer for whatever the history —
    /// unlike the retired <c>with</c>-clause error and LYS8007, which became unreachable
    /// when their keywords stopped being keywords and were retired for it. Second, and the
    /// reason that would hold even then: <c>voice { … }</c> legitimately takes no name, so
    /// a writer who has never seen an older Lily# reaches for <c>chords { … }</c> by
    /// analogy. It is a mistake the language invites, not a spelling it used to have.
    /// </para>
    /// <para>
    /// The history, kept as history: the nameless form associated by CO-WRITING — "the part
    /// in the same section" — which was written nowhere and stopped being well-defined the
    /// moment a section held two parts (the implementation hard-coded staff 0). Removed
    /// before the first tag, user decision, 2026-08-19.
    /// </para>
    /// <para>
    /// ⚠️ The summary above was ONE sentence beginning "Removed before the first tag", and
    /// that framing invited its own retirement: read against the never-released rule that
    /// killed LYS0031 and LYS8007 on the same day, a code justified by a removal looks like
    /// a code for nobody. Rewritten 2026-08-21. <see cref="RepeatedVoiceKeyword"/> is the
    /// pattern to copy — it argues from what the spelling does today, not from what it used
    /// to mean.
    /// </para>
    /// </remarks>
    public const string NamelessChordsRemoved = "LYS0032";

    // Semantic errors (LYS1xxx)

    /// <summary>Semantic error: reference to an undefined variable.</summary>
    public const string UndefinedVariable = "LYS1001";
    /// <summary>Semantic error: an invalid duration was specified.</summary>
    public const string InvalidDuration = "LYS1004";
    /// <summary>Semantic error: reference to an undefined section.</summary>
    public const string UndefinedSection = "LYS1005";
    /// <summary>Semantic error: a phrase/variable reference cycle — a phrase references
    /// itself, directly or through a chain (x -> y -> x, or x -> y -> z -> x). It can
    /// never expand to a finite piece, so it is reported rather than silently truncated.</summary>
    public const string PhraseReferenceCycle = "LYS1027";
    /// <summary>Syntax error: a <c>chords { … }</c> body holds something that is neither a
    /// chord entry nor a barline — most often a chord symbol written the way it PRINTS
    /// (<c>C</c>), where a root is written lowercase (GRAMMAR §ChordEntry: <c>c</c>=C).</summary>
    /// <remarks>
    /// ⚠️ Reported at all only since 2026-08-11. The token used to be skipped in silence,
    /// which also dropped its WIDTH, shifting every source offset after it — see
    /// Parser.Sections.SkipStrayChordToken for what that cost.
    /// </remarks>
    public const string ChordBlockBadMember = "LYS1028";

    /// <summary>Semantic error: reference to an undefined part.</summary>
    public const string UndefinedPart = "LYS1007";
    /// <summary>Semantic error: an unknown annotation was used.</summary>
    public const string UnknownAnnotation = "LYS1008";
    /// <summary>Semantic error: a rehearsal mark label was not quoted
    /// (<c>@mark(A)</c> instead of <c>@mark("A")</c>).</summary>
    public const string MarkLabelNotQuoted = "LYS1009";
    /// <summary>Semantic error: a <c>form</c> was declared without a name.</summary>
    public const string UnnamedForm = "LYS1016";
    /// <summary>Semantic error: two forms share the same name.</summary>
    public const string DuplicateFormName = "LYS1017";
    /// <summary>Semantic error: a <c>score</c> references a form that is missing or undeclared.</summary>
    public const string UnknownFormReference = "LYS1018";
    /// <summary>Semantic error: invalid barline placement for a volta repeat.</summary>
    public const string VoltaRepeatBarlinePlacement = "LYS1010";

    /// <summary>Syntax error: a volta ending must be bracketed — <c>[N. Section]</c>.</summary>
    public const string VoltaBracketRequired = "LYS1011";

    /// <summary>Syntax error: a phrase reference needs a <c>$</c> — write <c>$name</c>.</summary>
    public const string BareReferenceRequiresDollar = "LYS1012";

    /// <summary>Syntax error: a navigation mark was written with <c>@</c>
    /// (<c>@segno</c>, <c>@ds.al.coda</c>). Navigation marks are bare — <c>@</c>
    /// modifies a note, and a segno/coda/D.S. is a standalone landmark, not a note.</summary>
    public const string NavigationMarkIsBare = "LYS1022";

    /// <summary>Semantic error: a <c>revert</c> or <c>once</c> was written outside a music
    /// stream (at the top level or in a <c>part {}</c> header). Both are positional — they
    /// act from a point in the music forward — so they only make sense where notes flow; a
    /// structural context has no such position. Set a default with a plain <c>override</c>
    /// there instead, and revert inside a section/voice.</summary>
    public const string RevertOutsideMusic = "LYS1023";

    /// <summary>Semantic error: a <c>partial</c> (pickup) was written outside a section — at
    /// the top level, in a <c>part {}</c> header, or nested inside a part block/voice. A pickup
    /// shortens the opening bar for EVERY part of a section at once, so it belongs to the section,
    /// not the piece and not one voice. Write it as a section directive: <c>section A { partial 4
    /// … }</c>. (Bare music with no sections is a plain note stream, so a <c>partial</c> there is
    /// fine.)</summary>
    public const string PartialOutsideSection = "LYS1024";

    /// <summary>Semantic: in a PART-MAJOR file, a top-level <c>section</c> holds section-wide
    /// directives and cells for DECLARED parts only. Loose music there (<c>section A { c d e }</c>)
    /// belongs to no part — the parser reads the first pitch as a part-cell name, so it shows up
    /// as a cell naming an undeclared part. Put the music inside a part instead.</summary>
    public const string SectionMusicNeedsPart = "LYS1025";

    /// <summary>Semantic: <c>tempo</c> / <c>time</c> written as a PART header property
    /// (<c>part melody { tempo 120 … }</c>). These are score-level — every part shares one
    /// tempo and meter — so they cannot belong to a single part. Put them at the top level (the
    /// piece's opening value) or in a section header (a mid-piece change that applies to every
    /// part).</summary>
    public const string ScoreSettingInPartHeader = "LYS1026";

    /// <summary>Semantic: an <c>override</c> / <c>revert</c> names a grob property the engine
    /// does not read (<c>override Beam.thickness = 9</c>), or mis-cases one it does
    /// (<c>stem.direction</c>). The grammar accepts any <c>Grob.property</c>, so such a line
    /// used to engrave byte-for-byte identically to writing nothing, silently. The supported
    /// vocabulary is <see cref="Svg.Model.SupportedGrobOverrides"/> — a list of what is
    /// IMPLEMENTED, which grows; each addition turns this diagnostic off for one more
    /// spelling, so a file rejected today compiles unchanged the day its property lands.</summary>
    public const string OverridePropertyUnsupported = "LYS1029";

    /// <summary>Semantic: a <c>phrase</c> is declared under a name a bare reference in a
    /// music stream cannot reach, so the phrase could never be played. The music-item
    /// dispatch claims some bare words before they can be a reference: <c>q</c> repeats the
    /// previous chord and the drum vocabulary (<c>sn</c>, <c>bd</c>, <c>hh</c>, …) becomes a
    /// drum note — on ANY part, not just a percussion one. The declaration side accepts
    /// both (a drum name is an ordinary identifier to <c>ExpectPartName</c>), which is how
    /// the name gets written in the first place.
    /// <para>The four clef words are deliberately NOT here. They were unreachable for the
    /// same reason until 2026-08-22, but nothing claims a bare clef word in a music stream,
    /// so the fix there was to make the stream read it as a reference rather than to refuse
    /// the name (Parser.Music.cs). <c>q</c> and the drum vocabulary are real music items, so
    /// no such fix exists for them and the declaration is the only place left to speak.</para>
    /// <para>⚠️ This is reported at the DECLARATION, not the reference, on purpose: the
    /// reference does not fail loudly. Measured 2026-08-22 — <c>phrase sn { c4 d e f | }</c>
    /// played as bare <c>sn</c> turns the whole staff into a <c>DrumStaff</c> holding one
    /// note and emits nothing but a short-measure warning. Naming it here is the only place
    /// the writer is told before the picture changes under them.</para></summary>
    public const string PhraseNameUnreachable = "LYS1030";

    /// <summary>Semantic warning: a bare duration's repeat reaches back across a
    /// barline — the event it repeats is in an earlier measure. Within a measure
    /// the spelling is the idiom (<c>bes8 8 8 8</c>); a measure that OPENS on a
    /// bare number is also exactly what a dropped pitch letter looks like
    /// (<c>4 g f e</c> meant as <c>a4 g f e</c>) — the one silent misreading the
    /// spelling bought, accepted knowingly in HANDOFF §3 with this diagnostic as
    /// the agreed receiver (GRAMMAR_AUDIT §3.3). A warning, not an error: the
    /// repeat is legal and renders; writing the event itself silences it. A chain
    /// after the crossing (<c>… | 4 4 4</c>) is anchored by its first bare
    /// duration, so the crossing is reported once.</summary>
    public const string BareDurationAcrossBarline = "LYS1031";

    /// <summary>Syntax error: a metadata value (title/composer) must be a quoted string.</summary>
    public const string MetadataValueMustBeQuoted = "LYS1013";

    /// <summary>Semantic error: an unknown or wrong-case symbol (property name or a
    /// clef/instrument/tuning value); symbols are case-sensitive.</summary>
    public const string UnknownSymbolCase = "LYS1014";

    /// <summary>Syntax error: a DEGREE-anchored chord (opening with a number, so it
    /// measures from the key tonic and moves with the key) holds a named pitch —
    /// which would not move, half-transposing the chord. Anchor it on a pitch
    /// (<c>&lt;c 3 g&gt;</c> — a letter-anchored chord mixes freely) or write degrees
    /// only (<c>&lt;1 3 5&gt;</c>).</summary>
    public const string ChordMixesPitchesAndDegrees = "LYS1019";

    /// <summary>Warning: a bare <c>@chord</c> can't name its chord — the notes match
    /// no known chord quality. Use the explicit form <c>@chord(Cmaj7)</c>.</summary>
    public const string ChordNotRecognized = "LYS1020";

    /// <summary>Syntax error: a scale degree is 1-based (1 = root/unison), so
    /// <c>&lt;0 …&gt;</c> is invalid.</summary>
    public const string InvalidScaleDegree = "LYS1021";

    // Measure errors (LYS2xxx)

    /// <summary>Measure error: a measure has fewer beats than the time signature requires.</summary>
    public const string MeasureIncomplete = "LYS2001";
    /// <summary>Measure error: a measure has more beats than the time signature allows.</summary>
    public const string MeasureOverflow = "LYS2002";
    /// <summary>Measure error: a measure's total duration does not match the time signature.</summary>
    public const string MeasureDurationMismatch = "LYS2004";
    /// <summary>Measure error: conflicting time signatures were declared.</summary>
    public const string ConflictingTimeSignatures = "LYS2005";
    /// <summary>Measure warning: the same section spans a different number of bars in
    /// different parts, so the shorter parts are padded to align (often a miscount).</summary>
    public const string SectionBarCountMismatch = "LYS2007";
    // LYS2008 (EmptyPlaceholderMeasure) was retired: an empty `| |` measure now
    // reports the ordinary underfull warning (LYS2001, MeasureIncomplete) over the
    // region between the barlines — zero duration is just the extreme underfull case.

    /// <summary>Measure warning: a chord-row bar's slot count does not fit the
    /// meter's beat grid. Entries and <c>.</c> extensions are measure-relative
    /// (GRAMMAR_AUDIT 8.1): one slot takes the whole bar, a beat-count multiple
    /// subdivides each beat, a divisor groups whole beats — anything else (three
    /// slots in 4/4, say) matches no beat and the row falls back to dividing the
    /// bar equally, which this warning names (write <c>.</c> to reach a beat
    /// count: <c>| C F G |</c> → <c>| C F G . |</c>).</summary>
    public const string ChordSlotMismatch = "LYS2009";

    /// <summary>Measure error: a <c>.</c> at the head of a chord-row bar has no
    /// entry before it in that bar to extend — a <c>.</c> never crosses a barline
    /// (write <c>| C | C |</c>, not <c>| C | . |</c>). The slot still counts, so
    /// the bar's grid stays honest; the slot itself prints nothing.</summary>
    public const string ChordExtendAtBarHead = "LYS2010";

    // Lyric diagnostics (LYS4xxx — warnings, plus one error)

    /// <summary>Lyric warning: more lyric syllables than available notes.</summary>
    public const string LyricSyllableOverflow = "LYS4001";
    /// <summary>Lyric error: a top-level lyrics track in a part-major file is written
    /// flat; it must group its verses by section (<c>lyrics { section A { … } }</c>).</summary>
    public const string LyricTrackNeedsSections = "LYS4002";
    /// <summary>Warning: a navigation mark (segno/coda/D.S./…) sits mid-measure rather
    /// than at a barline boundary.</summary>
    public const string NavigationMarkMidMeasure = "LYS4003";
    /// <summary>Lyric warning: a section's plain (unbracketed) verse is fully shadowed
    /// by its <c>[N. …]</c> verses — every written-out occurrence already has a numbered
    /// verse, so the plain line (a fallback for uncovered occurrences) never renders.</summary>
    public const string LyricPlainVerseShadowed = "LYS4004";

    /// <summary>Warning: a top-level single-value global setting (tempo / time / key /
    /// octave / title / composer / font) is written more than once. Only the LAST
    /// occurrence takes effect; each earlier one is silently overwritten.</summary>
    public const string DuplicateGlobalSetting = "LYS4005";
    // LYS4006 (LyricUnattached) retired: a top-level lyrics block that no score
    // references is silently ignored rather than flagged — an unused/unnamed lyrics
    // block is not a diagnosable error. The code number is left unreused.
    /// <summary>Warning: the item after a tie (<c>~</c>) does not repeat the tied
    /// pitch (a different note, a chord with no matching pitch, or a rest). A tie
    /// joins two notes of the SAME pitch; different pitches connect with a slur.</summary>
    public const string TieTargetMismatch = "LYS4007";
    /// <summary>Warning: a <c>!</c> written GLUED to a note (<c>cis!</c>). <c>!</c> is
    /// the dashed barline (LilyPond's <c>\bar "!"</c>), so it ends the measure there —
    /// but someone arriving from LilyPond writes it meaning a forced accidental and
    /// gets a bar-length complaint that never mentions the <c>!</c>. The behavior is
    /// unchanged (it stays a barline); this only names what happened. Written with a
    /// space before it, the barline is unambiguous and nothing is reported.</summary>
    public const string DashedBarGluedToNote = "LYS4009";
    /// <summary>Warning: a slur mark that pairs with nothing — a <c>(</c> that is never
    /// closed, or a <c>)</c> with no <c>(</c> open. Either way no slur is drawn, and
    /// nothing else says so. A slur's marks also do not cross a voice boundary
    /// (LilyPond's Slur_engraver lives in the Voice context), so one left open when the
    /// voice ends is unpaired too.</summary>
    public const string UnpairedSlur = "LYS4010";

    /// <summary>Music warning: a manual beam bracket that pairs with nothing — a <c>[</c>
    /// never closed, or a <c>]</c> with none open. Unlike an unpaired slur, the score is not
    /// left bare: BeamDetector discards the bracket and the notes fall back to AUTOMATIC
    /// beaming, so the engraved grouping is simply not the written one. MEASURED on a bar
    /// where the two differ (<c>c8[ d8 e8 f8 g8] a8 b8 c8</c> in 4/4 — five beamed, which
    /// automatic beaming never produces): dropping either bracket makes the output
    /// byte-identical to the same notes with no bracket at all.</summary>
    public const string UnpairedBeam = "LYS4016";

    /// <summary>Error: a <c>|:</c> that no <c>:|</c> ever closes — a repeat whose end
    /// nobody wrote. The four walks disagree about what that means and always have
    /// (MEASURED 2026-08-15 on a lone <c>|:</c> in section music: the page draws a
    /// start-repeat bar; MIDI is byte-identical to no repeat at all; the LilyPond twin wraps
    /// everything to the end of the stream in <c>\repeat volta 2</c> and so plays it twice),
    /// and nothing said so. The OTHER half is not an error: a <c>:|</c> with no <c>|:</c>
    /// open means "repeat from the beginning of the piece", which is the ordinary reading of
    /// the sign.
    /// <para>
    /// ⚠️ Only decidable AFTER score expansion, which is why it is raised from the collected
    /// measures and not from the text: a section is not a piece of music on its own, so a
    /// <c>|:</c> written in a section may be closed by a <c>:|</c> the <c>form</c> writes.
    /// Books in the wild are spelled that way.
    /// </para></summary>
    public const string UnpairedRepeat = "LYS4017";
    /// <summary>Warning: a span that opens exactly ONE unnamed <c>voice { … }</c>. The
    /// block is then entirely transparent — stem forcing needs a second voice, so the
    /// music engraves as if the braces were not there. Someone who wrote it meaning
    /// "polyphonic from here" gets a single-voice score with nothing said. A NAMED lone
    /// voice is exempt: its name is what a <c>lyrics NAME { … }</c> block binds to.</summary>
    public const string LoneVoiceBlock = "LYS4011";
    /// <summary>Error: a slur or a tie with ONE end inside a <c>cue { … }</c> and the other
    /// outside it. LilyPond cannot spell this at all — a cue is a Voice context of its own, and
    /// both the Slur_engraver and the Tie_engraver live in the Voice — so the span it engraves
    /// is not the one that was written. MEASURED on LilyPond 2.26.0 against the four spellings
    /// (probe scratch/cue-span-probe): a slur crossing either way is dropped with
    /// "cannot end slur" / "unterminated slur"; a tie INTO a cue is dropped with
    /// "unterminated tie"; and a tie OUT of a cue is dropped WITHOUT A WORD — that book
    /// engraves byte-for-byte as the same bar with no tie written at all.
    /// <para>
    /// ⚠️ An error rather than a warning because Lily# is pre-release and this is the only
    /// direction that closes later: a spelling accepted today cannot be rejected after books
    /// exist, while `error → warning → drawn` costs nobody anything. Lily# DRAWS the curve
    /// (the renderer pairs across the boundary), so this is not a report of ink that went
    /// missing — it is a report of ink LilyPond will never make.
    /// </para></summary>
    public const string SpanCrossesCueBoundary = "LYS4012";
    /// <summary>Error: a <c>cue { … }</c> inside another <c>cue { … }</c>. LilyPond's cue is
    /// a CONTEXT with one <c>fontSize</c>, so a second one nested inside the first says
    /// nothing the outer one has not already said. Forbidden while the shape is young —
    /// opening it later is cheap, un-opening it is not.</summary>
    public const string NestedCueBlock = "LYS4013";
    /// <summary>Warning: a <c>q</c> chord repetition with no chord before it in its
    /// body — there is nothing to repeat, so it occupies its time silently.
    /// LILYPOND-REF: scm/music-functions.scm:941 expand-repeat-chords! — warning "Bad chord repetition".</summary>
    public const string BadChordRepetition = "LYS4015";
    /// <summary>Error: a <c>voice { … } voice { … }</c> span inside a <c>cue { … }</c>.
    /// LilyPond can spell it, but the meaning doubles up — a cue is already a voice of its
    /// own — and nothing in Lily# decides which of the two the polyphony forcing belongs to.
    /// Forbidden until there is a book that needs it.</summary>
    public const string VoiceInsideCue = "LYS4014";
    /// <summary>Warning: an annotation on a broken-chord group
    /// (<c>&lt;&lt; … &gt;&gt;@staccato</c>) or on one of its bare pitch members is
    /// not applied — only a dynamic (<c>@f</c>) and a chord name (<c>@chord</c>)
    /// work on the group so far. Surfaced so nothing is dropped in silence.</summary>
    public const string ArpeggioAnnotationUnsupported = "LYS4008";
    /// <summary>An underfull FIRST measure with no `partial` declaration - a
    /// bare anacrusis is indistinguishable from a miscount, so nudge toward
    /// declaring it (which also numbers it as bar 0).</summary>
    public const string PickupWithoutPartial = "LYS2006";

    // Tablature warnings (LYS5xxx)

    /// <summary>Tablature warning: a tie conflicts with the assigned string.</summary>
    public const string TabTieStringConflict = "LYS5001";
    /// <summary>Tablature warning: a fret position is out of range for the instrument.</summary>
    public const string TabOutOfRange = "LYS5002";

    // Render/score declaration errors (LYS6xxx)

    /// <summary>Render error: a score name was declared more than once.</summary>
    public const string DuplicateScoreName = "LYS6001";

    /// <summary>Render error: a <c>score</c> block holds no render item (no
    /// <c>staff</c> / <c>grandStaff</c> / <c>tab</c> / <c>ossia</c> / <c>chords</c> /
    /// <c>lyrics</c> row), so it engraves a page with no music on it. Reported
    /// rather than shipped, because an empty page looks like a layout failure.</summary>
    public const string EmptyScore = "LYS6002";

    /// <summary>Render error: a <c>condensedStaff { … }</c> names fewer than two parts.
    /// Condensing one part onto one staff is what <c>staff NAME</c> already is.</summary>
    /// <remarks>
    /// Its own code rather than a reuse of <see cref="EmptyScore"/>: <c>grandStaff</c> drops
    /// an under-filled group silently and lets the score report "its body declares no staff"
    /// about a body that plainly declares one, which names neither the container nor the
    /// rule it broke.
    /// </remarks>
    public const string CondensedStaffNeedsTwoParts = "LYS6003";

    /// <summary>Render error: a <c>condensedStaff { … }</c> contains something other than a
    /// part name — a staff group, or another condensed staff. Everything inside it becomes a
    /// VOICE of the one staff that comes out, and a bracketed group of staves is not a
    /// voice.</summary>
    public const string CondensedStaffBadMember = "LYS6004";

    /// <summary>Render error: a <c>combinedStaff { … }</c> does not name exactly two parts.
    /// Its own code rather than a reuse of <see cref="CondensedStaffNeedsTwoParts"/>: the
    /// rules differ (two or more there, exactly two here) and so does the way out.</summary>
    /// <remarks>
    /// Two is not an arbitrary limit. Combining is defined pairwise — at each moment the
    /// analysis asks what THESE TWO parts are doing relative to each other — and every one
    /// of its answers ("a2", "Solo", "Solo II") names one of two parts.
    /// LILYPOND-REF: scm/part-combiner.scm:339-342 determine-split-list — two event lists.
    /// </remarks>
    public const string CombinedStaffNeedsTwoParts = "LYS6005";

    /// <summary>Render error: a <c>combinedStaff { … }</c> contains something other than a
    /// part name.</summary>
    public const string CombinedStaffBadMember = "LYS6006";

    /// <summary>Render error: a <c>form</c> names no section, so the piece it arranges has
    /// nothing in it. The same failure as <see cref="EmptyScore"/> reached through the OTHER
    /// container: a book engraves what its form sequences, and a form that sequences nothing
    /// engraves nothing.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ The outcome is WORSE than LYS6002's blank page, which is why it needed its own
    /// arm rather than being left to the score check. MEASURED 2026-08-16: with no section
    /// reference anywhere in the form, <c>SvgDocumentContext.Assemble</c> takes its
    /// <c>_pages.Count == 0</c> arm and returns the empty string, so <c>lysc svg</c> writes a
    /// ZERO-BYTE file while printing "Created: … Font embedded: Yes" and exiting 0, and
    /// <c>lysc check</c> said "No errors found."
    /// </para>
    /// <para>
    /// ⚠️ THE REACH, subtracted rather than guessed (HANDOFF §5.0 — "wider than it was" is not
    /// "all of them"). Every form item GRAMMAR §StructureItem allows was enumerated and put
    /// through the engraver, alone and beside a real reference: <b>46 shapes, 16 of which
    /// engrave zero bytes, 15 of them caught here</b> (an empty body; a body holding only
    /// barlines <c>| || |. ! :|</c>, only navigation marks <c>segno fine coda dc ds</c>, only
    /// <c>break</c>/<c>nobreak</c>, only <c>@mark("X")</c>, or only <c>_"text"</c>).
    /// <b>The sixteenth shape — <c>form main { [1. A] }</c>, a volta ending that no repeat
    /// opens — is no longer one of them.</b> It NAMES a section, so this check was right to
    /// stay quiet; what was wrong was the ENGRAVER dropping it, and that half is now fixed
    /// (the ending engraves as its plain section, so the body is no longer zero bytes).
    /// The surviving half of that claim is <see cref="VoltaEndingWithoutRepeat"/>.
    /// </para>
    /// <para>
    /// Its own code rather than a reuse of <see cref="EmptyScore"/>, following the reasoning
    /// recorded on <see cref="CondensedStaffNeedsTwoParts"/>: LYS6002's message says "its body
    /// declares no staff", which names neither this container nor the way out. A form is not
    /// fixed by adding a staff — it is fixed by naming a section.
    /// </para>
    /// <para>
    /// ⚠️ "Names a section" counts ALL THREE spellings of a form's section reference — plain
    /// <c>A</c>, silent <c>~A</c>, and a volta alternative <c>[1. A]</c> — by asking
    /// <c>SectionReferenceFinder</c>, which is where that list already lives. A fourth copy of
    /// it here would drift the day a spelling is added (HANDOFF §5.2.1②).
    /// </para>
    /// </remarks>
    public const string EmptyForm = "LYS6007";

    /// <summary>Render WARNING: a volta ending that no repeat block opened —
    /// <c>form main { A [1. B] }</c>. It engraves as its plain section, so the number the
    /// author wrote prints nothing at all.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A WARNING, not an error, and the two halves of that decision come from different
    /// places. What it DOES is LilyPond's, and it is in LP's source, not merely in its
    /// output:
    /// LILYPOND-REF: lily/alternative-sequence-iterator.cc:83-84 — Alternative_sequence_iterator::analyze defaults repeat-count to 1
    /// when no enclosing repeat has set it, so an alternative outside a repeat plays exactly
    /// once and nothing spans a second pass. CONFIRMED on 2.26.0 (2026-08-16):
    /// <c>\alternative { \volta 1 { … } }</c> with no <c>\repeat volta</c> in front of it
    /// renders BYTE-IDENTICALLY to writing the music plainly — no bracket, no number — while
    /// the same book with the <c>\repeat</c> restored hashes differently.
    /// LP itself says nothing; breaking that silence is the Lily# half (user decision,
    /// 2026-08-16), on the same reasoning as <see cref="UsingFileUnreadable"/>: a construct
    /// that draws nothing is worth a word even when the output is right.
    /// </para>
    /// <para>
    /// ⚠️ Measure the shape you are actually asking about. An alternative written with no
    /// volta NUMBER (<c>\alternative { { … } }</c>) does draw a warning out of LP — "missing
    /// volta specification on alternative element" — but that is about the missing number,
    /// not the missing repeat. Reading only that shape gives the opposite answer to the
    /// question this code exists for.
    /// </para>
    /// <para>
    /// ⚠️ THE PREDICATE is the engraver's — a <c>FormAlternativeSyntax</c> with no
    /// <c>FormRepeatBlockSyntax</c> ancestor — and NOT "the form has no repeat block", which
    /// is what the ticket proposed. The weaker rule misses <c>|: A :| B [1. B]</c>, which has
    /// a repeat block and whose ending was dropped just the same, because the ending is a
    /// child of the FORM (measured on the tree: ink and MIDI byte-identical to
    /// <c>|: A :| B B</c>). The legitimate <c>|: A [1. D] :| [2. O]</c> is never accused:
    /// the ending after the <c>:|</c> fills ParseFormRepeatBlock's finalAlternative slot, so
    /// BOTH endings are children of the repeat block (measured, not assumed).
    /// </para>
    /// <para>
    /// ⚠️ Its own code rather than a reuse of <see cref="EmptyForm"/>: LYS6007 says the form
    /// names no section, and this form does name one. It is also not the same severity — the
    /// page LYS6007 describes does not exist, while this one is correct and merely less than
    /// what the author appears to have asked for.
    /// </para>
    /// <para>
    /// Written by <b>0 of 1025</b> books in the tree when this was added (counted by walking
    /// every <c>.lys</c> and applying the predicate above; the same walk found 1019 form
    /// declarations and 16 alternatives, and a positive control of 4 hand-written books was
    /// picked up 4/4 — so the zero is a zero and not a blind instrument).
    /// </para>
    /// </remarks>
    public const string VoltaEndingWithoutRepeat = "LYS6008";

    // LYS6009 (LyricsAttachmentUnbound) and LYS6010 (LyricsAttachmentWrongStaff)
    // are RETIRED — see the class remarks' retired-numbers list.

    /// <summary>Render error: a staff group (<c>grandStaff</c> / <c>staffGroup</c> /
    /// <c>choirStaff</c>) contains something other than a <c>staff</c> item or a
    /// <c>lyrics NAME</c> row. Reported at the member rather than left to the brace
    /// mismatch, for the reason recorded on <see cref="CondensedStaffBadMember"/>:
    /// "Expected 'CloseBrace'" describes the parser's predicament, not the writer's
    /// mistake.</summary>
    public const string StaffGroupBadMember = "LYS6011";

    /// <summary>Render error: a <c>lyrics NAME</c> row inside a staff group that does
    /// not sing the staff directly above it. Inside a group the row IS the staff
    /// above's attached verse (score = a vertical stack of bands, inside the braces as
    /// outside); a row belonging to no adjacent staff has no place a group can give
    /// it.</summary>
    public const string GroupRowNotBoundToStaffAbove = "LYS6012";

    // Structure / section-part grid errors (LYS7xxx)

    /// <summary>Structure error: a section-part grid cell was declared more than once.</summary>
    public const string DuplicateCell = "LYS7001";
    /// <summary>Structure error: a chords/lyrics track names the same section twice.</summary>
    public const string DuplicateTrackSection = "LYS7002";

    /// <summary>Structure error: one part header sets the same property twice
    /// (<c>part m { clef bass clef treble }</c>). Each property holds ONE value, and which
    /// of the two won was not even consistent between properties — MEASURED:
    /// <c>clef bass clef treble</c> engraved as treble (the LAST), while
    /// <c>lines 5 lines 3</c> engraved as five lines (the FIRST, byte-identical to
    /// <c>lines 5</c> alone). Rather than freeze either accident as the rule, the
    /// duplicate is refused: no book on disk writes one, so nothing has to choose.</summary>
    public const string DuplicatePartProperty = "LYS7003";

    /// <summary>Track error: <c>lyrics N sings T</c> where T names no declared part.</summary>
    public const string SingsTargetUnknown = "LYS7004";

    /// <summary>Track error: two blocks of the same lyrics track name different
    /// <c>sings</c> targets — the binding is a property of the TRACK, stated once
    /// (later same-name blocks may repeat it identically or omit it).</summary>
    public const string SingsConflict = "LYS7005";

    // Font warnings (LYS8xxx)
    // (LYS6xxx is already taken by the render/score-declaration band above.)

    /// <summary>Font warning: a font requested for embedding is under an unverified
    /// license (gray) — only clearly-free fonts (OFL/Apache/…) are auto-cleared.</summary>
    public const string FontEmbedLicenseUnclear = "LYS8001";
    /// <summary>Font warning: a font requested for embedding has a restricted fsType
    /// that forbids embedding, so it will not be embedded.</summary>
    public const string FontEmbedForbidden = "LYS8002";
    /// <summary>Font warning: a font requested for embedding is not installed on this
    /// system, so it cannot be embedded.</summary>
    public const string FontNotFound = "LYS8003";

    /// <summary>Font error: a <c>fonts { }</c> entry names a key that is not a text role,
    /// a role group, or a generic family. Refused rather than ignored — a binding nobody
    /// reads looks exactly like one that works.</summary>
    public const string UnknownFontRole = "LYS8004";

    /// <summary>Font warning: one <c>fonts { }</c> block binds the same key twice. The
    /// LAST one takes effect, like every other repeated setting in the language; the
    /// earlier one is named so it is not silently dropped.</summary>
    public const string DuplicateFontBinding = "LYS8005";

    /// <summary>Font error: a <c>fonts { }</c> entry names a key but no face — the value
    /// must be one or more quoted names, or a generic family (<c>serif</c> /
    /// <c>sans</c>).</summary>
    public const string FontBindingMissingValue = "LYS8006";

    /// <summary>Font error: <c>fonts</c> written with a bare value instead of a block —
    /// <c>fonts "Georgia"</c>. Every other metadata keyword takes a bare value, so this is
    /// a plausible first guess and gets the block to write, with the writer's own face name
    /// in it, rather than "Expected 'OpenBrace'".</summary>
    public const string FontsNeedsABlock = "LYS8008";

    // Paper diagnostics (LYS9xxx)

    /// <summary>Paper error: a <c>paper { }</c> entry names a key that is not in the paper
    /// vocabulary. Refused rather than ignored, the same reasoning as
    /// <see cref="UnknownFontRole"/>: a setting nobody reads looks exactly like one that
    /// works. When the stray key is a unit word (<c>mm</c>), the message shows the glued
    /// spelling (<c>210mm</c>) instead of the vocabulary list.</summary>
    public const string UnknownPaperKey = "LYS9001";

    /// <summary>Paper warning: one <c>paper { }</c> block sets the same key twice. The
    /// LAST one takes effect, like every other repeated setting in the language; the
    /// earlier one is named so it is not silently dropped.</summary>
    public const string DuplicatePaperKey = "LYS9002";

    /// <summary>Paper error: a <c>paper { }</c> entry has no usable value — a key with
    /// nothing after it, a scalar where the key wants a nested spacing block (or the
    /// reverse), or a stray token that is neither a key nor a number.</summary>
    public const string PaperEntryMissingValue = "LYS9003";

    /// <summary>Paper error: a number carries a glued suffix that is not a unit of this
    /// language — the units are <c>mm</c>, <c>cm</c> and <c>in</c>, and a bare number is
    /// staff spaces.</summary>
    public const string UnknownPaperUnit = "LYS9004";

    /// <summary>Paper error: <c>paper</c> written without a block. Mirrors
    /// <see cref="FontsNeedsABlock"/> — the block to write is in the message.</summary>
    public const string PaperNeedsABlock = "LYS9005";

    /// <summary>Paper error: a physical unit on a unitless quantity —
    /// <c>stretchability</c> is a spring flexibility, not a length.</summary>
    public const string PaperUnitOnUnitless = "LYS9006";
}