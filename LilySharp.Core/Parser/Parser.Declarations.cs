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
    // ========== Structure Declarations ==========

    private PartDeclarationGreen ParsePartDeclaration()
    {
        var keyword = Expect(SyntaxKind.PartKeyword);
        var name = ExpectPartName();   // names may be clef-name words (bass/treble/...)

        // Optional inline display name: `part melody "Violin I"`. This is the label
        // printed for the part in every score that renders it (a score's
        // `staff X "…"` overrides it per-score). Same `symbol "label"` idiom as a
        // structure section (`A "A2"`) and a staff render (`staff X "…"`).
        SyntaxToken? displayName = Check(SyntaxKind.StringLiteral) ? Advance() : (SyntaxToken?)null;

        // Check if there's a body
        if (!Check(SyntaxKind.OpenBrace))
        {
            // No body: part name ["display"]
            return displayName is { } dnn
                ? new PartDeclarationGreen(keyword, name, dnn)
                : new PartDeclarationGreen(keyword, name);
        }

        // With body: part name ["display"] { props }
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var properties = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // Part-major form: a `part` may hold its own `section` blocks
            //   part bass { clef bass  section A { c d } section B { e f } }
            // Each inner section's music belongs to THIS part (cell = section x part).
            if (Check(SyntaxKind.SectionKeyword))
            {
                properties.Add(ParsePartInnerSection());
                continue;
            }

            // A part-body grob directive: `override Grob.prop = value` is a default for
            // this part's staff (revert/once are parsed too, then rejected by a validator —
            // they belong in a music stream, not a part header).
            if (Check(SyntaxKind.OverrideKeyword)) { properties.Add(ParseOverrideDeclaration()); continue; }
            if (Check(SyntaxKind.RevertKeyword)) { properties.Add(ParseRevertDeclaration()); continue; }
            if (Check(SyntaxKind.OnceKeyword)) { properties.Add(ParseOnceModifier()); continue; }

            // A part-header key sets this part's default key (unlike time/tempo, which are
            // score-wide, a key is legitimately per-part — e.g. transposing instruments).
            // Parse it faithfully as a KeySignature so its tokens keep their source
            // positions; without this the bare `key …` fell through to a skipped token,
            // dropping its width and shifting every following note's source offset.
            if (Check(SyntaxKind.KeyKeyword)) { properties.Add(ParseKeySignature()); continue; }

            // A `using` here already spoke — as the generic stray token below — and STILL lost
            // its width to that `Advance()`, so the loud spelling corrupted source positions
            // exactly like the four silent ones. Its own name (LYS0029) says which brace to
            // move it out of, keeps its tokens, and stops the quoted path cascading into a
            // second stray-token error about a string that was never the mistake.
            if (Check(SyntaxKind.UsingKeyword))
            {
                properties.Add(ParseMisplacedUsing("a part header"));
                continue;
            }

            var prop = ParsePartProperty();
            if (prop != null)
            {
                properties.Add(prop);
                continue;
            }

            // A token the header cannot place. This used to be a bare `Advance()`, and the
            // silence was the trap: `part m { bass }` engraved byte-for-byte as `part m { }`
            // and said "No errors found", even though a bare clef word is exactly what a
            // reader would try. (`bass` lexes as BassKeyword, not ClefKeyword, so it never
            // reached ParsePartProperty at all — it fell straight to this line.)
            // ⚠️ It is REPORTED AND KEPT since 2026-08-16. Reporting came first, in 2026-08,
            // and for months this spoke while still dropping the token's WIDTH — the noisy
            // spelling corrupting positions exactly like the silent ones (§1 第183 measured
            // 14 characters for a `using` written here, WHILE this error was raised).
            // Kept in the property list, where it contributes width and nothing else:
            // PartDeclarationSyntax reads its display name and brace by INDEX before the
            // body and its properties by TYPE inside it, so a token among them is invisible
            // to both. Still consumed, so one stray does not cascade into the rest.
            var strayspan = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(strayspan, DiagnosticCodes.PartHeaderStrayToken,
                $"'{Current.Text}' is not a part property. A part header holds properties "
                + "written bare (e.g. 'clef bass', 'instrument \"Violin\"'), a 'key', "
                + "'override'/'revert', or an inner 'section'. "
                + "A clef needs its keyword: write 'clef bass', not 'bass'.");
            properties.Add(Advance());
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return displayName is { } dn
            ? new PartDeclarationGreen(keyword, name, dn, openBrace, [.. properties], closeBrace)
            : new PartDeclarationGreen(keyword, name, openBrace, [.. properties], closeBrace);
    }

    /// <summary>
    /// Parse a section nested inside a part (part-major form). Unlike a top-level
    /// section — whose body is per-part blocks — an inner section's body is the
    /// music itself, implicitly bound to the enclosing part. Built faithfully
    /// (no synthesized tokens) so source positions stay exact.
    /// </summary>
    private SectionDeclarationGreen ParsePartInnerSection()
    {
        var keyword = Expect(SyntaxKind.SectionKeyword);
        var name = ExpectPartName();
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = new List<GreenNode?>();
        while (_pendingPostEventMarkers.Count > 0
               || (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile)))
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new SectionDeclarationGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    // Legacy part inside score: part Name "display" { staff... }
    /// <summary>
    /// A token the container it stands in has no item rule for: REPORTED (LYS0030), and
    /// then kept in the tree.
    /// </summary>
    /// <param name="container">What the container is, named the way a reader would name
    /// it ("a section", "a form", "a score") — it goes into the message.</param>
    /// <param name="vocabulary">One sentence listing what that container DOES hold.</param>
    /// <remarks>
    /// ⚠️ KEEPING IT IS HALF THE REPAIR, and the half a reader who never mistypes still
    /// pays for. A bare <c>Advance()</c> — what all three <see cref="ParseList"/> callers
    /// and <c>ParseMusicBlock</c> did until 2026-08-16 — drops the token's WIDTH, and a
    /// node's position is the running sum of the green widths before it, so every source
    /// offset after the token slides. Measured: four books differing from a control only by
    /// an inserted <c>"oops"</c> rendered SVGs byte-identical to it, <c>data-pos</c>
    /// included, while <c>lysc check</c> said <c>No errors found.</c> — and on
    /// <c>form main { A section B }</c> the resulting <c>Undefined section: 'B'</c> pointed
    /// at column 15, the dropped <c>section</c> keyword, with <c>B</c> at column 23.
    /// Same shape and same fix as <see cref="SkipStrayChordToken"/>,
    /// <see cref="ReportUnclaimedDot"/> and <see cref="ReportStrayStringNumber"/>;
    /// <c>ToFullString() == source</c> is the detector for all of them.
    /// <para>
    /// ⚠️ Keeping it is also FASTER on the keystroke path, which nobody had measured.
    /// <see cref="IncrementalReuseMap"/> locates reusable items by accumulating
    /// <c>item.FullWidth</c>, so a dropped token put every later item's key out by its
    /// width and the lookup missed. Measured base-build against HEAD-build over 240
    /// simulated keystrokes, counting reference-equal green members (the count is
    /// deterministic — asserted identical across five runs): with one unplaceable token
    /// in another section, reuse went <b>4.00 → 5.00</b> members per keystroke; on clean
    /// books, where typing itself makes a transient stray, <b>4.67 → 5.00</b>
    /// (<c>perf-plain1k</c>, <c>perf-slur1k</c>) and <b>2.67 → 3.00</b>
    /// (<c>perf-fingbeam1k</c>). No configuration reused LESS.
    /// </para>
    /// </remarks>
    private SyntaxToken ReportStrayItem(string container, string vocabulary)
    {
        string text = Current.Text;
        var span = new TextSpan(_textPosition + Current.LeadingTriviaWidth,
                                Math.Max(1, text.Length));
        _diagnostics.Error(span, DiagnosticCodes.StrayItemToken,
            $"'{text}' is not something {container} can hold. {vocabulary}");
        return Advance();
    }

    /// <summary>
    /// Parses items with <paramref name="parseItem"/> until <paramref name="close"/>
    /// or EOF. A null result skips one token (the single infinite-loop guard shared
    /// by every brace-delimited list).
    /// </summary>
    /// <remarks>
    /// ⚠️ The <c>Advance()</c> below is a LOOP GUARD, not a recovery rule. All three item
    /// rules this list is given (<c>ParseSectionItem</c>, <c>ParseFormItem</c>,
    /// <c>ParseRenderItem</c>) now end in <see cref="ReportStrayItem"/>, so a token the
    /// container cannot place no longer arrives here at all. What still can is a token a
    /// rule reported and deliberately declined to keep — the music-item reports reached
    /// through <c>ParseSectionItem</c>'s music arm — and a rule that consumed nothing;
    /// dropping one token is the only thing that keeps this from spinning. It is kept for
    /// that reason, not because a token may be dropped in silence.
    /// </remarks>
    private List<GreenNode?> ParseList(SyntaxKind close, System.Func<GreenNode?> parseItem)
    {
        var items = new List<GreenNode?>();
        while (!Check(close) && !Check(SyntaxKind.EndOfFile))
        {
            var item = parseItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }
        return items;
    }

    private GreenNode? ParsePartProperty()
    {
        // In a part/staff header every attribute is written BARE ('name value'),
        // including time and tempo (which keep their richer value grammars); a stray
        // ':' is flagged and dropped. This matches the bare music-stream forms.
        if (Current.Kind == SyntaxKind.TimeKeyword)
            return ParseTimeSignature();
        if (Current.Kind == SyntaxKind.TempoKeyword)
            return ParseTempoDeclaration();

        // clef treble, instrument "Violin", tuning standard, transpose d
        if (Current.Kind == SyntaxKind.Identifier ||
            Current.Kind == SyntaxKind.ClefKeyword ||
            Current.Kind == SyntaxKind.InstrumentKeyword ||
            Current.Kind == SyntaxKind.TuningKeyword ||
            Current.Kind == SyntaxKind.OctaveKeyword ||
            Current.Kind == SyntaxKind.TransposeKeyword)
        {
            var propName = Advance();
            // Bare canonical form ('clef treble'); a stray ':' is flagged and skipped.
            var colon = ConsumeRejectedColon();

            // The value used to be taken unconditionally, which meant a property with none
            // ate whatever followed — including the closing brace. `part m { clef }` then
            // parsed the whole rest of the file INSIDE the part and complained about a line
            // far below ("Undefined variable or phrase: 'm'"); with another part after it,
            // the brace itself was reported as a clef name ("Unknown clef '}'"). Neither
            // named the missing value, and neither pointed at this line.
            if (Check(SyntaxKind.CloseBrace) || Check(SyntaxKind.EndOfFile))
            {
                var missingSpan = new TextSpan(_textPosition, Current.FullWidth);
                _diagnostics.Error(missingSpan, DiagnosticCodes.PartPropertyMissingValue,
                    $"'{propName.Text}' has no value. Part properties are written bare, "
                    + $"name then value — e.g. '{propName.Text} …'.");
                // Return the property with a zero-width missing value rather than consuming
                // the brace, so the header still closes here and the file below parses as
                // written. Same shape as Expect()'s synthetic token: no trivia, or the
                // root.FullWidth == text.Length invariant breaks.
                return new PropertyAssignmentGreen(propName, colon,
                    [new SyntaxToken(SyntaxKind.Identifier, "", null, null)]);
            }

            var value = Advance(); // identifier, string, number, or pitch
            // A transpose target may carry octave marks (transpose d' / c,);
            // harmless for the other properties, which never have trailing marks.
            // A hyphenated bare value ('instrument bass-guitar') is ONE word:
            // keep consuming minus+word pairs — it used to truncate silently
            // to "bass". (Lyrics/chords never reach this header-only path, so
            // merging hyphens here is safe.)
            // ⚠️ The gate was IsPartNameKind until 2026-08-19, and that was a BORROWED
            // predicate: it answers "may this word name a part?", which admits an identifier
            // and the four clef words that are legal part names (bass/treble/alto/tenor). The
            // question here is a different one — "is this the second half of one hyphenated
            // word?" — and the two coincided closely enough to look right. `voice-soprano` is
            // where they parted: it is in KnownInstruments and GetInstrument reads it, yet it
            // truncated to `voice` and errored, while its three siblings voice-alto /
            // voice-tenor / voice-bass compiled for no better reason than that THEIR second
            // halves happen to be clef words. Measured 2026-08-19: 0 of the 567 tracked books
            // write voice-soprano, and a part header holds a hyphen at exactly one site in the
            // whole corpus (`instrument electric-bass`), so widening this cannot change a book
            // that compiles today — only spellings that are errors today. User decision, taken
            // before 0.3.0 was tagged.
            var values = new List<GreenNode?> { value };
            while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma)
                   || (Check(SyntaxKind.Minus) && IsHyphenatedValueTail(Peek(1))))
            {
                if (Check(SyntaxKind.Minus))
                {
                    values.Add(Advance()); // -
                    values.Add(Advance()); // word
                }
                else
                {
                    values.Add(Advance());
                }
            }
            // An instrument preset may carry a quoted display-name label:
            // `instrument violin "1st Violin"` — the preset drives clef/octave/tuning
            // defaults while the quoted label overrides the shown instrument name.
            if (propName.Kind == SyntaxKind.InstrumentKeyword && Check(SyntaxKind.StringLiteral))
                values.Add(Advance());
            return new PropertyAssignmentGreen(propName, colon, [.. values]);
        }
        return null;
    }

    // ========== Properties and Metadata ==========

    // Top-level `transpose d` (bare, like `time 4/4` / `key c major`): a default
    // transpose applied to every part that does not set its own. Octave marks
    // (transpose d' / c,) are allowed on the target.
    private PropertyAssignmentGreen ParseTopLevelTranspose()
    {
        var keyword = Advance(); // transpose
        var value = Advance();   // target pitch
        var values = new List<GreenNode?> { value };
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
            values.Add(Advance());
        return new PropertyAssignmentGreen(keyword, null, [.. values]);
    }

    private MetadataDeclarationGreen ParseMetadataDeclaration()
    {
        var keyword = Advance();
        var valueTokens = new List<GreenNode?>();

        // The value is a quoted string — like score names and other free-text values
        // (title "Song", composer "Name"); a bare, unquoted value is rejected.
        if (Check(SyntaxKind.StringLiteral))
        {
            valueTokens.Add(Advance());
        }
        else
        {
            var span = new TextSpan(_textPosition, Math.Max(1, Current.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.MetadataValueMustBeQuoted,
                $"The {keyword.Text} value must be a quoted string, e.g. {keyword.Text} \"…\".");
            // Recover by consuming the old loose run so the rest still parses.
            while (Check(SyntaxKind.StringLiteral) ||
                   Check(SyntaxKind.IntegerLiteral) ||
                   Check(SyntaxKind.Identifier) ||
                   IsPitchStart() ||
                   Check(SyntaxKind.MajorKeyword) ||
                   Check(SyntaxKind.MinorKeyword) ||
                   Check(SyntaxKind.Slash))
            {
                valueTokens.Add(Advance());
            }
        }

        return new MetadataDeclarationGreen(keyword, [.. valueTokens]);
    }

    /// <summary>
    /// <c>fonts { KEY VALUE… }</c> — the only form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The block's tokens are kept FLAT, like every other declaration in this file: the
    /// entries are read back by <c>FontDeclarationSyntax.Entries</c> rather than shaped
    /// here, so a role vocabulary that grows does not grow the green tree. The parser's
    /// only job is to find the block's extent and to refuse a value that is neither a
    /// quoted name nor a bare word.
    /// </para>
    /// <para>
    /// ⚠️ A VALUE WITHOUT A BLOCK IS STILL WORTH A SENTENCE. <c>fonts "Georgia"</c> is a
    /// plausible first guess — every other metadata keyword in the language takes a bare
    /// value — so it is answered with the block to write rather than with
    /// "Expected 'OpenBrace'", which describes the parser's predicament and not the
    /// writer's mistake.
    /// </para>
    /// </remarks>
    private FontDeclarationGreen ParseFontDeclaration()
    {
        var keyword = Advance(); // fonts

        if (Check(SyntaxKind.OpenBrace))
            return ParseFontBlock(keyword);

        // The tokens are KEPT, not dropped: a declaration that loses them slides every
        // later `data-pos`, the LSP's jump targets and the columns of the diagnostics that
        // follow (RULES §5.1).
        var tokens = new List<GreenNode?>();
        string? face = Check(SyntaxKind.StringLiteral) ? Current.Text.Trim('"') : null;

        var span = new TextSpan(_textPosition + Current.LeadingTriviaWidth,
            Math.Max(1, Current.Text.Length));
        _diagnostics.Error(span, DiagnosticCodes.FontsNeedsABlock,
            face is { Length: > 0 }
                // The writer's own face name, so the fix is a copy rather than a reading.
                ? $"'fonts' binds a face per text role, so it takes a block: "
                  + $"fonts {{ serif \"{face}\"  sans \"{face}\" }} for the whole document's "
                  + "text, or one role at a time, e.g. lyricText \"Charis SIL\"."
                : "'fonts' takes a block of role bindings: fonts { serif \"Georgia\" }.");

        // Consume the stray value so one mistake does not cascade into the rest of the file.
        while (Check(SyntaxKind.StringLiteral) ||
               Check(SyntaxKind.IntegerLiteral) ||
               Check(SyntaxKind.Identifier))
        {
            tokens.Add(Advance());
        }
        if (Check(SyntaxKind.EmbeddedKeyword))
            tokens.Add(Advance());

        return new FontDeclarationGreen(keyword, [.. tokens]);
    }

    // fonts { serif "Georgia"  lyricText "Charis SIL" "Noto Serif CJK JP"  embedded }
    //
    // House style, the same as a part header: bare KEY, bare VALUEs, no colons and no
    // commas, entries separated by nothing but whitespace. That is also why an entry's
    // extent is found by looking for the NEXT key rather than by a terminator — there
    // isn't one, and inventing a ';' here would be a second punctuation convention in a
    // language that has none.
    private FontDeclarationGreen ParseFontBlock(SyntaxToken keyword)
    {
        var tokens = new List<GreenNode?> { Advance() }; // {
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            // A key is any word: role names like `text` and `mark` lex as identifiers,
            // while `title`, `lyrics`, `chords`, `tempo`, `instrument`, `tuplet` and
            // `volta` are already KEYWORDS of the language. Matching by token kind would
            // therefore need the keyword list mirrored here — a second home for it — so
            // the key is matched by its TEXT, in FontDeclarationSyntax, against the one
            // vocabulary in TextRoles.
            if (Check(SyntaxKind.StringLiteral) || IsWordLikeToken(Current))
            {
                tokens.Add(Advance());
                continue;
            }
            // Anything else inside the block is refused where it stands, and skipped, so
            // one stray token does not swallow the rest of the score.
            var span = new TextSpan(_textPosition, Math.Max(1, Current.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.FontBindingMissingValue,
                "A 'fonts { }' entry is a key followed by quoted face names or a generic " +
                "family, e.g. lyricText \"Charis SIL\" — '" + Current.Text + "' is neither.");
            tokens.Add(Advance());
        }
        if (Check(SyntaxKind.CloseBrace))
            tokens.Add(Advance());
        else
            _diagnostics.Error(new TextSpan(_textPosition, 1), DiagnosticCodes.ExpectedToken,
                "This 'fonts {' has no closing '}'.");
        return new FontDeclarationGreen(keyword, [.. tokens]);
    }

    // A token that reads as a bare WORD, judged by its text rather than by its kind:
    // several role keys (title / lyrics / chords / tempo / instrument / tuplet / volta)
    // are already keywords of the language, and `sans-serif` carries a hyphen. Asking
    // the lexer's classification instead would mean mirroring the keyword list here — a
    // second home for it — and the key is validated against TextRoles anyway.
    private static bool IsWordLikeToken(SyntaxToken token)
    {
        string t = token.Text;
        if (string.IsNullOrEmpty(t) || !char.IsLetter(t[0]))
            return false;
        foreach (char c in t)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        return true;
    }

    // 'time 4/4' is written bare everywhere — as a music-stream command and as a
    // part/staff-header attribute. A stray ':' ('time: 4/4') is flagged and dropped
    // by ConsumeRejectedColon so the rest still parses.
    private TimeSignatureGreen ParseTimeSignature()
    {
        var timeKeyword = Expect(SyntaxKind.TimeKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        // Senza misura: `time none` — unmeasured music (no signature printed,
        // no bar-length validation). MusicXML <senza-misura/>.
        if (Current.Kind == SyntaxKind.Identifier
            && Current.Text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return new TimeSignatureGreen(timeKeyword, colon, Advance());
        }
        var numerator = Expect(SyntaxKind.IntegerLiteral);
        // Additive meter: time 3+2/8 — MusicXML <beats>3+2</beats>.
        if (Check(SyntaxKind.Plus))
        {
            var numTokens = new List<GreenNode?> { numerator };
            while (Check(SyntaxKind.Plus))
            {
                numTokens.Add(Advance()); // +
                numTokens.Add(Expect(SyntaxKind.IntegerLiteral));
            }
            var addSlash = Expect(SyntaxKind.Slash);
            var addDen = Expect(SyntaxKind.IntegerLiteral);
            return new TimeSignatureGreen(timeKeyword, colon, [.. numTokens], addSlash, addDen);
        }
        var slash = Expect(SyntaxKind.Slash);
        var denominator = Expect(SyntaxKind.IntegerLiteral);
        return new TimeSignatureGreen(timeKeyword, colon, numerator, slash, denominator);
    }

    // 'tempo 120' is written bare everywhere (music-stream command and part/staff
    // attribute alike); a stray ':' is flagged and dropped by ConsumeRejectedColon.
    private TempoDeclarationGreen ParseTempoDeclaration()
    {
        var tempoKeyword = Expect(SyntaxKind.TempoKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        var valueTokens = new List<GreenNode?>();

        // A single unquoted word right after `tempo` is the marking —
        // `tempo Comodo 4 = 84` — matching the bare-identifier rule used for
        // staff display names (quotes stay available for multi-word text).
        // Only the FIRST value position, so a following swing word or the
        // music that comes after the declaration is never swallowed.
        if (Check(SyntaxKind.Identifier) && !IsSwingWord(Current.Text))
            valueTokens.Add(Advance());

        // Collect value tokens: "marking" duration = bpm, plus an optional trailing
        // 'swing' / 'shuffle' feel word (kept as a value token; TempoValue reads the
        // whole run). These are NOT reserved words, so they stay usable as names.
        while (Check(SyntaxKind.StringLiteral) ||
               Check(SyntaxKind.IntegerLiteral) ||
               // a dotted beat unit: "tempo \"Lively\" 4. = 116" lexes as
               // IntegerLiteral + Dot at declaration level — without accepting
               // the dot the parser stopped there and ". = 116" was dropped.
               Check(SyntaxKind.Dot) ||
               Check(SyntaxKind.Equals) ||
               Check(SyntaxKind.DecimalLiteral) ||
               (Check(SyntaxKind.Identifier) && IsSwingWord(Current.Text)))
        {
            // A decimal is taken INTO the run and then refused, rather than left to
            // end it. Ending the run there would drop the rest of the declaration on
            // the floor — `tempo 4.5 = 116` would keep neither the '=' nor the 116 —
            // and the reader would be told about a stray number somewhere after a
            // tempo instead of about the tempo.
            if (Check(SyntaxKind.DecimalLiteral))
            {
                var span = new TextSpan(_textPosition, System.Math.Max(1, Current.FullWidth));
                _diagnostics.Error(span, DiagnosticCodes.FractionalTempoValue,
                    $"'{Current.Text}' is not a tempo value - a metronome mark is a whole "
                    + "number of beats per minute (tempo 4 = 116) and a beat unit is a "
                    + "note value, dotted with a dot (tempo 4. = 116).");
            }
            valueTokens.Add(Advance());
        }

        return new TempoDeclarationGreen(tempoKeyword, colon, [.. valueTokens]);
    }

    private static bool IsSwingWord(string text) => TempoValue.IsFeelWord(text);

    // partial <duration> — declares the following measure a pickup (anacrusis)
    // of the given length. The value reuses the note-duration grammar (number +
    // optional dots) so 'partial 4', 'partial 8' and 'partial 2.' all parse.
    private PartialDeclarationGreen ParsePartialDeclaration()
    {
        var partialKeyword = Expect(SyntaxKind.PartialKeyword);

        // A clear, specific error instead of the raw "Expected 'IntegerLiteral', found 'Dot'":
        // `partial` needs a note-value number. Recover with a zero-width token (round-trip
        // safe); DurationSyntax.Value tolerates the empty number (defaults to a quarter), so
        // nothing downstream throws on the broken input.
        SyntaxToken number;
        if (Check(SyntaxKind.IntegerLiteral))
        {
            number = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, System.Math.Max(1, Current.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "'partial' needs a duration — a note value such as 'partial 4' or 'partial 2.'.");
            number = new SyntaxToken(SyntaxKind.IntegerLiteral, "", null, null);
        }

        var dots = new List<GreenNode?>();
        while (Check(SyntaxKind.Dot))
            dots.Add(Advance());
        var duration = new DurationGreen(number, [.. dots]);
        return new PartialDeclarationGreen(partialKeyword, duration);
    }

    // ========== Variables ==========

    // Phrase references carry the SAME trailing octave marks as a pitch — Chorus'
    // lands the movable phrase an octave higher, Chorus, an octave lower — so we
    // reuse the note grammar's ' / , collection (ParsePitch, Parser.Music.cs).
    // A GLUED '(N)' after the marks is the diatonic interval argument: Melody'(3)
    // shifts the phrase a THIRD up in the ambient key (1-based like a degree, so
    // '(8) is exactly '). The adjacency is what separates it from a slur — a
    // spaced ' (' still opens a slur — and the marks give the direction, so a
    // bare 'Melody(3)' stays a reference followed by a (broken) slur.
    private GreenNode?[] ParsePhraseOctaveMarks()
    {
        var marks = new List<GreenNode?>();
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
            marks.Add(Advance());
        if (marks.Count > 0
            && Check(SyntaxKind.OpenParen) && CurrentGluedToPrevious
            && Peek(1).Kind == SyntaxKind.IntegerLiteral
            && Peek(2).Kind == SyntaxKind.CloseParen)
        {
            marks.Add(Advance()); // '('
            var number = Advance();
            marks.Add(number);
            marks.Add(Advance()); // ')'
            if (int.TryParse(number.Text, out int n) && n < 1)
                _diagnostics.Error(new TextSpan(_textPosition, Math.Max(1, number.Width)),
                    DiagnosticCodes.InvalidScaleDegree,
                    "A phrase-shift interval is 1-based - '(1) is a unison (no shift), "
                    + "'(3) a third, '(8) an octave.");
        }
        return [.. marks];
    }


    // A bare identifier in music is a PHRASE REFERENCE (the `$` sigil is gone —
    // `Chorus` not `$Chorus`). A word that reads like an English-accidental note
    // slip (eb, bb, fsharp) is almost certainly a mistyped pitch rather than a
    // phrase, so keep the Dutch-spelling hint for that case; anything else is taken
    // as a phrase reference and SymbolReferenceValidator reports it if undefined.
    private VariableReferenceGreen ParseBareVariableReference()
    {
        if (PitchSuggestion(Current.Text) is { } pitch)
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            var bad = Advance();
            _diagnostics.Error(span, DiagnosticCodes.BareReferenceRequiresDollar,
                $"'{bad.Text}' is not a valid note — did you mean the pitch '{pitch}'?");
            return new VariableReferenceGreen(bad);
        }
        var name = Advance();
        var marks = ParsePhraseOctaveMarks();
        return marks.Length == 0
            ? new VariableReferenceGreen(name)
            : new VariableReferenceGreen(name, marks);
    }

    // English-style accidental spellings map to Lily#'s Dutch note names:
    // eb -> ees, bb -> bes, gflat -> ges, fsharp -> fis. Returns null when the
    // word is not a plausible pitch typo.
    private static string? PitchSuggestion(string word)
    {
        if (word.Length is < 2 or > 6)
            return null;
        char letter = char.ToLowerInvariant(word[0]);
        if (letter is < 'a' or > 'g')
            return null;
        return word[1..].ToLowerInvariant() switch
        {
            "b" or "flat" => $"{letter}es",
            "sharp" => $"{letter}is",
            _ => null,
        };
    }
}
