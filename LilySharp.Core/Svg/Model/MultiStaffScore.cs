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
/// Source byte offsets of the score's header grobs, so the renderer can stamp each
/// with a data-pos and the preview can click-to-jump / highlight them like notes.
/// 0 means "no known position" (the grob is then drawn without a data-pos).
/// </summary>
public readonly record struct HeaderPositions(
    int Title = 0,
    int Composer = 0,
    int Time = 0,
    int Key = 0,
    int Clef = 0
);

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

    /// <summary>Textual tempo marking ("Grave") for the opening metronome mark.</summary>
    public string? TempoText { get; init; }

    /// <summary>Metronome beat unit of the opening tempo (4 = quarter).</summary>
    public int TempoBeatUnit { get; init; } = 4;

    /// <summary>Augmentation dots on the opening tempo's beat unit.</summary>
    public int TempoDots { get; init; }

    /// <summary>The note value the initial tempo asked to swing (0 = none, 8, 16).</summary>
    public int SwingSubdivision { get; }

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

    /// <summary>Source offsets of the header grobs (title/composer/time/key) so the
    /// SVG can tag them with data-pos and the preview can click-to-jump.</summary>
    public HeaderPositions Header { get; }

    /// <summary>Whether this score has a grand staff.</summary>
    public bool HasGrandStaff => StaffGroups.Any(g => g.IsGrandStaff);

    /// <summary>Whether this score has multiple staff groups.</summary>
    public bool IsMultiStaff => StaffGroups.Length > 1 || HasGrandStaff;

    /// <summary>Total number of individual staves.</summary>
    public int TotalStaffCount => StaffGroups.Sum(g => g.StaffCount);

    /// <summary>Whether any staff carries more than one voice (polyphony). The
    /// incremental content key and the spring gate fold only each staff's PRIMARY
    /// voice, so a secondary-voice edit is not localized by them; the incremental
    /// compiler must fall back to full layout when this is true to stay
    /// byte-identical with a full recompile.</summary>
    public bool HasSecondaryVoices => EnumerateStaves().Any(s => s.Staff.Voices.Length > 1);

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
        ImmutableArray<TrillSpannerItem>? trillSpanners = null,
        HeaderPositions header = default,
        int swingSubdivision = 0)
    {
        if (staffGroups.Length == 0)
            throw new ArgumentException("Score must have at least one staff group", nameof(staffGroups));

        StaffGroups = staffGroups;
        TimeSignature = timeSignature;
        KeySignature = keySignature;
        Tempo = tempo;
        SwingSubdivision = swingSubdivision;
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
        Header = header;
    }

    /// <summary>
    /// Creates a MultiStaffScore from a single-voice Score (for backward compatibility).
    /// </summary>
    public static MultiStaffScore FromScore(Score score)
    {
        var clef = Staff.ParseClef(score.Clef);
        // Preserve ALL voices (polyphony), not just the primary, so the renderer
        // can draw every voice. (Voice collision offsets in the multi-staff layout
        // path remain a separate refinement.)
        var staff = Staff.Create(clef, score.Voices);
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
            // Arpeggios were dropped here (skipped between GraceNotes and the named
            // figuredBasses below), so a SINGLE-staff score — which always renders
            // through FromScore — never showed any arpeggio. Multi-staff scores build
            // MultiStaffScore directly with per-staff arpeggios, which masked this.
            arpeggios: score.Arpeggios,
            figuredBasses: score.FiguredBasses,
            chordNames: score.ChordNames,
            percentRepeats: score.PercentRepeats,
            crossStaffItems: score.CrossStaffItems,
            grobOverrides: score.GrobOverrides,
            grobReverts: score.GrobReverts,
            trillSpanners: score.TrillSpanners,
            header: score.Header,
            swingSubdivision: score.SwingSubdivision)
        {
            TempoText = score.TempoText,
            TempoBeatUnit = score.TempoBeatUnit,
            TempoDots = score.TempoDots,
        };
    }

    /// <summary>
    /// The first staff that actually carries musical content (a non-empty primary
    /// voice), skipping content-less rows such as chord rows. For a normal score
    /// this is exactly <c>StaffGroups[0].PrimaryStaff</c>, so existing behaviour is
    /// unchanged; it only differs when a chord row precedes the music in the score
    /// order, where it keeps the measure structure driven by the real staff.
    /// </summary>
    public Staff PrimaryContentStaff
    {
        get
        {
            // Skip ossias too: an ossia stacks ABOVE the staff it decorates, so
            // it can be the FIRST staff — but its stream is mostly rests and it
            // carries no break marks, so letting it drive line breaking (or the
            // measure count) would ignore the real music's `break`s.
            foreach (var (_, staff, _) in EnumerateStaves())
                if (!staff.IsTextRow && !staff.IsOssia && staff.PrimaryVoice.Measures.Length > 0)
                    return staff;
            foreach (var (_, staff, _) in EnumerateStaves())
                if (!staff.IsTextRow && staff.PrimaryVoice.Measures.Length > 0)
                    return staff;
            return StaffGroups[0].PrimaryStaff;
        }
    }

    /// <summary>Number of measures (from the first content staff).</summary>
    public int MeasureCount => PrimaryContentStaff.MeasureCount;

    /// <summary>
    /// True when every row is a text row (lyrics and/or chords) and there is no
    /// notation staff — a lead-sheet score. Such a score draws a measure grid on
    /// its top text row and spaces each bar by its densest (lyric) row rather than
    /// the coarse chord durations.
    /// </summary>
    public bool IsLeadSheet
    {
        get
        {
            bool any = false;
            foreach (var (_, staff, _) in EnumerateStaves())
            {
                any = true;
                if (!staff.IsTextRow)
                    return false;
            }
            return any;
        }
    }

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