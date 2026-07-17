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
using System.Linq;
using System.Reflection;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Soundness net for the F3 incremental reuse engine, complementing the
/// hand-authored single-edit cases in <see cref="IncrementalCompilerTests"/>:
///
/// 1. SESSION FUZZ — drives <see cref="IncrementalCompiler.Edit"/> (the real
///    cache/reuse path) over long chains of randomized edits and asserts each
///    step stays byte-identical to a full recompile. This is the differential
///    that catches a MISSED dependency edge — the failure mode the design doc
///    flags as un-spottable by eye — which the parser-level fuzz in
///    <see cref="IncrementalRenderEquivalenceTests"/> cannot reach (it renders
///    both sides fully and never exercises reuse).
///
/// 2. NEGATIVE / DECLINE cases — the soundness-critical "reuse must NOT fire"
///    paths: polyphony and grob overrides (gated off wholesale) and the
///    hand-maintained score-global key fields (composer, tempo).
///
/// 3. DRIFT canary — a reflection guard so a newly added stateful Staff field
///    cannot silently drift out of the content key (a false whole-layout reuse
///    would render stale geometry).
/// </summary>
[Trait("Category", "Visual")]
public class IncrementalReuseSoundnessTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    private static string Norm(string svg) => svg.Replace("\r\n", "\n");

    private static TextChange Replace(string text, string find, string replacement)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(at >= 0, $"snippet not found: {find}");
        return new TextChange(new TextSpan(at, find.Length), replacement);
    }

    // --- seeds spanning data-pos annotations, beams, header, and multi-staff ---

    private const string Plain = """
        time 4/4
        key c major
        part melody { clef treble }
        phrase p { c4 d e f | g4 a b c | d4 e f g | a4 b c d | }
        section Main { melody { $p } }
        form main { Main }
        score main "x" { staff melody }
        """;

    private const string Header = """
        tempo 90
        title "Song"
        composer "Me"
        time 4/4
        key c major
        part melody { clef treble }
        phrase p { c8 d e f g a b c | g4 a b c | }
        section Main { melody { $p } }
        form main { Main }
        score main "x" { staff melody }
        """;

    private const string Lyrics = """
        time 4/4
        key c major
        part melody
        section Main {
          melody { c4 d e f | g4 a b c | }
          lyrics { Twin- kle lit- tle | star how I you | }
        }
        form main { Main }
        score main "x" { staff melody }
        """;

    private const string GrandStaff = """
        time 4/4
        key c major
        part rh "Violin" { clef treble }
        part lh "Cello" { clef bass }
        section Main { rh { c4 d e f | g4 a b c | } lh { c4 d e f | g4 a b c | } }
        form main { Main }
        score main "x" { grandStaff { staff rh staff lh } }
        """;

    // ---------------------------------------------------------------------
    // 1. SESSION FUZZ: reuse path == full recompile, every step.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(Plain)]
    [InlineData(Header)]
    [InlineData(Lyrics)]
    [InlineData(GrandStaff)]
    public void SessionFuzz_RandomizedEdits_AlwaysMatchFull(string seed)
    {
        // Deterministic per-seed RNG so a failure reproduces exactly.
        var rng = new Random(20260702 + seed.Length);
        string[] snippets =
        {
            "c4 ", "d8 e ", "|", "", " ", "r4 ", "g'2 ", "\n", "<c e>4 ", ".",
            "@staccato ", "@finger(1) ", "f4 g a b ", "e", "cresc ", "// x\n",
        };

        var session = new IncrementalCompiler(SyntaxTree.Parse(seed), Opt);
        session.Render(); // warm the cache

        for (int i = 0; i < 60; i++)
        {
            // session.Tree is the single source of truth: on an edit whose compile
            // throws, the session tree does not advance, so reading it back keeps the
            // mirror text in lockstep without special-casing exceptions.
            string current = session.Tree.Text;
            int start = rng.Next(current.Length + 1);
            int length = Math.Min(rng.Next(6), current.Length - start);
            string replacement = snippets[rng.Next(snippets.Length)];
            var change = new TextChange(new TextSpan(start, length), replacement);
            string editedText = current[..start] + replacement + current[(start + length)..];

            // Compare OUTCOMES including exceptions: a malformed edit must fail the
            // same way on both sides rather than making the net flaky. A genuine reuse
            // bug shows up as either differing SVG or a one-sided throw.
            string? inc = null, full = null;
            Exception? incEx = null, fullEx = null;
            try { inc = Norm(session.Edit(change)); } catch (Exception e) { incEx = e; }
            try { full = Full(editedText); } catch (Exception e) { fullEx = e; }

            Assert.Equal(fullEx?.GetType(), incEx?.GetType());
            if (fullEx == null)
                Assert.Equal(full, inc);
        }
    }

    // ---------------------------------------------------------------------
    // 2. DECLINE cases: reuse must NOT fire, output must still match full.
    // ---------------------------------------------------------------------

    private const string Poly = """
        time 4/4
        key c major
        part melody
        phrase p { voice { c'2 d | e2 f | } voice { a2 g | b2 a | } }
        section Main { melody { $p } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void SecondaryVoice_ContentUnchangedEdit_DoesNotReuse_ButMatchesFull()
    {
        // A leading newline changes no content, so on a SINGLE-voice score whole-layout
        // reuse would fire. Here the score is polyphonic: the primary-voice-only content
        // key/spring gate cannot see voice 2, so reuse MUST be declined wholesale
        // (HasSecondaryVoices gate) — else a later voice-2 edit would render stale.
        var tree = SyntaxTree.Parse(Poly);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var inc = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), inc);
    }

    [Fact]
    public void SecondaryVoice_Voice2Edit_MatchesFull()
    {
        // Editing ONLY the second voice must still match a full recompile (this is the
        // exact staleness the HasSecondaryVoices gate exists to prevent).
        var tree = SyntaxTree.Parse(Poly);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(Poly, "b2 a", "b2 g");
        var inc = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), inc);
    }

    private const string Overridden = """
        time 4/4
        key c major
        part melody
        phrase p { override Stem.length = 10 c4 d e f | revert Stem.length g4 a b c | }
        section Main { melody { $p } }
        form main { Main }
        score main "x" { staff melody }
        """;

    [Fact]
    public void GrobOverride_ContentUnchangedEdit_DoesNotReuse_ButMatchesFull()
    {
        // Overrides spread spacing globally and are not localized by the per-measure
        // key, so an override-bearing score is gated out of every reuse path. A
        // content-unchanged edit must decline reuse yet stay byte-identical.
        var tree = SyntaxTree.Parse(Overridden);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = new TextChange(new TextSpan(0, 0), "\n");
        var inc = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), inc);
    }

    [Fact]
    public void ComposerEdit_DoesNotReuse_ButMatchesFull()
    {
        // Composer is a score-global rendered field (header), not in any per-measure
        // key — it is caught only by the hand-maintained global key. Guards that entry.
        var tree = SyntaxTree.Parse(Header);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(Header, "composer \"Me\"", "composer \"You\"");
        var inc = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), inc);
    }

    [Fact]
    public void TempoEdit_DoesNotReuse_ButMatchesFull()
    {
        // Tempo drives the synthesized tempo mark; it is in the global key. Guards it.
        var tree = SyntaxTree.Parse(Header);
        var session = new IncrementalCompiler(tree, Opt);
        session.Render();

        var change = Replace(Header, "tempo 90", "tempo 96");
        var inc = Norm(session.Edit(change));

        Assert.False(session.LastEditReusedLayout);
        Assert.Equal(Full(tree.WithChange(change).Text), inc);
    }

    [Fact]
    public void OverrideToggle_EligibleThenIneligibleThenEligible_StaysByteIdentical()
    {
        // The per-system cache is RETAINED across a transient ineligible (override-
        // bearing) edit rather than dropped, so a later eligible edit can still reuse
        // unchanged systems. This exercises that transition end-to-end: the cache must
        // never be CONSULTED with stale keys while ineligible, so every step stays
        // byte-identical to a full recompile.
        var session = new IncrementalCompiler(SyntaxTree.Parse(Plain), Opt);
        session.Render();

        // eligible -> ineligible: add an override.
        var add = Replace(Plain, "c4 d e f", "override Stem.length = 10 c4 d e f");
        var incAdd = Norm(session.Edit(add));
        Assert.Equal(Full(session.Tree.Text), incAdd);

        // ineligible -> eligible: remove the override again.
        var remove = Replace(session.Tree.Text, "override Stem.length = 10 ", "");
        var incRemove = Norm(session.Edit(remove));
        Assert.Equal(Full(session.Tree.Text), incRemove);
    }

    // ---------------------------------------------------------------------
    // 3. DRIFT canary: every stateful Staff field must be accounted for.
    // ---------------------------------------------------------------------

    [Fact]
    public void StaffIdentity_AccountsForEveryStatefulStaffField()
    {
        // MeasureContentKey.AddStaffIdentity folds the per-staff identity fields; Clef
        // (via entry context) and Voices (via intrinsic measures) are captured elsewhere.
        // If a NEW stateful Staff field appears and is folded into NONE of these, a
        // content-unchanged edit could trip a false whole-layout reuse and render stale
        // geometry. This canary fails until the new field is triaged into the key.
        var foldedByStaffIdentity = new[]
        {
            nameof(Staff.Tuning), nameof(Staff.InstrumentName), nameof(Staff.IsOssia),
            nameof(Staff.RemoveEmpty), nameof(Staff.RemoveFirst), nameof(Staff.StaffAffinity),
            nameof(Staff.PerStaffKeySignature), nameof(Staff.IsTextRow), nameof(Staff.TextRowVerses),
            nameof(Staff.IsLyricsTextRow),
        nameof(Staff.TabSourceClef), nameof(Staff.Transposition), nameof(Staff.TabNumbersOnly),
        nameof(Staff.Lines),
        };
        var capturedElsewhere = new[] { nameof(Staff.Clef), nameof(Staff.Voices) };
        var accounted = foldedByStaffIdentity.Concat(capturedElsewhere).ToHashSet(StringComparer.Ordinal);

        // Stateful == init/set-settable positional record members; computed getters
        // (PrimaryVoice, IsMultiVoice, MeasureCount, IsTab) are get-only and derived.
        var stateful = typeof(Staff)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();

        var missing = stateful.Where(n => !accounted.Contains(n)).ToList();
        Assert.True(missing.Count == 0,
            "New stateful Staff field(s) not folded into MeasureContentKey.AddStaffIdentity "
            + "(nor captured via Clef/Voices): " + string.Join(", ", missing)
            + ". A false whole-layout reuse could render this field stale — fold it into the "
            + "content key or add it to the captured-elsewhere allow-list here.");
    }
}
