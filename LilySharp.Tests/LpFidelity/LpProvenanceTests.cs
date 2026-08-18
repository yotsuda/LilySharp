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
    /// The 13 it started at are a real backlog, not noise, and the list the test prints is
    /// worth reading once: NoteheadHeight, NoteheadHalfHeight, RestHeight and RestWidth
    /// are all in it — the nominal boxes this guard exists because of. Two of them were
    /// still deciding spacing when it was written.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// ⚠️⚠️ IT WENT 13 → 20 ON 2026-08-04, AND NOT BECAUSE ANYTHING WAS ADDED. The walk that
    /// looks for a marker was reading a fourteen-line window and SKIPPING blank lines rather
    /// than stopping at them, so a constant could inherit the marker of a group it is not in.
    /// The comment beside that loop had described the correct rule the whole time; only the
    /// code disagreed with it. HANDOFF recorded the symptom three sessions ago — deleting one
    /// LILYSHARP-OWN line dropped FOUR constants to unsourced — and called the baseline "that
    /// much too lenient" without saying by how much. It was seven.
    /// </para>
    /// <para>
    /// ⚠️ THAT NUMBER WAS NEVER PAID AS DEBT. Seeing the twenty was what made them
    /// answerable, and seven turned out not to be provenance questions at all:
    /// </para>
    /// <list type="bullet">
    /// <item>FOUR HAD NO READER. <c>StemUpAttachY</c>, <c>StemDownAttachY</c>,
    /// <c>NoteheadHeight</c> and <c>MaxStiffness</c> were referenced by nothing but their own
    /// declarations. Sourcing an unread constant documents a fiction; they were deleted,
    /// which HANDOFF 5.2.1⑥ had already prescribed and which
    /// <see cref="EveryEngravingDefault_StatesWhereItsNumberCameFrom"/> cannot ask for —
    /// it checks that a number states an origin, never that anything computes with it.</item>
    /// <item>ONE WAS THE INSTRUMENT AGAIN. <c>LyricTextFontSize</c> carries as thorough a
    /// LILYPOND-REF as this file has, twenty-four lines up, and the walk had a fourteen-line
    /// cap. See the note where that cap used to be.</item>
    /// <item>TWO WERE ANSWERABLE ON SIGHT. <c>StaffMiddle</c> is a frame rather than a
    /// quantity, and <c>NoteheadDoubleWholeWidth</c> is hand-tuned because the metrics
    /// extractor omits a glyph LilyPond does have — both now say so.</item>
    /// </list>
    /// <para>
    /// The thirteen left were real provenance work, and none was a five-minute job:
    /// <c>FlagWidth</c>, <c>FlagBaseHeight</c>, <c>FlagHeightIncrement</c>,
    /// <c>NoteheadHalfHeight</c>, <c>RestHeight</c>, <c>RestWidth</c>, <c>DotGap</c>,
    /// <c>RepeatDotRadius</c>, <c>RepeatDotPosition1</c>, <c>RepeatDotPosition2</c> here, and
    /// <c>PageWidth</c>, <c>StaffHeight</c>, <c>SystemSpacing</c> in LayoutOptions.
    /// ⚠️ <c>DotGap</c> has no constant to cite: LilyPond's DotColumn padding is the callback
    /// <c>dot-column-interface::pad-by-one-dot-width</c>, so that one is a measurement.
    /// </para>
    /// <para>
    /// ⚠️⚠️ 13 → 0 ON 2026-08-04. The backlog is paid and the baseline is a real zero, so the
    /// next number that states no origin fails on the commit that introduces it. None of the
    /// thirteen was silenced with a bare <c>LILYSHARP-OWN</c>: the marker only buys the right
    /// to say WHY, and every why here survived being measured.
    /// </para>
    /// <list type="bullet">
    /// <item>THREE WERE ANSWERED BY ASKING 2.26.0. <c>make-colon-bar-line</c> returns a
    /// stencil of Y extent (-0.725 . 0.725) with a dot half-height of 0.225, i.e. dot centres
    /// at ±0.5 staff spaces — which sources both repeat-dot positions AND shows
    /// <c>RepeatDotRadius</c> is ours and 0.025 short of the glyph LilyPond draws.</item>
    /// <item>SIX WERE NOMINAL BOXES WITH NO LILYPOND COUNTERPART. A Flag's, a Rest's and a
    /// NoteHead's extent are all their stencils' there, so the three <c>Flag*</c> numbers, the
    /// two <c>Rest*</c> and <c>NoteheadHalfHeight</c> are Lily#'s own — read by RESERVATION
    /// while the drawing goes through GlyphMetrics, the two-spellings shape HANDOFF 7.7 names.
    /// Each now carries the size of its own disagreement; the rest box is the worst, 1.812
    /// short of a quarter rest and centred where a whole rest does not sit.</item>
    /// <item><c>DotGap</c> WAS CALLED UNCITEABLE AND IS NOT. Both of its roles are one
    /// LilyPond quantity — the dot's own width, 0.450000 — computed in
    /// <c>ly:dots::print</c> and <c>dot-column-interface::pad-by-one-dot-width</c>. Ours is
    /// 0.3. A callback is an address like any other; what it forbids is COPYING a literal,
    /// not naming where the number comes from.</item>
    /// <item><c>SystemSpacing</c> HAD A READER WHOSE ANSWER NEVER SURVIVES. Perturbing it to
    /// 37 and to -50 leaves the whole suite green, while the spec that really decides the
    /// distance fails 85 tests when moved — the positive control without which the silence
    /// would mean nothing. It is a deletion candidate, recorded at the declaration.</item>
    /// <item><c>PageWidth</c> and <c>StaffHeight</c> were the easy two: a4 out of
    /// <c>paper-alist</c>, and a frame rather than a quantity (five lines one space apart come
    /// to four), the same answer <c>StaffMiddle</c> got.</item>
    /// </list>
    /// <para>
    /// ★ WHAT THE PAYING TURNED UP, which is the argument for the ratchet: a second spelling
    /// of <c>NoteheadHalfHeight</c> as a bare literal in FingeringEngraver (now reading the
    /// one home), and five separate reservations that do not match the ink they reserve. None
    /// of that is visible to a test that only asks whether a number states an origin — but
    /// writing the origin down is what forced somebody to look at each one.
    /// </para>
    /// <para>
    /// ⚠️ THE MARKER MUST BE IN THE BLOCK, and this bit twice while paying the six down: a
    /// section header separated from its declarations by a BLANK LINE reaches none of them,
    /// because the walk stops at blank lines — deliberately, and it is the same rule whose
    /// absence made the count read 20. Group headers work only when they abut the group, the
    /// way the <c>Rest*</c> four do.
    /// </para>
    /// <para>
    /// ★ Three WERE sourced in an earlier commit rather than counted: the StaffGrouper
    /// <c>staff-staff-spacing</c> triple, whose section header had named the grob in prose for
    /// a long time without giving the address. ⚠️ That same commit claimed the fourth value,
    /// <c>stretchability . 5</c>, was "spelled nowhere in Core at all". FALSE — a grep's false
    /// negative written down as a finding. It has been in StaffSpacingParameters since
    /// 2026-02-22, and perturbing it drops five observers, two of them ledger points.
    /// </para>
    /// </remarks>
    private const int UnsourcedBaseline = 0;

    /// <summary>Files whose numbers are engraving policy rather than local arithmetic.</summary>
    /// <remarks>
    /// Segments, not a literal path. Written with backslashes these three were a filename
    /// containing '\' on Linux, so this test reported "did it move?" on both ubuntu legs of CI
    /// — every push at least back to 2026-08-15, unnoticed because the Windows suite was green
    /// (2026-08-19, session 212). Same family as the <c>lysc.exe</c> the CLI tests named:
    /// a platform-shaped literal in a test that never runs on the other platform locally.
    /// </remarks>
    private static readonly string[] DefaultsFiles =
    {
        Path.Combine("LilySharp.Core", "Svg", "EngravingDefaults.cs"),
        Path.Combine("LilySharp.Core", "Svg", "Layout", "LayoutOptions.cs"),
        Path.Combine("LilySharp.Core", "Svg", "Layout", "VerticalSpacingParameters.cs"),
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

    // ⚠️ THERE IS NO LOOKBACK CAP, and removing it was a correction rather than a
    //   relaxation. A `MarkerLookback = 14` used to be described as doing two jobs — reach
    //   far enough for a real doc comment, and stop a marker being borrowed from the
    //   constant above — which is how it came to do neither. It did not stop borrowing,
    //   because the walk skipped blank lines and so reached fourteen lines across anything
    //   at all; and it did not reach far enough, because it counted LyricTextFontSize as
    //   unsourced when that constant's LILYPOND-REF sits twenty-four lines up in one of the
    //   best-documented blocks in this file. The blank line is the boundary. A block is as
    //   long as its author needed.

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
                for (int k = i - 1; k >= 0; k--)
                {
                    // A blank line ends the block that belongs to this declaration, so a
                    // marker above it belongs to the previous constant, not this one.
                    if (lines[k].Trim().Length == 0)
                        break;
                    if (lines[k].Contains("LILYPOND-REF") || lines[k].Contains("LILYSHARP-OWN"))
                    {
                        sourced = true;
                        break;
                    }
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
