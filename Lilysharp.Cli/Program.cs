using Lilysharp.Core.Midi;
using Lilysharp.Core.MusicXml;
using Lilysharp.Core.Syntax;

if (args.Length == 0)
{
    Console.WriteLine("Lilysharp - Music notation compiler");
    Console.WriteLine();
    Console.WriteLine("Usage: lilysharp <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  midi <input.lys> [output.mid]  Convert to MIDI");
    Console.WriteLine("  xml <input.lys> [output.xml]   Convert to MusicXML");
    Console.WriteLine("  check <input.lys>              Check syntax");
    return 0;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "midi":
        return ExportMidi(args.Skip(1).ToArray());
    case "xml":
        return ExportMusicXml(args.Skip(1).ToArray());
    case "check":
        return CheckSyntax(args.Skip(1).ToArray());
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        return 1;
}

static int ExportMidi(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: Input file required");
        return 1;
    }

    var inputPath = args[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    var outputPath = args.Length > 1 
        ? args[1] 
        : Path.ChangeExtension(inputPath, ".mid");

    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
            {
                Console.Error.WriteLine($"  {diag}");
            }
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

static int CheckSyntax(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: Input file required");
        return 1;
    }

    var inputPath = args[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

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

static int ExportMusicXml(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: Input file required");
        return 1;
    }

    var inputPath = args[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    var outputPath = args.Length > 1 
        ? args[1] 
        : Path.ChangeExtension(inputPath, ".xml");

    try
    {
        var source = File.ReadAllText(inputPath);
        var tree = SyntaxTree.Parse(source);

        if (tree.HasErrors)
        {
            Console.Error.WriteLine("Syntax errors:");
            foreach (var diag in tree.Diagnostics)
            {
                Console.Error.WriteLine($"  {diag}");
            }
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
