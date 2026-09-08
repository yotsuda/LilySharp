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
/// Flags a <c>partial</c> (pickup) written where no bar can take it. A <c>partial</c> says
/// "the bar it stands in is this long", and it is legal wherever a bar is: a section
/// directive (<c>section A { partial 4  melody {…} bass {…} }</c> — every part's opening bar),
/// a single-voice section body, and — since 2026-09-08 (owner's decision) — anywhere in a
/// part's or voice's music, mid-piece included (<c>… | partial 2. r2. | …</c>). Two places
/// remain errors because they hold no bar: the top level of a structured file (the piece-wide
/// <c>partial</c> is a section directive there) and a <c>part {}</c> header.
/// </summary>
/// <remarks>
/// <para>
/// LILYPOND-REF: ly/music-functions-init.ly:1697-1705 partial = context-spec-music 'Timing —
/// "adjust the measure position to end the current measure at dur past the point of use";
/// <c>Timing</c> is an alias of <c>Score</c> (engraver-init.ly), so ONE
/// <c>\partial</c> in any staff moves every staff's clock. Lily# has no shared Timing: each
/// voice's <c>MeasureBuilder</c> keeps its own bar length, and a mid-music <c>time</c> is
/// restated per part (<see cref="MeasureValidator"/>, the per-block timeline). A mid-music
/// <c>partial</c> follows the same rule — write it in every part that shares the bar; a part
/// that omits it keeps a full bar and <see cref="CrossPartMeasureValidator"/> reports the
/// mismatch. The rule is per part, not "score-wide like LilyPond", by decision: no book of the
/// 900 swept asked for a shared clock, and one spelling of the fact is enough.
/// </para>
/// <para>
/// Position within the bar: the collector (<c>MeasureBuilder.SetPartial</c>) and the fullness
/// check (<see cref="MeasureValidator"/>) both read the bar's WHOLE length as the declared
/// value, so the natural place is the bar's start (right after a <c>|</c>, or at the block's
/// head); written after notes, the bar is still "N long" and the fill check says what did not
/// fit. That is a simplification of LilyPond's "the REMAINING length", which differs only in
/// that placement.
/// </para>
/// <para>
/// A bare-music file (no <c>part</c> / <c>section</c> / <c>form</c>) is a plain note stream, so a
/// leading <c>partial</c> there is just that music's pickup and is fine.
/// </para>
/// </remarks>
internal sealed class PartialScopeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // Bare music (no structural nodes) is a plain note stream — a leading `partial` there
        // is the music's own pickup. The placement rule only bites once the file is structured.
        bool structured = root.DescendantNodes().Any(n =>
            n is PartDeclarationSyntax or SectionDeclarationSyntax or FormDeclarationSyntax);
        if (!structured)
            return;

        foreach (var partial in root.DescendantNodes().OfType<PartialDeclarationSyntax>())
        {
            string? where = partial.Parent switch
            {
                PartDeclarationSyntax => "a part header",
                _ when partial.Parent == root => "the top level",
                _ => null,   // a section directive, a section body, a part block, a voice: a bar is there
            };
            if (where == null)
                continue;
            _diagnostics.Error(partial.Span, DiagnosticCodes.PartialOutsideSection,
                $"'partial' cannot go in {where} — there is no bar there for it to shorten. "
                + "Declare the opening pickup as a section directive (section A { partial 4  … }), "
                + "or write it in the music at the start of the bar it shortens, in every part "
                + "that shares that bar.");
        }
    }
}
