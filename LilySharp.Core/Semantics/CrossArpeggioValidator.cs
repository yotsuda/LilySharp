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
/// Warns at an <c>@arpeggio(cross)</c> that has nothing to run down to
/// (<see cref="DiagnosticCodes.CrossArpeggioWithoutPartner"/>, LYS4064): its part is not in a
/// grand staff, or is the grand staff's lowest staff, or nothing begins on the staff below at
/// the chord's moment. The arpeggio is drawn on its own chord, and the message says which.
/// </summary>
/// <remarks>
/// The pairing is the page's (<see cref="CrossArpeggios.Connect"/>), for the reason
/// <see cref="SlurPairingValidator"/> gives: whether a chord has a partner is decided by the
/// rule the page draws by — the collected bars and their clocks, a phrase's notes where the
/// part plays them — not by a second reading of the source.
/// <para>
/// ⚠️ OF THE FIRST SCORE, as every shared-collect validator is: a part only a later score
/// draws is not judged.
/// </para>
/// </remarks>
internal sealed class CrossArpeggioValidator : ISharedCollectValidator
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(() => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // Cheap exit: no cross arpeggio, nothing to pair — and no collect to pay for. The
        // marks are kept by their host's offset, which is the address the page's item carries.
        Dictionary<int, MusicMarkSyntax>? marks = null;
        foreach (var mark in tree.GetRoot().DescendantNodes<MusicMarkSyntax>())
            if (mark.Parent is { } host && AnnotationValues.IsArpeggioCross(mark))
                (marks ??= new())[host.SourceStart] = mark;
        if (marks is null || sharedCollect.Value?.Collected is not { } score)
            return;

        foreach (var arpeggio in score.Arpeggios)
        {
            if (!arpeggio.Cross || arpeggio.PartnerStaffIndex >= 0
                || !marks.TryGetValue(arpeggio.SourcePosition, out var mark))
                continue;
            string written = "@arpeggio(" + string.Join(" ", mark.Arguments.Select(a => a.Text)) + ")";
            string plain = AnnotationValues.IsArpeggioBracket(mark) ? "@arpeggio(bracket)"
                : AnnotationValues.ArpeggioArrow(mark) switch { > 0 => "@arpeggio(up)", < 0 => "@arpeggio(down)", _ => "@arpeggio" };
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            string why = CrossArpeggios.WhyAlone(score, arpeggio) switch
            {
                CrossArpeggioGap.NotInGrandStaff =>
                    "this part's staff is in no grand staff here. Put the two hands in a grandStaff { }, "
                    + $"or write '{plain}'",
                CrossArpeggioGap.NoStaffBelow =>
                    "this is the lowest staff of its grand staff: there is no staff below it. Write the mark "
                    + $"on the upper staff's chord, or write '{plain}'",
                _ =>
                    "nothing begins on the staff below at this moment (a rest, a held note or another rhythm). "
                    + $"Give the staff below a note or chord that begins here, or write '{plain}'",
            };
            _diagnostics.Add(Diagnostic.Warning(mark.Span, DiagnosticCodes.CrossArpeggioWithoutPartner,
                $"'{written}' runs one line from this chord down through the chord of the staff below it in a "
                + $"grand staff, and {why}. It is drawn on its own chord."));
        }
    }
}
