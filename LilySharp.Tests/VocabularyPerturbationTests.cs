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
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using LilySharp.Core.Tablature;
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

        // ⚠️⚠️ data-pos IS MASKED, and masking it is the whole validity of this file.
        // It carries each annotation's SOURCE OFFSET, so ANY perturbation written before the
        // music — every part property, every `layout { }` block, every `drummap { }` — shifts
        // every later data-pos merely by being a different NUMBER OF CHARACTERS. Unmasked,
        // `AssertMoves` passes for such a word whether or not the word does anything, and a
        // dead one reads alive. Found 2026-09-13 by a positive control: `tuning standard` and
        // `tuning guitar` are the SAME tuning, and their pages differed — at `data-pos`, by
        // the two characters of the longer word. The rest of the repository has masked this
        // attribute since 2026-08-15 whenever it compares two books (Diagnostic.cs's remarks
        // say so in four places); this file was the one comparer that did not.
        string svg = Regex.Replace(
            SvgGenerator.Generate(tree, new SvgRenderOptions { EmbedFont = false }),
            @"\sdata-pos=""[^""]*""", "");
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

    // ===================== the `@` forms that take an OPERAND =====================

    /// <summary>
    /// An annotation that takes an argument must read it: two books differing ONLY in the
    /// operand have to draw or play differently.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ THE LABEL SWEEP ABOVE CANNOT ASK THIS. It perturbs <c>@name</c> against a book
    /// without it, so an annotation whose argument is parsed and then dropped still moves the
    /// page — by the annotation's own ink — and reads alive. The operand is a dead word
    /// INSIDE a live word, which is the MIDI row's defect (session 374's 12th leg) one level
    /// down, and nothing had asked about it: this file's own remark records that the first
    /// runs perturbed by the popup's LABEL where the insert text carries an operand.
    /// <para>
    /// The pairs are two legal values of the same argument, so anything that differs is the
    /// argument and not the annotation.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("@fig(6)", "@fig(6 4)")]
    [InlineData("@chord(C)", "@chord(Dm)")]
    [InlineData("@finger(1)", "@finger(3)")]
    [InlineData("@mark(\"A\")", "@mark(\"B\")")]
    [InlineData("@text(\"dolce\")", "@text(\"pizz.\")")]
    public void EveryAnnotationOperandReachesThePage(string written, string other)
        => AssertMoves(OnFirstNote(written), OnFirstNote(other), written + " vs " + other);

    private static string OnFirstNote(string annotation) =>
        Plain.Replace("c'4 d' e' f' | c'4 d' e' f' |",
                      $"c'4{annotation} d' e' f' | c'4 d' e' f' |");

    /// <summary>Sixteenths under one beam — what a feathered beam needs to be a beam.</summary>
    private static string BeamedBook(string annotation) =>
        Plain.Replace("c'4 d' e' f' | c'4 d' e' f' |",
                      $"c'16{annotation} d' e' f' g' a' b' c'' | c'4 d' e' f' |");

    public static TheoryData<string> ArgumentValues(
        System.Func<LilySharp.Lsp.Protocol.CompletionList> list)
    {
        var data = new TheoryData<string>();
        foreach (var i in list().Items) data.Add(Resolved(i));
        return data;
    }

    public static TheoryData<string> FeatherDirections()
        => ArgumentValues(LilySharpLanguageServer.GetFeatherCompletions);

    public static TheoryData<string> BendAmounts()
        => ArgumentValues(LilySharpLanguageServer.GetBendCompletions);

    public static TheoryData<string> PluckFingers()
        => ArgumentValues(LilySharpLanguageServer.GetPluckCompletions);

    public static TheoryData<string> FiguredBassFigures()
        => ArgumentValues(LilySharpLanguageServer.GetFiguredBassCompletions);

    /// <summary>
    /// Every feather direction reaches the page, and the two DIRECTIONS differ from each
    /// other — so the operand is read, not merely accepted.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE SWEEP THAT FOUND THE FEATHER UNDRAWN (2026-09-13), and it is kept in
    /// the "must move" form rather than as the equality that pinned the defect for one leg.
    /// What it cost to find is worth the two lines: the annotation was parsed, validated,
    /// carried into <c>BeamGroup.GrowDirection</c>, copied, passed through two coordinators
    /// and folded into the beam memo key — and read by no renderer, while three tautologies
    /// in <c>FeatheredBeamTests</c> stood in for the question by doing their own arithmetic.
    /// ⚠️ The synonyms are the positive control the popup itself declares: <c>accel</c> IS
    /// <c>right</c> and <c>rit</c> IS <c>left</c>, so those must be byte-identical; if they
    /// ever differ, the popup's Detail is wrong or the reader has drifted from it.
    /// </remarks>
    [Fact]
    public void EveryFeatherDirectionReachesThePage()
    {
        string plain = Signature(BeamedBook(""));
        foreach (var item in LilySharpLanguageServer.GetFeatherCompletions().Items)
            Assert.True(plain != Signature(BeamedBook($"@feather({Resolved(item)})")),
                $"@feather({Resolved(item)}) changes nothing on the page.");

        Assert.Equal(Signature(BeamedBook("@feather(right)")), Signature(BeamedBook("@feather(accel)")));
        Assert.Equal(Signature(BeamedBook("@feather(left)")), Signature(BeamedBook("@feather(rit)")));
        AssertMoves(BeamedBook("@feather(right)"), BeamedBook("@feather(left)"),
            "@feather right vs left");
    }

    [Theory]
    [MemberData(nameof(PluckFingers))]
    public void EveryPluckFingerReachesThePage(string value)
        => AssertMoves(OnFirstNote(""), OnFirstNote($"@pluck({value})"), "@pluck(" + value + ")");

    [Theory]
    [MemberData(nameof(FiguredBassFigures))]
    public void EveryFiguredBassFigureReachesThePage(string value)
        => AssertMoves(OnFirstNote(""), OnFirstNote($"@fig({value})"), "@fig(" + value + ")");

    /// <summary>Every bend amount is a DIFFERENT height, so no two may draw alike.</summary>
    [Fact]
    public void NoTwoBendAmountsDrawAlike()
    {
        string[] values = [.. LilySharpLanguageServer.GetBendCompletions().Items.Select(Resolved)];
        var alike = values
            .GroupBy(v => Signature(TabBendBook(v)), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("=", g))
            .ToArray();
        Assert.Empty(alike);
    }

    /// <summary>A tab staff, which is where a string bend is drawn.</summary>
    private static string TabBendBook(string amount) =>
        "octave absolute\npart m { clef treble_8 tuning guitar\n"
        + $"  section A {{ e,4@bend({amount}) a, d g | }}\n}}\n"
        + "form main { A }\nscore main { tab m }\n";

    // ===================== the layout switches =====================

    // ⚠️⚠️ A LAYOUT KEY NEEDS A BOOK THAT CAN SHOW IT. `Plain` has two bars on ONE system,
    // no accidentals, no chord row, no second part and no section label — so it can express
    // almost none of these keys, and twelve of the sixteen values read inert against it for
    // the fixture's reason. That was invisible until data-pos was masked (2026-09-13): the
    // `layout { }` block is written BEFORE the music, so it moved every later source offset
    // and the sweep called every value alive. One book per key, each chosen to contain the
    // thing the key governs.
    private const string ManySystems =
        "octave absolute\npart m { clef treble\n"
        + "  section A { c'4 d' e' f' | break g'4 a' b' c'' | break d''4 e'' f'' g'' | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    /// <summary>
    /// The three situations the styles actually differ about, in one book — read from
    /// <see cref="LilySharp.Core.Semantics.AccidentalStyles"/>'s own rule sets rather than
    /// guessed: <c>default</c> is <c>extraNatural</c> + same-octave-0, <c>modern</c> drops
    /// the extra natural and adds any-octave-0 and same-octave-1, and
    /// <c>modernCautionary</c> prints those two additions as CAUTIONARY instead.
    /// </summary>
    /// <remarks>
    /// ⚠️ A book of plain repeated sharps shows NONE of this — every style prints the same
    /// picture — which is why the first fixture here read two styles as dead words.
    /// <list type="number">
    /// <item><c>cisis'</c> then <c>cis'</c>: the extra natural (<c>♮♯</c> under default,
    /// <c>♯</c> under both modern styles).</item>
    /// <item><c>cis'</c> then <c>c''</c>: any-octave 0 — the other octave is cancelled under
    /// modern, silent under default.</item>
    /// <item><c>cis'</c> in bar 1, <c>c'</c> in bar 2: same-octave 1 — cancelled in the NEXT
    /// measure under modern, silent under default.</item>
    /// </list>
    /// </remarks>
    private const string WithAccidentals =
        "octave absolute\npart m { clef treble\n"
        + "  section A { cisis'4 cis' c'' d' | c'4 d' e' f' | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    private const string TwoLabelledSections =
        "octave absolute\npart m { clef treble\n"
        + "  section A { c'4 d' e' f' | }\n  section B { g'4 a' b' c'' | }\n}\n"
        + "form main { A B }\nscore main { staff m }\n";

    /// <summary>Two parts on ONE staff, the second silent in the second bar — which is what
    /// makes the combiner print its texts at all.</summary>
    private const string CombinedParts =
        "octave absolute\npart one { clef treble }\npart two { clef treble }\n"
        + "section A { one { c'1 | d'1 | } two { c'1 | r1 | } }\n"
        + "form main { A }\nscore main { combinedStaff { one two } }\n";

    /// <summary>A chord row of the qualities LilyPond names with a symbol, plus a minor
    /// seventh over a slash bass — so both chord keys have something to spell.</summary>
    private const string ChordRow =
        "time 4/4\noctave absolute\npart m { clef treble }\n"
        + "section A { m { c'4 d' e' f' | c'4 d' e' f' | c'4 d' e' f' | }\n"
        + "  chords prog { Cdim | Cm7-5 | Am7/C | } }\n"
        + "form main { ~A }\nscore main { chords prog  staff m }\n";

    private static string BookFor(string key) => key switch
    {
        "barNumbers" => ManySystems,
        "accidentals" => WithAccidentals,
        "sectionLabels" or "marks" => TwoLabelledSections,
        "partCombineText" => CombinedParts,
        "chordQualities" or "minorChords" => ChordRow,
        _ => Plain,
    };

    public static TheoryData<string, string, bool> LayoutValues()
    {
        var data = new TheoryData<string, string, bool>();
        void Add(string key, IReadOnlyCollection<string> vocabulary,
                 IEnumerable<LilySharp.Lsp.Protocol.CompletionItem> items)
        {
            // ★ THE DEFAULT IS THE VOCABULARY'S FIRST WORD — LanguageVocabulary says so for
            // every one of these keys ("the default first"), so the net reads it instead of
            // carrying a hand-kept list that could drift from the compiler.
            string @default = vocabulary.First();
            foreach (var i in items)
            {
                string value = Resolved(i);
                data.Add(key, value, value.Split(' ')[0] == @default);
            }
        }
        Add("marks", LanguageVocabulary.MarkArrangements,
            LilySharpLanguageServer.GetMarkArrangementCompletions().Items);
        Add("barNumbers", LanguageVocabulary.BarNumberPolicies,
            LilySharpLanguageServer.GetBarNumberPolicyCompletions().Items);
        Add("accidentals", LanguageVocabulary.AccidentalStyleWords,
            LilySharpLanguageServer.GetAccidentalStyleCompletions().Items);
        Add("sectionLabels", LanguageVocabulary.SectionLabelStyles,
            LilySharpLanguageServer.GetSectionLabelCompletions().Items);
        Add("partCombineText", LanguageVocabulary.PartCombineTextWords,
            LilySharpLanguageServer.GetPartCombineTextCompletions().Items);
        Add("chordQualities", LanguageVocabulary.ChordQualityStyleWords,
            LilySharpLanguageServer.GetChordQualityStyleCompletions().Items);
        Add("minorChords", LanguageVocabulary.MinorChordWords,
            LilySharpLanguageServer.GetMinorChordCompletions().Items);
        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutValues))]
    public void EveryLayoutValueMovesThePage(string key, string value, bool isDefault)
    {
        // ⚠️ The VALUE is the item's insert text, not its label: `barNumbers every` is
        // LYS9103 on its own — the popup writes `every 4`, and perturbing with the label
        // reads as "refused" for a reason that is the harness's, not the language's.
        string book = BookFor(key);
        string written = $"layout {{ {key} {value} }}\n" + book;

        // ★ Writing the DEFAULT out is the one kind of inert this file accepts, and it is
        // asserted rather than skipped (as `as removeEmpty false` is): the day a default
        // changes, this says so instead of the sweep reading a dead word.
        if (isDefault)
        {
            Assert.Equal(Signature(book), Signature(written));
            return;
        }
        AssertMoves(book, written, $"layout {key} {value}");
    }

    // ===================== the part header =====================

    /// <remarks>
    /// ⚠️ <c>pitch</c> needs a TRANSPOSING part — it says whether the letters are sounding or
    /// written, and on a part that does not transpose the two readings are the same page and
    /// the same notes. <c>alsoInTheHeader</c> is what the baseline must already carry for the
    /// property under test to have anything to say (2026-09-13; before that `pitch concert`
    /// was measured against a plain treble part and read alive only because the layout block
    /// shifted every data-pos).
    /// ⚠️ <c>tuning</c> is NOT here: <c>Plain</c> is engraved as a STAFF, and a tuning shows
    /// itself only in fret numbers. It has its own sweep over the whole vocabulary against a
    /// tab book — <see cref="EveryTuningFretsDifferentlyFromTheGuitar"/>.
    /// </remarks>
    [Theory]
    [InlineData("instrument violin", "")]
    [InlineData("transpose d", "")]
    [InlineData("octave 3", "")]
    // ⚠️ `instrument clarinet`, not `transposition 8vb`: ConcertPitch reads the PRESET's
    // chromatic shift (InstrumentDefaults.GetTransposition, −2 for the B♭ clarinet), and an
    // octave marker is a different channel that leaves it with nothing to negate.
    [InlineData("pitch concert", "instrument clarinet")]
    public void EveryPartPropertyMovesSomething(string property, string alsoInTheHeader)
    {
        string baseline = Plain.Replace("part m { clef treble", "part m { clef treble " + alsoInTheHeader);
        AssertMoves(baseline,
            baseline.Replace("part m { clef treble", "part m { clef treble " + property),
            "part " + property);
    }

    /// <summary>A part with NO clef of its own — a clef tested against a part that already
    /// had one reads inert for the obvious wrong reason (this file's first lesson).</summary>
    private const string NoClef =
        "octave absolute\npart m {\n  section A { c'4 d' e' f' | }\n}\n"
        + "form main { A }\nscore main { staff m }\n";

    [Theory]
    [InlineData("alto")]
    [InlineData("bass")]
    [InlineData("tenor")]
    [InlineData("treble_8")]
    public void EveryClefMovesThePage(string clef)
        => AssertMoves(NoClef, NoClef.Replace("part m {", "part m { clef " + clef), "clef " + clef);

    [Fact]
    public void TheTrebleClef_IsTheDefault_AndThereforeChangesNothing()
        // ★ The second documented inert value, asserted rather than skipped for the same
        // reason as `as removeEmpty false`: writing a default out must not move the page, and
        // the day treble stops being the default this says so instead of the sweep calling
        // `clef treble` a dead word.
        => Assert.Equal(Signature(NoClef), Signature(NoClef.Replace("part m {", "part m { clef treble")));

    // ===================== the paper block =====================

    /// <summary>
    /// A book with room for every paper key to show: a title (so the markup-to-system keys
    /// have a markup), two staves in a group (so the staff-to-staff keys have a pair and a
    /// bracket), a lyrics row (the non-staff line), and three systems (so the
    /// system-to-system keys, <c>shortIndent</c> and <c>raggedBottom</c> have more than one).
    /// </summary>
    private const string PaperBook = """
        octave absolute
        title "T"
        composer "C"
        part m { clef treble
          section A { c'4 d' e' f' | break g'4 a' b' c'' | break d''4 e'' f'' g'' | }
        }
        part n { clef bass
          section A { c4 d e f | break g4 a b c' | break d'4 e' f' g' | }
        }
        part o { clef treble
          section A { e'4 f' g' a' | break b'4 c'' d'' e'' | break f''4 g'' a'' b'' | }
        }
        lyrics w sings m { section A { la la la la | la la la la | la la la la | } }
        lyrics v sings n { section A { do do do do | do do do do | do do do do | } }
        form main { A }
        score main { staffGroup { staff m  staff n }  lyrics w  lyrics v  staff o }

        """;

    private static string PaperBookWith(string entry) =>
        "paper { " + entry + " }\n" + PaperBook;

    /// <summary>
    /// A book that runs onto a SECOND page, so the first one is not the last and is justified
    /// vertically.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ WITHOUT THIS, EVERY SPRING QUANTITY IS INERT AND FOR ONE REASON:
    /// <c>RaggedLastBottom</c> defaults to TRUE — LilyPond's own default, "best for shorter
    /// scores" (ly/paper-defaults-init.ly:56) — so the LAST page keeps its natural spacing.
    /// A one-page book IS its last page, so nothing ever spreads on it, and
    /// <c>raggedBottom</c>, <c>lastBottomSpacing</c> and <c>topSystemSpacing</c> all have
    /// nothing to do. That is a property of the FIXTURE, not of those three keys, and it is why
    /// they sat on the can't-speak-for list for one leg (2026-09-13). ⚠️ The same leg also
    /// named <c>topSystemPadding</c> and <c>stretchability</c> as fixture-bound; both stay
    /// inert here, for other reasons — no reader, and agreement with LilyPond (session 377).
    /// </remarks>
    private static string FilledPageBook(string entry)
    {
        var music = new System.Text.StringBuilder();
        for (int i = 0; i < 24; i++)
            music.Append("c'4 d' e' f' | break ");
        return "paper { " + entry + " }\n"
            + "octave absolute\ntitle \"T\"\npart m { clef treble\n"
            + "  section A { " + music + "}\n}\n"
            + "form main { A }\nscore main { staff m }\n";
    }

    /// <summary>
    /// The paper keys LilyPond ITSELF ignores in a one-score book — measured, not assumed.
    /// </summary>
    /// <remarks>
    /// LilyPond 2.26.0 on the <c>lysc ly --pin-fonts</c> twin of <see cref="PaperBook"/>
    /// (session 377, scratch/p378/paper/lp-pairs.ps1: two values each, svg hashes) draws the
    /// same page for <c>score-system-spacing</c>, <c>score-markup-spacing</c> and
    /// <c>markup-markup-spacing</c>, while the positive control <c>markup-system-spacing</c>
    /// moves it. The selection says why (lily/page-layout-problem.cc:503-525): the score spec
    /// wants a system that opens a SECOND score, the score-markup spec a markup after a system,
    /// the markup-markup spec two markups in a row — a book with one score and one header has
    /// none. Lily#'s <c>VerticalSpacingParameters.SelectSpec</c> makes the same selection, so
    /// inert here is the port being faithful, not a dead word.
    /// </remarks>
    private static readonly string[] PaperKeysLilyPondAlsoIgnoresInOneScore =
        ["scoreSystemSpacing", "scoreMarkupSpacing", "markupMarkupSpacing"];

    /// <summary>
    /// The two non-staff specs whose <c>basicDistance</c> LilyPond also ignores on this book —
    /// so their reach is asked with <c>padding</c> instead.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-09-13 (session 377, scratch/p378/paper): on the twin of
    /// <see cref="PaperBook"/>, LilyPond 2.26.0 draws the same page for <c>basic-distance</c> 2
    /// and 30 of <c>nonstaff-unrelatedstaff-spacing</c> and <c>nonstaff-nonstaff-spacing</c>
    /// (and of <c>nonstaff-relatedstaff-spacing</c>), while their <c>padding</c> /
    /// <c>minimum-distance</c> move it — and Lily# agrees on all four readings. ★ NOT A
    /// RAGGED-PAGE ARTEFACT: the same pairs on a JUSTIFIED book (two staves with two lyrics
    /// lines between, 24 systems on 4 A4 pages in both engines — scratch/p378/paper/justified.ps1)
    /// leave all three <c>basic-distance</c>s inert in BOTH engines, while the
    /// <c>minimum-distance</c> and <c>padding</c> controls move both. WHY LilyPond's loose-line
    /// spring ideal does not show has not been read.
    /// ⚠️ <c>nonStaffRelatedStaffSpacing</c> stays on the plain sweep, but what moves there is
    /// only the content-sized page's HEIGHT (129.02 → 145.22; no drawn element moves) — a
    /// Lily#-only quantity, since LilyPond's page has a fixed size (HANDOFF §2 E).
    /// </remarks>
    private static readonly string[] PaperKeysAskedByPadding =
        ["nonStaffUnrelatedStaffSpacing", "nonStaffNonStaffSpacing"];

    /// <summary>
    /// <see cref="PaperBook"/> with three UNGROUPED staves — what
    /// <c>defaultStaffStaffSpacing</c> needs to have anything to space.
    /// </summary>
    /// <remarks>
    /// <see cref="PaperBook"/> opens with a bracketed group, so every pair it spaces has a
    /// grouper above it and <c>MultiStaffLayouter.SelectInterGroupSpec</c> never reaches the
    /// default spec — the key sat on the "unsettled" list for that reason alone. Against three
    /// ungrouped staves it moves, and so does LilyPond 2.26.0's
    /// <c>default-staff-staff-spacing.basic-distance</c> 2 / 30 (session 377, scratch/p378/paper).
    /// </remarks>
    private static string UngroupedPaperBookWith(string entry) =>
        PaperBookWith(entry).Replace(
            "score main { staffGroup { staff m  staff n }  lyrics w  lyrics v  staff o }",
            "score main { staff m  staff n  staff o }");

    /// <summary>
    /// The keys that want a page which must SPREAD — measured against a two-page book, where
    /// the first page is justified because it is not the last.
    /// </summary>
    /// <remarks>See <see cref="FilledPageBook"/> for why a one-page fixture can say nothing
    /// about any of them.</remarks>
    private static readonly string[] PaperKeysThatNeedAJustifiedPage =
        ["topSystemSpacing", "lastBottomSpacing"];

    /// <summary>
    /// The keys that want a line which is NOT stretched — asked on <see cref="PaperBook"/> with
    /// <c>raggedRight</c>.
    /// </summary>
    /// <remarks>
    /// <c>spacingIncrement</c> scales every duration spring of <see cref="PaperBook"/>'s lines
    /// alike (each is one bar of equal quarters), and a justified line stretched to the same
    /// width puts equal springs back where they were: MEASURED on this book (session 379,
    /// scratch/p380/incr/vp), 5mm and 25mm draw the same justified page and different ragged
    /// ones. That is the fixture, not a dead word — the key is wired and reaches LilyPond's
    /// lengths (<see cref="SpacingIncrementTests"/>).
    /// </remarks>
    private static readonly string[] PaperKeysThatNeedARaggedLine = ["spacingIncrement"];

    public static TheoryData<string, string, string> PaperEntries()
    {
        var data = new TheoryData<string, string, string>();
        data.Add("size", "size a4", "size a6");
        foreach (string key in LanguageVocabulary.PaperScalarKeys)
            data.Add(key, $"{key} 5mm", $"{key} 25mm");
        foreach (string key in LanguageVocabulary.PaperSpacingKeys)
            data.Add(key, key + " { basicDistance 2 }", key + " { basicDistance 30 }");
        return data;
    }

    /// <summary>
    /// Every paper key must reach the page — asked as "two DIFFERENT values of the same key
    /// draw differently", which needs no knowledge of the key's default and cannot be fooled
    /// by writing one.
    /// </summary>
    /// <remarks>
    /// ★ THE SHAPE IS THE POINT. The other sweeps in this file compare a spelling against a
    /// book WITHOUT it, which forces a separate decision about every value that happens to be
    /// the default (four of them are asserted inert above). A dimension has no such problem:
    /// if <c>indent 5</c> and <c>indent 20</c> draw the same page, the key is dead whatever
    /// its default is.
    /// <para>
    /// ★★ NO KEY IS "UNSETTLED" ANY MORE (session 377). The eight the sweep once could not speak
    /// for split four ways, and each way is a different claim: a fixture that could not express
    /// the key (<see cref="FilledPageBook"/>, <see cref="UngroupedPaperBookWith"/>), a sub-value
    /// LilyPond ignores too (<see cref="PaperKeysAskedByPadding"/>), a key LilyPond ignores in a
    /// one-score book (<see cref="PaperKeysLilyPondAlsoIgnoresInOneScore"/>), and dead words
    /// (spacingIncrement, since wired, and topSystemPadding, since retired — session 379).
    /// ⇒ AN INERT READING IS SETTLED ONLY BY ASKING
    /// LILYPOND THE SAME QUESTION — two of the three earlier attempts (a richer fixture,
    /// counting readers by grep) could not tell "the fixture cannot say it" from "LilyPond
    /// does not do it either" from "nothing reads it".
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PaperEntries))]
    public void EveryPaperKeyMovesThePage(string key, string small, string large)
    {
        if (PaperKeysThatNeedARaggedLine.Contains(key))
        {
            AssertMoves(PaperBookWith("raggedRight  " + small), PaperBookWith("raggedRight  " + large),
                "paper " + key);
            return;
        }
        if (PaperKeysThatNeedAJustifiedPage.Contains(key))
        {
            AssertMoves(FilledPageBook(small), FilledPageBook(large), "paper " + key);
            return;
        }
        if (key == "defaultStaffStaffSpacing")
        {
            AssertMoves(UngroupedPaperBookWith(small), UngroupedPaperBookWith(large), "paper " + key);
            return;
        }
        if (PaperKeysAskedByPadding.Contains(key))
        {
            // basicDistance is inert in LilyPond too on this book — see the list's remark.
            Assert.True(Signature(PaperBookWith(small)) == Signature(PaperBookWith(large)),
                $"'{key}' basicDistance now moves the page, where LilyPond 2.26.0's did not "
                + "(session 377, scratch/p378/paper) — re-measure the twin before believing it.");
            AssertMoves(PaperBookWith(key + " { padding 2 }"), PaperBookWith(key + " { padding 30 }"),
                "paper " + key + " padding");
            return;
        }
        if (PaperKeysLilyPondAlsoIgnoresInOneScore.Contains(key))
        {
            Assert.True(Signature(PaperBookWith(small)) == Signature(PaperBookWith(large)),
                $"'{key}' now moves a one-score book, where LilyPond 2.26.0 does not "
                + "(session 377, scratch/p378/paper) — a divergence, not a fix.");
            return;
        }
        AssertMoves(PaperBookWith(small), PaperBookWith(large), "paper " + key);
    }

    /// <summary>The two bare flags, which have no second value — on against absent.</summary>
    /// <remarks>⚠️ <c>raggedBottom</c> is inert against <see cref="PaperBook"/> — a one-page
    /// book is its own last page, which ragged-last-bottom already leaves ragged — so it is
    /// asked against <see cref="FilledPageBook"/>, where it moves.</remarks>
    [Theory]
    [MemberData(nameof(PaperFlags))]
    public void EveryPaperFlagMovesThePage(string flag)
    {
        if (flag == "raggedBottom")
        {
            // Against a book that must SPREAD: on a one-page book `raggedBottom` is a no-op
            // because ragged-last-bottom has already made that page ragged.
            AssertMoves(FilledPageBook(""), FilledPageBook(flag), "paper " + flag);
            return;
        }
        AssertMoves(PaperBook, PaperBookWith(flag), "paper " + flag);
    }

    public static TheoryData<string> PaperFlags()
    {
        var data = new TheoryData<string>();
        foreach (string flag in LanguageVocabulary.PaperFlagKeys) data.Add(flag);
        return data;
    }

    /// <summary>
    /// ★★ THE POSITIVE CONTROL: <c>size a4</c> IS the default page, so writing it must change
    /// nothing — and if it does, this sweep's "two values differ" readings prove nothing about
    /// the keys, only that the paper block's presence moves the page.
    /// </summary>
    [Fact]
    public void WritingTheDefaultPaperSize_ChangesNothing()
        => Assert.Equal(Signature(PaperBook), Signature(PaperBookWith("size a4")));

    /// <summary>Every spacing sub-key inside one block, the same way.</summary>
    /// <remarks>
    /// ⚠️ <c>stretchability</c> of <c>systemSystemSpacing</c> is inert on both books, and
    /// LilyPond's is too: MEASURED 2026-09-13 (session 377, scratch/p378/paper/lp2) on the
    /// <c>lysc ly --pin-fonts</c> twin of <see cref="FilledPageBook"/>, LilyPond 2.26.0 draws
    /// the same two pages for <c>system-system-spacing.stretchability</c> 2 and 30, while the
    /// positive control <c>last-bottom-spacing.basic-distance</c> moves them. Why neither
    /// engine's pages show it has not been read; the claim pinned is the agreement.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PaperSubKeys))]
    public void EverySpacingSubKeyMovesThePage(string subKey)
    {
        string entrySmall = $"systemSystemSpacing {{ {subKey} 2 }}";
        string entryLarge = $"systemSystemSpacing {{ {subKey} 30 }}";
        if (subKey == "stretchability")
        {
            // Inert in LilyPond 2.26.0 too on this book (see the remark) — pinned as agreement.
            Assert.True(Signature(FilledPageBook(entrySmall)) == Signature(FilledPageBook(entryLarge)),
                "'stretchability' now moves the justified page, where LilyPond 2.26.0's did not "
                + "(session 377, scratch/p378/paper/lp2) — re-measure the twin before believing it.");
            return;
        }
        AssertMoves(PaperBookWith(entrySmall), PaperBookWith(entryLarge),
            "systemSystemSpacing " + subKey);
    }

    public static TheoryData<string> PaperSubKeys()
    {
        var data = new TheoryData<string>();
        foreach (string k in LanguageVocabulary.PaperSpacingSubKeys) data.Add(k);
        return data;
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

    private static string DrumBook(string drum) =>
        "octave absolute\npart m { clef percussion\n"
        + $"  section A {{ {drum}4 {drum}4 {drum}4 {drum}4 | }}\n}}\n"
        + "form main { A }\nscore main { staff m }\n";

    /// <remarks>
    /// ⚠️ THE DRUM HAS TO BE ONE THE FIELD CAN CHANGE. <c>mark accent</c> was measured
    /// against <c>sn</c>, which carries NO mark, and "an unknown word clears the mark"
    /// clears nothing there — it read alive only because the <c>drummap</c> block shifted
    /// every data-pos (2026-09-13). It is the closed hi-hat that has a mark to lose.
    /// </remarks>
    [Theory]
    [InlineData("sn", "position 6")]
    [InlineData("sn", "notehead x")]
    [InlineData("sn", "midi 40")]
    [InlineData("sn", "mark open")]        // a mark word: the snare gains a ○
    [InlineData("hhc", "mark accent")]     // NOT a mark word: the closed hi-hat loses its +
    public void EveryDrummapFieldMovesTheDrumItNames(string drum, string field)
        => AssertMoves(DrumBook(drum), $"drummap {{\n  {drum}: {field}\n}}\n" + DrumBook(drum),
            $"drummap {drum} {field}");

    public static TheoryData<string> CanonicalDrums()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in DrumNameRegistry.CanonicalEntries) data.Add(name);
        return data;
    }

    public static TheoryData<string, string> DrumAliases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (alias, full) in DrumNameRegistry.AliasEntries) data.Add(alias, full);
        return data;
    }

    /// <summary>
    /// Every drum the table carries reaches the page or the .mid as something other than the
    /// bass drum — the sweep the TABLE guard below cannot do, because it reads the rows
    /// rather than asking what they draw.
    /// </summary>
    /// <remarks>
    /// ★ Added 2026-09-13, the leg after the table went from 30 names to LilyPond's 63. The
    /// new rows brought two things no row had before — a hi/lo pair placed by a table other
    /// than <c>drums-style</c>, and the <c>staccato</c>/<c>tenuto</c> marks the guiro needs —
    /// and a mark the renderer does not draw would have been invisible to every other test:
    /// <c>DrumNameRegistry</c> would carry the word, <c>DrumTableMatchesLilyPondTests</c>
    /// would agree it is LilyPond's, and the page would show nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CanonicalDrums))]
    public void EveryDrumInTheTableReachesThePage(string drum)
    {
        if (drum is "bassdrum") return;      // the baseline itself
        AssertMoves(DrumBook("bassdrum"), DrumBook(drum), "drum " + drum);
    }

    /// <summary>
    /// ★★ 63 POSITIVE CONTROLS. An abbreviation is the same instrument as its full name, so
    /// the two must draw and sound IDENTICALLY — an alias pointing at the wrong row is the
    /// one defect the sweep above cannot see (it would move the page, just to the wrong
    /// place), and it is exactly the shape of the <c>hhs</c> that had to be removed.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrumAliases))]
    public void EveryAbbreviationDrawsItsOwnInstrument(string alias, string full)
        => Assert.Equal(Signature(DrumBook(full)), Signature(DrumBook(alias)));

    /// <summary>
    /// Where the MARK is the only thing that tells two instruments apart, it must actually be
    /// drawn — the claim the sweep above cannot make, because it compares each drum to the
    /// bass drum and a mark nobody draws still leaves the two far apart.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ THIS IS THE GAP THE MARK WORDS LIVE IN. <c>guiro</c> and <c>longguiro</c> carry the
    /// same line, the same notehead AND the same GM key 74; LilyPond separates them by
    /// <c>tenuto</c> alone. If <c>DrumNameRegistry.MarkArticulation</c> stopped drawing that
    /// word, the registry would still carry it, <c>DrumTableMatchesLilyPondTests</c> would
    /// still agree it is LilyPond's, the collision guard below reads the TABLE and would see
    /// two different rows — and the page would show one instrument twice. Nothing else in the
    /// repository asks the PAGE this question.
    /// <para>
    /// Two entries sharing a row AND a mark are the doubles LilyPond itself spells twice;
    /// those are the collision guard's business, and are skipped here.
    /// </para>
    /// </remarks>
    [Fact]
    public void WhereTheMarkIsTheOnlyDifference_ItIsDrawn()
    {
        var byRow = DrumNameRegistry.CanonicalEntries.GroupBy(
            e => $"{e.Value.StaffPosition}/{e.Value.Notehead}/{e.Value.GmKey}", StringComparer.Ordinal);

        int pairs = 0;
        foreach (var row in byRow)
        {
            var members = row.ToArray();
            for (int i = 0; i < members.Length; i++)
                for (int j = i + 1; j < members.Length; j++)
                {
                    if (members[i].Value.Mark == members[j].Value.Mark)
                        continue;   // LilyPond's own double — the collision guard's business
                    pairs++;
                    Assert.True(
                        Signature(DrumBook(members[i].Key)) != Signature(DrumBook(members[j].Key)),
                        $"'{members[i].Key}' and '{members[j].Key}' share a row and differ only "
                        + $"by mark ('{members[i].Value.Mark}' vs '{members[j].Value.Mark}'), and "
                        + "the page cannot tell them apart — the mark is not drawn.");
                }
        }

        // A count, so that the day a mark word stops being anybody's only difference this
        // test says so instead of passing over an empty loop. The eleven: the hi and lo
        // bongo each contribute three (muted / plain / open on one row and one GM key), the
        // hi and lo conga one each (open vs plain), and one apiece for guiro/longguiro,
        // triangle/opentriangle and hihat/closedhihat.
        Assert.Equal(11, pairs);
    }

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

    // ===================== the chord-quality table =====================

    /// <summary>A chord row of one symbol, beside the staff that carries the beat.</summary>
    private static string ChordBook(string symbol, string style = "") =>
        style
        + "time 4/4\noctave absolute\npart m { clef treble }\n"
        + $"section A {{ m {{ c'4 d' e' f' | }}\n  chords prog {{ {symbol} | }} }}\n"
        + "form main { ~A }\nscore main { chords prog  staff m }\n";

    /// <summary>
    /// The two chord vocabularies, as the books that select them. BOTH have to be swept.
    /// </summary>
    /// <remarks>
    /// ⚠️⚠️ MEASURED THE HARD WAY (2026-09-13). This net first read the default style only,
    /// and a poison that gave <c>Major9</c> the minor ninth's spelling in
    /// <c>ChordQualityRegistry.Suffix</c> left all 1,047 chord and layout tests green — this
    /// guard included. The reason is that <c>Suffix</c> is the <c>words</c> table and the
    /// DEFAULT is <c>symbols</c>, which answers out of <c>SymbolSuffix</c>: the poison sat in
    /// a branch the book never reached. Two tables of 29 spellings each, and one of them was
    /// invisible to the page.
    /// </remarks>
    public static TheoryData<string, string> ChordStyles() => new()
    {
        { "symbols", "" },
        { "words", "layout { chordQualities words }\n" },
    };

    public static TheoryData<string> ChordQualityTokens()
    {
        var data = new TheoryData<string>();
        foreach (string token in ChordQualityRegistry.Tokens) data.Add(token);
        return data;
    }

    /// <summary>
    /// Every quality token the chord table accepts must reach the page as something other
    /// than the plain triad.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS VOCABULARY HAS NO POPUP. The chord completion offers four forms per root
    /// (triad, maj7, sus4, sus2), so the other thirty-odd spellings in
    /// <see cref="ChordQualityRegistry.Tokens"/> are reachable only by typing — and nothing
    /// asked the page about them until 2026-09-13. They are a name table with a display
    /// suffix, a tone set and a LilyPond modifier, which is the shape the drum table had when
    /// <c>hhs</c> was found in it: a spelling that draws another quality's symbol would look
    /// right in every table test.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ChordQualityTokens))]
    public void EveryChordQualityTokenReachesThePage(string token)
    {
        AssertMoves(ChordBook("C"), ChordBook("C" + token), "chord C" + token);
        // ...and under the OTHER vocabulary too, which is a second table of 29 spellings.
        AssertMoves(ChordBook("C", WordsStyle), ChordBook("C" + token, WordsStyle),
            "chord C" + token + " (words)");
    }

    private const string WordsStyle = "layout { chordQualities words }\n";

    /// <summary>
    /// ★★ THE POSITIVE CONTROLS. Several tokens are the same quality under two spellings
    /// (<c>min</c> for <c>m</c>, <c>+</c> for <c>aug</c>, <c>sus</c> for <c>sus4</c>), and
    /// those must draw IDENTICALLY — a second spelling that reaches a different quality is
    /// the one defect the sweep above cannot see, because it would move the page too.
    /// </summary>
    [Fact]
    public void EverySecondSpellingDrawsTheSameQuality()
    {
        var groups = ChordQualityRegistry.Tokens
            .GroupBy(t => ChordQualityRegistry.TryResolve(t, out var q) ? q : default)
            .Where(g => g.Count() > 1)
            .ToArray();

        Assert.NotEmpty(groups);
        foreach (var group in groups)
        {
            string[] spellings = [.. group.OrderBy(t => t, StringComparer.Ordinal)];
            string first = Signature(ChordBook("C" + spellings[0]));
            foreach (string other in spellings.Skip(1))
                Assert.True(first == Signature(ChordBook("C" + other)),
                    $"'C{spellings[0]}' and 'C{other}' are the same quality "
                    + $"({group.Key}) and must draw the same symbol.");
        }
    }

    /// <summary>The chord SYMBOL as it is drawn — the text, with nothing else about the
    /// page and nothing at all about the sound.</summary>
    /// <remarks>
    /// ⚠️⚠️ <see cref="Signature"/> IS THE WRONG INSTRUMENT FOR THIS ONE QUESTION, and the
    /// poison said so (2026-09-13): giving <c>Major9</c> the minor ninth's suffix left every
    /// test in the repository green — 1,047 chord and layout tests, this file's own collision
    /// guard included — because the signature carries the MIDI too, and the two chords still
    /// SOUND different. A wrong label over a right chord is exactly the defect worth catching
    /// here, so the picture has to be read on its own.
    /// </remarks>
    private static string[] ChordPictures(string symbol, string style)
    {
        var tree = SyntaxTree.Parse(ChordBook(symbol, style));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return [.. score.ChordNames.Select(c => c.ChordText)];
    }

    /// <summary>
    /// No two DIFFERENT qualities may be DRAWN the same, under EITHER vocabulary. Two that
    /// are cannot be told apart on the page whatever they play — and unlike the drum table's
    /// doubles, LilyPond licenses none of them here, so the set is empty.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChordStyles))]
    public void NoTwoChordQualitiesDrawAlike(string styleName, string style)
    {
        string FirstToken(ChordQuality q) => ChordQualityRegistry.Tokens.First(
            t => ChordQualityRegistry.TryResolve(t, out var r) && r == q);

        var drawnAlike = System.Enum.GetValues<ChordQuality>()
            .Where(q => ChordQualityRegistry.Tokens.Any(
                t => ChordQualityRegistry.TryResolve(t, out var r) && r == q))
            .GroupBy(q => string.Join("|", ChordPictures("C" + FirstToken(q), style)),
                     StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("=", g.Select(q => q.ToString())))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(drawnAlike.Length == 0,
            $"under '{styleName}' these qualities draw the same symbol: "
            + string.Join(", ", drawnAlike));
    }

    // ===================== the tuning table =====================

    /// <summary>A one-part book shown as TAB, so the fret numbers are on the page.</summary>
    /// <remarks>
    /// ⚠️ The pitches span two octaves and START LOW on purpose: a tuning only shows itself in
    /// the FRET numbers, and two tunings that differ on one string alone are told apart only
    /// by a note that lands on that string. The lowest note here is <c>e,</c> — 40, the
    /// guitar's open sixth string and the only string <c>guitardropd</c> retunes.
    /// ⚠️ MEASURED, not reasoned (2026-09-13): the first shape of this book started at
    /// <c>e</c> and its frets came out <c>2 0 1 0 3 8 3 5</c> for BOTH tunings — nothing
    /// reached string six, so drop-D read as a dead word. The notes are where they are
    /// because <c>lysc svg</c> was asked what they fret to.
    /// </remarks>
    private static string TabBook(string tuning) =>
        "octave absolute\npart m { clef treble_8 tuning " + tuning + "\n"
        + "  section A { e,4 a, d g | c' e' g' c'' | }\n}\n"
        + "form main { A }\nscore main { tab m }\n";

    public static TheoryData<string> TuningWords()
    {
        var data = new TheoryData<string>();
        foreach (string word in LanguageVocabulary.TuningNames) data.Add(word);
        return data;
    }

    /// <summary>
    /// Every tuning the language accepts must fret DIFFERENTLY from the plain guitar — the
    /// only check that reads the strings rather than repeating them.
    /// </summary>
    /// <remarks>
    /// ★ Session 374's 18th leg put LilyPond's whole table in (7 words → 32), and its own
    /// theory could not have caught a mis-transcribed array: the pinned numbers and the
    /// shipped numbers came from one generator. This asks the page instead. The guitar's own
    /// three spellings are the control — they MUST be inert, and are asserted equal below
    /// rather than skipped, exactly as <c>removeEmpty false</c> is.
    /// </remarks>
    /// <summary>The two words that ARE the guitar, written out rather than derived.</summary>
    /// <remarks>
    /// ⚠️⚠️ THE CONTROL MUST NOT BE CHOSEN BY THE THING UNDER TEST. This branch first read
    /// <c>Tunings.Parse(word) == TuningType.Guitar</c> — so poisoning <c>Parse</c> to answer
    /// "guitar" for <c>guitardropd</c> moved that word into the INERT branch and the theory
    /// stayed green (measured 2026-09-13, the poison this tripwire failed the first time).
    /// A tripwire whose two sides are picked by the code it watches cannot fire.
    /// </remarks>
    private static readonly string[] SpellingsOfTheGuitar = ["guitar", "standard"];

    [Theory]
    [MemberData(nameof(TuningWords))]
    public void EveryTuningFretsDifferentlyFromTheGuitar(string word)
    {
        if (SpellingsOfTheGuitar.Contains(word))
        {
            Assert.Equal(Signature(TabBook("guitar")), Signature(TabBook(word)));
            return;
        }
        AssertMoves(TabBook("guitar"), TabBook(word), "tuning " + word);
    }

    /// <summary>
    /// Two tuning WORDS that fret alike are one tuning under two names — which LilyPond does
    /// have, and which is therefore listed rather than forbidden, the way the drum table's
    /// doubles are.
    /// </summary>
    /// <remarks>
    /// ⚠️ A NEW LINE HERE IS A CLAIM ABOUT LILYPOND — check ly/string-tunings-init.ly before
    /// adding one. The five below are the whole set and every one is in that file (or older
    /// than it here): <c>guitar</c>/<c>standard</c> and <c>ukulele</c>/<c>uke</c> are Lily#'s
    /// own second spellings; <c>bass</c>/<c>bass4</c>/<c>doublebass</c> and
    /// <c>violin</c>/<c>mandolin</c> are LilyPond's, which defines each pair with identical
    /// chords a few lines apart.
    /// </remarks>
    [Fact]
    public void NoTwoTuningWordsFretAlike_ExceptWhereLilyPondSpellsOneTuningTwice()
    {
        var doubles = LanguageVocabulary.TuningNames
            .GroupBy(Tunings.Parse)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("=", g.OrderBy(w => w, StringComparer.Ordinal)))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "bass=bass4=doublebass", "guitar=standard", "mandolin=violin", "uke=ukulele" },
            doubles);

        // And the tunings themselves — one member per distinct set of strings, so two members
        // carrying the same strings would be a name that cannot be told from another.
        var sameStrings = Enum.GetValues<TuningType>()
            .GroupBy(t => string.Join(",", Tunings.GetTuning(t)), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("=", g.Select(t => t.ToString())))
            .ToArray();
        Assert.Empty(sameStrings);
    }
}
