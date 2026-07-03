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

/// <summary>
/// Recursive descent parser for LilySharp.
/// </summary>
internal sealed class Parser
{
    private readonly List<SyntaxToken> _tokens;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly IncrementalReuseMap? _reuse;
    private int _position;
    private int _textPosition; // Tracks position in source text

    public Parser(IEnumerable<SyntaxToken> tokens)
        : this(tokens, reuse: null)
    {
    }

    /// <summary>
    /// Creates a parser that may adopt unchanged top-level green nodes from a
    /// previous tree instead of re-parsing them (incremental reparse).
    /// </summary>
    internal Parser(IEnumerable<SyntaxToken> tokens, IncrementalReuseMap? reuse)
    {
        _tokens = tokens.ToList();
        _reuse = reuse;
        _position = 0;
        _textPosition = 0;
    }

    public DiagnosticBag Diagnostics => _diagnostics;

    private SyntaxToken Current => _position < _tokens.Count
        ? _tokens[_position]
        : _tokens[^1]; // EOF

    private SyntaxToken Peek(int offset = 1) => _position + offset < _tokens.Count
        ? _tokens[_position + offset]
        : _tokens[^1];

    private SyntaxToken Advance()
    {
        var token = Current;
        if (_position < _tokens.Count - 1)
        {
            _textPosition += token.FullWidth;
            _position++;
        }
        return token;
    }

    private bool Check(SyntaxKind kind) => Current.Kind == kind;
    private bool CheckAny(params SyntaxKind[] kinds) => kinds.Contains(Current.Kind);

    private SyntaxToken Expect(SyntaxKind kind)
    {
        if (Check(kind))
            return Advance();

        // Report error
        var span = new TextSpan(_textPosition, Current.FullWidth);
        _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
            $"Expected '{kind}', found '{Current.Kind}'");

