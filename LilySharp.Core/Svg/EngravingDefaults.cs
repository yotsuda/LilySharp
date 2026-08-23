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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg;

/// <summary>
/// Default metrics for music engraving.
/// All values are in staff spaces.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/spacing-options.cc:55-66 Spacing_options constructor (defaults)
/// LILYPOND-REF: scm/define-grobs.scm (space-alist for Clef, BarLine, TimeSignature, StaffGrouper)
/// </remarks>
internal static class EngravingDefaults
{
    // === Staff and lines ===

    /// <summary>
    /// Base line thickness. LilyPond's layout <c>line-thickness</c> at the
    /// default 20pt staff: calc-line-thickness interpolates to 0.5pt =
    /// 0.10 staff space. Every line-family thickness derives from this,
    /// mirroring LilyPond's structure (stems 1.3×, staff lines 1.0×, …).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:52-66 calc-line-thickness.</remarks>
    public const double LineThickness = 0.1;

    /// <summary>Staff line thickness: 1.0 × line-thickness.</summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-symbol.cc — StaffSymbol thickness default 1.0
    /// (in line-thickness units).
    /// </remarks>
    public const double StaffLineThickness = 1.0 * LineThickness;

    /// <summary>
    /// Horizon padding applied when measuring the X-aware distance between two
    /// SYSTEMS (page stacking): each roof gets 45° shoulders this wide, so
    /// facing ink that is only just X-disjoint (a deep bass note one staff
    /// space left of the next system's high note, a line-start bar number
    /// beside the staff) still spaces the systems apart instead of slipping
    /// past in the max-over-X.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm System (skyline-horizontal-padding . 1.0)
    /// LILYPOND-REF: lily/page-layout-problem.cc:618-629 append_system —
    /// up_skyline.distance (bottom_skyline_, skyline-horizontal-padding).
    /// </remarks>
    public const double SystemSkylineHorizontalPadding = 1.0;

    /// <summary>
    /// Ledger line thickness: StaffSymbol.ledger-line-thickness = (1.0 . 0.1)
    /// → 1.0·line-thickness + 0.1·staff-space.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-symbol.cc:337-344 get_ledger_line_thickness;
    /// scm/define-grobs.scm StaffSymbol (ledger-line-thickness . (1.0 . 0.1)).
    /// </remarks>
    public const double LegerLineThickness = 1.0 * StaffLineThickness + 0.1 * 1.0;

    /// <summary>
    /// Ledger lines extend beyond the notehead by this FRACTION of the head's
    /// own width on each side — proportional, so wide whole/half noteheads get
    /// proportionally longer ledgers.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/ledger-line-spanner.cc:204-233 —
    /// ledger_extent = head_extent widened by length-fraction·head_width;
    /// (length-fraction . 0.25) per scm/define-grobs.scm LedgerLineSpanner.
    /// </remarks>
    public const double LedgerLengthFraction = 0.25;

    // === Stems ===

    /// <summary>Stem thickness: 1.3 × line-thickness = 0.13 staff space.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm (Stem (thickness . 1.3)).</remarks>
    public const double StemThickness = 1.3 * LineThickness;
    // LILYPOND-REF: scm/define-grobs.scm:3448 Stem (lengths . (3.5 3.5 3.5 4.25 5.0 ...)) —
    // the first three entries (whole/half/quarter) are 3.5, LP's ideal single-note stem.
    public const double IdealStemLength = 3.5;
    // LILYSHARP-OWN: a 5-half-space floor with no single named LP constant behind it.
    // ⚠️ ITS FIRST CONSUMER IS GONE (session 85). It used to floor the UNBEAMED stem too, in
    // StemCalculator.CalculateStemEndY, on the claim that "LP never lets a lone stem drop
    // below this via the details minima" — but LilyPond bounds `length` nowhere in
    // internal_calc_stem_end_position, and the floor never fired anyway (the shortest length
    // the rule can produce is 3.5 − 1.0 = 2.5 exactly). It became live only when a
    // length-fraction arrived, so it was deleted there; removing it moved no test, no
    // snapshot and no ledger point. What remains is the BEAM quanter's use below, which is a
    // different mechanism and has not been audited against LilyPond.
    // LILYPOND-REF: lily/stem.cc:481-596 internal_calc_stem_end_position.
    public const double MinStemLength = 2.5;
    public const double DefaultStemLength = 3.5;

    // === Beams ===
    // LILYPOND-REF: scm/define-grobs.scm Beam (beam-thickness . 0.48) — in staff-space.
    public const double BeamThickness = 0.48;
    public const double BeamSpacing = 0.25;
    /// <summary>Distance between beam centers for multiple beams.</summary>
    // LILYPOND-REF: lily/beam.cc Beam::get_beam_translation — for <4 beams,
    // (2·ss + line − beam-thickness)/2 (ss = staff-space = 1.0 here).
    public const double BeamTranslation = (2.0 + LineThickness - BeamThickness) / 2.0;
    /// <summary>How far a GAPPED tremolo-pair beam stops short of each stem —
    /// the visual break that keeps the repeat symbol from reading as an
    /// ordinary beam.</summary>
    // LILYPOND-REF: scm/define-grobs.scm Beam (gap . 0.8).
    // LILYPOND-REF: lily/beam.cc:403-427 get_gaps — reads the gap length.
    public const double TremoloBeamGap = 0.8;
    /// <summary>A GRACE beam's declared thickness — LilyPond states it, it is not derived.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 <c>general-grace-settings</c> —
    ///   <c>(Voice Beam beam-thickness 0.384)</c>, alongside <c>(Voice Beam length-fraction 0.8)</c>
    ///   and <c>(Voice Stem length-fraction 0.8)</c> (both <see cref="GraceBeamLengthFraction"/>)
    ///   and the per-grob font-sizes (<see cref="Model.GraceNoteItem.FontSizeStep"/>).
    ///   ⚠️ NOT ly/grace-init.ly, which this said until 2026-08-02: that file holds the grace
    ///   slur and the acciaccatura slash and states no sizes at all.
    /// MEASURED on 2.26.0 (audit/lp-geometry/probes/beam-grace.ly, score G): the Beam grob of
    /// <c>\grace { d'16 e' }</c> reports beam-thickness 0.384 and length-fraction 0.8, against
    /// 0.48 and unset for the same two pitches written as ordinary sixteenths (score H).
    /// </remarks>
    public const double GraceBeamThickness = 0.384;
    /// <summary>A grace Beam's and Stem's <c>length-fraction</c>.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:635-648 <c>general-grace-settings</c>.
    /// ⚠️ NOT the notehead's scale: the heads
    /// shrink with <c>fontSize = -3</c>, i.e. <c>magstep(-3)</c>, which is a different number
    /// again — <see cref="Model.GraceNoteItem.ScaleFactor"/>, which DERIVES it rather than
    /// writing the result down. ⚠️ This line used to say the heads were drawn at 0.65; that
    /// stopped being true on 2026-08-01 and the sentence outlived it by one session.
    /// Three quantities, three values — do not fold them.
    /// </remarks>
    public const double GraceBeamLengthFraction = 0.8;
    /// <summary>
    /// The distance between beam centres for a beam of the given thickness,
    /// <c>length-fraction</c> and beam count — the derivation <see cref="BeamTranslation"/>
    /// is the full-size, fewer-than-four case of.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/beam.cc:130-145 Beam::get_beam_translation —
    ///   <c>(2·ss·fract + line·fract − beam-thickness) / 2</c> for fewer than four beams and
    ///   <c>(3·ss·fract + line·fract − beam-thickness) / 3</c> from four up.
    /// <para>
    /// ⚠️ THE STAFF SPACE AND THE LINE THICKNESS ARE SCALED BY <c>fract</c>; THE BEAM
    /// THICKNESS IS NOT — it arrives already scaled (0.384 for a grace,
    /// scm/music-functions.scm:635-648).
    /// LilyPond's own comment at :138-141 says exactly that: "if fract != 1.0, as is the case
    /// for grace notes, we want the gap to decrease too. To achieve this, we divide the
    /// thickness by fract." So a grace's translation is <c>(2×0.8 + 0.1×0.8 − 0.384)/2 =
    /// 0.648</c>, which is the full-size 0.81 scaled ONCE.
    /// </para>
    /// <para>
    /// ⚠️ This line used to read <c>fract × ((2 + line − thickness) / 2)</c> — scaling the
    /// already-scaled thickness a second time, for 0.6864 — and its own reference sentence
    /// stated the rule backwards. It was worth a whole quant step: the gap a staff line may
    /// not fall into is built from this number (lily/beam-quanting.cc:1287-1294), so a grace
    /// beam above the staff bought the next configuration up. Ledger grace.beam.stack-gap and
    /// beam.quant.grace.above-staff.*.
    /// </para>
    /// </remarks>
    /// <param name="lineThickness">
    /// The staff's line thickness IN THAT STAFF'S OWN SPACES. Only a TAB staff passes
    /// anything but the default: LilyPond builds this quantity from absolute lengths and
    /// the quanter then divides the lot by the staff space (lily/beam-quanting.cc:232-234
    /// beam_thickness_ and line_thickness_),
    /// so on a four-line tab of space 1.5 the staff-space term stays 2 while the line and
    /// the beam thicknesses arrive already divided — 0.1/1.5 and 0.48/1.5, for 0.873333
    /// against the notation staff's 0.81.
    /// </param>
    public static double BeamTranslationOf(double beamThickness, double lengthFraction, int beamCount,
                                           double lineThickness = LineThickness) =>
        beamCount < 4
            ? (2.0 * lengthFraction + lineThickness * lengthFraction - beamThickness) / 2.0
            : (3.0 * lengthFraction + lineThickness * lengthFraction - beamThickness) / 3.0;
    /// <summary>Length of a beamlet (partial beam).</summary>
    // LILYPOND-REF: scm/define-grobs.scm Beam (beamlet-default-length . (1.1 . 1.1)) —
    public const double BeamletLength = 1.1;
    /// <summary>A beamlet may not eat more than this share of the gap to the next stem.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3432 beamlet-max-length-proportion — read by
    ///   lily/beam.cc:607-622 calc_beam_segments (max_proportion), which caps the stub at
    ///   <c>|neighbour_stem_x − stem_x| × proportion</c>.
    /// It lives here because both sides must agree on it: the quanter measures a beam's
    /// ink against a collision at that stub's x, and the renderer draws the stub.
    /// </remarks>
    public const double BeamletMaxLengthProportion = 0.75;

