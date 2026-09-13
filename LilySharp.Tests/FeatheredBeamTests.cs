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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class FeatheredBeamTests
{
    // --- NoteItem.FeatherDirection ---

    [Fact]
    public void NoteItem_FeatherDirection_DefaultZero()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0);
        Assert.Equal(0, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_Right()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: 1);
        Assert.Equal(1, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_Left()
    {
        var note = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: -1);
        Assert.Equal(-1, note.FeatherDirection);
    }

    [Fact]
    public void NoteItem_FeatherDirection_ClampedToRange()
    {
        var noteHigh = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: 5);
        Assert.Equal(1, noteHigh.FeatherDirection);

        var noteLow = new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, featherDirection: -5);
        Assert.Equal(-1, noteLow.FeatherDirection);
    }

    // --- BeamGroup.GrowDirection ---

    [Fact]
    public void BeamGroup_GrowDirection_DefaultZero()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true);
        Assert.Equal(0, group.GrowDirection);
    }

    [Fact]
    public void BeamGroup_GrowDirection_Right()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true, growDirection: 1);
        Assert.Equal(1, group.GrowDirection);
    }

    [Fact]
    public void BeamGroup_GrowDirection_Left()
    {
        var members = ImmutableArray.Create(
            new BeamMember(new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0), 2, 0, 2, 0, 0),
            new BeamMember(new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1), 2, 2, 0, 2, 1));
        var group = new BeamGroup(members, 0, 0, true, growDirection: -1);
        Assert.Equal(-1, group.GrowDirection);
    }

    // --- BeamDetector propagation ---

    private static Measure MakeMeasure(params MusicItem[] items) =>
        new(ImmutableArray.Create(items), BarlineType.None, BarlineType.None, null, 0, 0);

    [Fact]
    public void BeamDetector_PropagatesFeatherDirection()
    {
        // Create 4 sixteenth notes, first with feather=1
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, hasBeamStart: true, featherDirection: 1),
            new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1),
            new NoteItem(4, Fraction.Sixteenth, 0, null, false, 2),
            new NoteItem(6, Fraction.Sixteenth, 0, null, false, 3, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.Equal(1, groups[0].GrowDirection);
    }

    [Fact]
    public void BeamDetector_NoFeather_DefaultZero()
    {
        var notes = new MusicItem[]
        {
            new NoteItem(0, Fraction.Sixteenth, 0, null, false, 0, hasBeamStart: true),
            new NoteItem(2, Fraction.Sixteenth, 0, null, false, 1),
            new NoteItem(4, Fraction.Sixteenth, 0, null, false, 2),
            new NoteItem(6, Fraction.Sixteenth, 0, null, false, 3, hasBeamEnd: true),
        };
        var measure = MakeMeasure(notes);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var detector = new BeamDetector();
        var groups = detector.DetectBeamGroups(voice, new TimeSignature(4, 4));

        Assert.NotEmpty(groups);
        Assert.Equal(0, groups[0].GrowDirection);
    }

    // --- Feathered beam rendering behavior ---
    //
    // ⚠️⚠️⚠️ THREE TESTS STOOD HERE AND ASSERTED NOTHING (removed 2026-09-13). They were
    // named FeatheredBeam_RightGrow_ConvergesAtLeft, FeatheredBeam_LeftGrow_ConvergesAtRight
    // and NormalBeam_NoFeathering, and each one computed `growDir > 0 ? 0.0 : 1.0` INSIDE
    // the test and asserted the answer. No production code was called; the arithmetic was
    // the test's own. They are why nobody noticed for as long as nobody did: the feather was
    // carried through four layers and drawn by none of them, and the page said so the moment
    // it was asked (VocabularyPerturbationTests' operand sweep). The geometry landed the
    // same day; what follows reads the DRAWN lines.

    /// <summary>
    /// The fan itself, measured off the page: with grow-direction RIGHT the secondary beam
    /// sits ON the primary at the left end and a full beam-translation away at the right;
    /// LEFT is the mirror; no feather holds them parallel.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1134-1145 calc_stem_y — LilyPond states the rule in a comment
    ///   beside the factor it multiplies every rank by ("feather dir = 1 , relx 0->1 :
    ///   factor 0 -> 1", and the two other directions), then applies it as
    ///   <c>stem_y += feather_factor * beam_translation * beam_multiplicity[stem_dir]</c>;
    /// LILYPOND-REF: beam.cc:785-792 print's local_slope — the drawn lines get the same fan
    ///   as an extra slope per rank, feather_dir * vertical_count_ * beam_dy / span, which
    ///   over a whole-span segment is that same factor evaluated at its two ends.
    /// ⚠️ The numbers are READ FROM THE SVG, not recomputed here — that is the whole point
    /// of replacing the three tautologies. <c>SharedRenderer.Beams</c> draws each beam as a
    /// four-point polygon whose first two points are the line's left and right TOP corners.
    /// </remarks>
    [Theory]
    [InlineData("", 0.0, 0.0)]                    // no feather: the gap is the same at both ends
    [InlineData("@feather(right)", -1.0, 0.0)]    // converged left, full gap right
    [InlineData("@feather(left)", 0.0, -1.0)]     // full gap left, converged right
    public void TheSecondaryBeamFansAsLilyPondSays(
        string annotation, double leftGapIsFull, double rightGapIsFull)
    {
        var (primary, secondary) = FirstTwoBeamLines(annotation);

        // The gap between the two lines at each end, in units of the beam translation.
        double gapLeft = System.Math.Abs(secondary.LeftY - primary.LeftY);
        double gapRight = System.Math.Abs(secondary.RightY - primary.RightY);
        double translation = System.Math.Max(gapLeft, gapRight);
        Assert.True(translation > 0.5, "no beam stack in the fixture at all");

        // leftGapIsFull/rightGapIsFull are −1 for "converged" and 0 for "full"; the plain
        // beam is full at both ends, which is what its two zeros say.
        Assert.Equal(leftGapIsFull < 0 ? 0.0 : translation, gapLeft, 2);
        Assert.Equal(rightGapIsFull < 0 ? 0.0 : translation, gapRight, 2);
    }

    /// <summary>
    /// ★ THE FEATHER DOES NOT CHANGE AN ORDINARY BEAM'S STEMS, and that is the correct
    /// answer rather than a gap: a stem ends at the outermost rank ON ITS OWN SIDE, which
    /// for an ordinary beam is the PRIMARY line (rank 0) — and rank 0 times any factor is
    /// still rank 0. The renderer applies LilyPond's factor there all the same, because the
    /// rank is not 0 in a KNEE, where the two sides reach different lines.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: beam.cc:1145 calc_stem_y — <c>stem_y += feather_factor *
    ///   beam_translation * beam_multiplicity[stem_dir]</c>: the same factor, at the stem's
    ///   own x, multiplying the rank the stem's DIRECTION selects.
    /// ⚠️ Measured, not assumed (2026-09-13): this test was first written the other way
    /// round — "a right-growing beam's outer stems cannot be the same length" — and failed,
    /// because it had guessed that the stems hang from the fanned rank. The beam's own
    /// comment (SharedRenderer.Beams, "for an ordinary beam that extreme is the primary…")
    /// says otherwise, and the page agreed with the comment.
    /// </remarks>
    [Theory]
    [InlineData("c'16ANNOTATION d' e' f'")]    // stems up
    [InlineData("c''16ANNOTATION d'' e'' f''")] // stems down
    public void AnOrdinaryBeamsStemsAreUnmovedByTheFeather(string music)
    {
        double[] plain = StemLengths("", music);
        foreach (string direction in new[] { "@feather(right)", "@feather(left)" })
        {
            double[] fanned = StemLengths(direction, music);
            Assert.Equal(plain.Length, fanned.Length);
            for (int i = 0; i < plain.Length; i++)
                Assert.Equal(plain[i], fanned[i], 3);
        }
    }

    /// <summary>
    /// The LilyPond twin carries the feather, so the two pictures cannot disagree about it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE SPELLING WAS VERIFIED BY RUNNING LILYPOND, not chosen from memory (2026-09-13,
    /// LilyPond 2.24.4 on the exported twin): with the override the beam's FAR end moves
    /// −1.01 → −1.82, one beam translation further from the primary, while its near end stays
    /// at 0.2 — the same fan, the same size, as the page draws.
    /// <para>
    /// ⚠️ <c>\once</c> is the right scope: the Beam grob is created at the beam's first
    /// moment, so the override reaches THIS beam and cannot leak into the next. And
    /// <c>\featherDurations</c> is deliberately absent — it scales the printed durations,
    /// which <c>@feather</c> does not do.
    /// </para>
    /// <para>
    /// ⚠️ MusicXML carries nothing here and cannot yet: that exporter writes no
    /// <c>&lt;beam&gt;</c> elements at all, so MusicXML's own <c>fan</c> attribute has no
    /// element to sit on. Named in SharedRenderer.Beams beside the geometry.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("@feather(right)", "#RIGHT")]
    [InlineData("@feather(accel)", "#RIGHT")]
    [InlineData("@feather(left)", "#LEFT")]
    [InlineData("@feather(rit)", "#LEFT")]
    public void TheTwinWritesTheGrowDirection(string annotation, string expected)
    {
        string ly = new LilyPondExporter().Export(
            SyntaxTree.Parse(Book.Replace("MUSIC", UpStemMusic).Replace("ANNOTATION", annotation)));
        Assert.Contains("\\once \\override Beam.grow-direction = " + expected, ly, StringComparison.Ordinal);
        // ...and the rhythm is untouched: the feather fans the drawing, nothing else.
        Assert.DoesNotContain("featherDurations", ly, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainBeamsTwinSaysNothingAboutGrowing()
        => Assert.DoesNotContain("grow-direction",
            new LilyPondExporter().Export(
                SyntaxTree.Parse(Book.Replace("MUSIC", UpStemMusic).Replace("ANNOTATION", ""))),
            StringComparison.Ordinal);

    // ---------- reading the drawn page ----------

    private const string Book =
        "octave absolute\npart m { clef treble\n"
        + "  section A { MUSIC | c'4 d' e' f' | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    private const string UpStemMusic = "c'16ANNOTATION d' e' f'";

    private static string Svg(string annotation, string music = UpStemMusic) =>
        SvgGenerator.Generate(
            SyntaxTree.Parse(Book.Replace("MUSIC", music).Replace("ANNOTATION", annotation)),
            new SvgRenderOptions { EmbedFont = false });

    private readonly record struct Line(double LeftY, double RightY);

    /// <summary>The first beam group's two lines, primary first, off the rendered page.</summary>
    private static (Line Primary, Line Secondary) FirstTwoBeamLines(string annotation)
    {
        var polygons = Regex.Matches(Svg(annotation), "<polygon[^>]*points=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Select(p => p.Split(' ').Select(pt => pt.Split(',')).ToArray())
            .Select(pts => new Line(
                double.Parse(pts[0][1], CultureInfo.InvariantCulture),
                double.Parse(pts[1][1], CultureInfo.InvariantCulture)))
            .ToArray();
        Assert.True(polygons.Length >= 2, "the fixture drew fewer than two beam lines");
        return (polygons[0], polygons[1]);
    }

    /// <summary>Every drawn stem's length in the first beam group, left to right.</summary>
    private static double[] StemLengths(string annotation, string music = UpStemMusic) =>
        [.. Regex.Matches(Svg(annotation, music),
                "<line[^>]*x1=\"([-\\d.]+)\"[^>]*y1=\"([-\\d.]+)\"[^>]*x2=\"([-\\d.]+)\"[^>]*y2=\"([-\\d.]+)\"")
            .Select(m => (
                X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Y1: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                X2: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                Y2: double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)))
            .Where(l => System.Math.Abs(l.X - l.X2) < 0.001      // vertical: a stem, not a staff line
                        && System.Math.Abs(l.Y1 - l.Y2) > 0.5)
            .OrderBy(l => l.X)
            .Take(4)
            .Select(l => System.Math.Abs(l.Y1 - l.Y2))];
}
