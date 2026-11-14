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
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>What a <see cref="RepeatSign"/> says where it stands.</summary>
internal enum RepeatSignKind
{
    /// <summary>A <c>|:</c>.</summary>
    RepeatStart,
    /// <summary>A <c>:|</c>, with its <c>*N</c> when one is written.</summary>
    RepeatEnd,
    /// <summary>A volta ending opens (<c>[1.</c>), with the passes it names.</summary>
    EndingStart,
    /// <summary>A volta ending closes (its <c>]</c>, or the <c>:|</c> that closes it).</summary>
    EndingEnd,
    /// <summary>A navigation mark: a sign (segno, coda) or a text (fine, to coda, dc, ds).</summary>
    Navigation,
}

/// <summary>A repeat sign, an ending's edge or a navigation mark at a place of the PRINTED
/// music — the music laid out once, in the order the page prints it. The axis is the reader's:
/// ticks for the MIDI (<c>Midi.PlayedTimeline</c>), bar lines for <see cref="RepeatStream"/>.</summary>
/// <param name="Lane">Who noted it: the part (or the chord row) whose music writes it, null for the
/// form. Every lane walking the same bars notes the same sign, and those are ONE sign; two alike
/// in one lane are two (<c>:| :|</c>).</param>
internal readonly record struct RepeatSign(
    int At, RepeatSignKind Kind, int? Count = null, PassSet Passes = default,
    NavigationMarkType? Navigation = null, string? Lane = null);

/// <summary>
/// THE SCORE'S SIGN COLUMN: a form's sections laid out in the order the page prints them, bar
/// by bar, with every repeat sign, ending and navigation mark — the form's own and the ones
/// the sections' music writes, in any voice — at the bar line it stands on; and the order a
/// performer plays those bars in. One reader for a sign wherever it is written
/// (docs/inline-repeat-design.md §3.2, §3.3, §4).
/// </summary>
/// <remarks>
/// <para>
/// The rules are the form's (GRAMMAR §6), read on the column — <see cref="Route"/>, the one
/// expansion the MIDI of such a book takes too (<c>Midi.PlayedTimeline</c>, on ticks): a run is
/// a <c>|:</c>, its body, its endings and its <c>:|</c>s; it plays <see cref="RepeatPasses.Count"/>
/// times, the body and then the ending the pass names (<see cref="RepeatPasses.EndingFor"/>).
/// A <c>:|</c> no <c>|:</c> opened repeats the piece from its beginning, once. The jump texts are
/// followed along <see cref="FormRoute"/>: a replayed stretch plays each run once, on its last
/// pass, and rewinds nothing. A mark inside a run is not a point the route addresses
/// (FormRoute's remarks), and a <c>|:</c> nothing closes repeats nothing (LYS4017 says so).
/// </para>
/// <para>
/// A place on the column is a BAR LINE, numbered by the printed bars before it: a section's
/// play takes the section's bars (<see cref="SectionBarCounts"/>, the semantic count every
/// exporter pads by), and a sign of its music stands at the play's first bar plus the bars of
/// the section before the sign (<see cref="SectionSigns"/>). What a reader asks of it is
/// <see cref="Next"/>: the bars played right after a bar — where a tie out of the bar goes, and
/// which bar's head may finish a bar a sign cuts short.
/// </para>
/// <para>
/// LILYSHARP-OWN: the language's reading of its own signs. Departs from: LilyPond unfolds the
/// music tree (scm/music-functions.scm unfold-repeats), which needs the repeat written around
/// the same music in every voice; a Lily# repeat is the SCORE's and is written once, in any
/// part, and may open in one section and close in a later one. Goes away: never on its own.
/// Observed by: <c>RepeatStreamTests</c>, and through the MIDI by <c>InlineRepeatMidiTests</c>.
/// </para>
/// </remarks>
internal sealed class RepeatStream
{
    /// <summary>A stretch <c>[From, To)</c> of the printed music, played as it stands.</summary>
    internal readonly record struct Segment(int From, int To);

    /// <summary>One printed play of a section: its bars are <c>[FirstBar, FirstBar + Bars)</c>.</summary>
    internal readonly record struct Play(string Section, int FirstBar, int Bars);

    /// <summary>The section plays in printed order.</summary>
    public IReadOnlyList<Play> Plays { get; }

    /// <summary>The signs, each at its bar line (<see cref="RepeatSign.At"/> = the printed bars before it).</summary>
    public IReadOnlyList<RepeatSign> Signs { get; }

