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
using LilySharp.Core.Export;
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

    // ★ --batch <list>: run this same command over MANY files in ONE process. Taken and
    // stripped here, before the per-command parsers, for the same reason --verbose is:
    // it belongs to the RUN, not to the command, and every command gets it at once.
    var rest = args.Skip(1).ToArray();
    if (Batch.Take(ref rest) is { } list)
    {
        if (!Batch.TakeParallel(ref rest)) return 1;
        return Batch.Run(list, first, rest, Dispatch);
    }

    return Dispatch(first, rest);
}

// The command table, reached once per run normally and once per file under --batch.
static int Dispatch(string command, string[] args) => command switch
{
    "svg" => RunSvg(args),
    "pdf" => RunPdf(args),
    "png" => RunPng(args),
    "boxes" => RunBoxes(args),
    "midi" => RunMidi(args),
    "xml" => RunXml(args),
    "ly" => RunLy(args),
    "import" => RunImport(args),
    "vsqx" => RunVsqx(args),
    "octave" => RunOctave(args),
    "harmonize" => RunHarmonize(args),
    "check" => RunCheck(args),
    "layout" => RunLayout(args),
    _ => UnknownCommand(command)
};

static void ShowHelp()
{
    Console.WriteLine($"Lily# {VersionString()} - Music notation compiler");
    Console.WriteLine("""

        Usage: lysc <command> [options] <input>
               lysc [options]

        Outputs are named for the input: song.lys writes song.svg for its main
        score and song-<alias>.svg for each other one, every score unless --score
        picks one. -d <folder> chooses where they go (default: the input's folder).

        Commands:
          svg        Convert to SVG (sheet music)
          pdf        Convert to PDF (sheet music)
          png        Convert to PNG (raster image)
          boxes      Write every drawn symbol's box as JSON (for OMR training data)
          midi       Convert to MIDI (audio)
          vsqx       Convert to VOCALOID sequence (vocal part + lyrics)

          xml        Convert to MusicXML
          ly         Convert to LilyPond (.ly) source
          import     Import MusicXML (.xml/.musicxml/.mxl) to a Lily# source file
          octave     Rewrite a Lily# file into absolute or relative octaves

          harmonize  Suggest a diatonic chord track for the melody (prints a chords part)
          check      Check syntax without output
          layout     Print a text summary of the layout (system/line breaks, bars per system)

        Global Options:
          -h, --help          Show this help
          -V, --version       Show version
          --batch <list>      Run the command over every file in <list>, in ONE process
                              (- reads the list from standard input)
          -j, --parallel <n>  Engrave n files of the batch at once (0 = one per
                              processor). Sequential by default. Not for `pdf`.
                              ⚠️ Buys less than the core count: rendering serialises
                              on the shaping lock (svg 1.3x for 5x the CPU; ly 2.2x).
                              Several --batch PROCESSES scale better than one -j N.
          --verbose, --debug  Print full stack traces on error

        Examples:
          lysc svg score.lys                    # Output: score.svg (+ score-<alias>.svg)
          lysc svg -d out score.lys             # The same, into out/
          lysc pdf score.lys                    # Output: score.pdf
          lysc midi score.lys                   # Output: score.mid
          lysc check score.lys                  # Syntax check only

        Batch: one command, many files, one process. The launch cost (~1 s, mostly font
        and JIT warm-up) is paid ONCE instead of per file, which is what makes a whole-
        corpus run practical. One file per line; '#' comments and blank lines are skipped;
        a TAB names the folder that file's outputs go to.

          lysc svg --batch books.txt            # each book -> its own .svg files
          lysc svg --batch books.txt -d out     # ...all into out/
          lysc svg --batch books.txt -j 0       # ...one per processor
          lysc check --batch books.txt          # syntax-check a whole corpus
          dir /b/s *.lys | lysc ly --batch -    # take the list from a pipe

          # books.txt
          score.lys                             # -> score.svg
          suite.lys<TAB>out                     # -> out/suite.svg, out/suite-<alias>.svg

        A file that fails does not stop the rest — the run's exit code is non-zero if
        any file failed.

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

    var r = OutputOptions()
        .Value("score", "--score requires a score name", "--score")
        .Flag("no-embed-font", "--no-embed-font", "-n")
        .Flag("combined", "--combined")
        .Value("set", "--set requires KEY=VALUE", "--set")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "svg");
    var settings = PaperOverridesOf(r, out var setError);
    if (setError != null) return OptionError("--set: " + setError, "svg");

    bool embedFont = !r.Has("no-embed-font");
    if (r.Has("combined"))
    {
        if (r.Get("score") != null)
            return OptionError("--combined and --score are mutually exclusive.", "svg");
        var (input, dir, error) = ResolveOutputs(r);
        if (error != null) return OptionError(error, "svg");
        return ExecuteSvgCombined(input!, OutputPathFor(input!, dir!, ".svg"), embedFont, settings);
    }
    var options = new ExportOptions { EmbedFont = embedFont, PaperOverrides = settings };
    return RunScoreOutputs("svg", r, ".svg",
        (tree, output) => Report(ScoreExport.Write(tree, "svg", output.Path, output.Score, options)));
}

static void ShowSvgHelp()
{
    Console.WriteLine("""
        Convert Lily# source to SVG

        Usage: lysc svg [options] <input.lys>

        Writes every score of the file: <input>.svg for the main score and
        <input>-<alias>.svg for each other one (score main "tab" -> song-tab.svg).

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score
          -n, --no-embed-font    Don't embed font (smaller file, requires font installed)
          --combined             Stack every score into ONE <input>.svg (like a \book)
          --set <KEY=VALUE>      Override a paper value (repeatable): spacingIncrement=1.6,
                                 staffStaffSpacing.basicDistance=9, leftMargin=20mm, raggedRight=false,
                                 shortestDurationSpace=3, systemsPerPage=4 (CLI_REFERENCE)
          -h, --help             Show this help

        Examples:
          lysc svg score.lys
          lysc svg -d out score.lys
          lysc svg --score tab score.lys
          lysc svg score.lys --no-embed-font
          lysc svg --combined multi-movement.lys
        """);
}

// The combined stack draws with the options a score's export uses (ScoreExport, one home).
static LilySharp.Core.Svg.Renderer.SvgRenderOptions MakeSvgOptions(bool embedFont, LilySharp.Core.Semantics.PaperOverrides? settings)
    => ScoreExport.SvgOptions(embedFont, settings);

// `--set KEY=VALUE` (repeatable, svg/png/pdf): paper values over the file's (PaperOverrides —
// LilySharp-Omr's training data engraves one piece many ways without rewriting it). Null when
// none is given; error says which setting does not read.
static LilySharp.Core.Semantics.PaperOverrides? PaperOverridesOf(CliParser.Result r, out string? error)
{
    error = null;
    var all = r.GetAll("set");
    return all.Count == 0 ? null : LilySharp.Core.Semantics.PaperOverrides.Parse(all, out error);
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

// LILYPOND-REF: lily/book.cc — a \book stacks every \score into one document.
static int ExecuteSvgCombined(string inputPath, string outputPath, bool embedFont,
    LilySharp.Core.Semantics.PaperOverrides? settings) =>
    RunOutputCommand(inputPath, null, tree =>
    {
        var svg = LilySharp.Core.Svg.SvgGenerator.GenerateMultiMovement(tree, MakeSvgOptions(embedFont, settings));
        File.WriteAllText(outputPath, svg);
        Console.WriteLine($"Created: {outputPath}");
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

    var r = OutputOptions()
        .Value("score", "--score requires a score name", "--score")
        .Value("set", "--set requires KEY=VALUE", "--set")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "pdf");
    var settings = PaperOverridesOf(r, out var setError);
    if (setError != null) return OptionError("--set: " + setError, "pdf");
    var pdfOptions = new ExportOptions { PaperOverrides = settings };

    return RunScoreOutputs("pdf", r, ".pdf",
        (tree, output) => Report(ScoreExport.Write(tree, "pdf", output.Path, output.Score, pdfOptions)));
}

static void ShowPdfHelp()
{
    Console.WriteLine("""
        Convert Lily# source to PDF

        Usage: lysc pdf [options] <input.lys>

        Writes every score of the file: <input>.pdf for the main score and
        <input>-<alias>.pdf for each other one.

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score
          --set <KEY=VALUE>      Override a paper value (repeatable): spacingIncrement=1.6,
                                 staffStaffSpacing.basicDistance=9, leftMargin=20mm, raggedRight=false,
                                 shortestDurationSpace=3, systemsPerPage=4 (CLI_REFERENCE)
          -h, --help             Show this help

        Examples:
          lysc pdf score.lys
          lysc pdf -d out score.lys
          lysc pdf --score tab score.lys
        """);
}

// ============ Boxes Command ============

// LilySharp-Omr's proposal of 2026-10-02, P5: every drawn symbol's ink box, page by page.
static int RunBoxes(string[] args)
{
    if (WantsHelp(args))
    {
        ShowBoxesHelp();
        return 0;
    }

    var r = OutputOptions()
        .Value("score", "--score requires a score name", "--score")
        .Value("set", "--set requires KEY=VALUE", "--set")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "boxes");
    var settings = PaperOverridesOf(r, out var setError);
    if (setError != null) return OptionError("--set: " + setError, "boxes");
    var options = new ExportOptions { PaperOverrides = settings };
    return RunScoreOutputs("boxes", r, ".boxes.json",
        (tree, output) => Report(ScoreExport.Write(tree, "boxes", output.Path, output.Score, options)));
}

static void ShowBoxesHelp()
{
    Console.WriteLine("""
        Write every drawn symbol's box as JSON

        Usage: lysc boxes [options] <input.lys>

        Writes <input>.boxes.json for the main score and <input>-<alias>.boxes.json for
        each other one: per page, every symbol the SVG/PNG draws — its kind (notehead,
        stem, staffLine, tie, lyric, ...), its ink box in staff spaces (origin top-left,
        Y down), its source offset (data-pos) and staff — and each bar's printed number
        and box. Takes the same --set as svg/png, so the boxes match those pictures.

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score
          --set <KEY=VALUE>      Override a paper value (repeatable), as for svg/png
          -h, --help             Show this help

        Examples:
          lysc boxes song.lys
          lysc boxes --set systemsPerPage=4 song.lys
        """);
}



static int RunPng(string[] args)
{
    if (WantsHelp(args))
    {
        ShowPngHelp();
        return 0;
    }

    var r = OutputOptions()
        .Value("score", "--score requires a score name", "--score")
        .Value("scale", "--scale requires a number", "--scale")
        .Flag("crop", "--crop")
        .Value("set", "--set requires KEY=VALUE", "--set")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "png");
    var settings = PaperOverridesOf(r, out var setError);
    if (setError != null) return OptionError("--set: " + setError, "png");

    float scale = 2.0f;
    if (r.Get("scale") is { } scaleText && (!float.TryParse(scaleText, out scale) || scale <= 0))
        return OptionError("--scale must be a positive number", "png");
    bool crop = r.Has("crop");

    // One file per page, named as LilyPond names them (ScoreExport.PngPagePaths).
    var options = new ExportOptions { PngScale = scale, CropPng = crop, PaperOverrides = settings };
    return RunScoreOutputs("png", r, ".png",
        (tree, output) => Report(ScoreExport.Write(tree, "png", output.Path, output.Score, options)));
}

static void ShowPngHelp()
{
    Console.WriteLine("""
        Convert Lily# source to PNG

        Usage: lysc png [options] <input.lys>

        Writes every score of the file: <input>.png for the main score and
        <input>-<alias>.png for each other one; a score of several pages writes
        NAME-page1.png, NAME-page2.png, …

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score
          --scale <factor>       Scale factor (default: 2.0 = 192 DPI)
          --crop                 Trim whitespace to the content bounding box
          --set <KEY=VALUE>      Override a paper value (repeatable): spacingIncrement=1.6,
                                 staffStaffSpacing.basicDistance=9, leftMargin=20mm, raggedRight=false,
                                 shortestDurationSpace=3, systemsPerPage=4 (CLI_REFERENCE)
          -h, --help             Show this help

        Examples:
          lysc png score.lys
          lysc png -d out score.lys
          lysc png --scale 3.0 score.lys    # High DPI (288 DPI)
          lysc png --scale 1.0 score.lys    # Standard DPI (96 DPI)
        """);
}

// ============ MIDI Command ============

static int RunMidi(string[] args)
{
    if (WantsHelp(args))
    {
        ShowMidiHelp();
        return 0;
    }

    return RunFormOutput(args, "midi", ".mid",
        (tree, output) => Report(ScoreExport.Write(tree, "midi", output.Path, output.Score)));
}

static void ShowMidiHelp()
{
    Console.WriteLine("""
        Convert Lily# source to MIDI

        Usage: lysc midi [options] <input.lys>

        Writes every score's form: <input>.mid for the main score and
        <input>-<alias>.mid for each other one. A .mid holds ONE arrangement, so
        a file of several movements writes one file each — which is what
        LilyPond does too (two \score blocks with \midi { } write ts.mid and
        ts-1.mid).

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score's form
          -h, --help             Show this help

        Examples:
          lysc midi score.lys
          lysc midi -d out score.lys
          lysc midi --score movement2 multi-movement.lys
        """);
}

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

            Usage: lysc vsqx [options] <input.lys>

            Writes <input>.vsqx.

            Options:
              -d, --out-dir <folder> Write into this folder (default: the input's folder)
              -h, --help             Show this help
            """);
        return 0;
    }

    var r = OutputOptions().Parse(args);
    if (r.Error != null) return OptionError(r.Error, "vsqx");
    var (inputPath, dir, error) = ResolveOutputs(r);
    if (error != null) return OptionError(error, "vsqx");
    string outputPath = OutputPathFor(inputPath!, dir!, ".vsqx");

    return RunOutputCommand(inputPath!, null,
        tree => Report(ScoreExport.Write(tree, "vsqx", outputPath, null)));
}

