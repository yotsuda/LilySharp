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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>A break directive a chords track writes, at the model measure it stands after
/// (<c>ChordNameCollector.WrittenBreaks</c>) — or one a part the score does not show writes
/// (<c>MeasureCollector.BreaksOffTheStaves</c>). <paramref name="Track"/> is the chords
/// track's name (<see cref="SyntaxFacts.UnnamedChordsName"/> for the unnamed one), null for a
/// part's.</summary>
internal readonly record struct WrittenBreak(int MeasureIndex, BreakKind Kind, int SourcePosition, string? Track);

/// <summary>
/// One bar line given a <c>break</c> by one part or chords track and a <c>noBreak</c> by
/// another (or <c>pageBreak</c> against <c>noPageBreak</c>): the break won. Read back by
/// <c>BreakConflictValidator</c> (LYS4077), which reports at the word that lost.
/// </summary>
/// <param name="LoserPosition">Where the <c>noBreak</c> / <c>noPageBreak</c> is written — its
/// bar's end where the word itself could not be found (a word reached through a phrase).</param>
/// <param name="LoserWord">The word that lost.</param>
/// <param name="LoserPlace">Who wrote it: <c>part 'vln'</c>, <c>chords track 'h'</c>.</param>
/// <param name="WinnerPosition">Where the winning <c>break</c> / <c>pageBreak</c> is written,
/// or -1 when the word could not be found (the form's, a phrase's).</param>
/// <param name="WinnerWord">The word that won.</param>
/// <param name="WinnerPlace">Who wrote it.</param>
public sealed record BreakConflict(int LoserPosition, string LoserWord, string LoserPlace,
    int WinnerPosition, string WinnerWord, string WinnerPlace);

public sealed partial class MeasureCollector
{
    // The bar lines two places disagree about (TakeBreaksFromEveryPart), for LYS4077.
    private readonly List<BreakConflict> _breakConflicts = new();

    /// <summary>The bar lines one place gave a break and another forbade one at; the break
    /// stands. Populated as a side effect of Collect.</summary>
    public IReadOnlyList<BreakConflict> BreakConflicts => _breakConflicts;

    // The breaks no drawn part's music writes (TakeBreaksFromEveryPart), for the .ly twin.
    private readonly List<WrittenBreak> _breaksOffTheStaves = new();

    /// <summary>The break directives of the last collect that no DRAWN part's music writes:
    /// every chords track's, and those of the parts the score does not show. The page has
    /// them on its measures; the <c>.ly</c> twin, whose staves write their own
    /// <c>\break</c>, has to be told (<c>LilyPondExporter.EmitBreaksOffTheStaves</c>).</summary>
    internal IReadOnlyList<WrittenBreak> BreaksOffTheStaves => _breaksOffTheStaves;

