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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The grobs of one note column whose SIZE LilyPond states — the rows of
/// <c>general-grace-settings</c>, plus <c>Rest</c>, which that table pointedly does not name.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/music-functions.scm:636-650 <c>general-grace-settings</c> (v2.26.0) —
/// Stem, Flag, NoteHead, TabNoteHead, Dots, Accidental, AccidentalCautionary, Script,
/// Fingering and StringNumber each state their own <c>font-size</c>; Rest does not appear.
/// </remarks>
internal enum SizedGrob
{
    /// <summary>The notehead. <c>font-size -3</c> in grace time.</summary>
    NoteHead,

    /// <summary>The fret number on a tab staff. <c>font-size -4</c> in grace time, one step
    /// below the notation head's — the one row of the table that is not −3 or −8.</summary>
    TabNoteHead,

    /// <summary>The stem. Its own <c>length-fraction</c> comes from
    /// <see cref="GrobFontSize.GraceStemDetails"/> / <see cref="EngravingDefaults.CueStemDetails"/>,
    /// which is a different property from this size.</summary>
    Stem,

    /// <summary>The flag hanging off an unbeamed stem.</summary>
    Flag,

    /// <summary>The augmentation dots.</summary>
    Dots,

    /// <summary>The accidental — <c>font-size -4</c> in grace time, one step BELOW the head's
    /// −3, which is why a grace note's placement takes two fonts.</summary>
    Accidental,

    /// <summary>
    /// A rest. ⚠️ FULL SIZE IN GRACE TIME: <c>general-grace-settings</c> never names Rest, so
    /// a grace rest reads the staff's own size. MEASURED in one book, side by side
    /// (scratch/p308/lp2/s2_gracerestchord, <c>\grace { r16 d'16 }</c>): the rest's glyph is
    /// drawn at 0.0040 and the head beside it at 0.0028 = magstep(−3), and the rest's path
    /// data is byte-identical to a main-stream rest's. See <see cref="GraceColumnInfo.IsRest"/>.
    /// </summary>
    Rest,

    /// <summary>An articulation / ornament script.</summary>
    Script,

    /// <summary>A fingering digit — <c>font-size -8</c> in grace time.</summary>
    Fingering,

    /// <summary>A string number — <c>font-size -8</c> in grace time.</summary>
    StringNumber,
}

