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
        // The previous entry's canonical key when it was a role or a group — what the old
        // redirect spelling (`chordName serif`) meant to point at, so the refusal of the
        // orphaned `serif` can name the `as` form to write instead.
        string? previousRoleOrGroup = null;
        foreach (var entry in font.Entries)
        {
            var span = entry.KeyToken.Span;
            if (!TextRoles.TryParseKey(entry.Key, out var role, out var group, out var family))
            {
                // An attribute word that opened an entry stands before any key
                // (`fonts { step +1 }`): it belongs to nothing.
                if (TextRoles.IsAttributeWord(entry.Key))
                    found.Add(new Problem(span, DiagnosticCodes.FontAttributeMisplaced,
                        $"'{entry.Key}' is an attribute and follows a key; here it follows " +
                        $"none. Write the role first, e.g. mark {entry.Key}" +
                        (entry.Key.Equals("step", StringComparison.OrdinalIgnoreCase) ? " +1" :
                         entry.Key.Equals("size", StringComparison.OrdinalIgnoreCase) ? " 3" :
                         entry.Key.Equals("as", StringComparison.OrdinalIgnoreCase) ? " sans" : "") + ".",
                        IsError: true));
                else
                    found.Add(new Problem(span, DiagnosticCodes.UnknownFontRole,
                        $"'{entry.Key}' is not a text role, a role group, or a generic family. " +
                        "Known keys: " + string.Join(", ", TextRoles.AllKeySpellings()) + ". " +
                        "After a key, the attributes are: " +
                        string.Join(", ", TextRoles.AttributeWords) + ".",
                        IsError: true));
                previousRoleOrGroup = null;
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

            var attrs = ReadAttributes(entry, canonical, family != null, found);
            bool hasNames = entry.Names.Count > 0;
            bool hasAttributes = attrs.Redirect != null || attrs.Step != null
                || attrs.Size != null || attrs.Style != null;

            if (hasNames && entry.Names.Any(n => n.Length == 0))
            {
                found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                    $"'{canonical}' has an empty face name.", IsError: true));
                boundKeys.Remove(canonical);
                previousRoleOrGroup = null;
                continue;
            }

            // A generic family takes only quoted NAMES: it is the face table the roles fall
            // back to, so a redirect (`serif as sans`) would be a re-classification no role
            // reads, and a size or style on it would reach nothing (refused in
            // ReadAttributes). Its message must not offer the forms the other keys accept.
            if (family is { } f)
            {
                if (!hasNames)
                {
                    // The old redirect spelling — `chordName serif` — lands here as a bare
                    // `serif` right after a role or group: answer with the spelling that
                    // replaced it rather than with "names no face".
                    string hint = previousRoleOrGroup is { } prev && !hasAttributes
                        ? $" To point '{prev}' at the {canonical} family write: {prev} as {canonical}."
                        : "";
                    found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                        $"'{canonical}' is a generic family and takes quoted face names, " +
                        $"e.g. {canonical} \"Georgia\"." + hint, IsError: true));
                    boundKeys.Remove(canonical);
                    previousRoleOrGroup = null;
                    continue;
                }
                builder.Family(f, entry.Names);
                previousRoleOrGroup = null;
                continue;
            }

            previousRoleOrGroup = canonical;
            if (!hasNames && !hasAttributes)
            {
                found.Add(new Problem(span, DiagnosticCodes.FontBindingMissingValue,
                    $"'{canonical}' names nothing. Write one or more quoted faces, " +
                    $"e.g. {canonical} \"Georgia\"; a generic family to follow, " +
                    $"e.g. {canonical} as serif; a size, e.g. {canonical} step -1; " +
                    $"or a style, e.g. {canonical} italic.",
                    IsError: true));
                boundKeys.Remove(canonical);
                continue;
            }

            var binding = new TextFontPlan.Binding(
                [.. entry.Names], attrs.Redirect, attrs.Step, attrs.Size, attrs.Style);
            if (group is { } gg)
            {
                WarnWhenThePageIgnoresIt(gg, attrs, span, found);
                builder.Group(gg, binding);
            }
            else
            {
                WarnWhenThePageIgnoresIt(role!.Value, attrs, span, found);
                builder.Role(role!.Value, binding);
            }
        }
    }

    /// <summary>What one entry's attribute tokens asked for, once read.</summary>
    private readonly record struct Attributes(
        TextFontFamily? Redirect, double? Step, double? Size, FontStyle? Style);

    /// <summary>The range a <c>step</c> may take: ±12 is a factor of four either way,
    /// past which no text role on a page is still that role.</summary>
    /// <remarks>
    /// LILYSHARP-OWN: a limit of the LANGUAGE, not a geometry — LilyPond's <c>font-size</c>
    /// is unbounded, and these bounds exist so a typo (<c>step +100</c>) is refused where it
    /// is written rather than drawn as a page-wide word. They are read by nothing but this
    /// reader and the diagnostic text, and observed by FontAttributeTests; they disappear
    /// only if the owner decides a wider range is wanted.
    /// </remarks>
    internal const double MaxStep = 12.0;

    /// <summary>The range a <c>size</c> may take, in staff spaces. LILYSHARP-OWN, as
    /// <see cref="MaxStep"/> is: half a staff space is below any legible text, twenty is a
    /// page-tall title.</summary>
    internal const double MinSize = 0.5, MaxSize = 20.0;

    /// <summary>
    /// Reads the tokens after an entry's key: <c>as FAMILY</c>, <c>step [±]N</c>,
    /// <c>size N</c>, and the style words, in any order; reports what is malformed and
    /// what a generic family key may not carry.
    /// </summary>
    /// <remarks>
    /// Styles ACCUMULATE within an entry (<c>bold italic</c> is bold-italic) and
    /// <c>regular</c> clears them, so the last word decides; a repeated <c>step</c>,
    /// <c>size</c> or <c>as</c> is the duplicate warning (LYS8005) and the last wins,
    /// the rule every repeated setting in the language follows.
    /// </remarks>
    private static Attributes ReadAttributes(
        FontDeclarationSyntax.Entry entry, string canonical, bool isFamily, List<Problem> found)
    {
        TextFontFamily? redirect = null;
        double? step = null, size = null;
        FontStyle? style = null;
        TextSpan? redirectSpan = null, stepSpan = null, sizeSpan = null;
        var tokens = entry.Attributes;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            string word = t.Text;
            if (t.Kind is SyntaxKind.Plus or SyntaxKind.Minus
                or SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral)
            {
                found.Add(new Problem(t.Span, DiagnosticCodes.FontBindingMissingValue,
                    $"'{word}' needs 'step' or 'size' before it: {canonical} step {word} " +
                    $"(relative to the role's default) or {canonical} size {word} (staff spaces).",
                    IsError: true));
                continue;
            }
            if (word.Equals("as", StringComparison.OrdinalIgnoreCase))
            {
                if (isFamily)
                {
                    found.Add(new Problem(t.Span, DiagnosticCodes.FontBindingMissingValue,
                        $"'{canonical}' is a generic family and cannot follow another one: " +
                        "pointing serif at sans is a re-classification no role reads. " +
                        $"Write {canonical} \"Georgia\".", IsError: true));
                    // Skip the family word so it is not also reported as missing.
                    if (i + 1 < tokens.Count && TextRoles.TryParseFamily(tokens[i + 1].Text, out _))
                        i++;
                    continue;
                }
                if (i + 1 < tokens.Count && TextRoles.TryParseFamily(tokens[i + 1].Text, out var fam))
                {
                    if (redirectSpan is { } earlier)
                        found.Add(new Problem(earlier, DiagnosticCodes.DuplicateFontBinding,
                            $"This 'as' is overwritten by a later 'as' in the same entry; " +
                            "only the last one takes effect.", IsError: false));
                    redirect = fam;
                    redirectSpan = t.Span;
                    i++;
                    continue;
                }
                found.Add(new Problem(t.Span, DiagnosticCodes.FontBindingMissingValue,
                    $"'as' takes a generic family: {canonical} as serif or {canonical} as sans.",
                    IsError: true));
                continue;
            }
            if (word.Equals("step", StringComparison.OrdinalIgnoreCase)
                || word.Equals("size", StringComparison.OrdinalIgnoreCase))
            {
                bool isStep = word.Equals("step", StringComparison.OrdinalIgnoreCase);
                if (isFamily)
                {
                    found.Add(new Problem(t.Span, DiagnosticCodes.FontAttributeMisplaced,
                        $"'{word}' is written on a role or a group, not on the generic family " +
                        $"'{canonical}', which is a face table and has no size of its own. " +
                        $"Write it on the role, e.g. lyricText {word} " + (isStep ? "-1" : "2") + ".",
                        IsError: true));
                    i += OperandLength(tokens, i + 1);
                    continue;
                }
                int used = OperandLength(tokens, i + 1);
                if (used == 0 || !TryReadNumber(tokens, i + 1, used, out double value))
                {
                    found.Add(new Problem(t.Span, DiagnosticCodes.FontSizeOutOfRange,
                        isStep
                            ? $"'step' takes a signed number of LilyPond font-size steps, e.g. {canonical} step +1 or {canonical} step -2."
                            : $"'size' takes an em in staff spaces, e.g. {canonical} size 2.2.",
                        IsError: true));
                    i += used;
                    continue;
                }
                i += used;
                if (isStep && Math.Abs(value) > MaxStep)
                {
                    found.Add(new Problem(t.Span, DiagnosticCodes.FontSizeOutOfRange,
                        $"'step {FormatNumber(value)}' is outside -{FormatNumber(MaxStep)}..+{FormatNumber(MaxStep)} " +
                        "(a factor of four either way).", IsError: true));
                    continue;
                }
                if (!isStep && (value < MinSize || value > MaxSize))
                {
                    found.Add(new Problem(t.Span, DiagnosticCodes.FontSizeOutOfRange,
                        $"'size {FormatNumber(value)}' is outside {FormatNumber(MinSize)}..{FormatNumber(MaxSize)} staff spaces.",
                        IsError: true));
                    continue;
                }
                if (isStep)
                {
                    if (stepSpan is { } earlier)
                        found.Add(new Problem(earlier, DiagnosticCodes.DuplicateFontBinding,
                            "This 'step' is overwritten by a later 'step' in the same entry; " +
                            "only the last one takes effect.", IsError: false));
                    step = value;
                    stepSpan = t.Span;
                }
                else
                {
                    if (sizeSpan is { } earlier)
                        found.Add(new Problem(earlier, DiagnosticCodes.DuplicateFontBinding,
                            "This 'size' is overwritten by a later 'size' in the same entry; " +
                            "only the last one takes effect.", IsError: false));
                    size = value;
                    sizeSpan = t.Span;
                }
                continue;
            }
            // The style words. Anything else cannot reach here: the entry walker only
            // continues an entry on an attribute word, and the others are handled above.
            if (isFamily)
            {
                found.Add(new Problem(t.Span, DiagnosticCodes.FontAttributeMisplaced,
                    $"'{word}' is written on a role or a group, not on the generic family " +
                    $"'{canonical}', which is a face table and has no style of its own. " +
                    $"Write it on the role, e.g. tempo {word.ToLowerInvariant()}.",
                    IsError: true));
                continue;
            }
            if (word.Equals("regular", StringComparison.OrdinalIgnoreCase))
                style = FontStyle.Regular;
            else if (word.Equals("bold", StringComparison.OrdinalIgnoreCase))
                style = (style ?? FontStyle.Regular) | FontStyle.Bold;
            else if (word.Equals("italic", StringComparison.OrdinalIgnoreCase))
                style = (style ?? FontStyle.Regular) | FontStyle.Italic;
        }

        if (step != null && size != null)
        {
            found.Add(new Problem(sizeSpan!.Value, DiagnosticCodes.FontSizeAndStepBothGiven,
                $"'{canonical}' writes both 'step' and 'size'. They answer the same question " +
                "two ways - relative to the role's default and absolute - so write one: " +
                $"{canonical} step {FormatNumber(step.Value, signed: true)} or {canonical} size {FormatNumber(size.Value)}.",
                IsError: true));
            step = null;
            size = null;
        }
        return new Attributes(redirect, step, size, style);
    }

    /// <summary>How many tokens from <paramref name="at"/> spell one number: an optional
    /// sign and a literal; 0 when there is no number there.</summary>
    private static int OperandLength(IReadOnlyList<SyntaxTokenNode> tokens, int at)
    {
        int i = at;
        if (i < tokens.Count && tokens[i].Kind is SyntaxKind.Plus or SyntaxKind.Minus)
            i++;
        if (i < tokens.Count && tokens[i].Kind is SyntaxKind.IntegerLiteral or SyntaxKind.DecimalLiteral)
            return i - at + 1;
        // A lone sign with no literal after it still counts as consumed, so `step +` does
        // not also report the sign as a stray number.
        return i - at;
    }

    private static bool TryReadNumber(IReadOnlyList<SyntaxTokenNode> tokens, int at, int count, out double value)
    {
        value = 0;
        var sb = new System.Text.StringBuilder();
        for (int i = at; i < at + count; i++)
            sb.Append(tokens[i].Text);
        return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string FormatNumber(double value, bool signed = false)
    {
        string s = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return signed && value > 0 ? "+" + s : s;
    }

    /// <summary>
    /// LYS8018: a size or style on a LEAF role the engraving does not read it for — see
    /// <see cref="TextRoles.PlanReachOf"/>. A warning, because the face on the same entry
    /// still binds; but a silent no-op is the outcome this feature was designed to refuse.
    /// </summary>
    private static void WarnWhenThePageIgnoresIt(TextRole role, Attributes attrs, TextSpan span, List<Problem> found)
    {
        var reach = TextRoles.PlanReachOf(role);
        string spelling = TextRoles.Spelling(role);
        if ((attrs.Step != null || attrs.Size != null) && (reach & PlanReach.Size) == 0)
            found.Add(new Problem(span, DiagnosticCodes.FontAttributeNotFollowed,
                $"The engraving sets '{spelling}' at its own size in this version and does not " +
                "read 'step' or 'size' for it; the face still binds. Roles whose size follows the " +
                "plan: " + string.Join(", ", SizedRoles()) + ".", IsError: false));
        if (attrs.Style != null && (reach & PlanReach.Style) == 0)
            found.Add(new Problem(span, DiagnosticCodes.FontAttributeNotFollowed,
                $"The engraving decides '{spelling}''s weight and slant in this version and does " +
                "not read a style for it; the face still binds. Roles whose style follows the " +
                "plan: " + string.Join(", ", StyledRoles()) + ".", IsError: false));
    }

    /// <summary>
    /// LYS8018 for a GROUP: warned only when NO leaf of the group follows the plan — a
    /// group with one following member is doing what the writer asked for the rest.
    /// </summary>
    private static void WarnWhenThePageIgnoresIt(TextRoleGroup group, Attributes attrs, TextSpan span, List<Problem> found)
    {
        var leaves = TextRoles.LeavesOf(group).ToList();
        string spelling = TextRoles.Spelling(group);
        if ((attrs.Step != null || attrs.Size != null)
            && !leaves.Any(l => (TextRoles.PlanReachOf(l) & PlanReach.Size) != 0))
            found.Add(new Problem(span, DiagnosticCodes.FontAttributeNotFollowed,
                $"No role in '{spelling}' reads 'step' or 'size' from the plan in this version " +
                "(" + string.Join(", ", leaves.Select(TextRoles.Spelling)) + "); the face still binds.",
                IsError: false));
        if (attrs.Style != null
            && !leaves.Any(l => (TextRoles.PlanReachOf(l) & PlanReach.Style) != 0))
            found.Add(new Problem(span, DiagnosticCodes.FontAttributeNotFollowed,
                $"No role in '{spelling}' reads a style from the plan in this version " +
                "(" + string.Join(", ", leaves.Select(TextRoles.Spelling)) + "); the face still binds.",
                IsError: false));
    }

    private static IEnumerable<string> SizedRoles() => TextRoles.All
        .Where(r => (TextRoles.PlanReachOf(r) & PlanReach.Size) != 0).Select(TextRoles.Spelling);

    private static IEnumerable<string> StyledRoles() => TextRoles.All
        .Where(r => (TextRoles.PlanReachOf(r) & PlanReach.Style) != 0).Select(TextRoles.Spelling);
}
