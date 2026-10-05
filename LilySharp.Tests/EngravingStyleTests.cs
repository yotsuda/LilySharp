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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { lineThickness … StaffLine.thickness … Stem.thickness … }</c> and the same keys as
/// <c>lysc --set lineThickness=…</c>
/// (LilySharp-Omr's proposal of 2026-10-02 P4, session 822): LilyPond's line thicknesses and
/// stem length under its own names and units, held by <see cref="EngravingStyle"/>.
/// </summary>
[Trait("Category", "Unit")]
public class EngravingStyleTests
{
    private static EngravingStyle StyleOf(params string[] settings)
    {
        var parsed = PaperOverrides.Parse(settings, out var error);
        Assert.Null(error);
        return parsed!.ApplyLayout(LayoutPlan.Default).EngravingStyle;
    }

    [Fact]
    public void TheKeys_SetLilyPondsProperties()
    {
        var s = StyleOf("lineThickness=0.15", "StaffLine.thickness=1.2", "LedgerLine.thickness=2,0.05",
            "LedgerLine.lengthFraction=0.4", "Stem.thickness=2", "Stem.lengthFraction=1.2", "Beam.thickness=0.6",
            "Beam.damping=0", "Dots.padding=0.6", "Accidental.rightPadding=0", "NoteHead.scale=1.2599210498948732",
            "BarLine.thinThickness=3", "BarLine.thickThickness=7");
        Assert.Equal(0.15, s.LineThickness);
        Assert.Equal(1.2, s.StaffSymbolThickness);
        Assert.Equal(2, s.LedgerLineThicknessLines);
        Assert.Equal(0.05, s.LedgerLineThicknessSpaces);
        Assert.Equal(0.4, s.LedgerLengthFraction);
        Assert.Equal(2, s.StemThickness);
        Assert.Equal(1.2, s.StemLengthFraction);
        Assert.Equal(0.6, s.BeamThickness);
        Assert.Equal(0, s.BeamDamping);
        Assert.Equal(0.6, s.DotPadding);
        Assert.Equal(0, s.AccidentalRightPadding);
        Assert.Null(EngravingStyle.Default.DotPadding);   // one dot's own width
        Assert.Equal(2, s.NoteHeadFontSizeStep, 12);       // 2^(2/6): LilyPond's font-size 2
        Assert.Equal(0, EngravingStyle.Default.NoteHeadFontSizeStep);
        Assert.Equal(3, s.BarLineHairThickness);
        Assert.Equal(7, s.BarLineThickThickness);
        Assert.Equal(EngravingStyle.Default, StyleOf("spacingIncrement=1.6"));
    }

    [Theory]
    [InlineData("lineThickness=0")]
    [InlineData("lineThickness=0.1mm")]
    [InlineData("Stem.thickness=-1")]
    [InlineData("Stem.lengthFraction=x")]
    [InlineData("LedgerLine.thickness=1")]
    [InlineData("LedgerLine.thickness=0,0")]
    [InlineData("LedgerLine.thickness=1,-0.1")]
    [InlineData("LedgerLine.lengthFraction=0")]
    [InlineData("Beam.damping=-1")]
    [InlineData("Dots.padding=-0.1")]
    [InlineData("Accidental.rightPadding=x")]
    [InlineData("NoteHead.scale=0")]
    [InlineData("NoteHead.scale=-1.1")]
    [InlineData("Stem.length=3.5")]
    public void AValueThatDoesNotRead_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>The scope holds a style and gives the last one back; outside every scope the
    /// constants are LilyPond's.</summary>
    [Fact]
    public void TheStyle_IsHeldForItsScopeOnly()
    {
        Assert.Equal(0.1, EngravingDefaults.LineThickness);
        Assert.Equal(0.13, EngravingDefaults.StemThickness, 12);
        using (EngravingStyle.Use(EngravingStyle.Default with { LineThickness = 0.2, StemLengthFraction = 1.5 }))
        {
            Assert.Equal(0.2, EngravingDefaults.LineThickness);
            Assert.Equal(1.5, Core.Svg.Layout.StemDetails.Default.LengthFraction);
            using (EngravingStyle.Use(EngravingStyle.Default))
                Assert.Same(EngravingStyle.DefaultStemDetails, Core.Svg.Layout.StemDetails.Default);
            Assert.Equal(0.2, EngravingDefaults.LineThickness);
        }
        Assert.Equal(0.1, EngravingDefaults.LineThickness);
        Assert.Same(EngravingStyle.DefaultStemDetails, Core.Svg.Layout.StemDetails.Default);
    }

