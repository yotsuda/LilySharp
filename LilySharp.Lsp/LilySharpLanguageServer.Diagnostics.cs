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

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// The document's tree with <c>using "..."</c> directives resolved (files read
    /// relative to the document), or the plain tree when there are no includes, plus the
    /// warnings from resolving them. The document's own text stays the prefix, so its
    /// positions are preserved.
    /// </summary>
    /// <remarks>
    /// ONE house, taking its inputs rather than reading the disk itself, so the preview,
    /// the exports and the Problems panel all read the SAME expansion and cannot disagree
    /// about what the piece is. They did disagree: the panel validated the unexpanded tree
    /// while everything else expanded, so a file with includes was told its parts were
    /// undefined by the very server that was drawing them.
    /// </remarks>
    internal static (SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> UsingDiagnostics) ExpandUsings(
        string text, SyntaxTree unexpanded, string basePath, Func<string, string?> readFile)
    {
        var (tree, diagnostics) = ExpandUsingsAlone(text, unexpanded, basePath, readFile);
        // …and the staff of what a named singer sings (`score { staff carol  lyrics carol }`), written after
        // the text the same way (Core's Editing.SingerParts): the same house, so the preview,
        // the exports, the playback and the Problems panel read one piece. Asked of the scores
        // alone; a file that prints no singer pays one look at them.
        if (!LilySharp.Core.Editing.SingerParts.Wanted(tree))
            return (tree, diagnostics);
        var made = SingerPartsCache.GetValue(tree, static t =>
        {
            string written = LilySharp.Core.Editing.SingerParts.Expand(t.Text, t, out var said);
            // (From the tree of what was written a keystroke ago: RecentTrees.)
            return new SingerPartsHolder(ReferenceEquals(written, t.Text) ? t : RecentTrees.Parse(written, t.Text.Length), said);
        });
        return (made.Tree, made.Diagnostics.Count == 0 ? diagnostics : [.. diagnostics, .. made.Diagnostics]);
    }

    private sealed record SingerPartsHolder(SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> Diagnostics);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxTree, SingerPartsHolder>
        SingerPartsCache = new();

    private static (SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> UsingDiagnostics) ExpandUsingsAlone(
        string text, SyntaxTree unexpanded, string basePath, Func<string, string?> readFile)
    {
        // Ask the tree we were handed, not the text: parsing it again here would be a whole
        // extra parse per keystroke, since both the preview and the Problems panel land here.
        if (!LilySharp.Core.Parser.UsingExpander.HasUsings(unexpanded))
            return (unexpanded, []);

        // A book WITH usings used to pay the full expansion on every arrival — and two
        // arrive per keystroke (the preview, then the debounced Problems panel), each one
        // re-parsing the main text, every include and the combined result (2026-08-26
        // review, appendix F finding 4). The expansion is a pure function of (main text,
        // base path, what readFile answered for the paths the expander asked), so the
        // snapshot records those reads and a later call REPLAYS them: same answers ⇒ the
        // expander would walk identically ⇒ reuse the parsed tree. Replay asks for the
        // include files again on purpose — the deliberate spec is that an include edited on
        // disk (an unsaved sibling saved, a generator rerun) invalidates by CONTENT, so
        // the price of a hit is one question + string compare per include, not a re-parse of
        // anything (and since session 1299 the question is a look at the file's stamp, not a
        // read of it, while the file stands: IncludeFiles). Keyed on the text INSTANCE (DocumentManager hands out one string per
        // version), so entries die with the edit; the volatile snapshot makes concurrent
        // requests race benignly (both compute, one wins, both answers correct).
        var holder = UsingExpansionCache.GetValue(text, static _ => new UsingExpansionHolder());
        var cached = System.Threading.Volatile.Read(ref holder.Snapshot);
        if (cached != null && cached.BasePath == basePath && ReadsReplayUnchanged(cached.Reads, readFile))
            return (cached.Tree, cached.Diagnostics);

        // A miss is every keystroke in the document itself (a new text instance), so what a
        // miss pays is the keystroke's price, and until session 1294 that was a parse of the
        // main text and of every include only to find their `using`s, then one of everything
        // (review of session 1291, P8). Now: the main text's directives are read off the tree
        // in hand, an include's are kept with its content (RecentTrees.IncludedUsings), and the combined
        // text is parsed from the combined tree of the keystroke before (RecentTrees: the main
        // text is its prefix, so the window is the edit's own). What is left is one string of
        // everything (the includes' texts are kept while their files stand: IncludeFiles).
        var reads = new List<(string Path, string? Content)>();
        var expanded = LilySharp.Core.Parser.UsingExpander.Expand(text, basePath,
            p => { var content = readFile(p); reads.Add((p, content)); return content; },
            out var usingDiagnostics,
            t => ReferenceEquals(t, unexpanded.Text)
                ? LilySharp.Core.Parser.UsingExpander.UsingsOf(unexpanded)
                : RecentTrees.IncludedUsings(t));
        var tree = RecentTrees.Parse(expanded, text.Length);
        var made = new UsingExpansionSnapshot
        {
            BasePath = basePath,
            Reads = reads.ToArray(),
            Tree = tree,
            Diagnostics = usingDiagnostics,
        };
        // Two requests arrive with one text (the preview or the caret, and the Problems panel's
        // pass): both expand, and the FIRST to finish is the expansion of that text for both.
        // The later one's own tree was the one published until session 1296, so the earlier
        // request went on with a tree nobody would be handed again — and everything kept by the
        // tree (the pass's diagnostics, the caret's collects, the sound's index) was made a
        // second time for the same text (KeystrokeSideWorkTests saw the caret collect twice,
        // one run in two). Another's snapshot of OTHER reads (an include saved meanwhile) is not
        // this text's piece any more: the newer one replaces it, as before.
        var raced = System.Threading.Interlocked.CompareExchange(ref holder.Snapshot, made, cached);
        if (ReferenceEquals(raced, cached))
            return (tree, usingDiagnostics);
        if (raced != null && raced.BasePath == basePath && raced.Reads.AsSpan().SequenceEqual(made.Reads))
            return (raced.Tree, raced.Diagnostics);
        System.Threading.Volatile.Write(ref holder.Snapshot, made);
        return (tree, usingDiagnostics);
    }

    /// <summary>One expansion of one document text: what was read to build it (every
    /// readFile question the expander asked, in order, answers included — nested
    /// includes and unreadable files land here too, as (path, null)), and what came
    /// out. Immutable once published.</summary>
    private sealed class UsingExpansionSnapshot
    {
        public string BasePath = "";
        public (string Path, string? Content)[] Reads = [];
        public SyntaxTree Tree = null!;
        public IReadOnlyList<CoreDiagnostic> Diagnostics = [];
    }

    /// <summary>The mutable cell a text's cache entry lives in (the snapshot swaps
    /// whole, under Volatile, so readers see a complete expansion or none).</summary>
    private sealed class UsingExpansionHolder
    {
        public UsingExpansionSnapshot? Snapshot;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, UsingExpansionHolder>
        UsingExpansionCache = new();

    /// <summary>True when every recorded read still gets the same answer — the expander
    /// would walk the same files to the same result, so the snapshot may be reused.</summary>
    private static bool ReadsReplayUnchanged(
        (string Path, string? Content)[] reads, Func<string, string?> readFile)
    {
        foreach (var (path, content) in reads)
            if (!string.Equals(readFile(path), content, StringComparison.Ordinal))
                return false;
        return true;
    }

    private static (SyntaxTree Tree, IReadOnlyList<CoreDiagnostic> UsingDiagnostics) ExpandUsings(
        Document doc, Uri uri)
        => ExpandUsings(doc.Text, doc.Tree, uri.IsFile ? uri.LocalPath : string.Empty,
            IncludeFiles.Read);

    /// <summary>
    /// Every diagnostic that belongs to THIS document: the include-resolution warnings and
    /// the semantic validators run over the expanded tree, keeping only what lands inside
    /// the document's own text.
    /// </summary>
    /// <remarks>
    /// The filter is the price of validating the expansion. An included file's text is
    /// appended AFTER the document's, so a diagnostic about that file's content carries an
    /// offset past the end of this document and would squiggle nothing (or the wrong
    /// thing). It is not lost work — it is another file's diagnostic, and it belongs in
    /// that file's panel, which is a separate piece of plumbing (the LSP has one document
    /// per URI and publishes per URI).
    /// ⚠️ With no includes the expansion is the identity and the filter passes everything,
    /// so the overwhelmingly common path is unchanged.
    /// </remarks>
    internal static IReadOnlyList<CoreDiagnostic> DocumentDiagnostics(
        string text, SyntaxTree unexpanded, string basePath, Func<string, string?> readFile,
        CancellationToken token = default,
        Func<SyntaxTree, LilySharp.Core.Svg.Collector.MeasureCollector?>? previewCollect = null)
    {
        var (tree, usingDiagnostics) = ExpandUsings(text, unexpanded, basePath, readFile);

        var result = new List<CoreDiagnostic>(usingDiagnostics);
        // The preview's collect of THIS expanded tree, when the server has one (the
        // instance method below); the validators otherwise collect the book again.
        Func<LilySharp.Core.Svg.Collector.MeasureCollector?>? lend =
            previewCollect == null ? null : () => previewCollect(tree);
        // (A note about a made singer's staff points at the singer line that made it, not past
        // the document's end: SingerParts.WithOwnNotes.)
        SyntaxNode? root = tree.GetRoot();
        foreach (var d in SemanticValidation.Run(tree, token, lend))
            if (d.Span.Start < text.Length)
                result.Add(LilySharp.Core.Editing.SingerParts.WithOwnNotes(d, tree.Text, ref root));
        return result;
    }

    /// <summary>
    /// <see cref="DocumentDiagnostics(string, SyntaxTree, string, Func{string, string?}, CancellationToken, Func{SyntaxTree, LilySharp.Core.Svg.Collector.MeasureCollector?}?)"/>
    /// for an open document, with the preview's collect lent to the validators
    /// (<see cref="PreviewCollectFor"/>). <paramref name="lentByPreview"/> says whether the
    /// collector-backed validators ran on the preview's collect (true) or on a fresh collect
    /// of the pass's own (false) — for diagnostics / tests.
    /// </summary>
    internal IReadOnlyList<CoreDiagnostic> DocumentDiagnostics(Document doc, CancellationToken token,
        out bool lentByPreview)
    {
        bool lent = false;
        System.Threading.Interlocked.Increment(ref _diagnosticPasses);
        var result = DocumentDiagnostics(doc.Text, doc.Tree,
            doc.Uri.IsFile ? doc.Uri.LocalPath : string.Empty,
            IncludeFiles.Read,
            token,
            tree =>
            {
                var collector = PreviewCollectFor(doc.Uri, tree);
                lent = collector != null;
                return collector;
            });
        lentByPreview = lent;
        return result;
    }

    /// <summary>
    /// <see cref="DocumentDiagnostics(Document, CancellationToken, out bool)"/>, computed once for
    /// each expansion of the document and kept with it: the Problems panel's pass and every code
    /// action asked on that text read one list.
    /// </summary>
    /// <remarks>
    /// The editor asks for the code actions on every caret move, and each request ran the whole
    /// pass again — every validator and a collect of its own, with no token and no yield to the
    /// preview (review of session 1291). The list is a function of the expanded tree: the document's
    /// text is its prefix, and <see cref="ExpandUsings(Document, Uri)"/> hands out the same tree
    /// while the text and what its <c>using</c>s read stand, so the tree is the key and an entry
    /// dies with the edit. A pass given up (cancelled) keeps nothing.
    /// </remarks>
    internal IReadOnlyList<CoreDiagnostic> DocumentDiagnosticsOnce(Document doc, CancellationToken token)
    {
        var (tree, _) = ExpandUsings(doc, doc.Uri);
        if (DiagnosticsOfTree.TryGetValue(tree, out var kept))
            return kept;
        var result = DocumentDiagnostics(doc, token, out _);
        // Kept only when the pass read this very expansion (an include saved meanwhile is another).
        if (ReferenceEquals(ExpandUsings(doc, doc.Uri).Tree, tree))
            DiagnosticsOfTree.AddOrUpdate(tree, result);
        return result;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxTree, IReadOnlyList<CoreDiagnostic>>
        DiagnosticsOfTree = new();

    /// <summary>How many times <see cref="DocumentDiagnostics(Document, CancellationToken, out bool)"/>
    /// has run its pass — for tests.</summary>
    internal int DiagnosticPasses => System.Threading.Volatile.Read(ref _diagnosticPasses);

    private int _diagnosticPasses;

    /// <summary>The open document at <paramref name="uri"/>, or null. For tests.</summary>
    internal Document? DocumentAt(Uri uri) => _documentManager.GetDocument(uri);

    /// <summary>What a request that names a document answers in its <c>Error</c> when the server
    /// does not hold that document. ONE spelling, because the editor reads it: the preview asks
    /// again while a just-opened file is not registered yet, and knows that window by this very
    /// text (<c>editors/vscode/src/extension.ts</c>, <c>response.Error === 'Document not found'</c>
    /// — <c>DocumentNotFoundTests</c> reads both sides). Five commands wrote it with a full stop
    /// until session 1295. A request whose answer has no <c>Error</c> (the caret's questions:
    /// its place, its bar, its status line) answers the empty answer instead — "nothing here" is
    /// all its caller can use.</summary>
    internal const string DocumentNotFound = "Document not found";

    /// <summary>
    /// The preview's collect of <paramref name="tree"/> for the document at
    /// <paramref name="uri"/>, from whichever of its render sessions collected exactly
    /// that tree against the first render block (<see cref="IncrementalCompiler.CollectFor"/>);
    /// null when none did. Reads the sessions without their gates: a slot's session is a
    /// reference swapped whole under its gate, and a lent collect is immutable from the
    /// moment it is lent, so the borrower never waits behind a running render.
    /// </summary>
    /// <remarks>
    /// The second computation a keystroke starts used to begin with a full collect of the
    /// whole book on top of the one the preview had just made (HANDOFF §2 R13⒜, session
    /// 399); on a settled keystroke the two collects are now one, and
    /// <c>PreviewCollectSharingTests</c> hold where the pass borrows and where it must not.
    /// The pass still walks the tree once per validator (R13⒜'s other half); this lends
    /// only the collect.
    /// </remarks>
    internal LilySharp.Core.Svg.Collector.MeasureCollector? PreviewCollectFor(Uri uri, SyntaxTree tree)
    {
        System.Collections.Generic.List<IncrementalCompiler>? sessions = null;
        lock (_svgSessionLock)
        {
            foreach (var (key, slot) in _svgSessions)
            {
                if (key.Uri != uri)
                    continue;
                var session = System.Threading.Volatile.Read(ref slot.Session);
                if (session != null)
                    (sessions ??= new()).Add(session);
            }
        }
        if (sessions == null)
            return null;
        foreach (var session in sessions)
        {
            var collector = session.CollectFor(tree);
            if (collector != null)
                return collector;
        }
        return null;
    }
}
