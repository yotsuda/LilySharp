using Lilysharp.Core.Syntax;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Parser;

/// <summary>
/// Lexer that tokenizes Lilysharp source code.
/// </summary>
internal sealed class Lexer
{
    private readonly string _text;
    private int _position;

    public Lexer(string text)
    {
        _text = text;
        _position = 0;
    }

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

        return new SyntaxToken(kind, text, leadingTrivia, trailingTrivia);
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
        return new SyntaxTrivia(SyntaxKind.WhitespaceTrivia, _text[start.._position]);
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
        return new SyntaxTrivia(SyntaxKind.EndOfLineTrivia, _text[start.._position]);
    }

    private SyntaxTrivia ScanLineComment()
    {
        int start = _position;
        _position += 2; // skip //
        while (!IsAtEnd && Current != '\r' && Current != '\n')
            _position++;
        return new SyntaxTrivia(SyntaxKind.LineCommentTrivia, _text[start.._position]);
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
        return new SyntaxTrivia(SyntaxKind.BlockCommentTrivia, _text[start.._position]);
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
            case '=': _position++; return (SyntaxKind.Equals, "=");
            case '@': _position++; return (SyntaxKind.At, "@");
            case '$': _position++; return (SyntaxKind.Dollar, "$");
            case '\'': _position++; return (SyntaxKind.Apostrophe, "'");
            case ',': _position++; return (SyntaxKind.Comma, ",");
            case '.': _position++; return (SyntaxKind.Dot, ".");
            case '\\':
                if (Peek(1) >= '1' && Peek(1) <= '9')
                {
                    _position++; // skip \
                    var stringNum = Current.ToString();
                    _position++;
                    return (SyntaxKind.StringNumber, stringNum);
                }
                _position++; 
                return (SyntaxKind.Backslash, "\\");
            case '/': _position++; return (SyntaxKind.Slash, "/");
        }

        // Colon: : or :|
        if (Current == ':')
        {
            _position++;
            if (Current == '|') { _position++; return (SyntaxKind.RepeatEndBar, ":|"); }
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

        // Barlines: |, ||, |., |:
        if (Current == '|')
        {
            _position++;
            if (Current == '|') { _position++; return (SyntaxKind.DoubleBar, "||"); }
            if (Current == '.') { _position++; return (SyntaxKind.FinalBar, "|."); }
            if (Current == ':') { _position++; return (SyntaxKind.RepeatStartBar, "|:"); }
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
            if (Current == '\\' && !IsAtEnd)
                _position++; // skip escape
            _position++;
        }

        if (Current == '"')
            _position++; // skip closing quote

        return (SyntaxKind.StringLiteral, _text[start.._position]);
    }

    private (SyntaxKind kind, string text) ScanNumber()
    {
        int start = _position;
        while (char.IsDigit(Current))
            _position++;
        return (SyntaxKind.IntegerLiteral, _text[start.._position]);
    }

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

        string text = _text[start.._position];
        return (GetKeywordKind(text), text);
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

            // Next is letter but not accidental - continue as identifier
            _position = accidentalStart;
        }

        // Fall through to regular identifier
        while (char.IsLetterOrDigit(Current) || Current == '_')
            _position++;

        string text = _text[start.._position];
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
            "structure" => SyntaxKind.StructureKeyword,
            "render" => SyntaxKind.RenderKeyword,
            "tab" => SyntaxKind.TabKeyword,
            
            // Part options
            "transpose" => SyntaxKind.TransposeKeyword,
            "octave" => SyntaxKind.OctaveKeyword,
            "instrument" => SyntaxKind.InstrumentKeyword,
            "channel" => SyntaxKind.ChannelKeyword,
            
            // Navigation keywords (structure block)
            "segno" => SyntaxKind.SegnoKeyword,
            "fine" => SyntaxKind.FineKeyword,
            "coda" => SyntaxKind.CodaKeyword,
            "dc" => SyntaxKind.DcKeyword,
            "ds" => SyntaxKind.DsKeyword,
            "al" => SyntaxKind.AlKeyword,
            "to" => SyntaxKind.ToKeyword,
            
            // Legacy structure keywords
            "score" => SyntaxKind.ScoreKeyword,
            "part" => SyntaxKind.PartKeyword,
            "staff" => SyntaxKind.StaffKeyword,
            "voice" => SyntaxKind.VoiceKeyword,
            "relative" => SyntaxKind.RelativeKeyword,
            "absolute" => SyntaxKind.AbsoluteKeyword,
            "fixed" => SyntaxKind.FixedKeyword,
            "repeat" => SyntaxKind.RepeatKeyword,
            "volta" => SyntaxKind.VoltaKeyword,
            "alternative" => SyntaxKind.AlternativeKeyword,
            "let" => SyntaxKind.LetKeyword,
            "use" => SyntaxKind.UseKeyword,

            // Metadata keywords
            "title" => SyntaxKind.TitleKeyword,
            "composer" => SyntaxKind.ComposerKeyword,
            "tempo" => SyntaxKind.TempoKeyword,
            "time" => SyntaxKind.TimeKeyword,
            "key" => SyntaxKind.KeyKeyword,
            "clef" => SyntaxKind.ClefKeyword,

            // Mode keywords
            "major" => SyntaxKind.MajorKeyword,
            "minor" => SyntaxKind.MinorKeyword,
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
            
            // Other keywords
            "tuplet" => SyntaxKind.TupletKeyword,
            "grace" => SyntaxKind.GraceKeyword,
            "acciaccatura" => SyntaxKind.AcciaccaturaKeyword,
            "appoggiatura" => SyntaxKind.AppogiaturaKeyword,
            "lyrics" => SyntaxKind.LyricsKeyword,
            "tabStaff" or "tabstaff" => SyntaxKind.TabStaffKeyword,
            "tuning" => SyntaxKind.TuningKeyword,

            // Articulation names
            "staccato" or "stac" => SyntaxKind.StaccatoKeyword,
            "accent" or "acc" => SyntaxKind.AccentKeyword,
            "tenuto" or "ten" => SyntaxKind.TenutoKeyword,
            "marcato" or "marc" => SyntaxKind.MarcatoKeyword,
            "fermata" or "ferm" => SyntaxKind.FermataKeyword,
            "portato" => SyntaxKind.PortatoKeyword,

            // Dynamics
            "ppp" => SyntaxKind.DynamicPPP,
            "pp" => SyntaxKind.DynamicPP,
            "p" => SyntaxKind.DynamicP,
            "mp" => SyntaxKind.DynamicMP,
            "mf" => SyntaxKind.DynamicMF,
            "f" => SyntaxKind.DynamicF,
            "ff" => SyntaxKind.DynamicFF,
            "fff" => SyntaxKind.DynamicFFF,
            "cresc" => SyntaxKind.CrescKeyword,
            "decresc" => SyntaxKind.DecrescKeyword,
            "dim" => SyntaxKind.DimKeyword,

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