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
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// One note of a chord from a shape: the string it is played on, spelled.
/// </summary>
/// <param name="Step">The letter (0 = C … 6 = B).</param>
/// <param name="Alter">The accidental in semitones.</param>
/// <param name="Octave">The octave of the pitch AS THE PART WRITES IT (scientific, C4 = middle
/// C) — the sounding pitch less the part's sounding shift (a guitar's treble_8 notation: an
/// octave up). This is the pitch every reader hands to its ordinary written-pitch path.</param>
/// <param name="StringNumber">The string, 1 = the highest-numbered in the tuning's list (the
/// top string of a guitar) — what <c>c\3</c> would write.</param>
/// <param name="SoundingMidi">The pitch that sounds: the open string plus the fret.</param>
public readonly record struct ShapeNote(int Step, int Alter, int Octave, int StringNumber, int SoundingMidi)
{
    /// <summary>The octave of the SOUNDING pitch, in the same spelling (what the hover lists).</summary>
    public int SoundingOctave => OctaveOf(SoundingMidi, Step, Alter);

    /// <summary>The sounding pitch as a name — <c>C3</c>, <c>Eb4</c>.</summary>
    public string SoundingName
        => "CDEFGAB"[Step] + (Alter switch { >= 2 => "x", 1 => "#", -1 => "b", <= -2 => "bb", _ => "" })
           + SoundingOctave.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The octave (C4 = middle C) of MIDI pitch <paramref name="midi"/> spelled
    /// <paramref name="step"/>/<paramref name="alter"/> — B♯3 is MIDI 60.</summary>
    internal static int OctaveOf(int midi, int step, int alter)
        => (midi - RelativeOctave.StepSemitoneOf(step) - alter) / 12 - 1;
}

/// <summary>
/// The notes of a chord from a shape — <c>chord(SYMBOL SHAPE)</c> — on the tuning of the part
/// that plays it: one shared resolver, which the page, the MIDI, the MusicXML, the LilyPond
/// twin, the validator and the editor all ask.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decisions 2026-09-28 (HANDOFF §2 K5):
/// <list type="bullet">
/// <item>The words are an <c>@chord(…)</c> argument's (<see cref="ChordAnnotation.Parse"/>): a
/// symbol and a shape, or a shape alone, both shape forms (<see cref="ChordShapes.TryRead"/>).
/// The shape is REQUIRED for now — with none usable the item is a spacer (LYS1040).</item>
/// <item>PITCHES: each non-muted string's open pitch plus its fret, on the tuning of the PART —
/// its fretted instrument (the tuning its tab frets on, <see cref="PartHeaderDefaults.FrettedTuning"/>),
/// else the standard guitar. ABSOLUTE: no octave mark, relative frame or <c>octave absolute</c>
/// moves them.</item>
/// <item>The shape gives SOUNDING pitches; the staff writes them as the part writes any sounding
/// pitch — less <see cref="PartHeaderDefaults.SoundingShiftSemitones"/> (the treble_8 clef's
/// octave, a <c>transposition</c>), which every reader adds back for playback and the tab. The
/// written pitch then takes the path a written note takes (the part's <c>transpose</c> included,
/// like every other note of the part — Lily#'s choice: the item is part of the music).</item>
/// <item>SPELLING: a pitch class that is a tone of the symbol (or its slash bass) is spelled as
/// the chord spells it (<see cref="ChordStructure.Tones"/>: Cm7's third is E♭, never D♯); any
/// other in the written key (<see cref="ChordAnnotation.SpellPitchClass"/>).</item>
/// <item>STRING NUMBERS: every note carries its string, so a tab of the part shows the shape
/// exactly; a muted string gives no note.</item>
/// <item>THE FRAME: the item hands its LOWEST sounding note on as the relative frame (as a
/// <c>&lt;…&gt;</c> chord hands on its anchor), so the next note is read from it; absolute mode
/// changes nothing.</item>
/// </list>
/// </para>
/// LILYSHARP-OWN: LilyPond has no chord-from-a-shape item.
/// </remarks>
public static class ShapeChords
{
    /// <summary>The words of a shape chord, read as an <c>@chord(…)</c> argument's.</summary>
    public static ChordAnnotation Words(ChordSyntax chord)
        => ChordAnnotation.Parse(chord.ShapeArguments.Select(a => a.Text).ToList());

    /// <summary>The tuning a shape chord of a part with header <paramref name="header"/> sounds
    /// on: its fretted instrument's, else the guitar.</summary>
    public static TuningType TuningOf(PartHeaderDefaults? header)
        => header?.FrettedTuning ?? TuningType.Guitar;

    /// <summary>The written shape the item plays on <paramref name="tuning"/>, or null (none
    /// written, or none of that string count / bound to that tuning — LYS1040).</summary>
    public static string? ShapeFor(ChordAnnotation words, TuningType tuning)
        => ChordShapes.WrittenFor(tuning, words.Shapes);

    /// <summary>The notes of <paramref name="chord"/> — see the class remarks — in string order,
    /// LOW string first; empty when no written shape fits <paramref name="tuning"/>.</summary>
    /// <param name="chord">A chord with <see cref="ChordSyntax.IsShapeChord"/>.</param>
    /// <param name="tuning">The part's tuning (<see cref="TuningOf"/>).</param>
    /// <param name="soundingShift">The part's written→sounding shift in semitones.</param>
    /// <param name="keySharps">The written key at the item, for a note outside the symbol.</param>
    public static ImmutableArray<ShapeNote> Notes(ChordSyntax chord, TuningType tuning, int soundingShift, int keySharps)
        => Notes(Words(chord), tuning, soundingShift, keySharps);

