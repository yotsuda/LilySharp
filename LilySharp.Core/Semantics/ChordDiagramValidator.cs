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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Which tuning a chord diagram draws on in each score of a file — read from the tree, the way
/// the page reads it (<see cref="LayoutPlanReader.Resolve"/>, <see cref="RenderSpecParser"/>):
/// the score's <c>chordDiagrams</c> word, else the fretted instrument of the part the chord
/// belongs to, else the guitar (<see cref="ChordDiagramsKey.Resolve"/>, owner's decision
/// 2026-09-28).
/// </summary>
/// <remarks>
/// <para>
/// The part of an <c>@chord</c> is the part whose note carries it. The part of a
/// <c>chords</c> row is the staff the row stands DIRECTLY above in the score's list — the
/// score is a vertical stack of bands and a row glues down (GRAMMAR; the old
/// <c>staff X with chords prog</c> clause is gone): an interior row the spec folds into the
/// staff below it (<c>StaffSpec.WithChords</c>), else the staff-like item right after a leading
/// row. A lead-sheet row, or a row below the last staff, stands over nothing: the guitar. A
/// tab staff answers its own tuning (the one it frets on); any other staff its part's fretted
/// tuning (a group, its first member's).
/// </para>
/// <para>
/// The twin, MusicXML, the editor's step and hover ask here; the page resolves the same rule
/// from the part it is collecting (<c>MeasureCollector</c>, <c>ChordNameCollector</c>) through
/// the same helpers (<see cref="StaffWord"/>, <see cref="RowStaffWord"/>).
/// </para>
/// </remarks>
public static class ChordDiagramScores
{
    /// <summary>One score: its node (null for a file with no <c>score</c>, which renders by the
    /// file's own layout), its <c>chordDiagrams</c> word as written (null when absent), its
    /// spec (null with no node), the names of the <c>chords</c> rows it places (over a
    /// staff or as a row), and whether it draws EVERY chord name (<c>chordDiagrams … all</c>,
    /// <see cref="LayoutPlan.ChordDiagramsAll"/>).</summary>
    public sealed record Score(RenderDeclarationSyntax? Node, string? LayoutWord,
        RenderSpec? Spec, ImmutableArray<string> RowNames, bool All = false)
    {
        /// <summary>The tuning word a diagram of a part with fretted tuning
        /// <paramref name="partWord"/> draws on in this score; null under <c>none</c>.</summary>
        public string? TuningWordFor(string? partWord) => ChordDiagramsKey.ResolveWord(LayoutWord, partWord);

        /// <summary>Whether this score draws <paramref name="part"/> (a file with no score draws
        /// every part).</summary>
        public bool RendersPart(string part) => Spec?.BindsVoice(part) ?? Node == null;
    }

    /// <summary>Every score of the file, in document order.</summary>
    public static IReadOnlyList<Score> Of(SyntaxNode root)
    {
        var scores = new List<Score>();
        foreach (var render in TopLevelNodes.OfRoot<RenderDeclarationSyntax>(root))
        {
            var plan = LayoutPlanReader.Resolve(root, render);
            var spec = RenderSpecParser.Parse(render);
            scores.Add(new Score(render, plan.ChordDiagrams, spec, spec == null ? [] : [.. RowNamesOf(spec)],
                plan.ChordDiagramsAll));
        }
        if (scores.Count == 0)
        {
            var plan = LayoutPlanReader.Resolve(root, null);
            scores.Add(new Score(null, plan.ChordDiagrams, null, [], plan.ChordDiagramsAll));
        }
        return scores;
    }

    /// <summary>The <c>chords</c> rows a spec places: as rows, and attached to staves and tabs
    /// (a group's members included).</summary>
    private static IEnumerable<string> RowNamesOf(RenderSpec spec)
        => spec.GetVoiceBindings().Select(b => b.WithChords).OfType<string>()
            .Concat(spec.Items.OfType<TabStaffSpec>().Select(t => t.WithChords).OfType<string>())
            .Concat(spec.Items.OfType<ChordRowSpec>().Select(r => r.PartName))
            .Distinct(System.StringComparer.Ordinal);

