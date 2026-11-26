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
    /// <summary>The word that opens a drum steps chart — claimed only before a <c>{</c> or a
    /// beat (<c>steps 4. {</c>).</summary>
    internal const string StepsWord = "steps";

    private bool AtStepsStart()
        => Current.Text == StepsWord
           && (Peek().Kind == SyntaxKind.OpenBrace
               || Peek().Kind == SyntaxKind.IntegerLiteral
                  && (Peek(2).Kind == SyntaxKind.OpenBrace
                      || Peek(2).Kind == SyntaxKind.Dot && Peek(3).Kind == SyntaxKind.OpenBrace));

    /// <summary>
    /// A drum steps chart (Orecchiante's request 24, owner's decision 2026-11-26,
    /// docs/drum-steps-design.md): <c>steps { hh "x.x. x.x. x.x. x.x."  sn ".... x... .... x..." }</c>.
    /// Each ROW is a drum and its steps in quotes; the rows play together, as a
    /// <c>drums { }</c> chart's lines do.
    /// </summary>
    /// <remarks>
    /// The chart IS a <c>drums { }</c> chart — the same <see cref="ParallelExpressionGreen"/>
    /// with the same <see cref="SyntaxKind.DrumsOpener"/> (spelled <c>steps</c>), one block a
    /// row — so the page, the MIDI, the exports and the validators read it with nothing new
    /// to learn. Each row's string token is replaced IN THE TOKEN STREAM by the tokens the row
    /// stands for (<see cref="StepsPattern.Lower"/>): the quotes are the block's braces, each
    /// hit a drum note spelled <c>hh</c> on its own character, the durations, rests, triplets
    /// and grace notes zero-width. Then the block is parsed as any block is. The reading
    /// depends on the chart's text alone (a beat is a quarter unless the chart says
    /// <c>steps 4.</c>), never on a <c>time</c> elsewhere: the incremental parse adopts an
    /// unchanged phrase's tree whatever else changed.
    /// </remarks>
    private ParallelExpressionGreen ParseStepsBlock()
    {
        if (_voiceBodyDepth > 0)
            _diagnostics.Error(new TextSpan(_textPosition, Current.FullWidth), DiagnosticCodes.NestedVoiceBlock,
                "a steps { } chart cannot stand inside a voice or another chart's line — its rows are "
                + "already played together; write it beside the voices, not inside one.");
        var word = Advance();
        var opener = new SyntaxToken(SyntaxKind.DrumsOpener, word.Text, word.LeadingTrivia, word.TrailingTrivia);
        var children = new List<GreenNode?>();

        int beat = StepsPattern.Whole / 4;
        bool dotted = false;
        if (Check(SyntaxKind.IntegerLiteral))
        {
            int at = _textPosition + Current.LeadingTriviaWidth;
            var number = Advance();
            children.Add(new SyntaxToken(SyntaxKind.StepsBeat, number.Text, number.LeadingTrivia, number.TrailingTrivia));
            if (Check(SyntaxKind.Dot) && CurrentGluedToPrevious)
            {
                var dot = Advance();
                children.Add(new SyntaxToken(SyntaxKind.StepsBeat, dot.Text, dot.LeadingTrivia, dot.TrailingTrivia));
                dotted = true;
            }
            if (int.TryParse(number.Text, out int d) && d is 1 or 2 or 4 or 8 or 16)
                beat = dotted ? StepsPattern.Whole / d * 3 / 2 : StepsPattern.Whole / d;
            else
            {
                _diagnostics.Error(new TextSpan(at, number.Width), DiagnosticCodes.StepsPattern,
                    $"a steps chart's beat is a note value — 1, 2, 4, 8 or 16, dotted or not "
                    + "(steps 4. { } for 6/8): a beat is a quarter when none is written");
                dotted = false;
            }
        }

        children.Add(Expect(SyntaxKind.OpenBrace));
        var rows = new List<(string Drum, TextSpan At, StepsPattern.Row Row)>();

        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            if (Check(SyntaxKind.Identifier) || IsBareReferenceKind(Current.Kind))
            {
                var nameAt = new TextSpan(_textPosition + Current.LeadingTriviaWidth, Current.Width);
                var name = Advance();
                children.Add(new SyntaxToken(SyntaxKind.StepsRowName, name.Text, name.LeadingTrivia, name.TrailingTrivia));
                bool drum = DrumNameRegistry.Contains(name.Text);
                if (!drum)
                    _diagnostics.Error(nameAt, DiagnosticCodes.StepsPattern,
                        $"'{name.Text}' is not a drum: a steps row is a drum's name and its steps in quotes — "
                        + "steps { hh \"x.x. x.x. x.x. x.x.\"  sn \".... x... .... x...\"  bd \"x... ..x. x... ....\" }");
                if (!Check(SyntaxKind.StringLiteral))
                {
                    _diagnostics.Error(nameAt, DiagnosticCodes.StepsPattern,
                        $"the {name.Text} row has no steps: write them in quotes after it — {name.Text} \"x.x. x.x. x.x. x.x.\"");
                    continue;
                }
                if (!drum)
                {
                    // Nothing to strike: the pattern is kept as it is written and plays nothing.
                    children.Add(Advance());
                    continue;
                }
                var literal = Current;
                int literalAt = _textPosition + literal.LeadingTriviaWidth;
                var row = StepsPattern.Lower(name.Text, literal.Text, beat, dotted);
                foreach (var p in row.Problems)
                {
                    var span = new TextSpan(literalAt + p.Offset, p.Length);
                    if (p.IsWarning)
                        _diagnostics.Warning(span, DiagnosticCodes.StepsMeaningless, p.Message);
                    else
                        _diagnostics.Error(span, DiagnosticCodes.StepsPattern, p.Message);
                }
                rows.Add((name.Text, nameAt, row));
                SpliceRow(literal, row);
                _voiceBodyDepth++;
                children.Add(ParseMusicBlock());
                _voiceBodyDepth--;
                continue;
            }

            if (Check(SyntaxKind.StringLiteral))
                _diagnostics.Error(new TextSpan(_textPosition + Current.LeadingTriviaWidth, Current.Width), DiagnosticCodes.StepsPattern,
                    "these steps have no drum: a steps row is a drum's name and its steps in quotes — hh \"x.x. x.x. x.x. x.x.\"");
            else
                _diagnostics.Error(new TextSpan(_textPosition + Current.LeadingTriviaWidth, Math.Max(1, Current.Width)), DiagnosticCodes.StepsPattern,
                    "a steps { } chart holds ROWS — a drum's name and its steps in quotes, one row a drum: "
                    + "steps { hh \"x.x. x.x. x.x. x.x.\"  sn \".... x... .... x...\" }. Notes and phrases go in a drums { } chart.");
            children.Add(Advance());
        }

        CheckRowsAgree(rows);
        var close = Expect(SyntaxKind.CloseBrace);
        return new ParallelExpressionGreen(opener, [.. children], close);
    }

    /// <summary>Every row writes the same bars, each of the same beats — or the rows would
    /// drift apart, and which one is wrong is the writer's to say.</summary>
    private void CheckRowsAgree(List<(string Drum, TextSpan At, StepsPattern.Row Row)> rows)
    {
        if (rows.Count < 2)
            return;
        var first = rows[0];
        foreach (var other in rows.Skip(1))
        {
            int a = first.Row.BeatsPerBar.Count, b = other.Row.BeatsPerBar.Count;
            if (a != b)
            {
                _diagnostics.Error(other.At, DiagnosticCodes.StepsPattern,
                    $"the {other.Drum} row has {Bars(b)} and the {first.Drum} row {Bars(a)}: every row of a steps chart "
                    + "writes the same bars (| between them)");
                continue;
            }
            for (int k = 0; k < a; k++)
                if (first.Row.BeatsPerBar[k] != other.Row.BeatsPerBar[k] && first.Row.BeatsPerBar[k] > 0 && other.Row.BeatsPerBar[k] > 0)
                {
                    _diagnostics.Error(other.At, DiagnosticCodes.StepsPattern,
                        $"bar {k + 1}: the {other.Drum} row has {Beats(other.Row.BeatsPerBar[k])} and the {first.Drum} row "
                        + $"{Beats(first.Row.BeatsPerBar[k])} — a beat is a run of steps between spaces");
                    break;
                }
        }

        static string Bars(int n) => n == 1 ? "1 bar" : $"{n} bars";
        static string Beats(int n) => n == 1 ? "1 beat" : $"{n} beats";
    }

    /// <summary>Puts the row's tokens in the stream where its string token stood. The
    /// string's own trivia goes to the first and last of them, so the widths add up.</summary>
    private void SpliceRow(SyntaxToken literal, StepsPattern.Row row)
    {
        var tokens = new List<SyntaxToken>(row.Pieces.Count);
        for (int i = 0; i < row.Pieces.Count; i++)
        {
            var p = row.Pieces[i];
            GreenNode? trailing = Trivia(p.Trailing);
            if (i == row.Pieces.Count - 1)
                trailing = Join(trailing, literal.TrailingTrivia);
            var leading = i == 0 ? literal.LeadingTrivia : null;
            tokens.Add(SyntaxToken.Spelled(p.Kind, p.Spelling, p.Source, leading, trailing));
        }
        _tokens.RemoveAt(_position);
        _tokens.InsertRange(_position, tokens);

        static GreenNode? Trivia(List<(SyntaxKind Kind, System.Text.StringBuilder Text)> runs)
        {
            if (runs.Count == 0)
                return null;
            if (runs.Count == 1)
                return new SyntaxTrivia(runs[0].Kind, runs[0].Text.ToString());
            return new SyntaxTriviaList([.. runs.Select(r => (GreenNode)new SyntaxTrivia(r.Kind, r.Text.ToString()))]);
        }

        static GreenNode? Join(GreenNode? a, GreenNode? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return new SyntaxTriviaList([a, b]);
        }
    }
}
