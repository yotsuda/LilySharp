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
using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A HALF-TIE (<c>@laissezVibrer</c>, <c>@repeatTie</c>, and the repeat tie a tie carried back
/// over a repeat sign draws — SectionTieCarry) is drawn on EVERY staff and in EVERY voice, at
/// the bow the inside-staff skyline reserves. Until 2026-09-28 <c>TieVariantEngraver.Calculate</c>
/// walked the primary staff's first voice alone, while the skyline (in place since that morning) reserved room on
/// every staff and voice: a second part, a piano's lower staff and a lower voice reserved room
/// and drew nothing. The MusicXML wrote neither annotation at all.
/// LilyPond: Laissez_vibrer_engraver / Repeat_tie_engraver live in every Voice context
/// (ly/engraver-init.ly), and LaissezVibrerTie / RepeatTie are direction-polyphonic grobs
/// (scm/music-functions.scm:617-634) — a lower voice's half-tie goes DOWN.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HalfTieEveryStaffTests
{
    // The tie carried back to |: is in the SECOND part (lo's `e1~` at A's end reaches A's first
    // note again), with a written @laissezVibrer beside it; the first part has one of its own.
    private const string Parts = """
        octave absolute
        part up {
          clef treble
          section I { c''1 || }
          section A { e''1 | c''1 | e''1 || }
          section B { e''1@laissezVibrer | }
        }
        part lo {
          clef bass
          section I { c1 || }
          section A { e1 | c1@laissezVibrer | e1~ || }
          section B { e1 | }
        }
        form main { I |: A :| B }
        """;

    private const string TwoStaves = Parts + "\nscore main { staff up  staff lo }\n";
    private const string GrandStaff = Parts + "\nscore main { grandStaff { staff up  staff lo } }\n";

    // Voice 2 carries an l.v. and a repeat tie. The pitches are chosen so the PITCH rule would
    // give the opposite side to the voice rule: voice one's a (below the middle line) would curve
    // down, voice two's e' / g' (above it) up.
    private const string TwoVoices = """
        octave absolute
        part up { clef treble }
        section A {
          up { voice { a2@laissezVibrer a2 } { e'2@laissezVibrer g'2@repeatTie } | c''1 | }
        }
        form main { A }
        score main { staff up }
        """;

    private static (MultiStaffScore Score, ScoreLayout Layout) Lay(string book)
    {
        var tree = SyntaxTree.Parse(book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        return (score, new LayoutEngine().Layout(score));
    }

    private static int StaffIndexOf(MultiStaffScore score, int ordinal)
        => score.EnumerateStaves().Select(s => s.GlobalStaffIndex).OrderBy(i => i).ElementAt(ordinal);

    [Theory]
    [InlineData(TwoStaves)]
    [InlineData(GrandStaff)]
    public void EveryStaffDrawsItsHalfTies(string book)
    {
        var (score, layout) = Lay(book);
        int up = StaffIndexOf(score, 0), lo = StaffIndexOf(score, 1);
        var drawn = layout.TieVariantLayouts;

        // up: the written l.v. in B. lo: the written l.v. on A's c1, and the automatic repeat
        // tie on A's first note (the tie from A's last note, played again after :|).
        Assert.Equal(1, drawn.Count(t => t.StaffIndex == up && t.Kind == TieVariantKind.LaissezVibrer));
        Assert.Equal(0, drawn.Count(t => t.StaffIndex == up && t.Kind == TieVariantKind.Repeat));
        Assert.Equal(1, drawn.Count(t => t.StaffIndex == lo && t.Kind == TieVariantKind.LaissezVibrer));
        Assert.Equal(1, drawn.Count(t => t.StaffIndex == lo && t.Kind == TieVariantKind.Repeat));
        Assert.All(drawn, t => Assert.Equal(0, t.VoiceIndex));
        AssertInkIsTheReservation(score, layout);
    }

    [Fact]
    public void EveryVoiceDrawsItsHalfTies_TheLowerVoiceDown()
    {
        var (score, layout) = Lay(TwoVoices);
        var drawn = layout.TieVariantLayouts;

        var v1 = drawn.Where(t => t.VoiceIndex == 0).ToList();
        var v2 = drawn.Where(t => t.VoiceIndex == 1).ToList();
        Assert.Single(v1);
        Assert.Equal(TieVariantKind.LaissezVibrer, v1[0].Kind);
        Assert.Equal(2, v2.Count);
        Assert.Equal(1, v2.Count(t => t.Kind == TieVariantKind.LaissezVibrer));
        Assert.Equal(1, v2.Count(t => t.Kind == TieVariantKind.Repeat));

        // \voiceOne / \voiceTwo set the half-ties' direction like a Tie's.
        Assert.True(v1[0].CurveUp, "voice one's l.v. curves up");
        Assert.All(v2, t => Assert.False(t.CurveUp, $"voice two's {t.Kind} curves down"));
        AssertInkIsTheReservation(score, layout);
    }

    /// <summary>
    /// Every drawn bow is the bow the inside-staff skyline reserves: SkylineBuilder asks
    /// <c>TieVariantEngraver.AppendItemSemiTies</c> about the staff's own middle (Y origin 0) at
    /// the item's column, so the drawn bow is that answer lifted by its own staff's middle — and
    /// every reserved bow is drawn.
    /// </summary>
    private static void AssertInkIsTheReservation(MultiStaffScore score, ScoreLayout layout)
    {
        var staves = score.EnumerateStaves().ToDictionary(s => s.GlobalStaffIndex, s => s.Staff);
        var map = LayoutUtilities.BuildMeasureMap(layout.AllSystems);
        var reserved = new List<(TieVariantLayout Bow, double Middle, int Staff, int Voice)>();
        foreach (var (si, staff) in staves)
        {
            if (staff.IsTab || staff.IsTextRow)
                continue;
            for (int vi = 0; vi < staff.Voices.Length; vi++)
            {
                var voice = staff.Voices[vi];
                for (int mi = 0; mi < voice.Measures.Length; mi++)
                {
                    if (!map.TryGetValue(mi, out var info))
                        continue;
                    var (system, ml) = info;
                    var items = voice.Measures[mi].Items;
                    for (int ii = 0; ii < items.Length; ii++)
                    {
                        if (!TieVariantEngraver.HasSemiTie(items[ii]))
                            continue;
                        double columnX = ml.X + LayoutUtilities.GetItemXOffset(voice.Measures, mi, ii, ml);
                        double slotX = ii < ml.Items.Length ? ml.X + ml.Items[ii].X : columnX;
                        var bows = new List<TieVariantLayout>();
                        TieVariantEngraver.AppendItemSemiTies(bows, voice, mi, ii, items[ii],
                            columnX, slotX, staffMiddleDown: 0, staffIndex: -1, voiceIndex: 0);
                        double middle = LayoutUtilities.StaffOffsetInSystemDown(system, si) + 2.0;
                        foreach (var b in bows)
                            reserved.Add((b, middle, si, vi));
                    }
                }
            }
        }

        Assert.Equal(reserved.Count, layout.TieVariantLayouts.Length);
        foreach (var (b, middle, si, vi) in reserved)
        {
            var match = layout.TieVariantLayouts.Where(t => t.StaffIndex == si && t.VoiceIndex == vi
                && t.MeasureIndex == b.MeasureIndex && t.ItemIndex == b.ItemIndex && t.Kind == b.Kind
                && Math.Abs(t.StartX - b.StartX) < 1e-9 && Math.Abs(t.EndX - b.EndX) < 1e-9
                && Math.Abs(t.Y - (b.Y + middle)) < 1e-9
                && Math.Abs(t.Control1.X - b.Control1.X) < 1e-9
                && Math.Abs(t.Control1.Y - (b.Control1.Y + middle)) < 1e-9
                && Math.Abs(t.Control2.X - b.Control2.X) < 1e-9
                && Math.Abs(t.Control2.Y - (b.Control2.Y + middle)) < 1e-9).ToList();
            Assert.True(match.Count == 1,
                $"reserved {b.Kind} at staff {si} voice {vi} m{b.MeasureIndex} i{b.ItemIndex} "
                + $"x[{b.StartX:F4},{b.EndX:F4}] y {b.Y + middle:F4} is drawn {match.Count} times");
        }
    }

    [Theory]
    [InlineData(TwoStaves)]
    [InlineData(GrandStaff)]
    public void MusicXml_WritesTheHalfTiesOfEveryPart(string book)
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(book));
        Assert.Equal(2, doc.Parts.Count);
        int LetRing(int part) => doc.Parts[part].Measures.SelectMany(m => m.Notes)
            .Count(n => n.ExtraNotations.Any(e => e.Name.LocalName == "tied"
                && (string?)e.Attribute("type") == "let-ring"));
        Assert.Equal(1, LetRing(0));
        Assert.Equal(1, LetRing(1));
        // lo's A opens on the note the carried tie stops on (the repeat tie's spelling).
        var loNotes = doc.Parts[1].Measures.SelectMany(m => m.Notes).Where(n => n.Step != null).ToList();
        Assert.True(loNotes[1].TieStop, "lo's first note of A carries the repeat tie's stop");
    }

    [Fact]
    public void MusicXml_WritesTheHalfTiesOfEveryVoice()
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse(TwoVoices));
        var notes = doc.Parts[0].Measures.SelectMany(m => m.Notes).Where(n => n.Step != null).ToList();
        bool LetRing(MusicXmlNote n) => n.ExtraNotations.Any(e => e.Name.LocalName == "tied"
            && (string?)e.Attribute("type") == "let-ring");
        Assert.Equal(1, notes.Count(n => n.Voice == 1 && LetRing(n)));
        Assert.Equal(1, notes.Count(n => n.Voice == 2 && LetRing(n)));
        Assert.Equal(1, notes.Count(n => n.Voice == 2 && n.TieStop));
    }

    [Fact]
    public void MusicXml_AChordLevelLaissezVibrerTiesEveryHead()
    {
        var doc = new MusicXmlExporter().Export(SyntaxTree.Parse("""
            octave absolute
            part up { clef treble }
            section A { up { <c' e' g'>2@laissezVibrer <d'@repeatTie f'>2 | } }
            form main { A }
            score main { staff up }
            """));
        var notes = doc.Parts[0].Measures.SelectMany(m => m.Notes).Where(n => n.Step != null).ToList();
        Assert.Equal(5, notes.Count);
        Assert.All(notes.Take(3), n => Assert.Contains(n.ExtraNotations,
            e => e.Name.LocalName == "tied" && (string?)e.Attribute("type") == "let-ring"));
        Assert.True(notes[3].TieStop);   // d: its own @repeatTie
        Assert.False(notes[4].TieStop);  // f: none
    }

    [Theory]
    [InlineData(TwoStaves, 2, 0)]
    [InlineData(TwoVoices, 2, 1)]
    public void LilyPondTwin_WritesTheHalfTiesOfEveryPartAndVoice(string book, int laissez, int repeat)
    {
        string ly = new LilyPondExporter().Export(SyntaxTree.Parse(book));
        int Count(string s) => (ly.Length - ly.Replace(s, "").Length) / s.Length;
        Assert.Equal(laissez, Count("\\laissezVibrer"));
        // The automatic repeat tie of the carried tie is the twin's own \repeatTie too.
        Assert.Equal(repeat + (book == TwoStaves ? 1 : 0), Count("\\repeatTie"));
    }
}
