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
/// Warns when a text-spanner mark pairs with nothing — a <c>@rit</c> / <c>@textSpan("…")</c>
/// that no <c>@!</c> ever closes, a <c>@!rit</c> with no span open in its voice, or a second
/// START written inside an open one. Every one of them draws nothing, and until the
/// terminator existed nothing could: a bare <c>@rit</c> was given a one-measure default and
/// the reader was never told that the length was the engine's guess.
/// </summary>
/// <remarks>
/// Like <see cref="SlurPairingValidator"/>, this reads back what the shared collector
/// already decided (<see cref="MeasureCollector.UnpairedTextSpanWarnings"/>) rather than
/// re-deciding the pairing — the reading and the drawing are two halves of ONE call
/// (<c>TextSpannerEngraver.PairTextSpanners</c>), so a mark cannot be warned about and drawn
/// at the same time.
/// <para>
/// LILYPOND-REF: lily/text-spanner-engraver.cc:59-88 Text_spanner_engraver::process_music, :117-127 Text_spanner_engraver::finalize — the three warnings this
/// surfaces are that engraver's three, in the same three situations.
/// </para>
/// </remarks>
internal sealed class TextSpanPairingValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedTextSpanWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.UnpairedTextSpan,
                w.Fault switch
                {
                    TextSpanPairingFault.Unterminated =>
                        "a text spanner is never closed, so neither its word nor its line is "
                        + "drawn; write '@!rit' (or '@!textSpan') on the note it should reach "
                        + "- a span with no end has no length to draw",
                    TextSpanPairingFault.StopWithNoStart =>
                        "this '@!' closes nothing, so nothing is drawn; no text spanner is "
                        + "open in this voice - note that a spanner does not carry into "
                        + "another voice",
                    _ =>
                        "a text spanner is already open in this voice, so this one is "
                        + "ignored; close the first with '@!' before starting a second "
                        + "- spanners do not nest",
                });
        }
    }
}
