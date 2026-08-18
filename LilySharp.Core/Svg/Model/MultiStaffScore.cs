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
    int Clef = 0,
    // The OPENING metronome mark's `tempo` declaration. A mid-piece `tempo`
    // carries its own offset on the mark itself (the music walk reads it off the
    // syntax node); the opening one is synthesised from the score's metadata by
    // MusicMarkEngraver.MergeTempoMark, which has no syntax to read, so its
    // offset travels here with the rest of the header.
    int Tempo = 0
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

    /// <summary>
    /// Which face each kind of non-music text is drawn in, from the <c>font</c> header
    /// directive. Never null — a score without one carries
    /// <see cref="Rendering.TextFontPlan.Default"/>.
    /// </summary>
    public Rendering.TextFontPlan Fonts { get; init; } = Rendering.TextFontPlan.Default;

    /// <summary>
    /// The text measurements this score's <see cref="Fonts"/> imply — what the LAYOUT asks,
    /// in the same words the drawing asks (<c>role</c> and <c>style</c>).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE LAYOUT USED NOT TO HAVE THIS AT ALL, and that is why a named face was drawn
    /// but not measured: <see cref="Fonts"/> reached the DRAWING context only
    /// (<c>SharedRenderer</c> sets <c>doc.Fonts</c>), while every measurement in
    /// <c>Svg/Layout</c> went to a static keyed by family alone. Hanging the metrics on the
    /// score is what lets the two agree, because the layout already carries the score
    /// everywhere it measures.
    /// <para>
    /// Built once per score and shared: <see cref="Rendering.ScoreTextMetrics"/> caches its
    /// (role, style) resolutions, and rebuilding it per call would ask the font manager per
    /// drawn string.
    /// </para>
    /// </remarks>
    public Rendering.ScoreTextMetrics TextMetrics =>
        _textMetrics ??= Fonts.IsDefault
            ? Rendering.ScoreTextMetrics.Bundled
            : new Rendering.ScoreTextMetrics(Fonts);

    private Rendering.ScoreTextMetrics? _textMetrics;

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

    /// <summary>True when EVERY rendered staff is a tab staff — no notation staff draws a
    /// key signature. An all-tab score reclaims the reserved key-signature prefix width
    /// (there is no notation staff to align against), so its notes spread into it.</summary>
    /// <remarks>
    /// ⚠️ THE KEY QUESTION ONLY. It used to gate the METER too, which was the same answer
    /// while a tab staff engraved neither; it is not. ly/property-init.ly:825-826 is the first
    /// revert in tabFullNotation, above its no-stem-extend one, and it undoes the blanking at
    /// ly/engraver-init.ly:1219-1220, five lines under that block's \remove Key_engraver —
    /// which itself has no revert anywhere. The meter
    /// asks <c>SpacingRules.AnyStaffEngravesTime</c>, which reads
    /// <see cref="Staff.TabNumbersOnly"/> per staff.
    /// </remarks>
    public bool AllStavesTab => StaffGroups.Length > 0
        && StaffGroups.All(g => g.Staves.Length > 0 && g.Staves.All(s => s.IsTab));

    /// <summary>The leading key signature actually engraved at each system head: the
    /// score key normally, but C major (nothing) for an all-tab score — tab never prints
    /// a key signature, so its reserved prefix width is reclaimed. The full record, so a
    /// custom (non-traditional) signature keeps its glyphs in the reservation.</summary>
    public KeySignature LeadingKey => AllStavesTab ? KeySignature.CMajor : KeySignature;

    /// <summary>Total number of individual staves.</summary>
    public int TotalStaffCount => StaffGroups.Sum(g => g.StaffCount);

    /// <summary>Whether any staff carries more than one voice (polyphony).
    /// NOTE: this no longer gates incremental reuse — both the content key
    /// (MeasureContentKey.Compute) and the spring gate
    /// (SystemBreaker.ComputeMultiStaffSpringData) fold every voice, so a
    /// secondary-voice edit is localized like any other.</summary>
    public bool HasSecondaryVoices => EnumerateStaves().Any(s => s.Staff.Voices.Length > 1);

    /// <summary>Creates a multi-staff score from its staff groups and score-level tables.</summary>
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
    public static MultiStaffScore FromScore(Score score, string? instrumentName = null, int lines = 5,
        PedalStyle pedalStyle = PedalStyle.Bracket)
    {
        var clef = Staff.ParseClef(score.Clef);
        // Preserve ALL voices (polyphony), not just the primary, so the renderer
        // can draw every voice. (Voice collision offsets in the multi-staff layout
        // path remain a separate refinement.)
        // The one staff a solo score has IS the one the score's clef declaration set, so it
        // carries that offset. ⚠️ Needed because the renderer reads the clef's data-pos off
        // the STAFF now (a multi-staff score's staves each have their own); without this the
        // wrap left it 0 and a solo score's clef — the one case that always worked — would
        // have lost its tag.
        var staff = Staff.Create(clef, score.Voices, instrumentName)
            with { Lines = lines, PedalStyle = pedalStyle, ClefPosition = score.Header.Clef };
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
            Fonts = score.Fonts,
        };
    }

    /// <summary>
    /// The first staff that actually carries musical content (a non-empty primary
    /// voice), skipping content-less rows such as chord rows. For a normal score
    /// this is exactly <c>StaffGroups[0].PrimaryStaff</c>, so existing behaviour is
    /// unchanged; it only differs when a chord row precedes the music in the score
    /// order, where it keeps the measure structure driven by the real staff.
    /// </summary>
    public Staff PrimaryContentStaff => PrimaryContentStaffWithIndex().Staff;

    /// <summary>
    /// <see cref="PrimaryContentStaff"/> together with its GLOBAL staff index — the index
    /// <see cref="EnumerateStaves"/> hands out, and the one every per-staff table
    /// (dynamics, tuplet brackets, articulations) is keyed by.
    /// </summary>
    /// <remarks>
    /// ONE HOME ON PURPOSE (HANDOFF §5.2.1②): a caller that needs the index used to have to
    /// re-derive it, and a second spelling of "which staff is the primary one" would drift
    /// from this one silently — the caller would then filter a per-staff table by the WRONG
    /// staff and get a plausible answer for the wrong music.
    /// </remarks>
    public (Staff Staff, int Index) PrimaryContentStaffWithIndex()
    {
        // Skip ossias too: an ossia stacks ABOVE the staff it decorates, so
        // it can be the FIRST staff — but its stream is mostly rests and it
        // carries no break marks, so letting it drive line breaking (or the
        // measure count) would ignore the real music's `break`s.
        foreach (var (_, staff, index) in EnumerateStaves())
            if (!staff.IsTextRow && !staff.IsOssia && staff.PrimaryVoice.Measures.Length > 0)
                return (staff, index);
        foreach (var (_, staff, index) in EnumerateStaves())
            if (!staff.IsTextRow && staff.PrimaryVoice.Measures.Length > 0)
                return (staff, index);
        return (StaffGroups[0].PrimaryStaff, 0);
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