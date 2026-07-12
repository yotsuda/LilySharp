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
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Scale-degree chord notation (&lt;d 3 5 7,&gt;): a root pitch plus degrees
/// stacked by diatonic scale steps in the current key. Degree N sits N−1 steps
/// above the root; a glued is/es alters it chromatically; ' / , move its octave.
/// Verified through the collected chord notes' MIDI pitch (C4 = 60).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChordDegreeTests
{
    private static ChordItem FirstChord(string body, string key = "c major")
    {
        var src = $"time 4/4\nkey {key}\npart m {{ clef treble\n  section A {{ {body} }} }}\n"
                + "form main { A }\nscore main { staff m }";
        return new MeasureCollector().Collect(SyntaxTree.Parse(src), "m").Voice.Measures
            .SelectMany(m => m.Items).OfType<ChordItem>().First();
    }

    private static int[] Midis(string body, string key = "c major") =>
        FirstChord(body, key).Notes.Select(n => n.Midi).ToArray();

    [Theory]
    [InlineData("<0 2 4>2")]  // degree 0 is not the leading tone — it is invalid
    [InlineData("<0>2")]
    public void ZeroDegree_IsDiagnosed(string body)
    {
        var tree = SyntaxTree.Parse($"time 4/4\nkey c major\n{body}");
        Assert.Contains(tree.Diagnostics, d => d.Code == DiagnosticCodes.InvalidScaleDegree);
    }

    [Fact]
    public void Degrees_StackDiatonicallyOnRoot()
    {
        // <d 3 5 7,> in C major = root D4, plus its 3rd (F), 5th (A), 7th (C)
        // with the 7th dropped an octave: C4 D4 F4 A4 (a Dm7 voicing).
        Assert.Equal(new[] { 62, 65, 69, 60 }, Midis("<d 3 5 7,>2"));
    }

    [Fact]
    public void Triad_FromDegrees()
    {
        // <c 3 5> = C major triad C E G.
        Assert.Equal(new[] { 60, 64, 67 }, Midis("<c 3 5>2"));
    }

    [Fact]
    public void GluedSharp_RaisesTheDegree()
    {
        // <d 3is 5 7> = D F♯ A C (a D7): the 3rd is raised a semitone.
        Assert.Equal(new[] { 62, 66, 69, 72 }, Midis("<d 3is 5 7>1"));
    }

    [Fact]
    public void Extensions_CarryPastTheOctave()
    {
        // <g 3 5 7 9> = G B D F A (a G9); 9 = the 2nd an octave up, no special case.
        // The root g resolves relatively to G3 (nearest g to C4), so the stack is
        // G3 B3 D4 F4 A4.
        Assert.Equal(new[] { 55, 59, 62, 65, 69 }, Midis("<g 3 5 7 9>1"));
    }

    [Fact]
    public void Degrees_AreRelativeToTheKey()
    {
        // The SAME <d 3 5 7> is Dm7 in C major but D7 in G major — the 3rd (F)
        // takes the key's accidental (F♯ in G), so the chord quality follows the key.
        Assert.Equal(new[] { 62, 65, 69, 72 }, Midis("<d 3 5 7>1", key: "c major")); // D F  A C
        Assert.Equal(new[] { 62, 66, 69, 72 }, Midis("<d 3 5 7>1", key: "g major")); // D F♯ A C
    }

    [Fact]
    public void GluedFlat_KeepsTheLetterSpelling()
    {
        // <d 3es> in C major = D + F♭ (letter-preserving): F♭ sounds as E (64) but
        // is SPELLED with a flat, not respelled to a natural E.
        var chord = FirstChord("<d 3es>2");
        Assert.Equal(new[] { 62, 64 }, chord.Notes.Select(n => n.Midi).ToArray());
        Assert.NotNull(chord.Notes[1].Accidental); // a flat is drawn on the F
    }

    [Fact]
    public void OmittedRoot_AnchorsOnTheKeyTonic()
    {
        // <1 3 5> with no root pitch = the tonic triad of the key: C E G in C.
        Assert.Equal(new[] { 60, 64, 67 }, Midis("<1 3 5>2"));
        // The same <1 3 5> follows the key — G B D in G major (tonic resolves
        // relatively to G3, nearest to the opening frame).
        Assert.Equal(new[] { 55, 59, 62 }, Midis("<1 3 5>2", key: "g major"));
    }

    [Fact]
    public void OmittedRoot_WithoutDegreeOne_OmitsTheTonic()
    {
        // <3 5> sounds only the 3rd and 5th ABOVE the tonic (the tonic itself is
        // not written, so not sounded): E G in C major.
        Assert.Equal(new[] { 64, 67 }, Midis("<3 5>2"));
    }

    [Fact]
    public void ChordDuration_AppliesAfterTheAngle()
    {
        // Duration is written after '>', like an ordinary chord: <c 3 5>2 is a half.
        Assert.Equal(Fraction.Half, FirstChord("<c 3 5>2").BaseDuration);
    }

    [Theory]
    [InlineData("<d 3 5 7,>2")]
    [InlineData("<d 3is 5 7>4")]
    [InlineData("<g 3 5 7 9>1")]
    [InlineData("<1 3 5>2")]
    [InlineData("<3 5>4")]
    public void DegreeChord_RoundTrips(string chord)
    {
        // Degrees live on green tokens, so ToFullString reproduces the source exactly.
        var src = $"{{ {chord} }}";
        Assert.Equal(src, SyntaxTree.Parse(src).Root.ToFullString());
    }

    private static bool MixDiagnosed(string chord) =>
        SyntaxTree.Parse($"{{ {chord} }}").Diagnostics
            .Any(d => d.Code == DiagnosticCodes.ChordMixesPitchesAndDegrees);

    [Theory]
    [InlineData("<c e 5>")]   // a second named pitch alongside a degree
    [InlineData("<c 3 e>")]   // a named pitch after a degree
    [InlineData("<3 c>")]     // omitted root, then a stray pitch
    [InlineData("<c e 3 5>")]
    public void MixingPitchesAndDegrees_IsDiagnosed(string chord) =>
        Assert.True(MixDiagnosed(chord));

    [Theory]
    [InlineData("<c e g>")]   // all pitches (classic chord)
    [InlineData("<d 3 5>")]   // one root + degrees
    [InlineData("<3 5>")]     // degrees only (omitted root)
    [InlineData("<1 3 5>")]
    [InlineData("<c>")]
    [InlineData("<d 3>")]
    public void ValidChords_AreNotDiagnosedAsMixed(string chord) =>
        Assert.False(MixDiagnosed(chord));
}
