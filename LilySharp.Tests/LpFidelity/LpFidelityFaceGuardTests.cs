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
using LilySharp.Core.Music;
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

    /// <summary>
    /// The characters a chord symbol can print that the face measuring them HAS NO GLYPH
    /// FOR — a named list, which may shrink and may never grow.
    /// </summary>
    /// <remarks>
    /// ⚠️ A LIST AND NOT A COUNT, for the reason the citation ratchet is one
    /// (HANDOFF §5.2.1⑦): a count lets an old member stay open while a new one arrives.
    /// <para>
    /// ★ THE TWO MEMBERS ARE ONE DEFECT, measured 2026-08-25 (session 254).
    /// <c>ChordStructure.SpellPitch</c> spells an altered root with U+266F / U+266D, and
    /// TeX Gyre Heros — the face <see cref="TextRole.ChordName"/> resolves to, and the one
    /// the test above requires it to resolve to — carries neither. So for every altered
    /// chord symbol in the language:
    /// </para>
    /// <list type="bullet">
    /// <item>the metrics read the .notdef box: ink <c>(0,0)</c> and advance 1.297445669,
    /// which is what <c>U+FFFD</c> reads too — asserted below, because a ZERO-INK reading
    /// alone cannot tell "absent" from "blank" and only the first is a defect;</item>
    /// <item>so a chord accidental is in NO skyline. <c>ChordNameEngraver.RowSkylines</c>
    /// merges the symbol's real ink, and that ink is the letter's alone — measured, `A♯m'
    /// and `Am' report the same box to nine digits;</item>
    /// <item>while the DRAWN glyph comes from whatever the platform's fallback supplies
    /// (verified: <c>lysc png</c> prints a full-size baseline ♯). That is the pollution
    /// <c>b69c73e6</c> removed from the probe path, arriving through the draw path
    /// instead — the picture is a function of the machine.</item>
    /// </list>
    /// <para>
    /// ⇒ THE FIX IS A PORT AND IT HAS AN ADDRESS, so this list is expected to empty rather
    /// than to be lived with: LilyPond does not put an accidental CHARACTER in a chord name
    /// at all. <c>scm/chord-name.scm:80-95 accidental-&gt;text-markup</c> /
    /// <c>accidental-&gt;markup</c> builds it as the Emmentaler ACCIDENTAL GLYPH, one step
    /// <c>smaller</c>, <c>translate-scaled</c> up by 0.6 (0.3 for the flat family), with a
    /// 0.094725 kern before the narrow glyphs. Measured in 2.26.0: ChordName `Am' is
    /// (0.0 . 1.907290480437992) and `A♯m' is (-0.9535167849233657 . 2.22487249815452) —
    /// the raised glyph adds 0.317582 on top and 0.953517 below, 1.271099 of ink height in
    /// all, which is the number ledger <c>mark.over-chord.*</c> reads as the pair's whole
    /// difference. Lily# has the glyphs and their outlines already
    /// (<c>EmmentalerGlyphs</c> / <c>GlyphMetrics</c>) and the run machinery to mix them
    /// with text (<c>FetaTextRun</c>, three consumers); what it has not got is a fourth
    /// consumer for the chord name.
    /// </para>
    /// </remarks>
    private static readonly char[] ChordCharactersTheFaceCannotDraw = ['♭', '♯'];

    /// <summary>
    /// Every character the chord namer can print is a character the face that MEASURES it
    /// can actually draw — except the named list above.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE VOCABULARY IS ENUMERATED FROM THE NAMER, NOT SPELLED HERE. A hand-written
    /// alphabet is a second spelling of the language (HANDOFF §5.2.1②) and would keep
    /// reading green on the day a quality suffix grows a character the face has not got —
    /// which is exactly the shape this test exists for. The sweep runs
    /// <see cref="ChordStructure.DisplayName"/> and
    /// <see cref="ChordStructure.ToRomanNumeral"/> over every root, alteration, quality and
    /// both bass forms, and adds the one literal the collector prints without a structure
    /// (<c>ly/engraver-init.ly:952 noChordSymbol</c> = "N.C.").
    /// <para>
    /// ⚠️ Whitespace is excluded rather than filtered by ink: a space HAS no ink and is
    /// not a missing glyph (<c>m maj7</c> contains one), so including it would put a
    /// non-defect in the list and hide the population it is supposed to name.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCharacterAChordSymbolCanPrint_HasInkInTheFaceThatMeasuresIt()
    {
        var fonts = ScoreTextMetrics.Bundled;
        double em = EngravingDefaults.ChordNameFontSize;

        (double Bottom, double Top) Ink(string s) =>
            fonts.Ink(s, em, TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);
        double Advance(string s) =>
            fonts.Advance(s, em, TextRole.ChordName, EngravingDefaults.ChordNameFontStyle);

        var vocabulary = new SortedSet<char>();
        void Take(string printed)
        {
            foreach (char c in printed)
                if (!char.IsWhiteSpace(c))
                    vocabulary.Add(c);
        }

        foreach (ChordQuality quality in System.Enum.GetValues<ChordQuality>())
            for (int rootStep = 0; rootStep < 7; rootStep++)
                for (int rootAlter = -2; rootAlter <= 2; rootAlter++)
                {
                    Take(new ChordStructure(rootStep, rootAlter, quality).DisplayName);
                    Take(new ChordStructure(rootStep, rootAlter, quality,
                        BassStep: (rootStep + 4) % 7, BassAlter: rootAlter).DisplayName);
                    // Every key, so a root's degree is reached both diatonic and chromatic
                    // — the ♯/♭ prefix of a roman degree is written by the SAME sweep.
                    for (int tonicStep = 0; tonicStep < 7; tonicStep++)
                        for (int keySharps = -7; keySharps <= 7; keySharps++)
                            Take(new ChordStructure(rootStep, rootAlter, quality)
                                .ToRomanNumeral(tonicStep, keySharps));
                }
        Take("N.C.");

        // The population, asserted before the emptiness is (HANDOFF RULES §5.4): a sweep
        // that enumerated nothing would report an empty defect list and read as green.
        Assert.True(vocabulary.Count >= 25,
            $"the chord namer's alphabet came out at only {vocabulary.Count} characters — "
            + "the sweep stopped reaching the namer, so this guard is covering less than it "
            + "reads as covering.");

        // What "the face has no glyph for this" READS AS, taken from a character no text
        // face carries. Without this the test below could not tell an absent glyph from a
        // blank one, and would name the wrong defect.
        double notdefAdvance = Advance("�");
        Assert.Equal(0.0, Ink("�").Top - Ink("�").Bottom, 9);

        var missing = new List<char>();
        foreach (char c in vocabulary)
        {
            var (bottom, top) = Ink(c.ToString());
            if (top - bottom > 0) continue;
            missing.Add(c);
            Assert.Equal(notdefAdvance, Advance(c.ToString()), 9);
        }

        Assert.Equal(ChordCharactersTheFaceCannotDraw, missing.ToArray());
    }
}
