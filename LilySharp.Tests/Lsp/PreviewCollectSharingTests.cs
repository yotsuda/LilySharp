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
using System.Threading;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Diagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The Problems panel borrows the preview's collect instead of collecting the book again
/// (HANDOFF §2 R13⒜, session 399). A keystroke starts two computations over one tree — the
/// preview's incremental compile and, debounced behind it, the semantic-validation pass —
/// and the pass used to open with its own full collect of the whole book, no resume, no
/// probe, on top of the collect the preview had just finished.
/// </summary>
/// <remarks>
/// Two claims, held apart. ⑴ EQUALITY: a lent collect — full or RESUMED — makes the
/// collector-backed validators say exactly what a fresh collect makes them say, across the
/// edit-resume net's synthetic edits over every fixture (the resume adopts the warning
/// tables by copy, <c>MeasureCollector.CumulativeSideTables</c>, and the post-walk scanners
/// run live; a table missing from either would show up here as a missing or mis-placed
/// warning). ⑵ LIVENESS AND ITS LIMITS: the server actually lends once the preview has
/// rendered the document's current tree, and declines while the tree moved on or while the
/// session previews a NAMED block — the validators are asked of the FIRST score by contract
/// (<see cref="DiagnosticCodes.UnengravedRehearsalMark"/>'s remarks), whichever block the
/// picker shows.
/// <para>
/// Poisons: make <c>IncrementalCompiler.CollectFor</c> ignore <c>FirstBlock</c> ⇒ the named
/// render test goes red; make it ignore the tree ⇒ the moved-on test goes red; drop a
/// warning table from <c>CumulativeSideTables</c> ⇒ the net reports the fixture that writes
/// that warning before its edit.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class PreviewCollectSharingTests
{
    /// <summary>A book the collector-backed validators have something to say about: a
    /// slur never closed (LYS4010), a tie to another pitch (LYS4007) and a rehearsal mark
    /// in a section the form never plays (LYS4019) — the first two are cumulative side
    /// tables a resume adopts, the third is read off the marks table.</summary>
    private const string Book = """
        octave absolute
        time 4/4
        key c major
        part melody { clef treble }
        part bass { clef bass octave 3 }
        section A {
          melody { c'4( d' e' f' | g'4 ~ a' b' c'' | }
          bass { c4 d e f | g a b c' | }
        }
        section B {
          melody { c'4@mark("B") d' e' f' | }
          bass { c4 d e f | }
        }
        form main { A }
        score main { staff melody staff bass }
        score main "Bass only" { staff bass }
        """;

    private static LilySharpLanguageServer Opened(Uri uri, string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp",
            },
        });
        return server;
    }

    private static SvgParams Ask(Uri uri, string? renderName) => new()
    {
        TextDocument = new TextDocumentIdentifier { Uri = uri },
        RenderName = renderName,
    };

    private static string Render(Diagnostic d) => $"{d.Code}@{d.Span.Start}+{d.Span.Length} {d.Severity}: {d.Message}";

    private static string[] Rendered(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(Render).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    // ---------- ⑵ liveness and its limits, through the server ----------

    [Fact]
    public void ThePanel_BorrowsThePreviewsCollect_OnlyWhileTheTreesAgree()
    {
        var uri = new Uri("file:///lend.lys");
        var server = Opened(uri, Book);
        var doc = server.DocumentAt(uri)!;

        // No preview yet: nothing to borrow, the pass collects for itself — and says the
        // three things the book was written to make it say.
        var fresh = server.DocumentDiagnostics(doc, CancellationToken.None, out bool lent);
        Assert.False(lent);
        Assert.Contains(fresh, d => d.Code == DiagnosticCodes.UnpairedSlur);
        Assert.Contains(fresh, d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.Contains(fresh, d => d.Code == DiagnosticCodes.UnengravedRehearsalMark);

        // The preview renders the default (first) score; now the pass borrows its collect...
        Assert.Null(server.GetSvg(Ask(uri, null)).Error);
        var borrowed = server.DocumentDiagnostics(doc, CancellationToken.None, out lent);
        Assert.True(lent);
        // ...and says exactly what it said on its own.
        Assert.Equal(Rendered(fresh), Rendered(borrowed));

        // The document moves on (the tie now lands on its own pitch): the preview's collect
        // is of the OLD tree, so the pass declines it and collects the new text itself...
        var edited = Book.Replace("g'4 ~ a'", "g'4 ~ g'", StringComparison.Ordinal);
        server.DidChange(new DidChangeTextDocumentParams
        {
            TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = [new TextDocumentContentChangeEvent { Text = edited }],
        });
        var moved = server.DocumentAt(uri)!;
        Assert.NotSame(doc, moved);
        var fresh2 = server.DocumentDiagnostics(moved, CancellationToken.None, out lent);
        Assert.False(lent);
        Assert.DoesNotContain(fresh2, d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.Contains(fresh2, d => d.Code == DiagnosticCodes.UnpairedSlur);

        // ...until the preview has rendered the new text, through the SAME session (a
        // resumed collect, on a book this small most likely a re-record — either way the
        // lent collect is the new tree's).
        var slot = server.SvgSlotFor(uri, null);
        var session = slot.Session;
        Assert.NotNull(session);
        Assert.Null(server.GetSvg(Ask(uri, null)).Error);
        Assert.Same(session, slot.Session);
        var borrowed2 = server.DocumentDiagnostics(moved, CancellationToken.None, out lent);
        Assert.True(lent);
        Assert.Equal(Rendered(fresh2), Rendered(borrowed2));
    }

    [Fact]
    public void ThePanel_DoesNotBorrowANamedRendersCollect()
    {
        // The validators are asked of the FIRST score. A session previewing "Bass only"
        // collected another score — its collect has no slur, no tie and no mark to warn
        // about — and must not be lent, or the panel would change its mind with the picker.
        var uri = new Uri("file:///named.lys");
        var server = Opened(uri, Book);
        var doc = server.DocumentAt(uri)!;

        var bassOnly = server.GetSvg(Ask(uri, "Bass only"));
        Assert.Null(bassOnly.Error);
        Assert.Equal("Bass only", bassOnly.SelectedRender);
        Assert.NotNull(server.SvgSlotFor(uri, "Bass only").Session);

        var diagnostics = server.DocumentDiagnostics(doc, CancellationToken.None, out bool lent);
        Assert.False(lent);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.UnpairedSlur);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.UnengravedRehearsalMark);

        // The default session, once it exists, is the one lent — beside the named one.
        Assert.Null(server.GetSvg(Ask(uri, null)).Error);
        var borrowed = server.DocumentDiagnostics(doc, CancellationToken.None, out lent);
        Assert.True(lent);
        Assert.Equal(Rendered(diagnostics), Rendered(borrowed));
    }

    // ---------- the session's own answer ----------

    [Fact]
    public void ASession_LendsItsResumedCollect_OfTheTreeItCompiled()
    {
        // The warnings sit in the FIRST bars and the edit in the last, on a book long
        // enough for the prefix resume to adopt the bars between: the lent collect is a
        // RESUMED one whose slur and tie warnings were adopted from the source by copy,
        // never re-walked — the case a table missing from CumulativeSideTables would lose.
        var bars = string.Join(" ", Enumerable.Repeat("c'4 d' e' f' |", 40));
        var text = MusicSource.Wrap("g'4( a' b' c'' | g'4 ~ a' b' c'' | " + bars,
            "octave absolute\ntime 4/4\nkey c major");
        var tree = SyntaxTree.Parse(text);
        var session = new IncrementalCompiler(tree);
        Assert.Null(session.CollectFor(tree)); // nothing compiled yet

        session.Render();
        var full = session.CollectFor(tree);
        Assert.NotNull(full);
        Assert.Null(session.CollectFor(SyntaxTree.Parse(text + "\n// another tree")));
        // The same text parsed again is the same collect.
        Assert.Same(full, session.CollectFor(SyntaxTree.Parse(text)));

        // A space doubled inside the LAST bar of the music: the prefix — warnings
        // included — is adopted, not re-walked.
        var editedText = text.Insert(text.LastIndexOf("f' |", StringComparison.Ordinal), " ");
        var edited = SyntaxTree.Parse(editedText);
        session.RenderIncremental(edited);
        Assert.True(session.LastCollectResume.AdoptedMeasures >= 30,
            $"the late edit did not adopt the prefix ({session.LastCollectResume}) — this net wants a RESUMED collect lent");
        var resumed = session.CollectFor(edited);
        Assert.NotNull(resumed);
        Assert.NotSame(full, resumed);
        Assert.Null(session.CollectFor(tree)); // the old tree's collect is no longer on offer

        var fresh = SemanticValidation.Run(edited);
        Assert.Contains(fresh, d => d.Code == DiagnosticCodes.UnpairedSlur);
        Assert.Contains(fresh, d => d.Code == DiagnosticCodes.TieTargetMismatch);
        Assert.Equal(
            Rendered(fresh),
            Rendered(SemanticValidation.Run(edited, CancellationToken.None, () => resumed)));
    }

    // ---------- ⑴ equality across the edit-resume net ----------

    /// <summary>What the collector-backed validators say about <paramref name="tree"/>
    /// when handed <paramref name="collector"/>.</summary>
    private static string[] SharedValidatorDiagnostics(SyntaxTree tree, MeasureCollector? collector)
    {
        var lazy = new Lazy<MeasureCollector?>(() => collector);
        var result = new List<Diagnostic>();
        foreach (var v in SemanticValidation.CreateAll())
        {
            if (v is not ISharedCollectValidator shared)
                continue;
            shared.ValidateWith(tree, lazy);
            result.AddRange(v.Diagnostics);
        }
        return Rendered(result);
    }

    [Fact]
    public void AResumedCollect_YieldsTheSameDiagnosticsAsAFreshOne_AcrossSyntheticEdits()
    {
        var failures = new List<string>();
        int edits = 0, resumedEdits = 0, booksWarning = 0;

        foreach (var path in CollectResumeTests.NetBooks())
        {
            var oldText = File.ReadAllText(path);
            if (oldText.Contains("using \"", StringComparison.Ordinal))
                continue; // `using` books are expanded by the LSP before collect
            bool warned = false;
            foreach (var newText in CollectEditResumeTests.SyntheticEdits(oldText))
            {
                edits++;
                var outcome = CompareOneEdit(Path.GetFileName(path), oldText, newText, failures);
                if (outcome.Resumed)
                    resumedEdits++;
                warned |= outcome.Warned;
            }
            if (warned)
                booksWarning++;
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} lent-collect diagnostic mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        // The net must bite on both axes: resumes actually happened, and the validators
        // actually had warnings to (mis)place. The floors are well under the counts the
        // day this was written (session 399, 258 books: 2344 edits, 1259 planned, 491
        // resumed, 99 bailed; 16 books / 35 edits carried a collector-backed warning —
        // LYS4001 ×31, LYS4010 ×7, LYS4007 ×2). The warnings-in-the-adopted-prefix case
        // the corpus is thin on is held by ASession_LendsItsResumedCollect_OfTheTreeItCompiled.
        Assert.True(resumedEdits >= 100,
            $"only {resumedEdits} of {edits} edits resumed — the planner's guards collapsed?");
        Assert.True(booksWarning >= 8,
            $"only {booksWarning} books produced a collector-backed warning — the net compares nothing");
    }

    private readonly record struct EditOutcome(bool Resumed, bool Warned);

    /// <summary>One edit, the edit-resume net's way: record a full collect of the old
    /// text, plan against the new, and compare the validators' answer on the resumed
    /// collect with their answer on a fresh collect of the new text.</summary>
    private static EditOutcome CompareOneEdit(string book, string oldText, string newText, List<string> failures)
    {
        SyntaxTree oldTree, newTree;
        RenderSpec? oldSpec, newSpec;
        MeasureCollector fresh;
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder };
        try
        {
            oldTree = SyntaxTree.Parse(oldText);
            oldSpec = RenderSpecParser.FindFirst(oldTree);
            source.ScoreTranspose = oldSpec?.ScoreTranspose;
            source.ScoreConcert = oldSpec?.ScoreConcert ?? false;
            SvgGenerator.CollectScore(source, oldTree, oldSpec);

            newTree = SyntaxTree.Parse(newText);
            newSpec = RenderSpecParser.FindFirst(newTree);
            fresh = SemanticValidation.TryCollect(newTree, newSpec)
                ?? throw new InvalidOperationException("fresh collect failed");
        }
        catch
        {
            return default; // the net covers texts that collect cleanly
        }

        var resumer = CollectResumePlanner.Plan(oldTree, newTree, recorder, source);
        if (resumer == null)
            return default;

        MeasureCollector resumed;
        try
        {
            resumed = new MeasureCollector
            {
                ScoreTranspose = newSpec?.ScoreTranspose,
                ScoreConcert = newSpec?.ScoreConcert ?? false,
                WalkProbe = resumer,
            };
            SvgGenerator.CollectScore(resumed, newTree, newSpec);
        }
        catch (CollectResumeAbortException)
        {
            return default; // a bail is a lost reuse, never a failure
        }
        bool didResume = resumer.ResumePlans.Values.Any(p => p.Consumed || p.SplicedMeasures > 0);

        string where = $"{book} (edit at {FirstDiff(oldText, newText)})";
        try
        {
            var expected = SharedValidatorDiagnostics(newTree, fresh);
            var actual = SharedValidatorDiagnostics(newTree, resumed);
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                failures.Add($"{where}: fresh said [{string.Join("; ", expected)}] but the resumed collect said [{string.Join("; ", actual)}]");
            }
            return new EditOutcome(didResume, expected.Length > 0);
        }
        catch (Exception ex)
        {
            failures.Add($"{where}: threw {ex.GetType().Name}: {ex.Message}");
            return default;
        }
    }

    private static int FirstDiff(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i])
                return i;
        return n;
    }

    [Fact]
    public void TheComparison_SeesAStaleCollect()
    {
        // The net's own poison: lend the OLD tree's collect for an edited text and the
        // validators place the slur warning where the old text had it — one column off
        // after a space inserted before it. A comparison blind to positions would pass this.
        var text = MusicSource.Wrap("c'4 d' e' f' | g'4( a' b' c'' |", "octave absolute\ntime 4/4\nkey c major");
        var oldTree = SyntaxTree.Parse(text);
        var stale = SemanticValidation.TryCollect(oldTree)!;
        var newTree = SyntaxTree.Parse(text.Insert(text.IndexOf("c'4", StringComparison.Ordinal), " "));
        var fresh = SemanticValidation.TryCollect(newTree)!;

        var expected = SharedValidatorDiagnostics(newTree, fresh);
        Assert.Contains(expected, s => s.StartsWith(DiagnosticCodes.UnpairedSlur, StringComparison.Ordinal));
        Assert.NotEqual(expected, SharedValidatorDiagnostics(newTree, stale));
    }
}
