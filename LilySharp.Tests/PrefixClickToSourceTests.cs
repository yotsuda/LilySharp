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

using System;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The clef and key signature repeat at the head of EVERY system, and every one of those
/// repeats is clickable: its <c>data-pos</c> points at whatever put it in force there — the
/// declaration, or the last mid-piece change before that system.
/// </summary>
/// <remarks>
/// Reported 2026-08-28 against a three-system lead sheet: clicking the key signature jumped
/// to its <c>key</c> line on the FIRST system and did nothing on the second and third. The
/// prefix tagged only the first system (<c>isFirstSystem ? … : 0</c>), the stated reason
/// being that a later line may be showing a CHANGE rather than the declaration. That reason
/// is real, which is why the repair RESOLVES the position instead of dropping it — and the
/// second test here is the one that keeps the two apart.
/// </remarks>
[Trait("Category", "Unit")]
public class PrefixClickToSourceTests
{
    private static string Render(string source) =>
        SvgGenerator.Generate(SyntaxTree.Parse(source), new SvgRenderOptions { EmbedFont = false });


    /// <summary>How many SYSTEM-PREFIX glyphs carry an offset inside <paramref name="token"/>.
    /// Any offset inside it counts: the renderer tags with the item's own recorded position,
    /// not the token's first character.</summary>
    /// <remarks>
    /// ⚠️ THE PREFIX ONLY, by X. A mid-piece key change draws its cancellation and its new
    /// signature IN THE BAR as well, tagged with the same offset, so counting every tagged
    /// glyph would mix "what the line opens with" — the thing this file is about — with
    /// "what the change itself drew". The prefix sits at the line's left edge before any
    /// note; ClefChangeTests tells the system-start clef from a mid-piece one the same way.
    /// </remarks>
    private static int TaggedInside(string svg, string source, string token)
    {
        int start = source.IndexOf(token, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the probe does not contain \"{token}\"");
        var inside = Enumerable.Range(start, token.Length).ToHashSet();
        return Regex.Matches(svg, "<text[^>]*x=\"(?<x>[0-9.]+)\"[^>]*data-pos=\"(?<p>[0-9]+)\"")
            .Count(m => double.Parse(m.Groups["x"].Value,
                           System.Globalization.CultureInfo.InvariantCulture) < 10.0
                        && inside.Contains(int.Parse(m.Groups["p"].Value)));
    }

    // Three systems (two breaks) and ONE staff, so the expected count is simply the number
    // of systems — a second staff would multiply it and hide an off-by-one.
    private const string ThreeSystems =
        "key g major\n"
        + "part melody {\n"
        + "  clef treble\n"
        + "  section A { c'4 c' g' g' | break a' a' g'2 | break f'4 f' e' e' }\n"
        + "}\n"
        + "form main { A }\n"
        + "score main { staff melody }\n";

    [Fact]
    public void EverySystemsKeySignature_PointsAtTheKeyDeclaration()
    {
        var svg = Render(ThreeSystems);
        // G major is one sharp, so one glyph per system.
        Assert.Equal(3, TaggedInside(svg, ThreeSystems, "key g major"));
    }

    [Fact]
    public void EverySystemsClef_PointsAtTheClefDeclaration()
    {
        var svg = Render(ThreeSystems);
        Assert.Equal(3, TaggedInside(svg, ThreeSystems, "clef treble"));
    }

    [Fact]
    public void ASystemShowingAMidPieceChange_PointsAtTheCHANGE_NotTheDeclaration()
    {
        // The guard the old `first system only` rule was protecting, done properly: after a
        // mid-piece change the later systems print the CHANGE, so that is where a click must
        // land. Without this, "tag every system with the declaration" would send the reader
        // to a line that no longer describes what they clicked.
        const string src =
            "key g major\n"
            + "part melody {\n"
            + "  clef treble\n"
            + "  section A { c'4 c' g' g' | break key f major clef bass a4 a g2 | break f4 f e e }\n"
            + "}\n"
            + "form main { A }\n"
            + "score main { staff melody }\n";
        var svg = Render(src);

        // System 1 shows the declarations; systems 2 and 3 show the change.
        Assert.Equal(1, TaggedInside(svg, src, "key g major"));
        Assert.Equal(1, TaggedInside(svg, src, "clef treble"));
        // F major is one flat and bass is one glyph, so two systems' worth of each —
        // and the change's OWN glyphs, out in the bar at x ~ 100, are not counted.
        Assert.Equal(2, TaggedInside(svg, src, "key f major"));
        Assert.Equal(2, TaggedInside(svg, src, "clef bass"));
    }

    [Fact]
    public void AKeyThatDrawsNothing_TagsNothing()
    {
        // C major has no accidentals, so there is no key glyph on any system and nothing to
        // click — which is why the original report could only be reproduced with a key that
        // prints. The clef still tags on every system.
        const string src =
            "key c major\n"
            + "part melody {\n"
            + "  clef treble\n"
            + "  section A { c'4 c' g' g' | break a' a' g'2 }\n"
            + "}\n"
            + "form main { A }\n"
            + "score main { staff melody }\n";
        var svg = Render(src);
        Assert.Equal(0, TaggedInside(svg, src, "key c major"));
        Assert.Equal(2, TaggedInside(svg, src, "clef treble"));
    }
}
