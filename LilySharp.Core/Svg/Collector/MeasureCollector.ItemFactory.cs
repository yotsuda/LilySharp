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

using System.Collections.Immutable;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Svg.Collector;

// Item construction for MeasureCollector: turning note/chord/rest/drum syntax
// (plus grace notes, tremolo shaping, and pitch/staff-position resolution) into
// the model items the layout engine consumes. Split out of MeasureCollector.cs
// as a partial class; same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    private NoteItem CreateNoteItem(NoteSyntax note, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasGlissando = false, int featherDirection = 0, bool isCue = false)
    {
        var rp = CalculateStaffPosition(note.Pitch);
        _octave.CurrentOctave = rp.RelativeOctave;
        int staffPosition = rp.StaffPosition;

        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (note.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = note.Duration?.DotCount ?? 0;
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

        // Parse tremolo suffix (:8 = 1 beam, :16 = 2 beams, :32 = 3 beams)
        int tremoloBeams = ParseTremoloBeams(note.Tremolo);

        // Inside `repeat tremolo N { … }`: print ONE note at the combined
        // duration, slashes from the body's subdivision.
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        // Two-note tremolo: this note prints at the pair's total duration.
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        var (accidental, isCourtesy) = GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

        // Quarter tones always print their own accidental (they are never in
        // the key). LILYPOND-REF: quarter-tone note names ih/eh/isih/eseh.
        if (note.Pitch.QuarterOffset != 0)
        {
            accidental = QuarterToneAccidental(note.Pitch, accidental);
            isCourtesy = false;
        }

        // Check for explicit @courtesy annotation
        if (!isCourtesy && _courtesySourcePositions.Contains(note.Position))
        {
            isCourtesy = true;
            // If no accidental shown, force the key-signature-matching accidental
            if (accidental == null)
            {
                int step = rp.DisplayStep;
                int alt = GetKeySignatureAlteration(step);
                accidental = alt switch
                {
                    >= 2 => "doubleSharp", 1 => "sharp", <= -2 => "doubleFlat", -1 => "flat", _ => "natural"
                };
            }
        }

        // LILYPOND-REF: lily/fingering-engraver.cc — finger event lookup at note creation.
        // CollectArticulations runs after CreateNoteItem, so we scan the note's
        // articulations directly here (mirroring HasCourtesyAnnotation's pattern).
        int? fingering = ExtractFingering(note);
        bool hasLv = HasLaissezVibrerAnnotation(note);
        bool hasRepeatTie = HasRepeatTieAnnotation(note);

        // @editorial: the accidental this note resolves to becomes a SUGGESTION
        // above the note instead of a regular accidental at its left; when the
        // note has no printed accidental, force the key-signature alteration
        // (same rule as @courtesy).
        // LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion;
        // suggestAccidentals replaces Accidental with AccidentalSuggestion.
        string? editorialAccidental = null;
        if (HasNamedArticulation(note, "editorial"))
        {
            if (accidental != null)
            {
                editorialAccidental = accidental;
            }
            else
            {
                int step = rp.DisplayStep;
                int alt = GetKeySignatureAlteration(step);
                editorialAccidental = alt switch
                {
                    >= 2 => "doubleSharp", 1 => "sharp", <= -2 => "doubleFlat", -1 => "flat", _ => "natural"
                };
            }
            accidental = null; // suggestion replaces the left-of-note accidental
        }

        return new NoteItem(
            staffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental,
            needsLedger,
            note.Position,
            tremoloBeams,
            hasTieStart: hasTieAfter,
            hasSlurStart: hasSlurStartAfter,
            hasSlurEnd: hasSlurEndAfter,
            hasBeamStart: hasBeamStartAfter,
            hasBeamEnd: hasBeamEndAfter,
            hasGlissando: hasGlissando,
            featherDirection: featherDirection,
            isCourtesy: isCourtesy,
            isCue: isCue,
            editorialAccidental: editorialAccidental,
            fingering: fingering,
            hasLaissezVibrer: hasLv,
            hasRepeatTie: hasRepeatTie)
        {
            StringNumber = ExtractStringNumber(note),
            Midi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.RelativeOctave),
            IsDead = HasNamedArticulation(note, "dead"),
            StemUpOverride = GetStemDirectionOverride(note),
        };
    }

    /// <summary>Drum note → NoteItem: placement/notehead/GM key from the
    /// DrumNameRegistry (LP drums-style table); duration semantics identical
    /// to pitched notes.</summary>
    /// <remarks>LILYPOND-REF: lily/drum-note-engraver.cc — Drum_notes_engraver
    /// reads drumStyleTable for position + style.</remarks>
    private NoteItem CreateDrumNoteItem(DrumNoteSyntax drum)
    {
        var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
        int noteValue = drum.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (drum.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);
        int dots = drum.Duration?.DotCount ?? 0;
        int tremoloBeams = ParseTremoloBeams(drum.Tremolo);
        bool needsLedger = info.StaffPosition <= -6 || info.StaffPosition >= 6;

        return new NoteItem(
            info.StaffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental: null,
            needsLedger,
            drum.Position,
            tremoloBeams)
        {
            Notehead = info.Notehead,
            Midi = info.GmKey,
            StemUpOverride = GetStemDirectionOverride(drum),
        };
    }

    private RestItem CreateRestItem(RestSyntax rest)
    {
        int noteValue = rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = rest.Duration?.DotCount ?? 0;

        // 's' is a spacer rest: it occupies time/width but is never drawn (unlike 'r').
        return new RestItem(Fraction.FromNoteValue(noteValue), dots, rest.Position)
        {
            IsSpacer = rest.RestToken.Text == "s",
            // Capital R = explicit multi-measure rest (centred). Lowercase r = plain
            // rest at beat 1, even when it fills the measure.
            IsMultiMeasure = rest.RestToken.Text == "R"
        };
    }

    /// <summary>
    /// Parses tremolo suffix into beam count.
    /// :8 = 1 beam, :16 = 2 beams, :32 = 3 beams
    /// </summary>
    private static int ParseTremoloBeams(SyntaxTokenNode? tremolo)
    {
        if (tremolo == null)
            return 0;

        // Tremolo text is ":8", ":16", or ":32"
        var text = tremolo.Text;
        if (text.Length < 2 || text[0] != ':')
            return 0;

        return text[1..] switch
        {
            "8" => 1,
            "16" => 2,
            "32" => 3,
            _ => 0
        };
    }

    private ChordItem CreateChordItem(ChordSyntax chord, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false, bool isCue = false, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false)
    {
        var notes = new List<ChordNoteInfo>();

        // Track first note's state for subsequent chord/note relative calculation
        int firstOctave = _octave.CurrentOctave;
        char firstPitchName = _octave.LastPitchName;

        foreach (var pitch in chord.Pitches)
        {
            var rp = CalculateStaffPosition(pitch);
            _octave.CurrentOctave = rp.RelativeOctave;
            int staffPosition = rp.StaffPosition;

            // Remember first pitch's state (original octave drives the relative chain)
            if (notes.Count == 0)
            {
                firstOctave = rp.RelativeOctave;
                firstPitchName = pitch.PitchName.ToLowerInvariant()[0];
            }

            var (accidental, isCourtesy) = GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

            // Quarter tones always print their own accidental (never in the key).
            if (pitch.QuarterOffset != 0)
            {
                accidental = QuarterToneAccidental(pitch, accidental);
                isCourtesy = false;
            }

            bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

            // LILYPOND-REF: lily/fingering-engraver.cc — per-pitch finger via <c@finger.N>.
            int? pitchFingering = ExtractPitchFingering(pitch);

            notes.Add(new ChordNoteInfo(
                staffPosition, accidental, needsLedger,
                IsCourtesy: isCourtesy,
                Fingering: pitchFingering,
                StringNumber: pitch.Articulations.OfType<StringNumberAnnotationSyntax>().FirstOrDefault()?.StringNumber,
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.RelativeOctave)));
        }

        // Drum chord members (<bd hh>): placement/head/GM key from the
        // registry, mixed freely with pitched members.
        foreach (var drum in chord.DrumNames)
        {
            var dinfo = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
            notes.Add(new ChordNoteInfo(
                dinfo.StaffPosition, null,
                dinfo.StaffPosition is <= -6 or >= 6,
                Notehead: dinfo.Notehead,
                Midi: dinfo.GmKey));
        }

        // Next chord/note is relative to first pitch of this chord (Lilypond spec)
        _octave.CurrentOctave = firstOctave;
        _octave.LastPitchName = firstPitchName;

        int noteValue = chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (chord.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = chord.Duration?.DotCount ?? 0;
        int tremoloBeams = ParseTremoloBeams(chord.Tremolo);

        // Inside `repeat tremolo N { … }` (see CreateNoteItem).
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.Position, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieStart: hasTieAfter, hasSlurStart: hasSlurStartAfter, hasSlurEnd: hasSlurEndAfter);
    }

    /// <summary>
    /// A pitch resolved for rendering. <see cref="RelativeOctave"/> is the
    /// ORIGINAL (written) octave that drives the relative-octave chain for the
    /// next note; the Display* fields are what is actually drawn — equal to the
    /// written pitch, or its transposition when the part has a transpose option.
    /// </summary>
    private readonly record struct ResolvedPitch(
        int StaffPosition, int RelativeOctave, int DisplayStep, int DisplayAlteration, int DisplayOctave);

    /// <summary>One entry in the resolved-pitch trace: the source position of the
    /// written pitch and its resolved absolute spelling (e.g. "C6").</summary>
    public readonly record struct PitchTraceEntry(int Position, string Pitch);

    /// <summary>
    /// Ottava spans (measure range + type) for one staff, derived from the
    /// collected @ottava/@loco marks. Reuses the SAME detector the bracket uses
    /// at layout time, so the display transposition and the drawn bracket stay in
    /// lockstep. The detector is a pure function of the marks (no layout geometry).
    /// </summary>
    private List<OttavaBracketItem> DetectOttavaSpans(int staffIndex)
    {
        var result = new List<OttavaBracketItem>();
        foreach (var b in Layout.OttavaBracketEngraver.DetectOttavaBrackets(_musicMarks.ToImmutableArray()))
            if (b.StaffIndex == staffIndex)
                result.Add(b);
        return result;
    }

    private ResolvedPitch CalculateStaffPosition(PitchSyntax pitch)
    {
        char pitchName = pitch.PitchName.ToLowerInvariant()[0];
        int step = GetPitchIndex(pitchName);

        // Absolute mode: '/, are offsets from a fixed C4 anchor (bare c = C4),
        // stateless — every note is independent. Relative mode (default): the
        // closest-octave rule + explicit '/, offset, shared with the exporters.
        // The relative chain runs on the ORIGINAL pitches; transpose is applied
        // afterwards, so a transposed part still resolves octaves from what the
        // user wrote.
        int actualOctave = _octave.Resolve(step, pitch.OctaveOffset, pitchName);

        // Display pitch = written pitch, transposed if the part has transpose:.
        var (dStep, dAlt, dOctave) = _octave.TransposePitch(step, pitch.AccidentalOffset, actualOctave);

        // Staff position 0 = middle line of the staff.
        //   Treble: B4   Bass: D3   Alto: C4 (middle line)   Tenor: A3
        // The C clefs differ — alto puts middle C on the middle line, tenor on
        // the 4th line (so the middle line is A3, a third lower). Without their
        // own cases both fell through to the treble default and rendered alike.
        int basePosition = _meta.Clef switch
        {
            "treble" or "treble_8" or "treble^8" => dStep - GetPitchIndex('b') + (dOctave - 4) * 7,
            "bass" or "bass_8" => dStep - GetPitchIndex('d') + (dOctave - 3) * 7,
            "alto" or "percussion" => dStep - GetPitchIndex('c') + (dOctave - 4) * 7,
            "tenor" => dStep - GetPitchIndex('a') + (dOctave - 3) * 7,
            // C clefs on other lines: middle line = G4 (soprano), E4 (mezzo), F3 (baritone).
            "soprano" => dStep - GetPitchIndex('g') + (dOctave - 4) * 7,
            "mezzosoprano" => dStep - GetPitchIndex('e') + (dOctave - 4) * 7,
            "baritone" => dStep - GetPitchIndex('f') + (dOctave - 3) * 7,
            _ => dStep - GetPitchIndex('b') + (dOctave - 4) * 7
        };

        // RelativeOctave keeps the ORIGINAL octave for the next note's chain.
        _pitchTrace.Add(new PitchTraceEntry(pitch.Position, FormatPitch(dStep, dAlt, dOctave)));
        return new ResolvedPitch(basePosition, actualOctave, dStep, dAlt, dOctave);
    }

    /// <summary>Formats a resolved pitch as a letter + accidental + octave number
    /// (C4 = middle C), e.g. "C4", "F#5", "Bb3", "Cx4" (double sharp).</summary>
    private static string FormatPitch(int step, int alteration, int octave)
    {
        char letter = "CDEFGAB"[((step % 7) + 7) % 7];
        string acc = alteration switch
        {
            >= 2 => "x",   // double sharp
            1 => "#",
            -1 => "b",
            <= -2 => "bb",
            _ => ""
        };
        return $"{letter}{acc}{octave}";
    }

    private static int GetPitchIndex(char pitch) => RelativeOctave.StepIndex(pitch);

    // internal (not private) so the F3 MeasureContext chain can map the
    // score-level initial clef string to a ClefType with the SAME canonical
    // table the collector uses (no duplicate clef map). Visibility only.
    internal static ClefType ParseClefType(string clef) => clef switch
    {
        "bass" => ClefType.Bass,
        "alto" => ClefType.Alto,
        "tenor" => ClefType.Tenor,
        "treble_8" => ClefType.Treble8Below,
        "treble^8" => ClefType.Treble8Above,
        "soprano" => ClefType.Soprano,
        "mezzosoprano" => ClefType.MezzoSoprano,
        "baritone" => ClefType.Baritone,
        "bass_8" => ClefType.Bass8Below,
        "percussion" => ClefType.Percussion,
        _ => ClefType.Treble
    };

    internal static BarlineType ParseBarlineType(string text) => text switch
    {
        "|:" => BarlineType.RepeatStart,
        ":|" => BarlineType.RepeatEnd,
        ":|:" => BarlineType.RepeatBoth,
        "||" => BarlineType.Double,
        "|." => BarlineType.Final,
        "!" => BarlineType.Dashed,
        _ => BarlineType.Single
    };

}
