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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// The score's repeat signs in every voice's stream: a `|:`, a `:|` or a `[N. … ]` written in
// one voice of a section is written into the play of every other voice (parts and chord
// tracks), on the same bar line, before EmitMusicStream groups the stream into \repeat volta.
public sealed partial class LilyPondExporter
{
    private readonly Dictionary<string, SectionSigns> _sectionSigns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _signWarned = new(StringComparer.Ordinal);

    /// <summary>What the voices of <paramref name="name"/> write of the score's repeats
    /// (<see cref="SectionSigns"/>, the reading the score's sign column shares), read once per
    /// export.</summary>
    private SectionSigns SignsOf(string name)
    {
        if (!_sectionSigns.TryGetValue(name, out var signs))
            _sectionSigns[name] = signs = SectionSigns.Of(name, _sectionBars.ByContainer.Values, _phraseBodies);
        return signs;
    }

    /// <summary>
    /// Writes the score's repeat signs into one voice's play of <paramref name="name"/> — the
    /// items of <paramref name="result"/> from <paramref name="from"/> on, its padding bars
    /// included (docs/inline-repeat-design.md §3.2: a sign written in one voice of a section
    /// is every voice's).
    /// </summary>
    /// <remarks>
    /// <para>
    /// LilyPond prints a <c>\repeat volta</c> of one staff on every staff, so the page of the
    /// twin was already whole; but each voice is its own music expression, and one that carries
    /// no <c>\repeat</c> is played straight through by <c>\unfoldRepeats</c> and by the MIDI —
    /// beside a voice that repeats. MEASURED before (2026-11-14, Lab sessions/p1243/stage0.md):
    /// only the voice that wrote the sign carried the <c>\repeat volta</c>.
    /// </para>
    /// <para>
    /// A sign stands on this voice's bar line with the same bars of the section before it: a
    /// <c>|:</c> takes the plain <c>|</c>'s place (it pairs like one — an empty bar before it
    /// stays a bar), a <c>:|</c> or a <c>:|:</c> is written after it (on an empty span it
    /// decorates the bar behind it, <see cref="MeasureModel.Split"/>), and a <c>|:</c> that
    /// opens the section opens this play. An ending holds this voice's own items between the
    /// two bar lines (<see cref="SpreadEndingMarker"/>). A place this voice has no bar line of
    /// its own at — the bar is inside a phrase it plays, or an <c>R1*4</c> — is left unwritten
    /// and said in the warnings.
    /// </para>
    /// <para>
    /// A row's own endings are written by this pass too: they stand flat among its chords,
    /// and <see cref="ChordBarsOf"/> keeps the bar lines only.
    /// </para>
    /// </remarks>
    private void WriteTheScoresRepeatSigns(string name, SyntaxNode? container, List<SyntaxNode> result, int from)
    {
        var signs = SignsOf(name);
        if (signs.HasNoRepeats || from >= result.Count || !_carriesTheSigns)
            return;
        var voice = container != null ? VoiceOf(container) : null;
        var voiceKey = voice?.Container;

        // The bars standing before what follows each top-level bar line of the play (-1: not a
        // bar line, or one the count cannot place).
        var items = result.GetRange(from, result.Count - from);
        var bars = PlaceTheBarLines(items, voice);

        foreach (var (place, sign) in signs.Lines)
        {
            if (ReferenceEquals(sign.Container, voiceKey))
                continue;
            var kind = sign.Node.BarToken.Kind;
            // The voice writes a sign of its own there (LYS1070 holds them to agree).
            if (Enumerable.Range(0, items.Count).Any(i => bars[i] == place && items[i] is BarlineSyntax own
                    && SyntaxFacts.IsRepeatBarlineKind(own.BarToken.Kind)))
                continue;
            int count = sign.Node.HasExplicitRepeatCount ? sign.Node.RepeatCount : 0;
            int at = bars.IndexOf(place);
            if (at < 0 && place == 0 && kind == SyntaxKind.RepeatStartBar)
            {
                items.Insert(0, CreateBarline(kind, "|:", sign.Node.Position, 0));
                bars.Insert(0, 0);
            }
            else if (at < 0 || items[at] is not BarlineSyntax { BarToken.Kind: SyntaxKind.Bar } plain)
                WarnTheSignIsNotWritten(name, place, sign.Node.BarToken.Text);
            else if (kind == SyntaxKind.RepeatStartBar)
                items[at] = InPlaceOf(plain, CreateBarline(kind, "|:", plain.Position, 0));
            else
            {
                items.Insert(at + 1, CreateBarline(kind, sign.Node.BarToken.Text, plain.Position, count));
                bars.Insert(at + 1, place);
            }
        }

        // Endings, the last first, so the places before it stay where they were counted.
        var taken = new HashSet<int>();
        foreach (var ending in signs.Endings
                     .OrderByDescending(e => e.From)
                     .ThenByDescending(e => ReferenceEquals(e.Container, voiceKey)))
        {
            if (!taken.Add(ending.From))
                continue;
            // A part's own ending holds its music already.
            if (ending.Nested && ReferenceEquals(ending.Container, voiceKey))
                continue;
            int start = ending.From == 0
                ? (items.Count > 0 && items[0] is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar } ? 1 : 0)
                : bars.LastIndexOf(ending.From) + 1;
            int close = bars.IndexOf(ending.To);
            if ((ending.From > 0 && start == 0) || close < 0 || close < start)
            {
                WarnTheSignIsNotWritten(name, ending.From, "[" + ending.Node.VoltaText);
                continue;
            }
            // The plain line that closes the ending's last bar is the ending's; a sign is not.
            int end = items[close] is BarlineSyntax { BarToken.Kind: SyntaxKind.Bar } ? close + 1 : close;
            if (end <= start)
                continue;
            var marker = new SpreadEndingMarker(ending.Node, items.GetRange(start, end - start));
            items.RemoveRange(start, end - start);
            bars.RemoveRange(start, end - start);
            items.Insert(start, marker);
            bars.Insert(start, -1);
        }

