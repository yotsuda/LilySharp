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

using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A token the parser consumes without storing is a token the TREE NO LONGER
/// CONTAINS — and a node's position is the running sum of the green widths
/// before it, so a dropped token slides every position after it. The '.' of an
/// annotation's '.up' / '.down' qualifier was dropped exactly that way:
/// <c>@staccato.up</c> came back out of the tree as <c>@staccatoup</c> with no
/// diagnostic at all, and every note after it reported a source position one
/// character early. That reaches the SVG's data-pos, the LSP's jump targets and
/// <c>PartSectionLayoutConverter</c>, which WRITES .lys back out of the tree.
/// </summary>
[Trait("Category", "Unit")]
public class AnnotationRoundTripTests
{
    [Theory]
    [InlineData("c4@staccato.up d4 e4 f4 |")]     // articulation, above
    [InlineData("c4@staccato.down d4 e4 f4 |")]   // articulation, below
    [InlineData("c4@f.up d4 e4 f4 |")]            // dynamic
    [InlineData("c4@feather.up d4 e4 f4 |")]      // unknown name — still spelled back
    [InlineData("c4@staccato.up.down d4 e4 f4 |")] // rejected second side, kept anyway
    [InlineData("c4@cresc.up d4 e4 f4 |")]        // rejected on a hairpin, kept anyway
    public void ADottedPlacementQualifier_SurvivesTheTree(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    /// <summary>
    /// The defect itself, stated as the quantity it corrupts: every node must
    /// stand where it says it stands — its text is the source slice at its own
    /// position. A dropped token slides everything after it, so this fails one
    /// character at a time and never at the node that lost the token. The
    /// control (same bar, no qualifier) says the mapping is right to begin with;
    /// without it, a test that pins positions passes just as well when NOTHING
    /// maps correctly.
    /// </summary>
    [Theory]
    [InlineData("melody { c4@staccato.up d4 e4 f4 | }")]
    [InlineData("melody { c4@staccato d4 e4 f4 | }")]     // control
    public void EveryNode_StandsWhereItSaysItStands(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        Assert.Equal(source.Length, root.FullWidth);

        foreach (var node in root.DescendantNodes())
        {
            var text = node.ToFullString();
            Assert.True(
                node.Position + text.Length <= source.Length
                && source.AsSpan(node.Position, text.Length).SequenceEqual(text),
                $"{node.Kind} at {node.Position} spells [{text}] but the source there is "
                + $"[{source.Substring(node.Position, Math.Min(text.Length, source.Length - node.Position))}]");
        }
    }

    /// <summary>
    /// A lyric hyphen is a WIDTH, not just a mark. ParseLyricSyllable glues a
    /// trailing '-' onto the word so "Hap-" stays one syllable carrying its own
    /// connector — but it used to glue a DETACHED one too, and the glued token
    /// keeps only the word's leading trivia and the hyphen's trailing trivia, so
    /// the space in <c>la -- la</c> belonged to neither and left the tree. Two
    /// characters short is not a cosmetic loss: a green width is what every later
    /// position is summed from, so the whole file after the lyrics block slid.
    /// Nothing downstream could see it — LyricSyllableReader.Classify folds
    /// <c>la-</c>+<c>-</c> and <c>la</c>+<c>--</c> onto the same Hyphen connector
    /// on the same syllable, so the engraving was right the whole time.
    /// </summary>
    [Theory]
    [InlineData("lyrics L { la -- la }")]    // the shape three corpus books write
    [InlineData("lyrics L { la -- }")]       // hyphen last in the block
    [InlineData("lyrics L { la - la }")]     // detached SINGLE hyphen
    [InlineData("lyrics L { la - - la }")]   // detached PAIR — '--' must not fuse
    [InlineData("lyrics L { la--la }")]      // glued: already right, must stay right
    [InlineData("lyrics L { la- la }")]      // word continuation: ditto
    [InlineData("lyrics L { Hap- py }")]     // ditto — the reason the glue exists
    [InlineData("lyrics L { la __ la }")]    // control: extender, never broken
    [InlineData("lyrics L { la _ la }")]     // control: skip, never broken
    [InlineData("lyrics L { va~ga la }")]    // control: elision, never broken
    public void ALyricHyphen_KeepsTheSpaceBesideIt(string source)
        => Assert.Equal(source, SyntaxTree.Parse(source).GetRoot().ToFullString());

    /// <summary>
    /// The price of that dropped space, stated as the quantity it corrupts: the
    /// note written after a lyrics block must stand where it says it stands. The
    /// control writes the same music with the hyphens glued — a spelling that
    /// never lost a character — because without it a test that pins a position
    /// passes just as well when NOTHING maps correctly.
    /// </summary>
    [Theory]
    [InlineData("part v\nsection A { lyrics L { la -- la -- } v { c4 d e f | } }\n")]
    [InlineData("part v\nsection A { lyrics L { la--la-- } v { c4 d e f | } }\n")] // control
    public void TheNoteAfterALyricsBlock_StandsWhereItSaysItStands(string source)
    {
        var root = SyntaxTree.Parse(source).GetRoot();
        Assert.Equal(source.Length, root.FullWidth);

        var note = root.DescendantNodes<NoteSyntax>().First();
        Assert.Equal(source.IndexOf("c4", StringComparison.Ordinal), note.Position);
    }

    /// <summary>
    /// The hole itself, measured: every book in the corpus and the fixtures must
    /// spell itself back out of its own tree. The named books below are the ones
    /// that do NOT, each for a reason that lives in a different island; they are
    /// listed so the number cannot grow silently, and so the remaining islands
    /// have an address instead of a memory.
    /// </summary>
    [Fact]
    public void EveryBook_SpellsItselfBackOutOfItsTree()
    {
        // Known-broken. ALL of these are ONE island: a post-event written AFTER a
        // slur/tie/beam mark ('g4(@cresc', 'g1)~@startTrillSpan', "f,)\3") is hoisted
        // onto the note, and ParsePostEvents replays the mark behind it — so the tree
        // spells the mark and the post-event in the opposite order. No width is lost
        // (628→628 on all four of the books that used to be listed here), but the two
        // reordered nodes DO stand in the wrong place: measured 2026-08-16, a hairpin
        // written 'g4(@cresc' reports data-pos 36 for a '@' standing at 37, and the two
        // spellings 'g4(@cresc'/'g4@cresc(' engrave to a BYTE-IDENTICAL SVG. So the
        // engraving is right and only the source map is wrong.
        // ⚠️ volta-labels came OFF this list on 2026-08-16: "the '|' before a '[1. …]'
        // label is not stored" was the last island where a token no form rule claimed was
        // consumed by a bare Advance(). ParseFormItem now parses every barline as the
        // BarlineSyntax it is, and the three containers around it report and keep
        // what they cannot place (LYS0030).
        // ⚠️ Closing that island is NOT a one-liner, and it needs a decision before
        // any code: a census of every consumer (2026-08-16) found 17 production
        // sites, of which 12 are live and EVERY live one reads the marker as a
        // SEQUENCE item, with the order load-bearing (MusicWalk.PeekMarkers scans
        // the run AFTER the note; TieTargetScanner binds the immediately-following
        // item; SlurPairingScanner is a stack in item order). Six further sites that
        // read markers out of note.Articulations are DEAD — 0 books of 1021 put one
        // there — and would wake up. editors/vscode/src/smartTyping.ts encodes the
        // same ordering contract a second time. §2F ⑺ carries the address.
        // ⚠️ The seven audit\lpreg books below joined this list on 2026-08-16 with
        // the widened reach. NOTHING regressed: they were already broken, by the one
        // island above, and no test had ever read that directory.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empty-chord.lys", "script-stack-order1.lys", "slur-vertical-skylines.lys",
            "feature-tour.lys",
            "obs-probe.lys", "perf-slurinside1k.lys", "perf-slurscript1k.lys",
            "scriptstack1.lys", "slurscript-obs.lys", "slurvsky.lys", "tupnumss.lys",
        };

        var broken = new List<string>();
        foreach (var file in CorpusBooks())
        {
            var source = File.ReadAllText(file);
            if (SyntaxTree.Parse(source).GetRoot().ToFullString() != source)
                broken.Add(Path.GetFileName(file));
        }

        var news = broken.Where(b => !known.Contains(b)).ToList();
        Assert.True(news.Count == 0, "these books stopped spelling themselves back: " + string.Join(", ", news));
        var fixedUp = known.Where(k => !broken.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(fixedUp.Count == 0,
            "these books round-trip now — take them off the known-broken list: " + string.Join(", ", fixedUp));
    }

    /// <summary>
    /// The OTHER half of the same claim, and the two are one pair:
    /// <see cref="EveryBook_SpellsItselfBackOutOfItsTree"/> compares a SUM — the whole
    /// tree against the whole file — so it sees a token that left the tree and is blind
    /// to two nodes that swapped places, since a swap keeps the sum. This one asks each
    /// node separately whether the source AT ITS OWN POSITION spells what it spells, so
    /// it sees the swap and is blind to nothing the other one catches.
    /// </summary>
    /// <remarks>
    /// ⚠️ WITHOUT BOTH, EITHER ONE READS AS "THE POSITIONS ARE FINE". Measured
    /// 2026-08-16 (HANDOFF §2F ⑺): the island below keeps every width — the four books
    /// then listed were 628 characters in and 628 out — while the two reordered nodes
    /// stand in the wrong place, so <c>g4(@cresc</c> reports data-pos 36 for a <c>@</c>
    /// that stands at 37. That is the source map the SVG's <c>data-pos</c>, the LSP's
    /// jump targets and <c>PartSectionLayoutConverter</c> all read.
    /// <para>
    /// ⚠️ THE TWO LISTS ARE THE SAME ELEVEN BOOKS TODAY, AND THAT IS MEASURED, NOT
    /// ASSUMED (2026-08-30 — the first draft of this remark claimed they differ and the
    /// first run refuted it). For §2F ⑺'s island they must coincide: the swap exchanges
    /// a post-event block <c>A</c> (which starts with <c>@</c> or <c>\</c>) with a marker
    /// <c>M</c> (<c>~ ( ) [ ]</c>), and <c>A+M == M+A</c> would need the two to be powers
    /// of one string, which those first characters forbid. So on THIS island the sum
    /// sweep is not weaker — it is the same claim read less precisely.
    /// </para>
    /// <para>
    /// ⚠️ WHERE THE PAIR EARNS ITS KEEP IS THE SECOND ASSERT, on <see cref="SyntaxNode.Span"/>.
    /// A sum is blind to a defect that keeps every width and still moves a node, and this
    /// repository has shipped exactly one: until 2026-08-29 <c>GreenNode.LeadingTrivia</c>
    /// was <c>virtual =&gt; null</c> and only tokens overrode it, so a synthesized node
    /// opening a line claimed the newline and indent in front of it as its own
    /// (HANDOFF §2 U3, a reader's report on <c>Walk.lys</c>). Every width was intact, both
    /// sum sweeps were green, and 93 snapshots' <c>data-pos</c> were wrong. The Span
    /// assert below is the observer that was missing then, and the poison says so: putting
    /// U3's answer back (<c>SyntaxNode.Span</c> reading <c>Green.LeadingTriviaWidth</c>
    /// instead of <c>Green.GetLeadingTriviaWidth()</c>) turns 569 books red on the Span
    /// half while <see cref="EveryBook_SpellsItselfBackOutOfItsTree"/> stays green — the
    /// two halves are not one claim spelled twice. Measured 2026-08-30.
    /// </para>
    /// <para>
    /// ⚠️ The Span half has to be stated INDEPENDENTLY of the widths it checks. Slicing the
    /// node's own text back out of the source would not do: that text is derived from the
    /// same <c>GetLeadingTriviaWidth</c>, so under U3's answer the derived text moved with
    /// the wrong span and the two agreed. What does not move with it is what trivia IS —
    /// a trivia-free span neither opens nor closes on whitespace or a comment.
    /// </para>
    /// <para>
    /// ⚠️ The FullSpan list goes green by itself the day HANDOFF §2F ⑺ is decided and
    /// closed — the island is one parser behaviour (<c>ParsePostEvents</c> hoists a
    /// post-event written after a slur/tie/beam marker onto the note and replays the
    /// marker behind it), not N book defects. Until then the named list is what the
    /// decision costs, counted.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryNodeInEveryBook_StandsWhereItSaysItStands()
    {
        // Known-broken: the ONE island of HANDOFF §2F ⑺ — see the remarks above and the
        // longer note on EveryBook_SpellsItselfBackOutOfItsTree. Every name here writes a
        // post-event (an '@' annotation or a '\N' string number) AFTER a slur/tie/beam
        // marker, which is the only shape that reorders. Nothing else in the corpus does.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "empty-chord.lys", "script-stack-order1.lys", "slur-vertical-skylines.lys",
            "feature-tour.lys",
            "obs-probe.lys", "perf-slurinside1k.lys", "perf-slurscript1k.lys",
            "scriptstack1.lys", "slurscript-obs.lys", "slurvsky.lys", "tupnumss.lys",
        };

        var fullSpanOffenders = new List<string>();
        var spanOffenders = new List<string>();
        foreach (var file in CorpusBooks())
        {
            var source = File.ReadAllText(file);
            var root = SyntaxTree.Parse(source).GetRoot();
            string? spanFailure = null;
            bool fullSpanFailed = false;
            foreach (var node in root.DescendantNodes())
            {
                if (!fullSpanFailed && !StandsAt(source, node.Position, node.ToFullString()))
                    fullSpanFailed = true;
                // The trivia-free half, stated INDEPENDENTLY of the widths it is checking:
                // a Span is trivia-free exactly when it neither opens nor closes on trivia.
                // Slicing the node's own text back out would not do — that text is derived
                // from the same GetLeadingTriviaWidth, so it agreed with U3's answer as
                // readily as the wrong Span did.
                if (spanFailure is null && node.Span.Length > 0 && OpensOrClosesOnTrivia(source, node.Span))
                    spanFailure = $"{Path.GetFileName(file)}: {node.Kind} claims [{node.Span.Start}"
                        + $"..{node.Span.End}), which begins or ends on trivia";
                if (fullSpanFailed && spanFailure is not null)
                    break;
            }

            if (fullSpanFailed) fullSpanOffenders.Add(Path.GetFileName(file));
            if (spanFailure is not null) spanOffenders.Add(spanFailure);
        }

        // ⚠️ BOTH LISTS ARE THE SAME ELEVEN BOOKS, MEASURED (2026-08-30). The island
        // reaches the Span half too, and the draft of this test that asserted zero here
        // was refuted by its first run: a replayed marker lands after the note's trailing
        // space, so a Slur that spells "(" claims a span holding " ". The Span message is
        // the sharper one — it names the NODE and its wrong address, which is what a
        // reader clicking the preview actually loses — so it is asserted first.
        var spanNews = spanOffenders
            .Where(s => !known.Contains(s[..s.IndexOf(':', StringComparison.Ordinal)])).ToList();
        Assert.True(spanNews.Count == 0,
            $"{spanNews.Count} nodes do not span their own text: "
            + string.Join(" | ", spanNews.Take(10)));

        var news = fullSpanOffenders.Where(b => !known.Contains(b)).ToList();
        Assert.True(news.Count == 0,
            $"{news.Count} books hold a node that does not stand where it says it stands: "
            + string.Join(", ", news));
        var fixedUp = known.Where(k => !fullSpanOffenders.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(fixedUp.Count == 0,
            "every node stands right in these books now — take them off the list: "
            + string.Join(", ", fixedUp));
    }

    /// <summary>The source at <paramref name="at"/> spells exactly <paramref name="text"/>.</summary>
    private static bool StandsAt(string source, int at, string text)
        => at >= 0 && at + text.Length <= source.Length
           && source.AsSpan(at, text.Length).SequenceEqual(text);

    /// <summary>
    /// The span's first or last character is trivia — whitespace, or the opening of a
    /// comment. Either one means the span is not the node's own text.
    /// </summary>
    private static bool OpensOrClosesOnTrivia(string source, TextSpan span)
        => span.Start < 0 || span.End > source.Length
           || char.IsWhiteSpace(source[span.Start])
           || char.IsWhiteSpace(source[span.End - 1])
           || (span.Start + 1 < source.Length && source[span.Start] == '/' && source[span.Start + 1] == '/');

    /// <summary>
    /// The corpus both sweeps read, in ONE place so their reach cannot drift apart.
    /// </summary>
    /// <remarks>
    /// ⚠️ A CHECKER'S REACH IS PART OF ITS CLAIM. The sum sweep called itself "every
    /// book" and read 300 of the repository's 566 tracked <c>.lys</c> until 2026-08-16:
    /// <c>audit\lpreg</c> (257) and <c>samples</c> (6) were outside it, and that is
    /// exactly where the lyric-hyphen island had been sitting — a dropped width, the
    /// family this file exists to catch, invisible because nothing looked.
    /// <para>
    /// ⚠️ The check is one subtraction — this list against <c>git ls-files '*.lys'</c> —
    /// and the first widening still left three behind
    /// (<c>audit\lilypond-ref\cases\*\case.lys</c>). Redo the subtraction when a corpus
    /// directory is added; "wider than it was" is not the same as "all of them".
    /// Measured 2026-08-30: this reach is 580 of 580 tracked, so the floor below is the
    /// number that must be swept, not a number that used to be enough.
    /// </para>
    /// </remarks>
    private static List<string> CorpusBooks()
    {
        var root = CollectResumeTests.FindRepoRoot();
        var books = new List<string>();
        foreach (var dir in new[]
        {
            Path.Combine(root, "audit", "lp-regression", "lys"),
            Path.Combine(root, "audit", "lpreg"),
            Path.Combine(root, "audit", "lilypond-ref"),
            Path.Combine(root, "LilySharp.Tests", "Fixtures"),
            Path.Combine(root, "samples"),
        })
        {
            if (!Directory.Exists(dir))
                continue;
            books.AddRange(Directory.EnumerateFiles(dir, "*.lys", SearchOption.AllDirectories));
        }

        // The floor is the tracked count as of 2026-08-30 (580). Raise it with the
        // corpus; a sweep that silently shrinks reads as "still passing".
        Assert.True(books.Count >= 580, $"only {books.Count} books found — the corpus paths moved");
        return books;
    }
}
