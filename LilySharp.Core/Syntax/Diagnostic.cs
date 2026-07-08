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

    /// <summary>Parser warning: a bare clef name (treble/bass/…) was used like a
    /// staff block at the top level (e.g. <c>treble { … }</c>), which the grammar
    /// silently drops. Points to the real grand-staff form.</summary>
    public const string ClefNameAsStaff = "LYS0011";

    /// <summary>Parser warning: a LilyPond forced accidental <c>!</c> was written
    /// glued to a note (e.g. <c>c'!</c>), which Lily# otherwise reads as a dashed
    /// barline. Points to the <c>@editorial</c> / <c>@courtesy</c> annotations.</summary>
    public const string LilypondAccidentalReflex = "LYS0012";

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
    /// <summary>Semantic error: reference to an undefined part.</summary>
    public const string UndefinedPart = "LYS1007";
    /// <summary>Semantic error: an unknown annotation was used.</summary>
    public const string UnknownAnnotation = "LYS1008";
    /// <summary>Semantic error: a rehearsal mark label was not quoted
    /// (<c>@mark(A)</c> instead of <c>@mark("A")</c>).</summary>
    public const string MarkLabelNotQuoted = "LYS1009";
    /// <summary>Semantic error: multiple structure declarations were found.</summary>
    public const string MultipleStructureDeclarations = "LYS1009";
    /// <summary>Semantic error: invalid barline placement for a volta repeat.</summary>
    public const string VoltaRepeatBarlinePlacement = "LYS1010";

    /// <summary>Syntax error: a volta ending must be bracketed — <c>[N. Section]</c>.</summary>
    public const string VoltaBracketRequired = "LYS1011";

    /// <summary>Syntax error: a phrase reference needs a <c>$</c> — write <c>$name</c>.</summary>
    public const string BareReferenceRequiresDollar = "LYS1012";

    /// <summary>Syntax error: a metadata value (title/composer) must be a quoted string.</summary>
    public const string MetadataValueMustBeQuoted = "LYS1013";

    /// <summary>Semantic error: an unknown or wrong-case symbol (property name or a
    /// clef/instrument/tuning value); symbols are case-sensitive.</summary>
    public const string UnknownSymbolCase = "LYS1014";

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

    // Lyric warnings (LYS4xxx)

    /// <summary>Lyric warning: more lyric syllables than available notes.</summary>
    public const string LyricSyllableOverflow = "LYS4001";
    /// <summary>An underfull FIRST measure with no `partial` declaration - a
    /// bare anacrusis is indistinguishable from a miscount, so nudge toward
    /// declaring it (which also numbers it as bar 0).</summary>
    public const string PickupWithoutPartial = "LYS2003";

    // Tablature warnings (LYS5xxx)

    /// <summary>Tablature warning: a tie conflicts with the assigned string.</summary>
    public const string TabTieStringConflict = "LYS5001";
    /// <summary>Tablature warning: a fret position is out of range for the instrument.</summary>
    public const string TabOutOfRange = "LYS5002";

    // Render/score declaration errors (LYS6xxx)

    /// <summary>Render error: a score name was declared more than once.</summary>
    public const string DuplicateScoreName = "LYS6001";

    // Structure / section-part grid errors (LYS7xxx)

    /// <summary>Structure error: a section-part grid cell was declared more than once.</summary>
    public const string DuplicateCell = "LYS7001";
    /// <summary>Lexer error: an unexpected character was encountered.</summary>
    public const string UnexpectedCharacter = "LYS0009";
}