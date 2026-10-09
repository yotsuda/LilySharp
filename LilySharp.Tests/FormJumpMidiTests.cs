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
using LilySharp.Core.Midi;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The MIDI follows a form's jump texts — <c>dc</c>, <c>ds</c>, <c>al fine</c>, <c>al coda</c>
/// — along the route <see cref="FormRoute"/> reads (2026-10-03). Until then the ten navigation
/// marks were drawn, exported to MusicXML as <c>&lt;sound&gt;</c> attributes and written to the
/// LilyPond twin as <c>\jump</c>, and the MIDI played the form's items in written order as if
/// none of them were there (<c>MidiExporter.PlayForm</c>'s old remark: "not yet honored").
/// </summary>
/// <remarks>
/// Each section is four quarters on one pitch, so a play order reads straight off the pitch
/// stream: the expected order is written as section names and mapped through the pitch each
/// section sounds alone, never as hard-coded numbers.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class FormJumpMidiTests
{
    private const string Head =
        "part m { clef treble }\n" +
        "section I { m { a4 a a a | } }\n" +
        "section A { m { c4 c c c | } }\n" +
        "section B { m { d4 d d d | } }\n" +
        "section C { m { e4 e e e | } }\n" +
        "section D { m { f4 f f f | } }\n";

    private const string Tail = "\nscore { staff m }\n";

    private static SyntaxTree Parse(string form) => SyntaxTree.Parse(Head + "form { " + form + " }" + Tail);

    private static int[] Pitches(string form)
        => new MidiExporter().Export(Parse(form)).Tracks[1].Notes.OrderBy(n => n.StartTick).Select(n => n.Pitch).ToArray();

    /// <summary>The pitch stream <paramref name="order"/> (section names) sounds as.</summary>
    private static int[] Expected(string order)
    {
        var pitchOf = new Dictionary<string, int>();
        var result = new List<int>();
        foreach (var name in order.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (!pitchOf.TryGetValue(name, out int pitch))
                pitchOf[name] = pitch = Pitches(name).Distinct().Single(); // a section alone: four notes on one pitch
            result.AddRange(Enumerable.Repeat(pitch, 4));
        }
        return result.ToArray();
    }

    private static void AssertPlays(string order, string form)
        => Assert.Equal(Expected(order), Pitches(form));

    [Fact]
    public void DaCapoAlFine_ReplaysFromTheBeginningAndEndsAtFine()
        => AssertPlays("A B A", "A fine B dc al fine");

    [Fact]
    public void DalSegnoAlCoda_ReplaysFromTheSegnoToTheCodaSignThenTheCoda()
        => AssertPlays("I A B A C", "I segno A to coda B ds al coda coda C");

    [Fact]
    public void ABareDaCapo_ReplaysUpToTheJumpAndGoesOn()
        => AssertPlays("A B A B C", "A B dc C");

    [Fact]
    public void OnTheReplay_ARepeatBlockPlaysOnceAsItsLastPass()
        => AssertPlays("A B A C D A C", "segno |: A [1. B] :| [2. C] fine D ds al fine");

    [Fact]
    public void ADalSegnoWithNoSegno_IsNotFollowed()
        => AssertPlays("A B", "A B ds al fine");

    [Fact]
    public void AnAlCodaWithNoCodaSign_ResumesAfterTheJump()
        => AssertPlays("A B A C", "A to coda B dc al coda C");

    [Fact]
    public void AfterAnAlFine_NothingWrittenAfterTheJumpSounds()
        => AssertPlays("A B A", "A fine B dc al fine C");

    [Fact]
    public void TwoRoutesInOneForm_AreEachFollowed()
        => AssertPlays("A A B C C", "segno A ds al coda coda B segno C ds al fine");

    [Fact]
    public void AOneSidedRepeatInsideTheReplay_RewindsNothing()
        => AssertPlays("A B A B C A B C", "A B :| C dc");

    /// <summary>The D.S. puts back the state the piece had at the segno — the per-part velocity
    /// lane included — so the replayed section sounds as it did the first time, not at the
    /// dynamic the section before the jump left.</summary>
    [Fact]
    public void ADalSegno_RestoresTheStateThePieceHadAtTheSegno()
    {
        var source = "section A { m { c4@f c c c | } } section B { m { d4 d d d | } } section C { m { e4@p e e e | } }"
            + " form { A segno B fine C ds al fine }";
        var notes = new MidiExporter().Export(SyntaxTree.Parse(source)).Tracks[1].Notes.OrderBy(n => n.StartTick).ToList();
        Assert.Equal(16, notes.Count);
        // A f, B f (the lane carries), C p, B again at f — the segno's state, not C's.
        Assert.Equal(Enumerable.Repeat(95, 8).Concat(Enumerable.Repeat(50, 4)).Concat(Enumerable.Repeat(95, 4)), notes.Select(n => n.Velocity));
    }

    /// <summary>PlayForm walks the form once per part; every part follows the same route.</summary>
    [Fact]
    public void EveryPart_FollowsTheRoute()
    {
        var source = "part m { clef treble } part n { clef bass }"
            + " section A { m { c4 c c c | } n { c4 c c c | } } section B { m { d4 d d d | } n { d4 d d d | } }"
            + " form { A fine B dc al fine } score { staff m staff n }";
        var midi = new MidiExporter().Export(SyntaxTree.Parse(source));
        foreach (var part in new[] { "m", "n" })
            Assert.Equal(12, midi.Tracks.Single(t => t.Name == part).Notes.Count);
    }

    /// <summary>The route itself, as stretches of item indices — what the MIDI tests above
    /// observe through the notes.</summary>
    [Theory]
    [InlineData("A B", "0-2")]
    [InlineData("A fine B dc al fine", "0-3 0-1r")]
    [InlineData("I segno A to coda B ds al coda coda C", "0-5 2-3r 7-8")]
    [InlineData("A B dc C", "0-2 0-2r 3-4")]
    [InlineData("A B ds al fine", "0-3")]
    [InlineData("segno A ds al coda coda B segno C ds al fine", "0-2 1-2r 4-7 6-7r")]
    public void TheRoute(string form, string stretches)
    {
        var tree = Parse(form);
        var items = FormWalk.Read(tree.GetNodes<FormDeclarationSyntax>().Single());
        var route = FormRoute.Of(items).Select(s => $"{s.From}-{s.To}{(s.Replay ? "r" : "")}");
        Assert.Equal(stretches, string.Join(" ", route));
    }
}
