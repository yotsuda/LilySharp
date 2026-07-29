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

using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests.Svg;

/// <summary>
/// The outline-skyline walk over a text string (TextOutlineSkylines): the profiles must
/// agree with the ink box at their extremes, and DISAGREE with it pointwise — the second
/// property is the whole reason the walk exists (ledger textscript.stacked.outline-step).
/// </summary>
[Trait("Category", "Unit")]
public class TextOutlineSkylineTests
{
    private readonly ITestOutputHelper _output;

    // The size and style DrawCustomTexts draws TextScript at.
    private const double Em = 2.2; // EngravingDefaults.TextScriptFontSize
    private const FontStyle Style = FontStyle.Italic;

    public TextOutlineSkylineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The resolved profiles' extremes ARE the ink box: max of the UP profile = ink top,
    /// extreme of the DOWN profile = ink bottom. This is also the orientation net — a
    /// flipped winding classification puts the bottom edges in the UP list and misses
    /// the ascender by half an em, so a sign error here cannot pass.
    /// </summary>
    [Theory]
    [InlineData("poco")]
    [InlineData("dolce")]
    [InlineData("mum")]
    public void ProfileExtremes_AreTheInkBox(string text)
    {
        var (up, down) = TextOutlineSkylines.Place(text, Em, sans: false, Style, x: 0, yBaseline: 0);
        var (inkBottom, inkTop) = TextFontMetrics.Ink(text, Em, sans: false, Style);

        _output.WriteLine($"{text}: up.Max={up.MaxHeight():F6} inkTop={inkTop:F6} " +
                          $"down.Min={down.MaxHeight():F6} inkBottom={inkBottom:F6}");

        // The flattening is max(2, len/0.2) segments, so a curve's true extreme can sit
        // between sample points — LilyPond's own quantisation, a few thousandths at most.
        Assert.Equal(inkTop, up.MaxHeight(), 2);
        Assert.Equal(inkBottom, down.MaxHeight(), 2);
        // The profile never exceeds the ink box (the box bounds the outline by definition).
        Assert.True(up.MaxHeight() <= inkTop + 1e-9);
        Assert.True(down.MaxHeight() >= inkBottom - 1e-9);
    }

    /// <summary>
    /// The pointwise property the interval stacker could not represent: "poco" stacked
    /// over "dolce" binds LOWER than the box arithmetic, because the p's descender falls
    /// over d-o-l's x-height bowls, not over the d ascender that sets the box top.
    /// LILYPOND-REF: lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions —
    /// the up-shift is skyline[DOWN].distance(other[UP]), a pointwise maximum.
    /// MEASURED (audit/lp-geometry/probes/textscript-ink.ly, book TXS): LilyPond's step is
    /// 2.104975 with outside-staff-padding 0.46, i.e. a pure skyline distance of 1.644975,
    /// against box arithmetic 1.621440 + 0.444430 = 2.065870.
    /// </summary>
    // LILYPOND-REF: scm/define-grobs.scm:3806 TextScript outside-staff-horizontal-padding
    // = 0.2 — avoid_outside_staff_collisions pads the moving grob's profile with it
    // (flat + 45°) before the pointwise distance. The two scripts share a PEN ORIGIN:
    // X-offset is aligned_on_x_parent with self/parent-alignment-X both #f, i.e. 0
    // (probe dump: all three strings' x-extents start at exactly 21.650926, which
    // per-glyph side bearings could not do).
    private const double HorizontalPadding = 0.2;

    [Fact]
    public void PocoOverDolce_BindsBelowTheBoxArithmetic()
    {
        var (_, pocoDown) = TextOutlineSkylines.Place("poco", Em, sans: false, Style, x: 0, yBaseline: 0);
        var (dolceUp, _) = TextOutlineSkylines.Place("dolce", Em, sans: false, Style, x: 0, yBaseline: 0);

        // Same baselines ⇒ the padded pointwise distance is the required baseline
        // separation for the outlines to just touch.
        double outlineStep = pocoDown.Distance(dolceUp, HorizontalPadding);

        var (pocoBottom, _) = TextFontMetrics.Ink("poco", Em, sans: false, Style);
        var (_, dolceTop) = TextFontMetrics.Ink("dolce", Em, sans: false, Style);
        double boxStep = dolceTop - pocoBottom; // descent is negative Bottom

        _output.WriteLine($"outline step = {outlineStep:F6}, box step = {boxStep:F6}, " +
                          $"LP outline step = 1.644975");

        // The pointwise term LilyPond measures: 2.065870 - 1.644975 = 0.420895 below the
        // box. Assert the mechanism (well below the box) and the landing. MEASURED
        // 1.646109 — and C059 (LilyPond's own face) gives the identical six digits, so
        // the +0.0011 against LilyPond's dump is not the metric twin: it is the
        // flattening's sample phase and Skia's float32 path riding the d-bowl's slope.
        Assert.True(outlineStep < boxStep - 0.3,
            $"outline step {outlineStep:F6} should bind well below box step {boxStep:F6}");
        Assert.InRange(outlineStep, 1.644975 - 0.005, 1.644975 + 0.005);
    }

