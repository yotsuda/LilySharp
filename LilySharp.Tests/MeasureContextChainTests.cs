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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3a substrate tests: the per-voice <c>entry_context → exit_context</c> chain
/// (<see cref="MeasureContextChain"/>) over the Timing/Clef/Key backbone. These
/// lock the chain's faithfulness (key/clef/time carry across barlines and update
/// at change points) and the value-equality the query DAG's early-cutoff relies
/// on. The chain is a post-pass that touches nothing in the render path, so the
/// rendered output is unchanged — that is asserted separately by the snapshot
/// suite; here we verify the new data itself.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureContextChainTests
{
    private static Score Collect(string source) =>
        new MeasureCollector().Collect(SyntaxTree.Parse(source), null);

    // Single section, exact measure indices. The mid-measure `clef bass` is
    // present on purpose: clef is NOT in the S2 context, so Advance must IGNORE
    // it (m2's exit stays key=G, time=4/4).
    //   m0  c4 d e f                      -> (C, 4/4)
    //   m1  key g major; c d e f          -> exit key becomes G (1 sharp)
    //   m2  clef bass; c d e f            -> unchanged (clef not carried)
    //   m3  time 3/4; c d e               -> exit time becomes 3/4
    private const string Changes = """
        time 4/4
        key c major
        part melody { clef treble }
        phrase p {
          c4 d e f |
          key g major c4 d e f |
          clef bass c4 d e f |
          time 3/4 c4 d e |
        }
        section Main { melody { $p } }
        structure { Main }
        score "x" { staff melody }
        """;

    [Fact]
    public void InlineChanges_KeyAndTime_CarryForwardAndUpdateAtChangePoints()
    {
        var score = Collect(Changes);
        var chain = MeasureContextChain.Compute(score);

        var cMaj = new KeySignature(0);
        var gMaj = new KeySignature(1);
        var four4 = new TimeSignature(4, 4);
        var three4 = new TimeSignature(3, 4);

        Assert.Equal(4, score.Voice.Measures.Length);
        Assert.Equal(4, chain.Entry.Length);
        Assert.Equal(4, chain.Exit.Length);

        // Entry of bar 1 is the score-level initial.
        Assert.Equal(new MeasureContext(cMaj, four4), chain.Entry[0]);

        // Exit after each measure folds that measure's change items; everything
        // else carries forward unchanged. The mid `clef bass` in m2 is ignored.
        Assert.Equal(new MeasureContext(cMaj, four4), chain.Exit[0]);
        Assert.Equal(new MeasureContext(gMaj, four4), chain.Exit[1]);  // +key
        Assert.Equal(new MeasureContext(gMaj, four4), chain.Exit[2]);  // clef change ignored
        Assert.Equal(new MeasureContext(gMaj, three4), chain.Exit[3]); // +time
    }

    [Fact]
    public void Chain_IsContinuous_EntryEqualsPriorExit()
    {
        var chain = MeasureContextChain.Compute(Collect(Changes));
        for (int i = 1; i < chain.Entry.Length; i++)
            Assert.Equal(chain.Exit[i - 1], chain.Entry[i]);
    }

    [Fact]
    public void NoChangePiece_AllContextsEqualTheInitial()
    {
        // A piece with no mid-piece key/clef/time change: every context equals
        // the score-level initial.
        const string source = """
            time 4/4
            key c major
            part melody { clef treble }
            phrase p { c4 d e f | g4 a b c | d4 e f g | }
            section Main { melody { $p } }
            structure { Main }
            score "x" { staff melody }
            """;
        var score = Collect(source);
        var chain = MeasureContextChain.Compute(score);
        var initial = MeasureContextChain.InitialContextOf(score);

        Assert.NotEmpty(chain.Entry);
        Assert.All(chain.Entry, c => Assert.Equal(initial, c));
        Assert.All(chain.Exit, c => Assert.Equal(initial, c));
    }

    [Theory]
    // Real single-staff fixtures: Compute must run cleanly and the chain stay
    // continuous and aligned to the measure list, whatever changes occur inside.
    [InlineData("test/notes")]
    [InlineData("test/keysig-change")]
    [InlineData("test/clef-change")]
    [InlineData("test/timesig-change")]
    [InlineData("test/mixed-meters")]
    [InlineData("test/ties-slurs")]
    public void Fixtures_ChainAlignsToMeasuresAndStaysContinuous(string fixture)
    {
        var path = System.IO.Path.Combine(FixturesDir, fixture + ".lys");
        var source = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
        var score = Collect(source);
        var chain = MeasureContextChain.Compute(score);

        Assert.Equal(score.Voice.Measures.Length, chain.Entry.Length);
        Assert.Equal(score.Voice.Measures.Length, chain.Exit.Length);
        if (chain.Entry.Length > 0)
            Assert.Equal(MeasureContextChain.InitialContextOf(score), chain.Entry[0]);
        for (int i = 1; i < chain.Entry.Length; i++)
            Assert.Equal(chain.Exit[i - 1], chain.Entry[i]);
    }

    [Fact]
    public void MeasureContext_HasValueEquality_ForEarlyCutoff()
    {
        var a = new MeasureContext(new KeySignature(1), new TimeSignature(4, 4));
        var b = new MeasureContext(new KeySignature(1), new TimeSignature(4, 4));
        var c = new MeasureContext(new KeySignature(2), new TimeSignature(4, 4));

        Assert.Equal(a, b);          // same state -> equal -> cascade can stop
        Assert.True(a == b);
        Assert.NotEqual(a, c);       // changed key -> not equal -> cascade continues
        Assert.True(a != c);
    }

    private static readonly string FixturesDir = FindFixturesDir();

    private static string FindFixturesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }
}
