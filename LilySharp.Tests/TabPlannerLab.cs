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
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// MEASUREMENT: an experimental fingering planner, its costs a vector the search fits to the
/// owner's bass books. Runs only when LILYSHARP_TABCORPUS_LAB is set.
/// </summary>
[Trait("Category", "Measure")]
public sealed class TabPlannerLab
{
    internal static readonly string[] Names =
    [
        "shiftA", "shiftB", "shiftCap", "freeFactor", "timeAlpha", "openUnheld",
        "skip", "skipExtra", "stretch", "slurAcross", "repeatChange", "open", "openHigh",
        "str1", "str2", "str3", "str4", "str5",
        "pos1", "pos2", "pos3", "pos4", "pos5", "pos6", "pos7", "pos8", "pos9", "pos10", "pos11", "pos12",
        "sameStringStep", "span", "open1", "open2", "open3", "open4", "open5", "openThenLower", "openThenLowerOpen",
    ];

    internal static int I(string name) => Array.IndexOf(Names, name);

    internal static double[] Load(string path)
    {
        var w = File.ReadAllText(path).Split(',').Select(double.Parse).ToList();
        while (w.Count < Names.Length) w.Add(0);
        return w.ToArray();
    }

    internal static double[] Initial()
    {
        var p = new double[Names.Length];
        p[I("shiftA")] = 1; p[I("shiftB")] = 0.5; p[I("shiftCap")] = 6; p[I("freeFactor")] = 0.35;
        p[I("timeAlpha")] = 0; p[I("openUnheld")] = 1; p[I("skip")] = 2; p[I("skipExtra")] = 1;
        p[I("stretch")] = 1; p[I("slurAcross")] = 5; p[I("repeatChange")] = 2; p[I("open")] = 0;
        p[I("openHigh")] = 0.2; p[I("span")] = 3;
        return p;
    }

    private readonly record struct St(int S, int F, int P);

