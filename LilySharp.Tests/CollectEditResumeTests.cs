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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The completeness net for the CROSS-EDIT (Δ≠0) resume — BOTH ends
/// (<see cref="CollectResumePlanner"/> — HANDOFF ▶ ⒭ ⑵'s second slice: the
/// prefix resume and the suffix splice): a collect resumed across a synthetic
/// edit must be indistinguishable from a full collect of the edited text.
/// Edits are mechanical (a duplicated space, a deleted space, a swapped pitch
/// letter — early, middle and late), so any book that still collects cleanly
/// after the edit participates; what the edit MEANS is irrelevant, because
/// both sides collect the same edited text. This is what holds the guard sets
/// honest — the planner's (MaxSourceRead folds, top-level window guard,
/// walk-entry validations, parse agreements) and the splice's (state match,
/// position shifter, canonical bars): a read missing from the folds, a state
/// field missing from the comparison, or a position field missing from the
/// shifter shows up here as a model/data-pos difference on whichever fixture
/// exercises it (the deep diff compares positions too).
/// </summary>
public class CollectEditResumeTests
{
    // ---------- the net ----------

    [Fact]
    public void ResumedCollect_MatchesFullCollect_AcrossSyntheticEdits()
    {
        var failures = new List<string>();
        int booksAdopting = 0, booksSplicing = 0, resumes = 0, bails = 0, planless = 0;

        foreach (var path in CollectResumeTests.NetBooks())
        {
            var outcome = RunBook(path, failures, render: false);
            if (outcome.Resumes > 0 && outcome.Adopted > 0)
                booksAdopting++;
            if (outcome.Spliced > 0)
                booksSplicing++;
            resumes += outcome.Resumes;
            bails += outcome.Bails;
            planless += outcome.Planless;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} edit-resume mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        // The net must bite: if the guards silently tightened until nothing is
        // ever planned (or everything bails), this alarm fires — not a pass.
        Assert.True(booksAdopting >= 15,
            $"only {booksAdopting} books adopted any prefix across an edit " +
            $"(resumes {resumes}, bails {bails}, planless {planless}) — the planner's guards collapsed?");
        // Same alarm for the suffix side: the splice's guard set (state match,
        // shifter, canonical bars, parse-suffix agreement) collapsing to
        // "never splices" must fail loudly, not read as a pass.
        Assert.True(booksSplicing >= 15,
            $"only {booksSplicing} books spliced any suffix across an edit — the splice's guards collapsed?");
    }

