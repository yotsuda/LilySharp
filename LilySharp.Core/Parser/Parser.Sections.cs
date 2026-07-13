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

using LilySharp.Core.Syntax;
using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Parser;

internal sealed partial class Parser
{
    /// <summary>
    /// Parse section declaration: section Name { ... }
    /// </summary>
    private SectionDeclarationGreen ParseSectionDeclaration()
    {
        var keyword = Expect(SyntaxKind.SectionKeyword);
        var name = ExpectPartName();
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseSectionItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new SectionDeclarationGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    private GreenNode? ParseSectionItem()
    {
        return Current.Kind switch
        {
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.TempoKeyword => ParseTempoDeclaration(),
            SyntaxKind.TimeKeyword => ParseTimeSignature(),
            SyntaxKind.PartialKeyword => ParsePartialDeclaration(),
            SyntaxKind.LyricsKeyword => ParseLyricsBlock(),
            SyntaxKind.ChordsKeyword => ParseChordPartBlock(),
            // Allow identifier or instrument keywords (bass, guitar-like names) as part names
            SyntaxKind.Identifier => ParsePartBlock(),
            // bass, treble etc. can also be part names
            SyntaxKind.BassKeyword or SyntaxKind.TrebleKeyword
                or SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword
                when Peek(1)?.Kind == SyntaxKind.OpenBrace => ParsePartBlockWithKeyword(),
            _ => null
        };
    }

    /// <summary>
    /// Parse part block when part name is a keyword (e.g., bass)
    /// </summary>
    private PartBlockGreen ParsePartBlockWithKeyword()
    {
        var partName = Advance(); // bass, treble, etc. as identifier
        var body = ParseMusicBlock();
        return new PartBlockGreen(partName, [], body);
    }

    /// <summary>
    /// Parse lyrics block: lyrics { syllable syllable | syllable | }
    /// </summary>
    /// <remarks>
    /// Lyrics are aligned with notes by measure:
    /// - Space separates syllables (each maps to next note)
    /// - Hyphen at end connects syllables within a word
    /// - ~ indicates melisma (syllable extends to next note)
    /// - _ skips a note (no lyric)
    /// - | marks measure boundary
    /// </remarks>
    private LyricsBlockGreen ParseLyricsBlock()
    {
        var keyword = Expect(SyntaxKind.LyricsKeyword);
        // Optional voice-binding name: `lyrics sop { … }` aligns to voice 'sop'.
        var name = Check(SyntaxKind.Identifier) ? Advance() : (SyntaxToken?)null;
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // Part-major lyric track: a `lyrics` block may hold its own `section`
            // blocks (dual of a part's inner sections), so a verse can be written per
            // section and replayed by the structure —
            //   lyrics { section A { Twin- kle | } section B { how | } }
            if (Check(SyntaxKind.SectionKeyword))
            {
                items.Add(ParseLyricInnerSection());
                continue;
            }

            // Per-occurrence verse: `[1. … ] [2. … ]` — different words each time the
            // enclosing section is sung (a repeat/reprise).
            if (Check(SyntaxKind.OpenBracket))
            {
                items.Add(ParseLyricVolta());
                continue;
            }

            var measure = ParseLyricMeasure();
            if (measure != null)
                items.Add(measure);
            else
                Advance();
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);

        return new LyricsBlockGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    /// <summary>A lyric track's inner section (part-major form): <c>section NAME {
    /// syllables }</c>. Its body is lyric measures bound to this lyric track; reuses
    /// <see cref="SectionDeclarationGreen"/> so it resolves through the same
    /// section-name machinery as instrument parts.</summary>
    private SectionDeclarationGreen ParseLyricInnerSection()
    {
        var keyword = Expect(SyntaxKind.SectionKeyword);
        var name = ExpectPartName();
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var measures = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // Per-occurrence verse `[1. … ] [2. … ]`: this section's words for its 1st,
            // 2nd, … playback pass.
            if (Check(SyntaxKind.OpenBracket))
            {
                measures.Add(ParseLyricVolta());
                continue;
            }

            var measure = ParseLyricMeasure();
            if (measure != null)
                measures.Add(measure);
            else
                break; // no syllable/barline consumed → at the section's close
        }
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new SectionDeclarationGreen(keyword, name, openBrace, [.. measures], closeBrace);
    }


