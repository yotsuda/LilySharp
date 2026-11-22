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
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>pass 2 { … }</c> (docs/eval4-features-design.md §3): a pass below 1 or a pass named twice
/// (<see cref="DiagnosticCodes.PassNumberInvalid"/>), a region inside another
/// (<see cref="DiagnosticCodes.PassInsidePass"/>), and a region none of whose notes the music
/// ever plays (<see cref="DiagnosticCodes.PassNeverPlayed"/>, read off the MIDI — only for a
/// book that writes a <c>pass</c>).
/// </summary>
internal sealed class PassRegionValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var starts = tree.GetRoot().DescendantNodes<PassStartSyntax>().ToList();
        if (starts.Count == 0)
            return;
        IReadOnlyCollection<int> silent = [];
        try
        {
            var exporter = new Midi.MidiExporter();
            exporter.Export(tree);
            silent = exporter.SilentPassRegions;
        }
        catch
        {
            // a book that cannot play surfaces its real error elsewhere
        }
        foreach (var start in starts)
        {
            if (silent.Contains(start.SourceStart))
                _diagnostics.Warning(start.Keyword.Span, DiagnosticCodes.PassNeverPlayed,
                    $"These notes are never played: the music does not come here on pass {start.Passes}. "
                    + "The passes are counted by the repeat around them ('|: … :|', ':|*3', the form's repeat); outside a repeat the music is played once, on pass 1.");
            var numbers = start.Numbers.ToList();
            var tokens = start.NumberTokens.ToList();
            for (int i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] < 1)
                    _diagnostics.Error(tokens[i].Span, DiagnosticCodes.PassNumberInvalid,
                        $"'pass {tokens[i].Text}' names no pass: the passes of a repeat are counted from 1 - 'pass 1' is the first time through, 'pass 2' the second.");
                else if (numbers.IndexOf(numbers[i]) < i)
                    _diagnostics.Error(tokens[i].Span, DiagnosticCodes.PassNumberInvalid,
                        $"Pass {numbers[i]} is named twice in this 'pass': write each pass once ('pass 1 3 {{ … }}').");
            }
            if (IsInsideAnotherRegion(start))
                _diagnostics.Error(start.Keyword.Span, DiagnosticCodes.PassInsidePass,
                    "A 'pass' region is written inside another: its notes sound on the passes of the outer one. "
                    + "Close the outer region first, or name the passes of the inner notes on the outer region.");
        }
    }

    /// <summary>Whether a region opened before <paramref name="start"/> among the items it is
    /// written in — or in the items around them — is still open there.</summary>
    private static bool IsInsideAnotherRegion(PassStartSyntax start)
    {
        SyntaxNode cur = start;
        while (cur.Parent is { } parent)
        {
            int open = 0;
            for (int i = 0; i < parent.SlotCount; i++)
            {
                if (parent.GetChild(i) is not { } item)
                    continue;
                if (item.SourceStart >= cur.SourceStart && item.Kind == cur.Kind)
                    break;
                if (item is PassStartSyntax)
                    open++;
                else if (item is PassEndSyntax && open > 0)
                    open--;
            }
            if (open > 0)
                return true;
            cur = parent;
        }
        return false;
    }
}
