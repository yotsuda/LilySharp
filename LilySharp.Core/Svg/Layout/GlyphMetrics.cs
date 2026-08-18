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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Glyph metrics for the Emmentaler music font.
/// All values are in staff spaces (the distance between two staff lines).
/// </summary>
/// <remarks>
/// This file holds <b>hand-tuned</b> constants — engraving thicknesses, spacing
/// heuristics, and LilyPond grob defaults. Values that can be derived directly
/// from the font binary (BBoxes, advance widths) live in
/// <c>GlyphMetricsGenerated.cs</c>, which is produced by
/// <c>audit/scripts/Extract-EmmentalerMetrics.py</c>.
///
/// Coordinate system:
/// - Origin (0, 0) is at the glyph's left edge on the baseline
/// - X increases to the right
/// - Y increases upward
/// - Bounding box is defined by SW (south-west) and NE (north-east) corners
/// </remarks>
internal static partial class GlyphMetrics
{
    /// <summary>
    /// Bounding box for a glyph, in staff spaces.
    /// </summary>
    public readonly record struct BBox(double Left, double Bottom, double Right, double Top)
    {
        public double Width => Right - Left;
        public double Height => Top - Bottom;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Bottom + Top) / 2;
    }

    /// <summary>
    /// Anchor point for stem attachment, in staff spaces relative to glyph origin.
    /// </summary>
    public readonly record struct Anchor(double X, double Y);

    /// <summary>
    /// The metrics a grob carrying <paramref name="fontSizeStep"/> reads — LilyPond's
    /// <c>font-size</c>, in sixths of an octave (full size 0, a grace −3).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — the requested size picks the
    /// FILE (<see cref="EmmentalerDesignSize"/>), and every dimension is then read from THAT
    /// file's table (lily/open-type-font.cc:390-408 Open_type_font::get_indexed_char_dimensions).
    /// Emmentaler is optically sized, so this is
    /// not the same as reading one table and scaling it: the 14 design's black head is 1.298161
    /// where the 20's is 1.304200, in each design's own staff spaces.
    /// <para>
    /// ⚠️ WHAT COMES BACK IS IN THE CHOSEN DESIGN'S STAFF SPACES, not the page's. The page's
    /// are <c>box · magstep(fontSizeStep)</c> — <see cref="StaffSize.Ink"/> is that multiply,
    /// and optical sizing does not change it: LilyPond's own requested/actual magnification
    /// (<see cref="EmmentalerDesignSize.Magnification"/>) cancels against the design size, so
    /// the only thing that changes is WHICH table the number came out of.
    /// </para>
    /// </remarks>
    public static DesignMetrics ForFontSizeStep(double fontSizeStep)
        => ForDesign(EmmentalerDesignSize.ForFontSizeStep(fontSizeStep).Rounded);

    /// <summary>
    /// The font a grob at <paramref name="fontSizeStep"/> reads: the design its size lands on,
    /// already in the PAGE's staff spaces. Nothing a caller reads out of this needs scaling.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font picks the file and hands back a
    ///   font scaled to the requested size; lily/modified-font-metric.cc:62-68
    ///   Modified_font_metric::get_indexed_char_dimensions is where that
    ///   scaling is applied, once, as the metric is read. A LilyPond grob therefore never
    ///   multiplies a glyph box by its own font size — it asks a font that has already done it,
    ///   and that is the shape this method exists to give Lily#'s seeds.
    /// <para>
    /// ⚠️ magstep, NOT LilyPond's requested/actual magnification: the design size the file
    /// carries cancels out (a 14.14 design read at 14.142pt on a 20pt staff is
    /// <c>14.142/20 = magstep(-3)</c> of the page's staff space), so what is left is exactly
    /// the factor Lily#'s callers already used — with the numbers now coming out of the right
    /// design. See <see cref="EmmentalerDesignSize.Magnification"/> for the part that cancels.
    /// </para>
    /// </remarks>
    public static DesignMetrics AtFontSize(double fontSizeStep)
        => _sizedFonts.GetOrAdd(fontSizeStep, static step =>
            ForFontSizeStep(step).Scaled(System.Math.Pow(2.0, step / 6.0)));

    /// <summary>
    /// The sized fonts built so far. A score asks for a handful of sizes (full, grace, ossia,
    /// cue) and asks for each of them on every grob, so the tables are built once and shared —
    /// as LilyPond shares them out of <c>Font_metric</c>'s own cache.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<double, DesignMetrics>
        _sizedFonts = new();

    // ========== Stem Anchors ==========
    // Stem attachment uses the notehead's advance width on the X axis (LP convention)
    // and a small vertical offset to account for the notehead curve. The Y offset is
    // hand-tuned (font does not expose stem-attach anchors via OTF tables).
    // LILYPOND-REF: lily/stem.cc — stem attaches at notehead.extent(X_AXIS)[RIGHT]

    /// <summary>Stem attachment point for upward stem (right side of filled notehead).</summary>
    /// <remarks>X = NoteheadBlackAdvance, Y from Emmentaler stem anchor convention.</remarks>
    public static readonly Anchor StemUpSE = new(NoteheadBlackAdvance, 0.168);

    /// <summary>Stem attachment point for downward stem (left side of notehead).</summary>
    public static readonly Anchor StemDownNW = new(0, -0.168);

    // ========== Accidental parenthesis ==========

    /// <summary>
    /// Combined ink width both parens add around a parenthesized accidental:
    /// parenthesize() juxtaposes stencil EXTENTS with zero padding, so the
    /// added width is the two paren glyphs' bounding-box widths — NOT their
    /// advances (leftparen draws behind its origin with advance 0).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: mf/feta-parenthesis.mf — accidentals.leftparen/rightparen
    /// LILYPOND-REF: lily/accidental.cc:35-46 — parenthesize() adds parens with 0 padding
    /// </remarks>
    /// (Computed property: static-field initialization order across partial
    /// class files is unspecified, and the BBoxes live in the generated file.)
    public static double AccidentalParensInkWidth =>
        AccidentalLeftParen.Width + AccidentalRightParen.Width;

    /// <summary>The same width out of a given font, for an accidental whose font-size picked
    /// another design (a grace's is −4, a cue's −4, an editorial one's −2).</summary>
    public static double GetAccidentalParensInkWidth(DesignMetrics font) =>
        font.AccidentalLeftParen.Width + font.AccidentalRightParen.Width;

    /// <summary>
    /// Maxima (8-measure) rest ink width, in staff spaces — the church-rest glyph for
    /// duration-log -3 (<see cref="EmmentalerGlyphs.RestMaxima"/>, rests.M3).
    /// </summary>
    /// <remarks>
    /// This is a font metric and belongs in GlyphMetricsGenerated.cs, but the extractor
    /// does not yet emit rests.M3; move it there when it does. The value is not guessed:
    /// LilyPond 2.24.4 renders `R1*8` as a SINGLE maxima glyph, so the multi-measure
    /// rest's own X-extent is that glyph's width — dumped via ly:grob-extent it is
    /// exactly 1.800. It cross-checks against the run-width model on two further
    /// independent points: N=8 gives 14.190 and N=10 (maxima + breve) gives 16.434,
    /// both matching LilyPond to the last digit.
    /// LILYPOND-REF: mf/feta-rests.mf — rests.M3.
    /// </remarks>
    public const double RestMaximaWidth = 1.8;

    // ========== Engraving line/stroke thicknesses ==========
    // Line-family thicknesses live in EngravingDefaults, derived from
    // LilyPond's line-thickness (scm/paper.scm). The SMuFL/Bravura duplicates
    // that used to sit here (staff 0.13 / stem 0.12 / ledger 0.16, and the
    // thin-barline 0.16) were unused and contradicted LilyPond's values;
    // the thin barline is EngravingDefaults.ThinBarlineThickness (1.9 ×
    // line-thickness = 0.19, LilyPond's hair-thickness).

    // ========== Spacing heuristics ==========

    /// <summary>
    /// Gap between an accidental's ink right edge and its note head's ink left edge.
    /// </summary>
    /// <remarks>
    /// LilyPond's two constants, summed: <c>AccidentalPlacement.padding</c> 0.20 and
    /// <c>right-padding</c> 0.15 (scm/define-grobs.scm AccidentalPlacement;
    /// lily/accidental-placement.cc:397 and :400, applied at :412-416).
    /// <para>
    /// LilyPond adds one more term Lily# does not compute: :412 measures the accidental's
    /// right SKYLINE against the heads' skyline, not their boxes, so a vertically thin glyph
    /// ends up slightly further out. Zeroing both paddings on 2.24.4 leaves exactly that
    /// term — sharp −0.000010, flat −0.000004, double-flat −0.001996, but natural +0.017606
    /// and double-sharp +0.047704. Perturbing each padding by +0.3 moves the gap by +0.3, so
    /// the two are additive and this constant is the whole of the non-skyline part.
    /// </para>
    /// <para>
    /// It was 0.2 — LilyPond's `padding` alone, with `right-padding` missing — which put
    /// every accidental 0.15 too close to its head. See the ledger's
    /// barline.next.accidental-to-notehead.
    /// </para>
    /// </remarks>
    public const double AccidentalNoteGap = 0.35;

    /// <summary>
    /// LILYSHARP-OWN: the gap Lily# leaves between a LYRIC syllable and its neighbour.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is no longer the note-to-note minimum, and must not become one again. That
    /// quantity is now built the way LilyPond builds it — each grob's own
    /// <c>extra-spacing-width</c> folded into its spacing box, a padding-free skyline
    /// distance for the spring minimum, and the spacing spanner's <c>padding</c> on top for
    /// the rod — so nothing this is set to can move a note-to-note distance, and
    /// <c>SeparatingPaddingTests.NoteToNoteDistance_DoesNotDependOnMinItemGap</c> asserts
    /// exactly that.
    /// LILYPOND-REF: lily/separation-item.cc:166-179 (extra-spacing-width into the boxes),
    ///   lily/note-spacing.cc:78-83 (the spring minimum),
    ///   lily/separation-item.cc:47-68 with lily/spacing-spanner.cc:315-316 (the rod).
    /// <para>
    /// What is LEFT is <see cref="LyricSpacing"/>, which adds this to a lyric's own extent
    /// in four places. ⚠️ DO NOT assume that is the same defect wearing a different hat and
    /// port it away by analogy. Lily#'s lyric model is not LilyPond's: syllables are bound
    /// to notes and DIVIDED BY BAR LINE here, and LilyPond has no such division — so the
    /// quantity these four sites reserve may have no LilyPond counterpart to port at all,
    /// and 0.4 may be a legitimate own value rather than a stand-in. Establish WHICH it is
    /// (a corpus pair, or the plain fact that LilyPond does not engrave this) before
    /// touching it. Removing it because the note-to-note case was an invention would be
    /// reasoning by resemblance, which is how the note-to-note port was nearly aimed at the
    /// wrong number twice.
    /// </para>
    /// </remarks>
    public const double MinItemGap = 0.4;

    /// <summary>
    /// Padding between barline and adjacent item, in staff spaces.
    /// </summary>
    public const double BarlinePadding = 0.8;

    // ========== Clef widths (advance) and change-clef variants ==========
    // Full clef advance widths come from the font (GlyphMetricsGenerated). Change
    // (mid-measure) clef variants are 75% of full size — LP draws them at reduced
    // font-size rather than as separate glyphs.
    // LILYPOND-REF: lily/clef.cc:29-52 — "_change" suffix glyphs are ~75% of full size

    /// <summary>G clef advance width (alias for GClefAdvance, kept for source compat).</summary>
    public const double GClefWidth = GClefAdvance;

    /// <summary>F clef advance width (alias for FClefAdvance).</summary>
    public const double FClefWidth = FClefAdvance;

    /// <summary>C clef advance width (alias for CClefAdvance).</summary>
    public const double CClefWidth = CClefAdvance;

    // A change clef is its OWN glyph — clefs.G_change / F_change / C_change — not the full
    // clef scaled down, so `full * 0.75` was an approximation of a metric that is available
    // exactly. It ran 4-7% narrow (F: 2.010 against 2.146680), and since the mid-measure gap
    // after a clef is measured from the glyph's right edge, that error landed straight in the
    // spacing. LILYPOND-REF: lily/clef.cc — Clef::calc_glyph_name appends "_change".
    //
    // The right edge, not the advance: Staff_spacing reads last_ext[RIGHT], a stencil extent.

    // ⚠️ PROPERTIES, not `static readonly` fields. These read a BBox declared in the OTHER
    // half of this partial class (GlyphMetricsGenerated.cs), and C# does not define the
    // initialisation order of static fields ACROSS partial parts — as fields these read a
    // default-constructed BBox and came out 0, which silently deleted every change glyph's
    // width from the spacing. A property is evaluated on use, so the order cannot bite.

    /// <summary>G clef change width — <c>clefs.G_change</c> ink right edge.</summary>
    public static double GClefChangeWidth => ClefGChange.Right;

    /// <summary>F clef change width — <c>clefs.F_change</c> ink right edge.</summary>
    public static double FClefChangeWidth => ClefFChange.Right;

    /// <summary>C clef change width — <c>clefs.C_change</c> ink right edge.</summary>
    public static double CClefChangeWidth => ClefCChange.Right;

    /// <summary>
    /// The line-start clef's stencil BBox (LILC bbox, staff spaces, Y-up) — the ONE place a
    /// clef's ink extent is read from, so the line-start prefix treats every clef uniformly
    /// through its own stencil, exactly as LilyPond does (no glyph is special-cased).
    /// </summary>
    /// <remarks>
    /// The G/F/C clefs' ink starts at the grob origin (LILC bbox left 0); the percussion clef's
    /// ink starts 0.67 ss RIGHT of its origin (LILC bbox left 0.67) — a per-glyph property of
    /// the (byte-identical) font, in the SAME staff-space frame, not a coordinate difference
    /// between Lily# and LilyPond. <see cref="LineStartClefWidth"/> (ink width) and
    /// <see cref="ClefInkLeft"/> (origin→ink-left) both derive from this map, so the reservation
    /// and the draw-origin correction can never disagree on a clef's ink. Tab clefs never reach
    /// here (filtered out of <see cref="SpacingRules.MaxClefWidth"/>, drawn by DrawTabStaff).
    /// </remarks>
    private static BBox ClefBBox(Model.ClefType clef) => clef switch
    {
        Model.ClefType.Bass or Model.ClefType.Bass8Below => ClefF,
        Model.ClefType.Alto or Model.ClefType.Tenor or Model.ClefType.Soprano
            or Model.ClefType.MezzoSoprano or Model.ClefType.Baritone => ClefC,
        Model.ClefType.Percussion => ClefPercussion,
        // Treble family (incl. treble_8 — the octave digit is drawn below/above, not on the
        // clef's horizontal ink).
        _ => ClefG,
    };

    /// <summary>
    /// The clef ink WIDTH the line-start prefix reserves, and the width the drawn key/time
    /// signatures break-align past — the ink-left-to-ink-right span, measured from the shared
    /// LeftEdge→clef column its ink-left sits on (<see cref="ClefInkLeft"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/staff-spacing.cc:169-198 + lily/break-alignment-interface.cc:141-142,242
    /// — the break-align gap after the clef rides on the clef stencil's ink (the next item lands
    /// at the clef's <c>extent[RIGHT]</c> + gap). Measured off the clef's own stencil, per clef,
    /// as <c>Right - Left</c>: the G clef 2.565, the F clef (bass) 2.6834, the C clef 2.720, the
    /// percussion clef 1.33. For the G/F/C clefs Left is 0 so this equals <c>.Right</c>; the
    /// percussion clef's Left is 0.67, so its ink width is 1.33, not its 2.0 origin-to-right
    /// span. Reserving the G width for the wider F/C clefs (or the 2.565 fallback for percussion)
    /// shoved their line-start meter and metered first note off LilyPond's position (ledger
    /// line-start.clef-to-time, "defect-3", now closed for every clef family).
    /// </remarks>
    public static double LineStartClefWidth(Model.ClefType clef)
    {
        var b = ClefBBox(clef);
        return b.Right - b.Left;
    }

    /// <summary>
    /// The clef stencil's LEFT ink edge relative to its grob origin (0 for the G/F/C clefs,
    /// 0.67 for the percussion clef). <c>DrawClef</c> shifts the glyph by <c>-ClefInkLeft</c> so
    /// EVERY clef's ink-left lands on the shared LeftEdge→clef column
    /// (<see cref="EngravingDefaults.ClefGlyphXOffset"/> = 0.8) — for the G/F/C clefs the shift
    /// is 0 (origin already = ink-left); for percussion it draws the glyph at origin 0.13 so its
    /// ink-left reaches 0.8, exactly as LilyPond places it (rendered on 2.26.0: origin 0.13,
    /// ink-left 0.8). Without it the percussion ink drew 0.67 too far right.
    /// </summary>
    public static double ClefInkLeft(Model.ClefType clef) => ClefBBox(clef).Left;

    /// <summary>
    /// The clef stencil's RIGHT ink edge relative to its grob origin (2.565 for the G clef,
    /// 2.0 for the percussion clef whose ink is only 1.33 wide).
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="ClefInkLeft"/> to give the Clef break-align GROUP's extent,
    /// which is the union across the system's staves — see
    /// <see cref="SpacingRules.ClefGroupInkLeft"/>. For a single kind of clef the group is
    /// that clef, so <c>Right - Left</c> is <see cref="LineStartClefWidth"/>.
    /// </remarks>
    public static double ClefInkRight(Model.ClefType clef) => ClefBBox(clef).Right;

    // ClefChangePadding (0.5) lived here as "the padding before and after a change item".
    // It was never a LilyPond quantity: 0.5 is Clef.space-alist's `right-edge` entry, the gap
    // to the END of a line, and LilyPond has no single padding that applies to both sides of
    // a change item at all — it prices the left gap through Note_spacing or break alignment
    // and the right gap through the space-alist entry keyed on what follows (clef 1.0, key
    // 2.5, time 2.0). Every caller now goes through SpacingRules.MidMeasureChangeGaps or
    // BoundaryChangePrefix. COORDINATE_AUDIT.md §4.7.2 / §4.7.3.

    // ========== LP grob spacing defaults ==========
    // The Clef/KeySignature/TimeSignature space-alist values (clef->key 3.5,
    // clef->first-note 5.0, key->time 1.15, key->first-note 2.5, time->first-note 2.0)
    // are owned by BreakAlignSpacing (the canonical, unit-tested space-alist home). The
    // copies that were here were dead duplicates and have been removed.

    // ========== Key signature accidental widths ==========
    // LP key-signature-interface.cc uses add_at_edge(padding=0), which butts one STENCIL
    // against the next — so the per-accidental step is the glyph's ink width, not its
    // advance. The old note here called the stencil width "(=advance)"; they are different
    // numbers (a sharp inks 1.100010 and advances 1.100000, a natural inks 0.666666 and
    // advances 0.664000), and LilyPond's A-major signature measures 3.300030 = 3 x 1.100010.

    // Properties for the same reason as the change-clef widths above: cross-partial static
    // field initialisation order is undefined.

    /// <summary>Width of a sharp accidental in key signature.</summary>
    public static double KeySignatureSharpWidth => AccidentalSharp.Width;

    /// <summary>Width of a flat accidental in key signature.</summary>
    public static double KeySignatureFlatWidth => AccidentalFlat.Width;

    /// <summary>Width of a natural accidental in key signature (used for cancellation).</summary>
    public static double KeySignatureNaturalWidth => AccidentalNatural.Width;

    /// <summary>Gets the per-accidental width for a key signature based on accidental type.</summary>
    public static double GetKeySignatureAccidentalWidth(bool isSharps) =>
        isSharps ? KeySignatureSharpWidth : KeySignatureFlatWidth;

    // ========== Time signature widths ==========

    /// <summary>
    /// Pango's device-pixel quantum, in staff spaces — the grid a digit's width is snapped
    /// to when LilyPond lays the time signature out as text.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE HOME, and it is <see cref="Rendering.TextFontMetrics.PangoPixelStaffSpaces"/>.
    /// This constant was discovered here, from the fetaText side, and stayed a private
    /// number until the text side measured the SAME grid per string (ledger
    /// <c>text.width.*</c>, 2026-08-02) and had to snap every advance to it. Two spellings
    /// of one quantity is the address of the next defect (HANDOFF §5.2.1②), so this is the
    /// alias and that is the value.
    /// <para>
    /// A default TimeSignature is NOT a music glyph: ly:time-signature::print builds a
    /// markup and interprets it (scm/time-signature.scm:31-41), the markup is
    /// <c>(markup #:number "N")</c> (scm/time-signature-settings.scm:923), and
    /// <c>\number</c> sets font-encoding fetaText (scm/define-markup-commands.scm:3980-3981)
    /// — so the digit goes through Pango over the FreeType outline, exactly as DynamicText
    /// does, and its advance is hinted to a whole device pixel.
    /// </para>
    /// <para>
    /// Pango scales a logical width (an integer count of device pixels × PANGO_SCALE) by
    /// <c>scale_ = INCH_TO_BP / (PANGO_SCALE · PANGO_RESOLUTION · output_scale)</c>
    /// (lily/pango-font.cc:109-112), so one device pixel measures
    /// <c>PANGO_SCALE · scale_ = INCH_TO_BP / (PANGO_RESOLUTION · output_scale)</c> staff
    /// spaces. This is that value DERIVED FROM LILYPOND'S OWN CONSTANTS, not fitted:
    /// INCH_TO_BP 72 and INCH_TO_PT 72.27 (lily/include/dimensions.hh:31,27),
    /// PANGO_RESOLUTION 1200 (lily/include/pango-font.hh:75), and
    /// <c>output_scale = staff-space · MM_PER_INCH / INCH_TO_PT</c> with the default 5 pt
    /// staff space. It comes to 0.034143 ss and reproduces LilyPond's measured digit widths
    /// to 1e-15 across all ten digits — the -0.004735 on the 4/4 signature was this
    /// quantisation, not a wrong glyph metric. Same phenomenon as DynamicText's -0.000076,
    /// which is the ink height quantised by the same PANGO_RESOLUTION; that one is left open
    /// because a skyline needs the whole quantised OUTLINE, while a width needs only this
    /// single snap.
    /// </para>
    /// </remarks>
    // LILYPOND-REF: lily/pango-font.cc:109-112 Pango_font::Pango_font;
    //   lily/include/pango-font.hh:75 PANGO_RESOLUTION;
    //   lily/include/dimensions.hh:27,31 INCH_TO_PT, INCH_TO_BP.
    private const double PangoQuantumStaffSpaces =
        Rendering.TextFontMetrics.PangoPixelStaffSpaces;

    /// <summary>Snaps a text width to Pango's device-pixel grid, as LilyPond's layout does.</summary>
    /// <remarks>Pango rounds a logical width to a whole device pixel; the width in staff
    /// spaces is therefore an integer multiple of <see cref="PangoQuantumStaffSpaces"/>.
    /// ⚠️ ONE glyph here, so snapping the total and snapping per glyph are the same thing —
    /// <c>Rendering.TextFontMetrics.Advance</c> must snap PER GLYPH, and does.</remarks>
    private static double PangoQuantise(double widthStaffSpaces) =>
        Rendering.TextFontMetrics.QuantiseToPangoPixel(widthStaffSpaces);

    /// <summary>
    /// The engraved time-signature width. LilyPond's DEFAULT style prints 4/4 and 2/2 as
    /// the <c>timesig.C44</c> / <c>timesig.C22</c> GLYPHS — the glyph (LILC ink) path,
    /// both 1.7 wide — and every other fraction as stacked <c>\number</c> digits, the
    /// Pango markup path <see cref="GetTimeSigDigitWidth"/> serves. The reservation must
    /// ride whichever path the draw takes: reserving the digit width under a drawn C
    /// glyph left every 4/4 first note 0.0953 short of LilyPond (ledger
    /// line-start.time-to-first-note.{standard,custom}-key; the digit-path point
    /// barline.next.time-change-to-notehead stays on the Pango width, exact).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/time-signature-settings.scm:954-964
    /// make-c-time-signature-markup — the glyph branch is exactly (n=2 ∧ d=2) ∨ (n=4 ∧
    /// d=4); :981-982 the default style is that procedure.</remarks>
    public static double GetTimeSigWidth(int beats, int beatType)
        => GetTimeSigWidth(
            beats.ToString(System.Globalization.CultureInfo.InvariantCulture),
            beatType.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// The engraved width of a digit-path time signature written out — the WIDER of its two
    /// rows, each row laid out as its own glyph run.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/time-signature-settings.scm:922-923 <c>format-compound-time</c> wraps
    ///   the two rows in <c>make-number-markup</c> and stacks them, so the grob's X extent is
    ///   the wider row's and the narrower one is centred inside it.
    /// <para>
    /// ⚠️ IT IS A RUN, NOT A DIGIT, and until session 164 it was a digit: this took
    /// <c>max(digit(beats), digit(beatType))</c> with <c>beats</c> passed to a per-DIGIT
    /// switch, so <c>10/8</c> fell through that switch's default and reserved ONE digit's
    /// width where LilyPond lays out two (ledger
    /// <c>line-start.time-to-first-note.two-digit-kerned</c>, −1.160872795). The rows kern:
    /// LilyPond's "10" is 34 + 43 = 77 device pixels, not 37 + 43 = 80.
    /// </para>
    /// <para>
    /// ⚠️ THE NUMERATOR MAY NOT BE A NUMBER. A compound meter's numerator is written as
    /// spelled ("3+2"), and the <c>+</c> has no glyph in this cut; <see cref="MeterGlyphRun"/>
    /// falls back to the serif face for it, which is what the drawing already did.
    /// </para>
    /// </remarks>
    public static double GetTimeSigWidth(string beats, string beatType)
    {
        // ⚠️ THE C GLYPHS COME FIRST, and this branch belongs HERE rather than in the int
        // overload: every reservation site now passes the printed rows, so a glyph test that
        // only the int path ran would put 4/4 and 2/2 on the digit path — measured, that
        // moves every C-meter book's first note (the whole line-start.time-to-first-note
        // family goes off at once, which is how this was caught).
        if (beats == "4" && beatType == "4")
            return TimeSigCommon.Width;
        if (beats == "2" && beatType == "2")
            return TimeSigCutCommon.Width;
        return System.Math.Max(MeterGlyphRun.Width(beats), MeterGlyphRun.Width(beatType));
    }

    /// <summary>
    /// Gets the advance width of ONE meter digit, snapped to Pango's grid the way LilyPond's
    /// <c>\number</c> markup is.
    /// </summary>
    /// <remarks>
    /// ⚠️ A DIGIT IS NOT A ROW. This is the single-glyph reading, correct only where the
    /// caller genuinely lays out one glyph at a time (the multi-measure rest's number, whose
    /// digits it steps through itself). A time signature's row goes through
    /// <see cref="MeterGlyphRun"/>, which also carries the GPOS kern between adjacent digits —
    /// summing this per digit misses 0.102429 on a "10".
    /// <para>
    /// ⚠️ THE ADVANCES WERE THE FATTENED CUT'S UNTIL 2026-08-14. <c>ss01</c> selects the
    /// fattened digits and <c>\number</c> asks for no features at all, so these are the plain
    /// ones now; see <see cref="MeterGlyphRun"/> for the reading that settled it and the two
    /// digits it moved.
    /// </para>
    /// </remarks>
    public static double GetTimeSigDigitWidth(int digit) =>
        PangoQuantise(UnquantisedMeterDigitAdvance(Design20, digit));

    /// <summary>The same lookup asked of ONE font — the design a grob's <c>font-size</c>
    /// selected, optionally already scaled (<see cref="AtFontSize"/>).</summary>
    /// <remarks>LILYPOND-REF: lily/open-type-font.cc:390-408 get_indexed_char_dimensions — a
    /// dimension is a question put to a FONT, never to a glyph name alone.</remarks>
    internal static double UnquantisedMeterDigitAdvance(DesignMetrics font, int digit) => digit switch
    {
        0 => font.TimeSigDigit0Advance,
        1 => font.TimeSigDigit1Advance,
        2 => font.TimeSigDigit2Advance,
        3 => font.TimeSigDigit3Advance,
        4 => font.TimeSigDigit4Advance,
        5 => font.TimeSigDigit5Advance,
        6 => font.TimeSigDigit6Advance,
        7 => font.TimeSigDigit7Advance,
        8 => font.TimeSigDigit8Advance,
        9 => font.TimeSigDigit9Advance,
        _ => font.TimeSigDigit0Advance, // fallback to a widest common digit
    };

    // ========== Helper methods ==========

    /// <summary>Gets the rest glyph bounding box for a given note value.</summary>
    public static BBox GetRestBBox(int noteValue) => noteValue switch
    {
        1 => RestWhole,
        2 => RestHalf,
        4 => RestQuarter,
        8 => Rest8th,
        16 => Rest16th,
        32 => Rest32nd,
        64 => Rest64th,
        128 => Rest128th,
        _ => RestQuarter
    };

    /// <summary>
    /// Gets the rest glyph's SKYLINE box — the flattened outline, which reaches a little
    /// further than the LILC box <see cref="GetRestBBox"/> returns.
    /// </summary>
    /// <remarks>
    /// The rest twin of <see cref="GetAccidentalSkylineBBox"/>, and the distinction is the one
    /// audit/lp-geometry keeps meeting: LilyPond's <c>vertical-skylines</c> come from the
    /// glyph's traced outline while its <c>extent</c> comes from the metric box, and for a
    /// quarter rest those differ by exactly 0.030000 at the bottom (-1.280000 against
    /// -1.250000). MEASURED as precisely that: seeding the LILC box left
    /// <c>staff.staff.rest-under-notes</c> at -0.030000 with everything else closed.
    /// </remarks>
    public static BBox GetRestSkylineBBox(int noteValue) => noteValue switch
    {
        1 => RestWholeOutline,
        2 => RestHalfOutline,
        4 => RestQuarterOutline,
        8 => Rest8thOutline,
        16 => Rest16thOutline,
        32 => Rest32ndOutline,
        64 => Rest64thOutline,
        128 => Rest128thOutline,
        _ => RestQuarterOutline
    };

    /// <summary>
    /// The gap between the prepended natural and the main glyph of a RESTORE-FIRST
    /// accidental — the ♮♯ / ♮♭ a step down within the same sign (𝄪→♯, 𝄫→♭) prints.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental.cc:131-142 Accidental_interface::print —
    ///   <c>st.add_at_edge (X_AXIS, LEFT, natural, 0.1)</c>.
    /// </remarks>
    public const double RestoreFirstPad = 0.1;

    /// <summary>
    /// The MAIN glyph name of a restore-first composite accidental
    /// ("naturalSharp" / "naturalFlat"), or null for a plain one. The composite is
    /// carried as a NAME so every consumer of an accidental's box — placement,
    /// skylines, spacing, the renderer — reads the composed stencil through the same
    /// pipes a plain glyph takes, and draw and reserve cannot split.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/accidental-engraver.cc:272-275 — LilyPond carries the same
    /// fact as the Accidental grob's <c>restore-first</c> property; the stencil is
    /// composed at print (accidental.cc:131-142) with the natural at the LEFT edge, so
    /// the composite's extent runs from the natural's ink to the main glyph's.
    /// </remarks>
    public static string? RestoreMainOf(string? accidental) => accidental switch
    {
        "naturalSharp" => "sharp",
        "naturalFlat" => "flat",
        _ => null
    };

    /// <summary>
    /// X of the composite's MAIN glyph origin, measured from the composite's own
    /// origin (= the natural's origin): the main glyph's ink LEFT lands
    /// <see cref="RestoreFirstPad"/> right of the natural's ink RIGHT.
    /// </summary>
    public static double RestoreMainOffset(DesignMetrics font, string mainName)
        => font.AccidentalNatural.Right + RestoreFirstPad
           - GetAccidentalBBox(font, mainName).Left;

    /// <summary>Gets the bounding box for an accidental by name.</summary>
    public static BBox GetAccidentalBBox(string? accidental)
        => GetAccidentalBBox(Design20, accidental);

    /// <summary>The same lookup asked of ONE font — see
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/>.</summary>
    public static BBox GetAccidentalBBox(DesignMetrics font, string? accidental)
    {
        if (RestoreMainOf(accidental) is { } main)
        {
            // The composed restore-first stencil: natural, pad, main glyph.
            var nat = font.AccidentalNatural;
            var mainBox = GetAccidentalBBox(font, main);
            double mainOrigin = RestoreMainOffset(font, main);
            return new BBox(
                nat.Left,
                Math.Min(nat.Bottom, mainBox.Bottom),
                mainOrigin + mainBox.Right,
                Math.Max(nat.Top, mainBox.Top));
        }
        return accidental switch
        {
            "sharp" => font.AccidentalSharp,
            "flat" => font.AccidentalFlat,
            "natural" => font.AccidentalNatural,
            "doubleSharp" => font.AccidentalDoubleSharp,
            "doubleFlat" => font.AccidentalDoubleFlat,
            _ => default
        };
    }

    /// <summary>
    /// Gets the notehead bounding box for a given note value.
    /// </summary>
    /// <param name="noteValue">1=whole, 2=half, 4=quarter, etc.</param>
    public static BBox GetNoteheadBBox(int noteValue) => GetNoteheadBBox(Design20, noteValue);

    /// <summary>
    /// The same lookup asked of ONE font — the design a grob's <c>font-size</c> selected,
    /// optionally already scaled (<see cref="AtFontSize"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/open-type-font.cc:390-408 get_indexed_char_dimensions — a dimension
    ///   is a question put to a FONT, never to a glyph name alone. The parameterless overload
    ///   is this one asked of <see cref="Design20"/>, which is the score's own size.
    /// </remarks>
    public static BBox GetNoteheadBBox(DesignMetrics font, int noteValue) => noteValue switch
    {
        1 => font.NoteheadWhole,
        2 => font.NoteheadHalf,
        _ => font.NoteheadBlack
    };

    // ========== The boxes a SKYLINE is built from ==========
    // A grob's skyline is NOT always its extent, and WHICH it is, is declared per grob.
    // LILYPOND-REF: scm/define-grobs.scm — Clef:902 and Flag:1625 take
    // `grob::always-vertical-skylines-from-stencil`, Accidental:35 and Rest:2958 take
    // `grob::unpure-vertical-skylines-from-stencil`; NoteHead:2595, StaffSymbol:3391 and
    // Dots:1272 declare nothing and fall to the default,
    // LILYPOND-REF: lily/grob.cc:85-89 `simple_vertical_skylines_from_extents`.
    // A from-stencil skyline is built from the glyph's OUTLINE
    // (LILYPOND-REF: lily/stencil-integral.cc:535-563 `add_named_glyph_segments`, which
    // takes `get_glyph_outline_bbox`), while an extent is the designed LILC box
    // (LILYPOND-REF: lily/open-type-font.cc:390-408 `get_indexed_char_dimensions`).
    //
    // ⚠️ SO THERE IS NO GENERAL RULE TO APPLY, and applying one is the defect this replaced.
    // MEASURED, LilyPond's own dump of both quantities at once
    // (audit/lp-geometry/probes/glyph-skyline.ly): the CLEF reads
    // `ext=(-2.550 . 4.800) skyline=(-2.540 . 4.776)` — two different boxes — while the
    // NOTEHEAD reads 0.545 for both even though its outline stops at 0.544. The notehead's
    // skyline is its extent BY DECLARATION, and seeding its outline instead would be an
    // invention that happens to be 0.001 away.
    //
    // Hence: a lookup exists here only for a grob LilyPond builds from a stencil. If a new
    // one is needed, read its line in define-grobs.scm first.

    /// <summary>The box an <c>Accidental</c>'s SKYLINE is built from — its outline.</summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:35 Accidental grob::unpure-vertical-skylines-from-stencil.
    /// </remarks>
    public static BBox GetAccidentalSkylineBBox(string? accidental)
    {
        if (RestoreMainOf(accidental) is { } main)
        {
            // The composite's outline: the natural's, united with the main glyph's
            // translated to its place in the composed stencil (same frame as
            // GetAccidentalBBox — origin at the natural's).
            var nat = AccidentalNaturalOutline;
            var mainBox = GetAccidentalSkylineBBox(main);
            double mainOrigin = RestoreMainOffset(Design20, main);
            return new BBox(
                nat.Left,
                Math.Min(nat.Bottom, mainBox.Bottom),
                mainOrigin + mainBox.Right,
                Math.Max(nat.Top, mainBox.Top));
        }
        return accidental switch
        {
            "sharp" => AccidentalSharpOutline,
            "flat" => AccidentalFlatOutline,
            "natural" => AccidentalNaturalOutline,
            "doubleSharp" => AccidentalDoubleSharpOutline,
            "doubleFlat" => AccidentalDoubleFlatOutline,
            _ => default
        };
    }

    /// <summary>
    /// The note-value bucket (1=whole, 2=half, else black) a written base duration
    /// maps to. A non-1 numerator (e.g. a breve 2/1) collapses to a black head as a
    /// safe default. Centralises the formerly inlined
    /// <c>Numerator == 1 ? Denominator : 1</c> idiom.
    /// </summary>
    public static int NoteValueOf(Semantics.Fraction baseDuration) =>
        baseDuration.Numerator == 2 && baseDuration.Denominator == 1
            ? 0 // breve
            : baseDuration.Numerator == 1 ? baseDuration.Denominator : 1;

    /// <summary>
    /// The same bucket for whichever ITEM carries a head — the one place that knows a note
    /// and a chord are asked the same question, so a caller that needs a head shape does not
    /// grow its own switch. A headless item (a rest, a clef) answers 4, the shape whose
    /// metrics every headless caller was already reading.
    /// </summary>
    public static int NoteValueOf(Model.MusicItem? item) => item switch
    {
        Model.NoteItem n => NoteValueOf(n.BaseDuration),
        Model.ChordItem c => NoteValueOf(c.BaseDuration),
        _ => 4,
    };

    /// <summary>Gets the notehead advance width for a given note value.</summary>
    public static double GetNoteheadAdvance(int noteValue)
        => GetNoteheadAdvance(Design20, noteValue);

    /// <summary>The same lookup asked of ONE font — see
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/>.</summary>
    public static double GetNoteheadAdvance(DesignMetrics font, int noteValue) => noteValue switch
    {
        // Breve: the sM1 glyph is the whole head plus its side bars.
        0 => font.NoteheadWholeAdvance * 1.30,
        1 => font.NoteheadWholeAdvance,
        2 => font.NoteheadHalfAdvance,
        _ => font.NoteheadBlackAdvance,
    };

    /// <summary>
    /// The stem-attachment point of the head a given note value draws, in staff spaces
    /// about the head's own origin.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:2608 ly:note-head::calc-stem-attachment is NoteHead's
    ///   stem-attachment — a callback, so the coordinate is asked per HEAD and never fixed
    ///   once for the whole column.
    /// LILYPOND-REF: lily/note-head.cc:201-213 Note_head::calc_stem_attachment answers it
    ///   through the head's OWN glyph name (<c>internal_print (me, &amp;key)</c>), which is
    ///   what makes the answer a function of the head SHAPE.
    /// LILYPOND-REF: lily/note-head.cc:164-196 Note_head::get_stem_attachment normalises the
    ///   point out of that same glyph's box (<c>2·(wx − centre)/length</c>), and
    /// LILYPOND-REF: lily/stem.cc:1051-1088 Stem::internal_calc_stem_offset_from_head puts it
    ///   back with <c>head_wid.linear_combination (attach)</c>. The round trip is the
    ///   identity, so what this returns is the font's attachment coordinate itself.
    /// <para>
    /// ⚠️ NOT A LITERAL PORT, and the difference is one indirection: LilyPond asks the FONT for
    /// a glyph NAME's attachment at run time, while Lily#'s metrics are extracted per design
    /// ahead of time and keyed by note value — the same shape
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/> already takes, because a name-keyed
    /// lookup would need the extractor to keep glyph names. Making it literal means giving
    /// DesignMetrics a name→metric map; nothing needs one yet.
    /// </para>
    /// <para>
    /// A WHOLE note (and a breve) falls to the black head here, which is not LilyPond's
    /// answer — LilyPond gives an invisible stem <c>attach = 0.0</c>, the head CENTRE
    /// (stem.cc:1063-1064, <c>center_invisible</c>). Nothing asks: the drawing side gates on
    /// <c>noteValue &gt;= 2</c> (Stem::is_normal_stem) before it reads a stem x at all. Left
    /// as it was so this change moves half notes only; it wants its own point.
    /// </para>
    /// </remarks>
    public static (double X, double Y) GetNoteheadStemAttachment(int noteValue)
        => GetNoteheadStemAttachment(Design20, noteValue);

    /// <summary>The same lookup asked of ONE font — see
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/>.</summary>
    public static (double X, double Y) GetNoteheadStemAttachment(
        DesignMetrics font, int noteValue) => noteValue switch
    {
        2 => font.NoteheadHalfStemAttachment,
        _ => font.NoteheadBlackStemAttachment,
    };

    /// <summary>
    /// The stem-attachment point of the head a given (style, note value) pair draws, per
    /// stem DIRECTION: the font's <c>attachment</c> entry for an up stem,
    /// <c>attachment-down</c> for a down stem. The two are NOT mirrors on an asymmetric
    /// shape — s2triangle attaches an up stem at (1.3828, +0.1262) but a down stem at
    /// (0.2186, −0.6828), each where the shape's ink is.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/open-type-font.cc:334-369 attachment_point — DOWN reads the
    ///   font's attachment-down, falling back to rotating the UP point only for fonts
    ///   that predate it; the bundled 2.26.0 Emmentaler carries both for every stemmed
    ///   head, so the fallback branch has no reader here.
    /// LILYPOND-REF: lily/note-head.cc:164-196 Note_head::get_stem_attachment — the
    ///   normalisation round-trips (see <see cref="GetNoteheadStemAttachment(int)"/>),
    ///   so this is the font's own coordinate.
    /// <para>
    /// The DEFAULT style routes to the same s1/s2 fields the style-less overload serves,
    /// so the two spellings cannot drift; its down X is 0.000000 in every design (the
    /// left edge), which is why <see cref="EngravingDefaults.StemDownAttachX"/> could be
    /// a constant for as long as only default heads had stems.
    /// </para>
    /// </remarks>
    public static (double X, double Y) GetNoteheadStemAttachment(
        Model.NoteheadStyle style, bool up, int noteValue)
        => GetNoteheadStemAttachment(Design20, style, up, noteValue);

    /// <summary>The same lookup asked of ONE font — see
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/>.</summary>
    public static (double X, double Y) GetNoteheadStemAttachment(
        DesignMetrics font, Model.NoteheadStyle style, bool up, int noteValue)
        => (style, halfHead: noteValue == 2) switch
    {
        (Model.NoteheadStyle.Cross, true) => up
            ? font.NoteheadCrossHalfStemAttachment : font.NoteheadCrossHalfStemAttachmentDown,
        (Model.NoteheadStyle.Cross, false) => up
            ? font.NoteheadCrossBlackStemAttachment : font.NoteheadCrossBlackStemAttachmentDown,
        (Model.NoteheadStyle.Diamond, true) => up
            ? font.NoteheadDiamondHalfStemAttachment : font.NoteheadDiamondHalfStemAttachmentDown,
        (Model.NoteheadStyle.Diamond, false) => up
            ? font.NoteheadDiamondBlackStemAttachment : font.NoteheadDiamondBlackStemAttachmentDown,
        (Model.NoteheadStyle.Triangle, true) => up
            ? font.NoteheadTriangleHalfStemAttachment : font.NoteheadTriangleHalfStemAttachmentDown,
        (Model.NoteheadStyle.Triangle, false) => up
            ? font.NoteheadTriangleBlackStemAttachment : font.NoteheadTriangleBlackStemAttachmentDown,
        (Model.NoteheadStyle.Slash, true) => up
            ? font.NoteheadSlashHalfStemAttachment : font.NoteheadSlashHalfStemAttachmentDown,
        (Model.NoteheadStyle.Slash, false) => up
            ? font.NoteheadSlashBlackStemAttachment : font.NoteheadSlashBlackStemAttachmentDown,
        // The xcircle is ONE glyph for every value (EmmentalerGlyphs.GetNotehead).
        (Model.NoteheadStyle.XCircle, _) => up
            ? font.NoteheadXCircleStemAttachment : font.NoteheadXCircleStemAttachmentDown,
        (_, true) => up
            ? font.NoteheadHalfStemAttachment : font.NoteheadHalfStemAttachmentDown,
        (_, false) => up
            ? font.NoteheadBlackStemAttachment : font.NoteheadBlackStemAttachmentDown,
    };

    /// <summary>
    /// Gets the flag bounding box for a given note value and stem direction.
    /// </summary>
    /// <param name="noteValue">8=eighth, 16=sixteenth, etc.</param>
    /// <param name="stemUp">True if stem points upward</param>
    /// <returns>Flag bounding box, or default if no flag needed</returns>
    public static BBox GetFlagBBox(int noteValue, bool stemUp)
        => GetFlagBBox(Design20, noteValue, stemUp);

    /// <summary>The same lookup asked of ONE font — see
    /// <see cref="GetNoteheadBBox(DesignMetrics, int)"/>.</summary>
    public static BBox GetFlagBBox(DesignMetrics font, int noteValue, bool stemUp)
        => (noteValue, stemUp) switch
    {
        (8, true) => font.Flag8thUp,
        (8, false) => font.Flag8thDown,
        (16, true) => font.Flag16thUp,
        (16, false) => font.Flag16thDown,
        // For 32nd, 64th etc., use 16th as approximation (they're similar width)
        (>= 32, true) => font.Flag16thUp,
        (>= 32, false) => font.Flag16thDown,
        _ => default
    };

    /// <summary>The bounding box of one fetaText dynamic letter, or default if the
    /// character is not one of the seven the encoding draws dynamics from.</summary>
    private static BBox GetDynamicLetterBBox(char c) => c switch
    {
        'f' => DynamicLetterF,
        'm' => DynamicLetterM,
        'n' => DynamicLetterN,
        'p' => DynamicLetterP,
        'r' => DynamicLetterR,
        's' => DynamicLetterS,
        'z' => DynamicLetterZ,
        _ => default
    };

    /// <summary>The hmtx advance of one fetaText dynamic letter (staff spaces), or null
    /// when the character is not one of the seven the encoding draws dynamics from —
    /// the letter-feed half of DynamicText's X model (advance + GPOS kern, measured in
    /// audit/lp-geometry/probes/dynamic-text-x.ly; the kerns live in
    /// <see cref="DynamicLetterKern"/>).</summary>
    public static double? DynamicLetterAdvance(char c) => c switch
    {
        'f' => DynamicLetterFAdvance,
        'm' => DynamicLetterMAdvance,
        'n' => DynamicLetterNAdvance,
        'p' => DynamicLetterPAdvance,
        'r' => DynamicLetterRAdvance,
        's' => DynamicLetterSAdvance,
        'z' => DynamicLetterZAdvance,
        _ => null
    };

    /// <summary>
    /// Vertical ink of a dynamic label, in staff spaces from its baseline — the union of
    /// its letters' bounding boxes. False when the label is not spelled from the fetaText
    /// dynamic letters (free <c>@text</c>, the <c>cresc.</c>/<c>dim.</c> words), which
    /// LilyPond sets in a text font and Lily# draws in a serif face.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/define-grobs.scm:1438,1445,1449 DynamicText — (font-encoding .
    ///   fetaText), (stencil . ly:text-interface::print), (Y-extent .
    ///   grob::always-Y-extent-from-stencil). The grob's extent IS the drawn glyphs' ink,
    ///   which is why it differs per dynamic — <c>p</c> descends below the baseline and
    ///   <c>m</c> does not — and why no single nominal constant can be right for all of
    ///   them. Lily# had three of them (1.2 / 0.64 / 0.3) and none matched.
    ///
    /// The letter boxes come from the OUTLINE, not from LILC, and that follows from the
    /// call path rather than from a fitted number: LILC is read only by
    /// <c>get_indexed_char_dimensions</c> (lily/open-type-font.cc:372-409), which the
    /// GLYPH path uses; a text stencil goes through Modified_font_metric::text_stencil
    /// (lily/modified-font-metric.cc:125-143) to Pango, which measures the FreeType
    /// outline and never consults LILC. Confirmed rather than derived: asked of the grob
    /// on 2.26.0, <c>\p</c> reports (-0.584004 . 1.168008) where LILC holds
    /// (-0.5834 . 1.1666), and <c>\mp</c> reports (-0.584004 . 1.196016) — the union of
    /// the two OUTLINE boxes, unreachable from LILC.
    /// The leftover ~2e-5 is Pango's own quantisation of that outline; Lily# has no Pango,
    /// so it stays a named residual rather than being fitted away.
    /// </remarks>
    public static bool TryGetDynamicInk(string? text, out double bottom, out double top)
    {
        bottom = 0;
        top = 0;
        if (string.IsNullOrEmpty(text))
            return false;
        bool any = false;
        foreach (char c in text)
        {
            var box = GetDynamicLetterBBox(c);
            if (box == default)
                return false;   // not a fetaText dynamic letter — caller falls back
            bottom = any ? System.Math.Min(bottom, box.Bottom) : box.Bottom;
            top = any ? System.Math.Max(top, box.Top) : box.Top;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// The Emmentaler glyph a figured-bass character draws, with its outline box and its
    /// advance — all three per EM at the font's design size, so a caller scales them by
    /// <c>EngravingDefaults.FiguredBassFontSize / 4</c>. False for a character LilyPond has
    /// no bass-figure glyph for (Lily#'s continuation dash), which the caller draws as text.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/translation-functions.scm:349-470 <c>format-bass-figure</c> — the
    /// digits are <c>make-number-markup</c> (the <c>fetaText</c> encoding, per
    /// scm/define-markup-commands.scm:3872-3881 <c>\number</c>: "the (music) font for
    /// numbers … also contains symbols for figured bass") and the alteration is the same
    /// markup over the Unicode accidental of <c>figbass-accidental-alist</c> (:338-343).
    /// LILYPOND-REF: scm/define-grobs.scm:354 font-features of BassFigure (bass-figure-interface at :359) —
    /// <c>("tnum" "cv47" "ss01")</c>, three OpenType SUBSTITUTIONS, so they
    /// name the glyph: fixedwidth + the .alt four/seven + fattened. A numeric TIME signature
    /// declares no features and therefore draws the BASE digits; the two are different cuts,
    /// which is why the ratio between their inks says nothing about either.
    /// <para>
    /// ONE HOME, because the same three answers are wanted twice: by
    /// <c>SharedRenderer.DrawFiguredBass</c> and by <c>FiguredBassEngraver</c>'s reservation
    /// (HANDOFF §5.0 — the size and the metric source are two halves of one claim).
    /// </para>
    /// </remarks>
    public static bool TryGetFiguredBassGlyph(char c, out char glyph, out BBox outline,
        out double advance)
    {
        (glyph, outline, advance) = c switch
        {
            '0' => (Svg.EmmentalerGlyphs.FigBassDigit0, FigBassDigit0Outline, FigBassDigit0Advance),
            '1' => (Svg.EmmentalerGlyphs.FigBassDigit1, FigBassDigit1Outline, FigBassDigit1Advance),
            '2' => (Svg.EmmentalerGlyphs.FigBassDigit2, FigBassDigit2Outline, FigBassDigit2Advance),
            '3' => (Svg.EmmentalerGlyphs.FigBassDigit3, FigBassDigit3Outline, FigBassDigit3Advance),
            '4' => (Svg.EmmentalerGlyphs.FigBassDigit4, FigBassDigit4Outline, FigBassDigit4Advance),
            '5' => (Svg.EmmentalerGlyphs.FigBassDigit5, FigBassDigit5Outline, FigBassDigit5Advance),
            '6' => (Svg.EmmentalerGlyphs.FigBassDigit6, FigBassDigit6Outline, FigBassDigit6Advance),
            '7' => (Svg.EmmentalerGlyphs.FigBassDigit7, FigBassDigit7Outline, FigBassDigit7Advance),
            '8' => (Svg.EmmentalerGlyphs.FigBassDigit8, FigBassDigit8Outline, FigBassDigit8Advance),
            '9' => (Svg.EmmentalerGlyphs.FigBassDigit9, FigBassDigit9Outline, FigBassDigit9Advance),
            '♭' => (Svg.EmmentalerGlyphs.FigBassFlat, FigBassFlatOutline, FigBassFlatAdvance),
            '♮' => (Svg.EmmentalerGlyphs.FigBassNatural, FigBassNaturalOutline, FigBassNaturalAdvance),
            '♯' => (Svg.EmmentalerGlyphs.FigBassSharp, FigBassSharpOutline, FigBassSharpAdvance),
            _ => ('\0', default, 0.0),
        };
        return glyph != '\0';
    }
}
