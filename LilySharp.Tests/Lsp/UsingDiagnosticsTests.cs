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
using System.IO;
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// What the Problems panel says about a piece split across files with
/// <c>using "..."</c>.
/// </summary>
/// <remarks>
/// It used to validate the UNEXPANDED tree while the preview, the exports and the
/// playback all expanded — so the same server drew the piece correctly and, beside it,
/// reported that the parts it drew were undefined. <c>LilySharpLanguageServer.DocumentDiagnostics</c>
/// is now the one house, and these tests hold both halves: the false errors are gone,
/// and LYS0028 (a <c>using</c> that resolves to nothing) can reach the panel at all.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class UsingDiagnosticsTests
{
    private const string Main =
        """
        using "parts.lys"

        time 4/4

        form main { ~A }

        score main { staff melody }
        """;

    private const string Parts =
        """
        part melody { clef treble }

        section A {
          melody { c4 d e f | }
        }
        """;

    private static IReadOnlyList<Diagnostic> Panel(string main, Dictionary<string, string> files)
        => LilySharpLanguageServer.DocumentDiagnostics(main, SyntaxTree.Parse(main),
            "C:/proj/main.lys",
            p => files.TryGetValue(Path.GetFileName(p), out var t) ? t : null);

    [Fact]
    public void TheUnexpandedTree_IsWhatUsedToBeValidated_AndItLies()
    {
        // The measurement behind the fix, kept as a test so the claim is not folklore:
        // run the validators the way the panel used to, and the CORRECT file above is
        // told its section and its part do not exist.
        var codes = SemanticValidation.Run(SyntaxTree.Parse(Main))
            .Select(d => d.Code).ToList();

        Assert.Contains(DiagnosticCodes.UndefinedSection, codes);
        Assert.Contains(DiagnosticCodes.UndefinedPart, codes);
    }

    [Fact]
    public void AResolvedInclude_LeavesThePanelClean()
    {
        Assert.Empty(Panel(Main, new() { ["parts.lys"] = Parts }));
    }

    [Fact]
    public void AMissingInclude_ReachesThePanel()
    {
        var diagnostics = Panel(Main, new());

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCodes.UsingFileUnreadable);
        // And it is a warning, so the preview beside it keeps drawing (the whole reason
        // the rule is a warning and not an error).
        Assert.Equal(DiagnosticSeverity.Warning,
            diagnostics.Single(d => d.Code == DiagnosticCodes.UsingFileUnreadable).Severity);
    }

    [Fact]
    public void ADiagnosticAboutTheIncludedFile_DoesNotSquiggleThisOne()
    {
        // An included file's text is appended AFTER the document's, so a diagnostic about
        // that file carries an offset past the end of this one and would squiggle nothing
        // (or the wrong thing). parts.lys here has five quarter notes in a 4/4 bar.
        //
        // ⚠️ The assertion carries its own positive control. A first version of this test
        // used a duplicate `part` declaration and passed with the filter REMOVED — there
        // was no out-of-range diagnostic to filter, so it was asserting about an empty set
        // (RULES §5.0 probe trap 21). The first Assert.Contains is what makes the second
        // one mean something.
        var overfull = Parts.Replace("c4 d e f |", "c4 d e f g |");
        var files = new Dictionary<string, string> { ["parts.lys"] = overfull };

        var (expanded, _) = LilySharpLanguageServer.ExpandUsings(
            Main, SyntaxTree.Parse(Main), "C:/proj/main.lys",
            p => files.TryGetValue(Path.GetFileName(p), out var t) ? t : null);

        var everything = SemanticValidation.Run(expanded);
        Assert.Contains(everything, d => d.Span.Start >= Main.Length);

        Assert.All(Panel(Main, files), d =>
            Assert.True(d.Span.Start < Main.Length,
                $"{d.Code} starts at {d.Span.Start}, past the document's {Main.Length} chars"));
    }

    [Fact]
    public void WithNoIncludes_TheExpansionIsTheIdentity()
    {
        // The overwhelmingly common path must be untouched: same diagnostics as running
        // the validators directly, in the same order.
        const string plain = "time 4/4\n\nscore main { staff nope }\n";
        var direct = SemanticValidation.Run(SyntaxTree.Parse(plain)).Select(d => d.Code);
        var panel = Panel(plain, new()).Select(d => d.Code);

        Assert.Equal(direct, panel);
    }
}
