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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds the LICENCE PAPERWORK to the same standard the citations are held to: a file
/// that carries LilyPond's expression must carry LilyPond's notice, and the list of such
/// files must match the source rather than describe it.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. Lily#'s engine is in part a port of LilyPond, and GPLv3 §4 requires the
/// original copyright notices to be kept intact in what is conveyed while §5(a) requires the
/// modified work to say it was modified. That is satisfied by a block in each ported file
/// plus <c>LILYPOND-ATTRIBUTION.md</c>, and both halves rot the same way a citation does:
/// a port gets added and nobody writes the notice, or the notice stays after the port is
/// rewritten into something original. Prose in a contributing guide cannot see either.
/// </para>
/// <para>
/// ⚠️ THIS TEST CANNOT DECIDE WHAT IS A PORT. It checks that the two records AGREE — the
/// table lists file X and file X carries the block — never that X deserves to be in the
/// table. Membership is a judgement about whether the C# follows LilyPond's procedure or
/// merely its behaviour, and it is made by reading, once, per file. What the test buys is
/// that the judgement cannot be silently lost afterwards.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LicenceHeaderTests
{
    private readonly ITestOutputHelper _out;

    public LicenceHeaderTests(ITestOutputHelper output) => _out = output;

    private const string ProvenanceMarker = "Parts of this file are ported from LilyPond";
    private const string GplMarker = "GNU General Public License";

    /// <summary>
    /// Source files carrying no GPL notice at all. May only go DOWN.
    /// </summary>
    /// <remarks>
    /// A file with no notice is the one case GPLv3 §4 does not forgive, so this is a real
    /// zero rather than a backlog: it was 0 when the guard was written and the next file
    /// added without a header fails on the commit that adds it.
    /// </remarks>
    private const int MissingGplHeaderBaseline = 0;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LilySharp.slnx"))
                               && !Directory.Exists(Path.Combine(dir.FullName, "LilySharp.Core")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException("could not find the repository root.");
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        new[] { "LilySharp.Core", "LilySharp.Cli", "LilySharp.Lsp", "LilySharp.Tests" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>The files LILYPOND-ATTRIBUTION.md says are ported.</summary>
    private static readonly Regex TableRow = new(
        @"^\| `([^`]+)` \| `([^`]+)` \| (.+?) \|$", RegexOptions.Compiled);

    private static Dictionary<string, List<string>> AttributionTable(string root)
    {
        string md = Path.Combine(root, "LILYPOND-ATTRIBUTION.md");
        Assert.True(File.Exists(md),
            "LILYPOND-ATTRIBUTION.md is missing. It is the list GPLv3 §4's notices are "
            + "delivered by; the ported files reference it by name.");

        var table = new Dictionary<string, List<string>>();
        foreach (var line in File.ReadAllLines(md))
        {
            var m = TableRow.Match(line);
            if (!m.Success)
                continue;
            string cs = m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
            if (!table.TryGetValue(cs, out var units))
                table[cs] = units = new List<string>();
            units.Add(m.Groups[2].Value);
        }
        return table;
    }

    [Fact]
    public void EverySourceFileCarriesTheGplNotice()
    {
        var root = RepoRoot();
        var missing = SourceFiles(root)
            .Where(f => !File.ReadAllText(f).Contains(GplMarker, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f)
            .ToList();

        _out.WriteLine($"{missing.Count} source files carry no GPL notice "
                       + $"(baseline {MissingGplHeaderBaseline}).");

        Assert.True(missing.Count <= MissingGplHeaderBaseline,
            $"{missing.Count} source files carry no GPL notice; the baseline is "
            + $"{MissingGplHeaderBaseline}. Copy the header from any neighbouring file.\n"
            + string.Join("\n", missing.Select(f => "  " + f)));
    }

    [Fact]
    public void EveryAttributedFileCarriesItsLilyPondNotice()
    {
        var root = RepoRoot();
        var table = AttributionTable(root);
        Assert.NotEmpty(table);

        var bad = new List<string>();
        foreach (var (cs, units) in table)
        {
            string path = Path.Combine(root, cs);
            if (!File.Exists(path))
            {
                bad.Add($"  {cs} — listed in LILYPOND-ATTRIBUTION.md but does not exist "
                        + "(renamed? then update the table)");
                continue;
            }

            // Only the header counts: a mention further down is prose, not a notice.
            string header = string.Join('\n', File.ReadLines(path).Take(40));
            if (!header.Contains(ProvenanceMarker, StringComparison.Ordinal))
            {
                bad.Add($"  {cs} — listed as ported, but its header carries no LilyPond notice");
                continue;
            }
            foreach (var unit in units.Where(u => !header.Contains(u, StringComparison.Ordinal)))
                bad.Add($"  {cs} — its header does not name {unit}, which the table says it "
                        + "was ported from");
        }

        _out.WriteLine($"{table.Count} files are listed as ported; {bad.Count} disagree with "
                       + "their own header.");

        Assert.True(bad.Count == 0,
            "LILYPOND-ATTRIBUTION.md and the file headers disagree. Both are the same claim "
            + "written twice, and GPLv3 §4 is satisfied by the pair:\n"
            + string.Join("\n", bad));
    }

    [Fact]
    public void EveryFileWithALilyPondNoticeIsInTheAttributionTable()
    {
        var root = RepoRoot();
        var table = AttributionTable(root);

        var unlisted = SourceFiles(root)
            .Where(f => string.Join('\n', File.ReadLines(f).Take(40))
                        .Contains(ProvenanceMarker, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .Where(f => !table.ContainsKey(f))
            .OrderBy(f => f)
            .ToList();

        Assert.True(unlisted.Count == 0,
            "these files carry a LilyPond notice but are not in LILYPOND-ATTRIBUTION.md, so a "
            + "reader of the table cannot find them:\n"
            + string.Join("\n", unlisted.Select(f => "  " + f)));
    }

    /// <summary>
    /// The README must not describe a port as an inspiration.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE CLAIM THAT WAS ACTUALLY WRONG. The README said Lily# was "inspired by
    /// LilyPond" while <see cref="Svg.Layout.BeamScoringProblem"/> called itself a faithful
    /// port of <c>Beam_scoring_problem</c> in its own summary. §5(a) wants the modified work
    /// to say it was modified, and "inspired by" says the opposite of that.
    /// </remarks>
    [Fact]
    public void TheReadmeStatesThePortRelationship()
    {
        var root = RepoRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));

        // Markdown emphasis can fall anywhere inside a sentence, so match on words that
        // carry the claim rather than on a phrase a '**' can split.
        Assert.DoesNotContain("inspired by LilyPond", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LILYPOND-ATTRIBUTION.md", readme, StringComparison.Ordinal);
        Assert.Contains("affiliated", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corresponding source", readme, StringComparison.OrdinalIgnoreCase);
    }
}
