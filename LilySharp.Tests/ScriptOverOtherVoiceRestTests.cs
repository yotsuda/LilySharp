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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Tests.LpFidelity;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A script of one voice clears a REST of another voice sounding at its note's moment
/// (session 962, OMR REQUESTS #3). LILYSHARP-OWN: LilyPond prints the two through each other
/// — its Script_engraver sees only its own voice's heads and stems, and Rest_collision moves a
/// rest off another voice's notes, never off its scripts — so there is no LilyPond number to
/// pin; the net is an identity net on the drawn geometry: the mark stands clear of the rest's
/// ink by its own padding, and a mark with no rest over it does not move.
/// </summary>
[Trait("Category", "Unit")]
public class ScriptOverOtherVoiceRestTests
{
    // The OMR's bar (gen-s2 piano003, right hand, bar 7; docs/repro/portato-over-rest-2026-10-21.lys
    // in LilySharp-Omr): the lower voice's `a8@portato.up` sits under the upper voice's r4, its
    // `g8@portato.up` under nothing.
    private const string OmrBar = """
        octave absolute
        time 2/4
        part rh { clef treble }
        score { staff rh }
        section Piece {
          rh {
            voice { cis'16 d'16 d'8 r4 } { a16 g16 fis8 a8@portato.up g8@portato.up } |
          }
        }
        """;

    // The same bar with the rest SILENT (a spacer prints nothing): what the marks read with
    // nothing of the other voice over them.
    private const string OmrBarNoRest = """
        octave absolute
        time 2/4
        part rh { clef treble }
        score { staff rh }
        section Piece {
          rh {
            voice { cis'16 d'16 d'8 s4 } { a16 g16 fis8 a8@portato.up g8@portato.up } |
          }
        }
        """;

    private static bool IsPortato(char c)
        => c == EmmentalerGlyphs.ArticPortatoAbove || c == EmmentalerGlyphs.ArticPortatoBelow;

    /// <summary>The drawn portato's glyph, through the font's own table (the Emmentaler
    /// character the recorder saw is the glyph the renderer asked for).</summary>
    private static MusicGlyph PortatoGlyphOf(char drawn) =>
        drawn == EmmentalerGlyphs.ArticPortatoAbove
            ? MusicGlyph.ArticTenutoStaccatoBelow : MusicGlyph.ArticTenutoStaccatoAbove;

    /// <summary>
    /// The side-position reading between the drawn rest and the drawn mark, read off the SAME
    /// outlines the engraver reads (the rest's at its drawn position, the mark's as drawn), in
    /// the engraver's own frame (Y-up about the staff middle): the support's padded distance
    /// to the mark — negative by the mark's own padding once the fold has placed it.
    /// </summary>
    private static double SupportDistance(DrawnGlyph rest, DrawnGlyph mark, double middle, bool markAbove)
    {
        double restPosition = (middle - rest.Y) * 2.0;
        var restGlyph = MusicGlyphs.Rest(4, restPosition, 5);
        double fontSize = LilySharp.Core.Rendering.SharedRenderer.FontSize;
        var (restUp, restDown) = TextOutlineSkylines.PlaceMusicGlyph(
            restGlyph, fontSize, rest.X, middle - rest.Y);
        var markSide = TextOutlineSkylines.PlaceMusicGlyphSide(
            PortatoGlyphOf(mark.Glyph), fontSize, mark.X, middle - mark.Y,
            markAbove ? VerticalDirection.Down : VerticalDirection.Up);
        Assert.NotNull(markSide);
        // The Script's horizon-padding 0.1 is spent on the support (ArticulationEngraver's chain).
        return (markAbove ? restUp : restDown).Distance(markSide!, 0.1);
    }

