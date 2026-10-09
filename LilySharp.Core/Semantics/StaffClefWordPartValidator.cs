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
/// LYS1046: <c>staff bass click</c> (or <c>ossia bass click</c>) where <c>bass</c> is both a part
/// of this file and a clef word. The part wins — <c>StaffTargets</c> reads the item as part
/// <c>bass</c>'s staff and <c>click</c> as a part played to MIDI only, the reading the tab item
/// has had since 2026-10-03 (LYS1044) — and the writer is told how to write either meaning so
/// that it reads one way only.
/// </summary>
internal sealed class StaffClefWordPartValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            var (keyword, part, midiOnly) = node switch
            {
                StaffRenderSyntax s => ("staff", s.PartToken, s.MidiOnlyPartToken),
                OssiaRenderSyntax o => ("ossia", o.PartToken, o.MidiOnlyPartToken),
                _ => (null, null, null),
            };
            if (keyword == null || part == null || midiOnly == null)
                continue;
            _diagnostics.Warning(part.Span, DiagnosticCodes.StaffPartNameIsAlsoAClef,
                $"'{part.Text}' names a part of this file, so this is part '{part.Text}''s {keyword} "
                + $"and '{midiOnly.Text}' after it is a part played to MIDI only — write "
                + $"'{midiOnly.Text}' before the {keyword} to say so. For part '{midiOnly.Text}' on a "
                + $"{part.Text} clef, write 'clef {part.Text}' in part '{midiOnly.Text}' instead.");
        }
    }
}
