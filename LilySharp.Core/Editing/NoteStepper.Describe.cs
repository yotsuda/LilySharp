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

using System;
using System.Globalization;
using System.Linq;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// The caret's status line (owner's request 2026-10-24): what the setting under the caret does, in
/// one line the editor shows in its status bar while the caret rests there — where a hover would
/// cover the text being edited. The things are the step keys' (<see cref="TargetAt"/>) plus a
/// part's <c>transposition</c>, and the line says what the step's message says of the value it
/// steps to, of the value written.
/// </summary>
public static partial class NoteStepper
{
    /// <summary>The 16 General MIDI families, eight programs each.</summary>
    private static readonly string[] MidiFamilies =
    [
        "piano", "chromatic percussion", "organ", "guitar", "bass", "strings", "ensemble", "brass",
        "reed", "pipe", "synth lead", "synth pad", "synth effects", "ethnic", "percussive", "sound effects",
    ];

    /// <summary>
    /// The status line for the caret at <paramref name="offset"/>, or null when it is on nothing
    /// that has one (a note, a rest, a word whose line would say only its name).
    /// </summary>
    public static string? Describe(string text, SyntaxTree tree, int offset)
    {
        foreach (var p in tree.GetNodes<PropertyAssignmentSyntax>())
            if (p.NameToken.Text == "transposition" && p.SourceStart <= offset && offset <= p.Span.End)
                return TranspositionSays(p.ValueText);
        if (TargetAt(text, tree, offset) is not { } target)
            return null;
        return target.Kind switch
        {
            TargetKind.Clef => ClefSays(target.Node),
            TargetKind.Key => KeySays((KeySignatureSyntax)target.Node),
            TargetKind.HeaderValue => OctaveSays((PropertyAssignmentSyntax)target.Node),
            TargetKind.OctaveMode => OctaveModeSays(((OctaveDirectiveSyntax)target.Node).IsAbsolute),
            TargetKind.Instrument => MidiInstrumentSays((PropertyAssignmentSyntax)target.Node),
            TargetKind.Tuning => TuningSays((PropertyAssignmentSyntax)target.Node),
            TargetKind.InstrumentPreset => PresetSays((PropertyAssignmentSyntax)target.Node),
            TargetKind.StaffGroup => StaffGroupSays(target.Node),
            TargetKind.Barline => BarSays((SyntaxTokenNode)target.Node),
            TargetKind.PaperSize => PaperSizeAt(tree, offset) is { } size ? PaperSays(size.Name) : null,
            _ => null,
        };
    }

    /// <summary>A clef: its middle line, and that it moves no note — the register is the part's
    /// <c>octave</c> or its <c>instrument</c> (owner's rule 2026-09-15, InstrumentDefaults).</summary>
    private static string? ClefSays(SyntaxNode clef)
    {
        string? name = clef switch
        {
            SyntaxTokenNode word => word.Text,
            ClefDeclarationSyntax d => d.ClefName.Text,
            PropertyAssignmentSyntax p when p.Values.ToList() is [SyntaxTokenNode only] => only.Text,
            _ => null,
        };
        if (name == null || !ClefMiddleLines.Any(c => c.Name == name))
            return null;
        if (name == "percussion")
            return "clef percussion: unpitched - the lines are the kit's places, not pitches";
        string octave = name.EndsWith("_8", StringComparison.Ordinal) ? " · it sounds an octave below the page"
            : name.EndsWith("^8", StringComparison.Ordinal) ? " · it sounds an octave above the page" : "";
        return $"clef {name}: middle line {SpellMidi(MiddleLineOf(name))}{octave} · "
            + "a clef moves no note - octave or instrument sets the register";
    }

