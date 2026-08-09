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
/// Lexer that tokenizes LilySharp source code.
/// </summary>
internal sealed class Lexer
{
    private readonly string _text;
    private int _position;

    public Lexer(string text)
        : this(text, 0)
    {
    }

    /// <summary>
    /// Starts lexing at an arbitrary offset. The lexer carries no state other
    /// than its position, so lexing from any character offset is well-defined
    /// (incremental lexing re-lexes only the damaged region this way).
    /// </summary>
    public Lexer(string text, int start)
    {
        _text = text;
        _position = start;
    }

    /// <summary>The current character offset of the lexer.</summary>
    public int Position => _position;

    private char Current => _position < _text.Length ? _text[_position] : '\0';
    private char Peek(int offset = 1) => _position + offset < _text.Length ? _text[_position + offset] : '\0';
    private bool IsAtEnd => _position >= _text.Length;

    /// <summary>
    /// Scans all tokens from the source.
    /// </summary>
    public IEnumerable<SyntaxToken> ScanAllTokens()
    {
        while (!IsAtEnd)
        {
            yield return ScanToken();
        }
        yield return new SyntaxToken(SyntaxKind.EndOfFile, "");
    }

    /// <summary>
    /// Scans the next token.
    /// </summary>
    public SyntaxToken ScanToken()
    {
        // Scan leading trivia
        var leadingTrivia = ScanTrivia(leading: true);

        // Scan the token itself
        var (kind, text) = ScanTokenCore();

        // Scan trailing trivia (until end of line)
        var trailingTrivia = ScanTrivia(leading: false);

        return GreenCache.GetToken(kind, text, leadingTrivia, trailingTrivia);
    }

    private GreenNode? ScanTrivia(bool leading)
    {
        var triviaList = new List<GreenNode>();

        while (!IsAtEnd)
        {
            switch (Current)
            {
                case ' ':
                case '\t':
                    triviaList.Add(ScanWhitespace());
                    break;

                case '\r':
                case '\n':
                    triviaList.Add(ScanEndOfLine());
                    if (!leading) goto done; // trailing trivia stops at end of line
                    break;

                case '/' when Peek() == '/':
                    triviaList.Add(ScanLineComment());
                    break;

                case '/' when Peek() == '*':
                    triviaList.Add(ScanBlockComment());
                    break;

                default:
                    goto done;
            }
        }

        done:
        return triviaList.Count switch
        {
            0 => null,
            1 => triviaList[0],
            _ => new SyntaxTriviaList([.. triviaList])
        };
    }

    private SyntaxTrivia ScanWhitespace()
    {
        int start = _position;
        while (Current is ' ' or '\t')
            _position++;
        return GreenCache.GetTrivia(SyntaxKind.WhitespaceTrivia, _text.AsSpan(start, _position - start));
    }

    private SyntaxTrivia ScanEndOfLine()
    {
        int start = _position;
        if (Current == '\r')
        {
            _position++;
            if (Current == '\n')
                _position++;
        }
        else if (Current == '\n')
        {
            _position++;
        }
        return GreenCache.GetTrivia(SyntaxKind.EndOfLineTrivia, _text.AsSpan(start, _position - start));
    }

    private SyntaxTrivia ScanLineComment()
    {
        int start = _position;
        _position += 2; // skip //
        while (!IsAtEnd && Current != '\r' && Current != '\n')
            _position++;
        return GreenCache.GetTrivia(SyntaxKind.LineCommentTrivia, _text.AsSpan(start, _position - start));
    }

    private SyntaxTrivia ScanBlockComment()
    {
        int start = _position;
        _position += 2; // skip /*
        while (!IsAtEnd)
        {
            if (Current == '*' && Peek() == '/')
            {
                _position += 2;
                break;
            }
            _position++;
        }
        // An unterminated block comment is detected post-lex (LexicalDiagnostics),
        // not here, so the lexer stays stateless for incremental re-lexing.
        return GreenCache.GetTrivia(SyntaxKind.BlockCommentTrivia, _text.AsSpan(start, _position - start));
    }

