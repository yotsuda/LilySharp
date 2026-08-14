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
using LilySharp.Cli;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Syntax;

return Run(args);

static int Run(string[] args)
{
    // Global --verbose/--debug: print full stack traces on error. Stripped here so
    // the strict per-command parsers don't reject it as an unknown option.
    CliParser.TakeVerbose(ref args);

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
        "import" => RunImport(args.Skip(1).ToArray()),
        "vsqx" => RunVsqx(args.Skip(1).ToArray()),
        "harmonize" => RunHarmonize(args.Skip(1).ToArray()),
        "check" => RunCheck(args.Skip(1).ToArray()),
        "layout" => RunLayout(args.Skip(1).ToArray()),
        _ => UnknownCommand(first)
    };
}

static void ShowHelp()
{
    Console.WriteLine($"Lily# {VersionString()} - Music notation compiler");
    Console.WriteLine("""

        Usage: lysc <command> [options] <input> [output]
               lysc [options]

        Commands:
          svg        Convert to SVG (sheet music)
          pdf        Convert to PDF (sheet music)
          png        Convert to PNG (raster image)
          midi       Convert to MIDI (audio)
          vsqx       Convert to VOCALOID sequence (vocal part + lyrics)

          xml        Convert to MusicXML
          ly         Convert to LilyPond (.ly) source
          import     Import MusicXML (.xml/.musicxml/.mxl) to a Lily# source file

          harmonize  Suggest a diatonic chord track for the melody (prints a chords part)
          check      Check syntax without output
          layout     Print a text summary of the layout (system/line breaks, bars per system)

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

// --version says who owns the program and under what terms, in the shape the GNU
// tools use. Nothing in the GPL forces a CLI to print this, but it is how a user
// who only ever receives a binary learns they may redistribute it and that it
// comes with no warranty.
static void ShowVersion()
{
    Console.WriteLine($"""
        lysc {VersionString()}
        Copyright (C) 2025-2026 Yoshifumi Tsuda
        License GPLv3+: GNU GPL version 3 or later <https://gnu.org/licenses/gpl.html>.
        This is free software: you are free to change and redistribute it.
        There is NO WARRANTY, to the extent permitted by law.

        Contains code ported from LilyPond (GPLv3+), and bundles third-party
        components under GPL-compatible licenses; see THIRD-PARTY-NOTICES.md.
        Source: <https://github.com/yotsuda/LilySharp>
        """);
}

// The version comes from <Version> in Directory.Build.props, which every project
// inherits, so lysc, the language server and the packages always report the same
// number. The build stamps the informational version with a "+<commit sha>"
// suffix; that is build provenance, not the version a user reports in a bug, so
// it is cut here. (The language server still reports the stamped string over
// lilysharp/version, where it exists precisely to identify a deployed build.)
static string VersionString()
{
    var informational = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";

    var plus = informational.IndexOf('+');
    return plus < 0 ? informational : informational[..plus];
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Error: Unknown command '{command}'");
    Console.Error.WriteLine("Run 'lysc --help' for usage.");
    return 1;
}

// Prints an option-parsing error with the standard "Run 'lysc <cmd> --help'"
// footer. One place for the per-command error footer that used to be inlined ~10x.
static int OptionError(string message, string command)
{
    Console.Error.WriteLine($"Error: {message}");
    Console.Error.WriteLine($"Run 'lysc {command} --help' for usage.");
    return 1;
}

static bool WantsHelp(string[] args) => args.Contains("-h") || args.Contains("--help");

// ============ SVG Command ============

static int RunSvg(string[] args)
{
    if (WantsHelp(args))
    {
        ShowSvgHelp();
        return 0;
    }

    var r = new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Value("score", "--score requires a score name", "--score")
        .Flag("no-embed-font", "--no-embed-font", "-n")
        .Flag("all", "--all")
        .Flag("combined", "--combined")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "svg");

    var (inputPath, outputPath, ioError) = CliParser.ResolveIo(r, ".svg");
    if (ioError != null) return OptionError(ioError, "svg");

    bool embedFont = !r.Has("no-embed-font");
    if (r.Has("all") && r.Has("combined"))
        return OptionError("--all and --combined are mutually exclusive.", "svg");
    if (r.Has("combined"))
        return ExecuteSvgCombined(inputPath!, outputPath!, embedFont);
    if (r.Has("all"))
        return ExecuteSvgAll(inputPath!, embedFont);
    return ExecuteSvg(inputPath!, outputPath!, embedFont, r.Get("score"));
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
          -n, --no-embed-font    Don't embed font (smaller file, requires font installed)
          --all                  Generate all render blocks as separate SVG files
          --combined             Stack all render blocks into ONE SVG (like a \book)
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

static int ExecuteSvg(string inputPath, string outputPath, bool embedFont, string? scoreName = null) =>
    RunOutputCommand(inputPath, scoreName, tree =>
    {
        var renderOptions = MakeSvgOptions(embedFont);
        var svg = LilySharp.Core.Svg.SvgGenerator.Generate(tree, renderOptions, scoreName);
        File.WriteAllText(outputPath, svg);
        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine(embedFont ? "  Font embedded: Yes" : "  Font embedded: No");
        return 0;
    });

static LilySharp.Core.Svg.Renderer.SvgRenderOptions MakeSvgOptions(bool embedFont)
    => embedFont
        ? LilySharp.Core.Svg.Renderer.SvgRenderOptions.Export(LilySharp.Core.Rendering.FontLocator.Find())
        : LilySharp.Core.Svg.Renderer.SvgRenderOptions.Default;

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

// LILYPOND-REF: lily/book.cc — a \book stacks every \score into one document.
static int ExecuteSvgCombined(string inputPath, string outputPath, bool embedFont) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var svg = LilySharp.Core.Svg.SvgGenerator.GenerateMultiMovement(tree, MakeSvgOptions(embedFont));
        File.WriteAllText(outputPath, svg);
        Console.WriteLine($"Created: {outputPath}");
        return 0;
    });

static int ExecuteSvgAll(string inputPath, bool embedFont) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var inputStem = Path.GetFileNameWithoutExtension(inputPath);
        var results = LilySharp.Core.Svg.SvgGenerator.GenerateAll(tree, MakeSvgOptions(embedFont), inputStem);
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
    });

// ============ PDF Command ============

static int RunPdf(string[] args)
{
    if (WantsHelp(args))
    {
        ShowPdfHelp();
        return 0;
    }

    var r = new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Value("score", "--score requires a score name", "--score")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "pdf");

    var (inputPath, outputPath, ioError) = CliParser.ResolveIo(r, ".pdf");
    if (ioError != null) return OptionError(ioError, "pdf");

    return ExecutePdf(inputPath!, outputPath!, r.Get("score"));
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

static int ExecutePdf(string inputPath, string outputPath, string? scoreName = null) =>
    RunOutputCommand(inputPath, scoreName, tree =>
    {
        var pdfBytes = PdfGenerator.Generate(tree, null, scoreName);
        File.WriteAllBytes(outputPath, pdfBytes);
        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Size: {pdfBytes.Length / 1024.0:F1} KB");
        return 0;
    });

// ============ PNG Command ============

static int RunPng(string[] args)
{
    if (WantsHelp(args))
    {
        ShowPngHelp();
        return 0;
    }

    var r = new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Value("score", "--score requires a score name", "--score")
        .Value("scale", "--scale requires a number", "--scale")
        .Flag("crop", "--crop")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "png");

    float scale = 2.0f;
    if (r.Get("scale") is { } scaleText && (!float.TryParse(scaleText, out scale) || scale <= 0))
        return OptionError("--scale must be a positive number", "png");

    var (inputPath, outputPath, ioError) = CliParser.ResolveIo(r, ".png");
    if (ioError != null) return OptionError(ioError, "png");

    return ExecutePng(inputPath!, outputPath!, scale, r.Get("score"), r.Has("crop"));
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
          --crop                 Trim whitespace to the content bounding box
          --score <name>         Render the named score block (default: the first)
          -h, --help             Show this help

        Examples:
          lysc png score.lys
          lysc png score.lys sheet.png
          lysc png --scale 3.0 score.lys    # High DPI (288 DPI)
          lysc png --scale 1.0 score.lys    # Standard DPI (96 DPI)
        """);
}

