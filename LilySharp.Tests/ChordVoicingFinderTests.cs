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

using LilySharp.Core.Editing;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>Owner's request 2026-10-30: a composed guitar part wrote a chord no hand could hold. A
/// tool answers the voicings a player can hold ("Cm7, on the guitar, round here"), and a warning
/// names a chord that cannot be (LYS1055); a bowed string sounds two strings at once.</summary>
public class ChordVoicingFinderTests
{
    [Fact]
    public void TheGuitarsVoicings_AreTheDiagramsShapes_InTheRegister_SpelledAsTheChordIs()
    {
        var answer = ChordVoicingFinder.Find("Cm7", "guitar", "C3", "C5", 6);
        Assert.Null(answer.Problem);
        Assert.Contains(answer.Voicings, v => v.Diagram == "x35343" && v.Chord == "<c g bes ees' g'>");
        Assert.All(answer.Voicings, v => Assert.True(v.Pitches[0] >= 48 && v.Pitches[^1] <= 72));
        // Every one of them can be held — the warning's own test agrees.
        var strings = LilySharp.Core.Tablature.Tunings.Guitar;
        Assert.All(answer.Voicings, v => Assert.NotNull(ChordFingeringValidator.Fingering(v.Pitches, strings)));
        Assert.Contains("x35343", ChordVoicingFinder.Describe("Cm7", "guitar", "C3", "C5"));
    }

    // Owner's request 2026-11-01: a composed guitar part held `<g, d g g d'>` — the diagrams' G
    // shape 35503x sounds G3 on two strings — and the doubled note reads as a mistake on the page.
    [Theory]
    [InlineData("G", "guitar")]
    [InlineData("D", "guitar")]
    [InlineData("C", "guitar")]
    [InlineData("Am", "guitar")]
    [InlineData("E", "guitar")]
    [InlineData("G", "bass")]
    [InlineData("C", "ukulele")]
    public void NoVoicingSoundsOnePitchOnTwoStrings(string chord, string instrument)
    {
        var answer = ChordVoicingFinder.Find(chord, instrument, null, null, 12);
        Assert.NotEmpty(answer.Voicings);
        Assert.All(answer.Voicings, v => Assert.Equal(v.Pitches.Count, v.Pitches.Distinct().Count()));
        Assert.DoesNotContain(ChordVoicingFinder.Find("G", "guitar", null, null, 12).Voicings, v => v.Diagram == "35503x");
    }

    // Owner's report 2026-11-01: `Downloads/Redline.lys`, an overdriven guitar in `<e, e b e' b'>` —
    // this tool's first E5 shape — from start to end. On a distorted sound a full chord blurs and two
    // or three notes ring clear: given the part's sound, the power chords come first.
    [Fact]
    public void OnADistortedSound_ThePowerChordsComeFirst()
    {
        var e5 = ChordVoicingFinder.Find("E5", "guitar", null, null, 6, sound: "overdriven guitar");
        Assert.True(e5.Distorted && e5.Powered);
        Assert.Equal(["<e, b,>", "<e, b, e>", "<e b>", "<e b e'>"], e5.Voicings.Take(4).Select(v => v.Chord));
        Assert.Equal(["02xxxx", "022xxx", "xx24xx", "xx245x"], e5.Voicings.Take(4).Select(v => v.Diagram));
        Assert.All(e5.Voicings, v => Assert.True(v.Pitches.Count <= 3));
        Assert.All(e5.Voicings, v => Assert.NotNull(ChordFingeringValidator.Fingering(v.Pitches, LilySharp.Core.Tablature.Tunings.Guitar)));
        // A minor chord's power chord drops its third; the register picks the position.
        var am = ChordVoicingFinder.Find("Am", "guitar", "A2", "A3", 6, sound: "distortion guitar");
        Assert.Equal("<a, e>", am.Voicings[0].Chord);
        Assert.Equal("x02xxx", am.Voicings[0].Diagram);
        Assert.Equal("<a, e a>", am.Voicings[1].Chord);
        // The full shapes follow the power chords; a chord with no perfect fifth has none, and the text says so.
        Assert.Contains(ChordVoicingFinder.Find("E", "guitar", null, null, 40, sound: "overdriven guitar").Voicings, v => v.Pitches.Count >= 4);
        string dim = ChordVoicingFinder.Describe("Bdim", "guitar", null, null, 6, "overdriven guitar");
        Assert.Contains("no perfect fifth over its root, so no power chord", dim);
        Assert.Contains("its power chords first", ChordVoicingFinder.Describe("E", "guitar", null, null, 6, "overdriven guitar"));
        // A clean sound, or none given, keeps the full shapes first; a slash chord has no power chord.
        Assert.Equal("<e, e b e' b'>", ChordVoicingFinder.Find("E5", "guitar", null, null, 6, "electric guitar (clean)").Voicings[0].Chord);
        Assert.Equal("<e, e b e' b'>", ChordVoicingFinder.Find("E5", "guitar", null, null, 6).Voicings[0].Chord);
        Assert.False(ChordVoicingFinder.Find("C/E", "guitar", null, null, 6, "overdriven guitar").Powered);
    }

