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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS1066: a <c>voice { } { }</c> in a tuplet's body, written there or in a phrase the body names
/// (owner's decision 2026-11-05). The tuplet goes inside each voice instead. The page drew none of
/// such a voice's notes.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: a deliberate difference from LilyPond, which takes <c>&lt;&lt; \\ &gt;&gt;</c>
/// inside <c>\tuplet</c> and draws no number for it.
/// </remarks>
internal sealed class TupletVoiceValidator : ISemanticValidator
{
    private const string Message = "'voice' cannot be written inside a tuplet: write a tuplet inside each voice, "
        + "'voice { tuplet 3/2 { … } } { tuplet 3/2 { … } }'";

    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var tuplets = tree.GetNodes<TupletExpressionSyntax>().ToList();
        if (tuplets.Count == 0)
            return;

        foreach (var voice in tree.GetNodes<ParallelExpressionSyntax>())
            if (InATuplet(voice))
                _diagnostics.Error(voice.Span, DiagnosticCodes.VoiceInTuplet, Message);

        Dictionary<string, MusicBlockSyntax>? phrases = null;
        foreach (var tuplet in tuplets)
            foreach (var reference in tuplet.Body.DescendantNodes<VariableReferenceSyntax>())
            {
                phrases ??= tree.GetNodes<PhraseDeclarationSyntax>()
                    .GroupBy(p => p.Name.Text, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Body, StringComparer.Ordinal);
                if (HoldsAVoice(reference.Name.Text, phrases, new HashSet<string>(StringComparer.Ordinal)))
                    _diagnostics.Error(reference.Span, DiagnosticCodes.VoiceInTuplet, Message);
            }
    }

    private static bool InATuplet(SyntaxNode node)
    {
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
            if (parent is TupletExpressionSyntax)
                return true;
        return false;
    }

    private static bool HoldsAVoice(string name, Dictionary<string, MusicBlockSyntax> phrases, HashSet<string> open)
    {
        if (!open.Add(name) || !phrases.TryGetValue(name, out var body))
            return false;
        if (body.DescendantNodes<ParallelExpressionSyntax>().Any())
            return true;
        return body.DescendantNodes<VariableReferenceSyntax>().Any(r => HoldsAVoice(r.Name.Text, phrases, open));
    }
}