    /// <summary><see cref="Notes(ChordSyntax, TuningType, int, int)"/> on words already read.</summary>
    public static ImmutableArray<ShapeNote> Notes(ChordAnnotation words, TuningType tuning, int soundingShift, int keySharps)
    {
        if (ShapeFor(words, tuning) is not { } shape)
            return [];
        var frets = ChordShapes.Frets(shape);
        var open = Tablature.Tunings.GetTuning(tuning);

        // The symbol's own letters for its tones and its slash bass (Cm7: E♭, B♭).
        var spelled = new Dictionary<int, (int Step, int Alter)>();
        if (words.Structure is { } chord)
        {
            foreach (var t in chord.Tones)
                spelled.TryAdd(Mod12(RelativeOctave.StepSemitoneOf(t.Step) + t.Alter), (t.Step, t.Alter));
            if (chord.BassStep is int bs)
                spelled.TryAdd(Mod12(RelativeOctave.StepSemitoneOf(Mod7(bs)) + (chord.BassAlter ?? 0)),
                    (Mod7(bs), chord.BassAlter ?? 0));
        }

        var notes = ImmutableArray.CreateBuilder<ShapeNote>();
        for (int i = 0; i < frets.Length && i < open.Length; i++)
        {
            if (frets[i] < 0)
                continue;
            int sounding = open[i] + frets[i];
            int written = sounding - soundingShift;
            var (step, alter) = spelled.TryGetValue(Mod12(written), out var s)
                ? s
                : ChordAnnotation.SpellPitchClass(Mod12(written), keySharps);
            notes.Add(new ShapeNote(step, alter, ShapeNote.OctaveOf(written, step, alter),
                open.Length - i, sounding));
        }
        return notes.ToImmutable();
    }

    /// <summary>The note the item hands on as the relative frame: its lowest sounding one
    /// (the first of the lowest, on a re-entrant tuning where two strings sound alike).</summary>
    public static ShapeNote? Lowest(ImmutableArray<ShapeNote> notes)
    {
        ShapeNote? lowest = null;
        foreach (var n in notes)
            if (lowest is not { } l || n.SoundingMidi < l.SoundingMidi)
                lowest = n;
        return lowest;
    }

    /// <summary>The notes lowest SOUNDING first — the order a chord lists its members in (the
    /// twin writes them so, so LilyPond's first member is the frame Lily# hands on).</summary>
    public static IEnumerable<ShapeNote> Ascending(ImmutableArray<ShapeNote> notes)
        => notes.OrderBy(n => n.SoundingMidi).ThenByDescending(n => n.StringNumber);

    /// <summary>A part a shape chord may be played by, as the tree says it: its tuning, the
    /// word naming it, and its written→sounding shift.</summary>
    public readonly record struct PartTuning(TuningType Tuning, string Word, int SoundingShift);

    /// <summary>
    /// The parts <paramref name="node"/> is played by, from the SYNTAX — the enclosing part
    /// declaration, or the part a section's block names; outside every part (a phrase), each
    /// distinct tuning of the file's parts (the guitar when it declares none). For the readers
    /// that have no part in hand: the validator, the hover, the editor's step. The page and the
    /// exporters know the part they are writing.
    /// </summary>
    public static IReadOnlyList<PartTuning> PartTuningsOf(SyntaxNode node)
    {
        var root = node;
        while (root.Parent != null)
            root = root.Parent;
        static PartTuning Of(PartHeaderDefaults h)
            => new(TuningOf(h), h.FrettedTuningWord ?? "guitar", h.SoundingShiftSemitones);
        if (ChordDiagramScores.PartNameOf(node) is { } part)
            return [Of(PartHeaderDefaults.Read(ConcertPitch.FindPart(root, part)))];
        var all = root.ChildNodes().OfType<PartDeclarationSyntax>()
            .Select(pd => Of(PartHeaderDefaults.Read(pd))).Distinct().ToList();
        return all.Count == 0 ? [Of(PartHeaderDefaults.Empty)] : all;
    }

    // ---------------------------------------------------------------- the messages

    /// <summary>LYS1040's sentence for an item with no usable shape (<paramref name="written"/>
    /// the item's words as written, <paramref name="symbol"/> its symbol or null,
    /// <paramref name="tuningWord"/> the part's tuning word, <paramref name="exampleShape"/> the
    /// symbol's default shape on it when there is one).</summary>
    internal static string NoShape(string written, string? symbol, string tuningWord, int strings,
        bool anyShape, string? exampleShape)
    {
        string shape = exampleShape ?? (strings == 6 ? "x32010" : new string('0', strings));
        string example = $"chord({(symbol == null ? "" : symbol + " ")}{shape})";
        string why = anyShape
            ? $"none of its shapes fits '{tuningWord}' ({strings} strings)"
            : "it has no shape";
        return $"chord({written}) draws and sounds nothing: {why} - write a shape: {example}. "
               + "The item keeps its time as a spacer, so the bar still adds up.";
    }

    private static int Mod12(int a) => ((a % 12) + 12) % 12;
    private static int Mod7(int a) => ((a % 7) + 7) % 7;
}
