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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Layout for a stanza number prefix (e.g., "1.") shown at the left edge of a verse.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — StanzaNumber grob
/// LILYPOND-REF: scm/define-grobs.scm StanzaNumber:
///   font-size = -1, font-series = bold
/// The StanzaNumber sits to the left of each lyric verse line, anchored at
/// the system's left edge.
/// </remarks>
public readonly record struct StanzaNumberLayout(
    int VerseNumber,
    int SystemIndex,
    // A measure in this system, used to resolve the system's Y when drawing.
    int MeasureIndex,
    // X coordinate at the system's left edge (start of staff lines).
    double X,
    // Y-up (frame B): staff-spaces above the system top, up-positive, matching the
    // verse's lyric baseline. The renderer reflects it to device against the
    // system top.
    double YUp,
    // Display text (e.g., "1.", "2.").
    string Text);

/// <summary>
/// Calculates StanzaNumber layouts for multi-verse lyrics.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/stanza-number-engraver.cc — Stanza_number_engraver.
/// Emits one entry per (system, verseNumber) combination so that a number
/// appears at the start of every verse line on every system.
/// </remarks>
internal static class StanzaNumberEngraver
{
    /// <summary>
    /// The number's ENGRAVING em, 2.4 staff spaces. ⚠️ Not LilyPond's: StanzaNumber declares
    /// <c>font-size -1</c> over the 2.2 text em (1.96). Kept as it was when the plan learned to
    /// reach it (2026-09-08); moving the default is a separate change with its own ledger.
    /// </summary>
    internal const double EngravingEm = 2.4;

    /// <summary>The number's padding to the syllables it stands left of.
    /// LILYPOND-REF: scm/define-grobs.scm:3412-3427 StanzaNumber — padding 1.0 (stanza-number-interface).</summary>
    internal const double Padding = 1.0;

    /// <summary>The number's em for THIS score: <see cref="EngravingEm"/> unless the score's
    /// <c>fonts { }</c> wrote a <c>step</c> or <c>size</c> for <c>stanza</c> (or <c>lyrics</c>).
    /// The draw is its only reader — the number reserves no space of its own.</summary>
    internal static double Em(Rendering.ScoreTextMetrics fonts)
        => fonts.Size(Rendering.TextRole.Stanza, EngravingEm);

    /// <summary>The number's weight and slant: bold (StanzaNumber's font-series) unless the
    /// score wrote a style.</summary>
    internal static Rendering.FontStyle Style(Rendering.ScoreTextMetrics fonts)
        => fonts.Style(Rendering.TextRole.Stanza, Rendering.FontStyle.Bold);

