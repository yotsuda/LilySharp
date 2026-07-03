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

using System.Reflection;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Syntax;

return Run(args);

static int Run(string[] args)
{
    // Handle empty args or global help
    if (args.Length == 0)
    {
        ShowHelp();
        return 0;
    }

    var first = args[0].ToLowerInvariant();
    
    // Global options
    if (first is "-h" or "--help")
    {
        ShowHelp();
        return 0;
    }
    
    if (first is "-V" or "--version")
    {
        ShowVersion();
        return 0;
    }

    // Commands
    return first switch
    {
        "svg" => RunSvg(args.Skip(1).ToArray()),
        "pdf" => RunPdf(args.Skip(1).ToArray()),
        "png" => RunPng(args.Skip(1).ToArray()),
        "midi" => RunMidi(args.Skip(1).ToArray()),
        "xml" => RunXml(args.Skip(1).ToArray()),
        "ly" => RunLy(args.Skip(1).ToArray()),
        "check" => RunCheck(args.Skip(1).ToArray()),
        "layout" => RunLayout(args.Skip(1).ToArray()),
        _ => UnknownCommand(first)
    };
}

static void ShowHelp()
{
    Console.WriteLine("""
        Lily# - Music notation compiler

        Usage: lysc <command> [options] <input> [output]
               lysc [options]

        Commands:
          svg     Convert to SVG (sheet music)
          pdf     Convert to PDF (sheet music)
          png     Convert to PNG (raster image)
          midi    Convert to MIDI (audio)
          xml     Convert to MusicXML
          check   Check syntax without output
          layout  Print a text summary of the layout (system/line breaks, bars per system)

        Global Options:
          -h, --help       Show this help
          -V, --version    Show version

        Examples:
          lysc svg score.lys                    # Output: score.svg
          lysc svg score.lys output.svg         # Specify output file
          lysc svg score.lys -o output.svg      # Same as above
          lysc pdf score.lys                    # Output: score.pdf
          lysc midi score.lys                   # Output: score.mid
          lysc check score.lys                  # Syntax check only

        Per-command help:
          lysc svg --help
          lysc pdf --help
          lysc midi --help
        """);
}

static void ShowVersion()
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    Console.WriteLine($"lysc {version}");
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Error: Unknown command '{command}'");
    Console.Error.WriteLine("Run 'lysc --help' for usage.");
    return 1;
}

// ============ SVG Command ============

static int RunSvg(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowSvgHelp();
        return 0;
    }

    var (inputPath, outputPath, embedFont, allMovements, scoreName, error) = ParseSvgOptions(args);
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc svg --help' for usage.");
        return 1;
    }

    if (allMovements)
        return ExecuteSvgAll(inputPath!, embedFont);
    else
        return ExecuteSvg(inputPath!, outputPath!, embedFont, scoreName);
}

static void ShowSvgHelp()
{
    Console.WriteLine("""
        Convert Lily# source to SVG

        Usage: lysc svg [options] <input.lys> [output.svg]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.svg]     Output SVG file (default: input with .svg extension)

        Options:
          -o, --output <file>    Output file path
          --no-embed-font        Don't embed font (smaller file, requires font installed)
          --all                  Generate all render blocks as separate SVG files
          --score <name>         Render the named score block (default: the first)
          -h, --help             Show this help

        Examples:
          lysc svg score.lys
          lysc svg score.lys sheet.svg
          lysc svg -o sheet.svg score.lys
          lysc svg score.lys --no-embed-font
          lysc svg --score greensleeves-grid greensleeves.lys
          lysc svg --all multi-movement.lys
        """);
}