    // chords [name] { c | g:7 c | } — a chord-symbol stream. WITH a name it is an
    // independent chord part placed in a score via `chords name` (lead-sheet row);
    // WITHOUT a name it aligns above the co-written part's staff by timing (the
    // former `chordnames` form, folded into the one keyword pre-release).
    private ChordPartBlockGreen ParseChordPartBlock()
    {
        var keyword = Expect(SyntaxKind.ChordsKeyword);
        var name = Check(SyntaxKind.Identifier) ? Advance() : (SyntaxToken?)null;
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var items = new List<GreenNode?>();

        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // Part-major chord track: a `chords` block may hold its own `section`
            // blocks (dual of a part's inner sections), so a chord progression can be
            // written per section and replayed by the structure —
            //   chords harmony { section A { c1 | f1 } section B { c1 } }
            if (Check(SyntaxKind.SectionKeyword))
            {
                items.Add(ParseChordInnerSection());
                continue;
            }

            var item = ParseChordBodyItem();
            if (item != null)
                items.Add(item);
            else
                Advance(); // error recovery — skip stray tokens
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new ChordPartBlockGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    /// <summary>A chord track's inner section (part-major form): <c>section NAME {
    /// chord-entries }</c>. Its body is chord entries + barlines (not general music),
    /// bound to this chord part; reuses <see cref="SectionDeclarationGreen"/> so it
    /// resolves through the same section-name machinery as instrument parts.</summary>
    private SectionDeclarationGreen ParseChordInnerSection()
    {
        var keyword = Expect(SyntaxKind.SectionKeyword);
        var name = ExpectPartName();
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var items = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            var item = ParseChordBodyItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new SectionDeclarationGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    /// <summary>One item of a chord-block body: a barline or a chord entry, or null
    /// for a stray token (the caller skips it). Shared by the flat and the
    /// per-section chord-block forms.</summary>
    private GreenNode? ParseChordBodyItem()
    {
        if (SyntaxFacts.IsMeasureBarlineKind(Current.Kind))
            return ParseBarline();
        if (SyntaxFacts.IsPitchKind(Current.Kind))
            return ParseChordEntry();
        return null;
    }

    private static bool IsQualityToken(SyntaxKind kind) => kind is SyntaxKind.Identifier
        or SyntaxKind.IntegerLiteral
        or SyntaxKind.Dot or SyntaxKind.Minus;

    // root[duration][:quality][/bass] — reuses the pitch and duration grammar.
    private ChordEntryGreen ParseChordEntry()
    {
        var root = ParsePitch();
        var duration = ParseOptionalDuration();

        SyntaxToken? colon = null, slash = null;
        GreenNode? bass = null;
        var qualityTokens = new List<GreenNode?>();

        // ':quality' — the WHOLE run of tokens directly after the colon with no
        // intervening whitespace, so m7 / maj7 / 7sus4 / m7.5- are captured as one
        // string instead of just their first token. Whitespace (a token's trailing
        // trivia), a '/' bass, a barline or '}' ends the run.
        if (Check(SyntaxKind.Colon))
        {
            colon = Advance();
            if (IsQualityToken(Current.Kind))
            {
                var prev = Advance();
                qualityTokens.Add(prev);
                while (prev.TrailingTriviaWidth == 0 && IsQualityToken(Current.Kind))
                {
                    prev = Advance();
                    qualityTokens.Add(prev);
                }
            }
        }

        // '/bass' — a slash bass pitch (c/g).
        if (Check(SyntaxKind.Slash))
        {
            slash = Advance();
            if (SyntaxFacts.IsPitchKind(Current.Kind))
                bass = ParsePitch();
        }

        return new ChordEntryGreen(root, duration, colon, [.. qualityTokens], slash, bass);
    }

    /// <summary>Any barline token that ends a lyric measure (excludes the dashed
    /// barline, matching the chord-block set).</summary>
    private static bool IsLyricBarline(SyntaxKind kind) => SyntaxFacts.IsMeasureBarlineKind(kind);

    /// <summary>Parse a per-occurrence lyric verse: <c>[1. syllable syllable | … ]</c>.
    /// The header selects the section's playback occurrence(s) — a single number, a
    /// comma list (<c>[1,3. …]</c>), a dash range (<c>[1-2. …]</c>), or a leading
    /// <c>~</c> for "every occurrence EXCEPT these" (<c>[~1. …]</c>) — so a
    /// repeated/reprised section can carry different words each pass; the body is
    /// ordinary lyric measures. The closing <c>]</c> is optional (a run to the block's
    /// <c>}</c> recovers).</summary>
    private LyricVoltaGreen ParseLyricVolta()
    {
        var openBracket = Expect(SyntaxKind.OpenBracket);

        // Header: `~? number ((',' | '-') number)*`. `~` negates the set (all but
        // these); ',' lists, '-' ranges — mirroring the form's volta selector.
        var header = new List<GreenNode?>();
        if (Check(SyntaxKind.Tilde))
            header.Add(Advance());
        header.Add(Expect(SyntaxKind.IntegerLiteral));
        while (Check(SyntaxKind.Comma) || Check(SyntaxKind.Minus))
        {
            header.Add(Advance());                       // ',' or '-'
            header.Add(Expect(SyntaxKind.IntegerLiteral));
        }

        var dot = Expect(SyntaxKind.Dot);

        var measures = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBracket) && !Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            var measure = ParseLyricMeasure();
            if (measure != null)
                measures.Add(measure);
            else
                break;
        }

        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;
        return new LyricVoltaGreen(openBracket, [.. header], dot, [.. measures], closeBracket);
    }

