using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

public class TempProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Probe()
    {
        var src = System.IO.File.ReadAllText(@"C:/MyProj/LilySharp/samples/showcase/grammar-tour.lys");
        var tree = SyntaxTree.Parse(src);
        var spec = RenderSpecParser.FindFirst(tree);
        var multi = new MeasureCollector().CollectMultiStaff(tree, spec!);

        int mi = 12; // bar 13
        var allTimings = MultiStaffLayouter.CollectAllTimingsForMeasure(multi, mi);
        var allMeasures = MultiStaffLayouter.CollectAllMeasuresAtIndex(multi, mi);
        output.WriteLine("timings: " + string.Join(" ", allTimings));
        foreach (var t in allTimings)
        {
            var sp = SpacingRules.ComputeShortestPlayingAt(t, allMeasures);
            output.WriteLine($"shortestPlaying@{t} = {sp}");
        }
        var primary = multi.StaffGroups[0].PrimaryStaff.PrimaryVoice.Measures[mi];
        var bsd = SpacingRules.CalculateCommonShortestDuration(multi);
        output.WriteLine($"bsd = {bsd}");
        var springs = new MeasureLayouter().CreateTimingSprings(primary, allTimings, bsd, allMeasures);
        foreach (var s in springs)
            output.WriteLine($"spring ideal={s.IdealDistance:F2} min={s.MinDistance:F2} invStretch={s.InverseStretchStrength:F2}");
    }
}
