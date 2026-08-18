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
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// The LP-fidelity ledger may only ever be measured against faces this engine SHIPS.
/// </summary>
/// <remarks>
/// Lily# used to be safe here by construction: the layout never saw a score's
/// <c>font</c> directive at all — <c>score.Fonts</c> reached the DRAWING context only
/// (<c>SharedRenderer</c>, <c>doc.Fonts = score.Fonts</c>) and every one of the layout's
/// text measurements went to <see cref="TextFontMetrics"/>'s statics keyed by
/// <c>(sans, style)</c>. Nothing a score could write could move a ledger number.
/// <para>
/// That construction is being deliberately opened: a face a score NAMES is going to be
/// measured, because LilyPond measures it too (<c>lily/font-select.cc:193-217
/// select_font</c> turns a <c>font-name</c> string into a <c>PangoFontDescription</c> and
/// hands it to <c>find_pango_font</c>, so LilyPond's own text extents come from whatever
/// fontconfig resolved). Once that path exists, "the ledger cannot be polluted" stops
/// being a property of the architecture and becomes a property that has to be STATED and
/// CHECKED — which is what this file is.
/// </para>
/// <para>
/// ⚠️ THE POLLUTION IS NOT HYPOTHETICAL. <c>b69c73e6</c> removed a system-font fallback
/// from the fidelity probe precisely because four ledger values had been measured against
/// whatever face fontconfig happened to pick on the machine that ran it. A ledger entry
/// measured against a machine-local face is not a measurement of Lily#; it is a
/// measurement of that machine.
/// </para>
/// <para>
/// The guard has TWO halves and they fail for different reasons, so they are two tests:
/// the ENTRANCE (no fidelity probe writes a <c>font</c> directive) and the EXIT (the
/// default plan resolves every role to a face this engine ships). The entrance alone
/// would pass on a day the default plan itself started naming an outside face; the exit
/// alone would pass on a day a probe bound one.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LpFidelityFaceGuardTests
{
    /// <summary>
    /// THE ENTRANCE: no probe in the LP-geometry ledger writes a <c>font</c> directive.
    /// </summary>
    /// <remarks>
    /// The probe sources are inline <c>.lys</c> text, so this reads them the way the
    /// engine does rather than grepping for the keyword — a probe that reached a binding
    /// through some other spelling would still be caught, and a probe that only mentions
    /// <c>font</c> in a comment would not be a false positive.
    /// </remarks>
    [Fact]
    public void EveryLedgerProbe_LeavesTheFontPlanAtItsDefault()
    {
        var bound = new List<string>();
        foreach (var probe in LpGeometryProbes.All)
        {
            var tree = SyntaxTree.Parse(probe.Source);
            var spec = RenderSpecParser.FindFirst(tree);
            var score = SvgGenerator.CollectScore(tree, spec);
            if (!score.Fonts.IsDefault)
                bound.Add($"{probe.Id}: {score.Fonts.Signature}");
        }

        // The population, asserted before the emptiness is: an `Assert.Empty` over a list
        // built from nothing is green for the wrong reason (HANDOFF RULES §5.4).
        Assert.True(LpGeometryProbes.All.Count > 400,
            $"only {LpGeometryProbes.All.Count} probes were read — this guard is supposed to "
            + "cover the whole ledger, so a number this small means the population moved.");

        Assert.True(bound.Count == 0,
            "A LilyPond-fidelity probe binds a text face:\n  "
            + string.Join("\n  ", bound)
            + "\nThe ledger records how far Lily# is from LilyPond. A probe that names a "
            + "face measures the FACE instead — on a machine that has it, and something "
            + "else on a machine that does not. Measure the divergence somewhere that is "
            + "not the ledger.");
    }

    /// <summary>
    /// THE EXIT: with no <c>font</c> directive, every role is drawn and measured with a
    /// face this engine ships.
    /// </summary>
    /// <remarks>
    /// This is the invariant that survives the measuring path learning about named faces:
    /// whatever that path grows, a score that asked for nothing must still land on the
    /// bundled files, because that is what every ledger number was taken against.
    /// <para>
    /// <see cref="TextRole.SystemBrace"/> is the one role that resolves to a NAME rather
    /// than to <c>IsBundled</c>, and the name is the bundled Emmentaler brace face — it is
    /// addressed by name only because the brace ladder lives in its own file. It is
    /// checked by name here rather than skipped, so a day when it starts naming something
    /// else is a day this test goes red.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDefaultPlan_ResolvesEveryRoleToAFaceThisEngineShips()
    {
        var outside = new List<string>();
        foreach (var role in TextRoles.All)
        {
            var face = TextFontPlan.Default.Resolve(role);
            if (role == TextRole.SystemBrace)
            {
                if (face.Names.Length != 1 || face.Names[0] != TextFontPlan.BraceFaceName)
                    outside.Add($"{TextRoles.Spelling(role)} -> {face.FamilyAttribute}");
                continue;
            }
            if (!face.IsBundled)
                outside.Add($"{TextRoles.Spelling(role)} -> {face.FamilyAttribute}");
        }

        Assert.True(TextRoles.All.Count >= 20,
            $"only {TextRoles.All.Count} roles exist — the vocabulary shrank, so this guard "
            + "is covering less than it reads as covering.");

        Assert.True(outside.Count == 0,
            "With no `font` directive, these roles resolve to a face this engine does not "
            + "ship:\n  " + string.Join("\n  ", outside)
            + "\nEvery number in audit/lp-geometry/lp-geometry.json was measured against the "
            + "bundled faces. A default that reaches outside them would re-date the whole "
            + "ledger silently.");
    }

    /// <summary>
    /// And the bundled families are the two <see cref="TextFontMetrics"/> measures with —
    /// stated here so the ledger's guard does not depend on reading two files to see that
    /// "bundled" means the same thing on both sides.
    /// </summary>
    [Fact]
    public void TheBundledNames_AreTheFamiliesTheMetricsRead()
    {
        Assert.Equal(TextFontMetrics.SerifFamily,
            TextFontPlan.BundledName(TextFontFamily.Serif));
        Assert.Equal(TextFontMetrics.SansFamily,
            TextFontPlan.BundledName(TextFontFamily.Sans));
    }
}