    /// <summary>A key: its sharps or flats, and its relative key.</summary>
    private static string? KeySays(KeySignatureSyntax key)
    {
        if (key.IsCustom || key.GetChild(2) is not PitchSyntax)
            return null;
        string tonic = key.Pitch.ToFullString().Trim();
        string mode = key.GetChild(3) is SyntaxTokenNode m && m.Text.Length > 0 ? m.Text : "major";
        if (Music.KeySpelling.SharpsFor(tonic.ToLowerInvariant(), mode) is not int sharps)
            return null;
        var steps = Music.KeySpelling.SignatureSteps(sharps);
        string signature = steps.Count == 0 ? "no sharps or flats"
            : $"{steps.Count} {(sharps > 0 ? "sharp" : "flat")}{(steps.Count == 1 ? "" : "s")} "
              + $"({string.Join(" ", steps.Select(s => "CDEFGAB"[s.Step] + (s.Alter > 0 ? "♯" : "♭")))})";
        string[] majors = ["C♭", "G♭", "D♭", "A♭", "E♭", "B♭", "F", "C", "G", "D", "A", "E", "B", "F♯", "C♯"];
        string[] minors = ["a♭", "e♭", "b♭", "f", "c", "g", "d", "a", "e", "b", "f♯", "c♯", "g♯", "d♯", "a♯"];
        string relative = mode switch
        {
            "major" or "ionian" => $"the relative minor is {minors[sharps + 7]}",
            "minor" or "aeolian" => $"the relative major is {majors[sharps + 7]}",
            _ => $"the signature of {majors[sharps + 7]} major",
        };
        return $"key {tonic} {mode}: {signature} · {relative}";
    }

    /// <summary>A part header's <c>octave</c>: where a bare letter is written — the page's register,
    /// which <c>transposition</c> leaves alone (it moves the sound). Null for <c>transpose</c> /
    /// <c>shift</c>.</summary>
    private static string? OctaveSays(PropertyAssignmentSyntax property)
    {
        if (property.NameToken.Text != "octave")
            return null;
        string? value = property.ValueText;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            return $"octave {n}: a bare c is written C{n} - the page's register (transposition moves only the sound)";
        return value switch
        {
            "absolute" => OctaveModeSays(true),
            "relative" => OctaveModeSays(false),
            _ => null,
        };
    }

    private static string OctaveModeSays(bool absolute) => absolute
        ? "octave absolute: a bare letter is in the part's octave; each ' raises it and each , lowers it"
        : "octave relative: each note goes to the nearest of the note before; ' and , move it an octave from there";

    /// <summary>A part's <c>transposition</c>: what sounds against what is written — the page does
    /// not change.</summary>
    private static string? TranspositionSays(string? marker)
    {
        if (marker == null || !Svg.Model.InstrumentDefaults.TranspositionMarkers.Contains(marker)
            || Svg.Model.InstrumentDefaults.ParseTranspositionSemitones(marker) is not int shift)
            return null;
        string how = Math.Abs(shift) == 12 ? "an octave" : "two octaves";
        return $"transposition {marker}: sounds {how} {(shift < 0 ? "below" : "above")} the page "
            + $"(written C4 sounds {SpellMidi(60 + shift)}) - the written notes do not move";
    }

    /// <summary>A <c>midiInstrument</c>: its General MIDI number (1-based, as the GM list counts),
    /// its family, and the presets whose sound it is.</summary>
    private static string? MidiInstrumentSays(PropertyAssignmentSyntax property)
    {
        if (InstrumentNameToken(property) is not { } token
            || Midi.GeneralMidi.ProgramOf(StringLiteral.Value(token.Text)) is not int program)
            return null;
        var presets = Svg.Model.InstrumentDefaults.KnownInstruments
            .Where(p => Svg.Model.InstrumentDefaults.GetMidiProgram(p) == program).ToList();
        string sound = presets.Count == 0 ? ""
            : $" · the sound of instrument {string.Join(", ", presets)}";
        return $"midiInstrument {token.Text}: GM {program + 1}, the {MidiFamilies[program / 8]} family{sound}";
    }

    /// <summary>A <c>tuning</c>: each string's open note by its number, and where a higher string
    /// is out of order (a re-entrant ukulele or banjo).</summary>
    private static string? TuningSays(PropertyAssignmentSyntax property)
    {
        if (TuningWordToken(property) is not { } token || !Tablature.Tunings.Names.Contains(token.Text))
            return null;
        var strings = Tablature.Tunings.GetTuning(Tablature.Tunings.Parse(token.Text)).ToList();
        int count = strings.Count;
        string line = $"tuning {token.Text}: "
            + string.Join(" · ", strings.Select((pitch, i) => $"{count - i} {SpellMidi(pitch)}"));
        // The table runs from the highest-numbered string: one above the next is out of order.
        for (int i = 0; i + 1 < count; i++)
            if (strings[i] > strings[i + 1])
                return line + $" - re-entrant: string {count - i} sounds above string {count - i - 1}";
        return line;
    }

