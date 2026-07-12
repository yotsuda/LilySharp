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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Warns when a note rendered on a tab staff falls outside the instrument's
/// playable range. A note below the lowest open string is silently clamped to
/// fret 0 (shown as a wrong open string), which otherwise hides the most common
/// authoring slip — a section whose relative octave reset landed an octave too low.
/// </summary>
/// <remarks>
/// Like <see cref="TabTieStringValidator"/>, this runs the collector (the same
/// multi-staff path the renderer uses, so the range check matches what is drawn)
/// and reads back the out-of-range notes it recorded as a side effect.
/// </remarks>
internal sealed class TabRangeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var seen = new HashSet<int>();
        foreach (var spec in RenderSpecParser.FindAll(tree).Where(s => s.HasTab))
        {
            IReadOnlyList<TabRangeWarning> warnings;
            try
            {
                var collector = new MeasureCollector();
                collector.CollectMultiStaff(tree, spec);
                warnings = collector.TabRangeWarnings;
            }
            catch
            {
                // A malformed score surfaces its real error elsewhere; add nothing.
                continue;
            }

            foreach (var w in warnings)
            {
                if (!seen.Add(w.SourcePosition)) continue; // one note, one warning
                _diagnostics.Warning(new TextSpan(w.SourcePosition, 1),
                    DiagnosticCodes.TabOutOfRange,
                    w.BelowRange
                        ? "note is below the tab's lowest string and was omitted from the tab " +
                          "(it shows only on the notation staff) — likely an octave too low"
                        : "note is above the tab's range (no fret 0-24 on any string) — likely an octave too high");
            }
        }
    }
}