static (string? InputPath, string? OutputPath, bool EmbedFont, bool AllMovements, string? ScoreName, string? Error) ParseSvgOptions(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    bool embedFont = true;
    bool allMovements = false;
    string? scoreName = null;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
                return (null, null, false, false, null, "-o requires a file path");
            outputPath = args[++i];
        }
        else if (arg is "--no-embed-font" or "-n")
        {
            embedFont = false;
        }
        else if (arg is "--all")
        {
            allMovements = true;
        }
        else if (arg is "--score")
        {
            if (i + 1 >= args.Length)
                return (null, null, false, false, null, "--score requires a score name");
            scoreName = args[++i];
        }
        else if (arg.StartsWith("-"))
        {
            return (null, null, false, false, null, $"Unknown option: {arg}");
        }
        else if (inputPath == null)
        {
            inputPath = arg;
        }
        else if (outputPath == null)
        {
            outputPath = arg;
        }
        else
        {
            return (null, null, false, false, null, $"Unexpected argument: {arg}");
        }
    }

    if (inputPath == null)
        return (null, null, false, false, null, "Input file required");

    if (!File.Exists(inputPath))
        return (null, null, false, false, null, $"File not found: {inputPath}");

    outputPath ??= Path.ChangeExtension(inputPath, ".svg");

    return (inputPath, outputPath, embedFont, allMovements, scoreName, null);
}

static int ExecuteSvg(string inputPath, string outputPath, bool embedFont, string? scoreName = null)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        // Surface every diagnostic (warnings to stderr too, so silent typos like `es`
        // as a bare variable reference are visible); errors abort.
        if (ReportDiagnostics(tree)) return 1;
        if (!ValidateScoreName(tree, scoreName)) return 1;

        // Configure render options
        LilySharp.Core.Svg.Renderer.SvgRenderOptions renderOptions;
        if (embedFont)
        {
            var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(fontDir);
        }
        else
        {
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;
        }
        
        // Generate SVG using shared generator
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, renderOptions, scoreName);
        
        File.WriteAllText(outputPath, svg);
        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine(embedFont ? "  Font embedded: Yes" : "  Font embedded: No");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// The generators deliberately fall back to the FIRST score for an unknown
// name (the LSP preview needs that after a rename) — on the command line a
// typo must fail loudly instead of silently rendering the wrong score.
static bool ValidateScoreName(LilySharp.Core.Syntax.SyntaxTree tree, string? scoreName)
{
    if (string.IsNullOrEmpty(scoreName))
        return true;
    if (LilySharp.Core.Svg.Collector.RenderSpecParser.FindByName(tree, scoreName) != null)
        return true;
    var names = LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree)
        .Select(s => s.Name)
        .Where(n => !string.IsNullOrEmpty(n))
        .ToList();
    Console.Error.WriteLine($"Error: no score named '{scoreName}'.");
    if (names.Count > 0)
        Console.Error.WriteLine($"Available scores: {string.Join(", ", names)}");
    return false;
}

static int ExecuteSvgAll(string inputPath, bool embedFont)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        if (ReportDiagnostics(tree)) return 1;

        LilySharp.Core.Svg.Renderer.SvgRenderOptions renderOptions;
        if (embedFont)
        {
            var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(fontDir);
        }
        else
        {
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;
        }

        var results = LilySharp.Core.Svg.SvgGenerator.GenerateAll(tree, renderOptions);
        var inputDir = Path.GetDirectoryName(inputPath) ?? ".";

        Console.WriteLine($"Generating {results.Count} movement(s):");

        foreach (var (filename, svg) in results)
        {
            var outputPath = string.IsNullOrEmpty(filename)
                ? Path.ChangeExtension(inputPath, ".svg")
                : Path.Combine(inputDir, filename);

            // Ensure .svg extension
            if (!outputPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                outputPath = Path.ChangeExtension(outputPath, ".svg");

            File.WriteAllText(outputPath, svg);
            Console.WriteLine($"  Created: {outputPath}");
        }

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ PDF Command ============

static int RunPdf(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowPdfHelp();
        return 0;
    }

    // --score is peeled off first; the rest goes through the shared parser.
    string? scoreName = null;
    var rest = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] is "--score")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --score requires a score name"); return 1; }
            scoreName = args[++i];
        }
        else rest.Add(args[i]);
    }
    var (inputPath, outputPath, error) = ParseSimpleOptions(rest.ToArray(), ".pdf");
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc pdf --help' for usage.");
        return 1;
    }

    return ExecutePdf(inputPath!, outputPath!, scoreName);
}

