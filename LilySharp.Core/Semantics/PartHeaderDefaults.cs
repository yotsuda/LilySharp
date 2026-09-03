// Lily# — a music notation language and engraver.
// Copyright (C) 2026 yotsuda
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
using System.Collections.Generic;
using System.Text;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Semantics;

/// <summary>
/// What a <c>part { … }</c> header says about pitch: the clef it reads in, the octave its
/// bare letters anchor to, and how far what it sounds is from what it prints.
/// </summary>
/// <remarks>
/// ⚠️ ONE HOME, because every consumer needs the SAME answers and they had drifted apart:
/// the page (MeasureCollector), the LilyPond twin, the MIDI exporter and the MusicXML
/// exporter each resolved some of this and skipped the rest. What that cost, measured:
/// <list type="bullet">
/// <item>MIDI stopped at <c>octave N</c> &gt; preset and never asked the CLEF, so
///   <c>part m { clef bass }</c> printed C3 and played C4.</item>
/// <item>MusicXML asked for none of it: every part exported at C4 with a treble clef,
///   whatever its header said, and no <c>transpose</c> at all.</item>
/// </list>
/// The rules themselves still live where they belong — <see cref="InstrumentDefaults"/> for
/// the presets and the two anchors, <see cref="Tunings"/> for the tuning defaults. This type
/// is the part-header READING that feeds them, done once.
/// </remarks>
public sealed class PartHeaderDefaults
{
    /// <summary>The part's explicit <c>octave N</c>, or null.</summary>
    public int? ExplicitOctave { get; private init; }

    /// <summary>The part's <c>instrument</c> preset (lower-cased), or null.</summary>
    public string? Preset { get; private init; }

    /// <summary>The clef word the part reads in: its own, else its preset's, else null.</summary>
    public string? ClefWord { get; private init; }

    /// <summary>The clef the part reads in — treble when it names none.</summary>
    public ClefType Clef { get; private init; } = ClefType.Treble;

    /// <summary>
    /// The RELATIVE-frame anchor: <c>octave N</c> &gt; preset &gt; the clef's own octave.
    /// </summary>
    public int AnchorOctave { get; private init; } = 4;

    /// <summary>
    /// The ABSOLUTE-mode base, which is a different rule: only <c>octave N</c> moves it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Do not fold this into <see cref="AnchorOctave"/>. OctaveContext: "the clef default
    /// is deliberately NOT used here". One field served both in the MIDI exporter, and
    /// giving the relative anchor its clef step promptly dragged <c>octave absolute</c>
    /// parts down with it.
    /// </remarks>
    public int AbsoluteBaseOctave { get; private init; } = 4;

    /// <summary>
    /// The octave the CLEF itself carries, in semitones (<c>treble_8</c> → −12).
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="TranspositionSemitones"/> because the two are separate
    /// things to a reader of the notation: MusicXML spells this one
    /// <c>clef-octave-change</c> and that one <c>transpose</c>. Emitting a single total in
    /// both places would have importers apply the octave twice.
    /// </remarks>
    public int ClefOctaveSemitones { get; private init; }

    /// <summary>
    /// The INSTRUMENT's written→sounding shift: explicit <c>transposition</c> &gt; preset
    /// &gt; the tuning's default.
    /// </summary>
    public int TranspositionSemitones { get; private init; }

    /// <summary>Everything between what the part prints and what it sounds.</summary>
    public int SoundingShiftSemitones => ClefOctaveSemitones + TranspositionSemitones;

    /// <summary>
    /// The part of <see cref="TranspositionSemitones"/> a concert-pitch score REWRITES: the
    /// whole of it for a chromatic transposer (a B♭ clarinet's −2, an E♭ alto saxophone's
    /// −9, a tenor saxophone's −14), and 0 for an octave-only one (a bass's −12, a
    /// piccolo's +12, a bass tuning's −12).
    /// </summary>
    /// <remarks>
    /// The convention of the printed page: a score at concert pitch shows the clarinets and
    /// saxophones at their sounding pitch, but keeps the piccolo, the contrabass and the
    /// guitar in their octave-transposed notation — the octave is a reading convenience of
    /// the instrument, not a key the conductor has to un-transpose. So the octave
    /// transposers print the same under <c>pitch concert</c> as under <c>pitch written</c>,
    /// and only the chromatic ones move. Decided with the first implementation
    /// (2026-09-03, HANDOFF §1); <see cref="ConcertPitch"/> is the reader.
    /// </remarks>
    public int ConcertShiftSemitones => TranspositionSemitones % 12 == 0 ? 0 : TranspositionSemitones;

