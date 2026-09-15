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
using System.Text;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A part's General MIDI sound (HANDOFF §2 F-midi): its preset's program, or the
/// <c>midiInstrument "…"</c> it names from LilyPond's table, carried as a track, a channel and a
/// program change in the <c>.mid</c>, as <c>midiInstrument</c> in the twin and as
/// <c>&lt;midi-instrument&gt;</c> in the MusicXML.
/// </summary>
/// <remarks>
/// Measured before this existed: every part of every book was one track on channel 1 with no
/// program change, so any MIDI player sounded a violin and a guitar as the same piano.
/// </remarks>
[Trait("Category", "Unit")]
public class MidiInstrumentTests
{
    private static string Book(string parts, string sectionBody, string staves) =>
        "title \"t\"\ntime 4/4\noctave absolute\n" + parts
        + "\nsection A {\n" + sectionBody + "\n}\nform main { A }\nscore main \"x\" { " + staves + " }\n";

    private static SyntaxTree Parse(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.False(tree.HasErrors, string.Join("\n", tree.Diagnostics));
        return tree;
    }

    private static byte[] Bytes(MidiFile midi)
    {
        using var stream = new MemoryStream();
        midi.WriteTo(stream);
        return stream.ToArray();
    }

    private static bool Contains(byte[] haystack, params byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (needle.Select((b, k) => haystack[i + k] == b).All(x => x))
                return true;
        return false;
    }

    // ---------------------------------------------------------------- the two tables

    [Fact]
    public void TheGeneralMidiTable_IsLilyPondsHundredAndTwentyEightNames()
    {
        Assert.Equal(128, GeneralMidi.InstrumentNames.Count);
        Assert.Equal(128, GeneralMidi.InstrumentNames.Distinct().Count());
        Assert.Equal(0, GeneralMidi.ProgramOf("acoustic grand"));
        Assert.Equal(24, GeneralMidi.ProgramOf("acoustic guitar (nylon)"));
        Assert.Equal(127, GeneralMidi.ProgramOf("gunshot"));
        // Exact, as LilyPond's assoc-get is.
        Assert.Null(GeneralMidi.ProgramOf("Acoustic Grand"));
        Assert.Null(GeneralMidi.ProgramOf("electric guitar"));
    }

    [Fact]
    public void EveryPreset_HasASound()
    {
        foreach (var preset in InstrumentDefaults.KnownInstruments)
            Assert.True(InstrumentDefaults.GetMidiProgram(preset) is not null,
                $"preset '{preset}' has no General MIDI program");
    }

    // ---------------------------------------------------------------- the part header

    [Theory]
    [InlineData("instrument guitar", 24)]                                           // the preset's
    [InlineData("instrument guitar  midiInstrument \"electric guitar (clean)\"", 27)] // written wins
    [InlineData("midiInstrument \"flute\"", 73)]                                    // no preset needed
    [InlineData("clef bass", 0)]                                                    // neither: acoustic grand
    public void AParts_Program_IsItsMidiInstrument_ElseItsPresets_ElseAcousticGrand(string properties, int program)
    {
        var tree = Parse(Book($"part p1 {{ {properties} }}", "p1 { c'1 | }", "staff p1"));
        var part = tree.GetRoot().DescendantNodes().OfType<PartDeclarationSyntax>().Single();
        Assert.Equal(program, PartHeaderDefaults.Read(part).MidiProgram);
    }

    [Theory]
    [InlineData("\"electric guitar\"")]   // not one of the 128
    [InlineData("violin")]                // bare: every name is quoted
    public void AnUnknownMidiInstrument_IsRefused(string value)
    {
        var tree = SyntaxTree.Parse(Book($"part p1 {{ midiInstrument {value} }}", "p1 { c'1 | }", "staff p1"));
        Assert.Contains(SemanticValidation.Run(tree),
            d => d.Severity == DiagnosticSeverity.Error && d.Message.StartsWith("Unknown midiInstrument"));
    }

    // ---------------------------------------------------------------- the .mid

    [Fact]
    public void EachPitchedPart_HasItsOwnTrack_Channel_AndProgramChange()
    {
        var tree = Parse(Book(
            "part vln { instrument violin }\npart gtr { instrument guitar }",
            "vln { c''4 d'' e'' f'' | }\ngtr { c4 e g c' | }",
            "staff vln staff gtr"));
        var midi = new MidiExporter().Export(tree);

        Assert.Equal(3, midi.Tracks.Count);                 // conductor + one per part
        Assert.Equal(("vln", 0, (int?)40), (midi.Tracks[1].Name, midi.Tracks[1].Channel, midi.Tracks[1].Program));
        Assert.Equal(("gtr", 1, (int?)24), (midi.Tracks[2].Name, midi.Tracks[2].Channel, midi.Tracks[2].Program));
        Assert.All(midi.Tracks[1].Notes, n => Assert.Equal(0, n.Channel));
        Assert.All(midi.Tracks[2].Notes, n => Assert.Equal(1, n.Channel));

        var bytes = Bytes(midi);
        Assert.True(Contains(bytes, 0xC0, 40), "no program change 'violin' on channel 1");
        Assert.True(Contains(bytes, 0xC1, 24), "no program change 'acoustic guitar (nylon)' on channel 2");
    }

