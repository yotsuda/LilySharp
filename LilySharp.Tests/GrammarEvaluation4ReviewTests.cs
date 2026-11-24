// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Linq;
using LilySharp.Core.Editing;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Grammar evaluation 4 (2026-11-13), what <c>lysc review</c> said of a big band chart, a choral
/// arrangement and a contemporary chamber piece: jazz voicings, passing notes, spoken notes and a
/// dissonant language were each "a wrong note?", and a chamber group with piano and percussion
/// was a band (Lab sessions/p1226/md/eval4, sessions/p1228/review).
/// </summary>
public class GrammarEvaluation4ReviewTests
{
    // A flute (and more) beside a piano that holds a C major chord, four bars.
    private static string Beside(string parts, string cells, string staves, string row = "", string piano = "<c' e' g'>1")
        => "title \"Study\"\ntempo 4 = 96\ntime 4/4\noctave absolute\n"
            + parts + "\npart pr { instrument piano-right }\npart pl { instrument piano-left }\n"
            + "section A {\n" + cells + "\n"
            + $"  pr {{ {piano}@mf | {piano} | {piano} | {piano} | }}\n  pl {{ c1 | c1 | c1 | c1 | }}\n"
            + (row.Length > 0 ? $"  chords h {{ {row} | {row} | {row} | {row} | }}\n" : "")
            + "}\nform { A }\nscore { " + (row.Length > 0 ? "chords h  " : "") + staves + "  grandStaff { staff pr  staff pl } }\n";

    private const string Flute = "part fl { instrument flute }";

    private static System.Collections.Generic.List<CompositionFinding> Clashes(string book)
        => CompositionChecks.Findings(book).Where(f => f.Id.StartsWith("harmony.", StringComparison.Ordinal)).ToList();

    private static string FluteOver(string bar, string row = "", string piano = "<c' e' g'>1")
        => Beside(Flute, $"  fl {{ {bar}@mf | {bar} | {bar} | {bar} | }}", "staff fl", row, piano);

    /// <summary>The control: F over the piano's E, held, four times.</summary>
    [Fact]
    public void ANoteHeldASemitoneFromTheHarmony_IsStillSaid()
    {
        var found = Clashes(FluteOver("f''1"));
        Assert.Equal("harmony.clash", Assert.Single(found).Id);
        Assert.StartsWith("fl, the melody: 4 times", found[0].Message);
    }

    /// <summary>The major seventh of the chord the row names is the chord's own tone.</summary>
    [Fact]
    public void AToneTheChordRowNames_IsNoWrongNote()
    {
        // E over the piano's F: a major seventh — told with no row, or under a row that says F.
        Assert.Single(Clashes(FluteOver("e''1", piano: "<f' a' c''>1")));
        Assert.Single(Clashes(FluteOver("e''1", "F", "<f' a' c''>1")));
        Assert.Empty(Clashes(FluteOver("e''1", "Fmaj7", "<f' a' c''>1")));
    }

    /// <summary>A quarter come to and left by step is the passing note the finding's own words allow.</summary>
    [Fact]
    public void APassingNote_IsNoWrongNote()
    {
        Assert.Empty(Clashes(FluteOver("e''4 f'' g''2")));
        // Left by leap, it is not one.
        Assert.Single(Clashes(FluteOver("e''4 f'' a''2")));
    }

    /// <summary>A cross head names no pitch: a spoken word, a key click.</summary>
    [Fact]
    public void ACrossHead_IsNoNoteOfTheHarmony()
    {
        Assert.Empty(Clashes(FluteOver("f''1@notehead(cross)")));
        // …and so are the notes a run of that head covers (2026-11-15): one mark, four bars.
        Assert.Empty(Clashes(Beside(Flute, "  fl { f''1@notehead(cross)@mf | f''1 | f''1 | f''1@!notehead | }", "staff fl")));
        Assert.NotEmpty(Clashes(Beside(Flute, "  fl { f''1@notehead(cross)@mf | f''1 | f''1 | f''1 | }", "staff fl")));
    }

