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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>What the collector stamps on a section play's first timed item
/// (<see cref="MusicItem.BeginsSectionPlay"/> and its siblings): how the form reached the play
/// and what the play is to a form repeat — for an ending, the passes its bracket names.</summary>
public readonly record struct SectionPlayStamp(
    SectionPlayEdge Edge,
    string Section,
    SectionRepeatRole Role = SectionRepeatRole.None,
    bool RunStart = false,
    int Count = 0,
    bool Rewinds = false,
    PassSet Passes = default,
    // The form-level navigation marks standing between the play before and this one
    // (NavMarkStamp's spelling), so the played order can follow the jump texts.
    string? MarksBefore = null);

/// <summary>
/// The spelling of a run of form-level navigation marks on a play's stamp
/// (<see cref="MusicItem.SectionPlayMarksBefore"/> / <see cref="MusicItem.SectionPlayMarksAfter"/>):
/// the <see cref="NavigationMarkType"/> names in form order, joined with <c>|</c>. A string so the
/// item's rare fields keep their value equality and the stamp stays position-independent content
/// (the marks between two plays depend on the form text there, not on where in the piece it is).
/// </summary>
internal static class NavMarkStamp
{
    public static string? Encode(IReadOnlyList<NavigationMarkType> marks)
        => marks.Count == 0 ? null : string.Join('|', marks);

    public static IEnumerable<NavigationMarkType> Decode(string? stamp)
    {
        if (string.IsNullOrEmpty(stamp))
            yield break;
        foreach (var word in stamp.Split('|'))
            if (Enum.TryParse<NavigationMarkType>(word, out var mark))
                yield return mark;
    }
}

/// <summary>Which span a section-boundary complaint is about — the four families a span may
/// be carried from one section into the next (owner's decision, 2026-09-28), and the four
/// mark-paired families that took the same rule on 2026-09-29 (the second stage: the owner's
/// "later" of the first).</summary>
public enum SectionSpanKind
{
    /// <summary>A slur <c>( )</c>.</summary>
    Slur,
    /// <summary>A phrasing slur <c>@phrasingSlur</c> … <c>@!phrasingSlur</c>.</summary>
    PhrasingSlur,
    /// <summary>A tie <c>~</c>.</summary>
    Tie,
    /// <summary>A hairpin <c>@cresc</c> / <c>@decresc</c> / <c>@dim</c>, ended by the next
    /// dynamic.</summary>
    Hairpin,
    /// <summary>A text spanner <c>@rit</c> / <c>@accel</c> / <c>@textSpan("…")</c> … <c>@!rit</c>.</summary>
    TextSpanner,
    /// <summary>An ottava bracket <c>@ottava</c> / <c>@quindicesima</c> … <c>@!ottava</c> (or the
    /// next ottava start, which closes it).</summary>
    Ottava,
    /// <summary>A piano pedal <c>@sustain</c> / <c>@sostenuto</c> / <c>@unaCorda</c> … its
    /// release (or a re-pedalling, which closes it).</summary>
    Pedal,
    /// <summary>A trill spanner <c>@startTrillSpan</c> … <c>@stopTrillSpan</c> (or the next
    /// start, which closes it).</summary>
    Trill,
}

/// <summary>Why a span could not be carried over a section boundary.</summary>
public enum SectionCarryFault
{
    /// <summary>Carried into the section the form plays next, and not ended there (D1).</summary>
    NotClosedInNext,
    /// <summary>A close at a section's start with nothing carried in from the section the
    /// form played before it (D2).</summary>
    NothingCarriedIn,
    /// <summary>Carried into a section in which the part writes no music — its bars are
    /// padding (D3).</summary>
    IntoEmptySection,
    /// <summary>Carried over a repeat sign, a volta ending's edge or a jump mark (D4).</summary>
    AcrossRepeat,
}

/// <summary>
/// A slur, phrasing slur, tie or hairpin that breaks the carry rule
/// (<see cref="SectionPlayCursor"/>). <see cref="From"/> is the section the span starts in
/// (for <see cref="SectionCarryFault.NothingCarriedIn"/>, the section played before the close);
/// <see cref="Into"/> the section it fails in; <see cref="AtClose"/> true when the mark is the
/// span's CLOSE. Nothing is drawn for the span.
/// </summary>
public record SectionCarryWarning(
    int SourcePosition,
    SectionSpanKind Kind,
    SectionCarryFault Fault,
    string? From,
    string? Into,
    bool AtClose = false,
    // The printed play the refused mark stands in (0-based, the SectionPlayCursor's count) —
    // a section played twice writes the same mark twice, and only one play may be refused
    // (`form { C D C E }`: the second C's slur, not the first's). -1 where the family does not
    // count plays. What the MusicXML export keys its refusals on, beside the position.
    int Play = -1);

