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
    // ========== Repeat and Parallel ==========

    private RepeatExpressionGreen ParseRepeatExpression()
    {
        int startPos = _textPosition;
        var repeatKeyword = Expect(SyntaxKind.RepeatKeyword);

        // Expect repeat type: unfold, percent, tremolo (volta is no longer a Lily#
        // construct — see the diagnostic below).
        SyntaxToken repeatType;
        if (Check(SyntaxKind.VoltaKeyword) || Check(SyntaxKind.Identifier))
        {
            repeatType = Advance();

            // 'repeat volta' / 'alternative' were removed in favor of the symbolic
            // |: … :| form with inline volta endings. Reject with a friendly hint and
            // recover by parsing the rest so no cascade errors follow.
            if (repeatType.Kind == SyntaxKind.VoltaKeyword || repeatType.Text == "volta")
            {
                var voltaSpan = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
                _diagnostics.Error(voltaSpan, DiagnosticCodes.RepeatVoltaRemoved,
                    "'repeat volta' is not a Lily# construct; use the symbolic repeat "
                    + "'|: … :|' (explicit count '|: … :|*N') with inline volta endings "
                    + "'[1. …] [2. …]'.");
            }
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected repeat type (unfold, percent, tremolo)");
            // Missing token: zero-width so root.FullWidth == text.Length holds
            // (matches Expect); the diagnostic above already reported the error.
            repeatType = new SyntaxToken(SyntaxKind.VoltaKeyword, "", null, null);
        }

        // Expect count
        var count = Expect(SyntaxKind.IntegerLiteral);

        // Parse body
        var body = ParseMusicBlock();

        // Parse optional alternative
        AlternativeClauseGreen? alternative = null;
        if (Check(SyntaxKind.AlternativeKeyword))
        {
            alternative = ParseAlternativeClause();
        }

        return new RepeatExpressionGreen(repeatKeyword, repeatType, count, body, alternative);
    }

    private AlternativeClauseGreen ParseAlternativeClause()
    {
        var alternativeKeyword = Expect(SyntaxKind.AlternativeKeyword);
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var alternatives = new List<GreenNode?>();
        while (Check(SyntaxKind.OpenBrace))
        {
            alternatives.Add(ParseMusicBlock());
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new AlternativeClauseGreen(alternativeKeyword, openBrace, [.. alternatives], closeBrace);
    }

    /// <summary>
    /// Parse parallel voices on one staff: <c>voice { … } voice { … } …</c>.
    /// Consecutive <c>voice</c> blocks become the staff's simultaneous voices
    /// (the 1st gets stems up, the 2nd down, and so on). This is the only
    /// polyphony form — the old <c>&lt;&lt; … \\ … &gt;&gt;</c> was removed — and it
    /// desugars to the same ParallelExpression those produced, so the collector,
    /// renderer and exporters are unchanged.
    /// </summary>
    /// <summary>Tracks whether the parser is inside a voice { } body, so a
    /// nested voice keyword can be flagged (it would silently become a
    /// parallel SIBLING voice, not an inner one).</summary>
    private int _voiceBodyDepth;

    /// <summary>drummap { hh: position 6 notehead x … } — the body is stored
    /// token-for-token; the red node reads the entries.</summary>
    private DrummapDeclarationGreen ParseDrummapDeclaration()
    {
        var keyword = Expect(SyntaxKind.DrummapKeyword);
        var open = Expect(SyntaxKind.OpenBrace);
        var tokens = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
            tokens.Add(Advance());
        var close = Expect(SyntaxKind.CloseBrace);
        return new DrummapDeclarationGreen(keyword, open, [.. tokens], close);
    }

    /// <summary>Flags a voice block opened INSIDE another voice's body, then
    /// recovers by INLINING its content into the enclosing voice (the braces
    /// read as transparent): no phantom parallel voice, no voice renumbering,
    /// no cascading measure warnings — the LYS0010 error alone marks the
    /// defect. A chain of nested voice blocks recovers one wrapper each.</summary>
    private GreenNode ParseVoiceBlocksCheckingNesting()
    {
        if (_voiceBodyDepth > 0)
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.NestedVoiceBlock,
                "voice { } blocks do not nest — close the enclosing voice's braces first; "
                + "voices are written as SIBLINGS: voice { … } voice { … }.");
            var voiceKeyword = Advance();
            SyntaxToken? name = Check(SyntaxKind.Identifier) ? Advance() : null;
            var block = ParseMusicBlock();
            return new NestedVoiceRecoveryGreen(voiceKeyword, name, block);
        }
        return ParseVoiceBlocks();
    }

    /// <summary>
    /// A voice span: the <c>voice</c> keyword ONCE, then one block per voice —
    /// <c>voice { … } { … }</c>, or <c>voice sop { … } alt { … }</c> when the voices are
    /// named (a name binds a <c>lyrics NAME { }</c> block to that voice).
    /// </summary>
    /// <remarks>
    /// A name is an ordinary identifier, which in a music stream is also a PHRASE
    /// REFERENCE, so the two are told apart by the token after it: an identifier followed
    /// by <c>{</c> opens a named voice, anything else is a reference and ends the span.
    /// </remarks>
    private ParallelExpressionGreen ParseVoiceBlocks()
    {
        var firstVoice = Expect(SyntaxKind.VoiceKeyword);
        var children = new List<GreenNode?>();
        do
        {
            if (Check(SyntaxKind.Identifier)) children.Add(Advance()); // optional voice name
            _voiceBodyDepth++;
            children.Add(ParseMusicBlock());
            _voiceBodyDepth--;
        }
        while (StartsAnotherVoiceBlock());

        // `voice { … } voice { … }` — the spelling before the keyword became a one-time
        // opener. It is reported rather than left alone because it still PARSES: the
        // second `voice` would open a SECOND span, and two one-voice spans sound one after
        // the other instead of together, so the file would quietly hold different music.
        while (Check(SyntaxKind.VoiceKeyword))
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.RepeatedVoiceKeyword,
                "'voice' opens the span once — write the other voices as further blocks: "
                + "voice { … } { … } (named: voice sop { … } alt { … }). Repeating the keyword "
                + "would start a SECOND span, and two one-voice spans play one after the other, "
                + "not together.");
            Advance(); // recover INTO this span, so the music stays what was meant
            do
            {
                if (Check(SyntaxKind.Identifier)) children.Add(Advance());
                _voiceBodyDepth++;
                children.Add(ParseMusicBlock());
                _voiceBodyDepth--;
            }
            while (StartsAnotherVoiceBlock());
        }

        // ParallelExpression carries an open/close token; voice blocks have no
        // closing delimiter, so reuse the opening `voice` keyword as the open
        // marker and a synthetic empty close. ParallelExpressionSyntax.Voices
        // only reads the MusicBlock children, so the markers are inert.
        var close = new SyntaxToken(SyntaxKind.VoiceKeyword, "", null, null);
        return new ParallelExpressionGreen(firstVoice, [.. children], close);
    }

    /// <summary>True when the next tokens open another voice of the span: a <c>{</c>, or a
    /// name followed by one. A bare identifier NOT followed by <c>{</c> is a phrase
    /// reference in the enclosing stream and ends the span.</summary>
    private bool StartsAnotherVoiceBlock()
        => Check(SyntaxKind.OpenBrace)
           || (Check(SyntaxKind.Identifier) && Peek().Kind == SyntaxKind.OpenBrace);

    /// <summary>
    /// Reports that the old <c>&lt;&lt; … \\ … &gt;&gt;</c> polyphony was removed in
    /// favor of <c>voice { … }</c> blocks, then recovers by parsing the old shape
    /// into the same ParallelExpression so the rest of the file still parses.
    /// </summary>
    private ParallelExpressionGreen ParseRemovedParallelExpression()
    {
        int startPos = _textPosition;
        var open = Expect(SyntaxKind.DoubleOpenAngle);

        var children = new List<GreenNode?> { ParseRemovedVoiceContent() };
        while (Check(SyntaxKind.Backslash) && Peek().Kind == SyntaxKind.Backslash)
        {
            children.Add(Advance()); // first \
            children.Add(Advance()); // second \
            children.Add(ParseRemovedVoiceContent());
        }

        var close = Expect(SyntaxKind.DoubleCloseAngle);

        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        // Worded for LilyPond newcomers (who reach for << … \\ … >> by habit) as
        // much as for old Lily# files: state the Lily# form, don't assume history.
        _diagnostics.Error(span, DiagnosticCodes.ParallelSyntaxRemoved,
            "Lily# writes parallel voices as 'voice { … }' blocks, not '<< … \\\\ … >>' "
            + "— e.g. 'voice { c d } voice { e f }'.");

        return new ParallelExpressionGreen(open, [.. children], close);
    }

    private GreenNode ParseRemovedVoiceContent()
    {
        if (Check(SyntaxKind.OpenBrace))
            return ParseMusicBlock();

        // Bare inline voice (no braces): consume items up to \\ or >>.
        var items = new List<GreenNode?>();
        while (!Check(SyntaxKind.DoubleCloseAngle) && !Check(SyntaxKind.EndOfFile)
               && !(Check(SyntaxKind.Backslash) && Peek().Kind == SyntaxKind.Backslash))
        {
            var item = ParseMusicItem();
            if (item != null) items.Add(item);
            else break;
        }
        var openBrace = new SyntaxToken(SyntaxKind.OpenBrace, "", null, null);
        var closeBrace = new SyntaxToken(SyntaxKind.CloseBrace, "", null, null);
        return new MusicBlockGreen(openBrace, [.. items], closeBrace);
    }

    /// <summary>
    /// A leading backslash — a reflex for readers coming from LilyPond, where every
    /// command carries one and Lily# carries none. Reported (LYS0009, with the Lily# form
    /// named where there is one to name) and the <c>\</c> is KEPT as an inert token; the
    /// word after it is left for the next turn of the caller's loop, which parses it as
    /// whatever it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ The <c>\</c> used to be swallowed on every path, and swallowing it moved every
    /// later node one character early. Measured 2026-08-16 on
    /// <c>melody { \foo c4 d e f | }</c>: <c>c4</c> reported at 37 where the source has it
    /// at 38. The recovery is now one shape instead of three — consume the <c>\</c>, keep
    /// it, and let the loop handle the word — which also fixed <c>\nobreak</c>, whose
    /// keyword the old structural branch dropped even though <c>nobreak</c> is a real
    /// Lily# spelling that the very next dispatch would have parsed.
    /// </para>
    /// <para>
    /// ⚠️ The old doc claimed "backslashes that ARE valid Lily# (\tabStaff, \tuning)" were
    /// deliberately left alone. Measured 2026-08-16: they are not valid — <c>\tabStaff</c>
    /// reports "Undefined variable or phrase: 'tabStaff'" — and NO book in the tree writes
    /// either spelling. An unrecognised <c>\word</c> now gets the general hint rather than
    /// nothing, because a leading backslash is wrong in Lily# whatever follows it.
    /// </para>
    /// </remarks>
    private GreenNode ParseLilypondBackslashCommand()
    {
        string word = Peek(1).Text;
        string hint = word switch
        {
            "new" => "Lily# has no '\\new'; declare 'part name { … }' and lay it out with "
                + "'staff { … }' / 'voice { … }'.",
            "relative" => "Lily# is relative by default — drop '\\relative …'; switch modes "
                + "with 'octave absolute'.",
            "addlyrics" => "Lily# writes lyrics as 'lyrics { … }', not '\\addlyrics'.",
            "noBreak" or "nobreak" => "Lily# writes '\\noBreak' as 'nobreak' (no backslash).",
            "tempo" or "clef" or "key" or "time" or "transpose" or "octave" or "break"
                => $"Lily# commands take no leading backslash — write '{word} …', not '\\{word} …'.",
            // A phrase reference is the commonest thing a LilyPond habit puts a backslash
            // on, and it is written bare (or with '$'), so that is what the general hint
            // says. It is right for any word: nothing in Lily# takes a leading backslash.
            _ => $"Lily# has no leading backslash — write '{word}', not '\\{word}' "
                + "(a phrase reference is bare, or '$name').",
        };

        int startPos = _textPosition;
        var backslash = Advance();
        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.LilypondBackslashCommand, hint);
        // No `topLevel` switch any more: the word after the backslash is parsed by whichever
        // loop called this, at its own level, on its next turn — which is exactly what the
        // old `topLevel ? ParseTopLevelItem() : ParseMusicItem()` re-dispatch was for.
        return backslash;
    }

    // ========== Key, Clef, Tuplet ==========

    private KeySignatureGreen ParseKeySignature()
    {
        var keyKeyword = Expect(SyntaxKind.KeyKeyword);

        // Non-traditional signature: key custom fis cis … (altered pitches in
        // print order; naturals allowed for explicit cancels).
        if (Current.Kind == SyntaxKind.Identifier
            && Current.Text.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            var customWord = Advance();
            var pitches = new List<GreenNode?>();
            // The list ends at the first NOTE-like token: a pitch followed by
            // octave marks or a duration belongs to the music, not the key
            // (custom-signature pitches are written plain: fis, bes, …).
            while (IsPitchStart()
                   && Peek(1)?.Kind is not (SyntaxKind.Apostrophe or SyntaxKind.Comma
                       or SyntaxKind.IntegerLiteral))
                pitches.Add(ParsePitch());
            return new KeySignatureGreen(keyKeyword, customWord, [.. pitches]);
        }

        var pitch = ParsePitch();

        SyntaxToken mode;
        if (Check(SyntaxKind.MajorKeyword) || Check(SyntaxKind.MinorKeyword)
            || Check(SyntaxKind.IonianKeyword)
            || Check(SyntaxKind.DorianKeyword) || Check(SyntaxKind.PhrygianKeyword)
            || Check(SyntaxKind.LydianKeyword) || Check(SyntaxKind.MixolydianKeyword)
            || Check(SyntaxKind.AeolianKeyword) || Check(SyntaxKind.LocrianKeyword))
        {
            mode = Advance();
        }
        else if (Current.Kind == SyntaxKind.Identifier)
        {
            // A bare word here is an unknown / wrong-case mode (e.g. `Major`). Modes
            // are case-sensitive, so keep the word IN the key node — this preserves
            // the round-trip AND stops it leaking into the music as a stray phrase
            // reference — but flag it clearly instead of the generic "use $Major".
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.UnknownSymbolCase,
                $"Unknown mode '{Current.Text}'. Modes are case-sensitive: major, minor, " +
                "ionian, dorian, phrygian, lydian, mixolydian, aeolian, locrian.");
            mode = Advance();
        }
        else
        {
            // No mode written (`key bes`): assume major and WARN (not an error) — the
            // most common key is major, so this stays valid and renders. The span
            // points just past the pitch, where the mode would go.
            var span = new TextSpan(_textPosition, 0);
            _diagnostics.Warning(span, DiagnosticCodes.KeyModeAssumedMajor,
                $"No mode given for 'key {pitch.ToFullString().Trim()}'; assuming major. " +
                $"Write 'key {pitch.ToFullString().Trim()} major' to be explicit.");
            // Missing token: zero-width to preserve round-trip. Kind stays
            // MajorKeyword and SharpsFor("") resolves to major, so the recovery
            // default is unchanged.
            mode = new SyntaxToken(SyntaxKind.MajorKeyword, "", null, null);
        }

        return new KeySignatureGreen(keyKeyword, pitch, mode);
    }

    private ClefDeclarationGreen ParseClefDeclaration()
    {
        var clefKeyword = Expect(SyntaxKind.ClefKeyword);

        SyntaxToken clefName;
        if (SyntaxFacts.IsClefKeyword(Current.Kind))
        {
            clefName = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected clef name (treble, treble_8, alto, tenor, bass)");
            // Missing token: zero-width to preserve round-trip (diagnostic above).
            clefName = new SyntaxToken(SyntaxKind.TrebleKeyword, "", null, null);
        }

        return new ClefDeclarationGreen(clefKeyword, clefName);
    }

    /// <summary>
    /// Parse an octave mode directive: <c>octave absolute</c> / <c>octave relative</c>.
    /// The mode switches how <c>'</c>/<c>,</c> octave marks resolve (relative is
    /// the default; absolute makes each mark an offset from a fixed C4 anchor).
    /// </summary>
    private OctaveDirectiveGreen ParseOctaveDirective()
    {
        var octaveKeyword = Expect(SyntaxKind.OctaveKeyword);

        SyntaxToken mode;
        if (Check(SyntaxKind.Identifier) &&
            (Current.Text == "absolute" || Current.Text == "relative"))
        {
            mode = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected octave mode (absolute or relative)");
            // Missing token: zero-width to preserve round-trip. An empty mode is
            // read as neither "absolute" nor "relative", so it falls back to the
            // relative default — unchanged recovery behavior.
            mode = new SyntaxToken(SyntaxKind.Identifier, "", null, null);
        }

        return new OctaveDirectiveGreen(octaveKeyword, mode);
    }

    private TupletExpressionGreen ParseTupletExpression()
    {
        var tupletKeyword = Expect(SyntaxKind.TupletKeyword);
        var numerator = Expect(SyntaxKind.IntegerLiteral);
        var slash = Expect(SyntaxKind.Slash);
        var denominator = Expect(SyntaxKind.IntegerLiteral);
        var body = ParseMusicBlock();

        return new TupletExpressionGreen(tupletKeyword, numerator, slash, denominator, body);
    }
    private GraceExpressionGreen ParseGraceExpression()
    {
        var keyword = Advance(); // grace, acciaccatura, or appoggiatura
        var body = ParseMusicBlock();
        return new GraceExpressionGreen(keyword, body);
    }

    /// <summary>
    /// Parses <c>cue { … }</c> or <c>cue &lt;clef&gt; { … }</c>.
    /// </summary>
    /// <remarks>
    /// The clef is the same vocabulary a <c>clef</c> declaration takes
    /// (<see cref="SyntaxFacts.IsClefKeyword"/>), and it is LilyPond's <c>\cueClef</c> —
    /// a property of the REGION, not of any note. See docs/cue-context-design.md.
    /// </remarks>
    private CueExpressionGreen ParseCueExpression()
    {
        var keyword = Advance(); // cue
        SyntaxToken? clef = SyntaxFacts.IsClefKeyword(Current.Kind) ? Advance() : null;
        var body = ParseMusicBlock();
        return new CueExpressionGreen(keyword, clef, body);
    }


    // ========== New Section-Oriented Parsing ==========

    /// <summary>
    /// Parse variable declaration: identifier = { ... } (legacy)
    /// </summary>
    private VariableDeclarationGreen ParseNewVariableDeclaration()
    {
        int startPos = _textPosition;
        var name = Expect(SyntaxKind.Identifier);
        var equals = Expect(SyntaxKind.Equals);

        // Body is always a music block
        var body = ParseMusicBlock();

        // Lily# has no '=' assignment — declarations are written 'keyword name { … }'.
        // Reject neutrally (don't presume which keyword was meant — phrase, section, …)
        // and recover by keeping the parsed node so $name still resolves.
        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.LegacyDeclarationForm,
            $"'{name.Text} = …' is not valid; Lily# declarations use a keyword and braces, " +
            $"e.g. 'phrase {name.Text} {{ … }}' or 'section {name.Text} {{ … }}'.");

        return new VariableDeclarationGreen(name, equals, body);
    }

    /// <summary>
    /// Parse phrase declaration: phrase name { ... }
    /// </summary>
    /// <summary>
    /// Parse a using directive: <c>using "file.lys"</c>. The expander resolves
    /// the file before collection; here it is parsed as an inert top-level marker.
    /// </summary>
    private UsingDirectiveGreen ParseUsingDirective()
    {
        var keyword = Expect(SyntaxKind.UsingKeyword);
        var path = Expect(SyntaxKind.StringLiteral);
        return new UsingDirectiveGreen(keyword, path);
    }

    /// <summary>
    /// A <c>using</c> written somewhere only the FILE level can hold one — inside a section,
    /// a score, a form, a part header or a music block. Reported as
    /// <see cref="DiagnosticCodes.UsingMustBeTopLevel"/>, then parsed as the directive it is
    /// so its tokens stay in the tree.
    /// </summary>
    /// <param name="container">
    /// Where it was written, named the way the reader wrote it ("a section", "a score", …);
    /// it goes into the message so the error says which brace to move it out of.
    /// </param>
    /// <remarks>
    /// ⚠️ Parsing it — rather than reporting and consuming with <c>Advance()</c> — is half the
    /// repair, and the half that has no diagnostic of its own. A dropped token takes its WIDTH
    /// out of the green tree, and a node's position is the running sum of the widths before it,
    /// so every source offset after the directive slides left: measured, a <c>using</c> inside
    /// a section moved every <c>data-pos</c> in the rest of the file 16 characters early and
    /// made <c>check --pitches</c> read the directive's own letters as the music. The part
    /// header already reported (LYS0025) and still did this. See
    /// <see cref="DiagnosticCodes.UsingMustBeTopLevel"/> for the measurements.
    /// </remarks>
    private UsingDirectiveGreen ParseMisplacedUsing(string container)
    {
        // Span the keyword's INK (its leading trivia belongs to the line above it).
        _diagnostics.Error(
            new TextSpan(_textPosition + Current.LeadingTriviaWidth, Current.Text.Length),
            DiagnosticCodes.UsingMustBeTopLevel,
            $"a 'using' cannot go inside {container} — it includes a whole FILE, so it belongs "
            + "at the top level, beside the parts and sections it brings in. Written here it "
            + "does nothing at all.");
        return ParseUsingDirective();
    }

    private PhraseDeclarationGreen ParsePhraseDeclaration()
    {
        var keyword = Expect(SyntaxKind.PhraseKeyword);
        var name = ExpectPartName();
        var body = ParseMusicBlock();

        return new PhraseDeclarationGreen(keyword, name, body);
    }
}