    /// <summary>
    /// Calculates stanza number layouts for verses present in the given lyric layouts.
    /// </summary>
    /// <param name="lyrics">All lyric layouts (post-engraver).</param>
    /// <param name="systems">System layouts; used to obtain each system's left edge X.</param>
    /// <param name="emitForFirstVerse">When false (LP default), single-verse scores
    /// don't get a "1." prefix. When multiple verses exist, all verses (including 1)
    /// are numbered.</param>
    public static ImmutableArray<StanzaNumberLayout> Calculate(
        Rendering.ScoreTextMetrics fonts,
        ImmutableArray<LyricLayout> lyrics,
        ImmutableArray<SystemLayout> systems,
        bool emitForFirstVerse = false,
        bool leadSheet = false)
    {
        if (lyrics.IsDefaultOrEmpty || systems.IsDefaultOrEmpty)
            return ImmutableArray<StanzaNumberLayout>.Empty;

        // Map measure → system index so we can match a lyric to its enclosing system.
        var measureToSystem = SpannerBreakSubstitution.BuildMeasureToSystemMap(systems);

        // Number verses PER SYSTEM: a stanza number is only useful where more than one
        // verse actually stacks. A song whose verses all stack everywhere numbers every
        // system (the usual case); a per-occurrence volta that puts a second verse on
        // only one system leaves the single-verse systems (a lone reprise line) clean.
        // A `~`-hidden verse still COUNTS toward a system's verse tally (so the visible
        // verse beside it keeps its number) but prints no number itself.
        // ⚠️ PER (SYSTEM, STAFF), NOT PER SYSTEM. A stanza number labels ONE line of one
        // staff's lyrics, and since a lyric hangs off its own staff an SATB score has four
        // lines on one system all numbered verse 1. Tallying per system alone made four
        // one-verse staves look like one staff with as many verses as the system carried,
        // and printed a single number for all of them at whichever line won the leftmost-X
        // race. Unreachable while at most one staff per system carried note-bound lyrics.
        var versesInSystem = new Dictionary<(int Sys, int Staff), HashSet<int>>();
        foreach (var l in lyrics)
            if (measureToSystem.TryGetValue(l.Item.MeasureIndex, out int sys))
                (versesInSystem.TryGetValue((sys, l.Item.StaffIndex), out var set)
                        ? set
                        : versesInSystem[(sys, l.Item.StaffIndex)] = new())
                    .Add(l.Item.VerseNumber);

        // The stanza number is one label per (system, verse) at the left edge, so a
        // `~2` on ONE section's verse must suppress that whole baseline — otherwise
        // another (unhidden) section sharing the verse on the same system (a plain
        // reprise line) would keep re-printing the number the author asked to hide.
        var hiddenPairs = new HashSet<(int sys, int staff, int verse)>();
        foreach (var l in lyrics)
            if (l.Item.HideStanza && measureToSystem.TryGetValue(l.Item.MeasureIndex, out int hs))
                hiddenPairs.Add((hs, l.Item.StaffIndex, l.Item.VerseNumber));

        // Where each written number's verse begins (LyricItem.OpensStanzaRun).
        var runOpensOn = new HashSet<(int sys, int staff, int verse, int label)>();
        foreach (var l in lyrics)
            if (l.Item.OpensStanzaRun && measureToSystem.TryGetValue(l.Item.MeasureIndex, out int os))
                runOpensOn.Add((os, l.Item.StaffIndex, l.Item.VerseNumber, l.Item.StanzaLabel));

        // Collect (system, staff, verse, label) → first lyric of that run (its Y is the
        // baseline). ★ THE LABEL IS PART OF THE KEY (owner's design 2026-11-01): a section
        // written out twice carries verse 1 and then verse 2 on the SAME row, and when both
        // printings share a system the "2." stands mid-line before the second play's first
        // syllable — LilyPond's `\set stanza` prints where it changes, which is this.
        var firstLyricBySystem = new Dictionary<(int sys, int staff, int verse, int label), LyricLayout>();
        foreach (var l in lyrics)
        {
            if (l.Item.HideStanza)
                continue;
            if (!measureToSystem.TryGetValue(l.Item.MeasureIndex, out int sysIdx))
                continue;
            // Only a system that stacks 2+ verses gets numbers (unless forced) — or a line
            // that carries a written number: a `verse 2 { … }` on the second printing of a
            // section written out twice stands alone on its row and still says "2."
            // (LyricItem.StanzaLabel).
            if (!emitForFirstVerse && l.Item.StanzaLabel <= 0
                && (!versesInSystem.TryGetValue((sysIdx, l.Item.StaffIndex), out var vs)
                    || vs.Count <= 1))
                continue;
            if (hiddenPairs.Contains((sysIdx, l.Item.StaffIndex, l.Item.VerseNumber)))
                continue; // a ~-hidden verse suppresses this baseline's number outright
            // A written number on a line it has to itself (a section written out twice: one
            // row, nothing stacked to tell apart) stands where its verse begins and nowhere
            // else, LilyPond's own once-per-change (lily/stanza-number-engraver.cc:57-68
            // process_music). Owner's question 2026-11-01 (風の道): a verse running on to the
            // next line printed "1." and "2." again in the middle of its sentence.
            if (!emitForFirstVerse && l.Item.StanzaLabel > 0 && !runOpensOn.Contains((sysIdx, l.Item.StaffIndex, l.Item.VerseNumber, l.Item.StanzaLabel))
                && versesInSystem.TryGetValue((sysIdx, l.Item.StaffIndex), out var alone) && alone.Count <= 1)
                continue;
            var key = (sysIdx, l.Item.StaffIndex, l.Item.VerseNumber, Math.Max(0, l.Item.StanzaLabel));
            // Keep the lyric whose X is leftmost in the run (the verse's start).
            if (!firstLyricBySystem.TryGetValue(key, out var cur) || l.X < cur.X)
                firstLyricBySystem[key] = l;
        }

        // Of the runs on one row of one system, the LEFTMOST is the line's label and shares
        // the line's right edge (below); any later run is a mid-line label and stands off
        // its own first syllable.
        var lineStartRun = new Dictionary<(int sys, int staff, int verse), (int label, double x)>();
        foreach (var ((sysIdx, staff, verse, label), lyric) in firstLyricBySystem)
            if (!lineStartRun.TryGetValue((sysIdx, staff, verse), out var cur) || lyric.X < cur.x)
                lineStartRun[(sysIdx, staff, verse)] = (label, lyric.X);

        // The numbers' shared RIGHT edge per (system, staff): LilyPond's x-aligned-side
        // with direction LEFT and padding 1.0 off the number's supports — the syllables of
        // the timestep that made it, which Stanza_number_align_engraver widens to EVERY
        // verse's syllable of that timestep, so all the numbers of a line end together,
        // 1.0 left of the leftmost first syllable's ink. A syllable's ink left is its
        // centre less half its advance (LyricLayout.X, Width) — LilyPond reads the ink
        // extent, the residual HANDOFF R10⒝ names. Until session 567 the number STARTED a
        // flat 4.0 left of the first measure, whatever the syllables did (HANDOFF R10⒡).
        // MEASURED on 2.26.0 (Lab sessions/p567/stanza-lp.log, a hand twin of
        // test/lyrics-verses with \set stanza): "1." and "2." both end at 5.659 = the
        // leftmost first syllable "Twas" 6.659 − 1.0, though verse 1's "A" starts at 8.349.
        // LILYPOND-REF: scm/define-grobs.scm:3412-3427 StanzaNumber — direction LEFT, padding 1.0, X-offset x-aligned-side (stanza-number-interface)
        // LILYPOND-REF: lily/stanza-number-align-engraver.cc:62-71 Stanza_number_align_engraver — add_support of every syllable to every number of the timestep
        // LILYPOND-REF: lily/stanza-number-engraver.cc:70-75 Stanza_number_engraver — acknowledge_lyric_syllable adds the support
        // LILYPOND-REF: lily/side-position-interface.cc:189-260 aligned_side — direction LEFT puts the grob's RIGHT edge padding off the supports' left
        // LILYSHARP-OWN: LilyPond makes ONE number, where \set stanza changed; Lily# labels
        //   STACKED verses on EVERY system (the per-(system, staff) key above), anchored to
        //   that system's first syllables — there the number says which line is which.
        //   departs from: lily/stanza-number-engraver.cc:57-68 process_music (once per change).
        //   goes away when: never — the page's own reading convention.
        //   observed by: StanzaNumberAlignmentTests (two verses, one system).
        var leftmostFirstSyllable = new Dictionary<(int sys, int staff), double>();
        foreach (var ((sysIdx, staff, verse, label), lyric) in firstLyricBySystem)
        {
            if (lineStartRun[(sysIdx, staff, verse)].label != label)
                continue;   // a mid-line label aligns to its own syllable
            double inkLeft = lyric.X - lyric.Width / 2.0;
            if (!leftmostFirstSyllable.TryGetValue((sysIdx, staff), out var cur) || inkLeft < cur)
                leftmostFirstSyllable[(sysIdx, staff)] = inkLeft;
        }
        double em = Em(fonts);
        var style = Style(fonts);

        // ⚠️ IT WAITS FOR ITS FIRST LABEL, for the reason the other eight builders of session
        // 448 carry: `CreateBuilder<T>()` lays out its first block before a single Add, and
        // this one was empty in every build over the reader's corpus.
        ImmutableArray<StanzaNumberLayout>.Builder? builder = null;
        foreach (var ((sysIdx, staff, verseNumber, label), lyric) in firstLyricBySystem)
        {
            if (sysIdx >= systems.Length) continue;
            var system = systems[sysIdx];
            // The written number of a `verse N { … }` line, else the row's own.
            string text = $"{(label > 0 ? label : verseNumber)}.";
            bool midLine = lineStartRun[(sysIdx, staff, verseNumber)].label != label;
            // ⚠️ On a LEAD SHEET the anchor is the LINE START (the indent), not the
            // syllables: the grid opens every line with a bar line and the first line's
            // syllables start past the bar + meter prefix, so a label hung off them put the
            // "1."'s DOT on the line-start bar (user report 2026-08-20) — and only on the
            // first line, tearing the labels out of their column. One anchor per sheet keeps
            // every verse number in the same column, clear of the bar. LILYSHARP-OWN: the
            // chord-grid row prints bar lines LilyPond's Lyrics context has nothing of.
            //   departs from: the x-aligned-side rule above.
            //   goes away when: the grid's bar line becomes a support the number clears.
            //   observed by: the lead-sheet snapshots.
            double x = midLine
                ? lyric.X - lyric.Width / 2.0 - Padding
                    - Rendering.TextFontMetrics.Advance(text, em, sans: false, style)
                : leadSheet
                ? system.Indent - 4.0
                : leftmostFirstSyllable[(sysIdx, staff)] - Padding
                    - Rendering.TextFontMetrics.Advance(text, em, sans: false, style);
            (builder ??= ImmutableArray.CreateBuilder<StanzaNumberLayout>()).Add(
                new StanzaNumberLayout(
                VerseNumber: verseNumber,
                SystemIndex: sysIdx,
                MeasureIndex: lyric.Item.MeasureIndex,
                X: x,
                YUp: lyric.YUp,
                Text: text));
        }

        return builder?.ToImmutable() ?? [];
    }
}