/// <summary>
/// THE carry rule for spans at section boundaries, in one place — every reader of a slur,
/// phrasing slur, tie or hairpin asks it, so the page, the diagnostics and the exports cannot
/// disagree about which span survives a boundary.
/// </summary>
/// <remarks>
/// <para>
/// A span open when a section ENDS is carried into the section the form plays NEXT — played
/// order, per part and per play (<see cref="MusicItem.BeginsSectionPlay"/>) — and must end
/// there (owner's decisions, 2026-09-28): it may not run on through a whole section, it may
/// not be carried over a repeat sign, a volta edge or a jump (<see cref="SectionPlayEdge"/>
/// Volta and Repeat — "forbidden for now" for all but a tie, which is carried along the PLAYED
/// order instead (<see cref="SectionPlayGraph"/>); the test lives in <see cref="Carries"/> alone),
/// and it may not be carried into a section the part does not play. A span that breaks the
/// rule is not drawn and is reported (LYS4023).
/// </para>
/// <para>
/// Section self-containment is about RUNNING STATE (frame, key, clef, meter, overrides), which
/// still resets at every boundary; a span is a pair of events, and the rule above is what keeps
/// a reusable section honest about it — checked in every play, so <c>form { C D C E }</c>
/// judges the second C's slur against E, not against the D written after C.
/// </para>
/// </remarks>
internal sealed class SectionPlayCursor
{
    private readonly List<(string? Name, SectionPlayEdge Edge, bool HasMusic)> _plays = new();

    /// <summary>The play the walk stands in: -1 before the first stamp (a voice with no
    /// sections, which the rule never touches).</summary>
    public int Play => _plays.Count - 1;

    /// <summary>Forgets every play — the walk moved to another voice.</summary>
    public void Reset() => _plays.Clear();

    /// <summary>Reads one item, in voice order, BEFORE its marks are read. True when the item
    /// opens a new play.</summary>
    public bool Enter(MusicItem item)
    {
        bool begins = false;
        if (item.BeginsSectionPlay != SectionPlayEdge.None)
        {
            _plays.Add((item.SectionPlayName, item.BeginsSectionPlay, false));
            begins = true;
        }
        if (_plays.Count > 0 && !_plays[^1].HasMusic
            && (item is NoteItem or ChordItem || item is RestItem { IsSpacer: false }))
            _plays[^1] = _plays[^1] with { HasMusic = true };
        return begins;
    }

    /// <summary>The section a play plays, or null.</summary>
    public string? NameOf(int play) => play >= 0 && play < _plays.Count ? _plays[play].Name : null;

    /// <summary>Whether a SLUR, PHRASING SLUR or HAIRPIN open at the end of the play before this
    /// one may be carried into this one: THE ONE TEST OF THE EDGE.</summary>
    /// <remarks>
    /// Not over a repeat sign, a volta edge or a jump: the span's end is written in the next
    /// section and would close nothing on the passes that reach it another way ("forbidden for
    /// now", owner 2026-09-28). A TIE is not asked this: it is carried over ANY edge, to the
    /// section actually PLAYED next each time (<see cref="SectionPlayGraph"/>, owner's second
    /// decision of the day, after the corpus sweep found 15 ties into and out of repeats in 13
    /// books).
    /// </remarks>
    public static bool Carries(SectionPlayEdge edge) => edge < SectionPlayEdge.Volta;

    /// <summary>The verdict on a slur or phrasing slur opened in play
    /// <paramref name="openedIn"/>, asked as the walk ENTERS a new play; null keeps it open.</summary>
    public SectionCarryFault? OnEntry(int openedIn)
    {
        int q = Play;
        if (openedIn < 0 || openedIn >= q)
            return null;
        if (openedIn == q - 1)
            return Carries(_plays[q].Edge) ? null : SectionCarryFault.AcrossRepeat;
        // Carried into play q-1 and still open when it ended.
        return _plays[q - 1].HasMusic ? SectionCarryFault.NotClosedInNext : SectionCarryFault.IntoEmptySection;
    }

