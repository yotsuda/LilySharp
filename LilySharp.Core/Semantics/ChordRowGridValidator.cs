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
/// Surfaces the chord-row grid faults the shared collect recorded
/// (<see cref="Svg.Collector.MeasureCollector.ChordRowGridWarnings"/>): a bar
/// whose slot count fits no beat-grid shape (LYS2009 — the bar fell back to
/// dividing equally), and a <c>.</c> at a bar's head with nothing to extend
/// (LYS2010). Reads back what the walk that PLACES the symbols recorded, so the
/// diagnostic can never disagree with what is drawn (the
/// <see cref="BeamPairingValidator"/> pattern).
/// </summary>
internal sealed class ChordRowGridValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(new System.Lazy<Svg.Collector.MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(System.Lazy<Svg.Collector.MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.ChordRowGridWarnings;
        if (warnings == null)
            return;

        // A section replayed by the structure walks its bars once per occurrence;
        // one spelling is one fault, so report each source position once.
        foreach (var w in warnings.DistinctBy(w => (w.SourcePosition, w.HeadDot)))
        {
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            if (w.HeadDot)
                _diagnostics.Error(new TextSpan(w.SourcePosition, 1),
                    DiagnosticCodes.ChordExtendAtBarHead,
                    "a '.' at the start of a chord-row bar has nothing to extend - "
                    + "a '.' never reaches across a barline. Write the chord again "
                    + "('| C | C |', not '| C | . |'); the slot's time still passes, "
                    + "so the rest of the bar stays where it was.");
            else
                _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                    DiagnosticCodes.ChordSlotMismatch,
                    $"{w.SlotCount} chord slot(s) in a {w.Beats}/{w.BeatType} bar fit no "
                    + $"beat: one slot takes the bar, a multiple of the beat count splits "
                    + $"each beat, a divisor groups whole beats. The bar is divided "
                    + $"equally instead; add '.' to land on beats "
                    + $"('| C F G |' -> '| C F G . |' in 4/4).");
        }
    }
}