static void ShowPdfHelp()
{
    Console.WriteLine("""
        Convert Lily# source to PDF

        Usage: lysc pdf [options] <input.lys> [output.pdf]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.pdf]     Output PDF file (default: input with .pdf extension)

        Options:
          -o, --output <file>    Output file path
          --score <name>         Render the named score block (default: the first)
          -h, --help             Show this help

        Examples:
          lysc pdf score.lys
          lysc pdf score.lys sheet.pdf
          lysc pdf -o sheet.pdf score.lys
        """);
}

static int ExecutePdf(string inputPath, string outputPath, string? scoreName = null)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        if (ReportDiagnostics(tree)) return 1;
        if (!ValidateScoreName(tree, scoreName)) return 1;

        var pdfBytes = PdfGenerator.Generate(tree, null, scoreName);
        File.WriteAllBytes(outputPath, pdfBytes);

        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Size: {pdfBytes.Length / 1024.0:F1} KB");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ PNG Command ============

static int RunPng(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowPngHelp();
        return 0;
    }

    // Parse options with optional --scale flag
    string? inputPath = null;
    string? outputPath = null;
    string? scoreName = null;
    float scale = 2.0f;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: -o requires a file path"); return 1; }
            outputPath = args[++i];
        }
        else if (arg is "--score")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --score requires a score name"); return 1; }
            scoreName = args[++i];
        }
        else if (arg is "--scale")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: --scale requires a number"); return 1; }
            if (!float.TryParse(args[++i], out scale) || scale <= 0)
            {
                Console.Error.WriteLine("Error: --scale must be a positive number");
                return 1;
            }
        }
        else if (arg.StartsWith("-"))
        {
            Console.Error.WriteLine($"Error: Unknown option: {arg}");
            Console.Error.WriteLine("Run 'lysc png --help' for usage.");
            return 1;
        }
        else if (inputPath == null) inputPath = arg;
        else if (outputPath == null) outputPath = arg;
    }

    if (inputPath == null) { Console.Error.WriteLine("Error: Input file required"); return 1; }
    if (!File.Exists(inputPath)) { Console.Error.WriteLine($"Error: File not found: {inputPath}"); return 1; }
    outputPath ??= Path.ChangeExtension(inputPath, ".png");

    return ExecutePng(inputPath, outputPath, scale, scoreName);
}

static void ShowPngHelp()
{
    Console.WriteLine("""
        Convert Lily# source to PNG

        Usage: lysc png [options] <input.lys> [output.png]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.png]     Output PNG file (default: input with .png extension)

        Options:
          -o, --output <file>    Output file path
          --scale <factor>       Scale factor (default: 2.0 = 192 DPI)
          --score <name>         Render the named score block (default: the first)
          -h, --help             Show this help

        Examples:
          lysc png score.lys
          lysc png score.lys sheet.png
          lysc png --scale 3.0 score.lys    # High DPI (288 DPI)
          lysc png --scale 1.0 score.lys    # Standard DPI (96 DPI)
        """);
}

