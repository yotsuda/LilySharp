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

    /// <summary>Per section name: bar index within the section → the change in force from it.</summary>
    private readonly Dictionary<string, SortedDictionary<int, TimeSignatureSyntax>> _changes = new(StringComparer.Ordinal);

    /// <summary>Per voice container: the bars at whose start it writes a <c>time</c> itself.</summary>
    private readonly Dictionary<SyntaxNode, HashSet<int>> _own = new(ReferenceEqualityComparer.Instance);

    /// <summary>A voice's change that differs from the one standing at the same bar.</summary>
    public List<(TimeSignatureSyntax Lost, TimeSignatureSyntax Standing, string Section, int Bar)> Conflicts { get; } = new();

    public bool IsEmpty => _changes.Count == 0;

    public static SectionMeterPlan Build(SyntaxNode root, IReadOnlyDictionary<string, SyntaxNode>? phraseBodies = null)
    {
        if (!WritesTimeInMusic(root))
            return Empty;
        var phrases = phraseBodies ?? SectionBarCounts.PhraseBodies(root);
        var plan = new SectionMeterPlan();
        // Which parts write each section, to tell a change every part writes itself (the
        // common spelling — nothing to apply, and the plan stays Empty) from one some part
        // does not.
        var parts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in root.DescendantNodesOfKinds([SyntaxKind.PartDeclaration, SyntaxKind.PartBlock]))
            if (PartNameOf(n) is { } name)
                parts.Add(name);
        var ownByPart = new Dictionary<(string Section, string Part), HashSet<int>>();
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
        {
            if (voice.IsChords || voice.IsLyrics)
                continue;
            var written = new List<(int Bar, TimeSignatureSyntax Time, bool MidBar)>();
            MeasureModel.Split(voice.Container, phrases, null, written);
            if (written.Count == 0)
                continue;
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
            foreach (var bar in changes.Keys)
                foreach (var part in parts)
                    if (!ownByPart.TryGetValue((section, part), out var own) || !own.Contains(bar))
                        return plan;
        // Every part writes every change itself: nothing to apply — but a conflict is still
        // the validator's to name.
        if (plan.Conflicts.Count == 0)
            return Empty;
        plan._changes.Clear();
        return plan;
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
    /// is handed back as it is.
    /// </summary>
    public IReadOnlyList<SyntaxNode> WithForeignChanges(string section, SyntaxNode container, IReadOnlyList<SyntaxNode> items)
    {
        if (!_changes.TryGetValue(section, out var c) || c.Count == 0)
            return items;
        foreach (var item in items)
            if (item is RepeatExpressionSyntax or ParallelExpressionSyntax or VariableReferenceSyntax)
                return items;
        var result = new List<SyntaxNode>(items.Count + c.Count);
        int bar = 0;
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
                    bar++;
                    pending = true;
                }
                timed = false;
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
        foreach (var n in root.DescendantNodesOfKinds([SyntaxKind.TimeSignature]))
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
