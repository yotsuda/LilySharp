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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reads the part-option <c>transpose:</c> target (with its octave marks) from
/// a part declaration. Shared by the renderer's collector and the MIDI / MusicXML
/// exporters so the single transpose grammar has one reader.
/// </summary>
public static class PartTranspose
{
    /// <summary>Reads the transpose target for <paramref name="partName"/>, or null.</summary>
    public static (int step, int alt, int oct)? Read(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
            if (partDecl.Name.Text == partName)
                return Read(partDecl);
        return null;
    }

    /// <summary>Reads the transpose target from a part declaration, or null.</summary>
    public static (int step, int alt, int oct)? Read(PartDeclarationSyntax partDecl)
    {
        foreach (var prop in partDecl.Properties)
        {
            if (!string.Equals(prop.NameToken.Text, "transpose", StringComparison.OrdinalIgnoreCase))
                continue;
            if (prop.GetChild(2) is not SyntaxTokenNode valueToken
                || !PitchTransposer.TryParseTarget(valueToken.Text, out int step, out int alt))
                return null;

            // Octave marks (' / ,) follow the pitch token as extra children.
            int oct = 0;
            for (int ci = 3; ci < prop.SlotCount; ci++)
                if (prop.GetChild(ci) is SyntaxTokenNode mark)
                {
                    if (mark.Kind == SyntaxKind.Apostrophe) oct++;
                    else if (mark.Kind == SyntaxKind.Comma) oct--;
                }
            return (step, alt, oct);
        }
        return null;
    }
}
