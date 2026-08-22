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
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Turns a <c>paper</c> directive into the <see cref="LayoutOptions"/> it asks for, and
/// says what was wrong with it.
/// </summary>
/// <remarks>
/// ONE HOME FOR THE READING, the same contract as <see cref="FontPlanReader"/>: the
/// collector wants the options and no diagnostics, the validator wants the diagnostics
/// and no options, and if each parsed the entries itself they would eventually disagree
/// about which directives are legal.
/// <para>
/// The vocabulary is LilyPond's <c>\paper</c> variables camelCased, plus the
/// staff-to-staff spacing family, which LilyPond keeps on grobs
/// (<c>StaffGrouper.staff-staff-spacing</c>, <c>VerticalAxisGroup</c>'s defaults) and
/// Lily# deliberately keeps HERE (user decision 2026-08-23): every one of these
/// quantities is applied score-wide in one pass — <c>StaffSpacingParameters</c> has no
/// positional scope — and <c>paper { }</c> is the spelling whose meaning IS score-wide,
/// while an override drags in a scope machinery (<c>once</c>, staff tags) that would
/// parse and then silently not apply. NOT here, though they live on
/// <see cref="LayoutOptions"/>: <c>StaffHeight</c> (the unit's own frame — four spaces
/// between five lines — not a knob), <c>SystemSpacing</c> (inert by measurement, its
/// remark says so), and the line/page-breaking algorithm switches (engine tuning, not a
/// dimension of the picture; user decision 2026-08-23).
/// </para>
/// </remarks>
internal static class PaperPlanReader
{
    /// <summary>Something the directive got wrong, with the span to point at.</summary>
    /// <param name="Span">Where to underline.</param>
    /// <param name="Code">A <see cref="DiagnosticCodes"/> constant.</param>
    /// <param name="Message">The prose, ASCII punctuation only (these reach the CLI).</param>
    /// <param name="IsError">False for a warning.</param>
    internal readonly record struct Problem(TextSpan Span, string Code, string Message, bool IsError);

    // ONE conversion, the same one every LayoutOptions page default was computed with
    // (LayoutOptions.cs's header): 1 staff space = 5 TeX points = 127/72.27 mm, so
    // mm -> ss is x 72.27/127. Converted values are rounded to six decimals BECAUSE THE
    // DEFAULTS ARE: `paperWidth 210mm` must equal the default PageWidth 119.501575
    // exactly, not to within 2e-7 — a book that states the default must be the default.
    private const double MmPerCm = 10.0;
    private const double MmPerInch = 25.4;

    private static double MmToSs(double mm) => Math.Round(mm * 72.27 / 127.0, 6);

    /// <summary>The scalar length keys, canonical spellings, in documentation order.</summary>
    private static readonly string[] ScalarKeys =
    [
        "paperWidth", "paperHeight",
        "leftMargin", "rightMargin", "topMargin", "bottomMargin",
        "indent", "shortIndent",
        "topSystemPadding", "spacingIncrement",
    ];

    /// <summary>The nested spacing-block keys, canonical spellings.</summary>
    private static readonly string[] SpecKeys =
    [
        "systemSystemSpacing", "scoreSystemSpacing", "markupSystemSpacing",
        "scoreMarkupSpacing", "markupMarkupSpacing", "topSystemSpacing",
        "lastBottomSpacing",
        "staffStaffSpacing", "staffGroupStaffSpacing", "defaultStaffStaffSpacing",
        "nonStaffRelatedStaffSpacing", "nonStaffUnrelatedStaffSpacing",
        "nonStaffNonStaffSpacing",
    ];

    private static readonly string[] SubKeys =
        ["basicDistance", "minimumDistance", "padding", "stretchability"];

    private static readonly string[] Units = ["mm", "cm", "in"];

    /// <summary>Every key a <c>paper { }</c> entry can be spelled with, for messages
    /// and for completion.</summary>
    internal static IReadOnlyList<string> AllKeySpellings() =>
        [.. ScalarKeys, "raggedRight", .. SpecKeys];

    /// <summary>The scalar length keys alone — the completion inserts these with a
    /// number position, unlike a flag or a spacing block.</summary>
    internal static IReadOnlyList<string> ScalarKeySpellings() => ScalarKeys;

    /// <summary>The nested spacing-block keys alone.</summary>
    internal static IReadOnlyList<string> SpecKeySpellings() => SpecKeys;

