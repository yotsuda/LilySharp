using Lilysharp.Core.Syntax;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Parser;

/// <summary>
/// Recursive descent parser for Lilysharp.
/// </summary>
internal sealed class Parser
{
    private readonly List<SyntaxToken> _tokens;
    private int _position;

    public Parser(IEnumerable<SyntaxToken> tokens)
    {
        _tokens = tokens.ToList();
        _position = 0;
    }

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
            _position++;
        return token;
    }

    private bool Check(SyntaxKind kind) => Current.Kind == kind;
    private bool CheckAny(params SyntaxKind[] kinds) => kinds.Contains(Current.Kind);

    private SyntaxToken Expect(SyntaxKind kind)
    {
        if (Check(kind))
            return Advance();
        
        // Error recovery: create a missing token
        return new SyntaxToken(kind, "", Current.LeadingTrivia, null);
    }

    /// <summary>
    /// Parse the entire source into a compilation unit.
    /// </summary>
    public CompilationUnitGreen ParseCompilationUnit()
    {
        var members = new List<GreenNode?>();

        while (!Check(SyntaxKind.EndOfFile))
        {
            var member = ParseTopLevelItem();
            if (member != null)
                members.Add(member);
            else
                Advance(); // Skip unexpected token
        }

        var eof = Expect(SyntaxKind.EndOfFile);
        return new CompilationUnitGreen([.. members], eof);
    }

    private GreenNode? ParseTopLevelItem()
    {
        return Current.Kind switch
        {
            SyntaxKind.RelativeKeyword => ParseRelativeExpression(),
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            _ when IsMusicItemStart() => ParseMusicItem(),
            _ => null
        };
    }

    private RelativeExpressionGreen ParseRelativeExpression()
    {
        var keyword = Expect(SyntaxKind.RelativeKeyword);
        var basePitch = ParsePitch();
        var body = ParseMusicBlock();
        return new RelativeExpressionGreen(keyword, basePitch, body);
    }

    private MusicBlockGreen ParseMusicBlock()
    {
        var openBrace = Expect(SyntaxKind.OpenBrace);
        var items = new List<GreenNode?>();

        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
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
        return Current.Kind switch
        {
            SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
            SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
            SyntaxKind.PitchB => true,
            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => true,
            SyntaxKind.OpenAngle => true, // Chord
            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => true,
            SyntaxKind.Tilde => true,
            SyntaxKind.OpenParen or SyntaxKind.CloseParen => true,
            _ => false
        };
    }

    private GreenNode? ParseMusicItem()
    {
        return Current.Kind switch
        {
            SyntaxKind.PitchC or SyntaxKind.PitchD or SyntaxKind.PitchE or
            SyntaxKind.PitchF or SyntaxKind.PitchG or SyntaxKind.PitchA or
            SyntaxKind.PitchB => ParseNote(),

            SyntaxKind.RestR or SyntaxKind.RestS or SyntaxKind.RestR_Full => ParseRest(),

            SyntaxKind.OpenAngle => ParseChord(),

            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => ParseBarline(),

            SyntaxKind.Tilde => ParseTie(),

            SyntaxKind.OpenParen or SyntaxKind.CloseParen => ParseSlur(),

            _ => null
        };
    }

    private bool IsPitchStart()
    {
        return Current.Kind is SyntaxKind.PitchC or SyntaxKind.PitchD or 
            SyntaxKind.PitchE or SyntaxKind.PitchF or SyntaxKind.PitchG or 
            SyntaxKind.PitchA or SyntaxKind.PitchB;
    }

    private PitchGreen ParsePitch()
    {
        var pitchToken = Advance(); // Consume pitch token (c, cis, des, etc.)
        var octaveMarks = new List<GreenNode?>();

        // Collect octave marks: ' or ,
        while (Check(SyntaxKind.Apostrophe) || Check(SyntaxKind.Comma))
        {
            octaveMarks.Add(Advance());
        }

        return new PitchGreen(pitchToken, [.. octaveMarks]);
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
        var articulations = ParseArticulations();
        return new NoteGreen(pitch, duration, articulations);
    }

    private RestGreen ParseRest()
    {
        var restToken = Advance();
        var duration = ParseOptionalDuration();
        return new RestGreen(restToken, duration);
    }

    private ChordGreen ParseChord()
    {
        var openAngle = Expect(SyntaxKind.OpenAngle);
        var pitches = new List<GreenNode?>();

        while (IsPitchStart())
        {
            pitches.Add(ParsePitch());
        }

        var closeAngle = Expect(SyntaxKind.CloseAngle);
        var duration = ParseOptionalDuration();
        var articulations = ParseArticulations();

        return new ChordGreen(openAngle, [.. pitches], closeAngle, duration, articulations);
    }

    private BarlineGreen ParseBarline()
    {
        var barToken = Advance();
        return new BarlineGreen(barToken);
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

    private GreenNode?[] ParseArticulations()
    {
        // TODO: Parse @staccato, \p, etc.
        return [];
    }
}