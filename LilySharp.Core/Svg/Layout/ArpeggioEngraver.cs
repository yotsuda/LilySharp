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
/// Layout for a single arpeggio marking.
/// All coordinates are in staff spaces.
/// </summary>
public readonly record struct ArpeggioLayout(
    double X,
    double TopY,
    double BottomY,
    int SourcePosition,
    int SourceIndex = -1,    // F3/B: index into score.Arpeggios (data-pos resolved at render)
    int MeasureIndex = -1,   // page membership for multi-page rendering (-1 = draw on every page)
    int StaffIndex = -1,     // owning staff (ossia shrink); -1 = unknown/test construction
    bool Bracket = false);   // non-arpeggiate: straight bracket, not a wave

/// <summary>
/// Calculates arpeggio layouts from detected arpeggio items.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/arpeggio.cc, scm/define-grobs.scm:201-224
/// Parameters: padding=0.5, direction=LEFT, protrusion=0.4
/// The arpeggio is a wavy vertical line placed to the left of a chord.
/// </remarks>
internal static class ArpeggioEngraver
{
    // LILYPOND-REF: scm/define-grobs.scm:210 Arpeggio (padding . 0.5) — this is the
    // gap between the arpeggio's RIGHT edge and the note column's LEFT edge
    // (side-position-interface with side-axis = X, direction = LEFT), NOT an offset
    // from the notehead center.
    internal const double Padding = 0.5;

    // Half-extent of the wavy line either side of its center; matches the
    // amplitude the renderer (SharedRenderer.DrawArpeggios) draws with, so the
    // wave's right edge lands exactly `Padding` left of the noteheads.
    internal const double WaveAmplitude = 0.2;

    // Vertical overhang past the outer noteheads. LILYPOND-REF: define-grobs.scm:212.
    internal const double Protrusion = 0.4;

    /// <summary>
    /// Calculates layout positions for all arpeggio items.
    /// </summary>
    public static ImmutableArray<ArpeggioLayout> Calculate(
        ImmutableArray<ArpeggioItem> arpeggios,
        ImmutableArray<SystemLayout> systems,
        ImmutableArray<MeasureLayout> measureLayouts,
        double staffHeight,
        ImmutableArray<Measure> measures = default,
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff = null,
        Func<int, int, double>? staffYAt = null)
    {
        if (arpeggios.IsDefaultOrEmpty || arpeggios.Length == 0)
            return ImmutableArray<ArpeggioLayout>.Empty;

        var measureMap = new Dictionary<int, (SystemLayout system, MeasureLayout measure)>();
        foreach (var system in systems)
        {
            foreach (var ml in system.Measures)
            {
                measureMap[ml.MeasureIndex] = (system, ml);
            }
        }

        var layouts = new List<ArpeggioLayout>();

        for (int ai = 0; ai < arpeggios.Length; ai++)
        {
            var arp = arpeggios[ai];
            if (!measureMap.TryGetValue(arp.MeasureIndex, out var info))
                continue;

            var (system, measure) = info;

            // Resolve this arpeggio's OWN staff (multi-staff): its measures (for
            // the item X) and the staff's vertical offset within the system.
            var arpMeasures = LayoutUtilities.ResolveStaffMeasures(measuresByStaff, arp.StaffIndex, measures);
            double staffOffset = staffYAt?.Invoke(arp.MeasureIndex, arp.StaffIndex) ?? 0;

            // Get X position of the chord item, then place arpeggio to the left
            // LILYPOND-REF: scm/define-grobs.scm:206 (direction . ,LEFT)
            double itemX = measure.X + LayoutUtilities.GetItemXOffset(
                arpMeasures, arp.MeasureIndex, arp.ItemIndex, measure);

            // The wavy line must clear the LEFT edge of the noteheads, not the
            // notehead center: place its right edge `Padding` left of the head's
            // left edge. itemX is the notehead center, so subtract the head's
            // half-width before the padding and the wave half-extent.
            // LILYPOND-REF: scm/define-grobs.scm Arpeggio side-position (LEFT, X).
            double noteheadCenterX = 0.65;
            // Most-negative within-chord head displacement. A head reversed to the
            // LEFT of the stem (a second in a stem-down chord) extends the column's
            // left ink past the un-displaced column, so the arpeggio must clear
            // THAT head, not the column. LILYPOND-REF: lily/stem.cc:606-760
            // calc_positioning_done (reversed heads); the arpeggio's side-position
            // (LEFT) clears the real head extents. Mirrors SpacingRules' left-extent.
            double minHeadOffset = 0;
            if (!arpMeasures.IsDefaultOrEmpty
                && arp.MeasureIndex < arpMeasures.Length
                && arp.ItemIndex < arpMeasures[arp.MeasureIndex].Items.Length
                && arpMeasures[arp.MeasureIndex].Items[arp.ItemIndex] is ChordItem arpChord)
            {
                int nv = arpChord.BaseDuration.Denominator <= 1 ? 1
                       : arpChord.BaseDuration.Denominator <= 2 ? 2 : 4;
                noteheadCenterX = GlyphMetrics.GetNoteheadBBox(nv).CenterX;
                foreach (var off in ChordHeadPositioning.CalculateOffsets(
                             arpChord.Notes, arpChord.StemUp, nv))
                    minHeadOffset = Math.Min(minHeadOffset, off);
            }
            double arpeggioX = itemX + minHeadOffset - noteheadCenterX - Padding - WaveAmplitude;

            // Calculate Y positions from staff positions. The arpeggio's Y is
            // absolute (system.Y based), so add the staff's within-system offset
            // so it lands over its OWN staff, not the first.
            double staffMiddleY = system.Y + staffOffset + staffHeight / 2;
            double topY = StaffFrame.PositionToDevice(arp.MaxStaffPosition, staffMiddleY);
            double bottomY = StaffFrame.PositionToDevice(arp.MinStaffPosition, staffMiddleY);

            // LILYPOND-REF: scm/define-grobs.scm:212 (protrusion . 0.4)
            topY -= Protrusion;
            bottomY += Protrusion;

            layouts.Add(new ArpeggioLayout(
                X: arpeggioX,
                TopY: topY,
                BottomY: bottomY,
                SourcePosition: arp.SourcePosition,
                SourceIndex: ai,
                MeasureIndex: arp.MeasureIndex,
                StaffIndex: arp.StaffIndex,
                Bracket: arp.Bracket));
        }

        return layouts.ToImmutableArray();
    }
}
