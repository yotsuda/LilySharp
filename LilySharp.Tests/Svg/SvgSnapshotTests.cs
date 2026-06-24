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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// Snapshot regression tests for SVG output.
/// Renders each sample .lys file and compares against a stored baseline SVG.
///
/// To update baselines after intentional changes:
///   set LILYSHARP_UPDATE_SNAPSHOTS=1
///   dotnet test --filter "FullyQualifiedName~SvgSnapshotTests"
/// </summary>
[Trait("Category", "Visual")]
public class SvgSnapshotTests
{
    private static readonly string SamplesDir = FindSamplesDir();
    private static readonly string SnapshotsDir = FindSnapshotsDir();
    private static readonly bool UpdateSnapshots =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_SNAPSHOTS") == "1";

    private static string FindSamplesDir()
    {
        // Snapshot fixtures live UNDER the test project (LilySharp.Tests/Fixtures),
        // deliberately separate from the user-editable samples/ playground so an
        // edit to a free sample never trips a snapshot. sampleName keeps its
        // "test/" and "showcase/" prefixes, which map to Fixtures/test, etc.
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }

    private static string FindSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Snapshots");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Snapshots/ directory");
    }

    /// <summary>
    /// Test sample files.
    /// </summary>
    public static IEnumerable<object[]> TestSamples()
    {
        yield return new object[] { "test/notes" };
        yield return new object[] { "test/chords" };
        yield return new object[] { "test/accidentals" };
        yield return new object[] { "test/articulations" };
        yield return new object[] { "test/dynamics" };
        yield return new object[] { "test/beaming" };
        yield return new object[] { "test/grace-notes" };
        yield return new object[] { "test/ties-slurs" };
        yield return new object[] { "test/tuplets" };
        yield return new object[] { "test/tuplets-beamed" };
        yield return new object[] { "test/grandstaff-repeat" };
        yield return new object[] { "test/bass-clef" };
        yield return new object[] { "test/keysig-treble" };
        yield return new object[] { "test/keysig-bass" };
        yield return new object[] { "test/keysig-clefs" };
        // Middle C in all four clefs — guards that alto and tenor C-clefs are
        // positioned distinctly (tenor a third higher than alto).
        yield return new object[] { "test/clef-positions" };
        yield return new object[] { "test/treble8" };
        yield return new object[] { "test/ledger-lines" };
        yield return new object[] { "test/barcheck" };
        yield return new object[] { "test/break" };
        yield return new object[] { "test/lyrics" };
        // Two verses: each verse line gets its stanza number ("1." / "2.") at the
        // left of its lyrics, not floating at the top of the page.
        yield return new object[] { "test/lyrics-verses" };
        yield return new object[] { "test/multi-voice" };
        // Sequential measures around a << \\ >> span — the single-voice measures
        // before and after the parallel block must not be dropped.
        yield return new object[] { "test/voice-mixed" };
        // Intra-staff polyphony on a grand staff: both voices of a << \\ >> in
        // the upper staff render, with the surrounding single-voice measures.
        yield return new object[] { "test/voice-grandstaff" };
        // Two voices each with a dynamic on the same note column — both render
        // (stacked) rather than one hiding the other.
        yield return new object[] { "test/voice-dynamics" };
        // A << \\ >> span mid-stream: its voices' dynamics land in the span's
        // measure, not back in measure 0.
        yield return new object[] { "test/voice-dynamics-mid" };
        // Multi-staff: a low lower-voice dynamic in the TOP staff's << \\ >> must
        // widen the inter-staff gap so it clears the staff below instead of
        // overlapping its lines (dynamics join the outside-staff skyline).
        yield return new object[] { "test/voice-dynamics-multistaff" };
        // Multi-staff: dynamics that belong to the SECOND (bass) staff render
        // under THAT staff, not collapsed under the first (DynamicItem.StaffIndex).
        yield return new object[] { "test/dynamics-lower-staff" };
        // Multi-staff: articulations + ornaments on the SECOND (bass) staff render
        // against that staff's notes, not the first (ArticulationItem.StaffIndex).
        yield return new object[] { "test/articulations-lower-staff" };
        // Multi-staff: a tuplet bracket on the SECOND (bass) staff sits over that
        // staff's notes, not the first (TupletBracketItem.StaffIndex).
        yield return new object[] { "test/tuplet-lower-staff" };
        // Multi-staff: an arpeggio on a SECOND (bass) staff chord draws to the
        // left of that chord, not the first staff (ArpeggioItem.StaffIndex).
        yield return new object[] { "test/arpeggio-lower-staff" };
        // Multi-staff: figured bass + chord names on the SECOND (bass) staff
        // render against that staff (FiguredBassItem/ChordNameItem.StaffIndex).
        yield return new object[] { "test/figbass-chordname-lower-staff" };
        // Multi-staff: grace notes on the SECOND (bass) staff render against that
        // staff (GraceNoteItem.StaffIndex / GraceNoteLayout.StaffYOffset).
        yield return new object[] { "test/grace-lower-staff" };
        // Multi-staff: a trill spanner on the SECOND (bass) staff runs above that
        // staff, not pulled to the top by the stacker (TrillSpannerItem.StaffIndex).
        yield return new object[] { "test/trillspan-lower-staff" };
        // Multi-staff: fingerings on the SECOND (bass) staff render at that staff
        // (per-staff FingeringEngraver pass), instead of vanishing.
        yield return new object[] { "test/fingering-lower-staff" };
        // Polyrhythm: an upper-voice triplet's "3" bracket sits above its notes
        // (the voice's stems are force-flipped), not below by the other voice.
        yield return new object[] { "test/voice-tuplet" };
        yield return new object[] { "test/collision" };
        yield return new object[] { "test/phrases" };
        yield return new object[] { "test/ornaments" };
        yield return new object[] { "test/repeat-volta" };
        yield return new object[] { "test/dollar-ref" };
        yield return new object[] { "test/grammar-test" };
        yield return new object[] { "test/instrument-defaults" };
        yield return new object[] { "test/section-octave-reset" };
        yield return new object[] { "test/instrument-names" };
        yield return new object[] { "test/clef-change" };
        yield return new object[] { "test/keysig-change" };
        yield return new object[] { "test/trill-spanner" };
        yield return new object[] { "test/courtesy-accidentals" };
        yield return new object[] { "test/editorial-accidental" };
        yield return new object[] { "test/multi-line-spanners" };
        yield return new object[] { "test/multi-measure-rest" };
        // Covers a stacked-digit time signature (3/8) AND a grand staff whose
        // secondary (bass) staff out-counts the primary in a measure — both
        // were uncovered and both had rendering bugs (time-sig digits too high;
        // surplus secondary noteheads dropped).
        yield return new object[] { "test/timesig-grandstaff" };
        // Mid-piece time signature changes (4/4 -> 3/4 -> 6/8 -> 2/4): digits
        // drawn at each change point, measure length re-armed, validator follows
        // the new meter. Verified against LilyPond \time at matching bars.
        yield return new object[] { "test/timesig-change" };
        // A meter change that lands at a system break: it must join the
        // system-start prefix (clef, key, time) instead of hanging far right of
        // the first note, and the key signature is reprinted at the system head.
        yield return new object[] { "test/timesig-change-linebreak" };
        // Part-option transpose: pitches re-spelled (c->d), key signature moved
        // (c major -> D major), in-key accidentals suppressed and an out-of-key
        // one (cis -> dis) kept. Verified against LilyPond \transpose c d.
        yield return new object[] { "test/transpose" };
        // Per-staff transpose in a multi-staff score: the transposed (top) staff
        // shows its own key (D major) while the concert (bottom) staff keeps C
        // major. Verified against LilyPond.
        yield return new object[] { "test/transpose-multistaff" };
        // Octave-marked transpose target (transpose: bes,) — a downward interval
        // spanning the octave; key moves G major -> F major. Verified vs LilyPond.
        yield return new object[] { "test/transpose-down" };
        // Top-level (score-wide) transpose default + bare headers: both parts
        // inherit transpose d (C major -> D major). Verified vs LilyPond.
        yield return new object[] { "test/transpose-score" };
        // Mid-piece tempo change: a new metronome mark (quarter = 160) prints at
        // the change point in the music stream. Verified vs LilyPond \tempo.
        yield return new object[] { "test/tempo-change" };
        // Mid-measure tempo on a grand staff with independent rhythms: the lower
        // staff's tempo must resolve via the shared timing columns and land on
        // its own note (beat 2), where the upper staff has no onset — not on the
        // upper voice's same-index note. Verified vs LilyPond \tempo.
        yield return new object[] { "test/tempo-grandstaff" };
    }

    /// <summary>
    /// Showcase sample files.
    /// </summary>
    public static IEnumerable<object[]> ShowcaseSamples()
    {
        yield return new object[] { "showcase/01-expressions" };
        yield return new object[] { "showcase/02-ornaments" };
        yield return new object[] { "showcase/03-piano" };
        yield return new object[] { "showcase/04-advanced" };
        yield return new object[] { "showcase/05-special-techniques" };
        yield return new object[] { "showcase/06-meter-changes" };
    }

    [Theory]
    [MemberData(nameof(TestSamples))]
    public void TestSample_MatchesSnapshot(string sampleName)
    {
        AssertSnapshotMatch(sampleName);
    }

    [Theory]
    [MemberData(nameof(ShowcaseSamples))]
    public void ShowcaseSample_MatchesSnapshot(string sampleName)
    {
        AssertSnapshotMatch(sampleName);
    }

    private void AssertSnapshotMatch(string sampleName)
    {
        var lysPath = Path.Combine(SamplesDir, sampleName + ".lys");
        Assert.True(File.Exists(lysPath), $"Source file not found: {lysPath}");

        // Render SVG (without font embedding for smaller/stable snapshots)
        var source = File.ReadAllText(lysPath);
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        // Snapshots are platform-neutral: LF on disk (.gitattributes pins
        // Snapshots/*.svg to eol=lf) and LF in comparison, regardless of the
        // generator's native newline.
        var svg = SvgGenerator.Generate(tree, options).Replace("\r\n", "\n");

        // Snapshot file path: "test/notes" → "test__notes.svg"
        var snapshotFileName = sampleName.Replace("/", "__").Replace("\\", "__") + ".svg";
        var snapshotPath = Path.Combine(SnapshotsDir, snapshotFileName);

        if (UpdateSnapshots || !File.Exists(snapshotPath))
        {
            // Create or update baseline
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, svg);

            if (!UpdateSnapshots)
            {
                // First run — baseline created, skip assertion
                Assert.Fail(
                    $"Snapshot baseline created: {snapshotFileName}. " +
                    "Re-run the test to verify against the new baseline.");
            }
            return;
        }

        // Compare against baseline (newline-normalized: working trees may
        // hold either ending depending on git config and platform)
        var baseline = File.ReadAllText(snapshotPath).Replace("\r\n", "\n");
        if (svg != baseline)
        {
            // Find first difference for a helpful error message
            var (line, col) = FindFirstDifference(baseline, svg);
            Assert.Fail(
                $"SVG snapshot mismatch for '{sampleName}' at line {line}, col {col}.\n" +
                $"To update: set LILYSHARP_UPDATE_SNAPSHOTS=1 and re-run.\n" +
                $"Baseline: {snapshotPath}");
        }
    }

    private static (int line, int col) FindFirstDifference(string expected, string actual)
    {
        int line = 1, col = 1;
        int len = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < len; i++)
        {
            if (expected[i] != actual[i])
                return (line, col);
            if (expected[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        // One is longer than the other
        return (line, col);
    }
}
