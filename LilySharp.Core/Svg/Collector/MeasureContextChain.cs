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
using System.Collections.Immutable;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// F3a substrate (LSP_F3_QUERY_GRAPH_DESIGN.md §2 Layer 2): the
/// <c>entry_context → exit_context</c> chain for one voice.
/// </summary>
/// <remarks>
/// <para>
/// <c>exit[i] = Advance(entry[i], measures[i])</c> and <c>entry[i+1] = exit[i]</c>;
/// <c>entry[0]</c> is the score-level initial context. This is the demand-driven
/// DAG's pure <c>(entry, measure) → exit</c> function, realized as a post-pass so
/// the running state crossing barlines is captured WITHOUT a stable identity yet
/// and WITHOUT touching the collector walk.
/// </para>
/// <para>
/// Deliberately a self-contained POST-PASS in the house style of
/// <see cref="TabResolver"/> / <see cref="LyricsCollector"/> rather than an edit
/// to the collector walk or the <c>Measure</c> record: it only reads the
/// already-collected change items and produces new data consumed by nothing in
/// the render path, so the output is byte-identical. It folds the SAME change
/// items the renderer reads to track the current key/clef/time, so it is
/// faithful by construction. Later stages wire this into the per-measure
/// semantics query (S5) and add a stable, edit-surviving measure identity (S4).
/// </para>
/// </remarks>
public sealed class MeasureContextChain
{
    /// <summary>Context at the START of each measure. <c>Entry[i+1] == Exit[i]</c>.</summary>
    public ImmutableArray<MeasureContext> Entry { get; }

    /// <summary>Context at the END of each measure (after its change items apply).</summary>
    public ImmutableArray<MeasureContext> Exit { get; }

    private MeasureContextChain(ImmutableArray<MeasureContext> entry, ImmutableArray<MeasureContext> exit)
    {
        Entry = entry;
        Exit = exit;
    }

    /// <summary>
    /// Computes the chain for a voice's measures starting from <paramref name="initial"/>.
    /// </summary>
    public static MeasureContextChain Compute(IReadOnlyList<Measure> measures, MeasureContext initial)
    {
        int n = measures.Count;
        var entry = ImmutableArray.CreateBuilder<MeasureContext>(n);
        var exit = ImmutableArray.CreateBuilder<MeasureContext>(n);

        var ctx = initial;
        for (int i = 0; i < n; i++)
        {
            entry.Add(ctx);                  // entry[i] = exit[i-1] (initial for i==0)
            ctx = Advance(ctx, measures[i]); // fold this measure's change items
            exit.Add(ctx);
        }

        return new MeasureContextChain(entry.MoveToImmutable(), exit.MoveToImmutable());
    }

    /// <summary>
    /// Computes the chain for a score's primary voice, deriving the initial
    /// context from the score-level key / time / clef.
    /// </summary>
    public static MeasureContextChain Compute(Score score) =>
        Compute(score.Voice.Measures, InitialContextOf(score));

    /// <summary>
    /// The score-level starting context (key / time / clef of bar 1).
    /// <c>Score.KeySignature</c>, <c>Score.TimeSignature</c> and <c>Score.Clef</c>
    /// are all the bar-1 values: the collector preserves <c>_initialKeySharps</c>
    /// / <c>_initialClef</c> before music processing, so they are NOT the end
    /// state after mid-piece changes (provided the score was collected with its
    /// voice name, i.e. the normal render path).
    /// </summary>
    public static MeasureContext InitialContextOf(Score score) =>
        new(score.KeySignature, score.TimeSignature, MeasureCollector.ParseClefType(score.Clef));

    /// <summary>
    /// Applies a measure's mid-piece change items to the running context. Only
    /// the zero-duration Key / Clef / Time change items move the context; every
    /// other item leaves it untouched (accidentals reset at the barline and so
    /// never cross — only the key does).
    /// </summary>
    private static MeasureContext Advance(MeasureContext ctx, Measure measure)
    {
        foreach (var item in measure.Items)
        {
            ctx = item switch
            {
                KeySignatureChangeItem k => ctx with { Key = k.NewKey },
                ClefChangeItem c => ctx with { Clef = c.NewClef },
                TimeSignatureChangeItem t => ctx with { Time = t.NewTime },
                _ => ctx,
            };
        }
        return ctx;
    }
}
