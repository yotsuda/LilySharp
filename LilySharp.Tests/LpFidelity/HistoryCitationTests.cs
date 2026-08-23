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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds the commit SHAs that the documents and the code quote to the history they claim to
/// quote: a cited SHA must be REACHABLE from the branch, not merely present in the object
/// database.
/// </summary>
/// <remarks>
/// <para>
/// This exists because "does that SHA resolve" and "is that SHA in the history" are different
/// questions, and the repository learned the difference the expensive way. On 2026-08-23 the
/// whole history was rewritten to strip a forbidden trailer, and the 958 SHA citations that
/// the rewrite would have orphaned were re-pointed at the new commits and verified pair by
/// pair. The session then ran <c>gc --prune=now</c> and watched the number of citations that
/// resolve fall from 547 to 503 — WITHOUT having touched a single citation. The 44 that
/// stopped resolving had never been in any history: they named commits that an amend or a
/// reset had orphaned long ago, and which survived in the object database as garbage. For as
/// long as that garbage was there, <c>git rev-parse</c> answered them, and forty-four dead
/// citations looked alive.
/// </para>
/// <para>
/// The bone that came out of it is the contract asserted here: <b>a resolving SHA is not the
/// same thing as a reachable one</b>. <c>git rev-parse</c> reads the object database; it does
/// not read reachability. So the check has to go one step further — <c>merge-base
/// --is-ancestor</c>, done set-wise here as membership in <c>rev-list</c> of the tip.
/// </para>
/// <para>
/// The first run of this guard found one, and it was not a leftover from an amend. The
/// trailer rewrite had needed a SECOND filter-branch pass, because one commit spelled its
/// trailer inside a long single line instead of on a line of its own and the first pass's
/// <c>/^Co-Authored-By:/</c> never matched it. The first pass rewrote every ref it was given,
/// tags included; the second pass rewrote only the branch. So <c>refs/tags/v0.3.0</c> — on
/// this machine AND on the remote — was left pointing into the first pass's history, holding
/// 98 commits alive that the branch no longer contains, and among them the one commit whose
/// trailer the whole operation existed to remove. A ref left behind by a partial rewrite is
/// exactly the shape this test is for: nothing about it is visible from the branch, the tree
/// is byte-identical, the suite is green, and the object it anchors answers <c>rev-parse</c>
/// as confidently as any live commit.
/// </para>
/// <para>
/// <b>What is deliberately NOT asserted.</b> There is no demand that every citation land in
/// the history. Several hundred in the tree name commits that were amended or reset away
/// sessions ago; they are unrepairable, because the commit they meant no longer exists
/// anywhere and nothing records what it was. <see cref="DeadCitationsDoNotGrow"/> census-
/// counts them under a ceiling instead, so the backlog cannot quietly grow, in the same
/// bargain as <c>LpProvenanceTests</c>: not that the backlog be cleared, but that it stop
/// expanding. Nor is unreferenced garbage in the local object database a failure — see
/// <see cref="CitedCommitsAreNotHeldAliveByAnotherRef"/> for where that line is drawn and
/// what it cost to find it.
/// </para>
/// <para>
/// <b>Why this shells out to git and refuses to skip.</b> It is the only test in the suite
/// that does, and the dependency is real: the question is about history, and history is not
/// in the working tree. A skip when git is unavailable would be the exact fake green §0 warns
/// about with the no-op incremental build — a check that reports success by not looking. So
/// an unusable repository is a FAILURE with the reason named, and the CI workflows carry
/// <c>fetch-depth: 0</c> because <c>actions/checkout</c> is shallow by default and a
/// one-commit clone would make every citation in the tree look dead at once.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class HistoryCitationTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HistoryCitationTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// The number of citations that resolved to a reachable commit when this guard was
    /// written. A ratchet in the same shape as <see cref="HandoffLedgerCitationTests"/>'s: it
    /// may rise freely and must never fall silently, because the cheapest way to make a
    /// citation check pass is to stop citing.
    /// </summary>
    /// <remarks>
    /// This is the number that fell 547 → 503 unnoticed. Had it been under a ratchet then,
    /// the drop would have been the thing that opened the investigation instead of a stray
    /// observation at the end of a long session.
    /// <para>
    /// Raised as sessions cite: 510 when written, then 515 and 517 across the two handoff
    /// commits of the session after it. Follow the measured number up in the same commit that
    /// raises it — leaving the floor behind means a later fall back to it is exactly the
    /// silent drop the ratchet exists to catch.
    /// </para>
    /// </remarks>
    private const int LiveCitationsWhenWritten = 517;

    /// <summary>
    /// The number of citation-shaped tokens whose commit is not in the history, when this
    /// guard was written. A CEILING, not a floor — the backlog may shrink, and must not grow.
    /// </summary>
    /// <remarks>
    /// These are commits that an amend or a reset orphaned before anyone thought to check.
    /// They are counted rather than listed because the list is not actionable: there is no
    /// record anywhere of what commit <c>01139c12</c> was, so no session can repair it. What
    /// a ceiling does buy is that the NEXT history rewrite cannot add to them silently — a
    /// citation written today always names a commit that exists today, so the only way this
    /// number rises is a rewrite that failed to re-point something.
    /// </remarks>
    private const int DeadCitationsWhenWritten = 469;

    /// <summary>
    /// Extensions scanned for citations. Chosen because they are where citations are actually
    /// written — prose, code comments, the ledger's <c>why</c> fields, the audit scripts — and
    /// validated against the count the rewrite session measured independently (503 resolving).
    /// </summary>
    private static readonly HashSet<string> ScannedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".cs", ".json", ".ps1", ".csv", ".yml", ".yaml" };

    /// <summary>
    /// A hexadecimal run that could be an abbreviated object name. Deliberately loose: every
    /// token that git itself is willing to resolve to a commit IS a citation, whatever it
    /// looked like, so the narrowing happens after git has answered rather than before.
    /// </summary>
    private static readonly Regex HexRun = new(@"\b[0-9a-f]{7,40}\b", RegexOptions.Compiled);

    /// <summary>
    /// Of the tokens that are not live citations, the ones shaped like a citation rather than
    /// like a number. Every citation in this repository that is live is 7 or 8 characters
    /// long, and the tree is full of hexadecimal-looking decimals — residuals such as
    /// <c>034143669</c> and <c>004735433</c> — which carry no letters at all. Requiring two
    /// letters inside a 7-to-10 run separates them; it also, honestly, misses the dead
    /// citations that happen to carry fewer, which is why this feeds a census and not a claim.
    /// </summary>
    private static bool LooksLikeCitation(string token)
        => token.Length is >= 7 and <= 10
           && token.Count(c => c is >= 'a' and <= 'f') >= 2;

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

    private static (int Exit, string Out) Git(string[] arguments, string? stdin = null)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("git could not be started");

        // Both pipes are drained before waiting: rev-list of this history is well over the
        // 64 KB a pipe buffer holds, and cat-file is fed on stdin while it writes.
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (stdin is not null)
        {
            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
        }
        p.WaitForExit();
        stderr.GetAwaiter().GetResult();
        return (p.ExitCode, stdout.GetAwaiter().GetResult());
    }

    /// <summary>
    /// The commit the citations are held against: the branch if it is here, otherwise
    /// whatever is checked out. Named rather than assumed, because the Linux leg of the gate
    /// runs from a hard reset onto a remote-tracking ref and need not carry a local
    /// <c>master</c>, and a guard that silently held citations against the wrong tip would be
    /// the same defect it is here to catch.
    /// </summary>
    private static string ResolveTip()
    {
        if (Git(new[] { "rev-parse", "--verify", "--quiet", "master^{commit}" }).Exit == 0)
            return "master";
        return "HEAD";
    }

    private sealed record Token(string Text, string File, int Line);

    private sealed record Survey(
        IReadOnlyList<Token> Occurrences,
        IReadOnlyDictionary<string, string?> ResolvedTo,
        IReadOnlySet<string> Reachable,
        IReadOnlySet<string> HeldByAnotherRef,
        IReadOnlySet<string> ResolvesToANonCommit,
        string Tip);

    private static readonly Lazy<Survey> Shared = new(Take, isThreadSafe: true);

    private static Survey Take()
    {
        var inside = Git(new[] { "rev-parse", "--is-inside-work-tree" });
        Assert.True(inside.Exit == 0 && inside.Out.Trim() == "true",
            "This guard asks git about the history behind the citations in the tree, and there "
            + "is no git repository at " + RepoRoot() + ". It does not skip: a citation check "
            + "that passes by not looking is worse than no citation check, because it reads as "
            + "coverage. If this is a source archive rather than a clone, the honest fix is to "
            + "exclude the LpFidelity history traits, not to make the assertion vacuous.");

        var shallow = Git(new[] { "rev-parse", "--is-shallow-repository" });
        Assert.True(shallow.Out.Trim() == "false",
            "The clone is shallow, so almost every citation in the tree would look dead and "
            + "this guard would be measuring the checkout instead of the documents. CI carries "
            + "fetch-depth: 0 for exactly this reason — actions/checkout fetches one commit by "
            + "default.");

        string tip = ResolveTip();

        var files = Git(new[] { "ls-files", "-z" }).Out
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(f => ScannedExtensions.Contains(Path.GetExtension(f)))
            .ToArray();
        Assert.True(files.Length > 100,
            $"git ls-files returned only {files.Length} scannable files — the survey would be "
            + "vacuous.");

        var occurrences = new List<Token>();
        var root = RepoRoot();
        foreach (var relative in files)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;   // tracked but not checked out (sparse checkout)
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
                foreach (Match m in HexRun.Matches(lines[i]))
                    occurrences.Add(new Token(m.Value, relative, i + 1));
        }

        var unique = occurrences.Select(o => o.Text).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();

        // One batch call rather than one process per token: --batch-check answers in input
        // order, printing "<input> missing" (or "<input> ambiguous") for what it cannot name.
        var answers = Git(new[] { "cat-file", "--batch-check=%(objectname) %(objecttype)" },
                          string.Join("\n", unique) + "\n").Out
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
        Assert.Equal(unique.Length, answers.Length);

        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);
        var otherObject = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < unique.Length; i++)
        {
            var parts = answers[i].Split(' ');
            bool named = parts.Length >= 2 && parts[1] is not ("missing" or "ambiguous");
            resolved[unique[i]] = named && parts[1] == "commit" ? parts[0] : null;
            if (named && parts[1] != "commit")
                otherObject.Add(unique[i]);   // a tree or blob quoted on purpose, not a dead commit
        }

        var reachable = Git(new[] { "rev-list", tip }).Out
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(reachable.Count > 1000,
            $"rev-list {tip} returned {reachable.Count} commits — that is not this history.");

        // Commits some OTHER ref keeps alive. This is the line between a defect that travels
        // with the repository and a local housekeeping detail: see HeldOnlyByAnotherRef.
        var heldElsewhere = Git(new[] { "rev-list", "--all", "--not", tip }).Out
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToHashSet(StringComparer.Ordinal);

        return new Survey(occurrences, resolved, reachable, heldElsewhere, otherObject, tip);
    }

    /// <summary>
    /// No cited SHA may resolve to a commit that the branch cannot reach but SOME OTHER REF
    /// holds alive. That is a ref left behind by a partial rewrite, and the citation under it
    /// answers every check anyone would think to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The qualifier is not a softening, and the first version of this test did not have it.
    /// It failed on Windows with the one real defect, then failed on the Linux leg with FORTY-
    /// EIGHT more — every one of them "kept alive by:" nothing at all. Those are orphans of
    /// old amends sitting in that clone's object database, and the two machines disagreed only
    /// because one had been garbage-collected the day before and the other had not. A test
    /// that goes red because of when someone last ran <c>gc</c> is measuring the machine, not
    /// the tree, and this suite has a name for a check whose answer depends on where it ran.
    /// </para>
    /// <para>
    /// So the line is drawn at REFS, which travel: a tag or branch pointing off the history is
    /// a fact about the repository that every clone fetches and every reader inherits, and it
    /// is the shape the trailer rewrite actually left behind. Unreferenced garbage is a fact
    /// about one disk. It is not ignored — it stops counting as a live citation and falls into
    /// <see cref="DeadCitationsDoNotGrow"/>, which is what it honestly is: a citation whose
    /// commit is not in the history, that happens to still get an answer here.
    /// </para>
    /// </remarks>
    [Fact]
    public void CitedCommitsAreNotHeldAliveByAnotherRef()
    {
        var s = Shared.Value;

        var zombies = s.Occurrences
            .Where(o => s.ResolvedTo[o.Text] is string sha
                        && !s.Reachable.Contains(sha)
                        && s.HeldByAnotherRef.Contains(sha))
            .GroupBy(o => o.Text, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        if (zombies.Length == 0)
            return;

        var report = new StringBuilder();
        report.AppendLine(
            $"{zombies.Length} cited commit(s) are NOT reachable from {s.Tip}, yet another ref "
            + "holds them alive. A SHA that resolves is not a SHA that is in the history: git "
            + "rev-parse reads the object database, so a commit anchored by a tag that a "
            + "partial rewrite forgot — or by a stale remote-tracking branch — answers just "
            + "like a live commit, and the citation looks verified forever. Re-point or delete "
            + "the ref; do not re-point the citation.");
        report.AppendLine();
        foreach (var z in zombies)
        {
            var (_, contains) = Git(new[] { "for-each-ref", "--contains", z.Key, "--format=%(refname)" });
            report.AppendLine($"  {z.Key} -> {s.ResolvedTo[z.Key]}");
            report.AppendLine($"    kept alive by: {contains.Replace("\n", " ").Trim()}");
            foreach (var o in z.Take(5))
                report.AppendLine($"    cited at {o.File}:{o.Line}");
        }
        Assert.Fail(report.ToString());
    }

    /// <summary>
    /// The number of citations that point at a reachable commit may rise and must not fall.
    /// </summary>
    [Fact]
    public void LiveCitationsDoNotShrink()
    {
        var s = Shared.Value;
        int live = s.Occurrences
            .Select(o => o.Text).Distinct(StringComparer.Ordinal)
            .Count(t => s.ResolvedTo[t] is string sha && s.Reachable.Contains(sha));

        _output.WriteLine($"live citations: {live} (floor {LiveCitationsWhenWritten}, tip {s.Tip})");
        Assert.True(live >= LiveCitationsWhenWritten,
            $"{live} citations resolve to a reachable commit; {LiveCitationsWhenWritten} did "
            + "when this ratchet was set. Either a history rewrite orphaned citations without "
            + "re-pointing them — that is the 958-citation repair the trailer removal had to "
            + "do — or the citations were deleted, which is the cheapest way to pass a "
            + "citation test and the reason this ratchet exists. Lower the floor only "
            + "deliberately, in the same commit that removes the citations, saying why.");
    }

    /// <summary>
    /// The backlog of citations whose commit is not in the history may shrink and must not
    /// grow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dead is defined as NOT LIVE — not as "git could not answer it". Those are different
    /// sets on a working clone, by exactly the objects a <c>gc</c> has not swept yet, and
    /// defining it the other way is what made this census read 469 on one machine and 421 on
    /// another for the same tree. Reachability from the branch is a property of the history;
    /// resolvability is a property of a disk.
    /// </para>
    /// <para>
    /// A token naming a TREE or a BLOB is neither, and is excluded. §1 of the handoff quotes
    /// the tree both sides of the repaired tag share, which is a citation of something that
    /// plainly exists; counting it as a dead commit reference would have taxed the prose for
    /// being precise. The census caught it on the first run after that sentence was written,
    /// which is the census working — but the right repair was the classifier, not the
    /// sentence.
    /// </para>
    /// </remarks>
    [Fact]
    public void DeadCitationsDoNotGrow()
    {
        var s = Shared.Value;
        var dead = s.Occurrences
            .Select(o => o.Text).Distinct(StringComparer.Ordinal)
            .Where(t => LooksLikeCitation(t)
                        && !s.ResolvesToANonCommit.Contains(t)
                        && !(s.ResolvedTo[t] is string sha && s.Reachable.Contains(sha)))
            .ToArray();

        _output.WriteLine($"dead citation-shaped tokens: {dead.Length} (ceiling {DeadCitationsWhenWritten})");
        foreach (var group in s.Occurrences
                     .Where(o => dead.Contains(o.Text, StringComparer.Ordinal))
                     .GroupBy(o => o.File)
                     .OrderByDescending(g => g.Select(o => o.Text).Distinct().Count()))
        {
            _output.WriteLine($"  {group.Key}: {group.Select(o => o.Text).Distinct().Count()}");
        }

        Assert.True(dead.Length <= DeadCitationsWhenWritten,
            $"{dead.Length} citation-shaped tokens resolve to nothing; the ceiling is "
            + $"{DeadCitationsWhenWritten}. A citation written today names a commit that "
            + "exists today, so this number rises only when a rewrite fails to re-point "
            + "something — check the rewrite before raising the ceiling.");
    }
}
