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

using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The twin carries what the collector engraves at each section PLAY: the boxed
/// section label, and the restore of the score's home key when a section modulated
/// and the next play has no key of its own.
/// </summary>
/// <remarks>
/// The exporter's 7th and 8th silent-drop holes (reported 2026-08-13,
/// scratch/ベースタブLy/Untitled-3.lys): the twin carried NO marks at all, and kept a
/// section's <c>key a major</c> to the end of the piece where Lily# reverts to the
/// score key at the next section — so the LP page diverged from Lily#'s at every
/// reprise (naturals on every c/f/g). Mirrors MeasureCollector's boundary:
/// ResolveSectionLabel for the label (occurrence label wins, "" suppresses, ~silent
/// drops), and Form.cs's header-key-else-restore for the key.
/// </remarks>
[Trait("Category", "Unit")]
public class LilyPondExporterSectionPlayTests
{
    private static string Export(string source)
        => new LilyPondExporter().Export(SyntaxTree.Parse(source));

    private static string Doc(string form, string scoreKey = "") => $$"""
        {{scoreKey}}
        part melody {
          clef treble
          section A { c4 c g' g | a a g2 | }
          section B { key a major g'4 g f f | e e d2 | }
        }
        form main { {{form}} }
        score main { staff melody }
        """;

    [Fact]
    public void EverySectionPlayCarriesItsBoxedMark()
    {
        var ly = Export(Doc("A B A \"A2\""));
        int a = ly.IndexOf("\\mark \\markup \\box \"A\"");
        int b = ly.IndexOf("\\mark \\markup \\box \"B\"");
        int a2 = ly.IndexOf("\\mark \\markup \\box \"A2\"");
        Assert.True(a >= 0 && b > a && a2 > b,
            $"marks missing or out of order (A={a}, B={b}, A2={a2})");
    }

    [Fact]
    public void TheRepriseRestoresTheScoreKey()
    {
        var ly = Export(Doc("A B A \"A2\""));
        int modulate = ly.IndexOf("\\key a \\major");
        int restore = ly.IndexOf("\\key c \\major");
        Assert.True(modulate >= 0 && restore > modulate,
            $"expected \\key c \\major after the a-major section (mod={modulate}, restore={restore})");
    }

    [Fact]
    public void ADeclaredHomeKeyIsRestoredVerbatim()
    {
        // The restore re-emits the home DECLARATION, so mode/spelling come from the
        // source — a d-major home comes back as \key d \major, not a c-major default.
        var ly = Export(Doc("A B A \"A2\"", scoreKey: "key d major"));
        int modulate = ly.IndexOf("\\key a \\major");
        int restore = ly.IndexOf("\\key d \\major", Math.Max(0, modulate));
        Assert.True(modulate >= 0 && restore > modulate,
            $"expected \\key d \\major restored after the a-major section (mod={modulate}, restore={restore})");
        Assert.DoesNotContain("\\key c \\major", ly);
    }

    [Fact]
    public void ASilentReferenceGetsNoMarkButKeepsItsMusic()
    {
        var ly = Export(Doc("A ~B"));
        Assert.DoesNotContain("\\box \"B\"", ly);
        Assert.Contains("\\key a \\major", ly); // B's music (its inline key) still plays
    }

    [Fact]
    public void AnEmptyOccurrenceLabelSuppressesTheMark()
    {
        var ly = Export(Doc("A B A \"\""));
        Assert.Equal(2, Regex.Matches(ly, Regex.Escape("\\mark \\markup \\box")).Count);
    }
}
