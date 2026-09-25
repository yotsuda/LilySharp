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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Cases where the preview (IncrementalCompiler) and the full render (SvgGenerator.Generate)
/// disagreed, found in session 596 by comparing the two over random one-character edits of
/// the tracked books. Each is the input a cache key or a replayed value could not see.
/// </summary>
public class IncrementalFullAgreementTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static System.Collections.Immutable.ImmutableArray<MeasureContentKey> Keys(string src)
    {
        var tree = SyntaxTree.Parse(src);
        return MeasureContentKey.Compute(SvgGenerator.CollectScore(tree, RenderSpecParser.FindFirst(tree)));
    }

    private static string Fixture(params string[] parts)
        => File.ReadAllText(Path.Combine([CollectResumeTests.FindRepoRoot(), .. parts]));

    /// <summary>
    /// A nullable field that goes from 0 to null moves the key: <c>Nullable.GetHashCode</c>
    /// answers 0 for both, and the key folded that. `@finger(0)` → `@ffinger(0)` (an unknown
    /// annotation) took <c>Fingering</c> from 0 to null with every key unchanged, and the
    /// preview reused the whole layout with the fingering drawn (scriptstack1.lys).
    /// </summary>
    [Fact]
    public void AFingeringOfZeroAndNoFingering_AreDifferentKeys()
    {
        const string book = """
            octave absolute
            part melody { clef treble }
            section Main { melody { c'4@finger(0) d' e' f' | } }
            form main { Main }
            score main "x" { staff melody }
            """;
        Assert.NotEqual(Keys(book)[0], Keys(book.Replace("@finger(0)", "@ffinger(0)"))[0]);
    }

    /// <summary>
    /// A row with no bars is still part of the score's shape. The key folded each staff at
    /// the bars it HAS, so a lyrics row whose name matched nothing was in no key: removing
    /// it (`lyrics wwords` → `lyric wwords`) left every key standing, and the preview kept
    /// the row's height (lyrics.lys).
    /// </summary>
    [Fact]
    public void AnEmptyRowGoing_MovesTheKeys()
    {
        var src = Fixture("LilySharp.Tests", "Fixtures", "test", "lyrics.lys");
        // The score block's row only — the lyrics block keeps its name, so the row names nothing.
        Assert.Contains("staff melody  lyrics words", src);
        var withEmptyRow = src.Replace("staff melody  lyrics words", "staff melody  lyrics wwords");
        Assert.NotEqual(Keys(withEmptyRow)[0], Keys(withEmptyRow.Replace("lyrics wwords", "lyric wwords"))[0]);
    }

    /// <summary>
    /// A text-style pedal word's solved baseline rides in the per-system skyline memo, which
    /// serves its value under shifted text; named by the mark's source position, it was lost
    /// the moment an edit ABOVE moved the marks (a `title` mistyped), and the word fell to the
    /// legacy placement (pedal-text.lys). Named by its anchor now.
    /// </summary>
    [Fact]
    public void APedalWord_KeepsItsSolvedRow_AfterAnEditAboveIt()
    {
        var src = Fixture("LilySharp.Tests", "Fixtures", "test", "pedal-text.lys");
        Assert.Contains("title \"Pedal text\"", src);
        var session = new IncrementalCompiler(SyntaxTree.Parse(src), Opt);
        session.Render();
        var edited = src.Replace("title \"Pedal text\"", "ttle \"Pedal text\"");
        var incremental = session.RenderIncremental(SyntaxTree.Parse(edited)).Replace("\r\n", "\n");
        Assert.Equal(SvgGenerator.Generate(SyntaxTree.Parse(edited), Opt).Replace("\r\n", "\n"), incremental);
    }
}