    /// <summary>
    /// The regime where the box IS the answer (ledger textscript.stacked.box-step):
    /// "mum"'s x-height top runs flat across its whole width, so wherever "poco"'s
    /// descender falls, the pointwise distance equals the box arithmetic. If a change
    /// closes the outline-step pair while breaking this one, it fitted the outline term.
    /// </summary>
    [Fact]
    public void PocoOverMum_TheBoxRegime_OutlineAgreesWithBoxArithmetic()
    {
        var (_, pocoDown) = TextOutlineSkylines.Place("poco", Em, sans: false, Style, x: 0, yBaseline: 0);
        var (mumUp, _) = TextOutlineSkylines.Place("mum", Em, sans: false, Style, x: 0, yBaseline: 0);

        // The 0.2 padding's plateau is WHY the box regime is a box regime: unpadded,
        // the descender falls on the first m-arch's slope 0.0165 below the plateau
        // (measured; C059 identical), and LilyPond's 1.6e-5 agreement is unreachable.
        double outlineStep = pocoDown.Distance(mumUp, HorizontalPadding);

        var (pocoBottom, _) = TextFontMetrics.Ink("poco", Em, sans: false, Style);
        var (_, mumTop) = TextFontMetrics.Ink("mum", Em, sans: false, Style);
        double boxStep = mumTop - pocoBottom;

        _output.WriteLine($"outline step = {outlineStep:F6}, box step = {boxStep:F6}");

        // LilyPond's own dump holds box-vs-outline to 1.6e-5 here; allow the italic
        // overshoot of the m's shoulders a little more.
        Assert.Equal(boxStep, outlineStep, 2);
    }

    /// <summary>
    /// The walk itself against LilyPond's OWN face: run the identical flattening over
    /// C059 Italic (the face LilyPond's "LilyPond Serif" alias prefers, present in the
    /// local LilyPond 2.26.0 install) and compare with the probe's measured numbers.
    /// Splits "the walk is wrong" from "the metric twins differ in their interiors":
    /// if C059 lands on LilyPond's numbers, the walk is verified and any residual the
    /// bundled Schola leaves in the ledger is a named face difference.
    /// MEASURED (textscript-ink.ly): poco/dolce outline step 2.104975 - 0.46 = 1.644975;
    /// poco/mum box-vs-outline agreement to 1.6e-5.
    /// </summary>
    [Fact]
    public void TheWalkOverC059_ReproducesLilyPondsNumbers()
    {
        const string dir = @"C:\bin\lilypond-2.26.0\share\lilypond\2.26.0\fonts\otf";
        if (!File.Exists(Path.Combine(dir, "C059-Italic.otf")))
        {
            _output.WriteLine("SKIPPED: LilyPond 2.26.0 install with C059 not present on this machine");
            return;
        }

        using var face = SkiaSharp.SKTypeface.FromFile(Path.Combine(dir, "C059-Italic.otf"));
        using var paint = new SkiaSharp.SKPaint { Typeface = face, TextSize = 1000f };
        double k = Em / 1000.0;

        (VerticalSkyline Up, VerticalSkyline Down) Build(string text, double x = 0)
        {
            using var path = paint.GetTextPath(text, 0, 0);
            var (up, down) = TextOutlineSkylines.FlattenPath(path, k);
            return (VerticalSkyline.FromGlyphOutline(VerticalDirection.Up, up, StaffSize.FullSize, x, 0),
                    VerticalSkyline.FromGlyphOutline(VerticalDirection.Down, down, StaffSize.FullSize, x, 0));
        }

        var poco = Build("poco");
        var dolce = Build("dolce");
        var mum = Build("mum");

        // Pen origins aligned (X-offset 0), the moving profile padded by the declared
        // 0.2 — the geometry avoid_outside_staff_collisions actually computes.
        double outlineStep = poco.Down.Distance(dolce.Up, HorizontalPadding);
        double mumStep = poco.Down.Distance(mum.Up, HorizontalPadding);
        double mumBox = mum.Up.MaxHeight() - poco.Down.MaxHeight();

        _output.WriteLine($"C059 poco/dolce outline step = {outlineStep:F6} (LP 1.644975)");
        _output.WriteLine($"C059 poco/mum outline step = {mumStep:F6}, box = {mumBox:F6} (LP diff 1.6e-5)");

        // Same +0.0011 flattening-phase noise as the Schola twin (the two faces give
        // identical six digits — the twins' outlines match, not just their metrics).
        Assert.InRange(outlineStep, 1.644975 - 0.005, 1.644975 + 0.005);
        Assert.Equal(mumBox, mumStep, 3);
    }

    /// <summary>
    /// Placement arithmetic: x shifts the horizon, yBaseline raises the profile —
    /// the same Transform FromGlyphOutline already applies to baked glyph quads.
    /// </summary>
    [Fact]
    public void Place_TranslatesHorizonAndBaseline()
    {
        var (upAtOrigin, _) = TextOutlineSkylines.Place("poco", Em, sans: false, Style, x: 0, yBaseline: 0);
        var (upPlaced, _) = TextOutlineSkylines.Place("poco", Em, sans: false, Style, x: 10, yBaseline: 3);

        Assert.Equal(upAtOrigin.MaxHeight() + 3, upPlaced.MaxHeight(), 9);

        // Sample the profile at a mid-string x: the placed profile at x+10 must be the
        // origin profile at x, raised by 3.
        double advance = TextFontMetrics.Advance("poco", Em, sans: false, Style);
        double mid = advance / 2;
        Assert.Equal(upAtOrigin.Height(mid) + 3, upPlaced.Height(mid + 10), 9);
    }
}
