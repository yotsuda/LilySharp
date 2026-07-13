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
/// Warns when a <c>font "NAME" embedded</c> directive names a font whose embedding is
/// legally unclear or technically disallowed. The actual PDF embedding is a separate
/// step; this only surfaces the risk up front: a font can forbid embedding outright
/// (fsType), can be present but under an unverified license (gray), or can be missing
/// from this system entirely. A plain <c>font "NAME"</c> (no <c>embedded</c>) is not
/// checked — nothing is embedded, so nothing can be restricted.
/// </summary>
internal sealed class FontEmbedWarningValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var font in root.DescendantNodes().OfType<FontDeclarationSyntax>())
        {
            if (!font.Embedded)
                continue;
            var name = font.FontName;
            if (string.IsNullOrEmpty(name))
                continue;

            FontEmbedInfo.FontEmbedClass cls;
            try
            {
                cls = FontEmbedInfo.Classify(name);
            }
            catch
            {
                // SkiaSharp can throw on an odd platform / broken font; a font warning is
                // advisory, so skip rather than crash the whole validation pass.
                continue;
            }

            // ASCII punctuation only: these strings reach legacy-codepage consoles
            // through the CLI.
            switch (cls)
            {
                case FontEmbedInfo.FontEmbedClass.NotFound:
                    _diagnostics.Warning(font.Span, DiagnosticCodes.FontNotFound,
                        $"the font '{name}' is not installed on this system, so it cannot " +
                        "be embedded in the PDF");
                    break;
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
}
