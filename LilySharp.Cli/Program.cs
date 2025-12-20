using System.Reflection;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
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
          lysc midi score.lys                   # Output: score.mid
          lysc check score.lys                  # Syntax check only

        Per-command help:
          lysc svg --help
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

    var (inputPath, outputPath, embedFont, error) = ParseSvgOptions(args);
    if (error != null)
    {
        Console.Error.WriteLine($"Error: {error}");
        Console.Error.WriteLine("Run 'lysc svg --help' for usage.");
        return 1;
    }

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
          -h, --help             Show this help

        Examples:
          lysc svg score.lys
          lysc svg score.lys sheet.svg
          lysc svg -o sheet.svg score.lys
          lysc svg score.lys --no-embed-font
        """);
}

static (string? InputPath, string? OutputPath, bool EmbedFont, string? Error) ParseSvgOptions(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    bool embedFont = true;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        
        if (arg is "-o" or "--output")
        {
            if (i + 1 >= args.Length)
                return (null, null, false, "-o requires a file path");
            outputPath = args[++i];
        }
        else if (arg is "--no-embed-font" or "-n")
        {
            embedFont = false;
        }
        else if (arg.StartsWith("-"))
        {
            return (null, null, false, $"Unknown option: {arg}");
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
            return (null, null, false, $"Unexpected argument: {arg}");
        }
    }

    if (inputPath == null)
        return (null, null, false, "Input file required");

    if (!File.Exists(inputPath))
        return (null, null, false, $"File not found: {inputPath}");

    outputPath ??= Path.ChangeExtension(inputPath, ".svg");
    
    return (inputPath, outputPath, embedFont, null);
}

static int ExecuteSvg(string inputPath, string outputPath, bool embedFont)
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

        if (tree.Diagnostics.Count == 0)
        {
            Console.WriteLine("No errors found.");
            return 0;
        }

        foreach (var diag in tree.Diagnostics)
        {
            var severity = diag.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                _ => "info"
            };
            Console.WriteLine($"{inputPath}({diag.Span.Start}): {severity}: {diag.Message}");
        }

        return tree.HasErrors ? 1 : 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
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
        "fonts",
        "../fonts",
        "editors/vscode/media/fonts",
        Path.Combine(AppContext.BaseDirectory, "fonts"),
        Path.Combine(AppContext.BaseDirectory, "..", "fonts")
    };
    
    foreach (var candidate in candidates)
    {
        if (Directory.Exists(candidate) && 
            File.Exists(Path.Combine(candidate, "emmentaler-20.woff2")))
        {
            return Path.GetFullPath(candidate);
        }
    }
    
    return null;
}