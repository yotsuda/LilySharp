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

namespace LilySharp.Core.Svg.Model;

/// <summary>
/// A complete musical score with multiple staff groups.
/// </summary>
/// <remarks>
/// MultiStaffScore extends the basic Score concept to support:
/// - Multiple staff groups (single staves, grand staves, bracketed groups)
/// - Different clefs per staff
/// - Voice-to-staff mapping
///
/// This is the primary model for rendering piano, organ, and orchestral scores.
/// </remarks>
public sealed record MultiStaffScore
{
    /// <summary>Staff groups in this score.</summary>
    public ImmutableArray<StaffGroup> StaffGroups { get; }

    /// <summary>Time signature for the score.</summary>
    public TimeSignature TimeSignature { get; }

    /// <summary>Key signature for the score.</summary>
    public KeySignature KeySignature { get; }

    /// <summary>Tempo in BPM (optional).</summary>
    public int? Tempo { get; }

    /// <summary>Title (optional).</summary>
    public string? Title { get; }

    /// <summary>Composer (optional).</summary>
    public string? Composer { get; }

    /// <summary>Lyrics in the score.</summary>
    public ImmutableArray<LyricItem> Lyrics { get; }

    /// <summary>Music marks (segno, coda, fine, D.S., etc.).</summary>
    public ImmutableArray<MusicMarkItem> MusicMarks { get; }

    /// <summary>Custom text annotations.</summary>
    public ImmutableArray<CustomTextItem> CustomTexts { get; }

    /// <summary>Volta brackets (first/second ending).</summary>
    public ImmutableArray<VoltaBracketItem> VoltaBrackets { get; }

    /// <summary>Tuplet brackets.</summary>
    public ImmutableArray<TupletBracketItem> TupletBrackets { get; }

    /// <summary>Dynamic markings.</summary>
    public ImmutableArray<DynamicItem> Dynamics { get; }

    /// <summary>Articulation marks.</summary>
    public ImmutableArray<ArticulationItem> Articulations { get; }

    /// <summary>Grace notes.</summary>
    public ImmutableArray<GraceNoteItem> GraceNotes { get; }

    /// <summary>Arpeggio markings.</summary>
    public ImmutableArray<ArpeggioItem> Arpeggios { get; }

    /// <summary>Figured bass annotations.</summary>
    public ImmutableArray<FiguredBassItem> FiguredBasses { get; }

    /// <summary>Chord name symbols.</summary>
    public ImmutableArray<ChordNameItem> ChordNames { get; }

    /// <summary>Percent repeat markers.</summary>
    public ImmutableArray<PercentRepeatItem> PercentRepeats { get; }

    /// <summary>Cross-staff note assignments for grand staff rendering.</summary>
    public ImmutableArray<CrossStaffItem> CrossStaffItems { get; }

    /// <summary>Grob property overrides.</summary>
    public ImmutableArray<GrobOverride> GrobOverrides { get; }

    /// <summary>Grob property reverts.</summary>
    public ImmutableArray<GrobRevert> GrobReverts { get; }

    /// <summary>Trill spanners (tr + wavy line).</summary>
    public ImmutableArray<TrillSpannerItem> TrillSpanners { get; }

    /// <summary>Whether this score has a grand staff.</summary>
    public bool HasGrandStaff => StaffGroups.Any(g => g.IsGrandStaff);

    /// <summary>Whether this score has multiple staff groups.</summary>
    public bool IsMultiStaff => StaffGroups.Length > 1 || HasGrandStaff;

    /// <summary>Total number of individual staves.</summary>
    public int TotalStaffCount => StaffGroups.Sum(g => g.StaffCount);

