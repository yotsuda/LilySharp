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
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// A fretted part read from MusicXML (owner's decision 2026-11-15): a part with a TAB staff, or
/// whose staff names its strings (<c>&lt;staff-tuning&gt;</c>), comes back saying so —
/// <c>instrument guitar</c>, or a <c>tuning</c> for strings that are no preset's — and its
/// one-note wavy lines are its <c>@vibrato</c>. Until then no imported part was fretted, so the
/// wave `lysc xml` writes for a guitar's <c>@vibrato</c> was not read back (on a part nobody
/// frets the mark is a singer's, and a warning). The General MIDI sound decides nothing.
/// </summary>
[Trait("Category", "Unit")]
public class MusicXmlFrettedImportTests
{
    private const string Wave = "<wavy-line type=\"start\" number=\"1\"/><wavy-line type=\"stop\" number=\"1\"/>";

    /// <summary>The book written to MusicXML and read back; no error, and no warning, in what
    /// comes back.</summary>
    private static string ComesBack(string header, string music, string score)
    {
        var tree = SyntaxTree.Parse($"octave absolute\ntime 4/4\npart gt {{ {header} }}\n"
            + $"section A {{ gt {{ {music} }} }}\nform {{ A }}\nscore {{ {score} }}");
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        // (The section's label comes back as a mark on its first note, and a tab's strings as
        // the notes' string numbers.)
        return System.Text.RegularExpressions.Regex.Replace(
            Read(new MusicXmlExporter().Export(tree).ToXml().ToString()).Replace("@mark(\"A\")", ""), @"\\\d", "");
    }

    private static string Read(string xml)
    {
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Empty(SemanticValidation.Run(SyntaxTree.Parse(lys)).Select(d => d.Message));
        return lys.Replace("\r", "");
    }

    /// <summary>The part's header line.</summary>
    private static string Header(string lys) => lys.Split('\n').First(l => l.StartsWith("part ", System.StringComparison.Ordinal));

    /// <summary>Another program's one-part file: <paramref name="attributes"/> after the
    /// divisions and the time, then <paramref name="measure"/>.</summary>
    private static string Foreign(string attributes, string measure, string scorePart = "")
        => "<score-partwise version=\"4.0\"><part-list><score-part id=\"P1\"><part-name>P</part-name>" + scorePart + "</score-part></part-list>"
            + "<part id=\"P1\"><measure number=\"1\"><attributes><divisions>1</divisions><time><beats>4</beats><beat-type>4</beat-type></time>"
            + attributes + "</attributes>" + measure + "</measure></part></score-partwise>";

    private static string Note(string step, int octave, string ornaments = "", int? staff = null)
        => $"<note><pitch><step>{step}</step><octave>{octave}</octave></pitch><duration>1</duration><voice>{staff ?? 1}</voice><type>quarter</type>"
            + (staff is { } s ? $"<staff>{s}</staff>" : "")
            + (ornaments.Length > 0 ? $"<notations><ornaments>{ornaments}</ornaments></notations>" : "") + "</note>";

    private static string Bar(string ornaments = Wave, int octave = 3)
        => Note("E", octave, ornaments) + Note("G", octave) + Note("A", octave) + Note("B", octave);

    private const string GClef = "<clef><sign>G</sign><line>2</line></clef>";
    private const string TabClef = "<clef><sign>TAB</sign><line>5</line></clef>";

    /// <summary>The open strings as a staff's details, line 1 first.</summary>
    private static string Strings(params (string Step, int Octave)[] strings)
        => $"<staff-details><staff-lines>{strings.Length}</staff-lines>"
            + string.Concat(strings.Select((s, i) => $"<staff-tuning line=\"{i + 1}\"><tuning-step>{s.Step[..1]}</tuning-step>"
                + (s.Step.Length > 1 ? $"<tuning-alter>{(s.Step[1] == '#' ? 1 : -1)}</tuning-alter>" : "")
                + $"<tuning-octave>{s.Octave}</tuning-octave></staff-tuning>"))
            + "</staff-details>";

