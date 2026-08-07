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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds every <c>LILYPOND-REF:</c> citation against the LilyPond tree it names.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. <see cref="LpProvenanceTests"/> asserts that a constant CARRIES a
/// citation; nothing asserted that the citation was TRUE. HANDOFF 5.2 has said "a
/// LILYPOND-REF does not mean the formula matches" in prose for many sessions, and on
/// 2026-07-28 that prose failed to stop the obvious version of the mistake: a port cited
/// <c>align-interface.cc:240-267</c> for a claim about the alignment's general loop, when
/// :240-267 is the <c>include_fixed_spacing</c> branch — and the same file already cited
/// that exact range correctly, for the other claim, twenty lines further down. Every
/// existing net was green: the output was right, the ledger was right, the snapshots were
/// right. A wrong citation is invisible to all of them, and it is the one artefact the
/// whole "port the source, not the output" method rests on.
/// </para>
/// <para>
/// ⚠️ THE LOAD-BEARING TEST IS <see cref="CitationsThatNameNothing_DoNotGrow"/>, AND IT
/// NEEDS NO LILYPOND TREE. It is a ratchet on citations that give a line range and no
/// symbol, and it is what would actually have caught the 2026-07-28 error: that comment
/// named no symbol, so the count would have risen and the test would have failed. Being
/// forced to name something at :240-267 means reading :240-267, where the only names are
/// <c>include_fixed_spacing</c>, <c>is_spaceable</c> and <c>get_fixed_spacing</c> — which
/// is the range's own answer to "is this the line you mean?". That is HANDOFF 5.2.1①'s
/// mechanism ("trying to write the source down is what shows you where the invention is")
/// applied one level in: trying to name the symbol is what shows you the range is wrong.
/// </para>
/// <para>
/// ⚠️ WHAT IS DELIBERATELY NOT CHECKED: that the cited lines SAY what the comment claims.
/// No machine can do that, and a checker that pretended to would be the guessing helper
/// HANDOFF 5.4 warns about. These tests verify the address, not the argument.
/// </para>
/// <para>
/// ⚠️ NAMING SOMETHING IS NOT THE SAME AS NAMING IT AT THE RIGHT LINE, and for three
/// sessions nothing said so. <see cref="CitationRangesHoldTheirNamedSymbol"/> closes the
/// half of the 2026-07-28 lesson that the ratchet cannot reach: a range that has drifted
/// WITHIN its own file still names the right symbol in the right file, so it satisfies
/// every test above it. HANDOFF's own self-audit found one function whose three citations
/// were each about 38 lines off, and observed that "the ratchet cannot catch this in
/// principle" — this is the check that can.
/// </para>
/// </remarks>
public sealed class LpReferenceCitationTests
{
    private readonly ITestOutputHelper _out;

    public LpReferenceCitationTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Citations that carry a line range but name no symbol next to it. May only go DOWN.
    /// </summary>
    /// <remarks>
    /// The same ratchet <see cref="LpProvenanceTests"/> uses for unsourced constants: a
    /// number that records a debt, lowered on purpose so the improvement lands in a diff.
    /// ⚠️ Lowering it is the point; raising it to make a build pass is the one forbidden
    /// move (HANDOFF 5.2.1①).
    /// <para>
    /// ⚠️ CONTINUATION ADDRESSES DO NOT COUNT HERE, and the reason is the ratchet's own
    /// mechanism rather than convenience. What this number buys is that writing the name
    /// makes someone READ the lines; a comment that says
    /// <c>engraver-init.ly:649-652 and :653-657 — the Lyrics context's two …</c> has already
    /// paid that, once, for both ranges. Counting the second would have added 51 at a stroke
    /// for comments that name their subject perfectly well — and the names it wants there
    /// are <c>TabVoice</c> and <c>Lyrics</c>, single words that
    /// <see cref="LooksLikeLilyPondSymbol"/> cannot claim by construction. The ADDRESS checks
    /// take them all; only the naming ratchet lets them past.
    /// </para>
    /// </remarks>
    private const int UnnamedCitationBaseline = 742;

