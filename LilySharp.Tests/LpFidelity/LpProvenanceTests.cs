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
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds the engraving DEFAULTS to a stated origin: every number is either LilyPond's,
/// with a <c>LILYPOND-REF</c> saying where, or deliberately Lily#'s own, with a
/// <c>LILYSHARP-OWN</c> saying why.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of what invented constants have actually cost. The single-page
/// path priced the gap between systems at <c>SystemSpacing * 0.5</c> — four times
/// LilyPond's system-system-spacing padding of 1 — directly under a LILYPOND-REF
/// pointing at the function it was not implementing. It survived for as long as it did
/// because nothing in the corpus could see it: the skylines were thin enough that the ink
/// term stayed below basic-distance, and it only surfaced when the clef joined them and
/// pushed a ledger entry 1.110000 off. The stretchability divided by 60 with a 0.1 floor
/// has no counterpart in LilyPond at all. A notehead's skyline box was a nominal staff
/// space while the font's own ink was already sitting in GlyphMetricsGenerated.
/// </para>
/// <para>
/// None of those were hard to see once looked at. The problem was that nothing MADE
/// anyone look. So this is a ratchet, in the same shape as the LP geometry ledger: a
/// number that is allowed to fall and never to rise. It does not demand that the backlog
/// be cleared to go green — it demands that the next constant added carry its origin.
/// </para>
/// <para>
/// Scope is deliberately the three DEFAULTS files rather than all of Core. Those are
/// where a bare number becomes engraving policy; a local in a layout algorithm is a
/// different kind of thing and is covered by the LP fidelity corpus instead.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LpProvenanceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LpProvenanceTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The count of unsourced constants at the time this guard was written.
    /// </summary>
    /// <remarks>
    /// LOWER THIS, NEVER RAISE IT. Sourcing a constant means finding the LilyPond
    /// expression it comes from and citing it, or deciding it is Lily#'s own and saying so
    /// in one line — both of which are the work, not paperwork around it. If a change
    /// needs this number raised, the constant it adds has no stated origin, and that is
    /// the thing to fix.
    /// </remarks>
    /// <remarks>
    /// The 13 it starts at are a real backlog, not noise, and the list the test prints is
    /// worth reading once: NoteheadHeight, NoteheadHalfHeight, RestHeight and RestWidth
    /// are all in it — the nominal boxes this guard exists because of. Two of them were
    /// still deciding spacing when it was written.
    /// </remarks>
    private const int UnsourcedBaseline = 13;

    /// <summary>Files whose numbers are engraving policy rather than local arithmetic.</summary>
    private static readonly string[] DefaultsFiles =
    {
        @"LilySharp.Core\Svg\EngravingDefaults.cs",
        @"LilySharp.Core\Svg\Layout\LayoutOptions.cs",
        @"LilySharp.Core\Svg\Layout\VerticalSpacingParameters.cs",
    };

    /// <summary>A declaration that fixes a NUMBER (not a type, string or bool).</summary>
    private static readonly Regex NumericDeclaration = new(
        @"^\s*public\s+(?:const|static\s+readonly)?\s*(?:double|int)\s+(\w+)\s*(?:=|\{\s*get;\s*init;\s*\}\s*=)\s*[-\d]",
        RegexOptions.Compiled);

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

    /// <summary>
    /// How far back a marker may sit: far enough for a doc comment with a summary and a
    /// remark, short enough that it cannot be borrowed from the constant above.
    /// </summary>
    private const int MarkerLookback = 14;

    [Fact]
    public void EveryEngravingDefault_StatesWhereItsNumberCameFrom()
    {
        var root = RepoRoot();
        var unsourced = new List<string>();

        foreach (var rel in DefaultsFiles)
        {
            var path = Path.Combine(root, rel);
            Assert.True(File.Exists(path), $"defaults file missing: {rel} — did it move?");
            var lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                var m = NumericDeclaration.Match(lines[i]);
                if (!m.Success)
                    continue;

                bool sourced = false;
                for (int k = Math.Max(0, i - MarkerLookback); k < i && !sourced; k++)
                {
                    // A blank line ends the block that belongs to this declaration, so a
                    // marker above it belongs to the previous constant, not this one.
                    if (lines[k].Trim().Length == 0)
                        continue;
                    sourced = lines[k].Contains("LILYPOND-REF") || lines[k].Contains("LILYSHARP-OWN");
                }

                if (!sourced)
                    unsourced.Add($"{rel}:{i + 1}  {m.Groups[1].Value}");
            }
        }

        _output.WriteLine($"unsourced engraving defaults: {unsourced.Count} (baseline {UnsourcedBaseline})");
        foreach (var u in unsourced)
            _output.WriteLine("  " + u);

        Assert.True(unsourced.Count <= UnsourcedBaseline,
            $"{unsourced.Count} engraving defaults state no origin; the baseline is {UnsourcedBaseline}.\n"
            + "Every number here is either LilyPond's — cite it with LILYPOND-REF: lily/<file>.cc:<line>\n"
            + "— or deliberately Lily#'s own, in which case say so with LILYSHARP-OWN: <why> on one line.\n"
            + "Both are the work itself: a number nobody can trace is a number nobody can check.\n\n"
            + string.Join("\n", unsourced));
    }
}
