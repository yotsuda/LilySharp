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
/// Common utility methods for layout calculations.
/// </summary>
internal static class LayoutUtilities
{
    /// <summary>
    /// How far right of a note column its STEM stands, given the stem's direction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:1050-1085 internal_calc_stem_offset_from_head — the stem is
    ///   offset from its support head by the font's attachment coordinate, then pulled back
    ///   half a stem thickness so the stem's EDGE meets the head.
    /// LILYPOND-REF: scm/define-grobs.scm:2608 ly:note-head::calc-stem-attachment (NoteHead's
    ///   stem-attachment) — the coordinate itself, so an up stem stands at the notehead's
    ///   right edge and a down stem at its left. A stem's x is NOT its column's x.
    /// <para>
    /// ⚠️ THE ATTACHMENT POINT, NOT THE ADVANCE — they differ by 0.000200, and this line read
    /// the advance until 2026-08-02. LilyPond's arithmetic is
    /// <c>head-&gt;extent(X).linear_combination(attach)</c> over an <c>attach</c> that
    /// lily/note-head.cc:164-196 get_stem_attachment NORMALISES out of the same box
    /// (<c>2·(wx − centre)/length</c>), so the round trip is the identity and what is left is
    /// the font's own attachment coordinate: <see cref="GlyphMetrics.NoteheadBlackStemAttachment"/>
    /// = 1.304200, where the hmtx advance is 1.304000. MEASURED (probes/beam-stem-x.ly) an up
    /// stem's X-offset is 1.2392 = 1.304200 − 0.065, and NOT 1.2390.
    /// </para>
    /// <para>
    /// ⚠️ AND IT IS PER HEAD SHAPE — <paramref name="noteValue"/> is what picks it
    /// (<see cref="GlyphMetrics.GetNoteheadStemAttachment(int)"/>), because the LilyPond
    /// property above is a callback answered from the head's own glyph. This read the BLACK
    /// head's 1.304200 for every head until 2026-08-03, so a half note's up stem stood
    /// 1.377400 − 1.304200 = 0.073200 left of LilyPond's; MEASURED as exactly that by ledger
    /// <c>stem.up.right-edge.half-head</c> against its control
    /// <c>stem.up.right-edge.black-head</c>, one bar apart at one pitch.
    /// ⚠️ A DOWN stem does not move, and that is a fact about the FONT rather than a
    /// simplification: every Emmentaler head box starts at x 0.000000
    /// (<see cref="GlyphMetrics.NoteheadHalf"/> and <see cref="GlyphMetrics.NoteheadBlack"/>
    /// are (0, 1.377400) and (0, 1.304200)), so the left edge is shape-independent and
    /// <see cref="EngravingDefaults.StemDownAttachX"/> stays a constant. It is also why each
    /// attachment X equals its own box's RIGHT edge exactly.
    /// </para>
    /// <para>
    /// ⚠️ THE one house. This offset had SEVEN spellings, and the quanter's frame claim
    /// ("a beam is scored against ink in the beam's own frame, whose x is its STEMS") is a
    /// claim that the quanter and the renderer compute it the SAME way — which nothing
    /// asserted, so a change on the renderer's side would have broken the scoring silently
    /// while every test stayed green. <c>BeamStemXMatchesTheDrawnStem</c> asserts it.
    /// </para>
    /// </remarks>
    /// <param name="noteValue">
    /// The head's own note value (1=whole, 2=half, else black), as
    /// <see cref="GlyphMetrics.NoteValueOf"/> reads it. A BEAM member is not always a black
    /// head: a two-note tremolo pair beams HALVES (<c>BeamDetector.IsBeamable</c>).
    /// </param>
    /// <param name="headScale">
    /// The head's own scale. A cue head is drawn at 0.66×, so an UP stem attaches at the
    /// scaled head's attachment point or it floats off the small head; a down stem attaches at
    /// the left edge, which does not move with the scale. At noteValue 4 and scale 1.0 this is
    /// <see cref="EngravingDefaults.StemUpAttachX"/> exactly.
    /// </param>
    public static double StemAttachX(bool up, int noteValue, double headScale = 1.0) =>
        up
            ? GlyphMetrics.GetNoteheadStemAttachment(noteValue).X * headScale
              - EngravingDefaults.StemThickness / 2
            : EngravingDefaults.StemDownAttachX;