    [Fact]
    public void ADrumPart_StaysOnChannelTen_AndSendsNoProgramChange()
    {
        var tree = Parse(Book(
            "part vln { instrument violin }\npart dr { }",
            "vln { c''4 d'' e'' f'' | }\ndr { bd4 sn bd sn | }",
            "staff vln staff dr"));
        var midi = new MidiExporter().Export(tree);

        var drums = midi.Tracks.Single(t => t.Name == "dr");
        Assert.Equal(9, drums.Channel);
        Assert.Null(drums.Program);
        Assert.All(drums.Notes, n => Assert.Equal(9, n.Channel));
    }

    private static string SixteenParts(System.Func<int, string> sound)
    {
        var parts = new StringBuilder();
        var body = new StringBuilder();
        var staves = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            parts.Append($"part inst{i} {{ midiInstrument \"{sound(i)}\" }}\n");
            body.Append($"inst{i} {{ c'4 d' e' f' | }}\n");
            staves.Append($"staff inst{i} ");
        }
        return Book(parts.ToString(), body.ToString(), staves.ToString());
    }

    [Fact]
    public void TheSixteenthPitchedPart_SharesAChannelWithTheSameSound_Silently()
    {
        var exporter = new MidiExporter();
        var midi = exporter.Export(Parse(SixteenParts(_ => "violin")));

        Assert.Equal(17, midi.Tracks.Count);
        // Fifteen pitched channels (0-15 without 9); the sixteenth shares the violin's.
        Assert.Equal(15, midi.Tracks.Skip(1).Take(15).Select(t => t.Channel).Distinct().Count());
        Assert.DoesNotContain(9, midi.Tracks.Skip(1).Select(t => t.Channel));
        Assert.Equal(0, midi.Tracks[16].Channel);
        Assert.Empty(exporter.Warnings);
    }

    [Fact]
    public void TheSixteenthPitchedPart_WithASoundOfItsOwn_IsWarnedAbout()
    {
        var exporter = new MidiExporter();
        var midi = exporter.Export(Parse(SixteenParts(i => GeneralMidi.InstrumentNames[i])));

        Assert.Equal(0, midi.Tracks[16].Channel);
        Assert.Contains(exporter.Warnings, w => w.Contains("'inst15'"));
    }

    [Theory]
    [InlineData("instrument contrabass", "cb", 3)]     // strings, from the program
    [InlineData("", "flute", 1)]                       // no sound named: the old guess from the name
    public void ThePreviewTimbre_FollowsTheSound_ElseTheName(string properties, string partName, int timbre)
    {
        var tree = Parse(Book($"part {partName} {{ {properties} }}", $"{partName} {{ c'1 | }}", $"staff {partName}"));
        var midi = new MidiExporter().Export(tree);
        Assert.Equal(timbre, midi.Tracks[1].Notes[0].Timbre);
    }

    // ---------------------------------------------------------------- the twin and the MusicXML

    [Fact]
    public void TheTwin_WritesTheSound_OnlyWhereItIsNotLilyPondsDefault()
    {
        const string Music = "gtr { c4 e g c' | }";
        string named = new LilyPondExporter().Export(Parse(Book(
            "part gtr { instrument guitar  midiInstrument \"electric guitar (clean)\" }", Music, "staff gtr")));
        Assert.Contains("midiInstrument = \"electric guitar (clean)\"", named);

        string plain = new LilyPondExporter().Export(Parse(Book("part gtr { clef treble }", Music, "staff gtr")));
        Assert.DoesNotContain("midiInstrument", plain);
    }

    [Fact]
    public void TheMusicXml_CarriesTheSound_AsItsMidiInstrument()
    {
        string xml = new MusicXmlExporter().Export(Parse(Book(
            "part gtr { instrument guitar  midiInstrument \"electric guitar (clean)\" }",
            "gtr { c4 e g c' | }", "staff gtr"))).ToXml().ToString();

        Assert.Contains("<instrument-name>electric guitar (clean)</instrument-name>", xml);
        Assert.Contains("<midi-channel>1</midi-channel>", xml);
        Assert.Contains("<midi-program>28</midi-program>", xml);   // MusicXML counts from 1
    }
}
