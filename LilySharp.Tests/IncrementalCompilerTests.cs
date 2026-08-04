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

using LilySharp.Core.Svg;
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
        phrase p { c4 d e f | g4 a b c | d4 e f g | a4 b c d | }
        section Main { melody { $p } }
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

    private static string ApplyFirst(string text, string find, string rep)
    {
        int at = text.IndexOf(find, System.StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return text[..at] + rep + text[(at + find.Length)..];
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
                staff sop "Soprano" with lyrics verse
                staff alt "Alto" with lyrics verse
                staff ten "Tenor" with lyrics verse
                staff bas "Bass" with lyrics verse
              }
            }
            """;
        const string line = "    staff bas \"Bass\" with lyrics verse";
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
}
