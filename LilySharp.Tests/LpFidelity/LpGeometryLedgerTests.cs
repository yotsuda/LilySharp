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

using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds Lily#'s geometry against measurements taken from real LilyPond.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT a snapshot test. A snapshot pins Lily# to its own previous output, so a wrong
/// value that has been blessed once stays wrong and looks green forever. This pins Lily# to
/// LilyPond, and states the remaining difference as a number with a named cause.
/// </para>
/// <para>
/// Why it exists: LilyPond measurements used to be taken ad hoc into a scratch directory and
/// thrown away, so every session re-derived the same numbers — and one of them
/// (a bar-line optical correction attributed to a clef rather than to a stem direction) was
/// carried forward wrong through several handoffs because nothing held it. Committing the
/// probe sources and the measured values makes the corpus grow instead of evaporate.
/// </para>
/// <para>
/// To add an entry: write the .ly probe under audit/lp-geometry/probes/, add the matching
/// Lily# probe to <see cref="LpGeometryProbes"/>, run the probe script (see that directory's
/// README) and paste the LilyPond number into lp-geometry.json. Run this test; it will tell
/// you the residual to record.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LpGeometryLedgerTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LpGeometryLedgerTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private sealed record LedgerEntry(double LilyPond, double? Residual, string Why, string Probe, string Score);

    private static readonly Lazy<(IReadOnlyDictionary<string, LedgerEntry> Entries, double Tolerance)> Ledger =
        new(LoadLedger);

    private static string LedgerPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "audit", "lp-geometry", "lp-geometry.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "audit/lp-geometry/lp-geometry.json not found above " + AppContext.BaseDirectory);
    }

    private static (IReadOnlyDictionary<string, LedgerEntry>, double) LoadLedger()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(LedgerPath()));
        var root = doc.RootElement;
        double tolerance = root.GetProperty("tolerance").GetDouble();
        var entries = new Dictionary<string, LedgerEntry>();
        foreach (var e in root.GetProperty("entries").EnumerateObject())
        {
            var v = e.Value;
            double? residual = v.TryGetProperty("residual", out var r) && r.ValueKind != JsonValueKind.Null
                ? r.GetDouble()
                : null;
            entries[e.Name] = new LedgerEntry(
                v.GetProperty("lilypond").GetDouble(),
                residual,
                v.TryGetProperty("why", out var w) ? (w.GetString() ?? "") : "",
                v.TryGetProperty("probe", out var p) ? (p.GetString() ?? "") : "",
                v.TryGetProperty("score", out var s) ? (s.GetString() ?? "") : "");
        }
        return (entries, tolerance);
    }

    public static TheoryData<string> ProbeIds()
    {
        var data = new TheoryData<string>();
        foreach (var probe in LpGeometryProbes.All)
            data.Add(probe.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProbeIds))]
    public void Geometry_MatchesLilyPondWithinTheRecordedResidual(string id)
    {
        var probe = LpGeometryProbes.All.Single(p => p.Id == id);
        var (entries, tolerance) = Ledger.Value;

        Assert.True(entries.ContainsKey(id),
            $"'{id}' has no entry in audit/lp-geometry/lp-geometry.json. "
            + "Every probe must carry a LilyPond measurement — add one rather than "
            + "letting the probe assert nothing.");
        var entry = entries[id];

        var geometry = RenderedGeometry.Render(probe.Source);
        double actual = probe.Measure(geometry);
        double actualResidual = actual - entry.LilyPond;

        if (entry.Residual is null)
        {
            Assert.Fail(
                $"'{id}' has no recorded residual yet.\n"
                + $"  LilyPond  {entry.LilyPond:F6}\n"
                + $"  Lily#     {actual:F9}\n"
                + $"  residual  {actualResidual:F9}\n"
                + "Record that residual in lp-geometry.json. If it is not 0, the 'why' must "
                + "name the defect that accounts for it.\n"
                + "Drawn geometry:\n" + geometry.Describe());
            return;
        }

        double expected = entry.Residual.Value;

        if (Math.Abs(expected) > tolerance && string.IsNullOrWhiteSpace(entry.Why))
        {
            Assert.Fail(
                $"'{id}' records a non-zero residual ({expected:F6}) with no 'why'.\n"
                + "An unexplained gap from LilyPond is an open bug, not a baseline. Name the "
                + "cause, or fix it and set the residual to 0.");
        }

        double drift = actualResidual - expected;
        if (Math.Abs(drift) <= tolerance)
            return;

        string verdict = Math.Abs(actualResidual) > Math.Abs(expected)
            ? "MOVED AWAY FROM LilyPond (regression)"
            : "MOVED TOWARD LilyPond — good, but record it so the improvement is in the diff";

        Assert.Fail(
            $"'{id}' {verdict}.\n"
            + $"  LilyPond           {entry.LilyPond:F6}   (probe {entry.Probe}, score {entry.Score})\n"
            + $"  Lily#              {actual:F9}\n"
            + $"  residual now       {actualResidual:F9}\n"
            + $"  residual recorded  {expected:F9}\n"
            + $"  drift              {drift:F9}\n"
            + (string.IsNullOrWhiteSpace(entry.Why) ? "" : $"  recorded cause     {entry.Why}\n")
            + "Drawn geometry:\n" + geometry.Describe());
    }

    /// <summary>
    /// The headline fidelity number: the total distance from LilyPond across the corpus.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an assertion on a threshold — a threshold would either be slack
    /// enough to be meaningless or would fail the build for an unrelated new probe. It
    /// prints, so the number is visible in the run and its trend is reviewable.
    /// </remarks>
    [Fact]
    public void Corpus_ReportsTotalDivergenceFromLilyPond()
    {
        var (entries, tolerance) = Ledger.Value;
        var report = new StringBuilder();
        double total = 0;
        int exact = 0, seeded = 0;

        foreach (var probe in LpGeometryProbes.All)
        {
            if (!entries.TryGetValue(probe.Id, out var entry) || entry.Residual is not { } residual)
                continue;
            seeded++;
            total += Math.Abs(residual);
            if (Math.Abs(residual) <= tolerance)
                exact++;
            else
                report.AppendLine($"  {residual,10:F6}   {probe.Id}");
        }

        Assert.True(seeded > 0, "the LP fidelity corpus is empty");

        // Written to test output, NOT asserted. Assert.True(true, msg) prints nothing —
        // a headline number nobody can read is not a headline number.
        // Visible with: dotnet test --logger 'console;verbosity=detailed'
        _output.WriteLine($"LP fidelity: {exact}/{seeded} exact, total |residual| = {total:F6} ss");
        if (report.Length > 0)
        {
            _output.WriteLine("divergent entries (residual, id):");
            _output.WriteLine(report.ToString().TrimEnd());
        }
    }
}
