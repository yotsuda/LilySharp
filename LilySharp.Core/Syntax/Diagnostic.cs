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
    public static Diagnostic Error(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Error, span, code, message);

    public static Diagnostic Warning(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Warning, span, code, message);

    public static Diagnostic Info(TextSpan span, string code, string message)
        => new(DiagnosticSeverity.Info, span, code, message);
}

/// <summary>
/// A collection of diagnostics.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = [];

    public void Add(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    public void AddRange(IEnumerable<Diagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    public void Error(TextSpan span, string code, string message)
        => Add(Diagnostic.Error(span, code, message));

    public void Warning(TextSpan span, string code, string message)
        => Add(Diagnostic.Warning(span, code, message));

    public IReadOnlyList<Diagnostic> ToList() => _diagnostics;

    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public int Count => _diagnostics.Count;

    public IEnumerable<Diagnostic> Errors => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    public IEnumerable<Diagnostic> Warnings => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);
}

/// <summary>
/// Standard diagnostic codes for LilySharp.
/// </summary>
public static class DiagnosticCodes
{
    // Parser errors (LYS0xxx)
    public const string UnexpectedToken = "LYS0001";
    public const string ExpectedToken = "LYS0002";
    public const string UnterminatedString = "LYS0003";
    public const string UnterminatedComment = "LYS0004";
    public const string InvalidNumber = "LYS0005";
    public const string RepeatVoltaRemoved = "LYS0006";
    public const string LegacyDeclarationForm = "LYS0007";
    public const string ParallelSyntaxRemoved = "LYS0008";
    public const string LilypondBackslashCommand = "LYS0009";
    public const string NestedVoiceBlock = "LYS0010";

    // Semantic errors (LYS1xxx)
    public const string UndefinedVariable = "LYS1001";
    public const string DuplicateVariable = "LYS1002";
    public const string InvalidPitch = "LYS1003";
    public const string InvalidDuration = "LYS1004";
    public const string UndefinedSection = "LYS1005";
    public const string UndefinedPhrase = "LYS1006";
    public const string UndefinedPart = "LYS1007";
    public const string UnknownAnnotation = "LYS1008";
    public const string MultipleStructureDeclarations = "LYS1009";
    public const string VoltaRepeatBarlinePlacement = "LYS1010";

    // Deprecation warnings (LYS3xxx)
    public const string DeprecatedBareReference = "LYS3001";
    public const string DeprecatedUseKeyword = "LYS3002";

    // Measure errors (LYS2xxx)
    public const string MeasureIncomplete = "LYS2001";
    public const string MeasureOverflow = "LYS2002";
    public const string NoTimeSignature = "LYS2003";
    public const string MeasureDurationMismatch = "LYS2004";
    public const string ConflictingTimeSignatures = "LYS2005";

    // Lyric warnings (LYS4xxx)
    public const string LyricSyllableOverflow = "LYS4001";
    /// <summary>An underfull FIRST measure with no `partial` declaration - a
    /// bare anacrusis is indistinguishable from a miscount, so nudge toward
    /// declaring it (which also numbers it as bar 0).</summary>
    public const string PickupWithoutPartial = "LYS2003";

    // Tablature warnings (LYS5xxx)
    public const string TabTieStringConflict = "LYS5001";
    public const string TabOutOfRange = "LYS5002";

    // Render/score declaration errors (LYS6xxx)
    public const string DuplicateScoreName = "LYS6001";

    // Structure / section-part grid errors (LYS7xxx)
    public const string DuplicateCell = "LYS7001";
    public const string UnexpectedCharacter = "LYS0009";
}