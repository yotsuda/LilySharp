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

[Trait("Category", "Unit")]
public class SymbolReferenceValidatorTests
{
    [Fact]
    public void Validate_UndefinedVariable_ReportsError()
    {
        var source = "$undefined";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedVariable, validator.Diagnostics[0].Code);
        Assert.Contains("undefined", validator.Diagnostics[0].Message);
    }
    
    [Fact]
    public void Validate_DefinedVariable_NoError()
    {
        var source = @"
phrase melody { c4 d e f | }
$melody
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_DefinedPhrase_NoError()
    {
        var source = @"
phrase intro { c4 d e f | }
$intro
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_UndefinedSectionInStructure_ReportsError()
    {
        var source = @"
section Intro { c4 d e f | }
form main {
    Intro
    NonExistent
}
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedSection, validator.Diagnostics[0].Code);
        Assert.Contains("NonExistent", validator.Diagnostics[0].Message);
    }
    
    /// <summary>
    /// A '~' hides the rehearsal LABEL. It must not hide a typo.
    /// </summary>
    /// <remarks>
    /// The validator matched only <c>SectionReferenceSyntax</c>, so <c>~NonExistent</c>
    /// passed `lysc check` clean and the section simply never appeared or played, while the
    /// same name without the '~' was reported. Found while fixing the sibling omission in
    /// the MIDI exporter (MidiSilentSectionTests) — a silent reference has to be answered
    /// everywhere a plain one is.
    /// </remarks>
    [Fact]
    public void Validate_UndefinedSilentSectionInStructure_ReportsError()
    {
        var source = @"
section Intro { c4 d e f | }
form main {
    Intro
    ~NonExistent
}
";
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));

        Assert.Single(validator.Diagnostics);
        Assert.Equal(DiagnosticCodes.UndefinedSection, validator.Diagnostics[0].Code);
        Assert.Contains("NonExistent", validator.Diagnostics[0].Message);
    }

    /// <summary>The other half: a DEFINED section behind a '~' stays clean.</summary>
    [Fact]
    public void Validate_DefinedSilentSectionInStructure_NoError()
    {
        var source = @"
section Intro { c4 d e f | }
form main {
    ~Intro
}
";
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));

        Assert.Empty(validator.Diagnostics);
    }

    [Fact]
    public void Validate_DefinedSectionInStructure_NoError()
    {
        var source = @"
section Intro { c4 d e f | }
section Verse { g4 a b c | }
form main {
    Intro
    Verse
}
";
        var tree = SyntaxTree.Parse(source);
        
        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);
        
        Assert.Empty(validator.Diagnostics);
    }
    
    [Fact]
    public void Validate_MultipleUndefinedReferences_ReportsAll()
    {
        var source = @"
form main {
    Section1
    Section2
}
$undefined1
$undefined2
";
        var tree = SyntaxTree.Parse(source);

        var validator = new SymbolReferenceValidator();
        validator.Validate(tree);

        Assert.Equal(4, validator.Diagnostics.Count);
    }

    private static IReadOnlyList<Diagnostic> Refs(string source)
    {
        var validator = new SymbolReferenceValidator();
        validator.Validate(SyntaxTree.Parse(source));
        return validator.Diagnostics;
    }

    [Fact]
    public void Validate_StaffNamesUndefinedPart_ReportsError()
    {
        // `staff melody2` names no part — a section-body block nor a header defines it.
        var diags = Refs("section A { melody { c d e f } }\n"
                       + "form main { A }\nscore main { staff melody\n staff melody2 }");
        var undef = diags.Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains("melody2", undef[0].Message);
    }

    [Theory]
    // A section-body part block DEFINES the part.
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { staff melody }")]
    // …as does a part header.
    [InlineData("part melody { clef treble section A { c d e f } }\nform main { A }\nscore main { staff melody }")]
    // A clef modifier before the part name is not the part.
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { staff bass melody }")]
    // `tab NAME as numbers | full` — the tab STYLE selector is not a part reference.
    // This reported LYS1007 "Undefined part: 'numbers'" on a valid score, so the
    // committed fixture test/tab-as-numbers.lys would not render through the CLI (the
    // snapshot path never runs this validator, which is why the suite stayed green).
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { tab melody as numbers }")]
    [InlineData("section A { melody { c d e f } }\nform main { A }\nscore main { tab melody as full }")]
    // Tuning override + style selector, and the chord-display selector after it.
    [InlineData("section A { melody { c d e f } }\nchords h { c1 }\nform main { A }\n"
              + "score main { tab bass melody as numbers with chords h as both }")]
    public void Validate_StaffNamesDefinedPart_NoUndefinedPartError(string source)
        => Assert.DoesNotContain(Refs(source), d => d.Code == DiagnosticCodes.UndefinedPart);

    [Fact]
    public void Validate_GrandStaffUndefinedInnerPart_ReportsError()
    {
        var diags = Refs("section A { rh { c1 } }\n"
                       + "form main { A }\nscore main { grandStaff { staff rh staff lh } }");
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.UndefinedPart && d.Message.Contains("lh"));
    }

    // ── row render targets: `chords NAME` / `lyrics NAME` ──

    /// <summary>
    /// A score's ROW targets were checked by nothing: a typo in <c>chords progg</c> passed
    /// `lysc check` clean and silently drew no row at all — the same silence
    /// <c>staff melody2</c> used to have.
    /// </summary>
    /// <remarks>
    /// This is a TIGHTENING, which is why it lands before 0.3.0: after a release, a spelling
    /// that is accepted today cannot be taken back. MEASURED over every .lys in the tree
    /// (897 books) before and after — see the commit message.
    /// </remarks>
    private const string RowSheet = """
        time 4/4
        section Main {
          chords prog { c1 | g1 | }
          lyrics words { la la | la la | }
        }
        form main { Main }
        score main "x" {
        """;

    [Theory]
    [InlineData("chords progg  lyrics words", "progg")]
    [InlineData("chords prog  lyrics wordz", "wordz")]
    public void Validate_RowNamesUndefinedTrack_ReportsError(string scoreBody, string bad)
    {
        var undef = Refs(RowSheet + scoreBody + " }")
            .Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains(bad, undef[0].Message);
    }

    [Fact]
    public void Validate_RowNamesDefinedTrack_NoError()
        => Assert.DoesNotContain(Refs(RowSheet + "chords prog  lyrics words }"),
            d => d.Code == DiagnosticCodes.UndefinedPart);

    /// <summary>
    /// And the two namespaces stay apart. A chord track is not a part a STAFF can render —
    /// folding the row names into the part set would have made this legal, and an empty
    /// staff is precisely what LYS1007 exists to catch.
    /// </summary>
    [Theory]
    [InlineData("staff prog", "prog")]     // a chord track is not a staff part…
    [InlineData("chords words", "words")]  // …and a lyric track is not a chord track
    [InlineData("lyrics prog", "prog")]    // …nor the other way round
    public void Validate_RowTracksAreNotStaffParts(string scoreBody, string bad)
    {
        var undef = Refs(RowSheet + scoreBody + " }")
            .Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains(bad, undef[0].Message);
    }

    /// <summary>
    /// An UNNAMED <c>chords { … }</c> block attaches to the staff written beside it rather
    /// than standing as a row, so it declares no name — and must not be read as declaring
    /// one. Slot 1 of an unnamed block holds the opening brace, which is what a naive
    /// reading would have collected.
    /// </summary>
    [Fact]
    public void Validate_UnnamedChordBlockDeclaresNoRowName()
    {
        var undef = Refs("""
            time 4/4
            section Main {
              m { c4 d e f | }
              chords { c1 | }
            }
            form main { Main }
            score main "x" { staff m  chords m }
            """).Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains("'m'", undef[0].Message);
    }

    // ── staff/tab ATTACHMENTS: `with chords NAME` / `with lyrics NAME` ──

    /// <summary>
    /// The attachment form names the same two track namespaces as a row, and had the same
    /// silence: `staff m with chords progg` passed `lysc check` clean and drew no chord line
    /// — MEASURED, the typo's SVG is byte-identical to the same score with the clause
    /// deleted, for all three spellings (`with chords`, `with lyrics`, `tab … with chords`).
    /// </summary>
    /// <remarks>
    /// A TIGHTENING, which is why it lands before 0.3.0: a spelling accepted at release
    /// cannot be taken back afterwards. Measured over every .lys in the tree before and
    /// after — see the commit message.
    /// </remarks>
    private const string AttachSheet = """
        time 4/4
        part m { clef treble }
        section Main {
          m { c4 d e f | }
          chords prog { c1 | }
          lyrics words { la la la la | }
        }
        form main { Main }
        score main "x" {
        """;

    [Theory]
    [InlineData("staff m with chords progg", "progg")]
    [InlineData("staff m with lyrics wordz", "wordz")]
    // The tab form spells the same attachment and was equally unchecked.
    [InlineData("tab m with chords progg", "progg")]
    // A stack of verses: the SECOND one is the typo, so a scan that stops at the first
    // `with` would pass this.
    [InlineData("staff m with lyrics words with lyrics wordz", "wordz")]
    // Any order, and past a chord-display selector — the `as both` must not be read as the
    // name of the clause after it.
    [InlineData("staff m with chords prog as both with lyrics wordz", "wordz")]
    // Inside a grand staff, whose staves are the same node kind.
    [InlineData("grandStaff { staff m with chords progg  staff m }", "progg")]
    public void Validate_AttachmentNamesUndefinedTrack_ReportsError(string scoreBody, string bad)
    {
        var undef = Refs(AttachSheet + scoreBody + " }")
            .Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains(bad, undef[0].Message);
    }

    [Theory]
    [InlineData("staff m with chords prog")]
    [InlineData("staff m with lyrics words")]
    [InlineData("tab m with chords prog")]
    [InlineData("staff m with chords prog as roman with lyrics words")]
    [InlineData("staff m with lyrics words with chords prog")]
    // A suppressed label and a per-score display name both sit BEFORE the first `with`, so
    // neither shifts the clause — the reason the raw token run can be read positionally.
    [InlineData("staff ~m with chords prog")]
    [InlineData("staff m \"Melody\" with lyrics words")]
    [InlineData("staff treble m with chords prog")]
    public void Validate_AttachmentNamesDefinedTrack_NoError(string scoreBody)
        => Assert.DoesNotContain(Refs(AttachSheet + scoreBody + " }"),
            d => d.Code == DiagnosticCodes.UndefinedPart);

    /// <summary>The two namespaces stay apart in the attachment form as well.</summary>
    [Theory]
    [InlineData("staff m with chords words", "words")]
    [InlineData("staff m with lyrics prog", "prog")]
    [InlineData("staff m with chords m", "m")]  // …and a staff part is not a chord track
    public void Validate_AttachedTracksKeepTheirNamespaces(string scoreBody, string bad)
    {
        var undef = Refs(AttachSheet + scoreBody + " }")
            .Where(d => d.Code == DiagnosticCodes.UndefinedPart).ToList();
        Assert.Single(undef);
        Assert.Contains(bad, undef[0].Message);
    }

    /// <summary>
    /// A `with chords` with NO name already reports "'with chords' needs a name" from the
    /// parser and attaches nothing. Adding "Undefined chords part: ''" under it would say
    /// less than the message already there, so the zero-width token is not a reference —
    /// the same rule the row targets follow.
    /// </summary>
    [Fact]
    public void Validate_AttachmentWithNoName_ReportsNoUndefinedTrack()
        => Assert.DoesNotContain(Refs(AttachSheet + "staff m with chords }"),
            d => d.Code == DiagnosticCodes.UndefinedPart);
}