        // Error recovery: create a zero-width missing token. It must carry NO trivia:
        // the parser does not Advance past Current, so Current keeps its own leading
        // trivia and contributes it when later consumed. Borrowing it here too would
        // count that trivia twice and break the root.FullWidth == text.Length invariant
        // (and round-tripping). Matches every hand-written synthetic token below.
        return new SyntaxToken(kind, "", null, null);
    }

    // Header attributes and top-level directives are written bare ('clef treble',
    // 'time 4/4'); the colon form was removed. If a colon is present, flag it as an
    // error but consume it so the rest of the header still parses.
    private SyntaxToken? ConsumeRejectedColon()
    {
        if (!Check(SyntaxKind.Colon))
            return null;
        var span = new TextSpan(_textPosition, Current.FullWidth);
        _diagnostics.Error(span, DiagnosticCodes.LegacyDeclarationForm,
            "Attributes are written bare (e.g. 'clef treble'); the ':' form has been removed.");
        return Advance();
    }

    private SyntaxToken Expect(params SyntaxKind[] kinds)
    {
        if (CheckAny(kinds))
            return Advance();

        // Report error
        var span = new TextSpan(_textPosition, Current.FullWidth);
        var expected = string.Join(" or ", kinds.Select(k => $"'{k}'"));
        _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
            $"Expected {expected}, found '{Current.Kind}'");

        // Error recovery: create a zero-width missing token with NO trivia (see the
        // single-kind Expect above — borrowing Current's trivia double-counts it).
        return new SyntaxToken(kinds[0], "", null, null);
    }

    private SyntaxToken? TryConsume(SyntaxKind kind)
    {
        if (Check(kind))
            return Advance();
        return null;
    }

    /// Parse the entire source into a compilation unit.
    /// </summary>
    public CompilationUnitGreen ParseCompilationUnit()
    {
        var members = new List<GreenNode?>();

        while (!Check(SyntaxKind.EndOfFile))
        {
            // Incremental reparse: adopt an unchanged top-level item from the
            // previous tree wholesale (greens are position-free). Adoption is
            // VERIFIED against the fresh token stream — an edit can change how
            // FOLLOWING text lexes (a new "//" or "/*" rewrites everything up
            // to the line end / comment close), so position math alone is not
            // safe; this mirrors why Roslyn's Blender compares tokens. On any
            // mismatch we fall back to ordinary parsing of that item.
            if (_reuse != null && _reuse.TryGet(_textPosition, out var reused)
                && TryAdoptTokens(reused))
            {
                members.Add(reused);
                continue;
            }

            var member = ParseTopLevelItem();
            if (member != null)
                members.Add(member);
            else
                Advance(); // Skip unexpected token
        }

        var eof = Expect(SyntaxKind.EndOfFile);
        return new CompilationUnitGreen([.. members], eof);
    }

    /// <summary>
    /// Consumes the tokens covered by a candidate node IF the fresh token
    /// stream matches the node's own tokens exactly; otherwise restores the
    /// parser position and reports failure.
    /// </summary>
    private bool TryAdoptTokens(GreenNode node)
    {
        int savePosition = _position;
        int saveTextPosition = _textPosition;

        // Depth-first token walk with an explicit stack (nested iterators are
        // far too slow for a per-edit hot path).
        var stack = new Stack<GreenNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var green = stack.Pop();
            if (green.IsToken)
            {
                if (!TokenMatches(Current, (SyntaxToken)green))
                {
                    _position = savePosition;
                    _textPosition = saveTextPosition;
                    return false;
                }
                Advance();
                continue;
            }
            for (int i = green.SlotCount - 1; i >= 0; i--)
            {
                if (green.GetSlot(i) is { } child)
                    stack.Push(child);
            }
        }

        if (_textPosition != saveTextPosition + node.FullWidth)
        {
            _position = savePosition;
            _textPosition = saveTextPosition;
            return false;
        }

        return true;
    }

    private static bool TokenMatches(SyntaxToken a, SyntaxToken b)
    {
        if (ReferenceEquals(a, b)) // common: the token interning cache hit
            return true;
        return a.Kind == b.Kind
            && a.FullWidth == b.FullWidth
            && a.Text == b.Text
            && TriviaTextEquals(a.LeadingTrivia, b.LeadingTrivia)
            && TriviaTextEquals(a.TrailingTrivia, b.TrailingTrivia);
    }

    private static bool TriviaTextEquals(GreenNode? x, GreenNode? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x == null || y == null)
            return false;
        return x.FullWidth == y.FullWidth && x.ToFullString() == y.ToFullString();
    }

    private GreenNode? ParseTopLevelItem()
    {
        return Current.Kind switch
        {
            // New section-oriented structure
            SyntaxKind.SectionKeyword => ParseSectionDeclaration(),
            SyntaxKind.StructureKeyword => ParseStructureDeclaration(),
            SyntaxKind.IncludeKeyword => ParseIncludeDirective(),
            // `score [ "basename" ] { layout }` — a printable score (visual
            // layout). The output format/extension is a CLI choice, not source.
            SyntaxKind.ScoreKeyword => ParseRenderDeclaration(),
            SyntaxKind.PhraseKeyword => ParsePhraseDeclaration(),
            SyntaxKind.PartKeyword => ParsePartDeclaration(),  // New part syntax

            // Variable declaration: identifier = { ... } (legacy)
            SyntaxKind.Identifier when Peek(1)?.Kind == SyntaxKind.Equals => ParseNewVariableDeclaration(),

            SyntaxKind.LetKeyword => ParseVariableDeclaration(),
            SyntaxKind.UseKeyword or SyntaxKind.Dollar => ParseVariableReference(),

            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword => ParseMetadataDeclaration(),
            SyntaxKind.TimeKeyword => ParseTimeSignature(),
            SyntaxKind.TempoKeyword => ParseTempoDeclaration(),
            SyntaxKind.PartialKeyword => ParsePartialDeclaration(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            SyntaxKind.OctaveKeyword => ParseOctaveDirective(),
            SyntaxKind.TransposeKeyword => ParseTopLevelTranspose(),

            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or
            SyntaxKind.AppogiaturaKeyword => ParseGraceExpression(),

            SyntaxKind.LyricsKeyword => ParseLyricsBlock(),
            SyntaxKind.BreakKeyword => ParseBreak(),
            SyntaxKind.TupletKeyword => ParseTupletExpression(),
            SyntaxKind.OverrideKeyword => ParseOverrideDeclaration(),
            SyntaxKind.RevertKeyword => ParseRevertDeclaration(),
            SyntaxKind.OnceKeyword => ParseOnceModifier(),
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            SyntaxKind.Backslash => ParseLilypondBackslashCommand(topLevel: true),
            _ when IsMusicItemStart() => ParseMusicItem(),
            _ => null
        };
    }

    // ========== Structure Declarations ==========

    private PartDeclarationGreen ParsePartDeclaration()
    {
        var keyword = Expect(SyntaxKind.PartKeyword);
        var name = ExpectPartName();   // names may be clef-name words (bass/treble/...)

        // Check if there's a body
        if (!Check(SyntaxKind.OpenBrace))
        {
            // No body: part name
            return new PartDeclarationGreen(keyword, name);
        }

        // With body: part name { props }
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

            var prop = ParsePartProperty();
            if (prop != null)
                properties.Add(prop);
            else
                Advance();
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new PartDeclarationGreen(keyword, name, openBrace, [.. properties], closeBrace);
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
    /// Parses items with <paramref name="parseItem"/> until <paramref name="close"/>
    /// or EOF. A null result skips one token (the single infinite-loop guard shared
    /// by every brace-delimited list).
    /// </summary>
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

        // clef treble, instrument "Violin", channel 1, tuning standard, transpose d
        if (Current.Kind == SyntaxKind.Identifier ||
            Current.Kind == SyntaxKind.ClefKeyword ||
            Current.Kind == SyntaxKind.InstrumentKeyword ||
            Current.Kind == SyntaxKind.ChannelKeyword ||
            Current.Kind == SyntaxKind.TuningKeyword ||
            Current.Kind == SyntaxKind.OctaveKeyword ||
            Current.Kind == SyntaxKind.TransposeKeyword)
        {
            var propName = Advance();
            // Bare canonical form ('clef treble'); a stray ':' is flagged and skipped.
            var colon = ConsumeRejectedColon();
            var value = Advance(); // identifier, string, number, or pitch
            // A transpose target may carry octave marks (transpose d' / c,);
            // harmless for the other properties, which never have trailing marks.
            var values = new List<GreenNode?> { value };
            while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
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

    private PropertyAssignmentGreen ParsePropertyAssignment()
    {
        var name = Advance(); // keyword like tempo, clef, etc.
        var colon = ConsumeRejectedColon();
        var valueTokens = ParsePropertyValue();
        return new PropertyAssignmentGreen(name, colon, valueTokens);
    }

    private GreenNode?[] ParsePropertyValue()
    {
        var tokens = new List<GreenNode?>();

        // Collect value tokens until we hit a newline, brace, or another property
        while (!Check(SyntaxKind.EndOfFile) &&
               !Check(SyntaxKind.OpenBrace) &&
               !Check(SyntaxKind.CloseBrace) &&
               !IsPropertyStart())
        {
            // Check if current token has trailing newline - stop after consuming it
            var token = Current;
            bool hasNewline = HasTrailingNewline(token);

            tokens.Add(Advance());

            if (hasNewline)
                break;
        }

        return [.. tokens];
    }

    private bool IsPropertyStart()
    {
        return Current.Kind is SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or
            SyntaxKind.KeyKeyword or SyntaxKind.ClefKeyword or
            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword;
    }

    private static bool HasTrailingNewline(SyntaxToken token)
    {
        var trivia = token.TrailingTrivia;
        if (trivia == null) return false;

        if (trivia.Kind == SyntaxKind.EndOfLineTrivia) return true;

        // Check trivia list
        for (int i = 0; i < trivia.SlotCount; i++)
        {
            if (trivia.GetSlot(i)?.Kind == SyntaxKind.EndOfLineTrivia)
                return true;
        }
        return false;
    }

    private MetadataDeclarationGreen ParseMetadataDeclaration()
    {
        var keyword = Advance();
        var valueTokens = new List<GreenNode?>();

        // Collect value tokens (string, number, identifiers)
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

        return new MetadataDeclarationGreen(keyword, [.. valueTokens]);
    }

    // 'time 4/4' is written bare everywhere — as a music-stream command and as a
    // part/staff-header attribute. A stray ':' ('time: 4/4') is flagged and dropped
    // by ConsumeRejectedColon so the rest still parses.
    private TimeSignatureGreen ParseTimeSignature()
    {
        var timeKeyword = Expect(SyntaxKind.TimeKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        var numerator = Expect(SyntaxKind.IntegerLiteral, SyntaxKind.DurationNumber);
        var slash = Expect(SyntaxKind.Slash);
        var denominator = Expect(SyntaxKind.IntegerLiteral, SyntaxKind.DurationNumber);
        return new TimeSignatureGreen(timeKeyword, colon, numerator, slash, denominator);
    }

    // 'tempo 120' is written bare everywhere (music-stream command and part/staff
    // attribute alike); a stray ':' is flagged and dropped by ConsumeRejectedColon.
    private TempoDeclarationGreen ParseTempoDeclaration()
    {
        var tempoKeyword = Expect(SyntaxKind.TempoKeyword);
        SyntaxToken? colon = ConsumeRejectedColon();
        var valueTokens = new List<GreenNode?>();

        // Collect value tokens: "marking" duration = bpm, plus an optional trailing
        // 'swing' / 'shuffle' feel word (kept as a value token; the red node reads it
        // via IsSwing). These are NOT reserved words, so they stay usable as names.
        while (Check(SyntaxKind.StringLiteral) ||
               Check(SyntaxKind.IntegerLiteral) ||
               Check(SyntaxKind.DurationNumber) ||
               // a dotted beat unit: "tempo \"Lively\" 4. = 116" lexes as
               // IntegerLiteral + Dot at declaration level — without accepting
               // the dot the parser stopped there and ". = 116" was dropped.
               Check(SyntaxKind.Dot) ||
               Check(SyntaxKind.Equals) ||
               (Check(SyntaxKind.Identifier) && IsSwingWord(Current.Text)))
        {
            valueTokens.Add(Advance());
        }

        return new TempoDeclarationGreen(tempoKeyword, colon, [.. valueTokens]);
    }

    private static bool IsSwingWord(string text) => text is "swing" or "shuffle";

    // partial <duration> — declares the following measure a pickup (anacrusis)
    // of the given length. The value reuses the note-duration grammar (number +
    // optional dots) so 'partial 4', 'partial 8' and 'partial 2.' all parse.
    private PartialDeclarationGreen ParsePartialDeclaration()
    {
        var partialKeyword = Expect(SyntaxKind.PartialKeyword);
        var number = Expect(SyntaxKind.IntegerLiteral, SyntaxKind.DurationNumber);
        var dots = new List<GreenNode?>();
        while (Check(SyntaxKind.Dot))
            dots.Add(Advance());
        var duration = new DurationGreen(number, [.. dots]);
        return new PartialDeclarationGreen(partialKeyword, duration);
    }

    // ========== Variables ==========

    private VariableDeclarationGreen ParseVariableDeclaration()
    {
        int startPos = _textPosition;
        var letKeyword = Expect(SyntaxKind.LetKeyword);
        var name = Expect(SyntaxKind.Identifier);
        var equals = Expect(SyntaxKind.Equals);
        var expression = ParseMusicExpression();

        // 'let name = …' was removed in favor of 'phrase name { … }'. Reject with a
        // hint and recover by keeping the parsed declaration (so $name still resolves).
        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.LegacyDeclarationForm,
            $"'let {name.Text} = …' is not a Lily# declaration; use 'phrase {name.Text} {{ … }}'.");

        return new VariableDeclarationGreen(letKeyword, name, equals, expression);
    }

    private VariableReferenceGreen ParseVariableReference()
    {
        if (Check(SyntaxKind.UseKeyword))
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            var use = Advance();
            var name = Expect(SyntaxKind.Identifier);
            _diagnostics.Warning(span, DiagnosticCodes.DeprecatedUseKeyword,
                $"Use '${name.Text}' instead of 'use {name.Text}' for variable references");
            return new VariableReferenceGreen(use, name);
        }
        else // $name
        {
            var dollar = Expect(SyntaxKind.Dollar);
            var name = ExpectPartName();   // phrase refs may name clef-name words too
            return new VariableReferenceGreen(dollar, name);
        }
    }


    private VariableReferenceGreen ParseBareVariableReference()
    {
        var span = new TextSpan(_textPosition, Current.FullWidth);
        var name = Advance();
        _diagnostics.Warning(span, DiagnosticCodes.DeprecatedBareReference,
            $"Use '${name.Text}' instead of '{name.Text}' for variable references");
        return new VariableReferenceGreen(name);
    }

    private GreenNode ParseMusicExpression()
    {
        return Current.Kind switch
        {
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            _ => ParseMusicBlock() // fallback
        };
    }


    private MusicBlockGreen ParseMusicBlock()
    {
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var items = new List<GreenNode?>();

        while (_pendingPostEventMarkers.Count > 0
               || (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile)))
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance(); // Skip unexpected token
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new MusicBlockGreen(openBrace, [.. items], closeBrace);
    }

    private bool IsMusicItemStart()
    {
        // Markers pending replay count as music items regardless of Current.
        if (_pendingPostEventMarkers.Count > 0)
            return true;

        return Current.Kind switch
        {
            SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
            SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
            SyntaxKind.PitchB => true,
            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => true,
            SyntaxKind.OpenAngle => true, // Chord
            SyntaxKind.VoiceKeyword => true, // Parallel voices: voice { } voice { }
            SyntaxKind.DoubleOpenAngle => true, // removed << >> — dispatched to a migration hint
            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => true,
            SyntaxKind.Tilde => true,
            SyntaxKind.OpenParen or SyntaxKind.CloseParen => true,
            SyntaxKind.OpenBracket or SyntaxKind.CloseBracket => true,
            SyntaxKind.RepeatKeyword => true,
            SyntaxKind.TupletKeyword => true,
            SyntaxKind.BreakKeyword => true,
            SyntaxKind.PartialKeyword => true,
            SyntaxKind.KeyKeyword => true,
            SyntaxKind.ClefKeyword => true,
            SyntaxKind.OctaveKeyword => true,
            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or SyntaxKind.AppogiaturaKeyword => true,
            SyntaxKind.LyricsKeyword => true,
            SyntaxKind.OverrideKeyword or SyntaxKind.RevertKeyword or SyntaxKind.OnceKeyword => true,
            SyntaxKind.Identifier => true, // Variable reference
            _ => false
        };
    }

    private GreenNode? ParseMusicItem()
    {
        // Replay slur/tie/beam markers that ParsePostEvents consumed out of
        // order (g4(@cresc — the '(' re-enters the sequence here).
        if (_pendingPostEventMarkers.Count > 0)
            return _pendingPostEventMarkers.Dequeue();

        return Current.Kind switch
        {
            SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
            SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
            SyntaxKind.PitchB => ParseNote(),

            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => ParseRest(),

            SyntaxKind.OpenAngle => ParseChord(),

            SyntaxKind.VoiceKeyword => ParseVoiceBlocks(),

            // Removed syntax: report a migration hint, then recover by parsing the
            // old structure so no cascade of errors follows.
            SyntaxKind.DoubleOpenAngle => ParseRemovedParallelExpression(),

            // A leading backslash on a known LilyPond command (\tempo, \new, …) —
            // a habit from LilyPond — gets a hint pointing at the Lily# form.
            SyntaxKind.Backslash => ParseLilypondBackslashCommand(topLevel: false),

            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => ParseBarline(),

            SyntaxKind.Tilde => ParseTie(),

            SyntaxKind.OpenParen or SyntaxKind.CloseParen => ParseSlur(),

            SyntaxKind.OpenBracket => ParseBeamOrInlineVolta(),
            SyntaxKind.CloseBracket => ParseBeamMarker(),

            SyntaxKind.UseKeyword or SyntaxKind.Dollar => ParseVariableReference(),


            SyntaxKind.RepeatKeyword => ParseRepeatExpression(),
            SyntaxKind.TupletKeyword => ParseTupletExpression(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            SyntaxKind.OctaveKeyword => ParseOctaveDirective(),
            SyntaxKind.TimeKeyword => ParseTimeSignature(),
            SyntaxKind.TempoKeyword => ParseTempoDeclaration(),
            SyntaxKind.PartialKeyword => ParsePartialDeclaration(),

            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or
            SyntaxKind.AppogiaturaKeyword => ParseGraceExpression(),

            SyntaxKind.LyricsKeyword => ParseLyricsBlock(),
            SyntaxKind.BreakKeyword => ParseBreak(),
            SyntaxKind.OverrideKeyword => ParseOverrideDeclaration(),
            SyntaxKind.RevertKeyword => ParseRevertDeclaration(),
            SyntaxKind.OnceKeyword => ParseOnceModifier(),

            SyntaxKind.Identifier => ParseBareVariableReference(), // Variable reference without '$' (deprecated)
            _ => null
        };
    }

    // ========== Notes and Pitches ==========

    private bool IsPitchStart()
    {
        return Current.Kind is SyntaxKind.PitchC or SyntaxKind.PitchD or
            SyntaxKind.PitchE or SyntaxKind.PitchF or SyntaxKind.PitchG or
            SyntaxKind.PitchA or SyntaxKind.PitchB;
    }

    private PitchGreen ParsePitch(bool inChord = false)
    {
        var pitchToken = Advance(); // Consume pitch token (c, cis, des, etc.)
        var octaveMarks = new List<GreenNode?>();

        // Collect octave marks: ' or ,
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
        {
            octaveMarks.Add(Advance());
        }

        // LILYPOND-REF: lily/lily-parser.yy — chord_body grammar accepts post-event
        // articulations on each pitch (e.g., <c@finger.1 e@finger.3>). Outside of
        // chord brackets, articulations belong to the surrounding NoteSyntax and
        // are consumed by ParseNote's own ParseArticulations call — we must NOT
        // pre-consume them here or they'd never reach the note.
        if (!inChord)
            return new PitchGreen(pitchToken, [.. octaveMarks]);

        var articulations = ParseArticulations();
        if (articulations.Length == 0)
            return new PitchGreen(pitchToken, [.. octaveMarks]);
        return new PitchGreen(pitchToken, [.. octaveMarks], articulations);
    }

    private DurationGreen? ParseOptionalDuration()
    {
        if (!Check(SyntaxKind.IntegerLiteral))
            return null;

        var number = Advance();
        var dots = new List<GreenNode?>();

        while (Check(SyntaxKind.Dot))
        {
            dots.Add(Advance());
        }

        return new DurationGreen(number, [.. dots]);
    }

    private NoteGreen ParseNote()
    {
        var pitch = ParsePitch();
        var duration = ParseOptionalDuration();
        var tremolo = Check(SyntaxKind.TremoloSuffix) ? Advance() : null;
        var articulations = ParsePostEvents();
        return new NoteGreen(pitch, duration, tremolo, articulations);
    }

    /// <summary>
    /// Queue of slur/tie/beam markers consumed out of order by
    /// <see cref="ParsePostEvents"/>; <see cref="ParseMusicItem"/> replays them
    /// as the following sequence items, preserving source order.
    /// </summary>
    private readonly Queue<GreenNode> _pendingPostEventMarkers = new();

    /// <summary>
    /// Parses a note's/chord's trailing post-events with LilyPond's order-free
    /// semantics: slur parens, ties and beam brackets may interleave with
    /// <c>@</c>-articulations (<c>g4(@cresc</c> ≡ <c>g4@cresc(</c>). Every
    /// articulation belongs to the host note; the markers are replayed into
    /// the music sequence in source order via the pending queue.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lily-parser.yy post_events — an unordered list of
    /// post-events following the note.
    /// </remarks>
    private GreenNode?[] ParsePostEvents()
    {
        var articulations = ParseArticulations();
        if (!PendingMarkerRunHasArticulation())
            return articulations;

        var combined = new List<GreenNode?>(articulations);
        while (PendingMarkerRunHasArticulation())
        {
            _pendingPostEventMarkers.Enqueue(ParsePostEventMarker());
            combined.AddRange(ParseArticulations());
        }
        return [.. combined];
    }

    /// <summary>
    /// True when the upcoming run of post-event markers (<c>( ) ~ [ ]</c>) is
    /// followed by an <c>@</c>-articulation that must attach to the current
    /// note. A bare marker run (no trailing <c>@</c>) takes the normal
    /// sequence-item path untouched.
    /// </summary>
    private bool PendingMarkerRunHasArticulation()
    {
        if (!IsPostEventMarkerKind(Current.Kind))
            return false;
        int k = 1;
        while (IsPostEventMarkerKind(Peek(k).Kind))
            k++;
        return Peek(k).Kind == SyntaxKind.At;
    }

    private static bool IsPostEventMarkerKind(SyntaxKind kind) => kind
        is SyntaxKind.OpenParen or SyntaxKind.CloseParen
        or SyntaxKind.Tilde
        or SyntaxKind.OpenBracket or SyntaxKind.CloseBracket;

    private GreenNode ParsePostEventMarker() => Current.Kind switch
    {
        SyntaxKind.Tilde => ParseTie(),
        SyntaxKind.OpenBracket or SyntaxKind.CloseBracket => ParseBeamMarker(),
        _ => ParseSlur(),
    };

    private RestGreen ParseRest()
    {
        var restToken = Advance();
        var duration = ParseOptionalDuration();

        // LILYPOND-REF: lily/lily-parser.yy — R<dur>*N grammar.
        // Only valid for full-measure rests (R), but we accept the syntax for any
        // rest token and let semantic analysis enforce the constraint if needed.
        SyntaxToken? asterisk = null;
        SyntaxToken? measureCount = null;
        if (Check(SyntaxKind.Asterisk))
        {
            asterisk = Advance();
            if (Check(SyntaxKind.IntegerLiteral))
            {
                measureCount = Advance();
            }
            else
            {
                var span = new TextSpan(_textPosition, Current.FullWidth);
                _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                    $"Expected integer measure-count after '*', found '{Current.Kind}'");
            }
        }

        // Post-events on rests (r4@fermata, r2@coda, ...) — standard notation;
        // LILYPOND-REF: lily/lily-parser.yy — post-events attach to rests
        // (r4\fermata).
        var articulations = ParseArticulations();

        return new RestGreen(restToken, duration, asterisk, measureCount, articulations);
    }

    private ChordGreen ParseChord()
    {
        var openAngle = Expect(SyntaxKind.OpenAngle);
        var pitches = new List<GreenNode?>();

        while (IsPitchStart())
        {
            // LILYPOND-REF: lily/lily-parser.yy chord_body — per-pitch articulations.
            pitches.Add(ParsePitch(inChord: true));
        }

        var closeAngle = Expect(SyntaxKind.CloseAngle);
        var duration = ParseOptionalDuration();
        var tremolo = Check(SyntaxKind.TremoloSuffix) ? Advance() : null;
        var articulations = ParsePostEvents();

        return new ChordGreen(openAngle, [.. pitches], closeAngle, duration, tremolo, articulations);
    }

    private BarlineGreen ParseBarline()
    {
        var barToken = Advance();

        // Optional explicit repeat count on a :| end-repeat barline: ":|*N"
        // (reuses the R1*N multiplier idiom; sets the volta-repeat play count).
        if (barToken.Kind == SyntaxKind.RepeatEndBar && Check(SyntaxKind.Asterisk))
        {
            var asterisk = Advance();
            var count = Expect(SyntaxKind.IntegerLiteral);
            return new BarlineGreen(barToken, asterisk, count);
        }

        return new BarlineGreen(barToken);
    }

    private BreakGreen ParseBreak()
    {
        var breakKeyword = Expect(SyntaxKind.BreakKeyword);
        return new BreakGreen(breakKeyword);
    }

    // ========== Override/Revert ==========

    /// <summary>
    /// Parses: override Grob.property = value
    /// LILYPOND-REF: lily/context-property.cc (push)
    /// </summary>
    private OverrideDeclarationGreen ParseOverrideDeclaration()
    {
        var overrideKeyword = Expect(SyntaxKind.OverrideKeyword);
        var grobName = Expect(SyntaxKind.Identifier);
        var dot = Expect(SyntaxKind.Dot);
        var propertyName = Expect(SyntaxKind.Identifier);
        var equals = Expect(SyntaxKind.Equals);

        // Value: integer (lengths/positions), identifier (symbolic, e.g. up / red),
        // string (e.g. color = "red" — the collector strips the quotes), or a negative
        // integer. The resolver stores the value as a string and reparses it per property
        // (GetInt / GetDouble / GetString / GetBool), so all four forms flow through.
        var value = Current.Kind switch
        {
            SyntaxKind.IntegerLiteral => Advance(),
            SyntaxKind.Identifier => Advance(),
            SyntaxKind.StringLiteral => Advance(),
            SyntaxKind.Minus => CombineNegativeNumber(),
            _ => Expect(SyntaxKind.IntegerLiteral) // error recovery
        };

        return new OverrideDeclarationGreen(overrideKeyword, grobName, dot, propertyName, equals, value);
    }

    /// <summary>
    /// Combines a minus sign with following integer into a negative number token.
    /// </summary>
    private SyntaxToken CombineNegativeNumber()
    {
        var minus = Advance(); // consume -
        if (Current.Kind == SyntaxKind.IntegerLiteral)
        {
            var num = Advance();
            // Keep ALL consumed text — including any whitespace written between the '-'
            // and the digits ("- 5") — in the token's TEXT so its width equals the source
            // span (root.FullWidth == text.Length) and the tree round-trips exactly. The
            // interior trivia has nowhere else to live: a token's trivia is only leading
            // or trailing, never internal. The collector strips this interior whitespace
            // when it reads the numeric value.
            string inner = (minus.TrailingTrivia?.ToFullString() ?? "")
                         + (num.LeadingTrivia?.ToFullString() ?? "");
            return new SyntaxToken(SyntaxKind.IntegerLiteral, "-" + inner + num.Text,
                minus.LeadingTrivia, num.TrailingTrivia);
        }
        // Error: minus not followed by number
        return minus;
    }

    /// <summary>
    /// Parses: revert Grob.property
    /// LILYPOND-REF: lily/context-property.cc (pop)
    /// </summary>
    private RevertDeclarationGreen ParseRevertDeclaration()
    {
        var revertKeyword = Expect(SyntaxKind.RevertKeyword);
        var grobName = Expect(SyntaxKind.Identifier);
        var dot = Expect(SyntaxKind.Dot);
        var propertyName = Expect(SyntaxKind.Identifier);
        return new RevertDeclarationGreen(revertKeyword, grobName, dot, propertyName);
    }

    /// <summary>
    /// Parses: once override/revert ...
    /// LILYPOND-REF: lily/context-property.cc (temporary_override/revert)
    /// </summary>
    private OnceModifierGreen ParseOnceModifier()
    {
        var onceKeyword = Expect(SyntaxKind.OnceKeyword);

        GreenNode command;
        if (Current.Kind == SyntaxKind.OverrideKeyword)
            command = ParseOverrideDeclaration();
        else if (Current.Kind == SyntaxKind.RevertKeyword)
            command = ParseRevertDeclaration();
        else
        {
            // Error: once must be followed by override or revert
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected 'override' or 'revert' after 'once'");
            command = ParseOverrideDeclaration(); // attempt error recovery
        }

        return new OnceModifierGreen(onceKeyword, command);
    }

    private TieGreen ParseTie()
    {
        var tilde = Expect(SyntaxKind.Tilde);
        return new TieGreen(tilde);
    }

    private SlurGreen ParseSlur()
    {
        var paren = Advance();
        return new SlurGreen(paren);
    }

    private BeamMarkerGreen ParseBeamMarker()
    {
        var bracket = Advance();
        return new BeamMarkerGreen(bracket);
    }

    /// <summary>
    /// Disambiguates the overloaded <c>[</c> in a music stream: <c>[</c> followed
    /// by an integer is an inline volta ending (<c>[1. …]</c>); otherwise it is a
    /// beam marker (<c>[c d]</c>). A bare integer never legitimately follows a beam
    /// <c>[</c>, so the lookahead is unambiguous.
    /// </summary>
    private GreenNode ParseBeamOrInlineVolta()
    {
        if (Peek(1).Kind == SyntaxKind.IntegerLiteral)
            return ParseInlineVolta();
        return ParseBeamMarker();
    }

    /// <summary>
    /// Parses an inline volta ending: <c>[1. … ]</c>, <c>[1-2. … ]</c>, or
    /// <c>[1,3. … ]</c>. The body is a sequence of music items up to the closing
    /// <c>]</c>.
    /// </summary>
    private InlineVoltaGreen ParseInlineVolta()
    {
        var openBracket = Expect(SyntaxKind.OpenBracket);
        var number = Expect(SyntaxKind.IntegerLiteral);

        // Optional range/list: [1-2. …] or [1,3. …]
        SyntaxToken? separator = null;
        SyntaxToken? endNumber = null;
        if (Check(SyntaxKind.Minus) || Check(SyntaxKind.Comma))
        {
            separator = Advance();
            endNumber = Expect(SyntaxKind.IntegerLiteral);
        }

        var dot = Expect(SyntaxKind.Dot);

        // The ending body runs until a structural boundary: the closing ']' (which
        // makes the ending CLOSED), the next ending '[N.', a repeat barline, the
        // block close, or EOF. Internal beams '[c8 …]' and plain '|' barlines stay
        // part of the body. Omitting the ']' leaves the ending open on the right.
        var items = new List<GreenNode?>();
        while (_pendingPostEventMarkers.Count > 0 || !AtInlineVoltaBoundary())
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                Advance(); // skip unexpected token to recover
        }

        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;
        return new InlineVoltaGreen(openBracket, number, separator, endNumber, dot, [.. items], closeBracket);
    }

    /// <summary>The token stream is at the end of an inline volta ending body: the
    /// closing ']', the next ending '[N.', a repeat barline, the block close, or EOF.
    /// A '[' that starts a beam (not followed by a number) is NOT a boundary.</summary>
    private bool AtInlineVoltaBoundary() =>
        Check(SyntaxKind.CloseBracket)
        || (Check(SyntaxKind.OpenBracket) && Peek(1).Kind == SyntaxKind.IntegerLiteral)
        || Check(SyntaxKind.RepeatStartBar)
        || Check(SyntaxKind.RepeatEndBar)
        || Check(SyntaxKind.CloseBrace)
        || Check(SyntaxKind.EndOfFile);

