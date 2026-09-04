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
/// The two words a <c>tab</c> render item can carry — an optional TUNING before the part and
/// an optional STYLE after <c>as</c> — are closed vocabularies. Neither was checked.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THIS IS THE OTHER HALF OF A VALIDATOR THAT ALREADY EXISTED. <c>ConsumeAsSelector</c> is
/// shared by <c>chords NAME as roman|names</c> and <c>tab NAME as numbers|full</c>, and its
/// own doc comment says so. Session 240 closed the chord half
/// (<see cref="ChordDisplayModeValidator"/>) because retiring <c>as both</c> was unsafe while
/// an unknown word fell through to <c>names</c> in silence. The tab half fell through to
/// <c>full</c> in exactly the same way and was left — MEASURED 2026-08-24:
/// <c>tab m as bogus</c> and <c>tab m as roman</c> both drew full notation and reported
/// nothing, byte-identical to <c>tab m</c>.
/// </para>
/// <para>
/// ⚠️ AND THE TUNING WAS WORSE, because it moved the picture. <c>RenderSpecParser.ParseTab</c>
/// ends its tuning switch in <c>_ =&gt; TuningType.Guitar</c>, so <c>tab bogus m</c> on a part
/// declared <c>instrument bass</c> silently re-fretted the music for a guitar; the only thing
/// said was a downstream "note is below the tab's lowest string", which names the symptom and
/// not the typo. <c>LanguageVocabulary.TuningNames</c> already CLAIMED to be this position's
/// vocabulary ("also the vocabulary of <c>tab NAME</c> in a score") — measured, that had
/// only ever meant the seven are ACCEPTED here, never that an eighth is refused. Accepting
/// every valid word and refusing none is the <c>Assert.DoesNotContain</c> shape one level up
/// (HANDOFF §5.0).
/// </para>
/// <para>
/// ⚠️ CASE IS PART OF IT. The style was compared <c>OrdinalIgnoreCase</c>, so
/// <c>tab m as NUMBERS</c> was accepted while every other symbol in the language is Ordinal
/// — the same split <c>removeEmpty</c> had until 2026-08-19. It is Ordinal now and the
/// wrong case is refused with the same message as an unknown word.
/// </para>
/// </remarks>
internal sealed class TabRenderVocabularyValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>The two tab styles. <c>full</c> is what omitting the clause means.</summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: ly/property-init.ly — the <c>tabFullNotation</c> definition (:822-830).
    /// ⚠️ The address carries NO line range on purpose: the only symbol on that line is one
    /// word, and LpReferenceCitationTests counts a citation with a range but no compound name
    /// as unnamed (HANDOFF §5.2.1⑦ documents this exact escape). The range is in the prose.
    /// The two styles are the two
    /// states of that switch, so the WORDS are Lily#'s and the thing each selects is
    /// LilyPond's: <c>numbers</c> is a plain TabStaff (fret digits, no stems or rests) and
    /// <c>full</c> is <c>\tabFullNotation</c>, which is what LilyPondExporter emits.
    /// ⚠️ THE DEFAULT IS THE SCORE'S ANSWER, not a fixed word (user decision, 2026-08-29). A
    /// tab beside a notation staff of the same part defaults to <c>numbers</c>, because that
    /// staff already carries the meter, the rests, the dots, the stems and the ties; a tab
    /// standing alone defaults to <c>full</c>, because it has to carry them itself. An
    /// explicit clause always wins. <c>RenderSpecParser.StaffRenderedParts</c> is where the
    /// question is asked and says what counts as "on a notation staff".
    /// ★ Until that decision Lily# defaulted to <c>full</c> everywhere, which is the opposite
    /// of LilyPond's own default and was written up here as a deliberate divergence. The
    /// paired case is now LilyPond's; only the lone tab still differs, and it differs where
    /// LilyPond has nothing to say — a TabStaff with no notation staff above it is not a
    /// shape LilyPond's default was chosen for.
    /// </para>
    /// <para>
    /// ⚠️ ONE HOME, and it is this one: <c>RenderSpecParser.ParseTab</c> tests
    /// <c>== "numbers"</c> for itself, which is safe only because there are exactly two
    /// styles — the moment a third arrives, a test for one word stops being a test for the
    /// vocabulary. The twin asks the page (<c>RenderSpecParser.TabIsNumbersOnly</c>, session
    /// 335) so the DEFAULT is the page's too. The editor's completion reads this list
    /// (LanguageVocabulary.TabStyles) so it cannot offer a word the compiler would now
    /// refuse.
    /// </para>
    /// <para>
    /// ⚠️ THE ORDER IS THE LANGUAGE'S, not alphabetical, and it is load-bearing: the editor
    /// offers these in list order and puts <c>numbers</c> first because <c>full</c> is what
    /// leaving the clause out already does. Publishing them sorted quietly reordered the
    /// completion and <c>TabDisplayCompletionTests</c> caught it, which is the net doing its
    /// job — the longer vocabularies on <see cref="SymbolCaseValidator"/> are sorted because
    /// a list of seven has no meaningful order, and a list of two does.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyCollection<string> StyleVocabulary =
        new[] { "numbers", "full" };

    /// <summary>
    /// Whether a tab item asks for the numbers-only style — the ONE reading of the style
    /// word, used by the page (<c>RenderSpecParser.ParseTab</c>) and by the LilyPond twin
    /// (<c>LilyPondExporter.TabIsNumbersOnly</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ IT IS ONE READING BECAUSE IT WAS TWO, AND BOTH WERE WRONG THE SAME WAY: each found
    /// the <c>as</c> for itself and then compared <c>OrdinalIgnoreCase</c>, so
    /// <c>tab m as NUMBERS</c> engraved AND exported a numbers-only tab while every other
    /// symbol in the language is case-sensitive. Two copies of a rule are two copies of its
    /// defect, and the twin's copy would have kept drawing the refused spelling after the
    /// page's was corrected (HANDOFF §5.2.1②).
    /// </remarks>
    internal static bool IsNumbersOnly(TabRenderSyntax tab) =>
        tab.DisplayModeToken is { } style
        && string.Equals(style.Text, "numbers", StringComparison.Ordinal);

    public void Validate(SyntaxTree tree)
    {
        foreach (var tab in tree.GetRoot().DescendantNodes().OfType<TabRenderSyntax>())
        {
            if (tab.DisplayModeToken is { } style && style.Text.Length > 0
                && !StyleVocabulary.Contains(style.Text, StringComparer.Ordinal))
            {
                _diagnostics.Error(style.Span, DiagnosticCodes.UnknownTabRenderWord,
                    $"'{style.Text}' is not a tab style. Tab styles are case-sensitive; "
                    + $"write 'as {string.Join("' or 'as ", StyleVocabulary)}' "
                    + "(omit 'as' for full).");
            }

            if (tab.TuningToken is { } tuning && tuning.Text.Length > 0
                && !LanguageVocabulary.TuningNames.Contains(tuning.Text, StringComparer.Ordinal))
            {
                _diagnostics.Error(tuning.Span, DiagnosticCodes.UnknownTabRenderWord,
                    $"Unknown tuning '{tuning.Text}'. Tuning names are case-sensitive; known: "
                    + $"{string.Join(", ", LanguageVocabulary.TuningNames)}. "
                    + "Omit it to take the tuning from the part.");
            }
        }
    }
}
