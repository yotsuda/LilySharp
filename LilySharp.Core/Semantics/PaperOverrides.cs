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

using System;
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// <c>paper</c> and <c>layout</c> values given from outside the file —
/// <c>lysc svg|png|pdf|boxes --set KEY=VALUE</c> (LilySharp-Omr's proposal of 2026-10-02, P1: the
/// same music engraved another way for training data, the <c>.lys</c> untouched). They overlay
/// the score's own LAST: after the file's <c>paper { }</c> / <c>layout { }</c> and the score's
/// <c>paper NAME</c> / <c>layout NAME</c>.
/// </summary>
/// <remarks>
/// <para>
/// A setting is an entry of either block spelled <c>KEY=VALUE</c>: <c>spacingIncrement=1.6</c>,
/// <c>leftMargin=20mm</c>, <c>size=a5</c>, <c>systemsPerPage=4</c>,
/// <c>staffStaffSpacing.basicDistance=9</c> (a spacing block's sub-key), <c>Stem.thickness=1.5</c>,
/// <c>LedgerLine.thickness=1.0,0.1</c> (a comma for the space between two numbers),
/// <c>barNumbers=none</c>, and a bare flag as <c>raggedRight</c> or <c>raggedRight=true</c>. The
/// entries are read by <see cref="PaperPlanReader"/> and <see cref="LayoutPlanReader"/>
/// themselves — written out as a block and parsed — so every key, unit and refusal is the
/// language's, and a key the language gains is a setting with no change here.
/// </para>
/// <para>
/// One spelling a block has no shape for: a flag turned OFF (<c>raggedRight=false</c>), so a
/// setting can undo what the file turns on. Those are applied after the block.
/// </para>
/// <para>
/// ⚠️ ONE KEY IS LAID UNDER THE FILE, not over it: <c>staffSpace</c> (<see cref="OnBase"/>),
/// since the file's millimetres are read through it — and it wins over a <c>staffSpace</c> the
/// file writes.
/// </para>
/// </remarks>
public sealed class PaperOverrides
{
    /// <summary>The flags <see cref="Apply"/> knows how to turn off.</summary>
    private static readonly string[] FlagsWithOff = ["raggedRight", "raggedBottom", "breaksOnly"];

    private readonly PaperDeclarationSyntax? _paper;
    private readonly LayoutDeclarationSyntax? _layout;
    private readonly string[] _flagsOff;

    private PaperOverrides(PaperDeclarationSyntax? paper, LayoutDeclarationSyntax? layout, string[] flagsOff,
        string? music)
    {
        _paper = paper;
        _layout = layout;
        _flagsOff = flagsOff;
        Music = music;
        StaffSpaceMm = paper != null ? PaperPlanReader.StaffSpaceOf(paper) : null;
    }

    /// <summary>
    /// <c>music=NAME</c>: the music font, over the file's <c>fonts { music "…" }</c> — the
    /// name as the font's metadata states it (<c>MusicFonts.Find</c> resolved it at parse
    /// time, so a name that exists nowhere is a refused setting, not a warning on the page).
    /// Null when not set. The OMR's door for engraving one score in several fonts.
    /// </summary>
    public string? Music { get; }

    /// <summary>
    /// <c>staffSpace=1.5mm</c>: the staff space on the paper, in millimetres (null = the file's,
    /// else LilyPond's <see cref="LayoutOptions.DefaultStaffSpaceMm"/>).
    /// </summary>
    internal double? StaffSpaceMm { get; }

    /// <summary>The paper a book starts from, on the setting's staff space — before the file's
    /// <c>paper { }</c>, whose millimetres it converts (<see cref="PaperPlanReader.AtStaffSpace"/>),
    /// and marked so the file's own <c>staffSpace</c> gives way.</summary>
    internal LayoutOptions OnBase(LayoutOptions @base)
        => StaffSpaceMm is { } mm
            ? PaperPlanReader.AtStaffSpace(@base, mm) with { StaffSpaceFromSetting = true }
            : @base;