    /// <summary>An <c>instrument</c> preset: the defaults it sets, in one line (its hover lists them
    /// with the part's own properties struck through).</summary>
    private static string? PresetSays(PropertyAssignmentSyntax property)
    {
        if (PresetOf(property) is not { } written || !Svg.Model.InstrumentDefaults.IsKnownInstrument(written.Name))
            return null;
        string preset = written.Name.ToLowerInvariant();
        var (clef, octave) = Svg.Model.InstrumentDefaults.GetDefaults(preset);
        int shift = Svg.Model.InstrumentDefaults.GetTransposition(preset);
        string? marker = Svg.Model.InstrumentDefaults.TranspositionMarkers
            .FirstOrDefault(m => Svg.Model.InstrumentDefaults.ParseTranspositionSemitones(m) == shift);
        int bare = 12 * (octave + 1);
        var parts = new System.Collections.Generic.List<string>
        {
            $"clef {Svg.Model.InstrumentDefaults.ClefWord(clef)}",
            $"octave {octave}",
        };
        if (shift != 0)
            parts.Add($"{(marker != null ? $"transposition {marker}" : $"sounds {Math.Abs(shift)} semitones {(shift < 0 ? "lower" : "higher")}")} "
                + $"(written C{octave} sounds {SpellFlat(bare + shift)})");
        if (Svg.Model.InstrumentDefaults.GetTuning(preset) is { } tuning)
            parts.Add($"tuning {tuning}");
        if (Svg.Model.InstrumentDefaults.GetMidiProgram(preset) is int program)
            parts.Add($"midiInstrument \"{Midi.GeneralMidi.InstrumentNames[program]}\"");
        return $"instrument {preset}: {string.Join(" · ", parts)}";
    }

    /// <summary>A MIDI pitch spelled with flats, as a transposing instrument's key is (B♭3).</summary>
    private static string SpellFlat(int midi)
        => new[] { "C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B" }[midi % 12] + (midi / 12 - 1);

    /// <summary>A staff group, condensed or combined staff: how it engraves its staves (GRAMMAR.md:
    /// ScoreItem, CondensedStaff, CombinedStaff).</summary>
    private static string? StaffGroupSays(SyntaxNode group)
        => (group.GetChild(0) as SyntaxTokenNode)?.Text switch
        {
            "grandStaff" => "grandStaff: a brace - one instrument on two or more staves (a piano's hands)",
            "staffGroup" => "staffGroup: a bracket, bar lines drawn through the staves (a section of the orchestra)",
            "choirStaff" => "choirStaff: a bracket, bar lines on each staff alone (voices - the lyrics go between)",
            "condensedStaff" => "condensedStaff: two or more parts on one staff, one voice each (the first stems up)",
            "combinedStaff" => $"combinedStaff: exactly {CombinedStaffParts} parts on one staff, merged where they agree (a2, Solo)",
            _ => null,
        };

    /// <summary>A bar line: what it is (<see cref="BarlineSays"/>); a <c>:|*N</c> its count.</summary>
    private static string BarSays(SyntaxTokenNode bar)
    {
        if (bar.Text == ":|" && NextToken(bar) is { Text: "*" } star
            && NextToken(star) is { Kind: SyntaxKind.IntegerLiteral } count)
            return $":|*{count.Text} closes the repeat - it plays {count.Text} times";
        return BarlineSays(bar.Text, InForm(bar));
    }

    /// <summary>A page name: its size, in millimetres and inches.</summary>
    private static string? PaperSays(string name)
    {
        if (!Svg.Layout.PaperSizes.TryGet(name, out double w, out double h))
            return null;
        static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        return $"size {name}: {N(w)} × {N(h)} mm ({N(w / 25.4)} × {N(h / 25.4)} in)";
    }
}
