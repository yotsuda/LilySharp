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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a header line gives everything that is not the page — MusicXML's title and creators
/// and rights, the MIDI copyright notice (owner's decision 2026-11-13).
/// </summary>
/// <remarks>
/// A line may be written as a string (<c>title "X"</c>), as Markdown (<c>```md title</c>), or
/// both. The PAGE draws the Markdown; everything else takes the string where one is written,
/// and else the Markdown's words with the formatting stripped, every line break one space
/// (Rendering.HeaderProse.Plain). A score that restates a line in either spelling restates it
/// whole, as the collector does (MeasureCollector.ForgetHeaderLine).
/// </remarks>
internal static class HeaderLineText
{
    /// <summary>The string <paramref name="place"/> (a word of
    /// <see cref="FencedText.HeaderPlaceWords"/>) gives what is not the page, for the score
    /// <paramref name="spec"/> (null: the file's own), or null where the line is not written.</summary>
    public static string? NonPage(SyntaxNode root, RenderSpec? spec, string place)
    {
        string? plain = null;
        MarkdownTextSyntax? fence = null;
        foreach (var child in root.ChildNodes())
        {
            if (child is MetadataDeclarationSyntax meta && meta.Keyword == place && meta.StringValue is { } value)
                plain = value;
            else if (child is MarkdownTextSyntax text && text.Value.HeaderPlace == place)
                fence = text;
        }
        if (spec is not null)
        {
            string? ownPlain = null;
            MarkdownTextSyntax? ownFence = null;
            if (!spec.HeaderOverrides.IsDefaultOrEmpty)
                foreach (var meta in spec.HeaderOverrides)
                    if (meta.Keyword == place && meta.StringValue is { } value)
                        ownPlain = value;
            if (!spec.TextOverrides.IsDefaultOrEmpty)
                foreach (var text in spec.TextOverrides)
                    if (text.Value.HeaderPlace == place)
                        ownFence = text;
            if (ownPlain is not null || ownFence is not null)
                (plain, fence) = (ownPlain, ownFence);
        }
        if (plain is not null)
            return plain;
        return fence is not null && Rendering.HeaderProse.PlainOf(fence.Value.Body) is { Length: > 0 } words ? words : null;
    }

    /// <summary>Whether the file writes any header line as Markdown, at its top level or in a
    /// score: a file that writes none is read as it always was.</summary>
    public static bool AnyFence(SyntaxNode root)
        => root.DescendantNodesOfKinds([SyntaxKind.MarkdownText]).OfType<MarkdownTextSyntax>()
            .Any(t => t.Value.HeaderPlace is not null);
}
