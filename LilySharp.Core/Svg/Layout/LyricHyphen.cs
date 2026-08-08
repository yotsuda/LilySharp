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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for lyric hyphen and extender layout.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:20-50 default parameters
/// LILYPOND-REF: scm/define-grobs.scm:2149-2167 LyricHyphen grob
/// </remarks>
internal sealed record LyricHyphenParameters
{
    /// <summary>Distance between the starts of consecutive dashes (in staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:72 dash_period read; the value 10.0
    /// is the LyricHyphen dash-period in scm/define-grobs.scm.</remarks>
    public double DashPeriod { get; init; } = 10.0;

    /// <summary>Length of one dash (in staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:73 dash_length read; the value 0.66
    /// is the LyricHyphen length in scm/define-grobs.scm.</remarks>
    public double DashLength { get; init; } = 0.66;

    /// <summary>Dash BOTTOM above the text baseline (in staff spaces) — the dash box
    /// spans height..height+thickness upward.</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:125 dash_mol's Box Y is (h, h + th);
    /// the value 0.42 is the LyricHyphen height in scm/define-grobs.scm.</remarks>
    public double HyphenHeight { get; init; } = 0.42;

    /// <summary>Dash thickness, in LINE-THICKNESS units (not staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:64-65 th = get_dimension of the
    /// layout line-thickness × the LyricHyphen thickness 1.3 (scm/define-grobs.scm).</remarks>
    public double HyphenThickness { get; init; } = 1.3;

    /// <summary>Padding kept between a syllable and a squeezed dash (in staff spaces).
    /// NOT part of the span points — those are the bare syllable ink edges.</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:107-112 dash_length squeezes between
    /// paddings; the value 0.07 is the LyricHyphen padding in scm/define-grobs.scm.</remarks>
    public double HyphenPadding { get; init; } = 0.07;

    /// <summary>Shortest a squeezed dash may get (in staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:109-111 minimum_length floor; the
    /// value 0.3 is the LyricHyphen minimum-length in scm/define-grobs.scm.</remarks>
    public double HyphenMinimumLength { get; init; } = 0.3;

    /// <summary>Extender line thickness (in staff spaces).</summary>
    public double ExtenderThickness { get; init; } = 0.08;

    /// <summary>Vertical offset from text baseline for extender (in staff spaces).</summary>
    public double ExtenderYOffset { get; init; } = 0.7;

    /// <summary>Padding between syllable and extender start (in staff spaces).</summary>
    public double ExtenderPadding { get; init; } = 0.2;

    /// <summary>Minimum extender length to be drawn (in staff spaces).</summary>
    public double MinExtenderLength { get; init; } = 0.5;

    public static LyricHyphenParameters Default { get; } = new();
}

/// <summary>
/// Represents a single hyphen dash segment.
/// </summary>
public sealed record HyphenDash(
    // Start X position (in staff spaces).
    double X1,
    // End X position (in staff spaces).
    double X2,
    // Y position (in staff spaces).
    double Y,
    // True when this dash belongs to the piece on the NEXT system of a broken
    // hyphen: the draw resolves it against that system's top (via
    // LyricHyphenLayout.NextLyricIndex), and Y is relative to THAT system.
    bool OnNextSystem = false
);

/// <summary>
/// Layout information for lyric hyphen or extender.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:60-100
/// </remarks>
internal sealed record LyricHyphenLayout(
    // Index of the source lyric in the lyrics array.
    int LyricIndex,

    // Type of connector.
    LyricConnectorType Type,

    // Hyphen dashes (may be multiple for wide gaps).
    ImmutableArray<HyphenDash> Dashes,

    // For extenders: start X position.
    double ExtenderStartX = 0,

    // For extenders: end X position.
    double ExtenderEndX = 0,

    // For extenders: Y position.
    double ExtenderY = 0,

    // Whether this connector crosses a system break.
    bool CrossesSystemBreak = false,

    // For system-crossing extenders: end of first segment.
    double FirstSegmentEndX = 0,

    // For system-crossing extenders: start of second segment.
    double SecondSegmentStartX = 0,

    // For system-crossing connectors: index of the NEXT syllable, whose system
    // the second segment/dash is resolved against at draw time (-1 = none).
    // A broken spanner's pieces each live on their OWN system, like LilyPond's.
    int NextLyricIndex = -1,

    // For system-crossing extenders: the second segment's Y, relative to the
    // NEXT syllable's system (same frame as ExtenderY is to the first's).
    double SecondSegmentY = 0
);

