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
        var first = cache.GetOrCompute(0, 2, true, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(1, calls);
        var second = cache.GetOrCompute(0, 2, true, false, 2.0, 0.25, factory);
        Assert.True(cache.LastWasHit);
        Assert.Equal(1, calls);
        Assert.Equal(first, second);

        // A differing scalar (isFirstSystem) is a different system -> miss.
        cache.GetOrCompute(0, 2, false, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(2, calls);

        // A changed content key in the range -> miss.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(99),
            new MeasureContentKey(3), new MeasureContentKey(4)));
        cache.GetOrCompute(0, 2, true, false, 2.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(3, calls);

        // A range whose keys were NOT touched still reuses.
        cache.GetOrCompute(2, 2, false, true, 1.0, 0.25, factory); // miss (first time)
        Assert.Equal(4, calls);
        cache.GetOrCompute(2, 2, false, true, 1.0, 0.25, factory); // hit
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

        cache.GetOrCompute(0, 5, true, true, 2.0, 0.25, factory); // count exceeds keys
        Assert.False(cache.LastWasHit);
        cache.GetOrCompute(0, 5, true, true, 2.0, 0.25, factory); // still not cached
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
}
