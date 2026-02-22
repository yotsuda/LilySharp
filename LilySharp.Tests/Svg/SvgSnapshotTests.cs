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
public class SvgSnapshotTests
{
    private static readonly string SamplesDir = FindSamplesDir();
    private static readonly string SnapshotsDir = FindSnapshotsDir();
    private static readonly bool UpdateSnapshots =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_SNAPSHOTS") == "1";

    private static string FindSamplesDir()
    {
        // Walk up from test assembly output to repo root
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "samples");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find samples/ directory");
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
        yield return new object[] { "test/bass-clef" };
        yield return new object[] { "test/keysig-treble" };
        yield return new object[] { "test/keysig-bass" };
        yield return new object[] { "test/keysig-clefs" };
        yield return new object[] { "test/treble8" };
        yield return new object[] { "test/ledger-lines" };
        yield return new object[] { "test/barcheck" };
        yield return new object[] { "test/break" };
        yield return new object[] { "test/lyrics" };
        yield return new object[] { "test/multi-voice" };
        yield return new object[] { "test/collision" };
        yield return new object[] { "test/phrases" };
        yield return new object[] { "test/ornaments" };
        yield return new object[] { "test/repeat-volta" };
        yield return new object[] { "test/dollar-ref" };
        yield return new object[] { "test/grammar-test" };
        yield return new object[] { "test/instrument-defaults" };
        yield return new object[] { "test/section-octave-reset" };
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
        var svg = SvgGenerator.Generate(tree, options);

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

        // Compare against baseline
        var baseline = File.ReadAllText(snapshotPath);
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