    // Owner's request 2026-11-10: a band's clean guitar plays its chords on the upper strings. The
    // diagrams' list (the root lowest, every shape maximal) had no A minor on the top four.
    [Fact]
    public void OnTheStringsAsked_TheShapesLieThere_TheRootNotAlwaysLowest()
    {
        var am = ChordVoicingFinder.Find("Am", "guitar", null, null, 12, "electric guitar (clean)", "1-4");
        Assert.Null(am.Problem);
        Assert.All(am.Voicings, v => Assert.True(v.Frets[0] < 0 && v.Frets[1] < 0, v.Diagram));
        Assert.Contains(am.Voicings, v => v.Diagram == "xx2210" && v.Chord == "<e a c' e'>");
        Assert.Contains(am.Voicings, v => v.Diagram == "xx7555");
        var c = ChordVoicingFinder.Find("C", "guitar", null, null, 12, null, "1-4");
        Assert.Contains(c.Voicings, v => v.Diagram == "xx2010");
        Assert.Contains(c.Voicings, v => v.Diagram == "xx5553");
        // Every one can be held, and sounds no pitch twice.
        Assert.All(am.Voicings.Concat(c.Voicings), v => Assert.NotNull(ChordFingeringValidator.Fingering(v.Pitches, LilySharp.Core.Tablature.Tunings.Guitar)));
        Assert.All(am.Voicings.Concat(c.Voicings), v => Assert.Equal(v.Pitches.Count, v.Pitches.Distinct().Count()));
        // The low positions first; the root lowest before an inversion at the same place.
        Assert.True(am.Voicings[0].Frets.Where(f => f > 0).DefaultIfEmpty(0).Min() <= 2, am.Voicings[0].Diagram);
        // E has none on the five highest strings with its root lowest; with the root free it has.
        Assert.NotEmpty(ChordVoicingFinder.Find("E", "guitar", null, null, 6, null, "1-5").Voicings);
        // A slash chord keeps its bass lowest.
        Assert.All(ChordVoicingFinder.Find("C/G", "guitar", null, null, 12, null, "1-4").Voicings, v => Assert.Equal(7, v.Pitches[0] % 12));
        // Strings in any order, or a list; every string is as if none were asked.
        Assert.Equal(ChordVoicingFinder.Find("Am", "guitar", null, null, 12, null, "4-1").Voicings.Select(v => v.Diagram),
            am.Voicings.Select(v => v.Diagram));
        Assert.All(ChordVoicingFinder.Find("G", "guitar", null, null, 12, null, "1,2,3").Voicings, v => Assert.True(v.Frets.Take(3).All(f => f < 0), v.Diagram));
        Assert.Equal(ChordVoicingFinder.Find("G", "guitar", null, null, 12).Voicings.Select(v => v.Diagram),
            ChordVoicingFinder.Find("G", "guitar", null, null, 12, null, "1-6").Voicings.Select(v => v.Diagram));
        string text = ChordVoicingFinder.Describe("Am", "guitar", null, null, 6, "electric guitar (clean)", "1-4");
        Assert.Contains("on strings 1-4", text);
        Assert.Contains("the root need not be lowest, as a band's bass plays it", text);
    }

    [Fact]
    public void StringsAskedWrongly_OrOfABowedString_AreRefused()
    {
        Assert.Contains("numbered from 1, the highest, to 6", ChordVoicingFinder.Find("Am", "guitar", null, null, 6, null, "0-4").Problem);
        Assert.Contains("numbered from 1, the highest, to 6", ChordVoicingFinder.Find("Am", "guitar", null, null, 6, null, "1-7").Problem);
        Assert.Contains("numbered from 1, the highest, to 6", ChordVoicingFinder.Find("Am", "guitar", null, null, 6, null, "top").Problem);
        Assert.Contains("a fretted instrument's strings", ChordVoicingFinder.Find("Cm7", "violin", null, null, 6, null, "1-2").Problem);
        // On a distorted sound the power chords on those strings come first.
        var e5 = ChordVoicingFinder.Find("E", "guitar", null, null, 6, "overdriven guitar", "1-4");
        Assert.True(e5.Voicings[0].Pitches.Count <= 3 && e5.Voicings[0].Frets[0] < 0 && e5.Voicings[0].Frets[1] < 0, e5.Voicings[0].Diagram);
    }

