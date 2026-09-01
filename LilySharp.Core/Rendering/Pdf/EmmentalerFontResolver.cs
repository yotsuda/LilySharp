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

using System.Collections.Concurrent;
using PdfSharpCore.Fonts;

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>
/// PdfSharpCore font resolver that loads Emmentaler from
/// <c>LilySharp.Core/Fonts/</c> (copied to output as content) and falls
/// back to PdfSharpCore's default resolver for everything else.
/// </summary>
/// <remarks>
/// Family name <c>"Emmentaler"</c> resolves to <c>emmentaler-20.otf</c>;
/// <c>"Emmentaler-Brace"</c> resolves to <c>emmentaler-brace.otf</c>.
/// PdfSharpCore reads the OTF/TTF bytes via <see cref="GetFont"/> and
/// embeds them in the produced PDF.
/// </remarks>
internal sealed class EmmentalerFontResolver : IFontResolver
{
    private readonly string? _fontDirectory;
    private readonly IFontResolver? _fallback;

    // The document's configured text faces — every name a `font` directive bound, and,
    // when it asked to EMBED and the licence allows, that face's bytes loaded from the
    // system via SkiaSharp. Set per-document by PdfDocumentContext.
    // ⚠️ A SET, NOT ONE NAME, since 2026-08-18: `fonts { }` binds a face per text role, so
    // one document can carry several. When Bytes is null the face is NOT embedded — it
    // resolves to the bundled face of its family instead, so a non-`embedded` document
    // never silently embeds a system font.
    // ⚠️ AN IMMUTABLE SNAPSHOT, SWAPPED WHOLE, for the reason its neighbour _fallbackFaces
    // states two fields down: THIS RESOLVER IS A PROCESS GLOBAL (PdfSharpCore's
    // GlobalFontSettings.FontResolver — see PdfDocumentContext.EnsureFontResolver), so every
    // document alive in the process shares this one instance. SetTextFonts used to Clear()
    // and refill a plain Dictionary in place, which two documents built at once corrupt:
    //   System.InvalidOperationException : Operations that change non-concurrent collections
    //   must have exclusive access.
    // MEASURED 2026-09-01 (session 317): the parallelised suite threw exactly that out of
    // Dictionary.TryInsert under SetTextFonts, red in
    // BackendKerningTests.PdfPlacesTextWhereTheLayoutReservedItForANamedFace and green on the
    // next run — an intermittent that is NOT the shaping crash fixed in the same session.
    // Building a fresh map and publishing it with one reference write also closes the window
    // where a reader saw the map CLEARED but not yet refilled, which no lock around the
    // writer alone would have.
    // ⚠️ WHAT THIS DOES NOT FIX, deliberately: two documents with DIFFERENT `fonts { }` still
    // overwrite each other's faces, because the resolver they share has room for one answer.
    // That is the pre-existing limitation PdfDocumentContext.EnsureFontResolver already
    // records; it goes away only by keying the faces per document, which PdfSharpCore's
    // one-resolver-per-process API does not invite. HANDOFF §2 carries it.
    private volatile IReadOnlyDictionary<string, ConfiguredTextFace> _textFaces =
        new Dictionary<string, ConfiguredTextFace>(StringComparer.OrdinalIgnoreCase);

    /// <summary>A face a <c>font</c> directive named: its program when embedding, and the
    /// bundled family it stands in for when not.</summary>
    /// <remarks>
    /// <paramref name="Sans"/> comes from the ROLE the name was bound to — chord symbols
    /// are the only sans role — and decides the stand-in, so a non-embedded
    /// <c>chordName "Georgia"</c> falls back to the Heros the layout measured rather than
    /// to Schola.
    /// <para>
    /// ⚠️ LILYSHARP-OWN: ONE NAME BOUND TO BOTH FAMILIES keeps the FIRST role's answer.
    /// LilyPond has no counterpart because it has no such collision — there, a grob names
    /// its own font and nothing has to reconcile two roles that named the same one.
    /// The fold GOES AWAY by keying faces on (name, family) instead of name, which costs
    /// embedding the same program twice to record a distinction that only shows when the
    /// face is ABSENT and the two roles then want different stand-ins.
    /// Observed by <c>EmmentalerFontResolverTests
    /// .OneNameBoundToBothFamilies_KeepsTheFirstRolesStandIn</c> — written because this
    /// remark first said "nothing observes it", which is a sentence to fix rather than to
    /// record.
    /// </para>
    /// </remarks>
    private readonly record struct ConfiguredTextFace(byte[]? Bytes, bool Sans);

