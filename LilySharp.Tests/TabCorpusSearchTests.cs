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
/// MEASUREMENT: the owner's bass books as planner input — each book's tab voice as the events
/// the planner reads (no string written), and the owner's string for every note LilyPond
/// engraved. Runs only when LILYSHARP_TABCORPUS and LILYSHARP_TABCORPUS_LP are set.
/// </summary>
[Trait("Category", "Measure")]
public sealed class TabCorpusSearchTests
{
    private static readonly Regex StringMark = new(@"\\([1-6])(?![0-9])");

    internal sealed record BookData(string Name, TabEvent[] Events, int[] Expected, int[] Tuning, int HandSpan, int[] Measure, double[] Duration);

    private static List<BookData>? s_books;

    internal static List<BookData> Books()
    {
        if (s_books != null) return s_books;
        var folder = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS")!;
        var lpFile = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LP")!;
        var lilyPond = new Dictionary<string, List<(int Midi, int String)>>(StringComparer.Ordinal);
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

        var books = new List<BookData>();
        foreach (var lys in Directory.GetFiles(folder, "*.lys").OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(lys);
            if (!lilyPond.TryGetValue(name, out var lp)) continue;
            BookData? data;
            try { data = Build(name, StringMark.Replace(File.ReadAllText(lys), "")); }
            catch { continue; }
            if (data == null) continue;
            var pairs = TabCorpusMeasureTests.Align(data.Events.Select(e => e.Midi).ToList(), lp.Select(n => n.Midi).ToList());
            if (pairs.Count < 0.9 * Math.Max(data.Events.Length, lp.Count)) continue;
            foreach (var (a, b) in pairs)
                if (!data.Events[a].TiedFromPrevious && data.Events[a].ChordHigh == 0)
                    data.Expected[a] = lp[b].String;
            books.Add(data);
        }
        return s_books = books;
    }

    /// <summary>The events <see cref="TabResolver.ResolveTabStrings"/> would hand the planner.</summary>
    private static BookData? Build(string name, string source)
    {
        var tree = SyntaxTree.Parse(source);
        foreach (var spec in RenderSpecParser.FindAll(tree))
        {
            var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
            var tab = multi.EnumerateStaves().Select(s => s.Staff).FirstOrDefault(s => s.IsTab && s.Tuning.HasValue);
            if (tab is null) continue;
            int shift = Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
            var tuning = Tunings.GetTuning(tab.Tuning!.Value);
            var events = new List<TabEvent>();
            var measures = new List<int>();
            var durations = new List<double>();
            int mi = -1;
            double time = 0, previousOnset = double.NaN;
            int? previousMidi = null;
            bool restSince = false, previousSlurStart = false;
            foreach (var m in tab.PrimaryVoice.Measures)
            {
                mi++;
                foreach (var item in m.Items)
                {
                    double onset = time;
                    time += item.Duration.ToDouble();
                    if (item is RestItem || item is ChordItem) { restSince = true; continue; }
                    if (item is not NoteItem note || note.IsPitchlessSlash || note.TabBelowRange) continue;
                    int midi = note.Midi + shift;
                    bool leapt = previousMidi is { } prev && Math.Abs(midi - prev) >= Tunings.LeapResetInterval;
                    events.Add(new TabEvent(midi, 0,
                        double.IsNaN(previousOnset) ? 1.0 : onset - previousOnset,
                        HandFree: restSince || leapt,
                        SlurFromPrevious: previousSlurStart && note.HasSlurEnd && !leapt,
                        TiedFromPrevious: note.IsTieTarget));
                    measures.Add(mi);
                    durations.Add(item.Duration.ToDouble());
                    previousOnset = onset;
                    previousMidi = midi;
                    previousSlurStart = note.HasSlurStart;
                    restSince = false;
                }
            }
            return new BookData(name, events.ToArray(), new int[events.Count], tuning, Tunings.HandSpanFor(tab.Tuning!.Value), measures.ToArray(), durations.ToArray());
        }
        return null;
    }

    internal static (int Ok, int Total) Score(IEnumerable<BookData> books, Func<BookData, int[]> plan)
    {
        int ok = 0, total = 0;
        foreach (var b in books)
        {
            var got = plan(b);
            for (int i = 0; i < got.Length; i++)
            {
                if (b.Expected[i] == 0) continue;
                total++;
                if (got[i] == b.Expected[i]) ok++;
            }
        }
        return (ok, total);
    }

