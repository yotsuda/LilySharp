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
/// The page breaker prices a section label stacked over a row the system opens with (owner's
/// report 2026-11-01, 帰り道の灯り: "ギターダイアグラム図が、直下の五線に食い込んで重なっている").
/// Priced as resting on the staff the label cost its line nothing, one page was chosen at cost 0,
/// the chain came out 13.0 over the paper, and the compression pressed each chord row with its
/// diagrams into the staff under it.
/// </summary>
[Trait("Category", "Unit")]
public class MarkOverLeadingRowPagingTests
{
    private const string Sections = """
        section Intro { singer { R@mp | R | R | R | } }
        section Verse {
          singer {
            b8 cis' dis' fis' dis' cis' b cis' | ais'4 gis'8 fis'8 gis'8 ais'4 r8 |
            gis'8. fis'16 e'8 dis' cis' b cis' dis' | cis'8 b'4 ais'8 gis'8 fis'4 r8 |
          }
          lyrics words sings singer {
            ゆ う や け ま ち を そ | め て い く よ |
            か え り み ち に ほ し | が と も る よ |
          }
        }
        section Chorus {
          singer {
            b'8@mf cis'' dis'' dis'' cis'' b' ais' fis' | gis'8 fis'4 dis'8 fis'8 ais'4 r8 |
            fis'8 gis' fis' dis' e' dis' cis' b | cis'8 b'4 ais'8 gis'8 b'4 r8 |
          }
          lyrics words sings singer {
            あ か り が き み を ま | っ て い る よ |
            な み だ の あ と に そ | ら を み る よ |
          }
        }
        section Verse2 {
          singer {
            b8 cis' dis' fis' gis' fis' dis' cis' | dis'8 fis'4 gis'8 fis'8 dis'4 r8 |
            gis'4 fis'8 e'8 dis'16 cis'16 b8 cis'8 dis'8 | cis'4 b'8 ais'8 gis'8 fis'4 r8 |
          }
          lyrics words sings singer {
            こ と ば も か ぜ に と | け て い く よ |
            だ れ か の え が お を | お も い だ す |
          }
        }
        section Bridge {
          singer {
            cis'4@p e'16 fis'16 gis'8 fis'8 e'8 dis'8 cis'8 | dis'4 cis'8 b8 ais'8 gis'4 r8 |
            dis'8 fis' gis' fis' dis' cis' b cis' | ais'8 gis'4 fis'8 gis'8 ais'4 r8 |
          }
          lyrics words sings singer {
            ま よ う よ る も そ ば | に い る か ら |
            あ し た へ つ づ く み | ち が あ る よ |
          }
        }
        section ChorusLast {
          singer {
            b'8@f cis'' dis'' e''@ff dis'' cis'' b' ais' | gis'8 fis'4 dis'8 fis'8 ais'4 r8 |
            fis'8 gis' fis' dis' e' dis' cis' b | cis'8 b'4 ais'8 gis'8 b'4 r8 |
          }
          lyrics words sings singer {
            あ か り が き み を ま | っ て い る よ |
            な み だ の あ と に そ | ら を み る よ |
          }
        }
        """;

    private const string Head = """
        title "帰り道の灯り"
        octave absolute
        key b major
        capo 4
        tempo "やさしく" 4 = 84
        time 4/4
        layout {
          chordDiagrams all
          chordList true
        }
        part singer { instrument alto }
        chords harmony {
          section Intro { B | F# | G#m | E | }
          section Verse { B | F# | E | F# | }
          section Chorus { E | F# | F# | B | }
          section Verse2 { B | G#m | E | B | }
          section Bridge { C#m | E | B | F# | }
          section ChorusLast { B | F# | F# | B | }
        }
        """;

    private const string Song = Head + "\n" + Sections + """

        form { Intro Verse Chorus Verse2 Bridge ChorusLast }
        score { chords harmony  staff singer  lyrics words }
        """;

    private static ScoreLayout LayoutOf(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindAll(tree).First());
        return new LayoutEngine(score.Paper).Layout(score);
    }

    [Fact]
    public void LabelsStackedOverLeadingRows_AreInThePagesPrice_SoNoPageIsPressedTogether()
    {
        var layout = LayoutOf(Song);
        Assert.True(layout.Pages.Length >= 2, $"{layout.Pages.Length} page(s): the song does not fit one");
        Assert.All(layout.Pages, p => Assert.Equal(0, p.Overflow));
        // The rows stand where the alignment put them, at the system's top: no chain pressed one
        // down into the staff under it.
        foreach (var system in layout.Pages.SelectMany(p => p.Systems))
        {
            var staves = system.StaffGroups.SelectMany(g => g.Staves).ToList();
            Assert.True(staves[0].Y > -0.01, $"the chord row was pressed down by {-staves[0].Y:F3}");
        }
    }
}
