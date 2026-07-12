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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;

namespace LilySharp.Core.Rendering;

internal static partial class SharedRenderer
{
    // ---------- Ties & slurs ----------

    private static void DrawTies(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        foreach (var tie in layout.TieLayouts)
        {
            if (!sysY.ContainsKey(tie.Tie.StartMeasureIndex))
                continue; // not on this page (geometry is page-local)
            DrawBow(tie.StartX, tie.StartY, tie.EndX, tie.EndY,
                tie.Control1, tie.Control2, tie.CurveUp,
                EngravingDefaults.TieMidThickness,
                tie.StaffIndex, tie.Tie.StartMeasureIndex, os, gc);
        }
    }

    private static void DrawSlurs(ScoreLayout layout, Dictionary<int, double> sysY,
        in OssiaShrink os, IDrawingContext gc)
    {
        foreach (var slur in layout.SlurLayouts)
        {
            if (!sysY.ContainsKey(slur.Slur.StartMeasureIndex))
                continue; // not on this page (geometry is page-local)
            DrawBow(slur.StartX, slur.StartY, slur.EndX, slur.EndY,
                slur.Control1, slur.Control2, slur.CurveUp,
                EngravingDefaults.SlurMidThickness,
                slur.StaffIndex, slur.Slur.StartMeasureIndex, os, gc);
        }
    }

    /// <summary>
    /// Draws a tie/slur bow, shrinking it around its OSSIA staff's frame when
    /// it belongs to one: Y contracts toward the staff top by the ossia scale
    /// and the mid-thickness scales too, while X stays on the shared spacing
    /// columns — the same affine the ossia staff pass and beam pass apply.
    /// Sound because the bow's endpoints/controls are note-anchored and note
    /// offsets from the staff frame are linear in staff spaces (LP: every grob
    /// of a magnified staff scales with it).
    /// </summary>
    private static void DrawBow(
        double startX, double startY, double endX, double endY,
        (double X, double Y) c1, (double X, double Y) c2,
        bool curveUp, double midThickness,
        int staffIndex, int startMeasureIndex,
        in OssiaShrink os, IDrawingContext gc)
    {
        startY = os.Y(startY, staffIndex, startMeasureIndex);
        endY = os.Y(endY, staffIndex, startMeasureIndex);
        c1 = (c1.X, os.Y(c1.Y, staffIndex, startMeasureIndex));
        c2 = (c2.X, os.Y(c2.Y, staffIndex, startMeasureIndex));
        midThickness = os.Size(midThickness, staffIndex);
        DrawCurve(startX, startY, endX, endY, c1, c2, curveUp, midThickness, gc);
    }

    /// <summary>
    /// Draws a tapered cubic Bézier "bow" (used for both ties and slurs) by
    /// emitting an outer curve from <c>start → c1 c2 → end</c> and an inner
    /// curve back, offset toward the curve interior for the LP-style thicker
    /// middle. A thin round-cap stroke rounds the tapered ends so they read as
    /// rounded, not sharp points — matching LilyPond's stroked slur/tie stencil.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/tie.cc, lily/slur.cc — Bezier bow rendering; the
    ///   stencil is filled AND stroked with a round-cap line-thickness pen.
    /// </remarks>
    private static void DrawCurve(
        double startX, double startY, double endX, double endY,
        (double X, double Y) c1, (double X, double Y) c2,
        bool curveUp, double midThickness, IDrawingContext gc)
    {
        // Port of LilyPond's slur/tie stencil (lily/lookup.cc Lookup::slur): the
        // computed control points (c1,c2) are the CENTRELINE of a "bezier sandwich" —
        // two Béziers that share the endpoints, their middle control points offset by
        // ±half the curve thickness PERPENDICULAR TO THE CHORD (control_[3] -
        // control_[0]). Splitting the thickness symmetrically about the reference
        // curve — rather than growing one edge off it — makes both ends taper to clean
        // points and keeps the ribbon thickest at the middle even on a slanted bow (a
        // grace slur runs diagonally down to the main note). `curveUp` no longer enters
        // the thickness: LP's ±perp is symmetric, so the bow direction only shapes the
        // control points, which the caller already set.
        // LILYPOND-REF: lily/lookup.cc Lookup::slur —
        //   perp = 0.5*curvethick*Offset(-dir[Y],dir[X]);
        //   back.control_[1,2] += perp; curve.control_[1,2] -= perp; bezier_sandwich(…).
        _ = curveUp;
        double dx = endX - startX, dy = endY - startY;
        double len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
        double half = 0.5 * midThickness * 0.9; // keep the previous total ribbon width
        double perpX = -dy / len * half, perpY = dx / len * half;
        var c1a = (X: c1.X + perpX, Y: c1.Y + perpY);
        var c2a = (X: c2.X + perpX, Y: c2.Y + perpY);
        var c1b = (X: c1.X - perpX, Y: c1.Y - perpY);
        var c2b = (X: c2.X - perpX, Y: c2.Y - perpY);
        gc.DrawClosedBezier(
            (startX, startY), c1a, c2a,
            (endX, endY), c2b, c1b,
            Color.Black, EngravingDefaults.BowEndRounding);
    }

