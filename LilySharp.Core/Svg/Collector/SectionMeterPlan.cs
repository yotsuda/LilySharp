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

using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// THE SCORE'S METER inside each section: a <c>time</c> written in ONE part's music is the
/// meter of every part from that bar of the section on (owner's decision 2026-10-06, HANDOFF
/// §2 F-partmeter ⒜ — "to let a part be written in pieces, this has to be accepted"; it is
/// LilyPond's Timing, which lives in the Score). A part that writes nothing in the bar — a
/// section it does not write, the bars it is short of — is padded with bars of THAT meter
/// and shows the change on its staff.
/// </summary>
/// <remarks>
/// Read off the same semantic voices as the bar counts (<see cref="SectionBarCounts.SemanticVoices"/>),
/// each split by <see cref="MeasureModel.Split"/>: the changes a voice writes at the START of
/// a bar of its section (before any of that bar's music), by bar index within the section.
/// A section resets the meter (to its header's, else the score's), so a change never
/// outlives its section. Where two voices write different meters at the same bar, the first
/// in document order stands (<see cref="Conflicts"/>). A <c>time</c> written after music in
/// its bar stays its own voice's. Empty — and free — for a book that writes no <c>time</c>
/// inside music at all, which is nearly every book.
/// </remarks>
internal sealed class SectionMeterPlan
{
    public static readonly SectionMeterPlan Empty = new();

    /// <summary>Per section name: bar index within the section → the change in force from it —
    /// the changes some part does NOT write itself (what a reader of that part applies).</summary>
    private readonly Dictionary<string, SortedDictionary<int, TimeSignatureSyntax>> _changes = new(StringComparer.Ordinal);

    /// <summary>Per section name: EVERY change at a bar's start, own or foreign (first voice in
    /// document order at a bar) — the score's meter bar by bar, for a reader that is not a
    /// part's walk: a chord row's bars (<see cref="MeterOfBar"/>). Kept when
    /// <see cref="_changes"/> is emptied because every part writes every change itself.</summary>
    private readonly Dictionary<string, SortedDictionary<int, TimeSignatureSyntax>> _timeline = new(StringComparer.Ordinal);

    /// <summary>Per voice container: the bars at whose start it writes a <c>time</c> itself.</summary>
    private readonly Dictionary<SyntaxNode, HashSet<int>> _own = new(ReferenceEqualityComparer.Instance);

    /// <summary>A voice's change that differs from the one standing at the same bar.</summary>
    public List<(TimeSignatureSyntax Lost, TimeSignatureSyntax Standing, string Section, int Bar)> Conflicts { get; } = new();

    public bool IsEmpty => _changes.Count == 0;

    // The book's phrase bodies, as Build read them (BarsOfItems splits a voice again by them).
    private IReadOnlyDictionary<string, SyntaxNode> _phrases = new Dictionary<string, SyntaxNode>();

    /// <summary>The bar of its section each item written at the top of <paramref name="container"/>'s
    /// music lands in, by the item's source start — by <see cref="MeasureModel.Split"/>, the model
    /// the plan's own bars were counted with.</summary>
    /// <remarks>For a reader that walks a voice's WRITTEN bars and asks the plan bar by bar
    /// (<see cref="WithForeignChanges"/>, <c>MeasureValidator</c>): an <c>R*N</c> is N bars in one
    /// token with one bar line after it, and a count of bar lines was N−1 short from there on.
    /// A <c>time</c> another part wrote after such a rest reached this part a bar or more late —
    /// "Measure duration 1/2 is less than time signature 5/8" on a correct bar, and in the MIDI
    /// a bare <c>R</c> four beats long under a 2/4 the page showed (grammar evaluation 3,
    /// 2026-11-13). The collector counts the bars it builds and was right.</remarks>
    public Dictionary<int, int> BarsOfItems(string section, SyntaxNode container)
    {
        var index = new Dictionary<(string Chain, int Position), int>();
        var scope = Normalize(container);
        MeasureModel.Split(scope, _phrases, null, null, bar => ForeignChangeAt(section, bar, scope), index: index);
        var bars = new Dictionary<int, int>();
        foreach (var ((chain, position), bar) in index)
            if (chain.Length == 0)
                bars[position] = bar;
        return bars;
    }

