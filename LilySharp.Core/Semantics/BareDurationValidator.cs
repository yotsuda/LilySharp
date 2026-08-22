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

using LilySharp.Core.Music;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports a bare duration that has no note, chord or slash before it in its
/// top-level body (LYS0016): there is nothing to repeat. The code is the old
/// detached-duration error's — until 2026-08-19 EVERY spaced number in music
/// was that error; now that <c>c4 4</c> is the repeat spelling
/// (LILYPOND-REF: lily/parser.yy music_embedded), the code survives for the
/// one shape that still has no meaning.
/// Also WARNS (LYS1031) when the repeat reaches back across a barline: a
/// measure opening on a bare number is what a dropped pitch letter looks like
/// (<c>4 g f e</c> meant as <c>a4 g f e</c>) — the one silent misreading the
/// repeat spelling traded in (HANDOFF §3, GRAMMAR_AUDIT §3.3). Both facts come
/// from the ONE shared walk in <see cref="BareDurations"/>; this validator
/// computes neither.
/// </summary>
internal sealed class BareDurationValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is not BareDurationSyntax bare)
                continue;
            if (BareDurations.OriginalOf(bare) is null)
                _diagnostics.Error(
                    bare.Span,
                    DiagnosticCodes.DetachedDuration,
                    "A bare duration repeats the previous note, chord or slash - "
                    + "and nothing repeatable comes before this one. Write the note "
                    + "itself (c4), or glue the number to what it lengthens (c4, "
                    + "not c 4).");
            else if (BareDurations.CrossesBarline(bare))
                _diagnostics.Warning(
                    bare.Span,
                    DiagnosticCodes.BareDurationAcrossBarline,
                    "A bare duration repeats the previous note, chord or slash - "
                    + "and this one reaches back across a barline for it. That is "
                    + "also what a dropped pitch letter looks like (4 g f e meant "
                    + "as a4 g f e), so write the event itself (c4) when the "
                    + "repeat is meant.");
        }
    }
}
