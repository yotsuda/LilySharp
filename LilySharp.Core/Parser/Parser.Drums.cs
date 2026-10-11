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
    /// <summary>The word that opens a drum chart — claimed only before a <c>{</c>.</summary>
    internal const string DrumsWord = "drums";

    /// <summary>
    /// A drum chart (owner's design 2026-10-11): <c>drums { hats  back  down kick  { r2 sn4 sn } }</c>.
    /// Each LINE is a phrase name or a <c>{ … }</c> block, optionally led by <c>up</c> or
    /// <c>down</c>, and the lines play together from the chart's start. The page merges them
    /// by limb — hands on stems up, feet on stems down — so the lines are written as a
    /// drummer thinks (a hi-hat pattern, a backbeat, a kick) and drawn as a drummer reads.
    /// </summary>
    /// <remarks>
    /// The chart is the SAME node <c>voice { } { }</c> builds — a
    /// <see cref="ParallelExpressionGreen"/>, one block a line — so every walk that reads
    /// voices (the exporters, MIDI, the validators) reads the lines as voices with nothing
    /// new to learn. Only its opener differs: the <c>drums</c> identifier re-kinded to
    /// <see cref="SyntaxKind.DrumsOpener"/>, which <see cref="ParallelExpressionSyntax.IsDrums"/>
    /// reads and the page's collector merges on. A phrase line is wrapped in a block of
    /// zero-width braces (the recovered <c>&lt;&lt; … &gt;&gt;</c> voice's device), and an
    /// <c>up</c>/<c>down</c> word is re-kinded to <see cref="SyntaxKind.DrumsLineDirection"/> so
    /// <see cref="ParallelExpressionSyntax.NamedVoices"/> never reads it as a voice's name.
    /// </remarks>
    private ParallelExpressionGreen ParseDrumsBlock()
    {
        if (_voiceBodyDepth > 0)
            _diagnostics.Error(new TextSpan(_textPosition, Current.FullWidth), DiagnosticCodes.NestedVoiceBlock,
                "a drums { } chart cannot stand inside a voice or another chart's line — its lines are "
                + "already played together; write it beside the voices, not inside one.");
        var word = Advance();
        var opener = new SyntaxToken(SyntaxKind.DrumsOpener, word.Text, word.LeadingTrivia, word.TrailingTrivia);
        var children = new List<GreenNode?> { Expect(SyntaxKind.OpenBrace) };

        while (!Check(SyntaxKind.CloseBrace) && !Check(SyntaxKind.EndOfFile))
        {
            if (Check(SyntaxKind.Identifier) && Current.Text is "up" or "down"
                && (Peek().Kind == SyntaxKind.OpenBrace || IsDrumsLineName(Peek())))
            {
                var dir = Advance();
                children.Add(new SyntaxToken(SyntaxKind.DrumsLineDirection, dir.Text, dir.LeadingTrivia, dir.TrailingTrivia));
            }

            _voiceBodyDepth++;
            if (Check(SyntaxKind.OpenBrace))
                children.Add(ParseMusicBlock());
            else if (IsDrumsLineName(Current))
                children.Add(WrappedLine(ParseBareVariableReference()));
            else
            {
                // A note, a rest, a bar line… written straight into the chart. It is kept as
                // a line of its own so the source keeps its width and the music still sounds.
                _diagnostics.Error(new TextSpan(_textPosition, Current.FullWidth), DiagnosticCodes.DrumsLineNotALine,
                    "a drums { } chart holds LINES — a phrase name or a { … } block each, played "
                    + "together: drums { hats  back  { bd4 r bd r } }. Put the notes inside braces.");
                var item = ParseMusicItem();
                if (item != null)
                    children.Add(WrappedLine(item));
                else
                    children.Add(Advance());
            }
            _voiceBodyDepth--;
        }

        var close = Expect(SyntaxKind.CloseBrace);
        return new ParallelExpressionGreen(opener, [.. children], close);

        static MusicBlockGreen WrappedLine(GreenNode item) => new(
            new SyntaxToken(SyntaxKind.OpenBrace, "", null, null), [item],
            new SyntaxToken(SyntaxKind.CloseBrace, "", null, null));
    }

    /// <summary>True for a token that names a phrase as a chart line: an identifier that is
    /// not a drum (<c>hh</c> is a note, not a line), or a clef/dynamic word
    /// (<see cref="IsBareReferenceKind"/>).</summary>
    private static bool IsDrumsLineName(SyntaxToken token)
        => token.Kind == SyntaxKind.Identifier
            ? !DrumNameRegistry.Contains(token.Text) && token.Text is not ("q" or ShapeChordWord)
            : IsBareReferenceKind(token.Kind);
}
