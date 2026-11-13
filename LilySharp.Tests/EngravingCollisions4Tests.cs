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
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The collisions four more large scores found on 2026-11-13 (grammar evaluation 4; Lab
/// sessions/p1226/md/eval4/fixB2). As in <see cref="EngravingCollisions3Tests"/>, each test
/// holds two pieces of drawn ink apart — never a coordinate.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EngravingCollisions4Tests
{
    // Device Y grows DOWN on the recorded page, so "below" is the larger Y.

    private static double[] StaffLineYs(RenderedGeometry g)
    {
        // The staff lines are the longest horizontal rules of the page.
        var horizontal = g.Lines.Where(l => Math.Abs(l.Y1 - l.Y2) < 1e-9 && !l.IsDashed).ToArray();
        double longest = horizontal.Max(l => l.X2 - l.X1);
        return [.. horizontal.Where(l => l.X2 - l.X1 > longest - 0.5).Select(l => Math.Round(l.Y1, 6)).Distinct().OrderBy(y => y)];
    }

    /// <summary>
    /// rock/r-08 — <c>score { staff v  chords h  staff g  tab g }</c>: the guitar's let ring,
    /// barre and palm-mute lines were drawn INSIDE the staff above. A <c>chords</c> row between
    /// two staves hangs on the staff under it, and its symbols were seeded into that staff's
    /// outside-staff support as if they were inline <c>@chord</c> names
    /// (<c>OutsideStaffStacker</c>), so every mover of the staff — these lines, a
    /// <c>@rit</c>, a text, a fermata — climbed over the row while the room reserved for them
    /// under the row stood empty. And two lines over the same notes were reserved as one
    /// (<c>TextSpannerEngraver.InkAboveStaff</c>), so even a row at the top of the score was
    /// printed through the upper one. The lines stand between their own staff and the row; the
    /// row and the staff distance make room.
    /// </summary>
    [Theory]
    [InlineData("staff v  chords h  staff g  tab g", true)]
    [InlineData("staff v  chords h  staff g", true)]
    [InlineData("chords h  staff g  tab g", false)]
    public void AStaffsGuitarLines_StandBetweenItAndTheChordsRowOverIt(string score, bool staffAbove)
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part v { clef treble }
            part g { instrument guitar }
            section A {
              v { R*2 | }
              g { c8@barre(3)@letRing g c' e' g' e' c' g@!letRing@!barre | e,8@palmMute e, e, e, e, e, e, e,@!palmMute | }
              chords h { C | E5 | }
            }
            form { ~A }
            """ + "\nscore { " + score + " }\n");
        var lines = StaffLineYs(g);
        // The guitar's notation staff: the second five-line staff under a staff above, else the first.
        double guitarTop = staffAbove ? lines[5] : lines[0];
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName && t.Text is "C" or "E").Max(t => t.Y);
        foreach (string words in new[] { "let ring", "CIII", "P.M." })
        {
            var text = g.Texts.Single(t => t.Text == words);
            // Over-read: 0.8 em is above the ascenders of any text face.
            double top = text.Y - 0.8 * text.FontSize;
            Assert.True(top > rowBaseline,
                $"\"{words}\" reaches up to {top:F3} and the chords row stands on {rowBaseline:F3}: the row is over the lines, not in them.");
            Assert.True(text.Y < guitarTop, $"\"{words}\" on {text.Y:F3} is not over its own staff ({guitarTop:F3}).");
        }
        if (staffAbove)
        {
            // …and the row, with everything under it, is clear of the staff above.
            double upperBottom = lines[4];
            double rowTop = g.Texts.Where(t => t.Role == TextRole.ChordName).Min(t => t.Y - 0.8 * t.FontSize);
            Assert.True(rowTop > upperBottom, $"the row reaches up to {rowTop:F3}, into the staff above ({upperBottom:F3}).");
        }
    }

    /// <summary>The same row, the staff's other movers: a text written <c>.up</c> and a
    /// <c>@rit</c> line of the lower staff stood over the row too, in the staff above.</summary>
    [Fact]
    public void AStaffsWordsAndRit_StandUnderTheChordsRowBetweenTheStaves()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            part v { clef treble }
            part g { instrument guitar }
            section A {
              v { R*3 | }
              g { c8@text("hey").up g c' e' g' e' c' g | e,1@rit | e,1@!rit | }
              chords h { C | E5 | E5 | }
            }
            form { ~A }
            score { staff v  chords h  staff g }
            """);
        var lines = StaffLineYs(g);
        double rowBaseline = g.Texts.Where(t => t.Role == TextRole.ChordName && t.Text is "C" or "E").Max(t => t.Y);
        foreach (string words in new[] { "hey", "rit." })
        {
            var text = g.Texts.Single(t => t.Text == words);
            Assert.True(text.Y - 0.8 * text.FontSize > rowBaseline,
                $"\"{words}\" on {text.Y:F3} is over the chords row ({rowBaseline:F3}).");
            Assert.True(text.Y < lines[5]);
        }
    }

    // ---- harbour-lights line 28: a dynamic line above the staff and the slur under it ----------

    private static string SungLine(string music) => """
        tempo 72
        time 4/4
        key ees major
        part singer "Voice" { clef treble_8  lyrics words }
        section Song {
          singer {
        """ + "\n    " + music + "\n" + """
          }
          lyrics words language english {
            Out on the tide I can hear | dis- tant sing- ing, | each bright |
          }
        }
        form { ~Song }
        score { staff singer  lyrics words }
        """;

    /// <summary>The page's one slur, as the curve of its OUTER edge sampled along its length
    /// (the first of the sandwich's two beziers — the upper one of a slur over the notes).</summary>
    private static (double X, double Y)[] SlurCurve(RenderedGeometry g)
    {
        var bow = g.Beziers.OrderByDescending(b => b.P1.X - b.P0.X).First();
        var points = new (double X, double Y)[65];
        for (int i = 0; i < points.Length; i++)
        {
            double t = i / 64.0, u = 1 - t;
            // The higher of the two halves at this parameter (device Y grows down).
            double x = u * u * u * bow.P0.X + 3 * u * u * t * bow.C1.X + 3 * u * t * t * bow.C2.X + t * t * t * bow.P1.X;
            double front = u * u * u * bow.P0.Y + 3 * u * u * t * bow.C1.Y + 3 * u * t * t * bow.C2.Y + t * t * t * bow.P1.Y;
            double back = u * u * u * bow.P0.Y + 3 * u * u * t * bow.C1Back.Y + 3 * u * t * t * bow.C2Back.Y + t * t * t * bow.P1.Y;
            points[i] = (x, Math.Min(front, back));
        }
        return points;
    }

    /// <summary>A hairpin's two arms: the two long sloped rules over the staff that span the
    /// same X (a staff line is level, a stem upright, and the recorder's one rule from a
    /// bow's end to its other end has no twin).</summary>
    private static DrawnLine[] HairpinArms(RenderedGeometry g)
    {
        double staffTop = StaffLineYs(g)[0];
        DrawnLine[] sloped = [.. g.Lines.Where(l => !l.IsDashed && l.X2 - l.X1 > 20
            && Math.Abs(l.Y1 - l.Y2) > 1e-6 && l.Y1 < staffTop && l.Y2 < staffTop)];
        return [.. sloped.Where(a => sloped.Count(b => Math.Abs(a.X1 - b.X1) < 1e-9 && Math.Abs(a.X2 - b.X2) < 1e-9) == 2)];
    }

    /// <summary>How far the slur's highest ink stays under the hairpin's lower arm, at the
    /// tightest of the sampled points under the hairpin (negative: the slur is through it).</summary>
    private static double HairpinOverSlur(RenderedGeometry g)
    {
        var arms = HairpinArms(g);
        Assert.Equal(2, arms.Length);
        double gap = double.MaxValue;
        foreach (var (x, y) in SlurCurve(g))
        {
            if (x < arms[0].X1 || x > arms[0].X2)
                continue;
            double lower = arms.Max(a => a.Y1 + (a.Y2 - a.Y1) * (x - a.X1) / (a.X2 - a.X1));
            gap = Math.Min(gap, y - lower);
        }
        return gap;
    }

    /// <summary>
    /// harbour-lights line 28 (owner, 2026-11-13) — on a sung part the dynamics stand above
    /// the staff, and the crescendo over an up-stemmed triplet was drawn THROUGH the phrasing
    /// slur: the slur passed between the hairpin's two lines. The drawn slur climbs over the
    /// triplet's number; the slur the staff's skyline held was laid out on a score that
    /// carried no tuplets and stayed 1.5 staff-spaces under it
    /// (<c>MultiStaffLayouter.StaffSlurLayouts</c>), and the dynamic line was placed against
    /// that one. A phrasing slur, a plain slur and a decrescendo alike: the whole wedge is
    /// over the whole curve, by more than the two lines' own weight.
    /// </summary>
    [Theory]
    [InlineData("tuplet 3/2 { aes8@cresc@phrasingSlur bes c@stemUp } ees4 d8 c bes4 | aes4. g8 f4 bes@!phrasingSlur | bes4@f g'2. |")]
    [InlineData("tuplet 3/2 { aes8@cresc( bes c@stemUp } ees4 d8 c bes4 | aes4. g8 f4 bes) | bes4@f g'2. |")]
    [InlineData("tuplet 3/2 { aes8@f@decresc@phrasingSlur bes c@stemUp } ees4 d8 c bes4 | aes4. g8 f4 bes@!phrasingSlur | bes4@p g'2. |")]
    public void AHairpinAboveTheStaff_StandsClearOverAHighSlur(string music)
    {
        var g = RenderedGeometry.Render(SungLine(music));
        double gap = HairpinOverSlur(g);
        Assert.True(gap > 0.2, $"the hairpin's lower line is {gap:F3} over the slur's highest ink: it is drawn through the slur.");
    }

    /// <summary>The same slur and a dynamic TEXT over it: the letter's foot is over the curve
    /// where it stands (it stood inside the bow).</summary>
    [Fact]
    public void ADynamicAboveTheStaff_StandsClearOverAHighSlur()
    {
        var g = RenderedGeometry.Render(SungLine(
            "tuplet 3/2 { aes8@phrasingSlur bes c@stemUp } ees4@f d8 c bes4 | aes4. g8 f4 bes@!phrasingSlur | bes4 g'2. |"));
        var forte = g.Texts.Single(t => t.Role == TextRole.Dynamics);
        var curve = SlurCurve(g);
        // The curve where the letter stands (the "f" is under an em wide).
        double slurTop = curve.Where(p => p.X > forte.X - forte.FontSize && p.X < forte.X + forte.FontSize).Min(p => p.Y);
        Assert.True(forte.Y < slurTop, $"the f's baseline is on {forte.Y:F3} and the slur under it reaches {slurTop:F3}.");
    }

    /// <summary>Without <c>@stemUp</c> the triplet's number is under the staff and the slur
    /// never met it: that picture is the one it was — the wedge the same height over the
    /// staff and over the slur as before the reserved slur was scored with the tuplets.</summary>
    [Fact]
    public void WithTheStemsDown_TheSlurAndTheHairpinStandWhereTheyDid()
    {
        var g = RenderedGeometry.Render(SungLine(
            "tuplet 3/2 { aes8@cresc@phrasingSlur bes c } ees4 d8 c bes4 | aes4. g8 f4 bes@!phrasingSlur | bes4@f g'2. |"));
        double staffTop = StaffLineYs(g)[0];
        double wedgeLow = HairpinArms(g).Max(a => Math.Max(a.Y1, a.Y2));
        double slurTop = SlurCurve(g).Min(p => p.Y);
        Assert.Equal(1.910, staffTop - wedgeLow, 3);
        Assert.Equal(1.543, staffTop - slurTop, 3);
    }

    // ---- bigband/r-10: the jazz gestures and what stands beside them --------------------------

    private readonly record struct Ink(string Kind, int Pos, double Left, double Top, double Right, double Bottom);

    /// <summary>The page's ink that a gesture can run into (heads, rests, the clef, the time
    /// signature, bar lines) and the gestures themselves: a fall or doit is the page's one
    /// kind of <c>curve</c> here, a scoop or plop the stroked lines of one source position
    /// (stems and staff lines have kinds of their own); a box is the stroke's, line included.</summary>
    private static (Ink[] Gestures, Ink[] Others) GestureInk(string music)
    {
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(
            "octave absolute\npart sx { clef treble }\nsection A { sx { " + music + " } }\nform { ~A }\nscore { staff sx }\n");
        var page = Assert.Single(LilySharp.Core.Rendering.Boxes.BoxesGenerator.GeneratePages(
            tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)));
        static Ink Of(LilySharp.Core.Rendering.Boxes.BoxSymbol s) => new(s.Kind, s.Pos, s.Box[0], s.Box[1], s.Box[2], s.Box[3]);
        var approaches = page.Symbols.Where(s => s.Kind == "line").GroupBy(s => s.Pos)
            .Select(g => new Ink("approach", g.Key, g.Min(s => s.Box[0]), g.Min(s => s.Box[1]), g.Max(s => s.Box[2]), g.Max(s => s.Box[3])));
        Ink[] gestures = [.. page.Symbols.Where(s => s.Kind == "curve").Select(Of), .. approaches];
        Ink[] others = [.. page.Symbols.Where(s => s.Kind is "notehead" or "rest" or "clef" or "timeSignature" or "barLine").Select(Of)];
        return (gestures, others);
    }

    /// <summary>
    /// bigband/r-10 — <c>@scoop</c> and <c>@plop</c> (a curve arriving at the head from its
    /// left) were drawn and never spaced for: a quarter rest before a scooped quarter stood
    /// inside the curve, and on a line's first note the curve ran 0.92 into the time
    /// signature. <c>@fall</c> and <c>@doit</c> keep their shortest form on a tight line, where
    /// it was drawn 0.7 into the rest after a sixteenth, 0.3 into the next eighth's head and
    /// through the scoop of the note after. The columns keep the curves' room
    /// (<c>ItemSkylineFactory.AddApproachGesture</c> / <c>AddLeavingGesture</c>): no gesture's
    /// ink box meets a head, a rest, the time signature, a bar line or another gesture.
    /// </summary>
    [Theory]
    [InlineData("r4 c''4@scoop r4 c''4@plop | r4 c''4@scoop r4 c''4@scoop |", 4)]          // a rest before the marked note
    [InlineData("c''4@fall r c''4@doit r | c''1@fall | <c'' e''>1@doit |", 4)]              // a rest, a bar line after it
    [InlineData("c''4@scoop r c''2 | c''4@plop r c''2 |", 2)]                                // a line's first note, after the time signature
    [InlineData("c''4@scoop c''4@scoop c''4@plop c''4@plop | c''4@fall c''4@scoop c''4@doit c''4@plop |", 8)]
    // …and a line too full for its natural spacing: the gestures at their shortest.
    [InlineData("c''4@scoop c''4@scoop c''4@plop c''4@plop | c''4@fall c''4@scoop c''4@doit c''4@plop | "
        + "c''8@fall c''8@scoop c''8@fall c''8@fall c''16@fall r16 c''16@scoop r16 r16 c''16@plop c''16@fall c''16@scoop | f'4@fall f'4@scoop a''4@plop r4 |", 20)]
    public void AJazzGesture_RunsIntoNothingBesideIt(string music, int gestureCount)
    {
        var (gestures, others) = GestureInk(music);
        Assert.Equal(gestureCount, gestures.Length);
        foreach (var gesture in gestures)
            foreach (var other in others.Concat(gestures.Where(o => o != gesture)))
            {
                double x = Math.Min(gesture.Right, other.Right) - Math.Max(gesture.Left, other.Left);
                double y = Math.Min(gesture.Bottom, other.Bottom) - Math.Max(gesture.Top, other.Top);
                // (0.02: a sixteenth rest's recorded box starts 0.11 left of the box it is spaced
                // by, and the fall's shortest form ends 0.1 before that one. The faults were 0.3 to 0.9.)
                Assert.False(x > 0.02 && y > 0.02,
                    $"the {gesture.Kind} at source {gesture.Pos} ([{gesture.Left:F3}, {gesture.Right:F3}]) runs {x:F3} into the {other.Kind} at [{other.Left:F3}, {other.Right:F3}].");
            }
    }

    /// <summary>The fall's room is its SHORTEST form's, and LilyPond keeps none at all
    /// (MEASURED, 2.26.0, Lab sessions/p1226/md/eval4/fixC1/lp-fall-probe.ly: every head and
    /// rest stands where it does without the bends): where the line is not tight, a fall or a
    /// doit moves nothing.</summary>
    [Fact]
    public void AFallOrDoit_MovesNothing_WhereTheLineIsNotTight()
    {
        static double[] Lefts(string music) => [.. GestureInk(music).Others.Select(o => Math.Round(o.Left, 4))];
        Assert.Equal(Lefts("c''4 r c''4 r | c''4 c''4 c''8 c''8 c''4 | c''16 r16 c''16 c''16 r4 r2 |"),
            Lefts("c''4@doit r c''4@fall r | c''4@fall c''4@fall c''8@fall c''8@doit c''4 | c''16@fall r16 c''16@fall c''16 r4 r2 |"));
    }
}
