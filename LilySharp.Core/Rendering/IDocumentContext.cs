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

namespace LilySharp.Core.Rendering;

/// <summary>
/// Backend-agnostic multi-page document. The same renderer code path can
/// produce SVG, PDF (or future formats) by switching the implementation.
/// </summary>
public interface IDocumentContext : IDisposable
{
    /// <summary>
    /// Begins a new page of the given dimensions (in staff-spaces) and
    /// returns the page's drawing context. Backends that produce a
    /// single-page SVG may treat additional pages as separate documents
    /// or stack them vertically — see implementation notes.
    /// </summary>
    IDrawingContext BeginPage(double widthSpaces, double heightSpaces);

    /// <summary>
    /// Closes the current page. Must be called once per
    /// <see cref="BeginPage"/>.
    /// </summary>
    void EndPage();
}
