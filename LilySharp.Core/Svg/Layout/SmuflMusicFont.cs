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

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using LilySharp.Core.Rendering;
using static LilySharp.Core.Svg.Layout.GlyphMetrics;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// A SMuFL font's own metadata — the font-specific JSON of the SMuFL specification
/// (<c>fontName</c>, <c>glyphBBoxes</c>, <c>glyphAdvanceWidths</c>, <c>glyphsWithAnchors</c>,
/// <c>engravingDefaults</c>, <c>optionalGlyphs</c> …) — read into the shape the engine asks
/// (docs/smufl-design.md §2).
/// </summary>
/// <remarks>
/// Every length in the file is in STAFF SPACES (SMuFL: the em is four of them), which is the
/// engine's unit already, so nothing is converted on the way in. A key the font does not write
/// is an empty table: Petaluma and Leland write no <c>glyphAdvanceWidths</c>, and the advances
/// then come from the font program's hmtx (<see cref="SmuflMusicFont"/>).
/// </remarks>
internal sealed class SmuflMetadata
{
    private SmuflMetadata(
        string fontName, string fontVersion,
        FrozenDictionary<string, BBox> glyphBBoxes,
        FrozenDictionary<string, double> glyphAdvanceWidths,
        FrozenDictionary<string, FrozenDictionary<string, (double X, double Y)>> glyphsWithAnchors,
        FrozenDictionary<string, double> engravingDefaults,
        FrozenDictionary<string, int> optionalGlyphs)
    {
        FontName = fontName;
        FontVersion = fontVersion;
        GlyphBBoxes = glyphBBoxes;
        GlyphAdvanceWidths = glyphAdvanceWidths;
        GlyphsWithAnchors = glyphsWithAnchors;
        EngravingDefaults = engravingDefaults;
        OptionalGlyphs = optionalGlyphs;
    }

    /// <summary>The font's name as the metadata states it (<c>fontName</c>) — the name a score
    /// writes, compared without regard to case.</summary>
    public string FontName { get; }

    /// <summary><c>fontVersion</c>, as written.</summary>
    public string FontVersion { get; }

    /// <summary><c>glyphBBoxes</c>: the box of each glyph's curves, by SMuFL name.</summary>
    public FrozenDictionary<string, BBox> GlyphBBoxes { get; }

    /// <summary><c>glyphAdvanceWidths</c>, by SMuFL name — empty when the font writes none.</summary>
    public FrozenDictionary<string, double> GlyphAdvanceWidths { get; }

    /// <summary><c>glyphsWithAnchors</c>: each glyph's anchors (<c>stemUpSE</c>, <c>stemDownNW</c>,
    /// <c>cutOutNE</c> …) by name.</summary>
    public FrozenDictionary<string, FrozenDictionary<string, (double X, double Y)>> GlyphsWithAnchors { get; }

    /// <summary><c>engravingDefaults</c>: the numeric entries (<c>staffLineThickness</c>,
    /// <c>stemThickness</c> …), in staff spaces. A non-numeric entry (<c>textFontFamily</c>) is
    /// left out.</summary>
    public FrozenDictionary<string, double> EngravingDefaults { get; }

    /// <summary><c>optionalGlyphs</c>: the font's own glyphs outside the SMuFL range, name →
    /// code point.</summary>
    public FrozenDictionary<string, int> OptionalGlyphs { get; }

    /// <summary>Reads the metadata JSON <paramref name="json"/>.</summary>
    /// <exception cref="JsonException">Not JSON.</exception>
    /// <exception cref="InvalidOperationException">No <c>fontName</c>.</exception>
    public static SmuflMetadata Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string name = root.TryGetProperty("fontName", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()!
            : throw new InvalidOperationException("SMuFL metadata without a fontName");
        string version = root.TryGetProperty("fontVersion", out var v) ? v.ToString() : "";

        var boxes = new Dictionary<string, BBox>(StringComparer.Ordinal);
        if (root.TryGetProperty("glyphBBoxes", out var bb) && bb.ValueKind == JsonValueKind.Object)
            foreach (var g in bb.EnumerateObject())
                if (g.Value.TryGetProperty("bBoxSW", out var sw) && g.Value.TryGetProperty("bBoxNE", out var ne)
                    && TryPoint(sw, out var swp) && TryPoint(ne, out var nep))
                    boxes[g.Name] = new BBox(swp.X, swp.Y, nep.X, nep.Y);

        var advances = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("glyphAdvanceWidths", out var aw) && aw.ValueKind == JsonValueKind.Object)
            foreach (var g in aw.EnumerateObject())
                if (g.Value.ValueKind == JsonValueKind.Number)
                    advances[g.Name] = g.Value.GetDouble();

