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
/// Builds <c>audit/magic_constants.csv</c> — every numeric literal in the layout files, with
/// the <c>LILYPOND-REF</c> comment near it, if any — and holds the committed file to it.
/// </summary>
/// <remarks>
/// <para>
/// The census answers one question: which numbers in the engine can name where they come
/// from. <c>Green</c> has a <c>LILYPOND-REF</c> within five lines; <c>Yellow</c> has only a
/// file-level one, or sits near a word admitting it is a guess; <c>Red</c> has neither.
/// It is a TRIAGE tool, not a verdict — plenty of Red is a loop bound or a buffer size.
/// </para>
/// <para>
/// ⚠️ IT WAS A POWERSHELL SCRIPT WITH NO OBSERVER, AND THAT IS WHY IT IS HERE NOW — the same
/// repair, for the same reason, as <see cref="ApproximationInventoryTests"/> (session 276,
/// which moved the approximation census out of Python and deleted the script). A generated
/// file nothing regenerates goes stale silently, and a stale census reads exactly like a
/// clean one. MEASURED: when session 278 finally re-ran the script the file went 662 → 917
/// rows and Red 121 → 42, having been wrong for long enough that the previous handoff was
/// carrying <c>RepeatDotPosition1/2</c> at line 119/121 when the real lines were ~700 later.
/// </para>
/// <para>
/// <b>Why the generator moved rather than gaining a checker.</b> A C# checker beside a
/// PowerShell generator would be TWO SPELLINGS of one classifier (RULES §5.2.1②), and they
/// would drift the way the document drifted from the tree. So the classifier itself moved,
/// <c>audit/scripts/Find-MagicConstants.ps1</c> was deleted, and the test writes the file it
/// checks. One spelling, and the thing that regenerates is the thing that runs on every build.
/// </para>
/// <para>
/// ⚠️ <b>What this guards and what it does not.</b> It guards that the CSV is NOT STALE — its
/// rows are the ones today's sources produce. It does NOT guard that the classification is
/// USEFUL, and one bucket is known not to be: session 278 measured that 561 of the Yellow rows
/// carry the same <c>(file-level)</c> reference, which is just the first REF in the file's
/// opening 60 lines pasted onto every unattributed constant below it. Read honestly, the
/// constants with no provenance are Red + Yellow, not Red alone. Sharpening that is a rebuild
/// of the audit and is deliberately NOT done here: this commit's job is to stop the rot, and
/// changing the classifier in the same breath would leave nothing to compare against.
/// </para>
/// <para>
/// ⚠️ <b>Two hazards the port removes.</b> ⑴ PowerShell's <c>-match</c> is case-INSENSITIVE by
/// default while <c>[regex]::Matches</c> is not, so the script mixed both in one file with
/// nothing saying which was which; each regex below states its own casing, and the ones that
/// were insensitive are marked <c>IgnoreCase</c> — with <c>CultureInvariant</c>, because this
/// suite runs on a ja-JP machine and on CI. ⑵ A missing target file printed a warning and was
/// skipped, so the census could lose a whole file and still look complete — four names had
/// been doing exactly that until session 278 removed them. Here it FAILS.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class MagicConstantInventoryTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MagicConstantInventoryTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    /// <summary>
    /// Blesses a regenerated census. The SAME variable
    /// <see cref="ApproximationInventoryTests"/> uses, and deliberately not the snapshot one:
    /// both are censuses of the tree taken by a test, and a reviewer blessing one expects to
    /// be shown the other's diff too — where a picture re-base is a different act entirely.
    /// </summary>
    private static readonly bool UpdateDocs =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_DOCS") == "1";

    private const string Relative = "audit/magic_constants.csv";

    /// <summary>
    /// The layout files the census covers, verbatim from the script it replaces.
    /// </summary>
    /// <remarks>
    /// ⚠️ A HAND-KEPT LIST, and that is a real limit: a new file under <c>Svg/Layout</c> is
    /// invisible here until somebody adds it, and nothing will say so. It is kept hand-written
    /// anyway because the census is about the files where a bare number means a LilyPond
    /// quantity; sweeping all of Core would drown that in parser and exporter arithmetic.
    /// ⚠️ A name that DISAPPEARS from the tree now fails the run (see the class remarks).
    /// Check such a name with <c>git log --diff-filter=D</c> on the path before removing it:
    /// four entries left this list on 2026-08-28 because the files had been deleted as dead
    /// code, which loses no coverage — a RENAME would.
    /// </remarks>
    private static readonly string[] Targets =
    [
        "Svg/Layout/SpacingRules.cs",
        "Svg/Layout/LyricEngraver.cs",
        "Svg/Layout/MultiStaffLayouter.cs",
        "Svg/Layout/PageBreaker.cs",
        "Svg/Layout/PageLayouter.cs",
        "Svg/Layout/SkylineBuilder.cs",
        "Svg/Layout/Skyline.cs",
        "Svg/Layout/HorizontalSkyline.cs",
        "Svg/Layout/VerticalSkyline.cs",
        "Svg/Layout/AccidentalPlacement.cs",
        "Svg/Layout/NoteCollision.cs",
        "Svg/Layout/BeamScoringProblem.cs",
        "Svg/Layout/BeamConfiguration.cs",
        "Svg/Layout/BeamQuantParameters.cs",
        "Svg/Layout/SlurScoringProblem.cs",
        "Svg/Layout/SlurScoreParameters.cs",
        "Svg/Layout/TieFormattingProblem.cs",
        "Svg/Layout/TieDetails.cs",
        "Svg/Layout/BreakAlignSpacing.cs",
        "Svg/Layout/ElementCoordinator.cs",
        "Svg/Layout/OutsideStaffStacker.cs",
        "Svg/Layout/HaraKiri.cs",
        "Svg/Layout/MeasureLayouter.cs",
        "Svg/Layout/LayoutEngine.cs",
        "Svg/Layout/ScoreLayout.cs",
        "Svg/Layout/SpringSolver.cs",
        "Svg/Layout/Spring.cs",
        "Svg/Layout/StaffSpacingParameters.cs",
        "Svg/Layout/NoteSpacingParameters.cs",
        "Svg/Layout/VerticalSpacingParameters.cs",
        "Svg/Layout/GraceSpacingParameters.cs",
        "Svg/Layout/KnuthPlassBreaker.cs",
        "Svg/Layout/ArticulationEngraver.cs",
        "Svg/Layout/HairpinEngraver.cs",
        "Svg/Layout/DynamicEngraver.cs",
        "Svg/Layout/TextSpannerEngraver.cs",
        "Svg/Layout/TupletBracketEngraver.cs",
        "Svg/Layout/OttavaBracketEngraver.cs",
        "Svg/Layout/PedalEngraver.cs",
        "Svg/Layout/GraceNoteEngraver.cs",
        "Svg/EngravingDefaults.cs",
        "Svg/PaperSettings.cs",
        "Svg/Layout/GlyphMetrics.cs",
        "Svg/EmmentalerGlyphs.cs",
    ];

    /// <summary>How far a <c>LILYPOND-REF</c> may be and still count as attached.</summary>
    private const int Context = 5;

    /// <summary>
    /// A file-level reference is one in the opening 60 lines — the header block. Below that a
    /// REF belongs to the code around it, not to the file.
    /// </summary>
    private const int FileLevelRefLimit = 60;

    /// <summary>
    /// A decimal, or an integer of two digits or more, not glued to a word or a dot. Small
    /// integers are excluded because indices and arities would swamp everything else.
    /// </summary>
    /// <remarks>CASE-SENSITIVE, as <c>[regex]::Matches</c> was.</remarks>
    private static readonly Regex Literal = new(@"(?<![\w.])(\d+\.\d+|\d{2,})(?![\w.])");

    /// <remarks>Case-INSENSITIVE, as PowerShell's <c>-match</c> was.</remarks>
    private static readonly Regex Reference =
        new("LILYPOND-REF[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Blank = new(@"^\s*$");
    private static readonly Regex LineComment = new(@"^\s*//");

    /// <summary>
    /// Words that admit the number is a guess. Near one, an otherwise unattributed constant is
    /// Yellow rather than Red — it has no provenance, but it is not pretending to have one.
    /// </summary>
    /// <remarks>Case-INSENSITIVE, as PowerShell's <c>-match</c> was.</remarks>
    private static readonly Regex Admission = new(
        "approximat|rough|simplif|heuristic|estimate|fallback|placeholder|stub|TODO|FIXME|"
        + "HACK|NOT YET",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Numbers that are not quantities: buffer sizes, array arities, colours, format strings.
    /// </summary>
    /// <remarks>Case-INSENSITIVE, as PowerShell's <c>-match</c> was.</remarks>
    private static readonly Regex NotAQuantity = new(
        @"StringBuilder\s*\(|new\s+\w+\s*\[\s*\d+\s*\]\s*;|0x[0-9a-fA-F]+|ToString\s*\(|"
        + @"Format\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record Row(string File, int Line, string Literals, string Text,
        string NearbyRef, string Decision);

    /// <summary>Renders the CSV, and returns the rows so the totals can be printed.</summary>
    private static (string Text, List<Row> Rows) Build(string root)
    {
        var rows = new List<Row>();

        foreach (var rel in Targets)
        {
            var path = Path.Combine(root, "LilySharp.Core",
                rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                $"the census names {rel}, which is not in the tree. A file DELETED as dead code "
                + "should leave this list (coverage is unchanged); a file RENAMED must be "
                + "renamed here too, or the census loses it in silence. Tell them apart with "
                + $"`git log --diff-filter=D -- LilySharp.Core/{rel}`.");

            var lines = File.ReadAllLines(path);

            // Where every reference in this file sits, so the file-level fallback below can be
            // decided once rather than re-scanned per line.
            var refLines = new List<(int Line, string Text)>();
            for (int k = 0; k < lines.Length; k++)
            {
                var m = Reference.Match(lines[k]);
                if (m.Success)
                    refLines.Add((k, m.Value));
            }
            bool hasFileLevelRef = refLines.Count > 0 && refLines[0].Line < FileLevelRefLimit;
            string fileLevelRef = hasFileLevelRef ? refLines[0].Text.Trim() : "";

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Blank.IsMatch(line))
                    continue;
                var literals = Literal.Matches(line);
                if (literals.Count == 0)
                    continue;
                // The reference itself carries the numbers of the LilyPond source it cites.
                if (Reference.IsMatch(line))
                    continue;
                if (LineComment.IsMatch(line))
                    continue;

                int lo = Math.Max(0, i - Context);
                int hi = Math.Min(lines.Length - 1, i + Context);
                string nearbyRef = "";
                for (int j = lo; j <= hi; j++)
                {
                    var m = Reference.Match(lines[j]);
                    if (m.Success)
                    {
                        nearbyRef = m.Value.Trim();
                        break;
                    }
                }

                string decision;
                if (nearbyRef.Length > 0)
                {
                    decision = "Green";
                }
                else if (hasFileLevelRef)
                {
                    decision = "Yellow";
                    nearbyRef = $"(file-level) {fileLevelRef}";
                }
                else
                {
                    decision = "Red";
                }

                if (decision == "Red"
                    && Admission.IsMatch(string.Join(" ", lines[lo..(hi + 1)])))
                {
                    decision = "Yellow";
                }

                if (NotAQuantity.IsMatch(line))
                    continue;

                rows.Add(new Row(rel, i + 1,
                    string.Join(", ", literals.Select(m => m.Value)),
                    line.Trim(), nearbyRef, decision));
            }
        }

        var sb = new StringBuilder();
        sb.Append(Csv("File", "Line", "Literals", "Text", "NearbyRef", "Decision"));
        foreach (var r in rows)
        {
            sb.Append(Csv(r.File, r.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.Literals, r.Text, r.NearbyRef, r.Decision));
        }
        return (sb.ToString(), rows);
    }

    /// <summary>
    /// One CSV record, in the shape <c>Export-Csv</c> wrote: every field quoted, embedded
    /// quotes doubled, LF endings.
    /// </summary>
    /// <remarks>
    /// ⚠️ QUOTING EVERY FIELD is not tidiness, it is compatibility: PowerShell 7's
    /// <c>Export-Csv</c> quotes unconditionally, and the committed file is its output, so a
    /// minimal-quoting writer would rewrite all 918 rows on the first run and bury whatever
    /// really changed. LF for the same reason as <see cref="ApproximationInventoryTests"/> —
    /// the blob is LF, and a wholesale ending flip would drown the diff.
    /// </remarks>
    private static string Csv(params string[] fields)
        => string.Concat(string.Join(",",
            fields.Select(f => "\"" + f.Replace("\"", "\"\"") + "\"")), "\n");

    /// <summary>The committed census is the one today's sources produce.</summary>
    [Fact]
    public void TheCensusIsNotStale()
    {
        var root = CollectResumeTests.FindRepoRoot();
        var (text, rows) = Build(root);
        var path = Path.Combine(root, Relative.Replace('/', Path.DirectorySeparatorChar));

        foreach (var g in rows.GroupBy(r => r.Decision).OrderBy(g => g.Key, StringComparer.Ordinal))
            _output.WriteLine($"{g.Key,-8} {g.Count()}");
        _output.WriteLine($"{"TOTAL",-8} {rows.Count}");

        if (UpdateDocs)
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(path), $"{Relative} is missing");
        var committed = File.ReadAllText(path).Replace("\r\n", "\n");
        if (committed == text)
            return;

        Assert.Fail(FirstDifference(committed, text)
            + $"\n\n{Relative} is not what the tree says today. It is generated, so the repair "
            + "is to regenerate it, never to edit it: set LILYSHARP_UPDATE_DOCS=1 and re-run, "
            + "then READ THE DIFF — a row that moved is a constant somebody added, moved or "
            + "gave a LILYPOND-REF, and which of those it was is the part worth knowing.");
    }

    /// <summary>The first line that differs, with its neighbours, and the two lengths.</summary>
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
