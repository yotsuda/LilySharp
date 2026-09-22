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

using System.Collections.Generic;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Post-collect scan for slur marks that pair with nothing. Whatever this reports,
/// <see cref="SlurDetector"/> silently drops: it draws a slur only when a <c>)</c> finds a
/// <c>(</c> on its stack, so a surplus <c>)</c> and a <c>(</c> still open at the end are
/// both discarded without a word, and one bar of music quietly loses its phrasing.
/// </summary>
/// <remarks>
/// THE PAIRING RULES ARE THE RENDERER'S, not a second opinion about them — a warning that
/// disagreed with what gets drawn would be worse than no warning:
/// <list type="bullet">
/// <item>marks pair as a STACK, innermost first, exactly as <c>SlurDetector</c> pops;</item>
/// <item>the scan is PER VOICE, because <c>SlurDetector</c> clears its stack at every
/// voice change (LILYPOND-REF: ly/engraver-init.ly — Slur_engraver lives in the Voice
/// context), so a <c>(</c> left open when a voice ends never pairs with anything;</item>
/// <item>a note carrying BOTH marks CLOSES before it OPENS, in whatever order they were
/// written — the middle of <c>c( d)( e)</c> ends one slur and starts the next — which is the
/// order <c>SlurDetector</c> reads them in, and LilyPond's:
/// LILYPOND-REF: lily/slur-engraver.cc:295-324 process_music — stop_events_ before start_events_. So
/// <c>c4()</c> with nothing open is an unmatched close AND a new open, as LilyPond warns
/// "cannot end slur"; until session 474 both read open-first and took it for a one-note slur.</item>
/// </list>
/// <para>
/// A <c>(</c> written where no note precedes it — <c>(e c4 d)</c> — never becomes a mark at
/// all: slur marks annotate the note BEFORE them
/// (<c>MeasureCollector.MusicWalk.PeekMarkers</c>). Nothing here can see that <c>(</c>; it
/// is reported through the <c>)</c> that is then left with nothing to pair with, which is
/// the one bar of music that goes missing in that spelling.
/// </para>
/// <para>
/// LilyPond warns on the same two shapes (its Slur_engraver reports an unterminated slur
/// and an unmatched close), so this is a fidelity fix as much as an editor one.
/// </para>
/// </remarks>
internal static class SlurPairingScanner
{
    /// <summary>Scans one voice for slur marks that pair with nothing (<paramref name="sink"/>)
    /// and, on the pairs that DO form, for the one that spans a cue boundary
    /// (<paramref name="cueSink"/>).</summary>
    /// <remarks>
    /// The cue test rides on THIS stack rather than getting a scanner of its own, because a
    /// second walk would be a second opinion about which <c>(</c> a <c>)</c> belongs to — the
    /// thing this class exists not to have. When the pop happens, both ends of the slur are in
    /// hand and the test is one comparison. See <see cref="CueSpanBoundaryWarning"/> for what
    /// LilyPond does with such a slur (nothing: it drops it with a warning).
    /// <para>
    /// The comparison is of REGION NUMBERS, not of a boolean: <c>cue { … } cue { … }</c> is two
    /// CueVoice contexts and LilyPond refuses a slur running from one into the next just as it
    /// refuses one leaving a cue (MEASURED — <see cref="MusicItem.BeginsCueRegion"/>). The number
    /// is counted HERE, off the edge stamp the collector left, because this scan already visits
    /// every note and chord of the voice in order — the identity costs one increment, and no
    /// second walk exists to disagree with this one.
    /// </para>
    /// </remarks>
    public static void Scan(Voice voice, List<UnpairedSlurWarning> sink,
        List<CueSpanBoundaryWarning> cueSink)
    {
        // The '(' marks still looking for a ')' — position, and WHICH cue region that note sits
        // in (0 = not in one).
        var open = new Stack<(int Position, int Region)>();
        int regionsSeen = 0;
        int region = 0;
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                if (!TryGetSlurFlags(items[ii], out bool hasStart, out bool hasEnd))
                    continue;
                region = RegionOf(items[ii], region, ref regionsSeen);
                if (hasEnd)
                {
                    if (open.Count > 0)
                    {
                        var start = open.Pop();
                        if (start.Region != region)
                            cueSink.Add(new CueSpanBoundaryWarning(
                                start.Position, CueSpanKind.Slur,
                                CrossingOf(start.Region, region)));
                    }
                    else
                        sink.Add(new UnpairedSlurWarning(items[ii].SourcePosition, IsOpen: false));
                }
                if (hasStart)
                    open.Push((items[ii].SourcePosition, region));
            }
        }

        // Whatever is still open when the voice ends is dropped by the renderer. Reported
        // in the order the marks were WRITTEN (the stack pops innermost-first), so a
        // diagnostic list reads down the score rather than back up it.
        var dangling = new List<(int Position, int Region)>(open);
        dangling.Reverse();
        foreach (var (position, _) in dangling)
            sink.Add(new UnpairedSlurWarning(position, IsOpen: true));
    }

    /// <summary>
    /// Scans one voice for phrasing-slur marks that draw nothing — the three faults of
    /// <see cref="SpanPairingFault"/>, which are LilyPond's three for a phrasing slur.
    /// </summary>
    /// <remarks>
    /// THE RULES ARE <c>SlurDetector</c>'s phrasing pairing, read the same way: per voice,
    /// close before open on one item, ONE open at a time — a second <c>@phrasingSlur</c> while
    /// one is open is ignored (StartWhileOpen), not stacked.
    /// LILYPOND-REF: lily/slur-engraver.cc:174 Slur_engraver::finalize "unterminated", :228
    /// "already have", :312 "cannot end" — the phrasing engraver is a Slur_engraver
    /// (lily/phrasing-slur-engraver.cc).
    /// </remarks>
    public static void ScanPhrasing(Voice voice, List<UnpairedSpanWarning> sink)
    {
        int open = MusicItem.NoSourcePosition;
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (item.HasPhrasingSlurEnd)
                {
                    if (open >= 0)
                        open = MusicItem.NoSourcePosition;
                    else
                        sink.Add(new UnpairedSpanWarning(item.PhrasingSlurEndSourcePosition,
                            SpanKind.PhrasingSlur, SpanPairingFault.StopWithNoStart));
                }
                if (item.HasPhrasingSlurStart)
                {
                    if (open < 0)
                        open = item.PhrasingSlurStartSourcePosition;
                    else
                        sink.Add(new UnpairedSpanWarning(item.PhrasingSlurStartSourcePosition,
                            SpanKind.PhrasingSlur, SpanPairingFault.StartWhileOpen));
                }
            }
        }
        if (open >= 0)
            sink.Add(new UnpairedSpanWarning(open, SpanKind.PhrasingSlur, SpanPairingFault.Unterminated));
    }

    /// <summary>Which cue region an item sits in — 0 outside any, else a number counted from
    /// the collector's edge stamps, fresh at every region the walk enters.</summary>
    /// <remarks>
    /// ⚠️ The last arm is for a cue item whose region's first note or chord this scan never saw
    /// (a region opening on an item kind that carries no cue flag, e.g. a drum note): it is
    /// still a region OTHER than the one just left, so counting it as new keeps the comparison
    /// on the safe side — the alternative would silently merge two regions into one.
    /// </remarks>
    internal static int RegionOf(MusicItem item, int current, ref int regionsSeen) =>
        !IsCueItem(item) ? 0
        : item.BeginsCueRegion ? ++regionsSeen
        : current != 0 ? current
        : ++regionsSeen;

    /// <summary>Which of the three crossings a pair of region numbers is; the two ends are
    /// known to differ.</summary>
    internal static CueSpanCrossing CrossingOf(int startRegion, int endRegion) =>
        startRegion == 0 ? CueSpanCrossing.IntoCue
        : endRegion == 0 ? CueSpanCrossing.OutOfCue
        : CueSpanCrossing.BetweenCues;

    /// <summary>Whether an item was written inside a <c>cue { … }</c>. Only the two kinds a
    /// slur pairs on can carry the flag; anything else is outside by construction.</summary>
    internal static bool IsCueItem(MusicItem item) => item switch
    {
        NoteItem n => n.IsCue,
        ChordItem c => c.IsCue,
        _ => false,
    };

    /// <summary>Slurs attach to a note OR a chord — the same two item kinds
    /// <see cref="SlurDetector.DetectSlurs"/> pairs.</summary>
    private static bool TryGetSlurFlags(MusicItem item, out bool hasStart, out bool hasEnd)
    {
        switch (item)
        {
            case NoteItem n: hasStart = n.HasSlurStart; hasEnd = n.HasSlurEnd; return true;
            case ChordItem c: hasStart = c.HasSlurStart; hasEnd = c.HasSlurEnd; return true;
            default: hasStart = false; hasEnd = false; return false;
        }
    }
}
