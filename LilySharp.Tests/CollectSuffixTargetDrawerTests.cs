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

using System.Text;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The liveness half of the net for the parked suffix-target table (session 522): a
/// resumed collect rents the thread's one table and gives it back emptied, so after the
/// first keystroke on a thread no further table is built. The equality half — a resumed
/// render equals a fresh one — is every resume net there is (IncrementalCompilerTests,
/// CollectEditResumeTests); this one catches the failure they cannot see, a drawer that
/// silently stops parking and draws the same page at the old price (RULES §5.4, session 456).
/// </summary>
public sealed class CollectSuffixTargetDrawerTests
{
    [Fact]
    public void AfterTheFirstKeystroke_NoSuffixTargetTableIsBuiltAgain()
    {
        var sb = new StringBuilder("part bass { clef bass }\nsection A {\n");
        for (int bar = 0; bar < 24; bar++)
            sb.Append("  c4 d e f |\n");
        sb.Append("}\nscore main { staff bass }\n");
        string text = sb.ToString();
        var options = new LilySharp.Core.Svg.Renderer.SvgRenderOptions { EmbedFont = false };
        var compiler = new IncrementalCompiler(SyntaxTree.Parse(text), options);
        compiler.RenderIncrementalPages(SyntaxTree.Parse(text), System.Threading.CancellationToken.None);

        // Six forward keystrokes at the book's middle, each raising one written pitch — the
        // corpus harness's shape: a prefix to adopt and a suffix to re-walk past the window.
        int from = text.Length / 2;
        var after = new (int Served, int Fresh)[6];
        for (int k = 0; k < after.Length; k++)
        {
            int at = text.IndexOf("c4", from, System.StringComparison.Ordinal);
            Assert.True(at >= 0, "the book ran out of c4s");
            text = text.Remove(at, 2).Insert(at, "d4");
            from = at + 2;
            var pages = compiler.RenderIncrementalPages(SyntaxTree.Parse(text), System.Threading.CancellationToken.None);
            var fresh = new IncrementalCompiler(SyntaxTree.Parse(text), options)
                .RenderIncrementalPages(SyntaxTree.Parse(text), System.Threading.CancellationToken.None);
            Assert.Equal(fresh.Pages, pages.Pages); // the equality half, on this very book
            after[k] = MeasureCollector.SuffixTargetStats;
        }

        // Keystrokes 2..6 rent the table the first one parked: served grows, fresh does not.
        int servedLater = after[^1].Served - after[0].Served;
        int freshLater = after[^1].Fresh - after[0].Fresh;
        Assert.True(servedLater >= 3, $"only {servedLater} tables served from the drawer over five keystrokes");
        Assert.Equal(0, freshLater);
    }
}
