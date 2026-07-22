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
internal sealed partial class Parser
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

        // Unknown characters lex to BadToken and no parse rule consumes them —
        // they were dropped in complete silence (a typo like `b??` compiled
        // clean and simply lost the "??"). Flag each one up front.
        //
        // Exception: '#' (a BadToken everywhere, since Lily# deliberately avoids
        // Scheme's '#') is LEGAL inside a @chord(...) / @fig(...) argument, where
        // it means "sharp" — sharp roots (C#/F#), altered tensions (7#9, #11), and
        // sharp figures (#6). It flows through MusicMarkSyntax.MarkName to the
        // chord / figured-bass parsers. Track those argument regions so a '#'
        // there is not flagged; every other BadToken (and '#' anywhere else) is.
        int scanPos = 0;
        int argDepth = 0;  // paren depth inside a @chord/@fig argument (0 = outside)
        int stage = 0;     // 0 = idle, 1 = saw '@', 2 = saw '@chord'/'@fig' name
        SyntaxToken? previous = null;
        bool afterNote = false;  // the previous token was the tail of a note
        foreach (var t in _tokens)
        {
            // GLUED = nothing at all between this token and the previous one. The
            // lexer hands an inter-token space run to the PREVIOUS token as
            // TRAILING trivia, so both sides have to be checked — the same test
            // CurrentGluedToPrevious makes for the parse rules.
            bool glued = previous is not null
                && previous.TrailingTriviaWidth == 0 && t.LeadingTriviaWidth == 0;

            // Start of the token's own text. FullWidth - Text.Length is NOT this
            // offset: it is leading PLUS trailing trivia, so a flagged token that
            // happens to be followed by a space was reported one column too far
            // right (`d? e` pointed at the space, not the '?').
            int inkPos = scanPos + t.LeadingTriviaWidth;

            bool inChordFigArg = argDepth > 0;
            if (t.Kind == SyntaxKind.BadToken && !(inChordFigArg && t.Text == "#"))
            {
                // '?' is LilyPond's cautionary accidental; Lily# has no such
                // shorthand, so point at the annotation form instead of a bare
                // "unexpected character".
                string message = t.Text == "?"
                    ? "Unexpected '?' — Lily# has no LilyPond-style cautionary accidental; "
                      + "write @courtesy (cautionary) or @editorial after the note."
                    : $"Unexpected character '{t.Text}' — it has no meaning here and is ignored";
                _diagnostics.Error(
                    new TextSpan(inkPos, t.Text.Length),
                    DiagnosticCodes.UnexpectedCharacter,
                    message);
            }

            // '!' is the DASHED BARLINE (LilyPond's \bar "!"), so `cis!` closes the
            // measure right there. In LilyPond '!' on a note is the forced
            // accidental, so that is what a LilyPond author means by it — and what
            // came back was a bar-length complaint about a measure they never wrote,
            // with the '!' mentioned nowhere. Silently doing the wrong thing is worse
            // than erroring, so name it.
            //
            // The '!' keeps its meaning: this is a diagnostic ONLY. That is exactly
            // why keying it on adjacency is safe here, where making adjacency CHANGE
            // the meaning was rejected (3e4188b) — spacing decides nothing, it only
            // decides whether we say something.
            if (t.Kind == SyntaxKind.DashedBar && glued && afterNote)
            {
                _diagnostics.Warning(
                    new TextSpan(inkPos, t.Text.Length),
                    DiagnosticCodes.DashedBarGluedToNote,
                    "This '!' is a dashed barline (LilyPond's \\bar \"!\"), so the measure "
                    + "ends here. Lily# has no forced-accidental shorthand: write "
                    + "@courtesy after the note for a parenthesized accidental, or "
                    + "@editorial for a small one above the head. If a dashed barline is "
                    + "what you meant, put a space before it and this warning goes away.");
            }

            // Is `t` the tail of a note, i.e. could a '!' glued AFTER it have been
            // meant as an accidental? A note is a pitch token (the accidental is
            // part of it: `cis` is one token) followed by octave marks, a duration
            // and dots — each GLUED to the one before, which is what makes them
            // part of the note rather than separate music. Anything else — a
            // barline, a bracket, the start of a line — resets it, so `| !` and a
            // '!' opening a line say nothing.
            afterNote =
                SyntaxFacts.IsPitchKind(t.Kind)
                || (afterNote && glued && t.Kind is SyntaxKind.Apostrophe
                    or SyntaxKind.Comma or SyntaxKind.IntegerLiteral or SyntaxKind.Dot);

            if (inChordFigArg)
            {
                if (t.Kind == SyntaxKind.OpenParen) argDepth++;
                else if (t.Kind == SyntaxKind.CloseParen) argDepth--;
            }
            else if (t.Kind == SyntaxKind.At)
                stage = 1;
            else if (stage == 1 && t.Kind == SyntaxKind.Identifier
                     && (t.Text.Equals("chord", StringComparison.OrdinalIgnoreCase)
                         || t.Text.Equals("fig", StringComparison.OrdinalIgnoreCase)))
                stage = 2;
            else if (stage == 2 && t.Kind == SyntaxKind.OpenParen)
            {
                argDepth = 1;
                stage = 0;
            }
            else
                stage = 0;

            scanPos += t.FullWidth;
            previous = t;
        }
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

    /// <summary>True when <see cref="Current"/> is GLUED to the previous token —
    /// no whitespace or comment between them. Adjacency carries meaning in music:
    /// a duration belongs to what it touches (<c>c4</c>, <c>&lt;c e g&gt;4</c>),
    /// while a spaced number is a scale degree inside brackets and nothing outside.
    /// (The lexer attaches an end-of-line comment/space run as the PREVIOUS
    /// token's trailing trivia, so both sides are checked.)</summary>
    private bool CurrentGluedToPrevious =>
        Current.LeadingTriviaWidth == 0
        && _position > 0 && _tokens[_position - 1].TrailingTriviaWidth == 0;

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

    /// <summary>
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
            //
            // Only when the parser is in its DEFAULT state: a top-level note whose
            // post-events queued markers (`c4( @staccato`) leaves
            // _pendingPostEventMarkers non-empty, and those must be REPLAYED as the
            // next items (a full parse does) before any adoption. Adoption advances
            // tokens without running ParseMusicItem, so it would strand them. The
            // music-block loops already drain the queue first in their while-condition;
            // do the same here so the top level is not the one place that skips it.
            if (_pendingPostEventMarkers.Count == 0
                && _reuse != null && _reuse.TryGet(_textPosition, out var reused)
                && TryAdoptTokens(reused))
            {
                members.Add(reused);
                continue;
            }

            var member = ParseTopLevelItem();
            if (member != null)
                members.Add(member);
            else
            {
                WarnIfClefNameUsedAsStaff();
                Advance(); // Skip unexpected token
            }
        }

        var eof = Expect(SyntaxKind.EndOfFile);
        return new CompilationUnitGreen([.. members], eof);
    }

    /// <summary>
    /// A newcomer reaching for a grand staff often writes the intuitive
    /// <c>treble { … } bass { … }</c>, treating a clef name like a staff block.
    /// Bare clef names aren't top-level items, so they get skipped silently and
    /// the <c>{ … }</c> blocks collapse onto one default-clef staff with no
    /// diagnostic. Catch the giveaway — a clef name immediately followed by a
    /// music block at the top level — and point at the real grand-staff form.
    /// Runs only on the skip path, so a legitimate <c>clef treble</c> (consumed
    /// by ParseClefDeclaration) never reaches here.
    /// </summary>
    private void WarnIfClefNameUsedAsStaff()
    {
        if (SyntaxFacts.IsClefKeyword(Current.Kind)
            && Peek(1)?.Kind == SyntaxKind.OpenBrace)
        {
            var name = Current.Text;
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Warning(span, DiagnosticCodes.ClefNameAsStaff,
                $"'{name}' is not a staff here. For multiple staves, put each part on its own " +
                $"staff in a grand staff -- score \"...\" {{ grandStaff {{ staff ... staff ... }} }} -- " +
                $"and set a part's clef with 'clef {name}'.");
        }
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

        // The node's own tokens match, but a top-level production can GROW to the
        // right: a note/chord/rest absorbs a trailing `@`-articulation or post-event
        // marker (`( ) ~ [ ]`) as a post-event. If the edit left one of those glued
        // to the node's end, a fresh parse would fold it in, but adoption consumed
        // only the node's tokens and would strand it as an orphaned item — the tree
        // then loses that width (a data-pos shift with identical geometry). Reject the
        // adoption and re-parse when the following token could still extend the node.
        // (Symmetric to the item-immediately-before-damage rule in IncrementalReuseMap:
        // both guard against a top-level item greedily consuming following tokens.)
        if (Current.Kind == SyntaxKind.At || IsPostEventMarkerKind(Current.Kind))
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
            SyntaxKind.FormKeyword => ParseFormDeclaration(),
            SyntaxKind.UsingKeyword => ParseUsingDirective(),
            // `score [ "basename" ] { layout }` — a printable score (visual
            // layout). The output format/extension is a CLI choice, not source.
            SyntaxKind.ScoreKeyword => ParseRenderDeclaration(),
            SyntaxKind.PhraseKeyword => ParsePhraseDeclaration(),
            SyntaxKind.PartKeyword => ParsePartDeclaration(),  // New part syntax
            // A top-level chord track — the part-major dual of an in-section chords
            // block: `chords name { section A { c1 } section B { c1 } }`.
            SyntaxKind.ChordsKeyword => ParseChordPartBlock(),
            SyntaxKind.DrummapKeyword => ParseDrummapDeclaration(),

            // Optional language-version directive: `version 1`.
            SyntaxKind.VersionKeyword => ParseVersionDeclaration(),

            // Variable declaration: identifier = { ... } (legacy)
            SyntaxKind.Identifier when Peek(1)?.Kind == SyntaxKind.Equals => ParseNewVariableDeclaration(),

            SyntaxKind.Dollar => ParseVariableReference(),

            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword => ParseMetadataDeclaration(),
            SyntaxKind.FontKeyword => ParseFontDeclaration(),
            SyntaxKind.TimeKeyword => ParseTimeSignature(),
            SyntaxKind.TempoKeyword => ParseTempoDeclaration(),
            SyntaxKind.PartialKeyword => ParsePartialDeclaration(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            SyntaxKind.OctaveKeyword => ParseOctaveDirective(),
            SyntaxKind.TransposeKeyword => ParseTopLevelTranspose(),

            SyntaxKind.GraceKeyword or SyntaxKind.AcciaccaturaKeyword or
            SyntaxKind.AppoggiaturaKeyword => ParseGraceExpression(),

            SyntaxKind.LyricsKeyword => ParseLyricsBlock(),
            SyntaxKind.BreakKeyword or SyntaxKind.NoBreakKeyword => ParseBreak(),
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
}
