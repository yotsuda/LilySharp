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

using System.Collections.Frozen;
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
/// <param name="StemUp">Where an UP stem attaches to a note head — SMuFL's <c>stemUpSE</c> anchor;
/// Emmentaler's LILC <c>attachment</c> (docs/smufl-design.md §3 #2: per design, three decimals of
/// the METAFONT point).</param>
/// <param name="StemDown">Where a DOWN stem attaches — SMuFL's <c>stemDownNW</c>; Emmentaler's
/// <c>attachment-down</c>, which is not the up point mirrored (the triangle's is not).</param>
/// <remarks>A field the font has nothing for is null: an Emmentaler glyph the generators do not
/// measure (a 32nd flag, a quarter-tone accidental) has no row, and its readers keep their own
/// rule until they move onto this type; a glyph that takes no stem has no anchors.</remarks>
internal readonly record struct MusicGlyphMetrics(
    BBox? DesignBox, BBox? OutlineBox, double? Advance,
    (double X, double Y)? StemUp = null, (double X, double Y)? StemDown = null);

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

    /// <summary>The optical design numbered <paramref name="rounded"/>
    /// (<see cref="MusicFontDesign.Rounded"/>), unscaled; a font with one design answers it for
    /// every number.</summary>
    public abstract MusicFontDesign Design(int rounded);

    /// <summary>The glyph the font draws with <paramref name="codepoint"/>, or null for a
    /// character that is none of its glyphs — the inverse of <see cref="Codepoint"/>.</summary>
    /// <remarks>For a layout that still carries a glyph as the font's CHARACTER
    /// (<c>ArticulationLayout.Glyph</c>, written by <c>ArticulationItem.GetGlyph</c> through
    /// <see cref="Current"/>) and needs the glyph back to ask the font for its outline.
    /// ⚠️ Two glyphs may share a character (Emmentaler draws the augmentation dot and the repeat
    /// dot with one <c>dots.dot</c>); the answer is then the first in
    /// <see cref="MusicGlyphs.Table"/> order — the same drawing either way.</remarks>
    public abstract MusicGlyph? GlyphOf(char codepoint);

    /// <summary>
    /// The GPOS pair kern between two adjacent glyphs of a text run, in the DESIGN's staff
    /// spaces — 0 for a pair the font does not kern (docs/smufl-design.md §3 #7, an
    /// EXTENSION: SMuFL metadata has no kerning).
    /// </summary>
    /// <remarks>A kern adjusts the FIRST glyph's own advance, so it goes inside the per-glyph
    /// device-pixel snap and never after it (<see cref="FetaTextRun"/>). Emmentaler kerns the
    /// fetaText dynamic letters among themselves and the plain digits among themselves
    /// (<see cref="GlyphMetrics.DynamicLetterKern"/>, <see cref="GlyphMetrics.MeterDigitKern"/>);
    /// a pair across the two cuts is 0.</remarks>
    public abstract double Kern(MusicGlyph first, MusicGlyph second);

    /// <summary>
    /// The outline of <paramref name="glyph"/> in the optical design numbered
    /// <paramref name="rounded"/>, at 1000 units per em — the frame
    /// <c>TextFontMetrics.OutlinePath</c> serves for text, and what the runtime skyline walk
    /// flattens (<see cref="TextOutlineSkylines"/>). Null when the font's file cannot be
    /// located; a glyph the face does not have comes back as its glyph 0, which is the
    /// caller's <c>IsEmpty</c> check.
    /// </summary>
    public abstract SkiaSharp.SKPath? OutlinePath(MusicGlyph glyph, int rounded);

    // ---- the faces: which file and which family name draw a design (docs/smufl-design.md §5) ----

    /// <summary>
    /// The design a glyph at the score's own size is drawn from — the one the bare family
    /// name (<see cref="FaceFamily"/>) means.
    /// </summary>
    /// <remarks>⚠️ It is what every existing SVG, PDF and PNG names, so a score with no small
    /// glyph in it is byte-identical to what it was before the other designs existed
    /// (<see cref="Rendering.EmmentalerFaces.DefaultDesign"/>, asked of the same rule).</remarks>
    public int DefaultDesign => DesignAt(0).Rounded;

    /// <summary>The font family a music glyph of design <paramref name="rounded"/> is drawn
    /// with — what an SVG writes in <c>font-family</c>, and what the PDF's resolver and the
    /// PNG's loader look a face up by.</summary>
    public abstract string FaceFamily(int rounded);

    /// <summary>The font file (OTF) of design <paramref name="rounded"/> — what the PDF embeds,
    /// the PNG rasterises from and the outline walk reads.</summary>
    public abstract string FaceFile(int rounded);

    /// <summary>The web font (WOFF2) of design <paramref name="rounded"/> — what the SVG embeds
    /// as a base64 <c>@font-face</c>.</summary>
    public abstract string WebFaceFile(int rounded);

    /// <summary>The design <paramref name="family"/> names, when it is one of this font's faces
    /// (<see cref="FaceFamily"/>'s inverse); false for any other family.</summary>
    public abstract bool TryParseFamily(string family, out int rounded);
}