    /// <summary>The bars a written item is beyond its first: an <c>R*N</c>'s or a <c>%N</c>'s
    /// N−1, else none — what a count of bar lines misses until the next item
    /// <see cref="BarsOfItems"/> places.</summary>
    public static int BarsBeyondTheFirst(SyntaxNode item) => item switch
    {
        RestSyntax { RestText: "R" } rest => Math.Max(1, rest.MeasureCount) - 1,
        PercentMarkSyntax mark => Math.Max(1, mark.Bars) - 1,
        _ => 0,
    };

    public static SectionMeterPlan Build(SyntaxNode root, IReadOnlyDictionary<string, SyntaxNode>? phraseBodies = null)
    {
        if (!WritesTimeInMusic(root))
            return Empty;
        var phrases = phraseBodies ?? SectionBarCounts.PhraseBodies(root);
        var plan = new SectionMeterPlan { _phrases = phrases };
        // Which parts write each section, to tell a change every part writes itself (the
        // common spelling — nothing to apply, and the plan stays Empty) from one some part
        // does not.
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.SitesOfKinds(SyntaxKind.PartDeclaration, SyntaxKind.PartBlock))
            if (PartNameOf(n) is { } name)
                parts.Add(name);
        var ownByPart = new Dictionary<(string Section, string Part), HashSet<int>>();
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
        {
            if (voice.IsChords || voice.IsLyrics)
                continue;
            var written = new List<(int Bar, TimeSignatureSyntax Time, bool MidBar)>();
            var bars = MeasureModel.Split(voice.Container, phrases, null, written);
            if (written.Count == 0)
                continue;
            plan.NoteUnmeteredBars(voice.SectionName, bars, written);
            if (!plan._changes.TryGetValue(voice.SectionName, out var changes))
                plan._changes[voice.SectionName] = changes = new SortedDictionary<int, TimeSignatureSyntax>();
            var own = new HashSet<int>();
            foreach (var (bar, time, midBar) in written)
            {
                if (midBar)
                    continue; // after music in its bar: the voice's own
                own.Add(bar);
                if (changes.TryGetValue(bar, out var standing))
                {
                    if (!SameMeter(standing, time))
                        plan.Conflicts.Add((time, standing, voice.SectionName, bar));
                    continue;
                }
                changes[bar] = time;
            }
            plan._own[voice.Container] = own;
            // The single-part shorthand (music straight in a top-level section) is the lone
            // part's, when the book has one.
            if ((PartNameOf(voice.Container) ?? (parts.Count == 1 ? parts.First() : null)) is { } part)
                ownByPart[(voice.SectionName, part)] = own;
        }
        foreach (var (section, changes) in plan._changes)
            plan._timeline[section] = changes;
        foreach (var (section, changes) in plan._changes)
            foreach (var bar in changes.Keys)
                foreach (var part in parts)
                    if (!ownByPart.TryGetValue((section, part), out var own) || !own.Contains(bar))
                        return plan;
        // Every part writes every change itself: nothing to apply (IsEmpty) — but a conflict is
        // still the validator's to name, and the timeline still answers a chord row's bars.
        plan._changes.Clear();
        return plan;
    }

    /// <summary>
    /// THE SCORE'S METER at <paramref name="bar"/> of <paramref name="section"/>: the latest
    /// metered <c>time</c> any voice writes at a bar's start at or before it, else
    /// <paramref name="opening"/> — the meter the section opens with (its header's, else the
    /// score's), which the caller holds. The one answer every reader of a chord row's bars takes
    /// (the page's grid, the MIDI, the MusicXML and the LilyPond twin): until 2026-11-07 the
    /// three exporters read a row's every bar in the section's opening meter, so a row under a
    /// <c>time 3/4</c> written in the music sounded and exported its bars a beat long.
    /// </summary>
    public (int Beats, int BeatType) MeterOfBar(string section, int bar, (int Beats, int BeatType) opening)
    {
        if (!_timeline.TryGetValue(section, out var c))
            return opening;
        var found = opening;
        foreach (var (at, time) in c)
        {
            if (at > bar)
                break;
            if (!time.IsSenzaMisura)
                found = (time.Beats, time.BeatType);
        }
        return found;
    }

    /// <summary>Per section name: bar index within the section → the length of the bar a voice
    /// writes there under <c>time none</c> (the longest, when several do).</summary>
    private readonly Dictionary<string, Dictionary<int, Fraction>> _unmetered = new(StringComparer.Ordinal);

    private void NoteUnmeteredBars(string section, List<MeasureModel.Bar> bars,
        List<(int Bar, TimeSignatureSyntax Time, bool MidBar)> written)
    {
        if (!written.Any(w => w.Time.IsSenzaMisura))
            return;
        if (!_unmetered.TryGetValue(section, out var lengths))
            _unmetered[section] = lengths = new Dictionary<int, Fraction>();
        bool senza = false;
        int next = 0;
        for (int bar = 0; bar < bars.Count; bar++)
        {
            for (; next < written.Count && written[next].Bar <= bar; next++)
                senza = written[next].Time.IsSenzaMisura;
            if (senza && (!lengths.TryGetValue(bar, out var standing) || standing < bars[bar].Duration))
                lengths[bar] = bars[bar].Duration;
        }
    }

    /// <summary>How long the unmetered bar at <paramref name="bar"/> of <paramref name="section"/>
    /// is — what the part that writes it under <c>time none</c> puts in it — or null when no
    /// part writes one there.</summary>
    /// <remarks>An unmetered bar has no length but its music's, so a part that rests through it
    /// (one the section does not write at all) has to be told: its padding bar there was a
    /// spacer under a clock that stands still, which closed no bar — the staff came out a bar
    /// short and every later bar of the part a bar early (grammar evaluation 3, 2026-11-13).</remarks>
    public Fraction? UnmeteredBarLength(string section, int bar)
        => _unmetered.TryGetValue(section, out var lengths) && lengths.TryGetValue(bar, out var length) ? length : null;

    /// <summary>The meter of each of the <paramref name="bars"/> bars of <paramref name="section"/>
    /// (<see cref="MeterOfBar"/>), as lengths.</summary>
    public List<Fraction> BarMeters(string section, int bars, (int Beats, int BeatType) opening)
    {
        var meters = new List<Fraction>(Math.Max(1, bars));
        for (int i = 0; i < Math.Max(1, bars); i++)
        {
            var (beats, beatType) = MeterOfBar(section, i, opening);
            meters.Add(new Fraction(beats, beatType));
        }
        return meters;
    }

    /// <summary>The part a voice container (or a part declaration) belongs to: a by-section
    /// block's name, a by-part cell's part; null for the single-part shorthand.</summary>
    private static string? PartNameOf(SyntaxNode node) => node switch
    {
        PartDeclarationSyntax decl => decl.Name.Text,
        PartBlockSyntax block => block.Name,
        SectionDeclarationSyntax { Parent: PartDeclarationSyntax decl } => decl.Name.Text,
        _ => null,
    };

    /// <summary>The changes of <paramref name="section"/>, by bar index within it.</summary>
    public IReadOnlyDictionary<int, TimeSignatureSyntax>? ChangesOf(string section)
        => _changes.TryGetValue(section, out var c) && c.Count > 0 ? c : null;

    /// <summary>The change standing at <paramref name="bar"/> of <paramref name="section"/>
    /// that <paramref name="container"/> does not write itself — what a reader of that voice
    /// applies there. Null when there is none, or the voice writes its own.</summary>
    public TimeSignatureSyntax? ForeignChangeAt(string section, int bar, SyntaxNode? container)
    {
        if (!_changes.TryGetValue(section, out var c) || !c.TryGetValue(bar, out var time))
            return null;
        if (container != null && _own.TryGetValue(Normalize(container), out var own) && own.Contains(bar))
            return null;
        return time;
    }

    /// <summary>The meter in force at <paramref name="bar"/> of <paramref name="section"/> by
    /// the plan alone (the latest change at or before it), or null when none stands yet.</summary>
    public TimeSignatureSyntax? MeterAt(string section, int bar)
    {
        if (!_changes.TryGetValue(section, out var c))
            return null;
        TimeSignatureSyntax? found = null;
        foreach (var (at, time) in c)
        {
            if (at > bar)
                break;
            found = time;
        }
        return found;
    }

    /// <summary>
    /// A voice's own items of <paramref name="section"/> with every change another part writes
    /// at a bar's start put in front of that bar — for a reader that walks the items as written
    /// and keeps a meter of its own (the MusicXML part's <c>&lt;time&gt;</c>). The bars are
    /// counted on the items' own bar lines (a bar line after music closes a bar; a bare
    /// <c>|</c> or <c>|:</c> with nothing timed before it is an empty bar), so a stream whose
    /// bars are not all written at its top level (a repeat, a phrase reference, a voice span)
    /// is handed back as it is. A percent repeat's bars and an <c>R*N</c>'s are counted
    /// (<see cref="BarsOfItems"/>): the item after them is asked for the bar it stands in.
    /// </summary>
    public IReadOnlyList<SyntaxNode> WithForeignChanges(string section, SyntaxNode container, IReadOnlyList<SyntaxNode> items)
    {
        if (!_changes.TryGetValue(section, out var c) || c.Count == 0)
            return items;
        foreach (var item in items)
            if (item is RepeatExpressionSyntax or PercentMarkSyntax
                or ParallelExpressionSyntax or VariableReferenceSyntax)
                return items;
        var result = new List<SyntaxNode>(items.Count + c.Count);
        var barOf = BarsOfItems(section, container);
        int bar = 0, beyond = 0;
        bool timed = false;
        bool pending = true; // the bar has not been opened yet
        foreach (var item in items)
        {
            bool directive = item is TimeSignatureSyntax or KeySignatureSyntax or ClefDeclarationSyntax
                or TempoDeclarationSyntax or PartialDeclarationSyntax;
            // The change goes in front of the bar's first timed item — or of the bar line
            // that closes it empty — so the voice's own directives at the bar's head stay first
            // and nothing is put after its last bar.
            bool decoration = item is BarlineSyntax { BarToken.Text: not ("|" or "|:") } && !timed;
            if (!directive && item is not BarlineSyntax)
            {
                // An `R*N` before it made the bar lines a short count (BarsOfItems).
                if (!timed && barOf.TryGetValue(item.Span.Start, out int placed))
                    bar = placed;
                beyond += BarsBeyondTheFirst(item);
            }
            if (pending && !directive && !decoration)
            {
                if (ForeignChangeAt(section, bar, container) is { } t)
                    result.Add(t);
                pending = false;
            }
            result.Add(item);
            if (item is BarlineSyntax b)
            {
                if (timed || b.BarToken.Text is "|" or "|:")
                {
                    bar += 1 + beyond;
                    pending = true;
                }
                timed = false;
                beyond = 0;
            }
            else if (!directive)
                timed = true;
        }
        return result;
    }

    private static SyntaxNode Normalize(SyntaxNode container)
        => container is MusicBlockSyntax { Parent: PartBlockSyntax block } ? block : container;

    private static bool SameMeter(TimeSignatureSyntax a, TimeSignatureSyntax b)
        => a.IsSenzaMisura == b.IsSenzaMisura
           && (a.IsSenzaMisura || (a.Beats == b.Beats && a.BeatType == b.BeatType && a.BeatsText == b.BeatsText));

    /// <summary>True when some <c>time</c> stands inside music — not at the score level, not
    /// in a part header, not in the header of a section that holds no inline music
    /// (<see cref="Semantics.SectionHeaders"/>) — the only books with a plan to build.</summary>
    private static bool WritesTimeInMusic(SyntaxNode root)
    {
        foreach (var n in root.SitesOfKinds(SyntaxKind.TimeSignature))
        {
            if (n is not TimeSignatureSyntax ts || SectionBarCounts.IsScoreLevel(ts))
                continue;
            if (ts.Parent is PartDeclarationSyntax)
                continue;
            if (ts.Parent is SectionDeclarationSyntax section && !MeasureCollector.SectionHasInlineMusic(section))
                continue;
            return true;
        }
        return false;
    }
}
