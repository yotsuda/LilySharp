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
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// Grace synchronisation: every voice keeps the grace time any other voice spends at the same
// moment, written as a grace skip in front of its own event.
public sealed partial class LilyPondExporter
{
    /// <summary>
    /// Reads off the page which written events owe a grace skip, and how long: wherever a voice
    /// leads an event with a grace run, every other voice's event at that moment is given a grace
    /// skip of the run's length, and a voice whose own run is shorter is padded to the longest.
    /// The music walk writes them (<see cref="GraceSyncPad"/>).
    /// </summary>
    /// <remarks>
    /// LilyPond keeps grace time per voice, and whatever a context engraves at a moment —
    /// a bar line, a meter, a key, a mark — it engraves at that VOICE's first moment there: the
    /// grace's on a staff that leads with one, the main moment on a staff that does not. So
    /// a grace on one staff alone draws the other staff's meter and bar line a second time.
    /// LILYPOND-REF: Documentation/en/notation/rhythms.itely:4632-4659 Grace notes, Known issues — "inserting grace skips of the corresponding durations in the other staves", with `\grace` (not `\acciaccatura`) for the skip.
    /// MEASURED (2.26.0, Lab sessions/p841/gl/ext): a grace on the lower staff at the score's
    /// start drew the meter twice and a stray bar line; at a `\time` change the meter twice; at
    /// `\bar "||"` two double bars; at a `\key` change the upper key at the grace's place. The
    /// owner's decision (session 842): skip at EVERY grace moment, not only where a known symbol
    /// breaks — the twin should lose nothing the .lys writes.
    /// <para>
    /// ⚠️ KEYED BY OCCURRENCE. One written event is played many times (a section played twice,
    /// a phrase referenced again, a volta body), and the page's model lists each play, in the
    /// order the twin writes them (both write a volta once and a reprise twice — see
    /// <see cref="EmitInlineChordTracks"/>). So an event's position holds one entry per play,
    /// and the walk counts the plays it writes. An event the model plays more often than the
    /// twin writes it (an unfolded repeat) gets the first play's entry.
    /// </para>
    /// </remarks>
    private void CollectGraceSync(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null)
            return;
        if (!tree.GetRoot().DescendantNodes<GraceExpressionSyntax>().Any())
            return;
        if (PageModel(tree, render) is not { } score || score.GraceNotes.IsDefaultOrEmpty)
            return;

        var staves = new Dictionary<int, Staff>();
        foreach (var (_, staff, idx) in score.EnumerateStaves())
            staves[idx] = staff;

        // The longest grace run at each moment (bar, onset), and each run by its main event.
        var longest = new Dictionary<(int Bar, Fraction Onset), Fraction>();
        var runs = new Dictionary<(int Staff, int Voice, int Bar, int Item), GraceNoteItem>();
        foreach (var g in score.GraceNotes)
        {
            if (!staves.TryGetValue(g.StaffIndex, out var staff) || g.VoiceIndex >= staff.Voices.Length)
                continue;
            var measures = staff.Voices[g.VoiceIndex].Measures;
            if (g.MeasureIndex >= measures.Length)
                continue;
            var key = (g.MeasureIndex, OnsetOf(measures[g.MeasureIndex], g.MainNoteItemIndex));
            var length = GraceRunLength(g);
            longest[key] = longest.TryGetValue(key, out var l) && l > length ? l : length;
            runs[(g.StaffIndex, g.VoiceIndex, g.MeasureIndex, g.MainNoteItemIndex)] = g;
        }
        if (longest.Count == 0)
            return;

