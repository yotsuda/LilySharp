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
using System.Text.RegularExpressions;
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every word the language accepts must MOVE something — the engraved page or the played
/// notes. A spelling that moves neither is a dead word: the compiler takes it, the popup
/// may even offer it, and nothing in the output can tell whether it was written.
/// </summary>
/// <remarks>
/// ⚠️⚠️ THIS IS THE ONLY DETECTOR THAT WORKS, and session 374 paid for the knowledge.
/// A dead word appears in NO book (the one it found — the MIDI-only row's
/// <c>instrument</c> / <c>octave</c> — was written in 0 of the 27,095 <c>.lys</c> on the
/// owner's machine), so a corpus sweep cannot find one. Two cheap proxies were measured and
/// both fail:
/// <list type="bullet">
/// <item>"a child no public property of the red node reaches" — 72.2M nodes, 44 node types
/// flagged, almost all of them punctuation: consumers walk the tree generically with
/// <c>DescendantNodes&lt;T&gt;()</c>, so having no typed accessor is not being unread.</item>
/// <item>"a red type nobody names outside the syntax layer" — 2 of 72 types, both false
/// positives (<c>UsingDirectiveSyntax</c> is read by <c>Parser/UsingExpander</c>;
/// <c>AlternativeClauseSyntax</c> is reached through a property by the MIDI exporter and the
/// engraver).</item>
/// </list>
/// ⇒ Perturb the spelling and diff the output. That is what proved the MIDI options dead,
/// and it is what this file does for every vocabulary the editor offers.
/// <para>
/// ⚠️ AN "INERT" RESULT IS ONLY MEANINGFUL IF THE FIXTURE CAN EXPRESS THE DIFFERENCE. Three
/// of the first runs' inert readings were the fixture's fault, not the language's: a clef
/// tested against a part that already had that clef, hara-kiri tested on a book whose two
/// sections shared ONE system (so no staff was ever empty for a whole system — the
/// <c>break</c> in <see cref="HaraKiriBook"/> is load-bearing), and a policy word perturbed
/// by its LABEL where the popup inserts a label AND an operand (<c>barNumbers every 4</c>).
/// Every fixture below is the repaired one.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class VocabularyPerturbationTests
{
    /// <summary>The page and the playback of a book, as one comparable string.</summary>
    private static string Signature(string source)
    {
        var tree = SyntaxTree.Parse(source);
        var errors = tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Code + " " + d.Message)
            .ToArray();
        Assert.True(errors.Length == 0,
            "the fixture does not compile: " + string.Join(" | ", errors) + "\n" + source);

        string svg = SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false });
        string midi = string.Join(",", new MidiExporter().Export(tree).Tracks
            .SelectMany(t => t.Notes)
            .Select(n => $"{n.Pitch}:{n.StartTick}:{n.Channel}:{n.Timbre}"));
        return svg + "" + midi;
    }

    private static void AssertMoves(string baseline, string variant, string what)
        => Assert.True(Signature(baseline) != Signature(variant),
            $"'{what}' changes neither the engraved page nor the played notes — a dead word, "
            + "unless the fixture cannot express its effect (see this file's remarks).");

    /// <summary>A snippet as the editor inserts it: placeholders resolved, stops dropped.</summary>
    private static string Resolved(LilySharp.Lsp.Protocol.CompletionItem item)
    {
        string t = item.InsertText ?? item.Label ?? "";
        t = Regex.Replace(t, @"\$\{\d+:([^}]*)\}", "$1");
        return Regex.Replace(t, @"\$\{\d+\}|\$\d+", "").Trim();
    }

    private const string Plain =
        "octave absolute\npart m { clef treble\n  section A { c'4 d' e' f' | c'4 d' e' f' | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    // ===================== the `@` vocabulary =====================

    public static TheoryData<string> Articulations()
    {
        var data = new TheoryData<string>();
        foreach (var label in LilySharpLanguageServer.GetArticulationCompletions().Items
                     .Select(i => i.Label!).Distinct())
            data.Add(label);
        return data;
    }

    /// <summary>
    /// How a span is CLOSED, per family — a start without its end is LYS4018 and the
    /// fixture would read as "refused" rather than telling us anything.
    /// </summary>
    /// <remarks>
    /// ⚠️ The families do not all close the same way, and the difference is the language's,
    /// not a detail of this file: a text spanner and a pedal repeat their own name
    /// (<c>@!rit</c>, <c>@!sustain</c> — the fixtures in showcase/03-piano.lys write the
    /// pedal that way), while the ottava family is closed by <c>@!ottava</c> whatever
    /// spelling opened it (its digit spellings also accept their own). Guessing one rule for
    /// all of them is what made this sweep's first runs report five "dead words" that were
    /// only unclosed spans.
    /// </remarks>
    private static string SpanEnd(string name) => name switch
    {
        "rit" or "accel" or "rall" or "textSpan" => "@!" + name,
        "8va" or "8vb" or "15ma" or "15mb" => "@!" + name,
        "sustain" or "sostenuto" or "unaCorda" => "@!" + name,
        "ottava" or "ottava(bassa)" or "quindicesima" or "quindicesima(bassa)" => "@!ottava",
        _ => "",
    };

    [Theory]
    [MemberData(nameof(Articulations))]
    public void EveryArticulationMovesThePageOrThePlayback(string name)
    {
        string end = SpanEnd(name);
        string music = end.Length > 0
            ? $"c'4 d'4@{name} e'4 f'4{end} |"
            : $"c'4 d'4@{name} e'4 f'4 |";
        AssertMoves(Plain, Plain.Replace("c'4 d' e' f' | c'4 d' e' f' |", music), "@" + name);
    }

    // ===================== the layout switches =====================

    public static TheoryData<string, string> LayoutValues()
    {
        var data = new TheoryData<string, string>();
        void Add(string key, IEnumerable<LilySharp.Lsp.Protocol.CompletionItem> items)
        {
            foreach (var i in items) data.Add(key, Resolved(i));
        }
        Add("marks", LilySharpLanguageServer.GetMarkArrangementCompletions().Items);
        Add("barNumbers", LilySharpLanguageServer.GetBarNumberPolicyCompletions().Items);
        Add("accidentals", LilySharpLanguageServer.GetAccidentalStyleCompletions().Items);
        Add("sectionLabels", LilySharpLanguageServer.GetSectionLabelCompletions().Items);
        Add("partCombineText", LilySharpLanguageServer.GetPartCombineTextCompletions().Items);
        Add("chordQualities", LilySharpLanguageServer.GetChordQualityStyleCompletions().Items);
        Add("minorChords", LilySharpLanguageServer.GetMinorChordCompletions().Items);
        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutValues))]
    public void EveryLayoutValueMovesThePage(string key, string value)
    {
        // ⚠️ The VALUE is the item's insert text, not its label: `barNumbers every` is
        // LYS9103 on its own — the popup writes `every 4`, and perturbing with the label
        // reads as "refused" for a reason that is the harness's, not the language's.
        AssertMoves(Plain, $"layout {{ {key} {value} }}\n" + Plain, $"layout {key} {value}");
    }

    // ===================== the part header =====================

    [Theory]
    [InlineData("instrument violin")]
    [InlineData("tuning guitar")]
    [InlineData("transpose d")]
    [InlineData("octave 3")]
    [InlineData("pitch concert")]
    public void EveryPartPropertyMovesSomething(string property)
        => AssertMoves(Plain, Plain.Replace("part m { clef treble", "part m { clef treble " + property),
            "part " + property);

    [Theory]
    [InlineData("alto")]
    [InlineData("bass")]
    [InlineData("tenor")]
    [InlineData("treble")]
    [InlineData("treble_8")]
    public void EveryClefMovesThePage(string clef)
    {
        // ⚠️ Against a part with NO clef of its own — tested against a part that already
        // had `clef treble`, `treble` reads inert for the obvious wrong reason.
        const string noClef = "octave absolute\npart m {\n  section A { c'4 d' e' f' | }\n}\n"
                            + "form main { A }\nscore main { staff m }\n";
        AssertMoves(noClef, noClef.Replace("part m {", "part m { clef " + clef), "clef " + clef);
    }

    // ===================== the score row's selectors =====================

    /// <summary>Two parts, the second silent for a whole SYSTEM — what hara-kiri needs to
    /// have anything to hide. The <c>break</c> is why the second section starts a system of
    /// its own.</summary>
    private const string HaraKiriBook = """
        octave absolute
        part m { clef treble
          section A { c'4 d' e' f' | c'4 d' e' f' | break }
          section B { c'4 d' e' f' | c'4 d' e' f' | }
        }
        part n { clef bass
          section A { c4 d e f | c4 d e f | }
          section B { R1 | R1 | }
        }
        form main { A B }

        """;

    [Theory]
    [InlineData("true")]
    [InlineData("all")]
    public void HaraKiriHidesAStaffThatHasNothingToSay(string value)
        => AssertMoves(HaraKiriBook + "score main { staff m  staff n }\n",
            HaraKiriBook + $"score main {{ staff m  staff n as removeEmpty {value} }}\n",
            "as removeEmpty " + value);

    [Fact]
    public void HaraKiriOff_IsTheDefault_AndThereforeChangesNothing()
    {
        // ★ The one DOCUMENTED inert value in the language: `false` means "do not hide",
        // which is what the page already does. Asserted rather than skipped, so that if the
        // default ever changes, this test says so instead of the sweep reading it as a dead
        // word.
        Assert.Equal(
            Signature(HaraKiriBook + "score main { staff m  staff n }\n"),
            Signature(HaraKiriBook + "score main { staff m  staff n as removeEmpty false }\n"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    public void EveryStaffLineCountMovesThePage(string lines)
        => AssertMoves(Plain, Plain.Replace("score main { staff m }", $"score main {{ staff m as lines {lines} }}"),
            "as lines " + lines);

    // ===================== the drum table =====================

    private const string DrumBook =
        "octave absolute\npart m { clef percussion\n  section A { sn4 sn4 sn4 sn4 | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    [Theory]
    [InlineData("position 6")]
    [InlineData("notehead x")]
    [InlineData("midi 40")]
    [InlineData("mark accent")]
    public void EveryDrummapFieldMovesTheDrumItNames(string field)
        => AssertMoves(DrumBook, $"drummap {{\n  sn: {field}\n}}\n" + DrumBook, "drummap sn " + field);

    /// <summary>
    /// Two drum names that draw and sound EXACTLY alike are the same instrument under two
    /// spellings — which LilyPond does have (<c>crashcymbal</c> / <c>crashcymbala</c>) — so
    /// the collisions are listed rather than forbidden. What must not happen is a name
    /// colliding with an instrument it is not: the list below is the whole set, and every
    /// pair in it is one LilyPond itself spells twice.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS HOW <c>splashhihat</c> / <c>hhs</c> WAS FOUND (2026-09-12). It was not
    /// LilyPond's — <c>ly/drumpitch-init.ly</c> knows <c>splashcymbal</c> / <c>cyms</c>, a
    /// cymbal, and no splash hi-hat — and it carried <c>pedalhihat</c>'s row exactly, so the
    /// popup offered a splash and the page drew a pedal hi-hat. Removed; the real splash was
    /// already reachable two rows away.
    /// </remarks>
    [Fact]
    public void NoTwoDrumsShareARow_ExceptWhereLilyPondSpellsOneInstrumentTwice()
    {
        // A CANONICAL entry is one instrument. Two of them carrying the same staff position,
        // notehead, GM key and mark cannot be told apart on the page or in the .mid — so one
        // of the two names is lying about what it plays.
        var collisions = DrumNameRegistry.CanonicalEntries
            .GroupBy(e => $"{e.Value.StaffPosition}/{e.Value.Notehead}/{e.Value.GmKey}/{e.Value.Mark}",
                     StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("=", g.Select(e => e.Key).OrderBy(s => s, StringComparer.Ordinal)))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // LilyPond's own doubles, each verified in ly/drumpitch-init.ly (2.24.4):
        //   acousticsnare / snare  — two canonical names there too, same drums-style row
        //                            `() #f 1` and the same GM key (D3 = 38, spelled
        //                            NATURAL on one and DOUBLE-FLAT on the other);
        //   crashcymbal / crashcymbala, ridecymbal / ridecymbala — one instrument under two
        //                            names, same GM key;
        //   hisidestick / sidestick — LilyPond gives the kit's side stick (drums-style,
        //                            `cross #f 1`) and the timbale player's high rim
        //                            (timbales-style, `cross #f 1`) the same line, the same
        //                            head and the same GM key 37. `losidestick` is the pair's
        //                            other hand and sits a row lower, so it is not here.
        // This table keeps both spellings because a writer may have either in hand.
        // ⚠️ A NEW LINE HERE IS A CLAIM ABOUT LILYPOND — check ly/drumpitch-init.ly before
        // adding one. `splashhihat`=`pedalhihat` was such a line waiting to happen: a name
        // LilyPond does not have, carrying pedalhihat's row, so `hhs` drew and played a
        // pedal hi-hat while the popup called it a splash (removed 2026-09-12).
        Assert.Equal(
            new[]
            {
                "acousticsnare=snare", "crashcymbal=crashcymbala",
                "hisidestick=sidestick", "ridecymbal=ridecymbala",
            },
            collisions);
    }
}
