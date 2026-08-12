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

namespace LilySharp.Core.Syntax;

/// <summary>
/// Single source of truth for the token-set predicates (pitch, barline, clef,
/// dynamic) that the parser and the syntax layer classify tokens by. These sets
/// were previously re-spelled at ~10 sites in <c>Parser.cs</c> and independently
/// in <c>DynamicSyntax.Level</c>; centralizing them removes the drift risk (the
/// dynamic sets had already diverged from one another).
/// </summary>
internal static class SyntaxFacts
{
    /// <summary>The seven diatonic pitch token kinds (<c>c d e f g a b</c>).</summary>
    public static bool IsPitchKind(SyntaxKind kind) => kind is
        SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
        SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
        SyntaxKind.PitchB;

    /// <summary>
    /// Any barline token, INCLUDING the dashed barline (<c>!</c>). A music stream
    /// accepts all of these. Use <see cref="IsMeasureBarlineKind"/> for the
    /// chord/lyric-row set, which excludes the dashed barline.
    /// </summary>
    public static bool IsBarlineKind(SyntaxKind kind) => kind is
        SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
        SyntaxKind.DashedBar or SyntaxKind.RepeatStartBar or
        SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar;

    /// <summary>
    /// Barlines that delimit a chord-block or lyric measure. EXCLUDES the dashed
    /// barline — chord and lyric rows never treated <c>!</c> as a measure break,
    /// a behavior preserved verbatim from the original per-site predicates.
    /// </summary>
    public static bool IsMeasureBarlineKind(SyntaxKind kind) => kind is
        SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
        SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar or
        SyntaxKind.RepeatBothBar;

    /// <summary>
    /// The five clef-name keywords accepted by a <c>clef</c> declaration
    /// (treble, bass, alto, tenor, treble_8). NOTE: this is deliberately narrower
    /// than <c>PartReferenceFinder.IsClefKeyword</c>, which also accepts
    /// <see cref="SyntaxKind.Treble8UpKeyword"/>; the clef-declaration grammar
    /// does not, and that difference is preserved.
    /// </summary>
    public static bool IsClefKeyword(SyntaxKind kind) => kind is
        SyntaxKind.TrebleKeyword or SyntaxKind.BassKeyword or
        SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword or
        SyntaxKind.Treble8Keyword;

    /// <summary>
    /// The token kinds that can spell a PART NAME: a plain identifier, or one of the four
    /// clef words, which are legal part names (<c>part bass { … }</c>).
    /// </summary>
    /// <remarks>
    /// One home for the rule, shared by the parser (which decides what to consume) and the
    /// render nodes (which decide what counts as a member). They must agree: a container
    /// that KEEPS a rejected token so its width survives would otherwise hand that token
    /// back as a part name.
    /// </remarks>
    public static bool IsPartNameKind(SyntaxKind kind) => kind is
        SyntaxKind.Identifier or
        SyntaxKind.BassKeyword or SyntaxKind.TrebleKeyword or
        SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword;

    /// <summary>
    /// The eight dynamic token KINDS the lexer emits, mapped to their level.
    /// Returns <see cref="DynamicLevel.None"/> for any non-dynamic kind. The lexer
    /// remains the sole producer of these kinds; this is the single consumer-side
    /// mapping shared by the parse gate and the velocity lookup.
    /// </summary>
    public static DynamicLevel DynamicLevelForKind(SyntaxKind kind) => kind switch
    {
        SyntaxKind.DynamicPPP => DynamicLevel.PPP,
        SyntaxKind.DynamicPP => DynamicLevel.PP,
        SyntaxKind.DynamicP => DynamicLevel.P,
        SyntaxKind.DynamicMP => DynamicLevel.MP,
        SyntaxKind.DynamicMF => DynamicLevel.MF,
        SyntaxKind.DynamicF => DynamicLevel.F,
        SyntaxKind.DynamicFF => DynamicLevel.FF,
        SyntaxKind.DynamicFFF => DynamicLevel.FFF,
        _ => DynamicLevel.None
    };

    /// <summary>True if <paramref name="kind"/> is one of the eight dynamic token kinds.</summary>
    public static bool IsDynamicKind(SyntaxKind kind) => DynamicLevelForKind(kind) != DynamicLevel.None;

    /// <summary>
    /// Fixed-level dynamics recognized by TEXT rather than a dedicated token kind:
    /// the pitch-token <c>f</c>, the extended p*/f* families the lexer does not
    /// tokenize as Dynamic* kinds (<c>ppp…ppppp</c>, <c>fff…fffff</c>), and the
    /// accent dynamics (<c>fp sf sfz rf rfz fz sffz</c>) which lex as identifiers.
    /// This is the single source shared by both the parse gate
    /// (<see cref="IsDynamicText"/> reads the keys) and the velocity map
    /// (<c>DynamicSyntax.Level</c> reads the values).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DynamicLevel> DynamicTextLevels =
        new Dictionary<string, DynamicLevel>
        {
            ["ppppp"] = DynamicLevel.PPPPP,
            ["pppp"] = DynamicLevel.PPPP,
            ["ppp"] = DynamicLevel.PPP,
            ["pp"] = DynamicLevel.PP,
            ["p"] = DynamicLevel.P,
            ["mp"] = DynamicLevel.MP,
            ["mf"] = DynamicLevel.MF,
            ["fp"] = DynamicLevel.FP,
            ["f"] = DynamicLevel.F,
            ["sf"] = DynamicLevel.SF,
            ["ff"] = DynamicLevel.FF,
            ["sfz"] = DynamicLevel.SFZ,
            ["rf"] = DynamicLevel.RF,
            ["rfz"] = DynamicLevel.RFZ,
            ["fz"] = DynamicLevel.FZ,
            ["sffz"] = DynamicLevel.SFFZ,
            ["fffff"] = DynamicLevel.FFFFF,
            ["ffff"] = DynamicLevel.FFFF,
            ["fff"] = DynamicLevel.FFF,
        };

    /// <summary>True if <paramref name="text"/> is a fixed-level dynamic name (see
    /// <see cref="DynamicTextLevels"/>).</summary>
    public static bool IsDynamicText(string text) => DynamicTextLevels.ContainsKey(text);

    /// <summary>
    /// The dynamic-spanner names (<c>cresc</c>, <c>decresc</c>, <c>dim</c>) the
    /// parser accepts as dynamics but which carry no fixed velocity level — they
    /// are resolved downstream as hairpins/text spanners.
    /// </summary>
    public static bool IsDynamicSpannerName(string text) => text is "cresc" or "decresc" or "dim";
}
