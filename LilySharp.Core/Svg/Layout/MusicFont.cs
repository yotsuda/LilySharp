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

using static LilySharp.Core.Svg.Layout.GlyphMetrics;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// One glyph's font-derived dimensions, in the staff spaces of the design they were read from.
/// </summary>
/// <param name="DesignBox">The box the layout places the glyph by. For Emmentaler this is the LILC
/// box LilyPond lays out with (lily/open-type-font.cc:390-408) — an EXTENSION SMuFL has no field
/// for (docs/smufl-design.md §3 #1); a SMuFL font answers its <c>glyphBBoxes</c> here too.</param>
/// <param name="OutlineBox">The box of the glyph's curves — SMuFL's <c>glyphBBoxes</c>, and what a
/// skyline is built from (lily/stencil-integral.cc:535-563).</param>
/// <param name="Advance">The horizontal feed to the next glyph — SMuFL's <c>glyphAdvanceWidths</c>,
/// Emmentaler's hmtx.</param>
/// <remarks>A field the font has nothing for is null: an Emmentaler glyph the generators do not
/// measure (a 32nd flag, a quarter-tone accidental) has no row, and its readers keep their own
/// rule until they move onto this type.</remarks>
internal readonly record struct MusicGlyphMetrics(BBox? DesignBox, BBox? OutlineBox, double? Advance);

/// <summary>
/// A music font in the engine's common shape: glyphs by <see cref="MusicGlyph"/>, dimensions in
/// the shape of SMuFL's font-specific metadata plus the LilyPond extensions SMuFL lacks
/// (docs/smufl-design.md §2).
/// </summary>
/// <remarks>
/// ⚠️ THE FOUNDATION STEP (第857): only <see cref="EmmentalerMusicFont"/> exists and nothing reads
/// through this type yet — the readers move onto it one family at a time, each step gated on an
/// unchanged sweep (§6 ①).
/// </remarks>
internal abstract class MusicFont
{
    [System.ThreadStatic] private static MusicFont? t_current;

    /// <summary>The music font of the layout or render running on this thread — Emmentaler
    /// outside one, and (until the import stage of docs/smufl-design.md §6) inside one too.</summary>
    /// <remarks>Held the way <see cref="EngravingStyle.Current"/> is, so a static helper that
    /// picks a glyph needs no font threaded through its callers.
    /// ⚠️ A cache keyed by a glyph's CHARACTER is keyed by this font as well: when a second font
    /// becomes reachable, such a key must carry the font (the import stage's checklist).</remarks>
    internal static MusicFont Current => t_current ?? EmmentalerMusicFont.Instance;

    /// <summary>Holds <paramref name="font"/> as <see cref="Current"/> until the scope is
    /// disposed; scopes nest.</summary>
    internal static Scope Use(MusicFont font) => new(font);

    /// <summary>The scope <see cref="Use"/> opens.</summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly MusicFont? _previous;

        internal Scope(MusicFont font)
        {
            _previous = t_current;
            t_current = ReferenceEquals(font, EmmentalerMusicFont.Instance) ? null : font;
        }

        /// <summary>Restores the font that was current before.</summary>
        public void Dispose() => t_current = _previous;
    }

    /// <summary>The name a score writes for the font (<c>fonts { music "…" }</c>).</summary>
    public abstract string Name { get; }

    /// <summary>Whether the font draws <paramref name="glyph"/>.</summary>
    public abstract bool Has(MusicGlyph glyph);

    /// <summary>The character the font draws <paramref name="glyph"/> with.</summary>
    /// <exception cref="KeyNotFoundException">The font does not have the glyph (<see cref="Has"/>).</exception>
    public abstract char Codepoint(MusicGlyph glyph);

    /// <summary>
    /// The design a grob carrying <paramref name="fontSizeStep"/> reads — LilyPond's
    /// <c>font-size</c>, in sixths of an octave (full size 0, a grace −3).
    /// </summary>
    /// <remarks>
    /// An optically sized font (Emmentaler: eight designs, docs/smufl-design.md §3 #4) answers the
    /// design the size lands on, in THAT design's staff spaces; a font with one design answers it
    /// whatever the size. Either way the page's staff spaces are the result times
    /// <c>magstep(fontSizeStep)</c>, as <see cref="GlyphMetrics.ForFontSizeStep"/> documents.
    /// </remarks>
    public abstract MusicFontDesign DesignAt(double fontSizeStep);

    /// <summary>
    /// The design a grob carrying <paramref name="fontSizeStep"/> reads, ALREADY in the page's
    /// staff spaces — nothing read out of it needs scaling.
    /// </summary>
    /// <remarks>The shape of <see cref="GlyphMetrics.AtFontSize"/>, which documents why the
    /// factor is <c>magstep(fontSizeStep)</c> and not the design's own magnification.</remarks>
    public abstract MusicFontDesign SizedAt(double fontSizeStep);

    /// <summary>The design a full-size grob reads — <see cref="SizedAt"/> at font-size 0.</summary>
    public MusicFontDesign FullSize => SizedAt(0);
}

