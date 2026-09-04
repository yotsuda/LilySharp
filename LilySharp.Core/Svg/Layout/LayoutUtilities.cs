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
    /// ⚠️ A DOWN stem reads the font too — the LILC <c>attachment-down</c> entry
    /// (lily/open-type-font.cc:334-369). For a DEFAULT head that X is 0.000000 in every
    /// design (the box's left edge), which kept
    /// <see cref="EngravingDefaults.StemDownAttachX"/> a constant while only default
    /// heads had stems; a styled shape attaches each stem where its ink is
    /// (s2triangle's down point is X 0.218600, a fifth of the way in). The default UP
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
    /// <see cref="GlyphMetrics.NoteValueOf(LilySharp.Core.Semantics.Fraction)"/> reads it.
    /// A BEAM member is not always a black
    /// head: a two-note tremolo pair beams HALVES (<c>BeamDetector.IsBeamable</c>).
    /// </param>
    /// <param name="headScale">
    /// The head's own scale. A cue head is drawn at 0.66×, so an UP stem attaches at the
    /// scaled head's attachment point or it floats off the small head; a down stem attaches at
    /// the left edge, which does not move with the scale. At noteValue 4 and scale 1.0 this is
    /// <see cref="EngravingDefaults.StemUpAttachX"/> exactly.
    /// </param>
    public static double StemAttachX(bool up, int noteValue, NoteheadStyle style,
        double headScale = 1.0) =>
        // Both directions are the font's own attachment coordinate for THIS head's
        // glyph, pulled back half a stem thickness toward the head so the stem's EDGE
        // meets the point.
        // LILYPOND-REF: lily/stem.cc:1071-1086 internal_calc_stem_offset_from_head — r += -d * rule_thick * 0.5
        // For a DEFAULT head the down X is 0.000000 in every design, which is what
        // EngravingDefaults.StemDownAttachX pinned while only default heads had stems;
        // a styled shape (s2triangle: 0.2186) attaches its down stem where its ink is.
        GlyphMetrics.GetNoteheadStemAttachment(style, up, noteValue).X * headScale
        + (up ? -1 : 1) * EngravingDefaults.StemThickness / 2;

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
    public static double StemAttachX(bool up, int noteValue, NoteheadStyle style,
        GlyphMetrics.DesignMetrics? font) =>
        GlyphMetrics.GetNoteheadStemAttachment(
            font ?? GlyphMetrics.Design20, style, up, noteValue).X
        + (up ? -1 : 1) * EngravingDefaults.StemThickness / 2;

    /// <summary>The head STYLE of the item a stem attaches to — Default for anything
    /// that has no styled head (a rest, a spacer, null). The style picks which glyph's
    /// attachment point <see cref="StemAttachX(bool, int, NoteheadStyle, double)"/>
    /// reads, exactly as the note value picks between the half and black heads.</summary>
    public static NoteheadStyle NoteheadStyleOf(MusicItem? item) => item switch
    {
        NoteItem n => n.Notehead,
        ChordItem c => c.Notehead,
        _ => NoteheadStyle.Default,
    };

    /// <summary>The x a stem stands at, given its note column's x.</summary>
    /// <remarks>See <see cref="StemAttachX(bool, int, NoteheadStyle, double)"/>.</remarks>
    public static double StemX(double columnX, bool up, int noteValue, NoteheadStyle style,
        double headScale = 1.0) =>
        columnX + StemAttachX(up, noteValue, style, headScale);

    /// <summary>The x a stem stands at, for a head read from <paramref name="font"/>.</summary>
    /// <remarks>See <see cref="StemAttachX(bool, int, NoteheadStyle, GlyphMetrics.DesignMetrics)"/>.</remarks>
    public static double StemX(double columnX, bool up, int noteValue, NoteheadStyle style,
        GlyphMetrics.DesignMetrics? font) =>
        columnX + StemAttachX(up, noteValue, style, font);

    /// <summary>
    /// The x an INVISIBLE stem stands at: the centre of its head's ink. A whole-note
    /// (or breve) display tremolo pair carries such stems — no ink of their own, but
    /// they are the beam's X frame, so the floating beam sits symmetrically between
    /// the heads.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/stem.cc:1051-1066 internal_calc_stem_offset_from_head —
    ///   <c>center_invisible &amp;&amp; is_invisible</c> answers attach 0.0, i.e. the
    ///   head-width interval's centre ("we center an invisible stem on the support
    ///   head because some things depend on that").
    /// </remarks>
    public static double InvisibleStemX(double columnX, int noteValue) =>
        columnX + GlyphMetrics.GetNoteheadBBox(noteValue).CenterX;

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
    /// LILYPOND-REF: lily/page-layout-problem.cc:622-626 — the distance the floor is built
    /// from is <c>Skyline::distance</c>, which is SIGNED: a system whose ink stops short of
    /// the reference it is measured from answers a NEGATIVE number and the spring is floored
    /// that much lower (lily/skyline.cc:667-680 <c>max_height</c> has no clamp either).
    /// MaxHeight() returns the topmost Y-up about the system's ORIGIN.
    /// ⚠️ IT WAS CLAMPED AT 0 UNTIL 2026-08-25, which reads "the origin is where the ink
    /// starts". It is, while the topmost element is a STAFF — the staff symbol's own top
    /// line IS the origin. A chord or lyric ROW is placed as a BAND whose top stands above
    /// its own ink (MEASURED: a chord row's baseline sits <c>RefpointBelowTop</c> 2.900000
    /// under the band top while its ink reaches 1.907250371, so 0.992749629 of the band is
    /// empty), and the clamp charged the page for that empty strip.
    /// </remarks>
    public static double CalculateUpExtent(VerticalSkyline upSkyline)
    {
        return upSkyline.IsEmpty ? 0 : upSkyline.MaxHeight();
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
    /// Two frames meet here and must not be confused:
    /// <list type="bullet">
    /// <item>Lily#'s system origin and <paramref name="systemUpExtent"/> are the TOPMOST
    /// ELEMENT's own top and the ink above it.</item>
    /// <item>LilyPond's <c>up_skyline.distance()</c> is measured from the first SPACEABLE
    /// staff's REFPOINT and always contains the staff symbol itself.</item>
    /// </list>
    /// They differ by <paramref name="originToRefpoint"/>, which is LilyPond's
    /// <c>-first_spaceable_dy</c> (page-layout-problem.cc:1120-1122) written in Lily#'s
    /// frame: half a staff while the topmost element IS that staff, and half a staff PLUS
    /// the rows the alignment stacked over it on a lead sheet.
    /// ⚠️ IT WAS THE STAFF'S OWN HALF SPAN UNTIL 2026-08-25, which asserts the first case
    /// for every score. MEASURED on the reported book (scratch/ベースタブLy/Untitled-6.lys,
    /// user report): a system written <c>chords / lyrics / staff</c> reserved 2.000000
    /// where its own alignment had stacked 27.782041 over that staff, so the two rows were
    /// spaced into the paper's top margin and the chord symbols printed THROUGH the title.
    /// Placing the first system is done in the refpoint frame here and converted back to
    /// the origin frame ONCE, in <see cref="CalculateFirstSystemY"/>.
    /// </remarks>
    public static double CalculateFirstStaffRefpoint(
        double topMargin, HeaderBand? header, double systemUpExtent,
        double originToRefpoint, VerticalSpacingParameters vs)
        // LILYPOND-REF: lily/simple-spacer.cc:295-305 spring_positions — a system's
        // position is the running sum of its springs' lengths, and for the FIRST system
        // that sum is the top spring alone — or, under a title, the top-markup spring and
        // the markup-system spring (page-layout-problem.cc:468-469, :506-507). At force 0
        // Spring::length is max(min_distance_, ideal_distance_) (spring.cc:219-237), which
        // is why a system whose ink is smaller than top-system-spacing's basic-distance is
        // not measured by its ink at all.
        => topMargin + (header is null
            ? CreateTopSystemSpring(systemUpExtent, originToRefpoint, vs.TopSystem).Length(0)
            : TitleTopSpring(vs).Length(0)
              + TitleToSystemSpring(header, systemUpExtent, originToRefpoint, vs).Length(0));

    /// <summary>
    /// The spring from the top of the printable area down to the TITLE column's top — the
    /// page's first spring when the page opens with a title.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:468-469 Page_layout_problem — the top spring
    /// is <c>top-markup-spacing</c> when the first element is a Prob; :734-770 append_prob
    /// floors it with the prob's ink above its reference point plus the padding, and a
    /// top-aligned title (paper-book.cc:443) has none, so the floor is the padding alone and
    /// the spring sits at its basic-distance 4 at rest. MEASURED (audit/lp-geometry
    /// titled-page.ly): the title's Y-offset is 4.000000 on every titled book.
    /// </remarks>
    public static Spring TitleTopSpring(VerticalSpacingParameters vs)
        => CreateSpring(vs.TopMarkup, vs.TopMarkup.Padding);

    /// <summary>
    /// The spring from the title column's top down to the first system's first staff
    /// refpoint — <c>markup-system-spacing</c>, floored by the title's depth, the system's
    /// ink above its refpoint and the padding.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:506-507 — <c>last_system_was_title</c>
    /// selects markup-system-spacing for the system after a title; :625-633 append_system
    /// floors it with <c>up_skyline.distance (bottom_skyline_) + padding</c>, where
    /// bottom_skyline_ is the title's down side (:746) and the system's up skyline is
    /// anchored on its first spaceable staff (:1120-1122). The title is a \fill-line, so its
    /// ink spans the line and the distance is against the system's TALLEST ink — MEASURED
    /// on titled-page.ly TTL: 20.073246 = top-margin + 4 + depth 6.106695 + 0.5 + the treble
    /// clef's 3.776 over the refpoint, to the digit.
    /// </remarks>
    public static Spring TitleToSystemSpring(
        HeaderBand header, double systemUpExtent, double originToRefpoint,
        VerticalSpacingParameters vs)
        => CreateSpring(vs.MarkupSystem,
            header.Depth + systemUpExtent + originToRefpoint + vs.MarkupSystem.Padding);

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
    /// LILYPOND-REF: lily/page-layout-problem.cc:1120-1122 — <c>build_system_skyline</c>
    /// closes with <c>up->raise (-first_spaceable_dy)</c>, so the skyline
    /// <c>append_system</c> takes the distance from is anchored on the first SPACEABLE
    /// staff and carries every non-spaceable line stacked above it. Lily# keeps its
    /// silhouette in the ORIGIN frame instead and makes that same raise here, which is
    /// what <paramref name="originToRefpoint"/> is.
    /// </remarks>
    /// <param name="originToRefpoint">
    /// The distance DOWN from the system's ORIGIN to its first SPACEABLE staff's refpoint —
    /// <c>PageAnchorOffsets</c>' <c>ToFirst</c>. ⚠️ NOT the staff's own half span: the two
    /// agree only while the topmost element IS that staff, and a lead sheet's chord and
    /// lyric rows are exactly the case where they part (see
    /// <see cref="CalculateFirstStaffRefpoint"/>'s remark for the measurement).
    /// </param>
    public static Spring CreateTopSystemSpring(
        double systemUpExtent, double originToRefpoint, VerticalSpacingSpec topSpec)
    {
        // Lily#'s up extent is the ink above the system's ORIGIN; LilyPond's up_skyline is
        // measured from the first spaceable staff's REFPOINT and always contains the staff
        // symbol itself, so the same quantity is originToRefpoint more there.
        // ★ THE HEADER LEFT THIS FLOOR IN SESSION 336. LilyPond's header_height_ (:435, :444)
        // is the PAGE header — oddHeaderMarkup, which Lily# does not print — and the book
        // TITLE is not in it: it is a paper system of its own at the head of the chain
        // (TitleTopSpring / TitleToSystemSpring), which is where Lily#'s title went too.
        double inkAboveRefpoint = systemUpExtent + originToRefpoint;
        return CreateSpring(topSpec, inkAboveRefpoint + topSpec.Padding);
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
    /// Distance DOWN from the paper's top edge to the FIRST system's ORIGIN (its TOPMOST
    /// ELEMENT's own top) — the frame <see cref="SystemLayout.Y"/> is stacked in.
    /// </summary>
    /// <remarks>
    /// The sole seam between the refpoint frame LilyPond's page spacing is written in and
    /// the origin frame Lily# stacks systems in; every caller placing a first system goes
    /// through here so the conversion exists in exactly one place.
    /// ⚠️ ONE PARAMETER, NOT TWO. It carried a <c>halfStaff</c> as well until 2026-08-25,
    /// on the reading that the FLOOR is built from an extent measured from the staff while
    /// only the ANSWER has to come back to the origin. Both are the same conversion — the
    /// extent is measured from the ORIGIN — and splitting them is what let the floor lose
    /// the rows a lead sheet stacks above its staff.
    /// </remarks>
    /// <param name="originToRefpoint">
    /// The distance from the system's ORIGIN down to its first SPACEABLE staff's refpoint.
    /// It converts the floor INTO the refpoint frame LilyPond writes the spring in, and the
    /// answer back OUT of it. Half a staff exactly while the topmost element is that staff.
    /// </param>
    public static double CalculateFirstSystemY(
        double topMargin, HeaderBand? header, double systemUpExtent,
        double originToRefpoint, VerticalSpacingParameters vs)
        => CalculateFirstStaffRefpoint(
               topMargin, header, systemUpExtent, originToRefpoint, vs)
           - originToRefpoint;

    // CalculateHeaderHeight lived here until session 336: "the ink below the title's
    // baseline, drawn at MarginTop" (3.974 for a title and a composer), folded into the
    // top-system spring's floor. It read LilyPond's header_height_ (page-layout-problem.cc:435)
    // as the book title, and it is not — that is the PAGE header. The title is HeaderBand, a
    // top-aligned column paged as a system of its own; audit/lp-geometry titled-page.ly
    // (page.titled.first-staff-refpoint, −5.632695 before the port) is the measurement.

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
    /// <summary>
    /// The staff a SCORE-CONTEXT grob hangs on — a rehearsal mark, a section label, a
    /// bar number: the system's topmost SPACEABLE staff.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE WALK IS <see cref="StaffAffinity.TopSpaceableStaff"/>'s — the bar number's
    /// <c>AnchorStaff</c> under its neutral name, whose remarks carry the LilyPond citation
    /// and the 2026-08-20 measurement. What this adds is the STAFFLESS fall-back, which the
    /// bar number answers differently (it has <c>BarNumberEngraver.AnchorRow</c>, chosen by
    /// which row reaches x≈0); a mark has no such rule and keeps the topmost placed row.
    /// ⚠️ ONE HOME FOR THE SENTINEL. Several above-staff layouts spell "the top staff" as
    /// <c>StaffIndex = -1</c> and the number was being resolved in two places that
    /// disagreed: <c>OutsideStaffStacker</c>'s tracker resolved it to the topmost PLACED
    /// element, while <see cref="StaffOffsetInSystemUp"/> did not resolve it at all and
    /// returned 0 — the SYSTEM TOP. The two coincide only while the system's top element
    /// IS a staff. Written <c>chords / staff / lyrics</c> they part company, and the
    /// section mark was PRICED against the melody staff's occupancy while being DRAWN
    /// from the chord row's band: 7.060 above the staff's top line, where every other row
    /// order puts it at 2.660 (user report, session 243).
    /// <para>
    /// ⚠️ A TEXT ROW IS NOT A STAFF. MEASURED against LilyPond 2.26.0 (a ChordNames
    /// context above a Staff, with <c>\mark</c>): LP draws the mark ON THE CHORD ROW'S OWN
    /// LINE, level with the symbols and immediately above the staff — a RehearsalMark's
    /// Y-parent is a staff's axis group, and a ChordNames context is not one.
    /// </para>
    /// <para>
    /// ⚠️ THE FALL-BACK IS REACHED AND IS NOT A GUESS: a rows-only lead sheet (chords row
    /// + lyrics row, no staff at all) has no spaceable line, and its marks and bar numbers
    /// must still hang on something — the topmost placed row, which is where they hung
    /// before. That book is <c>RowsOnlySystemGapTests</c>' arm.
    /// </para>
    /// </remarks>
    public static int TopScoreGrobStaff(SystemLayout system)
    {
        if (StaffAffinity.TopSpaceableStaff(system) is { } staff)
            return staff.StaffIndex;
        // Staffless: the topmost placed ROW, which is where these grobs hung before.
        int placed = -1;
        double placedY = double.NegativeInfinity;
        foreach (var group in system.StaffGroups.IsDefaultOrEmpty
                     ? ImmutableArray<StaffGroupLayout>.Empty : system.StaffGroups)
        {
            if (group.Staves.IsDefaultOrEmpty) continue;
            foreach (var row in group.Staves)
                if (!row.IsHidden && row.Y > placedY) { placedY = row.Y; placed = row.StaffIndex; }
        }
        return placed;
    }

    /// <summary>
    /// Resolves a score-context grob's staff index: its own when it carries one, and the
    /// system's <see cref="TopScoreGrobStaff"/> for the <c>-1</c> sentinel.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONLY FOR THE LAYOUTS WHOSE <c>-1</c> MEANS "THE TOP STAFF" — MusicMarkLayout,
    /// CustomTextLayout, BarNumberLayout and their kin. Several OTHER layouts spell an
    /// UNKNOWN staff the same way (<c>ArpeggioLayout</c>, <c>GraceNoteLayout</c> and
    /// <c>TupletBracketLayout</c> all say "-1 = unknown/test construction" at their
    /// declaration), and for those the sentinel is not this question.
    /// </remarks>
    public static int ResolveScoreGrobStaff(SystemLayout system, int staffIndex)
        => staffIndex >= 0 ? staffIndex : TopScoreGrobStaff(system);

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
    /// A staff's WITHIN-SYSTEM middle line as a Y-up offset from the system top —
    /// <see cref="StaffOffsetInSystemUp"/> (the staff's top line) less the nominal
    /// half staff (<see cref="EngravingDefaults.StaffMiddle"/>). This is the frame
    /// the outside-staff stacker and the paging skylines price grobs in: a grob
    /// whose stored <c>YUp</c> is Y-up about its staff's middle enters the system
    /// frame at <c>YUp + StaffMiddleUpInSystem(…)</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ NOMINAL, LIKE EVERY <c>- 2.0</c> IT REPLACED: 2.0 is half of a five-line
    /// staff's four spaces. A six-string tab staff's middle sits 3.75 below its top
    /// (five gaps of <see cref="EngravingDefaults.TabStringSpace(int)"/> 1.5,
    /// halved) and an ossia's shrinks with it — every caller prices those with the
    /// nominal number today, and this helper is the one home so the day one of them
    /// grows staff-aware geometry they all do (HANDOFF §5.2.1②, review 2026-08-26 ④).
    /// ⚠️ Does NOT resolve the <c>-1</c> sentinel: the raw sentinel falls through
    /// <see cref="StaffOffsetInSystemUp"/>'s <c>staffIndex &gt;= 0</c> guard to 0 —
    /// the SYSTEM TOP. A caller whose <c>-1</c> means "the top staff" resolves it
    /// first via <see cref="ResolveScoreGrobStaff"/> (the mark arm's session-243
    /// lesson at its call site).
    /// </remarks>
    public static double StaffMiddleUpInSystem(SystemLayout system, int staffIndex)
        => StaffOffsetInSystemUp(system, staffIndex) - EngravingDefaults.StaffMiddle;

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
    /// THE inter-system pair minimum — the staff-frame (refpoint-to-refpoint)
    /// distance LilyPond floors the system-system spring with, for the pair
    /// (prev, next): the X-aware skyline distance converted out of the origin
    /// frame, floored by the whole-line chord-row band, with the scalar sum as
    /// the fallback where a silhouette cannot answer. One home for what
    /// LayoutEngine.CreatePages (single-page stack) and
    /// PageLayouter.PositionSystemsOnPage (spring chain) each spelled locally.
    /// LILYPOND-REF: lily/page-layout-problem.cc:625-632 append_system — the
    ///   skyline distance plus padding reaches the spring as a floor (padding is
    ///   added by the callers, not here).
    /// LILYPOND-REF: lily/page-layout-problem.cc:1120-1126 build_system_skyline —
    ///   first_spaceable_dy / last_spaceable_dy are the frame conversion this
    ///   function performs at the spring instead of by raising the skylines.
    /// LILYPOND-REF: lily/page-layout-problem.cc:1070-1127 build_system_skyline —
    ///   the per-system silhouettes whose Distance() the callers hand in.
    /// LILYPOND-REF: lily/skyline.cc:529-535 Skyline::distance — the X-aware
    ///   minimum clearance itself (VerticalSkyline.Distance is its port).
    /// </summary>
    /// <remarks>
    /// ⚠️ NOT A LITERAL PORT, and the deviation is here rather than hidden:
    /// LilyPond performs the origin-to-refpoint conversion by RAISING THE SKYLINES
    /// THEMSELVES inside build_system_skyline, so what leaves that function is
    /// already relative to the top/bottom spaceable staff. Lily# adds the span at
    /// the spring instead, which keeps SkylineBuilder in ONE frame for its other
    /// readers (LayoutEngine's up/down extents, the paging pass). Same number,
    /// different place; moving it into the builder is a frame migration across
    /// those readers, not an edit to this function — HANDOFF 2D. (A leading loose
    /// line — a lead sheet's chord row — sits between the anchor and the first
    /// sprung staff, which is why the conversion is measured from the anchor and
    /// not from the first spring; and the band arm takes HalfFirst because a band
    /// is measured from the STAFF it hangs off — ToFirst on both sides would count
    /// a leading chord row twice.)
    /// ⚠️ ONE COMPOSITION, MEASURED INTO PLACE. The two callers used to ASSOCIATE
    /// this algebra differently (the single-page stack floored in the origin frame
    /// and converted once at the end; the chain wrote refpoint-frame terms as
    /// built — algebraically equal, expanded by the 2026-08-26 review §4-②), and
    /// doubles round per operation, so unifying them could have moved numbers.
    /// MEASURED 2026-08-27 before shipping (HANDOFF §1 第264): recomposing the
    /// single-page caller onto this chain-frame composition moved 0 of 1386
    /// full-tree books (ink / data-pos / midi hashes), 0 of 675 ledger points and
    /// 0 of 222 snapshots, suite fully green — the collapse's price was zero at
    /// every instrument, so this function carries the chain's composition and the
    /// origin-frame association is gone.
    /// ⚠️ THE OBSERVERS: the LP-regression corpus (rerender-ls, 81 books) is BLIND
    /// here — every book is single-system, so no pair exists (measured 2026-08-27:
    /// +0.01 on every return moved 0/81). What sees this function is the ledger's
    /// multi-system points (23 went red under the same poison, Release build) and
    /// the full-tree sweep's multi-system books.
    /// The inventory of REAL divergences (not just association):
    /// ⑴ THE EMPTY-SILHOUETTE FALLBACK CONVERTS DIFFERENTLY PER CALLER
    /// (<paramref name="emptySilhouetteHalfFirstFallback"/>): the single-page path
    /// converts the origin-measured extents with ToFirst, the spring chain with
    /// HalfFirst — for a system led by a loose row (chords/lyrics) the two differ
    /// by the row band, so this is a different NUMBER, not a different rounding.
    /// (The no-skyline case uses ToFirst on both paths — the extents are
    /// origin-measured, and the anchors' own remark says origin-measured terms take
    /// ToFirst.) ★ REACHABILITY MEASURED 2026-08-27 (session 266): the branch fired
    /// on NONE of 612 books (the 82-book corpus, the user's lead sheets, the p257
    /// variants) nor on any adversarial construction (rows-only scores, hara-kiri'd
    /// rows-only systems, single- and multi-page, titles) — a staffed edge seeds
    /// staff-symbol ink, and even an all-rows system's silhouette is fed by the
    /// paging augment families. Unreachable today, so no observer CAN be built; the
    /// first book that reaches it brings its observer with it
    /// (audit/lp-geometry/probes/rows-only-page.ly is where that pair would be
    /// refereed, and its header carries this audit).
    /// ⑵ THE ROWS-ONLY SCALAR FLOOR EXISTS ONLY ON THE SINGLE-PAGE PATH
    /// (<paramref name="scalarFloorForSpaceablelessPrev"/>): a system with no
    /// spaceable staff has no down silhouette to refine, so Distance() under-answers
    /// and the scalar stands (measured session 240, scratch/ベースタブLy/Untitled-6.lys
    /// — 6.395 against a true 14.900). The spring chain never grew the arm; its
    /// caller passes false, and extending the fix there is its own measured change.
    /// ★ MEASURED 2026-08-27 (session 266): on every constructible rows-only book
    /// the un-floored chain and the floored single-page path answer the SAME number
    /// — the rows-only silhouette degrades to exactly the scalar extents
    /// (skylineDistance == inkBelowLastRefpoint + nextUpExtent + prevOriginToLast,
    /// one- and two-row shapes alike) — so the missing arm is value-inert today.
    /// An LP referee for the pair was built and its ledgering DECLINED: LilyPond's
    /// own default output for the shape is dominated by its loose-line
    /// redistribution (the next system's chord rows laid onto that system's staff,
    /// page-layout-problem.cc:860-880), the subsystem Lily# does not have (HANDOFF
    /// 2D) — a residual would price that absence, not this floor. The committed,
    /// re-runnable evidence is audit/lp-geometry/probes/rows-only-page.ly.
    /// ⑶ WHAT "prev's last refpoint" MEANS differs at the callers:
    /// the spring chain answers ToFirst when the system carries no staff springs
    /// (the chain's last node — see its LILYSHARP-OWN remark), the single-page path
    /// always answers ToLast; equal today because springs are empty exactly when the
    /// system has at most one spaceable staff, where ToFirst == ToLast
    /// (InterSystemFloorTests.EverySystemWithTwoSpaceableStaves_CarriesAStaffSpring).
    /// </remarks>
    /// <param name="hasSkylines">False when the caller has no per-system skylines at
    /// all (preliminary pass) — distinct from an EMPTY silhouette, which arrives as
    /// a negative-infinity <paramref name="skylineDistance"/> and falls back
    /// differently on the spring chain (divergence ⑴).</param>
    /// <param name="skylineDistance">VerticalSkyline.Distance(next.up, prev.down,
    /// SystemSkylineHorizontalPadding) — origin-to-origin; negative infinity when a
    /// silhouette is empty. Ignored when <paramref name="hasSkylines"/> is false.</param>
    /// <param name="prevBodyHeight">prev's body height (origin to bottom staff line).</param>
    /// <param name="prevDownExtent">prev's scalar downward protrusion below its body.</param>
    /// <param name="nextUpExtent">next's scalar upward protrusion above its origin.</param>
    /// <param name="prevOriginToLast">prev's origin-to-last-refpoint span — the
    /// caller's own answer (divergence ⑶).</param>
    /// <param name="nextToFirst">next's origin-to-first-refpoint span (converts
    /// origin-measured terms).</param>
    /// <param name="nextHalfFirst">next's first staff's own half span (converts
    /// staff-measured terms — the band).</param>
    /// <param name="bandUpNext">the whole-line chord-row band above next (0 = none).</param>
    /// <param name="scalarFloorForSpaceablelessPrev">single-page only (divergence ⑵):
    /// true when prev has no spaceable staff, so the scalar sum floors the answer.</param>
    /// <param name="emptySilhouetteHalfFirstFallback">divergence ⑴: the spring chain
    /// converts the empty-silhouette fallback with HalfFirst (true), the single-page
    /// path with ToFirst (false).</param>
    public static double InterSystemPairMinimum(
        bool hasSkylines,
        double skylineDistance,
        double prevBodyHeight,
        double prevDownExtent,
        double nextUpExtent,
        double prevOriginToLast,
        double nextToFirst,
        double nextHalfFirst,
        double bandUpNext,
        bool scalarFloorForSpaceablelessPrev,
        bool emptySilhouetteHalfFirstFallback)
    {
        // How far prev's ink reaches below the staff its spring attaches to — the
        // term the scalar fallbacks and the band floor are written from.
        double inkBelowLastRefpoint = prevBodyHeight - prevOriginToLast + prevDownExtent;
        if (!hasSkylines)
            return inkBelowLastRefpoint + nextUpExtent + nextToFirst;
        if (double.IsNegativeInfinity(skylineDistance))
            return inkBelowLastRefpoint + nextUpExtent
                + (emptySilhouetteHalfFirstFallback ? nextHalfFirst : nextToFirst);
        double dist = skylineDistance + (nextToFirst - prevOriginToLast);
        if (bandUpNext > 0)
            dist = Math.Max(dist, inkBelowLastRefpoint + nextHalfFirst + bandUpNext);
        if (scalarFloorForSpaceablelessPrev)
            dist = Math.Max(dist, inkBelowLastRefpoint + nextUpExtent + nextToFirst);
        return dist;
    }

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
