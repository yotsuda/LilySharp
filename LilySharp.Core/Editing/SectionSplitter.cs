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
using System.Text.RegularExpressions;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// "Split Sections to Match a Part": one part of a file grouped by part has cut a section into
/// several (<c>vn1: section A { 16 bars } section B { 121 bars }</c>) while the others still
/// hold the whole passage in one (<c>section A { 137 bars }</c>) — the LYS2007 "not the same
/// length everywhere" case. This cuts the OTHER parts' section at the same bars into the same
/// section names, and makes every form play the new sections where it played the old one.
/// </summary>
/// <remarks>
/// <para>
/// Owner's decisions (2026-09-28): part-major files only; the part that subdivides the others
/// is found, not asked for, unless two parts subdivide differently; a spanner crossing a cut
/// and a cut that falls mid-bar are REPORTED, never rewritten; and the rewrite is checked
/// before it is offered — the result compiles, every rewritten part sounds exactly as it did
/// (MIDI pitches and times, part by part) and writes as many bars, the length warning on the
/// section is gone and no error is new. Anything else and nothing is returned but the reason.
/// </para>
/// <para>
/// ⚠️ A CUT IS A SECTION BOUNDARY, and a boundary resets what a section starts from
/// (MeasureCollector.ProcessSectionPrologue — measured, not assumed): the relative frame and
/// the octave mode, the running duration, the meter and key (to the score's, or the new
/// section's own header), the clef (to the part's) and grob overrides (to the part's). So the
/// second half would not sound — or read — as it did in the middle of the old section. Each
/// is put back at the cut: the octave by the converter's walk
/// (<see cref="OctaveModeConverter.Repin"/>, the one that decides; no copy of its rules
/// here), the duration by writing the running value on the first note that relied on it,
/// and the directives by restating the ones in force when the new section would start
/// elsewhere. A <c>q</c> opening a new section repeats a chord the section no longer holds
/// and is refused.
/// </para>
/// </remarks>
public static class SectionSplitter
{
    /// <summary>One section of a subdivision and the bars it writes.</summary>
    public readonly record struct SectionLength(string Name, int Bars);

    /// <summary>A part whose run of sections (the split one, then sections no long part has)
    /// adds up to the length the others still write in one.</summary>
    public sealed record Subdivision(string Part, IReadOnlyList<SectionLength> Sections)
    {
        /// <summary><c>A 16 + B 121 bars</c>.</summary>
        public string Describe() => string.Join(" + ", Sections.Select(s => $"{s.Name} {s.Bars}")) + " bars";

        internal string Key => string.Join(",", Sections.Select(s => $"{s.Name}:{s.Bars}"));
    }

    /// <summary>A section that is not the same length everywhere, its longest length, and the
    /// parts that subdivide it.</summary>
    public sealed record Offer(string Section, int Length, IReadOnlyList<Subdivision> Candidates,
        IReadOnlyList<string>? LongParts = null)
    {
        /// <summary>True when the candidates disagree, so the author picks which to follow.</summary>
        public bool NeedsChoice => Candidates.Select(c => c.Key).Distinct().Count() > 1;
    }

    /// <summary>The rewritten text and its plan, or the reason nothing was done
    /// (<see cref="Error"/>), or — neither — the <see cref="Offer"/> whose candidates the author
    /// must choose between.</summary>
    public sealed record Result(string? NewText, string? Plan, string? Error, Offer? Offer = null,
        string? Reference = null)
    {
        /// <summary>Where the rewrite added (or removed) lines: (its first line in
        /// <see cref="NewText"/>, lines added) — so a later step's refusal, found in the
        /// rewritten text, can name the lines of the file the author has.</summary>
        internal IReadOnlyList<(int Line, int Added)> LineShifts { get; init; } = [];

        /// <summary>What this step cut, for the check over the whole plan.</summary>
        internal StepInfo? Step { get; init; }
    }

    /// <summary>What one step cut: the section and the reference's bars of it, the parts cut,
    /// every part and track cut (by the name a message uses), and the writers of the sections
    /// involved that were NOT cut (by their MIDI part key).</summary>
    internal sealed record StepInfo(string Section, int FirstBars, List<string> CutParts,
        HashSet<string> CutOwners, HashSet<string> Untouched);

    /// <summary>The two stages a test may switch off to show the other one catches it.</summary>
    internal sealed record Options(bool FixOctaves = true, bool Verify = true);

