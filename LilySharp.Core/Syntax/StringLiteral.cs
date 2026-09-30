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
using System.Globalization;
using System.Text;

namespace LilySharp.Core.Syntax;

/// <summary>
/// A string literal's CONTENT, and the spelling that writes a value back: C#'s rules.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN: the owner's decision (2026-09-30) — Lily#'s strings follow C#'s grammar:
/// a regular literal <c>"…"</c> decodes <c>\" \' \\ \0 \a \b \f \n \r \t \v</c>,
/// <c>\uXXXX</c>, <c>\UXXXXXXXX</c> and <c>\x</c> + 1–4 hex digits, and any other escape is
/// an error (LYS0036, C#'s CS1009); a verbatim literal <c>@"…"</c> takes every character as
/// written, with <c>""</c> standing for one quote.
/// <para>
/// ONE HOUSE. Until 2026-09-30 some two dozen readers took a literal's content with
/// <c>Trim('"')</c> or <c>[1..^1]</c>: no escape was decoded, and <c>Trim</c> also ate an
/// escaped quote at the end, so <c>@text("say \"hi\"")</c> printed <c>say \"hi\</c>.
/// </para>
/// </remarks>
public static class StringLiteral
{
    /// <summary>Whether <paramref name="text"/> is a quoted literal — <c>"…"</c> or
    /// <c>@"…"</c> (a lone opening quote counts: an unterminated literal is still one).</summary>
    public static bool IsQuoted(string text)
        => text.Length >= 1 && (text[0] == '"' || (text.Length >= 2 && text[0] == '@' && text[1] == '"'));

    /// <summary>Whether the literal is verbatim (<c>@"…"</c>).</summary>
    public static bool IsVerbatim(string text) => text.Length >= 2 && text[0] == '@' && text[1] == '"';

    /// <summary>Whether <paramref name="text"/> is ONE closed literal: its closing quote is its
    /// last character. <c>@"</c> (the opening quote only), <c>"a\"</c> (the last quote is
    /// escaped) and <c>"a" "b"</c> (two literals) are not.</summary>
    /// <remarks>Until 2026-09-30 six readers spelled this as "opens with a quote and ends with
    /// <c>"</c>", which took all three of those for one literal.</remarks>
    public static bool IsClosed(string text) => IsQuoted(text) && ClosingQuote(text) == text.Length - 1;

    /// <summary>The index of the closing quote, read as <see cref="Value"/> reads it; −1 when
    /// the literal is unterminated.</summary>
    private static int ClosingQuote(string text)
    {
        bool verbatim = IsVerbatim(text);
        for (int i = verbatim ? 2 : 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++; // "" is one quote
                    continue;
                }
                return i;
            }
            if (c == '\\' && !verbatim)
                i++; // the escaped character is never the closing quote
        }
        return -1;
    }

    /// <summary>The literal's decoded content; <paramref name="text"/> itself when it is not
    /// quoted. An invalid escape keeps its characters (LYS0036 reports it).</summary>
    public static string Value(string text) => Decode(text, null);

    /// <summary>The invalid escapes in a literal, as (offset in the literal, length, message).</summary>
    public static List<(int Offset, int Length, string Message)> Errors(string text)
    {
        var errors = new List<(int, int, string)>();
        Decode(text, errors);
        return errors;
    }

    /// <summary>A value written back as a regular literal, quotes included: <c>\</c> and
    /// <c>"</c> escaped, and every control character as its escape.</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    private static string Decode(string text, List<(int, int, string)>? errors)
    {
        if (!IsQuoted(text))
            return text;
        bool verbatim = IsVerbatim(text);
        int i = verbatim ? 2 : 1;
        var sb = new StringBuilder(text.Length);
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"')
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                break; // the closing quote
            }
            if (c != '\\' || verbatim)
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (i + 1 >= text.Length)
            {
                sb.Append('\\'); // a backslash at the end of an unterminated literal
                break;
            }
            char e = text[i + 1];
            int consumed = 2;
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case '\\': sb.Append('\\'); break;
                case '0': sb.Append('\0'); break;
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case 'u':
                case 'U':
                case 'x':
                {
                    int max = e == 'u' ? 4 : e == 'U' ? 8 : 4;
                    int min = e == 'x' ? 1 : max;
                    int n = 0;
                    while (n < max && i + 2 + n < text.Length && IsHex(text[i + 2 + n]))
                        n++;
                    if (n < min)
                    {
                        errors?.Add((i, 2 + n, $"'\\{e}' needs {(e == 'x' ? "1 to 4" : max.ToString(CultureInfo.InvariantCulture))} hexadecimal digit(s)."));
                        sb.Append(text, i, 2 + n);
                    }
                    else
                    {
                        int code = int.Parse(text.AsSpan(i + 2, n), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        if (e == 'U' && code > 0x10FFFF)
                        {
                            errors?.Add((i, 2 + n, $"'\\U{text.Substring(i + 2, n)}' is not a Unicode character."));
                            sb.Append(text, i, 2 + n);
                        }
                        else
                            sb.Append(char.ConvertFromUtf32(code is >= 0xD800 and <= 0xDFFF ? 0xFFFD : code));
                    }
                    consumed = 2 + n;
                    break;
                }
                default:
                    errors?.Add((i, 2, $"'\\{e}' is not an escape sequence. A string follows C#'s escapes "
                        + "(\\\" \\\\ \\n \\t \\uXXXX …); write '\\\\' for a backslash, or use a verbatim "
                        + "string @\"…\", which takes a backslash as written."));
                    sb.Append('\\').Append(e);
                    break;
            }
            i += consumed;
        }
        return sb.ToString();
    }

    private static bool IsHex(char c) => c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
}
