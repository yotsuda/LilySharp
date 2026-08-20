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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A <c>fonts { }</c> edit through the incremental session must render byte-identical
/// to a full recompile — the incremental==full invariant for the one edit whose
/// changed input (the text metrics) lives OUTSIDE every per-measure content key.
/// </summary>
/// <remarks>
/// ⚠️ WHY THIS NET EXISTS (session 224): <c>MeasureContentKey</c> deliberately folds the
/// resolved per-measure model and the score side-tables, and the session's global key
/// folds title/composer/tempo/swing — <c>MultiStaffScore.Fonts</c> is in neither. A face
/// change moves every glyph advance, so the spring gate correctly declines its skip, but
/// the per-system caches (<c>SystemLayoutCache</c>) are keyed on the content slice alone:
/// without a session-level guard they would serve measure layouts and skylines engraved
/// at the OLD face. The guard is in <c>IncrementalCompiler.Compile</c> (the font plan
/// joins the session state that resets the caches); this net is what proves it holds.
/// </remarks>
[Trait("Category", "Visual")]
public class FontEditIncrementalTests
{
    private static readonly SvgRenderOptions Opt = new() { EmbedFont = false };

    private static string Full(string text) =>
        SvgGenerator.Generate(SyntaxTree.Parse(text), Opt).Replace("\r\n", "\n");

    private static string Norm(string svg) => svg.Replace("\r\n", "\n");

    /// <summary>A sung book long enough to break into several systems, so an unsound
    /// reuse would have unchanged-content systems to serve stale.</summary>
    private static string With(string face) => $$"""
        time 4/4
        fonts { serif "{{face}}" }
        part v { }
        lyrics w { section Main { rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | rurururururu rurururururu rurururururu rurururururu | } }
        section Main {
          v { c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | c4 d e f | }
        }
        form main { ~Main }
        score main { staff ~v  lyrics w }
        """;

    /// <remarks>
    /// ⚠️ THE GEOMETRY TEETH OF THIS NET ARE THE WINDOWS LEG'S. "Arial" resolves to a
    /// metrically different face there, and before the guard existed this assertion
    /// caught the session serving the OLD face's geometry (a barline at x1=14.00 where
    /// the full recompile draws 12.56) with only the family attribute and data-pos
    /// re-derived live. On a machine where "Arial" falls back to the same face the two
    /// renders differ only in those live-derived strings, so the net still proves the
    /// guard path renders equal — it just cannot prove the guard was needed.
    /// ⚠️ Two face-swap traps this experiment stepped in before it bit, kept so the next
    /// probe does not re-learn them: byte-inequality of the two FULLS is NOT geometry
    /// (the family attribute and every later data-pos shift with the face name's
    /// length), and "C059" is not resolvable here (it falls back, metrics unchanged) —
    /// LP's C059 numbers live in its own bundled .otf, not on this machine.
    /// </remarks>
    [Fact]
    public void FontsFaceEdit_MatchesFullRecompile()
    {
        string schola = With("TeX Gyre Schola");
        string arial = With("Arial");

        // Reachability: the two spellings must at least render differently (trivially
        // true via the family attribute; on Windows the geometry differs too).
        Assert.NotEqual(Full(schola), Full(arial));

        var session = new IncrementalCompiler(SyntaxTree.Parse(schola), Opt);
        session.RenderIncremental(SyntaxTree.Parse(schola));

        Assert.Equal(Full(arial), Norm(session.RenderIncremental(SyntaxTree.Parse(arial))));

        // ...and back, so the guard refreshes the plan rather than merely discarding
        // caches once.
        Assert.Equal(Full(schola), Norm(session.RenderIncremental(SyntaxTree.Parse(schola))));
    }
}
