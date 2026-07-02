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
/// Collects every chord-name source into <see cref="ChordNameItem"/>s: inline marks on
/// notes (<c>c:m</c>, added by the main walk via <see cref="AddInline"/>), parallel
/// <c>chordnames { … }</c> streams, and independent <c>chords name { … }</c> rows.
/// Pulled out of <see cref="MeasureCollector"/> so all chord-name logic — symbol
/// resolution and the chord-row rhythm — lives in one place. The collection-time
/// context it needs (the section→measure map, the time signature, the current staff
/// index) is passed in, as it is built by the main pass.
/// </summary>
internal sealed class ChordNameCollector
{
    private readonly List<ChordNameItem> _items = new();

    /// <summary>All collected chord-name items.</summary>
    public IReadOnlyList<ChordNameItem> Items => _items;

    /// <summary>Resets between reused collection passes.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Adds one inline chord name (a <c>c:m</c> mark on a note). The main
    /// walk parses the mark text and supplies the note's measure/item/position.</summary>
    public void AddInline(string text, int measureIndex, int itemIndex, int position, int staffIndex)
        => _items.Add(new ChordNameItem(text, measureIndex, itemIndex, position, staffIndex));

    /// <summary>
    /// Collects chord symbols from every <c>chordnames { … }</c> block. Each block is
    /// a parallel stream: entries are walked, split at barlines into measures, and each
    /// chord's start TIMING within its measure is accumulated from the entry durations
    /// (default quarter, carried). The symbol is auto-named from the resolved
    /// <see cref="LilySharp.Core.Music.ChordStructure"/>; an unknown quality token
    /// falls back to "root + raw text" so any name still displays.
    /// LILYPOND-REF: ly/engraver-init.ly ChordNames context; scm/chord-entry.scm.
    /// </summary>
    public void CollectBlocks(
        SyntaxNode root, IReadOnlyDictionary<string, int> sectionStartMeasure, int staffIndex)
    {
        // Nameless chords { … } blocks (the former chordnames keyword, folded
        // into chords pre-release): symbols align above the co-written staff.
        CollectAligned(
            root.DescendantNodes().OfType<ChordPartBlockSyntax>().Where(b => b.PartName == null),
            sectionStartMeasure, staffIndex);
    }

    /// <summary>
    /// Aligns a NAMED chord part's symbols above a staff
    /// (<c>staff NAME with chords CHORDPART</c>) — the same part can also feed a
    /// lead-sheet row, so a progression is written once and reused.
    /// </summary>
    public void CollectAttached(
        SyntaxNode root, string partName,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int staffIndex)
    {
        CollectAligned(
            root.DescendantNodes().OfType<ChordPartBlockSyntax>().Where(b => b.PartName == partName),
            sectionStartMeasure, staffIndex);
    }

    private void CollectAligned(
        IEnumerable<ChordPartBlockSyntax> alignedBlocks,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int staffIndex)
    {
        var blocks = alignedBlocks.ToList();
        if (blocks.Count == 0)
            return;

        foreach (var block in blocks)
        {
            int startMeasure = StartMeasureOf(block, sectionStartMeasure);

            int localMeasure = 0;
            var timing = Fraction.Zero;
            var defaultDuration = Fraction.Quarter;

            foreach (var item in block.Items)
            {
                if (item is BarlineSyntax)
                {
                    localMeasure++;
                    timing = Fraction.Zero;
                    continue;
                }
                if (item is not ChordEntrySyntax entry)
                    continue;

                var (text, structure) = ResolveChordEntry(entry);
                _items.Add(new ChordNameItem(
                    text,
                    startMeasure + localMeasure,
                    itemIndex: -1,
                    entry.Root.Position,
                    staffIndex,
                    useTiming: true,
                    timing: timing,
                    structure: structure));

                var dur = entry.Duration?.ToFraction() ?? defaultDuration;
                if (entry.Duration != null)
                    defaultDuration = dur;
                timing += dur;
            }
        }
    }

