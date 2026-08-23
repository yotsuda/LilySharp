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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds the prose in <c>docs/HANDOFF.md</c> to the LP geometry ledger: where a shelf
/// quotes a named point's divergence, that number must still be the ledger's.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of what a stale shelf has actually cost. On 2026-08-23 the handoff's
/// §2 named five defects as open work whose falsifier points had gone exact — four of them
/// months earlier, and two on the very day they were raised:
/// </para>
/// <list type="bullet">
/// <item><description>the stem attachment X (<c>stem.up.right-edge.half-head</c>): shelf said
/// "−0.073200 left, 測って名指しただけ・未修正", ledger said 0 since 2026-08-03;</description></item>
/// <item><description>the tie chord outline (<c>tie.width.seconds.upper</c>): shelf said
/// "+0.888699999", ledger said −1e-09;</description></item>
/// <item><description>the courtesy key gap (<c>courtesy.meter.barline-to-cancellation</c>):
/// shelf said "−0.2", ledger said 0 since a user-approved port;</description></item>
/// <item><description>the grace approach (<c>grace.column.approach</c>): shelf said
/// "+0.850449" AND "着手根拠はもう regime ではなく点", ledger said 0;</description></item>
/// <item><description><c>page.stretched.first-staff-refpoint</c>: shelf said "−0.000042",
/// ledger said −4.46e-07.</description></item>
/// </list>
/// <para>
/// What that cost is the point. The session before this one was handed a delegation — start
/// the next item if it is favourable — spent its closing effort triaging §2's two thousand
/// lines, and reported that exactly ONE item could be started from the port itself: the stem
/// attachment. It declined, for reasons that were sound about warmth of context and about
/// carrying six-digit numbers through a summarised one. But the item did not exist. The whole
/// triage rested on a shelf that had been wrong for a hundred and fifty sessions, and no
/// amount of care inside the triage could have caught it, because the triage read the shelf.
/// </para>
/// <para>
/// The shape of the defect is the one §2 itself is about: THE SAME QUANTITY IS SPELLED IN TWO
/// PLACES. A point's divergence lives in the ledger, where closing it is a mechanical edit
/// that the ledger tests enforce, and it lives again in the prose of the shelf that raised
/// it, where nothing enforces anything. The two drift, and the prose is what the next session
/// reads. So this is the second reader: the tag <c>&lt;!-- ledger: NAME = VALUE --&gt;</c>
/// records what the ledger said when that passage was last verified, and this test fails the
/// moment the ledger moves away from it.
/// </para>
/// <para>
/// It does NOT ask that a shelf be closed when its point goes exact — a shelf can outlive its
/// point honestly, and several here do (the courtesy group's remaining divergence is real and
/// has no point at all; the grace column's MODEL gap outlived the number that found it). It
/// asks only that the passage be RE-READ, by a person, before the number is allowed to change
/// under it. That is the same bargain as LpProvenanceTests: not that the backlog be cleared,
/// but that the next number carry its origin.
/// </para>
/// <para>
/// Deliberately scoped to HANDOFF.md. HANDOFF-ARCHIVE.md is verbatim history and is SUPPOSED
/// to hold the numbers as they read at the time; freezing it is the whole point of an archive.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class HandoffLedgerCitationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HandoffLedgerCitationTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// The number of cited points when this guard was written. A ratchet in the same shape as
    /// <see cref="LpProvenanceTests"/>'s: it may rise freely and must never fall silently,
    /// because the cheapest way to make a citation test pass is to delete the citation.
    /// </summary>
    private const int CitationsWhenWritten = 27;

    private static readonly Regex Tag = new(
        @"<!--\s*ledger:\s*(?<name>[A-Za-z0-9._\-]+)\s*=\s*(?<value>[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*-->",
        RegexOptions.Compiled);

    private static readonly Regex AnyComment = new(@"<!--.*?-->", RegexOptions.Compiled);

    private sealed record Citation(string Name, double Value, int Line);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "LilySharp.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            "LilySharp.slnx not found above " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<Citation> Citations()
    {
        var path = Path.Combine(RepoRoot(), "docs", "HANDOFF.md");
        Assert.True(File.Exists(path), $"docs/HANDOFF.md not found at {path}");

        var found = new List<Citation>();
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in Tag.Matches(lines[i]))
            {
                found.Add(new Citation(
                    m.Groups["name"].Value,
                    double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture),
                    i + 1));
            }
        }
        return found;
    }

    private static (IReadOnlyDictionary<string, double?> Residuals, double Tolerance) Ledger()
    {
        var path = Path.Combine(RepoRoot(), "audit", "lp-geometry", "lp-geometry.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        double tolerance = root.GetProperty("tolerance").GetDouble();
        var residuals = new Dictionary<string, double?>();
        foreach (var e in root.GetProperty("entries").EnumerateObject())
        {
            residuals[e.Name] = e.Value.TryGetProperty("residual", out var r)
                && r.ValueKind != JsonValueKind.Null
                    ? r.GetDouble()
                    : null;
        }
        return (residuals, tolerance);
    }

    /// <summary>
    /// Every point a handoff shelf cites must exist in the ledger. A typo here is worse than a
    /// missing citation: it reads as verified and can never go red.
    /// </summary>
    [Fact]
    public void EveryCitedPoint_IsInTheLedger()
    {
        var (residuals, _) = Ledger();
        var unknown = Citations().Where(c => !residuals.ContainsKey(c.Name)).ToList();

        Assert.True(unknown.Count == 0,
            "docs/HANDOFF.md cites LP geometry points that are not in the ledger:\n"
            + string.Join("\n", unknown.Select(c =>
                $"  HANDOFF.md:{c.Line}  '{c.Name}'"))
            + "\nEither the name is a typo, or the point was renamed or removed. A citation "
            + "that names nothing can never go red, so it is worse than no citation at all.");
    }

    /// <summary>
    /// The number a shelf quotes must still be the ledger's. When this goes red the ledger has
    /// moved — usually because the defect was FIXED — and the passage that quotes it is now
    /// telling the next session to start work that is already done.
    /// </summary>
    [Fact]
    public void EveryCitedResidual_IsStillTheLedgers()
    {
        var (residuals, tolerance) = Ledger();
        var drifted = new List<string>();

        foreach (var c in Citations())
        {
            if (!residuals.TryGetValue(c.Name, out var actual))
                continue;   // named by EveryCitedPoint_IsInTheLedger

            if (actual is null)
            {
                drifted.Add(
                    $"  HANDOFF.md:{c.Line}  {c.Name}\n"
                    + $"      handoff says {c.Value:G9}, ledger has NO residual recorded yet");
                continue;
            }

            double drift = Math.Abs(actual.Value - c.Value);
            if (drift <= tolerance)
                continue;

            string verdict = Math.Abs(actual.Value) <= tolerance
                ? "THE POINT IS NOW EXACT — the shelf is probably describing a closed defect"
                : "the point moved";

            drifted.Add(
                $"  HANDOFF.md:{c.Line}  {c.Name}\n"
                + $"      handoff says {c.Value:G9}, ledger says {actual.Value:G9} "
                + $"(drift {drift:G6}) — {verdict}");
        }

        if (drifted.Count > 0)
            _output.WriteLine(string.Join("\n", drifted));

        Assert.True(drifted.Count == 0,
            "docs/HANDOFF.md quotes divergences the ledger no longer holds:\n"
            + string.Join("\n", drifted)
            + "\n\nRE-READ THOSE PASSAGES before touching this test. A shelf whose point has "
            + "gone exact is the failure this guard exists for: the session that reads it will "
            + "triage work that no longer exists. Fix the prose, then update the tag to the "
            + "ledger's current value. Do NOT update the tag alone — the tag is the record "
            + "that a person read the passage, and moving it without reading is the drift.");
    }

    /// <summary>
    /// A point NAMED in the durable sections must carry a tag somewhere in them, or the guards
    /// above are checking a set that quietly shrinks as new prose is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// §1 is excluded on purpose and not by oversight: it is rewritten from scratch every
    /// session, so requiring a tag there would tax every handoff for a passage that is gone
    /// two sessions later. §2 (open work) and §3 (settled decisions) are the durable ones, and
    /// §3 is the sharper case of the two — it is headed 蒸し返さない, "do not relitigate", so
    /// it is the section nobody re-reads, which makes it the worst place for a number to rot
    /// unobserved. The four points this guard first pulled in are all §3's, and two of them
    /// (lyrics.band-floor.staff-to-lyric, lyrics.column.word-gap.narrow) are the measured
    /// consequences cited as the basis of a LICENSING decision — ship TeX Gyre Schola rather
    /// than match LilyPond's AGPL C059. If those move, the stated basis of that decision has
    /// changed and someone should be told.
    /// </para>
    /// <para>
    /// ⚠️ Matching is boundary-aware against the ledger's own name alphabet, because the names
    /// nest: <c>note-to-note.quarter</c> is a substring of
    /// <c>compressed.note-to-note.quarter</c>, and <c>lyrics.column.word-gap</c> of
    /// <c>lyrics.column.word-gap.narrow</c>. A plain Contains reports the shorter name as cited
    /// on every line that cites the longer one — the measurement that scoped this guard did
    /// exactly that and claimed six gaps where there are four.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPointNamedInTheDurableSections_CarriesATag()
    {
        var (residuals, _) = Ledger();
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docs", "HANDOFF.md"));

        int start = Array.FindIndex(lines, l => l.StartsWith("## 2. ", StringComparison.Ordinal));
        Assert.True(start >= 0,
            "docs/HANDOFF.md has no '## 2. ' heading — this guard locates the durable sections "
            + "by that heading, so a rename must be reflected here.");

        var tagged = new HashSet<string>(StringComparer.Ordinal);
        var named = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = start; i < lines.Length; i++)
        {
            foreach (Match m in Tag.Matches(lines[i]))
                tagged.Add(m.Groups["name"].Value);

            string bare = AnyComment.Replace(lines[i], string.Empty);
            foreach (var name in residuals.Keys)
            {
                if (named.ContainsKey(name))
                    continue;
                var cited = new Regex(
                    @"(?<![A-Za-z0-9._\-])" + Regex.Escape(name) + @"(?![A-Za-z0-9._\-])");
                if (cited.IsMatch(bare))
                    named[name] = i + 1;
            }
        }

        var untagged = named.Where(kv => !tagged.Contains(kv.Key))
            .OrderBy(kv => kv.Value).ToList();

        Assert.True(untagged.Count == 0,
            "docs/HANDOFF.md §2/§3 name LP geometry points that carry no "
            + "<!-- ledger: NAME = VALUE --> tag, so nothing would notice if they went stale:\n"
            + string.Join("\n", untagged.Select(kv =>
                $"  HANDOFF.md:{kv.Value}  {kv.Key}  (ledger residual "
                + $"{(residuals[kv.Key]?.ToString("G9") ?? "none recorded")})"))
            + "\nAdd the tag with the ledger's CURRENT residual, having read the passage and "
            + "checked that what it says is still true. If the passage only mentions the point "
            + "in passing and asserts nothing about it, the tag is still the cheaper answer — "
            + "it costs one comment and buys the passage a falsifier.");
    }

    /// <summary>
    /// The cheapest way to make the guards above pass is to delete the tags, so the count is a
    /// ratchet: it may rise and must not fall without saying so here.
    /// </summary>
    [Fact]
    public void TheCitationsAreNotQuietlyDeleted()
    {
        int count = Citations().Count;
        _output.WriteLine($"docs/HANDOFF.md cites {count} ledger points "
            + $"(was {CitationsWhenWritten} when this guard was written)");

        Assert.True(count >= CitationsWhenWritten,
            $"docs/HANDOFF.md now cites {count} ledger points, down from "
            + $"{CitationsWhenWritten}. Removing a citation removes the only thing that makes "
            + "a shelf's number go stale LOUDLY. If a passage was deleted for a good reason, "
            + "lower CitationsWhenWritten in the same commit and say which passage went.");
    }
}