    /// <summary>
    /// (symbol, LilyPond file) pairs a citation names that the file does not contain.
    /// </summary>
    /// <remarks>
    /// Each is a citation whose address may be wrong, or a name Lily# spells differently
    /// from LilyPond. They are listed rather than counted so that a NEW one fails
    /// immediately even while an old one is still open — a count would let the two cancel.
    /// ⚠️ ALSO ASSERTED IN REVERSE: an entry that stops missing must be REMOVED, so the
    /// improvement appears in the diff. That is the lp-geometry ledger's own rule ("a
    /// residual that shrinks is progress that must be recorded on purpose") applied here.
    /// </remarks>
    private static readonly HashSet<string> KnownUnverifiedSymbols = new()
    {
        "break_substitute|lily/break-substitution.cc",
        "church_rest|lily/rest.cc",
        "get_encompass_infos|lily/slur-scoring.cc",
        "key_signature|lily/key-engraver.cc",
        "listen_glissando|scm/scheme-engravers.scm",
        "calc_springs|lily/grace-spacing-engraver.cc",
        "footnote_height|lily/page-layout-problem.cc",
        "get_break_align_spacing|lily/break-alignment-interface.cc",
        "internal_print|lily/flag.cc",
        "Line_details|lily/simple-spacer.cc",
        "max_pages|lily/page-spacing.cc",
        "min_pages|lily/page-spacing.cc",
        "Slur_scoring|lily/slur-scoring.cc",
        "strict_note_spacing|lily/note-spacing.cc",
        "Tuplet_bracket_interface|lily/tuplet-bracket.cc",
    };

    /// <summary>A cited LilyPond location, and where in Lily# it was cited from.</summary>
    /// <remarks>
    /// ⚠️ THE TWO SYMBOL SETS ARE NOT THE SAME SET, for the reason
    /// <see cref="IsVerifiableSymbol"/> gives: the ratchet wants recall and the address
    /// checks want precision. <paramref name="Symbols"/> is everything named after the
    /// address, so a comment counts as "naming something" as generously as possible.
    /// <paramref name="OwnSymbols"/> stops at the NEXT address on the line, because
    /// <c>accidental.cc:33-43 parenthesize; :45-84 horizontal_skylines</c> is one comment
    /// carrying two correct citations, and lending the second one's name to the first
    /// invents a defect in a line that has none.
    /// </remarks>
    private sealed record Citation(
        string SourceFile, int SourceLine, string LpPath, int From, int To,
        IReadOnlyList<string> Symbols, IReadOnlyList<string> OwnSymbols,
        bool IsContinuation);

    /// <summary>
    /// A LilyPond path, optionally with a line or line range: <c>lily/foo.cc:12</c>,
    /// <c>scm/bar.scm:12-34</c>.
    /// </summary>
    private static readonly Regex CitationPattern = new(
        @"((?:lily|scm|ly|mf|python|flower)/[\w./-]+\.(?:cc|hh|scm|ly|mf|py))(?::(\d+)(?:-(\d+))?)?",
        RegexOptions.Compiled);

    /// <summary>
    /// A LilyPond identifier: a multi-part name joined by '_' or '-'.
    /// </summary>
    /// <remarks>
    /// Both spellings are needed and neither is optional. LilyPond's C++ is snake_case
    /// (<c>internal_get_minimum_translations</c>, <c>Align_interface</c>) and its Scheme —
    /// which is where <c>define-bar-line</c>, <c>tabvoice::make-double-stem-width-for-half-notes</c>
    /// and every grob property live — is hyphenated. Taking only one of them would let half
    /// the corpus cite without naming, which is the very hole this class exists to close.
    /// </remarks>
    private static readonly Regex SymbolPattern = new(
        @"\b[A-Za-z][A-Za-z0-9]*(?:[_-][A-Za-z0-9]+)+\b", RegexOptions.Compiled);

    /// <summary>
    /// Decides whether a token is plausibly a LilyPond name rather than English or C#.
    /// </summary>
    /// <remarks>
    /// Two rejections, both measured against this corpus rather than guessed:
    /// <list type="bullet">
    /// <item>C# TEST NAMES are <c>Pascal_Pascal_Pascal</c>; LilyPond capitalises only the
    /// first segment, so a capital after the first join is decisive.</item>
    /// <item>HYPHENATED ENGLISH is everywhere in these comments ("break-align column",
    /// "line-breaking"), so a hyphenated token needs THREE parts to count —
    /// <c>staff-staff-spacing</c> and <c>define-bar-line</c> clear it, "line-breaking" does
    /// not. Underscored tokens need only two, because English does not use underscores.</item>
    /// </list>
    /// </remarks>
    private static bool LooksLikeLilyPondSymbol(string token)
    {
        if (token.Length < 8)
            return false;
        var segments = token.Split('_', '-');
        for (int i = 1; i < segments.Length; i++)
            if (segments[i].Length > 0 && char.IsUpper(segments[i][0]))
                return false;
        return token.Contains('_') || segments.Length >= 3;
    }