static int ExecutePng(string inputPath, string outputPath, float scale, string? scoreName = null, bool crop = false) =>
    RunOutputCommand(inputPath, scoreName, tree =>
    {
        var fontDir = LilySharp.Core.Rendering.FontLocator.Find();
        var pngOptions = new PngRenderOptions { Scale = scale, FontDirectory = fontDir };

        // One file per page, following LilyPond's PNG naming: a single page
        // keeps the requested name, multiple pages become BASE-page1.png,
        // BASE-page2.png, … (scm/ps-to-png.scm).
        var rendered = PngGenerator.GeneratePages(tree, pngOptions, scoreName);
        var pages = crop
            ? rendered.Select(CropToContent).ToList()
            : rendered.ToList();
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
    });

// Trims a PNG to the bounding box of its non-background (non-near-white) pixels,
// plus a small margin, so a tiny snippet fills the frame instead of floating in a
// page-sized sea of white. Returns the original bytes if nothing (or everything)
// is background.
static byte[] CropToContent(byte[] png, int marginPx = 8)
{
    using var bitmap = SkiaSharp.SKBitmap.Decode(png);
    if (bitmap == null) return png;
    int w = bitmap.Width, h = bitmap.Height;
    int minX = w, minY = h, maxX = -1, maxY = -1;
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = bitmap.GetPixel(x, y);
            // "Ink" = any pixel darker than near-white on any channel (ignores the
            // white/near-white page background and anti-aliasing fringe).
            if (c.Alpha > 16 && (c.Red < 240 || c.Green < 240 || c.Blue < 240))
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
    if (maxX < minX || maxY < minY) return png; // blank image

    minX = Math.Max(0, minX - marginPx);
    minY = Math.Max(0, minY - marginPx);
    maxX = Math.Min(w - 1, maxX + marginPx);
    maxY = Math.Min(h - 1, maxY + marginPx);
    int cw = maxX - minX + 1, ch = maxY - minY + 1;
    if (cw >= w && ch >= h) return png; // already tight

    using var cropped = new SkiaSharp.SKBitmap(cw, ch);
    using (var canvas = new SkiaSharp.SKCanvas(cropped))
    {
        canvas.Clear(SkiaSharp.SKColors.White);
        canvas.DrawBitmap(bitmap, new SkiaSharp.SKRect(minX, minY, maxX + 1, maxY + 1),
            new SkiaSharp.SKRect(0, 0, cw, ch));
    }
    using var img = SkiaSharp.SKImage.FromBitmap(cropped);
    using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ============ MIDI Command ============

static int RunMidi(string[] args)
{
    if (WantsHelp(args))
    {
        ShowMidiHelp();
        return 0;
    }

    var (inputPath, outputPath, error) = ParseIoOnly(args, ".mid");
    if (error != null) return OptionError(error, "midi");

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

static int ExecuteMidi(string inputPath, string outputPath) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var exporter = new MidiExporter();
        var midi = exporter.Export(tree);
        midi.Save(outputPath);
        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Tracks: {midi.Tracks.Count}");
        Console.WriteLine($"  Notes: {midi.Tracks.Skip(1).Sum(t => t.Notes.Count)}");
        return 0;
    });

