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
/// A chords or lyrics TRACK written per section (<c>lyrics { section A { … } section
/// B { … } }</c>) may name each section only once. Repeating a name — two
/// <c>section B { … }</c> blocks — reads as a single section written twice and, in a
/// lyrics track, was the old way to stack a repeat's verses; that is confusing next to
/// the section-major form, so this rejects it. To give a repeated or reprised section
/// DIFFERENT words each pass, number the verses inside the one section instead:
/// <c>section B { [1. … ] [2. … ] }</c>.
///
/// (Part and structure sections are already covered by
/// <see cref="DuplicateCellValidator"/>, which catches a doubly-defined section/part
/// cell; a track section carries no part, so it needs this dedicated check.)
/// </summary>
internal sealed class DuplicateTrackSectionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var lyrics in root.DescendantNodes().OfType<LyricsBlockSyntax>())
            CheckDuplicates(lyrics.Sections, "lyrics");
        foreach (var chords in root.DescendantNodes().OfType<ChordPartBlockSyntax>())
            CheckDuplicates(chords.Sections, "chords");
    }

    private void CheckDuplicates(IEnumerable<SectionDeclarationSyntax> sections, string trackKind)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var section in sections)
        {
            if (seen.Add(section.SectionName))
                continue;
            _diagnostics.Error(section.Name.Span, DiagnosticCodes.DuplicateTrackSection,
                $"Section \"{section.SectionName}\" is written twice in this {trackKind} track; " +
                "name each section once. To give a repeated or reprised section different words " +
                "each pass, number the verses inside it: 'section " + section.SectionName +
                " { [1. … ] [2. … ] }'.");
        }
    }
}