    /// <summary>The verdict on a span still open when the voice ENDS: null when it was opened
    /// in the last play (the family's own "never closed" answers for it, as it always has).</summary>
    public SectionCarryFault? AtEnd(int openedIn)
    {
        int last = Play;
        if (openedIn < 0 || openedIn >= last)
            return null;
        return _plays[last].HasMusic ? SectionCarryFault.NotClosedInNext : SectionCarryFault.IntoEmptySection;
    }

    /// <summary>The verdict on a CLOSE that finds nothing open, when nothing of its family has
    /// opened yet in this play: it was written to end a span carried in from the play before,
    /// and none was. Null in the first play (an ordinary unmatched close).</summary>
    public SectionCarryFault? OnUnmatchedClose(bool openedThisPlay)
    {
        int q = Play;
        if (q < 1 || openedThisPlay)
            return null;
        return Carries(_plays[q].Edge) ? SectionCarryFault.NothingCarriedIn : SectionCarryFault.AcrossRepeat;
    }

    /// <summary>The (From, Into) section names for a fault found on entry to the current play.</summary>
    public (string? From, string? Into) NamesOnEntry(int openedIn, SectionCarryFault fault)
        => fault == SectionCarryFault.AcrossRepeat
            ? (NameOf(openedIn), NameOf(Play))
            : (NameOf(openedIn), NameOf(Play - 1));

    /// <summary>The (From, Into) section names for a fault found at the voice's end.</summary>
    public (string? From, string? Into) NamesAtEnd(int openedIn) => (NameOf(openedIn), NameOf(Play));

    /// <summary>The (From, Into) section names for an unmatched close in the current play.</summary>
    public (string? From, string? Into) NamesForClose() => (NameOf(Play - 1), NameOf(Play));
}

/// <summary>The section plays of a score by MEASURE — for the hairpin, which is paired over
/// the score's mark and dynamic tables rather than on a voice's items.</summary>
/// <remarks>
/// Plays are aligned across staves (every part pads each section to its canonical bar count),
/// so one voice's stamps give every staff's boundaries. A play's start is the measure of its
/// first timed item.
/// </remarks>
internal sealed class SectionPlays
{
    private readonly List<(int Measure, SectionPlayEdge Edge, string? Name)> _starts;

    private SectionPlays(List<(int, SectionPlayEdge, string?)> starts) => _starts = starts;

    /// <summary>The plays a voice's measures stamp, or null when they stamp none.</summary>
    public static SectionPlays? Of(ImmutableArray<Measure> measures)
    {
        List<(int, SectionPlayEdge, string?)>? starts = null;
        for (int mi = 0; mi < measures.Length; mi++)
            foreach (var item in measures[mi].Items)
                if (item.BeginsSectionPlay != SectionPlayEdge.None)
                    (starts ??= new()).Add((mi, item.BeginsSectionPlay, item.SectionPlayName));
        return starts is { Count: > 1 } ? new SectionPlays(starts) : null;
    }