    /// <summary>
    /// A break is the SCORE's (owner's decision 2026-11-13): every voice of
    /// <paramref name="voices"/> — the drawn ones and, behind the <c>omit:</c> keys, the parts
    /// this score does not show — and every chords track of the file says where the lines and
    /// pages break, and every voice leaves carrying what they say together. Until then the
    /// layout read the first staff's measures alone, so a <c>break</c> or <c>pageBreak</c>
    /// written in a second part was dropped in silence (grammar evaluation 4, the chamber
    /// writer's r-06d).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE BAR LINE, ONE ANSWER: forced where anyone forces it, else forbidden where anyone
    /// forbids it. A <c>break</c> against a <c>noBreak</c> at one bar line is recorded
    /// (<see cref="BreakConflicts"/>, LYS4077) and the break wins, the page pair the same way.
    /// A part the score does not show is asked too: the writer put the page turn there for
    /// everyone, as LilyPond's <c>\break</c> in any context breaks the whole score, and no
    /// test or document recorded a reason for the old rule.
    /// LILYPOND-REF: lily/paper-column-engraver.cc:68-112 Paper_column_engraver::handle_manual_breaks
    ///   — a Score-level engraver: a break event from ANY voice sets the one command column's permission.
    /// </para>
    /// <para>
    /// The voices are aligned by measure INDEX, as <see cref="SynchronizeBarlines"/> reads
    /// them, and this runs beside it. A file that writes none of the four words is handed
    /// back untouched: no measure is rewritten, so its pages are the bytes they were. The
    /// score's own controls (<c>breaksOnly</c>, <c>measuresPerSystem</c>) are applied after
    /// this, to every staff, as before (<see cref="BreaksOnly"/>).
    /// </para>
    /// </remarks>
    private void TakeBreaksFromEveryPart(SyntaxNode root, Dictionary<string, Voice> voices)
    {
        _chordNameCollector.SectionStarts = _sectionState.AllStarts;
        var chordBreaks = _chordNameCollector.WrittenBreaks(root, _sectionState.StartMeasure);
        int bars = 0;
        bool any = chordBreaks != null;
        foreach (var voice in voices.Values)
        {
            bars = Math.Max(bars, voice.Measures.Length);
            if (!any)
                foreach (var m in voice.Measures)
                    if (m.LineBreakPermission != BreakPermission.Allow || m.PageBreakPermission != BreakPermission.Allow)
                    {
                        any = true;
                        break;
                    }
        }
        if (!any || bars == 0)
            return;

        var line = new BreakPermission[bars];
        var page = new BreakPermission[bars];
        foreach (var voice in voices.Values)
            for (int i = 0; i < voice.Measures.Length; i++)
            {
                line[i] = Stronger(line[i], voice.Measures[i].LineBreakPermission);
                page[i] = Stronger(page[i], voice.Measures[i].PageBreakPermission);
            }
        if (chordBreaks != null)
            foreach (var b in chordBreaks)
            {
                if (b.MeasureIndex < 0 || b.MeasureIndex >= bars)
                    continue;
                var (l, p) = PermissionsOf(b.Kind);
                line[b.MeasureIndex] = Stronger(line[b.MeasureIndex], l);
                page[b.MeasureIndex] = Stronger(page[b.MeasureIndex], p);
            }

        RecordBreakConflicts(root, voices, chordBreaks, line, page);
        if (chordBreaks != null)
            _breaksOffTheStaves.AddRange(chordBreaks);
        foreach (var (key, voice) in voices)
        {
            if (!key.StartsWith("omit:", StringComparison.Ordinal))
                continue;
            for (int i = 0; i < voice.Measures.Length; i++)
            {
                var m = voice.Measures[i];
                if (m.PageBreakPermission == BreakPermission.Force)
                    _breaksOffTheStaves.Add(new WrittenBreak(i, BreakKind.Page, -1, null));
                else if (m.LineBreakPermission == BreakPermission.Force && !m.BreaksMidBar)
                    _breaksOffTheStaves.Add(new WrittenBreak(i, BreakKind.Line, -1, null));
                if (m.LineBreakPermission == BreakPermission.Forbid)
                    _breaksOffTheStaves.Add(new WrittenBreak(i, BreakKind.NoLine, -1, null));
                if (m.PageBreakPermission == BreakPermission.Forbid)
                    _breaksOffTheStaves.Add(new WrittenBreak(i, BreakKind.NoPage, -1, null));
            }
        }

        foreach (var key in voices.Keys.ToArray())
        {
            var measures = voices[key].Measures;
            ImmutableArray<Measure>.Builder? rewritten = null;
            for (int i = 0; i < measures.Length; i++)
            {
                if (measures[i].LineBreakPermission == line[i] && measures[i].PageBreakPermission == page[i])
                    continue;
                rewritten ??= measures.ToBuilder();
                rewritten[i] = measures[i] with { LineBreakPermission = line[i], PageBreakPermission = page[i] };
            }
            if (rewritten != null)
                voices[key] = voices[key] with { Measures = rewritten.MoveToImmutable() };
        }
    }

    /// <summary>Force over Forbid over Allow: what two places say of one bar line together.</summary>
    private static BreakPermission Stronger(BreakPermission a, BreakPermission b)
        => a == BreakPermission.Force || b == BreakPermission.Force ? BreakPermission.Force
            : a == BreakPermission.Forbid || b == BreakPermission.Forbid ? BreakPermission.Forbid
            : BreakPermission.Allow;

    /// <summary>The line and page permissions one directive sets, as <c>MeasureBuilder</c>'s
    /// four setters write them (a page break forces the line break with it).</summary>
    private static (BreakPermission Line, BreakPermission Page) PermissionsOf(BreakKind kind) => kind switch
    {
        BreakKind.NoLine => (BreakPermission.Forbid, BreakPermission.Allow),
        BreakKind.Page => (BreakPermission.Force, BreakPermission.Force),
        BreakKind.NoPage => (BreakPermission.Allow, BreakPermission.Forbid),
        _ => (BreakPermission.Force, BreakPermission.Allow),
    };

