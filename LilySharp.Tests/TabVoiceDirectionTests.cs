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

using System.Globalization;
using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Inside a polyphonic span a full tab's stems, beams, slurs and ties take the VOICE's side —
/// <c>\voiceOne</c> up, <c>\voiceTwo</c> down — not the string rule's.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/music-functions.scm:617-674 direction-polyphonic-grobs /
///   make-voice-props-set — Stem, Slur and Tie are in the list, and a TabVoice takes them as
///   any Voice does.
/// <para>
/// MEASURED before the fix (HANDOFF T9, Lab sessions/p635/poly.* and p636/poly2.*): the voice
/// direction was baked only into <c>StemUpOverride</c>, which the tab cannot read (it is also
/// the notation beam's pitch answer), so the tab ran the string rule — voice 1 on the top
/// string stemmed DOWN and voice 2 on the low strings UP, the two sets of stems crossing
/// through each other's digits, and the beams of the second bar crossed the same way.
/// LilyPond 2.26.0 draws voice 1 up and voice 2 down, with voice 1's slurs and ties above.
/// </para>
/// <para>
/// The book is built so that every claim DISAGREES with the string rule somewhere — each
/// assertion has a positive control saying so — or a pass would say nothing.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TabVoiceDirectionTests
{
    // Voice 1 on the top string then on the D/G strings (the rule stems the top string DOWN
    // and ties the D string DOWN); voice 2 on the A/D strings (the rule stems them UP).
    private const string Book = """
        octave absolute
        time 4/4
        part gtr { instrument guitar }
        section A {
          gtr {
            voice { e'4\1( f'\1) g'\1( a'\1) | e8\4 f\4 g\3 a\3 e2\4~ | e1\4 | }
                  { c4\5( d\5) e\4( f\4) | c8\5 d\5 e\5 f\5 c2\5~ | c1\5 | }
          }
        }
        form main { A }
        score main { tab gtr }
        """;

    private static (Staff Tab, TabStaffGeometry Geom) CollectTab()
    {
        var tree = SyntaxTree.Parse(Book);
        Assert.False(tree.HasErrors, string.Join("; ", tree.Diagnostics));
        var score = SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree));
        var tab = score.EnumerateStaves().Select(t => t.Staff).Single(s => s.IsTab);
        Assert.False(tab.TabNumbersOnly, "a lone tab is a full tab — this book draws stems");
        var geom = new TabStaffGeometry(ScoreTextMetrics.Bundled,
            tab.Tuning!.Value, staffY: 0.0, tab.TabSourceClef, tab.Transposition);
        return (tab, geom);
    }

    // The same item with the voice props taken off: what the string rule alone answers.
    private static MusicItem Unvoiced(MusicItem item) => item switch
    {
        NoteItem n => n with { VoiceStemUp = null },
        ChordItem c => c with { VoiceStemUp = null },
        _ => item,
    };

    [Fact]
    public void EachVoicesTabStemsAndBeams_TakeTheVoicesSide()
    {
        var (tab, geom) = CollectTab();
        Assert.Equal(2, tab.Voices.Length);

        for (int vi = 0; vi < 2; vi++)
        {
            bool up = vi == 0;
            var notes = tab.Voices[vi].Measures
                .SelectMany(m => m.Items)
                .Where(it => it is NoteItem or ChordItem && !it.GraceTime)
                .ToList();
            Assert.NotEmpty(notes);

            // POSITIVE CONTROL: the string rule disagrees with the voice somewhere.
            Assert.Contains(notes, it => geom.TabStemUp(Unvoiced(it)) != up);
            Assert.All(notes, it => Assert.Equal(up, geom.TabStemUp(it)));

            // The second bar's four eighths are one beam per voice.
            var beam = notes.Where(it => it is NoteItem { BeamId: not null }).ToList();
            Assert.Equal(4, beam.Count);
            Assert.NotEqual(up, geom.GroupStemUp(beam.Select(Unvoiced)));
            Assert.Equal(up, geom.GroupStemUp(beam));
        }
    }

    private readonly record struct Bow(double StartY, double ControlY)
    {
        // SVG Y grows downward: a control point above the ends is a bow curving up.
        public bool CurveUp => ControlY < StartY;
    }

    [Fact]
    public void EachVoicesTabSlursAndTies_TakeTheVoicesSide()
    {
        string svg = LiveRender.SvgFromRenderSpec(Book);
        var bows = Regex.Matches(svg,
                "<path d=\"M [-\\d.]+,([-\\d.]+) C [-\\d.]+,([-\\d.]+) ")
            .Select(m => new Bow(
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .OrderBy(b => b.StartY)
            .ToList();

        // Four slurs and two ties. Voice 1's three sit higher on the staff than voice 2's
        // three (its slurs on the top string, its tie leaving the D string ABOVE where voice
        // 2's slur leaves it BELOW), so the top three are voice 1's.
        Assert.Equal(6, bows.Count);
        Assert.All(bows.Take(3), b => Assert.True(b.CurveUp, "voice 1 bows curve up"));
        Assert.All(bows.Skip(3), b => Assert.False(b.CurveUp, "voice 2 bows curve down"));
    }
}