    /// <summary>The sub-keys a nested spacing block takes, for messages and completion.</summary>
    internal static IReadOnlyList<string> AllSubKeySpellings() => SubKeys;

    /// <summary>
    /// Reads <paramref name="paper"/> into the layout options it asks for.
    /// </summary>
    /// <param name="paper">The directive.</param>
    /// <param name="problems">Everything wrong with it, in source order.</param>
    /// <returns>
    /// <see cref="LayoutOptions.Default"/> with the directive's entries overlaid.
    /// Entries that produced an ERROR are left out of it, so a page with one bad key
    /// still gets the dimensions it spelled correctly.
    /// </returns>
    internal static LayoutOptions Read(PaperDeclarationSyntax paper, out IReadOnlyList<Problem> problems)
    {
        var found = new List<Problem>();
        problems = found;

        if (!paper.IsBlock)
        {
            // The blockless form is refused by the parser (LYS9005 and kin) and keeps
            // its tokens so no source position slides. It sets NOTHING here — a refused
            // directive has to be refused all the way through (the fonts one-liner's
            // reasoning, verbatim). A blockless NAMED node — a score's pure reference —
            // reads through ReadReference instead, never here.
            return LayoutOptions.Default;
        }

        return ReadEntriesInto(LayoutOptions.Default, paper, found);
    }

    /// <summary>Every named top-level paper declaration, in document order.</summary>
    internal static IReadOnlyList<PaperDeclarationSyntax> NamedDeclarations(SyntaxNode root) =>
        [.. root.DescendantNodes().OfType<PaperDeclarationSyntax>()
            .Where(p => p.NameToken != null && p.IsBlock && !FontPlanReader.IsInsideRender(p))];