/// <summary>One design of a <see cref="MusicFont"/> — the table its dimensions are read from.</summary>
internal abstract class MusicFontDesign
{
    /// <summary>The design size in points (Emmentaler's LILY table: 11.22 … 25.20 — §3 #5).</summary>
    public abstract double DesignSize { get; }

    /// <summary>Which optical design this table was read from — the rounded size in the
    /// Emmentaler file name (11 … 26; §3 #4). A font with one design answers 20, the score's own.</summary>
    /// <remarks>The key the outline skylines and the per-design caches are looked up by
    /// (<see cref="GlyphMetrics.AccidentalSkylinePair"/>).</remarks>
    public abstract int Rounded { get; }

    /// <summary>The magnification the dimensions have ALREADY been read at — 1 for a design's
    /// own table, magstep(font-size) for one <see cref="MusicFont.SizedAt"/> hands back
    /// (<see cref="GlyphMetrics.DesignMetrics.Magnification"/>).</summary>
    public abstract double Magnification { get; }

    /// <summary>This design at magnification 1, in its own staff spaces.</summary>
    public abstract MusicFontDesign Unscaled { get; }

    /// <summary>This design magnified once more by <paramref name="factor"/> — an ossia's
    /// scale on top of a grob's font-size (LilyPond's fontSize composes).</summary>
    public abstract MusicFontDesign Scaled(double factor);

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

    /// <summary>Where an up stem attaches to <paramref name="glyph"/>
    /// (<see cref="MusicGlyphMetrics.StemUp"/>).</summary>
    /// <exception cref="InvalidOperationException">The font has no up-stem anchor for the glyph.</exception>
    public (double X, double Y) StemUpAttachment(MusicGlyph glyph)
        => Metrics(glyph).StemUp ?? throw Missing(glyph, "up-stem anchor");

    /// <summary>Where a down stem attaches to <paramref name="glyph"/>
    /// (<see cref="MusicGlyphMetrics.StemDown"/>).</summary>
    /// <exception cref="InvalidOperationException">The font has no down-stem anchor for the glyph.</exception>
    public (double X, double Y) StemDownAttachment(MusicGlyph glyph)
        => Metrics(glyph).StemDown ?? throw Missing(glyph, "down-stem anchor");

    /// <summary>
    /// The (LEFT, RIGHT) horizontal skyline pair of an accidental or an accidental paren, from
    /// THIS design's own outline, in the glyph's own frame (X from the glyph origin, Y centred
    /// on the note) and the DESIGN's own staff spaces — unmagnified even on a
    /// <see cref="MusicFont.SizedAt"/> design: the caller composes in the design's spaces and
    /// applies the magnification once at the end, as it does to the boxes
    /// (<c>AccidentalPlacement.GlyphSkylinePair</c>).
    /// </summary>
    /// <remarks>⚠️ THE SAME DESIGN AS THE BOX. A glyph's box and its skyline are two readings of
    /// ONE face; taking them from different designs is the metric-versus-ink split, invisible
    /// in both halves separately — which is why the skyline is asked of the design the box
    /// came from and not of a table by number.</remarks>
    public abstract (HorizontalSkyline Left, HorizontalSkyline Right) HorizontalSkylinePair(MusicGlyph glyph);