    /// <summary>A line two parts play together is said once, of both.</summary>
    [Fact]
    public void OneLineInTwoParts_IsSaidOnce()
    {
        var found = Clashes(Beside(Flute + "\npart ob { instrument oboe }",
            "  fl { f''1@mf | f''1 | f''1 | f''1 | }\n  ob { f'1@mf | f'1 | f'1 | f'1 | }", "staff fl  staff ob"));
        var one = Assert.Single(found);
        Assert.Equal("harmony.clash", one.Id);
        Assert.Equal(["fl", "ob"], one.Parts);
        Assert.StartsWith("fl, ob, the melody: 4 times", one.Message);
    }

    private const string Winds = Flute + "\npart ob { instrument oboe }\npart va { instrument viola }\npart bn { instrument bassoon }";

    /// <summary>Seconds between most of the parts, no one part under all of them: the piece's language, said once as a choice.</summary>
    [Fact]
    public void DissonanceInMostParts_IsALanguage()
    {
        string Quartet(string ob, string bn) => Beside(Winds,
            $"  fl {{ f''1@mf | f''1 | f''1 | f''1 | }}\n  ob {{ {ob}1@mf | {ob}1 | {ob}1 | {ob}1 | }}\n"
            + $"  va {{ aes'1@mf | aes'1 | aes'1 | aes'1 | }}\n  bn {{ {bn}1@mf | {bn}1 | {bn}1 | {bn}1 | }}",
            "staff fl  staff ob  staff va  staff bn", piano: "<c d>1");
        // F against E, A♭ against G: two pairs, neither part under the other pair's.
        var found = Clashes(Quartet("e''", "g"));
        var one = Assert.Single(found);
        Assert.Equal("harmony.dissonant", one.Id);
        Assert.Equal(["fl", "ob", "va", "bn"], one.Parts);
        Assert.Equal(ReviewKind.Style, ReviewNotes.All[one.Id].Kind);
    }

    /// <summary>…but three parts against one harmony are three parts to look at.</summary>
    [Fact]
    public void PartsAgainstOneHarmony_AreEachSaid()
    {
        var found = Clashes(Beside(Winds.Replace("\npart bn { instrument bassoon }", ""),
            "  fl { f''1@mf | f''1 | f''1 | f''1 | }\n  ob { des''1@mf | des''1 | des''1 | des''1 | }\n  va { aes'1@mf | aes'1 | aes'1 | aes'1 | }",
            "staff fl  staff ob  staff va"));
        Assert.Equal(3, found.Count);
        Assert.All(found, f => Assert.Equal("harmony.clash", f.Id));
    }

    private static string Group(string parts, string cells, string staves)
        => "title \"Machines\"\ntempo 4 = 120\ntime 4/4\noctave absolute\n" + parts + "\nsection A {\n" + cells + "\n}\nform { A }\nscore { " + staves + " }\n";

    private static string Bars(string bar, int n = 8) => string.Concat(Enumerable.Repeat(bar + " | ", n));

    /// <summary>A piano and percussion among winds and strings are a chamber group's, not a band's.</summary>
    [Fact]
    public void PianoAndPercussionAmongWindsAndStrings_AreNoBand()
    {
        string Piece(string others, string cells, string staves) => Group(
            others + "\npart pr { instrument piano-right }\npart pl { instrument piano-left }\npart perc { clef percussion }",
            cells + $"\n  pr {{ {Bars("<c' e' g'>2@mf <d' f' a'>")} }}\n  pl {{ {Bars("c2 d")} }}\n"
            + $"  perc {{ {Bars("sn4@mf sn sn sn", 4)} tempo 4 = 90 {Bars("sn4 sn sn sn", 4)} }}",
            staves + "  grandStaff { staff pr  staff pl }  staff perc");
        string chamber = Piece("part fl { instrument flute }\npart cl { instrument clarinet }\npart vn { instrument violin }\npart vc { instrument cello }",
            $"  fl {{ {Bars("g''2@mf a''")} }}\n  cl {{ {Bars("e'2@mf f'")} }}\n  vn {{ {Bars("c''2@mf d''")} }}\n  vc {{ {Bars("c2@mf d")} }}",
            "staff fl  staff cl  staff vn  staff vc");
        Assert.DoesNotContain(CompositionChecks.Findings(chamber), f => f.Id.StartsWith("band.", StringComparison.Ordinal));
        // A horn with piano and drums still is one, and is told of its bass and its tempo.
        string trio = Piece("part sax { instrument alto-sax }", $"  sax {{ {Bars("g'2@mf a'")} }}", "staff sax");
        var found = CompositionChecks.Findings(trio);
        Assert.Contains(found, f => f.Id == "band.no-bass");
        Assert.Contains(found, f => f.Id == "band.tempo-changes");
    }