    /// <summary>The defaults of a part that declares nothing.</summary>
    public static readonly PartHeaderDefaults Empty = new();

    /// <summary>Reads a part declaration's pitch-bearing properties.</summary>
    public static PartHeaderDefaults Read(PartDeclarationSyntax? part)
    {
        if (part == null)
            return Empty;

        int? explicitOctave = null;
        string? preset = null, clefText = null, transText = null, tuningText = null;

        foreach (var prop in part.Properties)
        {
            string name = prop.NameToken.Text.ToLowerInvariant();
            string Joined()
            {
                var sb = new StringBuilder();
                for (int vi = 2; vi < prop.SlotCount; vi++)
                    if (prop.GetChild(vi) is SyntaxTokenNode vt)
                        sb.Append(vt.Text);
                return sb.ToString();
            }

            switch (name)
            {
                case "octave":
                    if (prop.GetChild(2) is SyntaxTokenNode ov && int.TryParse(ov.Text, out int oct))
                        explicitOctave = oct;
                    break;
                case "clef":
                    clefText = (prop.GetChild(2) as SyntaxTokenNode)?.Text.ToLowerInvariant();
                    break;
                case "transposition":
                    transText = (prop.GetChild(2) as SyntaxTokenNode)?.Text;
                    break;
                case "tuning":
                    tuningText = Joined().ToLowerInvariant();
                    break;
                case "instrument":
                    string joined = Joined();
                    var texts = new List<string>();
                    if (joined.Length > 0) texts.Add(joined);
                    string p = InstrumentDefaults.SplitInstrument(texts).Preset;
                    preset = p.Length == 0 ? null : p.ToLowerInvariant();
                    break;
            }
        }

        // The clef: the part's own word, else the one its preset implies.
        string? clefWord = clefText
            ?? (preset != null ? InstrumentDefaults.ClefWord(InstrumentDefaults.GetDefaults(preset).Clef) : null);
        ClefType clef = clefWord != null ? ParseClefWord(clefWord) : ClefType.Treble;

        // The transposition: explicit > preset > the tuning's own.
        int transposition;
        if (transText != null && InstrumentDefaults.ParseTranspositionSemitones(transText) is int ex)
            transposition = ex;
        else if (preset != null)
            transposition = InstrumentDefaults.GetTransposition(preset);
        else if (tuningText != null)
            transposition = Tunings.TuningTransposition(ParseTuningWord(tuningText));
        else
            transposition = 0;

        return new PartHeaderDefaults
        {
            ExplicitOctave = explicitOctave,
            Preset = preset,
            ClefWord = clefWord,
            Clef = clef,
            AnchorOctave = InstrumentDefaults.AnchorOctave(explicitOctave, preset, clef),
            AbsoluteBaseOctave = InstrumentDefaults.AbsoluteBaseOctave(explicitOctave),
            ClefOctaveSemitones = Tunings.ClefOctaveShift(clef),
            TranspositionSemitones = transposition,
        };
    }

    /// <summary>A <c>.lys</c> clef word → the clef it names.</summary>
    public static ClefType ParseClefWord(string clef) => clef switch
    {
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        "treble^8" => ClefType.Treble8Above,
        "bass_8" => ClefType.Bass8Below,
        _ => ClefType.Treble,
    };

    /// <summary>A <c>.lys</c> tuning word → the tuning it names.</summary>
    private static TuningType ParseTuningWord(string tuning) => tuning switch
    {
        "bass" => TuningType.Bass,
        "bass5" => TuningType.Bass5,
        "bass6" => TuningType.Bass6,
        "ukulele" or "uke" => TuningType.Ukulele,
        _ => TuningType.Guitar,
    };
}