    private (SyntaxKind kind, string text) ScanTokenCore()
    {
        if (IsAtEnd)
            return (SyntaxKind.EndOfFile, "");

        int start = _position;

        // Single character tokens
        switch (Current)
        {
            case '{': _position++; return (SyntaxKind.OpenBrace, "{");
            case '}': _position++; return (SyntaxKind.CloseBrace, "}");
            case '(': _position++; return (SyntaxKind.OpenParen, "(");
            case ')': _position++; return (SyntaxKind.CloseParen, ")");
            case '[': _position++; return (SyntaxKind.OpenBracket, "[");
            case ']': _position++; return (SyntaxKind.CloseBracket, "]");
            case '~': _position++; return (SyntaxKind.Tilde, "~");
            case '+': _position++; return (SyntaxKind.Plus, "+");
            case '!': _position++; return (SyntaxKind.DashedBar, "!"); // dashed barline (LP \bar "!")
            case '*': _position++; return (SyntaxKind.Asterisk, "*");
            case '=': _position++; return (SyntaxKind.Equals, "=");
            case '@': _position++; return (SyntaxKind.At, "@");
            case '$': _position++; return (SyntaxKind.Dollar, "$");
            case '\'': _position++; return (SyntaxKind.Apostrophe, "'");
            case ',': _position++; return (SyntaxKind.Comma, ",");
            case '-': _position++; return (SyntaxKind.Minus, "-");
            case '.': _position++; return (SyntaxKind.Dot, ".");
            case '\\':
                if (Peek(1) >= '1' && Peek(1) <= '9')
                {
                    _position++; // skip \
                    var stringNum = Current.ToString();
                    _position++;
                    // The token text MUST cover both consumed characters ("\4",
                    // not "4"): the token's width is derived from its text, so a
                    // 1-char text under a 2-char span drifts every following
                    // node's source position by 1 (breaks editor<->preview sync).
                    return (SyntaxKind.StringNumber, "\\" + stringNum);
                }
                _position++;
                return (SyntaxKind.Backslash, "\\");
            case '/': _position++; return (SyntaxKind.Slash, "/");
        }

        // Colon: : or :| or :8/:16/:32 (tremolo)
        if (Current == ':')
        {
            _position++;
            if (Current == '|')
            {
                _position++;
                // ':|:' — a back-to-back repeat (end then start). Written with one
                // shared '|', so it is a single token, not ':|' + '|:'.
                if (Current == ':') { _position++; return (SyntaxKind.RepeatBothBar, ":|:"); }
                return (SyntaxKind.RepeatEndBar, ":|");
            }

            // Check for tremolo suffix (:N where N is a power-of-two note value
            // ≥ 8, e.g. :8/:16/:32/:64/:128). LilyPond's tremolo_type accepts any
            // make_duration value (a power of two); beams = log2(N) − 2, so N ≥ 8
            // is the range that draws at least one slash.
            // LILYPOND-REF: lily/parser.yy tremolo_type.
            if (char.IsDigit(Current))
            {
                int numStart = _position;
                while (_position < _text.Length && char.IsDigit(_text[_position]))
                    _position++;
                string numText = _text.Substring(numStart, _position - numStart);

                if (int.TryParse(numText, out int tremoloValue)
                    && tremoloValue >= 8 && (tremoloValue & (tremoloValue - 1)) == 0)
                {
                    return (SyntaxKind.TremoloSuffix, ":" + numText);
                }

                // Not a valid tremolo - backtrack and return just the colon
                _position = numStart;
            }

            return (SyntaxKind.Colon, ":");
        }

        // Multi-character tokens
        // << and >>
        if (Current == '<')
        {
            _position++;
            if (Current == '<') { _position++; return (SyntaxKind.DoubleOpenAngle, "<<"); }
            return (SyntaxKind.OpenAngle, "<");
        }
        if (Current == '>')
        {
            _position++;
            if (Current == '>') { _position++; return (SyntaxKind.DoubleCloseAngle, ">>"); }
            return (SyntaxKind.CloseAngle, ">");
        }

        // Barlines: |, ||, |., |:, |/
        if (Current == '|')
        {
            _position++;
            if (Current == '|') { _position++; return (SyntaxKind.DoubleBar, "||"); }
            if (Current == '.') { _position++; return (SyntaxKind.FinalBar, "|."); }
            if (Current == ':') { _position++; return (SyntaxKind.RepeatStartBar, "|:"); }
            // |/ removed - use 'break' keyword instead
            return (SyntaxKind.Bar, "|");
        }

        // String literal
        if (Current == '"')
        {
            return ScanStringLiteral();
        }

        // Number
        if (char.IsDigit(Current))
        {
            return ScanNumber();
        }

        // Underscore followed by string: _"text" (custom text in structure)
        if (Current == '_' && Peek() == '"')
        {
            _position++;
            return (SyntaxKind.Underscore, "_");
        }

        // Identifier or keyword or pitch
        if (char.IsLetter(Current) || Current == '_')
        {
            return ScanIdentifierOrKeyword();
        }

        // Unknown character - bad token
        _position++;
        return (SyntaxKind.BadToken, _text[start.._position]);
    }

