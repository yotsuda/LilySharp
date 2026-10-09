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

using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// The page's streams: what the twin reads off the page model (PageModel) rather than the syntax
// - inline @chord names, figured bass, lyrics - each as a variable of timed entries with silent
// gaps. Split out of LilyPondExporter.cs as a partial class (2026-10-02); same instance state,
// no behavior change.
public sealed partial class LilyPondExporter
{
    /// <summary>
    /// One <c>\chordmode</c> variable per part carrying INLINE <c>@chord</c> marks — read off
    /// the PAGE's model rather than the syntax: the collector has already placed every
    /// symbol at its note's moment, recognized a bare <c>@chord</c> from the notes it sits
    /// on and resolved a degree in its key, so the twin asks it instead of re-deriving the
    /// note durations a second time. Each bar is the symbols at their onsets with silent
    /// <c>s</c> filling the gaps, so LilyPond prints exactly the symbols the page prints,
    /// where it prints them; a bar's length is the part's own (a pickup is short).
    /// </summary>
    /// <remarks>
    /// The model's bar sequence is the twin's: neither unfolds a <c>\repeat volta</c> or an
    /// ending (both write the bars once, in order), and both write a reprise twice — so
    /// the linear stream lines up bar for bar with the music stream's written bars. The
    /// page collects only when the file writes a <c>@chord</c> at all (the collect costs a
    /// render). Two symbols at one moment (two voices) keep the first.
    /// ⚠️ A chords ROW is the other reader (<see cref="EmitChordTracks"/>, syntax-driven
    /// through the form walk); the two contexts stand side by side when a book has both.
    /// </remarks>
    /// <summary>The page's model of the exported score, collected ONCE on first demand —
    /// the readers that ask the page (inline chords, lyrics) share it. Null when the
    /// collect fails (warned once) or the file has no score.</summary>
    private Svg.Model.MultiStaffScore? PageModel(SyntaxTree tree, RenderDeclarationSyntax render)
    {
        if (_pageCollected)
            return _page;
        _pageCollected = true;
        try
        {
            var spec = Svg.Collector.RenderSpecParser.Parse(render);
            _pageSpec = spec;
            if (spec != null)
                // ⚠️ THE PER-SCORE REFERENCES ARE SET HERE, the way the render pipeline sets
                // them (SvgGenerator.CollectFor). Until 2026-09-11 this collect was a bare
                // `new MeasureCollector()`, so `score { fonts house }` was invisible to
                // the twin and it wrote the FILE's plan — the page and its twin read two
                // different plans, which is the one thing PageModel exists to prevent. Found
                // while wiring `layout NAME`; measured at the same time: no tracked book
                // writes a named fonts or paper block, so no `.ly` in the corpus moves.
                _page = new Svg.Collector.MeasureCollector
                {
                    FontsOverride = spec.FontsRef,
                    PaperOverride = spec.PaperRef,
                    LayoutOverride = spec.LayoutRef,
                }.CollectMultiStaff(tree, spec);
        }
        catch (Exception e)
        {
            _warnings.Add("the page could not be collected, so its chords and lyrics are not exported: " + e.Message);
        }
        return _page;
    }

    private Svg.Model.MultiStaffScore? _page;
    private Svg.Collector.RenderSpec? _pageSpec;
    private bool _pageCollected;

    /// <summary>Whether the page keeps the named lyrics row as a ROW (a band of its own).
    /// A row written directly under the staff it sings is FOLDED into that staff's attached
    /// verses (RenderSpecParser.FoldAdjacentRows) and reaches the twin under the staff's
    /// name instead — so a row spec of that name is absent, and nothing is missing.</summary>
    private bool PageKeepsLyricsRow(string partName)
        => _pageSpec == null
           || _pageSpec.Items.OfType<Svg.Collector.LyricsRowSpec>().Any(r => r.PartName == partName);

    /// <summary>The length of one bar of a voice: its items' durations summed, else (an
    /// absent voice's placeholder bar has no items) the meter in force there.</summary>
    private static Fraction BarLength(Svg.Model.Measure measure, IReadOnlyList<Fraction> meters, int m)
    {
        var length = Fraction.Zero;
        foreach (var it in measure.Items)
            length += it.Duration;
        return length > Fraction.Zero || m >= meters.Count ? length : meters[m];
    }

