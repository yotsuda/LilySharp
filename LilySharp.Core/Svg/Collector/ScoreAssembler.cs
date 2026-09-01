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
            WithoutInitialRepeatBar(voices),
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
            WithoutInitialRepeatBar(staffGroups),
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

    /// <summary>
    /// LilyPond prints no automatic repeat bar line at the START of a piece, so neither
    /// does Lily#: the score's first measure loses an opening repeat, and nothing else
    /// about it changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: <c>lily/bar-engraver.cc:432-449 Bar_engraver::pre_process_music</c>,
    /// whose own comment above the method reads "At the start of a piece, we don't print any
    /// repeat bars". The whole <c>repeatCommands</c> loop — the one that turns the
    /// <c>start-repeat</c> posted by
    /// <c>lily/repeat-acknowledge-engraver.cc:96-109
    /// Repeat_acknowledge_engraver::listen_volta_repeat_start</c> into
    /// <c>startRepeatBarType</c> — is skipped while <c>first_time_</c> holds, i.e. while
    /// the Timing context is still at its first moment
    /// (<c>lily/bar-engraver.cc:414-417 Bar_engraver::initialize</c>). So the grob is
    /// never CREATED; this is a model edit, not a draw-time skip, because Lily#'s
    /// <c>StartBarline</c> is read by fifteen spacing/layout sites as well as the
    /// renderer — suppressing it only in <see cref="Rendering.SharedRenderer"/> would
    /// leave the reserved width behind as a gap.
    /// </para>
    /// <para>
    /// MEASURED on 2.26.0 (session 318, <c>scratch/p318/t4/startrepeat.ly</c>): the same
    /// <c>\repeat volta 2</c> prints no opener when it opens the piece and prints
    /// <c>.|:</c> when one bar precedes it — position alone decides, which is what makes
    /// this a rule rather than a quirk of the example.
    /// </para>
    /// <para>
    /// ⚠️ Scope, in LilyPond's own terms: the gate is on AUTOMATIC bars. An explicit
    /// <c>\bar ".|:"</c> sets <c>whichBar</c> and prints even at moment 0
    /// (<c>lily/bar-engraver.cc:441-445 Bar_engraver::pre_process_music</c>, the branch above
    /// the gate), and <c>\set Score.printInitialRepeatBar = ##t</c> restores the opener for
    /// the lead-sheet convention that does want it
    /// (Documentation/en/notation/repeats.itely:160-172, "Repeats / Long repeats").
    /// Lily# spells neither, so it has one behaviour and it is LilyPond's default. A
    /// <c>|:</c> is always the structural kind here, which is exactly the kind LilyPond
    /// suppresses.
    /// </para>
    /// <para>
    /// This runs HERE because this is the one place both model constructors are invoked,
    /// so the rule has one home for every path — single voice, several voices, staff
    /// groups, and the chord / lyric text rows, which draw their own barlines from their
    /// own <see cref="Voice"/> and would otherwise keep an opener the staff above had
    /// dropped (§5.2.1② — the same quantity must not be decided in two places).
    /// </para>
    /// </remarks>
    private static ImmutableArray<StaffGroup> WithoutInitialRepeatBar(ImmutableArray<StaffGroup> groups)
    {
        ImmutableArray<StaffGroup>.Builder? builder = null;
        for (int i = 0; i < groups.Length; i++)
        {
            var staves = WithoutInitialRepeatBar(groups[i].Staves);
            if (staves.Equals(groups[i].Staves))
                continue;
            builder ??= groups.ToBuilder();
            builder[i] = groups[i] with { Staves = staves };
        }
        return builder?.ToImmutable() ?? groups;
    }

    /// <inheritdoc cref="WithoutInitialRepeatBar(ImmutableArray{StaffGroup})"/>
    private static ImmutableArray<Staff> WithoutInitialRepeatBar(ImmutableArray<Staff> staves)
    {
        ImmutableArray<Staff>.Builder? builder = null;
        for (int i = 0; i < staves.Length; i++)
        {
            var voices = WithoutInitialRepeatBar(staves[i].Voices);
            if (voices.Equals(staves[i].Voices))
                continue;
            builder ??= staves.ToBuilder();
            builder[i] = staves[i] with { Voices = voices };
        }
        return builder?.ToImmutable() ?? staves;
    }

    /// <inheritdoc cref="WithoutInitialRepeatBar(ImmutableArray{StaffGroup})"/>
    private static ImmutableArray<Voice> WithoutInitialRepeatBar(ImmutableArray<Voice> voices)
    {
        ImmutableArray<Voice>.Builder? builder = null;
        for (int i = 0; i < voices.Length; i++)
        {
            var voice = voices[i];
            if (voice.Measures.Length == 0)
                continue;
            var first = voice.Measures[0];
            // RepeatBoth is the fused ':| |:' glyph. Nothing in the collector produces one at
            // a measure's START today — every producer writes RepeatStart and
            // SynchronizeBarlines takes a max over {None, RepeatStart} — so this arm is
            // named for the same reason StartBarWithBreakPieces and RepeatPairingScanner
            // name it, and because LilyPond's gate would cover it unchanged: at moment 0 it
            // drops the whole automatic bar, both observations at once (the else arm of
            // lily/bar-engraver.cc:489-494 Bar_engraver::pre_process_music), so there is no
            // half of it left to draw.
            if (first.StartBarline is not (BarlineType.RepeatStart or BarlineType.RepeatBoth))
                continue;
            builder ??= voices.ToBuilder();
            builder[i] = voice with
            {
                Measures = voice.Measures.SetItem(
                    0, first with { StartBarline = BarlineType.None }),
            };
        }
        return builder?.ToImmutable() ?? voices;
    }
}
