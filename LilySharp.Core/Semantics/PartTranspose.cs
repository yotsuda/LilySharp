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
/// Reads the <c>transpose</c> target (with its octave marks) for a part:
/// the part's own option if present, otherwise a top-level <c>transpose</c>
/// default that applies to every part. Shared by the renderer's collector and
/// the MIDI / MusicXML exporters so the single transpose grammar has one reader.
/// </summary>
public static class PartTranspose
{
    /// <summary>The effective transpose for <paramref name="partName"/>, or null.</summary>
    public static (int step, int alt, int oct)? Read(SyntaxNode root, string partName)
    {
        foreach (var partDecl in root.DescendantNodes().OfType<PartDeclarationSyntax>())
            if (partDecl.Name.Text == partName)
            {
                var own = Read(partDecl);
                if (own != null) return own; // a part's own transpose overrides the default
                break;
            }
        return ReadScoreDefault(root);
    }

    /// <summary>
    /// Parses a <c>transpose &lt;pitch&gt;</c> property node (the shared shape used by a
    /// part header and a per-score transpose) into a c-&gt;target interval, or null.
    /// </summary>
    public static (int step, int alt, int oct)? ReadProperty(PropertyAssignmentSyntax prop)
        => IsTranspose(prop) ? Parse(prop) : null;

    /// <summary>Reads the transpose option from a part declaration, or null.</summary>
    public static (int step, int alt, int oct)? Read(PartDeclarationSyntax partDecl)
    {
        foreach (var prop in partDecl.Properties)
            if (IsTranspose(prop))
                return Parse(prop);
        return null;
    }

    /// <summary>
    /// A free-standing top-level <c>transpose d</c> (not a part-header attribute):
    /// the score-wide default.
    /// </summary>
    private static (int step, int alt, int oct)? ReadScoreDefault(SyntaxNode root)
    {
        foreach (var prop in root.DescendantNodes().OfType<PropertyAssignmentSyntax>())
            if (IsTranspose(prop) && !IsInsidePart(prop))
                return Parse(prop);
        return null;
    }

    private static bool IsTranspose(PropertyAssignmentSyntax prop)
        => string.Equals(prop.NameToken.Text, "transpose", StringComparison.OrdinalIgnoreCase);

    private static bool IsInsidePart(SyntaxNode node) => node.IsInside<PartDeclarationSyntax>();

    // Children are: name, [optional colon], value, [octave marks...]. The colon
    // is now optional, so locate the value/marks by skipping the name and colon
    // rather than by a fixed slot index.
    private static (int step, int alt, int oct)? Parse(PropertyAssignmentSyntax prop)
    {
        SyntaxTokenNode? valueToken = null;
        int oct = 0;
        for (int ci = 1; ci < prop.SlotCount; ci++)
        {
            if (prop.GetChild(ci) is not SyntaxTokenNode tok || tok.Kind == SyntaxKind.Colon)
                continue;
            if (valueToken == null)
            {
                valueToken = tok;
            }
            else if (tok.Kind == SyntaxKind.Apostrophe)
            {
                oct++;
            }
            else if (tok.Kind == SyntaxKind.Comma)
            {
                oct--;
            }
        }

        if (valueToken == null || !PitchTransposer.TryParseTarget(valueToken.Text, out int step, out int alt))
            return null;
        return (step, alt, oct);
    }
}