private GreenNode?[] ParseArticulations()
    {
        var articulations = new List<GreenNode?>();

        while (true)
        {
            if (Check(SyntaxKind.At))
            {
                // @staccato, @accent, @p, @f, etc.
                var at = Advance();
                if (IsDynamicName())
                {
                    // @p, @f, @mf, @cresc, etc. - dynamics with @ prefix (new style).
                    // An optional '.up' / '.down' forces the dynamic above / below.
                    var name = Advance();
                    if (Current.Kind == SyntaxKind.Dot
                        && IsPlacementWord(Peek(1))
                        && Peek(2)?.Kind != SyntaxKind.Dot)
                    {
                        // cresc/decresc/dim drive a HAIRPIN (always below); '.up' / '.down'
                        // is a dynamic-text placement and is not meaningful there. Flag it
                        // explicitly (no silent drop) and recover as a plain trigger.
                        bool isHairpinTrigger = name.Text is "cresc" or "decresc" or "dim";
                        int qStart = _textPosition;
                        Advance();             // .
                        var dir = Advance();   // up / down
                        if (isHairpinTrigger)
                        {
                            var span = new TextSpan(qStart, System.Math.Max(1, _textPosition - qStart));
                            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                                $"'.{dir.Text}' placement is not supported on '@{name.Text}' (a hairpin is always below the staff).");
                            articulations.Add(new DynamicGreen(at, name));
                        }
                        else
                        {
                            articulations.Add(new DynamicGreen(at, name, dir));
                        }
                    }
                    else
                    {
                        articulations.Add(new DynamicGreen(at, name));
                    }
                }
                else if (IsArticulationName())
                {
                    // @name.up / @name.down — forced PLACEMENT on an articulation
                    // (above / below), recognised before the compound-mark form so the
                    // '.up' / '.down' qualifier is a placement, not a mark part.
                    if (Current.Kind == SyntaxKind.Identifier
                        && Peek(1)?.Kind == SyntaxKind.Dot
                        && IsPlacementWord(Peek(2))
                        && Peek(3)?.Kind != SyntaxKind.Dot)
                    {
                        var name = Advance();
                        Advance();             // .
                        var dir = Advance();   // up / down
                        articulations.Add(new ArticulationGreen(at, name, dir));
                    }
                    // @name(args) — parenthesised arguments, e.g. @fig(6 4), @chord(Dm),
                    // @mark(A), @finger(3), @feather(right), @ped(off). The '.' is reserved
                    // for .up/.down placement (handled above); EVERY annotation argument now
                    // goes in parentheses, separated by whitespace or commas. The arg tokens
                    // are kept on the green node (so the source span is exact) but excluded
                    // from MusicMarkSyntax.MarkName, which still yields "name.arg.arg" so the
                    // downstream collectors (figured bass / chord / fingering / mark) are
                    // unchanged.
                    else if (Current.Kind == SyntaxKind.Identifier && Peek(1)?.Kind == SyntaxKind.OpenParen)
                    {
                        var name = Advance();
                        var parts = new List<SyntaxToken> { at, name, Advance() /* ( */ };
                        while (!Check(SyntaxKind.CloseParen) && !Check(SyntaxKind.EndOfFile))
                            parts.Add(Advance()); // argument token (or a ',' separator)
                        parts.Add(Expect(SyntaxKind.CloseParen));
                        // @text("…").up / .down — placement on the free-text
                        // annotation. Only @text takes it: the other value
                        // annotations have fixed sides, and consuming a '.'
                        // here would corrupt their dotted MarkName forms.
                        if (name.Text.Equals("text", StringComparison.OrdinalIgnoreCase)
                            && Current.Kind == SyntaxKind.Dot
                            && IsPlacementWord(Peek(1))
                            && Peek(2)?.Kind != SyntaxKind.Dot)
                        {
                            parts.Add(Advance()); // .
                            parts.Add(Advance()); // up / down
                        }
                        articulations.Add(new MusicMarkGreen([.. parts]));
                    }
                    else
                    {
                        // @staccato, @accent, @trill, etc. (a bare name; an annotation
                        // argument must use the (…) form above, not a '.').
                        var name = Advance();
                        articulations.Add(new ArticulationGreen(at, name));
                    }
                }
                else
                {
                    // Error: expected articulation or dynamic name after @
                    var span = new TextSpan(_textPosition, Current.FullWidth);
                    _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                        $"Expected articulation or dynamic name after '@', found '{Current.Kind}'");
                }
            }
            else if (Check(SyntaxKind.StringNumber))
            {
                // \4, \3 … — tab string-number annotation on the note (forces the
                // fret's string on a tab staff; ignored on a notation staff).
                var stringNum = Advance();
                articulations.Add(new StringNumberAnnotationGreen(stringNum));
            }
            else if (Check(SyntaxKind.Backslash))
            {
                int startPos = _textPosition;
                var backslash = Advance();
                if (IsDynamicName())
                {
                    // \p, \f, \cresc — a LilyPond habit. Lily# writes annotations with
                    // '@' (e.g. @p); backslash is reserved for tablature (string numbers
                    // like \3, and \tuning). Flag it, then recover by parsing it anyway.
                    var name = Advance();
                    var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
                    _diagnostics.Error(span, DiagnosticCodes.LilypondBackslashCommand,
                        $"Use '@{name.Text}' for annotations; backslash is reserved for tablature (e.g. string numbers like \\3).");
                    articulations.Add(new DynamicGreen(backslash, name));
                }
                else
                {
                    // Not a dynamic - might be other command, stop parsing articulations
                    _position--; // Put backslash back
                    _textPosition -= backslash.FullWidth;
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return [.. articulations];
    }

    // 'up' / 'down' lex as plain identifiers; they are the placement words for the
    // '@name.up' / '@name.down' qualifier.
    private static bool IsPlacementWord(SyntaxToken? token) =>
        token?.Kind == SyntaxKind.Identifier && (token.Text == "up" || token.Text == "down");

    private bool IsArticulationName()
    {
        // Articulation/ornament names are plain identifiers after '@'; the specific
        // name is resolved by ArticulationRegistry, not by the lexer. A few mark
        // names (coda/segno/fine) lex as structure keywords but are equally valid
        // as in-music marks (e.g. g1@coda).
        return Current.Kind is SyntaxKind.Identifier
            or SyntaxKind.CodaKeyword
            or SyntaxKind.SegnoKeyword
            or SyntaxKind.FineKeyword;
    }

    private bool IsDynamicName()
    {
        // Dynamics are recognized by text: the level marks (p, f, mf, …) which the
        // lexer keeps as Dynamic* tokens (or pitch tokens like 'f'), plus cresc /
        // decresc / dim which are now plain identifiers resolved by name downstream.
        var text = Current.Text;
        if (text is "f" or "ff" or "fff" or "p" or "pp" or "ppp" or "mp" or "mf" or
            "sfz" or "sf" or "fp" or "rfz" or "fz" or
            "cresc" or "decresc" or "dim")
        {
            return true;
        }
        return Current.Kind is SyntaxKind.DynamicPPP or SyntaxKind.DynamicPP or
            SyntaxKind.DynamicP or SyntaxKind.DynamicMP or
            SyntaxKind.DynamicMF or SyntaxKind.DynamicF or
            SyntaxKind.DynamicFF or SyntaxKind.DynamicFFF;
    }

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
            repeatType = new SyntaxToken(SyntaxKind.VoltaKeyword, "volta", null, null);
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
    private ParallelExpressionGreen ParseVoiceBlocks()
    {
        var firstVoice = Expect(SyntaxKind.VoiceKeyword);
        var children = new List<GreenNode?>();
        if (Check(SyntaxKind.Identifier)) children.Add(Advance()); // optional voice name
        children.Add(ParseMusicBlock());

        while (Check(SyntaxKind.VoiceKeyword))
        {
            // Keep the separating `voice` keyword in the tree so ToFullString
            // round-trips exactly; Voices skips it (only MusicBlocks are voices).
            children.Add(Advance());
            if (Check(SyntaxKind.Identifier)) children.Add(Advance()); // optional voice name
            children.Add(ParseMusicBlock());
        }

        // ParallelExpression carries an open/close token; voice blocks have no
        // closing delimiter, so reuse the opening `voice` keyword as the open
        // marker and a synthetic empty close. ParallelExpressionSyntax.Voices
        // only reads the MusicBlock children, so the markers are inert.
        var close = new SyntaxToken(SyntaxKind.VoiceKeyword, "", null, null);
        return new ParallelExpressionGreen(firstVoice, [.. children], close);
    }

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
    /// A leading backslash before a well-known LilyPond command (a reflex for
    /// users coming from LilyPond) gets a hint pointing at the Lily# form, then
    /// recovers by parsing the now-bare command. Backslashes that ARE valid
    /// Lily# (\tabStaff, \tuning) or unrecognized ones are left untouched (return
    /// null without consuming, so the caller skips the '\' as before).
    /// </summary>
    private GreenNode? ParseLilypondBackslashCommand(bool topLevel)
    {
        string word = Peek(1).Text;
        string? hint = word switch
        {
            "new" => "Lily# has no '\\new'; declare 'part name { … }' and lay it out with "
                + "'staff { … }' / 'voice { … }'.",
            "relative" => "Lily# is relative by default — drop '\\relative …'; switch modes "
                + "with 'octave absolute'.",
            "addlyrics" => "Lily# writes lyrics as 'lyrics { … }', not '\\addlyrics'.",
            "tempo" or "clef" or "key" or "time" or "transpose" or "octave"
                => $"Lily# commands take no leading backslash — write '{word} …', not '\\{word} …'.",
            _ => null
        };
        if (hint == null)
            return null;

        int startPos = _textPosition;
        Advance(); // consume the leading '\'
        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.LilypondBackslashCommand, hint);

        // A bare directive (\tempo 120 → tempo 120) parses straight away once the
        // backslash is gone — re-dispatch it. The structural commands (\new /
        // \relative / \addlyrics) have no one-token form, so drop their keyword
        // and let the rest fall through to the caller's recovery without a
        // misleading secondary "use $new" warning.
        bool bareDirective = word is "tempo" or "clef" or "key"
            or "time" or "transpose" or "octave";
        if (bareDirective)
            return topLevel ? ParseTopLevelItem() : ParseMusicItem();

        Advance(); // drop the structural command keyword
        return null;
    }

    // ========== Key, Clef, Tuplet ==========

    private KeySignatureGreen ParseKeySignature()
    {
        var keyKeyword = Expect(SyntaxKind.KeyKeyword);
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
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected a mode: 'major', 'minor', 'ionian', 'dorian', 'phrygian', 'lydian', 'mixolydian', 'aeolian' or 'locrian'");
            mode = new SyntaxToken(SyntaxKind.MajorKeyword, "major", null, null);
        }

        return new KeySignatureGreen(keyKeyword, pitch, mode);
    }

    private ClefDeclarationGreen ParseClefDeclaration()
    {
        var clefKeyword = Expect(SyntaxKind.ClefKeyword);

        SyntaxToken clefName;
        if (CheckAny(SyntaxKind.TrebleKeyword, SyntaxKind.BassKeyword,
                     SyntaxKind.AltoKeyword, SyntaxKind.TenorKeyword,
                     SyntaxKind.Treble8Keyword))
        {
            clefName = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected clef name (treble, treble_8, alto, tenor, bass)");
            clefName = new SyntaxToken(SyntaxKind.TrebleKeyword, "treble", null, null);
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
            mode = new SyntaxToken(SyntaxKind.Identifier, "relative", null, null);
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


    // ========== Tablature ==========

    private TuningDeclarationGreen ParseTuningDeclaration()
    {
        var backslash = Expect(SyntaxKind.Backslash);
        var tuningKeyword = Expect(SyntaxKind.TuningKeyword);

        // Tuning name can be Identifier or a keyword like "bass"
        var tuningName = Current.Kind switch
        {
            SyntaxKind.BassKeyword => Advance(),
            SyntaxKind.Identifier => Advance(),
            _ => Expect(SyntaxKind.Identifier) // will produce error
        };

        return new TuningDeclarationGreen(backslash, tuningKeyword, tuningName);
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

        // 'name = { … }' was removed in favor of 'phrase name { … }'. Reject with a
        // hint and recover by keeping the parsed declaration (so $name still resolves).
        var span = new TextSpan(startPos, Math.Max(1, _textPosition - startPos));
        _diagnostics.Error(span, DiagnosticCodes.LegacyDeclarationForm,
            $"'{name.Text} = {{ … }}' is not a Lily# declaration; use 'phrase {name.Text} {{ … }}'.");

        return new VariableDeclarationGreen(name, equals, body);
    }

    /// <summary>
    /// Parse phrase declaration: phrase name { ... }
    /// </summary>
    /// <summary>
    /// Parse an include directive: <c>include "file.lys"</c>. The expander resolves
    /// the file before collection; here it is parsed as an inert top-level marker.
    /// </summary>
    private IncludeDirectiveGreen ParseIncludeDirective()
    {
        var keyword = Expect(SyntaxKind.IncludeKeyword);
        var path = Expect(SyntaxKind.StringLiteral);
        return new IncludeDirectiveGreen(keyword, path);
    }

    private PhraseDeclarationGreen ParsePhraseDeclaration()
    {
        var keyword = Expect(SyntaxKind.PhraseKeyword);
        var name = ExpectPartName();
        var body = ParseMusicBlock();

        return new PhraseDeclarationGreen(keyword, name, body);
    }


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

        var measures = ParseList(SyntaxKind.CloseBrace, ParseLyricMeasure);

        var closeBrace = Expect(SyntaxKind.CloseBrace);

        return new LyricsBlockGreen(keyword, name, openBrace, [.. measures], closeBrace);
    }

    private static bool IsPitchKind(SyntaxKind kind) => kind is SyntaxKind.PitchC
        or SyntaxKind.PitchD or SyntaxKind.PitchE or SyntaxKind.PitchF
        or SyntaxKind.PitchG or SyntaxKind.PitchA or SyntaxKind.PitchB;

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
            if (Check(SyntaxKind.Bar) || Check(SyntaxKind.DoubleBar) || Check(SyntaxKind.FinalBar)
                || Check(SyntaxKind.RepeatStartBar) || Check(SyntaxKind.RepeatEndBar))
                items.Add(ParseBarline());
            else if (IsPitchKind(Current.Kind))
                items.Add(ParseChordEntry());
            else
                Advance(); // error recovery — skip stray tokens
        }

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new ChordPartBlockGreen(keyword, name, openBrace, [.. items], closeBrace);
    }

    private static bool IsQualityToken(SyntaxKind kind) => kind is SyntaxKind.Identifier
        or SyntaxKind.IntegerLiteral or SyntaxKind.DurationNumber
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
            if (IsPitchKind(Current.Kind))
                bass = ParsePitch();
        }

        return new ChordEntryGreen(root, duration, colon, [.. qualityTokens], slash, bass);
    }

    /// <summary>Any barline token that ends a lyric measure.</summary>
    private static bool IsLyricBarline(SyntaxKind kind) => kind is SyntaxKind.Bar
        or SyntaxKind.DoubleBar or SyntaxKind.FinalBar
        or SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar;

    /// <summary>
    /// Parse a single lyric measure: syllable syllable ... |
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/lyric-engraver.cc:90-120 stop_translation_timestep
    /// </remarks>
    private LyricMeasureGreen? ParseLyricMeasure()
    {
        var syllables = new List<GreenNode?>();

        while (!IsLyricBarline(Current.Kind) && !Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
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
            // Create a synthetic barline token
            var syntheticBar = new SyntaxToken(SyntaxKind.Bar, "|");
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
                Advance(); // consume second minus
                // Return as special marker token
                return new LyricSyllableGreen(
                    new SyntaxToken(SyntaxKind.Identifier, "--"));
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
        while (prev.TrailingTriviaWidth == 0
               && Current.LeadingTriviaWidth == 0
               && Current.Kind != SyntaxKind.Bar
               && Current.Kind != SyntaxKind.CloseBrace
               && Current.Kind != SyntaxKind.EndOfFile
               && Current.Kind != SyntaxKind.Minus
               && Current.Kind != SyntaxKind.Tilde
               && Current.Kind != SyntaxKind.Underscore)
        {
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
        var colon = Expect(SyntaxKind.Colon);
        var value = Advance(); // value token
        return new PropertyAssignmentGreen(keyword, colon, [value]);
    }

    /// <summary>
    /// Parse structure declaration: structure { ... }
    /// </summary>
    private StructureDeclarationGreen ParseStructureDeclaration()
    {
        var keyword = Expect(SyntaxKind.StructureKeyword);
        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseStructureItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new StructureDeclarationGreen(keyword, openBrace, [.. items], closeBrace);
    }

    private GreenNode? ParseStructureItem()
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
            SyntaxKind.RepeatStartBar => ParseStructureRepeatBlock(),
            SyntaxKind.OpenBracket => ParseVoltaBracket(),
            SyntaxKind.SegnoKeyword or SyntaxKind.FineKeyword or SyntaxKind.CodaKeyword
                or SyntaxKind.DcKeyword or SyntaxKind.DsKeyword or SyntaxKind.ToKeyword
                => ParseNavigationMark(),
            _ => null
        };
    }

    /// <summary>
    /// Parse silent section reference: ~SectionName
    /// </summary>
    private SilentSectionReferenceGreen ParseSilentSectionReference()
    {
        var tilde = Expect(SyntaxKind.Tilde);
        var name = ExpectPartName();
        return new SilentSectionReferenceGreen(tilde, name);
    }

    /// <summary>
    /// Parse music mark: @segno, @fine, @ds.al.fine, etc.
    /// </summary>
    private MusicMarkGreen ParseMusicMark()
    {
        var at = Expect(SyntaxKind.At);
        var name = ExpectMarkName();

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
    private StructureAlternativeGreen ParseVoltaBracket()
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
        // The ']' is optional: present = closed (right cap drawn), absent = open.
        SyntaxToken? closeBracket = Check(SyntaxKind.CloseBracket) ? Advance() : null;

        return new StructureAlternativeGreen(openBracket, number, separator, endNumber, dot, tilde, section, closeBracket);
    }

    /// <summary>
    /// Parse repeat block: |: ... :| or |: ... :| x3
    /// </summary>
    private StructureRepeatBlockGreen ParseStructureRepeatBlock()
    {
        var startBar = Expect(SyntaxKind.RepeatStartBar);

        var items = new List<GreenNode?>();
        var alternatives = new List<GreenNode?>();
        SyntaxToken? pipeBeforeAlternatives = null;
        int voltaBracketsBeforeClose = 0;

        // Parse items until :| or | (for alternatives)
        while (!Check(SyntaxKind.RepeatEndBar) && !Check(SyntaxKind.EndOfFile))
        {
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

            var item = ParseStructureItem();
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
                alternatives.Add(ParseStructureAlternative());
            }
        }

        var endBar = Expect(SyntaxKind.RepeatEndBar);

        // Final alternative after :| — the bare "2. A2" form or the bracket form
        // "[2. A2]", so a structure repeat reads exactly like the inline volta:
        //   |: Intro2 B C A2 [1. D] :| [2. Outro]
        GreenNode? finalAlternative = null;
        if (Check(SyntaxKind.IntegerLiteral))
            finalAlternative = ParseStructureAlternative();
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

        return new StructureRepeatBlockGreen(startBar, [.. items], pipeBeforeAlternatives, [.. alternatives], endBar, finalAlternative, xToken, repeatCount);

    }

    /// <summary>
    /// Parse structure alternative: 1. SectionName
    /// </summary>
    private StructureAlternativeGreen ParseStructureAlternative()
    {
        var number = Expect(SyntaxKind.IntegerLiteral);
        var dot = Expect(SyntaxKind.Dot);
        var section = Expect(SyntaxKind.Identifier);
        return new StructureAlternativeGreen(number, dot, section);
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

        // "to coda"
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

        // Optional output basename: a single bare token (identifier, pitch letter,
        // number) or a quoted string — quotes only needed for spaces/special
        // characters, like `title`. Anything that is not the opening brace is the
        // name. Extension (if written) is dropped downstream.
        SyntaxToken? filename = Check(SyntaxKind.OpenBrace) || Check(SyntaxKind.TransposeKeyword)
            ? null : Advance();

        // Optional per-score transpose: `score [name] transpose <pitch> { ... }`.
        // Stored as a transpose property (same shape the part header uses).
        GreenNode? transpose = Check(SyntaxKind.TransposeKeyword) ? ParsePartProperty() : null;

        var openBrace = Expect(SyntaxKind.OpenBrace);

        var items = ParseList(SyntaxKind.CloseBrace, ParseRenderItem);

        var closeBrace = Expect(SyntaxKind.CloseBrace);
        // name is always null now (`score` is the keyword, not a name slot).
        return new RenderDeclarationGreen(keyword, null, filename, transpose, openBrace, [.. items], closeBrace);
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

        // Report error
        var span = new TextSpan(_textPosition, Current.FullWidth);
        _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
            $"Expected part name, found '{Current.Kind}'");

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
            SyntaxKind.TabKeyword => ParseTabRender(),
            SyntaxKind.OssiaKeyword => ParseOssiaRender(),
            // A score may carry its own `structure { ... }` to render a different
            // arrangement of the same sections (e.g. a practice excerpt), overriding
            // the top-level structure for that score only.
            SyntaxKind.StructureKeyword => ParseStructureDeclaration(),
            _ when IsPartNameStart() => ParseMidiPartRender(),
            _ => null
        };
    }

    /// <summary>
    /// Parse staff render: staff [clef] { partName }
    /// </summary>
    private StaffRenderGreen ParseStaffRender()
    {
        // staff [clef] part [with chords chordPart]   (no braces)
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.StaffKeyword) };

        // A clef keyword followed by a part name is an override.
        if (IsClefKeyword() && IsPartNameKind(Peek(1)?.Kind))
            tokens.Add(Advance());

        tokens.Add(ExpectPartName());

        // `with chords NAME` attaches a NAMED chord part's symbols above this
        // staff — the same progression can also feed a lead-sheet row, written
        // once (grammar feedback: the nameless/named forms forced duplication).
        if (Check(SyntaxKind.WithKeyword) && Peek(1)?.Kind == SyntaxKind.ChordsKeyword)
        {
            tokens.Add(Advance()); // with
            tokens.Add(Advance()); // chords
            tokens.Add(ExpectPartName());
        }

        return new StaffRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse chord-row render: <c>chords partName</c> (places a chord part as a row).
    /// </summary>
    private ChordRowRenderGreen ParseChordRowRender()
    {
        var tokens = new List<SyntaxToken> { Expect(SyntaxKind.ChordsKeyword) };
        tokens.Add(ExpectPartName());
        return new ChordRowRenderGreen([.. tokens]);
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

    private bool IsClefKeyword()
    {
        return Current.Kind is SyntaxKind.TrebleKeyword
            or SyntaxKind.BassKeyword
            or SyntaxKind.AltoKeyword
            or SyntaxKind.TenorKeyword
            or SyntaxKind.Treble8Keyword;
    }

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
        return new TabRenderGreen([.. tokens]);
    }

    /// <summary>
    /// Parse MIDI part render: partName [channel:N] [instrument:N] [octave:N]
    /// </summary>
    private MidiPartRenderGreen ParseMidiPartRender()
    {
        var partName = ExpectPartName();

        var options = new List<GreenNode?>();
        while (Current.Kind is SyntaxKind.ChannelKeyword
            or SyntaxKind.InstrumentKeyword
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