    private (SyntaxKind kind, string text) ScanStringLiteral()
    {
        int start = _position;
        _position++; // skip opening quote

        while (!IsAtEnd && Current != '"')
        {
            if (Current == '\\')
            {
                _position++; // skip the backslash
                if (IsAtEnd)
                    break; // trailing backslash at EOF: no escaped char to consume
            }
            _position++; // consume the (escaped or normal) character
        }

        if (Current == '"')
            _position++; // skip closing quote
        // An unterminated string (no closing quote) is flagged post-lex by
        // LexicalDiagnostics, keeping the lexer stateless.

        return (SyntaxKind.StringLiteral, _text[start.._position]);
    }

    private (SyntaxKind kind, string text) ScanNumber()
    {
        int start = _position;
        while (char.IsDigit(Current))
            _position++;
        // A scale-degree accidental glued to the number (chord degree <d 3is 5es>):
        // capture it into one ScaleDegree token so `3es` stays atomic instead of
        // splitting into 3 + the `es` pitch. Only when IMMEDIATELY adjacent (no
        // space) and not the head of a longer word, so `3 es`, durations, tuplet
        // numbers, etc. are untouched.
        int accLen = GluedDegreeAccidentalLength();
        if (accLen > 0)
        {
            _position += accLen;
            return (SyntaxKind.ScaleDegree, _text[start.._position]);
        }
        // An ottava suffix glued to the digits (8va / 8vb / 15ma / 15mb) — the value
        // of a `transposition` property. Kept as ONE Identifier token so the generic
        // property parser captures "8vb" whole; the collector maps it to semitones.
        int ottLen = GluedOttavaSuffixLength(start);
        if (ottLen > 0)
        {
            _position += ottLen;
            return (SyntaxKind.Identifier, _text[start.._position]);
        }
        return (SyntaxKind.IntegerLiteral, _text[start.._position]);
    }

    // Length of an accidental suffix (isis/eses/is/es, longest first) sitting
    // immediately after the digits and NOT followed by another letter — else 0.
    private int GluedDegreeAccidentalLength()
    {
        foreach (var suffix in DegreeAccidentalSuffixes)
        {
            int end = _position + suffix.Length;
            if (end <= _text.Length
                && _text.AsSpan(_position, suffix.Length).SequenceEqual(suffix)
                && (end == _text.Length || !char.IsLetter(_text[end])))
                return suffix.Length;
        }
        return 0;
    }

    private static readonly string[] DegreeAccidentalSuffixes = { "isis", "eses", "is", "es" };

    // Length of an ottava suffix (va/vb/ma/mb) sitting immediately after the digits
    // and NOT followed by another letter — else 0. Only the octave/double-octave
    // numbers 8 and 15 take it, so an ordinary duration/tuplet number is untouched.
    private int GluedOttavaSuffixLength(int numStart)
    {
        string num = _text[numStart.._position];
        if (num is not ("8" or "15"))
            return 0;
        foreach (var suffix in OttavaSuffixes)
        {
            int end = _position + suffix.Length;
            if (end <= _text.Length
                && _text.AsSpan(_position, suffix.Length).SequenceEqual(suffix)
                && (end == _text.Length || !char.IsLetter(_text[end])))
                return suffix.Length;
        }
        return 0;
    }

