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
using LilySharp.Core.Music;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Inside a <c>chords { }</c> block the completion offers the current key's diatonic
/// chords as ROMAN DEGREES as well as absolute names — only the chords ON the scale, so
/// every degree offered is spelled without an accidental and means the symbol printed
/// beside it.
/// </summary>
/// <remarks>
/// ⚠️ The load-bearing test here is <see cref="EveryOfferedDegreeIsAcceptedByTheCompiler"/>.
/// Session 240 shipped a completion item that taught a rejected spelling twice in one day
/// (the `chords` snippet, then the `lyrics` one it exposed), and both times the defect was
/// that no net connected the LIST to the PARSER. This connects them.
/// </remarks>
[Trait("Category", "Unit")]
public class RomanChordCompletionTests
{
    private static string DocIn(string key, string body) => $$"""
        time 4/4
        key {{key}}
        part m { clef treble section A { c4 d e f | } }
        chords prog { section A { {{body}}
        """;

    private static string[] Labels(string key, string body = "")
    {
        string text = DocIn(key, body);
        return LilySharpLanguageServer
            .GetDiatonicChordCompletions(text, text.Length, degreesToo: true)
            .Items.Select(i => i.Label!).ToArray();
    }

    [Fact]
    public void TheKeysDegreesAreOffered()
    {
        var labels = Labels("c major");

        // C major's scale chords, as degrees: I IIm IIIm IV V VIm VIIdim.
        Assert.Equal(new[] { "I", "IIm", "IIIm", "IV", "V", "VIm", "VIIdim" },
            new[] { "I", "IIm", "IIIm", "IV", "V", "VIm", "VIIdim" }
                .Where(labels.Contains).ToArray());
        // …and their sevenths.
        foreach (var seventh in new[] { "Imaj7", "IIm7", "IIIm7", "IVmaj7", "V7", "VIm7", "VIIm7-5" })
            Assert.Contains(seventh, labels);
    }

    [Fact]
    public void OnlyTheScalesChordsAreOffered_NoChromaticDegrees()
    {
        var labels = Labels("c major");

        // Writable, but not IN the key — so not offered. This is the whole of the user's
        // "only the chords on the current scale".
        foreach (var chromatic in new[] { "bVII", "bIII", "#IV", "#IVm7-5", "bVI", "II", "III" })
            Assert.DoesNotContain(chromatic, labels);
    }

    [Fact]
    public void TheDegreesFollowTheKey()
    {
        // A minor: the same seven scale chords, rotated — i ii° III iv v VI VII, which in
        // Lily#'s uppercase-plus-suffix spelling is Im IIdim III IVm Vm VI VII.
        var labels = Labels("a minor");

        foreach (var expected in new[] { "Im", "IIdim", "III", "IVm", "Vm", "VI", "VII" })
            Assert.Contains(expected, labels);
        // …and C major's minor-second degree is not among them.
        Assert.DoesNotContain("IIm", labels);
    }

    [Fact]
    public void TheAbsoluteSymbolsAreStillOffered()
    {
        var labels = Labels("c major");
        foreach (var name in new[] { "C", "Dm", "Em", "F", "G", "Am", "Bdim", "Cmaj7", "G7" })
            Assert.Contains(name, labels);
    }

    /// <summary>
    /// Every item the list offers must parse where it would be inserted. Both spellings,
    /// every degree, in several keys — so a change to either vocabulary that breaks the
    /// other fails here rather than in a writer's file.
    /// </summary>
    [Theory]
    [InlineData("c major")]
    [InlineData("a minor")]
    [InlineData("ees major")]
    [InlineData("fis minor")]
    public void EveryOfferedDegreeIsAcceptedByTheCompiler(string key)
    {
        foreach (var label in Labels(key))
        {
            string src = $$"""
                time 4/4
                key {{key}}
                part m { clef treble section A { c4 d e f | } }
                chords prog { section A { {{label}} | } }
                form main { A }
                score main { staff m  chords prog }
                """;
            var tree = SyntaxTree.Parse(src);
            Assert.False(tree.HasErrors,
                $"'{label}' does not parse in {key}: "
                + string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
        }
    }

    /// <summary>
    /// …and a degree offered means the chord shown beside it: writing the DEGREE gives the
    /// same engraved symbol as writing the NAME. That is the claim the Detail line makes.
    /// </summary>
    /// <remarks>
    /// ⚠️ Compared through the collector on BOTH sides, not against the label text. A
    /// completion label is the ENTRY spelling (<c>Eb</c>, <c>Bm7-5</c>) and the score
    /// prints the TYPOGRAPHIC one (<c>E♭</c>, <c>Bm7♭5</c>) — the first version of this
    /// test compared the two directly and failed on exactly that difference, which is a
    /// property of the language, not a defect.
    /// </remarks>
    [Theory]
    [InlineData("c major")]
    [InlineData("ees major")]
    public void AnOfferedDegreeResolvesToTheSymbolItNames(string key)
    {
        string[] Engraved(string entries)
        {
            var tree = SyntaxTree.Parse($$"""
                time 4/4
                key {{key}}
                part m { clef treble section A { c4 d e f | } }
                chords prog { section A { {{entries}} } }
                form main { A }
                score main { staff m  chords prog }
                """);
            Assert.False(tree.HasErrors,
                string.Join(" | ", tree.Diagnostics.Select(d => d.Message)));
            return new MeasureCollector().CollectMultiStaff(tree, RenderSpecParser.FindFirst(tree)!)
                .ChordNames.OrderBy(c => c.MeasureIndex).Select(c => c.ChordText).ToArray();
        }

        foreach (var chord in DiatonicChords.ForKey(
                     char.ToLowerInvariant(key[0]),
                     KeySpelling.SharpsFor(key.Split(' ')[0], key.Split(' ')[1]) ?? 0))
        {
            Assert.Equal(
                Engraved($"{chord.Symbol} | {chord.SeventhSymbol} |"),
                Engraved($"{chord.RomanSymbol} | {chord.RomanSeventhSymbol} |"));
        }
    }

    /// <summary>
    /// ⚠️ NOT inside <c>@chord(…)</c>. That annotation reads TryParseChordEntry alone —
    /// MEASURED: <c>@chord(V7)</c> is refused with "Unknown annotation" — so a degree
    /// offered there would be a completion teaching a rejected spelling.
    /// </summary>
    [Fact]
    public void DegreesAreNotOfferedInsideAChordAnnotation()
    {
        const string text = """
            time 4/4
            key c major
            part m { clef treble section A { c4@chord(
            """;
        var labels = LilySharpLanguageServer.GetDiatonicChordCompletions(text, text.Length)
            .Items.Select(i => i.Label!).ToArray();

        Assert.Contains("C", labels);
        Assert.Contains("G7", labels);
        foreach (var degree in new[] { "I", "V", "V7", "IIm7" })
            Assert.DoesNotContain(degree, labels);
    }

    [Fact]
    public void TheAnnotationStillRefusesADegree_WhichIsWhyItIsNotOffered()
    {
        var tree = SyntaxTree.Parse("""
            time 4/4
            key c major
            part m { clef treble section A { c4@chord(V7) d e f | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Contains(LilySharp.Core.Semantics.SemanticValidation.Run(tree),
            d => d.Message.Contains("@chord(V7)"));
    }
}
