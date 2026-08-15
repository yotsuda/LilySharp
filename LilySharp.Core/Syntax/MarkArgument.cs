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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace LilySharp.Core.Syntax;

/// <summary>
/// One argument of an <c>@name(…)</c> annotation: the VALUE it denotes and the TEXT
/// that was written for it.
/// </summary>
/// <remarks>
/// <para>
/// Both, not one. <c>docs/VALUE_SITE_AUDIT.md</c> §9.2 measured the reason:
/// <c>@frame(032010)</c> is a fret POSITION STRING, one character per string, and its
/// value is <c>Int(32010)</c> — the leading zero, i.e. the sixth string, is gone the
/// moment the argument becomes only a value. <c>@chord(c:m7)</c> is worse: its
/// <c>c</c> arrives as a <c>PitchC</c> token because the argument borrows the music
/// grammar, so it is a sub-language and not a value at all. A <see cref="LysValue"/>
/// alone would silently break both; the written text alone would throw away the typing
/// that the whole ⒯ workstream exists to add. So an argument carries both, and each
/// consumer says which one it means.
/// </para>
/// <para>
/// ⚠️ <b>The run, not the token, is the argument.</b> This is where this reading and
/// the older <see cref="MusicMarkSyntax.MarkName"/> differ: <c>MarkName</c> joins every
/// token with a '.', so <c>@chord(c:m7)</c> becomes <c>"chord.c.:.m7"</c> and the chord
/// parser then splits on '.' and rejoins with "" to get its <c>"c:m7"</c> back
/// (§9.3 — the value makes THREE round trips through a string). Here the tokens of
/// <c>c:m7</c> are ADJACENT in the source, so they are one argument whose
/// <see cref="Text"/> is already <c>"c:m7"</c>. Whitespace and ',' separate arguments;
/// nothing else does.
/// </para>
/// <para>
/// That rule was chosen by measurement, not taste: across the 80-book corpus and 219
/// fixtures, the only <c>@name(…)</c> argument that contains whitespace is the
/// genuinely multi-valued figured bass (<c>@fig(5 3)</c>, <c>@fig(6 4)</c>,
/// <c>@fig(6 s)</c>). Every other written argument is one unbroken run.
/// </para>
/// </remarks>
/// <param name="Text">
/// The source text of the argument, exactly as written — quotes, leading zeros and
/// sub-language punctuation included. Because an argument is by definition a run of
/// adjacent tokens, this is the source slice, not a reconstruction of it.
/// </param>
/// <param name="Value">
/// What the argument denotes when it is a single value token, or <see langword="null"/>
/// when it is a multi-token run (the sub-language case). Never trust this one alone for
/// an argument whose spelling is its meaning — see the remarks.
/// </param>
public sealed record MarkArgument(string Text, LysValue? Value)
{
    /// <summary>
    /// Reads the tokens BETWEEN the parentheses into arguments, in one pass.
    /// </summary>
    /// <remarks>
    /// The caller passes the interior only: no '@', no name, no '(' or ')', and no
    /// trailing '.up' / '.down' placement (that is not an argument — it is a qualifier
    /// on the annotation, and <see cref="MusicMarkSyntax.ForcedAbove"/> answers it).
    /// A ',' is a separator and never appears in an argument's <see cref="Text"/>.
    /// </remarks>
    public static ImmutableArray<MarkArgument> FromTokens(IReadOnlyList<SyntaxTokenNode> tokens)
    {
        if (tokens.Count == 0)
            return [];

        var arguments = ImmutableArray.CreateBuilder<MarkArgument>();
        var run = new StringBuilder();
        SyntaxTokenNode? only = null;   // the run's single token, while it has one
        int runEnd = -1;                // source offset just past the run so far

        void Flush()
        {
            if (run.Length == 0)
                return;
            // A one-token run denotes a value; a longer one is a sub-language and
            // denotes nothing this type can name, so it reports its text and null.
            arguments.Add(new MarkArgument(
                run.ToString(),
                only is null ? null : LysValue.FromToken(only.Kind, only.Text)));
            run.Clear();
            only = null;
        }

        foreach (var token in tokens)
        {
            if (token.Kind == SyntaxKind.Comma)
            {
                Flush();
                runEnd = -1;
                continue;
            }

            // Span excludes trivia, so "the previous token ended exactly where this one
            // begins" is precisely "nothing was written between them".
            if (token.Span.Start != runEnd)
            {
                Flush();
                only = token;
            }
            else
            {
                only = null;   // the run has grown past one token
            }

            run.Append(token.Text);
            runEnd = token.Span.End;
        }

        Flush();
        return arguments.ToImmutable();
    }
}
