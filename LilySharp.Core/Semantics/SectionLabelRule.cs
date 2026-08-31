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

using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Whether one PLAY of a section engraves a label, and which text — the single sentence the
/// page and the LilyPond twin both read.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THIS WAS SEVENTEEN CALL SITES. Before 2026-08-31 every one spelled the rule for
/// itself — <c>silent ? null : DisplayLabel ?? name</c>. Counted after the fold, by grepping
/// the four argument-shapers and this class and subtracting the four definitions: the PAGE 13
/// (MeasureCollector.cs 4 — the form-less path, a plain reference, a volta ending, a silent
/// one — and MeasureCollector.Form.cs 9, of which the last SIX are the bar-counting
/// AdvanceSection walk, two switch blocks of three arms each) and the LilyPond twin 4.
/// ⚠️ The first count written here was ELEVEN, taken from a partial grep that saw the
/// bar-counting walk as two arms rather than six, and it was repeated into three other files
/// and a commit message before being checked. That is the shape the
/// repository has been bitten by before: on 2026-08-25 one of the page's four label arms had
/// never been taught <c>IsSilent</c>, and the LilyPond exporter's comment said it MIRRORED
/// that arm — so the citation carried the defect across the output boundary, twice.
/// </para>
/// <para>
/// Folding them was not tidiness: the label default became SETTABLE on the declaration
/// (<c>section ~A { … }</c>, owner's decision 2026-08-31), and a rule with eleven homes
/// cannot gain a term. With one home it is one line.
/// </para>
/// </remarks>
internal static class SectionLabelRule
{
    /// <summary>
    /// The label this play engraves, or null for none.
    /// </summary>
    /// <param name="declaration">
    /// The section being played, or null when the caller cannot resolve it. A null
    /// declaration reads as the ordinary default (labels shown), which is what every caller
    /// did before the declaration had a say.
    /// </param>
    /// <param name="referenceIsSilent">True when the REFERENCE carries <c>~</c>.</param>
    /// <param name="displayLabel">The occurrence's quoted label, or null for the name.</param>
    /// <param name="sectionName">The section's own name — the label when none is quoted.</param>
    public static string? LabelFor(
        SectionDeclarationSyntax? declaration,
        bool referenceIsSilent,
        string? displayLabel,
        string sectionName)
        => IsShown(declaration, referenceIsSilent)
            ? Text(displayLabel, sectionName)
            : null;

    /// <summary>
    /// Whether this play shows a label at all: the declaration sets the DEFAULT and the
    /// reference's <c>~</c> asks for the other one.
    /// </summary>
    /// <remarks>
    /// The whole rule is one equality, and it is worth reading as one. With an ordinary
    /// declaration (default = shown) a tilde hides, exactly as it always did; with
    /// <c>section ~A</c> (default = hidden) a tilde shows. Writing it as
    /// <c>hidesByDefault == referenceIsSilent</c> rather than as two branches is deliberate:
    /// there is ONE question here, not two cases, and the two-branch spelling is what would
    /// invite a third.
    /// </remarks>
    public static bool IsShown(SectionDeclarationSyntax? declaration, bool referenceIsSilent)
        => (declaration?.LabelHiddenByDefault ?? false) == referenceIsSilent;

    /// <summary>
    /// The label TEXT of a shown play: the quoted label wins over the section name, and an
    /// EMPTY quoted label suppresses the mark.
    /// </summary>
    /// <remarks>
    /// ⚠️ The empty string is the THIRD way to say "no label" (the others being the two
    /// tildes), and it stays: it is the occurrence-level spelling, needs no declaration, and
    /// books in the tree use it. It is applied after <see cref="IsShown"/> rather than folded
    /// into it, so that "does this play show a label" and "what does it say" remain separable
    /// — the diagnostic for a label that will not be printed (LYS0012) has to ask the first
    /// question about a play that HAS the second.
    /// </remarks>
    private static string? Text(string? displayLabel, string sectionName)
    {
        var label = displayLabel ?? sectionName;
        return label.Length == 0 ? null : label;
    }
}
