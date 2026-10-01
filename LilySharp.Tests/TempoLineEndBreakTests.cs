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
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The line-break gate prices a line's last bar with its columns' keep-inside-line rods — a
/// metronome mark's ink among them (MultiStaffLayouter.ColumnOverhangs,
/// MeasureSpringData.LineEndSprings).
/// </summary>
/// <remarks>
/// The book is the user's stress test 01 (Lab sessions/p733/big/01-piano-nocturne.lys). Bar 13
/// opens with a 6/8 and C minor change and <c>tempo "Più mosso" 4. = 76</c>. LilyPond 2.26
/// breaks it 7/6/5/7, because the mark widens every candidate line ending at bar 13 by 1.647
/// (Lab sessions/p734/lb). Lily# broke it 6/6/6/7 until session 735: the gate priced no
/// keep-inside rod, and the mark stood on the bar's first note instead of on the meter.
/// The ledger points tempo.x.mid-line-meter-change and tempo.line-end.right-from-end-bar
/// price the two halves against LilyPond. This test is the observer of the gate.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TempoLineEndBreakTests
{
    private const string Nocturne = """
        title "Nocturne in Shadows"
        tempo "Andante cantabile" 4 = 66
        time 3/4
        key ees major
        octave absolute

        part rh { clef treble }
        part lh { clef bass pedal mixed }

        section Intro {
          rh {
            r4 g'4(@p bes'4 | ees''2.)@fermata |
            grace { f''16 g'' } aes''4( g''8 f'' ees'' d'') | ees''2 r4 |
          }
          lh {
            ees,4@sustain <bes, g>2 | <ees, bes, g>2.@arpeggio |
            aes,4@sustain <c ees>2 | bes,4@sustain <d aes>2@!sustain |
          }
        }

        section Theme {
          rh {
            voice { bes'4.(@mp c''8 d''4 | ees''2 f''4) | g''4.( f''8 ees''4 | d''2.) |
                    c''4( tuplet 3/2 { d''8 ees'' f'' } g''4~ | g''8 f''@trill ees''4 d'') | }
                  { g'2 aes'4 | g'2 aes'4 | bes'2 c''4 | bes'4 aes' f' |
                    aes'2 f'4 | g'2 f'4 | }
            ees''4@phrasingSlur@cresc acciaccatura { aes''16 } g''4( bes''4) | ees'''2.@f@!phrasingSlur |
          }
          lh {
            ees,4@sustain bes, g | ees,4@sustain bes, aes | ees,4@sustain c aes | bes,,4@sustain f d |
            aes,,4@sustain ees c | bes,,4@sustain f d | c4@sustain g ees' | <bes,, bes,>2.@!sustain |
          }
        }

        section Middle {
          time 6/8
          key c minor
          rh {
            tempo "Più mosso" 4. = 76
            c''8(@mf@cresc d'' ees'' f''4 g''8) | aes''8( g'' f'' ees''4.)@f |
            tuplet 2/3 { d''8@decresc( ees'') } c''4.@p | b'4.( g'4.) |
            c'''8@ottava( b'' aes'' g''4 f''8) | ees''8( d'' c''@!ottava b'4.) |
            tuplet 3/2 { c''16 d'' ees'' } tuplet 3/2 { f''16 g'' aes'' } b''8 c'''4.@fermata |
            <g' c'' ees''>2.@arpeggio@pp |
          }
          lh {
            c,8@sustain g, ees g c' g | f,8@sustain c aes c' f' c' |
            g,,8@sustain d b, d g d | g,,8 b, d g4.@!sustain |
            aes,8@sustain ees aes c' ees' aes' | g,8@sustain d g b d' g'@!sustain |
            g,,4. g,4. | <c, g, c>2.@arpeggio |
          }
        }

        section Ending1 {
          time 3/4
          key ees major
          rh { tempo "Tempo I" 4 = 66 bes'2.~ | bes'2 r4 | }
          lh { <ees, bes,>2.~ | <ees, bes,>2 r4 | }
        }

        section Ending2 {
          rh {
            appoggiatura { f''8 } ees''2.@turn | tuplet 5/4 { d''16( ees'' f'' g'' aes'') } bes''2 |
            ees'''2.@ppp@fermata |
          }
          lh {
            <aes,, aes,>2.@sustain | <bes,, bes,>2. | <ees,, ees,>2.@!sustain@fermata |
          }
        }

        form main { ~Intro |: Theme Middle [1. Ending1] :| [2. Ending2] }

        score main {
          grandStaff {
            staff rh "Piano"
            staff lh
          }
        }
        """;

    private static int[] BarsPerSystem(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var layout = new LayoutEngine(score.Paper).Layout(score);
        return layout.AllSystems.Select(s => s.Measures.Length).ToArray();
    }

    [Fact]
    public void ATempoMarkWiderThanItsBar_LengthensTheLinesEndingThere_LikeLilyPond()
    {
        // LilyPond 2.26.0: 7 | 6 | 5 | 7 (Lab sessions/p734/lb, pinned fonts).
        // Without the gate's line-end rods: 6 | 6 | 6 | 7.
        Assert.Equal(new[] { 7, 6, 5, 7 }, BarsPerSystem(Nocturne));
    }
}
