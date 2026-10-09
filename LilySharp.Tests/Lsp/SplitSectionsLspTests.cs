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

using System.IO;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The editor's side of "Split Sections to Match a Part": the LYS2007 quick fix that offers it
/// (a command, not an edit — the plan is built and confirmed when it is picked), and the
/// lilysharp/splitSections request that answers with the plan, the part choices, or the reason.
/// </summary>
public class SplitSectionsLspTests
{
    private static readonly System.Uri Uri = new("file:///split.lys");

    private static LilySharpLanguageServer Open(string text)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = Uri, Text = text, LanguageId = "lilysharp", Version = 1 },
        });
        return server;
    }

    private static CodeAction[] ActionsOnTheWarning(LilySharpLanguageServer server, string text)
    {
        var warning = SemanticValidation.Run(SyntaxTree.Parse(text))
            .Single(d => d.Code == DiagnosticCodes.SectionBarCountMismatch);
        var (line, character) = LilySharpLanguageServer.GetLineAndCharacter(text, warning.Span.Start + 1);
        var at = new Position(line, character);
        return server.GetCodeActions(new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Range = new LilySharp.Lsp.Protocol.Range { Start = at, End = at },
        }) ?? [];
    }

    private const string OneWay = """
        part vn1 {
          section A { g'1 | }
          section B { a1 | }
        }
        part vn2 {
          section A { d'1 | e1 | }
        }
        form { A }
        score { staff vn1 staff vn2 }
        """;

    [Fact]
    public void TheWarning_OffersTheSplit_AsACommand_AndTheRequestAnswersWithThePlan()
    {
        var server = Open(OneWay);
        var action = Assert.Single(ActionsOnTheWarning(server, OneWay), a => a.Title.StartsWith("Split section A"));
        Assert.Equal("Split section A in the other parts to match vn1 (A 1 + B 1 bars)…", action.Title);
        Assert.Null(action.Edit);
        Assert.Equal("lilysharp.splitSectionsToMatch", action.Command!.CommandIdentifier);
        Assert.Equal([Uri.ToString(), "A"], action.Command.Arguments!.Select(a => a?.ToString()));

        var response = server.SplitSections(new SplitSectionsParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Section = "A",
        });
        Assert.True(response.Success, response.Error);
        Assert.StartsWith("Follow vn1: A 1 + B 1 bars.\nSplit A in vn2 after bar 1 → A, B.", response.Plan);
        Assert.Contains("section A { d'1 | }", response.NewText);
        Assert.Contains("form { A B }", response.NewText);
    }

    [Fact]
    public void PartsThatDisagree_ComeBackAsChoices_GroupedByHowTheySubdivide()
    {
        const string text = """
            part fl {
              section A { c'1 | }
              section B { d1 | e1 | }
            }
            part ob {
              section A { c'1 | }
              section B { d1 | e1 | }
            }
            part cl {
              section A { c'1 | d1 | }
              section X { e1 | }
            }
            part hn {
              section A { c'1 | d1 | e1 | }
            }
            form { A }
            score { staff fl staff ob staff cl staff hn }
            """;
        var server = Open(text);
        var response = server.SplitSections(new SplitSectionsParams { TextDocument = new TextDocumentIdentifier { Uri = Uri } });
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("A", response.Section);
        Assert.Equal(["fl, ob: A 1 + B 2 bars", "cl: A 2 + X 1 bars"],
            response.Choices!.Select(c => $"{c.Parts}: {c.Description}"));
        Assert.Equal("fl", response.Choices![0].Part);

        var followed = server.SplitSections(new SplitSectionsParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = Uri },
            Section = "A",
            Reference = "cl",
        });
        Assert.True(followed.Success, followed.Error);
        Assert.Contains("section A { c'1 | d1 | }\n  section X { e1 | }", followed.NewText!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ARefusal_ComesBackAsTheError()
    {
        const string text = """
            part vn1 {
              section A { g'1 | }
              section B { a1 | }
            }
            part vn2 {
              section A { d'2 d4 d8[ d8 | d8] d8 d4 d2 | }
            }
            form { A }
            score { staff vn1 staff vn2 }
            """;
        // A manual beam across the cut is refused (a tie there is now carried, and kept).
        var server = Open(text);
        var response = server.SplitSections(new SplitSectionsParams { TextDocument = new TextDocumentIdentifier { Uri = Uri } });
        Assert.False(response.Success);
        Assert.Null(response.NewText);
        Assert.Contains("a manual beam (opened line 6) runs across the cut", response.Error);
    }
}
