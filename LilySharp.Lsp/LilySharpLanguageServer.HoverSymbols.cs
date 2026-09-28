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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    // ========== Hover ==========

    [JsonRpcMethod(Methods.TextDocumentHoverName, UseSingleObjectParameterDeserialization = true)]
    public Task<Hover?> HoverAsync(TextDocumentPositionParams @params, CancellationToken token)
        => OffDispatch(() => Hover(@params), token);

    public Hover? Hover(TextDocumentPositionParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return null;

        var offset = GetOffset(doc.Text, @params.Position.Line, @params.Position.Character);
        var node = doc.Tree.FindNode(offset);

        if (node == null)
            return null;

        // An @chord hovers as its diagrams, or with no shape written as the line that says how to
        // add one — checked first, because the mark sits INSIDE a chord (`<c e g>@chord(C)`),
        // which would otherwise answer.
        string? content = null;
        if (ChordDiagramMarkAt(node) is { } diagramMark
            && ChordDiagramHover(diagramMark) is { } diagramHover)
        {
            node = diagramMark;
            content = diagramHover;
        }
        else if (ChordLikeAt(node) is ChordSyntax { IsShapeChord: true } shapeItem)
        {
            node = shapeItem;
            content = ShapeChordHover(shapeItem);
        }
        else
        {
            var chordLike = ChordLikeAt(node);
            if (chordLike != null)
                node = chordLike;
            content = chordLike != null ? ChordHover(doc.Tree, chordLike) : GetHoverContent(node);
            // A chords-row entry adds the diagram each score placing its row draws (or the add hint).
            if (chordLike is ChordEntrySyntax entry && RowDiagramLines(doc.Tree.GetRoot(), entry) is { } lines)
                content = (content ?? "**Chord symbol**") + "\n\n" + lines;
        }
        if (content == null)
            return null;

        var (startLine, startCol) = GetLineAndColumn(doc.Text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(doc.Text, node.Span.End);

        return new Hover
        {
            Contents = new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = content
            },
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }

    // The hover text lives in LanguageReference (ONE HOME with the signature
    // table, so the two features cannot drift apart on the same construct).
    private static string? GetHoverContent(SyntaxNode node)
        => LanguageReference.Hover(node);

    /// <summary>The <c>@chord(…)</c> mark the hovered node is or sits inside; else null.</summary>
    private static MusicMarkSyntax? ChordDiagramMarkAt(SyntaxNode node)
    {
        for (var n = node; n != null; n = n.Parent)
            if (n is MusicMarkSyntax mark)
                return ChordAnnotation.Of(mark) is { IsBare: false, QuotedText: null } ? mark : null;
        return null;
    }

    /// <summary>
    /// An <c>@chord</c>'s chord-diagram hover. With NO shape written — so no diagram draws
    /// (owner's decision 2026-09-28) — one line that says how to add one and what it would be:
    /// <c>Ctrl+Shift+↑ adds a chord diagram (guitar: 320003)</c> (<see cref="AddHint"/>). With a
    /// shape written: the name, then the shape each tuning its scores draw on shows —
    /// <c>**Chord diagram** `C`</c>, <c>guitar: `x32010` (written) — shape 1 of 12</c>,
    /// <c>ukulele: no diagram</c> — or why none is.
    /// </summary>
    /// <remarks>
    /// The tunings are the owner's rule per score (<see cref="ChordDiagramScores.TuningsOfMark"/>:
    /// the layout's <c>chordDiagrams</c>, else the part's fretted instrument, else the guitar).
    /// The line of the tuning the step walks (<see cref="NoteStepper.StepTuning"/>) also says
    /// where the shape stands — "shape 3 of 21 (34 with stretch)", the normal count and the
    /// stretch-inclusive one, since a hover carries no setting (owner's decision 2026-09-28,
    /// lilysharp.chordShapes.includeStretch). A frets-10-and-up shape spells in the compact
    /// form, a '-' on each side of each two-digit fret (<c>8-10-10-988</c>; a written full-dash
    /// <c>8-x-x-8-8-11</c> shows as <c>8xx88-11</c>).
    /// </remarks>
    internal static string? ChordDiagramHover(MusicMarkSyntax mark)
    {
        if (ChordAnnotation.Of(mark) is not { IsBare: false, QuotedText: null } words)
            return null;
        if (!words.Problems.IsEmpty)
            return "**Chord diagram** — " + words.Problems[0].Message;
        var scores = ChordDiagramScores.TuningsOfMark(mark);
        // A name alone: the add hint — save where a score draws every chord (`chordDiagrams …
        // all`) or its layout table lists this one, which shows the shape it draws instead
        // (owner's decision 2026-09-28; the table 2026-09-29).
        if (words.Shapes.IsEmpty && !NameAloneDraws(mark, scores, words.Structure))
            return words.Structure is { } bare ? AddHint(mark, bare) : null;
        if (words.Shapes.IsEmpty && words.Structure == null)
            return null;

        var head = new StringBuilder("**Chord diagram**");
        if (words.Symbol != null)
            head.Append(" `").Append(words.Symbol).Append('`');
        return DiagramLines(mark, scores, words.Shapes, words.Structure)
            is { } lines ? head + "\n\n" + lines : null;
    }

    /// <summary>
    /// A <c>chord(…)</c> item's hover (owner's decision 2026-09-28): the notes it sounds on each
    /// tuning of the parts that play it, lowest first — <c>guitar: `x32013` — C3 E3 G3 C4 G4</c> —
    /// with where the shape stands in the step's order, or why it sounds nothing.
    /// </summary>
    internal static string ShapeChordHover(ChordSyntax item)
    {
        var words = ShapeChords.Words(item);
        var head = new StringBuilder("**Chord from a shape**");
        if (words.Symbol != null)
            head.Append(" `").Append(words.Symbol).Append('`');
        if (!words.Problems.IsEmpty)
            return head + " — " + words.Problems[0].Message;
        var lines = new List<string>();
        // A capo raises every string by its fret (the first score's capo, as the step reads it).
        int capo = NoteStepper.CapoOf(item);
        foreach (var part in ShapeChords.PartTuningsOf(item))
        {
            var notes = ShapeChords.Notes(words, part.Tuning, part.SoundingShift - capo, keySharps: 0);
            if (ShapeChords.ShapeFor(words, part.Tuning) is not { } shape || notes.IsEmpty)
            {
                lines.Add($"{part.Word}: no shape for its {LilySharp.Core.Tablature.Tunings.GetStringCount(part.Tuning)} "
                    + "strings - a spacer, nothing sounds");
                continue;
            }
            string spelled = ChordVoicings.Spell(ChordShapes.Frets(shape));
            string place = words.Structure is { } chord
                && NoteStepper.ShapePlace(item, chord, ChordShapes.Frets(shape)) is { } p ? $" — {p}" : "";
            lines.Add($"{part.Word}: `{spelled}` — "
                + string.Join("  ", ShapeChords.Ascending(notes).Select(n => PitchGlyphs(n.SoundingName)))
                + place);
        }
        return head + "\n\n" + string.Join("  \n", lines.Distinct(StringComparer.Ordinal));
    }

    /// <summary>Whether some score of <paramref name="scores"/> draws a diagram for the chord's
    /// NAME ALONE at <paramref name="site"/> — it writes <c>chordDiagrams … all</c>, or its
    /// layout table lists <paramref name="chord"/> (<see cref="ChordShapeTable"/>) — then a name
    /// alone has a diagram to show instead of the add hint.</summary>
    private static bool NameAloneDraws(SyntaxNode site,
        IReadOnlyList<(ChordDiagramScores.Score Score, string? Word)> scores, ChordStructure? chord)
        => scores.Any(s => s.Word != null
            && (s.Score.All
                || (chord != null && s.Score.Table != null
                    && ChordShapes.Drawn(LilySharp.Core.Tablature.Tunings.Parse(s.Word), [], all: false, chord,
                        s.Score.Table, ChordDiagramScores.SectionNameOf(site), s.Score.Capo) != null)));

    /// <summary>
    /// The diagram lines of a <c>chords</c> row entry: with no shape written, the add hint
    /// (<see cref="AddHint"/>); with one, the shape each tuning its scores draw the row on shows
    /// (<see cref="ChordDiagramScores.TuningsOfRow"/>) — null when nothing is to be said.
    /// </summary>
    internal static string? RowDiagramLines(SyntaxNode root, ChordEntrySyntax entry)
    {
        var chord = ChordStructure.TryParseChordEntry(entry.SymbolText, out var parsed) ? parsed : null;
        var shapes = ChordDiagramScores.ShapesOf(entry).Shapes;
        var scores = ChordDiagramScores.BlockOf(entry)?.PartName is { } rowName
            ? ChordDiagramScores.TuningsOfRow(root, rowName) : null;
        if (entry.ShapeWords.Count == 0 && (scores == null || !NameAloneDraws(entry, scores, chord) || chord == null))
            return chord != null ? AddHint(entry, chord) : null;
        return scores != null ? DiagramLines(entry, scores, shapes, chord) : null;
    }

    /// <summary>
    /// The discoverability line of a chord with no shape written (owner's decision 2026-09-28):
    /// <c>Ctrl+Shift+↑ adds a chord diagram (guitar: 320003)</c> — the tuning the step writes on
    /// and the default it would write (<see cref="NoteStepper.ShapeOrder"/>'s first); when that
    /// tuning has no shape for the chord, that the shape has to be written by hand. Not where a
    /// score writes <c>chordDiagrams … all</c>: the name draws the default there, and the hover
    /// shows it (<see cref="DiagramLines"/>).
    /// </summary>
    private static string AddHint(SyntaxNode site, ChordStructure chord)
    {
        var (word, _) = NoteStepper.StepTuning(site);
        var (tuning, order) = NoteStepper.ShapeOrder(site, chord, includeStretch: false);
        return order.Count > 0
            ? $"Ctrl+Shift+↑ adds a chord diagram ({word}: {ChordVoicings.Spell(order[0])})"
            : $"No chord diagram: no shape found for this chord on {word} - write one "
              + $"({new string('x', tuning.Count)} with the frets filled in).";
    }

    /// <summary>One line per distinct tuning <paramref name="scores"/> draw on: the written
    /// shape — in a <c>chordDiagrams … all</c> score, else the default it draws
    /// (<c>guitar: `320003` (default) — shape 1 of 12</c>) — or "no diagram"; or why nothing
    /// draws at all (every score writes <c>chordDiagrams none</c>, or none places the row).</summary>
    private static string DiagramLines(SyntaxNode site,
        IReadOnlyList<(ChordDiagramScores.Score Score, string? Word)> scores,
        IReadOnlyList<WrittenShape> shapes, ChordStructure? chord)
    {
        var words = scores.Select(s => s.Word).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (words.Count == 0)
            return scores.Count > 0
                ? "not drawn: every score of this row or part writes `chordDiagrams none`."
                : "not drawn: no score places this row.";
        string stepWord = NoteStepper.StepTuning(site).Word;
        return string.Join("  \n", words.Select(word =>
        {
            // The first score drawing on this tuning says whether a name alone draws there.
            var score = scores.First(s => s.Word == word).Score;
            bool all = score.All;
            var tuning = LilySharp.Core.Tablature.Tunings.Parse(word);
            string? section = score.Table != null ? ChordDiagramScores.SectionNameOf(site) : null;
            if (ChordShapes.Drawn(tuning, shapes, all, chord, score.Table, section, score.Capo) is not { } drawn)
                return chord != null && (all || score.Table?.Find(section, chord, tuning) != null)
                    ? $"{word}: no diagram - no shape on {word} for this chord; write one "
                      + $"({new string('x', LilySharp.Core.Tablature.Tunings.GetStringCount(tuning))} with the frets filled in)"
                    : $"{word}: no diagram";
            string source = drawn.Source switch
            {
                ShapeSource.Written => "written",
                ShapeSource.Layout => "layout",
                _ => "default",
            };
            return $"{word}: `{drawn.Spelled}` ({source})"
                + (word == stepWord && chord != null
                    && NoteStepper.ShapePlace(site, chord, drawn.Frets) is { } place
                      ? $" — {place}" : "");
        }));
    }

    /// <summary>The chord, <c>&lt;&lt; &gt;&gt;</c> arpeggio, <c>q</c> or <c>chords { }</c> entry the
    /// hovered node is (or sits inside — a member pitch hovers as its chord), or null.</summary>
    private static SyntaxNode? ChordLikeAt(SyntaxNode node)
    {
        for (var n = node; n != null; n = n.Parent)
            if (n is ChordSyntax or ArpeggioSyntax or ChordRepetitionSyntax or ChordEntrySyntax)
                return n;
        return null;
    }

    /// <summary>
    /// A chord's hover: the symbol a bare <c>@chord</c> on it would print — the same
    /// reader (MeasureCollector.RecordsChordFacts), so the hover and the page cannot
    /// disagree — its Roman-numeral degree in the key in force, and the pitches it sounds, a
    /// chord's lowest first and an arpeggio's in played order: <c>`Dm/F (IIm/IV)`  F4  A4  D5</c>.
    /// A <c>chords { }</c> entry hovers the same way, with its tones as letters (a symbol
    /// voices no octave): <c>`Dm (IIm)`  D  F  A</c>. No "Chord:" head and no dash (VS Code drew it long and tight against its neighbours) — the symbol says what
    /// it is. The pitches also fill the line: VS Code sizes a hover to at least 150 px, and
    /// the symbol alone left most of it empty. A chord no collect reaches (a phrase no part
    /// plays) keeps the plain construct hover.
    /// </summary>
    private static string? ChordHover(SyntaxTree tree, SyntaxNode chordLike)
    {
        string head = chordLike switch
        {
            ArpeggioSyntax => "**Arpeggio**",
            ChordEntrySyntax => "**Chord symbol**",
            _ => "**Chord**",
        };
        try
        {
            if (FindChordFacts(tree, chordLike) is { } facts)
            {
                var parts = new List<string>();
                if (facts.Symbol != null)
                    // Symbol and degree in ONE code span: outside it the parentheses stood off the
                    // numeral by the span's own padding — "( V/II )" — and two spans set a double
                    // gap between symbol and degree.
                    parts.Add(facts.Roman != null ? $"`{facts.Symbol} ({facts.Roman})`" : $"`{facts.Symbol}`");
                if (facts.Pitches.Length > 0)
                    parts.Add(string.Join(" \u00A0", facts.Pitches.Select(PitchGlyphs)));
                // A space and a no-break space, before the pitches and between them: Markdown
                // folds a run of plain spaces into one, and one alone set them too tight.
                if (parts.Count > 0)
                    return string.Join(" \u00A0", parts);
            }
        }
        catch (Exception)
        {
            // A document the collector cannot walk still hovers as the construct.
        }
        return head;
    }

    /// <summary>The facts a collect records for <paramref name="chordLike"/>. A chord in the
    /// music is collected with only the part it is written in; a <c>chords { }</c> entry is a
    /// row a SCORE places, so each score is collected until one holds it (an unnamed block
    /// inside a part then falls back to that part's collect).</summary>
    private static LilySharp.Core.Svg.Collector.MeasureCollector.ChordHoverFacts? FindChordFacts(
        SyntaxTree tree, SyntaxNode chordLike)
    {
        int key = chordLike.Span.Start;
        if (chordLike is ChordEntrySyntax)
            foreach (var spec in LilySharp.Core.Svg.Collector.RenderSpecParser.FindAll(tree))
            {
                var scoreCollector = new LilySharp.Core.Svg.Collector.MeasureCollector { RecordsChordFacts = true };
                scoreCollector.CollectMultiStaff(tree, spec);
                if (scoreCollector.ChordFacts.TryGetValue(key, out var found))
                    return found;
            }
        string? part = null;
        for (var n = chordLike.Parent; n != null && part == null; n = n.Parent)
            part = n switch
            {
                PartDeclarationSyntax p => p.Name.Text,
                PartBlockSyntax b => b.Name,
                _ => null,
            };
        var collector = new LilySharp.Core.Svg.Collector.MeasureCollector { RecordsChordFacts = true };
        collector.Collect(tree, part);
        return collector.ChordFacts.TryGetValue(key, out var facts) ? facts : null;
    }

    /// <summary>A trace pitch (<c>F#4</c>, <c>Bb3</c>, <c>Cx4</c>, <c>Dbb4</c>) with the
    /// accidental as its sign: F♯4, B♭3, C𝄪4, D𝄫4.</summary>
    private static string PitchGlyphs(string pitch)
    {
        if (pitch.Length < 2)
            return pitch;
        string rest = pitch.Substring(1);
        string sign = rest.StartsWith("bb", StringComparison.Ordinal) ? "𝄫"
            : rest.StartsWith('b') ? "♭"
            : rest.StartsWith('#') ? "♯"
            : rest.StartsWith('x') ? "𝄪"
            : "";
        int skip = sign switch { "𝄫" => 2, "" => 0, _ => 1 };
        return pitch[0] + sign + rest.Substring(skip);
    }

    // Delegate to the single, correct line/character -> offset conversion in
    // DocumentManager: it handles \n, \r\n AND lone \r line breaks and clamps the
    // character to the END OF ITS LINE (not just the text length), so an over-large
    // character no longer walks into following lines and resolves the wrong node.
    private static int GetOffset(string text, int line, int character)
        => DocumentManager.GetOffset(text, new Position { Line = line, Character = character });

    // ========== Document Symbols ==========

    [JsonRpcMethod(Methods.TextDocumentDocumentSymbolName, UseSingleObjectParameterDeserialization = true)]
    public Task<DocumentSymbol[]?> DocumentSymbolAsync(DocumentSymbolParams @params, CancellationToken token)
        => OffDispatch(() => DocumentSymbol(@params), token);

    public DocumentSymbol[]? DocumentSymbol(DocumentSymbolParams @params)
    {
        var uri = @params.TextDocument.Uri;
        var doc = _documentManager.GetDocument(uri);
        if (doc == null) return null;

        var symbols = new List<DocumentSymbol>();
        CollectSymbols(doc.Tree.GetRoot(), doc.Text, symbols);
        return symbols.ToArray();
    }

    private void CollectSymbols(SyntaxNode node, string text, List<DocumentSymbol> symbols)
    {
        var symbol = CreateSymbol(node, text);
        if (symbol != null)
        {
            // Collect children
            var children = new List<DocumentSymbol>();
            for (int i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null && child is not SyntaxTokenNode)
                    CollectSymbols(child, text, children);
            }
            if (children.Count > 0)
            {
                symbol.Children = children.ToArray();
            }
            symbols.Add(symbol);
        }
        else
        {
            // No symbol for this node, but check children
            for (int i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetChild(i);
                if (child != null && child is not SyntaxTokenNode)
                    CollectSymbols(child, text, symbols);
            }
        }
    }

    // The score's name (the quoted string right after `score`), or plain "score"
    // when unnamed. Child 1 is either the name token or the opening brace —
    // mirrors RenderSpecParser's name extraction.
    private static string RenderSymbolName(RenderDeclarationSyntax render) =>
        render.GetChild(1) is SyntaxTokenNode nameTok && nameTok.Kind != SyntaxKind.OpenBrace
            ? $"score {nameTok.Text}"
            : "score";

    private DocumentSymbol? CreateSymbol(SyntaxNode node, string text)
    {
        var (name, kind) = node switch
        {
            PartDeclarationSyntax part => (GetPartName(part), SymbolKind.Class),

            VariableDeclarationSyntax variable => (variable.Name.Text, SymbolKind.Variable),
            PhraseDeclarationSyntax phrase => ($"phrase {phrase.Name.Text}", SymbolKind.Function),
            SectionDeclarationSyntax section => ($"section {section.SectionName}", SymbolKind.Namespace),
            FormDeclarationSyntax => ("form", SymbolKind.Struct),
            RenderDeclarationSyntax render => (RenderSymbolName(render), SymbolKind.Module),
            RepeatExpressionSyntax repeat => ($"repeat {repeat.Count.Text}x", SymbolKind.Operator),
            // Tuplets and voice-parallel blocks are inline music constructs, not
            // navigation landmarks — emitting one per triplet floods the outline.
            KeySignatureSyntax key => ($"key {key.Pitch?.PitchName} {(key.IsMajor ? "major" : "minor")}", SymbolKind.Key),
            ClefDeclarationSyntax clef => ($"clef {clef.ClefName.Text}", SymbolKind.Key),
            LyricsBlockSyntax => ("lyrics", SymbolKind.String),
            OverrideDeclarationSyntax ovr => ($"override {ovr.GrobName.Text}.{ovr.PropertyName.Text}", SymbolKind.Property),
            // Header landmarks: title/composer (and any other metadata), time, tempo.
            MetadataDeclarationSyntax meta => (NodeText(meta, text), SymbolKind.String),
            TimeSignatureSyntax time => (NodeText(time, text), SymbolKind.Key),
            TempoDeclarationSyntax tempo => (NodeText(tempo, text), SymbolKind.Key),
            _ => (null, SymbolKind.Null)
        };

        if (name == null) return null;

        // A part's human-readable instrument name (the display label if present,
        // else the preset) shows dimmed beside the identifier via the detail field.
        string? detail = node is PartDeclarationSyntax partNode ? GetPartInstrument(partNode) : null;

        var (startLine, startCol) = GetLineAndColumn(text, node.Span.Start);
        var (endLine, endCol) = GetLineAndColumn(text, node.Span.End);

        return new DocumentSymbol
        {
            Name = name,
            Detail = detail,
            Kind = kind,
            Range = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            },
            SelectionRange = new LspRange
            {
                Start = new Position(startLine, startCol),
                End = new Position(endLine, endCol)
            }
        };
    }

    // Single-line source text of a node (whitespace-collapsed), used for header
    // landmarks (title / composer / time / tempo) shown verbatim in the outline.
    private static string NodeText(SyntaxNode node, string text)
    {
        // Start at the FIRST TOKEN's span, not the node's: a composite node reports
        // no leading trivia (LeadingTrivia => null), so its Span begins at any
        // leading comment above it — the first token's span correctly excludes it.
        int start = node.GetChild(0)?.Span.Start ?? node.Span.Start;
        var raw = text.Substring(start, node.Span.End - start);
        return WhitespaceRunRegex().Replace(raw.Trim(), " ");
    }

    // A part's instrument label for the outline detail: the quoted display name
    // (`instrument violin "1st Violin"` → "1st Violin") if present, else the
    // preset (`instrument piano-right` → piano-right), else null.
    private static string? GetPartInstrument(PartDeclarationSyntax part)
    {
        for (int i = 0; i < part.SlotCount; i++)
        {
            if (part.GetChild(i) is PropertyAssignmentSyntax prop
                && prop.NameToken.Kind == SyntaxKind.InstrumentKeyword)
            {
                var tokens = new List<SyntaxTokenNode>();
                for (int j = 2; j < prop.SlotCount; j++)
                    if (prop.GetChild(j) is SyntaxTokenNode t) tokens.Add(t);
                var label = tokens.FirstOrDefault(t => t.Kind == SyntaxKind.StringLiteral);
                if (label != null) return label.Text;          // quoted display name
                var preset = string.Concat(tokens.Select(t => t.Text));
                return string.IsNullOrWhiteSpace(preset) ? null : preset;
            }
        }
        return null;
    }

    private static string GetPartName(PartDeclarationSyntax part)
    {
        // The name is child 1 (keyword, name, ...). It may be a clef-word keyword
        // (`part bass`/`treble`), so take child 1 directly rather than scanning for
        // the first Identifier — that scan would skip a keyword name and wrongly
        // return the instrument identifier deeper in the body.
        if (part.GetChild(1) is SyntaxTokenNode name
            && name.Kind != SyntaxKind.OpenBrace
            && !string.IsNullOrWhiteSpace(name.Text))
            return $"part {name.Text.Trim('"')}";
        return "part";
    }

}