    /// <summary>
    /// Reads <c>KEY=VALUE</c> settings, or null with <paramref name="error"/> saying which one
    /// does not read (as the block would refuse it).
    /// </summary>
    public static PaperOverrides? Parse(IEnumerable<string> settings, out string? error)
    {
        error = null;
        var paperEntries = new List<string>();
        var layoutEntries = new List<string>();
        var flagsOff = new List<string>();
        var flags = PaperPlanReader.FlagKeySpellings();
        var layoutKeys = LayoutPlanReader.AllKeySpellings();
        string? music = null;
        foreach (var setting in settings)
        {
            int eq = setting.IndexOf('=');
            string key = (eq < 0 ? setting : setting[..eq]).Trim();
            string value = eq < 0 ? "" : setting[(eq + 1)..].Trim();
            if (key.Length == 0)
            {
                error = $"'{setting}' is not KEY=VALUE.";
                return null;
            }
            if (Rendering.TextRoles.IsMusicKey(key))
            {
                // The one fonts { } key a setting reaches: the music font, by name.
                if (value.Length == 0)
                {
                    error = $"'{key}' needs a font name: {key}=Bravura (bundled: "
                        + string.Join(", ", MusicFonts.BundledNames()) + ").";
                    return null;
                }
                if (MusicFonts.Find(value, out var tried) is not { } font)
                {
                    error = $"No music font named '{value}' was found (bundled: "
                        + string.Join(", ", MusicFonts.BundledNames()) + "). Looked for its SMuFL metadata at: "
                        + string.Join("; ", tried) + ".";
                    return null;
                }
                music = font.Name;
                continue;
            }
            if (flags.Contains(key))
            {
                switch (value.ToLowerInvariant())
                {
                    case "" or "true": paperEntries.Add(key); break;
                    case "false" when FlagsWithOff.Contains(key): flagsOff.Add(key); break;
                    case "false":
                        error = $"'{key}=false' is not supported yet; leave the flag out instead.";
                        return null;
                    default:
                        error = $"'{key}' is a flag: {key}, {key}=true or {key}=false.";
                        return null;
                }
                continue;
            }
            if (value.Length == 0)
            {
                error = $"'{key}' needs a value: {key}=VALUE.";
                return null;
            }
            if (layoutKeys.Contains(key))
            {
                // Two numbers are written with a comma, which a shell keeps in one argument.
                layoutEntries.Add($"{key} {value.Replace(',', ' ')}");
                continue;
            }
            // A spacing block's sub-key: staffStaffSpacing.basicDistance=9.
            int dot = key.IndexOf('.');
            paperEntries.Add(dot < 0 ? $"{key} {value}" : $"{key[..dot]} {{ {key[(dot + 1)..]} {value} }}");
        }

        PaperDeclarationSyntax? paper = null;
        if (paperEntries.Count > 0)
        {
            paper = ParseBlock<PaperDeclarationSyntax>("paper", paperEntries, out error);
            if (paper == null)
                return null;
            PaperPlanReader.Read(paper, LayoutOptions.Default, out var problems);
            if (problems.FirstOrDefault(p => p.IsError) is { Message: { } message })
            {
                error = message;
                return null;
            }
        }
        LayoutDeclarationSyntax? layout = null;
        if (layoutEntries.Count > 0)
        {
            layout = ParseBlock<LayoutDeclarationSyntax>("layout", layoutEntries, out error);
            if (layout == null)
                return null;
            LayoutPlanReader.Read(layout, out var problems);
            if (problems.FirstOrDefault(p => p.IsError) is { Message: { } message })
            {
                error = message;
                return null;
            }
        }
        return new PaperOverrides(paper, layout, [.. flagsOff], music);
    }

    /// <summary>The music font setting laid over <paramref name="fonts"/> — the file's chain
    /// replaced by the one name, or the plan untouched when none was set.</summary>
    internal Rendering.TextFontPlan ApplyFonts(Rendering.TextFontPlan fonts)
        => Music is { } name ? fonts.WithMusic([name]) : fonts;

    /// <summary>The entries written out as one block and parsed, or null with
    /// <paramref name="error"/> when they do not parse.</summary>
    private static T? ParseBlock<T>(string keyword, List<string> entries, out string? error) where T : SyntaxNode
    {
        error = null;
        var tree = SyntaxTree.Parse(keyword + " { " + string.Join("\n", entries) + " }");
        var block = tree.GetRoot().DescendantNodes<T>().FirstOrDefault();
        if (block == null || tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            error = $"the settings do not read as {keyword} entries: "
                + string.Join("; ", tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
            return null;
        }
        return block;
    }

    /// <summary>The paper settings laid over <paramref name="paper"/>.</summary>
    internal LayoutOptions Apply(LayoutOptions paper)
    {
        if (_paper != null)
            paper = PaperPlanReader.Read(_paper, paper, out _);
        foreach (var flag in _flagsOff)
            paper = flag switch
            {
                "raggedRight" => paper with { RaggedRight = false },
                "raggedBottom" => paper with { PageBreaking = paper.PageBreaking with { RaggedBottom = false } },
                "breaksOnly" => paper with { BreaksOnly = false },
                _ => paper,   // Parse admits only FlagsWithOff
            };
        return paper;
    }

    /// <summary>The layout settings laid over <paramref name="plan"/>.</summary>
    internal LayoutPlan ApplyLayout(LayoutPlan plan)
        => _layout != null ? LayoutPlanReader.Read(_layout, plan, out _) : plan;
}
