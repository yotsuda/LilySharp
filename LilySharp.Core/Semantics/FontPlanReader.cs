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

using LilySharp.Core.Rendering;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Turns a <c>font</c> directive into a <see cref="TextFontPlan"/>, and says what was
/// wrong with it.
/// </summary>
/// <remarks>
/// ONE HOME FOR THE READING. Two callers want this — the collector, which needs the plan
/// and no diagnostics, and the validator, which needs the diagnostics and no plan — and
/// if each parsed the entries itself they would eventually disagree about which
/// directives are legal. So the reading happens once and hands back both.
/// </remarks>
internal static class FontPlanReader
{
    /// <summary>Something the directive got wrong, with the span to point at.</summary>
    /// <param name="Span">Where to underline.</param>
    /// <param name="Code">A <see cref="DiagnosticCodes"/> constant.</param>
    /// <param name="Message">The prose, ASCII punctuation only (these reach the CLI).</param>
    /// <param name="IsError">False for a warning.</param>
    internal readonly record struct Problem(TextSpan Span, string Code, string Message, bool IsError);

    /// <summary>
    /// Reads <paramref name="font"/> into a plan.
    /// </summary>
    /// <param name="font">The directive.</param>
    /// <param name="problems">Everything wrong with it, in source order.</param>
    /// <returns>
    /// The plan the directive asks for. Entries that produced an ERROR are left out of
    /// it, so a score with one bad key still gets the bindings it spelled correctly.
    /// </returns>
    internal static TextFontPlan Read(FontDeclarationSyntax font, out IReadOnlyList<Problem> problems)
    {
        var found = new List<Problem>();
        var builder = new TextFontPlan.Builder();
        builder.Embed(font.Embedded);

        if (!font.IsBlock)
        {
            // font "NAME" [embedded] — the whole-document shorthand. One name only: the
            // one-liner has no way to spell a chain, and inventing one here
            // (font "A" "B") would be a second syntax for what the block already says.
            if (font.FontName is { Length: > 0 } name)
                builder.Everything([name]);
            problems = found;
            return builder.Build();
        }

        var boundKeys = new Dictionary<string, TextSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in font.Entries)
        {
            var span = entry.KeyToken.Span;
            if (!TextRoles.TryParseKey(entry.Key, out var role, out var group, out var family))
            {
                found.Add(new Problem(span, DiagnosticCodes.UnknownFontRole,
                    $"'{entry.Key}' is not a text role, a role group, or a generic family. " +
                    "Known keys: " + string.Join(", ", TextRoles.AllKeySpellings()) + ".",
                    IsError: true));
                continue;
            }

            // A canonical key so `lyricText` and `lyrictext` count as the same binding.
            string canonical = role is { } r ? TextRoles.Spelling(r)
                : group is { } g ? TextRoles.Spelling(g)
                : TextRoles.Spelling(family!.Value);
            if (boundKeys.TryGetValue(canonical, out var earlier))
                found.Add(new Problem(earlier, DiagnosticCodes.DuplicateFontBinding,
                    $"This '{canonical}' is overwritten by a later '{canonical}' in the same " +
                    "font block; only the last one takes effect.", IsError: false));
            boundKeys[canonical] = span;

            bool hasNames = entry.Names.Count > 0;
            if (!hasNames && entry.Family == null)
            {
                // A GENERIC FAMILY takes only quoted names — pointing serif at sans is a
                // re-classification, not a face choice, and no role reads it — so its
                // message must not offer the family form the other keys accept.
                found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                    family != null
                        ? $"'{canonical}' names no face. Write one or more quoted names, " +
                          $"e.g. {canonical} \"Georgia\"."
                        : $"'{canonical}' names no face. Write one or more quoted names, " +
                          $"e.g. {canonical} \"Georgia\", or a generic family, " +
                          $"e.g. {canonical} serif.",
                    IsError: true));
                boundKeys.Remove(canonical);
                continue;
            }
            if (hasNames && entry.Names.Any(n => n.Length == 0))
            {
                found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                    $"'{canonical}' has an empty face name.", IsError: true));
                boundKeys.Remove(canonical);
                continue;
            }

            // A generic family may only take face NAMES: `serif sans` would say "measure
            // the serif roles against the sans face", which is not a face choice at all
            // but a re-classification, and no role reads it.
            if (family is { } f)
            {
                if (!hasNames)
                {
                    found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                        $"'{canonical}' is a generic family and takes quoted face names, " +
                        $"not another family. Write {canonical} \"Georgia\".", IsError: true));
                    boundKeys.Remove(canonical);
                    continue;
                }
                builder.Family(f, entry.Names);
                continue;
            }
            if (group is { } gg)
            {
                if (hasNames)
                    builder.Group(gg, entry.Names);
                else
                    builder.Group(gg, entry.Family!.Value);
                continue;
            }
            if (hasNames)
                builder.Role(role!.Value, entry.Names);
            else
                builder.Role(role!.Value, entry.Family!.Value);
        }

        problems = found;
        return builder.Build();
    }
}
