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

// Music-node walk for MeasureCollector: gathering descendant music nodes,
// the one-node lookahead sequence driver, the per-node dispatch switch, and
// tuplet processing. Split out of MeasureCollector.cs as a partial class;
// same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    /// <summary>
    /// Adds a single descendant node to the flat music-node list, expanding
    /// variable references in place.
    /// </summary>
    private void GatherMusicNode(SyntaxNode node, List<SyntaxNode> musicNodes)
    {
        if (node is VariableReferenceSyntax varRef)
            ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, musicNodes, varRef.DiatonicShiftSteps);
        // NOTE: unlike the other walks, the per-voice path does NOT treat a
        // << \\ >> span as one wrapper. Its caller does not skip parallel
        // descendants, so the inner notes are collected (flattened) here — the
        // established multi-voice rendering behavior. A ParallelExpressionSyntax
        // node itself is therefore not added.
        else if (node is not ParallelExpressionSyntax && IsCollectableMusicNode(node))
            musicNodes.Add(node);
    }

    /// <summary>
    /// Processes a flat list of music nodes with one-node lookahead for
    /// ties/slurs/beams (which annotate the preceding note).
    /// </summary>
    private void ProcessMusicNodeSequence(List<SyntaxNode> musicNodes, MeasureBuilder builder)
    {
        for (int i = 0; i < musicNodes.Count; i++)
        {
            var node = musicNodes[i];

            // Phrase-reference boundary: evaluate the body in the default frame,
            // shifted by the reference's octave marks, and auto-transposed from the
            // score's home key to the ambient key here.
            if (node is RelativeResetMarker reset)
            {
                EnterDefaultFrame(reset.OctaveOffset);
                EnterPhraseTranspose(reset.DiatonicSteps, reset.AnchorStep);
                // A phrase reference is ONE item; its boundary re-arms the confirmable
                // boundary like a section start, so a barline at the edge of the phrase
                // body does not pair with an adjacent outer barline into an empty measure
                // (phrase x { … | } then `x | x` is two bars, not two bars + a gap).
                builder.ResetMeasureBoundary();
                continue;
            }

            // End of a phrase body: drop its auto-transpose so following inline
            // notes stay at their written pitch. A phrase that ended with a closed
            // bar hands that bar over as retargetable, so an OUTER `|` (section {
            // x | x }) owns the barline the phrase's trailing `|` drew.
            if (node is PhraseEndMarker)
            {
                ExitPhraseTranspose();
                builder.ResetMeasureBoundary(retargetableClose: true);
                continue;
            }

            ProcessMusicNode(node, builder, PeekMarkers(PeekPastAttachedMarks(musicNodes, i)));
        }
    }

    /// <summary>
    /// The lookahead node for position <paramref name="i"/>: the next list entry
    /// that is not a NOTE-ATTACHED mark. The flattened walk lists a note's own
    /// <c>@name(...)</c> mark (a MusicMarkSyntax child in its articulations)
    /// right after the note — it must stay in the list, because for a rehearsal
    /// mark the statement arm is the live collection path — but the naive
    /// <c>[i + 1]</c> lookahead read that mark instead of the tie/slur/beam
    /// marker written after the note: <c>c'8@text("x")[</c> silently lost its
    /// manual beam to the autobeamer (LP regression beaming.ly, the beam over
    /// the bar line), and a tie after such a mark died the same way.
    /// </summary>
    private static SyntaxNode? PeekPastAttachedMarks(List<SyntaxNode> nodes, int i)
    {
        for (int j = i + 1; j < nodes.Count; j++)
        {
            if (nodes[j] is MusicMarkSyntax mark
                && (mark.IsInside<NoteSyntax>() || mark.IsInside<ChordSyntax>()
                    || mark.IsInside<DrumNoteSyntax>() || mark.IsInside<RestSyntax>()
                    || mark.IsInside<ChordRepetitionSyntax>()))
                continue;
            return nodes[j];
        }
        return null;
    }

    /// <summary>
    /// One-node lookahead flags. Ties/slurs/beams are written AFTER the note
    /// they annotate, so a note's flags come from the following node.
    /// </summary>
    private readonly record struct MarkerFlags(
        bool HasTieAfter, bool HasSlurStartAfter, bool HasSlurEndAfter,
        bool HasBeamStartAfter, bool HasBeamEndAfter);

    /// <summary>
    /// Computes the tie/slur/beam lookahead for a note from the node that
    /// follows it. Centralized so the top-level stream, tuplet bodies, and the
    /// structure walk can't drift — a drifted copy previously silently dropped
    /// markers inside tuplet/structure bodies.
    /// </summary>
    private static MarkerFlags PeekMarkers(SyntaxNode? next) => new(
        HasTieAfter: next is TieSyntax,
        HasSlurStartAfter: next is SlurSyntax { IsOpen: true },
        HasSlurEndAfter: next is SlurSyntax { IsOpen: false },
        HasBeamStartAfter: next is BeamMarkerSyntax { IsStart: true },
        HasBeamEndAfter: next is BeamMarkerSyntax { IsStart: false });

    /// <summary>Resets the relative-octave and default-duration state to the
    /// initial frame — the invariant applied at every phrase-reference
    /// (<see cref="RelativeResetMarker"/>) boundary. <paramref name="octaveOffset"/>
    /// carries the reference's trailing marks (<c>Chorus'</c> / <c>Chorus,</c>),
    /// shifting the fresh frame so the movable phrase lands an octave up or down.
    /// The shift only bites in relative mode; absolute pitches (octave absolute)
    /// anchor to a fixed C and ignore the running frame, so they carry their own
    /// octaves and are unaffected by a reference mark.</summary>
    private void EnterDefaultFrame(int octaveOffset = 0)
    {
        _octave.ResetToInitial();
        _octave.CurrentOctave += octaveOffset;
        _defaultDuration = Fraction.Quarter;
        _defaultDots = 0;
    }

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, MarkerFlags m)
        => ProcessMusicNode(node, builder, m.HasTieAfter, m.HasSlurStartAfter,
            m.HasSlurEndAfter, m.HasBeamStartAfter, m.HasBeamEndAfter);

    /// <summary>
    /// Converts the inline volta endings collected during this voice walk into
    /// volta brackets. Each ending's right cap follows its source: a closing ']'
    /// draws the cap (closed), omitting it leaves the ending open. The engraver's
    /// segment splitter still opens a closed bracket only where a line break cuts
    /// it, so a closed ending never dangles a hook mid-system.
    /// </summary>
    private void FinalizeInlineVoltas()
    {
        foreach (var (startMeasure, endMeasure, voltaText, isClosed, sourcePosition) in _pendingInlineVoltas)
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
        _pendingInlineVoltas.Clear();
    }

    /// <summary>
    /// Emits an arpeggio (<c>&lt;&lt; c e g &gt;&gt;</c>) — a written-out broken chord — as
    /// SEQUENTIAL notes that EQUALLY SUBDIVIDE the group's total duration (an auto-tuplet
    /// when the equal share is not a plain note value: 3 notes in a beat play a triplet, 5 a
    /// quintuplet). The octaves anchor to the first pitched member — the chord rule (the
    /// octave reference stays frozen on the root while the pitch names flow) — and scale
    /// degrees (<c>&lt;&lt; c 3 5 &gt;&gt;</c>) resolve against the root and the key.
    /// </summary>
    private void ProcessArpeggio(ArpeggioSyntax arpeggio, MeasureBuilder builder)
    {
        var members = arpeggio.Members.ToList(); // bare pitches, degrees, chords and/or rests
        if (members.Count == 0)
            return;

        // The group occupies its total — the trailing `>>N`, or (absent one) the inherited
        // running duration (it acts like a single note), dots included: a group after
        // `c4.` spans a dotted quarter. This read missed the dots when _defaultDots
        // landed (2026-08-07) — the self-audit found it, not a book; no corpus twin
        // exercises a group after a dotted duration yet.
        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration — default_duration_
        Fraction total = arpeggio.TotalDuration?.ToFraction()
            ?? _defaultDuration.Dotted(_defaultDots);
        var sub = ArpeggioSubdivision.Compute(members.Count, total);
        Fraction scale = sub.TimeScale;
        var forced = (sub.MemberValue, sub.MemberDots);
        // Octave marks after '>>' shift the whole group (like a chord's '<c e g>,'): the
        // shift is applied to the ROOT, and the stacked members / degrees inherit it through
        // the anchor octave the root sets.
        int groupOctave = arpeggio.OctaveOffset;

        int measureIndex = builder.CurrentMeasureIndex;
        int startNoteIndex = builder.CurrentItemCount;

        // The ROOT is the first PITCHED member (leading rests just advance time) — it
        // resolves relative to the incoming frame and anchors the group. Every later PITCHED
        // member STACKS above it (the same octave placement as a `<c e g>` chord member, so
        // `<< c e g >>` == `<< c g >>` for g, and a `,` drops one below); rests keep the
        // normal frame; degrees stack on the root by diatonic steps in the key.
        bool savedAbsolute = _octave.OctaveAbsolute;
        int savedBase = _octave.OctaveBase;
        bool rootSet = false;
        int anchorOctave = 0;
        char rootLetter = 'c';
        int rootStep = 0;
        foreach (var member in members)
        {
            if (member is ScaleDegreeSyntax degree)
            {
                // Degrees anchor on the root — or, before any pitched member, on the
                // KEY TONIC (like an omitted-root degree chord), which then becomes
                // the group's anchor and outgoing reference. A custom/atonal key has
                // no tonic, so fall back to C.
                if (!rootSet)
                {
                    rootSet = true;
                    rootStep = _ambientTonicValid ? _ambientTonicStep : 0;
                    rootLetter = "cdefgab"[rootStep];
                    anchorOctave = _octave.Resolve(rootStep, 0, rootLetter) + groupOctave;
                }
                EmitArpeggioDegree(degree, builder, forced, scale, rootStep, anchorOctave);
                continue;
            }

            char? letter = FirstPitchLetter(member);
            // The group octave shift applies to the ROOT member only; the stacked members
            // pick it up via the anchor octave (which the shifted root sets).
            bool isRoot = !rootSet && letter is not null;
            if (rootSet && letter is { } l)
            {
                _octave.OctaveAbsolute = true;
                _octave.OctaveBase = anchorOctave + (GetPitchIndex(l) >= rootStep ? 0 : 1);
            }
            else
            {
                _octave.OctaveAbsolute = savedAbsolute; // the root, and any rest
            }
            EmitArpeggioMember(member, builder, forced, scale, isRoot ? groupOctave : 0);
            if (!rootSet && letter is { } rl)
            {
                rootSet = true;
                anchorOctave = _octave.CurrentOctave;
                rootLetter = rl;
                rootStep = GetPitchIndex(rl);
            }
        }
        _octave.OctaveAbsolute = savedAbsolute;
        _octave.OctaveBase = savedBase;
        // After the group the running reference is the root (chord-after behavior).
        if (rootSet)
        {
            _octave.CurrentOctave = anchorOctave;
            _octave.LastPitchName = rootLetter;
        }

        // Post-events after '>>': a chord name (bare '@chord' derives it from the
        // members; explicit '@chord(...)' shows as written) and a dynamic (@f — it
        // takes effect at the group's start, as if written on the first member),
        // both anchored on the group's first item. Other annotations are not
        // applied; AnnotationNameValidator warns (LYS4008) so nothing is silent.
        CollectChordNames(arpeggio, measureIndex, startNoteIndex);
        CollectDynamics(arpeggio, measureIndex, startNoteIndex);

        // Auto-tuplet: the members were added WITHOUT duration — draw the bracket now.
        if (sub.HasTuplet)
        {
            int endNoteIndex = builder.CurrentItemCount - 1;
            if (endNoteIndex >= startNoteIndex)
                _tupletBrackets.Add(new TupletBracketItem(sub.TupletNum, sub.TupletBase,
                    startNoteIndex, endNoteIndex, measureIndex, arpeggio.Position, 0,
                    _currentStaffIndex, _currentVoiceIndex));
        }
        // The group consumes exactly `total`; record it once (AddDuration may roll the bar,
        // which is why the bracket indices were captured above).
        builder.AddDuration(total, arpeggio.Position + 1);

        // Acts like one note: a trailing `>>N` carries N as the running duration
        // (dots included, like any written duration).
        if (arpeggio.TotalDuration is { } td)
        {
            _defaultDuration = Fraction.FromNoteValue(td.Value);
            _defaultDots = td.DotCount;
        }
    }

    /// <summary>Emit one arpeggio pitch / chord / rest member at the group's forced
    /// equal-subdivision value and tuplet <paramref name="scale"/> (added WITHOUT
    /// advancing the measure duration — the group adds its total once).</summary>
    private void EmitArpeggioMember(SyntaxNode member, MeasureBuilder builder,
        (int Value, int Dots) forced, Fraction scale, int octaveShift)
    {
        switch (member)
        {
            case PitchSyntax pitch:
                builder.AddItemWithoutDuration(BuildArpeggioNoteItem(pitch, forced, octaveShift) with { TimeScale = scale });
                break;
            case ChordSyntax chord:
                builder.AddItemWithoutDuration(
                    CreateChordItem(chord, forcedDuration: forced, extraOctave: octaveShift) with { TimeScale = scale });
                break;
            case RestSyntax rest:
                builder.AddItemWithoutDuration(
                    CreateRestItem(rest, forcedDuration: forced) with { TimeScale = scale });
                break;
        }
    }

    /// <summary>A bare arpeggio pitch → NoteItem, resolved through the octave frame the
    /// caller set up (root relative, later members stacked in absolute mode), at the group's
    /// forced value/dots. <paramref name="octaveShift"/> is the group-level octave mark,
    /// applied to the root (0 for stacked members, which inherit it via the anchor).</summary>
    private NoteItem BuildArpeggioNoteItem(PitchSyntax pitch, (int Value, int Dots) forced, int octaveShift)
    {
        // Stacked members arrive in forced-absolute mode and keep the plain path.
        // The ROOT, in relative mode, anchors on its bare LETTER: its own '/, marks
        // are LOCAL to its sounding pitch (<< c' e g >> = C5 E4 G4) and do not move
        // the anchor the group stacks on and propagates.
        ResolvedPitch rp;
        if (_octave.OctaveAbsolute)
        {
            rp = ShiftOctave(CalculateStaffPosition(pitch), octaveShift);
            _octave.CurrentOctave = rp.RelativeOctave;
        }
        else
        {
            char name = pitch.PitchName.ToLowerInvariant()[0];
            int step = GetPitchIndex(name);
            int anchor = _octave.Resolve(step, 0, name) + octaveShift;
            rp = ResolveAbsolutePitch(step, pitch.AccidentalOffset,
                anchor + pitch.OctaveOffset, pitch.Position);
            _octave.CurrentOctave = anchor;
        }
        int staffPosition = rp.StaffPosition;
        var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
        if (pitch.QuarterOffset != 0)
            accidental = QuarterToneAccidental(pitch, accidental);
        bool needsLedger = staffPosition <= -6 || staffPosition >= 6;
        return new NoteItem(staffPosition, Fraction.FromNoteValue(forced.Value), forced.Dots,
            accidental, needsLedger, pitch.Position, 0, isCourtesy: false)
        {
            Midi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave),
        };
    }

    /// <summary>A scale-degree arpeggio member (<c>&lt;&lt; c 3 5 &gt;&gt;</c>) → NoteItem,
    /// stacked on the group's anchor (the root, or the key tonic when no pitched member
    /// precedes — the caller resolves it) by diatonic steps in the WRITTEN key (the
    /// transpose is applied once by <see cref="ResolveAbsolutePitch"/>).</summary>
    private void EmitArpeggioDegree(ScaleDegreeSyntax degree, MeasureBuilder builder,
        (int Value, int Dots) forced, Fraction scale, int rootStep, int anchorOctave)
    {
        int writtenKeySharps = _meta.KeySharps - _octave.TransposeKeySharps(0);
        var (step, alteration, octave) = ChordDegrees.Resolve(
            rootStep, anchorOctave, degree.Number, degree.Alteration, degree.OctaveOffset, writtenKeySharps);
        var rp = ResolveAbsolutePitch(step, alteration, octave, degree.Position);
        var accidental = GetDisplayAccidental(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave);
        bool needsLedger = rp.StaffPosition is <= -6 or >= 6;
        var noteItem = new NoteItem(rp.StaffPosition, Fraction.FromNoteValue(forced.Value), forced.Dots,
            accidental, needsLedger, degree.Position, 0, isCourtesy: false)
        {
            Midi = PitchToMidi(rp.DisplayStep, rp.DisplayAlteration, rp.DisplayOctave),
        };
        builder.AddItemWithoutDuration(noteItem with { TimeScale = scale });
    }

    /// <summary>The letter of a member's root pitch — a bare pitch's letter, or a chord's
    /// root (first pitch) — used to stack the arpeggio's members above the first. Degrees
    /// and rests return null (they do not anchor the frame).</summary>
    private static char? FirstPitchLetter(SyntaxNode member) => member switch
    {
        PitchSyntax p => p.PitchName.ToLowerInvariant()[0],
        ChordSyntax c => c.Root?.PitchName.ToLowerInvariant()[0],
        _ => null,
    };

    // A slur mark written on an empty chord, waiting for the item that occupies the empty
    // chord's moment. See TakeEmptyChordSlurs.
    private bool _pendingEmptyChordSlurStart;
    private bool _pendingEmptyChordSlurEnd;

    /// <summary>
    /// Merges a slur mark carried by a preceding empty chord <c>&lt;&gt;</c> into the item
    /// now being emitted, and clears it.
    /// </summary>
    /// <remarks>
    /// <c>&lt;&gt;</c> occupies no time, so its moment IS the following note's moment, and a
    /// slur mark on it binds to the note column that lands there — the FOLLOWING note, not
    /// the preceding one the <c>)</c> visually trails.
    /// ★ MEASURED against LilyPond 2.26.0 (scratch/lpreg/ecslur-{a,b,c}.ly): the slur of
    /// <c>r4 e'8( g' &lt;&gt;) c''4</c> and of <c>r4 e'8( g' c''4)</c> are the SAME curve
    /// (both 1.2883 → 6.1207), while closing on <c>g'</c> gives a different one
    /// (0.7803 → 3.5345). ⚠️ Do not "fix" this to end on the visually preceding note.
    /// LILYPOND-REF: lily/slur-engraver.cc:131-137 — <c>acknowledge_note_column</c> adds the
    /// column it is handed to every slur waiting in <c>end_slurs_</c>, so a STOP ends the
    /// slur at the NEXT note column to arrive.
    /// LILYPOND-REF: lily/parser.yy:3166-3183 — <c>chord_body</c> makes an
    /// <c>event_chord</c> and <c>chord_body_elements</c> has an empty production, so
    /// <c>&lt;&gt;</c> is an event chord whose only elements are the post-events that
    /// <c>note_chord_element</c> (:3148-3164) appends.
    /// ⚠️ NOT a transcription of the engraver: LP reaches this by holding the slur in
    /// <c>end_slurs_</c> until a column is acknowledged, Lily# by holding the MARK until an
    /// item that can bind one is emitted. Same rule, different machine — the measured
    /// identity above is what pins them together, so keep that probe.
    /// ⚠️ Disclosed: a grace group between the empty chord and the next main note takes the
    /// mark to the MAIN note (grace items are collected on their own path). Untested against
    /// LP — no fixture reaches it.
    /// </remarks>
    private void TakeEmptyChordSlurs(ref bool hasSlurStartAfter, ref bool hasSlurEndAfter)
    {
        if (!_pendingEmptyChordSlurStart && !_pendingEmptyChordSlurEnd)
            return;
        hasSlurStartAfter |= _pendingEmptyChordSlurStart;
        hasSlurEndAfter |= _pendingEmptyChordSlurEnd;
        _pendingEmptyChordSlurStart = false;
        _pendingEmptyChordSlurEnd = false;
    }

    /// <summary>Whether this node emits an item a slur can bind to — the carrier an empty
    /// chord's slur mark is waiting for. A wrapper (tuplet, grace, repeat) is not one; its
    /// own inner emit picks the mark up.</summary>
    private static bool BindsASlur(SyntaxNode node) => node switch
    {
        ChordSyntax c => !c.IsEmpty,
        NoteSyntax or RestSyntax or ChordRepetitionSyntax or ArpeggioSyntax => true,
        _ => false,
    };

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false)
    {
        // ⚠️ The two bool reads come FIRST on purpose: they are false for every item of
        // every score that never writes `<>`, and that short-circuit keeps the type switch
        // in BindsASlur out of the per-item path this walk runs on every keystroke.
        if ((_pendingEmptyChordSlurStart || _pendingEmptyChordSlurEnd) && BindsASlur(node))
            TakeEmptyChordSlurs(ref hasSlurStartAfter, ref hasSlurEndAfter);

        switch (node)
        {
            case GraceExpressionSyntax grace:
                // Store grace expression to attach to the next note
                _pendingGrace = grace;
                break;

            // A cue is a REGION, walked with the ordinary walker so that everything a voice
            // can hold works inside it unchanged — MEASURED in
            // audit/lp-geometry/probes/cue-span.ly, LilyPond draws a bar line, a tuplet, a
            // grace, a script and a rest inside a CueVoice without complaint (books C-*).
            // What the region does NOT let through is a beam, a tie or a slur (books B-*),
            // and that is the whole reason a per-note @cue could not work: those three are
            // invisible to a mark and would have to be guessed.
            case CueExpressionSyntax cue:
                ProcessCueRegion(cue, builder);
                break;

            case ParallelExpressionSyntax parallel:
                {
                    // << \\ >> span. Voice 0 joins the primary stream (this
                    // builder) so measure indices stay continuous; the extra
                    // voices are reconstructed later from the recorded span.
                    var voiceBlocks = parallel.Voices.ToList();
                    // The frame at the span's OPENING is what every voice reads from, and
                    // what the music after the span reads from — a span of simultaneous
                    // music does not move the relative frame (see _parallelSpans). Voice 0
                    // is walked inline here, so it is saved and restored around that walk;
                    // the other voices take the recorded frame in BuildExtraVoiceTracks.
                    var spanFrame = _octave.Snapshot();
                    _parallelSpans.Add((parallel, builder.CurrentMeasureIndex, spanFrame));
                    if (voiceBlocks.Count > 0)
                    {
                        // Voice 0 is render voice 1: an override in its block scopes to it.
                        _currentVoiceScope = 1;
                        ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder);
                        _currentVoiceScope = null;
                        _octave.Restore(spanFrame);
                    }
                }
                break;

            case NoteSyntax note:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    // Onset timing of this note (elapsed duration before it is added)
                    // — anchors note-attached marks to the right column.
                    Fraction noteAnchorTiming = builder.CurrentDuration;
                    // Process grace notes BEFORE the main note so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasGliss = HasGlissandoArticulation(note);
                    int featherDir = GetFeatherDirection(note);
                    bool isCue = _cueDepth > 0;
                    // Pre-scan for @courtesy annotation before creating note
                    if (HasCourtesyAnnotation(note))
                        _courtesySourcePositions.Add(note.Position);
                    var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, hasGliss, featherDir, isCue);
                    if (ExtractNoteheadStyle(note) is var nhStyle && nhStyle != NoteheadStyle.Default)
                        noteItem = noteItem with { Notehead = nhStyle };
                    if (_tremoloPairShape is { } tpn)
                    {
                        // Halve the sounding time (display stays the total)
                        // and join the pair with the subdivision's beams.
                        noteItem = noteItem with
                        {
                            TimeScale = noteItem.TimeScale * new Fraction(1, 2),
                            TremoloPairBeams = tpn.Beams,
                            TremoloGapCount = tpn.GapCount,
                            HasBeamStart = _tremoloPairFirst,
                            HasBeamEnd = !_tremoloPairFirst,
                        };
                        _tremoloPairFirst = false;
                    }
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        noteItem = noteItem with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(noteItem);
                    CollectDynamics(note, measureIndex, itemIndex);
                    CollectArticulations(note, measureIndex, itemIndex, noteItem.StemUp,
                        noteItem.EditorialAccidental, noteAnchorTiming);
                    CollectFiguredBass(note, measureIndex, itemIndex);
                    CollectChordNames(note, measureIndex, itemIndex);
                    CollectCrossStaff(note, measureIndex, itemIndex);
                }
                break;

            case DrumNoteSyntax drumNote:
                {
                    int drumMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int drumItemIndex = builder.CurrentItemCount;
                    Fraction drumAnchorTiming = builder.CurrentDuration;
                    var drumItem = CreateDrumNoteItem(drumNote);
                    builder.AddItem(drumItem);
                    // The drums-style table marks the closed hi-hat "+" and
                    // the open hi-hat "○" automatically.
                    if (DrumOverrides.Resolve(_drumOverrides, drumNote.DrumName) is { Mark: not null } dInfoMark)
                        _articulations.Add(new ArticulationItem(
                            dInfoMark.Mark == "stopped"
                                ? ArticulationType.Stopped
                                : ArticulationType.Flageolet,
                            drumMeasureIndex, drumItemIndex, true,
                            drumNote.Position, _currentStaffIndex)
                        { VoiceIndex = _currentVoiceIndex });
                    CollectDynamics(drumNote, drumMeasureIndex, drumItemIndex);
                    CollectArticulations(drumNote, drumMeasureIndex, drumItemIndex, drumItem.StemUp,
                        null, drumAnchorTiming);
                }
                break;

            case RestSyntax rest:
                {
                    // A rest is a legal slur bound (LilyPond r16( … r): rests live
                    // inside NoteColumn grobs, so the Slur_engraver binds to them
                    // like any column — "slur-rest-direction.ly". These flags used
                    // to be dropped on the floor here, which silently swallowed a
                    // rest-bound slur with no warning.
                    var restItem = CreateRestItem(rest);
                    int restMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int restItemIndex = builder.CurrentItemCount;
                    Fraction restAnchorTiming = builder.CurrentDuration;
                    int count = rest.MeasureCount;
                    if (count <= 1)
                    {
                        builder.AddItem(restItem with
                        {
                            HasSlurStart = hasSlurStartAfter,
                            HasSlurEnd = hasSlurEndAfter,
                        });
                        // Post-events on the rest (r4@fermata, r2@coda, ...).
                        // Rests have no stem; stemUp=false makes the default
                        // direction UP, matching scripts over rests.
                        CollectArticulations(rest, restMeasureIndex, restItemIndex, stemUp: false, anchorTiming: restAnchorTiming);
                        // A dynamic rides a rest exactly as it rides a note (r2@p) —
                        // the engraver listens to the EVENT, not the note, and the
                        // text X-centres on the rest's ink (AnchorCentreOffset's rest
                        // branch). This walk was the one caller missing the collect:
                        // r@p rendered the rest and silently dropped the p.
                        // LILYPOND-REF: lily/dynamic-engraver.cc Dynamic_engraver — the
                        //   dynamic is its own event stream, unanchored to note heads
                        //   (regression dynamics-rest-positioning.ly is the pin).
                        CollectDynamics(rest, restMeasureIndex, restItemIndex);
                    }
                    else
                    {
                        // LILYPOND-REF: lily/parser.yy:3117-3120 MULTI_MEASURE_REST —
                        // R<dur>*N is ONE event carrying an N factor, which expands to N
                        // consecutive measure-rests semantically. The MeasureBuilder
                        // auto-completes each measure when its duration reaches the
                        // time signature.
                        // Only the FIRST copy is the written event; the rest are its
                        // interior. Without that distinction the copies are identical and
                        // MultiMeasureRestEngraver.FindRuns cannot tell `R1*3` from
                        // `R1 | R1 | R1`, so it merged the latter into one three-bar rest.
                        // LilyPond engraves three one-bar rests there (one spanner per
                        // written event) — measured on 2.26.0, scratch/lpreg/pcmsh-r1.log.
                        // The interior copies are all identical, so clone ONCE and reuse:
                        // cloning inside the loop would allocate N-1 records per written
                        // rest (99 of them for `R1*100`) to no purpose.
                        var interior = restItem with { OpensWrittenRun = false };
                        builder.AddItem(restItem);
                        for (int i = 1; i < count; i++)
                            builder.AddItem(interior);
                    }
                }
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction chordAnchorTiming = builder.CurrentDuration;
                    // The EMPTY chord <> is a zero-time carrier: it adds NO item,
                    // advances NO time, leaves the running default duration alone,
                    // and its post-events (dynamics, scripts) attach at the CURRENT
                    // moment — which is the next item's column index, exactly where
                    // the timestep's grobs go. It used to fall through to
                    // CreateChordItem, which threw on the empty member list
                    // ("Sequence contains no elements") and killed the render.
                    // LILYPOND-REF: lily/parser.yy chord_body "<>" — an event chord
                    //   with only post-events; regression empty-chord.ly is the pin
                    //   ("occupy no time, and leave the current duration unchanged").
                    if (chord.IsEmpty)
                    {
                        // A duration written on the empty chord sets the running default for
                        // what follows, though the chord itself takes no time. The metric
                        // side must not be the only one that knows this, or the two
                        // disagree again.
                        // LILYPOND-REF: lily/parser.yy:3505-3514 optional_notemode_duration
                        //   — the same rule the neighbouring _defaultDuration writes already
                        //   cite; <> reaches it through note_chord_element (:3148-3164).
                        // Measured: MeasureDurations' ChordSyntax{IsEmpty} arm lists the
                        // three LP bar-checks that separate "takes time" from "sets default".
                        if (chord.Duration != null)
                        {
                            _defaultDuration = Fraction.FromNoteValue(chord.Duration.Value);
                            _defaultDots = chord.Duration.DotCount;
                        }
                        CollectDynamics(chord, measureIndex, itemIndex);
                        CollectArticulations(chord, measureIndex, itemIndex,
                            stemUp: false, anchorTiming: chordAnchorTiming);
                        // A SLUR mark needs a carrier grob, so unlike a dynamic it cannot be
                        // addressed by column index — it waits for the item that occupies
                        // this moment (TakeEmptyChordSlurs). Dropping it here drew no slur
                        // and, until the file had to be structured, said nothing either:
                        // the twin of regression empty-chord.ly lost its phrase mark in
                        // silence (LYS0020 work, 2026-08-09).
                        _pendingEmptyChordSlurStart |= hasSlurStartAfter;
                        _pendingEmptyChordSlurEnd |= hasSlurEndAfter;
                        break;
                    }
                    // Process grace notes BEFORE the main chord so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasArpeggio = HasArpeggioArticulation(chord);
                    // @arpeggio(bracket) = non-arpeggiate (do NOT roll) — LilyPond's
                    // \nonArpeggiato, a ChordBracket rather than an Arpeggio.
                    // ⚠️ READ BEFORE THE ITEM IS BUILT, not after it is added: the SPACING
                    // reads this off the ChordItem (ItemSkylineFactory.AddArpeggio), so a
                    // bracket discovered only in time for the _arpeggios list is drawn with
                    // no room reserved for it.
                    bool arpBracket = chord.Articulations.Any(art =>
                        art is MusicMarkSyntax { } am
                        && am.MarkName.Equals("arpeggio.bracket", StringComparison.OrdinalIgnoreCase));
                    bool isCue = _cueDepth > 0;
                    var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                    if (_tremoloPairShape is { } tpc)
                    {
                        // Two-note tremolo with a chord body (`repeat tremolo N
                        // { c32 <dis fis> }`): same halving/beam-joining as the
                        // note case — the chord case used to skip this, so a
                        // chord in a pair silently rendered at its written value.
                        chordItem = chordItem with
                        {
                            TimeScale = chordItem.TimeScale * new Fraction(1, 2),
                            TremoloPairBeams = tpc.Beams,
                            TremoloGapCount = tpc.GapCount,
                            HasBeamStart = _tremoloPairFirst,
                            HasBeamEnd = !_tremoloPairFirst,
                        };
                        _tremoloPairFirst = false;
                    }
                    if (arpBracket)
                        chordItem = chordItem with { HasArpeggioBracket = true };
                    if (ExtractNoteheadStyle(chord) is var chStyle && chStyle != NoteheadStyle.Default)
                        chordItem = chordItem with { Notehead = chStyle };
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        chordItem = chordItem with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(chordItem);
                    CollectDynamics(chord, measureIndex, itemIndex);
                    // Use chord stem direction for articulation placement
                    CollectArticulations(chord, measureIndex, itemIndex, chordItem.StemUp, anchorTiming: chordAnchorTiming);
                    CollectFiguredBass(chord, measureIndex, itemIndex);
                    CollectChordNames(chord, measureIndex, itemIndex);
                    CollectCrossStaff(chord, measureIndex, itemIndex);
                    // Collect arpeggio if present (bracket or wiggle — see arpBracket above).
                    if ((hasArpeggio || arpBracket) && chordItem.Notes.Length > 0)
                    {
                        int minPos = chordItem.Notes.Min(n => n.StaffPosition);
                        int maxPos = chordItem.Notes.Max(n => n.StaffPosition);
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, chord.Position, _currentStaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            // `q` — the previous chord again, with its own duration/post-events.
            // Mirrors the ChordSyntax case; the octave frame is NOT touched (LP
            // expands q after \relative, so a q is transparent to the frame).
            case ChordRepetitionSyntax rep:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction repAnchorTiming = builder.CurrentDuration;
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasArpeggio = HasArpeggioArticulation(rep);
                    bool arpBracket = rep.Articulations.Any(art =>
                        art is MusicMarkSyntax { } am
                        && am.MarkName.Equals("arpeggio.bracket", StringComparison.OrdinalIgnoreCase));
                    bool isCue = _cueDepth > 0;
                    var repItem = CreateChordRepetitionItem(rep, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                    if (repItem is not ChordItem chordCopy)
                    {
                        // Bad chord repetition: a spacer keeps the time; the
                        // validator reports it (nothing is silent).
                        builder.AddItem(repItem);
                        break;
                    }
                    if (_tremoloPairShape is { } tpr)
                    {
                        // Two-note tremolo with a chord-repetition body (`repeat
                        // tremolo 4 { c16 q16 }`): same halving/beam-joining as the
                        // note and chord arms — this arm used to skip it, so the
                        // repeated chord silently rendered at its written value
                        // with a flag (regression repeat-tremolo-chord-rep.ly).
                        chordCopy = chordCopy with
                        {
                            TimeScale = chordCopy.TimeScale * new Fraction(1, 2),
                            TremoloPairBeams = tpr.Beams,
                            TremoloGapCount = tpr.GapCount,
                            HasBeamStart = _tremoloPairFirst,
                            HasBeamEnd = !_tremoloPairFirst,
                        };
                        _tremoloPairFirst = false;
                    }
                    if (arpBracket)
                        chordCopy = chordCopy with { HasArpeggioBracket = true };
                    if (ExtractNoteheadStyle(rep) is var repStyle && repStyle != NoteheadStyle.Default)
                        chordCopy = chordCopy with { Notehead = repStyle };
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        chordCopy = chordCopy with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(chordCopy);
                    CollectDynamics(rep, measureIndex, itemIndex);
                    CollectArticulations(rep, measureIndex, itemIndex, chordCopy.StemUp, anchorTiming: repAnchorTiming);
                    CollectFiguredBass(rep, measureIndex, itemIndex);
                    CollectChordNames(rep, measureIndex, itemIndex);
                    CollectCrossStaff(rep, measureIndex, itemIndex);
                    if ((hasArpeggio || arpBracket) && chordCopy.Notes.Length > 0)
                    {
                        int minPos = chordCopy.Notes.Min(n => n.StaffPosition);
                        int maxPos = chordCopy.Notes.Max(n => n.StaffPosition);
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, rep.Position, _currentStaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            case ArpeggioSyntax arpeggio:
                ProcessArpeggio(arpeggio, builder);
                break;

            case BarlineSyntax barline:
                var barType = ParseBarlineType(barline.BarToken.Text);
                // Pass the '|' token's INK offset (not barline.Position, which includes
                // leading trivia) so the barline's click/highlight data-pos lands on the
                // written bar, not the whitespace before it.
                builder.HandleBarline(barType, barline.BarToken.Span.Start);
                break;

            case InlineVoltaSyntax volta:
                {
                    // Render the ending's music in place (the body before |: … :| is
                    // written once; repeat barlines imply repetition) and overlay a
                    // volta bracket across the measures the ending occupies.
                    int startMeasureIndex = builder.CurrentMeasureIndex;

                    var innerNodes = new List<SyntaxNode>();
                    foreach (var item in volta.Items)
                        GatherMusicNode(item, innerNodes);
                    ProcessMusicNodeSequence(innerNodes, builder);

                    int endMeasureIndex = builder.CurrentMeasureIndex;
                    if (builder.CurrentItemCount > 0)
                        endMeasureIndex++; // include the in-progress measure
                    int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                    _pendingInlineVoltas.Add((startMeasureIndex, lastMeasure, volta.VoltaText, volta.IsClosed, volta.Position));
                }
                break;

            case BreakSyntax brk:
                // 'break' forces a line break here; 'nobreak' forbids one.
                if (brk.IsNoBreak)
                    builder.SetNoBreak();
                else
                    builder.SetBreak();
                break;

            case MusicMarkSyntax mark:
                {
                    // A note-attached compound mark (e.g. b@ped.off) is also surfaced
                    // here as a statement node; CollectArticulations already created it
                    // anchored to its host note. Skip this un-anchored duplicate so the
                    // release ("*") stays at its note rather than snapping to the bar.
                    if (_musicMarks.Any(m => m.SourcePosition == mark.Position))
                        break;
                    var markType = MusicMarkItem.ParseMarkName(mark.MarkName);
                    if (markType != null)
                    {
                        if (markType.Value == MusicMarkType.Rehearsal)
                        {
                            string text = MusicMarkItem.ParseRehearsalText(mark.MarkName);
                            _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, text, builder.CurrentMeasureIndex, mark.Position));
                        }
                        else
                        {
                            _musicMarks.Add(new MusicMarkItem(markType.Value, builder.CurrentMeasureIndex, mark.Position));
                        }
                    }
                }
                break;

            case NavigationMarkSyntax nav:
                {
                    // A bare navigation mark inside a section's music: place its sign at
                    // the current note position (same MusicMarkItem the form uses).
                    var navType = NavigationToMusicMark(nav.MarkType);
                    // A landmark belongs at a barline; flag a mid-measure placement.
                    if (!builder.AtMeasureBoundary)
                    {
                        // The reader knows the notation term ("D.S.", "To Coda"), not the
                        // internal enum name ("DalSegno") — spell it the way it is written.
                        string term = nav.MarkType switch
                        {
                            NavigationMarkType.Segno => "segno",
                            NavigationMarkType.Coda => "coda",
                            NavigationMarkType.Fine => "Fine",
                            NavigationMarkType.ToCoda => "To Coda",
                            NavigationMarkType.DaCapo => "D.C.",
                            NavigationMarkType.DaCapoAlFine => "D.C. al Fine",
                            NavigationMarkType.DaCapoAlCoda => "D.C. al Coda",
                            NavigationMarkType.DalSegno => "D.S.",
                            NavigationMarkType.DalSegnoAlFine => "D.S. al Fine",
                            NavigationMarkType.DalSegnoAlCoda => "D.S. al Coda",
                            _ => nav.MarkType.ToString()
                        };
                        _navPlacementWarnings.Add(new NavigationMarkPlacementWarning(nav.Position, term));
                    }
                    _musicMarks.Add(new MusicMarkItem(navType, builder.CurrentMeasureIndex, nav.Position));
                }
                break;

            case ClefDeclarationSyntax clefDecl:
                {
                    // Mid-measure clef change. An UNCHANGED clef engraves nothing and
                    // changes nothing: LilyPond creates a Clef grob only when the
                    // resolved glyph/position/transposition differ from the previous
                    // ones, so a redundant `clef treble` neither prints nor takes
                    // space (clef-unchanged.ly) — and it must not reset the relative
                    // frame to the clef's default octave either. ClefType bundles
                    // glyph+position+transposition, so one enum compare is that test;
                    // LilyPond's forceClef escape hatch has no Lily# spelling and is
                    // dropped with it.
                    // LILYPOND-REF: lily/clef-engraver.cc:139-166 inspect_clef_properties
                    string newClef = clefDecl.ClefName.Text.ToLowerInvariant();
                    if (ParseClefType(newClef) == ParseClefType(_meta.Clef))
                        break;
                    _meta.Clef = newClef;
                    _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
                    // The clef NAME's token span — `clef |bass`. Not clefDecl.Position,
                    // which is the declaration's FULL span and so starts at the trivia
                    // in front of it (see TimeDataPos for what that costs).
                    var clefChange = new ClefChangeItem(
                        ParseClefType(newClef), clefDecl.ClefName.Span.Start);
                    builder.AddItem(clefChange);
                }
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Mid-stream octave-mode switch: affects only how subsequent
                // pitches resolve '/, marks; emits no grob.
                _octave.OctaveAbsolute = octaveDir.IsAbsolute;
                break;

            case KeySignatureSyntax keySig:
                // Mid-measure key signature change
                // LILYPOND-REF: lily/key-engraver.cc — process_music() creates KeySignature grob
                ApplyKeySignatureChange(keySig, builder);
                break;

            case TimeSignatureSyntax timeSigChange:
                {
                    // LilyPond's Time_signature_engraver makes ONE TimeSignature
                    // grob per timestep, reflecting the CURRENT value, and the very
                    // first timestep compares against last_spec_ = null. So a
                    // \time before any note collapses INTO the initial signature
                    // (only the new value prints) — the default 4/4 never gets its
                    // own grob. A \time at the first moment of the piece therefore
                    // REPLACES the initial signature rather than printing a separate
                    // change grob on top of it ("C 3/4").
                    // LILYPOND-REF: lily/time-signature-engraver.cc:94-122
                    //   process_music — `if (time_signature_) return;` (one per
                    //   timestep) and the last_spec_ comparison.
                    if (builder.AtPieceOpening)
                    {
                        _meta.TimeBeats = timeSigChange.Beats;
                        _meta.TimeBeatsText = timeSigChange.BeatsText;
                        _meta.TimeBeatType = timeSigChange.BeatType;
                        builder.SetMeasureLength(new Fraction(timeSigChange.Beats, timeSigChange.BeatType));
                    }
                    else
                    {
                        // Mid-piece change: a zero-duration grob printed at the
                        // change point, re-arming the following measures' length.
                        var newTime = new TimeSignature(timeSigChange.Beats, timeSigChange.BeatType, timeSigChange.BeatsText);
                        // The numerator, not the keyword — see TimeDataPos.
                        builder.AddItem(new TimeSignatureChangeItem(newTime, TimeDataPos(timeSigChange)));
                    }
                }
                break;

            case TempoDeclarationSyntax tempoChange:
                {
                    // Mid-piece tempo change: a metronome mark (♩= NNN) above the
                    // staff at this point (the initial tempo is drawn from
                    // Score.Tempo). LILYPOND-REF: scm/define-grobs.scm MetronomeMark.
                    // Anchor on the note that FOLLOWS the \tempo (its musical
                    // moment) so a mid-measure change prints above that note, as
                    // LilyPond does — not snapped to the measure start. The next
                    // item appended to this measure takes index CurrentItemCount.
                    // CurrentDuration is the time elapsed in this measure, used
                    // to resolve the column X on a grand staff (where the voice's
                    // item index would point into the wrong staff's note list).
                    if (tempoChange.Bpm is int bpm)
                        _musicMarks.Add(new MusicMarkItem(
                            MusicMarkType.Tempo, bpm.ToString(),
                            // The declaration's first VALUE — see TempoDataPos.
                            builder.CurrentMeasureIndex, TempoDataPos(tempoChange),
                            builder.CurrentItemCount, builder.CurrentDuration)
                        {
                            // The mid-music path dropped everything but the
                            // number — "tempo Lively 4. = 80" rendered ♩=80.
                            TempoText = tempoChange.Marking,
                            TempoBeatUnit = tempoChange.BeatUnit ?? 4,
                            TempoDots = tempoChange.BeatDots,
                            SwingSubdivision = tempoChange.SwingSubdivision,
                        });
                    else if (tempoChange.Marking is { } markingOnly)
                        // Text-only change ("tempo Meno mosso"): bold marking,
                        // no metronome equation.
                        _musicMarks.Add(new MusicMarkItem(
                            MusicMarkType.Tempo, "",
                            // The declaration's first VALUE — see TempoDataPos.
                            builder.CurrentMeasureIndex, TempoDataPos(tempoChange),
                            builder.CurrentItemCount, builder.CurrentDuration)
                        {
                            TempoText = markingOnly,
                        });
                }
                break;

            case PartialDeclarationSyntax partial:
                // Anacrusis: shorten the current measure to the declared pickup
                // length so it auto-completes early; the meter resumes after.
                // LILYPOND-REF: ly/music-functions-init.ly:1670-1678 \partial.
                builder.SetPartial(partial.ToFraction());
                break;

            case TieSyntax:
            case SlurSyntax:
            case BeamMarkerSyntax:
                // Already processed with the preceding note
                break;

            case TupletExpressionSyntax tuplet:
                // LILYPOND-REF: lily/tuplet-engraver.cc - process tuplet as a unit
                ProcessTuplet(tuplet, builder, nestingDepth: 0);
                break;

            case RepeatExpressionSyntax repeat:
                // LILYPOND-REF: lily/percent-repeat-engraver.cc - percent repeat handling
                ProcessRepeatExpression(repeat, builder);
                break;

            case OverrideDeclarationSyntax overrideDecl:
                CollectOverride(overrideDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: false, staffIndex: _currentStaffIndex);
                // Track it so the next section boundary reverts it to the part default.
                _sectionActiveGrobProps.Add((overrideDecl.GrobName.Text, overrideDecl.PropertyName.Text));
                break;

            case RevertDeclarationSyntax revertDecl:
                CollectRevert(revertDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, staffIndex: _currentStaffIndex);
                _sectionActiveGrobProps.Remove((revertDecl.GrobName.Text, revertDecl.PropertyName.Text));
                break;

            case OnceModifierSyntax onceModifier:
                if (onceModifier.Command is OverrideDeclarationSyntax innerOverride)
                    CollectOverride(innerOverride, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: true, staffIndex: _currentStaffIndex);
                else if (onceModifier.Command is RevertDeclarationSyntax innerRevert)
                    CollectRevert(innerRevert, builder.CurrentMeasureIndex, builder.CurrentItemCount, staffIndex: _currentStaffIndex);
                break;
        }
    }

    /// <summary>
    /// Walks one <c>cue { … }</c> / <c>cue &lt;clef&gt; { … }</c> region.
    /// </summary>
    /// <remarks>
    /// The body goes through <see cref="ProcessMusicNodeSequence"/> unchanged; the only
    /// state the region carries is <see cref="_cueDepth"/> (which makes the items cue-sized)
    /// and, when a clef is given, LilyPond's <c>\cueClef</c> / <c>\cueClefUnset</c> pair.
    /// <para>
    /// ⚠️ BOTH clefs are emitted, and that is not tidiness: MEASURED
    /// (audit/lp-geometry/probes/cue-span.ly, book D-NOUNSET) LilyPond leaks the cue clef
    /// into the rest of the staff when the unset is missing — the note after the region read
    /// staff position 13 instead of 1. The closing clef restores the staff's own clef and is
    /// itself drawn small.
    /// </para>
    /// </remarks>
    private void ProcessCueRegion(CueExpressionSyntax cue, MeasureBuilder builder)
    {
        string? outerClef = null;
        if (cue.ClefKeyword is { } clefToken)
        {
            outerClef = _meta.Clef;
            string cueClef = clefToken.Text.ToLowerInvariant();
            _meta.Clef = cueClef;
            _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            // The token's span, not its Position — the clef name, not the trivia.
            builder.AddItem(new ClefChangeItem(
                ParseClefType(cueClef), clefToken.Span.Start, isCue: true));
        }

        _cueDepth++;
        ProcessMusicNodeSequence(cue.Body.Items.ToList(), builder);
        _cueDepth--;

        if (outerClef is not null)
        {
            _meta.Clef = outerClef;
            _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            builder.AddItem(new ClefChangeItem(
                ParseClefType(outerClef), cue.Body.Position + cue.Body.FullWidth, isCue: true));
        }
    }

    /// <summary>
    /// Processes a tuplet expression, collecting notes and creating a bracket item.
    /// Supports nested tuplets via recursive calls with increasing nesting depth.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tuplet-engraver.cc - Tuplet_engraver class
    /// LILYPOND-REF: lily/tuplet-bracket.cc:400-500 - nested bracket stacking
    ///
    /// For nested tuplets, duration scaling compounds:
    /// outer 3/2 containing inner 3/2 { e8 f g } →
    /// inner actual = 3/8 * 2/3 = 1/4, then outer scales again.
    /// Only the top-level tuplet (nestingDepth=0) adds duration to the measure.
    /// </remarks>
    /// <returns>The actual (scaled) duration of this tuplet.</returns>
    private Fraction ProcessTuplet(TupletExpressionSyntax tuplet, MeasureBuilder builder, int nestingDepth,
        Fraction? parentScale = null)
    {
        int measureIndex = builder.CurrentMeasureIndex;
        int startNoteIndex = builder.CurrentItemCount;

        // Cumulative time scale for items inside this tuplet. Items store
        // their ACTUAL duration (written × base/ratio, compounded through
        // nesting): BaseDuration carries the notation, Duration carries time.
        // Beat-based beaming and spacing need real time positions — a triplet
        // of written 8ths occupies ONE beat, so its beam group is the tuplet
        // itself, not "three 8ths plus whatever fills the half note".
        Fraction scale = (parentScale ?? new Fraction(1, 1))
            * new Fraction(tuplet.BaseDivision, tuplet.TupletRatio);

        // Track written duration of all items in the tuplet
        Fraction writtenDuration = Fraction.Zero;
        int lastSourcePosition = tuplet.Position;

        // Process all notes inside the tuplet body using Items property
        // (not DescendantNodes which includes all nested nodes)
        // Use AddItemWithoutDuration to avoid incorrect auto-completion
        var tupletItems = tuplet.Body.Items.ToList();
        for (int j = 0; j < tupletItems.Count; j++)
        {
            var item = tupletItems[j];

            // One-node lookahead for tie/slur/beam markers that annotate the
            // preceding note — the same rule ProcessMusicNodeSequence applies to
            // the top-level stream. Without this, a tie/slur/beam written inside
            // a tuplet body was silently dropped.
            var next = j + 1 < tupletItems.Count ? tupletItems[j + 1] : null;
            var (hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter) = PeekMarkers(next);

            if (item is TupletExpressionSyntax nestedTuplet)
            {
                // LILYPOND-REF: lily/tuplet-bracket.cc - nested tuplet processing
                // Recursively process nested tuplet; its actual duration
                // counts as "written" duration for this outer tuplet
                writtenDuration += ProcessTuplet(nestedTuplet, builder, nestingDepth + 1, scale);
            }
            else
            {
                writtenDuration += EmitScaledItem(item, builder, scale,
                    hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
            }
            lastSourcePosition = item.Position;
        }

        // Calculate actual duration: written × base / ratio
        // e.g., tuplet 3/2: 3 quarters (3/4) → actual 2/4
        // LILYPOND-REF: lily/tuplet-bracket.cc - tuplet duration scaling
        int ratio = tuplet.TupletRatio;   // e.g., 3 (play 3 notes)
        int @base = tuplet.BaseDivision;  // e.g., 2 (in time of 2)
        Fraction actualDuration = new Fraction(
            writtenDuration.Numerator * @base,
            writtenDuration.Denominator * ratio);

        // Record the bracket BEFORE adding the duration: AddDuration can
        // auto-complete (roll) the measure, after which CurrentItemCount is
        // reset and the indexes would be garbage — that dropped the second
        // nested tuplet's outer bracket and mis-indexed its inner one.
        int endNoteIndex = builder.CurrentItemCount - 1;

        // Only add bracket if we have at least 2 notes
        if (endNoteIndex >= startNoteIndex)
        {
            _tupletBrackets.Add(new TupletBracketItem(
                tuplet.TupletRatio,
                tuplet.BaseDivision,
                startNoteIndex,
                endNoteIndex,
                measureIndex,
                tuplet.Position,
                nestingDepth,
                _currentStaffIndex,
                _currentVoiceIndex
            ));
        }

        // Only add duration to the measure at the top level
        // Nested tuplets return their duration to the parent for compounding
        if (nestingDepth == 0)
        {
            builder.AddDuration(actualDuration, lastSourcePosition + 1);
        }

        return actualDuration;
    }

    /// <summary>
    /// Emits one note / rest / chord at a tuplet-scaled duration (TimeScale carries the
    /// notation-vs-time factor) with its post-events, and returns its WRITTEN duration.
    /// Shared by <see cref="ProcessTuplet"/> and the arpeggio auto-tuplet.
    /// </summary>
    private Fraction EmitScaledItem(SyntaxNode item, MeasureBuilder builder, Fraction scale,
        bool hasTieAfter, bool hasSlurStartAfter, bool hasSlurEndAfter, bool hasBeamStartAfter, bool hasBeamEndAfter)
    {
        // The first item inside a tuplet can be the carrier for a slur mark left by an
        // empty chord just before the group (the same hole the glissando note below records).
        // Flags first, as in ProcessMusicNode — this is the tuplet body's per-item path.
        if ((_pendingEmptyChordSlurStart || _pendingEmptyChordSlurEnd) && BindsASlur(item))
            TakeEmptyChordSlurs(ref hasSlurStartAfter, ref hasSlurEndAfter);

        int annMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
        int annItemIndex = builder.CurrentItemCount;
        Fraction annAnchor = builder.CurrentDuration;
        switch (item)
        {
            case NoteSyntax note:
            {
                // hasGlissando read here too — the main walk's arm reads it and this
                // arm didn't, which is the same one-arm-of-two hole the rest dynamics
                // above already had (a tuplet note's @glissando dropped silently).
                var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, HasGlissandoArticulation(note));
                builder.AddItemWithoutDuration(noteItem with { TimeScale = scale });
                CollectDynamics(note, annMeasureIndex, annItemIndex);
                CollectArticulations(note, annMeasureIndex, annItemIndex, noteItem.StemUp,
                    noteItem.EditorialAccidental, annAnchor);
                CollectFiguredBass(note, annMeasureIndex, annItemIndex);
                CollectChordNames(note, annMeasureIndex, annItemIndex);
                CollectCrossStaff(note, annMeasureIndex, annItemIndex);
                return noteItem.Duration;
            }
            case RestSyntax rest:
            {
                // Slur bounds on a tuplet rest too — same repair as the main
                // walk's rest arm (one-arm-of-two, like the rest dynamics above).
                var restItem = CreateRestItem(rest) with
                {
                    HasSlurStart = hasSlurStartAfter,
                    HasSlurEnd = hasSlurEndAfter,
                };
                builder.AddItemWithoutDuration(restItem with { TimeScale = scale });
                CollectArticulations(rest, annMeasureIndex, annItemIndex, stemUp: false, anchorTiming: annAnchor);
                // Same repair as the main walk's rest case: a rest carries dynamics too.
                CollectDynamics(rest, annMeasureIndex, annItemIndex);
                return restItem.Duration;
            }
            case ChordSyntax chord:
            {
                var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter,
                    hasArpeggio: false, isCue: false, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter);
                builder.AddItemWithoutDuration(chordItem with { TimeScale = scale });
                CollectDynamics(chord, annMeasureIndex, annItemIndex);
                CollectArticulations(chord, annMeasureIndex, annItemIndex, chordItem.StemUp, anchorTiming: annAnchor);
                CollectFiguredBass(chord, annMeasureIndex, annItemIndex);
                CollectChordNames(chord, annMeasureIndex, annItemIndex);
                CollectCrossStaff(chord, annMeasureIndex, annItemIndex);
                return chordItem.Duration;
            }
            case ChordRepetitionSyntax rep:
            {
                // `q` inside a tuplet — LP expands repetitions late, so \times/
                // \tuplet still applies to them (regression chord-repetition-times).
                var repItem = CreateChordRepetitionItem(rep, hasBeamStartAfter, hasBeamEndAfter,
                    hasArpeggio: false, isCue: false, hasTieAfter: hasTieAfter,
                    hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                if (repItem is ChordItem chordCopy)
                {
                    builder.AddItemWithoutDuration(chordCopy with { TimeScale = scale });
                    CollectDynamics(rep, annMeasureIndex, annItemIndex);
                    CollectArticulations(rep, annMeasureIndex, annItemIndex, chordCopy.StemUp, anchorTiming: annAnchor);
                    CollectFiguredBass(rep, annMeasureIndex, annItemIndex);
                    CollectChordNames(rep, annMeasureIndex, annItemIndex);
                    CollectCrossStaff(rep, annMeasureIndex, annItemIndex);
                    return chordCopy.Duration;
                }
                // Bad chord repetition: the spacer keeps the tuplet's time.
                var spacer = (RestItem)repItem;
                builder.AddItemWithoutDuration(spacer with { TimeScale = scale });
                return spacer.Duration;
            }
        }
        return Fraction.Zero;
    }
}
