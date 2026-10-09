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
/// A custom key signature is LilyPond's Staff.keyAlterations — (step . alteration) in print
/// order, the alteration in whole tones (lily/key-engraver.cc:146-151 draws it when it
/// changes). The twin wrote <c>\key c \major</c> and a warning until 2026-10-07, so LilyPond
/// printed no signature over <c>test/custom-key</c> and an accidental on every F and C; with
/// the alterations written its bar lines land on the page's to the hundredth (Lab
/// sessions/p851).
/// </summary>
public class TwinCustomKeyTests
{
    private static string Twin(string key) => new LilyPondExporter().Export(SyntaxTree.Parse($$"""
        octave absolute
        time 4/4
        {{key}}
        part melody
        section Main { melody { d'4 e' fis' g' | } }
        form { Main }
        score { staff melody }
        """));

    [Fact]
    public void TwoSharps_AreTheStaffsKeyAlterationsInPrintOrder()
    {
        string twin = Twin("key custom fis cis");
        Assert.Contains("\\set Staff.keyAlterations = #`((3 . 1/2) (0 . 1/2))", twin);
        Assert.DoesNotContain("\\key c \\major", twin);
    }

    [Fact]
    public void FlatsAndDoubles_AreWholeToneRationals()
        => Assert.Contains("\\set Staff.keyAlterations = #`((6 . -1/2) (2 . 1) (4 . -1))",
            Twin("key custom bes eisis geses"));
}
