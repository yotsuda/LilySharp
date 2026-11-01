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

using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A short final bar completing the pickup (<c>partial 4 … | e'2. |</c>) is correct to Lily#
/// (MeasureValidator's exemption) and the page draws its bar line. LilyPond has no such rule: a
/// <c>|</c> there is a bar check that fails ("bar check failed at: 3/4") and draws nothing. The
/// twin writes <c>\bar "|"</c> — the line, no count — as it does under \cadenzaOn (owner's
/// question 2026-11-01 on a Chopin prelude read from a scan; MEASURED 2.26.0, Lab
/// sessions/p1036/chop: the warning on the twin's last bar, none with the glyph).
/// </summary>
[Trait("Category", "Unit")]
public class ShortFinalBarExportTests
{
    private static string Twin(string book) => new LilyPondExporter().Export(SyntaxTree.Parse(book));

    [Fact]
    public void AShortFinalBar_CompletingThePickup_IsWrittenAsTheGlyph()
    {
        string twin = Twin("octave absolute\ntime 2/2\npart p { clef treble }\nsection S {\n  partial 4\n  p { b8. b'16 | b'2. c''4 | e'2.@fermata | }\n}\nform { S }\nscore { staff p }\n");
        Assert.Contains("e'2.-\\fermata \\bar \"|\"", twin);
        Assert.Contains("b'2. c''4 |", twin);
    }

    [Fact]
    public void AFullFinalBar_OrNoPickup_KeepsTheBarCheck()
    {
        Assert.Contains("e'1-\\fermata |", Twin("octave absolute\ntime 2/2\npart p { clef treble }\nsection S {\n  partial 4\n  p { b8. b'16 | b'2. c''4 | e'1@fermata | }\n}\nform { S }\nscore { staff p }\n"));
        // No pickup: the short last bar is a short bar, and the twin says so as the page does.
        Assert.Contains("e'2.-\\fermata |", Twin("octave absolute\ntime 2/2\npart p { clef treble }\nsection S {\n  p { b'2. c''4 | e'2.@fermata | }\n}\nform { S }\nscore { staff p }\n"));
        // Music after the bar: not the final bar.
        string later = Twin("octave absolute\ntime 2/2\npart p { clef treble }\nsection S {\n  partial 4\n  p { b4 | e'2. | c''1 | }\n}\nform { S }\nscore { staff p }\n");
        Assert.Contains("e'2. |", later);
        Assert.DoesNotContain("\\bar \"|\"", later);
    }

    [Fact]
    public void TheFinalBar_IsCountedFromItsOwnSectionPlay_WithTheRunningValue()
    {
        // Two sections: the last bar's quarters are unwritten values carried from `c''4`.
        string twin = Twin("octave absolute\ntime 2/2\npart p { clef treble }\nsection A {\n  partial 4\n  p { b4 | b'2. c''4 | }\n}\nsection B {\n  p { c''4 d'' e'' | }\n}\nform { A B }\nscore { staff p }\n");
        Assert.Contains("c''4 d'' e'' \\bar \"|\"", twin);
    }
}