    // === Barlines ===
    // All barline metrics scale with line-thickness, mirroring LilyPond.

    /// <summary>Thin barline: hair-thickness 1.9 × line-thickness = 0.19.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine (hair-thickness . 1.9).</remarks>
    public const double ThinBarlineThickness = 1.9 * LineThickness;

    /// <summary>Thick barline: thick-thickness 6.0 × line-thickness = 0.6.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm BarLine (thick-thickness . 6.0).</remarks>
    public const double ThickBarlineThickness = 6.0 * LineThickness;

    /// <summary>
    /// Ink gap between the segments of a compound barline:
    /// kern 3.0 × line-thickness = 0.3.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm BarLine (kern . 3.0);
    /// scm/bar-line.scm:766-801 — compound bars stack their glyph stencils
    /// with spacing <c>kern</c> between every pair.
    /// </remarks>
    public const double BarlineSeparation = 3.0 * LineThickness;

    /// <summary>
    /// Gap between the repeat dots and the adjacent bar segment — the same
    /// kern as between line segments (the colon is just another glyph in the
    /// compound bar).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:766-801.</remarks>
    public const double RepeatBarlineDotSeparation = BarlineSeparation;

    // === Other elements ===
    // (Hairpins draw at StaffLineThickness — LP Hairpin (thickness . 1.0)
    //  × line-thickness; the former unused HairpinThickness 0.16 is gone.)

    /// <summary>Tuplet bracket: thickness 1.6 × line-thickness = 0.16.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm TupletBracket (thickness . 1.6).</remarks>
    public const double TupletBracketThickness = 1.6 * LineThickness;

    /// <summary>Multi-measure-rest block bar thickness: thick-thickness 6.6 ×
    /// line-thickness = 0.66 staff space.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest
    /// (thick-thickness . 6.6); lily/multi-measure-rest.cc:203 big_rest
    /// <c>y = line-thickness·thick-thickness·ss / 2</c> (full height = 2y).</remarks>
    public const double MultiMeasureRestThickThickness = 6.6 * LineThickness;

    /// <summary>Multi-measure-rest end serifs: hair-thickness 2.0 ×
    /// line-thickness = 0.2 staff space.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm MultiMeasureRest
    /// (hair-thickness . 2.0); lily/multi-measure-rest.cc:204
    /// <c>ythick = hair-thickness·line-thickness·ss</c>.</remarks>
    public const double MultiMeasureRestHairThickness = 2.0 * LineThickness;

    // LILYPOND-REF: scm/lily-library.scm (magstep s) = 2^(s/6); magstep(-3) = 2^(-1/2) ≈ 0.7071.
    /// <summary>
    /// Ossia staff scale: magstep(-3) = 2^(-3/6) ≈ 0.707 — the LP ossia
    /// convention (fontSize = -3 with StaffSymbol.staff-space = magstep -3,
    /// NR "Ossia staves"). Shared by the layouter (reserved heights and gaps)
    /// and the renderer (drawing scale) so the two cannot drift apart and
    /// leave phantom whitespace under the drawn ossia.
    /// </summary>
    public const double OssiaScale = 0.7071;

    /// <summary>
    /// The <c>font-size</c> a CUE grob states — LilyPond's CueVoice number.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly CueVoice (at :429-444 in 2.26.0) declares
    ///   <c>fontSize = #-4</c>, and with it <c>Stem.length-fraction = (magstep -4)</c>,
    ///   <c>Beam.length-fraction = (magstep -4)</c> and <c>Beam.beam-thickness = 0.35</c>
    ///   (DECLARED, like the grace beam's 0.384 — not derived from the fraction).
    ///   ⚠️ The line range is in prose on purpose: none of the names at those lines is a
    ///   multi-part LilyPond identifier, so the citation ratchet cannot check it.
    /// <para>
    /// This is the number a cue grob STATES; everything else about its size follows from it —
    /// which Emmentaler design its glyphs come out of (<see cref="CueDesignSize"/>) and the
    /// magnification that design is read at (<see cref="CueScale"/>). It is the same shape
    /// <see cref="Svg.Model.GraceNoteItem.FontSizeStep"/> has, one grob family over.
    /// </para>
    /// </remarks>
    public const double CueFontSizeStep = -4.0;

    /// <summary>
    /// What a cue's font-size multiplies a length by — <c>magstep(-4)</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/lily-library.scm <c>magstep</c> = <c>2^(s/6)</c>, over
    ///   <see cref="CueFontSizeStep"/>, so the scale is <c>magstep(-4)</c> = <c>2^(-2/3)</c>.
    /// <para>
    /// ⚠️ IT WAS A HAND-WRITTEN 0.66 UNTIL 2026-08-03, declared LILYSHARP-OWN, and the
    /// comments that reached it said "font-size −4 ≈ 0.66" — arithmetic, and wrong by 4.8%
    /// against 0.629961. That was the FOURTH time this exact shape turned up: a grace was
    /// "≈0.65" against 0.707107, an ossia "0.7071" against 0.70710678, an editorial
    /// accidental "0.7937" against 0.79370053. <see cref="Layout.EmmentalerDesignSize.Magstep"/>
    /// is the one home for all of them.
    /// </para>
    /// <para>
    /// ⚠️ THE SCALE IS ONLY HALF OF WHAT A FONT-SIZE DECIDES, and the rounding hid the other
    /// half: −4 asks for 12.599pt, which lands on the THIRTEEN Emmentaler design, so a cue
    /// glyph is that design's own outline and not the twenty's shrunk. This factor stays
    /// because it is still one of the two terms — it is what <c>GlyphMetrics.AtFontSize</c>
    /// multiplies the chosen design's table by. See <see cref="CueFont"/>.
    /// </para>
    /// </remarks>
    public static readonly double CueScale = Layout.EmmentalerDesignSize.Magstep(CueFontSizeStep);

    /// <summary>
    /// The FONT a cue grob reads its glyph dimensions from: the design
    /// <see cref="CueFontSizeStep"/> selects, already magnified into the page's staff spaces.
    /// Nothing read out of it is multiplied again.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — one call answers both halves,
    ///   WHICH file and at WHAT magnification, and hands back a font that has applied the
    ///   second (lily/modified-font-metric.cc:62-68 get_indexed_char_dimensions). The same
    ///   pairing <see cref="Svg.Model.GraceNoteItem.Font"/> takes, one grob family over.
    /// </remarks>
    internal static Layout.GlyphMetrics.DesignMetrics CueFont
        => Layout.GlyphMetrics.AtFontSize(CueFontSizeStep);

    /// <summary>
    /// The Emmentaler design a cue is DRAWN from — the number a drawing context's music-face
    /// scope takes.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS: this and <see cref="CueFont"/> must come from the same
    /// <see cref="CueFontSizeStep"/>, or the box a cue column reserves stops being the box its
    /// glyph fills.
    /// </remarks>
    internal static int CueDesignSize
        => Layout.EmmentalerDesignSize.ForFontSizeStep(CueFontSizeStep).Rounded;

    /// <summary>
    /// The stem parameters a CUE note is measured and drawn with: the ordinary
    /// <see cref="Layout.StemDetails.Default"/> table with LilyPond's own
    /// <c>length-fraction</c> for a cue on it. Nothing else about a stem scales — the table
    /// entries, the shortening and the middle-line rule are all the plain ones.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly CueVoice (at :429-444 in 2.26.0) declares
    ///   <c>\override Stem.length-fraction = #(magstep -4)</c> — the SAME magstep the font
    ///   size takes, which is why this reads <see cref="CueScale"/> rather than restating it.
    ///   ⚠️ The line range is in prose for the reason <see cref="CueFontSizeStep"/> gives.
    /// LILYPOND-REF: lily/stem.cc:557 <c>length *= length-fraction</c> — applied AFTER the
    ///   unnatural-direction shortening at :519-555 and BEFORE the middle-line rule at
    ///   :591-593, which is the order <see cref="Layout.StemCalculator.CalculateStemEndY"/>
    ///   already runs in.
    /// <para>
    /// MEASURED, NOT READ OFF THE SOURCE (probe voice-boundary-spacing.ly section E, session
    /// 84): a cue quarter on the middle line runs <c>6.666666666666667 × magstep(-4) =
    /// 4.199736832982911</c> half-spaces, equal to LilyPond's dumped <c>length</c> as doubles.
    /// The two notes OFF the middle line cannot show the law at all, because there the
    /// middle-line rule is what sets the end — which is the trap this fraction walks into if
    /// it is applied without that rule (cue stems would come out 0.59 half-spaces short on
    /// exactly the register cues are written in).
    /// </para>
    /// <para>
    /// ⚠️ FLAGGED CUES ARE NOT CLOSED BY THIS. An eighth's stem is lengthened to carry its
    /// flag and the flag scales by its own font size, so the two terms are not one product:
    /// the same probe reads 4.039985 where this fraction gives 4.252234. The fraction still
    /// moves an eighth much nearer than full size did (6.750000); it is not exact, and the
    /// flag term wants its own measurement. ⚠️ BEAMED cues are untouched: a beam's stems come
    /// from <see cref="Layout.BeamScoringProblem"/>, whose <c>lengthFraction</c> only the
    /// grace and tab callers pass today.
    /// </para>
    /// </remarks>
    internal static readonly Layout.StemDetails CueStemDetails =
        Layout.StemDetails.Default with { LengthFraction = CueScale };

