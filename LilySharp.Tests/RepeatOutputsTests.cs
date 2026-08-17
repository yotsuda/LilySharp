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
/// A percent sign and a tremolo are ENGRAVED ONCE and PLAYED MANY TIMES, so every pass has
/// to be that one printed copy. Both exporters re-entered the body in whatever frame the
/// previous pass left, and a body that moves the relative frame climbed away.
/// </summary>
/// <remarks>
/// <para>
/// MEASURED 2026-08-17 on audit/lpreg/chord-tremolo-whole — `repeat tremolo 32 { g''64 a }`,
/// a page of ONE G5-A5 pair played thirty-two times:
/// </para>
/// <para>
///   page       2 written positions, G5 and A5<br/>
///   MIDI       79 81 103 105 and then key 127 sixty times — a rising figure pinned against
///              the top of the MIDI range<br/>
///   MusicXML   G5 A5 G7 A7 G9 A9 ... up to OCTAVE 67, with no ceiling to pin it
/// </para>
/// <para>
/// ⚠️ THE TWO WRONGS PARTLY CANCELLED, which is why a cross-output instrument had to be
/// pointed at it rather than a per-output net: both exporters climbed, so comparing them to
/// each other showed a difference only where the MIDI's clamp bit. Repairing the MIDI first
/// made the measured disagreement BIGGER (118 entries to 124) before the second repair took
/// it to zero. A count of disagreements is not a score to minimise.
/// </para>
/// <para>
/// ⚠️ `unfold` IS DELIBERATELY NOT IN THIS FAMILY. It prints every copy, and the page's own
/// copies climb with it (MEASURED: `repeat unfold 4 { g''8 a }` draws its four pairs an octave
/// apart, rising), so page and exporters agree. LilyPond does not — it resolves the relative
/// chain once and copies the result, so its unfolded copies are identical, and our twin
/// exports `\repeat unfold 4 { g''8 a }` verbatim into that rule. That disagreement is about
/// what the GRAMMAR means, not about one output being broken, and it is filed in HANDOFF §2F.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class RepeatOutputsTests
{
    private const string Tremolo = """
        time 4/4
        part melody
        section A { melody { repeat tremolo 32 { g''64 a } } }
        form main { ~A }
        score main { staff melody }
        """;

    private const string Percent = """
        time 4/4
        part melody
        section A { melody { repeat percent 3 { g''8 a b c d e f g | } } }
        form main { ~A }
        score main { staff melody }
        """;

    [Theory]
    // 32 passes of the one printed pair; 3 passes of the one printed bar.
    [InlineData(Tremolo, 32, new[] { 79, 81 })]
    [InlineData(Percent, 3, new[] { 79, 81, 83, 84, 86, 88, 89, 91 })]
    public void AnEngravedOnceRepeat_PlaysAndExportsThePrintedCopyEveryPass(
        string book, int passes, int[] printed)
    {
        var tree = SyntaxTree.Parse(book);

        // ⑴ the page prints the body ONCE — that is what makes the other two comparable
        Assert.Equal(printed, ResolvedPitches.ForFile(tree)!.Select(e => Key(e.Pitch)).ToArray());

        // ⑵ every MIDI pass is that copy: the multiset is the printed one, `passes` times
        var midi = new MidiExporter().Export(tree).Tracks
            .SelectMany(t => t.Notes).Select(n => n.Pitch).ToArray();
        Assert.Equal(printed.Length * passes, midi.Length);
        Assert.Equal(Enumerable.Repeat(printed, passes).SelectMany(p => p).OrderBy(k => k),
            midi.OrderBy(k => k));

        // ⑶ ... and so is every MusicXML pass
        var xml = new MusicXmlExporter().Export(tree).Parts
            .SelectMany(p => p.Measures).SelectMany(m => m.Notes)
            .Where(n => !n.IsRest && !n.IsBackup && !n.IsUnpitched && n.Step != null)
            .Select(n => RelativeOctave.StepToMidi("CDEFGAB".IndexOf(n.Step![0]),
                (int)System.Math.Round(n.Alter ?? 0), n.Octave!.Value)).ToArray();
        Assert.Equal(printed.Length * passes, xml.Length);
        Assert.Equal(Enumerable.Repeat(printed, passes).SelectMany(p => p).OrderBy(k => k),
            xml.OrderBy(k => k));
    }

    [Fact]
    public void InAbsoluteOctaveMode_ThereWasNothingToClimb()
    {
        // The control, and it has to be `octave absolute` rather than a well-chosen relative
        // body: the first draft used `repeat percent 3 { g''4 a g a | }` on the reasoning
        // that it "ends where it starts", and it went red under the poison with the others.
        // It does not end where it starts — `''` counts from the NEAREST g, so a body whose
        // last note is A5 re-enters two octaves up however tidy it looks. In absolute mode
        // there is no frame to carry, so this book was right through both defects and stays
        // right under either poison. A control that the poison reaches is not a control.
        var tree = SyntaxTree.Parse("""
            octave absolute
            time 4/4
            part melody
            section A { melody { repeat percent 3 { g'8 a' b' c'' d'' e'' f'' g'' | } } }
            form main { ~A }
            score main { staff melody }
            """);
        var midi = new MidiExporter().Export(tree).Tracks
            .SelectMany(t => t.Notes).Select(n => n.Pitch).Distinct().OrderBy(k => k).ToArray();
        Assert.Equal(8, midi.Length);
        Assert.Equal(ResolvedPitches.ForFile(tree)!.Select(e => Key(e.Pitch)).OrderBy(k => k),
            midi);
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
