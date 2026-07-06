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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Parameters for lyric hyphen and extender layout.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/lyric-hyphen.cc:20-50 default parameters
/// LILYPOND-REF: scm/define-grobs.scm:3080-3120 LyricHyphen grob
/// </remarks>
public sealed record LyricHyphenParameters
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
public sealed record LyricHyphenLayout(
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
    double SecondSegmentStartX = 0
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
public sealed class LyricHyphenEngraver
{
    private readonly LyricHyphenParameters _params;

    public LyricHyphenEngraver(LyricHyphenParameters? parameters = null)
    {
        _params = parameters ?? LyricHyphenParameters.Default;
    }

    /// <summary>
    /// Calculate hyphen and extender layouts for all lyrics.
    /// </summary>
    public ImmutableArray<LyricHyphenLayout> CalculateLayouts(
        IReadOnlyList<LyricLayout> lyricLayouts,
        IReadOnlyList<SystemLayout> systems)
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

            // Find the next lyric in the same verse
            LyricLayout? next = null;
            for (int j = i + 1; j < lyricLayouts.Count; j++)
            {
                if (lyricLayouts[j].Item.VerseNumber == current.Item.VerseNumber)
                {
                    next = lyricLayouts[j];
                    break;
                }
            }

            if (next == null)
                continue;

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
                LyricConnectorType.Hyphen => CalculateHyphenLayout(i, current, next, crossesSystem, systemEndX, nextSystemStartX),
                LyricConnectorType.Extender => CalculateExtenderLayout(i, current, next, crossesSystem, systemEndX, nextSystemStartX),
                _ => null
            };

            if (layout != null)
                layouts.Add(layout);
        }

        return layouts.ToImmutableArray();
    }

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
        bool crossesSystem,
        double systemEndX,
        double nextSystemStartX)
    {
        double startX = current.X + current.Width / 2 + _params.HyphenPadding;
        double endX = next.X - next.Width / 2 - _params.HyphenPadding;

        if (crossesSystem)
        {
            // Hyphen at end of current system, hyphen at start of next system
            var dashes = ImmutableArray.Create(
                new HyphenDash(startX, systemEndX - 0.5, current.Y + _params.HyphenYOffset),
                new HyphenDash(nextSystemStartX + 0.5, endX, next.Y + _params.HyphenYOffset)
            );

            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Hyphen,
                dashes,
                CrossesSystemBreak: true
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
                    current.Y + _params.HyphenYOffset)));
        }

        double y = current.Y + _params.HyphenYOffset;
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
        bool crossesSystem,
        double systemEndX,
        double nextSystemStartX)
    {
        double startX = current.X + current.Width / 2 + _params.ExtenderPadding;
        double endX = next.X - next.Width / 2 - _params.ExtenderPadding;
        double y = current.Y + _params.ExtenderYOffset;

        if (crossesSystem)
        {
            return new LyricHyphenLayout(
                index,
                LyricConnectorType.Extender,
                ImmutableArray<HyphenDash>.Empty,
                ExtenderStartX: startX,
                ExtenderEndX: endX,
                ExtenderY: y,
                CrossesSystemBreak: true,
                FirstSegmentEndX: systemEndX - 0.5,
                SecondSegmentStartX: nextSystemStartX + 0.5
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
    /// Get the configured parameters.
    /// </summary>
    public LyricHyphenParameters Parameters => _params;
}