    /// <summary>An ending that broadens is not a change of tempo in the middle.</summary>
    [Fact]
    public void ABroadEnding_IsNoChangeOfTempo()
    {
        string Rock(int before, int after) => Group(
            "part gtr { instrument guitar midiInstrument \"overdriven guitar\" }\npart bass { instrument bass midiInstrument \"electric bass (finger)\" }\npart kit { clef percussion }",
            $"  gtr {{ {Bars("e'4@f g' b' a'", before)} tempo \"Broadly\" 4 = 80 {Bars("e'4 g' b' a'", after)} }}\n"
            + $"  bass {{ {Bars("e,4@f e, e, e,", 16)} }}\n  kit {{ {Bars("bd4@f sn bd sn", 16)} }}",
            "staff gtr  staff bass  staff kit");
        Assert.DoesNotContain(CompositionChecks.Findings(Rock(14, 2)), f => f.Id == "band.tempo-changes");
        Assert.Contains(CompositionChecks.Findings(Rock(8, 8)), f => f.Id == "band.tempo-changes");
        // Faster at the end is a change.
        Assert.Contains(CompositionChecks.Findings(Rock(14, 2).Replace("4 = 80", "4 = 160")), f => f.Id == "band.tempo-changes");
    }

    /// <summary>A passage marked unis. is one line by the page's own word.</summary>
    [Fact]
    public void APassageMarkedUnison_IsNoFaultOfPartWriting()
    {
        const string Line = "c''4@mfMARK d'' e'' f'' | g''4 f'' e'' d'' | c''4 d'' e'' f'' | g''4 f'' e'' d'' | c''1 |";
        string Choir(string mark) => "title \"Hymn\"\ntempo 4 = 80\ntime 4/4\noctave absolute\n"
            + "part sop { instrument soprano }\npart alt { instrument alto }\npart ten { instrument tenor }\n"
            + $"section A {{\n  sop {{ {Line.Replace("MARK", mark)} }}\n  alt {{ {Line.Replace("MARK", "")} }}\n  ten {{ c'1@mf | b1 | c'1 | b1 | c'1 | }}\n}}\n"
            + "form { A }\nscore { choirStaff { staff sop  staff alt  staff ten } }\n";
        var plain = CompositionChecks.Findings(Choir(""));
        Assert.Contains(plain, f => f.Id == "singers.octaves" && f.Parts.SequenceEqual(["sop", "alt"]));
        Assert.Contains(plain, f => f.Id == "singers.parallels" && f.Message.Contains("sop and alt in unisons"));
        var marked = CompositionChecks.Findings(Choir("@text(\"unis.\").up"));
        Assert.DoesNotContain(marked, f => f.Id == "singers.octaves");
        Assert.DoesNotContain(marked, f => f.Id == "singers.parallels" && f.Message.Contains("sop and alt"));
    }

    // ── session 1375: the three left from session 1228 (the big band's horns, its blues fourth, a piece that changes key) ──

