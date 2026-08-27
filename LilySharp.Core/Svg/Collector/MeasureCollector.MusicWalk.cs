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
    /// Adds a single gathered site to the flat music-site list, expanding
    /// variable references in place. Kind reads only — no red is materialized
    /// except for a reference (rare; its name/marks live on the red).
    /// </summary>
    private void GatherMusicSite(GreenSite site, List<GreenSite> musicNodes)
    {
        if (site.Kind == SyntaxKind.VariableReference)
        {
            var varRef = (VariableReferenceSyntax)site.Node;
            ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, musicNodes, varRef.DiatonicShiftSteps);
        }
        // NOTE: unlike the other walks, the per-voice path does NOT treat a
        // << \\ >> span as one wrapper. Its caller does not skip parallel
        // descendants, so the inner notes are collected (flattened) here — the
        // established multi-voice rendering behavior. A ParallelExpressionSyntax
        // node itself is therefore not added.
        else if (site.Kind != SyntaxKind.ParallelExpression && IsCollectableMusicKind(site.Kind))
            musicNodes.Add(site);
    }

    /// <summary>
    /// Processes a flat list of music sites with one-node lookahead for
    /// ties/slurs/beams (which annotate the preceding note). Every site this
    /// walk reaches is consumed (materialized); the laziness pays off in the
    /// checkpointed top-level walk (ProcessNodes), which skips an adopted
    /// prefix and a spliced tail without ever creating their reds.
    /// </summary>
    private void ProcessMusicNodeSequence(List<GreenSite> musicNodes, MeasureBuilder builder)
    {
        for (int i = 0; i < musicNodes.Count; i++)
        {
            var site = musicNodes[i];

            // Kind None belongs to the synthetic phrase markers alone (their
            // reds are preset); every real site skips both type tests on it.
            if (site.Kind == SyntaxKind.None)
            {
                // Phrase-reference boundary: evaluate the body in the default frame,
                // shifted by the reference's octave marks, and auto-transposed from the
                // score's home key to the ambient key here.
                if (site.Node is RelativeResetMarker reset)
                {
                    EnterDefaultFrame(reset.OctaveOffset);
                    EnterPhraseTranspose(reset.DiatonicSteps, reset.AnchorStep, reset.OctaveOffset);
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
                if (site.Node is PhraseEndMarker)
                {
                    ExitPhraseTranspose();
                    builder.ResetMeasureBoundary(retargetableClose: true);
                    continue;
                }
            }

            ProcessMusicNode(site.Node, builder, PeekMarkers(musicNodes, i, out _));
        }
    }

    /// <summary>
    /// True when list entry <paramref name="j"/> is a NOTE-ATTACHED mark the
    /// lookahead must skip. The flattened walk lists a note's own
    /// <c>@name(...)</c> mark (a MusicMarkSyntax child in its articulations)
    /// right after the note — it must stay in the list, because for a rehearsal
    /// mark the statement arm is the live collection path — but the naive
    /// <c>[i + 1]</c> lookahead read that mark instead of the tie/slur/beam
    /// marker written after the note: <c>c'8@text("x")[</c> silently lost its
    /// manual beam to the autobeamer (LP regression beaming.ly, the beam over
    /// the bar line), and a tie after such a mark died the same way.
    /// </summary>
    /// <remarks>
    /// The kind gate keeps the scan red-free until it lands: only a MusicMark
    /// can be note-attached, and everything else is an answer for the caller.
    /// The peek only runs for sites the walk is about to process (live window),
    /// so it materializes at most the marker run it reads plus any attached
    /// marks it skips — never the adopted prefix or the spliced tail.
    /// </remarks>
    private static bool IsAttachedMark(List<GreenSite> nodes, int j)
        => nodes[j].Kind == SyntaxKind.MusicMark
            && nodes[j].Node is MusicMarkSyntax mark
            && (mark.IsInside<NoteSyntax>() || mark.IsInside<ChordSyntax>()
                || mark.IsInside<DrumNoteSyntax>() || mark.IsInside<RestSyntax>()
                || mark.IsInside<ChordRepetitionSyntax>()
                || mark.IsInside<SlashNoteSyntax>() || mark.IsInside<BareDurationSyntax>());

    /// <summary>
    /// Lookahead flags. Ties/slurs/beams are written AFTER the note they
    /// annotate, so a note's flags come from the RUN of marker nodes that
    /// follows it — every consecutive marker, not just the first one.
    /// </summary>
    private readonly record struct MarkerFlags(
        bool HasTieAfter, bool HasSlurStartAfter, bool HasSlurEndAfter,
        bool HasBeamStartAfter, bool HasBeamEndAfter);

    /// <summary>True for the marker nodes that annotate the preceding note and
    /// are otherwise skipped by the walk ("already processed"), i.e. exactly the
    /// nodes <see cref="FoldMarker"/> reads.</summary>
    private static bool IsMarkerNode(SyntaxNode node)
        => node is TieSyntax or SlurSyntax or BeamMarkerSyntax;

    /// <summary>Adds one marker node to the flags.</summary>
    private static MarkerFlags FoldMarker(MarkerFlags m, SyntaxNode node) => node switch
    {
        TieSyntax => m with { HasTieAfter = true },
        SlurSyntax { IsOpen: true } => m with { HasSlurStartAfter = true },
        SlurSyntax { IsOpen: false } => m with { HasSlurEndAfter = true },
        BeamMarkerSyntax { IsStart: true } => m with { HasBeamStartAfter = true },
        BeamMarkerSyntax { IsStart: false } => m with { HasBeamEndAfter = true },
        _ => m,
    };

    /// <summary>
    /// Computes the tie/slur/beam lookahead for the note at <paramref name="i"/>
    /// from the run of marker nodes that follows it, skipping note-attached
    /// marks the same way (<see cref="IsAttachedMark"/>). Centralized
    /// so the top-level stream, tuplet bodies, and the structure walk can't
    /// drift — a drifted copy previously silently dropped markers inside
    /// tuplet/structure bodies.
    /// </summary>
    /// <remarks>
    /// A run, not one node: LilyPond's post-events are an unordered list and all
    /// of them bind to the note before them (lily/parser.yy post_events; the
    /// parser preserves their source order as sequence items). The one-node peek
    /// read only the first — <c>c8[( c)]</c> lost the <c>(</c> behind the
    /// <c>[</c> (a bogus LYS4010 and no slur) AND the <c>]</c> behind the
    /// <c>)</c> (the manual beam never closed). Reported 2026-08-13
    /// (scratch/ベースタブLy/beam-slur.lys).
    /// <para>
    /// <paramref name="furthestRead"/> is the last node the scan examined (the
    /// terminator, or the last marker when the list ends) — the top-level walk
    /// folds its extent into the checkpoint read watermark; spans are ordered,
    /// so folding the furthest covers the whole run.
    /// </para>
    /// </remarks>
    private static MarkerFlags PeekMarkers(
        List<GreenSite> nodes, int i, out SyntaxNode? furthestRead)
    {
        var flags = default(MarkerFlags);
        furthestRead = null;
        for (int j = i + 1; j < nodes.Count; j++)
        {
            if (IsAttachedMark(nodes, j))
                continue;
            var node = nodes[j].Node;
            furthestRead = node;
            if (!IsMarkerNode(node))
                break;
            flags = FoldMarker(flags, node);
        }
        return flags;
    }

    /// <summary>Resets the relative-octave and default-duration state to the
    /// initial frame — the invariant applied at every phrase-reference
    /// (<see cref="RelativeResetMarker"/>) boundary. <paramref name="octaveOffset"/>
    /// carries the reference's trailing marks (<c>Chorus'</c> / <c>Chorus,</c>),
    /// shifting the fresh frame so the movable phrase lands an octave up or down.
    /// This moves the RELATIVE frame only. Absolute pitches ignore the running frame,
    /// so their half of the same shift lands on <c>OctaveBase</c> — the anchor a bare
    /// <c>c</c> resolves against — applied by the paired
    /// <c>EnterPhraseTranspose</c>, which owns the save/restore.
    /// ⚠️ Until 2026-08-16 the absolute half did not exist, and this remark declared its
    /// absence deliberate while GRAMMAR.md's PhraseRef called the same silence a defect.
    /// Nothing observed the disagreement: no book in the tree writes the spelling, and
    /// only the LilyPond twin exporter said anything (it warned "the body is exported
    /// UNSHIFTED").</summary>
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

            char? letter = RelativeOctave.FirstPitchLetter(member);
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
                    _cursor.StaffIndex, _cursor.VoiceIndex));
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
            rp = CalculateStaffPosition(pitch, octaveShift);
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
    /// ★ MEASURED against LilyPond 2.26.0 (audit/lpreg/ecslur-{a,b,c}.ly): the slur of
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
        NoteSyntax or RestSyntax or ChordRepetitionSyntax or ArpeggioSyntax
            or SlashNoteSyntax or BareDurationSyntax => true,
        _ => false,
    };

    private void ProcessMusicNode(SyntaxNode node, MeasureBuilder builder, bool hasTieAfter = false, bool hasSlurStartAfter = false, bool hasSlurEndAfter = false, bool hasBeamStartAfter = false, bool hasBeamEndAfter = false)
    {
        // Record mode (CollectWalkProbe): every processed node at ANY depth funnels
        // through here — including phrase/variable bodies inlined from elsewhere in
        // the file, whose spans are NOT inside the flat-list caller's span. Folding
        // at this one funnel is what makes WalkCheckpoint.MaxSourceRead the walk's
        // true read extent. One null-check per node when a probe records; nothing
        // when off (production CLI path).
        if (_probeRecording != null)
            _walkMaxSourceRead = Math.Max(_walkMaxSourceRead, node.FullSpan.End);

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
                    _parallelSpans.Add(
                        (parallel, builder.CurrentMeasureIndex, builder.CurrentDuration, spanFrame));
                    if (voiceBlocks.Count > 0)
                    {
                        // Voice 0 is render voice 1: an override in its block scopes to it.
                        _cursor.VoiceScope = 1;
                        ProcessMusicNodeSequence(GatherVoiceMusicNodes(voiceBlocks[0]), builder);
                        _cursor.VoiceScope = null;
                        _octave.Restore(spanFrame);
                    }
                }
                break;

            case NoteSyntax note:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
                    // `a4@rest` is a REST written at a pitch — LilyPond's `a4\rest`.
                    // It leaves the walk here rather than further down because it is
                    // not a note at all: nothing note-shaped (dynamics, figured bass,
                    // chord names, cross-staff) attaches to it, and the accidental,
                    // MIDI and ledger work in CreateNoteItem would all be wrong for it.
                    // The slur flags DO come along — a rest is a legal slur bound
                    // (the RestSyntax arm below says why).
                    // LILYPOND-REF: lily/rest-engraver.cc:62-80 process_music.
                    // The spelling is read from one house (Semantics.PitchedRest), because
                    // the MIDI and MusicXML exporters walk this same syntax and had no
                    // reader for it at all — they emitted a sounding note here.
                    if (Semantics.PitchedRest.Is(note))
                    {
                        int prMeasureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
                        int prItemIndex = builder.CurrentItemCount;
                        Fraction prAnchorTiming = builder.CurrentDuration;
                        var pitchedRest = CreatePitchedRestItem(note);
                        builder.AddItem(pitchedRest with
                        {
                            HasSlurStart = hasSlurStartAfter,
                            HasSlurEnd = hasSlurEndAfter,
                        });
                        // Post-events ride a pitched rest exactly as they ride `r4`.
                        CollectArticulations(note, prMeasureIndex, prItemIndex,
                            stemUp: false, anchorTiming: prAnchorTiming);
                        break;
                    }

                    bool hasGliss = HasGlissandoArticulation(note);
                    int featherDir = GetFeatherDirection(note);
                    bool isCue = _cueDepth > 0;
                    // Pre-scan for @courtesy annotation before creating note
                    if (HasCourtesyAnnotation(note))
                        _courtesySourcePositions.Add(note.Position);
                    var noteItem = CreateNoteItem(note, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, hasGliss, featherDir, isCue);
                    if (isCue && TakeCueRegionStart())
                        noteItem = noteItem with { BeginsCueRegion = true };
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
                    int drumMeasureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
                            drumNote.Position, _cursor.StaffIndex)
                        { VoiceIndex = _cursor.VoiceIndex });
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
                    int restMeasureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
                        // written event) — measured on 2.26.0, audit/lpreg/pcmsh-r1.log.
                        // The interior copies are all identical, so clone ONCE and reuse:
                        // cloning inside the loop would allocate N-1 records per written
                        // rest (99 of them for `R1*100`) to no purpose.
                        var interior = restItem with { OpensWrittenRun = false };
                        builder.AddItem(restItem);
                        // Each interior copy is a site like any other: `R1*2000000000`
                        // parses (int.TryParse, no clamp) and used to emit that many
                        // records — the expansion budget truncates it instead.
                        for (int i = 1; i < count; i++)
                        {
                            if (!ChargeExpansion(1, rest.Position))
                                break;
                            builder.AddItem(interior);
                        }
                    }
                }
                break;

            case ChordSyntax chord:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
                        && Semantics.AnnotationValues.IsArpeggioBracket(am));
                    bool isCue = _cueDepth > 0;
                    var chordItem = CreateChordItem(chord, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                    if (isCue && TakeCueRegionStart())
                        chordItem = chordItem with { BeginsCueRegion = true };
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
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, chord.Position, _cursor.StaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            // `q` — the previous chord again, with its own duration/post-events.
            // Mirrors the ChordSyntax case; the octave frame is NOT touched (LP
            // expands q after \relative, so a q is transparent to the frame).
            case ChordRepetitionSyntax rep:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
                        && Semantics.AnnotationValues.IsArpeggioBracket(am));
                    bool isCue = _cueDepth > 0;
                    var repItem = CreateChordRepetitionItem(rep, hasBeamStartAfter, hasBeamEndAfter, hasArpeggio, isCue, hasTieAfter: hasTieAfter, hasSlurStartAfter: hasSlurStartAfter, hasSlurEndAfter: hasSlurEndAfter);
                    if (repItem is not ChordItem chordCopy)
                    {
                        // Bad chord repetition: a spacer keeps the time; the
                        // validator reports it (nothing is silent).
                        builder.AddItem(repItem);
                        break;
                    }
                    if (isCue && TakeCueRegionStart())
                        chordCopy = chordCopy with { BeginsCueRegion = true };
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
                        _arpeggios.Add(new ArpeggioItem(measureIndex, itemIndex, minPos, maxPos, rep.Position, _cursor.StaffIndex,
                            Bracket: arpBracket));
                    }
                }
                break;

            // `/` — a slash note: mirrors the NoteSyntax case minus everything
            // that needs a pitch (accidentals, courtesy/editorial, fingering,
            // ledger). Post-events, dynamics and chord names ride it like any
            // note.
            case SlashNoteSyntax slash:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction slashAnchorTiming = builder.CurrentDuration;
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool isCue = _cueDepth > 0;
                    var slashItem = CreateSlashNoteItem(slash, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, isCue);
                    if (isCue && TakeCueRegionStart())
                        slashItem = slashItem with { BeginsCueRegion = true };
                    if (_tremoloPairShape is { } tps)
                    {
                        slashItem = slashItem with
                        {
                            TimeScale = slashItem.TimeScale * new Fraction(1, 2),
                            TremoloPairBeams = tps.Beams,
                            TremoloGapCount = tps.GapCount,
                            HasBeamStart = _tremoloPairFirst,
                            HasBeamEnd = !_tremoloPairFirst,
                        };
                        _tremoloPairFirst = false;
                    }
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty)
                    {
                        slashItem = slashItem with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(slashItem);
                    CollectDynamics(slash, measureIndex, itemIndex);
                    CollectArticulations(slash, measureIndex, itemIndex, slashItem.StemUp, anchorTiming: slashAnchorTiming);
                    CollectFiguredBass(slash, measureIndex, itemIndex);
                    CollectChordNames(slash, measureIndex, itemIndex);
                    CollectCrossStaff(slash, measureIndex, itemIndex);
                }
                break;

            // A bare duration — the previous note/chord/slash again at the new
            // length. Mirrors the `q` case; the octave frame is NOT touched (the
            // copy carries the original's absolute spelling).
            case BareDurationSyntax bare:
                {
                    int measureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
                    int itemIndex = builder.CurrentItemCount;
                    Fraction bareAnchorTiming = builder.CurrentDuration;
                    if (_pendingGrace != null)
                    {
                        CollectGraceNotes(_pendingGrace, measureIndex, itemIndex);
                        _pendingGrace = null;
                    }
                    bool isCue = _cueDepth > 0;
                    var bareItem = CreateBareDurationItem(bare, hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter, isCue);
                    if (isCue && TakeCueRegionStart() && bareItem is NoteItem or ChordItem)
                        bareItem = bareItem switch
                        {
                            NoteItem n => n with { BeginsCueRegion = true },
                            ChordItem c => c with { BeginsCueRegion = true },
                            _ => bareItem,
                        };
                    if (_tremoloPairShape is { } tpb)
                    {
                        bareItem = bareItem switch
                        {
                            NoteItem n => n with
                            {
                                TimeScale = n.TimeScale * new Fraction(1, 2),
                                TremoloPairBeams = tpb.Beams,
                                TremoloGapCount = tpb.GapCount,
                                HasBeamStart = _tremoloPairFirst,
                                HasBeamEnd = !_tremoloPairFirst,
                            },
                            ChordItem c => c with
                            {
                                TimeScale = c.TimeScale * new Fraction(1, 2),
                                TremoloPairBeams = tpb.Beams,
                                TremoloGapCount = tpb.GapCount,
                                HasBeamStart = _tremoloPairFirst,
                                HasBeamEnd = !_tremoloPairFirst,
                            },
                            _ => bareItem,
                        };
                        if (bareItem is NoteItem or ChordItem)
                            _tremoloPairFirst = false;
                    }
                    if (!_pendingLeadingGrace.IsDefaultOrEmpty && bareItem is NoteItem bn)
                    {
                        bareItem = bn with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    else if (!_pendingLeadingGrace.IsDefaultOrEmpty && bareItem is ChordItem bc)
                    {
                        bareItem = bc with { LeadingGrace = _pendingLeadingGrace };
                        _pendingLeadingGrace = ImmutableArray<GraceNoteInfo>.Empty;
                    }
                    builder.AddItem(bareItem);
                    bool bareStemUp = bareItem switch
                    {
                        NoteItem n => n.StemUp,
                        ChordItem c => c.StemUp,
                        _ => false,
                    };
                    CollectDynamics(bare, measureIndex, itemIndex);
                    CollectArticulations(bare, measureIndex, itemIndex, bareStemUp, anchorTiming: bareAnchorTiming);
                    CollectFiguredBass(bare, measureIndex, itemIndex);
                    CollectChordNames(bare, measureIndex, itemIndex);
                    CollectCrossStaff(bare, measureIndex, itemIndex);
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

                    var innerNodes = new List<GreenSite>();
                    foreach (var item in volta.Items)
                        GatherMusicSite(new GreenSite(item), innerNodes);
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
                    // A note-attached compound mark (e.g. b@ottava(bassa)) is also
                    // surfaced here as a statement node; CollectArticulations already
                    // created it anchored to its host note. Skip this un-anchored
                    // duplicate so the mark stays at its note rather than snapping to
                    // the bar.
                    if (MusicMarkExistsAt(mark.Position))
                        break;
                    // The rehearsal LABEL comes from the argument (@mark("A")), the other
                    // marks from their NAME (@segno, @ottava.bassa) — two questions, and
                    // each is asked of the thing that answers it.
                    // WHICH measure the mark belongs to is a different question from
                    // where the builder stands. A mark written ON a note (c1@mark("A"))
                    // reaches this node only after that note has been added, so a note
                    // that fills its bar has already carried the builder across the
                    // barline: the mark was drawn one measure late, and one written on
                    // the last note was dropped for a measure that never came.
                    // CollectArticulations recorded the host note's measure for exactly
                    // this. ⚠️ There is no such thing as a mark standing on its own here:
                    // a bare '@mark("A")' between notes does not parse (LYS0030 — '@'
                    // modifies a note), and even 'c1 @mark("A") g1' binds to the c1 across
                    // the whitespace. The fallback is for a mark that never rode a note's
                    // articulation list at all, where the builder's position is the only
                    // answer there is.
                    int markMeasure = _markHostMeasure.TryGetValue(mark.Position, out int hostMeasure)
                        ? hostMeasure
                        : builder.CurrentMeasureIndex;
                    if (Semantics.AnnotationValues.Rehearsal(mark, out _) is { } label)
                    {
                        _musicMarks.Add(new MusicMarkItem(MusicMarkType.Rehearsal, label, markMeasure, mark.Position));
                    }
                    else if (MusicMarkItem.ParseMarkName(mark.MarkName) is { } markType)
                    {
                        _musicMarks.Add(new MusicMarkItem(markType, markMeasure, mark.Position));
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
                CollectOverride(overrideDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: false, staffIndex: _cursor.StaffIndex);
                // Track it so the next section boundary reverts it to the part default.
                _sectionActiveGrobProps.Add((overrideDecl.GrobName.Text, overrideDecl.PropertyName.Text));
                break;

            case RevertDeclarationSyntax revertDecl:
                CollectRevert(revertDecl, builder.CurrentMeasureIndex, builder.CurrentItemCount, staffIndex: _cursor.StaffIndex);
                _sectionActiveGrobProps.Remove((revertDecl.GrobName.Text, revertDecl.PropertyName.Text));
                break;

            case OnceModifierSyntax onceModifier:
                if (onceModifier.Command is OverrideDeclarationSyntax innerOverride)
                    CollectOverride(innerOverride, builder.CurrentMeasureIndex, builder.CurrentItemCount, isOnce: true, staffIndex: _cursor.StaffIndex);
                else if (onceModifier.Command is RevertDeclarationSyntax innerRevert)
                    CollectRevert(innerRevert, builder.CurrentMeasureIndex, builder.CurrentItemCount, staffIndex: _cursor.StaffIndex);
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
        // This region's first note or chord carries the edge stamp (NoteItem.BeginsCueRegion),
        // which is what tells THIS region from the one that may sit right next to it.
        _cueRegionPending = true;
        // The body items are reds already (a cue region is always live).
        var cueSites = new List<GreenSite>();
        foreach (var item in cue.Body.Items)
            cueSites.Add(new GreenSite(item));
        ProcessMusicNodeSequence(cueSites, builder);
        // Cleared here too, for the region that emits no note or chord at all (`cue { r4 }`):
        // the stamp belongs to this region, not to the next item that happens to be cued.
        _cueRegionPending = false;
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

            // Lookahead over the RUN of tie/slur/beam markers that annotate the
            // preceding note — the same rule ProcessMusicNodeSequence applies to
            // the top-level stream. Without this, a tie/slur/beam written inside
            // a tuplet body was silently dropped.
            var flags = default(MarkerFlags);
            for (int k = j + 1; k < tupletItems.Count && IsMarkerNode(tupletItems[k]); k++)
                flags = FoldMarker(flags, tupletItems[k]);
            var (hasTieAfter, hasSlurStartAfter, hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter) = flags;

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
                _cursor.StaffIndex,
                _cursor.VoiceIndex
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

        int annMeasureIndex = builder.CurrentMeasureIndex + _cursor.MetadataMeasureOffset;
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
            case SlashNoteSyntax slash:
            {
                var slashItem = CreateSlashNoteItem(slash, hasTieAfter, hasSlurStartAfter,
                    hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
                builder.AddItemWithoutDuration(slashItem with { TimeScale = scale });
                CollectDynamics(slash, annMeasureIndex, annItemIndex);
                CollectArticulations(slash, annMeasureIndex, annItemIndex, slashItem.StemUp, anchorTiming: annAnchor);
                CollectChordNames(slash, annMeasureIndex, annItemIndex);
                return slashItem.Duration;
            }
            case BareDurationSyntax bare:
            {
                // Same late-expansion rule as `q`: the tuplet scale applies to
                // the copy, whatever it resolved to.
                var bareItem = CreateBareDurationItem(bare, hasTieAfter, hasSlurStartAfter,
                    hasSlurEndAfter, hasBeamStartAfter, hasBeamEndAfter);
                switch (bareItem)
                {
                    case NoteItem n:
                        builder.AddItemWithoutDuration(n with { TimeScale = scale });
                        CollectDynamics(bare, annMeasureIndex, annItemIndex);
                        CollectArticulations(bare, annMeasureIndex, annItemIndex, n.StemUp, anchorTiming: annAnchor);
                        CollectChordNames(bare, annMeasureIndex, annItemIndex);
                        return n.Duration;
                    case ChordItem c:
                        builder.AddItemWithoutDuration(c with { TimeScale = scale });
                        CollectDynamics(bare, annMeasureIndex, annItemIndex);
                        CollectArticulations(bare, annMeasureIndex, annItemIndex, c.StemUp, anchorTiming: annAnchor);
                        CollectChordNames(bare, annMeasureIndex, annItemIndex);
                        return c.Duration;
                    default:
                        var bareSpacer = (RestItem)bareItem;
                        builder.AddItemWithoutDuration(bareSpacer with { TimeScale = scale });
                        return bareSpacer.Duration;
                }
            }
        }
        return Fraction.Zero;
    }
}
