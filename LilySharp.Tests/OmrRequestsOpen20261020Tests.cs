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
/// The OMR's open requests of 2026-10-20 (<c>LilySharp-Omr/docs/repro/lilysharp-requests-open-2026-10-20.md</c>),
/// one net per item, on the request's own books.
/// </summary>
[Trait("Category", "Unit")]
public class OmrRequestsOpen20261020Tests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Render(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine().Layout(score));
    }

    private const string GrandStaffOttava = """
        octave absolute
        time 6/8
        key ees major
        part rh { clef treble }
        part lh { clef bass }
        section Piece {
          rh {
            voice { <bes d' f'>8. g'16 f'8 ees'8 d'16 c'16 r8 } { g8 r16 ges16 g8 g8 c16 d16 ees8 } |
            voice { f'8. aes'16 g'8 c'4 ees'8 } { d8 d16 c16 d8 d4 r8 } |
            voice { f'8 bes16 d'16 ees'8 <aes' c''>8. g'16 g'8 } { c8 d8 ees8 aes16 ges16 g16 c16 d8 } |
          }
          lh {
            g,,8 <f,, aes,,>16 aes,,16 r8 f,8 g8@ottava ees8 |
            dis4 ees8 d4. |
            c4. bes,4 r8@!ottava |
          }
        }
        form { ~Piece }
        score { grandStaff { staff rh "Right" staff lh "Left" } }
        """;

    // Item 1: a lower staff's 8va is outside-staff ink the staves are spaced off. Unreserved,
    // the staves stood CLOSER with the line (9.65 top to top) than without it (12.29, where
    // the same notes stand on ledger lines), and the label printed through the right hand's
    // lower voice.
    [Fact]
    public void Item1_ALowerStaffsOttava_HoldsTheStavesApart()
    {
        double Gap(string book)
        {
            var (_, layout) = Render(book);
            var staves = layout.AllSystems[0].StaffGroups.SelectMany(g => g.Staves).ToList();
            return staves[0].Y - staves[1].Y;
        }
        double withLine = Gap(GrandStaffOttava);
        double withoutLine = Gap(GrandStaffOttava.Replace("@!ottava", "").Replace("@ottava", ""));
        Assert.True(withLine > withoutLine - 1.0,
            $"with the 8va the staves stand {withLine:F3} apart, without it {withoutLine:F3}");
        // And the line stands between the staves, nearer the left hand than the right.
        var (_, layout) = Render(GrandStaffOttava);
        var lhTop = layout.AllSystems[0].StaffGroups.SelectMany(g => g.Staves).Single(s => s.StaffIndex == 1).Y;
        var line = Assert.Single(layout.OttavaBracketLayouts);
        Assert.InRange(line.YUp, lhTop, lhTop + 3.0);
    }

    // Item 2: an 8vb stands outside the tuplet's number and bracket below the staff (LilyPond
    // 2.26.0: number under the beam, the bracket under the number, the ottava under both).
    [Fact]
    public void Item2_ABelowStaffOttava_StandsOutsideTheTuplet()
    {
        var (_, layout) = Render("""
            octave absolute
            time 2/4
            part lh { clef bass }
            section Piece {
              lh {
                bes,,8 ges,,8@ottava(bassa) f,,16@!ottava ees,,16 bes,,8 |
                tuplet 3/2 { des,,8@ottava(bassa) ces,,8 <f, ges,>8@!ottava } ees,4 |
                d,,8@ottava(bassa) tuplet 3/2 { ees,,8 d,,8 <ees,, g,, bes,,>8 } f,,8@!ottava |
              }
            }
            score { staff lh "Left" }
            """);
        const double outsideStaffPadding = 0.46, halfLine = 0.05;
        // Bar 1: the span (one eighth) is shorter than the label, so the label slides left until
        // the minimum line (0.3) stands between it and the hook — not the hook through the "b".
        var line1 = layout.OttavaBracketLayouts.Single(o => o.StartMeasureIndex == 0);
        double label = OttavaBracketEngraver.LineStartX(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled,
            line1.Text, 0.0, OttavaBracketEngraver.LabelEm(LilySharp.Core.Rendering.ScoreTextMetrics.Bundled));
        Assert.True(line1.StartX + label + 0.3 <= line1.EndX + 1e-9,
            $"bar 1's label ends at {line1.StartX + label:F3}, the hook stands at {line1.EndX:F3}");
        // Bar 2: the number rides under the beam (no bracket); the dashed line runs under it.
        var number = layout.TupletBracketLayouts.Single(t => t.MeasureIndex == 1);
        var line2 = layout.OttavaBracketLayouts.Single(o => o.StartMeasureIndex == 1);
        Assert.True(line2.YUp + halfLine + outsideStaffPadding <= number.NumberYUp - 0.55,
            $"bar 2's line at {line2.YUp:F3} clears the number centred at {number.NumberYUp:F3}");
        // Bar 3: the bracket is drawn; the line runs under it.
        var bracket = layout.TupletBracketLayouts.Single(t => t.MeasureIndex == 2);
        Assert.True(bracket.ShowBracket);
        var line3 = layout.OttavaBracketLayouts.Single(o => o.StartMeasureIndex == 2);
        Assert.True(line3.YUp + halfLine + outsideStaffPadding
                    <= System.Math.Min(bracket.StartYUp, bracket.EndYUp),
            $"bar 3's line at {line3.YUp:F3} clears the bracket at {System.Math.Min(bracket.StartYUp, bracket.EndYUp):F3}");
    }

    // Follow-up, item 4: an ottava stopping inside a bar ends after ITS OWN STAFF's last
    // covered note. The right bound read the system's timing columns, which the other staff
    // shares: a right hand's sixteenth between the left hand's ces,, and its closing chord
    // became the bound, and the hook stood past the chord (OMR follow-up 2026-10-20).
    [Fact]
    public void Item4_AnOttavasHook_StopsBeforeTheNextNoteOfItsStaff()
    {
        var (score, layout) = Render("""
            octave absolute
            time 4/4
            key ges major
            part rh { clef treble }
            part lh { clef bass }
            section Piece {
              rh {
                voice { aes'8. ges'16 fes'16 <fis' ges'>16 <ges' aes'>8 <ces' ees'>8 des'16 des'16 ees'8. f'16 } { r8. ees16 f4 aes16 <f aes ces'>16 ees16 aes16 <f aes ces'>8. ces16 } |
                voice { r16 ges'16 f'16 f'16 r8 f'8 f'8. <ees' f'>16 f'8. ges'16 } { ges4. f8 aes4 ges16 f16 r8 } |
              }
              lh {
                tuplet 3/2 { g,8 ges,,8@ottava(bassa) f,,8 } ees,,4. f,,8 bes,,8 ces,8 |
                bes,,8 <ges,, bes,, des,>16 <f,, aes,,>16 tuplet 3/2 { des,,8 ces,,8 <f, ges,>8@!ottava } ees,4 r8 f,8 |
              }
            }
            form { ~Piece }
            score { grandStaff { staff rh "Right" staff lh "Left" } }
            """);
        var lh = score.StaffGroups[0].Staves[1].PrimaryVoice.Measures;
        var bar2 = layout.AllSystems.SelectMany(s => s.Measures).Single(m => m.MeasureIndex == 1);
        // ces,, is the left hand's item 4 in bar 2, the chord its item 5.
        double ces = bar2.X + LayoutUtilities.GetItemXOffset(lh, 1, 4, bar2);
        double chord = bar2.X + LayoutUtilities.GetItemXOffset(lh, 1, 5, bar2);
        var line = Assert.Single(layout.OttavaBracketLayouts);
        Assert.True(line.EndX > ces, $"the hook at {line.EndX:F3} stands after ces,, ({ces:F3})");
        Assert.True(line.EndX < chord, $"the hook at {line.EndX:F3} stands before the chord ({chord:F3})");
    }

    // A free text below the staff (TextScript, 450) stands outside an 8vb (400) — LilyPond's
    // order. It was placed before the bracket existed, and "dolce" printed over the dashes.
    [Fact]
    public void AFreeTextBelow_StandsOutsideAnOttavaBelow()
    {
        var (score, layout) = Render("""
            octave absolute
            time 4/4
            part p1 { clef bass }
            section Piece {
              p1 {
                c,4@ottava(bassa) d,4@text("dolce") e,4 f,4@!ottava | c1 |
              }
            }
            score { staff p1 }
            """);
        var line = Assert.Single(layout.OttavaBracketLayouts);
        var text = Assert.Single(layout.DynamicLayouts.Where(d => d.IsExpressiveText));
        Assert.False(text.IsAbove);
        // The text's baseline Y is about the staff middle; the line's about the system top,
        // 2 above the middle on this one staff. The text's ink top stands below the line.
        var (textUp, _) = DynamicEngraver.LabelSkylines(score.TextMetrics, text.Text,
            text.IsExpressiveText, text.X, text.YUp - 2.0, text.IsSubito);
        double textTopInSystem = double.NegativeInfinity;
        for (double x = line.StartX; x <= line.EndX; x += 0.05)
            if (!double.IsInfinity(textUp.Height(x)))
                textTopInSystem = System.Math.Max(textTopInSystem, textUp.Height(x));
        Assert.False(double.IsNegativeInfinity(textTopInSystem), "the text stands under the line");
        Assert.True(textTopInSystem < line.YUp - 0.05,
            $"the text's top {textTopInSystem:F3} stands below the line {line.YUp:F3}");
    }

    // The staff memory on a CONDENSED staff, whose voices come from several parts: each part's
    // own pass sees only its own voices, so the staff's pass runs again over the condensed
    // voices (the gap the first fix of 2026-10-20 named).
    [Fact]
    public void ACondensedStaff_HoldsAnAccidentalAcrossItsParts()
    {
        var (score, _) = Render("""
            octave absolute
            time 4/4
            key des major
            part fl1 { clef treble }
            part fl2 { clef treble }
            section Piece {
              fl1 { fes'4 ges'4 aes'4 bes'4 | }
              fl2 { des'4 ees'4 f'4 ges'4 | }
            }
            form { ~Piece }
            score { condensedStaff { fl1 fl2 } }
            """);
        var staff = score.StaffGroups.SelectMany(g => g.Staves).Single();
        Assert.Equal(2, staff.Voices.Length);
        // The second part's f' after the first part's fes' prints its natural.
        Assert.Equal("natural", Assert.IsType<NoteItem>(staff.Voices[1].Measures[0].Items[2]).Accidental);
    }

    // Item 3: a grace note's accidental is decided by the staff's memory too — drawn from the
    // grace groups' columns, which carry it as well as the grace's item does.
    [Fact]
    public void Item3_AGraceInAnotherVoice_PrintsTheStaffsAccidental()
    {
        var (score, _) = Render("""
            octave absolute
            time 4/4
            key c major
            part p1 { clef treble }
            section Piece {
              p1 {
                voice { c''4 bes'4 c''4 c''4 } { g'4 g'4 g'4 b'4 } |
                voice { c''4 bes'4 c''4 c''4 } { g'4 g'4 g'4 grace { b'16 } a'4 } |
                voice { c''4 bes'4 c''4 c''4 } { b'4 g'4 g'4 grace { b'16 } a'4 } |
                voice { c''4 bes'4 grace { b'16 } c''4 c''4 } { g'4 g'4 g'4 g'4 } |
              }
            }
            form { ~Piece }
            score { staff p1 "Staff" }
            """);
        var lower = score.StaffGroups[0].Staves[0].Voices[1].Measures;
        foreach (int bar in new[] { 1, 2 })
        {
            var grace = lower[bar].Items.OfType<NoteItem>().Single(n => n.GraceTime);
            Assert.Equal("natural", grace.Accidental);
            var group = score.GraceNotes.Single(g => g.VoiceIndex == 1 && g.MeasureIndex == bar);
            Assert.Equal("natural", group.Columns[0].Heads[0].Accidental);
            var main = lower[bar].Items[group.MainNoteItemIndex];
            Assert.Equal("natural", ((NoteItem)main).LeadingGrace[0].Heads[0].Accidental);
        }
    }
}