    /// <summary>
    /// The same offset for a head read from ANOTHER FONT — a grace's, whose head comes out of
    /// a different Emmentaler design and not out of a scaled 20 (<c>GraceNoteItem.Font</c>).
    /// Null is the score's own size, i.e. the <c>headScale = 1.0</c> overload exactly.
    /// </summary>
    /// <remarks>
    /// ⚠️ The stem's THICKNESS does not come from the font and is not scaled with it:
    /// MEASURED (ledger grace.stem.thickness against grace.stem.thickness.full-size-control)
    /// LilyPond draws both at 0.13.
    /// </remarks>
    public static double StemAttachX(bool up, int noteValue, GlyphMetrics.DesignMetrics? font) =>
        up
            ? (font is { } f
                   ? GlyphMetrics.GetNoteheadStemAttachment(f, noteValue).X
                   : GlyphMetrics.GetNoteheadStemAttachment(noteValue).X)
              - EngravingDefaults.StemThickness / 2
            : EngravingDefaults.StemDownAttachX;

    /// <summary>The x a stem stands at, given its note column's x.</summary>
    /// <remarks>See <see cref="StemAttachX(bool, int, double)"/>.</remarks>
    public static double StemX(double columnX, bool up, int noteValue, double headScale = 1.0) =>
        columnX + StemAttachX(up, noteValue, headScale);

    /// <summary>The x a stem stands at, for a head read from <paramref name="font"/>.</summary>
    /// <remarks>See <see cref="StemAttachX(bool, int, GlyphMetrics.DesignMetrics)"/>.</remarks>
    public static double StemX(double columnX, bool up, int noteValue,
        GlyphMetrics.DesignMetrics? font) =>
        columnX + StemAttachX(up, noteValue, font);

    /// <summary>
    /// The x a beamed rest's INVISIBLE stem stands at: the centre of the rest glyph's own
    /// ink. A beamlet beside the rest is length-capped against this x
    /// (BeamSubdivision.CalcBeamSegments' max-proportion cap), which is how the cap
    /// resolved against the rest's LEFT edge cut a beamlet LilyPond leaves at full length.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:1093-1105 Stem::offset_callback — the "rests" branch
    /// returns <c>robust_relative_extent (rest, rest, X_AXIS).center ()</c>.
    /// </remarks>
    public static double RestStemX(double restX, int noteValue)
    {
        var box = GlyphMetrics.GetRestBBox(noteValue);
        return restX + (box.Left + box.Right) / 2.0;
    }

    /// <summary>
    /// Where a FLAG's glyph origin sits, given the x its stem stands at.
    /// </summary>
    /// <remarks>
    /// A flag hangs on the stem's RIGHT EDGE, not on its centre.
    /// LILYPOND-REF: lily/flag.cc:198-205 Flag::calc_x_offset — the grob's X-offset is
    ///   <c>stem->extent (stem, X_AXIS)[RIGHT]</c>, and lily/flag.cc:118-165 Flag::print
    ///   returns the stencil UNTRANSLATED, so the glyph lands exactly on that offset.
    /// LILYPOND-REF: lily/stem.cc:889-906 ly:stem::width (Stem::width, past its is_invisible
    ///   branch) — a stem's extent is <c>Interval (-1, 1) * thickness / 2</c>, so that RIGHT is
    ///   half a stem thickness and nothing else.
    /// <para>
    /// MEASURED, both stem directions (ledger <c>flag.x.{down,up}.origin-from-head</c>, probe
    /// flagged-stem-reach.ly scores FLXD/FLXU): LilyPond puts the flag 0.130000 right of the
    /// head on a DOWN stem — a whole thickness, because a down stem's LEFT edge sits on the
    /// head's left edge — and 1.304200 on an UP one, the head's own width, i.e. the stem's
    /// centre 1.239200 plus this term. Both points opened at −0.065000 and close here.
    /// </para>
    /// <para>
    /// ⚠️ THE RESERVATION DOES NOT MOVE WITH IT, and that is LilyPond's own shape rather than a
    /// split to be tidied away: <c>Flag::width</c> declares the stencil's extent MINUS that same
    /// <c>[RIGHT]</c>, so offset + extent puts the RESERVED ink back on the stem's centre while
    /// the DRAWN glyph stays on its right edge. ItemSkylineFactory's remarks carry the full
    /// account; they are why the reserve side is deliberately left where it is.
    /// </para>
    /// <para>
    /// ⚠️ The thickness is NOT scaled for a grace — LilyPond draws every stem 0.13 (ledger
    /// <c>grace.stem.thickness</c> against its full-size control), so a grace flag takes the
    /// same term as a full-size one.
    /// </para>
    /// </remarks>
    public static double FlagDrawX(double stemX) => stemX + EngravingDefaults.StemThickness / 2;

