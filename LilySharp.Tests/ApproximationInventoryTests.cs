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
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Builds <c>docs/APPROXIMATIONS.md</c> from the comments in <c>LilySharp.Core</c>, and holds
/// the committed file to it.
/// </summary>
/// <remarks>
/// <para>
/// The document counts what the engine KNOWS it has not ported and whether anything watches
/// it: the ledger says whether the places we MEASURED agree with LilyPond, and cannot say how
/// many places were deliberately not measured. Those are recorded honestly, but only in prose,
/// one comment at a time, across the whole of Core. This turns that prose into a number.
/// It is a TRIAGE tool, not a verdict — a site on this list is not a defect, and most are
/// deliberate and argued. The question it answers is "how many, and where are they clustered".
/// </para>
/// <para>
/// ⚠️ IT USED TO BE A PYTHON SCRIPT WITH NO OBSERVER, AND THAT IS WHY IT IS HERE NOW. The
/// document whose job is to count what nothing watches was itself watched by nothing: it was
/// last written in fec59670, together with the script, and nothing regenerated it for 538
/// commits. Every number in it was wrong when session 276 re-ran the script (APPROX 48→54,
/// UNWATCHED 45→50, OWN 83→115, total 176→219) and its density table still led with a file
/// that had since been split. A stale count reads as coverage, which is worse than no count.
/// </para>
/// <para>
/// <b>Why the classifier moved here rather than gaining a checker.</b> A C# checker beside a
/// Python generator would be TWO SPELLINGS of one classifier — the defect RULES 5.2.1② is
/// about — and the two would drift exactly the way the document drifted from the tree. So the
/// generator itself moved, the script was deleted, and the test writes the file it checks.
/// One spelling, and the thing that regenerates is the thing that runs on every build.
/// </para>
/// <para>
/// ⚠️ <b>What this guards and what it does not.</b> It guards that the document is NOT STALE —
/// that its numbers and lists are the ones today's comments produce. It does NOT guard that
/// the 219 sites are correctly labelled: every entry here is a comment's SELF-REPORT, and a
/// port that quietly diverged without saying so is invisible to this exactly as it is to the
/// ledger. Reading the list is still the work; this only keeps the list honest about when it
/// was taken.
/// </para>
/// <para>
/// ⚠️ <b>The ordering is fixed here, and it was not in the script.</b> <c>pathlib</c> sorts
/// <c>WindowsPath</c> case-insensitively over <c>\</c>-separated strings and <c>PosixPath</c>
/// case-SENSITIVELY over <c>/</c>-separated ones, so the script's section order was a property
/// of the machine it ran on — a thing no gate can be built on. MEASURED 2026-08-28: over
/// today's 388 files the two orders coincide at every position, so this is a hazard the port
/// removes rather than a defect it repairs. This sorts one way everywhere: the repository-
/// relative path with <c>\</c> separators, lower-cased, compared ordinally.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ApproximationInventoryTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ApproximationInventoryTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// Blesses a regenerated document, the way <c>LILYSHARP_UPDATE_SNAPSHOTS</c> blesses a
    /// picture.
    /// </summary>
    /// <remarks>
    /// A SEPARATE variable on purpose. Re-basing snapshots and re-taking this census are
    /// different acts, and folding them together would let a snapshot approval rewrite the
    /// inventory as a side effect — a change the reviewer did not look at, in the one document
    /// whose entire content is a claim about the tree.
    /// </remarks>
    private static readonly bool UpdateDocs =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_DOCS") == "1";

    private const string Relative = "docs/APPROXIMATIONS.md";

    /// <summary>
    /// The three kinds, which are NOT the same thing — keeping them apart is most of the
    /// value. <c>OWN</c>: the comment claims LilyPond has no counterpart at all.
    /// <c>APPROX</c>: a port knowingly not the shape of LilyPond's. <c>UNWATCHED</c>: a
    /// divergence the comment says nothing observes.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>CultureInvariant</c> is not decoration: <c>RegexOptions.IgnoreCase</c> alone folds
    /// case with the CURRENT culture, and this suite runs on a ja-JP machine and on CI. Python's
    /// <c>re.I</c> is culture-free, so matching it means saying so.
    /// </remarks>
    private static readonly (string Key, string Desc, Regex Pattern)[] Categories =
    [
        ("APPROX", "LP に対応物はあるが、形が違うと自認しているもの",
            new Regex("DERIVATION, NOT A TRANSCRIPTION|NOT PORTED HERE|NOT PORTED:|"
                    + @"\bnot ported\b|IS NOT PORTED|approximation the|"
                    + "the same approximation|silently approximated",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("UNWATCHED", "観測者がゼロだと自認しているもの",
            new Regex("no ledger point|No ledger point|no point (?:measures|observes|watches|"
                    + "reaches|is)|nothing watches|no observer|unobserved|"
                    + "observer(?:s)? (?:is|are) zero|no book reaches|reaches that texture",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)),
        ("OWN", "LilyPond に対応物が無いと宣言しているもの (LILYSHARP-OWN)",
            new Regex("LILYSHARP-OWN", RegexOptions.CultureInvariant)),
    ];

    /// <summary>
    /// A MENTION of the marker is not a declaration of it.
    /// </summary>
    /// <remarks>
    /// The session-158 audit of the OWN list found ~1 in 7 hits were HISTORY ("was/were/declared
    /// LILYSHARP-OWN until …") or META ("it is NOT LILYSHARP-OWN"). Counting those inflates the
    /// total and sends the next reader hunting labels that no longer exist. The keyword must
    /// precede the marker on the SAME line — live labels put "not"/"declared" only AFTER it
    /// ("LILYSHARP-OWN, DECLARED:") — and cross-references were reworded at their sites rather
    /// than matched around here. The gap may not cross a sentence end, but a decimal
    /// ("Was an invented 0.3 (…") is not one, hence the tempered dot.
    /// </remarks>
    private static readonly Regex OwnMention = new(
        @"\b(?:was|were|used to|declared|not|than as)\b(?:[^.]|\.(?=\d)){0,40}LILYSHARP-OWN"
        + @"|LILYSHARP-OWN line\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A line that is a comment, and the prefix that makes it one.</summary>
    private static readonly Regex CommentPrefix = new(@"^\s*(?:///|//|\*)\s?");

    private static readonly Regex XmlTag = new("<[^>]+>");
    private static readonly Regex Whitespace = new(@"\s+");

    private static string Clean(string line)
    {
        var s = CommentPrefix.Replace(line.TrimEnd(), "", 1);
        s = XmlTag.Replace(s, "");            // XML doc tags carry no information here
        return Whitespace.Replace(s, " ").Trim();
    }

    /// <summary>
    /// Every <c>.cs</c> of Core, in the one order this generator defines (see the class
    /// remarks), as repository-relative POSIX paths.
    /// </summary>
    private static string[] SourceFiles(string root)
        => Directory.EnumerateFiles(Path.Combine(root, "LilySharp.Core"), "*.cs",
                SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('/', '\\'))
            .Where(rel => !rel.Contains(@"\bin\", StringComparison.Ordinal)
                       && !rel.Contains(@"\obj\", StringComparison.Ordinal))
            .OrderBy(rel => rel.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(rel => rel.Replace('\\', '/'))
            .ToArray();

    private sealed record Hit(string File, int Line, string Text);

    /// <summary>
    /// Scans the tree and renders the document. Returns the text and the per-category hits so
    /// the totals can be printed without parsing what was just written.
    /// </summary>
    private static (string Text, Dictionary<string, List<Hit>> Hits) Build(string root)
    {
        var hits = Categories.ToDictionary(c => c.Key, _ => new List<Hit>(), StringComparer.Ordinal);
        var perFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstSeen = new List<string>();   // ties in the density table keep this order

        var files = SourceFiles(root);
        Assert.True(files.Length > 100,
            $"only {files.Length} .cs files under LilySharp.Core — the census would be "
            + "vacuous, and a vacuous census reads exactly like a clean one.");

        foreach (var rel in files)
        {
            var lines = File.ReadAllLines(
                Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            for (int i = 0; i < lines.Length; i++)
            {
                if (!CommentPrefix.IsMatch(lines[i]))
                    continue;
                foreach (var (key, _, pattern) in Categories)
                {
                    if (!pattern.IsMatch(lines[i]))
                        continue;
                    // A history/meta MENTION of the marker, not a declaration: drop the line
                    // rather than let a later category claim it.
                    if (key == "OWN" && OwnMention.IsMatch(lines[i]))
                        break;
                    hits[key].Add(new Hit(rel, i + 1, Clean(lines[i])));
                    if (!perFile.ContainsKey(rel))
                        firstSeen.Add(rel);
                    perFile[rel] = perFile.GetValueOrDefault(rel) + 1;
                    break;    // one line counts once, in the first category that claims it
                }
            }
        }

        var doc = new List<string>
        {
            "# 未移植・近似・無観測の棚卸し（自動生成）",
            "",
            "> **AUTO-GENERATED by `LilySharp.Tests/ApproximationInventoryTests.cs` — 手で編集しない。**",
            "> 元は各 `.cs` のコメントで、**直すのはコメントの側**。テストが再生成する"
                + "（`LILYSHARP_UPDATE_DOCS=1` で書き換え）。",
            "",
            "台帳は**測った所**が LilyPond と合っているかを言う。**測っていない所が"
                + "どれだけ違う形をしているか**は言えない。この表はその欠けている側の数で、",
            "「全点 exact だが機械は別物」という状態を検知するための唯一の材料。",
            "",
            "⚠️ **ここに載ること自体は欠陥ではない。** ほとんどは意図的で、論証も付いている。"
                + "答えるのは「いくつあって、どこに固まっているか」だけ。",
            "",
            "## 総数",
            "",
            "| 区分 | 件数 | 意味 |",
            "|---|---:|---|",
        };
        foreach (var (key, desc, _) in Categories)
            doc.Add($"| `{key}` | {hits[key].Count} | {desc} |");
        doc.Add($"| **計** | **{hits.Values.Sum(v => v.Count)}** | |");
        doc.Add("");
        doc.Add("## 密度の高いファイル（上位 12）");
        doc.Add("");
        doc.Add("| ファイル | 件数 |");
        doc.Add("|---|---:|");
        foreach (var rel in firstSeen.OrderByDescending(f => perFile[f]).Take(12))
            doc.Add($"| `{rel}` | {perFile[rel]} |");
        doc.Add("");

        foreach (var (key, desc, _) in Categories)
        {
            doc.Add($"## {key} — {desc}（{hits[key].Count} 件）");
            doc.Add("");
            var current = "";
            foreach (var h in hits[key])
            {
                if (h.File != current)
                {
                    doc.Add($"### `{h.File}`");
                    current = h.File;
                }
                doc.Add($"- **:{h.Line}** {h.Text}");
            }
            doc.Add("");
        }

        return (string.Join("\n", doc) + "\n", hits);
    }

    /// <summary>
    /// The committed document is the one today's comments produce.
    /// </summary>
    [Fact]
    public void TheInventoryIsNotStale()
    {
        var root = CollectResumeTests.FindRepoRoot();
        var (text, hits) = Build(root);
        var path = Path.Combine(root, Relative.Replace('/', Path.DirectorySeparatorChar));

        foreach (var (key, _, _) in Categories)
            _output.WriteLine($"{key,-10} {hits[key].Count}");
        _output.WriteLine($"{"TOTAL",-10} {hits.Values.Sum(v => v.Count)}");

        if (UpdateDocs)
        {
            // LF, no BOM: the blob is LF and a wholesale ending flip would drown the real
            // change in a whole-file diff.
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(path), $"{Relative} is missing");
        var committed = File.ReadAllText(path).Replace("\r\n", "\n");
        if (committed == text)
            return;

        Assert.Fail(FirstDifference(committed, text)
            + "\n\ndocs/APPROXIMATIONS.md is not what the tree says today. It is generated, so "
            + "the repair is to regenerate it, never to edit it: set LILYSHARP_UPDATE_DOCS=1 and "
            + "re-run, then read the diff — a count that moved is a comment somebody added or "
            + "retired, and it is worth knowing which.");
    }

    /// <summary>The first line that differs, with its neighbours, and the two totals.</summary>
    private static string FirstDifference(string committed, string fresh)
    {
        var a = committed.Split('\n');
        var b = fresh.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
            i++;
        var sb = new StringBuilder();
        sb.AppendLine($"committed {a.Length} lines, freshly generated {b.Length}; "
                      + $"first difference at line {i + 1}:");
        for (int k = Math.Max(0, i - 2); k < i; k++)
            sb.AppendLine($"    {a[k]}");
        sb.AppendLine($"  - {(i < a.Length ? a[i] : "<end of file>")}");
        sb.AppendLine($"  + {(i < b.Length ? b[i] : "<end of file>")}");
        return sb.ToString();
    }
}
