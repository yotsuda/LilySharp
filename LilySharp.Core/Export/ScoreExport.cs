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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Pdf;
using LilySharp.Core.Png;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Core.Vocaloid;

namespace LilySharp.Core.Export;

/// <summary>
/// The settings a door may choose for an export. Everything else — which score, which
/// form, which parts, which staves — is the score's own and is not a setting.
/// </summary>
public sealed class ExportOptions
{
    /// <summary>SVG: embed the music font, so the file stands alone
    /// (<c>lysc svg --no-embed-font</c> turns it off).</summary>
    public bool EmbedFont { get; init; } = true;

    /// <summary>PNG: pixels per SVG-baseline pixel; 2.0 is 192 DPI (<c>lysc png --scale</c>).</summary>
    public float PngScale { get; init; } = 2.0f;

    /// <summary>PNG: trim every page to its ink plus a small margin (<c>lysc png --crop</c>).</summary>
    public bool CropPng { get; init; }

    /// <summary>LilyPond: write the <c>\paper</c> block that pins the text faces
    /// (<c>lysc ly --pin-fonts</c>; the twin's help says when).</summary>
    public bool PinFonts { get; init; }

    /// <summary>SVG, PNG, PDF: paper values laid over the paper the file says
    /// (<c>lysc svg|png|pdf --set KEY=VALUE</c>; <see cref="Semantics.PaperOverrides"/>).</summary>
    public Semantics.PaperOverrides? PaperOverrides { get; init; }

    public static ExportOptions Default { get; } = new();
}

/// <summary>
/// What one export wrote: the files (one, or one per page for a PNG of several pages),
/// the figures a console reports under them ("Tracks: 2"), and the exporter's warnings.
/// </summary>
public sealed record ExportResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings);