    // ---------------------------------------------------------------- the part's instrument

    /// <summary>The fretted tuning word of the part named <paramref name="partName"/>
    /// (<see cref="PartHeaderDefaults.FrettedTuningWord"/>), null for none.</summary>
    public static string? FrettedWordOfPart(SyntaxNode root, string partName)
        => PartHeaderDefaults.Read(ConcertPitch.FindPart(root, partName)).FrettedTuningWord;

    /// <summary>The part <paramref name="node"/> is written in, from the SYNTAX: the enclosing
    /// <c>part</c> declaration, or the part a section's block names; null outside every part.</summary>
    public static string? PartNameOf(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            if (p is PartDeclarationSyntax declared)
                return declared.Name.Text;
            if (p is PartBlockSyntax block)
                return block.Name;
        }
        return null;
    }

    /// <summary>
    /// The fretted tuning word of the part <paramref name="node"/> is written in; outside every
    /// part (a phrase), the one word all the file's parts share, else null.
    /// </summary>
    public static string? FrettedWordOfNode(SyntaxNode node)
    {
        var root = RootOf(node);
        if (PartNameOf(node) is { } part)
            return FrettedWordOfPart(root, part);
        var words = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .Select(pd => PartHeaderDefaults.Read(pd).FrettedTuningWord).Distinct().ToList();
        return words.Count == 1 ? words[0] : null;
    }

    /// <summary>
    /// The fretted tuning word of the staff <paramref name="voiceName"/> draws — a tab staff of
    /// it that attaches <paramref name="rowName"/> answers its own tuning, any other its part's.
    /// </summary>
    public static string? StaffWord(SyntaxNode root, RenderSpec? spec, string voiceName, string? rowName)
    {
        if (spec?.Items.OfType<TabStaffSpec>()
                .FirstOrDefault(t => t.Staff.VoiceName == voiceName && t.WithChords == rowName) is { } tab)
            return TabWord(root, tab);
        return FrettedWordOfPart(root, voiceName);
    }

    /// <summary>
    /// The fretted tuning word of the staff the row <paramref name="rowName"/> is FIRST placed
    /// over in <paramref name="spec"/> (see the class remarks); null when it stands over no
    /// staff (a lead sheet) or over one whose part frets nothing.
    /// </summary>
    public static string? RowStaffWord(SyntaxNode root, RenderSpec spec, string rowName)
    {
        foreach (var (voice, withChords, _, _, _) in spec.GetVoiceBindings())
            if (withChords == rowName)
                return StaffWord(root, spec, voice, rowName);
        var items = spec.Items;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is TabStaffSpec { WithChords: { } w } attachedTab && w == rowName)
                return TabWord(root, attachedTab);
            if (items[i] is ChordRowSpec row && row.PartName == rowName)
                return i + 1 < items.Length ? WordUnder(root, items[i + 1]) : null;
        }
        return null;
    }

    /// <summary>The fretted tuning word of a staff-like item: a tab's own tuning, a staff's
    /// part's, a group's first member's, a condensed or combined staff's first part's.</summary>
    private static string? WordUnder(SyntaxNode root, RenderItemSpec item) => item switch
    {
        TabStaffSpec tab => TabWord(root, tab),
        SingleStaffSpec s => FrettedWordOfPart(root, s.Staff.VoiceName),
        GrandStaffRenderSpec g => g.GrandStaff.Members.Length > 0 ? WordUnder(root, g.GrandStaff.Members[0]) : null,
        CondensedStaffSpec c => c.PartNames.Length > 0 ? FrettedWordOfPart(root, c.PartNames[0]) : null,
        CombinedStaffSpec c => c.PartNames.Length > 0 ? FrettedWordOfPart(root, c.PartNames[0]) : null,
        _ => null,
    };

    /// <summary>A tab staff's tuning as a word: its part's when that is the tuning it frets on,
    /// else the first word naming it (a <c>tab bass m</c> override).</summary>
    private static string TabWord(SyntaxNode root, TabStaffSpec tab)
    {
        var part = PartHeaderDefaults.Read(ConcertPitch.FindPart(root, tab.Staff.VoiceName));
        if (part.FrettedTuningWord is { } w && Tablature.Tunings.Parse(w) == tab.Tuning)
            return w;
        return Tablature.Tunings.Names.First(n => Tablature.Tunings.Parse(n) == tab.Tuning);
    }

    // ---------------------------------------------------------------- per chord

    /// <summary>
    /// Each score that draws the part an <c>@chord</c> is written in (every score when none does,
    /// or when the part is not the tree's to say), in document order, with the tuning word its
    /// diagram draws on there — null under <c>chordDiagrams none</c>.
    /// </summary>
    public static IReadOnlyList<(Score Score, string? Word)> TuningsOfMark(MusicMarkSyntax mark)
        => TuningsOfMarkIn(mark, Of(RootOf(mark)));

    /// <summary><see cref="TuningsOfMark"/> over the file's scores read once
    /// (<see cref="Of"/>) — for a caller that asks for every mark.</summary>
    public static IReadOnlyList<(Score Score, string? Word)> TuningsOfMarkIn(MusicMarkSyntax mark,
        IReadOnlyList<Score> scores)
    {
        string? partWord = FrettedWordOfNode(mark);
        var rendering = PartNameOf(mark) is { } part ? scores.Where(s => s.RendersPart(part)).ToList() : [];
        if (rendering.Count == 0)
            rendering = [.. scores];
        return [.. rendering.Select(s => (s, s.TuningWordFor(partWord)))];
    }

    /// <summary>Each score that places the <c>chords</c> row <paramref name="rowName"/>, in
    /// document order, with the tuning word its diagrams draw on there (the staff it is first
    /// placed over — <see cref="RowStaffWord"/>) — null under <c>chordDiagrams none</c>.</summary>
    public static IReadOnlyList<(Score Score, string? Word)> TuningsOfRow(SyntaxNode root, string rowName)
        => TuningsOfRowIn(root, rowName, Of(root));

    /// <summary><see cref="TuningsOfRow"/> over the file's scores read once.</summary>
    public static IReadOnlyList<(Score Score, string? Word)> TuningsOfRowIn(SyntaxNode root, string rowName,
        IReadOnlyList<Score> scores)
        => [.. scores.Where(s => s.Spec != null && s.RowNames.Contains(rowName))
            .Select(s => (s, s.TuningWordFor(RowStaffWord(root, s.Spec!, rowName))))];

    /// <summary>The <c>chords</c> block an entry is written in, or null.</summary>
    public static ChordPartBlockSyntax? BlockOf(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is ChordPartBlockSyntax block)
                return block;
        return null;
    }

    private static SyntaxNode RootOf(SyntaxNode node)
    {
        while (node.Parent != null)
            node = node.Parent;
        return node;
    }

    /// <summary>The shapes a <c>chords</c> row entry writes (<c>F(133211 2010)</c>), and what
    /// is wrong with them by word index into <see cref="ChordEntrySyntax.ShapeWords"/>.</summary>
    public static (ImmutableArray<WrittenShape> Shapes, IReadOnlyList<ChordShapes.Problem> Problems)
        ShapesOf(ChordEntrySyntax entry)
    {
        var words = entry.ShapeWords;
        if (words.Count == 0)
            return ([], []);
        var problems = ChordShapes.ParseWords([.. words.Select(w => w.Text)], entry.SymbolText, out var shapes);
        return (shapes, problems);
    }
}

