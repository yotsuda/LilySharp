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
using System.Text.RegularExpressions;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The fingerings the user asked for, passage by passage, while the tab string chooser was
/// being designed (2026-09-14) — held against the real books they were asked about.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN. The books are byte copies of the user's corpus in <c>audit/tabfingering/</c>
/// (the corpus itself is edited by its owner and is never a test input). Each case names a
/// note by where a reader finds it — a section or a rehearsal mark, the bar counted by bar
/// lines in the source, and the note's place among the pitches of that bar — and checks its
/// written pitch before its string and fret, so a book edit cannot silently point the case
/// at another note.
/// <para>
/// <c>Known</c> marks what the current chooser still gets wrong. Those cases do not fail: they
/// are the yardstick for the next chooser (a dynamic programme over the phrase), which should
/// turn them into ordinary cases without breaking the rest.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabFingeringExpectationTests
{
    /// <param name="Book">File name in <c>audit/tabfingering/</c>.</param>
    /// <param name="Anchor"><c>section NAME</c> or <c>mark NAME</c> (the first such mark).</param>
    /// <param name="Bar">Bar number from the anchor, counted by <c>|</c> in the source.</param>
    /// <param name="Note">The note's place (1-based) among the pitches of that bar.</param>
    /// <param name="Pitch">The note's pitch name as written, octave marks excluded.</param>
    /// <param name="String">Expected string (1 = highest).</param>
    /// <param name="Fret">Expected fret.</param>
    /// <param name="Known">The current chooser does not meet this yet.</param>
    /// <param name="Why">The user's words, shortened.</param>
    public sealed record Case(string Book, string Anchor, int Bar, int Note, string Pitch,
        int String, int Fret, bool Known, string Why)
    {
        public override string ToString() => $"{Book} {Anchor} bar {Bar} note {Note} ({Pitch})";
    }

    public static readonly Case[] Cases =
    [
        new("tab-fret.lys", "section A", 1, 6, "ees", 2, 1, false,
            "f with the little finger on the 3rd, so the index stays at the 1st"),
        new("tab-fret.lys", "section B", 2, 1, "bes", 4, 6, false, "little finger on the 4th string's 6th"),
        new("tab-fret.lys", "section B", 2, 4, "g", 4, 3, false, "index on the 4th string's 3rd"),
        new("tab-fret.lys", "section B", 3, 1, "c", 3, 3, false, "index on the 3rd string's 3rd"),
        new("tab-fret.lys", "section B", 4, 1, "ees", 3, 6, false,
            "a stretch to the 6th is cheaper than moving two frets"),
        new("tab-fret.lys", "section B", 6, 4, "g", 2, 5, true, "the 2nd string's 5th, not the open 1st"),
        new("arthurs-theme.lys", "section A", 5, 1, "bes", 3, 1, false,
            "a stretch across a skipped string is hard"),
        new("arthurs-theme.lys", "section E", 2, 2, "d", 3, 5, false, "no skip from the 2nd to the 4th string"),
        new("amanda.lys", "section A2", 7, 2, "a", 2, 7, true, "the first a on the 2nd string's 7th"),
        new("amanda.lys", "section A2", 7, 6, "a", 2, 7, false, "the second a on the 2nd string's 7th"),
        new("amanda.lys", "section A3", 1, 2, "g", 2, 5, false, "octave shape: index on the root, little finger an octave up"),
        new("amanda.lys", "section B1", 1, 1, "e", 2, 2, false, "down two frets rather than a stretch"),
        new("amanda.lys", "section B1", 3, 1, "e", 2, 2, false, "down two frets rather than a stretch"),
        new("real-gone.lys", "section Intro", 4, 1, "b", 3, 2, false, "the slur keeps the 3rd string"),
        new("real-gone.lys", "section C", 4, 5, "a", 3, 0, false, "the open 3rd string"),
        new("real-gone.lys", "section D", 5, 1, "b", 3, 2, false, "the hand is free after a fall"),
        new("real-gone.lys", "section F", 1, 1, "b", 3, 2, false, "the 3rd string's 2nd after a bar of open strings"),
        new("sayonara-elegy.lys", "mark C", 3, 4, "f", 4, 1, false, "the 4th string's 1st"),
        new("sayonara-elegy.lys", "mark D", 7, 1, "aes", 4, 4, false, "down to the 4th string's 4th"),
        new("automatic.lys", "section A2", 8, 3, "f", 4, 1, false, "e f ges g up the 4th string"),
        new("nine-to-five-xanadu.lys", "section A", 6, 2, "a", 2, 7, true, "unplayable unless a is on the 2nd string"),
    ];

    public static IEnumerable<object[]> AgreedCases() =>
        Cases.Where(c => !c.Known).Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AgreedCases))]
    public void TheChooserPlaysTheAgreedFingering(Case expected)
    {
        var (writtenPitch, stringNum, fret) = Resolve(expected);
        Assert.Equal(expected.Pitch, writtenPitch);
        Assert.True(stringNum == expected.String && fret == expected.Fret,
            $"{expected}: expected string {expected.String} fret {expected.Fret}, got string {stringNum} fret {fret} — {expected.Why}");
    }

    /// <summary>The known misses still resolve to a note of the right pitch — so each stays a
    /// meaningful yardstick — whether or not the chooser meets them today.</summary>
    [Fact]
    public void TheKnownMissesStillPointAtTheirNotes()
    {
        foreach (var c in Cases.Where(c => c.Known))
            Assert.Equal(c.Pitch, Resolve(c).WrittenPitch);
    }

    private static (string WrittenPitch, int String, int Fret) Resolve(Case c)
    {
        var path = Path.Combine(CollectResumeTests.FindRepoRoot(), "audit", "tabfingering", c.Book);
        var source = File.ReadAllText(path);
        int position = NotePosition(source, c, out var writtenPitch);

        var tree = SyntaxTree.Parse(source);
        foreach (var spec in RenderSpecParser.FindAll(tree))
        {
            var multi = new MeasureCollector().CollectMultiStaff(tree, spec);
            var tab = multi.EnumerateStaves().Select(s => s.Staff).FirstOrDefault(s => s.IsTab && s.Tuning.HasValue);
            if (tab is null) continue;

            var note = tab.PrimaryVoice.Measures.SelectMany(m => m.Items).OfType<NoteItem>()
                .FirstOrDefault(n => n.SourcePosition == position);
            Assert.True(note is not null, $"{c}: no tab note at source position {position}");
            Assert.True(note!.StringNumber.HasValue, $"{c}: the tab note has no string");

            int shift = Tunings.SoundingShift(tab.TabSourceClef, tab.Transposition);
            int fret = Tunings.CalculateFret(note.Midi + shift, Tunings.GetTuning(tab.Tuning!.Value),
                note.StringNumber!.Value).fret;
            return (writtenPitch, note.StringNumber.Value, fret);
        }
        throw new InvalidOperationException($"{c}: no score in {c.Book} has a tab staff");
    }

    // A pitch: a letter a–g with its -is/-es, not part of a word (break, grace, @fall, tuplet).
    private static readonly Regex PitchToken = new(@"(?<![A-Za-z@\\""])([a-g](?:is|es)*)(?=[,'\d\\.~()\s|}@\[\]]|$)");

    private static int NotePosition(string source, Case c, out string writtenPitch)
    {
        var parts = c.Anchor.Split(' ', 2);
        int barStart;
        if (parts[0] == "section")
        {
            var m = Regex.Match(source, @"section\s+" + Regex.Escape(parts[1]) + @"\s*\{");
            Assert.True(m.Success, $"{c}: section not found");
            barStart = m.Index + m.Length;
        }
        else
        {
            int markAt = source.IndexOf("@mark(\"" + parts[1] + "\")", StringComparison.Ordinal);
            Assert.True(markAt >= 0, $"{c}: mark not found");
            barStart = source.LastIndexOf('|', markAt) + 1;
        }

        for (int b = 1; b < c.Bar; b++)
            barStart = source.IndexOf('|', barStart) + 1;
        int barEnd = source.IndexOf('|', barStart);
        var bar = source.Substring(barStart, barEnd - barStart);

        var pitches = PitchToken.Matches(bar);
        Assert.True(pitches.Count >= c.Note, $"{c}: the bar has only {pitches.Count} pitches: {bar.Trim()}");
        var token = pitches[c.Note - 1];
        writtenPitch = token.Groups[1].Value;
        return barStart + token.Index;
    }
}