    /// <summary>
    /// The (DOWN, UP) vertical skyline of <paramref name="glyph"/>, as raw sign-framed
    /// buildings in the glyph's own frame (X from the glyph origin, Y from the line it sits on
    /// or its baseline) — the clefs, the dynamic letters, the trill line's element and the
    /// sustain-pedal pieces, which LilyPond reads from the stencil outline
    /// (<c>grob::always-vertical-skylines-from-stencil</c>). <c>default</c> when the font
    /// bakes no outline for the glyph.
    /// </summary>
    /// <remarks>Raw rather than a built skyline because every seat wants it at a different x,
    /// y and staff size, and a <see cref="VerticalSkyline"/> is mutable; the readers resolve
    /// once per (array, size) and place copies.</remarks>
    public abstract (double[] Down, double[] Up) VerticalSkylineQuads(MusicGlyph glyph);

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
    public override MusicGlyph? GlyphOf(char codepoint)
        => ByCodepoint.TryGetValue(codepoint, out var glyph) ? glyph : null;

    // The first row wins where two share a character (the remark on MusicFont.GlyphOf).
    private static readonly FrozenDictionary<char, MusicGlyph> ByCodepoint = BuildByCodepoint();

    private static FrozenDictionary<char, MusicGlyph> BuildByCodepoint()
    {
        var map = new Dictionary<char, MusicGlyph>();
        foreach (var e in MusicGlyphs.Table)
            map.TryAdd(e.EmmentalerCode, e.Glyph);
        return map.ToFrozenDictionary();
    }

    /// <inheritdoc/>
    /// <remarks>The generated GPOS tables (<c>GlyphSkylinesGenerated.cs</c>), keyed by the
    /// fetaText characters they were extracted for — the dynamic letters' among themselves,
    /// the plain digits' among themselves.</remarks>
    public override double Kern(MusicGlyph first, MusicGlyph second)
    {
        if (MusicGlyphs.IsDynamicLetter(first) && MusicGlyphs.IsDynamicLetter(second))
            return DynamicLetterKern(Codepoint(first), Codepoint(second));
        if (MusicGlyphs.IsTimeSigDigit(first) && MusicGlyphs.IsTimeSigDigit(second))
            return MeterDigitKern(Codepoint(first), Codepoint(second));
        return 0.0;
    }

    /// <inheritdoc/>
    /// <remarks>The bundled <c>emmentaler-&lt;rounded&gt;.otf</c>, through the one measurement
    /// loader (<see cref="Rendering.TextFontMetrics.MusicGlyphPath"/>).</remarks>
    public override SkiaSharp.SKPath? OutlinePath(MusicGlyph glyph, int rounded)
        => Rendering.TextFontMetrics.MusicGlyphPath(Codepoint(glyph), rounded);

    // The bundled designs as the three backends name them (Rendering.EmmentalerFaces).

    /// <inheritdoc/>
    public override string FaceFamily(int rounded) => Rendering.EmmentalerFaces.Family(rounded);

    /// <inheritdoc/>
    public override string FaceFile(int rounded) => Rendering.EmmentalerFaces.OtfFile(rounded);

    /// <inheritdoc/>
    public override string WebFaceFile(int rounded) => Rendering.EmmentalerFaces.Woff2File(rounded);

    /// <inheritdoc/>
    public override bool TryParseFamily(string family, out int rounded)
        => Rendering.EmmentalerFaces.TryParseFamily(family, out rounded);

    /// <inheritdoc/>
    /// <remarks>LILYPOND-REF: lily/font-select.cc:115-186 select_font — ported as
    /// <see cref="EmmentalerDesignSize"/>.</remarks>
    public override MusicFontDesign DesignAt(double fontSizeStep)
        => DesignByRounded(EmmentalerDesignSize.ForFontSizeStep(fontSizeStep).Rounded);

    /// <inheritdoc/>
    /// <remarks>Wraps <see cref="GlyphMetrics.AtFontSize"/>, so the numbers — and the sized
    /// tables' own cache — are the ones every unmoved reader still gets.</remarks>
    public override MusicFontDesign SizedAt(double fontSizeStep)
        => fontSizeStep == 0 ? FullSizeDesign : Sized.GetOrAdd(fontSizeStep, static s => new EmmentalerDesign(AtFontSize(s)));