    /// <summary>The printed bars.</summary>
    public int Bars { get; }

    /// <summary>The stretches of printed bars in played order; every seam is a jump.</summary>
    public IReadOnlyList<Segment> Played { get; }

    private readonly List<int>[] _next;

    /// <summary>A repeat of the column: its <c>|:</c>, where its body ends (its first ending, or
    /// its <c>:|</c>), where the whole run ends, its written <c>:|*N</c> and its endings.</summary>
    internal readonly record struct RepeatRun(int Start, int BodyEnd, int End, int? Count,
        IReadOnlyList<(PassSet Passes, int From, int To)> Endings);

    /// <summary>The repeats of the column, in order — what the rules about a repeat's endings
    /// are asked of (<c>MusicRepeatValidator</c>).</summary>
    public IReadOnlyList<RepeatRun> Runs { get; }

    /// <summary>The bar lines a <c>|:</c> stands on while the <c>|:</c> before it is still
    /// open: repeats do not nest (docs/inline-repeat-design.md §3.3).</summary>
    public IReadOnlyList<int> NestedAt { get; }

    /// <summary>The bar lines an ending opens on with no repeat open: its music is plain.</summary>
    public IReadOnlyList<int> LoneEndingsAt { get; }

    private sealed class Faults
    {
        public readonly List<int> Nested = [];
        public readonly List<int> LoneEndings = [];
    }

    private RepeatStream(List<Play> plays, List<RepeatSign> written, int bars)
    {
        var signs = Normalized(written);
        Plays = plays;
        Signs = signs;
        Bars = bars;
        Played = Route(signs, bars);
        var faults = new Faults();
        var runs = new List<RepeatRun>();
        foreach (var element in Elements(signs, bars, faults))
            if (element is Run run)
                runs.Add(new RepeatRun(run.Start, run.BodyEnd, run.End, run.Count, run.Endings));
        (Runs, NestedAt, LoneEndingsAt) = (runs, faults.Nested, faults.LoneEndings);
        _next = new List<int>[bars];
        for (int b = 0; b < bars; b++)
            _next[b] = [];
        int last = -1;
        foreach (var segment in Played)
        {
            for (int b = segment.From; b < segment.To && b < bars; b++)
            {
                if (last >= 0 && !_next[last].Contains(b))
                    _next[last].Add(b);
                last = b;
            }
        }
    }

    /// <summary>The printed bars played right after <paramref name="bar"/>, in the order the
    /// piece first reaches them — none after the bar the piece ends on (and nothing else).</summary>
    public IReadOnlyList<int> Next(int bar) => bar >= 0 && bar < _next.Length ? _next[bar] : [];

    /// <summary>Whether the bar printed after <paramref name="bar"/> is also played after it.</summary>
    public bool PrintedNextIsPlayedNext(int bar) => Next(bar).Contains(bar + 1);

    /// <summary>The printed play <paramref name="bar"/> stands in (-1 outside the column).</summary>
    public int PlayOf(int bar)
    {
        for (int p = 0; p < Plays.Count; p++)
            if (bar >= Plays[p].FirstBar && bar < Plays[p].FirstBar + Plays[p].Bars)
                return p;
        return -1;
    }

    /// <summary>The signs standing at bar line <paramref name="barLine"/> (the printed bars
    /// before it), in the order they are read there.</summary>
    public IEnumerable<RepeatSign> SignsAt(int barLine)
    {
        foreach (var sign in Signs)
            if (sign.At == barLine)
                yield return sign;
    }

    /// <summary>Whether the book writes a repeat sign, a volta ending or a navigation mark in
    /// its music — anywhere but in a form (the three node kinds RepeatStructureScopeValidator
    /// asks about). A book that writes none is read by the form's own readers, as it always was.</summary>
    /// <remarks>
    /// Asked by the page's collector for every voice it finishes, on every keystroke — so it
    /// walks GREENS, builds no red node and no descendant index (<c>KeystrokeStaysRedFreeTests</c>),
    /// and remembers the answer of each top-level declaration by its green: a keystroke
    /// re-walks the one declaration it changed.
    /// </remarks>
    public static bool InTheMusic(SyntaxNode root)
    {
        var green = root.Green;
        for (int i = 0; i < green.SlotCount; i++)
        {
            if (green.GetSlot(i) is not { } declaration)
                continue;
            if (!s_signsInGreen.TryGetValue(declaration, out var known))
                s_signsInGreen.AddOrUpdate(declaration, known = WritesSigns(declaration) ? s_yes : s_no);
            if (ReferenceEquals(known, s_yes))
                return true;
        }
        return false;
    }

