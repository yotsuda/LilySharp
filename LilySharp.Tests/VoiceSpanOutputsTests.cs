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
using LilySharp.Core.LilyPond;
using LilySharp.Core.Midi;
using LilySharp.Core.Music;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>voice { … } { … }</c> span, asked of three outputs at once: what sounds, what is
/// engraved, and what is exported. Until 2026-08-17 the MusicXML answered a different piece.
/// </summary>
/// <remarks>
/// <para>
/// TWO DEFECTS, ONE HOUSE, and neither is visible from inside MusicXML alone — an export
/// that quietly stops early is still well-formed MusicXML, and an export an octave off is
/// still a chord importers will happily open.
/// </para>
/// <para>
/// ⑴ THE MUSIC AFTER THE SPAN WAS DROPPED. <c>FlushCurrentMeasure</c> closes the measure AND
/// nulls the cursor, and every emitter in the exporter opens with
/// <c>if (_currentMeasure == null) return;</c> — so everything written after the span went
/// nowhere, in silence. MEASURED on <c>test/multi-voice</c>: the page drew 3 bars and the MIDI
/// sounded 14 notes while the MusicXML carried 2 bars and 8. Over the 566 tracked books the
/// export was missing 10,995 sounding notes of the MIDI's 203,279.
/// </para>
/// <para>
/// ⑵ EACH FURTHER VOICE READ ITS OCTAVE FROM MIDDLE C. The exporter reset
/// <c>_currentOctave = 4</c> per sub-voice and let the frame after the span carry from voice 1.
/// The collector states the rule in the one place that enforces it (MeasureCollector.MusicWalk,
/// the ParallelExpressionSyntax case): the frame at the span's OPENING is what every voice
/// reads from, and what the music after the span reads from — simultaneous music does not move
/// the relative frame. This exporter is a second READER of that rule now, not a second rule.
/// </para>
/// <para>
/// ⚠️ THE BOOK BELOW IS BUILT TO SEPARATE THE THREE CANDIDATE FRAMES, because the obvious
/// book cannot: with <c>c'1 | voice { g2 a } { b c } d1</c> all three readings answer D5 and
/// the test passes on the defect. Here the last note answers D5 from the span's frame, D7 from
/// voice 1's end, and D3 from voice 2's end.
/// </para>
/// <para>
/// ⚠️ VERIFIED AGAINST LILYPOND 2.26.0, not against our own twin's spelling: the twin writes
/// <c>&lt;&lt; { g'2 a' } \\ { b,,,2 c, } &gt;&gt; d''1</c>, whose octave marks only make sense
/// under LilyPond's own relative rule, and reading a rule off our own output is how session 174
/// filed a defect that did not exist. Run through LilyPond with a NoteHead dump, the twin gives
/// C5 G5 A6 B3 C3 D5 — the same six the page resolves.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class VoiceSpanOutputsTests
{
    private const string Book = """
        part melody
        section Main { melody { c'1 | voice { g'2 a' | } { b,2 c, | } d1 | } }
        form main { Main }
        score main { staff melody }
        """;

    /// <summary>The control: the same six LETTERS with no span at all. ⚠️ NOT the same six
    /// pitches, and the first draft of this test asserted that it was — sequential music
    /// DOES move the relative frame, so <c>b,</c> here reads from <c>a'</c> and lands on B5.
    /// That is the point of the control: it is a book where the frame carries, and the
    /// exporter agreed with the page about it through both defects.</summary>
    private const string Sequential = """
        part melody
        section Main { melody { c'1 | g'2 a' | b,2 c, | d1 | } }
        form main { Main }
        score main { staff melody }
        """;

    /// <summary>C5 G5 A6 B3 C3 D5 — measured from the page, and from LilyPond through the
    /// twin. Written as keys because that is what two of the three outputs speak.</summary>
    private static readonly int[] Expected =
    [
        72, // C5
        79, // G5
        93, // A6
        59, // B3
        48, // C3
        74, // D5
    ];

    [Fact]
    public void AVoiceSpan_SoundsAndExportsAndEngravesTheSamePiece()
    {
        var tree = SyntaxTree.Parse(Book);

        // ⑴ the page, through the resolved-pitch trace, in written order
        var page = ResolvedPitches.ForFile(tree);
        Assert.NotNull(page);
        Assert.Equal(Expected, page!.Select(e => Key(e.Pitch)).ToArray());

        // ⑵ the MIDI, ordered by onset — the span's two voices start together, so the
        // multiset is the comparable reading, not the sequence
        var midi = new MidiExporter().Export(tree).Tracks
            .SelectMany(t => t.Notes).Select(n => n.Pitch).OrderBy(k => k).ToArray();
        Assert.Equal(Expected.OrderBy(k => k).ToArray(), midi);

        // ⑶ the MusicXML: every sounding note, backup rewinds excluded
        Assert.Equal(Expected.OrderBy(k => k).ToArray(), XmlKeys(tree).OrderBy(k => k).ToArray());
    }

    [Fact]
    public void TheNoteAfterTheSpan_IsExportedAtAll()
    {
        // The half of ⑴ that a multiset comparison would hide if the octave were also wrong:
        // the last bar must EXIST. Kept separate so a future octave regression cannot be
        // mistaken for this one.
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(Book));
        Assert.Equal(3, doc.Parts[0].Measures.Count);
        Assert.Contains(doc.Parts[0].Measures[2].Notes, n => !n.IsRest && !n.IsBackup);
    }

    [Fact]
    public void WithoutTheSpan_TheOutputsAgreedAllAlong()
    {
        // The control: the same letters written sequentially. Both defects lived in the span
        // handler, so this book was green before the repair and has to stay green after it —
        // if it ever moves, the repair leaked out of the span.
        var tree = SyntaxTree.Parse(Sequential);
        var page = ResolvedPitches.ForFile(tree)!.Select(e => Key(e.Pitch)).OrderBy(k => k);
        Assert.Equal(page.ToArray(), XmlKeys(tree).OrderBy(k => k).ToArray());
        // ... and it is a DIFFERENT piece from the spanned book, which is what "simultaneous
        // music does not move the frame" means: b, reads from a' here and from c' there.
        Assert.NotEqual(Expected.OrderBy(k => k).ToArray(), page.ToArray());
    }

    /// <summary>The NOTE VALUE is part of the frame the span opens in (session 398, HANDOFF
    /// §2 R4): every branch reads its bare notes at the value in force where the span opened,
    /// and so does the music after the span. The book is built to part the candidate
    /// readings — the span opens at a QUAVER (after <c>g'</c>), voice 0 ends at a MINIM,
    /// the last voice ends at a CROTCHET — so the eight bare notes after the span fill
    /// one bar under the rule, two under "the last voice's end" (the MIDI's old reading)
    /// and four under "voice 0's end" (the page's), and the second voice's four bare notes
    /// are quavers under the rule and crotchets under the old fresh-quarter reset.</summary>
    private const string ValueBook = """
        part melody
        section Main { melody { c'2 d'8 e' f' g' | voice { a'2 b' | } { c'' d'' e'' f'' g''4 a'' | } g'' a'' b'' c''' d''' e''' f''' g''' | } }
        form main { Main }
        score main { staff melody }
        """;

    /// <summary>The same piece with every value written out — the differential's other side.</summary>
    private const string ValueBookExplicit = """
        part melody
        section Main { melody { c'2 d'8 e' f' g' | voice { a'2 b' | } { c''8 d'' e'' f'' g''4 a'' | } g''8 a'' b'' c''' d''' e''' f''' g''' | } }
        form main { Main }
        score main { staff melody }
        """;

    [Fact]
    public void AVoiceSpan_OpensEveryVoiceAndTheMusicAfterIt_AtTheValueInForceWhereItOpened()
    {
        var tree = SyntaxTree.Parse(ValueBook);

        // ⑴ the page: three bars in voice 0, the second voice's bare notes are quavers, and
        //    the eight bare notes after the span are quavers filling ONE bar
        var score = new MeasureCollector().Collect(tree, null);
        Assert.Equal(2, score.Voices.Length);
        Assert.Equal(3, score.Voices[0].Measures.Length);
        var second = score.Voices[1].Measures[1].Items.OfType<NoteItem>().ToList();
        Assert.Equal(6, second.Count);
        Assert.All(second.Take(4), n => Assert.Equal(Fraction.Eighth, n.BaseDuration));
        var after = score.Voices[0].Measures[2].Items.OfType<NoteItem>().ToList();
        Assert.Equal(8, after.Count);
        Assert.All(after, n => Assert.Equal(Fraction.Eighth, n.BaseDuration));

        // ⑵ the MIDI: the piece ends three bars (six minims) after it starts
        var notes = new MidiExporter().Export(tree).Tracks.SelectMany(t => t.Notes).ToList();
        Assert.Equal(21, notes.Count);
        int minim = notes.OrderBy(n => n.StartTick).First().DurationTicks;
        Assert.Equal(6 * minim, notes.Max(n => n.StartTick + n.DurationTicks));

        // ⑶ the MusicXML: three measures, the last holding eight eighths
        var doc = new MusicXmlExporter().Export(tree);
        var measures = doc.Parts.Single().Measures;
        Assert.Equal(3, measures.Count);
        var last = measures[2].Notes.Where(n => !n.IsBackup).ToList();
        Assert.Equal(8, last.Count);
        Assert.All(last, n => Assert.Equal("eighth", n.Type));

        // ⑷ the twin: LilyPond's parser carries the last value WRITTEN, so the twin has to
        //    write the value out where the two rules part — at the second branch's first
        //    event and at the first event after the span
        string ly = new LilyPondExporter().Export(tree);
        Assert.Matches(@"\\\\ \{ [a-g][',]*8\b", ly);
        Assert.Matches(@">> [a-g][',]*8\b", ly);
    }

    [Fact]
    public void AVoiceSpan_TheBareSpellingIsTheExplicitSpelling()
    {
        // The differential: the piece with every value written is the same piece — in
        // what sounds and in what is exported. (The twin differs by construction: it
        // writes the forced values in one and copies the source's in the other.)
        var bare = SyntaxTree.Parse(ValueBook);
        var explicitly = SyntaxTree.Parse(ValueBookExplicit);

        static IEnumerable<(int, int, int)> Sounding(SyntaxTree t)
            => new MidiExporter().Export(t).Tracks.SelectMany(tr => tr.Notes)
                .Select(n => (n.Pitch, n.StartTick, n.DurationTicks)).OrderBy(n => n);
        Assert.Equal(Sounding(explicitly), Sounding(bare));

        static IEnumerable<string> Exported(SyntaxTree t)
            => new MusicXmlExporter().Export(t).Parts.Single().Measures.Select(m => m.ToXml().ToString());
        var exportedExplicit = Exported(explicitly).ToList();
        Assert.Equal(3, exportedExplicit.Count); // the differential is a claim only over a real piece
        Assert.Equal(exportedExplicit, Exported(bare));
    }

    private static IEnumerable<int> XmlKeys(SyntaxTree tree)
        => new MusicXmlExporter().Export(tree).Parts
            .SelectMany(p => p.Measures).SelectMany(m => m.Notes)
            .Where(n => !n.IsRest && !n.IsBackup && !n.IsUnpitched && n.Step != null)
            .Select(n => RelativeOctave.StepToMidi("CDEFGAB".IndexOf(n.Step![0]),
                (int)System.Math.Round(n.Alter ?? 0), n.Octave!.Value));

    /// <summary>"C5" / "F#3" (the collector's spelling) to a MIDI key, through the engine's
    /// own conversion so the two sides cannot drift on the octave convention.</summary>
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
