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
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Unit tests for <see cref="ScoreAssembler"/> — the type extracted from
/// MeasureCollector that turns a <see cref="ScoreContent"/> snapshot into a
/// <see cref="Score"/> / <see cref="MultiStaffScore"/>. These pin the contracts
/// that used to be implicit in three duplicated constructor call sites: metadata
/// flow-through, the init-only tempo properties, and — most importantly — which
/// annotation subsets each build path surfaces.
/// </summary>
[Trait("Category", "Unit")]
public class ScoreAssemblerTests
{
    private static Voice EmptyVoice(string name = "v") =>
        new Voice(name, ImmutableArray<Measure>.Empty);

    /// <summary>Builds a ScoreContent with distinctive metadata and the given
    /// annotation lists (everything else empty).</summary>
    private static ScoreContent MakeContent(
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffItem>? crossStaff = null,
        ImmutableArray<GrobOverride>? grobOverrides = null,
        ImmutableArray<GrobRevert>? grobReverts = null) =>
        new ScoreContent(
            new TimeSignature(3, 4, "3/4", false),
            new KeySignature(2, null),
            "treble",
            Tempo: 120,
            Title: "Title",
            Composer: "Composer",
            SwingSubdivision: 3,
            Dynamics: ImmutableArray<DynamicItem>.Empty,
            Articulations: ImmutableArray<ArticulationItem>.Empty,
            GraceNotes: ImmutableArray<GraceNoteItem>.Empty,
            Lyrics: ImmutableArray<LyricItem>.Empty,
            MusicMarks: ImmutableArray<MusicMarkItem>.Empty,
            CustomTexts: ImmutableArray<CustomTextItem>.Empty,
            VoltaBrackets: ImmutableArray<VoltaBracketItem>.Empty,
            TupletBrackets: ImmutableArray<TupletBracketItem>.Empty,
            Arpeggios: ImmutableArray<ArpeggioItem>.Empty,
            FiguredBasses: ImmutableArray<FiguredBassItem>.Empty,
            ChordNames: chordNames ?? ImmutableArray<ChordNameItem>.Empty,
            PercentRepeats: percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty,
            CrossStaffItems: crossStaff ?? ImmutableArray<CrossStaffItem>.Empty,
            GrobOverrides: grobOverrides ?? ImmutableArray<GrobOverride>.Empty,
            GrobReverts: grobReverts ?? ImmutableArray<GrobRevert>.Empty,
            TrillSpanners: ImmutableArray<TrillSpannerItem>.Empty,
            Header: new HeaderPositions(1, 2, 3, 4, 5),
            TempoText: "Allegro",
            TempoBeatUnit: 2,
            TempoDots: 1);

    private static ImmutableArray<ChordNameItem> OneChord() =>
        ImmutableArray.Create(new ChordNameItem("C", 0, 0, 0));
    private static ImmutableArray<PercentRepeatItem> OnePercent() =>
        ImmutableArray.Create(new PercentRepeatItem(0, 0));
    private static ImmutableArray<CrossStaffItem> OneCrossStaff() =>
        ImmutableArray.Create(new CrossStaffItem(0, 0, 1, 0));
    private static ImmutableArray<GrobOverride> OneOverride() =>
        ImmutableArray.Create(new GrobOverride("NoteHead", "color", "red", 0, 0));
    private static ImmutableArray<GrobRevert> OneRevert() =>
        ImmutableArray.Create(new GrobRevert("NoteHead", "color", 0, 0));

    [Fact]
    public void BuildScore_SingleVoice_WrapsVoiceAndFlowsMetadata()
    {
        var c = MakeContent();
        var s = ScoreAssembler.BuildScore(EmptyVoice("melody"), c, includeChordExtras: true);

        Assert.Single(s.Voices);
        Assert.Equal("melody", s.Voices[0].Name);
        Assert.Equal("treble", s.Clef);
        Assert.Equal(120, s.Tempo);
        Assert.Equal("Title", s.Title);
        Assert.Equal("Composer", s.Composer);
        Assert.Equal(3, s.SwingSubdivision);
        Assert.Equal(c.TimeSignature, s.TimeSignature);
        Assert.Equal(c.KeySignature, s.KeySignature);
        Assert.Equal(c.Header, s.Header);
    }

