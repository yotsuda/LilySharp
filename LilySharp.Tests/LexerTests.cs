using Xunit;
using LilySharp.Core.Parser;
using LilySharp.Core.Syntax;

namespace LilySharp.Tests;

public class LexerTests
{
    [Fact]
    public void ScanSimpleNote()
    {
        var lexer = new Lexer("c");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(2, tokens.Count);
        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal("c", tokens[0].Text);
        Assert.Equal(SyntaxKind.EndOfFile, tokens[1].Kind);
    }

    [Fact]
    public void ScanNoteWithAccidental()
    {
        var lexer = new Lexer("cis des fisis");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal("cis", tokens[0].Text);
        Assert.Equal(SyntaxKind.PitchD, tokens[1].Kind);
        Assert.Equal("des", tokens[1].Text);
        Assert.Equal(SyntaxKind.PitchF, tokens[2].Kind);
        Assert.Equal("fisis", tokens[2].Text);
    }

    [Fact]
    public void ScanNotesWithOctaveMarks()
    {
        var lexer = new Lexer("c'' d,");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Apostrophe, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Apostrophe, tokens[2].Kind);
        Assert.Equal(SyntaxKind.PitchD, tokens[3].Kind);
        Assert.Equal(SyntaxKind.Comma, tokens[4].Kind);
    }

    [Fact]
    public void ScanDuration()
    {
        var lexer = new Lexer("4 8 16");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.IntegerLiteral, tokens[0].Kind);
        Assert.Equal("4", tokens[0].Text);
        Assert.Equal(SyntaxKind.IntegerLiteral, tokens[1].Kind);
        Assert.Equal("8", tokens[1].Text);
        Assert.Equal(SyntaxKind.IntegerLiteral, tokens[2].Kind);
        Assert.Equal("16", tokens[2].Text);
    }

    [Fact]
    public void ScanRest()
    {
        var lexer = new Lexer("r4");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.RestR, tokens[0].Kind);
        Assert.Equal(SyntaxKind.IntegerLiteral, tokens[1].Kind);
    }

    [Fact]
    public void ScanBarline()
    {
        var lexer = new Lexer("c d | e f |.");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Contains(tokens, t => t.Kind == SyntaxKind.Bar);
        Assert.Contains(tokens, t => t.Kind == SyntaxKind.FinalBar);
    }

    [Fact]
    public void ScanChordBrackets()
    {
        var lexer = new Lexer("<c e g>");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.OpenAngle, tokens[0].Kind);
        Assert.Equal(SyntaxKind.PitchC, tokens[1].Kind);
        Assert.Equal(SyntaxKind.PitchE, tokens[2].Kind);
        Assert.Equal(SyntaxKind.PitchG, tokens[3].Kind);
        Assert.Equal(SyntaxKind.CloseAngle, tokens[4].Kind);
    }

    [Fact]
    public void ScanBraces()
    {
        var lexer = new Lexer("{ c d e }");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.OpenBrace, tokens[0].Kind);
        Assert.Equal(SyntaxKind.CloseBrace, tokens[4].Kind);
    }

    [Fact]
    public void ScanKeywords()
    {
        var lexer = new Lexer("relative c' { }");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.RelativeKeyword, tokens[0].Kind);
        Assert.Equal(SyntaxKind.PitchC, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Apostrophe, tokens[2].Kind);
        Assert.Equal(SyntaxKind.OpenBrace, tokens[3].Kind);
        Assert.Equal(SyntaxKind.CloseBrace, tokens[4].Kind);
    }

    [Fact]
    public void ScanLineComment()
    {
        var lexer = new Lexer("c // comment\nd");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal(SyntaxKind.PitchD, tokens[1].Kind);
        
        // Comment should be in trivia
        var firstToken = tokens[0];
        Assert.NotNull(firstToken.TrailingTrivia);
    }

    [Fact]
    public void ScanBlockComment()
    {
        var lexer = new Lexer("c /* comment */ d");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal(SyntaxKind.PitchD, tokens[1].Kind);
    }

    [Fact]
    public void ScanString()
    {
        var lexer = new Lexer("title \"Happy Birthday\"");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.TitleKeyword, tokens[0].Kind);
        Assert.Equal(SyntaxKind.StringLiteral, tokens[1].Kind);
        Assert.Equal("\"Happy Birthday\"", tokens[1].Text);
    }

    [Fact]
    public void ScanTie()
    {
        var lexer = new Lexer("c4~ c4");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal(SyntaxKind.IntegerLiteral, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Tilde, tokens[2].Kind);
    }

    [Fact]
    public void ScanSlur()
    {
        var lexer = new Lexer("c( d e f)");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.PitchC, tokens[0].Kind);
        Assert.Equal(SyntaxKind.OpenParen, tokens[1].Kind);
        Assert.Equal(SyntaxKind.CloseParen, tokens[5].Kind);
    }

    [Fact]
    public void ScanRepeatBars()
    {
        var lexer = new Lexer("|: c d e f :|");
        var tokens = lexer.ScanAllTokens().ToList();

        Assert.Equal(SyntaxKind.RepeatStartBar, tokens[0].Kind);
        Assert.Equal("|:", tokens[0].Text);
    }

    [Fact]
    public void RoundTrip_PreservesText()
    {
        var original = "relative c' { c4 d e f | g2 g | }";
        var lexer = new Lexer(original);
        var tokens = lexer.ScanAllTokens().ToList();

        // Reconstruct text from tokens
        var reconstructed = string.Join("", tokens.Select(t => t.ToFullString()));

        Assert.Equal(original, reconstructed);
    }
}