/// <summary>
/// The chord-diagram warnings (LYS1038) a <c>chords</c> row's written shapes earn — a word
/// that is not a shape or a tuning, a shape of the wrong length, two unnamed shapes of one
/// length, a tuning named twice — and (LYS1039) a written shape that disagrees with its
/// symbol, in a row or an <c>@chord</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A WRITTEN SHAPE IS CHECKED AGAINST ITS SYMBOL (owner's decision 2026-09-28, REVERSING
/// the same day's "the shape is the writer's call, no warning second-guesses it"): a shape
/// that sounds a note outside the chord or lacks a required tone (not the root, not the perfect
/// fifth; the bass is not checked — the owner's second decision that day) is warned once (<see cref="ChordShapes.Mismatch"/>), naming the chord it does play when the
/// recognizer names one. Each shape is checked on the tuning it is ROUTED to
/// (<see cref="ChordShapes.WrittenFor"/>) in each score that draws it
/// (<see cref="ChordDiagramScores"/>: layout, else the part's fretted instrument, else the
/// guitar); identical warnings from several scores are said once. A shape routed to no tuning
/// in any score — a file written for two instruments carries shapes each score leaves alone —
/// is not checked, nor is a symbol-less <c>@chord(x32010)</c> (its name comes from the shape)
/// or an <c>@diagram</c> (it names nothing).
/// </para>
/// <para>
/// ⚠️ THE "NO SHAPE ON THIS TUNING" WARNING IS ONLY FOR <c>chordDiagrams … all</c> SCORES
/// (owner's decisions 2026-09-28): elsewhere a chord name with no written shape draws no
/// diagram by design, so there is nothing to report. In an <c>all</c> score every name is
/// meant to draw, so a chord with NO shape on the score's tuning — none written, no default
/// (<see cref="ChordShapes.Default"/>: an eleventh on the ukulele) — is warned ONCE per symbol
/// and tuning in the file, at its first appearance, naming the fix (write the shape).
/// (Commit 9cf95fab drew a default for every name and warned in every score.)
/// </para>
/// <para>
/// Lily#'s choices for what the tree cannot say: a ROMAN degree is not checked — neither its
/// shape against its chord nor its "no shape" — because its chord depends on the key at its
/// bar, which the page resolves and the tree does not; nor are a bare <c>@chord</c> and a
/// symbol-less shape (their names come from notes the page reads). The <c>@chord</c> half reads
/// the scores drawing the mark's part (<see cref="ChordDiagramScores.TuningsOfMark"/>); a row is
/// checked against the scores that place it.
/// </para>
/// <para>
/// The <c>@chord</c> words' own problems are <see cref="AnnotationNameValidator"/>'s (they
/// share its one-warning-per-mark rule).
/// </para>
/// </remarks>
internal sealed class ChordDiagramValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var entries = root.KindSites(SyntaxKind.ChordEntry).OfType<ChordEntrySyntax>().ToList();
        foreach (var entry in entries)
        {
            var words = entry.ShapeWords;
            if (words.Count == 0)
                continue;
            foreach (var p in ChordDiagramScores.ShapesOf(entry).Problems)
                _diagnostics.Warning(words[p.WordIndex].Span, DiagnosticCodes.ChordDiagramNotDrawn, p.Message);
        }

        var fileScores = ChordDiagramScores.Of(root);
        CheckShapesAgainstSymbols(root, entries, fileScores);

        // ⑵ In a `chordDiagrams … all` score: the chords with no shape on its tuning.
        if (!fileScores.Any(s => s.All))
            return;
        var warned = new HashSet<(string Symbol, TuningType Tuning)>();
        void Check(string symbol, ChordStructure chord, IReadOnlyList<WrittenShape> shapes,
            TextSpan span, IReadOnlyList<(ChordDiagramScores.Score Score, string? Word)> scores)
        {
            foreach (var (score, word) in scores)
            {
                if (!score.All || word == null)
                    continue;
                var tuning = Tablature.Tunings.Parse(word);
                if (ChordShapes.Drawn(tuning, shapes, all: true, chord) != null || !warned.Add((symbol, tuning)))
                    continue;
                var strings = Tablature.Tunings.GetTuning(tuning);
                _diagnostics.Warning(span, DiagnosticCodes.ChordDiagramNotDrawn,
                    ChordShapes.NoShape(symbol, word, ChordVoicings.IsGuitarType(strings), strings.Length));
            }
        }

        // Document order, rows and marks interleaved, so "its first appearance" is the first
        // in the file whichever kind it is.
        var sites = entries.Cast<SyntaxNode>()
            .Concat(root.KindSites(SyntaxKind.MusicMark).OfType<MusicMarkSyntax>())
            .OrderBy(n => n.SourceStart);
        foreach (var site in sites)
        {
            if (site is ChordEntrySyntax entry)
            {
                if (ChordDiagramScores.BlockOf(entry)?.PartName is not { } rowName
                    || !ChordStructure.TryParseChordEntry(entry.SymbolText, out var chord))
                    continue;
                Check(entry.SymbolText, chord, ChordDiagramScores.ShapesOf(entry).Shapes, entry.Span,
                    ChordDiagramScores.TuningsOfRowIn(root, rowName, fileScores));
            }
            else if (site is MusicMarkSyntax mark
                     && ChordAnnotation.Of(mark) is { Symbol: { } symbol, Structure: { } structure } words)
                Check(symbol, structure, words.Shapes, mark.Span, ChordDiagramScores.TuningsOfMarkIn(mark, fileScores));
        }
    }

    /// <summary>
    /// ⑶ LYS1039: each written shape of a row entry or an <c>@chord</c> with a symbol, checked
    /// against that symbol on the tuning it is routed to in each score drawing it (the class
    /// remarks); a warning at the shape word, said once however many scores repeat it.
    /// </summary>
    private void CheckShapesAgainstSymbols(SyntaxNode root, IReadOnlyList<ChordEntrySyntax> entries,
        IReadOnlyList<ChordDiagramScores.Score> fileScores)
    {
        var said = new HashSet<(int Start, string Message)>();
        void Check(string symbol, ChordStructure chord, IReadOnlyList<WrittenShape> shapes,
            IReadOnlyList<(string Text, TextSpan Span)> words, bool inRow,
            IReadOnlyList<(ChordDiagramScores.Score Score, string? Word)> scores)
        {
            foreach (var (_, word) in scores)
            {
                if (word == null)
                    continue;
                var tuning = Tablature.Tunings.Parse(word);
                if (ChordShapes.WrittenFor(tuning, shapes) is not { } shape
                    || ChordShapes.Mismatch(ChordShapes.Frets(shape), Tablature.Tunings.GetTuning(tuning), chord)
                        is not { } mismatch)
                    continue;
                string? tuningName = shapes.First(s => s.Shape == shape).TuningName;
                string message = ChordShapes.MismatchMessage(mismatch, shape, tuningName, symbol, inRow);
                var at = words.FirstOrDefault(w => w.Text == shape);
                var span = at.Text == null ? words[0].Span : at.Span;
                if (said.Add((span.Start, message)))
                    _diagnostics.Warning(span, DiagnosticCodes.ChordShapeMismatch, message);
            }
        }

        foreach (var entry in entries)
        {
            var words = entry.ShapeWords;
            if (words.Count == 0 || ChordDiagramScores.BlockOf(entry)?.PartName is not { } rowName
                || !ChordStructure.TryParseChordEntry(entry.SymbolText, out var chord))
                continue;   // a Roman degree does not parse here: its key is the page's
            Check(entry.SymbolText, chord, ChordDiagramScores.ShapesOf(entry).Shapes, words, inRow: true,
                ChordDiagramScores.TuningsOfRowIn(root, rowName, fileScores));
        }
        foreach (var mark in root.KindSites(SyntaxKind.MusicMark).OfType<MusicMarkSyntax>())
        {
            if (ChordAnnotation.Of(mark) is not { Symbol: { } symbol, Structure: { } structure } annotation
                || annotation.Shapes.IsEmpty)
                continue;
            var spans = ChordAnnotation.WordSpans(mark);
            var args = mark.Arguments.Select(a => a.Text).ToList();
            var words = new List<(string Text, TextSpan Span)>();
            for (int i = 1; i < args.Count && i < spans.Count; i++)
                words.Add((args[i], new TextSpan(spans[i].Start, spans[i].End - spans[i].Start)));
            if (words.Count == 0)
                continue;
            Check(symbol, structure, annotation.Shapes, words, inRow: false,
                ChordDiagramScores.TuningsOfMarkIn(mark, fileScores));
        }
    }
}
