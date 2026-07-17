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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Enforces that a part's header symbols are written in their canonical case.
/// Symbols are case-sensitive — <c>Treble</c> is a different (and unknown) symbol
/// from <c>treble</c> — so a wrong-case or unknown property name, or clef /
/// instrument-preset / tuning value, is an error rather than silently falling back
/// to a default. Free-text values (a quoted <c>"…"</c> name) are not symbols and are
/// left alone.
/// </summary>
internal sealed class SymbolCaseValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    private static readonly HashSet<string> PropertyNames = new(StringComparer.Ordinal)
    {
        "clef", "instrument", "transpose", "transposition", "tuning",
        "octave", "removeEmpty", "lines",
    };

    private static readonly HashSet<string> ClefValues = new(StringComparer.Ordinal)
    {
        "treble", "bass", "alto", "tenor", "treble_8", "treble^8",
        "soprano", "mezzosoprano", "baritone", "bass_8", "percussion",
    };

    private static readonly HashSet<string> TuningValues = new(StringComparer.Ordinal)
    {
        "standard", "guitar", "bass", "bass5", "bass6", "ukulele", "uke",
    };

    private static readonly HashSet<string> InstrumentPresets =
        new(InstrumentDefaults.KnownInstruments, StringComparer.Ordinal);

    public void Validate(SyntaxTree tree)
    {
        foreach (var part in tree.GetRoot().DescendantNodes().OfType<PartDeclarationSyntax>())
            foreach (var prop in part.Properties)
                Check(prop);
    }

    private void Check(PropertyAssignmentSyntax prop)
    {
        var name = prop.NameToken.Text;
        if (!PropertyNames.Contains(name))
        {
            Error(prop.NameToken,
                $"Unknown part property '{name}'. Property names are case-sensitive; known: " +
                $"{string.Join(", ", PropertyNames.OrderBy(s => s, StringComparer.Ordinal))}.");
            return; // a property we do not understand — do not also flag its value
        }

        var valueTokens = ValueTokens(prop).ToList();
        if (valueTokens.Count == 0) return;

        switch (name)
        {
            case "clef":
                CheckValue(valueTokens, ClefValues, "clef");
                break;
            case "tuning":
                CheckValue(valueTokens, TuningValues, "tuning");
                break;
            case "instrument":
                // A quoted "…" label is free-text; only a bare preset is a symbol.
                if (valueTokens.All(t => IsQuoted(t.Text)))
                    break;
                var (preset, _) = InstrumentDefaults.SplitInstrument(valueTokens.Select(t => t.Text));
                if (!InstrumentPresets.Contains(preset))
                    Error(valueTokens[0],
                        $"Unknown instrument preset '{preset}'. Presets are case-sensitive; " +
                        "use a known preset (e.g. violin, cello, piano-right) or a quoted \"…\" name.");
                break;
        }
    }

    private void CheckValue(List<SyntaxTokenNode> tokens, HashSet<string> known, string kind)
    {
        // A hyphenated value is word+minus+word in the tree, so join the tokens.
        var text = string.Concat(tokens.Select(t => t.Text));
        if (IsQuoted(text)) return; // a quoted value is free-text, not a symbol
        if (!known.Contains(text))
            Error(tokens[0],
                $"Unknown {kind} '{text}'. {kind} names are case-sensitive; known: " +
                $"{string.Join(", ", known.OrderBy(s => s, StringComparer.Ordinal))}.");
    }

    private static bool IsQuoted(string t) => t.Length >= 2 && t[0] == '"' && t[^1] == '"';

    private static IEnumerable<SyntaxTokenNode> ValueTokens(PropertyAssignmentSyntax prop)
    {
        for (int i = 2; i < prop.SlotCount; i++)
            if (prop.GetChild(i) is SyntaxTokenNode t)
                yield return t;
    }

    private void Error(SyntaxNode token, string message) =>
        _diagnostics.Error(token.Span,
            DiagnosticCodes.UnknownSymbolCase, message);
}