/// <summary>
/// What <c>font-size</c> one grob of one item states, and the font that follows from it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THE TWO REDUCTIONS THIS REPOSITORY HAS ARE NOT THE SAME MECHANISM, and asking one
/// question per grob is what lets both be stated honestly:
/// </para>
/// <list type="bullet">
/// <item>A CUE is a CONTEXT with a context-wide <c>fontSize</c> — <c>\name CueVoice</c> in
/// ly/engraver-init.ly declares <c>fontSize = #-4</c> beside overrides that are not sizes.
/// One number reaches every grob the voice engraves, and it reaches them by ADDITION:
/// LILYPOND-REF: lily/font-size-engraver.cc:47-62 <c>Font_size_engraver::acknowledge_font</c>
/// — <c>font_size = size +</c> the grob's own <c>font-size</c>.</item>
/// <item>A GRACE is not a context at all — <c>grep -c "name Grace" ly/engraver-init.ly</c> is
/// <c>0</c>. What it has is <c>\consists Grace_engraver</c> inside the ordinary
/// <c>\name Voice</c> setting a PER-GROB TABLE.
/// LILYPOND-REF: scm/music-functions.scm:636-650 <c>general-grace-settings</c> — NoteHead −3,
/// Accidental −4, Fingering −8, and no row for Rest at all.</item>
/// </list>
/// <para>
/// ⇒ A single "how much smaller is this item" number cannot express the second, which is why
/// <c>GraceNoteItem</c> already carried THREE constants (<c>FontSizeStep</c>,
/// <c>AccidentalFontSizeStep</c>, and a full-size rest) before this type existed. The
/// engraver asks the GROB for its font rather than multiplying a full-size one by a scale —
/// the rule docs/HANDOFF.md §2 U8 ⒝2 states, and the reason the ordinary engravers can draw a
/// grace at all.
/// </para>
/// <para>
/// ⚠️ WHAT THIS DOES NOT YET ANSWER FOR. Lily# reduces a cue through a per-NOTE
/// <see cref="NoteItem.IsCue"/> flag rather than a context, and <c>RestItem</c> carries no
/// such flag, so a cue rest is drawn full size where LilyPond's context-wide
/// <c>fontSize</c> shrinks it.
///   departs from: lily/font-size-engraver.cc:47-62 <c>Font_size_engraver::acknowledge_font</c>
///     — the context's <c>fontSize</c> is added to EVERY grob the context acknowledges, where
///     a per-note flag reaches only the notes.
///   goes away when: a cue becomes a region on the item the way grace time is
///     (<c>MusicItem.GraceTime</c>), which is the same repair
///     <c>MusicItem.BeginsCueRegion</c>'s remarks name for <c>test/cue-region-measure</c>.
///   observed by: NOTHING — no book in the corpus puts a rest inside <c>cue { }</c>.
/// This type does NOT paper over it: <see cref="StepOf"/> answers 0 for a rest that is not in
/// grace time, because that is what a <c>RestItem</c> can honestly be asked.
/// </para>
/// </remarks>
internal static class GrobFontSize
{
    /// <summary>
    /// The stem parameters a GRACE stem is measured and drawn with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:642-643 <c>general-grace-settings</c> —
    ///   <c>(Voice Stem length-fraction 0.8)</c> and <c>(Voice Stem no-stem-extend #t)</c>.
    /// ⚠️ 0.8 IS DECLARED, NOT DERIVED: it is not <c>magstep(-3)</c> = 0.707107, which is what
    /// the CUE's <c>length-fraction</c> happens to be — ly/engraver-init.ly spells that magstep
    /// out inside <c>\name CueVoice</c>. Deriving 0.8 from the font size would make a grace
    /// stem 12% too short.
    /// <para>
    /// ⚠️ <see cref="StemDetails.NoStemExtend"/> IS THE SECOND HALF AND IT IS NOT DERIVABLE
    /// FROM THE FIRST. The same table states <c>(Voice Stem no-stem-extend #t)</c>, so a grace
    /// stem is NEVER dragged out to reach the middle staff line the way every other stem is
    /// (lily/stem.cc:591-593) — and <c>\name CueVoice</c> states no such thing, so a cue stem
    /// still is. Measured in scratch/p313/lp/g3.ly; see that field.
    /// </para>
    /// <para>
    /// MEASURED against 2.26.0, and the flagged grace stem now agrees to four digits on every
    /// case asked (scratch/p313/lp/measurements.md, <c>g2.ly</c> and <c>g3.ly</c>):
    /// <c>\grace { d'8 }</c> and <c>{ d'16 }</c> 2.80, <c>{ d'32 }</c> 3.40, <c>{ d'64 }</c>
    /// 4.00 — the duration's own length times 0.8 — with the shortening INSIDE that product
    /// (<c>{ b'16 }</c> on the middle line 2.70, <c>{ d''16 }</c> a space above it 2.60) and
    /// the middle-line extension turned off (<c>{ a16 }</c> two ledgers below it 2.80, where
    /// the full-size control is dragged to 4.00).
    /// <para>
    /// Until session 313 Lily# drew every one of those 2.475 — <c>GraceNoteEngraver.StemLength</c>
    /// multiplied the FLAT <see cref="EngravingDefaults.DefaultStemLength"/> (3.5) by the FONT
    /// scale <c>magstep(-3)</c> = 0.707107, two different numbers (0.8 against 0.7071) with
    /// the duration ignored, so the error grew with the duration: −0.325 on an eighth and
    /// −1.525 on a 64th. The ordinary stem engraver draws it now (HANDOFF §2 U8 ⒝2), which is
    /// what this table exists to let it do.
    /// </para>
    /// </para>
    /// </remarks>
    internal static readonly StemDetails GraceStemDetails =
        StemDetails.Default with
        {
            LengthFraction = EngravingDefaults.GraceBeamLengthFraction,
            NoStemExtend = true,
        };

    /// <summary>
    /// The <c>font-size</c> <paramref name="grob"/> states on <paramref name="item"/>, in
    /// LilyPond's sixths of an octave — 0 at the staff's own size.
    /// </summary>
    /// <remarks>
    /// ⚠️ GRACE TIME OUTRANKS THE CUE FLAG when an item is inside both
    /// (<c>cue { grace { … } }</c>). That is what Lily# has always drawn — the grace side
    /// model never read <see cref="NoteItem.IsCue"/> — and it is NOT what LilyPond does. The
    /// two sizes are two properties of one grob and LilyPond ADDS them, which the source states
    /// in one line —
    /// LILYPOND-REF: lily/font-size-engraver.cc:47-62 <c>Font_size_engraver::acknowledge_font</c>
    /// is <c>font_size = size +</c> the grob's own <c>font-size</c>, where <c>size</c> is the
    /// context's <c>fontSize</c>. So a grace inside a cue is −4 + −3 = −7 there and −3 here.
    ///   departs from: lily/font-size-engraver.cc:47-62 <c>Font_size_engraver::acknowledge_font</c>
    ///     — addition, not precedence.
    ///   goes away when: the cue becomes a region on the item the way grace time is, so the
    ///     two can compose instead of one winning.
    ///   observed by: NOTHING — no book in the corpus nests a grace inside a cue.
    /// </remarks>
    internal static double StepOf(MusicItem item, SizedGrob grob)
    {
        if (item.GraceTime)
            return GraceStep(grob);
        return IsCue(item) ? EngravingDefaults.CueFontSizeStep : 0;
    }

