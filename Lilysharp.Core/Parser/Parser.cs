using Lilysharp.Core.Syntax;
using Lilysharp.Core.Syntax.InternalSyntax;

namespace Lilysharp.Core.Parser;

/// <summary>
/// Recursive descent parser for Lilysharp.
/// </summary>
internal sealed class Parser
{
    private readonly List<SyntaxToken> _tokens;
    private readonly DiagnosticBag _diagnostics = new();
    private int _position;
    private int _textPosition; // Tracks position in source text

    public Parser(IEnumerable<SyntaxToken> tokens)
    {
        _tokens = tokens.ToList();
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
        
        // Error recovery: create a missing token
        return new SyntaxToken(kind, "", Current.LeadingTrivia, null);
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
            SyntaxKind.ScoreKeyword => ParseScoreDeclaration(),
            SyntaxKind.PartKeyword => ParsePartDeclaration(),
            SyntaxKind.RelativeKeyword => ParseRelativeExpression(),
            SyntaxKind.LetKeyword => ParseVariableDeclaration(),
            SyntaxKind.UseKeyword or SyntaxKind.Dollar => ParseVariableReference(),
            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword => ParseMetadataDeclaration(),
            SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword => ParseMetadataDeclaration(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            SyntaxKind.TupletKeyword => ParseTupletExpression(),
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            _ when IsMusicItemStart() => ParseMusicItem(),
            _ => null
        };
    }

    // ========== Structure Declarations ==========

