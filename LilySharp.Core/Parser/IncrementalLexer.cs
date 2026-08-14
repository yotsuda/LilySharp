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

using LilySharp.Core.Syntax.InternalSyntax;

namespace LilySharp.Core.Parser;

/// <summary>
/// Incremental lexing: builds the token stream for an edited text by reusing
/// the previous stream outside the damaged region and re-lexing only the
/// damage itself.
/// </summary>
/// <remarks>
/// Soundness rests on the lexer being a pure function of (text, offset) with
/// no carried state:
/// <list type="bullet">
/// <item><b>Prefix</b> — a token whose span ends more than two characters
/// before the edit lexes from unchanged text AND its end was decided by
/// unchanged characters, so it is reused verbatim. Two is the widest lookahead
/// past a token's end in the lexer, and there are two of them: the trivia
/// scanner (<c>//</c>, <c>/*</c>, <c>\r\n</c>) and the number scanner, which
/// reads <c>.</c> plus one digit to decide whether an integer continues into a
/// decimal. Typing the <c>.</c> of <c>3.5</c> falls inside the guard, so the
/// <c>3</c> is re-lexed rather than reused.</item>
/// <item><b>Damage</b> — re-lexed from the first unreusable token's start.</item>
/// <item><b>Suffix</b> — once the fresh lexer reaches a token start at or
/// beyond the damage end that coincides with an OLD token start (shifted by
/// the edit's length delta), every following token lexes from an identical
/// suffix and is spliced as-is; greens are position-free. If the lexer never
/// lands on a shared boundary (an edit that opens an unterminated
/// <c>/*</c> rewrites everything), it simply re-lexes to the end.</item>
/// </list>
/// </remarks>
internal static class IncrementalLexer
{
    /// <summary>
    /// Two-character guard: the widest lookahead any scanner uses past the end of
    /// what it has consumed — the trivia scanner deciding where trailing trivia
    /// stops, and <c>ScanNumber</c> deciding whether <c>.</c> + a digit continues
    /// the number. Widen this if a third scanner ever looks further.
    /// </summary>
    private const int Guard = 2;

    public static List<SyntaxToken> Splice(
        IReadOnlyList<SyntaxToken> oldTokens, string newText,
        int damageStart, int damageOldEnd, int delta)
    {
        var result = new List<SyntaxToken>(oldTokens.Count + 8);

        // ---- 1. Reusable prefix: tokens ending strictly before the guard. ----
        int oldPos = 0;
        int index = 0;
        while (index < oldTokens.Count - 1) // never pre-consume the old EOF
        {
            var token = oldTokens[index];
            int end = oldPos + token.FullWidth;
            if (end + Guard > damageStart)
                break;
            result.Add(token);
            oldPos = end;
            index++;
        }

        // ---- 2. Re-lex the damage from the first unreusable boundary. ----
        // Old token start positions at-or-after the damage end, for resync.
        // (Built lazily from the remaining old tokens.)
        var oldStarts = new Dictionary<int, int>(); // old start position -> token index
        {
            int pos = oldPos;
            for (int i = index; i < oldTokens.Count; i++)
            {
                // First-wins: the zero-width EOF token can share its start
                // with the preceding token's end; resync must splice from the
                // EARLIEST token at a given boundary.
                if (pos >= damageOldEnd && !oldStarts.ContainsKey(pos))
                    oldStarts[pos] = i;
                pos += oldTokens[i].FullWidth;
            }
        }

        int damageNewEnd = damageOldEnd + delta;
        var lexer = new Lexer(newText, oldPos);
        while (true)
        {
            int tokenStart = lexer.Position;

            // ---- 3. Resync: splice the old suffix once boundaries align. ----
            if (tokenStart >= damageNewEnd + Guard
                && oldStarts.TryGetValue(tokenStart - delta, out int oldIndex))
            {
                for (int i = oldIndex; i < oldTokens.Count; i++)
                    result.Add(oldTokens[i]);
                return result;
            }

            if (tokenStart >= newText.Length)
            {
                result.Add(GreenCache.GetToken(Syntax.SyntaxKind.EndOfFile, "", null, null));
                return result;
            }

            result.Add(lexer.ScanToken());
        }
    }
}
