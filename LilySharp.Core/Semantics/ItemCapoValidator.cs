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

using System.Globalization;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// The capo of ONE score item — <c>tab gtr capo 2</c> (owner-approved 2026-11-13, grammar
/// evaluation 3): that tab counts its frets from fret 2 instead of from the file's
/// <c>capo N</c> (<see cref="CapoDirective"/>); <c>capo 0</c> is no capo on a tab of a capoed
/// file. Two guitars, one capoed, are two tab items.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN, as the file's capo is (LilyPond's TabStaff has none). WHAT AN ITEM'S CAPO IS
/// follows from what the file's was made to be (<see cref="CapoDirective"/>'s remarks): the
/// capo changes what a <c>chord(D x32010)</c> item SOUNDS, and a sound must not change with the
/// edition — so the capo that raises the shapes, names the pressed chords and prints "Capo N"
/// stays the file's, and a score item's capo is the one thing of a capo that IS an edition's:
/// where a tab counts its frets from. The notes are written as they sound either way.
/// </para>
/// <para>
/// So <c>staff gtr capo 2</c> is an ERROR (LYS4069), not a silent word: a staff shows its
/// notes as they sound, and the chord diagrams over it read the file's capo — on the page, in
/// the MIDI, in MusicXML and in the twin alike — so a capo there could only be a capo of the
/// diagrams on one of the four, which is a wrong page. The message names the two spellings
/// that do something. A tab whose own capo differs from the file's under a part that writes
/// chord SHAPES is warned (same code): the diagram and the tab under it then show different
/// frets, and the diagrams' capo of one part is not a spelling yet (docs/ROADMAP.md).
/// </para>
/// </remarks>
internal sealed class ItemCapoValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    /// <summary>
    /// The fret of the tab item's own capo — 0 (none) to <see cref="CapoDirective.MaxFret"/> —
    /// or null: the item writes none, or one outside the range (refused here, and the tab then
    /// reads the file's). THE one reading: the page's (<c>RenderSpecParser.ParseTab</c>) and
    /// this validator's.
    /// </summary>
    internal static int? CapoOf(TabRenderSyntax tab)
        => tab.CapoToken is { } token
           && int.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int fret)
           && fret <= CapoDirective.MaxFret
            ? fret
            : null;

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
        foreach (var staff in root.DescendantNodes<StaffRenderSyntax>())
        {
            if (staff.CapoToken is not { } fret)
                continue;
            string part = staff.PartToken?.Text ?? "NAME";
            int wordStart = CapoWordStart(staff, fret);
            _diagnostics.Error(new TextSpan(wordStart, fret.Span.End - wordStart), DiagnosticCodes.ItemCapo,
                $"'capo {fret.Text}' on a staff item changes nothing: a staff shows its notes as they sound, with a capo "
                + $"or without. An item's capo is a tab's - 'tab {part} capo {fret.Text}' counts that tab's frets from the "
                + $"capo - and the capo the chord diagrams, the chord names and every tab read is the file's: a "
                + $"top-level 'capo {fret.Text}'.");
        }

        int fileCapo = CapoDirective.CapoOf(root);
        foreach (var tab in root.DescendantNodes<TabRenderSyntax>())
        {
            if (tab.CapoToken is not { } fret)
                continue;
            string part = tab.PartToken?.Text ?? SyntaxFacts.UnnamedPartName;
            if (CapoOf(tab) is not int own)
            {
                _diagnostics.Error(fret.Span, DiagnosticCodes.ItemCapo,
                    $"'{fret.Text}' is not a capo fret: a tab's own capo is 0 (no capo, where the file has one) to "
                    + $"{CapoDirective.MaxFret} - 'tab {(tab.PartToken is null ? "" : part + " ")}capo 2'. The tab reads the file's capo.");
                continue;
            }
            if (own != fileCapo && WritesShapes(root, part))
                _diagnostics.Warning(fret.Span, DiagnosticCodes.ItemCapo,
                    $"this tab counts its frets from its own capo ({Say(own)}), but the chord shapes part '{part}' writes "
                    + $"- its diagrams, its chord(...) and diagram(...) items - are pressed above the file's ({Say(fileCapo)}), "
                    + "so a diagram and the tab under it show different frets. A capo of one part's diagrams is not a "
                    + $"spelling yet: the shapes read the top-level 'capo N' alone.");
        }
    }

    private static string Say(int capo) => capo == 0 ? "none" : "fret " + capo.ToString(CultureInfo.InvariantCulture);

    /// <summary>Where the item's <c>capo</c> word starts: the token before the fret.</summary>
    private static int CapoWordStart(SyntaxNode item, SyntaxTokenNode fret)
    {
        for (int i = 1; i < item.SlotCount; i++)
            if (ReferenceEquals(item.GetChild(i), fret) || item.GetChild(i) is SyntaxTokenNode t && t.Span.Start == fret.Span.Start)
                return item.GetChild(i - 1) is SyntaxTokenNode word ? word.Span.Start : fret.Span.Start;
        return fret.Span.Start;
    }

    /// <summary>Whether the part's music writes a chord shape — a <c>chord(…)</c> /
    /// <c>diagram(…)</c> item, an <c>@chord</c> or an <c>@diagram</c> — in any section.</summary>
    private static bool WritesShapes(SyntaxNode root, string part)
        => root.DescendantNodes<PartBlockSyntax>().Any(block => block.Name == part && block.DescendantNodes().Any(n =>
            n is ChordSyntax { IsShapeChord: true }
            || n is MusicMarkSyntax { Name: "chord" or "diagram" }
            || n is ArticulationSyntax { Name: "chord" or "diagram" }));
}