    /// <summary>
    /// Records every bar line that ends up forced while some voice or chords track forbids
    /// it — one <see cref="BreakConflict"/> for each place that forbids, against the first
    /// place that forces. Reads the voices as they came in, before the write-back.
    /// </summary>
    private void RecordBreakConflicts(SyntaxNode root, Dictionary<string, Voice> voices,
        List<WrittenBreak>? chordBreaks, BreakPermission[] line, BreakPermission[] page)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool pageBreak = pass == 1;
            var merged = pageBreak ? page : line;
            for (int i = 0; i < merged.Length; i++)
            {
                if (merged[i] != BreakPermission.Force)
                    continue;
                (string Place, int Position)? winner = null;
                List<(string Place, int Position)>? losers = null;
                foreach (var (key, voice) in voices)
                {
                    if (i >= voice.Measures.Length)
                        continue;
                    var said = pageBreak ? voice.Measures[i].PageBreakPermission : voice.Measures[i].LineBreakPermission;
                    if (said == BreakPermission.Allow)
                        continue;
                    string place = $"part '{(key.StartsWith("omit:", StringComparison.Ordinal) ? key[5..] : voice.Name)}'";
                    int at = BreakWordOf(root, voice.Measures, i, pageBreak, said == BreakPermission.Forbid);
                    if (said == BreakPermission.Force)
                        winner ??= (place, at);
                    else
                        (losers ??= new()).Add((place, at >= 0 ? at : voice.Measures[i].SourceEnd));
                }
                if (chordBreaks != null)
                    foreach (var b in chordBreaks)
                    {
                        if (b.MeasureIndex != i)
                            continue;
                        var (l, p) = PermissionsOf(b.Kind);
                        var said = pageBreak ? p : l;
                        string place = b.Track is null or SyntaxFacts.UnnamedChordsName
                            ? "the chords track" : $"chords track '{b.Track}'";
                        if (said == BreakPermission.Force)
                            winner ??= (place, b.SourcePosition);
                        else if (said == BreakPermission.Forbid)
                            (losers ??= new()).Add((place, b.SourcePosition));
                    }
                if (winner is not { } won || losers == null)
                    continue;
                foreach (var (place, position) in losers)
                {
                    if (place == won.Place)
                        continue;       // one place saying both: its own last word, as it always was
                    var conflict = new BreakConflict(position, pageBreak ? "noPageBreak" : "noBreak", place,
                        won.Position, pageBreak ? "pageBreak" : "break", won.Place);
                    if (!_breakConflicts.Contains(conflict))
                        _breakConflicts.Add(conflict);
                }
            }
        }
    }

    /// <summary>
    /// Where the word that set measure <paramref name="index"/>'s permission is written, or
    /// -1: the first directive of the right kind from the bar's start to the next bar's, in
    /// the music the bar itself is written in. A word the bar did not get from there (the
    /// form's, one a phrase reference brought) is not found. Run for a conflict's two places
    /// only, so its walk of the file's break words is paid by a file that has a LYS4077.
    /// </summary>
    private static int BreakWordOf(SyntaxNode root, ImmutableArray<Measure> measures, int index,
        bool pageBreak, bool forbids)
    {
        int from = measures[index].SourceStart;
        int to = index + 1 < measures.Length && measures[index + 1].SourceStart > from
            ? measures[index + 1].SourceStart : int.MaxValue;
        if (from <= 0 && measures[index].SourceEnd <= 0)
            return -1;      // a placeholder bar: written nowhere
        foreach (var site in root.KindSites(SyntaxKind.Break))
        {
            if (site is not BreakSyntax brk || brk.SourceStart < from || brk.SourceStart >= to)
                continue;
            bool right = (pageBreak, forbids) switch
            {
                (false, false) => brk.Directive is BreakKind.Line or BreakKind.Page,
                (false, true) => brk.Directive == BreakKind.NoLine,
                (true, false) => brk.Directive == BreakKind.Page,
                (true, true) => brk.Directive == BreakKind.NoPage,
            };
            if (right && brk.Parent is { } list && list.Span.Start <= from && from < list.Span.End)
                return brk.SourceStart;
        }
        return -1;
    }
}
