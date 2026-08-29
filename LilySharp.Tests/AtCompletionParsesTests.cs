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
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;
// Not a using for LilySharp.Lsp.Protocol: its DiagnosticSeverity would collide
// with Core's, which this file uses throughout. The one protocol enum needed
// below is spelled out instead.
using LspInsertTextFormat = LilySharp.Lsp.Protocol.InsertTextFormat;

namespace LilySharp.Tests;

/// <summary>
/// Everything the '@' completion offers must actually parse as an annotation on a
/// note AND be recognized by the collector — otherwise the editor suggests
/// something that either errors the moment it is accepted (e.g. the structure-only
/// jump directive 'ds.al.coda') or is silently dropped with an "Unknown annotation"
/// warning (e.g. '@harmonic' was offered while unregistered).
/// </summary>
[Trait("Category", "Unit")]
public class AtCompletionParsesTests
{
    [Fact]
    public void EveryAtCompletionItem_ParsesAndIsRecognized()
    {
        var list = LilySharpLanguageServer.GetArticulationCompletions();
        var failures = new System.Collections.Generic.List<string>();
        foreach (var item in list.Items)
        {
            var raw = string.IsNullOrEmpty(item.InsertText) ? item.Label : item.InsertText;
            // An item that opens a SECOND list ('@notehead(' → the shapes) is a
            // stub: its argument is what the follow-up suggestion fills in, so
            // there is nothing to parse until then. Each such argument list has
            // its own round-trip test below.
            if (item.Command != null && raw.EndsWith("($0)", System.StringComparison.Ordinal)
                && !raw.StartsWith("chord", System.StringComparison.Ordinal))
                continue;
            // Strip snippet placeholders ($0, $1, ${1:…}) — editor cursors, not text.
            // e.g. the 'chord($0)' item becomes '@chord()' (a recognized empty chord).
            var label = System.Text.RegularExpressions.Regex.Replace(raw, @"\$\{[^}]*\}|\$\d+", "");
            var tree = MusicSource.Parse("c4@" + label);

            var problems = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();

            // A completion must never offer a name the collector then ignores: run
            // the annotation validator and reject any "Unknown annotation" warning.
            var validator = new AnnotationNameValidator();
            validator.Validate(tree);
            problems.AddRange(validator.Diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnknownAnnotation)
                .Select(d => d.Message));

            if (problems.Count > 0)
                failures.Add($"@{label} -> {string.Join("; ", problems)}");
        }
        Assert.True(failures.Count == 0,
            "'@' completion offers items that don't parse or aren't recognized:\n"
            + string.Join("\n", failures));
    }

    /// <summary>
    /// The other direction of the same contract: the completion must not just be
    /// SOUND (nothing offered that fails to parse) but COMPLETE. Every plain
    /// annotation the collector consumes — the articulation/ornament registry, the
    /// dynamics table, and the named feature annotations — has to be offered, or
    /// the feature is invisible in the editor. This is how @notehead, the whole
    /// fretted-technique family and the extended dynamics were found missing.
    /// </summary>
    [Fact]
    public void EveryKnownPlainAnnotationName_IsOffered()
    {
        // Compound items ('ped(off)', 'notehead(x)') carry an argument; the plain
        // names checked here are the bare labels.
        var offered = LilySharpLanguageServer.GetArticulationCompletions().Items
            .Select(i => i.Label)
            .Where(l => !l.Contains('('))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var expected = ArticulationRegistry.Names
            .Concat(SyntaxFacts.DynamicTextLevels.Keys)
            // Hairpin/spanner dynamics carry no fixed level, so they are not in
            // the table above.
            .Concat(new[] { "cresc", "decresc", "dim" })
            .Concat(AnnotationNameValidator.PlainFeatureNames)
            .Distinct(System.StringComparer.OrdinalIgnoreCase);

        var missing = expected
            .Where(n => !offered.Contains(n))
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(missing.Count == 0,
            "The '@' completion does not offer these known annotations:\n  @"
            + string.Join("\n  @", missing));
    }

    /// <summary>
    /// The compound (argument-taking) families. They cannot be enumerated from a
    /// registry — each is a parser rule — so the list of families that must be
    /// reachable from the editor is pinned by name here.
    /// </summary>
    [Theory]
    // The argument-stub families are bare names here: their members live in a
    // second list (see ArgumentStub_OpensItsOwnList).
    [InlineData("notehead")]       // notehead shapes
    [InlineData("finger")]         // left-hand fingering
    [InlineData("pluck")]          // right-hand (plucking) fingering
    [InlineData("bend")]           // string bends
    [InlineData("feather")]        // feathered beams
    [InlineData("fig")]            // figured bass
    [InlineData("frame(")]         // guitar chord frames — free-form, no second list
    [InlineData("ottava(")]        // ottava bassa
    [InlineData("quindicesima")]   // 15ma / 15mb
    [InlineData("text")]           // free expressive text
    [InlineData("mark")]           // rehearsal mark
    public void CompoundAnnotationFamily_IsOffered(string labelPrefix)
    {
        var offered = LilySharpLanguageServer.GetArticulationCompletions().Items;
        Assert.True(
            offered.Any(i => i.Label.StartsWith(labelPrefix, System.StringComparison.OrdinalIgnoreCase)),
            $"The '@' completion offers nothing for the '{labelPrefix}…' family.");
    }

    /// <summary>The annotations whose argument is picked from a second list.</summary>
    public static TheoryData<string> ArgumentStubNames() =>
        new() { "notehead", "finger", "pluck", "bend", "feather", "fig" };

    /// <summary>
    /// Each of these inserts an empty argument list and asks the editor to suggest
    /// again, so the members are chosen from a second, small list instead of six
    /// '@notehead(…)'-style entries crowding the main one.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArgumentStubNames))]
    public void ArgumentStub_OpensItsOwnList(string name)
    {
        var items = LilySharpLanguageServer.GetArticulationCompletions().Items;
        var item = items.Single(i => i.Label == name);

        Assert.Equal($"{name}($0)", item.InsertText);              // caret inside the parens
        Assert.Equal(LspInsertTextFormat.Snippet, item.InsertTextFormat);
        Assert.NotNull(item.Command);                              // re-triggers suggestions

        // The members must NOT also be in the '@' list — that is the whole point.
        Assert.DoesNotContain(items, i => i.Label.StartsWith($"{name}("));

        // And the caret the stub leaves behind must route to a non-empty list.
        var arguments = LilySharpLanguageServer.GetAnnotationArgumentCompletions(name);
        Assert.NotNull(arguments);
        Assert.NotEmpty(arguments!.Items);
    }

    [Theory]
    [InlineData("c4@notehead(", "notehead")]
    [InlineData("c4@finger(", "finger")]
    [InlineData("c4@chord(", "chord")]
    public void InsideAnnotationArguments_TheAnnotationIsRecognized(string text, string expected)
        => Assert.Equal(expected, LilySharpLanguageServer.AnnotationArgumentName(text, text.Length));

    [Theory]
    [InlineData("c4@notehead")]        // not inside the parens yet
    [InlineData("c4@notehead(x) ")]    // past the closing paren
    [InlineData("(c4")]                // a paren that is not an annotation's
    public void OutsideAnnotationArguments_NoAnnotationIsReported(string text)
        => Assert.Null(LilySharpLanguageServer.AnnotationArgumentName(text, text.Length));

    /// <summary>
    /// Every argument the second list offers must complete its stub into something
    /// that parses and is recognized — the same contract the '@' list is held to.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArgumentStubNames))]
    public void EveryAnnotationArgument_ParsesAndIsRecognized(string name)
    {
        var arguments = LilySharpLanguageServer.GetAnnotationArgumentCompletions(name);
        Assert.NotNull(arguments);

        var failures = new System.Collections.Generic.List<string>();
        foreach (var argument in arguments!.Items)
        {
            var raw = string.IsNullOrEmpty(argument.InsertText) ? argument.Label : argument.InsertText;
            var tree = MusicSource.Parse($"c4@{name}({raw})");

            var problems = tree.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)
                .ToList();

            var validator = new AnnotationNameValidator();
            validator.Validate(tree);
            problems.AddRange(validator.Diagnostics
                .Where(d => d.Code == DiagnosticCodes.UnknownAnnotation)
                .Select(d => d.Message));

            if (problems.Count > 0)
                failures.Add($"@{name}({raw}) -> {string.Join("; ", problems)}");
        }
        Assert.True(failures.Count == 0,
            $"The '@{name}' argument list offers values that don't parse or aren't recognized:\n"
            + string.Join("\n", failures));
    }

    // ----- incremental search over the '@' list -----

    private static string[] MatchesFor(string query)
        => LilySharpLanguageServer.MatchAnywhere(
                LilySharpLanguageServer.GetArticulationCompletions(), query)
            .Items.Select(i => i.Label).ToArray();

    /// <summary>
    /// The query matches ANYWHERE in the name, the way an incremental search
    /// does — not just at its start or at a camelCase hump. The editor's own
    /// matcher cannot do this, which is why the server filters.
    /// </summary>
    [Theory]
    [InlineData("start", "startTrillSpan")]      // prefix
    [InlineData("span", "startTrillSpan")]       // camelCase hump
    [InlineData("ill", "startTrillSpan")]        // mid-word — the case that fails elsewhere
    [InlineData("rill", "stopTrillSpan")]
    [InlineData("corda", "unaCorda")]
    [InlineData("orde", "treCorde")]
    [InlineData("head", "notehead")]
    [InlineData("ermata", "fermata")]
    public void MatchAnywhere_FindsTheItemWhereverTheQueryLands(string query, string expected)
        => Assert.Contains(expected, MatchesFor(query));

    /// <summary>
    /// A word that is not in the name at all still finds the item, via the
    /// search-terms table — "pedal" for LilyPond's event names, "lv" for
    /// laissez vibrer.
    /// </summary>
    [Theory]
    [InlineData("pedal", "sustain")]
    [InlineData("pedal", "sostenuto")]
    [InlineData("pedal", "unaCorda")]
    [InlineData("ped", "sustain")]               // '@ped' is not a spelling; it is a search term
    [InlineData("lv", "laissezVibrer")]
    [InlineData("bartok", "snapPizz")]
    [InlineData("figured", "fig")]
    [InlineData("rehearsal", "mark")]
    public void MatchAnywhere_FindsItemsByTheirSearchTerms(string query, string expected)
        => Assert.Contains(expected, MatchesFor(query));

    [Fact]
    public void MatchAnywhere_DropsWhatDoesNotMatch()
    {
        var matches = MatchesFor("ill");
        Assert.DoesNotContain("staccato", matches);
        Assert.All(matches, label => Assert.Contains("ill",
            label + " " + string.Join(' ', matches), System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Two mechanics the editor needs for server-side filtering to hold: the list
    /// says it is incomplete (so the editor re-asks instead of filtering its cache
    /// with word-start rules), and each item's FilterText is the query itself (so
    /// the editor's matcher cannot drop what the server chose to return).
    /// </summary>
    [Fact]
    public void MatchAnywhere_KeepsTheEditorFromRefilteringTheResult()
    {
        var list = LilySharpLanguageServer.MatchAnywhere(
            LilySharpLanguageServer.GetArticulationCompletions(), "ill");

        Assert.True(list.IsIncomplete);
        Assert.NotEmpty(list.Items);
        Assert.All(list.Items, i => Assert.Equal("ill", i.FilterText));
    }

    [Fact]
    public void MatchAnywhere_WithNothingTypedYet_KeepsEverythingButStaysIncomplete()
    {
        var all = LilySharpLanguageServer.GetArticulationCompletions().Items.Length;
        var list = LilySharpLanguageServer.MatchAnywhere(
            LilySharpLanguageServer.GetArticulationCompletions(), "");

        Assert.Equal(all, list.Items.Length);
        Assert.True(list.IsIncomplete);   // or the editor would filter the cache itself
    }

    /// <summary>
    /// Reported: '@tril' listed four names, '@trill' collapsed to '.up'/'.down',
    /// '@trills' listed two names again. A complete name is not a finished search
    /// — the placement qualifiers are ONE more continuation, not a replacement
    /// for every name the text still matches.
    /// </summary>
    [Fact]
    public void CompleteName_StillOffersTheNamesThatMatch_AlongsideThePlacements()
    {
        static string[] LabelsFor(string text)
        {
            var list = LilySharpLanguageServer.GetCompletionContext(text, text.Length)
                    == LilySharpLanguageServer.CompletionContext.AfterArticulationPlacement
                ? LilySharpLanguageServer.PlacementAndStillMatchingNames(text, text.Length, null, false)
                : LilySharpLanguageServer.MatchAnywhere(
                    LilySharpLanguageServer.GetArticulationCompletions(),
                    LilySharpLanguageServer.PartialAnnotationName(text, text.Length));
            return list.Items.Select(i => i.Label).ToArray();
        }

        var partial = LabelsFor("c4@tril");
        Assert.Contains("trill", partial);
        Assert.Contains("pralltriller", partial);
        Assert.Contains("startTrillSpan", partial);
        Assert.DoesNotContain(".up", partial);          // not a complete name yet

        var complete = LabelsFor("c4@trill");
        Assert.Contains(".up", complete);               // the placements are offered…
        Assert.Contains(".down", complete);
        Assert.Contains("trill", complete);             // …and so is everything still matching
        Assert.Contains("pralltriller", complete);
        Assert.Contains("startTrillSpan", complete);
        Assert.Contains("stopTrillSpan", complete);

        var longer = LabelsFor("c4@trills");
        Assert.Contains("startTrillSpan", longer);
        Assert.DoesNotContain("trill", longer);         // 'trills' is not in 'trill'
    }

    /// <summary>Past the dot the name IS settled: only up/down can follow.</summary>
    [Theory]
    [InlineData("c4@trill.")]
    [InlineData("c4@trill.d")]
    public void AfterThePlacementDot_OnlyThePlacementsAreOffered(string text)
    {
        var labels = LilySharpLanguageServer
            .PlacementAndStillMatchingNames(text, text.Length, null, false)
            .Items.Select(i => i.Label).ToArray();

        Assert.Equal(["up", "down"], labels);
    }

    [Theory]
    [InlineData("c4@", "")]
    [InlineData("c4@ill", "ill")]
    [InlineData("c'8@stacc", "stacc")]
    [InlineData("c4@fig(6", "")]          // inside an argument, not the name
    [InlineData("c4 d4", "")]             // no annotation being typed
    public void PartialAnnotationName_ReadsWhatIsTypedAfterTheAt(string text, string expected)
        => Assert.Equal(expected, LilySharpLanguageServer.PartialAnnotationName(text, text.Length));

    [Fact]
    public void ChordCompletion_OnNote_InsertsParensForExplicitEntry()
    {
        var chord = LilySharpLanguageServer.GetArticulationCompletions(afterChord: false)
            .Items.Single(i => i.Label == "chord");
        Assert.Equal("chord($0)", chord.InsertText);
        Assert.NotNull(chord.Command); // re-triggers the diatonic-chord suggestions
    }

    [Fact]
    public void ChordCompletion_OnChord_InsertsBareAutoForm()
    {
        var chord = LilySharpLanguageServer.GetArticulationCompletions(afterChord: true)
            .Items.Single(i => i.Label == "chord");
        Assert.Equal("chord", chord.InsertText); // no '(…)': auto-derive from the notes
        Assert.Null(chord.Command);

        var tree = MusicSource.Parse("<c e g>@chord");
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ----- which chord form the '@' completion offers -----

    private static bool AutoNames(string source)
    {
        int at = source.IndexOf('@') + 1;
        return LilySharpLanguageServer.AtFollowsChord(source, at)
            && LilySharpLanguageServer.GroupBeforeAtAutoNames(source, at);
    }

    [Theory]
    [InlineData("{ <c e g>@ }")]          // recognizable chord → bare @chord
    [InlineData("{ <c e g>4@ }")]         // duration between '>' and '@'
    [InlineData("{ <d f a c>@ }")]
    [InlineData("{ << c e g >>@ }")]      // arpeggio auto-names the same way
    [InlineData("{ << c e g >>4@ }")]
    [InlineData("{ <c 3 5>@ }")]          // degrees: key-dependent → collector's call
    [InlineData("{ << <c e> g >>@ }")]    // nested chord member
    public void RecognizableGroup_OffersBareChord(string source)
        => Assert.True(AutoNames(source));

    [Theory]
    [InlineData("{ <c cis d>@ }")]        // a cluster: no known quality
    [InlineData("{ <c>@ }")]              // a single pitch derives nothing
    [InlineData("{ << c cis d >>@ }")]
    public void UnrecognizableGroup_FallsBackToParenForm(string source)
        => Assert.False(AutoNames(source));

    [Fact]
    public void NoteBeforeAt_IsNotAChord()
        => Assert.False(LilySharpLanguageServer.AtFollowsChord("{ c4@ }", 5));
}
