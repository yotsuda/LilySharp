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
                => ParseSectionReference(),
            SyntaxKind.Tilde => ParseSilentSectionReference(),
            SyntaxKind.At => ParseMusicMark(),
            SyntaxKind.Underscore => ParseCustomText(),
            SyntaxKind.RepeatStartBar => ParseFormRepeatBlock(),
            // A ':|' HERE is not the close of a '|: … :|' block — that one is consumed by
            // ParseFormRepeatBlock's Expect. This is a repeat barline written in the form
            // itself, and it needs a node before anything can give it meaning: without this
            // arm ParseFormItem returned null and ParseList's shared `else Advance()` — the
            // same guard whose part-header twin was LYS0025 — dropped it. Measured
            // 2026-08-15 on `form main { … Solo :| }`: the MIDI hash, the SVG hash, the
            // MusicXML repeat count and the LilyPond twin were all byte-identical to not
            // writing it, with no diagnostic. It is the same BarlineSyntax the music stream
            // uses, so ':|*N' comes along for free.
            SyntaxKind.RepeatEndBar => ParseBarline(),
            // Every OTHER barline, for the same reason as the ':|' arm above: it needs a
            // node before anything can give it meaning, and a bare Advance() dropped its
            // width. A plain '|' is kept as an inert divider, the rest as engraved
            // barlines — see ParseFormBarline for the measurement behind the split
            // (user decision 2026-08-16).
            _ when SyntaxFacts.IsBarlineKind(Current.Kind) => ParseFormBarline(),
            SyntaxKind.OpenBracket => ParseVoltaBracket(),
            // `break` / `noBreak` between sections force / forbid a system break at
            // that point in the played sequence, `pageBreak` / `noPageBreak` a page break
            // (layout directives, so no '@').
            SyntaxKind.BreakKeyword or SyntaxKind.NoBreakKeyword
                or SyntaxKind.PageBreakKeyword or SyntaxKind.NoPageBreakKeyword => ParseBreak(),
            SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
                or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword
                => ParseNavigationMark(),
            // Same guard as the ':|' arm above, for `using` (LYS0029).
            SyntaxKind.UsingKeyword => ParseMisplacedUsing("a form"),
            // Anything else: reported and KEPT (LYS0030) — the general case of the two
            // arms above, which were added one silent spelling at a time. Measured
            // 2026-08-16 on `form main { A section B }`: the `section` keyword was dropped
            // in silence, and the (correct) `Undefined section: 'B'` was then reported at
            // column 15, ON that keyword, with `B` standing at column 23.
            _ => ReportStrayItem("a form",
                    "A form body holds section references (bare names, '~name' to hide a "
                    + "label, a trailing ' or , to shift the octave), repeat blocks "
                    + "('|: … :|'), volta endings ('[1. … ]'), "
                    + "navigation marks (segno, fine, ds al coda), '@' marks, '_' texts and "
                    + "break/noBreak/pageBreak/noPageBreak.")
        };
    }

    /// <summary>A barline standing in a <c>form</c> body. Every one of them is KEPT — a
    /// bare <c>Advance()</c> dropped the width and slid every later source offset — but
    /// they are kept as two different things, because they mean two different things.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ A plain <c>|</c> is kept as the RAW TOKEN, not as a <see cref="BarlineSyntax"/>:
    /// it contributes its width and nothing else, exactly like the tokens
    /// <see cref="ReportStrayItem"/> keeps. Sections already abut, so a <c>|</c> between
    /// form items asks for nothing the page does not already do — and made into a barline
    /// NODE it asks for something the author did not write. Measured 2026-08-16:
    /// <c>form main { A | B }</c> engraved THREE bars where <c>form main { A B }</c>
    /// engraves two, because section A's music already closes with <c>|</c> and the
    /// language's own rule is that "an empty measure is always an explicit <c>| |</c> pair"
    /// (MeasureCollector.Form ProcessSectionPrologue). Both books in the tree that write
    /// this spell it as a divider before a <c>[1. …]</c> ending, which is not an empty bar.
    /// </para>
    /// <para>
    /// ⚠️ Every OTHER barline IS a request for a glyph, and parsing it as the ordinary
    /// <see cref="BarlineSyntax"/> the music stream uses grants it. Measured on
    /// <c>scratch/…/blogger.lys</c>, whose form writes <c>… to coda || D …</c>: the page
    /// gained exactly ONE element — the single <c>&lt;rect x="59.82"&gt;</c> became the pair
    /// <c>x="59.51"</c> / <c>x="60.00"</c>, 0.49 apart, which is a double barline — with
    /// every other difference being the respacing that one glyph causes, and the bar count
    /// and MIDI unchanged. Before this it was dropped, and <c>A || B</c> engraved
    /// byte-identically to <c>A B</c>.
    /// </para>
    /// <para>
    /// ⚠️ The <c>|:</c> and <c>:|</c> arms above are reached first and keep their own
    /// meanings; this is the rest of the family, which had none.
    /// </para>
    /// </remarks>
    private GreenNode ParseFormBarline() =>
        Current.Text == "|" ? Advance() : ParseBarline();

    /// <summary>
    /// Parse a plain section reference: <c>Chorus</c>, <c>Chorus'</c>, <c>Chorus, "label"</c>.
    /// </summary>
    /// <remarks>
    /// The trailing <c>'</c> / <c>,</c> are the SAME marks a phrase reference carries
    /// (<see cref="ParsePhraseOctaveMarks"/>) and mean the same thing: they move the frame
    /// this PLAY of the section resolves in. Written out rather than left inline in
    /// <see cref="ParseFormItem"/>'s switch because three tokens now have to be taken in
    /// source order — name, marks, label — and a switch arm's argument list gives no place
    /// to put the middle one.
    /// </remarks>
    private SectionReferenceGreen ParseSectionReference()
    {
        var name = Advance();
        var marks = ParsePhraseOctaveMarks();
        return new SectionReferenceGreen(
            name, marks, Check(SyntaxKind.StringLiteral) ? Advance() : null);
    }

    /// <summary>
    /// Parse silent section reference: ~SectionName or ~SectionName "label".
    /// The optional label is kept on the node but NOT displayed (the '~' hides it);
    /// it lets an author park a label text and reveal it later by dropping the '~'.
    /// The octave marks ride here too — the tilde hides the LABEL, never the music, and
    /// the marks are part of the music.
    /// </summary>
    private SilentSectionReferenceGreen ParseSilentSectionReference()
    {
        var tilde = Expect(SyntaxKind.Tilde);
        var name = ExpectPartName();
        var marks = ParsePhraseOctaveMarks();

        // '~B "alt"' — a label parked on a tilde reference. Kept, never dropped.
        // ⚠️ THE WARNING THAT USED TO LIVE HERE (LYS0012) MOVED OUT on 2026-08-31, and it had
        // to: it said "hidden by '~'", and since a section can declare its own label default
        // (`section ~A { … }`) whether THIS label prints depends on the DECLARATION — which a
        // parser cannot see. Asking the question here would have shipped an instrument that
        // says "hidden" over a label the page prints. It is FormDeclarationValidator's now,
        // where the declarations are, and it asks the real question (is this play shown?)
        // instead of the surface one (is there a tilde?).
        SyntaxToken? label = Check(SyntaxKind.StringLiteral) ? Advance() : null;

        return new SilentSectionReferenceGreen(tilde, name, marks, label);
    }

    /// <summary>
    /// Parse music mark: @segno, @fine, @ds.al.fine, etc.
    /// </summary>
    private MusicMarkGreen ParseMusicMark()
    {
        var at = Expect(SyntaxKind.At);
        // '@!X' — the terminator form, read here for the same reason the music stream reads
        // it (Parser.Music ParseArticulations): kept on the node, stepped over by Name /
        // MarkName. A form body is not where a text spanner is written, but refusing the
        // spelling HERE would report a lexical surprise ("expected a mark name") for what is
        // really a placement mistake — the annotation validator names that one properly.
        var bang = Check(SyntaxKind.DashedBar) ? Advance() : null;
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
        var parts = bang is null
            ? new List<SyntaxToken> { at, name }
            : new List<SyntaxToken> { at, bang, name };
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
        // Navigation keywords are the live case here: this function is reached ONLY from
        // the form-level `@` item (the single caller is ParseFormItem), where the marks
        // are @ds.al.fine and friends.
        //
        // ⚠️ The integer / RestS / PitchF arms below say they are for figured bass, and
        // that is the wrong island: figured bass is note-attached (Parser.Music.cs), not
        // form-level, and its written form is `@fig(6 s)` — the dotted `@fig.6.s` the old
        // comment showed does not parse anywhere (measured 2026-08-15: LYS0016). They are
        // kept rather than deleted because "unreachable" is a claim and nothing measures
        // it; NO corpus book or fixture reaches them. Deleting them is a job for whoever
        // can show a form-level `@6` is meaningless. (A LILYPOND-REF to
        // figured-bass-engraver.cc sat on this line and pointed at that wrong island.)
        if (Current.Kind is SyntaxKind.Identifier
            or SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
            or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword
            or SyntaxKind.AlKeyword
            or SyntaxKind.IntegerLiteral  // unobserved — see the note above
            or SyntaxKind.RestS           // unobserved — 's' lexes as a rest
            or SyntaxKind.PitchF)         // unobserved — 'f' lexes as a pitch
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
        // The same trailing octave marks a plain reference takes: an ending IS a section
        // reference with a bracket around it, so `[1. B']` opens B an octave up for THAT
        // play. ⚠️ Collected AFTER the name on purpose — the range separator in `[1,3. B]`
        // is a Comma token too, and it sits before the dot, so the two can only be told
        // apart by position (SyntaxFacts.NetOctaveMarksFrom takes a starting slot for
        // exactly this node).
        var marks = ParsePhraseOctaveMarks();
        // Optional display label: [1. B "label"] — shown as the section's mark,
        // exactly like a plain reference's  A "A2".
        SyntaxToken? displayLabel = Check(SyntaxKind.StringLiteral) ? Advance() : null;
        // The ']' is optional: present = closed (right cap drawn), absent = open.
        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;

        return new FormAlternativeGreen(openBracket, number, separator, endNumber, dot, tilde, section, marks, displayLabel, closeBracket);
    }

    /// <summary>
    /// Parse repeat block: |: ... :| or |: ... :|*3
    /// </summary>
    private FormRepeatBlockGreen ParseFormRepeatBlock()
    {
        int startPosition = _textPosition;
        var startBar = Expect(SyntaxKind.RepeatStartBar);

        var items = new List<GreenNode?>();
        var alternatives = new List<GreenNode?>();
        SyntaxToken? pipeBeforeAlternatives = null;
        int voltaBracketsBeforeClose = 0;

        // Parse items until :| or | (for alternatives)
        // ⚠️ AND STOP AT THE FORM'S OWN `}`. Without that stop an unclosed `|:` ate the rest
        // of the FILE looking for a `:|` that was never coming: `form main { ~Body |: A }`
        // reported `}`, `score`, `{`, `staff`, `}` as five things "a form cannot hold" and
        // only then said "Expected RepeatEndBar, found EndOfFile" — five wrong errors before
        // the true one, and the score block declared garbage. Reported 2026-08-31 on
        // scratch/ベースタブLy/Venus.lys, where the author had moved the repeat's OPEN into
        // the form and left its `:|` in the section's music.
        while (!Check(SyntaxKind.RepeatEndBar) && !Check(SyntaxKind.EndOfFile)
               && !Check(SyntaxKind.CloseBrace))
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

        // ⚠️ THE PAIRING USED TO CROSS THE FORM/MUSIC LINE IN ONE DIRECTION ONLY, and saying
        // so was the whole value of this message: a `|:` written in a SECTION could be closed
        // by a `:|` the form wrote (which is why LYS4017 is raised only after score
        // expansion), while the reverse never held, because a form repeat is a BRACKETED
        // construct. That ONE-SIDEDNESS is the argument that carried LYS1034 — the author
        // moving structure into the form always hit the direction that does not work first —
        // and since 2026-08-31 neither direction exists: a repeat can only be written here.
        SyntaxToken endBar;
        if (Check(SyntaxKind.RepeatEndBar))
        {
            endBar = Advance();
        }
        else
        {
            _diagnostics.Error(new TextSpan(startPosition, Math.Max(1, _textPosition - startPosition)),
                DiagnosticCodes.UnpairedRepeat,
                "this form repeat '|:' is never closed — add the matching ':|' before the "
                + "form's '}'. A repeat opens and closes in the form; a ':|' written in a "
                + "section's music does not close it, and is not a spelling the language "
                + "takes any more.");
            endBar = new SyntaxToken(SyntaxKind.RepeatEndBar, "", null, null);
        }

        // Explicit play count, written ON the bar line: `:|*3`. The SAME spelling the inline
        // music stream takes (Parser.Music.cs ParseBarline), which is LilyPond's `R1*20`
        // multiplier idiom — one thing, one spelling, in both places it can be written.
        // ⚠️ IT WAS `x3` UNTIL 2026-08-03 AND COULD NEVER HAVE WORKED: the lexer reads `x3` as
        // ONE identifier, so the `Current.Text == "x"` test never matched and the count fell
        // through to the form's item list as a section reference nobody declared (LYS1005).
        // Two ParserTests wrote `:| x4` and passed the whole time, because they assert only that
        // nothing is a SYNTAX error and that the tree round-trips — never that the count
        // arrived. `*` also cannot begin an identifier, so `*3` has no such ambiguity to have.
        SyntaxToken? asterisk = null;
        SyntaxToken? repeatCount = null;
        if (Check(SyntaxKind.Asterisk))
        {
            asterisk = Advance();
            repeatCount = Expect(SyntaxKind.IntegerLiteral);
        }

        // Final alternative after :| — the bare "2. A2" form or the bracket form
        // "[2. A2]", so a structure repeat reads exactly like the inline volta:
        //   |: Intro2 B C A2 [1. D] :| [2. Outro]
        GreenNode? finalAlternative = null;
        if (Check(SyntaxKind.IntegerLiteral))
            finalAlternative = ParseFormAlternative();
        else if (Check(SyntaxKind.OpenBracket))
            finalAlternative = ParseVoltaBracket();

        // A THIRD ENDING, AND A FOURTH: `:| [3. C]` again, as many times as it is written.
        // ⚠️ This arm exists because of LYS1034 (2026-08-31). The music stream could spell
        // `|: X | [1. A] :| [2. B] :| [3. C]` and the form could not — it took ONE ending
        // after its `:|` and dropped the rest, which then warned LYS6008 ("no repeat opens
        // this ending") and engraved as a plain reference. That was survivable only while
        // the music spelling existed. MEASURED before writing this: 13 of the author's 326
        // books and one tracked book (audit/lpreg/voltasky) write a third or later ending,
        // so banning the music spelling without this arm would have left them NO spelling.
        // ⚠️ It takes the same two shapes the first final ending takes, so the bare
        // `:| 3. C` still reaches ParseFormAlternative's "the bracket is required" hint
        // rather than falling out of the block as a stray `:|`.
        var furtherAlternatives = new List<GreenNode?>();
        while (finalAlternative != null && Check(SyntaxKind.RepeatEndBar)
               && Peek(1).Kind is SyntaxKind.OpenBracket or SyntaxKind.IntegerLiteral)
        {
            furtherAlternatives.Add(Advance());   // the ':|' between this ending and the next
            furtherAlternatives.Add(Check(SyntaxKind.OpenBracket)
                ? ParseVoltaBracket()
                : ParseFormAlternative());
        }

        return new FormRepeatBlockGreen(startBar, [.. items], pipeBeforeAlternatives, [.. alternatives], endBar, finalAlternative, asterisk, repeatCount, [.. furtherAlternatives]);

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

        // "to coda", two words — the one spelling (Lexer.GetKeywordKind's note on `to`).
        if (first.Kind == SyntaxKind.ToKeyword)
        {
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
            || Check(SyntaxKind.PitchKeyword)
            || Check(SyntaxKind.StringLiteral)
            ? null : Advance();

        // Optional output basename (a quoted string). The extension, if written,
        // is dropped downstream (the file format is a CLI choice).
        SyntaxToken? filename = Check(SyntaxKind.StringLiteral) ? Advance() : null;

        // Optional per-score options, in either order: `transpose <pitch>` (stored as a
        // transpose property, the shape the part header uses) and `pitch concert|written`
        // (how this score prints a transposing part — Semantics.ConcertPitch).
        var options = new List<GreenNode?>();
        while (Check(SyntaxKind.TransposeKeyword) || Check(SyntaxKind.PitchKeyword))
            options.Add(Check(SyntaxKind.TransposeKeyword) ? ParsePartProperty() : ParsePitchDirective());

        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseRenderItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        // The `name` slot now carries the form reference; `filename` the basename.
        return new RenderDeclarationGreen(keyword, formName, filename, [.. options], openBrace, [.. items], closeBrace);
    }


    /// <summary>
    /// Check if current token can be a part name (Identifier or instrument keyword like bass).
    /// </summary>
    private bool IsPartNameStart() => IsPartNameKind(Current.Kind);

    // Delegates to the one home for the rule — the render nodes read the same predicate
    // when deciding which of their kept tokens are members (see SyntaxFacts).
    private static bool IsPartNameKind(SyntaxKind? kind) =>
        kind is { } k && SyntaxFacts.IsPartNameKind(k);

    /// <summary>
    /// The token after the <c>-</c> in a hyphenated part-header value (<c>instrument
    /// bass-guitar</c>): any BARE WORD, whatever else that word is reserved for.
    /// </summary>
    /// <remarks>
    /// ⚠️ A separate predicate from <see cref="IsPartNameKind"/> on purpose. That one answers
    /// "may this word name a part?" and was standing in for this question until 2026-08-19,
    /// which made <c>voice-soprano</c> — a published preset that <c>GetInstrument</c> reads —
    /// impossible to write, while <c>voice-alto</c>, <c>voice-tenor</c> and <c>voice-bass</c>
    /// compiled because their tails are clef words and <c>soprano</c> is not.
    /// The rule asks the token's TEXT rather than its kind: what makes a tail legal is being a
    /// word, and no set of kinds says that without being re-checked every time a keyword is
    /// added.
    /// </remarks>
    private static bool IsHyphenatedValueTail(SyntaxToken? token) =>
        token is { } t && SyntaxFacts.IsBareWord(t.Text);

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

    private GreenNode? ParseRenderItem()
    {
        return Current.Kind switch
        {
            SyntaxKind.StaffKeyword => ParseStaffRender(),
            SyntaxKind.ChordsKeyword => ParseChordRowRender(),
            SyntaxKind.LyricsKeyword => ParseLyricsRowRender(),
            SyntaxKind.GrandStaffKeyword => ParseGrandStaffRender(),
            SyntaxKind.StaffGroupKeyword => ParseGrandStaffRender(),
            SyntaxKind.ChoirStaffKeyword => ParseGrandStaffRender(),
            SyntaxKind.CondensedStaffKeyword => ParseCondensedStaffRender(),
            SyntaxKind.CombinedStaffKeyword => ParseCombinedStaffRender(),
            SyntaxKind.TabKeyword => ParseTabRender(),
            SyntaxKind.OssiaKeyword => ParseOssiaRender(),
            // A per-score header: `score main { title "…" composer "…" staff … }`
            // restates the file's title/composer for THIS score only. Parsed as the
            // ordinary metadata node so it round-trips and keeps its source spans —
            // an unrecognised token here falls to ParseList's Advance(), which drops
            // the token's width and shifts every following source offset.
            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword => ParseMetadataDeclaration(),
            // `fonts NAME` / `paper NAME` — this score's reference to a named top-level
            // block, optionally with an override block of its own. Parsed by the same
            // functions as the declarations (the node is one shape; position says which
            // reading it gets), with the in-score flag choosing the shape errors.
            SyntaxKind.FontsKeyword => ParseFontDeclaration(inScore: true),
            SyntaxKind.PaperKeyword => ParsePaperDeclaration(inScore: true),
            // The same drop this comment names, for the one keyword that reads most like it
            // belongs here: `using` includes a FILE, not a staff (LYS0029).
            SyntaxKind.UsingKeyword => ParseMisplacedUsing("a score"),
            _ when IsPartNameStart() => ParseMidiPartRender(),
            // Anything else: reported and KEPT (LYS0030) — the drop the comment four lines
            // above already named, now neither silent nor width-losing.
            // ⚠️ This vocabulary is the ONE list a writer actually reads — it arrives at the
            // moment of the mistake — so it has to name every branch above. It omitted the two
            // staff groups and the bare-part-name item, and advertised braces `ossia` does not
            // take, until DocKeywordListTests grew a machine for it.
            _ => ReportStrayItem("a score",
                    "A score body holds render items — 'staff NAME', 'tab NAME', "
                    + "'grandStaff { … }', 'staffGroup { … }', 'choirStaff { … }', "
                    + "'condensedStaff { … }', 'combinedStaff { … }', 'ossia NAME', "
                    + "'chords NAME', 'lyrics NAME' — its own 'title'/'composer', a "
                    + "'fonts NAME' / 'paper NAME' reference, and a bare part name to "
                    + "render that part to MIDI only.")
        };
    }

    /// <summary>
    /// Parse staff render: staff [clef] { partName }
    /// </summary>
    private StaffRenderGreen ParseStaffRender()
    {
        // staff [~] [clef] part ["display name"] [as lines N]   (no braces)
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.StaffKeyword) };

        // `staff ~flute` suppresses the default instrument name label.
        if (Check(SyntaxKind.Tilde))
            tokens.Add(Advance());

        // A clef keyword followed by a part name is an override.
        if (IsClefKeyword() && IsPartNameKind(Peek(1)?.Kind))
            tokens.Add(Advance());

        tokens.Add(ExpectPartName());

        // `staff flute "津田さん"` overrides the displayed instrument name. QUOTED ONLY
        // (2026-08-23, user-approved — GRAMMAR_AUDIT section 3.1): the bare form this
        // branch used to accept (`staff flute 津田さん`) was position-dependent, because a
        // ScoreItem may also be a BARE PART NAME — the MIDI-only part, played and never
        // engraved, beginning with no keyword at all. So `staff flute click` took `click`
        // as the label of flute's staff and the click track silently stopped being played;
        // GRAMMAR.md section 7 had to teach a word-order workaround. A trailing bare
        // identifier now falls through to the ScoreItem loop and reads as that MIDI-only
        // part reference — the meaning the grammar could never reach from this position.
        //
        // Measured 2026-08-21 across the 571 .lys in samples/ audit/ Fixtures/: the bare
        // form was written NOWHERE, and the quoted form nowhere either (the tests that use
        // a display name build their source in C#). The one pin was
        // ScoreRowFoldingTests.TheRetiredWithSpelling_ReadsAsADisplayNameAndARow —
        // `staff vocal with lyrics ja` degrading into a staff labelled "with" plus a row,
        // graceful decay of a retired, never-released clause; it now pins the new reading.
        if (Check(SyntaxKind.StringLiteral))
            tokens.Add(Advance());

        ConsumeLinesSelector(tokens);
        return new StaffRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse chord-row render: <c>chords partName [as roman|names]</c> (places a
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
    /// Shared by the chord display mode (<c>as roman | names</c>) and the tab
    /// style (<c>as numbers | full</c>). NB: <c>as</c> also lexes as the Dutch A-flat
    /// pitch, so match it by TEXT, not token kind; the mode word follows. This position
    /// (right after the target name) is unambiguous — a bare pitch there is meaningless —
    /// so the match is safe.</summary>
    private void ConsumeAsSelector(List<SyntaxToken> tokens)
    {
        if (string.Equals(Current.Text, "as", System.StringComparison.Ordinal) && Peek(1) != null)
        {
            tokens.Add(Advance()); // as
            tokens.Add(Advance()); // roman | names
        }
    }

    /// <summary>
    /// Consumes an optional <c>as lines N</c> staff-line selector on a staff or
    /// ossia render item. The line count moved OFF the part header (user
    /// decision, 2026-08-19): the count is presentation, so the SCORE item that
    /// renders the part carries it — the same part can print five-lined in the
    /// full score and one-lined in a lead sheet. Matched by TEXT like
    /// <see cref="ConsumeAsSelector"/>: <c>as</c> also lexes as the Dutch
    /// A-flat pitch, and <c>lines</c> is an ordinary word, not a keyword.
    /// The range check keeps the part-header era's message word for word
    /// (SymbolCaseValidator's CheckWholeNumber, retired with the property).
    /// </summary>
    private void ConsumeLinesSelector(List<SyntaxToken> tokens)
    {
        if (!string.Equals(Current.Text, "as", System.StringComparison.Ordinal)
            || Peek(1) is not { } second
            || !string.Equals(second.Text, "lines", System.StringComparison.Ordinal))
            return;

        tokens.Add(Advance()); // as
        tokens.Add(Advance()); // lines

        string valueText = Check(SyntaxKind.IntegerLiteral) ? Current.Text : "";
        if (!(int.TryParse(valueText, out int n)
            && n >= Semantics.LanguageVocabulary.MinStaffLines
            && n <= Semantics.LanguageVocabulary.MaxStaffLines))
            _diagnostics.Error(
                new TextSpan(_textPosition + Current.LeadingTriviaWidth, Current.Text.Length),
                DiagnosticCodes.UnknownSymbolCase,
                $"'{Current.Text}' is not a staff-line count. 'lines' takes a whole number " +
                $"from {Semantics.LanguageVocabulary.MinStaffLines} to {Semantics.LanguageVocabulary.MaxStaffLines}.");
        if (Check(SyntaxKind.IntegerLiteral))
            tokens.Add(Advance()); // N
    }

    /// <summary>
    /// Parse lyrics-row render: <c>lyrics partName [sings partName]</c> (places a
    /// lyrics part as a row; the optional <c>sings</c> names the melody THIS ROW
    /// sings, overriding the definition's default for this placement).
    /// </summary>
    private LyricsRowRenderGreen ParseLyricsRowRender()
    {
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.LyricsKeyword) };
        tokens.Add(ExpectPartName());
        // Contextual like the definition's (Parser.Sections): 'sings' is claimed only
        // directly after a row's track name, and stays an ordinary identifier
        // everywhere else. Without this branch the word fell through to the score's
        // next render item and was reported as an undefined MIDI part.
        if (Check(SyntaxKind.Identifier) && Current.Text == "sings")
        {
            tokens.Add(Advance());
            tokens.Add(ExpectPartName());
        }
        return new LyricsRowRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse a bracket/brace staff-group render: grandStaff / staffGroup /
    /// choirStaff { staff staff ... }. The leading keyword (already the current
    /// token) is kept on the green node so the collector can pick the group type.
    /// A `lyrics NAME` row is a member too (score = a vertical stack of bands,
    /// inside a group as outside): a bound row under the staff it sings folds
    /// into that staff's verses (RenderSpecParser.FoldAdjacentRows), which is
    /// how a chorale writes the words between the sopranos and the altos.
    /// A row that sings no adjacent staff is LYS6012 (validator). Anything else
    /// is LYS6011, reported at the member for the reason recorded on
    /// ParseBarePartNameMembers, and its tokens are KEPT (width-preserving).
    /// </summary>
    private GrandStaffRenderGreen ParseGrandStaffRender()
    {
        var grandStaffKeyword = Advance(); // grandStaff | staffGroup | choirStaff
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var members = new List<GreenNode>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            if (Check(SyntaxKind.StaffKeyword))
            {
                members.Add(ParseStaffRender());
                continue;
            }
            if (Check(SyntaxKind.LyricsKeyword))
            {
                members.Add(ParseLyricsRowRender());
                continue;
            }

            _diagnostics.Error(
                new TextSpan(_textPosition, Current.FullWidth),
                DiagnosticCodes.StaffGroupBadMember,
                $"'{grandStaffKeyword.Text}' cannot contain '{Current.Text}' — a staff group "
                + "holds 'staff NAME' items and, between them, 'lyrics NAME' rows. "
                + "A chords row stands above the group, outside the braces.");

            // Keep the offending tokens (the whole ITEM — its bare tokens, then a
            // braced body if it has one) so the report is one per item, not one
            // per token — and a bare Advance() would drop their width and shift
            // every later source offset (see ParseBarePartNameMembers).
            members.Add(Advance());
            while (!Check(SyntaxKind.StaffKeyword) && !Check(SyntaxKind.LyricsKeyword)
                && !Check(SyntaxKind.OpenBrace) && !Check(SyntaxKind.CloseBrace)
                && !Check(SyntaxKind.EndOfFile))
            {
                members.Add(Advance());
            }
            if (Check(SyntaxKind.OpenBrace))
            {
                int depth = 0;
                do
                {
                    if (Check(SyntaxKind.OpenBrace)) depth++;
                    else if (Check(SyntaxKind.CloseBrace)) depth--;
                    members.Add(Advance());
                }
                while (depth > 0 && !Check(SyntaxKind.EndOfFile));
            }
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new GrandStaffRenderGreen(grandStaffKeyword, openBrace, [.. members], closeBrace);
    }

    /// <summary>
    /// Parse a condensed-staff render: <c>condensedStaff { partA partB … }</c>.
    /// </summary>
    /// <remarks>
    /// The members are BARE part names, like <c>tab m</c> / <c>ossia m</c> / <c>chords p</c>
    /// and unlike <c>grandStaff { staff a staff b }</c> — a grand staff really does contain
    /// staves, while everything written here ends up as a VOICE of the one staff that comes
    /// out. Source order is voice order: the first part is voice 1 (stems up), exactly as the
    /// first block of a <c>voice { … } { … }</c> span.
    /// The arity rule (two or more) is enforced semantically, not here, so a half-typed
    /// <c>condensedStaff { fl1 }</c> still produces a tree for the editor.
    /// </remarks>
    private CondensedStaffRenderGreen ParseCondensedStaffRender()
    {
        var keyword = Expect(SyntaxKind.CondensedStaffKeyword);
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var partNames = ParseBarePartNameMembers(
            "condensedStaff", DiagnosticCodes.CondensedStaffBadMember,
            "Everything inside becomes a VOICE of the one staff it produces, and a staff or "
            + "a group of staves is not a voice.");
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new CondensedStaffRenderGreen(keyword, openBrace, [.. partNames], closeBrace);
    }

    /// <summary>
    /// Parse a combined-staff render: <c>combinedStaff { partA partB }</c>.
    /// </summary>
    /// <remarks>
    /// Same shape as <see cref="ParseCondensedStaffRender"/> — bare part names, one staff
    /// out. The arity (exactly two) is enforced semantically so a half-typed
    /// <c>combinedStaff { fl1 }</c> still produces a tree for the editor.
    /// </remarks>
    private CombinedStaffRenderGreen ParseCombinedStaffRender()
    {
        var keyword = Expect(SyntaxKind.CombinedStaffKeyword);
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var partNames = ParseBarePartNameMembers(
            "combinedStaff", DiagnosticCodes.CombinedStaffBadMember,
            "It combines two PARTS — it has to see each part's own music to decide, moment "
            + "by moment, whether they are playing the same thing.");
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new CombinedStaffRenderGreen(keyword, openBrace, [.. partNames], closeBrace);
    }

    /// <summary>
    /// The body shared by <c>condensedStaff</c> and <c>combinedStaff</c>: bare part names
    /// until the closing brace, with anything else reported and skipped.
    /// </summary>
    /// <remarks>
    /// A non-part member is reported HERE rather than left to the brace mismatch: stopping
    /// the loop and failing on the missing '}' produces "Expected 'CloseBrace'", which
    /// describes the parser's predicament and not the writer's mistake.
    /// <c>condensedStaff { staff a staff b }</c> is the first thing a grandStaff user tries,
    /// so it has to answer well.
    /// </remarks>
    private List<SyntaxToken> ParseBarePartNameMembers(
        string containerName, string diagnosticCode, string why)
    {
        var members = new List<SyntaxToken>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            if (IsPartNameStart())
            {
                members.Add(ExpectPartName());
                continue;
            }

            _diagnostics.Error(
                new TextSpan(_textPosition, Current.FullWidth),
                diagnosticCode,
                $"'{containerName}' cannot contain '{Current.Text}' — write bare part names, "
                + $"e.g. {containerName} {{ flute1 flute2 }}. " + why);

            // Pass over the offending item — the keyword, then a braced body if it has one
            // — but KEEP every token it spans.
            // ⚠️ Reporting is not enough: consuming these with a bare Advance() dropped
            // their WIDTH, so every source offset after the block shifted left and the
            // preview's click-to-jump landed short (measured — a `condensedStaff { staff
            // fl1 staff fl2 }` lost the two `staff ` words and moved a later score's title
            // data-pos off its string). The tokens are kept as members of the green node
            // and filtered out by KIND where the names are read (SyntaxFacts.IsPartNameKind
            // in CondensedStaffRenderSyntax / CombinedStaffRenderSyntax), so they cost
            // nothing but the width they are here to preserve.
            members.Add(Advance());
            if (Check(SyntaxKind.OpenBrace))
            {
                int depth = 0;
                do
                {
                    if (Check(SyntaxKind.OpenBrace)) depth++;
                    else if (Check(SyntaxKind.CloseBrace)) depth--;
                    members.Add(Advance());
                }
                while (depth > 0 && !Check(SyntaxKind.EndOfFile));
            }
        }
        return members;
    }

    private bool IsClefKeyword() => SyntaxFacts.IsClefKeyword(Current.Kind);

    /// <summary>
    /// Parse ossia render: ossia [clef] partName — bare, exactly like staff
    /// (the braces of the old form only ever held the one name).
    /// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
    /// </summary>
    private OssiaRenderGreen ParseOssiaRender()
    {
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.OssiaKeyword) };

        // A clef keyword followed by a part name is an override; alone it IS
        // the part name (clef words are legal part names, as for staff).
        if (IsClefKeyword() && IsPartNameKind(Peek(1)?.Kind))
            tokens.Add(Advance());

        tokens.Add(ExpectPartName());
        ConsumeLinesSelector(tokens);
        return new OssiaRenderGreen([.. tokens]);
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
