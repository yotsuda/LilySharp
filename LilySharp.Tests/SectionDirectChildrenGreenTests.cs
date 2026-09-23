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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net for the collector's reads of a section's DIRECT children off the green
/// (session 521): <see cref="MeasureCollector.SectionHasInlineMusic"/> decides on slot kinds,
/// and the inline arm of the section walk gathers its music as lazy sites
/// (<see cref="MeasureCollector.InlineSectionSites"/>) instead of wrapping children it had
/// already made red. On every section of every net book, and on a book that spells every
/// direct-child shape, each must answer exactly what the red spelling it replaced answered.
/// </summary>
/// <remarks>
/// WHY: a single-part book writes its music straight into its sections, so the section's
/// direct children ARE the notes — and ProcessSectionBody made every one of them red on
/// every keystroke, three times over (the part-block scan, the inline test, the preset
/// list), adopted prefix included: 512 reds a keystroke over the owner's corpus, 358 of
/// them notes. The kind is the green's; the red spelling stays here as the oracle.
/// </remarks>
public class SectionDirectChildrenGreenTests
{
    // Every direct-child shape a section can hold: header directives, an override, a part
    // block, a chord block, a lyrics block, inline music of every collectable kind, and a
    // phrase reference — and a section whose only non-token children are directives.
    private const string EveryShape = """
        part bass { clef bass }
        phrase P { c4 d }
        section Head { key g major  time 3/4  tempo 4 = 100  partial 4  bass { c4 d e | } }
        section Inline { c4 d e f | r4 s8 R1*2 | <c e g>4 q | break c4 ~ c ( d ) | P | tuplet 3/2 { c8 d e } | repeat unfold 2 { c4 } | @mark("A") c4 | }
        section Mixed { override Stem.thickness = 2  c4 d e f | }
        section Blocks { bass { c4 d | } chords { c1 } lyrics { la la } }
        section Empty { key c major }
        score main { staff bass }
        """;

