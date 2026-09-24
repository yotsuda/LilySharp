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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a <c>lyrics</c> line carries MORE syllables than the notes it binds
/// to. The extra syllables run off the end of the melody and are silently dropped
/// from the engraving, which shifts every following syllable off its note — an easy
/// authoring mistake to make by miscounting. A line SHORTER than the melody is fine
/// (melisma, extenders, an instrumental tail), so only overflow is reported.
/// </summary>
/// <remarks>
/// The note-to-syllable alignment is non-trivial (sections, named voices, repeats,
/// phrase expansion), and <see cref="MeasureCollector"/> already computes it exactly
/// for rendering. Rather than re-derive a parallel, fragile count from the tree, this
/// validator runs the collector and reads back the overflow it recorded as a side
/// effect (<see cref="MeasureCollector.LyricWarnings"/>).
/// </remarks>
internal sealed class LyricSyllableValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.LyricWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            string tail = w.UnplacedSyllables == 1
                ? "it will not be shown"
                : $"it and the {w.UnplacedSyllables - 1} after it will not be shown";
            // ASCII punctuation only: this exact string reaches legacy-codepage
            // consoles through the CLI.
            // WHY the bar ran out, when a slur or a tie held some of its notes: since 0.8.0 a
            // note inside a slur (after its first) or reached by a tie takes no syllable, as in
            // LilyPond. Two habits hit it, and each gets its own advice:
            //  * a slur written only as a legato/phrasing mark over a lyric line swallows the
            //    syllables (ChatGPT's 01_evening_song.lys lost 22) — a phrasing slur is not
            //    melisma-busy, so it is the spelling to offer;
            //  * a lyric `~` / `_` written to hold a syllable over a TIED note, which the tie now
            //    holds by itself, takes the next note instead (the Lab corpus's amazing-grace.lys).
            string cause = "";
            if (w.SlurHeldInFirstBar > 0)
                cause += $". {w.SlurHeldInFirstBar} note(s) of that bar are inside a slur and take no " +
                         "syllable (a slur holds its syllable, as in LilyPond); if the slur only marks " +
                         "phrasing, write it as @phrasingSlur ... @!phrasingSlur, which holds none";
            if (w.TieHeldInFirstBar > 0)
                cause += $". {w.TieHeldInFirstBar} note(s) of that bar are reached by a tie and take no " +
                         "syllable (the tie already holds the one before); a lyric '~' or '_' written " +
                         "for a tied note now takes the next note - drop it";
            _diagnostics.Warning(w.Span, DiagnosticCodes.LyricSyllableOverflow,
                $"lyric syllable '{w.FirstSyllable}' (bar {w.FirstBar} of its lyrics line) " +
                $"has no note to align with; {tail}{cause}");
        }
    }
}