        var anchors = new Dictionary<string, FrozenDictionary<string, (double X, double Y)>>(StringComparer.Ordinal);
        if (root.TryGetProperty("glyphsWithAnchors", out var ga) && ga.ValueKind == JsonValueKind.Object)
            foreach (var g in ga.EnumerateObject())
            {
                var one = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
                foreach (var a in g.Value.EnumerateObject())
                    if (TryPoint(a.Value, out var p))
                        one[a.Name] = p;
                anchors[g.Name] = one.ToFrozenDictionary(StringComparer.Ordinal);
            }

        var defaults = new Dictionary<string, double>(StringComparer.Ordinal);
        if (root.TryGetProperty("engravingDefaults", out var ed) && ed.ValueKind == JsonValueKind.Object)
            foreach (var d in ed.EnumerateObject())
                if (d.Value.ValueKind == JsonValueKind.Number)
                    defaults[d.Name] = d.Value.GetDouble();

        var optional = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("optionalGlyphs", out var og) && og.ValueKind == JsonValueKind.Object)
            foreach (var g in og.EnumerateObject())
                if (g.Value.TryGetProperty("codepoint", out var cp) && cp.ValueKind == JsonValueKind.String
                    && cp.GetString() is { } s && s.StartsWith("U+", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    optional[g.Name] = code;

        return new SmuflMetadata(name, version,
            boxes.ToFrozenDictionary(StringComparer.Ordinal),
            advances.ToFrozenDictionary(StringComparer.Ordinal),
            anchors.ToFrozenDictionary(StringComparer.Ordinal),
            defaults.ToFrozenDictionary(StringComparer.Ordinal),
            optional.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static bool TryPoint(JsonElement e, out (double X, double Y) p)
    {
        p = default;
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            return false;
        var x = e[0];
        var y = e[1];
        if (x.ValueKind != JsonValueKind.Number || y.ValueKind != JsonValueKind.Number)
            return false;
        p = (x.GetDouble(), y.GetDouble());
        return true;
    }
}

/// <summary>
/// A SMuFL font in the engine's common shape: its glyphs by the SMuFL name of each
/// <see cref="MusicGlyph"/>, its dimensions out of its own metadata, its outlines out of its
/// font program (docs/smufl-design.md §2 "他のフォントは実行時に読む").
/// </summary>
/// <remarks>
/// ONE DESIGN: a SMuFL font is not optically sized, so every <see cref="DesignAt"/> is the same
/// table and <see cref="SizedAt"/> is that table magnified by LilyPond's magstep — the "1 枚を
/// magstep で拡縮" column of §3 #4.
/// <para>
/// ⚠️ THE IMPORT STAGE'S REMAINDER, named here rather than hidden: <see cref="Kern"/> answers 0
/// until the font's GPOS is read (§3 #7, "OTF に GPOS が在れば読む"); the §3 #17 constants LilyPond
/// applies by glyph NAME to whatever font it is given, so they are Emmentaler's answers here too;
/// and <see cref="Brace"/> hands back the one <c>brace</c> glyph at its natural size, which the
/// output stage scales to the span (§3 #10).
/// </para>
/// </remarks>
internal sealed class SmuflMusicFont : MusicFont
{
    private readonly SmuflDesign _design;
    private readonly ConcurrentDictionary<double, SmuflDesign> _sized = new();
    private readonly FrozenDictionary<char, MusicGlyph> _byCodepoint;

    /// <param name="metadata">The font's metadata.</param>
    /// <param name="fontFile">The font program (OTF), an absolute path — outlines and, when the
    /// metadata writes no advances, the hmtx come from it.</param>
    /// <param name="webFontFile">The WOFF2 beside it, if the font ships one — what an SVG embeds;
    /// the OTF otherwise.</param>
    public SmuflMusicFont(SmuflMetadata metadata, string fontFile, string? webFontFile)
    {
        Metadata = metadata;
        FontFile = fontFile;
        WebFontFile = webFontFile;
        _design = new SmuflDesign(this, 1.0);
        var map = new Dictionary<char, MusicGlyph>();
        foreach (var e in MusicGlyphs.Table)
            if (CodepointOf(e.Glyph) is { } cp)
                map.TryAdd((char)cp, e.Glyph);
        _byCodepoint = map.ToFrozenDictionary();
    }

    /// <summary>The metadata this font was read from.</summary>
    public SmuflMetadata Metadata { get; }

    /// <summary>The font program's absolute path.</summary>
    public string FontFile { get; }

    /// <summary>The WOFF2's absolute path, or null when the font ships none.</summary>
    public string? WebFontFile { get; }

    /// <inheritdoc/>
    public override string Name => Metadata.FontName;

    /// <inheritdoc/>
    /// <remarks>A glyph the vocabulary names in SMuFL and the font's metadata gives a box — or
    /// one of the font's own optional glyphs by that name. A <c>feta.</c> glyph is never a
    /// SMuFL font's (its reader falls back to Emmentaler, §1).</remarks>
    public override bool Has(MusicGlyph glyph) => CodepointOf(glyph) is not null;

    private int? CodepointOf(MusicGlyph glyph)
    {
        var e = MusicGlyphs.Of(glyph);
        if (e.SmuflCodepoint != 0 && Metadata.GlyphBBoxes.ContainsKey(e.SmuflName))
            return e.SmuflCodepoint;
        if (Metadata.OptionalGlyphs.TryGetValue(e.SmuflName, out int optional))
            return optional;
        return null;
    }

    /// <inheritdoc/>
    public override char Codepoint(MusicGlyph glyph)
        => (char)(CodepointOf(glyph)
                  ?? throw new KeyNotFoundException($"{Name} has no {MusicGlyphs.SmuflName(glyph)}"));

    /// <inheritdoc/>
    public override MusicGlyph? GlyphOf(char codepoint)
        => _byCodepoint.TryGetValue(codepoint, out var glyph) ? glyph : null;

    /// <inheritdoc/>
    /// <remarks>One design, whatever the size.</remarks>
    public override MusicFontDesign DesignAt(double fontSizeStep) => _design;

    /// <inheritdoc/>
    public override MusicFontDesign SizedAt(double fontSizeStep)
        => fontSizeStep == 0
            ? _design
            : _sized.GetOrAdd(fontSizeStep, (s, font) => new SmuflDesign(font, EmmentalerDesignSize.Magstep(s)), this);

    /// <inheritdoc/>
    /// <remarks>One design, whatever the number.</remarks>
    public override MusicFontDesign Design(int rounded) => _design;

    /// <inheritdoc/>
    /// <remarks>0 until the font program's GPOS pair kerning is read (§3 #7).</remarks>
    public override double Kern(MusicGlyph first, MusicGlyph second) => 0.0;

    /// <inheritdoc/>
    public override SkiaSharp.SKPath? OutlinePath(MusicGlyph glyph, int rounded)
        => TextFontMetrics.MusicGlyphPathFromFile(FontFile, Codepoint(glyph));

    /// <inheritdoc/>
    /// <remarks>One face: the font's own name, for every design number.</remarks>
    public override string FaceFamily(int rounded) => Name;

    /// <inheritdoc/>
    public override string FaceFile(int rounded) => Path.GetFileName(FontFile);

    /// <inheritdoc/>
    public override string WebFaceFile(int rounded) => Path.GetFileName(WebFontFile ?? FontFile);

    /// <inheritdoc/>
    public override bool TryParseFamily(string family, out int rounded)
    {
        rounded = DefaultDesign;
        return string.Equals(family, Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    /// <remarks>The one <c>brace</c> glyph (U+E000) at its natural size, as wide as its box —
    /// the output stage scales it to the span (§3 #10's "拡縮"); no ladder.</remarks>
    public override SystemBrace Brace(double length)
    {
        double width = Metadata.GlyphBBoxes.TryGetValue("brace", out var box) ? box.Width : 0.0;
        return new('', width);
    }

    /// <inheritdoc/>
    /// <remarks>LilyPond applies this by the glyph's NAME to whatever font draws it, so a SMuFL
    /// font takes Emmentaler's answer (§3 #17's "輪郭から（要設計）" is the better one).</remarks>
    public override double StemSidePaddingFraction(MusicGlyph glyph)
        => EmmentalerMusicFont.Instance.StemSidePaddingFraction(glyph);

    /// <inheritdoc/>
    /// <remarks>As <see cref="StemSidePaddingFraction"/>: LilyPond's chord-name.scm constants
    /// are applied by alteration to any font.</remarks>
    public override (double Kern, double Raise) ChordNameAccidental(MusicGlyph glyph)
        => EmmentalerMusicFont.Instance.ChordNameAccidental(glyph);

    /// <summary>The one design of a SMuFL font, at a magnification.</summary>
    private sealed class SmuflDesign : MusicFontDesign
    {
        private const double StaffSpacesPerEm = 4.0;
        private readonly SmuflMusicFont _font;
        private readonly ConcurrentDictionary<MusicGlyph, MusicGlyphMetrics> _metrics = new();
        private readonly ConcurrentDictionary<MusicGlyph, (HorizontalSkyline Left, HorizontalSkyline Right)> _horizontal = new();
        private readonly ConcurrentDictionary<MusicGlyph, (double[] Down, double[] Up)> _vertical = new();

        public SmuflDesign(SmuflMusicFont font, double magnification)
        {
            _font = font;
            Magnification = magnification;
        }

        /// <inheritdoc/>
        /// <remarks>The score's own staff: a SMuFL font is drawn at the size asked, so its
        /// design size is the base size.</remarks>
        public override double DesignSize => EmmentalerDesignSize.BaseSizePoints;

        /// <inheritdoc/>
        /// <remarks>A font with one design answers the score's own number.</remarks>
        public override int Rounded => EmmentalerDesignSize.BestRounded(EmmentalerDesignSize.BaseSizePoints).Rounded;

        /// <inheritdoc/>
        public override double Magnification { get; }

        /// <inheritdoc/>
        public override MusicFontDesign Unscaled => _font._design;

        /// <inheritdoc/>
        public override MusicFontDesign Scaled(double factor) => new SmuflDesign(_font, Magnification * factor);

        /// <inheritdoc/>
        /// <remarks>The metadata's box serves as BOTH boxes (a SMuFL font has no LILC box,
        /// §3 #1); the advance is the metadata's, else the hmtx's; the anchors are
        /// <c>stemUpSE</c> / <c>stemDownNW</c>. All at this design's magnification.</remarks>
        public override MusicGlyphMetrics Metrics(MusicGlyph glyph)
            => _metrics.GetOrAdd(glyph, static (g, self) => self.ReadMetrics(g), this);

        private MusicGlyphMetrics ReadMetrics(MusicGlyph glyph)
        {
            if (!_font.Has(glyph))
                return default;
            var meta = _font.Metadata;
            string name = MusicGlyphs.SmuflName(glyph);
            double m = Magnification;
            BBox? box = meta.GlyphBBoxes.TryGetValue(name, out var b)
                ? new BBox(b.Left * m, b.Bottom * m, b.Right * m, b.Top * m)
                : null;
            double? advance = meta.GlyphAdvanceWidths.TryGetValue(name, out double a)
                ? a * m
                : TextFontMetrics.MusicGlyphAdvanceFromFile(_font.FontFile, _font.Codepoint(glyph)) is { } ems
                    ? ems * StaffSpacesPerEm * m
                    : null;
            (double X, double Y)? up = null, down = null;
            if (meta.GlyphsWithAnchors.TryGetValue(name, out var anchors))
            {
                if (anchors.TryGetValue("stemUpSE", out var u)) up = (u.X * m, u.Y * m);
                if (anchors.TryGetValue("stemDownNW", out var d)) down = (d.X * m, d.Y * m);
            }
            return new MusicGlyphMetrics(box, box, advance, up, down);
        }

        /// <inheritdoc/>
        /// <remarks>Walked from the outline at the design's own size (the §2 rule: LilyPond's
        /// flattening, <see cref="TextOutlineSkylines.FlattenPathHorizontal"/>); the box when
        /// the font program cannot be read. Unmagnified, as the contract says.</remarks>
        public override (HorizontalSkyline Left, HorizontalSkyline Right) HorizontalSkylinePair(MusicGlyph glyph)
            => _horizontal.GetOrAdd(glyph, static (g, self) =>
            {
                var path = self._font.Has(g) ? self._font.OutlinePath(g, 0) : null;
                if (path is { IsEmpty: false })
                {
                    var (left, right) = TextOutlineSkylines.FlattenPathHorizontal(path, StaffSpacesPerEm / 1000.0);
                    return (HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Left, left),
                            HorizontalSkyline.FromSignedBuildings(HorizontalDirection.Right, right));
                }
                var box = self._font.Has(g) ? self.Unscaled.Metrics(g).OutlineBox : null;
                return box is { } bb
                    ? (HorizontalSkyline.FromBox(bb.Bottom, bb.Top, bb.Left, bb.Right, HorizontalDirection.Left),
                       HorizontalSkyline.FromBox(bb.Bottom, bb.Top, bb.Left, bb.Right, HorizontalDirection.Right))
                    : (new HorizontalSkyline(HorizontalDirection.Left), new HorizontalSkyline(HorizontalDirection.Right));
            }, this);

        /// <inheritdoc/>
        /// <remarks>Walked from the outline at the design's own size
        /// (<see cref="TextOutlineSkylines.FlattenPath"/>); <c>default</c> when the glyph or the
        /// font program is missing.</remarks>
        public override (double[] Down, double[] Up) VerticalSkylineQuads(MusicGlyph glyph)
            => _vertical.GetOrAdd(glyph, static (g, self) =>
            {
                var path = self._font.Has(g) ? self._font.OutlinePath(g, 0) : null;
                if (path is not { IsEmpty: false })
                    return default;
                var (up, down) = TextOutlineSkylines.FlattenPath(path, StaffSpacesPerEm / 1000.0);
                return (down, up);
            }, this);
    }
}