// ============ VSQX Command ============

static int RunVsqx(string[] args)
{
    if (WantsHelp(args))
    {
        Console.WriteLine("""
            Convert Lily# source to a VOCALOID4 sequence (.vsqx)

            The first part carrying lyrics becomes the vocal track (Piapro
            Studio / VOCALOID4+ import this directly). Kana lyrics get
            VOCALOID phonemes; ties merge; rests become gaps.

            Usage: lysc vsqx <input.lys> [output.vsqx]
            """);
        return 0;
    }

    var (inputPath, outputPath, error) = ParseIoOnly(args, ".vsqx");
    if (error != null) return OptionError(error, "vsqx");

    return RunOutputCommand(inputPath!, null, tree =>
    {
        var doc = new LilySharp.Core.Vocaloid.VsqxExporter().Export(tree);
        doc.Save(outputPath!);
        Console.WriteLine($"Created: {outputPath}");
        return 0;
    });
}

// ============ MusicXML Command ============

static int RunXml(string[] args)
{
    if (WantsHelp(args))
    {
        ShowXmlHelp();
        return 0;
    }

    var (inputPath, outputPath, error) = ParseIoOnly(args, ".xml");
    if (error != null) return OptionError(error, "xml");

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

static int ExecuteXml(string inputPath, string outputPath) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var (parts, measures) = new MusicXmlExporter().ExportToFile(tree, outputPath);
        Console.WriteLine($"Created: {outputPath}");
        Console.WriteLine($"  Parts: {parts}");
        Console.WriteLine($"  Measures: {measures}");
        return 0;
    });

