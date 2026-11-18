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

using System.Text;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// ABC notation read into Lily# (owner's decision 2026-11-18, <c>AbcReader</c>,
/// <c>docs/abc-import-design.md</c>). What comes back must compile clean and SOUND what a
/// hand-written book of the same tune sounds; how it is spelled and laid out is asked apart.
/// </summary>
public class AbcImportTests
{
    private static (string Lys, ImportReport Report) Import(string abc)
        => new MusicXmlImporter().ImportBytes(Encoding.UTF8.GetBytes(abc));

    private static List<(int Tick, int Pitch, int Ticks)> Sounds(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics.Select(d => d.Message)) + "\n" + source);
        Assert.Empty(SemanticValidation.Run(tree).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message + "\n" + source));
        var spec = RenderSpecParser.FindFirst(tree);
        var midi = new MidiExporter { Form = spec?.Form, Score = spec }.Export(tree);
        return [.. midi.Tracks.SelectMany(t => t.Notes).Select(n => (n.StartTick, n.Pitch, n.DurationTicks)).Order()];
    }

    private static string Book(string music, string head = "time 4/4\nkey c major")
        => $"octave absolute\ntempo 120\n{head}\npart p {{ }}\nsection A {{ p {{ {music} }} }}\nform {{ A }}\nscore {{ staff p }}\n";

    private static string Tune(string body, string head = "M:4/4\nL:1/8\nQ:1/4=120\nK:C")
        => $"X:1\nT:Test\n{head}\n{body}\n";

    [Theory]
    [InlineData("CDEF GABc|c8|]", "c'8 d'8 e'8 f'8 g'8 a'8 b'8 c''8 | c''1 |")]                         // letters, octaves
    [InlineData("C,D,E,F, c'd'e'f'|z8|]", "c8 d8 e8 f8 c'''8 d'''8 e'''8 f'''8 | r1 |")]                 // octave marks
    [InlineData("C2 D/E/ F3/2G/ A4|B8|]", "c'4 d'16 e'16 f'8. g'16 a'2 | b'1 |")]                      // lengths
    [InlineData("C>D E<F G>>A B2|c8|]", "c'8. d'16 e'16 f'8. g'8.. a'32 b'4 | c''1 |")]                 // broken rhythm
    [InlineData("A5 B3|c8|]", "a'2~ a'8 b'4. | c''1 |")]                                             // a length no one value spells
    [InlineData("[CEG]2 [D2F2A2] [CEc]4|z8|]", "<c' e' g'>4 <d' f' a'>4 <c' e' c''>2 | r1 |")]        // chords
    [InlineData("(3CDE (3FGA B2 c2|c8|]", "tuplet 3/2 { c'8 d'8 e'8 } tuplet 3/2 { f'8 g'8 a'8 } b'4 c''4 | c''1 |")]   // triplets
    [InlineData("c4- c4|c2 z2 x4|]", "c''2~ c''2 | c''4 r4 r2 |")]                                    // a tie; an unseen rest
    public void WhatComesBackSoundsTheSame(string body, string music)
    {
        var (lys, report) = Import(Tune(body));
        Assert.Equal(Sounds(Book(music)), Sounds(lys));
        Assert.Empty(report.Warnings);
    }

    [Theory]
    // An accidental holds for its bar and its pitch, and is gone after the bar line.
    [InlineData("K:C", "^F2 F2 F,2 G2|F8|]", "fis'4 fis'4 f4 g'4 | f'1 |")]
    // The key's sharp, a natural against it, and the sharp back in the next bar.
    [InlineData("K:G", "F2 =F2 F2 G2|F8|]", "fis'4 f'4 f'4 g'4 | fis'1 |")]
    // A tie carries its accidental over the bar line.
    [InlineData("K:C", "z4 ^c4-|c4 c4|]", "r2 cis''2~ | cis''2 c''2 |")]
    // A mode: D dorian has no sharps.
    [InlineData("K:Ddor", "d2 e2 f2 b2|c'8|]", "d''4 e''4 f''4 b''4 | c'''1 |")]
    [InlineData("K:Bbmin", "B2 d2 f2 e2|a8|]", "bes'4 des''4 f''4 ees''4 | aes''1 |")]
    public void PitchesFollowTheKeyAndTheBar(string key, string body, string music)
    {
        var (lys, _) = Import(Tune(body, "M:4/4\nL:1/8\nQ:1/4=120\n" + key));
        Assert.Equal(Sounds(Book(music)), Sounds(lys));
    }

    [Fact]
    public void AReelWithRepeatsAndEndings_ComesBackAsAForm()
    {
        string abc = "X:1\nT:Speed the Plough\nT:Reel\nC:Trad.\nM:4/4\nL:1/8\nQ:1/4=120\nK:G\n"
            + "|:GABc dedB|dedB dedB|c2ec B2dB|1 A2F2 G4:|2 A2F2 G2z2||\n"
            + "\"G\"g2gf gdBd|\"Am\"c2A2 \"D7\"A>B (3cBA|\"G\"G4 {/A}G2 z2|]\n";
        var (lys, report) = Import(abc);
        Sounds(lys);
        Assert.Empty(report.Warnings);
        Assert.Contains("title \"Speed the Plough\"", lys);
        Assert.Contains("subtitle \"Reel\"", lys);
        Assert.Contains("composer \"Trad.\"", lys);
        Assert.Contains("tempo 120", lys);
        Assert.Contains("key g major", lys);
        Assert.Contains("[1.", lys);
        Assert.Contains("[2.", lys);
        Assert.Contains("g''4@chord(G)", lys);
        Assert.Contains("a'8.@chord(D7) b'16 tuplet 3/2 { c''8 b'8 a'8 }", lys);
        Assert.Contains("acciaccatura { a'8 } g'4", lys);
    }

    [Fact]
    public void APickupIsAPartialBar()
    {
        var (lys, _) = Import(Tune("C | F2 G | A3 |]", "M:3/4\nL:1/4\nK:F"));
        Sounds(lys);
        Assert.Contains("partial 4", lys);
    }

    [Fact]
    public void WordsFallOnTheNotesOfTheLineAbove()
    {
        // A tied note takes a syllable of its own (ABC 2.1 §5.1); `_` holds, `*` skips, a rest takes none.
        string abc = Tune("C2 D2 E2- E2|F2 z2 G2 A2|]\nw: Hel-lo dear_ my * friend", "M:4/4\nL:1/8\nK:C");
        var (lys, report) = Import(abc);
        Sounds(lys);
        Assert.Empty(report.Warnings);
        Assert.Contains("lyrics", lys);
        Assert.Contains("Hel- lo dear __", lys);
        Assert.Contains("friend", lys);
    }

    [Fact]
    public void SecondWordsLinesAreVerses()
    {
        string abc = Tune("C2 D2 E2 F2|G8|]\nw: one two three four five\nw: six sev-en eight nine", "M:4/4\nL:1/8\nK:C");
        var (lys, _) = Import(abc);
        Sounds(lys);
        Assert.Contains("one two three four", lys);
        Assert.Contains("sev- en", lys);
    }

    [Fact]
    public void VoicesAreStaves()
    {
        string abc = "X:7\nT:Duet\nM:6/8\nL:1/8\nK:Ddor\nV:1 name=\"Flute\"\nV:2 name=\"Cello\" clef=bass\n"
            + "[V:1] d2e f2g | a3 a2z |\n[V:2] D,3 A,,3 | D,6 |\n[V:1] (fed) ^c2d | d6 |]\n[V:2] F,3 A,3 | D,6 |]\n";
        var (lys, report) = Import(abc);
        Sounds(lys);
        Assert.Empty(report.Warnings);
        Assert.Contains("part cello { clef bass }", lys);
        Assert.Contains("staff flute \"Flute\"", lys);
        Assert.Contains("staff cello \"Cello\"", lys);
        Assert.Contains("f''8( e''8 d''8) cis''4 d''8", lys);
        Assert.Contains("d4. a,4.", lys);
    }

    [Fact]
    public void DecorationsAndDynamics()
    {
        var (lys, _) = Import(Tune("!p!.C2 HD2 !crescendo(!TE2 !crescendo)!!f!F2|\"^Fine\"G8|]"));
        Sounds(lys);
        Assert.Contains("c'4@p@staccato", lys);
        Assert.Contains("d'4@fermata", lys);
        Assert.Contains("e'4@cresc@trill", lys);
        Assert.Contains("f'4@!cresc@f", lys);
        Assert.Contains("g'1@text(\"Fine\").up", lys);
    }

    [Fact]
    public void OnlyTheFirstTuneIsRead_AndTheReportSaysSo()
    {
        string abc = Tune("C8|]") + "\n" + "X:2\nT:Second\nM:4/4\nL:1/8\nK:C\nG8|]\n";
        var (lys, report) = Import(abc);
        Assert.Contains("title \"Test\"", lys);
        Assert.DoesNotContain("Second", lys);
        Assert.Contains(report.Warnings, w => w.Contains("2 tunes"));
    }

    [Fact]
    public void ATuneBook_IsAPiecePerTune_NamedByNumberAndTitle()
    {
        // The file header (before the first X:) holds for every tune; a tune's own field wins.
        string abc = "%abc-2.1\nM:4/4\nL:1/8\n\n"
            + "X:1\nT:Speed the Plough\nK:G\nGABc dedB|G8|]\n\n"
            + "X:2\nT:The Kesh Jig\nM:6/8\nK:G\nGAG GAB|G6|]\n\n"
            + "X:10\nT:Sakura (さくら)\nK:Am\nA2B2 c4|A8|]\n";
        var pieces = new MusicXmlImporter().ImportPieces(Encoding.UTF8.GetBytes(abc));
        Assert.Equal(["001-speed-the-plough", "002-the-kesh-jig", "010-sakura-さくら"], pieces.Select(p => p.Name));
        Assert.Equal(["Speed the Plough", "The Kesh Jig", "Sakura (さくら)"], pieces.Select(p => p.Title));
        foreach (var piece in pieces)
        {
            Sounds(piece.Lys!);
            Assert.Empty(piece.Report.Warnings);
        }
        Assert.Contains("time 4/4", pieces[0].Lys);
        Assert.Contains("g'8 a'8 b'8 c''8", pieces[0].Lys);          // L:1/8 from the file header
        Assert.Contains("time 6/8", pieces[1].Lys);
        Assert.Contains("title \"Sakura (さくら)\"", pieces[2].Lys);
        Assert.Contains("key a minor", pieces[2].Lys);
    }

    [Fact]
    public void ATuneThatCannotBeRead_LeavesTheOthers()
    {
        string abc = "X:1\nT:One\nM:4/4\nL:1/8\nK:C\nC8|]\n\nX:2\nT:Empty\nK:C\n\nX:3\nT:Three\nM:4/4\nL:1/8\nK:C\nE8|]\n";
        var pieces = new MusicXmlImporter().ImportPieces(Encoding.UTF8.GetBytes(abc));
        Assert.Equal(3, pieces.Count);
        Assert.NotNull(pieces[0].Lys);
        Assert.Null(pieces[1].Lys);
        Assert.Contains("no music", pieces[1].Error);
        Assert.NotNull(pieces[2].Lys);
    }

    [Fact]
    public void OneTune_IsOnePieceWithNoName_AndSoIsAnyOtherFile()
    {
        var one = new MusicXmlImporter().ImportPieces(Encoding.UTF8.GetBytes(Tune("C8|]")));
        Assert.Null(Assert.Single(one).Name);
    }

    [Theory]
    [InlineData(new[] { "1", "1", "" }, new[] { "A", "A", null }, new[] { "001-a", "002-a", "003" })]   // a number used twice, none, a title used twice
    [InlineData(new[] { "1200", "7" }, new[] { "x", "y" }, new[] { "1200-x", "0007-y" })]                 // the widest number sets the width
    [InlineData(new[] { "1", "2" }, new[] { "?!", "Reel: \"The Ash Plant\" / G" }, new[] { "001", "002-reel-the-ash-plant-g" })]
    public void TheFileNames(string[] numbers, string?[] titles, string[] expected)
    {
        var tunes = numbers.Select((n, k) => new AbcReader.Tune(n.Length > 0 ? n : null, titles[k], "")).ToList();
        Assert.Equal(expected, AbcReader.FileNames(tunes));
    }

    [Fact]
    public void WhatIsLeftOutIsReported()
    {
        var (_, report) = Import(Tune("P:A\nC8|]\nW:words after"));
        Assert.Contains(report.Warnings, w => w.Contains("P:"));
        Assert.Contains(report.Warnings, w => w.Contains("W:"));
    }

    [Fact]
    public void ItIsToldFromMusicXmlAndMidiByWhatItHolds()
    {
        Assert.True(AbcReader.IsAbc(Encoding.UTF8.GetBytes(Tune("C8|]"))));
        Assert.True(AbcReader.IsAbc(Encoding.UTF8.GetBytes("%abc-2.1\n" + Tune("C8|]"))));
        Assert.False(AbcReader.IsAbc(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<score-partwise>K:C</score-partwise>")));
        Assert.False(AbcReader.IsAbc(Encoding.UTF8.GetBytes("T:no key line\nCDEF")));
    }
}