    [Fact]
    public void BuildScore_SetsInitOnlyTempoProperties()
    {
        var s = ScoreAssembler.BuildScore(EmptyVoice(), MakeContent(), includeChordExtras: true);
        Assert.Equal("Allegro", s.TempoText);
        Assert.Equal(2, s.TempoBeatUnit);
        Assert.Equal(1, s.TempoDots);
    }

    [Fact]
    public void BuildScore_MultiVoice_KeepsAllVoices()
    {
        var voices = ImmutableArray.Create(EmptyVoice("v1"), EmptyVoice("v2"), EmptyVoice("v3"));
        var s = ScoreAssembler.BuildScore(voices, MakeContent(), includeChordExtras: false);
        Assert.Equal(3, s.Voices.Length);
    }

    [Fact]
    public void BuildScore_IncludeChordExtrasTrue_PreservesChordExtras()
    {
        var c = MakeContent(chordNames: OneChord(), percentRepeats: OnePercent(), crossStaff: OneCrossStaff());
        var s = ScoreAssembler.BuildScore(EmptyVoice(), c, includeChordExtras: true);

        Assert.Single(s.ChordNames);
        Assert.Single(s.PercentRepeats);
        Assert.Single(s.CrossStaffItems);
    }

    [Fact]
    public void BuildScore_IncludeChordExtrasFalse_OmitsChordExtras()
    {
        // Even though the snapshot carries them, the multi-voice path omits these
        // three — the historical behavior this flag makes explicit.
        var c = MakeContent(chordNames: OneChord(), percentRepeats: OnePercent(), crossStaff: OneCrossStaff());
        var s = ScoreAssembler.BuildScore(EmptyVoice(), c, includeChordExtras: false);

        Assert.Empty(s.ChordNames);
        Assert.Empty(s.PercentRepeats);
        Assert.Empty(s.CrossStaffItems);
    }

    [Fact]
    public void BuildScore_AlwaysIncludesGrobOverridesAndReverts()
    {
        // Grob overrides/reverts flow through regardless of includeChordExtras.
        var c = MakeContent(grobOverrides: OneOverride(), grobReverts: OneRevert());
        var s = ScoreAssembler.BuildScore(EmptyVoice(), c, includeChordExtras: false);

        Assert.Single(s.GrobOverrides);
        Assert.Single(s.GrobReverts);
    }

    [Fact]
    public void BuildMultiStaffScore_IncludesChordExtras_ButNotGrob()
    {
        var c = MakeContent(
            chordNames: OneChord(), percentRepeats: OnePercent(), crossStaff: OneCrossStaff(),
            grobOverrides: OneOverride(), grobReverts: OneRevert());
        var staff = new Staff(ClefType.Treble, ImmutableArray.Create(EmptyVoice()));
        var groups = ImmutableArray.Create(new StaffGroup(StaffGroupType.Single, ImmutableArray.Create(staff)));

        var ms = ScoreAssembler.BuildMultiStaffScore(groups, c);

        Assert.Single(ms.StaffGroups);
        Assert.Single(ms.ChordNames);
        Assert.Single(ms.PercentRepeats);
        Assert.Single(ms.CrossStaffItems);
        // Grob overrides/reverts are not surfaced at the multi-staff level.
        Assert.Empty(ms.GrobOverrides);
        // Init-only tempo properties flow through here too.
        Assert.Equal("Allegro", ms.TempoText);
        Assert.Equal(2, ms.TempoBeatUnit);
    }

    [Fact]
    public void BuildMultiStaffScore_PreservesStaffGroups()
    {
        var staffA = new Staff(ClefType.Treble, ImmutableArray.Create(EmptyVoice("a")));
        var staffB = new Staff(ClefType.Bass, ImmutableArray.Create(EmptyVoice("b")));
        var groups = ImmutableArray.Create(
            new StaffGroup(StaffGroupType.Single, ImmutableArray.Create(staffA, staffB)));

        var ms = ScoreAssembler.BuildMultiStaffScore(groups, MakeContent());

        Assert.Single(ms.StaffGroups);
        Assert.Equal(2, ms.StaffGroups[0].Staves.Length);
    }
}