/// <summary>One design of a <see cref="MusicFont"/> — the table its dimensions are read from.</summary>
internal abstract class MusicFontDesign
{
    /// <summary>The design size in points (Emmentaler's LILY table: 11.22 … 25.20 — §3 #5).</summary>
    public abstract double DesignSize { get; }

    /// <summary>The dimensions of <paramref name="glyph"/> in this design's staff spaces.</summary>
    public abstract MusicGlyphMetrics Metrics(MusicGlyph glyph);

    /// <summary>The box the layout places <paramref name="glyph"/> by
    /// (<see cref="MusicGlyphMetrics.DesignBox"/>).</summary>
    /// <exception cref="InvalidOperationException">The font has no box for the glyph.</exception>
    public BBox Box(MusicGlyph glyph) => Metrics(glyph).DesignBox ?? throw Missing(glyph, "box");

    /// <summary>The box of <paramref name="glyph"/>'s curves — what its skyline is built from
    /// (<see cref="MusicGlyphMetrics.OutlineBox"/>).</summary>
    /// <exception cref="InvalidOperationException">The font has no outline box for the glyph.</exception>
    public BBox Outline(MusicGlyph glyph) => Metrics(glyph).OutlineBox ?? throw Missing(glyph, "outline box");

    /// <summary>The horizontal feed after <paramref name="glyph"/>
    /// (<see cref="MusicGlyphMetrics.Advance"/>).</summary>
    /// <exception cref="InvalidOperationException">The font has no advance for the glyph.</exception>
    public double Advance(MusicGlyph glyph) => Metrics(glyph).Advance ?? throw Missing(glyph, "advance");

    private static InvalidOperationException Missing(MusicGlyph glyph, string what)
        => new($"the music font has no {what} for {MusicGlyphs.SmuflName(glyph)}");
}

/// <summary>
/// Emmentaler wrapped in the common shape. Every value is read from the generated tables
/// (<c>GlyphMetricsGenerated.cs</c>, <c>EmmentalerGlyphs.Generated.cs</c>) — this class adds no
/// number of its own, which is what keeps the wrap byte-invariant.
/// </summary>
internal sealed class EmmentalerMusicFont : MusicFont
{
    /// <summary>The one instance.</summary>
    public static readonly EmmentalerMusicFont Instance = new();

    private static readonly EmmentalerDesign[] Designs =
        AllDesigns.Select(d => new EmmentalerDesign(d)).ToArray();

    private EmmentalerMusicFont()
    {
    }

    /// <inheritdoc/>
    public override string Name => "Emmentaler";

    /// <inheritdoc/>
    /// <remarks>Every <see cref="MusicGlyph"/> member is an Emmentaler glyph — the vocabulary was
    /// cut from it (a test holds this).</remarks>
    public override bool Has(MusicGlyph glyph) => true;

    /// <inheritdoc/>
    public override char Codepoint(MusicGlyph glyph) => MusicGlyphs.Of(glyph).EmmentalerCode;

    /// <inheritdoc/>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:115-186 select_font — ported as
    /// <see cref="EmmentalerDesignSize"/>.</remarks>
    public override MusicFontDesign DesignAt(double fontSizeStep)
        => DesignOf(EmmentalerDesignSize.ForFontSizeStep(fontSizeStep).Rounded);