static int ExecutePng(string inputPath, string outputPath, float scale, string? scoreName = null)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        if (ReportDiagnostics(tree)) return 1;

        if (!ValidateScoreName(tree, scoreName)) return 1;
        var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
        var pngOptions = new PngRenderOptions { Scale = scale, FontDirectory = fontDir };

        // One file per page, following LilyPond's PNG naming: a single page
        // keeps the requested name, multiple pages become BASE-page1.png,
        // BASE-page2.png, … (scm/ps-to-png.scm).
        var pages = PngGenerator.GeneratePages(tree, pngOptions, scoreName);
        if (pages.Count == 1)
        {
            File.WriteAllBytes(outputPath, pages[0]);
            Console.WriteLine($"Created: {outputPath}");
            Console.WriteLine($"  Size: {pages[0].Length / 1024.0:F1} KB");
        }
        else
        {
            string dir = Path.GetDirectoryName(outputPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(outputPath);
            string ext = Path.GetExtension(outputPath);
            for (int p = 0; p < pages.Count; p++)
            {
                string pagePath = Path.Combine(dir, $"{baseName}-page{p + 1}{ext}");
                File.WriteAllBytes(pagePath, pages[p]);
                Console.WriteLine($"Created: {pagePath}");
                Console.WriteLine($"  Size: {pages[p].Length / 1024.0:F1} KB");
            }
        }
        Console.WriteLine($"  Scale: {scale:F1}x");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ MIDI Command ============

static int RunMidi(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowMidiHelp();
        return 0;
    }

    var (inputPath, outputPath, error) = ParseSimpleOptions(args, ".mid");
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc midi --help' for usage.");
        return 1;
    }

    return ExecuteMidi(inputPath!, outputPath!);
}

static void ShowMidiHelp()
{
    Console.WriteLine("""
        Convert Lily# source to MIDI

        Usage: lysc midi [options] <input.lys> [output.mid]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.mid]     Output MIDI file (default: input with .mid extension)

        Options:
          -o, --output <file>    Output file path
          -h, --help             Show this help

        Examples:
          lysc midi score.lys
          lysc midi score.lys audio.mid
          lysc midi -o audio.mid score.lys
        """);
}

static int ExecuteMidi(string inputPath, string outputPath)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        if (ReportDiagnostics(tree)) return 1;

        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        midi.Save(outputPath);

        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Tracks: {midi.Tracks.Count}");
        Console.WriteLine($"  Notes: {midi.Tracks.Skip(1).Sum(t => t.Notes.Count)}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ MusicXML Command ============

static int RunXml(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowXmlHelp();
        return 0;
    }

    var (inputPath, outputPath, error) = ParseSimpleOptions(args, ".xml");
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc xml --help' for usage.");
        return 1;
    }

    return ExecuteXml(inputPath!, outputPath!);
}

static void ShowXmlHelp()
{
    Console.WriteLine("""
        Convert Lily# source to MusicXML

        Usage: lysc xml [options] <input.lys> [output.xml]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.xml]     Output MusicXML file (default: input with .xml extension)

        Options:
          -o, --output <file>    Output file path
          -h, --help             Show this help

        Examples:
          lysc xml score.lys
          lysc xml score.lys export.xml
          lysc xml -o export.xml score.lys
        """);
}