    /// <summary>The plays of the first voice that stamps any.</summary>
    public static SectionPlays? Of(IEnumerable<Voice> voices)
    {
        foreach (var voice in voices)
            if (Of(voice.Measures) is { } plays)
                return plays;
        return null;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Holder> s_byScore = new();

    private sealed class Holder(SectionPlays? plays)
    {
        public readonly SectionPlays? Plays = plays;
    }

    /// <summary>A score's plays, memoised per score (the hairpin detection is asked for more
    /// than once per layout).</summary>
    public static SectionPlays? For(Score score)
        => s_byScore.GetValue(score, static s => new Holder(Of(((Score)s).Voices))).Plays;

    /// <summary>A multi-staff score's plays, memoised per score.</summary>
    public static SectionPlays? For(MultiStaffScore score)
        => s_byScore.GetValue(score, static s =>
        {
            foreach (var (_, staff, _) in ((MultiStaffScore)s).EnumerateStaves())
                if (Of(staff.Voices) is { } plays)
                    return new Holder(plays);
            return new Holder(null);
        }).Plays;

    /// <summary>The play a measure belongs to (-1 before the first).</summary>
    public int PlayAt(int measure)
    {
        int play = -1;
        for (int i = 0; i < _starts.Count && _starts[i].Measure <= measure; i++)
            play = i;
        return play;
    }

    /// <summary>The first measure of a play.</summary>
    public int StartOf(int play) => _starts[play].Measure;

    /// <summary>The number of plays.</summary>
    public int Count => _starts.Count;

    /// <summary>The section a play plays.</summary>
    public string? NameOf(int play) => play >= 0 && play < _starts.Count ? _starts[play].Name : null;

    /// <summary>The verdict on a span that starts in <paramref name="startMeasure"/> and ends at
    /// the start of, or inside, <paramref name="endMeasure"/> (an end at a play's first measure
    /// belongs to that play): null when it stays in its play or is carried into the next one
    /// over an edge that carries. The failing span is cut at the end of its own play.</summary>
    public SectionCarryFault? Judge(int startMeasure, int endMeasure)
    {
        int ps = PlayAt(startMeasure), pe = PlayAt(endMeasure);
        if (ps < 0 || pe <= ps)
            return null;
        if (pe == ps + 1)
            return SectionPlayCursor.Carries(_starts[pe].Edge) ? null : SectionCarryFault.AcrossRepeat;
        return SectionCarryFault.NotClosedInNext;
    }

    /// <summary>The verdict on a CLOSE in <paramref name="stopMeasure"/> that finds nothing
    /// open, when nothing of its family has opened yet in that play
    /// (<paramref name="openedThisPlay"/> false): it was written to end a span carried in
    /// from the play before, and none was — <see cref="SectionPlayCursor.OnUnmatchedClose"/>
    /// for the mark-paired families. Null in the first play (an ordinary unmatched close).</summary>
    public SectionCarryFault? OnUnmatchedStop(int stopMeasure, bool openedThisPlay)
    {
        int q = PlayAt(stopMeasure);
        if (q < 1 || openedThisPlay)
            return null;
        return SectionPlayCursor.Carries(_starts[q].Edge) ? SectionCarryFault.NothingCarriedIn : SectionCarryFault.AcrossRepeat;
    }

    /// <summary>The warning for a span opened in <paramref name="startMeasure"/> whose end
    /// <see cref="Judge"/> refused with <paramref name="fault"/>: from its own play into the
    /// play after it.</summary>
    public SectionCarryWarning Refused(int sourcePosition, SectionSpanKind kind, SectionCarryFault fault, int startMeasure)
    {
        int own = PlayAt(startMeasure);
        return new SectionCarryWarning(sourcePosition, kind, fault, NameOf(own), NameOf(own + 1));
    }

    /// <summary>The warning for an unmatched close in <paramref name="stopMeasure"/>
    /// (<see cref="OnUnmatchedStop"/>): the play before it carried nothing in.</summary>
    public SectionCarryWarning RefusedClose(int sourcePosition, SectionSpanKind kind, SectionCarryFault fault, int stopMeasure)
    {
        int q = PlayAt(stopMeasure);
        return new SectionCarryWarning(sourcePosition, kind, fault, NameOf(q - 1), NameOf(q), AtClose: true);
    }

    /// <summary>Where a span opened in <paramref name="startMeasure"/> is CUT when its end is
    /// refused: the first measure of the play after its own — the bar line its own play ends
    /// at, as the hairpin is cut (<c>HairpinEngraver.DetectHairpins</c>).</summary>
    public int CutMeasure(int startMeasure) => StartOf(PlayAt(startMeasure) + 1);
}

/// <summary>What one printed section play is to the PLAYED order — the input
/// <see cref="PlayedOrder.Expand"/> rebuilds it from. The page reads it off the stamps
/// (<see cref="SectionPlayGraph"/>); MusicXML and the LilyPond twin off the form
/// (<see cref="PlayedOrder.PlaysOf"/>). <paramref name="MarksBefore"/> are the form-level
/// navigation marks between the play before and this one, <paramref name="MarksAfter"/> those
/// after the LAST play of the form (<see cref="NavMarkStamp"/>'s spelling, null when none).</summary>
internal readonly record struct PrintedPlay(SectionRepeatRole Role, bool RunStart, int Count, bool Rewinds,
    PassSet Passes = default, string? MarksBefore = null, string? MarksAfter = null);

/// <summary>
/// The PLAYED order of a form's printed section plays: the order the MIDI plays them in
/// (<c>MidiExporter.PlayForm</c> along <see cref="FormRoute"/>, <c>PlayRepeatRun</c>,
/// <c>RepeatFromTheBeginning</c>), as indices into the printed order. A tie is carried along it
/// (owner's decision 2026-09-28): from the end of a printed play to the first note of EVERY play
/// that follows it in this order.
/// </summary>
/// <remarks>
/// A repeat RUN (a block, or a block's part between <c>:|:</c> dividers, or a run a form-level
/// <c>:|:</c> opened) plays its body once per pass and, on pass p, the ending whose numbers name
/// p (<see cref="RepeatPasses"/> — the one spelling; the passes are the written <c>:|*N</c>, else
/// the highest number an ending names, and at least two). ⚠️ Until 2026-09-29 this reader
/// played the N-th WRITTEN ending on pass N and counted the endings, so a tie out of
/// <c>[2. C]</c> in <c>|: A [1,3. B] :| [2. C] D</c> was carried into D, which never follows it.
/// A one-sided form <c>:|</c> plays the piece so far again (without its own earlier rewinds).
/// <para>
/// THE JUMP TEXTS ARE FOLLOWED (session 792; owner's choice among session 775's candidates):
/// the marks stamped between the plays (<see cref="PrintedPlay.MarksBefore"/> /
/// <see cref="PrintedPlay.MarksAfter"/>) rebuild the form's mark sequence, <see cref="FormRoute"/>
/// reads the route through it — the ONE reading of the signs, the MIDI's — and each stretch is
/// expanded here: on a first-pass stretch as above; on a REPLAY a run plays once, as its last
/// pass, and a one-sided <c>:|</c> rewinds nothing. Until this session the four readers of the
/// played order (this one for the page, MusicXML, the twin, the bar-complement adjacency) stopped
/// at the jump texts while the MIDI followed them, so a tie at the end of the section before a
/// <c>ds al coda</c> reached nothing where the MIDI sustained it into the segno's section.
/// </para>
/// ⚠️ A one-sided <c>:|</c> at the very END of a form has no play after it to carry the stamp,
/// so its rewind is not seen here: a tie at the end of such a piece has no successor.
/// </remarks>
internal static class PlayedOrder
{
    private enum Unit { Mark, Rewind, Single, Run }