    /// <summary>
    /// The contiguous measure range [From..To] of this system where the ossia
    /// has notes/chords, or (-1, -1) when it only rests here. The ossia prints
    /// nothing outside this range — in LP the ossia context exists only for
    /// its fragment (NR "Ossia staves").
    /// </summary>
    private static (int From, int To) OssiaFragment(Staff staff, SystemLayout system)
    {
        int from = -1, to = -1;
        var measures = staff.PrimaryVoice.Measures;
        foreach (var ml in system.Measures)
        {
            if (ml.MeasureIndex >= measures.Length)
                continue;
            bool hasNotes = false;
            foreach (var item in measures[ml.MeasureIndex].Items)
            {
                if (item is NoteItem or ChordItem)
                {
                    hasNotes = true;
                    break;
                }
            }
            if (!hasNotes)
                continue;
            if (from < 0) from = ml.MeasureIndex;
            to = ml.MeasureIndex;
        }
        return (from, to);
    }

    /// <summary>True when the ossia already printed a fragment in an EARLIER
    /// system: LP's ossia convention sets firstClef = ##f, and the clef
    /// engraver creates a clef only when a previous clef exists or firstClef
    /// is true (lily/clef-engraver.cc) — so the FIRST fragment opens bare and
    /// later fragments carry the clef.</summary>
    private static bool OssiaAppearedBefore(
        ScoreLayout layout, Staff staff, SystemLayout system, int staffIndex)
    {
        foreach (var sys in layout.AllSystems)
        {
            if (sys.SystemIndex >= system.SystemIndex)
                break;
            if (StaffPresentInSystem(sys, staffIndex) && OssiaFragment(staff, sys).From >= 0)
                return true;
        }
        return false;
    }

    /// <summary>True when the staff is VISIBLE in this system. A hara-kiri
    /// staff stays in the system's staff table but with IsHidden=true and its
    /// Y collapsed onto a neighbour — drawing by that Y would print the hidden
    /// staff's clef/rests on top of a visible staff. Single-staff layouts
    /// carry no table (empty StaffGroups) and are always visible.</summary>
    private static bool StaffPresentInSystem(SystemLayout system, int staffIndex)
    {
        if (system.StaffGroups.IsDefaultOrEmpty)
            return true;
        foreach (var g in system.StaffGroups)
            foreach (var s in g.Staves)
                if (s.StaffIndex == staffIndex)
                    return !s.IsHidden;
        return true;
    }

    // ---------- Helpers for system-Y lookup ----------

