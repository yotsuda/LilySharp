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

using System.Globalization;

namespace LilySharp.Core.Syntax;

/// <summary>
/// A value written in the source, carried as a TYPE rather than as the token's text.
/// </summary>
/// <remarks>
/// <para>
/// Before this type existed, an <c>override</c> value crossed the whole compiler as a
/// <c>string</c> — parser token text → <c>Dictionary&lt;string, Dictionary&lt;string,string&gt;&gt;</c>
/// → <c>double.TryParse</c> at each use site, five of them, one of which parsed the value
/// without going through the resolver at all. The audit that counted those sites is
/// <c>docs/VALUE_SITE_AUDIT.md</c>; its §2 is why this type exists. A string pipeline
/// cannot carry an expression, so the value has to become a type before the grammar can
/// grow one.
/// </para>
/// <para>
/// ⚠️ <b>The grammar cannot write every case.</b> The lexer has no decimal number
/// (<c>ScanNumber</c>, <c>Parser\Lexer.cs</c>, consumes digits only), so a <c>.lys</c>
/// source can produce <see cref="Int"/> (including the negative form the parser folds
/// into one token), <see cref="Str"/> and <see cref="Symbol"/> — never <see cref="Real"/>.
/// <see cref="Real"/> is reachable only from C# (the staff-spacing and note-collision
/// tests build one directly), and <see cref="Bool"/> only via <see cref="AsBool"/>'s
/// reading of a symbol. Measured 2026-08-15: <b>0 of the corpus books</b> write a
/// fractional override value. Adding a decimal literal is a GRAMMAR change and belongs
/// to its own session, not to this one.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Str"/> is not a number.</b> The string pipeline this replaces
/// stripped the quotes before storing, so <c>= "10"</c> used to answer 10 to
/// <c>GetDouble</c>. It no longer does — that was the untyped plumbing leaking, not a
/// rule anyone wrote. Production reads exactly one string property (<c>color</c>), so
/// nothing in the corpus depends on the old answer.
/// </para>
/// </remarks>
public abstract record LysValue
{
    private LysValue() { }

    /// <summary>An integer, including the negative form (<c>= -5</c>).</summary>
    public sealed record Int(long V) : LysValue;

    /// <summary>A real number. Not writable in the grammar — see the type's remarks.</summary>
    public sealed record Real(double V) : LysValue;

    /// <summary>A quoted string's CONTENT (the quotes are not part of the value).</summary>
    public sealed record Str(string V) : LysValue;

    /// <summary>A boolean. Not writable directly; <see cref="AsBool"/> reads one off a symbol.</summary>
    public sealed record Bool(bool V) : LysValue;

    /// <summary>A bare identifier used as a value: <c>up</c>, <c>red</c>, <c>treble</c>, <c>8vb</c>.</summary>
    public sealed record Symbol(string Name) : LysValue;

    /// <summary>
    /// Builds the value a value-position token denotes.
    /// </summary>
    /// <remarks>
    /// The two normalisations the string pipeline did at its single collection site are
    /// kept here, because they are properties of the TOKEN, not of the consumer:
    /// a quoted string yields its content, and a folded negative number
    /// (<c>CombineNegativeNumber</c> keeps the interior whitespace of <c>"- 5"</c> so the
    /// tree round-trips) drops that whitespace before it is read as a number.
    /// </remarks>
    public static LysValue FromToken(SyntaxKind kind, string text)
    {
        if (kind == SyntaxKind.StringLiteral)
            return new Str(text.Trim('"'));

        if (kind == SyntaxKind.IntegerLiteral)
        {
            var compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            if (long.TryParse(compact, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out long n))
                return new Int(n);
            // A malformed number token (the parser's error-recovery arm can hand one
            // over) keeps its text rather than throwing: a broken override must not
            // take the render pass down with it. Same reasoning as DurationSyntax.Value.
            return new Symbol(compact);
        }

        return new Symbol(text);
    }

    /// <summary>The value as a double, or null when it does not denote a number.</summary>
    public double? AsDouble => this switch
    {
        Int i => i.V,
        Real r => r.V,
        _ => null,
    };

    /// <summary>
    /// The value as an int, or null when it does not denote one. A <see cref="Real"/>
    /// is NOT truncated — <c>int.TryParse("3.5")</c> failed before this type existed too.
    /// </summary>
    public int? AsInt => this switch
    {
        Int i when i.V >= int.MinValue && i.V <= int.MaxValue => (int)i.V,
        _ => null,
    };

    /// <summary>
    /// The value as a bool, or null when it does not denote one. The spellings are the
    /// ones the string pipeline accepted: true/1/yes and false/0/no, case-insensitively.
    /// </summary>
    public bool? AsBool => this switch
    {
        Bool b => b.V,
        Int i when i.V == 1 => true,
        Int i when i.V == 0 => false,
        Symbol s => Word(s.Name),
        Str s => Word(s.V),
        _ => null,
    };

    private static bool? Word(string text) => text.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => null,
    };

    /// <summary>
    /// The value as text — what a consumer that wants the written word sees
    /// (<c>color = red</c>, <c>direction = up</c>). Numbers render invariantly.
    /// </summary>
    public string AsText => this switch
    {
        Int i => i.V.ToString(CultureInfo.InvariantCulture),
        Real r => r.V.ToString(CultureInfo.InvariantCulture),
        Str s => s.V,
        Bool b => b.V ? "true" : "false",
        Symbol s => s.Name,
        _ => "",
    };
}