// ============ LilyPond Command ============

static int RunLy(string[] args)
{
    if (WantsHelp(args))
    {
        ShowLyHelp();
        return 0;
    }

    var (inputPath, outputPath, error) = ParseIoOnly(args, ".ly");
    if (error != null) return OptionError(error, "ly");

    return ExecuteLy(inputPath!, outputPath!);
}

static void ShowLyHelp()
{
    Console.WriteLine("""
        Convert Lily# source to LilyPond (.ly)

        Usage: lysc ly [options] <input.lys> [output.ly]

        Arguments:
          <input.lys>      Input Lily# source file
          [output.ly]      Output LilyPond file (default: input with .ly extension)

        Options:
          -o, --output <file>    Output file path
          -h, --help             Show this help

        The octave marks you wrote in the .lys are preserved verbatim: an
        `octave absolute` source is wrapped in \fixed c', a relative one in
        \relative c', so the pitches stay identical in real LilyPond.

        Examples:
          lysc ly score.lys
          lysc ly score.lys export.ly
          lysc ly -o export.ly score.lys
        """);
}

static int ExecuteLy(string inputPath, string outputPath) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var exporter = new LilySharp.Core.LilyPond.LilyPondExporter();
        var ly = exporter.Export(tree);
        File.WriteAllText(outputPath, ly);
        Console.WriteLine($"Created: {outputPath}");
        foreach (var w in exporter.Warnings)
            Console.WriteLine($"  warning: {w}");
        return 0;
    });

// ============ Import Command ============

static int RunImport(string[] args)
{
    if (WantsHelp(args))
    {
        Console.WriteLine("""
            Import MusicXML into a Lily# source file

            Usage: lysc import [options] <input.(xml|musicxml|mxl)> [output.lys]

            Reads a MusicXML score (or an .mxl zip) and writes an idiomatic Lily#
            source file that renders the same music. Import is an opinionated,
            non-unique mapping: the result is a faithful STARTING POINT to edit, not
            a byte round-trip. Anything not representable is reported, never emitted
            wrong.

            Options:
              -o, --output <file>    Output file path (default: input with .lys)
              -r, --relative         Emit relative-octave notes (default: absolute)
              -h, --help             Show this help

            Examples:
              lysc import song.xml
              lysc import song.mxl song.lys
              lysc import --relative song.xml
            """);
        return 0;
    }

    var r = new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Flag("relative", "-r", "--relative")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "import");

    var (inputPath, outputPath, ioError) = CliParser.ResolveIo(r, ".lys");
    if (ioError != null) return OptionError(ioError, "import");

    try
    {
        var bytes = File.ReadAllBytes(inputPath!);
        var (lys, report) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().ImportBytes(bytes, r.Has("relative"));
        File.WriteAllText(outputPath!, lys);

        Console.WriteLine($"Created: {outputPath}");
        if (report.HasWarnings)
        {
            Console.WriteLine($"  Imported with {report.Warnings.Count} approximation(s):");
            foreach (var w in report.Warnings)
                Console.WriteLine($"    - {w}");
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
        return 1;
    }
}

// ============ Harmonize Command ============

static int RunHarmonize(string[] args)
{
    if (WantsHelp(args))
    {
        Console.WriteLine("""
            Usage: lysc harmonize <input.lys>

            Reads the melody and key and prints a `chords harmony { … }` part — one
            diatonic chord per measure — a starting point to drop into your section
            (referenced with `staff <melody> with chords harmony`) and edit.
            """);
        return 0;
    }

    var r = new CliParser(maxPositionals: 1).Parse(args);
    if (r.Error != null) return OptionError(r.Error, "harmonize");
    if (r.Positionals.Count == 0)
    {
        Console.Error.WriteLine("Error: no input file. Try: lysc harmonize score.lys");
        return 1;
    }

    return RunOutputCommand(r.Positionals[0], null, tree =>
    {
        var block = LilySharp.Core.Harmony.ChordHarmonizer.Harmonize(tree);
        if (block == null)
        {
            Console.Error.WriteLine("No melody found to harmonize.");
            return 1;
        }
        Console.WriteLine(block);
        return 0;
    });
}

