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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Collects lyrics into <see cref="LyricItem"/>s: note-bound lines (<c>lyrics melody
/// { … }</c>, one syllable per note, optionally auto-wrapping into verses) and
/// independent lyrics ROWS (<c>lyrics name { … }</c> placed as a score row, syllables
/// spread evenly across each bar). Pulled out of <see cref="MeasureCollector"/> so all
/// lyric logic lives together. The collection-time context it reads — the section→
/// measure map, the bound voices, the lyrics-row names, the time signature — is passed
/// in, as it is built by the main pass.
/// </summary>
internal sealed class LyricsCollector
{
    private readonly List<LyricItem> _lyrics = new();
    private readonly List<LyricSyllableWarning> _warnings = new();
    private readonly List<ShadowedPlainLyricWarning> _shadowedPlain = new();

    /// <summary>All collected lyric syllables (note-bound and row).</summary>
    public IReadOnlyList<LyricItem> Lyrics => _lyrics;

    /// <summary>Lyric lines with more syllables than notes (trailing words dropped).</summary>
    public IReadOnlyList<LyricSyllableWarning> Warnings => _warnings;

    /// <summary>Plain lyric verses fully shadowed by a section's <c>[N. …]</c> verses
    /// (they never render). Populated as a side effect of collection.</summary>
    public IReadOnlyList<ShadowedPlainLyricWarning> ShadowedPlainWarnings => _shadowedPlain;

    /// <summary>
    /// Collects the note-bound lyrics attached to ONE staff via
    /// <c>staff X with lyrics L [with lyrics L2 …]</c>: the named block(s) align to
    /// <paramref name="measures"/> (the staff's primary voice) and render below the staff
    /// at <paramref name="staffIndex"/>. Multiple names stack as verses (document order).
    /// </summary>
    public void CollectAttached(
        SyntaxNode root, IReadOnlyList<string> blockNames, List<Measure> measures, int staffIndex,
        IReadOnlySet<string> lyricsRowNames,
        IReadOnlyDictionary<string, (int Index, List<Measure> Measures)> voiceMeasuresByName,
        IReadOnlyDictionary<string, int> sectionStartMeasure,
        IReadOnlyDictionary<string, List<int>>? sectionAllStarts = null)
        => CollectNoteBound(root, measures, lyricsRowNames, voiceMeasuresByName,
            sectionStartMeasure, sectionAllStarts, onlyBlocks: blockNames, staffIndex: staffIndex);

    /// <summary>
    /// Collects note-bound lyrics from every <c>lyrics { … }</c> block (skipping the
    /// independent ROW blocks, which <see cref="CollectRow"/> handles). A block aligns
    /// to the notes of the SECTION it is written in (offset to that section's first
    /// measure); blocks sharing a section start stack as successive verses.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:60-88 process_music
    /// LILYPOND-REF: lily/lyric-combine-music-iterator.cc:100-150 note association
    /// </remarks>
    /// <param name="onlyBlocks">When non-null, process ONLY the lyrics blocks whose
    /// name is in this set (the explicit <c>staff X with lyrics NAME …</c> attachments
    /// for one staff), stacking them as verses in document order. Null = every
    /// non-row block (legacy path, no longer used by the render pipeline).</param>
    /// <param name="staffIndex">Staff the syllables sit under; tagged onto each
    /// <c>LyricItem</c> so a later staff's lyrics render beneath THAT staff.</param>
    public void CollectNoteBound(
        SyntaxNode root, List<Measure> measures,
        IReadOnlySet<string> lyricsRowNames,
        IReadOnlyDictionary<string, (int Index, List<Measure> Measures)> voiceMeasuresByName,
        IReadOnlyDictionary<string, int> sectionStartMeasure,
        IReadOnlyDictionary<string, List<int>>? sectionAllStarts = null,
        IReadOnlyList<string>? onlyBlocks = null,
        int staffIndex = 0,
        bool asRow = false)
    {
        var lyricsBlocks = root.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>().ToList();
        if (lyricsBlocks.Count == 0)
            return;

        var defaultIndices = BuildNoteIndices(measures);

        var lyricCollector = new LyricCollector();
        var nextVerseByStart = new Dictionary<int, int>();
        foreach (var lyricsBlock in lyricsBlocks)
        {
            // An independent lyrics row's block is collected by CollectRow (even-spread),
            // not here — skip it so it isn't also note-bound.
            if (lyricsBlock.VoiceName is { } rowName && lyricsRowNames.Contains(rowName))
                continue;

            // Explicit-attach filter: only the blocks this `with lyrics` clause names.
            // An unnamed block can never be referenced, so it is skipped (and flagged
            // elsewhere as an unattached-lyrics error).
            if (onlyBlocks != null
                && (lyricsBlock.VoiceName is not { } bn || !onlyBlocks.Contains(bn)))
                continue;

            // `lyrics sop { … }` aligns to the same-named voice's notes; an unnamed
            // block uses the default (first) voice. The note count AND columns then come
            // from the right voice (the voice index drives timing-based X), so a voice
            // with its own rhythm matches.
            var indices = defaultIndices;
            int voiceId = 0;
            if (lyricsBlock.VoiceName is { } vn
                && voiceMeasuresByName.TryGetValue(vn, out var bound))
            {
                indices = BuildNoteIndices(bound.Measures);
                voiceId = bound.Index;
            }

            // Collect one run of lyric measures aligned from an absolute start bar,
            // stacking verses per start and reporting any overflow. A `[N. …]` verse
            // forces its own verse number (forcedVerse) and may hide the number label
            // (hideLabel); a plain run stacks at the next free verse for its start.
            void ProcessRun(IEnumerable<SyntaxNode> syllableMeasures, int startMeasure,
                int? forcedVerse = null, bool hideLabel = false)
            {
                int verseNumber = forcedVerse
                    ?? (nextVerseByStart.TryGetValue(startMeasure, out var v) ? v : 1);

                IReadOnlyList<(int MeasureIndex, int ItemIndex, Fraction Timing)> aligned = startMeasure <= 0
                    ? indices
                    : indices.Where(n => n.MeasureIndex >= startMeasure).ToList();

                // Index the run from its own first bar, so a rest-only bar (an r1 pickup)
                // still counts and a leading "| " lyric bar lines up with it.
                var lyrics = lyricCollector.Collect(syllableMeasures, aligned, out var overflow,
                    voiceId: voiceId, verseNumber, hideStanza: hideLabel,
                    baseMeasureIndex: Math.Max(0, startMeasure));
                _lyrics.AddRange(lyrics.Select(l => asRow
                    // A melody-BOUND ROW: the syllables carry the melody's note
                    // timings but render on the row band (IsLyricsRow X-from-
                    // timing path), grouped under the row's own staff index.
                    ? l with { StaffIndex = staffIndex, VoiceId = staffIndex, IsLyricsRow = true }
                    : staffIndex == 0 ? l : l with { StaffIndex = staffIndex }));

                // A single block may auto-wrap into several stacked verses; the next
                // block at this start begins after the highest verse this one produced.
                int maxVerse = verseNumber;
                foreach (var ly in lyrics)
                    if (ly.VerseNumber > maxVerse) maxVerse = ly.VerseNumber;
                nextVerseByStart[startMeasure] = maxVerse + 1;

                // More syllables than notes: the loop above ran out of notes and the
                // trailing syllables vanished. Flag the FIRST dropped syllable itself
                // (not just the lyrics keyword) so the author lands on the exact word
                // where the miscount starts instead of recounting the whole line.
                if (overflow is { } of)
                    _warnings.Add(new LyricSyllableWarning(
                        new LilySharp.Core.Syntax.TextSpan(of.FirstPosition, Math.Max(1, of.FirstText.Length)),
                        of.Count, of.FirstText, of.FirstBar));
            }

            // Part-major lyric track: each inner section's verse aligns under its own
            // named section's bars — at EVERY occurrence (a reprise gets it too). Flat
            // form: the enclosing section's occurrences (or 0 at top level).
            List<int> StartsFor(string sectionName)
            {
                if (sectionAllStarts != null && sectionAllStarts.TryGetValue(sectionName, out var all) && all.Count > 0)
                    return all;
                return new List<int> { sectionStartMeasure.GetValueOrDefault(sectionName, 0) };
            }

            // Place a section's verse(s). No `[N. …]` brackets → the same words repeat
            // at every written-out occurrence (the old default). With brackets:
            // occurrence i takes the verse whose selector covers it (else the plain
            // fallback line); verses numbered PAST the written-out occurrences — a
            // `|: :|` repeat that prints once but is sung again, or extra stanzas —
            // stack as further verses at the last occurrence.
            void PlaceSection(IReadOnlyList<SyntaxNode> body, IReadOnlyList<int> starts, string sectionName)
            {
                if (starts.Count == 0) return;
                var (voltas, plain) = SplitVoltas(body);
                if (voltas == null)
                {
                    foreach (int start in starts)
                        ProcessRun(body, start);
                    return;
                }

                bool plainUsed = false;
                for (int i = 1; i <= starts.Count; i++)
                {
                    var hit = voltas.FirstOrDefault(v => v.Spec.Occurrences.Contains(i));
                    if (hit.Measures != null)
                        ProcessRun(hit.Measures, starts[i - 1], hit.Spec.PrimaryNumber, hit.Spec.HideLabel);
                    else if (plain is { Count: > 0 })
                    {
                        ProcessRun(plain, starts[i - 1]);
                        plainUsed = true;
                    }
                }

                // Extra stanzas — verses whose occurrences ALL lie past the written-out
                // passes (a |: :| repeat printed once but sung again, or extra numbered
                // stanzas) — stack at the last occurrence. Test every occurrence, not just
                // the primary (label) number: a descending list like [3,1.] has primary 3
                // but occurrence 1, which the per-occurrence loop already placed — stacking
                // it here too would print it twice.
                foreach (var v in voltas)
                    if (v.Spec.Occurrences.All(o => o > starts.Count))
                        ProcessRun(v.Measures, starts[^1], v.Spec.PrimaryNumber, v.Spec.HideLabel);

                // A plain verse only fills an occurrence NO bracket covers. If every
                // occurrence already has a numbered verse, the plain line is dead — it
                // never renders. Silent for the user, so flag it rather than dropping it.
                if (!plainUsed && plain is { Count: > 0 })
                    _shadowedPlain.Add(new ShadowedPlainLyricWarning(plain[0].Span, sectionName));
            }

            if (lyricsBlock.HasSections)
                foreach (var section in lyricsBlock.Sections)
                    PlaceSection(SectionLyricMeasures(section).ToList(), StartsFor(section.SectionName), section.SectionName);
            else
            {
                string? enclosing = EnclosingSectionName(lyricsBlock);
                if (enclosing != null)
                    PlaceSection(lyricsBlock.Syllables.ToList(), StartsFor(enclosing), enclosing);
                else
                    // Top level, no section to repeat under: take the first verse only
                    // (also flattens away any stray brackets so they never render as words).
                    ProcessRun(FirstVerse(lyricsBlock.Syllables.ToList()),
                        ResolveStartMeasure(lyricsBlock, sectionStartMeasure));
            }
        }
    }

    /// <summary>A per-occurrence lyric verse's occurrence selector and number label:
    /// the occurrence(s) it covers, the primary (label) number, and whether a leading
    /// <c>~</c> hides that number.</summary>
    private readonly record struct VoltaSpec(int PrimaryNumber, bool HideLabel, HashSet<int> Occurrences);

    /// <summary>Splits a lyric-section body into its per-occurrence <c>[N. …]</c> verses
    /// (in source order) and the plain (unbracketed) measures, if any.</summary>
    private static (List<(VoltaSpec Spec, List<SyntaxNode> Measures)>? Voltas, List<SyntaxNode>? Plain)
        SplitVoltas(IReadOnlyList<SyntaxNode> body)
    {
        List<(VoltaSpec, List<SyntaxNode>)>? voltas = null;
        List<SyntaxNode>? plain = null;
        foreach (var node in body)
        {
            if (node.Kind == SyntaxKind.LyricVolta)
                (voltas ??= new()).Add((ParseVoltaSpec(node), VoltaMeasures(node).ToList()));
            else
                (plain ??= new()).Add(node);
        }
        return (voltas, plain);
    }

    /// <summary>Parses a <c>[ ~? N ((,|-) N)* . ]</c> header into its occurrence set and
    /// label: <c>,</c> lists, <c>-</c> ranges, a leading <c>~</c> hides the number label.
    /// The primary (label) number is the first written.</summary>
    private static VoltaSpec ParseVoltaSpec(SyntaxNode volta)
    {
        bool hide = false, range = false;
        int primary = 0;
        int? prev = null;
        var nums = new HashSet<int>();
        for (int i = 1; i < volta.SlotCount; i++)
        {
            if (volta.GetChild(i) is not SyntaxTokenNode t) continue;
            if (t.Kind == SyntaxKind.Dot) break;
            switch (t.Kind)
            {
                case SyntaxKind.Tilde: hide = true; break;
                case SyntaxKind.Minus: range = true; break;
                case SyntaxKind.Comma: range = false; break;
                case SyntaxKind.IntegerLiteral when int.TryParse(t.Text, out int n):
                    if (primary == 0) primary = n;
                    if (range && prev is { } p)
                        for (int k = Math.Min(p, n); k <= Math.Max(p, n); k++) nums.Add(k);
                    else
                        nums.Add(n);
                    prev = n;
                    range = false;
                    break;
            }
        }
        return new VoltaSpec(primary == 0 ? 1 : primary, hide, nums);
    }

    /// <summary>The lyric measures inside a <c>[ … . measures ]</c> verse: the non-token
    /// children AFTER the header's '.', skipping the optional closing ']' and null slots.</summary>
    private static IEnumerable<SyntaxNode> VoltaMeasures(SyntaxNode volta)
    {
        bool afterDot = false;
        for (int i = 0; i < volta.SlotCount; i++)
        {
            var child = volta.GetChild(i);
            if (!afterDot)
            {
                if (child is SyntaxTokenNode t && t.Kind == SyntaxKind.Dot) afterDot = true;
                continue;
            }
            if (child is SyntaxNode node and not SyntaxTokenNode)
                yield return node;
        }
    }

    /// <summary>The first verse's measures of a lyric body — plain measures if any, else
    /// the first <c>[N. …]</c> verse, else the body as written. Used where a run can't be
    /// spread across passes (an independent row, or a top-level block).</summary>
    private static IReadOnlyList<SyntaxNode> FirstVerse(IReadOnlyList<SyntaxNode> body)
    {
        var (voltas, plain) = SplitVoltas(body);
        if (voltas == null) return body;
        if (plain is { Count: > 0 }) return plain;
        return voltas[0].Measures;
    }

    /// <summary>
    /// Collects an independent lyrics row (<c>lyrics name { … }</c> placed via
    /// <c>lyrics name</c> in a score) with NO bound melody: each bar's syllables are
    /// spread evenly across it, and an empty bar (bare <c>|</c>) is skipped. Multiple
    /// blocks of the same name stack as verses (1番, 2番, …). A SINGLE block whose bars
    /// exceed the section's bar count also auto-wraps: every wrap-bars measures start
    /// the next stacked verse, mapped back onto the same bars. Returns the row's measure
    /// skeleton (spacer rests for width); the syllables go to <see cref="Lyrics"/>
    /// tagged with this row's staff index.
    /// </summary>
    /// <summary>
    /// Collects a melody-BOUND lyrics row: a track that <c>sings</c> a part the
    /// score does not engrave. The syllables align to the melody's notes with the
    /// SAME machinery a <c>with lyrics</c> attachment uses (melisma <c>~</c>,
    /// skip <c>_</c>, verses, per-occurrence <c>[N.]</c> brackets), and land on
    /// the row band at the melody's own timings — the caller supplies the row's
    /// measure skeleton (one spacer per melody item) so those timings are real
    /// columns the spacing solves.
    /// </summary>
    public void CollectBoundRow(
        SyntaxNode root, string trackName, int staffIndex,
        List<Measure> melody,
        IReadOnlyDictionary<string, int> sectionStartMeasure,
        IReadOnlyDictionary<string, List<int>>? sectionAllStarts)
        => CollectNoteBound(root, melody,
            lyricsRowNames: new HashSet<string>(),
            voiceMeasuresByName: new Dictionary<string, (int Index, List<Measure> Measures)>
            {
                [trackName] = (staffIndex, melody),
            },
            sectionStartMeasure, sectionAllStarts,
            onlyBlocks: new[] { trackName }, staffIndex: staffIndex, asRow: true);

    public ImmutableArray<Measure> CollectRow(
        SyntaxNode root, string partName, int staffIndex, int totalBars,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int timeBeats, int timeBeatType)
    {
        var blocks = root.KindSites(SyntaxKind.LyricsBlock).OfType<LyricsBlockSyntax>()
            .Where(b => b.VoiceName == partName).ToList();
        if (blocks.Count == 0)
            return ImmutableArray<Measure>.Empty;

        var measureItems = new Dictionary<int, ImmutableArray<MusicItem>>();
        int maxIndex = -1;
        var measureLen = new Fraction(timeBeats, timeBeatType);

        // Spreads one run of lyric-measure nodes across the bars from absolute bar
        // `startMeasure`, wrapping every `wrapBars` bars into stacked verses counted
        // from `verseBase`. Returns the bar count (for the caller's verse bookkeeping).
        int PlaceRun(IEnumerable<SyntaxNode> barNodes, int startMeasure, int wrapBars, int verseBase)
        {
            int j = 0; // run-local bar index
            bool atRunStart = true;
            foreach (var measureNode in barNodes)
            {
                // A lone bare '|' OPENING the run anchors its start (the music
                // rule) and creates no bar — skip exactly that one; an empty
                // leading bar is the explicit '| |' pair whose second survives.
                if (atRunStart)
                {
                    atRunStart = false;
                    if (LyricSyllableReader.IsLeadingAnchor(measureNode))
                        continue;
                }
                // Wrap a long run: bar j belongs to verse (j / wrapBars) and maps back
                // onto bar (j % wrapBars). wrapBars <= 0 → no wrap (one verse).
                int barInVerse = wrapBars > 0 ? j % wrapBars : j;
                int v = verseBase + (wrapBars > 0 ? j / wrapBars : 0);
                int mi = startMeasure + barInVerse;

                var sylls = RowSyllables(measureNode);
                if (sylls.Count > 0)
                {
                    var slotDur = measureLen * new Fraction(1, sylls.Count);
                    var spacers = ImmutableArray.CreateBuilder<MusicItem>(sylls.Count);
                    var timing = Fraction.Zero;
                    // ItemIndex = the syllable's slot (one spacer per syllable), so the
                    // bar gets widened for the lyric widths (SpacingRules.ApplyLyricSpacing).
                    // The engraver still takes X from Timing (IsLyricsRow path).
                    for (int k = 0; k < sylls.Count; k++)
                    {
                        var (text, conn, pos) = sylls[k];
                        _lyrics.Add(new LyricItem(
                            Text: text, MeasureIndex: mi, ItemIndex: k,
                            ConnectorType: conn, VoiceId: staffIndex, VerseNumber: v,
                            Timing: timing, SourcePosition: pos,
                            StaffIndex: staffIndex, IsLyricsRow: true));
                        spacers.Add(new RestItem(slotDur, 0, pos) { IsSpacer = true });
                        timing += slotDur;
                    }
                    // Keep the widest verse's slot count at this bar (verses usually match).
                    if (!measureItems.TryGetValue(mi, out var existing) || existing.Length < sylls.Count)
                        measureItems[mi] = spacers.MoveToImmutable();
                    maxIndex = Math.Max(maxIndex, mi);
                }
                j++;
            }
            return j;
        }

        // A run wraps within its OWN section's bars (up to the next section start) so a
        // short section's verse 2 doesn't overrun into the next section; a top-level run
        // wraps at the whole piece's length.
        int SectionWrap(int startMeasure)
        {
            int sectionEnd = totalBars;
            foreach (var st in sectionStartMeasure.Values)
                if (st > startMeasure && st < sectionEnd)
                    sectionEnd = st;
            return sectionEnd - startMeasure;
        }
        static int VersesFrom(int bars, int wrapBars) =>
            wrapBars > 0 && bars > 0 ? (bars + wrapBars - 1) / wrapBars : 1;

        int verse = 1;
        int prevSectionStart = -1;
        // Part-major sectioned tracks stack verses per named section start.
        var nextVerseBySection = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            // Part-major track (`lyrics name { section A { .. } .. }`): each inner
            // section's verse spreads across THAT named section's bars — the `section
            // NAME { … }` wrapper is structure, not literal "section"/"NAME" syllables
            // (which is what the flat reader below would otherwise emit).
            if (block.HasSections)
            {
                foreach (var section in block.Sections)
                {
                    int start = sectionStartMeasure.GetValueOrDefault(section.SectionName, 0);
                    int wrap = SectionWrap(start);
                    int vb = nextVerseBySection.TryGetValue(start, out var v0) ? v0 : 1;
                    // A row places once per section, so a per-occurrence `[N. …]` verse
                    // can't spread across passes here — take the first (and flatten the
                    // brackets so they never render as literal words).
                    int bars = PlaceRun(FirstVerse(SectionLyricMeasures(section).ToList()), start, wrap, vb);
                    nextVerseBySection[start] = vb + VersesFrom(bars, wrap);
                }
                continue;
            }

            int startMeasure = 0;
            bool inSection = false;
            for (var n = block.Parent; n != null; n = n.Parent)
            {
                if (n is SectionDeclarationSyntax section
                    && sectionStartMeasure.TryGetValue(section.SectionName, out int s))
                {
                    startMeasure = s;
                    inSection = true;
                    break;
                }
            }

            int wrapBars = inSection ? SectionWrap(startMeasure) : totalBars;

            // Verses stack within ONE section; a block in a new section restarts at
            // verse 1, so its line sits directly under that section's staff rather than
            // carrying over the previous section's verse offset.
            if (startMeasure != prevSectionStart)
            {
                verse = 1;
                prevSectionStart = startMeasure;
            }

            int barCount = PlaceRun(FirstVerse(block.Syllables.ToList()), startMeasure, wrapBars, verse);
            // The block produced ceil(barCount / wrapBars) stacked verses.
            verse += VersesFrom(barCount, wrapBars);
        }

        if (maxIndex < 0)
            return ImmutableArray<Measure>.Empty;

        var emptyBar = ImmutableArray.Create<MusicItem>(
            new RestItem(measureLen, 0, 0) { IsSpacer = true });
        var measures = ImmutableArray.CreateBuilder<Measure>(maxIndex + 1);
        for (int i = 0; i <= maxIndex; i++)
        {
            var items = measureItems.TryGetValue(i, out var it) ? it : emptyBar;
            // A lyrics row separates measures with plain `|` so a standalone lyrics-only
            // lead sheet shows a measure grid. The score-wide barline sync merges these
            // harmlessly with any music staff (which dominates via Stronger).
            // ⚠️ The last measure used to get BarlineType.Final here — removed with the
            // same rule in MeasureCollector.FinalizeMeasures: `|.` is written, not inferred.
            var end = BarlineType.Single;
            measures.Add(new Measure(items, BarlineType.None, end, null, 0, 0));
        }
        return measures.MoveToImmutable();
    }

    /// <summary>
    /// Extracts one bar's lyric syllables (text + connector) from a lyric-measure node,
    /// applying the hyphen ("a-" / "-" / "--") and extender ("~" / "_") markers — the
    /// barline token and melisma-only markers are dropped.
    /// </summary>
    private static List<(string Text, LyricConnectorType Conn, int Pos)> RowSyllables(SyntaxNode measureNode)
    {
        var result = new List<(string, LyricConnectorType, int)>();
        void SetPrevConnector(LyricConnectorType conn)
        {
            if (result.Count > 0)
                result[^1] = (result[^1].Item1, conn, result[^1].Item3);
        }

        for (int i = 0; i < measureNode.SlotCount; i++)
        {
            var child = measureNode.GetChild(i);
            if (child == null) continue;
            var (text, pos) = LyricSyllableReader.ReadToken(child);
            if (string.IsNullOrEmpty(text)) continue;

            switch (LyricSyllableReader.Classify(text))
            {
                case LyricSyllableReader.Marker.Barline:
                    break; // a measure boundary, never a word
                case LyricSyllableReader.Marker.Hyphen:
                    SetPrevConnector(LyricConnectorType.Hyphen);
                    break;
                // A row holds no notes, so an extender/melisma marker just draws an
                // extender line off the previous syllable.
                case LyricSyllableReader.Marker.Extender:
                case LyricSyllableReader.Marker.Melisma:
                    SetPrevConnector(LyricConnectorType.Extender);
                    break;
                case LyricSyllableReader.Marker.HyphenWord:
                    result.Add((LyricSyllableReader.DisplaySyllable(LyricSyllableReader.TrimHyphenWord(text)),
                        LyricConnectorType.Hyphen, pos));
                    break;
                default: // Syllable
                    result.Add((LyricSyllableReader.DisplaySyllable(text),
                        LyricConnectorType.None, pos));
                    break;
            }
        }
        return result;
    }

    /// <summary>(measureIndex, itemIndex, timing) of every note/chord (not rests) in a
    /// voice's measures — the slots a lyric line's syllables map onto. The timing
    /// (musical moment in the measure) lets a bound voice's syllable land over its real
    /// column even when that voice's rhythm differs.</summary>
    private static List<(int MeasureIndex, int ItemIndex, Fraction Timing)> BuildNoteIndices(List<Measure> measures)
    {
        var noteIndices = new List<(int MeasureIndex, int ItemIndex, Fraction Timing)>();
        for (int m = 0; m < measures.Count; m++)
        {
            var timing = Fraction.Zero;
            var items = measures[m].Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is NoteItem or ChordItem)
                    noteIndices.Add((m, i, timing));
                timing += items[i].Duration;
            }
        }
        return noteIndices;
    }

    /// <summary>The first expanded-measure index a lyrics block aligns to: the start of
    /// the section it is written in (0 when it is top-level or its section was never
    /// reached by the structure).</summary>
    private static int ResolveStartMeasure(
        LyricsBlockSyntax lyricsBlock, IReadOnlyDictionary<string, int> sectionStartMeasure)
    {
        for (var n = lyricsBlock.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section
                && sectionStartMeasure.TryGetValue(section.SectionName, out int start))
                return start;
        return 0;
    }

    /// <summary>The name of the section a flat lyrics block is written inside, or null
    /// at top level — used to repeat its verse under every occurrence of that section.</summary>
    private static string? EnclosingSectionName(LyricsBlockSyntax block)
    {
        for (var n = block.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section)
                return section.SectionName;
        return null;
    }

    /// <summary>The lyric measures of a lyric-track inner section (the nodes between
    /// its name and closing brace).</summary>
    private static IEnumerable<SyntaxNode> SectionLyricMeasures(SectionDeclarationSyntax section)
    {
        // Slots: 0 keyword, 1 name, 2 '{', 3..n-2 items, n-1 '}'.
        for (int i = 3; i < section.SlotCount - 1; i++)
            if (section.GetChild(i) is SyntaxNode node and not SyntaxTokenNode)
                yield return node;
    }
}
