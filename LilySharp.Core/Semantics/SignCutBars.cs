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

/// <summary>
/// THE BARS A SIGN CUTS: a bar of the music that a repeat sign, an ending's edge or a
/// navigation mark written in a section's music stands INSIDE — <c>|: g4 | c d e f | g a b :|
/// g4 | c1 |</c>, the repeat that takes its pickup with it: the three beats before the
/// <c>:|</c> and the beat after it are one bar, as they are one bar with the pickup the repeat
/// returns to. A sign is written as a bar line (GRAMMAR, Barline), so the bar arrives as two
/// short ones; this class is how every reader learns they are one
/// (docs/inline-repeat-design.md §7 stage 4, "a bar a sign cuts").
/// </summary>
/// <remarks>
/// <para>
/// THE RULE is <see cref="SectionBoundaryBars"/>' — a short bar is the first half of a cut bar
/// when every bar PLAYED after it opens with exactly what it lacks, and a bar is the second
/// half when every bar played before it leaves exactly that much open — read on the score's
/// sign column (<see cref="RepeatStream.Next"/>) instead of on the form's sections, and only
/// at a bar line that carries a sign: a short bar with a plain line after it is a short bar
/// (LYS2001), as it always was.
/// </para>
/// <para>
/// The SCORE decides, not each part: the halves are what every music voice that writes both
/// bars writes there (a voice padded through one of them has no say; a chord row has no beats).
/// The lengths are the bar model's (<see cref="MeasureModel.Split"/>), under the section's
/// opening meter and the score's meter plan. A section's declared pickup is the SECOND half of
/// a bar like any first bar (the upbeat finishes the bar before it), never the first half of one.
/// </para>
/// <para>
/// LILYPOND-REF: lily/repeat-acknowledge-engraver.cc, lily/volta-engraver.cc — a repeat sign
/// and a volta bracket stand at whatever moment the music reaches, and
/// lily/timing-translator.cc:478-507 advances currentBarNumber with measurePosition, which
/// neither resets: the bar a sign stands in is one bar and one number. Lily#'s sign is a bar
/// line, so the same bar arrives here as two; this class restores LilyPond's reading.
/// </para>
/// </remarks>
internal sealed class SignCutBars
{
    private readonly RepeatStream _stream;
    private readonly bool[] _first, _second;
    private readonly int[] _from;
    private readonly Fraction[] _head;
    // By the end of a written bar line: it closes the first half of a cut bar / the bar it
    // opens is a second half with this much of its bar behind it — in every play of the form.
    private readonly HashSet<int> _cutLines = new();
    private readonly Dictionary<int, Fraction> _headsBehindLines = new();
    private readonly List<TextSpan> _halves = new();

    /// <summary>The column the bars are numbered on.</summary>
    public RepeatStream Stream => _stream;

    /// <summary>No bar of this form is cut.</summary>
    public bool IsEmpty { get; }

    /// <summary>Whether printed bar <paramref name="bar"/> is the first half of a cut bar:
    /// short, a sign on the line that closes it, and every bar played after it completes it.</summary>
    public bool IsFirstHalf(int bar) => bar >= 0 && bar < _first.Length && _first[bar];

    /// <summary>Whether printed bar <paramref name="bar"/> is the second half of a cut bar:
    /// a sign on the line that opens it, and every bar played before it leaves it open.</summary>
    public bool IsSecondHalf(int bar) => bar >= 0 && bar < _second.Length && _second[bar];

    /// <summary>The printed bar whose bar <paramref name="bar"/> continues — the bar before it,
    /// or, for a later ending, the last bar of the repeat's body — when the bar is a second
    /// half and each of those is a first half; -1 otherwise.</summary>
    public int ContinuedFrom(int bar) => bar >= 0 && bar < _from.Length ? _from[bar] : -1;

    /// <summary>How much of its bar a second half finds behind it (the first half's length).</summary>
    public Fraction HeadOf(int bar) => bar >= 0 && bar < _head.Length ? _head[bar] : Fraction.Zero;

    /// <summary>Whether the written bar line <paramref name="line"/> closes the first half of
    /// a cut bar in every play of its section: the bar behind it is not to be filled out.</summary>
    public bool ClosesAFirstHalf(BarlineSyntax line) => _cutLines.Contains(line.Span.Start + line.Span.Length);

