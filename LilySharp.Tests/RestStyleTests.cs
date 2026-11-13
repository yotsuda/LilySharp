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

using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>layout { restStyle default | classical | z }</c> and <c>lysc --set restStyle=z</c> (OMR
/// request #26, 2026-11-12; built 2026-11-13): which quarter rest a score draws — LilyPond's
/// <c>Rest.style</c>. The glyph changes and every reader of its box follows it; the value, the
/// MIDI and the MusicXML do not.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/rest.cc:166-227 Rest::glyph_name — <c>classical</c> and <c>z</c> append the
/// style to <c>rests.2</c> and to no other duration. The LilyPond numbers below were read off
/// LilyPond 2.26.0 on the request's own repro, the <c>lysc ly</c> twin with
/// <c>\override Staff.Rest.style</c> added (Lab sessions/p1226/md/req26, lp-*.ly and lpx-*.ly).
/// </remarks>
[Trait("Category", "Unit")]
public class RestStyleTests
{
    // The request's repro (z-rest-2026-11-12.lys): bars 1-2 are a left hand in two voices, the
    // lower one ending on a quarter rest under the upper voice's chord; bar 3 is one voice with
    // the rest in the staff.
    private static string Repro(string layout) => $$"""
        title "Z Rest"
        octave absolute
        time 2/2
        key c major
        {{layout}}
        part lh { clef bass }

        section Piece {
          lh {
            voice { r2 <g, c e g>4@staccato <g, c e>4@staccato } { <c, g,>2~ <c, g,>4 r4 } |
            voice { r2 <g, c e g>4@staccato <g, c e>4@staccato } { <c, c>2~ <c, c>4 r4 } |
            c4 r4 e4 r4 |
          }
        }

        form { ~Piece }

        score {
          staff lh "Piano"
        }
        """;

    // Every duration beside the quarter, dotted rests, and a second two-voice bar.
    private static string Durations(string layout) => $$"""
        octave absolute
        time 4/4
        {{layout}}
        part m { clef bass }
        section A {
          m {
            r4. c8 r4. r8 |
            voice { r4 c r4. c8 } { c,4 r4 r4. c,8 } |
            r1 |
            r2 r4 r8 r16 r16 |
            r32 r32 r16 r8 r4 r2 |
            R1*2 |
          }
        }
        form { A }
        score { staff m }
        """;

    private static string Layout(string word) => word.Length == 0 ? "" : "layout { restStyle " + word + " }";

    private static char Char(MusicGlyph glyph) => MusicFont.Current.Codepoint(glyph);

    private static MusicGlyph QuarterOf(string word) => word switch
    {
        "classical" => MusicGlyph.RestQuarterOld,
        "z" => MusicGlyph.RestQuarterZ,
        _ => MusicGlyph.RestQuarter,
    };

    private static EngravingStyle StyleOf(string word)
        => EngravingStyle.Default with { RestStyle = RestStyleKey.Find(word)!.Value };

    // ---- the glyph ----

    [Theory]
    [InlineData("default", "rests.2")]
    [InlineData("classical", "rests.2classical")]
    [InlineData("z", "rests.2z")]
    public void TheQuarterRest_IsTheStylesGlyph_AndNoOtherDurationChanges(string word, string feta)
    {
        using (EngravingStyle.Use(StyleOf(word)))
        {
            var quarter = MusicGlyphs.Rest(4, 0);
            Assert.Equal(QuarterOf(word), quarter);
            Assert.Equal(feta, MusicGlyphs.Table.Single(e => e.Glyph == quarter).FetaName);
            // LilyPond: "these styles differ from the default in quarter rests only".
            Assert.Equal(MusicGlyph.RestDoubleWhole, MusicGlyphs.Rest(0, 0));
            Assert.Equal(MusicGlyph.RestWhole, MusicGlyphs.Rest(1, 2));
            Assert.Equal(MusicGlyph.RestHalf, MusicGlyphs.Rest(2, 0));
            Assert.Equal(MusicGlyph.RestHalfLegerLine, MusicGlyphs.Rest(2, 7));
            Assert.Equal(MusicGlyph.Rest8th, MusicGlyphs.Rest(8, 0));
            Assert.Equal(MusicGlyph.Rest16th, MusicGlyphs.Rest(16, 0));
            Assert.Equal(MusicGlyph.Rest32nd, MusicGlyphs.Rest(32, 0));
            Assert.Equal(MusicGlyph.Rest64th, MusicGlyphs.Rest(64, 0));
            Assert.Equal(MusicGlyph.Rest128th, MusicGlyphs.Rest(128, 0));
        }
        Assert.Equal(MusicGlyph.RestQuarter, MusicGlyphs.Rest(4, 0));
    }

    /// <summary>The box every reader of a quarter rest asks for is the chosen glyph's — LilyPond's
    /// measured extents (the Rest grob's X-extent and Y-extent under each style).</summary>
    [Theory]
    [InlineData("default", 0.95, -1.25, 1.5624)]
    [InlineData("classical", 1.0, -1.05, 0.82)]
    [InlineData("z", 1.125, -0.82, 0.82)]
    public void TheQuarterRestsBox_IsTheStylesGlyphs(string word, double right, double bottom, double top)
    {
        using (EngravingStyle.Use(StyleOf(word)))
        {
            var box = GlyphMetrics.GetRestBBox(4);
            Assert.Equal(0.0, box.Left, 6);
            Assert.Equal(right, box.Right, 6);
            Assert.Equal(bottom, box.Bottom, 6);
            Assert.Equal(top, box.Top, 6);
            // The skyline's outline is the same glyph's, and the eighth rest's box does not move.
            Assert.Equal(MusicFont.Current.FullSize.Outline(QuarterOf(word)), GlyphMetrics.GetRestSkylineBBox(4));
            Assert.Equal(new GlyphMetrics.BBox(0.0, -1.05, 1.0, 0.82), GlyphMetrics.GetRestBBox(8));
        }
    }

    [Fact]
    public void TheClassicalQuarterRest_HasTheEighthRestsBox()
        => Assert.Equal(MusicFont.Current.FullSize.Box(MusicGlyph.Rest8th),
            MusicFont.Current.FullSize.Box(MusicGlyph.RestQuarterOld));

    // ---- geometry against LilyPond 2.26.0 ----

    /// <summary>
    /// The repro's four quarter rests and the note after the third, against LilyPond: X from the
    /// bar-1 whole... half rest of the upper voice (the first glyph of the music, at the same
    /// place under every style), Y in staff spaces over the middle line.
    /// </summary>
    /// <remarks>
    /// MEASURED (LilyPond 2.26.0, lp-default.ly / lp-classical.ly / lp-z.ly; the system's X):
    /// <code>
    ///             half rest   r4 bar 1          r4 bar 2          r4 bar 3 (1)     e (bar 3)   r4 bar 3 (2)
    /// default     17.239227   24.578217 @ −5.0  36.680366 @ −5.0  43.722756 @ 0    46.370801   49.373046 @ 0
    /// classical   17.239227   24.578217 @ −4.5  36.705366 @ −4.5  43.772756 @ 0    46.470801   49.473046 @ 0
    /// z           17.239227   24.578217 @ −4.5  36.767866 @ −4.5  43.897756 @ 0    46.720801   49.723046 @ 0
    /// </code>
    /// Two things move with the glyph. The columns AFTER a quarter rest stand further right by
    /// what its box is wider (0.05 classical, 0.175 z — halved across the bar line in bar 1,
    /// where the rest is one of two voices). And the lower voice's rest under the chord stands
    /// half a space higher, because the shorter glyph clears the chord sooner (Rest_collision
    /// reads the rest's own extent).
    /// </remarks>
    [Theory]
    [InlineData("", 7.338990, -5.0, 19.441139, -5.0, 26.483529, 29.131574, 32.133819)]
    [InlineData("classical", 7.338990, -4.5, 19.466139, -4.5, 26.533529, 29.231574, 32.233819)]
    [InlineData("z", 7.338990, -4.5, 19.528639, -4.5, 26.658529, 29.481574, 32.483819)]
    public void TheRepro_StandsWhereLilyPondPutsIt(string word,
        double x1, double y1, double x2, double y2, double x3, double xE, double x4)
    {
        var g = RenderedGeometry.RenderProduct(Repro(Layout(word)));
        double middle = g.StaffRefpoints()[0];
        double origin = g.Glyphs.First(x => x.Glyph == Char(MusicGlyph.RestHalf)).X;
        var rests = g.Glyphs.Where(x => x.Glyph == Char(QuarterOf(word))).OrderBy(x => x.X).ToList();
        Assert.Equal(4, rests.Count);
        // No other quarter rest glyph is on the page.
        foreach (var other in new[] { MusicGlyph.RestQuarter, MusicGlyph.RestQuarterOld, MusicGlyph.RestQuarterZ })
            if (other != QuarterOf(word))
                Assert.DoesNotContain(g.Glyphs, x => x.Glyph == Char(other));

        Assert.Equal(x1, rests[0].X - origin, 4);
        Assert.Equal(y1, middle - rests[0].Y, 4);
        Assert.Equal(x2, rests[1].X - origin, 4);
        Assert.Equal(y2, middle - rests[1].Y, 4);
        Assert.Equal(x3, rests[2].X - origin, 4);
        Assert.Equal(0.0, middle - rests[2].Y, 4);
        Assert.Equal(x4, rests[3].X - origin, 4);
        Assert.Equal(0.0, middle - rests[3].Y, 4);
        // The e of bar 3: the note a quarter rest is followed by.
        var heads = g.Glyphs.Where(x => x.Glyph == Char(MusicGlyph.NoteheadBlack)).OrderBy(x => x.X).ToList();
        Assert.Equal(xE, heads[^1].X - origin, 4);
    }

    /// <summary>A dotted quarter rest's dot stands one dot's width past the chosen glyph's right
    /// edge — LilyPond: 1.4 / 1.45 / 1.575 from the rest's left (lpx-*.ly, <c>r4.</c>), the
    /// box's right edge plus DotColumn's padding 0.45.</summary>
    [Theory]
    [InlineData("", 1.4)]
    [InlineData("classical", 1.45)]
    [InlineData("z", 1.575)]
    public void ADottedQuarterRestsDot_FollowsTheGlyphsRightEdge(string word, double dotFromRest)
    {
        var g = RenderedGeometry.RenderProduct(Durations(Layout(word)));
        var rest = g.Glyphs.Where(x => x.Glyph == Char(QuarterOf(word))).OrderBy(x => x.X).First();
        var dot = g.Glyphs.Where(x => x.Glyph == Char(MusicGlyph.AugmentationDot)).OrderBy(x => x.X).First();
        Assert.Equal(dotFromRest, dot.X - rest.X, 4);
    }

    /// <summary>Two voices resting and sounding against each other (<c>{ r4 c … } { c,4 r4 … }</c>):
    /// the lower voice's quarter rest beside the upper voice's note stands at −3.5 by default and
    /// at −3.0 in either style — LilyPond's numbers (lpx-*.ly, bar 5) — and the two dotted rests
    /// of the second half stay at ±2.</summary>
    [Theory]
    [InlineData("", -3.5)]
    [InlineData("classical", -3.0)]
    [InlineData("z", -3.0)]
    public void TwoVoices_ShiftTheRestByItsOwnBox(string word, double lowerRest)
    {
        var g = RenderedGeometry.RenderProduct(Durations(Layout(word)));
        double middle = g.StaffRefpoints()[0];
        // Bar 2's quarter rests, left to right: the upper r4, the lower r4, the two r4. (one X).
        var rests = g.Glyphs.Where(x => x.Glyph == Char(QuarterOf(word))).OrderBy(x => x.X).ThenBy(x => x.Y).ToList();
        // Bar 1 holds two (r4. r4.), bar 2 four.
        Assert.Equal(2.0, middle - rests[2].Y, 4);
        Assert.Equal(lowerRest, middle - rests[3].Y, 4);
        Assert.Equal(2.0, middle - rests[4].Y, 4);
        Assert.Equal(-2.0, middle - rests[5].Y, 4);
    }

    /// <summary>Only the quarter rest's glyph differs: every other glyph of the page is drawn as
    /// often as by default — the eighth and shorter rests, the half and whole, and the
    /// multi-measure rest, which is another grob in LilyPond (MultiMeasureRest) and keeps its own.</summary>
    [Theory]
    [InlineData("classical")]
    [InlineData("z")]
    public void OnlyTheQuarterRestsGlyph_Differs(string word)
    {
        static System.Collections.Generic.Dictionary<char, int> Counts(RenderedGeometry g)
            => g.Glyphs.GroupBy(x => x.Glyph).ToDictionary(x => x.Key, x => x.Count());
        var plain = Counts(RenderedGeometry.RenderProduct(Durations("")));
        var styled = Counts(RenderedGeometry.RenderProduct(Durations(Layout(word))));
        char modern = Char(MusicGlyph.RestQuarter), chosen = Char(QuarterOf(word));
        Assert.Equal(plain[modern], styled[chosen]);
        Assert.False(styled.ContainsKey(modern));
        plain.Remove(modern);
        styled.Remove(chosen);
        Assert.Equal(plain.OrderBy(x => x.Key), styled.OrderBy(x => x.Key));
    }

    /// <summary>A cue's quarter rest is the style's glyph at the cue's size: the context's
    /// override reaches a CueVoice's Rest as it reaches every voice's.</summary>
    [Fact]
    public void ACuesQuarterRest_IsTheStylesGlyph_AtTheCuesSize()
    {
        const string book = """
            octave absolute
            time 4/4
            layout { restStyle z }
            part m { clef treble }
            section A { m { c''4 r4 cue { c''4 r4 } | } }
            form { A }
            score { staff m }
            """;
        var g = RenderedGeometry.RenderProduct(book);
        var rests = g.Glyphs.Where(x => x.Glyph is var c && (c == Char(MusicGlyph.RestQuarterZ)
            || MusicFont.Current.GlyphOf(c) == MusicGlyph.RestQuarterZ)).OrderBy(x => x.X).ToList();
        Assert.Equal(2, rests.Count);
        Assert.True(rests[1].FontSize < rests[0].FontSize);
        Assert.DoesNotContain(g.Glyphs, x => MusicFont.Current.GlyphOf(x.Glyph) == MusicGlyph.RestQuarter);
    }

    // ---- the default does not move ----

    private static string Svg(string book, params string[] settings) =>
        SvgGenerator.Generate(SyntaxTree.Parse(book), new SvgRenderOptions
        {
            EmbedFont = false,
            PaperOverrides = settings.Length == 0 ? null : PaperOverrides.Parse(settings, out _),
        });

    private static string Picture(string svg) => Regex.Replace(svg, "data-pos=\"[^\"]*\"", "");

    [Fact]
    public void TheDefault_IsThePictureOfABookThatWritesNoStyle()
    {
        string plain = Svg(Repro(""));
        // The word `default` is the default: the same picture (the source positions move by the
        // block's own characters).
        Assert.Equal(Picture(plain), Picture(Svg(Repro(Layout("default")))));
        Assert.Equal(plain, Svg(Repro(""), "restStyle=default"));
        // ...and a style drawn in between leaves nothing behind.
        Assert.NotEqual(plain, Svg(Repro(""), "restStyle=z"));
        Assert.Equal(plain, Svg(Repro("")));
        Assert.Equal(EngravingStyle.Default, PaperOverrides.Parse(["restStyle=default"], out _)!
            .ApplyLayout(LayoutPlan.Default).EngravingStyle);
    }

    // ---- --set ----

    [Theory]
    [InlineData("classical")]
    [InlineData("z")]
    public void TheSetting_IsTheLayoutKey(string word)
    {
        var parsed = PaperOverrides.Parse(["restStyle=" + word], out var error);
        Assert.Null(error);
        Assert.Equal(RestStyleKey.Find(word), parsed!.ApplyLayout(LayoutPlan.Default).EngravingStyle.RestStyle);
        // The setting draws the picture the file's own key draws (but for the source positions).
        Assert.Equal(Picture(Svg(Repro(Layout(word)))), Picture(Svg(Repro(""), "restStyle=" + word)));
        // ...and wins over the file's.
        Assert.Equal(Picture(Svg(Repro(Layout(word)))),
            Picture(Svg(Repro(Layout(word == "z" ? "classical" : "z")), "restStyle=" + word)));
    }

    [Theory]
    [InlineData("restStyle=mensural")]
    [InlineData("restStyle=neomensural")]
    [InlineData("restStyle=Z")]
    [InlineData("restStyle=")]
    [InlineData("reststyle=z")]
    public void AWordThatIsNotAStyle_IsRefused(string setting)
    {
        Assert.Null(PaperOverrides.Parse([setting], out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void AnotherWord_IsAProblemInTheBlock()
    {
        var tree = SyntaxTree.Parse("layout { restStyle mensural }");
        LayoutPlanReader.Read(tree.GetRoot().DescendantNodes().OfType<LayoutDeclarationSyntax>().Single(), out var found);
        var problem = Assert.Single(found);
        Assert.True(problem.IsError);
        Assert.Contains("default, classical or z", problem.Message);
    }

    // ---- the twin, MusicXML, MIDI ----

    [Theory]
    [InlineData("classical")]
    [InlineData("z")]
    public void TheTwin_WritesLilyPondsRestStyle(string word)
    {
        string twin = new LilyPondExporter().Export(SyntaxTree.Parse(Repro(Layout(word))));
        Assert.Contains("\\override Rest.style = #'" + word + "\n", twin);
        // In the \Score context block of \layout, with the other piece-wide overrides.
        int at = twin.IndexOf("\\override Rest.style", System.StringComparison.Ordinal);
        Assert.True(twin.LastIndexOf("\\Score", at, System.StringComparison.Ordinal) > twin.LastIndexOf("\\layout", at, System.StringComparison.Ordinal));
    }

    [Fact]
    public void TheTwin_WritesNothing_ForTheDefault()
    {
        Assert.DoesNotContain("Rest.style", new LilyPondExporter().Export(SyntaxTree.Parse(Repro(""))));
        Assert.DoesNotContain("Rest.style", new LilyPondExporter().Export(SyntaxTree.Parse(Repro(Layout("default")))));
    }

    [Theory]
    [InlineData("classical")]
    [InlineData("z")]
    public void MusicXmlAndMidi_AreTheDefaults(string word)
    {
        static string Xml(string book) => new MusicXmlExporter().Export(SyntaxTree.Parse(book)).ToXml().ToString();
        static byte[] Midi(string book)
        {
            using var stream = new MemoryStream();
            new MidiExporter().Export(SyntaxTree.Parse(book)).WriteTo(stream);
            return stream.ToArray();
        }
        foreach (var book in new System.Func<string, string>[] { Repro, Durations })
        {
            Assert.Equal(Xml(book("")), Xml(book(Layout(word))));
            Assert.Equal(Midi(book("")), Midi(book(Layout(word))));
        }
    }

    // ---- a SMuFL font ----

    /// <summary>SMuFL names both shapes (<c>restQuarterOld</c> U+E4F2, <c>restQuarterZ</c> U+E4F6)
    /// and the three bundled fonts carry them, so a score in one of them draws the font's own.</summary>
    [Theory]
    [InlineData("Bravura")]
    [InlineData("Leland")]
    [InlineData("Petaluma")]
    public void ASmuflFont_DrawsItsOwnGlyphs(string name)
    {
        var font = MusicFonts.Find(name, out _);
        Assert.NotNull(font);
        Assert.True(font!.Has(MusicGlyph.RestQuarterOld));
        Assert.True(font.Has(MusicGlyph.RestQuarterZ));
        Assert.Equal('', font.Codepoint(MusicGlyph.RestQuarterOld));
        Assert.Equal('', font.Codepoint(MusicGlyph.RestQuarterZ));
        // The page: four of the font's own z rests, none of its modern ones, and the box the
        // readers measure is the font's (its metadata), not Emmentaler's.
        string fonts = "fonts { music \"" + name + "\" }\n";
        var g = RenderedGeometry.RenderProduct(Repro(fonts + Layout("z")));
        Assert.Equal(4, g.Glyphs.Count(x => x.Glyph == ''));
        Assert.DoesNotContain(g.Glyphs, x => x.Glyph == '');
        var old = RenderedGeometry.RenderProduct(Repro(fonts + Layout("classical")));
        Assert.Equal(4, old.Glyphs.Count(x => x.Glyph == ''));
        Assert.NotEqual(EmmentalerMusicFont.Instance.FullSize.Box(MusicGlyph.RestQuarterZ),
            font.FullSize.Box(MusicGlyph.RestQuarterZ));
    }
}