    // Page-scoped on purpose: the map doubles as the page-membership test for
    // every overlay drawer (missing key = the measure is on another page).
    private static Dictionary<int, double> BuildMeasureToSystemY(PageLayout page)
    {
        var map = new Dictionary<int, double>();
        foreach (var system in page.Systems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system.Y;
        return map;
    }

    // Measure → its SystemLayout, for drawers that need per-staff Y resolution
    // inside the system (the ossia bow shrink).
    private static Dictionary<int, SystemLayout> BuildMeasureToSystem(PageLayout page)
    {
        var map = new Dictionary<int, SystemLayout>();
        foreach (var system in page.Systems)
            foreach (var ml in system.Measures)
                map[ml.MeasureIndex] = system;
        return map;
    }

    /// <summary>
    /// Shrinks overlay geometry that belongs to an OSSIA staff: absolute Y
    /// contracts toward the staff's top by the ossia scale and sizes/offsets
    /// multiply by it, while X stays on the shared spacing columns — the same
    /// affine the ossia staff pass and beam pass apply (LP: every grob of a
    /// magnified staff scales with it, ly/music-functions-init.ly
    /// magnifyStaff). Identity for normal staves and for layouts without
    /// staff identity (StaffIndex &lt; 0). Sound for note-anchored overlays
    /// because their offsets from the staff frame are linear in staff spaces.
    /// </summary>
    private readonly struct OssiaShrink
    {
        private readonly HashSet<int> _ossiaStaves;
        private readonly Dictionary<int, SystemLayout> _systems;

        public OssiaShrink(HashSet<int> ossiaStaves, Dictionary<int, SystemLayout> systems)
        {
            _ossiaStaves = ossiaStaves;
            _systems = systems;
        }

        public bool Contains(int staffIndex)
            => staffIndex >= 0 && _ossiaStaves.Contains(staffIndex);

        /// <summary>Absolute-Y affine around the staff top; identity off-ossia
        /// (or when the measure has no system on this page).</summary>
        public double Y(double y, int staffIndex, int measureIndex)
        {
            if (!Contains(staffIndex) || !_systems.TryGetValue(measureIndex, out var system))
                return y;
            double top = LayoutUtilities.FindStaffYInSystem(system, staffIndex);
            return top + (y - top) * OssiaScale;
        }

        /// <summary>Scales a size/offset/amplitude; identity off-ossia.</summary>
        public double Size(double v, int staffIndex)
            => Contains(staffIndex) ? v * OssiaScale : v;
    }

    // ---------- F3/B: data-pos resolution ----------

    /// <summary>
    /// Re-derives each annotation layout's data-pos source offset from the LIVE score,
    /// via the <c>SourceIndex</c> the layout carries (an index into the matching score
    /// side-table). This keeps the cached <see cref="ScoreLayout"/> position-independent:
    /// source offsets are NOT baked into the layout geometry but resolved here at render
    /// time. For a normal render this reproduces the same value (snapshot-identical); for
    /// whole-layout reuse (a content-unchanged edit) it yields the edited score's fresh
    /// positions, so editor click-to-source stays correct.
    /// </summary>
    private static ScoreLayout ResolveDataPos(ScoreLayout layout, MultiStaffScore score)
    {
        // Note-hosted annotations (glissando, …) carry a (staff, measure, item) locator
        // instead of a side-table index; their data-pos is the HOST NOTE's source offset.
        // Build the staff -> measures map once so the resolver can re-derive it. Lazy:
        // only built when such an annotation is present.
        var noteHosts = (layout.GlissandoLayouts.IsDefaultOrEmpty
                         && layout.FingeringLayouts.IsDefaultOrEmpty)
            ? null : BuildStaffVoices(score);
        return layout with
        {
            DynamicLayouts = ResolveArr(layout.DynamicLayouts, score.Dynamics,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ArticulationLayouts = ResolveArr(layout.ArticulationLayouts, score.Articulations,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ArpeggioLayouts = ResolveArr(layout.ArpeggioLayouts, score.Arpeggios,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            CustomTextLayouts = ResolveArr(layout.CustomTextLayouts, score.CustomTexts,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            FiguredBassLayouts = ResolveArr(layout.FiguredBassLayouts, score.FiguredBasses,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            VoltaBracketLayouts = ResolveArr(layout.VoltaBracketLayouts, score.VoltaBrackets,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TupletBracketLayouts = ResolveArr(layout.TupletBracketLayouts, score.TupletBrackets,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            PercentRepeatLayouts = ResolveArr(layout.PercentRepeatLayouts, score.PercentRepeats,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            GraceNoteLayouts = ResolveArr(layout.GraceNoteLayouts, score.GraceNotes,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            ChordNameLayouts = ResolveArr(layout.ChordNameLayouts, score.ChordNames,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TrillSpannerLayouts = ResolveArr(layout.TrillSpannerLayouts, score.TrillSpanners,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            // MusicMarks (incl. section labels + tempo) aren't a flat score side-table;
            // their SourceIndex points into the reconstructed BuildAllMarks() list. Rebuild
            // it the same way Calculate does so each layout re-derives its data-pos. Tempo
            // marks carry SourcePosition 0 (no data-pos emitted), section labels resolve from
            // the (re-collected) measures, explicit marks from score.MusicMarks.
            MusicMarkLayouts = ResolveArr(layout.MusicMarkLayouts,
                MusicMarkEngraver.BuildAllMarks(score.MusicMarks,
                    score.PrimaryContentStaff.PrimaryVoice.Measures, score.Tempo,
                    score.SwingSubdivision, score.TempoText, score.TempoBeatUnit, score.TempoDots),
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            // Lyrics carry the source offset on their nested LyricItem (the renderer draws
            // data-pos from Item.SourcePosition); re-derive that from the live Lyrics table.
            LyricLayouts = ResolveArr(layout.LyricLayouts, score.Lyrics,
                static (l, it) => l with { Item = l.Item with { SourcePosition = it.SourcePosition } },
                static l => l.SourceIndex),
            // Glissando data-pos = its start note's source offset; re-read it from the live
            // score by the note locator the layout carries.
            GlissandoLayouts = ResolveNoteArr(layout.GlissandoLayouts, noteHosts,
                static l => (l.StaffIndex, l.VoiceIndex, l.MeasureIndex, l.ItemIndex),
                static (l, pos) => l with { SourcePosition = pos }),
            // Fingering data-pos = its host note/chord's source offset. Fingerings are
            // computed only for the primary voice, so they always resolve against voice 0.
            FingeringLayouts = ResolveNoteArr(layout.FingeringLayouts, noteHosts,
                static l => (l.StaffIndex, 0, l.MeasureIndex, l.ItemIndex),
                static (l, pos) => l with { SourcePosition = pos }),
            // Detected spanners (hairpin / ottava / text spanner) take their data-pos from
            // the originating cresc/ottava/rit mark in score.MusicMarks — re-derive by its index.
            HairpinLayouts = ResolveArr(layout.HairpinLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            OttavaBracketLayouts = ResolveArr(layout.OttavaBracketLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
            TextSpannerLayouts = ResolveArr(layout.TextSpannerLayouts, score.MusicMarks,
                static (l, it) => l with { SourcePosition = it.SourcePosition }, static l => l.SourceIndex),
        };
    }

    // staff index -> that staff's VOICES, the host tables the note-locator annotations
    // resolve against. Same staff-index convention as the layout build path
    // (LayoutEngine's EnumerateStaves loop). A note-hosted layout carries the voice it
    // lives in, so a second voice's glissando resolves against its own voice's measures.
    private static System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>
        BuildStaffVoices(MultiStaffScore score)
    {
        var map = new System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>();
        foreach (var (_, staff, staffIndex) in score.EnumerateStaves())
            map[staffIndex] = staff.Voices;
        return map;
    }

    // Refreshes each note-hosted layout's data-pos from its HOST ITEM's source offset,
    // located by the (staff, measure, item) triple it carries. The host is read as the base
    // MusicItem, so it covers both a single note (glissando, melodic fingering) and a chord
    // (chord fingering) — both expose SourcePosition. A locator that doesn't resolve (out of
    // range, or staffIndex -1 from the single-staff Layout(Score) path) is left as-is — its
    // baked value is already correct for a normal full render; only whole-layout reuse needs
    // the re-derivation, and that path always carries a real staff index.
    private static ImmutableArray<T> ResolveNoteArr<T>(
        ImmutableArray<T> layouts,
        System.Collections.Generic.Dictionary<int, ImmutableArray<Voice>>? staffVoices,
        System.Func<T, (int Staff, int Voice, int Measure, int Item)> locator,
        System.Func<T, int, T> resolve)
    {
        if (layouts.IsDefaultOrEmpty || staffVoices == null)
            return layouts;
        var b = layouts.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            var (s, v, m, it) = locator(b[i]);
            if (staffVoices.TryGetValue(s, out var voices)
                && (uint)v < (uint)voices.Length
                && (uint)m < (uint)voices[v].Measures.Length)
            {
                var items = voices[v].Measures[m].Items;
                if ((uint)it < (uint)items.Length)
                    b[i] = resolve(b[i], items[it].SourcePosition);
            }
        }
        return b.MoveToImmutable();
    }

    // Refreshes each layout's resolved field from the side-table item it references
    // (by SourceIndex). Out-of-range indices are left as-is (defensive).
    private static ImmutableArray<T> ResolveArr<T, TItem>(
        ImmutableArray<T> layouts, ImmutableArray<TItem> items,
        System.Func<T, TItem, T> resolve, System.Func<T, int> sourceIndex)
    {
        if (layouts.IsDefaultOrEmpty)
            return layouts;
        var b = layouts.ToBuilder();
        for (int i = 0; i < b.Count; i++)
        {
            int si = sourceIndex(b[i]);
            if ((uint)si < (uint)items.Length)
                b[i] = resolve(b[i], items[si]);
        }
        return b.MoveToImmutable();
    }

}
