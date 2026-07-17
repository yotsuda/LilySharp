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
    /// Parse form declaration: form Name { ... }
    /// </summary>
    private FormDeclarationGreen ParseFormDeclaration()
    {
        var keyword = Expect(SyntaxKind.FormKeyword);   // the `form` keyword

        // A form is always named: `form Main { … }`. A score binds to it by that
        // name (`score Main { … }`); the reserved name `main` writes to the input
        // .lys stem. Names are case-sensitive (like every Lily# symbol).
        SyntaxToken name;
        if (!Check(SyntaxKind.OpenBrace))
            name = Advance();
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken, "Expected a form name after 'form'");
            name = new SyntaxToken(SyntaxKind.Identifier, "", null, null);
        }

        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseFormItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new FormDeclarationGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    private GreenNode? ParseFormItem()
    {
        return Current.Kind switch
        {
            // Section reference with optional per-occurrence display label:
            //   structure { First Second First "First (reprise)" }
            // Clef-name words (bass/treble/alto/tenor) are allowed as section names too,
            // matching part/section/phrase declarations.
            SyntaxKind.Identifier or SyntaxKind.BassKeyword or SyntaxKind.TrebleKeyword
                or SyntaxKind.AltoKeyword or SyntaxKind.TenorKeyword
                => new SectionReferenceGreen(
                    Advance(),
                    Check(SyntaxKind.StringLiteral) ? Advance() : null),
            SyntaxKind.Tilde => ParseSilentSectionReference(),
            SyntaxKind.At => ParseMusicMark(),
            SyntaxKind.Underscore => ParseCustomText(),
            SyntaxKind.RepeatStartBar => ParseFormRepeatBlock(),
            SyntaxKind.OpenBracket => ParseVoltaBracket(),
            // `break` / `nobreak` between sections force / forbid a system break at
            // that point in the played sequence (a layout directive, so no '@').
            SyntaxKind.BreakKeyword or SyntaxKind.NoBreakKeyword => ParseBreak(),
            SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
                or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword
                => ParseNavigationMark(),
            _ => null
        };
    }

    /// <summary>
    /// Parse silent section reference: ~SectionName or ~SectionName "label".
    /// The optional label is kept on the node but NOT displayed (the '~' hides it);
    /// it lets an author park a label text and reveal it later by dropping the '~'.
    /// </summary>
    private SilentSectionReferenceGreen ParseSilentSectionReference()
    {
        var tilde = Expect(SyntaxKind.Tilde);
        var name = ExpectPartName();

        // '~B "alt"' — a label written but hidden by '~'. Keep it (do not drop it),
        // and nudge that it is currently not shown.
        SyntaxToken? label = null;
        if (Check(SyntaxKind.StringLiteral))
        {
            int labelStart = _textPosition;
            label = Advance();
            var span = new TextSpan(labelStart, Math.Max(1, _textPosition - labelStart));
            _diagnostics.Warning(span, DiagnosticCodes.HiddenSectionLabel,
                $"The section label {label.Text} is hidden by '~'; drop the '~' to show it (or remove the label).");
        }

        return new SilentSectionReferenceGreen(tilde, name, label);
    }

    /// <summary>
    /// Parse music mark: @segno, @fine, @ds.al.fine, etc.
    /// </summary>
    private MusicMarkGreen ParseMusicMark()
    {
        var at = Expect(SyntaxKind.At);
        int nameStart = _textPosition; // the mark name starts here, before we consume it
        var name = ExpectMarkName();

        // `@` modifies the note it follows; a navigation mark (segno/coda/fine/D.S./
        // D.C./to coda) is a standalone landmark, not a note modifier, so it is BARE.
        // Reject the `@`-prefixed form (recover by parsing it anyway).
        if (name.Kind is SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
            or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword)
        {
            // Squiggle the mark name itself, not the token after it (name is already
            // consumed, so _textPosition/Current now point one token past it).
            var span = new TextSpan(nameStart, Math.Max(1, name.FullWidth));
            _diagnostics.Error(span, DiagnosticCodes.NavigationMarkIsBare,
                $"A navigation mark is bare, not '@': write '{name.Text}' (e.g. segno, ds al coda) — '@' modifies a note.");
        }

        // Handle compound marks like @ds.al.fine
        var parts = new List<SyntaxToken> { at, name };
        while (Check(SyntaxKind.Dot))
        {
            parts.Add(Advance()); // .
            parts.Add(ExpectMarkName());
        }

        return new MusicMarkGreen([.. parts]);
    }

    /// <summary>
    /// Expect a mark name (identifier or navigation keyword)
    /// </summary>
    private SyntaxToken ExpectMarkName()
    {
        // Navigation keywords, integers, and pitch/rest tokens can appear as mark names
        // LILYPOND-REF: lily/figured-bass-engraver.cc - figure numbers (e.g., @fig.6)
        // Figured bass alterations: @fig.6.s (sharp), @fig.4.f (flat), @fig.7.n (natural)
        // 's' → RestS, 'f' → PitchF, 'n' → Identifier (handled naturally)
        if (Current.Kind is SyntaxKind.Identifier
            or SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
            or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword
            or SyntaxKind.AlKeyword
            or SyntaxKind.IntegerLiteral  // For figured bass numbers (e.g., @fig.6)
            or SyntaxKind.RestS           // For figured bass sharp suffix (e.g., @fig.6.s)
            or SyntaxKind.PitchF)         // For figured bass flat suffix (e.g., @fig.4.f)
        {
            return Advance();
        }
        return Expect(SyntaxKind.Identifier);
    }

    /// <summary>
    /// Parse custom text: _"text"
    /// </summary>
    private CustomTextGreen ParseCustomText()
    {
        var underscore = Expect(SyntaxKind.Underscore);
        var text = Expect(SyntaxKind.StringLiteral);
        return new CustomTextGreen(underscore, text);
    }

    /// <summary>
    /// Parse volta bracket: [1. Section] or [1,3. Section] or [1-3. Section] or [1. ~Section]
    /// </summary>
    private FormAlternativeGreen ParseVoltaBracket()
    {
        var openBracket = Expect(SyntaxKind.OpenBracket);
        var number = Expect(SyntaxKind.IntegerLiteral);

        // Check for range or list: [1-3. ] or [1,3. ]
        SyntaxToken? separator = null;
        SyntaxToken? endNumber = null;
        if (Check(SyntaxKind.Minus) || Check(SyntaxKind.Comma))
        {
            separator = Advance();
            endNumber = Expect(SyntaxKind.IntegerLiteral);
        }

        var dot = Expect(SyntaxKind.Dot);

        // Check for silent section reference: [1. ~Section]
        SyntaxToken? tilde = null;
        if (Check(SyntaxKind.Tilde))
        {
            tilde = Advance();
        }

        var section = Expect(SyntaxKind.Identifier);
        // Optional display label: [1. B "label"] — shown as the section's mark,
        // exactly like a plain reference's  A "A2".
        SyntaxToken? displayLabel = Check(SyntaxKind.StringLiteral) ? Advance() : null;
        // The ']' is optional: present = closed (right cap drawn), absent = open.
        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;

        return new FormAlternativeGreen(openBracket, number, separator, endNumber, dot, tilde, section, displayLabel, closeBracket);
    }

    /// <summary>
    /// Parse repeat block: |: ... :| or |: ... :| x3
    /// </summary>
    private FormRepeatBlockGreen ParseFormRepeatBlock()
    {
        var startBar = Expect(SyntaxKind.RepeatStartBar);

        var items = new List<GreenNode?>();
        var alternatives = new List<GreenNode?>();
        SyntaxToken? pipeBeforeAlternatives = null;
        int voltaBracketsBeforeClose = 0;

        // Parse items until :| or | (for alternatives)
        while (!Check(SyntaxKind.RepeatEndBar) && !Check(SyntaxKind.EndOfFile))
        {
            // ':|:' back-to-back repeat: closes this repeat and immediately opens
            // the next, sharing one barline. Keep it as a divider token in the item
            // list; ProcessRepeatBlock expands it to ':|' + '|:' (which fuse into the
            // RepeatBoth glyph), so 'A |: B :|: C :|' == 'A |: B :| |: C :|'.
            if (Check(SyntaxKind.RepeatBothBar))
            {
                items.Add(Advance());
                continue;
            }

            // Check for | followed by number (start of alternatives)
            if (Check(SyntaxKind.Bar) && Peek(1)?.Kind == SyntaxKind.IntegerLiteral)
            {
                pipeBeforeAlternatives = Advance(); // consume |
                break;
            }

            // The repeat barline belongs BETWEEN the endings — write
            //   |: … [1. D] :| [2. Outro]
            // A second ending bracket before the :| is the old, ambiguous spelling
            // (|: … [1. D] [2. Outro] :|), which wrongly implies the 2nd ending also
            // repeats. Reject it with a hint to the correct form.
            if (Check(SyntaxKind.OpenBracket) && ++voltaBracketsBeforeClose == 2)
            {
                _diagnostics.Error(new TextSpan(_textPosition, Current.FullWidth),
                    DiagnosticCodes.VoltaRepeatBarlinePlacement,
                    "Put the repeat barline between the endings: write '[1. ...] :| [2. ...]', " +
                    "not '[1. ...] [2. ...] :|'");
            }

            var item = ParseFormItem();
            if (item != null)
                items.Add(item);
            else
                Advance();
        }
        // Parse alternatives before :| (e.g., "1. A1" in "|: A | 1. A1 :| 2. A2")
        if (pipeBeforeAlternatives != null)
        {
            while (Check(SyntaxKind.IntegerLiteral) && !Check(SyntaxKind.RepeatEndBar))
            {
                alternatives.Add(ParseFormAlternative());
            }
        }

        var endBar = Expect(SyntaxKind.RepeatEndBar);

        // Final alternative after :| — the bare "2. A2" form or the bracket form
        // "[2. A2]", so a structure repeat reads exactly like the inline volta:
        //   |: Intro2 B C A2 [1. D] :| [2. Outro]
        GreenNode? finalAlternative = null;
        if (Check(SyntaxKind.IntegerLiteral))
            finalAlternative = ParseFormAlternative();
        else if (Check(SyntaxKind.OpenBracket))
            finalAlternative = ParseVoltaBracket();

        // Parse repeat count: x3
        SyntaxToken? xToken = null;
        SyntaxToken? repeatCount = null;
        if (Check(SyntaxKind.Identifier) && Current.Text == "x")
        {
            xToken = Advance();
            repeatCount = Expect(SyntaxKind.IntegerLiteral);
        }

        return new FormRepeatBlockGreen(startBar, [.. items], pipeBeforeAlternatives, [.. alternatives], endBar, finalAlternative, xToken, repeatCount);

    }

    /// <summary>
    /// Parse a bare (unbracketed) structure alternative: 1. SectionName.
    /// The bracket is required — <c>[1. SectionName]</c> — so this rejects the bare
    /// form with a hint and recovers by keeping the parsed alternative.
    /// </summary>
    private FormAlternativeGreen ParseFormAlternative()
    {
        int startPos = _textPosition;
        var number = Expect(SyntaxKind.IntegerLiteral);
        var dot = Expect(SyntaxKind.Dot);
        var section = Expect(SyntaxKind.Identifier);

        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.VoltaBracketRequired,
            $"A volta ending must be bracketed: write '[{number.Text}. {section.Text}]'. " +
            "The closing ']' is optional (present = closed cap, absent = open).");

        return new FormAlternativeGreen(number, dot, section);
    }

    /// <summary>
    /// Parse navigation mark: segno, fine, coda, dc, ds, etc.
    /// </summary>
    private NavigationMarkGreen ParseNavigationMark()
    {
        var first = Advance();

        // Single keyword: segno, fine, coda
        if (first.Kind is SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword)
        {
            return new NavigationMarkGreen(first);
        }

        // "to coda" (two words) or "tocoda" (one word). The one-word spelling
        // already carries the whole instruction, so there is no trailing 'coda'.
        if (first.Kind == SyntaxKind.ToKeyword)
        {
            if (first.Text.Equals("tocoda", StringComparison.OrdinalIgnoreCase))
                return new NavigationMarkGreen(first);
            var coda = Expect(SyntaxKind.CodaKeyword);
            return new NavigationMarkGreen(first, coda);
        }

        // dc/ds alone or with "al fine/coda"
        if (first.Kind is SyntaxKind.DcKeyword or SyntaxKind.DsKeyword)
        {
            if (Check(SyntaxKind.AlKeyword))
            {
                var al = Advance();
                var target = Expect(SyntaxKind.FineKeyword, SyntaxKind.CodaKeyword);
                return new NavigationMarkGreen(first, al, target);
            }
            return new NavigationMarkGreen(first);
        }

        return new NavigationMarkGreen(first);
    }

    /// <summary>
    /// Parse render declaration: render [name] "file.svg" { ... }
    /// </summary>
    // Parses a printable-score declaration: `score [ "basename" ] { layout }`.
    // `score` is the keyword (the old `render score` form is gone). The optional
    // string is the output BASENAME — its extension, if any, is ignored because
    // the file format is a CLI choice; omitting it derives the name from the
    // input file. Multiple `score` blocks (with distinct basenames) emit
    // multiple files, e.g. a full score plus part extracts.
    private RenderDeclarationGreen ParseRenderDeclaration()
    {
        var keyword = Expect(SyntaxKind.ScoreKeyword);

        // `score <FormName> ["basename"] [transpose <pitch>] { ... }`.
        // A bare token is the FORM reference (which form this score renders); a
        // quoted string is the output basename (quotes only needed for spaces).
        // The form name is REQUIRED at the semantic layer; a missing one is caught
        // by the validator, not here, so recovery stays local.
        SyntaxToken? formName = Check(SyntaxKind.OpenBrace)
            || Check(SyntaxKind.TransposeKeyword)
            || Check(SyntaxKind.StringLiteral)
            ? null : Advance();

        // Optional output basename (a quoted string). The extension, if written,
        // is dropped downstream (the file format is a CLI choice).
        SyntaxToken? filename = Check(SyntaxKind.StringLiteral) ? Advance() : null;

        // Optional per-score transpose: `score <Form> transpose <pitch> { ... }`.
        // Stored as a transpose property (same shape the part header uses).
        GreenNode? transpose = Check(SyntaxKind.TransposeKeyword) ? ParsePartProperty() : null;

        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseRenderItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        // The `name` slot now carries the form reference; `filename` the basename.
        return new RenderDeclarationGreen(keyword, formName, filename, transpose, openBrace, [.. items], closeBrace);
    }


    /// <summary>
    /// Check if current token can be a part name (Identifier or instrument keyword like bass).
    /// </summary>
    private bool IsPartNameStart() => IsPartNameKind(Current.Kind);

    private static bool IsPartNameKind(SyntaxKind? kind) => kind is SyntaxKind.Identifier
        or SyntaxKind.BassKeyword
        or SyntaxKind.TrebleKeyword
        or SyntaxKind.AltoKeyword
        or SyntaxKind.TenorKeyword;

    /// <summary>
    /// Expect a part name (Identifier or instrument keyword like bass).
    /// </summary>
    private SyntaxToken ExpectPartName()
    {
        if (IsPartNameStart())
            return Advance();

        // A name written with a leading digit — `phrase 2foo { … }` — lexes as an
        // IntegerLiteral GLUED to the name token (numbers are durations / scale
        // degrees, so the digit run splits off first). Report the real cause once
        // and RECOVER by consuming the whole `2foo` as the name: without this the
        // stray digit and identifier cascade into three more unrelated errors
        // ("expected {", "detached duration", "undefined variable 'foo'").
        if (Check(SyntaxKind.IntegerLiteral)
            && IsPartNameKind(Peek(1).Kind)
            && Current.TrailingTriviaWidth == 0 && Peek(1).LeadingTriviaWidth == 0)
        {
            // Span the combined ink `2foo` (skip the digit's leading trivia).
            int inkStart = _textPosition + Current.LeadingTriviaWidth;
            var digits = Advance();          // "2"
            var rest = Advance();            // "foo" (glued)
            var name = digits.Text + rest.Text;
            _diagnostics.Error(new TextSpan(inkStart, name.Length),
                DiagnosticCodes.NameStartsWithDigit,
                $"A name cannot start with a digit: '{name}' — a leading number is a "
                + "duration or scale degree in Lily#; start the name with a letter.");
            // Merge into one Identifier so the rest of the declaration parses cleanly
            // (leading trivia from the digit, trailing from the name — round-trips).
            return new SyntaxToken(SyntaxKind.Identifier, name,
                digits.LeadingTrivia, rest.TrailingTrivia);
        }

        // Report error. If a reserved word (segno/coda/time/…) was written where a
        // name belongs, name the actual word and flag it as reserved — clearer than
        // the internal token kind.
        var span = new TextSpan(_textPosition, Current.FullWidth);
        string found = !string.IsNullOrEmpty(Current.Text) && char.IsLetter(Current.Text[0])
            ? $"'{Current.Text}', a reserved word — pick another name"
            : $"'{Current.Kind}'";
        _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
            $"Expected a name, found {found}");

        // Zero-width missing token with NO trivia (Current keeps its own; borrowing it
        // here would double-count — see Expect).
        return new SyntaxToken(SyntaxKind.Identifier, "", null, null);
    }

    /// <summary>
    /// Expect the NAME after <c>with chords</c> / <c>with lyrics</c>. Lyrics and
    /// chords are named symbols in Lily#: a score attaches them by the name the
    /// block was given, so the name is required. When it is missing this reports a
    /// clause-specific message anchored ON the <c>chords</c>/<c>lyrics</c> keyword
    /// (span <paramref name="keywordInkStart"/>/<paramref name="keywordInkLength"/>)
    /// — the plain <see cref="ExpectPartName"/> would instead land its opaque
    /// "Expected a name, found 'CloseBrace'" on the score's closing brace, since a
    /// trailing bare <c>with lyrics</c> leaves the next token a whole line away.
    /// </summary>
    private SyntaxToken ExpectAttachmentName(SyntaxKind attachKind, int keywordInkStart, int keywordInkLength)
    {
        if (IsPartNameStart())
            return Advance();

        string word = attachKind == SyntaxKind.LyricsKeyword ? "lyrics" : "chords";
        _diagnostics.Error(new TextSpan(keywordInkStart, keywordInkLength),
            DiagnosticCodes.ExpectedToken,
            $"'with {word}' needs a name: name the block '{word} NAME {{ … }}' and "
            + $"reference it here as 'with {word} NAME'.");

        // Zero-width missing name; the render item recovers and the staff still parses.
        return new SyntaxToken(SyntaxKind.Identifier, "", null, null);
    }

    private GreenNode? ParseRenderItem()
    {
        return Current.Kind switch
        {
            SyntaxKind.StaffKeyword => ParseStaffRender(),
            SyntaxKind.ChordsKeyword => ParseChordRowRender(),
            SyntaxKind.LyricsKeyword => ParseLyricsRowRender(),
            SyntaxKind.GrandStaffKeyword => ParseGrandStaffRender(),
            SyntaxKind.TabKeyword => ParseTabRender(),
            SyntaxKind.OssiaKeyword => ParseOssiaRender(),
            _ when IsPartNameStart() => ParseMidiPartRender(),
            _ => null
        };
    }

    /// <summary>
    /// Parse staff render: staff [clef] { partName }
    /// </summary>
    private StaffRenderGreen ParseStaffRender()
    {
        // staff [~] [clef] part ["display name"] [with chords chordPart]   (no braces)
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.StaffKeyword) };

        // `staff ~flute` suppresses the default instrument name label.
        if (Check(SyntaxKind.Tilde))
            tokens.Add(Advance());

        // A clef keyword followed by a part name is an override.
        if (IsClefKeyword() && IsPartNameKind(Peek(1)?.Kind))
            tokens.Add(Advance());

        tokens.Add(ExpectPartName());

        // `staff flute "津田さん"` (or a bare single word:
        // `staff flute 津田さん`) overrides the displayed instrument name.
        // Following render items always begin with a keyword, so a trailing
        // identifier is unambiguous.
        if (Check(SyntaxKind.StringLiteral) || IsPartNameKind(Peek(0)?.Kind))
            tokens.Add(Advance());

        // Attachments compose with the single operator `with X`, repeatable and in
        // any order: `with chords NAME [as roman|both|names]` (symbols above the staff)
        // and `with lyrics NAME` (syllables note-aligned below the staff). The tokens
        // are read positionally by RenderSpecParser.ParseStaff. `with lyrics` takes no
        // `as` selector; multiple `with lyrics` stack as verses.
        while (Check(SyntaxKind.WithKeyword)
            && Peek(1)?.Kind is SyntaxKind.ChordsKeyword or SyntaxKind.LyricsKeyword)
        {
            tokens.Add(Advance()); // with
            var attachKind = Current.Kind;
            // Ink span of the `chords` / `lyrics` keyword, captured before it is
            // consumed, so a missing name can be reported ON the keyword.
            int kwInk = _textPosition + Current.LeadingTriviaWidth;
            int kwLen = Current.Text.Length;
            tokens.Add(Advance()); // chords | lyrics
            tokens.Add(ExpectAttachmentName(attachKind, kwInk, kwLen));
            if (attachKind == SyntaxKind.ChordsKeyword)
                ConsumeAsSelector(tokens); // chords only: `... as roman | both | names`
        }

        return new StaffRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse chord-row render: <c>chords partName [as roman|both|names]</c> (places a
    /// chord part as a row, with an optional display selector).
    /// </summary>
    private ChordRowRenderGreen ParseChordRowRender()
    {
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.ChordsKeyword) };
        tokens.Add(ExpectPartName());
        ConsumeAsSelector(tokens);
        return new ChordRowRenderGreen([.. tokens]);
    }

    /// <summary>Consumes an optional <c>as WORD</c> selector, appending its two tokens.
    /// Shared by the chord display mode (<c>as roman | both | names</c>) and the tab
    /// style (<c>as numbers | full</c>). NB: <c>as</c> also lexes as the Dutch A-flat
    /// pitch, so match it by TEXT, not token kind; the mode word follows. This position
    /// (right after the target name) is unambiguous — a bare pitch there is meaningless —
    /// so the match is safe.</summary>
    private void ConsumeAsSelector(List<SyntaxToken> tokens)
    {
        if (string.Equals(Current.Text, "as", System.StringComparison.Ordinal) && Peek(1) != null)
        {
            tokens.Add(Advance()); // as
            tokens.Add(Advance()); // roman | both | names
        }
    }

    /// <summary>
    /// Parse lyrics-row render: <c>lyrics partName</c> (places a lyrics part as a row).
    /// </summary>
    private LyricsRowRenderGreen ParseLyricsRowRender()
    {
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.LyricsKeyword) };
        tokens.Add(ExpectPartName());
        return new LyricsRowRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse grand staff render: grandStaff { staff staff ... }
    /// </summary>
    private GrandStaffRenderGreen ParseGrandStaffRender()
    {
        var grandStaffKeyword = Expect(SyntaxKind.GrandStaffKeyword);
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var staves = new List<StaffRenderGreen>();
        while (Check(SyntaxKind.StaffKeyword))
        {
            staves.Add(ParseStaffRender());
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new GrandStaffRenderGreen(grandStaffKeyword, openBrace, [.. staves], closeBrace);
    }

    private bool IsClefKeyword() => SyntaxFacts.IsClefKeyword(Current.Kind);

    /// <summary>
    /// Parse ossia render: ossia [clef] partName — bare, exactly like staff
    /// (the braces of the old form only ever held the one name).
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
    /// </summary>
    private OssiaRenderGreen ParseOssiaRender()
    {
        var ossiaKeyword = Expect(SyntaxKind.OssiaKeyword);

        // A clef keyword followed by a part name is an override; alone it IS
        // the part name (clef words are legal part names, as for staff).
        if (IsClefKeyword() && IsPartNameKind(Peek(1)?.Kind))
        {
            var clef = Advance();
            return new OssiaRenderGreen(ossiaKeyword, clef, ExpectPartName());
        }

        return new OssiaRenderGreen(ossiaKeyword, ExpectPartName());
    }

    /// <summary>
    /// Parse tab render: tab tuning { partName }
    /// </summary>
    private TabRenderGreen ParseTabRender()
    {
        // tab [tuning] part   (tuning optional; no braces)
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.TabKeyword) };

        // A tuning name followed by a part name is an override; otherwise the lone
        // token is the part and the tuning comes from the part definition.
        bool tuningish = Current.Kind is SyntaxKind.Identifier or SyntaxKind.BassKeyword;
        if (tuningish && IsPartNameKind(Peek(1)?.Kind))
            tokens.Add(Advance());

        tokens.Add(ExpectPartName());
        ConsumeAsSelector(tokens); // `... as numbers | full` (numbers-only tab)

        // `with chords NAME [as roman|both|names]` attaches a chord part's symbols
        // above the tab, exactly like the notation-staff form.
        if (Check(SyntaxKind.WithKeyword) && Peek(1)?.Kind == SyntaxKind.ChordsKeyword)
        {
            tokens.Add(Advance()); // with
            tokens.Add(Advance()); // chords
            tokens.Add(ExpectPartName());
            ConsumeAsSelector(tokens); // chord display: `as roman | both | names`
        }
        return new TabRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse MIDI part render: partName [instrument:N] [octave:N]
    /// </summary>
    private MidiPartRenderGreen ParseMidiPartRender()
    {
        var partName = ExpectPartName();

        var options = new List<GreenNode?>();
        while (Current.Kind is SyntaxKind.InstrumentKeyword
            or SyntaxKind.OctaveKeyword)
        {
            var optKeyword = Advance();
            var colon = ConsumeRejectedColon();
            var value = Advance();
            options.Add(new PropertyAssignmentGreen(optKeyword, colon, [value]));
        }

        return new MidiPartRenderGreen(partName, [.. options]);
    }
}