    public static List<int> Expand(IReadOnlyList<PrintedPlay> plays)
    {
        int n = plays.Count;
        // The form's top-level sequence, rebuilt from the stamps: a mark, a rewind (a one-sided
        // `:|`), a single play, or a repeat RUN [Start, End) of consecutive printed plays — the
        // grouping the stamps' roles give (a run continues while the role is a repeat's and no
        // new run starts and no rewind stands).
        var units = new List<(Unit Kind, int Start, int End)>();
        var marks = new List<NavigationMarkType?>();
        void Add(Unit kind, int start, int end, NavigationMarkType? mark = null)
        {
            units.Add((kind, start, end));
            marks.Add(mark);
        }
        int i = 0;
        while (i < n)
        {
            if (plays[i].Rewinds)
                Add(Unit.Rewind, i, i);
            foreach (var mark in NavMarkStamp.Decode(plays[i].MarksBefore))
                Add(Unit.Mark, i, i, mark);
            if (plays[i].Role == SectionRepeatRole.None)
            {
                Add(Unit.Single, i, i + 1);
                i++;
                continue;
            }
            int j = i;
            do
                j++;
            while (j < n && plays[j].Role != SectionRepeatRole.None
                   && !plays[j].RunStart && !plays[j].Rewinds);
            Add(Unit.Run, i, j);
            i = j;
        }
        if (n > 0)
            foreach (var mark in NavMarkStamp.Decode(plays[n - 1].MarksAfter))
                Add(Unit.Mark, n, n, mark);

        var played = new List<int>();
        var plain = new List<int>(); // the first-pass plays so far, without rewinds — what a rewind replays
        foreach (var stretch in FormRoute.Of(marks))
        {
            for (int u = stretch.From; u < stretch.To; u++)
            {
                var (kind, start, end) = units[u];
                switch (kind)
                {
                    case Unit.Rewind:
                        // One rewind per written sign, on the first pass only (MidiExporter.PlayForm).
                        if (!stretch.Replay)
                            played.AddRange(plain);
                        break;
                    case Unit.Single:
                        played.Add(start);
                        if (!stretch.Replay)
                            plain.Add(start);
                        break;
                    case Unit.Run:
                        ExpandRun(plays, start, end, stretch.Replay, played, plain);
                        break;
                }
            }
        }
        return played;
    }