    // Rest collision avoidance.
    // ⚠️ FOUR CONSTANTS HERE REPORTED AS SOURCED UNTIL 2026-08-03 AND NEVER WERE (the
    // provenance ratchet looks back a fixed number of lines for a marker and read
    // CueScale's LILYSHARP-OWN line as theirs). Three of them — a symmetric ±2-position
    // rest box, a 1-position beam clearance, a 0.1 apply-threshold — died on 2026-08-06
    // when the rest-under-beam shift got LilyPond's real inputs (the rest glyph's own
    // extent, Rest.minimum-distance below; LP regression beam-rest-extreme.ly read
    // +2.0 ss where LP prints +3.0 under every raised beam).
    // LILYPOND-REF: lily/rest.cc staff_position_internal — the middle line IS a
    // non-semibreve rest's default origin, so the VALUE is LilyPond's (the label here
    // was LILYSHARP-OWN until session 158's §5.2 audit; it hid that source).
    // ⚠️ What is NOT ported is the semibreve exception: the beam covered-grob booking
    // that reads this applies it to EVERY rest including semibreves, which LP hangs one
    // line higher.
    /// <summary>Default staff position for rest center (middle line).</summary>
    public const double RestCenterPosition = 0.0;

    /// <summary>Clearance a beamed rest keeps from the beam face, in staff spaces.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm Rest (minimum-distance . 0.25) — consumed by
    /// lily/beam.cc:1393-1396 rest_collision_callback as <c>ss * (stemlet-length +
    /// Rest.minimum-distance)</c>, with stemlet-length 0 by default. Distinct from
    /// RestCollision.minimum-distance (0.75), the BETWEEN-VOICES clearance.
    /// </remarks>
    public const double RestMinimumDistance = 0.25;

    /// <summary>Horizontal flare (staff spaces) of a piano-pedal bracket's edge at a
    /// pedal change; two abutting flares form the "/\" notch.
    /// LILYPOND-REF: scm/define-grobs.scm PianoPedalBracket bracket-flare = (0.5 . 0.5).</summary>
    public const double PedalBracketFlare = 0.5;

    // === Flags ===
    // ⚠️ LILYSHARP-OWN: A NOMINAL BOX, AND LILYPOND HAS NO COUNTERPART TO IT. A Flag's
    //   extent there IS its stencil's — lily/flag.cc:49-67 Flag::width returns
    //   `sten->extent (X_AXIS)` off the glyph lily/flag.cc:69-83 Flag::glyph_name selected —
    //   so there is no width, no base height and no per-level increment to cite. These three
    //   are Lily#'s own, and the ONLY thing that reads them is the reservation
    //   (SkylineBuilder's flag box); the DRAWN flag goes through GlyphMetrics, which is the
    //   two-spellings shape HANDOFF 7.7 names.
    // ⚠️ MEASURED against our own extracted Emmentaler metrics (design 20, staff spaces), so
    //   the size of the disagreement is on the record rather than merely suspected:
    //     eighth up    box 1.2 x 2.5   glyph 0.828200 x 3.115200  (+0.372 wide, -0.615 short)
    //     eighth down  box 1.2 x 2.5   glyph 1.066800 x 2.905200  (+0.133 wide, -0.405 short)
    //     16th up      box 1.2 x 3.0   glyph 0.828200 x 3.615200  (          , -0.615 short)
    //     16th down    box 1.2 x 3.0   glyph 1.066800 x 3.115200  (          , -0.115 short)
    //   The reservation is WIDER and SHORTER than the ink in every case, and it is not even
    //   the same shape: the real glyph differs up against down, this box does not.
    // ⚠️ CLOSING IT IS NOT A PROVENANCE FIX. Seeding GlyphMetrics.Flag* here moves the column
    //   reach on every flagged note, so it changes spacing as well as reserved height — its
    //   own island with its own snapshot approval. Recorded, not done.
    /// <summary>Reserved width of a flag (staff spaces) — Lily#'s nominal box, see above.</summary>
    public const double FlagWidth = 1.2;
    /// <summary>Reserved height of an eighth-note flag (staff spaces) — nominal, see above.</summary>
    public const double FlagBaseHeight = 2.5;
    /// <summary>Reserved height added per further flag level — nominal, see above.</summary>
    public const double FlagHeightIncrement = 0.5;

    /// <summary>
    /// The gap between a system's left edge and the clef glyph's drawing origin — the
    /// LeftEdge → Clef break-align spacing.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm LeftEdge.space-alist (clef . (extra-space . 0.8)),
    /// with LeftEdge's X-extent (0 . 0) so the extra-space ideal is 0 + 0.8 = 0.8. This is
    /// the first gap the line-start prefix opens (LeftEdge is the origin of break-alignment),
    /// so a line-start clef's ink sits 0.8 ss in. Measured on 2.26.0: every line-start clef
    /// anchor is at 0.8. Was an invented 0.3 (LILYSHARP-OWN), which sat the clef 0.5 too far
    /// left and, because CalculatePrefixWidth did NOT reserve it, left the first note short by
    /// the same 0.8 (ledger line-start.clef-to-first-note). It lives here because THREE readers
    /// must agree on it — SharedRenderer.DrawClef draws the glyph at this offset, SkylineBuilder
    /// seeds the clef ink there, and BreakAlignSpacing.CalculatePrefixWidth reserves it as the
    /// prefix's leading distance — and one constant is how they stay agreed.
    /// </remarks>
    public const double ClefGlyphXOffset = 0.8;

    /// <summary>
    /// The em size a lyric syllable is set at, in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2213-2230 <c>LyricText</c>, whose self-alignment-X is <c>left-align-at-split-notes</c> and which declares <c>(font-size . 1.0)</c> at :2220.
    /// The size that is off is the paper's: LILYPOND-REF scm/paper.scm:69-77 sets <c>text-font-size</c> to <c>11 * (staff-height / 20pt)</c> and <c>output-scale</c> to <c>staff-height / 4</c>,
    /// so on the default staff it is 11pt against a 5pt staff space = <b>2.2 ss</b>
    /// (scm/define-paper-variables.scm:548-550 documents the same <c>text-font-size</c> rule as <c>staff-height / 20 * 11</c>).
    /// LILYPOND-REF scm/lily-library.scm <c>magstep</c> is <c>exp((s/6) * log 2)</c> = 2^(s/6).
    /// Hence 2.2 * 2^(1/6).
    /// ⚠️ THE ADDRESS WAS WRONG WHEN THIS WAS WRITTEN and is corrected here: it said
    /// <c>ly/paper-defaults-init.ly</c>, which does not mention text-font-size at all — the
    /// claim was copied from <c>BarNumberEngraver.FontSize</c>'s comment rather than read
    /// (HANDOFF 5.2.1①). The VALUE was right; only the citation was not.
    /// <para>
    /// ⚠️ IT WAS 3.2, i.e. 29.6% TOO LARGE, and the ledger had it recorded as a FONT
    /// DIFFERENCE that must never be closed ("Lily#'s lyric face is about 27% bigger than
    /// LilyPond's"). It was not a face difference at all: measured 2026-07-28, the bundled
    /// face's own ink for the syllable "no" at THIS size is 1.187789 against LilyPond's
    /// measured 1.187880 — 0.000091 apart — where at 3.2 it reads 1.539200. That one
    /// mis-sourced number is the +0.271310 that appeared in nine ledger entries.
    /// </para>
    /// <para>
    /// It lives here because BOTH sides must agree on it: <c>LyricEngraver</c> reserves the
    /// syllable's ink and advance with it and <c>SharedRenderer.DrawLyrics</c> draws with it.
    /// They used to hold two copies (3.2 and <c>FontSize * 0.8</c>), which is the split this
    /// file exists to prevent.
    /// </para>
    /// </remarks>
    public static readonly double LyricTextFontSize = 2.2 * Math.Pow(2.0, 1.0 / 6.0);