    private static readonly (string, int)[] GuitarStrings = [("E", 2), ("A", 2), ("D", 3), ("G", 3), ("B", 3), ("E", 4)];
    private static readonly (string, int)[] BassStrings = [("E", 1), ("A", 1), ("D", 2), ("G", 2)];

    /// <summary>A guitar's <c>@vibrato</c> comes back, on a note and on a chord, with the part a
    /// guitar's again: the staff beside its tab, and the tab alone.</summary>
    [Theory]
    [InlineData("staff gt\n tab gt")]
    [InlineData("tab gt")]
    public void AGuitarsVibrato_ComesBackWithItsInstrument(string score)
    {
        string lys = ComesBack("instrument guitar", "e'2@vibrato b4 g4 | <c' e' g'>2@vibrato r2 |", score);
        Assert.Contains(" instrument guitar ", Header(lys));
        Assert.Contains("@vibrato b4", lys);
        Assert.Contains("<c' e' g'>2@vibrato", lys);
    }

    /// <summary>The strings that are a preset's come back as that preset — a bass with the octave
    /// it is written above its sound.</summary>
    [Theory]
    [InlineData("guitar", "e'1@vibrato |", "instrument guitar }")]
    [InlineData("bass", "e1@vibrato |", "instrument bass transposition 8vb }")]
    [InlineData("bass5", "e1@vibrato |", "instrument bass5 transposition 8vb }")]
    [InlineData("bass6", "e1@vibrato |", "instrument bass6 transposition 8vb }")]
    [InlineData("ukulele", "e''1@vibrato |", "instrument ukulele }")]
    [InlineData("banjo", "e'1@vibrato |", "instrument banjo }")]
    public void APresetsStrings_AreItsInstrument(string preset, string music, string header)
    {
        string lys = ComesBack("instrument " + preset, music, "staff gt\n tab gt");
        Assert.EndsWith(header, Header(lys));
        Assert.DoesNotContain(" tuning ", Header(lys));
        Assert.Contains("1@vibrato", lys);
    }

    /// <summary>Strings that are no preset's are a <c>tuning</c> — the word of a named one, the
    /// list of any other — and no instrument is guessed; the part is fretted all the same.</summary>
    [Theory]
    [InlineData("clef treble_8 tuning guitarDropD", "tuning guitarDropD }")]
    [InlineData("clef treble_8 tuning \"d, a, d g a d'\"", "tuning guitarDadgad }")]
    [InlineData("clef treble_8 tuning \"c, g, d g a d'\"", "tuning \"c, g, d g a d'\" }")]
    [InlineData("clef treble_8 tuning tenorUkulele", "tuning tenorUkulele }")]
    public void OtherStrings_AreATuning(string written, string header)
    {
        string lys = ComesBack(written, "d'2@vibrato a4 g4 |", "staff gt\n tab gt");
        Assert.EndsWith(header, Header(lys));
        Assert.DoesNotContain(" instrument ", Header(lys));
        Assert.Contains("d'2@vibrato", lys);
    }

    /// <summary>A notation staff that names its strings is a fretted part's with no TAB staff.</summary>
    [Fact]
    public void AStaffThatNamesItsStrings_IsFretted()
    {
        string lys = Read(Foreign(GClef + Strings(GuitarStrings), Bar()));
        Assert.EndsWith("{ clef treble instrument guitar }", Header(lys));
        Assert.Contains("e4@vibrato g4", lys);
    }

