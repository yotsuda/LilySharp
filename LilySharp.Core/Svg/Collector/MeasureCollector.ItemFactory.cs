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

        // What a following bare duration copies (same contract as
        // _resolvedChordMembers): the ABSOLUTE spelling, resolved by THIS walk —
        // a repetition must not re-run the written pitch through the relative
        // frame, whose anchor has moved on to this very note.
        var resolvedSpelling = new ResolvedChordMember(
            staffPosition, rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave,
            NoteheadStyle.Default, PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave));
        _resolvedNotes[note] = resolvedSpelling;
        // Record mode (finding 3-4): log the write iff some bare duration copies this
        // note, so a resume can restore the adopted prefix's entries. Order matters
        // (a form replay's overwrite must land last); the filter keeps repetition-free
        // books at zero log.
        if (_probeRecording != null && Music.BareDurations.IsOriginal(note))
            _resolvedSpellingLog.Add((note, ImmutableArray.Create(resolvedSpelling)));

        int noteValue = note.Duration?.Value ?? (int)_defaultDuration.Denominator;
        // An undurated note takes the whole default — dots included (`c8. c` is two
        // dotted eighths). LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration.
        int dots = note.Duration?.DotCount ?? _defaultDots;
        if (note.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }

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

        var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
        bool isCourtesy = false;

        // Quarter tones always print their own accidental (they are never in
        // the key). LILYPOND-REF: quarter-tone note names ih/eh/isih/eseh.
        if (note.Pitch.QuarterOffset != 0)
            accidental = QuarterToneAccidental(note.Pitch, accidental);

        // Explicit @courtesy annotation: print the pitch's accidental in parentheses.
        if (_courtesySourcePositions.Contains(note.SourceStart))
        {
            isCourtesy = true;
            // If no accidental shown, force the key-signature-matching accidental
            if (accidental == null)
                accidental = KeySignatureAccidentalName(rp.DisplayStep);
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
                editorialAccidental = KeySignatureAccidentalName(rp.DisplayStep);
            }
            accidental = null; // suggestion replaces the left-of-note accidental
        }

        return new NoteItem(
            staffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental,
            needsLedger,
            note.SourceStart,
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
            ForcedStemUp = GetStemDirectionOverride(note),
            LaissezVibrerUp = hasLv ? LaissezVibrerUpOf(note) : null,
            RepeatTieUp = hasRepeatTie ? RepeatTieUpOf(note) : null,
            // The half-tie names the '@' that wrote it, not this note's own address
            // (MusicItem.LaissezVibrerSourcePosition).
            LaissezVibrerSourcePosition = hasLv
                ? NamedArticulationSourceOf(note, "laissezvibrer") : MusicItem.NoSourcePosition,
            RepeatTieSourcePosition = hasRepeatTie
                ? NamedArticulationSourceOf(note, "repeattie") : MusicItem.NoSourcePosition,
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
        int dots = drum.Duration?.DotCount ?? _defaultDots;
        if (drum.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }
        int tremoloBeams = ParseTremoloBeams(drum.Tremolo);
        bool needsLedger = info.StaffPosition <= -6 || info.StaffPosition >= 6;

        return new NoteItem(
            info.StaffPosition,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental: null,
            needsLedger,
            drum.SourceStart,
            tremoloBeams)
        {
            Notehead = info.Notehead,
            Midi = info.GmKey,
            ForcedStemUp = GetStemDirectionOverride(drum),
        };
    }

    /// <summary>
    /// <c>a4@rest</c> — a rest that takes its vertical place from a written pitch
    /// instead of from its voice and the collisions around it.
    /// </summary>
    /// <remarks>
    /// LilyPond writes this <c>a4\rest</c>, and it is a REST EVENT that happens to
    /// carry a pitch, not a note: <c>Rest_engraver</c> reads the pitch only to set the
    /// grob's <c>staff-position</c>. So this is deliberately NOT <see cref="CreateNoteItem"/>
    /// with the head swapped — the pitch here must not print an accidental, must not
    /// enter the measure's accidental memory (no note head sounds, so LilyPond's
    /// Accidental_engraver never hears it), must not sound in MIDI, and must not take a
    /// ledger line. What it MUST still do is exactly what the note it replaces would:
    /// move the relative-octave frame on, and carry the duration to the next item.
    /// LILYPOND-REF: lily/rest-engraver.cc:62-80 process_music — the pitch becomes
    /// staff-position and nothing else;
    /// LILYPOND-REF: lily/parser.yy — the <c>\rest</c> post-event makes the event a rest.
    /// </remarks>
    private RestItem CreatePitchedRestItem(NoteSyntax note)
    {
        var rp = CalculateStaffPosition(note.Pitch);
        _octave.CurrentOctave = rp.RelativeOctave;

        int noteValue = note.Duration?.Value ?? (int) _defaultDuration.Denominator;
        int dots = note.Duration?.DotCount ?? _defaultDots;
        if (note.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }

        return new RestItem(Fraction.FromNoteValue(noteValue), dots, note.SourceStart)
        {
            StaffPosition = rp.StaffPosition,
        };
    }

    private RestItem CreateRestItem(RestSyntax rest, (int Value, int Dots)? forcedDuration = null)
    {
        // An arpeggio member has no written duration — the group forces the
        // equal-subdivision value/dots on it (and must not disturb the default carry).
        int noteValue = forcedDuration?.Value ?? rest.Duration?.Value ?? (int)_defaultDuration.Denominator;
        int dots = forcedDuration?.Dots ?? rest.Duration?.DotCount ?? _defaultDots;
        if (forcedDuration == null && rest.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }

        // 's' is a spacer rest: it occupies time/width but is never drawn (unlike 'r').
        return new RestItem(Fraction.FromNoteValue(noteValue), dots, rest.SourceStart)
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

    /// <summary>One resolved chord member as a <c>q</c> repetition copies it: the
    /// ABSOLUTE spelling (post-transpose Display* triple for pitched members, null
    /// for drums), never the display accidental — a copy re-derives its accidental
    /// through the normal stateful path, so the repeated chord shows exactly what
    /// its own measure requires (and the original's cautionary/forced marks are
    /// naturally absent — LP clears them, copy-repeat-chord :892-895).</summary>
    // Internal (was private) since finding 3-4: VoiceWalkRecording.ResolvedSpellings
    // carries these values across keystrokes for the resume's re-keyed replay.
    internal readonly record struct ResolvedChordMember(
        int StaffPosition, int? Step, int? Alter, int? Octave, NoteheadStyle Notehead, int Midi)
    {
        /// <summary>
        /// The same member displaced by whole octaves — what <c>q'</c> repeats. A
        /// diatonic octave is seven staff positions, twelve semitones and one octave
        /// number; the letter, the accidental and the notehead do not move, which is
        /// why the displaced chord keeps its spelling.
        /// </summary>
        internal ResolvedChordMember DisplacedBy(int octaves) => octaves == 0 ? this : this with
        {
            StaffPosition = StaffPosition + 7 * octaves,
            Octave = Octave + octaves,
            Midi = Midi + 12 * octaves,
        };
    }

    /// <summary>The resolved members of every chord this walk has built, keyed by
    /// node — what a following <c>q</c> copies. Refilled on every re-walk (form
    /// replays re-enter with a reset frame and resolve to the same values), so the
    /// entry a <c>q</c> reads is always the one its own walk just wrote.
    /// A RESUMED walk restores the adopted prefix's entries from the recording,
    /// re-keyed onto this tree's nodes (finding 3-4 — see
    /// <see cref="VoiceWalkRecording.ResolvedSpellings"/>).</summary>
    private readonly Dictionary<ChordSyntax, ImmutableArray<ResolvedChordMember>> _resolvedChordMembers = new();

    /// <summary>The resolved spelling of every pitched note this walk has built,
    /// keyed by node — what a following bare duration copies. Same refill-per-walk
    /// contract (and same resume restore) as <see cref="_resolvedChordMembers"/>.
    /// (Chords and drum notes need no twin: chords are in
    /// <see cref="_resolvedChordMembers"/>, and a drum or slash resolves statelessly
    /// from its own syntax.)</summary>
    private readonly Dictionary<NoteSyntax, ResolvedChordMember> _resolvedNotes = new();

    /// <summary>Record-mode walk log of the two dictionaries above, in insertion
    /// order, filtered to nodes that ARE the original of some <c>q</c> / bare
    /// duration in the tree — see <see cref="VoiceWalkRecording.ResolvedSpellings"/>.
    /// Cleared per walk; harvested into the recording at walk end.</summary>
    private readonly List<(SyntaxNode Node, ImmutableArray<ResolvedChordMember> Members)>
        _resolvedSpellingLog = new();

    /// <summary>Record-mode walk log of every <c>q</c> / bare duration built: the
    /// source position of the original it copied (-1 = none) — see
    /// <see cref="VoiceWalkRecording.RepetitionOriginalReads"/>. Cleared per walk.</summary>
    private readonly List<(int Start, int End)> _repetitionOriginalReads = new();

    private ChordItem CreateChordItem(ChordSyntax chord, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false, bool isCue = false, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, (int Value, int Dots)? forcedDuration = null, int extraOctave = 0)
    {
        var notes = new List<ChordNoteInfo>();
        var members = new List<ResolvedChordMember>();

        // A chord-level @laissezVibrer half-ties EVERY member; a member-level one
        // ties just its own head (detected in the member loop below). The event's
        // ^/_ forces every tie's side.
        // LILYPOND-REF: lily/laissez-vibrer-engraver.cc:66-108 acknowledge_note_head —
        // "use the heard event_ for all note heads, or an individual event for
        // just a single note head"; :99-103 direction copied from the event.
        bool chordLv = HasLaissezVibrerAnnotation(chord);
        bool? chordLvUp = chordLv ? LaissezVibrerUpOf(chord) : null;
        // The repeat-tie fans over the members the same way — Repeat_tie_engraver
        // IS a Laissez_vibrer_engraver with the event class and grob names swapped.
        // LILYPOND-REF: lily/repeat-tie-engraver.cc:27-33 Repeat_tie_engraver.
        bool chordRt = HasRepeatTieAnnotation(chord);
        bool? chordRtUp = chordRt ? RepeatTieUpOf(chord) : null;
        // The '@' each half-tie names. One chord-level annotation writes EVERY member's
        // tie, so all of them cite this one offset; a member-level annotation carries its
        // own on the ChordNoteInfo and wins there.
        int chordLvSrc = chordLv
            ? NamedArticulationSourceOf(chord, "laissezvibrer") : MusicItem.NoSourcePosition;
        int chordRtSrc = chordRt
            ? NamedArticulationSourceOf(chord, "repeattie") : MusicItem.NoSourcePosition;

        // Octave marks AFTER the closing '>' (<1 3 5>' / <c e g>,,) shift the WHOLE
        // chord uniformly. Applying it to the root's resolved octave (and, for an
        // omitted root, to the tonic anchor) flows through firstOctave into every
        // stacked/degree member; absolute-mode members each fold it into their own
        // octave, since absolute mode has no anchor to carry it.
        // extraOctave is the enclosing arpeggio's group-level shift when this chord is
        // the arpeggio's root (`<< <c e> g >>,`); 0 otherwise.
        int chordOctave = chord.ChordOctaveOffset + extraOctave;

        // Track first note's state for subsequent chord/note relative calculation
        int firstOctave = _octave.CurrentOctave;
        char firstPitchName = _octave.LastPitchName;
        int rootStepForStack = 0;

        foreach (var pitch in chord.Pitches)
        {
            ResolvedPitch rp;
            if (notes.Count == 0)
            {
                // The first member is the ROOT: its LETTER, resolved bare in the
                // incoming frame, is the chord's ANCHOR — the stacking base and
                // what the next chord/note is relative to. The root's own '/,
                // marks are LOCAL to its sounding pitch (<c' e g> = C5 E4 G4, and
                // the next note stays relative to C4); only the whole-chord marks
                // after '>' move the anchor. Absolute mode has no frame: the
                // root's marks are its register and anchor the degrees as written.
                firstPitchName = pitch.PitchName.ToLowerInvariant()[0];
                rootStepForStack = GetPitchIndex(firstPitchName);
                if (_octave.OctaveAbsolute)
                {
                    rp = CalculateStaffPosition(pitch, chordOctave);
                    firstOctave = rp.RelativeOctave;
                }
                else
                {
                    int anchor = _octave.Resolve(rootStepForStack, 0, firstPitchName) + chordOctave;
                    rp = ResolveAbsolutePitch(rootStepForStack, pitch.AccidentalOffset,
                        anchor + pitch.OctaveOffset, pitch.SourceStart);
                    firstOctave = anchor;
                }
            }
            else if (_octave.OctaveAbsolute)
            {
                // Absolute mode: every member is a fixed pitch (offset from the C
                // anchor), already order-independent; no stacking.
                rp = CalculateStaffPosition(pitch, chordOctave);
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
                rp = ResolveAbsolutePitch(step, pitch.AccidentalOffset, octave, pitch.SourceStart);
            }
            int staffPosition = rp.StaffPosition;

            var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);

            // Quarter tones always print their own accidental (never in the key).
            if (pitch.QuarterOffset != 0)
                accidental = QuarterToneAccidental(pitch, accidental);

            // Per-pitch @courtesy (<f@courtesy a …> = LP's f?): the member's
            // accidental prints in parentheses, forcing the key-matching one
            // when nothing would print — the same rule CreateNoteItem applies.
            // LILYPOND-REF: lily/accidental.cc:145-146 Accidental_interface
            // print — the "parenthesized" property wraps the stencil.
            bool memberCourtesy = pitch.Articulations.Any(a =>
                a is ArticulationSyntax { Type: ArticulationType.None } ca
                && ca.NameToken.Text.Equals("courtesy", StringComparison.OrdinalIgnoreCase));
            if (memberCourtesy && accidental == null)
                accidental = KeySignatureAccidentalName(rp.DisplayStep);

            bool needsLedger = staffPosition <= -6 || staffPosition >= 6;

            // LILYPOND-REF: lily/fingering-engraver.cc — per-pitch finger via <c@finger.N>.
            int? pitchFingering = ExtractPitchFingering(pitch);

            // Member-level @laissezVibrer / @repeatTie (<d@laissezVibrer g> =
            // LP <d-\laissezVibrer g>): this head only; the chord-level event covers
            // every head and wins (the engraver reads the heard event before the
            // articulation). Plain loop, not LINQ: this runs per member of every
            // chord on every collect walk (the preview's incremental recompiles
            // included).
            ArticulationSyntax? memberLv = null;
            ArticulationSyntax? memberRt = null;
            foreach (var a in pitch.Articulations)
                if (a is ArticulationSyntax { Type: ArticulationType.None } la)
                {
                    if (memberLv == null
                        && la.NameToken.Text.Equals("laissezvibrer", StringComparison.OrdinalIgnoreCase))
                        memberLv = la;
                    else if (memberRt == null
                        && la.NameToken.Text.Equals("repeattie", StringComparison.OrdinalIgnoreCase))
                        memberRt = la;
                }

            notes.Add(new ChordNoteInfo(
                staffPosition, accidental, needsLedger,
                IsCourtesy: memberCourtesy,
                Fingering: pitchFingering,
                StringNumber: pitch.Articulations.OfType<StringNumberAnnotationSyntax>().FirstOrDefault()?.StringNumber,
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave),
                SourcePosition: pitch.SourceStart,
                HasLaissezVibrer: chordLv || memberLv != null,
                LaissezVibrerUp: chordLv ? chordLvUp : memberLv?.ForcedAbove,
                HasRepeatTie: chordRt || memberRt != null,
                RepeatTieUp: chordRt ? chordRtUp : memberRt?.ForcedAbove,
                // THIS member's own '@' only; -1 means "the chord's annotation wrote my
                // tie", and the chord-level offset lives on the ChordItem. Unlike the
                // direction above, the fallback is not resolved here — TieVariantEngraver
                // reads the chord's first, because a chord-level event wins over a
                // member-level one and its '@' is then the character that wrote every tie.
                LaissezVibrerSourcePosition: memberLv?.SourceStart ?? MusicItem.NoSourcePosition,
                RepeatTieSourcePosition: memberRt?.SourceStart ?? MusicItem.NoSourcePosition));
            members.Add(new ResolvedChordMember(staffPosition, rp.DisplayStep, rp.DisplayAlteration,
                rp.DisplayOctave, NoteheadStyle.Default, PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave)));
        }

        // String numbers OUTSIDE the brackets (<e dis'>\5\4) pair with the members
        // in written order: each member without its own \N takes the next
        // chord-level one, so <e dis'>\5\4 == <e\5 dis'\4> == <e dis'\4>\5
        // (tablature.ly's claim). LilyPond walks the notes in order, the in-chord
        // articulation winning, and REPEATS the last outside event once the list
        // is exhausted — the repeat is ported as-is. Plain loop with a lazy list,
        // not LINQ: this runs per chord on every collect walk (the preview's
        // incremental recompiles included), and almost no chord carries \N — the
        // common case must not allocate.
        // LILYPOND-REF: lily/articulations.cc:38-80 articulation_list — per note
        //   event, the note's own articulation wins; else articulation_events[j],
        //   j advancing only while more remain.
        List<StringNumberAnnotationSyntax>? chordStrings = null;
        foreach (var art in chord.Articulations)
            if (art is StringNumberAnnotationSyntax sn)
                (chordStrings ??= new()).Add(sn);
        if (chordStrings != null)
        {
            int j = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].StringNumber != null)
                    continue;
                notes[i] = notes[i] with { StringNumber = chordStrings[j].StringNumber };
                if (j + 1 < chordStrings.Count)
                    j++;
            }
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
            var rp = ResolveAbsolutePitch(step, alteration, octave, degree.SourceStart);
            var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
            notes.Add(new ChordNoteInfo(
                rp.StaffPosition, accidental,
                rp.StaffPosition is <= -6 or >= 6,
                IsCourtesy: false,
                Midi: PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave),
                SourcePosition: degree.SourceStart,
                HasLaissezVibrer: chordLv,
                LaissezVibrerUp: chordLvUp,
                HasRepeatTie: chordRt,
                RepeatTieUp: chordRtUp));
            members.Add(new ResolvedChordMember(rp.StaffPosition, rp.DisplayStep, rp.DisplayAlteration,
                rp.DisplayOctave, NoteheadStyle.Default, PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave)));
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
                Midi: dinfo.GmKey,
                SourcePosition: drum.SourceStart,
                HasLaissezVibrer: chordLv,
                LaissezVibrerUp: chordLvUp,
                HasRepeatTie: chordRt,
                RepeatTieUp: chordRtUp));
            members.Add(new ResolvedChordMember(dinfo.StaffPosition, null, null, null,
                dinfo.Notehead, dinfo.GmKey));
        }
        var memberArray = members.ToImmutableArray();
        _resolvedChordMembers[chord] = memberArray;
        // Record mode (finding 3-4): same log as CreateNoteItem's, for a chord some
        // `q` or bare duration copies.
        if (_probeRecording != null
            && (Music.ChordRepetitions.IsOriginal(chord) || Music.BareDurations.IsOriginal(chord)))
            _resolvedSpellingLog.Add((chord, memberArray));

        // The next chord/note is relative to the chord's ANCHOR — the root's bare
        // letter plus any whole-chord '>' marks; the members' own marks stay local.
        _octave.CurrentOctave = firstOctave;
        _octave.LastPitchName = firstPitchName;

        // An arpeggio member has no written duration — the group forces the
        // equal-subdivision value/dots on it (and must not disturb the default carry).
        int noteValue = forcedDuration?.Value ?? chord.Duration?.Value ?? (int)_defaultDuration.Denominator;
        int dots = forcedDuration?.Dots ?? chord.Duration?.DotCount ?? _defaultDots;
        if (forcedDuration == null && chord.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }

        int tremoloBeams = ParseTremoloBeams(chord.Tremolo);

        // Inside `repeat tremolo N { … }` (see CreateNoteItem).
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        // Two-note tremolo: this chord prints at the pair's total duration
        // (same override as CreateNoteItem).
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, chord.SourceStart, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieStart: hasTieAfter, hasSlurStart: hasSlurStartAfter, hasSlurEnd: hasSlurEndAfter)
        {
            // A chord has ONE stem, so @stemUp / @stemDown on it is the same wish a note's is.
            ForcedStemUp = GetStemDirectionOverride(chord),
            // Read in the FACTORY so every chord-creating walk arm gets it — a walk-arm
            // read is exactly how a chord's @glissando was silently swallowed until
            // 2026-08-07 (regression glissando-accidental.ly: the event parsed, no
            // reader existed, eight of the book's ten lines vanished without a word).
            HasGlissando = HasGlissandoArticulation(chord),
            // The chord-level '@' every member's half-tie names when the chord carries
            // the annotation (the members' own offsets stay -1 there). Degree and drum
            // members have no articulation list of their own, so this is the ONLY offset
            // their ties can name.
            LaissezVibrerSourcePosition = chordLvSrc,
            RepeatTieSourcePosition = chordRtSrc,
        };
    }

    /// <summary>
    /// A <c>q</c> chord repetition → ChordItem: the ORIGINAL chord's resolved
    /// members (from <see cref="_resolvedChordMembers"/>) with the repetition's
    /// own duration and post-events. Display accidentals are re-derived through
    /// the stateful path, so cautionary/forced marks on the original are absent
    /// (LP clears them — copy-repeat-chord :892-895) and the copy shows what its
    /// own measure requires. Per-pitch fingerings/string numbers are NOT copied
    /// (LP copies note events only). The relative frame is NOT touched: LP
    /// expands q AFTER \relative has resolved (toplevel-music-functions), so a
    /// q is transparent to the frame. When no chord precedes the repetition
    /// (LP: "Bad chord repetition") the result is a SPACER rest of the written
    /// duration — the time still counts, nothing is drawn, and the validator
    /// reports it (LP keeps the empty chord the same way).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/music-functions.scm:854-920 copy-repeat-chord.</remarks>
    private MusicItem CreateChordRepetitionItem(ChordRepetitionSyntax rep, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool hasArpeggio = false, bool isCue = false, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, (int Value, int Dots)? forcedDuration = null)
    {
        // Checkpoint/resume (finding 3-4): a `q` reads _resolvedChordMembers, whose
        // entry is written when the ORIGINAL chord is walked. A resumed walk restores
        // the adopted prefix's entries from the recording's spelling log (re-keyed
        // onto the new tree), so this no longer makes the walk ineligible wholesale.
        // What the recorder logs HERE is the read: the original's position, which
        // the suffix splice checks against the re-walked live region (a recorded
        // resolution copying state from there is not certified — TrySpliceSuffix).
        if (_probeRecording != null)
            _repetitionOriginalReads.Add(
                Music.ChordRepetitions.OriginalOf(rep) is { } chordOriginal
                    ? (chordOriginal.FullSpan.Start, chordOriginal.FullSpan.End)
                    : (-1, -1));

        // The duration carry applies whether or not the q resolves — a bad
        // repetition still occupies its written time (LP keeps the empty chord).
        int noteValue = forcedDuration?.Value ?? rep.Duration?.Value ?? (int)_defaultDuration.Denominator;
        int dots = forcedDuration?.Dots ?? rep.Duration?.DotCount ?? _defaultDots;
        if (forcedDuration == null && rep.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }
        int tremoloBeams = ParseTremoloBeams(rep.Tremolo);

        // Inside `repeat tremolo N { … }` (see CreateNoteItem).
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }

        // Two-note tremolo: this chord prints at the pair's total duration
        // (same override as CreateNoteItem/CreateChordItem — the q arm used to
        // skip it, so `repeat tremolo 4 { c16 q16 }` printed the q at its
        // written 16th; regression repeat-tremolo-chord-rep.ly).
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        if (Music.ChordRepetitions.OriginalOf(rep) is not { } original
            || !_resolvedChordMembers.TryGetValue(original, out var members))
            return new RestItem(Fraction.FromNoteValue(noteValue), dots, rep.SourceStart) { IsSpacer = true };

        // q' repeats the chord an octave up. The displacement accumulates along the q
        // chain, so ChordRepetitions answers with the total rather than this node's marks.
        int displacement = Music.ChordRepetitions.DisplacementOf(rep);

        var notes = new List<ChordNoteInfo>(members.Length);
        foreach (var written in members)
        {
            var m = written.DisplacedBy(displacement);
            string? accidental = m.Step is { } step
                ? GetDisplayAccidental(step, m.Alter!.Value, m.Octave!.Value)
                : null;
            notes.Add(new ChordNoteInfo(
                m.StaffPosition, accidental,
                m.StaffPosition is <= -6 or >= 6,
                IsCourtesy: false,
                Notehead: m.Notehead,
                Midi: m.Midi,
                SourcePosition: rep.SourceStart));
        }

        return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, rep.SourceStart, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieStart: hasTieAfter, hasSlurStart: hasSlurStartAfter, hasSlurEnd: hasSlurEndAfter)
        {
            // The repetition's OWN post-events only — the original's are not copied.
            ForcedStemUp = GetStemDirectionOverride(rep),
            HasGlissando = HasGlissandoArticulation(rep),
        };
    }

    /// <summary>
    /// <c>/4</c> — a slash note: a pitchless NoteItem with a slash head on the
    /// MIDDLE staff line (staff position 0), silent in playback. Rhythm
    /// (comping) notation; duration carry, stems and beams behave as on an
    /// ordinary note.
    /// LILYPOND-REF: ly/property-init.ly improvisationOn — LilyPond spells this
    /// NoteHead.style = #'slash on a written pitch; Lily# carries no pitch, so
    /// the head sits where charts put it, the middle line, under any clef.
    /// </summary>
    private NoteItem CreateSlashNoteItem(SlashNoteSyntax slash, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool isCue = false, (int Value, int Dots)? forcedDuration = null)
    {
        int noteValue = forcedDuration?.Value ?? slash.Duration?.Value ?? (int)_defaultDuration.Denominator;
        int dots = forcedDuration?.Dots ?? slash.Duration?.DotCount ?? _defaultDots;
        if (forcedDuration == null && slash.Duration != null)
        {
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }
        int tremoloBeams = ParseTremoloBeams(slash.Tremolo);
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        return new NoteItem(
            staffPosition: 0,
            Fraction.FromNoteValue(noteValue),
            dots,
            accidental: null,
            needsLedgerLines: false,
            slash.SourceStart,
            tremoloBeams,
            hasTieStart: hasTieAfter,
            hasSlurStart: hasSlurStartAfter,
            hasSlurEnd: hasSlurEndAfter,
            hasBeamStart: hasBeamStartAfter,
            hasBeamEnd: hasBeamEndAfter,
            isCue: isCue)
        {
            Notehead = NoteheadStyle.Slash,
            // Silent: no pitch exists. The only NoteItem.Midi consumers are the
            // tab renderers, where 0 falls outside every tuning and the range
            // validator says so — a slash has no place on a fretboard.
            Midi = 0,
            ForcedStemUp = GetStemDirectionOverride(slash),
        };
    }

    /// <summary>
    /// <c>c4 4</c> — a bare duration: the previous note, chord or slash again at
    /// the written length. Resolution comes from the shared
    /// <see cref="Music.BareDurations"/> map plus this walk's recorded spellings
    /// (<see cref="_resolvedNotes"/> / <see cref="_resolvedChordMembers"/>) — the
    /// same two-step a <c>q</c> uses, for the same reason: the relative frame has
    /// moved on, so the original must be copied, never re-resolved.
    /// LILYPOND-REF: lily/parser.yy music_embedded; behaviour pinned against
    /// 2.26.0 (note / full chord / through rests — byte-identical to the
    /// explicit spellings).
    /// </summary>
    private MusicItem CreateBareDurationItem(BareDurationSyntax bare, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false, bool isCue = false, (int Value, int Dots)? forcedDuration = null)
    {
        // Same checkpoint/resume handling as `q` (finding 3-4): the prefix restore
        // supplies the original's entry; the recorder logs the read for the suffix
        // splice's certification guard.
        if (_probeRecording != null)
            _repetitionOriginalReads.Add(
                Music.BareDurations.OriginalOf(bare) is { } bareOriginal
                    ? (bareOriginal.FullSpan.Start, bareOriginal.FullSpan.End)
                    : (-1, -1));

        int noteValue = forcedDuration?.Value ?? bare.Duration.Value;
        int dots = forcedDuration?.Dots ?? bare.Duration.DotCount;
        if (forcedDuration == null)
        {
            // A bare duration ALWAYS sets the running default — it IS a written
            // duration (LP: music_embedded assigns parser->default_duration_).
            _defaultDuration = Fraction.FromNoteValue(noteValue);
            _defaultDots = dots;
        }
        int tremoloBeams = ParseTremoloBeams(bare.Tremolo);
        if (_tremoloRepeatCount > 1
            && CombineTremoloDuration(_tremoloRepeatCount, noteValue) is { } combined)
        {
            tremoloBeams = Math.Max(tremoloBeams, (int)Math.Log2(noteValue) - 2);
            noteValue = combined.Value;
            dots = combined.Dots;
        }
        if (_tremoloPairShape is { } pairDisp)
        {
            noteValue = pairDisp.Value;
            dots = pairDisp.Dots;
        }

        switch (Music.BareDurations.OriginalOf(bare))
        {
            case ChordSyntax chord when _resolvedChordMembers.TryGetValue(chord, out var members):
            {
                // The full chord again — same copy a `q` makes (accidentals
                // re-derived through the measure's own state, post-events not
                // copied), displacement included: a run that reached here through
                // a `q'` repeats the chord where that q left it.
                int bareDisplacement = Music.BareDurations.DisplacementOf(bare);
                var notes = new List<ChordNoteInfo>(members.Length);
                foreach (var writtenMember in members)
                {
                    var m = writtenMember.DisplacedBy(bareDisplacement);
                    string? accidental = m.Step is { } step
                        ? GetDisplayAccidental(step, m.Alter!.Value, m.Octave!.Value)
                        : null;
                    notes.Add(new ChordNoteInfo(
                        m.StaffPosition, accidental,
                        m.StaffPosition is <= -6 or >= 6,
                        IsCourtesy: false,
                        Notehead: m.Notehead,
                        Midi: m.Midi,
                        SourcePosition: bare.SourceStart));
                }
                return new ChordItem(notes.ToImmutableArray(), Fraction.FromNoteValue(noteValue), dots, bare.SourceStart, tremoloBeams, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio: false, isCue, hasTieStart: hasTieAfter, hasSlurStart: hasSlurStartAfter, hasSlurEnd: hasSlurEndAfter)
                {
                    ForcedStemUp = GetStemDirectionOverride(bare),
                    HasGlissando = HasGlissandoArticulation(bare),
                };
            }

            case NoteSyntax note when _resolvedNotes.TryGetValue(note, out var m):
                return new NoteItem(
                    m.StaffPosition,
                    Fraction.FromNoteValue(noteValue),
                    dots,
                    m.Step is { } noteStep ? GetDisplayAccidental(noteStep, m.Alter!.Value, m.Octave!.Value) : null,
                    needsLedgerLines: m.StaffPosition is <= -6 or >= 6,
                    bare.SourceStart,
                    tremoloBeams,
                    hasTieStart: hasTieAfter,
                    hasSlurStart: hasSlurStartAfter,
                    hasSlurEnd: hasSlurEndAfter,
                    hasBeamStart: hasBeamStartAfter,
                    hasBeamEnd: hasBeamEndAfter,
                    hasGlissando: HasGlissandoArticulation(bare),
                    isCue: isCue)
                {
                    Midi = m.Midi,
                    ForcedStemUp = GetStemDirectionOverride(bare),
                };

            case DrumNoteSyntax drum:
            {
                // Stateless from the original's own syntax — the drummap does not
                // move between the original and the repeat.
                var info = DrumOverrides.Resolve(_drumOverrides, drum.DrumName);
                return new NoteItem(
                    info.StaffPosition,
                    Fraction.FromNoteValue(noteValue),
                    dots,
                    accidental: null,
                    needsLedgerLines: info.StaffPosition is <= -6 or >= 6,
                    bare.SourceStart,
                    tremoloBeams,
                    hasTieStart: hasTieAfter,
                    hasSlurStart: hasSlurStartAfter,
                    hasSlurEnd: hasSlurEndAfter,
                    hasBeamStart: hasBeamStartAfter,
                    hasBeamEnd: hasBeamEndAfter,
                    isCue: isCue)
                {
                    Notehead = info.Notehead,
                    Midi = info.GmKey,
                    ForcedStemUp = GetStemDirectionOverride(bare),
                };
            }

            case SlashNoteSyntax:
                return new NoteItem(
                    staffPosition: 0,
                    Fraction.FromNoteValue(noteValue),
                    dots,
                    accidental: null,
                    needsLedgerLines: false,
                    bare.SourceStart,
                    tremoloBeams,
                    hasTieStart: hasTieAfter,
                    hasSlurStart: hasSlurStartAfter,
                    hasSlurEnd: hasSlurEndAfter,
                    hasBeamStart: hasBeamStartAfter,
                    hasBeamEnd: hasBeamEndAfter,
                    isCue: isCue)
                {
                    Notehead = NoteheadStyle.Slash,
                    Midi = 0,
                    ForcedStemUp = GetStemDirectionOverride(bare),
                };

            default:
                // Nothing to repeat: a spacer keeps the time; the validator
                // reports it (LYS0016 — nothing is silent).
                return new RestItem(Fraction.FromNoteValue(noteValue), dots, bare.SourceStart) { IsSpacer = true };
        }
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

    /// <param name="groupOctaves">
    /// The enclosing GROUP's octave marks — the ones after a chord's <c>&gt;</c> or an
    /// arpeggio's <c>&gt;&gt;</c>, which move every member alike. Folded into the octave
    /// BEFORE resolution rather than applied to the returned pitch, because
    /// <see cref="ResolveAbsolutePitch"/> writes the <c>--pitches</c> trace entry as it
    /// resolves: a shift applied afterwards moved the drawn note and left the report
    /// naming the unshifted one. Relative mode never had the bug because it adds the same
    /// shift into the chord's ANCHOR, before resolving. The fold is exact — the diatonic
    /// shift and the part transpose were both octave-equivariant (adding 12 semitones to
    /// <c>PitchTransposer</c>'s target leaves its spelling alone; the phrase's diatonic
    /// shift, removed 2026-08-28, was equivariant in sevenths the same way).
    /// </param>
    private ResolvedPitch CalculateStaffPosition(PitchSyntax pitch, int groupOctaves = 0)
    {
        char pitchName = pitch.PitchName.ToLowerInvariant()[0];
        int step = GetPitchIndex(pitchName);

        // Absolute mode: '/, are offsets from a fixed C4 anchor (bare c = C4),
        // stateless — every note is independent. Relative mode (default): the
        // closest-octave rule + explicit '/, offset, shared with the exporters.
        // The relative chain runs on the ORIGINAL pitches; transpose is applied
        // afterwards, so a transposed part still resolves octaves from what the
        // user wrote.
        int actualOctave = _octave.Resolve(step, pitch.OctaveOffset, pitchName) + groupOctaves;
        return ResolveAbsolutePitch(step, pitch.AccidentalOffset, actualOctave, pitch.SourceStart);
    }

    /// <summary>
    /// Transpose + staff-position + pitch-trace for an already-absolute written
    /// pitch (diatonic step 0..6, accidental in semitones, absolute octave).
    /// Shared by ordinary pitches (after relative-octave resolution) and by
    /// scale-degree chord members (absolute from the start, anchored on the root).
    /// </summary>
    private ResolvedPitch ResolveAbsolutePitch(int step, int accidentalOffset, int actualOctave, int position)
    {
        // (A phrase reference's interval argument shifted the body by scale steps in the
        // WRITTEN key HERE — modal transposition, applied before the chromatic part
        // transpose below. The spelling was removed 2026-08-28 and nothing else ever
        // armed the shift, so the written pitch reaches the transpose untouched.)

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