    /// <summary>
    /// The em size a chord symbol is set at, in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-846 <c>ChordName</c>, which declares <c>font-family . sans</c> and <c>(font-size . 1.5)</c>, so the size is 2.2 * magstep(1.5) = 2.2 * 2^(1.5/6).
    /// The 2.2 and the magstep are the paper's; see <see cref="LyricTextFontSize"/>, which
    /// carries both addresses.
    /// <para>
    /// ⚠️ IT WAS 2.6, declared LILYSHARP-OWN with LilyPond's own rule quoted right beside it —
    /// an approximation of the number that rule works out to (2.616256), 0.62% low, kept in
    /// TWO homes (<c>ChordNameEngraver.ChordFontSize</c> and the renderer's
    /// <c>FontSize * 0.65</c>). Derived here so both read it and neither approximates.
    /// </para>
    /// <para>
    /// The point the WEIGHT wanted before it could move now exists:
    /// <c>chord.symbol-width.minor-pair-gap</c> measured the bold "Am" 0.262120 wider than
    /// LilyPond's regular one, which is what <see cref="ChordNameFontStyle"/> closed.
    /// </para>
    /// </remarks>
    public static readonly double ChordNameFontSize = 2.2 * Math.Pow(2.0, 1.5 / 6.0);

    /// <summary>
    /// The em size a text script (<c>_"..."</c> — expression text like "molto rit.") is
    /// set at, in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3800-3833 <c>TextScript</c> (the block that declares its <c>outside-staff-priority</c>) declares NO <c>font-size</c>,
    /// so a script is set at the paper's own text size: LILYPOND-REF scm/paper.scm:69-77 <c>text-font-size</c> = 11 * (staff-height / 20pt),
    /// i.e. 11pt against a 5pt staff space = <b>2.2 ss</b>, with no magstep on top (see
    /// <see cref="LyricTextFontSize"/>, which carries the same paper addresses plus its
    /// grob's own <c>font-size 1.0</c>).
    /// <para>
    /// ⚠️ IT WAS <c>FontSize * 0.6</c> = 2.4, a Lily#-own em 9.1% large — the third member
    /// of the family the lyric 3.2 and the chord 2.6 belonged to (HANDOFF 5.0: size and
    /// metric source are two halves of one claim). Confirmed against the dedicated pair
    /// (audit/lp-geometry/probes/textscript-ink.ly): LilyPond's measured inks for
    /// "poco" / "dolce" / "mum" divide by the bundled italic face's per-em inks to
    /// 2.200149 / 2.200054 / 2.200074 / 2.200667 — four independent readings of 2.2.
    /// </para>
    /// <para>
    /// It lives here because both sides must agree on it:
    /// <c>SharedRenderer.DrawCustomTexts</c> draws with it and
    /// <c>OutsideStaffStacker.PlaceCustomTexts</c> reserves the string's own ink at it.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: scm/paper.scm:69-77 text-font-size — 11 * (staff-height/20pt), i.e.
    // 11pt over a 5pt staff space; scm/define-grobs.scm:3800-3833 TextScript (the outside-staff-priority
    // block) declares no font-size of its own, so the paper size applies unstepped.
    public static readonly double TextScriptFontSize = 11.0 / 5.0;

    /// <summary>
    /// The em a bass figure is set at, in staff spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm:468-470 <c>format-bass-figure</c> ends
    /// <c>(make-fontsize-markup -5 fig-markup)</c> — every figure carries an explicit
    /// font-size step of −5, which is why <c>BassFigure</c> itself declares none
    /// (scm/define-grobs.scm:352-364 declares only <c>font-features</c>).
    /// LILYPOND-REF: lily/font-select.cc:99-117 <c>select_font</c> — for the <c>fetaText</c>
    /// encoding the base size is <b><c>staff-height</c></b>, NOT <c>text-font-size</c>
    /// (that branch is latin1's), and the requested size is
    /// <c>base * 2^(font-size / 6)</c>. A `\number` markup at font-size 0 is therefore set
    /// at the MUSIC em — 20pt over a 5pt staff space = 4 ss — and a bass figure at 4 ss ×
    /// magstep(−5).
    /// <para>
    /// ⚠️ SO THE NUMBER FACE IS NOT ON THE TEXT LADDER AT ALL. The handoff's chain guess was
    /// that a figure is "that face at font-size 0" = the paper's 2.2 ss
    /// (<see cref="LyricTextFontSize"/>'s base); it is not, and the two differ by 2%, an
    /// order of magnitude more than the Pango-quantisation residual the corpus reads. The
    /// same branch is what makes a numeric TIME signature's digit 2 ss tall from the very
    /// same glyph family at font-size 0 — the ratio between the two is the magstep, not a
    /// difference of face.
    /// </para>
    /// <para>
    /// It lives here because both sides must agree on it (the same reason
    /// <see cref="LyricTextFontSize"/> does): <c>FiguredBassEngraver</c> reserves the row's
    /// ink at this em and <c>SharedRenderer.DrawFiguredBass</c> draws at it.
    /// ⚠️ IT WAS <c>FontSize * 0.75</c> = 3.0 with a serif face — 34% large AND the wrong
    /// font — which is the +0.375204764 that stood under every figured-bass ledger point.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: scm/translation-functions.scm:468-470 format-bass-figure —
    // (make-fontsize-markup -5 fig-markup); lily/font-select.cc:99-117 select_font — for
    // fetaText the base size is staff-height (4 staff spaces), stepped by 2^(font-size/6).
    // ⚠️ THE 4.0 IS THE DEFAULT STAFF'S HEIGHT, WRITTEN AS A CONSTANT where LilyPond LOOKS IT
    // UP (layout->lookup_variable ("staff-height") at :104-106). It is the §5 "staff extent
    // written as a constant" family: a magnified or ossia staff scales its music font and
    // this em would not follow. Lily# does not scale a figure for an ossia today either
    // (SharedRenderer.DrawFiguredBass passes no OssiaShrink size), so the two halves are at
    // least consistent — and both close from StaffLayout, with the rest of that family.
    public static readonly double FiguredBassFontSize = 4.0 * Math.Pow(2.0, -5.0 / 6.0);

    /// <summary>
    /// The chord symbol's font series: REGULAR, in one home the reserving engraver, the
    /// spacing rules and the renderer all read.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:837-855 <c>ChordName</c> (the block that declares its <c>extra-spacing-width</c>) — it declares
    /// <c>font-family . sans</c> and <c>font-size . 1.5</c> and NO <c>font-series</c> at all,
    /// so the series is the default regular one.
    /// <para>
    /// ⚠️ IT WAS BOLD (<c>TextFontMetrics.SansBold</c>) everywhere, a Lily#-own choice with
    /// no LilyPond source, and it was invisible to the corpus until 2026-07-29 because every
    /// chord point was an anchor difference in which the symbol's width cancels. The
    /// dedicated pair (audit/lp-geometry/probes/chord-symbol-width.ly,
    /// <c>chord.symbol-width.minor-pair-gap</c>) measured LilyPond's exts as Nimbus Sans
    /// REGULAR advances per glyph ("Am" 3.926480 where bold would be 4.188600 at the stale
    /// em) — a per-glyph difference, not a scale, so it was the series and not the size.
    /// </para>
    /// </remarks>
    public const LilySharp.Core.Rendering.FontStyle ChordNameFontStyle =
        LilySharp.Core.Rendering.FontStyle.Regular;

    // === Notehead dimensions ===
    // Aliases to the auto-extracted GlyphMetrics constants (Emmentaler advance widths).
    // Existing call sites use these names; new code should prefer GlyphMetrics directly.
    public const double NoteheadWholeWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadWholeAdvance;
    public const double NoteheadHalfWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadHalfAdvance;
    public const double NoteheadBlackWidth = LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadBlackAdvance;
    /// <summary>Double-whole notehead is hand-tuned (no glyph in extracted set).</summary>
    /// <remarks>
    /// LILYSHARP-OWN: hand-tuned, and the only width on this list that is. ⚠️ NOT because
    /// LilyPond lacks the glyph — it has it (mf/feta-noteheads.mf:240,
    /// <c>fet_beginchar ("brevis notehead", "sM1")</c>) — but because
    /// Extract-EmmentalerMetrics.py does not emit it, so GlyphMetricsGenerated has
    /// <c>RestDoubleWhole*</c> and no notehead counterpart. This number has therefore never
    /// been checked against the font at all; the neighbours above it are advance widths read
    /// out of Emmentaler. Closing it is extractor work, not measurement work.
    /// </remarks>
    public const double NoteheadDoubleWholeWidth = 2.296;

    // === Stem attachment points ===
    // The stem centre is shifted by -dir*thickness/2 so the stem EDGE (not its
    // centre) sits on the notehead boundary: an up-stem's right edge aligns with
    // the notehead's right edge, a down-stem's left edge with the notehead's left
    // edge. Without this the half-thickness pokes outside the notehead.
    // LILYPOND-REF: lily/stem.cc internal_calc_stem_offset_from_head
    //   (r += -d * rule_thick * 0.5).
    // The ±0.168 Y is the black notehead's stem-attachment vertical offset, from the
    // feta font metrics that back ly:note-head::calc-stem-attachment (define-grobs.scm:2608
    // NoteHead.stem-attachment; NoteHead grob @2595); an up-stem attaches slightly above centre, a down-stem below.
    // ⚠️ THE ATTACHMENT POINT, NOT THE ADVANCE. It read NoteheadBlackWidth (the hmtx
    // advance, 1.304000) until 2026-08-02; LilyPond reads the font's own attachment
    // coordinate, 1.304200 — see LayoutUtilities.StemAttachX, which is the house every
    // caller goes through, and audit/lp-geometry/probes/beam-stem-x.ly (1.2392, not 1.2390).
    // ⚠️ AND IT IS THE BLACK HEAD'S, WHICH IS NOT EVERY HEAD'S. Since 2026-08-03
    // LayoutUtilities.StemAttachX picks the attachment per head shape (a half head's is
    // 1.377400), so this constant is that house's answer for a QUARTER at scale 1 and nothing
    // wider. Nothing computes with it — it survives as the documented black-head reading, and
    // a caller that needs "where does this stem stand" must go through the house.
    public static readonly double StemUpAttachX =
        LilySharp.Core.Svg.Layout.GlyphMetrics.NoteheadBlackStemAttachment.X
        - StemThickness / 2;
    public const double StemDownAttachX = StemThickness / 2;
    // ⚠️ StemUpAttachY = 0.168 and StemDownAttachY = -0.168 stood here with no explanation
    //   and no reader. Deleted 2026-08-04 rather than given one: HANDOFF 5.2.1⑥ says an
    //   argument a port stopped using is dropped where it is found, because leaving it is
    //   misinformation for whoever reads next — and inventing provenance for a number
    //   nothing computes with would have been worse than the silence it replaced.