    internal static int[] Plan(TabEvent[] events, int[] tuning, double[] w)
    {
        int n = events.Length, strings = tuning.Length;
        var result = new int[n];
        if (n == 0) return result;
        int span = (int)Math.Round(w[I("span")]);
        double shiftA = w[I("shiftA")], shiftB = w[I("shiftB")], shiftCap = w[I("shiftCap")];
        double freeFactor = w[I("freeFactor")], timeAlpha = w[I("timeAlpha")], openUnheld = w[I("openUnheld")];
        double skip = w[I("skip")], skipExtra = w[I("skipExtra")], stretch = w[I("stretch")];
        double slurAcross = w[I("slurAcross")], repeatChange = w[I("repeatChange")];
        double open = w[I("open")], openHigh = w[I("openHigh")], sameStringStep = w[I("sameStringStep")];
        int str0 = I("str1"), pos0 = I("pos1");

        var states = new List<St>[n];
        var cost = new List<double>[n];
        var back = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            var ev = events[i];
            var st = new List<St>();
            for (int s = 1; s <= strings; s++)
            {
                if (ev.FixedString > 0 && s != ev.FixedString) continue;
                int f = ev.Midi - tuning[strings - s];
                if (f < 0 || f > 24) continue;
                if (f > 0)
                    for (int p = Math.Max(1, f - span); p <= f; p++) st.Add(new St(s, f, p));
                else if (i == 0)
                    st.Add(new St(s, 0, 1));
                else
                {
                    var hands = new HashSet<int>();
                    foreach (var q in states[i - 1]) hands.Add(q.P);
                    if (ev.HandFree) hands.Add(1);
                    foreach (var p in hands) st.Add(new St(s, 0, p));
                }
            }
            if (st.Count == 0)
            {
                var (s0, f0) = Tunings.CalculateFret(ev.Midi, tuning, 0);
                st.Add(new St(s0, f0, Math.Max(1, f0)));
            }
            states[i] = st;
            var c = new List<double>(st.Count);
            var b = new List<int>(st.Count);
            double timeScale = timeAlpha == 0 ? 1 : Math.Pow(0.25 / Math.Clamp(ev.TimeToMove, 1.0 / 32, 2), timeAlpha);
            foreach (var x in st)
            {
                double node = 0;
                if (x.F > 0)
                {
                    node += w[str0 + Math.Min(x.S, 5) - 1];
                    if (x.F - x.P == span) node += stretch;
                }
                else node += open + openHigh * (x.P - 1) + w[I("open1") + Math.Min(x.S, 5) - 1];
                node += w[pos0 + Math.Min(x.P, 12) - 1];
                if (i == 0) { c.Add(node); b.Add(-1); continue; }

                double best = double.MaxValue; int arg = -1;
                var prev = states[i - 1];
                var pc = cost[i - 1];
                var pe = events[i - 1];
                for (int j = 0; j < prev.Count; j++)
                {
                    var q = prev[j];
                    if (x.F == 0 && x.P != q.P && !ev.HandFree) continue;
                    double e = pc[j];
                    int dp = Math.Abs(x.P - q.P);
                    if (dp > 0)
                    {
                        double sh = (shiftA + shiftB * Math.Min(dp, shiftCap)) * timeScale;
                        if (ev.HandFree) sh *= freeFactor;
                        else if (q.F == 0) sh *= openUnheld;
                        e += sh;
                    }
                    int ds = Math.Abs(x.S - q.S);
                    if (ds > 1) e += skip + skipExtra * (ds - 2);
                    if (ev.TiedFromPrevious && ds != 0) e += 1e6;
                    if (pe.Midi == ev.Midi && ds != 0) e += repeatChange;
                    if (ds == 0 && x.F > 0 && q.F > 0 && x.F != q.F) e += sameStringStep;
                    if (q.F == 0 && x.S > q.S) e += w[I("openThenLower")] + (x.F == 0 ? w[I("openThenLowerOpen")] : 0);
                    if (e < best) { best = e; arg = j; }
                }
                if (arg < 0) { best = 1e9; arg = 0; }
                c.Add(best + node); b.Add(arg);
            }
            cost[i] = c; back[i] = b;
        }
        int k = 0;
        for (int j = 1; j < cost[n - 1].Count; j++) if (cost[n - 1][j] < cost[n - 1][k]) k = j;
        for (int i = n - 1; i >= 0; i--)
        {
            result[i] = states[i][k].S;
            k = back[i][k];
            if (k < 0) break;
        }
        return result;
    }

    /// <summary>How many strings a user would write, first miss first, before the planner plays
    /// the whole book as expected — each written string fixes that note and the planner replans.</summary>
    internal static int PinsNeeded(TabCorpusSearchTests.BookData b, Func<TabEvent[], int[]> plan, int cap = 3000)
    {
        var events = (TabEvent[])b.Events.Clone();
        int pins = 0;
        while (pins < cap)
        {
            var got = plan(events);
            int miss = -1;
            for (int i = 0; i < got.Length; i++)
                if (b.Expected[i] != 0 && got[i] != b.Expected[i]) { miss = i; break; }
            if (miss < 0) break;
            events[miss] = events[miss] with { FixedString = b.Expected[miss] };
            pins++;
        }
        return pins;
    }

    internal static BassFingeringWeights ToWeights(double[] w) => new()
    {
        ShiftBase = w[I("shiftA")], ShiftPerFret = w[I("shiftB")], ShiftCap = w[I("shiftCap")],
        FreeShiftFactor = w[I("freeFactor")], TimeExponent = w[I("timeAlpha")], AfterOpenFactor = w[I("openUnheld")],
        Skip = w[I("skip")], SkipPerString = w[I("skipExtra")], Stretch = w[I("stretch")],
        RepeatAcross = w[I("repeatChange")], Open = w[I("open")],
        OpenPerHandFret = w[I("openHigh")],
        StringCost = Enumerable.Range(0, 5).Select(k => w[I("str1") + k]).ToArray(),
        PositionCost = Enumerable.Range(0, 12).Select(k => w[I("pos1") + k]).ToArray(),
        SameStringStep = w[I("sameStringStep")], Span = (int)Math.Round(w[I("span")]),
        OpenStringCost = Enumerable.Range(0, 5).Select(k => w[I("open1") + k]).ToArray(),
        OpenThenLowerOpen = w[I("openThenLowerOpen")],
    };

    private static (int? String, int Fret)[][] BassTabFrets(string body)
    {
        var src = "part melody {\n  instrument bass\n  section A {\n    " + body + "\n  }\n}\n" +
                  "form { A }\nscore {\n  tab melody\n}\n";
        var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(src);
        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
        var tab = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec)
            .EnumerateStaves().First(s => s.Staff.IsTab).Staff;
        int shift = Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
        return tab.PrimaryVoice.Measures
            .Select(m => m.Items.OfType<LilySharp.Core.Svg.Model.NoteItem>()
                .Select(n => (n.StringNumber, Tunings.CalculateFret(n.Midi + shift, Tunings.Bass, n.StringNumber ?? 0).fret))
                .ToArray())
            .ToArray();
    }

    /// <summary>The owner's stated fingerings (TabFingeringExpectationTests, TabStringNumberTests)
    /// this weight vector breaks, by name.</summary>
    internal static List<string> Violations(double[] w)
    {
        var broken = new List<string>();
        BassFingeringPlanner.t_weightsForMeasurement = ToWeights(w);
        try
        {
            foreach (var c in TabFingeringExpectationTests.Cases.Where(c => !c.Known))
            {
                var (_, s, f) = TabFingeringExpectationTests.Resolve(c);
                if (s != c.String || f != c.Fret) broken.Add(c.ToString());
            }
            void Check(string name, string body, int bar, (int?, int)[] expected)
            {
                var got = BassTabFrets(body)[bar];
                if (!got.SequenceEqual(expected)) broken.Add(name + " → " + string.Join(" ", got.Select(g => $"{g.Item1}/{g.Item2}")));
            }
            Check("aes g g f f ees", "aes'4 g8 g f f ees4 | bes c8 c d f ees4 |", 0, [(1, 1), (1, 0), (1, 0), (2, 3), (2, 3), (2, 1)]);
            Check("bes c c d f ees", "aes'4 g8 g f f ees4 | bes c8 c d f ees4 |", 1, [(3, 1), (3, 3), (3, 3), (2, 0), (2, 3), (2, 1)]);
            Check("leap frees", "a'4\\4 a, b r |", 0, [(4, 17), (3, 0), (3, 2)]);
            Check("rests free", "a'4\\4 r r r | r1 | e r r r |", 2, [(3, 7)]);
            Check("slur keeps (no slur)", "a'4\\3 c r r |", 0, [(3, 12), (2, 10)]);
            Check("slur keeps", "a'4\\3( c) r r |", 0, [(3, 12), (3, 15)]);
            Check("no fall", "a'2\\3 e |", 0, [(3, 12), (4, 12)]);
            Check("fall frees", "a'2\\3@fall e |", 0, [(3, 12), (2, 2)]);
            // As TabStringNumberTests.BarePitch_… reads it: a plain bass-clef part on a bass tab.
            {
                var src = "part bl { clef bass }\nsection Main {\n  bl {\n a4\\4 b4 |\n  }\n}\n" +
                          "form { Main }\nscore x { staff bass bl tab bass bl }\n";
                var tree = LilySharp.Core.Syntax.SyntaxTree.Parse(src);
                var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree)!;
                var tab = new LilySharp.Core.Svg.Collector.MeasureCollector().CollectMultiStaff(tree, spec)
                    .EnumerateStaves().First(s => s.Staff.IsTab).Staff;
                var notes = tab.PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<LilySharp.Core.Svg.Model.NoteItem>().ToList();
                if (notes[1].StringNumber != 3) broken.Add("a\\4 b low");
            }
            Check("D A: not two open strings", "d4 a r2 |", 0, [(3, 5), (4, 5)]);
            Check("A E: stopped then open", "a4 e r2 |", 0, [(4, 5), (4, 0)]);
        }
        finally { BassFingeringPlanner.t_weightsForMeasurement = null; }
        return broken;
    }

    /// <summary>How often a plan plays an open string and then an open LOWER string, over the corpus:
    /// the frozen planner's, the lab's and the owner's own.</summary>
    [Fact]
    public void OpenThenLowerOpenCount()
    {
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_OTLO");
        if (string.IsNullOrEmpty(start)) return;
        var w = Load(start);
        int core = 0, lab = 0, owner = 0;
        foreach (var b in TabCorpusSearchTests.Books())
        {
            int n = b.Tuning.Length;
            int Count(int[] s)
            {
                int c = 0;
                for (int i = 1; i < s.Length; i++)
                {
                    if (s[i] == 0 || s[i - 1] == 0 || b.Events[i].HandFree) continue;
                    bool o0 = b.Events[i - 1].Midi == b.Tuning[n - s[i - 1]], o1 = b.Events[i].Midi == b.Tuning[n - s[i]];
                    if (o0 && o1 && s[i] > s[i - 1]) c++;
                }
                return c;
            }
            core += Count(BassFingeringPlanner.Plan(b.Events, b.Tuning));
            lab += Count(Plan(b.Events, b.Tuning, w));
            owner += Count(b.Expected);
        }
        File.WriteAllText(TabCorpusSearchTests.Report("lab-otlo"), $"open then open lower string — frozen planner {core}, lab {lab}, owner {owner}");
    }

    [Fact]
    public void KnownCases()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_KNOWN"))) return;
        var lines = TabFingeringExpectationTests.Cases.Where(c => c.Known).Select(c =>
        {
            var (_, s, f) = TabFingeringExpectationTests.Resolve(c);
            return $"{c}: want {c.String}/{c.Fret}, got {s}/{f}";
        });
        File.WriteAllText(TabCorpusSearchTests.Report("lab-known"), string.Join("\n", lines));
    }

    [Fact]
    public void ShowViolations()
    {
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_VIOL");
        if (string.IsNullOrEmpty(start)) return;
        var w = Load(start);
        File.WriteAllText(TabCorpusSearchTests.Report("lab-viol"), string.Join("\n", Violations(w)));
    }

    /// <summary>The search again, now for the fewest written strings over the training books.</summary>
    [Fact]
    public void SearchPins()
    {
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_PINSEARCH");
        if (string.IsNullOrEmpty(start)) return;
        var w = Load(start);
        var books = TabCorpusSearchTests.Books();
        var train = books.Where((_, i) => i % 2 == 0).ToList();
        var test = books.Where((_, i) => i % 2 == 1).ToList();
        int penalty = int.TryParse(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_PENALTY"), out var pen) ? pen : 0;
        int Eval(double[] x, List<TabCorpusSearchTests.BookData> set)
        {
            int total = penalty == 0 || set != train ? 0 : penalty * Violations(x).Count;
            System.Threading.Tasks.Parallel.ForEach(set, b =>
            {
                int p = PinsNeeded(b, e => Plan(e, b.Tuning, x));
                System.Threading.Interlocked.Add(ref total, p);
            });
            return total;
        }
        var log = new StringBuilder();
        int best = Eval(w, train);
        log.AppendLine($"start train pins {best} test pins {Eval(w, test)}");
        double[] mult = [0, 0.5, 1.5];
        double[] add = [-0.3, -0.1, 0.1, 0.3];
        for (int round = 0; round < 3; round++)
        {
            bool moved = false;
            for (int k = 0; k < w.Length; k++)
            {
                if (Names[k] == "openThenLower") continue;   // the owner ruled it out (2026-10-25)
                double v0 = w[k];
                var tries = Names[k] == "span" ? new[] { 2.0, 3, 4 }
                    : mult.Select(m => v0 * m).Concat(add.Select(a => v0 + a)).Distinct().Where(v => v != v0
                        && (Names[k].StartsWith("pos") || Names[k].StartsWith("str") || (Names[k].StartsWith("open") && char.IsDigit(Names[k][^1])) || v >= 0));
                foreach (var v in tries)
                {
                    var cand = (double[])w.Clone();
                    cand[k] = v;
                    int pins = Eval(cand, train);
                    if (pins < best) { best = pins; w = cand; moved = true; log.AppendLine($"r{round} {Names[k]}={v:G4} train pins {best}"); }
                }
                File.WriteAllText(TabCorpusSearchTests.Report("lab-pinsearch"), log.ToString());
                File.WriteAllText(TabCorpusSearchTests.Report("lab-pinweights"), string.Join(",", w.Select(x => x.ToString("R"))));
            }
            if (!moved) break;
        }
        log.AppendLine($"final train pins {best} test pins {Eval(w, test)}");
        for (int k = 0; k < w.Length; k++) log.AppendLine($"  {Names[k]} = {w[k]:G4}");
        File.WriteAllText(TabCorpusSearchTests.Report("lab-pinsearch"), log.ToString());
        File.WriteAllText(TabCorpusSearchTests.Report("lab-pinweights"), string.Join(",", w.Select(x => x.ToString("R"))));
    }

    [Fact]
    public void Pins()
    {
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_PINS");
        if (string.IsNullOrEmpty(start)) return;
        var w = Load(start);
        var books = TabCorpusSearchTests.Books();
        int notes = books.Sum(b => b.Expected.Count(e => e != 0));
        var sb = new StringBuilder();
        sb.AppendLine($"scored notes {notes}; LilyPond's own books write {books.Sum(b => 0)} (see notes.tsv)");
        int lab = 0, current = 0, lilyPond = 0, core = 0;
        var acc = TabCorpusSearchTests.Score(books, b => BassFingeringPlanner.Plan(b.Events, b.Tuning));
        var accW = TabCorpusSearchTests.Score(books.Select(b => b with { Expected = b.Expected.Select((e, i) => e).ToArray() }), b => BassFingeringPlanner.Plan(b.Events, b.Tuning));
        foreach (var b in books)
        {
            int pl = PinsNeeded(b, e => Plan(e, b.Tuning, w));
            int pk = PinsNeeded(b, e => BassFingeringPlanner.Plan(e, b.Tuning));
            int pc = PinsNeeded(b, e => TabFingeringPlanner.Plan(e, b.Tuning, b.HandSpan));
            int pd = PinsNeeded(b, e => e.Select(x => x.FixedString > 0 ? x.FixedString : TabCorpusMeasureTests.LilyPondDefault(x.Midi, b.Tuning)).ToArray());
            lab += pl; current += pc; lilyPond += pd; core += pk;
            sb.AppendLine($"{pk,5} {pl,5} {pc,5} {pd,5}  {b.Name}");
        }
        sb.Insert(0, $"pins needed — core bass planner {core}, lab {lab}, current planner {current}, LilyPond's rule {lilyPond}\n" +
            $"core bass planner note agreement {100.0 * acc.Ok / acc.Total:F2}% ({acc.Ok}/{acc.Total})\n");
        File.WriteAllText(TabCorpusSearchTests.Report("lab-pins"), sb.ToString());
    }

    /// <summary>Where the owner's own fingering disagrees with itself: a phrase of eight or more
    /// notes that comes back with the same pitches and different strings, and fast jumps.</summary>
    [Fact]
    public void Inconsistencies()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_INCONS"))) return;
        const int Len = 8;
        var sb = new StringBuilder();
        int phrases = 0, booksWith = 0;
        var jumps = new StringBuilder();
        int jumpCount = 0;
        foreach (var b in TabCorpusSearchTests.Books())
        {
            int n = b.Tuning.Length;
            var seen = new Dictionary<string, int>();
            var reported = new HashSet<string>();
            int here = 0;
            for (int i = 0; i + Len <= b.Events.Length; i++)
            {
                bool scored = true;
                for (int k = i; k < i + Len; k++) if (b.Expected[k] == 0) { scored = false; break; }
                if (!scored) continue;
                string key = string.Join(",", Enumerable.Range(i, Len).Select(k => b.Events[k].Midi));
                if (!seen.TryGetValue(key, out int first)) { seen[key] = i; continue; }
                bool differs = false;
                for (int k = 0; k < Len; k++) if (b.Expected[first + k] != b.Expected[i + k]) { differs = true; break; }
                if (!differs) continue;
                string tag = key + "@" + b.Measure[first];
                if (!reported.Add(tag)) continue;
                here++;
                string Fing(int at) => string.Join(" ", Enumerable.Range(at, Len).Select(k =>
                {
                    int midi = b.Events[k].Midi, s = b.Expected[k];
                    return $"{NoteNames[midi % 12]}{midi / 12 - 1}:{s}/{midi - b.Tuning[n - s]}";
                }));
                sb.AppendLine($"{b.Name}: bar {b.Measure[first] + 1} [{Fing(first)}]  vs  bar {b.Measure[i] + 1} [{Fing(i)}]");
            }
            if (here > 0) { booksWith++; phrases += here; }

            for (int i = 1; i < b.Events.Length; i++)
            {
                if (b.Expected[i] == 0 || b.Expected[i - 1] == 0 || b.Events[i].HandFree) continue;
                int f0 = b.Events[i - 1].Midi - b.Tuning[n - b.Expected[i - 1]];
                int f1 = b.Events[i].Midi - b.Tuning[n - b.Expected[i]];
                if (f0 == 0 || f1 == 0 || Math.Abs(f1 - f0) < 5 || b.Events[i].TimeToMove > 0.125) continue;
                // Could the same two pitches be played closer together?
                int best = int.MaxValue;
                for (int s0 = 1; s0 <= n; s0++)
                    for (int s1 = 1; s1 <= n; s1++)
                    {
                        int a = b.Events[i - 1].Midi - b.Tuning[n - s0], c = b.Events[i].Midi - b.Tuning[n - s1];
                        if (a < 0 || c < 0) continue;
                        int d = a == 0 || c == 0 ? 0 : Math.Abs(a - c);
                        best = Math.Min(best, d);
                    }
                if (best >= Math.Abs(f1 - f0)) continue;
                jumpCount++;
                int m0 = b.Events[i - 1].Midi, m1 = b.Events[i].Midi;
                jumps.AppendLine($"{b.Name}: bar {b.Measure[i] + 1} {NoteNames[m0 % 12]}{m0 / 12 - 1} {b.Expected[i - 1]}/{f0} -> {NoteNames[m1 % 12]}{m1 / 12 - 1} {b.Expected[i]}/{f1} in 1/{Math.Round(1 / b.Events[i].TimeToMove)} (closest pair {best} frets apart)");
            }
        }
        File.WriteAllText(TabCorpusSearchTests.Report("lab-incons"),
            $"phrases fingered two ways: {phrases} in {booksWith} books\nfast jumps of 5+ frets with a closer choice: {jumpCount}\n\n{sb}\n\n{jumps}");
    }

    private static readonly string[] NoteNames = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    /// <summary>Every book's misses under the weights in LILYSHARP_TABCORPUS_LAB_START, bar by bar:
    /// pitch(duration) expected string/fret, then a star and the planner's when they differ.</summary>
    [Fact]
    public void Show()
    {
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_SHOW");
        if (string.IsNullOrEmpty(start)) return;
        var w = Load(start);
        var sb = new StringBuilder();
        var books = TabCorpusSearchTests.Books();
        var rows = new List<(string Book, int Miss, int Total)>();
        foreach (var b in books)
        {
            var got = Plan(b.Events, b.Tuning, w);
            int miss = 0, total = 0;
            for (int i = 0; i < got.Length; i++) if (b.Expected[i] != 0) { total++; if (got[i] != b.Expected[i]) miss++; }
            rows.Add((b.Name, miss, total));
            sb.AppendLine($"=== {b.Name}  miss {miss}/{total}");
            int n = b.Tuning.Length;
            foreach (var bar in Enumerable.Range(0, got.Length).GroupBy(i => b.Measure[i]))
            {
                if (!bar.Any(i => b.Expected[i] != 0 && got[i] != b.Expected[i])) { sb.AppendLine($"{bar.Key,4}: ok"); continue; }
                var cells = bar.Select(i =>
                {
                    int midi = b.Events[i].Midi;
                    string nm = NoteNames[midi % 12] + (midi / 12 - 1);
                    string dur = b.Duration[i] >= 1 ? "1" : "1/" + Math.Round(1 / b.Duration[i]);
                    int e = b.Expected[i], g = got[i];
                    string fe = e == 0 ? "?" : $"{e}/{midi - b.Tuning[n - e]}";
                    string fg = $"{g}/{midi - b.Tuning[n - g]}";
                    string pre = (b.Events[i].HandFree ? "_" : "") + (b.Events[i].TiedFromPrevious ? "~" : "");
                    return e != 0 && e != g ? $"{pre}{nm}({dur}) {fe}*{fg}" : $"{pre}{nm}({dur}) {fe}";
                });
                sb.AppendLine($"{bar.Key,4}: {string.Join(" | ", cells)}");
            }
        }
        var head = string.Join("\n", rows.OrderByDescending(r => r.Miss).Select(r => $"{r.Miss,4}/{r.Total,4} {r.Book}"));
        File.WriteAllText(TabCorpusSearchTests.Report("lab-show"), head + "\n\n" + sb);
    }

    [Fact]
    public void Search()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB"))) return;
        var books = TabCorpusSearchTests.Books();
        var train = books.Where((_, i) => i % 2 == 0).ToList();
        var test = books.Where((_, i) => i % 2 == 1).ToList();
        var log = new StringBuilder();
        var w = Initial();
        var start = Environment.GetEnvironmentVariable("LILYSHARP_TABCORPUS_LAB_START");
        if (!string.IsNullOrEmpty(start) && File.Exists(start))
            w = Load(start);
        double Eval(double[] x, List<TabCorpusSearchTests.BookData> set)
        {
            var r = TabCorpusSearchTests.Score(set, b => Plan(b.Events, b.Tuning, x));
            return (double)r.Ok / r.Total;
        }
        string Path1(string n) => TabCorpusSearchTests.Report(n);
        double best = Eval(w, train);
        log.AppendLine($"start train {best:P2} test {Eval(w, test):P2}");
        double[] mult = [0, 0.3, 0.6, 1.5, 3];
        double[] add = [-1, -0.3, -0.1, 0.1, 0.3, 1];
        for (int round = 0; round < 6; round++)
        {
            bool moved = false;
            for (int k = 0; k < w.Length; k++)
            {
                if (Names[k] == "openThenLower") continue;   // the owner ruled it out (2026-10-25)
                double v0 = w[k];
                var tries = Names[k] == "span" ? new[] { 2.0, 3, 4 }
                    : mult.Select(m => v0 * m).Concat(add.Select(a => v0 + a)).Distinct().Where(v => v != v0
                        && (Names[k].StartsWith("pos") || Names[k].StartsWith("str") || (Names[k].StartsWith("open") && char.IsDigit(Names[k][^1])) || v >= 0));
                foreach (var v in tries)
                {
                    var cand = (double[])w.Clone();
                    cand[k] = v;
                    double acc = Eval(cand, train);
                    if (acc > best + 1e-6) { best = acc; w = cand; moved = true; log.AppendLine($"r{round} {Names[k]}={v:G4} train {best:P2}"); }
                }
            }
            File.WriteAllText(Path1("lab"), log.ToString());
            File.WriteAllText(Path1("lab-weights"), string.Join(",", w.Select(x => x.ToString("R"))));
            if (!moved) break;
        }
        log.AppendLine($"final train {best:P2} test {Eval(w, test):P2}");
        for (int k = 0; k < w.Length; k++) log.AppendLine($"  {Names[k]} = {w[k]:G4}");
        File.WriteAllText(Path1("lab"), log.ToString());
        File.WriteAllText(Path1("lab-weights"), string.Join(",", w.Select(x => x.ToString("R"))));
    }
}
