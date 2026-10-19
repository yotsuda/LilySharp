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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The OMR's report of 2026-10-20 (<c>LilySharp-Omr/docs/repro/lilysharp-bug-report-2026-10-20.md</c>),
/// one net per item, each on that report's own book (shortened where the shape allows).
/// </summary>
[Trait("Category", "Unit")]
public class OmrReport20261020Tests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Render(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine().Layout(score));
    }

    private static MeasureLayout Bar(ScoreLayout layout, int measure)
        => layout.AllSystems.SelectMany(s => s.Measures).Single(m => m.MeasureIndex == measure);

    private static SystemLayout SystemOf(ScoreLayout layout, int measure)
        => layout.AllSystems.Single(s => s.Measures.Any(m => m.MeasureIndex == measure));

    private const string GrandStaffHead =
        "octave absolute\ntime 4/4\npart rh { clef treble }\npart lh { clef bass }\nsection Piece {\n" +
        "  rh { c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''8 d''8 e''8 f''8 g''8 a''8 b''8 c'''8 | c''1 | }\n";

    private const string GrandStaffTail = "}\nscore { grandStaff { staff rh staff lh } }\n";

    // Item 1: a lower staff's `@rit` is bound by ITS OWN notes, not the upper staff's of the
    // same index (TextSpannerEngraver read the layout's item slots, which are the first staff's).
    [Fact]
    public void Item1_ALowerStaffsTextSpanner_IsBoundByItsOwnNotes()
    {
        var (score, layout) = Render(GrandStaffHead
            + "  lh { c2 e4@rit g4 | c2 e4 g4@!rit | c1 | }\n" + GrandStaffTail);
        var lh = score.StaffGroups[0].Staves[1].PrimaryVoice.Measures;
        var spanner = Assert.Single(layout.TextSpannerLayouts.Where(t => t.Text != ""));
        var bar1 = Bar(layout, 0);
        // The left hand's e4 is its item 1, at the half bar; the right hand's item 1 is d''8.
        double e = bar1.X + LayoutUtilities.GetItemXOffset(lh, 0, 1, bar1);
        Assert.InRange(spanner.StartX - e, 0, 1);
        var bar2 = Bar(layout, 1);
        double g = bar2.X + LayoutUtilities.GetItemXOffset(lh, 1, 2, bar2);
        Assert.InRange(g - layout.TextSpannerLayouts.Max(t => t.EndX), 0, 1);
    }

    // Item 2: the same for the trill line.
    [Fact]
    public void Item2_ALowerStaffsTrillSpanner_IsBoundByItsOwnNotes()
    {
        var (score, layout) = Render(GrandStaffHead
            + "  lh { c4 d4@startTrillSpan e4 f4@stopTrillSpan | g,2@startTrillSpan a,4 b,4@stopTrillSpan | c1 | }\n"
            + GrandStaffTail);
        var lh = score.StaffGroups[0].Staves[1].PrimaryVoice.Measures;
        var trills = layout.TrillSpannerLayouts.OrderBy(t => t.StartMeasureIndex).ToList();
        Assert.Equal(2, trills.Count);
        var bar1 = Bar(layout, 0);
        double d = bar1.X + LayoutUtilities.GetItemXOffset(lh, 0, 1, bar1);
        double f = bar1.X + LayoutUtilities.GetItemXOffset(lh, 0, 3, bar1);
        Assert.InRange(trills[0].GlyphX - d, 0, 1.5);
        Assert.Equal(f, trills[0].LineEndX, 6);
        var bar2 = Bar(layout, 1);
        double b = bar2.X + LayoutUtilities.GetItemXOffset(lh, 1, 2, bar2);
        Assert.Equal(b, trills[1].LineEndX, 6);
    }

    // Item 3: a second voice's arpeggio stands left of ITS chord, not of the first voice's
    // item of the same index.
    [Fact]
    public void Item3_ASecondVoicesArpeggio_StandsLeftOfItsOwnChord()
    {
        var (score, layout) = Render(
            "octave absolute\ntime 4/4\npart p1 { clef treble }\nsection Piece {\n" +
            "  p1 { voice { r8 g''8 r8 e''8 c''2 } { d'4 f'8 <d' f' a'>8@arpeggio <c' e'>2 } | }\n" +
            "}\nform { ~Piece }\nscore { staff p1 \"Staff\" }\n");
        var lower = score.StaffGroups[0].Staves[0].Voices[1].Measures;
        var bar = Bar(layout, 0);
        double f = bar.X + LayoutUtilities.GetItemXOffset(lower, 0, 1, bar);
        double chord = bar.X + LayoutUtilities.GetItemXOffset(lower, 0, 2, bar);
        var arpeggio = Assert.Single(layout.ArpeggioLayouts);
        Assert.True(arpeggio.X > f && arpeggio.X < chord,
            $"the wiggle at {arpeggio.X} stands between f'8 ({f}) and its chord ({chord})");
    }

    // Item 4: a rolled chord inside a tuplet draws its wiggle.
    [Fact]
    public void Item4_ARolledChordInATuplet_DrawsItsArpeggio()
    {
        var (_, layout) = Render(
            "octave absolute\ntime 4/4\npart p1 { clef treble }\nsection Piece {\n" +
            "  p1 { <c' e' g'>4@arpeggio tuplet 3/2 { <c' e' g'>8@arpeggio <d' f'>8 <e' g'>8@arpeggio } <c' e' g'>2@arpeggio | }\n" +
            "}\nform { ~Piece }\nscore { staff p1 \"Staff\" }\n");
        Assert.Equal(4, layout.ArpeggioLayouts.Length);
        Assert.Equal(4, layout.ArpeggioLayouts.Select(a => a.X).Distinct().Count());
    }

    // Item 5: a trill line starting closer to the first system's end than the first system's
    // indent draws its first piece (the piece's edge was read without the indent).
    [Fact]
    public void Item5_ATrillStartingNearTheFirstSystemsEnd_DrawsItsFirstPiece()
    {
        var (_, layout) = Render(
            "octave absolute\ntime 3/4\nkey g major\npart p1 { clef bass }\nsection Piece {\n  p1 {\n" +
            "    d,8. a,16 a,8 bes,8 e,16 r16 d,16 e,16 |\n    d,16 e,16 b,8 c4 b,16 a,16 b,8 |\n" +
            "    tuplet 3/2 { c8 b,8 c8 } b,4. b,8 |\n" +
            "    a,8 b,16 c16 b,8. d,16 c,16 b,,16@startTrillSpan a,,8 |\n" +
            "    a,,8 g,,16 a,,16@stopTrillSpan b,,4. a,,8 |\n    r2. | g,,2. |\n" +
            "    r16 fis,,16 g,,16 a,,16 b,,8 c,16 fis,,16 c,4 |\n    b,,4 a,,8. b,,16 a,,8. g,,16 |\n" +
            "    f,,4 e,,8 c,8 b,,4 |\n    a,,8 e,8 fis,2 |\n  }\n}\nform { ~Piece }\nscore { staff p1 \"Staff\" }\n");
        Assert.True(SystemOf(layout, 3) != SystemOf(layout, 4), "the trill crosses a line break");
        var pieces = layout.TrillSpannerLayouts.OrderBy(t => t.StartMeasureIndex).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal(3, pieces[0].StartMeasureIndex);
        var first = SystemOf(layout, 3);
        Assert.True(pieces[0].LineEndX > pieces[0].GlyphX, "the first piece has its glyph and a wave");
        Assert.Equal(first.Measures[^1].X + first.Measures[^1].Width, pieces[0].LineEndX, 0);
    }

    // Item 6: a lower staff's 8vb and its "(8vb)" continuation stand under THAT staff on
    // THEIR OWN system, and the label starts at the lower staff's own note.
    [Fact]
    public void Item6_ALowerStaffsOttava_StandsUnderItsStaffOnEverySystem()
    {
        var (score, layout) = Render(OttavaContinuationBook);
        var lh = score.StaffGroups[0].Staves[1].PrimaryVoice.Measures;
        var lower = layout.OttavaBracketLayouts.Where(o => o.StaffIndex == 1).ToList();
        Assert.True(lower.Count >= 4, $"8vb pieces on the left hand: {lower.Count}");
        foreach (var piece in lower)
        {
            var system = SystemOf(layout, piece.StartMeasureIndex);
            var lhStaff = system.StaffGroups.SelectMany(g => g.Staves).Single(s => s.StaffIndex == 1);
            var rhStaff = system.StaffGroups.SelectMany(g => g.Staves).Single(s => s.StaffIndex == 0);
            Assert.True(piece.YUp < lhStaff.Y - lhStaff.Height,
                $"{piece.Text} of bar {piece.StartMeasureIndex + 1} at {piece.YUp} stands under the left hand (bottom {lhStaff.Y - lhStaff.Height})");
            Assert.True(piece.YUp < rhStaff.Y - rhStaff.Height, "…and not anywhere near the right hand");
        }
        // Bar 4's 8vb begins at <e,, gis,,>16, the left hand's item 4.
        var bar4 = Bar(layout, 3);
        var start = lower.Single(o => o.StartMeasureIndex == 3 && !o.Text.StartsWith('('));
        double chord = bar4.X + LayoutUtilities.GetItemXOffset(lh, 3, 4, bar4);
        Assert.InRange(start.StartX - chord, -1.5, 0.5);
    }

    // Item 7: an ottava carried over a break that ends INSIDE a tuplet keeps the tuplet's
    // first note under the line (the closing mark read the tuplet's first moment).
    [Fact]
    public void Item7_AnOttavaEndingInsideATuplet_CoversTheNotesBeforeTheEnd()
    {
        var (score, layout) = Render(
            "title \"ott-tuplet\"\noctave absolute\ntime 4/4\nkey aes major\npart p1 { clef treble }\nsection Piece {\n" +
            "  p1 {\n    c'4 d'4 ees'4 f'4 |\n" +
            "    fis''4@quindicesima ges''4. aes''8 tuplet 3/2 { g''8 c'''8 bes''8 } | break\n" +
            "    c'''4 tuplet 3/2 { des'''8 c'8@!ottava bes8 } r4 aes4 |\n    c'1 |\n  }\n}\n" +
            "score { staff p1 }\n");
        var bar3 = score.StaffGroups[0].Staves[0].PrimaryVoice.Measures[2].Items;
        var c = Assert.IsType<NoteItem>(bar3[0]);
        var des = Assert.IsType<NoteItem>(bar3[1]);
        // des''' is drawn two octaves down like c''' before it: one step above it.
        Assert.Equal(c.StaffPosition + 1, des.StaffPosition);
        var continuation = Assert.Single(layout.OttavaBracketLayouts.Where(o => o.StartMeasureIndex == 2));
        var bar = Bar(layout, 2);
        var measures = score.StaffGroups[0].Staves[0].PrimaryVoice.Measures;
        double desX = bar.X + LayoutUtilities.GetItemXOffset(measures, 2, 1, bar);
        Assert.True(continuation.EndX > desX, "the continuation's line reaches past des'''");
    }

    // Item 8: an accidental shown in one voice holds for the other voices of the staff.
    [Fact]
    public void Item8_AnAccidentalInOneVoice_HoldsForTheStaffsOtherVoices()
    {
        var (score, _) = Render(
            "octave absolute\ntime 4/4\nkey des major\npart p1 { clef treble }\nscore { staff p1 }\nsection Piece {\n" +
            "  p1 {\n    voice { fes'4 ges'4 aes'4 bes'4 } { des'4 ees'4 f'4 ges'4 } |\n" +
            "    voice { g'4 aes'4 bes'4 c''4 } { des'4 ges'4 aes'4 bes'4 } |\n  }\n}\nform { ~Piece }\n");
        var lower = score.StaffGroups[0].Staves[0].Voices[1].Measures;
        // Bar 1: the lower voice's f' after the upper voice's fes' prints its natural.
        Assert.Equal("natural", Assert.IsType<NoteItem>(lower[0].Items[2]).Accidental);
        // Bar 2: the lower voice's ges' after the upper voice's g' prints its flat.
        Assert.Equal("flat", Assert.IsType<NoteItem>(lower[1].Items[1]).Accidental);
        // …and nothing else gains one: the lower voice's other notes are the key's.
        Assert.Null(Assert.IsType<NoteItem>(lower[0].Items[0]).Accidental);
        Assert.Null(Assert.IsType<NoteItem>(lower[1].Items[2]).Accidental);
    }

    // Item 10: a head drawn on the line or space an ottava's other side used, in another
    // octave and with another alteration, prints its accidental.
    [Fact]
    public void Item10_AHeadDrawnWhereAnOttavasOtherSideStood_PrintsItsAccidental()
    {
        var (score, _) = Render(
            "octave absolute\ntime 4/4\nkey a major\npart p1 { clef treble }\nsection Piece {\n" +
            "  p1 {\n    fis'8@ottava cisis''8 r16 b16@!ottava cis'8 d'2 |\n" +
            "    d'8@ottava ais'8 b'8 r16 b16@!ottava a8 d'4. |\n    a'1 |\n  }\n}\n" +
            "form { ~Piece }\nscore { staff p1 \"Staff\" }\n");
        var measures = score.StaffGroups[0].Staves[0].PrimaryVoice.Measures;
        var cis = Assert.IsType<NoteItem>(measures[0].Items[4]);
        var cisis = Assert.IsType<NoteItem>(measures[0].Items[1]);
        Assert.Equal(cisis.StaffPosition, cis.StaffPosition);
        // After the double sharp the sharp restores with a natural, as the walk spells it.
        Assert.Equal("naturalSharp", cis.Accidental);
        Assert.Equal("natural", Assert.IsType<NoteItem>(measures[1].Items[5]).Accidental);
        // b16, drawn where b' was not, gains nothing.
        Assert.Null(Assert.IsType<NoteItem>(measures[0].Items[3]).Accidental);
    }

    private const string OttavaContinuationBook = """
        title "piano006"
        octave absolute
        time 12/8
        key b major

        part rh { clef treble }
        part lh { clef bass }

        section Piece {
          rh {
            voice { fis'16 gis'16 ais'16 <gis' ais'>16 ais'8 r8. <g' b'>16 ais'8 r8 gis'8 fis'8 gis'8 ais'16 gis'16 fis'8 } { fisis4 e8 <a cis'>8. <e fis>16 r8 r16 disis16 ais16 a16 <gis b>8 ais8. ais16 gis8 } |
            voice { cis'4 cis'8 <dis' e'>8. <cis' e' gis'>16 <disis' fis' ais'>8 gis'8 e'8 fis'8 r8 cis'8 b8 } { <fis ais cis'>4 ais8 gis8. gis16 fis8 e4. cis8. r16 d8 } |
            voice { cis'8 r16 dis'16 cis'8 fis'4 b8 cis'4. cis'8 b16 e'16 ais'8 } { cis8. r16 r8 r8. dis16 gis8 ais16 ais16 gis16 fis16 cis8 a4 gis8 } |
            voice { <ais' cis''>1. } { ais4. r16 gis16 ais16 <gis b>16 ais8 r4 cis8 dis4 e8 } |
            <ais cis'>8 gis8 dis8 b8 fis'16 gis'16 fis'8 e'8 <ais cis' e'>8 ais8 r8 gis16 ais16 r8 |
            <ais cis'>16 e16 r16 fis16 e8 dis8 e16 fisis16 gis8 ais8 gis16 fis16 <gis b dis'>8 gis4. |
            <disis fis>16 fis16 e16 cis16 fis8 e4 dis8 cis'4.@ottava <dis' fis' ais'>4. |
            ais'4 gis'8 <dis'' fis''>4. <ais cis' e'>8.@!ottava b16 b8 e'8. dis'16 <cis' e'>8 |
            dis'8 ais8 r8 gis8. dis16 e8 dis16 cis16 fis16 <gis b dis'>16 ais8 <b dis' fis'>8. ais16 dis'8 |
            cis'16 bis16 r16 ais16 ais8 <a cis' e'>16 <dis fis ais>16 cis16 dis16 cis8 eis4. dis8 e16 b16 b8 |
          }
          lh {
            r8 b,,16 ais,,16 <b,, cis,>8 <fis, ais,>4. e,8. <fis, gis,>16 cis8 gis,4. |
            ais,1. |
            dis,4. e,4. dis,8 e,16 gis,16 gis,8 r16 r16 <cis e>16 b,16 cis8 |
            r4. b,16 <a, cis e>16 b,16 <e,, gis,,>16@ottava(bassa) dis,,8 dis,,8. cis,,16 <dis,, fis,,>8 b,,4 cis,8 |
            cis,4 <fis,, ais,,>8 gis,,4. <dis,, fis,,>8 <ais,,, b,,,>8 b,,8@!ottava <b,, dis,>8 <b,, dis, fis,>16 <cis, dis,>16 e,,8 |
            fis,,,1.@ottava(bassa) |
            <fis,,, ais,,,>4. cis,,4 b,,,8 <gis,, b,,>4. ais,,4. |
            <gis,, ais,,>4. r8 b,,,8 gis,,,8 fisis,,,8. r16 e,,8@!ottava e,,8 r8 e,,8 |
            b,,4. <e,, gis,,>16 gis,,16 <gis,, b,,>16 fis,,16 <e,, gis,,>8 fis,,16 gis,,16 r16 cis,,16@ottava(bassa) dis,,8 <e,, gis,,>4. |
            b,,,8 r8 cis,8@!ottava <b,, dis,>8 gis,,16 ais,,16 r8 fisis,,4. eis,,4 cis,8 |
          }
        }

        form { ~Piece }

        score {
          grandStaff {
            staff rh "Right"
            staff lh "Left"
          }
        }
        """;
}