    /// <summary>
    /// Parse a single lyric measure: syllable syllable ... |
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:90-120 stop_translation_timestep
    /// </remarks>
    private LyricMeasureGreen? ParseLyricMeasure()
    {
        var syllables = new List<GreenNode?>();

        while (!IsLyricBarline(Current.Kind) && !Check(SyntaxKind.CloseBrace)
            && !Check(SyntaxKind.CloseBracket) && !Check(SyntaxKind.EndOfFile))
        {
            var syllable = ParseLyricSyllable();
            if (syllable != null)
            {
                syllables.Add(syllable);
            }
            else
            {
                // Unknown token - skip to prevent infinite loop (error recovery)
                Advance();
            }
        }

        // Any barline kind ends a lyric measure — single `|`, compound `||`/`|.`,
        // or a repeat bar — so a measure break is honored however it's written.
        if (IsLyricBarline(Current.Kind))
        {
            var barline = Advance();
            return new LyricMeasureGreen([.. syllables], barline);
        }

        // No barline found - might be at end of block
        if (syllables.Count > 0)
        {
            // Create a synthetic, zero-width barline token. This is a NON-error
            // path (a valid lyrics block whose last measure omits the trailing
            // '|' before '}'), so the token must add no width or every following
            // node's Position would drift and ToFullString would emit a phantom '|'.
            var syntheticBar = new SyntaxToken(SyntaxKind.Bar, "", null, null);
            return new LyricMeasureGreen([.. syllables], syntheticBar);
        }

        return null;
    }

