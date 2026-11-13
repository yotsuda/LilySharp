// This file is part of Lily#.
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// Lily# is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Lily# is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.MusicXmlImport;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>What sessions 1227 and 1228 found and left (Lab sessions/p1228/leftovers.md), closed in session 1229.</summary>
public class Session1229LeftoversTests
{
    private static string Book(string music)
        => "time 4/4\noctave absolute\npart mel { }\nsection Main { mel { " + music + " } }\nform { Main }\nscore { staff mel }\n";

    private static System.Collections.Generic.List<(string Step, double Alter, string? Accidental)> Pitched(string music)
        => [.. new MusicXmlExporter().Export(SyntaxTree.Parse(Book(music))).Parts.Single().Measures.SelectMany(m => m.Notes)
            .Where(n => n.Step != null && !n.IsRest).Select(n => (n.Step!, n.Alter ?? 0, n.AccidentalName))];

    /// <summary>
    /// A quarter tone in a chord, in a repeated chord and in a grace note went to MusicXML as the
    /// plain letter (found by session 1228: only a single note and a <c>&lt;&lt; &gt;&gt;</c> member carried
    /// the half alter).
    /// </summary>
    [Fact]
    public void MusicXml_KeepsTheQuarterTonesOfChordsAndGraceNotes()
    {
        Assert.Equal([("C", 0.5, "quarter-sharp"), ("E", -0.5, "quarter-flat"), ("C", 0.5, "quarter-sharp"), ("E", -0.5, "quarter-flat")],
            Pitched("<cih' eeh'>2 q2 |"));
        Assert.Equal([("A", -0.5, "quarter-flat"), ("C", 0.0, null)], Pitched("grace { aeh'16 } c'1 |"));
        Assert.Equal([("C", 1.5, "three-quarters-sharp"), ("E", 0.0, null), ("D", 0.0, null)], Pitched("grace { <cisih' e'>16 } d'1 |"));
        // The control: the single note that always carried it.
        Assert.Equal([("C", 0.5, "quarter-sharp")], Pitched("cih'1 |"));
    }

    /// <summary>…so the round trip through <c>lysc import</c> holds for them too.</summary>
    [Fact]
    public void QuarterTonesInAChord_ComeBackFromMusicXml()
    {
        string xml = new MusicXmlExporter().Export(SyntaxTree.Parse(Book("<cih' eeh'>2 grace { aeh'16 } c'2 |"))).ToXml().ToString();
        var (lys, _) = new MusicXmlImporter().Import(xml);
        Assert.Contains("cih'", lys);
        Assert.Contains("eeh'", lys);
        Assert.Contains("aeh'", lys);
    }
}
