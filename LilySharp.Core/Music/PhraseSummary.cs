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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Music;

/// <summary>
/// What a <c>phrase</c> holds, told in a line or two for the editor's hover and completion
/// list: its length, how often the file plays it, what it is played on — for a drum phrase the
/// kit's pieces ("hi-hat", "snare") and a grid of its hits, one row per drum — and its source.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN (owner's request 2026-10-11): a drum part is written as phrases of patterns
/// (hi-hat, snare, kick) combined bar by bar, and a pattern written in durations (<c>hh8 8 8 hho
/// hh</c>) does not line its hits up the way a drum chart does — the grid is that chart, drawn
/// for the reader. The phrase is measured by the collector itself, on a small book that plays
/// only it (the file's phrases, time, key and octave directives copied around it), so tuplets,
/// bare durations, voices and nested phrases count exactly as the page counts them.
/// </remarks>
public sealed record PhraseSummary(
    string Name,
    string LengthText,
    int Uses,
    bool IsDrums,
    IReadOnlyList<string> Families,
    int NoteCount,
    string? Grid,
    string Source)
{
    /// <summary>The short tag a completion item shows beside the name: the kit's pieces for a
    /// drum phrase ("hi-hat · 1 bar"), else the notes ("melody · 2 bars").</summary>
    public string Tag => (Families.Count > 0 ? string.Join(", ", Families) : IsDrums ? "drums" : "melody")
                         + " · " + LengthText;

    /// <summary>The hover's Markdown: a title line, the drum grid when there is one, the source.</summary>
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("**Phrase** `").Append(Name).Append("` — ").Append(LengthText);
        if (!IsDrums && NoteCount > 0)
            sb.Append(" · ").Append(NoteCount).Append(NoteCount == 1 ? " note" : " notes");
        sb.Append(" · ").Append(Uses == 0 ? "not used yet" : Uses == 1 ? "used once" : $"used {Uses} times");
        if (Grid != null)
            sb.Append("\n\n```text\n").Append(Grid).Append("\n```");
        sb.Append("\n\n`").Append(Source).Append('`');
        return sb.ToString();
    }

    private static readonly ConditionalWeakTable<SyntaxTree, ConcurrentDictionary<string, PhraseSummary?>> Cache = new();

    /// <summary>The summary of the phrase <paramref name="name"/> declared in
    /// <paramref name="tree"/>, or null when there is none or it cannot be measured.</summary>
    public static PhraseSummary? Of(SyntaxTree tree, string name)
        => Cache.GetOrCreateValue(tree).GetOrAdd(name, n => Measure(tree, n));

    private static PhraseSummary? Measure(SyntaxTree tree, string name)
    {
        var root = tree.GetRoot();
        var decl = root.ChildNodes().OfType<PhraseDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == name);
        if (decl == null)
            return null;
        int uses = root.DescendantNodes().OfType<VariableReferenceSyntax>().Count(v => v.Name.Text == name);
        string source = SourceOf(tree, decl);
        bool drums = HasDrums(root, decl, new HashSet<string>(StringComparer.Ordinal));

        try
        {
            var score = Collect(tree, root, name, drums);
            if (score == null)
                return new PhraseSummary(name, "?", uses, drums, [], 0, null, source);
            var staff = score.EnumerateStaves().First().Staff;
            var hits = new List<(Fraction Onset, int Midi, int Position)>();
            // Every item's start, rests included: the grid steps by what is WRITTEN, so a
            // kick of `bd4 r bd r` reads in quarters, not in the halves its hits alone fall on.
            var starts = new List<Fraction>();
            Fraction length = Fraction.Zero;
            int notes = 0;
            foreach (var voice in staff.Voices)
            {
                Fraction t = Fraction.Zero;
                foreach (var measure in voice.Measures)
                    foreach (var item in measure.Items)
                    {
                        if (item.GraceTime)
                            continue;
                        if (item.Duration > Fraction.Zero)
                            starts.Add(t);
                        switch (item)
                        {
                            case NoteItem n when !n.IsPitchlessSlash:
                                hits.Add((t, n.Midi, n.StaffPosition));
                                notes++;
                                break;
                            case ChordItem c:
                                foreach (var m in c.Notes)
                                    hits.Add((t, m.Midi, m.StaffPosition));
                                notes += c.Notes.Length;
                                break;
                        }
                        t += item.Duration;
                    }
                if (t > length)
                    length = t;
            }
            var time = score.TimeSignature;
            var families = drums
                ? hits.Select(h => FamilyOf(h.Midi)).Distinct().OrderBy(f => Array.IndexOf(FamilyOrder, f)).ToList()
                : new List<string>();
            string? grid = drums ? GridOf(hits, starts, length, time) : null;
            return new PhraseSummary(name, LengthWords(length, time), uses, drums, families, notes, grid, source);
        }
        catch (Exception)
        {
            // A summary is a courtesy: a book the measuring copy cannot collect says only
            // what the syntax does.
            return new PhraseSummary(name, "?", uses, drums, [], 0, null, source);
        }
    }

    /// <summary>The phrase body's text without its braces, its whitespace folded, cut short.</summary>
    private static string SourceOf(SyntaxTree tree, PhraseDeclarationSyntax decl)
    {
        string body = tree.Text.Substring(decl.Body.Span.Start, decl.Body.Span.Length).Trim();
        if (body.StartsWith('{')) body = body[1..];
        if (body.EndsWith('}')) body = body[..^1];
        body = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return body.Length > 90 ? body[..87] + " …" : body;
    }

    /// <summary>Whether the phrase plays a drum — in its body, or in a phrase it names.</summary>
    private static bool HasDrums(SyntaxNode root, PhraseDeclarationSyntax decl, HashSet<string> seen)
    {
        if (!seen.Add(decl.Name.Text))
            return false;
        if (decl.Body.DescendantNodes().OfType<DrumNoteSyntax>().Any())
            return true;
        foreach (var reference in decl.Body.DescendantNodes().OfType<VariableReferenceSyntax>())
            if (root.ChildNodes().OfType<PhraseDeclarationSyntax>().FirstOrDefault(p => p.Name.Text == reference.Name.Text)
                is { } inner && HasDrums(root, inner, seen))
                return true;
        return false;
    }

    // The measuring book's own names: words no file declares by accident.
    private const string MeasurePart = "lysPhraseMeasurePart";
    private const string MeasureSection = "lysPhraseMeasureSection";

    /// <summary>The phrase collected alone: the file's phrases and the directives that shape
    /// them (time, key, octave mode, pitch, variables) copied, and one part playing the phrase.</summary>
    private static MultiStaffScore? Collect(SyntaxTree tree, SyntaxNode root, string name, bool drums)
    {
        var sb = new StringBuilder();
        foreach (var child in root.ChildNodes())
            if (child.Kind is SyntaxKind.PhraseDeclaration or SyntaxKind.VariableDeclaration
                or SyntaxKind.TimeSignature or SyntaxKind.KeySignature or SyntaxKind.OctaveDirective
                or SyntaxKind.PropertyAssignment)
                sb.Append(tree.Text, child.Span.Start, child.Span.Length).Append('\n');
        sb.Append("part ").Append(MeasurePart).Append(" { clef ").Append(drums ? "percussion" : "treble").Append(" }\n");
        sb.Append("section ").Append(MeasureSection).Append(" { ").Append(MeasurePart).Append(" { ").Append(name).Append(" } }\n");
        sb.Append("form { ~").Append(MeasureSection).Append(" }\n");
        sb.Append("score { staff ").Append(MeasurePart).Append(" }\n");
        var measured = SyntaxTree.Parse(sb.ToString());
        var spec = RenderSpecParser.Choose(RenderSpecParser.FindAll(measured), null);
        return spec == null ? null : SvgGenerator.CollectScore(measured, spec);
    }

    /// <summary>"1 bar (4/4)", "2 bars (3/4)", "3 beats", else the whole-note fraction.</summary>
    private static string LengthWords(Fraction length, TimeSignature time)
    {
        if (length == Fraction.Zero)
            return "empty";
        if (!time.SenzaMisura && time.Beats > 0 && time.BeatType > 0)
        {
            var bar = new Fraction(time.Beats, time.BeatType);
            var bars = length / bar;
            if (bars.Denominator == 1)
                return $"{bars.Numerator} {(bars.Numerator == 1 ? "bar" : "bars")} ({time.Beats}/{time.BeatType})";
            var beats = length / new Fraction(1, time.BeatType);
            if (beats.Denominator == 1)
                return $"{beats.Numerator} {(beats.Numerator == 1 ? "beat" : "beats")}";
        }
        return $"{length.Numerator}/{length.Denominator} of a whole note";
    }

    // ── the drum grid ──

    /// <summary>The kit's pieces in the order a chart lists them (and the completion list sorts by).</summary>
    public static readonly string[] FamilyOrder = ["hi-hat", "ride", "cymbal", "snare", "toms", "bass drum", "percussion"];

    private static string FamilyOf(int midi) => midi switch
    {
        42 or 44 or 46 => "hi-hat",
        51 or 53 or 59 => "ride",
        49 or 52 or 55 or 57 => "cymbal",
        37 or 38 or 39 or 40 => "snare",
        41 or 43 or 45 or 47 or 48 or 50 => "toms",
        35 or 36 => "bass drum",
        _ => "percussion",
    };

    // The row label per General MIDI key: the short name a drummer writes (several names share
    // a key — hh and hhc are both 42; the row is the drum, not the spelling).
    private static readonly Dictionary<int, string> RowNames = new()
    {
        [35] = "bda", [36] = "bd", [37] = "ss", [38] = "sn", [39] = "hc", [40] = "sne",
        [41] = "tomfl", [42] = "hh", [43] = "tomfh", [44] = "hhp", [45] = "toml", [46] = "hho",
        [47] = "tomml", [48] = "tommh", [49] = "cymc", [50] = "tomh", [51] = "cymr", [52] = "cymch",
        [53] = "rb", [54] = "tamb", [55] = "cyms", [56] = "cb", [57] = "cymcb", [59] = "cymrb",
    };

    private const int MaxColumns = 32;

    /// <summary>One row per drum (highest on the staff first), one column per grid step — the
    /// finest step every hit and the length fall on — 'x' for a hit, '.' for none, a wider gap
    /// at each beat; null when the steps would be too many to read.</summary>
    private static string? GridOf(List<(Fraction Onset, int Midi, int Position)> hits, List<Fraction> starts,
        Fraction length, TimeSignature time)
    {
        if (hits.Count == 0 || length == Fraction.Zero)
            return null;
        var points = starts.Append(length).Where(f => f != Fraction.Zero).ToList();
        long lcm = 1;
        foreach (var p in points)
            lcm = Lcm(lcm, p.Denominator);
        long g = 0;
        foreach (var p in points)
            g = Gcd(g, p.Numerator * (lcm / p.Denominator));
        if (g == 0)
            return null;
        var unit = new Fraction((int)g, (int)lcm);
        var cols = length / unit;
        if (cols.Denominator != 1 || cols.Numerator > MaxColumns)
            return null;
        int columns = cols.Numerator;
        int perBeat = 0;
        if (time.BeatType > 0 && new Fraction(1, time.BeatType) / unit is { Denominator: 1 } beat)
            perBeat = beat.Numerator;

        var rows = hits.GroupBy(h => h.Midi)
            .Select(grp => (Midi: grp.Key, Position: grp.Max(h => h.Position), Onsets: grp.Select(h => h.Onset).ToHashSet()))
            .OrderByDescending(r => r.Position).ThenBy(r => r.Midi)
            .ToList();
        int labelWidth = rows.Max(r => Label(r.Midi).Length);
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Label(row.Midi).PadRight(labelWidth));
            for (int i = 0; i < columns; i++)
            {
                sb.Append(perBeat > 1 && i > 0 && i % perBeat == 0 ? "  " : " ");
                sb.Append(row.Onsets.Contains(unit * new Fraction(i, 1)) ? 'x' : '.');
            }
        }
        return sb.ToString();

        static string Label(int midi) => RowNames.TryGetValue(midi, out var n) ? n : $"gm{midi}";
    }

    private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
    private static long Lcm(long a, long b) => a / Gcd(a, b) * b;
}
