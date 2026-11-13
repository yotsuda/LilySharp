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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// The fenced texts a score prints (docs/markdown-text-design.md): the Markdown under its
/// title and the Markdown after its last system, each as written between the fences, with
/// where its first fence begins in the file (0 = none) for the preview's click.
/// </summary>
/// <param name="Before">The Markdown under the title, or null.</param>
/// <param name="BeforePosition">Where its first fence begins.</param>
/// <param name="After">The Markdown after the last system, or null.</param>
/// <param name="AfterPosition">Where its first fence begins.</param>
/// <param name="BeforeShape">How the text under the title is set: its opening line's
/// <c>xMargin</c>, <c>yMargin</c>, <c>center</c>, <c>box</c> (owner-approved 2026-11-13).</param>
/// <param name="AfterShape">The same for the text after the last system: each place has its own.</param>
public readonly record struct ScoreTexts(
    string? Before, int BeforePosition, string? After, int AfterPosition,
    Syntax.TextShape BeforeShape = default, Syntax.TextShape AfterShape = default);