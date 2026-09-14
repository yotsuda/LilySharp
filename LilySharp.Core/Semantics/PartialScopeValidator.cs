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
/// Flags a <c>partial</c> (pickup) written where it may not stand. A <c>partial</c> says
/// "the bar it stands in is this long". A section's OPENING bar is the section's: its pickup is
/// a section directive (<c>section A { partial 4  melody {…} bass {…} }</c>, or a standalone
/// <c>section A { partial 4 }</c> beside part-major cells), for every part at once — so a
/// <c>partial</c> written in a part's music within that first bar is refused and pointed at the
/// header (owner's decision 2026-09-15). After the first bar it is legal in a part's or voice's
/// music (<c>… | partial 2. r2. | …</c>, since 2026-09-08), written in every part that shares
/// the bar. The section's own single-voice body is the section's music, so a leading
/// <c>partial</c> there is the directive. Two more places are errors because they hold no bar:
/// the top level of a structured file and a <c>part {}</c> header.
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
            if (where != null)
            {
                _diagnostics.Error(partial.Span, DiagnosticCodes.PartialOutsideSection,
                    $"'partial' cannot go in {where} — there is no bar there for it to shorten. "
                    + "Declare the opening pickup as a section directive (section A { partial 4  … }), "
                    + "or write it in the music at the start of a later bar it shortens, in every "
                    + "part that shares that bar.");
                continue;
            }

            if (OpeningBarOfPartMusic(partial) is { } section)
            {
                string name = section.Name.Text;
                string header = section.Parent is PartDeclarationSyntax
                    ? $"a standalone 'section {name} {{ partial … }}' beside the part cells"
                    : $"'section {name} {{ partial …  … }}', before the part blocks";
                _diagnostics.Error(partial.Span, DiagnosticCodes.PartialOutsideSection,
                    $"A pickup at the start of section {name} belongs to the section header, which "
                    + $"shortens the opening bar for every part at once: write {header}. In a part's "
                    + "music, 'partial' shortens a later bar, written in every part that shares it.");
            }
        }
    }

    /// <summary>
    /// The section whose opening bar <paramref name="partial"/> stands in, when it is written in
    /// a PART's music — a section-major part block (<c>section A { melody { partial 4 … } }</c>)
    /// or a part-major cell (<c>part melody { section A { partial 4 … } }</c>) — and no bar line
    /// of that music comes before it. Null anywhere else: a section directive, the section's own
    /// single-voice body, a phrase body (reusable music, not a section's head), or a later bar.
    /// </summary>
    private static SectionDeclarationSyntax? OpeningBarOfPartMusic(PartialDeclarationSyntax partial)
    {
        for (SyntaxNode? p = partial.Parent; p != null; p = p.Parent)
        {
            switch (p)
            {
                case PhraseDeclarationSyntax:
                    return null;
                case MusicBlockSyntax block when block.Parent is PartBlockSyntax { Parent: SectionDeclarationSyntax sec }:
                    return StandsInOpeningBar(block, partial) ? sec : null;
                case SectionDeclarationSyntax sec:
                    return sec.Parent is PartDeclarationSyntax && StandsInOpeningBar(sec, partial) ? sec : null;
            }
        }
        return null;
    }

    /// <summary>True when no bar line of <paramref name="music"/> precedes the partial — voices
    /// and repeat bodies nested in it included, so a bar line anywhere earlier ends the first bar.</summary>
    private static bool StandsInOpeningBar(SyntaxNode music, PartialDeclarationSyntax partial)
        => !music.DescendantNodes().OfType<BarlineSyntax>().Any(b => b.Span.Start < partial.Span.Start);
}
