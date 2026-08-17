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
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A phrase body opens in a FRESH relative frame — and "fresh" is the part's own anchor, the
/// octave it prints in, not a fixed middle C. The MusicXML walk used the literal 4.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED 2026-08-17 on a bass part whose music is one phrase: the page and the MIDI read
/// C3 E3 C3 G3 and the MusicXML wrote C4 E4 C4 G4 — a left hand exported an octave above its
/// own page. The rule was already spelled correctly twice: the collector resets to
/// <c>OctaveContext.InitialOctave</c> (the voice's armed octave, which the clef sets) and the
/// MIDI walk uses <c>_partOctaveAnchor + varRef.OctaveOffset</c>. Only this third reader kept
/// its own answer, and the exporter's OWN part-music path had the right one four hundred
/// lines up (<c>EmitPartMusic</c>: "the relative frame starts at the part's own anchor — the
/// octave it PRINTS — not at a fixed middle C").
/// </para>
/// <para>
/// ⚠️ IT TOOK TWO OUTPUTS SIDE BY SIDE. Every MusicXML net in the suite was green: the export
/// is internally consistent, it round-trips through our own importer, and an octave is not
/// visible in a shape assertion. What is visible is that the MIDI plays one thing and the file
/// says another. Corpus effect: books whose exported notes differ from the sounded ones went
/// 69 to 64, with `showcase/03-piano`, `test/bass-clef`, `test/keysig-clefs`,
/// `test/instrument-names` and one lilypond-ref case going to zero, and `test/feature-tour`
/// from 250 disagreeing entries to 6.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PhraseAnchorOutputsTests
{
    private const string InAPhrase = """
        time 4/4
        part lh { clef bass }
        phrase lhA { <c e>2 <c g> | }
        section Main { lh { lhA } }
        form main { Main }
        score main { staff lh }
        """;

    /// <summary>The control: the same notes written inline in the part. This path already
    /// read the part's anchor, so it was right through the defect and has to stay right —
    /// the two spellings of one piece of music must not be two pieces.</summary>
    private const string Inline = """
        time 4/4
        part lh { clef bass }
        section Main { lh { <c e>2 <c g> | } }
        form main { Main }
        score main { staff lh }
        """;

    [Theory]
    [InlineData(InAPhrase)]
    [InlineData(Inline)]
    public void ABassPartsFrame_IsItsOwnAnchorInEveryOutput(string book)
    {
        var tree = SyntaxTree.Parse(book);
        int[] printed = [48, 52, 48, 55]; // C3 E3 C3 G3

        Assert.Equal(printed, ResolvedPitches.ForFile(tree)!.Select(e => Key(e.Pitch)).ToArray());

        Assert.Equal(printed.OrderBy(k => k), new MidiExporter().Export(tree).Tracks
            .SelectMany(t => t.Notes).Select(n => n.Pitch).OrderBy(k => k));

        Assert.Equal(printed.OrderBy(k => k), new MusicXmlExporter().Export(tree).Parts
            .SelectMany(p => p.Measures).SelectMany(m => m.Notes)
            .Where(n => !n.IsRest && !n.IsBackup && !n.IsUnpitched && n.Step != null)
            .Select(n => RelativeOctave.StepToMidi("CDEFGAB".IndexOf(n.Step![0]),
                (int)System.Math.Round(n.Alter ?? 0), n.Octave!.Value))
            .OrderBy(k => k));
    }

    private static int Key(string spelt)
    {
        int step = "CDEFGAB".IndexOf(spelt[0]);
        int i = 1, alter = 0;
        for (; i < spelt.Length; i++)
        {
            if (spelt[i] == '#') alter++;
            else if (spelt[i] == 'x') alter += 2;
            else if (spelt[i] == 'b') alter--;
            else break;
        }
        return RelativeOctave.StepToMidi(step, alter, int.Parse(spelt[i..]));
    }
}
