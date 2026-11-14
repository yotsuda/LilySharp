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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// One written hairpin mark met more than once (session 1241). Played again in ANOTHER play —
/// a section the form lists twice — it is another playing of the same hairpin and never the
/// end of the first (2026-08-29, test/hairpin-in-a-repeated-section). Played again in its OWN
/// play — a phrase used twice — it is the next hairpin, and ends the one before it as any
/// other hairpin's start does. Until session 1241 the guard of the first case passed over the
/// second too: `riff | riff | c'1@f` ran the first playing's hairpin on to the f, through the
/// second's, and drew the two wedges one over the other (Lab sessions/p1240/xml/s3).
/// </summary>
[Trait("Category", "Unit")]
public class HairpinPlayedAgainTests
{
    private static List<string> Pins(string src)
    {
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src));
        return HairpinEngraver.DetectHairpins(score.MusicMarks, score.Dynamics, SectionPlays.For(score))
            .OrderBy(h => h.StartMeasureIndex).ThenBy(h => h.StartItemIndex)
            .Select(h => $"({h.StartMeasureIndex},{h.StartItemIndex})..({h.EndMeasureIndex},{h.EndItemIndex})")
            .ToList();
    }

    private static string Book(string phrases, string bars)
        => "octave absolute\ntime 4/4\npart m { clef treble }\n" + phrases
            + "section A { m { " + bars + " } }\nform { A }\nscore { staff m }\n";

    /// <summary>The phrase played twice pairs as the same bars written out do: the first
    /// hairpin ends where the second starts, the second at the f.</summary>
    [Fact]
    public void APhrasesHairpinPlayedTwiceInOnePlay_EndsAtItsNextPlaying_AsTheBarsWrittenOutDo()
    {
        var played = Pins(Book("phrase riff { c'4@cresc d' e' f' }\n", "riff | riff | c'1@f |"));
        Assert.Equal(["(0,0)..(1,0)", "(1,0)..(2,0)"], played);
        Assert.Equal(Pins(Book("", "c'4@cresc d' e' f' | c'4@cresc d' e' f' | c'1@f |")), played);
    }

    /// <summary>…with nothing after the second playing: the first still ends at the second,
    /// and the second runs to its bar's line.</summary>
    [Fact]
    public void APhrasesHairpinPlayedTwiceWithNothingAfter_TheFirstEndsAtTheSecond()
        => Assert.Equal(["(0,0)..(1,0)", "(1,0)..(2,0)"],
            Pins(Book("phrase riff { c'4@cresc d' e' f' }\n", "riff | riff | c'1 |")));

    /// <summary>A section the form plays twice is two plays: the second playing of its hairpin
    /// is not the first's end, and each runs to its own bar's line (the 2026-08-29 rule, which
    /// the change above must leave alone).</summary>
    [Fact]
    public void ASectionsHairpinPlayedInAnotherPlay_DoesNotEndTheFirstPlaying()
        => Assert.Equal(["(0,0)..(1,0)", "(4,0)..(5,0)"],
            Pins("octave absolute\ntime 4/4\npart m { clef treble }\n"
                + "section A { m { c''2@cresc c''2 | c''2 c''2 | } }\nsection B { m { d''2 d''2 | d''2 d''2 | } }\n"
                + "form { A B A \"A2\" }\nscore { staff m }\n"));

    /// <summary>A phrase played twice in a section that is itself played twice: within each
    /// play the first playing ends at the second — not at the f past it — and the second at
    /// the f; no hairpin reaches into the other play.</summary>
    [Fact]
    public void APhrasePlayedTwiceInASectionPlayedTwice_PairsWithinEachPlay()
        => Assert.Equal(["(0,0)..(1,0)", "(1,0)..(2,0)", "(5,0)..(6,0)", "(6,0)..(7,0)"],
            Pins("octave absolute\ntime 4/4\npart m { clef treble }\nphrase riff { c'4@cresc d' e' f' }\n"
                + "section A { m { riff | riff | c'1@f | } }\nsection B { m { d''2 d''2 | d''2 d''2 | } }\n"
                + "form { A B A \"A2\" }\nscore { staff m }\n"));
}
