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

using System.Linq;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// F3 engine slice S4b: the incremental compiler must produce SVG byte-identical
/// to a full recompile on every edit (the S1 incremental==full invariant, now
/// with a real cutoff), AND it must actually skip the line-break DP when — and
/// only when — the edit leaves the line-break gate unchanged.
/// </summary>
[Trait("Category", "Visual")]
public class IncrementalCompilerTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private const string Base = """
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel { c4 d e f | g4 a b c | d4 e f g | a4 b c d | }
        section Main { melody { mel } }
        form main { Main }
        score main "x" { staff melody }
        """;

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    private static string Norm(string svg) => svg.Replace("\r\n", "\n");

    private static TextChange Replace(string text, string find, string replacement)
    {
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return new TextChange(new TextSpan(at, find.Length), replacement);
    }

    [Fact]
    public void FirstRender_EqualsFullGenerate()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Base), Opt);
        Assert.Equal(Full(Base), Norm(session.Render()));
    }

    /// <summary>
    /// The two fixtures <c>LilySharp.Benchmarks.IncrementalSessionBenchmark</c> measures the
    /// warm-session edit on. This test asserts the PREMISE that benchmark depends on — that a
    /// width-preserving edit takes the whole-layout reuse path — so the premise is checked by
    /// the suite instead of only by a benchmark nobody runs.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS EXISTS BECAUSE THE BENCHMARK WAS THROWING AND NOBODY SAW IT. Its
    /// <c>VerifyReuses</c> had been failing on the multi-staff fixture since before
    /// 2026-08-04 — a pedal bracket disqualified reuse wholesale — and the only way to find
    /// out was to run <c>dotnet run -c Release -- --filter '*IncrementalSession*'</c> by
    /// hand. A failing benchmark is not a failing test. Whole-layout reuse is the preview's
    /// per-keystroke payoff (measured on the multi-staff fixture at 5.5 ms against 9.0 ms,
    /// and a third of the allocation), so it is worth a test that costs milliseconds.
    /// <para>
    /// ⚠️ THE FIXTURES ARE NAMED HERE AND IN THE BENCHMARK, and they must stay the same two.
    /// If one is edited into ineligibility — a grob override, or any future disqualifier —
    /// this test fails and says so, where the benchmark would simply stop reporting.
    /// </para>
    /// <para>
    /// ⚠️ BOTH ASSERTIONS, for the reason the pedal fix showed: making reuse FIRE is easy and
    /// wrong on its own. The byte-identity check is what says the reused layout re-derives
    /// everything an edit invalidated.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("showcase/03-piano")]              // multi-staff (grand staff), has pedals
    [InlineData("showcase/grammar-2026-06-09")]    // the largest single-staff fixture
    public void BenchmarkFixtures_WidthPreservingEdit_ReuseWholeLayout(string fixture)
    {
        string path = System.IO.Path.Combine(
            FindFixturesDir(), fixture.Replace('/', System.IO.Path.DirectorySeparatorChar) + ".lys");
        string src = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");

        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // A leading newline: pure trivia, so no measure's content and no line-break gate
        // changes, but every source offset moves.
        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout,
            $"{fixture}: expected whole-layout reuse to fire, but it did not — "
            + "IncrementalSessionBenchmark measures the wrong thing when this is false.");
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    private static string FindFixturesDir()
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (System.IO.Directory.Exists(candidate))
                return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.DirectoryNotFoundException(
            "Cannot find LilySharp.Tests/Fixtures/ directory");
    }

    private static string ApplyFirst(string text, string find, string rep)
    {
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return text[..at] + rep + text[(at + find.Length)..];
    }

    /// <summary>
    /// A pedal bracket SPANS measures its own marks are not in: SolveAndSeed puts its ink
    /// into every system the span crosses, but the content key buckets a pedal MARK into
    /// its own measure only — so deleting the RELEASE (whose measure sits on system 2)
    /// must still re-derive system 1, where the bracket's ink was seeded under the
    /// lyrics. Written the day the seed landed, as the falsifier for exactly that
    /// staleness (HANDOFF 2F(g) leftover ⑶).
    /// </summary>
    [Fact]
    public void DeletingAPedalRelease_RedrawsTheSystemsTheBracketSpanned()
    {
        string bars =
            "c4 d e f | g4 a@sustainOn b c' | d4 e f g | a4 b c' d' | "
            + "c'4 b a g | f4 e d c | c4 d e f | g4 a b c' | "
            + "d4 e f g | a4 b c' d' | c'4 b@sustainOff a g | f4 e d c |";
        string sylls = string.Concat(System.Linq.Enumerable.Repeat("la la la la | ", 12)).TrimEnd();
        string text = $$"""
            time 4/4
            part melody { clef treble }
            section Main {
              melody { {{bars}} }
              lyrics w sings melody { {{sylls}} }
            }
            form main { Main }
            score main "x" { staff melody  lyrics w }
            """.Replace("\r\n", "\n");

        var tree = SyntaxTree.Parse(text);
        var session = new IncrementalCompiler(tree, Opt);
        Assert.Equal(Full(text), Norm(session.RenderIncremental(tree)));

        var change = Replace(text, "@sustainOff", "");
        tree = tree.WithChange(change);
        text = ApplyFirst(text, "@sustainOff", "");
        Assert.Equal(Full(text), Norm(session.RenderIncremental(tree)));
    }

    [Fact]
    public void RenderIncremental_OnExternallyEditedTrees_MatchesFullEachTime()
    {
        // Mirrors the LSP preview: the CALLER owns the tree (its own incremental reparse)
        // and hands each new tree to the session via RenderIncremental. Every render must
        // equal a full compile of that tree's text, across a sequence of edits — including
        // the first (cold) render and later (warm, system-reusing) ones.
        var text = Base;
        var tree = SyntaxTree.Parse(text);
        var session = new IncrementalCompiler(tree, Opt);

        Assert.Equal(Full(text), Norm(session.RenderIncremental(tree)));

        var edits = new[]
        {
            ("c4 d e f", "c4 d e g"),   // one note in measure 1
            ("d4 e f g", "d4 e f a"),   // one note in measure 3 (a different system's bar)
            ("c4 d e g", "c4 d e f"),   // revert measure 1
        };
        foreach (var (find, rep) in edits)
        {
            var change = Replace(text, find, rep);
            tree = tree.WithChange(change);
            text = ApplyFirst(text, find, rep);
            Assert.Equal(Full(text), Norm(session.RenderIncremental(tree)));
        }
    }

    /// <summary>
    /// Commenting a staff OUT of the score and back IN returns the same picture — including
    /// the grand-staff brace, whose glyph is chosen from the span it has to enclose.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE EDIT SHAPE IS THE POINT, and the suite had none like it: every other edit here
    /// changes a note INSIDE a measure, so the score's staff SET is constant and nothing that
    /// depends on it — the group's height, the brace rung, the system-start bar, the indent —
    /// is ever asked to change and change back. Reported 2026-08-04 as a preview defect ("the
    /// staff comes back, the brace does not grow with it"), and NOT reproduced here: both
    /// steps are byte-identical to a full compile, through the same API and the same
    /// <c>SvgRenderOptions.Preview()</c> the language server uses. Kept because a missing
    /// dependency edge on the staff set is exactly what this would catch, and because a
    /// defect that could not be reproduced deserves an observer more than one that could.
    /// </remarks>
    [Fact]
    public void CommentingAStaffOutAndBackIn_MatchesFullBothWays()
    {
        const string src = """
            time 4/4
            key c major
            part sop { clef treble }
            part bas { clef bass }
            section A {
              sop { c'4 d' e' f' | g'4 a' b' c'' | c'4 d' e' f' | g'4 a' b' c'' | }
              bas { c4 d e f | g4 a b c' | c4 d e f | g4 a b c' | }
            }
            form main { A }
            score main "x" {
              grandStaff {
                staff sop "Soprano"
                staff bas "Bass"
              }
            }
            """;
        const string line = "    staff bas \"Bass\"";
        const string commented = "    // staff bas \"Bass\"";

        var previewOpt = SvgRenderOptions.Preview();
        string FullPreview(string t) =>
            Norm(SvgGenerator.Generate(SyntaxTree.Parse(t), previewOpt));

        var text = src;
        var tree = SyntaxTree.Parse(text);
        var session = new IncrementalCompiler(tree, previewOpt);
        Assert.Equal(FullPreview(text), Norm(session.RenderIncremental(tree)));

        foreach (var (find, rep) in new[] { (line, commented), (commented, line) })
        {
            var change = Replace(text, find, rep);
            tree = tree.WithChange(change);
            text = ApplyFirst(text, find, rep);
            Assert.Equal(FullPreview(text), Norm(session.RenderIncremental(tree)));
        }

        // ...and the round trip really did return to the start, or the two assertions above
        // could both hold while the score drifted.
        Assert.Equal(src, text);
    }

    /// <summary>
    /// Commenting a staff out of a <c>grandStaff { }</c> and back in, ONE KEYSTROKE AT A
    /// TIME, returns the original picture — brace included.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE HALF-TYPED STATE IS THE DEFECT, which is why the atomic version of this edit
    /// (<see cref="CommentingAStaffOutAndBackIn_MatchesFullBothWays"/>) passes and this one
    /// did not. The editor inserts the two slashes one at a time, so the session sees a tree
    /// with a single `/`, and THAT tree closes the group early: the staff moves OUT of the
    /// brace while the score still has four staves, every staff's identity is unchanged and
    /// every measure's content is unchanged. The content key could not see it, whole-layout
    /// reuse fired, and the previous picture came back — then again on the way out, so the
    /// restored score kept the three-staff brace.
    /// <para>
    /// MEASURED on the reported file before the fix: the brace stayed at y=24.69 where a full
    /// compile puts it at y=30.98, and the SVG came back 13000 bytes against 13171.
    /// </para>
    /// <para>
    /// ⚠️ ASSERTED AT EVERY STEP, not just the last. Step 1 was already wrong (it returned
    /// step 0's picture), and a test that only compared the endpoints would have called the
    /// round trip clean on the way out while the damage was done on the way in.
    /// </para>
    /// </remarks>
    [Fact]
    public void HalfTypedComment_OnAGroupedStaff_MatchesFullAtEveryKeystroke()
    {
        const string src = """
            time 4/4
            key c major
            part sop { clef treble }
            part alt { clef treble }
            part ten { clef treble }
            part bas { clef bass }
            section A {
              sop { c'4 d' e' f' | g'4 a' b' c'' | }
              alt { c'4 d' e' f' | g'4 a' b' c'' | }
              ten { c'4 d' e' f' | g'4 a' b' c'' | }
              bas { c4 d e f | g4 a b c' | }
              lyrics verse { la le li lo | la le li lo | }
            }
            form main { A }
            score main "x" {
              grandStaff {
                staff sop "Soprano"  lyrics verse
                staff alt "Alto"  lyrics verse
                staff ten "Tenor"  lyrics verse
                staff bas "Bass"  lyrics verse
              }
            }
            """;
        const string line = "    staff bas \"Bass\"  lyrics verse";
        int at = src.IndexOf(line, System.StringComparison.Ordinal);
        Assert.True(at >= 0, "anchor line not found");

        var previewOpt = SvgRenderOptions.Preview();
        string FullPreview(string t) =>
            Norm(SvgGenerator.Generate(SyntaxTree.Parse(t), previewOpt));
        string With(string prefix) => src[..at] + prefix + src[at..];

        // "" -> "/" -> "//" -> "/" -> "": the two valid states with a BROKEN one either side.
        var prefixes = new[] { "", "/", "//", "/", "" };
        var tree = SyntaxTree.Parse(With(prefixes[0]));
        var session = new IncrementalCompiler(tree, previewOpt);

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (i > 0)
                tree = tree.WithChange(prefixes[i].Length > prefixes[i - 1].Length
                    ? new TextChange(new TextSpan(at, 0), "/")
                    : new TextChange(new TextSpan(at, 1), ""));
            Assert.Equal(With(prefixes[i]), tree.Text);   // precondition, both sides same text
            Assert.Equal(FullPreview(With(prefixes[i])), Norm(session.RenderIncremental(tree)));
        }
    }

    [Fact]
    public void WidthPreservingEdit_SkipsLineBreak_AndMatchesFull()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render(); // warm the cache

        // Adding an articulation collects into a separate list, not the measure
        // items, so the gate is unchanged -> the break DP is skipped.
        var change = Replace(Base, "c4 d e f", "c4@staccato d e f");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditSkippedLineBreak);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void WidthChangingEdit_RecomputesBreaks_AndMatchesFull()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // Re-rhythming a bar changes its natural width -> the gate changes -> the
        // breaks are recomputed. Output still matches a full recompile.
        var change = Replace(Base, "c4 d e f", "c2 d4 e4");
        var incremental = Norm(session.Edit(change));

        Assert.False(session.LastEditSkippedLineBreak);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_ReusesWholeLayout_AndMatchesFull()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render(); // warm the cache

        // A leading newline shifts EVERY source offset but changes no measure content,
        // no line-break gate, and no score-global input -> the whole ScoreLayout is
        // reused and LayoutEngine.Layout is skipped. The renderer re-derives each
        // annotation's data-pos (the section label, header grobs, …) from the edited
        // score, so the result is byte-identical to a full recompile of the shifted text.
        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.True(session.LastEditSkippedLineBreak); // reuse implies the gate was skipped
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_OnAScoreWithPedalBrackets_ReusesWholeLayout_AndMatchesFull()
    {
        // A pedal bracket used to DISQUALIFY whole-layout reuse outright: ReuseSafe declined
        // any layout carrying one, under a comment asserting the array was "always empty
        // today (pedals render as text marks, never a bracket layout)". Staff.PedalStyle
        // defaults to Bracket, so that was false for every `@sustainOn` in the corpus — including
        // showcase/03-piano, the multi-staff fixture LilySharp.Benchmarks' warm-session
        // benchmark uses, which had been throwing "expected whole-layout reuse to fire".
        //
        // ⚠️ THE ASSERTION THAT MATTERS IS THE SECOND ONE. Making reuse FIRE is easy and
        // wrong on its own: the bracket baked an absolute source offset, so a reused layout
        // would emit a stale data-pos after an edit that shifts every offset. The bracket now
        // carries a SourceIndex into the list DetectPedalBrackets rebuilds from the live
        // score, exactly as a music mark does against BuildAllMarks, and the byte-identity
        // check below is what says the re-derivation is right rather than merely present.
        string src = """
            tempo 120
            time 4/4
            key c major
            part lh { clef bass }
            section Main { lh { <c e>2@sustainOn <c g> | <f a>2@sustainOff@sustainOn <d f> | } }
            form main { Main }
            score main "x" { staff lh }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void SwingToggleEdit_DefeatsReuse_AndMatchesFull()
    {
        // Toggling swing at an UNCHANGED bpm ("tempo 120" -> "tempo 120 swing") changes no
        // measure content and no line-break gate, but the synthesized tempo/swing mark
        // differs. SwingSubdivision is in the score-global key, so reuse must be defeated
        // and the output must match a full recompile (before the fix: reuse fired -> stale).
        string src = """
            tempo 120
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c4 d e f | g4 a b c | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "tempo 120", "tempo 120 swing");
        var incremental = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void StaffNameEdit_DefeatsReuse_AndMatchesFull()
    {
        // A staff name drives the system indent and the drawn label but is NOT part of the
        // line-break gate (springs/prefix), so before the fix a name edit tripped
        // whole-layout reuse and kept the STALE name. AddStaffIdentity folds the name into
        // the per-measure content key, defeating reuse so output matches a full recompile.
        string src = """
            time 4/4
            key c major
            part rh "Violin" { clef treble }
            part lh "Cello" { clef bass }
            section Main { rh { c4 d e f | g4 a b c | } lh { c4 d e f | g4 a b c | } }
            form main { Main }
            score main "x" { grandStaff { staff rh staff lh } }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "rh \"Violin\"", "rh \"Viola\"");
        var incremental = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_WithLyrics_ReusesWholeLayout_AndMatchesFull()
    {
        // Lyrics are now migrated onto SourceIndex resolution, so a score carrying them
        // is reuse-eligible (LyricLayouts no longer in ReuseSafe). A content-unchanged
        // edit must reuse the whole layout AND stay byte-identical — the lyric data-pos
        // re-derives from the edited Lyrics table.
        string withLyrics = """
            time 4/4
            key c major
            part melody
            section Main {
              melody { c4 d e f | g4 a b c | }
              lyrics { Twin- kle lit- tle | star how I you |
              }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(withLyrics);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_WithGlissando_ReusesWholeLayout_AndMatchesFull()
    {
        // Glissando is note-hosted and now migrated (out of ReuseSafe), so a score with a
        // glissando is reuse-eligible. A content-unchanged edit must reuse the whole layout
        // AND stay byte-identical — the glissando data-pos re-derives from its host note.
        string withGliss = """
            time 4/4
            key c major
            part melody
            section Main {
              melody { g4@glissando c e@glissando b | c4 d e f | }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(withGliss);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_WithFingering_ReusesWholeLayout_AndMatchesFull()
    {
        // Fingering (single-note AND chord) is note-hosted and migrated, so a fingered
        // score is reuse-eligible. Reuse must fire and stay byte-identical.
        string withFingering = """
            time 4/4
            key c major
            part melody
            section Main {
              melody { g4@finger(1) a@finger(2) b@finger(3) c@finger(4) | <c@finger(1) e@finger(3) g@finger(5)>4 d e f | }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(withFingering);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_WithBeams_ReusesWholeLayout_AndMatchesFull()
    {
        // Beamed notes: the renderer's beamed-item set must be matched by POSITION
        // (staff, measure, item), NOT by MusicItem value — a value key includes the
        // shifting SourcePosition, so a reused layout would fail to recognise every
        // beamed note and re-stem it ON TOP of the beam (double stems). Guards that.
        string withBeams = """
            time 4/4
            key c major
            part melody
            section Main {
              melody { c8 d e f g a b c | d8 e f g a b c d | }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(withBeams);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void ContentUnchangedEdit_WithHairpin_ReusesWholeLayout_AndMatchesFull()
    {
        // Hairpins (detected from cresc/decresc marks) are now migrated out of ReuseSafe,
        // so a hairpin-bearing score is reuse-eligible. Reuse must fire and stay
        // byte-identical — the hairpin data-pos re-derives from the originating mark.
        string withHairpin = """
            time 4/4
            key c major
            part melody
            section Main {
              melody { c4@p d@cresc e f@f | g4@f a@decresc b c@p | }
            }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(withHairpin);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void TitleChange_DoesNotReuseLayout_ButMatchesFull()
    {
        // A rendered title is score-global: it is NOT in any per-measure content key, so
        // without the global-key guard a title edit would falsely reuse the layout and
        // render the stale title. It must NOT reuse, and must match a full recompile.
        string titled = "title \"Song\"\n" + Base;
        var tree = SyntaxTree.Parse(titled);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(titled, "title \"Song\"", "title \"Tune\"");
        var incremental = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    [Fact]
    public void WidthPreservingContentEdit_SkipsLineBreak_ButDoesNotReuseLayout()
    {
        var tree = SyntaxTree.Parse(Base);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // Adding an articulation keeps the line-break gate (still a skip) but DOES change
        // the measure's content key (the articulation side-table is folded into it), so the
        // whole-layout reuse is correctly declined while the cheaper break-skip still applies.
        var change = Replace(Base, "c4 d e f", "c4@staccato d e f");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.LastEditSkippedLineBreak);
        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    /// <summary>
    /// The per-system BEAM memo's incremental==full gate (HANDOFF §1 ⒟⁶ first step). Every
    /// other session fixture here is beamless quarters, so before this test no suite member
    /// ever returned a CACHED beam layout into a rendered picture: a keystroke edits one
    /// measure, the edited system recomputes (miss), and every other system's preliminary
    /// beams come back from <c>SystemLayoutCache.GetOrComputeStaffSystemBeams</c> (hits) —
    /// then ride into the final spanner pass. Byte-equality against a cache-free full render
    /// on EVERY step is what says a hit is the same beams, in the same order, as a fresh
    /// layout (the reassembly is cursor-matched by group identity; a defect there reorders
    /// or drops a beam and changes the SVG).
    /// </summary>
    [Fact]
    public void ChainedEditsOnABeamedMultiSystemScore_AlwaysMatchFull()
    {
        // 18 eighth-note measures: breaks into multiple systems on the default paper,
        // 2 beams per measure — so an edit in ONE measure leaves most systems' beams
        // to the memo. Bar 10 is spelled uniquely (its double g8) purely so the toggle
        // below has an unambiguous anchor in an otherwise-repeated text.
        // (Verified multi-system below, so the fixture cannot silently shrink into a
        // single-system score where the memo has nothing to reuse.)
        var plain = string.Join(" ", Enumerable.Repeat("c8 d8 e8 f8 g8 a8 b8 c'8 |", 9));
        var bars = plain + " c8 d8 e8 f8 g8 g8 b8 c'8 | "
            + string.Join(" ", Enumerable.Repeat("c8 d8 e8 f8 g8 a8 b8 c'8 |", 8));
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { " + bars + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        Assert.Equal(Full(source), Norm(session.Render()));

        // (find, replace) — a pitch toggle in the unique bar (one system misses, the rest
        // hit), then its inverse, then a structural insertion at the head that SHIFTS every
        // later system's index (the memo keys on systemIndex, so shifted systems must miss
        // and recompute rather than reuse a stale stamp).
        var steps = new (string Find, string Replace)[]
        {
            ("g8 g8 b8", "a8 a8 b8"),
            ("a8 a8 b8", "g8 g8 b8"),
            ("melody { c8", "melody { r1 | c8"),
        };
        foreach (var (find, replace) in steps)
        {
            string current = session.Tree.Text;
            var change = Replace(current, find, replace);
            int at = current.IndexOf(find, System.StringComparison.Ordinal);
            string editedText = current[..at] + replace + current[(at + find.Length)..];

            var incremental = Norm(session.Edit(change));
            Assert.Equal(Full(editedText), incremental);
        }

        // The fixture's premise, asserted so it cannot rot: the final layout really has
        // several systems (else every step above degenerated to one-system misses).
        var lastLayout = new LayoutEngine().Layout(
            SvgGenerator.CollectScore(session.Tree, RenderSpecParser.FindFirst(session.Tree)));
        Assert.True(lastLayout.AllSystems.Length >= 3,
            $"fixture shrank to {lastLayout.AllSystems.Length} system(s); the memo has nothing to reuse");
    }

    /// <summary>
    /// The per-system TIE and SLUR memos' incremental==full gate (2026-08-26 review,
    /// finding 4-2) — the bow twin of the beamed net above. The beams beside the prelim
    /// bows hit <c>GetOrComputeStaffSystemBeams</c> since ⒟⁶, but the ties and slurs of
    /// EVERY system were re-solved on every keystroke; now an intra-system bow comes back
    /// from <c>GetOrComputeStaffSystemTies/Slurs</c> and rides into the final pass through
    /// the carry. Byte-equality against a cache-free full render on every step says a hit
    /// is the same bows, in the same order (the reassembly is cursor-matched by column /
    /// slur identity, and any drift falls back to the whole-staff solve rather than
    /// guessing). BowMemoStats is the liveness half: a fixture whose bows all straddled
    /// systems would pass byte-equality while never exercising the memo.
    /// </summary>
    [Fact]
    public void ChainedEditsOnABowedMultiSystemScore_AlwaysMatchFull()
    {
        // 18 bars, each carrying an intra-bar slur and an intra-bar tie (a bow that
        // CROSSES a system break falls back by design, so the fixture keeps its bows
        // inside bars — the break can then never split one). Bar 10's a2~ is the
        // unique toggle anchor.
        var bar = "c4( d e) f | g2~ g4 a4 |";
        var bars = string.Join(" ", Enumerable.Repeat(bar, 7))
            + " c4( d e) f | a2~ a4 g4 | "
            + string.Join(" ", Enumerable.Repeat(bar, 7));
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { " + bars + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        Assert.Equal(Full(source), Norm(session.Render()));

        var steps = new (string Find, string Replace)[]
        {
            ("a2~ a4 g4", "b2~ b4 g4"),   // pitch toggle in the unique bar
            ("b2~ b4 g4", "a2~ a4 g4"),   // its inverse
            ("melody { c4(", "melody { r1 | c4("), // head insertion shifts system indices
        };
        foreach (var (find, replace) in steps)
        {
            string current = session.Tree.Text;
            var change = Replace(current, find, replace);
            int at = current.IndexOf(find, System.StringComparison.Ordinal);
            string editedText = current[..at] + replace + current[(at + find.Length)..];

            var incremental = Norm(session.Edit(change));
            Assert.Equal(Full(editedText), incremental);
        }

        // Liveness: the bow memos actually served (misses alone would mean the
        // fallback ran every time and the memo is dead machinery).
        var stats = session.SystemCache?.BowMemoStats ?? (0, 0);
        Assert.True(stats.Hits > 0,
            $"no bow memo hit across three keystrokes (hits {stats.Hits}, misses {stats.Misses}) "
            + "— every staff fell back or the memo was never consulted");

        // The fixture's premise: several systems, so the memo has cross-system reuse
        // to serve at all.
        var lastLayout = new LayoutEngine().Layout(
            SvgGenerator.CollectScore(session.Tree, RenderSpecParser.FindFirst(session.Tree)));
        Assert.True(lastLayout.AllSystems.Length >= 3,
            $"fixture shrank to {lastLayout.AllSystems.Length} system(s); the memo has nothing to reuse");
    }

    /// <summary>
    /// The per-system PAGING-AUGMENT memo's incremental==full gate (HANDOFF §1 ⒪′). The
    /// beamed fixture above never returns a cached AUGMENTED skyline into a rendered
    /// picture — its books carry no bows and few scripts, so the paging programs are
    /// trivial. This one is the v2bow texture in miniature: two voices, the second
    /// carrying a slur and a tie per pair of bars, staccati on the first — so every
    /// system's paging skyline is built through a real program (script steps + two bow
    /// groups), and on each keystroke the unedited systems' pairs come back from
    /// <c>SystemLayoutCache.GetOrComputePagingAugment</c> while the edited system misses
    /// (its base skyline instance and its resolved bow numbers both change). Byte-equality
    /// against a cache-free full render on EVERY step is what says a hit is the same
    /// silhouette, bit for bit, as a fresh merge — the paging skylines decide the
    /// inter-system springs, so a stale hit moves whole systems on the page.
    /// </summary>
    [Fact]
    public void ChainedEditsOnABowedTwoVoiceScore_AlwaysMatchFull()
    {
        // 26 bars per voice over multiple systems (18 broke into only 2 on the default
        // paper and the premise assert below fired). Bar 13 of voice 2 is spelled
        // uniquely (its "e4( d g," ) purely so the toggle below has an unambiguous anchor.
        var v1 = string.Join(" ", Enumerable.Repeat("g'4@staccato a' b' c'' | b'2 a'2 |", 13));
        var v2 = string.Join(" ", Enumerable.Repeat("e4( c g, c,) | g,2~ g,2 |", 6))
            + " e4( d g, c,) | g,2~ g,2 | "
            + string.Join(" ", Enumerable.Repeat("e4( c g, c,) | g,2~ g,2 |", 6));
        string source = "time 4/4\nkey c major\noctave absolute\npart m { clef treble }\n"
            + "section Main { m { voice { " + v1 + " } { " + v2 + " } } }\n"
            + "form main { Main }\nscore main \"x\" { staff m }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        Assert.Equal(Full(source), Norm(session.Render()));

        // (find, replace) — a slurred-note pitch toggle in the unique bar (one system's
        // program changes, the rest hit), its inverse (the memo must return to the FIRST
        // edit's answer through hits, not stale entries), then a rhythm re-spelling near
        // the head that moves the break gate and re-breaks systems (every base skyline
        // instance changes, so every program must miss and re-merge).
        var steps = new (string Find, string Replace)[]
        {
            ("e4( d g,", "e4( f g,"),
            ("e4( f g,", "e4( d g,"),
            ("b'2 a'2 | g'4@staccato", "b'1 | g'4@staccato"),
        };
        foreach (var (find, replace) in steps)
        {
            string current = session.Tree.Text;
            var change = Replace(current, find, replace);
            int at = current.IndexOf(find, System.StringComparison.Ordinal);
            string editedText = current[..at] + replace + current[(at + find.Length)..];

            var incremental = Norm(session.Edit(change));
            Assert.Equal(Full(editedText), incremental);
        }

        var lastLayout = new LayoutEngine().Layout(
            SvgGenerator.CollectScore(session.Tree, RenderSpecParser.FindFirst(session.Tree)));
        Assert.True(lastLayout.AllSystems.Length >= 3,
            $"fixture shrank to {lastLayout.AllSystems.Length} system(s); the memo has nothing to reuse");
    }

    [Fact]
    public void ChainedEdits_AlwaysMatchFull_WithExpectedSkips()
    {
        var session = new IncrementalCompiler(SyntaxTree.Parse(Base), Opt);
        session.Render();

        // (find, replace, expectSkip) — alternating gate-preserving (skip) and
        // gate-changing (recompute, incl. a structural measure insertion) edits.
        var steps = new (string Find, string Replace, bool ExpectSkip)[]
        {
            ("c4 d e f", "c4@staccato d e f", true),   // +articulation -> skip
            ("g4 a b c", "g2 a4 b4", false),           // re-rhythm        -> recompute
            ("d4 e f g", "d4@accent e f g", true),     // +articulation    -> skip
            ("a4 b c d", "a4 b c d | r1", false),      // insert a measure -> recompute
        };

        foreach (var (find, replace, expectSkip) in steps)
        {
            string current = session.Tree.Text;
            var change = Replace(current, find, replace);
            int at = current.IndexOf(find, System.StringComparison.Ordinal);
            string editedText = current[..at] + replace + current[(at + find.Length)..];

            var incremental = Norm(session.Edit(change));
            Assert.Equal(Full(editedText), incremental);
            Assert.Equal(expectSkip, session.LastEditSkippedLineBreak);
        }
    }

    // --- ⒟⁗ per-measure spring memo (HANDOFF §1 ▶) ---------------------------------
    // On a content-CHANGING edit the session rebuilds only the measures whose content-key
    // neighbourhood (keys i−1..i+1, index-aligned) moved, and reuses the rest of the
    // previous spring vector entry by entry. These nets hold the memo to the one standard
    // that matters: the vector it hands the gate and the layout must be VALUE-IDENTICAL to
    // a from-scratch build of the edited score — plus liveness (the reuse actually fires),
    // because a memo that silently recomputes everything passes every equality net.

    /// <summary>The memo'd vector must equal a from-scratch build of the session's current
    /// tree, element for element (MeasureSpringData is a record struct, so this is deep
    /// value equality down through the LineStartSpring).</summary>
    private static void AssertSpringsMatchFromScratch(IncrementalCompiler session)
    {
        var tree = session.Tree;
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        double shortest = SpacingRules.CalculateCommonShortestDuration(score);
        Assert.Equal(SystemBreaker.ComputeMultiStaffSpringData(score, shortest),
            session.SpringsForTest);
    }

    /// <summary>
    /// The memo's basic contract on a plain content-changing edit (an added accidental —
    /// regime ⑶, the gate moves): the spring vector equals a from-scratch build, the SVG
    /// equals a full recompile, and EXACTLY the edited measure plus its two neighbours were
    /// recomputed — the rest came back from the previous vector. The neighbour count is
    /// asserted exactly so the window cannot silently widen (a perf regression) or narrow
    /// (a soundness hole) without this saying so.
    /// </summary>
    [Fact]
    public void SpringMemo_RebuildsOnlyTheEditedNeighbourhood_AndMatchesFromScratch()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            phrase mel { c4 d e f | g4 a b c | d4 e f g | a4 b c d | e4 f g a | f4 g a b | g4 a b c | a4 g f e | }
            section Main { melody { mel } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "f4 g a b", "fis4 g a b");   // measure index 5
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        AssertSpringsMatchFromScratch(session);
        // 8 measures; key 5 moved -> measures 4 (right-neighbour window), 5 and 6
        // (left-neighbour window) recomputed, the other 5 reused.
        Assert.Equal((5, 3), session.LastSpringMemo);
    }

    /// <summary>
    /// The LEFT-neighbour window is load-bearing: a multi-measure-rest run whose opening
    /// measure declares no start bar line prices its run rod from the PREVIOUS measure's
    /// end bar line (SpacingRules.RunLeftBoundBarline) — a spring input that lives in key
    /// i−1, not in key i. The edit flips that bar line (double → regular) while leaving
    /// the run-opening measure's own key untouched; a memo that only compared key i would
    /// hand back the run's rod priced from the OLD bar line.
    /// </summary>
    [Fact]
    public void SpringMemo_RunRodReadsThePreviousMeasuresBarline_LeftNeighbourWindow()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c1 || R1*2 | c4 d e f | c4 d e f | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var before = session.SpringsForTest![1];

        var change = Replace(src, "c1 ||", "c1 |");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        AssertSpringsMatchFromScratch(session);
        // The hazard is real, not vacuous: the run-opening measure's springs DID change
        // even though its own content key did not (the rod reaches the left bar line).
        Assert.NotEqual(before, session.SpringsForTest![1]);
        // 5 measures; key 0 moved -> measure 0 (its own key) and 1 (left-neighbour
        // window) recomputed, measures 2..4 reused.
        Assert.Equal((3, 2), session.LastSpringMemo);
    }

    /// <summary>
    /// The RIGHT-neighbour window is load-bearing: whether a break is forbidden AFTER
    /// measure i asks whether i+1 belongs to the same multi-measure-rest run
    /// (MmrRunMap.ForbidsBreakAfter) — visible in key i+1, never in key i. Extending the
    /// run (<c>R1*2</c> → <c>R1*3</c>) flips the old run tail's break permission while its
    /// own key is unchanged (an interior rested measure both times); a memo that compared
    /// only key i would hand back BreakPermission.Allow on a boundary the run now spans —
    /// which is exactly what the from-scratch comparison below would catch.
    /// </summary>
    [Fact]
    public void SpringMemo_RunExtension_FlipsForbidsBreakAfter_RightNeighbourWindow()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody { c2 d2 | R1*2 | c1 | e2 f2 | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // Run 1..2: a break after its LAST measure (index 2) is where a break belongs.
        Assert.Equal(BreakPermission.Allow, session.SpringsForTest![2].BreakPermission);

        var change = Replace(src, "R1*2", "R1*3");             // run 1..2 grows to 1..3
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        AssertSpringsMatchFromScratch(session);
        // The old run tail now sits INSIDE the run: a break after it would split the MMR.
        Assert.Equal(BreakPermission.Forbid, session.SpringsForTest![2].BreakPermission);
    }

    /// <summary>
    /// The memo on a MULTI-STAFF score: every staff's content at the measure index folds
    /// into one key, so an accidental added in the soprano rebuilds that measure's combined
    /// springs (and its window) and reuses the rest — and the vector still equals a
    /// from-scratch build of the grand staff.
    /// </summary>
    [Fact]
    public void SpringMemo_OnAGrandStaff_MatchesFromScratch()
    {
        const string src = """
            time 4/4
            key c major
            part sop { clef treble }
            part bas { clef bass }
            section A {
              sop { c'4 d' e' f' | g'4 a' b' c'' | c'4 d' e' f' | g'4 a' b' c'' | }
              bas { c4 d e f | g4 a b c' | c4 d e f | g4 a b c' | }
            }
            form main { A }
            score main "x" {
              grandStaff {
                staff sop "Soprano"
                staff bas "Bass"
              }
            }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "g'4 a' b' c''", "g'4 a' bes' c''");   // measure index 1
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        AssertSpringsMatchFromScratch(session);
        // 4 measures; key 1 moved -> measures 0..2 recomputed, measure 3 reused.
        Assert.Equal((1, 3), session.LastSpringMemo);
    }

    /// <summary>
    /// The memo on a SUNG book (the cross-bar lyric rod port, 2026-08-20): each entry
    /// carries its LyricBarPricing half and the conditional bar-boundary halves read the
    /// NEIGHBOURS' lyrics — all inside the i−1..i+1 window, which this holds to the same
    /// standard as the rest: value-identity with a from-scratch build (deep equality runs
    /// through LyricBarPricing.Equals), reuse liveness, and non-vacuity (the reused tail
    /// entry really carries lyric pricing, so the equality is not two nulls agreeing).
    /// </summary>
    [Fact]
    public void SpringMemo_OnASungBook_MatchesFromScratch()
    {
        const string src = """
            octave absolute
            time 4/4
            key c major

            part melody {
              section A { a4 a a a | g4 a a a | f4 a a a | e4 a a a | }
            }

            lyrics w sings melody {
              section A { mum mum mum mum | mum mum mum mum | mum mum mum mum | mum mum mum mum }
            }

            form main { ~A }

            score main "x" {
              staff melody
              lyrics w
            }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "g4 a a a", "gis4 a a a");   // measure index 1
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        AssertSpringsMatchFromScratch(session);
        // 4 measures; key 1 moved -> measures 0..2 recomputed, measure 3 reused.
        Assert.Equal((1, 3), session.LastSpringMemo);
        // The reused entry is a SUNG measure whose pricing half came back from the
        // previous vector — the from-scratch equality above compared it deeply.
        Assert.NotNull(session.SpringsForTest![3].CrossBarLyricPricing);
    }

    // --- ⑶ beamdirs per-measure beam-detection memo (HANDOFF §1) --------------------
    // The collect-phase stem-direction probe (ResolveBeamStemDirections) re-detects, per
    // keystroke, only the measures whose content-key (intrinsic content + effective meter
    // + tuplet brackets) moved; the bake — and BeamId numbering — always runs live, so the
    // resolved model is byte-identical to a from-scratch collect. These nets hold the memo
    // to byte-identity with a full recompile PLUS liveness (exact hit/miss counts, so the
    // reuse cannot silently widen into a soundness hole or narrow into a no-op), and they
    // pin the two guards an equality net alone would let rot: the effective-signature fold
    // (a mid-piece \time re-beams the UNCHANGED tail measures) and the cross-measure
    // manual-beam gate (those measures always detect live). The replay-vs-live surface
    // equivalence lives in BeamDetectionMemoTests.

    private const string BeamedBook = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody {
          c8 d e f g f e d |
          d8 e f g a g f e |
          e8 f g a b a g f |
          f8 g a b c b a g |
          g8 a b c d c b a |
          a8 b c d e d c b |
          b8 c d e f e d c |
          c8 e g e c e g e |
        } }
        form main { Main }
        score main "x" { staff melody }
        """;

    /// <summary>The memo's basic contract on a pitch edit in a beamed book: the SVG equals
    /// a full recompile, and EXACTLY the edited measure re-detects — the other seven replay
    /// from the previous keystroke's detection.</summary>
    [Fact]
    public void BeamMemo_PitchEditInABeamedBook_RedetectsOnlyTheEditedMeasure()
    {
        var tree = SyntaxTree.Parse(BeamedBook);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(BeamedBook, "e d c b", "e d c a");   // measure index 5
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        Assert.Equal((7, 1), session.LastBeamMemo);
    }

    /// <summary>
    /// The EFFECTIVE-signature fold is load-bearing: a mid-piece <c>time</c> edit changes
    /// how every FOLLOWING measure beams while leaving those measures' own content
    /// untouched. The meter in effect is folded into each measure's key, so the tail
    /// re-detects (misses) and only the measures still under the unchanged opening meter
    /// replay. A key without the fold would replay the tail's old grouping — which the
    /// byte-identity assertion would catch as a wrong picture.
    /// </summary>
    [Fact]
    public void BeamMemo_MidPieceMeterEdit_RebeamsTheTail_AndMatchesFullRecompile()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c8 d e f g f e d |
              time 6/8 e8 f g a g f |
              f8 g a b a g |
              g8 a b c b a |
            } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "time 6/8", "time 3/4");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        // Measure 0 (still under 4/4) replays; measure 1 (its own content moved) and
        // measures 2..3 (content untouched, meter in effect moved) re-detect.
        Assert.Equal((1, 3), session.LastBeamMemo);
    }

    /// <summary>
    /// The cross-measure manual-beam gate: the two measures a <c>[ … | … ]</c> pair spans
    /// are never served (nor stored) by the memo — their detection depends on each other —
    /// while the rest of the book still replays. An edit elsewhere must leave the beam's
    /// picture byte-identical to a full recompile.
    /// </summary>
    [Fact]
    public void BeamMemo_CrossMeasureManualBeam_DetectsLive_AndMatchesFullRecompile()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c8 d e f g f e d |
              d8[ e f g a g f e |
              e8] f g a b a g f |
              f8 g a b c b a g |
            } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "c b a g", "c b a c");          // measure index 3
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
        // Measures 1..2 (the pair's span) are gated out of the counts entirely;
        // measure 0 replays, measure 3 re-detects.
        Assert.Equal((1, 1), session.LastBeamMemo);
    }

    /// <summary>Within-collect dedup: a book of content-identical measures detects ONE
    /// measure and replays the rest even on the session's very first (full) compile — and
    /// that compile still equals a memo-free full generate.</summary>
    [Fact]
    public void BeamMemo_IdenticalMeasures_DedupWithinOneCollect()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
              c8 d e f g f e d |
            } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var session = new IncrementalCompiler(SyntaxTree.Parse(src), Opt);
        Assert.Equal(Full(src), Norm(session.Render()));
        // One miss: the key folds only what detection READS (AddDetectionInputs), so
        // measure 0's section label — which detection never looks at — does not split
        // the key. (Under the earlier MeasureContentKey.Of fold it did: this was (6, 2),
        // measured — the label sat in the intrinsic key.)
        Assert.Equal((7, 1), session.LastBeamMemo);
    }

    // --- ⒭ per-system SVG fragment memo (HANDOFF §1 ▶) -----------------------------
    // On every edit the renderer replays the recorded SVG text of each system whose
    // content-key window and drawn geometry are unchanged, re-emitting its data-pos /
    // data-alt numbers through the edit window (SvgSystemFragmentCache). These nets
    // hold the memo to byte-identity with a cache-free full render PLUS liveness (the
    // replay actually fires — a memo that silently redraws everything passes every
    // equality net), and they pin the two mechanisms an equality net alone would let
    // rot invisibly: the slot SHIFT (a Δ≠0 edit must move every baked offset) and the
    // RIGHT window (the end-of-line courtesy reads the next system's opening).

    /// <summary>A pitch toggle in one bar of a multi-system beamed book: the SVG equals
    /// a full recompile and every system outside the edited window replays its recorded
    /// text. The drawn count is bounded, not exact, because the edited measure may sit
    /// at a system boundary (its ±1-measure window then touches two systems).</summary>
    [Fact]
    public void RenderFragments_ReplayAllButTheEditedWindow_AndMatchFull()
    {
        var plain = string.Join(" ", Enumerable.Repeat("c8 d8 e8 f8 g8 a8 b8 c'8 |", 9));
        var bars = plain + " c8 d8 e8 f8 g8 g8 b8 c'8 | "
            + string.Join(" ", Enumerable.Repeat("c8 d8 e8 f8 g8 a8 b8 c'8 |", 8));
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { " + bars + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();

        var change = Replace(source, "g8 g8 b8", "a8 a8 b8");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "g8 g8 b8", "a8 a8 b8")), incremental);
        var (replayed, drawn) = session.LastRenderFragments;
        int total = replayed + drawn;
        Assert.True(total >= 4, $"fixture shrank to {total} system(s); nothing to replay");
        Assert.True(drawn <= 2, $"window widened: {drawn} of {total} systems drew live");
        Assert.True(replayed == total - drawn && replayed >= 2,
            $"replay did not fire: replayed {replayed} / drawn {drawn}");
    }

    /// <summary>
    /// The data-pos slot shift is load-bearing: a whitespace insertion in the middle of
    /// the book changes NO content key and NO geometry — every system replays — but it
    /// shifts every source offset at/after the edit by Δ=+1, so the replayed text must
    /// re-emit its numbers through the window (offsets before the edit unchanged,
    /// after it +1). Byte-equality with a full render of the edited text is the claim;
    /// the inequality with the PREVIOUS render proves the hazard is real (the numbers
    /// really moved — a fragment replayed verbatim would equal the old bytes instead).
    /// </summary>
    [Fact]
    public void RenderFragments_MidBookTriviaInsertion_ShiftsEveryLaterDataPos()
    {
        var bars = string.Join(" ", Enumerable.Repeat("c8 d8 e8 f8 g8 a8 b8 c'8 |", 18));
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { " + bars + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        string before = Norm(session.Render());

        // Insert one space between two bars near the middle: pure trivia, Δ=+1.
        int mid = source.IndexOf("| c8", source.Length / 2, System.StringComparison.Ordinal);
        Assert.True(mid >= 0);
        var change = new TextChange(new TextSpan(mid + 1, 0), " ");
        var incremental = Norm(session.Edit(change));

        string editedText = source[..(mid + 1)] + " " + source[(mid + 1)..];
        Assert.Equal(Full(editedText), incremental);
        Assert.NotEqual(before, incremental); // the offsets moved; verbatim replay would not
        var (replayed, drawn) = session.LastRenderFragments;
        Assert.True(replayed >= 4 && drawn == 0,
            $"expected every system to replay: replayed {replayed} / drawn {drawn}");
    }

    /// <summary>
    /// The end-of-line courtesy regime stays byte-identical across an edit of the NEXT
    /// system's opening time change (GetSystemEndTimeChange reads measure last+1 — the
    /// fragment key's RIGHT window). ⚠️ MEASURED under a window-dropping poison
    /// (session 151): this net still passes, because the courtesy glyph carries the
    /// time-change item's own data-pos, which lands INSIDE the edit window and declines
    /// the fragment through the slot check — so the right window has NO isolating
    /// positive control yet. It is kept on the fold argument (the neighbour's opening
    /// items are read; an item whose SourcePosition lags outside its own value text
    /// would slip past the slot check), and this net pins the regime's byte-identity.
    /// </summary>
    [Fact]
    public void RenderFragments_SystemEndCourtesy_ReadsTheNextSystemsOpening()
    {
        const string src = """
            time 4/4
            key c major
            part melody { clef treble }
            section Main { melody {
              c4 d e f | g4 a b c' |
              break
              time 3/4
              c4 d e | f4 g a |
            } }
            form main { Main }
            score main "x" { staff melody }
            """;
        var tree = SyntaxTree.Parse(src);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(src, "time 3/4", "time 2/4");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(tree.WithChange(change).Text), incremental);
    }

    /// <summary>
    /// Interactive (preview) output replays too: the hit rectangles and data-alt alias
    /// lists are extra baked offsets, and a Δ≠0 edit must shift every one of them. Byte
    /// equality against a cache-free interactive render is the whole claim.
    /// </summary>
    [Fact]
    public void RenderFragments_InteractiveOutput_ShiftsDataAltAcrossAnEdit()
    {
        var interactive = new SvgRenderOptions { EmbedFont = false, Interactive = true };
        var bars = string.Join(" ", Enumerable.Repeat("<c e g>4 d e f | g4 a b c' |", 8));
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { " + bars + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), interactive);
        session.Render();

        int mid = source.IndexOf("| g4", source.Length / 2, System.StringComparison.Ordinal);
        Assert.True(mid >= 0);
        var change = new TextChange(new TextSpan(mid + 1, 0), " ");
        var incremental = Norm(session.Edit(change));

        string editedText = source[..(mid + 1)] + " " + source[(mid + 1)..];
        var full = Norm(SvgGenerator.Generate(SyntaxTree.Parse(editedText), interactive));
        Assert.Equal(full, incremental);
        var (replayed, drawn) = session.LastRenderFragments;
        Assert.True(replayed >= 2 && drawn == 0,
            $"expected every system to replay: replayed {replayed} / drawn {drawn}");
    }

    // --- ⒭ overlay (page-level) fragment memo: fingerings (HANDOFF §1 ▶ ⒭) ---------
    // The page-level drawers run AFTER the per-system loop, drawer-major, so their
    // contiguous unit is one drawer's output on one PAGE. Fingerings are the measured
    // overlay term of the keystroke render floor; their fragment is keyed by a VALUE
    // FOLD of the exact draw inputs (digit, x, y, page height) plus the same anchor /
    // slot machinery as the system fragments. These nets hold the memo to byte-identity
    // with a cache-free full render PLUS liveness, and pin the value fold with an
    // isolating positive control (a Δ=0 digit edit that no other layer declines).

    /// <summary>The overlay-net fixture: enough fingered bars to spill onto 2+ pages
    /// (a single treble staff packs ~12 systems a page, so 48 bars stayed on one).</summary>
    private static string FingeredBook() =>
        "time 4/4\nkey c major\npart melody { clef treble }\n"
        + "section Main { melody { "
        + string.Join(" ", Enumerable.Repeat(
            "c4@finger(1) d4@finger(2) e4@finger(3) f4@finger(4) |", 160))
        + " } }\n";

    /// <summary>A multi-page fingered book: after a pitch toggle on the first page, the
    /// SVG equals a full recompile, every untouched page replays its recorded fingering
    /// overlay, and only the edited page (± a page-boundary straddle) draws it live.</summary>
    [Fact]
    public void OverlayFragments_FingeringsReplayOnUntouchedPages_AndMatchFull()
    {
        string source = FingeredBook();
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();
        var (_, pages) = session.LastRenderOverlays;
        Assert.True(pages >= 2, $"fixture must span 2+ pages; captured {pages} overlay page(s)");

        var change = Replace(source, "e4@finger(3)", "d4@finger(3)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "e4@finger(3)", "d4@finger(3)")), incremental);
        var (replayed, drawn) = session.LastRenderOverlays;
        Assert.True(replayed + drawn == pages,
            $"page count moved under the edit: {replayed}+{drawn} != {pages}");
        Assert.True(drawn <= 2, $"overlay window widened: {drawn} of {pages} pages drew live");
        Assert.True(replayed >= 1, $"overlay replay did not fire: replayed {replayed} / drawn {drawn}");
    }

    /// <summary>
    /// The value fold is load-bearing, isolated as a LAYER: a same-length digit edit
    /// (finger 2 → 4, Δ=0) moves NO source offset, so the anchor and slot layers both
    /// pass and a foldless cache would replay the stale "2" digit verbatim — only the
    /// value fold (Number, and X0/Y if the digit's advance differs) declines the edited
    /// page. Byte equality with the full render is the fold's positive control, and the
    /// liveness assert pins that the OTHER pages did replay (the memo stayed on).
    /// </summary>
    [Fact]
    public void OverlayFragments_DigitEdit_IsCaughtByTheValueFoldAlone()
    {
        string source = FingeredBook();
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();
        var (_, pages) = session.LastRenderOverlays;
        Assert.True(pages >= 2, $"fixture must span 2+ pages; captured {pages} overlay page(s)");

        var change = Replace(source, "@finger(2)", "@finger(4)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "@finger(2)", "@finger(4)")), incremental);
        var (replayed, drawn) = session.LastRenderOverlays;
        Assert.True(replayed >= 1, $"overlay replay did not fire: replayed {replayed} / drawn {drawn}");
        Assert.True(drawn >= 1 && drawn <= 2,
            $"the edited page must draw its overlay live: replayed {replayed} / drawn {drawn}");
    }

    /// <summary>A mid-book trivia insertion (Δ=+1, no content or geometry change): every
    /// page's fingering overlay replays while every emitted data-pos at/after the edit
    /// shifts — byte equality with the full render proves the slot shift, and the counts
    /// prove nothing drew live.</summary>
    [Fact]
    public void OverlayFragments_TriviaInsertion_ShiftsDataPosInReplayedPages()
    {
        string source = FingeredBook();
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        string before = Norm(session.Render());
        var (_, pages) = session.LastRenderOverlays;
        Assert.True(pages >= 2, $"fixture must span 2+ pages; captured {pages} overlay page(s)");

        int mid = source.IndexOf("| c4", source.Length / 2, System.StringComparison.Ordinal);
        Assert.True(mid >= 0);
        var change = new TextChange(new TextSpan(mid + 1, 0), " ");
        var incremental = Norm(session.Edit(change));

        string editedText = source[..(mid + 1)] + " " + source[(mid + 1)..];
        Assert.Equal(Full(editedText), incremental);
        Assert.NotEqual(before, incremental); // the offsets moved; verbatim replay would not
        var (replayed, drawn) = session.LastRenderOverlays;
        Assert.True(replayed == pages && drawn == 0,
            $"expected every page's overlay to replay: replayed {replayed} / drawn {drawn}");
    }

    // --- chained keystrokes: the data-pos basis of a CARRIED-OVER system (session 190) ---
    // Every net above edits ONCE from a warm session, and that shape is structurally blind
    // to this: the per-system layout memo (SystemLayoutCache, incl. its FingScriptMemo)
    // hands back a system computed at an EARLIER edit, and the annotation layouts inside it
    // carry THAT edit's source offsets. MeasureContentKey is blind to source offsets BY
    // DESIGN — a trivia insertion must not move content — so an equal key certifies the
    // GEOMETRY and says nothing about data-pos. After one edit a carried-over system is one
    // edit behind and the render still agrees; by the THIRD keystroke it is two behind and
    // its data-pos freezes, drifting further with every later keystroke while the picture
    // stays byte-identical. That is why the renderer must re-derive data-pos on every
    // session render, not only when the WHOLE layout was reused (SharedRenderer.RenderTo).

    /// <summary>The chained-keystroke fixture: fingered BEAMED eighths, so twelve bars
    /// already fill enough systems for one to survive two keystrokes without being
    /// redrawn.</summary>
    /// <remarks>
    /// ⚠️ MEASURED, NOT ASSUMED (session 190). Crossing beamed/unbeamed × chord/single ×
    /// 12..160 bars: fingerings are NECESSARY (no unfingered shape ever diverges), and the
    /// system count is what decides — beamed shapes diverge from 12 bars, quarter-note
    /// CHORDS only at 160, and quarter-note SINGLE notes never did up to 160. That last row
    /// is exactly <see cref="FingeredBook"/>, which is why reusing it here left this net
    /// green while the defect was live: a fixture chosen by resemblance is not a fixture.
    /// </remarks>
    private static string BeamedFingeredBook(int bars = 12) =>
        "time 4/4\nkey c major\npart melody { clef treble }\n"
        + "section Main { melody { "
        + string.Join(" ", Enumerable.Repeat(
            "c8@finger(1) d8@finger(2) e8@finger(3) f8@finger(4) "
            + "g8@finger(5) a8@finger(4) b8@finger(3) c'8@finger(2) |", bars))
        + " } }\n";

    /// <summary>Three single-character insertions, sent one at a time exactly as an editor
    /// sends them: every one must still render byte-identical to a full recompile. The
    /// liveness asserts pin that the chain actually reaches the regime — a keystroke that
    /// re-runs layout (so the whole-layout-reuse path is NOT what is being tested) while the
    /// fingering memo is serving carried-over units.</summary>
    [Fact]
    public void ChainedKeystrokes_KeepDataPosEqualToFull_WhenSystemsAreCarriedOver()
    {
        string source = BeamedFingeredBook();
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        string before = Norm(session.Render());
        var fing = session.SystemCache!.FinalFingScripts;
        int hits0 = fing.Hits;

        int at = source.IndexOf('|', source.Length / 2);
        Assert.True(at > 0, "fixture must have a bar line past the middle");

        string live = source;
        bool everReranLayout = false;
        string incremental = "";
        foreach (var (offset, ch) in new[] { (0, " "), (1, "g"), (2, "4") })
        {
            incremental = Norm(session.Edit(new TextChange(new TextSpan(at + offset, 0), ch)));
            live = live[..(at + offset)] + ch + live[(at + offset)..];
            Assert.Equal(Full(live), incremental);
            everReranLayout |= !session.LastEditReusedLayout;
        }

        Assert.NotEqual(before, incremental); // the offsets moved; a verbatim replay would not
        Assert.True(everReranLayout,
            "no keystroke re-ran layout, so the whole-layout-reuse path answered every one "
            + "and the carried-over-system regime was never entered");
        Assert.True(fing.Hits > hits0,
            $"the fingering memo never served a carried-over unit (hits {fing.Hits}, misses {fing.Misses})");
    }

    // --- ⒟⁶⑵ above-staff stacking memo (AboveStackMemo, session 161) -------------
    // A bar number stands on every system, so every keystroke used to rebuild every
    // system's tracker (a copy of its whole inside-staff profile) and re-place every
    // above-staff grob, twice (preliminary + final annotation pass). The memo replays
    // the systems whose stacking inputs are unchanged; these nets hold it to byte
    // identity against a cache-free full compile PLUS liveness on both passes' stores.

    /// <summary>A pitch toggle in a multi-system book with above-staff grobs (bar
    /// numbers on every system, a tuplet, a tempo mark): the SVG equals a full
    /// recompile and both annotation passes' memos replay unchanged systems.</summary>
    [Fact]
    public void AboveStackMemo_ReplaysUnchangedSystems_AndMatchesFull()
    {
        string source = "time 4/4\nkey c major\ntempo 96\npart melody { clef treble }\n"
            + "section Main { melody { tuplet 3/2 { c8 d e } f4 g4 a4 | "
            + string.Join(" ", Enumerable.Repeat("c4 d e f | g4 a b c' |", 12))
            + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();
        var prelim = session.SystemCache!.PreliminaryAboveStack;
        var final = session.SystemCache!.FinalAboveStack;
        int prelimHits0 = prelim.Hits, finalHits0 = final.Hits;
        int prelimMisses0 = prelim.Misses;

        var change = Replace(source, "g4 a b c'", "g4 a b d'");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "g4 a b c'", "g4 a b d'")), incremental);
        // Liveness, both passes: the book spans several systems and only the edited
        // one (± neighbours the respacing touches) may decline.
        Assert.True(prelim.Hits > prelimHits0,
            $"preliminary above-stack memo never hit (hits {prelim.Hits}, misses {prelim.Misses})");
        Assert.True(final.Hits > finalHits0,
            $"final above-stack memo never hit (hits {final.Hits}, misses {final.Misses})");
        // The edited system declined (the memo is not replaying everything blindly).
        Assert.True(prelim.Misses > prelimMisses0,
            $"the edited system should have declined (misses {prelim.Misses})");
    }

    // --- ⒟⁶⑶ fingering memo (FingScriptMemo, session 163) ------------------------
    // Every digit was re-islanded and re-columned on every keystroke, twice — measured
    // at islands 28.1 + walk 39.1 ms per keystroke on perf-fingbeam1k (session 163).
    // The memo replays the (staff, system) units whose inputs are unchanged. These nets
    // hold it to byte identity against a cache-free full compile, prove the reference
    // keys are load-bearing (a stale replay would print the OLD digit), and pin the
    // declared gate: a unit carrying a script is never memoized.

    /// <summary>A fingered book over several systems: an edit far from most of them
    /// equals a full recompile and both annotation passes replay unchanged units.</summary>
    [Fact]
    public void FingScriptMemo_ReplaysUnchangedUnits_AndMatchesFull()
    {
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { "
            + string.Join(" ", Enumerable.Repeat(
                "c4@finger(1) d@finger(2) e@finger(3) f@finger(4) | "
                + "g4@finger(1) a@finger(2) b@finger(3) c'@finger(4) |", 12))
            + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();
        var prelim = session.SystemCache!.PreliminaryFingScripts;
        var final = session.SystemCache!.FinalFingScripts;
        int prelimHits0 = prelim.Hits, finalHits0 = final.Hits, prelimMisses0 = prelim.Misses;

        var change = Replace(source, "g4@finger(1)", "a4@finger(1)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "g4@finger(1)", "a4@finger(1)")), incremental);
        Assert.True(prelim.Hits > prelimHits0,
            $"preliminary fingering memo never hit (hits {prelim.Hits}, misses {prelim.Misses})");
        Assert.True(final.Hits > finalHits0,
            $"final fingering memo never hit (hits {final.Hits}, misses {final.Misses})");
        Assert.True(prelim.Misses > prelimMisses0,
            $"the edited unit should have declined (misses {prelim.Misses})");
    }

    /// <summary>STALENESS POSITIVE CONTROL: changing a DIGIT (not a pitch) must decline
    /// that unit and print the new number. The model measure is the reference key that
    /// catches it — without it the unit's inputs would look unchanged to a key built out
    /// of measure layouts alone, and the memo would replay the old digit verbatim.</summary>
    [Fact]
    public void FingScriptMemo_ChangedDigit_DeclinesAndMovesTheAnswer()
    {
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { "
            + string.Join(" ", Enumerable.Repeat(
                "c4@finger(1) d@finger(2) e@finger(3) f@finger(4) |", 14))
            + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        string before = Norm(session.Render());

        var change = Replace(source, "e@finger(3)", "e@finger(5)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "e@finger(3)", "e@finger(5)")), incremental);
        Assert.NotEqual(before, incremental); // a replayed unit would still say "3"
    }

    /// <summary>A slur covering the fingered notes: the digit rides off the bow
    /// (<c>avoid-slur #'around</c>), so the slur layouts are part of the unit's key.
    /// An edit elsewhere must still equal a full recompile.</summary>
    [Fact]
    public void FingScriptMemo_SlurredDigits_MatchFull()
    {
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { "
            + string.Join(" ", Enumerable.Repeat(
                "c4@finger(1)( d@finger(2) e@finger(3) f@finger(4)) |", 14))
            + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();

        var change = Replace(source, "d@finger(2)", "e@finger(2)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "d@finger(2)", "e@finger(2)")), incremental);
    }

    /// <summary>THE DECLARED GATE: a unit carrying ANY script is never memoized, because
    /// a script and a digit on one note share a column and the articulation half of the
    /// call is deliberately left unfiltered. With a script in every measure the store
    /// must stay empty — and the output must still equal a full recompile.</summary>
    [Fact]
    public void FingScriptMemo_UnitWithAScript_IsNeverMemoized()
    {
        string source = "time 4/4\nkey c major\npart melody { clef treble }\n"
            + "section Main { melody { "
            + string.Join(" ", Enumerable.Repeat(
                "c4@finger(1)@staccato d@finger(2) e@finger(3) f@finger(4)@accent |", 14))
            + " } }\n";
        var session = new IncrementalCompiler(SyntaxTree.Parse(source), Opt);
        session.Render();
        var prelim = session.SystemCache!.PreliminaryFingScripts;
        var final = session.SystemCache!.FinalFingScripts;

        var change = Replace(source, "d@finger(2)", "e@finger(2)");
        var incremental = Norm(session.Edit(change));

        Assert.Equal(Full(ApplyFirst(source, "d@finger(2)", "e@finger(2)")), incremental);
        Assert.True(prelim.Hits == 0 && final.Hits == 0,
            $"a scripted unit must never be memoized (prelim {prelim.Hits}, final {final.Hits})");
    }

    // ============================================================
    // Line-break DP row-prefix resume (2026-08-26 review, finding 4-5)
    // ============================================================

    /// <summary>Finding 4-5 liveness + value: a GATE-CHANGING edit (an accidental
    /// widens its measure's springs) runs the line-break DP, and in a session the
    /// DP must reuse the table rows before the first changed spring
    /// (LineBreakDpSession) while staying byte-identical to a full recompile —
    /// across a late edit, an early edit against the updated baseline, and an
    /// n-changing edit (a whole bar inserted, the re-stride path). A poison that
    /// treats every row as reusable serves the previous keystroke's break solution
    /// and turns the equality red (verified while building this test).</summary>
    [Fact]
    public void LineBreakDp_GateChangingEdits_ReuseTheRowPrefix()
    {
        string source = "time 4/4\nkey c major\npart m { clef treble }\n"
            + "section S { m { " + string.Join(" | ", Enumerable.Repeat("c4 d e f", 40)) + " } }\n"
            + "form main { S }\nscore main { staff m }\n";

        var tree = SyntaxTree.Parse(source);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        // Late gate-changing edit: a bar near the end densifies to sixteenths — its
        // springs widen enough to overflow its line and MOVE the break solution, so
        // a table serving stale rows cannot hide behind an unchanged partition.
        int at = tree.Text.LastIndexOf("c4 d e f | c4 d e f", System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(at, 8), "c16 d e f g a b c d e f g a b c d"));
        Assert.Equal(Full(tree.Text), Norm(session.RenderIncremental(tree)));
        var stats = session.SystemCache!.LineBreakDp.Stats;
        Assert.True(stats.Reused > 30 && stats.Recomputed > 0,
            $"late gate edit: expected a large reused row prefix, got reused {stats.Reused} / "
            + $"recomputed {stats.Recomputed}");

        // Early edit against the updated baseline: small prefix, the tail refills.
        int early = tree.Text.IndexOf("c4 d e f", System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(early + 3, 1), "dis"));
        Assert.Equal(Full(tree.Text), Norm(session.RenderIncremental(tree)));

        // n-changing edit: a whole bar inserted mid-book (the re-stride path).
        int mid = tree.Text.IndexOf("c4 d e f", tree.Text.Length / 2, System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(mid, 0), "g4 a b c | "));
        Assert.Equal(Full(tree.Text), Norm(session.RenderIncremental(tree)));
    }

    // ============================================================
    // Named render sessions (2026-08-26 review, finding 3-1)
    // ============================================================

    /// <summary>Two scores over two different parts, music ABOVE the render blocks —
    /// the shape in which an edit near the blocks leaves the whole book as a common
    /// prefix for the collect resume to adopt, which is exactly where a stale spec
    /// could serve stale measures.</summary>
    private const string TwoScores = """
        time 4/4
        key c major
        part melody { clef treble }
        part alto { clef treble }
        phrase mel { c4 d e f | g4 a b c | d4 e f g | a4 b c d | }
        phrase alt { e4 f g a | b4 c d e | f4 g a b | c4 d e f | }
        section Main { melody { mel } alto { alt } }
        form main { Main }
        form sub { Main }
        score main "x" { staff melody }
        score sub "y" { staff alto }
        """;

    private static string FullNamed(string text, string name) =>
        Norm(SvgGenerator.Generate(SyntaxTree.Parse(text), Opt, name));

    /// <summary>A named session must resolve its score the way the full path does —
    /// by name, by output file, or (no match, e.g. a stale preview selection) the
    /// first score — and stay byte-identical to it across edits in EITHER score's
    /// music. Before finding 3-1 a named preview bypassed the session entirely, so
    /// this incremental==full net simply did not exist for names.</summary>
    [Theory]
    [InlineData("sub")]   // matches score sub by name
    [InlineData("y")]     // matches score sub by its output file
    [InlineData("nope")]  // matches nothing -> first score, like SvgGenerator.Generate
    public void NamedSession_ChainedEdits_EqualFullGenerate(string name)
    {
        var tree = SyntaxTree.Parse(TwoScores);
        var session = new IncrementalCompiler(tree, Opt, name);
        Assert.Equal(FullNamed(tree.Text, name), Norm(session.Render()));

        // An edit inside alto's music (the named score's own systems move)...
        tree = tree.WithChange(Replace(tree.Text, "b4 c d e", "b4 c d f"));
        Assert.Equal(FullNamed(tree.Text, name), Norm(session.RenderIncremental(tree)));

        // ...then one inside melody's music (the other score's).
        tree = tree.WithChange(Replace(tree.Text, "g4 a b c", "g4 a b d"));
        Assert.Equal(FullNamed(tree.Text, name), Norm(session.RenderIncremental(tree)));
    }

    /// <summary>The payoff a named preview used to be denied: an edit confined to the
    /// OTHER score's music leaves the named score's collected content unchanged, so the
    /// session must take the whole-layout reuse path — and still equal the full path.</summary>
    [Fact]
    public void NamedSession_EditConfinedToTheOtherScore_ReusesWholeLayout()
    {
        var tree = SyntaxTree.Parse(TwoScores);
        var session = new IncrementalCompiler(tree, Opt, "sub");
        session.Render();

        // "g4 a b c" lives in phrase mel, drawn only by score main; same length,
        // so alto's offsets do not move either.
        tree = tree.WithChange(Replace(tree.Text, "g4 a b c", "g4 a b d"));
        var svg = Norm(session.RenderIncremental(tree));

        Assert.True(session.LastEditReusedLayout,
            "an edit that leaves the named score's content unchanged must reuse the whole layout");
        Assert.Equal(FullNamed(tree.Text, "sub"), svg);
    }

    /// <summary>THE SPEC-IDENTITY GUARD, on the named side. Inserting ANOTHER block
    /// named sub — with a transpose — above the one the session has been rendering
    /// drifts resolution (first match wins) to the inserted block, while every guard
    /// keyed on content stays green: the music above is an untouched common prefix,
    /// the baseline's own render blocks are value-stable, and no walk-entry check
    /// reads the transpose. Without the guard the resume adopts every measure at
    /// concert pitch under the new name (verified red while building this test);
    /// the guard sheds the session cold instead.</summary>
    /// <summary>A book whose music is written directly in the section cell — the
    /// shape whose measure boundaries the collect resume actually checkpoints and
    /// adopts (a phrase-reference cell is one item and adopts nothing), which is
    /// what makes the spec drift below DANGEROUS rather than merely cold.</summary>
    private static string DriftBook() =>
        "octave absolute\npart m { clef treble }\nsection S {\n  m {\n    "
        + string.Join(" |\n    ", Enumerable.Repeat("c'4 d'4 e'4 f'4", 60))
        + " |\n  }\n}\nform main { S }\nform sub { S }\n"
        + "score main \"x\" { staff m }\nscore sub \"y\" { staff m }\n";

    [Fact]
    public void NamedSession_SameNamedBlockInsertedAbove_StaysEqualToFull()
    {
        var tree = SyntaxTree.Parse(DriftBook());
        var session = new IncrementalCompiler(tree, Opt, "sub");
        session.Render();

        // Inserted BEFORE `form main`, where the old text continues with an `f`:
        // the common prefix stops right there instead of running into a baseline
        // render block's header, the dirty window is empty, and every baseline
        // block sits value-stable in the shifted suffix — so the resume planner
        // PROCEEDS and the adopted measures are the stale spec's.
        int at = tree.Text.IndexOf("form main", System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(at, 0),
            "score sub transpose d { staff m }\n"));
        Assert.Equal(FullNamed(tree.Text, "sub"), Norm(session.RenderIncremental(tree)));
    }

    /// <summary>Finding 4-4 liveness + value: the note-bound loose-line block between two
    /// staves is memoized per (system, upper staff) across keystrokes
    /// (SystemLayoutCache.GetOrComputeLooseLines). An edit confined to a LATER system
    /// leaves the first system's slice key unchanged, so its block must be SERVED — and
    /// the served value must render byte-identical to a full recompile (a poison that
    /// drops the served value to null turns the equality red; verified while building
    /// this test).</summary>
    [Fact]
    public void LooseLinesMemo_LaterSystemEdit_ServesTheEarlierBlock()
    {
        string bars(string cell) => string.Join(" | ", Enumerable.Repeat(cell, 12));
        string source =
            "part melody { section A { " + bars("c4 d e f") + " } }\n"
            + "part back { section A { " + bars("e4 f g a") + " } }\n"
            + "lyrics ly sings melody { section A { " + bars("la le li lo") + " } }\n"
            + "form main { A }\n"
            + "score main {\n  staff melody  lyrics ly\n  staff back\n}\n";

        var tree = SyntaxTree.Parse(source);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();
        Assert.True(session.SystemCache!.LooseLinesStats.Misses > 0,
            "the fixture must exercise the loose-line block at all");

        // A same-duration pitch edit in the LAST bar: the tail system's content moves,
        // the first system's slice does not, and no spring width changes so the break
        // solution holds and the memo is asked with the same key.
        int at = source.LastIndexOf("c4 d e f", System.StringComparison.Ordinal);
        var change = new TextChange(new TextSpan(at, 1), "d");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.SystemCache!.LooseLinesStats.Hits > 0,
            "the earlier system's loose-line block was recomputed rather than served");
        Assert.Equal(Full(source.Remove(at, 1).Insert(at, "d")), incremental);
    }

    /// <summary>Finding 4-3 liveness + value: the below-staff stacking pass (dynamics at
    /// 250, the fermata family at 75, down trills at 50) replays unchanged systems from
    /// BelowStackMemo — the below-side mirror of the above pass's memo. An edit confined
    /// to a later system must SERVE the earlier system's below stack, byte-identical to a
    /// full recompile (a poison that replays a nudged output turns the equality red;
    /// verified while building this test).</summary>
    [Fact]
    public void BelowStackMemo_LaterSystemEdit_ServesTheEarlierSystem()
    {
        string bars(string cell) => string.Join(" | ", Enumerable.Repeat(cell, 12));
        string source =
            "part m { clef treble }\n"
            + "section S { m { " + bars("c''4@mf d''4 e''4@fermata f''4") + " } }\n"
            + "form main { S }\n"
            + "score main { staff m }\n";

        var tree = SyntaxTree.Parse(source);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();
        Assert.True(session.SystemCache!.FinalBelowStack.Misses > 0,
            "the fixture must exercise the below stack at all");

        int at = source.LastIndexOf("c''4@mf", System.StringComparison.Ordinal);
        var change = new TextChange(new TextSpan(at, 1), "d");
        var incremental = Norm(session.Edit(change));

        Assert.True(session.SystemCache!.FinalBelowStack.Hits > 0,
            "the earlier system's below stack was rebuilt rather than served");
        Assert.Equal(Full(source.Remove(at, 1).Insert(at, "d")), incremental);
    }

    /// <summary>The same drift on the DEFAULT side, which existed before named
    /// sessions did: a new first block inserted between the music and the old first
    /// block moves FindFirst's answer without touching any baseline render block.</summary>
    [Fact]
    public void DefaultSession_RenderBlockInsertedAboveTheFirst_StaysEqualToFull()
    {
        var tree = SyntaxTree.Parse(DriftBook());
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        int at = tree.Text.IndexOf("form main", System.StringComparison.Ordinal);
        tree = tree.WithChange(new TextChange(new TextSpan(at, 0),
            "score main transpose d { staff m }\n"));
        Assert.Equal(Full(tree.Text), Norm(session.RenderIncremental(tree)));
    }
}