    /// <summary>One repeat run, printed plays <c>[start, end)</c>: its body once per pass and,
    /// on pass p, the ending whose numbers name p — on a replay, the last pass alone.</summary>
    private static void ExpandRun(IReadOnlyList<PrintedPlay> plays, int start, int end, bool replay,
        List<int> played, List<int> plain)
    {
        var body = new List<int>();
        // One list per ending: its sections' plays in order ([1. C D] is two plays) — and
        // the passes its bracket names, off its first play's stamp.
        var endings = new List<List<int>>();
        var endingPasses = new List<PassSet>();
        int count = plays[start].Count;
        for (int j = start; j < end; j++)
        {
            if (plays[j].Role == SectionRepeatRole.Body)
                body.Add(j);
            else if (plays[j].Role == SectionRepeatRole.EndingContinued && endings.Count > 0)
                endings[^1].Add(j);
            else
            {
                endings.Add(new List<int> { j });
                endingPasses.Add(plays[j].Passes);
            }
        }
        int passes = RepeatPasses.Count(count > 0 ? count : null, endingPasses);
        for (int pass = replay ? passes : 1; pass <= passes; pass++)
        {
            played.AddRange(body);
            if (!replay)
                plain.AddRange(body);
            int ending = RepeatPasses.EndingFor(pass, endingPasses);
            if (ending >= 0)
            {
                played.AddRange(endings[ending]);
                if (!replay)
                    plain.AddRange(endings[ending]);
            }
        }
    }

    /// <summary>
    /// The printed plays of a form read off its items (<see cref="FormWalk.Read"/>), in printed
    /// order, with their roles, counts, rewinds, passes and the navigation marks between them —
    /// the input <see cref="Expand"/> takes, built from the FORM for the readers that have no
    /// stamps (the LilyPond twin, the bar-complement adjacency). <paramref name="names"/>, when
    /// given, receives each play's section name. A section of a nested repeat block is not read
    /// (no reader walks one; the page's block walk has no arm for it either).
    /// </summary>
    public static List<PrintedPlay> PlaysOf(IReadOnlyList<FormWalk.Item> items, List<string>? names = null)
    {
        var plays = new List<PrintedPlay>();
        var pending = new List<NavigationMarkType>();
        bool rewind = false;
        void Add(string name, SectionRepeatRole role, bool runStart, int count, PassSet passes = default)
        {
            plays.Add(new PrintedPlay(role, runStart, runStart ? count : 0, rewind, passes,
                NavMarkStamp.Encode(pending)));
            pending.Clear();
            names?.Add(name);
            rewind = false;
        }
        foreach (var item in items)
        {
            switch (item)
            {
                case FormWalk.SectionRef s:
                    Add(s.Name, SectionRepeatRole.None, false, 0);
                    break;
                case FormWalk.Ending e:
                    foreach (var es in e.Sections)
                        Add(es.Name, SectionRepeatRole.None, false, 0);
                    break;
                case FormWalk.LoneRepeatEnd:
                    rewind = true;
                    break;
                case FormWalk.Other { Node: NavigationMarkSyntax nav }:
                    pending.Add(nav.MarkType);
                    break;
                case FormWalk.Repeat rb:
                    bool runStart = true;
                    int count = rb.ExplicitPlayCount ?? 0;
                    foreach (var child in rb.Children)
                    {
                        if (child is FormWalk.SectionRef bs)
                        {
                            Add(bs.Name, SectionRepeatRole.Body, runStart, count);
                            runStart = false;
                        }
                        else if (child is FormWalk.Ending be)
                        {
                            // [1. C D] is two printed plays of ONE ending; its passes ride the first.
                            var role = SectionRepeatRole.Ending;
                            var passes = PassSet.Of(be.Node.Numbers);
                            foreach (var es in be.Sections)
                            {
                                Add(es.Name, role, runStart, count, passes);
                                runStart = false;
                                role = SectionRepeatRole.EndingContinued;
                                passes = default;
                            }
                        }
                        else if (child is FormWalk.BothBar)
                            runStart = true;
                    }
                    break;
            }
        }
        if (pending.Count > 0 && plays.Count > 0)
            plays[^1] = plays[^1] with { MarksAfter = NavMarkStamp.Encode(pending) };
        return plays;
    }