    /// <summary>
    /// Gets note value (1=whole, 2=half, 4=quarter, 8=eighth) from duration fraction.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:600 Stem::duration_log
    /// Duration log: 0=whole, 1=half, 2=quarter, 3=eighth, etc.
    /// Note value: 1=whole, 2=half, 4=quarter, 8=eighth, etc.
    /// </remarks>
    public static int GetNoteValueFromFraction(Fraction duration)
    {
        // duration = 1/1 for whole, 1/2 for half, 1/4 for quarter, 1/8 for eighth, etc.
        if (duration.Numerator == 0) return 4; // Default to quarter
        return (int)(duration.Denominator / duration.Numerator);
    }

    /// <summary>
    /// Calculates the flag height based on note value.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/flag.cc:80-95 Flag::internal_print
    /// Flag height increases with shorter note values (more beams/flags).
    /// </remarks>
    public static double CalculateFlagHeight(int noteValue)
    {
        double height = EngravingDefaults.FlagBaseHeight;
        if (noteValue >= 16) height += EngravingDefaults.FlagHeightIncrement;
        if (noteValue >= 32) height += EngravingDefaults.FlagHeightIncrement;
        return height;
    }

    /// <summary>
    /// The measures a per-staff annotation is positioned against: the annotation's own
    /// staff measures when a multi-staff map is supplied and contains the staff, else
    /// the fallback (the single- or primary-voice measures). Shared by the annotation
    /// engravers, which all repeated this ternary.
    /// </summary>
    public static ImmutableArray<Measure> ResolveStaffMeasures(
        Dictionary<int, ImmutableArray<Measure>>? measuresByStaff, int staffIndex,
        ImmutableArray<Measure> fallback)
        => measuresByStaff != null && measuresByStaff.TryGetValue(staffIndex, out var mm)
            ? mm : fallback;

