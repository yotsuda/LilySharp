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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
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

    /// <summary>
    /// A system's PER-STAFF skylines are reused on an exact key match and recomputed when
    /// the content under them changes.
    /// </summary>
    /// <remarks>
    /// These went from one list per SCORE to one per SYSTEM when the placement did
    /// (2026-07-27), which is what made them worth memoising: without this, a one-note edit
    /// on a fifty-system score rebuilt all fifty, measured at ~100 ms of an edit's ~500 ms.
    /// With it, only the edited system's list is rebuilt. ⚠️ This test is the reason a
    /// future reader cannot quietly drop the memo — the fuzz tests would stay green,
    /// because dropping it costs time and not correctness.
    /// </remarks>
    [Fact]
    public void StaffSkylines_ReuseOnExactMatch_RecomputeWhenTheContentChanges()
    {
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));

        int calls = 0;
        Func<MultiStaffLayouter.StaffSkylineSet> factory = () =>
        {
            calls++;
            return new MultiStaffLayouter.StaffSkylineSet(
                new List<(VerticalSkyline Up, VerticalSkyline Down)>(),
                new List<MultiStaffLayouter.StaffInsideSpanners>(),
                new List<(VerticalSkyline Up, VerticalSkyline Down)>(),
                new List<System.Collections.Immutable.ImmutableArray<PedalEngraver.SolvedPedalLine>>(),
                new List<System.Collections.Immutable.ImmutableArray<PedalEngraver.SolvedPedalRow>>(),
                new List<System.Collections.Immutable.ImmutableArray<BeamLayout>>());
        };

        var first = cache.GetOrComputeStaffSkylines(0, 2, true, false, 2.0, 0.25, factory);
        Assert.Equal(1, calls);

        // Same system, same content -> the same list object, not a rebuild.
        var second = cache.GetOrComputeStaffSkylines(0, 2, true, false, 2.0, 0.25, factory);
        Assert.Equal(1, calls);
        Assert.Same(first.Skylines, second.Skylines);

        // A different system in the same score -> its own entry.
        cache.GetOrComputeStaffSkylines(2, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(2, calls);

        // Content under the first system changed -> it must not be reused.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(99),
            new MeasureContentKey(3), new MeasureContentKey(4)));
        cache.GetOrComputeStaffSkylines(0, 2, true, false, 2.0, 0.25, factory);
        Assert.Equal(3, calls);
    }

    /// <summary>One laid-out beam filed under <paramref name="measureIndex"/> — the stamp
    /// this test is about; the geometry is arbitrary.</summary>
    private static BeamLayout BeamAt(int measureIndex)
    {
        var note = new NoteItem(0, Fraction.Eighth, 0, null, false, 0);
        var members = ImmutableArray.Create(
            new BeamMember(note, 1, 0, 1, 0, 0, memberStemUp: true),
            new BeamMember(note, 1, 1, 0, 0, 1, memberStemUp: true));
        return new BeamLayout(
            new BeamGroup(members, measureIndex, 0, stemUp: true),
            leftY: 0, rightY: 0, leftX: 0, rightX: 1, leftStemX: 0, rightStemX: 1,
            ImmutableArray.Create(0.0, 1.0), staffIndex: 0, systemIndex: 0);
    }

    /// <summary>
    /// The BEAMS the room carries since session 414 are re-stamped on a shifted hit, exactly
    /// like the slurs and ties beside them: an entry found under other measure numbers is the
    /// same geometry under new ones, and a beam names its measure
    /// (<c>BeamGroup.MeasureIndex</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE NET FOR WHAT THE ROOM GAINED. Before the beams rode here the system
    /// silhouette laid them out itself on every miss, so nothing in this store carried a
    /// beam's measure stamp. A shift that forgot them would hand the silhouette a beam filed
    /// under the wrong bar, while the DRAWN beams — the annotation pass's own — stayed right,
    /// so no rendering net would see it.
    /// </remarks>
    [Fact]
    public void StaffSkylines_ShiftedHit_ReStampsTheBeamsRidingInTheRoom()
    {
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));

        int calls = 0;
        Func<MultiStaffLayouter.StaffSkylineSet> factory = () =>
        {
            calls++;
            return new MultiStaffLayouter.StaffSkylineSet(
                new List<(VerticalSkyline Up, VerticalSkyline Down)>(),
                new List<MultiStaffLayouter.StaffInsideSpanners>(),
                new List<(VerticalSkyline Up, VerticalSkyline Down)>(),
                new List<ImmutableArray<PedalEngraver.SolvedPedalLine>>(),
                new List<ImmutableArray<PedalEngraver.SolvedPedalRow>>(),
                new List<ImmutableArray<BeamLayout>> { ImmutableArray.Create(BeamAt(2)) });
        };

        var stored = cache.GetOrComputeStaffSkylines(2, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(1, calls);
        Assert.Equal(2, stored.Beams[0][0].Group.MeasureIndex);

        // A bar inserted at index 1: the same content slice now starts at 3, not 2.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(99), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));
        var shifted = cache.GetOrComputeStaffSkylines(3, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(1, calls);                                    // served, not recomputed
        Assert.Equal(3, shifted.Beams[0][0].Group.MeasureIndex);   // ...and re-stamped
        Assert.Equal(2, stored.Beams[0][0].Group.MeasureIndex);    // the stored value untouched
    }

    /// <summary>
    /// The system silhouette's edge-staff beams come OUT OF the staff-skyline room, not from
    /// a second run of the quanter (session 414).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FALLBACK IS SILENT, which is the whole reason this net exists: a change that
    /// stopped filling <c>StaffSkylineSet.Beams</c> would draw exactly the same picture and
    /// keep every other net green, while <c>LayoutBeams</c> ran twice for every system.
    /// MEASURED at 4.31% of a keystroke (owner's corpus, 231 books × 8 forward keystrokes,
    /// Release, allocation bytes) — EXACTLY 50.0% of the 6,528 calls a run made repeated a
    /// (staff, system, first, length) the same keystroke had already laid out, and all 3,264
    /// repeats were value-identical to the first answer.
    /// </remarks>
    [Fact]
    public void SystemSilhouette_TakesItsEdgeBeamsFromTheStaffSkylineRoom()
    {
        string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c8[ d8] e8[ f8] g8[ a8] b8[ c'8] | c8[ d8] e8[ f8] g8[ a8] b8[ c'8] | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var score = new MeasureCollector().Collect(SyntaxTree.Parse(src), "melody");
        var engine = new LayoutEngine();
        engine.Layout(score);

        Assert.True(engine.EdgeBeamSource.FromRoom > 0,
            "the silhouette asked for edge beams at all — a zero means this net stopped watching");
        Assert.Equal(0, engine.EdgeBeamSource.LaidOut);
    }

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

    /// <summary>
    /// A system found under OTHER measure numbers — the same content slice, the same edge
    /// flags and scalars, only firstMeasureIndex differs, as after a bar inserted before it
    /// — is served re-stamped instead of recomputed: the value's measure numbers move by
    /// the difference, nothing is computed, and the shifted result is then an EXACT hit
    /// under its new numbers (the same instances, for the reference-keyed memos downstream).
    /// </summary>
    [Fact]
    public void ShiftedHit_ReStampsTheMeasureNumbers_AndIsExactAfterwards()
    {
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));

        int calls = 0;
        Func<ImmutableArray<MeasureLayout>> factory = () =>
        {
            calls++;
            return ImmutableArray.Create(
                new MeasureLayout(2, 0, 1, ImmutableArray<ItemLayout>.Empty),
                new MeasureLayout(3, 1, 1, ImmutableArray<ItemLayout>.Empty));
        };
        var stored = cache.GetOrComputeMeasures(2, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(1, calls);
        Assert.Equal(new SystemLayoutCache.MemoCounters(0, 0, 1),
            cache.PassCounters(SystemLayoutCache.Store.Measures));

        // A bar inserted at index 1: the slice (3, 4) now starts at 3, not 2.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(1), new MeasureContentKey(99), new MeasureContentKey(2),
            new MeasureContentKey(3), new MeasureContentKey(4)));
        var shifted = cache.GetOrComputeMeasures(3, 2, false, true, 1.0, 0.25, factory);
        Assert.True(cache.LastWasHit);
        Assert.Equal(1, calls);
        Assert.Equal(new SystemLayoutCache.MemoCounters(0, 1, 0),
            cache.PassCounters(SystemLayoutCache.Store.Measures));
        Assert.Equal(new[] { 3, 4 }, shifted.Select(m => m.MeasureIndex));
        Assert.Equal(stored.Select(m => (m.X, m.Width)), shifted.Select(m => (m.X, m.Width)));

        // ...and again under the new numbers: exact, the same instances.
        var again = cache.GetOrComputeMeasures(3, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(1, calls);
        Assert.Equal(new SystemLayoutCache.MemoCounters(1, 1, 0),
            cache.PassCounters(SystemLayoutCache.Store.Measures));
        Assert.Same(shifted[0], again[0]);

        // A bar DELETED before it (the slice moves the other way) is the mirror.
        cache.SetContentKeys(ImmutableArray.Create(
            new MeasureContentKey(2), new MeasureContentKey(3), new MeasureContentKey(4)));
        var back = cache.GetOrComputeMeasures(1, 2, false, true, 1.0, 0.25, factory);
        Assert.Equal(1, calls);
        Assert.Equal(new[] { 1, 2 }, back.Select(m => m.MeasureIndex));

        // The shift is a re-stamp of the SAME computation, never a different one: a
        // differing edge flag under the new numbers is still a miss.
        cache.GetOrComputeMeasures(1, 2, false, false, 1.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(2, calls);
    }

    /// <summary>
    /// A system reads the measures around it — the previous bar's end (a `:|:` hands the
    /// line a `.|:` to draw) and the next bar's head — and the content slice cannot see
    /// either. Session 595 found the first render of a corpus book laying out one system by
    /// shifting another with the same slice but a different bar line before it, so every
    /// store matches on the neighbours' edge keys too (SystemLayoutCache.ContextOf).
    /// </summary>
    [Fact]
    public void TheSameSliceBehindADifferentBarLine_IsNotTheSameSystem()
    {
        var keys = ImmutableArray.Create(
            new MeasureContentKey(7), new MeasureContentKey(1), new MeasureContentKey(2),
            new MeasureContentKey(8), new MeasureContentKey(1), new MeasureContentKey(2));
        SystemBreaker.SpringEdgeKey Edge(long next) => new(next, 0, 0);
        var edges = ImmutableArray.Create(Edge(10), Edge(0), Edge(0), Edge(20), Edge(0), Edge(0));
        var cache = new SystemLayoutCache();
        cache.SetContentKeys(keys, edges);
        int calls = 0;
        Func<ImmutableArray<MeasureLayout>> factory = () => { calls++; return Layout(0); };

        // Bars 1-2 and 4-5 hold the same content, behind bars 0 and 3 that end differently.
        cache.GetOrComputeMeasures(1, 2, false, false, 1.0, 0.25, factory);
        cache.GetOrComputeMeasures(4, 2, false, false, 1.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        Assert.Equal(2, calls);

        // ...and the SAME system misses when only the bar before it changes its end.
        cache.SetContentKeys(keys, edges.SetItem(3, Edge(30)));
        cache.GetOrComputeMeasures(4, 2, false, false, 1.0, 0.25, factory);
        Assert.False(cache.LastWasHit);
        cache.GetOrComputeMeasures(1, 2, false, false, 1.0, 0.25, factory);
        Assert.True(cache.LastWasHit);
        Assert.Equal(3, calls);
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

    [Fact]
    public void ScoreLargerThanTheCap_KeepsEveryCurrentSystemCached()
    {
        // Regression guard for the eviction policy: with a flat FIFO cap, a score
        // with more systems than MaxEntries (1024) would evict its OWN working set
        // while the pass is still inserting it, and every subsequent edit would then
        // miss every system forever (permanent 0% hit rate). Entries inserted or hit
        // in the current pass are eviction-exempt, so a content-unchanged second
        // pass must hit all N systems.
        const int N = 1500;
        var keys = new MeasureContentKey[N];
        for (int i = 0; i < N; i++)
            keys[i] = new MeasureContentKey(i + 1);

        var cache = new SystemLayoutCache();
        cache.SetContentKeys(keys.ToImmutableArray());
        for (int i = 0; i < N; i++)
            cache.GetOrComputeMeasures(i, 1, i == 0, i == N - 1, 2.0, 0.25, () => Layout(0));

        cache.SetContentKeys(keys.ToImmutableArray()); // next edit, content unchanged
        for (int i = 0; i < N; i++)
        {
            cache.GetOrComputeMeasures(i, 1, i == 0, i == N - 1, 2.0, 0.25, () => Layout(0));
            Assert.True(cache.LastWasHit, $"system {i} was evicted mid-pass");
        }
    }

    [Fact]
    public void StaleEntriesAcrossManyEdits_StayBounded()
    {
        // The cap still does its job on the STALE backlog: a long session where
        // every edit changes the (single) system's content leaves one dead entry
        // per edit behind, and those must not accumulate without bound.
        var cache = new SystemLayoutCache();
        for (int g = 0; g < 3000; g++)
        {
            cache.SetContentKeys(ImmutableArray.Create(new MeasureContentKey(g + 1)));
            cache.GetOrComputeMeasures(0, 1, true, true, 2.0, 0.25, () => Layout(0));
        }
        // At most the 1024-entry stale cap plus the current pass's live entry.
        Assert.True(cache.Count <= 1025, $"cache grew unbounded: {cache.Count}");
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
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>
    /// Systems 2 and 4 hold the same bars, but system 4 opens after a `:|:` and draws its
    /// `.|:` at the line start. With the content slice alone as the key, the FIRST render
    /// laid system 4 out by shifting system 2 — the preview differed from
    /// <see cref="SvgGenerator.Generate"/> before any edit (session 595, a corpus book).
    /// </summary>
    [Fact]
    public void IncrementalCompiler_FirstRender_DoesNotReuseASystemAcrossADifferentBarLine()
    {
        const string src = """
            octave absolute
            time 4/4
            key c major
            part melody
            section Main {
              melody {
                c4 d e f | g4 a b c |
                break
                e4 f g a | b4 c d e |
                break
                e4 f g a | b4 c d e :|:
                break
                e4 f g a | b4 c d e |
                break
                c4 d e f | g4 a b c |
              }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var opt = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var tree = SyntaxTree.Parse(src);
        // Non-vacuous: systems 2 and 4 (bars 2-3, 6-7) share their slice, and bar 5 ends on `:|:`.
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var keys = MeasureContentKey.Compute(score);
        var ms = score.PrimaryContentStaff.PrimaryVoice.Measures;
        Assert.True(keys[2] == keys[6], ModelDeepDiff.FirstDifference(ms[2], ms[6], "m") ?? "same measure, other key");
        Assert.Equal(keys[2], keys[6]);
        Assert.Equal(keys[3], keys[7]);
        Assert.Equal(BarlineType.RepeatBoth, score.PrimaryContentStaff.PrimaryVoice.Measures[5].EndBarline);

        var session = new IncrementalCompiler(tree, opt);
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(src), opt), session.Render());
    }

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
        // feature-tour is a single-voice, 2-staff score with no grob overrides, so the
        // per-system cache engages (S5-3c) with per-measure keys that combine both staves,
        // and both the spring solve AND the skyline are memoized per system. (A multi-VOICE
        // score is deliberately excluded from reuse — see
        // MultiVoice_FallsBackToFullLayout_AndStaysByteIdentical.)
        // ⚠️ It must span SEVERAL systems or this test is vacuous — reuse across systems is
        // the whole subject. It used to read showcase/03-piano, whose sixteen bars stopped
        // spanning two systems once the break gate's springs were corrected; the assertion
        // below then failed on the fixture's premise rather than on reuse. Pick a longer
        // score rather than weakening the guard.
        var source = LoadFixture("test/feature-tour");
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
    public void MultiVoice_EditToSecondVoice_DeclinesReuse_AndStaysByteIdentical()
    {
        // grammar-tour has a staff with two simultaneous voices (voice { } { }).
        // A polyphonic score is no longer gated out of reuse wholesale: the per-measure
        // content key folds every voice, and the spring gate always saw them. So the
        // per-system cache IS installed here — and an edit that changes voice 2 moves
        // the key, declines whole-layout reuse, and stays byte-identical with a full
        // recompile. (Detecting the edit is what makes reuse safe, rather than
        // refusing to reuse at all.)
        var source = LoadFixture("showcase/grammar-tour");
        var session = new IncrementalCompiler(SyntaxTree.Parse(source));
        session.Render();

        // The per-system cache is now installed for a multi-voice score.
        Assert.NotNull(session.SystemCache);

        // Editing the SECOND voice's first pitch (b' -> c') must stay byte-identical
        // to a full recompile, and must NOT reuse the cached whole layout.
        int at = source.IndexOf("voice { b'2", StringComparison.Ordinal) + "voice { ".Length;
        Assert.True(at > "voice { ".Length, "expected a 'voice { b'2' second voice in the fixture");
        string edited = source.Remove(at, 1).Insert(at, "c");
        var incremental = session.Edit(new TextChange(new TextSpan(at, 1), "c"));

        Assert.False(session.LastEditReusedLayout);
        var full = new IncrementalCompiler(SyntaxTree.Parse(edited)).Render();
        Assert.Equal(full, incremental);
    }

    private static (VerticalSkyline up, VerticalSkyline down) Pair(double end)
        => (VerticalSkyline.FromBox(0, end, 1, 1, VerticalDirection.Up),
            VerticalSkyline.FromBox(0, end, 1, 1, VerticalDirection.Down));

    /// <summary>A one-step UP program's steps — so the DOWN instance comes straight back and
    /// the UP one is the identity the assertions read.</summary>
    private static PagingAugmentProgram.Builder VoltaProgram(double start, double end)
    {
        var builder = new PagingAugmentProgram.Builder();
        builder.AddVoltaBox(start, end, 4, 4);
        return builder;
    }

    private static List<(double Start, double End, double Value)> Shape(VerticalSkyline s)
        => s.Buildings.Select(b => (b.Start, b.End, b.ValueAt(b.Start))).ToList();

    /// <summary>
    /// The PAGING-AUGMENT store's two rooms: the count loop's two placements of the same
    /// book must not evict each other under a shared system index.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS A LIVENESS NET, AND LIVENESS IS ALL IT CAN BE — a memo that serves nothing
    /// is still CORRECT (every miss just recomputes), so the incremental==full harness stays
    /// green while the store is dead machinery. MEASURED before the second room (session 412,
    /// 292 books × 8 keystrokes, Release, allocation bytes): 13,896 lookups missed and every
    /// one of them found an entry whose baseline was another INSTANCE — never a missing
    /// entry, never a differing program. 5,936 of those were the second placement taking the
    /// first's slot and 5,936 the next keystroke's first placement taking it back, one for
    /// one. ★ With one room this test reads 0 hits and 8 misses.
    /// </remarks>
    [Fact]
    public void PagingAugments_TheTwoPlacementsOfOneSystemIndex_BothKeepServing()
    {
        var cache = new SystemLayoutCache();
        // Two placements' system 0: a different run of measures, hence different instances
        // out of GetOrComputeSkyline and a different resolved program.
        var ideal = Pair(100);
        var chosen = Pair(80);

        var first = cache.GetOrComputePagingAugment(0, ideal, VoltaProgram(10, 20));
        var second = cache.GetOrComputePagingAugment(0, chosen, VoltaProgram(30, 40));
        Assert.Equal((0, 2), cache.PagingAugmentStats);
        Assert.NotSame(first.up, second.up);

        // Three more keystrokes over an unedited book: each placement rebuilds an EQUAL
        // program (what AugmentSkylinesForPaging does every time) and finds its own room.
        for (int keystroke = 0; keystroke < 3; keystroke++)
        {
            Assert.Same(first.up,
                cache.GetOrComputePagingAugment(0, ideal, VoltaProgram(10, 20)).up);
            Assert.Same(second.up,
                cache.GetOrComputePagingAugment(0, chosen, VoltaProgram(30, 40)).up);
        }
        Assert.Equal((6, 2), cache.PagingAugmentStats);
    }

    /// <summary>A third distinct lookup evicts the OLDER room — which is what keeps the
    /// store bounded, and costs a recompute rather than a wrong answer.</summary>
    [Fact]
    public void PagingAugments_AThirdDistinctLookup_EvictsTheOlderRoomAndStaysCorrect()
    {
        var cache = new SystemLayoutCache();
        var (a, b, c) = (Pair(100), Pair(80), Pair(60));

        var va = cache.GetOrComputePagingAugment(0, a, VoltaProgram(10, 20));
        cache.GetOrComputePagingAugment(0, b, VoltaProgram(10, 20));
        var vc = cache.GetOrComputePagingAugment(0, c, VoltaProgram(10, 20));
        Assert.Equal((0, 3), cache.PagingAugmentStats);

        // b and c hold the rooms — and asking for b PROMOTES it, so the miss below evicts c.
        Assert.Same(vc.up, cache.GetOrComputePagingAugment(0, c, VoltaProgram(10, 20)).up);
        var vb = cache.GetOrComputePagingAugment(0, b, VoltaProgram(10, 20));
        Assert.Equal((2, 3), cache.PagingAugmentStats);

        // a was evicted: it recomputes, and the recompute is the same silhouette.
        var again = cache.GetOrComputePagingAugment(0, a, VoltaProgram(10, 20));
        Assert.NotSame(va.up, again.up);
        Assert.Equal(Shape(va.up), Shape(again.up));
        Assert.Same(vb.up, cache.GetOrComputePagingAugment(0, b, VoltaProgram(10, 20)).up);
        Assert.Equal((3, 4), cache.PagingAugmentStats);
    }

    /// <summary>
    /// The key reads the steps' resolved NUMBERS, not only their kinds: over the same
    /// baseline instances, a volta moved along the line is a miss and its own silhouette.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOTHING ELSE WATCHED IT. POISONED (session 508): a key that skipped the numbers
    /// (<c>PagingAugmentProgram.Builder.Matches</c> without its <c>_args</c> loop) left all
    /// 8,850 tests green — the reference half of the key answered first in every book the
    /// suite edits. The poison serves the first volta's silhouette for the second.
    /// </remarks>
    [Fact]
    public void PagingAugments_AStepMovedOverTheSameBaseline_Misses()
    {
        var cache = new SystemLayoutCache();
        var baseline = Pair(100);

        var first = cache.GetOrComputePagingAugment(0, baseline, VoltaProgram(10, 20));
        var moved = cache.GetOrComputePagingAugment(0, baseline, VoltaProgram(30, 40));

        Assert.Equal((0, 2), cache.PagingAugmentStats);
        Assert.NotEqual(Shape(first.up), Shape(moved.up));
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
