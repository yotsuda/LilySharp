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
using LilySharp.Core.Pdf.Renderer;
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
        "check" => RunCheck(args.Skip(1).ToArray()),
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

    var (inputPath, outputPath, embedFont, allMovements, error) = ParseSvgOptions(args);
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc svg --help' for usage.");
        return 1;
    }

    if (allMovements)
        return ExecuteSvgAll(inputPath!, embedFont);
    else
        return ExecuteSvg(inputPath!, outputPath!, embedFont);
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
          -h, --help             Show this help

        Examples:
          lysc svg score.lys
          lysc svg score.lys sheet.svg
          lysc svg -o sheet.svg score.lys
          lysc svg score.lys --no-embed-font
          lysc svg --all multi-movement.lys
        """);
}

static (string? InputPath, string? OutputPath, bool EmbedFont, bool AllMovements, string? Error) ParseSvgOptions(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    bool embedFont = true;
    bool allMovements = false;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];

        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
                return (null, null, false, false, "-o requires a file path");
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
        else if (arg.StartsWith("-"))
        {
            return (null, null, false, false, $"Unknown option: {arg}");
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
            return (null, null, false, false, $"Unexpected argument: {arg}");
        }
    }

    if (inputPath == null)
        return (null, null, false, false, "Input file required");

    if (!File.Exists(inputPath))
        return (null, null, false, false, $"File not found: {inputPath}");

    outputPath ??= Path.ChangeExtension(inputPath, ".svg");

    return (inputPath, outputPath, embedFont, allMovements, null);
}

static int ExecuteSvg(string inputPath, string outputPath, bool embedFont)
{
    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);
        var allDiagnostics = CollectDiagnostics(tree);

        // Always surface diagnostics — warnings are emitted to stderr even when
        // the build proceeds, so silent typos (e.g. `es` masquerading as a
        // bare variable reference, or a misspelled phrase name) are visible
        // to the user.
        if (allDiagnostics.Count > 0)
        {
            bool hasErrors = allDiagnostics.Any(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error);
            Console.Error.WriteLine(hasErrors ? "Syntax errors:" : "Diagnostics:");
            foreach (var diag in allDiagnostics)
                Console.Error.WriteLine($"  {diag}");
            if (hasErrors) return 1;
        }

        // Configure render options
        LilySharp.Core.Svg.Renderer.SvgRenderOptions renderOptions;
        if (embedFont)
        {
            var fontDir = FindFontDirectory();
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(fontDir);
        }
        else
        {
            renderOptions = LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;
        }
        
        // Generate SVG using shared generator
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, renderOptions);
        
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

static int ExecuteSvgAll(string inputPath, bool embedFont)
{
    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
                Console.Error.WriteLine($"  {diag}");
            return 1;
        }

        LilySharp.Core.Svg.Renderer.SvgRenderOptions renderOptions;
        if (embedFont)
        {
            var fontDir = FindFontDirectory();
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

    var (inputPath, outputPath, error) = ParseSimpleOptions(args, ".pdf");
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc pdf --help' for usage.");
        return 1;
    }

    return ExecutePdf(inputPath!, outputPath!);
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
          -h, --help             Show this help

        Examples:
          lysc pdf score.lys
          lysc pdf score.lys sheet.pdf
          lysc pdf -o sheet.pdf score.lys
        """);
}

static int ExecutePdf(string inputPath, string outputPath)
{
    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
                Console.Error.WriteLine($"  {diag}");
            return 1;
        }

        var pdfBytes = PdfGenerator.Generate(tree);
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
    float scale = 2.0f;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length) { Console.Error.WriteLine("Error: -o requires a file path"); return 1; }
            outputPath = args[++i];
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

    return ExecutePng(inputPath, outputPath, scale);
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
          -h, --help             Show this help

        Examples:
          lysc png score.lys
          lysc png score.lys sheet.png
          lysc png --scale 3.0 score.lys    # High DPI (288 DPI)
          lysc png --scale 1.0 score.lys    # Standard DPI (96 DPI)
        """);
}

static int ExecutePng(string inputPath, string outputPath, float scale)
{
    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
                Console.Error.WriteLine($"  {diag}");
            return 1;
        }

        var fontDir = FindFontDirectory();
        var pngOptions = new PngRenderOptions { Scale = scale, FontDirectory = fontDir };
        var pngBytes = PngGenerator.Generate(tree, pngOptions);
        File.WriteAllBytes(outputPath, pngBytes);

        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Size: {pngBytes.Length / 1024.0:F1} KB");
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
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
                Console.Error.WriteLine($"  {diag}");
            return 1;
        }

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
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
                Console.Error.WriteLine($"  {diag}");
            return 1;
        }

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

    var inputPath = args[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    return ExecuteCheck(inputPath);
}

static void ShowCheckHelp()
{
    Console.WriteLine("""
        Check Lily# source syntax

        Usage: lysc check <input.lys>

        Arguments:
          <input.lys>      Input Lily# source file

        Options:
          -h, --help       Show this help

        Examples:
          lysc check score.lys
        """);
}

static int ExecuteCheck(string inputPath)
{
    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);
        var allDiagnostics = CollectDiagnostics(tree);

        if (allDiagnostics.Count == 0)
        {
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
            Console.WriteLine($"{inputPath}({diag.Span.Start}): {severity}: {diag.Message}");
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
/// Combines syntax-tree diagnostics with semantic-validator diagnostics
/// (e.g. undefined variable / phrase / section references).
/// </summary>
static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> CollectDiagnostics(SyntaxTree tree)
{
    var combined = new List<LilySharp.Core.Syntax.Diagnostic>(tree.Diagnostics);
    var validator = new LilySharp.Core.Semantics.SymbolReferenceValidator();
    validator.Validate(tree);
    combined.AddRange(validator.Diagnostics);
    return combined;
}

// ============ Shared Utilities ============

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

static string? FindFontDirectory()
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "fonts"),
        Path.Combine(AppContext.BaseDirectory, "..", "fonts"),
        "fonts",
        "../fonts",
        "editors/vscode/media/fonts"
    };
    
    foreach (var candidate in candidates)
    {
        if (Directory.Exists(candidate) &&
            (File.Exists(Path.Combine(candidate, "emmentaler-20.otf")) ||
             File.Exists(Path.Combine(candidate, "emmentaler-20.woff2"))))
        {
            return Path.GetFullPath(candidate);
        }
    }
    
    return null;
}