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
using System.Linq;
using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a <c>chord(SYMBOL SHAPE)</c> item's words earn (owner's decisions 2026-09-28, HANDOFF
/// §2 K5): every shape diagnostic an <c>@chord(…)</c>'s words earn (LYS1038 — a word that is
/// not a shape, a dash error, the case), a symbol Lily# does not know (LYS1020), NO SHAPE the
/// part's tuning can play (LYS1040 — the item is then a spacer), and the shape against its
/// symbol (LYS1039).
/// </summary>
/// <remarks>
/// The part's tuning is read from the tree (<see cref="ShapeChords.PartTuningsOf"/>): in a part,
/// its own; in a phrase, each tuning of the file's parts — a phrase a guitar and a ukulele both
/// play needs a shape for each, and the one missing is said once per tuning. Identical warnings
/// are said once.
/// </remarks>
internal sealed class ShapeChordValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var chord in root.KindSites(SyntaxKind.Chord).OfType<ChordSyntax>())
            if (chord.IsShapeChord)
                Check(chord);
    }

    private void Check(ChordSyntax item)
    {
        var args = item.ShapeArguments;
        var spans = item.ShapeWordSpans;
        TextSpan SpanOf(int word) => word < spans.Count ? spans[word] : item.Span;
        string written = string.Join(" ", args.Select(a => a.Text));
        var words = ShapeChords.Words(item);

        // ⑴ The words, as an @chord's (ChordShapes.ParseWords' messages name each fix).
        foreach (var p in words.Problems)
            _diagnostics.Warning(SpanOf(p.WordIndex), DiagnosticCodes.ChordDiagramNotDrawn, p.Message);
        if (words.QuotedText != null)
        {
            _diagnostics.Warning(SpanOf(0), DiagnosticCodes.ShapeChordNoShape,
                $"chord({written}) takes a chord symbol and a shape, not quoted text - write "
                + "chord(C x32010). It draws and sounds nothing; the item keeps its time as a spacer.");
            return;
        }
        if (words.Symbol is { } unknown && words.Structure == null)
            _diagnostics.Warning(SpanOf(0), DiagnosticCodes.ChordNotRecognized,
                $"'{unknown}' is not a chord symbol Lily# knows (C, Cm7, F#m7-5/C, ...) - the shape "
                + "still sounds, but nothing is checked against the symbol and a bare @chord on "
                + "the item names nothing.");

        // ⑵ A shape the part's tuning can play, and ⑶ the shape against the symbol.
        var said = new HashSet<string>();
        foreach (var part in ShapeChords.PartTuningsOf(item))
        {
            var strings = Tablature.Tunings.GetTuning(part.Tuning);
            if (ShapeChords.ShapeFor(words, part.Tuning) is not { } shape)
            {
                string? example = words.Structure is { } s ? ChordShapes.Default(part.Tuning, s)?.Spelled : null;
                string message = ShapeChords.NoShape(written, words.Symbol, part.Word, strings.Length,
                    !words.Shapes.IsEmpty, example);
                if (said.Add(message))
                    _diagnostics.Warning(new TextSpan(item.SourceStart, "chord".Length),
                        DiagnosticCodes.ShapeChordNoShape, message);
                continue;
            }
            if (words.Symbol is not { } symbol || words.Structure is not { } structure
                || ChordShapes.Mismatch(ChordShapes.Frets(shape), strings, structure) is not { } mismatch)
                continue;
            string? tuningName = words.Shapes.First(w => w.Shape == shape).TuningName;
            string mismatchMessage = ChordShapes.MismatchMessage(mismatch, shape, tuningName, symbol,
                inRow: false, item: true);
            int at = args.Select((a, i) => (a, i)).FirstOrDefault(x => x.a.Text == shape).i;
            if (said.Add(mismatchMessage))
                _diagnostics.Warning(SpanOf(at), DiagnosticCodes.ChordShapeMismatch, mismatchMessage);
        }
    }
}