    /// <summary>
    /// Collects independent chord parts (<c>chords name { … }</c>) into chord-name
    /// items. Unlike <c>chordnames</c>, a chord part fills each measure by the
    /// per-count default rhythm table (<see cref="ChordRhythm"/>) when the user omits
    /// durations. If ANY chord in a measure carries an explicit duration that measure
    /// is "explicit mode": unspecified chords carry the previous duration forward
    /// (note-like), so the last chord absorbs whatever fills the bar.
    /// </summary>
    /// <returns>
    /// The chord row's measure skeleton: one measure per bar, each filled with
    /// invisible spacer rests at the chord rhythm. The rests are never drawn (the
    /// renderer skips chord rows) but give the layout timing columns so the bar gets
    /// width even with no music staff (standalone lead sheet).
    /// </returns>
    public ImmutableArray<Measure> CollectPart(
        SyntaxNode root, string partName, int staffIndex,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int timeBeats, int timeBeatType)
    {
        var blocks = root.DescendantNodes().OfType<ChordPartBlockSyntax>()
            .Where(b => b.PartName == partName).ToList();
        if (blocks.Count == 0)
            return ImmutableArray<Measure>.Empty;

        var measureItems = new Dictionary<int, ImmutableArray<MusicItem>>();
        // Source barline types per measure. A chord row carries its own `|`, `:|`,
        // `||` etc. so a standalone lead sheet draws a real measure grid (and the
        // score-wide barline sync propagates a repeat/final set here into the music
        // staves of a mixed score, exactly as for a written staff). End barline
        // defaults to Single (a drawn boundary); the last bar is finalised below.
        var measureStartBar = new Dictionary<int, BarlineType>();
        var measureEndBar = new Dictionary<int, BarlineType>();
        int maxIndex = -1;

        foreach (var block in blocks)
        {
            int startMeasure = StartMeasureOf(block, sectionStartMeasure);

            int localMeasure = 0;
            var pending = new List<ChordEntrySyntax>();
            var pendingStart = BarlineType.None;

            // Close the current measure with the given end barline (None = trailing
            // measure, no written closer → defaults to Single in the build below).
            void Commit(BarlineType endBar)
            {
                int mi = startMeasure + localMeasure;
                if (pending.Count > 0)
                {
                    measureItems[mi] = EmitChordPartMeasure(pending, mi, staffIndex, timeBeats, timeBeatType);
                    maxIndex = Math.Max(maxIndex, mi);
                }
                if (pendingStart != BarlineType.None)
                    measureStartBar[mi] = pendingStart;
                if (endBar != BarlineType.None)
                    measureEndBar[mi] = endBar;
                pendingStart = BarlineType.None;
                localMeasure++;
                pending.Clear();
            }

            foreach (var item in block.Items)
            {
                if (item is BarlineSyntax bar)
                {
                    var t = MeasureCollector.ParseBarlineType(bar.BarToken.Text);
                    if (t == BarlineType.RepeatStart)
                    {
                        // |: opens the NEXT measure; close anything pending first.
                        if (pending.Count > 0)
                            Commit(BarlineType.Single);
                        pendingStart = BarlineType.RepeatStart;
                    }
                    else if (t == BarlineType.RepeatBoth)
                    {
                        // :|: ends this measure AND opens the next.
                        Commit(BarlineType.RepeatEnd);
                        pendingStart = BarlineType.RepeatStart;
                    }
                    else
                    {
                        Commit(t);
                    }
                }
                else if (item is ChordEntrySyntax entry)
                {
                    pending.Add(entry);
                }
            }
            // A trailing measure with no closing barline still counts.
            if (pending.Count > 0 || pendingStart != BarlineType.None)
                Commit(BarlineType.None);
        }

        if (maxIndex < 0)
            return ImmutableArray<Measure>.Empty;

        // An empty bar (written as a bare "|") gets one whole-measure spacer rest so it
        // keeps its width even with no music staff (standalone lead sheet), matching
        // how empty lyric measures work.
        var emptyBar = ImmutableArray.Create<MusicItem>(
            new RestItem(new Fraction(timeBeats, timeBeatType), 0, 0) { IsSpacer = true });

        var measures = ImmutableArray.CreateBuilder<Measure>(maxIndex + 1);
        for (int i = 0; i <= maxIndex; i++)
        {
            var items = measureItems.TryGetValue(i, out var it) ? it : emptyBar;
            var start = measureStartBar.GetValueOrDefault(i, BarlineType.None);
            var end = measureEndBar.GetValueOrDefault(i, BarlineType.Single);
            measures.Add(new Measure(items, start, end, null, 0, 0));
        }
        // Auto-set the final barline on the last measure (music convention), matching
        // MeasureCollector — a written end-repeat / double bar there is kept.
        var lastMeasure = measures[maxIndex];
        if (lastMeasure.EndBarline == BarlineType.Single)
            measures[maxIndex] = new Measure(
                lastMeasure.Items, lastMeasure.StartBarline, BarlineType.Final, null, 0, 0);
        return measures.MoveToImmutable();
    }