    private static readonly string[] OttavaSuffixes = { "va", "vb", "ma", "mb" };

    private (SyntaxKind kind, string text) ScanIdentifierOrKeyword()
    {
        int start = _position;
        char first = Current;

        // Check for pitch name (a-g) or rest (r, s, R)
        if (first is >= 'a' and <= 'g' || first is 'r' or 's' or 'R')
        {
            return ScanPitchOrRestOrIdentifier(start);
        }

        // Regular identifier/keyword - read all alphanumeric
        while (char.IsLetterOrDigit(Current) || Current == '_')
            _position++;

        string text = TrimDrumDuration(start, _text[start.._position]);
        // "treble^8" — the ^ stops the word scan, so the up-octave clef name
        // is stitched here (mirrors treble_8, whose _ is a word character).
        if (text == "treble" && Current == '^' && Peek(1) == '8')
        {
            _position += 2;
            return (SyntaxKind.Treble8UpKeyword, "treble^8");
        }
        return (GetKeywordKind(text), text);
    }

    /// <summary>
    /// "hh8" scans as one word, but a drum name takes its duration like a
    /// pitch does — split the glued digits back off so the parser sees
    /// name + duration tokens. The chord repetition <c>q</c> takes a glued
    /// duration the same way (<c>q4</c> = q + 4). Identifiers whose letter
    /// prefix is neither (v2, tomX3) are untouched.
    /// </summary>
    private string TrimDrumDuration(int start, string text)
    {
        int i = text.Length;
        while (i > 0 && char.IsDigit(text[i - 1])) i--;
        if (i == 0 || i == text.Length) return text;
        string prefix = text[..i];
        if (prefix != "q" && !Syntax.DrumNameRegistry.Contains(prefix)) return text;
        _position = start + i;
        return prefix;
    }

    private (SyntaxKind kind, string text) ScanPitchOrRestOrIdentifier(int start)
    {
        char first = Current;
        _position++;

        // Rest: single character r, s, R (not followed by letter)
        if (first is 'r' or 's' or 'R' && !char.IsLetter(Current))
        {
            var kind = first switch
            {
                'r' => SyntaxKind.RestR,
                's' => SyntaxKind.RestS,
                'R' => SyntaxKind.RestR_Full,
                _ => SyntaxKind.Identifier
            };
            return (kind, first.ToString());
        }

        // Pitch: a-g possibly followed by accidentals (is, es, isis, eses)
        if (first is >= 'a' and <= 'g')
        {
            // Try to match accidentals
            int accidentalStart = _position;
            if (TryMatchAccidental())
            {
                // Matched accidental - return pitch
                string pitchText = _text[start.._position];
                return (GetPitchKind(pitchText), pitchText);
            }

            // No accidental - if next is not a letter, it's a single-char pitch
            if (!char.IsLetter(Current))
            {
                return (GetPitchKind(first.ToString()), first.ToString());
            }

            // Dutch contractions: `es` = ees (E flat), `as` = aes (A flat) — the
            // bare letter e/a plus a trailing 's' that is not part of a longer
            // word. LilyPond accepts these alongside ees/aes; PitchSyntax.PitchName
            // normalizes the token to its canonical spelling for decoding.
            if (first is 'e' or 'a' && Current == 's' && !char.IsLetter(Peek(1)))
            {
                _position++; // consume the 's'
                string contracted = _text[start.._position];
                return (GetPitchKind(contracted), contracted);
            }

            // Next is letter but not accidental - continue as identifier
            _position = accidentalStart;
        }

        // Fall through to regular identifier
        while (char.IsLetterOrDigit(Current) || Current == '_')
            _position++;

        string text = TrimDrumDuration(start, _text[start.._position]);
        return (GetKeywordKind(text), text);
    }

