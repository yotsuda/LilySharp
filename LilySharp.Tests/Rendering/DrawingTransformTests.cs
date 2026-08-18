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

using LilySharp.Core.Rendering;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// The named transforms mean what they are called. Written 2026-08-19, when
/// <c>DrawingTransform.Identity</c> had spent its whole life as <c>new()</c> — the record
/// STRUCT's parameterless constructor, which zeroes every field rather than running the
/// primary constructor — so it was the transform that collapses all coordinates to zero and
/// its own <c>IsIdentity</c> said false. Nothing in the renderer read it, which is why no
/// output ever moved; two test authors reached for it, hit the collapse, and each wrote
/// <c>new(0, 0, 1, 1)</c> by hand with a comment warning the next person off. Three sessions
/// read that note and left it. The property is the one place the meaning belongs.
/// </summary>
[Trait("Category", "Unit")]
public class DrawingTransformTests
{
    [Fact]
    public void Identity_IsIdentity()
    {
        Assert.True(DrawingTransform.Identity.IsIdentity);
    }

    /// <summary>
    /// The neighbours, because the bug was never about the number 1 — it was about which
    /// constructor runs. <c>Translate</c> and <c>Scale</c> pass arguments, so the primary
    /// constructor's defaults DO reach them, and they were correct the whole time. Asserting
    /// them here is what makes the pattern visible rather than the single symptom.
    /// </summary>
    [Fact]
    public void TranslateAndScale_KeepTheDefaultsTheParameterlessConstructorSkips()
    {
        var t = DrawingTransform.Translate(3, -4);
        Assert.Equal((3, -4, 1, 1), (t.TranslateX, t.TranslateY, t.ScaleX, t.ScaleY));
        Assert.False(t.IsIdentity);

        var s = DrawingTransform.Scale(0.65);
        Assert.Equal((0, 0, 0.65, 0.65), (s.TranslateX, s.TranslateY, s.ScaleX, s.ScaleY));
        Assert.False(s.IsIdentity);
    }

    /// <summary>
    /// ⚠️ And the edge that did NOT move, so the fix above is not read as more than it is:
    /// <c>default(DrawingTransform)</c> is still the degenerate all-zero value. That is what
    /// a struct is — the runtime hands out zeroed memory and no constructor is involved — and
    /// it cannot be fixed without storing the scales biased by one. Anyone seeding an
    /// accumulator must say <c>Identity</c>; this test is here so that stays written down.
    /// </summary>
    [Fact]
    public void Default_IsStillNotTheIdentity()
    {
        Assert.False(default(DrawingTransform).IsIdentity);
    }
}