    /// <summary>
    /// Horizontal shift of a TAB fret number from its note column (the notehead's
    /// LEFT edge) to the notehead CENTRE. A notation notehead is left-anchored at
    /// the column while a fret digit is centre-anchored, so without this the digit
    /// (and its stem) sit a notehead-width left of the companion notation staff's
    /// note. Shifting the digit here — and drawing the tab stem at the notation
    /// StemUpAttachX/StemDownAttachX — lines the two staves' stems up on one x while
    /// keeping the stem attached to the (narrower) digit.
    /// </summary>
    public const double TabHeadCenterOffset = NoteheadBlackWidth / 2;

    /// <summary>Distance between the two stem-line centres of a tab half note's
    /// double stem (staff spaces).</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/tablature.scm:97-111 tabvoice::make-double-stem-width-for-half-notes
    /// — the Stem's X-extent widens to
    /// single-stem-width + `double-stem-separation`, whose fallback is 0.5 (:107), and the
    /// draw callback puts one stem line at each edge of that extent. Measured on 2.26.0:
    /// centres exactly 0.5 apart (tablature-double-stem-tremolo twin). Until 2026-08-09 this
    /// was 0.355, a pasted measurement of unknown provenance — the debt its drawing site
    /// carried as a ⚠️ since 2026-07-28.
    /// </remarks>
    public const double TabDoubleStemSeparation = 0.5;


    // === Staff geometry (local engraver coordinates) ===
    /// <summary>Device-Y of the staff's middle line in the engravers' local
    /// staff-space frame (top line = 0, bottom line = 4). A notehead's local Y is
    /// <c>StaffMiddle - staffPosition / 2</c> (staffPosition is half-spaces from the
    /// middle line, LilyPond convention). Use this instead of a bare <c>2.0</c>.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: a frame, not a quantity. LilyPond has no counterpart to look up
    /// because it never needs one — its staff positions ARE the vertical coordinate, while
    /// the engravers here work in a local top-line-is-zero frame and have to say where the
    /// middle line sits in it. The 2.0 is "half of a five-line staff's four spaces", which
    /// is the frame's definition rather than a number anyone chose. HANDOFF's rule about
    /// naming the frame applies: this constant exists so that the conversion is spelled once
    /// instead of appearing as a bare 2.0 in fourteen call sites.
    /// </remarks>
    public const double StaffMiddle = 2.0;

    // === Notehead collision ===
    // ⚠️ LILYSHARP-OWN: A NOMINAL HALF-HEAD, NOT THE GLYPH'S. LilyPond has no such constant —
    //   a NoteHead's Y-extent is its stencil's, so what corresponds to this is the drawn
    //   head's own half extent, which our extraction reads as 0.545000 for BOTH the black and
    //   the half head (GlyphMetrics.NoteheadBlack / NoteheadHalf, design 20). This 0.5 is
    //   therefore 0.045 short of the ink, and it is the ROUND number that gives it away: a
    //   half-space is the staff's frame, not a font's measurement.
    // ⚠️ THE ARTICULATION SUPPORT NO LONGER READS IT (2026-08-05). That was the island this
    //   note called "recorded, not done": NoteColumnLayout's whole-note/breve branch and the
    //   ArticulationEngraver stem-away branches now take the head's own extent, permitted by
    //   the ledger pair script.{staccato,marcato}-below.staff-to-ink-top (+0.045000 each
    //   against LilyPond's single -4.745000, which decomposes as the whole head's ink bottom
    //   less the script's padding 0.2). The 0.045 in the paragraph above is that residual.
    // ⚠️ THREE READERS ARE LEFT, AND THEY ARE THREE DIFFERENT CLAIMS — do not "finish the
    //   job" by pointing them at a head box without opening a point for each:
    //   * ArticulationEngraver.HeadSupportExtent's REST branch — LilyPond's support there is
    //     the Rest's from-stencil outline, a different glyph and a different box per duration.
    //   * FingeringEngraver — the same side-position claim as the scripts, but over a model
    //     that has no glyph near-extent and no padding of its own, so correcting this one term
    //     would be fitting inside a formula that is not yet LilyPond's. No point observes it.
    //   * ElementCoordinator's rest-shift collision, which doubles it into staff positions.
    //     ⚠️ Its comment says "head ink is +-0.545" and then computes +-0.5; the two halves of
    //     that line disagree today. Naming it here rather than fixing it blind: the rest-shift
    //     island has its own books.
    /// <summary>Nominal half-height of a notehead for collision/support (staff spaces).</summary>
    public const double NoteheadHalfHeight = 0.5;
    // ⚠️ NoteheadHeight = 1.0 stood here with nothing reading it. HANDOFF 5.2.1⑥ recorded
    //   why in 2026-07: the vertical skyline moved to the LILC ink in 498d2501, and this
    //   survived as an unused argument threaded through five signatures — which is exactly
    //   how it kept being diagnosed as live. Deleted 2026-08-04.

    // === Rest dimensions ===
    // ⚠️ LILYSHARP-OWN: ONE NOMINAL BOX FOR EVERY REST, WHICH LILYPOND DOES NOT HAVE. There a
    //   Rest's extent is its stencil's, and the stencil is the glyph the duration selects, so
    //   the box differs per rest in both size AND placement. The pair below is a 1.0 x 1.0
    //   square centred on the middle line, used for every duration alike
    //   (SkylineBuilder's rest seed is the only reader).
    // ⚠️ MEASURED against our own extracted Emmentaler metrics (design 20, staff spaces), so
    //   the disagreement is on the record rather than merely suspected:
    //     quarter  box 1.0 x 1.0   glyph 0.950000 x 2.812400   (-1.812 SHORT: the deepest one)
    //     whole    box 1.0 x 1.0   glyph 1.500000 x 0.625000   (+0.500 narrow, +0.375 tall)
    //     half     box 1.0 x 1.0   glyph 1.500000 x 0.625000   (+0.500 narrow, +0.375 tall)
    //   ...and the CENTRING is wrong in kind, not only in size: a whole rest hangs BELOW its
    //   line (RestWhole is (0 . -0.625)..(1.5 . 0)) and a half rest sits ON one
    //   (RestHalf is (0 . 0)..(1.5 . 0.625)), while this box straddles the middle either way.
    // ⚠️ AND YET CORRECTING IT MOVES NOTHING, which is the part that has to be written down
    //   because it is not what the sizes above suggest. MEASURED 2026-08-04: setting the pair
    //   to the real quarter-rest 2.812400 x 0.950000 leaves the whole suite green — 4009
    //   tests, 657 SVG snapshots, not one byte. The reason is geometric and complete: the box
    //   is CENTRED on the middle line, so it reaches H/2 either way, while the staff symbol's
    //   own ink already reaches 2.05 (SeedStaffSymbol). Nothing under H = 4.1 can ever show,
    //   and no ordinary rest comes close — the deepest real glyph reaches 1.25 below the
    //   middle and 1.5624 above. A rest is INSIDE its staff, so the staff swallows the error.
    // ⚠️ THE POSITIVE CONTROL SAYS THE SUITE IS NOT BLIND: at H = 20 sixty-two tests fail. The
    //   silence at the real values is the geometry, not a missing observer (HANDOFF 5.0).
    // ⚠️ SO THE ISLAND IS NOT "EVERY BOOK WITH A REST" — an earlier draft of this remark said
    //   it was, and that sentence was written without measuring. What the error can reach is
    //   only a rest that LEAVES its staff (a staff-position override, a multi-voice shift),
    //   and there the SECOND half of the defect governs anyway: this seed is centred on the
    //   middle line unconditionally and does not read the rest's position at all, so in
    //   exactly the case where the size would matter, the placement is wrong too.
    /// <summary>Nominal rest height for the skyline seed (staff spaces).</summary>
    public const double RestHeight = 1.0;
    /// <summary>Nominal rest width for the skyline seed (staff spaces).</summary>
    public const double RestWidth = 1.0;

    // === Paper column ===
    /// <summary>
    /// Width of the extent a grob aligns to on a paper column that carries no rhythmic grob at
    /// all — "as wide as a note head". The extent itself is <c>(0 . 1.35)</c>, so the point a
    /// CENTER-aligned grob takes is half of this.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2749-2750 — PaperColumn
    /// <c>X-alignment-extent = (0 . 1.35)</c>, used by
    /// lily/self-alignment-interface.cc:121-139 when the column's note-column extent is empty.
    /// LilyPond's own comment there names the case: "This situation happens for lyrics without
    /// `associatedVoice`, for example."
    /// MEASURED (audit/lp-geometry/probes/staffless-system.ly, scores CLI and CLA): a staff-less
    /// syllable's ink centre stands 0.675000 = 1.35/2 right of its column, the same for two
    /// syllables of different widths.
    /// </remarks>
    public const double PaperColumnXAlignmentExtentWidth = 1.35;

