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
using System.Text;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg;

/// <summary>
/// A plain-text summary of the engine's LAYOUT decisions — system/line breaks, page
/// grouping, and bars per system — for a score. The point is to expose what a source
/// file does NOT reveal: where the line breaker actually split the music, how many
/// systems/pages resulted, and how densely the bars are packed. An AI (or a human)
/// generating Lily# can read this to verify the layout came out as intended without
/// rendering an image. Notes/pitches are deliberately NOT echoed — `check --pitches`
/// already shows the resolved pitch chain; this view is purely about layout.
/// </summary>
public static class LayoutReport
{
    /// <summary>
    /// Builds the layout report for the first render block in <paramref name="tree"/>
    /// (or, with <paramref name="allScores"/>, every block — matching the
    /// first-by-default / <c>--all</c> convention of the export commands). Falls back
    /// to the implicit default score when none is declared.
    /// </summary>
    public static string Generate(SyntaxTree tree, bool allScores = false)
    {
        var sb = new StringBuilder();
        var specs = RenderSpecParser.FindAll(tree);

        if (specs.Count == 0)
        {
            AppendScore(sb, tree, null);
            return sb.ToString();
        }

        int count = allScores ? specs.Count : 1;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                sb.AppendLine();
            AppendScore(sb, tree, specs[i]);
        }
        return sb.ToString();
    }

    private static void AppendScore(StringBuilder sb, SyntaxTree tree, RenderSpec? spec)
    {
        var score = SvgGenerator.CollectScore(tree, spec);
        var layout = new LayoutEngine().Layout(score);

        string name = ScoreName(spec);
        sb.Append("score");
        if (name.Length > 0)
            sb.Append(" \"").Append(name).Append('"');
        sb.AppendLine();

        var staves = score.EnumerateStaves()
            .Select(t => StaffLabel(t.Staff))
            .ToList();
        int totalBars = layout.AllSystems.Sum(s => s.Measures.Length);

        // No page count: the engine lays a score out as one continuous flow (SVG/PNG
        // are a single tall image; PDF auto-sizes one page), so there is no printed-page
        // concept to report — only systems (lines) and where they break.
        sb.Append("  staves: ")
            .Append(staves.Count > 0 ? string.Join(", ", staves) : "(none)")
            .Append("  |  time ").Append(TimeField(score))
            .Append("  |  ").Append(Count(layout.SystemCount, "system"))
            .Append(", ").Append(Count(totalBars, "bar"))
            .AppendLine();

        foreach (var sys in layout.AllSystems)
            AppendSystem(sb, "  ", sys);

        // Where the line breaker split the music: the last bar of every system but the
        // final one. This is the headline answer to "where did it break?".
        var breaks = new List<int>();
        for (int i = 0; i < layout.AllSystems.Length - 1; i++)
        {
            var ms = layout.AllSystems[i].Measures;
            if (!ms.IsDefaultOrEmpty)
                breaks.Add(ms[^1].MeasureIndex + 1);
        }
        sb.Append("  line breaks after bar: ")
            .AppendLine(breaks.Count > 0 ? string.Join(", ", breaks) : "(none, single system)");

        // Hara-kiri: a staff auto-hidden in a system (empty there). An unexpected hide
        // is a real layout surprise, so call it out.
        var hidden = HiddenStaves(layout);
        if (hidden.Count > 0)
            sb.Append("  hidden (empty) staves: ").AppendLine(string.Join("; ", hidden));
    }

    private static void AppendSystem(StringBuilder sb, string indent, SystemLayout sys)
    {
        sb.Append(indent).Append("system ").Append(sys.SystemIndex + 1).Append(": ");
        if (sys.Measures.IsDefaultOrEmpty)
        {
            sb.AppendLine(" (empty)");
            return;
        }
        int first = sys.Measures[0].MeasureIndex + 1;
        int last = sys.Measures[^1].MeasureIndex + 1;
        int count = sys.Measures.Length;
        string range = first == last ? $"bar {first}" : $"bars {first}-{last}";
        sb.Append(range.PadRight(14)).Append('(').Append(count).AppendLine(count == 1 ? " bar)" : " bars)");
    }

    private static List<string> HiddenStaves(ScoreLayout layout)
    {
        var result = new List<string>();
        foreach (var sys in layout.AllSystems)
        {
            if (sys.StaffGroups.IsDefaultOrEmpty)
                continue;
            foreach (var group in sys.StaffGroups)
                foreach (var st in group.Staves)
                    if (st.IsHidden)
                        result.Add($"staff {st.StaffIndex + 1} in system {sys.SystemIndex + 1}");
        }
        return result;
    }

    /// <summary>
    /// The score's meter, plus any mid-piece changes with the bar they take effect —
    /// e.g. "4/4 -> 3/4 (bar 2) -> 6/8 (bar 3)". The starting meter alone hides a
    /// change the source makes, so the report would otherwise misreport the meter.
    /// </summary>
    private static string TimeField(MultiStaffScore score)
    {
        string initial = score.TimeSignature.ToString();
        var measures = score.PrimaryContentStaff.PrimaryVoice.Measures;
        var changes = new List<string>();
        for (int i = 0; i < measures.Length; i++)
            foreach (var item in measures[i].Items)
                if (item is TimeSignatureChangeItem ts)
                    changes.Add($"{ts.NewTime} (bar {i + 1})");

        return changes.Count == 0 ? initial : initial + " -> " + string.Join(" -> ", changes);
    }

    private static string ScoreName(RenderSpec? spec)
    {
        if (spec is null || string.IsNullOrEmpty(spec.OutputFile))
            return "";
        return Path.GetFileNameWithoutExtension(spec.OutputFile);
    }

    private static string StaffLabel(Staff staff)
    {
        string clef = staff.Clef switch
        {
            ClefType.Treble => "treble",
            ClefType.Bass => "bass",
            ClefType.Alto => "alto",
            ClefType.Tenor => "tenor",
            ClefType.Treble8Below => "treble_8",
            ClefType.Tab => "tab",
            _ => staff.Clef.ToString().ToLowerInvariant(),
        };
        return string.IsNullOrEmpty(staff.InstrumentName) ? clef : $"{staff.InstrumentName} ({clef})";
    }

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