    // Per-codepoint SYSTEM fallback faces, keyed by the face name below. The bundled text
    // faces are Latin-only, so a CJK title has no glyph in them and PdfSharpCore would emit
    // .notdef for every character — the text simply vanishes from the PDF (the SVG and PNG
    // backends never showed this because both resolve a covering face at draw time:
    // TextFontMetrics.MissingGlyphAdvance's remark, PngDrawingContext.SegmentByTypeface).
    // PdfDrawingContext resolves the covering face and registers its bytes here; from
    // PdfSharpCore's side it is then an ordinary face to subset and embed.
    // ⚠️ Concurrent because the renderer registers while PdfSharpCore reads.
    private readonly ConcurrentDictionary<string, FallbackFace> _fallbackFaces = new(StringComparer.Ordinal);

    /// <summary>A registered fallback: its font program, and whether the emphasis has to be
    /// SYNTHESISED because the family ships no such face (Yu Gothic UI has a bold but no
    /// italic).</summary>
    /// <remarks>
    /// ⚠️ THE SIMULATION FLAGS ARE INERT IN PdfSharpCore 1.3.65 — measured 2026-08-11, by
    /// exporting the same score with and without them: 113 bytes differ, and two runs of ONE
    /// build differ by 114 (the creation date), so the flags changed nothing. They are set
    /// because they are the truthful answer to what the resolver is asked, not because they
    /// currently move the page. What that costs today: a CJK composer line, italic in the
    /// SVG and PNG backends (Skia obliques it), stands upright in the PDF.
    /// </remarks>
    private readonly record struct FallbackFace(byte[] Bytes, bool SimulateBold, bool SimulateItalic);

    /// <summary>The face name a registered fallback family answers to. Style is IN the name:
    /// the matcher returns a different face per weight/slant and they are different font
    /// programs, unlike a <c>LysEmbed:…#</c> face whose emphasis PdfSharpCore
    /// synthesises from one program.</summary>
    internal static string FallbackFaceName(string family, bool isBold, bool isItalic)
        => $"LysFallback:{family}:{(isBold ? 'b' : '-')}{(isItalic ? 'i' : '-')}#";

    /// <summary>
    /// Registers a covering system face's bytes so a later <see cref="ResolveTypeface"/> for
    /// <paramref name="family"/> at that style serves them. Idempotent — the renderer asks
    /// once per codepoint but many codepoints share a face.
    /// </summary>
    public void RegisterFallback(string family, bool isBold, bool isItalic, byte[] bytes,
        bool simulateBold, bool simulateItalic)
        => _fallbackFaces.TryAdd(FallbackFaceName(family, isBold, isItalic),
            new FallbackFace(bytes, simulateBold, simulateItalic));

    public EmmentalerFontResolver(string? fontDirectory = null, IFontResolver? fallback = null)
    {
        _fontDirectory = fontDirectory;
        _fallback = fallback;
    }

