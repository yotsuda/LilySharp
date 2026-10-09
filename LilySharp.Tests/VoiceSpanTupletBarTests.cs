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

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using DiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;

namespace LilySharp.Tests;

/// <summary>
/// A tuplet in voice 2..N of a <c>voice { } { }</c> span that does not open the staff is
/// bracketed over ITS OWN notes, in its own bar (owner's report, 2026-09-28,
/// scratch/tuplet-in-voice2.lys: bar 2's voice-2 triplet had no bracket and a "3" bracket
/// was drawn under bar 1's voice-2 notes instead).
/// </summary>
/// <remarks>
/// THE CAUSE: voices 2..N of a span are collected by a builder of their own
/// (<c>MeasureCollector.BuildExtraVoiceTracks</c>), which counts the SPAN's bars from 0, and
/// the walk shifts every per-bar side table by <c>_cursor.MetadataMeasureOffset</c> to put it
/// in the staff's bar — every one except the tuplet bracket (<c>ProcessTuplet</c>) and the
/// <c>&lt;&lt; &gt;&gt;</c> group (its bracket, chord name, dynamics, articulations). So a
/// span at bar N+1 keyed them at bar 0+k and the page read them against the wrong bar's items.
/// The collect-time stem probe reads the sub-voice in the SPAN's frame, so it now shifts
/// them back (<c>ProbeTupletBrackets</c>).
/// <para>
/// The same book's MusicXML exported FOUR measures for two bars: every <c>voice { } { } |</c>
/// was followed by a blank whole-bar rest, the <c>|</c> read as the second half of a
/// <c>| |</c> pair (<c>MusicXmlExporter._voiceSpanJustClosed</c>). The page, the MIDI and the
/// twin were right.
/// </para>
/// <para>
/// The report also quoted LYS2002 "Measure duration 7/4 exceeds 5/4". It does not reproduce
/// from the saved book by any path — CLI check, the LSP pass fresh or on the preview's lent
/// collect, the book typed in character by character, and the owner's installed 19:40 server
/// over stdio. 7/4 is exactly what voice 2 measures with the three triplet notes as unscaled
/// crotchets (a draft <c>{ ais,, b,, cis, b,,2 cis,2 }</c> inheriting voice 1's <c>cis4</c>);
/// the diagnostic tests below pin that the finished book draws none.
/// </para>
/// <para>
/// The same miscount was in every other bar-keyed side table the walk fills — overrides
/// and reverts (plain and <c>once</c>), tempo changes (count and text-only), navigation
/// marks, inline voltas (start and end), percent signs and beat slashes; one net each below,
/// each written red first and poisoned after the fix (all red). The rehearsal-mark fallback
/// got the offset too, but no spelling reaches it (see its test's summary).
/// </para>
/// <para>
/// Poisons: drop the offset in <c>ProcessTuplet</c> ⇒ the bracket tests go red (the owner's
/// shape exactly); drop it in the arpeggio group ⇒ <see cref="AGroupsTupletAndDynamicInVoice2OfBar2LandOnTheGroup"/>;
/// drop <c>_voiceSpanJustClosed</c> ⇒ the MusicXML measure count.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class VoiceSpanTupletBarTests
{
    /// <summary>The owner's book, verbatim but for the comment line.</summary>
    internal const string OwnersBook = """
        title "tuplet-a"
        octave absolute
        time 5/4
        key fis major

        part p1 { clef bass }

        section Piece {
          p1 {
            voice { r8. r16 fis,8 gis,8 fis,2 gis,4 } { gis,,8. fis,,16 gis,,16 fis,,16 b,,8 ais,,16 b,,16 ais,,8 gis,,8 ais,,8 r4 } |
            voice { ais,8 r16 c16 b,4 ais,16 dis,16 eis,16 eis,16 fis,8 ais,16 gisis,16 cis4 } { tuplet 3/2 { ais,,8 b,,8 cis,8 } b,,2 cis,2 } |
          }
        }

        form { ~Piece }

        score {
          staff p1 "Staff"
        }
        """;

    private static string Book(string music, string octave = "absolute", string time = "4/4") => $$"""
        octave {{octave}}
        time {{time}}
        key c major
        part lh { clef treble }
        section Main { lh { {{music}} } }
        form { Main }
        score { staff lh }
        """;

    private static MultiStaffScore Collect(string source)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.DoesNotContain(tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
    }

    private static Fraction? ScaleOf(MusicItem? item) => item switch
    {
        NoteItem n => n.TimeScale,
        ChordItem c => c.TimeScale,
        RestItem r => r.TimeScale,
        _ => null,
    };

    /// <summary>Every bracket, as "bar:voice:members", after asserting that each one covers
    /// items that ARE its tuplet's — scaled by its own ratio — in the voice and bar it names.
    /// A bracket keyed to the wrong bar lands on unscaled notes (or on nothing).</summary>
    private static string[] Brackets(MultiStaffScore score)
    {
        var staff = score.EnumerateStaves().Single().Staff;
        var result = new System.Collections.Generic.List<string>();
        foreach (var t in score.TupletBrackets.OrderBy(t => t.MeasureIndex).ThenBy(t => t.VoiceIndex).ThenBy(t => t.StartNoteIndex))
        {
            Assert.InRange(t.VoiceIndex, 0, staff.Voices.Length - 1);
            var expected = new Fraction(t.Denominator, t.Numerator);
            for (int i = t.StartNoteIndex; i <= t.EndNoteIndex; i++)
            {
                var item = LayoutUtilities.VoiceItemAt(staff.Voices, t.VoiceIndex, t.MeasureIndex, i);
                Assert.True(ScaleOf(item) == expected,
                    $"the {t.Numerator}:{t.Denominator} bracket at bar {t.MeasureIndex + 1}, voice {t.VoiceIndex + 1}, "
                    + $"item {i} covers {item?.GetType().Name ?? "nothing"} at scale {ScaleOf(item)} — not its tuplet's notes");
            }
            result.Add($"{t.MeasureIndex}:{t.VoiceIndex}:{t.EndNoteIndex - t.StartNoteIndex + 1}");
        }
        return result.ToArray();
    }

    [Fact]
    public void OwnersBook_TheTripletIsBracketedInBar2Voice2_AndBar1HasNone()
        => Assert.Equal(new[] { "1:1:3" }, Brackets(Collect(OwnersBook)));

    [Theory]
    // voice 2 of bar 1 (the offset is 0 — the case that always worked)
    [InlineData("voice { c''4 d'' e'' f'' } { tuplet 3/2 { c'8 d' e' } f'4 g'2 } | voice { c''1 } { c'1 } |",
        "absolute", "0:1:3")]
    // voice 3 of bar 2
    [InlineData("voice { c''1 } { e'1 } { g1 } | voice { c''1 } { e'1 } { tuplet 3/2 { g4 a b } g2 } |",
        "absolute", "1:2:3")]
    // two tuplets in one voice of bar 2
    [InlineData("c''1 | voice { c''1 } { tuplet 3/2 { c'8 d' e' } f'4 tuplet 3/2 { g'8 a' b' } c''4 } |",
        "absolute", "1:1:3", "1:1:3")]
    // tuplets in both voices of the same bar (bar 2)
    [InlineData("c''1 | voice { tuplet 3/2 { c''4 d'' e'' } f''2 } { g'2 tuplet 3/2 { c'8 d' e' } f'4 } |",
        "absolute", "1:0:3", "1:1:3")]
    // not first in its voice, bar 3
    [InlineData("c''1 | d''1 | voice { c''1 } { c'2 tuplet 3/2 { d'8 e' f' } g'4 } |",
        "absolute", "2:1:3")]
    // a span two bars long, the tuplet in its SECOND bar (local 1 + offset 1)
    [InlineData("c''1 | voice { c''1 | d''1 } { e'1 | tuplet 3/2 { f'4 g' a' } f'2 } |",
        "absolute", "2:1:3")]
    // relative octaves, the owner's shape
    [InlineData("c''1 | voice { c''1 } { tuplet 3/2 { c8 d e } f4 g2 } |",
        "relative", "1:1:3")]
    public void ATupletInALaterVoiceIsBracketedInItsOwnBar(string music, string octave, params string[] expected)
        => Assert.Equal(expected, Brackets(Collect(Book(music, octave))));

    /// <summary>The <c>&lt;&lt; &gt;&gt;</c> group keys its automatic bracket AND its note-attached
    /// marks by the same bar number, so it had the same fault in both.</summary>
    [Fact]
    public void AGroupsTupletAndDynamicInVoice2OfBar2LandOnTheGroup()
    {
        var score = Collect(Book("c''1 | voice { c''1 } { << c' e' g' >>4@f d'4 e'2 } |"));
        Assert.Equal(new[] { "1:1:3" }, Brackets(score));
        var staff = score.EnumerateStaves().Single().Staff;
        var dynamic = Assert.Single(score.Dynamics);
        Assert.Equal(1, dynamic.MeasureIndex);
        var anchor = LayoutUtilities.VoiceItemAt(staff.Voices, dynamic.VoiceIndex, dynamic.MeasureIndex, dynamic.ItemIndex);
        Assert.Equal(new Fraction(2, 3), ScaleOf(anchor));
    }

    /// <summary>The same span written in bar 1 and again in bar 2 bakes the same stems and
    /// beams into its voice-2 notes, and brackets each bar's own triplet.</summary>
    /// <remarks>
    /// ⚠️ THIS DOES NOT GUARD <c>ProbeTupletBrackets</c>' shift, and that was measured: with
    /// the shift poisoned away this stays green, because the collect-time probe's brackets
    /// reach only <c>BeamDetector.BeamletCounts</c> (through <c>BuildTupletSpans</c>) and the
    /// probe stamps directions, beam identities and pure tips — never beamlets. The shift
    /// keeps the probe's input in the frame of the measures it is handed (the same reason
    /// <c>OttavaDisplayProbe</c> shifts its spans); no output is known to depend on it.
    /// </remarks>
    [Fact]
    public void TheSameSpanInBar1AndBar2BakesTheSameVoice2Stems()
    {
        const string span = "voice { c''4 d'' e'' f'' } { tuplet 3/2 { c'8 d' e' } f'8 g'8 a'2 }";
        var score = Collect(Book($"{span} | {span} |"));
        var v2 = score.EnumerateStaves().Single().Staff.Voices[1];
        string Stems(int m)
        {
            var notes = v2.Measures[m].Items.OfType<NoteItem>().ToList();
            var ids = notes.Where(n => n.BeamId is not null).Select(n => n.BeamId).Distinct().ToList();
            return string.Join(",", notes.Select(n =>
                $"{n.StemUp}/{(n.BeamId is { } id ? ids.IndexOf(id) : -1)}/{n.PureBeamedStemTip:F3}/{n.TimeScale}"));
        }
        Assert.Equal(Stems(0), Stems(1));
        Assert.Contains("/1/", Stems(1)); // two beams, not one
        Assert.Equal(new[] { "0:1:3", "1:1:3" }, Brackets(score));
    }

    // ---------- the rest of the walk's bar-keyed side tables, in voice 2 of bar 2 ----------
    // Each book opens with a bar of voice 1 alone, so the span starts at bar index 1 and a
    // side table keyed by the span's own count lands one bar early.

    [Fact]
    public void AnOverrideInVoice2OfBar2TakesEffectInBar2()
    {
        var score = Collect(Book(
            "voice { c''1 } { c'1 } | voice { d''1 } { override NoteHead.color = \"red\" d'1 } |"));
        var ov = Assert.Single(score.GrobOverrides);
        Assert.Equal((1, 0, 2), (ov.MeasureIndex, ov.ItemIndex, ov.VoiceIndex));
        var voice2 = GrobPropertyResolver.ForStaffVoice(score.GrobOverrides, score.GrobReverts, 0, 2);
        voice2.AdvanceTo(0, 0);
        Assert.Null(voice2.GetValue("NoteHead", "color"));   // bar 1's voice-2 note stays black
        voice2.AdvanceTo(1, 0);
        Assert.NotNull(voice2.GetValue("NoteHead", "color"));
    }

    [Theory]
    [InlineData("revert")]
    [InlineData("once revert")]
    public void AOnceOverrideAndARevertInVoice2OfBar2AreKeyedToBar2(string revert)
    {
        var score = Collect(Book(
            $"c''1 | voice {{ d''2 e''2 }} {{ once override Stem.color = \"red\" d'2 {revert} Stem.color e'2 }} |"));
        var ov = Assert.Single(score.GrobOverrides);
        var rv = Assert.Single(score.GrobReverts);
        Assert.Equal((1, 0), (ov.MeasureIndex, ov.ItemIndex));
        Assert.Equal((1, 1), (rv.MeasureIndex, rv.ItemIndex));
    }

    [Theory]
    [InlineData("tempo 4 = 90")]      // a count
    [InlineData("tempo Andante")]     // a text-only change — the walk's second arm
    public void ATempoChangeInVoice2OfBar2IsMarkedInBar2(string change)
    {
        var score = Collect(Book($"c''1 | voice {{ d''1 }} {{ d'2 {change} e'2 }} |"));
        var tempo = Assert.Single(score.MusicMarks, m => m.Type == MusicMarkType.Tempo);
        Assert.Equal(1, tempo.MeasureIndex);
    }

    [Fact]
    public void ANavigationMarkInVoice2OfBar2IsMarkedInBar2()
    {
        var score = Collect(Book("c''1 | voice { d''1 } { segno d'1 } |"));
        var segno = Assert.Single(score.MusicMarks, m => m.Type == MusicMarkType.Segno);
        Assert.Equal(1, segno.MeasureIndex);
    }

    [Theory]
    [InlineData("c''1 | voice { d''1 | e''1 } { d'1 | [1. e'1 ] } |", 2, 2)]
    // two bars long, so the END is read and not clamped up to the start
    [InlineData("c''1 | voice { d''1 | e''1 | f''1 } { d'1 | [1. e'1 | f'1 ] } |", 2, 3)]
    public void AnInlineVoltaInVoice2OfALaterBlockBracketsItsOwnBars(string music, int start, int end)
    {
        var volta = Assert.Single(Collect(Book(music)).VoltaBrackets);
        Assert.Equal((start, end), (volta.StartMeasureIndex, volta.EndMeasureIndex));
    }

    [Fact]
    public void APercentRepeatInVoice2OfALaterBlockSignsItsOwnBar()
    {
        var score = Collect(Book("c''1 | voice { d''1 | e''1 } { repeat percent 2 { d'1 } } |"));
        var percent = Assert.Single(score.PercentRepeats);
        Assert.Equal(2, percent.MeasureIndex);
    }

    [Fact]
    public void ABeatSlashInVoice2OfBar2IsSignedInBar2()
    {
        var score = Collect(Book("c''1 | voice { d''1 } { repeat percent 4 { d'4 } } |"));
        Assert.NotEmpty(score.PercentRepeats);
        Assert.All(score.PercentRepeats, p => Assert.Equal(1, p.MeasureIndex));
    }

    /// <summary>ALREADY RIGHT before the fix, and pinned so it stays so: a mark written on a
    /// note, rest, spacer or chord takes its host's bar (<c>_markHostMeasure</c>, stamped
    /// with the offset by the note arms). The walk's own fallback for a mark that rode no
    /// host now adds the offset too, but no spelling here reaches it — poisoning it leaves
    /// all five green (MusicWalk's remark: a bare <c>@mark</c> does not parse).</summary>
    [Theory]
    [InlineData("d'1@mark(\"B\")")]
    [InlineData("s1@mark(\"B\")")]
    [InlineData("r1@mark(\"B\")")]
    [InlineData("R1@mark(\"B\")")]
    [InlineData("<d' f'>1@mark(\"B\")")]
    public void ARehearsalMarkInVoice2OfBar2IsMarkedInBar2(string voice2)
    {
        var score = Collect(Book($"c''1 | voice {{ d''1 }} {{ {voice2} }} |"));
        var mark = Assert.Single(score.MusicMarks, m => m.Type == MusicMarkType.Rehearsal);
        Assert.Equal(1, mark.MeasureIndex);
    }

    // ---------- the other outputs ----------

    [Fact]
    public void OwnersBook_MusicXml_HasTwoMeasures_TheTupletInMeasure2Voice2()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(OwnersBook)).ToXml();
        var measures = xml.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);
        Assert.Empty(measures[0].Descendants("tuplet"));
        var tuplets = measures[1].Descendants("tuplet").ToList();
        Assert.Equal(new[] { "start", "stop" }, tuplets.Select(t => (string?)t.Attribute("type")));
        Assert.All(tuplets, t => Assert.Equal("2", (string?)t.Ancestors("note").Single().Element("voice")));
        Assert.Equal(3, measures[1].Descendants("time-modification").Count());
    }

    [Theory]
    [InlineData("voice { c''1 } { c'1 } | d''1 |", 2)]       // the bar line closes the span's bar
    [InlineData("voice { c''1 } { c'1 } | | d''1 |", 3)]     // …and a second one is an empty bar
    [InlineData("voice { c''1 } { c'1 } d''1 |", 2)]         // no bar line after the span
    public void MusicXml_ABarLineAfterAVoiceSpanClosesTheSpansBar(string music, int measures)
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).ToXml();
        Assert.Equal(measures, xml.Descendants("measure").Count());
        Assert.Equal(measures, new MidiExporter().Export(SyntaxTree.Parse(Book(music)))
            .Tracks.SelectMany(t => t.Notes).Max(n => n.StartTick) / (4 * 480) + 1);
    }

    [Fact]
    public void MusicXml_ATypedBarLineAfterAVoiceSpanDecoratesTheSpansBar()
    {
        var xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("voice { c''1 } { c'1 } || d''1 |."))).ToXml();
        var measures = xml.Descendants("measure").ToList();
        Assert.Equal(2, measures.Count);
        Assert.Equal("light-light", (string?)measures[0].Descendants("bar-style").Single());
    }

    [Fact]
    public void OwnersBook_Midi_PlaysTheTripletInBar2()
    {
        var midi = new MidiExporter().Export(SyntaxTree.Parse(OwnersBook));
        int q = midi.TicksPerQuarterNote, bar2 = 5 * q;
        // ais,, b,, cis, = A#2 B2 C#3, the first three voice-2 notes of bar 2.
        var triplet = midi.Tracks.SelectMany(t => t.Notes)
            .Where(n => n.StartTick >= bar2 && n.StartTick < bar2 + q && n.Pitch is 46 or 47 or 49)
            .OrderBy(n => n.StartTick).ToList();
        Assert.Equal(new[] { 46, 47, 49 }, triplet.Select(n => n.Pitch));
        Assert.Equal(new[] { bar2, bar2 + q / 3, bar2 + 2 * q / 3 }, triplet.Select(n => n.StartTick));
    }

    [Fact]
    public void OwnersBook_LilyPondTwin_WritesTheTupletInsideVoice2()
    {
        string ly = new LilySharp.Core.LilyPond.LilyPondExporter().Export(SyntaxTree.Parse(OwnersBook));
        Assert.Contains(@"\\ { \tuplet 3/2 { ais,,8 b,,8 cis,8 } b,,2 cis,2 } >>", ly);
    }

    // ---------- diagnostics: CLI check and the LSP pass ----------

    private static bool IsBarLength(LilySharp.Core.Syntax.Diagnostic d)
        => d.Code is DiagnosticCodes.MeasureOverflow or DiagnosticCodes.MeasureIncomplete;

    [Fact]
    public void OwnersBook_CheckDrawsNoBarLengthWarning()
        => Assert.DoesNotContain(SemanticValidation.Run(SyntaxTree.Parse(OwnersBook)), IsBarLength);

    [Fact]
    public void OwnersBook_TheLspPassDrawsNoBarLengthWarning_FreshLentAndTypedIn()
    {
        var uri = new Uri("file:///tuplet-in-voice2.lys");
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        // Opened with voice 2 of bar 2 empty, then the voice typed in one character at a
        // time through incremental changes, the preview rendering after every keystroke.
        const string typed = "tuplet 3/2 { ais,,8 b,,8 cis,8 } b,,2 cis,2";
        int at = OwnersBook.IndexOf(typed, StringComparison.Ordinal);
        string text = OwnersBook.Remove(at, typed.Length);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = text, Version = 1, LanguageId = "lilysharp" },
        });
        int line = text[..at].Count(c => c == '\n');
        int col = at - (text.LastIndexOf('\n', at - 1) + 1);
        for (int i = 0; i < typed.Length; i++)
        {
            server.DidChange(new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier { Uri = uri, Version = i + 2 },
                ContentChanges = [new TextDocumentContentChangeEvent
                {
                    Range = new LilySharp.Lsp.Protocol.Range(new Position(line, col + i), new Position(line, col + i)),
                    RangeLength = 0,
                    Text = typed[i].ToString(),
                }],
            });
            // (a half-typed `tuplet 3/2 {` does not parse — the preview says so and keeps going)
            server.GetSvg(new SvgParams { TextDocument = new TextDocumentIdentifier { Uri = uri } });
        }
        Assert.Null(server.GetSvg(new SvgParams { TextDocument = new TextDocumentIdentifier { Uri = uri } }).Error);
        var doc = server.DocumentAt(uri)!;
        Assert.Equal(OwnersBook, doc.Text);

        var lent = server.DocumentDiagnostics(doc, CancellationToken.None, out bool wasLent);
        Assert.True(wasLent);
        Assert.DoesNotContain(lent, IsBarLength);
        Assert.DoesNotContain(doc.Tree.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        var fresh = LilySharpLanguageServer.DocumentDiagnostics(
            OwnersBook, SyntaxTree.Parse(OwnersBook), string.Empty, _ => null);
        Assert.DoesNotContain(fresh, IsBarLength);
    }

    /// <summary>The number the report quoted is the draft's, and the check does say it there:
    /// the triplet's notes written bare inherit voice 1's crotchet, 3/4 + 1/2 + 1/2.</summary>
    [Fact]
    public void TheDraftWithoutTheTuplet_Measures7Over4()
    {
        string draft = OwnersBook.Replace("tuplet 3/2 { ais,,8 b,,8 cis,8 }", "ais,, b,, cis,", StringComparison.Ordinal);
        var over = Assert.Single(SemanticValidation.Run(SyntaxTree.Parse(draft)), IsBarLength);
        Assert.Contains("7/4 exceeds time signature 5/4", over.Message);
    }
}
