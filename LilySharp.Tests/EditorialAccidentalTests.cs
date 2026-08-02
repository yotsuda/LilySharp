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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Verifies the editorial (suggestion) accidental pipeline: @editorial turns
/// the note's resolved accidental into a small accidental ABOVE the note
/// (musica ficta) instead of a regular accidental at its left.
/// </summary>
/// <remarks>
/// LILYPOND-REF: scm/define-grobs.scm:96-123 AccidentalSuggestion —
/// (direction . UP), (font-size . -2), centered on the notehead.
/// </remarks>
[Trait("Category", "Unit")]
public class EditorialAccidentalTests
{
    private static Score Collect(string source)
        => new MeasureCollector().Collect(SyntaxTree.Parse(source));

    [Fact]
    public void Editorial_OnPlainNote_ForcesKeySignatureNatural()
    {
        // C in C major prints no accidental — the suggestion shows natural.
        var score = Collect("c4@editorial d e f |");
        var note = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);

        Assert.Equal("natural", note.EditorialAccidental);
        Assert.True(note.IsEditorial);
        Assert.Null(note.Accidental); // suggestion replaces the left accidental
    }

    [Fact]
    public void Editorial_OnSharpedNote_MovesSharpAboveAndSuppressesLeft()
    {
        var score = Collect("fis4@editorial g a b |");
        var note = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);

        Assert.Equal("sharp", note.EditorialAccidental);
        Assert.Null(note.Accidental);
    }

    [Fact]
    public void Editorial_CreatesAboveArticulationOfMatchingKind()
    {
        var score = Collect("fis4@editorial g a b |");

        var editorial = Assert.Single(score.Articulations,
            a => a.IsEditorialAccidental);
        Assert.Equal(ArticulationType.EditorialSharp, editorial.Type);
        Assert.True(editorial.IsAbove);
        Assert.Equal(0, editorial.MeasureIndex);
        Assert.Equal(0, editorial.ItemIndex);
    }

    [Fact]
    public void PlainNote_WithoutEditorial_HasNoEditorialState()
    {
        var score = Collect("fis4 g a b |");
        var note = Assert.IsType<NoteItem>(score.Voice.Measures[0].Items[0]);

        Assert.False(note.IsEditorial);
        Assert.Equal("sharp", note.Accidental); // normal left accidental kept
        Assert.DoesNotContain(score.Articulations, a => a.IsEditorialAccidental);
    }

    [Fact]
    public void EditorialTypeMapping_RoundTrips()
    {
        foreach (var kind in new[] { "sharp", "flat", "natural", "doubleSharp", "doubleFlat" })
        {
            var type = ArticulationItem.EditorialTypeFor(kind);
            Assert.Equal(kind, ArticulationItem.AccidentalKindFor(type));
        }
    }

    [Fact]
    public void EditorialLayout_IsAboveTheNote_AtReducedScale()
    {
        var score = Collect("c4@editorial d e f |");
        var layout = new LayoutEngine(new LayoutOptions()).Layout(score);

        var editorial = Assert.Single(layout.ArticulationLayouts,
            a => a.FontSizeStep < 0.0);
        // The grob STATES a font-size and the size follows from it — magstep(-2) =
        // 2^(-2/6) = 0.79370053.
        // LILYPOND-REF: scm/define-grobs.scm:101 accidental-suggestion-interface's grob
        //   declares (font-size . -2) there (AccidentalSuggestion runs :96-123);
        //   scm/lily-library.scm magstep is the 2^(s/6) a font-size means.
        Assert.Equal(-2.0, editorial.FontSizeStep);
        Assert.Equal(0.79370053, EmmentalerDesignSize.Magstep(editorial.FontSizeStep), 8);
        // …and that font-size chooses the 16 design: the glyph is not the 20's shrunk.
        Assert.Equal(16, EmmentalerDesignSize.ForFontSizeStep(editorial.FontSizeStep).Rounded);
        Assert.True(editorial.IsAbove);
        // c (C4? — relative anchor) sits below the middle line; the suggestion
        // must end up above the staff top minus padding, i.e. y < noteY.
        var measure = layout.Systems[0].Measures[0];
        // Y-up (frame B): above the middle line means a positive value.
        Assert.True(editorial.YUp > 0.0,
            $"Editorial accidental should sit above the note (YUp={editorial.YUp:F2})");
    }
}