    /// <summary>
    /// One variable of TIMED entries read off the page — the one shape the three page streams
    /// share (inline chord names, figured bass, lyrics): the staff's bars in order; in each bar
    /// every entry at its onset, lasting to the next entry of the bar at a LATER moment (else the
    /// bar's end), the gaps before and after filled with the mode's silence
    /// (<paramref name="silence"/>: <c>s</c>, or <c>\skip</c> for lyrics). What an entry is
    /// (<paramref name="entry"/>: a <c>\chordmode</c> entry, a figure group, a syllable with its
    /// alignment and connector) is the caller's; everything else is here ONCE.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-02 this loop stood three times, written out (the third copied from the
    /// first the day before), and a rule on it — the first of two entries on one moment wins, a
    /// bar a line break splits (<c>Measure.BreaksMidBar</c>) is ONE bar in LilyPond with no bar
    /// check between its halves, an entry the walk placed past the part's last bar is dropped —
    /// had to be kept the same in three places by hand.
    /// </remarks>
    /// <param name="senza">Whether <c>time none</c> is in force at each bar
    /// (<see cref="Svg.Layout.MultiMeasureRestEngraver.PrevailingSenzaMisura"/>): such a bar
    /// ends with no bar check, since under <c>\cadenzaOn</c> LilyPond counts nothing and a
    /// <c>|</c> in a lyric or chord line fails ("bar check failed", session 762's chorale) —
    /// the music line draws the bar with <c>\bar "|"</c> instead (<c>BarlineText</c>).</param>
    private void EmitTimedStream<T>(string varName, string mode, string silence,
        System.Collections.Immutable.ImmutableArray<Svg.Model.Measure> measures,
        IReadOnlyList<Fraction> meters, List<(T Item, int Measure, Fraction Onset)> items,
        Action<StringBuilder, T, string> entry, bool[]? senza = null)
    {
        _sb.Append(varName).Append(" = \\").Append(mode).Append(" {\n");
        int k = 0;
        for (int m = 0; m < measures.Length; m++)
        {
            var length = BarLength(measures[m], meters, m);
            var line = new StringBuilder("  ");
            var at = Fraction.Zero;
            while (k < items.Count && items[k].Measure == m)
            {
                var (item, _, onset) = items[k++];
                if (onset < at)
                    continue;   // the same moment as the entry just written: the first wins
                if (onset > at)
                {
                    AppendToken(line, silence + ChordModeDuration(onset - at), "  ");
                    at = onset;
                }
                // Up to the next entry of this bar at a LATER moment, else the bar end.
                var next = length;
                for (int j = k; j < items.Count && items[j].Measure == m; j++)
                    if (items[j].Onset > at) { next = items[j].Onset; break; }
                if (next > length) next = length;
                entry(line, item, ChordModeDuration(next - at));
                at = next;
            }
            if (at < length)
                AppendToken(line, silence + ChordModeDuration(length - at), "  ");
            // The two halves of a bar a line break splits (Measure.BreaksMidBar) are ONE
            // bar in LilyPond: no bar check between them — nor at the end of a cadenza bar.
            bool noCheck = measures[m].BreaksMidBar || (senza != null && m < senza.Length && senza[m]);
            _sb.Append(line).Append(noCheck ? "\n" : " |\n");
        }
        // Entries the walk placed past the part's last bar (none in practice) are dropped.
        _sb.Append("}\n\n");
    }

    private void EmitInlineChordTracks(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null)
            return;
        var root = tree.GetRoot();
        if (!root.DescendantNodes<MusicMarkSyntax>().Any(m => m.Name == "chord"))
            return;
        if (PageModel(tree, render) is not { } score)
            return;