    /// <summary>When the bar the written bar line <paramref name="line"/> opens is a second
    /// half in every play of its section: how much of that bar is behind the line.</summary>
    public Fraction? HeadBehind(BarlineSyntax line)
        => _headsBehindLines.TryGetValue(line.Span.Start + line.Span.Length, out var head) ? head : null;

    /// <summary>The source spans of the halves (<see cref="MeasureModel.Bar.Span"/>) that are a
    /// half in every play of their section — the bars that are not short bars to warn about.</summary>
    public IReadOnlyList<TextSpan> Halves => _halves;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SyntaxNode, Dictionary<string, SignCutBars>> s_byRoot = new();

    /// <summary>The cut bars of <paramref name="form"/> (null: the sections in declaration
    /// order), read once per tree; null for a book whose music writes no sign.</summary>
    public static SignCutBars? For(SyntaxNode root, FormDeclarationSyntax? form)
    {
        if (!RepeatStream.InTheMusic(root))
            return null;
        var byForm = s_byRoot.GetValue(root, static _ => new Dictionary<string, SignCutBars>(StringComparer.Ordinal));
        string key = form == null ? "\n" : form.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (byForm)
        {
            if (!byForm.TryGetValue(key, out var cuts))
                byForm[key] = cuts = new SignCutBars(root, RepeatStream.For(root, form));
            return cuts;
        }
    }

    /// <summary>Where the bars start (<see cref="MeasureModel.Bar.Span"/>) that are a half of a cut bar in every play of
    /// every form of the book (in declaration order, for a book with no form) — what the bar
    /// check exempts. Empty for a book whose music writes no sign.</summary>
    public static HashSet<int> HalvesInEveryForm(SyntaxNode root)
    {
        HashSet<int>? starts = null;
        if (!RepeatStream.InTheMusic(root))
            return [];
        var forms = new List<FormDeclarationSyntax?>();
        foreach (var form in TopLevelNodes.OfRoot<FormDeclarationSyntax>(root))
            forms.Add(form);
        if (forms.Count == 0)
            forms.Add(null);
        foreach (var form in forms)
        {
            var here = new HashSet<int>();
            if (For(root, form) is { } cuts)
                foreach (var span in cuts.Halves)
                    here.Add(span.Start);
            if (starts == null)
                starts = here;
            else
                starts.IntersectWith(here);
        }
        return starts ?? [];
    }

    /// <summary>The part a voice's container writes for: a by-section part block's, a by-part
    /// cell's part, "" for the single-part shorthand.</summary>
    private static string LaneOf(SyntaxNode container) => container switch
    {
        PartBlockSyntax block => block.Name,
        SectionDeclarationSyntax { Parent: PartDeclarationSyntax part } => part.Name.Text,
        _ => "",
    };