    private bool TryMatchAccidental()
    {
        // Try to match: isis, eses, is, es
        // Must not be followed by a letter (to avoid matching "issue" as "is" + "sue")

        int savedPos = _position;

        // isis / eses (4 chars) - check Peek(4) for char after accidental
        if (MatchesAt("isis") && !char.IsLetter(Peek(4)))
        {
            _position += 4;
            return true;
        }
        if (MatchesAt("eses") && !char.IsLetter(Peek(4)))
        {
            _position += 4;
            return true;
        }

        // Three-quarter-tone accidentals (4 chars): isih (¾♯), eseh (¾♭).
        // LILYPOND-REF: Note names — quarter-tone suffixes ih / eh.
        if (MatchesAt("isih") && !char.IsLetter(Peek(4)))
        {
            _position += 4;
            return true;
        }
        if (MatchesAt("eseh") && !char.IsLetter(Peek(4)))
        {
            _position += 4;
            return true;
        }

        // is / es (2 chars) - check Peek(2) for char after accidental
        if (MatchesAt("is") && !char.IsLetter(Peek(2)))
        {
            _position += 2;
            return true;
        }
        if (MatchesAt("es") && !char.IsLetter(Peek(2)))
        {
            _position += 2;
            return true;
        }

        // Quarter-tone accidentals: ih (¼♯), eh (¼♭).
        if (MatchesAt("ih") && !char.IsLetter(Peek(2)))
        {
            _position += 2;
            return true;
        }
        if (MatchesAt("eh") && !char.IsLetter(Peek(2)))
        {
            _position += 2;
            return true;
        }

        _position = savedPos;
        return false;
    }

