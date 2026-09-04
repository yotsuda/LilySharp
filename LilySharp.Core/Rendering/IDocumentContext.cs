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
    /// Which face each <see cref="TextRole"/> is drawn in — the score's <c>font</c>
    /// directive, resolved.
    /// </summary>
    /// <remarks>
    /// ⚠️ SET BEFORE THE FIRST <see cref="BeginPage"/>, by <c>SharedRenderer.RenderTo</c>,
    /// which is the one caller holding both the score and the document. It lives on the
    /// DOCUMENT rather than travelling with each draw call because a face is a property of
    /// the whole score, and because the page contexts are built here — a backend that
    /// took it per call would be handed the same plan 37 times a page.
    /// <para>
    /// The default is <see cref="TextFontPlan.Default"/>, so a backend driven straight
    /// from a test (there are several) draws the bundled faces without arranging anything.
    /// </para>
    /// </remarks>
    TextFontPlan Fonts { get; set; }

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

    /// <summary>
    /// Whether the renderer should draw each system in a frame of its OWN — its top at
    /// the page's origin, inside a group that translates it to where it sits — instead of
    /// in page coordinates. False for every backend but the interactive SVG preview.
    /// </summary>
    /// <remarks>
    /// The picture is the same either way; what changes is the TEXT. In page coordinates
    /// every Y of a system carries the system's position, so a system that moved down the
    /// page (a bar inserted before it, a system above it grown taller) is new text from
    /// its first byte: the per-system fragment memo cannot replay it and the preview has
    /// to parse and lay it out again — MEASURED in the editor (2026-09-04): 200 ms to
    /// swap two later pages plus 200–245 ms of layout, on the renderer's own thread, for
    /// a one-system edit. In its own frame the same system is the same bytes under a
    /// different <c>transform</c>, which the memo replays and the preview re-attributes.
    /// Static export keeps page coordinates, so exported files are byte for byte what
    /// they were (the same posture as <see cref="IDrawingContext.BeginLabeledGroup"/>).
    /// </remarks>
    bool SystemLocalFrames => false;
}
