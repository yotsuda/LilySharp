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
    /// <summary>Minimum length of a single hyphen dash (in staff spaces).</summary>
    public double MinDashLength { get; init; } = 0.4;

    /// <summary>Maximum length before adding additional hyphens (in staff spaces).</summary>
    public double MaxDashLength { get; init; } = 3.0;

    /// <summary>Dash thickness for hyphen (in staff spaces).</summary>
    public double DashThickness { get; init; } = 0.12;

    /// <summary>Padding between syllable edge and hyphen start (in staff spaces).</summary>
    public double HyphenPadding { get; init; } = 0.3;

    /// <summary>Minimum gap required to draw a hyphen (in staff spaces).</summary>
    public double MinGapForHyphen { get; init; } = 0.8;

    /// <summary>Vertical offset from text baseline for hyphen (in staff spaces).</summary>
    /// <remarks>LILYPOND-REF: lily/lyric-hyphen.cc:67 — height = 0.5 ABOVE the
    /// baseline (a hyphen sits at mid x-height, like the text glyph would);
    /// device Y is down-positive, hence negative. The old +0.4 drew the dash
    /// BELOW the baseline — it read as an underscore.</remarks>
    public double HyphenYOffset { get; init; } = -0.5;

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
    double Y
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

            if (measureToSystem.TryGetValue(current.Item.MeasureIndex, out var currentSystem) &&
                measureToSystem.TryGetValue(next.Item.MeasureIndex, out var nextSystem))
            {
                crossesSystem = currentSystem.systemIndex != nextSystem.systemIndex;
                if (crossesSystem)
                {
                    systemEndX = currentSystem.systemEndX;
                    nextSystemStartX = nextSystem.systemStartX;
                }
            }

            var layout = current.Item.ConnectorType switch
            {
                LyricConnectorType.Hyphen => CalculateHyphenLayout(i, current, next, nextIndex, crossesSystem, systemEndX, nextSystemStartX),
                LyricConnectorType.Extender => CalculateExtenderLayout(i, current, next, nextIndex, crossesSystem, systemEndX, nextSystemStartX),
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
    /// Calculate hyphen layout with support for multiple dashes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-hyphen.cc:80-120
    ///
    /// For wide gaps, LilyPond distributes multiple hyphens evenly.
    /// </remarks>
    private LyricHyphenLayout? CalculateHyphenLayout(
        int index,
        LyricLayout current,
        LyricLayout next,
        int nextIndex,
        bool crossesSystem,
        double systemEndX,
        double nextSystemStartX)
    {
        double startX = current.X + current.Width / 2 + _params.HyphenPadding;
        double endX = next.X - next.Width / 2 - _params.HyphenPadding;

        // Lyric baselines are stored Y-up from the system top; the hyphen layout is
        // still system-relative device, so reflect back (= -YUp) for the dash Y.
        double currentBaselineY = -current.YUp;
        double nextBaselineY = -next.YUp;

        if (crossesSystem)
        {
            // Hyphen at end of current system, hyphen at start of next system.
            // Each dash's Y is relative to its OWN system (the second uses the
            // next syllable's baseline); the draw resolves the second dash
            // against the next syllable's system via NextLyricIndex.
            var dashes = ImmutableArray.Create(
                new HyphenDash(startX, systemEndX - 0.5, currentBaselineY + _params.HyphenYOffset),
                new HyphenDash(nextSystemStartX + 0.5, endX, nextBaselineY + _params.HyphenYOffset)
            );

            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Hyphen,
                dashes,
                CrossesSystemBreak: true,
                NextLyricIndex: nextIndex
            );
        }

        // Tight gap: LP shrinks the dash to max(gap - 2*padding, minimum 0.3)
        // and only lets the hyphen DISAPPEAR when there is truly no room
        // (mid-line; the cross-system case above always keeps its dashes).
        // LILYPOND-REF: lily/lyric-hyphen.cc:107-121.
        double gap = endX - startX;
        if (gap < _params.MinGapForHyphen)
        {
            double squeezed = Math.Max(gap - 2 * _params.HyphenPadding, 0.3);
            if (gap <= 0)
                return null;
            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Hyphen,
                ImmutableArray.Create(new HyphenDash(
                    (startX + endX) / 2 - squeezed / 2,
                    (startX + endX) / 2 + squeezed / 2,
                    currentBaselineY + _params.HyphenYOffset)));
        }

        double y = currentBaselineY + _params.HyphenYOffset;
        var dashList = new List<HyphenDash>();

        // Calculate number of hyphens needed
        int numHyphens = 1;
        if (gap > _params.MaxDashLength)
        {
            numHyphens = (int)Math.Ceiling(gap / _params.MaxDashLength);
        }

        if (numHyphens == 1)
        {
            // Single hyphen centered in gap
            double dashLength = Math.Min(gap * 0.6, _params.MinDashLength * 2);
            double center = (startX + endX) / 2;
            dashList.Add(new HyphenDash(center - dashLength / 2, center + dashLength / 2, y));
        }
        else
        {
            // Multiple hyphens evenly distributed
            double spacing = gap / (numHyphens + 1);
            double dashLength = Math.Min(spacing * 0.5, _params.MinDashLength * 2);

            for (int i = 1; i <= numHyphens; i++)
            {
                double center = startX + spacing * i;
                dashList.Add(new HyphenDash(center - dashLength / 2, center + dashLength / 2, y));
            }
        }

        return new LyricHyphenLayout(
            index,
            LyricConnectorType.Hyphen,
            dashList.ToImmutableArray()
        );
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
        double nextSystemStartX)
    {
        double startX = current.X + current.Width / 2 + _params.ExtenderPadding;
        double endX = next.X - next.Width / 2 - _params.ExtenderPadding;
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

}
