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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// What a new piece's notation leaves flat, said for the AI that wrote it to revise (owner's
/// report 2026-10-26: a "dramatic string quartet" came back as four piano staves of whole notes
/// and scales, every part in the same rhythm). Not errors — a piece may mean any of it — but
/// the commonest ways a composed piece falls short of sounding like music for its players:
/// parts that play a piano for want of an <c>instrument</c>, an ensemble braced as one piano,
/// a part of one note value, and parts that all move together.
/// </summary>
public static class CompositionChecks
{
    /// <summary>The findings for <paramref name="source"/>, each one sentence; empty when none.</summary>
    public static IReadOnlyList<string> Of(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return [];
        var root = tree.GetRoot();
        var findings = new List<string>();
        var declared = RenderSpecParser.ChooseDeclared(tree, null);

        // A piece has a title of its own (owner's request 2026-10-27).
        if (TitleOf(source) is not { } title)
            findings.Add("The piece has no title: give it a title of its own that fits its music, a `title \"…\"` line at the top.");
        else if (IsPlaceholderTitle(title))
            findings.Add($"\"{title}\" is a placeholder, not a title: give the piece a title of its own that fits its music.");

        // An ensemble braced as one piano.
        var braced = new HashSet<string>(System.StringComparer.Ordinal);
        if (declared is { } d)
            foreach (var grand in d.Declaration.DescendantNodes<GrandStaffRenderSyntax>())
            {
                // One node for the three groups; the keyword says which (staffGroup and
                // choirStaff are an ensemble's brackets).
                if (grand.GrandStaffKeyword.Text != "grandStaff")
                    continue;
                var staves = grand.DescendantNodes<StaffRenderSyntax>().Select(s => s.PartToken?.Text).OfType<string>().ToList();
                // A piano's two hands need no instrument; four players braced as one still do.
                if (staves.Count <= 2)
                    braced.UnionWith(staves);
                if (staves.Count > 2)
                    findings.Add($"The score puts {staves.Count} staves ({string.Join(", ", staves)}) in one grandStaff, the brace of ONE "
                        + "instrument on two staves (a piano): an ensemble is a staffGroup { … }.");
            }

        // A part with no instrument plays a piano — fine for a piano's two hands, not for a player of something else.
        var parts = root.DescendantNodes<PartDeclarationSyntax>().GroupBy(p => p.Name.Text).Select(g => g.First()).ToList();
        if (parts.Count >= 2)
        {
            var bare = parts.Where(p => !braced.Contains(p.Name.Text) && !p.Properties.Any(prop =>
                prop.NameToken.Text is "instrument" or "midiInstrument")).Select(p => p.Name.Text).ToList();
            if (bare.Count > 0)
                findings.Add($"No instrument for {string.Join(", ", bare)}: a part without `instrument` sounds as a piano "
                    + "(write `instrument violin`, `viola`, `cello`, … in its part declaration).");
        }

        MidiFile midi;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            return findings;
        }
        int tpq = midi.TicksPerQuarterNote;
        // Each part's onsets (a chord one onset) with its duration.
        var byPart = midi.Tracks.SelectMany(t => t.Notes.Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.DurationTicks, n.Channel)))
            .Where(n => n.Channel != 9 && !n.Part.EndsWith(" (chords)", System.StringComparison.Ordinal))
            .GroupBy(n => n.Part)
            .ToDictionary(g => g.Key, g => g.GroupBy(n => n.StartTick).Select(c => (Start: c.Key, Length: c.Max(n => n.DurationTicks))).OrderBy(o => o.Start).ToList());

        // A part of one note value.
        foreach (var (part, onsets) in byPart)
        {
            if (onsets.Count < 16)
                continue;
            // A note's value as it reads: the distance to the next onset (a staccato quarter sounds
            // shorter than it is written), the last note its own length.
            var values = onsets.Select((o, i) => Value(i + 1 < onsets.Count ? onsets[i + 1].Start - o.Start : o.Length, tpq));
            var common = values.GroupBy(v => v).OrderByDescending(g => g.Count()).First();
            if (common.Count() >= onsets.Count * 0.8)
                findings.Add($"{part}: {common.Count() * 100 / onsets.Count}% of its notes are {common.Key}s — mix long and short notes, "
                    + "rests, dotted rhythms; give the line a rhythm of its own.");
        }

        // Every part in the same rhythm, bar after bar.
        var bars = midi.BarTicks.ToList();
        if (byPart.Count >= 3 && bars.Count >= 4)
        {
            int together = 0, counted = 0;
            for (int b = 0; b + 1 < bars.Count; b++)
            {
                var rhythms = byPart.Values
                    .Select(o => string.Join(",", o.Where(x => x.Start >= bars[b] && x.Start < bars[b + 1]).Select(x => x.Start - bars[b])))
                    .Where(r => r.Length > 0).ToList();
                if (rhythms.Count < 3)
                    continue;
                counted++;
                if (rhythms.Distinct().Count() == 1)
                    together++;
            }
            if (counted >= 4 && together >= counted * 0.6)
                findings.Add($"In {together} of {counted} bars every part moves in the same rhythm: let one voice carry the melody "
                    + "while the others accompany (broken chords, repeated notes, a moving bass), and pass the roles between them.");
        }
        return findings;
    }

    /// <summary>The file's top-level <c>title "…"</c>, or null when it writes none (or an empty one).</summary>
    public static string? TitleOf(string source)
    {
        var m = System.Text.RegularExpressions.Regex.Match(source, @"^[ \t]*title[ \t]+""((?:[^""\\]|\\.)*)""",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>A title that names no piece: a template's or a tool's placeholder.</summary>
    public static bool IsPlaceholderTitle(string title)
        => System.Text.RegularExpressions.Regex.IsMatch(title.Trim(),
            @"^(new piece|new score|untitled|title|piece|my piece|song|composition|無題|新しい曲|タイトル)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // A duration's name, for the sentence ("whole", "quarter", …), else its quarters.
    private static string Value(int ticks, int tpq)
    {
        double q = ticks / (double)tpq;
        return q switch
        {
            >= 3.9 and <= 4.1 => "whole note",
            >= 2.9 and <= 3.1 => "dotted half note",
            >= 1.9 and <= 2.1 => "half note",
            >= 1.4 and <= 1.6 => "dotted quarter note",
            >= 0.9 and <= 1.1 => "quarter note",
            >= 0.45 and <= 0.55 => "eighth note",
            >= 0.2 and <= 0.3 => "sixteenth note",
            _ => $"{q:0.##}-quarter note",
        };
    }
}
