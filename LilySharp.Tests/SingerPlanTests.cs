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
using System.Runtime.InteropServices;
using LilySharp.Core.Audio;
using LilySharp.Core.Editing;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Who would sing a file's <c>singer</c> lines on this machine (<see cref="SingerPlan"/>,
/// owner's decisions 2026-11-13): what is asked of the engine for a score's sung voices, how
/// its answer is read, and the sentence for a line nobody installed can sing. No engine runs
/// here: the answer is the JSON <c>cantoria plan --json</c> prints, written out.
/// </summary>
[Trait("Category", "Unit")]
public class SingerPlanTests
{
    private const string Music = """
        time 4/4
        part m { clef treble }
        part n { clef bass }
        section A { m { c'2 d | } n { c2 d | } }
        lyrics ja { あ い | }
        lyrics en language english { la la | }
        lyrics lat language latin { la la | }
        form { A }

        """;

    /// <summary>The asked list of the file's first score, read as the command line reads a
    /// file (a singer a score prints is written out after it: <see cref="SingerParts.Expand"/>).</summary>
    private static (IReadOnlyList<SingerAsk> Asked, string Source) Asked(string source, string? defaultVoice = null)
    {
        var tree = SyntaxTree.Parse(source);
        if (SingerParts.Wanted(tree))
        {
            source = SingerParts.Expand(source, tree, out _);
            tree = SyntaxTree.Parse(source);
        }
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)));
        var score = RenderSpecParser.FindAllDeclared(tree).First();
        return (SingerPlan.AskedOfScore(tree, score.Spec, defaultVoice), source);
    }

    [Fact]
    public void ANamedVoice_IsAskedForByName_AtItsLine()
    {
        string src = Music + "singer \"neutrino/MERROW\" sings ja on m\nscore { staff m  lyrics ja }\n";
        var ask = Assert.Single(Asked(src).Asked);

        Assert.Equal(("m", "m", "neutrino/MERROW", "female", "ja"), (ask.Part, ask.Key, ask.Voice, ask.Gender, ask.Language));
        Assert.Equal(src.IndexOf("singer", StringComparison.Ordinal), ask.Position);
        Assert.Equal("singer \"neutrino/MERROW\" sings ja on m", ask.Text);
    }

    [Fact]
    public void AGenderAlone_AsksForNoVoice_OrForTheCommandLines()
    {
        string src = Music + "singer male sings ja on m\nscore { staff m  lyrics ja }\n";
        var ask = Assert.Single(Asked(src).Asked);
        Assert.Equal((null, "male"), (ask.Voice, ask.Gender));

        // `--voice` is the voice of the lines that name none, as `lysc wav --voice` always was.
        Assert.Equal("neutrino/SOMA", Assert.Single(Asked(src, "neutrino/SOMA").Asked).Voice);
    }

    [Fact]
    public void TwoLanguages_AreAskedEachInItsOwn()
    {
        string src = Music + "singer female sings ja on m\nsinger male sings lat on n\nscore { staff m  lyrics ja  staff n  lyrics lat }\n";
        var asked = Asked(src).Asked;

        Assert.Equal(["ja", "la"], asked.Select(a => a.Language));
        Assert.Equal(["m", "n"], asked.Select(a => a.Part));
        Assert.Equal(["female", "male"], asked.Select(a => a.Gender));
        Assert.Equal(src.IndexOf("singer male", StringComparison.Ordinal), asked[1].Position);
    }

    [Fact]
    public void AHarmonyLine_IsAVoiceOfItsOwn_AskedAtItsOwnLine()
    {
        string src = Music + "singer \"n/A\" sings ja on m\nsinger female sings ja on m up 3\nscore { staff m  lyrics ja }\n";
        var asked = Asked(src).Asked;

        Assert.Equal(["m", "m#2"], asked.Select(a => a.Key));
        Assert.Equal(["n/A", null], asked.Select(a => a.Voice));
        Assert.Equal(src.IndexOf("singer female", StringComparison.Ordinal), asked[1].Position);
        Assert.Equal("singer female sings ja on m up 3", asked[1].Text);
    }

    [Fact]
    public void ASingerPrintedAsAStaff_IsAskedAtTheLineThatNamesHer_InTheFilesOwnText()
    {
        string src = Music + "singer carol female sings ja on m up 3\nscore { staff carol  lyrics carol }\n";
        var (asked, expanded) = Asked(src);

        var ask = Assert.Single(asked);
        Assert.StartsWith("carol", ask.Part);                      // the part made of her line
        Assert.Equal((null, "female", "ja"), (ask.Voice, ask.Gender, ask.Language));
        Assert.Equal(src.IndexOf("singer carol", StringComparison.Ordinal), ask.Position);
        Assert.True(ask.Position < SingerParts.MadeFrom(expanded));
        Assert.Equal("singer carol female sings ja on m up 3", ask.Text);
    }

    [Fact]
    public void AFileThatSingsNothing_AsksNothing()
    {
        Assert.Empty(Asked(Music + "score { staff m  lyrics ja }\n").Asked);
    }

    [Fact]
    public void TheAskedList_IsWrittenAsCantoriaReadsIt()
    {
        var asked = new[]
        {
            new SingerAsk("melody", "melody", "neutrino/MERROW", "female", "ja"),
            new SingerAsk("alto", "alto#2", null, "female", "la"),
        };
        Assert.Equal(
            """{"asked":[{"part":"melody","key":"melody","voice":"neutrino/MERROW","gender":"female","language":"ja"},{"part":"alto","key":"alto#2","gender":"female","language":"la"}]}""",
            SingerPlan.AskedJson(asked));
    }

    private const string Answer = """
        { "ready": false,
          "singers": [
            { "part": "melody", "key": "melody", "asked": { "voice": "neutrino/NOPE", "language": "ja" },
              "voice": "neutrino/MERROW", "note": "neutrino/NOPE is not installed; singing with neutrino/MERROW", "reason": null },
            { "part": "alto", "asked": { "gender": "female", "language": "la" },
              "voice": null, "reason": "no installed voice sings Latin (la); installed voices sing ja, en, zh, ko",
              "languages": ["ja", "en", "zh", "ko"] } ] }
        """;

    private static readonly SingerAsk[] Two =
    [
        new("melody", "melody", "neutrino/NOPE", "female", "ja"),
        new("alto", "alto", null, "female", "la"),
    ];

    [Fact]
    public void TheAnswer_IsReadInTheOrderAsked_AVoiceOrAReason()
    {
        var plan = SingerPlan.Read(Answer, Two);

        Assert.Equal(["neutrino/MERROW", null], plan.Singers.Select(s => s.Voice));
        Assert.Equal("neutrino/NOPE is not installed; singing with neutrino/MERROW", plan.Singers[0].Note);
        Assert.Null(plan.Singers[0].Reason);
        Assert.Equal("no installed voice sings Latin (la); installed voices sing ja, en, zh, ko", plan.Singers[1].Reason);
        Assert.Same(Two[1], plan.Singers[1].Asked);
    }

    [Fact]
    public void TheSentenceForALineNobodyCanSing_IsTheReason_AndWhatPlaysThen()
    {
        Assert.Equal(
            "No installed voice can sing this line: no installed voice sings Latin (la); installed voices sing ja, en, zh, ko. The part's instrument plays instead.",
            SingerPlan.NotSung(SingerPlan.Read(Answer, Two).Singers[1].Reason));
        Assert.Equal("No installed voice can sing this line: no voice is installed. The part's instrument plays instead.",
            SingerPlan.NotSung("no voice is installed."));
    }

    [Theory]
    [InlineData("usage: cantoria …", "is not JSON")]
    [InlineData("""{"ready":true}""", "lists no singers")]
    [InlineData("""{"ready":true,"singers":[{"voice":"a/B"}]}""", "1 voice(s), and 2 were asked")]
    public void AnAnswerThatIsNotAPlan_IsAProblemInOneSentence_NotAPlan(string json, string says)
    {
        var (plan, problem) = SingerPlan.Ask(new FakeSinger { PlanJson = json }, Two);
        Assert.Null(plan);
        Assert.Contains(says, problem);
        Assert.StartsWith("fake/voice did not say who would sing: ", problem);
    }

    [Fact]
    public void AnEngineWithNoPlan_AnswersNeither_AndTheCallerSingsAsBefore()
    {
        Assert.Equal((null, null), SingerPlan.Ask(new FakeSinger(), Two));
        var (plan, problem) = SingerPlan.Ask(new FakeSinger { PlanProblem = "too old" }, Two);
        Assert.Null(plan);
        Assert.Equal("too old", problem);
    }

    // ---- The real program's door (CantoriaProgram.Plan), with a script standing in for cantoria ----

    /// <summary>A program that answers <c>plan</c> with <paramref name="answer"/> and
    /// <paramref name="exit"/>, as a script of the platform's shell.</summary>
    internal static string FakeCantoria(string dir, string answer, int exit)
    {
        File.WriteAllText(Path.Combine(dir, "answer.json"), answer);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            string cmd = Path.Combine(dir, "cantoria.cmd");
            File.WriteAllText(cmd, $"@echo off\r\ntype \"%~dp0answer.json\"\r\nexit /b {exit}\r\n");
            return cmd;
        }
        string sh = Path.Combine(dir, "cantoria.sh");
        File.WriteAllText(sh, $"#!/bin/sh\ncat \"$(dirname \"$0\")/answer.json\"\nexit {exit}\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]     // one asked for has no singer: still the plan
    public void TheProgram_IsRunOnce_AndItsPlanRead_WhateverItSaysOfReadiness(int exit)
    {
        string dir = Directory.CreateTempSubdirectory("lilysharp-plan-").FullName;
        try
        {
            var (plan, problem) = SingerPlan.Ask(new CantoriaProgram(FakeCantoria(dir, Answer, exit)), Two);
            Assert.Null(problem);
            Assert.Equal(["neutrino/MERROW", null], plan!.Singers.Select(s => s.Voice));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AProgramFromBeforePlan_OrNoProgramAtAll_IsOneSentence()
    {
        string dir = Directory.CreateTempSubdirectory("lilysharp-plan-").FullName;
        try
        {
            // Exit code 2 is cantoria's usage: what an older one answers to a command it does not know.
            var (plan, problem) = SingerPlan.Ask(new CantoriaProgram(FakeCantoria(dir, "", 2)), Two);
            Assert.Null(plan);
            Assert.Contains("does not know `plan`", problem);

            (plan, problem) = SingerPlan.Ask(new CantoriaProgram(Path.Combine(dir, "none", "cantoria.exe")), Two);
            Assert.Null(plan);
            Assert.StartsWith("cantoria.exe could not be asked who would sing: ", problem);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