    /// <summary>
    /// Parse a single lyric syllable.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:60-88 process_music
    ///
    /// Handles:
    /// - Identifier: syllable text (e.g., "Hap", "py")
    /// - Identifier + Minus: word continuation (e.g., "Hap-")
    /// - Minus Minus: syllable break marker (--)
    /// - Tilde: melisma (~)
    /// - Underscore: extender (_)
    /// </remarks>
    private LyricSyllableGreen? ParseLyricSyllable()
    {
        // Melisma: ~
        if (Check(SyntaxKind.Tilde))
        {
            return new LyricSyllableGreen(Advance());
        }

        // Skip/Extender: _
        if (Check(SyntaxKind.Underscore))
        {
            return new LyricSyllableGreen(Advance());
        }

        // Hyphen connector: -- (syllable break marker)
        // In Lilypond, -- separates syllables within a word
        if (Check(SyntaxKind.Minus))
        {
            var first = Advance();
            if (Check(SyntaxKind.Minus))
            {
                var second = Advance(); // consume second minus
                // Return as special marker token, preserving the outer trivia of
                // the two consumed minuses so the tree round-trips (adjacent "--").
                return new LyricSyllableGreen(
                    new SyntaxToken(SyntaxKind.Identifier, "--",
                        first.LeadingTrivia, second.TrailingTrivia));
            }
            // Single minus - treat as connector (rare but handle gracefully)
            return new LyricSyllableGreen(first);
        }

        // Text syllable: an identifier, OR any word that merely happens to lex as
        // a reserved token. Inside a lyrics block everything up to | or } is free
        // text, so syllables like "to", "time", "key" (keywords elsewhere) and
        // single letters that look like pitches ("a".."g") or dynamics ("f","p")
        // must still render. The special lyric tokens (~ _ -) are handled above
        // and the measure delimiters (| }) are stopped by the caller, so anything
        // reaching here is a syllable — normalize it to a plain identifier token.
        var text = Advance();
        var word = new System.Text.StringBuilder(text.Text);

        // A word can lex as several ADJACENT tokens (no whitespace between them):
        // an apostrophe splits "'ry"/"don't", a letter run, etc. Glue any
        // immediately-following token onto the syllable so one written word is one
        // syllable (else the stray "'" consumes a note of its own and shifts every
        // following syllable). Whitespace lives as TRAILING trivia on the previous
        // token, so "no space between" means the previous token has no trailing
        // trivia. Stop at a connector (- ~ _) or a delimiter (| }).
        var prev = text;
        bool merged = false;
        while (true)
        {
            if (prev.TrailingTriviaWidth != 0 || Current.LeadingTriviaWidth != 0)
                break;
            var k = Current.Kind;
            if (k is SyntaxKind.Bar or SyntaxKind.CloseBrace or SyntaxKind.EndOfFile
                or SyntaxKind.Minus or SyntaxKind.Underscore)
                break;
            if (k == SyntaxKind.Tilde)
            {
                // An INTERIOR '~' (glued on both sides: "va~ga") is a lyric
                // ELISION and belongs to the word; a '~' at a word boundary
                // stays the melisma marker.
                // LILYPOND-REF: lyric tie "va~ga".
                var nxt = Peek(1);
                bool interior = Current.TrailingTriviaWidth == 0
                    && nxt != null && nxt.LeadingTriviaWidth == 0
                    && nxt.Kind != SyntaxKind.Bar && nxt.Kind != SyntaxKind.CloseBrace
                    && nxt.Kind != SyntaxKind.EndOfFile && nxt.Kind != SyntaxKind.Minus
                    && nxt.Kind != SyntaxKind.Tilde && nxt.Kind != SyntaxKind.Underscore;
                if (!interior)
                    break;
            }
            prev = Advance();
            word.Append(prev.Text);
            merged = true;
        }

        // Trailing hyphen (word continuation, e.g. "Hap-"). Keep the first token's
        // leading trivia and the hyphen's trailing trivia so the tree round-trips.
        if (Check(SyntaxKind.Minus))
        {
            var hyphen = Advance();
            return new LyricSyllableGreen(
                new SyntaxToken(SyntaxKind.Identifier, word.Append(hyphen.Text).ToString(),
                    text.LeadingTrivia, hyphen.TrailingTrivia));
        }

        // A lone identifier is returned verbatim so its trivia is preserved exactly.
        if (!merged && text.Kind == SyntaxKind.Identifier)
            return new LyricSyllableGreen(text);

        // Otherwise rebuild as one identifier, keeping the outer trivia.
        return new LyricSyllableGreen(
            new SyntaxToken(SyntaxKind.Identifier, word.ToString(),
                text.LeadingTrivia, prev.TrailingTrivia));
    }

    /// <summary>
    /// Parse part block: partName [options] { ... } or partName [options] relative c' { ... }
    /// </summary>
    private PartBlockGreen ParsePartBlock()
    {
        var partName = ExpectPartName();

        // Parse optional options (transpose, octave, instrument, clef)
        var options = new List<GreenNode?>();
        while (IsPartOption())
        {
            options.Add(ParsePartOption());
        }

        // Body can be: { ... } or variable reference
        GreenNode body;
        if (Check(SyntaxKind.OpenBrace))
        {
            body = ParseMusicBlock();
        }
        else if (Check(SyntaxKind.Identifier))
        {
            // Variable reference: partName { existingVariable }
            body = new VariableReferenceGreen(Advance());
        }
        else
        {
            // Error recovery: expect a block
            body = ParseMusicBlock();
        }

        return new PartBlockGreen(partName, [.. options], body);
    }

    private bool IsPartOption()
    {
        return Current.Kind is SyntaxKind.TransposeKeyword
            or SyntaxKind.OctaveKeyword
            or SyntaxKind.InstrumentKeyword
            or SyntaxKind.ClefKeyword;
    }

    private GreenNode ParsePartOption()
    {
        var keyword = Advance(); // transpose, octave, instrument, or clef
        // Attributes are written bare ('transpose d'), matching the top-level
        // part-property form (ParsePartProperty). A stray ':' is flagged as
        // legacy and consumed rather than demanded — before, Expect(Colon) here
        // raised a spurious "Expected ':'" on the canonical bare form.
        var colon = ConsumeRejectedColon();
        var value = Advance(); // value token
        return new PropertyAssignmentGreen(keyword, colon, [value]);
    }
}
