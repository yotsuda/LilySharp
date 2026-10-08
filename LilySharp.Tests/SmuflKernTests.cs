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
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A SMuFL font kerns its dynamic letters and time-signature digits by its own GPOS
/// (docs/smufl-design.md §3 #7, §6 ② ⒠) — read by shaping each pair, the reading that
/// reproduces Emmentaler's extracted table.
/// </summary>
public class SmuflKernTests
{
    private static readonly MusicGlyph[] Kerned = MusicGlyphs.Table.Select(e => e.Glyph)
        .Where(g => MusicGlyphs.IsDynamicLetter(g) || MusicGlyphs.IsTimeSigDigit(g)).ToArray();

    private static MusicFont Font(string name)
        => MusicFonts.Find(name, out var tried) ?? throw new Xunit.Sdk.XunitException(name + " not found: " + string.Join("; ", tried));

    [Fact]
    public void TheShapingReader_ReproducesEmmentalersExtractedTable()
    {
        // The generated table came out of emmentaler-20's GPOS by another tool; the reader the
        // SMuFL fonts use, pointed at the same file, must give every pair back — the 74 kerned
        // ones and the zeros. An em is four of the design's staff spaces.
        string file = FontLocator.ResolveFile(EmmentalerFaces.OtfFile(20))!;
        var emmentaler = EmmentalerMusicFont.Instance;
        int kerned = 0;
        foreach (var a in Kerned)
            foreach (var b in Kerned)
            {
                double expected = emmentaler.Kern(a, b);
                double shaped = TextFontMetrics.MusicGlyphPairKernFromFile(file,
                    emmentaler.Codepoint(a), emmentaler.Codepoint(b))!.Value * 4.0;
                Assert.True(System.Math.Abs(expected - shaped) < 5e-4, $"{a} {b}: table {expected}, shaped {shaped}");
                if (expected != 0) kerned++;
            }
        Assert.Equal(74, kerned);
    }

    [Fact]
    public void ASmuflFont_KernsByItsOwnGpos()
    {
        // Bravura kerns z before p (sfzp, fzp); Petaluma kerns nothing in these two runs.
        Assert.True(Font("Bravura").Kern(MusicGlyph.DynamicZ, MusicGlyph.DynamicPiano) < -0.1);
        Assert.Equal(0.0, Font("Bravura").Kern(MusicGlyph.DynamicForte, MusicGlyph.DynamicForte));
        Assert.All(Kerned, a => Assert.All(Kerned, b => Assert.Equal(0.0, Font("Petaluma").Kern(a, b))));
        // A pair outside the two runs is never kerned.
        Assert.Equal(0.0, Font("Bravura").Kern(MusicGlyph.NoteheadBlack, MusicGlyph.NoteheadBlack));
    }

    [Fact]
    public void TheChain_KernsWithinOneFont_AndNotAcrossTwo()
    {
        var chain = new MusicFontChain([Font("Leland")]);
        Assert.Equal(Font("Leland").Kern(MusicGlyph.DynamicZ, MusicGlyph.DynamicPiano),
            chain.Kern(MusicGlyph.DynamicZ, MusicGlyph.DynamicPiano));
        Assert.Equal(0.0, chain.Kern(MusicGlyph.Figbass1, MusicGlyph.DynamicPiano));
    }
}