    /// <summary>For each printed play, the printed plays that follow it in the played order.</summary>
    public static List<int>[] Successors(IReadOnlyList<PrintedPlay> plays)
    {
        var next = new List<int>[plays.Count];
        for (int k = 0; k < next.Length; k++)
            next[k] = new List<int>();
        var played = Expand(plays);
        for (int k = 0; k + 1 < played.Count; k++)
            if (!next[played[k]].Contains(played[k + 1]))
                next[played[k]].Add(played[k + 1]);
        return next;
    }
}

/// <summary>
/// A voice's section plays as the page sees them — where each printed play starts (its stamped
/// first timed item) and which plays follow each in the PLAYED order
/// (<see cref="PlayedOrder"/>). What a TIE at the end of a play is carried to.
/// </summary>
/// <remarks>
/// Every reader of a tie asks this one table: <c>TieDetector</c> draws the arc only to the play
/// printed next and only when that play is also played next; <c>SectionTieCarry</c> gives every
/// other play that follows the tied note a repeat tie (and the tied note a hanging tie when no
/// arc is drawn from it); <c>TieTargetScanner</c> checks each such target's pitch (LYS4007).
/// </remarks>
internal sealed class SectionPlayGraph
{
    private readonly List<(int Measure, int Item)> _starts;
    private readonly List<string?> _names;
    private readonly List<int>[] _next;

    private SectionPlayGraph(List<(int, int)> starts, List<string?> names, List<int>[] next)
    {
        _starts = starts;
        _names = names;
        _next = next;
    }

    /// <summary>The section a printed play plays.</summary>
    public string? NameOf(int play) => play >= 0 && play < _names.Count ? _names[play] : null;

    /// <summary>The graph of a voice's stamped plays, or null when it stamps none (one play is a graph too:<br/>/// <c>|: A :|</c> follows itself).</summary>
    public static SectionPlayGraph? Of(ImmutableArray<Measure> measures)
    {
        List<(int, int)>? starts = null;
        List<PrintedPlay>? plays = null;
        List<string?>? names = null;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (item.BeginsSectionPlay == SectionPlayEdge.None)
                    continue;
                (starts ??= new()).Add((mi, ii));
                (names ??= new()).Add(item.SectionPlayName);
                (plays ??= new()).Add(new PrintedPlay(item.SectionRepeatRole, item.SectionRepeatRunStart,
                    item.SectionRepeatCount, item.SectionPlayRewinds, item.SectionEndingPasses,
                    item.SectionPlayMarksBefore, item.SectionPlayMarksAfter));
            }
        }
        return starts is { Count: > 0 } ? new SectionPlayGraph(starts, names!, PlayedOrder.Successors(plays!)) : null;
    }

    /// <summary>The number of printed plays.</summary>
    public int Count => _starts.Count;

    /// <summary>The printed play an item stands in (-1 before the first).</summary>
    public int PlayOf(int measure, int item)
    {
        int play = -1;
        for (int i = 0; i < _starts.Count; i++)
        {
            var (m, it) = _starts[i];
            if (m < measure || (m == measure && it <= item))
                play = i;
            else
                break;
        }
        return play;
    }

    /// <summary>Where a printed play's first timed item stands.</summary>
    public (int Measure, int Item) StartOf(int play) => _starts[play];

    /// <summary>The printed plays that follow <paramref name="play"/> in the played order.</summary>
    public IReadOnlyList<int> Successors(int play) => play >= 0 && play < _next.Length ? _next[play] : [];

    /// <summary>Whether the play printed after <paramref name="play"/> is also played after it —
    /// the one case a tie at its end is drawn as an arc to the next note.</summary>
    public bool PrintedNextIsPlayedNext(int play) => Successors(play).Contains(play + 1);

    /// <summary>Whether <paramref name="measure"/>/<paramref name="item"/> is the first timed
    /// item of a play (its stamp is what makes it so).</summary>
    public bool IsPlayStart(int measure, int item, out int play)
    {
        for (int i = 0; i < _starts.Count; i++)
            if (_starts[i] == (measure, item))
            {
                play = i;
                return true;
            }
        play = -1;
        return false;
    }
}

