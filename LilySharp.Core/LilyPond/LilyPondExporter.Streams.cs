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
                // `new MeasureCollector()`, so `score main { fonts house }` was invisible to
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
                .ToList();
            if (items.Count == 0)
                continue;
            foreach (var c in items)
                _shared.InlineChordMarks.Add(c.SourcePosition);

            string varName = VarName(partName + "InlineChords");
            _inlineChordVars[partName] = varName;
            _sb.Append(varName).Append(" = \\chordmode {\n");
            var measures = staff.PrimaryVoice.Measures;
            int k = 0;
            for (int m = 0; m < measures.Length; m++)
            {
                var length = BarLength(measures[m], meters, m);
                var line = new StringBuilder("  ");
                var at = Fraction.Zero;
                while (k < items.Count && items[k].MeasureIndex == m)
                {
                    var c = items[k++];
                    if (c.Timing < at)
                        continue;   // the same moment as the symbol just written: the first wins
                    if (c.Timing > at)
                    {
                        AppendToken(line, "s" + ChordModeDuration(c.Timing - at), "  ");
                        at = c.Timing;
                    }
                    // Up to the next symbol of this bar at a LATER moment, else the bar end.
                    var next = length;
                    for (int j = k; j < items.Count && items[j].MeasureIndex == m; j++)
                        if (items[j].Timing > at) { next = items[j].Timing; break; }
                    if (next > length) next = length;
                    AppendToken(line, InlineChordEntry(c, ChordModeDuration(next - at)), "  ");
                    at = next;
                }
                if (at < length)
                    AppendToken(line, "s" + ChordModeDuration(length - at), "  ");
                // The two halves of a bar a line break splits (Measure.BreaksMidBar) are ONE
                // bar in LilyPond: no bar check between them.
                _sb.Append(line).Append(measures[m].BreaksMidBar ? "\n" : " |\n");
            }
            // Symbols the walk placed past the part's last bar (none in practice) are dropped.
            _sb.Append("}\n\n");
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
                .Select(f => (Item: f, Onset: OnsetOf(measures[f.MeasureIndex], f.ItemIndex)))
                .OrderBy(t => t.Item.MeasureIndex).ThenBy(t => t.Onset)
                .ToList();
            if (items.Count == 0)
                continue;
            foreach (var (f, _) in items)
                _shared.FigureMarks.Add(f.SourcePosition);

            string varName = VarName(partName + "Figures");
            _figureVars[partName] = varName;
            _sb.Append(varName).Append(" = \\figuremode {\n");
            int k = 0;
            for (int m = 0; m < measures.Length; m++)
            {
                var length = BarLength(measures[m], meters, m);
                var line = new StringBuilder("  ");
                var at = Fraction.Zero;
                while (k < items.Count && items[k].Item.MeasureIndex == m)
                {
                    var (f, onset) = items[k++];
                    if (onset < at)
                        continue;   // the same moment as the group just written: the first wins
                    if (onset > at)
                    {
                        AppendToken(line, "s" + ChordModeDuration(onset - at), "  ");
                        at = onset;
                    }
                    var next = length;
                    for (int j = k; j < items.Count && items[j].Item.MeasureIndex == m; j++)
                        if (items[j].Onset > at) { next = items[j].Onset; break; }
                    if (next > length) next = length;
                    AppendToken(line, FigureGroup(f) + ChordModeDuration(next - at), "  ");
                    at = next;
                }
                if (at < length)
                    AppendToken(line, "s" + ChordModeDuration(length - at), "  ");
                _sb.Append(line).Append(measures[m].BreaksMidBar ? "\n" : " |\n");
            }
            _sb.Append("}\n\n");
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
    /// <c>--</c> after a hyphenated syllable, <c>__</c> after an extended one.
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
            var items = line.OrderBy(l => l.MeasureIndex).ThenBy(l => l.Timing).ToList();
            var measures = staff.PrimaryVoice.Measures;

            // A LilyPond identifier is letters only, so the verse (and voice) is a word.
            string varName = VarName(partName
                + (line.Key.VoiceId > 0 && !line.Key.IsLyricsRow ? "Voice" + NumberWord(line.Key.VoiceId) : "")
                + "Lyrics" + NumberWord(line.Key.VerseNumber));
            var target = line.Key.IsLyricsRow ? _lyricRowVars : _lyricVars;
            if (!target.TryGetValue(partName, out var vars))
                target[partName] = vars = new List<string>();
            vars.Add(varName);

            _sb.Append(varName).Append(" = \\lyricmode {\n");
            int k = 0;
            for (int m = 0; m < measures.Length; m++)
            {
                var length = BarLength(measures[m], meters, m);
                var text = new StringBuilder("  ");
                var at = Fraction.Zero;
                while (k < items.Count && items[k].MeasureIndex == m)
                {
                    var l = items[k++];
                    if (l.Timing < at)
                        continue;   // two syllables on one moment: the first wins
                    if (l.Timing > at)
                    {
                        AppendToken(text, "\\skip " + ChordModeDuration(l.Timing - at), "  ");
                        at = l.Timing;
                    }
                    var next = length;
                    for (int j = k; j < items.Count && items[j].MeasureIndex == m; j++)
                        if (items[j].Timing > at) { next = items[j].Timing; break; }
                    if (next > length) next = length;
                    // A melisma syllable is LEFT-aligned on its note (the page's `~` / `__`;
                    // LilyPond's lyricMelismaAlignment, lily/lyric-engraver.cc:180-183). LilyPond
                    // learns a melisma only through \lyricsto from the music's slurs, ties or
                    // \melisma, which a duration-carrying \lyricmode line never tells it — so
                    // the alignment the page applies is written out on the syllable itself.
                    // Owner's decision 2026-09-08: `~` stays Lily#'s melisma source; the twin
                    // carries its consequence rather than switching to \lyricsto.
                    if (l.MelismaAlignLeft)
                        AppendToken(text, "\\once \\override LyricText.self-alignment-X = #LEFT", "  ");
                    AppendToken(text, LyricSyllable(l.Text) + ChordModeDuration(next - at), "  ");
                    if (l.ConnectorType == Svg.Model.LyricConnectorType.Hyphen)
                        AppendToken(text, "--", "  ");
                    else if (l.ConnectorType == Svg.Model.LyricConnectorType.Extender)
                        AppendToken(text, "__", "  ");
                    at = next;
                }
                if (at < length)
                    AppendToken(text, "\\skip " + ChordModeDuration(length - at), "  ");
                // The two halves of a bar a line break splits (Measure.BreaksMidBar) are ONE
                // bar in LilyPond: no bar check between them.
                _sb.Append(text).Append(measures[m].BreaksMidBar ? "\n" : " |\n");
            }
            _sb.Append("}\n\n");
        }
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