    [Fact]
    public void SectionHasInlineMusic_AnswersWhatTheRedSpellingDid()
    {
        var failures = new List<string>();
        int books = 0, sections = 0, inline = 0;
        foreach (var (label, root) in Roots())
        {
            books++;
            foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
            {
                sections++;
                bool red = RedSectionHasInlineMusic(section);
                if (red) inline++;
                if (MeasureCollector.SectionHasInlineMusic(section) != red)
                    failures.Add($"{label} section '{section.SectionName}'@{section.Position}: green {!red} != red {red}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && sections >= 100 && inline >= 5,
            $"the net did not bite: {books} books, {sections} sections, {inline} inline");
    }

    [Fact]
    public void InlineSectionSites_AreTheRedChildrenThePresetListHeld()
    {
        var failures = new List<string>();
        int books = 0, sectionsCompared = 0, sitesCompared = 0;
        foreach (var (label, root) in Roots())
        {
            books++;
            foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>())
            {
                if (section.Parent is not CompilationUnitSyntax || !RedSectionHasInlineMusic(section))
                    continue;
                sectionsCompared++;
                var expected = RedInlineChildren(section).ToList();
                var sites = MeasureCollector.InlineSectionSites(section).ToList();
                if (expected.Count != sites.Count)
                {
                    failures.Add($"{label} section '{section.SectionName}'@{section.Position}: {sites.Count} sites, red preset held {expected.Count}");
                    continue;
                }
                for (int i = 0; i < expected.Count; i++)
                {
                    sitesCompared++;
                    var site = sites[i];
                    var red = expected[i];
                    if (site.Kind != red.Kind || site.Position != red.Position || !ReferenceEquals(site.Node, red))
                    {
                        failures.Add($"{label} section '{section.SectionName}'@{section.Position} site {i}: "
                            + $"{site.Kind}@{site.Position} (node {site.Node.Kind}@{site.Node.Position}) vs red {red.Kind}@{red.Position}");
                        break;
                    }
                }
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(sectionsCompared >= 5 && sitesCompared >= 40,
            $"the net did not bite: {books} books, {sectionsCompared} inline sections, {sitesCompared} sites");
    }

    /// <summary>
    /// The liveness half (RULES §5.4: an optimisation that never shows in the output needs
    /// one): a resume in a single-part book — every note a DIRECT child of its section —
    /// enters the inline gather at the checkpoint by its slot path and gathers only what
    /// lies after it. Until session 521 the inline list was preset (no gather root, no
    /// path), so the resume could not seek and every site of the section was red.
    /// </summary>
    [Fact]
    public void AResumeInASinglePartBook_SeeksIntoTheInlineSection_AndGathersAlmostNothing()
    {
        var sb = new System.Text.StringBuilder("part bass { clef bass }\nsection A {\n");
        for (int bar = 0; bar < 100; bar++)
            sb.Append("  c4 d e f |\n");
        sb.Append("}\nscore main { staff bass }\n");
        var tree = SyntaxTree.Parse(sb.ToString());
        var section = tree.GetRoot().DescendantNodes<SectionDeclarationSyntax>().Single();
        Assert.True(MeasureCollector.SectionHasInlineMusic(section));
        int sitesInTheBook = MeasureCollector.InlineSectionSites(section).Count();
        Assert.Equal(500, sitesInTheBook); // 100 × (c d e f |)

        var spec = LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree);
        var recorder = CollectWalkProbe.Recorder();
        var source = new MeasureCollector { WalkProbe = recorder, ScoreTranspose = spec?.ScoreTranspose };
        var full = LilySharp.Core.Svg.SvgGenerator.CollectScore(source, tree, spec);
        var (ordinal, recording) = recorder.Recordings.Single(r => r.Value.Checkpoints.Count > 0);
        var last = recording.Checkpoints[^1];
        Assert.NotNull(last.GatherPath);
        Assert.Single(last.GatherPath!); // a direct child: one slot deep

        var resumer = CollectWalkProbe.Resumer();
        resumer.ResumePlans[ordinal] = new VoiceResumePlan { Checkpoint = last, Recording = recording, Source = source };
        var resumed = LilySharp.Core.Svg.SvgGenerator.CollectScore(
            new MeasureCollector { WalkProbe = resumer, ScoreTranspose = spec?.ScoreTranspose }, tree, spec);
        Assert.Null(ModelDeepDiff.FirstDifference(full, resumed, "score"));
        Assert.Equal(1, resumer.GatherSeeks);
        Assert.True(resumer.GatherSitesMaterialized < sitesInTheBook / 10,
            $"{resumer.GatherSitesMaterialized} sites gathered for a resume at the last of {sitesInTheBook}");
    }

    private static IEnumerable<(string Label, SyntaxNode Root)> Roots()
    {
        foreach (var path in CollectResumeTests.NetBooks())
        {
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(text); } catch { continue; }
            yield return (Path.GetFileName(path), tree.GetRoot());
        }
        yield return ("EveryShape", SyntaxTree.Parse(EveryShape).GetRoot());
    }

    // MeasureCollector.SectionHasInlineMusic as it was spelled on the red children until session 521.
    private static bool RedSectionHasInlineMusic(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is null or SyntaxTokenNode)
                continue;
            if (child is PartBlockSyntax or ChordPartBlockSyntax or LyricsBlockSyntax)
                continue;
            if (child is KeySignatureSyntax or TimeSignatureSyntax or TempoDeclarationSyntax
                or PartialDeclarationSyntax or ClefDeclarationSyntax or OctaveDirectiveSyntax
                or OverrideDeclarationSyntax or RevertDeclarationSyntax or OnceModifierSyntax)
                continue;
            return true;
        }
        return false;
    }

    // The inline arm's preset list as it was built until session 521: every direct child
    // the walk consumes (a collectable node, or a reference it expands), in slot order.
    private static IEnumerable<SyntaxNode> RedInlineChildren(SectionDeclarationSyntax section)
    {
        for (int i = 0; i < section.SlotCount; i++)
        {
            var child = section.GetChild(i);
            if (child is VariableReferenceSyntax || (child != null && MeasureCollector.IsCollectableMusicNode(child)))
                yield return child;
        }
    }
}
