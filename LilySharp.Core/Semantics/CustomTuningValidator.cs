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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Semantics;

/// <summary>
/// A custom tuning — the quoted list of open strings a part's <c>tuning "d, a, d g a d'"</c>
/// or a tab item's <c>tab "d, a, d g a d'" gtr</c> writes in a tuning word's place
/// (owner-approved 2026-11-13, grammar evaluation 3) — that spells no tuning:
/// <see cref="DiagnosticCodes.CustomTuningMalformed"/>, LYS4068, an error that says what is
/// wrong with the list and shows one that is right.
/// </summary>
/// <remarks>
/// The reading is <see cref="Tunings.TryReadStrings"/>'s — the one the page, the tab and the
/// twin take the strings from (<see cref="Tunings.Parse"/>) — so what is refused here is
/// exactly what they fall back to the guitar for. Until 2026-11-13 a quoted value after
/// <c>tuning</c> was taken for free text and the part was fretted as a guitar in silence.
/// <para>
/// A list that reads is also checked against the part it re-strings: on a BASS part
/// (<c>instrument bass</c> …) a list no string of which is a bass's is almost always the
/// strings written as the part's notes are — an octave above their sound — and is warned
/// with the list an octave down (the same code; the tab is drawn on the strings as written).
/// </para>
/// </remarks>
internal sealed class CustomTuningValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
    private const string Shape =
        "A tuning written out is its open strings from the highest string number (usually the lowest "
        + "string) to string 1, spaced, each a pitch name with its octave marks, as it sounds: "
        + "'tuning \"d, a, d g a d'\"' (DADGAD), 'tab \"d, a, d g a d'\" NAME' for one tab.";

    public void Validate(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var part in TopLevelNodes.OfRoot<PartDeclarationSyntax>(root))
        {
            if (part.Properties.FirstOrDefault(p => p.NameToken.Text == "tuning") is not { } tuning
                || tuning.GetChild(2) is not SyntaxTokenNode { Kind: SyntaxKind.StringLiteral } list)
                continue;
            if (Check(list))
                CheckBass(list, PartHeaderDefaults.Read(part).Preset);
        }
        foreach (var tab in root.DescendantNodes<TabRenderSyntax>())
            if (tab.CustomTuningToken is { } list && Check(list))
                CheckBass(list, tab.PartToken is { } name && ConcertPitch.FindPart(root, name.Text) is { } part
                    ? PartHeaderDefaults.Read(part).Preset : null);
    }

    /// <summary>Reports a list that spells no tuning; true when it spells one.</summary>
    private bool Check(SyntaxTokenNode list)
    {
        if (Tunings.TryReadStrings(list.Text, out _, out string? problem))
            return true;
        _diagnostics.Error(list.Span, DiagnosticCodes.CustomTuningMalformed,
            $"{list.Text} is not a tuning: {problem}. {Shape}");
        return false;
    }

    /// <summary>Warns when a bass part's list is written at the pitch of the part's notes — an
    /// octave above the strings' sound — which draws a guitar's tab under a bass.</summary>
    private void CheckBass(SyntaxTokenNode list, string? preset)
    {
        if (preset is null || InstrumentDefaults.GetTuning(preset) is not { } usualWord
            || !Tunings.IsBass(Tunings.Parse(usualWord)) || InstrumentDefaults.GetTransposition(preset) != -12
            || !Tunings.TryReadStrings(list.Text, out int[] strings, out _)
            || Tunings.IsBass(Tunings.Custom(strings)))
            return;
        var lowered = Tunings.Custom([.. strings.Select(s => s - 12)]);
        if (!Tunings.IsBass(lowered))
            return;
        _diagnostics.Warning(list.Span, DiagnosticCodes.CustomTuningMalformed,
            $"{list.Text} is above every string of a bass: instrument {preset} is written an octave above its "
            + $"sound, and a tuning names its strings as they sound. For these strings an octave lower write "
            + $"{Tunings.Spell(lowered)}.");
    }
}