    private static string Pct((int Ok, int Total) s) => $"{100.0 * s.Ok / s.Total:F2}% ({s.Ok}/{s.Total})";

    [Fact]
    public void Baseline()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LP"))) return;
        var books = Books();
        var sb = new StringBuilder();
        sb.AppendLine($"books {books.Count}");
        sb.AppendLine("current planner: " + Pct(Score(books, b => TabFingeringPlanner.Plan(b.Events, b.Tuning, b.HandSpan))));
        File.WriteAllText(Report("baseline"), sb.ToString());
    }

    private static readonly (string Name, Func<TabFingeringWeights, double> Get, Func<TabFingeringWeights, double, TabFingeringWeights> Set)[] Knobs =
    [
        ("Shift", w => w.Shift, (w, v) => w with { Shift = v }),
        ("FirstFret", w => w.FirstFret, (w, v) => w with { FirstFret = v }),
        ("MaxShiftFrets", w => w.MaxShiftFrets, (w, v) => w with { MaxShiftFrets = v }),
        ("FastestTime", w => w.FastestTime, (w, v) => w with { FastestTime = v }),
        ("SlowestTime", w => w.SlowestTime, (w, v) => w with { SlowestTime = v }),
        ("FreeShiftFactor", w => w.FreeShiftFactor, (w, v) => w with { FreeShiftFactor = v }),
        ("Stretch", w => w.Stretch, (w, v) => w with { Stretch = v }),
        ("Skip", w => w.Skip, (w, v) => w with { Skip = v }),
        ("High", w => w.High, (w, v) => w with { High = v }),
        ("HeightPerFret", w => w.HeightPerFret, (w, v) => w with { HeightPerFret = v }),
        ("OpenPerHandFret", w => w.OpenPerHandFret, (w, v) => w with { OpenPerHandFret = v }),
        ("OpenAboveLowPosition", w => w.OpenAboveLowPosition, (w, v) => w with { OpenAboveLowPosition = v }),
        ("LowerFret", w => w.LowerFret, (w, v) => w with { LowerFret = v }),
    ];

    [Fact]
    public void SearchWeights()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_SEARCH"))) return;
        var books = Books();
        var train = books.Where((_, i) => i % 2 == 0).ToList();
        var test = books.Where((_, i) => i % 2 == 1).ToList();
        var log = new StringBuilder();
        var w = TabFingeringWeights.Default;
        int span = 3;
        double Eval(TabFingeringWeights x, int s, List<BookData> set)
        {
            if (x.FastestTime > x.SlowestTime) return -1;
            var r = Score(set, b => TabFingeringPlanner.Plan(b.Events, b.Tuning, s, x));
            return (double)r.Ok / r.Total;
        }
        double best = Eval(w, span, train);
        log.AppendLine($"start train {best:P2} test {Eval(w, span, test):P2}");
        double[] factors = [0, 0.1, 0.3, 0.6, 1.5, 3, 10];
        for (int round = 0; round < 4; round++)
        {
            bool moved = false;
            foreach (var knob in Knobs)
            {
                double v0 = knob.Get(w);
                foreach (var f in factors)
                {
                    double v = v0 == 0 ? f : v0 * f;
                    if (v == v0) continue;
                    var cand = knob.Set(w, v);
                    double acc = Eval(cand, span, train);
                    if (acc > best + 1e-6) { best = acc; w = cand; moved = true; log.AppendLine($"r{round} {knob.Name}={v:G4} train {best:P2}"); }
                }
            }
            foreach (var s in new[] { 2, 3, 4, 5 })
            {
                if (s == span) continue;
                double acc = Eval(w, s, train);
                if (acc > best + 1e-6) { best = acc; span = s; moved = true; log.AppendLine($"r{round} span={s} train {best:P2}"); }
            }
            File.WriteAllText(Report("search"), log.ToString());
            if (!moved) break;
        }
        log.AppendLine($"final train {best:P2} test {Eval(w, span, test):P2}  span {span}");
        log.AppendLine(w.ToString());
        File.WriteAllText(Report("search"), log.ToString());
    }

    internal static string Report(string name)
        => Path.Combine(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_OUT") ?? Path.GetTempPath(), name + ".txt");
}