        var meters = Svg.Layout.ScoreSideTables.PrevailingMeters(score);
        foreach (var (_, staff, idx) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            string partName = staff.PrimaryVoice.Name;
            if (_inlineChordVars.ContainsKey(partName))
                continue;
            var items = score.ChordNames
                .Where(c => !c.UseTiming && c.StaffIndex == idx)
                .OrderBy(c => c.MeasureIndex).ThenBy(c => c.Timing)
                .Select(c => (Item: c, Measure: c.MeasureIndex, Onset: c.Timing))
                .ToList();
            if (items.Count == 0)
                continue;
            foreach (var (c, _, _) in items)
                _shared.InlineChordMarks.Add(c.SourcePosition);

            string varName = VarName(partName + "InlineChords");
            _inlineChordVars[partName] = varName;
            EmitTimedStream(varName, "chordmode", "s", staff.PrimaryVoice.Measures, meters, items,
                (line, c, duration) => AppendToken(line, InlineChordEntry(c, duration), "  "), SenzaMisuraByBar(score));
        }
    }

    /// <summary>
    /// One <c>\figuremode</c> variable per staff carrying <c>@figuredBass</c> marks, read off
    /// the page's model like the inline chords (<see cref="EmitInlineChordTracks"/>): each
    /// group at its note's onset, lasting to the next group of the bar (or the bar's end),
    /// with silent <c>s</c> between, so LilyPond's FiguredBass context prints the figures the
    /// page prints, under the staff, where it prints them.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-02 every <c>@figuredBass</c> was "dropped (out of scope)" — 13 of the 25
    /// twin warnings left in the repository (Lab sessions/p546/warnings-after.tsv).
    /// A held figure (<c>_</c>, the page's continuation dash) has no figure of its own in
    /// LilyPond (an extender joins two equal figures) and is written blank, with a warning.
    /// LILYPOND-REF: ly/engraver-init.ly FiguredBass context; lily/figured-bass-engraver.cc —
    ///   <c>\figuremode</c> entries <c>&lt;6 4&gt;</c> (top first), <c>6+</c> sharp, <c>6-</c>
    ///   flat, <c>6!</c> natural, <c>_+</c> a bare accidental, <c>_</c> a blank figure.
    /// </remarks>
    private void EmitFiguredBassTracks(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null)
            return;
        if (!tree.GetRoot().DescendantNodes<MusicMarkSyntax>().Any(m => m.Name == "figuredBass"))
            return;
        if (PageModel(tree, render) is not { } score || score.FiguredBasses.IsDefaultOrEmpty)
            return;

        var meters = Svg.Layout.ScoreSideTables.PrevailingMeters(score);
        foreach (var (_, staff, idx) in score.EnumerateStaves())
        {
            if (staff.IsTextRow)
                continue;
            string partName = staff.PrimaryVoice.Name;
            if (_figureVars.ContainsKey(partName))
                continue;
            var measures = staff.PrimaryVoice.Measures;
            // The page names a group by its bass note's item; its onset is the items before it.
            var items = score.FiguredBasses
                .Where(f => f.StaffIndex == idx && f.MeasureIndex < measures.Length
                            && f.ItemIndex < measures[f.MeasureIndex].Items.Length)
                .Select(f => (Item: f, Measure: f.MeasureIndex, Onset: OnsetOf(measures[f.MeasureIndex], f.ItemIndex)))
                .OrderBy(t => t.Measure).ThenBy(t => t.Onset)
                .ToList();
            if (items.Count == 0)
                continue;
            foreach (var (f, _, _) in items)
                _shared.FigureMarks.Add(f.SourcePosition);

            string varName = VarName(partName + "Figures");
            _figureVars[partName] = varName;
            EmitTimedStream(varName, "figuremode", "s", measures, meters, items,
                (line, f, duration) => AppendToken(line, FigureGroup(f) + duration, "  "), SenzaMisuraByBar(score));
        }
    }

    private static Fraction OnsetOf(Svg.Model.Measure measure, int itemIndex)
    {
        var at = Fraction.Zero;
        for (int i = 0; i < itemIndex; i++)
            at += measure.Items[i].Duration;
        return at;
    }

    /// <summary>One figure group in <c>\figuremode</c>: <c>&lt;6 4+&gt;</c>.</summary>
    private string FigureGroup(Svg.Model.FiguredBassItem f)
    {
        var figures = new List<string>(f.Figures.Length);
        foreach (var fig in f.Figures)
        {
            if (fig.Held)
            {
                _warnings.Add("a held figure (@figuredBass(_)) is written blank: LilyPond draws a "
                    + "continuation only between two equal figures");
                figures.Add("_");
                continue;
            }
            string alt = fig.Alteration switch { 1 => "+", -1 => "-", 2 => "!", _ => "" };
            figures.Add((fig.Number > 0 ? fig.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "_") + alt);
        }
        return "<" + string.Join(" ", figures) + ">";
    }

    /// <summary>The FiguredBass context for a part's <c>@figuredBass</c> marks, under the first
    /// staff that shows the part — once.</summary>
    private void AddFiguredBassRow(List<string> rows, string? partName, string indent)
    {
        if (partName != null && _figureVars.TryGetValue(partName, out var v) && _figurePlaced.Add(partName))
            rows.Add(indent + "\\new FiguredBass \\" + v + "\n");
    }

    private readonly Dictionary<string, string> _figureVars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _figurePlaced = new(StringComparer.Ordinal);

    /// <summary>
    /// One <c>\lyricmode</c> variable per lyric LINE the page places — a staff's attached
    /// verse, a melody-bound row, an independent even-spread row, each stacked verse its own
    /// — read off the page's model like the inline chords: every syllable at its placed
    /// moment with its length to the next syllable of the line (or the bar's end), so a
    /// melisma is simply a longer syllable, and <c>\skip</c> filling the gaps. LilyPond then
    /// sets the words by their own durations, which are the note onsets the page aligned
    /// them to — no <c>\lyricsto</c>, no named voices, and the same reading for all three
    /// kinds of line. A melisma syllable carries the page's LEFT alignment as a
    /// <c>\once \override</c>, since without <c>\lyricsto</c> LilyPond has no melisma to
    /// align by. The context stands below the staff it is attached to, or at the row's
    /// place (<see cref="EmitScore"/>).
    /// </summary>
    /// <remarks>
    /// MEASURED (session 350, scratch/p351/lp b.ly): a <c>\lyricmode</c> line with written
    /// durations under a Staff is what LilyPond's own spacing was probed with, and it read
    /// the syllables at the note columns. Deliberately NOT carried: the stanza number the
    /// page prints before verse 2+ (LilyPond's <c>\set stanza</c>), and the extender's exact
    /// end (the page stops it at the last held head, LilyPond's <c>__</c> runs to the next
    /// syllable) — both are named in the CHANGELOG. A connector is the page's:
    /// <c>--</c> after a hyphenated syllable, <c>__</c> after an extended one; an extender
    /// whose end comes before the next syllable (or the line's end) gets an empty syllable to
    /// end at (<see cref="ExtenderTerminator"/>), and a cadenza bar no bar check
    /// (<see cref="SenzaMisuraByBar"/>) — both 2026-10-03.
    /// </remarks>
    private void EmitLyricTracks(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null)
            return;
        if (!tree.GetRoot().DescendantNodes<LyricsBlockSyntax>().Any())
            return;
        if (PageModel(tree, render) is not { } score || score.Lyrics.IsDefaultOrEmpty)
            return;

        var meters = Svg.Layout.ScoreSideTables.PrevailingMeters(score);
        var senza = SenzaMisuraByBar(score);
        var staves = score.EnumerateStaves().ToDictionary(t => t.GlobalStaffIndex, t => t.Staff);
        // One line = one staff's one voice's one verse (a row's lines key on the row's staff).
        var lines = score.Lyrics
            .GroupBy(l => (l.StaffIndex, l.VoiceId, l.VerseNumber, l.IsLyricsRow))
            .OrderBy(g => g.Key.StaffIndex).ThenBy(g => g.Key.VoiceId).ThenBy(g => g.Key.VerseNumber);
        foreach (var line in lines)
        {
            if (!staves.TryGetValue(line.Key.StaffIndex, out var staff))
                continue;
            string partName = staff.PrimaryVoice.Name;
            var items = line.OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing)
                .Select(l => (Item: l, Measure: l.MeasureIndex, Onset: l.Timing))
                .ToList();
            // An extender ends at the NEXT syllable in LilyPond — and in this duration-carrying
            // \lyricmode form (no \lyricsto, no voice to take heads from) it needs that syllable
            // at the very next moment of the line: with a \skip between, the extender is dropped
            // in silence, and with nothing after it at all LilyPond warns "unterminated extender"
            // (drawing none). So wherever the page's extender ends BEFORE the next syllable — a
            // melisma followed by a rest or by notes nobody sings, or a bare `__` on a note that
            // holds nothing — an EMPTY syllable is written at that end (ExtenderTerminator), as a
            // line's last extender has had since 2026-10-03 (session 764); every extender since
            // session 776, when the chorale's `hill __ | Sing` showed the silent drop.
            for (int k = 0; k < items.Count; k++)
            {
                if (items[k].Item.ConnectorType != Svg.Model.LyricConnectorType.Extender
                    || ExtenderTerminator(staff.PrimaryVoice.Measures, meters, items[k].Item) is not { } end)
                    continue;
                if (k + 1 < items.Count && !(items[k + 1].Measure > end.Measure
                        || (items[k + 1].Measure == end.Measure && items[k + 1].Onset > end.Onset)))
                    continue; // the next syllable stands at (or before) the end: it terminates the line
                items.Insert(k + 1, (items[k].Item with
                {
                    Text = "", ConnectorType = Svg.Model.LyricConnectorType.None, MelismaAlignLeft = false,
                    MeasureIndex = end.Measure, Timing = end.Onset,
                }, end.Measure, end.Onset));
                k++;
            }

            // A LilyPond identifier is letters only, so the verse (and voice) is a word.
            string varName = VarName(partName
                + (line.Key.VoiceId > 0 && !line.Key.IsLyricsRow ? "Voice" + NumberWord(line.Key.VoiceId) : "")
                + "Lyrics" + NumberWord(line.Key.VerseNumber));
            var target = line.Key.IsLyricsRow ? _lyricRowVars : _lyricVars;
            if (!target.TryGetValue(partName, out var vars))
                target[partName] = vars = new List<string>();
            vars.Add(varName);

            EmitTimedStream(varName, "lyricmode", "\\skip ", staff.PrimaryVoice.Measures, meters, items,
                (text, l, duration) =>
                {
                    // A melisma syllable is LEFT-aligned on its note (the page's `~` / `__`;
                    // LilyPond's lyricMelismaAlignment, lily/lyric-engraver.cc:180-183). LilyPond
                    // learns a melisma only through \lyricsto from the music's slurs, ties or
                    // \melisma, which a duration-carrying \lyricmode line never tells it — so
                    // the alignment the page applies is written out on the syllable itself.
                    // Owner's decision 2026-09-08: `~` stays Lily#'s melisma source; the twin
                    // carries its consequence rather than switching to \lyricsto.
                    if (l.MelismaAlignLeft)
                        AppendToken(text, "\\once \\override LyricText.self-alignment-X = #LEFT", "  ");
                    AppendToken(text, LyricSyllable(l.Text) + duration, "  ");
                    if (l.ConnectorType == Svg.Model.LyricConnectorType.Hyphen)
                        AppendToken(text, "--", "  ");
                    else if (l.ConnectorType == Svg.Model.LyricConnectorType.Extender)
                        AppendToken(text, "__", "  ");
                }, senza);
        }
    }

    /// <summary>
    /// The silent TIMING track of a lead sheet's twin — a score of text rows and no staff
    /// (<see cref="Svg.Model.MultiStaffScore.IsLeadSheet"/>). The rows' lines end every bar
    /// with a bar check, and LilyPond checks them against ITS clock, which with no staff's
    /// music never hears the pickup or a change of meter: the chorale's words-only score
    /// opened <c>The4 |</c> under a 4/4 clock and the check failed (session 762). This track
    /// carries the page's grid — the bar lengths of the row the page draws its bar lines on
    /// (<see cref="Svg.Model.MultiStaffScore.GridBarlineRowIndex"/>) — as skips in a
    /// <c>\new Devnull</c> (<see cref="EmitScore"/> places it first), so LilyPond's clock is
    /// the page's and nothing is printed. A bar a line break splits is one bar of the clock,
    /// as it is one bar of the lines (<see cref="EmitTimedStream"/>).
    /// </summary>
    /// <remarks>
    /// LILYSHARP-OWN: LilyPond has no lead sheet — a staff-less score prints no bar line,
    /// meter or bar number, so the page's grid has no counterpart to match and this track
    /// only makes the clock agree. Nothing observes the difference; the track goes if the
    /// twin ever draws the grid on a staff of its own, which would carry the timing itself.
    /// The page's rows know their bars' LENGTHS only (a row's spacers; no time-change item
    /// reaches a row — the page prints the score's meter once, at the sheet's head), so every
    /// change of length is written as a <c>\time</c> of that length, a length equal to the
    /// score's meter in the meter's own spelling (4/4, not 1/1) — the pickup as
    /// <c>\time 1/4</c>, NOT as <c>\partial</c>: MEASURED (2.26.0, Lab sessions/p777/probes:
    /// m.ly, m2.ly), a <c>\partial</c> in a score with no staff trips LilyPond's spacing
    /// ("programming error: insane spring distance requested", twice, and the syllables
    /// move), where a <c>\time</c> of the pickup's length is silent.
    /// </remarks>
    private void EmitLeadSheetTiming(SyntaxTree tree, RenderDeclarationSyntax? render)
    {
        if (render == null || PageModel(tree, render) is not { IsLeadSheet: true } score)
            return;
        Svg.Model.Staff? grid = null;
        foreach (var (_, staff, idx) in score.EnumerateStaves())
            if (idx == score.GridBarlineRowIndex)
            {
                grid = staff;
                break;
            }
        if (grid == null || grid.PrimaryVoice.Measures.Length == 0)
            return;
        var measures = grid.PrimaryVoice.Measures;
        var meters = Svg.Layout.ScoreSideTables.PrevailingMeters(score);
        var senza = SenzaMisuraByBar(score);
        var home = score.TimeSignature;
        var marks = LeadSheetScoreMarks(score);

        _leadSheetTimingVar = VarName("leadSheetTiming");
        _sb.Append(_leadSheetTimingVar).Append(" = {\n");
        Fraction? inForce = null;   // the clock's measure length, once written
        bool inCadenza = false;
        for (int m = 0; m < measures.Length; m++)
        {
            int last = m;
            var length = BarLength(measures[m], meters, m);
            while (measures[last].BreaksMidBar && last + 1 < measures.Length)
                length += BarLength(measures[++last], meters, last);
            bool cadenza = m < senza.Length && senza[m];
            var line = new StringBuilder("  ");
            if (cadenza != inCadenza)
            {
                AppendToken(line, cadenza ? "\\cadenzaOn" : "\\cadenzaOff", "  ");
                inCadenza = cadenza;
                inForce = null;     // the clock is re-armed when the cadenza closes
            }
            if (!cadenza && length != inForce)
            {
                AppendToken(line, !home.SenzaMisura && length == home.MeasureDuration
                    ? TimeText(new Semantics.Meter(home.Beats, home.BeatType, home.BeatsText))
                    : "\\time " + length.Numerator + "/" + length.Denominator, "  ");
                inForce = length;
            }
            for (int h = m; h <= last; h++)
            {
                // The bar's SCORE marks stand at its head, before its skip (see LeadSheetScoreMarks).
                if (marks.TryGetValue(h, out var atBar))
                    foreach (string mark in atBar)
                        AppendToken(line, mark, "  ");
                AppendToken(line, "s" + ChordModeDuration(BarLength(measures[h], meters, h)), "  ");
            }
            _sb.Append(line).Append(cadenza ? "\n" : " |\n");
            m = last;
        }
        _sb.Append("}\n\n");
    }

    /// <summary>
    /// The SCORE-level marks a lead sheet's timing track carries, per bar: the metronome marks
    /// and the section labels the page draws, in LilyPond's spelling. A rows-only score has no
    /// staff variable in its <c>\score</c>, so a <c>\tempo</c> or <c>\mark</c> written in the
    /// part variables never reaches LilyPond's Score — the header tempo and every section
    /// label of a chords-only twin were missing from its page until session 787 (the
    /// amazing-grace grid printed neither "Verse" nor ♩ = 84). The Devnull track is heard:
    /// MEASURED 2.26.0 (Lab sessions/p787/probes/devnull.ly), a <c>\tempo</c> and two
    /// <c>\mark \markup \box</c> in a <c>\new Devnull</c> stream engrave over the ChordNames
    /// line exactly as from a staff.
    /// </summary>
    /// <remarks>
    /// THE LIST IS THE PAGE'S OWN: <see cref="Svg.Layout.MusicMarkEngraver.BuildAllMarks"/>, the
    /// one home that merges the header tempo and the section labels into the drawn marks (and
    /// drops the header's stream copies, and every label under <c>layout { sectionLabels
    /// none }</c>) — so the twin writes what the page draws, not a second reading of the
    /// source. The labels are read off the FIRST text row's measures, which is where the
    /// rows-only collector stamps them (MeasureCollector: "A rows-only score prints its
    /// section labels from the FIRST row's measures"). A <c>plain</c> label drops the
    /// <c>\box</c>, as the staff twin's does (EmitSectionHead). Navigation marks (segno, coda,
    /// D.S.) keep their own spelling and are not written here.
    /// LILYPOND-REF: lily/translator-group.cc connect_to_context — a translator's listeners
    ///   hang on the context's events-below dispatcher, so Score's Metronome_mark_engraver and
    ///   Mark_engraver hear a tempo-change or ad-hoc-mark event from ANY descendant, a Devnull
    ///   included.
    /// </remarks>
    private Dictionary<int, List<string>> LeadSheetScoreMarks(Svg.Model.MultiStaffScore score)
    {
        var byBar = new Dictionary<int, List<string>>();
        Svg.Model.Staff? firstRow = null;
        foreach (var (_, staff, _) in score.EnumerateStaves())
            if (staff.IsTextRow && staff.PrimaryVoice.Measures.Length > 0)
            {
                firstRow = staff;
                break;
            }
        if (firstRow == null)
            return byBar;
        var marks = Svg.Layout.MusicMarkEngraver.BuildAllMarks(
            score.MusicMarks, firstRow.PrimaryVoice.Measures, score.Tempo, score.SwingSubdivision,
            score.TempoText, score.TempoBeatUnit, score.TempoDots, 0, _layoutPlan.SectionLabels);
        foreach (var mark in marks)
        {
            string text;
            switch (mark.Type)
            {
                case Svg.Model.MusicMarkType.Tempo:
                    text = EmitTempo(mark);
                    break;
                case Svg.Model.MusicMarkType.SectionLabel:
                    text = _layoutPlan.SectionLabels == Semantics.SectionLabelStyle.Plain
                        ? "\\mark \\markup \"" + Escape(mark.Text) + "\""
                        : "\\mark \\markup \\box \"" + Escape(mark.Text) + "\"";
                    break;
                default:
                    continue;
            }
            if (text.Length == 0)
                continue;
            if (!byBar.TryGetValue(mark.MeasureIndex, out var list))
                byBar[mark.MeasureIndex] = list = new List<string>();
            list.Add(text);
        }
        return byBar;
    }

    /// <summary>The lead sheet's timing track, when the score is one (<see cref="EmitLeadSheetTiming"/>).</summary>
    private string? _leadSheetTimingVar;

    /// <summary>Whether <c>time none</c> is in force at each bar of the page — the walk
    /// <see cref="Svg.Layout.MultiMeasureRestEngraver.PrevailingMeters"/> makes, asked for the
    /// cadenza (every voice's time changes, the score's signature first).</summary>
    private static bool[] SenzaMisuraByBar(Svg.Model.MultiStaffScore score)
        => Svg.Layout.MultiMeasureRestEngraver.PrevailingSenzaMisura(
            score.AllVoices.Select(v => v.Measures).ToList(), score.MeasureCount, score.TimeSignature.SenzaMisura);

    /// <summary>
    /// Where an extender's EMPTY terminating syllable goes: the onset after the melisma's last
    /// note (<see cref="Svg.Model.LyricItem.MelismaEndMeasureIndex"/>; the syllable's own note
    /// when it holds none — a bare <c>__</c>) — the next bar's head when that note closes its
    /// bar — or, when that note is the part's last, the note's own onset; null when the
    /// extender sits on that last note itself (then LilyPond's warning stands, and nothing is
    /// drawn either way).
    /// </summary>
    /// <remarks>
    /// MEASURED (2.26.0, Lab sessions/p764/probes/extender.ly): <c>la1 __ \skip 1 ""1</c> draws
    /// the extender to the empty syllable and warns nothing; <c>la1 __ \skip 1 \skip 1</c> warns
    /// "unterminated extender" and draws none. MEASURED (Lab sessions/p776/probes/mid-extender2.ly):
    /// <c>la2 __ \skip 4 \skip 4 | lu1</c> warns nothing and draws NOTHING either — a skip between
    /// the extender and the next syllable drops it — while <c>la2 __ ""4 \skip 4 | lu1</c> and the
    /// same with nothing after the skips end it at the empty syllable, drawn when the room allows
    /// (LilyPond's minimum-length and drop threshold, which the page shares). The page stops its
    /// extender at the last held head's RIGHT (lily/lyric-extender.cc:80-84, as LyricItem says);
    /// LilyPond's reaches the next syllable's LEFT, which is the following note's column — one
    /// head's width apart at most, and at the part's last note the terminator stands ON the head,
    /// a head short.
    /// </remarks>
    private static (int Measure, Fraction Onset)? ExtenderTerminator(
        System.Collections.Immutable.ImmutableArray<Svg.Model.Measure> measures,
        IReadOnlyList<Fraction> meters, Svg.Model.LyricItem last)
    {
        // A bare `__` holds no note: the line the page draws is the stub past its own note.
        bool bare = last.MelismaEndMeasureIndex < 0;
        int m = bare ? last.MeasureIndex : last.MelismaEndMeasureIndex;
        var heldOnset = bare ? last.Timing : last.MelismaEndTiming;
        if (m < 0 || m >= measures.Length)
            return null;
        var at = Fraction.Zero;
        foreach (var it in measures[m].Items)
        {
            if (it.Duration > Fraction.Zero && at == heldOnset)
            {
                var end = at + it.Duration;
                if (end < BarLength(measures[m], meters, m))
                    return (m, end);
                if (m + 1 < measures.Length)
                    return (m + 1, Fraction.Zero);
                return last.MeasureIndex == m && last.Timing == at ? null : (m, at);
            }
            at += it.Duration;
        }
        return null;
    }

    /// <summary>A small number as a word, for a LilyPond identifier (letters only).</summary>
    private static string NumberWord(int n) => n switch
    {
        1 => "One", 2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six",
        7 => "Seven", 8 => "Eight", 9 => "Nine", 10 => "Ten", 11 => "Eleven", 12 => "Twelve",
        _ => "Many" + new string('I', Math.Max(0, n - 12)),
    };

    /// <summary>A syllable as a <c>\lyricmode</c> word: bare when it is letters (an
    /// apostrophe and the usual punctuation included), quoted otherwise — a digit, a
    /// space, a quote, a backslash, a lone connector-looking word all need the quotes.</summary>
    private static string LyricSyllable(string text)
    {
        bool bare = text.Length > 0;
        foreach (char ch in text)
            if (!(char.IsLetter(ch) || ch is ',' or '.' or '!' or '?' or ';' or ':' or '‿'))
            {
                bare = false;
                break;
            }
        if (bare && text != "--" && text != "__" && text != "_" && text != "~")
            return text;
        return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>The Lyrics contexts attached BELOW a part's staff (each verse its own), or
    /// standing as a row — placed once per part, in verse order.</summary>
    private void AddLyricRows(List<string> rows, string? partName, string indent, bool asRow)
    {
        var source = asRow ? _lyricRowVars : _lyricVars;
        var placed = asRow ? _lyricRowPlaced : _lyricPlaced;
        if (partName != null && source.TryGetValue(partName, out var vars) && placed.Add(partName))
            foreach (var v in vars)
                rows.Add(indent + "\\new Lyrics \\" + v + "\n");
    }

    /// <summary>The <c>\lyricmode</c> variables per part (attached lines, below the staff)
    /// and per lyrics ROW part (standing rows), in verse order; and the parts placed.</summary>
    private readonly Dictionary<string, List<string>> _lyricVars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _lyricRowVars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lyricPlaced = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lyricRowPlaced = new(StringComparer.Ordinal);
}
