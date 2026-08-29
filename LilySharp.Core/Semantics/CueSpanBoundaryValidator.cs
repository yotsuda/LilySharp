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
/// Rejects a slur or a tie with one end inside a <c>cue { … }</c> region and the other
/// outside it — the span LilyPond has no way to engrave, because a cue is a Voice context of
/// its own and both engravers live in the Voice.
/// </summary>
/// <remarks>
/// ⚠️ THIS IS THE ONE DIAGNOSTIC IN THE CUE FAMILY THAT REPORTS INK THAT EXISTS, not ink that
/// went missing. LYS4010 and LYS4007 name marks the renderer silently DROPS; here Lily# draws
/// the curve happily and it is LilyPond that cannot. MEASURED on 2.26.0 across all four
/// spellings — see <see cref="CueSpanBoundaryWarning"/> — the worst of them being the tie
/// leaving a cue, which LilyPond drops with NO WARNING AT ALL (the book engraves
/// byte-for-byte as the same bar with no tie written). So a writer moving between the two
/// tools gets no signal from either side; this is the signal.
/// <para>
/// Like <see cref="SlurPairingValidator"/>, the pairing is the collector's, read back from the
/// side table it fills, so this can never claim a span the renderer did not actually form.
/// </para>
/// <para>
/// An ERROR rather than a warning, on the pre-release rule that only the tightening direction
/// closes later: a spelling accepted in 0.3.0 cannot be rejected in 0.4.0, while
/// <c>error → warning → drawn</c> costs nobody anything. Since <c>lysc</c> is best-effort, the
/// page still comes out — the severity decides what is SAID, not what is engraved.
/// </para>
/// </remarks>
internal sealed class CueSpanBoundaryValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.CueSpanBoundaryWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            string span = w.Kind == CueSpanKind.Slur ? "slur" : "tie";
            string direction = w.Crossing switch
            {
                CueSpanCrossing.OutOfCue => "it starts inside a 'cue { … }' and ends outside it",
                CueSpanCrossing.BetweenCues =>
                    "it starts in one 'cue { … }' and ends in the NEXT one, and two cue blocks "
                    + "side by side are two voices",
                _ => "it starts outside a 'cue { … }' and ends inside it",
            };
            _diagnostics.Error(new TextSpan(w.SourcePosition, 1),
                DiagnosticCodes.SpanCrossesCueBoundary,
                $"a {span} cannot cross a cue boundary: {direction}. A cue is a voice of its "
                + $"own, so LilyPond drops such a {span} entirely - close it inside the cue, or "
                + "move the note it reaches for out of the cue");
        }
    }
}