/// <summary>
/// Calculates positions for lyric hyphens and extenders.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:1-150
/// LILYPOND-REF: lily/extender-engraver.cc:1-100
///
/// LilyPond distributes multiple hyphens evenly across wide gaps.
/// Extenders can cross system breaks, requiring two separate line segments.
/// </remarks>
internal sealed class LyricHyphenEngraver
{
    private readonly LyricHyphenParameters _params;

    public LyricHyphenEngraver(LyricHyphenParameters? parameters = null)
    {
        _params = parameters ?? LyricHyphenParameters.Default;
    }

    /// <summary>
    /// Calculate hyphen and extender layouts for all lyrics.
    /// </summary>
    /// <param name="measuresByStaff">The staves' measures, for resolving a final
    /// extender's melisma end (null keeps the legacy next-syllable-only behaviour,
    /// used by tests that have no score).</param>
    public ImmutableArray<LyricHyphenLayout> CalculateLayouts(
        IReadOnlyList<LyricLayout> lyricLayouts,
        IReadOnlyList<SystemLayout> systems,
        IReadOnlyDictionary<int, ImmutableArray<Measure>>? measuresByStaff = null)
    {
        if (lyricLayouts.Count == 0)
            return ImmutableArray<LyricHyphenLayout>.Empty;

        // Build measure to system mapping
        var measureToSystem = new Dictionary<int, (int systemIndex, double systemEndX, double systemStartX)>();
        for (int i = 0; i < systems.Count; i++)
        {
            var system = systems[i];
            // Calculate system bounds from measures
            double systemStartX = system.Measures.Length > 0 ? system.Measures[0].X : 0;
            double systemEndX = system.Measures.Length > 0
                ? system.Measures[^1].X + system.Measures[^1].Width
                : 0;
            foreach (var measure in system.Measures)
            {
                measureToSystem[measure.MeasureIndex] = (i, systemEndX, systemStartX);
            }
        }

        var layouts = new List<LyricHyphenLayout>();

        for (int i = 0; i < lyricLayouts.Count; i++)
        {
            var current = lyricLayouts[i];
            if (current.Item.ConnectorType == LyricConnectorType.None)
                continue;

            // Find the next lyric in the same verse OF THE SAME STAFF.
            // LILYPOND-REF: lily/hyphen-engraver.cc:102-115 acknowledge_lyric_syllable — the
            // right bound is whatever syllable THIS engraver is acknowledged with, and there
            // is one engraver per Lyrics context, so in LilyPond a hyphen cannot reach
            // outside its own line at all. Lily# holds every line in one flat list, so the
            // pairing has to be restricted here instead.
            // ⚠️ The obvious name to cite here is `last_syllable_`, and the citation ratchet
            // cannot see it: its symbol regex needs a word boundary after the last
            // alphanumeric, which a trailing-underscore C++ member never has. Cite a method.
            // ⚠️ THE STAFF HALF IS NOT DECORATION. A hyphen joins two syllables of one
            // line, and a line belongs to a staff; matching on the verse number alone was
            // unreachable only while at most one staff per system carried note-bound
            // lyrics. Since a lyric hangs off its own staff, an SATB score has four lines
            // all numbered verse 1, and the next syllable "in the same verse" was as often
            // the staff below's — a hyphen drawn from one staff's word to another's.
            LyricLayout? next = null;
            int nextIndex = -1;
            for (int j = i + 1; j < lyricLayouts.Count; j++)
            {
                if (lyricLayouts[j].Item.VerseNumber == current.Item.VerseNumber
                    && lyricLayouts[j].Item.StaffIndex == current.Item.StaffIndex)
                {
                    next = lyricLayouts[j];
                    nextIndex = j;
                    break;
                }
            }

            if (next == null)
            {
                // A FINAL extender — no later syllable in this verse — still
                // draws: LilyPond "completizes" it with the melisma's LAST note
                // head as the right bound, so the line runs to that head's ink
                // right and never on to the next note column (the whole point of
                // lyric-extender-completion.ly: more notes than lyrics).
                // LILYPOND-REF: lily/extender-engraver.cc:109-123 listen_completize_extender —
                //   "prevents the right bound being extended to the next
                //   note-column if no lyric follows the extender";
                // LILYPOND-REF: lily/extender-engraver.cc:241-257 completize_extender —
                //   RIGHT bound = heads.back();
                // LILYPOND-REF: lily/lyric-extender.cc:80-84 print — right_point
                //   is raised to the last head's extent RIGHT, exactly.
                if (current.Item.ConnectorType == LyricConnectorType.Extender
                    && MelismaEndInkRight(current, measuresByStaff, systems) is { } melismaEnd)
                {
                    double sx = current.X + current.Width / 2 + _params.ExtenderPadding;
                    if (melismaEnd - sx >= _params.MinExtenderLength)
                        layouts.Add(new LyricHyphenLayout(
                            i,
                            LyricConnectorType.Extender,
                            ImmutableArray<HyphenDash>.Empty,
                            ExtenderStartX: sx,
                            ExtenderEndX: melismaEnd,
                            ExtenderY: -current.YUp + _params.ExtenderYOffset));
                }
                continue;
            }

            // Check if crossing system break
            bool crossesSystem = false;
            double systemEndX = 0;
            double nextSystemStartX = 0;
            bool nextAtSystemStart = false;

            if (measureToSystem.TryGetValue(current.Item.MeasureIndex, out var currentSystem) &&
                measureToSystem.TryGetValue(next.Item.MeasureIndex, out var nextSystem))
            {
                crossesSystem = currentSystem.systemIndex != nextSystem.systemIndex;
                if (crossesSystem)
                {
                    systemEndX = currentSystem.systemEndX;
                    nextSystemStartX = nextSystem.systemStartX;
                    // Whether the right syllable sits on the new line's FIRST musical
                    // moment: its measure opens the system and its onset is zero.
                    // LilyPond kills a broken-hyphen piece spanning no musical time —
                    // and a grace note takes none, so a grace under the would-be stub
                    // does not save it (the claim of lyric-hyphen-grace.ly).
                    // LILYPOND-REF: scm/define-grobs.scm:2151 after-line-breaking =
                    //   ly:spanner::kill-zero-spanned-time on LyricHyphen.
                    var nextSys = systems[nextSystem.systemIndex];
                    // Numerator, not == Fraction.Zero: a default Fraction is 0/0
                    // and its value equality checks the denominator too.
                    nextAtSystemStart = nextSys.Measures.Length > 0
                        && next.Item.MeasureIndex == nextSys.Measures[0].MeasureIndex
                        && next.Item.Timing.Numerator == 0;
                }
            }

            var layout = current.Item.ConnectorType switch
            {
                LyricConnectorType.Hyphen => CalculateHyphenLayout(i, current, next, nextIndex, crossesSystem, systemEndX, nextSystemStartX, nextAtSystemStart),
                LyricConnectorType.Extender => CalculateExtenderLayout(i, current, next, nextIndex, crossesSystem, systemEndX, nextSystemStartX, measuresByStaff, systems),
                _ => null
            };

            if (layout != null)
                layouts.Add(layout);
        }

        return layouts.ToImmutableArray();
    }