    // === Dots ===
    // ⚠️ LILYSHARP-OWN, AND LILYPOND'S ANSWER IS MEASURABLE THOUGH NOT DECLARED. There is no
    //   dot-gap constant to cite because both gaps are the DOT'S OWN WIDTH, computed:
    //     scm/output-lib.scm:686-690 ly:dots::print stacks a note's dots with
    //       `padding = (interval-length (ly:stencil-extent dot-stencil X))` — the gap BETWEEN
    //       consecutive dots;
    //     scm/output-lib.scm:692-704 dot-column-interface::pad-by-one-dot-width returns the
    //       max dot width as DotColumn's side-position padding (scm/define-grobs.scm:1263) —
    //       the gap from the SUPPORT.
    //   One quantity in two roles, which is exactly how Lily# uses this constant
    //   (ElementCoordinator lays the dots out as `DotGap + d * (width + DotGap)`).
    // ⚠️ MEASURED: that width is 0.450000 on 2.26.0 — asked of LilyPond directly
    //   (`dots.dot` X extent (0 . 0.45)) and matching our own extraction
    //   (GlyphMetrics.AugmentationDot). So the LilyPond padding term is 0.45 and this is 0.3,
    //   0.15 short in both roles.
    // ⚠️ NOT CHANGED HERE. The number feeds the drawn dot X, the column's horizontal skyline
    //   (ItemSkylineFactory) and the spacing rules, so moving it changes ink AND spacing on
    //   every dotted note in the corpus — an island with snapshot approval, and one that
    //   wants a ledger point rather than a bare re-base. Recorded, not done.
    /// <summary>Gap from the notehead to its first augmentation dot, and between dots
    /// (staff spaces). Lily#'s own; see the measurement above.</summary>
    public const double DotGap = 0.3;

    // === Repeat dots ===
    // LILYPOND-REF: scm/bar-line.scm:296-368 make-colon-bar-line — LilyPond does not
    //   declare dot positions. It folds the staff about its centre, finds the first space
    //   wide enough for a dot plus a staff line, and translates one dot to each side of the
    //   centre by half that span (`(* dist (/ staff-space 2))`). The constant below is that
    //   procedure's answer for the ordinary five-line staff.
    // ⚠️ MEASURED, NOT DERIVED (2026-08-04). Calling the procedure on 2.26.0 and reading the
    //   stencil it returns gives a Y extent of (-0.725 . 0.725) with a dot half-height of
    //   0.225 — i.e. dot centres at exactly -0.5 and +0.5 staff spaces from the staff
    //   CENTRE. Walking the folded-staff `any` by hand gives the same answer, but only
    //   measuring says the walk was read right.
    // ⚠️⚠️ ★ THE FRAME IS THE WHOLE POINT (2026-08-23). This pair used to be stored as 1.5
    //   and 2.5 "below the top line" — the same two dots, with the staff's own half-height
    //   (2.0) folded into the numbers. That fold is only valid while the band the barline
    //   spans IS a five-line staff. A lead-sheet row is taller by one LyricVerseSpacing per
    //   extra verse, so the dots stayed pinned to the row's top edge and drifted
    //   (height - StaffHeight)/2 above its centre — 1.6 ss on a two-verse row (user report,
    //   session 240; measured against a 2.26.0 render of the same music, where the dot pair
    //   centre sits exactly on the staff centre). Stored from the CENTRE, the arithmetic is
    //   the procedure's own and no band height can fold into it.
    // ⚠️ NOT PORTED: the fold-and-search itself. `dist` is a per-staff search over
    //   `line-positions` (scm/bar-line.scm:317-358), so a staff whose lines are unevenly
    //   spaced, or a context with NO staff symbol, gets a different span — a bare row takes
    //   the "dots should go outside" branch, dist = 4*dot-y-length/staff-space, i.e. ±0.45 ss
    //   rather than ±0.5. Lily#'s renderer carries no line-positions model to search, and its
    //   lead-sheet row is already a decided divergence from LilyPond's bare row (it prints a
    //   meter; HANDOFF §3, session 226). Porting the search starts from that model.

    /// <summary>
    /// Radius of the drawn repeat-barline dot (staff spaces).
    /// </summary>
    /// <remarks>
    /// ⚠️ LILYSHARP-OWN: LILYPOND HAS NO RADIUS. It draws a GLYPH — scm/bar-line.scm:298-301
    /// <c>ly:font-get-glyph</c> fetches <c>dots.dot</c>, the same one an augmentation dot uses
    /// — so what corresponds to a radius is that glyph's own half extent — 0.225,
    /// measured on 2.26.0 and independently in our own extraction
    /// (<c>GlyphMetrics.AugmentationDot</c> = (0, -0.225)..(0.45, 0.225)). Lily# draws a
    /// circle instead, and this 0.2 makes it 0.025 short in radius, 0.45 → 0.40 across.
    /// ⚠️ MEASURED AND NOT FIXED: closing it would redraw every repeat barline and also widen
    /// <see cref="RepeatDotsOffset"/>, which reserves horizontal room from this same number,
    /// so it moves spacing as well as ink. It is a snapshot island of its own, not a
    /// provenance fix.
    /// </remarks>
    public const double RepeatDotRadius = 0.2;

    /// <summary>Half the span between the two repeat dots (staff spaces), measured from the
    /// CENTRE of the band the barline spans — one dot falls into each space adjacent to a
    /// five-line staff's middle line.</summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:360-368 make-colon-bar-line — the stencil is
    /// translated to <c>center ± dist/2</c> line positions, measured at ±0.5 staff spaces from
    /// the staff centre on 2.26.0; see the section remark above for the measurement and for
    /// why this is stored from the centre rather than from the top line.</remarks>
    public const double RepeatDotHalfSpan = 0.5;

    // === Spacing (Lilypond-compatible) ===
    // See: lily/spacing-options.cc, lily/spacing-spanner.cc

    /// <summary>
    /// Spacing increment, approximately notehead width.
    /// Lilypond default: 1.2 staff spaces.
    /// </summary>
    // LILYPOND-REF: scm/define-grobs.scm SpacingSpanner (spacing-increment . 1.2).
    public const double SpacingIncrement = 1.2;
    // === Element spacing (from scm/define-grobs.scm) ===
    // BarLine space-alist. (The Clef and TimeSignature space-alist entries live in
    // BreakAlignSpacing, the canonical space-alist home; the copies that were here were
    // dead duplicates and have been removed.)
    /// <summary>
    /// BarLine <c>(first-note . (semi-shrink-space . 1.3))</c>. LilyPond reads this
    /// entry ONLY when the bar line's break_status_dir is not CENTER — i.e. at the
    /// start of a system. An ordinary mid-line bar line uses
    /// <see cref="BarLineToNextNoteSpace"/> instead.
    /// LILYPOND-REF: scm/define-grobs.scm:300 BarLine space-alist;
    ///               lily/staff-spacing.cc:147-153.
    /// </summary>
    public const double BarLineToFirstNoteSpace = 1.3;

    /// <summary>
    /// BarLine <c>(next-note . (semi-fixed-space . 0.9))</c> — the entry that governs
    /// every bar line inside a system, and therefore the one that sets the gap from a
    /// bar line to the first note of the following measure.
    /// semi-fixed-space: <c>fixed = d/2</c>, <c>ideal = fixed + d/2 = d</c>.
    /// LILYPOND-REF: scm/define-grobs.scm:301 BarLine space-alist;
    ///               lily/staff-spacing.cc:176-180.
    /// </summary>
    public const double BarLineToNextNoteSpace = 0.9;

    // === Staff spacing ===
    // LILYPOND-REF: scm/define-grobs.scm:3352-3355 StaffGrouper staff-staff-spacing —
    //   (basic-distance . 9) (minimum-distance . 7) (padding . 1) (stretchability . 5).
    //   The section header said this in prose for a long time; the ratchet wants the address,
    //   and writing it down is what shows the fourth value has no constant here.
    // ⚠️ THE FOURTH IS DELIBERATELY NOT HERE, and this file is not where to look for it.
    //   A stretchability is a member of a SPRING, not a distance, so it lives with the spec
    //   that builds one: StaffSpacingParameters.StaffStaff spells `Stretchability = 5`, and
    //   LayoutUtilities.CreateSpring turns it into the inverse stretch strength. The three
    //   constants here are the ones a PLACEMENT reads without a spring in hand.
    // ⚠️ AN EARLIER NOTE HERE SAID THE 5 WAS "not spelled anywhere in Core" AND TOLD THE
    //   NEXT READER NOT TO ADD IT. That was a false negative from a grep, not a finding: the
    //   line it missed has been in StaffSpacingParameters since 2026-02-22, months before the
    //   note. MEASURED 2026-08-04 by perturbing it — 5 observers fall, two of them LP-fidelity
    //   ledger points (page.stretched.staff-staff-inside,
    //   system.stretched-distance.two-staff), so the value is consumed and pinned against
    //   LilyPond. A silence is not a measurement (HANDOFF 5.0).
    /// <summary>Basic distance between staves in a group (center to center).</summary>
    public const double StaffStaffBasicDistance = 9.0;
    /// <summary>Minimum distance between staves in a group.</summary>
    public const double StaffStaffMinimumDistance = 7.0;
    /// <summary>Padding between staves.</summary>
    public const double StaffStaffPadding = 1.0;

