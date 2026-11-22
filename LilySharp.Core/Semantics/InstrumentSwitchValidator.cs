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
/// that leaves the later systems of a part that names them with no name
/// (<see cref="DiagnosticCodes.InstrumentChangeHasNoShortName"/>).
/// </summary>
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
            // A part that heads its later systems with a short name: the change names its own,
            // or those systems are left unnamed (SharedRenderer.ShortNameAt).
            if (part.ShortName != null && change.ShortName == null && !ReturnsHome(change))
                _diagnostics.Warning(change.Keyword.Span, DiagnosticCodes.InstrumentChangeHasNoShortName,
                    $"Part '{partName}' heads its later systems with a short name ('{part.ShortName}'), and this change writes none: "
                    + "the systems after it print no name for the part. Write the instrument's names after it, long then short: "
                    + $"instrument {preset} \"{change.PrintedName}\" \"…\".");
        }
    }

    /// <summary>
    /// Whether <paramref name="change"/> goes back to its part's own instrument writing no names
    /// (<c>instrument flute</c> in a flute part): the part's own names head its systems again.
    /// </summary>
    internal static bool ReturnsHome(InstrumentSwitchSyntax change)
    {
        if (change.LongName != null || PartOf(change) is not { } partName)
            return false;
        SyntaxNode root = change;
        while (root.Parent is { } parent)
            root = parent;
        return ConcertPitch.FindPart(root, partName) is { } part && PartHeaderDefaults.Read(part).Preset == change.Preset;
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