    [Fact]
    public void ResumedCollect_RendersByteIdenticalSvg_OnSubset()
    {
        var failures = new List<string>();
        int rendered = 0;
        foreach (var path in CollectResumeTests.NetBooks())
        {
            var outcome = RunBook(path, failures, render: true);
            if (outcome.Resumes > 0 && ++rendered >= 3)
                break;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(rendered >= 3, $"only {rendered} books reached the SVG comparison");
    }

    [Fact]
    public void ResumedCollect_AdoptsThePrefix_OnPerfBooks()
    {
        // The books the memo is priced for, edited the way EditKeystrokeBench
        // edits them (one token from the middle of the book). The prefix side
        // must actually adopt about half the walk here — this is the perf claim's
        // correctness half. perf-v2bow1k is the SPLICE-ONLY shape: its entire
        // body is one parallel `voice {…} {…}` run, and checkpoints exist only at
        // the primary walk's TOP-LEVEL boundaries — so the prefix side has a
        // single m=0 checkpoint and adopts NOTHING, but the suffix side splices
        // the WHOLE recording: the bench token edits the SECOND voice, which the
        // primary walk never adopts (voices 2..N are rebuilt live from the
        // re-resolved parallel spans, so the edit is picked up there).
        // ⚠️ This book used to be excluded here under the claim "nothing is
        // adoptable" — measured false for the splice half in session 149, which
        // caught the splice firing on every keystroke with zero observers.
        // ⚠️ audit/lpreg, not scratch/lpreg: the old address is gitignored, so the
        // File.Exists skip below fired on every machine (session 177).
        var dir = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "lpreg");
        var books = new (string Book, string Find, string Replace, bool ExpectPrefix)[]
        {
            ("perf-plain1k.lys", "g8", "a8", true),
            ("perf-fingbeam1k.lys", "e@finger(3)", "f@finger(3)", true),
            ("perf-v2bow1k.lys", "e4(", "f4(", false),
        };
        var failures = new List<string>();
        foreach (var (book, find, replace, expectPrefix) in books)
        {
            var path = Path.Combine(dir, book);
            if (!File.Exists(path))
                continue; // checkout without the perf corpus
            var oldText = File.ReadAllText(path);
            int idx = oldText.IndexOf(find, oldText.Length / 2, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"{book}: bench token not found");
            var newText = oldText.Remove(idx, find.Length).Insert(idx, replace);

            var (adopted, resumesRun, spliced) = RunOneEdit(book, oldText, newText, failures, render: false);
            Assert.True(resumesRun > 0, $"{book}: the mid-book edit produced no resume plan");
            if (expectPrefix)
            {
                Assert.True(adopted > 50,
                    $"{book}: only {adopted} measures adopted for a mid-book edit — the prefix side is not working");
                // The suffix half of the same perf claim: everything PAST the edited
                // measure must come from the recorded tail, not a live re-walk.
                Assert.True(spliced > 50,
                    $"{book}: only {spliced} measures spliced for a mid-book edit — the suffix side is not working");
            }
            else
            {
                // The whole-book-span shape: prefix adopts nothing (m=0 is the
                // only checkpoint) and the splice adopts the whole primary walk.
                Assert.Equal(0, adopted);
                Assert.True(spliced > 900,
                    $"{book}: only {spliced} measures spliced — the m=0 whole-walk splice regressed");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void V2bowWholeBookSpan_SecondVoiceEdit_SplicesTheWholeWalk_ByteIdentical()
    {
        // Session 149's discovery, held: a book whose whole body is one
        // `voice {…} {…}` parallel span splices its ENTIRE primary-walk
        // recording at the m=0 checkpoint when the edit sits in a non-primary
        // voice — the primary walk adopts no edited byte, and the edited voice
        // is rebuilt live from the re-resolved spans (ResolveShifted). This is
        // the production path (IncrementalCompiler), asserted byte-identical
        // against a full recompile on BOTH alternation sides; the splice
        // liveness assertion is what makes a silently-declining guard set fail
        // here instead of reading as a pass.
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(),
            "scratch", "lpreg", "perf-v2bow1k.lys");
        if (!File.Exists(path))
            return; // checkout without the perf corpus
        var baseText = File.ReadAllText(path);
        int idx = baseText.IndexOf("e4(", baseText.Length / 2, StringComparison.Ordinal);
        Assert.True(idx >= 0, "bench token not found");
        var edited = baseText.Remove(idx, 3).Insert(idx, "f4(");

        var options = new SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(baseText), options);
        compiler.Render();

        for (int i = 0; i < 2; i++)
        {
            string text = i % 2 == 0 ? edited : baseText;
            var incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            var full = SvgGenerator.Generate(SyntaxTree.Parse(text), options);
            Assert.Equal(full, incremental);
            Assert.True(compiler.LastCollectResume.SplicedMeasures > 900,
                $"round {i} ({(i % 2 == 0 ? "edited" : "restore")}): spliced only "
                + $"{compiler.LastCollectResume.SplicedMeasures} measures — the whole-walk splice regressed");
        }
    }

    [Fact]
    public void V2bowWholeBookSpan_PrimaryVoiceEdit_DeclinesTheSplice()
    {
        // The boundary of the same discovery: an edit inside the PRIMARY voice's
        // block lands inside the adopted tail's positions, so every splice
        // candidate must decline and the walk runs live (correct output, no
        // reuse — session 149 measured collect 52–81 ms vs 27–30 spliced).
        // A splice that fires anyway would adopt the OLD pitch: the deep
        // compare inside RunOneEdit is what bites then.
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(),
            "scratch", "lpreg", "perf-v2bow1k.lys");
        if (!File.Exists(path))
            return; // checkout without the perf corpus
        var oldText = File.ReadAllText(path);
        // Mid-voice-0: the primary voice's block occupies the first half of the
        // book, so a token found from 25% sits strictly inside it.
        int idx = oldText.IndexOf("g'4", (int)(oldText.Length * 0.25), StringComparison.Ordinal);
        Assert.True(idx >= 0 && idx < oldText.Length / 2,
            $"voice-0 token landed at {idx} — fixture texture changed?");
        var newText = oldText.Remove(idx, 3).Insert(idx, "a'4");

        var failures = new List<string>();
        var (adopted, resumesRun, spliced) = RunOneEdit(
            "perf-v2bow1k.lys", oldText, newText, failures, render: false);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(resumesRun > 0, "the voice-0 edit produced no resume plan");
        Assert.Equal(0, adopted);
        Assert.Equal(0, spliced);
    }