        result.RemoveRange(from, result.Count - from);
        result.AddRange(items);
    }

    // ---- a tie carried over a repeat the MUSIC writes ------------------------------------
    //
    // The carry rule is the form's (LilyPondExporter.Music.cs, "a tie carried over a repeat"),
    // read where the signs stand: in a book whose music writes repeat signs the bar played
    // after a bar is the score's sign column's answer (Semantics.RepeatStream.Next), not the
    // form's alone. A tie that ends a bar of this part reaches the first note of every bar
    // played after it; LilyPond draws the arc to the bar printed next, and every other such
    // bar takes \repeatTie — the head of the repeat a first ending returns to, the second
    // ending a tie out of the body also enters. MEASURED before (2026-11-15, Lab
    // sessions/p1247/probe/t-b-endings, t-c-first): neither note carried one, where the same
    // music spelled with the form's signs did (t-e-formtwin, t-f-formback).

    // By printed play of the column: the bars of the section whose first note takes \repeatTie
    // in the part being written. Null in a book whose music writes no sign — the form's
    // reading stands there (RepeatTiePlays).
    private Dictionary<int, HashSet<int>>? _repeatTieBars;
    private RepeatStream? _repeatTieStream;
    // The bars a sign written in the music cuts, for the form being written (null: none).
    private SignCutBars? _signCuts;

    private Dictionary<int, HashSet<int>>? RepeatTieBars(FormDeclarationSyntax? form,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName)
    {
        _repeatTieStream = null;
        _signCuts = null;
        if (_shared.Root is not { } root || !RepeatStream.InTheMusic(root))
            return null;
        _signCuts = SignCutBars.For(root, form) is { IsEmpty: false } cuts ? cuts : null;
        var stream = _repeatTieStream = RepeatStream.For(root, form);
        var result = new Dictionary<int, HashSet<int>>();
        for (int p = 0; p < stream.Plays.Count; p++)
        {
            var play = stream.Plays[p];
            if (!byName.TryGetValue(play.Section, out var entry) || VoiceOf(entry.Container) is not { } voice)
                continue;
            foreach (int k in TiedBars(voice))
            {
                if (k < 0 || k >= play.Bars)
                    continue;
                int bar = play.FirstBar + k;
                foreach (int next in stream.Next(bar))
                {
                    int q = next == bar + 1 ? -1 : stream.PlayOf(next);
                    if (q < 0)
                        continue;
                    if (!result.TryGetValue(q, out var bars))
                        result[q] = bars = new HashSet<int>();
                    bars.Add(next - stream.Plays[q].FirstBar);
                }
            }
        }
        return result;
    }

    /// <summary>The bars of its section (0-based) a part voice leaves on a tie: the event
    /// before the bar line that closes the bar is tied — or, for a last bar no line closes,
    /// the voice's last event. A tie inside a phrase the voice plays is not seen (the form's
    /// reading has the same blind spot, <see cref="EndsWithTie"/>).</summary>
    private IEnumerable<int> TiedBars(SectionBarCounts.SemanticVoice voice)
    {
        if (voice.IsChords)
            yield break;
        foreach (var line in VoiceBarLines.Of(voice, _phraseBodies))
            if (line.Bars > 0 && line.Node.Parent is { } parent && EndsTied(parent.ChildNodes(), before: line.Node))
                yield return line.Bars - 1;
        if (voice.TrailingOpen && EndsTied(voice.Container.ChildNodes(), before: null))
            yield return voice.Bars - 1;
    }

    /// <summary>Whether the last event of <paramref name="items"/> before <paramref name="before"/>
    /// (null: of all of them) is tied — looking into an ending that stands last.</summary>
    private static bool EndsTied(IEnumerable<SyntaxNode> items, SyntaxNode? before)
    {
        var list = new List<SyntaxNode>();
        foreach (var item in items)
        {
            if (ReferenceEquals(item, before))
                break;
            list.Add(item);
        }
        for (int i = list.Count - 1; i >= 0; i--)
        {
            switch (list[i])
            {
                case TieSyntax:
                    return true;
                case BarlineSyntax:
                    return false;
                case InlineVoltaSyntax ending:
                    return EndsTied(ending.Items, before: null);
            }
            if (list[i] is not SyntaxTokenNode && SectionSigns.TakesTime(list[i]))
                return false;
        }
        return false;
    }

    /// <summary>
    /// Plants a <see cref="RepeatTieMarker"/> before the first note of every bar of this play
    /// that <see cref="_repeatTieBars"/> names — after the bar line with that many bars of the
    /// section before it, or at the play's head. Called before the score's signs are written
    /// into the play (<see cref="WriteTheScoresRepeatSigns"/>), so an ending spread from
    /// another voice takes the marker in with the bar; one before an ending this voice writes
    /// is carried in by <see cref="EmitInlineRepeat"/>.
    /// </summary>
    /// <param name="play">The printed play (form-walk order), or -1 for a book with no form:
    /// the section's one play.</param>
    private void PlantTheRepeatTies(int play, string name, SyntaxNode container, List<SyntaxNode> result, int from)
    {
        // The plain line a voice writes where another voice's sign cuts a bar (`g2. |` under the
        // melody's `g a b :|`, SignCutBars) closes half a bar: a bar check there fails in
        // LilyPond ("bar check failed at: 3/4", MEASURED 2026-11-15, Lab
        // sessions/p1247/probe/m-a-pickup), so it is written as nothing (EmitBarline), as the
        // line of a bar a section boundary splits is.
        if (_signCuts != null && !_chordTrack)
            for (int i = from; i < result.Count; i++)
                if (result[i] is BarlineSyntax { BarToken.Kind: SyntaxKind.Bar, Parent: not null } line && _signCuts.ClosesAFirstHalf(line))
                    _shared.OpenWrittenBars.Add(line);
        if (_repeatTieBars == null || _repeatTieStream is not { } stream || _chordTrack)
            return;
        if (play < 0)
            for (int p = 0; p < stream.Plays.Count && play < 0; p++)
                if (stream.Plays[p].Section == name)
                    play = p;
        if (play < 0 || play >= stream.Plays.Count || stream.Plays[play].Section != name
            || !_repeatTieBars.TryGetValue(play, out var targets))
            return;
        var bars = PlaceTheBarLines(result.GetRange(from, result.Count - from), VoiceOf(container));
        foreach (int k in targets.OrderByDescending(k => k))
        {
            int line = bars.LastIndexOf(k);
            if (line < 0 && k > 0)
                continue;
            result.Insert(from + line + 1, new RepeatTieMarker());
        }
    }

    // Whether the voice being written takes the other voices' signs (CarriesTheScoresSigns).
    private bool _carriesTheSigns = true;

    /// <summary>
    /// Whether the voice whose sections are <paramref name="byName"/> has a bar line of its
    /// own under EVERY sign and ending the other voices write — asked before its first play is
    /// written, because the signs are written all or none: a <c>:|</c> without its <c>|:</c>
    /// is another repeat (it rewinds to the beginning), which is worse than the straight play
    /// the voice had. MEASURED 2026-11-14 (Lab sessions/p1244/probe/t-phrase): a part whose
    /// first two bars are a phrase took the <c>:|</c> alone and repeated from its bar 1.
    /// </summary>
    /// <remarks>A place stands when the voice writes a line of its own there as an item of its
    /// stream (not inside a voice span or an ending of its own), or pads the bar
    /// (<see cref="PaddingBars"/>); the section's head always stands. A section the voice does
    /// not write is all padding (<see cref="AppendSilentPlay"/>).</remarks>
    private bool CarriesTheScoresSigns(
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName, string who)
    {
        foreach (var (name, entry) in byName)
        {
            var signs = SignsOf(name);
            if (signs.HasNoRepeats || VoiceOf(entry.Container) is not { } voice)
                continue;
            var lines = VoiceBarLines.Of(voice, _phraseBodies);
            // A row's `]` that closes its bar stands for a plain line (ChordBarsOf writes one).
            var closedByEnd = VoiceBarLines.RowEndClosedBars(voice);
            int own = voice.Bars - (voice.TrailingOpen ? 1 : 0);
            bool pads = _sectionBars.Missing(entry.Container, out _) > 0;
            int canonical = _sectionBars.Canonical.GetValueOrDefault(name);
            bool Stands(int place) => place == 0
                || (pads && place > own && place <= canonical)
                || closedByEnd.Contains(place)
                || lines.Any(l => l.Bars == place && (voice.IsChords || ReferenceEquals(l.Node.Parent, entry.Container)));
            int? missing = null;
            foreach (var (place, sign) in signs.Lines)
                if (!ReferenceEquals(sign.Container, voice.Container) && !Stands(place))
                    missing ??= place;
            foreach (var ending in signs.Endings)
                if (!(ending.Nested && ReferenceEquals(ending.Container, voice.Container)))
                    missing ??= !Stands(ending.From) ? ending.From : !Stands(ending.To) ? ending.To : null;
            if (missing is not { } bar)
                continue;
            _warnings.Add(
                $"{who} does not carry the repeat signs the other voices write: it has no bar line of its own "
                + $"after bar {bar} of section '{name}' (the bar is inside a phrase, a voice span or a multi-bar "
                + "rest). LilyPond draws the signs on every staff, but \\unfoldRepeats and the MIDI play "
                + "this voice straight through");
            return false;
        }
        return true;
    }

    /// <summary>
    /// The bars of the section standing before what follows each top-level bar line of one
    /// play: a line the voice wrote by <see cref="VoiceBarLines"/>, a chord track's by the bars
    /// before it in the stream (a <see cref="ChordBarMarker"/> each), and the padding lines
    /// after the voice's own (<see cref="PaddingBars"/>, <see cref="AppendSilentPlay"/>) one bar
    /// each — the first only closing a bar the voice left open.
    /// </summary>
    private List<int> PlaceTheBarLines(List<SyntaxNode> items, SectionBarCounts.SemanticVoice? voice)
    {
        var bars = new List<int>(items.Count);
        if (_chordTrack)
        {
            int closed = 0;
            foreach (var item in items)
            {
                if (item is { Green: ChordBarGreen })
                    closed++;
                bars.Add(item is BarlineSyntax ? closed : -1);
            }
            return bars;
        }

        var written = new Dictionary<int, int>();
        if (voice != null)
            foreach (var line in VoiceBarLines.Of(voice, _phraseBodies))
                written.TryAdd(line.End, line.Bars);
        int padded = voice == null ? 0 : voice.Bars - (voice.TrailingOpen ? 1 : 0);
        foreach (var item in items)
        {
            if (item is not BarlineSyntax line)
                bars.Add(-1);
            else if (line.Parent != null)
                bars.Add(written.GetValueOrDefault(line.Span.Start + line.Span.Length, -1));
            else if (line.BarToken.Kind == SyntaxKind.Bar)
                bars.Add(++padded);
            else
                bars.Add(-1);
        }
        return bars;
    }

    /// <summary><paramref name="sign"/> where the stream held <paramref name="plain"/>, with
    /// what the export had noted about that bar line (an empty bar's measured length, the
    /// section a padding bar belongs to).</summary>
    private BarlineSyntax InPlaceOf(BarlineSyntax plain, BarlineSyntax sign)
    {
        if (_shared.UnmeteredWrittenBars.TryGetValue(plain, out var written))
            _shared.UnmeteredWrittenBars[sign] = written;
        if (_shared.UnmeteredPaddingBars.TryGetValue(plain.Green, out var padding))
            _shared.UnmeteredPaddingBars[sign.Green] = padding;
        if (_shared.PaddingBarSections.TryGetValue(plain.Green, out var section))
            _shared.PaddingBarSections[sign.Green] = section;
        return sign;
    }

    private void WarnTheSignIsNotWritten(string section, int place, string sign)
    {
        string where = _chordTrack ? "a chord track" : $"part '{_currentPartName}'";
        if (_signWarned.Add($"{section}\n{place}\n{sign}\n{where}"))
            _warnings.Add(
                $"the '{sign}' another voice writes after bar {place} of section '{section}' is not written in "
                + $"{where}: it has no bar line of its own there (the bar is inside a phrase or a multi-bar "
                + "rest) — LilyPond draws the sign on every staff, but \\unfoldRepeats and the MIDI play "
                + "that voice straight through");
    }
}

