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
/// TRACK must mirror that shape — <c>lyrics v { section A { … } section B { … } }</c>,
/// <c>chords prog { section A { … } }</c> — one cell per named section. A flat top-level
/// track has no section to anchor to: its cells silently run from bar 0 across whatever
/// the form plays, so a reprise, a repeat, or any section after the first gets nothing.
/// This requires the sectioned form, matching how the parts are written.
///
/// Only genuinely top-level track blocks are checked: an inline <c>lyrics { … }</c> or
/// <c>chords { … }</c> written inside a part or section is bound to THAT section's music
/// and left alone, and a section-major or structureless file (where flat tracks are the
/// norm) is not touched.
/// </summary>
/// <remarks>
/// The lyrics half shipped first (LYS4002). The chords half (LYS2011) was added in
/// session 240 from a user report: <c>chords prog { Dmaj7 | Em7 | Gmaj7 | A7 }</c> beside
/// a part-major <c>part melody { section A … section B … }</c> was accepted and laid its
/// four bars over bar 0 onward, so a form of <c>A |: B :| A</c> chorded the first pass of
/// A and nothing else. The two halves live in ONE validator because it is one rule about
/// one shape (HANDOFF §5.2.1②): a track kind that gains this rule later, or a change to
/// what "top level" means, must not be able to apply to only one of them.
/// ⚠️ The exemption is the ANCESTOR test, not the file's layout alone — a part-major file
/// may still write <c>section A { chords prog { … } }</c> as a standalone section header,
/// and that block does have its section.
/// </remarks>
internal sealed class TrackNeedsSectionsValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        if (PartSectionLayoutConverter.Detect(root) != LayoutForm.PartMajor)
            return;

        foreach (var block in root.DescendantNodes().OfType<LyricsBlockSyntax>())
        {
            if (block.HasSections || HasPartOrSectionAncestor(block))
                continue;
            _diagnostics.Error(block.LyricsKeyword.Span, DiagnosticCodes.LyricTrackNeedsSections,
                "In part-major layout a lyrics track must group its verses by section: " +
                "write 'lyrics { section A { … } }' (mirroring the part's sections).");
        }

        foreach (var block in root.DescendantNodes().OfType<ChordPartBlockSyntax>())
        {
            if (block.HasSections || HasPartOrSectionAncestor(block))
                continue;
            // Name the track when it has one, so the fix can be pasted: the writer's own
            // spelling is what they are being asked to wrap, not a placeholder.
            string name = block.PartName is { Length: > 0 } n ? n + " " : "";
            _diagnostics.Error(block.ChordsKeyword.Span, DiagnosticCodes.ChordTrackNeedsSections,
                "In part-major layout a chords track must group its bars by section: " +
                $"write 'chords {name}{{ section A {{ … }} }}' (mirroring the part's sections).");
        }
    }

    private static bool HasPartOrSectionAncestor(SyntaxNode node)
    {
        for (var n = node.Parent; n != null; n = n.Parent)
            if (n is PartDeclarationSyntax or PartBlockSyntax or SectionDeclarationSyntax)
                return true;
        return false;
    }
}
