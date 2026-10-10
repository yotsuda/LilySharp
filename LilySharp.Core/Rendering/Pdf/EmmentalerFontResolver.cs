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
using LilySharp.Core.Svg.Layout;
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

    // The font PROGRAMS this process embeds for named faces, by family name: a face a
    // `fonts { }` directive named with `embedded` (licence permitting) or a music font's
    // companion face, loaded once by LoadTextFonts. Add-only and shared by every document,
    // because the same name on one machine is the same bytes — what differs between
    // documents is not the program but the ANSWER (embed it, or stand in), and that answer
    // no longer lives here.
    // ⚠️ THIS RESOLVER IS A PROCESS GLOBAL (PdfSharpCore's GlobalFontSettings.FontResolver —
    // see PdfDocumentContext.EnsureFontResolver), AND SO IS PDFSHARPCORE'S MEMORY OF ITS
    // ANSWERS: FontFactory caches the FontResolverInfo per requested family name and style
    // (its `tk:<family>/…` keys) for the life of the process. Until session 885 the answer
    // for a named face was a per-document field here — the LAST document's `fonts { }`
    // decided for every face name, and the cache then froze the FIRST document's answer for
    // every later one. MEASURED 2026-10-10 (Lab sessions/p885/probe2.log, two documents in
    // one process, same name): `title "Georgia" embedded` then `title "Georgia"` — the second
    // page EMBEDS Georgia (a Gray-licensed face nobody asked to embed); `title "Verdana"`
    // then `title "Verdana" embedded` — the second page carries Schola (the embedding is
    // lost). Sequential, not only concurrent; session 317's note on the old field recorded
    // the concurrent half (a torn map) and left this one as "latent".
    // THE REPAIR: the document's answer rides in the family name the drawing side ASKS FOR
    // (PdfDrawingContext.DrawText — `LysEmbed:<name>` for a face whose own program is
    // embedded, the generic `serif` / `sans-serif` for a stand-in), so PdfSharpCore's cache
    // key IS the answer and two documents can never collide on it. What this resolver keeps
    // is only the bytes behind `LysEmbed:` — see DocumentTextFaces for the per-document half.
    private readonly ConcurrentDictionary<string, byte[]> _embeddedPrograms = new(StringComparer.OrdinalIgnoreCase);

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
    /// The faces one document draws its named text in — which of them carry their own
    /// program and which stand in as a bundled family — and the programs loaded for the
    /// former, registered here so <c>LysEmbed:</c> requests can be served.
    /// </summary>
    /// <param name="plan">The score's <c>font</c> directive, resolved.</param>
    /// <remarks>
    /// When <c>plan.Embed</c> is true and a face's licence permits embedding — Free, or
    /// Gray (an explicit <c>embedded</c> honours a gray font, having already warned) —
    /// its bytes are loaded for subset-embedding; a Forbidden or not-installed face is
    /// not embedded. Without <c>embedded</c> a face is a REFERENCE only and maps to the
    /// bundled stand-in, so nothing proprietary is embedded without asking.
    /// <para>
    /// The answer is RETURNED, not kept: the document hands it to its pages
    /// (<see cref="PdfDocumentContext.TextFaces"/>), which spell it in the family name they
    /// ask PdfSharpCore for. Two documents with different directives for one name therefore
    /// never meet — see the field remark on <c>_embeddedPrograms</c> for the measured
    /// collision this replaced.
    /// </para>
    /// </remarks>
    internal DocumentTextFaces LoadTextFonts(TextFontPlan plan)
    {
        var faces = new Dictionary<string, DocumentTextFaces.Face>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in TextRoles.All)
        {
            // The brace is a music glyph addressed by its face name, not a text face a score
            // binds (TextFontPlan.Resolve answers "Emmentaler-Brace" for it unconditionally).
            // Taking it in here made it a serif STAND-IN — the page then asked for "serif"
            // and the brace vanished from every grand-staff PDF: MEASURED on the p885 sweep,
            // 46 of 1387 books lost their Emmentaler-Brace font. The old resolver hid the
            // same entry by testing the brace's name before the configured faces.
            if (role == TextRole.SystemBrace)
                continue;
            var face = plan.Resolve(role);
            if (face.IsBundled)
                continue;
            bool sans = face.Family == TextFontFamily.Sans;
            foreach (var name in face.Names)
            {
                if (faces.ContainsKey(name))
                    continue;   // first role bound to this name decides the stand-in
                byte[]? bytes = null;
                // A music font's companion face (Petaluma Script) is in the bundle under the
                // OFL: its own program is always on the page, `embedded` or not — the bundled
                // stand-in would be a face the layout never measured.
                if (TextFontMetrics.CompanionPath(name) is { } companion)
                    bytes = File.ReadAllBytes(companion);
                else if (plan.Embed)
                {
                    var cls = FontEmbedInfo.Classify(name);
                    if (cls is not (FontEmbedInfo.FontEmbedClass.Forbidden
                                    or FontEmbedInfo.FontEmbedClass.NotFound))
                        bytes = FontEmbedInfo.TryGetFontBytes(name);
                }
                if (bytes != null)
                    RegisterEmbeddedProgram(name, bytes);
                faces[name] = new DocumentTextFaces.Face(Embedded: bytes != null, Sans: sans);
            }
        }
        return new DocumentTextFaces(faces);
    }

    /// <summary>
    /// Makes <paramref name="bytes"/> the program <see cref="EmbeddedFaceName"/> of
    /// <paramref name="family"/> serves. Idempotent, first registration wins: on one machine
    /// a family name is one file, so a second registration carries the same bytes.
    /// </summary>
    internal void RegisterEmbeddedProgram(string family, byte[] bytes)
        => _embeddedPrograms.TryAdd(family, bytes);

    /// <summary>The prefix a family REQUEST carries to say "this face's own program, which
    /// <see cref="LoadTextFonts"/> registered" — what <c>PdfDrawingContext</c> asks
    /// PdfSharpCore for, so that the process-wide cache keyed on the request can never hand
    /// one document another's answer.</summary>
    internal const string EmbeddedRequestPrefix = "LysEmbed:";

    /// <summary>The family name to ask for when <paramref name="family"/>'s own program is
    /// to be drawn: <c>LysEmbed:&lt;family&gt;</c>.</summary>
    internal static string EmbeddedFamilyRequest(string family) => EmbeddedRequestPrefix + family;

    /// <summary>The face name an embedded configured face answers to.</summary>
    /// <remarks>
    /// ⚠️ WAS THE BARE <c>LysEmbed#</c> until 2026-08-18, when one document could carry
    /// only one configured face. A block form binds several, and a single face name for
    /// all of them would serve the first face's program for every one of them.
    /// </remarks>
    internal static string EmbeddedFaceName(string family) => $"{EmbeddedRequestPrefix}{family}#";

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

    public string DefaultFontName => MusicFont.Current.FaceFamily(MusicFont.Current.DefaultDesign);

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var name = familyName.ToLowerInvariant();
        // Every music-font DESIGN is its own face — Emmentaler is optically sized, so a grace's
        // 14 is not the 20 scaled (MusicFont.FaceFamily). The default design keeps the bare
        // "Emmentaler#" face name so existing PDFs are unchanged.
        // ⚠️ THE RESOLVER IS GLOBAL (PdfSharpCore asks it from wherever it builds the document),
        // so it answers for MusicFont.Current on THAT thread — Emmentaler, until the import stage
        // teaches it every reachable font's faces.
        var music = MusicFont.Current;
        // The face name is the family plus '#': "Emmentaler#" for the default design,
        // "Emmentaler-14#" for another, "Bravura#" for a SMuFL font's one face.
        if (music.TryParseFamily(name, out int design))
            return new FontResolverInfo(music.FaceFamily(design) + "#");
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
        // A face the document's `fonts { }` directive named AND asked to carry its own
        // program (`embedded`, licence permitting) — the drawing side asks for it under the
        // LysEmbed: prefix, and the one face serves every weight and slant (PdfSharpCore
        // synthesises emphasis). A named face WITHOUT its program never arrives here: the
        // drawing side asks for the generic family of its stand-in instead, so a plain
        // reference never silently embeds a system (possibly proprietary) font.
        if (familyName.StartsWith(EmbeddedRequestPrefix, StringComparison.Ordinal))
        {
            string family = familyName[EmbeddedRequestPrefix.Length..];
            if (_embeddedPrograms.ContainsKey(family))
                return new FontResolverInfo(EmbeddedFaceName(family));
            // Not a fallback: only LoadTextFonts spells this request, after registering.
            throw new InvalidOperationException(
                $"Embed font '{family}' requested but its bytes were not loaded.");
        }
        // A system face the renderer resolved for characters no bundled face covers.
        var fallbackFace = FallbackFaceName(familyName, isBold, isItalic);
        if (_fallbackFaces.TryGetValue(fallbackFace, out var fb))
            return new FontResolverInfo(fallbackFace, fb.SimulateBold, fb.SimulateItalic);
        return _fallback?.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[] GetFont(string faceName)
    {
        if (faceName.StartsWith(EmbeddedRequestPrefix, StringComparison.Ordinal)
            && faceName.EndsWith("#", StringComparison.Ordinal))
        {
            string family = faceName[EmbeddedRequestPrefix.Length..^1];
            if (_embeddedPrograms.TryGetValue(family, out var program))
                return program;
            throw new InvalidOperationException(
                $"Embed font '{family}' requested but its bytes were not loaded.");
        }

        if (_fallbackFaces.TryGetValue(faceName, out var fallback))
            return fallback.Bytes;

        // "Emmentaler-14#" and friends — the face name is the family plus the '#' this
        // resolver marks its own faces with, so one bundled OTF answers per design.
        var music = MusicFont.Current;
        if (faceName.EndsWith("#", StringComparison.Ordinal)
            && music.TryParseFamily(faceName[..^1], out int design))
        {
            var designPath = ResolveFontPath(music.FaceFile(design));
            if (designPath != null) return File.ReadAllBytes(designPath);
            throw new FileNotFoundException(
                $"Font file not found: {music.FaceFile(design)}");
        }

        var fileName = faceName switch
        {
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

/// <summary>
/// ONE DOCUMENT'S answer for each face its <c>fonts { }</c> directive named: whether the
/// page carries that face's own program, and which bundled family stands in when it does
/// not. Built by <see cref="EmmentalerFontResolver.LoadTextFonts"/>, handed to every page
/// by <see cref="PdfDocumentContext"/>, and read by <c>PdfDrawingContext.DrawText</c>,
/// which spells the answer in the family name it asks PdfSharpCore for.
/// </summary>
/// <remarks>
/// Immutable, so a page can hold it while another document builds its own. It is this
/// per-document object, not a field on the process-wide resolver, that keeps two documents'
/// directives apart (the resolver's <c>_embeddedPrograms</c> remark has the measurement).
/// <para>
/// <see cref="Face.Sans"/> comes from the ROLE the name was bound to — chord symbols are
/// the only sans role — and decides the stand-in, so a non-embedded <c>chord "Georgia"</c>
/// falls back to the Heros the layout measured rather than to Schola.
/// </para>
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
internal sealed class DocumentTextFaces
{
    /// <summary>One named face's answer: its own program is on the page
    /// (<paramref name="Embedded"/>), else the bundled stand-in of the sans or the serif
    /// family (<paramref name="Sans"/>).</summary>
    internal readonly record struct Face(bool Embedded, bool Sans);

    /// <summary>A document that named nothing.</summary>
    public static readonly DocumentTextFaces None =
        new(new Dictionary<string, Face>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, Face> _faces;

    internal DocumentTextFaces(IReadOnlyDictionary<string, Face> faces)
        => _faces = faces;

    /// <summary>The answer for <paramref name="family"/>, or null when this document never
    /// named it — then it is not a text face the directive bound (the brace, a music face)
    /// and the request goes out under the name as written.</summary>
    public Face? Of(string family)
        => _faces.TryGetValue(family, out var face) ? face : null;

    /// <summary>
    /// Will a page drawn in <paramref name="family"/> carry that face's OWN font program?
    /// </summary>
    /// <remarks>
    /// True only when <c>embedded</c> was written and the licence permitted the bytes to be
    /// loaded (or the face is a music font's bundled companion). It is the question the
    /// drawing side has to ask before placing glyphs at the positions the layout reserved:
    /// with the real program the ink IS the face that was measured, and shaping it is right;
    /// without it the page carries the bundled STAND-IN, whose glyphs are not the measured
    /// ones, and putting them at the measured positions would be worse than letting the
    /// viewer lay the string out.
    /// </remarks>
    public bool EmbedsOwnProgram(string family)
        => _faces.TryGetValue(family, out var face) && face.Embedded;
}