/// <summary>
/// ONE HOME for writing a score to a file in any format — the command line
/// (<c>lysc svg|pdf|png|midi|xml|ly|vsqx</c>), the preview's Export button and the
/// Explorer's batch export all come here with the score they chose, so the three doors
/// cannot drift apart.
/// </summary>
/// <remarks>
/// They did drift. Each door carried its own switch over the formats with its own
/// exporter settings: the Export button handed MIDI, MusicXML and the twin a null form
/// (the file's primary) while the command line's <c>--score</c> handed the score's own,
/// so a practice score whose form starts at section B exported a .mid that began with
/// section A (owner report, 2026-10-03); the button's PDF and PNG resolved a score name
/// without the fallback the SVG and the preview use; the button's SVG dropped font
/// embedding when the font folder was not found where the command line's embeds anyway.
/// A setting that is one door's alone lives in <see cref="ExportOptions"/>; a rule
/// that must be the same at every door lives here.
/// </remarks>
public static class ScoreExport
{
    /// <summary>The file extension each format writes, by the format's name
    /// (<c>lysc xml</c> is the command for <c>musicxml</c>).</summary>
    public static IReadOnlyDictionary<string, string> Extensions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["svg"] = ".svg", ["png"] = ".png", ["pdf"] = ".pdf",
            ["midi"] = ".mid", ["musicxml"] = ".xml", ["vsqx"] = ".vsqx", ["ly"] = ".ly",
        };

    /// <summary>The SVG options an export uses: the bundled font embedded as base64 so the
    /// file stands alone, or a bare reference to it when the caller asks for the smaller
    /// file. The combined (<c>\book</c>-like) SVG the command line stacks uses the same.</summary>
    public static SvgRenderOptions SvgOptions(bool embedFont, Semantics.PaperOverrides? paperSettings = null,
        Action<string>? layoutWarning = null)
        => embedFont
            ? new SvgRenderOptions { EmbedFont = true, FontDirectory = FontLocator.Find(), PaperOverrides = paperSettings, LayoutWarning = layoutWarning }
            : new SvgRenderOptions { PaperOverrides = paperSettings, LayoutWarning = layoutWarning };

    /// <summary>
    /// Writes <paramref name="score"/> (null: a file with no <c>score</c> block — its one
    /// picture, the primary form, every part) to <paramref name="outputPath"/> in
    /// <paramref name="format"/>, and says what was written.
    /// </summary>
    /// <remarks>
    /// The visual formats draw the score; MIDI, MusicXML and the LilyPond twin write ITS
    /// form — a .mid, a .musicxml and a .ly each hold one arrangement, and the arrangement a
    /// score names is the one it means (HANDOFF §3, 2026-08-17). MIDI also sounds only the
    /// parts the score shows (its spec), and the twin engraves the score's own staves (its
    /// declaration): two scores of one form differ only there.
    /// A PNG of several pages is one file per page, named as LilyPond names them
    /// (<see cref="PngPagePaths"/>).
    /// </remarks>
    public static ExportResult Write(
        SyntaxTree tree, string format, string outputPath,
        (RenderDeclarationSyntax Declaration, RenderSpec Spec)? score,
        ExportOptions? options = null)
    {
        options ??= ExportOptions.Default;
        var spec = score?.Spec;
        var form = spec?.Form;
        // What the visual formats' layout had to give up (LayoutWarnings — an over-full page).
        var layoutWarnings = new List<string>();
        switch (format)
        {
            case "svg":
                File.WriteAllText(outputPath, SvgGenerator.GenerateScore(tree, spec,
                    SvgOptions(options.EmbedFont, options.PaperOverrides, layoutWarnings.Add)));
                return new ExportResult([outputPath], [], layoutWarnings);

            case "png":
            {
                var pngOptions = new PngRenderOptions
                {
                    Scale = options.PngScale, FontDirectory = FontLocator.Find(), PaperOverrides = options.PaperOverrides,
                    LayoutWarning = layoutWarnings.Add,
                };
                var rendered = PngGenerator.GenerateScorePages(tree, spec, pngOptions);
                var pages = options.CropPng
                    ? rendered.Select(p => PngGenerator.CropToContent(p)).ToList()
                    : rendered.ToList();
                var files = PngPagePaths(outputPath, pages.Count);
                for (int p = 0; p < pages.Count; p++)
                    File.WriteAllBytes(files[p], pages[p]);
                return new ExportResult(files,
                    ["Size: " + string.Join(", ", pages.Select(Kilobytes)), $"Scale: {options.PngScale:F1}x"],
                    layoutWarnings);
            }

            case "boxes":
            {
                var pages = Rendering.Boxes.BoxesGenerator.GeneratePages(tree, spec, options.PaperOverrides,
                    layoutWarnings.Add);
                File.WriteAllText(outputPath, Rendering.Boxes.BoxesGenerator.ToJson(pages,
                    options.PaperOverrides?.StaffSpaceMm ?? Svg.Layout.LayoutOptions.DefaultStaffSpaceMm));
                return new ExportResult([outputPath],
                    [$"Pages: {pages.Count}", $"Symbols: {pages.Sum(p => p.Symbols.Count)}"], layoutWarnings);
            }

            case "pdf":
            {
                var bytes = PdfGenerator.GenerateScore(tree, spec, new PdfRenderOptions
                {
                    PaperOverrides = options.PaperOverrides, LayoutWarning = layoutWarnings.Add,
                });
                File.WriteAllBytes(outputPath, bytes);
                return new ExportResult([outputPath], [$"Size: {Kilobytes(bytes)}"], layoutWarnings);
            }

            case "midi":
            {
                var exporter = new MidiExporter { Form = form, Score = spec };
                var midi = exporter.Export(tree);
                midi.Save(outputPath);
                // A note the file writes outside MIDI's 128 keys is the one thing this
                // format loses that the page and the MusicXML keep — said in Warnings.
                return new ExportResult([outputPath],
                    [$"Tracks: {midi.Tracks.Count}", $"Notes: {midi.Tracks.Skip(1).Sum(t => t.Notes.Count)}"],
                    exporter.Warnings.ToList());
            }

            case "musicxml":
            {
                var (parts, measures) = new MusicXmlExporter { Form = form }.ExportToFile(tree, outputPath);
                return new ExportResult([outputPath], [$"Parts: {parts}", $"Measures: {measures}"], []);
            }

            case "vsqx":
                // One arrangement, no form, no score: the first part carrying lyrics.
                new VsqxExporter().Export(tree).Save(outputPath);
                return Plain(outputPath);

            case "ly":
            {
                var exporter = new LilyPondExporter
                {
                    Form = form, Score = score?.Declaration, PinFonts = options.PinFonts,
                };
                File.WriteAllText(outputPath, exporter.Export(tree));
                return new ExportResult([outputPath], [], exporter.Warnings.ToList());
            }

            default:
                throw new ArgumentException($"Unknown format: {format}");
        }
    }

    /// <summary>
    /// The files a PNG export of <paramref name="pageCount"/> pages writes: one page keeps
    /// <paramref name="outputPath"/>; several become <c>BASE-page1.png</c>,
    /// <c>BASE-page2.png</c>, … beside it, as LilyPond's PNG backend names them
    /// (LILYPOND-REF: scm/ps-to-png.scm — one file per page).
    /// </summary>
    public static IReadOnlyList<string> PngPagePaths(string outputPath, int pageCount)
    {
        if (pageCount <= 1)
            return [outputPath];
        string dir = Path.GetDirectoryName(outputPath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(outputPath);
        string ext = Path.GetExtension(outputPath);
        var names = new List<string>(pageCount);
        for (int p = 0; p < pageCount; p++)
            names.Add(Path.Combine(dir, $"{baseName}-page{p + 1}{ext}"));
        return names;
    }

    private static string Kilobytes(byte[] bytes) => $"{bytes.Length / 1024.0:F1} KB";

    private static ExportResult Plain(string path) => new([path], [], []);
}
