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

namespace LilySharp.Tests;

/// <summary>
/// Pins the deliberately-divergent contracts of the two color entry points, which
/// now share one core (ColorParser.TryParse): Color.Parse throws + treats "black"
/// as opaque black; ColorParser.Parse returns null + treats "black" as "no color"
/// (backend default). Both accept the same union of hex formats and name table.
/// </summary>
public class ColorParserTests
{
    // ---- Shared core: hex formats + name table (both entry points) ----

    [Theory]
    [InlineData("#abc", 0xaa, 0xbb, 0xcc, 255)]          // #rgb (each nibble * 17)
    [InlineData("#112233", 0x11, 0x22, 0x33, 255)]       // #rrggbb
    [InlineData("#11223344", 0x11, 0x22, 0x33, 0x44)]    // #rrggbbaa (alpha)
    public void Hex_formats_parse_the_same_in_both(string spec, int r, int g, int b, int a)
    {
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b, (byte)a), Color.Parse(spec));
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b, (byte)a), ColorParser.Parse(spec));
    }

    [Theory]
    [InlineData("red", 255, 0, 0)]
    [InlineData("green", 0, 128, 0)]
    [InlineData("yellow", 255, 255, 0)]
    [InlineData("orange", 255, 165, 0)]
    public void Named_colors_parse_the_same_in_both(string name, int r, int g, int b)
    {
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b), Color.Parse(name));
        Assert.Equal(new Color((byte)r, (byte)g, (byte)b), ColorParser.Parse(name));
    }

    // ---- The deliberately-divergent "black" quirk ----

    [Fact]
    public void Black_is_opaque_black_for_Color_but_null_for_ColorParser()
    {
        Assert.Equal(Color.Black, Color.Parse("black"));
        Assert.Null(ColorParser.Parse("black"));
    }

    // ---- The deliberately-divergent error contract ----

    [Fact]
    public void Color_Parse_throws_on_unknown_and_empty()
    {
        Assert.Throws<FormatException>(() => Color.Parse("chartreuse"));
        Assert.Throws<ArgumentException>(() => Color.Parse(""));
    }

    [Fact]
    public void ColorParser_Parse_returns_null_on_unknown()
    {
        Assert.Null(ColorParser.Parse("chartreuse"));
        Assert.Null(ColorParser.Parse(""));
        Assert.Null(ColorParser.Parse("#zz"));
    }
}