    /// <summary>
    /// The FONT <paramref name="grob"/> reads its glyph dimensions from — the design its
    /// <c>font-size</c> selects, already magnified into the page's staff spaces. Nothing read
    /// out of it is multiplied again.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/font-select.cc:115-186 select_font — one call answers both halves,
    ///   WHICH file and at WHAT magnification, and hands back a font that has applied the
    ///   second (lily/modified-font-metric.cc:62-68 get_indexed_char_dimensions).
    /// </remarks>
    internal static GlyphMetrics.DesignMetrics FontOf(MusicItem item, SizedGrob grob)
        => GlyphMetrics.AtFontSize(StepOf(item, grob));

    /// <summary>
    /// The Emmentaler design <paramref name="grob"/> is DRAWN from — the number a drawing
    /// context's music-face scope takes, paired with <see cref="FontOf"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ ONE DECISION, TWO READERS: this and <see cref="FontOf"/> must come from the same
    /// step, or the box a column reserves stops being the box its glyph fills. See
    /// <see cref="EmmentalerDesignSize"/>.
    /// </remarks>
    internal static int DesignOf(MusicItem item, SizedGrob grob)
        => EmmentalerDesignSize.ForFontSizeStep(StepOf(item, grob)).Rounded;

    /// <summary>
    /// The MAGNIFICATION <paramref name="grob"/>'s <c>font-size</c> asks for —
    /// <c>magstep(step)</c>, 1.0 at the staff's own size.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS ONLY HALF OF WHAT A FONT-SIZE DECIDES. Emmentaler is optically sized, so
    /// LilyPond picks the FILE before it scales anything; a metric wanted at this size comes
    /// from <see cref="FontOf"/> and not from multiplying a full-size one by this. The factor
    /// is what remains for the things that are genuinely a scaling — the drawn glyph's point
    /// size, and a click target's box.
    /// </remarks>
    internal static double ScaleOf(MusicItem item, SizedGrob grob)
    {
        double step = StepOf(item, grob);
        return step == 0 ? 1.0 : EmmentalerDesignSize.Magstep(step);
    }

    /// <summary>True when this item states a size other than the staff's own.</summary>
    internal static bool IsReduced(MusicItem item) => item.GraceTime || IsCue(item);

    /// <summary>
    /// One row of <c>general-grace-settings</c>.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:636-650 <c>general-grace-settings</c> (v2.26.0),
    /// read row for row:
    /// Stem −3, Flag −3, NoteHead −3, TabNoteHead −4, Dots −3, Accidental −4,
    /// AccidentalCautionary −4, Script −3, Fingering −8, StringNumber −8. The two entries
    /// that are NOT sizes (<c>Stem length-fraction 0.8</c>, <c>Stem no-stem-extend #t</c>,
    /// <c>Beam beam-thickness 0.384</c>, <c>Beam length-fraction 0.8</c>) are
    /// <see cref="GraceStemDetails"/> and <c>EngravingDefaults.GraceBeam*</c>.
    /// ⚠️ REST IS ABSENT FROM THE TABLE, and that absence is the answer — see
    /// <see cref="SizedGrob.Rest"/>.
    /// </remarks>
    private static double GraceStep(SizedGrob grob) => grob switch
    {
        SizedGrob.NoteHead or SizedGrob.Stem or SizedGrob.Flag
            or SizedGrob.Dots or SizedGrob.Script => GraceNoteItem.FontSizeStep,
        SizedGrob.TabNoteHead or SizedGrob.Accidental => GraceNoteItem.AccidentalFontSizeStep,
        SizedGrob.Fingering or SizedGrob.StringNumber => GraceFingeringFontSizeStep,
        SizedGrob.Rest => 0,
        _ => 0,
    };

    /// <summary>
    /// The <c>font-size</c> a grace's fingering and string number state.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/music-functions.scm:649-650 <c>general-grace-settings</c> —
    /// <c>(Voice Fingering font-size -8)</c> and <c>(Voice StringNumber font-size -8)</c>.
    /// ⚠️ NOT −3: these two are five steps below the head, not the same step, which is the
    /// whole reason this table is per-grob.
    /// </remarks>
    internal const double GraceFingeringFontSizeStep = -8.0;

    private static bool IsCue(MusicItem item) => item switch
    {
        NoteItem n => n.IsCue,
        ChordItem c => c.IsCue,
        _ => false,
    };
}
