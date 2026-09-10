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
using InternalSyntax = LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Svg.Collector;

// Structure expansion for MeasureCollector: unfolding structure/section/repeat
// blocks and music containers into the flat music-node stream, expanding phrase
// variable references, and the synthetic-barline helper. Split out of
// MeasureCollector.cs as a partial class; same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    private void ProcessRepeatBlock(FormRepeatBlockSyntax repeat, Action<List<GreenSite>> processNodes, MeasureBuilder builder)
    {
        // Checkpoint/resume (finding 3-4): a repeat block's bookkeeping (the volta
        // start/end pairing below, the synthesized barlines between sections) lives
        // in locals no checkpoint captures, so no boundary INSIDE the block is a
        // resume point — the depth counter suppresses capture and splice there
        // (TryCaptureWalkCheckpoint / the splice-attempt gate). Boundaries before
        // and after the block resume normally: everything the block emitted is in
        // the watermarked tables and measures. This replaces the old wholesale
        // "form-repeat-block" walk ineligibility, which zeroed reuse for every
        // band book with a `|: … :|` in its form.
        _formRepeatDepth++;
        try
        {
            ProcessRepeatBlockCore(repeat, processNodes, builder);
        }
        finally
        {
            _formRepeatDepth--;
        }
    }

    private void ProcessRepeatBlockCore(FormRepeatBlockSyntax repeat, Action<List<GreenSite>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        var pendingVoltaBrackets = new List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)>();

        // A form barline CONFIRMS the boundary it lands on; it is never the second of a
        // written pair, because the author did not write two barlines next to each other —
        // one is the section's own close and this one is the structure's. See
        // MeasureBuilder.ArmBoundaryForStructuralBarline for what it cost to learn that.
        void PushFormBarline(string barText, int position)
        {
            builder.ArmBoundaryForStructuralBarline();
            processNodes([new GreenSite(CreateBarlineSyntax(barText, position))]);
        }

        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);

            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    PushFormBarline(token.Text, token.SourceStart);
                    afterRepeatStart = true;
                }
                else if (token.Text == ":|")
                {
                    PushFormBarline(token.Text, token.SourceStart);
                }
                else if (token.Text == ":|:")
                {
                    // Back-to-back repeat divider: close the current repeat and open
                    // the next. The adjacent ':|' + '|:' fuse into the RepeatBoth
                    // glyph at render time, and the following section is still marked
                    // as a repeat (StartBarline = RepeatStart) — exactly ':| |:'.
                    PushFormBarline(":|", token.SourceStart);
                    PushFormBarline("|:", token.SourceStart);
                    afterRepeatStart = true;
                }
            }
            else if (afterRepeatStart)
            {
                // Resume gates (finding 3-4), mirroring ProcessForm's top-level arms:
                // during the pre-restore skip (and after a splice) the block's
                // bookkeeping must not run — RecordSectionStart / the labels would
                // read a pre-restore (or post-splice) builder, and the volta pairing
                // below would flush garbage indices into the cumulative table on top
                // of the adopted entries. ProcessSection is still entered: its own
                // visit gate does the skipping.
                bool live = _resumePending == null && !_suffixSpliced;
                if (child is BreakSyntax brk)
                {
                    // A break directive inside the repeat flags the section just played
                    // (MeasureBuilder.ApplyBreak — the same dispatch as outside the block).
                    // Resume: the flag is baked into the adopted measures (both sides).
                    if (live)
                        builder.ApplyBreak(brk.Directive, brk.SourceStart);
                }
                else if (child is SectionReferenceSyntax reference)
                {
                    if (_sectionState.Sections.TryGetValue(reference.SectionName, out var section))
                    {
                        if (live)
                        {
                            RecordSectionStart(reference.SectionName, builder.CurrentMeasureIndex);
                            builder.SectionLabel = LabelForReference(reference);
                            builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        }
                        ProcessSection(section, processNodes, builder, reference.OctaveOffset);
                    }
                }
                else if (child is { Kind: SyntaxKind.SilentSectionReference } silent
                         && silent.GetChild(1) is SyntaxTokenNode silentName
                         && _sectionState.Sections.TryGetValue(silentName.Text, out var silentSection))
                {
                    // ~Name inside a repeat: render the section's music but show NO
                    // label. The top-level silent-reference case skips in-repeat nodes
                    // (IsInsideRepeatBlock), so without this the section's measures
                    // were dropped entirely, not just its label.
                    if (live)
                    {
                        RecordSectionStart(silentName.Text, builder.CurrentMeasureIndex);
                        builder.SectionLabel = LabelForSilentReference(silent, silentName.Text);
                        builder.SectionLabelPosition = SectionDeclPos(silentName.Text);
                    }
                    ProcessSection(silentSection, processNodes, builder,
                        SyntaxFacts.NetOctaveMarks(silent));
                }
                else if (child is FormAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sectionState.Sections.TryGetValue(altSectionName, out var section))
                    {
                        // Track measure index before processing this alternative
                        int startMeasureIndex = builder.CurrentMeasureIndex;
                        if (live)
                            RecordSectionStart(altSectionName, startMeasureIndex);

                        // `~` BINDS TO THE SECTION NAME, NOT TO THE ENDING — the grammar
                        // spells the ending `'[' Integer '.' ['~'] Identifier [']']`, so the
                        // tilde is the same one the plain `~Name` item carries and it hides
                        // the same thing: the section LABEL. The bracket, its number and its
                        // caps are the ending's own and are not the tilde's to take.
                        // ⚠️ UNTIL 2026-08-25 THIS ARM APPLIED IT TO THE OTHER LINE, and both
                        // halves were wrong at once: the label was written unconditionally
                        // here while the bracket was gated on IsSilent below, so
                        // `|: [1. ~B :|` printed B's label and drew no ending at all —
                        // exactly inverted (user report, scratch/ベースタブLy/
                        // repeat-disappear.lys). The sibling arm for an ending OUTSIDE a
                        // repeat (MeasureCollector.cs, the `!IsInsideRepeatBlock` case) has
                        // always read it this way and FormVoltaWithoutRepeatTests pins it;
                        // so do the two resume arms. This was the ONE page reader of four
                        // that had not been taught.
                        if (live)
                        {
                            builder.SectionLabel = LabelForEnding(alt);
                            builder.SectionLabelPosition = SectionDeclPos(altSectionName);
                        }
                        // An ending IS a section reference with a bracket around it, marks
                        // included: `[1. B']` opens B an octave up for that ending only.
                        ProcessSection(section, processNodes, builder, alt.OctaveOffset);

                        // Track measure index after processing
                        int endMeasureIndex = builder.CurrentMeasureIndex;
                        // If we're mid-measure, include that measure
                        if (builder.CurrentItemCount > 0)
                            endMeasureIndex++;

                        // Collect volta bracket info if bracket style
                        // endMeasureIndex is exclusive (one-past-end); convert to inclusive
                        // for VoltaBracketItem which stores the last measure index
                        // ⚠️ NOT gated on IsSilent — see the label above. Writing an ending
                        // with no bracket already has a spelling, and it is the one without
                        // the `[`: `|: A :|` engraves the repeat and no volta.
                        // Resume: gated like every emission above — a skipped block's
                        // brackets are in the adopted table slice, and the frozen
                        // builder would pair (0, 0) here.
                        if (live && alt.HasBracket)
                        {
                            int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                            pendingVoltaBrackets.Add((startMeasureIndex, lastMeasure, alt.VoltaText, alt.IsClosed, alt.SourceStart));
                        }
                    }
                }
            }
        }

        // Each ending's right cap follows its source ']' (present = closed); the
        // engraver's segment splitter opens only line-break pieces of a closed one.
        foreach (var (startMeasure, endMeasure, voltaText, isClosed, sourcePosition) in pendingVoltaBrackets)
            _voltaBrackets.Add(new VoltaBracketItem(startMeasure, endMeasure, voltaText, isClosed, sourcePosition));
    }

    /// <param name="octaveOffset">The net octave shift written on the REFERENCE that
    /// opened this play (<c>~B'</c> = +1, <c>~B,</c> = -1). It belongs to the play, not to
    /// the declaration, so it is threaded from the form walk rather than read off the
    /// section — the same section referenced twice can open at two different octaves.</param>
    private void ProcessSection(SectionDeclarationSyntax section, Action<List<GreenSite>> processNodes, MeasureBuilder builder, int octaveOffset = 0)
    {
        // Checkpoint/resume gate (CollectWalkProbe): a section wholly before the
        // resume target is in the adopted prefix — prologue, music and epilogue —
        // so it is skipped whole. The TARGET section skips only its prologue (it
        // ran inside the prefix); the container walk below restores the checkpoint
        // at the target invocation and runs the tail live. Section visits and
        // per-section invocation ordinals are deterministic per document, so a
        // recording and a resume of the same document address the same boundary.
        int visit = _sectionVisit++;
        _invocationInSection = 0;
        // A spliced walk adopted every remaining section — prologue, music,
        // padding epilogue — inside the recorded tail (the end checkpoint
        // carries the section maps and metadata the prologue would write).
        if (_suffixSpliced)
            return;
        // Record mode (cross-edit resume): the prologue consumes this section's
        // NAME (label + its data-pos) and header directives (the _sectionHeader*
        // map values are sourced from these direct children). Deliberately NOT
        // folded into MaxSourceRead — declarations often sit BELOW the music they
        // reference, so a scalar max would reject every checkpoint of every
        // phrase-style book. Recorded as discrete header-read spans instead; the
        // planner validates each checkpoint's read prefix span-by-span
        // (VoiceWalkRecording.HeaderReads). ⚠️ A standalone same-named header node
        // (`section A { key g }` beside a music-bearing `section A { … }`) would be
        // a SECOND source this does not see; today Sections maps one node per name.
        if (_probeRecording != null)
        {
            _walkHeaderReads.Add(section.Name.Span);
            foreach (var child in section.ChildNodes())
                if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                    or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax
                    or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax)
                    _walkHeaderReads.Add(child.FullSpan);
        }
        if (_resumePending is { } plan)
        {
            if (visit < plan.Checkpoint!.SectionVisit) // _resumePending is only armed with a prefix target
                return;
        }
        else
        {
            ProcessSectionPrologue(section, builder, octaveOffset);
        }

        int startMeasure = builder.CurrentMeasureIndex;
        _sectionStartMeasureForResume = startMeasure;
        ProcessSectionBody(section, processNodes, builder, startMeasure);
    }

    /// <summary>The section-boundary prologue: frame/duration/meter/key/override
    /// reverts and the section's own header directives. Extracted verbatim from
    /// <see cref="ProcessSection"/> so the resume gate can skip it as one unit
    /// (it ran inside the adopted prefix when the resume target sits mid-section).</summary>
    private void ProcessSectionPrologue(SectionDeclarationSyntax section, MeasureBuilder builder, int octaveOffset = 0)
    {
        // Reset the relative frame (and revert the octave mode to the file
        // default) at each section boundary. The default DURATION resets too, so a
        // section is self-contained: an un-numbered first note starts a quarter
        // regardless of the preceding section's last duration. Without this the
        // reprise `A` after `~B` (`g'1`) inherited B's whole-note and rendered its
        // quarter-note melody as whole notes.
        //
        // The reset stays and the CARRY is given by notation (user decision,
        // 2026-08-31): the reference's own trailing marks say which octave this play
        // opens in, so `~B'` re-anchors a whole octave up without touching B or any
        // other play of it. That is the whole of what the marks do — the duration and
        // the meter/key reverts below are unaffected, and a section's first note still
        // states its own value.
        _octave.ResetForSection(octaveOffset);
        _defaultDuration = Fraction.Quarter;
        _defaultDots = 0;

        // A section is self-contained: settle the boundary so a section that OPENS with a
        // `|` closes an EMPTY measure of its own (owner's decision, 2026-08-28) rather than
        // pairing with — or anchoring against — whatever the previous section wrote.
        builder.ResetMeasureBoundary();

        // The phrase auto-transpose baseline reverts with the key: a mid-section
        // modulation must not carry into the next section (nor a reused copy).
        // Unconditional — the running tonic can differ from home even when the
        // sharp count matches (A minor → C major both have 0 sharps).
        ResetAmbientTonicToHome();

        // Time and key revert to the SCORE level too, for the same self-containment:
        // a mid-section meter/key change must not leak past the section end (nor into
        // the same section reused elsewhere by the form). A section that wants a
        // different meter/key states it at its own start, which overrides this.
        //
        // Only redraw when a prior section actually left a different meter/key — so
        // the common case (nothing changed) emits nothing, and the first section
        // (running == score level) is a no-op. The redraw makes the revert visible
        // instead of silently leaving the previous signature on the staff.
        int sectionPos = section.Name.Span.Start;

        // Grob overrides reset to the part default at each section boundary (self-
        // containment, like clef/key/time): a section-internal override does not leak into
        // the next section. For each grob property the PREVIOUS section changed in-music,
        // restore the part-default value here, or revert when the part has no default.
        // (`once` overrides auto-pop, so they are never tracked here.)
        if (_sectionActiveGrobProps.Count > 0)
        {
            foreach (var (grob, prop) in _sectionActiveGrobProps)
            {
                if (_sectionResetOverrides.TryGetValue((grob, prop), out var defaultValue))
                    _grobOverrides.Add(new GrobOverride(grob, prop, defaultValue,
                        builder.CurrentMeasureIndex, builder.CurrentItemCount, false, _cursor.StaffIndex));
                else
                    _grobReverts.Add(new GrobRevert(grob, prop,
                        builder.CurrentMeasureIndex, builder.CurrentItemCount, _cursor.StaffIndex));
            }
            _sectionActiveGrobProps.Clear();
        }

        // A section-major section can carry its OWN grob directive
        // (`section A { override … melody {…} }`): a default for THIS section on every
        // staff. Collect it here — once per voice, staff-scoped — at the section start, and
        // track it so it resets at the next boundary (and re-applies on a reprise). An
        // inline-music section walks its override as music instead, so it is excluded.
        if (!SectionHasInlineMusic(section))
        {
            // Direct children only — the old descendant walk re-read the section's whole
            // music body to apply a filter (`child.Parent == section`) that IS the
            // direct-child test, so the narrowing is an identity (session 145: that walk
            // enumerated 234k nodes per keystroke per part on perf-fingbeam1k).
            foreach (var child in section.ChildNodes())
            {
                if (child is OverrideDeclarationSyntax secOv)
                {
                    CollectOverride(secOv, builder.CurrentMeasureIndex, builder.CurrentItemCount,
                        isOnce: false, staffIndex: _cursor.StaffIndex);
                    _sectionActiveGrobProps.Add((secOv.GrobName.Text, secOv.PropertyName.Text));
                }
            }
        }

        // Clef reverts to the part default the same way: a section that opens without its
        // own `clef` uses the part clef, so a mid-section clef change in a prior section
        // (or the same section played earlier) does not leak in. A section that opens with
        // its own `clef` overrides this at the music walk. Only redraw when it actually
        // differs (first section is a no-op). Mirror the mid-music clef change so the
        // default octave for relative pitches follows the reverted clef.
        if (_meta.Clef != _sectionResetClef)
        {
            _meta.Clef = _sectionResetClef;
            _octave.CurrentOctave = InstrumentDefaults.GetDefaultOctave(ParseClefType(_meta.Clef));
            builder.AddItem(new ClefChangeItem(ParseClefType(_sectionResetClef), sectionPos));
        }

        // A section can state its own time (section-major or a standalone header): apply
        // it and re-arm the measure length; otherwise revert to the score meter.
        if (_sectionHeaderTimes.TryGetValue(section.SectionName, out var sectionTime))
        {
            // A `time none` header carries no ink and no width — TimeSignatureChangeItem.Blanked.
            builder.AddItem(new TimeSignatureChangeItem(
                new TimeSignature(sectionTime.Beats, sectionTime.BeatType, sectionTime.BeatsText,
                    sectionTime.IsSenzaMisura),
                sectionPos)
            {
                Blanked = sectionTime.IsSenzaMisura,
            });
            builder.SetMeasureLength(new Fraction(sectionTime.Beats, sectionTime.BeatType),
                sectionTime.IsSenzaMisura);
        }
        else
        {
            // No section meter: revert the running meter to the SCORE level. Compare (and
            // redraw) against the SNAPSHOT, not _meta.Time - a mid-music `time` in a prior
            // section mutates _meta (which also drives the opening signature), so _meta no
            // longer holds the score meter. Only redraw when the previous section actually
            // left a different meter on the staff. `time none` is part of the comparison: a
            // section that ended unmetered against a 4/4 score meter differs, and the 4/4
            // is redrawn — LilyPond prints a grob for every \time event (measured 2.26.0,
            // scratch/p354/lp/senza-reprint.ly: `\time 4/4 … \time 4/4` prints twice).
            var resetTime = new Fraction(_sectionResetTimeBeats, _sectionResetTimeBeatType);
            if (builder.CurrentMeasureLength != resetTime
                || builder.SenzaMisura != _sectionResetTimeSenzaMisura)
            {
                builder.AddItem(new TimeSignatureChangeItem(
                    new TimeSignature(_sectionResetTimeBeats, _sectionResetTimeBeatType,
                        _sectionResetTimeBeatsText, _sectionResetTimeSenzaMisura),
                    sectionPos)
                {
                    Blanked = _sectionResetTimeSenzaMisura,
                });
                builder.SetMeasureLength(resetTime, _sectionResetTimeSenzaMisura);
            }
        }

        // A section can state its own tempo, printed as a metronome mark at its start.
        if (_sectionHeaderTempos.TryGetValue(section.SectionName, out var sectionTempo))
        {
            // At the very first timestep the section tempo IS the piece's opening tempo,
            // so it REPLACES the score's initial metronome mark rather than stacking a
            // second one on top of it (the initial time signature collapses the same
            // way). Anywhere else it prints a metronome mark at the section start.
            // ProcessSection runs once PER PART; a section tempo is a score-level mark
            // that must engrave ONCE — without this guard a grand staff printed the
            // metronome mark twice, stacked (mirrors the navigation-mark guard).
            bool tempoAlready = _musicMarks.Any(m =>
                m.Type == MusicMarkType.Tempo
                && m.MeasureIndex == builder.CurrentMeasureIndex
                && m.SourcePosition == sectionPos);
            if (builder.AtPieceOpening)
                CollectTempo(sectionTempo);
            else if (tempoAlready)
            {
                // already emitted for an earlier staff of this section
            }
            else if (sectionTempo.Bpm is int bpm)
                _musicMarks.Add(new MusicMarkItem(
                    MusicMarkType.Tempo, bpm.ToString(),
                    builder.CurrentMeasureIndex, sectionPos,
                    builder.CurrentItemCount, builder.CurrentDuration)
                {
                    TempoText = sectionTempo.Marking,
                    TempoBeatUnit = sectionTempo.BeatUnit ?? 4,
                    TempoDots = sectionTempo.BeatDots,
                    SwingSubdivision = sectionTempo.SwingSubdivision,
                });
            else if (sectionTempo.Marking is { } marking)
                _musicMarks.Add(new MusicMarkItem(
                    MusicMarkType.Tempo, "",
                    builder.CurrentMeasureIndex, sectionPos,
                    builder.CurrentItemCount, builder.CurrentDuration) { TempoText = marking });
        }

        // A section's own starting key sits beside the part blocks (section-major) or in
        // a standalone part-major header (`section A { key g major }`) — either way it is
        // NOT reached by the per-part music walk. Apply it here (transposed per voice,
        // printed on every staff); it overrides the score-level revert below. Keyed by
        // section NAME so a standalone header applies whichever node represents the
        // section. An inline-music section walks its `key` as music, so it is not mapped.
        if (_sectionHeaderKeys.TryGetValue(section.SectionName, out var sectionKey))
        {
            ApplyKeySignatureChange(sectionKey, builder);
        }
        else if (_meta.KeySharps != _sectionResetKeySharps || _meta.KeyCustom != _sectionResetKeyCustom)
        {
            var previousKey = new KeySignature(_meta.KeySharps, _meta.KeyCustom);
            _meta.KeySharps = _sectionResetKeySharps;
            _meta.KeyCustom = _sectionResetKeyCustom;
            builder.AddItem(new KeySignatureChangeItem(
                new KeySignature(_meta.KeySharps, _meta.KeyCustom), previousKey, sectionPos));
        }

        // A section can begin with a pickup (`section A { partial 4  melody { … } }`):
        // shorten its first measure, per part. Applied after any section meter so the
        // pickup restores to the section's own time when it closes.
        if (_sectionHeaderPartials.TryGetValue(section.SectionName, out var sectionPartial))
            builder.SetPartial(sectionPartial.ToFraction());

    }

    /// <summary>The section's container walk and padding epilogue — the part of
    /// <see cref="ProcessSection"/> after the prologue (see the resume gate there).</summary>
    private void ProcessSectionBody(SectionDeclarationSyntax section,
        Action<List<GreenSite>> processNodes, MeasureBuilder builder, int startMeasure)
    {
        bool matched = false;
        // Direct children only: a PartBlockSyntax is produced exclusively by
        // ParseSectionItem (Parser.Sections.cs — the Identifier and clef-keyword arms),
        // so every part block is a DIRECT child of its section declaration. The old
        // descendant walk visited part v's entire music body before reaching sibling
        // part w — O(section) per part, per section, per keystroke.
        foreach (var child in section.ChildNodes())
        {
            if (child is PartBlockSyntax partBlock)
            {
                if (_voiceName == null || partBlock.Name == _voiceName)
                {
                    ProcessMusicContainer(partBlock, processNodes);
                    matched = true;

                    // One part block per voice name; stop looking. (A null voice
                    // is single-staff and legitimately concatenates every block.)
                    if (_voiceName != null) break;
                }
            }
        }

        // Part-major fallback: this section's music for the current voice is not a
        // part-block here but lives inside `part <voice> { section <name> { ... } }`.
        if (!matched && _voiceName != null
            && _sectionState.PartMajorCells.TryGetValue((section.SectionName, _voiceName), out var cell))
        {
            ProcessMusicContainer(cell, processNodes);
            matched = true;
        }

        // Single-part shorthand: bare music written straight into a top-level section
        // (`part bl { clef bass } section A { c d e }`) is the lone part's music for this
        // section — no part cell wraps it. Walk the section's OWN direct music (expanding
        // phrase refs) for the current voice. (In a part-major file this loose music belongs
        // to no part and is reported by SectionMusicNeedsPartValidator; rendering it here is
        // harmless.) Only a GENUINE top-level section — a part-major section lives inside a
        // part and its inline music is that part's alone, so other voices must spacer-fill it.
        if (!matched && section.Parent is CompilationUnitSyntax && SectionHasInlineMusic(section))
        {
            // Direct children only, already materialized by GetChild — wrap preset.
            var inline = new List<GreenSite>();
            for (int i = 0; i < section.SlotCount; i++)
            {
                var child = section.GetChild(i);
                if (child is VariableReferenceSyntax varRef)
                    ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, inline);
                else if (child != null && IsCollectableMusicNode(child))
                    inline.Add(new GreenSite(child));
            }
            processNodes(inline);
        }

        // Pad this voice up to the section's canonical bar count so every staff stays
        // aligned — whether this voice does not define the section AT ALL (fill it
        // whole) or defines it with TOO FEW bars (fill only the shortfall). Without
        // this the section is short here, the staff ends up under-length, and every
        // part after it drifts out of alignment. The filler is invisible spacer rests
        // (`s`, not `R`, so they never collapse into a multi-measure rest); the
        // caller's pending SectionLabel still lands on the first filled measure, so
        // the section mark shows on this staff too. Only pad at a clean bar boundary
        // (a mid-measure section is malformed and flagged elsewhere).
        // A mid-section resume restored the builder AFTER startMeasure was read off
        // the pre-restore builder; the checkpoint carries the section's true start.
        if (_resumeRestoredSectionStart is { } resumedStart)
        {
            startMeasure = resumedStart;
            _resumeRestoredSectionStart = null;
        }

        if (_voiceName != null && builder.CurrentItemCount == 0)
        {
            // Record mode: the canonical bar count is a function of EVERY part's
            // music for this section (GetCanonicalSectionBars — keep the fold's
            // enumeration in step with it), so the whole section span and every
            // part-major cell of this section join the walk's read extent. The
            // decision "no padding needed" reads them just as much as the padding.
            if (_probeRecording != null)
            {
                _walkMaxSourceRead = Math.Max(_walkMaxSourceRead, section.FullSpan.End);
                foreach (var kv in _sectionState.PartMajorCells)
                    if (kv.Key.section == section.SectionName)
                        _walkMaxSourceRead = Math.Max(_walkMaxSourceRead, kv.Value.FullSpan.End);
                foreach (var kv in _sectionState.ChordTrackCells)
                    if (kv.Key.section == section.SectionName)
                        _walkMaxSourceRead = Math.Max(_walkMaxSourceRead, kv.Value.FullSpan.End);
            }
            int produced = builder.CurrentMeasureIndex - startMeasure;
            // A collect that overran its expansion budget is truncated music: what it drew
            // is shorter than any count of the source (`R1*1000` under a cap of 10 draws 11
            // bars), so padding it up to the count would draw the truncation as silence.
            int canonical = ExpansionBudgetExceededAt == null ? GetCanonicalSectionBars(section) : 0;
            for (int i = produced; i < canonical; i++)
                builder.AddItem(new RestItem(TimeSignatureFraction, 0, section.SourceStart) { IsSpacer = true });
        }
    }

    /// <summary>
    /// Apply a key-signature change at the builder's current position: update the running
    /// key metadata (transposed for this voice), advance the phrase auto-transpose baseline
    /// and the per-measure key map, and emit the <see cref="KeySignatureChangeItem"/>.
    /// Shared by a mid-music <c>key</c> and a section-major section's own <c>key</c>.
    /// </summary>
    private void ApplyKeySignatureChange(KeySignatureSyntax keySig, MeasureBuilder builder)
    {
        var previousKey = new KeySignature(_meta.KeySharps, _meta.KeyCustom);
        KeySignature newKey;
        if (keySig.IsCustom)
        {
            // Custom signature: alterations as written (transpose does not respell a
            // custom map). A custom key has no tonic — phrases placed here are unshifted.
            _meta.KeySharps = 0;
            _meta.KeyCustom = KeySignature.EncodeCustom(keySig.CustomAlterations);
            newKey = new KeySignature(0, _meta.KeyCustom);
            _ambientTonicValid = false;
        }
        else
        {
            int newSharps = _octave.TransposeKeySharps(CalculateKeySharps(keySig));
            _meta.KeySharps = newSharps;
            _meta.KeyCustom = null;
            newKey = new KeySignature(newSharps);
            // Advance the phrase auto-transpose baseline to this key's (written) tonic.
            _ambientTonicStep = Math.Max(0,
                LilySharp.Core.Music.KeySpelling.StepOf(keySig.Pitch.PitchName[0]));
            _ambientTonicAlter = keySig.Pitch.AccidentalOffset;
            _ambientTonicValid = true;
            // Record the modulation for Roman-numeral chord degrees at this bar onward
            // (per-voice walk, so the SortedDictionary dedups by measure).
            RecordKeyAtMeasure(builder.CurrentMeasureIndex,
                Math.Max(0, LilySharp.Core.Music.KeySpelling.StepOf(keySig.Pitch.PitchName[0])), newSharps);
        }

        // A key change at the very opening (bar 0, before any note sounds) IS the piece's
        // opening key, not a change within it — e.g. a top-level `key d major` overridden by
        // the first section's `key a major`. Fold it into the initial signature so the first
        // measure shows the FINAL key from the start, with no redundant change drawn. This
        // mirrors the opening time signature, which already collapses this way
        // (CaptureScoreContent takes _meta.TimeBeats after the section's own time). The
        // running key state above is left as set, so mid-piece changes still draw normally.
        // Recorded PER VOICE (not the shared _meta.InitialKeySharps): in a multi-staff
        // score each staff keeps its own opening key, so `part a { key c … }` and
        // `part b { key a … }` do not overwrite each other.
        if (builder.AtPieceOpening)
        {
            _openingKeyOverride = (_meta.KeySharps, _meta.KeyCustom);
            return;
        }

        // The TONIC's token, not the declaration's Position — see KeyDataPos.
        builder.AddItem(new KeySignatureChangeItem(newKey, previousKey, KeyDataPos(keySig)));
    }

    /// <summary>The first direct-child directive of type <typeparamref name="T"/> (the
    /// section's own starting key / time / tempo), or null when it states none.</summary>
    private static T? FirstDirect<T>(SectionDeclarationSyntax section) where T : SyntaxNode
    {
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is T t)
                return t;
        return null;
    }

    /// <summary>
    /// True when the section has a direct-child MUSIC node (note / phrase reference /
    /// rest / …), as opposed to only directives (<c>key</c> / <c>time</c> / …) and part /
    /// chord / lyric blocks. An inline-music section walks its own <c>key</c> as music;
    /// a section-major or directives-only header does not.
    /// </summary>
    /// <remarks>
    /// THE ONE SPELLING — the MIDI exporter, the LilyPond exporter and
    /// MeasureValidator all read this predicate (2026-08-28, review ⑫). They used to
    /// carry their own copies, and the MIDI copy drifted: it did not exclude
    /// override/revert/once, so a header carrying a page directive beside its
    /// <c>key</c> (<c>section A { key g major  override … }</c>) was classed as
    /// inline music there, the key registration was skipped, and the .mid played the
    /// phrase in the home key while the page engraved it in the section's
    /// (MidiTests.SectionHeaderWithGrobOverride_StillRegistersItsKey). A
    /// section-level override IS a directive: this collector arms it as a section
    /// default at the section start and resets it at the next boundary (see the
    /// section-start collection above) — it never makes the section inline music.
    /// </remarks>
    internal static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax)
                continue;
            if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax)
                continue; // a directive — a section-level grob override doesn't make it inline
            return true; // a music node
        }
        return false;
    }

    /// <summary>
    /// The canonical bar count of a section: the greatest bar count among every part
    /// that defines it (part-major cells across parts, or the sibling part blocks of a
    /// section-major section) AND every named chord track that writes it (a part-major
    /// chord-track cell, or a named chord block inside a section-major section). A
    /// section spans as many bars as its longest voice, so shorter parts pad up to this
    /// to stay aligned. A chord row is a voice of the section too: were it left out, a
    /// row longer than its melody would spill its trailing chords onto the next section's
    /// first bars, beside that section's own chords. The cross-part validator
    /// (<c>CrossPartMeasureValidator</c>, LYS2007) counts the same voices.
    /// </summary>
    private int GetCanonicalSectionBars(SectionDeclarationSyntax section)
    {
        // One answer per section per collect: the count is a pure function of the
        // syntax (PartMajorCells is filled once by CollectDefinitions), yet every
        // part's walk — and every reprise of the section in a form — re-counted every
        // part's bars from scratch, O(parts² × section syntax) per keystroke.
        if (_canonicalSectionBars.TryGetValue(section, out int cached))
            return cached;

        // THE ONE HOUSE (SectionBarCounts): every voice of the name — part-major cells,
        // chord-track cells, a section-major declaration's part blocks and named chord
        // blocks, the single-part shorthand — folded by name, once per collect (one green
        // walk over the section declarations). The page keeps the SYNTACTIC counter (its
        // remarks say what it undercounts and why the exporters use the semantic one).
        // Until 2026-09-10 this method folded the collector's own cell registry (parts only).
        _canonicalByName ??= SectionBarCounts.CanonicalByNameSyntactic(SectionBarCounts.RootOf(section));
        int max = _canonicalByName.TryGetValue(section.SectionName, out int bars) ? bars : 0;

        _canonicalSectionBars[section] = max;
        return max;
    }

    // section name -> canonical bar count for the whole book (SectionBarCounts.CanonicalByNameSyntactic);
    // computed on first use per collect, cleared with _canonicalSectionBars (Reset).
    private Dictionary<string, int>? _canonicalByName;

    /// <summary>
    /// Bar count of a music scope (a part block or a part-major section cell),
    /// mirroring <see cref="MeasureBuilder.HandleBarline"/>'s bare-barline rules: a
    /// barline after music closes a bar; a <c>|</c> on an empty span closes an EMPTY
    /// measure — the one that OPENS the scope included, so <c>{ | | | | }</c> is four
    /// bars (owner's decision, 2026-08-28); a TYPED barline on an empty span is a
    /// decoration. A trailing partial bar (music after the last barline) counts. (Chords/lyrics tracks keep their own slot-style counting — see
    /// <see cref="ChordNameCollector.CountBars(LilySharp.Core.Syntax.ChordPartBlockSyntax)"/>
    /// — their barlines ARE the structure.)
    /// A <c>&lt;&lt; \\ &gt;&gt;</c> polyphonic span counts as ONLY its first voice's
    /// bars: the main stream advances by that voice while the others overlay the same
    /// measures, so counting every voice's barlines would multiply the bar count.
    /// </summary>
    internal static int CountBarsInScope(SyntaxNode scope)
        => CountBarsInScope(scope, out _, phrases: null);

    /// <summary>As <see cref="CountBarsInScope(SyntaxNode)"/>, and whether the last bar is
    /// still OPEN — music after the last bar line, counted as a bar of its own; a reader
    /// that appends bar lines to pad the scope closes it with its first one.</summary>
    internal static int CountBarsInScope(SyntaxNode scope, out bool trailingOpen)
        => CountBarsInScope(scope, out trailingOpen, phrases: null);

    /// <summary>
    /// As above, with the book's PHRASE table (name → the phrase's body green, or a
    /// variable's expression green): a phrase reference counts its body's bars, in a fresh
    /// bar frame (a phrase's edge closes an open bar, as the collector's phrase boundary
    /// does). Without the table a reference counts nothing — the shape the equivalence net
    /// (green ≡ red) still runs on. <c>R1*N</c> counts N bars and a <c>repeat</c> its
    /// body's bars COUNT times either way (2026-09-10; the count used to be one token, one
    /// bar, so a section written as three phrases read as ZERO bars — measured on 97
    /// sections of the fixture net, SectionVoicePaddingExportTests'
    /// SyntacticCanonical_MatchesSemantic_OnEveryNetBook).
    /// </summary>
    internal static int CountBarsInScope(SyntaxNode scope, out bool trailingOpen, PhraseBarTable? phrases)
    {
        int bars = 0;
        bool pendingMusic = false;
        bool confirmable = false; // the scope-start boundary is CONSUMED: a `|` there is a bar
        // The walk's own budget, the collector's cap (DefaultExpansionBudgetCap): phrase
        // ENTRIES it will still follow, and the bar count's ceiling. A phrase DAG doubling
        // per level (ExpansionBudgetTests' DagBook) or `R1*2000000000` must neither hang
        // this walk nor hand the page a count to pad up to — a collect that overran its
        // budget pads nothing (ProcessSection), so the ceiling only has to keep the sum sane.
        int budget = DefaultExpansionBudgetCap;
        WalkBars(scope.Green, ref bars, ref pendingMusic, ref confirmable, phrases, activeRefs: null, ref budget);
        trailingOpen = pendingMusic;
        return Math.Min(DefaultExpansionBudgetCap, bars + (pendingMusic ? 1 : 0));
    }

    /// <summary>
    /// The phrase table the bar-counting walk reads: every <c>phrase NAME { … }</c> body and
    /// <c>NAME = …</c> expression under the root as greens (last declaration wins, the
    /// collector's <c>_variables</c> rule), plus the walk's memo of what each body counted.
    /// A body's count is a pure function of the body and the ONE bit of walk state that
    /// reaches into it (whether a leading <c>|</c> would confirm a close made just before
    /// the reference), so a phrase referenced from a hundred sections is walked once or
    /// twice per collect, not a hundred times — MEASURED before the memo (Release, 120
    /// sections × 2 parts × 3 references over 40 eight-bar phrases): +8.4% / +3.9% on the
    /// whole compile in the two orders, against a ±4% control (scratch/p363/perf-phrases.txt).
    /// </summary>
    internal sealed class PhraseBarTable
    {
        public Dictionary<string, InternalSyntax.GreenNode> Greens { get; } = new(StringComparer.Ordinal);
        /// <summary>(name, confirmable at entry) → (bars the body adds, whether it leaves a bar open).</summary>
        public Dictionary<(string Name, bool Confirmable), (int Bars, bool Pending)> Memo { get; } = new();
    }

    /// <inheritdoc cref="PhraseBarTable"/>
    internal static PhraseBarTable PhraseGreens(SyntaxNode root)
    {
        var table = new PhraseBarTable();
        foreach (var ph in root.KindSites(SyntaxKind.PhraseDeclaration).OfType<PhraseDeclarationSyntax>())
            table.Greens[ph.Name.Text] = ph.Body.Green;
        foreach (var vd in root.KindSites(SyntaxKind.VariableDeclaration).OfType<VariableDeclarationSyntax>())
            table.Greens[vd.Name.Text] = vd.Expression.Green;
        return table;
    }

    /// <summary>
    /// The bar-counting walk, on GREENS. Session 155: after the flat-list
    /// gather went lazy, this was the walk that INHERITED the whole-book red
    /// first-touch (the session-152 remark's inheritance chain, third
    /// occurrence — stack samples put ~99% of the keystroke's remaining red
    /// creation here on perf-plain1k). The count is a pure function of kinds
    /// plus the bar token's text, all green-readable; kinds are 1:1 with the
    /// red types the old spelling switched on (<c>SyntaxNode.CreateRed</c>).
    /// The net is CanonicalBarsEquivalence (green count ≡ red-spelling count
    /// on every scope of every fixture book) — the old red walk stays below
    /// as its oracle.
    /// </summary>
    private static void WalkBars(InternalSyntax.GreenNode node, ref int bars, ref bool pendingMusic, ref bool confirmable,
        PhraseBarTable? phrases, HashSet<string>? activeRefs, ref int budget)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetSlot(i);
            if (child == null || child.IsToken)
                continue; // the old walk recursed into tokens as a no-op (no slots)
            switch (child.Kind)
            {
                case SyntaxKind.Rest:
                    // `R1*N` is N bars written as one token (RestGreen's slot 3 is the count):
                    // N−1 here, the last one when the bar closes or stays pending.
                    pendingMusic = true;
                    if (child.GetSlot(3) is InternalSyntax.SyntaxToken count
                        && int.TryParse(count.Text, out int n) && n > 1)
                        bars = (int)Math.Min(DefaultExpansionBudgetCap, (long)bars + n - 1);
                    break;
                case SyntaxKind.RepeatExpression:
                    // Played length, MeasureModel's reading (its class remarks): the body's
                    // bars COUNT times for every type but tremolo, which is one metric item
                    // (the page expands an in-music `repeat volta 2` into two bars too:
                    // audit/lpreg/voltagrace-probe.lys draws 2). Without durations this walk
                    // can only multiply the body's CLOSED bars: a body with no bar line at all
                    // is music like a note (`repeat percent 2 { c16 d e f }` is a beat, not a
                    // bar — audit/lpreg/slashprobe.lys drew 4 bars for 1 when a turn counted
                    // as a bar), an open tail carries over ONCE, and music pending at the
                    // entry is absorbed by the body's first close (`grace { f8 } repeat volta
                    // 2 { b1 | }` is 2 bars, not 3). Under, never over: the page pads other
                    // voices up to this count, so an overcount pads them wrongly.
                    {
                        var type = (InternalSyntax.SyntaxToken)child.GetSlot(1)!;
                        if (type.Text == "tremolo")
                        {
                            pendingMusic = true;
                            break;
                        }
                        int turns = child.GetSlot(2) is InternalSyntax.SyntaxToken c && int.TryParse(c.Text, out int k) ? Math.Max(1, k) : 1;
                        int bodyBars = 0; bool bodyPending = false; bool bodyConfirmable = false;
                        if (child.GetSlot(3) is { } body)
                            WalkBars(body, ref bodyBars, ref bodyPending, ref bodyConfirmable, phrases, activeRefs, ref budget);
                        if (bodyBars == 0)
                        {
                            pendingMusic = true;
                            break;
                        }
                        bars = (int)Math.Min(DefaultExpansionBudgetCap, (long)bars + (long)turns * bodyBars);
                        pendingMusic = bodyPending;
                        // The alternative clause's endings are written bars of their own.
                        if (child.GetSlot(4) is { } alternative)
                            WalkBars(alternative, ref bars, ref pendingMusic, ref confirmable, phrases, activeRefs, ref budget);
                        // A closed body's exit is a bar boundary: a `|` right after it confirms
                        // the close (`repeat percent 2 { c4 e g e | } |` is two bars, not three).
                        confirmable = !pendingMusic;
                    }
                    break;
                case SyntaxKind.VariableReference:
                    // A phrase reference is its body's bars, walked where it stands (the
                    // collector expands it there, ExpandVariable). Nothing without the table;
                    // a cycle is walked once; past the budget nothing (a DAG doubling per
                    // level is 2^19 entries from twenty lines — ExpansionBudgetTests).
                    if (phrases != null
                        && child.GetSlot(0) is InternalSyntax.SyntaxToken nameTok
                        && phrases.Greens.TryGetValue(nameTok.Text, out var phraseBody)
                        && budget > 0)
                    {
                        budget--;
                        // The body's count depends on the walk's state only through
                        // `confirmable` (a leading `|` confirms or counts); pending music at
                        // the entry is closed by the body's first bar line exactly as a
                        // leading empty bar would count, so the memo is keyed on that one bit.
                        var key = (nameTok.Text, confirmable);
                        if (!phrases.Memo.TryGetValue(key, out var counted))
                        {
                            activeRefs ??= new HashSet<string>(StringComparer.Ordinal);
                            if (!activeRefs.Add(nameTok.Text))
                                break; // a cycle: walked once, nothing on re-entry
                            int bodyBars = 0; bool bodyPending = false; bool bodyConfirmable = confirmable;
                            WalkBars(phraseBody, ref bodyBars, ref bodyPending, ref bodyConfirmable, phrases, activeRefs, ref budget);
                            activeRefs.Remove(nameTok.Text);
                            counted = (bodyBars, bodyPending);
                            phrases.Memo[key] = counted;
                        }
                        if (counted.Bars == 0 && !counted.Pending)
                            break; // an empty body touches nothing
                        bars = Math.Min(DefaultExpansionBudgetCap, bars + counted.Bars);
                        pendingMusic = counted.Pending;
                        // LEAVING a body that closed its last bar makes the next `|` a
                        // confirmation of that close: `x | x` is two bars
                        // (EmptyMeasureValidatorTests PhraseBoundary_RendersTwoBars). An
                        // OPEN tail stays open — a phrase can be a beat (`phrase p1 { c4 }`,
                        // ExpansionBudgetTests' DagBook: 32 of them are eight bars, not 32)
                        // and this walk has no durations to close it by; under, never over.
                        // ENTERING changes nothing: a leading `|` inside the body closes an
                        // empty bar, as a section's own.
                        confirmable = !pendingMusic;
                    }
                    break;
                case SyntaxKind.Barline:
                    if (pendingMusic)
                    {
                        bars++; // closes the bar of music before it
                        pendingMusic = false;
                        confirmable = false;
                    }
                    // The bar token is slot 0 (BarlineSyntax.BarToken's shape).
                    else if (((InternalSyntax.SyntaxToken)child.GetSlot(0)!).Text != "|")
                    {
                        confirmable = false; // a typed bar decorates the boundary
                    }
                    else if (confirmable)
                    {
                        confirmable = false; // it CONFIRMS a close this walk just made
                    }
                    else
                    {
                        bars++; // an empty span between two boundaries: an empty measure
                    }
                    break;
                case SyntaxKind.Note:
                case SyntaxKind.Chord:
                case SyntaxKind.ChordRepetition:
                case SyntaxKind.SlashNote:
                case SyntaxKind.BareDuration:
                case SyntaxKind.ChordEntry:
                case SyntaxKind.ChordExtend:
                    pendingMusic = true;
                    break;
                case SyntaxKind.ParallelExpression:
                    // First voice only (ParallelExpressionSyntax.Voices: the
                    // MusicBlock slots between the << >> tokens).
                    for (int v = 1; v < child.SlotCount - 1; v++)
                    {
                        if (child.GetSlot(v) is { Kind: SyntaxKind.MusicBlock } firstVoice)
                        {
                            WalkBars(firstVoice, ref bars, ref pendingMusic, ref confirmable, phrases, activeRefs, ref budget);
                            break;
                        }
                    }
                    break;
                default:
                    WalkBars(child, ref bars, ref pendingMusic, ref confirmable, phrases, activeRefs, ref budget);
                    break;
            }
        }
    }

    /// <summary>
    /// The old RED bar-counting spelling, verbatim. ⚠️ No production caller
    /// since <see cref="WalkBars"/> went green — kept internal as the
    /// REFERENCE SPELLING the equivalence net (CanonicalBarsEquivalence in
    /// MusicSitesEquivalenceTests) runs against. Do not re-grow production
    /// callers: every red descendant it materializes is the whole-book
    /// first-touch cost the green walk exists to avoid.
    /// </summary>
    internal static int CountBarsInScopeRed(SyntaxNode scope)
    {
        int bars = 0;
        bool pendingMusic = false;
        bool confirmable = false;
        WalkBarsRed(scope, ref bars, ref pendingMusic, ref confirmable);
        return bars + (pendingMusic ? 1 : 0);
    }

    private static void WalkBarsRed(SyntaxNode node, ref int bars, ref bool pendingMusic, ref bool confirmable)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            switch (child)
            {
                case null:
                    break;
                case BarlineSyntax bar:
                    if (pendingMusic)
                    {
                        bars++;
                        pendingMusic = false;
                        confirmable = false;
                    }
                    else if (bar.BarToken.Text != "|")
                    {
                        confirmable = false;
                    }
                    else if (confirmable)
                    {
                        confirmable = false;
                    }
                    else
                    {
                        bars++;
                    }
                    break;
                case RestSyntax rest:
                    pendingMusic = true;
                    if (rest.MeasureCount > 1)
                        bars = (int)Math.Min(DefaultExpansionBudgetCap, (long)bars + rest.MeasureCount - 1);
                    break;
                case RepeatExpressionSyntax rep:
                    if (rep.RepeatType.Text == "tremolo")
                    {
                        pendingMusic = true;
                        break;
                    }
                    {
                        int turns = int.TryParse(rep.Count.Text, out int k) ? Math.Max(1, k) : 1;
                        int bodyBars = 0; bool bodyPending = false; bool bodyConfirmable = false;
                        WalkBarsRed(rep.Body, ref bodyBars, ref bodyPending, ref bodyConfirmable);
                        if (bodyBars == 0)
                        {
                            pendingMusic = true;
                            break;
                        }
                        bars = (int)Math.Min(DefaultExpansionBudgetCap, (long)bars + (long)turns * bodyBars);
                        pendingMusic = bodyPending;
                        if (rep.Alternative is { } alternative)
                            WalkBarsRed(alternative, ref bars, ref pendingMusic, ref confirmable);
                        confirmable = !pendingMusic;
                    }
                    break;
                case NoteSyntax:
                case ChordSyntax:
                case ChordRepetitionSyntax:
                case SlashNoteSyntax:
                case BareDurationSyntax:
                case ChordEntrySyntax:
                case ChordExtendSyntax:
                    pendingMusic = true;
                    break;
                case ParallelExpressionSyntax parallel:
                    var first = parallel.Voices.FirstOrDefault();
                    if (first != null)
                        WalkBarsRed(first, ref bars, ref pendingMusic, ref confirmable);
                    break;
                default:
                    WalkBarsRed(child, ref bars, ref pendingMusic, ref confirmable);
                    break;
            }
        }
    }

    /// <summary>
    /// Process the music inside a container node — a <c>part-block</c> (section-major)
    /// or a part-major inner <c>section</c>. Both expose their music as descendants.
    /// </summary>
    private void ProcessMusicContainer(SyntaxNode container, Action<List<GreenSite>> processNodes)
    {
        // Collect all music sites, expanding variable references. MusicSitesLazy
        // walks the green tree and yields only candidate sites outside processed
        // containers (tuplet/repeat/grace/inline volta/parallel — their content
        // is processed by those handlers, so an inline volta passes through as
        // ONE wrapper node, or the bracket ([1. ]/[2.]) is lost while its notes
        // leak out flat; a << \\ >> span likewise travels as one node). No red
        // node is created here — only the consumption points in ProcessNodes
        // materialize, so an adopted prefix / spliced tail stays red-free.
        var musicNodes = new List<GreenSite>();

        foreach (var site in MusicSitesLazy(container, includeParallel: true))
        {
            if (site.Kind == SyntaxKind.VariableReference)
            {
                var varRef = (VariableReferenceSyntax)site.Node;
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, musicNodes);
            }
            else if (IsCollectableMusicKind(site.Kind))
                musicNodes.Add(site);
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, int octaveOffset, List<GreenSite> musicNodes)
        => ExpandVariable(name, octaveOffset, musicNodes, new HashSet<string>());

    private void ExpandVariable(string name, int octaveOffset, List<GreenSite> musicNodes,
        HashSet<string> activeRefs)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;

        // Guard a reference cycle (x -> y -> x, or a three-way x -> y -> z -> x):
        // a phrase already open on the active chain is not re-expanded, so nesting
        // can never recurse forever. The cycle itself is reported once, statically,
        // by PhraseCycleValidator; here we simply render the acyclic prefix.
        if (!activeRefs.Add(name))
            return;

        // The DAG guard the cycle guard cannot be: activeRefs.Remove below means a
        // SIBLING reference re-expands, so an acyclic chain doubles per level (2^29
        // sites from 30 written lines). Charging one unit per phrase ENTRY — not
        // just per music site — matters: a DAG of EMPTY phrases emits only marker
        // pairs, which are sites all the same. On a spent budget the phrase emits
        // nothing (no reset marker, no end marker — balanced by omission).
        if (!ChargeExpansion(1, expression.SourceStart))
        {
            activeRefs.Remove(name);
            return;
        }

        // Each phrase reference evaluates its body in a FRESH relative frame
        // (default octave / pitch / duration): a phrase's pitches must not
        // depend on what happened to be played before the reference, or the
        // same phrase would render differently at every call site. This is
        // the moral equivalent of LilyPond variables carrying their own
        // \relative block. What flows OUT is the phrase's ANCHOR — its first
        // note's bare letter, the `<c e g>` chord rule — never its interior,
        // so a note after the reference does not depend on how the body ends.
        // Trailing marks on the reference (Chorus' / Chorus,) shift that fresh
        // frame, and shift the outgoing anchor with them.
        musicNodes.Add(new GreenSite(RelativeResetMarker.For(octaveOffset,
            Music.PhraseAnchor.AnchorStep(expression,
                n => _variables.TryGetValue(n, out var nested) ? nested : null))));

        // A phrase body may itself reference other phrases (phrase x { y }): expand a
        // nested reference IN PLACE — recursing into its own fresh frame — instead of
        // dropping it, so SVG stays in step with the MIDI / MusicXML exporters (which
        // already recurse). A bare music node is collected as before; container
        // expressions travel as ONE wrapper each (inner content skipped).
        void Emit(GreenSite s)
        {
            if (s.Kind == SyntaxKind.VariableReference)
            {
                var nestedRef = (VariableReferenceSyntax)s.Node;
                ExpandVariable(nestedRef.Name.Text, nestedRef.OctaveOffset, musicNodes,
                    activeRefs);
            }
            else if (IsCollectableMusicKind(s.Kind)
                && ChargeExpansion(1, expression.SourceStart))
                musicNodes.Add(s);
        }

        // The declaration's own expression node is a stored red — preset site.
        if (expression is VariableReferenceSyntax || IsCollectableMusicNode(expression))
            Emit(new GreenSite(expression));
        foreach (var s in MusicSitesLazy(expression, includeParallel: true))
            Emit(s);

        // Close the phrase so its auto-transpose is dropped before any inline
        // notes that follow the reference (paired with the reset marker above).
        musicNodes.Add(new GreenSite(PhraseEndMarker.Instance));
        activeRefs.Remove(name);
    }

    private static BarlineSyntax CreateBarlineSyntax(string barText, int position)
    {
        var kind = barText switch
        {
            "|:" => SyntaxKind.RepeatStartBar,
            ":|" => SyntaxKind.RepeatEndBar,
            "||" => SyntaxKind.DoubleBar,
            "|." => SyntaxKind.FinalBar,
            _ => SyntaxKind.Bar
        };

        var token = new LilySharp.Core.Syntax.InternalSyntax.SyntaxToken(kind, barText);
        var green = new LilySharp.Core.Syntax.InternalSyntax.BarlineGreen(token);
        return new BarlineSyntax(green, null, position);
    }

    // The pair below moved here from the main part (review 2026-08-26 appendix E-9):
    // EnsureSectionStartsForRows is the self-declared SECOND SPELLING of the form walk's
    // section-start bookkeeping — kept unfolded (a net holds the pair), placed next to
    // its twin so the two spellings are edited in one file.
    /// <summary>
    /// Rows-only scores reach row collection with an EMPTY section-start
    /// table — sections normally register while MUSIC is processed. Derive
    /// the starts from the row blocks themselves: replay the form's PLAYBACK
    /// order (or, with no form, declaration order), advancing by each
    /// section's widest row block (chord bars preferred, lyric bars
    /// otherwise). Without this a two-section chord grid printed both
    /// sections' symbols from bar 0, overlapped. No-op when music already
    /// filled the table.
    /// </summary>
    /// <remarks>
    /// ⚠️ SECOND SPELLING of <see cref="ProcessForm"/> (HANDOFF §5.2.1②). The two must
    /// agree on the ORDER and the OCCURRENCE COUNT of every section and differ only in
    /// what they advance — a bar cursor over the row grid here, a <c>MeasureBuilder</c>
    /// over real music there. They cannot be folded into one: a part-less chord grid
    /// (<c>chords X { section A { … } }</c>) declares its sections inside the TRACK, so
    /// there is no music for <see cref="ProcessForm"/> to walk. The seam has a net
    /// instead — <c>RowsOnlyFormOrderTests</c> renders the same book with and without a
    /// staff and compares the section starts bar for bar.
    /// <para>
    /// Before that net existed this walk skipped <c>|: … :|</c> blocks whole (every arm
    /// was gated on <c>!IsInsideRepeatBlock</c> and no arm handled the block itself) and
    /// collapsed a section's second occurrence onto its first (a <c>ContainsKey</c> guard
    /// that also REWOUND the cursor). So a staffless <c>form main { A B A }</c> engraved
    /// 6 bars instead of 10 with the reprise's syllables landing on top of the first A's,
    /// which is what the "lyrics overlap" report was.
    /// </para>
    /// </remarks>
    private void EnsureSectionStartsForRows(SyntaxNode root)
    {
        // Not `Sections.Count == 0`: a rows-only score's sections live INSIDE the chord / lyric
        // tracks (chords X { section A { … } }) and are deliberately kept out of the structure
        // Sections map, so bailing on an empty map stacked every section at bar 0.
        if (_sectionState.StartMeasure.Count > 0)
            return;

        // Walk the structure's children IN SOURCE ORDER so navigation marks
        // (segno / to coda / D.S. …) interleave with the section references at
        // the right bars — a rows-only score never runs ProcessForm, so
        // the band grid lost exactly the signs a band chart needs. Labels are
        // stamped onto the grid row's measures afterwards.
        int cur = 0;
        void AdvanceSection(string name, string? label, int pos)
        {
            int secBars = RowGridSectionBars(root, name);
            // An unknown name — neither a track cell nor a structure section — has nothing to place.
            if (secBars == 0 && !_sectionState.Sections.ContainsKey(name))
                return;
            // EVERY occurrence, not just the first — the same call ProcessForm makes.
            // RecordSectionStart keeps the first pass in StartMeasure and APPENDS each
            // pass to AllStarts, which is what the row collectors read to place a
            // reprise's cells (LyricsCollector / ChordNameCollector's StartsFor). This
            // used to write StartMeasure directly and never touch AllStarts, so both
            // collectors fell through to their single-anchor fallback and a section the
            // form names twice was engraved once.
            RecordSectionStart(name, cur);
            if (label != null)
                _sectionState.RowLabels.Add((cur, label, pos));
            // …and the cursor moves FORWARD by this pass's width. It used to be assigned
            // `StartMeasure[name] + secBars`, which REWOUND the grid to the section's
            // first occurrence, so every bar after a reprise overprinted bars already written.
            cur += secBars;
        }

        // The two barline edits ProcessRepeatBlock gets for free by pushing a BarlineSyntax
        // through MeasureBuilder.HandleBarline — restated here against a bar cursor because
        // a rows-only score has no builder. Both mirror that method's own branches:
        //   `|:` (RepeatStart)  -> HandleBarline sets _pendingStartBarline, i.e. it opens the
        //                          NEXT measure, which at this point in the walk is `cur`.
        //   `:|` on an empty span -> HandleBarline retro-applies the type to _measures[^1],
        //                          i.e. it closes the PREVIOUS measure, `cur - 1`.
        // An adjacent `:|` + `|:` is left as two edits on two measures; the renderer fuses
        // them into the RepeatBoth glyph, which is what `:|:` means (Form.cs:59-66).
        void OpenRepeatAt(int measure)
        {
            var (s, e) = _rowsOnlyFormBars.TryGetValue(measure, out var p) ? p : (BarlineType.None, BarlineType.None);
            _rowsOnlyFormBars[measure] = (Stronger(s, BarlineType.RepeatStart), e);
        }
        void CloseRepeatBefore(int measure)
        {
            if (measure <= 0)
                return;
            var (s, e) = _rowsOnlyFormBars.TryGetValue(measure - 1, out var p) ? p : (BarlineType.None, BarlineType.None);
            _rowsOnlyFormBars[measure - 1] = (s, Stronger(e, BarlineType.RepeatEnd));
        }

        // `|: … :|` — the slot walk ProcessRepeatBlock does (MeasureCollector.Form.cs:42-129),
        // with the music build removed: the block's own bars are raw tokens rather than
        // BarlineSyntax, and only the nodes AFTER the opening `|:` are played. A barline
        // occupies no bar, so the cursor is moved by the section arms alone.
        void AdvanceRepeatBlock(FormRepeatBlockSyntax repeat)
        {
            bool afterRepeatStart = false;
            for (int i = 0; i < repeat.SlotCount; i++)
            {
                var child = repeat.GetChild(i);
                if (child is SyntaxTokenNode token)
                {
                    // ':|:' closes one repeat and opens the next, so it arms the gate too.
                    if (token.Text is "|:" or ":|:")
                    {
                        if (token.Text == ":|:")
                            CloseRepeatBefore(cur);
                        OpenRepeatAt(cur);
                        afterRepeatStart = true;
                    }
                    else if (token.Text == ":|")
                    {
                        CloseRepeatBefore(cur);
                    }
                    continue;
                }
                if (!afterRepeatStart)
                    continue;
                switch (child)
                {
                    case SectionReferenceSyntax r:
                        AdvanceSection(r.SectionName, LabelForReference(r), SectionDeclPos(r.SectionName));
                        break;
                    case FormAlternativeSyntax alt:
                        // The bracket spans the bars this ending occupies, so it is measured
                        // ACROSS the advance — the same start/end pair ProcessRepeatBlock
                        // reads off the builder (Form.cs:105-125).
                        int altStart = cur;
                        AdvanceSection(alt.SectionName.Text, LabelForEnding(alt),
                            SectionDeclPos(alt.SectionName.Text));
                        if (alt.HasBracket && !alt.IsSilent && cur > altStart)
                            _voltaBrackets.Add(new VoltaBracketItem(
                                altStart, cur - 1, alt.VoltaText, alt.IsClosed, alt.SourceStart));
                        break;
                    case { Kind: SyntaxKind.SilentSectionReference } silent
                            when silent.GetChild(1) is SyntaxTokenNode silentName:
                        AdvanceSection(silentName.Text, LabelForSilentReference(silent, silentName.Text), SectionDeclPos(silentName.Text));
                        break;
                }
            }
        }

        if (_form != null)
        {
            foreach (var child in _form.DescendantNodes())
            {
                switch (child)
                {
                    case SectionReferenceSyntax r when !IsInsideRepeatBlock(r):
                        AdvanceSection(r.SectionName, LabelForReference(r), SectionDeclPos(r.SectionName));
                        break;
                    // The arm ProcessForm has and this walk did not. Every other arm is
                    // gated on !IsInsideRepeatBlock, so without this one the whole block
                    // was stepped over: its sections took no bars and everything after it
                    // was laid on top of what came before.
                    case FormRepeatBlockSyntax repeat:
                        AdvanceRepeatBlock(repeat);
                        break;
                    // A volta ending that NO repeat block opened — `form main { A [1. B] }`.
                    // It plays exactly once and engraves no bracket; see the same arm in
                    // ProcessForm for the LilyPond reference that settles the play count.
                    case FormAlternativeSyntax alt when !IsInsideRepeatBlock(alt):
                        AdvanceSection(alt.SectionName.Text, LabelForEnding(alt),
                            SectionDeclPos(alt.SectionName.Text));
                        break;
                    // ~Name — its bars are played, its label is not shown.
                    case { Kind: SyntaxKind.SilentSectionReference } silent
                            when !IsInsideRepeatBlock(silent)
                              && silent.GetChild(1) is SyntaxTokenNode nameTok:
                        AdvanceSection(nameTok.Text, LabelForSilentReference(silent, nameTok.Text), SectionDeclPos(nameTok.Text));
                        break;
                    case NavigationMarkSyntax nav when !IsInsideRepeatBlock(nav):
                        // Same anchoring as ProcessForm: targets (segno/coda)
                        // at the NEXT section's start, jump text at the end of
                        // the section just played.
                        var navMark = NavigationToMusicMark(nav.MarkType);
                        bool target = navMark is MusicMarkType.Segno or MusicMarkType.Coda;
                        int navMeasure = target ? cur : Math.Max(0, cur - 1);
                        _musicMarks.Add(new MusicMarkItem(navMark, navMeasure, nav.SourceStart));
                        break;
                    case CustomTextSyntax custom when !IsInsideRepeatBlock(custom):
                        _customTexts.Add(new CustomTextItem(
                            custom.Text, Math.Max(0, cur - 1), custom.SourceStart));
                        break;
                }
            }
        }
        else
        {
            foreach (var s in _sectionState.Sections.Values.OrderBy(s => s.Name.Span.Start))
                AdvanceSection(s.SectionName, s.SectionName, s.Name.Span.Start);
            // (No form, no repeat block — _rowsOnlyFormBars stays empty on this branch.)
        }

        // The grid the walk laid out. Bounds the synthetic structure voice so it never
        // claims a bar the rows do not have.
        _rowsOnlyFormGridBars = cur;
    }

    /// <summary>
    /// The bar span section <paramref name="name"/> occupies in the chord / lyric ROW grid. A
    /// rows-only score never runs ProcessForm, so the section starts are laid out from here — and
    /// the section must be counted however it is written: as a part-major chord / lyric TRACK
    /// inner section (<c>chords X { section NAME { … } }</c>), whose bars live on the section
    /// itself (the block is its ancestor, not a descendant), OR as chord / lyric blocks nested in
    /// a section-major section. The descendant-only count missed the track form, so a rows-only
    /// score with several sections stacked every section at bar 0.
    /// </summary>
    private int RowGridSectionBars(SyntaxNode root, string name)
    {
        int bars = 0;

        // Part-major TRACKS: the section sits INSIDE the chord / lyric block.
        foreach (var block in root.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>())
            if (block.HasSections)
                foreach (var sec in block.Sections)
                    if (sec.SectionName == name)
                        bars = Math.Max(bars, ChordNameCollector.CountSectionBars(sec));
        foreach (var block in root.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
            if (block.HasSections)
                foreach (var sec in block.Sections)
                    if (sec.SectionName == name)
                        bars = Math.Max(bars, LyricSyllableReader.CountBars(sec));

        // Section-major: the chord / lyric blocks are nested in the (registered) section itself.
        if (_sectionState.Sections.TryGetValue(name, out var representative))
        {
            foreach (var block in representative.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>())
                bars = Math.Max(bars, ChordNameCollector.CountBars(block));
            foreach (var block in representative.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>())
                bars = Math.Max(bars, LyricSyllableReader.CountBars(block));
        }

        return bars;
    }

    private static bool IsInsideRepeatBlock(SyntaxNode node) => node.IsInside<FormRepeatBlockSyntax>();

}
