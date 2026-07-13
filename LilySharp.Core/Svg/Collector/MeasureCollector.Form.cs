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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

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
        var scoreTime = TimeSignatureFraction;
        if (builder.CurrentMeasureLength != scoreTime)
            builder.AddItem(new TimeSignatureChangeItem(
                new TimeSignature(_meta.TimeBeats, _meta.TimeBeatType, _meta.TimeBeatsText),
                sectionPos));

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
        builder.AddItem(new KeySignatureChangeItem(newKey, previousKey, keySig.Position));
    }

    /// <summary>The first <c>key</c> that is a DIRECT child of the section (its own
    /// starting key), or null when the section states none.</summary>
    private static KeySignatureSyntax? FirstDirectKey(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
            if (section.GetChild(i) is KeySignatureSyntax k)
                return k;
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
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax)
                continue;
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
    /// Bar count of a music scope (a part block or a part-major section cell): one per
    /// written barline, plus a trailing partial bar when music follows the last
    /// barline — the same segmentation as <see cref="ChordNameCollector.CountBars"/>.
    /// A <c>&lt;&lt; \\ &gt;&gt;</c> polyphonic span counts as ONLY its first voice's
    /// bars: the main stream advances by that voice while the others overlay the same
    /// measures, so counting every voice's barlines would multiply the bar count.
    /// </summary>
    private static int CountBarsInScope(SyntaxNode scope)
    {
        int bars = 0;
        bool pendingMusic = false;
        WalkBars(scope, ref bars, ref pendingMusic);
        return bars + (pendingMusic ? 1 : 0);
    }

    private static void WalkBars(SyntaxNode node, ref int bars, ref bool pendingMusic)
    {
        for (int i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetChild(i);
            switch (child)
            {
                case null:
                    break;
                case BarlineSyntax:
                    bars++;
                    pendingMusic = false;
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
                        WalkBars(first, ref bars, ref pendingMusic);
                    break;
                default:
                    WalkBars(child, ref bars, ref pendingMusic);
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
                ExpandVariable(varRef.Name.Text, varRef.OctaveOffset, musicNodes);
            else if (IsCollectableMusicNode(node))
                musicNodes.Add(node);
        }

        processNodes(musicNodes);
    }

    private void ExpandVariable(string name, int octaveOffset, List<SyntaxNode> musicNodes)
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
        // on the reference (Chorus' / Chorus,) shift that fresh frame.
        musicNodes.Add(RelativeResetMarker.For(octaveOffset));

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
