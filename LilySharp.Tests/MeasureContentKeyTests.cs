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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3 / S5a substrate tests: the stable, position-independent per-measure
/// <see cref="MeasureContentKey"/> (the Layer-1 "measure identity" the design
/// assumed it got for free). These lock the headline property the incremental
/// engine relies on — <b>edit-locality</b>: a measure that does not overlap an
/// edit keeps its key even though its source position shifts — plus determinism
/// and position-independence. The key is a pure utility consumed by nothing in
/// the render path yet, so rendered output is unchanged (asserted by the snapshot
/// suite); here we verify the new data itself.
/// </summary>
[Trait("Category", "Unit")]
public class MeasureContentKeyTests
{
    // Collect the way the renderer does (cf. MeasureContextChainTests): resolve
    // the render spec and pass the staff's voice name, so the resolved model is
    // the same one the renderer lays out.
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

    // Intrinsic keys (items + structural fields only).
    private static ImmutableArray<MeasureContentKey> Keys(string source) =>
        MeasureContentKey.Compute(Collect(source).Voice.Measures);

    // Complete render-input keys (intrinsic + side-tables + entry context).
    private static ImmutableArray<MeasureContentKey> CompleteKeys(string source) =>
        MeasureContentKey.Compute(Collect(source));

    // Four measures, each with a distinct leading note so we can target one.
    private static string FourBars(string bar2 = "g4 a b c") => $$"""
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel {
          c4 d e f |
          {{bar2}} |
          d4 e f g |
          a4 b c d |
        }
        section Main { melody { mel } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void Compute_IsDeterministic_SameSourceSameKeys()
    {
        Assert.Equal(Keys(FourBars()), Keys(FourBars()));
    }

    [Fact]
    public void KeyVector_AlignsToMeasureList()
    {
        var measures = Collect(FourBars()).Voice.Measures;
        var keys = MeasureContentKey.Compute(measures);
        Assert.Equal(measures.Length, keys.Length);
        Assert.Equal(4, keys.Length);
    }

    [Fact]
    public void IdenticalMeasures_AtDifferentPositions_ShareKey()
    {
        // Two measures with identical resolved content must share a key: the key
        // depends on content, not position.
        const string source = """
            time 4/4
            key c major
            part melody { clef treble }
            phrase mel {
              r1 |
              c4 d e f |
              c4 d e f |
              r1 |
            }
            section Main { melody { mel } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var keys = Keys(source);
        Assert.Equal(4, keys.Length);
        Assert.Equal(keys[1], keys[2]);    // same content -> same key
        Assert.NotEqual(keys[0], keys[1]); // different content -> different key
    }

    private static string BeamedBars(params string[] bars) =>
        "octave absolute\ntime 4/4\nkey c major\npart melody { clef treble }\n"
        + "section Main { melody {\n" + string.Join("\n", bars) + "\n} }\n"
        + "form main { Main }\nscore main \"x\" { staff melody }\n";

    /// <summary>
    /// A beamed bar deleted (or added) EARLIER leaves a beamed measure's key alone. BeamId
    /// is numbered by the bake in score order, so before session 330 it moved every later
    /// item's hash — a one-bar deletion made every later key miss (springs and systems
    /// alike), while the same book with the bar ADDED as quarters kept them (no new group,
    /// no renumbering). Position-independence has to hold in both directions.
    /// </summary>
    [Fact]
    public void BeamedMeasures_AfterADeletedBeamedBar_KeepTheirKeys()
    {
        // Bar 0 stays in both books: the section's first measure carries the section
        // label, so it is never the same measure as a later one.
        const string bar = "c8 d e f g a b c' |";
        var four = CompleteKeys(BeamedBars(bar, "d8 e f g a b c' d' |", bar, "e8 f g a b c' d' e' |"));
        var three = CompleteKeys(BeamedBars(bar, bar, "e8 f g a b c' d' e' |"));
        Assert.Equal(4, four.Length);
        Assert.Equal(3, three.Length);
        Assert.Equal(four[2], three[1]);
        Assert.Equal(four[3], three[2]);
        // ...and the grouping is still in the key: the same pitches unbeamed differ.
        Assert.NotEqual(four[2], CompleteKeys(BeamedBars(bar, "c4 d e f g a b c' |"))[1]);
    }

    /// <summary>
    /// The grouping the key folds instead is RELATIONAL, and it reaches across the bar
    /// line: a manual beam continuing into the next measure changes BOTH measures' keys
    /// against the same music beamed within each bar (the continuation changes both
    /// bars' stems and beam — BeamContinuationTests), while the numbers themselves stay
    /// out (the two bars of one book agree with the same two bars placed later).
    /// </summary>
    [Fact]
    public void ABeamCrossingTheBarLine_EntersBothKeys_ByRelationNotByNumber()
    {
        // A leading rest bar keeps the compared measures off the section's labelled first.
        var crossing = Keys(BeamedBars("r1 |", "c8[ d e f | g8 a b c8] |"));
        var separate = Keys(BeamedBars("r1 |", "c8 d e f | g8 a b c8 |"));
        Assert.NotEqual(separate[1], crossing[1]);
        Assert.NotEqual(separate[2], crossing[2]);

        var later = Keys(BeamedBars("r1 |", "c8 d e f g a b c' |", "c8[ d e f | g8 a b c8] |"));
        Assert.Equal(crossing[1], later[2]);
        Assert.Equal(crossing[2], later[3]);
    }

