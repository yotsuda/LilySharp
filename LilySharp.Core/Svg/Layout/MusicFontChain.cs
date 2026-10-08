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

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// The music fonts a score names, as one font: each glyph from the first of them that has it,
/// and from Emmentaler — always the last — when none does (docs/smufl-design.md §1 "複数の名前は
/// グリフ単位のフォールバック", §6 ② ⒟).
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A CHARACTER NAMES ONE GLYPH OF ONE FONT. The fonts' private-use slots overlap (Leland's
/// <c>U+E0A4</c> is not Emmentaler's), and the layout carries a glyph as its character
/// (<see cref="MusicFont.Codepoint"/>), so a glyph drawn from a later font cannot be handed out
/// at that font's own character. It is handed out at <see cref="FallbackBase"/> plus its
/// <see cref="MusicGlyph"/> number — a lone low surrogate, which no font holds and no text
/// carries — and every backend turns it back into the font's character and face through
/// <see cref="Drawn"/>. A glyph of the FIRST font keeps its own character, so a score that needs
/// no fallback draws the characters it drew before the chain existed.
/// </para>
/// <para>
/// THE FACE NUMBER: a backend's music-face scope holds the first font's design number
/// (<see cref="MusicFont.DesignAt"/>); <see cref="Drawn"/> answers member <c>k</c>'s face as
/// <c>k × <see cref="FaceStride"/> + design</c>, and <see cref="FaceFamily"/>,
/// <see cref="FaceFile"/>, <see cref="WebFaceFile"/> and <see cref="TryParseFamily"/> read that
/// number back. So the SVG's used-face record, its fragment replay and the PDF and PNG loaders
/// carry a fallback face without knowing there is one.
/// </para>
/// <para>
/// A LATER FONT'S DIMENSIONS are read at the first font's design number, magnified as the first
/// font's are — the "1 枚を magstep で拡縮" rule of §3 #4 — because that is the face the backend
/// draws it from: an Emmentaler glyph in a Bravura score is the 20 design scaled, not the 14,
/// at a grace's size.
/// </para>
/// </remarks>
internal sealed class MusicFontChain : MusicFont
{
    /// <summary>Where the characters of glyphs drawn from a later font start: the low
    /// surrogates, which are no character on their own.</summary>
    internal const char FallbackBase = '\uDC00';

    /// <summary>The span of face numbers one member owns — above every design number
    /// (Emmentaler's run 11 … 26).</summary>
    internal const int FaceStride = 100;

    private readonly MusicFont[] _members;
    private readonly byte[] _owner;          // by (int) MusicGlyph: the member that draws it
    private readonly char[] _code;           // by (int) MusicGlyph: the character handed out
    private readonly FrozenDictionary<char, MusicGlyph> _byCodepoint;
    private readonly ChainDesign _design;
    private readonly ConcurrentDictionary<double, MusicFontDesign> _sized = new();

    /// <param name="members">The fonts in the order written; Emmentaler is appended when it is
    /// not among them, and nothing after it is reached (it draws every glyph).</param>
    public MusicFontChain(IReadOnlyList<MusicFont> members)
    {
        var list = new List<MusicFont>();
        foreach (var m in members)
        {
            if (list.Contains(m))
                continue;
            list.Add(m);
            if (ReferenceEquals(m, EmmentalerMusicFont.Instance))
                break;
        }
        if (!list.Contains(EmmentalerMusicFont.Instance))
            list.Add(EmmentalerMusicFont.Instance);
        _members = list.ToArray();

        var glyphs = Enum.GetValues<MusicGlyph>();
        int size = glyphs.Max(g => (int) g) + 1;
        _owner = new byte[size];
        _code = new char[size];
        var map = new Dictionary<char, MusicGlyph>();
        foreach (var e in MusicGlyphs.Table)
        {
            int owner = Array.FindIndex(_members, m => m.Has(e.Glyph));
            _owner[(int) e.Glyph] = (byte) owner;
            char code = owner == 0 ? _members[0].Codepoint(e.Glyph) : (char) (FallbackBase + (int) e.Glyph);
            _code[(int) e.Glyph] = code;
            map.TryAdd(code, e.Glyph);
        }
        _byCodepoint = map.ToFrozenDictionary();
        _design = new ChainDesign(this, _members.Select(m => m.Design(First.DefaultDesign)).ToArray());
    }