    private static readonly object s_yes = new(), s_no = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Syntax.InternalSyntax.GreenNode, object> s_signsInGreen = new();

    private static bool WritesSigns(Syntax.InternalSyntax.GreenNode node)
    {
        switch (node.Kind)
        {
            case SyntaxKind.FormDeclaration:
                return false;
            case SyntaxKind.InlineVolta or SyntaxKind.NavigationMark:
                return true;
        }
        bool line = node.Kind == SyntaxKind.Barline;
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetSlot(i) is not { } child)
                continue;
            if (line ? SyntaxFacts.IsRepeatBarlineKind(child.Kind) : WritesSigns(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The signs of one bar line as a STAMP — what the page's collector writes on the first
    /// item of the bar behind the line (<c>MusicItem.ScoreSignsBefore</c>), so the readers of a
    /// finished voice can rebuild the column from the voice alone
    /// (<c>Svg.Collector.SectionPlayGraph</c>). The signs in reading order, joined with
    /// <c>|</c>: <c>S</c> a <c>|:</c>, <c>E</c> / <c>E3</c> a <c>:|</c> and its count,
    /// <c>V1,2</c> an ending's opening and its passes, <c>v</c> its close, <c>N</c> and the
    /// name a navigation mark. Null for a line with no sign.
    /// </summary>
    public static string? Encode(IEnumerable<RepeatSign> signs)
    {
        System.Text.StringBuilder? text = null;
        foreach (var sign in signs)
        {
            text = text == null ? new System.Text.StringBuilder() : text.Append('|');
            switch (sign.Kind)
            {
                case RepeatSignKind.RepeatStart: text.Append('S'); break;
                case RepeatSignKind.RepeatEnd: text.Append('E').Append(sign.Count?.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
                case RepeatSignKind.EndingStart: text.Append('V').Append(sign.Passes.ToString()); break;
                case RepeatSignKind.EndingEnd: text.Append('v'); break;
                default: text.Append('N').Append(sign.Navigation); break;
            }
        }
        return text?.ToString();
    }

    /// <summary>The signs of a stamp (<see cref="Encode"/>), standing at <paramref name="at"/>.</summary>
    public static IEnumerable<RepeatSign> Decode(string? stamp, int at)
    {
        if (string.IsNullOrEmpty(stamp))
            yield break;
        foreach (var word in stamp.Split('|'))
        {
            if (word.Length == 0)
                continue;
            string rest = word[1..];
            switch (word[0])
            {
                case 'S':
                    yield return new RepeatSign(at, RepeatSignKind.RepeatStart);
                    break;
                case 'E':
                    yield return new RepeatSign(at, RepeatSignKind.RepeatEnd,
                        Count: int.TryParse(rest, out int count) ? count : null);
                    break;
                case 'V':
                    var passes = new List<int>();
                    foreach (var pass in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(pass, out int p))
                            passes.Add(p);
                    yield return new RepeatSign(at, RepeatSignKind.EndingStart, Passes: PassSet.Of(passes));
                    break;
                case 'v':
                    yield return new RepeatSign(at, RepeatSignKind.EndingEnd);
                    break;
                case 'N' when Enum.TryParse<NavigationMarkType>(rest, out var mark):
                    yield return new RepeatSign(at, RepeatSignKind.Navigation, Navigation: mark);
                    break;
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxNode, Dictionary<string, RepeatStream>> s_byRoot = new();

    /// <summary>The column of <paramref name="form"/>, read once per tree (the page's collector
    /// asks it for every voice it finishes).</summary>
    public static RepeatStream For(SyntaxNode root, FormDeclarationSyntax? form)
    {
        var byForm = s_byRoot.GetValue(root, static _ => new Dictionary<string, RepeatStream>(StringComparer.Ordinal));
        string key = form == null ? "\n" : form.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (byForm)
        {
            if (!byForm.TryGetValue(key, out var stream))
                byForm[key] = stream = Of(root, form);
            return stream;
        }
    }

    /// <summary>The column of <paramref name="form"/> (null: the sections in declaration
    /// order, the reading of a file with no form).</summary>
    public static RepeatStream Of(SyntaxNode root, FormDeclarationSyntax? form)
    {
        var phrases = SectionBarCounts.PhraseBodies(root);
        var voices = SectionBarCounts.SemanticVoices(root, phrases);
        var items = form != null ? FormWalk.Read(form) : DeclaredOrder(voices);
        return Of(items, voices, phrases);
    }

    /// <summary>The column of a form's items (<see cref="FormWalk.Read"/>) over
    /// <paramref name="voices"/>, the book's section voices.</summary>
    public static RepeatStream Of(IReadOnlyList<FormWalk.Item> items,
        IReadOnlyList<SectionBarCounts.SemanticVoice> voices, IReadOnlyDictionary<string, SyntaxNode> phrases)
    {
        var canonical = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var voice in voices)
            canonical[voice.SectionName] = Math.Max(canonical.GetValueOrDefault(voice.SectionName), voice.Bars);
        var sectionSigns = new Dictionary<string, SectionSigns>(StringComparer.Ordinal);
        var plays = new List<Play>();
        var signs = new List<RepeatSign>();
        int bar = 0;

        void PlaySection(string name)
        {
            int bars = canonical.GetValueOrDefault(name);
            plays.Add(new Play(name, bar, bars));
            if (!sectionSigns.TryGetValue(name, out var written))
                sectionSigns[name] = written = SectionSigns.Of(name, voices, phrases);
            AddTheMusics(written, bar, bars, signs);
            bar += bars;
        }

        // The form walked once, as printed — MidiExporter.PlayFormAsPrinted's walk, on bars.
        void Walk(IReadOnlyList<FormWalk.Item> list, int? count)
        {
            foreach (var item in list)
            {
                switch (item)
                {
                    case FormWalk.SectionRef s:
                        PlaySection(s.Name);
                        break;
                    case FormWalk.Repeat repeat:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.RepeatStart));
                        Walk(repeat.Children, repeat.ExplicitPlayCount);
                        break;
                    // Inside a block, the block's ':|' with the block's '*N'; and a ':|:' is both.
                    case FormWalk.RepeatEnd:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.RepeatEnd, Count: count));
                        break;
                    case FormWalk.BothBar:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.RepeatEnd, Count: count));
                        signs.Add(new RepeatSign(bar, RepeatSignKind.RepeatStart));
                        break;
                    case FormWalk.Ending e:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.EndingStart, Passes: PassSet.Of(e.Node.Numbers)));
                        foreach (var s in e.Sections)
                            PlaySection(s.Name);
                        signs.Add(new RepeatSign(bar, RepeatSignKind.EndingEnd));
                        break;
                    case FormWalk.LoneRepeatEnd:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.RepeatEnd));
                        break;
                    case FormWalk.Other { Node: NavigationMarkSyntax nav }:
                        signs.Add(new RepeatSign(bar, RepeatSignKind.Navigation, Navigation: nav.MarkType));
                        break;
                }
            }
        }
        Walk(items, null);
        return new RepeatStream(plays, signs, bar);
    }

    /// <summary>The lane of every sign the music writes: a section's voices are read as one
    /// set (<see cref="SectionSigns"/> keeps the first sign at each place), so the music is one
    /// lane beside the form's — a <c>|:</c> the form writes where a section's music opens with
    /// one is ONE sign, as on the MIDI's timeline.</summary>
    private const string Music = "music";

    private static void AddTheMusics(SectionSigns written, int first, int bars, List<RepeatSign> signs)
    {
        foreach (var (place, sign) in written.Lines)
        {
            if (place > bars)
                continue;
            var kind = sign.Node.BarToken.Kind;
            if (kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
                signs.Add(new RepeatSign(first + place, RepeatSignKind.RepeatEnd,
                    Count: sign.Node.HasExplicitRepeatCount ? sign.Node.RepeatCount : null, Lane: Music));
            if (kind is SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar)
                signs.Add(new RepeatSign(first + place, RepeatSignKind.RepeatStart, Lane: Music));
        }
        // One ending at a place, whichever voices write it (LYS1070's sibling for endings is
        // not written yet: the first voice's numbers stand).
        var taken = new HashSet<int>();
        foreach (var ending in written.Endings)
        {
            if (ending.From >= bars || !taken.Add(ending.From))
                continue;
            signs.Add(new RepeatSign(first + ending.From, RepeatSignKind.EndingStart,
                Passes: PassSet.Of(ending.Node.Numbers), Lane: Music));
            signs.Add(new RepeatSign(first + Math.Min(ending.To, bars), RepeatSignKind.EndingEnd, Lane: Music));
        }
        var marked = new HashSet<(int, NavigationMarkType)>();
        foreach (var mark in written.Marks)
            if (mark.Bars <= bars && marked.Add((mark.Bars, mark.Node.MarkType)))
                signs.Add(new RepeatSign(first + mark.Bars, RepeatSignKind.Navigation,
                    Navigation: mark.Node.MarkType, Lane: Music));
    }

    /// <summary>A file with no form plays its sections in declaration order, each once.</summary>
    private static List<FormWalk.Item> DeclaredOrder(IReadOnlyList<SectionBarCounts.SemanticVoice> voices)
    {
        var items = new List<FormWalk.Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var voice in voices)
            if (seen.Add(voice.SectionName))
                items.Add(new FormWalk.SectionRef(voice.SectionName, null, Silent: false, voice.Container, 0));
        return items;
    }

    // ───────────────────────── the one expansion ─────────────────────────

    private abstract record Element;
    private sealed record Plain(int From, int To) : Element;
    private sealed record Run(int Start, int BodyEnd, int End, int? Count,
        List<(PassSet Passes, int From, int To)> Endings) : Element;
    private sealed record LoneEnd : Element;
    private sealed record Sign(NavigationMarkType Type) : Element;

    /// <summary>The stretches of printed music <c>[0, end)</c> in played order, along
    /// <paramref name="signs"/> — on whatever axis they stand (ticks, bar lines).</summary>
    internal static List<Segment> Route(IEnumerable<RepeatSign> signs, int end)
    {
        var elements = Elements(signs, end);
        var marks = new List<NavigationMarkType?>(elements.Count);
        foreach (var element in elements)
            marks.Add(element is Sign sign ? sign.Type : null);

        var played = new List<Segment>();
        foreach (var stretch in FormRoute.Of(marks))
        {
            for (int i = stretch.From; i < stretch.To; i++)
            {
                // A ':|' no '|:' opened: the piece from its beginning, once more — on the first
                // pass only (one rewind per written sign, MidiExporter.PlayForm's rule).
                if (elements[i] is LoneEnd && !stretch.Replay)
                {
                    for (int j = 0; j < i; j++)
                        PlayElement(elements[j], lastPassOnly: false, played);
                    continue;
                }
                PlayElement(elements[i], stretch.Replay, played);
            }
        }
        return Joined(played);
    }

    private static void PlayElement(Element element, bool lastPassOnly, List<Segment> played)
    {
        switch (element)
        {
            case Plain plain:
                Add(played, plain.From, plain.To);
                break;
            case Run run:
                var passes = new List<PassSet>(run.Endings.Count);
                foreach (var ending in run.Endings)
                    passes.Add(ending.Passes);
                int count = RepeatPasses.Count(run.Count, passes);
                for (int pass = lastPassOnly ? count : 1; pass <= count; pass++)
                {
                    Add(played, run.Start, run.BodyEnd);
                    int ending = RepeatPasses.EndingFor(pass, passes);
                    if (ending >= 0)
                        Add(played, run.Endings[ending].From, run.Endings[ending].To);
                }
                break;
        }
    }

    private static void Add(List<Segment> played, int from, int to)
    {
        if (from < to)
            played.Add(new Segment(from, to));
    }

    /// <summary>Neighbours that follow each other in the printed music are one stretch, so
    /// every seam left is a jump.</summary>
    private static List<Segment> Joined(List<Segment> played)
    {
        var joined = new List<Segment>(played.Count);
        foreach (var segment in played)
        {
            if (joined.Count > 0 && joined[^1].To == segment.From)
                joined[^1] = new Segment(joined[^1].From, segment.To);
            else
                joined.Add(segment);
        }
        return joined;
    }

    /// <summary>At one place: an ending closes, the repeat ends, a text of the bar behind it, a
    /// sign of the bar ahead, the next repeat opens, an ending opens.</summary>
    private static int Rank(RepeatSign sign) => sign.Kind switch
    {
        RepeatSignKind.EndingEnd => 0,
        RepeatSignKind.RepeatEnd => 1,
        RepeatSignKind.Navigation => sign.Navigation is NavigationMarkType.Segno or NavigationMarkType.Coda ? 3 : 2,
        RepeatSignKind.RepeatStart => 4,
        _ => 5,
    };

    /// <summary>The signs as ONE set, in reading order: every lane that walks a sign notes it,
    /// so there are as many of a sign as the lane that wrote the most of it (the record's
    /// remark). Idempotent — the result is one lane's.</summary>
    private static List<RepeatSign> Normalized(IEnumerable<RepeatSign> signs)
        => signs.GroupBy(m => m with { Lane = null })
            .SelectMany(g => Enumerable.Repeat(g.Key, g.GroupBy(m => m.Lane).Max(lane => lane.Count())))
            .OrderBy(m => m.At).ThenBy(Rank).ToList();

    private static List<Element> Elements(IEnumerable<RepeatSign> signs, int end, Faults? faults = null)
    {
        var sorted = Normalized(signs);
        var elements = new List<Element>();
        int pos = 0;            // where the plain stretch being gathered began
        int runStart = -1;      // the open run's '|:' (or -1)
        int bodyEnd = -1;       // …where its body ends: its first ending, or its ':|'
        int? count = null;      // …its written ':|*N'
        int lastEnd = -1;       // …its last ':|' (or -1 before the first)
        int tail = -1;          // …where it ends so far: that ':|', or an ending closed after it
        var endings = new List<(PassSet Passes, int From, int To)>();
        (PassSet Passes, int From)? open = null; // the ending being read

        void Flush(int upTo)
        {
            if (pos < upTo)
                elements.Add(new Plain(pos, upTo));
            pos = Math.Max(pos, upTo);
        }
        void CloseEnding(int at)
        {
            if (open is { } ending)
                endings.Add((ending.Passes, ending.From, at));
            open = null;
        }
        void CloseRun(int at)
        {
            CloseEnding(at);
            elements.Add(new Run(runStart, bodyEnd, at, count, endings));
            pos = at;
            (runStart, bodyEnd, count, lastEnd, tail) = (-1, -1, null, -1, -1);
            endings = new List<(PassSet, int, int)>();
        }

        foreach (var mark in sorted)
        {
            bool inRun = runStart >= 0;
            // A run that has met its ':|' goes on only through an ending opening where it ends
            // so far, or a further ':|' closing such an ending; anything else finds it finished.
            if (inRun && lastEnd >= 0 && open == null
                && !(mark.Kind == RepeatSignKind.EndingStart && mark.At == tail)
                && !(mark.Kind == RepeatSignKind.RepeatEnd && mark.At == tail && tail > lastEnd))
            {
                CloseRun(tail);
                inRun = false;
            }
            switch (mark.Kind)
            {
                case RepeatSignKind.RepeatStart:
                    if (inRun && lastEnd >= 0)
                        CloseRun(mark.At); // a trailing ending nothing closed ends here
                    // A '|:' nothing closed repeats nothing: its music is plain (LYS4017).
                    if (runStart >= 0 && lastEnd < 0 && mark.At > runStart)
                        faults?.Nested.Add(mark.At);
                    Flush(mark.At);
                    (runStart, bodyEnd, count, lastEnd, tail) = (mark.At, -1, null, -1, -1);
                    endings = new List<(PassSet, int, int)>();
                    open = null;
                    break;
                case RepeatSignKind.RepeatEnd:
                    if (!inRun)
                    {
                        Flush(mark.At);
                        elements.Add(new LoneEnd());
                        break;
                    }
                    CloseEnding(mark.At);
                    if (bodyEnd < 0)
                        bodyEnd = mark.At;
                    count ??= mark.Count;
                    lastEnd = tail = mark.At;
                    break;
                case RepeatSignKind.EndingStart:
                    // An ending no repeat opens is its plain music, once (GRAMMAR §6, LYS6008).
                    if (!inRun)
                    {
                        faults?.LoneEndings.Add(mark.At);
                        break;
                    }
                    CloseEnding(mark.At);
                    if (bodyEnd < 0)
                        bodyEnd = mark.At;
                    open = (mark.Passes, mark.At);
                    break;
                case RepeatSignKind.EndingEnd:
                    if (inRun && open != null)
                    {
                        CloseEnding(mark.At);
                        if (lastEnd >= 0)
                            tail = mark.At;
                    }
                    break;
                case RepeatSignKind.Navigation:
                    if (!inRun && mark.Navigation is { } type)
                    {
                        Flush(mark.At);
                        elements.Add(new Sign(type));
                    }
                    break;
            }
        }
        if (runStart >= 0 && lastEnd >= 0)
            CloseRun(open != null ? end : tail);
        Flush(end);
        return elements;
    }
}
