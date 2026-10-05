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
/// <c>paper</c> values given from outside the file — <c>lysc svg|png|pdf --set KEY=VALUE</c>
/// (LilySharp-Omr's proposal of 2026-10-02, P1: the same music engraved with other spacing
/// for training data, the <c>.lys</c> untouched). They overlay the score's paper LAST: after
/// the file's <c>paper { }</c> and the score's <c>paper NAME</c>.
/// </summary>
/// <remarks>
/// <para>
/// A setting is a paper entry spelled <c>KEY=VALUE</c>: <c>spacingIncrement=1.6</c>,
/// <c>leftMargin=20mm</c>, <c>size=a5</c>, <c>staffStaffSpacing.basicDistance=9</c> (a spacing
/// block's sub-key), and a bare flag as <c>raggedRight</c> or <c>raggedRight=true</c>. The
/// entries are read by <see cref="PaperPlanReader"/> itself — written out as a
/// <c>paper { }</c> block and parsed — so every key, unit and refusal is the language's, and a
/// key the language gains is a setting with no change here.
/// </para>
/// <para>
/// One spelling the block has no shape for: a flag turned OFF (<c>raggedRight=false</c>), so
/// a setting can undo what the file turns on. Those are applied after the block.
/// </para>
/// </remarks>
public sealed class PaperOverrides
{
    /// <summary>The flags <see cref="Apply"/> knows how to turn off.</summary>
    private static readonly string[] FlagsWithOff = ["raggedRight", "raggedBottom", "breaksOnly"];

    private readonly PaperDeclarationSyntax? _block;
    private readonly string[] _flagsOff;

    private PaperOverrides(PaperDeclarationSyntax? block, string[] flagsOff)
    {
        _block = block;
        _flagsOff = flagsOff;
    }

    /// <summary>
    /// Reads <c>KEY=VALUE</c> settings, or null with <paramref name="error"/> saying which one
    /// does not read (as the paper block would refuse it).
    /// </summary>
    public static PaperOverrides? Parse(IEnumerable<string> settings, out string? error)
    {
        error = null;
        var entries = new List<string>();
        var flagsOff = new List<string>();
        var flags = PaperPlanReader.FlagKeySpellings();
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
            if (flags.Contains(key))
            {
                switch (value.ToLowerInvariant())
                {
                    case "" or "true": entries.Add(key); break;
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
            // A spacing block's sub-key: staffStaffSpacing.basicDistance=9.
            int dot = key.IndexOf('.');
            entries.Add(dot < 0 ? $"{key} {value}" : $"{key[..dot]} {{ {key[(dot + 1)..]} {value} }}");
        }

        PaperDeclarationSyntax? block = null;
        if (entries.Count > 0)
        {
            var tree = SyntaxTree.Parse("paper { " + string.Join("\n", entries) + " }");
            block = tree.GetRoot().DescendantNodes<PaperDeclarationSyntax>().FirstOrDefault();
            if (block == null || tree.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                error = "the settings do not read as paper entries: "
                    + string.Join("; ", tree.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
                return null;
            }
            PaperPlanReader.Read(block, LayoutOptions.Default, out var problems);
            if (problems.FirstOrDefault(p => p.IsError) is { Message: { } message })
            {
                error = message;
                return null;
            }
        }
        return new PaperOverrides(block, [.. flagsOff]);
    }

    /// <summary>The settings laid over <paramref name="paper"/>.</summary>
    internal LayoutOptions Apply(LayoutOptions paper)
    {
        if (_block != null)
            paper = PaperPlanReader.Read(_block, paper, out _);
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
}
