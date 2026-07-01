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

using System;
using System.Collections.Immutable;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3 / S5-3a: the per-system layout reuse cache. Unit tests pin the keying
/// (exact-match reuse, scalar/content sensitivity, out-of-range fallback); the
/// end-to-end test proves that, through IncrementalCompiler, a width-preserving
/// edit recomputes only the edited system and reuses the rest — while staying
/// byte-identical to a full recompile.
/// </summary>
[Trait("Category", "Unit")]
public class SystemLayoutCacheTests
{
    private static ImmutableArray<MeasureLayout> Layout(int measureIndex) =>
        ImmutableArray.Create(new MeasureLayout(measureIndex, 0, 1, ImmutableArray<ItemLayout>.Empty));

    [Fact]
    public void ReusesOnExactMatch_RecomputesOnAnyDifference()
    {
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));

        int calls = 0;
        Func<ImmutableArray<MeasureLayout>> factory = () => { calls++; return Layout(calls); };

        // Miss, then hit on identical inputs.
        var first = cache.GetOrComputeMeasures(0, 2, true, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(1, calls);
        var second = cache.GetOrComputeMeasures(0, 2, true, false, 2.0, 0.25, factory);
        Assert.True(cache.LastWasHit);
        Assert.Equal(1, calls);
        Assert.Equal(first, second);

        // A differing scalar (isFirstSystem) is a different system -> miss.
        cache.GetOrComputeMeasures(0, 2, false, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(2, calls);

        // A changed content key in the range -> miss.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(99),
            new MeasureContentKey(3), new MeasureContentKey(4)));
        cache.GetOrComputeMeasures(0, 2, true, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(3, calls);

        // A range whose keys were NOT touched still reuses.
        cache.GetOrComputeMeasures(2, 2, false, true, 1.0, 0.25, factory); // miss (first time)
        Assert.Equal(4, calls);
        cache.GetOrComputeMeasures(2, 2, false, true, 1.0, 0.25, factory); // hit
        Assert.True(cache.LastWasHit);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void OutOfRange_FallsBackToCompute_NoCaching()
    {
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(ImmutableArray.Create(new MeasureContentKey(1), new MeasureContentKey(2)));
        int calls = 0;
        Func<ImmutableArray<MeasureLayout>> factory = () => { calls++; return Layout(0); };

        cache.GetOrComputeMeasures(0, 5, true, true, 2.0, 0.25, factory); // count exceeds keys
        Assert.False(cache.LastWasHit);
        cache.GetOrComputeMeasures(0, 5, true, true, 2.0, 0.25, factory); // still not cached
        Assert.False(cache.LastWasHit);
        Assert.Equal(2, calls);
        Assert.Equal(0, cache.Count);
    }

    // Three systems via forced `break`s; one staff.
    private const string ThreeSystems = """
        time 4/4
        key c major
        part melody
        section Main {
          melody {
            c4 d e f | g4 a b c |
            break
            c4 d e f | g4 a b c |
            break
            e4 f g a | b4 c d e |
          }
        }
        structure { Main }
        score "x" { staff melody }
        """;

    [Fact]
    public void IncrementalCompiler_ReusesUnchangedSystems_AndStaysByteIdentical()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(ThreeSystems));
        session.Render();

        var cache = session.SystemCache;
        Assert.NotNull(cache);
        Assert.Equal(3, cache!.Count); // three systems cached

        // Width-preserving edit (a staccato) on a note in the LAST system: the
        // line-break gate is unchanged, so the grouping holds and only system 3's
        // content key changes. Systems 1 and 2 must be reused (no new entries).
        int at = ThreeSystems.IndexOf("e4 f g a", StringComparison.Ordinal) + 2; // after "e4"
        string editedText = ThreeSystems.Insert(at, "@staccato");
        var incremental = session.Edit(new TextChange(new TextSpan(at, 0), "@staccato"));

        Assert.Equal(4, cache.Count); // only one new system entry => two reused

        // ...and the incremental result equals a full recompile of the edited text.
        var full = new IncrementalCompiler(SyntaxTree.Parse(editedText)).Render();
        Assert.Equal(full, incremental);
    }

    [Fact]
    public void MultiStaff_ReusesSystems_AndStaysByteIdentical()
    {
        // 03-piano is a single-voice, 2-staff grand-staff score with no grob
        // overrides, so the per-system cache engages (S5-3c) with per-measure keys
        // that combine both staves, and both the spring solve AND the skyline are
        // memoized per system. (A multi-VOICE score is deliberately excluded from
        // reuse — see MultiVoice_FallsBackToFullLayout_AndStaysByteIdentical.)
        var source = LoadFixture("showcase/03-piano");
        var session = new IncrementalCompiler(SyntaxTree.Parse(source));
        session.Render();

        var cache = session.SystemCache;
        Assert.NotNull(cache);
        Assert.True(cache!.Count >= 2); // spans multiple systems

        int before = cache.Count;

        // Width-preserving edit (leading newline = pure trivia): every system's
        // content is unchanged, so ALL multi-staff systems are reused (no new cache
        // entries). Reusing the cached skylines must stay byte-identical to a full
        // recompile (this also guards against any downstream skyline mutation).
        var incremental = session.Edit(new TextChange(new TextSpan(0, 0), "\n"));
        Assert.Equal(before, cache.Count);
        var full = new IncrementalCompiler(SyntaxTree.Parse("\n" + source)).Render();
        Assert.Equal(full, incremental);

        // A content edit still equals a full recompile (soundness over a real change).
        string edited2 = ("\n" + source).Insert(source.Length / 2 + 1, " c4");
        var incremental2 = session.Edit(new TextChange(new TextSpan(source.Length / 2 + 1, 0), " c4"));
        var full2 = new IncrementalCompiler(SyntaxTree.Parse(edited2)).Render();
        Assert.Equal(full2, incremental2);
    }

    [Fact]
    public void MultiVoice_FallsBackToFullLayout_AndStaysByteIdentical()
    {
        // grammar-tour has a staff with two simultaneous voices (voice { } voice { }).
        // The per-measure content key and the spring gate fold only each staff's
        // PRIMARY voice, so an edit to a SECONDARY voice would not be localized by
        // them — reuse could emit stale voice-2 geometry. The incremental compiler
        // must therefore disable reuse for any polyphonic score and fall back to a
        // full layout every edit, which is byte-identical with a full recompile.
        var source = LoadFixture("showcase/grammar-tour");
        var session = new IncrementalCompiler(SyntaxTree.Parse(source));
        session.Render();

        // No per-system cache is installed for a multi-voice score.
        Assert.Null(session.SystemCache);

        // Editing the SECOND voice's first pitch (b' -> c') must stay byte-identical
        // to a full recompile, and must NOT reuse the cached layout.
        int at = source.IndexOf("voice { b'2", StringComparison.Ordinal) + "voice { ".Length;
        Assert.True(at > "voice { ".Length, "expected a 'voice { b'2' second voice in the fixture");
        string edited = source.Remove(at, 1).Insert(at, "c");
        var incremental = session.Edit(new TextChange(new TextSpan(at, 1), "c"));

        Assert.False(session.LastEditReusedLayout);
        Assert.Null(session.SystemCache);
        var full = new IncrementalCompiler(SyntaxTree.Parse(edited)).Render();
        Assert.Equal(full, incremental);
    }

    private static string LoadFixture(string rel)
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (System.IO.Directory.Exists(candidate))
                return System.IO.File.ReadAllText(
                    System.IO.Path.Combine(candidate, rel.Replace('/', System.IO.Path.DirectorySeparatorChar) + ".lys"))
                    .Replace("\r\n", "\n");
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }
}
