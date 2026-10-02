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
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.MusicXml;

/// <summary>
/// Two spellings of the meter in the MusicXML that stage C3 (REFACTOR_PLAN, p744) found
/// observed by no test: an additive meter's numerator AS WRITTEN (<c>&lt;beats&gt;3+2&lt;/beats&gt;</c>,
/// Semantics.Meter.BeatsText) and <c>time none</c> as <c>&lt;senza-misura/&gt;</c> (the corpus's
/// test/senza-misura was its only observer).
/// </summary>
public class MusicXmlMeterSpellingTests
{
    private static string[] MeasureXml(string source)
        => new MusicXmlExporter().Export(SyntaxTree.Parse(source))
            .Parts.Single().Measures.Select(m => m.ToXml().ToString()).ToArray();

    /// <summary>`time 3+2/8` keeps its numerator as written: a reader groups the beats
    /// 3+2, where `5` would say nothing.</summary>
    [Fact]
    public void AnAdditiveMeter_KeepsItsNumeratorAsWritten()
    {
        var measures = MeasureXml("""
            octave absolute
            time 3+2/8
            part m { clef treble }
            section A { m { c'8 d' e' f' g' | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Contains("<beats>3+2</beats>", measures[0]);
        Assert.Contains("<beat-type>8</beat-type>", measures[0]);
    }

    /// <summary>`time none` is `&lt;senza-misura/&gt;` on the bar it opens, and the metered
    /// `time` that ends it writes its pair again on its own bar.</summary>
    [Fact]
    public void TimeNone_IsSenzaMisura_AndTheReturnWritesThePair()
    {
        var measures = MeasureXml("""
            octave absolute
            time 4/4
            part m { clef treble }
            section A { m { c'4 d' e' f' | time none g'4 a' b' | time 4/4 c''1 | } }
            form main { A }
            score main { staff m }
            """);
        Assert.Equal(3, measures.Length);
        Assert.Contains("<senza-misura />", measures[1]);
        Assert.DoesNotContain("<beats>", measures[1]);
        Assert.Contains("<beats>4</beats>", measures[2]);
        Assert.DoesNotContain("senza-misura", measures[2]);
    }
}
