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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>instrument piccolo</c> in the music (docs/eval4-features-design.md §1): a preset that is
/// none (the part header's message, <see cref="DiagnosticCodes.UnknownSymbolCase"/>), and a change
/// the page cannot draw yet (<see cref="DiagnosticCodes.InstrumentChangeMovesThePage"/>).
/// </summary>
/// <remarks>
/// A page prints a part with ONE transposition (ConcertPitch: the shift rides the part's
/// <c>transpose</c>), so a change to an instrument of another transposition is refused where the
/// page would have to move: a part typed at concert pitch (its letters sound; a transposed page
/// prints them for the instrument), and a part typed as written that a score prints at concert
/// pitch, when the two instruments differ by more than octaves (an octave-only transposer keeps
/// its octave in a concert score). A part typed as written and printed for its player — every
/// book that writes no <c>pitch</c> — draws as written, and only its sound moves.
/// </remarks>
internal sealed class InstrumentSwitchValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var changes = root.DescendantNodes<InstrumentSwitchSyntax>().ToList();
        if (changes.Count == 0)
            return;
        bool fileConcert = ConcertPitch.FileIsConcert(root);
        var renders = root.ChildNodes().OfType<RenderDeclarationSyntax>().ToList();
        foreach (var change in changes)
        {
            string preset = change.Preset;
            if (preset.Length == 0)
                continue; // the parser said so
            var first = change.ValueTokens.First();
            if (!InstrumentDefaults.IsKnownInstrument(preset))
            {
                if (!Syntax.StringLiteral.IsClosed(first.Text))
                    _diagnostics.Error(first.Span, DiagnosticCodes.UnknownSymbolCase, SymbolCaseValidator.UnknownPresetMessage(preset));
                continue;
            }
            if (PartOf(change) is not { } partName || ConcertPitch.FindPart(root, partName) is not { } part)
                continue;
            var header = PartHeaderDefaults.Read(part);
            int to = InstrumentDefaults.GetTransposition(preset);
            bool lettersConcert = ConcertPitch.PartMode(part) ?? fileConcert;
            int chromatic = to % 12 == 0 ? 0 : to;
            string? why = lettersConcert && to != header.TranspositionSemitones
                    ? $"part '{partName}' is typed at concert pitch, and its transposed page"
                : !lettersConcert && chromatic != header.ConcertShiftSemitones
                    && renders.Any(r => ConcertPitch.PageMode(r, partName) == true)
                    ? $"a score prints part '{partName}' at concert pitch, and that page"
                : null;
            if (why != null)
                _diagnostics.Error(change.Keyword.Span, DiagnosticCodes.InstrumentChangeMovesThePage,
                    $"The {InstrumentDefaults.PrintedName(preset) ?? preset} transposes otherwise than the part's own instrument: {why} "
                    + "would have to change its transposition here, and a page prints a part with one. Not drawn yet: give the "
                    + "instrument a part of its own, or type the part as written ('pitch written') and print it for the player.");
        }
    }

    /// <summary>The part whose music the change is written in, or null (a phrase, played by
    /// whichever part calls it).</summary>
    private static string? PartOf(SyntaxNode node)
    {
        for (var cur = node.Parent; cur != null; cur = cur.Parent)
        {
            if (cur is PartBlockSyntax block)
                return block.Name;
            if (cur is PartDeclarationSyntax part)
                return part.Name.Text;
            if (cur is PhraseDeclarationSyntax)
                return null;
        }
        return null;
    }
}
