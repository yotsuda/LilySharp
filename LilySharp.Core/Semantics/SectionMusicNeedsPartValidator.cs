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
using LilySharp.Core.Editing;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// In a PART-MAJOR file (parts carry their own <c>section</c> blocks), a top-level
/// <c>section A { … }</c> is a section-level HEADER: it may hold section-wide directives
/// (a <c>partial</c> pickup, key, time) and cells for the parts, but no loose music. Bare
/// notes written straight into it (<c>section A { c d e }</c>) belong to no part, so they are
/// flagged: put the music inside a part (<c>part melody { section A { c d e } }</c>).
///
/// Only PART-MAJOR files are checked. A single-part file that writes its one part's setup and
/// music apart (<c>part bl { clef bass } section A { c d e }</c>) is NOT part-major — its loose
/// section music binds to the lone part and is left alone; nor is a section-major file, whose
/// top-level sections legitimately hold the parts' cells.
/// </summary>
internal sealed class SectionMusicNeedsPartValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (PartSectionLayoutConverter.Detect(root) != LayoutForm.PartMajor)
            return;

        foreach (var section in root.DescendantNodes().OfType<SectionDeclarationSyntax>())
        {
            // Only a GENUINE top-level section (a direct member of the file). A part cell's
            // parent is the part; a lyrics/chords track section's parent is that track.
            if (section.Parent is not CompilationUnitSyntax)
                continue;

            for (int i = 0; i < section.SlotCount; i++)
            {
                var child = section.GetChild(i);
                if (child == null || !IsBareMusic(child))
                    continue;
                _diagnostics.Error(child.Span, DiagnosticCodes.SectionMusicNeedsPart,
                    "This file is part-major, so a top-level section holds only section-wide "
                    + "directives and the parts' cells — put the music inside a part, e.g. "
                    + $"'part melody {{ section {section.Name.Text} {{ … }} }}'.");
                break; // one report per section is enough
            }
        }
    }

    /// <summary>A direct section child that is MUSIC (would form measures) rather than a part
    /// cell, a track (lyrics/chords) block, or a directive (key/time/clef/…).</summary>
    private static bool IsBareMusic(SyntaxNode n) => n is
        NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax or ChordRepetitionSyntax
        or ArpeggioSyntax
        or TupletExpressionSyntax or GraceExpressionSyntax or RepeatExpressionSyntax
        or VariableReferenceSyntax or BarlineSyntax;
}
