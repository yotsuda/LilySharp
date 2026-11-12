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
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// LYS1052: a note the part's <c>instrument</c> cannot sound (owner's request 2026-10-25) — its
/// sounding pitch outside <see cref="InstrumentDefaults.SoundingRange"/>. Read off the notes the
/// MIDI export plays, so it is the SOUNDING pitch, every transposition applied (a B♭ clarinet's
/// written E3 is its D3), and a phrase or a repeat is the note it expands from.
/// </summary>
/// <remarks>
/// Nothing is played for a book with no ranged preset, so a book that names no instrument pays
/// nothing. ASCII only: the message reaches legacy-codepage consoles through the CLI.
/// </remarks>
internal sealed class InstrumentRangeValidator : ISemanticValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree)
    {
        var presets = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var ranges = new Dictionary<string, (int Low, int High)>(System.StringComparer.Ordinal);
        foreach (var part in tree.GetRoot().ChildNodes().OfType<PartDeclarationSyntax>())
        {
            var header = PartHeaderDefaults.Read(part);
            if (header.Preset is { } preset && InstrumentDefaults.SoundingRange(preset) is { } range
                && presets.TryAdd(part.Name.Text, preset))
                ranges[part.Name.Text] = Retuned(range, preset, header.Tuning);
        }
        if (presets.Count == 0)
            return;

        MidiFile midi;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            return; // a book that cannot play surfaces its real error elsewhere
        }

        var seen = new HashSet<int>();
        foreach (var n in midi.Tracks.SelectMany(t => t.Notes))
        {
            if (n.SourcePos < 0 || n.Part is not { } part || !presets.TryGetValue(part, out var preset))
                continue;
            var (low, high) = ranges[part];
            if (n.Pitch >= low && n.Pitch <= high || !seen.Add(n.SourcePos))
                continue;
            string printed = InstrumentDefaults.PrintedName(preset) ?? preset;
            _diagnostics.Warning(new TextSpan(n.SourcePos, 1), DiagnosticCodes.NoteOutsideInstrumentRange,
                $"this note sounds {Spell(n.Pitch)}, {(n.Pitch < low ? "below" : "above")} the range of the "
                + $"{printed} ({Spell(low)}-{Spell(high)}, as it sounds) - check its octave, or the part's instrument");
        }
    }

    /// <summary>
    /// A string instrument's range on the strings the part TUNES it to (found 2026-11-13,
    /// grammar evaluation 3: <c>instrument guitar  tuning guitarDropD</c> warned that its open
    /// sixth string, D2, was "below the range of the Guitar" while the tab fretted it 0). The
    /// preset's range is its usual tuning's — the lowest open string to the top of the highest —
    /// so the low end is the lowest string of the part's tuning, and the top moves with the
    /// highest string. A preset with no tuning, or a part on its usual one, keeps its range.
    /// LILYSHARP-OWN.
    /// </summary>
    private static (int Low, int High) Retuned((int Low, int High) range, string preset, TuningType tuning)
    {
        if (InstrumentDefaults.GetTuning(preset) is not { } usualWord)
            return range;
        var usual = Tablature.Tunings.Parse(usualWord);
        if (usual == tuning)
            return range;
        int[] open = Tablature.Tunings.GetTuning(tuning), usualOpen = Tablature.Tunings.GetTuning(usual);
        return (open.Min(), range.High + open.Max() - usualOpen.Max());
    }

    private static string Spell(int midi)
        => new[] { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" }[midi % 12] + (midi / 12 - 1);
}