// ============ MusicXML Command ============

static int RunXml(string[] args)
{
    if (WantsHelp(args))
    {
        ShowXmlHelp();
        return 0;
    }

    return RunFormOutput(args, "xml", ".xml",
        (tree, output) => Report(ScoreExport.Write(tree, "musicxml", output.Path, output.Score)));
}

static void ShowXmlHelp()
{
    Console.WriteLine("""
        Convert Lily# source to MusicXML

        Usage: lysc xml [options] <input.lys>

        Writes every score's form: <input>.xml for the main score and
        <input>-<alias>.xml for each other one. One document holds ONE
        arrangement, so a file of several movements writes one file each.

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score's form
          -h, --help             Show this help

        Examples:
          lysc xml score.lys
          lysc xml -d out score.lys
          lysc xml --score movement2 multi-movement.lys
        """);
}

// ============ LilyPond Command ============

static int RunLy(string[] args)
{
    if (WantsHelp(args))
    {
        ShowLyHelp();
        return 0;
    }

    // `--pin-fonts` belongs to the twin alone, not to the one-form-per-file shape the
    // three exporters share, so it is taken here before RunFormOutput's strict parser —
    // the same reason --verbose and --batch are stripped before the per-command parsers.
    bool pinFonts = args.Contains("--pin-fonts");
    var rest = pinFonts ? args.Where(a => a != "--pin-fonts").ToArray() : args;
    var options = new ExportOptions { PinFonts = pinFonts };
    return RunFormOutput(rest, "ly", ".ly",
        (tree, output) => Report(ScoreExport.Write(tree, "ly", output.Path, output.Score, options)));
}