    private static readonly EmmentalerDesign FullSizeDesign = new(AtFontSize(0));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<double, EmmentalerDesign> Sized = new();

    /// <inheritdoc/>
    /// <remarks>The design in <c>emmentaler-&lt;rounded&gt;.otf</c>.</remarks>
    public override MusicFontDesign Design(int rounded) => DesignByRounded(rounded);

    private static MusicFontDesign DesignByRounded(int rounded)
    {
        var table = ForDesign(rounded);
        foreach (var d in Designs)
            if (ReferenceEquals(d.Table, table))
                return d;
        throw new ArgumentOutOfRangeException(nameof(rounded), rounded, "not an Emmentaler design size");
    }

    private sealed class EmmentalerDesign : MusicFontDesign
    {
        // Read once: the layout asks a design for a box on every grob it seats.
        private readonly MusicGlyphMetrics[] _metrics;

        public EmmentalerDesign(DesignMetrics table)
        {
            Table = table;
            var glyphs = Enum.GetValues<MusicGlyph>();
            _metrics = new MusicGlyphMetrics[glyphs.Length];
            foreach (var g in glyphs)
                _metrics[(int) g] = MetricsOf(g, table);
        }

        public DesignMetrics Table { get; }

        public override double DesignSize => Table.DesignSize;

        public override int Rounded => Table.Rounded;

        public override double Magnification => Table.Magnification;

        public override MusicFontDesign Unscaled => DesignByRounded(Table.Rounded);

        public override MusicFontDesign Scaled(double factor) => new EmmentalerDesign(Table.Scaled(factor));

        public override MusicGlyphMetrics Metrics(MusicGlyph glyph) => _metrics[(int) glyph];

        /// <inheritdoc/>
        /// <remarks>The baked pairs of this design's number (<c>GlyphSkylinesGenerated.cs</c>).
        /// A glyph the generator bakes no pair for reads the natural's — the generated
        /// accessor's own fallback, and what the quarter-tone accidentals have always read.</remarks>
        public override (HorizontalSkyline Left, HorizontalSkyline Right) HorizontalSkylinePair(MusicGlyph glyph) => glyph switch
        {
            MusicGlyph.AccidentalParensLeft => AccidentalParenSkylinePair(leftParen: true, Table.Rounded),
            MusicGlyph.AccidentalParensRight => AccidentalParenSkylinePair(leftParen: false, Table.Rounded),
            _ => AccidentalSkylinePair(AccidentalKindOf(glyph), Table.Rounded),
        };

        // The generated accessor is keyed by the resolved accidental kind — the inverse of
        // MusicGlyphs.Accidental for the five it bakes.
        private static string AccidentalKindOf(MusicGlyph glyph) => glyph switch
        {
            MusicGlyph.AccidentalSharp => "sharp",
            MusicGlyph.AccidentalFlat => "flat",
            MusicGlyph.AccidentalDoubleSharp => "doubleSharp",
            MusicGlyph.AccidentalDoubleFlat => "doubleFlat",
            _ => "natural",
        };