    /// <summary>The fonts in fallback order, Emmentaler last.</summary>
    public IReadOnlyList<MusicFont> Members => _members;

    private MusicFont First => _members[0];

    /// <summary>The font that draws <paramref name="glyph"/>.</summary>
    public MusicFont OwnerOf(MusicGlyph glyph) => _members[_owner[(int) glyph]];

    /// <inheritdoc/>
    /// <remarks>The first font's: the name the score wrote first.</remarks>
    public override string Name => First.Name;

    /// <inheritdoc/>
    /// <remarks>Every glyph — Emmentaler, the last font, draws them all.</remarks>
    public override bool Has(MusicGlyph glyph) => true;

    /// <inheritdoc/>
    public override char Codepoint(MusicGlyph glyph) => _code[(int) glyph];

    /// <inheritdoc/>
    public override MusicGlyph? GlyphOf(char codepoint)
        => _byCodepoint.TryGetValue(codepoint, out var glyph) ? glyph : null;

    /// <inheritdoc/>
    /// <remarks>A character of the first font passes through; a fallback character is its
    /// owner's character in its owner's face — and is noted, for the warning
    /// (<see cref="MusicFallbackLog"/>), when the owner is Emmentaler: a glyph none of the
    /// fonts the score names has.</remarks>
    internal override (char Code, int Face) Drawn(char glyph, int design)
    {
        int index = glyph - FallbackBase;
        if (index < 0 || index >= _code.Length || _code[index] != glyph)
            return (glyph, design);
        var g = (MusicGlyph) index;
        int owner = _owner[index];
        var font = _members[owner];
        if (ReferenceEquals(font, EmmentalerMusicFont.Instance))
            MusicFallbackLog.Note(this, g);
        return (font.Codepoint(g), owner * FaceStride + design);
    }

    /// <inheritdoc/>
    /// <remarks>The first font's design number, whatever the size (the remarks on the class).</remarks>
    public override MusicFontDesign DesignAt(double fontSizeStep) => _design;

    /// <inheritdoc/>
    public override MusicFontDesign SizedAt(double fontSizeStep)
        => fontSizeStep == 0
            ? _design
            : _sized.GetOrAdd(fontSizeStep, static (s, d) => d.Scaled(EmmentalerDesignSize.Magstep(s)), _design);

    /// <inheritdoc/>
    public override MusicFontDesign Design(int rounded) => _design;

    /// <inheritdoc/>
    /// <remarks>Within one font, that font's kern; across two, none.</remarks>
    public override double Kern(MusicGlyph first, MusicGlyph second)
        => _owner[(int) first] == _owner[(int) second] ? OwnerOf(first).Kern(first, second) : 0.0;

    /// <inheritdoc/>
    public override SkiaSharp.SKPath? OutlinePath(MusicGlyph glyph, int rounded)
        => OwnerOf(glyph).OutlinePath(glyph, rounded);

    // ---- the faces, by the numbers Drawn answers ----

    private (MusicFont Font, int Design) FaceOf(int face)
        => face >= FaceStride && face / FaceStride < _members.Length
            ? (_members[face / FaceStride], face % FaceStride)
            : (First, face);

    /// <inheritdoc/>
    public override string FaceFamily(int rounded)
    {
        var (font, design) = FaceOf(rounded);
        return font.FaceFamily(design);
    }

    /// <inheritdoc/>
    public override string FaceFile(int rounded)
    {
        var (font, design) = FaceOf(rounded);
        return font.FaceFile(design);
    }

    /// <inheritdoc/>
    public override string WebFaceFile(int rounded)
    {
        var (font, design) = FaceOf(rounded);
        return font.WebFaceFile(design);
    }