    /// <summary>Parts that strike with a higher part play the harmony under it: their wrong notes are
    /// still told, but they are not called the melody, nor held to a melody's rhythm.</summary>
    [Fact]
    public void PartsUnderALead_AreNotTheMelody()
    {
        string Trio(string ob) => Beside(Winds.Replace("\npart bn { instrument bassoon }", ""),
            $"  fl {{ f''1@mf | f''1 | f''1 | f''1 | }}\n  ob {{ {ob} }}\n  va {{ aes'1@mf | aes'1 | aes'1 | aes'1 | }}",
            "staff fl  staff ob  staff va");
        var found = Clashes(Trio("des''1@mf | des''1 | des''1 | des''1 |"));
        Assert.Equal(3, found.Count);
        Assert.Single(found, f => f.Message.StartsWith("fl, the melody: 4 times", StringComparison.Ordinal));
        Assert.Single(found, f => f.Message.StartsWith("ob: 4 times", StringComparison.Ordinal));
        Assert.Single(found, f => f.Message.StartsWith("va: 4 times", StringComparison.Ordinal));
        // An oboe with a rhythm of its own leads a line of its own.
        var own = Clashes(Trio("r2 des''2@mf | r2 des''2 | r2 des''2 | r2 des''2 |"));
        Assert.Single(own, f => f.Message.StartsWith("ob, the melody:", StringComparison.Ordinal));
        // Half its notes with the flute, half alone: under it less than two thirds of the time, it leads.
        var half = Clashes(Trio("des''2@mf des''2 | des''2 des''2 | des''2 des''2 | des''2 des''2 |"));
        Assert.Single(half, f => f.Message.StartsWith("ob, the melody:", StringComparison.Ordinal));
    }

    /// <summary>In a piece that swings, the blues scale's fourth over the tonic's seventh chord is the language's own.</summary>
    [Fact]
    public void TheBluesFourth_InAPieceThatSwings_IsNoWrongNote()
    {
        // B♭ held over the piano's F7, a minor ninth over its A: the riff of a swing band in F.
        string book = FluteOver("bes''1", piano: "<f' a' ees''>1").Replace("tempo 4 = 96", "tempo 4 = 96\nkey f major");
        Assert.Single(Clashes(book));                                                // straight eighths: told
        Assert.Empty(Clashes(book.Replace("tempo 4 = 96", "tempo 4 = 96 swing")));   // swung: the blues
        Assert.Contains(10, CompositionChecks.SwungBlueNotes(LilySharp.Core.Syntax.SyntaxTree.Parse(book.Replace("tempo 4 = 96", "tempo 4 = 96 swing")).GetRoot())!);   // B♭, F's fourth
    }

    private const string InE = "e'4 fis' gis' a' | b' cis'' dis'' e'' | e''4 dis'' cis'' b' | a' gis' fis' e' |";

    private static string Modulating(string second, string music = InE) => $$"""
        title "Up a step"
        tempo 4 = 96
        time 4/4
        octave absolute
        key d major
        part fl { instrument flute }
        section A {
          fl { d'4@mf e' fis' g' | a' b' cis'' d'' | d''4 cis'' b' a' | g' fis' e' d' | }
        }
        section B {
          {{second}}
          fl { {{music}} }
        }
        form { A B }
        score { staff fl }
        """;

    /// <summary>A piece whose written key changes is heard stretch by stretch, in the order played.</summary>
    [Fact]
    public void APieceThatChangesKey_IsHeardKeyByKey()
    {
        static string Heard(string book) => (string)System.Text.Json.Nodes.JsonNode.Parse(ScoreReview.Json(book))!["facts"]!["keyHeard"]!;
        Assert.Matches(@"^D major \(\d\.\d\d\) → E major \(\d\.\d\d\)$", Heard(Modulating("key e major")));
        // The same notes with B in the file's key, D: one key for the whole, its two best fits.
        Assert.Contains(" / ", Heard(Modulating("")));
        // A key written in B whose notes stay in D: two stretches heard alike are one, and the whole is said as one key.
        Assert.Contains(" / ", Heard(Modulating("key g major", "d''4 cis'' b' a' | g' fis' e' d' | e'4 fis' g' a' | b' cis'' d'' d'' |")));
    }

}
