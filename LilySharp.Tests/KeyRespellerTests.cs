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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Editing;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

[Trait("Category", "Unit")]
public class KeyRespellerTests
{
    private static string Doc(string a, string b = "c'1 |", string mode = "octave absolute") => $$"""
        {{mode}}
        time 4/4
        part melody { clef treble }
        section A { melody { {{a}} } }
        section B { melody { {{b}} } }
        form { A B }
        score { staff melody }
        """;

    private static List<int> Sounding(string source)
        => new MeasureCollector().Collect(SyntaxTree.Parse(source), "melody").Voice.Measures
            .SelectMany(m => m.Items)
            .SelectMany(i => i switch
            {
                NoteItem n => new[] { n.Midi },
                ChordItem c => c.Notes.Select(x => x.Midi).ToArray(),
                _ => [],
            })
            .ToList();

    private static string Respell(string source, string key, IReadOnlyCollection<string>? accepted = null)
    {
        var result = KeyRespeller.Respell(source, "A", key, accepted);
        Assert.True(result.NewText != null, result.Error);
        Assert.False(SyntaxTree.Parse(result.NewText!).HasErrors);
        Assert.Equal(Sounding(source), Sounding(result.NewText!));
        return result.NewText!;
    }

    [Fact]
    public void SharpsWrittenInGFlatMajor_BecomeTheScalesFlats()
    {
        var text = Respell(Doc("fis'4 gis' ais' b' | cis''4 dis'' f'' fis'' | fis'1 |"), "ges major");
        Assert.Contains("ges'4 aes' bes' ces'' | des''4 ees'' f'' ges'' | ges'1 |", text);
    }

    [Fact]
    public void RelativeMode_KeepsEveryNoteWhereItSounded()
    {
        // b -> ces crosses the octave, and every respelled letter moves the frame the next
        // relative note reads; the marks are recomputed so nothing moves.
        var source = Doc("ges'4 b ais fis | b2 cis | dis1 |", mode: "");
        var text = Respell(source, "ges major");
        Assert.DoesNotContain("ais", text);
        Assert.Contains("ces", text);
    }

    [Fact]
    public void OnlyTheSectionIsRespelled()
    {
        var text = Respell(Doc("fis'1 |", "fis'1 |"), "ges major");
        Assert.Contains("section A { melody { ges'1 | } }", text);
        Assert.Contains("section B { melody { fis'1 | } }", text);
    }

    [Fact]
    public void ChromaticNote_SpelledOneWay_IsLeftAlone()
        => Assert.Empty(KeyRespeller.Plan(Doc("c'4 aes' g' c'' |"), "A", "c major"));

    [Fact]
    public void ChromaticNote_SpelledTwoWays_IsUnifiedToTheMoreWritten()
    {
        var text = Respell(Doc("fis'4 g' ges' fis' |"), "c major");
        Assert.Contains("fis'4 g' fis' fis' |", text);
    }

    [Fact]
    public void MinorKey_TieGoesToTheRaisedSeventh()
    {
        var text = Respell(Doc("a'4 gis' aes' a' |"), "a minor");
        Assert.Contains("a'4 gis' gis' a' |", text);
    }

    [Fact]
    public void AcceptedList_LimitsTheChanges()
    {
        var source = Doc("fis'4 cis'' fis' cis'' |");
        var plan = KeyRespeller.Plan(source, "A", "ges major");
        Assert.Equal(new[] { "cis>des", "fis>ges" }, plan.Select(c => c.Id).OrderBy(x => x));
        var text = Respell(source, "ges major", new[] { "fis>ges" });
        Assert.Contains("ges'4 cis'' ges' cis'' |", text);
    }

    [Fact]
    public void NotesAfterAKeyChangeInTheSection_AreLeftAlone()
    {
        var text = Respell(Doc("fis'2 fis' | key d major fis'1 |"), "ges major");
        Assert.Contains("ges'2 ges' | key d major fis'1 |", text);
    }

    [Fact]
    public void AlreadySpelled_SaysSo()
    {
        var result = KeyRespeller.Respell(Doc("ges'1 |"), "A", "ges major");
        Assert.Null(result.NewText);
        Assert.Contains("already", result.Error);
    }
}
