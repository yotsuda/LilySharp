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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The compiler's own symbol lists, PUBLISHED so that a reader outside this assembly is
/// derived from them rather than a copy of them.
/// </summary>
/// <remarks>
/// <para>
/// The lists themselves live where the compiler uses them — <see cref="SymbolCaseValidator"/>
/// refuses an unknown word with them, <see cref="SyntaxFacts.IsClefKeyword"/> decides what a
/// music block will consume. Nothing here re-spells any of that; every member forwards.
/// </para>
/// <para>
/// ⚠️ This type exists because <c>LilySharp.Lsp</c> is a SEPARATE ASSEMBLY and those homes are
/// <c>internal</c>: <c>InternalsVisibleTo</c> covers Tests, Benchmarks and Probe only. So while
/// the editor's TextMate grammar could be held to the compiler from a test (EditorColouringTests
/// does exactly that), the editor's COMPLETION could not — and it duly drifted, keeping its own
/// copies of the clef names, the <c>removeEmpty</c> values and the property-name list. Measured
/// 2026-08-19: the part-property list offered six of the nine properties and told the writer
/// that <c>octave</c> takes <c>absolute | relative</c>, two words a part header had never read
/// and that a part header has REFUSED since the day before.
/// </para>
/// <para>
/// ★ Publishing is the fix rather than a convenience: an editor that suggests a word the
/// compiler rejects is worse than one that suggests nothing, and only a shared home makes the
/// two impossible to separate.
/// </para>
/// </remarks>
public static class LanguageVocabulary
{
    // ===== The PART HEADER's vocabularies (GRAMMAR.md: PartProperty and friends) =====

    /// <summary>Every name a <c>part { }</c> header property can be spelled with, including
    /// <c>key</c> — which the parser takes as a KeySignature rather than a property, and which
    /// is therefore in the LANGUAGE but not in the set an editor can insert as a pair.
    /// See <see cref="PartPropertiesTakingAValuePair"/>.</summary>
    public static IReadOnlyCollection<string> PartProperties => SymbolCaseValidator.PropertyNameVocabulary;

    /// <summary>The part-header properties written as <c>NAME value</c>, i.e.
    /// <see cref="PartProperties"/> without <c>key</c>. This is the set an editor may offer:
    /// completing <c>key</c> here would insert text the parser reads as a key SIGNATURE, so the
    /// suggestion would be legal and yet not be the property it looked like.</summary>
    public static IReadOnlyCollection<string> PartPropertiesTakingAValuePair =>
        SymbolCaseValidator.ValuePairPropertyVocabulary;

    /// <summary>Every clef name a PART HEADER takes — eleven. Measured 2026-08-19 and again
    /// 2026-08-19 (session 211): all eleven compile in a header, and the six that are not in
    /// <see cref="ClefNames"/> are refused in music with "Expected clef name".</summary>
    public static IReadOnlyCollection<string> PartClefNames => SymbolCaseValidator.ClefValueVocabulary;

    /// <summary>Every pedal style a part header takes.</summary>
    public static IReadOnlyCollection<string> PedalStyles => SymbolCaseValidator.PedalValueVocabulary;

    /// <summary>Every tuning name — also the vocabulary of <c>tab NAME</c> in a score.</summary>
    public static IReadOnlyCollection<string> TuningNames => SymbolCaseValidator.TuningValueVocabulary;

    /// <summary>Every value <c>removeEmpty</c> accepts (enforced since 2026-08-19).</summary>
    public static IReadOnlyCollection<string> RemoveEmptyValues => SymbolCaseValidator.RemoveEmptyValueVocabulary;

    /// <summary>Every ottava marker <c>transposition</c> accepts.</summary>
    public static IReadOnlyCollection<string> TranspositionMarkers => InstrumentDefaults.TranspositionMarkers;

    /// <summary>Every instrument preset <c>instrument</c> accepts.</summary>
    public static IReadOnlyCollection<string> InstrumentPresets => InstrumentDefaults.KnownInstruments;

    /// <summary>The staff-line counts <c>lines</c> accepts, inclusive. Published for the same
    /// reason as the word lists: a description that states a range is a copy of one.</summary>
    public static int MinStaffLines => Svg.Collector.StaffSpec.MinLines;

    /// <inheritdoc cref="MinStaffLines"/>
    public static int MaxStaffLines => Svg.Collector.StaffSpec.MaxLines;

    // ===== The PAPER BLOCK's vocabularies (GRAMMAR.md: PaperDecl) =====

    /// <summary>The scalar length keys a <c>paper { }</c> entry takes
    /// (<c>paperWidth 210mm</c>).</summary>
    public static IReadOnlyCollection<string> PaperScalarKeys => PaperPlanReader.ScalarKeySpellings();

    /// <summary>The nested spacing-block keys a <c>paper { }</c> entry takes
    /// (<c>systemSystemSpacing { … }</c>).</summary>
    public static IReadOnlyCollection<string> PaperSpacingKeys => PaperPlanReader.SpecKeySpellings();

    /// <summary>The sub-keys a spacing block takes (<c>basicDistance 12</c>).</summary>
    public static IReadOnlyCollection<string> PaperSpacingSubKeys => PaperPlanReader.AllSubKeySpellings();

    // ===== The MUSIC / SCORE position (GRAMMAR.md: ClefName) =====

    /// <summary>
    /// The clef names a <c>clef</c> directive IN MUSIC takes — five, not the eleven of
    /// <see cref="PartClefNames"/>. <c>staff</c> / <c>ossia</c> in a score read the same five.
    /// </summary>
    /// <remarks>
    /// ⚠️ Derived, not written down: the eleven filtered by the parser's own
    /// <see cref="SyntaxFacts.IsClefKeyword"/>. One production standing in two positions is
    /// exactly the shape that produced the editor's drift, so the narrower position is computed
    /// from the wider one and can never name a word the parser would reject.
    /// </remarks>
    public static IReadOnlyCollection<string> ClefNames => SyntaxFacts.ClefNameVocabulary;
}