    /// <summary>
    /// Emits the chord-name items for one measure of a chord part and returns the
    /// matching invisible spacer rests. Each chord's start timing / duration comes from
    /// the default rhythm table (no explicit durations) or, in "explicit mode" (any
    /// explicit duration present), carry-forward explicit durations.
    /// </summary>
    private ImmutableArray<MusicItem> EmitChordPartMeasure(
        List<ChordEntrySyntax> entries, int measureIndex, int staffIndex, int timeBeats, int timeBeatType)
    {
        bool anyExplicit = entries.Any(e => e.Duration != null);
        ImmutableArray<Fraction>? table = anyExplicit
            ? null
            : ChordRhythm.DefaultDurations(entries.Count, timeBeats, timeBeatType);

        var rests = ImmutableArray.CreateBuilder<MusicItem>(entries.Count);
        var timing = Fraction.Zero;
        var carry = Fraction.Quarter;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var (text, structure) = ResolveChordEntry(entry);
            _items.Add(new ChordNameItem(
                text, measureIndex, itemIndex: -1, entry.Root.Position,
                staffIndex: staffIndex, useTiming: true, timing: timing, structure: structure,
                isChordRow: true));

            Fraction dur;
            if (entry.Duration != null)
            {
                dur = entry.Duration.ToFraction();
                carry = dur;
            }
            else if (table != null && i < table.Value.Length)
            {
                dur = table.Value[i];
            }
            else
            {
                dur = carry; // explicit-mode remainder, or unsupported meter/count
            }
            // BaseDuration = the resolved fraction (Dots 0): only its Duration drives
            // the spacing spring, so a dotted/compound value spaces correctly too.
            rests.Add(new RestItem(dur, 0, entry.Root.Position) { IsSpacer = true });
            timing += dur;
        }
        return rests.MoveToImmutable();
    }

    /// <summary>The first measure of the section a block sits in (0 at top level), so a
    /// chord block inside section B starts under B's bars.</summary>
    private static int StartMeasureOf(SyntaxNode block, IReadOnlyDictionary<string, int> sectionStartMeasure)
    {
        for (var n = block.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section
                && sectionStartMeasure.TryGetValue(section.SectionName, out int s))
                return s;
        return 0;
    }

    /// <summary>
    /// Resolves a chord entry to its display text and (when the quality is known) its
    /// structure. The root step comes from the pitch letter, the alteration from its
    /// accidental; an unrecognized quality token is shown verbatim.
    /// </summary>
    private static (string Text, LilySharp.Core.Music.ChordStructure? Structure) ResolveChordEntry(ChordEntrySyntax entry)
    {
        int rootStep = "cdefgab".IndexOf(entry.Root.BaseName);
        int rootAlter = entry.Root.AccidentalOffset;
        int? bassStep = null, bassAlter = null;
        if (entry.Bass is { } bass)
        {
            bassStep = "cdefgab".IndexOf(bass.BaseName);
            bassAlter = bass.AccidentalOffset;
        }

        if (rootStep >= 0 && LilySharp.Core.Music.ChordQualityRegistry.TryResolve(entry.QualityText, out var quality))
        {
            var structure = new LilySharp.Core.Music.ChordStructure(
                rootStep, rootAlter, quality, bassStep, bassAlter);
            return (structure.DisplayName, structure);
        }

        // Unknown quality (e.g. an extended chord not in the vocabulary): show the root
        // + the raw token text so the name still displays, but carry no structure (no
        // interval set for future notes/fret diagrams).
        var sb = new System.Text.StringBuilder();
        sb.Append(rootStep >= 0
            ? LilySharp.Core.Music.ChordStructure.SpellPitch(rootStep, rootAlter)
            : entry.Root.PitchName);
        sb.Append(entry.QualityText);
        if (bassStep is int bs)
            sb.Append('/').Append(LilySharp.Core.Music.ChordStructure.SpellPitch(bs, bassAlter ?? 0));
        return (sb.ToString(), null);
    }
}