    private ScoreDeclarationGreen ParseScoreDeclaration()
    {
        var keyword = Expect(SyntaxKind.ScoreKeyword);
        var title = TryConsume(SyntaxKind.StringLiteral);
        var openBrace = Expect(SyntaxKind.OpenBrace);
        
        var members = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            var member = ParseScoreMember();
            if (member != null)
                members.Add(member);
            else
                Advance();
        }
        
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new ScoreDeclarationGreen(keyword, title, openBrace, [.. members], closeBrace);
    }

    private GreenNode? ParseScoreMember()
    {
        return Current.Kind switch
        {
            SyntaxKind.PartKeyword => ParsePartDeclaration(),
            SyntaxKind.TempoKeyword or SyntaxKind.TimeKeyword or 
            SyntaxKind.KeyKeyword => ParsePropertyAssignment(),
            SyntaxKind.TitleKeyword or SyntaxKind.ComposerKeyword => ParsePropertyAssignment(),
            _ => null
        };
    }

    private PartDeclarationGreen ParsePartDeclaration()
    {
        var keyword = Expect(SyntaxKind.PartKeyword);
        var name = TryConsume(SyntaxKind.Identifier);
        var displayName = TryConsume(SyntaxKind.StringLiteral);
        var openBrace = Expect(SyntaxKind.OpenBrace);
        
        var members = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            var member = ParsePartMember();
            if (member != null)
                members.Add(member);
            else
                Advance();
        }
        
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new PartDeclarationGreen(keyword, name, displayName, openBrace, [.. members], closeBrace);
    }

    private GreenNode? ParsePartMember()
    {
        return Current.Kind switch
        {
            SyntaxKind.StaffKeyword => ParseStaffDeclaration(),
            SyntaxKind.RelativeKeyword => ParseRelativeExpression(),
            SyntaxKind.ClefKeyword or SyntaxKind.TempoKeyword or 
            SyntaxKind.TimeKeyword or SyntaxKind.KeyKeyword => ParsePropertyAssignment(),
            SyntaxKind.UseKeyword or SyntaxKind.Dollar => ParseVariableReference(),
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            _ when IsMusicItemStart() => ParseMusicItem(),
            _ => null
        };
    }

    private StaffDeclarationGreen ParseStaffDeclaration()
    {
        var keyword = Expect(SyntaxKind.StaffKeyword);
        var name = TryConsume(SyntaxKind.Identifier);
        var openBrace = Expect(SyntaxKind.OpenBrace);
        
        var members = new List<GreenNode?>();
        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            var member = ParsePartMember(); // Staff has same members as Part
            if (member != null)
                members.Add(member);
            else
                Advance();
        }
        
        var closeBrace = Expect(SyntaxKind.CloseBrace);
        return new StaffDeclarationGreen(keyword, name, openBrace, [.. members], closeBrace);
    }

    // ========== Properties and Metadata ==========

    private PropertyAssignmentGreen ParsePropertyAssignment()
    {
        var name = Advance(); // keyword like tempo, clef, etc.
        var colon = Expect(SyntaxKind.Colon);
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

    // ========== Variables ==========

    private VariableDeclarationGreen ParseVariableDeclaration()
    {
        var letKeyword = Expect(SyntaxKind.LetKeyword);
        var name = Expect(SyntaxKind.Identifier);
        var equals = Expect(SyntaxKind.Equals);
        var expression = ParseMusicExpression();
        return new VariableDeclarationGreen(letKeyword, name, equals, expression);
    }

    private VariableReferenceGreen ParseVariableReference()
    {
        if (Check(SyntaxKind.UseKeyword))
        {
            var use = Advance();
            var name = Expect(SyntaxKind.Identifier);
            return new VariableReferenceGreen(use, name);
        }
        else // $name
        {
            var dollar = Expect(SyntaxKind.Dollar);
            var name = Expect(SyntaxKind.Identifier);
            return new VariableReferenceGreen(dollar, name);
        }
    }

    private GreenNode ParseMusicExpression()
    {
        return Current.Kind switch
        {
            SyntaxKind.RelativeKeyword => ParseRelativeExpression(),
            SyntaxKind.OpenBrace => ParseMusicBlock(),
            _ => ParseMusicBlock() // fallback
        };
    }

    // ========== Music Expressions ==========

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
            SyntaxKind.DoubleOpenAngle => true, // Parallel <<
            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => true,
            SyntaxKind.Tilde => true,
            SyntaxKind.OpenParen or SyntaxKind.CloseParen => true,
            SyntaxKind.RepeatKeyword => true,
            SyntaxKind.TupletKeyword => true,
            SyntaxKind.KeyKeyword => true,
            SyntaxKind.ClefKeyword => true,
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
            
            SyntaxKind.DoubleOpenAngle => ParseParallelExpression(),

            SyntaxKind.Bar or SyntaxKind.DoubleBar or SyntaxKind.FinalBar or
            SyntaxKind.RepeatStartBar or SyntaxKind.RepeatEndBar => ParseBarline(),

            SyntaxKind.Tilde => ParseTie(),

            SyntaxKind.OpenParen or SyntaxKind.CloseParen => ParseSlur(),

            SyntaxKind.UseKeyword or SyntaxKind.Dollar => ParseVariableReference(),
            
            SyntaxKind.RepeatKeyword => ParseRepeatExpression(),
            SyntaxKind.TupletKeyword => ParseTupletExpression(),
            SyntaxKind.KeyKeyword => ParseKeySignature(),
            SyntaxKind.ClefKeyword => ParseClefDeclaration(),
            

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
        var articulations = new List<GreenNode?>();
        
        while (true)
        {
            if (Check(SyntaxKind.At))
            {
                // @staccato, @accent, etc.
                var at = Advance();
                if (IsArticulationName())
                {
                    var name = Advance();
                    articulations.Add(new ArticulationGreen(at, name));
                }
                else
                {
                    // Error: expected articulation name after @
                    var span = new TextSpan(_textPosition, Current.FullWidth);
                    _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                        $"Expected articulation name after '@', found '{Current.Kind}'");
                }
            }
            else if (Check(SyntaxKind.Backslash))
            {
                // \p, \f, \cresc, etc.
                var backslash = Advance();
                if (IsDynamicName())
                {
                    var name = Advance();
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

    private bool IsArticulationName()
    {
        return Current.Kind is SyntaxKind.StaccatoKeyword or SyntaxKind.AccentKeyword or
            SyntaxKind.TenutoKeyword or SyntaxKind.MarcatoKeyword or
            SyntaxKind.FermataKeyword or SyntaxKind.PortatoKeyword or
            SyntaxKind.Identifier; // Allow custom articulation names
    }

    private bool IsDynamicName()
    {
        return Current.Kind is SyntaxKind.DynamicPPP or SyntaxKind.DynamicPP or
            SyntaxKind.DynamicP or SyntaxKind.DynamicMP or
            SyntaxKind.DynamicMF or SyntaxKind.DynamicF or
            SyntaxKind.DynamicFF or SyntaxKind.DynamicFFF or
            SyntaxKind.CrescKeyword or SyntaxKind.DecrescKeyword or
            SyntaxKind.DimKeyword;
    }

    // ========== Repeat and Parallel ==========

    private RepeatExpressionGreen ParseRepeatExpression()
    {
        var repeatKeyword = Expect(SyntaxKind.RepeatKeyword);
        
        // Expect repeat type: volta, unfold, percent, tremolo
        SyntaxToken repeatType;
        if (Check(SyntaxKind.VoltaKeyword) || Check(SyntaxKind.Identifier))
        {
            repeatType = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected repeat type (volta, unfold, percent, tremolo)");
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

    private ParallelExpressionGreen ParseParallelExpression()
    {
        var openAngle = Expect(SyntaxKind.DoubleOpenAngle);
        
        var voices = new List<GreenNode?>();
        
        // Parse first voice
        voices.Add(ParseVoiceContent());
        
        // Parse additional voices separated by \\
        while (Check(SyntaxKind.Backslash) && Peek().Kind == SyntaxKind.Backslash)
        {
            voices.Add(Advance()); // first backslash
            voices.Add(Advance()); // second backslash
            voices.Add(ParseVoiceContent());
        }
        
        var closeAngle = Expect(SyntaxKind.DoubleCloseAngle);
        return new ParallelExpressionGreen(openAngle, [.. voices], closeAngle);
    }

    private GreenNode ParseVoiceContent()
    {
        // A voice can be a relative expression, music block, or sequence of items
        if (Check(SyntaxKind.RelativeKeyword))
        {
            return ParseRelativeExpression();
        }
        if (Check(SyntaxKind.OpenBrace))
        {
            return ParseMusicBlock();
        }
        
        // Parse inline music items until \\ or >>
        var items = new List<GreenNode?>();
        while (!Check(SyntaxKind.DoubleCloseAngle) && 
               !Check(SyntaxKind.EndOfFile) &&
               !(Check(SyntaxKind.Backslash) && Peek().Kind == SyntaxKind.Backslash))
        {
            var item = ParseMusicItem();
            if (item != null)
                items.Add(item);
            else
                break;
        }
        
        // Wrap in an implicit music block
        var openBrace = new SyntaxToken(SyntaxKind.OpenBrace, "", null, null);
        var closeBrace = new SyntaxToken(SyntaxKind.CloseBrace, "", null, null);
        return new MusicBlockGreen(openBrace, [.. items], closeBrace);
    }

    // ========== Key, Clef, Tuplet ==========

    private KeySignatureGreen ParseKeySignature()
    {
        var keyKeyword = Expect(SyntaxKind.KeyKeyword);
        var pitch = ParsePitch();
        
        SyntaxToken mode;
        if (Check(SyntaxKind.MajorKeyword) || Check(SyntaxKind.MinorKeyword))
        {
            mode = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected 'major' or 'minor'");
            mode = new SyntaxToken(SyntaxKind.MajorKeyword, "major", null, null);
        }
        
        return new KeySignatureGreen(keyKeyword, pitch, mode);
    }

    private ClefDeclarationGreen ParseClefDeclaration()
    {
        var clefKeyword = Expect(SyntaxKind.ClefKeyword);
        
        SyntaxToken clefName;
        if (CheckAny(SyntaxKind.TrebleKeyword, SyntaxKind.BassKeyword, 
                     SyntaxKind.AltoKeyword, SyntaxKind.TenorKeyword))
        {
            clefName = Advance();
        }
        else
        {
            var span = new TextSpan(_textPosition, Current.FullWidth);
            _diagnostics.Error(span, DiagnosticCodes.ExpectedToken,
                "Expected clef name (treble, bass, alto, tenor)");
            clefName = new SyntaxToken(SyntaxKind.TrebleKeyword, "treble", null, null);
        }
        
        return new ClefDeclarationGreen(clefKeyword, clefName);
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
}