    /// <summary>
    /// Whether a named token is precise enough to CLAIM as missing from a file.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE TWO CHECKS WANT OPPOSITE PRECISION, so they do not share a predicate.
    /// <list type="bullet">
    /// <item>The RATCHET wants recall: any identifiable name will do, and a false positive
    /// only means a comment counts as "naming something", which costs nothing.</item>
    /// <item>The EXISTENCE check wants precision: a false positive is a failing build over
    /// a citation that is perfectly correct.</item>
    /// </list>
    /// Measured on this corpus, hyphenated three-part tokens are as often English as
    /// LilyPond — <c>end-to-end</c>, <c>staff-affinity-aware</c>, <c>if-no-beam</c> all
    /// appear beside citations — so only UNDERSCORED tokens are claimed. That gives up
    /// verifying Scheme names, and says so rather than pretending; closing it wants a real
    /// symbol index of the LilyPond tree, not a sharper regex.
    /// </remarks>
    private static bool IsVerifiableSymbol(string token) => token.Contains('_');

    /// <summary>
    /// Every address on a line: a path with an optional range, or a bare <c>:45-84</c> that
    /// continues the path of the address before it.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE CONTINUATION IS A REAL CITATION, not punctuation.
    /// <c>accidental.cc:33-43 parenthesize; :45-84 horizontal_skylines</c> makes two claims
    /// about two ranges, and reading the second as decoration was wrong twice over: it left
    /// 62 addresses unchecked by every test in this class, AND it let the first citation
    /// claim the second's name, which invented a defect in a comment that had none.
    /// A bare range before any path names no file and is dropped.
    /// </remarks>
    private static readonly Regex AddressPattern = new(
        @"(?<path>(?:lily|scm|ly|mf|python|flower)/[\w./-]+\.(?:cc|hh|scm|ly|mf|py))"
        + @"(?::(?<from>\d+)(?:-(?<to>\d+))?)?"
        + @"|:(?<from2>\d+)(?:-(?<to2>\d+))?",
        RegexOptions.Compiled);

    /// <summary>The LilyPond names in a fragment of comment text.</summary>
    private static IReadOnlyList<string> Named(string text) =>
        SymbolPattern.Matches(text)
            .Select(s => s.Value)
            .Where(LooksLikeLilyPondSymbol)
            .Distinct()
            .ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LilySharp.slnx"))
                               && !Directory.Exists(Path.Combine(dir.FullName, "LilySharp.Core")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException("could not find the repository root.");
    }