        // Every event of every voice, in the page's order: what it owes at its moment. A staff
        // that shows a part another staff already showed (a tab under its notation) is the
        // same written music, so its plays are not counted twice.
        var pads = _shared.GraceSyncPads;
        var emptyBars = _shared.GraceSyncEmptyBars;
        var sectionAt = new Dictionary<int, string>();
        foreach (var section in tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>())
            sectionAt.TryAdd(section.SourceStart, section.SectionName);
        var seenParts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, staff, idx) in score.EnumerateStaves())
        {
            if (staff.IsTextRow || !seenParts.Add(staff.PrimaryVoice.Name))
                continue;
            for (int v = 0; v < staff.Voices.Length; v++)
            {
                var measures = staff.Voices[v].Measures;
                for (int m = 0; m < measures.Length; m++)
                {
                    // A bar of silence the twin writes as one spacer of its own (`s1 |`) has no
                    // written event to key on — see EmptyBarKey.
                    if (v == 0 && EmptyBarKey(measures[m], sectionAt) is { } barKey)
                    {
                        var k = (staff.PrimaryVoice.Name, barKey);
                        if (!emptyBars.TryGetValue(k, out var bars))
                            emptyBars[k] = bars = new List<Fraction>();
                        bars.Add(longest.TryGetValue((m, Fraction.Zero), out var lb) ? lb : Fraction.Zero);
                    }
                    var at = Fraction.Zero;
                    var items = measures[m].Items;
                    for (int i = 0; i < items.Length; i++)
                    {
                        var item = items[i];
                        if (item.Duration <= Fraction.Zero)
                            continue;
                        // An event led by its own run is written after it: the skip goes in
                        // front of the run, at the run's position.
                        runs.TryGetValue((idx, v, m, i), out var own);
                        int position = own?.SourcePosition ?? item.SourcePosition;
                        var owed = longest.TryGetValue((m, at), out var l)
                            ? l - (own != null ? GraceRunLength(own) : Fraction.Zero)
                            : Fraction.Zero;
                        if (position >= 0)
                        {
                            if (!pads.TryGetValue(position, out var plays))
                                pads[position] = plays = new List<Fraction>();
                            plays.Add(owed);
                        }
                        at += item.Duration;
                    }
                }
            }
        }
        // Positions that never owe anything are not asked about again.
        foreach (var position in pads.Where(p => p.Value.All(f => f <= Fraction.Zero)).Select(p => p.Key).ToList())
            pads.Remove(position);
        foreach (var key in emptyBars.Where(p => p.Value.All(f => f <= Fraction.Zero)).Select(p => p.Key).ToList())
            emptyBars.Remove(key);
    }

    /// <summary>
    /// The key a bar of pure silence is asked by, or null when the bar holds anything else:
    /// <c>§name</c> for a bar the page pads a section's play with (a part that does not write the
    /// section, or writes fewer bars of it — the spacer cites the section's declaration, and the
    /// twin writes the bar from <see cref="AppendSilentPlay"/> / <see cref="PaddingBars"/>), and
    /// <c>@N</c> for the author's own empty bar (<c>| |</c>), N the source end the page gives that
    /// bar — the bar line that closes it, which is the one the twin writes the spacer at.
    /// </summary>
    /// <remarks>
    /// Keyed by part and counted by play like the events (<see cref="GraceSyncPad"/>): a padded
    /// section has no written event, so the page's own order of its silent bars is the only
    /// thing the two walks share. MEASURED (2.26.0, Lab sessions/p845/gs): without the skip, a
    /// grace on the lower staff at a bar the upper staff is silent in drew the upper staff's
    /// section mark twice, and its `\time` twice when the section opened with one.
    /// </remarks>
    private static string? EmptyBarKey(Measure measure, Dictionary<int, string> sectionAt)
    {
        MusicItem? first = null;
        foreach (var item in measure.Items)
        {
            if (item.Duration <= Fraction.Zero)
                continue;
            if (item is not RestItem { IsSpacer: true })
                return null;
            first ??= item;
        }
        if (first == null)
            return null;
        if (measure.IsEmptyPlaceholder)
            return "@" + measure.SourceEnd;
        return sectionAt.TryGetValue(first.SourcePosition, out var name) ? "§" + name : null;
    }

    /// <summary>
    /// The grace skip the empty bar <paramref name="bar"/> closes owes (<see cref="EmptyBarKey"/>),
    /// as <c>\grace { sN }</c>, or "". Counts the occurrence either way.
    /// </summary>
    private string EmptyBarGracePad(BarlineSyntax bar)
    {
        var bars = _shared.GraceSyncEmptyBars;
        if (bars.Count == 0 || _currentPartName is not { } part)
            return "";
        var key = _shared.PaddingBarSections.TryGetValue(bar.Green, out var section)
            ? "§" + section
            : "@" + bar.SourceStart;
        if (!bars.TryGetValue((part, key), out var plays))
            return "";
        var seen = _shared.GraceSyncEmptyBarsSeen;
        seen.TryGetValue((part, key), out int k);
        seen[(part, key)] = k + 1;
        var owed = plays[Math.Min(k, plays.Count - 1)];
        return owed <= Fraction.Zero ? "" : "\\grace { s" + ChordModeDuration(owed) + " }";
    }

    /// <summary>The grace time a run spends: its columns' written lengths, dots included.</summary>
    private static Fraction GraceRunLength(GraceNoteItem run)
    {
        var length = Fraction.Zero;
        foreach (var column in run.Columns)
        {
            var value = column.BaseDuration;
            var add = value;
            for (int d = 0; d < column.Dots; d++)
            {
                add /= new Fraction(2, 1);
                value += add;
            }
            length += value;
        }
        return length;
    }

    /// <summary>
    /// The grace skip this occurrence of <paramref name="item"/> owes (<see cref="CollectGraceSync"/>),
    /// as <c>\grace { sN }</c>, or "" when it owes none. Counts the occurrence either way.
    /// </summary>
    /// <remarks>
    /// The skip's written length would carry to the next event in LilyPond (a grace body's last
    /// duration is the stream's next default), so the event after it writes its own duration —
    /// the same reason <see cref="EmitGrace"/> forces it.
    /// </remarks>
    private string GraceSyncPad(SyntaxNode item)
    {
        var pads = _shared.GraceSyncPads;
        if (pads.Count == 0)
            return "";
        if (item is not (NoteSyntax or DrumNoteSyntax or RestSyntax or ChordSyntax
            or ChordRepetitionSyntax or SlashNoteSyntax or BareDurationSyntax or GraceExpressionSyntax))
            return "";
        if (!pads.TryGetValue(item.SourceStart, out var plays))
            return "";
        var seen = _shared.GraceSyncSeen;
        seen.TryGetValue(item.SourceStart, out int k);
        seen[item.SourceStart] = k + 1;
        var owed = plays[Math.Min(k, plays.Count - 1)];
        if (owed <= Fraction.Zero)
            return "";
        _forceNextDuration = true;
        return "\\grace { s" + ChordModeDuration(owed) + " }";
    }
}