        /// <inheritdoc/>
        /// <remarks>⚠️ Emmentaler bakes these for the 20 design alone (the generator's header:
        /// nothing selects another design for a clef, a dynamic letter, the trill element or
        /// the pedal word), so every design answers the 20's — as the flat accessors did.</remarks>
        public override (double[] Down, double[] Up) VerticalSkylineQuads(MusicGlyph glyph) => glyph switch
        {
            MusicGlyph.GClef => ClefVerticalSkylineQuads("G"),
            MusicGlyph.FClef => ClefVerticalSkylineQuads("F"),
            MusicGlyph.CClef => ClefVerticalSkylineQuads("C"),
            MusicGlyph.WiggleTrill => TrillElementVerticalSkylineQuads(),
            MusicGlyph.KeyboardPedalPed or MusicGlyph.KeyboardPedalDot or MusicGlyph.KeyboardPedalUp
                => PedalGlyphVerticalSkylineQuads(MusicGlyphs.Of(glyph).EmmentalerCode),
            _ when MusicGlyphs.IsDynamicLetter(glyph)
                => DynamicLetterVerticalSkylineQuads(MusicGlyphs.Of(glyph).EmmentalerCode),
            _ => default,
        };
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
        MusicGlyph.NoteheadHalf => new(d.NoteheadHalf, d.NoteheadHalfOutline, d.NoteheadHalfAdvance, d.NoteheadHalfStemAttachment, d.NoteheadHalfStemAttachmentDown),
        MusicGlyph.NoteheadBlack => new(d.NoteheadBlack, d.NoteheadBlackOutline, d.NoteheadBlackAdvance, d.NoteheadBlackStemAttachment, d.NoteheadBlackStemAttachmentDown),
        MusicGlyph.NoteheadXWhole => new(d.NoteheadCrossWhole, d.NoteheadCrossWholeOutline, d.NoteheadCrossWholeAdvance),
        MusicGlyph.NoteheadXHalf => new(d.NoteheadCrossHalf, d.NoteheadCrossHalfOutline, d.NoteheadCrossHalfAdvance, d.NoteheadCrossHalfStemAttachment, d.NoteheadCrossHalfStemAttachmentDown),
        MusicGlyph.NoteheadXBlack => new(d.NoteheadCrossBlack, d.NoteheadCrossBlackOutline, d.NoteheadCrossBlackAdvance, d.NoteheadCrossBlackStemAttachment, d.NoteheadCrossBlackStemAttachmentDown),
        MusicGlyph.NoteheadDiamondWhole => new(d.NoteheadDiamondWhole, d.NoteheadDiamondWholeOutline, d.NoteheadDiamondWholeAdvance),
        MusicGlyph.NoteheadDiamondHalf => new(d.NoteheadDiamondHalf, d.NoteheadDiamondHalfOutline, d.NoteheadDiamondHalfAdvance, d.NoteheadDiamondHalfStemAttachment, d.NoteheadDiamondHalfStemAttachmentDown),
        MusicGlyph.NoteheadDiamondBlack => new(d.NoteheadDiamondBlack, d.NoteheadDiamondBlackOutline, d.NoteheadDiamondBlackAdvance, d.NoteheadDiamondBlackStemAttachment, d.NoteheadDiamondBlackStemAttachmentDown),
        MusicGlyph.NoteheadTriangleUpWhole => new(d.NoteheadTriangleWhole, d.NoteheadTriangleWholeOutline, d.NoteheadTriangleWholeAdvance),
        MusicGlyph.NoteheadTriangleUpHalf => new(d.NoteheadTriangleHalf, d.NoteheadTriangleHalfOutline, d.NoteheadTriangleHalfAdvance, d.NoteheadTriangleHalfStemAttachment, d.NoteheadTriangleHalfStemAttachmentDown),
        MusicGlyph.NoteheadTriangleUpBlack => new(d.NoteheadTriangleBlack, d.NoteheadTriangleBlackOutline, d.NoteheadTriangleBlackAdvance, d.NoteheadTriangleBlackStemAttachment, d.NoteheadTriangleBlackStemAttachmentDown),
        MusicGlyph.NoteheadSlashWhiteWhole => new(d.NoteheadSlashWhole, d.NoteheadSlashWholeOutline, d.NoteheadSlashWholeAdvance),
        MusicGlyph.NoteheadSlashWhiteHalf => new(d.NoteheadSlashHalf, d.NoteheadSlashHalfOutline, d.NoteheadSlashHalfAdvance, d.NoteheadSlashHalfStemAttachment, d.NoteheadSlashHalfStemAttachmentDown),
        MusicGlyph.NoteheadSlashHorizontalEnds => new(d.NoteheadSlashBlack, d.NoteheadSlashBlackOutline, d.NoteheadSlashBlackAdvance, d.NoteheadSlashBlackStemAttachment, d.NoteheadSlashBlackStemAttachmentDown),
        MusicGlyph.NoteheadCircleX => new(d.NoteheadXCircle, d.NoteheadXCircleOutline, d.NoteheadXCircleAdvance, d.NoteheadXCircleStemAttachment, d.NoteheadXCircleStemAttachmentDown),

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
