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
using System.IO;
using System.Linq;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The drift net for the accessors that read a token off the GREEN node (session 520):
/// on every node of every net book, each such accessor must answer exactly what the red
/// token spelling it replaced answers — the same string, the same count, the same offset.
/// The red token accessors (<see cref="PitchSyntax.PitchToken"/> and its siblings) are
/// kept for other readers, which is what makes this a two-spellings-of-one-question net.
/// </summary>
/// <remarks>
/// WHY: the collector reads a pitch's name and octave marks, a duration's number, a
/// string number, a barline's text and ink start, a rest's spelling and an articulation's
/// name once per note it re-collects, once a keystroke — and every one of those reads used
/// to materialise a red token (1,124 a keystroke over the owner's corpus, 31% of them the
/// pitch's). The green read is the same string with no red; this net is what pins it.
/// </remarks>
public class GreenTokenAccessorTests
{
    private const string EveryShape = """
        part bass { clef bass }
        section A {
          bass { es,4 as,, cis'8. d''16 | r4 s8 R1*2 | <c e g>4 q | e4\2 f\3 | g4@staccato.up a@rest b@accent.down | c4@arpeggio | }
        }
        section B { bass { d4 || e :| f |: g4 :|*3 | break a4 noBreak pageBreak b4 noPageBreak | } }
        score main { staff bass }
        """;

    [Fact]
    public void EveryGreenTokenAccessor_AnswersWhatTheRedTokenDoes()
    {
        var failures = new List<string>();
        int books = 0, pitches = 0, durations = 0, strings = 0, bars = 0, rests = 0, arts = 0, breaks = 0;

        var sources = CollectResumeTests.NetBooks()
            .Select(p => (Label: Path.GetFileName(p), Text: TryRead(p)))
            .Where(s => s.Text is not null)
            .Append(("EveryShape", EveryShape));

        foreach (var (label, text) in sources)
        {
            SyntaxTree tree;
            try { tree = SyntaxTree.Parse(text!); }
            catch { continue; }
            books++;
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case PitchSyntax p:
                        pitches++;
                        Check(failures, label, p, "PitchName", p.PitchName,
                            p.PitchToken.Text switch { "es" => "ees", "as" => "aes", var t => t });
                        Check(failures, label, p, "OctaveOffset", p.OctaveOffset, RedOctaveMarks(p));
                        break;
                    case DurationSyntax d:
                        durations++;
                        Check(failures, label, d, "Value", d.Value, int.TryParse(d.NumberToken.Text, out int v) ? v : 4);
                        break;
                    case StringNumberAnnotationSyntax s:
                        strings++;
                        Check(failures, label, s, "StringNumber", s.StringNumber, int.Parse(s.StringNumberToken.Text.TrimStart('\\')));
                        break;
                    case BarlineSyntax b:
                        bars++;
                        Check(failures, label, b, "BarText", b.BarText, b.BarToken.Text);
                        Check(failures, label, b, "BarTokenStart", b.BarTokenStart, b.BarToken.Span.Start);
                        break;
                    case RestSyntax r:
                        rests++;
                        Check(failures, label, r, "RestText", r.RestText, r.RestToken.Text);
                        Check(failures, label, r, "MeasureCount", r.MeasureCount,
                            r.GetChild(3) is SyntaxTokenNode ct && int.TryParse(ct.Text, out int n) && n >= 1 ? n : 1);
                        break;
                    case ArticulationSyntax a:
                        arts++;
                        Check(failures, label, a, "Name", a.Name, a.NameToken.Text);
                        Check(failures, label, a, "ForcedAbove", a.ForcedAbove,
                            a.GetChild(3) is SyntaxTokenNode dir
                                ? dir.Text == "up" ? true : dir.Text == "down" ? false : (bool?)null
                                : null);
                        break;
                    case BreakSyntax k:
                        breaks++;
                        Check(failures, label, k, "Directive", k.Directive, k.BreakKeyword.Kind switch
                        {
                            SyntaxKind.NoBreakKeyword => BreakKind.NoLine,
                            SyntaxKind.PageBreakKeyword => BreakKind.Page,
                            SyntaxKind.NoPageBreakKeyword => BreakKind.NoPage,
                            _ => BreakKind.Line,
                        });
                        break;
                    // The same fold, asked of the other octave-mark carriers (SyntaxFacts.NetOctaveMarks).
                    case ChordRepetitionSyntax q:
                        Check(failures, label, q, "OctaveOffset", q.OctaveOffset, RedOctaveMarks(q));
                        break;
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} mismatch(es):\n" + string.Join("\n", failures.Take(20)));
        Assert.True(books >= 50 && pitches >= 1000 && durations >= 500 && strings >= 5
            && bars >= 500 && rests >= 50 && arts >= 50 && breaks >= 3,
            $"the net did not bite: {books} books, {pitches} pitches, {durations} durations, {strings} strings, "
            + $"{bars} barlines, {rests} rests, {arts} articulations, {breaks} breaks");
    }

    // SyntaxFacts.NetOctaveMarks as it was spelled on the red children until session 520.
    private static int RedOctaveMarks(SyntaxNode node)
    {
        int offset = 0;
        for (int i = 0; i < node.SlotCount; i++)
        {
            if (node.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.Kind == SyntaxKind.Apostrophe)
                offset++;
            else if (t.Kind == SyntaxKind.Comma)
                offset--;
        }
        return offset;
    }

    private static void Check<T>(List<string> failures, string label, SyntaxNode node, string what, T green, T red)
    {
        if (!EqualityComparer<T>.Default.Equals(green, red))
            failures.Add($"{label} {node.Kind}@{node.Position} {what}: green {green} != red {red}");
    }

    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }
}
