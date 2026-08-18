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

using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a <c>font</c> directive names a face this system does not have, or — with
/// <c>embedded</c> — one whose embedding is legally unclear or technically disallowed.
/// </summary>
/// <remarks>
/// The actual PDF embedding is a separate step; this surfaces the risk up front. A font
/// can be missing from this system entirely, can forbid embedding outright (fsType), or
/// can be present under an unverified licence (gray). The last two only matter under
/// <c>embedded</c> — without it nothing is embedded, so nothing can be restricted.
/// <para>
/// ⚠️ THE MISSING-FACE WARNING IS NOT GATED ON <c>embedded</c>, decided 2026-08-18 (user).
/// It used to be, which meant <c>font "NoSuchFontFace" embedded</c> was reported and the
/// plain <c>font "NoSuchFontFace"</c> was accepted in silence — the same fact, detected by
/// the same code, reported through one spelling and not the other. A misspelt face is not
/// less wrong for being un-embedded; it just fails later, on the page, in a substitute.
/// </para>
/// <para>
/// ⚠️ A WARNING AND NOT AN ERROR, same decision: whether a font is installed is a property
/// of the MACHINE, not of the source. A score that is right on the author's box would
/// otherwise fail to compile on a CI runner that has no fonts, and the file would be
/// blamed for the runner's contents.
/// </para>
/// </remarks>
internal sealed class FontEmbedWarningValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var font in root.DescendantNodes().OfType<FontDeclarationSyntax>())
        {
            // EVERY name the directive asks for, not just the first: a block binds a face
            // per role, and a PDF embeds all of them. Checking `FontName` alone would
            // clear a block whose second face is the restricted one.
            foreach (var name in font.NamedFaces())
                CheckOne(font, name);
        }
    }

    private void CheckOne(FontDeclarationSyntax font, string name)
    {
        // ⚠️ A NAME THIS ENGINE SHIPS IS NEVER MISSING. The bundle is consulted before the
        // machine (LilyPond's own order — TextFontMetrics.BundledPathForName), so
        // `serif "TeX Gyre Schola"` draws and MEASURES the file in Fonts/ and not a
        // substitute. Skia only enumerates INSTALLED families, so asking it about a bundled
        // name answers NotFound — and the warning that followed was false in both halves:
        // the face is present, and no substitution happens. Measured 2026-08-18: a book
        // binding both bundled families has geometry identical, coordinate for coordinate,
        // to one that binds nothing.
        //
        // ⚠️ It is asked of the ONE home that already answers "is this name bundled"
        // (TextFontMetrics.TryBundledFamily, whose remarks say it exists so a caller can ask
        // rather than re-derive) — not by comparing against SerifFamily/SansFamily here,
        // which would be that question's third spelling.
        if (TextFontMetrics.TryBundledFamily(TextFace.Named(name, sans: false, FontStyle.Regular), out _))
            return;

        FontEmbedInfo.FontEmbedClass cls;
        try
        {
            cls = FontEmbedInfo.Classify(name);
        }
        catch
        {
            // SkiaSharp can throw on an odd platform / broken font; a font warning is
            // advisory, so skip rather than crash the whole validation pass.
            return;
        }

        // ASCII punctuation only: these strings reach legacy-codepage consoles
        // through the CLI.
        if (cls == FontEmbedInfo.FontEmbedClass.NotFound)
        {
            _diagnostics.Warning(font.Span, DiagnosticCodes.FontNotFound,
                font.Embedded
                    ? $"the font '{name}' is not installed on this system, so it cannot " +
                      "be embedded in the PDF"
                    : $"the font '{name}' is not installed on this system, so this text " +
                      "will be drawn in a substitute face");
            return;
        }
        if (!font.Embedded)
            return;   // nothing is embedded, so no licence can be breached
        switch (cls)
        {
            case FontEmbedInfo.FontEmbedClass.Forbidden:
                _diagnostics.Warning(font.Span, DiagnosticCodes.FontEmbedForbidden,
                    $"the font '{name}' does not permit embedding (its fsType is " +
                    "restricted); it will not be embedded");
                break;
            case FontEmbedInfo.FontEmbedClass.Gray:
                _diagnostics.Warning(font.Span, DiagnosticCodes.FontEmbedLicenseUnclear,
                    $"embedding the font '{name}' may be restricted by its license - " +
                    "verify the font's license (only clearly-free fonts such as " +
                    "OFL/Apache are auto-cleared)");
                break;
            case FontEmbedInfo.FontEmbedClass.Free:
            default:
                break; // clearly embeddable — no diagnostic
        }
    }
}
