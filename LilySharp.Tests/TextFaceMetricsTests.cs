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
using LilySharp.Core.Rendering;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="TextFontMetrics"/> asked for a face BY NAME.
/// </summary>
/// <remarks>
/// ⚠️ EVERY ASSERTION HERE NAMES A BUNDLED FAMILY, and that is the point rather than a
/// convenience. A test that named Georgia would assert something about the machine it ran
/// on: green here, red on a build agent, and worst of all green-for-the-wrong-reason on a
/// machine where Skia substitutes silently. The bundled files are present by construction,
/// so naming one is a named-face question with a deterministic answer everywhere — and it
/// exercises exactly the branch a score's <c>fonts { }</c> directive will take.
/// <para>
/// The pair that carries the weight is <see cref="NamingAFace_DecidesTheFile_NotTheFallbackFamily"/>:
/// the fallback family says SERIF and the name says the SANS file, so a reading that
/// ignored the name would return the serif number. Nothing else in this file separates
/// "the name was used" from "the flag happened to agree".
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class TextFaceMetricsTests
{
    // Long enough that a face difference is not a rounding difference, and made of letters
    // whose pair kerning the two bundled faces do not treat alike.
    private const string Sample = "Allegro moderato";
    private const double Size = 2.2;

    /// <summary>A name that no font manager can answer for.</summary>
    private const string Absent = "NoSuchFontFace-LilySharp-Test";

    [Fact]
    public void NamingAFace_DecidesTheFile_NotTheFallbackFamily()
    {
        // The fallback family is SERIF; the NAME is the bundled sans.
        var named = TextFace.Named(TextFontMetrics.SansFamily, sans: false, FontStyle.Regular);

        double byName = TextFontMetrics.Advance(Sample, Size, named);
        double bundledSans = TextFontMetrics.Advance(Sample, Size, TextFace.Bundled(sans: true));
        double bundledSerif = TextFontMetrics.Advance(Sample, Size, TextFace.Bundled(sans: false));

        Assert.Equal(bundledSans, byName, 12);
        Assert.NotEqual(bundledSerif, byName, 6);
    }

    /// <summary>
    /// The same, through the INK path, because the ink and the advance are two different
    /// caches and a face key wired into one of them proves nothing about the other.
    /// </summary>
    [Fact]
    public void NamingAFace_DecidesTheFileForInkToo()
    {
        var named = TextFace.Named(TextFontMetrics.SansFamily, sans: false, FontStyle.Regular);

        var byName = TextFontMetrics.Ink(Sample, Size, named);
        var bundledSans = TextFontMetrics.Ink(Sample, Size, TextFace.Bundled(sans: true));
        var bundledSerif = TextFontMetrics.Ink(Sample, Size, TextFace.Bundled(sans: false));

        Assert.Equal(bundledSans.Top, byName.Top, 12);
        Assert.Equal(bundledSans.Bottom, byName.Bottom, 12);
        Assert.NotEqual(bundledSerif.Top, byName.Top, 6);
    }

    /// <summary>
    /// …and through the shaped run, which is what a backend draws from. If the reservation
    /// followed the name and the drawing did not, this engine would have re-created the
    /// reserve-versus-draw split it spent 2026-08-03 deleting.
    /// </summary>
    [Fact]
    public void NamingAFace_DecidesTheFileForTheShapedRunToo()
    {
        var named = TextFace.Named(TextFontMetrics.SansFamily, sans: false, FontStyle.Regular);

        var byName = TextFontMetrics.ShapeRun(Sample, Size, named);
        var bundledSans = TextFontMetrics.ShapeRun(Sample, Size, TextFace.Bundled(sans: true));

        Assert.Equal(bundledSans.Count, byName.Count);
        Assert.Equal(bundledSans.Select(g => g.GlyphId), byName.Select(g => g.GlyphId));
        for (int i = 0; i < byName.Count; i++)
            Assert.Equal(bundledSans[i].X, byName[i].X, 12);
    }

    /// <summary>
    /// Writing the bundled family's own name gets the bundled FILE, not some other copy of
    /// it the machine may have installed.
    /// </summary>
    /// <remarks>
    /// The engine consults its bundle before the machine, so this holds even on a box where
    /// TeX Gyre Schola is also a system font — which is the case that would otherwise turn
    /// "the default face" into "whichever Schola won", quietly, on one machine only.
    /// </remarks>
    [Fact]
    public void TheBundledFamiliesOwnNames_ResolveToTheBundledFiles()
    {
        foreach (var (name, sans) in new[]
                 {
                     (TextFontMetrics.SerifFamily, false),
                     (TextFontMetrics.SansFamily, true),
                 })
        {
            foreach (var style in Enum.GetValues<FontStyle>())
            {
                double byName = TextFontMetrics.Advance(
                    Sample, Size, TextFace.Named(name, sans: !sans, style));
                double bundled = TextFontMetrics.Advance(
                    Sample, Size, TextFace.Bundled(sans, style));
                Assert.Equal(bundled, byName, 12);
            }
        }
    }

    [Fact]
    public void CanMeasure_IsTrueForTheBundledFacesAndForTheirNames()
    {
        Assert.True(TextFontMetrics.CanMeasure(TextFace.Bundled(sans: false)));
        Assert.True(TextFontMetrics.CanMeasure(TextFace.Bundled(sans: true)));
        Assert.True(TextFontMetrics.CanMeasure(
            TextFace.Named(TextFontMetrics.SerifFamily, sans: false, FontStyle.Bold)));
        Assert.True(TextFontMetrics.CanMeasure(
            TextFace.Named(TextFontMetrics.SansFamily, sans: true, FontStyle.BoldItalic)));
    }

    [Fact]
    public void CanMeasure_IsFalseForAFaceThisMachineDoesNotHave()
        => Assert.False(TextFontMetrics.CanMeasure(
            TextFace.Named(Absent, sans: false, FontStyle.Regular)));

    /// <summary>
    /// An unavailable face THROWS. It does not quietly become the bundled one.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE WHOLE STANCE OF <see cref="TextFontMetrics"/>, restated for the named
    /// path. Skia never returns null for an unknown family — it hands back a default face —
    /// so "measure it and see" is a call that always succeeds and sometimes measures Segoe
    /// UI. <c>b69c73e6</c> is what that costs: four LP-fidelity ledger values had been
    /// taken against whatever fontconfig picked. The caller has to ask
    /// <see cref="TextFontMetrics.CanMeasure"/> and say what it is doing about the answer.
    /// </remarks>
    [Fact]
    public void MeasuringAFaceThisMachineDoesNotHave_Throws_RatherThanSubstituting()
    {
        var absent = TextFace.Named(Absent, sans: false, FontStyle.Regular);

        var advance = Assert.Throws<InvalidOperationException>(
            () => TextFontMetrics.Advance(Sample, Size, absent));
        Assert.Contains(Absent, advance.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(
            () => TextFontMetrics.Ink(Sample, Size, absent));
    }

    /// <summary>
    /// The <c>(sans, style)</c> spellings still answer exactly what the face spellings do.
    /// </summary>
    /// <remarks>
    /// They are an ADAPTER during the migration, not a second model: each forwards in one
    /// line to <see cref="TextFace.Bundled"/>. This test is what says so mechanically, and
    /// it is what makes the commit that introduced the face key provably output-identical
    /// — the 54 measurement sites in <c>Svg/Layout</c> still call the old spellings, so if
    /// the two disagreed anywhere the engine would already be drawing differently.
    /// ⚠️ The old spellings go when the last caller moves; they are not API to build on.
    /// </remarks>
    [Fact]
    public void TheFlagSpellings_AnswerTheSameAsTheFaceSpellings()
    {
        foreach (bool sans in new[] { false, true })
            foreach (var style in Enum.GetValues<FontStyle>())
            {
                var face = TextFace.Bundled(sans, style);
                Assert.Equal(TextFontMetrics.Advance(Sample, Size, sans, style),
                             TextFontMetrics.Advance(Sample, Size, face), 12);
                Assert.Equal(TextFontMetrics.InkHeight(Sample, Size, sans, style),
                             TextFontMetrics.InkHeight(Sample, Size, face), 12);
            }
    }
}
