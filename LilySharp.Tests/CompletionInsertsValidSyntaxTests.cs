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
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LilySharp.Core.Syntax;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace LilySharp.Tests;

/// <summary>
/// The editor may not offer a spelling the parser refuses.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Written 2026-08-18 after the one-line <c>font "NAME"</c> was removed and the top-level
/// KEYWORD item went on inserting it. Completing <c>font</c> — the path a writer actually
/// takes — typed a diagnostic. The removal had fixed the three font contexts and missed the
/// fourth, which is the shape RULES §5.1 calls "report and keep are different repairs": four
/// consumers of one spelling, three updated, and nothing looking at the SET.
/// </para>
/// <para>
/// ⚠️ This is deliberately narrow. It does NOT claim every insertion parses — most items are
/// fragments (a pitch, a role key, a grob property) that are only valid in the context that
/// offered them, and judging those needs the context. It claims the one thing that can be
/// judged context-free: no insertion may raise a diagnostic that says a spelling was REMOVED
/// from the language. Those are exactly the codes that mean "the editor is out of date".
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class CompletionInsertsValidSyntaxTests(ITestOutputHelper output)
{
    /// <summary>The diagnostics that mean "this spelling used to exist". An editor that can
    /// provoke one of these is offering yesterday's language.</summary>
    /// <remarks>
    /// ⚠️ <c>LegacyDeclarationForm</c> (LYS0007) belongs to this family by NAME and not by
    /// behaviour, and including it made the net report four false positives on its first
    /// run: it fires on any bare <c>X = …</c>, which is what the override list legitimately
    /// inserts as a FRAGMENT (<c>NoteHead.color = </c> is only meaningful after the
    /// <c>override</c> keyword that offered it). A code that fires on incompleteness cannot
    /// tell a stale spelling from a fragment, so it is not in this set. The ones that are
    /// name a specific removed spelling and nothing else.
    /// <para>
    /// ⚠️ <c>FontOneLinerRemoved</c> (LYS8007) was in this set for one day and is now
    /// retired: <c>font</c> stopped being a keyword when the block was renamed to
    /// <c>fonts</c>, so nothing can raise it. The set shrank; the check did not.
    /// </para>
    /// </remarks>
    private static readonly string[] RemovedSpellingCodes =
    [
        DiagnosticCodes.RepeatVoltaRemoved,
        DiagnosticCodes.ParallelSyntaxRemoved,
    ];

    /// <summary>
    /// Every completion list the server can build without being told where the caret is —
    /// static, returning a <see cref="CompletionList"/>, every parameter optional.
    /// </summary>
    /// <remarks>
    /// ⚠️ The reach is PRINTED and asserted, because a reflection filter that silently
    /// matches nothing passes every assertion below it (RULES §5.4, the empty-set trap). The
    /// twenty lists that need a caret position are outside this net and are named by the
    /// count: a lower number than the last run means the filter stopped matching, not that
    /// the server shrank.
    /// </remarks>
    private static List<(string Name, CompletionList List)> ReachableLists()
    {
        var found = new List<(string, CompletionList)>();
        foreach (var m in typeof(LilySharpLanguageServer)
                     .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                     .Where(m => m.ReturnType == typeof(CompletionList))
                     .Where(m => m.GetParameters().All(p => p.HasDefaultValue))
                     .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var args = m.GetParameters().Select(p => p.DefaultValue).ToArray();
            if (m.Invoke(null, args) is CompletionList list)
                found.Add((m.Name, list));
        }
        return found;
    }

    /// <summary>An LSP snippet as the editor would leave it once the writer tabs through:
    /// <c>${1:face}</c> becomes <c>face</c>, <c>$0</c> becomes nothing.</summary>
    private static string Resolved(CompletionItem item)
    {
        string text = item.InsertText ?? item.Label ?? "";
        if (item.InsertTextFormat != InsertTextFormat.Snippet)
            return text;
        text = Regex.Replace(text, @"\$\{\d+:([^}]*)\}", "$1");   // ${1:face} -> face
        text = Regex.Replace(text, @"\$\{\d+\}|\$\d+", "");       // ${1} / $0 -> nothing
        return text;
    }

    [Fact]
    public void NoCompletionItem_InsertsASpellingTheParserHasRemoved()
    {
        var lists = ReachableLists();
        output.WriteLine($"lists reached: {lists.Count} (of the server's completion lists; "
                         + "those needing a caret position are outside this net)");
        Assert.True(lists.Count >= 30, $"only {lists.Count} lists reached — the filter stopped matching");

        var offenders = new List<string>();
        int checkedItems = 0;
        foreach (var (name, list) in lists)
        {
            foreach (var item in list.Items)
            {
                string text = Resolved(item);
                if (text.Length == 0) continue;
                checkedItems++;
                foreach (var d in SyntaxTree.Parse(text).Diagnostics)
                    if (RemovedSpellingCodes.Contains(d.Code))
                        offenders.Add($"{name} -> '{item.Label}' inserts \"{text.Replace("\n", "\\n")}\" ({d.Code}: {d.Message})");
            }
        }
        output.WriteLine($"items checked: {checkedItems}");
        Assert.True(checkedItems > 200, $"only {checkedItems} items checked");

        Assert.True(offenders.Count == 0,
            "the editor offers spellings the parser refuses:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheCheckCanFail()
    {
        // ★ The net was written after a fix, so it was green for free (RULES §5.4). It bit
        // on the `font` keyword item when that inserted the removed one-liner; `font` is not
        // a spelling at all any more, so the demonstration moves to a spelling that IS still
        // refused by name — the removed repeat-volta form.
        var wasWrong = new CompletionItem
        {
            Label = "repeat",
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = "repeat volta ${1:2} { $0 }",
        };
        Assert.Contains(SyntaxTree.Parse(Resolved(wasWrong)).Diagnostics,
            d => RemovedSpellingCodes.Contains(d.Code));

        // …and what the editor actually offers for the same keyword does not.
        var isRight = new CompletionItem
        {
            Label = "fonts",
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = "fonts {\n  serif \"${1:TeX Gyre Schola}\"$0\n}",
        };
        Assert.DoesNotContain(SyntaxTree.Parse(Resolved(isRight)).Diagnostics,
            d => RemovedSpellingCodes.Contains(d.Code));

        // The snippet resolver is not vacuous: it really does fill the placeholder.
        Assert.Contains("serif \"TeX Gyre Schola\"", Resolved(isRight), StringComparison.Ordinal);
    }
}