    /// <summary>A TAB staff that names no strings is a guitar's — a bass's on four lines, when
    /// the file states the octave a bass is written above its sound.</summary>
    [Fact]
    public void ATabStaffWithNoStrings_IsAGuitars_OrABasssOnFourLines()
    {
        Assert.EndsWith("instrument guitar }", Header(Read(Foreign(TabClef, Bar()))));
        Assert.EndsWith("instrument guitar }", Header(Read(Foreign(TabClef + "<staff-details><staff-lines>6</staff-lines></staff-details>", Bar()))));
        string bass = Read(Foreign(TabClef + "<staff-details><staff-lines>4</staff-lines></staff-details>"
            + "<transpose><chromatic>0</chromatic><octave-change>-1</octave-change></transpose>", Bar()));
        Assert.EndsWith("instrument bass transposition 8vb }", Header(bass));
        Assert.Contains("e4@vibrato", bass);
    }

    /// <summary>A bass's preset and tunings carry its octave, and the header has no word for
    /// "none": a bass written at the pitch it sounds stays undeclared, its notes where they
    /// were, and its wave unread.</summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    // Three octaves have no `transposition` word either (the writer says so and writes none).
    [InlineData(true, -3)]
    public void ABassWrittenAtItsSoundingPitch_IsLeftAsItWas(bool namesItsStrings, int transposedOctaves)
    {
        string details = namesItsStrings ? Strings(BassStrings) : "<staff-details><staff-lines>4</staff-lines></staff-details>";
        string transpose = transposedOctaves == 0 ? ""
            : $"<transpose><chromatic>0</chromatic><octave-change>{transposedOctaves}</octave-change></transpose>";
        string lys = Read(Foreign(TabClef + details + transpose, Bar(octave: 2)));
        Assert.EndsWith("{ clef treble }", Header(lys));
        Assert.Contains("e,4 g,4", lys);
        Assert.DoesNotContain("vibrato", lys);
    }

    /// <summary>The sound decides nothing: a part with a guitar's General MIDI program and no
    /// TAB staff or strings is not fretted, and its one-note wave is not read. Nor are the bowed
    /// strings' tunings a fretted instrument's, or strings that spell no tuning.</summary>
    [Fact]
    public void TheSoundAndTheBowedStrings_DeclareNothing()
    {
        string guitarSound = "<score-instrument id=\"P1-I1\"><instrument-name>Guitar</instrument-name></score-instrument>"
            + "<midi-instrument id=\"P1-I1\"><midi-channel>1</midi-channel><midi-program>25</midi-program></midi-instrument>";
        (string, int)[] violin = [("G", 3), ("D", 4), ("A", 4), ("E", 5)];
        (string, int)[] three = [("G", 3), ("D", 4), ("A", 4)];
        (string, int)[] unheard = [("G", 10), ("D", 11), ("A", 11), ("E", 12)];
        // A guitar's six with one string a quarter tone off, and with one that names no octave.
        string quarterTone = Strings(GuitarStrings).Replace("<tuning-step>B</tuning-step>", "<tuning-step>B</tuning-step><tuning-alter>-0.5</tuning-alter>");
        string noOctave = Strings(GuitarStrings).Replace("<tuning-octave>4</tuning-octave>", "");
        Assert.NotEqual(Strings(GuitarStrings), quarterTone);
        Assert.NotEqual(Strings(GuitarStrings), noOctave);
        foreach (string lys in new[]
        {
            Read(Foreign(GClef, Bar(), guitarSound)),
            Read(Foreign(GClef + Strings(violin), Bar())),
            Read(Foreign(GClef + Strings(three), Bar())),
            Read(Foreign(GClef + Strings(unheard), Bar())),
            Read(Foreign(GClef + quarterTone, Bar())),
            Read(Foreign(GClef + noOctave, Bar())),
        })
        {
            Assert.EndsWith("{ clef treble }", Header(lys));
            Assert.DoesNotContain("vibrato", lys);
            Assert.DoesNotContain("trillSpan", lys);
        }
    }

