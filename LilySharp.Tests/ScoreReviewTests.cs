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

    private static string Source(string file, [CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "LilySharp.Core", "Editing", file));

    private static readonly Regex CheckId = new(@"new(?: CompositionFinding)?\(""([a-z]+\.[a-z-]+)""");

    [Fact]
    public void EveryCheckHasANote_AndEveryNoteACheck()
    {
        var made = CheckId.Matches(Source("CompositionChecks.cs") + Source("KeyChecks.cs") + Source("ChordChecks.cs")).Select(m => m.Groups[1].Value).ToHashSet();
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

    private static string Tune(string key, string notes) => $$"""
        title "A Tune"
        key {{key}}
        time 4/4
        octave absolute
        part m { instrument flute }
        section A { m { {{notes}} } }
        form { A }
        score { staff m }
        """;

    private static System.Collections.Generic.List<JsonObject> KeyFindings(string source)
        => Report(source, focus: "key")["findings"]!.AsArray().Select(f => f!.AsObject()).ToList();

    private static bool Says(string source, string id) => KeyFindings(source).Any(f => (string?)f["id"] == id);

    // A scale of D major up and down, and its chord.
    private const string InD = "d'4 e' fis' g' | a' b' cis'' d'' | cis'' b' a' g' | fis' e' d' fis' | a' fis' d'2 |";
    private const string InAMinor = "a'4 b' c'' d'' | e'' f'' gis'' a'' | g'' f'' e'' d'' | c'' b' a'2 |";

    [Fact]
    public void AForgottenKeySignature_IsSaid_WithTheKeyHeard()
    {
        var found = KeyFindings(Tune("c major", InD)).Single(f => (string?)f["id"] == "key.signature");
        Assert.Equal("mistake", (string?)found["kind"]);
        Assert.Contains("The key says C major, and the notes are heard in D major", (string?)found["what"]);
        Assert.Contains("F♯", (string?)found["what"]);
        Assert.Contains("C♯", (string?)found["what"]);
        Assert.StartsWith("D major (", (string?)Report(Tune("c major", InD))["facts"]!["keyHeard"]);
    }

    [Fact]
    public void TheRightSignature_IsNotSaid()
    {
        Assert.False(Says(Tune("d major", InD), "key.signature"));
        // The relative key shares its signature.
        Assert.False(Says(Tune("b minor", InD), "key.signature"));
        // A minor key's raised notes lie outside its signature, and are its own.
        Assert.False(Says(Tune("a minor", InAMinor), "key.signature"));
    }

    [Fact]
    public void NotesInNoKey_AreNotHeardInOne()
    {
        const string chromatic = "c'4 cis' d' dis' | e' f' fis' g' | gis' a' ais' b' | ais' a' gis' g' | fis' f' e' dis' | d' cis' c'2 |";
        Assert.False(Says(Tune("c major", chromatic), "key.signature"));
    }

    [Fact]
    public void APieceThatChangesKey_IsNotWeighedAgainstOneKey()
    {
        string book = Tune("c major", "c'4 d' e' f' | g' a' b' c'' | c'' b' a' g' | f' e' d' c' |")
            .Replace("form { A }", $"section B {{ key d major m {{ {InD} }} }}\nform {{ A B }}");
        Assert.False(Says(book, "key.signature"));
        Assert.False(Says(book, "key.outside"));
    }

    [Fact]
    public void ANoteThatLeadsNowhere_IsAsked_WithItsBar()
    {
        var found = KeyFindings(Tune("c major", "c'4 d' e' g' | ees'' c'' a' g' | e' f' g' a' | g' e' c'2 |")).Single(f => (string?)f["id"] == "key.outside");
        Assert.Equal("low", (string?)found["confidence"]);
        Assert.Equal([2], found["where"]!["bars"]!.AsArray().Select(b => (int)b!));
        Assert.Contains("E♭5 in bar 2, as the form plays them", (string?)found["what"]);
    }

    [Theory]
    [InlineData("a minor", InAMinor)]                                                     // the leading tone, and its key's own
    [InlineData("a minor", "e'4 gis' b' e'' | c'' a' e' a' | e'' d'' c'' b' | a' c'' a'2 |")]   // the leading tone, a leap from anything
    [InlineData("a minor", "d'4 fis' a' d'' | c'' a' e' a' | e'' d'' c'' b' | a' c'' a'2 |")]   // the raised sixth, a leap from anything
    [InlineData("c major", "c'4 d' e' f' | g' gis' a' g' | e' f' g' a' | g' e' c'2 |")]   // a semitone into the key
    [InlineData("c major", "c'4 d' e' f' | fis' g' a' b' | c'' b' a' g' | e' d' c'2 |")]  // a semitone from the key
    public void ANoteThatLeadsSomewhere_IsNotSaid(string key, string notes)
        => Assert.False(Says(Tune(key, notes), "key.outside"));

    private const string TwoParts = """
        title "Two"
        key c major
        time 4/4
        octave absolute
        part m { instrument flute }
        part h { instrument cello }
        section A { m { c''4 d'' e'' g'' | ees'' c'' a' g' | e'' f'' g'' a'' | g'' e'' c''2 | } h { HARMONY | } }
        form { A }
        score { staff m staff h }
        """;

    [Fact]
    public void ANoteTheHarmonyHas_IsAChord_NotASlip()
    {
        string plain = "c1 | c1 | c1 | c1";
        string borrowed = "c1 | <c ees>1 | c1 | c1";
        Assert.True(Says(TwoParts.Replace("HARMONY", plain), "key.outside"));
        Assert.False(Says(TwoParts.Replace("HARMONY", borrowed), "key.outside"));
    }

    [Fact]
    public void ABassPlaysTheRootOfWhateverChordIsThere()
    {
        // In a bass's register, played alone, as the owner's bass books are.
        const string book = "c,4 d, e, g, | ees c a, g, | e, f, g, a, | g, e, c,2 |";
        Assert.False(Says(Tune("c major", book).Replace("instrument flute", "instrument cello"), "key.outside"));
    }

    [Theory]
    [InlineData("c'4 des' d' e' | f' g' a' b' |", "des", "up", "cis")]
    [InlineData("e'4 d' cis' c' | d' e' f' g' |", "cis", "down", "des")]
    public void APassingNoteAgainstItsDirection_IsSaid_WhereItIsWritten(string notes, string written, string way, string right)
    {
        var found = KeyFindings(Tune("c major", notes)).Single(f => (string?)f["id"] == "spelling.direction");
        Assert.Equal("notation", (string?)found["kind"]);
        Assert.Equal(written, (string?)found["where"]!["written"]);
        Assert.Equal(6, (int)found["where"]!["line"]!);
        Assert.Contains($"`{written}` on the way {way}: write `{right}`", (string?)found["what"]);
    }

    [Theory]
    [InlineData("c major", "c'4 cis' d' e' | f' g' a' b' |")]   // rising, raised
    [InlineData("c major", "e'4 d' des' c' | d' e' f' g' |")]   // falling, lowered
    [InlineData("c major", "c'4 des' c' e' | f' g' a' b' |")]   // a neighbour, moving on no one way
    [InlineData("c major", "d'4 cis' d' e' | f' g' a' b' |")]    // a neighbour below, moving on no one way
    [InlineData("f major", "a'4 bes' b' c'' | d'' e'' f'' g'' |")] // a note of the key, whatever is either side
    public void ASpellingWithItsDirection_IsNotSaid(string key, string notes)
        => Assert.False(Says(Tune(key, notes), "spelling.direction"));

    [Fact]
    public void TheKeyChecks_AreTheReviewsAlone()
    {
        string book = Tune("c major", InD);
        Assert.Contains(Report(book)["findings"]!.AsArray(), f => (string?)f!["id"] == "key.signature");
        Assert.DoesNotContain(CompositionChecks.Findings(book), f => f.Id.StartsWith("key.", System.StringComparison.Ordinal));
    }

    private static string WithChords(string key, string row, string form = "A") => $$"""
        title "With Chords"
        key {{key}}
        time 4/4
        octave absolute
        part m { instrument flute }
        section A { m { c''1 | c''1 | c''1 | c''1 | } chords prog { {{row}} } }
        form { {{form}} }
        score { chords prog staff m }
        """;

    private static System.Collections.Generic.List<JsonObject> ChordFacts(string source)
        => Report(source)["facts"]!["chords"]!.AsArray().Select(c => c!.AsObject()).ToList();

    private static string? RoleIn(string key, string row, string symbol)
        => (string?)ChordFacts(WithChords(key, row)).First(c => (string?)c["symbol"] == symbol)["role"];

    private static System.Collections.Generic.List<JsonObject> ChordFindings(string source)
        => KeyFindings(source).Where(f => (string?)f["id"] == "chord.outside").ToList();

    [Fact]
    public void TheChords_AreListedWithTheirDegrees_EachWhereItIsFirstPlayed()
    {
        var chords = ChordFacts(WithChords("c major", "C | Am | F | G7 |", "A A"));
        Assert.Equal(["C", "Am", "F", "G7"], chords.Select(c => (string?)c["symbol"]));
        Assert.Equal([1, 2, 3, 4], chords.Select(c => (int)c["bar"]!));
        Assert.Equal(["I", "VIm", "IV", "V7"], chords.Select(c => (string?)c["roman"]));
        Assert.All(chords, c => Assert.Null(c["role"]));
    }

    [Fact]
    public void AChordOnAMark_IsReadToo()
    {
        var chords = ChordFacts(Tune("c major", "c'1@chord(C) | d'1@chord(E7) | a'1@chord(Am) | c'1 |"));
        Assert.Equal(["C", "E7", "Am"], chords.Select(c => (string?)c["symbol"]));
        Assert.Equal([1, 2, 3], chords.Select(c => (int)c["bar"]!));
        Assert.Equal("V/VI, a secondary dominant", (string?)chords[1]["role"]);
    }

    [Fact]
    public void ThePlayedOrderDecidesWhatAChordLeadsTo()
    {
        // E7 ends the section, and the form plays it into A again: C. It leads to A, and goes to C.
        var found = ChordFindings(WithChords("c major", "C | C | C | E7 |", "A A")).Single();
        Assert.Contains("E7 (III7, bar 4, as the form plays them) leads to a chord on A, and goes to C", (string?)found["what"]);
    }

    [Theory]
    [InlineData("c major", "C | E7 | Am | C |", "E7", "V/VI, a secondary dominant")]
    [InlineData("c major", "C | D7 | G | C |", "D7", "V/V, a secondary dominant")]
    [InlineData("c major", "C | C7 | F | C |", "C7", "V/IV, a secondary dominant")]
    [InlineData("c major", "C | F#dim | G | C |", "F#dim", "vii°/V, a secondary leading-tone chord")]
    [InlineData("c major", "C | Fm | C | C |", "Fm", "borrowed from the parallel minor")]
    [InlineData("c major", "C | Bb | C | C |", "Bb", "borrowed from the parallel minor")]
    [InlineData("c major", "C | Ab | C | C |", "Ab", "borrowed from the parallel minor")]
    [InlineData("c major", "C | Eb | C | C |", "Eb", "borrowed from the parallel minor")]
    [InlineData("c major", "Dm | Db | C | C |", "Db", "♭II, the Neapolitan")]
    [InlineData("c major", "Dm | Db7 | C | C |", "Db7", "a tritone substitute")]
    [InlineData("c major", "C | F7 | C | G7 |", "F7", "a dominant seventh on the blues's own degree")]
    [InlineData("c major", "C | C7 | G | C |", "C7", "a dominant seventh on the blues's own degree")]
    [InlineData("a minor", "Am | A | Am | Am |", "A", "borrowed from the parallel major")]
    public void AChordOutsideTheScale_IsToldByItsRole(string key, string row, string symbol, string role)
    {
        Assert.Equal(role, RoleIn(key, row, symbol));
        Assert.Empty(ChordFindings(WithChords(key, row)));
    }

    [Theory]
    [InlineData("a minor", "Am | E7 | Am | Am |")]    // the major V of a minor key
    [InlineData("a minor", "Am | G#dim | Am | Am |")] // its vii°
    [InlineData("c major", "Dm7 | G7 | Cmaj7 | C |")]
    [InlineData("c major", "C | G7(b9) | C | C |")]   // an extension colours a chord and does not place it
    public void AChordOfTheKey_HasNoRole_AndNoFinding(string key, string row)
    {
        Assert.All(ChordFacts(WithChords(key, row)), c => Assert.Null(c["role"]));
        Assert.Empty(ChordFindings(WithChords(key, row)));
    }

    [Fact]
    public void AWrittenFunction_IsReadAsItsChord()
    {
        var chords = ChordFacts(WithChords("c major", "I | V7/V | V | I |"));
        Assert.Equal("V/V, a secondary dominant", (string?)chords[1]["role"]);
        Assert.Equal("V7/V", (string?)chords[1]["roman"]);
    }

    [Fact]
    public void AWrittenFunction_SaysWhatItIsFor_WhereTheSameChordCouldBeTheBlues()
    {
        // C7 going to G is the blues's I7; written V7/IV, it says it leads to F, and is asked about.
        Assert.Empty(ChordFindings(WithChords("c major", "C | C7 | G | C |")));
        var found = ChordFindings(WithChords("c major", "I | V7/IV | V | I |")).Single();
        Assert.Contains("leads to a chord on F, and goes to G", (string?)found["what"]);
    }
    [Fact]
    public void ARoleNotFulfilled_IsAsked_NotToldOff()
    {
        var found = ChordFindings(WithChords("c major", "C | E7 | F | C |")).Single();
        Assert.Equal("style", (string?)found["kind"]);
        Assert.Equal("low", (string?)found["confidence"]);
        Assert.Equal([2], found["where"]!["bars"]!.AsArray().Select(b => (int)b!));
        Assert.Equal("E7", (string?)found["where"]!["written"]);
        Assert.Equal(6, (int)found["where"]!["line"]!);
        Assert.Contains("E7 (III7, bar 2, as the form plays them) leads to a chord on A, and goes to F", (string?)found["what"]);
        Assert.Contains("Is the E7 in bar 2 meant to lead to A? Going to F instead, it may be a deceptive cadence.", (string?)found["ask"]);
    }

    [Fact]
    public void AChordWithNoRole_IsAsked()
    {
        var found = ChordFindings(WithChords("c major", "C | F#maj7 | C | C |")).Single();
        Assert.Contains("is outside the key, and none of the usual roles fits it", (string?)found["what"]);
        Assert.Equal("Is F#maj7 in bar 2 the chord you meant?", (string?)found["ask"]);
    }

    [Fact]
    public void TheLastChord_HasNotGoneAnywhereYet()
        => Assert.Empty(ChordFindings(WithChords("c major", "C | C | C | E7 |")));

    [Fact]
    public void AChordHeldOn_LeadsWhereItGoesNext()
        => Assert.Empty(ChordFindings(WithChords("c major", "E7 | E7 | Am | C |")));

    [Fact]
    public void WithNoOneKey_TheSymbolsAreSaid_AndNothingIsJudged()
    {
        string book = WithChords("c major", "C | F#maj7 | C | C |").Replace("section A {", "section A { key d major ");
        var chords = ChordFacts(book);
        Assert.Equal("F#maj7", (string?)chords[1]["symbol"]);
        Assert.Null(chords[1]["roman"]);
        Assert.Empty(ChordFindings(book));
    }

    [Fact]
    public void APieceWithNoChords_ListsNone()
        => Assert.Null(Report(Piece)["facts"]!["chords"]);

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
