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
        "octave", "removeEmpty", "lines", "pedal",
    };

    private static readonly HashSet<string> PedalValues = new(StringComparer.Ordinal)
    {
        "bracket", "text", "mixed",
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

    /// <summary>
    /// The values <c>removeEmpty</c> takes. ⚠️ Listed but NOT enforced: an unknown word here
    /// is read as <c>false</c> by <c>RenderSpecParser</c> rather than refused, so
    /// <c>removeEmpty banana</c> compiles today (measured 2026-08-19, as does
    /// <c>lines banana</c>). Turning that into a diagnostic would refuse books that compile
    /// now, so it is a decision and not a tidy-up — it is written up in HANDOFF §2F. The list
    /// is here because it is a vocabulary of this header and the vocabulary has one home;
    /// <c>EditorColouringTests</c> reads it, so it cannot rot unnoticed.
    /// </summary>
    private static readonly HashSet<string> RemoveEmptyValues = new(StringComparer.Ordinal)
    {
        "true", "all", "false",
    };

    // ★ The part header's vocabulary, published so that it has exactly ONE home and every
    // other reader is the SECOND one. Before 2026-08-19 the editor's TextMate grammar held its
    // own copy of a PART of this — whichever words happened to be reserved for unrelated
    // reasons — and the rest were plain: `clef treble` coloured, `clef treble^8` not; `pedal
    // text` half-coloured because `text` is a fonts key; all seven tuning names plain but for
    // `bass`, which is a clef. That is what the user reported about `fonts { }`, in five more
    // vocabularies. A grammar that reads these cannot drift from them silently, because
    // EditorColouringTests holds it to them in both directions.
    //
    // ⚠️ These are the PART BODY's vocabularies and not the language's everywhere. Measured
    // 2026-08-19: a part body takes all eleven clef names, while `clef` in music and `staff` in
    // a score take five (treble treble_8 alto tenor bass) and refuse the other six. One
    // production, two positions — GRAMMAR.md's `ClefName` names the second.

    /// <summary>Every name a part header property can be spelled with.</summary>
    /// <remarks>Includes <c>key</c>, which never reaches <see cref="Check"/> because the parser
    /// takes it as a KeySignature rather than a PropertyAssignment — it belongs to the
    /// vocabulary all the same, and leaving it out told a reader the language has no per-part
    /// key, which is false.</remarks>
    internal static IReadOnlyCollection<string> PropertyNameVocabulary { get; } =
        [.. PropertyNames.Append("key").OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Every clef name a PART BODY takes — eleven, not the five of <c>ClefName</c>.</summary>
    internal static IReadOnlyCollection<string> ClefValueVocabulary { get; } =
        [.. ClefValues.OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Every pedal style.</summary>
    internal static IReadOnlyCollection<string> PedalValueVocabulary { get; } =
        [.. PedalValues.OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Every tuning name — also the vocabulary of <c>tab NAME</c> in a score
    /// (measured 2026-08-19: all seven are accepted in both positions).</summary>
    internal static IReadOnlyCollection<string> TuningValueVocabulary { get; } =
        [.. TuningValues.OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Every value <c>removeEmpty</c> documents. See <see cref="RemoveEmptyValues"/>
    /// for why it is not enforced.</summary>
    internal static IReadOnlyCollection<string> RemoveEmptyValueVocabulary { get; } =
        [.. RemoveEmptyValues.OrderBy(s => s, StringComparer.Ordinal)];

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
            // `key` is not in PropertyNames because it never reaches here: a part-header key
            // is parsed as a KeySignature, not a PropertyAssignment (Parser.Declarations —
            // a key is legitimately per-part, e.g. a transposing instrument). It belongs in
            // the LIST all the same. Leaving it out told a reader who wrote `Key c major`
            // that the language has no per-part key, which is false — measured:
            // `part m { key fis major }` engraves byte-identically to a top-level
            // `key fis major`.
            Error(prop.NameToken,
                $"Unknown part property '{name}'. Property names are case-sensitive; known: " +
                $"{string.Join(", ", PropertyNameVocabulary)}.");
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
            case "pedal":
                CheckValue(valueTokens, PedalValues, "pedal");
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