    /// <summary>
    /// Ink RIGHT edge (absolute X) of the last note the final extender covers: the
    /// slur/tie melisma chain from the syllable's own note. A note extends the chain
    /// while it opens a slur or a tie; the first note that opens neither ends it, and
    /// a rest never joins (LilyPond completizes a pending extender on a headless
    /// timestep unless extendersOverRests). Null when the syllable's note cannot be
    /// resolved (no score measures / timing not found).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/extender-engraver.cc:200-226 stop_translation_timestep —
    ///   one head per timestep joins the extender while the lyrics still run, and a
    ///   headless timestep completizes it (extendersOverRests default #f);
    /// LILYPOND-REF: lily/lyric-extender.cc:75-84 print — the right point is capped
    ///   by the system bound and raised to the last head's extent RIGHT.
    /// The chain stands in for LilyPond's "while the \lyricsto iterator has not run
    /// out", which for exhausted lyrics is exactly the melisma's span.
    /// </remarks>
    private static double? MelismaEndInkRight(
        LyricLayout current,
        IReadOnlyDictionary<int, ImmutableArray<Measure>>? measuresByStaff,
        IReadOnlyList<SystemLayout> systems)
    {
        if (measuresByStaff == null
            || !measuresByStaff.TryGetValue(current.Item.StaffIndex, out var measures))
            return null;

        MusicItem? end = null;
        int endMeasure = -1;
        Fraction endTiming = Fraction.Zero;
        bool started = false, chainOpen = false;

        for (int mi = current.Item.MeasureIndex; mi < measures.Length; mi++)
        {
            var onset = Fraction.Zero;
            foreach (var item in measures[mi].Items)
            {
                bool isNote = item is NoteItem or ChordItem;
                if (!started)
                {
                    if (mi == current.Item.MeasureIndex && onset == current.Item.Timing && isNote)
                    {
                        started = true;
                        (end, endMeasure, endTiming) = (item, mi, onset);
                        chainOpen = MelismaContinues(item);
                    }
                }
                else if (item.Duration > Fraction.Zero)
                {
                    // The next rhythmic moment: a note joins while the chain is
                    // open; anything else (a rest, or a closed chain) completizes.
                    if (!chainOpen || !isNote)
                        goto done;
                    (end, endMeasure, endTiming) = (item, mi, onset);
                    chainOpen = MelismaContinues(item);
                }
                onset += item.Duration;
            }
        }
        done:

        if (end == null)
            return null;

        MeasureLayout? ml = null;
        foreach (var system in systems)
            foreach (var m in system.Measures)
                if (m.MeasureIndex == endMeasure)
                    ml = m;
        if (ml == null)
            return null;
        double inkRight = ml.X + ml.GetXForTiming(endTiming)
            + GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(end)).Right;