    /// <summary>Every section of a part-major file that some part subdivides — cheap (a parse
    /// and the bar counts; nothing is compiled), for the editor's quick fix.</summary>
    public static IReadOnlyList<Offer> FindOffers(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors || PartSectionRegrouper.Detect(tree.GetRoot()) != Grouping.ByPart)
            return [];
        return new Model(source, tree.GetRoot()).Offers();
    }

    /// <summary>Splits <paramref name="section"/> (null: the first section some part
    /// subdivides) of every other part to match <paramref name="referencePart"/> (null: the
    /// only candidate, or the first of several that agree).</summary>
    public static Result Split(string source, string? section = null, string? referencePart = null)
        => Split(source, section, referencePart, new Options());

    /// <remarks>
    /// ⚠️ ONE SECTION AT A TIME LEFT A LONG PART BEHIND, SILENTLY (owner's report, 2026-09-28,
    /// bohemian-rhapsody.lys). vn1 wrote A..H; vn2, va and vc wrote A + B (B = 121 bars); cb
    /// wrote everything in A (137). Splitting B to follow vn1 cut vn2/va/vc and was applied —
    /// cb has no B, so it was not in B's plan, and the check only asked about B's warning. The
    /// file was left with A 137 bars long in cb, every score laid A out at 137 bars and vn1's
    /// B..H came in after 121 empty bars; the slur cb carries across the C/D boundary, which
    /// would have refused the whole thing, was never looked at. So a split now CONTINUES: while
    /// any section is still split differently and its part to follow is not in question (the
    /// reference itself, or candidates that agree), that section is split too, and the steps
    /// are one plan — refused whole if any step is refused (owner: no partial Apply). Whatever
    /// length warning is still left at the end is named in the plan. Starting from A or from B
    /// therefore lands on the same plan.
    /// </remarks>
    internal static Result Split(string source, string? section, string? referencePart, Options options)
    {
        var first = SplitOnce(source, section, referencePart, options);
        if (first.NewText == null)
            return first;
        string reference = first.Reference!;
        var plans = new List<string> { first.Plan! };
        var steps = new List<StepInfo> { first.Step! };
        string text = first.NewText;
        var shifts = new List<IReadOnlyList<(int Line, int Added)>> { first.LineShifts };
        var done = new HashSet<string>(StringComparer.Ordinal);
        for (int step = 0; step < 32; step++)
        {
            var offers = FindOffers(text);
            var next = offers.FirstOrDefault(o => o.Candidates.Any(c => c.Part == reference))
                ?? offers.FirstOrDefault(o => !o.NeedsChoice);
            if (next == null || !done.Add(next.Section))
                break;
            string? follow = next.Candidates.Any(c => c.Part == reference) ? reference : null;
            var more = SplitOnce(text, next.Section, follow, options);
            if (more.NewText == null)
            {
                if (more.Offer != null)
                    break; // a choice nobody made: left, and named below
                // The step's own reasons, without its headline (this one replaces it).
                var reasons = more.Error!.Split('\n');
                string why = reasons.Length > 1 && reasons[0].StartsWith("Nothing was changed:", StringComparison.Ordinal)
                    ? string.Join("\n", reasons.Skip(1)) : more.Error;
                // Its lines are the rewritten text's: name the author's.
                why = LineNumber.Replace(why, m =>
                {
                    int line = int.Parse(m.Groups[1].Value);
                    for (int s = shifts.Count - 1; s >= 0; s--)
                        foreach (var (at, added) in shifts[s].OrderByDescending(x => x.Line))
                            if (line > at)
                                line = Math.Max(at, line - added);
                    return $"line {line}";
                });
                string whom = follow ?? next.Candidates[0].Part;
                return Fail($"Nothing was changed: after \"{plans[0].Split('\n')[0]}\" {Describe(next)}; "
                    + $"following {whom} it has to be cut too, and that is refused — a split that leaves "
                    + $"it long is not offered.\n{why}");
            }
            plans.Add(more.Plan!);
            shifts.Add(more.LineShifts);
            steps.Add(more.Step!);
            text = more.NewText;
        }

        var plan = new StringBuilder(string.Join("\n", plans));

        // The check, once, over the whole plan against the file as the author has it.
        if (options.Verify)
        {
            var moved = new List<string>();
            var stillDiffer = new List<string>();
            if (Splitter.Verify(SyntaxTree.Parse(source), text, steps, moved, stillDiffer) is { } failure)
                return Fail("Nothing was changed: the split was checked and would change the music:\n" + failure);
            var leftUncut = steps.SelectMany(s => s.Untouched).ToHashSet(StringComparer.Ordinal);
            var owners = steps.SelectMany(s => s.CutOwners).Distinct(StringComparer.Ordinal).ToList();
            var cut = owners.Where(o => !leftUncut.Contains(o)).ToList();
            var kept = owners.Where(leftUncut.Contains).ToList();
            var gone = steps.Select(s => s.Section)
                .Where(s => !stillDiffer.Any(d => d.Contains($"({s} ", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal).ToList();
            plan.Append("\nChecked: "
                + (cut.Count == 0 ? "" : $"{Names(cut)} {(cut.Count == 1 ? "sounds and counts" : "sound and count")} exactly as before, ")
                + (kept.Count == 0 ? "" : $"{Names(kept)} {(kept.Count == 1 ? "keeps" : "keep")} every note {(kept.Count == 1 ? "it" : "they")} played, ")
                + "no error is new"
                + (gone.Count == 0 ? "." : $", and the length warning{(gone.Count > 1 ? "s" : "")} on "
                    + $"{Names(gone)} {(gone.Count > 1 ? "are" : "is")} gone."));
            if (moved.Count > 0)
                plan.Append($"\nHeard at other times now that the sections are no longer padded to the "
                    + $"longest part (their notes are not changed): {Names(moved)}.");
        }

        // Anything still not the same length is said, never left to be found.
        foreach (var d in SemanticValidation.Run(SyntaxTree.Parse(text))
                     .Where(d => d.Code == DiagnosticCodes.SectionBarCountMismatch))
            plan.Append("\nStill not the same length: ")
                .Append(d.Message[..(d.Message.IndexOf(". It is laid out", StringComparison.Ordinal) is var cut and > 0 ? cut : d.Message.Length)])
                .Append(" — no part subdivides it the same way; nothing here splits it.");
        return new Result(text, plan.ToString(), null, null, reference);
    }

    private static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    /// <summary><c>cb still holds A 137 bars long</c> — who writes an offer's section whole.</summary>
    private static string Describe(Offer offer)
    {
        var who = offer.LongParts ?? [];
        string names = who.Count switch
        {
            0 => "a part",
            1 => who[0],
            _ => $"{string.Join(", ", who.Take(who.Count - 1))} and {who[^1]}",
        };
        return $"{names} still hold{(who.Count == 1 ? "s" : "")} section {offer.Section} {offer.Length} bars long";
    }

    private static readonly Regex LineNumber = new(@"line (\d+)", RegexOptions.Compiled);

    private static Result SplitOnce(string source, string? section, string? referencePart, Options options)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return Fail("Fix the syntax errors before splitting sections — no changes made.");
        var root = tree.GetRoot();
        if (root.DescendantNodes<UsingDirectiveSyntax>().Any())
            return Fail("This file includes another with 'using', whose music is not in this file — "
                + "no changes made.");
        switch (PartSectionRegrouper.Detect(root))
        {
            case Grouping.BySection:
                return Fail("Splitting sections to match a part is not supported yet for a file grouped "
                    + "by section. Regroup it by part first (Lily#: Regroup), split, and regroup back.");
            case Grouping.Unknown:
                return Fail("Nothing to split — the file needs parts with sections (part p { section A { … } }).");
        }

        var model = new Model(source, root);
        var offers = model.Offers();
        // A section named but no longer split differently (the quick fix of a warning another
        // step has since settled) falls back to whatever is: the plan is the same either way.
        Offer? offer = (section == null ? null : offers.FirstOrDefault(o => o.Section == section))
            ?? offers.FirstOrDefault();
        if (offer == null)
            return Fail("No section is split differently between the parts — nothing to do.");

        Subdivision? reference;
        if (referencePart != null)
        {
            reference = offer.Candidates.FirstOrDefault(c => c.Part == referencePart);
            if (reference == null)
                return Fail($"Part '{referencePart}' does not subdivide section '{offer.Section}'.");
        }
        else if (offer.NeedsChoice)
            return new Result(null, null, null, offer);
        else
            reference = offer.Candidates[0];

        return new Splitter(source, model, offer, reference, options).Run();
    }

    private static Result Fail(string error) => new(null, null, error);

    // --- the file as the splitter sees it ------------------------------------

    /// <summary>What a cell holds: a part's music, a chord row, or a lyrics track.</summary>
    private enum CellKind { Music, Chords, Lyrics }

    /// <summary>One section's worth of one voice: the node whose braces hold it
    /// (<see cref="Body"/>), the section declaration it is written as, and its bars.</summary>
    /// <param name="Owner">How a message names it: <c>vn2</c>, <c>chords prog</c>,
    /// <c>lyrics words</c>.</param>
    /// <param name="Declaration">The <c>section S</c> this cell is (by-part) or sits in (a
    /// top-level section holding track blocks).</param>
    /// <param name="Body">The node whose own braces enclose the cell's items — the section
    /// itself, or the track block inside a top-level section.</param>
    private sealed record Cell(string Owner, string? Part, CellKind Kind,
        SectionDeclarationSyntax Declaration, SyntaxNode Body, int Bars)
    {
        public bool InTopLevelSection => !ReferenceEquals(Declaration, Body);
    }

    private sealed class Model
    {
        public readonly string Source;
        public readonly CompilationUnitSyntax Root;
        public readonly Dictionary<string, SyntaxNode> Phrases = new(StringComparer.Ordinal);
        /// <summary>Every part, in document order, with its section cells in document order.</summary>
        public readonly List<(string Part, PartDeclarationSyntax Node, List<Cell> Cells)> Parts = new();
        /// <summary>Chord and lyrics cells (by-part tracks and top-level sections' blocks).</summary>
        public readonly List<Cell> Tracks = new();

        public Model(string source, CompilationUnitSyntax root)
        {
            Source = source;
            Root = root;
            foreach (var n in root.DescendantNodesOfKinds(PhraseCycleValidator.DeclaringKinds))
            {
                if (n is PhraseDeclarationSyntax ph) Phrases[ph.Name.Text] = ph.Body;
                else if (n is VariableDeclarationSyntax vd) Phrases[vd.Name.Text] = vd.Expression;
            }
            var voices = new Dictionary<SyntaxNode, int>(ReferenceEqualityComparer.Instance);
            foreach (var v in SectionBarCounts.SemanticVoices(root, Phrases))
                voices[v.Container] = v.Bars;
            foreach (var member in root.ChildNodes())
            {
                switch (member)
                {
                    case PartDeclarationSyntax part:
                        var cells = new List<Cell>();
                        foreach (var sec in part.ChildNodes().OfType<SectionDeclarationSyntax>())
                            cells.Add(new Cell(part.Name.Text, part.Name.Text, CellKind.Music, sec, sec,
                                voices.TryGetValue(sec, out int b) ? b : 0));
                        Parts.Add((part.Name.Text, part, cells));
                        break;
                    case ChordPartBlockSyntax { HasSections: true, PartName: { } track } chords:
                        foreach (var sec in chords.Sections)
                            Tracks.Add(new Cell($"chords {track}", null, CellKind.Chords, sec, sec,
                                ChordNameCollector.CountSectionBars(sec)));
                        break;
                    case LyricsBlockSyntax { HasSections: true } lyrics:
                        foreach (var sec in lyrics.Sections)
                            Tracks.Add(new Cell(LyricsLabel(lyrics), null, CellKind.Lyrics, sec, sec,
                                LyricSyllableReader.CountBars(sec)));
                        break;
                    case SectionDeclarationSyntax top:
                        // A top-level section holding track blocks beside a part-major body.
                        foreach (var child in top.ChildNodes())
                        {
                            if (child is ChordPartBlockSyntax { PartName: { } t } cb)
                                Tracks.Add(new Cell($"chords {t}", null, CellKind.Chords, top, cb,
                                    ChordNameCollector.CountBars(cb)));
                            else if (child is LyricsBlockSyntax lb && !lb.HasSections)
                                Tracks.Add(new Cell(LyricsLabel(lb), null, CellKind.Lyrics, top, lb,
                                    LyricSyllableReader.CountBars(lb)));
                        }
                        break;
                }
            }
        }

        private static string LyricsLabel(LyricsBlockSyntax b)
            => b.VoiceName is { } n ? $"lyrics {n}" : "lyrics";

        /// <summary>Every section some part subdivides (see the class remarks for the rule),
        /// in the order the sections are first written.</summary>
        public List<Offer> Offers()
        {
            var offers = new List<Offer>();
            var names = Parts.SelectMany(p => p.Cells).Select(c => c.Declaration.SectionName)
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (var s in names)
            {
                var holding = Parts.Select(p => (p.Part, Cell: p.Cells.FirstOrDefault(c => c.Declaration.SectionName == s)))
                    .Where(p => p.Cell != null).ToList();
                var lengths = holding.Select(h => h.Cell!.Bars)
                    .Concat(Tracks.Where(t => t.Kind == CellKind.Chords && t.Declaration.SectionName == s).Select(t => t.Bars))
                    .ToList();
                if (lengths.Count < 2 || lengths.Distinct().Count() < 2)
                    continue;
                int longest = lengths.Max();
                // The names the long parts write: a subdivision's new sections are none of them.
                var longNames = holding.Where(h => h.Cell!.Bars == longest)
                    .SelectMany(h => Parts.First(p => p.Part == h.Part).Cells)
                    .Select(c => c.Declaration.SectionName).ToHashSet(StringComparer.Ordinal);
                var candidates = new List<Subdivision>();
                foreach (var (part, _, cells) in Parts)
                {
                    int at = cells.FindIndex(c => c.Declaration.SectionName == s);
                    if (at < 0 || cells[at].Bars <= 0 || cells[at].Bars >= longest)
                        continue;
                    var run = new List<SectionLength> { new(s, cells[at].Bars) };
                    int sum = cells[at].Bars;
                    for (int i = at + 1; i < cells.Count && sum < longest; i++)
                    {
                        string n = cells[i].Declaration.SectionName;
                        if (longNames.Contains(n) || run.Any(r => r.Name == n))
                            break;
                        run.Add(new SectionLength(n, cells[i].Bars));
                        sum += cells[i].Bars;
                    }
                    if (sum == longest && run.Count > 1)
                        candidates.Add(new Subdivision(part, run));
                }
                if (candidates.Count > 0)
                    offers.Add(new Offer(s, longest, candidates,
                        holding.Where(h => h.Cell!.Bars == longest).Select(h => h.Part)
                            .Concat(Tracks.Where(t => t.Kind == CellKind.Chords && t.Declaration.SectionName == s
                                && t.Bars == longest).Select(t => t.Owner))
                            .ToList()));
            }
            return offers;
        }
    }

    // --- the split -------------------------------------------------------------

    private sealed class Splitter
    {
        private readonly string _source;
        private readonly Model _model;
        private readonly Offer _offer;
        private readonly Subdivision _reference;
        private readonly Options _options;
        private readonly string _nl;
        private readonly List<Edit> _edits = new();
        private readonly List<string> _problems = new();
        private readonly List<string> _restated = new();
        private int _durations;
        /// <summary>The bar each new section starts after, and its name.</summary>
        private readonly List<(int After, string Name)> _cuts = new();
        /// <summary>Parts that already write the reference's sections: not rewritten, not
        /// compared (their form may now play sections it used to pad over).</summary>
        private readonly HashSet<string> _followers;

        public Splitter(string source, Model model, Offer offer, Subdivision reference, Options options)
        {
            _source = source;
            _model = model;
            _offer = offer;
            _reference = reference;
            _options = options;
            // What the file ends its lines with — a new section's lines end the same way.
            _nl = source.Contains("\r\n") ? "\r\n" : "\n";
            int after = 0;
            for (int i = 0; i + 1 < reference.Sections.Count; i++)
            {
                after += reference.Sections[i].Bars;
                _cuts.Add((after, reference.Sections[i + 1].Name));
            }
            _followers = offer.Candidates.Where(c => c.Key == reference.Key).Select(c => c.Part)
                .ToHashSet(StringComparer.Ordinal);
        }

        private string S => _offer.Section;

        public Result Run()
        {
            // The durations of the reference's bars, cut by cut: a cut in another part must
            // fall at the same point in time, or it is mid-bar there.
            var refDurations = new List<Fraction>();
            var refPart = _model.Parts.First(p => p.Part == _reference.Part);
            var total = Fraction.Zero;
            foreach (var s in _reference.Sections.Take(_reference.Sections.Count - 1))
            {
                var cell = refPart.Cells.First(c => c.Declaration.SectionName == s.Name);
                foreach (var bar in MeasureModel.Split(cell.Body, _model.Phrases, MeterAt(cell.Declaration)))
                    total += bar.Duration;
                refDurations.Add(total);
            }

            var cutParts = new List<string>();
            foreach (var (part, _, cells) in _model.Parts)
            {
                if (_followers.Contains(part))
                    continue;
                var cell = cells.FirstOrDefault(c => c.Declaration.SectionName == S);
                if (cell == null || cell.Bars <= _reference.Sections[0].Bars)
                    continue;
                if (CutCell(cell, refDurations))
                    cutParts.Add(part);
            }
            var cutTracks = new List<string>();
            var topLevel = new Dictionary<SectionDeclarationSyntax, List<(Cell Cell, List<(int Cut, string Name)> Pieces)>>(
                ReferenceEqualityComparer.Instance);
            foreach (var track in _model.Tracks.Where(t => t.Declaration.SectionName == S))
            {
                if (track.Bars <= _reference.Sections[0].Bars)
                    continue;
                if (track.Kind == CellKind.Lyrics && track.Bars > _offer.Length)
                {
                    _problems.Add($"{track.Owner}, section {S}: it writes {track.Bars} bars against the "
                        + $"section's {_offer.Length} (stacked verses) — split it by hand.");
                    continue;
                }
                if (track.InTopLevelSection)
                {
                    if (FindCuts(track, null) is { } pieces)
                    {
                        if (!topLevel.TryGetValue(track.Declaration, out var list))
                            topLevel[track.Declaration] = list = new();
                        list.Add((track, pieces));
                        cutTracks.Add(track.Owner);
                    }
                }
                else if (CutCell(track, null))
                    cutTracks.Add(track.Owner);
            }
            foreach (var (decl, blocks) in topLevel)
                MoveTrackPieces(decl, blocks);

            var formChanges = FormEdits();

            if (_problems.Count > 0)
                return Fail($"Nothing was changed: {Describe(_offer)}, and splitting it to follow "
                    + $"{_reference.Part} is refused:\n" + string.Join("\n", _problems.Select(p => "• " + p)));
            if (cutParts.Count == 0 && cutTracks.Count == 0)
                return Fail($"No other part writes section '{S}' longer than {_reference.Part} does — nothing to split.");

            string candidate = Apply(_source, _edits);
            if (SyntaxTree.Parse(candidate).HasErrors)
                return Fail("The split would not parse — no changes made. (Please report this file.)");

            int remarked = 0;
            if (_options.FixOctaves)
            {
                var repinned = OctaveModeConverter.Repin(_source, candidate);
                if (repinned.NewText == null)
                    return Fail(repinned.Error!);
                candidate = repinned.NewText;
                remarked = repinned.ChangedPitches;
            }

            // Who must sound exactly as before: every part and chord row that was cut, and every
            // one that writes none of the sections involved. A part that writes S or a new
            // section and was NOT cut (the reference, a part that already follows it, one whose
            // S is short) is not rewritten — but its later sections can come in at another time,
            // because S no longer lasts as long as its longest part did. That is the point of the
            // split, so it is said in the plan rather than refused.
            var involved = _reference.Sections.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            var untouched = _model.Parts
                .Where(p => !cutParts.Contains(p.Part) && p.Cells.Any(c => involved.Contains(c.Declaration.SectionName)))
                .Select(p => p.Part)
                .Concat(_model.Tracks
                    .Where(t => t.Kind == CellKind.Chords && !cutTracks.Contains(t.Owner)
                        && involved.Contains(t.Declaration.SectionName))
                    .Select(t => t.Owner["chords ".Length..] + " (chords)"))
                .ToHashSet(StringComparer.Ordinal);

            // The check is the caller's (Split), over every step at once against the file as the
            // author has it: one step alone need not keep the music — a part the next step cuts
            // can still hold a section long — and the plan is applied whole or not at all.
            var step = new StepInfo(S, _reference.Sections[0].Bars, cutParts,
                cutParts.Concat(cutTracks).ToHashSet(StringComparer.Ordinal), untouched);

            var plan = new StringBuilder();
            plan.Append($"Follow {_reference.Part}: {_reference.Describe()}.");
            string afters = _cuts.Count == 1 ? $"after bar {_cuts[0].After}"
                : $"after bars {string.Join(", ", _cuts.Select(c => c.After))}";
            string into = string.Join(", ", _reference.Sections.Select(s => s.Name));
            if (cutParts.Count > 0)
                plan.Append($"\nSplit {S} in {Names(cutParts)} {afters} → {into}.");
            if (cutTracks.Count > 0)
                plan.Append($"\nAlso split {Names(cutTracks)}.");
            foreach (var change in formChanges)
                plan.Append('\n').Append(change);
            var fixes = new List<string>();
            if (remarked > 0) fixes.Add($"octave marks on {remarked} note(s)");
            if (_durations > 0) fixes.Add($"a written duration on {_durations} note(s)");
            if (_restated.Count > 0) fixes.Add("restated " + string.Join(", ", _restated));
            if (fixes.Count > 0)
                plan.Append($"\nAt the cuts: {string.Join("; ", fixes)}.");
            // Another section still split differently is the caller's next step (Split), and
            // whatever is left after that is named there.
            return new Result(candidate, plan.ToString(), null, null, _reference.Part)
            {
                LineShifts = LineShifts(),
                Step = step,
            };
        }

        /// <summary>The lines the edits add or remove, at their line in the rewritten text (the
        /// octave re-marking changes no line). See <see cref="Result.LineShifts"/>.</summary>
        private List<(int Line, int Added)> LineShifts()
        {
            var shifts = new List<(int, int)>();
            int moved = 0;
            foreach (var e in _edits.Select((e, i) => (e, i)).OrderBy(x => x.e.Start).ThenBy(x => x.i).Select(x => x.e))
            {
                int added = e.Text.Count(ch => ch == '\n') - _source[e.Start..e.End].Count(ch => ch == '\n');
                if (added == 0)
                    continue;
                int line = 1 + _source[..e.Start].Count(ch => ch == '\n');
                shifts.Add((line + moved, added));
                moved += added;
            }
            return shifts;
        }

        private static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";


        // --- cutting a cell ---------------------------------------------------

        /// <summary>Cuts a by-part cell in place: its section closes after each cut and a new
        /// <c>section N {</c> opens. False (with the reason recorded) when a cut cannot be made.</summary>
        private bool CutCell(Cell cell, List<Fraction>? refDurations)
        {
            var pieces = FindCuts(cell, refDurations);
            if (pieces == null)
                return false;
            var (bodyStart, bodyEnd) = BodyRange(cell.Body);
            string secIndent = Indent(cell.Declaration.SectionKeyword.Span.Start);
            string bodyIndent = BodyIndent(cell.Body, secIndent);
            // A section written on one line stays on one line per piece.
            bool oneLine = !_source[bodyStart..bodyEnd].Contains('\n');
            for (int i = 0; i < pieces.Count; i++)
            {
                var (cut, name) = pieces[i];
                int pieceEnd = i + 1 < pieces.Count ? pieces[i + 1].Cut : bodyEnd;
                var carried = cell.Kind == CellKind.Music ? Carry(cell, cut, pieceEnd, name) : new List<string>();
                if (carried == null)
                    return false;
                string open = oneLine ? $" }}{_nl}{secIndent}section {name} {{"
                    : $"{_nl}{secIndent}}}{_nl}{secIndent}section {name} {{";
                if (carried.Count > 0)
                    open += oneLine ? $" {string.Join(" ", carried)}" : $"{_nl}{bodyIndent}{string.Join(" ", carried)}";
                _edits.Add(new Edit(cut, cut, open));
            }
            return true;
        }

        /// <summary>The cut offsets of one cell (each just after the bar line that closes the
        /// reference's bar), paired with the section each opens; null when one cannot be made
        /// (the reason is recorded).</summary>
        private List<(int Cut, string Name)>? FindCuts(Cell cell, List<Fraction>? refDurations)
        {
            var (bodyStart, bodyEnd) = BodyRange(cell.Body);
            var ends = BarEnds(cell.Body);
            var pieces = new List<(int Cut, string Name)>();
            int prev = 0;
            bool ok = true;
            for (int k = 0; k < _cuts.Count; k++)
            {
                var (after, name) = _cuts[k];
                if (after >= cell.Bars)
                    break;
                string where = $"{cell.Owner}, section {S}, after bar {after}";
                // The first bar line whose prefix counts `after` bars (the count only grows).
                int lo = prev, hi = ends.Count - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    int bars = CountPrefix(cell, bodyStart, ends[mid]).Bars;
                    if (bars >= after) { found = mid; hi = mid - 1; }
                    else lo = mid + 1;
                }
                if (found < 0 || CountPrefix(cell, bodyStart, ends[found]).Bars != after
                    || CountPrefix(cell, ends[found], bodyEnd).Bars != cell.Bars - after)
                {
                    _problems.Add($"{where}: no bar line of its own ends that bar — it falls inside a "
                        + "repeat, tuplet, voice, phrase reference or multi-bar rest, or mid-bar.");
                    ok = false;
                    break;
                }
                int cut = ends[found];
                if (refDurations != null && CountPrefix(cell, bodyStart, cut).Duration is var d
                    && d != refDurations[k])
                {
                    _problems.Add($"{where} ({LineOf(cut)}): its first {after} bar(s) last {d} whole "
                        + $"note(s) but {_reference.Part}'s last {refDurations[k]} — the cut would fall mid-bar here.");
                    ok = false;
                    break;
                }
                foreach (var open in OpenAt(cell, bodyStart, cut))
                {
                    _problems.Add($"{where} ({LineOf(cut)}): {open} runs across the cut.");
                    ok = false;
                }
                pieces.Add((cut, name));
                prev = found + 1;
            }
            return ok && pieces.Count > 0 ? pieces : null;
        }

        /// <summary>Bars (and, for music, their total duration) of the cell's items in
        /// [<paramref name="from"/>, <paramref name="to"/>), counted by the cell kind's own
        /// counter over a small file holding just that text.</summary>
        private (int Bars, Fraction Duration) CountPrefix(Cell cell, int from, int to)
        {
            string text = _source[from..to];
            switch (cell.Kind)
            {
                case CellKind.Music:
                {
                    var sec = SyntaxTree.Parse($"part probe {{ section X {{{text}\n}} }}").GetRoot()
                        .DescendantNodes<SectionDeclarationSyntax>().First();
                    var bars = MeasureModel.Split(sec, _model.Phrases, MeterAt(cell.Declaration));
                    var d = Fraction.Zero;
                    foreach (var b in bars) d += b.Duration;
                    return (bars.Count, d);
                }
                case CellKind.Chords:
                {
                    var sec = SyntaxTree.Parse($"chords probe {{ section X {{{text}\n}} }}").GetRoot()
                        .DescendantNodes<SectionDeclarationSyntax>().First();
                    return (ChordNameCollector.CountSectionBars(sec), Fraction.Zero);
                }
                default:
                {
                    var sec = SyntaxTree.Parse($"lyrics probe {{ section X {{{text}\n}} }}").GetRoot()
                        .DescendantNodes<SectionDeclarationSyntax>().First();
                    return (LyricSyllableReader.CountBars(sec), Fraction.Zero);
                }
            }
        }

        /// <summary>The score-level meter in force where <paramref name="declaration"/>'s part
        /// starts — the meter <see cref="SectionBarCounts"/> counts the cell with.</summary>
        private Fraction MeterAt(SyntaxNode declaration)
        {
            var meter = DurationCalculator.ParseTimeSignature(4, 4);
            foreach (var ts in _model.Root.DescendantNodes<TimeSignatureSyntax>())
            {
                if (ts.Span.Start > declaration.Span.Start)
                    break;
                if (!ts.IsSenzaMisura && SectionBarCounts.IsScoreLevel(ts))
                    meter = DurationCalculator.ParseTimeSignature(ts.Beats, ts.BeatType);
            }
            return meter;
        }

        /// <summary>The offsets just after each bar line standing directly in the cell (a
        /// bar-line node, or an item whose last token is one — a lyric measure).</summary>
        private static List<int> BarEnds(SyntaxNode body)
        {
            var ends = new List<int>();
            foreach (var child in BodyItems(body))
            {
                var last = LastToken(child);
                if (last != null && last.Text.Contains('|'))
                    ends.Add(last.Span.End);
            }
            return ends;
        }

        // --- what a cut must not cross ------------------------------------------

        private static readonly Regex AnnotationAt = new(@"\G@(!?)([A-Za-z]+)", RegexOptions.Compiled);
        private static readonly HashSet<string> DynamicNames = new(StringComparer.Ordinal)
            { "ppppp", "pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff", "fffff",
              "sfz", "sf", "sff", "sfp", "sp", "spp", "fp", "rfz", "rf", "fz" };
        private static readonly Dictionary<string, string> SpanFamilies = new(StringComparer.Ordinal)
        {
            ["cresc"] = "hairpin", ["decresc"] = "hairpin", ["dim"] = "hairpin",
            ["textSpan"] = "text spanner", ["rit"] = "text spanner", ["accel"] = "text spanner",
            ["rall"] = "text spanner",
            ["ottava"] = "ottava", ["quindicesima"] = "ottava",
            ["startTrillSpan"] = "trill span", ["stopTrillSpan"] = "trill span",
            ["sustain"] = "sustain pedal", ["sostenuto"] = "sostenuto pedal",
            ["unaCorda"] = "una corda pedal", ["treCorde"] = "una corda pedal",
            ["phrasingSlur"] = "phrasing slur",
        };

        /// <summary>What is still open at <paramref name="cut"/> among the cell's items before
        /// it: a tie, a slur, a manual beam, and the <c>@</c> spans (a hairpin stays open until
        /// the next dynamic, a span until its <c>@!</c> end).</summary>
        private List<string> OpenAt(Cell cell, int bodyStart, int cut)
        {
            var open = new List<string>();
            var nodes = cell.Body.DescendantNodes()
                .Where(n => n.Span.Start >= bodyStart && n.Span.Start < cut).ToList();

            int lastOnset = nodes.Where(n => n is NoteSyntax or ChordSyntax or RestSyntax or ChordRepetitionSyntax)
                .Select(n => n.Span.Start).DefaultIfEmpty(-1).Max();
            if (nodes.OfType<TieSyntax>().Select(t => t.Span.Start).DefaultIfEmpty(-1).Max() is var tie
                && tie > lastOnset)
                open.Add($"a tie ({LineOf(tie)})");

            int slurs = 0, beams = 0, slurAt = -1, beamAt = -1;
            foreach (var n in nodes.OrderBy(n => n.Span.Start))
            {
                if (n is SlurSyntax slur)
                {
                    if (slur.IsOpen) { if (slurs++ == 0) slurAt = n.Span.Start; }
                    else if (slurs > 0) slurs--;
                }
                else if (n is BeamMarkerSyntax beam)
                {
                    if (beam.IsStart) { if (beams++ == 0) beamAt = n.Span.Start; }
                    else if (beams > 0) beams--;
                }
            }
            if (slurs > 0) open.Add($"a slur (opened {LineOf(slurAt)})");
            if (beams > 0) open.Add($"a manual beam (opened {LineOf(beamAt)})");

            // The @ marks, read off the text at each '@' token: '@name' opens (or, for a
            // dynamic, closes a hairpin), '@!name' ends.
            var spans = new Dictionary<string, (string Name, int At)>(StringComparer.Ordinal);
            foreach (var t in nodes.OfType<SyntaxTokenNode>().Where(t => t.Text.StartsWith('@'))
                         .OrderBy(t => t.Span.Start))
            {
                var m = AnnotationAt.Match(_source, t.Span.Start);
                if (!m.Success)
                    continue;
                bool end = m.Groups[1].Value == "!";
                string name = m.Groups[2].Value;
                if (!end && DynamicNames.Contains(name))
                {
                    spans.Remove("hairpin");
                    continue;
                }
                if (!SpanFamilies.TryGetValue(name, out var family))
                    continue;
                if (end || name is "stopTrillSpan" or "treCorde")
                    spans.Remove(family);
                else
                    spans[family] = (name, t.Span.Start);
            }
            foreach (var (family, (name, at)) in spans.OrderBy(s => s.Value.At))
                open.Add(family == "hairpin"
                    ? $"a hairpin (@{name} {LineOf(at)}, with no dynamic before the cut)"
                    : $"a {family} (@{name} {LineOf(at)})");

            // A lyric word whose hyphen or extender reaches over the bar line.
            if (cell.Kind == CellKind.Lyrics
                && nodes.OfType<SyntaxTokenNode>().Where(t => !t.Text.Contains('|'))
                    .OrderBy(t => t.Span.Start).LastOrDefault() is { } word
                && (word.Text.EndsWith('-') || word.Text == "__"))
                open.Add($"a lyric word ('{word.Text}' {LineOf(word.Span.Start)})");
            if (cell.Kind == CellKind.Lyrics && nodes.OfType<SyntaxTokenNode>().Any(t => t.Kind == SyntaxKind.OpenBracket))
                open.Add("a verse bracket [N. …]");
            return open;
        }

        // --- what a new section starts from ------------------------------------

        /// <summary>The directives to restate at the head of the new section
        /// <paramref name="name"/> opening at <paramref name="cut"/>, and the running duration
        /// written on its first note (an edit). Null when the piece cannot open cleanly.</summary>
        private List<string>? Carry(Cell cell, int cut, int pieceEnd, string name)
        {
            var (bodyStart, _) = BodyRange(cell.Body);
            // In SOURCE order: the tree hoists a post-event ahead of the mark written before it
            // (PartSectionRegrouper.Verbatim's remark), so pre-order is not quite text order.
            var before = cell.Body.DescendantNodes()
                .Where(n => n.Span.Start >= bodyStart && n.Span.Start < cut)
                .OrderBy(n => n.Span.Start).ToList();
            var piece = cell.Body.DescendantNodes()
                .Where(n => n.Span.Start >= cut && n.Span.Start < pieceEnd)
                .OrderBy(n => n.Span.Start).ToList();
            var firstTimed = piece.FirstOrDefault(IsTimedLeaf);
            int headEnd = firstTimed?.Span.Start ?? pieceEnd;
            bool Restates<T>() where T : SyntaxNode
                => piece.Any(n => n is T && n.Span.Start < headEnd);

            var carried = new List<string>();
            void CarryLast<T>(string? startsWith) where T : SyntaxNode
            {
                var last = before.OfType<T>().LastOrDefault();
                string? inForce = last != null ? InkText(last) : null;
                if (inForce == null || Restates<T>() || (startsWith != null && Normalize(inForce) == Normalize(startsWith)))
                    return;
                carried.Add(inForce);
                _restated.Add($"{inForce} ({cell.Owner} {name})");
            }
            // A top-level `section X { key … time … }` states X's opening meter / key for every
            // part (a standalone header, or one beside track blocks) — the first one written,
            // as the exporters register it; otherwise the score's applies.
            string? Header<T>(string section) where T : SyntaxNode
                => _model.Root.ChildNodes().OfType<SectionDeclarationSyntax>()
                    .Where(h => h.SectionName == section)
                    .Select(h => h.ChildNodes().OfType<T>().FirstOrDefault())
                    .FirstOrDefault(d => d != null) is { } d ? InkText(d) : null;

            // Meter and key in force at the cut: the last one written in the cell before it,
            // else the one the old section opened with (its header, else the score's); and the
            // one the new section would open with (its own header, else the score's).
            void CarryScoped<T>(string fallback) where T : SyntaxNode
            {
                var last = before.OfType<T>().LastOrDefault();
                string inForce = last != null ? InkText(last)
                    : Header<T>(S) ?? ScoreLevel<T>() ?? fallback;
                string starts = Header<T>(name) ?? ScoreLevel<T>() ?? fallback;
                if (Restates<T>() || Normalize(inForce) == Normalize(starts))
                    return;
                carried.Add(inForce);
                _restated.Add($"{inForce} ({cell.Owner} {name})");
            }
            CarryScoped<TimeSignatureSyntax>("time 4/4");
            CarryScoped<KeySignatureSyntax>("key c major");
            // The clef reverts to the part's own; the octave mode to the file's.
            var partClef = _model.Parts.First(p => p.Part == cell.Part).Node.ChildNodes()
                .OfType<ClefDeclarationSyntax>().LastOrDefault();
            CarryLast<ClefDeclarationSyntax>(partClef != null ? InkText(partClef) : null);
            var fileMode = _model.Root.ChildNodes().OfType<OctaveDirectiveSyntax>().LastOrDefault();
            CarryLast<OctaveDirectiveSyntax>(fileMode != null ? InkText(fileMode) : "octave relative");
            // Grob overrides still standing (set in the cell, not reverted) — the boundary
            // puts the part's defaults back.
            var standing = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in before)
            {
                if (n is OverrideDeclarationSyntax ov && ov.Parent is not OnceModifierSyntax)
                    standing[$"{ov.GrobName.Text}.{ov.PropertyName.Text}"] = InkText(ov);
                else if (n is RevertDeclarationSyntax rv && rv.Parent is not OnceModifierSyntax)
                    standing.Remove($"{rv.GrobName.Text}.{rv.PropertyName.Text}");
            }
            foreach (var ov in standing.Values)
            {
                carried.Add(ov);
                _restated.Add($"{ov} ({cell.Owner} {name})");
            }

            // The running duration, onto the first note that relied on it.
            if (firstTimed is ChordRepetitionSyntax)
            {
                _problems.Add($"{cell.Owner}, section {S} ({LineOf(firstTimed.Span.Start)}): the new section "
                    + $"{name} would open with 'q', which repeats a chord written before the cut.");
                return null;
            }
            if (firstTimed != null && DurationSlot(firstTimed) is { } slot)
            {
                string? running = before.Where(n => IsTimedLeaf(n) && !n.IsInside<GraceExpressionSyntax>())
                    .Select(DurationText).LastOrDefault(t => t != null);
                if (running != null && running != "4")
                {
                    _edits.Add(new Edit(slot, slot, running));
                    _durations++;
                }
            }
            return carried;
        }

        private string? ScoreLevel<T>() where T : SyntaxNode
            => _model.Root.ChildNodes().OfType<T>().LastOrDefault() is { } n ? InkText(n) : null;

        private static string Normalize(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

        private static bool IsTimedLeaf(SyntaxNode n)
            => n is NoteSyntax or RestSyntax or ChordSyntax or ChordRepetitionSyntax
                || (n is VariableReferenceSyntax && n.Parent is not VariableReferenceSyntax);

        /// <summary>Where a duration goes on a timed item that writes none (after the pitch and
        /// its marks / the rest letter / the chord's closing bracket and marks); null when it
        /// writes one, or is a phrase reference (which opens its own frame).</summary>
        private static int? DurationSlot(SyntaxNode n) => n switch
        {
            NoteSyntax note => note.Duration is null ? InkEnd(note.Pitch) : null,
            RestSyntax rest => rest.Duration is null ? ((SyntaxTokenNode)rest.GetChild(0)!).Span.End : null,
            ChordSyntax chord => chord.Duration is null ? ChordDurationSlot(chord) : null,
            _ => null,
        };

        private static int ChordDurationSlot(ChordSyntax chord)
        {
            int at = chord.Span.End;
            for (int i = 0; i < chord.SlotCount; i++)
                if (chord.GetChild(i) is SyntaxTokenNode t
                    && t.Kind is SyntaxKind.CloseAngle or SyntaxKind.Apostrophe or SyntaxKind.Comma)
                    at = t.Span.End;
            return at;
        }

        private string? DurationText(SyntaxNode node)
        {
            var d = node switch
            {
                NoteSyntax n => n.Duration,
                RestSyntax r => r.Duration,
                ChordSyntax c => c.Duration,
                ChordRepetitionSyntax q => q.Duration,
                _ => null,
            };
            return d is null ? null : InkText(d);
        }

        // --- chord rows and lyrics in a top-level section ------------------------

        /// <summary>A top-level <c>section S { chords t { … } lyrics w { … } }</c>: each block
        /// keeps its bars up to the first cut, and the rest moves into new top-level
        /// declarations <c>section N { chords t { … } }</c> written after this one.</summary>
        private void MoveTrackPieces(SectionDeclarationSyntax decl,
            List<(Cell Cell, List<(int Cut, string Name)> Pieces)> blocks)
        {
            string indent = Indent(decl.SectionKeyword.Span.Start);
            var bySection = new List<(string Name, StringBuilder Text)>();
            foreach (var (_, name) in _cuts)
                bySection.Add((name, new StringBuilder()));
            foreach (var (cell, pieces) in blocks)
            {
                var (_, bodyEnd) = BodyRange(cell.Body);
                int openEnd = OpenBraceEnd(cell.Body);
                string head = _source[InkStart(cell.Body)..openEnd];
                _edits.Add(new Edit(pieces[0].Cut, bodyEnd, " "));
                for (int i = 0; i < pieces.Count; i++)
                {
                    int end = i + 1 < pieces.Count ? pieces[i + 1].Cut : bodyEnd;
                    var target = bySection.First(s => s.Name == pieces[i].Name).Text;
                    target.Append($"{_nl}{indent}  {head}{_source[pieces[i].Cut..end].TrimEnd()} }}");
                }
            }
            var text = new StringBuilder();
            foreach (var (name, body) in bySection)
                if (body.Length > 0)
                    text.Append($"{_nl}{indent}section {name} {{{body}{_nl}{indent}}}");
            int at = InkEnd(decl);
            _edits.Add(new Edit(at, at, text.ToString()));
        }

        // --- the forms --------------------------------------------------------

        /// <summary>Every play of S in every form becomes S and the new sections, in place — the
        /// marks and the '~' of the play carried onto each. A play already followed by them is
        /// left alone; S as a repeat ending, or a form that already names a new section
        /// somewhere else, is refused.</summary>
        private List<string> FormEdits()
        {
            var changes = new List<string>();
            var newNames = _reference.Sections.Skip(1).Select(s => s.Name).ToList();
            foreach (var form in _model.Root.DescendantNodes<FormDeclarationSyntax>())
            {
                string label = form.NameText.Length > 0 ? $"Form {form.NameText}" : "The form";
                var items = form.DescendantNodes()
                    .Where(n => n is SectionReferenceSyntax or FormAlternativeSyntax
                        || n.Kind == SyntaxKind.SilentSectionReference)
                    .ToList();
                var handled = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
                int edited = 0, already = 0;
                foreach (var item in items)
                {
                    if (item is FormAlternativeSyntax alt)
                    {
                        if (alt.SectionName.Text == S)
                            _problems.Add($"{label.ToLowerInvariant()} plays {S} as a repeat ending "
                                + $"({InkText(alt)}); an ending holds one section, so it cannot become "
                                + $"{S} {string.Join(" ", newNames)} — split the form by hand.");
                        else if (newNames.Contains(alt.SectionName.Text))
                            _problems.Add($"{label.ToLowerInvariant()} already plays {alt.SectionName.Text} "
                                + "as a repeat ending — fix the form by hand.");
                        continue;
                    }
                    if (handled.Contains(item) || SectionSymbols.ReferencedName(item)?.Text != S)
                        continue;
                    // The plays right after this one, in the same list.
                    var following = Siblings(item).Where(n => SectionSymbols.ReferencedName(n) != null)
                        .Take(newNames.Count).ToList();
                    var followingNames = following.Select(n => SectionSymbols.ReferencedName(n)!.Text).ToList();
                    if (followingNames.SequenceEqual(newNames))
                    {
                        foreach (var f in following) handled.Add(f);
                        already++;
                        continue;
                    }
                    bool silent = item.Kind == SyntaxKind.SilentSectionReference;
                    string marks = MarkText(SyntaxFacts.NetOctaveMarks(item));
                    string insert = string.Concat(newNames.Select(n => $" {(silent ? "~" : "")}{n}{marks}"));
                    int at = InkEnd(item);
                    _edits.Add(new Edit(at, at, insert));
                    edited++;
                }
                // A new name played anywhere but right after S.
                foreach (var item in items)
                    if (!handled.Contains(item) && item is not FormAlternativeSyntax
                        && SectionSymbols.ReferencedName(item)?.Text is { } n && newNames.Contains(n))
                    {
                        _problems.Add($"{label.ToLowerInvariant()} already plays {n} somewhere other than "
                            + $"right after {S} — fix the form by hand.");
                        break;
                    }
                if (edited > 0)
                    changes.Add($"{label}: {S} → {S} {string.Join(" ", newNames)}"
                        + (edited > 1 ? $" ({edited} places)." : "."));
                else if (already > 0)
                    changes.Add($"{label} already plays {S} {string.Join(" ", newNames)}.");
            }
            return changes;
        }

        private static IEnumerable<SyntaxNode> Siblings(SyntaxNode node)
        {
            var parent = node.Parent!;
            bool after = false;
            for (int i = 0; i < parent.SlotCount; i++)
            {
                if (parent.GetChild(i) is not SyntaxNode c || c is SyntaxTokenNode)
                    continue;
                if (ReferenceEquals(c, node)) { after = true; continue; }
                if (after) yield return c;
            }
        }

        private static string MarkText(int marks)
            => marks >= 0 ? new string('\'', marks) : new string(',', -marks);

        // --- the check ----------------------------------------------------------

        /// <summary>Null when <paramref name="candidate"/> — every step applied to
        /// <paramref name="original"/> — is what the plan claims; otherwise what differs.</summary>
        /// <param name="moved">Filled with the parts not cut whose timing changed.</param>
        /// <param name="stillDiffer">Filled with the voices of a split section the plan leaves at
        /// another length (a part it does not touch).</param>
        internal static string? Verify(SyntaxTree original, string candidate, List<StepInfo> steps,
            List<string> moved, List<string> stillDiffer)
        {
        var after = SyntaxTree.Parse(candidate);
        if (after.HasErrors)
            return "• it would not parse.";
        var problems = new List<string>();
        var cutOwners = steps.SelectMany(s => s.CutOwners).ToHashSet(StringComparer.Ordinal);
        // A part left uncut by some step (it wrote that step's sections already) may come in at
        // other times — its section no longer waits for the longest part, and the form may play
        // a section it did not before. If another step cut it, what it played must still be
        // there, note for note (pitch and length), wherever it now falls. A part only ever cut
        // sounds exactly as before; so does every part the plan does not involve.
        var leftUncut = steps.SelectMany(s => s.Untouched).ToHashSet(StringComparer.Ordinal);
        var cutParts = steps.SelectMany(s => s.CutParts).Distinct(StringComparer.Ordinal)
            .Where(p => !leftUncut.Contains(p)).ToList();
        var alsoCut = steps.SelectMany(s => s.CutParts).Where(leftUncut.Contains).ToHashSet(StringComparer.Ordinal);

        // Diagnostics: the warning on each split section goes, and no error is new. It may stay
        // only for a part the split does not touch — another that subdivides the section its
        // own way, or one shorter than the cut — and then the plan says whose it is.
        var before = Diagnostics(original);
        var now = Diagnostics(after);
        Model? model = null;
        foreach (var step in steps)
        {
            string S = step.Section;
            if (now.FirstOrDefault(d => d.Code == DiagnosticCodes.SectionBarCountMismatch
                    && d.Message.StartsWith($"Section '{S}' ", StringComparison.Ordinal)) is not { } still)
                continue;
            model ??= new Model(candidate, after.GetRoot());
            var odd = model.Parts.SelectMany(p => p.Cells).Concat(model.Tracks)
                .Where(c => c.Declaration.SectionName == S && c.Kind != CellKind.Lyrics && c.Bars != step.FirstBars)
                .ToList();
            if (odd.Count == 0 || odd.Any(c => cutOwners.Contains(c.Owner)))
                problems.Add($"• section {S} would still not be the same length everywhere: {still.Message}");
            else
                stillDiffer.AddRange(odd.Select(c => $"{c.Owner} ({S} {c.Bars} bar{(c.Bars == 1 ? "" : "s")})"));
        }
            var oldErrors = before.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Code + d.Message).ToList();
            foreach (var d in now.Where(d => d.Severity == DiagnosticSeverity.Error))
                if (!oldErrors.Remove(d.Code + d.Message))
                    problems.Add($"• a new error: {d.Code} {d.Message}");

            // The sound, part by part, for every form; and the bars each part writes.
            var formsBefore = original.GetRoot().DescendantNodes<FormDeclarationSyntax>().ToList();
            var formsAfter = after.GetRoot().DescendantNodes<FormDeclarationSyntax>().ToList();
            if (formsBefore.Count != formsAfter.Count)
                return "• the forms changed in number.";
            for (int f = 0; f < Math.Max(1, formsBefore.Count); f++)
            {
                var fb = formsBefore.Count > 0 ? formsBefore[f] : null;
                var fa = formsAfter.Count > 0 ? formsAfter[f] : null;
                string label = fb == null ? "" : $" (form {fb.NameText})";
                var sb = Sound(original, fb);
                var sa = Sound(after, fa);
                foreach (var part in sb.Keys.Union(sa.Keys).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var x = sb.GetValueOrDefault(part) ?? new List<(int, int, int, int)>();
                    var y = sa.GetValueOrDefault(part) ?? new List<(int, int, int, int)>();
                    if (FirstDifference(x, y) is not { } diff)
                        continue;
                    if (alsoCut.Contains(part) && LostNote(x, y) is { } lost)
                        problems.Add($"• part {part}{label}: {lost}");
                    else if (!leftUncut.Contains(part))
                        problems.Add($"• part {part}{label}: {diff}");
                    else if (!moved.Contains(part))
                        moved.Add(part);
                }
                var lb = Lyrics(original, fb);
                var la = Lyrics(after, fa);
                foreach (var track in lb.Keys.Union(la.Keys))
                    if (!(lb.GetValueOrDefault(track) ?? "").Equals(la.GetValueOrDefault(track) ?? "", StringComparison.Ordinal))
                        problems.Add($"• the words of {track}{label} would fall on other notes.");
                var bb = WrittenBars(original.GetRoot(), fb);
                var ba = WrittenBars(after.GetRoot(), fa);
                foreach (var part in cutParts.Where(p => bb.GetValueOrDefault(p) != ba.GetValueOrDefault(p)))
                    problems.Add($"• part {part}{label} would write {ba.GetValueOrDefault(part)} bars "
                        + $"instead of {bb.GetValueOrDefault(part)}.");
            }
            return problems.Count == 0 ? null : string.Join("\n", problems.Take(8))
                + (problems.Count > 8 ? $"\n• … and {problems.Count - 8} more" : "");
        }

        private static List<Diagnostic> Diagnostics(SyntaxTree tree)
        {
            var list = tree.Diagnostics.ToList();
            try { list.AddRange(SemanticValidation.Run(tree)); }
            catch { /* a validator that throws is compared as silent on both sides */ }
            return list;
        }

        /// <summary>Each part's sounding notes (start, key, length, bend), sorted.</summary>
        private static Dictionary<string, List<(int, int, int, int)>> Sound(SyntaxTree tree, FormDeclarationSyntax? form)
        {
            var result = new Dictionary<string, List<(int, int, int, int)>>(StringComparer.Ordinal);
            var midi = new MidiExporter { Form = form }.Export(tree);
            foreach (var track in midi.Tracks)
                foreach (var n in track.Notes)
                {
                    string part = n.Part ?? "(no part)";
                    if (!result.TryGetValue(part, out var list))
                        result[part] = list = new();
                    list.Add((n.StartTick, n.Pitch, n.DurationTicks, n.QuarterBend));
                }
            foreach (var list in result.Values)
                list.Sort();
            return result;
        }

        private static Dictionary<string, string> Lyrics(SyntaxTree tree, FormDeclarationSyntax? form)
        {
            var midi = new MidiExporter { Form = form }.Export(tree);
            return midi.Tracks.Where(t => t.Lyrics.Count > 0)
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => string.Join("|", g.SelectMany(t => t.Lyrics)
                    .OrderBy(l => l.Tick).Select(l => $"{l.Tick}:{l.Text}")), StringComparer.Ordinal);
        }

        /// <summary>A note (key, length) the part played before and plays no more — the check for
        /// a part whose timing may move; null when every one is still played.</summary>
        private static string? LostNote(List<(int Start, int Key, int Length, int Bend)> before,
            List<(int Start, int Key, int Length, int Bend)> after)
        {
            var left = after.GroupBy(n => (n.Key, n.Length, n.Bend)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var n in before)
            {
                var key = (n.Key, n.Length, n.Bend);
                if (left.TryGetValue(key, out int count) && count > 0)
                    left[key] = count - 1;
                else
                    return $"the note at tick {n.Start} (key {n.Key}, {n.Length} ticks) would be lost or changed.";
            }
            return null;
        }

        private static string? FirstDifference(List<(int Start, int Key, int Length, int Bend)> a,
            List<(int Start, int Key, int Length, int Bend)> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i])
                    return $"the note at tick {a[i].Start} (key {a[i].Key}, {a[i].Length} ticks) would "
                        + $"become tick {b[i].Start} (key {b[i].Key}, {b[i].Length} ticks).";
            return a.Count == b.Count ? null : $"{a.Count} notes would become {b.Count}.";
        }

        /// <summary>The bars each part writes over a play of <paramref name="form"/>: each
        /// section's cell counted as often as the form plays the section (a repeat's body
        /// twice, or its <c>:|*N</c>; an ending once per number it carries).</summary>
        private static Dictionary<string, int> WrittenBars(CompilationUnitSyntax root, FormDeclarationSyntax? form)
        {
            var plays = new Dictionary<string, int>(StringComparer.Ordinal);
            if (form != null)
            {
                foreach (var n in form.DescendantNodes())
                {
                    string? name = n is FormAlternativeSyntax alt ? alt.SectionName.Text
                        : SectionSymbols.ReferencedName(n)?.Text;
                    if (name == null)
                        continue;
                    int times = 1;
                    if (n is FormAlternativeSyntax a2 && a2.HasSeparator && a2.Separator!.Kind == SyntaxKind.Minus
                        && a2.GetChild(3) is SyntaxTokenNode endNo && int.TryParse(endNo.Text, out int last))
                        times = Math.Max(1, last - a2.AlternativeNumber + 1);
                    else if (n is not FormAlternativeSyntax && n.Parent is FormRepeatBlockSyntax block)
                        times = block.DescendantNodes().OfType<BarlineSyntax>().FirstOrDefault(b => b.HasExplicitRepeatCount)
                            ?.RepeatCount ?? 2;
                    plays[name] = plays.GetValueOrDefault(name) + times;
                }
            }
            var bars = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var voice in SectionBarCounts.SemanticVoices(root))
            {
                if (voice.Container is not SectionDeclarationSyntax { Parent: PartDeclarationSyntax part })
                    continue;
                int times = form == null ? 1 : plays.GetValueOrDefault(voice.SectionName);
                bars[part.Name.Text] = bars.GetValueOrDefault(part.Name.Text) + voice.Bars * times;
            }
            return bars;
        }

        // --- text helpers -------------------------------------------------------

        private string LineOf(int pos)
        {
            int line = 1;
            for (int i = 0; i < pos && i < _source.Length; i++)
                if (_source[i] == '\n') line++;
            return $"line {line}";
        }

        private string Indent(int pos)
        {
            int start = pos;
            while (start > 0 && _source[start - 1] != '\n')
                start--;
            string lead = _source[start..pos];
            return lead.All(ch => ch is ' ' or '\t') ? lead : "";
        }

        /// <summary>The indentation of the cell's first item when it starts a line, else the
        /// section's plus two spaces.</summary>
        private string BodyIndent(SyntaxNode body, string secIndent)
        {
            var first = BodyItems(body).FirstOrDefault();
            if (first != null)
            {
                int at = InkStart(first);
                string lead = Indent(at);
                // Indent() answers "" when something else stands before the item on its line.
                if (lead.Length > 0 && at - lead.Length > OpenBraceEnd(body))
                    return lead;
            }
            return secIndent + "  ";
        }

        private string InkText(SyntaxNode node) => _source[InkStart(node)..InkEnd(node)];
    }

    // --- shared tree helpers -------------------------------------------------------

    /// <summary>The items between a node's own braces (its direct children after the first
    /// <c>{</c> and before the matching <c>}</c>).</summary>
    private static IEnumerable<SyntaxNode> BodyItems(SyntaxNode body)
    {
        bool inBody = false;
        for (int i = 0; i < body.SlotCount; i++)
        {
            var child = body.GetChild(i);
            if (child == null)
                continue;
            if (child is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace } && !inBody) { inBody = true; continue; }
            // Nested braces belong to child nodes, so a direct '}' is the body's own.
            if (child is SyntaxTokenNode { Kind: SyntaxKind.CloseBrace } && inBody) break;
            if (inBody)
                yield return child;
        }
    }

    private static int LastSlot(SyntaxNode body)
    {
        for (int i = body.SlotCount - 1; i >= 0; i--)
            if (body.GetChild(i) != null)
                return i;
        return -1;
    }

    /// <summary>[just after the opening brace, the closing brace's start).</summary>
    private static (int Start, int End) BodyRange(SyntaxNode body)
    {
        int start = OpenBraceEnd(body), end = -1;
        for (int i = LastSlot(body); i >= 0; i--)
            if (body.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.CloseBrace } close)
            {
                end = close.Span.Start;
                break;
            }
        return (start, end);
    }

    private static int OpenBraceEnd(SyntaxNode body)
    {
        for (int i = 0; i < body.SlotCount; i++)
            if (body.GetChild(i) is SyntaxTokenNode { Kind: SyntaxKind.OpenBrace } open)
                return open.Span.End;
        return -1;
    }

    private static SyntaxTokenNode? LastToken(SyntaxNode node)
    {
        if (node is SyntaxTokenNode t)
            return t.Width > 0 ? t : null;
        for (int i = node.SlotCount - 1; i >= 0; i--)
            if (node.GetChild(i) is { } child && LastToken(child) is { } last)
                return last;
        return null;
    }

    private static SyntaxTokenNode? FirstToken(SyntaxNode node)
    {
        if (node is SyntaxTokenNode t)
            return t.Width > 0 ? t : null;
        for (int i = 0; i < node.SlotCount; i++)
            if (node.GetChild(i) is { } child && FirstToken(child) is { } first)
                return first;
        return null;
    }

    /// <summary>Where a node's ink starts / ends — its first / last token's own span (a
    /// composite's span carries its children's trivia).</summary>
    private static int InkStart(SyntaxNode node) => FirstToken(node)?.Span.Start ?? node.Span.Start;

    private static int InkEnd(SyntaxNode node) => LastToken(node)?.Span.End ?? node.Span.End;

    private readonly record struct Edit(int Start, int End, string Text);

    /// <summary>Applies non-overlapping edits; insertions at one offset keep the order they
    /// were added in.</summary>
    private static string Apply(string source, List<Edit> edits)
    {
        var sb = new StringBuilder(source.Length + 256);
        int at = 0;
        foreach (var e in edits.Select((e, i) => (e, i)).OrderBy(x => x.e.Start).ThenBy(x => x.i).Select(x => x.e))
        {
            if (e.Start < at)
                continue; // overlapping: the first one wins (never produced on purpose)
            sb.Append(source, at, e.Start - at).Append(e.Text);
            at = e.End;
        }
        sb.Append(source, at, source.Length - at);
        return sb.ToString();
    }
}