    /// <summary>
    /// Sets (or clears) the document's configured text faces and whether to embed them.
    /// </summary>
    /// <param name="plan">The score's <c>font</c> directive, resolved.</param>
    /// <remarks>
    /// When <c>plan.Embed</c> is true and a face's licence permits embedding — Free, or
    /// Gray (an explicit <c>embedded</c> honours a gray font, having already warned) —
    /// its bytes are loaded for subset-embedding; a Forbidden or not-installed face is
    /// not embedded. Without <c>embedded</c> a face is a REFERENCE only and maps to the
    /// bundled stand-in, so nothing proprietary is embedded without asking.
    /// </remarks>
    public void SetTextFonts(TextFontPlan plan)
    {
        // Built aside, then published with one reference write — see the field's remark.
        var faces = new Dictionary<string, ConfiguredTextFace>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in TextRoles.All)
        {
            var face = plan.Resolve(role);
            if (face.IsBundled)
                continue;
            bool sans = face.Family == TextFontFamily.Sans;
            foreach (var name in face.Names)
            {
                if (faces.ContainsKey(name))
                    continue;   // first role bound to this name decides the stand-in
                byte[]? bytes = null;
                if (plan.Embed)
                {
                    var cls = FontEmbedInfo.Classify(name);
                    if (cls is not (FontEmbedInfo.FontEmbedClass.Forbidden
                                    or FontEmbedInfo.FontEmbedClass.NotFound))
                        bytes = FontEmbedInfo.TryGetFontBytes(name);
                }
                faces[name] = new ConfiguredTextFace(bytes, sans);
            }
        }
        _textFaces = faces;
    }

    /// <summary>
    /// Will a page drawn in <paramref name="family"/> carry that face's OWN font program?
    /// </summary>
    /// <remarks>
    /// True only when <c>embedded</c> was written and the licence permitted the bytes to be
    /// loaded. It is the question the drawing side has to ask before placing glyphs at the
    /// positions the layout reserved: with the real program the ink IS the face that was
    /// measured, and shaping it is right; without it the page carries the bundled STAND-IN,
    /// whose glyphs are not the measured ones, and putting them at the measured positions
    /// would be worse than letting the viewer lay the string out.
    /// </remarks>
    internal bool EmbedsOwnProgram(string family)
        => _textFaces.TryGetValue(family, out var configured) && configured.Bytes != null;

    /// <summary>The face name an embedded configured face answers to.</summary>
    /// <remarks>
    /// ⚠️ WAS THE BARE <c>LysEmbed#</c> until 2026-08-18, when one document could carry
    /// only one configured face. A block form binds several, and a single face name for
    /// all of them would serve the first face's program for every one of them.
    /// </remarks>
    internal static string EmbeddedFaceName(string family) => $"LysEmbed:{family}#";

    // The bundled TeX Gyre Schola (GUST Font License / LPPL 1.3c) face for a weight/slant
    // — the PDF stand-in for the CSS-generic "serif" and for any non-embedded text font.
    // It is LilyPond's own text face by metrics: "LilyPond Serif" prefers URW's C059, and
    // C059 and Schola agree on every advance measured, so this is what the layout reserved
    // for through TextFontMetrics. Was Liberation Serif, which is Times-metric and 9%
    // narrower — the PDF then disagreed with what the engine had spaced.
    private static FontResolverInfo SerifFace(bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "ScholaBoldItalic#",
            (true, false) => "ScholaBold#",
            (false, true) => "ScholaItalic#",
            _ => "Schola#",
        });

    // The sans stand-in, likewise metric-identical to the Nimbus Sans that LilyPond's
    // "LilyPond Sans Serif" alias prefers. Chord symbols are the caller.
    private static FontResolverInfo SansFace(bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "HerosBoldItalic#",
            (true, false) => "HerosBold#",
            (false, true) => "HerosItalic#",
            _ => "Heros#",
        });

    public string DefaultFontName => "Emmentaler";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var name = familyName.ToLowerInvariant();
        // Every Emmentaler DESIGN is its own face — the font is optically sized, so a grace's
        // 14 is not the 20 scaled (see EmmentalerFaces). The default design keeps the bare
        // "Emmentaler#" face name so existing PDFs are unchanged.
        if (EmmentalerFaces.TryParseFamily(name, out int design))
            return new FontResolverInfo(design == EmmentalerFaces.DefaultDesign
                ? "Emmentaler#"
                : EmmentalerFaces.Family(design) + "#");
        if (name == "emmentaler-brace")
            return new FontResolverInfo("EmmentalerBrace#");
        // SharedRenderer asks for the CSS generics for titles/lyrics/dynamics/chord
        // symbols. PdfSharpCore has no face for them and would substitute an arbitrary
        // installed font (e.g. the sans-serif "Agency"), so PDFs looked nothing like the
        // rest — and embedded a proprietary system font. They resolve to the bundled
        // TeX Gyre faces, which is also what TextFontMetrics measured when the engine
        // spaced this score, and which are licensed for embedding and redistribution.
        if (name is "serif")
            return SerifFace(isBold, isItalic);
        if (name is "sans" or "sans-serif")
            return SansFace(isBold, isItalic);
        // A face the document's `font` directive named. With `embedded` and a permitted
        // licence we serve its own bytes so PdfSharpCore subsets and embeds them (a
        // portable PDF; bold/italic reuse the one face and PdfSharpCore synthesises
        // emphasis). WITHOUT embedding it resolves to the bundled face of its family —
        // so a plain reference never silently embeds a system (possibly proprietary) font.
        if (_textFaces.TryGetValue(familyName, out var configured))
            return configured.Bytes != null
                ? new FontResolverInfo(EmbeddedFaceName(familyName))
                : configured.Sans ? SansFace(isBold, isItalic) : SerifFace(isBold, isItalic);
        // A system face the renderer resolved for characters no bundled face covers.
        var fallbackFace = FallbackFaceName(familyName, isBold, isItalic);
        if (_fallbackFaces.TryGetValue(fallbackFace, out var fb))
            return new FontResolverInfo(fallbackFace, fb.SimulateBold, fb.SimulateItalic);
        return _fallback?.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[] GetFont(string faceName)
    {
        if (faceName.StartsWith("LysEmbed:", StringComparison.Ordinal)
            && faceName.EndsWith("#", StringComparison.Ordinal))
        {
            string family = faceName["LysEmbed:".Length..^1];
            if (_textFaces.TryGetValue(family, out var cfg) && cfg.Bytes != null)
                return cfg.Bytes;
            throw new InvalidOperationException(
                $"Embed font '{family}' requested but its bytes were not loaded.");
        }

        if (_fallbackFaces.TryGetValue(faceName, out var fallback))
            return fallback.Bytes;

        // "Emmentaler-14#" and friends — the face name is the family plus the '#' this
        // resolver marks its own faces with, so one bundled OTF answers per design.
        if (faceName.EndsWith("#", StringComparison.Ordinal)
            && EmmentalerFaces.TryParseFamily(faceName[..^1], out int design)
            && design != EmmentalerFaces.DefaultDesign)
        {
            var designPath = ResolveFontPath(EmmentalerFaces.OtfFile(design));
            if (designPath != null) return File.ReadAllBytes(designPath);
            throw new FileNotFoundException(
                $"Font file not found: {EmmentalerFaces.OtfFile(design)}");
        }

        var fileName = faceName switch
        {
            "Emmentaler#" => EmmentalerFaces.OtfFile(EmmentalerFaces.DefaultDesign),
            "EmmentalerBrace#" => "emmentaler-brace.otf",
            "Schola#" => "texgyreschola-regular.otf",
            "ScholaBold#" => "texgyreschola-bold.otf",
            "ScholaItalic#" => "texgyreschola-italic.otf",
            "ScholaBoldItalic#" => "texgyreschola-bolditalic.otf",
            "Heros#" => "texgyreheros-regular.otf",
            "HerosBold#" => "texgyreheros-bold.otf",
            "HerosItalic#" => "texgyreheros-italic.otf",
            "HerosBoldItalic#" => "texgyreheros-bolditalic.otf",
            _ => null
        };
        if (fileName != null)
        {
            var path = ResolveFontPath(fileName);
            if (path != null) return File.ReadAllBytes(path);
            throw new FileNotFoundException($"Font file not found: {fileName}");
        }
        return _fallback?.GetFont(faceName) ?? throw new InvalidOperationException(
            $"No font data available for face '{faceName}' and no fallback resolver configured.");
    }

    private string? ResolveFontPath(string fileName) =>
        FontLocator.ResolveFile(fileName, _fontDirectory);
}
