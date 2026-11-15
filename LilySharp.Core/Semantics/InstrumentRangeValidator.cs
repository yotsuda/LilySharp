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
/// sounding pitch outside <see cref="PartHeaderDefaults.Range"/>: the part's own <c>range</c>,
/// else its preset's <see cref="InstrumentDefaults.SoundingRange"/>. Read off the notes the
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
        // Each ranged part: its range, whose range it is in the message's words, and what the
        // message advises. A part's own `range` (2026-11-15) is the writer's number: it is not
        // moved to the tuning, and it is the part's, whatever its preset.
        var ranges = new Dictionary<string, (int Low, int High, string Whose, string Advice)>(System.StringComparer.Ordinal);
        foreach (var part in tree.GetRoot().ChildNodes().OfType<PartDeclarationSyntax>())
        {
            var header = PartHeaderDefaults.Read(part);
            if (header.WrittenRange is { } written)
                ranges.TryAdd(part.Name.Text, (written.Low, written.High, "the part's range",
                    "check its octave, or the part's range"));
            else if (header.Preset is { } preset && InstrumentDefaults.SoundingRange(preset) is { } range)
            {
                var (low, high) = Retuned(range, preset, header.Tuning);
                // A part that states its own transposition is likely another member of the
                // preset's family (a bass clarinet on `instrument clarinet`): the preset's range
                // stays where it is, and the message names the property that states the part's.
                bool ownShift = header.TranspositionSemitones != InstrumentDefaults.GetTransposition(preset);
                ranges.TryAdd(part.Name.Text, (low, high,
                    $"the range of the {InstrumentDefaults.PrintedName(preset) ?? preset}",
                    ownShift
                        ? "check its octave, or state the part's own range beside its transposition, e.g. 'range d, bes'''"
                        : "check its octave, or the part's instrument"));
            }
        }
        if (ranges.Count == 0)
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

        // A harmonic sounds above what the instrument stops: `b4@harmonic(3)` on a guitar is
        // F#6, over the top of its fretted range, and is no slip of an octave (owner-approved
        // 2026-11-13, grammar evaluation 3). The notes that sound one — a natural harmonic's,
        // a chord with a touched member — are not judged at the top.
        HashSet<int>? harmonics = null;
        foreach (var node in tree.GetRoot().DescendantNodes())
            if (node is NoteSyntax note && Music.Harmonics.FretOn(note) > 0
                || node is ChordSyntax chord && chord.Pitches.Any(Music.Harmonics.IsTouchedMember))
                (harmonics ??= new()).Add(node.SourceStart);

        var seen = new HashSet<int>();
        foreach (var n in midi.Tracks.SelectMany(t => t.Notes))
        {
            if (n.SourcePos < 0 || n.Part is not { } part || !ranges.TryGetValue(part, out var range))
                continue;
            var (low, high, whose, advice) = range;
            if (n.Pitch >= low && n.Pitch <= high || !seen.Add(n.SourcePos))
                continue;
            if (n.Pitch > high && harmonics?.Contains(n.SourcePos) == true)
                continue;
            _diagnostics.Warning(new TextSpan(n.SourcePos, 1), DiagnosticCodes.NoteOutsideInstrumentRange,
                $"this note sounds {Spell(n.Pitch)}, {(n.Pitch < low ? "below" : "above")} {whose} "
                + $"({Spell(low)}-{Spell(high)}, as it sounds) - {advice}");
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
