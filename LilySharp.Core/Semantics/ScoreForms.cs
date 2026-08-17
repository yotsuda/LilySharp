// Lily# — a music notation language and engraver.
// Copyright (C) 2026 yotsuda
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
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Which <c>form</c> an output renders when nothing says otherwise: the one named
/// <c>main</c>, else the first declared.
/// </summary>
/// <remarks>
/// ⚠️ ONE HOME, because three exporters had written the same two lines and one of them had
/// drifted: MIDI and MusicXML matched <c>main</c> by ordinal comparison, the LilyPond twin
/// case-insensitively, so a file declaring <c>form Main</c> would have had the twin render a
/// movement the other two did not. (No book in the corpus spells it that way, which is
/// exactly why the drift could sit there — measured 2026-08-17: the 560 books that declare a
/// form use `main` and the three `movementN` of `test/multi-movement`.)
/// <para>
/// The three formats each write ONE form per file, so a file with several says so out loud
/// and takes <c>--score</c> / <c>--all</c> to reach the rest (HANDOFF §3, decided
/// 2026-08-17). <see cref="All"/> is what the CLI counts to know it is dropping something.
/// </para>
/// </remarks>
public static class ScoreForms
{
    /// <summary>Every <c>form</c> the file declares, in declaration order.</summary>
    public static IReadOnlyList<FormDeclarationSyntax> All(SyntaxNode root)
        => root.DescendantNodes().OfType<FormDeclarationSyntax>().ToList();

    /// <summary>The form an output renders by default — <c>main</c>, else the first
    /// declared, else null when the file declares none (the sections then play in
    /// declaration order).</summary>
    public static FormDeclarationSyntax? Primary(SyntaxNode root)
    {
        var forms = All(root);
        return forms.FirstOrDefault(f => f.NameText == "main") ?? forms.FirstOrDefault();
    }
}