    /// <summary>On a fretted part a trill's line is still a trill spanner: only the wave started
    /// and stopped on its one note, with no trill-mark, is the vibrato.</summary>
    [Fact]
    public void ATrillsLineOnAFrettedPart_IsStillATrillSpanner()
    {
        string trill = Read(Foreign(GClef + Strings(GuitarStrings), Bar("<trill-mark/>" + Wave)));
        Assert.Contains("e4@trillSpan@!trillSpan", trill);
        Assert.DoesNotContain("vibrato", trill);
        string line = Read(Foreign(GClef + Strings(GuitarStrings),
            Note("E", 3, "<wavy-line type=\"start\" number=\"1\"/>") + Note("G", 3)
            + Note("A", 3, "<wavy-line type=\"stop\" number=\"1\"/>") + Note("B", 3)));
        Assert.Contains("e4@trillSpan g4 a4@!trillSpan", line);
        Assert.DoesNotContain("vibrato", line);
    }

    /// <summary>A rest has nothing to shake: a wave a file hangs on one is not read.</summary>
    [Fact]
    public void AWaveOnARest_IsNotRead()
    {
        string rest = "<note><rest/><duration>1</duration><voice>1</voice><type>quarter</type>"
            + $"<notations><ornaments>{Wave}</ornaments></notations></note>";
        string lys = Read(Foreign(GClef + Strings(GuitarStrings), rest + Note("G", 3, Wave) + Note("A", 3) + Note("B", 3)));
        Assert.Contains("r4 g4@vibrato a4", lys);
    }

    /// <summary>The strings are read where they stand: a flat or a sharp open string, and the
    /// lines in whatever order the file lists them.</summary>
    [Fact]
    public void TheStrings_AreReadByTheirLineAndTheirAlter()
    {
        // E♭ standard has no name: a list, sharps for the black keys.
        (string, int)[] flat = [("Eb", 2), ("Ab", 2), ("Db", 3), ("Gb", 3), ("Bb", 3), ("Eb", 4)];
        Assert.EndsWith("tuning \"dis, gis, cis fis ais dis'\" }", Header(Read(Foreign(GClef + Strings(flat), Bar()))));
        string reversed = "<staff-details>" + string.Concat(GuitarStrings.Select((s, i) => (s, i)).Reverse()
            .Select(x => $"<staff-tuning line=\"{x.i + 1}\"><tuning-step>{x.s.Item1}</tuning-step><tuning-octave>{x.s.Item2}</tuning-octave></staff-tuning>"))
            + "</staff-details>";
        Assert.EndsWith("instrument guitar }", Header(Read(Foreign(GClef + reversed, Bar()))));
    }

    /// <summary>A part of two notation staves that names its strings: each staff it splits into
    /// is that instrument's, and each reads its own waves.</summary>
    [Theory]
    [InlineData(false, "instrument guitar }")]
    [InlineData(true, "tuning guitarDropD }")]
    public void TheStavesOfASplitPart_AreEachTheInstruments(bool droppedD, string header)
    {
        (string, int)[] strings = droppedD ? [("D", 2), .. GuitarStrings[1..]] : GuitarStrings;
        string lys = Read(Foreign("<staves>2</staves><clef number=\"1\"><sign>G</sign><line>2</line></clef>"
            + "<clef number=\"2\"><sign>F</sign><line>4</line></clef>" + Strings(strings),
            Note("E", 4, Wave, staff: 1) + Note("G", 4, staff: 1) + Note("A", 4, staff: 1) + Note("B", 4, staff: 1)
            + "<backup><duration>4</duration></backup>"
            + Note("E", 3, Wave, staff: 2) + Note("G", 3, staff: 2) + Note("A", 3, staff: 2) + Note("B", 3, staff: 2)));
        var headers = lys.Split('\n').Where(l => l.StartsWith("part ", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(2, headers.Count);
        Assert.All(headers, h => Assert.EndsWith(header, h));
        Assert.Contains("e'4@vibrato", lys);
        Assert.Contains("e4@vibrato", lys);
    }
}