        // Cap at the system the syllable lives on (LP caps right_point at the
        // system's right bound; a melisma running past the break keeps the line
        // inside its own system).
        foreach (var system in systems)
            foreach (var m in system.Measures)
                if (m.MeasureIndex == current.Item.MeasureIndex)
                    return Math.Min(inkRight,
                        system.Measures[^1].X + system.Measures[^1].Width);
        return inkRight;
    }

    /// <summary>True when the melisma keeps running past this note — it opens a
    /// slur or a tie onto the next one.</summary>
    private static bool MelismaContinues(MusicItem item) => item switch
    {
        NoteItem n => n.HasSlurStart || n.HasTieStart,
        ChordItem c => c.HasSlurStart || c.HasTieStart,
        _ => false,
    };

    /// <summary>
    /// Calculate hyphen layout: dashes repeat on a fixed period across the span
    /// between the bound syllables' ink edges, the leftover space split evenly
    /// at both ends. A too-tight mid-line dash squeezes down to minimum-length
    /// and then disappears — but never at a line end, where the piece instead
    /// fills with full dashes to the barline. A line-START piece only exists
    /// when it spans real musical time.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:37-158 Lyric_hyphen::print.
    /// </remarks>
    private LyricHyphenLayout? CalculateHyphenLayout(
        int index,
        LyricLayout current,
        LyricLayout next,
        int nextIndex,
        bool crossesSystem,
        double systemEndX,
        double nextSystemStartX,
        bool nextAtSystemStart)
    {
        // Span points are the bound syllables' INK edges — no padding here.
        // LILYPOND-REF: lily/lyric-hyphen.cc:51-62 span_points from each bound's
        //   generic_bound_extent, taking the inner edge.
        double spanLeft = current.X + current.Width / 2;
        double spanRight = next.X - next.Width / 2;

        // The dash box spans (h .. h+th) ABOVE the baseline; the stored Y is the
        // box centre as a device offset from the system top (lyric YUp is up-positive).
        // LILYPOND-REF: lily/lyric-hyphen.cc:125 dash_mol Box Y = (h, h + th).
        double th = _params.HyphenThickness * EngravingDefaults.LineThickness;
        double yCurrent = -current.YUp - (_params.HyphenHeight + th / 2);

        if (crossesSystem)
        {
            var dashes = ImmutableArray.CreateBuilder<HyphenDash>();

            // Line-END piece: left syllable ink right → the end barline's ink
            // LEFT edge (LP: the break column's bound extent). The right bound
            // is broken, so the dash neither squeezes nor disappears — the
            // period just keeps filling to the line end.
            // LILYPOND-REF: lily/lyric-hyphen.cc:107-121 break_status_dir of the
            //   RIGHT bound skips both the squeeze and the disappear.
            // LILYSHARP-OWN: the barline's ink width is taken as the THIN
            //   barline's; a final "|." group's extra thick bar is not seen here.
            AppendDashes(dashes,
                spanLeft, systemEndX - EngravingDefaults.ThinBarlineThickness,
                yCurrent, rightBroken: true, onNextSystem: false);

            // Line-START piece: killed outright when it spans no musical time
            // (the right syllable on the new line's first moment — the common
            // case, and the claim: a grace there takes no time and does not
            // save it). Otherwise it runs from the line-start prefix end to the
            // right syllable's ink left, with the full mid-line squeeze rules.
            // LILYPOND-REF: scm/define-grobs.scm:2151 after-line-breaking =
            //   ly:spanner::kill-zero-spanned-time.
            // LILYSHARP-OWN: the left bound is the first measure's X (= where
            //   music spacing starts); LP bounds on the break-align group's ink
            //   (clef right edge, measured 3.365 vs prefix end ~4.6) —
            //   boundary-column regime.
            if (!nextAtSystemStart)
            {
                double yNext = -next.YUp - (_params.HyphenHeight + th / 2);
                AppendDashes(dashes,
                    nextSystemStartX, spanRight,
                    yNext, rightBroken: false, onNextSystem: true);
            }

            if (dashes.Count == 0)
                return null;
            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Hyphen,
                dashes.ToImmutable(),
                CrossesSystemBreak: true,
                NextLyricIndex: nextIndex
            );
        }

        var single = ImmutableArray.CreateBuilder<HyphenDash>();
        AppendDashes(single, spanLeft, spanRight, yCurrent,
            rightBroken: false, onNextSystem: false);
        if (single.Count == 0)
            return null;
        return new LyricHyphenLayout(
            index,
            LyricConnectorType.Hyphen,
            single.ToImmutable()
        );
    }

    /// <summary>
    /// The dash distribution of Lyric_hyphen::print for one (possibly broken)
    /// piece: n = ceil(l/period − ½) dashes of the declared length, one period
    /// apart, the leftover space split half at each end. A span too tight for a
    /// full dash squeezes it to max(l − 2·padding, minimum-length); a span with
    /// negative leftover drops the dash entirely — both ONLY when the right
    /// bound is a real syllable (never at a broken line end). Appends nothing
    /// when the piece disappears.
    /// </summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:98-134 space_left around
    /// dash_period steps.</remarks>
    private void AppendDashes(
        ImmutableArray<HyphenDash>.Builder dashes,
        double spanLeft, double spanRight, double y,
        bool rightBroken, bool onNextSystem)
    {
        double period = _params.DashPeriod;
        double length = _params.DashLength;
        if (period < length)
            period = 1.5 * length;

        double l = spanRight - spanLeft;
        int n = (int)Math.Ceiling(l / period - 0.5);
        if (n <= 0)
            n = 1;

        if (l < length + 2 * _params.HyphenPadding && !rightBroken)
            length = Math.Max(l - 2 * _params.HyphenPadding, _params.HyphenMinimumLength);

        double spaceLeft = l - length - (n - 1) * period;
        if (spaceLeft < 0.0 && !rightBroken)
            return;
        spaceLeft = Math.Max(spaceLeft, 0.0);

        for (int i = 0; i < n; i++)
        {
            double x1 = spanLeft + i * period + spaceLeft / 2;
            dashes.Add(new HyphenDash(x1, x1 + length, y, onNextSystem));
        }
    }

    /// <summary>
    /// Calculate extender layout with system break support.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/extender-engraver.cc:50-100
    /// </remarks>
    private LyricHyphenLayout? CalculateExtenderLayout(
        int index,
        LyricLayout current,
        LyricLayout next,
        int nextIndex,
        bool crossesSystem,
        double systemEndX,
        double nextSystemStartX,
        IReadOnlyDictionary<int, ImmutableArray<Measure>>? measuresByStaff,
        IReadOnlyList<SystemLayout> systems)
    {
        double startX = current.X + current.Width / 2 + _params.ExtenderPadding;
        // The extender ends at the LAST HELD note's ink right — the melisma's end —
        // not at the next syllable: the line must not run on under notes the NEXT
        // syllable owns. Fall back to the next syllable's ink left only when the
        // held notes are unknown (no markers, or no score measures — unit tests).
        // MEASURED (lyric-melisma-melisma twin): LP extender right 27.210 = the
        // f16 head's ink right (25.906 + head width), while the next syllable
        // stands at 29.07.
        // LILYPOND-REF: lily/lyric-extender.cc:80-84 print — right_point is
        //   raised to the last head's extent RIGHT.
        double endX = !crossesSystem
            && HeldEndInkRight(current.Item, measuresByStaff, systems) is { } heldEnd
                ? heldEnd
                : next.X - next.Width / 2 - _params.ExtenderPadding;
        // Lyric baseline is stored Y-up from the system top; reflect back for the
        // still-device extender layout.
        double y = -current.YUp + _params.ExtenderYOffset;

        if (crossesSystem)
        {
            // A broken extender's pieces each sit on their OWN system's lyric
            // row: the stub before the next syllable takes THAT system's
            // baseline, not the first's (it used to draw both segments at the
            // first system's Y — the second landed over the first system).
            // LILYPOND-REF: lily/lyric-extender.cc:98-107 print — each broken
            //   piece runs to its own bound within its own system.
            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Extender,
                ImmutableArray<HyphenDash>.Empty,
                ExtenderStartX: startX,
                ExtenderEndX: endX,
                ExtenderY: y,
                CrossesSystemBreak: true,
                FirstSegmentEndX: systemEndX - 0.5,
                SecondSegmentStartX: nextSystemStartX + 0.5,
                NextLyricIndex: nextIndex,
                SecondSegmentY: -next.YUp + _params.ExtenderYOffset
            );
        }

        double length = endX - startX;
        if (length < _params.MinExtenderLength)
            return null;

        return new LyricHyphenLayout(
            index,
            LyricConnectorType.Extender,
            ImmutableArray<HyphenDash>.Empty,
            ExtenderStartX: startX,
            ExtenderEndX: endX,
            ExtenderY: y
        );
    }

    /// <summary>
    /// Ink RIGHT edge (absolute X) of the LAST note the syllable's melisma markers
    /// consumed (LyricItem.MelismaEndMeasureIndex/-Timing), or null when unknown —
    /// no markers recorded, no score measures (unit tests without a score), or the
    /// note's measure is not in the laid-out systems.
    /// </summary>
    private static double? HeldEndInkRight(
        LyricItem lyric,
        IReadOnlyDictionary<int, ImmutableArray<Measure>>? measuresByStaff,
        IReadOnlyList<SystemLayout> systems)
    {
        if (lyric.MelismaEndMeasureIndex < 0
            || measuresByStaff == null
            || !measuresByStaff.TryGetValue(lyric.StaffIndex, out var measures)
            || lyric.MelismaEndMeasureIndex >= measures.Length)
            return null;

        MusicItem? held = null;
        var onset = Fraction.Zero;
        foreach (var it in measures[lyric.MelismaEndMeasureIndex].Items)
        {
            if (onset == lyric.MelismaEndTiming) { held = it; break; }
            onset += it.Duration;
        }
        if (held == null)
            return null;

        foreach (var system in systems)
            foreach (var m in system.Measures)
                if (m.MeasureIndex == lyric.MelismaEndMeasureIndex)
                    return m.X + m.GetXForTiming(lyric.MelismaEndTiming)
                        + GlyphMetrics.GetNoteheadBBox(GlyphMetrics.NoteValueOf(held)).Right;
        return null;
    }

}
