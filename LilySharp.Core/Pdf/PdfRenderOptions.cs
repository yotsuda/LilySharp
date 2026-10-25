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

namespace LilySharp.Core.Pdf;

/// <summary>
/// Options for PDF rendering.
/// </summary>
public sealed class PdfRenderOptions
{
    /// <summary>Paper values from outside the file (<c>lysc … --set KEY=VALUE</c>), laid over the
    /// paper the file says (<see cref="Semantics.PaperOverrides"/>).</summary>
    public Semantics.PaperOverrides? PaperOverrides { get; init; }

    /// <summary>Told what the layout had to give up (<see cref="Svg.Layout.LayoutWarnings"/>).</summary>
    public System.Action<string>? LayoutWarning { get; init; }
    /// <summary>Draw the editor's <c>@todo</c> marks — the red rings and the memo callouts, with
    /// the room the preview opens for the memos (<c>lysc png|pdf --todo</c>).</summary>
    public bool EditorMarks { get; init; }

    /// <summary>Page size in points (1 point = 1/72 inch).</summary>
    public double PageWidthPt { get; init; } = 595.28; // A4

    /// <summary>Page height in points.</summary>
    public double PageHeightPt { get; init; } = 841.89; // A4

    /// <summary>Staff space size in PDF points. Controls the overall scale.</summary>
    /// <remarks>
    /// LilyPond's 20pt staff: a staff space of 5 TeX points (LayoutOptions.DefaultStaffSpaceMm),
    /// 5 × 72 / 72.27 PDF points — at which the paper the layout is drawn on comes out its own
    /// size (owner's report 2026-10-25: an A4 book was 753 × 1050 pt, not 595 × 842). It was
    /// 6.0, said to be "~1.7mm" — it is 2.1 mm, 1.2 times the staff.
    /// </remarks>
    public double StaffSpacePt { get; init; } = DefaultStaffSpacePt;

    /// <summary>LilyPond's default staff space in PDF points (≈ 4.981).</summary>
    public const double DefaultStaffSpacePt = 5.0 * 72.0 / 72.27;

    /// <summary>Whether to embed the music font in the PDF.</summary>
    public bool EmbedFont { get; init; } = true;

    /// <summary>Directory containing font files (OTF/TTF).</summary>
    public string? FontDirectory { get; init; }

    /// <summary>Default options (A4, standard staff size).</summary>
    public static PdfRenderOptions Default => new();

    /// <summary>Letter size variant.</summary>
    public static PdfRenderOptions Letter => new()
    {
        PageWidthPt = 612,   // 8.5"
        PageHeightPt = 792   // 11"
    };
}