    /// <inheritdoc/>
    /// <remarks>The first member whose face it is, numbered as <see cref="Drawn"/> numbers it.</remarks>
    public override bool TryParseFamily(string family, out int rounded)
    {
        for (int k = 0; k < _members.Length; k++)
            if (_members[k].TryParseFamily(family, out int design))
            {
                rounded = k * FaceStride + design;
                return true;
            }
        rounded = DefaultDesign;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>The first font's: §1 "engravingDefaults は先頭のフォントだけ".</remarks>
    internal override EngravingStyle Engrave(EngravingStyle style) => First.Engrave(style);

    /// <inheritdoc/>
    /// <remarks>The first font's (each bundled SMuFL font has its <c>brace</c>).</remarks>
    public override SystemBrace Brace(double length) => First.Brace(length);

    /// <inheritdoc/>
    public override double StemSidePaddingFraction(MusicGlyph glyph)
        => OwnerOf(glyph).StemSidePaddingFraction(glyph);

    /// <inheritdoc/>
    public override (double Kern, double Raise) ChordNameAccidental(MusicGlyph glyph)
        => OwnerOf(glyph).ChordNameAccidental(glyph);

    /// <summary>One design of each member, read for the glyphs that member draws.</summary>
    private sealed class ChainDesign(MusicFontChain chain, MusicFontDesign[] designs) : MusicFontDesign
    {
        private MusicFontDesign First => designs[0];

        private MusicFontDesign Of(MusicGlyph glyph) => designs[chain._owner[(int) glyph]];

        public override double DesignSize => First.DesignSize;

        public override int Rounded => First.Rounded;

        public override double Magnification => First.Magnification;

        public override MusicFontDesign Unscaled => chain._design;

        public override MusicFontDesign Scaled(double factor)
            => new ChainDesign(chain, designs.Select(d => d.Scaled(factor)).ToArray());

        public override MusicGlyphMetrics Metrics(MusicGlyph glyph) => Of(glyph).Metrics(glyph);

        public override (HorizontalSkyline Left, HorizontalSkyline Right) HorizontalSkylinePair(MusicGlyph glyph)
            => Of(glyph).HorizontalSkylinePair(glyph);

        public override (double[] Down, double[] Up) VerticalSkylineQuads(MusicGlyph glyph)
            => Of(glyph).VerticalSkylineQuads(glyph);
    }
}

/// <summary>
/// The glyphs a render drew from Emmentaler because no music font the score names has them —
/// gathered while the backends draw (<see cref="MusicFontChain.Drawn"/>) and said once per glyph
/// (docs/smufl-design.md §1 "グリフ名ごとに 1 回警告").
/// </summary>
/// <remarks>Held per thread, as <see cref="MusicFont.Current"/> is: the layout and the render
/// run on the caller's thread. Nothing is gathered while no log is open.</remarks>
internal sealed class MusicFallbackLog : IDisposable
{
    [ThreadStatic] private static MusicFallbackLog? t_open;

    private readonly MusicFallbackLog? _outer;
    private readonly List<(string Font, MusicGlyph Glyph)> _seen = [];

    private MusicFallbackLog()
    {
        _outer = t_open;
        t_open = this;
    }

    /// <summary>Opens a log on this thread until it is disposed; logs nest.</summary>
    public static MusicFallbackLog Open() => new();

    internal static void Note(MusicFontChain chain, MusicGlyph glyph)
    {
        if (t_open is not { } log)
            return;
        string fonts = string.Join(", ", chain.Members
            .Where(m => !ReferenceEquals(m, EmmentalerMusicFont.Instance))
            .Select(m => m.Name));
        if (!log._seen.Contains((fonts, glyph)))
            log._seen.Add((fonts, glyph));
    }

    /// <summary>Tells <paramref name="sink"/> about each glyph drawn from Emmentaler, in the
    /// order first drawn; nothing when the sink is null.</summary>
    public void Report(Action<string>? sink)
    {
        if (sink is null)
            return;
        foreach (var (fonts, glyph) in _seen)
            sink($"the music font {fonts} has no {MusicGlyphs.SmuflName(glyph)}: it is drawn in Emmentaler");
    }

    /// <summary>Closes the log, restoring the one it nested in.</summary>
    public void Dispose() => t_open = _outer;
}
