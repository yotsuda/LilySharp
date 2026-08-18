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
/// Affine transform applied to a group scope. Translation and scaling are
/// combined as: <c>x' = TranslateX + x * ScaleX</c>, similarly for Y.
/// All units are staff-spaces (the same coordinate system as
/// <see cref="IDrawingContext"/>).
/// </summary>
public readonly record struct DrawingTransform(
    double TranslateX = 0,
    double TranslateY = 0,
    double ScaleX = 1,
    double ScaleY = 1)
{
    /// <summary>The identity transform (no translation or scaling).</summary>
    /// <remarks>
    /// ⚠️ The arguments are spelled out, and they have to be. `new()` on a record STRUCT is
    /// the parameterless constructor, which zero-initialises every field instead of running
    /// the primary constructor — so the `ScaleX = 1, ScaleY = 1` defaults above do NOT apply
    /// to it. Written as `new()` (until 2026-08-19) this property was the transform that
    /// collapses every coordinate to zero, and `Identity.IsIdentity` was itself false.
    /// It caught two independent authors, who both worked around it by hand-writing
    /// `new(0, 0, 1, 1)` in a recording context rather than by fixing it here; both now read
    /// this property, and <c>DrawingTransformTests</c> holds it to <see cref="IsIdentity"/>.
    /// ⚠️ <c>default(DrawingTransform)</c> is still the degenerate one — that is what a struct
    /// is, and it cannot be fixed without storing the scales biased by one. Use this property.
    /// </remarks>
    public static DrawingTransform Identity => new(0, 0, 1, 1);

    /// <summary>Creates a pure translation by the given offsets.</summary>
    public static DrawingTransform Translate(double x, double y) => new(x, y);

    /// <summary>Creates a uniform scale about the origin.</summary>
    public static DrawingTransform Scale(double s) => new(0, 0, s, s);

    /// <summary>True when this transform has no effect on coordinates.</summary>
    public bool IsIdentity =>
        TranslateX == 0 && TranslateY == 0 && ScaleX == 1 && ScaleY == 1;
}
