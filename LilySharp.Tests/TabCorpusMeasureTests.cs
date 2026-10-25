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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// MEASUREMENT, not a test: how closely the planner, given no written strings, plays the
/// owner's bass books the way the owner fingered them. Runs only when LILYSHARP_TABCORPUS
/// names the folder of .lys twins; writes its report to LILYSHARP_TABCORPUS_REPORT.
/// </summary>
/// <remarks>
/// The owner's fingering of a note is its written string (<c>\N</c>), or else the string
/// LilyPond gives an unmarked note — the highest string it can be fretted on — since the
/// LilyPond books' printed tab is what the owner checked.
/// </remarks>
[Trait("Category", "Measure")]
public sealed class TabCorpusMeasureTests
{
    private static readonly Regex StringMark = new(@"\\([1-6])(?![0-9])");

    internal sealed record Played(int Midi, int Expected, bool Written, int Got, bool Tied, double Onset,
        double Duration, int Measure, string Book);

    [Fact]
    public void Measure()
    {
        var folder = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS");
        if (string.IsNullOrEmpty(folder)) return;
        var report = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_REPORT")
            ?? Path.Combine(Path.GetTempPath(), "tabcorpus-report.txt");

        // LilyPond's own strings, note by note (the TABPROBE logs, first engraving of each place).
        var lilyPond = new Dictionary<string, List<(int Midi, int String)>>(StringComparer.Ordinal);
        var lpFile = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LP");
        if (!string.IsNullOrEmpty(lpFile))
        {
            var lines = File.ReadAllLines(lpFile);
            var cols = lines[0].Split('\t').Select(c => c.Trim('"')).ToList();
            int iBook = cols.IndexOf("Book"), iMidi = cols.IndexOf("Midi"), iStr = cols.IndexOf("String"), iDup = cols.IndexOf("Dup");
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t').Select(c => c.Trim('"')).ToArray();
                if (f[iDup] == "True") continue;
                if (!lilyPond.TryGetValue(f[iBook], out var seq)) lilyPond[f[iBook]] = seq = [];
                seq.Add((int.Parse(f[iMidi]), int.Parse(f[iStr])));
            }
        }

        var all = new List<Played>();
        var sb = new StringBuilder();
        int alignedTotal = 0, twinTotal = 0, lpTotal = 0, twinAgreesLp = 0;
        foreach (var lys in Directory.GetFiles(folder, "*.lys").OrderBy(p => p, StringComparer.Ordinal))
        {
            var ly = Path.ChangeExtension(lys, ".ly");
            if (!File.Exists(ly) || !StringMark.IsMatch(File.ReadAllText(ly))) continue;
            List<Played>? notes;
            try { notes = Book(lys); }
            catch (Exception e) { sb.AppendLine($"!! {Path.GetFileName(lys)}: {e.GetType().Name} {e.Message}"); continue; }
            if (notes == null) { sb.AppendLine($"-- {Path.GetFileName(lys)}: no tab"); continue; }
            string book = Path.GetFileNameWithoutExtension(lys);
            if (lilyPond.TryGetValue(book, out var lp))
            {
                // Align the twin to LilyPond's notes by pitch, and take LilyPond's string as the
                // owner's wherever the two meet; a twin note LilyPond does not have is dropped.
                var pairs = Align(notes.Select(n => n.Midi).ToList(), lp.Select(n => n.Midi).ToList());
                twinTotal += notes.Count; lpTotal += lp.Count; alignedTotal += pairs.Count;
                var kept = new List<Played>();
                foreach (var (a, b) in pairs)
                {
                    if (notes[a].Expected == lp[b].String) twinAgreesLp++;
                    kept.Add(notes[a] with { Expected = lp[b].String });
                }
                if (pairs.Count < 0.9 * Math.Max(notes.Count, lp.Count))
                    sb.AppendLine($"~~ {book}: aligned {pairs.Count} of twin {notes.Count} / LilyPond {lp.Count}");
                notes = kept;
            }
            else if (lilyPond.Count > 0)
            {
                sb.AppendLine($"?? {book}: no LilyPond notes");
                continue;
            }
            all.AddRange(notes);
        }
        if (lilyPond.Count > 0)
            sb.Insert(0, $"alignment: twin {twinTotal}, LilyPond {lpTotal}, aligned {alignedTotal}; twin's string = LilyPond's on {twinAgreesLp}\n");

