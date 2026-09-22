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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// Detects slurs between notes in a score.
/// </summary>
internal sealed class SlurDetector
{
    public ImmutableArray<SlurItem> DetectSlurs(Score score)
    {
        var slurs = new List<SlurItem>();

        // Each voice runs its own slur engraver: a voice's open-slur stack must not
        // pair with another voice's close, so the stack resets at each voice change.
        // LILYPOND-REF: ly/engraver-init.ly — Slur_engraver lives in the Voice context.
        var openSlurs = new Stack<(int measureIdx, int itemIdx, MusicItem item)>();
        int currentVoice = -1;

        // The phrasing slurs, paired on the same walk and appended AFTER every slur: the
        // layout scores a phrasing slur against the slurs inside it, so those must be laid out
        // first (LilyPond reads the small slur's finished curve — lily/slur-scoring.cc:817).
        // ONE may be open per voice, not a stack: a second '@phrasingSlur' while one is open
        // is ignored, as LilyPond ignores it ("already have phrasing slur",
        // lily/slur-engraver.cc:228) — SlurPairingScanner.ScanPhrasing reports it.
        List<SlurItem>? phrasingSlurs = null;
        (int measureIdx, int itemIdx, MusicItem item)? openPhrasing = null;

        foreach (var (v, measures, measureIdx, itemIdx, item) in VoiceScan.WalkVoiceItems(score))
        {
            if (v != currentVoice)
            {
                openSlurs.Clear();
                openPhrasing = null;
                currentVoice = v;
            }

            // Close before open, as for a slur below (lily/slur-engraver.cc:295-324 is the
            // phrasing engraver's process_music too).
            if (item.HasPhrasingSlurEnd && openPhrasing is { } op)
            {
                openPhrasing = null;
                bool up = VoiceScan.SpanCurvesUp(score.Voices.Length, v,
                    AnyCoveredStemDown(measures, op.measureIdx, op.itemIdx, measureIdx, itemIdx));
                (phrasingSlurs ??= new List<SlurItem>()).Add(new SlurItem(
                    MusicItem.EdgeStaffPosition(op.item, up) ?? 0,
                    MusicItem.EdgeStaffPosition(item, up) ?? 0,
                    up, op.measureIdx, measureIdx, op.itemIdx, itemIdx, voiceIndex: v)
                {
                    StartSourcePosition = op.item.PhrasingSlurStartSourcePosition,
                    EndSourcePosition = item.PhrasingSlurEndSourcePosition,
                    IsPhrasing = true,
                });
            }
            if (item.HasPhrasingSlurStart && openPhrasing is null)
                openPhrasing = (measureIdx, itemIdx, item);

            // Slurs attach to a note OR a chord (`<c e>( <d f>)`).
            if (!TryGetSlurFlags(item, out bool hasStart, out bool hasEnd))
                continue;

            // A note carrying both marks CLOSES before it OPENS, whatever order they were written
            // in: the middle of `c( d)( e)` ends the first slur and starts the second. Until
            // session 474 the open came first, so `)` popped the slur `(` had just pushed —
            // `c'4( d c)( d)` drew one bow from the first note to the last and a zero-length
            // one on the third, where LilyPond draws two (and where the tab's hammer-on pairing,
            // TabResolver, and the part combiner's span state already read close-then-open).
            // LILYPOND-REF: lily/slur-engraver.cc:295-324 process_music — stop_events_ before start_events_.
            if (hasEnd && openSlurs.Count > 0)
            {
                var (startMeasureIdx, startItemIdx, startItem) = openSlurs.Pop();

                // Single voice: default DOWN, flipped UP when ANY covered stem
                // points DOWN — not just the start note's (a slur from a stem-up
                // note to a stem-down note goes UP, or it curves straight into the
                // later note's stem side). Polyphony: the voice fixes the direction.
                // LILYPOND-REF: lily/slur.cc Slur::calc_direction — d = DOWN, set UP
                //   if any non-rest note column has direction DOWN.
                bool curveUp = VoiceScan.SpanCurvesUp(score.Voices.Length, v,
                    AnyCoveredStemDown(measures, startMeasureIdx, startItemIdx, measureIdx, itemIdx));

                slurs.Add(new SlurItem(
                    // For a chord the slur anchors at the head on the curve side.
                    MusicItem.EdgeStaffPosition(startItem, curveUp) ?? 0,
                    MusicItem.EdgeStaffPosition(item, curveUp) ?? 0,
                    curveUp,
                    startMeasureIdx,
                    measureIdx,
                    startItemIdx,
                    itemIdx,
                    voiceIndex: v)
                {
                    // The two characters the reader can point at. They come off the BOUND
                    // ITEMS, not off this walk: the marker run that wrote them was folded
                    // onto the note back in the collect (MeasureCollector.MarkerFlags),
                    // which is the only place that still knows where `(` and `)` stood.
                    StartSourcePosition = startItem.SlurStartSourcePosition,
                    EndSourcePosition = item.SlurEndSourcePosition,
                });
            }

            if (hasStart)
            {
                openSlurs.Push((measureIdx, itemIdx, item));
            }
        }

        if (phrasingSlurs != null)
            slurs.AddRange(phrasingSlurs);
        return slurs.ToImmutableArray();
    }

    private static bool TryGetSlurFlags(MusicItem item, out bool hasStart, out bool hasEnd)
    {
        switch (item)
        {
            case NoteItem n: hasStart = n.HasSlurStart; hasEnd = n.HasSlurEnd; return true;
            case ChordItem c: hasStart = c.HasSlurStart; hasEnd = c.HasSlurEnd; return true;
            // A rest is a legal slur bound (r16( … r) — LilyPond's rests live in
            // NoteColumn grobs, so the Slur_engraver binds to them like any column.
            // Its edge staff position resolves to null downstream; the layout takes
            // the rest-bound base attachment instead ("slur-rest-direction.ly").
            case RestItem r when !r.IsSpacer: hasStart = r.HasSlurStart; hasEnd = r.HasSlurEnd; return true;
            default: hasStart = false; hasEnd = false; return false;
        }
    }

    // True when any note/chord covered by the slur (inclusive range) has a
    // DOWN stem — rests are transparent, like LP's !has_rests columns.
    // LILYPOND-REF: lily/slur.cc Slur::calc_direction.
    private static bool AnyCoveredStemDown(
        ImmutableArray<Measure> measures,
        int startMeasureIdx, int startItemIdx, int endMeasureIdx, int endItemIdx)
    {
        for (int mi = startMeasureIdx; mi <= endMeasureIdx && mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            int from = mi == startMeasureIdx ? startItemIdx : 0;
            int to = mi == endMeasureIdx ? Math.Min(endItemIdx, items.Length - 1) : items.Length - 1;
            for (int ii = from; ii <= to; ii++)
            {
                switch (items[ii])
                {
                    case NoteItem n when !n.StemUp:
                        return true;
                    case ChordItem c when !c.StemUp:
                        return true;
                }
            }
        }
        return false;
    }
}