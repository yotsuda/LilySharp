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
using LilySharp.Core.Music;
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
            // MIDI/tab pitch = the actually-drawn (display) pitch: pair the display
            // step/alteration with DisplayOctave, not the written RelativeOctave, so a
            // transpose that pushes a pitch across an octave boundary frets correctly.
            Midi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave),
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

    private RestItem CreateRestItem(RestSyntax rest, (int Value, int Dots)? forcedDuration = null)
    {
        // An arpeggio member has no written duration — the group forces the
        // equal-subdivision value/dots on it (and must not disturb the default carry).
        int noteValue = forcedDuration?.Value ?? rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (forcedDuration == null && rest.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = forcedDuration?.Dots ?? rest.Duration?.DotCount ?? 0;

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
    /// beams = log2(N) − 2, so :8 = 1, :16 = 2, :32 = 3, :64 = 4, :128 = 5.
    /// </summary>
    private static int ParseTremoloBeams(SyntaxTokenNode? tremolo)
    {
        if (tremolo == null)
            return 0;

        // Tremolo text is ":N" for a power-of-two value ≥ 8 (see Lexer).
        var text = tremolo.Text;
        if (text.Length < 2 || text[0] != ':')
            return 0;

        return int.TryParse(text[1..], out int value)
            && value >= 8 && (value & (value - 1)) == 0
            ? (int)Math.Log2(value) - 2
            : 0;
    }

    private ChordItem CreateChordItem(ChordSyntax chord, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false, bool isCue = false, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, (int Value, int Dots)? forcedDuration = null)
    {
        var notes = new List<ChordNoteInfo>();

        // Octave marks AFTER the closing '>' (<1 3 5>' / <c e g>,,) shift the WHOLE
        // chord uniformly. Applying it to the root's resolved octave (and, for an
        // omitted root, to the tonic anchor) flows through firstOctave into every
        // stacked/degree member; absolute-mode members are shifted individually.
        int chordOctave = chord.ChordOctaveOffset;

        // Track first note's state for subsequent chord/note relative calculation
        int firstOctave = _octave.CurrentOctave;
        char firstPitchName = _octave.LastPitchName;
        int rootStepForStack = 0;

        foreach (var pitch in chord.Pitches)
        {
            ResolvedPitch rp;
            if (notes.Count == 0)
            {
                // The first member is the ROOT — resolved relative to the incoming
                // frame; it anchors the chord and drives the next chord/note.
                rp = ShiftOctave(CalculateStaffPosition(pitch), chordOctave);
                firstOctave = rp.RelativeOctave;
                firstPitchName = pitch.PitchName.ToLowerInvariant()[0];
                rootStepForStack = GetPitchIndex(firstPitchName);
            }
            else if (_octave.OctaveAbsolute)
            {
                // Absolute mode: every member is a fixed pitch (offset from the C
                // anchor), already order-independent; no stacking.
                rp = ShiftOctave(CalculateStaffPosition(pitch), chordOctave);
            }
            else
            {
                // Relative mode: every other member STACKS above the root — the same
                // octave placement as a scale degree, so <c e g> == <c 3 5> and the
                // chord's pitches are independent of the order its notes are written.
                // A `,` drops a member below the root. (A deliberate Lily# divergence
                // from LilyPond's per-member relative chain.)
                int step = GetPitchIndex(pitch.PitchName.ToLowerInvariant()[0]);
                int octave = firstOctave + (step >= rootStepForStack ? 0 : 1) + pitch.OctaveOffset;
                rp = ResolveAbsolutePitch(step, pitch.AccidentalOffset, octave, pitch.Position);
            }
            int staffPosition = rp.StaffPosition;

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
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave)));
        }

        // Omitted root (<1 3 5> / <3 5>): the degrees are relative to the KEY'S
        // TONIC (degree 1 = tonic). Anchor the (unsounded) tonic in the relative
        // frame like a written root would be, so a following note stays relative
        // to it. A custom/atonal key has no tonic, so fall back to C.
        if (chord.Root is null && chord.Degrees.Any())
        {
            int tonicStep = _ambientTonicValid ? _ambientTonicStep : 0;
            char tonicName = "cdefgab"[tonicStep];
            firstOctave = _octave.Resolve(tonicStep, 0, tonicName) + chordOctave;
            firstPitchName = tonicName;
            _octave.CurrentOctave = firstOctave;
        }

        // Scale-degree members (<d 3 5 7,>): each stacks on the root by diatonic
        // steps in the current key, then is placed absolutely (no relative-frame
        // advance — the root already set the frame for the next chord/note).
        int rootStep = GetPitchIndex(firstPitchName);
        // Degrees stack in the WRITTEN key; the part transpose is then applied
        // once by ResolveAbsolutePitch (TransposePitch). Using the displayed
        // (already-transposed) key here would transpose a degree chord twice.
        int writtenKeySharps = _meta.KeySharps - _octave.TransposeKeySharps(0);
        foreach (var degree in chord.Degrees)
        {
            var (step, alteration, octave) = ChordDegrees.Resolve(
                rootStep, firstOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, writtenKeySharps);
            var rp = ResolveAbsolutePitch(step, alteration, octave, degree.Position);
            var (accidental, isCourtesy) =
                GetDisplayAccidentalWithCourtesy(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
            notes.Add(new ChordNoteInfo(
                rp.StaffPosition, accidental,
                rp.StaffPosition is <= -6 or >= 6,
                IsCourtesy: isCourtesy,
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave)));
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

        // An arpeggio member has no written duration — the group forces the
        // equal-subdivision value/dots on it (and must not disturb the default carry).
        int noteValue = forcedDuration?.Value ?? chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        if (forcedDuration == null && chord.Duration != null)
            _defaultDuration = Fraction.FromNoteValue(noteValue);

        int dots = forcedDuration?.Dots ?? chord.Duration?.DotCount ?? 0;
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

    /// <summary>Shift a resolved pitch by whole octaves (7 staff positions each) —
    /// used for chord-level octave marks after the closing <c>&gt;</c>. The spelling
    /// (step/alteration) is unchanged; only the register moves.</summary>
    private static ResolvedPitch ShiftOctave(ResolvedPitch rp, int octaves) =>
        octaves == 0 ? rp : rp with
        {
            StaffPosition = rp.StaffPosition + octaves * 7,
            RelativeOctave = rp.RelativeOctave + octaves,
            DisplayOctave = rp.DisplayOctave + octaves,
        };

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
        return ResolveAbsolutePitch(step, pitch.AccidentalOffset, actualOctave, pitch.Position);
    }

    /// <summary>
    /// Transpose + staff-position + pitch-trace for an already-absolute written
    /// pitch (diatonic step 0..6, accidental in semitones, absolute octave).
    /// Shared by ordinary pitches (after relative-octave resolution) and by
    /// scale-degree chord members (absolute from the start, anchored on the root).
    /// </summary>
    private ResolvedPitch ResolveAbsolutePitch(int step, int accidentalOffset, int actualOctave, int position)
    {
        // Display pitch = written pitch, transposed if the part has transpose:.
        var (dStep, dAlt, dOctave) = _octave.TransposePitch(step, accidentalOffset, actualOctave);

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
        _pitchTrace.Add(new PitchTraceEntry(position, FormatPitch(dStep, dAlt, dOctave)));
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
