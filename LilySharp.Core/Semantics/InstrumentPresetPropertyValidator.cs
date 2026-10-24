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
/// Two slips a part's own property makes against its <c>instrument</c> preset (owner's request
/// 2026-10-24): LYS1050, an <c>octave</c> written below the preset's where the preset already
/// sounds below the page (the part comes out two octaves low), and LYS1051, a <c>tuning</c> of
/// another instrument's strings. Both are warnings: the part reads as written.
/// </summary>
internal sealed class InstrumentPresetPropertyValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        foreach (var part in tree.GetRoot().DescendantNodes().OfType<PartDeclarationSyntax>())
        {
            var defaults = PartHeaderDefaults.Read(part);
            if (defaults.Preset is not { } preset || !InstrumentDefaults.IsKnownInstrument(preset))
                continue;
            CheckOctave(part, defaults, preset);
            CheckTuning(part, preset);
        }
    }

    /// <summary>LYS1050: the octave's number, when it is below the preset's and the part sounds
    /// an octave or more below the page.</summary>
    private void CheckOctave(PartDeclarationSyntax part, PartHeaderDefaults defaults, string preset)
    {
        int presetOctave = InstrumentDefaults.GetDefaults(preset).Octave;
        int shift = defaults.TranspositionSemitones;
        if (defaults.ExplicitOctave is not int written || written >= presetOctave || shift > -12)
            return;
        if (part.Properties.FirstOrDefault(p => p.NameToken.Text == "octave")?.GetChild(2) is not SyntaxTokenNode number)
            return;
        string how = shift % 12 == 0 ? $"{-shift / 12} octave{(shift == -12 ? "" : "s")}" : $"{-shift} semitones";
        _diagnostics.Warning(number.Span, DiagnosticCodes.OctaveBelowTransposingPreset,
            $"octave {written} writes a bare c as C{written}, which sounds {Spell(12 * (written + 1) + shift)}: "
            + $"instrument {preset} already sounds {how} below the page. The octave is the written register, "
            + $"not the sounding one - instrument {preset} is written from octave {presetOctave} "
            + $"(a bare c is C{presetOctave} and sounds {Spell(12 * (presetOctave + 1) + shift)}).");
    }

    /// <summary>LYS1051: the tuning word, when its family is not the preset's strings'.</summary>
    private void CheckTuning(PartDeclarationSyntax part, string preset)
    {
        if (InstrumentDefaults.GetTuning(preset) is not { } presetWord
            || part.Properties.FirstOrDefault(p => p.NameToken.Text == "tuning") is not { } tuning
            || tuning.Values.OfType<SyntaxTokenNode>().FirstOrDefault() is not { } word
            || !Tunings.Names.Contains(word.Text))
            return;
        var written = Tunings.Parse(word.Text);
        var strings = Tunings.Parse(presetWord);
        if (Tunings.Family(written) == Tunings.Family(strings))
            return;
        string Open(TuningType t) => string.Join(" ", Tunings.GetTuning(t).Select(Spell));
        _diagnostics.Warning(word.Span, DiagnosticCodes.TuningOfAnotherInstrument,
            $"tuning {word.Text} gives this part another instrument's strings ({Open(written)}), but instrument "
            + $"{preset} is strung in {presetWord} ({Open(strings)}). The tab and the chord diagrams fret on "
            + $"tuning {word.Text}: write a tuning of the {preset}'s, or the instrument these strings are.");
    }

    private static string Spell(int midi)
        => new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[midi % 12] + (midi / 12 - 1);
}
