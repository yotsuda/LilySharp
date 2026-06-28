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
public sealed class LyricSyllableValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        IReadOnlyList<LyricSyllableWarning> warnings;
        try
        {
            var collector = new MeasureCollector();
            collector.Collect(tree);
            warnings = collector.LyricWarnings;
        }
        catch
        {
            // A malformed score that the collector cannot process surfaces its real
            // error through the parser / other validators; we add nothing here.
            return;
        }

        foreach (var w in warnings)
        {
            bool one = w.UnplacedSyllables == 1;
            string phrase = one ? "syllable has" : "syllables have";
            _diagnostics.Warning(w.Span, DiagnosticCodes.LyricSyllableOverflow,
                $"{w.UnplacedSyllables} lyric {phrase} no note to align with " +
                "(more syllables than notes) and will not be shown");
        }
    }
}
