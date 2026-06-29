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
            .Collect(tree, voiceName, spec?.LocalStructure);
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
        phrase p {
          c4 d e f |
          {{bar2}} |
          d4 e f g |
          a4 b c d |
        }
        section Main { melody { $p } }
        structure { Main }
        score "x" { staff melody }
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
            phrase p {
              r1 |
              c4 d e f |
              c4 d e f |
              r1 |
            }
            section Main { melody { $p } }
            structure { Main }
            score "x" { staff melody }
            """;
        var keys = Keys(source);
        Assert.Equal(4, keys.Length);
        Assert.Equal(keys[1], keys[2]);    // same content -> same key
        Assert.NotEqual(keys[0], keys[1]); // different content -> different key
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