    public MultiStaffScore(
        ImmutableArray<StaffGroup> staffGroups,
        TimeSignature timeSignature,
        KeySignature keySignature,
        int? tempo = null,
        string? title = null,
        string? composer = null,
        ImmutableArray<LyricItem>? lyrics = null,
        ImmutableArray<MusicMarkItem>? musicMarks = null,
        ImmutableArray<CustomTextItem>? customTexts = null,
        ImmutableArray<VoltaBracketItem>? voltaBrackets = null,
        ImmutableArray<TupletBracketItem>? tupletBrackets = null,
        ImmutableArray<DynamicItem>? dynamics = null,
        ImmutableArray<ArticulationItem>? articulations = null,
        ImmutableArray<GraceNoteItem>? graceNotes = null,
        ImmutableArray<ArpeggioItem>? arpeggios = null,
        ImmutableArray<FiguredBassItem>? figuredBasses = null,
        ImmutableArray<ChordNameItem>? chordNames = null,
        ImmutableArray<PercentRepeatItem>? percentRepeats = null,
        ImmutableArray<CrossStaffItem>? crossStaffItems = null,
        ImmutableArray<GrobOverride>? grobOverrides = null,
        ImmutableArray<GrobRevert>? grobReverts = null,
        ImmutableArray<TrillSpannerItem>? trillSpanners = null)
    {
        if (staffGroups.Length == 0)
            throw new ArgumentException("Score must have at least one staff group", nameof(staffGroups));

        StaffGroups = staffGroups;
        TimeSignature = timeSignature;
        KeySignature = keySignature;
        Tempo = tempo;
        Title = title;
        Composer = composer;
        Lyrics = lyrics ?? ImmutableArray<LyricItem>.Empty;
        MusicMarks = musicMarks ?? ImmutableArray<MusicMarkItem>.Empty;
        CustomTexts = customTexts ?? ImmutableArray<CustomTextItem>.Empty;
        VoltaBrackets = voltaBrackets ?? ImmutableArray<VoltaBracketItem>.Empty;
        TupletBrackets = tupletBrackets ?? ImmutableArray<TupletBracketItem>.Empty;
        Dynamics = dynamics ?? ImmutableArray<DynamicItem>.Empty;
        Articulations = articulations ?? ImmutableArray<ArticulationItem>.Empty;
        GraceNotes = graceNotes ?? ImmutableArray<GraceNoteItem>.Empty;
        Arpeggios = arpeggios ?? ImmutableArray<ArpeggioItem>.Empty;
        FiguredBasses = figuredBasses ?? ImmutableArray<FiguredBassItem>.Empty;
        ChordNames = chordNames ?? ImmutableArray<ChordNameItem>.Empty;
        PercentRepeats = percentRepeats ?? ImmutableArray<PercentRepeatItem>.Empty;
        CrossStaffItems = crossStaffItems ?? ImmutableArray<CrossStaffItem>.Empty;
        GrobOverrides = grobOverrides ?? ImmutableArray<GrobOverride>.Empty;
        GrobReverts = grobReverts ?? ImmutableArray<GrobRevert>.Empty;
        TrillSpanners = trillSpanners ?? ImmutableArray<TrillSpannerItem>.Empty;
    }

    /// <summary>
    /// Creates a MultiStaffScore from a single-voice Score (for backward compatibility).
    /// </summary>
    public static MultiStaffScore FromScore(Score score)
    {
        var clef = Staff.ParseClef(score.Clef);
        var staff = Staff.Create(clef, score.Voice);
        var staffGroup = StaffGroup.CreateSingle(staff);

        return new MultiStaffScore(
            ImmutableArray.Create(staffGroup),
            score.TimeSignature,
            score.KeySignature,
            score.Tempo,
            score.Title,
            score.Composer,
            score.Lyrics,
            score.MusicMarks,
            score.CustomTexts,
            score.VoltaBrackets,
            score.TupletBrackets,
            score.Dynamics,
            score.Articulations,
            score.GraceNotes,
            figuredBasses: score.FiguredBasses,
            chordNames: score.ChordNames,
            percentRepeats: score.PercentRepeats,
            crossStaffItems: score.CrossStaffItems,
            grobOverrides: score.GrobOverrides,
            grobReverts: score.GrobReverts,
            trillSpanners: score.TrillSpanners);
    }

    /// <summary>Number of measures (from first staff of first group).</summary>
    public int MeasureCount => StaffGroups[0].PrimaryStaff.MeasureCount;

    /// <summary>Gets all voices across all staves.</summary>
    public IEnumerable<Voice> AllVoices
    {
        get
        {
            foreach (var group in StaffGroups)
            foreach (var staff in group.Staves)
            foreach (var voice in staff.Voices)
                yield return voice;
        }
    }

    /// <summary>
    /// Iterates over all staves with their group context. The yielded
    /// <c>GlobalStaffIndex</c> is the score-wide staff index used by
    /// <c>StaffLayout.StaffIndex</c> and <c>FindStaffYInSystem</c>; it
    /// continues across <see cref="StaffGroups"/> boundaries.
    /// </summary>
    public IEnumerable<(StaffGroup Group, Staff Staff, int GlobalStaffIndex)> EnumerateStaves()
    {
        int globalIndex = 0;
        foreach (var group in StaffGroups)
        {
            for (int i = 0; i < group.Staves.Length; i++)
            {
                yield return (group, group.Staves[i], globalIndex);
                globalIndex++;
            }
        }
    }
}