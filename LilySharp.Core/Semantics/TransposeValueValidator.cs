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
/// The value of every <c>transpose</c> — the top level's, a part header's, a score header's —
/// is one of its two forms (<see cref="TransposeValue"/>): a pitch with octave marks
/// (<c>transpose bes,</c>) or a number of semitones with an optional <c>sharp</c> / <c>flat</c>
/// (<c>transpose -2</c>, <c>transpose +6 flat</c>). Anything else is refused; a <c>sharp</c> /
/// <c>flat</c> that changes nothing — the key it lands in has no twin — is warned.
/// </summary>
/// <remarks>
/// Until 2026-11-16 only a part header's value was checked (SymbolCaseValidator), and a
/// top-level or score <c>transpose banana</c> moved nothing and said nothing. A number past
/// eleven is the parser's (Parser.ParseTransposeAmount) and not said twice.
/// </remarks>
internal sealed class TransposeValueValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        int? firstKey = null;
        foreach (var prop in tree.GetNodes<PropertyAssignmentSyntax>())
        {
            if (!TransposeValue.IsTranspose(prop))
                continue;
            var tokens = Values(prop);
            if (tokens.Count == 0)
                continue;   // the parser said the value is missing
            switch (TransposeValue.Parse(prop))
            {
                case null:
                    if (tokens.Any(t => t.Kind == SyntaxKind.IntegerLiteral
                                        && (!int.TryParse(t.Text, out int n) || n > FileTranspose.Max)))
                        break;  // past eleven: the parser's message
                    _diagnostics.Error(new TextSpan(tokens[0].Span.Start, tokens[^1].Span.End - tokens[0].Span.Start),
                        DiagnosticCodes.UnknownSymbolCase,
                        $"'{string.Concat(tokens.Select(t => t.Text))}' is not a transpose target. transpose takes a pitch — a "
                        + "letter a–g with an optional is/isis/es/eses and octave marks (e.g. 'transpose d', 'transpose bes,') — "
                        + "or a number of semitones, -11 to +11, with an optional 'sharp' or 'flat' (e.g. 'transpose -2', "
                        + "'transpose +6 flat').");
                    break;
                case TransposeValue.Amount { Spelling: not TransposeSpelling.Auto } amount:
                    firstKey ??= FileTranspose.FirstKeySharps(root);
                    int from = firstKey.Value;
                    if (prop.IsInside<RenderDeclarationSyntax>() && FileTranspose.Read(root) is { } file)
                        from += PitchTransposer.KeySignatureFifthsShift(file.step, file.alt);
                    if (!FileTranspose.HasTwin(from, amount.Semitones))
                    {
                        var word = tokens[^1];
                        _diagnostics.Warning(word.Span, DiagnosticCodes.TransposeSpellingUnused,
                            $"'{word.Text}' changes nothing here: the key {Signed(amount.Semitones)} lands in has one spelling. "
                            + "'sharp' and 'flat' choose between twin keys only — B / C♭, F♯ / G♭, D♭ / C♯ and their minors.");
                    }
                    break;
            }
        }
    }

    private static List<SyntaxTokenNode> Values(PropertyAssignmentSyntax prop)
    {
        var tokens = new List<SyntaxTokenNode>();
        for (int ci = 1; ci < prop.SlotCount; ci++)
            if (prop.GetChild(ci) is SyntaxTokenNode tok && tok.Kind != SyntaxKind.Colon && tok.Text.Length > 0)
                tokens.Add(tok);
        return tokens;
    }

    private static string Signed(int n) => n > 0 ? "+" + n : n.ToString();
}
