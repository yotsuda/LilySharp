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
using LilySharp.Core.Editing;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Harmony;

/// <summary>
/// Guesses the key of a section from its notes, and writes a chosen key into the section.
/// Powers the "Lily#: Guess Section Key" editor command.
/// </summary>
/// <remarks>
/// <para>
/// LILYSHARP-OWN: an editor aid, not engraving — LilyPond has no key finder. The estimate is
/// the Krumhansl–Schmuckler algorithm: the section's pitch classes, weighted by duration
/// (every part of the section, grace notes count nothing), correlated with the
/// Krumhansl–Kessler major and minor key profiles rotated to each of the 12 tonics. On top
/// of the correlation, a key whose tonic is the section's final bass note gets
/// <see cref="FinalTonicBonus"/> — the profiles alone often cannot tell a key from its
/// relative major/minor, and a tonal section usually ends on its tonic.
/// </para>
/// <para>
/// The tonic is SPELLED from the written notes: of the enharmonic spellings of one tonic
/// (F♯ / G♭ major), the one whose scale agrees with more of the written accidentals wins,
/// then the one with fewer accidentals in its signature.
/// </para>
/// </remarks>
public static class KeyEstimator
{
    /// <summary>One ranked guess: the tonic as the language spells it (<c>fis</c>,
    /// <c>ees</c>), the mode, and the score it ranked by (correlation plus the final-note
    /// bonus; higher is better).</summary>
    public readonly record struct KeyCandidate(string Tonic, bool Major, double Score)
    {
        /// <summary>The key as written after <c>key</c>: <c>d major</c>, <c>fis minor</c>.</summary>
        public string Text => Tonic + (Major ? " major" : " minor");
    }

    /// <summary>A section's guesses, best first, and the key its source states at its
    /// start (null when it states none there).</summary>
    public sealed record SectionKeyEstimate(string Section, string? CurrentKey, IReadOnlyList<KeyCandidate> Candidates)
    {
        /// <summary>The candidates worth offering: those within <see cref="ListMargin"/> of
        /// the best, at most <see cref="ListMax"/> — the best always among them.</summary>
        public IReadOnlyList<KeyCandidate> Likely
            => Candidates.Where((c, i) => i == 0 || c.Score >= Candidates[0].Score - ListMargin)
                .Take(ListMax).ToList();
    }

    /// <summary>How far below the best a key may score and still be offered. MEASURED on
    /// short tonal melodies (2026-10-12): the best scores 0.9–1.0, the keys a writer might
    /// really mean (the parallel or relative key, the subdominant) 0.2–0.3 below it, and the
    /// rest 0.4 or more below.</summary>
    public const double ListMargin = 0.3;

    /// <summary>The most keys offered, however close they score.</summary>
    public const int ListMax = 5;

    /// <summary>The bonus for a key whose tonic is the section's final bass note — about a
    /// tenth of the correlation's range, enough to settle a relative major/minor near-tie
    /// and not enough to overturn a clear profile.</summary>
    public const double FinalTonicBonus = 0.1;

