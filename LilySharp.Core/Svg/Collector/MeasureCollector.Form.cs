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

namespace LilySharp.Core.Svg.Collector;

// Structure expansion for MeasureCollector: unfolding structure/section/repeat
// blocks and music containers into the flat music-node stream, expanding phrase
// variable references, and the synthetic-barline helper. Split out of
// MeasureCollector.cs as a partial class; same instance state, no behavior change.
public sealed partial class MeasureCollector
{
    private void ProcessRepeatBlock(FormRepeatBlockSyntax repeat, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        bool afterRepeatStart = false;
        var pendingVoltaBrackets = new List<(int startMeasure, int endMeasure, string voltaText, bool isClosed, int sourcePosition)>();

        for (int i = 0; i < repeat.SlotCount; i++)
        {
            var child = repeat.GetChild(i);

            if (child is SyntaxTokenNode token)
            {
                if (token.Text == "|:")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                    afterRepeatStart = true;
                }
                else if (token.Text == ":|")
                {
                    processNodes(new[] { CreateBarlineSyntax(token.Text, token.Position) });
                }
                else if (token.Text == ":|:")
                {
                    // Back-to-back repeat divider: close the current repeat and open
                    // the next. The adjacent ':|' + '|:' fuse into the RepeatBoth
                    // glyph at render time, and the following section is still marked
                    // as a repeat (StartBarline = RepeatStart) — exactly ':| |:'.
                    processNodes(new[] { CreateBarlineSyntax(":|", token.Position) });
                    processNodes(new[] { CreateBarlineSyntax("|:", token.Position) });
                    afterRepeatStart = true;
                }
            }
            else if (afterRepeatStart)
            {
                if (child is BreakSyntax brk)
                {
                    // `break` / `nobreak` inside the repeat flags the section just played.
                    if (brk.IsNoBreak) builder.SetNoBreak();
                    else builder.SetBreak();
                }
                else if (child is SectionReferenceSyntax reference)
                {
                    if (_sectionState.Sections.TryGetValue(reference.SectionName, out var section))
                    {
                        RecordSectionStart(reference.SectionName, builder.CurrentMeasureIndex);
                        builder.SectionLabel = ResolveSectionLabel(reference);
                        builder.SectionLabelPosition = SectionDeclPos(reference.SectionName);
                        ProcessSection(section, processNodes, builder);
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
                    RecordSectionStart(silentName.Text, builder.CurrentMeasureIndex);
                    builder.SectionLabel = null;
                    builder.SectionLabelPosition = SectionDeclPos(silentName.Text);
                    ProcessSection(silentSection, processNodes, builder);
                }
                else if (child is FormAlternativeSyntax alt)
                {
                    string altSectionName = alt.SectionName.Text;
                    if (_sectionState.Sections.TryGetValue(altSectionName, out var section))
                    {
                        // Track measure index before processing this alternative
                        int startMeasureIndex = builder.CurrentMeasureIndex;
                        RecordSectionStart(altSectionName, startMeasureIndex);

                        builder.SectionLabel = alt.DisplayLabel ?? altSectionName;
                        builder.SectionLabelPosition = SectionDeclPos(altSectionName);
                        ProcessSection(section, processNodes, builder);

                        // Track measure index after processing
                        int endMeasureIndex = builder.CurrentMeasureIndex;
                        // If we're mid-measure, include that measure
                        if (builder.CurrentItemCount > 0)
                            endMeasureIndex++;

                        // Collect volta bracket info if bracket style
                        // endMeasureIndex is exclusive (one-past-end); convert to inclusive
                        // for VoltaBracketItem which stores the last measure index
                        if (alt.HasBracket && !alt.IsSilent)
                        {
                            int lastMeasure = Math.Max(startMeasureIndex, endMeasureIndex - 1);
                            pendingVoltaBrackets.Add((startMeasureIndex, lastMeasure, alt.VoltaText, alt.IsClosed, alt.Position));
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

    private void ProcessSection(SectionDeclarationSyntax section, Action<IEnumerable<SyntaxNode>> processNodes, MeasureBuilder builder)
    {
        // Reset the relative frame (and revert the octave mode to the file
        // default) at each section boundary. The default DURATION resets too, so a
        // section is self-contained: an un-numbered first note starts a quarter
        // regardless of the preceding section's last duration. Without this the
        // reprise `A` after `~B` (`g'1`) inherited B's whole-note and rendered its
        // quarter-note melody as whole notes.
        _octave.ResetForSection();
        _defaultDuration = Fraction.Quarter;

        // A section is self-contained: re-arm the confirmable boundary so a section that
        // OPENS with a bare `|` anchors its OWN start (no empty measure, no leak into the
        // previous section) — an empty measure is always an explicit `| |` pair.
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
                        builder.CurrentMeasureIndex, builder.CurrentItemCount, false, _currentStaffIndex));
                else
                    _grobReverts.Add(new GrobRevert(grob, prop,
                        builder.CurrentMeasureIndex, builder.CurrentItemCount, _currentStaffIndex));
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
            foreach (var child in section.DescendantNodes())
            {
                if (child.Parent == section && child is OverrideDeclarationSyntax secOv)
                {
                    CollectOverride(secOv, builder.CurrentMeasureIndex, builder.CurrentItemCount,
                        isOnce: false, staffIndex: _currentStaffIndex);
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
            builder.AddItem(new TimeSignatureChangeItem(
                new TimeSignature(sectionTime.Beats, sectionTime.BeatType, sectionTime.BeatsText),
                sectionPos));
            builder.SetMeasureLength(new Fraction(sectionTime.Beats, sectionTime.BeatType));
        }
        else
        {
            // No section meter: revert the running meter to the SCORE level. Compare (and
            // redraw) against the SNAPSHOT, not _meta.Time - a mid-music `time` in a prior
            // section mutates _meta (which also drives the opening signature), so _meta no
            // longer holds the score meter. Only redraw when the previous section actually
            // left a different meter on the staff.
            var resetTime = new Fraction(_sectionResetTimeBeats, _sectionResetTimeBeatType);
            if (builder.CurrentMeasureLength != resetTime)
            {
                builder.AddItem(new TimeSignatureChangeItem(
                    new TimeSignature(_sectionResetTimeBeats, _sectionResetTimeBeatType,
                        _sectionResetTimeBeatsText, _sectionResetTimeSenzaMisura),
                    sectionPos));
                builder.SetMeasureLength(resetTime);
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

        int startMeasure = builder.CurrentMeasureIndex;
        bool matched = false;
        foreach (var child in section.DescendantNodes())
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
            var inline = new List<SyntaxNode>();
            for (int i = 0; i < section.SlotCount; i++)
            {
                var child = section.GetChild(i);
                if (child is VariableReferenceSyntax varRef)
                    ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, inline, varRef.DiatonicShiftSteps);
                else if (child != null && IsCollectableMusicNode(child))
                    inline.Add(child);
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
        if (_voiceName != null && builder.CurrentItemCount == 0)
        {
            int produced = builder.CurrentMeasureIndex - startMeasure;
            int canonical = GetCanonicalSectionBars(section);
            for (int i = produced; i < canonical; i++)
                builder.AddItem(new RestItem(TimeSignatureFraction, 0, section.Position) { IsSpacer = true });
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
            _keyByMeasure[builder.CurrentMeasureIndex] =
                (Math.Max(0, LilySharp.Core.Music.KeySpelling.StepOf(keySig.Pitch.PitchName[0])), newSharps);
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

        builder.AddItem(new KeySignatureChangeItem(newKey, previousKey, keySig.Position));
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
    private static bool SectionHasInlineMusic(SectionDeclarationSyntax section)
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
    /// section-major section). A section spans as many bars as its longest part, so
    /// shorter parts pad up to this to stay aligned.
    /// </summary>
    private int GetCanonicalSectionBars(SectionDeclarationSyntax section)
    {
        int max = 0;

        // Part-major: every `part <p> { section <name> { ... } }` cell for this name.
        foreach (var kv in _sectionState.PartMajorCells)
            if (kv.Key.section == section.SectionName)
                max = Math.Max(max, CountBarsInScope(kv.Value));

        // Section-major: the sibling part blocks inside the section declaration.
        foreach (var part in section.DescendantNodes().OfType<PartBlockSyntax>())
            max = Math.Max(max, CountBarsInScope(part));

        // Fallback: a standalone section whose own descendants are the music.
        if (max == 0)
            max = CountBarsInScope(section);

        return max;
    }

    /// <summary>
    /// Bar count of a music scope (a part block or a part-major section cell),
    /// mirroring <see cref="MeasureBuilder.HandleBarline"/>'s bare-barline rules: a
    /// barline after music closes a bar; a single bare <c>|</c> on an empty span
    /// anchors the boundary it sits on (the scope start) and counts NOTHING; only the
    /// second of a <c>| |</c> pair is an empty measure; a TYPED barline on an empty
    /// span is a decoration. A trailing partial bar (music after the last barline)
    /// counts. (Chords/lyrics tracks keep their own slot-style counting — see
    /// <see cref="ChordNameCollector.CountBars"/> — their barlines ARE the structure.)
    /// A <c>&lt;&lt; \\ &gt;&gt;</c> polyphonic span counts as ONLY its first voice's
    /// bars: the main stream advances by that voice while the others overlay the same
    /// measures, so counting every voice's barlines would multiply the bar count.
    /// </summary>
    private static int CountBarsInScope(SyntaxNode scope)
    {
        int bars = 0;
        bool pendingMusic = false;
        bool confirmable = true; // the scope-start boundary absorbs one bare `|`
        WalkBars(scope, ref bars, ref pendingMusic, ref confirmable);
        return bars + (pendingMusic ? 1 : 0);
    }

    private static void WalkBars(SyntaxNode node, ref int bars, ref bool pendingMusic, ref bool confirmable)
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
                        bars++; // closes the bar of music before it
                        pendingMusic = false;
                        confirmable = false;
                    }
                    else if (bar.BarToken.Text != "|")
                    {
                        confirmable = false; // a typed bar decorates the boundary
                    }
                    else if (confirmable)
                    {
                        confirmable = false; // a lone `|` anchors the boundary
                    }
                    else
                    {
                        bars++; // the second of a `| |` pair: an empty measure
                    }
                    break;
                case NoteSyntax:
                case RestSyntax:
                case ChordSyntax:
                case ChordEntrySyntax:
                    pendingMusic = true;
                    break;
                case ParallelExpressionSyntax parallel:
                    var first = parallel.Voices.FirstOrDefault();
                    if (first != null)
                        WalkBars(first, ref bars, ref pendingMusic, ref confirmable);
                    break;
                default:
                    WalkBars(child, ref bars, ref pendingMusic, ref confirmable);
                    break;
            }
        }
    }

    /// <summary>
    /// Process the music inside a container node — a <c>part-block</c> (section-major)
    /// or a part-major inner <c>section</c>. Both expose their music as descendants.
    /// </summary>
    private void ProcessMusicContainer(SyntaxNode container, Action<IEnumerable<SyntaxNode>> processNodes)
    {
        // Collect all music nodes, expanding variable references
        var musicNodes = new List<SyntaxNode>();

        foreach (var node in container.DescendantNodes())
        {
            // Skip nodes inside containers (tuplet/repeat/grace/inline volta/
            // parallel) — they'll be processed by those handlers. Inline voltas
            // in particular must pass through as ONE wrapper node, or the
            // bracket ([1. ]/[2.]) is lost while its notes leak out flat. A
            // << \\ >> span likewise passes through as one node.
            if (IsInsideProcessedContainer(node))
                continue;

            if (node is VariableReferenceSyntax varRef)
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, musicNodes, varRef.DiatonicShiftSteps);
            else if (IsCollectableMusicNode(node))
                musicNodes.Add(node);
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, int octaveOffset, List<SyntaxNode> musicNodes,
        int diatonicSteps = 0)
    {
        if (!_variables.TryGetValue(name, out var expression))
            return;

        // Each phrase reference evaluates its body in a FRESH relative frame
        // (default octave / pitch / duration): a phrase's pitches must not
        // depend on what happened to be played before the reference, or the
        // same $phrase would render differently at every call site. This is
        // the moral equivalent of LilyPond variables carrying their own
        // \relative block. State flows OUT of the phrase normally, so a note
        // following $phrase is relative to the phrase's last note. Trailing marks
        // on the reference (Chorus' / Chorus,) shift that fresh frame; a glued
        // interval argument (Chorus'(3)) shifts the body by scale steps.
        musicNodes.Add(RelativeResetMarker.For(octaveOffset, diatonicSteps));

        // Include the expression itself if it is a music node.
        if (IsCollectableMusicNode(expression))
            musicNodes.Add(expression);

        // Get music nodes from the variable expression descendants; container
        // expressions travel as ONE wrapper node each (inner content skipped).
        var nodes = expression.DescendantNodes()
            .Where(n => !IsInsideProcessedContainer(n) && IsCollectableMusicNode(n));

        musicNodes.AddRange(nodes);

        // Close the phrase so its auto-transpose is dropped before any inline
        // notes that follow the reference (paired with the reset marker above).
        musicNodes.Add(PhraseEndMarker.Instance);
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
}