    [Fact]
    public void ABowedString_AnswersDoubleStops_OnNeighbouringStrings()
    {
        var answer = ChordVoicingFinder.Find("Cm7", "violin", "G3", "D5", 6);
        Assert.True(answer.Bowed);
        Assert.All(answer.Voicings, v =>
        {
            Assert.Equal(2, v.Pitches.Count);
            int first = v.Frets.ToList().FindIndex(f => f >= 0);
            Assert.True(v.Frets[first + 1] >= 0);   // the next string up
        });
        Assert.Contains("two strings at once", ChordVoicingFinder.Describe("Cm7", "cello", null, null));
    }

    [Fact]
    public void AnUnknownChordOrInstrument_SaysWhy()
    {
        Assert.Contains("not a chord symbol", ChordVoicingFinder.Describe("Xyz", "guitar", null, null));
        Assert.Contains("no string instrument", ChordVoicingFinder.Describe("C", "piano", null, null));
    }

    static IEnumerable<Diagnostic> Fingered(string instrument, string chord) => SemanticValidation.Run(SyntaxTree.Parse(
        $"octave absolute\npart p {{ instrument {instrument} }}\nsection A {{ p {{ {chord}1 | }} }}\nform {{ A }}\n"))
        .Where(d => d.Code == DiagnosticCodes.ChordNotFingerable);

    [Fact]
    public void AChordNoHandCanHold_IsWarned_WithOneThatCan()
    {
        // G2 B2 D3 F3: F3 has no string left within reach.
        Assert.Contains(Fingered("guitar", "<g, b, d f>"), d => d.Message.StartsWith("this chord (G2 B2 D3 F3) cannot be fingered")
            && d.Message.Contains("a voicing of its notes a player can hold"));
        Assert.Empty(Fingered("guitar", "<g b d' f'>"));    // xx5431
        Assert.Empty(Fingered("guitar", "<c g bes ees' g'>")); // x35343
    }