static int ExecuteXml(string inputPath, string outputPath)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);

        if (ReportDiagnostics(tree)) return 1;

        var exporter = new MusicXmlExporter();
        var xml = exporter.Export(tree);
        xml.Save(outputPath);

        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Parts: {xml.Parts.Count}");
        Console.WriteLine($"  Measures: {xml.Parts.Sum(p => p.Measures.Count)}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ LilyPond Command ============

static int RunLy(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        Console.WriteLine("""
            Convert Lily# source to LilyPond (.ly)

            Usage: lysc ly [options] <input.lys> [output.ly]
              -o, --output <file>    Output file path
              -h, --help             Show this help
            """);
        return 0;
    }

    var (inputPath, outputPath, error) = ParseSimpleOptions(args, ".ly");
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc ly --help' for usage.");
        return 1;
    }

    try
    {
        var (_, tree) = LoadAndParse(inputPath!);
        if (ReportDiagnostics(tree)) return 1;
        var ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        File.WriteAllText(outputPath!, ly);
        Console.WriteLine($"Created: {outputPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ Check Command ============

static int RunCheck(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        ShowCheckHelp();
        return 0;
    }

    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: Input file required");
        Console.Error.WriteLine("Run 'lysc check --help' for usage.");
        return 1;
    }

    bool showPitches = args.Contains("--pitches") || args.Contains("-p");
    var inputPath = args.FirstOrDefault(a => !a.StartsWith('-'));
    if (inputPath is null || !File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    return ExecuteCheck(inputPath, showPitches);
}

static void ShowCheckHelp()
{
    Console.WriteLine("""
        Check Lily# source syntax

        Usage: lysc check <input.lys>

        Arguments:
          <input.lys>      Input Lily# source file

        Options:
          -p, --pitches    Also print each note's resolved absolute pitch
                           (written -> resolved), so relative-octave mistakes
                           are visible before rendering
          -h, --help       Show this help

        Examples:
          lysc check score.lys
          lysc check score.lys --pitches
        """);
}

static int ExecuteCheck(string inputPath, bool showPitches = false)
{
    try
    {
        var (source, tree) = LoadAndParse(inputPath);
        var allDiagnostics = CollectDiagnostics(tree);

        if (showPitches)
            PrintResolvedPitches(source, tree);

        if (allDiagnostics.Count == 0)
        {
            if (!showPitches)
                Console.WriteLine("No errors found.");
            return 0;
        }

        bool hasErrors = false;
        foreach (var diag in allDiagnostics)
        {
            var severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                _ => "info"
            };
            if (diag.Severity == DiagnosticSeverity.Error) hasErrors = true;
            Console.WriteLine($"{inputPath}({LineCol(source, diag.Span.Start)}): {severity}: {diag.Message}");
        }

        return hasErrors ? 1 : 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

/// <summary>
/// Prints each note's resolved absolute pitch (written → resolved), making the
/// relative-octave chain's otherwise-invisible state visible so authors can spot
/// octave mistakes BEFORE rendering. Driven by `check --pitches`.
/// </summary>
static void PrintResolvedPitches(string source, SyntaxTree tree)
{
    IReadOnlyList<LilySharp.Core.Svg.Collector.MeasureCollector.PitchTraceEntry> trace;
    try
    {
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector();
        collector.Collect(tree);
        trace = collector.PitchTrace;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(could not resolve pitches: {ex.Message})");
        return;
    }

    Console.WriteLine($"Resolved pitches ({trace.Count}):");
    foreach (var e in trace)
    {
        // The token's span starts at its leading trivia (indent/newline); advance
        // to the actual pitch so the line:col and written token line up.
        int p = e.Position;
        while (p < source.Length && char.IsWhiteSpace(source[p])) p++;
        var (line, col) = LineColFromPosition(source, p);
        string written = ReadPitchToken(source, p);
        Console.WriteLine($"  {line,4}:{col,-3} {written,-7} -> {e.Pitch}");
    }
    Console.WriteLine();
}

static (int Line, int Col) LineColFromPosition(string source, int pos)
{
    int line = 1, col = 1;
    int n = Math.Min(pos, source.Length);
    for (int i = 0; i < n; i++)
    {
        if (source[i] == '\n') { line++; col = 1; }
        else col++;
    }
    return (line, col);
}

static string ReadPitchToken(string source, int pos)
{
    if (pos < 0 || pos >= source.Length) return "";
    int end = pos;
    while (end < source.Length &&
           (char.IsLetter(source[end]) || source[end] == '\'' || source[end] == ','))
        end++;
    return source[pos..end];
}

/// <summary>
/// Combines syntax-tree diagnostics with semantic-validator diagnostics
/// (e.g. undefined variable / phrase / section references).
/// </summary>
static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> CollectDiagnostics(SyntaxTree tree)
{
    // Parser diagnostics plus every semantic validator (single shared registry, so
    // the CLI and the LSP can never diverge on which validators run).
    var combined = new List<LilySharp.Core.Syntax.Diagnostic>(tree.Diagnostics);
    combined.AddRange(LilySharp.Core.Semantics.SemanticValidation.Run(tree));
    return combined;
}

/// <summary>
/// Prints every diagnostic (parser AND semantic) to stderr, then reports whether the
/// caller should abort. EVERY output path (svg/pdf/png/midi/xml/ly) goes through this,
/// so a semantic error — an undefined variable, a measure overflow — can't be silently
/// dropped for one format while it blocks another. Warnings are surfaced but don't abort.
/// </summary>
static bool ReportDiagnostics(SyntaxTree tree)
{
    var all = CollectDiagnostics(tree);
    if (all.Count == 0)
        return false;

    bool hasErrors = all.Any(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
    Console.Error.WriteLine(hasErrors ? "Syntax errors:" : "Diagnostics:");
    foreach (var diag in all)
        Console.Error.WriteLine($"  ({LineCol(tree.Text, diag.Span.Start)}) {diag}");
    return hasErrors;
}

// 1-based line,column for a source offset — every human-facing diagnostic
// prints this instead of the raw byte offset.
static string LineCol(string text, int offset)
{
    int line = 1, col = 1;
    for (int i = 0; i < offset && i < text.Length; i++)
    {
        if (text[i] == '\n') { line++; col = 1; }
        else col++;
    }
    return $"{line},{col}";
}

// ============ Layout Command ============

static int RunLayout(string[] args)
{
    if (args.Contains("-h") || args.Contains("--help"))
    {
        Console.WriteLine("""
            Print a text summary of the engine's layout decisions

            Usage: lysc layout <input.lys>

            Shows, per score: the staves, the meter (with any mid-piece changes),
            the system count, which bars landed in each system, and where the line
            breaker split the music — the layout facts a source file does not reveal,
            so you can verify the result without rendering an image. (For resolved
            pitches, use 'check --pitches'.)

            By default the first score is reported; --all reports every score block.

            Options:
              --all            Report every score block (default: first only)
              -h, --help       Show this help

            Examples:
              lysc layout score.lys
              lysc layout --all multi-score.lys
            """);
        return 0;
    }

    bool allScores = args.Contains("--all");
    var inputPath = args.FirstOrDefault(a => !a.StartsWith('-'));
    if (inputPath is null)
    {
        Console.Error.WriteLine("Error: Input file required");
        Console.Error.WriteLine("Run 'lysc layout --help' for usage.");
        return 1;
    }
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    try
    {
        var (_, tree) = LoadAndParse(inputPath);
        // Surface warnings, abort on errors — a layout summary of broken source is
        // misleading, so route through the same gate as every output command.
        if (ReportDiagnostics(tree)) return 1;

        Console.Write(LilySharp.Core.Svg.LayoutReport.Generate(tree, allScores));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

// ============ Shared Utilities ============

// Read a .lys file and parse it, first resolving any `include "..."` directives
// (relative to the file) into one combined source. The main file is the prefix, so
// its diagnostic positions are unchanged.
static (string Source, LilySharp.Core.Syntax.SyntaxTree Tree) LoadAndParse(string inputPath)
{
    var source = File.ReadAllText(inputPath);
    if (LilySharp.Core.Parser.IncludeExpander.HasIncludes(source))
        source = LilySharp.Core.Parser.IncludeExpander.Expand(source, inputPath,
            p => File.Exists(p) ? File.ReadAllText(p) : null);
    return (source, LilySharp.Core.Syntax.SyntaxTree.Parse(source));
}

static (string? InputPath, string? OutputPath, string? Error) ParseSimpleOptions(string[] args, string defaultExt)
{
    string? inputPath = null;
    string? outputPath = null;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        
        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
                return (null, null, "-o requires a file path");
            outputPath = args[++i];
        }
        else if (arg.StartsWith("-"))
        {
            return (null, null, $"Unknown option: {arg}");
        }
        else if (inputPath == null)
        {
            inputPath = arg;
        }
        else if (outputPath == null)
        {
            outputPath = arg;
        }
        else
        {
            return (null, null, $"Unexpected argument: {arg}");
        }
    }

    if (inputPath == null)
        return (null, null, "Input file required");

    if (!File.Exists(inputPath))
        return (null, null, $"File not found: {inputPath}");

    outputPath ??= Path.ChangeExtension(inputPath, defaultExt);
    
    return (inputPath, outputPath, null);
}