    // === Outside-staff side-position declarations, per grob (the declaration table) ===
    //
    // The per-grob values lily/side-position-interface.cc aligned_side and the
    // outside-staff pass read: `padding` (:361-370, paid against the side supports),
    // `staff-padding` (:219-222 include_staff — declaring it puts the STAFF EXTENT into
    // the support skyline as a minimum (:323-330 set_minimum_height) — and :433-453, the
    // refpoint floor), and minimum-space (:384-385). One home per grob so a floor is
    // never an ad hoc constant in a consumer again (the TextScript 0.5 and Ottava 2.0
    // used to live in OutsideStaffStacker / OttavaBracketEngraver; the trill and text
    // spanner joined as a table, not as a third and fourth stray).
    //
    // outside-staff-priority, for the record (consumed as the stackers' call ORDER, not
    // as data): TrillSpanner 50, BarNumber 100, TupletBracket 200, DynamicLineSpanner /
    // DynamicText 250, TextSpanner 350, OttavaBracket 400, TextScript 450,
    // VoltaBracketSpanner 600, RehearsalMark 1500.

    /// <summary>TrillSpanner's side-position padding, paid against its supports.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:4079 TrillSpanner padding, read by aligned_side —
    /// measured binding in both spanner-floors.ly books (ledger
    /// trill.{quiet,support}.staff-to-line: staff ink + 0.5 + reach, box top + 0.5
    /// + reach).</remarks>
    public const double TrillSpannerPadding = 0.5;

    /// <summary>TrillSpanner's staff-padding. Its :433-453 refpoint floor
    /// (ink + 1.0) is SUBSUMED by <see cref="TrillSpannerPadding"/> + the glyph's
    /// downward reach whenever reach &gt; staff-padding − padding, which the trill's
    /// 1.0 always satisfies — what declaring it still does is include_staff, putting
    /// the staff extent into the support.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:4081 TrillSpanner staff-padding, read by aligned_side.</remarks>
    public const double TrillSpannerStaffPadding = 1.0;

    /// <summary>How far BELOW the trill's line the "tr" glyph's origin sits: the bound
    /// text's stencil-offset (0 . −1), in staff spaces. The glyph ink about the LINE is
    /// therefore (−1.0 . glyphTop − 1.0) — LilyPond's own ext dump reads (−1.0 . 1.1) —
    /// and the grob's downward facing reach in every aligned_side term is this value.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:4068 TrillSpanner stencil-offset, inside its left-bound-info text.</remarks>
    public const double TrillSpannerTextOffsetDown = 1.0;

    /// <summary>TextSpanner's staff-padding — the naked :433-453 refpoint floor: with
    /// no declared padding (side-position's default 0.0) and a facing reach of only the
    /// dash half-thickness, the floor is what stands on a quiet staff (ledger
    /// textspanner.floor.staff-to-line = 2.05 + 0.8, six-digit round).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3852 TextSpanner staff-padding, read by aligned_side.</remarks>
    public const double TextSpannerStaffPadding = 0.8;

    /// <summary>TextScript's staff-padding — a floor under the grob's REFPOINT (the
    /// text baseline, not its ink edge) against the staff's own ink edge, applied
    /// BEFORE the outside-staff pass. It binds when the string has no descender
    /// (ledger textscript.no-descender.staff-to-baseline = 2.05 + 0.5).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3816 TextScript staff-padding;
    /// lily/side-position-interface.cc:401-453 aligned_side.</remarks>
    public const double TextScriptStaffPadding = 0.5;

    /// <summary>BassFigureAlignmentPositioning's staff-padding — the floor under a figure
    /// ROW's refpoint, and (by being declared at all) what puts the staff extent into the
    /// row's side-position support. MEASURED INERT in every regime a five-line staff has:
    /// the support placement is staff ink + padding 0.5 + the top digit's cap, and every
    /// figure's cap beats staff-padding − padding = 0.5 (ledger
    /// figbass.quiet.staff-to-baseline = 2.05 + 0.5 + 1.124795235605315). Spelled anyway
    /// because LilyPond computes both and takes the larger.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:395 staff-padding of BassFigureAlignmentPositioning (side-position-interface at :407);
    /// lily/side-position-interface.cc:219-222 include_staff; lily/side-position-interface.cc:433-453 aligned_side's floor.</remarks>
    public const double BassFigureStaffPadding = 1.0;

    /// <summary>BassFigureAlignmentPositioning's side-position padding, paid against the
    /// supports — the same 0.5 the loose-line device spells as
    /// nonstaff-relatedstaff-spacing's padding (ly/engraver-init.ly:1121), which is why the
    /// two devices put a figure row in the same place to fifteen digits (the probe measured
    /// both). ⚠️ Lily# still SPENDS it in SkylineDrop, shared with the lyric line, so this
    /// declaration is not yet its only home.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:393 padding of BassFigureAlignmentPositioning (side-position-interface at :407),
    /// read by aligned_side :370.</remarks>
    public const double BassFigurePadding = 0.5;

    /// <summary>OttavaBracket's staff-padding — the floor its LINE rests on over a
    /// quiet staff (ledger ottava.floor.staff-to-line = 2.05 + 2.0, exact).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2718 OttavaBracket staff-padding, read by aligned_side.</remarks>
    public const double OttavaBracketStaffPadding = 2.0;

    /// <summary>OttavaBracket's padding — spent ONCE by aligned_side against the side
    /// supports (the staff's note columns and, via <see cref="OttavaBracketStaffPadding"/>,
    /// the staff itself). It is BIGGER than outside-staff-padding 0.46, so wherever a
    /// column decides, this is the number that stands and the collision pass moves
    /// nothing.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2716 OttavaBracket padding, whose Y-offset is side-position-interface::y-aligned-side
    /// — spent at lily/side-position-interface.cc:360-370 aligned_side, total_off += dir * ss * padding.
    /// MEASURED (audit/lp-geometry/probes/ottava-floor.ly OTC, session 73): the clearance
    /// between the bracket's own DOWN skyline and the binding notehead's UP skyline is
    /// 0.500000000 to nine digits, not 0.46 — the reading that named
    /// ledger ottava.support.staff-to-line's residual.</remarks>
    public const double OttavaBracketPadding = 0.5;

    /// <summary>The ottava LABEL's text em ("8va" / "8vb" / "15ma" / "15mb"):
    /// OttavaBracket declares font-series bold and font-shape italic and NO font-size, so
    /// the paper text-font-size applies unstepped — the same derivation as
    /// <see cref="TextScriptFontSize"/> and <see cref="MetronomeMarkFontSize"/>.</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:69-77 text-font-size;
    /// scm/define-grobs.scm:2708-2731 OttavaBracket ly:ottava-bracket::print — the block declares no font-size.
    /// MEASURED: ledger ottava.label.ink-height — LilyPond's centred label ink is
    /// 1.5840627276 tall and this em reproduces it to 1.584000136, where the
    /// <c>FontSize × 0.45 = 1.8</c> the draw used until 2026-08-02 gave 1.296000112. That
    /// was the em mislabel's FOURTH instance (lyrics 3.2, chords 2.6, TextScript 2.4).</remarks>
    public static readonly double OttavaBracketFontSize = TextScriptFontSize;

    /// <summary>
    /// The em size the part combiner's "a2" / "Solo" / "Solo II" label is set at, in staff
    /// spaces.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1077-1094 CombineTextScript, outside-staff-priority 475 —
    /// it declares <c>font-series . bold</c> and NO <c>font-size</c>, so like <c>TextScript</c> it is set
    /// at the paper's own text size — see <see cref="TextScriptFontSize"/>, which carries the
    /// address. MEASURED (audit/lpreg/pcombine-lp.ly, grob dump): <c>series=bold shape=()
    /// size=()</c>, i.e. bold, upright, unscaled.
    /// <para>
    /// ⚠️ IT WAS <c>FontSize * 0.65</c> = 2.6 AND ITALIC in the renderer — the same shape of
    /// mistake as the chord symbol's 2.6 (see <see cref="ChordNameFontSize"/>): a size
    /// guessed from the staff size and a style nobody had read off the grob.
    /// </para>
    /// </remarks>
    public static readonly double CombineTextFontSize = TextScriptFontSize;

    // TrillWaveAmplitude (0.2, "half-amplitude of the drawn trill wave") is GONE
    // (2026-07-30). It was a LILYSHARP-OWN drawing device with no LilyPond counterpart at
    // all — LilyPond's trill line has no amplitude, being a run of scripts.trill_element
    // glyphs (lily/line-interface.cc:48-108 make_trill_line). Now that the run is what
    // Lily# reserves AND draws (Svg.Layout.TrillWaveOutline), the constant had no reader
    // left in any of its three former seats: the renderer, the outside-staff profile and
    // LayoutEngine's paging extent all take their reach from the element itself.

    /// <summary>DynamicLineSpanner's side-position padding — the gap from the
    /// note/staff skyline to a dynamic or hairpin, NOT outside-staff-padding.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1408 DynamicLineSpanner padding, read by aligned_side.</remarks>
    public const double DynamicLineSpannerPadding = 0.6;