    /// <summary>
    /// Resolves a score reference's name to its top-level declaration — ONE HOME for
    /// the unknown-name sentence, the same contract as the fonts one.
    /// </summary>
    internal static bool TryResolve(SyntaxNode root, PaperDeclarationSyntax reference,
        out PaperDeclarationSyntax? declaration, out Problem? problem)
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
        problem = new Problem(nameToken.Span, DiagnosticCodes.UnknownPaperBlockName,
            $"No paper block is named '{name}'." + (declared.Count > 0
                ? " Declared: " + string.Join(", ", declared) + "."
                : $" Declare one at the top level: paper {name} {{ paperWidth 210mm }}."),
            IsError: true);
        return false;
    }

    /// <summary>
    /// The options a score's <c>paper NAME [{ … }]</c> reference asks for: the named
    /// block overlaid on the defaults, then the reference's own override entries
    /// overlaid on THAT — the same reading as one merged block, so a key the override
    /// writes wins and a spacing block's unwritten lines keep the named block's
    /// values. A reference that resolves to nothing keeps <paramref name="fallback"/> —
    /// refused all the way through, like every other refused directive.
    /// </summary>
    /// <remarks>
    /// ⚠️ Entry problems are NOT surfaced here — each block's entries are validated
    /// where the block stands — and a same-key repeat ACROSS the two blocks is
    /// deliberately not a warning: overriding a key is the override block's purpose.
    /// </remarks>
    internal static LayoutOptions ReadReference(SyntaxNode root, PaperDeclarationSyntax reference,
        LayoutOptions fallback)
    {
        if (!TryResolve(root, reference, out var declaration, out _))
            return fallback;
        var discard = new List<Problem>();
        var options = ReadEntriesInto(LayoutOptions.Default, declaration!, discard);
        if (reference.IsBlock)
            options = ReadEntriesInto(options, reference, discard);
        return options;
    }

    /// <summary>Overlays one block's entries onto <paramref name="options"/> — the loop
    /// <see cref="Read"/> and <see cref="ReadReference"/> share, so a directive and a
    /// merged reference cannot disagree about what an entry means. Duplicate-key
    /// detection is scoped to the one block: a repeat across blocks is an override.</summary>
    private static LayoutOptions ReadEntriesInto(
        LayoutOptions options, PaperDeclarationSyntax paper, List<Problem> found)
    {
        var boundKeys = new Dictionary<string, TextSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in paper.Entries)
        {
            var span = entry.KeyToken.Span;
            string? key = Canonical(entry.Key, ScalarKeys)
                ?? Canonical(entry.Key, SpecKeys)
                ?? (entry.Key.Equals("raggedRight", StringComparison.OrdinalIgnoreCase) ? "raggedRight" : null);
            if (key == null)
            {
                found.Add(new Problem(span, DiagnosticCodes.UnknownPaperKey,
                    Canonical(entry.Key, Units) != null
                        // The spaced-unit trap: `paperWidth 210 mm` reads as a key named
                        // mm, and the fix is the glued spelling, not the vocabulary list.
                        ? $"'{entry.Key}' is a unit, and a unit is spelled glued to its "
                          + $"number: 210{entry.Key.ToLowerInvariant()}, one word."
                        : $"'{entry.Key}' is not a paper key. Known keys: "
                          + string.Join(", ", AllKeySpellings()) + ".",
                    IsError: true));
                continue;
            }

            if (boundKeys.TryGetValue(key, out _))
            {
                found.Add(new Problem(span, DiagnosticCodes.DuplicatePaperKey,
                    $"'{key}' is set twice in this paper block; the last one wins.",
                    IsError: false));
            }
            boundKeys[key] = span;

            bool isSpec = Canonical(key, SpecKeys) != null;
            if (isSpec)
            {
                if (!entry.HasBlock || entry.NumberToken != null)
                {
                    found.Add(new Problem(span, DiagnosticCodes.PaperEntryMissingValue,
                        $"'{key}' takes a spacing block: {key} {{ basicDistance 12 }}.",
                        IsError: true));
                    continue;
                }
                options = ApplySpec(options, key, entry, found);
                continue;
            }

            if (entry.HasBlock)
            {
                found.Add(new Problem(span, DiagnosticCodes.PaperEntryMissingValue,
                    $"'{key}' takes a number, not a block.", IsError: true));
                continue;
            }

            if (key == "raggedRight")
            {
                if (entry.NumberToken != null || entry.MinusToken != null)
                {
                    found.Add(new Problem(span, DiagnosticCodes.PaperEntryMissingValue,
                        "'raggedRight' is a bare flag; writing it turns it on.",
                        IsError: true));
                    continue;
                }
                options = options with { RaggedRight = true };
                continue;
            }

            if (!TryReadLength(entry.MinusToken, entry.NumberToken, entry.UnitToken,
                    unitless: false, span, key, found, out double v))
                continue;
            options = key switch
            {
                "paperWidth" => options with { PageWidth = v },
                "paperHeight" => options with { PageHeight = v },
                "leftMargin" => options with { MarginLeft = v },
                "rightMargin" => options with { MarginRight = v },
                "topMargin" => options with { MarginTop = v },
                "bottomMargin" => options with { MarginBottom = v },
                "indent" => options with { Indent = v },
                "shortIndent" => options with { ShortIndent = v },
                "topSystemPadding" => options with { TopSystemPadding = v },
                "spacingIncrement" => options with { SpacingIncrement = v },
                _ => options,
            };
        }

        return options;
    }

    /// <summary>The canonical spelling <paramref name="word"/> matches in
    /// <paramref name="vocabulary"/>, or null. Case-insensitive, like a font key.</summary>
    private static string? Canonical(string word, string[] vocabulary)
    {
        foreach (var candidate in vocabulary)
            if (word.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }

    /// <summary>Overlays one nested spacing block onto the spec it names.</summary>
    private static LayoutOptions ApplySpec(
        LayoutOptions options, string key, PaperDeclarationSyntax.Entry entry, List<Problem> found)
    {
        var vs = options.VerticalSpacing;
        var ss = options.StaffSpacing;
        return key switch
        {
            "systemSystemSpacing" => options with { VerticalSpacing = vs with { SystemSystem = ReadSpec(vs.SystemSystem, entry, found) } },
            "scoreSystemSpacing" => options with { VerticalSpacing = vs with { ScoreSystem = ReadSpec(vs.ScoreSystem, entry, found) } },
            "markupSystemSpacing" => options with { VerticalSpacing = vs with { MarkupSystem = ReadSpec(vs.MarkupSystem, entry, found) } },
            "scoreMarkupSpacing" => options with { VerticalSpacing = vs with { ScoreMarkup = ReadSpec(vs.ScoreMarkup, entry, found) } },
            "markupMarkupSpacing" => options with { VerticalSpacing = vs with { MarkupMarkup = ReadSpec(vs.MarkupMarkup, entry, found) } },
            "topSystemSpacing" => options with { VerticalSpacing = vs with { TopSystem = ReadSpec(vs.TopSystem, entry, found) } },
            "lastBottomSpacing" => options with { VerticalSpacing = vs with { LastBottom = ReadSpec(vs.LastBottom, entry, found) } },
            "staffStaffSpacing" => options with { StaffSpacing = ss with { StaffStaff = ReadSpec(ss.StaffStaff, entry, found) } },
            "staffGroupStaffSpacing" => options with { StaffSpacing = ss with { StaffGroupStaff = ReadSpec(ss.StaffGroupStaff, entry, found) } },
            "defaultStaffStaffSpacing" => options with { StaffSpacing = ss with { DefaultStaffStaff = ReadSpec(ss.DefaultStaffStaff, entry, found) } },
            "nonStaffRelatedStaffSpacing" => options with { StaffSpacing = ss with { NonStaffRelatedStaff = ReadSpec(ss.NonStaffRelatedStaff, entry, found) } },
            "nonStaffUnrelatedStaffSpacing" => options with { StaffSpacing = ss with { NonStaffUnrelatedStaff = ReadSpec(ss.NonStaffUnrelatedStaff, entry, found) } },
            "nonStaffNonStaffSpacing" => options with { StaffSpacing = ss with { NonStaffNonStaff = ReadSpec(ss.NonStaffNonStaff, entry, found) } },
            _ => options,
        };
    }

    /// <summary>Overlays one spacing block's lines onto <paramref name="current"/>.</summary>
    private static VerticalSpacingSpec ReadSpec(
        VerticalSpacingSpec current, PaperDeclarationSyntax.Entry entry, List<Problem> found)
    {
        var bound = new Dictionary<string, TextSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in entry.SubEntries)
        {
            var span = sub.KeyToken.Span;
            string? key = Canonical(sub.Key, SubKeys);
            if (key == null)
            {
                found.Add(new Problem(span, DiagnosticCodes.UnknownPaperKey,
                    Canonical(sub.Key, Units) != null
                        ? $"'{sub.Key}' is a unit, and a unit is spelled glued to its "
                          + $"number: 12{sub.Key.ToLowerInvariant()}, one word."
                        : $"'{sub.Key}' is not a spacing sub-key. Known sub-keys: "
                          + string.Join(", ", SubKeys) + ".",
                    IsError: true));
                continue;
            }
            if (bound.TryGetValue(key, out _))
            {
                found.Add(new Problem(span, DiagnosticCodes.DuplicatePaperKey,
                    $"'{key}' is set twice in this spacing block; the last one wins.",
                    IsError: false));
            }
            bound[key] = span;

            if (!TryReadLength(sub.MinusToken, sub.NumberToken, sub.UnitToken,
                    unitless: key == "stretchability", span, key, found, out double v))
                continue;
            current = key switch
            {
                "basicDistance" => current with { BasicDistance = v },
                "minimumDistance" => current with { MinimumDistance = v },
                "padding" => current with { Padding = v },
                "stretchability" => current with { Stretchability = v },
                _ => current,
            };
        }
        return current;
    }

    /// <summary>
    /// Reads one value: a number, an optional sign, an optional GLUED unit. A bare
    /// number is staff spaces — the unit everything else in this language is measured
    /// in — and a physical unit converts through the one mm-to-ss conversion the
    /// defaults were computed with.
    /// </summary>
    private static bool TryReadLength(
        SyntaxTokenNode? minus, SyntaxTokenNode? number, SyntaxTokenNode? unit,
        bool unitless, TextSpan keySpan, string key, List<Problem> found, out double value)
    {
        value = 0;
        if (number == null)
        {
            found.Add(new Problem(keySpan, DiagnosticCodes.PaperEntryMissingValue,
                $"'{key}' needs a number.", IsError: true));
            return false;
        }
        double v = double.Parse(number.Text, CultureInfo.InvariantCulture);
        if (minus != null)
            v = -v;

        if (unit == null)
        {
            value = v;
            return true;
        }
        if (unitless)
        {
            found.Add(new Problem(unit.Span, DiagnosticCodes.PaperUnitOnUnitless,
                $"'{key}' is unitless (a spring flexibility, not a length); write a "
                + "bare number.", IsError: true));
            return false;
        }
        string? u = Canonical(unit.Text, Units);
        if (u == null)
        {
            found.Add(new Problem(unit.Span, DiagnosticCodes.UnknownPaperUnit,
                $"'{unit.Text}' is not a unit of this language. A bare number is staff "
                + "spaces; the physical units are mm, cm and in.", IsError: true));
            return false;
        }
        value = u switch
        {
            "mm" => MmToSs(v),
            "cm" => MmToSs(v * MmPerCm),
            "in" => MmToSs(v * MmPerInch),
            _ => v,
        };
        return true;
    }
}
