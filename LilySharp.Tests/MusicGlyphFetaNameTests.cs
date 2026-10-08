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

using System.Collections.Generic;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Each row of the glyph table names, in its feta column, the glyph Emmentaler really holds at
/// the row's character — read from the font's own glyph names.
/// </summary>
/// <remarks>
/// ⚠️ THE DEFECT THIS HOLDS (session 867): the extractor's list named the quarter-tone
/// accidentals by hand, wrongly — the three-quarter sharp was the makam 8/9 sharp
/// <c>sharp.slashslashslash.stemstem</c> and the quarter-tone flats <c>flat.slash</c> /
/// <c>flatflat.slash</c>, where LilyPond draws <c>sharp.slashslash.stemstemstem</c>,
/// <c>mirroredflat</c> and <c>mirroredflat.flat</c> (scm/output-lib.scm:1136-1149). Label and
/// character AGREED with each other — so the first test here, which compares them with the
/// font, would not have caught it; the second, which compares the choice with LilyPond's
/// table, does.
/// </remarks>
public class MusicGlyphFetaNameTests
{
    /// <summary>LilyPond's glyph for each alteration a note can carry.</summary>
    /// <remarks>LILYPOND-REF: scm/output-lib.scm:1136-1149 standard-alteration-glyph-name-alist.</remarks>
    [Theory]
    [InlineData("natural", "accidentals.natural")]
    [InlineData("flat", "accidentals.flat")]
    [InlineData("sharp", "accidentals.sharp")]
    [InlineData("doubleSharp", "accidentals.doublesharp")]
    [InlineData("doubleFlat", "accidentals.flatflat")]
    [InlineData("threeQuarterSharp", "accidentals.sharp.slashslash.stemstemstem")]
    [InlineData("quarterSharp", "accidentals.sharp.slashslash.stem")]
    [InlineData("quarterFlat", "accidentals.mirroredflat")]
    [InlineData("threeQuarterFlat", "accidentals.mirroredflat.flat")]
    public void EachAlteration_IsDrawnWithLilyPondsGlyph(string kind, string feta)
        => Assert.Equal(feta, MusicGlyphs.Of(MusicGlyphs.Accidental(kind)).FetaName);

    [Fact]
    public void EveryRowsCharacter_IsTheFetaGlyphItNames()
    {
        string file = FontLocator.ResolveFile(EmmentalerFaces.OtfFile(20))!;
        using var blob = HarfBuzzSharp.Blob.FromFile(file);
        using var face = new HarfBuzzSharp.Face(blob, 0);
        using var font = new HarfBuzzSharp.Font(face);
        var wrong = new List<string>();
        int checkedRows = 0;
        foreach (var e in MusicGlyphs.Table)
        {
            // The fetaText digits and dynamic letters are ASCII in a separate encoding of the
            // same file; their names are the characters', not feta's.
            if (e.EmmentalerCode < 0xE000)
                continue;
            checkedRows++;
            string? name = font.TryGetNominalGlyph(e.EmmentalerCode, out uint gid) && font.TryGetGlyphName(gid, out string n)
                ? n : null;
            if (name != e.FetaName)
                wrong.Add($"{e.Glyph}: the row says {e.FetaName}, U+{(int)e.EmmentalerCode:X4} is {name ?? "nothing"}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.True(checkedRows > 100);
    }
}
