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

using System.Collections.Immutable;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class GrobOverrideTests
{
    private static MultiStaffScore CollectMulti(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!);
    }

    [Fact]
    public void MultiStaff_InMusicOverride_IsSurfaced()
    {
        // Regression: BuildMultiStaffScore dropped grob overrides, so a two-staff
        // score never coloured/hid anything even though the collector captured them.
        var score = CollectMulti(
            "part rh { clef treble }\npart lh { clef bass }\n" +
            "section A { rh { override NoteHead.color = red c4 d e f | } lh { c2 g | } }\n" +
            "form main { A }\nscore main { staff rh  staff lh }");
        Assert.Single(score.GrobOverrides);
        Assert.Equal("NoteHead", score.GrobOverrides[0].GrobType);
    }

    [Fact]
    public void MultiStaff_TopLevelOverride_IsSurfaced()
    {
        // A document-wide override on a multi-staff score, seeded at (0,0).
        var score = CollectMulti(
            "override NoteHead.color = red\npart rh { clef treble }\npart lh { clef bass }\n" +
            "section A { rh { c4 d e f | } lh { c2 g | } }\n" +
            "form main { A }\nscore main { staff rh  staff lh }");
        Assert.Single(score.GrobOverrides);
        Assert.Equal(0, score.GrobOverrides[0].MeasureIndex);
    }

    [Fact]
    public void InMusicOverride_IsTaggedWithItsStaff_GlobalIsNot()
    {
        // An override written in rh's music is scoped to rh's staff (index 0); a global
        // one carries no staff scope (applies to all).
        var scoped = CollectMulti(
            "part rh { clef treble }\npart lh { clef bass }\n" +
            "section A { rh { override NoteHead.color = red c4 d e f | } lh { c2 g | } }\n" +
            "form main { A }\nscore main { staff rh  staff lh }");
        Assert.Equal(0, Assert.Single(scoped.GrobOverrides).StaffIndex);

        var global = CollectMulti(
            "override NoteHead.color = red\npart rh { clef treble }\npart lh { clef bass }\n" +
            "section A { rh { c4 d e f | } lh { c2 g | } }\n" +
            "form main { A }\nscore main { staff rh  staff lh }");
        Assert.Null(Assert.Single(global.GrobOverrides).StaffIndex);
    }

    [Fact]
    public void PartBodyOverride_IsAStaffScopedDefault()
    {
        // `part melody { override … }` parses (no longer an error) and is collected as a
        // staff-scoped default at (0,0), so it colours the whole part across its sections.
        var score = CollectMulti(
            "part melody { clef bass  override NoteHead.color = red\n" +
            "  section A { clef treble c4 d e f | } section B { g4 a b c' | } }\n" +
            "form main { A B }\nscore main { staff melody }");
        var ov = Assert.Single(score.GrobOverrides);
        Assert.Equal(new LysValue.Symbol("red"), ov.Value);
        Assert.Equal(0, ov.StaffIndex);    // melody's staff
        Assert.Equal(0, ov.MeasureIndex);  // part default, from the start
        Assert.Equal(0, ov.ItemIndex);
    }

    [Fact]
    public void SectionMajorOverride_ScopesToEveryStaffForThatSectionOnly()
    {
        // `section A { override … melody {…} bass {…} }` colours A on BOTH staves and, via
        // the boundary reset, not B. Collected once per staff at A's start (measure 0).
        var score = CollectMulti(
            "part melody { clef treble }\npart bass { clef bass }\n" +
            "section A { override NoteHead.color = red  melody { c4 d e f | } bass { c2 g | } }\n" +
            "section B { melody { g4 a b c' | } bass { e2 c | } }\n" +
            "form main { A B }\nscore main { staff melody  staff bass }");
        var reds = score.GrobOverrides.Where(o => o.Value.AsText == "red").ToList();
        Assert.Equal(2, reds.Count);
        Assert.Contains(reds, o => o.StaffIndex == 0);   // melody
        Assert.Contains(reds, o => o.StaffIndex == 1);   // bass
        Assert.All(reds, o => Assert.Equal(0, o.MeasureIndex)); // section A start
    }

    [Fact]
    public void VoiceBlockOverride_ScopesToItsVoice()
    {
        // Two voices on one staff: each `voice {}` override scopes to that voice (render
        // voice number 1 and 2), so they don't bleed into each other.
        var score = CollectMulti(
            "part m { clef treble }\n" +
            "section A { m { voice { override NoteHead.color = red c4 d e f | } { override NoteHead.color = blue c2 g | } } }\n" +
            "form main { A }\nscore main { staff m }");
        Assert.Equal(1, Assert.Single(score.GrobOverrides.Where(o => o.Value.AsText == "red")).VoiceIndex);
        Assert.Equal(2, Assert.Single(score.GrobOverrides.Where(o => o.Value.AsText == "blue")).VoiceIndex);
    }

    [Fact]
    public void SectionMajorRevert_IsAnError()
    {
        Assert.True(HasRevertContextError(
            "part melody { clef treble }\nsection A { revert NoteHead.color  melody { c4 d e f | } }\n" +
            "form main { A }\nscore main { staff melody }"));
    }

    private static bool HasRevertContextError(string src)
        => LilySharp.Core.Semantics.SemanticValidation.Run(SyntaxTree.Parse(src))
            .Any(d => d.Code == DiagnosticCodes.RevertOutsideMusic);

    [Fact]
    public void RevertOrOnce_OutsideMusic_IsAnError()
    {
        // A part header holds no note stream, so revert/once there is meaningless.
        Assert.True(HasRevertContextError(
            "part m { override NoteHead.color = red  revert NoteHead.color  section A { c4 d e f | } }\n" +
            "form main { A }\nscore main { staff m }"));
        // The top level of a STRUCTURED file (has a part/section/form) is structural too.
        Assert.True(HasRevertContextError(
            "override NoteHead.color = red\nrevert NoteHead.color\npart m { section A { c4 d e f | } }\n" +
            "form main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void RevertOrOnce_InMusic_IsAllowed()
    {
        // A part-body override (a valid default) is not flagged.
        Assert.False(HasRevertContextError(
            "part m { override NoteHead.color = red  section A { c4 d e f | } }\n" +
            "form main { A }\nscore main { staff m }"));
        // Bare music: the top level IS a note stream, so a revert there is fine.
        Assert.False(HasRevertContextError("override NoteHead.color = red c4 d revert NoteHead.color e f"));
        // A revert inside a section's music is fine.
        Assert.False(HasRevertContextError(
            "part m { section A { override NoteHead.color = red c4 d revert NoteHead.color e f | } }\n" +
            "form main { A }\nscore main { staff m }"));
    }

    [Fact]
    public void SectionInternalOverride_ResetsAtTheBoundary()
    {
        // A's in-music override does not leak into B: the boundary reverts to the part
        // default (none here, so NoteHead.color is unset in B).
        var score = CollectMulti(
            "part m { section A { override NoteHead.color = red c4 d e f | } section B { g4 a b c' | } }\n" +
            "form main { A B }\nscore main { staff m }");
        var r = GrobPropertyResolver.ForStaffVoice(score.GrobOverrides, score.GrobReverts, 0, 1);
        r.AdvanceTo(0, 0);                                       // section A (measure 0)
        Assert.Equal("red", r.GetString("NoteHead", "color"));
        r.AdvanceTo(1, 0);                                       // section B (measure 1)
        Assert.Null(r.GetString("NoteHead", "color"));          // reset at the boundary
    }

    [Fact]
    public void PartDefaultPersists_WhileSectionOverrideIsCarvedWithin()
    {
        // Part default red; A overrides blue; B resets to the part default (red), not to
        // nothing — the boundary reverts to the PART DEFAULT, not a bare revert.
        var score = CollectMulti(
            "part m { override NoteHead.color = red  section A { override NoteHead.color = blue c4 d e f | } section B { g4 a b c' | } }\n" +
            "form main { A B }\nscore main { staff m }");
        var r = GrobPropertyResolver.ForStaffVoice(score.GrobOverrides, score.GrobReverts, 0, 1);
        r.AdvanceTo(0, 0);
        Assert.Equal("blue", r.GetString("NoteHead", "color")); // A: section-internal
        r.AdvanceTo(1, 0);
        Assert.Equal("red", r.GetString("NoteHead", "color"));  // B: back to the part default
    }

    [Fact]
    public void ForStaffVoice_SeesOnlyInScopeOverrides()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("red"), 0, 0, false, StaffIndex: 0),   // staff 0 only
            new GrobOverride("Stem", "color", new LysValue.Symbol("blue"), 0, 0, false, StaffIndex: null));  // all staves
        var reverts = ImmutableArray<GrobRevert>.Empty;

        var staff0 = GrobPropertyResolver.ForStaffVoice(overrides, reverts, staffIndex: 0, voiceIndex: 1);
        staff0.AdvanceTo(0, 0);
        Assert.Equal("red", staff0.GetString("NoteHead", "color"));
        Assert.Equal("blue", staff0.GetString("Stem", "color"));

        var staff1 = GrobPropertyResolver.ForStaffVoice(overrides, reverts, staffIndex: 1, voiceIndex: 1);
        staff1.AdvanceTo(0, 0);
        Assert.Null(staff1.GetString("NoteHead", "color"));   // staff-0 override is out of scope
        Assert.Equal("blue", staff1.GetString("Stem", "color"));
    }

    // --- Parser Tests ---
    // The note stream is wrapped in the minimal document (MusicSource) — music at the top
    // level of a file is LYS0020 — so these pin the IN-MUSIC, positional spelling of
    // override/revert/once, which is where they now have to be written.

    [Fact]
    public void Parse_Override_Basic()
    {
        var tree = MusicSource.Parse("override Stem.length = 10 c4 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("Stem", overrideNode.GrobName.Text);
        Assert.Equal("length", overrideNode.PropertyName.Text);
        Assert.Equal("10", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_Revert_Basic()
    {
        var tree = MusicSource.Parse("revert Stem.length c4 d e f");
        Assert.False(tree.HasErrors);
        var revertNode = tree.GetRoot().DescendantNodes()
            .OfType<RevertDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(revertNode);
        Assert.Equal("Stem", revertNode.GrobName.Text);
        Assert.Equal("length", revertNode.PropertyName.Text);
    }

    [Fact]
    public void Parse_OnceOverride()
    {
        var tree = MusicSource.Parse("once override Stem.length = 10 c4 d e f");
        Assert.False(tree.HasErrors);
        var onceNode = tree.GetRoot().DescendantNodes()
            .OfType<OnceModifierSyntax>()
            .FirstOrDefault();
        Assert.NotNull(onceNode);
        Assert.IsType<OverrideDeclarationSyntax>(onceNode.Command);
        var innerOverride = (OverrideDeclarationSyntax)onceNode.Command;
        Assert.Equal("Stem", innerOverride.GrobName.Text);
    }

    [Fact]
    public void Parse_Override_IdentifierValue()
    {
        var tree = MusicSource.Parse("override Stem.direction = up c4 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("direction", overrideNode.PropertyName.Text);
        Assert.Equal("up", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_Override_NegativeValue()
    {
        var tree = MusicSource.Parse("override Beam.positions = -3 c8 d e f");
        Assert.False(tree.HasErrors);
        var overrideNode = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .FirstOrDefault();
        Assert.NotNull(overrideNode);
        Assert.Equal("-3", overrideNode.ValueToken.Text);
    }

    [Fact]
    public void Parse_MultipleOverrides()
    {
        var tree = MusicSource.Parse("override Stem.length = 10 override Beam.thickness = 2 c8 d e f");
        Assert.False(tree.HasErrors);
        var overrides = tree.GetRoot().DescendantNodes()
            .OfType<OverrideDeclarationSyntax>()
            .ToList();
        Assert.Equal(2, overrides.Count);
        Assert.Equal("Stem", overrides[0].GrobName.Text);
        Assert.Equal("Beam", overrides[1].GrobName.Text);
    }

    [Fact]
    public void Parse_OverrideThenRevert()
    {
        var tree = MusicSource.Parse("override Stem.length = 10 c4 d revert Stem.length e f");
        Assert.False(tree.HasErrors);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<OverrideDeclarationSyntax>());
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RevertDeclarationSyntax>());
    }

    // --- Collector Tests ---

    [Fact]
    public void Collector_Override_Basic()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Equal("Stem", score.GrobOverrides[0].GrobType);
        Assert.Equal("length", score.GrobOverrides[0].PropertyName);
        // The value is TYPED at collection — an integer, not the text "10".
        Assert.Equal(new LysValue.Int(10), score.GrobOverrides[0].Value);
        Assert.Equal(0, score.GrobOverrides[0].MeasureIndex);
        Assert.False(score.GrobOverrides[0].IsOnce);
    }

    [Fact]
    public void Collector_Override_NegativeWithSpace_RoundTrips_AndValueStripped()
    {
        // "- 5" (a space between the sign and the digits) must (a) round-trip verbatim —
        // the combined negative-number token keeps the interior whitespace in its text so
        // root.FullWidth == text.Length and positions don't drift — and (b) still resolve
        // to the number -5 (LysValue.FromToken strips that interior whitespace).
        var source = "override Stem.length = - 5 c4 d e f";
        var tree = SyntaxTree.Parse(source);

        Assert.Equal(source, tree.ToFullString());

        var score = new MeasureCollector().Collect(tree);
        Assert.Single(score.GrobOverrides);
        Assert.Equal(new LysValue.Int(-5), score.GrobOverrides[0].Value);
    }

    [Fact]
    public void Collector_Revert_Basic()
    {
        var tree = SyntaxTree.Parse("revert Stem.length c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobReverts);
        Assert.Equal("Stem", score.GrobReverts[0].GrobType);
        Assert.Equal("length", score.GrobReverts[0].PropertyName);
        Assert.Equal(0, score.GrobReverts[0].MeasureIndex);
    }

    [Fact]
    public void Collector_OnceOverride()
    {
        var tree = SyntaxTree.Parse("once override Stem.length = 10 c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.True(score.GrobOverrides[0].IsOnce);
    }

    [Fact]
    public void Collector_NoOverrides()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.True(score.GrobOverrides.IsEmpty);
        Assert.True(score.GrobReverts.IsEmpty);
    }

    [Fact]
    public void Collector_Override_AcrossMeasures()
    {
        var tree = SyntaxTree.Parse("c4 d e f | override Stem.length = 7 g a b c'");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Equal(1, score.GrobOverrides[0].MeasureIndex);
    }

    [Fact]
    public void Collector_OverrideThenRevert()
    {
        var tree = SyntaxTree.Parse("override Stem.length = 10 c4 d revert Stem.length e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);

        Assert.Single(score.GrobOverrides);
        Assert.Single(score.GrobReverts);
    }

    // --- GrobPropertyResolver Tests ---

    [Fact]
    public void Resolver_Empty_ReturnsNull()
    {
        var resolver = GrobPropertyResolver.Empty;
        Assert.Null(resolver.GetDouble("Stem", "length"));
        Assert.False(resolver.HasOverrides);
    }

    [Fact]
    public void Resolver_Override_GetDouble()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_Override_GetDouble_NotYetActive()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 2));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Null(resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_Revert_RemovesOverride()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0));
        var reverts = ImmutableArray.Create(
            new GrobRevert("Stem", "length", 0, 2));
        var resolver = new GrobPropertyResolver(overrides, reverts);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Null(resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_OnceOverride_ClearsAfterOneAdvance()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0, IsOnce: true));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        // Advance to next item — once override should be cleared
        resolver.AdvanceTo(0, 1);
        Assert.Null(resolver.GetDouble("Stem", "length"));
    }

    [Fact]
    public void Resolver_IsOverridden()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.True(resolver.IsOverridden("Stem", "length"));
        Assert.False(resolver.IsOverridden("Stem", "thickness"));
        Assert.False(resolver.IsOverridden("Beam", "length"));
    }

    [Fact]
    public void Resolver_GetString()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "direction", new LysValue.Symbol("up"), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal("up", resolver.GetString("Stem", "direction"));
    }

    [Fact]
    public void Resolver_GetBool()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("NoteHead", "transparent", new LysValue.Symbol("true"), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.True(resolver.GetBool("NoteHead", "transparent"));
    }

    [Fact]
    public void Resolver_GetInt()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("StaffSymbol", "linecount", new LysValue.Int(1), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(1, resolver.GetInt("StaffSymbol", "linecount"));
    }

    [Fact]
    public void Resolver_MultipleOverrides_SameGrob()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0),
            new GrobOverride("Stem", "thickness", new LysValue.Int(3), 0, 0));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));
        Assert.Equal(3.0, resolver.GetDouble("Stem", "thickness"));
    }

    [Fact]
    public void Resolver_Override_ReplacesPrevious()
    {
        var overrides = ImmutableArray.Create(
            new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0),
            new GrobOverride("Stem", "length", new LysValue.Int(7), 0, 2));
        var resolver = new GrobPropertyResolver(overrides, ImmutableArray<GrobRevert>.Empty);

        resolver.AdvanceTo(0, 0);
        Assert.Equal(10.0, resolver.GetDouble("Stem", "length"));

        resolver.AdvanceTo(0, 2);
        Assert.Equal(7.0, resolver.GetDouble("Stem", "length"));
    }

    // --- Pipeline Integration Tests ---
    // LILYPOND-REF: lily/grob-property.cc — end-to-end override/revert pipeline

    /// <summary>Renders a music fragment in the minimal document (music at the top level is
    /// LYS0020). The wrapping is geometry-neutral, so the glyph counts and notehead Xs these
    /// tests compare are the same numbers as when the fragment was a file of its own.</summary>
    private static string RenderToSvg(string music)
    {
        var source = MusicSource.Wrap(music);
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, $"Parse errors: {string.Join(", ", tree.Diagnostics)}");
        return LiveRender.Svg(source);
    }

    [Fact]
    public void Pipeline_Override_NoteHeadColor_AppearsInSvg()
    {
        var svg = RenderToSvg("override NoteHead.color = red c4 d e f");
        // All noteheads should have fill="#FF0000" (live path emits hex)
        Assert.Contains("fill=\"#FF0000\"", svg);
    }

    [Fact]
    public void Pipeline_Override_StemColor_AppearsInSvg()
    {
        var svg = RenderToSvg("override Stem.color = blue c4 d e f");
        // Stems should have stroke="#0000FF" (live path emits hex)
        Assert.Contains("stroke=\"#0000FF\"", svg);
    }

    [Fact]
    public void Pipeline_OnceOverride_NoteHeadColor_OnlyFirstNote()
    {
        var svg = RenderToSvg("once override NoteHead.color = red c4 d e f");
        // Only one note should have fill="#FF0000" (the first one)
        int count = CountOccurrences(svg, "fill=\"#FF0000\"");
        Assert.Equal(1, count);
    }

    [Fact]
    public void Pipeline_Override_ThenRevert_NoteHeadColor()
    {
        var svg = RenderToSvg("override NoteHead.color = red c4 d revert NoteHead.color e f");
        // First two notes have red, last two don't
        int count = CountOccurrences(svg, "fill=\"#FF0000\"");
        Assert.Equal(2, count);
    }

    [Fact]
    public void Pipeline_NoOverrides_NoColorAttributes()
    {
        var svg = RenderToSvg("c4 d e f");
        // No override colors on noteheads (default rendering)
        Assert.DoesNotContain("fill=\"#FF0000\"", svg);
        Assert.DoesNotContain("fill=\"#0000FF\"", svg);
    }

    [Fact]
    public void Pipeline_Override_NoteHeadTransparent_HidesNoteheads()
    {
        var svgWithTransparent = RenderToSvg("override NoteHead.transparent = true c4 d e f");
        var svgNormal = RenderToSvg("c4 d e f");
        // Transparent score should have fewer music text elements (noteheads hidden)
        int transparentGlyphs = CountOccurrences(svgWithTransparent, "class=\"music\"");
        int normalGlyphs = CountOccurrences(svgNormal, "class=\"music\"");
        Assert.True(transparentGlyphs < normalGlyphs,
            $"Transparent ({transparentGlyphs}) should have fewer glyphs than normal ({normalGlyphs})");
    }

    [Fact]
    public void Pipeline_Override_StemTransparent_HidesStemAndFlag_KeepsSpacing()
    {
        // \hideNotes is Stem.transparent (ly/property-init.ly): the stem prints no
        // ink but keeps its extent, so nothing else moves.
        // LILYPOND-REF: lily/grob.cc:164-176 get_print_stencil — transparent
        //   replaces the stencil with an empty box of the same extent.
        var hidden = RenderToSvg("override Stem.transparent = true c4 d e f");
        var normal = RenderToSvg("c4 d e f");
        Assert.Equal(0, CountOccurrences(hidden, "stroke-width=\"0.130\""));   // no stem lines
        Assert.Equal(4, CountOccurrences(normal, "stroke-width=\"0.130\""));
        // Ink only: every notehead X survives unchanged (transparent keeps extents).
        Assert.Equal(NoteheadXs(normal), NoteheadXs(hidden));

        // The flag inherits the stem's transparency (an unbeamed lone eighth — a
        // beamed run would take the DrawBeams path, which this port does not cover).
        // LILYPOND-REF: scm/define-grobs.scm:1631-1632 Flag transparent = grob::inherit-parent-property
        var flagHidden = RenderToSvg("override Stem.transparent = true c8 r8 r4 r2");
        var flagNormal = RenderToSvg("c8 r8 r4 r2");
        Assert.Equal(CountOccurrences(flagNormal, "class=\"music\"") - 1,
            CountOccurrences(flagHidden, "class=\"music\""));
    }

    [Fact]
    public void Pipeline_OnceOverrides_StackOnTheSameNote()
    {
        // complex-once.ly's shape: TWO once overrides written back to back both land on
        // the SAME next note (LP applies \once \hideNotes to every property operation
        // in the bundle at one timestep) — b loses head AND stem, c gets both back.
        // Twin: audit/lp-regression/lys/complex-once.lys (X match vs LP to 2 dp).
        var svg = RenderToSvg(
            "c4 d\n" +
            "override NoteHead.transparent = true\noverride Stem.transparent = true\n" +
            "e4 f |\n" +
            "revert NoteHead.transparent\nrevert Stem.transparent\n" +
            "g a\n" +
            "once override NoteHead.transparent = true\nonce override Stem.transparent = true\n" +
            "b c |");
        // 8 notes: e f (windowed) and b (once) are hidden → 5 heads, 5 stems.
        Assert.Equal(5, CountOccurrences(svg, "stroke-width=\"0.130\""));
        var heads = NoteheadXs(svg);
        Assert.Equal(5, heads.Count);
    }

    /// <summary>Xs of the FILLED notehead glyphs (the code point quarters and
    /// eighths draw — EmmentalerGlyphs.GetNotehead(4)); flags/clefs don't match.</summary>
    private static List<string> NoteheadXs(string svg) =>
        System.Text.RegularExpressions.Regex.Matches(svg,
                "<text class=\"music\"[^>]* x=\"([0-9.\\-]+)\"[^>]*>("
                + LilySharp.Core.Svg.EmmentalerGlyphs.GetNotehead(noteValue: 4) + ")</text>")
            .Select(m => m.Groups[1].Value)
            .ToList();

    [Fact]
    public void Pipeline_GrobPropertyResolver_InScoreLayout()
    {
        var tree = SyntaxTree.Parse("override Stem.color = red c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine();
        var layout = engine.Layout(score);

        // Verify resolver is attached to layout
        Assert.True(layout.GrobPropertyResolver.HasOverrides);
    }

    [Fact]
    public void Pipeline_NoOverrides_EmptyResolver()
    {
        var tree = SyntaxTree.Parse("c4 d e f");
        var collector = new MeasureCollector();
        var score = collector.Collect(tree);
        var engine = new LayoutEngine();
        var layout = engine.Layout(score);

        // No overrides → empty resolver
        Assert.False(layout.GrobPropertyResolver.HasOverrides);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
