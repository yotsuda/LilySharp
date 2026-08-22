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
    private readonly List<ChordRowGridWarning> _gridWarnings = new();

    /// <summary>What the row's grid walk recorded as a side effect — read back by
    /// <c>ChordRowGridValidator</c> (the BeamPairingValidator pattern: the warning
    /// can never disagree with what is drawn, because the walk that draws is the
    /// walk that recorded it).</summary>
    public IReadOnlyList<ChordRowGridWarning> GridWarnings => _gridWarnings;

    /// <summary>The key timeline for Roman-numeral degrees: (start measure, tonic step
    /// 0=C..6=B, signature ±sharps) sorted ascending, so a chord's degree follows the
    /// key in force at its bar (a mid-piece modulation re-bases the degrees). Set by
    /// the collector before use; defaults to C major.</summary>
    public IReadOnlyList<(int Measure, int TonicStep, int Sharps)> KeyByMeasure { get; set; }
        = new[] { (0, 0, 0) };

    /// <summary>The (tonic step, sharps) in force at <paramref name="measure"/> — the
    /// last timeline entry that begins at or before it.</summary>
    private (int TonicStep, int Sharps) KeyAt(int measure)
    {
        (int TonicStep, int Sharps) eff = (0, 0);
        foreach (var e in KeyByMeasure)
        {
            if (e.Measure <= measure) eff = (e.TonicStep, e.Sharps);
            else break;
        }
        return eff;
    }

    /// <summary>The chord's Roman degree in the key at <paramref name="measure"/>, or
    /// null with no resolved structure.</summary>
    private string? Roman(LilySharp.Core.Music.ChordStructure? structure, int measure)
    {
        if (structure == null)
            return null;
        var (tonicStep, sharps) = KeyAt(measure);
        return structure.ToRomanNumeral(tonicStep, sharps);
    }

    /// <summary>EVERY start measure of each section across the structure, so a chord
    /// track repeats under a reprise (A played again as "A2" gets its chords again).
    /// Null falls back to the single first-occurrence start.</summary>
    public IReadOnlyDictionary<string, List<int>>? SectionStarts { get; set; }

    /// <summary>All collected chord-name items.</summary>
    public IReadOnlyList<ChordNameItem> Items => _items;

    /// <summary>The mutable list, for the checkpoint/resume probe only — inline
    /// <c>@chord</c> items are appended DURING the primary walk, so a resumed walk
    /// adopts this table's prefix like the collector's own lists
    /// (<c>MeasureCollector.CumulativeSideTables</c>).</summary>
    internal List<ChordNameItem> ItemsList => _items;

    /// <summary>Resets between reused collection passes.</summary>
    public void Clear()
    {
        _items.Clear();
        _gridWarnings.Clear();
    }

    /// <summary>Adds one inline chord name (a <c>@chord(c:m)</c> mark on a note). The
    /// main walk parses the mark text/structure and supplies the note's
    /// measure/item/position. The structure lets it render as a Roman degree if the
    /// staff's attached chords are shown that way (see <see cref="ApplyDisplayMode"/>).</summary>
    public void AddInline(string text, int measureIndex, int itemIndex, int position, int staffIndex,
        LilySharp.Core.Music.ChordStructure? structure = null)
        => _items.Add(new ChordNameItem(text, measureIndex, itemIndex, position, staffIndex,
            structure: structure));

    /// <summary>Applies a display mode to the INLINE <c>@chord</c> symbols already
    /// collected on a staff (aligned/row items already carry their own mode). Called
    /// when a staff attaches chords <c>as roman|both</c>, so an inline mark on the same
    /// staff shows the same way instead of clashing with the track's symbol.</summary>
    public void ApplyDisplayMode(int staffIndex, ChordDisplayMode mode)
    {
        if (mode == ChordDisplayMode.Names)
            return;
        for (int k = 0; k < _items.Count; k++)
        {
            var it = _items[k];
            // Inline marks are placed by item index (UseTiming false); aligned/row
            // symbols use timing and already carry the mode.
            if (it.StaffIndex == staffIndex && !it.UseTiming)
                _items[k] = it with
                {
                    DisplayMode = mode,
                    RomanText = Roman(it.Structure, it.MeasureIndex),
                };
        }
    }

    // (CollectBlocks — the nameless `chords { }` auto-attach — was removed with the
    // form itself, LYS0032: a chord part renders only where a score places it.)

    /// <summary>
    /// Aligns a NAMED chord part's symbols above a staff
    /// (<c>staff NAME with chords CHORDPART</c>) — the same part can also feed a
    /// lead-sheet row, so a progression is written once and reused.
    /// </summary>
    public void CollectAttached(
        SyntaxNode root, string partName,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int staffIndex,
        int timeBeats, int timeBeatType,
        ChordDisplayMode mode = ChordDisplayMode.Names)
    {
        CollectAligned(
            root.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>()
                .Where(b => b.PartName == partName),
            sectionStartMeasure, staffIndex, mode, timeBeats, timeBeatType);
        // An inline @chord on this staff should follow the same display as the track.
        ApplyDisplayMode(staffIndex, mode);
    }

    private void CollectAligned(
        IEnumerable<ChordPartBlockSyntax> alignedBlocks,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int staffIndex, ChordDisplayMode mode,
        int timeBeats, int timeBeatType)
    {
        var blocks = alignedBlocks.ToList();
        if (blocks.Count == 0)
            return;

        foreach (var block in blocks)
        {
            // Part-major chord track: each inner section's chords align under its own
            // named section's bars — at EVERY occurrence (a reprise gets them too).
            // Flat form: the whole block aligns under the section it is written inside.
            if (block.HasSections)
                foreach (var section in block.Sections)
                    foreach (int start in StartsFor(section.SectionName, sectionStartMeasure))
                        CollectAlignedItems(SectionItems(section), start, staffIndex, mode, timeBeats, timeBeatType);
            else
                foreach (int start in BlockStarts(block, sectionStartMeasure))
                    CollectAlignedItems(block.Items, start, staffIndex, mode, timeBeats, timeBeatType);
        }
    }

    /// <summary>Every start measure a section occupies (each structure replay), or the
    /// single first-occurrence anchor when the all-starts map is absent/empty.</summary>
    private IEnumerable<int> StartsFor(string sectionName, IReadOnlyDictionary<string, int> single)
    {
        if (SectionStarts != null && SectionStarts.TryGetValue(sectionName, out var all) && all.Count > 0)
            return all;
        return new[] { single.GetValueOrDefault(sectionName, 0) };
    }

    /// <summary>Start measures for a FLAT (in-section) chord block: every occurrence of
    /// its enclosing section, or 0 at top level.</summary>
    private IEnumerable<int> BlockStarts(SyntaxNode block, IReadOnlyDictionary<string, int> single)
    {
        for (var n = block.Parent; n != null; n = n.Parent)
            if (n is SectionDeclarationSyntax section)
                return StartsFor(section.SectionName, single);
        return new[] { 0 };
    }

    private void CollectAlignedItems(IEnumerable<SyntaxNode> items, int startMeasure, int staffIndex,
        ChordDisplayMode mode, int timeBeats, int timeBeatType)
    {
        int localMeasure = 0;
        var pending = new List<SyntaxNode>();

        // One bar's slots, placed on the meter's beat grid. r / R print the
        // no-chord symbol at their slot; s (a skip) prints nothing; a '.' prints
        // nothing (the previous slot's symbol holds). All occupy their slot.
        // LILYPOND-REF: scm/scheme-engravers.scm:1520-1527 Current_chord_text_engraver
        //   — general-rest-event (r and R, not s) → currentChordText = noChordSymbol;
        // LILYPOND-REF: ly/engraver-init.ly:952 noChordSymbol = "N.C.", below ignatzek-chord-names.
        void Commit()
        {
            if (pending.Count == 0)
                return;
            int mi = startMeasure + localMeasure;
            ForEachSlotGroup(pending, timeBeats, timeBeatType, (node, timing, _) =>
            {
                if (node is RestSyntax rest)
                {
                    if (rest.RestToken.Text != "s")
                        _items.Add(new ChordNameItem(
                            "N.C.", mi, itemIndex: -1, rest.RestToken.Position, staffIndex,
                            useTiming: true, timing: timing));
                }
                else if (node is ChordEntrySyntax entry)
                {
                    var (text, structure) = ResolveChordEntry(entry);
                    _items.Add(new ChordNameItem(
                        text, mi, itemIndex: -1, entry.Position, staffIndex,
                        useTiming: true, timing: timing, structure: structure)
                    {
                        RomanText = Roman(structure, mi),
                        DisplayMode = mode,
                    });
                }
            });
            pending.Clear();
        }

        foreach (var item in items)
        {
            if (item is BarlineSyntax)
            {
                Commit();
                localMeasure++;
                continue;
            }
            if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                pending.Add(item);
        }
        Commit();
    }

    /// <summary>
    /// Walks ONE BAR's written slots — entries, rests, '.' extensions — on the
    /// meter's beat grid: each entry/rest opens a group that its trailing '.'
    /// slots extend, and <paramref name="emit"/> receives (node, start timing,
    /// merged duration) per group. A slot count that fits no grid shape falls
    /// back to equal division; that, and a '.' at the bar's head (its own silent
    /// group — the time still passes), are recorded in <see cref="GridWarnings"/>
    /// for ChordRowGridValidator to surface.
    /// </summary>
    private void ForEachSlotGroup(List<SyntaxNode> items, int timeBeats, int timeBeatType,
        Action<SyntaxNode, Fraction, Fraction> emit)
    {
        int slotCount = items.Count;
        var slots = ChordRhythm.SlotDurations(slotCount, timeBeats, timeBeatType);
        if (slots == null)
        {
            _gridWarnings.Add(new ChordRowGridWarning(
                PositionOf(items[0]), HeadDot: false, slotCount, timeBeats, timeBeatType));
            var equal = new Fraction(timeBeats, timeBeatType) * new Fraction(1, slotCount);
            var eq = System.Collections.Immutable.ImmutableArray.CreateBuilder<Fraction>(slotCount);
            for (int i = 0; i < slotCount; i++)
                eq.Add(equal);
            slots = eq.MoveToImmutable();
        }

        var timing = Fraction.Zero;
        int at = 0;
        while (at < slotCount)
        {
            var node = items[at];
            var dur = slots.Value[at];
            int next = at + 1;
            while (next < slotCount && items[next] is ChordExtendSyntax)
            {
                dur += slots.Value[next];
                next++;
            }
            if (node is ChordExtendSyntax head)
                // Nothing before it in THIS bar to extend ('.' never crosses a
                // barline). The group stays silent but keeps its time.
                _gridWarnings.Add(new ChordRowGridWarning(
                    head.DotToken.Position, HeadDot: true, slotCount, timeBeats, timeBeatType));
            emit(node, timing, dur);
            timing += dur;
            at = next;
        }
    }

    private static int PositionOf(SyntaxNode node) => node switch
    {
        RestSyntax rest => rest.RestToken.Position,
        ChordExtendSyntax dot => dot.DotToken.Position,
        _ => node.Position,
    };

    /// <summary>The chord entries and barlines of a chord-track inner section (the
    /// nodes between its name and closing brace).</summary>
    private static IEnumerable<SyntaxNode> SectionItems(SectionDeclarationSyntax section)
    {
        // Slots: 0 keyword, 1 name, 2 '{', 3..n-2 items, n-1 '}'.
        for (int i = 3; i < section.SlotCount - 1; i++)
            if (section.GetChild(i) is SyntaxNode node and not SyntaxTokenNode)
                yield return node;
    }

    /// <summary>
    /// Collects independent chord parts (<c>chords name { … }</c>) into chord-name
    /// items. A chord row is measure-relative (GRAMMAR_AUDIT 8.1): each bar's
    /// written slots — entries, rests, '.' extensions — divide it on the meter's
    /// beat grid (<see cref="ChordRhythm.SlotDurations"/>); no durations are
    /// written.
    /// </summary>
    /// <returns>
    /// The chord row's measure skeleton: one measure per bar, each filled with
    /// invisible spacer rests at the chord rhythm. The rests are never drawn (the
    /// renderer skips chord rows) but give the layout timing columns so the bar gets
    /// width even with no music staff (standalone lead sheet).
    /// </returns>
    /// <summary>Bar count of a chord part block: one per written barline —
    /// except a lone bare '|' OPENING the run, which only anchors it — plus a
    /// trailing bar when entries follow the last barline (the same
    /// segmentation CollectPart commits by).</summary>
    public static int CountBars(ChordPartBlockSyntax block)
        => CountBars(block.Items);

    /// <summary>Bar count of a part-major chord-track inner section
    /// (<c>chords X { section NAME { … } }</c>): its chords sit directly in the section, so
    /// count them there rather than in a nested chord block.</summary>
    public static int CountSectionBars(SectionDeclarationSyntax section)
        => CountBars(SectionItems(section));

    private static int CountBars(IEnumerable<SyntaxNode> items)
    {
        int bars = 0;
        bool pendingEntries = false;
        bool atRunStart = true;
        foreach (var item in items)
        {
            if (item is BarlineSyntax bar)
            {
                // Mirror ProcessRun: the leading anchor bar counts nothing.
                // Any drift here pads the row grid with a phantom bar.
                if (atRunStart && bar.BarToken.Text == "|")
                {
                    atRunStart = false;
                    continue;
                }
                atRunStart = false;
                bars++;
                pendingEntries = false;
            }
            else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
            {
                atRunStart = false;
                pendingEntries = true;
            }
        }
        return bars + (pendingEntries ? 1 : 0);
    }

    public ImmutableArray<Measure> CollectPart(
        SyntaxNode root, string partName, int staffIndex,
        IReadOnlyDictionary<string, int> sectionStartMeasure, int timeBeats, int timeBeatType,
        ChordDisplayMode mode = ChordDisplayMode.Names)
    {
        var blocks = root.KindSites(SyntaxKind.ChordPartBlock).OfType<ChordPartBlockSyntax>()
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

        // Process one run of chord items (flat block, or one inner section) whose
        // first bar sits at absolute measure <paramref>startMeasure</paramref>.
        void ProcessRun(IEnumerable<SyntaxNode> items, int startMeasure)
        {
            int localMeasure = 0;
            // Chord entries AND rests: r / R print the no-chord symbol at their
            // moment, s prints nothing; all three advance the timing like an entry
            // (EmitChordPartMeasure). Same rule as the attached path.
            // LILYPOND-REF: scm/scheme-engravers.scm:1520-1527 Current_chord_text_engraver
            //   — general-rest-event (r and R, not s) → currentChordText = noChordSymbol.
            var pending = new List<SyntaxNode>();
            var pendingStart = BarlineType.None;

            // Close the current measure with the given end barline (None = trailing
            // measure, no written closer → defaults to Single in the build below).
            void Commit(BarlineType endBar)
            {
                int mi = startMeasure + localMeasure;
                if (pending.Count > 0)
                {
                    measureItems[mi] = EmitChordPartMeasure(pending, mi, staffIndex, timeBeats, timeBeatType, mode);
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

            bool atRunStart = true;
            foreach (var item in items)
            {
                if (item is BarlineSyntax bar)
                {
                    // A lone bare '|' OPENING the run anchors its start (the
                    // bare-barline rule; same as music and lyrics) and creates
                    // no bar — '| c1 | f1 |' == 'c1 | f1 |'; an empty leading
                    // bar is the explicit '| |' pair, whose second bar commits.
                    if (atRunStart && bar.BarToken.Text == "|")
                    {
                        atRunStart = false;
                        continue;
                    }
                    atRunStart = false;
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
                else if (item is ChordEntrySyntax or RestSyntax or ChordExtendSyntax)
                {
                    atRunStart = false;
                    pending.Add(item);
                }
            }
            // A trailing measure with no closing barline still counts.
            if (pending.Count > 0 || pendingStart != BarlineType.None)
                Commit(BarlineType.None);
        }

        foreach (var block in blocks)
        {
            // Part-major chord track: each inner section fills its own named section's
            // bars, at EVERY occurrence. Flat form: the enclosing section's occurrences.
            if (block.HasSections)
                foreach (var section in block.Sections)
                    foreach (int start in StartsFor(section.SectionName, sectionStartMeasure))
                        ProcessRun(SectionItems(section), start);
            else
                foreach (int start in BlockStarts(block, sectionStartMeasure))
                    ProcessRun(block.Items, start);
        }

        if (maxIndex < 0)
            return ImmutableArray<Measure>.Empty;

        // An empty bar (the explicit "| |" pair) gets one whole-measure spacer rest so
        // it keeps its width even with no music staff (standalone lead sheet), matching
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
        // ⚠️ NO AUTOMATIC FINAL BARLINE — the same rule (and the same removal) as
        // MeasureCollector.FinalizeMeasures: `|.` is written, never inferred. A chord row
        // that stamped Final here also dominated the score-wide barline merge, so it put a
        // final barline on the music staff of any lead sheet.
        return measures.MoveToImmutable();
    }

    /// <summary>
    /// Emits the chord-name items for one measure of a chord part and returns the
    /// matching invisible spacer rests. Timing comes from the bar's slot grid
    /// (<see cref="ChordRhythm.SlotDurations"/>): each entry/rest takes its slot
    /// plus its trailing '.' extensions, merged into one spacer, so <c>| C . . G7 |</c>
    /// carries two spacers (a dotted half and a quarter) just as the explicit
    /// durations used to.
    /// </summary>
    private ImmutableArray<MusicItem> EmitChordPartMeasure(
        List<SyntaxNode> entries, int measureIndex, int staffIndex, int timeBeats, int timeBeatType,
        ChordDisplayMode mode = ChordDisplayMode.Names)
    {
        var rests = ImmutableArray.CreateBuilder<MusicItem>();
        ForEachSlotGroup(entries, timeBeats, timeBeatType, (node, timing, dur) =>
        {
            int position = PositionOf(node);
            if (node is ChordEntrySyntax entry)
            {
                var (text, structure) = ResolveChordEntry(entry);
                _items.Add(new ChordNameItem(
                    text, measureIndex, itemIndex: -1, position,
                    staffIndex: staffIndex, useTiming: true, timing: timing, structure: structure,
                    isChordRow: true)
                {
                    RomanText = Roman(structure, measureIndex),
                    DisplayMode = mode,
                });
            }
            else if (node is RestSyntax rest && rest.RestToken.Text != "s")
            {
                // r / R print "N.C." at their slot; s prints nothing. All three
                // occupy it (see ProcessRun's remark). A bar-head '.' group prints
                // nothing either (recorded by the grid walk, LYS2010).
                _items.Add(new ChordNameItem(
                    "N.C.", measureIndex, itemIndex: -1, position,
                    staffIndex: staffIndex, useTiming: true, timing: timing,
                    isChordRow: true));
            }
            // BaseDuration = the resolved fraction (Dots 0): only its Duration drives
            // the spacing spring, so a dotted/compound value spaces correctly too.
            rests.Add(new RestItem(dur, 0, position) { IsSpacer = true });
        });
        return rests.ToImmutable();
    }

    /// <summary>
    /// Resolves a chord entry — the printed symbol its token run spells — to its
    /// display text and (when the quality is registered) its structure, through
    /// the ONE string grammar <c>@chord</c> uses too
    /// (<see cref="Music.ChordStructure.TryParseChordEntry"/>). An unregistered
    /// quality on a valid root keeps the raw suffix: the interval set is unknown
    /// (no note expansion), but the root resolves to a Roman degree, so
    /// <c>Cm13</c> shows "Cm13" / "Im13" instead of an un-converted literal.
    /// </summary>
    private static (string Text, LilySharp.Core.Music.ChordStructure? Structure) ResolveChordEntry(ChordEntrySyntax entry)
    {
        string symbol = entry.SymbolText;
        if (LilySharp.Core.Music.ChordStructure.TryParseChordEntry(symbol, out var parsed))
            return (parsed.DisplayName, parsed);

        int slash = symbol.IndexOf('/');
        string main = slash >= 0 ? symbol[..slash] : symbol;
        string? bassText = slash >= 0 ? symbol[(slash + 1)..] : null;
        if (LilySharp.Core.Music.ChordStructure.TryParseSymbolPitch(main, out int step, out int alter, out string qual))
        {
            int? bassStep = null, bassAlter = null;
            if (bassText != null
                && LilySharp.Core.Music.ChordStructure.TryParseSymbolPitch(
                    bassText, out int bs, out int ba, out string bassRest)
                && bassRest.Length == 0)
            {
                bassStep = bs;
                bassAlter = ba;
            }
            var raw = new LilySharp.Core.Music.ChordStructure(
                step, alter, LilySharp.Core.Music.ChordQuality.Major,
                bassStep, bassAlter, RawSuffix: qual);
            return (raw.DisplayName, raw);
        }

        // Unparseable root — show the raw run with no structure at all.
        return (symbol, null);
    }
}

/// <summary>One grid fault a chord-row walk recorded, surfaced by
/// <c>ChordRowGridValidator</c>: a bar whose slot count fits no beat-grid shape
/// (<see cref="HeadDot"/> false — the bar fell back to equal division, LYS2009),
/// or a '.' at the head of a bar with nothing to extend (<see cref="HeadDot"/>
/// true, LYS2010).</summary>
public readonly record struct ChordRowGridWarning(
    int SourcePosition, bool HeadDot, int SlotCount, int Beats, int BeatType);
