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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A strokes part (<c>strokes rhythm { section A { 4 8 8 4 4 | } }</c>, owner's design
/// 2026-11-01) holds strokes alone: slash heads (a bare value is one, its <c>/</c> unwritten —
/// <c>Parser.ParseStrokeOfBareValue</c>), rests, ties, bar lines, repeat marks, tuplets of strokes, and phrases of the same. A
/// pitch written in it — a note, a chord, a drum, a <c>q</c>, a broken chord — is an error
/// (LYS2021) at the item: the part names no instrument and sounds nothing of its own; the chord
/// row that <c>plays</c> it is what sounds.
/// </summary>
internal sealed class StrokesContentValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var strokes = root.DescendantNodes<PartDeclarationSyntax>().Where(p => p.IsStrokes).ToList();
        if (strokes.Count == 0)
            return;
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>().Where(p => p.Parent is CompilationUnitSyntax)
            .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First());
        var said = new HashSet<SyntaxNode>();
        foreach (var part in strokes)
        {
            string name = part.Name.Text;
            var bodies = new List<SyntaxNode>();
            bodies.AddRange(part.DescendantNodes<SectionDeclarationSyntax>());
            bodies.AddRange(root.DescendantNodes<PartBlockSyntax>().Where(b => b.PartName.Text == name));
            var seenPhrases = new HashSet<string>();
            foreach (var body in bodies)
                Check(body, name, phrases, seenPhrases, said);
        }
    }

    private void Check(SyntaxNode body, string part, IReadOnlyDictionary<string, PhraseDeclarationSyntax> phrases,
        HashSet<string> seenPhrases, HashSet<SyntaxNode> said)
    {
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case NoteSyntax or ChordSyntax or DrumNoteSyntax or ChordRepetitionSyntax or ArpeggioSyntax
                    or ChordEntrySyntax or ParallelExpressionSyntax or GraceExpressionSyntax:
                    if (said.Add(node))
                        _diagnostics.Error(node.Span, DiagnosticCodes.StrokesHoldNoPitch,
                            $"'{part}' is a strokes part and holds no pitches: write the strokes as values ('4 8 8 4 4'), "
                            + "a rest, a tie or a bar line — the chords it strums are the chord row's "
                            + "('chords NAME plays " + part + "').");
                    break;
                case VariableReferenceSyntax reference when phrases.TryGetValue(reference.Name.Text, out var phrase)
                    && seenPhrases.Add(reference.Name.Text):
                    Check(phrase.Body, part, phrases, seenPhrases, said);
                    break;
            }
        }
    }
}