static void ShowLyHelp()
{
    Console.WriteLine("""
        Convert Lily# source to LilyPond (.ly)

        Usage: lysc ly [options] <input.lys>

        Writes every score: <input>.ly for the main score and <input>-<alias>.ly
        for each other one.

        Options:
          -d, --out-dir <folder> Write into this folder (default: the input's folder)
          --score <name>         Write only the named score
          --pin-fonts            Write a \paper block that pins the text faces
                                 (property-defaults.fonts.serif / .sans) so that
                                 a `lilypond -dbackend=svg` run of the twin uses
                                 the same faces as its pdf/png would
          -h, --help             Show this help

        The octave marks you wrote in the .lys are preserved verbatim: an
        `octave absolute` source is wrapped in \fixed c', a relative one in
        \relative c', so the pitches stay identical in real LilyPond.

        The twin writes no \paper block by default. LilyPond 2.26 replaces the
        serif and sans faces with generic names under its svg backend only, and
        fontconfig then picks a machine-dependent font, so text widths measured
        from a twin's svg drift from its pdf. --pin-fonts writes the two lines the
        LP-fidelity probes carry for that reason; use it when you measure a twin
        through svg, not when you print it.

        The twin writes one \score, so a file of several movements writes one .ly
        each.

        Examples:
          lysc ly score.lys
          lysc ly -d out score.lys
          lysc ly --score movement2 multi-movement.lys
        """);
}

