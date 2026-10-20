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
/// The OMR's late requests of 2026-10-20 (<c>LilySharp-Omr/docs/repro/lilysharp-requests-2026-10-20-late.md</c>),
/// one net per item.
/// </summary>
[Trait("Category", "Unit")]
public class OmrRequestsLate20261020Tests
{
    private static (MultiStaffScore Score, ScoreLayout Layout) Render(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join(", ", tree.Diagnostics.Select(d => d.Message)));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine().Layout(score));
    }

    private static string OttavaBook(string bar) => $$"""
        octave absolute
        time 4/4
        key g major
        part p1 { clef bass }
        section Piece {
          p1 {
            {{bar}} |
          }
        }
        form { ~Piece }
        score { staff p1 "Staff" }
        """;

    // Item 3: a pitch shown under an 8vb (drawn on G2, with its flat) and again past the line
    // (drawn on G1) printed no flat there — nothing in the bar had flattened G1, so a reader
    // saw g. The drawn-position rule (StaffAccidentals ⑶) gives it its flat.
    [Fact]
    public void Item3_PastAnOttava_TheSamePitchOnAnotherLine_PrintsItsAccidental()
    {
        var (score, _) = Render(OttavaBook(
            "fis,,,16@ottava(bassa) ges,,,16 c,,,16 c,,,16 ges,,,8@!ottava r8 r2"));
        var items = score.StaffGroups[0].Staves[0].PrimaryVoice.Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Equal("flat", items[1].Accidental);   // under the line, on G2
        Assert.Equal("flat", items[4].Accidental);   // past it, on G1
        Assert.NotEqual(items[1].StaffPosition, items[4].StaffPosition);
    }

    // The control: past the line, the pitch the key already gives that line (F-sharp in G
    // major) needs nothing — the reader assumes the key there.
    [Fact]
    public void Item3_PastAnOttava_ThePitchTheKeyGives_PrintsNothing()
    {
        var (score, _) = Render(OttavaBook(
            "fis,,,16@ottava(bassa) c,,,16 c,,,16 c,,,16 fis,,,8@!ottava r8 r2"));
        var items = score.StaffGroups[0].Staves[0].PrimaryVoice.Measures[0].Items.OfType<NoteItem>().ToList();
        Assert.Null(items[4].Accidental);
    }

    // Item 2: a trill above a system is ink the PAGE spaces systems by. It reached the scalar
    // extents only, so the next system's trill printed into the low notes hanging under the
    // system above (ledger002, bars 13 and 16).
    [Fact]
    public void Item2_ATrillAboveASystem_HoldsTheSystemAboveAway()
    {
        static double Gap(string trill)
        {
            var (_, layout) = Render($$"""
                octave absolute
                time 4/4
                part p1 { clef bass }
                section Piece {
                  p1 {
                    c,,,4 c,,,4 c,,,4 c,,,4 | break
                    a'4{{trill}} g'4 a'4 g'4 |
                  }
                }
                form { ~Piece }
                score { staff p1 "Staff" }
                """);
            var systems = layout.AllSystems;
            Assert.Equal(2, systems.Length);
            return systems[1].Y - systems[0].Y;
        }
        double with = Gap("@startTrillSpan");
        double without = Gap("");
        // The trill's line stands well over a staff space above the a's head.
        Assert.True(System.Math.Abs(with) > System.Math.Abs(without) + 0.5,
            $"the systems stand {with:F3} apart with the trill, {without:F3} without");
    }

    // Item 1: a lower staff's trill that runs on to the next system holds the staves apart on
    // both systems, as one that ends on its own system does (piano003 bars 4-5).
    [Fact]
    public void Item1_ALowerStaffsTrillAcrossABreak_HoldsTheStavesApart()
    {
        static double Gap(string start, string stop)
        {
            var (_, layout) = Render($$"""
                octave absolute
                time 4/4
                part rh { clef treble }
                part lh { clef bass }
                section Piece {
                  rh {
                    voice { c''4 c''4 c''4 c''4 } { c16 d16 e16 f16 c16 d16 e16 f16 c16 d16 e16 f16 c16 d16 e16 f16 } | break
                    voice { c''4 c''4 c''4 c''4 } { c16 d16 e16 f16 c16 d16 e16 f16 c16 d16 e16 f16 c16 d16 e16 f16 } |
                  }
                  lh {
                    c4{{start}} c4 c4 c4 |
                    c4 c4{{stop}} c4 c4 |
                  }
                }
                form { ~Piece }
                score { grandStaff { staff rh "Right" staff lh "Left" } }
                """);
            var staves = layout.AllSystems[0].StaffGroups.SelectMany(g => g.Staves).ToList();
            return staves[0].Y - staves[1].Y;
        }
        double with = Gap("@startTrillSpan", "@stopTrillSpan");
        double without = Gap("", "");
        Assert.True(System.Math.Abs(with) > System.Math.Abs(without) + 0.5,
            $"the staves stand {with:F3} apart with the trill, {without:F3} without");
    }
}
