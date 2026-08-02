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

using System.IO;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Step 1 of the probe OCTAVE AUDIT: writes every ledger probe's Lily# source to disk so the
/// twins can be GENERATED with <c>lysc ly</c> and compared against the hand-written .ly books.
/// </summary>
/// <remarks>
/// WHY THE AUDIT EXISTS. Lily#'s absolute <c>c</c> is staff position −6 = C4, so Lily#
/// <c>c</c> IS LilyPond <c>c'</c>. A probe whose Lily# side was written with LilyPond's
/// spelling is a whole octave out, and then the two engines are not being asked the same
/// question — which has bitten three times, most recently <c>flag.up.reach</c> (session 71),
/// where a −1.613200 residual turned out to be an octave and not a defect at all.
/// <para>
/// SKIPPED ON PURPOSE: it writes ~400 files to TEMP and asserts nothing about Lily#. Run it
/// by hand, then the two steps that follow:
/// <code>
/// dotnet test --filter "FullyQualifiedName~ProbeSourceDump" -- xUnit.ExplicitTests=on
/// # or drop the Skip= for one run, then:
/// #   lysc ly &lt;TEMP&gt;\lys-probe-sources\&lt;id&gt;.lys -o &lt;…&gt;\gen\&lt;id&gt;.ly   (one per distinct source)
/// #   audit\scripts\Audit-ProbeOctaves.ps1
/// </code>
/// </para>
/// <para>
/// LAST RUN 2026-08-02 (session 72): 232 distinct books behind 398 ledger entries — 127 books
/// (210 entries) MATCH, 18 books SAME-PITCH-SET (a <c>\repeat unfold</c> or a reused variable
/// in the .ly, so the run lengths differ but no pitch does), and ZERO mismatches. 77 books
/// (146 entries) are NOT readable by a textual comparison because one side is
/// <c>\lyricmode</c> or <c>\relative</c>; those are still unverified.
/// </para>
/// </remarks>
public class ProbeSourceDump
{
    [Fact(Skip = "Step 1 of the octave audit — run by hand; see audit/scripts/Audit-ProbeOctaves.ps1")]
    public void WritesEveryProbeSourceForTheOctaveAudit()
    {
        string dir = Path.Combine(
            Environment.GetEnvironmentVariable("TEMP") ?? ".", "lys-probe-sources");
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.lys")) File.Delete(f);

        foreach (var probe in LpGeometryProbes.All)
            File.WriteAllText(Path.Combine(dir, probe.Id + ".lys"), probe.Source);

        Assert.NotEmpty(Directory.GetFiles(dir, "*.lys"));
    }
}