// ============ Import Command ============

static int RunImport(string[] args)
{
    if (WantsHelp(args))
    {
        Console.WriteLine("""
            Import MusicXML into a Lily# source file

            Usage: lysc import [options] <input.(xml|musicxml|mxl)>

            Reads a MusicXML score (or an .mxl zip) and writes an idiomatic Lily#
            source file, <input>.lys, that renders the same music. Import is an
            opinionated, non-unique mapping: the result is a faithful STARTING POINT
            to edit, not a byte round-trip. Anything not representable is reported,
            never emitted wrong.

            Options:
              -d, --out-dir <folder> Write into this folder (default: the input's folder)
              -r, --relative         Emit relative-octave notes (default: absolute)
              -h, --help             Show this help

            Examples:
              lysc import song.xml
              lysc import -d books song.mxl
              lysc import --relative song.xml
            """);
        return 0;
    }

    var r = OutputOptions()
        .Flag("relative", "-r", "--relative")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "import");

    var (inputPath, dir, ioError) = ResolveOutputs(r);
    if (ioError != null) return OptionError(ioError, "import");

    try
    {
        var bytes = File.ReadAllBytes(inputPath!);
        var (lys, report) = new LilySharp.Core.MusicXmlImport.MusicXmlImporter().ImportBytes(bytes, r.Has("relative"));
        string outputPath = OutputPathFor(inputPath!, dir!, ".lys");
        File.WriteAllText(outputPath, lys);

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

// ============ Octave Command ============

static int RunOctave(string[] args)
{
    if (WantsHelp(args))
    {
        Console.WriteLine("""
            Usage: lysc octave (--absolute | --relative) [options] <input.lys>

            Rewrites a Lily# source file into the other octave mode, keeping every
            note at the pitch it sounds now: the `octave absolute` / `octave relative`
            directives are replaced, and each note's ' and , marks are recomputed.
            Nothing else in the file changes. The result is compiled and compared
            note by note before it is written; a file that cannot be converted
            exactly is left alone and the reason is printed.

            Without -i or -d the result goes to standard output.

            Options:
              -a, --absolute         Convert to `octave absolute` (bare c = C4)
              -r, --relative         Convert to relative octaves (the default mode)
              -i, --in-place         Overwrite the input
              -d, --out-dir <folder> Write <input>.lys into this folder
              -h, --help             Show this help

            Examples:
              lysc octave --absolute song.lys             # print the absolute version
              lysc octave --relative -i song.lys          # convert in place
              lysc octave --absolute -d absolute song.lys # absolute/song.lys
            """);
        return 0;
    }

    var r = OutputOptions()
        .Flag("absolute", "-a", "--absolute")
        .Flag("relative", "-r", "--relative")
        .Flag("in-place", "-i", "--in-place")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "octave");
    if (r.Has("absolute") == r.Has("relative"))
        return OptionError("give exactly one of --absolute or --relative", "octave");
    if (r.Has("in-place") && r.Get("out-dir") != null)
        return OptionError("--in-place and --out-dir are mutually exclusive.", "octave");
    var (inputPath, dir, ioError) = ResolveOutputs(r);
    if (ioError != null) return OptionError(ioError, "octave");
    string? outputPath = r.Has("in-place") ? inputPath
        : r.Get("out-dir") != null ? OutputPathFor(inputPath!, dir!, ".lys")
        : null;

    try
    {
        var source = File.ReadAllText(inputPath!);
        var target = r.Has("absolute")
            ? LilySharp.Core.Editing.OctaveMode.Absolute
            : LilySharp.Core.Editing.OctaveMode.Relative;
        var result = LilySharp.Core.Editing.OctaveModeConverter.Convert(source, target);
        if (result.NewText == null)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return 1;
        }
        if (outputPath == null)
        {
            Console.Write(result.NewText);
            return 0;
        }
        File.WriteAllText(outputPath, result.NewText);
        Console.WriteLine($"Created: {outputPath} ({result.ChangedPitches} note(s) re-marked)");
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
        .Flag("todo-as-error", "--todo-as-error")
        .Flag("no-todo", "--no-todo")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, "check");
    if (r.Has("todo-as-error") && r.Has("no-todo"))
        return OptionError("--todo-as-error and --no-todo cannot be used together", "check");

    if (r.Positionals.Count == 0)
        return OptionError("Input file required", "check");

    var inputPath = r.Positionals[0];
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Error: File not found: {inputPath}");
        return 1;
    }

    return ExecuteCheck(inputPath, r.Has("pitches"),
        r.Has("todo-as-error") ? TodoReport.AsError : r.Has("no-todo") ? TodoReport.Silent : TodoReport.AsWarning);
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
          --todo-as-error  Report each @todo mark as an error (exit code 1
                           while any is left)
          --no-todo        Do not report @todo marks
          -h, --help       Show this help

        Examples:
          lysc check score.lys
          lysc check score.lys --pitches
          lysc check score.lys --todo-as-error
        """);
}

static int ExecuteCheck(string inputPath, bool showPitches = false, TodoReport todos = TodoReport.AsWarning)
{
    try
    {
        var (source, tree, usingDiagnostics) = LoadAndParse(inputPath);
        var allDiagnostics = CollectDiagnostics(tree, usingDiagnostics);
        // The @todo marks themselves (LYS4026), not a malformed or repeated one.
        if (todos == TodoReport.Silent)
            allDiagnostics = allDiagnostics.Where(d => d.Code != DiagnosticCodes.TodoMark).ToList();
        bool IsError(Diagnostic d) => d.Severity == DiagnosticSeverity.Error
                                      || (todos == TodoReport.AsError && d.Code == DiagnosticCodes.TodoMark);

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
            var severity = IsError(diag) ? "error" : diag.Severity switch
            {
                DiagnosticSeverity.Warning => "warning",
                _ => "info"
            };
            if (IsError(diag)) hasErrors = true;
            Console.WriteLine($"{inputPath}({LineCol(source, diag.Span.Start)}): {severity}: {diag.Message}");
            // The places the diagnostic is about besides its own (Diagnostic.Related), as
            // `note:` lines under it — indented, and never counted as a warning of their own.
            foreach (var related in diag.Related)
                Console.WriteLine($"  {inputPath}({LineCol(source, related.Span.Start)}): note: {related.Message}");
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
    // The SAME collect the validators run, because this report is only worth reading if
    // it resolves pitches the way the page will. It used to build its own bare
    // `new MeasureCollector().Collect(tree)` — no RenderSpec — and that is a different
    // answer, not a cheaper one: with no spec the relative-octave chain runs once through
    // the tree in source order, so a section's SECOND part block inherited the first
    // one's chain instead of starting at its own clef's anchor.
    //
    // ⚠️ It reported the bass part of `test/grandstaff-high-bass` starting at C6.
    // Swapping the two part blocks moved that to C4 while the rendered SVG stayed
    // character-identical (data-pos masked): the picture does not depend on the order,
    // the report did. That matters more than one wrong line, because RULES §5.3 判定法⑶
    // tells every session to run THIS command over a synthesized book before filing
    // anything about it.
    // EVERY score, not just the first, folded onto the WRITTEN position. Both halves of
    // that were measured over 300 books before being written:
    //   · first-score-only loses coverage — `test/cue-notes` reports 26 notes when all
    //     its scores are walked and 8 when only the first is
    //   · folding on position is what stops a tab book counting twice: `tab X` and
    //     `staff X` render the same written note, so without the fold every tab fixture
    //     doubled (e.g. tab-percent-repeat 0 -> 64 vs 0 -> 32)
    // A score that will not collect is skipped rather than fatal; the message below only
    // appears when NONE of them collected.
    var trace = LilySharp.Core.Semantics.ResolvedPitches.ForFile(tree);
    if (trace == null)
    {
        Console.WriteLine("(could not resolve pitches: no score collected — "
            + "fix the errors above first)");
        return;
    }

    // ⚠️ Name the scope. The answer is what the SCORES draw, so a part no score renders
    // is not in this list — a reader who wrote more notes than they see here has to be
    // told why rather than left to wonder. `ResolvedPitches` carries the rest of the
    // reasoning, and the measurements behind it.
    Console.WriteLine($"Resolved pitches ({trace.Count}) — as the scores render them:");
    foreach (var e in trace)
    {
        // The token's span starts at its leading trivia (indent/newline); advance
        // to the actual pitch so the line:col and written token line up.
        int p = e.Position;
        while (p < source.Length && char.IsWhiteSpace(source[p])) p++;
        var (line, col) = LineColOf(source, p);
        string written = LilySharp.Core.Semantics.ResolvedPitches.ReadPitchToken(source, p);
        Console.WriteLine($"  {line,4}:{col,-3} {written,-7} -> {e.Pitch}");
    }
    Console.WriteLine();
}

/// <summary>
/// Combines syntax-tree diagnostics with semantic-validator diagnostics
/// (e.g. undefined variable / phrase / section references).
/// </summary>
static IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> CollectDiagnostics(
    SyntaxTree tree, IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> usingDiagnostics)
{
    // Include resolution first: `using "x.lys"` naming a file that cannot be read is the
    // CAUSE of the undefined-part and undefined-section errors the validators are about to
    // report, and reading it first puts it above them.
    var combined = new List<LilySharp.Core.Syntax.Diagnostic>(usingDiagnostics);
    // Parser diagnostics plus every semantic validator (single shared registry, so
    // the CLI and the LSP can never diverge on which validators run).
    combined.AddRange(tree.Diagnostics);
    combined.AddRange(LilySharp.Core.Semantics.SemanticValidation.Run(tree));
    return combined;
}

/// <summary>
/// Prints every diagnostic (parser AND semantic) to stderr, then reports whether the
/// caller should abort. EVERY output path (svg/pdf/png/midi/xml/ly) goes through this,
/// so a semantic error — an undefined variable, a measure overflow — can't be silently
/// dropped for one format while it blocks another. Warnings are surfaced but don't abort.
/// </summary>
static bool ReportDiagnostics(
    SyntaxTree tree, IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> usingDiagnostics)
{
    var all = CollectDiagnostics(tree, usingDiagnostics);
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
// Usings are returned alongside the tree rather than stashed, because they cannot come
// from a semantic validator: deciding whether `using "x.lys"` resolves needs the base
// path and the file reader, and a SyntaxTree carries neither.
static (string Source, LilySharp.Core.Syntax.SyntaxTree Tree,
        IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> UsingDiagnostics)
    LoadAndParse(string inputPath)
{
    var source = File.ReadAllText(inputPath);
    IReadOnlyList<LilySharp.Core.Syntax.Diagnostic> usingDiagnostics = [];
    if (LilySharp.Core.Parser.UsingExpander.HasUsings(source))
    {
        source = LilySharp.Core.Parser.UsingExpander.Expand(source, inputPath,
            p => File.Exists(p) ? File.ReadAllText(p) : null,
            out usingDiagnostics);
    }
    return (source, LilySharp.Core.Syntax.SyntaxTree.Parse(source), usingDiagnostics);
}

// The shared skeleton behind every file-output command: load+parse, surface
// diagnostics, validate an optional --score name, run the format-specific body, and
// turn any exception into "Error: <message>" / exit 1.
// scoreName == null skips score validation (formats without a --score option).
//
// BEST EFFORT, deliberately: an error is REPORTED and sets the exit code, but it does
// not stop the render. Severity and "may this produce output" are different questions,
// and most diagnostics answer only the first — an unsupported `override` (LYS1029), a
// stray token in a part header (LYS0025), a duplicate property (LYS7003) all leave a
// score that engraves perfectly. Refusing to write anything for those meant a reader
// porting a LilyPond file could not see one page until every unsupported line was gone.
// The parser recovers by dropping the tokens it cannot place, so what DID parse is what
// gets drawn. This is the policy the LSP preview has always had (GetSvg: "a tree with
// parse errors still renders ... the error text rides along for the preview's banner"),
// and the two now agree.
//
// What still stops: a render that THROWS (the catch below — no file is written), and a
// --score naming something the file does not define (nothing to draw).
// The exit code is unchanged, so scripts and CI still fail on an error.
static int RunOutputCommand(string inputPath, string? scoreName, Func<SyntaxTree, int> body)
{
    try
    {
        var (_, tree, usingDiagnostics) = LoadAndParse(inputPath);
        bool hasErrors = ReportDiagnostics(tree, usingDiagnostics);
        if (!ValidateScoreName(tree, scoreName)) return 1;
        int result = body(tree);
        if (hasErrors && result == 0)
            Console.Error.WriteLine(
                "  (written anyway, from the part of the file that parsed - fix the errors "
                + "above before trusting it)");
        return hasErrors ? 1 : result;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(CliParser.Verbose ? ex.ToString() : $"Error: {ex.Message}");
        return 1;
    }
}

// The console's account of one export (ScoreExport.Write): every file it wrote, the
// figures under it, the exporter's warnings. The writing itself is not the CLI's — the
// preview's Export button and the batch export write through the same home.
static int Report(ExportResult result)
{
    foreach (var file in result.Files)
        Console.WriteLine($"Created: {file}");
    foreach (var note in result.Notes)
        Console.WriteLine($"  {note}");
    foreach (var warning in result.Warnings)
        Console.WriteLine($"  warning: {warning}");
    return 0;
}

// ============ One form per file: the shape `midi`, `xml` and `ly` share ============
//
// A .mid, a .musicxml and a .ly each carry ONE arrangement, while a .lys may declare
// several (`form movement1 … form movement2 …`, each named by its own `score`). The page
// has had three doors to them since long before — `--score NAME`, `--all`, `--combined` —
// and these three had none: they wrote the primary form and said nothing about the rest.
// Decided 2026-08-17 (HANDOFF §3): warn, and give them `--score` / `--all` too.
//
// ⚠️ `--combined` is deliberately NOT here. Stacking movements onto one page is a
// LAYOUT (LilyPond's \book); a .mid holding three movements back to back is a different
// PIECE. LilyPond agrees by construction: two \score blocks with \midi { } write ts.mid
// and ts-1.mid, two files (2.26.0, measured 2026-08-17).
//
// ⚠️ The selector is a SCORE name and the unit written is its FORM, because that is what
// `--score` already means for svg/pdf/png — one word, one meaning. Two scores naming one
// form therefore write the same music under two names, exactly as svg does.
// The score's DECLARATION goes along too: its staves are not the form's (a book's
// `score main`, `score main "both"` and `score main "tab"` share one form), and the ly
// twin engraves them. midi and xml write the form's music and take no staves from it.
static int RunFormOutput(
    string[] args, string verb, string defaultExt,
    Func<SyntaxTree, ScoreOutput, int> write)
{
    var r = OutputOptions()
        .Value("score", "--score requires a score name", "--score")
        .Parse(args);
    if (r.Error != null) return OptionError(r.Error, verb);

    return RunScoreOutputs(verb, r, defaultExt, (tree, output) =>
    {
        int rc = write(tree, output);
        // A file with no `score` block writes its primary form alone.
        if (rc == 0 && output.Score == null) WarnFormsLeftOut(tree);
        return rc;
    });
}

// ⚠️ "If you drop something, say so in Warnings" (HANDOFF §2F). One file is one form, so
// a scoreless book of several forms loses all but the primary one — in silence until
// 2026-08-17, when `lysc midi` on the tree's one three-movement book wrote 40 notes and
// reported nothing. A book with scores writes every score's form, so only this case is left.
static void WarnFormsLeftOut(SyntaxTree tree)
{
    var forms = LilySharp.Core.Semantics.ScoreForms.All(tree.GetRoot());
    if (forms.Count <= 1) return;
    var chosen = LilySharp.Core.Semantics.ScoreForms.Primary(tree.GetRoot());
    var left = forms.Where(f => !ReferenceEquals(f, chosen)).Select(f => f.NameText).ToList();
    if (left.Count == 0) return;
    Console.WriteLine($"  warning: this file declares {forms.Count} forms and no score — "
        + $"wrote '{chosen?.NameText}', left out {string.Join(", ", left)} "
        + "(declare a score for each to write them all)");
}

// ============ Where the outputs go ============
//
// ★ AN OUTPUT IS NAMED FOR ITS BOOK, NEVER BY THE CALLER (user decision 2026-09-26):
// `<input stem>` for the main score and `<input stem>-<alias>` for every other one
// (RenderSpec.ResolveOutputStem), in the folder `-d/--out-dir` names — the input's own
// folder when it names none. Every score is written unless `--score` picks one. A name typed
// on the command line could say anything; this one always says which book and which score a
// file came from. And the old default wrote the FIRST score only: 92 of the owner's 332 bass
// books declare two to four (`score main`, `"both"`, `"tab"`), and `pdf`/`png` had no way to
// write the rest at all.

static string RemovedOutputMessage() =>
    "output names are fixed — <input>.<ext> for the main score, <input>-<alias>.<ext> for "
    + "each other — so -o/--output and the output argument are gone; choose the folder with -d <folder>";

// The options every file-writing command shares. `-o` is still RECOGNISED, only to say why
// it went (an old script gets the new spelling rather than "Unknown option"); `--all` the
// same, since writing every score is now what a command does.
static CliParser OutputOptions() => new CliParser(maxPositionals: 2)
    .Value("out-dir", "-d requires a folder", "-d", "--out-dir")
    .Value("output", RemovedOutputMessage(), "-o", "--output")
    .Flag("all", "--all");

// The input, and the folder the outputs go to ("" = the working directory, for an input
// named without one — the file then lands beside it, as it always did).
static (string? Input, string? Dir, string? Error) ResolveOutputs(CliParser.Result r)
{
    if (r.Get("output") != null || r.Positionals.Count > 1)
        return (null, null, RemovedOutputMessage());
    if (r.Has("all"))
        return (null, null, "--all is gone: every score is written unless --score picks one");
    if (r.Positionals.Count == 0)
        return (null, null, "Input file required");
    string input = r.Positionals[0];
    if (!File.Exists(input))
        return (null, null, $"File not found: {input}");
    return (input, r.Get("out-dir") ?? Path.GetDirectoryName(input) ?? "", null);
}

// `<dir>/<input stem><ext>`, making the folder — the one name an output that is not a
// score's (vsqx, a --combined stack) can have.
static string OutputPathFor(string input, string dir, string ext)
{
    if (dir.Length > 0) Directory.CreateDirectory(dir);
    return Path.Combine(dir, Path.GetFileNameWithoutExtension(input) + ext);
}

// Writes every score (or the one --score names) through `write`, one fixed name each.
static int RunScoreOutputs(string verb, CliParser.Result r, string ext,
    Func<SyntaxTree, ScoreOutput, int> write)
{
    var (input, dir, error) = ResolveOutputs(r);
    if (error != null) return OptionError(error, verb);
    string? scoreName = r.Get("score");
    return RunOutputCommand(input!, scoreName, tree =>
    {
        var outputs = ScoreOutputs(tree, input!, dir!, scoreName, ext);
        if (outputs == null) return 1;
        foreach (var output in outputs)
        {
            int rc = write(tree, output);
            if (rc != 0) return rc;
        }
        return 0;
    });
}

static List<ScoreOutput>? ScoreOutputs(SyntaxTree tree, string input, string dir, string? scoreName, string ext)
{
    var scores = scoreName == null
        ? LilySharp.Core.Svg.Collector.RenderSpecParser.FindAllDeclared(tree).ToList()
        : LilySharp.Core.Svg.Collector.RenderSpecParser.FindDeclaredByName(tree, scoreName) is { } one
            ? [one]
            : [];
    if (dir.Length > 0) Directory.CreateDirectory(dir);
    string stem = Path.GetFileNameWithoutExtension(input);
    if (scores.Count == 0)
        return [new ScoreOutput(Path.Combine(dir, stem + ext), null)];

    var outputs = scores
        .Select(s => new ScoreOutput(Path.Combine(dir, s.Spec.ResolveOutputStem(stem) + ext), (s.Declaration, s.Spec)))
        .ToList();
    // Two scores with one name would write one file twice, the second over the first.
    var clash = outputs.GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
    if (clash != null)
    {
        Console.Error.WriteLine($"Error: {clash.Count()} scores would all be written to {clash.Key} — "
            + "give each its own alias (score main \"tab\").");
        return null;
    }
    return outputs;
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

/// <summary>One output of one score: where it goes, and the score with its declaration
/// (null for a file with no <c>score</c> block, which writes its one rendering under the
/// input's own stem) — what ScoreExport.Write takes.</summary>
sealed record ScoreOutput(string Path,
    (LilySharp.Core.Syntax.RenderDeclarationSyntax Declaration, LilySharp.Core.Svg.Collector.RenderSpec Spec)? Score);

/// <summary>What <c>lysc check</c> does with a <c>@todo</c> mark (LYS4026):
/// <c>--todo-as-error</c>, <c>--no-todo</c>, or the warning it is.</summary>
enum TodoReport { AsWarning, AsError, Silent }