    [Fact]
    public void ThePortatoUnderTheOtherVoicesRest_StandsClearOfTheRestsInk()
    {
        var g = RenderedGeometry.Render(OmrBar);
        double middle = g.StaffRefpoints()[0];
        var portatos = g.Glyphs.Where(x => IsPortato(x.Glyph)).OrderBy(x => x.X).ToList();
        Assert.Equal(2, portatos.Count);
        var rest = Assert.Single(g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.RestQuarter));
        var overRest = portatos[0];
        // The rest stands where the renderer put it (the upper voice's +4, nothing to push it
        // further), and the mark is OVER it, horizontally: the two share the column.
        Assert.Equal(middle - 2.0, rest.Y, 3);
        Assert.InRange(overRest.X, rest.X - 1.0, rest.X + 1.0);
        // The mark clears the rest's OUTLINE by the portato's own padding 0.45
        // (ArticulationSpacing.VerticalPadding) — the same support reading a tie or a stacked
        // script gets: the padded support's distance to the mark is exactly −0.45. (The box
        // reading is looser: the quarter rest's top is a slant, and the dot stands over its
        // lower part — the ink bottom is 0.26 over the box's top, 0.45 over the outline's.)
        Assert.Equal(-0.45, SupportDistance(rest, overRest, middle, markAbove: true), 3);
        // ...and, plainly, the whole mark is above the whole rest: its ink bottom over the
        // rest's ink top (device Y is down, so "above" is less).
        double restInkTop = rest.Y - GlyphMetrics.GetRestSkylineBBox(4).Top;
        double markInkBottom = overRest.Y - MusicFont.Current.FullSize.Box(PortatoGlyphOf(overRest.Glyph)).Bottom;
        Assert.True(markInkBottom < restInkTop,
            $"the portato's ink bottom {markInkBottom:F3} is not above the rest's ink top {restInkTop:F3}");
    }

    [Fact]
    public void ThePortatoWithNoRestOverIt_DoesNotMove_AndTheRestDoesNotEither()
    {
        var with = RenderedGeometry.Render(OmrBar);
        var without = RenderedGeometry.Render(OmrBarNoRest);
        var withMarks = with.Glyphs.Where(x => IsPortato(x.Glyph)).OrderBy(x => x.X).ToList();
        var withoutMarks = without.Glyphs.Where(x => IsPortato(x.Glyph)).OrderBy(x => x.X).ToList();
        Assert.Equal(2, withMarks.Count);
        Assert.Equal(2, withoutMarks.Count);
        double dyWith = withMarks[1].Y - with.StaffRefpoints()[0];
        double dyWithout = withoutMarks[1].Y - without.StaffRefpoints()[0];
        // g8's mark: the same height whether or not the other voice rests over a8.
        Assert.Equal(dyWithout, dyWith, 3);
        // a8's mark: lifted by the rest, by at least a staff space.
        double liftedWith = withMarks[0].Y - with.StaffRefpoints()[0];
        double liftedWithout = withoutMarks[0].Y - without.StaffRefpoints()[0];
        Assert.True(liftedWith < liftedWithout - 1.0,
            $"the mark under the rest reads {liftedWith:F3}, without the rest {liftedWithout:F3}");
        // The REST is the one that stays: the other way round (moving the rest off the mark) is
        // the design not taken — a rest's place is the voice's, the mark is the one that floats.
        var rest = Assert.Single(with.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.RestQuarter));
        Assert.Equal(with.StaffRefpoints()[0] - 2.0, rest.Y, 3);
    }

    /// <summary>The mirror: an upper voice's <c>.down</c> mark under which the lower voice rests.</summary>
    [Fact]
    public void TheMirror_ADownMarkOverTheLowerVoicesRest_StandsClearBelowIt()
    {
        var g = RenderedGeometry.Render("""
            octave absolute
            time 2/4
            part rh { clef treble }
            score { staff rh }
            section Piece {
              rh {
                voice { e'16 d'16 e'8 c'8@portato.down d'8@portato.down } { a16 g16 a8 r4 } |
              }
            }
            """);
        double middle = g.StaffRefpoints()[0];
        var portatos = g.Glyphs.Where(x => IsPortato(x.Glyph)).OrderBy(x => x.X).ToList();
        Assert.Equal(2, portatos.Count);
        var rest = Assert.Single(g.Glyphs.Where(x => x.Glyph == EmmentalerGlyphs.RestQuarter));
        // The lower voice's rest, pushed below its −4 by the upper voice's c' (Rest_collision):
        // below the middle, on a line (the collision's whole-space step inside the staff).
        Assert.True(rest.Y > middle + 1.9, $"the lower voice's rest reads {rest.Y - middle:F3} below the middle");
        Assert.Equal(0.0, (rest.Y - middle) % 1.0, 3);
        var underRest = portatos[0];
        Assert.InRange(underRest.X, rest.X - 1.0, rest.X + 1.0);
        Assert.Equal(-0.45, SupportDistance(rest, underRest, middle, markAbove: false), 3);
        double restInkBottom = rest.Y - GlyphMetrics.GetRestSkylineBBox(4).Bottom;
        double markInkTop = underRest.Y - MusicFont.Current.FullSize.Box(PortatoGlyphOf(underRest.Glyph)).Top;
        Assert.True(markInkTop > restInkBottom,
            $"the portato's ink top {markInkTop:F3} is not below the rest's ink bottom {restInkBottom:F3}");
        // d'8's mark, with no rest under it, keeps the plain answer: higher than c'8's.
        Assert.True(portatos[1].Y < underRest.Y - 1.0,
            $"the clear mark reads {portatos[1].Y:F3}, the lifted one {underRest.Y:F3}");
    }
}