    /// <inheritdoc/>
    /// <remarks>Wraps <see cref="GlyphMetrics.AtFontSize"/>, so the numbers — and the sized
    /// tables' own cache — are the ones every unmoved reader still gets.</remarks>
    public override MusicFontDesign SizedAt(double fontSizeStep)
        => fontSizeStep == 0 ? FullSizeDesign : Sized.GetOrAdd(fontSizeStep, static s => new EmmentalerDesign(AtFontSize(s)));

    private static readonly EmmentalerDesign FullSizeDesign = new(AtFontSize(0));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<double, EmmentalerDesign> Sized = new();

    /// <summary>The design in <c>emmentaler-&lt;rounded&gt;.otf</c>.</summary>
    public static MusicFontDesign DesignOf(int rounded)
    {
        var table = ForDesign(rounded);
        foreach (var d in Designs)
            if (ReferenceEquals(d.Table, table))
                return d;
        throw new ArgumentOutOfRangeException(nameof(rounded), rounded, "not an Emmentaler design size");
    }

    private sealed class EmmentalerDesign(DesignMetrics table) : MusicFontDesign
    {
        public DesignMetrics Table { get; } = table;

        public override double DesignSize => Table.DesignSize;

        public override MusicGlyphMetrics Metrics(MusicGlyph glyph) => MetricsOf(glyph, Table);
    }

