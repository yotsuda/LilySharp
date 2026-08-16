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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Which music a <c>transpose</c> applies to, for each of the three places it can be
/// written: a part header, the top level, and a single score.
/// </summary>
/// <remarks>
/// The per-score spelling used to be read TWICE — once by
/// <see cref="PartTranspose.ReadScoreDefault"/>, whose "not inside a part" filter also
/// matched it, and once as the score's own <c>RenderSpec.ScoreTranspose</c>, which the
/// collector composes on top. One line produced three different answers for one
/// construct: `transpose d` moved c by a major third where the part-header spelling
/// moved it by a second, and every OTHER score in the file was transposed too, unasked.
/// Measured 2026-08-16 (第182) on the page as well as in the trace: the untransposed
/// score of a two-score book engraved in D major, two sharps and all.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TransposeScopeTests
{
    private const string Music =
        """
        time 4/4

        part m { clef treble }

        section Main {
          m { c4 d e f | }
        }

        form main { ~Main }
        """;

    // The same four notes, moved by the same interval, written three ways.
    private const string OnThePart =
        """
        time 4/4

        part m { clef treble transpose d }

        section Main {
          m { c4 d e f | }
        }

        form main { ~Main }

        score main { staff m }
        """;

    private const string OnTheScore = Music + "\n\nscore main transpose d { staff m }\n";

    private const string OnTheFile =
        """
        time 4/4
        transpose d

        part m { clef treble }

        section Main {
          m { c4 d e f | }
        }

        form main { ~Main }

        score main { staff m }
        """;

    private const string Untransposed = Music + "\n\nscore main { staff m }\n";

    // A score that asks for nothing, sitting before one that asks for a transpose.
    private const string PlainThenTransposed =
        Music + "\n\nscore main \"plain\" { staff m }\nscore main \"up\" transpose d { staff m }\n";

    private static List<string> Pitches(string source)
    {
        var trace = ResolvedPitches.ForFile(SyntaxTree.Parse(source));
        Assert.NotNull(trace);
        return trace!.Select(e => e.Pitch).ToList();
    }

    [Fact]
    public void TheThreeSpellings_MoveTheMusicByTheSameInterval()
    {
        // An identity trio, which is stronger than any of the three lists on its own:
        // whatever `transpose d` means, it has to mean it in all three places.
        Assert.Equal(Pitches(OnThePart), Pitches(OnTheScore));
        Assert.Equal(Pitches(OnThePart), Pitches(OnTheFile));
    }

    [Fact]
    public void AndThatIntervalIsASecond()
    {
        // Naming it once, because an identity trio would also be satisfied by three
        // equally wrong answers. `\transpose c d` is a major second, which
        // `test/transpose-down.lys` and `test/transpose-score.lys` both document
        // against LilyPond.
        Assert.Equal(new[] { "C4", "D4", "E4", "F4" }, Pitches(Untransposed));
        Assert.Equal(new[] { "D4", "E4", "F#4", "G4" }, Pitches(OnThePart));
    }

    [Fact]
    public void AScoresTranspose_DoesNotReachTheScoreBesideIt()
    {
        // The first score asks for nothing and must get nothing, however loudly the
        // second one asks. Collected per score rather than folded, so this is about
        // that score's own reading and not about which one the fold happens to keep.
        var tree = SyntaxTree.Parse(PlainThenTransposed);
        var specs = RenderSpecParser.FindAll(tree);
        Assert.Equal(2, specs.Count);

        var plain = SemanticValidation.TryCollect(tree, specs[0]);
        Assert.Equal(
            new[] { "C4", "D4", "E4", "F4" },
            plain!.PitchTrace.Select(e => e.Pitch));

        var up = SemanticValidation.TryCollect(tree, specs[1]);
        Assert.Equal(
            new[] { "D4", "E4", "F#4", "G4" },
            up!.PitchTrace.Select(e => e.Pitch));
    }

    [Fact]
    public void ATopLevelTranspose_StillReachesEveryPart()
    {
        // The guard narrows what counts as the file's default, so the thing it must NOT
        // break is the spelling that legitimately is one. `test/transpose-score.lys` is
        // the only book in the tree that writes it.
        Assert.Equal(Pitches(OnThePart), Pitches(OnTheFile));
        Assert.NotEqual(Pitches(Untransposed), Pitches(OnTheFile));
    }
}
