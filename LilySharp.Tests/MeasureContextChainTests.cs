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
    // Collect the way the renderer does (SvgGenerator.BuildLayout): resolve the
    // render spec and pass the staff's voice name, so Phase 1.5 reads the part
    // clef into _initialClef and Score.Clef is the faithful bar-1 clef. Calling
    // Collect(tree, null) skips that and leaves the clef wrong — the bug that
    // made S2 defer clef.
    private static Score Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree);
        string? voiceName = spec is { Items.Length: 1 } && spec.Items[0] is SingleStaffSpec single
            ? single.Staff.VoiceName
            : null;
        return new MeasureCollector { ScoreTranspose = spec?.ScoreTranspose }
            .Collect(tree, voiceName, spec?.Form);
    }

    // Single section, exact measure indices, one change of each kind:
    //   m0  c4 d e f                      -> (C, 4/4, Treble)
    //   m1  key g major; c d e f          -> exit key becomes G (1 sharp)
    //   m2  clef bass; c d e f            -> exit clef becomes Bass
    //   m3  time 3/4; c d e               -> exit time becomes 3/4
    private const string Changes = """
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel {
          c4 d e f |
          key g major c4 d e f |
          clef bass c4 d e f |
          time 3/4 c4 d e |
        }
        section Main { melody { mel } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void InlineChanges_KeyClefTime_CarryForwardAndUpdateAtChangePoints()
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

        // Entry of bar 1 is the score-level initial (faithful clef = Treble).
        Assert.Equal(new MeasureContext(cMaj, four4, ClefType.Treble), chain.Entry[0]);

        // Exit after each measure folds that measure's change items; everything
        // else carries forward unchanged.
        Assert.Equal(new MeasureContext(cMaj, four4, ClefType.Treble), chain.Exit[0]);
        Assert.Equal(new MeasureContext(gMaj, four4, ClefType.Treble), chain.Exit[1]); // +key
        Assert.Equal(new MeasureContext(gMaj, four4, ClefType.Bass), chain.Exit[2]);   // +clef
        Assert.Equal(new MeasureContext(gMaj, three4, ClefType.Bass), chain.Exit[3]);  // +time
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
            phrase mel { c4 d e f | g4 a b c | d4 e f g | }
            section Main { melody { mel } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var score = Collect(source);
        var chain = MeasureContextChain.Compute(score);
        var initial = MeasureContextChain.InitialContextOf(score);

        Assert.NotEmpty(chain.Entry);
        Assert.All(chain.Entry, c => Assert.Equal(initial, c));
        Assert.All(chain.Exit, c => Assert.Equal(initial, c));
    }

    [Fact]
    public void PartWithoutItsOwnClef_StartsInTheDefault_NotInAMidPieceChange()
    {
        // A `clef` written INSIDE the music is a mid-piece change engraved from its own
        // position — it must NOT also become the file default. CollectDefinitions folded
        // it into _meta.Clef with no IsInsideMusicContent guard (its key / octave / partial
        // neighbours all have one), so a part declaring no clef of its own started in the
        // CHANGED clef: bass glyph at the system head, and — since Phase 1.5 derives the
        // default octave from _meta.Clef too — a bare `c` landing at C3 instead of C4.
        // Every existing fixture declares a part clef, which masked this completely.
        const string source = """
            time 4/4
            key c major
            part melody
            section Main { melody { c4 d e f | clef bass g,4 a, b, c | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var score = Collect(source);

        // Bar 1 is still treble, and a bare `c` is still C4 (position -6 from the
        // middle line), not the bass-clef default octave's C3 (-1).
        Assert.Equal("treble", score.Clef);
        Assert.Equal(ClefType.Treble, MeasureContextChain.InitialContextOf(score).Clef);
        var firstNote = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);
        Assert.Equal(-6, firstNote.StaffPosition);

        // ...and the change itself still lands, from its own bar onwards.
        var chain = MeasureContextChain.Compute(score);
        Assert.Equal(ClefType.Treble, chain.Entry[0].Clef);
        Assert.Equal(ClefType.Bass, chain.Exit[1].Clef);
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
        var a = new MeasureContext(new KeySignature(1), new TimeSignature(4, 4), ClefType.Treble);
        var b = new MeasureContext(new KeySignature(1), new TimeSignature(4, 4), ClefType.Treble);
        var c = new MeasureContext(new KeySignature(2), new TimeSignature(4, 4), ClefType.Treble);

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