    /// <summary>
    /// The generated dimensions of <paramref name="glyph"/> in <paramref name="d"/> — the one
    /// place a <see cref="MusicGlyph"/> meets a <see cref="DesignMetrics"/> property name.
    /// </summary>
    /// <remarks>
    /// ⚠️ A test (<c>MusicFontTests.EveryGeneratedMetric_IsReachedByAGlyph</c>) walks
    /// <see cref="DesignMetrics"/> by reflection and fails on a property no glyph reaches — the
    /// "nothing Emmentaler has is lost" rule (docs/smufl-design.md §0 3), held mechanically.
    /// </remarks>
    internal static MusicGlyphMetrics MetricsOf(MusicGlyph glyph, DesignMetrics d) => glyph switch
    {
        MusicGlyph.GClef => new(d.ClefG, d.ClefGOutline, d.ClefGAdvance),
        MusicGlyph.GClefChange => new(d.ClefGChange, d.ClefGChangeOutline, d.ClefGChangeAdvance),
        MusicGlyph.FClef => new(d.ClefF, d.ClefFOutline, d.ClefFAdvance),
        MusicGlyph.FClefChange => new(d.ClefFChange, d.ClefFChangeOutline, d.ClefFChangeAdvance),
        MusicGlyph.CClef => new(d.ClefC, d.ClefCOutline, d.ClefCAdvance),
        MusicGlyph.CClefChange => new(d.ClefCChange, d.ClefCChangeOutline, d.ClefCChangeAdvance),
        MusicGlyph.UnpitchedPercussionClef1 => new(d.ClefPercussion, d.ClefPercussionOutline, d.ClefPercussionAdvance),
        MusicGlyph.SixStringTabClef => new(d.ClefTab, d.ClefTabOutline, d.ClefTabAdvance),

        MusicGlyph.NoteheadWhole => new(d.NoteheadWhole, d.NoteheadWholeOutline, d.NoteheadWholeAdvance),
        MusicGlyph.NoteheadHalf => new(d.NoteheadHalf, d.NoteheadHalfOutline, d.NoteheadHalfAdvance),
        MusicGlyph.NoteheadBlack => new(d.NoteheadBlack, d.NoteheadBlackOutline, d.NoteheadBlackAdvance),
        MusicGlyph.NoteheadXWhole => new(d.NoteheadCrossWhole, d.NoteheadCrossWholeOutline, d.NoteheadCrossWholeAdvance),
        MusicGlyph.NoteheadXHalf => new(d.NoteheadCrossHalf, d.NoteheadCrossHalfOutline, d.NoteheadCrossHalfAdvance),
        MusicGlyph.NoteheadXBlack => new(d.NoteheadCrossBlack, d.NoteheadCrossBlackOutline, d.NoteheadCrossBlackAdvance),
        MusicGlyph.NoteheadDiamondWhole => new(d.NoteheadDiamondWhole, d.NoteheadDiamondWholeOutline, d.NoteheadDiamondWholeAdvance),
        MusicGlyph.NoteheadDiamondHalf => new(d.NoteheadDiamondHalf, d.NoteheadDiamondHalfOutline, d.NoteheadDiamondHalfAdvance),
        MusicGlyph.NoteheadDiamondBlack => new(d.NoteheadDiamondBlack, d.NoteheadDiamondBlackOutline, d.NoteheadDiamondBlackAdvance),
        MusicGlyph.NoteheadTriangleUpWhole => new(d.NoteheadTriangleWhole, d.NoteheadTriangleWholeOutline, d.NoteheadTriangleWholeAdvance),
        MusicGlyph.NoteheadTriangleUpHalf => new(d.NoteheadTriangleHalf, d.NoteheadTriangleHalfOutline, d.NoteheadTriangleHalfAdvance),
        MusicGlyph.NoteheadTriangleUpBlack => new(d.NoteheadTriangleBlack, d.NoteheadTriangleBlackOutline, d.NoteheadTriangleBlackAdvance),
        MusicGlyph.NoteheadSlashWhiteWhole => new(d.NoteheadSlashWhole, d.NoteheadSlashWholeOutline, d.NoteheadSlashWholeAdvance),
        MusicGlyph.NoteheadSlashWhiteHalf => new(d.NoteheadSlashHalf, d.NoteheadSlashHalfOutline, d.NoteheadSlashHalfAdvance),
        MusicGlyph.NoteheadSlashHorizontalEnds => new(d.NoteheadSlashBlack, d.NoteheadSlashBlackOutline, d.NoteheadSlashBlackAdvance),
        MusicGlyph.NoteheadCircleX => new(d.NoteheadXCircle, d.NoteheadXCircleOutline, d.NoteheadXCircleAdvance),

        MusicGlyph.RestLonga => new(d.RestLonga, d.RestLongaOutline, d.RestLongaAdvance),
        MusicGlyph.RestDoubleWhole => new(d.RestDoubleWhole, d.RestDoubleWholeOutline, d.RestDoubleWholeAdvance),
        MusicGlyph.RestDoubleWholeLegerLine => new(d.RestDoubleWholeLedgered, d.RestDoubleWholeLedgeredOutline, d.RestDoubleWholeLedgeredAdvance),
        MusicGlyph.RestWhole => new(d.RestWhole, d.RestWholeOutline, d.RestWholeAdvance),
        MusicGlyph.RestWholeLegerLine => new(d.RestWholeLedgered, d.RestWholeLedgeredOutline, d.RestWholeLedgeredAdvance),
        MusicGlyph.RestHalf => new(d.RestHalf, d.RestHalfOutline, d.RestHalfAdvance),
        MusicGlyph.RestHalfLegerLine => new(d.RestHalfLedgered, d.RestHalfLedgeredOutline, d.RestHalfLedgeredAdvance),
        MusicGlyph.RestQuarter => new(d.RestQuarter, d.RestQuarterOutline, d.RestQuarterAdvance),
        MusicGlyph.Rest8th => new(d.Rest8th, d.Rest8thOutline, d.Rest8thAdvance),
        MusicGlyph.Rest16th => new(d.Rest16th, d.Rest16thOutline, d.Rest16thAdvance),
        MusicGlyph.Rest32nd => new(d.Rest32nd, d.Rest32ndOutline, d.Rest32ndAdvance),
        MusicGlyph.Rest64th => new(d.Rest64th, d.Rest64thOutline, d.Rest64thAdvance),
        MusicGlyph.Rest128th => new(d.Rest128th, d.Rest128thOutline, d.Rest128thAdvance),

        MusicGlyph.AccidentalSharp => new(d.AccidentalSharp, d.AccidentalSharpOutline, d.AccidentalSharpAdvance),
        MusicGlyph.AccidentalFlat => new(d.AccidentalFlat, d.AccidentalFlatOutline, d.AccidentalFlatAdvance),
        MusicGlyph.AccidentalNatural => new(d.AccidentalNatural, d.AccidentalNaturalOutline, d.AccidentalNaturalAdvance),
        MusicGlyph.AccidentalDoubleSharp => new(d.AccidentalDoubleSharp, d.AccidentalDoubleSharpOutline, d.AccidentalDoubleSharpAdvance),
        MusicGlyph.AccidentalDoubleFlat => new(d.AccidentalDoubleFlat, d.AccidentalDoubleFlatOutline, d.AccidentalDoubleFlatAdvance),
        MusicGlyph.AccidentalParensLeft => new(d.AccidentalLeftParen, d.AccidentalLeftParenOutline, d.AccidentalLeftParenAdvance),
        MusicGlyph.AccidentalParensRight => new(d.AccidentalRightParen, d.AccidentalRightParenOutline, d.AccidentalRightParenAdvance),

        MusicGlyph.Flag8thUp => new(d.Flag8thUp, d.Flag8thUpOutline, d.Flag8thUpAdvance),
        MusicGlyph.Flag8thDown => new(d.Flag8thDown, d.Flag8thDownOutline, d.Flag8thDownAdvance),
        MusicGlyph.Flag16thUp => new(d.Flag16thUp, d.Flag16thUpOutline, d.Flag16thUpAdvance),
        MusicGlyph.Flag16thDown => new(d.Flag16thDown, d.Flag16thDownOutline, d.Flag16thDownAdvance),

        // Both are dots.dot in Emmentaler (MusicGlyphs' remark).
        MusicGlyph.AugmentationDot or MusicGlyph.RepeatDot
            => new(d.AugmentationDot, d.AugmentationDotOutline, d.AugmentationDotAdvance),

        MusicGlyph.TimeSig0 => new(null, null, d.TimeSigDigit0Advance),
        MusicGlyph.TimeSig1 => new(null, null, d.TimeSigDigit1Advance),
        MusicGlyph.TimeSig2 => new(null, null, d.TimeSigDigit2Advance),
        MusicGlyph.TimeSig3 => new(null, null, d.TimeSigDigit3Advance),
        MusicGlyph.TimeSig4 => new(null, null, d.TimeSigDigit4Advance),
        MusicGlyph.TimeSig5 => new(null, null, d.TimeSigDigit5Advance),
        MusicGlyph.TimeSig6 => new(null, null, d.TimeSigDigit6Advance),
        MusicGlyph.TimeSig7 => new(null, null, d.TimeSigDigit7Advance),
        MusicGlyph.TimeSig8 => new(null, null, d.TimeSigDigit8Advance),
        MusicGlyph.TimeSig9 => new(null, null, d.TimeSigDigit9Advance),
        MusicGlyph.TimeSigCommon => new(d.TimeSigCommon, d.TimeSigCommonOutline, d.TimeSigCommonAdvance),
        MusicGlyph.TimeSigCutCommon => new(d.TimeSigCutCommon, d.TimeSigCutCommonOutline, d.TimeSigCutCommonAdvance),

        MusicGlyph.Fingering0 => new(d.FingeringDigit0, d.FingeringDigit0Outline, d.FingeringDigit0Advance),
        MusicGlyph.Fingering1 => new(d.FingeringDigit1, d.FingeringDigit1Outline, d.FingeringDigit1Advance),
        MusicGlyph.Fingering2 => new(d.FingeringDigit2, d.FingeringDigit2Outline, d.FingeringDigit2Advance),
        MusicGlyph.Fingering3 => new(d.FingeringDigit3, d.FingeringDigit3Outline, d.FingeringDigit3Advance),
        MusicGlyph.Fingering4 => new(d.FingeringDigit4, d.FingeringDigit4Outline, d.FingeringDigit4Advance),
        MusicGlyph.Fingering5 => new(d.FingeringDigit5, d.FingeringDigit5Outline, d.FingeringDigit5Advance),
        MusicGlyph.Fingering6 => new(d.FingeringDigit6, d.FingeringDigit6Outline, d.FingeringDigit6Advance),
        MusicGlyph.Fingering7 => new(d.FingeringDigit7, d.FingeringDigit7Outline, d.FingeringDigit7Advance),
        MusicGlyph.Fingering8 => new(d.FingeringDigit8, d.FingeringDigit8Outline, d.FingeringDigit8Advance),
        MusicGlyph.Fingering9 => new(d.FingeringDigit9, d.FingeringDigit9Outline, d.FingeringDigit9Advance),
        MusicGlyph.Figbass0 => new(d.FigBassDigit0, d.FigBassDigit0Outline, d.FigBassDigit0Advance),
        MusicGlyph.Figbass1 => new(d.FigBassDigit1, d.FigBassDigit1Outline, d.FigBassDigit1Advance),
        MusicGlyph.Figbass2 => new(d.FigBassDigit2, d.FigBassDigit2Outline, d.FigBassDigit2Advance),
        MusicGlyph.Figbass3 => new(d.FigBassDigit3, d.FigBassDigit3Outline, d.FigBassDigit3Advance),
        MusicGlyph.Figbass4 => new(d.FigBassDigit4, d.FigBassDigit4Outline, d.FigBassDigit4Advance),
        MusicGlyph.Figbass5 => new(d.FigBassDigit5, d.FigBassDigit5Outline, d.FigBassDigit5Advance),
        MusicGlyph.Figbass6 => new(d.FigBassDigit6, d.FigBassDigit6Outline, d.FigBassDigit6Advance),
        MusicGlyph.Figbass7 => new(d.FigBassDigit7, d.FigBassDigit7Outline, d.FigBassDigit7Advance),
        MusicGlyph.Figbass8 => new(d.FigBassDigit8, d.FigBassDigit8Outline, d.FigBassDigit8Advance),
        MusicGlyph.Figbass9 => new(d.FigBassDigit9, d.FigBassDigit9Outline, d.FigBassDigit9Advance),
        MusicGlyph.FigbassFlat => new(d.FigBassFlat, d.FigBassFlatOutline, d.FigBassFlatAdvance),
        MusicGlyph.FigbassNatural => new(d.FigBassNatural, d.FigBassNaturalOutline, d.FigBassNaturalAdvance),
        MusicGlyph.FigbassSharp => new(d.FigBassSharp, d.FigBassSharpOutline, d.FigBassSharpAdvance),

        MusicGlyph.FermataAbove => new(d.FermataAboveGlyph, d.FermataAboveGlyphOutline, d.FermataAboveGlyphAdvance),
        MusicGlyph.FermataBelow => new(d.FermataBelowGlyph, d.FermataBelowGlyphOutline, d.FermataBelowGlyphAdvance),
        MusicGlyph.ArticAccentAbove => new(d.ArticAccent, d.ArticAccentOutline, d.ArticAccentAdvance),
        MusicGlyph.ArticStaccatoAbove => new(d.ArticStaccato, d.ArticStaccatoOutline, d.ArticStaccatoAdvance),
        MusicGlyph.ArticTenutoAbove => new(d.ArticTenuto, d.ArticTenutoOutline, d.ArticTenutoAdvance),
        MusicGlyph.ArticStaccatissimoAbove => new(d.ArticStaccatissimoAboveGlyph, d.ArticStaccatissimoAboveGlyphOutline, d.ArticStaccatissimoAboveGlyphAdvance),
        MusicGlyph.ArticStaccatissimoBelow => new(d.ArticStaccatissimoBelowGlyph, d.ArticStaccatissimoBelowGlyphOutline, d.ArticStaccatissimoBelowGlyphAdvance),
        MusicGlyph.ArticMarcatoAbove => new(d.ArticMarcatoAbove, d.ArticMarcatoAboveOutline, d.ArticMarcatoAboveAdvance),
        MusicGlyph.ArticMarcatoBelow => new(d.ArticMarcatoBelow, d.ArticMarcatoBelowOutline, d.ArticMarcatoBelowAdvance),
        MusicGlyph.StringsUpBow => new(d.ArticUpBowAboveGlyph, d.ArticUpBowAboveGlyphOutline, d.ArticUpBowAboveGlyphAdvance),
        MusicGlyph.StringsUpBowTurned => new(d.ArticUpBowBelowGlyph, d.ArticUpBowBelowGlyphOutline, d.ArticUpBowBelowGlyphAdvance),
        MusicGlyph.StringsDownBow => new(d.ArticDownBowAboveGlyph, d.ArticDownBowAboveGlyphOutline, d.ArticDownBowAboveGlyphAdvance),
        MusicGlyph.StringsDownBowTurned => new(d.ArticDownBowBelowGlyph, d.ArticDownBowBelowGlyphOutline, d.ArticDownBowBelowGlyphAdvance),
        MusicGlyph.StringsHarmonic => new(d.ArticFlageoletGlyph, d.ArticFlageoletGlyphOutline, d.ArticFlageoletGlyphAdvance),
        MusicGlyph.PluckedSnapPizzicatoAbove => new(d.ScriptSnappizzicato, d.ScriptSnappizzicatoOutline, d.ScriptSnappizzicatoAdvance),
        MusicGlyph.KeyboardPedalPed => new(d.PedalPed, d.PedalPedOutline, d.PedalPedAdvance),
        MusicGlyph.KeyboardPedalDot => new(d.PedalDot, d.PedalDotOutline, d.PedalDotAdvance),
        MusicGlyph.KeyboardPedalUp => new(d.PedalStar, d.PedalStarOutline, d.PedalStarAdvance),

        MusicGlyph.OrnamentTrill => new(d.OrnTrillGlyph, d.OrnTrillGlyphOutline, d.OrnTrillGlyphAdvance),
        MusicGlyph.WiggleTrill => new(d.OrnTrillElementGlyph, d.OrnTrillElementGlyphOutline, d.OrnTrillElementGlyphAdvance),
        MusicGlyph.OrnamentTurn => new(d.OrnTurnGlyph, d.OrnTurnGlyphOutline, d.OrnTurnGlyphAdvance),
        MusicGlyph.OrnamentTurnInverted => new(d.OrnReverseTurnGlyph, d.OrnReverseTurnGlyphOutline, d.OrnReverseTurnGlyphAdvance),
        MusicGlyph.OrnamentShortTrill => new(d.OrnPrallGlyph, d.OrnPrallGlyphOutline, d.OrnPrallGlyphAdvance),
        MusicGlyph.OrnamentMordent => new(d.OrnMordentGlyph, d.OrnMordentGlyphOutline, d.OrnMordentGlyphAdvance),
        MusicGlyph.OrnamentTremblement => new(d.OrnPrallPrallGlyph, d.OrnPrallPrallGlyphOutline, d.OrnPrallPrallGlyphAdvance),
        MusicGlyph.WiggleArpeggiatoUp => new(d.Arpeggio, d.ArpeggioOutline, d.ArpeggioAdvance),
        MusicGlyph.Segno => new(d.MarkSegno, d.MarkSegnoOutline, d.MarkSegnoAdvance),
        MusicGlyph.Coda => new(d.MarkCoda, d.MarkCodaOutline, d.MarkCodaAdvance),
        MusicGlyph.BreathMarkComma => new(d.BreathComma, d.BreathCommaOutline, d.BreathCommaAdvance),
        MusicGlyph.Caesura => new(d.CaesuraStraight, d.CaesuraStraightOutline, d.CaesuraStraightAdvance),

        MusicGlyph.BracketTop => new(d.BracketTipUp, d.BracketTipUpOutline, d.BracketTipUpAdvance),
        MusicGlyph.BracketBottom => new(d.BracketTipDown, d.BracketTipDownOutline, d.BracketTipDownAdvance),

        MusicGlyph.DynamicPiano => new(d.DynamicLetterP, d.DynamicLetterPOutline, d.DynamicLetterPAdvance),
        MusicGlyph.DynamicMezzo => new(d.DynamicLetterM, d.DynamicLetterMOutline, d.DynamicLetterMAdvance),
        MusicGlyph.DynamicForte => new(d.DynamicLetterF, d.DynamicLetterFOutline, d.DynamicLetterFAdvance),
        MusicGlyph.DynamicRinforzando => new(d.DynamicLetterR, d.DynamicLetterROutline, d.DynamicLetterRAdvance),
        MusicGlyph.DynamicSforzando => new(d.DynamicLetterS, d.DynamicLetterSOutline, d.DynamicLetterSAdvance),
        MusicGlyph.DynamicZ => new(d.DynamicLetterZ, d.DynamicLetterZOutline, d.DynamicLetterZAdvance),
        MusicGlyph.DynamicNiente => new(d.DynamicLetterN, d.DynamicLetterNOutline, d.DynamicLetterNAdvance),

        _ => default,
    };
}
