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
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// The Phase-0 safety net for the MusicXML importer. Because export already
/// exists, each fixture makes the full loop
/// <c>.lys -&gt; export XML -&gt; import -&gt; .lys'</c> and asserts the imported
/// source (a) parses clean and (b) re-collects to the SAME music as the original
/// on the covered subset: absolute pitch (MIDI), sounding duration, per-measure
/// item order, and measure count. This defines "done" for Tier 1 objectively and
/// guards against regressions as the importer grows.
/// </summary>
public class MusicXmlRoundTripTests
{
    [Fact]
    public void Melody_PitchesDurationsDotsRestsTies_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            title "Round Trip"
            tempo 96
            time 4/4
            key g major

            c'4 d' e' fis' | g'2 a'4. b'8 | c''1 | r2 g'4 fis' | e'2~ e'2 |
            """);
    }

    [Fact]
    public void FlatKeyAndAccidentals_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            time 3/4
            key ees major

            ees'4 g' bes' | aes'2 f'4 | ees'2. |
            """);
    }

    [Fact]
    public void LeadSheet_ChordsAndLyrics_SurviveRoundTrip()
    {
        AssertRoundTrips("""
            octave absolute
            title "Lead Sheet"
            composer "Lily#"
            tempo 120
            time 4/4
            key c major

            part melody { clef treble }

            section A {
              melody {
                e'4@chord(c) e' f' g' | a'4@chord(a:m) g' e' d' |
                f'4@chord(f) a' g' f' | e'4@chord(g:7) d' c'2 |
              }
              lyrics { Mu- sic fills the | air to- night so | ev- 'ry- one will | sing a- long | }
            }

            structure { A }

            score "lead-sheet" { staff melody }
            """);
    }

    [Fact]
    public void ChordSymbolBeforeNote_DoesNotStealItsLyric()
    {
        // Regression: a <harmony> pseudo-entry precedes the first note in the note
        // stream; NextSingable must skip it, else the first note's syllable lands on
        // the harmony and is dropped on serialization. Guards both export and import.
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            time 4/4
            key c major
            part melody { clef treble }
            section A {
              melody { e'4@chord(c) e' f' g' | }
              lyrics { Mu- sic fills the | }
            }
            structure { A }
            score x { staff melody }
            """)).ToXml();

        var firstNote = xml.Descendants("note").First();
        Assert.Equal("Mu", firstNote.Element("lyric")?.Element("text")?.Value);
    }

    // NOTE: structure-level repeats (|: A :|) replay sections in the collector but
    // the exporter unrolls them to repeat BARLINES (section emitted once), so a
    // round-trip through XML can't match on replay count until the importer factors
    // repeats back into structure { } (Phase 3). Deliberately not covered here yet.

    // ---- harness ----------------------------------------------------------

    private static void AssertRoundTrips(string originalLys)
    {
        var originalTree = SyntaxTree.Parse(originalLys);
        Assert.False(HasErrors(originalTree), "the fixture itself must parse clean");

        // .lys -> MusicXML (in-memory) -> .lys'
        var xml = new MusicXmlExporter().Export(originalTree).ToXml().ToString();
        var (importedLys, report) = new MusicXmlImporter().Import(xml);

        var importedTree = SyntaxTree.Parse(importedLys);
        Assert.False(HasErrors(importedTree),
            $"imported .lys did not parse clean:\n{importedLys}\n--- diagnostics ---\n{Diagnostics(importedTree)}");

        var expected = Signature(originalTree);
        var actual = Signature(importedTree);
        Assert.True(expected == actual,
            $"round-trip music mismatch\nexpected: {expected}\nactual:   {actual}\n\nimported .lys:\n{importedLys}\n\nwarnings: {string.Join("; ", report.Warnings)}");
    }

    /// <summary>A structure-independent fingerprint of the collected music:
    /// per measure, the ordered items as absolute MIDI + sounding duration. Import
    /// re-spells octaves and reshapes scaffolding, so we compare SOUND, not text.</summary>
    private static string Signature(SyntaxTree tree)
    {
        var measures = new MeasureCollector().Collect(tree).Voice.Measures;
        var parts = new List<string>();
        foreach (var measure in measures)
        {
            var items = measure.Items.Select(ItemSig).Where(s => s != null);
            parts.Add(string.Join(" ", items));
        }
        return string.Join(" | ", parts);
    }

    private static string? ItemSig(MusicItem item) => item switch
    {
        NoteItem n => $"N{n.Midi}:{n.Duration}",
        RestItem r => $"R:{r.Duration}",
        ChordItem c => $"C[{string.Join(",", c.Notes.Select(x => x.Midi).OrderBy(m => m))}]:{c.Duration}",
        _ => null, // clef/key/time change markers carry no sounding music
    };

    private static bool HasErrors(SyntaxTree tree)
        => tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Any(d => d.Severity == DiagnosticSeverity.Error);

    private static string Diagnostics(SyntaxTree tree)
        => string.Join("\n", tree.Diagnostics.Concat(SemanticValidation.Run(tree)));
}
