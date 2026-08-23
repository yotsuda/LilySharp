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
using System.Linq;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// In a part-major file (parts nest their own sections) a top-level TRACK — lyrics or
/// chords — must mirror that shape: <c>lyrics v { section A { … } }</c>,
/// <c>chords prog { section A { … } }</c>. A flat top-level track is rejected; a
/// sectioned track, an inline block inside a part or section, and any section-major or
/// structureless file are left alone.
/// </summary>
/// <remarks>
/// The two kinds are asserted side by side on purpose. The lyrics half shipped alone
/// (LYS4002) and the chords half did not exist, so the identical shape was an error in
/// one track and silently accepted in the other — <c>chords prog { Dmaj7 | Em7 | Gmaj7 |
/// A7 }</c> beside a part-major part laid its bars over bar 0 onward and chorded only the
/// first pass of the first section (user report, session 240).
/// </remarks>
[Trait("Category", "Unit")]
public class TrackNeedsSectionsValidatorTests
{
    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var validator = new TrackNeedsSectionsValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }

    private static bool LyricsFlagged(string source)
        => Run(source).Any(d => d.Code == DiagnosticCodes.LyricTrackNeedsSections);

    private static bool ChordsFlagged(string source)
        => Run(source).Any(d => d.Code == DiagnosticCodes.ChordTrackNeedsSections);

    private const string PartMajorParts = """
        time 4/4
        key c major
        part melody { clef treble
          section A { c'4 d' e' f' | }
          section B { g'4 a' b' c'' | }
        }
        """;

    // ---- lyrics ----

    [Fact]
    public void PartMajor_FlatTopLevelLyricsTrack_Errors()
    {
        Assert.True(LyricsFlagged(PartMajorParts + """
            lyrics words { Do re mi fa | sol la ti do | }
            form main { A B }
            score main { staff melody  lyrics words }
            """));
    }

    [Fact]
    public void PartMajor_SectionedLyricsTrack_IsClean()
    {
        Assert.False(LyricsFlagged(PartMajorParts + """
            lyrics words { section A { Do re mi fa | } section B { sol la ti do | } }
            form main { A B }
            score main { staff melody  lyrics words }
            """));
    }

    [Fact]
    public void SectionMajor_InlineFlatLyrics_IsClean()
    {
        // Section-major (top-level `section` holds the parts): a flat lyrics block in a
        // section is the norm, not a top-level track — never flagged.
        Assert.False(LyricsFlagged("""
            time 4/4
            key c major
            part melody { clef treble }
            section A { melody { c'4 d' e' f' | } lyrics { Do re mi fa | } }
            form main { A }
            score main { staff melody }
            """));
    }

    [Fact]
    public void Structureless_FlatLyrics_IsClean()
    {
        // No parts-with-sections: layout is not part-major, so flat lyrics are fine.
        Assert.False(LyricsFlagged("time 4/4\n{ c4 d e f }\nlyrics { one two three four }\n"));
    }

    // ---- chords: the same four questions, which is the point ----

    [Fact]
    public void PartMajor_FlatTopLevelChordsTrack_Errors()
    {
        // The reported shape, reduced.
        Assert.True(ChordsFlagged(PartMajorParts + """
            chords prog { Dmaj7 | Em7 | Gmaj7 | A7 }
            form main { A B }
            score main { staff melody  chords prog }
            """));
    }

    [Fact]
    public void PartMajor_SectionedChordsTrack_IsClean()
    {
        Assert.False(ChordsFlagged(PartMajorParts + """
            chords prog { section A { Dmaj7 | Em7 | } section B { Gmaj7 | A7 | } }
            form main { A B }
            score main { staff melody  chords prog }
            """));
    }

    [Fact]
    public void PartMajor_ChordsInsideAStandaloneSection_IsClean()
    {
        // A part-major file may still write a section header holding the track's cell.
        // That block HAS its section — the ancestor test, not the file's layout, is what
        // exempts it.
        Assert.False(ChordsFlagged(PartMajorParts + """
            section A { chords prog { Dmaj7 | Em7 | } }
            form main { A B }
            score main { staff melody  chords prog }
            """));
    }

    [Fact]
    public void Structureless_FlatChords_IsClean()
    {
        Assert.False(ChordsFlagged("time 4/4\n{ c4 d e f }\nchords prog { C | F | }\n"));
    }

    [Fact]
    public void TheChordsMessageNamesTheTrack_SoTheFixCanBePasted()
    {
        var d = Run(PartMajorParts + """
            chords prog { Dmaj7 | Em7 | Gmaj7 | A7 }
            form main { A B }
            score main { staff melody  chords prog }
            """).Single(x => x.Code == DiagnosticCodes.ChordTrackNeedsSections);

        Assert.Contains("chords prog { section A { … } }", d.Message);
    }

    /// <summary>
    /// The tests above construct the validator directly, so every one of them would still
    /// pass if nothing ever RAN it. This goes through <c>SemanticValidation.Run</c> — the
    /// single list the CLI's <c>check</c> and the LSP's live diagnostics both read — so the
    /// registration is pinned too, not just the rule.
    /// </summary>
    [Fact]
    public void TheValidatorIsRegistered_SoCheckAndTheEditorBothReportIt()
    {
        var tree = SyntaxTree.Parse(PartMajorParts + """
            chords prog { Dmaj7 | Em7 | Gmaj7 | A7 }
            form main { A B }
            score main { staff melody  chords prog }
            """);

        Assert.Contains(SemanticValidation.Run(tree),
            d => d.Code == DiagnosticCodes.ChordTrackNeedsSections
              && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OneShapeOneRule_BothKindsAreFlaggedInTheSameFile()
    {
        // The reason the two halves share a validator: a file that writes both flat gets
        // both errors, and neither kind can quietly lose the rule the other keeps.
        var diags = Run(PartMajorParts + """
            lyrics words { Do re mi fa | sol la ti do | }
            chords prog { Dmaj7 | Em7 | Gmaj7 | A7 }
            form main { A B }
            score main { staff melody  lyrics words  chords prog }
            """);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.LyricTrackNeedsSections);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.ChordTrackNeedsSections);
    }
}
