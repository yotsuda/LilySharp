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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A system a spanner merely CROSSES is not independent of that spanner: the pedal line is
/// seeded under the lyrics of every system it runs through (<c>PedalEngraver.SolveAndSeed</c>)
/// and the hairpin's wedge is reserved in the staff's silhouette on every system it runs
/// through (<c>SkylineBuilder.AddHairpinsToSkyline</c>), and both reach values the system
/// cache keeps — the lyric bands, the staff skylines. So the span fold of
/// <c>MeasureContentKey.BucketSpan</c> into the measures BETWEEN a spanner's ends is
/// load-bearing, and these are its observers: delete either end and every crossed system
/// must be re-derived, on the production wiring, against a full compile.
/// </summary>
/// <remarks>
/// Session 796 (HANDOFF §1.0 ⒳⁶): the item proposed folding a spanner into its END measures
/// only, on the finding of session 453 that nothing a system caches depends on a bracket that
/// merely crosses it — measured on a bare, single-staff book, where it holds (the SVG stayed
/// byte-identical with four systems served stale). It does not hold once something sits
/// under the bracket: with a lyric row under the pedal line, or a second staff under the
/// wedge, the poison that drops the middle fold leaves 4 of these 11 shapes stale (the pedal's
/// two deletions, the hairpin's two) while the trill's and the volta's stay the same. The
/// 24-bar books break every 4 bars, so bars 5-8 to 17-20 are systems the spanner (bar 3 to
/// bar 22) only crosses — the shape the session-453 book had no lyrics on. The trill and volta
/// rows are controls that pin what the single-staff shapes DO see, not a claim that an
/// ends-only fold would be sound for them (unmeasured on a multi-staff book, and an edit at a
/// spanner's end is rare enough that the saving was never worth the question).
/// </remarks>
[Trait("Category", "Unit")]
public class SpannerCrossedSystemsTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Bars(int n, Func<int, string> bar)
        => string.Join(" ", Enumerable.Range(1, n).Select(bar));

    // 24 bars, `break` every 4 — bars 5-8, 9-12, 13-16 and 17-20 are systems the spanner
    // (bar 3 → bar 22) merely crosses.
    private static string Cell(int bar, string mark3, string mark22, string plain = "c'4 d' e' f'")
    {
        string body = bar == 3 ? mark3 : bar == 22 ? mark22 : plain;
        return body + " |" + (bar % 4 == 0 && bar < 24 ? " break" : "");
    }

    /// <summary>A sustain bracket under a lyric row: the line is seeded under the syllables
    /// of every crossed system.</summary>
    private static string PedalBook()
    {
        string music = Bars(24, b => Cell(b, "c'4 d'@sustain e' f'", "c'4 d'@!sustain e' f'"));
        string sylls = string.Concat(Enumerable.Repeat("la la la la | ", 24)).TrimEnd();
        return $$"""
            octave absolute
            time 4/4
            part melody { clef treble }
            section Main {
              melody { {{music}} }
              lyrics w sings melody { {{sylls}} }
            }
            form { Main }
            score "x" { staff melody  lyrics w }
            """;
    }

    /// <summary>A crescendo wedge over a second staff: the wedge is reserved in the upper
    /// staff's silhouette on every crossed system, and the lower staff is spaced off it.</summary>
    private static string HairpinBook()
    {
        string top = Bars(24, b => Cell(b, "c'4@p d'@cresc e' f'", "c'4 d'@f e' f'"));
        string low = Bars(24, b => Cell(b, "c4 d e f", "c4 d e f", "c4 d e f"));
        return $$"""
            octave absolute
            time 4/4
            part top { clef treble }
            part low { clef bass }
            section Main {
              top { {{top}} }
              low { {{low}} }
            }
            form { Main }
            score "x" { staff top  staff low }
            """;
    }

    private static string TrillBook()
    {
        string music = Bars(24, b => Cell(b, "c'4 d'@startTrillSpan e' f'", "c'4 d'@stopTrillSpan e' f'"));
        return $$"""
            octave absolute
            time 4/4
            part melody { clef treble }
            section Main { melody { {{music}} } }
            form { Main }
            score "x" { staff melody }
            """;
    }

    /// <summary>A first ending of 16 bars broken every 4: the bracket's middle systems.</summary>
    private static string VoltaBook()
    {
        string a = Bars(4, b => "c'4 d' e' f' |");
        string ending = Bars(16, b => "g'4 a' b' c'' |" + (b % 4 == 0 && b < 16 ? " break" : ""));
        string c = Bars(4, b => "e'4 d' c' b |");
        return $$"""
            octave absolute
            time 4/4
            part melody { clef treble }
            section A { melody { {{a}} } }
            section B { melody { {{ending}} } }
            section C { melody { {{c}} } }
            form { |: A [1. B] :| [2. C] }
            score "x" { staff melody }
            """;
    }

    [Theory]
    [InlineData("pedal", "@!sustain", "")]
    [InlineData("pedal", "@sustain", "")]
    [InlineData("pedal", "d'@!sustain", "d'@!sustain@f")]
    [InlineData("hairpin", "@f", "")]
    [InlineData("hairpin", "@cresc", "")]
    [InlineData("hairpin", "@f", "@ff")]
    [InlineData("trill", "@stopTrillSpan", "")]
    [InlineData("trill", "@startTrillSpan", "")]
    [InlineData("volta", "[1. B]", "[1-2. B]")]
    [InlineData("volta", "[1. B] :| [2. C]", "B C")]
    [InlineData("volta", "[2. C]", "[2. C] break")]
    public void AnEditAtASpannersEnd_RedrawsTheSystemsItMerelyCrossed(string book, string find, string replacement)
    {
        string text = (book switch
        {
            "pedal" => PedalBook(),
            "hairpin" => HairpinBook(),
            "trill" => TrillBook(),
            _ => VoltaBook(),
        }).Replace("\r\n", "\n");
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics.Select(d => d.Message)));
        var session = new IncrementalCompiler(tree, Opt);
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), Opt), session.RenderIncremental(tree));

        int at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, "edit anchor not found");
        tree = tree.WithChange(new TextChange(new TextSpan(at, find.Length), replacement));
        text = text.Substring(0, at) + replacement + text.Substring(at + find.Length);
        Assert.Equal(text, tree.Text);
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(text), Opt), session.RenderIncremental(tree));
    }
}