        var scored = all.Where(p => !p.Tied).ToList();
        int agree = scored.Count(p => p.Expected == p.Got);
        int written = scored.Count(p => p.Written);
        int writtenAgree = scored.Count(p => p.Written && p.Expected == p.Got);
        var head = new StringBuilder();
        head.AppendLine($"books {all.Select(p => p.Book).Distinct().Count()}  notes {scored.Count}  agree {agree} ({100.0 * agree / scored.Count:F2}%)");
        head.AppendLine($"written {written}: agree {writtenAgree} ({100.0 * writtenAgree / Math.Max(1, written):F2}%)");
        head.AppendLine($"unwritten {scored.Count - written}: agree {agree - writtenAgree} ({100.0 * (agree - writtenAgree) / Math.Max(1, scored.Count - written):F2}%)");
        head.AppendLine();
        foreach (var g in all.GroupBy(p => p.Book))
        {
            var s = g.Where(p => !p.Tied).ToList();
            int miss = s.Count(p => p.Expected != p.Got);
            head.AppendLine($"{miss,4} / {s.Count,4}  {g.Key}");
        }
        head.AppendLine();
        head.Append(sb);
        File.WriteAllText(report, head.ToString());

        var dump = Path.ChangeExtension(report, ".tsv");
        using var w = new StreamWriter(dump);
        w.WriteLine("book\tmeasure\tonset\tdur\tmidi\texpected\twritten\tgot\ttied");
        foreach (var p in all)
            w.WriteLine($"{p.Book}\t{p.Measure}\t{p.Onset:F4}\t{p.Duration:F4}\t{p.Midi}\t{p.Expected}\t{(p.Written ? 1 : 0)}\t{p.Got}\t{(p.Tied ? 1 : 0)}");
    }

    /// <summary>The tab notes of a book's first tab staff, owner's string beside the planner's.</summary>
    internal static List<Played>? Book(string path)
    {
        string source = File.ReadAllText(path);
        var withMarks = TabNotes(source);
        if (withMarks == null) return null;
        var bare = TabNotes(StringMark.Replace(source, ""));
        if (bare == null || bare.Notes.Count != withMarks.Notes.Count)
            throw new InvalidOperationException($"note counts differ: {withMarks.Notes.Count} vs {bare?.Notes.Count}");

        var tuning = withMarks.Tuning;
        int n = tuning.Length;
        var list = new List<Played>();
        for (int k = 0; k < withMarks.Notes.Count; k++)
        {
            var (note, onset, measure) = withMarks.Notes[k];
            int midi = note.Midi + withMarks.Shift;
            int? writtenString = WrittenString(source, note.SourcePosition);
            int expected = writtenString ?? LilyPondDefault(midi, tuning);
            list.Add(new Played(midi, expected, writtenString.HasValue, bare.Notes[k].Note.StringNumber ?? 0,
                note.IsTieTarget, onset, note.Duration.ToDouble(), measure, Path.GetFileNameWithoutExtension(path)));
        }
        return list;
    }

    /// <summary>The longest common subsequence of two pitch sequences, as index pairs.</summary>
    internal static List<(int A, int B)> Align(List<int> a, List<int> b)
    {
        int n = a.Count, m = b.Count;
        var len = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                len[i, j] = a[i] == b[j] ? len[i + 1, j + 1] + 1 : Math.Max(len[i + 1, j], len[i, j + 1]);
        var pairs = new List<(int, int)>();
        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (a[i] == b[j]) { pairs.Add((i, j)); i++; j++; }
            else if (len[i + 1, j] >= len[i, j + 1]) i++;
            else j++;
        }
        return pairs;
    }

    /// <summary>LilyPond's string for an unmarked note: the highest string it can be fretted on.</summary>
    internal static int LilyPondDefault(int midi, int[] tuning)
    {
        int n = tuning.Length;
        for (int str = 1; str <= n; str++)
            if (midi - tuning[n - str] >= 0) return str;
        return n;
    }

    private static readonly Regex NoteHead = new(@"\G[a-g](?:isis|eses|is|es)?[,']*(?:\d+\.*)?(?:~)?\\([1-6])");

    private static int? WrittenString(string source, int position)
    {
        var m = NoteHead.Match(source, position);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    internal sealed record TabVoice(List<(NoteItem Note, double Onset, int Measure)> Notes, int[] Tuning, int Shift);

    private static TabVoice? TabNotes(string source)
    {
        var tree = SyntaxTree.Parse(source);
        foreach (var spec in RenderSpecParser.FindAll(tree))
        {
            var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
            var tab = multi.EnumerateStaves().Select(s => s.Staff).FirstOrDefault(s => s.IsTab && s.Tuning.HasValue);
            if (tab is null) continue;
            int shift = Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
            var tuning = Tunings.GetTuning(tab.Tuning!.Value);
            var notes = new List<(NoteItem, double, int)>();
            double time = 0;
            int mi = 0;
            foreach (var m in tab.PrimaryVoice.Measures)
            {
                foreach (var item in m.Items)
                {
                    if (item is NoteItem nt && !nt.IsPitchlessSlash && !nt.TabBelowRange)
                        notes.Add((nt, time, mi));
                    time += item.Duration.ToDouble();
                }
                mi++;
            }
            return new TabVoice(notes, tuning, shift);
        }
        return null;
    }
}
