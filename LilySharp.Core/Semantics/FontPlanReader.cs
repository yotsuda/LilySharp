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
        problems = found;
        var builder = new TextFontPlan.Builder();
        builder.Embed(font.Embedded);

        if (!font.IsBlock)
        {
            // The one-line `font "NAME"` was removed 2026-08-18; the parser reports it
            // (LYS8007) and keeps its tokens so no source position slides. It binds
            // NOTHING here. (A blockless NAMED node — a score's pure reference — reads
            // through ReadReference instead, never here.)
            //
            // ⚠️ Applying the old meaning anyway would be worse than either choice: the
            // score would engrave in the named face while the editor underlined the line
            // as an error, and the writer would have no reason to believe the message.
            // A refused directive has to be refused all the way through.
            return builder.Build();
        }

        ReadEntriesInto(builder, font, found);
        return builder.Build();
    }

    /// <summary>True when <paramref name="node"/> stands inside a score block — where a
    /// fonts or paper node is a REFERENCE rather than a declaration.</summary>
    internal static bool IsInsideRender(SyntaxNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p is RenderDeclarationSyntax)
                return true;
        return false;
    }

    /// <summary>Every named top-level fonts declaration, in document order.</summary>
    internal static IReadOnlyList<FontDeclarationSyntax> NamedDeclarations(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<FontDeclarationSyntax>()
            .Where(f => f.NameToken != null && f.IsBlock && !IsInsideRender(f))];

    /// <summary>
    /// Resolves a score reference's name to its top-level declaration. ONE HOME for the
    /// unknown-name sentence — the validator reports what this hands back, and the
    /// collector discards it, so the two cannot disagree about which names exist.
    /// </summary>
    /// <returns>False when the name resolves to nothing (or the node has no name — the
    /// parser already spoke); <paramref name="problem"/> carries the sentence when the
    /// name is unknown.</returns>
    internal static bool TryResolve(SyntaxNode root, FontDeclarationSyntax reference,
        out FontDeclarationSyntax? declaration, out Problem? problem)
    {
        declaration = null;
        problem = null;
        if (reference.NameToken is not { } nameToken)
            return false;
        string name = nameToken.Text;
        var declarations = NamedDeclarations(root);
        declaration = declarations.FirstOrDefault(d => d.NameToken!.Text == name);
        if (declaration != null)
            return true;
        var declared = declarations.Select(d => d.NameToken!.Text)
            .Distinct(StringComparer.Ordinal).ToList();
        problem = new Problem(nameToken.Span, DiagnosticCodes.UnknownFontsBlockName,
            $"No fonts block is named '{name}'." + (declared.Count > 0
                ? " Declared: " + string.Join(", ", declared) + "."
                : $" Declare one at the top level: fonts {name} {{ serif \"Georgia\" }}."),
            IsError: true);
        return false;
    }

    /// <summary>
    /// The plan a score's <c>fonts NAME [{ … }]</c> reference asks for: the named
    /// block's entries with the reference's own entries laid over them — the same
    /// reading as ONE merged block, so the last same-key entry wins (the override) and
    /// the narrower spelling wins WHICHEVER block it came from. A reference that
    /// resolves to nothing keeps <paramref name="fallback"/>: refused all the way
    /// through, like every other refused directive.
    /// </summary>
    /// <remarks>
    /// ⚠️ Entry problems are NOT surfaced here — each block's own entries are validated
    /// where the block stands (the validator walks every node) — and the cross-block
    /// same-key repeat is deliberately not a warning: overriding a key is the whole
    /// point of the override block.
    /// </remarks>
    internal static TextFontPlan ReadReference(SyntaxNode root, FontDeclarationSyntax reference,
        TextFontPlan fallback)
    {
        if (!TryResolve(root, reference, out var declaration, out _))
            return fallback;
        var builder = new TextFontPlan.Builder();
        builder.Embed(declaration!.Embedded || reference.Embedded);
        var discard = new List<Problem>();
        ReadEntriesInto(builder, declaration, discard);
        if (reference.IsBlock)
            ReadEntriesInto(builder, reference, discard);
        return builder.Build();
    }

    /// <summary>Reads one block's entries into <paramref name="builder"/> — the loop
    /// <see cref="Read"/> and <see cref="ReadReference"/> share, so a directive and a
    /// merged reference cannot disagree about what an entry means. Duplicate-key
    /// detection is scoped to the one block: a repeat ACROSS blocks is an override.</summary>
    private static void ReadEntriesInto(TextFontPlan.Builder builder, FontDeclarationSyntax font, List<Problem> found)
    {
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
    }
}
