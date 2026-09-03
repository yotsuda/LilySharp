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
using LilySharp.Core.Svg.Collector;
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
        "octave", "removeEmpty", "pedal", "pitch",
    };

    /// <summary>The two words <c>pitch</c> takes, read from their one home.</summary>
    private static readonly HashSet<string> PitchModes =
        new(ConcertPitch.Modes, StringComparer.Ordinal);

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

    /// <summary>The ottava markers <c>transposition</c> takes, read from their one home.</summary>
    private static readonly HashSet<string> TranspositionMarkers =
        new(InstrumentDefaults.TranspositionMarkers, StringComparer.Ordinal);

    /// <summary>
    /// The values <c>removeEmpty</c> takes — and, since 2026-08-19, the values it ACCEPTS.
    /// </summary>
    /// <remarks>
    /// Until then the list was written down but not enforced: <c>RenderSpecParser</c> compares
    /// against <c>"true" or "all"</c>, so any other word was read as <c>false</c> rather than
    /// refused and <c>removeEmpty banana</c> compiled. That was one of FIVE part properties
    /// whose value nobody checked (with <c>lines</c>, <c>octave</c>, <c>transpose</c> and
    /// <c>transposition</c>) while <c>clef</c>, <c>tuning</c>, <c>pedal</c> and
    /// <c>instrument</c> refused an unknown word — two weights inside one header. Enforcing
    /// it refuses books that compiled before, so it was a decision rather than a tidy-up:
    /// taken 2026-08-19, before 0.3.0 was tagged, when it costs nobody a migration
    /// (measured the same day: 0 of the 567 tracked books write a value outside the list).
    /// <c>false</c> is in the list and means what leaving the property out means.
    /// <c>HaraKiriTests</c> holds the list to what <c>RenderSpecParser</c> reads, and
    /// <c>EditorColouringTests</c> reads it too, so it cannot rot unnoticed.
    /// ⚠️ The reader's <c>ToLowerInvariant</c> can no longer fire for a bad case, exactly as
    /// with <c>pedal</c>: this validator refuses <c>removeEmpty TRUE</c> first, by the Ordinal
    /// rule every other symbol in this header already obeyed.
    /// </remarks>
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

    /// <summary>The properties written as a <c>NAME value</c> PAIR — the vocabulary without
    /// <c>key</c>, which the parser takes as a key SIGNATURE instead.</summary>
    /// <remarks>The distinction only matters to a writer that INSERTS one of these (the
    /// editor's completion), so it is published next to the vocabulary rather than left for
    /// each such writer to subtract <c>key</c> for itself and explain why in a comment.</remarks>
    internal static IReadOnlyCollection<string> ValuePairPropertyVocabulary { get; } =
        [.. PropertyNames.OrderBy(s => s, StringComparer.Ordinal)];

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

    /// <summary>Every value <c>removeEmpty</c> ACCEPTS. See <see cref="RemoveEmptyValues"/> for
    /// what it cost to make that true — it read "documents … not enforced" until 2026-08-19,
    /// and the sentence outlived the enforcement by one session.</summary>
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
            case "removeEmpty":
                CheckValue(valueTokens, RemoveEmptyValues, "removeEmpty", "values");
                break;
            // ("lines" left this list 2026-08-19: the staff-line count is a
            // property of the RENDERING, written `staff m as lines N` in the
            // score; the parser keeps this arm's message word for word.)
            case "octave":
                // No bound: an octave number is read as written (PartHeaderDefaults),
                // so the only thing that can be wrong about it is not being a number.
                CheckWholeNumber(valueTokens, "octave", int.MinValue, int.MaxValue,
                    "an octave number", "a whole number, e.g. 'octave 3'");
                break;
            case "transpose":
                // Ask the READER whether it can read this, rather than spelling the
                // pitch-target grammar a second time here. It also carries the octave
                // marks (`transpose bes,`), which the joined token text would not.
                if (PartTranspose.ReadProperty(prop) is null)
                    Error(valueTokens[0],
                        $"'{Joined(valueTokens)}' is not a transpose target. transpose takes a "
                        + "pitch — a letter a–g with an optional is/isis/es/eses and "
                        + "octave marks (e.g. 'transpose d', 'transpose bes,').");
                break;
            case "transposition":
                // NOT "ask the reader" here, and the difference is the whole point:
                // ParseTranspositionSemitones lowers its argument, so asking IT would
                // ACCEPT `transposition 8VB` — measured 2026-08-19, when a first draft of
                // this branch did exactly that and let the one spelling this session set out
                // to refuse sail straight through. The published marker list is the
                // vocabulary (InstrumentPresetTests holds it to that switch), and Ordinal is
                // the rule every other symbol in this header obeys.
                CheckValue(valueTokens, TranspositionMarkers, "transposition", "markers");
                break;
            case "pitch":
                // The same two words the top-level directive takes (the parser refuses a
                // third there; here the header's generic value path leaves it to this rule).
                CheckValue(valueTokens, PitchModes, "pitch", "modes");
                break;
        }
    }

    /// <summary>
    /// Refuses a value that is not a whole number, or is one outside
    /// <paramref name="min"/>..<paramref name="max"/>. The bounds are passed in from
    /// whoever READS the property so this does not become a second spelling of them.
    /// </summary>
    /// <param name="isNot">What the written value FAILS to be ("a staff-line count").</param>
    /// <param name="takes">What the property accepts instead ("a whole number from 1 to 5").</param>
    private void CheckWholeNumber(
        List<SyntaxTokenNode> tokens, string property, int min, int max,
        string isNot, string takes)
    {
        string text = Joined(tokens);
        if (int.TryParse(text, out int n) && n >= min && n <= max)
            return;
        Error(tokens[0], $"'{text}' is not {isNot}. '{property}' takes {takes}.");
    }

    /// <param name="noun">What the listed words ARE — clefs have names, removeEmpty has
    /// values. Only the wording differs; the rule is one rule.</param>
    private void CheckValue(
        List<SyntaxTokenNode> tokens, HashSet<string> known, string kind, string noun = "names")
    {
        // A hyphenated value is word+minus+word in the tree, so join the tokens.
        var text = Joined(tokens);
        if (IsQuoted(text)) return; // a quoted value is free-text, not a symbol
        if (!known.Contains(text))
            Error(tokens[0],
                $"Unknown {kind} '{text}'. {kind} {noun} are case-sensitive; known: " +
                $"{string.Join(", ", known.OrderBy(s => s, StringComparer.Ordinal))}.");
    }

    private static bool IsQuoted(string t) => t.Length >= 2 && t[0] == '"' && t[^1] == '"';

    /// <summary>A hyphenated or mark-carrying value is several tokens in the tree.</summary>
    private static string Joined(List<SyntaxTokenNode> tokens)
        => string.Concat(tokens.Select(t => t.Text));

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