    /// <summary>Owner's request 2026-10-30: a bare <c>@diagram</c> on a <c>&lt;…&gt;</c> chord draws the
    /// chord's own notes as the part's strings fret them — on the page and in the twin, no warning;
    /// a chord no hand can hold draws none (LYS1055 says why).</summary>
    [Fact]
    public void ABareDiagramOnAChord_DrawsItsOwnNotesFretted()
    {
        const string book = "octave absolute\npart gt { instrument guitar }\n"
            + "section A { gt { <g b d' f'>1@diagram | <c e g c'>1@diagram | <g, b, d f>1@diagram | } }\nform { ~A }\nscore { staff gt }\n";
        var tree = SyntaxTree.Parse(book);
        Assert.DoesNotContain(SemanticValidation.Run(tree), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        var score = LilySharp.Core.Svg.SvgGenerator.CollectScore(tree, LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(tree));
        var frames = score.Articulations.Where(a => a.Type == ArticulationType.FretFrame)
            .OrderBy(a => a.SourcePosition).Select(a => a.FrameSpec).ToList();
        Assert.Equal(new[] { "xx5431", "x3201x" }, frames);
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(tree);
        Assert.Contains("\\fret-diagram-terse \"x;x;5;4;3;1;\"", ly);
        Assert.Contains("\\fret-diagram-terse \"x;3;2;o;1;x;\"", ly);
        // On a single note it draws where that note is held, its lowest fret (owner's decision
        // 2026-10-31; it was unknown until then): C4 is the B string's first fret.
        var single = SyntaxTree.Parse("octave absolute\npart gt { instrument guitar }\nsection A { gt { c'1@diagram | } }\nform { A }\nscore { staff gt }\n");
        Assert.DoesNotContain(SemanticValidation.Run(single), d => d.Code == DiagnosticCodes.UnknownAnnotation);
        Assert.Equal("xxxx1x", Assert.Single(LilySharp.Core.Svg.SvgGenerator.CollectScore(single,
            LilySharp.Core.Svg.Collector.RenderSpecParser.FindFirst(single)).Articulations,
            a => a.Type == ArticulationType.FretFrame).FrameSpec);
    }

    [Fact]
    public void ABowedStringsChord_OfMoreThanTwo_OrFarApart_IsWarned()
    {
        Assert.Contains(Fingered("violin", "<g d' b'>"), d => d.Message.Contains("sounds two strings together, not 3"));
        Assert.Empty(Fingered("violin", "<g d'>"));
        Assert.Empty(Fingered("violin", "<b g'>"));          // G string +4, D string +5
        Assert.Contains(Fingered("violin", "<a e''>"), d => d.Message.Contains("do not lie on neighbouring strings"));
    }

    // Owner's report 2026-11-11: asked for "high chords with the root left out", the composing chat
    // had a tool that answered rooted shapes from the nut, asked it eight times and sifted by hand.
    [Fact]
    public void WithoutTheRoot_NoShapeSoundsIt_AndFromAFret_NoneLiesBelowIt()
    {
        var cm = ChordVoicingFinder.Find("Cm7", "guitar", null, null, 12, "electric guitar (clean)", "1-4", omitRoot: true, fromFret: 5);
        Assert.Null(cm.Problem);
        Assert.False(cm.RootKept);
        Assert.NotEmpty(cm.Voicings);
        Assert.All(cm.Voicings, v => Assert.DoesNotContain(v.Pitches, p => p % 12 == 0));
        Assert.All(cm.Voicings, v => Assert.All(v.Frets, f => Assert.True(f < 0 || f >= 5, v.Diagram)));
        Assert.Contains(cm.Voicings, v => v.Diagram == "xx5886" && v.Chord == "<g ees' g' bes'>");
        // Two TONES at least, never one tone and its octave.
        Assert.All(cm.Voicings, v => Assert.True(v.Pitches.Select(p => p % 12).Distinct().Count() >= 2, v.Chord));
        // Every one can be held.
        Assert.All(cm.Voicings, v => Assert.NotNull(ChordFingeringValidator.Fingering(v.Pitches, LilySharp.Core.Tablature.Tunings.Guitar)));
        // A power chord is its root: none without it.
        Assert.False(ChordVoicingFinder.Find("E", "guitar", null, null, 6, "overdriven guitar", null, omitRoot: true).Powered);
        // With no strings named, the whole neck is searched from the fret.
        var cm7 = ChordVoicingFinder.Find("Cm7", "guitar", null, null, 12, null, null, omitRoot: false, fromFret: 7);
        Assert.All(cm7.Voicings, v => Assert.All(v.Frets, f => Assert.True(f < 0 || f >= 7, v.Diagram)));
    }

    // Owner's decision 2026-11-11: without its root a triad is two notes, and the answer that said
    // so cost a second call. A triad asked without its root keeps it, on the upper strings.
    [Fact]
    public void ATriadAskedWithoutItsRoot_KeepsIt_OnTheUpperStrings()
    {
        var bb = ChordVoicingFinder.Find("Bb", "guitar", null, null, 12, null, null, omitRoot: true, fromFret: 5);
        Assert.True(bb.RootKept);
        Assert.Equal("xxx766", bb.Voicings[0].Diagram);
        Assert.All(bb.Voicings, v => Assert.Contains(v.Pitches, p => p % 12 == 10));
        Assert.All(bb.Voicings, v => Assert.Equal(3, v.Pitches.Count));
        Assert.All(bb.Voicings, v => Assert.All(v.Frets.Take(3), f => Assert.Equal(-1, f)));
        // The strings named are the strings searched.
        Assert.Equal("xx8766", ChordVoicingFinder.Find("Bb", "guitar", null, null, 12, null, "1-4", omitRoot: true, fromFret: 5).Voicings[0].Diagram);
        // A suspended chord is three tones too; a seventh keeps three without its root.
        Assert.True(ChordVoicingFinder.Find("Csus4", "guitar", null, null, 6, null, null, omitRoot: true).RootKept);
        Assert.False(ChordVoicingFinder.Find("D7", "guitar", null, null, 6, null, null, omitRoot: true).RootKept);
        // Not asked without the root, nothing is read anew.
        Assert.False(ChordVoicingFinder.Find("Bb", "guitar", null, null, 6).RootKept);
        string text = ChordVoicingFinder.Describe("Bb", "guitar", null, null, 6, null, null, omitRoot: true, fromFret: 5);
        Assert.Contains("on strings 1-3", text);
        Assert.Contains("KEEP the root", text);
        Assert.DoesNotContain("the root left to the bass", text);
        Assert.DoesNotContain("no perfect fifth", ChordVoicingFinder.Describe("E", "guitar", null, null, 6, "overdriven guitar", null, omitRoot: true));
    }

    // Owner's correction and question 2026-11-11: the answer wrote `xx(12)(10)x(10)`, which nothing
    // reads; and a shape strummed across a string muted between its sounding ones is no common grip.
    [Fact]
    public void ADiagramIsSpelledAsLilySharpReadsIt_AndAShapeMutedBetweenComesAfterTheOthers()
    {
        string text = ChordVoicingFinder.Describe("C7", "guitar", null, null, 12, null, "1-4", omitRoot: true, fromFret: 5);
        Assert.Contains("diagram(xx-14-15-x-12) (a string muted between the sounding ones", text);
        Assert.DoesNotContain("(12)", text);
        Assert.True(text.IndexOf("diagram(xx8986)", StringComparison.Ordinal) < text.IndexOf("diagram(xx5x56)", StringComparison.Ordinal));
        // …in every list: the shapes on neighbouring strings all come before the first one muted between.
        foreach (string chord in new[] { "Gm", "Eb", "F", "C7" })
        {
            var shapes = ChordVoicingFinder.Find(chord, "guitar", null, null, 12, null, "1-4", omitRoot: true, fromFret: 5).Voicings;
            int firstMuted = shapes.ToList().FindIndex(v => ChordVoicingFinder.MutedBetween(v.Frets));
            Assert.True(firstMuted < 0 || shapes.Skip(firstMuted).All(v => ChordVoicingFinder.MutedBetween(v.Frets)), chord);
            Assert.False(ChordVoicingFinder.MutedBetween(shapes[0].Frets), chord);
        }
        Assert.True(ChordVoicingFinder.MutedBetween([-1, -1, 12, 10, -1, 10]));
        Assert.False(ChordVoicingFinder.MutedBetween([-1, -1, -1, 7, 6, -1]));
        // What it writes, the diagram's reader takes back.
        var voicing = ChordVoicingFinder.Find("Bb", "guitar", null, null, 12, null, "1-4", omitRoot: true, fromFret: 5)
            .Voicings.First(v => v.Frets.Any(f => f > 9));
        Assert.Equal(LilySharp.Core.Music.ChordVoicings.Spell(voicing.Frets), voicing.Diagram);
    }

    // `Bbmaj` is B-flat major SEVENTH here: the answer says the tones it read, so that it is not asked twice.
    [Fact]
    public void TheAnswerNamesTheTonesItReadTheSymbolAs()
    {
        Assert.Contains("Bbmaj — its tones bes d f a", ChordVoicingFinder.Describe("Bbmaj", "guitar", null, null));
        Assert.Contains("Bb — its tones bes d f —", ChordVoicingFinder.Describe("Bb", "guitar", null, null));
        Assert.Contains("the root left to the bass", ChordVoicingFinder.Describe("Cm7", "guitar", null, null, 6, null, "1-4", omitRoot: true));
    }

    // One call for a part's chords: a voicing each, the hand moving little.
    [Fact]
    public void AProgression_AnswersOneVoicingAChord_NearItsNeighbours()
    {
        string[] chords = ["Bb", "Gm", "Eb", "F", "D7", "C7", "Ebm", "Cm7"];
        string text = ChordVoicingFinder.DescribeProgression(chords, "guitar", null, null, "electric guitar (clean)", "1-4", omitRoot: true, fromFret: 5);
        var lines = text.Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")).ToList();
        Assert.Equal(8, lines.Count);
        for (int i = 0; i < chords.Length; i++)
        {
            Assert.StartsWith($"{i + 1}. {chords[i]} (", lines[i]);
            Assert.Contains("diagram(", lines[i]);
        }
        // No shape of the path is strummed across a muted string where one on neighbouring strings was to be had.
        Assert.DoesNotContain("-x-", string.Join("\n", lines));
        // The same chord twice in a row keeps its shape.
        string twice = ChordVoicingFinder.DescribeProgression(["Am", "Am", "Dm"], "guitar", null, null, null, "1-4");
        var two = twice.Split('\n').Where(l => l.StartsWith("1. ") || l.StartsWith("2. ")).Select(l => l[(l.IndexOf('<'))..]).ToList();
        Assert.Equal(two[0], two[1]);
        // A chord that comes more than once takes ONE shape (a part writes it once and calls it by
        // name), and the hand stays in a few frets: no shape of this song's part starts above fret 9.
        string[] song = ["Bb", "Gm", "Eb", "F", "Bb", "D7", "Gm", "C7", "Eb", "Ebm", "Bb", "F", "Gm", "Eb", "Cm7", "F", "Bb"];
        var sung = ChordVoicingFinder.DescribeProgression(song, "guitar", null, null, "electric guitar (clean)", "1-3", omitRoot: false, fromFret: 5)
            .Split('\n').Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")).ToList();
        var byChord = sung.Select((l, i) => (Chord: song[i], Shape: l[l.IndexOf('<')..])).GroupBy(x => x.Chord).ToList();
        Assert.All(byChord, g => Assert.Single(g.Select(x => x.Shape).Distinct()));
        // The upper-string triads a band's guitar plays, the root in an upper voice.
        Assert.Contains(sung, l => l.StartsWith("1. Bb ") && l.Contains("<d' f' bes'> — diagram(xxx766)"));
        Assert.Contains(sung, l => l.Contains("Gm ") && l.Contains("diagram(xxx786)"));
        Assert.Contains(sung, l => l.Contains("F ") && l.Contains("diagram(xxx565)"));
        // Asked without the root, the song's triads keep it and its sevenths leave it out: the
        // same shapes in ONE call, and the answer says which is which.
        var kept = ChordVoicingFinder.DescribeProgression(song, "guitar", null, null, "electric guitar (clean)", null, omitRoot: true, fromFret: 5);
        Assert.Contains("Bb (bes d f) — <d' f' bes'> — diagram(xxx766) (a triad: its root kept)", kept);
        Assert.Contains("D7 (d fis a c) — <a c' fis' a'> — diagram(xx7575)\n", kept);
        Assert.Contains("KEEP the root", kept);
        Assert.DoesNotContain("KEEP the root", ChordVoicingFinder.DescribeProgression(["D7", "Cm7"], "guitar", null, null, null, "1-4", omitRoot: true, fromFret: 5));
        // Owner's run 2026-11-11 16:59: "high chords" asked with no fret named came back in the low
        // positions and were written as a high position. The answer says where on the neck it lies.
        string low = ChordVoicingFinder.DescribeProgression(["Bb", "Gm", "Eb", "F"], "guitar", "F3", "D5", null, "1-4", omitRoot: true);
        Assert.Matches(@"These shapes lie in frets [1-4]–\d+: the low positions\. For shapes up the neck \(""high chords"", a higher position\), ask again with `fromFret` \(5, or 7\)\.", low);
        string up = ChordVoicingFinder.DescribeProgression(["Bb", "Gm", "Eb", "F"], "guitar", null, null, null, "1-4", omitRoot: true, fromFret: 5);
        Assert.Matches(@"These shapes lie in frets [5-9]–\d+\.\n", up);
        Assert.DoesNotContain("the low positions", up);
        // Owner's run 2026-11-11: a register no four-string shape fits (C4–C5) was answered with
        // shapes from B♭3, and nothing said so. Each such line says it, and the answer once.
        string narrow = ChordVoicingFinder.DescribeProgression(["Bb", "Gm", "F"], "guitar", "C4", "C5", null, "1-4", omitRoot: true, fromFret: 3);
        Assert.Contains("(a triad: its root kept) (outside the register asked, ", narrow);
        Assert.Contains(": no shape of it lies wholly in it there)", narrow);
        Assert.Contains("⚠ The register asked holds no shape of the chords so marked", narrow);
        // A register the shapes fit is not spoken of, nor is none.
        string fits = ChordVoicingFinder.DescribeProgression(["Bb", "Gm", "F"], "guitar", "C4", "C5", null, "1-3", omitRoot: true, fromFret: 3);
        Assert.DoesNotContain("outside the register", fits);
        Assert.DoesNotContain("The register asked holds no shape", fits);
        Assert.DoesNotContain("outside the register", ChordVoicingFinder.DescribeProgression(["Bb", "Gm", "F"], "guitar", null, null, null, "1-4"));
        // A symbol that is no chord says so on its own line and breaks nothing after it.
        string broken = ChordVoicingFinder.DescribeProgression(["C", "Xyz", "G"], "guitar", null, null);
        Assert.Contains("2. Xyz — no voicing", broken);
        Assert.Contains("3. G (g b d)", broken);
    }
}
