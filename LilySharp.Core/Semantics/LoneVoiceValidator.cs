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
/// Warns for a span that opens exactly one <c>voice { … }</c>. Polyphony is what the span
/// exists for: with a second voice the collector forces stems up/down and gives each branch
/// its own track, but with only one it is entirely transparent — the block changes no stem,
/// no beam, no duration and no octave, so the music engraves exactly as it would with the
/// braces deleted. Someone who wrote it meaning "this staff is polyphonic now" gets a
/// single-voice score and nothing said so.
/// </summary>
/// <remarks>
/// Two spellings are exempt, because for them the lone voice is not a no-op:
/// <list type="bullet">
/// <item>A NAMED voice (<c>voice sop { … }</c>) publishes that name for a
/// <c>lyrics sop { … }</c> block to bind to (<c>MeasureCollector._voiceMeasuresByName</c>),
/// so the block does carry meaning on its own.</item>
/// <item>A span recovered from the removed <c>&lt;&lt; … &gt;&gt;</c> syntax, which already
/// reports its own error (LYS0008) — a second diagnostic on the same text would only
/// crowd it.</item>
/// </list>
/// </remarks>
internal sealed class LoneVoiceValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var span in tree.GetRoot().DescendantNodes().OfType<ParallelExpressionSyntax>())
        {
            // Only the `voice` spelling; `<< … >>` recovery opens with the angle token.
            if (span.OpenAngle.Kind != SyntaxKind.VoiceKeyword)
                continue;
            var named = span.NamedVoices.ToList();
            if (named.Count != 1 || named[0].Name != null)
                continue;

            // The `voice` keyword itself, not the whole block — the point is the wrapper,
            // and squiggling several bars of music would bury it.
            // Punctuation is ASCII apart from the ellipsis, which quotes the language the
            // same way LYS0010 (the other voice-block message) does.
            _diagnostics.Warning(span.OpenAngle.Span, DiagnosticCodes.LoneVoiceBlock,
                "a single 'voice { … }' engraves exactly like the music written without it - "
                + "stems, beams and durations are unchanged, because the up/down forcing needs "
                + "a second voice. Write the other voice (voice { … } voice { … }) to make the "
                + "passage polyphonic, or drop the block. To name a track for lyrics to bind to, "
                + "name it: voice sop { … }.");
        }
    }
}