    [Fact]
    public void Edit_PitchChange_ChangesOnlyThatKey()
    {
        // Change measure 1's first note (g -> a). Measures 2 and 3 shift forward
        // in the source, yet only measure 1's key changes — the others are
        // unaffected (edit-locality) AND unmoved-by-value despite their shift
        // (position-independence).
        var before = Keys(FourBars("g4 a b c"));
        var after = Keys(FourBars("a4 a b c"));

        Assert.Equal(4, before.Length);
        Assert.Equal(4, after.Length);
        Assert.Equal(before[0], after[0]);    // before the edit: untouched
        Assert.NotEqual(before[1], after[1]); // the edited measure: changed
        Assert.Equal(before[2], after[2]);    // after the edit: shifted but identical
        Assert.Equal(before[3], after[3]);
    }

    [Fact]
    public void Edit_RhythmChange_ChangesOnlyThatKey()
    {
        // A duration change (the kind that DOES move natural width) is still local
        // to its measure as far as identity goes.
        var before = Keys(FourBars("g4 a b c"));
        var after = Keys(FourBars("g8 a b c d"));

        Assert.Equal(before[0], after[0]);
        Assert.NotEqual(before[1], after[1]);
        Assert.Equal(before[2], after[2]);
        Assert.Equal(before[3], after[3]);
    }

    [Fact]
    public void CompleteKey_ReflectsArticulations_AndStaysLocal()
    {
        // A staccato is an ArticulationItem on Score.Articulations, NOT on the
        // NoteItem — so the INTRINSIC key does not see it (S5a's documented gap)...
        var beforeIntrinsic = Keys(FourBars("g4 a b c"));
        var afterIntrinsic = Keys(FourBars("g4@staccato a b c"));
        Assert.Equal(beforeIntrinsic[1], afterIntrinsic[1]);

        // ...but the COMPLETE key folds side-tables by MeasureIndex, so it changes
        // exactly the edited measure's key, and only that one (edit-locality holds
        // through the side-table fold too).
        var before = CompleteKeys(FourBars("g4 a b c"));
        var after = CompleteKeys(FourBars("g4@staccato a b c"));
        Assert.Equal(before[0], after[0]);
        Assert.NotEqual(before[1], after[1]);
        Assert.Equal(before[2], after[2]);
        Assert.Equal(before[3], after[3]);
    }

    [Fact]
    public void CompleteKey_IsDeterministic_AndAlignsToMeasures()
    {
        var a = CompleteKeys(FourBars());
        var b = CompleteKeys(FourBars());
        Assert.Equal(a, b);
        Assert.Equal(Collect(FourBars()).Voice.Measures.Length, a.Length);
    }

    [Fact]
    public void CompleteKey_PitchEdit_StaysLocal()
    {
        // The complete key keeps the intrinsic edit-locality: a pitch change in
        // measure 1 changes only measure 1's complete key.
        var before = CompleteKeys(FourBars("g4 a b c"));
        var after = CompleteKeys(FourBars("a4 a b c"));
        Assert.Equal(before[0], after[0]);
        Assert.NotEqual(before[1], after[1]);
        Assert.Equal(before[2], after[2]);
        Assert.Equal(before[3], after[3]);
    }

    // The complete MULTI-STAFF key — the one the incremental engine actually uses
    // (IncrementalCompiler feeds Compute(MultiStaffScore) to SystemLayoutCache).
    private static ImmutableArray<MeasureContentKey> MultiStaffKeys(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var spec = RenderSpecParser.FindFirst(tree)!;
        return MeasureContentKey.Compute(new MeasureCollector().CollectMultiStaff(tree, spec));
    }

    [Fact]
    public void MultiStaffKey_FoldsTheNextMeasuresOpeningClef_IntoThePrecedingMeasure()
    {
        // A clef change opening measure N is engraved BEFORE the bar line N shares with
        // measure N-1, so it widens N-1's closing spring
        // (SpacingRules.BoundaryClefAllowance). Like multi-measure-rest run membership,
        // that width is decided by the NEIGHBOURING measure and so cannot be recovered
        // from N-1's own intrinsic hash — it has to be folded in explicitly. Without the
        // fold, a system ENDING at N-1 keeps its whole key slice when N gains a clef, and
        // the per-system cache hands back a layout with no room reserved for that clef.
        var before = MultiStaffKeys(FourBars("g4 a b c"));
        var after = MultiStaffKeys(FourBars("clef bass g4 a b c"));

        Assert.Equal(4, before.Length);
        Assert.Equal(4, after.Length);
        Assert.NotEqual(before[0], after[0]); // the cross-measure dependency
        Assert.NotEqual(before[1], after[1]); // the measure that gained the clef
    }

    [Theory]
    [InlineData("test/notes")]
    [InlineData("test/keysig-change")]
    [InlineData("test/clef-change")]
    [InlineData("test/ties-slurs")]
    public void Fixtures_KeyVectorAlignsToMeasures(string fixture)
    {
        var path = System.IO.Path.Combine(FixturesDir, fixture + ".lys");
        var source = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
        var measures = Collect(source).Voice.Measures;
        var keys = MeasureContentKey.Compute(measures);
        Assert.Equal(measures.Length, keys.Length);
    }

    [Fact]
    public void MeasureContentKey_HasValueEquality()
    {
        var a = new MeasureContentKey(12345);
        var b = new MeasureContentKey(12345);
        var c = new MeasureContentKey(54321);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
    }

    private static readonly string FixturesDir = FindFixturesDir();

    private static string FindFixturesDir()
    {
        var dir = System.AppContext.BaseDirectory;
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
