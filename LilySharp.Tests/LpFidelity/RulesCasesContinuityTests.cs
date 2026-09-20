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
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Guards the split of <c>docs/RULES.md</c> into a rulebook and its worked examples
/// (<c>docs/RULES-CASES.md</c>): the seam between the two files, and the ceiling that keeps
/// the rulebook a thing a session can actually read at its start.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> RULES.md says in its own header that it was split out of HANDOFF.md
/// because, inside a 1.7 MB file, "it had never been read end to end". §0 then tells every
/// session to read it end to end. By session 431 it was 5,422 lines, §5.0 and §5.3 alone were
/// 56% of that and were case collections rather than rules, and the session that measured it
/// found it had read 860 lines — 15% — with the section §0 names four times (§5.5) at zero.
/// That is the same failure the split was supposed to fix, one size up. So session 433 moved
/// every worked example to RULES-CASES.md and left the bolded rule statement behind.
/// </para>
/// <para>
/// <b>Not a summary — a move.</b> Nothing was rewritten. Each rule keeps its own sentence
/// verbatim and ends with a marker; the text that follows it in the original sits under the
/// matching anchor in the case file. Concatenating the two reproduces the pre-split RULES.md
/// byte for byte, which is how the move was proved (SHA-256
/// 3E25095EC7978F5EAEDD88C69A48532D850FF9C265872502418C0901B7251C7E, the split and verify
/// scripts are in the lab at <c>sessions/p433/</c>). That proof cannot be re-run here — it
/// needs the pre-split file — so what is guarded instead is the seam it rests on.
/// </para>
/// <para>
/// <b>And a ceiling, because a rule with no instrument comes back.</b> That sentence is §5.0's
/// own, and this file is its application to §5.0. The rulebook grew back once already after
/// being split out of HANDOFF.md; a ceiling is what stops it happening a third time silently.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RulesCasesContinuityTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RulesCasesContinuityTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// A rule's pointer at the end of its statement: <c>（→CASES 5.0-001）</c>, or
    /// <c>（→CASES 5.0-001・続）</c> when the seam falls inside the line rather than at its end.
    /// </summary>
    /// <remarks>
    /// Anchored to end of line on purpose: the headers of both files quote the form as an
    /// example, mid-sentence and in backticks, and those quotations are documentation rather
    /// than pointers. Matching them would make the census disagree with itself.
    /// </remarks>
    private static readonly Regex Marker =
        new(@"（→CASES (?<id>[\d.]+-\d{3})(?:・続)?）\r?$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The case file's anchor for one moved example.</summary>
    private static readonly Regex Anchor =
        new(@"^### (?<id>[\d.]+-\d{3})\r?$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string DocsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "LilySharp.slnx")))
                return Path.Combine(dir, "docs");
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "LilySharp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string ReadDoc(string fileName)
    {
        var path = Path.Combine(DocsDir(), fileName);
        Assert.True(File.Exists(path), $"docs/{fileName} not found at {path}");
        return File.ReadAllText(path);
    }

    private static IReadOnlyList<string> IdsOf(Regex r, string text)
        => r.Matches(text).Select(m => m.Groups["id"].Value).ToArray();

    /// <summary>
    /// Every pointer finds its example and every example is pointed at. Either half failing
    /// means a piece of the split went missing — text with no way in, or a way in to nothing.
    /// </summary>
    [Fact]
    public void EveryRulePointsAtACaseAndEveryCaseIsPointedAt()
    {
        var markers = IdsOf(Marker, ReadDoc("RULES.md"));
        var anchors = IdsOf(Anchor, ReadDoc("RULES-CASES.md"));

        _output.WriteLine($"{markers.Count} markers in RULES.md, {anchors.Count} cases in RULES-CASES.md");

        var dupMarkers = markers.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        var dupAnchors = anchors.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(dupMarkers.Length == 0,
            "RULES.md points at the same case from more than one rule: "
            + string.Join(", ", dupMarkers)
            + ". Each case belongs to exactly one rule — that is what makes the two files "
            + "reconstruct the original. Give the second rule its own case.");
        Assert.True(dupAnchors.Length == 0,
            "RULES-CASES.md has two anchors with the same id: " + string.Join(", ", dupAnchors));

        var orphanRules = markers.Except(anchors).ToArray();
        var orphanCases = anchors.Except(markers).ToArray();
        Assert.True(orphanRules.Length == 0,
            "RULES.md points at cases that do not exist in RULES-CASES.md: "
            + string.Join(", ", orphanRules)
            + ". The example was deleted, or the anchor renamed. The text is still in the "
            + "history of docs/RULES-CASES.md at the commit that dropped it — recover it rather "
            + "than deleting the pointer, which is the only remaining sign that it existed.");
        Assert.True(orphanCases.Length == 0,
            "RULES-CASES.md holds cases nothing points at: " + string.Join(", ", orphanCases)
            + ". A case with no rule in front of it will never be read: the case file is an "
            + "appendix, entered from RULES.md. Either restore the rule that owned it or, if "
            + "the rule is genuinely gone, say so in RULES.md and keep the pointer.");
    }

    /// <summary>
    /// The cases stand in the same order as the rules that point at them. This is the whole
    /// navigation contract of the appendix — RULES.md carries no page numbers, so "same
    /// section, same order" is how a reader gets from one to the other by eye.
    /// </summary>
    [Fact]
    public void TheCasesStandInTheOrderTheRulesDo()
    {
        var markers = IdsOf(Marker, ReadDoc("RULES.md"));
        var anchors = IdsOf(Anchor, ReadDoc("RULES-CASES.md"));

        int firstDiff = -1;
        for (int i = 0; i < Math.Min(markers.Count, anchors.Count); i++)
            if (markers[i] != anchors[i]) { firstDiff = i; break; }

        Assert.True(firstDiff < 0,
            $"the {firstDiff + 1}th case is {anchors[Math.Max(firstDiff, 0)]} but the "
            + $"{firstDiff + 1}th rule points at {markers[Math.Max(firstDiff, 0)]}. "
            + "Cases are appended in the order their rules appear; when a rule moves, move its "
            + "case with it. Re-running tools in the lab (sessions/p433/) regenerates both.");
    }

    /// <summary>
    /// Every case id names a section that still exists in RULES.md, spelled the same way.
    /// </summary>
    /// <remarks>
    /// The section numbers are load-bearing outside these two files: code comments cite
    /// <c>§5.2</c> and <c>§5.2.1④</c> from 60 places in 35 files, which is why both headers say
    /// not to renumber. If a section is renumbered anyway, its cases are the first thing to
    /// become unreachable, and this says so in the commit that does it.
    /// </remarks>
    [Fact]
    public void EveryCaseNamesASectionThatStillExists()
    {
        string rules = ReadDoc("RULES.md");
        var sections = Regex.Matches(rules, @"^### (?<n>\d+\.\d+(?:\.\d+)?) ", RegexOptions.Multiline)
                            .Select(m => m.Groups["n"].Value)
                            .ToHashSet();

        var missing = IdsOf(Anchor, ReadDoc("RULES-CASES.md"))
            .Select(id => id.Substring(0, id.LastIndexOf('-')))
            .Distinct()
            .Where(s => !sections.Contains(s))
            .ToArray();

        _output.WriteLine($"sections in RULES.md: {string.Join(", ", sections.OrderBy(s => s))}");
        Assert.True(missing.Length == 0,
            "RULES-CASES.md has cases filed under sections RULES.md no longer has: "
            + string.Join(", ", missing)
            + ". Section numbers do not get renumbered — code comments cite them from 60 places "
            + "in 35 files. If a section was genuinely merged away, refile its cases under the "
            + "section that absorbed it.");
    }

    /// <summary>
    /// The rulebook stays a size a session can read at its start. The point of the split.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 194,528 bytes / 1,749 lines the day the split landed, down from 711,284 / 5,422. The
    /// ceilings leave room for roughly a hundred more rules, because a rule now costs its own
    /// statement and nothing else — the example it was born from goes to the appendix.
    /// </para>
    /// <para>
    /// <b>Raising a ceiling to get past it is the failure this test exists for.</b> When it
    /// trips, the question is which of the new text is a rule and which is the story of how the
    /// rule was learned; the second kind goes to RULES-CASES.md behind a marker. That is a
    /// five-minute edit, and it is the same five minutes checklist step 3.5 asks of §1.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRulebookStaysReadable()
    {
        const int FileCeilingBytes = 250_000;   // 194,528 the day the split landed
        const int LineCeiling = 2_000;          // 1,749 that day

        var path = Path.Combine(DocsDir(), "RULES.md");
        long bytes = new FileInfo(path).Length;
        int lines = File.ReadAllLines(path).Length;

        _output.WriteLine($"RULES.md {bytes} bytes / {lines} lines "
            + $"(ceilings {FileCeilingBytes} / {LineCeiling}); "
            + $"RULES-CASES.md {new FileInfo(Path.Combine(DocsDir(), "RULES-CASES.md")).Length} bytes");

        Assert.True(bytes <= FileCeilingBytes,
            $"docs/RULES.md is {bytes} bytes; the ceiling is {FileCeilingBytes}. §0 tells every "
            + "session to read this file end to end, and the session that measured it found 15% "
            + "was what actually got read at 711 KB. Move the worked examples to "
            + "RULES-CASES.md behind a （→CASES …） marker — do not raise the ceiling.");
        Assert.True(lines <= LineCeiling,
            $"docs/RULES.md is {lines} lines; the ceiling is {LineCeiling}. Same remedy as the "
            + "byte ceiling above: the rule statement stays, the story goes to the appendix.");
    }
}