    [Fact]
    public void IncrementalCompiler_EditedKeystroke_ResumesCollect_AndMatchesFullRecompile()
    {
        // End-to-end through the production wiring: alternate two texts the way
        // EditKeystrokeBench does and require (a) byte identity with a full
        // recompile at every step, (b) the collect resume actually firing on the
        // edited steps (this is the wiring's liveness check — without it a
        // planner that always returns null would pass everything else).
        var path = CollectResumeTests.NetBooks()
            .First(p => Path.GetFileName(p) == "01-expressions.lys");
        var baseText = File.ReadAllText(path);
        // Late edit: swap a pitch letter inside the LAST phrase body so a real
        // prefix exists before the window.
        int idx = baseText.IndexOf("a4 b cis d@fermata", StringComparison.Ordinal);
        Assert.True(idx >= 0, "fixture texture changed — pick another late token");
        var edited = baseText.Remove(idx, 2).Insert(idx, "g4");

        var options = new SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(baseText), options);
        compiler.Render();

        bool everResumed = false, everSpliced = false;
        for (int i = 0; i < 4; i++)
        {
            string text = i % 2 == 0 ? edited : baseText;
            var incremental = compiler.RenderIncremental(SyntaxTree.Parse(text));
            var full = SvgGenerator.Generate(SyntaxTree.Parse(text), options);
            Assert.Equal(full, incremental);
            everResumed |= compiler.LastCollectResume.Walks > 0
                && compiler.LastCollectResume.AdoptedMeasures > 0;
            everSpliced |= compiler.LastCollectResume.SplicedWalks > 0
                && compiler.LastCollectResume.SplicedMeasures > 0;
        }
        Assert.True(everResumed, "no keystroke resumed the collect — the wiring is dead");
        Assert.True(everSpliced, "no keystroke spliced the collect's suffix — the splice wiring is dead");
    }

    [Fact]
    public void SuffixSplice_DeclinesWhenAnotherPartsBarCountChanges()
    {
        // The canonical-bars guard's own book (nothing in the fixture corpus
        // exercises it): part b defines ONE bar of section S, part a three, so
        // b's walk pads S up to the canonical count — a function of a's TEXT.
        // Deleting a's last bar changes the canonical count while leaving b's
        // own music (and thus every state field b's checkpoints compare) intact:
        // the ONLY thing standing between b's walk and adopting a tail with a
        // stale spacer count is CanonicalBarsMatch. Byte-equal output proves the
        // decline; a poisoned guard makes this the test that fails.
        // The score block sits ABOVE the edit on purpose: restating a title, it
        // must be position-stable (HeaderOverrides bakes token positions), and
        // below a Δ≠0 edit that guard would correctly refuse to plan at all —
        // which would leave this test testing nothing. And va's bars are RESTS
        // on purpose: notes feed the pitch trace, so deleting a bar of notes
        // changes va's cumulative table counts and vb's walk bails at ENTRY
        // (StartTableCounts) before the guard under test is even consulted —
        // rests keep every table count intact, so vb's checkpoints match on
        // every field EXCEPT what only the canonical count can see.
        const string oldText = @"part va { clef treble }
part vb { clef bass }

score main ""canon"" { staff va staff vb }

section S {
  va { r4 r r r | r4 r r r | r4 r r r | }
  vb { c4 b a g | }
}
";
        const string removed = "r4 r r r | ";
        int last = oldText.LastIndexOf(removed, StringComparison.Ordinal);
        Assert.True(last > 0);
        var newText = oldText.Remove(last, removed.Length);

        var failures = new List<string>();
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder };
        var oldTree = SyntaxTree.Parse(oldText);
        SvgGenerator.CollectScore(source, oldTree, RenderSpecParser.FindFirst(oldTree));

        var newTree = SyntaxTree.Parse(newText);
        var newSpec = RenderSpecParser.FindFirst(newTree);
        var fullNew = SvgGenerator.CollectScore(newTree, newSpec);

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        Assert.True(resumer != null, "the bar deletion produced no plan at all");
        Assert.True(resumer!.ResumePlans.Values.Any(p => p.SuffixCandidates is { Count: > 0 }),
            "no walk kept suffix candidates — the guard under test is not even reachable");

        var collector = new MeasureCollector { WalkProbe = resumer };
        var resumed = SvgGenerator.CollectScore(collector, newTree, newSpec);
        var diff = ModelDeepDiff.FirstDifference(fullNew, resumed, "score");
        Assert.True(diff == null, $"resumed differs from full: {diff}");
        // And the equality must have come from DECLINING, not from a lucky
        // splice: every plan's splice count is zero on this edit.
        Assert.All(resumer.ResumePlans.Values, p => Assert.Equal(0, p.SplicedMeasures));
    }

    // ---------- runner ----------

    private readonly record struct BookOutcome(
        int Resumes, int Adopted, int Bails, int Planless, int Spliced);

    /// <summary>Applies each synthetic edit to <paramref name="path"/> and compares
    /// a resumed collect of the edited text against a full collect of it.</summary>
    private static BookOutcome RunBook(string path, List<string> failures, bool render)
    {
        var oldText = File.ReadAllText(path);
        if (oldText.Contains("using \"", StringComparison.Ordinal))
            return default; // `using` books are expanded by the LSP before collect

        int resumes = 0, adopted = 0, bails = 0, planless = 0, spliced = 0;
        foreach (var newText in SyntheticEdits(oldText))
        {
            var (a, r, s) = RunOneEdit(Path.GetFileName(path), oldText, newText, failures, render,
                onBail: () => bails++, onPlanless: () => planless++);
            adopted += a;
            resumes += r;
            spliced += s;
        }
        return new BookOutcome(resumes, adopted, bails, planless, spliced);
    }

    /// <summary>One edit: records a full collect of <paramref name="oldText"/>,
    /// plans against <paramref name="newText"/>, and compares the resumed collect
    /// with a fresh full collect of the new text. Returns (adopted measures,
    /// resumes run, spliced measures); a book/edit that does not collect cleanly
    /// is skipped.</summary>
    private static (int Adopted, int Resumes, int Spliced) RunOneEdit(
        string book, string oldText, string newText, List<string> failures, bool render,
        Action? onBail = null, Action? onPlanless = null)
    {
        SyntaxTree oldTree, newTree;
        RenderSpec? oldSpec, newSpec;
        MultiStaffScore fullNew;
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder };
        try
        {
            oldTree = SyntaxTree.Parse(oldText);
            oldSpec = RenderSpecParser.FindFirst(oldTree);
            source.ScoreTranspose = oldSpec?.ScoreTranspose;
            SvgGenerator.CollectScore(source, oldTree, oldSpec);

            newTree = SyntaxTree.Parse(newText);
            newSpec = RenderSpecParser.FindFirst(newTree);
            fullNew = SvgGenerator.CollectScore(newTree, newSpec);
        }
        catch
        {
            return default; // the net covers texts that collect cleanly
        }

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        if (resumer == null)
        {
            onPlanless?.Invoke();
            return default;
        }

        string where = $"{book} (edit at {FirstDiff(oldText, newText)})";
        try
        {
            var collector = new MeasureCollector
            {
                ScoreTranspose = newSpec?.ScoreTranspose,
                WalkProbe = resumer,
            };
            var resumed = SvgGenerator.CollectScore(collector, newTree, newSpec);
            int adoptedMeasures = resumer.ResumePlans.Values
                .Where(p => p.Consumed).Sum(p => p.Checkpoint!.MeasureCount);
            int splicedMeasures = resumer.ResumePlans.Values.Sum(p => p.SplicedMeasures);

            if (render)
            {
                var fullSvg = Render(fullNew);
                var resumedSvg = Render(resumed);
                if (fullSvg != resumedSvg)
                    failures.Add($"{where}: SVG differs");
            }
            else
            {
                var diff = ModelDeepDiff.FirstDifference(fullNew, resumed, "score");
                if (diff != null)
                    failures.Add($"{where}: {diff}");
            }
            return (adoptedMeasures, 1, splicedMeasures);
        }
        catch (CollectResumeAbortException)
        {
            onBail?.Invoke(); // a bail is a lost reuse, never a failure
            return default;
        }
        catch (Exception ex)
        {
            failures.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    /// <summary>Deterministic mechanical edits: a duplicated space (pure position
    /// shift, Δ=+1) late and mid-file, a deleted space late (Δ=-1), a pitch
    /// letter swap late (Δ=0 content change), and a deleted MID-FILE barline
    /// (Δ=-1 structural change — the measure count downstream of the window
    /// moves, so every suffix-splice state comparison must decline; a splice
    /// that fires anyway corrupts the whole tail, which is what makes this the
    /// edit that bites on a poisoned state match). "Late" maximizes the
    /// adoptable prefix; the mid variants check strictly inside the walk.</summary>
    private static IEnumerable<string> SyntheticEdits(string text)
    {
        int late = text.LastIndexOf(' ');
        if (late > 0)
            yield return text.Insert(late, " ");

        int mid = text.IndexOf(' ', text.Length / 2);
        if (mid > 0)
            yield return text.Insert(mid, " ");

        if (late > 0)
            yield return text.Remove(late, 1);

        // Last "<pitch letter><digit>" occurrence, swapped to a neighboring letter.
        for (int i = text.Length - 2; i > 0; i--)
        {
            char c = text[i];
            if (c is >= 'a' and <= 'g' && char.IsDigit(text[i + 1]) && !char.IsLetter(text[i - 1]))
            {
                yield return text.Remove(i, 1).Insert(i, c == 'g' ? "a" : ((char)(c + 1)).ToString());
                break;
            }
        }

        // A lone mid-file `|` deleted (not `|:` `:|` `||` — those would change
        // the barline TYPE rather than the measure structure).
        for (int i = text.Length / 2; i < text.Length; i++)
        {
            if (text[i] == '|'
                && (i == 0 || (text[i - 1] != '|' && text[i - 1] != ':'))
                && (i + 1 >= text.Length || (text[i + 1] != '|' && text[i + 1] != ':' && text[i + 1] != '.')))
            {
                yield return text.Remove(i, 1);
                break;
            }
        }
    }

    private static string Render(MultiStaffScore score)
        => SvgGenerator.RenderToSvg(score, new LayoutEngine().Layout(score), new SvgRenderOptions());

    private static string FirstDiff(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i.ToString();
    }
}
