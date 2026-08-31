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

using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>clef</c> and <c>octave</c> are PART settings, so they say nothing in the header
/// position of a section that holds part cells: written there they belong to no cell
/// (<see cref="DiagnosticCodes.PartSettingInSectionHeader"/>).
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="ScoreSettingInPartHeaderValidator"/>, which refuses a
/// SCORE-level setting written as a PART property. Same shape, opposite direction: a setting
/// written one container too far in, against one written one container too far out.
/// </para>
/// <para>
/// ⚠️ IT CLOSES AN ASYMMETRY RATHER THAN ADDING A RULE. Of the four part settings that can
/// stand in that position, <c>instrument</c> and <c>transpose</c> were already refused —
/// nothing claims them, so <c>ParseSectionItem</c> ends at <c>ReportStrayItem</c> (LYS0030).
/// <c>clef</c> and <c>octave</c> are in <c>IsMusicItemStart</c>, because they ARE music items
/// where music is legal, so the section item's music arm takes them and they become bare
/// music belonging to no part. Two of four spoke; this makes it four.
/// </para>
/// <para>
/// ⚠️ TWO POSITIONS, ONE RULE. Beside a section's cells (the header position) and ON one
/// of those cells as a PART-BLOCK OPTION (<c>section A { m clef bass { … } }</c>). The
/// parser has a dedicated arm for the second (<c>Parser.Sections.IsPartOption</c> —
/// transpose / octave / instrument / clef) that <c>GRAMMAR.md</c>'s
/// <c>PartBlock = Identifier , MusicBlock ;</c> never mentioned, so it was reachable and
/// unwritten. MEASURED 2026-08-31 (scratch/p305/r4_partblock_options.lys): <c>clef</c>,
/// <c>octave</c> and <c>instrument</c> are silently ignored there, and <c>transpose</c> is
/// read at the WRONG SCOPE — written on ONE section's cell it transposes the WHOLE part
/// (the twin wraps the part variable in <c>\transpose c d</c>).
/// </para>
/// <para>
/// ⚠️ THE SECOND POSITION IS REFUSED RATHER THAN IMPLEMENTED, and that follows a decision
/// already on the books rather than a new one: HANDOFF §3, 2026-08-31 — <c>transpose</c> /
/// <c>octave</c> are NOT added as section-scoped features, because the case that wants a
/// transpose ("the last chorus up a semitone") is a mark on the REFERENCE, not on the
/// declaration; <c>section D { transpose d }</c> would move EVERY play of D. A cell is a
/// declaration site, so the same reasoning reaches it unchanged, and scoping the option to
/// the cell would not deliver what the case wants either (HANDOFF §2 F ⒭).
/// </para>
/// <para>
/// ⚠️ THE PREDICATE IS ABOUT THE SECTION, NOT THE KEYWORD, and the two shapes that work are
/// why. <c>part m { section A { clef bass … } }</c> and <c>section A { clef bass c'4 … }</c>
/// both engrave the clef, because in each the section's body IS a music stream — the first is
/// part-major, the second the single-part piece GRAMMAR.md allows to write bare music in a
/// section. A section that holds CELLS has nowhere to put a loose setting, and that is the
/// one case reported.
/// </para>
/// </remarks>
internal sealed class PartSettingInSectionHeaderValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        ReportPartBlockOptions(tree);

        foreach (var section in tree.GetRoot().DescendantNodes().OfType<SectionDeclarationSyntax>())
        {
            if (HasOwnMusic(section))
                continue;

            for (int i = 0; i < section.SlotCount; i++)
            {
                switch (section.GetChild(i))
                {
                    case ClefDeclarationSyntax clef:
                        _diagnostics.Error(clef.Span,
                            DiagnosticCodes.PartSettingInSectionHeader,
                            "'clef' is a property of one PART, and this section holds part "
                            + "cells - a clef written beside them belongs to none of them and "
                            + "nothing draws it. Put it on the part "
                            + "('part NAME { clef bass }'), or inside a cell's music for a "
                            + "change mid-piece ('NAME { clef bass c4 … }').");
                        break;

                    case OctaveDirectiveSyntax octave:
                        // Worth its own sentence: this one is not merely ignored. The page and
                        // the pitches keep reading relative while the LilyPond twin's wrapper
                        // for the whole part flips to `\fixed`, so the readers disagree.
                        _diagnostics.Error(octave.Span,
                            DiagnosticCodes.PartSettingInSectionHeader,
                            "'octave' says how a file's pitches are READ, so it belongs to the "
                            + "file or to one part - not to a section that holds part cells. "
                            + "Written here it moves no pitch, while the LilyPond twin changes "
                            + "the octave model of the WHOLE part with it. Put it at the top "
                            + "level ('octave absolute') or on the part.");
                        break;
                }
            }
        }
    }

    /// <summary>
    /// The PART-BLOCK OPTION position: <c>section A { m clef bass { … } }</c>. A cell is a
    /// declaration of one part's music for one section, and a part setting written on it is
    /// the same mistake as one written beside the cells — the language keeps part settings
    /// on the part.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <c>PartBlockSyntax</c> is ALWAYS a section cell — <c>ParsePartBlock</c> has one
    /// caller, <c>ParseSectionItem</c> — so no ancestor test is needed here. The top-level
    /// <c>part m transpose d { … }</c> is a <c>PartDeclarationSyntax</c> and a different
    /// node; that one is the CORRECT home and must keep working.
    /// </remarks>
    private void ReportPartBlockOptions(SyntaxTree tree)
    {
        foreach (var cell in tree.GetRoot().DescendantNodes().OfType<PartBlockSyntax>())
        {
            foreach (var option in cell.ChildNodes().OfType<PropertyAssignmentSyntax>())
            {
                string word = option.NameToken.Text;
                // `transpose` earns its own sentence for the reason `octave` does above: it
                // is not ignored, it is MIS-SCOPED, so the author's book renders — wrongly —
                // rather than merely failing to change.
                string detail = word == "transpose"
                    ? "Written on a cell it does not transpose that cell: the whole PART "
                      + "moves, every section of it. Put it on the part "
                      + "('part NAME transpose d { … }') or on the score."
                    : "Written on a cell nothing reads it at all. Put it on the part "
                      + $"('part NAME {word} … {{ … }}'), or inside the cell's music for a "
                      + "change mid-piece.";
                _diagnostics.Error(option.Span,
                    DiagnosticCodes.PartSettingInSectionHeader,
                    $"'{word}' is a property of one PART, and a section's part cell is not "
                    + "where a part is declared. " + detail);
            }
        }
    }

    /// <summary>
    /// True when the section carries MUSIC OF ITS OWN — a stream the setting can join. A part
    /// cell's music is the cell's, not the section's, so a section built of cells has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ THE QUESTION IS "IS THERE A STREAM TO JOIN", not "are there cells", and the
    /// difference is a book the converter can produce. A DIRECTIVES-ONLY header —
    /// <c>section A { clef bass }</c> standing beside the parts, the shape GRAMMAR.md
    /// documents for <c>key</c> — holds no cells either, so a cells-only test let it through;
    /// then the LSP's convert-layout command folds that header into the section-major section
    /// and hands the author a book this rule refuses. MEASURED 2026-08-31: clean before the
    /// conversion, LYS1035 after. Asking for the STREAM catches both shapes at the source.
    /// </para>
    /// <para>
    /// ⚠️ THE UNKNOWN NODE COUNTS AS MUSIC, deliberately: this rule raises an ERROR, so the
    /// safe failure is silence on a shape nobody listed rather than a refusal of a legal book.
    /// A node type added later is silent here until it is added to the list.
    /// </para>
    /// </remarks>
    private static bool HasOwnMusic(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            switch (section.GetChild(i))
            {
                // The section's own frame, its settings, its cells and its tracks: none of
                // these is a music stream.
                case null:
                case SyntaxTokenNode:                 // 'section', '~', the name, the braces
                case KeySignatureSyntax:
                case TimeSignatureSyntax:
                case TempoDeclarationSyntax:
                case PartialDeclarationSyntax:
                case ClefDeclarationSyntax:           // the two settings under test
                case OctaveDirectiveSyntax:
                case OverrideDeclarationSyntax:
                case RevertDeclarationSyntax:
                case OnceModifierSyntax:
                case PartBlockSyntax:
                case LyricsBlockSyntax:
                case ChordPartBlockSyntax:
                    continue;
                default:
                    return true;
            }
        }
        return false;
    }
}
