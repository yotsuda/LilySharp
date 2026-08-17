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
using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// What the exported document says about key, meter and clef AFTER the first bar.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ EVERY CASE READS THE VALUE AND THE BAR IT LANDS ON, because the defect this file was
/// written for is not a wrong value — it is a value that never appears. Until 2026-08-17 the
/// exporter wrote exactly one <c>&lt;attributes&gt;</c>, on the part's opening measure, and
/// filled it from a metadata pass that ran to the END of the file: `test/keysig-treble`
/// (D major, then G, then F) opened in F and never changed, and
/// `test/section-meter-resets-to-global` declared 4/4 over a bar holding three quarters.
/// Both books were in the suite the whole time; nothing looked past the first bar.
/// </para>
/// <para>
/// ⚠️ A SECTION'S OWN KEY SITS OUTSIDE EVERY PART BLOCK, so the per-part music walk cannot
/// see it — the collector states this and applies it from the section level
/// (MeasureCollector.Form.cs), and this exporter now does the same. That is one rule with
/// two spellings in the corpus (`section A { key g major  m { … } }` and
/// `section A { m { key g major … } }`), and only the second one used to work; the pair is
/// asserted here as producing the same document.
/// </para>
/// </remarks>
public class MusicXmlAttributeChangeTests
{
    private static XDocument Export(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source)).ToXml();

    /// <summary>(measure number, fifths) for every measure that states a key.</summary>
    private static (int Measure, string Fifths)[] KeyChanges(XDocument doc)
        => doc.Descendants("measure")
            .Where(m => m.Element("attributes")?.Element("key") != null)
            .Select(m => (int.Parse(m.Attribute("number")!.Value),
                          m.Element("attributes")!.Element("key")!.Element("fifths")!.Value))
            .ToArray();

    private static (int Measure, string Beats)[] TimeChanges(XDocument doc)
        => doc.Descendants("measure")
            .Where(m => m.Element("attributes")?.Element("time") != null)
            .Select(m => (int.Parse(m.Attribute("number")!.Value),
                          m.Element("attributes")!.Element("time")!.Element("beats")!.Value))
            .ToArray();

    private static string[] Pitches(XDocument doc)
        => doc.Descendants("pitch")
            .Select(p => p.Element("step")!.Value + p.Element("octave")!.Value).ToArray();

    private const string ThreeKeys = """
        phrase Lick { c d e c }
        part m { clef treble }
        section A { key d major
          m { Lick } }
        section B { key g major
          m { Lick } }
        section C { key f major
          m { Lick } }
        form main { A B C }
        score main { staff m }
        """;

    [Fact]
    public void EachSectionsOwnKey_ReachesTheDocument_OnItsOwnBar()
    {
        // The book's three keys, in order, each on the bar its section starts.
        Assert.Equal(new[] { (1, "2"), (2, "1"), (3, "-1") }, KeyChanges(Export(ThreeKeys)));
    }

    [Fact]
    public void ASectionsOwnKey_AlsoMovesTheMovablePhrase()
    {
        // The signature and the phrase auto-transpose are the same reading of the same
        // word: a section key that prints but does not move the phrase (or the reverse)
        // would be half a fix. Lick is written in C; D, G and F are its three homes here.
        Assert.Equal(
            new[] { "D4", "E4", "F4", "D4", "G3", "A3", "B3", "G3", "F4", "G4", "A4", "F4" },
            Pitches(Export(ThreeKeys)));
    }

    [Fact]
    public void TheTwoSpellingsOfASectionKey_ExportTheSameDocument()
    {
        // `section A { key g major  m { … } }` vs `section A { m { key g major … } }`.
        // Only the second used to reach the exporter, and a case that used only that one
        // would have called the whole family correct.
        string outside = """
            key c major
            phrase Lick { c d e c }
            section A { key g major
              m { Lick } }
            form main { A }
            score main { staff m }
            """;
        string inside = """
            key c major
            phrase Lick { c d e c }
            section A { m { key g major Lick } }
            form main { A }
            score main { staff m }
            """;
        Assert.Equal(KeyChanges(Export(inside)), KeyChanges(Export(outside)));
        Assert.Equal(Pitches(Export(inside)), Pitches(Export(outside)));
        Assert.Equal(new[] { "G3", "A3", "B3", "G3" }, Pitches(Export(outside)));
    }

    [Fact]
    public void TheOpeningKey_IsTheFirstOne_NotTheLastInTheFile()
    {
        // The metadata pass reads the whole file before a note is written, so an unguarded
        // walk left the opening bar holding whatever key was written LAST.
        var doc = Export("""
            key c major
            part m { clef treble }
            section A { m { c4 d e f | } }
            section B { key g major
              m { c4 d e f | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, "0"), (2, "1") }, KeyChanges(doc));
    }

    [Fact]
    public void AMidPieceMeterChange_ReachesTheBarItStartsOn()
    {
        // section-meter-resets-to-global's shape: a 3/4 pickup, then 4/4, then a section
        // that states no meter of its own and so must return to the score's.
        var doc = Export("""
            time 4/4
            part m { clef treble }
            section A { m { time 3/4 c'4 d' e' | time 4/4 f'4 e' d' c' | } }
            section B { m { g'4 f' e' d' | } }
            form main { A B }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, "3"), (2, "4") }, TimeChanges(doc));
        // And the bar that says 3/4 holds three quarters — the pair that makes the
        // declaration mean something rather than just being present.
        var first = doc.Descendants("measure").First();
        Assert.Equal(3, first.Elements("note").Count());
    }

    [Fact]
    public void AnUnchangingBook_StatesItsAttributesOnce()
    {
        // The control. A rule that wrote the attributes whenever it could would satisfy
        // every case above and quietly repeat the key on every bar of every book.
        var doc = Export("""
            key d major
            time 3/4
            part m { clef treble }
            section A { m { c'4 d' e' | f'4 g' a' | b'4 c'' d'' | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(new[] { (1, "2") }, KeyChanges(doc));
        Assert.Equal(new[] { (1, "3") }, TimeChanges(doc));
        Assert.Single(doc.Descendants("attributes"));
    }
}
