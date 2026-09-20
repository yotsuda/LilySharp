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
/// Post-collect scan for ties that cannot bind. A tie joins two notes of the SAME
/// pitch and binds only to the IMMEDIATELY following timed item — the rule
/// <see cref="TieDetector"/> renders by (it never scans past an intervening item;
/// LILYPOND-REF: lily/tie-engraver.cc stop_translation_timestep). A pitch mismatch
/// or an audible rest there is almost always an authoring slip (a slur was meant,
/// or the target note was mistyped), so it is surfaced as a warning.
/// </summary>
/// <remarks>
/// Runs per voice on the collected measures BEFORE display-only transforms (the
/// ottava transposer moves staff positions), comparing both staff position and
/// sounding pitch so a same-position accidental change (<c>c~ cis</c>) is caught
/// too. A tie into an invisible spacer (<c>s</c> — an absent parallel voice's padding)
/// is left alone; a tie with NOTHING after it is reported, because the renderer draws no
/// tie for it either and the mark is simply lost.
/// </remarks>
internal static class TieTargetScanner
{
    /// <summary>Scans one voice for ties whose target cannot receive them
    /// (<paramref name="sink"/>) and, on the ties that DO bind, for the one whose two notes sit
    /// on opposite sides of a cue boundary (<paramref name="cueSink"/>).</summary>
    /// <remarks>
    /// The cue test rides on the binding this scan already computes, for the reason
    /// <see cref="SlurPairingScanner"/> gives: a second walk would be a second opinion about
    /// which note a tie reaches. It is asked only of a tie that FOUND its note — a tie with no
    /// target, or one whose target is a rest or another pitch, is already reported as the
    /// larger problem (LYS4007) and adding a second complaint about the same mark would only
    /// bury it. See <see cref="CueSpanBoundaryWarning"/> for what LilyPond does with a crossing
    /// tie: it drops it, and in the outward direction WITHOUT A WARNING.
    /// </remarks>
    public static void Scan(Voice voice, List<TieTargetWarning> sink,
        List<CueSpanBoundaryWarning> cueSink)
    {
        var measures = voice.Measures;
        for (int mi = 0; mi < measures.Length; mi++)
        {
            var items = measures[mi].Items;
            for (int ii = 0; ii < items.Length; ii++)
            {
                var item = items[ii];
                if (item is not (NoteItem { HasTieStart: true } or ChordItem { HasTieStart: true }))
                    continue;
                var next = NoteScan.FindNext(measures, mi, ii,
                    x => x is NoteItem or ChordItem or RestItem);
                if (next is not { } n)
                {
                    // Nothing follows: the tie is the last thing in its voice. This used to
                    // `continue` — "nothing to compare" — which was true of the COMPARISON
                    // and false of the loss: TieDetector draws nothing, so the mark vanishes
                    // in silence. MEASURED: `c4 d4 e4 f4~ |` engraves byte-for-byte as the
                    // same bar with no tie at all, while `f4@laissezVibrer` draws the hanging
                    // tie the writer probably meant. The span points at the TIED NOTE, there
                    // being no following item to point at (the other two cases point at the
                    // item that fails to receive the tie).
                    sink.Add(new TieTargetWarning(item.SourcePosition, TieTargetProblem.NoTarget));
                    continue;
                }
                if (n.Item is RestItem rest)
                {
                    if (!rest.IsSpacer)
                        sink.Add(new TieTargetWarning(rest.SourcePosition, TieTargetProblem.IntoRest));
                }
                else if (!AnyPitchMatches(item, n.Item))
                {
                    sink.Add(new TieTargetWarning(n.Item.SourcePosition, TieTargetProblem.PitchMismatch));
                }
                else
                {
                    // The tie binds. The only question left is whether it binds ACROSS a cue
                    // boundary, which LilyPond cannot do. THE TARGET IS THE VERY NEXT NOTE, so
                    // no region counting is needed here: two notes in a row are in different cue
                    // regions exactly when the second one BEGINS one (a region's own second note
                    // never carries that stamp). See NoteItem.BeginsCueRegion.
                    bool startsInCue = SlurPairingScanner.IsCueItem(item);
                    bool endsInCue = SlurPairingScanner.IsCueItem(n.Item);
                    bool betweenCues = startsInCue && endsInCue
                        && n.Item.BeginsCueRegion;
                    if (startsInCue != endsInCue || betweenCues)
                        cueSink.Add(new CueSpanBoundaryWarning(
                            item.SourcePosition, CueSpanKind.Tie,
                            betweenCues ? CueSpanCrossing.BetweenCues
                            : startsInCue ? CueSpanCrossing.OutOfCue
                            : CueSpanCrossing.IntoCue));
                }
            }
        }
    }

    /// <summary>True when at least one pitch of <paramref name="start"/> recurs in
    /// <paramref name="end"/>. A chord tie with SOME matching pitches is fine (the
    /// unmatched ones are dropped silently, the LilyPond chord-tie behavior); only
    /// a total mismatch — nothing gets tied at all — is reported.</summary>
    private static bool AnyPitchMatches(MusicItem start, MusicItem end)
    {
        for (int i = 0, n = PitchCount(start); i < n; i++)
        {
            var (pos, midi) = PitchAt(start, i);
            for (int j = 0, m = PitchCount(end); j < m; j++)
            {
                var (endPos, endMidi) = PitchAt(end, j);
                if (pos == endPos && midi == endMidi)
                    return true;
            }
        }
        return false;
    }

    /// <summary>How many pitches an item sounds — a note one, a chord its notes.</summary>
    /// <remarks>
    /// ⚠️ THIS PAIR EXISTS IN ORDER NOT TO ALLOCATE. It was one <c>yield return</c> method,
    /// and a C# iterator builds its state machine on the CALL — 64 bytes per ask, and this
    /// walk is NESTED, so the inner one paid again for every pitch of the outer. MEASURED
    /// (2026-09-20, session 446; the reader's corpus, 231 books x 8 forward keystrokes,
    /// Release): 21.18 + 21.18 calls a keystroke = 2,712 B/keystroke (RULES §5.3).
    /// <para>
    /// ⚠️ THE TWO HALVES OF THE CHORD ARM ARE OBSERVED DIFFERENTLY, and session 446 measured
    /// which: a chord sounding NO pitch at all reddens FOUR tests (the warning about a tie
    /// that binds nothing), while reading note 0 for every pitch of the chord — the member
    /// mapping — leaves all 8,774 green. So "a chord ties by its members" is asserted only as
    /// far as "a chord has members". Same shape as sessions 443-445 (RULES §5.4).
    /// </para>
    /// </remarks>
    private static int PitchCount(MusicItem item) => item switch
    {
        NoteItem => 1,
        ChordItem c => c.Notes.Length,
        _ => 0
    };

    /// <summary>The (staff position, midi) of the i-th pitch an item sounds.</summary>
    private static (int Pos, int Midi) PitchAt(MusicItem item, int i) => item switch
    {
        NoteItem n => (n.StaffPosition, n.Midi),
        ChordItem c => (c.Notes[i].StaffPosition, c.Notes[i].Midi),
        _ => default
    };
}