    /// <summary>
    /// The item an annotation hangs off: its OWN voice's item at (measure, index), or null
    /// when the voice or the index is out of range. An annotation's ItemIndex counts the
    /// items of the voice it was written in, so resolving it against the staff's PRIMARY
    /// voice returns whatever note happens to share the index — a different pitch, and a
    /// different column as soon as the two voices' rhythms differ.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly:359,414-416 Script_engraver — the <c>\name Voice</c>
    ///   context consists it (and :410 the Dynamic_align_engraver), so both grobs see their
    ///   OWN context's heads: lily/script-engraver.cc:234-250 acknowledge_rhythmic_head and
    ///   lily/dynamic-align-engraver.cc:108-117 acknowledge_rhythmic_head.
    /// ONE house for both (HANDOFF §5.2.1②): the dynamics island grew this lookup first, and
    /// the scripts' identical one was missing until the placement pair
    /// <c>script.{staccato,marcato}-below.staff-to-ink-top</c> measured its price.
    /// </remarks>
    public static MusicItem? VoiceItemAt(
        ImmutableArray<Voice> voices, int voiceIndex, int measureIndex, int itemIndex)
    {
        if (voices.IsDefaultOrEmpty)
            return null;
        // ⚠️ THE CLAMP NEVER FIRES, measured: replacing it with a throw and running the whole
        // suite (4034 tests, the corpus included) reaches it zero times — an annotation is
        // stamped with the voice it was collected in, so its index is in range by
        // construction. It is kept as a clamp rather than a throw because an exception in a
        // per-keystroke preview is worse than a misplaced glyph; but a HIT IS A BUG, not an
        // absence — the honest reading of a script that lands on voice 1 with the clamp
        // firing is "the collector lost the voice", and that is what to look for.
        var voice = voices[Math.Clamp(voiceIndex, 0, voices.Length - 1)];
        if (measureIndex < 0 || measureIndex >= voice.Measures.Length)
            return null;
        var items = voice.Measures[measureIndex].Items;
        return itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] : null;
    }

    /// <summary>
    /// The measures an annotation's ItemIndex counts against: its own voice's when that
    /// voice exists, else the staff's (a single-voice staff, where the two coincide).
    /// The companion of <see cref="VoiceItemAt"/> for the callers that need the whole
    /// measure list — item X resolution walks the voice's items to reach a timing
    /// (<see cref="GetItemXOffset"/>), so it must walk the SAME list the index came from.
    /// </summary>
    public static ImmutableArray<Measure> ResolveVoiceMeasures(
        ImmutableArray<Voice> voices, int voiceIndex, ImmutableArray<Measure> fallback)
        => voices.IsDefaultOrEmpty
            ? fallback
            : voices[Math.Clamp(voiceIndex, 0, voices.Length - 1)].Measures;

    /// <summary>
    /// Builds a map from measure index to (system, measureLayout) for quick lookup.
    /// </summary>
    public static Dictionary<int, (SystemLayout System, MeasureLayout Measure)> BuildMeasureMap(
        ImmutableArray<SystemLayout> systems)
    {
        var map = new Dictionary<int, (SystemLayout, MeasureLayout)>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                map[measureLayout.MeasureIndex] = (system, measureLayout);
            }
        }
        return map;
    }

    /// <summary>
    /// Builds a map from measure index to measureLayout for quick lookup.
    /// </summary>
    public static Dictionary<int, MeasureLayout> BuildMeasureLayoutMap(
        ImmutableArray<SystemLayout> systems)
    {
        var map = new Dictionary<int, MeasureLayout>();
        foreach (var system in systems)
        {
            foreach (var measureLayout in system.Measures)
            {
                map[measureLayout.MeasureIndex] = measureLayout;
            }
        }
        return map;
    }

    /// <summary>
    /// Calculates the upward extent of a system skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:622-626
    /// MaxHeight() returns the topmost Y-up (positive for notes above the staff top).
    /// It is already the positive extent above the staff top.
    /// </remarks>
    public static double CalculateUpExtent(VerticalSkyline upSkyline)
    {
        return upSkyline.IsEmpty ? 0 : Math.Max(0, upSkyline.MaxHeight());
    }

    /// <summary>
    /// Calculates the downward extent of a system skyline.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/skyline.cc:667-680 Skyline::max_height()
    /// DOWN skyline's MaxHeight() returns the bottommost Y-up (negative below the
    /// staff top). The staff bottom line sits at Y-up = -staffHeight, so the extent
    /// below it is (-MaxHeight) - staffHeight.
    /// </remarks>
    public static double CalculateDownExtent(VerticalSkyline downSkyline, double staffHeight)
    {
        return downSkyline.IsEmpty ? 0 : Math.Max(0, -downSkyline.MaxHeight() - staffHeight);
    }

    /// <summary>
    /// Distance DOWN from the paper's top edge to the FIRST system's staff refpoint —
    /// its top staff's MIDDLE line, which is the anchor <c>top-system-spacing</c> is
    /// written against.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:441-444 — the problem opens with
    /// <c>bottom_skyline_</c> AT the top of the printable area, set header_height_ below
    /// it, so the top spring is anchored at the TOP of the header (:471-473 says so in
    /// as many words). The header therefore enters the FLOOR, not the anchor.
    /// LILYPOND-REF: lily/page-layout-problem.cc:625-633 — that floor is the ink the
    /// system carries above its refpoint plus the spec's padding, and it reaches the
    /// spring through <c>Spring::ensure_min_distance</c> (lily/spring.cc:156-159), which
    /// raises the MINIMUM and leaves the ideal alone.
    ///
    /// Two frames meet here and must not be confused (they differ by exactly halfStaff):
    /// <list type="bullet">
    /// <item>Lily#'s system origin and <paramref name="systemUpExtent"/> are the top
    /// staff's TOP LINE and the ink above it.</item>
    /// <item>LilyPond's <c>up_skyline.distance()</c> is measured from the staff REFPOINT
    /// and always contains the staff symbol itself.</item>
    /// </list>
    /// Placing the first system is done in the refpoint frame here and converted back to
    /// the origin frame ONCE, in <see cref="CalculateFirstSystemY"/>.
    /// </remarks>
    public static double CalculateFirstStaffRefpoint(
        double topMargin, double headerHeight, double systemUpExtent,
        double halfStaff, VerticalSpacingSpec topSpec)
        // LILYPOND-REF: lily/simple-spacer.cc:295-305 spring_positions — a system's
        // position is the running sum of its springs' lengths, and for the FIRST system
        // that sum is the top spring alone. At force 0 Spring::length is
        // max(min_distance_, ideal_distance_) (spring.cc:219-237), which is why a system
        // whose ink is smaller than top-system-spacing's basic-distance is not measured by
        // its ink at all.
        => topMargin + CreateTopSystemSpring(headerHeight, systemUpExtent, halfStaff, topSpec)
                       .Length(0);

    /// <summary>
    /// The spring from the top of the printable area down to the first system's staff
    /// refpoint — <c>top-system-spacing</c>, floored by the ink that system carries above
    /// its refpoint.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:511-518 — the first system's spring comes
    /// from top_system_spacing, and :625-633 floors it with the system's own ink through
    /// <c>Spring::ensure_min_distance</c>.
    /// The header is part of that FLOOR, not of the anchor: the problem is built with
    /// <c>bottom_skyline_</c> set header_height_ below the top of the printable area
    /// (:441-444), which is what the comment at :471-473 means by anchoring the spring at
    /// the top of the header.
    /// </remarks>
    public static Spring CreateTopSystemSpring(
        double headerHeight, double systemUpExtent, double halfStaff, VerticalSpacingSpec topSpec)
    {
        // Lily#'s up extent is the ink above the top staff LINE; LilyPond's up_skyline is
        // measured from the staff REFPOINT and always contains the staff symbol itself, so
        // the same quantity is halfStaff more there.
        double inkAboveRefpoint = systemUpExtent + halfStaff;
        return CreateSpring(topSpec, headerHeight + inkAboveRefpoint + topSpec.Padding);
    }

    /// <summary>
    /// Builds one of LilyPond's vertical springs from a spacing spec and the minimum
    /// distance the geometry imposes on it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1345-1358 alter_spring_from_spacing_spec —
    /// basic-distance is the IDEAL and minimum-distance the MIN, then
    /// <c>set_default_strength()</c> runs UNCONDITIONALLY (:1354), making the inverse stretch
    /// equal the ideal (spring.cc:213-216), and only then does a <c>stretchability</c> entry
    /// override it (:1356-1357). So the two branches below are LilyPond's own two, and
    /// <c>Stretchability</c> is null exactly where LilyPond's spec declares none —
    /// <c>top-system-spacing</c> and <c>markup-markup-spacing</c>
    /// (ly/paper-defaults-init.ly:76-80), <c>default-staff-staff-spacing</c>
    /// (scm/define-grobs.scm:4237-4239).
    /// ⚠️ A DECLARED 0 IS NOT THE SAME AS ABSENT, and used to be folded into it: it makes
    /// the spring rigid however large the ideal, where an absent one tracks the ideal.
    /// <c>nonstaff-nonstaff-spacing</c> declares 0 (ly/engraver-init.ly:657) and its ideal
    /// is also 0, which is the coincidence that let the two spellings pass for one.
    ///
    /// The compress strength is fixed at <c>ideal - minimum-distance</c> from the SPEC
    /// (spring.cc:205-210), because <c>ensure_min_distance</c> raises the minimum
    /// afterwards and deliberately does not restrengthen the spring (spring.cc:156-159).
    /// Passing the raised minimum here instead would quietly change every blocking force.
    /// ⚠️ <c>set_default_compress_strength</c> is NOT overridable from a spec at all — no
    /// spacing spec has a compress member — so it is computed here in every case.
    /// </remarks>
    public static Spring CreateSpring(VerticalSpacingSpec spec, double ensureMinDistance)
    {
        double inverseStretch = spec.Stretchability ?? spec.BasicDistance;
        double inverseCompress = Math.Max(0, spec.BasicDistance - spec.MinimumDistance);
        double minDistance = Math.Max(spec.MinimumDistance, ensureMinDistance);
        return new Spring(spec.BasicDistance, minDistance, inverseStretch, inverseCompress);
    }

    /// <summary>
    /// Distance DOWN from the paper's top edge to the FIRST system's ORIGIN (its top
    /// staff's TOP LINE) — the frame <see cref="SystemLayout.Y"/> is stacked in.
    /// </summary>
    /// <remarks>
    /// The sole seam between the refpoint frame LilyPond's page spacing is written in and
    /// the origin frame Lily# stacks systems in; every caller placing a first system goes
    /// through here so the halfStaff conversion exists in exactly one place.
    /// </remarks>
    /// <param name="halfStaff">
    /// The first SPACEABLE staff's OWN half span, which is what converts an EXTENT (measured
    /// from that staff) into ink above its refpoint.
    /// </param>
    /// <param name="originToRefpoint">
    /// The distance from the system's ORIGIN down to that same refpoint, which is what
    /// converts the answer back into the frame systems are stacked in. The two differ exactly
    /// when something stands above the staff inside the system — a lead sheet's chord row —
    /// and they were one parameter until 2026-07-28, which is why such a row was counted
    /// twice the moment either stopped being a nominal 2.000000.
    /// </param>
    public static double CalculateFirstSystemY(
        double topMargin, double headerHeight, double systemUpExtent,
        double halfStaff, double originToRefpoint, VerticalSpacingSpec topSpec)
        => CalculateFirstStaffRefpoint(topMargin, headerHeight, systemUpExtent, halfStaff, topSpec)
           - originToRefpoint;

    /// <summary>
    /// Calculates the actual header height based on title and composer presence.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:435
    /// header_height_ = head ? head->extent(Y_AXIS).length() : 0;
    ///
    /// SVG text coordinates specify the baseline, which is approximately
    /// the bottom of the text (excluding descenders). Therefore:
    /// - Title at y=MarginTop has its bottom at MarginTop
    /// - Composer follows with spacing from title baseline
    /// - headerBottom = MarginTop + (vertical extent of all header elements)
    /// </remarks>
    // Mirror of SharedRenderer.DrawHeader: the title BASELINE sits at
    // MarginTop (fs 3.49) and the composer baseline TitleFontSize below it
    // (fs 2.2). Header HEIGHT is the ink below MarginTop — the old model
    // pretended the title had no descender (and used a stale 3.0 for the
    // composer step), so a first system with no tall content of its own
    // (a lyrics/chords ROW score) started inside the title's descender ink.
    private const double HeaderTitleFontSize = 3.49;
    private const double HeaderComposerFontSize = 2.2;
    private const double DescentEm = 0.22; // serif descender depth per em

    public static double CalculateHeaderHeight(string? title, string? composer)
    {
        if (title != null && composer != null)
            return HeaderTitleFontSize + HeaderComposerFontSize * DescentEm;
        if (title != null)
            return HeaderTitleFontSize * DescentEm;
        if (composer != null)
            return HeaderComposerFontSize * DescentEm;
        return 0;
    }

    /// <summary>
    /// A staff's WITHIN-SYSTEM vertical offset in LilyPond's frame: staff-spaces
    /// <b>UP</b> from the system top to this staff's top line, so it is NEGATIVE for
    /// every staff below the first and 0 for the first. Now that
    /// <see cref="StaffLayout.Y"/> stores that frame natively (island 1's atomic
    /// flip), this reads <c>staff.Y</c> straight out. Returns 0 when the staff is not
    /// found (single-staff fallback), so this is exactly
    /// <see cref="FindStaffYInSystem"/> minus <see cref="SystemLayout.Y"/>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:274 — <c>where += stacking_dir * dy</c>
    /// with <c>stacking_dir = DOWN = -1</c>; the accumulator walks negative and the
    /// translates are stored as-is.
    /// LILYPOND-REF: lily/page-layout-problem.cc:896-901 — a system's staves are placed
    /// from <c>min_offsets</c>, which <c>Align_interface::get_minimum_translations</c>
    /// produces Y-up (negative going down); :915-917 calls the sign out explicitly
    /// ("this is relative to the system: negative numbers are down").
    ///
    /// This is the frame-INVARIANT part of the staff's vertical position: it is
    /// the offset within the system, independent of where paging places the
    /// system. Engravers that lay an element out relative to its own staff
    /// (ties, slurs, ledger spans, multi-measure rests, outside-staff stacking,
    /// figured bass) resolve against THIS or its <see cref="StaffOffsetInSystemDown"/>
    /// reflection rather than the absolute <see cref="FindStaffYInSystem"/>, so they
    /// stay decoupled from <see cref="SystemLayout.Y"/>.
    /// </remarks>
    public static double StaffOffsetInSystemUp(SystemLayout system, int staffIndex)
    {
        if (!system.StaffGroups.IsDefaultOrEmpty && staffIndex >= 0)
        {
            foreach (var staffGroup in system.StaffGroups)
            {
                foreach (var staff in staffGroup.Staves)
                {
                    if (staff.StaffIndex == staffIndex)
                        return staff.Y;
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// A staff's WITHIN-SYSTEM vertical offset as a DOWNWARD (device) distance from
    /// the system top to this staff's top line — positive for every staff below the
    /// first. Exactly <c>-<see cref="StaffOffsetInSystemUp"/></c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This does NOT "go away when the last caller is gone", which an earlier
    /// version of this remark predicted. Surveyed 2026-07-22: of its callers, only the
    /// two Y-up skyline passes in <c>LayoutEngine</c> were migrations; the rest are the
    /// boundaries of computations that are DELIBERATELY device (the tab/arc geometry
    /// behind <c>TabStaffGeometry</c>, the slur and tie-variant scorers, the paging
    /// extent pass, <c>SkylineDrop</c>'s floor, and the stored device Y of ledger spans
    /// and multi-measure rests). A device island needs a reflection at its edge, and
    /// this accessor IS that reflection. It survives on purpose — the negation now
    /// lives here rather than in <see cref="StaffOffsetInSystemUp"/> because storage
    /// itself is Y-up (island 1's atomic flip, docs/HANDOFF.md 3B).
    /// </remarks>
    public static double StaffOffsetInSystemDown(SystemLayout system, int staffIndex)
        => -StaffOffsetInSystemUp(system, staffIndex);

    /// <summary>
    /// Finds the absolute page-Y-up position of a staff's TOP line within a
    /// specific system (staff-spaces UP from the page bottom). Returns system.Y —
    /// the system top's Y-up — if no matching staff is found (single-staff
    /// fallback). Both <see cref="SystemLayout.Y"/> (Stage-4 W2-core) and
    /// <see cref="StaffLayout.Y"/> (island 1) now store page/system Y-up natively,
    /// so this is a plain SUM — the composition LilyPond itself performs.
    /// </summary>
    public static double FindStaffYInSystem(SystemLayout system, int staffIndex)
        => system.Y + StaffOffsetInSystemUp(system, staffIndex);

    /// <summary>
    /// Absolute page-Y-up of a staff's middle line, the anchor that staff-position
    /// reflections (device = middle − pos/2) measure staff positions from.
    /// Equivalent to <see cref="FindStaffYInSystem"/> LESS half the staff height
    /// (the middle sits half a staff below the top, so in the Y-up frame it
    /// subtracts). Engravers that route an element to its own staff (ties, slurs,
    /// glissandi, multi-measure rests, ledger-line spanners) share this resolution.
    /// </summary>
    public static double ResolveStaffMiddleY(SystemLayout system, int staffIndex, double staffHeight)
        => FindStaffYInSystem(system, staffIndex) - staffHeight / 2.0;

    /// <summary>
    /// Page Y-up of a system's top line — staff-spaces measured UP from the page
    /// bottom. The renderer emits page-Y-up primitives (the single device flip is
    /// the <see cref="Rendering.YFlipDrawingContext"/>), so this is the origin a
    /// system-anchored draw adds its relative Y-up to. Since <see
    /// cref="SystemLayout.Y"/> now stores page Y-up natively (Stage-4 W2-core),
    /// this returns it directly (kept as a named alias at the render seam).
    /// </summary>
    public static double SystemTopYUp(SystemLayout system)
        => system.Y;

    /// <summary>
    /// Page Y-up of a staff's top line within a system. Now that
    /// <see cref="SystemLayout.Y"/> stores page Y-up natively (Stage-4 W2-core),
    /// this is identical to <see cref="FindStaffYInSystem"/> (kept as a named
    /// alias at the render seam).
    /// </summary>
    public static double StaffTopYUp(SystemLayout system, int staffIndex)
        => FindStaffYInSystem(system, staffIndex);

    /// <summary>
    /// Resolves an item's X offset within a measure layout. Single-staff
    /// layouts index <see cref="MeasureLayout.Items"/> directly; multi-staff
    /// layouts use timing-aligned COLUMNS, so the item's timing is computed
    /// from the voice's measures and matched to a column. Engravers that
    /// index Items directly silently shift on the multi-staff path — always
    /// go through this helper.
    /// </summary>
    public static double GetItemXOffset(
        ImmutableArray<Measure> measures, int measureIndex, int itemIndex, MeasureLayout measureLayout)
    {
        // Single-staff path: MeasureLayout.Items aligns with this voice.
        if (measureLayout.Columns.IsDefaultOrEmpty)
        {
            if (itemIndex < measureLayout.Items.Length)
                return measureLayout.Items[itemIndex].X;
            return 0;
        }

        // Multi-staff path: timing → column lookup.
        if (measures.IsDefault || measureIndex < 0 || measureIndex >= measures.Length)
            return 0;
        var measure = measures[measureIndex];
        var timing = Fraction.Zero;
        for (int i = 0; i < itemIndex && i < measure.Items.Length; i++)
            timing = timing + measure.Items[i].Duration;

        return NearestColumnX(measureLayout.Columns, timing);
    }

    /// <summary>
    /// X of the column whose timing matches <paramref name="timing"/> exactly, else the
    /// nearest column by absolute timing distance (0 when there are no columns). This is
    /// the snap-to-onset resolution shared by <see cref="GetItemXOffset"/> and
    /// <see cref="MeasureLayouter.LayoutItemsFromColumns"/>. It is DISTINCT from
    /// <see cref="MeasureLayout.GetXForTiming"/>, which interpolates between the
    /// bracketing columns for a timing that falls BETWEEN onsets — do not fold the two
    /// together. For an exact item onset (the only timings this helper is fed) both agree.
    /// </summary>
    internal static double NearestColumnX(ImmutableArray<ColumnLayout> columns, Fraction timing)
    {
        if (columns.IsDefaultOrEmpty)
            return 0;

        double targetT = timing.ToDouble();
        double bestX = 0;
        double bestDiff = double.MaxValue;
        foreach (var col in columns)
        {
            if (col.Timing == timing)
                return col.X;
            double diff = Math.Abs(col.Timing.ToDouble() - targetT);
            if (diff < bestDiff) { bestX = col.X; bestDiff = diff; }
        }
        return bestX;
    }
}
