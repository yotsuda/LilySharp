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
    /// </remarks>
    private const int UnnamedCitationBaseline = 746;

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
        "stem_attachment|mf/feta-noteheads.mf",
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
    private sealed record Citation(
        string SourceFile, int SourceLine, string LpPath, int From, int To,
        IReadOnlyList<string> Symbols);

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
                foreach (Match m in CitationPattern.Matches(lines[i]))
                {
                    // Only the text AFTER the address counts as naming it — a symbol on the
                    // line before belongs to whatever that line cited.
                    string tail = lines[i][(m.Index + m.Length)..];
                    var symbols = SymbolPattern.Matches(tail)
                        .Select(s => s.Value)
                        .Where(LooksLikeLilyPondSymbol)
                        .Distinct()
                        .ToList();
                    int from = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                    int to = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : from;
                    found.Add(new Citation(
                        Path.GetRelativePath(root, file), i + 1, m.Groups[1].Value,
                        from, to, symbols));
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
        var ranged = AllCitations().Where(c => c.From > 0).ToList();
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
}
