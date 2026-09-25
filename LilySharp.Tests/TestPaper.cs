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

using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

/// <summary>
/// The paper the older layout tests were measured on: the first system NOT indented.
/// </summary>
/// <remarks>
/// Until 2026-09-25 Lily# indented a first system only when a staff named an instrument, and
/// these tests' numbers — many of them read off LilyPond twins written with
/// <c>\layout { indent = 0 }</c> — were taken on that page. The product now indents every
/// first system by LilyPond's 15mm (owner's decision, session 586), so a test that still
/// asserts those numbers states the paper it was measured on, in the book's own words:
/// <c>paper { indent 0 }</c>, appended so no source offset moves.
/// </remarks>
internal static class TestPaper
{
    /// <summary>The paper block a book measured without the indent carries.</summary>
    public const string IndentZeroBlock = "\npaper { indent 0 }\n";

    /// <summary><paramref name="source"/> on that paper: <c>indent 0</c> joins the book's own
    /// <c>paper</c> block when it has one (a second block would REPLACE the first), else a
    /// block of its own is appended.</summary>
    public static string AtIndentZero(string source)
    {
        var m = System.Text.RegularExpressions.Regex.Match(source, @"\bpaper\s*\{");
        return m.Success
            ? source.Insert(m.Index + m.Length, " indent 0 ")
            : source + IndentZeroBlock;
    }

    /// <summary><see cref="SyntaxTree.Parse(string)"/> of <see cref="AtIndentZero"/>.</summary>
    public static SyntaxTree ParseAtIndentZero(string source) => SyntaxTree.Parse(AtIndentZero(source));

    /// <summary><see cref="LiveRender.SvgFromRenderSpec"/> of <see cref="AtIndentZero"/>.</summary>
    public static string SvgFromRenderSpec(string source) => LiveRender.SvgFromRenderSpec(AtIndentZero(source));

    /// <summary>The engine's paper for a test that lays a score out directly.</summary>
    public static LayoutOptions IndentZero { get; } = LayoutOptions.Default with { Indent = 0 };
}