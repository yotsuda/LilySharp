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
            ExpandVariable(varRef.Name.Text, musicNodes);
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

            // Phrase-reference boundary: evaluate the body in the default frame.
            if (node is RelativeResetMarker)
            {
                _octave.ResetToInitial();
                _defaultDuration = Fraction.Quarter;
                continue;
            }

            bool hasTieAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is TieSyntax;
            bool hasSlurStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurS && slurS.IsOpen;
            bool hasSlurEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is SlurSyntax slurE && !slurE.IsOpen;
            bool hasBeamStartAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamS && beamS.IsStart;
            bool hasBeamEndAfter = i + 1 < musicNodes.Count && musicNodes[i + 1] is BeamMarkerSyntax beamE && !beamE.IsStart;
            ProcessMusicNode(node, builder, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
        }
    }

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

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false)
    {
        switch (node)
        {
            case GraceExpressionSyntax grace:
                // Store grace expression to attach to the next note
                _pendingGrace = grace;
                break;

            case ParallelExpressionSyntax parallel:
                {
                    // << \\ >> span. Voice 0 joins the primary stream (this
                    // builder) so measure indices stay continuous; the extra
                    // voices are reconstructed later from the recorded span.
                    var voiceBlocks = parallel.Voices.ToList();
                    _parallelSpans.Add((parallel, builder.CurrentMeasureIndex));
                    if (voiceBlocks.Count > 0)
                        ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder);
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
                    bool isCue = HasCueAnnotation(note);
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
                            drumNote.Position, _currentStaffIndex));
                    CollectDynamics(drumNote, drumMeasureIndex, drumItemIndex);
                    CollectArticulations(drumNote, drumMeasureIndex, drumItemIndex, drumItem.StemUp,
                        null, drumAnchorTiming);
                }
                break;

            case RestSyntax rest:
                {
                    var restItem = CreateRestItem(rest);
                    int restMeasureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int restItemIndex = builder.CurrentItemCount;
                    Fraction restAnchorTiming = builder.CurrentDuration;
                    int count = rest.MeasureCount;
                    if (count <= 1)
                    {
                        builder.AddItem(restItem);
                        // Post-events on the rest (r4@fermata, r2@coda, ...).
                        // Rests have no stem; stemUp=false makes the default
                        // direction UP, matching scripts over rests.
                        CollectArticulations(rest, restMeasureIndex, restItemIndex, stemUp: false, anchorTiming: restAnchorTiming);
                    }
                    else
                    {
                        // LILYPOND-REF: lily/lily-parser.yy — R<dur>*N expands to N
                        // consecutive measure-rests semantically. The MeasureBuilder
                        // auto-completes each measure when its duration reaches the
                        // time signature.
                        for (int i = 0; i < count; i++)
                            builder.AddItem(restItem);
                    }
                }
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _metadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction chordAnchorTiming = builder.CurrentDuration;
                    // Process grace notes BEFORE the main chord so they get correct octave context
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool hasArpeggio = HasArpeggioArticulation(chord);
                    bool isCue = HasCueAnnotation(chord);
                    var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
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
                    // Collect arpeggio if present
                    // @arpeggio(bracket) = non-arpeggiate (do NOT roll).
                    bool arpBracket = chord.Articulations.Any(art =>
                        art is MusicMarkSyntax { } am
                        && am.MarkName.Equals("arpeggio.bracket", StringComparison.OrdinalIgnoreCase));
                    if ((hasArpeggio || arpBracket) && chordItem.Notes.Length > 0)
                    {
                        int minPos = chordItem.Notes.Min(n => n.StaffPosition);
                        int maxPos = chordItem.Notes.Max(n => n.StaffPosition);
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, chord.Position, _currentStaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            case BarlineSyntax barline:
                var barType = ParseBarlineType(barline.BarToken.Text);
                builder.HandleBarline(barType, barline.Position);
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

            case BreakSyntax:
                // 'break' keyword triggers line break
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

            case ClefDeclarationSyntax clefDecl:
                {
                    // Mid-measure clef change
                    // LILYPOND-REF: lily/clef-engraver.cc — inspect_clef_properties()
                    string newClef = clefDecl.ClefName.Text.ToLowerInvariant();
                    _clef = newClef;
                    _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_clef));
                    var clefChange = new ClefChangeItem(ParseClefType(newClef), clefDecl.Position);
                    builder.AddItem(clefChange);
                }
                break;

            case OctaveDirectiveSyntax octaveDir:
                // Mid-stream octave-mode switch: affects only how subsequent
                // pitches resolve '/, marks; emits no grob.
                _octave.OctaveAbsolute = octaveDir.IsAbsolute;
                break;

            case KeySignatureSyntax keySig:
                {
                    // Mid-measure key signature change
                    // LILYPOND-REF: lily/key-engraver.cc — process_music() creates KeySignature grob
                    var previousKey = new KeySignature(_keySharps, _keyCustom);
                    KeySignature newKey;
                    if (keySig.IsCustom)
                    {
                        // Mid-piece custom signature: alterations as written
                        // (transpose does not respell a custom map).
                        _keySharps = 0;
                        _keyCustom = KeySignature.EncodeCustom(keySig.CustomAlterations);
                        newKey = new KeySignature(0, _keyCustom);
                    }
                    else
                    {
                        int newSharps = _octave.TransposeKeySharps(CalculateKeySharps(keySig));
                        _keySharps = newSharps;
                        _keyCustom = null;
                        newKey = new KeySignature(newSharps);
                        // Record the modulation for Roman-numeral chord degrees at this
                        // bar onward (per-voice walk, so a SortedDictionary dedups by
                        // measure). Custom signatures carry no tonic → no degree change.
                        _keyByMeasure[builder.CurrentMeasureIndex] =
                            (Math.Max(0, LilySharp.Core.Music.KeySpelling.StepOf(keySig.Pitch.PitchName[0])), newSharps);
                    }
                    var keyChange = new KeySignatureChangeItem(newKey, previousKey, keySig.Position);
                    builder.AddItem(keyChange);
                }
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
                    if (builder.CurrentMeasureIndex == 0 && builder.CurrentDuration == Fraction.Zero)
                    {
                        _timeBeats = timeSigChange.Beats;
                        _timeBeatsText = timeSigChange.BeatsText;
                        _timeBeatType = timeSigChange.BeatType;
                        builder.SetMeasureLength(new Fraction(timeSigChange.Beats, timeSigChange.BeatType));
                    }
                    else
                    {
                        // Mid-piece change: a zero-duration grob printed at the
                        // change point, re-arming the following measures' length.
                        var newTime = new TimeSignature(timeSigChange.Beats, timeSigChange.BeatType, timeSigChange.BeatsText);
                        builder.AddItem(new TimeSignatureChangeItem(newTime, timeSigChange.Position));
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
                            builder.CurrentMeasureIndex, tempoChange.Position,
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
                            builder.CurrentMeasureIndex, tempoChange.Position,
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
                CollectOverride(overrideDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: false);
                break;

            case RevertDeclarationSyntax revertDecl:
                CollectRevert(revertDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;

            case OnceModifierSyntax onceModifier:
                if (onceModifier.Command is OverrideDeclarationSyntax innerOverride)
                    CollectOverride(innerOverride, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: true);
                else if (onceModifier.Command is RevertDeclarationSyntax innerRevert)
                    CollectRevert(innerRevert, builder.CurrentMeasureIndex, builder.CurrentItemCount);
                break;
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
            bool hasTieAfter = next is TieSyntax;
            bool hasSlurStartAfter = next is SlurSyntax { IsOpen: true };
            bool hasSlurEndAfter = next is SlurSyntax { IsOpen: false };
            bool hasBeamStartAfter = next is BeamMarkerSyntax { IsStart: true };
            bool hasBeamEndAfter = next is BeamMarkerSyntax { IsStart: false };

            if (item is NoteSyntax note)
            {
                var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
                writtenDuration += noteItem.Duration;
                builder.AddItemWithoutDuration(noteItem with { TimeScale = scale });
                lastSourcePosition = note.Position;
            }
            else if (item is RestSyntax rest)
            {
                var restItem = CreateRestItem(rest);
                writtenDuration += restItem.Duration;
                builder.AddItemWithoutDuration(restItem with { TimeScale = scale });
                lastSourcePosition = rest.Position;
            }
            else if (item is ChordSyntax chord)
            {
                var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter,
                    hasArpeggio: false, isCue: false, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter);
                writtenDuration += chordItem.Duration;
                builder.AddItemWithoutDuration(chordItem with { TimeScale = scale });
                lastSourcePosition = chord.Position;
            }
            else if (item is TupletExpressionSyntax nestedTuplet)
            {
                // LILYPOND-REF: lily/tuplet-bracket.cc - nested tuplet processing
                // Recursively process nested tuplet; its actual duration
                // counts as "written" duration for this outer tuplet
                Fraction nestedActualDuration = ProcessTuplet(nestedTuplet, builder, nestingDepth + 1, scale);
                writtenDuration += nestedActualDuration;
                lastSourcePosition = nestedTuplet.Position;
            }
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
}
