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
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// An immutable snapshot of everything <see cref="MeasureCollector"/> accumulates
/// during a pass that a <see cref="Score"/> / <see cref="MultiStaffScore"/> needs
/// beyond its voices or staff groups: the piece-level metadata and every
/// annotation list. Captured once at the end of collection
/// (<c>MeasureCollector.CaptureScoreContent</c>) and handed to
/// <see cref="ScoreAssembler"/>, which is the single place that knows the shape of
/// the model constructors.
/// </summary>
internal sealed record ScoreContent(
    TimeSignature TimeSignature,
    KeySignature KeySignature,
    string Clef,
    int? Tempo,
    string? Title,
    string? Composer,
    int SwingSubdivision,
    ImmutableArray<DynamicItem> Dynamics,
    ImmutableArray<ArticulationItem> Articulations,
    ImmutableArray<GraceNoteItem> GraceNotes,
    ImmutableArray<LyricItem> Lyrics,
    ImmutableArray<MusicMarkItem> MusicMarks,
    ImmutableArray<CustomTextItem> CustomTexts,
    ImmutableArray<VoltaBracketItem> VoltaBrackets,
    ImmutableArray<TupletBracketItem> TupletBrackets,
    ImmutableArray<ArpeggioItem> Arpeggios,
    ImmutableArray<FiguredBassItem> FiguredBasses,
    ImmutableArray<ChordNameItem> ChordNames,
    ImmutableArray<PercentRepeatItem> PercentRepeats,
    ImmutableArray<CrossStaffItem> CrossStaffItems,
    ImmutableArray<GrobOverride> GrobOverrides,
    ImmutableArray<GrobRevert> GrobReverts,
    ImmutableArray<TrillSpannerItem> TrillSpanners,
    HeaderPositions Header,
    string? TempoText,
    int TempoBeatUnit,
    int TempoDots,
    Rendering.TextFontPlan Fonts,
    Layout.LayoutOptions Paper);

/// <summary>
/// Turns a <see cref="ScoreContent"/> snapshot plus a set of voices / staff groups
/// into the corresponding model object. This is the one place the ~25-argument
/// Score / MultiStaffScore constructors are invoked — previously the same call was
/// copy-pasted across three collector methods, which is how they drifted (the
/// multi-voice path silently omitted chord names, percent repeats and cross-staff
/// items — a copy-paste oversight when voice{}-block support was added). Both Score
/// paths now surface the full annotation set, including grob overrides/reverts.
/// </summary>
internal static class ScoreAssembler
{
    /// <summary>
    /// Builds a single- or multi-voice <see cref="Score"/> with the full annotation
    /// set. A single-staff score therefore renders the same annotations (chord names,
    /// percent repeats, etc.) whether it has one voice or several — the multi-voice
    /// path used to drop chord names / percent repeats / cross-staff items.
    /// </summary>
    public static Score BuildScore(ImmutableArray<Voice> voices, ScoreContent c) =>
        new Score(
            voices,
            c.TimeSignature,
            c.KeySignature,
            c.Clef,
            c.Tempo,
            c.Title,
            c.Composer,
            dynamics: c.Dynamics,
            articulations: c.Articulations,
            graceNotes: c.GraceNotes,
            lyrics: c.Lyrics,
            musicMarks: c.MusicMarks,
            customTexts: c.CustomTexts,
            voltaBrackets: c.VoltaBrackets,
            tupletBrackets: c.TupletBrackets,
            arpeggios: c.Arpeggios,
            figuredBasses: c.FiguredBasses,
            chordNames: c.ChordNames,
            percentRepeats: c.PercentRepeats,
            crossStaffItems: c.CrossStaffItems,
            grobOverrides: c.GrobOverrides,
            grobReverts: c.GrobReverts,
            trillSpanners: c.TrillSpanners,
            header: c.Header,
            swingSubdivision: c.SwingSubdivision)
        {
            TempoText = c.TempoText,
            TempoBeatUnit = c.TempoBeatUnit,
            TempoDots = c.TempoDots,
            Fonts = c.Fonts,
            Paper = c.Paper,
        };

    /// <summary>Single-voice convenience overload.</summary>
    public static Score BuildScore(Voice voice, ScoreContent c) =>
        BuildScore(ImmutableArray.Create(voice), c);

    /// <summary>
    /// Builds a <see cref="MultiStaffScore"/> with the full annotation set — chord names,
    /// percent repeats, cross-staff items, and grob overrides/reverts (so a top-level or
    /// in-music \override colours / hides grobs on a multi-staff score too).
    /// </summary>
    public static MultiStaffScore BuildMultiStaffScore(ImmutableArray<StaffGroup> staffGroups, ScoreContent c) =>
        new MultiStaffScore(
            staffGroups,
            c.TimeSignature,
            c.KeySignature,
            c.Tempo,
            c.Title,
            c.Composer,
            swingSubdivision: c.SwingSubdivision,
            lyrics: c.Lyrics,
            musicMarks: c.MusicMarks,
            customTexts: c.CustomTexts,
            voltaBrackets: c.VoltaBrackets,
            tupletBrackets: c.TupletBrackets,
            dynamics: c.Dynamics,
            articulations: c.Articulations,
            graceNotes: c.GraceNotes,
            arpeggios: c.Arpeggios,
            figuredBasses: c.FiguredBasses,
            chordNames: c.ChordNames,
            percentRepeats: c.PercentRepeats,
            crossStaffItems: c.CrossStaffItems,
            grobOverrides: c.GrobOverrides,
            grobReverts: c.GrobReverts,
            trillSpanners: c.TrillSpanners,
            header: c.Header)
        {
            TempoText = c.TempoText,
            TempoBeatUnit = c.TempoBeatUnit,
            TempoDots = c.TempoDots,
            Fonts = c.Fonts,
            Paper = c.Paper,
        };

    // ⚠️ THERE IS NO INITIAL-REPEAT GATE HERE ANY MORE, and that is a decision, not an
    // omission. LILYSHARP-OWN: a `|:` that opens the piece IS printed. LilyPond's default
    // prints no automatic repeat bar at moment 0
    // (LILYPOND-REF: lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music,
    // "At the start of a piece, we don't print any repeat bars"), and session 319 ported
    // that gate at this very spot — the one place both model
    // constructors are invoked — with a model edit rather than a draw-time skip, because
    // Measure.StartBarline is read by fifteen spacing/layout sites. Session 328 removed it on
    // the owner's word: in Lily# a `|:` is always one the writer wrote (there is no automatic
    // repeat bar to suppress), the corpus is lead sheets, and LilyPond keeps the same door open
    // with `\set Score.printInitialRepeatBar = ##t` (Documentation/en/notation/repeats.itely:
    // 160-172). LilyPondExporter writes that setting into every twin so the pages agree, and
    // audit/lp-geometry/probes/initial-repeat-bar.ly carries it too, so the ledger point
    // line-start.time-to-first-note.initial-repeat measures the opener's width on both sides.
    // Observed by: InitialRepeatBarTests (every producer of a RepeatStart keeps it at
    // measure 0), the snapshot test/initial-repeat-bar, and that ledger pair.
}
