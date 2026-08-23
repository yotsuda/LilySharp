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

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds step 3.5 of the end-of-session checklist: when §1 of <c>docs/HANDOFF.md</c> is
/// rewritten, the session block it displaces must land in <c>docs/HANDOFF-ARCHIVE.md</c>
/// rather than simply stop existing.
/// </summary>
/// <remarks>
/// <para>
/// The rule is old, correct, and carries its own worked example: the archive exists because
/// nobody was doing this, HANDOFF.md reached 1.7 MB with §1 alone at 86% of it, and the
/// checklist says in as many words that "the design was there from the start — only the
/// procedure was not being run". It then says the step takes five minutes per session and
/// that letting it accumulate means nobody does it.
/// </para>
/// <para>
/// It was still not being run. Archiving one block by hand turned up nine session numbers
/// between the oldest block and the newest with no block at all, and three of them are
/// demonstrably not numbering skips: the 232 block opens its own start-of-session check with
/// "HEAD 175b8b12 (231's closing handoff, matching §1)", and the 234 block does the same for
/// 233. Those sessions wrote a §1. The next session overwrote it instead of moving it, and
/// the only trace left is another session's quotation of its HEAD.
/// </para>
/// <para>
/// So this is the machine the previous session's first bone asks for: when a rule has been
/// broken more than once, stop strengthening the wording and write an instrument. Rewording
/// has been tried here — the checklist step already carries two warnings and a cost estimate.
/// </para>
/// <para>
/// <b>What it asserts, and why that shape.</b> The failure is invisible in the working tree:
/// the block is simply gone, and no count anywhere moves. What IS visible is the seam. §1
/// keeps the current session plus exactly one predecessor; the archive must resume at the one
/// before that. A session that forgets makes the seam skip a number, and it does so in the
/// very commit that forgot, which is the only moment anyone can still recover the block from
/// their own working tree.
/// </para>
/// <para>
/// Deliberately NOT asserted: that the numbering be gapless. Nine numbers already have no
/// block and they are not all losses — a session can legitimately produce none (the 87 block
/// says outright that it was reconstructed from commit messages because that session never
/// wrote a §1), and older blocks span several legs under one heading. Those are census-
/// counted under a ceiling instead, so the backlog is visible and can only shrink.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class HandoffArchiveContinuityTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public HandoffArchiveContinuityTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// Session numbers between the oldest and newest archived block that carry no block, when
    /// this guard was written. A CEILING — the backlog may shrink and must not grow.
    /// </summary>
    /// <remarks>
    /// Not all nine are known losses. 230, 231 and 233 are, on the evidence of the neighbouring
    /// blocks quoting their closing HEADs. The rest are unclassified: this repository has both
    /// sessions that wrote no §1 at all and headings that cover several legs at once, and
    /// deciding which is which for a session from months ago is archaeology that buys nothing.
    /// The number is here so that the loss is written down somewhere, which it was not.
    /// </remarks>
    private const int ArchiveGapsWhenWritten = 9;

    /// <summary>
    /// Every heading form the archive has actually used for a session block. Matching loosely
    /// is the point: the strict form <c>## 以下は第N セッションの経緯</c> misses the per-leg
    /// headings (<c>…第Nセッション第M便の経緯</c>) and the two that carry a parenthetical, and
    /// a first measurement with the strict form reported sixteen more gaps than exist. A
    /// census that over-reports is not conservative — it makes the real losses unfindable.
    /// </summary>
    private static readonly Regex BlockHeading =
        new(@"^##\s*以下は第(?<n>\d+)セッション", RegexOptions.Compiled);

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

    private static IReadOnlyList<int> BlocksIn(string fileName)
    {
        var path = Path.Combine(DocsDir(), fileName);
        Assert.True(File.Exists(path), $"docs/{fileName} not found at {path}");
        return File.ReadAllLines(path)
            .Select(l => BlockHeading.Match(l))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups["n"].Value))
            .ToArray();
    }

    /// <summary>
    /// §1 carries the current session plus exactly one predecessor. A second one means a
    /// session appended where it should have displaced.
    /// </summary>
    [Fact]
    public void TheHandoffKeepsExactlyOnePredecessorBlock()
    {
        var blocks = BlocksIn("HANDOFF.md");
        Assert.True(blocks.Count == 1,
            $"docs/HANDOFF.md holds {blocks.Count} session blocks ({string.Join(", ", blocks)}); "
            + "§1 keeps the current session plus exactly ONE predecessor. More than one means a "
            + "session added its own without moving the oldest to the archive — checklist step "
            + "3.5, the step whose absence grew this file to 1.7 MB. Fewer than one means the "
            + "predecessor was dropped rather than archived. Move the surplus block verbatim to "
            + "the top of HANDOFF-ARCHIVE.md; do not summarise it on the way.");
    }

    /// <summary>
    /// The archive must resume exactly one session before the block §1 still holds. This is
    /// the seam, and it goes red in the commit that breaks it — while the block is still
    /// recoverable from the author's own working tree.
    /// </summary>
    [Fact]
    public void TheArchiveResumesWhereTheHandoffStops()
    {
        var handoff = BlocksIn("HANDOFF.md");
        Assert.NotEmpty(handoff);
        int kept = handoff.Max();
        int archived = BlocksIn("HANDOFF-ARCHIVE.md").Max();

        _output.WriteLine($"§1 keeps {kept}, archive resumes at {archived}");
        Assert.True(archived == kept - 1,
            $"§1 still holds session {kept}'s block and the archive's newest is {archived}; it "
            + $"should be {kept - 1}. The block(s) in between were displaced from §1 and never "
            + "landed anywhere — and nothing else in the tree records that they existed, which "
            + "is why sessions 230, 231 and 233 survive only as another session's quotation of "
            + "their closing HEAD. If a session genuinely produced no block, say so in the "
            + "archive with a heading that says why, the way session 87's does; do not leave "
            + "the seam to be read as an accident.");
    }

    /// <summary>
    /// The census of numbers with no block at all. May shrink; must not grow.
    /// </summary>
    /// <remarks>
    /// <b>Blind at the top, on purpose.</b> Gaps are counted between the oldest and newest
    /// block, so dropping the NEWEST block does not raise the count — it lowers the ceiling
    /// of the range instead. Poisoning found this: deleting session 234's heading left this
    /// count at nine and only <see cref="TheArchiveResumesWhereTheHandoffStops"/> went red.
    /// That division is the right one — the seam owns the top and catches the drop in the
    /// commit that makes it, while this census owns the interior, where a block can only be
    /// lost by deleting it long after the fact. But the pair is load-bearing: neither of
    /// these two tests may be weakened on the argument that the other covers it.
    /// </remarks>
    [Fact]
    public void ArchiveGapsDoNotGrow()
    {
        var nums = BlocksIn("HANDOFF-ARCHIVE.md").ToHashSet();
        int lo = nums.Min(), hi = nums.Max();
        var gaps = Enumerable.Range(lo, hi - lo + 1).Where(n => !nums.Contains(n)).ToArray();

        _output.WriteLine(
            $"archive covers {lo}..{hi}, {nums.Count} sessions, {gaps.Length} without a block: "
            + string.Join(", ", gaps));

        Assert.True(gaps.Length <= ArchiveGapsWhenWritten,
            $"{gaps.Length} session numbers in {lo}..{hi} have no block; the ceiling is "
            + $"{ArchiveGapsWhenWritten}. All of them: " + string.Join(", ", gaps)
            + ". A block is only ever added, so this number rises when one is deleted or when "
            + "the seam was broken and the ceiling raised to hide it. Lower it when a block is "
            + "recovered — the displaced text is still in the history of docs/HANDOFF.md at the "
            + "commit that displaced it.");
    }
}