// ============ Check Command ============

static int RunCheck(string[] args)
{
    if (WantsHelp(args))
    {
        ShowCheckHelp();
        return 0;
    }

    var r = new CliParser(maxPositionals: 1)
        .Flag("pitches", "-p", "--pitches")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "check");

    if (r.Positionals.Count == 0)
        return OptionError("Input file required", "check");

    var inputPath = r.Positionals[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    return ExecuteCheck(inputPath, r.Has("pitches"));
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
        Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
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
        var (line, col) = LineColOf(source, p);
        string written = ReadPitchToken(source, p);
        Console.WriteLine($"  {line,4}:{col,-3} {written,-7} -> {e.Pitch}");
    }
    Console.WriteLine();
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

// ============ Layout Command ============

static int RunLayout(string[] args)
{
    if (WantsHelp(args))
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

    var r = new CliParser(maxPositionals: 1)
        .Flag("all", "--all")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "layout");

    if (r.Positionals.Count == 0)
        return OptionError("Input file required", "layout");

    var inputPath = r.Positionals[0];
    if (!File.Exists(inputPath))
        return OptionError($"File not found: {inputPath}", "layout");

    bool allScores = r.Has("all");
    return RunOutputCommand(inputPath, null, tree =>
    {
        Console.Write(LilySharp.Core.Svg.LayoutReport.Generate(tree, allScores));
        return 0;
    });
}

// ============ Shared Utilities ============

// Read a .lys file and parse it, first resolving any `using "..."` directives
// (relative to the file) into one combined source. The main file is the prefix, so
// its diagnostic positions are unchanged.
static (string Source, LilySharp.Core.Syntax.SyntaxTree Tree) LoadAndParse(string inputPath)
{
    var source = File.ReadAllText(inputPath);
    if (LilySharp.Core.Parser.UsingExpander.HasUsings(source))
        source = LilySharp.Core.Parser.UsingExpander.Expand(source, inputPath,
            p => File.Exists(p) ? File.ReadAllText(p) : null);
    return (source, LilySharp.Core.Syntax.SyntaxTree.Parse(source));
}

// The shared skeleton behind every file-output command: load+parse, surface
// diagnostics (abort on error), validate an optional --score name, run the
// format-specific body, and turn any exception into "Error: <message>" / exit 1.
// scoreName == null skips score validation (formats without a --score option).
static int RunOutputCommand(string inputPath, string? scoreName, Func<SyntaxTree, int> body)
{
    try
    {
        var (_, tree) = LoadAndParse(inputPath);
        if (ReportDiagnostics(tree)) return 1;
        if (!ValidateScoreName(tree, scoreName)) return 1;
        return body(tree);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
        return 1;
    }
}

// The input/output pair for the format commands whose only option is -o/--output.
static (string? InputPath, string? OutputPath, string? Error) ParseIoOnly(string[] args, string defaultExt)
{
    var r = new CliParser(maxPositionals: 2)
        .Value("output", "-o requires a file path", "-o", "--output")
        .Parse(args);
    if (r.Error != null) return (null, null, r.Error);
    return CliParser.ResolveIo(r, defaultExt);
}

// 1-based (line, column) for a source offset.
static (int Line, int Col) LineColOf(string text, int offset)
{
    int line = 1, col = 1;
    int n = Math.Min(offset, text.Length);
    for (int i = 0; i < n; i++)
    {
        if (text[i] == '\n') { line++; col = 1; }
        else col++;
    }
    return (line, col);
}

// "line,column" for a source offset — every human-facing diagnostic prints this
// instead of the raw byte offset.
static string LineCol(string text, int offset)
{
    var (line, col) = LineColOf(text, offset);
    return $"{line},{col}";
}
