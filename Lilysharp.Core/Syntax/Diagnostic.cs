namespace Lilysharp.Core.Syntax;

/// <summary>
/// Severity of a diagnostic.
/// </summary>
public enum DiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
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
/// Standard diagnostic codes for Lilysharp.
/// </summary>
public static class DiagnosticCodes
{
    // Parser errors (LYS0xxx)
    public const string UnexpectedToken = "LYS0001";
    public const string ExpectedToken = "LYS0002";
    public const string UnterminatedString = "LYS0003";
    public const string UnterminatedComment = "LYS0004";
    public const string InvalidNumber = "LYS0005";

    // Semantic errors (LYS1xxx)
    public const string UndefinedVariable = "LYS1001";
    public const string DuplicateVariable = "LYS1002";
    public const string InvalidPitch = "LYS1003";
    public const string InvalidDuration = "LYS1004";

    // Measure errors (LYS2xxx)
    public const string MeasureIncomplete = "LYS2001";
    public const string MeasureOverflow = "LYS2002";
    public const string NoTimeSignature = "LYS2003";
}