/// <summary>
/// Draws the continuations of a tie carried over a section boundary that are NOT the arc to
/// the next printed note: every play that follows the tied note's play in the PLAYED order
/// other than the one printed next (back to a <c>|:</c>, into a later ending) gives its first
/// note a repeat tie — the short arc from the left, <c>@repeatTie</c>'s glyph — and the tied
/// note, when no arc leaves it at all, a hanging tie toward the bar line (<c>@laissezVibrer</c>'s
/// glyph). Owner's decision 2026-09-28.
/// </summary>
/// <remarks>
/// ONCE per target, however many tied notes reach it, and a repeat tie already written there
/// is kept, not doubled — both fall out of setting a flag. Only a target that repeats the tied
/// pitch gets one (TieTargetScanner reports the others, LYS4007). A pure function of the
/// finished voice, so a resumed collect that re-finishes the voice gets the same answer, and
/// applying it twice changes nothing.
/// <para>
/// LILYSHARP-OWN: the hanging tie is drawn as a laissez-vibrer tie, LilyPond's short half-tie,
/// where LilyPond's own tie into a repeat that goes elsewhere would run to the bar line.
/// </para>
/// </remarks>
internal static class SectionTieCarry
{
    public static ImmutableArray<Measure> Apply(ImmutableArray<Measure> measures)
    {
        var graph = SectionPlayGraph.Of(measures);
        if (graph == null)
            return measures;
        Dictionary<(int, int), MusicItem>? changed = null;
        MusicItem Current(int m, int i) => changed != null && changed.TryGetValue((m, i), out var c) ? c : measures[m].Items[i];

        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (item.GraceTime || item is not (NoteItem { HasTieStart: true } or ChordItem { HasTieStart: true }))
                    continue;
                int play = graph.PlayOf(mi, ii);
                if (play < 0)
                    continue;
                var next = NoteScan.FindNext(measures, mi, ii, x => x is NoteItem or ChordItem or RestItem);
                if (next is { } n && n.Item.BeginsSectionPlay == SectionPlayEdge.None)
                    continue; // not at its play's end: an ordinary tie
                var successors = graph.Successors(play);
                bool arc = next != null && graph.PrintedNextIsPlayedNext(play);
                bool any = false;
                foreach (int s in successors)
                {
                    if (arc && s == play + 1)
                        continue;
                    var (sm, si) = graph.StartOf(s);
                    if (WithRepeatTie(Current(sm, si), item) is { } tied)
                    {
                        (changed ??= new())[(sm, si)] = tied;
                        any = true;
                    }
                }
                if (!arc && any && Current(mi, ii) is NoteItem source)
                    (changed ??= new())[(mi, ii)] = source.WithHalfTies(laissezVibrer: true, repeatTie: false);
                else if (!arc && any && Current(mi, ii) is ChordItem chord)
                    changed![(mi, ii)] = chord with
                    {
                        Notes = chord.Notes.Select(m => m with { HasLaissezVibrer = true }).ToImmutableArray(),
                    };
            }
        }
        if (changed == null)
            return measures;
        var builder = measures.ToBuilder();
        foreach (var group in changed.GroupBy(kv => kv.Key.Item1))
        {
            var list = builder[group.Key].Items.ToBuilder();
            foreach (var kv in group)
                list[kv.Key.Item2] = kv.Value;
            builder[group.Key] = builder[group.Key] with { Items = list.ToImmutable() };
        }
        return builder.ToImmutable();
    }

    /// <summary>The target with a repeat tie on each head that repeats a tied pitch, or null
    /// when none does (or it is a rest).</summary>
    private static MusicItem? WithRepeatTie(MusicItem target, MusicItem tied)
    {
        bool Matches(int pos, int midi)
        {
            switch (tied)
            {
                case NoteItem n:
                    return TieDetector.SamePitch(pos, midi, n.StaffPosition, n.Midi);
                case ChordItem c:
                    foreach (var m in c.Notes)
                        if (TieDetector.SamePitch(pos, midi, m.StaffPosition, m.Midi))
                            return true;
                    return false;
                default:
                    return false;
            }
        }
        switch (target)
        {
            case NoteItem n when Matches(n.StaffPosition, n.Midi):
                return n.HasRepeatTie ? n : n.WithHalfTies(laissezVibrer: false, repeatTie: true);
            case ChordItem c:
                bool hit = false;
                var notes = c.Notes.Select(m =>
                {
                    if (!Matches(m.StaffPosition, m.Midi))
                        return m;
                    hit = true;
                    return m with { HasRepeatTie = true };
                }).ToImmutableArray();
                return hit ? c with { Notes = notes } : null;
            default:
                return null;
        }
    }
}