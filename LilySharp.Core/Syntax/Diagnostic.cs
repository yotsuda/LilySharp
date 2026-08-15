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
public static class DiagnosticCodes
{
    // Parser errors (LYS0xxx)

    /// <summary>Parser error: an unexpected token was encountered.</summary>
    public const string UnexpectedToken = "LYS0001";
    /// <summary>Parser error: an expected token was missing.</summary>
    public const string ExpectedToken = "LYS0002";
    /// <summary>Parser error: a string literal was not terminated.</summary>
    public const string UnterminatedString = "LYS0003";
    /// <summary>Parser error: a comment was not terminated.</summary>
    public const string UnterminatedComment = "LYS0004";
    /// <summary>Parser error: an invalid numeric literal was encountered.</summary>
    public const string InvalidNumber = "LYS0005";
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

    /// <summary>Parse error: a bare number in a music stream — a DETACHED
    /// duration. A duration must be glued to what it lengthens (<c>c4</c>,
    /// <c>&lt;c e g&gt;4</c>); separated by a space it means nothing.</summary>
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
    /// are back in the band they number. LYS0013 is RETIRED (it was the removed
    /// <c>version</c> directive) and is deliberately not reused.
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

    // Semantic errors (LYS1xxx)

    /// <summary>Semantic error: reference to an undefined variable.</summary>
    public const string UndefinedVariable = "LYS1001";
    /// <summary>Semantic error: a variable was declared more than once.</summary>
    public const string DuplicateVariable = "LYS1002";
    /// <summary>Semantic error: an invalid pitch was specified.</summary>
    public const string InvalidPitch = "LYS1003";
    /// <summary>Semantic error: an invalid duration was specified.</summary>
    public const string InvalidDuration = "LYS1004";
    /// <summary>Semantic error: reference to an undefined section.</summary>
    public const string UndefinedSection = "LYS1005";
    /// <summary>Semantic error: reference to an undefined phrase.</summary>
    public const string UndefinedPhrase = "LYS1006";
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
    /// <summary>Semantic error: multiple structure declarations were found.</summary>
    public const string MultipleFormDeclarations = "LYS1015";
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
    /// no known chord quality. Use the explicit form <c>@chord(c:maj7)</c>.</summary>
    public const string ChordNotRecognized = "LYS1020";

    /// <summary>Syntax error: a scale degree is 1-based (1 = root/unison), so
    /// <c>&lt;0 …&gt;</c> is invalid.</summary>
    public const string InvalidScaleDegree = "LYS1021";

    // Measure errors (LYS2xxx)

    /// <summary>Measure error: a measure has fewer beats than the time signature requires.</summary>
    public const string MeasureIncomplete = "LYS2001";
    /// <summary>Measure error: a measure has more beats than the time signature allows.</summary>
    public const string MeasureOverflow = "LYS2002";
    /// <summary>Measure error: no time signature is in effect.</summary>
    public const string NoTimeSignature = "LYS2003";
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
    /// <summary>Warning: a span that opens exactly ONE unnamed <c>voice { … }</c>. The
    /// block is then entirely transparent — stem forcing needs a second voice, so the
    /// music engraves as if the braces were not there. Someone who wrote it meaning
    /// "polyphonic from here" gets a single-voice score with nothing said. A NAMED lone
    /// voice is exempt: its name is what a <c>lyrics NAME { … }</c> block binds to.</summary>
    public const string LoneVoiceBlock = "LYS4011";
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

    // Structure / section-part grid errors (LYS7xxx)

    /// <summary>Structure error: a section-part grid cell was declared more than once.</summary>
    public const string DuplicateCell = "LYS7001";
    /// <summary>Structure error: a chords/lyrics track names the same section twice.</summary>
    public const string DuplicateTrackSection = "LYS7002";

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
}