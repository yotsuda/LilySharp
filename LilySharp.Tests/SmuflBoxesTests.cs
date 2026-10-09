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
using System.Text.Json;
using LilySharp.Core.Rendering.Boxes;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc boxes</c> of a score in a SMuFL font names its glyphs as it names Emmentaler's — so a
/// glyph's kind does not depend on the font — gives the code point the font drew, says which
/// font the page was drawn in, and which glyph came from another (docs/smufl-design.md §5, §6 ③).
/// </summary>
public class SmuflBoxesTests
{
    private const string Book =
        "part m { clef bass }\n" +
        "section A { m { c4@figuredBass(6) d4 e2 } }\n" +
        "form { A }\n" +
        "score { staff m }\n";

    private static BoxDocument Boxes(string fonts)
    {
        var tree = SyntaxTree.Parse(fonts + "\n" + Book);
        return BoxesGenerator.GenerateDocument(tree, RenderSpecParser.FindFirst(tree));
    }

    [Fact]
    public void ABravuraNoteHead_IsANoteheadByTheSameName_AtBravurasCodePoint()
    {
        var bravura = Boxes("fonts { music \"Bravura\" }");
        var emmentaler = Boxes("");
        Assert.Equal("Bravura", bravura.MusicFont);
        Assert.Equal("Emmentaler", emmentaler.MusicFont);
        var head = bravura.Pages.SelectMany(p => p.Symbols).First(s => s.Kind == "notehead");
        var emmHead = emmentaler.Pages.SelectMany(p => p.Symbols).First(s => s.Kind == "notehead");
        Assert.Equal(emmHead.Glyph, head.Glyph);
        Assert.Equal(0xE0A4, head.Codepoint);   // SMuFL noteheadBlack
        Assert.NotEqual(emmHead.Codepoint, head.Codepoint);
        // Every glyph kind the Emmentaler page has, the Bravura page has too.
        var kinds = bravura.Pages.SelectMany(p => p.Symbols).Where(s => s.Glyph != null).Select(s => s.Kind).ToHashSet();
        var emmKinds = emmentaler.Pages.SelectMany(p => p.Symbols).Where(s => s.Glyph != null).Select(s => s.Kind).ToHashSet();
        Assert.Superset(emmKinds, kinds);
        Assert.DoesNotContain(bravura.Pages.SelectMany(p => p.Symbols), s => s.Font != null);
    }

    [Fact]
    public void AGlyphDrawnFromAnotherFont_SaysWhich()
    {
        var leland = Boxes("fonts { music \"Leland\" }");
        var figure = Assert.Single(leland.Pages.SelectMany(p => p.Symbols), s => s.Kind == "figuredBass");
        Assert.Equal("Emmentaler", figure.Font);
        Assert.DoesNotContain(leland.Pages.SelectMany(p => p.Symbols), s => s.Kind == "notehead" && s.Font != null);
    }

    [Fact]
    public void TheJson_NamesTheMusicFont()
    {
        using var json = JsonDocument.Parse(BoxesGenerator.ToJson(Boxes("fonts { music \"Leland\" }")));
        Assert.Equal("Leland", json.RootElement.GetProperty("musicFont").GetString());
        var symbols = json.RootElement.GetProperty("pages")[0].GetProperty("symbols").EnumerateArray().ToList();
        Assert.Contains(symbols, s => s.TryGetProperty("font", out var f) && f.GetString() == "Emmentaler");
    }
}