/// <summary>
/// An ending another voice of the section writes, around THIS voice's own bars of it
/// (<c>LilyPondExporter.WriteTheScoresRepeatSigns</c>): the numbers are the written ending's,
/// the items this voice's stream items as they are — their nodes, so everything the export
/// keyed on them still finds them. Matched by its green, like <see cref="SectionPlayMarker"/>.
/// </summary>
internal sealed class SpreadEndingMarker : SyntaxNode
{
    public SpreadEndingMarker(InlineVoltaSyntax written, List<SyntaxNode> items)
        : base(new SpreadEndingGreen(written, items), parent: null, position: 0)
    {
    }
}

/// <summary>The spread ending's green (see <see cref="SpreadEndingMarker"/>).</summary>
internal sealed class SpreadEndingGreen : Syntax.InternalSyntax.GreenNode
{
    /// <summary>The ending as its voice wrote it — its numbers.</summary>
    public InlineVoltaSyntax Written { get; }
    /// <summary>The bars of the voice the ending is spread to.</summary>
    public List<SyntaxNode> Items { get; }

    public SpreadEndingGreen(InlineVoltaSyntax written, List<SyntaxNode> items)
        : base(SyntaxKind.None, fullWidth: 0)
    {
        Written = written;
        Items = items;
    }
}
