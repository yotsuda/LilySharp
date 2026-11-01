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

using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>The review's report (docs/review-tool-design.md §2) and the checks' ids and notes under it.</summary>
public class ScoreReviewTests
{
    private const string Piece = """
        title "A Short Air"
        key g major
        time 4/4
        tempo 96
        octave absolute
        part fl { instrument flute }
        part vc { instrument cello }
        section A { fl { g'4@mf a' b' c'' | d''2 b' | } vc { g,2@p d | g,1 | } }
        section B { fl { e''4@f d'' c'' b' | a'2 fis' | } vc { c2@mf e | d1 | } }
        form { A B A }
        score { staff fl staff vc }
        """;

    private static JsonObject Report(string source, string audience = "beginner", string? focus = null)
        => JsonNode.Parse(ScoreReview.Json(source, audience, focus))!.AsObject();

    private static string Source([CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "LilySharp.Core", "Editing", "CompositionChecks.cs"));

    private static readonly Regex CheckId = new(@"new(?: CompositionFinding)?\(""([a-z]+\.[a-z-]+)""");

    [Fact]
    public void EveryCheckHasANote_AndEveryNoteACheck()
    {
        var made = CheckId.Matches(Source()).Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(made.Count >= 60, $"read only {made.Count} ids: has the way a check names itself changed?");
        Assert.Empty(made.Except(ReviewNotes.All.Keys));
        Assert.Empty(ReviewNotes.All.Keys.Except(made));
    }

    [Fact]
    public void TheComposingFlowStillReadsTheSentences()
    {
        const string bare = "part a { }\npart b { }\nsection A { a { c'4 d' e' f' | } b { c4 d e f | } }\nform { A }\nscore { staff a staff b }\n";
        var findings = CompositionChecks.Findings(bare);
        Assert.Contains(findings, f => f.Id == "part.no-instrument" && f.Parts.SequenceEqual(["a", "b"]));
        Assert.Equal(findings.Select(f => f.Message), CompositionChecks.Of(bare));
    }

    [Fact]
    public void TheFacts_SayWhatThePieceIs()
    {
        var report = Report(Piece);
        Assert.Equal("beginner", (string?)report["audience"]);
        Assert.True((bool)report["compiles"]!);
        var facts = report["facts"]!.AsObject();
        Assert.Equal("A Short Air", (string?)facts["title"]);
        Assert.Equal("G major", (string?)facts["key"]);
        Assert.Equal("4/4", (string?)facts["meter"]);
        Assert.Equal("♩ = 96", (string?)facts["tempo"]);
        Assert.Equal(6, (int)facts["bars"]!);
        Assert.Equal(["A", "B", "A"], facts["form"]!.AsArray().Select(s => (string?)s));
        var flute = facts["parts"]!.AsArray()[0]!.AsObject();
        Assert.Equal("fl", (string?)flute["name"]);
        Assert.Equal("flute", (string?)flute["instrument"]);
        Assert.Equal("F♯4–E5", (string?)flute["range"]);
    }

    [Fact]
    public void AFinding_CarriesWhatTheAiNeedsToJudgeIt()
    {
        var findings = Report(Piece)["findings"]!.AsArray().Select(f => f!.AsObject()).ToList();
        var length = findings.Single(f => (string?)f["id"] == "form.length");
        Assert.Equal("idea", (string?)length["kind"]);
        Assert.Equal("low", (string?)length["confidence"]);
        Assert.StartsWith("The piece is 6 bars long", (string?)length["what"]);
        Assert.False(string.IsNullOrEmpty((string?)length["why"]));
        Assert.Contains("short piece was asked for", (string?)length["fine_when"]);
    }

    [Fact]
    public void MistakesComeFirst()
    {
        const string slip = """
            title "Slips"
            key c major
            time 4/4
            octave absolute
            part a { instrument flute }
            section A { a { c'4@mf d' e' c''' | e' f' g' c''' | g' a' g' c''' | e' d' c'2 | } }
            form { A }
            score { staff a }
            """;
        var kinds = Report(slip)["findings"]!.AsArray().Select(f => (string?)f!["kind"]).ToList();
        string[] order = ["mistake", "notation", "style", "idea"];
        Assert.Equal(kinds.OrderBy(k => System.Array.IndexOf(order, k)), kinds);
        Assert.NotEmpty(kinds);
    }

    [Fact]
    public void TheStrengths_NameWhatWasReadAndNotFaulted()
    {
        var strengths = Report(Piece)["strengths"]!.AsArray().Select(s => (string?)s).ToList();
        Assert.Contains("It ends where it began: in G major, over its tonic.", strengths);
        Assert.Contains("The form has a return: A B A.", strengths);
        Assert.Contains("Every part has its own dynamics, from its first note.", strengths);
        // Six bars is said to be short, so its length is no strength.
        Assert.DoesNotContain(strengths, s => s!.StartsWith("It has room", System.StringComparison.Ordinal));
    }

    [Fact]
    public void APieceWithNoMusicHasNoStrengths()
        => Assert.Empty(Report("title \"Empty\"\n")["strengths"]!.AsArray());

    [Fact]
    public void AScoreThatDoesNotCompile_IsReportedAsThat_NotAsAPieceWithNoFaults()
    {
        var report = Report("title \"Broken\"\npart a { instrument flute }\nsection A { a { c'4 d' \n");
        Assert.False((bool)report["compiles"]!);
        var error = report["errors"]!.AsArray()[0]!.AsObject();
        Assert.StartsWith("LYS", (string?)error["code"]);
        Assert.True((int)error["line"]! >= 1);
        Assert.Null(report["findings"]);
        Assert.Null(report["strengths"]);
    }

    [Fact]
    public void WhatTheCompilerWarnsOf_IsInTheReport()
    {
        // A flute's C3 is out of its range (LYS1052).
        string low = Piece.Replace("g'4@mf a' b' c''", "c4@mf a' b' c''");
        var warnings = Report(low)["warnings"]!.AsArray().Select(w => w!.AsObject()).ToList();
        Assert.Contains(warnings, w => (string?)w["code"] == "LYS1052" && (int)w["line"]! == 8);
    }

    [Fact]
    public void AFocus_KeepsItsFindings_AndTheRest()
    {
        var all = Report(Piece);
        var form = Report(Piece, focus: "form");
        Assert.Contains(form["findings"]!.AsArray(), f => (string?)f!["id"] == "form.length");
        var notation = Report(Piece, focus: "notation")["findings"]!.AsArray();
        Assert.All(notation, f => Assert.Equal("notation", (string?)f!["kind"]));
        Assert.DoesNotContain(notation, f => (string?)f!["id"] == "form.length");
        Assert.Equal(all["facts"]!.ToJsonString(), form["facts"]!.ToJsonString());
        Assert.Equal(all["strengths"]!.ToJsonString(), form["strengths"]!.ToJsonString());
    }

    [Fact]
    public void TheAudienceIsGivenBack()
        => Assert.Equal("advanced", (string?)Report(Piece, "advanced")["audience"]);

    [Fact]
    public void TheReportKeepsItsWordsAsWritten()
    {
        string json = ScoreReview.Json(Piece);
        Assert.Contains("♩ = 96", json);
        Assert.DoesNotContain("\\u", json);
    }
}