    // Krumhansl & Kessler (1982) probe-tone ratings, tonic first.
    private static readonly double[] MajorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] MinorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    /// <summary>
    /// The names of the sections a selection touches: every section declaration (top-level
    /// or inside a part) and every <c>form</c> reference whose span meets
    /// [<paramref name="start"/>, <paramref name="end"/>], in document order. A caret
    /// (<paramref name="start"/> == <paramref name="end"/>) names the section it stands in.
    /// </summary>
    public static IReadOnlyList<string> SectionsAt(SyntaxNode root, int start, int end)
    {
        var names = new List<string>();
        void Add(string name) { if (!names.Contains(name)) names.Add(name); }

        if (start == end)
        {
            // The innermost declaration or reference holding the caret.
            SyntaxNode? best = null;
            foreach (var n in root.DescendantNodes())
                if ((n is SectionDeclarationSyntax || SectionSymbols.ReferencedName(n) != null)
                    && n.Span.Start <= start && start <= n.Span.End
                    && (best == null || n.Span.Length <= best.Span.Length))
                    best = n;
            if (best != null)
                Add(SectionSymbols.DeclaredName(best)?.Text ?? SectionSymbols.ReferencedName(best)!.Text);
            return names;
        }

        foreach (var n in root.DescendantNodes())
        {
            if (n.Span.End <= start || n.Span.Start >= end)
                continue;
            if (n is SectionDeclarationSyntax s)
            {
                // A selection over a whole part (grouped by part) names every section in it.
                Add(s.SectionName);
            }
            else if (SectionSymbols.ReferencedName(n) is { } r)
            {
                Add(r.Text);
            }
        }
        return names;
    }

    /// <summary>
    /// Estimates the key of <paramref name="sectionName"/> from every part's music in it.
    /// Null when the section has no pitched note.
    /// </summary>
    public static SectionKeyEstimate? Estimate(SyntaxTree tree, string sectionName)
    {
        var root = tree.GetRoot();
        var form = SectionForm(sectionName);
        if (form == null)
            return null;

        var spellings = SpellingsByPosition(root);
        var weight = new double[12];
        var spelled = new List<(int Step, int Alter, double Weight)>();
        (int Midi, int Pc)? finalBass = null;
        Fraction finalEnd = Fraction.Zero;

        foreach (var part in PartsPlaying(root, sectionName))
        {
            var measures = new MeasureCollector().Collect(tree, part, form).Voice.Measures;

            // Sounding MIDI gives every pitch (shapes and degree chords spell no letter);
            // the part's written-minus-sounding offset puts it back in the written key,
            // which is what a `key` states.
            int offset = WrittenOffset(measures, spellings);

            Fraction time = Fraction.Zero;
            (int Midi, int Pc)? lastBass = null;
            Fraction lastEnd = Fraction.Zero;
            foreach (var measure in measures)
            {
                foreach (var item in measure.Items)
                {
                    // A note counts when it spells a pitch: a drum note is a NoteItem too,
                    // with the instrument's MIDI key for a pitch.
                    var midis = item switch
                    {
                        NoteItem n when n.Midi > 0 && spellings.ContainsKey(n.SourcePosition) => new[] { n.Midi },
                        ChordItem c => c.Notes.Select(x => x.Midi).Where(m => m > 0).ToArray(),
                        _ => Array.Empty<int>(),
                    };
                    var d = item.Duration;
                    time += d;
                    if (midis.Length == 0 || d == Fraction.Zero)
                        continue;
                    double w = d.ToDouble();
                    foreach (int m in midis)
                        weight[Mod12(m + offset)] += w;
                    if (spellings.TryGetValue(item.SourcePosition, out var sp))
                        foreach (var (step, alter) in sp)
                            spelled.Add((step, alter, w / sp.Count));
                    int low = midis.Min();
                    lastBass = (low, Mod12(low + offset));
                    lastEnd = time;
                }
            }

            // The section's final bass: the lowest of the notes that sound last.
            if (lastBass is { } lb && (finalBass == null || lastEnd > finalEnd
                || (lastEnd == finalEnd && lb.Midi < finalBass.Value.Midi)))
            {
                finalBass = lb;
                finalEnd = lastEnd;
            }
        }

        if (weight.Sum() <= 0)
            return null;

        var candidates = new List<KeyCandidate>(24);
        for (int tonic = 0; tonic < 12; tonic++)
        {
            foreach (bool major in new[] { true, false })
            {
                var profile = major ? MajorProfile : MinorProfile;
                double r = Correlation(weight, i => profile[Mod12(i - tonic)]);
                if (finalBass?.Pc == tonic)
                    r += FinalTonicBonus;
                candidates.Add(new KeyCandidate(SpellTonic(tonic, major, spelled), major, r));
            }
        }
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        return new SectionKeyEstimate(sectionName, CurrentKey(root, sectionName), candidates);
    }

    /// <summary>
    /// Writes <c>key <paramref name="keyText"/></c> (<c>d major</c>) as the key
    /// <paramref name="sectionName"/> starts in, and returns the new source — or null when
    /// the result would not parse cleanly.
    /// </summary>
    /// <remarks>
    /// Every key the section states at its start (in its header, or before the first note of
    /// a part's music in it) is rewritten, so none is left contradicting the choice. With
    /// none, the key goes into the top-level declaration's header; a file grouped by part
    /// with no top-level declaration of the section gets a standalone header
    /// <c>section NAME { key … }</c> after the last part that plays it (GRAMMAR.md's
    /// SectionSetting note).
    /// </remarks>
    public static string? SetSectionKey(string source, string sectionName, string keyText)
    {
        var tree = SyntaxTree.Parse(source);
        var root = tree.GetRoot();
        var edits = new List<(int Start, int End, string Text)>();

        foreach (var key in StartingKeys(root, sectionName))
        {
            int from = (key.ForceMark ?? key.KeyKeyword).Span.End;
            edits.Add((from, key.Span.End, " " + keyText));
        }

        if (edits.Count == 0)
        {
            var topLevel = root.DescendantNodes<SectionDeclarationSyntax>()
                .FirstOrDefault(s => s.SectionName == sectionName && IsTopLevel(s));
            if (topLevel != null)
            {
                if (DeclarationBody.BraceSpan(topLevel) is not { } braces)
                    return null;
                int open = braces.Start + 1;
                int next = open;
                while (next < source.Length && source[next] is ' ' or '\t') next++;
                if (next < source.Length && source[next] is '\r' or '\n')
                {
                    // The body opens on its own line: the key gets a line of its own,
                    // indented like the first line of the body.
                    int lineStart = source.IndexOf('\n', open) + 1;
                    int indentEnd = lineStart;
                    while (indentEnd < source.Length && source[indentEnd] is ' ' or '\t') indentEnd++;
                    string indent = source[lineStart..indentEnd];
                    if (indentEnd >= source.Length || source[indentEnd] == '}')
                        indent += "  ";
                    string newline = next < source.Length && source[next] == '\r' ? "\r\n" : "\n";
                    edits.Add((open, open, newline + indent + "key " + keyText));
                }
                else
                {
                    edits.Add((open, open, " key " + keyText + (source[next] == '}' ? " " : "")));
                }
            }
            else
            {
                var lastPart = root.DescendantNodes<PartDeclarationSyntax>()
                    .LastOrDefault(p => p.DescendantNodes<SectionDeclarationSyntax>().Any(s => s.SectionName == sectionName));
                if (lastPart == null)
                    return null;
                string newline = source.Contains("\r\n") ? "\r\n" : "\n";
                edits.Add((lastPart.Span.End, lastPart.Span.End,
                    newline + "section " + sectionName + " { key " + keyText + " }"));
            }
        }

        var text = source;
        foreach (var (start, end, insert) in edits.OrderByDescending(e => e.Start))
            text = text[..start] + insert + text[end..];
        return SyntaxTree.Parse(text).HasErrors ? null : text;
    }

    // ---- reading the section ----

    internal static bool IsTopLevel(SectionDeclarationSyntax s) => s.Parent is { Parent: null };

    // The parts with music in the section: a by-section declaration's cells, and every part
    // declaring the section inside itself (by part). A single-part section of bare music
    // has neither — null asks the collector for its one voice.
    private static IEnumerable<string?> PartsPlaying(SyntaxNode root, string sectionName)
    {
        var parts = new List<string?>();
        foreach (var s in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            if (s.SectionName != sectionName)
                continue;
            if (IsTopLevel(s))
            {
                foreach (var cell in s.DescendantNodes<PartBlockSyntax>())
                    if (!parts.Contains(cell.Name)) parts.Add(cell.Name);
            }
            else if (EnclosingPart(s) is { } part && !parts.Contains(part.Name.Text))
            {
                parts.Add(part.Name.Text);
            }
        }
        if (parts.Count == 0)
            parts.Add(null);
        return parts;
    }

    private static PartDeclarationSyntax? EnclosingPart(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is PartDeclarationSyntax part)
                return part;
        return null;
    }

    // The written (step, alteration) of every note and chord, by source position — the
    // collected items carry the same position. A `q` repeats its original chord's.
    private static Dictionary<int, List<(int Step, int Alter)>> SpellingsByPosition(SyntaxNode root)
    {
        static (int, int) Of(PitchSyntax p) => (KeySpelling.StepOf(p.BaseName), p.AccidentalOffset);
        var map = new Dictionary<int, List<(int, int)>>();
        foreach (var n in root.DescendantNodes<NoteSyntax>())
            map[n.Position] = n.DescendantNodes<PitchSyntax>().Take(1).Select(Of).ToList();
        foreach (var c in root.DescendantNodes<ChordSyntax>())
            map[c.Position] = c.DescendantNodes<PitchSyntax>().Select(Of).ToList();
        foreach (var q in root.DescendantNodes<ChordRepetitionSyntax>())
            if (ChordRepetitions.OriginalOf(q) is { } orig)
                map[q.Position] = orig.DescendantNodes<PitchSyntax>().Select(Of).ToList();
        foreach (var key in map.Keys.ToList())
            map[key].RemoveAll(sp => sp.Item1 < 0);
        return map;
    }

    // Written minus sounding pitch class, as most of the part's single notes have it: 0
    // unless the part transposes.
    private static int WrittenOffset(IEnumerable<Measure> measures, Dictionary<int, List<(int Step, int Alter)>> spellings)
    {
        var votes = new int[12];
        foreach (var measure in measures)
            foreach (var item in measure.Items)
                if (item is NoteItem { Midi: > 0 } n
                    && spellings.TryGetValue(n.SourcePosition, out var sp) && sp.Count == 1)
                    votes[Mod12(StepSemitones[sp[0].Step] + sp[0].Alter - n.Midi)]++;
        int best = 0;
        for (int i = 1; i < 12; i++)
            if (votes[i] > votes[best]) best = i;
        return best;
    }

    /// <summary>The keys the section states before its music begins, wherever the layout
    /// writes them — the keys <see cref="SetSectionKey"/> rewrites.</summary>
    internal static List<KeySignatureSyntax> StartingKeys(SyntaxNode root, string sectionName)
    {
        var keys = new List<KeySignatureSyntax>();
        foreach (var s in root.DescendantNodes<SectionDeclarationSyntax>())
        {
            if (s.SectionName != sectionName)
                continue;
            var children = Enumerable.Range(0, s.SlotCount).Select(s.GetChild)
                .Where(c => c is not null and not SyntaxTokenNode).Cast<SyntaxNode>().ToList();
            if (IsTopLevel(s) && children.Any(c => c is PartBlockSyntax))
            {
                // Beside cells, every direct directive is the section's header.
                keys.AddRange(children.OfType<KeySignatureSyntax>());
                foreach (var cell in children.OfType<PartBlockSyntax>())
                    if (cell.DescendantNodes<MusicBlockSyntax>().FirstOrDefault() is { } block)
                        keys.AddRange(Leading(block.Items));
            }
            else
            {
                keys.AddRange(Leading(children));
            }
        }
        return keys;

        static IEnumerable<KeySignatureSyntax> Leading(IEnumerable<SyntaxNode> items)
            => items.Where(i => i is not SyntaxTokenNode)
                .TakeWhile(SectionSymbols.IsSectionDirective)
                .OfType<KeySignatureSyntax>();
    }

    // The key the section starts in as its source states it: its own starting key, else the
    // file's top-level key.
    private static string? CurrentKey(SyntaxNode root, string sectionName)
    {
        var key = StartingKeys(root, sectionName).FirstOrDefault()
            ?? root.ChildNodesOfKind<KeySignatureSyntax>().FirstOrDefault();
        return key?.DisplayName;
    }

    private static FormDeclarationSyntax? SectionForm(string sectionName)
        => SyntaxTree.Parse("form { " + sectionName + " }").GetRoot()
            .DescendantNodes<FormDeclarationSyntax>().FirstOrDefault();

    // ---- the estimate ----

    private static readonly int[] StepSemitones = { 0, 2, 4, 5, 7, 9, 11 };

    private static int Mod12(int x) => ((x % 12) + 12) % 12;

    private static double Correlation(double[] x, Func<int, double> y)
    {
        double mx = x.Average(), my = 0;
        for (int i = 0; i < 12; i++) my += y(i);
        my /= 12;
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < 12; i++)
        {
            double dx = x[i] - mx, dy = y(i) - my;
            sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
        }
        return sxx == 0 || syy == 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    // Of the spellings of the tonic within seven accidentals, the one whose signature the
    // written notes agree with most; then the one with fewer accidentals; then sharps.
    private static string SpellTonic(int tonicPc, bool major, List<(int Step, int Alter, double Weight)> spelled)
    {
        string? best = null;
        double bestAgree = -1;
        int bestSharps = 0;
        for (int step = 0; step < 7; step++)
        {
            for (int alter = -1; alter <= 1; alter++)
            {
                if (Mod12(StepSemitones[step] + alter) != tonicPc)
                    continue;
                string name = "cdefgab"[step] + (alter switch { 1 => "is", -1 => "es", _ => "" });
                if (KeySpelling.SharpsFor(name, major ? "major" : "minor") is not int sharps || Math.Abs(sharps) > 7)
                    continue;
                double agree = spelled.Where(n => KeySpelling.Alteration(n.Step, sharps) == n.Alter).Sum(n => n.Weight);
                bool better = best == null
                    || agree > bestAgree + 1e-9
                    || (Math.Abs(agree - bestAgree) <= 1e-9
                        && (Math.Abs(sharps) < Math.Abs(bestSharps)
                            || (Math.Abs(sharps) == Math.Abs(bestSharps) && sharps > bestSharps)));
                if (better)
                {
                    best = name;
                    bestAgree = agree;
                    bestSharps = sharps;
                }
            }
        }
        return best!;
    }
}
