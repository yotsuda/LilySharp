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
using System.IO;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp.Protocol;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// Every legal spelling of every score ROW, and what the popup offers at the end of it.
/// </summary>
/// <remarks>
/// ⚠️⚠️ THIS IS THE NET UNDER A DELIBERATE SPLIT. The rows are read two ways
/// (LilySharpLanguageServer's <c>RowReadingRule</c>): <c>staff</c> and <c>ossia</c> by
/// replaying their tokens, the others by counting words back from the caret — which is
/// enough while every optional clause is bounded and made of bare words. Session 374 fixed
/// six defects that were all one shape: a clause the grammar has and a reader does not know
/// about. The rows below are correct TODAY; what this file buys is that the day one of them
/// grows a clause cannot pass unnoticed.
/// <para>
/// ★ <see cref="AWordTheRowDoesNotTake_StartsANewRow"/> is the tripwire. Every row here
/// ends at a word it cannot attach, so the extra word parses as a bare MIDI-only part row
/// and LYS1007 says the name is undefined. Add an optional clause to a row's parser and
/// that error stops appearing for that row — the test goes red and names the row whose
/// reader now has to be taught (or moved to the token reader).
/// ⚠️ POISONED AND WATCHED IT FIRE (2026-09-12): ONE clause added to
/// <c>ParseChordRowRender</c> (<c>if (Current.Text == "label") Advance();</c>) turned
/// exactly the three <c>chords</c> spellings red and left the other 23 green — it goes off
/// in the row that grew, and nowhere else.
/// </para>
/// <para>
/// ⚠️ The bare MIDI-only row (<c>NAME [instrument X] [octave N]</c>, repeatable) is pinned
/// here for its PARSE only. Its completion is an open gap, measured 2026-09-12: neither
/// option is offered after the part name, and <c>n octave ▮</c> offers the part header's
/// <c>absolute|relative</c> where this row takes a number (it compiles, so nothing refuses
/// it). Asserting today's answers there would freeze the gap, so this file does not.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ScoreRowSpellingMatrixTests
{
    /// <summary>A part-major book with everything the rows below reference.</summary>
    private const string Book = """
        part melody { clef treble
          section A { c'4 d' e' f' | }
        }
        part bass { clef bass
          section A { c4 d e f | }
        }
        chords prog { section A { C | } }
        lyrics w { section A { Do re mi fa | } }
        form main { A }

        """;

    /// <summary>
    /// One row spelling, and what must still be offered at its end beyond the next row's
    /// keyword. A spelling whose clause is still open (a staff chain with one selector
    /// written) carries the selector that may still follow.
    /// </summary>
    public static TheoryData<string, string[]> Spellings() => new()
    {
        // staff — ['~'] [Clef] Name [String] ['as' Sel {Sel}]
        { "staff melody", ["as lines", "as removeEmpty"] },
        { "staff ~melody", ["as lines", "as removeEmpty"] },
        { "staff treble melody", ["as lines", "as removeEmpty"] },
        { "staff ~treble melody", ["as lines", "as removeEmpty"] },
        { "staff melody \"Violin I\"", ["as lines", "as removeEmpty"] },
        { "staff treble melody \"Violin I\"", ["as lines", "as removeEmpty"] },
        { "staff melody as lines 1", ["removeEmpty"] },
        { "staff melody as removeEmpty all", ["lines"] },
        { "staff melody as lines 1 removeEmpty all", [] },
        { "staff ~treble melody \"Violin I\" as lines 1 removeEmpty all", [] },
        // ossia — [Clef] Name ['as' Sel {Sel}]
        { "ossia melody", ["as lines", "as removeEmpty"] },
        { "ossia treble melody", ["as lines", "as removeEmpty"] },
        { "ossia melody as lines 1", ["removeEmpty"] },
        // tab — [Tuning] Name ['as' Style]
        { "tab melody", ["as numbers", "as full"] },
        { "tab bass5 melody", ["as numbers", "as full"] },
        { "tab melody as numbers", [] },
        { "tab bass5 melody as full", [] },
        // chords — Name ['as' Mode]
        { "chords prog", ["as roman", "as names"] },
        { "chords prog as roman", [] },
        { "chords prog as names", [] },
        // lyrics — Name ['sings' Part]
        { "lyrics w", ["sings"] },
        { "lyrics w sings melody", [] },
        // the bare MIDI-only row — Name {'instrument' X | 'octave' N}: parse only (see the
        // remarks; its completion is an open gap and is not asserted here).
        { "bass", [] },
        { "bass instrument piano", [] },
        { "bass octave 1", [] },
        { "bass instrument piano octave 1", [] },
    };

    private static string[] LabelsAfter(string row)
    {
        string text = Book + "score main { staff melody  " + row + " ";
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new System.Uri("file:///score.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            { Uri = uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, text.Length);
        var list = server.Completion(new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, character),
        });
        return list?.Items.Select(i => i.Label!).ToArray() ?? System.Array.Empty<string>();
    }

    private static string[] Errors(string source)
    {
        var tree = SyntaxTree.Parse(source);
        return [.. tree.Diagnostics.Concat(SemanticValidation.Run(tree))
            .Where(d => d.Severity == LilySharp.Core.Syntax.DiagnosticSeverity.Error)
            .Select(d => d.Code + " " + d.Message)];
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void EverySpellingCompiles(string row, string[] _)
    {
        string book = Book + "score main { staff melody  " + row + " }\n";
        Assert.Empty(Errors(book));
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void EverySpellingKeepsTheRowsContinuations(string row, string[] stillOpen)
    {
        var labels = LabelsAfter(row);

        // A row may always be followed by another one, at every one of its lengths — the
        // invariant the word-count readers kept losing when a clause was written.
        Assert.Contains("staff", labels);

        // …and a clause still open offers what may follow inside it.
        foreach (string item in stillOpen)
            Assert.Contains(item, labels);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void AWordTheRowDoesNotTake_StartsANewRow(string row, string[] _)
    {
        // ★ THE TRIPWIRE (see the remarks). `label` is not a part, so reading it as its own
        // bare MIDI-only row is what LYS1007 reports. If a row's parser learns a new
        // optional clause that could swallow this word, the error stops coming and this test
        // names the row.
        string book = Book + "score main { staff melody  " + row + " label }\n";
        Assert.Contains(Errors(book),
            e => e.StartsWith("LYS1007", System.StringComparison.Ordinal) && e.Contains("'label'"));
    }
}