    private bool MatchesAt(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (Peek(i) != s[i])
                return false;
        }
        return true;
    }

    private static SyntaxKind GetKeywordKind(string text)
    {
        return text switch
        {
            // New structure keywords
            "section" => SyntaxKind.SectionKeyword,
            "form" => SyntaxKind.FormKeyword,   // piece-level form (play/render order); node kind stays "Structure" internally
            "using" => SyntaxKind.UsingKeyword,
            "tab" => SyntaxKind.TabKeyword,
            "ossia" => SyntaxKind.OssiaKeyword,

            // Part options
            "transpose" => SyntaxKind.TransposeKeyword,
            "octave" => SyntaxKind.OctaveKeyword,
            "instrument" => SyntaxKind.InstrumentKeyword,


            // Navigation keywords (structure block)
            "segno" => SyntaxKind.SegnoKeyword,
            "fine" => SyntaxKind.FineKeyword,
            "coda" => SyntaxKind.CodaKeyword,
            "dc" => SyntaxKind.DcKeyword,
            "ds" => SyntaxKind.DsKeyword,
            "al" => SyntaxKind.AlKeyword,
            "to" => SyntaxKind.ToKeyword,
            // "tocoda" (one word) is accepted alongside "to coda" (two words) —
            // LP charts write "To Coda" and the run-together spelling is a common
            // reflex. Both lex to ToKeyword; ParseNavigationMark handles the one-
            // word form (no trailing 'coda' to consume).
            "tocoda" => SyntaxKind.ToKeyword,

            // Legacy structure keywords
            "score" => SyntaxKind.ScoreKeyword,
            "part" => SyntaxKind.PartKeyword,
            "staff" => SyntaxKind.StaffKeyword,
            "grandStaff" => SyntaxKind.GrandStaffKeyword,
            "staffGroup" => SyntaxKind.StaffGroupKeyword,
            "choirStaff" => SyntaxKind.ChoirStaffKeyword,
            "condensedStaff" => SyntaxKind.CondensedStaffKeyword,
            "voice" => SyntaxKind.VoiceKeyword,
            "phrase" => SyntaxKind.PhraseKeyword,
            "repeat" => SyntaxKind.RepeatKeyword,
            "volta" => SyntaxKind.VoltaKeyword,
            "alternative" => SyntaxKind.AlternativeKeyword,
            "with" => SyntaxKind.WithKeyword,

            // Metadata keywords
            "title" => SyntaxKind.TitleKeyword,
            "composer" => SyntaxKind.ComposerKeyword,
            "font" => SyntaxKind.FontKeyword,
            "embedded" => SyntaxKind.EmbeddedKeyword,
            "tempo" => SyntaxKind.TempoKeyword,
            "time" => SyntaxKind.TimeKeyword,
            "key" => SyntaxKind.KeyKeyword,
            "clef" => SyntaxKind.ClefKeyword,

            // Mode keywords
            "major" => SyntaxKind.MajorKeyword,
            "minor" => SyntaxKind.MinorKeyword,
            "ionian" => SyntaxKind.IonianKeyword,
            "dorian" => SyntaxKind.DorianKeyword,
            "phrygian" => SyntaxKind.PhrygianKeyword,
            "lydian" => SyntaxKind.LydianKeyword,
            "mixolydian" => SyntaxKind.MixolydianKeyword,
            "aeolian" => SyntaxKind.AeolianKeyword,
            "locrian" => SyntaxKind.LocrianKeyword,

            // Clef keywords
            "treble" => SyntaxKind.TrebleKeyword,
            "bass" => SyntaxKind.BassKeyword,
            "alto" => SyntaxKind.AltoKeyword,
            "tenor" => SyntaxKind.TenorKeyword,
            "treble_8" => SyntaxKind.Treble8Keyword,
            "soprano" => SyntaxKind.SopranoKeyword,
            "mezzosoprano" => SyntaxKind.MezzoSopranoKeyword,
            "baritone" => SyntaxKind.BaritoneKeyword,
            "bass_8" => SyntaxKind.Bass8Keyword,
            "percussion" => SyntaxKind.PercussionKeyword,
            "drummap" => SyntaxKind.DrummapKeyword,

            // Other keywords
            "tuplet" => SyntaxKind.TupletKeyword,
            "grace" => SyntaxKind.GraceKeyword,
            "acciaccatura" => SyntaxKind.AcciaccaturaKeyword,
            "appoggiatura" => SyntaxKind.AppoggiaturaKeyword,
            "cue" => SyntaxKind.CueKeyword,
            "lyrics" => SyntaxKind.LyricsKeyword,
            "chords" => SyntaxKind.ChordsKeyword,
            "tuning" => SyntaxKind.TuningKeyword,
            "break" => SyntaxKind.BreakKeyword,
            "nobreak" => SyntaxKind.NoBreakKeyword,
            "partial" => SyntaxKind.PartialKeyword,

            // Articulation/ornament names (staccato, tr, mordent, cresc, dim, …) are
            // NOT reserved words: they are resolved from the '@name' text against
            // ArticulationRegistry / the mark registry, so they stay identifiers here
            // and no longer collide with user names like 'tr', 'acc', 'ten', 'dim'.

            // Override/Revert
            "override" => SyntaxKind.OverrideKeyword,
            "revert" => SyntaxKind.RevertKeyword,
            "once" => SyntaxKind.OnceKeyword,

            // Dynamics
            "ppp" => SyntaxKind.DynamicPPP,
            "pp" => SyntaxKind.DynamicPP,
            "p" => SyntaxKind.DynamicP,
            "mp" => SyntaxKind.DynamicMP,
            "mf" => SyntaxKind.DynamicMF,
            "f" => SyntaxKind.DynamicF,
            "ff" => SyntaxKind.DynamicFF,
            "fff" => SyntaxKind.DynamicFFF,

            // Default: identifier
            _ => SyntaxKind.Identifier
        };
    }

    private static SyntaxKind GetPitchKind(string text)
    {
        // Get base pitch from first character
        return char.ToLower(text[0]) switch
        {
            'c' => SyntaxKind.PitchC,
            'd' => SyntaxKind.PitchD,
            'e' => SyntaxKind.PitchE,
            'f' => SyntaxKind.PitchF,
            'g' => SyntaxKind.PitchG,
            'a' => SyntaxKind.PitchA,
            'b' => SyntaxKind.PitchB,
            _ => SyntaxKind.Identifier
        };
    }
}