    private SignCutBars(SyntaxNode root, RepeatStream stream)
    {
        _stream = stream;
        int n = stream.Bars;
        _first = new bool[n];
        _second = new bool[n];
        _from = new int[n];
        _head = new Fraction[n];
        Array.Fill(_from, -1);
        IsEmpty = true;

        // The bar lines a sign stands on, and nothing to do without one inside the piece.
        var signed = new bool[n + 1];
        foreach (var sign in stream.Signs)
            if (sign.At >= 0 && sign.At <= n)
                signed[sign.At] = true;
        if (n < 2 || !signed.Contains(true))
            return;

        // Every music voice's bars, by section and lane.
        var phrases = SectionBarCounts.PhraseBodies(root);
        var headers = SectionHeaders.Read(root);
        var plan = SectionMeterPlan.Build(root, phrases);
        var home = SectionShortEnds.HomeMeter(root);
        var cells = new Dictionary<string, List<(string Lane, List<MeasureModel.Bar> Bars)>>(StringComparer.Ordinal);
        foreach (var voice in SectionBarCounts.SemanticVoices(root, phrases))
        {
            if (voice.IsChords || voice.IsLyrics)
                continue;
            if (headers.Times.TryGetValue(voice.SectionName, out var time) && time.IsSenzaMisura)
                continue;
            var opening = time != null ? new Fraction(time.Beats, time.BeatType) : home;
            Func<int, TimeSignatureSyntax?>? planned = plan.IsEmpty
                ? null
                : bar => plan.ForeignChangeAt(voice.SectionName, bar, voice.Container);
            var bars = MeasureModel.Split(voice.Container, phrases, opening, plannedMeterAt: planned);
            if (bars.Count != voice.Bars)
                continue;
            if (!cells.TryGetValue(voice.SectionName, out var list))
                cells[voice.SectionName] = list = new();
            list.Add((LaneOf(voice.Container), bars));
        }

        // What each lane writes in each printed bar.
        var column = new Dictionary<string, MeasureModel.Bar>?[n];
        var pickup = new bool[n];
        foreach (var play in stream.Plays)
        {
            if (!cells.TryGetValue(play.Section, out var lanes))
                continue;
            if (play.Bars > 0 && headers.Partials.ContainsKey(play.Section))
                pickup[play.FirstBar] = true;
            foreach (var (lane, bars) in lanes)
                for (int k = 0; k < bars.Count && k < play.Bars; k++)
                    (column[play.FirstBar + k] ??= new(StringComparer.Ordinal))[lane] = bars[k];
        }

        // The halves two bars are when one is played after the other: what every lane that
        // writes both writes — the first short of its meter, the two together one bar of it.
        bool Halves(int before, int after, out Fraction head)
        {
            head = Fraction.Zero;
            if (column[before] is not { } a || column[after] is not { } b || pickup[before])
                return false;
            bool some = false;
            foreach (var (lane, x) in a)
            {
                if (!b.TryGetValue(lane, out var y))
                    continue;
                if (x.IsEmpty || y.IsEmpty || x.Meter is not { } meter
                    || x.Duration <= Fraction.Zero || y.Duration <= Fraction.Zero
                    || x.Duration >= meter || x.Duration + y.Duration != meter
                    || (some && x.Duration != head))
                    return false;
                (some, head) = (true, x.Duration);
            }
            return some;
        }

        var before = new List<int>[n];
        for (int b = 0; b < n; b++)
            foreach (int next in stream.Next(b))
                (before[next] ??= new()).Add(b);
        int opening0 = stream.Played.Count > 0 ? stream.Played[0].From : -1;

        for (int b = 0; b < n; b++)
        {
            var next = stream.Next(b);
            _first[b] = signed[b + 1] && next.Count > 0 && next.All(after => Halves(b, after, out _));
            if (!signed[b] || b == opening0 || before[b] is not { Count: > 0 } played)
                continue;
            var head = Fraction.Zero;
            _second[b] = played.All(p => Halves(p, b, out head));
            if (_second[b])
                _head[b] = head;
        }
        for (int b = 0; b < n; b++)
        {
            if (!_second[b] || !before[b]!.All(p => _first[p]))
                continue;
            // The bar printed before it, or — a later ending — the body's last bar.
            _from[b] = before[b]!.Contains(b - 1) ? b - 1 : before[b]!.Where(p => p < b).DefaultIfEmpty(-1).Max();
            IsEmpty &= _from[b] < 0;
        }
        if (IsEmpty)
            return;

        // The same answers by what is WRITTEN: a bar of a voice is a half when it is one in
        // every play of its section.
        foreach (var (section, lanes) in cells)
        {
            var plays = stream.Plays.Where(p => p.Section == section && p.Bars > 0).ToList();
            if (plays.Count == 0)
                continue;
            foreach (var (_, bars) in lanes)
                for (int k = 0; k < bars.Count; k++)
                {
                    bool first = plays.All(p => k < p.Bars && _first[p.FirstBar + k]);
                    bool second = plays.All(p => k < p.Bars && _second[p.FirstBar + k]);
                    bool continues = plays.All(p => k < p.Bars && _from[p.FirstBar + k] >= 0);
                    if (first || second)
                        _halves.Add(bars[k].Span);
                    if (first && bars[k].BarLineEnd >= 0)
                        _cutLines.Add(bars[k].BarLineEnd);
                    if (continues && k > 0 && bars[k - 1].BarLineEnd >= 0)
                        _headsBehindLines[bars[k - 1].BarLineEnd] = _head[plays[0].FirstBar + k];
                }
        }
    }
}