    /// <summary>DynamicLineSpanner's staff-padding (see DynamicEngraver.BaselineY for
    /// the full aligned_side transcription that consumes it).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1411 DynamicLineSpanner staff-padding, read by aligned_side.</remarks>
    public const double DynamicLineSpannerStaffPadding = 0.1;

    /// <summary>DynamicLineSpanner's minimum-space (aligned_side :384-385).</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:1406 DynamicLineSpanner minimum-space, read by aligned_side.</remarks>
    public const double DynamicLineSpannerMinimumSpace = 1.2;

    /// <summary>MetronomeMark's side-position padding, paid against its supports — and its
    /// supports are the STAVES themselves (metronome-engraver.cc sets side-support-elements
    /// = stavesFound), so on a quiet staff the mark's stencil BOTTOM lands at staff ink +
    /// this, and its baseline rides its own ink bottom above that (ledger
    /// tempo.quiet.staff-to-baseline = 2.05 + 0.8 + 0.033010, to the digit). It declares
    /// NO staff-padding; its outside-staff-priority is 1300 and its horizontal padding the
    /// mark family's 0.2.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:2343 MetronomeMark padding, read by aligned_side;
    /// lily/metronome-engraver.cc:136-139 stop_translation_timestep, side-support-elements = stavesFound.</remarks>
    public const double MetronomeMarkPadding = 0.8;

    /// <summary>The metronome mark's TEXT em (" = 120", and the \bold marking): MetronomeMark
    /// declares no font-size, so the paper text-font-size applies unstepped — the same
    /// derivation as <see cref="TextScriptFontSize"/> (11pt over a 5pt staff space = 2.2).</summary>
    /// <remarks>LILYPOND-REF: scm/paper.scm:69-77 text-font-size;
    /// scm/define-grobs.scm:2335-2365 MetronomeMark outside-staff-priority block — the
    /// grob declares no font-size property, so the paper em applies.</remarks>
    public static readonly double MetronomeMarkFontSize = TextScriptFontSize;

    /// <summary>The scale of the metronome mark's note glyphs: the markup wraps its
    /// note-by-number in \smaller = one font-size step down, magstep(-1) = 2^(-1/6).
    /// The note's stem-length is max(3, log-1) staff spaces BEFORE this scale.</summary>
    /// <remarks>LILYPOND-REF: scm/translation-functions.scm:118-124 metronome-markup make-smaller-markup;
    /// scm/define-markup-commands.scm:5541-5569 note-by-number size-factor,
    /// stem-length = size-factor * max(3, log-1), size-factor = magstep(font-size).</remarks>
    public static readonly double MetronomeMarkNoteMagstep = Math.Pow(2.0, -1.0 / 6.0);


    /// <summary>
    /// Space for shortest duration.
    /// Lilypond default: 2.0 (so shortest note gets 2.0 * increment space).
    /// </summary>
    // LILYPOND-REF: scm/define-grobs.scm SpacingSpanner (shortest-duration-space . 2.0).
    public const double ShortestDurationSpace = 2.0;

    /// <summary>
    /// Base shortest duration (3/16). Caps the common-shortest spacing basis:
    /// a piece whose shortest note is a quarter or longer is spaced as if its
    /// shortest were 3/16, keeping long-note music from spreading out. A piece
    /// with eighths or shorter uses that actual (smaller) shortest instead.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:3242 SpacingSpanner
    ///   (base-shortest-duration . (ly:make-moment 3 16)); used by
    ///   lily/spacing-spanner.cc:166-172 calc_common_shortest_duration as
    ///   d = min(base-shortest-duration, mode). (Previously 1/8 here, which
    ///   over-spaced every quarter-or-longer piece by ~23% vs LilyPond.)
    /// </remarks>
    public const double BaseShortestDuration = 0.1875; // 3/16

    // ⚠️ MaxStiffness = 10.0 ("maximum stiffness for zero-duration items") stood here with
    //   no reader and no LilyPond counterpart named. Deleted 2026-08-04 — see the note on
    //   NoteheadHeight above for why an unread constant is dropped rather than sourced.

    /// <summary>
    /// Tab string-line spacing in staff spaces — 1.5, for every string count.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/engraver-init.ly, TabStaff <c>\override StaffSymbol.staff-space
    /// = #1.5</c>. One value, with no dependence on the string count: LilyPond's
    /// six-string tab staff spans 5 × 1.5 = 7.5 line centre to line centre and its
    /// four-string one 3 × 1.5 = 4.5, both confirmed on 2.26.0
    /// (audit/lp-geometry/probes/line-start-mindist.ly, scores CGT and CG4).
    /// <para>
    /// This USED to taper — 1.5 below five strings, 1.4 at five, 1.3 at six — "so a 5- or
    /// 6-string staff does not grow excessively tall". That reasoning also worked against
    /// Lily#'s own larger fret digits (<see cref="Svg.Layout.TabConstants.FretFontSize"/>,
    /// a ratified deviation): a 1.7875-tall digit overlaps a 1.3 string gap, which is what
    /// the white occluding background behind each digit is for. Widening to LilyPond's 1.5
    /// gives them more room, so fidelity and legibility agreed here. Ledger pair
    /// tab.staff.line-span.{six,four}-string; the four-string half was already exact,
    /// which is what proved the taper was the defect rather than the tab staff generally.
    /// </para>
    /// <para>
    /// ⚠️ Do not confuse this with the fret DIGIT size, which stays deliberately larger
    /// than LilyPond's (docs/HANDOFF.md §3).
    /// </para>
    /// <para>
    /// <paramref name="stringCount"/> is deliberately still taken and deliberately unused:
    /// every caller has it, and keeping it in the signature is what says "LilyPond's tab
    /// spacing does not depend on the string count" at each call site rather than only
    /// here. It is not a leftover.
    /// </para>
    /// </remarks>
    public static double TabStringSpace(int stringCount) => 1.5;

    // === Barline rendering ===
    /// <summary>
    /// Total width of the repeat-dots block including its kern to the next
    /// bar segment: dot diameter + kern.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm:766-801 — the colon glyph is
    /// stacked with the same kern as the line segments.</remarks>
    public const double RepeatDotsOffset = 2 * RepeatDotRadius + RepeatBarlineDotSeparation;

    /// <summary>
    /// The drawn X-extent of a bar line, in staff spaces — the sum of the glyph
    /// components actually stencilled (thin/thick segments, their separations, and
    /// the leftward repeat-dots block). This is the SINGLE source of truth shared
    /// by the renderer (which draws the bar line this wide) and the spacing engine
    /// (which must reserve at least this much so a bar line is never engraved into
    /// the preceding column). Keeping both on this one method prevents the two from
    /// drifting apart, which is what let a whole rest collide with a `:|`.
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/bar-line.scm — a bar line's own stencil X-extent
    /// feeds the spacing; the printed width and the reserved width are the same
    /// quantity.</remarks>
    public static double BarlineDrawnWidth(BarlineType type) => type switch
    {
        BarlineType.None => 0,
        BarlineType.Single => ThinBarlineThickness,
        BarlineType.Double => ThinBarlineThickness + BarlineSeparation + ThinBarlineThickness,
        BarlineType.Final => ThinBarlineThickness + BarlineSeparation + ThickBarlineThickness,
        BarlineType.RepeatStart => ThickBarlineThickness + BarlineSeparation + ThinBarlineThickness + RepeatDotsOffset,
        BarlineType.RepeatEnd => RepeatDotsOffset + ThinBarlineThickness + BarlineSeparation + ThickBarlineThickness,
        BarlineType.RepeatBoth => RepeatDotsOffset + ThinBarlineThickness + BarlineSeparation + ThickBarlineThickness
                                  + BarlineSeparation + ThinBarlineThickness + RepeatDotsOffset,
        _ => ThinBarlineThickness
    };

    // Bow (slur/tie) thickness — LilyPond's bezier sandwich (lily/lookup.cc:395-405
    // Lookup::slur): the two arcs are offset by ±0.5·curvethick and the outline is stroked
    // with a linethick round-cap pen. curvethick = the grob's `thickness` property, linethick
    // = its `line-thickness` property, both in line-thickness units. Slur and Tie share the
    // same defaults (define-grobs.scm:3175-3180 / 3898-3902): thickness 1.2, line-thickness 0.8.

    /// <summary>Bow middle thickness (arc separation) = Tie/Slur thickness 1.2 × line-thickness.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3902 Tie (thickness . 1.2).</remarks>
    public const double TieMidThickness = 1.2 * LineThickness;

    /// <summary>Bow middle thickness (arc separation) = Slur thickness 1.2 × line-thickness.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3180 Slur (thickness . 1.2).</remarks>
    public const double SlurMidThickness = 1.2 * LineThickness;

    /// <summary>Round-cap pen that strokes the bow outline (its tapered ends read as rounded,
    /// and it is the bow's thickness at the endpoints) = Slur/Tie line-thickness 0.8 × line-thickness.</summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm:3175/3898 (line-thickness . 0.8);
    /// lily/lookup.cc:415 bezier_sandwich(back, curve, linethick).</remarks>
    public const double BowEndRounding = 0.8 * LineThickness;


    // === Conversion helpers ===

    /// <summary>Converts staff spaces to staff positions (1 space = 2 positions).</summary>
    public static double ToStaffPositions(double staffSpaces) => staffSpaces * 2;

    /// <summary>Converts staff positions to staff spaces (1 position = 0.5 spaces).</summary>
    public static double ToStaffSpaces(double staffPositions) => staffPositions / 2;
}