    private const string Book = """
        octave absolute
        part m { clef treble
          section A { c'8 d' e' f' g'4 a'' | b16 c' d' e' f'8 g' a'2 | c'''4 b'' a'' g'' | e2 c''2 | }
        }
        form main { A }
        score main { staff m }
        """;

    private static string Svg(string book, params string[] settings) =>
        SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions
        {
            EmbedFont = false,
            PaperOverrides = settings.Length == 0 ? null : PaperOverrides.Parse(settings, out _),
        });

    private static string[] StrokeWidths(string svg) =>
        [.. Regex.Matches(svg, "stroke-width=\"([\\d.]+)\"").Select(m => m.Groups[1].Value).Distinct().Order()];

    private static string[] BarWidths(string svg) =>
        [.. Regex.Matches(svg, "<rect x=\"[\\d.]+\" y=\"[\\d.]+\" width=\"([\\d.]+)\" height=\"4.00\"").Select(m => m.Groups[1].Value).Distinct()];

    /// <summary>
    /// The thicknesses LilyPond 2.26.0 draws the same book with under the same overrides (Lab
    /// sessions/p822/p4, the <c>lysc ly --pin-fonts</c> twin with <c>line-thickness = 0.75\pt</c>,
    /// <c>ledger-line-thickness = #'(2 . 0.1)</c>, <c>BarLine.hair-thickness = 3</c>,
    /// <c>Stem.thickness = 2</c>): staff lines 0.15, stems 0.30, ledger lines 2 × 0.15 + 0.1,
    /// thin bar lines 3 × 0.15.
    /// </summary>
    [Fact]
    public void TheThicknesses_AreLilyPonds()
    {
        string plain = Svg(Book);
        Assert.Equal(["0.100", "0.130", "0.200"], StrokeWidths(plain));   // staff, stem, ledger
        Assert.Equal(["0.19"], BarWidths(plain));

        string set = Svg(Book, "lineThickness=0.15", "Stem.thickness=2", "BarLine.thinThickness=3",
            "LedgerLine.thickness=2,0.1");
        Assert.Equal(["0.150", "0.300", "0.400"], StrokeWidths(set));
        Assert.Equal(["0.45"], BarWidths(set));
    }

    /// <summary>
    /// StaffSymbol.thickness is the staff's line thickness, which nearly every line is stated
    /// in — the stems follow it — but the bar lines read the paper's line-thickness, as
    /// LilyPond's do (scm/bar-line.scm make-simple-bar-line).
    /// </summary>
    [Fact]
    public void TheStaffsThickness_MovesTheStemsAndNotTheBarLines()
    {
        string set = Svg(Book, "StaffLine.thickness=2");
        Assert.Equal(["0.200", "0.260", "0.300"], StrokeWidths(set));      // staff, 1.3 × 0.2, 0.2 + 0.1
        Assert.Equal(["0.19"], BarWidths(set));
    }

    private static double[] LedgerLengths(string svg) =>
        [.. Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.200\"")
            .Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)
                         - double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Order()];

    /// <summary>
    /// The 27 ledger lines LilyPond 2.26.0 draws for the same book at its default and under
    /// <c>\override LedgerLineSpanner.length-fraction = #0.4</c> (Lab sessions/p824/ledger, the
    /// <c>lysc ly --pin-fonts</c> twin): black heads 1.9563 → 2.3476, half 2.0661 → 2.4793, whole
    /// 2.9430 → 3.5316 — each head's width × (1 + 2 × length-fraction). LilyPond's spacing rods
    /// read minimum-length-fraction, so nothing else on the page moves, as there.
    /// </summary>
    [Fact]
    public void TheLedgerLength_IsLilyPonds_AndMovesNothingElse()
    {
        string book = Book.Replace("e2 c''2 |", "e2 c''2 | a,1 |", StringComparison.Ordinal);
        string plain = Svg(book);
        string set = Svg(book, "LedgerLine.lengthFraction=0.4");
        void AreLilyPonds(double black, double half, double whole, double[] drawn)
        {
            double[] lilyPond = [.. Enumerable.Repeat(black, 22).Concat(Enumerable.Repeat(half, 3))
                .Concat(Enumerable.Repeat(whole, 2)).Order()];
            Assert.Equal(lilyPond.Length, drawn.Length);
            for (int i = 0; i < drawn.Length; i++)
                Assert.InRange(drawn[i] - lilyPond[i], -0.015, 0.015);   // the svg's two decimals
        }
        AreLilyPonds(1.9563, 2.0661, 2.9430, LedgerLengths(plain));
        AreLilyPonds(2.3476, 2.4793, 3.5316, LedgerLengths(set));
        static string Rest(string svg) => Regex.Replace(svg, "<line[^>]*stroke-width=\"0.200\"/>", "");
        Assert.Equal(Rest(plain), Rest(set));
    }

    private const string Beamed = """
        octave absolute
        part m { clef treble
          section A { c'8 d' e' f' g' a' b' c'' | b16 c' d' e' f' g' a' b' e''8 d'' c'' b' | a'8 g' f' e' d''16 e'' f'' g'' a''8 g'' | }
        }
        form main { A }
        score main { staff m }
        """;

    private static double[] StemLengths(string svg, string width) =>
        [.. Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"([-\\d.]+)\" x2=\"([-\\d.]+)\" y2=\"([-\\d.]+)\"[^>]*stroke-width=\"" + Regex.Escape(width) + "\"")
            .Select(m => Math.Abs(double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)
                                  - double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .Order()];

    /// <summary>
    /// The 30 beamed stems' lengths LilyPond 2.26.0 draws (sorted; Lab sessions/p822/p4, the
    /// twin, stem rects) at its defaults and under <c>line-thickness = 0.75\pt</c>,
    /// <c>Stem.thickness = 2</c>, <c>Stem.length-fraction = 1.2</c>, <c>Beam.beam-thickness = 0.6</c>.
    /// Lily#'s stem is drawn 0.03 longer than LilyPond's rect at the default — the drawing's
    /// convention, measured 0.027 … 0.036 over all 30 — and stays so under the settings: the
    /// beams were placed where LilyPond places them.
    /// </summary>
    [Fact]
    public void TheBeamedStems_AreLilyPondsUnderTheSettings()
    {
        double[] lilyPondDefault =
        [
            2.4970, 2.4970, 2.5025, 2.6160, 2.6160, 2.6682, 2.6682, 2.8394, 2.8394, 2.8512,
            2.8512, 2.9400, 3.0106, 3.0106, 3.0864, 3.0864, 3.3138, 3.3216, 3.3216, 3.3776,
            3.8138, 3.8151, 4.3138, 4.3138, 4.8138, 4.8138, 5.3138, 5.8138, 6.0000, 6.3176,
        ];
        double[] lilyPondSet =
        [
            3.0768, 3.2983, 3.2983, 3.3265, 3.4753, 3.4753, 3.5353, 3.5672, 3.5672, 3.5761,
            3.6523, 3.6523, 3.7483, 3.7483, 3.8258, 3.8293, 3.8293, 3.9293, 3.9293, 3.9626,
            4.1104, 4.1104, 4.3138, 4.3900, 4.8138, 4.8173, 5.3138, 5.8138, 6.0289, 6.3237,
        ];
        var plain = StemLengths(Svg(Beamed), "0.130");
        var set = StemLengths(Svg(Beamed, "lineThickness=0.15", "Stem.thickness=2", "Stem.lengthFraction=1.2",
            "Beam.thickness=0.6"), "0.300");
        Assert.Equal(30, plain.Length);
        Assert.Equal(30, set.Length);
        for (int i = 0; i < 30; i++)
        {
            Assert.InRange(plain[i] - lilyPondDefault[i], 0.02, 0.045);
            Assert.InRange(set[i] - lilyPondSet[i], 0.02, 0.045);
        }
    }

    /// <summary>
    /// The 35 stems LilyPond 2.26.0 draws (sorted; Lab sessions/p825/damp, the
    /// <c>lysc ly --pin-fonts</c> twin, stem rects) for a book whose beams leap, under
    /// <c>\override Beam.damping</c> = 0 (no damping), 3 and 10000 (flat). The beams follow
    /// LilyPond's to the drawing's 0.03 at each (as <see cref="TheBeamedStems_AreLilyPondsUnderTheSettings"/>),
    /// and the three are three different pages.
    /// </summary>
    [Theory]
    [InlineData("0", new[]
    {
        2.4926, 2.4926, 2.5007, 2.5137, 2.5401, 2.5401, 2.5875, 2.5875, 2.6139, 2.6350, 2.6350, 2.8009,
        2.8761, 2.9128, 3.0248, 3.0671, 3.1367, 3.2515, 3.2976, 3.3084, 3.3138, 3.3192, 3.3300, 3.6269,
        3.8138, 4.3138, 4.3138, 4.8138, 4.8138, 5.3138, 5.8138, 5.9948, 6.0605, 6.1328, 8.2756,
    })]
    [InlineData("3", new[]
    {
        2.4956, 2.5007, 2.5007, 2.5023, 2.8108, 2.8117, 2.8117, 2.8761, 2.8761, 2.9400, 3.1268, 3.2498,
        3.2498, 3.2515, 3.2515, 3.3138, 3.3776, 3.6269, 3.6269, 3.6878, 3.6878, 3.8138, 3.8153, 4.1259,
        4.1259, 4.3138, 4.3138, 4.8138, 4.8138, 5.3138, 5.8138, 5.9993, 6.3183, 7.6320, 8.7071,
    })]
    [InlineData("10000", new[]
    {
        2.3138, 2.3138, 2.3138, 2.5038, 2.6238, 2.6238, 2.6238, 2.8138, 2.8138, 2.8138, 3.1238, 3.1238,
        3.1238, 3.3138, 3.3138, 3.3138, 3.3138, 3.6238, 3.6238, 3.8138, 3.8138, 3.8138, 3.8138, 4.1238,
        4.1238, 4.3138, 4.3138, 4.8138, 4.8138, 5.3138, 5.8138, 5.8138, 6.3138, 8.0038, 8.6238,
    })]
    public void TheBeamDamping_IsLilyPonds(string damping, double[] lilyPond)
    {
        string book = Beamed.Replace("a''8 g'' | }", "a''8 g'' | c'8 g'' d'16 b'' e'8 | }", StringComparison.Ordinal);
        string svg = Svg(book, "Beam.damping=" + damping);
        var drawn = StemLengths(svg, "0.130");
        Assert.Equal(lilyPond.Length, drawn.Length);
        for (int i = 0; i < drawn.Length; i++)
            Assert.InRange(drawn[i] - lilyPond[i], 0.02, 0.045);
        Assert.NotEqual(Svg(book), svg);
    }

    private const string DottedAndSharp = """
        octave absolute
        part m { clef treble
          section A { c'4. d'8 e'4.. f'16 | r4. g'8 <c' e' g'>4. r8 | fis'4 bes' <cis'' e'' gis''>2 | aes'2. b'4 | }
        }
        form main { A }
        score main { staff m }
        """;

    private static double[] GlyphXs(string svg, params char[] glyphs) =>
        [.. Regex.Matches(svg, "<text class=\"music\" x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"4.00\"[^>]*>(.)</text>")
            .Where(m => glyphs.Contains(m.Groups[2].Value[0]))
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order()];

    /// <summary>
    /// <c>Dots.padding</c> and <c>Accidental.rightPadding</c> against LilyPond 2.26.0 (Lab
    /// sessions/p826/dotacc, the <c>lysc ly --pin-fonts</c> twin) at its defaults and under
    /// <c>\override DotColumn.padding = #0.7</c>, <c>\override AccidentalPlacement.right-padding = #0.4</c>:
    /// each dot's distance from the head or rest before it, each accidental's from the head after
    /// it, and the columns — the accidentals' wider reach moves the bars after them, as there.
    /// </summary>
    [Theory]
    [InlineData(false, new[] { 1.754, 1.754, 2.654, 1.400, 1.754, 1.754, 1.754, 1.827 },
        new[] { 1.450, 1.150, 2.514, 1.450, 1.150 },
        new[] { 0.00, 3.70, 5.81, 9.78, 12.67, 16.02, 18.13, 21.83, 25.87, 28.90, 33.31, 39.51, 44.48 })]
    [InlineData(true, new[] { 2.004, 2.004, 2.904, 1.650, 2.004, 2.004, 2.004, 2.077 },
        new[] { 1.700, 1.400, 2.764, 1.700, 1.400 },
        new[] { 0.00, 3.70, 5.81, 9.78, 12.67, 16.02, 18.13, 21.83, 26.12, 29.40, 34.06, 40.51, 45.48 })]
    public void TheDotAndAccidentalGaps_AreLilyPonds(bool set, double[] dotGaps, double[] accidentalGaps, double[] columns)
    {
        // A default render first, on this thread: the placement kept in static fields must not
        // carry its padding into the next score (it did, session 826 — the accidentals stayed
        // where the default put them while the bars moved for the new gap).
        string plain = Svg(DottedAndSharp);
        string svg = set ? Svg(DottedAndSharp, "Dots.padding=0.7", "Accidental.rightPadding=0.4") : plain;
        // Emmentaler: the dot, the black and half heads, the quarter and eighth rests, ♯ and ♭.
        double[] heads = GlyphXs(svg, '\uE0FE', '\uE0FD', '\uE008', '\uE00B');
        double[] dots = GlyphXs(svg, '\uE038');
        double[] accidentals = GlyphXs(svg, '\uE013', '\uE021');
        static void Near(double[] lilyPond, double[] drawn)
        {
            Assert.Equal(lilyPond.Length, drawn.Length);
            for (int i = 0; i < drawn.Length; i++)
                Assert.InRange(drawn[i] - lilyPond[i], -0.011, 0.011);   // the svg's two decimals
        }
        Near(dotGaps, [.. dots.Select(d => d - heads.Where(h => h < d).Max())]);
        Near(accidentalGaps, [.. accidentals.Select(a => heads.Where(h => h > a).Min() - a)]);
        Near(columns, [.. heads.Distinct().Select(h => h - heads[0])]);
    }

    private const string HeadBook = """
        paper { raggedRight }
        octave absolute
        part m { clef treble
          section A { c'1 | d'2. g''4 | <c' d' f'>4 <f'' g''>8 a'' e'4. f'8 | b4~ b8 c''( d'' e'') a4 | }
        }
        form main { A }
        score main { staff m }
        """;

    // Every music glyph, whatever face and size it is drawn at: (char, x, font-size, face or "").
    private static (char Glyph, double X, string Size, string Face)[] Glyphs(string svg) =>
        [.. Regex.Matches(svg, "<text class=\"music\"(?: font-family=\"([^\"]*)\")? x=\"([-\\d.]+)\" y=\"[-\\d.]+\" font-size=\"([\\d.]+)\"[^>]*>(.)</text>")
            .Select(m => (m.Groups[4].Value[0], double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                m.Groups[3].Value, m.Groups[1].Value))];

    /// <summary>
    /// <c>NoteHead.scale</c> against LilyPond 2.26.0's <c>\override NoteHead.font-size</c> (Lab
    /// sessions/p827/nh, the <c>lysc ly --pin-fonts</c> twin, ragged right) at 0, +1.2 and −0.9 —
    /// <c>NoteHead.scale</c> 2^(1.2/6) and 2^(−0.9/6): the heads are drawn at that size out of the
    /// design LilyPond picks (the 23 for +1.2, the 18 for −0.9), and the columns, which the wider
    /// heads push apart, the ledger lines, which reach a quarter of a head past it, and the stems,
    /// which stand on its attachment point, are LilyPond's.
    /// </summary>
    [Theory]
    [InlineData("1", "4.00", "", new[] { 0.00, 8.01, 13.94, 18.19, 19.43, 22.14, 23.38, 25.89, 28.14, 32.55, 36.25, 39.95, 42.70, 45.21, 47.71, 49.79 },
        new[] { 1.956, 3.196 }, new[] { 8.07, 14.00, 19.49, 23.45, 25.95, 28.21, 32.61, 36.31, 40.01, 42.77, 45.27, 47.78, 51.03 })]
    [InlineData("1.148698", "4.59", "Emmentaler-23", new[] { 0.00, 8.31, 14.45, 18.89, 20.33, 23.04, 24.47, 27.17, 29.62, 34.22, 38.12, 42.01, 44.96, 47.66, 50.36, 52.63 },
        new[] { 2.249, 3.683 }, new[] { 8.38, 14.51, 20.39, 24.54, 27.24, 29.69, 34.29, 38.18, 42.08, 45.03, 47.73, 50.43, 54.07 })]
    [InlineData("0.901250", "3.60", "Emmentaler-18", new[] { 0.00, 7.80, 13.60, 17.72, 18.83, 21.54, 22.65, 25.03, 27.15, 31.43, 35.00, 38.57, 41.19, 43.57, 45.94, 47.89 },
        new[] { 1.761, 2.870 }, new[] { 7.87, 13.66, 18.89, 22.72, 25.09, 27.22, 31.49, 35.06, 38.64, 41.26, 43.63, 46.01, 49.00 })]
    public void TheNoteHeadScale_IsLilyPonds(string scale, string size, string face, double[] columns, double[] ledgerWidths, double[] stems)
    {
        // A default render first, on this thread: nothing read at the first style may stay.
        Svg(HeadBook);
        string svg = Svg(HeadBook, "NoteHead.scale=" + scale);
        // Emmentaler: the black, half and whole heads.
        var heads = Glyphs(svg).Where(g => g.Glyph is '' or '' or '').ToArray();
        Assert.Equal(17, heads.Length);
        Assert.All(heads, h => Assert.Equal(size, h.Size));
        Assert.All(heads, h => Assert.StartsWith(face, h.Face, StringComparison.Ordinal));
        double[] xs = [.. heads.Select(h => h.X).Distinct().Order()];
        Assert.Equal(columns.Length, xs.Length);
        for (int i = 0; i < xs.Length; i++)
            Assert.InRange(xs[i] - xs[0] - columns[i], -0.011, 0.011);   // the svg's two decimals
        // Each drawn ledger is one of LilyPond's lengths (its two ends rounded apart: ±0.011), and
        // each of LilyPond's is drawn.
        double[] ledgers = LedgerLengths(svg);
        Assert.All(ledgers, w => Assert.Contains(ledgerWidths, lp => Math.Abs(w - lp) <= 0.011));
        Assert.All(ledgerWidths, lp => Assert.Contains(ledgers, w => Math.Abs(w - lp) <= 0.011));
        // Each stem stands on its head's attachment point, which the head's font answers.
        double[] stemXs = [.. Regex.Matches(svg, "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"\\1\"[^>]*stroke-width=\"0.130\"")
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Order()];
        Assert.Equal(stems.Length, stemXs.Length);
        for (int i = 0; i < stemXs.Length; i++)
            Assert.InRange(stemXs[i] - xs[0] - stems[i], -0.011, 0.011);
    }

    /// <summary>
    /// The whole line's natural length (ragged right) under <c>NoteHead.scale</c> against the twin
    /// (Lab sessions/p827/nh, book c): ties, a slur, scripts, dynamics, a tuplet, two voices and a
    /// dotted chord — every column whose readers never named a font, and read the twenty's head
    /// until the font-less overloads answered the staff's (<c>GlyphMetrics.StaffHeadFont</c>; 0.1
    /// short at +1.2 without it).
    /// </summary>
    [Theory]
    [InlineData("1", 72.658)]
    [InlineData("1.148698", 76.475)]
    [InlineData("0.901250", 70.114)]
    public void TheNoteHeadScale_LineIsLilyPonds(string scale, double lilyPond)
    {
        const string book = """
            paper { raggedRight }
            octave absolute
            part m { clef treble
              section A { <e' g'>4~ <e' g'>8 a'8@staccato b'4@accent c''8( b' | a'4)@p g'4@f tuplet 3/2 { f'8 e' d' } c'4@fermata | voice { e'4 f' g' a' } { e'4 e' g'8 f' e'4 } | <f' g'>2.. r8 | }
            }
            form main { A }
            score main { staff m }
            """;
        var line = Regex.Match(Svg(book, "NoteHead.scale=" + scale),
            "<line x1=\"([-\\d.]+)\" y1=\"[-\\d.]+\" x2=\"([-\\d.]+)\"[^>]*stroke-width=\"0.100\"");
        double length = double.Parse(line.Groups[2].Value, CultureInfo.InvariantCulture)
            - double.Parse(line.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(length - lilyPond, -0.011, 0.011);
    }

    /// <summary>
    /// What <c>NoteHead.scale</c> does not reach, as LilyPond's <c>\override NoteHead.font-size</c>
    /// in the <c>\Score</c> context does not (the twin, Lab sessions/p827/nh): a grace head states
    /// its own −3, which replaces it (2.83 either way); the accidental, the dot, the flag and the
    /// clef keep their size; a cue head adds the two (−4 + the scale's step).
    /// </summary>
    [Fact]
    public void TheNoteHeadScale_ReachesTheHeadsOnly()
    {
        const string book = """
            octave absolute
            part m { clef treble
              section A { grace { d''16 } c''4 fis'8. g'16 cue { a'4 } r4 | }
            }
            form main { A }
            score main { staff m }
            """;
        var plain = Glyphs(Svg(book));
        var set = Glyphs(Svg(book, "NoteHead.scale=1.148698"));
        Assert.Equal(plain.Length, set.Length);
        int heads = 0;
        for (int i = 0; i < plain.Length; i++)
        {
            Assert.Equal(plain[i].Glyph, set[i].Glyph);
            bool head = plain[i].Glyph is '' or '' or '';
            if (!head || plain[i].Size == "2.83")             // not a head, or the grace's
            {
                Assert.Equal(plain[i].Size, set[i].Size);
                continue;
            }
            heads++;
            string expected = plain[i].Size == "4.00" ? "4.59" : "2.89";   // magstep(1.2), magstep(1.2 − 4)
            Assert.Equal(expected, set[i].Size);
        }
        Assert.Equal(4, heads);
    }

    /// <summary>
    /// Two voices' accidentals on one column are packed at COLLECT time
    /// (<c>StaffAccidentalColumns</c>), which holds no style of its own: the collector opens the
    /// score's for it, so the packed flats move with <c>Accidental.rightPadding</c> exactly as a
    /// single note's does — 0.25 further left for 0.4 against 0.15.
    /// </summary>
    [Fact]
    public void TwoVoicesPackedAccidentals_FollowTheRightPadding()
    {
        const string book = """
            octave absolute
            part m { clef treble
              section A { << { aes'2 bes'2 } \\ { ges'2 ees'2 } >> | }
            }
            form main { A }
            score main { staff m }
            """;
        double[] Gaps(string svg)
        {
            double[] heads = GlyphXs(svg, '');
            return [.. GlyphXs(svg, '').Select(a => heads.Where(h => h > a).Min() - a)];
        }
        double[] plain = Gaps(Svg(book));
        double[] set = Gaps(Svg(book, "Accidental.rightPadding=0.4"));
        Assert.Equal(4, plain.Length);
        Assert.Equal(4, set.Length);
        for (int i = 0; i < 4; i++)
            Assert.InRange(set[i] - plain[i], 0.24, 0.26);
    }

    /// <summary>
    /// The keys are the language's (owner's decision 2026-10-05): the file writes them in
    /// <c>layout { }</c>, a score overrides them through a named block, and <c>--set</c> wins
    /// over both — and each reaches the page.
    /// </summary>
    [Fact]
    public void TheFile_WritesTheStyle_AScoreOverridesIt_AndTheSettingWins()
    {
        const string music = """
            octave absolute
            part m { clef treble
              section A { c'8 d' e' f' g'4 a'' | b16 c' d' e' f'8 g' a'2 | }
            }
            form main { A }
            """;
        string file = "layout { lineThickness 0.15  Stem.thickness 2 }\n" + music + "score main { staff m }\n";
        Assert.Equal(["0.150", "0.250", "0.300"], StrokeWidths(Svg(file)));                // ledger 0.15 + 0.1
        Assert.Equal(["0.150", "0.250", "0.450"], StrokeWidths(Svg(file, "Stem.thickness=3")));

        string scored = "layout thin { lineThickness 0.08 }\n" + music
            + "score main { layout thin { Stem.thickness 1.0 }  staff m }\n";
        Assert.Equal(["0.080", "0.180"], StrokeWidths(Svg(scored)));                        // stem 1.0 × 0.08 joins the staff

        var tree = SyntaxTree.Parse(file);
        var layout = tree.GetRoot().DescendantNodes<LayoutDeclarationSyntax>().First();
        LayoutPlanReader.Read(layout, out var problems);
        Assert.Empty(problems);
    }

    /// <summary>The <c>lysc ly</c> twin writes the style in LilyPond's words — and nothing at the
    /// defaults, so a book that writes none twins as before.</summary>
    [Fact]
    public void TheTwin_SpellsTheStyleInLilyPond()
    {
        string Twin(string layout) => new Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(
            layout + "part m { }\nsection A { m { c'1 | } }\nform main { A }\nscore main { staff m }\n"));
        string styled = Twin("layout { lineThickness 0.15  StaffLine.thickness 1.2  LedgerLine.thickness 2 0.1  "
            + "LedgerLine.lengthFraction 0.4  Stem.thickness 2 Stem.lengthFraction 1.2  Beam.thickness 0.6  Beam.damping 3  Dots.padding 0.7  Accidental.rightPadding 0.4  NoteHead.scale 1.1  BarLine.thinThickness 3  BarLine.thickThickness 7 }\n");
        foreach (string line in new[]
        {
            "line-thickness = 0.75\\pt", "\\override StaffSymbol.thickness = #1.2",
            "\\override StaffSymbol.ledger-line-thickness = #'(2 . 0.1)",
            "\\override LedgerLineSpanner.length-fraction = #0.4", "\\override Stem.thickness = #2",
            "\\override Stem.length-fraction = #1.2", "\\override Beam.beam-thickness = #0.6",
            "\\override Beam.damping = #3", "\\override DotColumn.padding = #0.7",
            "\\override AccidentalPlacement.right-padding = #0.4",
            "\\override NoteHead.font-size = #(magnification->font-size 1.1)",
            "\\override BarLine.hair-thickness = #3", "\\override BarLine.thick-thickness = #7",
        })
            Assert.Contains(line, styled, StringComparison.Ordinal);
        string plain = Twin("");
        Assert.DoesNotContain("line-thickness", plain, StringComparison.Ordinal);
        Assert.Equal(plain, Twin("layout { Stem.thickness 1.3  lineThickness 0.1 }\n"));
    }

    /// <summary>A setting reaches the PNG and the PDF as well: the same layout, drawn.</summary>
    [Fact]
    public void TheStyle_ChangesEveryPicture_AndNothingWithoutIt()
    {
        var tree = SyntaxTree.Parse(Book);
        var set = PaperOverrides.Parse(["lineThickness=0.2"], out _);
        Assert.NotEqual(Core.Png.PngGenerator.Generate(tree),
            Core.Png.PngGenerator.Generate(tree, new Core.Png.PngRenderOptions { PaperOverrides = set }));
        Assert.Equal(Svg(Book), Svg(Book, "spacingIncrement=1.2"));
    }
}