    /// <summary>
    /// The LilyPond source tree, or null when this machine has none.
    /// </summary>
    /// <remarks>
    /// ⚠️ The path is an ENVIRONMENT fact, not a repository one, so it is looked up rather
    /// than hard-coded to one developer's disk: <c>LILYSHARP_LILYPOND_SRC</c> first, then
    /// the working checkout beside the repository, then the documented default.
    /// </remarks>
    private static string? LilyPondSource()
    {
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("LILYSHARP_LILYPOND_SRC"),
            Path.Combine(Path.GetDirectoryName(RepoRoot()) ?? ".", "lilypond-src"),
            @"C:\MyProj\lilypond-src",
        };
        return candidates.FirstOrDefault(
            c => !string.IsNullOrWhiteSpace(c) && Directory.Exists(Path.Combine(c!, "lily")));
    }

    private static IReadOnlyList<Citation> AllCitations()
    {
        var root = RepoRoot();
        var sources = new[] { "LilySharp.Core", "LilySharp.Tests" }
            .Select(p => Path.Combine(root, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        var found = new List<Citation>();
        foreach (var file in sources)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("LILYPOND-REF"))
                    continue;

                var addresses = AddressPattern.Matches(lines[i]);
                string? carried = null;
                for (int a = 0; a < addresses.Count; a++)
                {
                    var m = addresses[a];
                    string? path = m.Groups["path"].Success ? m.Groups["path"].Value : carried;
                    if (path is null)
                        continue;   // a bare ":12" before any path names no file.
                    carried = path;

                    var fromGroup = m.Groups["path"].Success ? m.Groups["from"] : m.Groups["from2"];
                    var toGroup = m.Groups["path"].Success ? m.Groups["to"] : m.Groups["to2"];
                    int from = fromGroup.Success ? int.Parse(fromGroup.Value) : 0;
                    int to = toGroup.Success ? int.Parse(toGroup.Value) : from;

                    // Only the text AFTER the address counts as naming it — a symbol on the
                    // line before belongs to whatever that line cited.
                    int tailStart = m.Index + m.Length;
                    // ...and only up to the NEXT address, when the line carries several.
                    int ownEnd = a + 1 < addresses.Count ? addresses[a + 1].Index : lines[i].Length;

                    found.Add(new Citation(
                        Path.GetRelativePath(root, file), i + 1, path, from, to,
                        Named(lines[i][tailStart..]), Named(lines[i][tailStart..ownEnd]),
                        IsContinuation: !m.Groups["path"].Success));
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Every citation that gives a line range should say what is AT it. Ratchet.
    /// </summary>
    [Fact]
    public void CitationsThatNameNothing_DoNotGrow()
    {
        var ranged = AllCitations().Where(c => c.From > 0 && !c.IsContinuation).ToList();
        var unnamed = ranged.Where(c => c.Symbols.Count == 0).ToList();

        _out.WriteLine($"{ranged.Count} citations carry a line range; "
                       + $"{ranged.Count - unnamed.Count} name a symbol, {unnamed.Count} do not.");

        Assert.True(
            unnamed.Count <= UnnamedCitationBaseline,
            $"citations with a line range but no symbol rose to {unnamed.Count} from the "
            + $"baseline {UnnamedCitationBaseline}. Name what is at the lines you cited — "
            + "reading them to find the name is the check. Do NOT raise the baseline.\n"
            + string.Join("\n", unnamed
                .OrderBy(c => c.SourceFile).ThenBy(c => c.SourceLine)
                .TakeLast(20)
                .Select(c => $"  {c.SourceFile}:{c.SourceLine} -> {c.LpPath}:{c.From}")));
    }

    /// <summary>Every cited LilyPond file must exist in the tree.</summary>
    [Fact]
    public void EveryCitedLilyPondFileExists()
    {
        string? lp = LilyPondSource();
        if (lp is null)
        {
            _out.WriteLine("NO LILYPOND TREE ON THIS MACHINE — set LILYSHARP_LILYPOND_SRC to "
                           + "check citation addresses. The ratchet test above still ran.");
            return;
        }

        var missing = AllCitations()
            .Where(c => !File.Exists(Path.Combine(lp, c.LpPath.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();

        Assert.True(missing.Count == 0,
            "these citations name a file LilyPond does not have:\n"
            + string.Join("\n", missing.Select(
                c => $"  {c.SourceFile}:{c.SourceLine} -> {c.LpPath}")));
    }

    /// <summary>Every cited line must be inside the file it names.</summary>
    /// <remarks>
    /// Weak today (nothing fails) and kept anyway: it is the guard for the day the pinned
    /// LilyPond moves. HANDOFF 3 pins the truth at 2.26.0, and a version bump renumbers
    /// every file at once — this says so instead of leaving 1000 citations quietly stale.
    /// </remarks>
    [Fact]
    public void EveryCitedLineRangeIsInsideItsFile()
    {
        string? lp = LilyPondSource();
        if (lp is null)
        {
            _out.WriteLine("NO LILYPOND TREE ON THIS MACHINE — line ranges unchecked.");
            return;
        }

        var lengths = new Dictionary<string, int>();
        var bad = new List<string>();
        foreach (var c in AllCitations().Where(c => c.From > 0))
        {
            string path = Path.Combine(lp, c.LpPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;
            if (!lengths.TryGetValue(c.LpPath, out int length))
                lengths[c.LpPath] = length = File.ReadAllLines(path).Length;
            if (c.To > length)
                bad.Add($"  {c.SourceFile}:{c.SourceLine} -> {c.LpPath}:{c.From}-{c.To} "
                        + $"(the file has {length} lines)");
        }

        Assert.True(bad.Count == 0,
            "these citations point past the end of the file they name:\n"
            + string.Join("\n", bad));
    }

    /// <summary>
    /// A symbol named next to a citation must occur in the file the citation names.
    /// </summary>
    /// <remarks>
    /// The FILE rather than the range: LilyPond declares a member in its header and defines
    /// it lower down, and a comment reasonably cites the definition while naming the class.
    /// Requiring the name inside the range would fail on correct citations, which is the
    /// helper-that-guesses failure mode. '-' is folded to '_' so a Scheme property written
    /// <c>strict_note_spacing</c> in prose matches <c>strict-note-spacing</c> in the source.
    /// </remarks>
    [Fact]
    public void EveryNamedSymbolOccursInItsCitedFile()
    {
        string? lp = LilyPondSource();
        if (lp is null)
        {
            _out.WriteLine("NO LILYPOND TREE ON THIS MACHINE — symbols unchecked.");
            return;
        }

        var text = new Dictionary<string, string>();
        var misses = new SortedSet<string>();
        var where = new Dictionary<string, string>();
        foreach (var c in AllCitations().Where(c => c.Symbols.Count > 0))
        {
            string path = Path.Combine(lp, c.LpPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;
            if (!text.TryGetValue(c.LpPath, out var body))
                text[c.LpPath] = body = File.ReadAllText(path).Replace('-', '_');
            foreach (var s in c.Symbols)
            {
                if (!IsVerifiableSymbol(s))
                    continue;
                if (body.Contains(s.Replace('-', '_'), StringComparison.Ordinal))
                    continue;
                string key = $"{s}|{c.LpPath}";
                misses.Add(key);
                where.TryAdd(key, $"{c.SourceFile}:{c.SourceLine}");
            }
        }

        var appeared = misses.Where(k => !KnownUnverifiedSymbols.Contains(k)).ToList();
        Assert.True(appeared.Count == 0,
            "these citations name something their file does not contain:\n"
            + string.Join("\n", appeared.Select(k => $"  {where[k]} -> {k}")));

        var fixedUp = KnownUnverifiedSymbols.Where(k => !misses.Contains(k)).ToList();
        Assert.True(fixedUp.Count == 0,
            "these are no longer unverified — remove them from KnownUnverifiedSymbols so the "
            + "improvement shows up in the diff:\n"
            + string.Join("\n", fixedUp.Select(k => "  " + k)));
    }

    // ------------------------------------------------------------------
    // The RANGE, not just the file. See CitationRangesHoldTheirNamedSymbol.
    // ------------------------------------------------------------------

    /// <summary>
    /// Citations whose line range does not sit in the definition they name. May only go DOWN.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is 0, and the six it opened at are corrected rather than carried. They are kept
    /// here because the next drifted range will look exactly like one of them, and because
    /// a checker's first catch is the only evidence that it catches anything. Each was read
    /// in the pinned 2.26.0 tree before being counted — the instrument was wrong about
    /// twenty others first, and every one of those was a defect in the instrument rather
    /// than in a comment (HANDOFF 5.0):
    /// </para>
    /// <list type="bullet">
    /// <item><c>axis-group-interface.cc:112-136</c> cited twice for
    /// <c>generic_group_extent</c>, which is at <c>:220-238</c>; <c>:112-136</c> is
    /// <c>generic_bound_extent</c>, its neighbour by name and not by behaviour.</item>
    /// <item><c>beam.cc:1451-1459</c> for <c>Beam::is_cross_staff</c>, which is at
    /// <c>:1496-1507</c>; <c>:1451-1459</c> is inside
    /// <c>pure_rest_collision_callback</c>.</item>
    /// <item><c>stem.cc:1168-1179</c> for <c>Stem::is_cross_staff</c>, which is at
    /// <c>:1279-1283</c>; <c>:1168-1179</c> is the beamed ideal length inside
    /// <c>calc_stem_info</c>. ⚠️ THIS ONE WAS INVISIBLE UNTIL THE SCANNER LEARNED RAW
    /// STRINGS — it sits 880 lines past the point where <c>stem.cc</c> stopped parsing, and
    /// it is the twin of the beam.cc line above, on the line below it in the same comment.
    /// A defect hiding one line from its own twin is what a silent instrument buys.</item>
    /// <item><c>page-layout-problem.cc:808-823</c> for <c>fixed_force_solution</c>, which is
    /// at <c>:1056-1061</c>; <c>:808-823</c> is the over-full-page compression inside
    /// <c>solve_rod_spring_problem</c>.</item>
    /// <item><c>beam.cc:773</c> for <c>calc_stem_y</c> — the ADDRESS is right for the
    /// <c>grow-direction</c> read it describes, but the reader there is <c>Beam::print</c>;
    /// the read that reaches <c>calc_stem_y</c> is <c>:1201</c>.</item>
    /// </list>
    /// ⚠️ Lowering it is the point; raising it to make a build pass is the one forbidden
    /// move (HANDOFF 5.2.1①). It went 5 → 6 once, when the scanner started reading files it
    /// had been skipping — debt that becomes VISIBLE is not debt that was added, and the
    /// distinction is only defensible because the six are enumerated here.
    /// </remarks>
    private const int StaleRangeBaseline = 0;

    /// <summary>A top-level definition in a LilyPond C++ file: its span and its text.</summary>
    /// <remarks>
    /// <paramref name="From"/> is the first line of the DECLARATOR, not of the body, so that
    /// a citation naming a function and pointing at its signature counts as inside it.
    /// <paramref name="Text"/> has '-' folded to '_', the same fold
    /// <see cref="EveryNamedSymbolOccursInItsCitedFile"/> applies, so a Scheme property
    /// written <c>collapse_height</c> in prose matches <c>collapse-height</c> in the source.
    /// </remarks>
    private sealed record LpDefinition(int From, int To, string Text);

    /// <summary>What the scanner carries from one line of a C++ file to the next.</summary>
    private struct CxxScan
    {
        public bool InBlockComment;

        /// <summary>The <c>)delim"</c> that ends the raw string we are inside, if any.</summary>
        public string? RawStringEnd;
    }

    /// <summary>Removes comments and literals so only real braces are counted.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ EVERY CASE HERE IS ONE THIS TREE ACTUALLY CONTAINS, and each was found by the
    /// instrument being wrong first (HANDOFF 5.0). Prose lives in <c>/* */</c> blocks between
    /// functions; <c>'{'</c> appears as a char literal.
    /// </para>
    /// <para>
    /// ⚠️⚠️ RAW STRINGS ARE THE ONE THAT COST A SESSION. <c>stem.cc:283-290</c> documents a
    /// callback with <c>R"(…@code{'(() . ())}…)"</c>: an unbalanced <c>{</c> and a lone
    /// apostrophe, inside a literal that spans six lines. Treating <c>R"(</c> as an ordinary
    /// quote left the scanner one brace deep for the remaining 1080 lines, so <c>stem.cc</c>
    /// yielded 11 definitions instead of ~90 — and the citations in the lost region did not
    /// FAIL, they became "file scope, nothing to check". A broken instrument that reports
    /// silence is worse than one that reports nonsense, which is why
    /// <see cref="CxxDefinitions"/> now returns whether the scan balanced and the caller
    /// refuses to draw conclusions from a file that did not.
    /// </para>
    /// <para>
    /// ⚠️ AN UNMATCHED QUOTE NO LONGER SWALLOWS THE LINE. A <c>'</c> or <c>"</c> with no
    /// partner before the newline is prose, not a literal — the closing brace after it is
    /// real, and consuming it is how the raw-string defect did its damage.
    /// </para>
    /// </remarks>
    private static string StripCxxCode(string line, ref CxxScan scan)
    {
        var kept = new StringBuilder(line.Length);
        int i = 0;

        if (scan.RawStringEnd is { } pending)
        {
            int close = line.IndexOf(pending, StringComparison.Ordinal);
            if (close < 0)
                return "";
            i = close + pending.Length;
            scan.RawStringEnd = null;
        }

        for (; i < line.Length; i++)
        {
            if (scan.InBlockComment)
            {
                if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    scan.InBlockComment = false;
                    i++;
                }
                continue;
            }
            char c = line[i];
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                break;
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
            {
                scan.InBlockComment = true;
                i++;
                continue;
            }
            if (c == 'R' && i + 1 < line.Length && line[i + 1] == '"'
                && (i == 0 || !char.IsLetterOrDigit(line[i - 1]) && line[i - 1] != '_'))
            {
                int open = line.IndexOf('(', i + 2);
                if (open >= 0)
                {
                    string end = ")" + line[(i + 2)..open] + "\"";
                    int close = line.IndexOf(end, open + 1, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        scan.RawStringEnd = end;
                        break;
                    }
                    i = close + end.Length - 1;
                    continue;
                }
            }
            if (c is '"' or '\'')
            {
                int j = i + 1;
                while (j < line.Length && line[j] != c)
                    j += line[j] == '\\' ? 2 : 1;
                if (j < line.Length)
                {
                    i = j;
                    continue;
                }
                // No partner on this line: prose, not a literal. Fall through and keep going.
                continue;
            }
            kept.Append(c);
        }
        return kept.ToString();
    }

    /// <summary>Splits a LilyPond C++ file into the definitions its braces delimit.</summary>
    /// <remarks>
    /// A definition is a block that opens at brace depth 0, which in LilyPond's C++ is a
    /// function body, a class body or a table initialiser. The declarator above it is found
    /// by walking back over unbroken non-blank lines — LilyPond puts the return type on its
    /// own line, so <c>Real \n Note_spacing::stem_dir_correction (…) \n {</c> is one header.
    /// ⚠️ BLANK LINES BEFORE THE BRACE ARE SKIPPED FIRST, because LilyPond's tree contains
    /// the declarator/blank/brace shape (<c>note-collision.cc:39-42</c>) and stopping at that
    /// blank drops the very line that carries the name.
    /// </remarks>
    private static (IReadOnlyList<LpDefinition> Defs, bool Balanced) CxxDefinitions(
        string[] lines)
    {
        var defs = new List<LpDefinition>();
        var scan = default(CxxScan);
        bool sane = true;
        int depth = 0, openLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (char c in StripCxxCode(lines[i], ref scan))
            {
                if (c == '{')
                {
                    if (depth == 0)
                        openLine = i;
                    depth++;
                }
                else if (c == '}')
                {
                    if (depth == 0)
                        sane = false;
                    depth = Math.Max(0, depth - 1);
                    if (depth == 0 && openLine >= 0)
                    {
                        int head = openLine;
                        while (head > 0 && lines[head - 1].Trim().Length == 0)
                            head--;
                        while (head > 0 && lines[head - 1].Trim() is { Length: > 0 } prev
                               && !prev.EndsWith('}') && !prev.EndsWith(';')
                               && !prev.StartsWith('#'))
                            head--;
                        defs.Add(new LpDefinition(
                            head + 1, i + 1,
                            string.Join('\n', lines[head..(i + 1)]).Replace('-', '_')));
                        openLine = -1;
                    }
                }
            }
        }
        return (defs, sane && depth == 0 && !scan.InBlockComment && scan.RawStringEnd is null);
    }

    /// <summary>
    /// A cited RANGE must name something that is actually in the definition it lands in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS, AND WHY <see cref="EveryNamedSymbolOccursInItsCitedFile"/> IS NOT IT.
    /// That test asks the FILE, deliberately, so that a comment may cite a definition while
    /// naming the class declared in the header. The cost of that leniency came due on
    /// 2026-08-03: one function's three citations were each about 38 lines off
    /// (<c>note-spacing.cc:243-248</c> for what lives at <c>:281-286</c>, <c>:263-264</c> for
    /// <c>:299-300</c>, <c>:200-201</c> for <c>:248-249</c>) and every one of them named the
    /// right symbol, in the right file. A range that has drifted inside its own file is
    /// invisible to a file-level check, and drifted ranges are the normal way a citation
    /// rots — the source moves, the name does not.
    /// </para>
    /// <para>
    /// ⚠️ THE ENCLOSING DEFINITION, NOT THE RANGE ITSELF. Requiring the name between
    /// <c>From</c> and <c>To</c> would fail on correct citations: <c>:281-286</c> is six
    /// lines inside <c>Note_spacing::stem_dir_correction</c> that mention neither the class
    /// nor the function, and that is exactly the kind of line worth citing. Asking whether
    /// the name occurs anywhere in the definition those lines belong to is the weakest
    /// question that still separates ":281-286 in stem_dir_correction" from ":243-248 in
    /// stem_dir_correction" — no, it does not separate those two, and that is the honest
    /// limit of this test: it catches a range that left the named definition, not one that
    /// merely moved within it. HANDOFF 5.4's guessing helper is the alternative.
    /// </para>
    /// <para>
    /// ⚠️ WHAT IS OUT OF SCOPE, said rather than silently dropped: citations with no line
    /// range (nothing to check), citations naming no verifiable symbol (the ratchet
    /// <see cref="CitationsThatNameNothing_DoNotGrow"/> owns those), names absent from the
    /// whole file (<see cref="EveryNamedSymbolOccursInItsCitedFile"/> owns those, and
    /// claiming them here would report one debt twice), and every non-C++ file.
    /// Scheme and <c>.ly</c> are skipped because <see cref="IsVerifiableSymbol"/> claims only
    /// underscored names, and closing THAT wants a real symbol index of the LilyPond tree
    /// rather than a sharper regex — the same limit the file-level test already documents.
    /// The test reports its own coverage so the untested share stays visible.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The brace scanner must find the definition a known line is in, and not find one it
    /// is not in.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE POSITIVE CONTROL, and it exists because the check above returned a
    /// CLEAN result for a file it could not read at all. <c>stem.cc:1168-1179</c> cites
    /// <c>Stem::is_cross_staff</c> and is really inside <c>Stem::calc_stem_info</c> — a
    /// defect of exactly the kind this class was written for — and the scanner missed it by
    /// stopping at <c>:285</c>, where a raw string opens. Nothing failed; the citation simply
    /// stopped being counted. "It found nothing" and "it never got there" are the same
    /// observation without a control, so both directions are asserted here on lines read by
    /// hand in the pinned 2.26.0 tree.
    /// </remarks>
    [Fact]
    public void TheBraceScannerReachesPastRawStrings()
    {
        string? lp = LilyPondSource();
        if (lp is null)
        {
            _out.WriteLine("NO LILYPOND TREE ON THIS MACHINE — the scanner is unexercised.");
            return;
        }

        var (defs, balanced) = CxxDefinitions(File.ReadAllLines(
            Path.Combine(lp, "lily", "stem.cc")));
        Assert.True(balanced, "lily/stem.cc did not scan to a closed brace depth.");

        // :1279 is Stem::is_cross_staff. :1168 is inside Stem::calc_stem_info, which is what
        // the citation at CrossStaffItem.cs claimed was is_cross_staff.
        var atName = defs.SingleOrDefault(d => d.From <= 1279 && 1279 <= d.To);
        Assert.NotNull(atName);
        Assert.Contains("is_cross_staff", atName!.Text, StringComparison.Ordinal);

        var atCitedRange = defs.SingleOrDefault(d => d.From <= 1168 && 1168 <= d.To);
        Assert.NotNull(atCitedRange);
        Assert.DoesNotContain("is_cross_staff", atCitedRange!.Text, StringComparison.Ordinal);
        Assert.Contains("calc_stem_info", atCitedRange.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CitationRangesHoldTheirNamedSymbol()
    {
        string? lp = LilyPondSource();
        if (lp is null)
        {
            _out.WriteLine("NO LILYPOND TREE ON THIS MACHINE — cited ranges unchecked.");
            return;
        }

        var defsByFile = new Dictionary<string, (IReadOnlyList<LpDefinition> Defs, bool Balanced)>();
        var bodyByFile = new Dictionary<string, string>();
        var inScope = new List<Citation>();
        var stale = new List<string>();
        var noDefinition = new List<Citation>();
        var unreadable = new SortedSet<string>();

        foreach (var c in AllCitations())
        {
            if (c.From == 0 || !c.LpPath.EndsWith(".cc") && !c.LpPath.EndsWith(".hh"))
                continue;

            string path = Path.Combine(lp, c.LpPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                continue;
            if (!defsByFile.TryGetValue(c.LpPath, out var scanned))
            {
                var lines = File.ReadAllLines(path);
                defsByFile[c.LpPath] = scanned = CxxDefinitions(lines);
                bodyByFile[c.LpPath] = string.Join('\n', lines).Replace('-', '_');
            }
            if (!scanned.Balanced)
            {
                // Its braces did not close. Every citation into it would land "nowhere",
                // which reads as "nothing to check" — the failure this test must not make
                // quietly. Report the file and judge none of its citations.
                unreadable.Add(c.LpPath);
                continue;
            }
            var defs = scanned.Defs;

            // A name the FILE does not have is EveryNamedSymbolOccursInItsCitedFile's
            // finding, not this one's — asking which definition holds a name that is in no
            // definition would report the same debt twice under a wrong description.
            var named = c.OwnSymbols
                .Where(IsVerifiableSymbol)
                .Where(s => bodyByFile[c.LpPath].Contains(s.Replace('-', '_'), StringComparison.Ordinal))
                .Select(s => s.Replace('-', '_'))
                .ToList();
            if (named.Count == 0)
                continue;

            inScope.Add(c);
            var landed = defs.Where(d => d.From <= c.To && c.From <= d.To).ToList();
            if (landed.Count == 0)
            {
                // File scope: an include, a static table, a comment between functions.
                // Nothing encloses the range, so there is nothing to disagree with.
                noDefinition.Add(c);
                continue;
            }
            if (landed.Any(d => named.Any(s => d.Text.Contains(s, StringComparison.Ordinal))))
                continue;

            stale.Add($"  {c.SourceFile}:{c.SourceLine} -> {c.LpPath}:{c.From}-{c.To} "
                      + $"names {string.Join(", ", named)}, but those lines are in "
                      + string.Join(" + ", landed.Take(3).Select(d => $"{d.From}-{d.To}"))
                      + (landed.Count > 3 ? $" + {landed.Count - 3} more" : ""));
        }

        _out.WriteLine(
            $"{inScope.Count} citations give a C++ range and name a verifiable symbol; "
            + $"{inScope.Count - stale.Count - noDefinition.Count} land in a definition that "
            + $"contains the name, {noDefinition.Count} land at file scope (nothing to check), "
            + $"{stale.Count} do not.");

        Assert.True(unreadable.Count == 0,
            "the brace scanner could not read these cited files to the end, so their "
            + "citations were judged by nothing. Fix StripCxxCode — do NOT let this stand, "
            + "because an unread file looks exactly like a clean one here:\n"
            + string.Join("\n", unreadable.Select(f => "  " + f)));

        Assert.True(
            stale.Count <= StaleRangeBaseline,
            $"citations whose range left the definition they name rose to {stale.Count} from "
            + $"the baseline {StaleRangeBaseline}. Read the lines and correct the range — the "
            + "name is not the address. Do NOT raise the baseline.\n"
            + string.Join("\n", stale));
    }
}
